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

    public SessionRunnerEventPump(
        IServiceScopeFactory scopeFactory,
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerEventPump> logger,
        TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
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
                            // The marker carries the event held across this yield. Publish that
                            // copy before the enumerator is disposed, then keep the existing
                            // reconnect or host-shutdown path. A cancelled host token must not
                            // block the callback.
                            var hostStopping = ex is OperationCanceledException
                                && stoppingToken.IsCancellationRequested;
                            if (!hostStopping)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Subscription catch-up failed; delivering the held live event without snapshot coverage");
                            }

                            await DispatchEventAsync(
                                evt, scope.ServiceProvider, runtime, CancellationToken.None);
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
        }

        if (sessions.Count > 0)
            _logger.LogInformation("Transcript catch-up completed for {Count} runner session(s)", sessions.Count);
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
