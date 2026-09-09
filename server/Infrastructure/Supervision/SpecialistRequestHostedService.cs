using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Progresses already-authorized requests and qualification; never grants or launches a standing process.</summary>
public sealed class SpecialistRequestHostedService(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<SpecialistRequestHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().ReconcileAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                { logger.LogWarning(ex, "Specialist request reconciliation failed; durable deadlines remain authoritative"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
