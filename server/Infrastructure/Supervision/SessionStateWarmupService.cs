using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Startup barrier, then pin/eviction maintenance; never polls cached transcripts.</summary>
public sealed class SessionStateWarmupService(SessionStateStore states, ILogger<SessionStateWarmupService> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await states.WarmAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await states.WarmAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Session state pin maintenance failed; retaining previous pins");
            }
        }
    }
}
