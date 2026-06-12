using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class AgentSessionLaunchQueue
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _launches = [];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentSessionLaunchQueue> _logger;

    public AgentSessionLaunchQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentSessionLaunchQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(StartAgentSessionRequest request, AgentLaunchSpec spec)
    {
        Enqueue(request, spec, AgentSessionLaunchBehavior.Orchestrated);
    }

    public void EnqueueInteractive(StartAgentSessionRequest request, AgentLaunchSpec spec)
    {
        Enqueue(request, spec, AgentSessionLaunchBehavior.Interactive);
    }

    /// <summary>
    /// Background-launches a pre-created cardless interactive session (no card/worktree/workflow).
    /// On failure the session is marked Failed by <see cref="AgentSessionService.LaunchInteractiveAsync"/>.
    /// With <paramref name="resume"/> the agent's previous Claude conversation is resumed.
    /// </summary>
    public void EnqueueInteractiveSession(
        Guid sessionId, Guid agentId, AgentLaunchSpec spec, string? remoteControlName, bool resume = false)
    {
        var launch = Task.Run(() => LaunchInteractiveSessionAsync(sessionId, agentId, spec, remoteControlName, resume));
        lock (_gate)
            _launches.Add(launch);

        launch.ContinueWith(
            task =>
            {
                lock (_gate)
                    _launches.Remove(task);

                if (task.Exception is not null)
                    _logger.LogWarning(task.Exception, "Queued interactive session launch failed for session {SessionId}", sessionId);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task LaunchInteractiveSessionAsync(
        Guid sessionId, Guid agentId, AgentLaunchSpec spec, string? remoteControlName, bool resume)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
        await service.LaunchInteractiveAsync(sessionId, agentId, spec, remoteControlName, resume, CancellationToken.None);
    }

    private void Enqueue(
        StartAgentSessionRequest request,
        AgentLaunchSpec spec,
        AgentSessionLaunchBehavior behavior)
    {
        var launch = Task.Run(() => LaunchAsync(request, spec, behavior));
        lock (_gate)
            _launches.Add(launch);

        launch.ContinueWith(
            task =>
            {
                lock (_gate)
                    _launches.Remove(task);

                if (task.Exception is not null)
                    _logger.LogWarning(task.Exception, "Queued agent session launch failed for card {CardId}", request.CardId);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async Task WaitForIdleAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (true)
        {
            Task[] launches;
            lock (_gate)
                launches = _launches.ToArray();

            if (launches.Length == 0)
                return;

            await Task.WhenAll(launches).WaitAsync(cts.Token);
        }
    }

    private async Task LaunchAsync(
        StartAgentSessionRequest request,
        AgentLaunchSpec spec,
        AgentSessionLaunchBehavior behavior)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retryScheduler = scope.ServiceProvider.GetRequiredService<RetryScheduler>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        try
        {
            var service = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
            var result = await service.StartAsync(request, spec, CancellationToken.None);
            var attempt = await db.RunAttempts
                .AsNoTracking()
                .SingleAsync(a => a.Id == result.RunAttemptId, CancellationToken.None);
            var now = timeProvider.GetUtcNow().UtcDateTime;

            if (attempt.Phase == RunPhase.Succeeded)
            {
                CardChangedNotification? notification = null;
                if (behavior.MoveToReviewOnSuccess)
                {
                    notification = await MoveCardToReviewAsync(
                        db,
                        request.CardId,
                        now,
                        CancellationToken.None);
                }

                if (behavior.ScheduleContinuationOnSuccess)
                    await retryScheduler.ScheduleContinuationAsync(db, request.CardId, now, CancellationToken.None);
                if (behavior.StopOnSuccess)
                    await StopCompletedSessionAsync(service, result.SessionId, CancellationToken.None);
                if (behavior.ReleaseClaimOnSuccess)
                {
                    notification = await CompleteClaimAsync(
                        db,
                        request.CardId,
                        SessionStatus.Stopped,
                        null,
                        now,
                        CancellationToken.None) ?? notification;
                }

                await db.SaveChangesAsync(CancellationToken.None);
                await PublishCardChangedAsync(eventBus, notification, CancellationToken.None);
                return;
            }
            else if (RunAttemptStateMachine.IsTerminal(attempt.Phase))
            {
                if (behavior.ScheduleRetryOnFailure)
                {
                    await retryScheduler.ScheduleFailureAsync(
                        db,
                        request.CardId,
                        attempt.ErrorDetails ?? $"Run attempt ended in phase {attempt.Phase}.",
                        now,
                        CancellationToken.None);
                }
                var notification = await CompleteClaimAsync(
                    db,
                    request.CardId,
                    SessionStatus.Failed,
                    attempt.ErrorDetails ?? $"Run attempt ended in phase {attempt.Phase}.",
                    now,
                    CancellationToken.None);
                await db.SaveChangesAsync(CancellationToken.None);
                await PublishCardChangedAsync(eventBus, notification, CancellationToken.None);
                return;
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (behavior.ScheduleRetryOnFailure)
                await retryScheduler.ScheduleFailureAsync(db, request.CardId, ex.Message, now, CancellationToken.None);
            var notification = await CompleteClaimAsync(db, request.CardId, SessionStatus.Failed, ex.Message, now, CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
            await PublishCardChangedAsync(eventBus, notification, CancellationToken.None);
            throw;
        }
    }

    private static async Task<CardChangedNotification?> MoveCardToReviewAsync(
        AppDbContext db,
        Guid cardId,
        DateTime utcNow,
        CancellationToken ct)
    {
        var card = await db.Cards
            .Include(c => c.BoardColumn)
            .Include(c => c.Board).ThenInclude(b => b.Columns)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null)
            return null;

        return CardLifecycleTransitions.TryMoveToReview(card, utcNow)
            ? new CardChangedNotification(card.BoardId, card.Id)
            : null;
    }

    private async Task StopCompletedSessionAsync(
        AgentSessionService service,
        Guid sessionId,
        CancellationToken ct)
    {
        try
        {
            await service.KillAsync(sessionId, ct);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Completed agent session {SessionId} was missing from runtime cleanup", sessionId);
        }
    }

    private static async Task<CardChangedNotification?> CompleteClaimAsync(
        AppDbContext db,
        Guid cardId,
        SessionStatus sessionStatus,
        string? failureReason,
        DateTime utcNow,
        CancellationToken ct)
    {
        var card = await db.Cards
            .Include(c => c.OwnerSession)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null)
            return null;

        if (card.OwnerSession is not null)
        {
            card.OwnerSession.Status = sessionStatus;
            card.OwnerSession.EndedAt ??= utcNow;
            card.OwnerSession.LastSeenAt = utcNow;
            card.OwnerSession.FailureReason = failureReason;
        }

        card.OwnerSessionId = null;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = utcNow;
        return new CardChangedNotification(card.BoardId, card.Id);
    }

    private static async Task PublishCardChangedAsync(
        IEventBus eventBus,
        CardChangedNotification? notification,
        CancellationToken ct)
    {
        if (notification is null)
            return;

        await eventBus.PublishToAllAsync(
            "CardChanged",
            new { boardId = notification.BoardId, cardId = notification.CardId },
            ct);
    }

    private sealed record CardChangedNotification(Guid BoardId, Guid CardId);

    private sealed record AgentSessionLaunchBehavior(
        bool StopOnSuccess,
        bool ReleaseClaimOnSuccess,
        bool ScheduleContinuationOnSuccess,
        bool ScheduleRetryOnFailure,
        bool MoveToReviewOnSuccess)
    {
        public static AgentSessionLaunchBehavior Orchestrated { get; } =
            new(
                StopOnSuccess: true,
                ReleaseClaimOnSuccess: true,
                ScheduleContinuationOnSuccess: false,
                ScheduleRetryOnFailure: true,
                MoveToReviewOnSuccess: true);

        public static AgentSessionLaunchBehavior Interactive { get; } =
            new(
                StopOnSuccess: false,
                ReleaseClaimOnSuccess: false,
                ScheduleContinuationOnSuccess: false,
                ScheduleRetryOnFailure: false,
                MoveToReviewOnSuccess: true);
    }
}
