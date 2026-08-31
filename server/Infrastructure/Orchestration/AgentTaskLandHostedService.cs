using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>Runs explicit land operations off the request thread.</summary>
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
                    var result = await scope.ServiceProvider.GetRequiredService<AgentTaskLandService>()
                        .RunAsync(request.TaskId, request.VerifyFilter, stoppingToken);
                    if (result == LandRunResult.Held)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        _queue.TryEnqueue(request.TaskId, request.VerifyFilter);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Land operation failed for task {TaskId}", request.TaskId); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
