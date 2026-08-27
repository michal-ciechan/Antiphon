using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Periodic driver for <see cref="CardWorkTransitionService"/> (CARD-0040) — see that class for why
/// the transitions are a sweep rather than a hook at the dispatch and settle sites.
/// </summary>
public sealed class CardWorkTransitionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CardWorkTransitionSettings _settings;
    private readonly ILogger<CardWorkTransitionHostedService> _logger;

    public CardWorkTransitionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<CardWorkTransitionSettings> settings,
        ILogger<CardWorkTransitionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var transitions = scope.ServiceProvider.GetRequiredService<CardWorkTransitionService>();
                await transitions.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Card work transition sweep failed");
            }
        }
    }
}
