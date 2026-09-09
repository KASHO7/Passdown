using System.Globalization;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The one authoritative mapping from a user-selected sync root to every deterministic file path
/// personal sync uses (Phase 16A §E, Phase 16B §7). Pure string composition — no filesystem access —
/// so every caller (folder validation, the settings screen, and future 16C+ entity sync) resolves
/// identical paths without duplicating this logic. No entity data is read or written through this
/// type; it only computes where such data would live.
/// </summary>
public static class SyncPathService
{
    /// <summary>The app-owned subfolder created inside whatever root the user selects (Phase 16A §E) — the app never writes records directly into the user's chosen folder itself.</summary>
    public const string SyncFolderName = "Carowinds Operations Sync";

    private const string ManifestFileName = "manifest.json";
    private const string DraftsFolderName = "drafts";
    private const string NotesFolderName = "notes";
    private const string HistoryFolderName = "history";

    /// <summary>Invariant, sortable date format used for every date-identified sync filename (drafts, history) — never the current culture's format.</summary>
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>The app-owned folder under the user's selected root — every other path in this type is a descendant of this one.</summary>
    public static string GetSyncFolderPath(string selectedRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRootPath);
        return Path.Combine(selectedRootPath, SyncFolderName);
    }

    public static string GetManifestFilePath(string selectedRootPath) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), ManifestFileName);

    /// <summary>The <c>drafts/</c> directory itself (Phase 16C §8/§10) — the one place a sync pass enumerates candidate draft files; never scanned recursively, never assumed to contain only canonical filenames (§28).</summary>
    public static string GetDraftsDirectoryPath(string selectedRootPath) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), DraftsFolderName);

    /// <summary>The <c>notes/</c> directory itself (Phase 16C §9/§10) — mirrors <see cref="GetDraftsDirectoryPath"/> for Operational Notes.</summary>
    public static string GetNotesDirectoryPath(string selectedRootPath) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), NotesFolderName);

    public static string GetDraftFilePath(string selectedRootPath, DateOnly operatingDate) =>
        Path.Combine(GetDraftsDirectoryPath(selectedRootPath), $"{FormatDate(operatingDate)}.json");

    public static string GetOperationalNoteFilePath(string selectedRootPath, Guid noteId) =>
        Path.Combine(GetNotesDirectoryPath(selectedRootPath), $"{FormatGuid(noteId)}.json");

    public static string GetHistoryJsonFilePath(string selectedRootPath, DateOnly passdownDate) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), HistoryFolderName, $"{FormatDate(passdownDate)}.json");

    public static string GetHistoryPdfFilePath(string selectedRootPath, DateOnly passdownDate) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), HistoryFolderName, $"{FormatDate(passdownDate)}.pdf");

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Lowercase, hyphenated 32-hex-digit form ("D") — the exact stable representation the Phase 16A folder-layout examples use.</summary>
    private static string FormatGuid(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
