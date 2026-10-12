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

                await foreach (var evt in client.StreamEventsAsync(stoppingToken))
                {
                    if (evt.SubscriptionReady)
                    {
                        try
                        {
                            using var ready = new RuntimePhase(
                                _logger, _time, evt.SessionId, "pump.subscription-catch-up");
                            await CatchUpTranscriptsAsync(
                                client, runtime, stoppingToken, subscriptionProof: true);
                        }
                        catch (Exception ex)
                        {
                            // Offer the marker's held payload once before disposing the iterator.
                            // A pending consumer has a bounded drain; cancellation must neither
                            // silently reject the initial offer nor obstruct shutdown forever.
                            var hostStopping = ex is OperationCanceledException
                                && stoppingToken.IsCancellationRequested;
                            if (!hostStopping)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Subscription catch-up failed; delivering the held live event without snapshot coverage");
                            }

                            await PreserveHeldEventAsync(evt, runtime, stoppingToken);
                            throw;
                        }

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

    private async Task CatchUpTranscriptsAsync(
        ISessionRunnerClient client,
        AgentSessionRuntime runtime,
        CancellationToken ct,
        bool subscriptionProof = false)
    {
        IReadOnlyList<SessionRunnerSessionDto> sessions;
        try
        {
            sessions = await client.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (subscriptionProof)
            {
                // Return so the enumerator can yield the held event. Rethrowing here
                // abandons that yield and reconnects before the callback.
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

    private async Task PreserveHeldEventAsync(
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
                _ = ObserveHeldDeliveryCompletionAsync(delivery, deliveryScope);
            }
        }
        finally
        {
            if (!scopeTransferred)
                await deliveryScope.DisposeAsync();
        }
    }

    private async Task ObserveHeldDeliveryCompletionAsync(Task delivery, AsyncServiceScope scope)
    {
        try
        {
            await delivery;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Abandoned held-event callback later failed");
        }
        finally
        {
            try { await scope.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Abandoned held-event callback scope disposal failed"); }
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
