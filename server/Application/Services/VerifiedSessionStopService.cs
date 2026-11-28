using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Fences one observed seat against reassignment and confirms its generation's exit.</summary>
public sealed class VerifiedSessionStopService(
    AppDbContext db, AgentSessionRuntime runtime, SessionMessageQueueService messages,
    SessionTerminalAttachments attachments, IOptions<AgentSessionSettings> settings,
    TimeProvider clock, ILogger<VerifiedSessionStopService> logger)
{
    private const string Unattached = "Unattached";
    private const string Attached = "Attached";
    private const string Unknown = "Unknown";

    public async Task<VerifiedSessionStopPreconditions> InspectAsync(Guid id, CancellationToken ct)
    {
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(nameof(AgentSession), id);
        var key = id.ToString("D");
        var owners = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null && a.PersistentSessionId.ToLower() == key).ToListAsync(ct);
        var task = await LatestTaskAsync(id, ct);
        var runner = await ReadRunnerAsync(id, ct);
        return new(session.StartedAt, owners.Count == 1 ? owners[0].Id : null, task?.Id, task?.ConcurrencyToken,
            await OpenTaskAsync(id, ct), await TurnEndAsync(id, ct),
            attachments.IsAttached(id) ? Attached : RunnerAttachment(session, owners, runner),
            IsProtected(session, owners, task), runner?.Status);
    }

    public sealed record Authority(string Actor, Guid? ProjectId, Guid? BoardId, string Root);

    public async Task<Authority> ResolveAuthorityAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw PrincipalDenied();
        var hash = AgentTaskService.HashToken(token);
        var source = await (from session in db.AgentSessions.AsNoTracking()
            join agent in KeepSixService.StandingOrchestrators(db) on session.StandingAgentId equals (Guid?)agent.Id
            where session.DelegationTokenHash == hash && session.Status == SessionStatus.Running
                && session.CardId == null && agent.Status == AgentStatus.Running
                && agent.PersistentSessionId != null && agent.PersistentSessionId.ToLower() == session.Id.ToString()
            select new { session.Id, session.Cwd, agent.BoardId, ProjectId = (Guid?)agent.Board!.ProjectId })
            .SingleOrDefaultAsync(ct) ?? throw PrincipalDenied();
        var root = OrchestratorWorkspaceFactGatherer.FollowMarkerOrSelf(source.Cwd);
        if (!Path.IsPathFullyQualified(root) && !WorkingDirectoryIdentity.IsWindowsPath(root)) throw ScopeDenied();
        return new Authority($"session:{source.Id:D}", source.ProjectId, source.BoardId, root);
    }

    public async Task<VerifiedSessionStopReceipt> StopAsync(
        Guid id, VerifiedSessionStopRequest expected, Authority authority, string reason, CancellationToken ct)
    {
        var target = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ScopeDenied();
        var targetKey = id.ToString("D");
        var targetOwners = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null && a.PersistentSessionId.ToLower() == targetKey).Take(2).ToListAsync(ct);
        await RequireScopeAsync(authority, target, await LatestTaskAsync(id, ct),
            targetOwners.Count == 1 ? targetOwners[0] : null, ct);

        if (!attachments.TryBeginStop(id))
            throw new ConflictException("The seat has a browser attachment, terminal input or a stop in flight.",
                "session_stop_attached");
        try
        {
            var gate = messages.GetLock(id);
            await gate.WaitAsync(ct);
            try
            {
                return await StopUnderFenceAsync(id, expected, authority, reason, ct);
            }
            finally { gate.Release(); }
        }
        finally { attachments.EndStop(id); }
    }

    private async Task<VerifiedSessionStopReceipt> StopUnderFenceAsync(
        Guid id, VerifiedSessionStopRequest expected, Authority authority, string reason, CancellationToken ct)
    {
        // Commit the generation hold under the claim lock before any runner command can be issued.
        AuditRecord audit;
        await using (var prepare = await db.Database.BeginTransactionAsync(ct))
        {
            var session = await LockSessionAsync(id, expected, ct);
            await VerifyAsync(session, expected, authority, checkRunner: true, ct);
            if (await SessionStopAudit.Pending(db, id, session.StartedAt).AnyAsync(ct))
                throw new ConflictException("The verified stop is still awaiting an exit for this generation.", "session_stop_in_flight");

            audit = SessionStopAudit.Begin(db, id, session.StartedAt, authority.Actor, reason, clock.GetUtcNow().UtcDateTime);
            SessionStopAudit.BeginIntent(audit, id, session.StartedAt);
            await db.SaveChangesAsync(ct);
            await prepare.CommitAsync(ct);
        }

        await using var stop = await db.Database.BeginTransactionAsync(ct);
        var current = await LockSessionAsync(id, expected, ct);
        await db.Entry(audit).ReloadAsync(ct);
        if (db.Entry(audit).State == EntityState.Detached ||
            !await SessionStopAudit.Pending(db, id, current.StartedAt).AnyAsync(a => a.Id == audit.Id, ct))
            throw Changed();

        try
        {
            // A claim cannot take this generation across the commit above, but independent
            // authoritative task/transcript changes during that gap still veto the command.
            await VerifyAsync(current, expected, authority, checkRunner: false, ct);
        }
        catch (Exception ex) when (ex is ConflictException or ForbiddenException)
        {
            SessionStopAudit.SetOutcome(audit, "Preconditions changed before command");
            await db.SaveChangesAsync(ct);
            await stop.CommitAsync(ct);
            throw;
        }

        // A caller disconnect or lost runner reply does not discharge the committed hold.
        SessionRunnerSessionDto? exited = null;
        try
        {
            var requested = await runtime.KillGenerationAsync(id, expected.ExpectedGeneration,
                TimeSpan.FromMilliseconds(Math.Max(100, settings.Value.KillGraceMs)), ct);
            if (requested) exited = await ReadRunnerAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The verified stop command for session {SessionId} was not confirmed", id);
        }

        if (!SessionStopAudit.IsMatchingExit(current, exited) || await OpenTaskAsync(id, ct) is not null)
        {
            SessionStopAudit.SetOutcome(audit, "Exit unconfirmed", settleIntent: false);
            await db.SaveChangesAsync(ct);
            await stop.CommitAsync(ct);
            throw new ConflictException("The runner did not confirm exit for the verified unowned generation.",
                "session_stop_exit_unconfirmed");
        }

        // The row and audit settle atomically while warm dispatch is still fenced.
        SessionStopAudit.RecordExit(current, exited!, audit, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await stop.CommitAsync(ct);
        await runtime.DisposeSessionAsync(id);
        return new(id, exited!.AcceptedStartedAt!.Value, "Exited", null);
    }

    private async Task<AgentSession> LockSessionAsync(Guid id, VerifiedSessionStopRequest expected, CancellationToken ct)
    {
        // Warm dispatch locks the previous task, then takes KEY SHARE on the session.
        if (expected.ExpectedLastTaskId is Guid taskId)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT "Id" FROM "AgentTasks" WHERE "Id" = {taskId} AND "AgentSessionId" = {id} FOR UPDATE
                """, ct);
        var session = await db.AgentSessions.FromSqlInterpolated($"""
            SELECT * FROM "AgentSessions" WHERE "Id" = {id} FOR UPDATE
            """).SingleOrDefaultAsync(ct) ?? throw new NotFoundException(nameof(AgentSession), id);
        await db.Entry(session).ReloadAsync(ct);
        return session;
    }

    private async Task VerifyAsync(AgentSession session, VerifiedSessionStopRequest expected,
        Authority authority, bool checkRunner, CancellationToken ct)
    {
        var id = session.Id;
        var task = await LatestTaskAsync(id, ct);
        var key = id.ToString("D");
        var owners = await db.Agents.FromSqlInterpolated($"""
            SELECT * FROM "Agents" WHERE lower("PersistentSessionId") = {key} FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        var owner = owners.Count == 1 ? owners[0] : null;
        await RequireScopeAsync(authority, session, task, owner, ct);
        if (!SessionGeneration.Equal(session.StartedAt, expected.ExpectedGeneration)
            || session.Status != SessionStatus.Running || await OpenTaskAsync(id, ct) is not null
            || task?.Id != expected.ExpectedLastTaskId || task?.ConcurrencyToken != expected.ExpectedLastTaskRevision
            || task is not null && (task.CompletedAt is null || !AgentTaskService.IsSettled(task.Status))
            || IsProtected(session, owners, task) || owner?.Id != expected.ExpectedOwnerId
            || owner is { Status: not AgentStatus.Idle })
            throw Changed();

        if (checkRunner)
        {
            var runner = await ReadRunnerAsync(id, ct);
            if (RunnerAttachment(session, owners, runner) != Unattached)
                throw new ConflictException("An unattached terminal could not be verified.", "session_stop_attachment_unverified");
            if (runner is not { Status: "Running", TranscriptBound: true, Pending: null, Adopted: false })
                throw Changed();
        }

        if (await TurnEndAsync(id, ct) != expected.ExpectedTurnEndSequence
            || runtime.HasPendingTerminalInput(id) || runtime.HasManualTerminalTurn(id)
            || await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == id
                && (m.Status == QueuedMessageStatus.Pending
                    || m.Status == QueuedMessageStatus.Sent && m.DeliveryAttempts > 0 && m.DeliveryVerdict == null), ct))
            throw Changed();
    }

    private async Task RequireScopeAsync(
        Authority authority, AgentSession session, AgentTask? task, Agent? owner, CancellationToken ct)
    {
        var cardId = session.CardId ?? task?.CardId;
        var card = cardId is Guid id
            ? await db.Cards.AsNoTracking().Where(c => c.Id == id)
                .Select(c => new { c.BoardId, c.Board.ProjectId }).SingleOrDefaultAsync(ct)
            : null;
        var projectId = card?.ProjectId ?? task?.ProjectId ?? owner?.PoolProjectId;
        var boardId = card?.BoardId ?? owner?.BoardId;
        if (authority.BoardId is Guid boardScope && boardScope != boardId
            || projectId is not null && projectId != authority.ProjectId
            || projectId is null && authority.ProjectId is not null && (task is not null || owner is not null)
            || !WithinWorkspace(task?.RepoPath ?? session.Cwd, authority.Root))
            throw ScopeDenied();
    }

    private static bool WithinWorkspace(string path, string root) =>
        WorkingDirectoryIdentity.Same(path, root)
        || Path.IsPathFullyQualified(path) && Path.IsPathFullyQualified(root)
            && DelegationWorkspaceResolver.IsWithinRoot(path, root);

    private static ForbiddenException ScopeDenied() =>
        new("Stopping this session requires an orchestrator credential scoped to its workspace and project.", "session_stop_scope_denied");

    private static ForbiddenException PrincipalDenied() =>
        new("A verified stop requires a live standing orchestrator session credential.", "session_stop_principal_denied");

    private Task<AgentTask?> LatestTaskAsync(Guid id, CancellationToken ct) =>
        db.AgentTasks.AsNoTracking().Where(t => t.AgentSessionId == id)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).FirstOrDefaultAsync(ct);

    private Task<Guid?> OpenTaskAsync(Guid id, CancellationToken ct) =>
        db.AgentTasks.AsNoTracking().Where(t => t.AgentSessionId == id
            && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked))
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);

    private async Task<long?> TurnEndAsync(Guid id, CancellationToken ct)
    {
        var tail = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == id)
            .OrderByDescending(t => t.Sequence).Select(t => new { t.Kind, t.Sequence }).FirstOrDefaultAsync(ct);
        return tail is { Kind: TranscriptKinds.TurnEnd } ? tail.Sequence : null;
    }

    private static string RunnerAttachment(AgentSession session, List<Agent> owners, SessionRunnerSessionDto? runner)
    {
        if (runner?.HerdrOrigin == HerdrPaneOrigins.Attached) return Attached;
        // A native pane can be viewed without a SignalR connection, and a Claude remote-control
        // client cannot be excluded by the browser registry. Require positive runner evidence
        // from a pty-host seat for which this application owns the attachment surface.
        if (session.SessionBackend != SessionBackend.PtyHost
            || runner is not { Backend: SessionBackends.PtyHost, AcceptedStartedAt: { } generation }
            || runner.SessionId != session.Id || !SessionGeneration.Equal(generation, session.StartedAt)
            || session.AgentKind is not (AgentKind.Codex or AgentKind.Grok)
            || owners.Any(a => a.RemoteControlEnabled))
            return Unknown;
        return Unattached;
    }

    private static bool IsProtected(AgentSession session, List<Agent> owners, AgentTask? task) =>
        session.StandingAgentId is not null || session.CardId is not null
        || owners.Count > 1 || owners.Any(a => !a.IsPoolDelegate || a.AlwaysOn || a.BoardId is not null
            || a.CurrentCardId is not null || a.StandingSpecialistOwnerId is not null || a.StandingSpecialistRole is not null)
        || task is not null && (task.SourceLandingOperationId is not null || task.VerificationCustodyContractVersion is not null
            || AgentTaskRoles.IsSpecialist(task.Role));

    private async Task<SessionRunnerSessionDto?> ReadRunnerAsync(Guid id, CancellationToken ct)
    {
        using var budget = new ExecutionBudget(clock.GetUtcNow().AddSeconds(5), clock, ct);
        try { return await runtime.GetSessionAsync(id, budget.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Runner evidence for verified stop of session {SessionId} is unavailable", id);
            return null;
        }
    }

    private static ConflictException Changed() =>
        new("The verified session generation, task, owner or idle state changed.", "session_stop_precondition_changed");
}
