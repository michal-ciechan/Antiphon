using System.Net.WebSockets;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Exceptions;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
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
    public async Task C1031_Advisory_diagnostics_preserve_freshness()
    {
        // Actual producer bytes read by frozen old shapes, independent literal expected values.
        using (var io = new ProbeIo())
        {
            foreach (var (mode, token) in new[] { ("notice", "stderr_output"), ("stderr-4097", "output_truncated") })
            {
                io.Mode = mode;
                await io.Probe.RefreshDefaultAsync(CancellationToken.None);
                var adapter = new PhoneHomeRuntimeAdapter(io.Runtime, new RunnerBuildDto("test", "d40c1670", T.UtcDateTime, T.UtcDateTime));
                var shapes = new[] { Shape(io.Probe.Snapshot), Shape(adapter.Capabilities()),
                    Shape(io.Runtime.DescribeCapabilities(new("test", "d40c1670", T.UtcDateTime, T.UtcDateTime), [SessionBackends.PtyHost], [])),
                    Shape(new PhoneHomeCapacityHeartbeat(2, io.Probe.Snapshot)).GetProperty("codexCli") };
                foreach (var shape in shapes)
                {
                    foreach (var name in new[] { "codexCliVersion", "codexCliVersionCheckedAtUtc", "codexCliVersionError", "codexCliLauncherFingerprint" })
                        shape.TryGetProperty(name, out _).ShouldBeTrue("C1031-wire-members " + name);
                    var old = shape.Deserialize<LegacyC959Sample>(Json)!;
                    old.CodexCliVersion.ShouldBe("0.160.0", "C1031-old-reader");
                    old.CodexCliVersionCheckedAtUtc.ShouldBe(T, "C1031-old-reader time");
                    old.CodexCliVersionError.ShouldBe(token, "C1031-wire-members");
                    old.CodexCliLauncherFingerprint!.Length.ShouldBe(64, "C1031-old-reader fingerprint");
                    LegacyStale(old, T).ShouldBeNull("C1031-old-reader advisory freshness");
                    shape.Deserialize<PreC959Reader>(Json).ShouldNotBeNull("C1031-old-reader ignores new fields");
                }
            }
        }

        var vectors = new List<(RunnerCapabilitiesDto Caps, bool? Stale, string? Error, string Label)>();
        foreach (var advisory in new[] { "stderr_output", "output_truncated" })
        {
            vectors.AddRange(new (RunnerCapabilitiesDto, bool?, string?, string)[]
            {
                (Caps("0.160.0", T, advisory), false, advisory, "C1031-advisory-fresh"),
                (Caps("0.160.0", T.AddMinutes(-15), advisory), false, advisory, "C1031-age-boundary"),
                (Caps("0.160.0", T.AddMinutes(-15).AddTicks(-1), advisory), true, advisory, "C1031-age-boundary after"),
                (Caps("0.160.0", T.AddMinutes(1), advisory), false, advisory, "C1031-skew equality"),
                (Caps("0.160.0", T.AddMinutes(1).AddTicks(1), advisory), null, "clock_skew", "C1031-skew"),
                (Caps("0.160.0", T.AddMinutes(1).AddTicks(1), advisory) with { CodexCliLauncherFingerprint = "bad" }, null, "clock_skew", "C1031-skew priority"),
                (Caps("0.160.0", T, advisory) with { CodexCliLauncherFingerprint = null }, false, advisory, "C1031-legacy-fingerprint"),
                (Caps(null, T, advisory), null, advisory, "C1031-version-required absent"),
                (Caps("banana", T, advisory), null, advisory, "C1031-version-required"),
                (Caps("0.160.0", null, advisory), null, advisory, "C1031-time-required"),
            });
            foreach (var fingerprint in new[] { "", new string('a', 63), new string('a', 65), new string('z', 64) })
                vectors.Add((Caps("0.160.0", T, advisory) with { CodexCliLauncherFingerprint = fingerprint },
                    null, "launcher_mismatch", "C1031-fingerprint"));
        }
        foreach (var error in new[] { "invalid_output", "executable_missing", "nonzero_exit", "timeout", "cancelled",
                     "cleanup_unconfirmed", "launcher_unverified", "probe_busy", "probe_unavailable" })
        {
            vectors.Add((Caps("0.160.0", T, error), null, error, "C1031-failure-null"));
            vectors.Add((Caps("0.160.0", T.AddMinutes(2), error) with { CodexCliLauncherFingerprint = "bad" },
                null, error, "C1031-failure-null priority"));
        }
        foreach (var error in new[] { "STDERR_OUTPUT", "Output_Truncated", "clock_skew", "launcher_mismatch",
                     @"C:\Users\C1031\private; /home/C1031/private; C1031-token-canary" })
            vectors.Add((Caps("0.160.0", T, error), null, "probe_unavailable", "C1031-unknown-token"));

        const string oldRunnerJson = """
            {"ptyBackend":"PortaPty","ptyBackendRequested":"PortaPty","ptyBackendReason":"legacy","ptyBackendFellBack":false}
            """;
        var oldRunner = JsonSerializer.Deserialize<RunnerCapabilitiesDto>(oldRunnerJson, Json)!;
        oldRunner.CodexCliVersion.ShouldBeNull("C1031-old-runner absent members");
        vectors.Add((oldRunner, null, null, "C1031-old-runner absent members"));
        foreach (var (wire, stale, error) in new (string, bool?, string?)[]
        {
            ("""{"codexCliVersion":"0.160.0","codexCliVersionCheckedAtUtc":"2026-10-03T12:00:00Z"}""", false, null),
            ("""{"codexCliVersion":null,"codexCliVersionCheckedAtUtc":"2026-10-03T12:00:00Z","codexCliVersionError":"stderr_output"}""", null, "stderr_output"),
        })
        {
            var sample = JsonSerializer.Deserialize<RunnerCodexCliVersionDto>(wire, Json)!;
            vectors.Add((Caps(sample.CodexCliVersion, sample.CodexCliVersionCheckedAtUtc, sample.CodexCliVersionError)
                with { CodexCliLauncherFingerprint = null }, stale, error, "C1031-old-runner retained meaning"));
        }

        foreach (var (caps, stale, error, label) in vectors)
        {
            var clock = new FakeTimeProvider(T);
            await using var host = await PhoneHomeTestHost.StartAsync(clock);
            host.Local.Capabilities = caps;
            await using var peer = await host.ConnectPeerAsync(capabilities: caps);
            host.Directory.MarkRecovered(await host.WaitLiveAsync());
            async Task CheckProjection()
            {
                using var response = await host.Http.GetAsync("/api/session-runners");
                response.EnsureSuccessStatusCode();
                var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                using var status = await host.Http.GetAsync($"/api/session-runners/{host.AllowedRunnerId}/status");
                status.EnsureSuccessStatusCode();
                var remote = JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement;
                foreach (var shape in rows.EnumerateArray().Append(remote))
                {
                    shape.TryGetProperty("codexCliLauncherFingerprint", out _).ShouldBeFalse("C1031-public-private");
                    foreach (var secret in new[] { new string('a', 64), @"C:\Users\C1031\private", "/home/C1031/private", "C1031-token-canary" })
                        shape.GetRawText().ShouldNotContain(secret, customMessage: "C1031-public-private");
                    Text(shape, "codexCliVersion").ShouldBe(caps.CodexCliVersion, label + " version");
                    Text(shape, "codexCliVersionError").ShouldBe(error, label + " token");
                    Flag(shape, "codexCliVersionStale").ShouldBe(stale, label);
                    var at = shape.GetProperty("codexCliVersionCheckedAtUtc");
                    (at.ValueKind == JsonValueKind.Null ? (DateTimeOffset?)null : at.GetDateTimeOffset())
                        .ShouldBe(caps.CodexCliVersionCheckedAtUtc, label + " original time");
                }
            }
            await CheckProjection();
            await Heartbeat(host, peer, 2, new(caps.CodexCliVersion, caps.CodexCliVersionCheckedAtUtc,
                caps.CodexCliVersionError, caps.CodexCliLauncherFingerprint));
            await CheckProjection();
            foreach (var log in host.Logs.Entries)
            foreach (var secret in new[] { @"C:\Users\C1031\private", "/home/C1031/private", "C1031-token-canary" })
                (log.Message + log.Exception + string.Join(";", log.Properties.Select(p => p.Key + "=" + p.Value)))
                    .ShouldNotContain(secret, customMessage: "C1031-public-private logs");
        }
        foreach (var advisory in new[] { "stderr_output", "output_truncated" })
        {
            var clock = new FakeTimeProvider(T);
            await using var host = await PhoneHomeTestHost.StartAsync(clock);
            await using var peer = await host.ConnectPeerAsync(capabilities: Caps("0.160.0", T, advisory));
            host.Directory.MarkRecovered(await host.WaitLiveAsync());
            clock.Advance(TimeSpan.FromMinutes(15));
            await Heartbeat(host, peer, 2, null);
            Flag(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersionStale").ShouldBe(false, "C1031-age-boundary reads do not renew");
            clock.Advance(TimeSpan.FromTicks(1));
            await Heartbeat(host, peer, 2, null);
            var aged = Shape(host.Directory.Status(host.AllowedRunnerId));
            Flag(aged, "codexCliVersionStale").ShouldBe(true, "C1031-age-boundary reads do not renew");
            aged.GetProperty("codexCliVersionCheckedAtUtc").GetDateTimeOffset().ShouldBe(T);
        }
    }

    // C959 wire/reader semantics frozen from 6a88d8ce, intentionally unaware of advisories.
    private sealed record LegacyC959Sample(string? CodexCliVersion = null,
        DateTimeOffset? CodexCliVersionCheckedAtUtc = null, string? CodexCliVersionError = null,
        string? CodexCliLauncherFingerprint = null);
    private sealed record PreC959Reader(string? PtyBackend = null);
    private static bool? LegacyStale(LegacyC959Sample sample, DateTimeOffset now)
    {
        if (sample.CodexCliVersionCheckedAtUtc is not { } completed
            || CodexCliVersion.Parse(sample.CodexCliVersion) is null || sample.CodexCliVersionError is not null
            || completed - now > TimeSpan.FromMinutes(1)
            || sample.CodexCliLauncherFingerprint is { } fingerprint && (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)))
            return null;
        return now - completed > TimeSpan.FromMinutes(15);
    }

    [Test]
    public async Task C959_Heartbeat_updates_only_probe_evidence()
    {
        using (var io = new ProbeIo())
        {
            await io.Probe.RefreshDefaultAsync(CancellationToken.None);
            await using var receiver = await PhoneHomeTestHost.StartAsync(io.Clock);
            await using var sender = await receiver.ConnectPeerAsync(capabilities: Caps("0.159.1", T.AddMinutes(-1)));
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
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersion")
                .ShouldBe("0.160.0", "C959-pc-074 C959-pc-079 actual recipient snapshot");
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
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersion").ShouldBeNull("C959-pc-076");
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersionError").ShouldBe("nonzero_exit", "C959-v09-production-failure-snapshot");
            var completed = io.Probe.Snapshot.CodexCliVersionCheckedAtUtc;
            io.Clock.Advance(TimeSpan.FromMinutes(1));
            await producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None);
            await new PhoneHomeRunnerClient(connected).GetHealthAsync(CancellationToken.None);
            Text(Shape(receiver.Directory.Status(receiver.AllowedRunnerId)), "codexCliVersionCheckedAtUtc")
                .ShouldBe(completed!.Value.ToString("yyyy-MM-ddTHH:mm:sszzz"), "C959-v09-repeat-original-time");
            sender.Socket.Abort();
            var disconnectWait = Stopwatch.StartNew();
            while (connected.SocketOpen && disconnectWait.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            connected.SocketOpen.ShouldBeFalse("C959-v09-original-connection-ended");
            var registrationProducer = new PhoneHomeRuntimeAdapter(io.Runtime,
                new RunnerBuildDto("test", "d40c1670", T.UtcDateTime, T.UtcDateTime));
            await using var reconnected = await receiver.ConnectPeerAsync(capabilities: registrationProducer.Capabilities());
            receiver.Directory.MarkRecovered(await receiver.WaitLiveAsync());
            var recovered = Shape(receiver.Directory.Status(receiver.AllowedRunnerId));
            Text(recovered, "codexCliVersion").ShouldBeNull("C959-pc-080");
            Text(recovered, "codexCliVersionError").ShouldBe("nonzero_exit", "C959-pc-080 completed failure reacquired");
            Text(recovered, "codexCliVersionCheckedAtUtc").ShouldBe(completed.Value.ToString("yyyy-MM-ddTHH:mm:sszzz"), "C959-pc-080 no time renewal");
        }
        foreach (var (mode, advisory) in new[] { ("notice", "stderr_output"), ("stderr-4097", "output_truncated") })
        {
            using var io = new ProbeIo { Mode = mode };
            await io.Probe.RefreshDefaultAsync(CancellationToken.None);
            await using var receiver = await PhoneHomeTestHost.StartAsync(io.Clock);
            await using var sender = await receiver.ConnectPeerAsync(capabilities: Caps("0.159.1", T.AddMinutes(-1)));
            var connected = await receiver.WaitLiveAsync();
            receiver.Directory.MarkRecovered(connected);
            using var controlled = new ControlledSendSocket(sender.Socket);
            var settings = new PhoneHomeSettings { RunnerId = receiver.AllowedRunnerId, Capacity = 2,
                CapacityStatePath = Path.Combine(io.Root, "capacity"), AllowedCwd = io.Root };
            var adapter = new PhoneHomeRuntimeAdapter(io.Runtime, new RunnerBuildDto("test", "d40c1670", T.UtcDateTime, T.UtcDateTime));
            using var producer = new PhoneHomeConnectionService(Options.Create(settings), new PhoneHomeAdoptionGate(),
                new PhoneHomeCommandDispatcher(adapter, settings), io.Runtime, new UnusedHttpFactory(), io.Clock,
                NullLogger<PhoneHomeConnectionService>.Instance);
            var writer = new PhoneHomeConnectionWriter(controlled, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
            async Task Receipt(DateTimeOffset at, string? token, string label)
            {
                await new PhoneHomeRunnerClient(receiver.Directory.SnapshotLive(receiver.AllowedRunnerId)!)
                    .GetHealthAsync(CancellationToken.None);
                using var list = await receiver.Http.GetAsync("/api/session-runners");
                list.EnsureSuccessStatusCode();
                var rows = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
                using var status = await receiver.Http.GetAsync($"/api/session-runners/{receiver.AllowedRunnerId}/status");
                status.EnsureSuccessStatusCode();
                foreach (var shape in new[] { rows.EnumerateArray().Single(r => Text(r, "runnerId") == receiver.AllowedRunnerId),
                             JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement })
                {
                    Text(shape, "codexCliVersion").ShouldBe("0.160.0", label);
                    Text(shape, "codexCliVersionError").ShouldBe(token, label);
                    shape.GetProperty("codexCliVersionCheckedAtUtc").GetDateTimeOffset().ShouldBe(at, label);
                }
            }
            var hold = writer.SendAsync(new(PhoneHomeFrameKind.Heartbeat, sender.Epoch, Guid.NewGuid(),
                Payload: Shape(new { capacity = 2 })), CancellationToken.None);
            await controlled.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var heartbeat = producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None);
            try
            {
                heartbeat.IsCompleted.ShouldBeFalse("C1031-writer-original-time held");
                io.Clock.Advance(TimeSpan.FromMinutes(5));
            }
            finally
            {
                controlled.Release.TrySetResult();
                await Task.WhenAll(hold, heartbeat).WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Receipt(T, advisory, "C1031-heartbeat-receipt C1031-writer-original-time");
            await io.Probe.RefreshDefaultAsync(CancellationToken.None);
            controlled.BeforeFailure = true;
            await Should.ThrowAsync<IOException>(() => producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None));
            await Receipt(T, advisory, "C1031-before-send-recovery unchanged");
            await producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None);
            await Receipt(T.AddMinutes(5), advisory, "C1031-before-send-recovery");
            io.Clock.Advance(TimeSpan.FromMinutes(1));
            await io.Probe.RefreshDefaultAsync(CancellationToken.None);
            controlled.AfterFailure = true;
            await Should.ThrowAsync<IOException>(() => producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None));
            await Receipt(T.AddMinutes(6), advisory, "C1031-after-send-recovery already received");
            io.Clock.Advance(TimeSpan.FromMinutes(1));
            await producer.SendHeartbeatAsync(writer, sender.Epoch, CancellationToken.None);
            await Receipt(T.AddMinutes(6), advisory, "C1031-after-send-recovery no renewal");
            sender.Socket.Abort();
            var disconnect = Stopwatch.StartNew();
            while (connected.SocketOpen && disconnect.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            connected.SocketOpen.ShouldBeFalse("C1031-reconnect-receipt old connection ended");
            await using var reconnected = await receiver.ConnectPeerAsync(capabilities: adapter.Capabilities());
            receiver.Directory.MarkRecovered(await receiver.WaitLiveAsync());
            await Receipt(T.AddMinutes(6), advisory, "C1031-reconnect-receipt");
            io.Mode = "success";
            await io.Probe.RefreshDefaultAsync(CancellationToken.None);
            var reconnectWriter = new PhoneHomeConnectionWriter(reconnected.Socket, PhoneHomeProtocol.DefaultMaxMessageUtf8Bytes);
            await producer.SendHeartbeatAsync(reconnectWriter, reconnected.Epoch, CancellationToken.None);
            await Receipt(T.AddMinutes(7), null, "C1031-heartbeat-receipt recovered quiet");
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
            .ShouldBe(Text(status, "codexCliVersionCheckedAtUtc"), "C959-pc-077");
        await Heartbeat(host, peer, 2, Sample("0.9.0", T));
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBe("0.160.0", "C959-v09-old-success-ignored");
        await Heartbeat(host, peer, 2, Sample(null, T.AddMinutes(2), "nonzero_exit"));
        status = Shape(host.Directory.Status(host.AllowedRunnerId));
        Text(status, "codexCliVersion").ShouldBeNull("C959-v09-failure-clears");
        Text(status, "codexCliVersionError").ShouldBe("nonzero_exit", "C959-v09-failure");
        await Heartbeat(host, peer, 2, Sample("0.159.1", T));
        var retainedFailure = Shape(host.Directory.Status(host.AllowedRunnerId));
        Text(retainedFailure, "codexCliVersion").ShouldBeNull("C959-pc-078");
        Text(retainedFailure, "codexCliVersionError").ShouldBe("nonzero_exit", "C959-pc-078 retain failure");
    }

    [Test]
    public async Task C959_Freshness_boundaries()
    {
        foreach (var (caps, stale, error, label) in new (RunnerCapabilitiesDto, bool?, string?, string)[]
        {
            (Caps("0.160.0", T.AddMinutes(-15)), false, null, "C959-pc-200"),
            (Caps("0.160.0", T.AddMinutes(-15).AddTicks(-1)), true, null, "C959-pc-081"),
            (Caps("0.160.0", null), null, null, "C959-pc-082"),
            (Caps("0.160.0", T.AddMinutes(1)), false, null, "C959-pc-084"),
            (Caps("0.160.0", T.AddMinutes(1).AddTicks(1)), null, "clock_skew", "C959-pc-250"),
            (Caps("0.160.0", T) with { CodexCliLauncherFingerprint = "bad" }, null, "launcher_mismatch", "C959-pc-251"),
            (Caps("0.160.0", T) with { CodexCliLauncherFingerprint = null }, false, null, "C959-legacy-fingerprint"),
            (Caps("0.160.0", T, "timeout"), null, "timeout", "C959-pc-091"),
            (Caps("banana", T), null, null, "C959-invalid-version"),
        })
        {
            await using var projection = await PhoneHomeTestHost.StartAsync(new FakeTimeProvider(T));
            projection.Local.Capabilities = caps;
            await using var remote = await projection.ConnectPeerAsync(capabilities: caps);
            projection.Directory.MarkRecovered(await projection.WaitLiveAsync());
            using var response = await projection.Http.GetAsync("/api/session-runners");
            response.EnsureSuccessStatusCode();
            var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var projectedStatus = Shape(projection.Directory.Status(projection.AllowedRunnerId));
            foreach (var shape in rows.EnumerateArray().Append(projectedStatus))
            {
                Text(shape, "codexCliVersionError").ShouldBe(error, label);
                Flag(shape, "codexCliVersionStale").ShouldBe(stale, label + " freshness");
                Text(shape, "codexCliVersion").ShouldBe(caps.CodexCliVersion, label + " original version");
            }
        }
        foreach (var (settings, label) in new[]
        {
            (new CodexCliVersionSettings { MaxAgeMinutes = 61 }, "C959-pc-089"),
            (new CodexCliVersionSettings { MaxAgeMinutes = 1, RefreshIntervalMinutes = 1 }, "C959-pc-090"),
            (new CodexCliVersionSettings { RefreshIntervalMinutes = 0 }, "C959-pc-254"),
            (new CodexCliVersionSettings { RefreshIntervalMinutes = double.NaN }, "C959-pc-255"),
        }) Should.Throw<InvalidOperationException>(() => settings.Validate(), label);
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
        await using (var correlatedHost = await PhoneHomeTestHost.StartAsync(new FakeTimeProvider(T)))
        {
            await using var correlatedPeer = await correlatedHost.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
            var connection = await correlatedHost.WaitLiveAsync();
            correlatedHost.Directory.MarkRecovered(connection);
            correlatedPeer.AutoReply = false;
            var current = true;
            var client = new PhoneHomeRunnerClient(connection, isCurrent: () => current);
            var pending = client.GetCodexCliVersionAsync(new("/isolated/codex", "/isolated"), CancellationToken.None);
            var request = await correlatedPeer.WaitForAsync(PhoneHomeOperation.CodexCliVersion);
            current = false;
            connection.SocketOpen.ShouldBeTrue("C959-pc-097 isolate ownership from socket");
            await correlatedPeer.EmitAsync(new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId,
                request.Operation, Shape(Sample("0.160.0", T))));
            (await pending.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeNull("C959-pc-097");
        }

        foreach (var keepLive in new[] { true, false })
        {
            var clock = new FakeTimeProvider(T);
            await using var guarded = await PhoneHomeTestHost.StartAsync(clock);
            var registered = guarded.Directory.Register(guarded.Registration() with { Capabilities = Caps("0.160.0", T) });
            using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
            var held = keepLive ? guarded.Directory.AcceptConnect(guarded.AllowedRunnerId, registered.Ticket, socket) : null;
            guarded.Directory.ApplyState(guarded.AllowedRunnerId, new RunnerState(true, T, "C959", null, false, null, T, "C959"));
            guarded.Directory.ApplyState(guarded.AllowedRunnerId, new RunnerState(false, null, null, null, false, null, null, null));
            clock.Advance(TimeSpan.FromSeconds(keepLive ? 91 : 89));
            held?.NoteHeartbeat(clock.GetUtcNow());
            var label = keepLive ? "C959-pc-201" : "C959-pc-202";
            var failure = Should.Throw<ConflictException>(() => guarded.Directory.Register(guarded.Registration(storeId: Guid.NewGuid())), label);
            failure.Code.ShouldBe(PhoneHomeProblemTypes.StoreMismatch, label);
            Text(Shape(guarded.Directory.Status(guarded.AllowedRunnerId)), "codexCliVersion")
                .ShouldBe("0.160.0", label + " original observation preserved");
        }
        foreach (var sameStore in new[] { true, false })
        {
            await using var retired = await PhoneHomeTestHost.StartAsync(new FakeTimeProvider(T));
            retired.Directory.Register(retired.Registration() with { Capabilities = Caps("0.160.0", T) });
            retired.Directory.ApplyState(retired.AllowedRunnerId, new RunnerState(true, T, "C959", null, false, null, T, "C959"));
            var failure = Should.Throw<ConflictException>(() => retired.Directory.Register(
                retired.Registration(storeId: sameStore ? retired.StoreId : Guid.NewGuid())), "C959-pc-203");
            failure.Code.ShouldBe(PhoneHomeProblemTypes.RunnerRetired, "C959-pc-203");
            Text(Shape(retired.Directory.Status(retired.AllowedRunnerId)), "codexCliVersion")
                .ShouldBe("0.160.0", "C959-pc-203 refused observation does not replace current");
        }
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
                .ShouldBeNull(change == "boot" ? "C959-pc-092" : "C959-pc-093");
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
                    .ShouldBeNull("C959-pc-094");
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
        descriptor!.Capabilities!.CodexCliVersion.ShouldBeNull("C959-pc-095");
        await using var next = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        live.Epoch.ShouldBeGreaterThan(old.Epoch, "C959-v11-epoch");
        await Heartbeat(host, next, 1, Sample("0.160.0", T.AddMinutes(1)), old.Epoch);
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBeNull("C959-pc-096");
        await Heartbeat(host, next, 1, Sample("0.159.1", T));
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBe("0.159.1", "C959-v11-new-epoch");
        using var refused = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
        refused.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, host.Secret);
        refused.Content = JsonContent.Create(host.Registration(storeId: Guid.NewGuid()), options: Json);
        using var response = await host.Http.SendAsync(refused);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, "C959-v11-store-owner");
        Text(Shape(host.Directory.Status(host.AllowedRunnerId)), "codexCliVersion").ShouldBe("0.159.1", "C959-pc-098");
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
        foreach (var advisory in new[] { "stderr_output", "output_truncated" })
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            host.Local.Capabilities = Caps("0.160.0", clock.GetUtcNow(), advisory);
            await Heartbeat(host, peer, 1, Sample("0.160.0", clock.GetUtcNow(), advisory));
            using var catalogue = await host.Http.GetAsync("/api/session-runners");
            catalogue.EnsureSuccessStatusCode();
            var observed = JsonDocument.Parse(await catalogue.Content.ReadAsStringAsync()).RootElement;
            using var received = await host.Http.GetAsync($"/api/session-runners/{host.AllowedRunnerId}/status");
            received.EnsureSuccessStatusCode();
            foreach (var shape in observed.EnumerateArray().Append(JsonDocument.Parse(await received.Content.ReadAsStringAsync()).RootElement))
            {
                Text(shape, "codexCliVersion").ShouldBe("0.160.0", "C1031-heartbeat-receipt catalogue");
                Text(shape, "codexCliVersionError").ShouldBe(advisory, "C1031-heartbeat-receipt catalogue");
                Flag(shape, "codexCliVersionStale").ShouldBe(false, "C1031-advisory-fresh catalogue");
            }
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        await Heartbeat(host, peer, 1, Sample(null, clock.GetUtcNow(), "C959-diagnostic-sentinel"));
        using var sanitized = await host.Http.GetAsync("/api/session-runners");
        var sanitizedRows = JsonDocument.Parse(await sanitized.Content.ReadAsStringAsync()).RootElement;
        var sanitizedStatus = Shape(host.Directory.Status(host.AllowedRunnerId));
        foreach (var shape in new[] { sanitizedRows.EnumerateArray().Single(row => Text(row, "runnerId") == host.AllowedRunnerId), sanitizedStatus })
        {
            Text(shape, "codexCliVersionError").ShouldBe("probe_unavailable", "C959-pc-252");
            shape.GetRawText().ShouldNotContain("C959-diagnostic-sentinel", customMessage: "C959-pc-252 no raw diagnostic");
            Flag(shape, "codexCliVersionStale").ShouldBeNull("C959-pc-091 unverified");
        }
    }

    [Test]
    public async Task C959_Exact_probe_transport_is_bound()
    {
        ((int)PhoneHomeOperation.Capabilities).ShouldBe(1, "C959-pc-119");
        ((int)PhoneHomeOperation.CodexCliVersion).ShouldBe(33, "C959-pc-119 additive operation");
        var twoSettings = new PhoneHomeRunnerSettings { Enabled = true };
        foreach (var id in new[] { "runner-a", "runner-b" })
            twoSettings.Runners[id] = new() { Enabled = true, AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\work", RunnerWorkspace = "/work/" + id, SharedSecret = "c959-two",
                RunnerRepository = "/work/repos/antiphon", CallbackOrigin = "https://antiphon.test" };
        await using (var two = await PhoneHomeTestHost.StartAsync(new FakeTimeProvider(T), configured: twoSettings))
        {
            await using var a = await two.ConnectPeerAsync(runnerId: "runner-a", secret: "c959-two", capabilities: Caps("0.160.0", T));
            await using var b = await two.ConnectPeerAsync(runnerId: "runner-b", secret: "c959-two", capabilities: Caps("0.156.1", T));
            two.Directory.MarkRecovered(await two.WaitLiveAsync(runnerId: "runner-a"));
            two.Directory.MarkRecovered(await two.WaitLiveAsync(runnerId: "runner-b"));
            a.Reply = request => request.Operation == PhoneHomeOperation.CodexCliVersion
                ? new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation, Shape(Sample("0.160.0", T))) : null;
            b.Reply = request => request.Operation == PhoneHomeOperation.CodexCliVersion
                ? new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation, Shape(Sample("0.156.1", T))) : null;
            var counted = new DiagnosticDirectory(two.Directory);
            var selected = await new RunnerScopedSessionRunnerClient(counted, "runner-a")
                .GetCodexCliVersionAsync(new("/isolated/codex", "/isolated"), CancellationToken.None);
            counted.LocalProbeCalls.ShouldBe(0, "C959-pc-108 no local typed diagnostic");
            counted.RemoteProbeCalls.ShouldBe(1, "C1029-v13 selected typed diagnostic");
            a.RequestCount(PhoneHomeOperation.CodexCliVersion).ShouldBe(1, "C959-pc-111 selected recipient");
            selected!.CodexCliVersion.ShouldBe("0.160.0", "C959-pc-111");
            b.RequestCount(PhoneHomeOperation.CodexCliVersion).ShouldBe(0, "C959-pc-111 no reroute");
        }
        var lostClock = new FakeTimeProvider(T);
        await using (var lostHost = await PhoneHomeTestHost.StartAsync(lostClock))
        {
            await using var lostPeer = await lostHost.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
            var lostConnection = await lostHost.WaitLiveAsync();
            lostHost.Directory.MarkRecovered(lostConnection);
            lostPeer.Reply = frame => frame.Operation == PhoneHomeOperation.CodexCliVersion
                ? new(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation, Shape(Sample("0.156.1", T))) : null;
            var selected = new RunnerScopedSessionRunnerClient(lostHost.Directory, lostHost.AllowedRunnerId);
            var installationB = new RunnerCodexCliProbeRequest("/isolated/package-b/codex", "/isolated/package-b");
            (await selected.GetCodexCliVersionAsync(installationB, CancellationToken.None))!.CodexCliVersion
                .ShouldBe("0.156.1", "C959-diagnostic-B-differs-from-default-A");
            lostPeer.AutoReply = false;
            lostPeer.Reply = _ => null;
            var pending = selected.GetCodexCliVersionAsync(installationB, CancellationToken.None);
            var arrival = Stopwatch.StartNew();
            while (lostPeer.RequestCount(PhoneHomeOperation.CodexCliVersion) < 2 && arrival.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            lostPeer.RequestCount(PhoneHomeOperation.CodexCliVersion).ShouldBe(2, "C959-lost-response-request-reached-recipient");
            lostClock.Advance(TimeSpan.FromSeconds(8));
            (await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5))) == pending).ShouldBeTrue("C959-pc-123 bounded loss");
            (await pending).ShouldBeNull("C959-pc-123 no advertised-A fallback");
            lostPeer.AutoReply = true;
            lostPeer.Reply = frame => frame.Operation == PhoneHomeOperation.CodexCliVersion
                ? new(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation, Shape(Sample("0.156.1", lostClock.GetUtcNow()))) : null;
            (await selected.GetCodexCliVersionAsync(installationB, CancellationToken.None))!.CodexCliVersion
                .ShouldBe("0.156.1", "C959-new-explicit-request-reacquires-B");
        }
        using (var wire = new MemoryStream())
        using (var socket = WebSocket.CreateFromStream(wire, new WebSocketCreationOptions { IsServer = true }))
        using (var failureSocket = new ControlledSendSocket(socket) { BeforeSocketFailure = true })
        {
            failureSocket.Release.TrySetResult();
            await using var failedConnection = new PhoneHomeLiveConnection("runner-a", Guid.NewGuid(), Guid.NewGuid(),
                1, failureSocket, new(), new FakeTimeProvider(T), capabilities: Caps("0.160.0", T));
            (await new PhoneHomeRunnerClient(failedConnection).GetCodexCliVersionAsync(
                new("/different/codex", "/different"), CancellationToken.None)).ShouldBeNull("C959-pc-122");
            wire.Length.ShouldBe(0, "C959-pc-122 failed before frame write");
            failureSocket.Abort();
        }
        foreach (var variant in new[] { "request-id", "epoch", "operation", "503", "error-payload", "cancel" })
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
                    await Should.ThrowAsync<OperationCanceledException>(() => pending, "C959-pc-116");
                }
                else
                {
                    await correlationPeer.EmitAsync(variant is "503" or "error-payload"
                        ? new(PhoneHomeFrameKind.Error, sent.Epoch, sent.RequestId, sent.Operation,
                            Payload: variant == "error-payload" ? Shape(Sample("0.160.0", T)) : null,
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
                    RunnerCodexCliVersionDto? observed = null;
                    Exception? failure = null;
                    try { observed = await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception ex) { failure = ex; }
                    failure.ShouldBeNull("C959-pc-248 diagnostic errors do not throw " + variant);
                    observed.ShouldBeNull(variant == "operation" ? "C959-pc-247" : variant == "request-id" ? "C959-pc-109" : "C959-v13-refused-reply " + variant);
                }
                correlationPeer.RequestCount((PhoneHomeOperation)33).ShouldBe(1, variant == "503" ? "C959-pc-115" : "C959-v13-no-retry " + variant);
            }
            finally
            {
                caller.Cancel();
                try { await pending; } catch (OperationCanceledException) { }
            }
        }
        {
            await using var closingHost = await PhoneHomeTestHost.StartAsync(new FakeTimeProvider(T));
            await using var closingPeer = await closingHost.ConnectPeerAsync(capabilities: Caps("0.160.0", T));
            var live = await closingHost.WaitLiveAsync();
            closingHost.Directory.MarkRecovered(live);
            closingPeer.Reply = request => request.Operation == PhoneHomeOperation.CodexCliVersion
                ? new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation, Shape(Sample("0.160.0", T))) : null;
            var checks = 0;
            var closingClient = new PhoneHomeRunnerClient(live, isCurrent: () =>
            {
                if (++checks != 2) return true;
                live.DisposeAsync("C959 closed before projection").AsTask().GetAwaiter().GetResult();
                return true;
            });
            (await closingClient.GetCodexCliVersionAsync(new("/isolated/codex", "/isolated"), CancellationToken.None))
                .ShouldBeNull("C959-pc-249 closed socket");
            checks.ShouldBe(2, "C959-projection-cut-after-correlated-reply");
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
            .ShouldBeNull("C959-pc-113");
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
        (throughHttp?.CodexCliVersion).ShouldBe("0.160.0", "C959-pc-120");
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
        var liveWriter = actual.Directory.SnapshotLive(actual.AllowedRunnerId)!;
        var sendGate = (SemaphoreSlim)typeof(PhoneHomeLiveConnection).GetField("_send", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(liveWriter)!;
        await sendGate.WaitAsync();
        Task<RunnerCodexCliVersionDto?> queuedDiagnostic;
        try
        {
            queuedDiagnostic = bound.GetCodexCliVersionAsync(exact, CancellationToken.None);
            queuedDiagnostic.IsCompleted.ShouldBeFalse("C959-diagnostic-queues-behind-real-writer");
        }
        finally { sendGate.Release(); }
        (await queuedDiagnostic.WaitAsync(TimeSpan.FromSeconds(5)))!.CodexCliVersion
            .ShouldBe("0.160.0", "C959-diagnostic-reaches-real-recipient-after-writer-release");
        io.Starts.Count.ShouldBe(1, "C959-diagnostic-busy-writer-preserves-probe-cache");
        actual.Local.Calls.ShouldBeEmpty("C959-pc-108");
        (await actual.Directory.DescribeAsync(actual.AllowedRunnerId, CancellationToken.None))!
            .Capabilities!.CodexCliVersion.ShouldBeNull("C959-v13-exact-does-not-overwrite-default");
        foreach (var vector in CodexCliDescriptorCases.All(exact).Where(v => !v.Accepted))
        {
            foreach (var (transport, diagnostic) in new (string, ISessionRunnerClient)[]
                     { ("local-post", localClient), ("framed-dispatch", bound) })
            {
                var starts = io.Starts.Count;
                var unknown = await diagnostic.GetCodexCliVersionAsync(vector.Request, CancellationToken.None);
                var label = $"{vector.RunnerControl} {transport}/{vector.Field}/{vector.Name}";
                (unknown?.CodexCliVersionError).ShouldBe("launcher_unverified", label);
                unknown!.CodexCliVersion.ShouldBeNull(label + " no sample");
                io.Starts.Count.ShouldBe(starts, label + " no child");
            }
        }
        // These two fields can reach a real child at the boundary on either OS.
        // Pure FromSpec covers representability of the other three, not OS path support.
        foreach (var vector in CodexCliDescriptorCases.All(exact)
                     .Where(v => v.Accepted && v.Field is "Path" or "PathExt"))
        {
            var starts = io.Starts.Count;
            var atLimit = await localClient.GetCodexCliVersionAsync(vector.Request, CancellationToken.None);
            (atLimit?.CodexCliVersion).ShouldBe("0.160.0", "C1029-v13 local exact boundary " + vector.Field);
            io.Starts.Count.ShouldBe(starts + 1, "C1029-v13 boundary reaches child " + vector.Field);
            (await bound.GetCodexCliVersionAsync(vector.Request, CancellationToken.None))
                .ShouldBe(atLimit, "C1029-v13 framed exact boundary " + vector.Field);
            io.Starts.Count.ShouldBe(starts + 1, "C1029-v13 exact descriptor cache " + vector.Field);
        }
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
        (await Task.WhenAny(silent, Task.Delay(TimeSpan.FromSeconds(5))) == silent).ShouldBeTrue("C959-pc-114");
        (await silent).ShouldBeNull("C959-pc-114 C959-pc-123");
    }

    private sealed class DiagnosticDirectory(ISessionRunnerDirectory inner) : ISessionRunnerDirectory
    {
        public int LocalProbeCalls { get; private set; }
        public int RemoteProbeCalls { get; private set; }
        public ISessionRunnerClient Local => new DiagnosticClient(inner.Local, () => LocalProbeCalls++);
        public ISessionRunnerClient Resolve(string? runnerId) =>
            new DiagnosticClient(inner.Resolve(runnerId), () => RemoteProbeCalls++);
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => inner.GetOwnerAsync(sessionId, ct);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => inner.GetBindingAsync(sessionId, ct);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => inner.GetInventoryAsync(runnerId, ct);
        public IReadOnlyList<string> KnownRunnerIds => inner.KnownRunnerIds;
        public Guid? GetLiveStoreId(string? runnerId) => inner.GetLiveStoreId(runnerId);
    }

    private sealed class DiagnosticClient(ISessionRunnerClient inner, Action onProbe) : ISessionRunnerClient
    {
        public Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request, CancellationToken ct)
        {
            onProbe(); // Count the typed operation even if forwarding refuses before a frame is written.
            return inner.GetCodexCliVersionAsync(request, ct);
        }
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => inner.StartAsync(id, spec, ct);
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => inner.GetBufferAsync(id, ct);
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => inner.GetSnapshotAsync(id, ct);
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => inner.GetTranscriptAsync(id, ct);
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => inner.SendInputAsync(id, input, ct);
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => inner.ClearLiveBufferAsync(id, ct);
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => inner.ResizeAsync(id, cols, rows, ct);
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => inner.KillAsync(id, ct);
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => inner.StreamEventsAsync(ct);
    }

    private sealed class ControlledSendSocket(WebSocket inner) : WebSocket
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sends;
        public int PeakSends { get; private set; }
        public bool BeforeFailure { get; set; }
        public bool BeforeSocketFailure { get; set; }
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
                if (BeforeSocketFailure) { BeforeSocketFailure = false; throw new WebSocketException("C959 before frame write"); }
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
