using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>Whether a registered source is checked per-date or once for the whole catalog (spec §6.2).</summary>
public enum SourceScope
{
    Dated,
    Catalog,
}

/// <summary>
/// What the refresh orchestrator needs to know about one completed Phase 3 source adapter
/// (Phase 3G Part A): its identity/category (for freshness planning), its scope (dated vs.
/// catalog, for how the orchestrator expands it into operations), whether it needs the shared
/// job browser session (Part E), and — via <see cref="ExecuteAsync"/> — how its successful
/// payload is persisted or reconciled. Two concrete, typed subclasses
/// (<see cref="DatedSourceRegistration{TPayload}"/>, <see cref="CatalogSourceRegistration"/>)
/// cover every currently-registered source; this stays a small typed-adapter shape rather than a
/// generic reflection-driven framework, per Part A's "do not create a giant generic framework".
/// </summary>
public abstract class SourceRegistration(string sourceId, SourceCategory category, bool requiresBrowserSession)
{
    public string SourceId { get; } = sourceId;

    public SourceCategory Category { get; } = category;

    public abstract SourceScope Scope { get; }

    /// <summary>True when this source's <see cref="IOfficialDataSource{TPayload}"/> needs a shared job browser session (spec §6.4/Part E). False for every currently-implemented adapter — all are DirectApi or Html.</summary>
    public bool RequiresBrowserSession { get; } = requiresBrowserSession;

    /// <summary>
    /// Retrieves and persists/reconciles one operation for this source: one date for a
    /// <see cref="SourceScope.Dated"/> registration, or the whole catalog for a
    /// <see cref="SourceScope.Catalog"/> registration (<paramref name="requestedDate"/> must be
    /// null in that case). Never swallows <see cref="OperationCanceledException"/> — the caller
    /// (the orchestrator) is responsible for distinguishing cancellation from an ordinary failure
    /// (Part D).
    /// </summary>
    public abstract Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken);
}
