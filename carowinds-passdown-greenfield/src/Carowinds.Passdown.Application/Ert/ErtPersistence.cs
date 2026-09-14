using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Ert;

/// <summary>
/// Persistence contract for monthly Early Ride Time configurations (Phase 17B spec Parts 2/19/20).
/// One row per <see cref="MonthlyErtConfiguration.YearMonthKey"/>. <see cref="DeleteAsync"/> exists
/// solely as the Personal Sync tombstone primitive (spec Part 23), mirroring
/// <see cref="OperationalNotes.IOperationalNoteRepository.DeleteAsync"/> — no product "delete" UI
/// calls it; the product-facing way to say "no ERT" is <see cref="Domain.Ert.ErtConfigurationState.NoErt"/>,
/// not row absence.
/// </summary>
public interface IMonthlyErtRepository
{
    Task<MonthlyErtConfiguration?> GetAsync(int year, int month, CancellationToken cancellationToken = default);

    Task UpsertAsync(MonthlyErtConfiguration configuration, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    Task DeleteAsync(int year, int month, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>Every persisted monthly configuration — used for Personal Sync export/bootstrap (spec Part 25), never on the hot resolution path (spec Part 43).</summary>
    Task<IReadOnlyList<MonthlyErtConfiguration>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persistence contract for per-date Early Ride Time overrides (Phase 17B spec Parts 3/19/20) — same shape and rationale as <see cref="IMonthlyErtRepository"/>, keyed by <see cref="ErtDateOverride.OperatingDate"/> instead.</summary>
public interface IErtDateOverrideRepository
{
    Task<ErtDateOverride?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    Task UpsertAsync(ErtDateOverride dateOverride, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    Task DeleteAsync(DateOnly operatingDate, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ErtDateOverride>> ListAsync(CancellationToken cancellationToken = default);
}
