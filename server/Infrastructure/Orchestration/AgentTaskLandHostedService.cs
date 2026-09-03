using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>
/// Drains <see cref="AgentTaskLandQueue"/> and runs each land (CARD-0331). Retry and Held
/// re-pick belong to <see cref="AgentTaskLandSweepHostedService"/>; this reader never sleeps.
/// </summary>
public sealed class AgentTaskLandHostedService : BackgroundService
{
    private readonly AgentTaskLandQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AgentTaskLandHostedService> _logger;

    public AgentTaskLandHostedService(AgentTaskLandQueue queue, IServiceScopeFactory scopes,
        ILogger<AgentTaskLandHostedService> logger) => (_queue, _scopes, _logger) = (queue, scopes, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
                    try
                    {
                        await lands.RunAsync(request.TaskId, request.VerifyFilter, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Land operation failed for task {TaskId}", request.TaskId);
                        try
                        {
                            await lands.FailAsync(request.TaskId, ex, stoppingToken);
                        }
                        catch (Exception failEx) when (failEx is not OperationCanceledException)
                        {
                            _logger.LogWarning(failEx,
                                "Could not persist land failure for task {TaskId}; the sweep will retry",
                                request.TaskId);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Land operation failed for task {TaskId}", request.TaskId);
                }
                finally
                {
                    _queue.Release(request.TaskId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
