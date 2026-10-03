using System.Net.WebSockets;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Exceptions;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerCodexCliEvidenceTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static RunnerCapabilitiesDto Caps(string? version, DateTimeOffset? at, string? error = null) =>
        new("PortaPty", "PortaPty", "test", false, Version: "d40c1670",
            Features: [RunnerPlatformWire.Feature, "codex-cli-version-v1"], Platform: "linux",
            CodexCliVersion: version, CodexCliVersionCheckedAtUtc: at,
            CodexCliVersionError: error, CodexCliLauncherFingerprint: new string('a', 64));
    private static RunnerCodexCliVersionDto Sample(string? version, DateTimeOffset at, string? error = null) =>
        new(version, at, error, new string('a', 64));
    private static JsonElement Shape(object value) => JsonSerializer.SerializeToElement(value, Json);
    private static string? Text(JsonElement shape, string name) =>
        shape.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static bool? Flag(JsonElement shape, string name) =>
        shape.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : null;
    private static async Task Heartbeat(PhoneHomeTestHost host, PhoneHomeScriptedPeer peer,
        int capacity, RunnerCodexCliVersionDto? cli, long? epoch = null)
    {
        // Anonymous legacy-compatible wire shape allows this behavior to run before the member exists.
        await peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, epoch ?? peer.Epoch, Guid.NewGuid(),
            Payload: Shape(new { capacity, codexCli = cli })));
        // Health is allowed during recovery. Its reply is a receive-order barrier after the heartbeat.
        await new PhoneHomeRunnerClient(host.Directory.SnapshotLive(host.AllowedRunnerId)!)
            .GetHealthAsync(CancellationToken.None);
    }

    [Test]
    public async Task C959_Heartbeat_updates_only_probe_evidence()
    {
        using (var io = new ProbeIo())
        {
            await io.Probe.RefreshDefaultAsync(CancellationToken.None);
            await using var receiver = await PhoneHomeTestHost.StartAsync(io.Clock);
            await using var sender = await receiver.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
            var connected = await receiver.WaitLiveAsync();
            receiver.Directory.MarkRecovered(connected);
            using var controlled = new ControlledSendSocket(sender.Socket);
            var settings = new PhoneHomeSettings { RunnerId = receiver.AllowedRunnerId, Capacity = 2,
                CapacityStatePath = Path.Combine(io.Root, "capacity"), AllowedCwd = io.Root };
            using var producer = new PhoneHomeConnectionService(Options.Create(settings), new PhoneHomeAdoptionGate(),
                new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(io.Runtime,
                    new RunnerBuildDto("test", "d40c1670", T.UtcDateTime, T.UtcDateTime)), settings),
                io.Runtime, new UnusedHttpFactory(), io.Clock, NullLogger<PhoneHomeConnectionService>.Instance);
            var writer = new PhoneHomeConnectionWriter(controlled, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
            var hold = writer.SendAsync(new(PhoneHomeFrameKind.Heartbeat, sender.Epoch, Guid.NewGuid(),
                Payload: Shape(new { capacity = 2 })), CancellationToken.None);
            await controlled.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var heartbeat = producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None);
            try
            {
                heartbeat.IsCompleted.ShouldBeFalse("C959-v09-busy-writer");
                io.Clock.Advance(TimeSpan.FromMinutes(5));
            }
            finally
            {
                controlled.Release.TrySetResult();
                await Task.WhenAll(hold, heartbeat).WaitAsync(TimeSpan.FromSeconds(5));
            }
            await new PhoneHomeRunnerClient(connected).GetHealthAsync(CancellationToken.None);
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersionCheckedAtUtc")
                .ShouldBe(T.ToString("yyyy-MM-ddTHH:mm:sszzz"), "C959-v09-producer-original-time");
            controlled.PeakSends.ShouldBe(1, "C959-v09-one-writer");
            io.Mode = "nonzero";
            await io.Probe.RefreshDefaultAsync(CancellationToken.None);
            controlled.BeforeFailure = true;
            await Should.ThrowAsync<IOException>(() => producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None));
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersion").ShouldBe("0.160.0", "C959-v09-failed-send-no-projection");
            controlled.AfterFailure = true;
            await Should.ThrowAsync<IOException>(() => producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None));
            await new PhoneHomeRunnerClient(connected).GetHealthAsync(CancellationToken.None);
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersion").ShouldBeNull("C959-v09-accepted-frame-clears");
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersionError").ShouldBe("nonzero_exit", "C959-v09-production-failure-snapshot");
            var completed = io.Probe.Snapshot.CodexCliVersionCheckedAtUtc;
            io.Clock.Advance(TimeSpan.FromMinutes(1));
            await producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None);
            await new PhoneHomeRunnerClient(connected).GetHealthAsync(CancellationToken.None);
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersionCheckedAtUtc")
                .ShouldBe(completed!.Value.ToString("yyyy-MM-ddTHH:mm:sszzz"), "C959-v09-repeat-original-time");
        }
        var clock = new FakeTimeProvider(T);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync(capabilities: Caps("0.159.1", T));
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Heartbeat(host, peer, 2, Sample("0.160.0", T.AddMinutes(1)));
        var status = Shape(host.Directory.Status(host.AllowedRunnerId));
        Text(status, "codexCliVersion").ShouldBe("0.160.0", "C959-v09-newer");
        Text(status, "codexCliVersionCheckedAtUtc").ShouldBe(T.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:sszzz"), "C959-pc-075");
        live.Capacity.ShouldBe(2, "C959-v09-capacity");
        live.LastHeartbeatUtc.ShouldBe(clock.GetUtcNow(), "C959-v09-liveness");
        live.Capabilities!.CodexCliVersion.ShouldBe("0.159.1", "C959-v09-immutable-registration");
        clock.Advance(TimeSpan.FromMinutes(1));
        await Heartbeat(host, peer, 2, null);
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersionCheckedAtUtc")
            .ShouldBe(Text(status, "codexCliVersionCheckedAtUtc"), "C959-pc-078");
        await Heartbeat(host, peer, 2, Sample("0.9.0", T));
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBe("0.160.0", "C959-pc-080");
        await Heartbeat(host, peer, 2, Sample(null, T.AddMinutes(2), "nonzero_exit"));
        status = Shape(host.Directory.Status(host.AllowedRunnerId));
        Text(status, "codexCliVersion").ShouldBeNull("C959-pc-077");
        Text(status, "codexCliVersionError").ShouldBe("nonzero_exit", "C959-v09-failure");
    }

    [Test]
    public async Task C959_Freshness_boundaries()
    {
        foreach (var (completed, code, reason) in new (DateTimeOffset?, string?, string?)[]
        {
            (T.AddMinutes(-15), null, null), (T.AddMinutes(-15).AddTicks(-1), "codex_cli_version_stale", "evidence_expired"),
            (null, "codex_cli_version_unknown", "evidence_missing"), (T.AddMinutes(1), null, null),
            (T.AddMinutes(1).AddTicks(1), "codex_cli_version_unknown", "clock_skew"),
        })
        {
            var refusal = CodexCliAdmissionPolicy.Evaluate("desktop", "gpt-6.1-sol", "0.159.1",
                new("0.160.0", completed, null, new string('a',64)), T, 15);
            (refusal?.Code).ShouldBe(code, "C959-v10-admission-age " + completed);
            if (reason is not null) refusal!.Extensions!["reason"].ShouldBe(reason, "C959-v10-admission-reason");
        }
        foreach (var maxAge in new[] { 0, 1, 15, 60, 61 })
        {
            var settings = new DelegationSettings { CodexCliVersionMaxAgeMinutes = maxAge };
            new DelegationSettingsValidator(new FakeTimeProvider(T)).Validate(null, settings).Failed
                .ShouldBe(maxAge is 0 or 61, "C959-v10-server-age-config " + maxAge);
        }
        Should.Throw<InvalidOperationException>(() => new CodexCliVersionSettings { MaxAgeMinutes = 1, RefreshIntervalMinutes = 1 }.Validate());
        using (var io = new ProbeIo())
        {
            await using var probeHost = await PhoneHomeTestHost.StartAsync(io.Clock,
                configureServices: services => services.AddSingleton(io.Probe),
                mapEndpoints: app => app.MapCodexCliVersionRoutes());
            using var http = new HttpClient();
            var local = new SessionRunnerHttpClient(http, new UnusedHttpFactory(),
                Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings
                { BaseUrl = probeHost.Http.BaseAddress!.ToString() }), time: io.Clock);
            var descriptor = new CodexCliProbeDescriptor("desktop", "gpt-6.1-sol", null,
                new(io.Executable, io.Root));
            var settings = new DelegationSettings();
            var admitted = await CodexCliAdmissionPolicy.RequireAsync(descriptor, new SingleRunnerDirectory(local), settings, io.Clock, CancellationToken.None);
            admitted!.Sample!.CodexCliVersion.ShouldBe("0.160.0", "C959-v10-actual-refresh");
            io.Mode = "nonzero";
            io.Clock.Advance(TimeSpan.FromMinutes(5));
            var refused = await Should.ThrowAsync<CodexCliVersionRequiredException>(() =>
                CodexCliAdmissionPolicy.RequireAsync(descriptor, new SingleRunnerDirectory(local), settings, io.Clock, CancellationToken.None));
            refused.Code.ShouldBe("codex_cli_version_unknown", "C959-v10-failed-refresh-refuses");
            io.Starts.Count.ShouldBe(2, "C959-v10-bounded-refresh-count");
        }
        var clock = new FakeTimeProvider(T);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        clock.Advance(TimeSpan.FromMinutes(15));
        await Heartbeat(host, peer, 1, Sample("0.160.0", T));
        Flag(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersionStale").ShouldBe(false, "C959-pc-200");
        clock.Advance(TimeSpan.FromTicks(1));
        await Heartbeat(host, peer, 1, Sample("0.160.0", T));
        var status = Shape(host.Directory.Status(host.AllowedRunnerId));
        Flag(status, "codexCliVersionStale").ShouldBe(true, "C959-v10-stale");
        status.GetProperty("available").GetBoolean().ShouldBeTrue("C959-v10-independent-liveness");
        await Heartbeat(host, peer, 1, Sample("0.160.0", clock.GetUtcNow().AddMinutes(1)));
        Flag(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersionStale").ShouldBe(false, "C959-v10-skew-tolerance");
        await Heartbeat(host, peer, 1, Sample("0.160.0", clock.GetUtcNow().AddMinutes(1).AddTicks(1)));
        Flag(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersionStale").ShouldBeNull("C959-v10-clock-skew");
    }

    [Test]
    public async Task C959_Generation_change_clears_version()
    {
        foreach (var change in new[] { "boot", "epoch" })
        {
            var clock = new FakeTimeProvider(T);
            await using var generation = await PhoneHomeTestHost.StartAsync(clock);
            await using var before = await generation.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
            var oldLive = await generation.WaitLiveAsync();
            generation.Directory.MarkRecovered(oldLive);
            if (change == "boot")
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
                request.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, generation.Secret);
                request.Content = JsonContent.Create(generation.Registration(bootId: Guid.NewGuid()), options: Json);
                using var registrationResponse = await generation.Http.SendAsync(request);
                registrationResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict, "C959-v11-ownershipLive-boot-refused");
                Text(Shape(generation.Directory.Status(generation.AllowedRunnerId)), "codexCliVersion")
                    .ShouldBe("0.160.0", "C959-v11-unaccepted-registration-preserves");
                clock.Advance(TimeSpan.FromSeconds(91));
            }
            await generation.RegisterAsync(bootId: change == "boot" ? Guid.NewGuid() : generation.BootId);
            Text(Shape(generation.Directory.Status(generation.AllowedRunnerId)), "codexCliVersion")
                .ShouldBeNull("C959-v11-identity-clear " + change);
            oldLive.NoteHeartbeat(clock.GetUtcNow());
            Text(Shape(generation.Directory.Status(generation.AllowedRunnerId)), "codexCliVersion")
                .ShouldBeNull("C959-v11-no-prior-evidence " + change);
        }
        foreach (var clearRetirement in new[] { false, true })
        {
            var clock = new FakeTimeProvider(T);
            await using var ownership = await PhoneHomeTestHost.StartAsync(clock);
            using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
            var ownershipRegistration = ownership.Directory.Register(ownership.Registration() with { Capabilities = Caps("0.160.0", T) });
            var ownershipLive = ownership.Directory.AcceptConnect(ownership.AllowedRunnerId, ownershipRegistration.Ticket, socket);
            clock.Advance(TimeSpan.FromSeconds(91));
            ownershipLive.NoteHeartbeat(clock.GetUtcNow());
            if (clearRetirement)
            {
                ownership.Directory.ApplyState(ownership.AllowedRunnerId, new RunnerState(true, clock.GetUtcNow(), "C959", null, false, null, clock.GetUtcNow(), "C959"));
                ownership.Directory.ApplyState(ownership.AllowedRunnerId, new RunnerState(false, null, null, null, false, null, null, null));
            }
            ownership.Directory.Disconnect(ownershipLive, "C959 owned fixture disconnect");
            var replacement = ownership.Registration(storeId: Guid.NewGuid());
            clock.Advance(TimeSpan.FromSeconds(89));
            Should.Throw<ConflictException>(() => ownership.Directory.Register(replacement)).Code
                .ShouldBe(PhoneHomeProblemTypes.StoreMismatch, clearRetirement ? "C959-pc-207" : "C959-pc-208");
            ownership.Directory.GetLiveStoreId(ownership.AllowedRunnerId).ShouldBe(ownership.StoreId, "C959-v11-rejected-store-preserved");
            Text(Shape(ownership.Directory.Status(ownership.AllowedRunnerId)), "codexCliVersion")
                .ShouldBe("0.160.0", "C959-v11-rejected-evidence-preserved");
            clock.Advance(TimeSpan.FromSeconds(1));
            if (clearRetirement)
            {
                ownership.Directory.Register(replacement).RunnerStoreId.ShouldBe(replacement.RunnerStoreId, "C959-v11-authorized-store");
                Text(Shape(ownership.Directory.Status(ownership.AllowedRunnerId)), "codexCliVersion")
                    .ShouldBeNull("C959-v11-replacement-clears");
            }
            else
                Should.Throw<ConflictException>(() => ownership.Directory.Register(replacement)).Code.ShouldBe(PhoneHomeProblemTypes.StoreMismatch, "C959-pc-208-full-expiry");
        }
        await using var host = await PhoneHomeTestHost.StartAsync(new FakeTimeProvider(T));
        await using var first = await host.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
        var old = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(old);
        // Registration itself must clear CLI evidence, even while retaining general capabilities.
        await host.RegisterAsync();
        var descriptor = await host.Directory.DescribeAsync(host.AllowedRunnerId, CancellationToken.None);
        descriptor!.Capabilities!.CodexCliVersion.ShouldBeNull("C959-v11-register-clear");
        await using var next = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        live.Epoch.ShouldBeGreaterThan(old.Epoch, "C959-v11-epoch");
        await Heartbeat(host, next, 1, Sample("0.160.0", T.AddMinutes(1)), old.Epoch);
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBeNull("C959-v11-old-epoch");
        await Heartbeat(host, next, 1, Sample("0.159.1", T));
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBe("0.159.1", "C959-v11-new-epoch");
        using var refused = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
        refused.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, host.Secret);
        refused.Content = JsonContent.Create(host.Registration(storeId: Guid.NewGuid()), options: Json);
        using var response = await host.Http.SendAsync(refused);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, "C959-v11-store-owner");
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBe("0.159.1", "C959-v11-refusal-preserves");
    }

    [Test]
    public async Task C959_Catalogue_and_status_project_version()
    {
        var clock = new FakeTimeProvider(T);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        host.Local.Capabilities = Caps("0.159.1", T);
        await using var peer = await host.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        using var response = await host.Http.GetAsync("/api/session-runners");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var desktop = rows.EnumerateArray().Single(row => Text(row, "runnerId") == "desktop");
        var remote = rows.EnumerateArray().Single(row => Text(row, "runnerId") == host.AllowedRunnerId);
        Text(desktop, "codexCliVersion").ShouldBe("0.159.1", "C959-v12-desktop");
        Text(remote, "codexCliVersion").ShouldBe("0.160.0", "C959-v12-remote");
        remote.TryGetProperty("codexCliLauncherFingerprint", out _).ShouldBeFalse("C959-v12-private-fingerprint");
        using var own = await host.Http.GetAsync($"/api/session-runners/{host.AllowedRunnerId}/status");
        var status = JsonDocument.Parse(await own.Content.ReadAsStringAsync()).RootElement;
        Text(status, "buildVersion").ShouldBe("d40c1670", "C959-v12-build-separate");
        Text(status, "codexCliVersion").ShouldBe("0.160.0", "C959-v12-status");
        using var missing = await host.Http.GetAsync("/api/session-runners/missing-c959/status");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, "C959-v12-not-found");

        var settings = new PhoneHomeRunnerSettings { Enabled = true };
        foreach (var id in new[] { "runner-a", "runner-b", "runner-offline" })
            settings.Runners[id] = new() { Enabled = true, AllowDelegatedTasks = true, DisplayName = id,
                HostWorkspaceRoot = @"C:\work", RunnerWorkspace = "/work/" + id, SharedSecret = "c959-many",
                RunnerRepository = "/work/repos/antiphon", CallbackOrigin = "https://antiphon.test" };
        await using var many = await PhoneHomeTestHost.StartAsync(clock, configured: settings);
        many.Local.Capabilities = Caps("0.159.1", T);
        await using var a = await many.ConnectPeerAsync(runnerId: "runner-a", secret: "c959-many", capabilities: Caps("0.160.0", T));
        await using var b = await many.ConnectPeerAsync(runnerId: "runner-b", secret: "c959-many", capabilities: Caps("0.156.1", T));
        var liveA = await many.WaitLiveAsync(runnerId: "runner-a");
        var liveB = await many.WaitLiveAsync(runnerId: "runner-b");
        many.Directory.MarkRecovered(liveA); many.Directory.MarkRecovered(liveB);
        async Task<JsonElement> ListMany()
        {
            using var result = await many.Http.GetAsync("/api/session-runners");
            result.StatusCode.ShouldBe(HttpStatusCode.OK);
            return JsonDocument.Parse(await result.Content.ReadAsStringAsync()).RootElement;
        }
        var distinct = await ListMany();
        Text(distinct.EnumerateArray().Single(row => Text(row, "runnerId") == "runner-a"), "codexCliVersion")
            .ShouldBe("0.160.0", "C959-pc-099");
        Text(distinct.EnumerateArray().Single(row => Text(row, "runnerId") == "runner-b"), "codexCliVersion")
            .ShouldBe("0.156.1", "C959-pc-100");
        var offline = distinct.EnumerateArray().Single(row => Text(row, "runnerId") == "runner-offline");
        foreach (var field in new[] { "codexCliVersion", "codexCliVersionCheckedAtUtc", "codexCliVersionError", "codexCliVersionStale" })
            offline.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null, "C959-pc-102 " + field);
        Flag(offline, "codexCliVersionStale").ShouldBeNull("C959-pc-103");
        Flag(distinct.EnumerateArray().Single(row => Text(row, "runnerId") == "runner-a"), "codexCliVersionStale")
            .ShouldBe(false, "C959-pc-105");
        clock.Advance(TimeSpan.FromMinutes(16));
        await a.EmitAsync(new(PhoneHomeFrameKind.Heartbeat, a.Epoch, Guid.NewGuid(), Payload: Shape(new { capacity = 1 })));
        await new PhoneHomeRunnerClient(liveA).GetHealthAsync(CancellationToken.None);
        var aged = await ListMany();
        Flag(aged.EnumerateArray().Single(row => Text(row, "runnerId") == "runner-a"), "codexCliVersionStale")
            .ShouldBe(true, "C959-pc-104");
        await a.EmitAsync(new(PhoneHomeFrameKind.Heartbeat, a.Epoch, Guid.NewGuid(),
            Payload: Shape(new { capacity = 1, codexCli = Sample("0.159.1", clock.GetUtcNow()) })));
        await new PhoneHomeRunnerClient(liveA).GetHealthAsync(CancellationToken.None);
        Text(Shape(many.Directory.Status("runner-a")), "codexCliVersion").ShouldBe("0.159.1", "C959-pc-101");
        b.Socket.Abort();
        var end = Stopwatch.StartNew();
        while (liveB.SocketOpen && end.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(10);
        var unavailable = Shape(many.Directory.Status("runner-b"));
        Text(unavailable, "codexCliVersion").ShouldBe("0.156.1", "C959-v12-retained-display");
        unavailable.GetProperty("dispatchEligible").GetBoolean().ShouldBeFalse("C959-v12-disconnected-not-admitted");
    }

    [Test]
    public async Task C959_Exact_probe_transport_is_bound()
    {
        foreach (var variant in new[] { "request-id", "epoch", "operation", "503", "cancel" })
        {
            var correlationClock = new FakeTimeProvider(T);
            await using var correlationHost = await PhoneHomeTestHost.StartAsync(correlationClock);
            await using var correlationPeer = await correlationHost.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
            correlationHost.Directory.MarkRecovered(await correlationHost.WaitLiveAsync());
            correlationPeer.AutoReply = false;
            using var caller = new CancellationTokenSource();
            var selected = new RunnerScopedSessionRunnerClient(correlationHost.Directory, correlationHost.AllowedRunnerId);
            var pending = selected.GetCodexCliVersionAsync(new("/isolated/codex", "/isolated"), caller.Token);
            var sent = await correlationPeer.WaitForAsync((PhoneHomeOperation)33);
            try
            {
                if (variant == "cancel")
                {
                    caller.Cancel();
                    await Should.ThrowAsync<OperationCanceledException>(() => pending);
                }
                else
                {
                    await correlationPeer.EmitAsync(variant == "503"
                        ? new(PhoneHomeFrameKind.Error, sent.Epoch, sent.RequestId, sent.Operation,
                            ErrorCode: "unavailable", StatusCode: 503)
                        : new(PhoneHomeFrameKind.Result, variant == "epoch" ? sent.Epoch - 1 : sent.Epoch,
                            variant == "request-id" ? Guid.NewGuid() : sent.RequestId,
                            variant == "operation" ? PhoneHomeOperation.Health : sent.Operation,
                            Shape(Sample("0.160.0", T))));
                    correlationPeer.AutoReply = true;
                    await selected.GetHealthAsync(CancellationToken.None);
                    if (variant is "request-id" or "epoch")
                    {
                        pending.IsCompleted.ShouldBeFalse("C959-v13-unmatched-reply " + variant);
                        correlationClock.Advance(TimeSpan.FromSeconds(8));
                    }
                    (await pending.WaitAsync(TimeSpan.FromSeconds(5)))
                        .ShouldBeNull("C959-v13-refused-reply " + variant);
                }
                correlationPeer.RequestCount((PhoneHomeOperation)33).ShouldBe(1, "C959-v13-no-retry " + variant);
            }
            finally
            {
                caller.Cancel();
                try { await pending; } catch (OperationCanceledException) { }
            }
        }
        await using var host = await PhoneHomeTestHost.StartAsync(new FakeTimeProvider(T));
        await using var peer = await host.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var requests = new List<RunnerCodexCliProbeRequest>();
        peer.Reply = request => request.Operation == (PhoneHomeOperation)33
            ? Reply(request) : null;
        PhoneHomeFrame Reply(PhoneHomeFrame request)
        {
            requests.Add(request.Payload!.Value.Deserialize<RunnerCodexCliProbeRequest>(Json)!);
            return new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                Shape(Sample("0.159.1", T)));
        }
        var method = typeof(ISessionRunnerClient).GetMethod("GetCodexCliVersionAsync");
        var descriptor = new RunnerCodexCliProbeRequest("/isolated/codex", "/isolated", "/isolated/bin", null);
        var client = new RunnerScopedSessionRunnerClient(host.Directory, host.AllowedRunnerId);
        RunnerCodexCliVersionDto? sample = null;
        if (method is not null)
            sample = await (Task<RunnerCodexCliVersionDto?>)method.Invoke(client, [descriptor, CancellationToken.None])!;
        (sample?.CodexCliVersion).ShouldBe("0.159.1", "C959-v13-phone-home");
        requests.ShouldHaveSingleItem("C959-v13-no-retry");
        requests[0].ShouldBe(descriptor, "C959-v13-descriptor");
        var legacy = host.Local;
        (await (Task<RunnerCodexCliVersionDto?>)method!.Invoke(legacy, [descriptor, CancellationToken.None])!)
            .ShouldBeNull("C959-v13-legacy-default");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await (Task<RunnerCodexCliVersionDto?>)method.Invoke(client, [descriptor, cancelled.Token])!);

        using var io = new ProbeIo();
        await using var actual = await PhoneHomeTestHost.StartAsync(io.Clock,
            configureServices: services => services.AddSingleton(io.Probe),
            mapEndpoints: app => app.MapCodexCliVersionRoutes());
        using var http = new HttpClient();
        var localClient = new SessionRunnerHttpClient(http, new UnusedHttpFactory(),
            Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings
            { BaseUrl = actual.Http.BaseAddress!.ToString() }), time: io.Clock);
        var exact = new RunnerCodexCliProbeRequest(io.Executable, io.Root);
        var throughHttp = await (Task<RunnerCodexCliVersionDto?>)method.Invoke(localClient, [exact, CancellationToken.None])!;
        (throughHttp?.CodexCliVersion).ShouldBe("0.160.0", "C959-v13-http-route");
        io.Starts.Single().FileName.ShouldBe(io.Executable, "C959-v13-selected-native");
        io.Starts.Single().ArgumentList.ShouldBe(["--version"], "C959-v13-version-only");
        throughHttp!.CodexCliLauncherFingerprint!.Length.ShouldBe(64, "C959-v13-opaque");
        throughHttp.CodexCliLauncherFingerprint.Contains(io.Root, StringComparison.Ordinal).ShouldBeFalse("C959-v13-no-path");
        var dispatcher = new PhoneHomeCommandDispatcher(new PhoneHomeRuntimeAdapter(io.Runtime,
            new RunnerBuildDto("test", "d40c1670", T.UtcDateTime, T.UtcDateTime)),
            new PhoneHomeSettings { AllowedCwd = io.Root });
        await using var actualPeer = await actual.ConnectPeerAsync(capabilities: dispatcher.Capabilities());
        actual.Directory.MarkRecovered(await actual.WaitLiveAsync());
        actualPeer.Reply = request => dispatcher.DispatchAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        var bound = new RunnerScopedSessionRunnerClient(actual.Directory, actual.AllowedRunnerId);
        var throughSocket = await (Task<RunnerCodexCliVersionDto?>)method.Invoke(bound, [exact, CancellationToken.None])!;
        throughSocket.ShouldBe(throughHttp, "C959-v13-actual-dispatch");
        io.Starts.Count.ShouldBe(1, "C959-v13-shared-exact-cache");
        actual.Local.Calls.ShouldBeEmpty("C959-pc-108");
        (await actual.Directory.DescribeAsync(actual.AllowedRunnerId, CancellationToken.None))!
            .Capabilities!.CodexCliVersion.ShouldBeNull("C959-v13-exact-does-not-overwrite-default");
        foreach (var invalid in new[]
        {
            exact with { Executable = new string('X', 65537) }, exact with { ResolutionCwd = new string('X', 65537) },
            exact with { Path = new string('X', 65537) }, exact with { ResolutionCwd = "${secret:C959}" },
            exact with { Path = "${secret:C959}" }, exact with { Executable = "codex\0" },
            exact with { Path = new string('X', CodexCliVersionProbe.DescriptorFieldLimit + 1) },
        })
        {
            var unknown = await (Task<RunnerCodexCliVersionDto?>)method.Invoke(localClient, [invalid, CancellationToken.None])!;
            (unknown?.CodexCliVersionError).ShouldBe("launcher_unverified", "C959-pc-117/118");
            io.Starts.Count.ShouldBe(1, "C959-v13-invalid-no-child");
        }
        var atLimit = exact with { Path = new string('X', CodexCliVersionProbe.DescriptorFieldLimit) };
        (await (Task<RunnerCodexCliVersionDto?>)method.Invoke(localClient, [atLimit, CancellationToken.None])!)!
            .CodexCliVersion.ShouldBe("0.160.0", "C959-v13-limit-equality");
        actualPeer.Reply = request => request.Operation == (PhoneHomeOperation)33
            ? new(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId, request.Operation,
                ErrorCode: PhoneHomeProblemTypes.UnsupportedOperation, StatusCode: 409) : null;
        (await (Task<RunnerCodexCliVersionDto?>)method.Invoke(bound, [exact, CancellationToken.None])!)
            .ShouldBeNull("C959-pc-112");
        actualPeer.AutoReply = false;
        actualPeer.Reply = _ => null;
        var silent = (Task<RunnerCodexCliVersionDto?>)method.Invoke(bound, [exact, CancellationToken.None])!;
        await actualPeer.WaitForAsync((PhoneHomeOperation)33);
        io.Clock.Advance(TimeSpan.FromSeconds(8));
        (await silent.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeNull("C959-pc-114");
    }

    private sealed class ControlledSendSocket(WebSocket inner) : WebSocket
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sends;
        public int PeakSends { get; private set; }
        public bool BeforeFailure { get; set; }
        public bool AfterFailure { get; set; }
        public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
        public override string? CloseStatusDescription => inner.CloseStatusDescription;
        public override string? SubProtocol => inner.SubProtocol;
        public override WebSocketState State => inner.State;
        public override void Abort() => inner.Abort();
        public override void Dispose() { Release.TrySetResult(); }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => inner.CloseAsync(status, description, ct);
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => inner.CloseOutputAsync(status, description, ct);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) => inner.ReceiveAsync(buffer, ct);
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct)
        {
            var sends = Interlocked.Increment(ref _sends);
            PeakSends = Math.Max(PeakSends, sends);
            try
            {
                if (!Entered.Task.IsCompleted) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
                if (BeforeFailure) { BeforeFailure = false; throw new IOException("C959 before send"); }
                await inner.SendAsync(buffer, type, end, ct);
                if (AfterFailure) { AfterFailure = false; throw new IOException("C959 lost send acknowledgment"); }
            }
            finally { Interlocked.Decrement(ref _sends); }
        }
    }

    private sealed class UnusedHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Probe entered a named GET client.");
    }

    private sealed class ProbeIo : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c959-wire-" + Guid.NewGuid().ToString("N"));
        public string Executable { get; }
        public FakeTimeProvider Clock { get; } = new(T);
        public List<ProcessStartInfo> Starts { get; } = [];
        public string Mode { get; set; } = "success";
        private readonly List<Process> _children = [];
        public CodexCliVersionProbe Probe { get; }
        public SessionRunnerRuntime Runtime { get; }
        public ProbeIo()
        {
            Directory.CreateDirectory(Root);
            Executable = Path.Combine(Root, OperatingSystem.IsWindows() ? "codex.exe" : "codex");
            File.Copy(Environment.ProcessPath!, Executable);
            Runtime = new(Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings { SessionLogPath = Root }),
                NullLogger<SessionRunnerRuntime>.Instance, timeProvider: Clock);
            Probe = new(Clock, new PhoneHomeProcessIdentity(), Options.Create(new CodexCliVersionSettings
                { Executable = Executable, ResolutionCwd = Root }), Start);
            typeof(SessionRunnerRuntime).GetProperty("CodexCliProbe", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Runtime, Probe);
        }
        private Process Start(ProcessStartInfo actual)
        {
            Starts.Add(actual);
            var script = Path.Combine(Environment.CurrentDirectory, "tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1");
            for (var dir = new DirectoryInfo(Environment.CurrentDirectory); !File.Exists(script) && dir.Parent is not null; dir = dir.Parent)
                script = Path.Combine(dir.Parent.FullName, "tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1");
            var child = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = actual.WorkingDirectory };
            child.Environment.Clear();
            foreach (var pair in actual.Environment) child.Environment[pair.Key] = pair.Value;
            var receipts = Path.Combine(Root, "receipts-" + Starts.Count);
            Directory.CreateDirectory(receipts);
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script,
                         "-Mode", Mode, "-ReceiptRoot", receipts }) child.ArgumentList.Add(arg);
            var process = Process.Start(child)!;
            _children.Add(Process.GetProcessById(process.Id));
            return process;
        }
        public void Dispose()
        {
            foreach (var child in _children)
            {
                if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); }
                child.Dispose();
            }
            Probe.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
