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

    public async Task<VerifiedSessionStopReceipt> StopAsync(
        Guid id, VerifiedSessionStopRequest expected, CancellationToken ct)
    {
        if (!attachments.TryBeginStop(id))
            throw new ConflictException("The seat has a browser attachment or a stop in flight.", "session_stop_attached");
        try
        {
            var gate = messages.GetLock(id);
            await gate.WaitAsync(ct);
            try
            {
                return await StopUnderFenceAsync(id, expected, ct);
            }
            finally { gate.Release(); }
        }
        finally { attachments.EndStop(id); }
    }

    private async Task<VerifiedSessionStopReceipt> StopUnderFenceAsync(
        Guid id, VerifiedSessionStopRequest expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Warm dispatch locks the previous task, then takes KEY SHARE on the session before
        // rechecking liveness and binding its new task. Match that order and take FOR UPDATE
        // so each side sees the other's committed decision before it can pass its own checks.
        if (expected.ExpectedLastTaskId is Guid taskId)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT "Id" FROM "AgentTasks" WHERE "Id" = {taskId} AND "AgentSessionId" = {id} FOR UPDATE
                """, ct);
        var session = await db.AgentSessions.FromSqlInterpolated($"""
            SELECT * FROM "AgentSessions" WHERE "Id" = {id} FOR UPDATE
            """).SingleOrDefaultAsync(ct) ?? throw new NotFoundException(nameof(AgentSession), id);
        if (!SessionGeneration.Equal(session.StartedAt, expected.ExpectedGeneration)
            || session.Status != SessionStatus.Running || await OpenTaskAsync(id, ct) is not null)
            throw Changed();

        var task = await LatestTaskAsync(id, ct);
        if (task?.Id != expected.ExpectedLastTaskId || task?.ConcurrencyToken != expected.ExpectedLastTaskRevision
            || task is not null && (task.CompletedAt is null || !AgentTaskService.IsSettled(task.Status)))
            throw Changed();

        var key = id.ToString("D");
        var owners = await db.Agents.FromSqlInterpolated($"""
            SELECT * FROM "Agents" WHERE lower("PersistentSessionId") = {key} FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        var owner = owners.Count == 1 ? owners[0] : null;
        if (IsProtected(session, owners, task) || owner?.Id != expected.ExpectedOwnerId
            || owner is { Status: not AgentStatus.Idle })
            throw Changed();

        var runner = await ReadRunnerAsync(id, ct);
        if (RunnerAttachment(session, owners, runner) != Unattached)
            throw new ConflictException("An unattached terminal could not be verified.", "session_stop_attachment_unverified");
        if (runner is not { Status: "Running", TranscriptBound: true, Pending: null, Adopted: false }
            || await TurnEndAsync(id, ct) != expected.ExpectedTurnEndSequence
            || runtime.HasPendingTerminalInput(id)
            || await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == id
                && (m.Status == QueuedMessageStatus.Pending
                    || m.Status == QueuedMessageStatus.Sent && m.DeliveryAttempts > 0 && m.DeliveryVerdict == null), ct))
            throw Changed();

        var requested = await runtime.KillGenerationAsync(id, expected.ExpectedGeneration,
            TimeSpan.FromMilliseconds(Math.Max(100, settings.Value.KillGraceMs)), ct);
        var exited = await ReadRunnerAsync(id, ct);
        if (!requested || exited is not { Status: "Exited", AcceptedStartedAt: { } generation }
            || exited.SessionId != id || !SessionGeneration.Equal(generation, expected.ExpectedGeneration)
            || await OpenTaskAsync(id, ct) is not null)
            throw new ConflictException("The runner did not confirm exit for the verified unowned generation.",
                "session_stop_exit_unconfirmed");

        // Publish Stopped before releasing the dispatch fence. A waiting claimant then sees
        // the terminal row even if the runner exit event has yet to reach the server.
        SessionExitEvidence.Apply(session, exited.ExitCode, exited.ExitSignal, exited.ExitStopIntent);
        SessionTermination.Record(session, SessionTerminationSource.OperatorRequest);
        session.Status = SessionStatus.Stopped;
        session.FailureReason = null;
        session.EndedAt = clock.GetUtcNow().UtcDateTime;
        session.LastSeenAt = session.EndedAt.Value;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await runtime.DisposeSessionAsync(id);
        return new(id, generation, "Exited", null);
    }

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
