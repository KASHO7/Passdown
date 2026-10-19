using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Infrastructure.Updates;

namespace Carowinds.Passdown.UpdateSigning;

/// <summary>Phase 23D: a release-production step that could not be completed. The message is safe to print (never contains key material).</summary>
public sealed class ReleaseSigningException(string message) : Exception(message);

/// <summary>
/// Phase 23D: the release signing operations. The private key is only ever read from a PEM file the caller names
/// (or <see cref="KeyFileEnvironmentVariable"/>); it is never printed, never written into the manifest or signature,
/// and a key file inside a Git working tree is refused.
/// </summary>
public static class ReleaseSigning
{
    public const string KeyFileEnvironmentVariable = "CAROWINDS_UPDATE_SIGNING_KEY_FILE";

    /// <summary>Creates a new P-256 key pair. Writes the private key (PKCS#8 PEM) and returns the public key (SPKI, Base64).</summary>
    public static string GenerateKey(string privateKeyPath)
    {
        var fullPath = Path.GetFullPath(privateKeyPath);
        RefuseInsideGitWorkingTree(fullPath);
        if (File.Exists(fullPath))
        {
            throw new ReleaseSigningException("A file already exists at the private key path; refusing to overwrite a signing key.");
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(ecdsa.ExportPkcs8PrivateKeyPem());
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Builds the manifest for an existing release ZIP: package hash plus every file's size and SHA-256.</summary>
    public static byte[] CreateManifest(string zipPath, string versionText, string channelText, string title, string releaseNotes, DateTimeOffset? publishedAtUtc)
    {
        if (!SemanticVersion.TryParse(versionText, out var version) || version.BuildMetadata is not null)
        {
            throw new ReleaseSigningException("The version is not a valid semantic version (e.g. 0.1.0-rc.8).");
        }

        if (!UpdateChannelPolicy.TryParseManifestName(channelText, out var channel) || !UpdateChannelPolicy.IsConsistent(channel, version))
        {
            throw new ReleaseSigningException($"The channel must be '{UpdateChannelPolicy.ReleaseCandidateName}' for 0.1.0-rc.N versions or '{UpdateChannelPolicy.StableName}' for versions without a prerelease part.");
        }

        if (!File.Exists(zipPath))
        {
            throw new ReleaseSigningException("The release ZIP was not found.");
        }

        var expectedName = UpdateAssetNames.Package(version);
        if (!string.Equals(Path.GetFileName(zipPath), expectedName, StringComparison.Ordinal))
        {
            throw new ReleaseSigningException($"The ZIP must be named exactly '{expectedName}'.");
        }

        var rootFolder = UpdateAssetNames.ReleaseName(version);
        var files = new List<UpdatePackageFile>();
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(rootFolder + "/", StringComparison.Ordinal))
                {
                    throw new ReleaseSigningException($"Every ZIP entry must be inside '{rootFolder}/'.");
                }

                var relative = name[(rootFolder.Length + 1)..];
                if (relative.Length == 0 || relative.EndsWith('/'))
                {
                    continue;
                }

                using var stream = entry.Open();
                files.Add(new UpdatePackageFile(relative, entry.Length, Convert.ToHexStringLower(SHA256.HashData(stream))));
            }
        }

        var manifest = new UpdateManifest(
            UpdateManifest.CurrentFormatVersion,
            UpdateManifest.CarowindsOperationsProductId,
            version,
            channel,
            UpdateManifest.WindowsX64RuntimeIdentifier,
            publishedAtUtc,
            title,
            releaseNotes,
            new UpdatePackageDescriptor(expectedName, new FileInfo(zipPath).Length, SafeZipExtractor.HashFile(zipPath), rootFolder),
            files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList());

        var bytes = UpdateManifestJson.Serialize(manifest);
        if (UpdateManifestJson.TryParse(bytes, out var rejection, out var detail) is null)
        {
            throw new ReleaseSigningException($"The release ZIP does not produce a valid update manifest ({rejection}: {detail}). Check that it is a complete portable release with the updater.");
        }

        return bytes;
    }

    /// <summary>Signs the exact manifest bytes. Requires the private key file; verifies the result before returning it.</summary>
    public static byte[] Sign(byte[] manifestBytes, string? privateKeyPath, string keyId)
    {
        privateKeyPath ??= Environment.GetEnvironmentVariable(KeyFileEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new ReleaseSigningException($"No signing key is configured. Pass --private-key <file> or set {KeyFileEnvironmentVariable}. The key is never stored in the repository.");
        }

        if (!File.Exists(privateKeyPath))
        {
            throw new ReleaseSigningException("The signing key file was not found.");
        }

        if (!EcdsaUpdateManifestVerifier.IsValidKeyId(keyId))
        {
            throw new ReleaseSigningException("The key id may contain only letters, digits, '-', '_' and '.' (1–64 characters).");
        }

        if (UpdateManifestJson.TryParse(manifestBytes, out var rejection, out _) is null)
        {
            throw new ReleaseSigningException($"The manifest is not valid ({rejection}); refusing to sign it.");
        }

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(File.ReadAllText(privateKeyPath));
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new ReleaseSigningException("The signing key file is not a valid ECDSA private key.");
        }

        if (ecdsa.KeySize != 256)
        {
            throw new ReleaseSigningException("The signing key must be ECDSA P-256.");
        }

        var signature = ecdsa.SignData(manifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var signatureFile = JsonSerializer.SerializeToUtf8Bytes(new SignatureFile(
            EcdsaUpdateManifestVerifier.SignatureFormatVersion, EcdsaUpdateManifestVerifier.Algorithm, keyId, Convert.ToBase64String(signature)),
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var check = Verify(manifestBytes, signatureFile, [new TrustedUpdateKey(keyId, publicKey)]);
        if (!check.IsVerified)
        {
            throw new ReleaseSigningException($"The new signature did not verify ({check.Rejection}).");
        }

        return signatureFile;
    }

    public static ManifestVerificationResult Verify(byte[] manifestBytes, byte[] signatureBytes, IEnumerable<TrustedUpdateKey> trustedKeys) =>
        new EcdsaUpdateManifestVerifier(trustedKeys).Verify(manifestBytes, signatureBytes);

    /// <summary>Reads Updates:TrustedSigningKeys from an appsettings.json, so verification uses exactly what the app will trust.</summary>
    public static IReadOnlyList<TrustedUpdateKey> ReadTrustedKeys(string appSettingsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(appSettingsPath));
        if (!document.RootElement.TryGetProperty("Updates", out var updates)
            || !updates.TryGetProperty("TrustedSigningKeys", out var keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return keys.EnumerateArray()
            .Where(key => key.ValueKind == JsonValueKind.Object)
            .Select(key => new TrustedUpdateKey(
                key.TryGetProperty("KeyId", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                key.TryGetProperty("PublicKey", out var value) ? value.GetString() ?? string.Empty : string.Empty))
            .ToList();
    }

    private static void RefuseInsideGitWorkingTree(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                throw new ReleaseSigningException("Refusing to create a signing key inside a Git working tree. Choose a folder outside the repository (and outside any cloud-synced folder).");
            }

            directory = directory.Parent;
        }
    }

    private sealed record SignatureFile(int FormatVersion, string Algorithm, string KeyId, string Signature);
}
