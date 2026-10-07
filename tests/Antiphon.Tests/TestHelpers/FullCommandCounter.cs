using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Counts every command a context sends: readers, non-queries and scalars, sync and async.
/// Executed callbacks are not counted, so one round-trip is one statement.
/// </summary>
public sealed class FullCommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    public int Total => _commands.Count;

    public IReadOnlyList<string> Commands => _commands.ToArray();

    public void Reset()
    {
        while (_commands.TryDequeue(out _))
        {
        }
    }

    public string Roster()
    {
        var lines = _commands.ToArray();
        return string.Join('\n', lines.Select((sql, index) =>
        {
            var flat = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (flat.Length > 180)
                flat = flat[..180];
            return $"{index + 1}. {flat}";
        }));
    }

    private void Record(DbCommand command) => _commands.Enqueue(command.CommandText);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
