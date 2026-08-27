using System.IO.Compression;
using System.Text;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Infrastructure.Diagnostics;

/// <summary>
/// Builds the diagnostic export ZIP entirely in memory (Phase 7F Part G) — never writes a temporary
/// file to the app directory or anywhere else; the caller (Desktop) decides the destination via a
/// native save picker, exactly like <see cref="Carowinds.Passdown.Documents.Associate.IAssociatePassdownPdfExportService"/>
/// hands PDF bytes to <c>IPdfSaveFilePicker</c>. Contains only <see cref="DiagnosticReportFormatter"/>'s
/// text output and a sanitized log excerpt — never the SQLite database file itself (Part D).
/// </summary>
public sealed class DiagnosticExportService(IDiagnosticReportBuilder reportBuilder, IClock clock) : IDiagnosticExportService
{
    public async Task<DiagnosticExportResult> BuildZipAsync(CancellationToken cancellationToken = default)
    {
        var report = await reportBuilder.BuildAsync(cancellationToken).ConfigureAwait(false);

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteTextEntryAsync(archive, "summary.txt", DiagnosticReportFormatter.BuildSummaryText(report), cancellationToken).ConfigureAwait(false);
            await WriteTextEntryAsync(archive, "system.txt", DiagnosticReportFormatter.BuildSystemText(report), cancellationToken).ConfigureAwait(false);
            await WriteTextEntryAsync(archive, "source-status.txt", DiagnosticReportFormatter.BuildSourceStatusText(report), cancellationToken).ConfigureAwait(false);

            if (report.RecentLogLines.Count > 0)
            {
                var logContent = string.Join(Environment.NewLine, report.RecentLogLines);
                await WriteTextEntryAsync(archive, $"logs/scheduled-refresh-{report.GeneratedAtUtc:yyyyMMdd}.log", logContent, cancellationToken).ConfigureAwait(false);
            }
        }

        // yyyyMMdd-HHmm is digits and dashes only — always a safe file name component, no path
        // separators or characters a destination filesystem could reject (Part S "output filename
        // is safe").
        var fileName = $"Carowinds-Operations-Diagnostics-{clock.UtcNow:yyyyMMdd-HHmm}.zip";
        return new DiagnosticExportResult(memoryStream.ToArray(), fileName);
    }

    private static async Task WriteTextEntryAsync(ZipArchive archive, string entryName, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await entryStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
