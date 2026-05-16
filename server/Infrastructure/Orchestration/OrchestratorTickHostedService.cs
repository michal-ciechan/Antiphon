using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

public sealed class OrchestratorTickHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrchestratorSettings _settings;
    private readonly ILogger<OrchestratorTickHostedService> _logger;

    public OrchestratorTickHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<OrchestratorSettings> settings,
        ILogger<OrchestratorTickHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestratorService>();
                await orchestrator.PollTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Orchestrator tick failed");
            }
        }
    }
}
