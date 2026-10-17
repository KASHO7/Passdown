namespace Carowinds.Passdown.Application.SpellCheck;

/// <summary>
/// Phase 20A: the shipped allow-list of Carowinds operational vocabulary that a general English
/// dictionary would wrongly flag. Deliberately small and maintainable — park/event brands, common
/// operations shorthand, and ride names that already appear in this app's own data (ERT configuration
/// and fixtures). Ordinary English words ("Fast", "Lane", "Pass") need no entry. All-caps acronyms
/// (ERT, DAP, VIP) are already skipped by <see cref="SpellCheckTokenizer"/>; they are listed anyway so a
/// "Dap"/"Ert" capitalization is not flagged either.
///
/// Extending it: add a term here (shipped code — never Personal Sync), or pass extra terms to
/// <see cref="SpellCheckService"/>'s constructor. Matching is case-insensitive and ignores a trailing
/// possessive ("Carowinds'", "Carowinds's").
/// </summary>
public static class DomainSpellingTerms
{
    public static IReadOnlyList<string> Seed { get; } =
    [
        // Park, events and brand
        "Carowinds", "SCarowinds", "WinterFest", "Passdown", "accesso", "FunPix", "Snoopy",
        // Operations shorthand
        "ERT", "DAP", "passholder", "passholders", "walkie", "walkies",
        // Event/attraction names present in app data
        "Conjuring",
        // Ride names present in app data
        "Copperhead", "Intimidator", "Afterburn", "Nighthawk",
    ];

    public static HashSet<string> CreateAllowList(IEnumerable<string>? additionalTerms = null)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in Seed.Concat(additionalTerms ?? []))
        {
            // Multi-word terms ("Fast Lane") contribute each word.
            foreach (var word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                allowed.Add(Normalize(word));
            }
        }

        return allowed;
    }

    /// <summary>Strips a trailing possessive and normalizes the curly apostrophe.</summary>
    public static string Normalize(string word)
    {
        var normalized = word.Replace('’', '\'');
        if (normalized.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
        {
            return normalized[..^2];
        }

        return normalized.EndsWith('\'') ? normalized[..^1] : normalized;
    }
}
