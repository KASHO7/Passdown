namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// The composed Associate Passdown view state for one operating date (spec Phase 4's "compose the
/// Associate view state") — everything a future Avalonia screen binds to, built by merging the
/// saved manual draft with whatever official data is currently cached. Never itself the product of
/// a network call; see <see cref="IAssociatePassdownService.LoadAssociateDateAsync"/>.
/// </summary>
public sealed record AssociateWorkspace(
    DateOnly OperatingDate,
    AssociateDraftPayload Draft,
    AssociateParkHoursView ParkHours,
    IReadOnlyList<AssociateProduct> Products,
    AssociateSaveStatus SaveStatus);
