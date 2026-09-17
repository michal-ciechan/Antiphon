using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// CARD-0552 D-9. Periodic driver for the unattended Mutation creator, in the
/// <see cref="DiagnoseSweepHostedService"/> shape: a <see cref="PeriodicTimer"/>, one scope per
/// tick, exceptions logged and the next tick tried. Every gate and the create live on
/// <see cref="MutationAutoDispatchSweep"/> so tests never have to host this service.
/// </summary>
public sealed class MutationAutoDispatchHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DelegationSettings> _settings;
    private readonly ILogger<MutationAutoDispatchHostedService> _logger;

    public MutationAutoDispatchHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<MutationAutoDispatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _settings.Value.MutationAutoDispatch;
        if (!options.Enabled)
        {
            _logger.LogInformation(
                "Mutation auto-dispatch is disabled; post-land verification debt will only be worked explicitly.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.SweepMinutes));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sweep = scope.ServiceProvider.GetRequiredService<MutationAutoDispatchSweep>();
                await sweep.TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mutation auto-dispatch sweep failed");
            }
        }
    }
}
