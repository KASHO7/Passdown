using System.Globalization;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The one authoritative mapping between a syncable entity's own domain identity (a <see cref="DateOnly"/>
/// for an Associate draft, a <see cref="Guid"/> for an Operational Note) and its <see cref="SyncEnvelope.EntityId"/>
/// string (Phase 16C §3/§18/§19) — centralized so no other type re-implements date/GUID-to-string
/// formatting for sync purposes. Independent of <see cref="SyncPathService"/>, which composes
/// filesystem paths directly from typed <see cref="DateOnly"/>/<see cref="Guid"/> values and never
/// needed a string identity of its own.
/// </summary>
public static class SyncEntityIdentity
{
    private const string DraftDateFormat = "yyyy-MM-dd";

    /// <summary>Draft identity is the exact operating date (Phase 16C §18) — invariant <c>yyyy-MM-dd</c>, never timezone-converted.</summary>
    public static string ForDraft(DateOnly operatingDate) => operatingDate.ToString(DraftDateFormat, CultureInfo.InvariantCulture);

    public static bool TryParseDraftId(string entityId, out DateOnly operatingDate) =>
        DateOnly.TryParseExact(entityId, DraftDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out operatingDate);

    /// <summary>Note identity is its existing Guid (Phase 16C §19) — preserved exactly, never replaced.</summary>
    public static string ForNote(Guid noteId) => noteId.ToString("D", CultureInfo.InvariantCulture);

    public static bool TryParseNoteId(string entityId, out Guid noteId) =>
        Guid.TryParseExact(entityId, "D", out noteId);

    /// <summary>
    /// A finalized passdown's sync identity is its existing surrogate <c>Id</c> Guid (Phase 16A §B, Phase
    /// 16E §2) — never its <c>PassdownDate</c>. Two independently-finalized records for the same date
    /// (Phase 16E §14) are two distinct sync entities with two distinct ids; the date alone is never
    /// unique enough across devices to serve as identity, even though it is unique on any one device's
    /// own <c>finalized_passdowns</c> table today.
    /// </summary>
    public static string ForFinalizedPassdown(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    public static bool TryParseFinalizedPassdownId(string entityId, out Guid id) =>
        Guid.TryParseExact(entityId, "D", out id);
}
