using System.Diagnostics;
using System.IO.Pipes;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1653 S2 (portable): before the hello the runner asks the kernel who is behind the pipe.
/// Under Enforce a peer that is not the expected host (pid, Linux uid, start time) is refused
/// without a write and held pending with no kill; Observe only logs; Off is today's connect. The
/// stranger world's fake host is this test process, so the real reader names a foreign peer on
/// both platforms. On Windows the stand-in is ping.exe and every uid is null.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public class Card1653PeerCheckTests
{
    private const string MismatchToken = "pty_host_peer_mismatch";
    private const string UnprovenReason = "pty_host_peer_unproven";
    private const string LegacyToken = "pty_host_legacy_socket_path";

    [Test]
    public async Task Peer_reader_returns_the_listener_process()
    {
        var root = TestSessionLogRoot.Create("c1653-peer");
        var name = OperatingSystem.IsWindows() ? $"antiphon-c1653-peer-{Guid.NewGuid():N}" : Path.Combine(root, "v10");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accepted = server.WaitForConnectionAsync();
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5_000);
        await accepted.WaitAsync(TimeSpan.FromSeconds(5));

        var peer = PtyHostPeerCredentials.Read(client.SafePipeHandle);

        peer.ShouldNotBeNull("the kernel names the process behind a connected pipe");
        peer.Pid.ShouldBe(Environment.ProcessId);
        if (OperatingSystem.IsWindows()) peer.Uid.ShouldBeNull();
        else peer.Uid.ShouldBe(PtyHostStrangerWorld.Euid());
    }

    [Test]
    public async Task Enforce_refuses_a_replayed_identity_from_a_foreign_peer()
    {
        await using var world = await RefusalWorldAsync();
        var seen = ObservePolicies();

        await AssertRefusedAsync(world, await world.AdoptAsync());

        world.Logger.Text.ShouldNotContain("pty_host_identity_mismatch", customMessage: "the hello was never exchanged");
        seen.Count.ShouldBe(1);
        seen[0].ShouldNotBeNull("Enforce connects with a policy");
        seen[0]!.CurrentUserOnly.ShouldBe(OperatingSystem.IsWindows());
    }

    [Test]
    public async Task Off_adopts_the_replayed_identity()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-peer");
        world.Answer(world.SessionId, world.HostInstance);
        var seen = ObservePolicies();

        (await world.AdoptAsync()).ShouldBe(1, "Off is today's connect, which adopts the replayed identity");

        world.Runtime.Get(world.SessionId).Status.ShouldBe("Running");
        world.Frames.ShouldNotBeEmpty();
        world.Frames[0].ShouldBe(nameof(HelloMessage));
        seen.ShouldBe([null], "Off passes no policy");
        world.Logger.Text.ShouldNotContain(MismatchToken);
    }

    [Test]
    public async Task Unreadable_peer_credentials_are_unproven()
    {
        await using var world = await RefusalWorldAsync();
        PtyHostPeerPolicy.PeerReaderOverride.Value = _ => null;

        await AssertRefusedAsync(world, await world.AdoptAsync());
    }

    [Test]
    public async Task Peer_start_must_match_the_manifest_host()
    {
        await using var world = await RefusalWorldAsync();
        var flip = new FlipProbe();
        PtyHostPeerPolicy.PeerReaderOverride.Value = flip.Wrap(world.ProvenPeer());

        await AssertRefusedAsync(world, await world.AdoptAsync(flip));

        flip.Calls.ShouldBeGreaterThanOrEqualTo(2, "the peer check re-reads the host's start after the sweep's pre-check");
    }

    [Test]
    public async Task Peer_refusal_row_is_released_once_its_host_is_dead()
    {
        await using var world = await RefusalWorldAsync();
        var id = world.SessionId;
        await AssertRefusedAsync(world, await world.AdoptAsync());
        var probe = new SystemProcessLivenessProbe();

        world.Runtime.SweepVanishedSessions(probe).ShouldBeEmpty("the host is alive, so the held row is not released");
        world.StandIn.Kill(entireProcessTree: true);
        world.StandIn.WaitForExit(5_000).ShouldBeTrue();
        world.Runtime.SweepVanishedSessions(probe).ShouldBe([id], "a dead host releases its held peer row");

        var dto = world.Runtime.Get(id);
        dto.Status.ShouldBe("Exited");
        dto.ExitReason.ShouldBe("ProcessVanished");
        dto.Pending.ShouldBeNull();
        world.ExitedEvents(id).ShouldBe(1);
        File.Exists(world.ManifestPath).ShouldBeFalse("the release deletes the retained manifest");
        world.Runtime.SweepVanishedSessions(probe).ShouldBeEmpty();
    }

    [Test]
    public async Task Observe_adopts_and_logs_the_mismatch()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-peer",
            peerCheck: PtyHostPeerCheckMode.Observe);
        world.Answer(world.SessionId, world.HostInstance);
        var seen = ObservePolicies();

        (await world.AdoptAsync()).ShouldBe(1, "Observe never refuses");

        world.Runtime.Get(world.SessionId).Status.ShouldBe("Running");
        world.LogLines(MismatchToken).ShouldBe([$"{MismatchToken} (observe): session {world.SessionId}"],
            "one fixed observe line naming the session only, and no refusal line");
        seen.Count.ShouldBe(1);
        seen[0].ShouldNotBeNull();
        seen[0]!.CurrentUserOnly.ShouldBeFalse("Observe never sets CurrentUserOnly");
    }

    [Test]
    public async Task Owned_enforce_real_host_is_launched_and_adopted()
    {
        var root = TestSessionLogRoot.Create("c1653-peer");
        var settings = new SessionRunnerSettings
        {
            SessionLogPath = root, PtyHostLingerHours = 0.02, PtyHostConnectTimeoutSec = 5,
            PtyHostSocketDirMode = PtyHostSocketDirMode.Owned, PtyHostPeerCheck = PtyHostPeerCheckMode.Enforce,
        };
        var sessionId = Guid.NewGuid();
        var runtimeA = new SessionRunnerRuntime(Options.Create(settings), new PtyHostStrangerWorld.RecordingLogger());
        var logger = new PtyHostStrangerWorld.RecordingLogger();
        SessionRunnerRuntime? runtimeB = null;
        RunnerSessionDto? dto = null;
        try
        {
            using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "c1653-owned-real-host-launch"))
                dto = await runtimeA.StartAsync(new RunnerLaunchRequest(sessionId,
                    OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
                    OperatingSystem.IsWindows() ? [] : ["-c", "read line"],
                    new Dictionary<string, string>(), root, 80, 24,
                    AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow)), CancellationToken.None);
            dto.Status.ShouldBe("Running");
            await runtimeA.DisposeAsync();

            var manifest = PtyHostManifest.TryLoad(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId))!;
            if (OperatingSystem.IsWindows())
                manifest.PipeName.ShouldBe(PtyHostProtocol.PipeNameFor(sessionId), "Windows keeps the well-known name");
            else
            {
                manifest.PipeName.ShouldBe(settings.ResolvedPtyHostSocketDir + "/" + sessionId.ToString("N"));
                File.GetUnixFileMode(settings.ResolvedPtyHostSocketDir)
                    .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                OwnerUid(manifest.PipeName).ShouldBe(PtyHostStrangerWorld.Euid());
                File.Exists(PtyHostProtocol.PipeNameFor(sessionId)).ShouldBeFalse("an owned launch creates no /tmp socket");
            }

            runtimeB = new SessionRunnerRuntime(Options.Create(settings), logger);
            using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "c1653-owned-real-host-adoption"))
                (await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None))
                    .ShouldBe(1, "the real host proves itself across processes");
            runtimeB.Get(sessionId).Status.ShouldBe("Running");
            logger.Text.ShouldNotContain(LegacyToken);
            logger.Text.ShouldNotContain(MismatchToken);
        }
        finally
        {
            if (runtimeB is not null)
            {
                await TestSessionTeardown.KillAndAwaitHostExitAsync(runtimeB, sessionId, dto?.HostPid);
                await runtimeB.DisposeAsync();
            }
            else
            {
                await runtimeA.DisposeAsync();
                KillBestEffort(dto?.HostPid);
            }
            KillBestEffort(dto?.Pid);
        }
    }

    [Test]
    public async Task Proven_peer_failure_keeps_the_lawful_exit_path()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-peer",
            statusReply: PtyHostStrangerWorld.StatusReplyMode.Error, peerCheck: PtyHostPeerCheckMode.Enforce);
        world.Answer(world.SessionId, world.HostInstance);
        PtyHostPeerPolicy.PeerReaderOverride.Value = world.ProvenPeer();

        (await world.AdoptAsync()).ShouldBe(0);

        var dto = world.Runtime.Get(world.SessionId);
        dto.Status.ShouldBe("Exited");
        dto.Pending.ShouldBeNull();
        File.Exists(world.ManifestPath).ShouldBeFalse();
        world.StandIn.WaitForExit(5_000).ShouldBeTrue("a proven host that fails adoption keeps today's lawful kill");
        world.Logger.Text.ShouldNotContain(MismatchToken);
    }

    [Test]
    [Arguments(PtyHostPeerCheckMode.Off, false)]
    [Arguments(PtyHostPeerCheckMode.Off, true)]
    [Arguments(PtyHostPeerCheckMode.Observe, false)]
    [Arguments(PtyHostPeerCheckMode.Observe, true)]
    [Arguments(PtyHostPeerCheckMode.Enforce, false)]
    [Arguments(PtyHostPeerCheckMode.Enforce, true)]
    public void Connect_policy_by_mode_and_platform(PtyHostPeerCheckMode mode, bool isWindows)
    {
        var policy = PtyHostPeerPolicy.Create(mode, isWindows, isLinux: !isWindows, expectedPid: 4242,
            expectedStart: null, new SystemProcessLivenessProbe(), PtyHostSocketDirectory.DefaultProbe,
            _ => null, _ => { });

        PtyHostConnectPolicy.PipeOptionsFor(null).ShouldBe(PipeOptions.Asynchronous,
            "with no policy the client stream options are today's, byte for byte");
        if (mode == PtyHostPeerCheckMode.Off)
        {
            policy.ShouldBeNull("Off passes no policy");
            return;
        }

        policy.ShouldNotBeNull();
        policy.Observe.ShouldBe(mode == PtyHostPeerCheckMode.Observe);
        policy.CurrentUserOnly.ShouldBe(mode == PtyHostPeerCheckMode.Enforce && isWindows);
        PtyHostConnectPolicy.PipeOptionsFor(policy).ShouldBe(mode == PtyHostPeerCheckMode.Enforce && isWindows
            ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly : PipeOptions.Asynchronous);
        (policy.CheckSocketFile is not null).ShouldBe(!isWindows, "only Linux checks a socket file");
    }

    [Test]
    [Arguments(PtyHostSocketDirMode.Legacy, false, false)]
    [Arguments(PtyHostSocketDirMode.Legacy, false, true)]
    [Arguments(PtyHostSocketDirMode.Legacy, true, false)]
    [Arguments(PtyHostSocketDirMode.Legacy, true, true)]
    [Arguments(PtyHostSocketDirMode.Owned, false, false)]
    [Arguments(PtyHostSocketDirMode.Owned, false, true)]
    [Arguments(PtyHostSocketDirMode.Owned, true, false)]
    [Arguments(PtyHostSocketDirMode.Owned, true, true)]
    public void Rendezvous_name_by_mode_platform_and_custody(PtyHostSocketDirMode mode, bool isWindows, bool custody)
    {
        var root = TestSessionLogRoot.Create("c1653-peer");
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostSocketDirMode = mode };
        var id = Guid.NewGuid();
        var request = new RunnerLaunchRequest(id, "/bin/sh", [], new Dictionary<string, string>(), root, 80, 24,
            VerificationBinding: custody ? Binding(id) : null);

        var name = PtyHostRendezvous.PipeNameFor(settings, request, isWindows);

        if (mode == PtyHostSocketDirMode.Owned && !isWindows && !custody)
        {
            name.ShouldBe(settings.ResolvedPtyHostSocketDir + "/" + id.ToString("N"));
            return;
        }
        var legacy = isWindows ? $"antiphon-pty-{id:N}" : $"/tmp/antiphon-pty-{id:N}";
        name.ShouldBe(legacy);
        if (isWindows == OperatingSystem.IsWindows())
            name.ShouldBe(PtyHostProtocol.PipeNameFor(id), "this platform's well-known name is unchanged");
    }

    [Test]
    public void Settings_default_to_legacy_and_off()
    {
        var bound = new ServiceCollection()
            .Configure<SessionRunnerSettings>(new ConfigurationBuilder().Build().GetSection("SessionRunner"))
            .BuildServiceProvider()
            .GetRequiredService<IOptions<SessionRunnerSettings>>().Value;

        foreach (var settings in new[] { new SessionRunnerSettings(), bound })
        {
            settings.PtyHostSocketDirMode.ShouldBe(PtyHostSocketDirMode.Legacy);
            settings.PtyHostPeerCheck.ShouldBe(PtyHostPeerCheckMode.Off);
            settings.PtyHostSocketDir.ShouldBeNull();
            settings.ResolvedPtyHostSocketDir.ShouldBe(Path.GetFullPath(Path.Combine(settings.ResolvedPtyHostDir, "sock")));
        }
    }

    /// <summary>
    /// D-1 (review db8d32c0): an empty environment variable binds <c>""</c>, not null. A null, empty
    /// or whitespace-only setting is unset and resolves to the default without throwing.
    /// </summary>
    [Test]
    [Arguments("empty")]
    [Arguments("spaces")]
    [Arguments("tab")]
    public void Blank_socket_dir_setting_means_the_default(string shape)
    {
        var blank = shape switch { "empty" => "", "spaces" => "   ", "tab" => "\t", _ => throw new ArgumentOutOfRangeException(nameof(shape)) };
        var bound = new ServiceCollection()
            .Configure<SessionRunnerSettings>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["SessionRunner:PtyHostSocketDir"] = blank })
                .Build().GetSection("SessionRunner"))
            .BuildServiceProvider()
            .GetRequiredService<IOptions<SessionRunnerSettings>>().Value;
        bound.PtyHostSocketDir.ShouldBe(blank, "precondition: configuration binds the blank value, not null");

        foreach (var settings in new[] { new SessionRunnerSettings { PtyHostSocketDir = blank }, bound })
            Should.NotThrow(() => settings.ResolvedPtyHostSocketDir)
                .ShouldBe(Path.GetFullPath(Path.Combine(settings.ResolvedPtyHostDir, "sock")));
    }

    /// <summary>
    /// D-1/A-4: an exception inside the proof (the peer reader, the start-time liveness re-check) is
    /// not a proof. Nothing is written and the row is held exactly as a refusal; the host is never
    /// killed through the sweep's generic arm.
    /// </summary>
    [Test]
    [Arguments("peer-reader")]
    [Arguments("liveness")]
    public async Task Proof_step_exception_holds_the_row_without_a_kill(string step)
    {
        await using var world = await RefusalWorldAsync();
        IProcessLivenessProbe? probe = null;
        if (step == "peer-reader")
            PtyHostPeerPolicy.PeerReaderOverride.Value = _ => throw new InvalidOperationException("c1653 injected peer reader fault");
        else
        {
            var throwing = new ThrowAfterReadProbe();
            PtyHostPeerPolicy.PeerReaderOverride.Value = throwing.Wrap(world.ProvenPeer());
            probe = throwing;
        }

        await AssertRefusedAsync(world, await world.AdoptAsync(probe));
        world.Logger.Text.ShouldNotContain("treating as dead");
    }

    /// <summary>The CARD-1636 stranger shape: the correct session and instance, under Enforce, from a foreign peer.</summary>
    private static async Task<PtyHostStrangerWorld> RefusalWorldAsync()
    {
        var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-peer",
            peerCheck: PtyHostPeerCheckMode.Enforce);
        world.Answer(world.SessionId, world.HostInstance);
        return world;
    }

    private static List<PtyHostConnectPolicy?> ObservePolicies()
    {
        var seen = new List<PtyHostConnectPolicy?>();
        PtyHostPeerPolicy.ConnectPolicyObserver.Value = policy => { lock (seen) seen.Add(policy); };
        return seen;
    }

    private static async Task AssertRefusedAsync(PtyHostStrangerWorld world, int adopted)
    {
        adopted.ShouldBe(0, "a peer that is not the manifest's host is not adopted");
        await world.PeerClosedAsync(TimeSpan.FromSeconds(5));
        world.Frames.ShouldBeEmpty("nothing, not even the hello, is written to an unproven peer");
        world.StandIn.WaitForExit(500).ShouldBeFalse("a peer refusal never kills the manifest's host");
        File.Exists(world.ManifestPath).ShouldBeTrue("a peer refusal keeps the manifest");
        PtyHostStrangerWorld.HashFile(world.ManifestPath).ShouldBe(world.ManifestHash);
        var dto = world.Runtime.Get(world.SessionId);
        dto.Status.ShouldBe("Starting");
        dto.Pending.ShouldBe(UnprovenReason);
        dto.HostPid.ShouldBe(world.StandIn.Id);
        world.ExitedEvents(world.SessionId).ShouldBe(0, "a held row publishes no SessionExited");
        world.LogLines(MismatchToken).ShouldBe([$"{MismatchToken}: adoption refused for session {world.SessionId}"],
            "one fixed refusal line naming the session only");
    }

    private static uint OwnerUid(string path)
    {
        using var stat = Process.Start(new ProcessStartInfo("stat", ["-c", "%u", path])
            { UseShellExecute = false, RedirectStandardOutput = true })!;
        var output = stat.StandardOutput.ReadToEnd();
        stat.WaitForExit(5_000).ShouldBeTrue();
        return uint.Parse(output.Trim());
    }

    private static VerificationExecutionBinding Binding(Guid sessionId) => new(
        Guid.NewGuid(), new VerificationSourceIdentity(Guid.NewGuid(), Guid.NewGuid(), "deadbeef"),
        new VerificationSessionGeneration(sessionId, DateTime.UtcNow),
        new VerificationCreationCoordinates("r", "g", "w", "wg", "main", Guid.NewGuid()),
        VerificationCustodyBackends.LinuxCgroup, Guid.NewGuid());

    /// <summary>Alive (as the system says) until a wrapped peer reader has run; then every liveness read throws.</summary>
    private sealed class ThrowAfterReadProbe : IProcessLivenessProbe
    {
        private readonly SystemProcessLivenessProbe _real = new();
        private volatile bool _read;

        public Func<Microsoft.Win32.SafeHandles.SafePipeHandle, PtyHostPeerCredentials?> Wrap(
            Func<Microsoft.Win32.SafeHandles.SafePipeHandle, PtyHostPeerCredentials?> reader) =>
            handle => { _read = true; return reader(handle); };

        public bool IsAlive(int pid, DateTime startedAt) => _read
            ? throw new InvalidOperationException("c1653 injected liveness fault")
            : _real.IsAlive(pid, startedAt);

        public string? TryGetProcessName(int pid) => _real.TryGetProcessName(pid);

        public DateTime? TryGetStartTimeUtc(int pid) => _real.TryGetStartTimeUtc(pid);
    }

    private static void KillBestEffort(int? pid)
    {
        if (pid is not int value) return;
        try { using var process = Process.GetProcessById(value); process.Kill(entireProcessTree: true); }
        catch { /* Already gone. */ }
    }
}
