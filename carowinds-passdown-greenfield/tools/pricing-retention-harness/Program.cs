// Pre-RC.15 Windows pricing-retention verification harness (test only; docs/pre-rc15-post-closing-pricing-retention.md §11).
//
//   carowinds-pricing-retention-harness seed   [--database <path>]
//   carowinds-pricing-retention-harness verify [--database <path>]
//
// seed   — for the fixed synthetic date below, runs the PRODUCTION DatedAdmissionSource / DatedFastLaneSource through
//          the PRODUCTION DatedSourceRegistration into the PRODUCTION SqliteDatedResultRepository twice: at 3:10 PM ET
//          (the calendar lists the date: prices verified) and at 10:15 PM ET (a successful calendar answer that no
//          longer lists the date). Then checks that the 3:10 PM prices and timestamp survived.
// verify — read-only: repeats the checks (use after restarting the app).
//
// Safety: every official answer comes from embedded synthetic fixtures through a stub HTTP client — no network
// request is possible. The harness has its own clock; the system clock is never read for the scenario or changed.
// Without --database it writes only to the signed-in user's Carowinds Passdown database, and only when that user is
// an approved disposable test account (TestAccountPolicy: CarowindsTest, CarowindsTestB), the app is closed, the database already exists at the current schema and
// the date has no stored dated-admission or Fast Lane result yet.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Updates.Installation;
using Carowinds.PricingRetentionHarness;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

var syntheticDate = new DateOnly(2026, 8, 20);                                  // matches the committed fixtures
var verifiedAtUtc = new DateTimeOffset(2026, 8, 20, 19, 10, 0, TimeSpan.Zero);  // 3:10 PM EDT
var afterClosingUtc = new DateTimeOffset(2026, 8, 21, 2, 15, 0, TimeSpan.Zero); // 10:15 PM EDT, same Eastern date
(string Name, decimal Price)[] expectedAdmission = [("Park Ticket", 49.00m)];
(string Name, decimal Price)[] expectedFastLane = [("FAST LANE RESERVE", 49.00m), ("FAST LANE ULTIMATE", 85.00m)];

if (args.Length == 0 || args[0] is not ("seed" or "verify"))
{
    Console.WriteLine("Usage: carowinds-pricing-retention-harness seed|verify [--database <path>]");
    return 2;
}

var command = args[0];
var explicitPath = args.Length >= 3 && args[1] == "--database" ? Path.GetFullPath(args[2]) : null;
var defaultPath = Path.Combine(AppPaths.LocalApplicationDataDirectory, new StorageOptions().DatabaseFileName);
var databasePath = explicitPath ?? defaultPath;

Console.WriteLine("Carowinds pricing-retention verification harness (TEST ONLY — synthetic data, no network)");
Console.WriteLine($"Database: {databasePath}");
Console.WriteLine($"Synthetic operating date: {syntheticDate:dddd, MMMM d, yyyy}");
Console.WriteLine();

if (string.Equals(databasePath, defaultPath, StringComparison.OrdinalIgnoreCase)
    && !TestAccountPolicy.IsApproved(Environment.UserName))
{
    return Refuse(TestAccountPolicy.RefusalMessage(Environment.UserName));
}

var migrations = typeof(IMigration).Assembly.GetTypes()
    .Where(t => typeof(IMigration).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) is not null)
    .Select(t => (IMigration)Activator.CreateInstance(t)!)
    .OrderBy(m => m.Version)
    .ToList();
var currentSchema = migrations.Max(m => m.Version);

if (!File.Exists(databasePath))
{
    if (explicitPath is null || string.Equals(databasePath, defaultPath, StringComparison.OrdinalIgnoreCase) || command != "seed")
    {
        return Refuse("No Carowinds Passdown database yet. Open the test app once, close it, then run this again.");
    }

    // Build-host self-check only (--database to a new file): create an empty database with the production migrations.
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    new MigrationRunner(new SqliteConnectionFactory(databasePath), migrations, NullLogger<MigrationRunner>.Instance).Run();
    Console.WriteLine($"Created an empty schema-{currentSchema} database with the production migrations.");
}

var schema = ReadUserVersion(databasePath);
if (schema != currentSchema)
{
    return Refuse($"Database schema is {schema}; this harness matches the test app's schema {currentSchema}. Use the matching test app build.");
}

var repository = new SqliteDatedResultRepository(new SqliteConnectionFactory(databasePath));
var admissionKey = new DatedResultKey(SourceCategory.DatedAdmissionTickets, syntheticDate);
var fastLaneKey = new DatedResultKey(SourceCategory.DatedFastLane, syntheticDate);

if (command == "seed")
{
    if (Process.GetProcessesByName(SystemUpdaterEnvironment.AppProcessName).Length > 0)
    {
        return Refuse("Close Carowinds Operations first (the harness must not write while the app is open).");
    }

    if (await repository.GetAsync(admissionKey) is not null || await repository.GetAsync(fastLaneKey) is not null)
    {
        return Refuse($"{syntheticDate:MMMM d, yyyy} already has stored dated-admission or Fast Lane results. Nothing was changed. Run 'verify' to inspect them.");
    }

    var clock = new HarnessClock(verifiedAtUtc);
    var http = new FixtureOnlyAccessoClient();
    var admission = new DatedSourceRegistration<DatedAdmissionPayload>(new DatedAdmissionSource(http, clock), repository);
    var fastLane = new DatedSourceRegistration<DatedFastLanePayload>(new DatedFastLaneSource(http, clock), repository);

    Console.WriteLine("Step 1 — 3:10 PM ET: the official calendar lists the date (synthetic fixture).");
    Report(await admission.ExecuteAsync(syntheticDate, CancellationToken.None));
    Report(await fastLane.ExecuteAsync(syntheticDate, CancellationToken.None));

    Console.WriteLine("Step 2 — 10:15 PM ET, after closing: a successful calendar answer that no longer lists the date (synthetic fixture).");
    clock.UtcNow = afterClosingUtc;
    http.AfterClosing = true;
    Report(await admission.ExecuteAsync(syntheticDate, CancellationToken.None));
    Report(await fastLane.ExecuteAsync(syntheticDate, CancellationToken.None));

    Console.WriteLine($"Official answers served: {http.Answers}, all from embedded synthetic fixtures. Network requests: 0.");
    Console.WriteLine();
}

Console.WriteLine("Stored results (what the app reads):");
var failures = 0;
failures += await CheckAsync("Daily Admission", admissionKey, json => JsonSerializer.Deserialize<DatedAdmissionPayload>(json)!.Products.Select(p => (p.DisplayName, p.ExactDatePrice)), expectedAdmission);
failures += await CheckAsync("Fast Lane", fastLaneKey, json => JsonSerializer.Deserialize<DatedFastLanePayload>(json)!.Products.Select(p => (p.DisplayName, p.ExactDatePrice)), expectedFastLane);

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine("RESULT: PASS — the prices verified at 3:10 PM ET were kept, with their original verification time,");
    Console.WriteLine("        and the later 'date no longer listed' answer is recorded as the latest check.");
    Console.WriteLine($"Next: open the app, select {syntheticDate:MMMM d, yyyy} and follow the Windows checklist.");
    return 0;
}

Console.WriteLine($"RESULT: FAIL — {failures} check(s) failed (see above).");
return 1;

async Task<int> CheckAsync(string label, DatedResultKey key, Func<string, IEnumerable<(string Name, decimal Price)>> read, (string Name, decimal Price)[] expected)
{
    var record = await repository.GetAsync(key);
    var failed = 0;
    void Check(bool ok, string text)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}: {text}");
        failed += ok ? 0 : 1;
    }

    if (record is null)
    {
        Check(false, "no stored result for the synthetic date (run 'seed' first)");
        return failed;
    }

    var success = record.SuccessEvidence;
    var prices = record.SuccessPayloadJson is { } json ? read(json).OrderBy(p => p.Name, StringComparer.Ordinal).ToList() : [];
    Check(success?.Status == RetrievalStatus.Verified, $"last verified result is '{success?.Status}' (expected Verified)");
    Check(success?.TimeChecked == verifiedAtUtc, $"verified at {Eastern(success?.TimeChecked)} (expected {Eastern(verifiedAtUtc)})");
    Check(prices.SequenceEqual(expected.OrderBy(p => p.Name, StringComparer.Ordinal)),
        $"prices {Describe(prices)} (expected {Describe(expected)})");
    var latest = record.LastAttemptEvidence;
    Check(latest.Status is RetrievalStatus.Unavailable or RetrievalStatus.NotPublished && latest.TimeChecked >= afterClosingUtc,
        $"latest check '{latest.Status}' at {Eastern(latest.TimeChecked)} — {latest.Detail}");
    return failed;
}

static void Report(SourceOperationResult result) =>
    Console.WriteLine($"  {result.Category}: {result.Status} — {result.Detail}");

static string Describe(IEnumerable<(string Name, decimal Price)> prices) =>
    string.Join(", ", prices.Select(p => $"{p.Name} ${p.Price.ToString("0.00", CultureInfo.InvariantCulture)}")) is { Length: > 0 } text ? text : "none";

static string Eastern(DateTimeOffset? instant) =>
    instant is { } value ? EasternTimeZone.ConvertFromUtc(value).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) + " ET" : "never";

static long ReadUserVersion(string path)
{
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA user_version;";
    return (long)command.ExecuteScalar()!;
}

static int Refuse(string reason)
{
    Console.WriteLine($"REFUSED: {reason}");
    return 2;
}

/// <summary>The harness's own clock: the scenario's 3:10 PM / 10:15 PM instants. The system clock is never changed.</summary>
internal sealed class HarnessClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}

/// <summary>
/// Answers the accesso calendar POSTs the production sources send, only from embedded synthetic fixtures — the
/// harness has no HTTP client, so a real request is impossible. Before closing: admission window and Fast Lane
/// single-day entry list the date; after closing: both answer without it (the Nov 27, 2026 live observation).
/// </summary>
internal sealed class FixtureOnlyAccessoClient : IRetrievalHttpClient
{
    public bool AfterClosing { get; set; }

    public int Answers { get; private set; }

    public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The harness never makes GET requests.");

    public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using var body = JsonDocument.Parse(jsonBody);
        var root = body.RootElement;
        var keyword = root.GetProperty("keyword").GetString();
        var singleDay = root.GetProperty("start_date").GetString() == root.GetProperty("end_date").GetString();
        var fixture = (keyword, singleDay, AfterClosing) switch
        {
            ("SnapWizardDailyTickets", _, false) => "fixtures/dated-admission/published-window.json",
            ("SnapWizardDailyTickets", _, true) => "fixtures/dated-admission/after-closing-window.json",
            ("FastLaneOnly", true, false) => "fixtures/dated-fast-lane/published-today.json",
            ("FastLaneOnly", false, false) => "fixtures/dated-fast-lane/horizon-window.json",
            ("FastLaneOnly", true, true) => "fixtures/dated-fast-lane/no-dates-found.json",
            ("FastLaneOnly", false, true) => "fixtures/dated-fast-lane/after-closing-horizon-window.json",
            _ => throw new InvalidOperationException($"Unexpected request keyword '{keyword}'."),
        };

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fixture)
            ?? throw new InvalidOperationException($"Missing embedded fixture {fixture}.");
        using var reader = new StreamReader(stream);
        Answers++;
        return Task.FromResult(new RetrievalHttpResponse(HttpStatusCode.OK, "application/json", reader.ReadToEnd()));
    }
}
