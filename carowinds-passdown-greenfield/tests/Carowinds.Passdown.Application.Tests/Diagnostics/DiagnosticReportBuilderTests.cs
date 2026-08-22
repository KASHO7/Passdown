using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;

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
