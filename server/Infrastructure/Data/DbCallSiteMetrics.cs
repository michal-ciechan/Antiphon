using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// CARD-1987 M3. Counts every transaction completion, and samples one command in 64 and one
/// transaction event in 16. The kept frame is the closest <c>Antiphon.</c> method outside
/// <c>Infrastructure.Data</c>. An async state machine (<c>&lt;RecordActivityAsync&gt;d__12.MoveNext</c>)
/// is reported as <c>AgentSessionRuntime.RecordActivityAsync</c>. Keys are code identifiers.
/// </summary>
public sealed class DbCallSiteMetrics : DbCommandInterceptor
{
    public const int MaxSites = 256;
    private readonly int _commandInterval;
    private readonly int _transactionInterval;
    private readonly Tx _transactions;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _sites = new(StringComparer.Ordinal);
    private long _commands;
    private long _txEvents;
    private long _unknown;
    private long _overflow;
    private long _begin, _commit, _rollback, _savepoint, _release;

    public DbCallSiteMetrics(int commandInterval = 64, int transactionInterval = 16)
    {
        _commandInterval = commandInterval < 1 ? 1 : commandInterval;
        _transactionInterval = transactionInterval < 1 ? 1 : transactionInterval;
        _transactions = new Tx(this);
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

    public Reading Read()
    {
        lock (_gate)
        {
            return new Reading(new Dictionary<string, long>(_sites, StringComparer.Ordinal),
                Interlocked.Read(ref _unknown), Interlocked.Read(ref _overflow),
                Interlocked.Read(ref _begin), Interlocked.Read(ref _commit), Interlocked.Read(ref _rollback),
                Interlocked.Read(ref _savepoint), Interlocked.Read(ref _release));
        }
    }

    /// <summary>Record an already-sampled site. A null frame counts as <c>unknown</c>.</summary>
    internal void ObserveSite(string? site)
    {
        if (site is null)
        {
            Interlocked.Increment(ref _unknown);
            return;
        }
        lock (_gate)
        {
            if (_sites.ContainsKey(site))
            {
                _sites[site]++;
                return;
            }
            if (_sites.Count >= MaxSites)
            {
                Interlocked.Increment(ref _overflow);
                return;
            }
            _sites[site] = 1;
        }
    }

    private void NoteCommand()
    {
        var n = Interlocked.Increment(ref _commands);
        if (n % _commandInterval == 0) ObserveSite(Capture());
    }

    private void NoteTransaction(int kind)
    {
        switch (kind)
        {
            case 0: Interlocked.Increment(ref _begin); break;
            case 1: Interlocked.Increment(ref _commit); break;
            case 2: Interlocked.Increment(ref _rollback); break;
            case 3: Interlocked.Increment(ref _savepoint); break;
            default: Interlocked.Increment(ref _release); break;
        }
        var n = Interlocked.Increment(ref _txEvents);
        if (n % _transactionInterval == 0) ObserveSite(Capture());
    }

    private static string? Capture()
    {
        var trace = new StackTrace(false);
        for (var i = 0; i < trace.FrameCount; i++)
        {
            var method = trace.GetFrame(i)?.GetMethod();
            if (method is not null && TryKey(method, out var key)) return key;
        }
        return null;
    }

    internal static bool TryKey(MethodBase method, out string key)
    {
        key = "";
        var type = method.DeclaringType;
        var name = method.Name;
        if (type is null) return false;
        if (name == "MoveNext" && type.Name.Length > 2 && type.Name[0] == '<')
        {
            var end = type.Name.IndexOf('>');
            if (end > 1 && type.DeclaringType is not null)
            {
                name = type.Name[1..end];
                type = type.DeclaringType;
            }
        }
        var ns = type.Namespace ?? "";
        if (!ns.StartsWith("Antiphon.", StringComparison.Ordinal)) return false;
        if (ns.Equals("Antiphon.Server.Infrastructure.Data", StringComparison.Ordinal)
            || ns.StartsWith("Antiphon.Server.Infrastructure.Data.", StringComparison.Ordinal))
            return false;
        if (type.Name.Length == 0 || type.Name[0] == '<') return false;
        key = type.Name + "." + name;
        return true;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    { NoteCommand(); return result; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { NoteCommand(); return ValueTask.FromResult(result); }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result)
    { NoteCommand(); return result; }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { NoteCommand(); return ValueTask.FromResult(result); }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result)
    { NoteCommand(); return result; }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { NoteCommand(); return ValueTask.FromResult(result); }

    private sealed class Tx(DbCallSiteMetrics owner) : DbTransactionInterceptor
    {
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        { owner.NoteTransaction(0); return result; }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        { owner.NoteTransaction(0); return ValueTask.FromResult(result); }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => owner.NoteTransaction(1);
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { owner.NoteTransaction(1); return Task.CompletedTask; }

        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => owner.NoteTransaction(2);
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { owner.NoteTransaction(2); return Task.CompletedTask; }

        public override void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) => owner.NoteTransaction(3);
        public override Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData,
            CancellationToken cancellationToken = default)
        { owner.NoteTransaction(3); return Task.CompletedTask; }

        public override void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData) => owner.NoteTransaction(4);
        public override Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData,
            CancellationToken cancellationToken = default)
        { owner.NoteTransaction(4); return Task.CompletedTask; }

        public override void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) => owner.NoteTransaction(2);
        public override Task RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData,
            CancellationToken cancellationToken = default)
        { owner.NoteTransaction(2); return Task.CompletedTask; }
    }
}
