namespace Carowinds.Passdown.Documents.Teams;

/// <summary>
/// Phase 29: turns text the user typed (reminders, break plan, DAP/placement, OD notes) into message
/// blocks without losing their intent: line breaks are kept, a blank line starts a new paragraph, lines
/// beginning with "- ", "* " or "• " become bullets, leading spaces are kept, and http/https addresses
/// become links when <see cref="TeamsLinkPolicy"/> accepts them. Never adds or reorders content and never
/// turns "@Name" into a Teams mention — it stays ordinary text.
/// </summary>
public static class TeamsUserText
{
    private static readonly string[] BulletMarkers = ["- ", "* ", "• "];

    /// <param name="detectBullets">False for text whose leading "- " is content, not a list (a Daily Feature quote attribution, a greeting).</param>
    public static IReadOnlyList<TeamsBlock> ToBlocks(string? text, bool everyLineIsBullet = false, bool detectBullets = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var blocks = new List<TeamsBlock>();
        var paragraph = new List<TeamsLine>();
        var bullets = new List<TeamsLine>();

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new TeamsParagraph(paragraph.ToList()));
                paragraph.Clear();
            }
        }

        void FlushBullets()
        {
            if (bullets.Count > 0)
            {
                blocks.Add(new TeamsBulletList(bullets.ToList()));
                bullets.Clear();
            }
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                FlushParagraph();
                FlushBullets();
                continue;
            }

            string? item = null;
            if ((detectBullets && TryStripBullet(line, out item)) || everyLineIsBullet)
            {
                FlushParagraph();
                bullets.Add(ToLine(everyLineIsBullet && item is null ? line.TrimStart() : item!));
            }
            else
            {
                FlushBullets();
                paragraph.Add(ToLine(line));
            }
        }

        FlushParagraph();
        FlushBullets();
        return blocks;
    }

    /// <summary>One line as runs: ordinary text, with each safe http/https address as a link run (its text is the address itself).</summary>
    public static TeamsLine ToLine(string line)
    {
        var runs = new List<TeamsRun>();
        var pending = new System.Text.StringBuilder();
        var index = 0;

        while (index < line.Length)
        {
            if (IsTokenStart(line, index) && TryReadLink(line, index, out var length, out var uri))
            {
                if (pending.Length > 0)
                {
                    runs.Add(TeamsRun.Plain(pending.ToString()));
                    pending.Clear();
                }

                runs.Add(new TeamsRun(line.Substring(index, length), Link: uri));
                index += length;
                continue;
            }

            pending.Append(line[index]);
            index++;
        }

        if (pending.Length > 0 || runs.Count == 0)
        {
            runs.Add(TeamsRun.Plain(pending.ToString()));
        }

        return new TeamsLine(runs);
    }

    private static bool TryStripBullet(string line, out string? item)
    {
        var trimmed = line.TrimStart();
        foreach (var marker in BulletMarkers)
        {
            if (trimmed.StartsWith(marker, StringComparison.Ordinal) && trimmed.Length > marker.Length)
            {
                item = trimmed[marker.Length..].TrimStart();
                return true;
            }
        }

        item = null;
        return false;
    }

    private static bool IsTokenStart(string line, int index) => index == 0 || char.IsWhiteSpace(line[index - 1]) || line[index - 1] is '(' or '<' or '[';

    private static bool TryReadLink(string line, int index, out int length, out Uri? uri)
    {
        length = 0;
        uri = null;
        if (!line.AsSpan(index).StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !line.AsSpan(index).StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var end = index;
        while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] is not ('<' or '>' or '"'))
        {
            end++;
        }

        // Trailing sentence punctuation belongs to the sentence, not the address.
        while (end > index && line[end - 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '\'')
        {
            end--;
        }

        if (Uri.TryCreate(line[index..end], UriKind.Absolute, out var candidate) && TeamsLinkPolicy.IsSafe(candidate))
        {
            length = end - index;
            uri = candidate;
            return true;
        }

        return false;
    }
}
