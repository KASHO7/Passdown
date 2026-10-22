namespace Carowinds.Passdown.Documents.Teams;

/// <summary>
/// Phase 29: one run of text with the only styling a Teams paste needs — bold, italic, and a link.
/// <see cref="Link"/> is honored only when <see cref="TeamsLinkPolicy.IsSafe"/> accepts it; otherwise the
/// run renders as its text alone.
/// </summary>
public sealed record TeamsRun(string Text, bool Bold = false, bool Italic = false, Uri? Link = null)
{
    public static TeamsRun Plain(string text) => new(text);

    public static TeamsRun Strong(string text) => new(text, Bold: true);

    public static TeamsRun Emphasis(string text) => new(text, Italic: true);
}

/// <summary>Phase 29: one line of runs. Lines inside a block are separated by a line break.</summary>
public sealed record TeamsLine(IReadOnlyList<TeamsRun> Runs)
{
    public TeamsLine(params TeamsRun[] runs) : this((IReadOnlyList<TeamsRun>)runs)
    {
    }

    public static TeamsLine Of(string text) => new(TeamsRun.Plain(text));

    /// <summary>"Label: value" with the label bold in formatted output.</summary>
    public static TeamsLine Labeled(string label, string value) => new(TeamsRun.Strong(label + ":"), TeamsRun.Plain(" " + value));

    public string PlainText => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>Phase 29: a block of content. Blocks are separated by a blank line (plain) or paragraph spacing (formatted).</summary>
public abstract record TeamsBlock;

/// <summary>Lines kept together with single line breaks.</summary>
public sealed record TeamsParagraph(IReadOnlyList<TeamsLine> Lines) : TeamsBlock
{
    public TeamsParagraph(params TeamsLine[] lines) : this((IReadOnlyList<TeamsLine>)lines)
    {
    }
}

/// <summary>A bulleted list: "• item" lines in plain text, a &lt;ul&gt; in formatted output.</summary>
public sealed record TeamsBulletList(IReadOnlyList<TeamsLine> Items) : TeamsBlock;

/// <summary>
/// Phase 29: one titled section of a Teams message. <see cref="Title"/> is bold in formatted output; in
/// plain text it is the first line. When <see cref="TitleIsSeparateBlock"/> is true the title stands
/// alone with a blank line after it (the Phase 18F "Ticket Prices:" layout); otherwise the first block
/// follows it directly. <see cref="Notices"/> are preview-only warnings (missing or stale information)
/// and are never rendered into the clipboard.
/// </summary>
public sealed record TeamsMessageSection(
    string Key,
    string? Title,
    IReadOnlyList<TeamsBlock> Blocks,
    bool TitleIsSeparateBlock = false)
{
    public IReadOnlyList<string> Notices { get; init; } = [];

    /// <summary>True when there is nothing to copy — the renderers skip an empty section entirely (no orphan heading).</summary>
    public bool IsEmpty => Blocks.Count == 0;
}

/// <summary>
/// Phase 29: the one message representation both clipboard formats are rendered from
/// (<see cref="TeamsPlainTextRenderer"/>, <see cref="TeamsHtmlRenderer"/>), so the plain and formatted
/// copies always carry the same words in the same order.
/// </summary>
public sealed record TeamsMessage(IReadOnlyList<TeamsMessageSection> Sections)
{
    public static TeamsMessage Empty { get; } = new([]);

    public TeamsMessage(params TeamsMessageSection[] sections) : this((IReadOnlyList<TeamsMessageSection>)sections)
    {
    }

    public bool IsEmpty => Sections.All(section => section.IsEmpty);
}
