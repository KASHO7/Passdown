using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Infrastructure.Files;

/// <summary>
/// Phase 18B PRIV-002: the app-owned temp folder used when History's "Open Final PDF" hands a frozen
/// PDF to the OS viewer. Generation and "Save PDF Copy" never use temp files (bytes are generated in
/// memory and written straight to the picker-chosen file), so this folder is the only temp-PDF location
/// the app has.
/// <list type="bullet">
/// <item>Every open gets its own name, <c>Carowinds-Operations-yyyy-MM-dd.view-&lt;32 hex&gt;.pdf</c>, so
/// re-opening a date while a viewer (commonly Acrobat on Windows) still holds the previous copy locked
/// no longer fails.</item>
/// <item>A write that fails or is cancelled deletes its own partial file; a viewer that could not be
/// started has its file deleted immediately.</item>
/// <item>A successfully opened file cannot be deleted right away (the viewer is reading it), so
/// <see cref="CleanStale"/> — run at startup and before each open — removes files older than
/// <see cref="StaleAfter"/>. A file still locked by a viewer is skipped and retried later.</item>
/// </list>
/// Only names matching <see cref="IsOwnedTempFileName"/> directly inside <see cref="Directory"/>
/// are ever deleted — never any other <c>*.pdf</c>, never a subfolder, never a user-chosen save
/// location.
/// </summary>
public sealed partial class HistoryPdfTempFileStore(string directory, TimeProvider? timeProvider = null)
{
    /// <summary>24 hours: long enough that a document a person is still reading is not pulled away the same day.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    public static string DefaultDirectory { get; } = Path.Combine(Path.GetTempPath(), "Carowinds Passdown", "History");

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string Directory { get; } = Path.GetFullPath(directory);

    /// <summary>
    /// The suggested name is always the app's own <c>Carowinds-Operations-yyyy-MM-dd.pdf</c>
    /// (<c>FinalizedPassdownPdfRetrievalService</c>); anything else is rejected rather than turned
    /// into a path.
    /// </summary>
    public string CreateUniquePath(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        if (!SuggestedFileName().IsMatch(suggestedFileName))
        {
            throw new ArgumentException("Unexpected history PDF file name.", nameof(suggestedFileName));
        }

        return Path.Combine(Directory, $"{Path.GetFileNameWithoutExtension(suggestedFileName)}.view-{Guid.NewGuid():N}.pdf");
    }

    /// <summary>Writes <paramref name="pdfBytes"/> to a new unique file; on any failure (including cancellation) the partial file is removed before the exception propagates.</summary>
    public async Task<string> WriteAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        var path = CreateUniquePath(suggestedFileName);
        System.IO.Directory.CreateDirectory(Directory);

        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 81920, FileOptions.Asynchronous);
            await stream.WriteAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        return path;
    }

    /// <summary>Deletes <paramref name="path"/> only if it is one of this store's own files; never throws.</summary>
    public bool TryDelete(string path)
    {
        try
        {
            if (!IsOwnedPath(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <returns>The number of stale temp PDFs deleted. Never throws.</returns>
    public int CleanStale()
    {
        var deleted = 0;
        try
        {
            var directoryInfo = new DirectoryInfo(Directory);
            if (!directoryInfo.Exists || directoryInfo.LinkTarget is not null)
            {
                return 0;
            }

            var cutoffUtc = _time.GetUtcNow().UtcDateTime - StaleAfter;
            foreach (var file in directoryInfo.EnumerateFiles("*.pdf", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (IsOwnedPath(file.FullName) && file.LinkTarget is null && file.LastWriteTimeUtc < cutoffUtc)
                    {
                        file.Delete();
                        deleted++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still open in a viewer, or already gone — try again next time.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Best-effort hygiene only.
        }

        return deleted;
    }

    /// <summary>The per-open name, or the single fixed name pre-18B builds reused (<c>Carowinds-Operations-yyyy-MM-dd.pdf</c>) in this same app-owned folder.</summary>
    public static bool IsOwnedTempFileName(string fileName) => OwnedFileName().IsMatch(fileName);

    private bool IsOwnedPath(string path)
    {
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Directory), comparison)
            && IsOwnedTempFileName(Path.GetFileName(full));
    }

    [GeneratedRegex(@"^Carowinds-Operations-[0-9]{4}-[0-9]{2}-[0-9]{2}\.pdf$", RegexOptions.CultureInvariant)]
    private static partial Regex SuggestedFileName();

    [GeneratedRegex(@"^Carowinds-Operations-[0-9]{4}-[0-9]{2}-[0-9]{2}(\.view-[0-9a-f]{32})?\.pdf$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnedFileName();
}
