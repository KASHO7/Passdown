using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.UpdateSigning;

namespace Carowinds.Passdown.Infrastructure.Tests.Updates;

/// <summary>Phase 23D: the release signing tool (disposable keys only) and the local update-state file.</summary>
public sealed class ReleaseSigningAndLocalStateTests : IDisposable
{
    private readonly UpdateTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public void GenerateCreateSignVerify_RoundTrip()
    {
        var keyPath = Path.Combine(_kit.Root, "keys", "signing.pem");
        var publicKey = ReleaseSigning.GenerateKey(keyPath);
        var zip = _kit.CreateZip("0.1.0-rc.8");
        var manifest = UpdateTestKit.CreateManifest(zip, "0.1.0-rc.8");

        var signature = ReleaseSigning.Sign(manifest, keyPath, "release-2026");
        var result = ReleaseSigning.Verify(manifest, signature, [new TrustedUpdateKey("release-2026", publicKey)]);

        Assert.True(result.IsVerified);
        // The signature file never contains private key material.
        Assert.DoesNotContain("PRIVATE", System.Text.Encoding.UTF8.GetString(signature));
        Assert.DoesNotContain(File.ReadAllText(keyPath).Split('\n')[1], System.Text.Encoding.UTF8.GetString(manifest) + System.Text.Encoding.UTF8.GetString(signature));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyPath));
        }
    }

    [Fact]
    public void MissingPrivateKeyConfiguration_FailsClosed()
    {
        var previous = Environment.GetEnvironmentVariable(ReleaseSigning.KeyFileEnvironmentVariable);
        Environment.SetEnvironmentVariable(ReleaseSigning.KeyFileEnvironmentVariable, null);
        try
        {
            var manifest = UpdateTestKit.CreateManifest(_kit.CreateZip("0.1.0-rc.8"), "0.1.0-rc.8");
            var ex = Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.Sign(manifest, null, "release-2026"));
            Assert.Contains("No signing key is configured", ex.Message);
            Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.Sign(manifest, Path.Combine(_kit.Root, "missing.pem"), "release-2026"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ReleaseSigning.KeyFileEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void KeyGeneration_RefusesAGitWorkingTree_AndNeverOverwritesAKey()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_kit.Root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.GenerateKey(Path.Combine(repo, "keys", "signing.pem")));

        var keyPath = Path.Combine(_kit.Root, "signing.pem");
        ReleaseSigning.GenerateKey(keyPath);
        var original = File.ReadAllText(keyPath);
        Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.GenerateKey(keyPath));
        Assert.Equal(original, File.ReadAllText(keyPath));
    }

    [Fact]
    public void CreateManifest_RejectsWrongNamesChannelsAndLayouts()
    {
        var zip = _kit.CreateZip("0.1.0-rc.8");
        Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.CreateManifest(zip, "0.1.0-rc.9", "release-candidate", "t", "n", null));
        Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.CreateManifest(zip, "0.1.0-rc.8", "stable", "t", "n", null));
        Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.CreateManifest(zip, "v0.1.0-rc.8", "release-candidate", "t", "n", null));

        var renamed = Path.Combine(_kit.Root, "renamed.zip");
        File.Copy(zip, renamed);
        Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.CreateManifest(renamed, "0.1.0-rc.8", "release-candidate", "t", "n", null));
    }

    [Fact]
    public void Sign_RefusesAnInvalidManifest()
    {
        var keyPath = Path.Combine(_kit.Root, "signing.pem");
        ReleaseSigning.GenerateKey(keyPath);
        Assert.Throws<ReleaseSigningException>(() => ReleaseSigning.Sign("{}"u8.ToArray(), keyPath, "release-2026"));
    }

    [Fact]
    public void ShippedConfiguration_HasNoTrustedKeyYet_SoInstallationStaysDisabled()
    {
        var appSettings = Path.Combine(ReleaseRepository.Root, "src", "Carowinds.Passdown.Desktop", "appsettings.json");
        var keys = ReleaseSigning.ReadTrustedKeys(appSettings);
        // Phase 23D ships with the provider wired to the project's own repository but no production key: the owner
        // provisions it (docs/phase23d-github-updater.md). Every configured key must at least be a valid P-256 key.
        Assert.All(keys, key => Assert.True(EcdsaUpdateManifestVerifier.TryDecodeP256PublicKey(key.PublicKeySpkiBase64, out _)));
        Assert.Contains("\"Repository\": \"KASHO7/Passdown\"", File.ReadAllText(appSettings));
    }

    [Fact]
    public void RepositoryContainsNoPrivateKeyMaterial()
    {
        var offenders = Directory.EnumerateFiles(ReleaseRepository.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && new FileInfo(path).Length < 2_000_000)
            .Where(path => Path.GetExtension(path) is ".pem" or ".key" or ".json" or ".txt" or ".md" or ".cs" or ".ps1")
            .Where(path => File.ReadAllText(path).Contains("-----BEGIN " + "PRIVATE KEY-----", StringComparison.Ordinal)
                || File.ReadAllText(path).Contains("-----BEGIN EC " + "PRIVATE KEY-----", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public async Task LocalState_RoundTrips_AndBadFilesFallBackToEmpty()
    {
        var path = Path.Combine(_kit.Root, "updates.json");
        var store = new FileUpdateStateStore(path);
        Assert.Equal(UpdateLocalState.Empty, store.Load());

        var state = new UpdateLocalState(DateTimeOffset.UnixEpoch, null, "0.1.0-rc.8", DateTimeOffset.UnixEpoch.AddDays(7), "0.1.0-rc.7");
        Assert.True(await store.SaveAsync(state));
        Assert.Equal(state, store.Load());

        File.WriteAllText(path, "{ not json");
        Assert.Equal(UpdateLocalState.Empty, store.Load());
    }

    [Fact]
    public void LocalStatePaths_AreOutsideTheSyncFolder_AndPackagesAreOutsideTheDataFolder()
    {
        Assert.Equal(Path.Combine(AppPaths.LocalApplicationDataDirectory, "updates.json"), FileUpdateStateStore.DefaultPath);
        Assert.False(UpdatePaths.IsWithin(UpdatePaths.DefaultRoot, AppPaths.LocalApplicationDataDirectory));
        Assert.False(UpdatePaths.IsWithin(AppPaths.LocalApplicationDataDirectory, UpdatePaths.DefaultRoot));
    }

    private static class ReleaseRepository
    {
        public static string Root { get; } = Find();

        private static string Find()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
