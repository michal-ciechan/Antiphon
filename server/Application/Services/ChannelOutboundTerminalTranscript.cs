using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal static class ChannelOutboundTerminalTranscript
{
    // Null means unknown, including old/unreachable runners, stale generations and partial persistence.
    internal static async Task<DateTime?> CatchUpAsync(IServiceProvider services, Guid sessionId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.Status is not (SessionStatus.Stopped or SessionStatus.Failed)) return null;
        var runner = services.GetService<ISessionRunnerClient>();
        var runtime = services.GetService<AgentSessionRuntime>();
        if (runner is null || runtime is null) return null;
        Dtos.SessionRunnerTranscriptDto snapshot;
        try { snapshot = await runner.GetTranscriptAsync(sessionId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
        if (!snapshot.TerminalComplete || snapshot.SessionId != sessionId
            || snapshot.AcceptedStartedAt != session.StartedAt || snapshot.Entries.Count == 0
            || snapshot.Entries.Any(e => e.SessionId != sessionId)
            || snapshot.LastSequence != snapshot.Entries[^1].Sequence) return null;
        await runtime.PersistTranscriptAsync(sessionId, snapshot.Entries);
        // UUID identity survives runner sequence rebasing. Check payload as well: a metadata stub
        // or an ignored failed insert is not proof of a complete persisted transcript.
        foreach (var page in snapshot.Entries.Chunk(128))
        {
            var uuids = page.Where(e => e.Uuid != null).Select(e => e.Uuid!).ToArray();
            var sequences = page.Where(e => e.Uuid == null).Select(e => e.Sequence).ToArray();
            var stored = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == sessionId
                && (t.Uuid != null && uuids.Contains(t.Uuid) || sequences.Contains(t.Sequence))).ToListAsync(ct);
            foreach (var entry in page)
                if (!stored.Any(t => (entry.Uuid is null ? t.Sequence == entry.Sequence : t.Uuid == entry.Uuid)
                    && t.Kind == entry.Kind && t.Text == entry.Text && t.ToolInput == entry.ToolInput
                    && t.StopReason == entry.StopReason && t.IsApiError == entry.IsApiError)) return null;
        }
        return await db.AgentSessions.AnyAsync(s => s.Id == sessionId && s.StartedAt == session.StartedAt
            && (s.Status == SessionStatus.Stopped || s.Status == SessionStatus.Failed), ct) ? session.StartedAt : null;
    }
}
