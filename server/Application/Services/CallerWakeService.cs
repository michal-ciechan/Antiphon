using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Antiphon.Server.Application.Services;

/// <summary>Provider-owned wake for a Claude caller whose terminal delivery may be withheld indefinitely.</summary>
public sealed class CallerWakeService(
    AppDbContext db, KeepSixShortfall shortfall, TimeProvider clock, IMemoryCache cache)
{
    internal static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(KeepSixShortfall.BackoffMinutes);
    private static readonly object ClaimLock = new();
    public sealed record Wake(IReadOnlyList<Guid> ReportTaskIds, int Shortfall, bool Idle = true);

    public async Task<Wake?> PollAsync(Guid agentId, Guid? conversationId, DateTime? generation, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = AgentTaskService.HashToken(token);
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) return null;
        AgentSession? session = null;
        if (Guid.TryParse(agent.PersistentSessionId, out var standingId))
            session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s =>
                s.Id == standingId && s.DelegationTokenHash == hash && s.Status == SessionStatus.Running, ct);

        var standing = session != null;
        if (standing)
        {
            if (!await KeepSixService.StandingOrchestrators(db).AnyAsync(a => a.Id == agentId, ct))
                return null;
        }
        else
        {
            var ownSessions = db.AgentTasks.Where(t => t.TokenHash == hash && t.AgentId == agentId
                && t.Kind == AgentTaskKind.Orchestrator
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
                .Select(t => t.AgentSessionId);
            session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s =>
                ownSessions.Contains(s.Id) && s.Status == SessionStatus.Running, ct);
        }
        if (session is null || session.AgentKind != AgentKind.ClaudeCode || session.Id != conversationId
            || !SessionGeneration.Equal(session.StartedAt, generation)) return null;

        // An empty transcript and a restart boundary both look non-working to the queue.
        // A wake needs positive end-of-turn evidence from this process before it spends a claim.
        if (!await db.TranscriptEntries.AnyAsync(e => e.AgentSessionId == session.Id
                && e.Kind == TranscriptKinds.TurnEnd && (e.Timestamp ?? e.CreatedAt) >= session.StartedAt, ct)
            || await SessionMessageQueueService.IsWorkingAsync(db, session.Id, ct))
            return new([], 0, Idle: false);

        var now = clock.GetUtcNow().UtcDateTime;
        var reportsKey = (session.Id, "caller-wake-reports");
        var reminderKey = (session.Id, "caller-wake-keep-six");
        List<Guid> reports = [];
        if (Due(reportsKey, now))
        {
            reports = await OwedReports(db, now - ReportInterval, session.Id)
                .OrderBy(t => t.CompletedAt).ThenBy(t => t.Id)
                .Select(t => t.Id).Take(16).ToListAsync(ct);
            if (reports.Count > 0 && !Claim(reportsKey, now, ReportInterval)) reports.Clear();
        }

        var count = 0;
        if (standing && KeepSixShortfall.Accepts(agent, session) && Due(reminderKey, now))
        {
            var row = await db.KeepSixOrchestrators.AsNoTracking()
                .SingleOrDefaultAsync(r => r.AgentId == agentId && r.Enabled
                    && (r.HoldUntil == null || r.HoldUntil <= now), ct);
            if (row is not null && !(row.LastAcknowledgedAt + ReminderInterval > now))
            {
                var counts = await shortfall.ReadAsync([agent], new Dictionary<Guid, KeepSixOrchestrator> { [agentId] = row }, ct);
                if (counts.TryGetValue(agentId, out var fleet) && fleet.Shortfall > 0
                    && KeepSixShortfall.GraceElapsed(row, fleet, now) && !KeepSixShortfall.InBackoff(row, fleet, now)
                    && Claim(reminderKey, now, ReminderInterval))
                    count = fleet.Shortfall;
            }
        }

        return new(reports, count);
    }

    // A fresh server process may repeat an advisory once. The durable receipt and the
    // caller's authenticated report read remain authoritative across process restarts.
    private bool Due((Guid, string) key, DateTime now) =>
        !cache.TryGetValue(key, out DateTime next) || now >= next;

    private bool Claim((Guid, string) key, DateTime now, TimeSpan interval)
    {
        lock (ClaimLock)
        {
            if (!Due(key, now)) return false;
            cache.Set(key, now + interval, interval);
            return true;
        }
    }

    internal static IQueryable<AgentTask> OwedReports(AppDbContext db, DateTime cutoff, Guid? callerId = null)
    {
        var candidates = db.AgentTasks.AsNoTracking().Where(AgentTaskRoles.NotSpecialist)
            .Where(t => t.ReplyTo == AgentTaskReplyTo.Session && t.ParentSessionId != null
                && t.Result != null && t.Result != "" && t.CompletedAt <= cutoff
                && (t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Failed
                    || t.Status == AgentTaskStatus.Blocked || t.Status == AgentTaskStatus.Canceled)
                && (t.ReceivedReportCompletedAt == null || t.ReceivedReportCompletedAt != t.CompletedAt
                    || t.ReceivedReportSessionId != t.ParentSessionId));
        if (callerId is Guid id) candidates = candidates.Where(t => t.ParentSessionId == id);
        else
        {
            var running = db.AgentSessions.Where(s => s.Status == SessionStatus.Running).Select(s => s.Id);
            candidates = candidates.Where(t => running.Contains(t.ParentSessionId!.Value));
        }

        // Only observations belonging to this settlement can close it. A task may have
        // returned Blocked, received a Reply and produced a different final report.
        return candidates.Where(t => !db.AgentTaskLandNotifications.Any(n =>
            n.TaskId == t.Id && n.ParentSessionId == t.ParentSessionId
            && n.Kind == LandNotificationKind.TaskCompletion && db.AgentTaskEvents.Any(e =>
                e.Id == n.SourceEventId && e.AgentTaskId == t.Id && e.At == t.CompletedAt) && n.ConfirmedAt != null && n.ConfirmingPromptSequence != null));
    }
}
