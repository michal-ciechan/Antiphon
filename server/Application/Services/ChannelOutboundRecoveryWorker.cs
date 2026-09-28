using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Antiphon.Server.Application.Services;

/// <summary>The outbound retry clock; normally the host clock, independently replaceable in tests.</summary>
public sealed class ChannelOutboundClock(TimeProvider provider)
{
    public TimeProvider Provider { get; } = provider;
}

/// <summary>Observable completion of each real recovery pass.</summary>
public class ChannelOutboundRecoveryObserver
{
    public virtual Task ScanCompletedAsync(long pass, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Startup and periodic recovery of frozen sends and completed, unmaterialized turns.</summary>
public sealed class ChannelOutboundRecoveryWorker : BackgroundService
{
    private readonly ChannelOutboundPublicationService _publications;
    private readonly ChannelReplyDispatcher _dispatcher;
    private readonly ChannelBridgeSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ChannelOutboundRecoveryObserver _observer;
    private readonly ILogger<ChannelOutboundRecoveryWorker> _logger;

    public ChannelOutboundRecoveryWorker(ChannelOutboundPublicationService publications,
        ChannelReplyDispatcher dispatcher, IOptions<ChannelBridgeSettings> settings,
        ChannelOutboundClock clock, ChannelOutboundRecoveryObserver observer,
        ILogger<ChannelOutboundRecoveryWorker> logger)
    {
        _publications = publications;
        _dispatcher = dispatcher;
        _settings = settings.Value;
        _clock = clock.Provider;
        _observer = observer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long pass = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            Task? nextScan = null;
            try
            {
                await _publications.RecoverDueAsync(stoppingToken);
                await _dispatcher.DiscoverCompletedTurnsAsync(stoppingToken);
                // Arm the fake/host clock before observers release a waiting caller. Otherwise
                // an immediate clock advance can happen before the delay has been registered.
                nextScan = Task.Delay(TimeSpan.FromSeconds(_settings.OutboundScanSeconds), _clock, stoppingToken);
                await _observer.ScanCompletedAsync(++pass, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Channel outbound recovery pass failed");
            }
            try
            {
                await (nextScan ?? Task.Delay(TimeSpan.FromSeconds(_settings.OutboundScanSeconds), _clock, stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

public static class ChannelOutboundRegistration
{
    public static IServiceCollection AddChannelOutboundRecovery(this IServiceCollection services)
    {
        services.TryAddSingleton<ChannelOutboundClock>(provider =>
            new ChannelOutboundClock(provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ChannelOutboundRecoveryObserver>();
        services.AddSingleton<ChannelOutboundRecoveryWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<ChannelOutboundRecoveryWorker>());
        return services;
    }
}
