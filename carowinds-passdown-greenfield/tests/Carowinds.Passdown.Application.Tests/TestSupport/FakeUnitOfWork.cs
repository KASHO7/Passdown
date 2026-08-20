using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>No-op unit of work for Application-level tests using in-memory repositories. Real cross-table atomicity is covered by Infrastructure's SqliteUnitOfWorkTests.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public bool Committed { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
{
    public int BeginCount { get; private set; }

    public Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        BeginCount++;
        return Task.FromResult<IUnitOfWork>(new FakeUnitOfWork());
    }
}
