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

    public SessionRunnerEventPump(
        IServiceScopeFactory scopeFactory,
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerEventPump> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
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

                await foreach (var evt in client.StreamEventsAsync(stoppingToken))
                {
                    if (evt.Output is not null)
                        await runtime.ObserveOutputAsync(evt.Output.SessionId, evt.Output.Sequence, evt.Output.Text, stoppingToken);
                    else if (evt.Exited is not null)
                        await runtime.ObserveExitAsync(evt.Exited.SessionId, evt.Exited.ExitCode, evt.Exited.ExitReason, stoppingToken);
                    else if (evt.Transcript is not null)
                        await runtime.ObserveTranscriptAsync(evt.Transcript, stoppingToken);
                    else if (evt.Adopted is not null)
                    {
                        // The session survived a runner restart (pty-host split). No state change:
                        // the DB row is already Running and stays Running. Nudge any connected
                        // terminal to resync its buffer since output streamed while the runner
                        // (and therefore SignalR fanout) was down.
                        _logger.LogInformation(
                            "Session {SessionId} adopted by restarted runner (last sequence {LastSequence})",
                            evt.Adopted.SessionId, evt.Adopted.LastSequence);
                        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
                        await eventBus.PublishToGroupAsync(
                            AgentSessionGroups.Session(evt.Adopted.SessionId),
                            "SessionAdopted",
                            new { sessionId = evt.Adopted.SessionId, lastSequence = evt.Adopted.LastSequence },
                            stoppingToken);
                    }
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
}
