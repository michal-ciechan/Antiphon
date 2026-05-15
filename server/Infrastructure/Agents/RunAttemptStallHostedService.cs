using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents;

public sealed class RunAttemptStallHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentSessionSettings _settings;
    private readonly ILogger<RunAttemptStallHostedService> _logger;

    public RunAttemptStallHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentSessionSettings> settings,
        ILogger<RunAttemptStallHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(100, _settings.StallScanIntervalMs));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var detector = scope.ServiceProvider.GetRequiredService<RunAttemptStallDetector>();
                await detector.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Run attempt stall scan failed");
            }
        }
    }
}
