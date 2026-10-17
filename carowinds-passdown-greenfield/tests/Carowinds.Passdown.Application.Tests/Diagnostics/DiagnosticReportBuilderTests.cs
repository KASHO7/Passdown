using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Tests.Diagnostics;

/// <summary>
/// Phase 7F Part D/I: <see cref="DiagnosticReportBuilder"/> must compose a report purely from
/// already-persisted state and injected local-metadata providers — never by calling
/// <see cref="SourceRegistration.ExecuteAsync"/> (which would mean a diagnostic export silently
/// performed a live network refresh, exactly what Part I forbids).
/// </summary>
public class DiagnosticReportBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    private static DatedSourceRegistration<FakePayload> BuildRegistration(string sourceId, SourceCategory category, InMemoryDatedResultRepository repository) =>
        new(
            new FakeOfficialDataSource<FakePayload>(sourceId, category, (_, _) => throw new InvalidOperationException(
                "DiagnosticReportBuilder must never execute a source registration — a diagnostic export must be local-only.")),
            repository);

    private static DiagnosticReportBuilder BuildSut(
        IReadOnlyList<SourceRegistration> registrations,
        InMemorySourceStateRepository? sourceStateRepository = null,
        DatabaseDiagnostics? database = null,
        ScheduledTaskDiagnostics? scheduledTask = null,
        IReadOnlyList<string>? logLines = null) =>
        new(
            registrations,
            sourceStateRepository ?? new InMemorySourceStateRepository(),
            new FakeDatabaseDiagnosticsProvider(database ?? new DatabaseDiagnostics(false, null, null, false, null)),
            new FakeScheduledTaskStatusProvider(scheduledTask ?? new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.NotFound, null)),
            new FakeScheduledRefreshLogProvider(logLines ?? []),
            new FakeClock(Now));

    // Phase 19B §49: lifecycle and season-tag counts only — never product names or payloads.
    [Fact]
    public async Task BuildAsync_WithCatalogRepository_ReportsLifecycleAndSeasonTagCounts()
    {
        var catalog = new InMemoryCatalogProductRepository();
        var reconciliation = new ProductReconciliationService(catalog, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory());
        ObservedProduct Promo(string name) => new(
            CatalogProductKey.FromSourceAndName(SourceCategory.Promotions, "promotions", name), name, 10m, "$10", null, null, false, null);
        await reconciliation.ReconcileAsync(SourceCategory.Promotions, FakeEvidence.Verified(Now.AddHours(-8)),
            [Promo("Bring-A-Friend Ticket"), Promo("SCarowinds Bundles"), Promo("Ended Offer"), Promo("Fading Offer")]);
        await reconciliation.ReconcileAsync(SourceCategory.Promotions, FakeEvidence.Verified(Now.AddHours(-4)),
            [Promo("Bring-A-Friend Ticket"), Promo("SCarowinds Bundles"), Promo("Fading Offer")]);
        await reconciliation.ReconcileAsync(SourceCategory.Promotions, FakeEvidence.Verified(Now),
            [Promo("Bring-A-Friend Ticket"), Promo("SCarowinds Bundles"), Promo("WinterFest Ticket")]);
        var promotions = new CatalogSourceRegistration(
            new FakeOfficialDataSource<CatalogPayload>("promotions", SourceCategory.Promotions, (_, _) => throw new InvalidOperationException("must not execute")),
            reconciliation);

        var sut = new DiagnosticReportBuilder(
            [promotions],
            new InMemorySourceStateRepository(),
            new FakeDatabaseDiagnosticsProvider(new DatabaseDiagnostics(false, null, null, false, null)),
            new FakeScheduledTaskStatusProvider(new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.NotFound, null)),
            new FakeScheduledRefreshLogProvider([]),
            new FakeClock(Now),
            catalogProductRepository: catalog);

        var report = await sut.BuildAsync();

        Assert.Equal(new CatalogLifecycleDiagnostics(
            Active: 3, PendingConfirmation: 1, Inactive: 1,
            ActiveCarowinds: 0, ActiveSCarowinds: 1, ActiveWinterFest: 1, ActiveUnclassified: 1), report.CatalogLifecycle);
        var summary = DiagnosticReportFormatter.BuildSummaryText(report);
        Assert.Contains("inactive (retired/expired, kept for history): 1", summary);
        Assert.DoesNotContain("Ended Offer", summary);
    }

    [Fact]
    public async Task BuildAsync_IncludesABoundedAppLogExcerpt_WhenAnAppLogProviderIsRegistered()
    {
        var appLog = new FakeInteractiveAppLogProvider(["app line"]);
        var sut = new DiagnosticReportBuilder(
            [],
            new InMemorySourceStateRepository(),
            new FakeDatabaseDiagnosticsProvider(new DatabaseDiagnostics(false, null, null, false, null)),
            new FakeScheduledTaskStatusProvider(new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.NotFound, null)),
            new FakeScheduledRefreshLogProvider(["scheduled line"]),
            new FakeClock(Now),
            appLog);

        var report = await sut.BuildAsync();

        Assert.Equal(["app line"], report.RecentAppLogLines);
        Assert.Equal(["scheduled line"], report.RecentLogLines);
        Assert.Equal(200, appLog.RequestedMaxLines);
    }

    [Fact]
    public async Task BuildAsync_WithoutAnAppLogProvider_HasAnEmptyAppLogExcerpt()
    {
        var report = await BuildSut([]).BuildAsync();

        Assert.Empty(report.RecentAppLogLines);
    }

    [Fact]
    public async Task BuildAsync_SourceNeverChecked_ReportsNeverChecked()
    {
        var registration = BuildRegistration("park-hours.official", SourceCategory.ParkHours, new InMemoryDatedResultRepository());
        var sut = BuildSut([registration]);

        var report = await sut.BuildAsync();

        var source = Assert.Single(report.Sources);
        Assert.Equal(DiagnosticSourceStatus.NeverChecked, source.Status);
        Assert.Null(source.LastAttemptAt);
        Assert.Null(source.LastSuccessAt);
        Assert.Equal(0, source.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task BuildAsync_SourceWithNoConsecutiveFailures_ReportsOk()
    {
        var registration = BuildRegistration("park-hours.official", SourceCategory.ParkHours, new InMemoryDatedResultRepository());
        var stateRepository = new InMemorySourceStateRepository();
        await stateRepository.SaveAsync(new Application.Persistence.SourceStateRecord(
            "park-hours.official", SourceCategory.ParkHours, Now, Now, null, null, 0));

        var sut = BuildSut([registration], stateRepository);

        var report = await sut.BuildAsync();

        Assert.Equal(DiagnosticSourceStatus.Ok, Assert.Single(report.Sources).Status);
    }

    [Fact]
    public async Task BuildAsync_SourceWithConsecutiveFailures_ReportsError()
    {
        var registration = BuildRegistration("park-hours.official", SourceCategory.ParkHours, new InMemoryDatedResultRepository());
        var stateRepository = new InMemorySourceStateRepository();
        await stateRepository.SaveAsync(new Application.Persistence.SourceStateRecord(
            "park-hours.official", SourceCategory.ParkHours, Now, null, null, null, 2));

        var sut = BuildSut([registration], stateRepository);

        var report = await sut.BuildAsync();

        var source = Assert.Single(report.Sources);
        Assert.Equal(DiagnosticSourceStatus.Error, source.Status);
        Assert.Equal(2, source.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task BuildAsync_TwoSourcesSharingACategory_GetDistinctDisplayNames()
    {
        var seasonPassAddOns = BuildRegistration("season-pass-add-ons.official", SourceCategory.AddOns, new InMemoryDatedResultRepository());
        var generalAddOns = BuildRegistration("general-add-ons.official", SourceCategory.AddOns, new InMemoryDatedResultRepository());

        var sut = BuildSut([seasonPassAddOns, generalAddOns]);

        var report = await sut.BuildAsync();

        var names = report.Sources.Select(s => s.DisplayName).ToList();
        Assert.Equal(2, names.Distinct().Count());
    }

    [Fact]
    public async Task BuildAsync_NeverCallsSourceRegistrationExecuteAsync()
    {
        // BuildRegistration's handler throws if ever invoked (see above) — a passing test proves
        // no source's ExecuteAsync ran, i.e. no network refresh was triggered building the report.
        var registration = BuildRegistration("dated-admission.official", SourceCategory.DatedAdmissionTickets, new InMemoryDatedResultRepository());
        var sut = BuildSut([registration]);

        await sut.BuildAsync();
    }

    [Fact]
    public async Task BuildAsync_ComposesDatabaseAndScheduledTaskAndLogInformationFromInjectedProviders()
    {
        var database = new DatabaseDiagnostics(true, 7, 12345, false, null);
        var scheduledTask = new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Enabled, null);
        var logLines = new List<string> { "line one", "line two" };

        var sut = BuildSut([], database: database, scheduledTask: scheduledTask, logLines: logLines);

        var report = await sut.BuildAsync();

        Assert.Equal(database, report.Database);
        Assert.Equal(scheduledTask, report.ScheduledTask);
        Assert.Equal(logLines, report.RecentLogLines);
        Assert.Equal(Now, report.GeneratedAtUtc);
    }
}
