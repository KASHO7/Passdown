using System.Net;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Documents.Teams;

namespace Carowinds.Passdown.Documents.Tests.Teams;

/// <summary>
/// Phase 29: <see cref="TeamsPlainTextRenderer"/> and <see cref="TeamsHtmlRenderer"/> render the same
/// <see cref="TeamsMessage"/>; the HTML uses only the constrained element set and encodes every user string.
/// </summary>
public sealed partial class TeamsRenderingTests
{
    private static TeamsMessage Sample() => new(
        new TeamsMessageSection("greeting", null, [new TeamsParagraph(TeamsLine.Of("Good Morning!"))]),
        new TeamsMessageSection("ops", "Today's Operations:", [new TeamsParagraph(TeamsLine.Labeled("Park Hours", "10:00 AM - 8:00 PM"), TeamsLine.Labeled("Projected Attendance", "18,000"))]),
        new TeamsMessageSection("reminders", "Reminders:", TeamsUserText.ToBlocks("- Hydrate\n- Smile\n\nSee https://www.carowinds.com/tickets for details.")),
        new TeamsMessageSection("pricing", "Ticket Prices:", [new TeamsParagraph(new TeamsLine(TeamsRun.Strong("Daily Admission")), TeamsLine.Of("Park Ticket: $59.00"))], TitleIsSeparateBlock: true));

    /// <summary>Very small HTML-to-text reduction for the consistency check: block ends become line breaks, tags go, entities decode.</summary>
    internal static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, "<br>|</p>|</li>|<ul>", "\n");
        text = Regex.Replace(text, "<li>", "• ");
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text).Replace('\u00A0', ' ');
    }

    internal static IReadOnlyList<string> Words(string text) =>
        Regex.Split(text, @"\s+").Where(word => word.Length > 0).ToList();

    [Fact]
    public void PlainText_HasBlankLineBetweenSections_TitleOnFirstLine_SeparateTitleBlockForPrices()
    {
        Assert.Equal(
            "Good Morning!\r\n\r\n"
            + "Today's Operations:\r\nPark Hours: 10:00 AM - 8:00 PM\r\nProjected Attendance: 18,000\r\n\r\n"
            + "Reminders:\r\n• Hydrate\r\n• Smile\r\n\r\nSee https://www.carowinds.com/tickets for details.\r\n\r\n"
            + "Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00",
            TeamsPlainTextRenderer.Render(Sample()));
    }

    [Fact]
    public void Html_UsesBoldHeadingsLabelsBulletsParagraphsAndSafeLink()
    {
        var html = TeamsHtmlRenderer.Render(Sample());

        Assert.Equal(
            "<p>Good Morning!</p>"
            + "<p><b style=\"font-weight:bold\">Today&#39;s Operations:</b><br><b style=\"font-weight:bold\">Park Hours:</b> 10:00 AM - 8:00 PM<br><b style=\"font-weight:bold\">Projected Attendance:</b> 18,000</p>"
            + "<p><b style=\"font-weight:bold\">Reminders:</b></p><ul><li>Hydrate</li><li>Smile</li></ul>"
            + "<p>See <a href=\"https://www.carowinds.com/tickets\">https://www.carowinds.com/tickets</a> for details.</p>"
            + "<p><b style=\"font-weight:bold\">Ticket Prices:</b></p><p><b style=\"font-weight:bold\">Daily Admission</b><br>Park Ticket: $59.00</p>",
            html);
    }

    [Fact]
    public void Html_OnlyUsesTheConstrainedElementSet()
    {
        var html = TeamsHtmlRenderer.Render(Sample());
        var tags = TagPattern().Matches(html).Select(match => match.Groups[1].Value.ToLowerInvariant()).ToHashSet();

        Assert.Subset(new HashSet<string> { "p", "br", "b", "i", "ul", "li", "a" }, tags);

        // Phase 29B: the only style attributes are the two fixed emphasis values; no style sheets.
        var styles = StylePattern().Matches(html).Select(match => match.Value).ToHashSet();
        Assert.Subset(new HashSet<string> { "style=\"font-weight:bold\"", "style=\"font-style:italic\"" }, styles);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormattedAndPlain_CarryTheSameWordsInTheSameOrder()
    {
        var message = Sample();
        Assert.Equal(Words(TeamsPlainTextRenderer.Render(message)), Words(HtmlToText(TeamsHtmlRenderer.Render(message))));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("\"><a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<style>body{display:none}</style>")]
    [InlineData("&lt;b&gt;already-encoded&lt;/b&gt;")]
    public void UserText_IsEncoded_NeverBecomesMarkup(string hostile)
    {
        var message = new TeamsMessage(new TeamsMessageSection("r", "Reminders:", TeamsUserText.ToBlocks(hostile)));
        var html = TeamsHtmlRenderer.Render(message);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);

        // The words still arrive, as text.
        Assert.Equal(hostile, HtmlToText(html).Split('\n')[1]);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("file:///C:/Windows/system32")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://example.com/x")]
    public void UnsafeLinks_RenderAsInertText(string address)
    {
        var uri = new Uri(address, UriKind.RelativeOrAbsolute);
        Assert.False(TeamsLinkPolicy.IsSafe(uri));

        var message = new TeamsMessage(new TeamsMessageSection("x", null, [new TeamsParagraph(new TeamsLine(new TeamsRun("Click", Link: uri)))]));
        Assert.Equal("<p>Click</p>", TeamsHtmlRenderer.Render(message));
        Assert.Equal("Click", TeamsPlainTextRenderer.Render(message));
    }

    [Fact]
    public void SafeLink_WithDifferentText_PlainTextShowsAddress()
    {
        var uri = new Uri("https://www.carowinds.com/tickets");
        var message = new TeamsMessage(new TeamsMessageSection("x", null, [new TeamsParagraph(new TeamsLine(new TeamsRun("Tickets", Link: uri)))]));

        Assert.Equal("<p><a href=\"https://www.carowinds.com/tickets\">Tickets</a></p>", TeamsHtmlRenderer.Render(message));
        Assert.Equal("Tickets (https://www.carowinds.com/tickets)", TeamsPlainTextRenderer.Render(message));
    }

    [Fact]
    public void UserText_LinkDetection_OnlyHttpAndHttps_TrailingPunctuationExcluded()
    {
        var line = TeamsUserText.ToLine("Read https://example.com/a?b=1&c=2. Not javascript:alert(1) or www.example.com");

        var link = Assert.Single(line.Runs, run => run.Link is not null);
        Assert.Equal("https://example.com/a?b=1&c=2", link.Text);
        Assert.Equal("Read https://example.com/a?b=1&c=2. Not javascript:alert(1) or www.example.com", line.PlainText);
        Assert.Contains("href=\"https://example.com/a?b=1&amp;c=2\"", TeamsHtmlRenderer.Render(new TeamsMessage(new TeamsMessageSection("x", null, [new TeamsParagraph(line)]))));
    }

    [Fact]
    public void Encode_PreservesSpacingTabsAndUnicode_DropsControlCharacters()
    {
        Assert.Equal("&nbsp;&nbsp;Kai &nbsp;&nbsp;&nbsp;10:30", TeamsHtmlRenderer.Encode("  Kai    10:30"));
        Assert.Equal("A&nbsp;&nbsp;&nbsp;&nbsp;B", TeamsHtmlRenderer.Encode("A\tB"));
        Assert.Equal("Café — ¡Olé! 🎢 你好", TeamsHtmlRenderer.Encode("Café — ¡Olé! 🎢 你好"));
        Assert.Equal("ab", TeamsHtmlRenderer.Encode("a\u0000\u0007b"));
        Assert.Equal("1<br>2", TeamsHtmlRenderer.Encode("1\r\n2"));
    }

    [Fact]
    public void UserText_BlankLinesStartParagraphs_DashLinesBecomeBullets_EveryLineOption()
    {
        var blocks = TeamsUserText.ToBlocks("First line\nSecond line\n\n- one\n* two\n• three\n\nLast");
        Assert.Collection(blocks,
            block => Assert.Equal(["First line", "Second line"], Assert.IsType<TeamsParagraph>(block).Lines.Select(l => l.PlainText)),
            block => Assert.Equal(["one", "two", "three"], Assert.IsType<TeamsBulletList>(block).Items.Select(l => l.PlainText)),
            block => Assert.Equal(["Last"], Assert.IsType<TeamsParagraph>(block).Lines.Select(l => l.PlainText)));

        var bullets = Assert.Single(TeamsUserText.ToBlocks("Hydrate\n- Smile", everyLineIsBullet: true));
        Assert.Equal(["Hydrate", "Smile"], Assert.IsType<TeamsBulletList>(bullets).Items.Select(l => l.PlainText));

        var literal = Assert.Single(TeamsUserText.ToBlocks("Quote\n- Author", detectBullets: false));
        Assert.Equal(["Quote", "- Author"], Assert.IsType<TeamsParagraph>(literal).Lines.Select(l => l.PlainText));
    }

    [Fact]
    public void Mentions_StayPlainText()
    {
        var message = new TeamsMessage(new TeamsMessageSection("r", null, TeamsUserText.ToBlocks("@Jordan please cover rides")));

        Assert.Equal("<p>@Jordan please cover rides</p>", TeamsHtmlRenderer.Render(message));
        Assert.DoesNotContain("<at", TeamsHtmlRenderer.Render(message), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptySections_AreSkipped_NoOrphanHeadings()
    {
        var message = new TeamsMessage(
            new TeamsMessageSection("a", "Reminders:", []),
            new TeamsMessageSection("b", "Tentative Break Plan:", TeamsUserText.ToBlocks("   \n  ")),
            new TeamsMessageSection("c", null, [new TeamsParagraph(TeamsLine.Of("Hi"))]));

        Assert.Equal("Hi", TeamsPlainTextRenderer.Render(message));
        Assert.Equal("<p>Hi</p>", TeamsHtmlRenderer.Render(message));
    }

    [GeneratedRegex("<\\s*/?\\s*([a-zA-Z]+)")]
    private static partial Regex TagPattern();

    [GeneratedRegex("style\\s*=\\s*\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex StylePattern();
}
