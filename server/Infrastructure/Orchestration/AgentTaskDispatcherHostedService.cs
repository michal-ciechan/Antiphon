using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>Ticks the delegated-task dispatcher. Same shape as the orchestrator's tick service.</summary>
public sealed class AgentTaskDispatcherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly ILogger<AgentTaskDispatcherHostedService> _logger;

    public AgentTaskDispatcherHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<AgentTaskDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Delegation is disabled; the task dispatcher will not run.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                var result = await dispatcher.TickAsync(stoppingToken);
                if (result.Dispatched > 0 || result.Failures > 0)
                {
                    _logger.LogDebug(
                        "Delegation tick: {Dispatched} dispatched, {Concurrency} held on concurrency, "
                        + "{Scope} held on scope, {Failures} failed",
                        result.Dispatched, result.SkippedConcurrency, result.SkippedScope, result.Failures);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Delegation dispatch tick failed");
            }
        }
    }
}
