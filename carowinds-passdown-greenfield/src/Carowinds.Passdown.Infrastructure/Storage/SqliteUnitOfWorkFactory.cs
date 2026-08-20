using Carowinds.Passdown.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>Opens a <see cref="SqliteUnitOfWork"/> on a fresh connection from <see cref="SqliteConnectionFactory"/>.</summary>
public sealed class SqliteUnitOfWorkFactory(SqliteConnectionFactory connectionFactory) : IUnitOfWorkFactory
{
    public async Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return new SqliteUnitOfWork(connection, transaction);
    }
}
