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
    private FrozenSet<Guid>? _snapshot;
    private ExceptionDispatchInfo? _failure;
    private DateTimeOffset _retryAt;

    public IReadOnlyCollection<Guid> Read(string runnerId)
    {
        // Separate from the directory gate: joined readers share this load (including its
        // exception). Recovery never waits for database I/O and the directory rechecks afterward.
        lock (_gate)
        {
            if (clock.GetUtcNow() >= _retryAt)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    _snapshot = db.AgentSessions.AsNoTracking()
                        .Where(s => s.RunnerId == runnerId
                            && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running
                                || s.Status == SessionStatus.Stopping))
                        .Select(s => s.Id).ToFrozenSet();
                    _failure = null;
                }
                catch (Exception ex)
                {
                    _failure = ExceptionDispatchInfo.Capture(ex);
                    logger.LogWarning(ex, "Pending session inventory refresh failed for runner {RunnerId}", runnerId);
                }
                finally
                {
                    // Expiry is measured after completion, including failures; hits never slide it.
                    _retryAt = clock.GetUtcNow().AddSeconds(5);
                }
            }
            if (_snapshot is not null)
                return _snapshot;
            _failure!.Throw();
            throw new InvalidOperationException("Pending inventory has no completed load.");
        }
    }
}
