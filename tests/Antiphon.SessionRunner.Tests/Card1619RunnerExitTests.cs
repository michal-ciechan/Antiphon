using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests.TestHelpers;
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
using TUnit.Core.Exceptions;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NativeLinuxOnly("CARD-1619 requires real Linux host and runner exit observation")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619RunnerExitTests
{
    [Test]
    public async Task Exit_while_detached_survives_adoption()
    {
        await using var fixture = await NativeFixture.StartAsync();
        await fixture.DetachAndSignalAsync(15);
        var runtime = fixture.NewRuntime();
        var events = runtime.Subscribe(default);
        (await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), default)).ShouldBe(1);
        var exit = await ExitAsync(events);
        AssertExit(runtime.Get(fixture.Id), exit, fixture.Generation, 15);
        runtime.Get(fixture.Id).Adopted.ShouldBeTrue();
    }

    [Test]
    public async Task Dead_host_manifest_preserves_signal()
    {
        await using var fixture = await NativeFixture.StartAsync();
        await fixture.DetachAndSignalAsync(9);
        await fixture.StopHostAsync();
        var runtime = fixture.NewRuntime();
        var events = runtime.Subscribe(default);
        await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), default);
        AssertExit(runtime.Get(fixture.Id), await ExitAsync(events), fixture.Generation, 9);
    }

    [Test]
    public async Task Old_host_adoption_keeps_signal_unknown()
    {
        await using var fixture = await NativeFixture.StartAsync();
        await fixture.DetachAndSignalAsync(9);
        await fixture.StopHostAsync();
        var old = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        old.Remove("exitSignal");
        File.WriteAllText(fixture.ManifestPath, old.ToJsonString());
        var runtime = fixture.NewRuntime();
        var events = runtime.Subscribe(default);
        await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), default);
        AssertExit(runtime.Get(fixture.Id), await ExitAsync(events), fixture.Generation, null);
    }

    [Test]
    public async Task Http_exit_and_list_keep_signal()
    {
        await using var fixture = await NativeFixture.StartAsync();
        var runtime = fixture.Runtime;
        var events = runtime.Subscribe(default);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(runtime);
        await using var app = builder.Build();
        app.MapSessionGetRoute();
        // Same typed list endpoint as Program; no test-side DTO mapping.
        app.MapGet("/sessions", (SessionRunnerRuntime value) => value.List());
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            new Uri(address).Port.ShouldNotBe(17204);
            new Uri(address).Port.ShouldNotBe(8080);
            using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
            fixture.Signal(9);
            var exit = await ExitAsync(events);
            var dto = (await http.GetFromJsonAsync<RunnerSessionDto>($"/sessions/{fixture.Id}"))!;
            AssertExit(dto, exit, fixture.Generation, 9);
            var list = (await http.GetFromJsonAsync<RunnerSessionDto[]>("/sessions"))!;
            list.Single(value => value.SessionId == fixture.Id).ExitSignal.ShouldBe(9);
        }
        finally { await app.StopAsync(); }
    }

    [Test]
    public async Task Phone_home_exit_and_recovery_keep_signal()
    {
        await using var fixture = await NativeFixture.StartAsync();
        var runtime = fixture.Runtime;
        var events = runtime.Subscribe(default);
        var surface = new PhoneHomeRuntimeAdapter(runtime, new RunnerBuildDto("test", "test", DateTime.UnixEpoch, DateTime.UnixEpoch));
        var settings = new PhoneHomeSettings { CapacityStatePath = Path.Combine(fixture.Root, "capacity"),
            StoreIdPath = Path.Combine(fixture.Root, "store"), AllowedCwd = fixture.Root };
        var dispatcher = new PhoneHomeCommandDispatcher(surface, settings);
        using var service = new PhoneHomeConnectionService(Options.Create(settings), new PhoneHomeAdoptionGate(),
            dispatcher, runtime, new UnusedHttpFactory(), TimeProvider.System, NullLogger<PhoneHomeConnectionService>.Instance);
        fixture.Signal(15);
        var envelope = await ExitEnvelopeAsync(events);
        using var socket = new PhoneHomeTestWebSocket();
        var writer = new PhoneHomeConnectionWriter(socket, 1024 * 1024);
        await service.SendEventAsync(writer, 7, envelope, default);
        var exit = socket.Sent.Single().Payload!.Value.Deserialize<RunnerSessionExitedEvent>(PhoneHomeFraming.Json)!;
        AssertExit(runtime.Get(fixture.Id), exit, fixture.Generation, 15);
        // A reconnect obtains the registry through the actual List command dispatcher.
        var reply = await dispatcher.DispatchAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Request, 8,
            Guid.NewGuid(), Operation: PhoneHomeOperation.List), default);
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        reply.Payload!.Value.Deserialize<RunnerSessionDto[]>(PhoneHomeFraming.Json)!
            .Single(value => value.SessionId == fixture.Id).ExitSignal.ShouldBe(15);
    }

    [Test]
    public async Task Stale_generation_does_not_replace_exit_evidence()
    {
        await using var fixture = await NativeFixture.StartAsync();
        await fixture.DetachAndSignalAsync(15);
        var old = PtyHostManifest.TryLoad(fixture.ManifestPath)!;
        await fixture.StopHostAsync();
        var runtime = fixture.NewRuntime();
        var generationB = fixture.Generation.AddSeconds(1);
        var current = await fixture.LaunchAsync(runtime, generationB);
        old.SaveAtomic(fixture.ManifestPath);
        var events = runtime.Subscribe(default);
        await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), default);
        var stale = await ExitAsync(events);
        stale.AcceptedStartedAt.ShouldBe(fixture.Generation);
        stale.ExitSignal.ShouldBe(15);
        var b = runtime.Get(fixture.Id);
        b.AcceptedStartedAt.ShouldBe(generationB);
        b.Status.ShouldBe("Running");
        b.ExitSignal.ShouldBeNull();
        using var childB = Process.GetProcessById(current.Pid!.Value);
        Kill(childB.Id, 9).ShouldBe(0);
        AssertExit(await fixture.WaitExitedAsync(runtime), await ExitAsync(events), generationB, 9);
    }

    private static void AssertExit(RunnerSessionDto dto, RunnerSessionExitedEvent exit, DateTime generation, int? signal)
    {
        dto.ExitSignal.ShouldBe(signal);
        exit.ExitSignal.ShouldBe(signal);
        dto.ExitCode.ShouldBe(0);
        exit.ExitCode.ShouldBe(0);
        dto.Status.ShouldBe("Exited");
        dto.AcceptedStartedAt.ShouldBe(generation);
        exit.AcceptedStartedAt.ShouldBe(generation);
        exit.SessionId.ShouldBe(dto.SessionId);
    }

    private static async Task<RunnerServerSentEvent> ExitEnvelopeAsync(ChannelReader<RunnerServerSentEvent> events)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await events.WaitToReadAsync(cts.Token))
            while (events.TryRead(out var value))
                if (value.EventName == SessionRunnerEventNames.SessionExited) return value;
        throw new InvalidOperationException("No exit event");
    }
    private static async Task<RunnerSessionExitedEvent> ExitAsync(ChannelReader<RunnerServerSentEvent> events) =>
        JsonSerializer.Deserialize<RunnerSessionExitedEvent>((await ExitEnvelopeAsync(events)).Json, PhoneHomeFraming.Json)!;

    private sealed class NativeFixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "card1619-runner-" + Guid.NewGuid().ToString("N"));
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Generation { get; } = SessionGeneration.Normalize(DateTime.UtcNow);
        private readonly List<SessionRunnerRuntime> _runtimes = [];
        private readonly List<Process> _owned = [];
        public SessionRunnerRuntime Runtime { get; private set; } = null!;
        private Process _child = null!, _host = null!;
        private readonly Dictionary<int, string> _identities = [];
        public string ManifestPath => PtyHostManifest.PathFor(Path.Combine(Root, "pty-hosts", "manifests"), Id);

        public static async Task<NativeFixture> StartAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new SkipTestException("Real Linux PTY witness");
            var fixture = new NativeFixture();
            try
            {
                fixture.Runtime = fixture.NewRuntime();
                var dto = await fixture.LaunchAsync(fixture.Runtime, fixture.Generation);
                fixture._child = Process.GetProcessById(dto.Pid!.Value);
                fixture._host = Process.GetProcessById(dto.HostPid!.Value);
                fixture._owned.Add(fixture._child);
                fixture._owned.Add(fixture._host);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public SessionRunnerRuntime NewRuntime()
        {
            Directory.CreateDirectory(Root);
            var value = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings {
                SessionLogPath = Root, PtyHostLingerHours = 0.02 }), NullLogger<SessionRunnerRuntime>.Instance);
            _runtimes.Add(value);
            return value;
        }
        public async Task<RunnerSessionDto> LaunchAsync(SessionRunnerRuntime runtime, DateTime generation)
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessSpawn, "card1619-host-launch-and-readiness");
            var dto = await runtime.StartAsync(new RunnerLaunchRequest(Id, "/bin/sh",
                ["-c", "printf 'card1619-ready\\n'; read line"], new Dictionary<string, string>(), Root,
                80, 24, AcceptedStartedAt: generation), default);
            // Track every launched process before readiness/assertions, including generation B.
            _owned.Add(Process.GetProcessById(dto.Pid!.Value));
            _owned.Add(Process.GetProcessById(dto.HostPid!.Value));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!runtime.GetBuffer(Id).Buffer.Contains("card1619-ready") && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            runtime.GetBuffer(Id).Buffer.ShouldContain("card1619-ready");
            dto.Status.ShouldBe("Running");
            var manifest = PtyHostManifest.TryLoad(ManifestPath)!;
            manifest.ChildStartTimeUtc.ShouldBe(dto.StartedAt);
            _identities[dto.Pid.Value] = StartTicks(dto.Pid.Value);
            return dto;
        }
        public void Signal(int signal)
        {
            _child.HasExited.ShouldBeFalse();
            StartTicks(_child.Id).ShouldBe(_identities[_child.Id], "owned PID/start ticks before external signal");
            Kill(_child.Id, signal).ShouldBe(0);
        }
        public async Task DetachAndSignalAsync(int signal)
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "card1619-detached-exit-and-manifest");
            await Runtime.DisposeAsync();
            Signal(signal);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (PtyHostManifest.TryLoad(ManifestPath)?.ExitedAtUtc is null && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            PtyHostManifest.TryLoad(ManifestPath)!.ExitedAtUtc.ShouldNotBeNull();
        }
        public async Task StopHostAsync()
        {
            if (!_host.HasExited) _host.Kill();
            await _host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async Task<RunnerSessionDto> WaitExitedAsync(SessionRunnerRuntime runtime)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (runtime.Get(Id).Status != "Exited" && DateTime.UtcNow < deadline) await Task.Delay(20);
            return runtime.Get(Id);
        }
        public async ValueTask DisposeAsync()
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "card1619-owned-process-join");
            foreach (var runtime in _runtimes) await runtime.DisposeAsync();
            foreach (var owned in _owned)
            {
                try { if (!owned.HasExited) owned.Kill(true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (InvalidOperationException) { }
                finally { owned.Dispose(); }
            }
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        private static string StartTicks(int pid)
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19];
        }
    }

    private sealed class UnusedHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No registration in this witness");
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
