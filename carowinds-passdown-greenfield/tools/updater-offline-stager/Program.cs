using System.Diagnostics;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.Infrastructure.Updates.Installation;

namespace Carowinds.OfflineUpdateStager;

/// <summary>
/// Offline replacement for "Install Update → Restart and Install" in Windows updater tests: does what the installed
/// app does after it has downloaded a release, with the same production classes, then exits so the updater it
/// started can take over. Nothing is downloaded, and the installed app's own close (which saves drafts) is done by
/// the tester before running this.
/// </summary>
internal static class Program
{
    private const string Usage =
        "Usage: carowinds-offline-update-stager --package <Carowinds-Operations-<version>-win-x64.zip> --install <app folder> [--updates-root <folder>] [--no-launch]";

    public static int Main(string[] args)
    {
        string? package = null, install = null, updatesRoot = null;
        var launch = true;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--package" when i + 1 < args.Length: package = args[++i]; break;
                case "--install" when i + 1 < args.Length: install = args[++i]; break;
                case "--updates-root" when i + 1 < args.Length: updatesRoot = args[++i]; break;
                case "--no-launch": launch = false; break;
                default: Console.WriteLine(Usage); return 64;
            }
        }

        if (package is null || install is null)
        {
            Console.WriteLine(Usage);
            return 64;
        }

        Console.WriteLine("UPDATER TEST ONLY — offline stager. Trusts only the throwaway test key.");
        package = Path.GetFullPath(package);
        install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(install));
        var paths = updatesRoot is null ? UpdatePaths.Default : new UpdatePaths(updatesRoot);
        var manifestPath = Path.ChangeExtension(package, ".update.json");
        var signaturePath = manifestPath + ".sig";

        // 1. Signature (EcdsaUpdateManifestVerifier, as the installed app does before offering a release).
        var verification = new EcdsaUpdateManifestVerifier(TestTrust())
            .Verify(File.ReadAllBytes(manifestPath), File.ReadAllBytes(signaturePath));
        if (!verification.IsVerified)
        {
            Console.WriteLine($"1. Signature: REJECTED ({verification.Rejection}). Nothing was changed.");
            return 2;
        }

        var manifest = verification.Manifest!;
        Console.WriteLine($"1. Signature: verified (key {verification.KeyId}), version {manifest.Version.Normalized}, {manifest.Files.Count} files.");

        // 2. Package bytes against the signed size and SHA-256 (the downloaded file's check).
        if (new FileInfo(package).Length != manifest.Package.Size || SafeZipExtractor.HashFile(package) != manifest.Package.Sha256)
        {
            Console.WriteLine("2. Package: REJECTED (size or SHA-256 does not match the signed manifest). Nothing was changed.");
            return 3;
        }

        Console.WriteLine($"2. Package: size and SHA-256 match ({manifest.Package.Sha256[..16]}…).");

        // 3. The installed version, read from the installed app (the app passes its own version).
        var installed = InstalledVersion(install);
        if (installed is null)
        {
            Console.WriteLine($"3. Installed app: no Carowinds.Passdown.Desktop.dll with a version in {install}.");
            return 4;
        }

        Console.WriteLine($"3. Installed app: {installed.Normalized} in {install}.");
        if (Process.GetProcessesByName(SystemUpdaterEnvironment.AppProcessName).Length > 0)
        {
            Console.WriteLine("   Note: a Carowinds.Passdown.Desktop process is running. Close the app first, or the updater will refuse (installation in use).");
        }

        // 4. Unpack into a brand-new staging folder (SafeZipExtractor, exactly as after a download).
        var staged = Path.Combine(paths.Staging, $"{manifest.Version.Normalized}-{Guid.NewGuid():N}");
        try
        {
            SafeZipExtractor.ExtractVerified(package, manifest, staged);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"4. Staging: REJECTED ({ex.GetType().Name}: {ex.Message}). Nothing was changed.");
            return 5;
        }

        Console.WriteLine($"4. Staging: unpacked and verified into {staged}.");
        if (!launch)
        {
            Console.WriteLine("5. Updater: not started (--no-launch).");
            return 0;
        }

        // 5. Write the install request and start the staged updater (UpdaterProcessLauncher, unchanged since RC.8).
        var offer = new UpdateOffer(new ReleaseCandidate($"v{manifest.Version.Normalized}", manifest.Version, null, null, false, []), manifest, manifest.Package.Size);
        var result = new UpdaterProcessLauncher(paths, install).Launch(new StagedUpdate(offer, staged), installed);
        if (!result.Started)
        {
            Console.WriteLine($"5. Updater: NOT started ({result.Failure}).");
            return 6;
        }

        Console.WriteLine($"5. Updater: started from the staged folder; request {paths.RequestFile(result.RequestId)}.");
        Console.WriteLine("   This window closes now; the updater waits for it to exit, then installs.");
        return 0;
    }

    private static IReadOnlyList<TrustedUpdateKey> TestTrust()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("OfflineUpdateStager.test-trust.json")
            ?? throw new InvalidOperationException("This build has no test key embedded.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("Updates").GetProperty("TrustedSigningKeys").EnumerateArray()
            .Select(key => new TrustedUpdateKey(key.GetProperty("KeyId").GetString()!, key.GetProperty("PublicKey").GetString()!))
            .ToList();
    }

    private static SemanticVersion? InstalledVersion(string install)
    {
        var dll = Path.Combine(install, "Carowinds.Passdown.Desktop.dll");
        if (!File.Exists(dll))
        {
            return null;
        }

        var product = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
        return SemanticVersion.TryParse(product?.Split('+')[0], out var version) ? version : null;
    }
}
