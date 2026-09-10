using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

public sealed class AgentTaskLandMonitorHostedService(IServiceScopeFactory scopes, IOptions<DelegationSettings> settings,
    ILogger<AgentTaskLandMonitorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskLandMonitorService>().SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Land age monitor failed"); }
            await Task.Delay(TimeSpan.FromSeconds(settings.Value.LandSweepSeconds), stoppingToken);
        }
    }
}
