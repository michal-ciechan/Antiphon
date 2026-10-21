using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1403 D-2: a deterministic command budget for a hot path. Register it on the test context,
/// open a <see cref="Begin"/> scope around the code under test and assert the count, so a change
/// that reintroduces N+1 fails with a number and the list of tags seen, not a flaky elapsed time.
/// With <paramref name="tagPrefixes"/> only commands whose leading tag starts with one of them
/// count. <paramref name="delay"/> (D-3) is injected before every counted command while a scope is
/// open; it is evidence for the test output, never the assertion.
/// </summary>
public sealed class QueryBudget(TimeSpan? delay = null, params string[] tagPrefixes) : DbCommandInterceptor
{
    private volatile Scope? _scope;

    public Scope Begin() => _scope = new Scope(this);

    private bool Counts(DbCommand command, out string tag)
    {
        tag = LeadingTag(command.CommandText) ?? "untagged";
        if (_scope is null) return false;
        return tagPrefixes.Length == 0 || tagPrefixes.Any(p => tag.StartsWith(p, StringComparison.Ordinal));
    }

    private static string? LeadingTag(string sql) => DbCommandMetrics.LeadingTag(sql)
        ?? (sql.StartsWith("-- ", StringComparison.Ordinal) ? sql[3..].Split('\n', 2)[0].Trim() : null);

    private void Hit(DbCommand command)
    {
        if (!Counts(command, out var tag)) return;
        _scope?.Seen.Enqueue(tag);
        if (delay is { } d) Thread.Sleep(d);
    }

    private async ValueTask HitAsync(DbCommand command, CancellationToken ct)
    {
        if (!Counts(command, out var tag)) return;
        _scope?.Seen.Enqueue(tag);
        if (delay is { } d) await Task.Delay(d, ct);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result) { Hit(command); return result; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { await HitAsync(command, cancellationToken); return result; }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result) { Hit(command); return result; }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { await HitAsync(command, cancellationToken); return result; }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result) { Hit(command); return result; }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { await HitAsync(command, cancellationToken); return result; }

    public sealed class Scope(QueryBudget owner) : IDisposable
    {
        internal ConcurrentQueue<string> Seen { get; } = new();
        public int Count => Seen.Count;
        public IReadOnlyList<string> Tags => Seen.ToArray();

        /// <summary>Throws when more than <paramref name="budget"/> commands ran, naming every tag seen.</summary>
        public void ShouldBeWithin(int budget)
        {
            var tags = Tags;
            if (tags.Count > budget)
                throw new QueryBudgetExceededException(
                    $"{tags.Count} commands, budget {budget}: {string.Join(", ", tags)}");
        }

        public void Dispose()
        {
            if (ReferenceEquals(owner._scope, this)) owner._scope = null;
        }
    }
}

public sealed class QueryBudgetExceededException(string message) : Exception(message);
