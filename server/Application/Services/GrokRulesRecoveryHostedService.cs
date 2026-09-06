using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>Startup and periodic catch-up; persisted triggers survive a lost live notification.</summary>
public sealed class GrokRulesRecoveryHostedService(GrokRulesRefreshService rules, ILogger<GrokRulesRecoveryHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await rules.RecoverActiveAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Grok rules recovery pass failed; persisted work stays held"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
