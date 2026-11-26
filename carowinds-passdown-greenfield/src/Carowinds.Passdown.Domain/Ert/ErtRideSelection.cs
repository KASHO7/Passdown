using System.Globalization;
using System.Text;

namespace Carowinds.Passdown.Domain.Ert;

/// <summary>
/// Phase 31B: order-independent meaning of an Early Ride Time ride selection. The rides an associate
/// lists keep their own order for display; equality (Personal Sync conflict detection) compares the
/// <em>set</em> of rides by a canonical ride key, so "Copperhead Strike, Mountain Gliders" and "Mountain
/// Gliders, Copperhead Strike" are the same selection while "Copperhead Strike" and "Fury 325" are not.
/// <para>
/// No official stable ride id is published for attractions (rides are free text, Phase 17B), so the
/// canonical key is the ride name after only meaning-preserving normalization: surrounding and repeated
/// whitespace, letter case, Unicode compatibility forms and typographic apostrophes/dashes/quotes. A ride
/// is folded into another name only through <see cref="VerifiedAliases"/>, which lists nothing that has not
/// been verified — two different names are never assumed to be the same ride.
/// </para>
/// </summary>
public static class ErtRideSelection
{
    /// <summary>
    /// Verified alternate spellings → canonical ride name. Intentionally empty until an alias is confirmed
    /// against official Carowinds ride naming; normalization alone handles case, spacing and punctuation forms.
    /// </summary>
    public static IReadOnlyDictionary<string, string> VerifiedAliases { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The canonical comparison key of one ride name; empty for a blank name.</summary>
    public static string RideKey(string? ride)
    {
        if (string.IsNullOrWhiteSpace(ride))
        {
            return string.Empty;
        }

        var normalized = ride.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            builder.Append(c switch
            {
                '‘' or '’' or 'ʼ' or '`' or '´' => '\'',
                '“' or '”' => '"',
                '‐' or '‑' or '‒' or '–' or '—' or '−' => '-',
                _ when char.IsWhiteSpace(c) => ' ',
                _ => c,
            });
        }

        var key = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLower(CultureInfo.InvariantCulture);

        return VerifiedAliases.TryGetValue(key, out var canonical) ? RideKey(canonical) : key;
    }

    /// <summary>The selection's distinct ride keys, sorted ordinally — the order-independent meaning.</summary>
    public static IReadOnlyList<string> CanonicalSet(IEnumerable<string>? rides) =>
        (rides ?? [])
            .Select(RideKey)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>True when both lists name exactly the same rides, ignoring order, duplicates, case and spacing.</summary>
    public static bool SameSelection(IEnumerable<string>? first, IEnumerable<string>? second) =>
        CanonicalSet(first).SequenceEqual(CanonicalSet(second), StringComparer.Ordinal);

    /// <summary>
    /// Union of two selections for an editable merge: every ride of <paramref name="preferred"/> in its
    /// order, then the rides only <paramref name="other"/> has, in its order. Each ride appears once
    /// (by canonical key), keeping the spelling of the first list that names it.
    /// </summary>
    public static IReadOnlyList<string> Merge(IEnumerable<string>? preferred, IEnumerable<string>? other) =>
        Distinct((preferred ?? []).Concat(other ?? []));

    /// <summary>Trims, drops blanks and removes duplicates by canonical key, preserving the user's order and first spelling.</summary>
    public static IReadOnlyList<string> Distinct(IEnumerable<string>? rides)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var ride in rides ?? [])
        {
            var key = RideKey(ride);
            if (key.Length > 0 && seen.Add(key))
            {
                result.Add(ride.Trim());
            }
        }

        return result;
    }
}
