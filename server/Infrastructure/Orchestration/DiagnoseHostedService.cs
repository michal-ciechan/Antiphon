using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Drains <see cref="DiagnoseQueue"/> serially (CARD-0352 S3). One seat, one turn at a time;
/// a parallel drainer would only queue inside the session. Each request runs in its own scope.
/// A throw is logged and dropped — titles are best-effort, and the next create is a new request.
/// </summary>
public sealed class DiagnoseHostedService : BackgroundService
{
    private readonly DiagnoseQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly ILogger<DiagnoseHostedService> _logger;

    public DiagnoseHostedService(
        DiagnoseQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<DiagnoseHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.DiagnoseEnabled)
        {
            _logger.LogInformation("Diagnose is disabled; the diagnose worker will not run.");
            return;
        }

        await EnsureSeatAsync(stoppingToken);

        try
        {
            await foreach (var request in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var diagnose = scope.ServiceProvider.GetRequiredService<DiagnoseService>();
                    await diagnose.RunAsync(request, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Diagnose request {Kind} task={TaskId} card={CardId} failed",
                        request.Kind, request.TaskId, request.CardId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task EnsureSeatAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var provisioner = scope.ServiceProvider.GetRequiredService<DiagnoseProvisioner>();
            await provisioner.EnsureAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not provision the diagnose seat at startup; title requests will retry Ensure per request");
        }
    }
}
