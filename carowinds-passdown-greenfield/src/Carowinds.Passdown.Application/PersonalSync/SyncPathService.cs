using System.Globalization;
using Carowinds.Passdown.Application.Ert;

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
    private const string MonthlyErtFolderName = "monthly-ert";
    private const string ErtDateOverridesFolderName = "ert-date-overrides";

    /// <summary>Invariant <c>yyyy-MM</c> format for monthly ERT filenames (Phase 17B) — matches <see cref="Ert.MonthlyErtConfiguration.YearMonthKey"/>.</summary>
    private const string MonthFormat = "yyyy-MM";

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

    /// <summary>The <c>history/</c> directory itself (Phase 16E §5) — mirrors <see cref="GetDraftsDirectoryPath"/>/<see cref="GetNotesDirectoryPath"/>; never scanned recursively, never assumed to contain only canonical filenames.</summary>
    public static string GetHistoryDirectoryPath(string selectedRootPath) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), HistoryFolderName);

    /// <summary>
    /// A finalized passdown's identity is its Guid, not its date (Phase 16A §B, Phase 16E §2) — the
    /// filename includes both because two independent devices can each finalize the same
    /// <paramref name="passdownDate"/> with a different <paramref name="id"/> (Phase 16E §6/§14:
    /// "audit whether operating date alone is guaranteed unique... if not, use a naming scheme that
    /// cannot collide"). The date-only naming Phase 16A §E originally illustrated could not survive that
    /// case — two devices' independent finalizations for the same date would silently overwrite each
    /// other's file. Filenames stay inspectable (date is still the leading, human-readable segment) and
    /// deterministic (same input always maps to the same name).
    /// </summary>
    public static string GetHistoryJsonFilePath(string selectedRootPath, DateOnly passdownDate, Guid id) =>
        Path.Combine(GetHistoryDirectoryPath(selectedRootPath), $"{FormatHistoryFileStem(passdownDate, id)}.json");

    public static string GetHistoryPdfFilePath(string selectedRootPath, DateOnly passdownDate, Guid id) =>
        Path.Combine(GetHistoryDirectoryPath(selectedRootPath), $"{FormatHistoryFileStem(passdownDate, id)}.pdf");

    /// <summary>The <c>monthly-ert/</c> directory itself (Phase 17B) — mirrors <see cref="GetDraftsDirectoryPath"/>/<see cref="GetNotesDirectoryPath"/>.</summary>
    public static string GetMonthlyErtsDirectoryPath(string selectedRootPath) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), MonthlyErtFolderName);

    public static string GetMonthlyErtFilePath(string selectedRootPath, int year, int month) =>
        Path.Combine(GetMonthlyErtsDirectoryPath(selectedRootPath), $"{FormatMonth(year, month)}.json");

    /// <summary>The <c>ert-date-overrides/</c> directory itself (Phase 17B) — mirrors <see cref="GetDraftsDirectoryPath"/>/<see cref="GetNotesDirectoryPath"/>.</summary>
    public static string GetErtDateOverridesDirectoryPath(string selectedRootPath) =>
        Path.Combine(GetSyncFolderPath(selectedRootPath), ErtDateOverridesFolderName);

    public static string GetErtDateOverrideFilePath(string selectedRootPath, DateOnly operatingDate) =>
        Path.Combine(GetErtDateOverridesDirectoryPath(selectedRootPath), $"{FormatDate(operatingDate)}.json");

    /// <summary>
    /// The inverse of <see cref="GetHistoryJsonFilePath"/>/<see cref="GetHistoryPdfFilePath"/>'s naming
    /// (Phase 16E §5/§6) — a canonical history filename is always exactly <c>yyyy-MM-dd-&lt;guid-D&gt;</c>
    /// (10 + 1 + 36 = 47 characters). Anything else (an OneDrive conflict-copy name, a stray file, a
    /// pre-16E date-only name from an unreleased build) fails to parse and must be left untouched by the
    /// caller (Phase 16C §28's "unexpected filename ignored" rule, extended to History).
    /// </summary>
    public static bool TryParseHistoryFileName(string fileNameWithoutExtension, out DateOnly passdownDate, out Guid id)
    {
        passdownDate = default;
        id = Guid.Empty;

        const int expectedLength = DateLength + 1 + GuidLength;
        if (fileNameWithoutExtension is not { Length: expectedLength } stem || stem[DateLength] != '-')
        {
            return false;
        }

        var datePart = stem[..DateLength];
        var guidPart = stem[(DateLength + 1)..];

        return DateOnly.TryParseExact(datePart, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out passdownDate)
            && Guid.TryParseExact(guidPart, "D", out id);
    }

    private const int DateLength = 10; // "yyyy-MM-dd"
    private const int GuidLength = 36; // Guid.ToString("D") length

    private static string FormatHistoryFileStem(DateOnly passdownDate, Guid id) => $"{FormatDate(passdownDate)}-{FormatGuid(id)}";

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string FormatMonth(int year, int month) => MonthlyErtConfiguration.FormatKey(year, month);

    /// <summary>Lowercase, hyphenated 32-hex-digit form ("D") — the exact stable representation the Phase 16A folder-layout examples use.</summary>
    private static string FormatGuid(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
