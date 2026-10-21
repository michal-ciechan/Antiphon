using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// CARD-1403 S0: counts executing EF commands by call site, keyed by the command's leading
/// <c>-- &lt;tag&gt;</c> line when that tag carries an allowlisted prefix. Never retains SQL or
/// parameters: only an allowlisted tag string becomes a key.
/// </summary>
public sealed class DbCommandMetrics : DbCommandInterceptor, IDisposable
{
    private static readonly string[] Prefixes = ["session-state.", "rt."];
    private readonly ConcurrentDictionary<string, StrongBox<long>> _tags = new(StringComparer.Ordinal);
    private readonly Meter _meter = new("Antiphon.Db.Commands");
    private readonly Counter<long> _commands;
    private long _total;

    public DbCommandMetrics() => _commands = _meter.CreateCounter<long>("db.commands");

    /// <summary>The allowlisted leading tag of <paramref name="sql"/>, or null.</summary>
    internal static string? LeadingTag(string sql)
    {
        if (!sql.StartsWith("-- ", StringComparison.Ordinal)) return null;
        var end = sql.IndexOfAny(['\r', '\n'], 3);
        var tag = (end < 0 ? sql[3..] : sql[3..end]).Trim();
        if (tag.Length is 0 or > 64 || !tag.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-'))
            return null;
        return Prefixes.Any(p => tag.StartsWith(p, StringComparison.Ordinal)) ? tag : null;
    }

    private void Record(DbCommand command)
    {
        if (LeadingTag(command.CommandText) is not { } tag) return;
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _tags.GetOrAdd(tag, _ => new StrongBox<long>()).Value);
        _commands.Add(1, new KeyValuePair<string, object?>("call_site", tag));
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

    public Snapshot Read() => new(Interlocked.Read(ref _total), 0,
        _tags.ToDictionary(p => p.Key, p => Interlocked.Read(ref p.Value.Value), StringComparer.Ordinal));

    public void Dispose() => _meter.Dispose();

    /// <param name="Total">Every executing command: reader, scalar and non-query.</param>
    /// <param name="Untagged">Commands with no allowlisted leading tag.</param>
    /// <param name="Tags">Per allowlisted call-site tag.</param>
    public sealed record Snapshot(long Total, long Untagged, IReadOnlyDictionary<string, long> Tags);
}
