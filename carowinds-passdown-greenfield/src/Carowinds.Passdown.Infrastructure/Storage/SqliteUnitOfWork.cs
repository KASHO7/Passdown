using Carowinds.Passdown.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// SQLite-backed <see cref="IUnitOfWork"/>: one connection plus one transaction,
/// shared by every repository call it's passed to, so writes to more than one
/// table commit together or not at all (spec §7.3/§6.2). Not committing before
/// disposal rolls back.
/// </summary>
public sealed class SqliteUnitOfWork : IUnitOfWork
{
    internal SqliteConnection Connection { get; }
    internal SqliteTransaction Transaction { get; }

    private bool _committed;
    private bool _disposed;

    internal SqliteUnitOfWork(SqliteConnection connection, SqliteTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_committed)
        {
            await Transaction.RollbackAsync().ConfigureAwait(false);
        }

        await Transaction.DisposeAsync().ConfigureAwait(false);
        await Connection.DisposeAsync().ConfigureAwait(false);
    }
}
