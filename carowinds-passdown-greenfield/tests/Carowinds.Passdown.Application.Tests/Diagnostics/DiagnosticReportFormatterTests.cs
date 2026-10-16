using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Tests.Diagnostics;

/// <summary>
/// Phase 7F Part G/I: <see cref="DiagnosticReportFormatter"/> is pure text rendering — these tests
/// never touch a database, filesystem, or Windows host, and specifically assert none of the
/// excluded-by-design content (Part D: drafts, notes, secrets) can leak in even by accident, since
/// the formatter only ever sees a <see cref="DiagnosticReport"/> that structurally cannot carry it.
/// </summary>
public class DiagnosticReportFormatterTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 22, 10, 30, 0, TimeSpan.Zero);

    private static DiagnosticReport BuildReport(
        IReadOnlyList<SourceStatusSummary>? sources = null,
        DatabaseDiagnostics? database = null,
        ScheduledTaskDiagnostics? scheduledTask = null,
        IReadOnlyList<string>? logLines = null) =>
        new(
            "0.1.0-dev",
            GeneratedAt,
            sources ?? [],
            database ?? new DatabaseDiagnostics(true, 7, 45_000, false, null),
            scheduledTask ?? new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Enabled, null),
            logLines ?? []);

    [Fact]
    public void BuildSummaryText_IncludesVersionAndSchemaVersionAndGeneratedTimestamp()
    {
        var text = DiagnosticReportFormatter.BuildSummaryText(BuildReport());

        Assert.Contains("0.1.0-dev", text);
        Assert.Contains("schema version: 7", text);
        Assert.Contains(GeneratedAt.ToString("O"), text);
    }

    [Fact]
    public void BuildSummaryText_DatabaseMissing_ReportsExistsNoWithoutSchemaVersion()
    {
        var text = DiagnosticReportFormatter.BuildSummaryText(BuildReport(database: new DatabaseDiagnostics(false, null, null, false, null)));

        Assert.Contains("exists: no", text);
        Assert.Contains("schema version: unknown", text);
    }

    [Fact]
    public void BuildSourceStatusText_ListsEachSourceWithStatusAndTimestamps()
    {
        var sources = new List<SourceStatusSummary>
        {
            new("park-hours.official", "Park Hours", SourceCategory.ParkHours, DiagnosticSourceStatus.Ok, GeneratedAt, GeneratedAt, 0),
            new("dated-admission.official", "Dated Admission", SourceCategory.DatedAdmissionTickets, DiagnosticSourceStatus.Error, GeneratedAt, null, 3),
        };

        var text = DiagnosticReportFormatter.BuildSourceStatusText(BuildReport(sources: sources));

        Assert.Contains("Park Hours", text);
        Assert.Contains("Dated Admission", text);
        Assert.Contains("consecutive failures: 3", text);
    }

    [Fact]
    public void BuildSourceStatusText_NeverChecked_ReportsNeverForTimestamps()
    {
        var sources = new List<SourceStatusSummary>
        {
            new("park-hours.official", "Park Hours", SourceCategory.ParkHours, DiagnosticSourceStatus.NeverChecked, null, null, 0),
        };

        var text = DiagnosticReportFormatter.BuildSourceStatusText(BuildReport(sources: sources));

        Assert.Contains("last successful check: never", text);
        Assert.Contains("last attempt: never", text);
    }

    [Fact]
    public void BuildSystemText_NeverIncludesTheMachineUserProfilePath()
    {
        var text = DiagnosticReportFormatter.BuildSystemText(BuildReport());

        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%LOCALAPPDATA%\Carowinds Passdown", text);
    }

    [Fact]
    public void BuildSummaryText_ScheduledTaskNotAvailableOnThisPlatform_IncludesDetail()
    {
        var text = DiagnosticReportFormatter.BuildSummaryText(BuildReport(
            scheduledTask: new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.NotAvailableOnThisPlatform, "Task Scheduler is a Windows-only feature.")));

        Assert.Contains("NotAvailableOnThisPlatform", text);
        Assert.Contains("Task Scheduler is a Windows-only feature.", text);
    }

    [Fact]
    public void BuildSummaryText_SeasonPriceHistory_IsCountsAndATimestampOnly()
    {
        var report = BuildReport() with
        {
            SeasonPriceHistory = new Persistence.SeasonPriceHistorySummary(4, 9, new DateTimeOffset(2026, 9, 19, 16, 0, 0, TimeSpan.Zero)),
        };

        var text = DiagnosticReportFormatter.BuildSummaryText(report);

        Assert.Contains("Season price history:", text);
        Assert.Contains("tracked products with history: 4", text);
        Assert.Contains("recorded price states: 9", text);
        Assert.Contains("most recent price change first seen: 2026-09-19T16:00:00", text);
        Assert.DoesNotContain("$", text);
    }

    [Fact]
    public void BuildSummaryText_WithoutSeasonPriceHistory_OmitsTheSection() =>
        Assert.DoesNotContain("Season price history:", DiagnosticReportFormatter.BuildSummaryText(BuildReport()));
}
