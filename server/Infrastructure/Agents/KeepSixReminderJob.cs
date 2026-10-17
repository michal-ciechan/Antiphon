using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-1320 S3. One WhenIdle reminder per standing orchestrator after the grace.
/// The server reminds only. This job does not dispatch.
/// </summary>
public sealed class KeepSixReminderJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<KeepSixReminderJob> logger)
{
    public const string RecurringJobId = "antiphon:keep-six";
    public const int GraceMinutes = 3;
    public const int BackoffMinutes = 15;
    public const string AdvisoryLockKey = "antiphon.keep-six.reminder";

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AppDbContext>();
        var keepSix = provider.GetRequiredService<KeepSixService>();
        var shortfall = provider.GetRequiredService<KeepSixShortfall>();
        var queue = provider.GetRequiredService<SessionMessageQueueService>();
        var pipeline = provider.GetRequiredService<AgentTaskPipelineStatusService>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({AdvisoryLockKey}))", ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var agents = await keepSix.ListStandingOrchestratorsAsync(ct).ConfigureAwait(false);
        if (agents.Count == 0)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return;
        }

        var ids = agents.Select(agent => agent.Id).ToArray();
        var stored = await db.KeepSixOrchestrators.AsNoTracking()
            .Where(row => ids.Contains(row.AgentId))
            .ToDictionaryAsync(row => row.AgentId, ct)
            .ConfigureAwait(false);
        var counts = await shortfall.ReadAsync(agents, stored, ct).ConfigureAwait(false);
        var sessionIds = agents
            .Select(agent => ParseSession(agent.PersistentSessionId))
            .Where(id => id is Guid)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var sessions = sessionIds.Length == 0
            ? new Dictionary<Guid, AgentSession>()
            : await db.AgentSessions.AsNoTracking()
                .Where(session => sessionIds.Contains(session.Id))
                .ToDictionaryAsync(session => session.Id, ct)
                .ConfigureAwait(false);
        var boardIds = agents
            .Where(agent => agent.BoardId is Guid)
            .Select(agent => agent.BoardId!.Value)
            .Distinct()
            .ToArray();
        var boardProjects = boardIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Boards.AsNoTracking()
                .Where(board => boardIds.Contains(board.Id))
                .ToDictionaryAsync(board => board.Id, board => board.ProjectId, ct)
                .ConfigureAwait(false);
        var pipelines = new Dictionary<Guid, AgentTaskPipelineDto>();

        foreach (var agent in agents)
        {
            if (!stored.TryGetValue(agent.Id, out var row) || !row.Enabled)
                continue;
            if (!counts.TryGetValue(agent.Id, out var count) || count.Shortfall <= 0)
                continue;
            if (row.HoldUntil is DateTime holdUntil && now < holdUntil)
                continue;
            if (ParseSession(agent.PersistentSessionId) is not Guid sessionId)
                continue;
            if (!sessions.TryGetValue(sessionId, out var session) || !Accepts(agent, session))
                continue;
            if (agent.BoardId is not Guid boardId || !boardProjects.TryGetValue(boardId, out var projectId))
                continue;
            if (!GraceElapsed(row, count, now))
                continue;
            if (InBackoff(row, count, now))
                continue;
            if (await PendingAsync(db, sessionId, ct).ConfigureAwait(false))
                continue;

            AgentTaskPipelineDto snapshot;
            try
            {
                snapshot = await PipelineAsync(pipeline, pipelines, projectId, ct).ConfigureAwait(false);
            }
            catch (NotFoundException ex)
            {
                logger.LogInformation(ex, "Keep-six skipped orchestrator {AgentId}: project is gone", agent.Id);
                continue;
            }

            var body = KeepSixReminderText.Compose(Describe(count, snapshot, projectId));
            Guid messageId = Guid.Empty;
            await queue.EnqueueAsync(
                session.Id,
                body,
                MessageSendMode.WhenIdle,
                ct,
                origin: QueuedMessageOrigin.System,
                noteHeader: KeepSixReminderText.NoteHeader,
                onCreated: id => messageId = id,
                deliverIfIdle: true).ConfigureAwait(false);
            if (messageId == Guid.Empty)
                continue;

            var stamped = now;
            await db.KeepSixOrchestrators
                .Where(candidate => candidate.AgentId == agent.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.LastReminderEnqueuedAt, stamped)
                    .SetProperty(candidate => candidate.OutstandingMessageId, messageId), ct)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Enqueued keep-six reminder {MessageId} for orchestrator {AgentId}",
                messageId, agent.Id);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<AgentTaskPipelineDto> PipelineAsync(
        AgentTaskPipelineStatusService pipeline,
        Dictionary<Guid, AgentTaskPipelineDto> cache,
        Guid projectId,
        CancellationToken ct)
    {
        if (cache.TryGetValue(projectId, out var cached))
            return cached;
        var snapshot = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, projectId), ct)
            .ConfigureAwait(false);
        cache[projectId] = snapshot;
        return snapshot;
    }

    private static KeepSixReminderText.Facts Describe(
        KeepSixShortfall.Counts count, AgentTaskPipelineDto snapshot, Guid projectId)
    {
        var scope = snapshot.ConcurrencyScopes.FirstOrDefault(candidate => candidate.ProjectId == projectId);
        var stages = scope is null
            ? Array.Empty<KeepSixReminderText.StageRemaining>()
            : scope.Occupancy.Roles
                .Select(role => new KeepSixReminderText.StageRemaining(role.Role, role.ParallelRemaining))
                .ToArray();
        var hosts = snapshot.Hosts
            .Select(host => new KeepSixReminderText.HostRemaining(host.HostId, host.Remaining))
            .ToArray();
        return new KeepSixReminderText.Facts(
            count.CountedOpen,
            count.EffectiveTarget,
            count.DispatchableBacklog,
            count.Shortfall,
            count.TopCards,
            hosts,
            stages);
    }

    private static bool Accepts(Agent agent, AgentSession session) =>
        session.Status == SessionStatus.Running
        && session.SessionBackend == SessionBackend.PtyHost
        && session.CardId is null
        && agent.PolicyRefreshMode != PolicyRefreshMode.Off;

    private static bool GraceElapsed(KeepSixOrchestrator row, KeepSixShortfall.Counts count, DateTime now)
    {
        var anchor = Latest(row.EnabledAt, count.LastDispatchAt, count.LastCompletionAt);
        return anchor is DateTime from && now >= from.AddMinutes(GraceMinutes);
    }

    private static bool InBackoff(KeepSixOrchestrator row, KeepSixShortfall.Counts count, DateTime now)
    {
        if (row.LastReminderEnqueuedAt is not DateTime enqueued)
            return false;
        var createdAfter = count.LastDispatchAt is DateTimeOffset created && created.UtcDateTime > enqueued;
        return now < enqueued.AddMinutes(BackoffMinutes) && !createdAfter;
    }

    private static DateTime? Latest(DateTime? enabledAt, DateTimeOffset? lastCreate, DateTimeOffset? lastCompletion)
    {
        DateTime? latest = enabledAt;
        void Consider(DateTimeOffset? value)
        {
            if (value is not DateTimeOffset instant)
                return;
            var utc = instant.UtcDateTime;
            if (latest is null || utc > latest)
                latest = utc;
        }

        Consider(lastCreate);
        Consider(lastCompletion);
        return latest;
    }

    private static async Task<bool> PendingAsync(AppDbContext db, Guid sessionId, CancellationToken ct) =>
        await db.SessionQueuedMessages.AsNoTracking().AnyAsync(message =>
            message.AgentSessionId == sessionId
            && message.NoteHeader == KeepSixReminderText.NoteHeader
            && message.Status == QueuedMessageStatus.Pending, ct).ConfigureAwait(false);

    private static Guid? ParseSession(string? persistentSessionId) =>
        Guid.TryParse(persistentSessionId, out var sessionId) ? sessionId : null;
}
