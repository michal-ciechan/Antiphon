using System.Net;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1763: a pty buffer or snapshot that still matches the caller's validator is 304
/// with no second read. Herdr, a missing header, and a changed tuple stay a full body.
/// </summary>
[Category("Unit")]
public class PtyConditionalReadTests
{
    [Test]
    public async Task Quiet_pty_buffer_returns_304_and_does_not_reread_the_log()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        File.WriteAllText(session.AnsiPath!, "hello");
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var first = await host.Get($"/sessions/{session.Id}/buffer");
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await first.Content.ReadAsStringAsync()).ShouldContain("hello");
            var etag = first.Headers.ETag!.Tag;
            session.AnsiLogReads.ShouldBe(1);

            var second = await host.Get($"/sessions/{session.Id}/buffer", etag);
            second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
            (await second.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
            second.Headers.ETag!.Tag.ShouldBe(etag);
            session.AnsiLogReads.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Appended_log_at_the_same_sequence_returns_the_new_tail()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        File.WriteAllText(session.AnsiPath!, "hello");
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var first = await host.Get($"/sessions/{session.Id}/buffer");
            var etag = first.Headers.ETag!.Tag;
            File.AppendAllText(session.AnsiPath!, "-more");

            var second = await host.Get($"/sessions/{session.Id}/buffer", etag);
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await second.Content.ReadAsStringAsync()).ShouldContain("hello-more");
            session.AnsiLogReads.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Quiet_pty_snapshot_returns_304_and_does_not_copy_the_live_buffer()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        session.OutputForTest(1);
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var first = await host.Get($"/sessions/{session.Id}/snapshot");
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await first.Content.ReadAsStringAsync()).ShouldContain("fixture output");
            var etag = first.Headers.ETag!.Tag;
            session.LiveBufferMaterializations.ShouldBe(1);

            var second = await host.Get($"/sessions/{session.Id}/snapshot", etag);
            second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
            (await second.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
            session.LiveBufferMaterializations.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Input_revision_change_returns_a_new_snapshot()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        session.OutputForTest(1);
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var first = await host.Get($"/sessions/{session.Id}/snapshot");
            var etag = first.Headers.ETag!.Tag;
            session.NoteInputForTest();

            var second = await host.Get($"/sessions/{session.Id}/snapshot", etag);
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
            var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("dialog").GetProperty("inputRevision").GetInt64().ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Cleared_live_buffer_returns_the_empty_text()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        session.OutputForTest(1);
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var first = await host.Get($"/sessions/{session.Id}/snapshot");
            var etag = first.Headers.ETag!.Tag;
            await runtime.ClearLiveBufferAsync(session.Id, CancellationToken.None);

            var second = await host.Get($"/sessions/{session.Id}/snapshot", etag);
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
            var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("rawOutput").GetString().ShouldBe("");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Herdr_snapshot_ignores_if_none_match_and_reads_the_pane()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        var pane = new PaneChild();
        session.BindChildForTest(pane);
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var response = await host.Get($"/sessions/{session.Id}/snapshot", "\"s1.pretend\"");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.ETag.ShouldBeNull();
            (await response.Content.ReadAsStringAsync()).ShouldContain("pane-text");
            pane.Reads.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Missing_or_weak_header_returns_the_full_body()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        File.WriteAllText(session.AnsiPath!, "hello");
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var open = await host.Get($"/sessions/{session.Id}/buffer");
            open.StatusCode.ShouldBe(HttpStatusCode.OK);
            open.Headers.ETag.ShouldNotBeNull();
            var etag = open.Headers.ETag!.Tag;

            var star = await host.Get($"/sessions/{session.Id}/buffer", "*");
            star.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await star.Content.ReadAsStringAsync()).ShouldContain("hello");

            var weak = await host.Get($"/sessions/{session.Id}/buffer", "W/" + etag);
            weak.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await weak.Content.ReadAsStringAsync()).ShouldContain("hello");
            session.AnsiLogReads.ShouldBe(3);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Non_pty_buffer_has_no_validator()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        File.WriteAllText(session.AnsiPath!, "hello");
        session.SetTerminalBackendStateForTest(backend: SessionBackends.Herdr);
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var response = await host.Get($"/sessions/{session.Id}/buffer", "\"b1.pretend\"");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.ETag.ShouldBeNull();
            (await response.Content.ReadAsStringAsync()).ShouldContain("hello");
            session.AnsiLogReads.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Pending_herdr_snapshot_stays_unavailable()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        session.SetTerminalBackendStateForTest(pendingReason: "adopting");
        await using var host = await Host.StartAsync(runtime);
        try
        {
            var response = await host.Get($"/sessions/{session.Id}/snapshot");
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Capabilities_advertise_pty_conditional_read()
    {
        PhoneHomeRuntimeAdapter.StaticFeatures.ShouldContain(RunnerCapabilityFeatures.PtyConditionalReadV1);
        foreach (var enabled in new[] { false, true })
        {
            var (runtime, _, root) = Arm();
            await using var _runtime = runtime;
            try
            {
                await using var host = await Host.StartAsync(runtime, enabled);
                var response = await host.Get("/capabilities");
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).ShouldContain(RunnerCapabilityFeatures.PtyConditionalReadV1);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public async Task Phone_home_unchanged_buffer_is_not_an_empty_screen()
    {
        var (runtime, session, root) = Arm();
        await using var _ = runtime;
        File.WriteAllText(session.AnsiPath!, "hello");
        var dispatcher = new PhoneHomeCommandDispatcher(
            new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve()),
            new PhoneHomeSettings { Enabled = true, AllowedCwd = root, CapacityStatePath = Path.Combine(root, "capacity") });
        try
        {
            var first = await dispatcher.DispatchAsync(BufferFrame(session.Id, null), CancellationToken.None);
            first.Kind.ShouldBe(PhoneHomeFrameKind.Result);
            PtyConditionalSignals.IsNotModified(first.Payload).ShouldBeFalse();
            first.Match.ShouldNotBeNullOrWhiteSpace();
            session.AnsiLogReads.ShouldBe(1);

            var second = await dispatcher.DispatchAsync(BufferFrame(session.Id, first.Match), CancellationToken.None);
            second.Kind.ShouldBe(PhoneHomeFrameKind.Result);
            PtyConditionalSignals.IsNotModified(second.Payload).ShouldBeTrue();
            second.Payload!.Value.TryGetProperty("buffer", out var bufferProperty).ShouldBeFalse();
            bufferProperty.ValueKind.ShouldBe(default);
            second.Match.ShouldBe(first.Match);
            session.AnsiLogReads.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static PhoneHomeFrame BufferFrame(Guid sessionId, string? ifNoneMatch) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Buffer,
            JsonSerializer.SerializeToElement(
                ifNoneMatch is null ? new { sessionId } : (object)new { sessionId, ifNoneMatch },
                PhoneHomeFraming.Json));

    private static (SessionRunnerRuntime Runtime, ArmedSession Session, string Root) Arm()
    {
        var root = Path.Combine(Path.GetTempPath(), "pty-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = new SessionRunnerSettings { SessionLogPath = root };
        var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var id = Guid.NewGuid();
        var ansi = Path.Combine(root, "session.ansi");
        var session = new SessionRunnerRuntime.RunnerSession(
            id, settings, new SessionRunnerEventHub(), NullLogger.Instance);
        session.ArmPtyReadForTest(new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc), ansi);
        runtime.Track(session);
        return (runtime, new ArmedSession(id, ansi, session), root);
    }

    private sealed class ArmedSession(Guid id, string ansiPath, SessionRunnerRuntime.RunnerSession session)
    {
        public Guid Id { get; } = id;
        public string AnsiPath { get; } = ansiPath;
        public int AnsiLogReads => session.AnsiLogReads;
        public int LiveBufferMaterializations => session.LiveBufferMaterializations;
        public void OutputForTest(long sequence) => session.OutputForTest(sequence);
        public void NoteInputForTest() => session.NoteInputForTest();
        public void BindChildForTest(ISessionChild child) => session.BindChildForTest(child);
        public void SetTerminalBackendStateForTest(string status = "Running", string? pendingReason = null, string backend = SessionBackends.PtyHost) =>
            session.SetTerminalBackendStateForTest(status, pendingReason, backend);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Host(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<Host> StartAsync(SessionRunnerRuntime runtime, bool herdr = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(runtime);
            builder.Services.Configure<HerdrSettings>(settings => settings.Enabled = herdr);
            builder.Services.Configure<HostStatsSettings>(_ => { });
            var app = builder.Build();
            app.UseRunnerExceptionMapping();
            app.MapRunnerCapabilitiesRoute(new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow));
            app.MapSessionObservationRoutes();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            new Uri(address).Port.ShouldNotBe(17204);
            return new Host(app, new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) });
        }

        public Task<HttpResponseMessage> Get(string path, string? ifNoneMatch = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (ifNoneMatch is not null)
                request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            return _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _client.Dispose();
        }
    }

    private sealed class PaneChild : ISessionChild
    {
        public int Reads { get; private set; }
        public Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteAsync(string input, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> KillAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<ChildScreen?> ReadScreenAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<ChildScreen?>(new ChildScreen("pane-text", 3, 4));
        }
        public event Action<ChildExit>? Exited { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
