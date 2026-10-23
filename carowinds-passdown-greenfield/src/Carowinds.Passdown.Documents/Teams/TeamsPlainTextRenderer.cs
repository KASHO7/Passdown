namespace Carowinds.Passdown.Documents.Teams;

/// <summary>
/// Phase 29: renders a <see cref="TeamsMessage"/> as Teams-friendly plain text — CRLF line breaks, a blank
/// line between blocks and between sections, "• " bullets, no Markdown. A link whose text differs from its
/// address is written "text (address)" so the plain copy carries the same information as the formatted one.
/// This is also the exact Phase 18F output for the existing Copy actions (guarded by
/// AssociateTeamsTextFormatterTests).
/// </summary>
public static class TeamsPlainTextRenderer
{
    public const string LineBreak = "\r\n";

    public const string Bullet = "• ";

    public static string Render(TeamsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return string.Join(LineBreak + LineBreak, message.Sections.Where(section => !section.IsEmpty).Select(RenderSection));
    }

    public static string RenderSection(TeamsMessageSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var blocks = section.Blocks.Select(RenderBlock).ToList();
        if (!string.IsNullOrWhiteSpace(section.Title))
        {
            var title = Normalize(section.Title);
            if (section.TitleIsSeparateBlock || blocks.Count == 0)
            {
                blocks.Insert(0, title);
            }
            else
            {
                blocks[0] = title + LineBreak + blocks[0];
            }
        }

        return string.Join(LineBreak + LineBreak, blocks);
    }

    private static string RenderBlock(TeamsBlock block) => block switch
    {
        TeamsParagraph paragraph => string.Join(LineBreak, paragraph.Lines.Select(RenderLine)),
        TeamsBulletList list => string.Join(LineBreak, list.Items.Select(item => Bullet + RenderLine(item))),
        _ => throw new ArgumentOutOfRangeException(nameof(block), block.GetType().Name, null),
    };

    private static string RenderLine(TeamsLine line) => string.Concat(line.Runs.Select(RenderRun));

    /// <summary>One run's words as the plain copy writes them (also the Phase 29B preview's text, so the two never drift).</summary>
    internal static string RenderRun(TeamsRun run)
    {
        var text = Normalize(run.Text);
        if (run.Link is { } link && TeamsLinkPolicy.IsSafe(link))
        {
            var address = link.AbsoluteUri;
            if (!string.Equals(text, address, StringComparison.Ordinal) && !string.Equals(text, link.OriginalString, StringComparison.Ordinal))
            {
                return $"{text} ({address})";
            }
        }

        return text;
    }

    /// <summary>Any embedded line ending becomes CRLF, matching every other line break in the copy.</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", LineBreak, StringComparison.Ordinal);
}
