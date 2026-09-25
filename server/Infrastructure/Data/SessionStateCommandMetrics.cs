using System.Data.Common;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>Counts tagged EF read attempts, including retries. Never retains SQL or parameters.</summary>
public sealed class SessionStateCommandMetrics : DbCommandInterceptor, IDisposable
{
    private static readonly string[] Paths = ["seed", "pins", "fallback", "identity", "binding"];
    private static readonly string[] Tags = Paths.Select(p => "-- session-state." + p).ToArray();
    private readonly long[] _counts = new long[Paths.Length];
    private readonly Meter _meter = new("Antiphon.SessionState.Commands");
    private readonly Counter<long> _commands;

    public SessionStateCommandMetrics() => _commands = _meter.CreateCounter<long>("session_state.ef_read_attempts");

    private void Record(DbCommand command)
    {
        for (var i = 0; i < Tags.Length; i++)
        {
            if (!command.CommandText.StartsWith(Tags[i], StringComparison.Ordinal)) continue;
            Interlocked.Increment(ref _counts[i]);
            _commands.Add(1, new KeyValuePair<string, object?>("query_path", Paths[i]));
            return;
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public IReadOnlyDictionary<string, long> Snapshot() => Enumerable.Range(0, Paths.Length)
        .ToDictionary(i => Paths[i], i => Interlocked.Read(ref _counts[i]));

    public void Dispose() => _meter.Dispose();
}
