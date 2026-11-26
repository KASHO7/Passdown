using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Seasons;

namespace Carowinds.Passdown.Application.Seasons;

/// <summary>
/// Phase 31: the official season context for one selected date, as shown on the Associate screen.
/// <see cref="Schedule"/> is the full last-known-good event schedule (used for past-season product
/// rules); <see cref="Resolution"/> is that schedule resolved for <see cref="OperatingDate"/>.
/// </summary>
public sealed record AssociateSeasonView(
    DateOnly OperatingDate,
    SeasonResolution Resolution,
    OperatingEventSchedule? Schedule,
    OfficialDocumentStatus ScheduleStatus,
    ParkDayOperation ParkDay)
{
    public static AssociateSeasonView NotChecked(DateOnly operatingDate) =>
        new(operatingDate, SeasonResolution.Unknown(operatingDate), null, OfficialDocumentStatus.NotChecked, ParkDayOperation.Unknown);

    public bool ScheduleKnown => Resolution.ScheduleKnown;
}

public interface IOperatingSeasonService
{
    /// <summary>Reads the cached official event schedule only — never retrieves online.</summary>
    Task<AssociateSeasonView> ResolveAsync(DateOnly operatingDate, ParkDayOperation parkDay, CancellationToken cancellationToken = default);
}

public sealed class OperatingSeasonService(IOfficialReferenceDocumentRepository documents) : IOperatingSeasonService
{
    public async Task<AssociateSeasonView> ResolveAsync(DateOnly operatingDate, ParkDayOperation parkDay, CancellationToken cancellationToken = default)
    {
        var record = await documents.GetAsync(OfficialReferenceDocumentIds.OperatingEvents, cancellationToken).ConfigureAwait(false);
        var schedule = OfficialSeasonJson.TryDeserialize<OperatingEventSchedule>(record?.SuccessPayloadJson);
        var resolution = SeasonResolver.Resolve(operatingDate, schedule, parkDay);
        return new AssociateSeasonView(operatingDate, resolution, schedule, OfficialDocumentStatus.From(record), parkDay);
    }
}
