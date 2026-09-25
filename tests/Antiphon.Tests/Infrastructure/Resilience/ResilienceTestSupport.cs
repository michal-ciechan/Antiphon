using System.Net;
using Antiphon.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

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

internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    public List<string> Lines { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Collector(Lines);

    public void Dispose()
    {
    }

    private sealed class Collector(List<string> lines) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Add(formatter(state, exception));
            if (exception is not null)
                lines.Add(exception.ToString());
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                    lines.Add(pair.Key + "=" + pair.Value);
            }
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

internal static class ResilienceTestHost
{
    public static ServiceProvider Build(
        ScriptHandler handler,
        string clientName,
        ResilienceSettings? settings = null,
        FakeTimeProvider? time = null,
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
