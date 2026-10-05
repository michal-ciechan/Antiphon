using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

public sealed class ChannelOutboundHostedService(
    IServiceScopeFactory scopes, ILogger<ChannelOutboundHostedService> logger,
    TimeProvider? timeProvider = null, IOptions<ChannelOutboundSettings>? settings = null) : BackgroundService
{
    internal Func<CancellationToken, Task>? CycleCompletedAsync { get; set; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings?.Value.UnifiedRecoveryEnabled == true
            ? settings.Value.ScanIntervalSeconds : 5), timeProvider ?? TimeProvider.System);
        do
        {
            try
            {
                // Discovery faults do not consume the pump's share of the cycle.
                try
                {
                    await using var discoveryScope = scopes.CreateAsyncScope();
                    var discovery = discoveryScope.ServiceProvider.GetService<ChannelOutboundDiscoveryService>();
                    if (discovery is not null) await discovery.TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogError(ex, "Outbound source discovery tick failed"); }
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ChannelOutboundDeliveryPump>()
                    .TickAsync(stoppingToken);
                if (CycleCompletedAsync is { } completed) await completed(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Outbound delivery pump tick failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
