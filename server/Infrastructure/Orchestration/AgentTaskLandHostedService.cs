using Antiphon.Server.Application.Dtos;
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
                        await lands.RunRequestAsync(request.TaskId, request.RequestId, request.VerifyFilter, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            var handled = await lands.FailRequestAsync(request.TaskId, request.RequestId, ex, stoppingToken);
                            _logger.LogWarning(
                                "Land operation failed for task {TaskId} request {RequestId} attempt {Attempt} exception {ExceptionType} code {Code} diagnostic {DiagnosticId} frames {Frames}",
                                request.TaskId, handled.RequestId, handled.Attempt, handled.ExceptionType, handled.Code,
                                handled.DiagnosticId, LandFailureDiagnostic.RedactedFrames(ex));
                        }
                        catch (LandFailurePersistenceException persistEx)
                        {
                            _logger.LogWarning(
                                "Could not persist land failure for task {TaskId} diagnostic {DiagnosticId} persistence {PersistenceErrorType} frames {Frames}",
                                request.TaskId, persistEx.DiagnosticId, persistEx.PersistenceErrorType,
                                LandFailureDiagnostic.RedactedFrames(persistEx.InnerException ?? persistEx));
                        }
                        catch (Exception failEx) when (failEx is not OperationCanceledException)
                        {
                            _logger.LogWarning(
                                "Could not persist land failure for task {TaskId}; the sweep will retry; diagnostic {DiagnosticId} frames {Frames}",
                                request.TaskId, Guid.NewGuid(), LandFailureDiagnostic.RedactedFrames(failEx));
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Land operation failed for task {TaskId} exception {ExceptionType} diagnostic {DiagnosticId} frames {Frames}",
                        request.TaskId, LandFailureDiagnostic.ExceptionTypeName(ex), Guid.NewGuid(),
                        LandFailureDiagnostic.RedactedFrames(ex));
                }
                finally
                {
                    _queue.Release(request);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
