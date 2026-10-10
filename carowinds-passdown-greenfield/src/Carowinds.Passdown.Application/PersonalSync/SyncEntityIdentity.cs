using System.Globalization;
using Carowinds.Passdown.Application.Ert;

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

    /// <summary>Monthly ERT identity is <c>Year</c>/<c>Month</c> together (Phase 17B spec Part 5/21) — invariant <c>yyyy-MM</c>, matching <see cref="Ert.MonthlyErtConfiguration.YearMonthKey"/> exactly. Formatted directly from the two integers (not via <see cref="DateOnly"/>) so no day-of-month value is ever implied.</summary>
    public static string ForMonthlyErt(int year, int month) => MonthlyErtConfiguration.FormatKey(year, month);

    public static bool TryParseMonthlyErtId(string entityId, out int year, out int month)
    {
        year = 0;
        month = 0;

        if (entityId is not { Length: 7 } || entityId[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(entityId.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear)
            || !int.TryParse(entityId.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMonth)
            || parsedMonth is < 1 or > 12)
        {
            return false;
        }

        year = parsedYear;
        month = parsedMonth;
        return true;
    }

    /// <summary>ERT date-override identity is the exact operating date (Phase 17B spec Part 21) — same <c>yyyy-MM-dd</c> format as <see cref="ForDraft"/>, since a draft and a date override share the same date-keyed shape.</summary>
    public static string ForErtDateOverride(DateOnly operatingDate) => operatingDate.ToString(DraftDateFormat, CultureInfo.InvariantCulture);

    public static bool TryParseErtDateOverrideId(string entityId, out DateOnly operatingDate) =>
        DateOnly.TryParseExact(entityId, DraftDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out operatingDate);
}
