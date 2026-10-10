using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0822. Startup reconcile, then a sweep. A failed pass is logged and the host stays up.
/// The integration harness does not register this service.
/// </summary>
public sealed class OrchestratorInstructionsHostedService(
    OrchestratorInstructionsService service,
    IServiceScopeFactory scopes,
    ILogger<OrchestratorInstructionsHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Once("startup", stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(SweepSeconds()), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await Once("sweep", stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task Once(string reason, CancellationToken ct)
    {
        try
        {
            await service.ReconcileNowAsync(reason, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orchestrator instructions {Reason} reconcile failed", reason);
        }
    }

    private int SweepSeconds()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var seconds = scope.ServiceProvider.GetRequiredService<IOptions<DelegationSettings>>()
                .Value.OrchestratorInstructions.SweepSeconds;
            return Math.Clamp(
                seconds,
                OrchestratorInstructionsSettings.MinSweepSeconds,
                OrchestratorInstructionsSettings.MaxSweepSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Orchestrator instructions sweep interval fell back to the default");
            return OrchestratorInstructionsSettings.DefaultSweepSeconds;
        }
    }
}
