using System.Text;

namespace Carowinds.Passdown.Documents.Teams;

/// <summary>
/// Phase 29: which links may become clickable in a formatted copy. Only absolute http/https addresses with
/// a host and no embedded user name/password; everything else (javascript:, data:, file:, mailto:, relative
/// paths) is rendered as inert text.
/// </summary>
public static class TeamsLinkPolicy
{
    public static bool IsSafe(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);
}

/// <summary>
/// Phase 29: renders a <see cref="TeamsMessage"/> as a constrained HTML fragment for the clipboard. The
/// only elements ever produced are <c>p</c>, <c>br</c>, <c>b</c>, <c>i</c>, <c>ul</c>, <c>li</c> and
/// <c>a href</c> (safe links only) — no scripts, style sheets, classes, images, tables, fonts or colors.
/// Every piece of text, including user-entered notes, is HTML-encoded; nothing a user types can become markup.
/// <para>
/// Phase 29B: bold and italic are written as <c>&lt;b style="font-weight:bold"&gt;</c> and
/// <c>&lt;i style="font-style:italic"&gt;</c> — direct formatting, the form Word, Outlook and browsers put
/// on the clipboard themselves — instead of unstyled semantic <c>strong</c>/<c>em</c>, which Windows 11
/// validation showed pasting without visible bold in both Word and Teams desktop (bullets survived).
/// Word maps <c>strong</c> to its "Strong" character style rather than direct bold. Those two fixed
/// attribute values are the only <c>style</c> attributes ever written; if a sanitizer strips them, the
/// <c>b</c>/<c>i</c> element still carries the emphasis.
/// </para>
/// </summary>
public static class TeamsHtmlRenderer
{
    public const string BoldOpen = "<b style=\"font-weight:bold\">";

    public const string BoldClose = "</b>";

    public const string ItalicOpen = "<i style=\"font-style:italic\">";

    public const string ItalicClose = "</i>";

    public static string Render(TeamsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var html = new StringBuilder();
        foreach (var section in message.Sections.Where(section => !section.IsEmpty))
        {
            RenderSection(html, section);
        }

        return html.ToString();
    }

    private static void RenderSection(StringBuilder html, TeamsMessageSection section)
    {
        var blocks = section.Blocks;
        var hasTitle = !string.IsNullOrWhiteSpace(section.Title);

        if (hasTitle && (section.TitleIsSeparateBlock || blocks[0] is not TeamsParagraph))
        {
            html.Append("<p>").Append(BoldOpen).Append(Encode(section.Title!)).Append(BoldClose).Append("</p>");
            hasTitle = false;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            switch (blocks[i])
            {
                case TeamsParagraph paragraph:
                    html.Append("<p>");
                    if (i == 0 && hasTitle)
                    {
                        html.Append(BoldOpen).Append(Encode(section.Title!)).Append(BoldClose);
                        if (paragraph.Lines.Count > 0)
                        {
                            html.Append("<br>");
                        }
                    }

                    for (var line = 0; line < paragraph.Lines.Count; line++)
                    {
                        if (line > 0)
                        {
                            html.Append("<br>");
                        }

                        RenderLine(html, paragraph.Lines[line]);
                    }

                    html.Append("</p>");
                    break;
                case TeamsBulletList list:
                    html.Append("<ul>");
                    foreach (var item in list.Items)
                    {
                        html.Append("<li>");
                        RenderLine(html, item);
                        html.Append("</li>");
                    }

                    html.Append("</ul>");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section), blocks[i].GetType().Name, null);
            }
        }
    }

    private static void RenderLine(StringBuilder html, TeamsLine line)
    {
        var previousWasSpace = true; // spacing is tracked across runs, so "Label:" + " value" keeps a normal space
        foreach (var run in line.Runs)
        {
            var text = Encode(run.Text, ref previousWasSpace);
            if (run.Italic)
            {
                text = ItalicOpen + text + ItalicClose;
            }

            if (run.Bold)
            {
                text = BoldOpen + text + BoldClose;
            }

            if (run.Link is { } link && TeamsLinkPolicy.IsSafe(link))
            {
                text = "<a href=\"" + EncodeAttribute(link.AbsoluteUri) + "\">" + text + "</a>";
            }

            html.Append(text);
        }
    }

    /// <summary>
    /// HTML-encodes text for element content: the five markup characters become entities, control
    /// characters are dropped, embedded line endings become &lt;br&gt;, a tab becomes four non-breaking
    /// spaces, and runs of spaces (and a leading space) keep their width with non-breaking spaces — so a
    /// break plan's column spacing survives HTML whitespace collapsing. Other Unicode is kept as-is
    /// (the clipboard payload is UTF-8).
    /// </summary>
    public static string Encode(string? text)
    {
        var previousWasSpace = true; // a leading space would otherwise collapse
        return Encode(text, ref previousWasSpace);
    }

    private static string Encode(string? text, ref bool previousWasSpace)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var html = new StringBuilder(normalized.Length + 16);

        foreach (var character in normalized)
        {
            switch (character)
            {
                case '&': html.Append("&amp;"); break;
                case '<': html.Append("&lt;"); break;
                case '>': html.Append("&gt;"); break;
                case '"': html.Append("&quot;"); break;
                case '\'': html.Append("&#39;"); break;
                case '\n': html.Append("<br>"); previousWasSpace = true; continue;
                case '\t': html.Append("&nbsp;&nbsp;&nbsp;&nbsp;"); previousWasSpace = true; continue;
                case ' ':
                    html.Append(previousWasSpace ? "&nbsp;" : " ");
                    previousWasSpace = true;
                    continue;
                default:
                    if (char.IsControl(character))
                    {
                        continue;
                    }

                    html.Append(character);
                    break;
            }

            previousWasSpace = false;
        }

        return html.ToString();
    }

    /// <summary>Encodes an attribute value (always double-quoted by this renderer).</summary>
    public static string EncodeAttribute(string value)
    {
        var html = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '&': html.Append("&amp;"); break;
                case '<': html.Append("&lt;"); break;
                case '>': html.Append("&gt;"); break;
                case '"': html.Append("&quot;"); break;
                case '\'': html.Append("&#39;"); break;
                default:
                    if (!char.IsControl(character))
                    {
                        html.Append(character);
                    }

                    break;
            }
        }

        return html.ToString();
    }
}
