using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton flap counter for reconciliation's third pass (CARD-0056): how many times each session
/// has been written back from Failed to Running during this server's uptime.
///
/// <para>It lives outside <see cref="SessionReconciliationService"/> because that service is scoped
/// — a fresh instance per sweep — so a counter held there would reset every 15 seconds and bound
/// nothing. In-memory on purpose: the cap exists to stop a loop between two live components (the
/// reconciler re-adopting, something else re-failing), and a restart genuinely is a fresh start.
/// </para>
/// </summary>
public sealed class SessionReAdoptionState
{
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    /// <summary>
    /// Registers one re-adoption of <paramref name="sessionId"/> and reports whether it is allowed.
    /// <c>Allowed</c> is false once the session has already been re-adopted <paramref name="cap"/>
    /// times — the caller must then change nothing and escalate, because a session that keeps
    /// flapping is a state for a human rather than a loop to run forever. <c>Count</c> is the total
    /// including this attempt, so an escalation can say which one it is.
    /// </summary>
    public (bool Allowed, int Count) TryRegisterReAdoption(Guid sessionId, int cap)
    {
        var count = _counts.AddOrUpdate(sessionId, 1, (_, existing) => existing + 1);
        return (count <= Math.Max(0, cap), count);
    }

    /// <summary>How many times this session has been re-adopted so far (diagnostics/tests).</summary>
    public int CountFor(Guid sessionId) => _counts.TryGetValue(sessionId, out var count) ? count : 0;
}
