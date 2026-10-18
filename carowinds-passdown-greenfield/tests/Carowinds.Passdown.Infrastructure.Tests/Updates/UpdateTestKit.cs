using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.UpdateSigning;

namespace Carowinds.Passdown.Infrastructure.Tests.Updates;

/// <summary>
/// Phase 23D: builds synthetic signed releases in a temporary folder with a disposable ECDSA key created per test.
/// Never touches the real installation, profile or network.
/// </summary>
internal sealed class UpdateTestKit : IDisposable
{
    public const string KeyId = "test-key-1";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public UpdateTestKit()
    {
        Root = Path.Combine(Path.GetTempPath(), "carowinds-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        PublicKey = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    }

    public string Root { get; }

    public string PublicKey { get; }

    public TrustedUpdateKey TrustedKey => new(KeyId, PublicKey);

    public EcdsaUpdateManifestVerifier Verifier() => new([TrustedKey]);

    /// <summary>A minimal "release": every required file plus a few extras, with distinct contents per version.</summary>
    public static Dictionary<string, byte[]> AppFiles(string version) => new(StringComparer.Ordinal)
    {
        ["Carowinds.Passdown.Desktop.exe"] = Encoding.UTF8.GetBytes($"desktop-exe {version}"),
        ["Carowinds.Passdown.Desktop.dll"] = Encoding.UTF8.GetBytes($"desktop-dll {version}"),
        ["Carowinds.Passdown.Desktop.runtimeconfig.json"] = Encoding.UTF8.GetBytes("{}"),
        ["Carowinds.Passdown.Updater.exe"] = Encoding.UTF8.GetBytes($"updater-exe {version}"),
        ["Carowinds.Passdown.Updater.dll"] = Encoding.UTF8.GetBytes($"updater-dll {version}"),
        ["Carowinds.Passdown.Updater.runtimeconfig.json"] = Encoding.UTF8.GetBytes("{}"),
        ["coreclr.dll"] = Encoding.UTF8.GetBytes($"runtime {version}"),
        ["LatoFont/OFL.txt"] = Encoding.UTF8.GetBytes("license"),
    };

    /// <summary>Writes a release ZIP with the portable layout (single top-level folder).</summary>
    public string CreateZip(string version, IReadOnlyDictionary<string, byte[]>? files = null, Action<ZipArchive, string>? extra = null)
    {
        var parsed = SemanticVersion.Parse(version);
        var directory = Path.Combine(Root, "release-" + version);
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, UpdateAssetNames.Package(parsed));
        var rootFolder = UpdateAssetNames.ReleaseName(parsed);
        File.Delete(zipPath); // a test may rebuild the same version with different contents
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (path, bytes) in files ?? AppFiles(version))
            {
                using var stream = archive.CreateEntry($"{rootFolder}/{path}").Open();
                stream.Write(bytes);
            }

            extra?.Invoke(archive, rootFolder);
        }

        return zipPath;
    }

    public static byte[] CreateManifest(string zipPath, string version, string notes = "## Highlights\n- Synthetic release") =>
        ReleaseSigning.CreateManifest(zipPath, version, SemanticVersion.Parse(version).IsPrerelease ? "release-candidate" : "stable", $"Synthetic {version}", notes, new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    public byte[] Sign(byte[] manifest, string keyId = KeyId) => SignWith(_key, manifest, keyId);

    public static byte[] SignWith(ECDsa key, byte[] manifest, string keyId) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            algorithm = EcdsaUpdateManifestVerifier.Algorithm,
            keyId,
            signature = Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
        });

    /// <summary>A verified manifest for a freshly built ZIP.</summary>
    public (string ZipPath, UpdateManifest Manifest) BuildVerifiedRelease(string version, IReadOnlyDictionary<string, byte[]>? files = null)
    {
        var zip = CreateZip(version, files);
        var manifestBytes = CreateManifest(zip, version);
        var result = Verifier().Verify(manifestBytes, Sign(manifestBytes));
        Assert.True(result.IsVerified, result.Rejection.ToString());
        return (zip, result.Manifest!);
    }

    public void Dispose()
    {
        _key.Dispose();
        try
        {
            if (Directory.Exists(Root))
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
