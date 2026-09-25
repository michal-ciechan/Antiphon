using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Antiphon.Resilience;

/// <summary>
/// Outer handler for one named read client. Admitted GET/HEAD operations share a cached pipeline.
/// Anything else is a single send.
/// </summary>
public sealed class AdmittedReadHandler : DelegatingHandler
{
    private readonly string _dependency;
    private readonly IOptionsMonitor<ResilienceSettings> _settings;
    private readonly ResiliencePipelineCache _cache;
    private readonly ResilienceTelemetry _telemetry;
    private readonly TimeProvider _time;
    private readonly ILogger<AdmittedReadHandler> _logger;

    public AdmittedReadHandler(
        string dependency,
        IOptionsMonitor<ResilienceSettings> settings,
        ResiliencePipelineCache cache,
        ResilienceTelemetry telemetry,
        TimeProvider time,
        ILogger<AdmittedReadHandler> logger)
    {
        _dependency = dependency;
        _settings = settings;
        _cache = cache;
        _telemetry = telemetry;
        _time = time;
        _logger = logger;
    }

    public string Dependency => _dependency;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var operation = request.Options.TryGetValue(ResilienceRequest.Operation, out var stamped) ? stamped : null;
        var admitted = ResilienceOperations.TryAdmit(_dependency, operation, request.Method, out var known);
        var profileDisabled = admitted
            && known.Profile is not null
            && settings.Profiles.TryGetValue(known.Profile, out var profile)
            && profile.Enabled == false;
        if (request.Content is not null)
            await BufferContentAsync(request, cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled || !admitted || profileDisabled || request.Options.TryGetValue(SingleAttempt, out _))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var budget = request.Options.TryGetValue(ResilienceRequest.Budget, out var existing)
            ? existing
            : ResilienceBudget.Start(_time, settings, known.Profile);
        var started = _time.GetTimestamp();
        var authority = ResilienceRequest.NormalizeAuthority(request.RequestUri);
        var pipeline = _cache.HttpPipeline(_dependency, authority);
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(ResilienceRequest.BudgetProperty, budget);
        context.Properties.Set(ResilienceRequest.OperationProperty, known.Name);
        context.Properties.Set(ResilienceRequest.MethodProperty, request.Method);
        try
        {
            var response = await pipeline.ExecuteAsync(
                async (ctx, state) =>
                {
                    using var attempt = await CloneAsync(state, ctx.CancellationToken).ConfigureAwait(false);
                    var response = await base.SendAsync(attempt, ctx.CancellationToken).ConfigureAwait(false);
                    if (response.Content is not null)
                        await response.Content.LoadIntoBufferAsync(ctx.CancellationToken).ConfigureAwait(false);
                    return response;
                },
                context,
                request).ConfigureAwait(false);
            _telemetry.Completed("http", _dependency, "success", _time.GetElapsedTime(started));
            return response;
        }
        catch (Exception ex)
        {
            var outcome = OutcomeName(ex, cancellationToken);
            _telemetry.Completed("http", _dependency, outcome, _time.GetElapsedTime(started));
            if (outcome is "budget" or "timeout" or "canceled")
            {
                _logger.LogWarning(
                    "Resilience outcome {Operation} dependency {Dependency} outcome {Outcome} reason {Reason} elapsed {ElapsedMs}",
                    known.Name,
                    _dependency,
                    outcome,
                    ex.GetType().Name,
                    (long)_time.GetElapsedTime(started).TotalMilliseconds);
            }

            if (ex is BrokenCircuitException)
            {
                _telemetry.Rejected("http", _dependency, "circuit-open");
                throw new HttpRequestException("The read circuit is open.", ex);
            }

            if (ex is RateLimiterRejectedException)
            {
                _telemetry.Rejected("http", _dependency, "limiter");
                throw new HttpRequestException("The read attempt limit was reached.", ex);
            }

            if (ex is TimeoutRejectedException && !cancellationToken.IsCancellationRequested)
            {
                _telemetry.Exhausted("http", _dependency);
                throw new TaskCanceledException("The resilience deadline elapsed.", ex);
            }

            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private static string OutcomeName(Exception exception, CancellationToken caller) => exception switch
    {
        TimeoutRejectedException when !caller.IsCancellationRequested => "budget",
        OperationCanceledException when caller.IsCancellationRequested => "canceled",
        BrokenCircuitException => "circuit-open",
        RateLimiterRejectedException => "limiter",
        _ => "failed",
    };

    private static async ValueTask<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
        };
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is null)
            return clone;

        var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var content = new ByteArrayContent(bytes);
        foreach (var header in request.Content.Headers)
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        clone.Content = content;
        return clone;
    }

    private static async Task BufferContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null || request.Content is ByteArrayContent)
            return;
        var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var headers = request.Content.Headers;
        var buffered = new ByteArrayContent(bytes);
        foreach (var header in headers)
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        request.Content = buffered;
        if (bytes.Length > ResilienceRequest.MaxBufferedBodyBytes)
            request.Options.Set(SingleAttempt, true);
    }

    private static readonly HttpRequestOptionsKey<bool> SingleAttempt = new("Antiphon.Resilience.SingleAttempt");
}
