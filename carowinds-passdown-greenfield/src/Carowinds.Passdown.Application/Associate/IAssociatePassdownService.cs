namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// Application-layer Associate Passdown workflow (Phase 4A): loads/saves the per-date manual
/// draft and composes it with cached official data. The Phase 4B Avalonia screen binds to this —
/// it contains no Avalonia/UI-command concerns itself.
/// </summary>
public interface IAssociatePassdownService
{
    /// <summary>
    /// Local-only date load (spec §6.3's "fast date changes"): reads the saved draft plus whatever
    /// dated/catalog data is already cached and composes the view. Never calls
    /// <see cref="Refresh.IRefreshOrchestrator"/>, HTTP, or Playwright — changing/loading a date
    /// must never itself trigger retrieval.
    /// </summary>
    Task<AssociateWorkspace> LoadAssociateDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit "Check this date online" (spec §6.3): runs the existing manual-date refresh for
    /// <paramref name="operatingDate"/>, then recomposes the view from whatever is cached
    /// afterward — success or failure. All manual draft fields, product include/exclude, and
    /// display-price overrides are preserved regardless of outcome (spec §8.3/§6.7).
    /// </summary>
    Task<AssociateWorkspace> CheckAssociateDateOnlineAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="draft"/> as the manual draft for <paramref name="operatingDate"/>
    /// (spec §8.3's "save a separate associate draft per date"). Presentation-neutral: a future
    /// debounced ViewModel decides when to call this and how to render the returned
    /// <see cref="AssociateSaveStatus"/> while awaiting it.
    /// </summary>
    Task<AssociateSaveStatus> SaveDraftAsync(DateOnly operatingDate, AssociateDraftPayload draft, CancellationToken cancellationToken = default);
}
