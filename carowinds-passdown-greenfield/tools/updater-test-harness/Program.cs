using System.Reflection;
using System.Text;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.Infrastructure.Updates.Installation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.UpdaterTestHarness;

/// <summary>
/// Synthetic "new version" for Windows updater tests. Started by the real external updater after it replaced the
/// program files, it does what the real app does at startup that matters to the updater — and nothing else:
/// 1. run the production migrations against the real data folder (with the real verified pre-migration backup),
/// 2. confirm the launch to the waiting updater and wait for its final record (real <see cref="PostUpdateLaunch"/>),
/// 3. apply the real What's New policy and, when it says so, show the notes once and record the acknowledgement.
/// Every step is printed and written to updater-test-report-*.txt in the data folder.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--verify-release", var zip, var manifest, var signature, var publicKeyId, var publicKey])
        {
            return VerifyRelease(zip, manifest, signature, publicKeyId, publicKey);
        }

        var report = new StringBuilder();
        void Say(string line)
        {
            Console.WriteLine(line);
            report.AppendLine(line);
        }

        var assembly = typeof(Program).Assembly;
        var mode = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "HarnessMode")?.Value ?? "Normal";
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var running = SemanticVersion.Parse(informational.Split('+')[0]);
        var databasePath = Path.Combine(AppPaths.LocalApplicationDataDirectory, SqliteUpdateDatabaseGuard.DatabaseFileName);
        var exitCode = 0;

        Say("UPDATER TEST ONLY — synthetic package. This is not Carowinds Operations.");
        Say($"Version {running.Normalized} ({informational}), mode {mode}");
        Say($"Running from {AppContext.BaseDirectory}");
        Say($"Data folder  {AppPaths.LocalApplicationDataDirectory}");
        Say($"Started at   {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Say(string.Empty);

        try
        {
            if (mode == "CrashBeforeMigration")
            {
                Say("Simulating a crash BEFORE the database is touched (exit code 3). Expected: the updater restores the previous version and starts it once.");
                exitCode = 3;
                return exitCode;
            }

            Say($"Database before: schema {Describe(SafeSchema(databasePath))}");
            var runner = new MigrationRunner(new SqliteConnectionFactory(databasePath), Migrations(), NullLogger<MigrationRunner>.Instance);
            var schema = runner.Run();
            Say($"Database after:  schema {schema} (integrity check passed)");
            Say($"Pre-migration backup made by this start: {runner.LastPreMigrationBackupPath ?? "none (nothing to migrate)"}");
            Say(string.Empty);

            if (mode == "CrashAfterMigration")
            {
                Say("Simulating a crash AFTER upgrading the database, before confirming the launch (exit code 4).");
                Say("Expected: the updater does NOT restore the previous version, starts nothing, records RecoveryRequired and writes UPDATE-RECOVERY.txt.");
                exitCode = 4;
                return exitCode;
            }

            var postUpdate = new PostUpdateLaunch(new UpdateInstallRecordStore(UpdatePaths.Default, AppContext.BaseDirectory), running);
            var outcome = await postUpdate.ConfirmAsync().ConfigureAwait(false);
            Say(outcome is null
                ? "Update record: none (manual start, not an in-app update)."
                : $"Update record: {outcome.Status} {outcome.FromVersion} -> {outcome.ToVersion}{(outcome.FailureCode is null ? string.Empty : $" ({outcome.FailureCode})")}");

            var stateStore = new FileUpdateStateStore(FileUpdateStateStore.DefaultPath);
            var state = stateStore.Load();
            if (postUpdate.ShouldShowWhatsNew(outcome, state.LastAcknowledgedReleaseNotesVersion))
            {
                Say(string.Empty);
                Say("=== WHAT'S NEW (shown once after a successful update) ===");
                Say(ReadNotes(running.Normalized));
                Say("=== end of What's New ===");
                var saved = await stateStore.SaveAsync(state with { LastAcknowledgedReleaseNotesVersion = running.Normalized }).ConfigureAwait(false);
                Say(saved ? "What's New acknowledged; it will not open again for this version." : "WARNING: acknowledgement could not be saved.");
            }
            else
            {
                Say($"What's New: not shown (last acknowledged: {state.LastAcknowledgedReleaseNotesVersion ?? "none"}).");
            }
        }
        catch (Exception ex)
        {
            Say($"FAILED: {ex.GetType().Name}: {ex.Message}");
            exitCode = 5;
        }
        finally
        {
            Say(string.Empty);
            Say($"Exit code {exitCode}.");
            WriteReport(report.ToString());
        }

        if (!Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close this window.");
            Console.ReadLine();
        }

        return exitCode;
    }

    /// <summary>The production migration list, in the same order the Desktop app registers it.</summary>
    private static IReadOnlyList<IMigration> Migrations() =>
    [
        new Migration0001CreateSchemaVersionTable(), new Migration0002CreateSourceStateTable(), new Migration0003CreateDatedResultsTable(),
        new Migration0004CreateCatalogProductsTable(), new Migration0005CreateProductHistoryTable(), new Migration0006CreateAssociateDraftsTable(),
        new Migration0007CreateManagerDraftsTable(), new Migration0008CreateFinalizedPassdownsTable(), new Migration0009AddFinalizedPassdownPdfColumns(),
        new Migration0010CreateOperationalNotesTable(), new Migration0011CreateDeepRefreshStatusTable(), new Migration0012CreateSyncEntityStateTable(),
        new Migration0013CreateSyncConflictsTable(), new Migration0014CreateMonthlyErtConfigurationsTable(), new Migration0015CreateErtDateOverridesTable(),
        new Migration0016CreateSeasonPriceHistoryTable(), new Migration0017CreateSyncedPriceObservationsTable(),
    ];

    private static int? SafeSchema(string databasePath)
    {
        try
        {
            return SqliteDatabaseBackup.ReadSchemaVersion(databasePath);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static string Describe(int? schema) => schema switch
    {
        null => "none (no database yet)",
        -1 => "unreadable",
        _ => schema.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string ReadNotes(string version)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("UpdaterTestHarness.release-notes.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("{VERSION}", version, StringComparison.Ordinal).TrimEnd();
    }

    private static void WriteReport(string text)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalApplicationDataDirectory);
            var path = Path.Combine(AppPaths.LocalApplicationDataDirectory, $"updater-test-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text);
            Console.WriteLine($"Report saved: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Report could not be saved ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Build-host check (never used by the updater, which starts this program without arguments): the signed manifest
    /// verifies with the given public key, and the ZIP stages exactly as the installed app would stage it.
    /// </summary>
    private static int VerifyRelease(string zip, string manifestPath, string signaturePath, string keyId, string publicKey)
    {
        var verification = new EcdsaUpdateManifestVerifier([new TrustedUpdateKey(keyId, publicKey)])
            .Verify(File.ReadAllBytes(manifestPath), File.ReadAllBytes(signaturePath));
        if (!verification.IsVerified)
        {
            Console.WriteLine($"SIGNATURE REJECTED: {verification.Rejection}");
            return 2;
        }

        var package = verification.Manifest!.Package;
        if (new FileInfo(zip).Length != package.Size || SafeZipExtractor.HashFile(zip) != package.Sha256)
        {
            Console.WriteLine("PACKAGE REJECTED: size or SHA-256 does not match the signed manifest");
            return 4;
        }

        var staging = Path.Combine(Path.GetTempPath(), "carowinds-updater-test-verify", Guid.NewGuid().ToString("N"));
        try
        {
            SafeZipExtractor.ExtractVerified(zip, verification.Manifest!, staging);
            Console.WriteLine($"VERIFIED AND STAGED: {verification.Manifest!.Version.Normalized}, {verification.Manifest.Files.Count} files");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"STAGING REJECTED: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
