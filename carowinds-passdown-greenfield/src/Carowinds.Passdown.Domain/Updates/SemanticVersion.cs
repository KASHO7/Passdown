using System.Globalization;

namespace Carowinds.Passdown.Domain.Updates;

/// <summary>
/// Phase 23D: a Semantic Versioning 2.0.0 version (<c>MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]</c>) with the
/// specification's precedence rules, so update decisions never depend on alphabetical string order
/// ("0.1.0-rc.10" is newer than "0.1.0-rc.9"; "0.1.0" is newer than every "0.1.0-rc.N").
/// Build metadata is kept for display but ignored for precedence and equality, as SemVer requires.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    /// <summary>Longest accepted input; version strings come from untrusted remote metadata.</summary>
    public const int MaximumLength = 128;

    private SemanticVersion(int major, int minor, int patch, IReadOnlyList<string> prerelease, string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PrereleaseIdentifiers = prerelease;
        BuildMetadata = buildMetadata;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>Dot-separated prerelease identifiers, e.g. ["rc", "7"]; empty for a stable release.</summary>
    public IReadOnlyList<string> PrereleaseIdentifiers { get; }

    public string? BuildMetadata { get; }

    public bool IsPrerelease => PrereleaseIdentifiers.Count > 0;

    /// <summary>The version without build metadata — the form used in tags, asset names and comparisons.</summary>
    public string Normalized => IsPrerelease
        ? $"{Major}.{Minor}.{Patch}-{string.Join('.', PrereleaseIdentifiers)}"
        : $"{Major}.{Minor}.{Patch}";

    public static SemanticVersion Parse(string value) =>
        TryParse(value, out var version) ? version : throw new FormatException("Not a semantic version.");

    /// <summary>Strict parse: no leading "v", no whitespace, no leading zeros in numeric parts, no empty identifiers.</summary>
    public static bool TryParse(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        string? build = null;
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            build = value[(plus + 1)..];
            value = value[..plus];
            if (!AreValidIdentifiers(build.Split('.'), numericMustNotHaveLeadingZero: false))
            {
                return false;
            }
        }

        IReadOnlyList<string> prerelease = [];
        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            var identifiers = value[(dash + 1)..].Split('.');
            if (!AreValidIdentifiers(identifiers, numericMustNotHaveLeadingZero: true))
            {
                return false;
            }

            prerelease = identifiers;
            value = value[..dash];
        }

        var core = value.Split('.');
        if (core.Length != 3
            || !TryParseNumeric(core[0], out var major)
            || !TryParseNumeric(core[1], out var minor)
            || !TryParseNumeric(core[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease, build);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        // A prerelease has lower precedence than the associated normal version.
        if (!IsPrerelease || !other.IsPrerelease)
        {
            return (IsPrerelease, other.IsPrerelease) switch
            {
                (true, false) => -1,
                (false, true) => 1,
                _ => 0,
            };
        }

        var count = Math.Min(PrereleaseIdentifiers.Count, other.PrereleaseIdentifiers.Count);
        for (var i = 0; i < count; i++)
        {
            result = CompareIdentifiers(PrereleaseIdentifiers[i], other.PrereleaseIdentifiers[i]);
            if (result != 0)
            {
                return result;
            }
        }

        // A larger set of prerelease fields has higher precedence when all preceding ones are equal.
        return PrereleaseIdentifiers.Count.CompareTo(other.PrereleaseIdentifiers.Count);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Normalized);

    public override string ToString() => BuildMetadata is null ? Normalized : $"{Normalized}+{BuildMetadata}";

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    private static int CompareIdentifiers(string left, string right)
    {
        var leftNumeric = IsNumeric(left);
        var rightNumeric = IsNumeric(right);
        if (leftNumeric && rightNumeric)
        {
            // Lengths first so arbitrarily long numeric identifiers never overflow.
            var byLength = left.Length.CompareTo(right.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
        }

        // Numeric identifiers always have lower precedence than alphanumeric identifiers.
        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return Math.Sign(string.CompareOrdinal(left, right));
    }

    private static bool TryParseNumeric(string text, out int value)
    {
        value = 0;
        return IsNumeric(text)
            && (text.Length == 1 || text[0] != '0')
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool AreValidIdentifiers(string[] identifiers, bool numericMustNotHaveLeadingZero)
    {
        foreach (var identifier in identifiers)
        {
            if (identifier.Length == 0 || !identifier.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-'))
            {
                return false;
            }

            if (numericMustNotHaveLeadingZero && IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumeric(string text) => text.Length > 0 && text.All(c => c is >= '0' and <= '9');
}
