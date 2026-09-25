using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Antiphon.Resilience;

/// <summary>
/// Singleton pipelines. Dynamic authorities are capped and inactive entries are evicted.
/// A pipeline is never built per call: circuit state has to survive the next request.
/// </summary>
public sealed class ResiliencePipelineCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PipelineEntry> _http = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkedListNode<string>> _dynamicOrder = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _dynamicLru = new();
    private readonly Dictionary<string, ConcurrencyLimiter> _httpLimiters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PipelineEntry> _databases = new(StringComparer.Ordinal);
    private readonly IOptionsMonitor<ResilienceSettings> _settings;
    private readonly ResilienceTelemetry _telemetry;
    private readonly TimeProvider _time;
    private readonly ILogger<ResiliencePipelineCache> _logger;
    private readonly IResilienceJitter _jitter;

    public ResiliencePipelineCache(
        IOptionsMonitor<ResilienceSettings> settings,
        ResilienceTelemetry telemetry,
        TimeProvider time,
        ILogger<ResiliencePipelineCache> logger,
        IResilienceJitter jitter)
    {
        _settings = settings;
        _telemetry = telemetry;
        _time = time;
        _logger = logger;
        _jitter = jitter;
    }

    public int ActiveHttpPipelines(string dependency)
    {
        lock (_gate)
            return _http.Keys.Count(key => key.StartsWith(dependency + "|", StringComparison.Ordinal));
    }

    public ResiliencePipeline<HttpResponseMessage> HttpPipeline(string dependency, string authority)
    {
        var key = dependency + "|" + authority;
        lock (_gate)
        {
            if (_http.TryGetValue(key, out var existing))
            {
                Touch(key, dependency);
                return (ResiliencePipeline<HttpResponseMessage>)existing.Pipeline;
            }

            if (ResilienceDependencies.IsDynamic(dependency))
                EvictDynamicIfNeeded();

            var pipeline = BuildHttp(dependency);
            _http[key] = new PipelineEntry(pipeline, null);
            if (ResilienceDependencies.IsDynamic(dependency))
            {
                var node = _dynamicLru.AddLast(key);
                _dynamicOrder[key] = node;
            }

            return pipeline;
        }
    }

    public ResiliencePipeline DatabasePipeline(
        string databaseName,
        Func<Exception, bool> retryable,
        Func<Exception, bool> dependencyFailure)
    {
        lock (_gate)
        {
            if (_databases.TryGetValue(databaseName, out var existing))
                return (ResiliencePipeline)existing.Pipeline;
            var (pipeline, limiter) = BuildDatabase(databaseName, retryable, dependencyFailure);
            _databases[databaseName] = new PipelineEntry(pipeline, limiter);
            return pipeline;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _http.Values)
                DisposeEntry(entry);
            foreach (var entry in _databases.Values)
                DisposeEntry(entry);
            foreach (var limiter in _httpLimiters.Values)
                limiter.Dispose();
            _http.Clear();
            _databases.Clear();
            _httpLimiters.Clear();
            _dynamicLru.Clear();
            _dynamicOrder.Clear();
        }
    }

    private void Touch(string key, string dependency)
    {
        if (!ResilienceDependencies.IsDynamic(dependency))
            return;
        if (!_dynamicOrder.TryGetValue(key, out var node))
            return;
        _dynamicLru.Remove(node);
        _dynamicLru.AddLast(node);
    }

    private void EvictDynamicIfNeeded()
    {
        var cap = Math.Max(1, _settings.CurrentValue.Http.MaxAuthorityPipelines);
        while (_dynamicLru.Count >= cap && _dynamicLru.First is { } oldest)
        {
            var key = oldest.Value;
            _dynamicLru.RemoveFirst();
            _dynamicOrder.Remove(key);
            if (_http.Remove(key, out var entry))
                DisposeEntry(entry);
        }
    }

    private ResiliencePipeline<HttpResponseMessage> BuildHttp(string dependency)
    {
        var settings = _settings.CurrentValue;
        var limiter = LimiterFor(dependency, settings.Http.MaxConcurrentAttempts);
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = _time };
        builder.AddRateLimiter(new Polly.RateLimiting.RateLimiterStrategyOptions
        {
            RateLimiter = args => limiter.AcquireAsync(cancellationToken: args.Context.CancellationToken),
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(settings.TotalTimeoutSeconds),
            TimeoutGenerator = args => new ValueTask<TimeSpan>(Positive(BudgetOf(args.Context)?.Remaining
                ?? TimeSpan.FromSeconds(_settings.CurrentValue.TotalTimeoutSeconds))),
        });
        builder.AddRetry(HttpRetry(dependency));
        builder.AddCircuitBreaker(HttpBreaker(dependency));
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds),
            TimeoutGenerator = args =>
            {
                var configured = TimeSpan.FromSeconds(_settings.CurrentValue.AttemptTimeoutSeconds);
                var budget = BudgetOf(args.Context);
                var remaining = budget?.AttemptTimeout(_settings.CurrentValue) ?? configured;
                if (remaining > configured)
                    remaining = configured;
                return new ValueTask<TimeSpan>(Positive(remaining));
            },
        });
        return builder.Build();
    }

    private RetryStrategyOptions<HttpResponseMessage> HttpRetry(string dependency)
    {
        var settings = _settings.CurrentValue;
        return new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = settings.MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(settings.BaseDelayMilliseconds),
            MaxDelay = TimeSpan.FromMilliseconds(settings.MaxDelayMilliseconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Randomizer = _jitter.Next,
            ShouldHandle = args =>
            {
                var current = _settings.CurrentValue;
                var operation = OperationOf(args.Context);
                var method = MethodOf(args.Context);
                var budget = BudgetOf(args.Context);
                var retry = HttpTransientFailureClassifier.IsRetryable(
                    args.Outcome,
                    current,
                    dependency,
                    operation,
                    method,
                    budget,
                    _time,
                    args.Context.CancellationToken);
                return new ValueTask<bool>(retry);
            },
            DelayGenerator = args =>
            {
                var current = _settings.CurrentValue;
                if (!current.Http.HonorRetryAfter || args.Outcome.Result is not { } response)
                    return default;
                if (!ResilienceRetryAfter.TryGet(response, _time, out var hint))
                    return default;
                var cap = TimeSpan.FromMilliseconds(current.MaxDelayMilliseconds);
                if (hint > cap)
                    return default;
                var budget = BudgetOf(args.Context);
                if (budget is not null && hint > budget.Remaining)
                    return default;
                return new ValueTask<TimeSpan?>(hint);
            },
            OnRetry = args =>
            {
                var current = _settings.CurrentValue;
                var retryNumber = args.AttemptNumber + 1;
                var budget = BudgetOf(args.Context);
                var reason = HttpTransientFailureClassifier.Reason(args.Outcome);
                _logger.LogWarning(
                    "Resilience retry {Operation} dependency {Dependency} retry {RetryNumber} of {MaxRetries} reason {Reason} elapsed {ElapsedMs} remaining {RemainingMs} delay {DelayMs}",
                    OperationOf(args.Context) ?? "unknown",
                    dependency,
                    retryNumber,
                    current.MaxRetryAttempts,
                    reason,
                    (long)args.Duration.TotalMilliseconds,
                    (long)(budget?.Remaining.TotalMilliseconds ?? 0),
                    (long)args.RetryDelay.TotalMilliseconds);
                _telemetry.Retry("http", dependency, reason, args.RetryDelay);
                return default;
            },
        };
    }

    private CircuitBreakerStrategyOptions<HttpResponseMessage> HttpBreaker(string dependency)
    {
        var settings = _settings.CurrentValue;
        return new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = settings.CircuitBreaker.FailureRatio,
            MinimumThroughput = settings.CircuitBreaker.MinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(settings.CircuitBreaker.SamplingDurationSeconds),
            BreakDuration = TimeSpan.FromSeconds(settings.CircuitBreaker.BreakDurationSeconds),
            ShouldHandle = args =>
            {
                var current = _settings.CurrentValue;
                var retry = HttpTransientFailureClassifier.IsRetryable(
                    args.Outcome,
                    current,
                    dependency,
                    OperationOf(args.Context),
                    MethodOf(args.Context),
                    BudgetOf(args.Context),
                    _time,
                    args.Context.CancellationToken);
                return new ValueTask<bool>(retry);
            },
            OnOpened = _ => Transition(dependency, "open"),
            OnClosed = _ => Transition(dependency, "closed"),
            OnHalfOpened = _ => Transition(dependency, "half-open"),
        };
    }

    private (ResiliencePipeline Pipeline, ConcurrencyLimiter Limiter) BuildDatabase(
        string databaseName,
        Func<Exception, bool> retryable,
        Func<Exception, bool> dependencyFailure)
    {
        var settings = _settings.CurrentValue;
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = Math.Max(1, settings.Database.MaxConcurrentAttempts),
            QueueLimit = 0,
        });
        var builder = new ResiliencePipelineBuilder { TimeProvider = _time };
        builder.AddRateLimiter(new Polly.RateLimiting.RateLimiterStrategyOptions
        {
            RateLimiter = args => limiter.AcquireAsync(cancellationToken: args.Context.CancellationToken),
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(settings.TotalTimeoutSeconds),
            TimeoutGenerator = args => new ValueTask<TimeSpan>(Positive(BudgetOf(args.Context)?.Remaining
                ?? TimeSpan.FromSeconds(_settings.CurrentValue.TotalTimeoutSeconds))),
        });
        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = settings.MaxRetryAttempts,
            Delay = TimeSpan.FromMilliseconds(settings.BaseDelayMilliseconds),
            MaxDelay = TimeSpan.FromMilliseconds(settings.MaxDelayMilliseconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Randomizer = _jitter.Next,
            ShouldHandle = args =>
            {
                if (args.Outcome.Exception is not { } exception)
                    return new ValueTask<bool>(false);
                if (BudgetOf(args.Context) is { Expired: true })
                    return new ValueTask<bool>(false);
                return new ValueTask<bool>(retryable(exception));
            },
            OnRetry = args =>
            {
                var current = _settings.CurrentValue;
                var budget = BudgetOf(args.Context);
                var reason = args.Outcome.Exception?.GetType().Name ?? "unknown";
                _logger.LogWarning(
                    "Resilience retry {Operation} dependency {Dependency} retry {RetryNumber} of {MaxRetries} reason {Reason} elapsed {ElapsedMs} remaining {RemainingMs} delay {DelayMs}",
                    OperationOf(args.Context) ?? databaseName,
                    ResilienceDependencies.Database,
                    args.AttemptNumber + 1,
                    current.MaxRetryAttempts,
                    reason,
                    (long)args.Duration.TotalMilliseconds,
                    (long)(budget?.Remaining.TotalMilliseconds ?? 0),
                    (long)args.RetryDelay.TotalMilliseconds);
                _telemetry.Retry("database", ResilienceDependencies.Database, reason, args.RetryDelay);
                return default;
            },
        });
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = settings.CircuitBreaker.FailureRatio,
            MinimumThroughput = settings.CircuitBreaker.MinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(settings.CircuitBreaker.SamplingDurationSeconds),
            BreakDuration = TimeSpan.FromSeconds(settings.CircuitBreaker.BreakDurationSeconds),
            ShouldHandle = args => new ValueTask<bool>(
                args.Outcome.Exception is { } exception && dependencyFailure(exception)),
            OnOpened = _ => Transition(ResilienceDependencies.Database, "open"),
            OnClosed = _ => Transition(ResilienceDependencies.Database, "closed"),
            OnHalfOpened = _ => Transition(ResilienceDependencies.Database, "half-open"),
        });
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds),
            TimeoutGenerator = args =>
            {
                var configured = TimeSpan.FromSeconds(_settings.CurrentValue.AttemptTimeoutSeconds);
                var remaining = BudgetOf(args.Context)?.AttemptTimeout(_settings.CurrentValue) ?? configured;
                if (remaining > configured)
                    remaining = configured;
                return new ValueTask<TimeSpan>(Positive(remaining));
            },
        });
        return (builder.Build(), limiter);
    }

    private ConcurrencyLimiter LimiterFor(string dependency, int permits)
    {
        if (_httpLimiters.TryGetValue(dependency, out var existing))
            return existing;
        var created = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = Math.Max(1, permits),
            QueueLimit = 0,
        });
        _httpLimiters[dependency] = created;
        return created;
    }

    private ValueTask Transition(string dependency, string transition)
    {
        var family = dependency == ResilienceDependencies.Database ? "database" : "http";
        _logger.LogInformation(
            "Resilience circuit {Dependency} transition {Transition}",
            dependency,
            transition);
        _telemetry.Circuit(family, dependency, transition);
        return default;
    }

    private static ResilienceBudget? BudgetOf(ResilienceContext context) =>
        context.Properties.TryGetValue(ResilienceRequest.BudgetProperty, out var budget) ? budget : null;

    private static string? OperationOf(ResilienceContext context) =>
        context.Properties.TryGetValue(ResilienceRequest.OperationProperty, out var operation) ? operation : null;

    private static HttpMethod? MethodOf(ResilienceContext context) =>
        context.Properties.TryGetValue(ResilienceRequest.MethodProperty, out var method) ? method : null;

    private static TimeSpan Positive(TimeSpan value) =>
        value < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : value;

    private static void DisposeEntry(PipelineEntry entry)
    {
        if (entry.Pipeline is IDisposable disposable)
            disposable.Dispose();
        entry.Limiter?.Dispose();
    }

    private sealed class PipelineEntry(object pipeline, ConcurrencyLimiter? limiter)
    {
        public object Pipeline { get; } = pipeline;
        public ConcurrencyLimiter? Limiter { get; } = limiter;
    }

    }
