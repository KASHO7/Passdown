using System.Globalization;
using System.Text;

namespace Carowinds.Passdown.Domain.Shows;

/// <summary>
/// Phase 21C: the stable identity of one official Carowinds show, independent of display order,
/// capitalization, or harmless whitespace changes. Precedence follows the project's source-stability
/// practice (authoritative id first):
/// <list type="number">
/// <item>The official FIMS location id (<c>extLocationID</c>, e.g. <c>SHOW-030-00122</c>) — the same id the
/// official show pages carry as <c>fimsId</c>.</item>
/// <item>The feed's numeric show id (<c>showId</c>), prefixed <c>fims-show:</c>.</item>
/// <item>A normalized name + venue key, prefixed <c>name:</c> — so two distinct shows that share a name
/// at different venues never merge.</item>
/// </list>
/// </summary>
public static class ShowIdentity
{
    public static string Resolve(string? officialLocationId, int? numericShowId, string name, string? venue)
    {
        if (!string.IsNullOrWhiteSpace(officialLocationId))
        {
            return officialLocationId.Trim().ToUpperInvariant();
        }

        if (numericShowId is { } id and > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"fims-show:{id}");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"name:{NormalizeText(name)}|{NormalizeText(venue)}";
    }

    /// <summary>Lowercase, trimmed, internal whitespace collapsed — used for the fallback key only, never for display.</summary>
    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>A display value with surrounding/duplicate whitespace removed (the feed sometimes ends venues with a space), or null when blank.</summary>
    public static string? CleanDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
