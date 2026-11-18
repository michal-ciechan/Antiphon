using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>CARD-1987 red witness. Completions are accepted and none of them are counted.</summary>
public sealed class DbCallSiteMetrics : DbCommandInterceptor
{
    public const int MaxSites = 256;
    private readonly Tx _transactions = new();

    public DbCallSiteMetrics(int commandInterval = 64, int transactionInterval = 16)
    {
        _ = commandInterval;
        _ = transactionInterval;
    }

    public DbTransactionInterceptor Transactions => _transactions;

    public sealed record Reading(
        IReadOnlyDictionary<string, long> Sites,
        long Unknown,
        long Overflow,
        long Begin,
        long Commit,
        long Rollback,
        long Savepoint,
        long Release);

    public Reading Read() => new(new Dictionary<string, long>(StringComparer.Ordinal), 0, 0, 0, 0, 0, 0, 0);

    internal void ObserveSite(string? site)
    {
        _ = site;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        => result;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(result);

    private sealed class Tx : DbTransactionInterceptor
    {
        public override DbTransaction TransactionStarted(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
            => result;

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) { }

        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) { }

        public override void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) { }

        public override void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData) { }

        public override void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) { }
    }
}
