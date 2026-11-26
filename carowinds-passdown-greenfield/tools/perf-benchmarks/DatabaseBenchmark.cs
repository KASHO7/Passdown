using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace PerfBenchmarks;

/// <summary>
/// Read-path timings on a synthetic SQLite database built with the real migrations and repositories: three
/// years of dated results, 24 tracked season products with 40 local price intervals each, and a configurable
/// number of Personal Sync observations. "Caller" is the time spent inside the call before it returns its
/// task — in the app that is time the UI thread is blocked, because Microsoft.Data.Sqlite completes
/// synchronously (the "completed on return" column confirms it).
/// </summary>
internal static class DatabaseBenchmark
{
    private const string SeasonSource = "Carowinds Memberships & Season Passes";
    private static readonly Uri SourceUrl = new("https://www.sixflags.com/carowinds/store/season-passes");
    private static readonly DateOnly SelectedDate = new(2026, 8, 21);
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Tiers = ["gold", "prestige", "platinum", "diamond", "silver", "bronze"];
    private static readonly (string Kind, string Suffix)[] Kinds =
    [
        ("season-pass|new-purchase|2027", "Pass"),
        ("season-pass|renewal|2027", "Pass Renewal"),
        ("membership|monthly", "Membership"),
        ("membership|initiation-fee", "Membership Initiation Fee"),
    ];

    public static async Task<int> RunAsync(string scratchDirectory, int[] syncedCounts)
    {
        foreach (var synced in syncedCounts.Distinct())
        {
            var databasePath = Path.Combine(scratchDirectory, $"perf-{synced}-{Guid.NewGuid():N}.db");
            try
            {
                await RunOneAsync(databasePath, synced);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                foreach (var file in Directory.GetFiles(scratchDirectory, Path.GetFileName(databasePath) + "*"))
                {
                    File.Delete(file);
                }
            }
        }

        return 0;
    }

    private static async Task RunOneAsync(string databasePath, int syncedCount)
    {
        Console.WriteLine();
        Console.WriteLine($"## db — synthetic database with {syncedCount:N0} synced price observations");
        var factory = new SqliteConnectionFactory(databasePath);
        var seedTime = Stopwatch.StartNew();
        new MigrationRunner(factory, Migrations(), NullLogger<MigrationRunner>.Instance).Run();
        var productKeys = await SeedAsync(factory, syncedCount);
        Console.WriteLine($"  seeded in {seedTime.Elapsed.TotalSeconds:F1} s ({new FileInfo(databasePath).Length / 1048576.0:F1} MB)");

        IClock clock = new FakeClock(Now);
        var drafts = new SqliteAssociateDraftRepository(factory);
        var datedResults = new SqliteDatedResultRepository(factory);
        var catalog = new SqliteCatalogProductRepository(factory);
        var notes = new OperationalNotesService(new SqliteOperationalNoteRepository(factory), clock);
        var associate = new AssociatePassdownService(drafts, datedResults, catalog, new UnreachableRefreshOrchestrator(), notes, clock);
        var calendar = new OperationsCalendarService(associate, drafts, datedResults, new SqliteFinalizedPassdownRepository(factory), notes, clock);
        var priceHistory = new SeasonPriceHistoryService(catalog, new SqliteSeasonPriceHistoryRepository(factory), new SqliteSyncedPriceObservationRepository(factory));

        var dates = Enumerable.Range(0, 20).Select(i => SelectedDate.AddDays(i - 10)).ToArray();
        var i = 0;
        await MeasureAsync("Associate date load (LoadAssociateDateAsync)", () => associate.LoadAssociateDateAsync(dates[i++ % dates.Length]));
        var month = 0;
        await MeasureAsync("Calendar month (OperationsCalendarService.LoadAsync)", () =>
        {
            var displayed = new DateOnly(2026, 1 + (month++ % 12), 1);
            return calendar.LoadAsync(displayed, displayed);
        });
        await MeasureAsync("Price History open (ListTrackedProductsAsync)", () => priceHistory.ListTrackedProductsAsync(), samples: 15);
        var p = 0;
        await MeasureAsync("Price History product (GetHistoryAsync)", () => priceHistory.GetHistoryAsync(productKeys[p++ % productKeys.Count]));
        await MeasureAsync("Price History open, 34B-2 (OpenReaderAsync + reader.ListTrackedProductsAsync)", async () =>
        {
            var opened = await priceHistory.OpenReaderAsync();
            return await opened.ListTrackedProductsAsync();
        }, samples: 15);
        var reader = await priceHistory.OpenReaderAsync();
        var r = 0;
        await MeasureAsync("Price History product, 34B-2 (reader.GetHistoryAsync)", () => reader.GetHistoryAsync(productKeys[r++ % productKeys.Count]));
        var syncedRepository = new SqliteSyncedPriceObservationRepository(factory);
        await MeasureAsync("  of which: synced table read (GetAllAsync)", () => syncedRepository.GetAllAsync());
        var localRepository = new SqliteSeasonPriceHistoryRepository(factory);
        var q = 0;
        await MeasureAsync("  of which: local intervals (one product)", () => localRepository.GetHistoryAsync(productKeys[q++ % productKeys.Count]));
    }

    private static async Task MeasureAsync<T>(string label, Func<Task<T>> call, int samples = 40)
    {
        for (var w = 0; w < 5; w++)
        {
            await call();
        }

        var caller = new List<double>(samples);
        var total = new List<double>(samples);
        var completedOnReturn = 0;
        for (var s = 0; s < samples; s++)
        {
            var stopwatch = Stopwatch.StartNew();
            var task = call();
            caller.Add(stopwatch.Elapsed.TotalMilliseconds);
            completedOnReturn += task.IsCompleted ? 1 : 0;
            await task;
            total.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        Console.WriteLine($"  {label}");
        Console.WriteLine($"    caller (UI-thread) {Stats.Summary(caller)} | completed on return {completedOnReturn}/{samples}");
        Console.WriteLine($"    total              {Stats.Summary(total)}");
    }

    private static IReadOnlyList<IMigration> Migrations() =>
    [
        new Migration0001CreateSchemaVersionTable(), new Migration0002CreateSourceStateTable(), new Migration0003CreateDatedResultsTable(),
        new Migration0004CreateCatalogProductsTable(), new Migration0005CreateProductHistoryTable(), new Migration0006CreateAssociateDraftsTable(),
        new Migration0007CreateManagerDraftsTable(), new Migration0008CreateFinalizedPassdownsTable(), new Migration0009AddFinalizedPassdownPdfColumns(),
        new Migration0010CreateOperationalNotesTable(), new Migration0011CreateDeepRefreshStatusTable(), new Migration0012CreateSyncEntityStateTable(),
        new Migration0013CreateSyncConflictsTable(), new Migration0014CreateMonthlyErtConfigurationsTable(), new Migration0015CreateErtDateOverridesTable(),
        new Migration0016CreateSeasonPriceHistoryTable(), new Migration0017CreateSyncedPriceObservationsTable(), new Migration0018CreateOfficialReferenceDocumentsTable(),
    ];

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset checkedAt, DateOnly? date = null) =>
        new("Synthetic Source", SourceUrl, checkedAt, status, $"{status}.", RetrievalStrategy.DirectApi, date, status == RetrievalStatus.Verified ? date : null);

    private static async Task<List<string>> SeedAsync(SqliteConnectionFactory factory, int syncedCount)
    {
        var dated = new SqliteDatedResultRepository(factory);
        for (var date = new DateOnly(2024, 1, 1); date <= new DateOnly(2026, 12, 31); date = date.AddDays(1))
        {
            await dated.RecordSuccessAsync(new DatedResultKey(SourceCategory.ParkHours, date), Evidence(RetrievalStatus.Verified, Now, date),
                JsonSerializer.Serialize(new ParkHoursPayload(new TimeOnly(10, 0), new TimeOnly(20, 0), null, null)));
            var products = Enumerable.Range(1, 8)
                .Select(n => new DatedAdmissionProduct($"PKG-{n}", $"Synthetic Ticket {n}", 40m + n, 60m + n, null, date))
                .ToArray();
            await dated.RecordSuccessAsync(new DatedResultKey(SourceCategory.DatedAdmissionTickets, date), Evidence(RetrievalStatus.Verified, Now, date),
                JsonSerializer.Serialize(new DatedAdmissionPayload(products)));
        }

        var catalog = new SqliteCatalogProductRepository(factory);
        var local = new SqliteSeasonPriceHistoryRepository(factory);
        var keys = new List<string>();
        var intervals = new List<SeasonPriceHistoryInterval>();
        foreach (var tier in Tiers)
        {
            foreach (var (kind, suffix) in Kinds)
            {
                var key = CatalogProductKey.Parse($"SeasonPasses:pass-offer:carowinds|{tier}|{kind}");
                var name = $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tier)} {suffix}";
                keys.Add(key.ToString());
                await catalog.UpsertAsync(new CatalogProductUpsert(
                    key, name, ProductLifecycleState.Active, Now.AddYears(-2), Now, Now, 0, null, null, 100m, "$100.00", null, null,
                    DateSpecific: false, ApplicableDate: null, LastEvidence: new SourceEvidence(SeasonSource, SourceUrl, Now, RetrievalStatus.Verified, "Verified.", RetrievalStrategy.Html)));
                for (var sequence = 1; sequence <= 40; sequence++)
                {
                    var first = Now.AddDays(-700 + (sequence * 17));
                    var interval = new SeasonPriceHistoryInterval(key.ToString(), sequence, 80m + (sequence % 7 * 5), PriceKind.StandardCatalog, null, null, name, SeasonSource,
                        first, first.AddDays(16), SeasonPriceHistoryOrigin.Observed);
                    intervals.Add(interval);
                    await local.InsertAsync(interval);
                }
            }
        }

        if (syncedCount > 0)
        {
            var synced = new SqliteSyncedPriceObservationRepository(factory);
            var observations = new List<OfficialPriceObservation>(syncedCount);
            for (var n = 0; observations.Count < syncedCount; n++)
            {
                var source = intervals[n % intervals.Count];
                var device = new Guid(n % 3 + 1, 0, 0, new byte[8]);
                var shifted = source with
                {
                    Sequence = 1 + (n / intervals.Count),
                    FirstObservedAt = source.FirstObservedAt.AddMinutes(n / intervals.Count),
                    LastConfirmedAt = source.LastConfirmedAt.AddMinutes(n / intervals.Count),
                };
                if (OfficialPriceObservation.FromLocalInterval(shifted, device) is { } observation)
                {
                    observations.Add(observation);
                }
            }

            foreach (var batch in observations.Chunk(1_000))
            {
                await synced.MergeAsync(batch, Now);
            }

            Console.WriteLine($"  synced observations stored: {await synced.CountAsync():N0}");
        }

        return keys;
    }
}
