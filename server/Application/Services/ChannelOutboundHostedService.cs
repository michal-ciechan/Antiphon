using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0418: periodic claim/convert/publish pump. No DB transaction stays open across agent work.</summary>
public sealed class ChannelOutboundHostedService(
    IServiceScopeFactory scopes,
    ILogger<ChannelOutboundHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var outbound = scope.ServiceProvider.GetRequiredService<ChannelOutboundService>();
                await outbound.PumpOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Channel outbound pump tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}