using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: the signed description of one approved release — the only source of truth the updater
/// trusts. It is produced by the release signing tool, signed with the release private key, and accepted
/// only after <see cref="IUpdateManifestVerifier"/> has checked that signature against a public key bundled
/// in the installed build. Everything here is therefore authenticated; nothing in GitHub's own release
/// metadata (title, body, download URLs, the "prerelease" flag) is.
/// </summary>
public sealed record UpdateManifest(
    int FormatVersion,
    string ProductId,
    SemanticVersion Version,
    UpdateChannel Channel,
    string RuntimeIdentifier,
    DateTimeOffset? PublishedAtUtc,
    string Title,
    string ReleaseNotes,
    UpdatePackageDescriptor Package,
    IReadOnlyList<UpdatePackageFile> Files)
{
    /// <summary>The only manifest format this build understands.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Product identity bound into every signed manifest; a manifest for another product is rejected.</summary>
    public const string CarowindsOperationsProductId = "carowinds-operations";

    /// <summary>The only architecture this application is published for.</summary>
    public const string WindowsX64RuntimeIdentifier = "win-x64";

    /// <summary>Sum of the extracted files' sizes — the space the new version needs in the installation folder.</summary>
    public long TotalFileBytes => Files.Sum(file => file.Size);
}

/// <summary>Phase 23D: the release ZIP as the signed manifest describes it.</summary>
/// <param name="FileName">The exact asset name, e.g. "Carowinds-Operations-0.1.0-rc.8-win-x64.zip".</param>
/// <param name="Size">Exact byte length; a download that is shorter or longer is rejected.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the whole ZIP.</param>
/// <param name="RootFolder">The single top-level folder every ZIP entry lives under (the existing portable layout).</param>
public sealed record UpdatePackageDescriptor(string FileName, long Size, string Sha256, string RootFolder);

/// <summary>Phase 23D: one file of the new version, relative to the installation folder ('/'-separated).</summary>
public sealed record UpdatePackageFile(string Path, long Size, string Sha256);

/// <summary>Phase 23D: release asset names. One naming rule shared by packaging, signing and discovery.</summary>
public static class UpdateAssetNames
{
    public const string ProductFilePrefix = "Carowinds-Operations";

    /// <summary>"v0.1.0-rc.8" — the Git tag a release must carry.</summary>
    public static string Tag(SemanticVersion version) => $"v{version.Normalized}";

    /// <summary>"Carowinds-Operations-0.1.0-rc.8-win-x64" — also the ZIP's single top-level folder.</summary>
    public static string ReleaseName(SemanticVersion version) =>
        $"{ProductFilePrefix}-{version.Normalized}-{UpdateManifest.WindowsX64RuntimeIdentifier}";

    public static string Package(SemanticVersion version) => $"{ReleaseName(version)}.zip";

    public static string Manifest(SemanticVersion version) => $"{ReleaseName(version)}.update.json";

    public static string Signature(SemanticVersion version) => $"{Manifest(version)}.sig";

    /// <summary>Parses a "v"-prefixed tag back to its version; anything else is not an update tag.</summary>
    public static bool TryParseTag(string? tag, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        return tag is { Length: > 1 } && tag[0] == 'v' && SemanticVersion.TryParse(tag[1..], out version) && version.BuildMetadata is null;
    }
}
