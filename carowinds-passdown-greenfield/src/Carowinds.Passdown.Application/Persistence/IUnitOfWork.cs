namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// An atomic unit of work spanning multiple repository writes — e.g. a catalog
/// row and its history snapshot, which must commit together or not at all
/// (spec §7.3/§6.2's "writes are atomic"). Free of any storage-specific type so
/// Application code can request atomicity without depending on Infrastructure;
/// concrete repository implementations recognize their own concrete unit-of-work
/// type and participate in it, and fall back to their own connection when none
/// is supplied.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Commits every write made through this unit of work. Not committing before disposal rolls the writes back.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
