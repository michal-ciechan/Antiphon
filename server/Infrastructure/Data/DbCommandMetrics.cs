using Antiphon.Observability;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// CARD-1403 S0: counts every executing EF command (reader, scalar and non-query, sync and async)
/// by call site, keyed by the command's leading <c>-- &lt;tag&gt;</c> line when that tag carries an
/// allowlisted prefix; everything else is <c>untagged</c>. Never retains SQL or parameters: only an
/// allowlisted tag string becomes a key, and the key set is capped.
/// </summary>
public sealed class DbCommandMetrics : DbCommandInterceptor, IDisposable
{
    internal const int MaxTagLength = 64;
    private const int MaxTags = 128;
    private const string Overflow = "rt.overflow";
    private static readonly string[] Prefixes = ["session-state.", "rt."];
    private readonly ConcurrentDictionary<string, StrongBox<long>> _tags = new(StringComparer.Ordinal);
    private readonly Meter _meter = new("Antiphon.Db.Commands");
    private readonly Counter<long> _commands;
    private long _total;
    private long _untagged;

    private readonly LiveTiming? _timing;
    public DbCommandMetrics(LiveTiming? timing = null)
    {
        _timing = timing;
        _commands = _meter.CreateCounter<long>("db.commands");
    }

    /// <summary>The allowlisted leading tag of <paramref name="sql"/>, or null.</summary>
    internal static string? LeadingTag(string sql)
    {
        if (!sql.StartsWith("-- ", StringComparison.Ordinal)) return null;
        var end = sql.IndexOfAny(['\r', '\n'], 3);
        var tag = (end < 0 ? sql[3..] : sql[3..end]).Trim();
        if (tag.Length is 0 or > MaxTagLength || !tag.All(IsTagChar))
            return null;
        return Prefixes.Any(p => tag.StartsWith(p, StringComparison.Ordinal)) ? tag : null;
    }

    /// <summary>
    /// A tag-safe call-site suffix: lower-case letters and digits only, so a caller-derived name
    /// (a member or file name) can extend an allowlisted tag without widening the key alphabet.
    /// </summary>
    internal static string TagSegment(string name) =>
        new(name.ToLowerInvariant().Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')).ToArray());

    private static bool IsTagChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-';

    private void Record(DbCommand command)
    {
        Interlocked.Increment(ref _total);
        if (LeadingTag(command.CommandText) is not { } tag)
        {
            Interlocked.Increment(ref _untagged);
            _commands.Add(1, new KeyValuePair<string, object?>("call_site", "untagged"));
            return;
        }

        if (!_tags.ContainsKey(tag) && _tags.Count >= MaxTags) tag = Overflow;
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

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, true); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    { _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, true); return ValueTask.FromResult(result); }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    { _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, true); return result; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    { _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, true); return ValueTask.FromResult(result); }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    { _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, true); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    { _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, true); return ValueTask.FromResult(result); }
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, false);
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    { CommandFailed(command, eventData); return Task.CompletedTask; }
    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) =>
        _timing?.RecordExternal(ExternalOperation.Database, eventData.Duration, false);
    public override Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
    { CommandCanceled(command, eventData); return Task.CompletedTask; }

    public Snapshot Read() => new(Interlocked.Read(ref _total), Interlocked.Read(ref _untagged),
        _tags.ToDictionary(p => p.Key, p => Interlocked.Read(ref p.Value.Value), StringComparer.Ordinal));

    public void Dispose() => _meter.Dispose();

    /// <param name="Total">Every executing command: reader, scalar and non-query.</param>
    /// <param name="Untagged">Commands with no allowlisted leading tag.</param>
    /// <param name="Tags">Per allowlisted call-site tag.</param>
    public sealed record Snapshot(long Total, long Untagged, IReadOnlyDictionary<string, long> Tags);
}
