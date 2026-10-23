using Carowinds.Passdown.Application.MorningMessage;

namespace Carowinds.Passdown.Documents.Teams;

/// <summary>Phase 29B: one styled piece of the composer preview. <see cref="Text"/> may contain "\n" line breaks.</summary>
public sealed record TeamsPreviewSpan(string Text, bool Bold = false, bool Italic = false);

/// <summary>
/// Phase 29B: renders a <see cref="TeamsMessage"/> for the composer's on-screen preview as styled spans —
/// no HTML, nothing remote, nothing executable. The layout is the plain-text copy's (a blank line between
/// blocks and sections, "• " bullets), so the words and line breaks on screen are exactly what
/// <see cref="TeamsPlainTextRenderer"/> copies, with "\n" in place of CRLF.
/// <para>
/// <see cref="TeamsCopyMode.PlainText"/> yields unstyled spans, as plain text pastes.
/// <see cref="TeamsCopyMode.Formatted"/> adds the same bold and italic the formatted copy carries — section
/// headings, "Label:" names and emphasized notes — so the preview shows what Copy Formatted will paste.
/// </para>
/// </summary>
public static class TeamsPreviewRenderer
{
    public static IReadOnlyList<TeamsPreviewSpan> Render(TeamsMessage message, TeamsCopyMode mode)
    {
        ArgumentNullException.ThrowIfNull(message);

        var styled = mode == TeamsCopyMode.Formatted;
        var spans = new List<TeamsPreviewSpan>();
        var firstSection = true;
        foreach (var section in message.Sections.Where(section => !section.IsEmpty))
        {
            if (!firstSection)
            {
                Add(spans, "\n\n");
            }

            firstSection = false;
            RenderSection(spans, section, styled);
        }

        return spans;
    }

    private static void RenderSection(List<TeamsPreviewSpan> spans, TeamsMessageSection section, bool styled)
    {
        var hasTitle = !string.IsNullOrWhiteSpace(section.Title);
        if (hasTitle)
        {
            Add(spans, Normalize(section.Title!), bold: styled);
            Add(spans, section.TitleIsSeparateBlock ? "\n\n" : "\n");
        }

        for (var i = 0; i < section.Blocks.Count; i++)
        {
            if (i > 0)
            {
                Add(spans, "\n\n");
            }

            switch (section.Blocks[i])
            {
                case TeamsParagraph paragraph:
                    RenderLines(spans, paragraph.Lines, bullet: false, styled);
                    break;
                case TeamsBulletList list:
                    RenderLines(spans, list.Items, bullet: true, styled);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section), section.Blocks[i].GetType().Name, null);
            }
        }
    }

    private static void RenderLines(List<TeamsPreviewSpan> spans, IReadOnlyList<TeamsLine> lines, bool bullet, bool styled)
    {
        for (var line = 0; line < lines.Count; line++)
        {
            if (line > 0)
            {
                Add(spans, "\n");
            }

            if (bullet)
            {
                Add(spans, TeamsPlainTextRenderer.Bullet);
            }

            foreach (var run in lines[line].Runs)
            {
                Add(spans, Normalize(TeamsPlainTextRenderer.RenderRun(run)), bold: styled && run.Bold, italic: styled && run.Italic);
            }
        }
    }

    /// <summary>Adds a span, merging it into the previous one when the styling matches, so the preview stays a short list.</summary>
    private static void Add(List<TeamsPreviewSpan> spans, string text, bool bold = false, bool italic = false)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (spans.Count > 0 && spans[^1] is { } last && last.Bold == bold && last.Italic == italic)
        {
            spans[^1] = last with { Text = last.Text + text };
            return;
        }

        spans.Add(new TeamsPreviewSpan(text, bold, italic));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
