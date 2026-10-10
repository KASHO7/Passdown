namespace Carowinds.Passdown.Application.Concurrency;

/// <summary>
/// Cross-process overlap guard for refresh work (Phase 6B Part A; relocated from Infrastructure in
/// Phase 13B Part K so <see cref="Associate.AssociatePassdownService"/> can depend on the
/// abstraction directly, without Application acquiring a concrete Infrastructure dependency —
/// mirrors the existing repository-port pattern). Exactly one holder at a time: a second caller's
/// <see cref="TryAcquire"/> returns <see langword="false"/> while the first holder is still alive,
/// and never performs migrations/refresh writes of its own as a result. Deliberately not a
/// distributed lock/server — one local exclusive file handle (the concrete
/// <c>Infrastructure.Concurrency.FileScheduledRefreshLock</c>) is enough for a single-machine,
/// per-user scheduled task.
/// </summary>
public interface IScheduledRefreshLock : IDisposable
{
    /// <summary>
    /// Attempts to become the sole holder of this lock. Returns <see langword="false"/> — never
    /// throws — when another holder already has it. Safe to call more than once on the same
    /// instance; a holder that already owns the lock stays the holder.
    /// </summary>
    bool TryAcquire();
}
