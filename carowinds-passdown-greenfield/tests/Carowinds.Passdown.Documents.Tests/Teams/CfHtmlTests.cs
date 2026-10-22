using System.Text;
using Carowinds.Passdown.Documents.Teams;

namespace Carowinds.Passdown.Documents.Tests.Teams;

/// <summary>
/// Phase 29: the Windows "HTML Format" (CF_HTML) payload — header fields, ten-digit byte offsets into the
/// UTF-8 bytes, fragment markers, no BOM — validated independently of <see cref="CfHtml.Parse"/> too.
/// </summary>
public sealed class CfHtmlTests
{
    [Fact]
    public void Header_HasVersionAndTenDigitOffsets_InTheDocumentedOrder()
    {
        var payload = CfHtml.Build("<p>Hi</p>");
        var text = Encoding.UTF8.GetString(payload);

        Assert.Matches(
            "^Version:0\\.9\\r\\nStartHTML:\\d{10}\\r\\nEndHTML:\\d{10}\\r\\nStartFragment:\\d{10}\\r\\nEndFragment:\\d{10}\\r\\n<html>",
            text);
        Assert.Equal(CfHtml.WindowsFormatName, "HTML Format");
    }

    [Theory]
    [InlineData("<p>Hi</p>")]
    [InlineData("<p>Café — ¡Olé! 🎢 你好 &amp; ✓</p>")]
    [InlineData("")]
    public void Offsets_AreByteOffsets_AndDelimitTheExactFragment(string fragment)
    {
        var payload = CfHtml.Build(fragment);
        var header = Encoding.ASCII.GetString(payload, 0, 105);

        int Read(string name)
        {
            var start = header.IndexOf(name + ":", StringComparison.Ordinal) + name.Length + 1;
            return int.Parse(header.Substring(start, 10));
        }

        var startHtml = Read("StartHTML");
        var endHtml = Read("EndHTML");
        var startFragment = Read("StartFragment");
        var endFragment = Read("EndFragment");

        Assert.Equal(105, startHtml); // the fixed-length header
        Assert.Equal(payload.Length, endHtml);
        Assert.Equal(fragment, Encoding.UTF8.GetString(payload, startFragment, endFragment - startFragment));
        Assert.Equal(CfHtml.StartFragmentMarker, Encoding.UTF8.GetString(payload, startFragment - CfHtml.StartFragmentMarker.Length, CfHtml.StartFragmentMarker.Length));
        Assert.Equal(CfHtml.EndFragmentMarker, Encoding.UTF8.GetString(payload, endFragment, CfHtml.EndFragmentMarker.Length));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(payload, startHtml, endHtml - startHtml), StringComparison.Ordinal);
        Assert.EndsWith("</html>", Encoding.UTF8.GetString(payload), StringComparison.Ordinal);

        var parsed = CfHtml.Parse(payload);
        Assert.Equal("0.9", parsed.Version);
        Assert.Equal(fragment, parsed.Fragment);
    }

    [Fact]
    public void MultiByteCharacters_ShiftOffsetsByBytesNotCharacters()
    {
        var ascii = CfHtml.Parse(CfHtml.Build("<p>aaaa</p>"));
        var emoji = CfHtml.Parse(CfHtml.Build("<p>🎢🎢</p>")); // 4 UTF-16 chars, 8 UTF-8 bytes

        Assert.Equal(ascii.StartFragment, emoji.StartFragment);
        Assert.Equal(ascii.EndFragment + 4, emoji.EndFragment);
    }

    [Fact]
    public void Payload_HasNoByteOrderMark_AndNoNul()
    {
        var payload = CfHtml.Build("<p>x</p>");

        Assert.NotEqual(0xEF, payload[0]);
        Assert.DoesNotContain((byte)0, payload);
    }

    [Fact]
    public void LargeMessage_StaysConsistent()
    {
        var fragment = string.Concat(Enumerable.Range(0, 5000).Select(i => $"<p>Line {i} — Café 🎢</p>"));
        var parsed = CfHtml.Parse(CfHtml.Build(fragment));

        Assert.Equal(fragment, parsed.Fragment);
    }

    [Fact]
    public void Parse_RejectsBrokenOffsets()
    {
        var payload = CfHtml.Build("<p>x</p>");
        var tampered = Encoding.UTF8.GetString(payload).Replace("EndHTML:0000000", "EndHTML:0000009", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => CfHtml.Parse(Encoding.UTF8.GetBytes(tampered)));
    }

    [Fact]
    public void RenderedMessage_RoundTripsThroughCfHtml()
    {
        var message = new TeamsMessage(new TeamsMessageSection("r", "Reminders:", TeamsUserText.ToBlocks("- <b>not bold</b>\n- Café 🎢")));
        var html = TeamsHtmlRenderer.Render(message);

        Assert.Equal(html, CfHtml.Parse(CfHtml.Build(html)).Fragment);
    }
}
