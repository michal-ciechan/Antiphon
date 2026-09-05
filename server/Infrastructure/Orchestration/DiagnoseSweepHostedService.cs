using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Periodic driver for job 2 (CARD-0352 D4). Same shape as
/// <see cref="CardWorkTransitionHostedService"/>: a <see cref="PeriodicTimer"/>, one scope per
/// tick, exceptions logged and the next tick tried. Selection and enqueue live on
/// <see cref="CardDiagnosisSweep"/> so tests do not have to host this service.
/// </summary>
public sealed class DiagnoseSweepHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DelegationSettings> _settings;
    private readonly ILogger<DiagnoseSweepHostedService> _logger;

    public DiagnoseSweepHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<DiagnoseSweepHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _settings.Value;
        if (!settings.DiagnoseEnabled || !settings.DiagnoseSweepEnabled)
        {
            _logger.LogInformation("Diagnose sweep is disabled; unlabelled cards will not be queued.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.DiagnoseSweepMinutes));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sweep = scope.ServiceProvider.GetRequiredService<CardDiagnosisSweep>();
                await sweep.TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Diagnose card sweep failed");
            }
        }
    }
}
