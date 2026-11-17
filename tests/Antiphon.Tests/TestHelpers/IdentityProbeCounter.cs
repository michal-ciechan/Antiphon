using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1888: counts the transcript ingest identity probe. A command counts when its leading tag is
/// <c>session-state.identity</c> or <c>rt.ingest.prelude</c>; every UUID bound to such a command, in
/// the prelude's <c>uuids</c> array or the EF <c>ANY</c> chunk array, is a probed key. Call
/// <see cref="Reset"/> immediately before the persist under test so earlier persists do not count.
/// </summary>
public sealed class IdentityProbeCounter : DbCommandInterceptor
{
    private static readonly string[] Tags = [TranscriptIngestPrelude.IdentityTag, TranscriptIngestPrelude.PreludeTag];
    private ConcurrentQueue<string[]> _commands = new();

    public int Commands => _commands.Count;
    public int Keys => _commands.Sum(c => c.Length);
    public IReadOnlySet<string> Probed => _commands.SelectMany(c => c).ToHashSet(StringComparer.Ordinal);

    public void Reset() => _commands = new ConcurrentQueue<string[]>();

    private void Hit(DbCommand command)
    {
        var tag = DbCommandMetrics.LeadingTag(command.CommandText);
        if (tag is null || !Tags.Contains(tag, StringComparer.Ordinal)) return;
        _commands.Enqueue(command.Parameters.Cast<DbParameter>()
            .Select(p => p.Value).OfType<IEnumerable<string>>().SelectMany(keys => keys).ToArray());
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result) { Hit(command); return result; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Hit(command); return ValueTask.FromResult(result); }
}
