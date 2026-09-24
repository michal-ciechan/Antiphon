using System.Collections.Concurrent;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0604 G-21. Carries a REMOTE session's spilled body from the point it is spilled to the
/// Input frame that types its pointer, so the body travels in the Input payload and the RUNNER
/// writes the file inside the session's own cwd.
///
/// The desktop must never write it: a remote session's <c>Cwd</c> is a Windows path it cannot see,
/// so a desktop write leaves a file no one reads behind a prompt pointing at nothing — the agent
/// is told "read it in full before you do anything else" about a path that does not exist.
///
/// Transient staging is keyed by session and path. Queued bodies are persisted on their message
/// rows; this service reads the exact row Id from the pointer when an Input is sent.
/// </summary>
public sealed class RemoteSpillCourier
{
    private readonly ConcurrentDictionary<(Guid SessionId, string Path), StagedSpill> _staged = new();
    private IServiceScopeFactory? _scopeFactory;

    public RemoteSpillCourier(IServiceScopeFactory? scopeFactory = null) => _scopeFactory = scopeFactory;

    internal void UseScopeFactory(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public void Stage(Guid sessionId, string runnerCwd, PhoneHomeInputSpill spill) =>
        _staged[(sessionId, spill.RelativePath)] = new StagedSpill(runnerCwd, spill);

    public bool TryTake(Guid sessionId, out StagedSpill staged) =>
        TryPeek(sessionId, out staged) && Ack(sessionId, staged);

    /// <summary>
    /// CARD-0604 D-3. Look at the staged body WITHOUT clearing it. Taking it before the Input
    /// frame is acknowledged loses it outright when the phone-home socket drops mid-request:
    /// the body is gone from memory, the runner never wrote the file, and the retry types a
    /// pointer at a path that does not exist. Pair this with <see cref="Ack"/>.
    /// </summary>
    public bool TryPeek(Guid sessionId, out StagedSpill staged) =>
        TryPeek(sessionId, null, out staged);

    public bool TryPeek(Guid sessionId, string? input, out StagedSpill staged)
    {
        foreach (var entry in _staged)
        {
            if (entry.Key.SessionId != sessionId || (input is not null
                && !input.Contains(entry.Key.Path, StringComparison.Ordinal)))
                continue;
            staged = entry.Value;
            return true;
        }
        staged = null!;
        return false;
    }

    /// <summary>Find the exact queued spill after a server restart or a failed Input.</summary>
    public async Task<StagedSpill?> FindDurableAsync(Guid sessionId, string input, CancellationToken ct)
    {
        if (_scopeFactory is null)
            return null;
        const string prefix = ".antiphon/inbox/";
        var start = input.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += prefix.Length;
        var end = input.IndexOf(".md", start, StringComparison.Ordinal);
        if (end < 0 || !Guid.TryParse(input[start..end], out var messageId))
            return null;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Id == messageId && m.AgentSessionId == sessionId && m.RemoteSpillBody != null)
            .Select(m => new { m.Id, m.RemoteSpillBody, m.RemoteSpillRelativePath })
            .SingleOrDefaultAsync(ct);
        if (row is null || row.RemoteSpillRelativePath is null
            || !input.Contains(row.RemoteSpillRelativePath, StringComparison.Ordinal))
            return null;
        var cwd = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => s.RunnerCwd).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(cwd))
            throw new InvalidOperationException("Queued remote spill has no runner cwd.");
        return new StagedSpill(cwd, new PhoneHomeInputSpill(
            row.RemoteSpillRelativePath!, row.RemoteSpillBody!, row.Id));
    }

    /// <summary>
    /// CARD-0604 D-3. Clear the staged body now that the runner has acknowledged writing it.
    /// Only the body that was actually delivered is removed: a newer spill staged while the
    /// frame was in flight stays, so it is not silently dropped by a late acknowledgement.
    /// </summary>
    public bool Ack(Guid sessionId, StagedSpill delivered) =>
        _staged.TryRemove(new KeyValuePair<(Guid, string), StagedSpill>(
            (sessionId, delivered.Spill.RelativePath), delivered));

    public bool IsStaged(Guid sessionId) => _staged.Keys.Any(k => k.SessionId == sessionId);

    public void Clear(Guid sessionId)
    {
        foreach (var key in _staged.Keys.Where(k => k.SessionId == sessionId))
            _staged.TryRemove(key, out _);
    }

    public sealed record StagedSpill(string RunnerCwd, PhoneHomeInputSpill Spill);
}
