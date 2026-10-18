using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: the update manifest's JSON format (format version 1) — one strict reader used by the app and
/// one writer used by the release signing tool, so both sides always agree. The signature covers the exact
/// manifest bytes, so no canonical form is needed. The reader rejects anything outside the documented
/// limits: oversize values, malformed hashes, unsafe or duplicate file paths, a missing main executable or
/// updater. See docs/phase23d-github-updater.md "Manifest format".
/// </summary>
public static class UpdateManifestJson
{
    public const int MaximumFiles = 5_000;

    public const int MaximumPathLength = 240;

    public const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;

    public const int MaximumTitleLength = 200;

    public const string MainExecutable = "Carowinds.Passdown.Desktop.exe";

    public const string UpdaterExecutable = "Carowinds.Passdown.Updater.exe";

    /// <summary>Files the new version must contain for the app to run and for the next update to be possible.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } =
    [
        MainExecutable,
        "Carowinds.Passdown.Desktop.dll",
        "Carowinds.Passdown.Desktop.runtimeconfig.json",
        UpdaterExecutable,
        "Carowinds.Passdown.Updater.dll",
        "Carowinds.Passdown.Updater.runtimeconfig.json",
    ];

    private static readonly string[] WindowsReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    public static byte[] Serialize(UpdateManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", manifest.FormatVersion);
            writer.WriteString("productId", manifest.ProductId);
            writer.WriteString("version", manifest.Version.Normalized);
            writer.WriteString("channel", UpdateChannelPolicy.ToManifestName(manifest.Channel));
            writer.WriteString("runtimeIdentifier", manifest.RuntimeIdentifier);
            if (manifest.PublishedAtUtc is { } published)
            {
                writer.WriteString("publishedAtUtc", published.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }

            writer.WriteString("title", manifest.Title);
            writer.WriteString("releaseNotes", manifest.ReleaseNotes);
            writer.WriteStartObject("package");
            writer.WriteString("fileName", manifest.Package.FileName);
            writer.WriteNumber("size", manifest.Package.Size);
            writer.WriteString("sha256", manifest.Package.Sha256);
            writer.WriteString("rootFolder", manifest.Package.RootFolder);
            writer.WriteEndObject();
            writer.WriteStartArray("files");
            foreach (var file in manifest.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteNumber("size", file.Size);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>Parses and validates. Returns null with a reason when the manifest is unusable.</summary>
    public static UpdateManifest? TryParse(ReadOnlySpan<byte> json, out ManifestRejection rejection, out string? detail)
    {
        rejection = ManifestRejection.MalformedManifest;
        detail = null;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, AllowDuplicateProperties = false });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                detail = "root is not an object";
                return null;
            }

            if (!TryGetInt(root, "formatVersion", out var formatVersion))
            {
                detail = "formatVersion";
                return null;
            }

            if (formatVersion != UpdateManifest.CurrentFormatVersion)
            {
                rejection = ManifestRejection.UnsupportedFormat;
                return null;
            }

            var productId = GetString(root, "productId", 64);
            var versionText = GetString(root, "version", SemanticVersion.MaximumLength);
            var channelText = GetString(root, "channel", 32);
            var runtimeIdentifier = GetString(root, "runtimeIdentifier", 32);
            var title = GetString(root, "title", MaximumTitleLength);
            var notes = GetString(root, "releaseNotes", ReleaseNotesDocument.MaximumSourceCharacters);
            if (productId is null || versionText is null || channelText is null || runtimeIdentifier is null || title is null || notes is null)
            {
                detail = "missing or oversize text field";
                return null;
            }

            if (!SemanticVersion.TryParse(versionText, out var version) || version.BuildMetadata is not null)
            {
                detail = "version";
                return null;
            }

            DateTimeOffset? published = null;
            if (root.TryGetProperty("publishedAtUtc", out var publishedElement))
            {
                if (publishedElement.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParseExact(publishedElement.GetString(), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    detail = "publishedAtUtc";
                    return null;
                }

                published = parsed;
            }

            if (productId != UpdateManifest.CarowindsOperationsProductId)
            {
                rejection = ManifestRejection.WrongProduct;
                return null;
            }

            if (runtimeIdentifier != UpdateManifest.WindowsX64RuntimeIdentifier)
            {
                rejection = ManifestRejection.WrongArchitecture;
                return null;
            }

            if (!UpdateChannelPolicy.TryParseManifestName(channelText, out var channel) || !UpdateChannelPolicy.IsConsistent(channel, version))
            {
                rejection = ManifestRejection.InconsistentChannel;
                return null;
            }

            if (!root.TryGetProperty("package", out var packageElement) || packageElement.ValueKind != JsonValueKind.Object)
            {
                detail = "package";
                return null;
            }

            var fileName = GetString(packageElement, "fileName", 200);
            var packageHash = GetString(packageElement, "sha256", 64);
            var rootFolder = GetString(packageElement, "rootFolder", 200);
            if (fileName is null || rootFolder is null || !IsSha256(packageHash)
                || !TryGetLong(packageElement, "size", out var packageSize) || packageSize <= 0 || packageSize > MaximumPackageBytes
                || !IsSafeSegment(rootFolder) || fileName.Contains('/') || fileName.Contains('\\'))
            {
                detail = "package fields";
                return null;
            }

            if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
            {
                detail = "files";
                return null;
            }

            var count = filesElement.GetArrayLength();
            if (count == 0 || count > MaximumFiles)
            {
                detail = "file count";
                return null;
            }

            var files = new List<UpdatePackageFile>(count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var fileElement in filesElement.EnumerateArray())
            {
                if (fileElement.ValueKind != JsonValueKind.Object)
                {
                    detail = "file entry";
                    return null;
                }

                var path = GetString(fileElement, "path", MaximumPathLength);
                var hash = GetString(fileElement, "sha256", 64);
                if (path is null || !IsSafeRelativePath(path) || !IsSha256(hash)
                    || !TryGetLong(fileElement, "size", out var size) || size < 0 || size > MaximumPackageBytes * 4)
                {
                    detail = "file entry fields";
                    return null;
                }

                if (!seen.Add(path))
                {
                    detail = "duplicate file path";
                    return null;
                }

                total += size;
                files.Add(new UpdatePackageFile(path, size, hash!));
            }

            if (total > MaximumPackageBytes * 4)
            {
                detail = "total extracted size";
                return null;
            }

            if (RequiredFiles.Any(required => !files.Any(file => string.Equals(file.Path, required, StringComparison.Ordinal))))
            {
                detail = "required file missing";
                return null;
            }

            rejection = ManifestRejection.None;
            return new UpdateManifest(
                formatVersion, productId, version, channel, runtimeIdentifier, published, title, notes,
                new UpdatePackageDescriptor(fileName, packageSize, packageHash!, rootFolder),
                files);
        }
        catch (JsonException)
        {
            detail = "invalid JSON";
            return null;
        }
        catch (InvalidOperationException)
        {
            detail = "unexpected JSON type";
            return null;
        }
    }

    /// <summary>
    /// A file path inside the package: '/'-separated, relative, every segment safe on Windows, and not inside
    /// the updater's own work folder. Rejects "..", ".", empty segments, drive letters, ':' (alternate data
    /// streams), backslashes, control characters, wildcard characters, trailing dots/spaces and device names.
    /// </summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (path.Length == 0 || path.Length > MaximumPathLength || path[0] == '/' || path.Contains('\\'))
        {
            return false;
        }

        var segments = path.Split('/');
        if (string.Equals(segments[0], UpdatePaths.InstallWorkDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return segments.All(IsSafeSegment);
    }

    public static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.StartsWith(' '))
        {
            return false;
        }

        if (segment.Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|' or '/' or '\\'))
        {
            return false;
        }

        var stem = segment.Split('.')[0];
        return !WindowsReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static string? GetString(JsonElement element, string name, int maximumLength) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= maximumLength
            ? text
            : null;

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static bool TryGetLong(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value);
    }
}
