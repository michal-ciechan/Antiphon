using System.Net;
using Antiphon.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Antiphon.Tests.TestHelpers;
using System.Diagnostics;

namespace Antiphon.Tests.Infrastructure.Resilience;

internal sealed class ScriptHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public ScriptHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        _respond = respond;

    public int Sends { get; private set; }

    public List<string> Paths { get; } = [];

    public List<byte[]> Bodies { get; } = [];

    public List<DateTimeOffset> ObservedAt { get; } = [];

    public Func<DateTimeOffset>? Clock { get; set; }

    public Func<HttpResponseMessage>? Next { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sends++;
        Paths.Add(request.RequestUri?.PathAndQuery ?? "");
        if (Clock is not null)
            ObservedAt.Add(Clock());
        if (request.Content is not null)
            Bodies.Add(await request.Content.ReadAsByteArrayAsync(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (Next is not null)
            return Next();
        return await _respond(request, cancellationToken);
    }
}

internal readonly record struct ResilienceLogLine(
    string Message,
    IReadOnlyDictionary<string, string?> Properties);

internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly Action<ResilienceLogLine>? _onLine;

    public CollectingLoggerProvider(Action<ResilienceLogLine>? onLine = null) =>
        _onLine = onLine;

    public List<string> Lines { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Collector(Lines, _onLine);

    public void Dispose()
    {
    }

    private sealed class Collector(List<string> lines, Action<ResilienceLogLine>? onLine) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            lines.Add(message);
            if (exception is not null)
                lines.Add(exception.ToString());
            var properties = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    lines.Add(pair.Key + "=" + pair.Value);
                    properties[pair.Key] = pair.Value?.ToString();
                }
            }

            onLine?.Invoke(new ResilienceLogLine(message, properties));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}

/// <summary>Keeps an observed cancellation callback alive until its test acknowledges it.</summary>
internal sealed class RetainedCancellationRegistration : IDisposable
{
    private CancellationTokenRegistration _registration;

    public void Register(CancellationToken token, Action callback) =>
        _registration = token.Register(callback);

    public void Dispose() => _registration.Dispose();
}

internal static class ResilienceTestHost
{
    public static ServiceProvider Build(
        ScriptHandler handler,
        string clientName,
        ResilienceSettings? settings = null,
        TimeProvider? time = null,
        IResilienceJitter? jitter = null,
        CollectingLoggerProvider? logs = null)
    {
        settings ??= new ResilienceSettings();
        time ??= new FakeTimeProvider(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        if (jitter is not null)
            services.AddSingleton(jitter);
        if (logs is not null)
            services.AddSingleton<ILoggerProvider>(logs);
        services.AddLogging();
        services.AddAntiphonResilience(new ConfigurationBuilder().Build());
        services.PostConfigure<ResilienceSettings>(target => Copy(settings, target));
        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    public static HttpClient Client(ServiceProvider provider, string clientName)
    {
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(clientName);
        client.BaseAddress ??= new Uri("https://dependency.test/");
        return client;
    }

    public static void Stamp(HttpRequestMessage request, string operation, ResilienceBudget? budget = null) =>
        ResilienceRequest.Stamp(request, operation, budget);

    public static async Task<T> Pump<T>(FakeTimeProvider time, Task<T> work, TimeSpan step, TimeSpan realLimit)
    {
        var started = DateTime.UtcNow;
        while (!work.IsCompleted && DateTime.UtcNow - started < realLimit)
        {
            time.Advance(step);
            await Task.Delay(1);
        }

        return await work.WaitAsync(TimeSpan.FromSeconds(2));
    }

    /// <summary>One observed zero-advance step: current UTC, requested boundary, whether the phase was still pending, and any invariant exception.</summary>
    internal readonly record struct BoundaryStep(
        DateTimeOffset Now,
        DateTimeOffset Boundary,
        bool PhasePending,
        Exception? Error);

    /// <summary>Advance one registered virtual boundary, then wait for its explicit phase.</summary>
    public static Task AdvanceAfterAsync(
        ControlledTimeProvider time,
        ControlledTimeProvider.TimerEvent timer,
        Task phase,
        DateTimeOffset boundary,
        CancellationToken token) =>
        AdvanceAfterAsync(time, timer, phase, boundary, observer: null, token);

    /// <summary>
    /// Advance one registered virtual boundary, then wait for its explicit phase.
    /// <paramref name="observer"/> receives each zero-advance loop step, including a captured invariant exception, before that exception is rethrown.
    /// </summary>
    public static async Task AdvanceAfterAsync(
        ControlledTimeProvider time,
        ControlledTimeProvider.TimerEvent timer,
        Task phase,
        DateTimeOffset boundary,
        Action<BoundaryStep>? observer,
        CancellationToken token)
    {
        if (timer.Sequence <= 0 || timer.Deadline != boundary || time.GetUtcNow() > boundary)
            throw new InvalidOperationException("Expected timer was not registered at the requested boundary.");
        var dueTimers = time.Events.GroupBy(e => e.TimerId).Select(group => group.Last())
            .Where(e => e.Action is "create" or "change" && e.DueTime > TimeSpan.Zero
                && e.Deadline <= boundary).ToArray();
        if (dueTimers.Length == 0 || dueTimers.Any(e => e.Deadline != boundary)
            || dueTimers.All(e => e.TimerId != timer.TimerId))
            throw new InvalidOperationException("all-timers-at-boundary: live timer inventory changed before advance.");
        time.AdvanceTo(boundary);
        var watchdog = Stopwatch.StartNew();
        while (!phase.IsCompleted)
        {
            Exception? error = null;
            var pending = !phase.IsCompleted;
            try
            {
                if (watchdog.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    var events = string.Join("; ", time.Events.Select(e =>
                        $"#{e.Sequence} timer={e.TimerId} {e.Action} at={e.RegisteredAt:O} due={e.DueTime}"));
                    var states = string.Join("; ", time.TimerStates.Select(e => $"{e.Key}={e.Value}"));
                    throw new TimeoutException(
                        $"Boundary phase did not complete at {boundary:O}; now={time.GetUtcNow():O}; " +
                        $"tokenCancelled={token.IsCancellationRequested}; expectedTimer={timer.TimerId}; " +
                        $"timerStates=[{states}]; timerEvents=[{events}]");
                }

                // A timer can be registered or rearmed by a continuation after the first advance.
                // Drive due callbacks again without changing the asserted virtual instant.
                time.Advance(TimeSpan.Zero);
                if (time.GetUtcNow() != boundary)
                    throw new InvalidOperationException("held-completion-keeps-time-at-boundary");
                if (!phase.IsCompleted)
                    await Task.WhenAny(phase, Task.Delay(1));
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                observer?.Invoke(new BoundaryStep(time.GetUtcNow(), boundary, pending, error));
            }
        }

        await phase;
        if (!token.IsCancellationRequested || time.GetUtcNow() != boundary
            || !time.Events.Any(e => e.Action == "fire" && e.RegisteredAt == boundary))
            throw new InvalidOperationException("cancellation-callback-at-boundary: timer/token phase was not acknowledged.");
    }

    public static HttpResponseMessage Status(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body) };

    private static void Copy(ResilienceSettings source, ResilienceSettings target)
    {
        target.Enabled = source.Enabled;
        target.TotalTimeoutSeconds = source.TotalTimeoutSeconds;
        target.AttemptTimeoutSeconds = source.AttemptTimeoutSeconds;
        target.BaseDelayMilliseconds = source.BaseDelayMilliseconds;
        target.MaxDelayMilliseconds = source.MaxDelayMilliseconds;
        target.MaxRetryAttempts = source.MaxRetryAttempts;
        target.UseJitter = source.UseJitter;
        target.CircuitBreaker = source.CircuitBreaker;
        target.Http = source.Http;
        target.Database = source.Database;
        target.Profiles = source.Profiles;
    }
}
