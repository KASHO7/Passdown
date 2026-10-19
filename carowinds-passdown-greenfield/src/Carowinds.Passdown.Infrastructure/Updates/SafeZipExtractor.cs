using System.IO.Compression;
using System.Security.Cryptography;
using Carowinds.Passdown.Application.Updates;

namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: extracts a release ZIP only if it is exactly the package the signed manifest describes.
/// Every entry must sit under the manifest's single root folder, have a safe relative path (no traversal, no
/// absolute or drive paths, no alternate data streams, no device names), not be a symbolic link, appear once,
/// and match a manifest file's size and SHA-256 byte for byte. Extra entries and missing files are both
/// rejected. Bytes written are capped at each file's declared size, so a "zip bomb" cannot fill the disk.
/// Extraction goes to a fresh folder that is deleted again on any failure.
/// </summary>
public static class SafeZipExtractor
{
    /// <summary>Unix file-type bits in a ZIP entry's external attributes; 0xA000 is a symbolic link.</summary>
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymlinkType = 0xA000;

    public static void ExtractVerified(string zipPath, UpdateManifest manifest, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
        {
            throw Invalid("The staging folder already exists.");
        }

        var expected = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var rootPrefix = manifest.Package.RootFolder + "/";
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymlinkType)
                {
                    throw Invalid("The package contains a symbolic link.");
                }

                if (!name.StartsWith(rootPrefix, StringComparison.Ordinal))
                {
                    throw Invalid("The package contains a file outside its release folder.");
                }

                var relative = name[rootPrefix.Length..];
                if (relative.Length == 0 || relative.EndsWith('/'))
                {
                    // Directory entry. Its name must still be safe; directories are created on demand.
                    if (relative.Length > 0 && !UpdateManifestJson.IsSafeRelativePath(relative.TrimEnd('/')))
                    {
                        throw Invalid("The package contains an unsafe folder name.");
                    }

                    continue;
                }

                if (!UpdateManifestJson.IsSafeRelativePath(relative))
                {
                    throw Invalid("The package contains an unsafe file name.");
                }

                if (!expected.TryGetValue(relative, out var file) || !string.Equals(file.Path, relative, StringComparison.Ordinal))
                {
                    throw Invalid("The package contains a file the signed manifest does not list.");
                }

                if (!extracted.Add(relative))
                {
                    throw Invalid("The package lists the same file twice.");
                }

                if (entry.Length != file.Size)
                {
                    throw Invalid("A file in the package has an unexpected size.");
                }

                var target = Path.GetFullPath(Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!UpdatePaths.IsWithin(target, destinationRoot) || target.Length == destinationRoot.Length)
                {
                    throw Invalid("A file in the package would be written outside the staging folder.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var hash = CopyCapped(entry, target, file.Size);
                if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
                {
                    throw Invalid("A file in the package does not match the signed manifest.");
                }
            }

            if (extracted.Count != expected.Count)
            {
                throw Invalid("The package is missing files the signed manifest lists.");
            }
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(destinationRoot);
            if (ex is UpdateStagingException)
            {
                throw;
            }

            if (ex is InvalidDataException)
            {
                throw new UpdateStagingException(StagingFailureKind.InvalidArchive, "The package is not a valid ZIP file.", ex);
            }

            if (ex is IOException { HResult: unchecked((int)0x80070070) or unchecked((int)0x80070027) })
            {
                throw new UpdateStagingException(StagingFailureKind.InsufficientDiskSpace, "There is not enough free disk space.", ex);
            }

            if (ex is IOException or UnauthorizedAccessException)
            {
                throw new UpdateStagingException(StagingFailureKind.StorageError, "The update could not be unpacked.", ex);
            }

            throw;
        }
    }

    /// <summary>Lowercase hex SHA-256 of a file.</summary>
    public static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string CopyCapped(ZipArchiveEntry entry, string target, long declaredSize)
    {
        using var input = entry.Open();
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > declaredSize)
            {
                throw Invalid("A file in the package is larger than declared.");
            }

            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }

        if (total != declaredSize)
        {
            throw Invalid("A file in the package is shorter than declared.");
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static UpdateStagingException Invalid(string message) => new(StagingFailureKind.InvalidArchive, message);

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !UpdatePaths.IsLink(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover staging folder is removed by the next cleanup.
        }
    }
}
