using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: the build's update settings (appsettings.json "Updates" section, embedded in the app).
/// The release source (which GitHub repository) is configuration, kept apart from version comparison,
/// signature verification and installation. Installation is possible only when at least one trusted
/// public key is configured; without one the app never contacts the release source at all.
/// </summary>
public sealed record UpdateConfiguration(
    UpdateRepository? Repository,
    UpdateChannel Channel,
    IReadOnlyList<TrustedUpdateKey> TrustedKeys)
{
    public static UpdateConfiguration Disabled { get; } = new(null, UpdateChannel.ReleaseCandidate, []);

    /// <summary>True when there is a release source and something to verify its releases with.</summary>
    public bool IsConfigured => Repository is not null && TrustedKeys.Count > 0;
}

/// <summary>Phase 23D: a GitHub "owner/name" pair, validated to GitHub's own character rules.</summary>
public sealed record UpdateRepository
{
    private UpdateRepository(string owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    public string Owner { get; }

    public string Name { get; }

    public override string ToString() => $"{Owner}/{Name}";

    /// <summary>Accepts exactly "owner/name": owner 1–39 of [A-Za-z0-9-] not starting/ending with '-', name 1–100 of [A-Za-z0-9._-] and not "." or "..".</summary>
    public static bool TryParse(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UpdateRepository? repository)
    {
        repository = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        var (owner, name) = (parts[0], parts[1]);
        var ownerValid = owner.Length is >= 1 and <= 39
            && owner.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && owner[0] != '-' && owner[^1] != '-';
        var nameValid = name.Length is >= 1 and <= 100
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            && name is not "." and not "..";

        if (!ownerValid || !nameValid)
        {
            return false;
        }

        repository = new UpdateRepository(owner, name);
        return true;
    }
}

/// <summary>Phase 23D: one public verification key (ECDSA P-256, SubjectPublicKeyInfo, Base64) and its identifier.</summary>
public sealed record TrustedUpdateKey(string KeyId, string PublicKeySpkiBase64);
