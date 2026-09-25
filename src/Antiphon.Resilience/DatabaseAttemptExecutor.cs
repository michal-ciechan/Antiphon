using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Antiphon.Resilience;

/// <summary>
/// Shared attempt loop for an admitted database unit. The server supplies the classifier and the fresh context.
/// </summary>
public sealed class DatabaseAttemptExecutor
{
    private readonly ResiliencePipelineCache _cache;
    private readonly ResilienceTelemetry _telemetry;
    private readonly TimeProvider _time;
    private readonly ILogger<DatabaseAttemptExecutor> _logger;

    public DatabaseAttemptExecutor(
        ResiliencePipelineCache cache,
        ResilienceTelemetry telemetry,
        TimeProvider time,
        ILogger<DatabaseAttemptExecutor> logger)
    {
        _cache = cache;
        _telemetry = telemetry;
        _time = time;
        _logger = logger;
    }

    public async Task<T> ExecuteAsync<T>(
        string databaseName,
        string operation,
        ResilienceBudget budget,
        Func<Exception, bool> retryable,
        Func<Exception, bool> dependencyFailure,
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            databaseName = "default";
        var pipeline = _cache.DatabasePipeline(databaseName, retryable, dependencyFailure);
        var started = _time.GetTimestamp();
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(ResilienceRequest.BudgetProperty, budget);
        context.Properties.Set(ResilienceRequest.OperationProperty, operation);
        try
        {
            var result = await pipeline.ExecuteAsync(
                async (ctx, state) => await state(ctx.CancellationToken).ConfigureAwait(false),
                context,
                action).ConfigureAwait(false);
            _telemetry.Completed("database", ResilienceDependencies.Database, "success", _time.GetElapsedTime(started));
            return result;
        }
        catch (Exception ex)
        {
            var outcome = ex switch
            {
                TimeoutRejectedException when !cancellationToken.IsCancellationRequested => "budget",
                OperationCanceledException when cancellationToken.IsCancellationRequested => "canceled",
                BrokenCircuitException => "circuit-open",
                RateLimiterRejectedException => "limiter",
                _ => "failed",
            };
            _telemetry.Completed("database", ResilienceDependencies.Database, outcome, _time.GetElapsedTime(started));
            if (outcome is not "success")
            {
                _logger.LogWarning(
                    "Resilience outcome {Operation} dependency {Dependency} outcome {Outcome} reason {Reason} elapsed {ElapsedMs}",
                    operation,
                    ResilienceDependencies.Database,
                    outcome,
                    ex.GetType().Name,
                    (long)_time.GetElapsedTime(started).TotalMilliseconds);
            }

            if (ex is BrokenCircuitException)
            {
                _telemetry.Rejected("database", ResilienceDependencies.Database, "circuit-open");
                throw new ResilienceAdmissionException("circuit-open");
            }

            if (ex is RateLimiterRejectedException)
                throw new ResilienceAdmissionException("limiter");
            if (ex is TimeoutRejectedException && !cancellationToken.IsCancellationRequested)
            {
                _telemetry.Exhausted("database", ResilienceDependencies.Database);
                throw new TimeoutException("The database resilience deadline elapsed.", ex);
            }

            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
