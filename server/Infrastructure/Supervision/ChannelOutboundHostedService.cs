using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Supervision;

public sealed class ChannelOutboundHostedService(
    IServiceScopeFactory scopes, ILogger<ChannelOutboundHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ChannelOutboundDeliveryPump>()
                    .TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Outbound delivery pump tick failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
