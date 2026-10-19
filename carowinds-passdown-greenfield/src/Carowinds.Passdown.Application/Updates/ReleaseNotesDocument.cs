using System.Text;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Application.Updates;

public enum ReleaseNotesBlockKind
{
    Heading,
    Paragraph,
    Bullet,
}

public sealed record ReleaseNotesBlock(ReleaseNotesBlockKind Kind, string Text)
{
    public bool IsHeading => Kind == ReleaseNotesBlockKind.Heading;

    public bool IsParagraph => Kind == ReleaseNotesBlockKind.Paragraph;

    public bool IsBullet => Kind == ReleaseNotesBlockKind.Bullet;
}

/// <summary>
/// Phase 23D: release notes as plain text blocks. Supports the small, safe Markdown subset release notes
/// need — "#"/"##"/"###" headings, "-"/"*" bullets, blank-line paragraphs — and renders everything else as
/// literal text. Nothing is ever interpreted as HTML, script, image or link: link syntax keeps only its
/// label, inline markers (** _ `) are dropped, HTML tags are removed, and control characters are stripped.
/// Bounded in size, so hostile input cannot produce an unbounded document.
/// </summary>
public sealed partial class ReleaseNotesDocument
{
    public const int MaximumSourceCharacters = 20_000;

    public const int MaximumBlocks = 400;

    private ReleaseNotesDocument(IReadOnlyList<ReleaseNotesBlock> blocks, bool truncated)
    {
        Blocks = blocks;
        IsTruncated = truncated;
    }

    public IReadOnlyList<ReleaseNotesBlock> Blocks { get; }

    public bool IsTruncated { get; }

    public bool IsEmpty => Blocks.Count == 0;

    public static ReleaseNotesDocument Empty { get; } = new([], false);

    public static ReleaseNotesDocument Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Empty;
        }

        var truncated = source.Length > MaximumSourceCharacters;
        if (truncated)
        {
            source = source[..MaximumSourceCharacters];
        }

        var blocks = new List<ReleaseNotesBlock>();
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                Add(ReleaseNotesBlockKind.Paragraph, paragraph.ToString());
                paragraph.Clear();
            }
        }

        void Add(ReleaseNotesBlockKind kind, string text)
        {
            var clean = CleanInline(text);
            if (clean.Length == 0)
            {
                return;
            }

            if (blocks.Count >= MaximumBlocks)
            {
                truncated = true;
                return;
            }

            blocks.Add(new ReleaseNotesBlock(kind, clean));
        }

        foreach (var rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                Add(ReleaseNotesBlockKind.Heading, heading.Groups[1].Value);
                continue;
            }

            var bullet = BulletPattern().Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                Add(ReleaseNotesBlockKind.Bullet, bullet.Groups[1].Value);
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(line);
        }

        FlushParagraph();
        return new ReleaseNotesDocument(blocks, truncated);
    }

    /// <summary>A short plain-text summary (first few blocks) for the Settings card.</summary>
    public string Summarize(int maximumBlocks = 4)
    {
        var lines = Blocks.Where(block => !block.IsHeading).Take(maximumBlocks).Select(block => block.IsBullet ? $"• {block.Text}" : block.Text);
        return string.Join(Environment.NewLine, lines);
    }

    private static string CleanInline(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        var result = builder.ToString();
        result = ImagePattern().Replace(result, "$1");
        result = LinkPattern().Replace(result, "$1");
        result = HtmlTagPattern().Replace(result, string.Empty);
        result = result.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        return WhitespacePattern().Replace(result, " ").Trim();
    }

    [GeneratedRegex(@"^#{1,3}\s+(.+?)\s*#*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^[-*]\s+(.+)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

/// <summary>Phase 23D: the release notes shown in What's New and before installing.</summary>
public sealed record ReleaseNotesContent(string Version, string Title, DateOnly? PublishedOn, ReleaseNotesDocument Document);

/// <summary>Phase 23D: the installed version's own notes, bundled inside the application so they work offline.</summary>
public interface IBundledReleaseNotesSource
{
    /// <summary>Null when this build carries no notes for its own version.</summary>
    ReleaseNotesContent? LoadCurrent();
}
