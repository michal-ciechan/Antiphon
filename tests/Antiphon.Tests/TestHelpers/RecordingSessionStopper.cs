using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Records the sessions a task action asked to stop. The point of the seam: a Cancel that only
/// relabels the row leaves a Claude running against the run's cost ceiling, so "did it actually
/// stop the delegate" is the thing worth asserting.
/// </summary>
public sealed class RecordingSessionStopper : IDelegateSessionStopper
{
    private readonly List<Guid> _killed = [];
    private readonly List<(Guid SessionId, SessionTerminationSource Source)> _sources = [];

    public IReadOnlyList<Guid> Killed => _killed;

    /// <summary>The termination source each kill carried (the one-argument overload records SystemRequest).</summary>
    public IReadOnlyList<(Guid SessionId, SessionTerminationSource Source)> Sources => _sources;

    /// <summary>Set to make the stopper throw — a runner that has already lost the session.</summary>
    public Exception? Throws { get; set; }

    /// <summary>
    /// When set, a kill that does not throw also closes the session row (Stopped) in this database,
    /// the way <c>AgentSessionService.KillAsync</c> does on a kill that takes. CARD-0691 D-3: a pool
    /// agent row is removed only once its session is verified terminal, so a fixture that expects the
    /// row gone after a kill must use a stopper whose kill actually ends the session. Null leaves the
    /// row as it was — the "kill did not take" shape.
    /// </summary>
    public string? StopsSessionsIn { get; set; }

    public Task KillAsync(Guid sessionId, CancellationToken ct) =>
        KillAsync(sessionId, SessionTerminationSource.SystemRequest, ct);

    public async Task KillAsync(Guid sessionId, SessionTerminationSource source, CancellationToken ct)
    {
        _killed.Add(sessionId);
        _sources.Add((sessionId, source));
        if (Throws is not null)
            throw Throws;
        if (StopsSessionsIn is not { } connectionString)
            return;

        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        await db.AgentSessions
            .Where(s => s.Id == sessionId
                && (s.Status == SessionStatus.Created || s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running || s.Status == SessionStatus.Stopping))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, SessionStatus.Stopped)
                .SetProperty(s => s.EndedAt, now)
                .SetProperty(s => s.LastSeenAt, now), ct);
    }
}
