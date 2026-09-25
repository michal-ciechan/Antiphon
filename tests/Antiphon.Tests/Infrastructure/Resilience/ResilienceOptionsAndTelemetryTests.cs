using System.Diagnostics.Metrics;
using System.Net;
using Antiphon.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure.Resilience;

[Category("Unit")]
[NotInParallel("antiphon-resilience-meter")]
public class ResilienceOptionsAndTelemetryTests
{
    private const string Marker = "SECRET-MARKER-0717";

    [Test]
    public async Task Valid_defaults_match_the_contract()
    {
        var settings = new ResilienceSettings();
        Validate(settings).Succeeded.ShouldBeTrue();
        settings.Enabled.ShouldBeTrue();
        settings.TotalTimeoutSeconds.ShouldBe(120);
        settings.AttemptTimeoutSeconds.ShouldBe(10);
        settings.BaseDelayMilliseconds.ShouldBe(250);
        settings.MaxDelayMilliseconds.ShouldBe(5_000);
        settings.MaxRetryAttempts.ShouldBe(6);
        settings.UseJitter.ShouldBeTrue();
        settings.CircuitBreaker.FailureRatio.ShouldBe(0.5);
        settings.CircuitBreaker.MinimumThroughput.ShouldBe(10);
        settings.CircuitBreaker.SamplingDurationSeconds.ShouldBe(30);
        settings.CircuitBreaker.BreakDurationSeconds.ShouldBe(15);
        settings.Http.MaxConcurrentAttempts.ShouldBe(32);
        settings.Http.AllowedStatusCodes.ShouldBe([408, 429, 502, 503, 504]);
        settings.Database.MaxConcurrentAttempts.ShouldBe(8);
        settings.Database.AllowedSqlStates.ShouldContain("08006");
        settings.Database.AllowedSqlStates.ShouldNotContain("57014");
        settings.Http.MaxAuthorityPipelines.ShouldBe(128);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Values_above_120_unknown_codes_and_invalid_timing_are_rejected()
    {
        Validate(new ResilienceSettings { TotalTimeoutSeconds = 121 }).Succeeded.ShouldBeFalse();
        Validate(new ResilienceSettings { TotalTimeoutSeconds = 0 }).Succeeded.ShouldBeFalse();
        Validate(new ResilienceSettings { UseJitter = false }).Succeeded.ShouldBeFalse();
        Validate(new ResilienceSettings { MaxRetryAttempts = 0 }).Succeeded.ShouldBeFalse();
        Validate(new ResilienceSettings { MaxRetryAttempts = 101 }).Succeeded.ShouldBeFalse();
        var widened = new ResilienceSettings();
        widened.Http.AllowedStatusCodes = [408, 500];
        Validate(widened).Succeeded.ShouldBeFalse();
        var unknownState = new ResilienceSettings();
        unknownState.Database.AllowedSqlStates = ["08006", "23505"];
        Validate(unknownState).Succeeded.ShouldBeFalse();
        Validate(new ResilienceSettings
        {
            Profiles = new Dictionary<string, ResilienceProfileSettings>
            {
                ["mutation"] = new() { TotalTimeoutSeconds = 1 },
            },
        }).Succeeded.ShouldBeFalse();
        var subset = new ResilienceSettings();
        subset.Http.AllowedStatusCodes = [503];
        subset.Database.AllowedSqlStates = ["08006"];
        Validate(subset).Succeeded.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Disabled_registration_keeps_a_single_attempt()
    {
        var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)));
        await using var provider = ResilienceTestHost.Build(
            handler,
            ResilienceClientNames.GitHubRead,
            new ResilienceSettings { Enabled = false });
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        client.Timeout.ShouldNotBe(Timeout.InfiniteTimeSpan);
        var request = new HttpRequestMessage(HttpMethod.Get, "user");
        ResilienceTestHost.Stamp(request, ResilienceOperations.GitHubUser);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        handler.Sends.ShouldBe(1);
    }

    [Test]
    public async Task Retry_terminal_and_circuit_signals_are_exported_once()
    {
        var exported = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAntiphonResilienceMetrics()
            .AddInMemoryExporter(exported)
            .Build();
        var seen = new List<(string Name, long Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ResilienceTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            seen.Add((instrument.Name, value)));
        listener.Start();

        var logs = new CollectingLoggerProvider();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
            Task.FromResult(ResilienceTestHost.Status(
                Interlocked.Increment(ref step) < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        var settings = new ResilienceSettings { MaxRetryAttempts = 2, BaseDelayMilliseconds = 200, MaxDelayMilliseconds = 200 };
        await using var provider = ResilienceTestHost.Build(
            handler, ResilienceClientNames.GitHubRead, settings, time, new FixedResilienceJitter(0), logs);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var response = await ResilienceTestHost.Pump(
            time, Send(client), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        meterProvider.ForceFlush();
        seen.Count(row => row.Name == ResilienceTelemetry.RetryAttempts).ShouldBe(1);
        seen.Count(row => row.Name == ResilienceTelemetry.OperationsCompleted).ShouldBe(1);
        exported.Any(metric => metric.Name == ResilienceTelemetry.RetryAttempts).ShouldBeTrue();
    }

    [Test]
    public async Task Sensitive_markers_never_enter_metric_tags_or_logs()
    {
        var logs = new CollectingLoggerProvider();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
        var tags = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ResilienceTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tagList, _) =>
        {
            foreach (var tag in tagList)
                tags.Add(tag.Key + "=" + tag.Value);
        });
        listener.SetMeasurementEventCallback<double>((_, _, tagList, _) =>
        {
            foreach (var tag in tagList)
                tags.Add(tag.Key + "=" + tag.Value);
        });
        listener.Start();
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
        {
            var response = ResilienceTestHost.Status(
                Interlocked.Increment(ref step) == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                Marker);
            return Task.FromResult(response);
        });
        await using var provider = ResilienceTestHost.Build(
            handler, ResilienceClientNames.GitHubRead, time: time, jitter: new FixedResilienceJitter(0), logs: logs);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var request = new HttpRequestMessage(HttpMethod.Get, "user?token=" + Marker);
        ResilienceTestHost.Stamp(request, ResilienceOperations.GitHubUser);
        var response = await ResilienceTestHost.Pump(time, client.SendAsync(request), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string.Join('\n', logs.Lines).ShouldNotContain(Marker);
        string.Join('\n', tags).ShouldNotContain(Marker);
    }

    private static ValidateOptionsResult Validate(ResilienceSettings settings) =>
        new ResilienceSettingsValidator().Validate(null, settings);

    private static Task<HttpResponseMessage> Send(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "user");
        ResilienceTestHost.Stamp(request, ResilienceOperations.GitHubUser);
        return client.SendAsync(request);
    }
}
