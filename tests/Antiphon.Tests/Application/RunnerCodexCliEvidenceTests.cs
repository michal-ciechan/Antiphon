using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
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
        new("PortaPty", "PortaPty", "test", false, Version: "build-sha",
            Features: [RunnerPlatformWire.Feature, "codex-cli-version-v1"], Platform: "linux",
            CodexCliVersion: version, CodexCliVersionCheckedAtUtc: at,
            CodexCliVersionError: error, CodexCliLauncherFingerprint: "opaque-fingerprint");
    private static RunnerCodexCliVersionDto Sample(string? version, DateTimeOffset at, string? error = null) =>
        new(version, at, error, "opaque-fingerprint");
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
        Text(status, "buildVersion").ShouldBe("build-sha", "C959-v12-build-separate");
        Text(status, "codexCliVersion").ShouldBe("0.160.0", "C959-v12-status");
        using var missing = await host.Http.GetAsync("/api/session-runners/missing-c959/status");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, "C959-v12-not-found");
    }

    [Test]
    public async Task C959_Exact_probe_transport_is_bound()
    {
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
    }
}
