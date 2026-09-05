using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Drains <see cref="OutputDistillationQueue"/> serially (CARD-0330 S3). One seat, one turn at a
/// time. Each request runs in its own scope. A throw is logged and dropped — the raw note is
/// already queued, and a failed distillation must never lose it.
/// </summary>
public sealed class OutputDistillationHostedService : BackgroundService
{
    private readonly OutputDistillationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly ILogger<OutputDistillationHostedService> _logger;

    public OutputDistillationHostedService(
        OutputDistillationQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<OutputDistillationHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.OutputDistillerEnabled)
        {
            _logger.LogInformation("Output distiller is disabled; the distiller worker will not run.");
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
                    var distiller = scope.ServiceProvider.GetRequiredService<OutputDistillationService>();
                    await distiller.RequestAsync(request.TaskId, request.QueuedMessageId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Distillation of task {TaskId} queued={QueuedId} failed; the raw note stands",
                        request.TaskId, request.QueuedMessageId);
                    try
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var distiller = scope.ServiceProvider.GetRequiredService<OutputDistillationService>();
                        await distiller.ReleaseHoldAsync(request.QueuedMessageId, stoppingToken);
                    }
                    catch (Exception releaseEx) when (releaseEx is not OperationCanceledException)
                    {
                        _logger.LogWarning(
                            releaseEx, "Could not clear HoldUntil on queued message {QueuedId}",
                            request.QueuedMessageId);
                    }
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
            var provisioner = scope.ServiceProvider.GetRequiredService<OutputDistillerProvisioner>();
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
                "Could not provision the output distiller at startup; requests will retry Ensure per request");
        }
    }
}
