using System.Globalization;
using System.Text;

namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>
/// Pure text rendering of a <see cref="DiagnosticReport"/> (Phase 7F Part G/I) — no I/O, so this is
/// unit-testable without a database, filesystem, or Windows host. Deterministic, human-readable
/// plain text (Part G "do not overengineer JSON schemas unless the spec requires them").
/// </summary>
public static class DiagnosticReportFormatter
{
    public static string BuildSummaryText(DiagnosticReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Carowinds Passdown — Diagnostic Summary");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Generated (UTC): {report.GeneratedAtUtc:O}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Application version: {report.AppVersion}");
        sb.AppendLine();
        sb.AppendLine("Database:");
        AppendDatabaseLines(sb, report.Database);
        sb.AppendLine();
        sb.AppendLine("Automatic refresh (Task Scheduler):");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  status: {report.ScheduledTask.Status}");
        if (!string.IsNullOrWhiteSpace(report.ScheduledTask.Detail))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  detail: {report.ScheduledTask.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"See source-status.txt for per-source detail ({report.Sources.Count} sources) and logs/ for recent scheduled-refresh activity.");
        return sb.ToString();
    }

    public static string BuildSystemText(DiagnosticReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("System information");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Application version: {report.AppVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion.VersionString}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"64-bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Report generated (UTC): {report.GeneratedAtUtc:O}");
        sb.AppendLine();
        sb.AppendLine("Data location (sanitized — no machine-specific path is recorded here):");
        sb.AppendLine(@"  %LOCALAPPDATA%\Carowinds Passdown");
        return sb.ToString();
    }

    public static string BuildSourceStatusText(DiagnosticReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Source status (local-only — reflects the last completed check, no network call was made to produce this report)");
        sb.AppendLine();

        foreach (var source in report.Sources)
        {
            sb.AppendLine(source.DisplayName);
            sb.AppendLine(CultureInfo.InvariantCulture, $"  status: {source.Status}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  last successful check: {FormatTimestamp(source.LastSuccessAt)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  last attempt: {FormatTimestamp(source.LastAttemptAt)}");
            if (source.ConsecutiveFailureCount > 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  consecutive failures: {source.ConsecutiveFailureCount}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendDatabaseLines(StringBuilder sb, DatabaseDiagnostics database)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"  exists: {(database.Exists ? "yes" : "no")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  schema version: {(database.SchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  file size: {(database.FileSizeBytes is { } bytes ? FormatBytes(bytes) : "unknown")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  WAL present: {(database.WalPresent ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(database.Note))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  note: {database.Note}");
        }
    }

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value is { } timestamp ? timestamp.ToString("O", CultureInfo.InvariantCulture) : "never";

    private static string FormatBytes(long bytes) => $"{bytes / 1024.0 / 1024.0:0.00} MB";
}
