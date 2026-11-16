using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Timeout(10_000)]
[ClassDataSource<C1514HostLoadFixture>(Shared = SharedType.PerTestSession)]
public class C1893CollectorDiagnosticsTests(C1514HostLoadFixture fixture)
{
    [Test]
    public void Missing_default_data_directory_never_resolves_against_application_directory()
    {
        var resolver = typeof(HostLoadCaptureService).Assembly.GetType("Antiphon.SessionRunner.HostLoadStoragePath");
        resolver.ShouldNotBeNull("an explicit default path resolver must handle absent LocalApplicationData");
        var method = resolver.GetMethod("Resolve"); method.ShouldNotBeNull();
        var home = fixture.PrivatePath();
        var resolved = (string)method.Invoke(null, [null, "", home])!;
        resolved.ShouldBe(Path.Combine(home, ".local", "share", "antiphon", "host-load"));
        Path.IsPathFullyQualified(resolved).ShouldBeTrue();
        var configured = fixture.PrivatePath();
        ((string)method.Invoke(null, [configured, "", home])!).ShouldBe(configured);
        Should.Throw<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [null, "", ""]));
        Should.Throw<System.Reflection.TargetInvocationException>(() => method.Invoke(null, ["", "", home]));
        Should.Throw<System.Reflection.TargetInvocationException>(() => method.Invoke(null, ["relative-private", "", home]));
    }

    [Test]
    public async Task Unwritable_default_candidate_reports_open_stage_without_private_path()
    {
        // A regular file cannot be a writable data directory on any platform.
        var blocked = fixture.PrivatePath(); Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.WriteAllText(blocked, "fixture");
        var settings = C1514HostLoadFixture.Config(Path.Combine(blocked, "antiphon", "host-load"));
        var clock = Clock(); var log = new CollectorLog();
        using var capture = Capture(settings, clock, log);
        await capture.StartAsync(CancellationToken.None);
        try
        {
            await C1514HostLoadFixture.EventuallyAsync(() => !log.Lines.IsEmpty);
            var diagnostic = Diagnostics(capture).GetProperty("recent").EnumerateArray().Single();
            diagnostic.GetProperty("stage").GetString().ShouldBe("open");
            diagnostic.GetProperty("pathClass").GetString().ShouldBe("configured-absolute");
            diagnostic.GetProperty("writable").GetBoolean().ShouldBeFalse();
            diagnostic.GetProperty("action").GetString().ShouldBe("check-history-directory-access");
            string.Join("", log.Lines).ShouldNotContain(blocked);
            log.Levels.ShouldBe([LogLevel.Warning]);
            Should.Throw<IOException>(() => capture.ReadHistory(null, 1));
        }
        finally { await capture.StopAsync(CancellationToken.None); }
    }

    [Test]
    public async Task Repeated_errors_are_capped_rate_limited_and_scrubbed()
    {
        var clock = Clock(); var log = new CollectorLog(); var attempts = 0;
        using var capture = Capture(C1514HostLoadFixture.Config(fixture.PrivatePath()), clock, log,
            () => { Interlocked.Increment(ref attempts); throw new UnauthorizedAccessException("/private/token=DO_NOT_LEAK"); });
        await capture.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 1; i <= 20; i++)
            {
                var expected = i;
                await C1514HostLoadFixture.EventuallyAsync(() => Diagnostics(capture).GetProperty("failures").GetInt64() >= expected);
                clock.Advance(TimeSpan.FromSeconds(5));
            }
            var json = Diagnostics(capture);
            json.GetProperty("recent").GetArrayLength().ShouldBe(8);
            var last = json.GetProperty("recent").EnumerateArray().Last();
            last.GetProperty("errorClass").GetString().ShouldBe("permission");
            last.GetProperty("writable").GetBoolean().ShouldBeFalse();
            json.GetProperty("lastAppendErrorAt").ValueKind.ShouldBe(JsonValueKind.String);
            log.Lines.Count.ShouldBeGreaterThanOrEqualTo(2);
            log.Lines.Count.ShouldBeLessThanOrEqualTo(2);
            var text = json.GetRawText() + string.Join("", log.Lines);
            text.ShouldNotContain("DO_NOT_LEAK"); text.ShouldNotContain("/private/");
            text.Length.ShouldBeLessThan(6000);
        }
        finally { await capture.StopAsync(CancellationToken.None); }
    }

    [Test]
    public void Production_factory_keeps_absent_path_optional_and_resolves_absolute_profile_data()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        configuration["SessionRunner:HostStats:HistoryPath"].ShouldBeNull();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHostStats(configuration, startSampler: false);
        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<HostStatsSettings>>().Value;
        settings.HistoryPath.ShouldBeNull("absence must not be replaced by a cwd-relative path during options binding");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        var resolver = typeof(HostLoadCaptureService).Assembly.GetType("Antiphon.SessionRunner.HostLoadStoragePath")!;
        var resolved = (string)resolver.GetMethod("Resolve")!.Invoke(null, [null, local, profile])!;
        Path.IsPathFullyQualified(resolved).ShouldBeTrue();
        resolved.ShouldNotStartWith(AppContext.BaseDirectory);
        provider.GetRequiredService<HostLoadCaptureService>().ShouldNotBeNull();
    }

    [Test]
    public async Task Invalid_sample_is_dropped_with_serialization_reason_and_next_valid_sample_advances()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); var clock = Clock();
        using var capture = Capture(settings, clock, new CollectorLog());
        await capture.StartAsync(CancellationToken.None);
        try
        {
            var sample = new ScriptedHostStatsProbe().Read()!.Value with { At = clock.GetUtcNow() };
            capture.TryCapture(sample with { CpuPercent = double.NaN }).ShouldBeTrue();
            await C1514HostLoadFixture.EventuallyAsync(() => capture.DroppedSamples == 1);
            Diagnostics(capture).GetProperty("droppedByReason").GetProperty("append-validation-serialization").GetInt64().ShouldBe(1);
            await C1514HostLoadFixture.EventuallyAsync(() => Diagnostics(capture).GetProperty("failures").GetInt64() == 1);
            capture.TryCapture(sample).ShouldBeTrue(); clock.Advance(TimeSpan.FromSeconds(5));
            await C1514HostLoadFixture.EventuallyAsync(() => capture.LastAppendedAt is not null);
            var page = capture.ReadHistory(null, 120);
            page.Samples.Single().Source.Sequence.ShouldBe(2);
            page.Gaps.ShouldContain(g => g.FromSequence == 1);
            var json = Diagnostics(capture);
            json.GetProperty("appended").GetInt64().ShouldBe(1);
            json.GetProperty("authorityUnknown").GetInt64().ShouldBe(2);
        }
        finally { await capture.StopAsync(CancellationToken.None); }
    }

    [Test]
    public async Task Writable_store_counts_samples_and_persisted_metadata_cursor_advances()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); var clock = Clock();
        using var capture = Capture(settings, clock, new CollectorLog());
        await capture.StartAsync(CancellationToken.None);
        try
        {
            capture.TryCapture(new ScriptedHostStatsProbe().Read()!.Value with { At = clock.GetUtcNow() }).ShouldBeTrue();
            await C1514HostLoadFixture.EventuallyAsync(() => capture.LastAppendedAt is not null);
            var first = capture.ReadHistory(null, 1); first.Samples.Count.ShouldBe(1);
            var cursorPayload = JsonDocument.Parse(Convert.FromBase64String(first.NextCursor!.Split('.')[0]));
            cursorPayload.RootElement.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["from", "store", "through"]);
            capture.ReadHistory(first.NextCursor, 1).Samples.ShouldBeEmpty();
            capture.ReadHistory(first.NextCursor, 1).Samples.ShouldBeEmpty();
            var json = Diagnostics(capture);
            json.GetProperty("accepted").GetInt64().ShouldBe(1);
            json.GetProperty("appended").GetInt64().ShouldBe(1);
            json.GetProperty("cursorAdvances").GetInt64().ShouldBe(1);
            var reopened = new HostLoadSpool(settings.HistoryPath!, settings, clock);
            reopened.RunnerStoreId.ShouldBe(first.Source.RunnerStoreId);
            reopened.ReadHistory(null, 1).Samples.ShouldBeEmpty();
        }
        finally { await capture.StopAsync(CancellationToken.None); }
    }

    [Test]
    public async Task Full_queue_has_a_fixed_drop_reason_and_existing_surfaces_expose_diagnostics()
    {
        var settings = C1514HostLoadFixture.Config(fixture.PrivatePath()); settings.AppendQueueCapacity = 1;
        using var capture = Capture(settings, Clock(), new CollectorLog());
        capture.TryCapture(new ScriptedHostStatsProbe().Read()!.Value).ShouldBeTrue();
        capture.TryCapture(new ScriptedHostStatsProbe().Read()!.Value).ShouldBeFalse();
        var json = Diagnostics(capture);
        json.GetProperty("accepted").GetInt64().ShouldBe(1);
        json.GetProperty("dropped").GetInt64().ShouldBe(1);
        json.GetProperty("droppedByReason").GetProperty("queue-full").GetInt64().ShouldBe(1);
        await using var host = await HostStatsTestHost.StartAsync(new(), new ScriptedHostStatsProbe(), Clock());
        var stats = await host.Http.GetFromJsonAsync<JsonElement>("host-stats");
        stats.GetProperty("collector").GetProperty("accepted").GetInt64().ShouldBe(0);
        var response = await host.Http.GetAsync("host-stats/history");
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("collector").GetProperty("appended").GetInt64().ShouldBe(0);
    }

    private static JsonElement Diagnostics(HostLoadCaptureService capture)
    {
        var property = capture.GetType().GetProperty("Diagnostics");
        property.ShouldNotBeNull("collector failures must have bounded operator-readable diagnostics");
        return JsonSerializer.SerializeToElement(property.GetValue(capture), PhoneHomeFraming.Json);
    }
    private static FakeTimeProvider Clock() => new(C1514HostLoadFixture.T0);
    private static HostLoadCaptureService Capture(HostStatsSettings settings, FakeTimeProvider clock, CollectorLog log,
        Func<HostLoadSpool>? open = null) => new(Options.Create(settings), clock, new UnknownAuthority(), log, open: open);
    private sealed class UnknownAuthority : IHostAdmissionSnapshotReader
    {
        public Task<HostAdmissionSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HostAdmissionSnapshot.Unknown());
    }
    private sealed class CollectorLog : ILogger<HostLoadCaptureService>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Levels.Enqueue(logLevel); Lines.Enqueue(formatter(state, exception)); }
    }
}
