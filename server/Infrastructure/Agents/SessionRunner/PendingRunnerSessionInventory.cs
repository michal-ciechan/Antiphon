using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// Bootstrap membership only, until the runner supplies an authoritative List. CARD-0701 may
/// replace this bounded loader with committed lifecycle invalidation; no transcript state lives here.
/// </summary>
internal sealed class PendingRunnerSessionInventory(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<Guid> Read(string runnerId)
    {
        // Separate from the directory gate: joined readers of ONE id share that id's load.
        // Another runner's snapshot is never returned.
        lock (_gate)
        {
            if (!_entries.TryGetValue(runnerId, out var entry))
                _entries[runnerId] = entry = new Entry();
            if (clock.GetUtcNow() >= entry.RetryAt)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    entry.Snapshot = db.AgentSessions.AsNoTracking()
                        .Where(s => s.RunnerId == runnerId
                            && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running
                                || s.Status == SessionStatus.Stopping))
                        .Select(s => s.Id).ToFrozenSet();
                    entry.Failure = null;
                }
                catch (Exception ex)
                {
                    entry.Failure = ExceptionDispatchInfo.Capture(ex);
                    logger.LogWarning(ex, "Pending session inventory refresh failed for runner {RunnerId}", runnerId);
                }
                finally
                {
                    entry.RetryAt = clock.GetUtcNow().AddSeconds(5);
                }
            }
            if (entry.Snapshot is not null)
                return entry.Snapshot;
            entry.Failure!.Throw();
            throw new InvalidOperationException("Pending inventory has no completed load.");
        }
    }

    private sealed class Entry
    {
        public FrozenSet<Guid>? Snapshot;
        public ExceptionDispatchInfo? Failure;
        public DateTimeOffset RetryAt;
    }
}
