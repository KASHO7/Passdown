using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// Category-level status for one dated product category (Phase 4B Part E), independent of any
/// individual <see cref="AssociateProduct"/> row. Phase 4A identified a presentation gap: when a
/// category's latest retrieval is <c>Error</c>/<c>NotPublished</c>/<c>Unavailable</c> with no prior
/// successful payload, <see cref="AssociateWorkspace.Products"/> alone renders nothing for it — a
/// screen needs somewhere to say "Fast Lane: Not available for this date" instead of showing an
/// empty section with no explanation. <see cref="LastCheckedAt"/> is the most recent attempt of
/// any outcome (unlike <see cref="AssociateVerificationInfo.LastVerifiedAt"/>, which is
/// success-only) so a pure-failure category can still say when it was last checked.
/// </summary>
public sealed record AssociateProductCategoryStatus(
    SourceCategory Category,
    AssociateVerificationState State,
    DateTimeOffset? LastCheckedAt,
    string? Detail);

/// <summary>
/// The composed Associate Passdown view state for one operating date (spec Phase 4's "compose the
/// Associate view state") — everything a future Avalonia screen binds to, built by merging the
/// saved manual draft with whatever official data is currently cached. Never itself the product of
/// a network call; see <see cref="IAssociatePassdownService.LoadAssociateDateAsync"/>.
/// </summary>
/// <param name="SelectedOperationalNoteSourceStates">
/// Phase 11E Part G: an informational <see cref="OperationalNoteSourceComparisonState"/> per entry in
/// <see cref="Associate.AssociateDraftPayload.SelectedOperationalNotes"/>, keyed by
/// <see cref="AssociateOperationalNoteSelection.SourceOperationalNoteId"/> — computed fresh on every
/// compose, never persisted. Defaults to <see langword="null"/> (treated as empty by every reader) so
/// every pre-11E construction site of this record keeps compiling unchanged.
/// </param>
/// <param name="EarlyRideTime">
/// Phase 17B: the one resolved Early Ride Time value for <see cref="OperatingDate"/> —
/// <see cref="IEffectiveErtResolver.ResolveAsync"/>'s result, computed once in
/// <see cref="AssociatePassdownService.ComposeAsync"/>. Every consumer (Associate screen, Dashboard,
/// PDF, finalized snapshot) reads this rather than resolving ERT itself. The constructor parameter is
/// nullable only so every pre-17B construction site of this record (tests, fixtures) keeps compiling
/// unchanged; the <see cref="EarlyRideTime"/> property itself is never null — a <see langword="null"/>
/// argument maps to <see cref="EffectiveErt.NotConfigured"/>, never a silently fabricated configured value.
/// </param>
public sealed record AssociateWorkspace(
    DateOnly OperatingDate,
    AssociateDraftPayload Draft,
    AssociateParkHoursView ParkHours,
    IReadOnlyList<AssociateProduct> Products,
    IReadOnlyList<AssociateProductCategoryStatus> CategoryStatuses,
    AssociateSaveStatus SaveStatus,
    IReadOnlyDictionary<Guid, OperationalNoteSourceComparisonState>? SelectedOperationalNoteSourceStates = null,
    EffectiveErt? EarlyRideTime = null)
{
    public EffectiveErt EarlyRideTime { get; init; } = EarlyRideTime ?? EffectiveErt.NotConfigured;
}
