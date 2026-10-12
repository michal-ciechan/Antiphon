using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class SessionRunnerEventPump : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionRunnerSettings _settings;
    private readonly ILogger<SessionRunnerEventPump> _logger;
    private readonly TimeProvider _time;
    private int _abandonedHeldEvents;

    public SessionRunnerEventPump(
        IServiceScopeFactory scopeFactory,
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerEventPump> logger,
        TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_settings.HeldEventDeliveryTimeoutSeconds);
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var client = scope.ServiceProvider.GetRequiredService<ISessionRunnerClient>();
                var runtime = scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>();

                // Backfill BEFORE consuming live events: anything that streamed while this server
                // was down (restart) or the stream was severed lands first, so stored order tracks
                // runner order. Left lazy (first GET /transcript, 30 min later), the 2026-08-08
                // restart backfilled a turn's tail ABOVE its already-persisted TurnEnd and the
                // agent read "Working" forever. SyncTranscriptAsync also fires the turn-end
                // actions for any boundary that only ever arrived via this backfill.
                await CatchUpTranscriptsAsync(client, runtime, stoppingToken);

                var skipHeldCopy = false;
                await foreach (var evt in client.StreamEventsAsync(stoppingToken))
                {
                    // The HTTP iterator keeps its public [marker, original event] contract.
                    // The marker's payload has already received its one offer below.
                    if (skipHeldCopy)
                    {
                        skipHeldCopy = false;
                        continue;
                    }

                    if (evt.SubscriptionReady)
                    {
                        await HandleSubscriptionReadyAsync(evt, client, runtime, stoppingToken);
                        skipHeldCopy = HasPayload(evt);
                        continue;
                    }

                    using var phase = new RuntimePhase(_logger, TimeProvider.System,
                        evt.SessionId, "pump.event", producerTimestamp: evt.Transcript?.Timestamp,
                        producerSequence: evt.Transcript?.Sequence, transcriptUuid: evt.Transcript?.Uuid);
                    await DispatchEventAsync(evt, scope.ServiceProvider, runtime, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session runner event stream disconnected");
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Max(100, _settings.EventReconnectDelayMs)),
                    stoppingToken);
            }
        }
    }

    private async Task HandleSubscriptionReadyAsync(
        SessionRunnerEvent held, ISessionRunnerClient client,
        AgentSessionRuntime runtime, CancellationToken stoppingToken)
    {
        // Exit-path table while this marker owns a parsed payload:
        // success / swallowed list error -> one bounded offer; skip the following copy.
        // list timeout / iterator-independent list fault -> same offer, then reconnect.
        // host cancellation anywhere (including inside a caught-error log or final pull)
        // -> same initial offer, then unwind; a pending callback is explicitly abandoned.
        // non-cooperative list -> deadline ends our wait; retain/observe its owned scope.
        // callback pending/cancelled/faulted -> explicit abandonment, unwind, never replay.
        // subsequent iterator read fault / disposal -> no held payload remains to offer.
        // A fault before parsing/receiving a marker supplies no payload to this region.
        // Only PreserveHeldEventAsync decides completion versus abandonment. No exit,
        // including a return/exception in catch-up, bypasses this finally.
        try
        {
            using var ready = new RuntimePhase(
                _logger, _time, held.SessionId, "pump.subscription-catch-up");
            await CatchUpTranscriptsAsync(client, runtime, stoppingToken, subscriptionProof: true);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                _logger.LogWarning(
                    ex, "Subscription catch-up failed; delivering the held live event without snapshot coverage");
            throw;
        }
        finally
        {
            if (HasPayload(held) && !await PreserveHeldEventAsync(held, runtime, stoppingToken))
            {
                // Do not resume the iterator after abandoning an uncertain offer.
                // Host cancellation takes the normal stop path; deadline/fault reconnects.
                stoppingToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Held live event delivery did not complete");
            }
        }
    }

    private static bool HasPayload(SessionRunnerEvent evt) =>
        evt.Output is not null || evt.Exited is not null || evt.Transcript is not null
        || evt.TranscriptFault is not null || evt.TranscriptBound is not null
        || evt.Adopted is not null || evt.AgentStatus is not null;

    private async Task CatchUpTranscriptsAsync(
        ISessionRunnerClient client,
        AgentSessionRuntime runtime,
        CancellationToken ct,
        bool subscriptionProof = false)
    {
        IReadOnlyList<SessionRunnerSessionDto> sessions;
        try
        {
            sessions = subscriptionProof
                ? await ListForSubscriptionAsync(ct)
                : await client.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (subscriptionProof)
            {
                // Ordinary list faults retain the connection. The caller's common
                // finally offers the held payload even if host cancellation races this log.
                _logger.LogWarning(
                    ex, "Subscription catch-up failed; snapshot coverage was not established");
            }
            else
            {
                // Runner not up (yet) — the reconnect loop retries and the next pass catches up.
                _logger.LogDebug(ex, "Session runner unreachable; transcript catch-up skipped");
            }

            return;
        }

        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            // Per-session failures are swallowed (logged) inside SyncTranscriptAsync.
            using var phase = new RuntimePhase(_logger, TimeProvider.System, session.SessionId, "pump.catch-up");
            await runtime.SyncTranscriptAsync(session.SessionId, ct);
            // Sync is best effort and swallows pull cancellation. In particular the last
            // pull must not resume the iterator's held event with an already-cancelled token.
            ct.ThrowIfCancellationRequested();
        }

        ct.ThrowIfCancellationRequested();

        if (sessions.Count > 0)
            _logger.LogInformation("Transcript catch-up completed for {Count} runner session(s)", sessions.Count);
    }

    private async Task<IReadOnlyList<SessionRunnerSessionDto>> ListForSubscriptionAsync(CancellationToken ct)
    {
        // Keep the owner's existing list deadline, with monotonic injected time, and
        // bound our wait even if an HTTP handler ignores its cancellation token.
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(1, _settings.ListTimeoutSeconds)), _time);
        using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var scope = _scopeFactory.CreateAsyncScope();
        Task<IReadOnlyList<SessionRunnerSessionDto>>? request = null;
        var scopeTransferred = false;
        try
        {
            var client = scope.ServiceProvider.GetRequiredService<ISessionRunnerClient>();
            request = client.ListAsync(requestToken.Token);
            return await request.WaitAsync(requestToken.Token);
        }
        catch
        {
            if (request is not null && !request.IsCompleted)
            {
                scopeTransferred = true;
                _ = ObserveAbandonedOperationAsync(request, scope, "subscription list");
            }
            throw;
        }
        finally
        {
            if (!scopeTransferred) await scope.DisposeAsync();
        }
    }

    private async Task<bool> PreserveHeldEventAsync(
        SessionRunnerEvent evt, AgentSessionRuntime runtime, CancellationToken stoppingToken)
    {
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(_settings.HeldEventDeliveryTimeoutSeconds), _time);
        using var hostSignal = new CancellationTokenSource();
        using var deliveryToken = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, hostSignal.Token);
        using var phase = new RuntimePhase(_logger, _time, evt.SessionId, "pump.held-event-delivery");
        // Own a separate scope: an asynchronous consumer that ignores cancellation can
        // outlive this bounded wait. Its services stay alive until its task actually settles.
        var deliveryScope = _scopeFactory.CreateAsyncScope();
        Task? delivery = null;
        var scopeTransferred = false;
        try
        {
            // Give even a host already stopping one initial offer through the normal callback
            // path. Linking that cancelled token before invocation would lose non-replayable
            // output outright. Register immediately after invocation; Register observes both
            // an earlier cancellation and one racing the offer. Only a pending task is drained.
            delivery = DispatchEventAsync(evt, deliveryScope.ServiceProvider, runtime, deliveryToken.Token);
            using var hostRegistration = stoppingToken.Register(hostSignal.Cancel);
            await delivery.WaitAsync(deliveryToken.Token);
            phase.Completed();
            return true;
        }
        catch (OperationCanceledException) when (deliveryToken.IsCancellationRequested)
        {
            // The consumer is demonstrably still pending (or cancelled without completing
            // delivery) at the host/deadline bound. This pump has no held-output redelivery
            // queue; retrying an uncertain publication could duplicate accepted output.
            // Explicitly abandon this attempt and unwind the iterator for shutdown/reconnect.
            // Durable transcript payloads may be recovered by the existing next catch-up.
            _logger.LogWarning(
                "Held live event delivery abandoned: consumer did not complete within the cancellation/deadline bound; session={SessionId} event={EventName} reason={Reason} abandonedCount={AbandonedCount}",
                evt.SessionId, evt.EventName, stoppingToken.IsCancellationRequested ? "host-cancellation" : "deadline",
                Interlocked.Increment(ref _abandonedHeldEvents));
            if (delivery is not null)
            {
                scopeTransferred = true;
                _ = ObserveAbandonedOperationAsync(delivery, deliveryScope, "held-event callback");
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Held live event delivery abandoned: consumer failed; session={SessionId} event={EventName} reason=consumer-fault abandonedCount={AbandonedCount}",
                evt.SessionId, evt.EventName, Interlocked.Increment(ref _abandonedHeldEvents));
            return false;
        }
        finally
        {
            if (!scopeTransferred)
                await deliveryScope.DisposeAsync();
        }
    }

    private async Task ObserveAbandonedOperationAsync(Task delivery, AsyncServiceScope scope, string operation)
    {
        try
        {
            await delivery;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Abandoned {Operation} later failed", operation);
        }
        finally
        {
            try { await scope.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Abandoned {Operation} scope disposal failed", operation); }
        }
    }

    private async Task DispatchEventAsync(
        SessionRunnerEvent evt,
        IServiceProvider services,
        AgentSessionRuntime runtime,
        CancellationToken ct)
    {
        if (evt.Output is null
            && evt.Exited is null
            && evt.Transcript is null
            && evt.TranscriptFault is null
            && evt.TranscriptBound is null
            && evt.Adopted is null
            && evt.AgentStatus is null)
        {
            return;
        }

        if (evt.Output is not null)
            await runtime.ObserveOutputAsync(evt.Output.SessionId, evt.Output.Sequence, evt.Output.Text, ct);
        else if (evt.Exited is not null)
            await runtime.ObserveExitAsync(evt.Exited, ct);
        else if (evt.Transcript is not null)
            await runtime.ObserveTranscriptAsync(evt.Transcript, ct);
        else if (evt.TranscriptFault is not null)
        {
            // The runner is running a session with NO transcript because it could not
            // prove any candidate was that session's (CARD-0006). Nothing is ingested,
            // working/idle reads permanently idle and channel replies cannot dispatch —
            // it fails safe, but it must not fail silently.
            await services.GetRequiredService<TranscriptBindingIncidentService>()
                .OnTranscriptFaultAsync(evt.TranscriptFault, ct);
        }
        else if (evt.TranscriptBound is not null)
        {
            await services.GetRequiredService<TranscriptBindingIncidentService>()
                .OnHeuristicBindAsync(evt.TranscriptBound, ct);
        }
        else if (evt.Adopted is not null)
        {
            // The session survived a runner restart (pty-host split). No state change:
            // the DB row is already Running and stays Running. Nudge any connected
            // terminal to resync its buffer since output streamed while the runner
            // (and therefore SignalR fanout) was down.
            _logger.LogInformation(
                "Session {SessionId} adopted by restarted runner (last sequence {LastSequence})",
                evt.Adopted.SessionId, evt.Adopted.LastSequence);
            var eventBus = services.GetRequiredService<IEventBus>();
            await eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(evt.Adopted.SessionId),
                "SessionAdopted",
                new { sessionId = evt.Adopted.SessionId, lastSequence = evt.Adopted.LastSequence },
                ct);
        }
        else if (evt.AgentStatus is not null)
        {
            // CARD-0162: herdr status change — blocked-exit may ONLY nudge FlushIfIdleAsync.
            await runtime.ObserveAgentStatusAsync(evt.AgentStatus, ct);
        }
    }
}
