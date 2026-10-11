using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Phase 18A (SEC-001/SEC-002): structural validation of a sync payload that has already deserialized
/// successfully. The sync folder is untrusted input (docs/phase18-comprehensive-audit.md threat model,
/// boundary G/H) — System.Text.Json leaves a missing/explicit-<c>null</c> positional-record member as
/// <see langword="null"/> and accepts an integer for an enum even when no such member exists, so
/// "deserialized without a <c>JsonException</c>" is not the same as "safe to persist". Every check here
/// is one the app's own export path always satisfies, so a payload a real device wrote is never
/// rejected. A payload that fails is counted as malformed and left untouched in the folder (never
/// imported, never deleted) — exactly like unparseable JSON.
/// </summary>
public static class SyncPayloadValidator
{
    public static bool IsValidDraft(AssociateDraftPayload payload) =>
        payload is not null
        && payload.ManualHoursOverride is not null
        && payload.PrivateBuyout is not null
        && payload.OperationalNotes is not null && payload.OperationalNotes.All(note => note is not null)
        && (payload.DailyFeatureType is null || Enum.IsDefined(payload.DailyFeatureType.Value))
        && payload.ProductOverrides is not null && payload.ProductOverrides.All(pair => pair.Key is not null && pair.Value is not null)
        && payload.CustomProducts is not null && payload.CustomProducts.All(product => product is { Id: not null, DisplayName: not null })
        && payload.SectionSelection is not null
        && payload.SelectedOperationalNotes is not null && payload.SelectedOperationalNotes.All(selection => selection is not null);

    /// <summary>The payload's own <see cref="OperationalNoteSyncPayload.Id"/> must be the note the file name/envelope identify — otherwise one file could rewrite a different note, bypassing that note's own conflict detection.</summary>
    public static bool IsValidNote(OperationalNoteSyncPayload payload, Guid expectedId) =>
        payload is not null
        && payload.Id == expectedId
        && !string.IsNullOrWhiteSpace(payload.Title)
        && payload.Title.Trim().Length <= OperationalNote.MaxTitleLength
        && (payload.Body is null || payload.Body.Length <= OperationalNote.MaxBodyLength)
        && Enum.IsDefined(payload.Category)
        && Enum.IsDefined(payload.Priority)
        && Enum.IsDefined(payload.Status);

    /// <summary>Year/month must match the identity the file name/envelope carry — otherwise <c>2026-05.json</c> could silently overwrite a different month's configuration.</summary>
    public static bool IsValidMonthlyErt(MonthlyErtSyncPayload payload, int expectedYear, int expectedMonth) =>
        payload is not null
        && payload.Year == expectedYear
        && payload.Month == expectedMonth
        && Enum.IsDefined(payload.State)
        && payload.Rides is not null && payload.Rides.All(ride => ride is not null);

    public static bool IsValidErtDateOverride(ErtDateOverrideSyncPayload payload, DateOnly expectedDate) =>
        payload is not null
        && payload.OperatingDate == expectedDate
        && Enum.IsDefined(payload.State)
        && payload.Rides is not null && payload.Rides.All(ride => ride is not null);

    /// <summary>Identity (id/date) and content-hash agreement are already checked by the caller; this covers the members that would otherwise throw or persist <see langword="null"/> into NOT NULL columns.</summary>
    public static bool IsValidFinalizedPassdown(FinalizedPassdownSyncPayload payload) =>
        payload is not null
        && !string.IsNullOrWhiteSpace(payload.SnapshotJson)
        && !string.IsNullOrWhiteSpace(payload.ContentHash)
        && !string.IsNullOrWhiteSpace(payload.PdfSha256)
        && payload.PdfSizeBytes >= 0
        && Enum.IsDefined(payload.Origin);

    /// <summary>
    /// The exception types a payload decode can legitimately raise for hostile/corrupt input:
    /// <see cref="System.Text.Json.JsonException"/> (bad JSON/shape), <see cref="InvalidOperationException"/>
    /// (a <c>null</c> payload, or a <see cref="System.Text.Json.JsonDocument"/> property probe against a
    /// non-object root), <see cref="ArgumentException"/> (a null/blank string handed to a serializer
    /// guard), and <see cref="NotSupportedException"/> (an unsupported type shape). Anything else is a
    /// genuine bug and is deliberately left to propagate.
    /// </summary>
    public static bool IsPayloadDecodeFailure(Exception exception) =>
        exception is System.Text.Json.JsonException or InvalidOperationException or ArgumentException or NotSupportedException;
}
