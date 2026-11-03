using System.Diagnostics;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1653 S2 (Linux): new sockets live in an owned 0700 directory under safe ancestors, a
/// socket file that is not this runner's socket is refused before connect, the peer's uid must be
/// the runner's, and a launch never falls back to <c>/tmp</c>. Adoption cases use the stranger
/// world under Enforce with the 5 s adoption connect budget.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
[NativeLinuxOnly("CARD-1653 S2 statx, SO_PEERCRED and the /bin/sh fake host require Linux")]
public class Card1653SocketDirTests
{
    private const string UnsafeCode = "pty_host_socket_dir_unsafe";
    private const string TooLongCode = "pty_host_socket_path_too_long";
    private const string MismatchToken = "pty_host_peer_mismatch";
    private const string UnprovenReason = "pty_host_peer_unproven";
    private const string LegacyToken = "pty_host_legacy_socket_path";
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [Test]
    public void Ensure_safe_creates_an_absent_directory_owner_only()
    {
        var dir = Path.Combine(NewRoot(), "s", "sock");

        PtyHostSocketDirectory.EnsureSafe(dir, PtyHostSocketDirectory.DefaultProbe);

        File.GetUnixFileMode(dir).ShouldBe(OwnerOnly, "an absent socket directory is created 0700");
    }

    [Test]
    public void Ensure_safe_tightens_an_owned_wider_directory()
    {
        var dir = Path.Combine(NewRoot(), "s", "sock");
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        PtyHostSocketDirectory.EnsureSafe(dir, PtyHostSocketDirectory.DefaultProbe);

        File.GetUnixFileMode(dir).ShouldBe(OwnerOnly, "an owned 0755 socket directory is tightened to 0700");
    }

    [Test]
    [Arguments("symlink")]
    [Arguments("foreign-owner")]
    [Arguments("unsafe-ancestor")]
    [Arguments("symlinked-unsafe-ancestor")]
    [Arguments("tighten-ineffective")]
    public void Unsafe_socket_directory_is_refused(string shape)
    {
        var root = NewRoot();
        var parent = Path.Combine(root, "s");
        var dir = Path.Combine(parent, "sock");
        var euid = PtyHostStrangerWorld.Euid();
        var overrides = new Dictionary<string, UnixFileFacts?>(StringComparer.Ordinal);
        switch (shape)
        {
            case "symlink":
                var target = Path.Combine(root, "target");
                Directory.CreateDirectory(target, OwnerOnly);
                Directory.CreateDirectory(parent);
                Directory.CreateSymbolicLink(dir, target);
                break;
            case "foreign-owner":
                overrides[dir] = new UnixFileFacts(UnixFileKind.Directory, euid + 1, DirectoryMode(0b111_000_000));
                break;
            case "unsafe-ancestor":
                overrides[parent] = new UnixFileFacts(UnixFileKind.Directory, euid + 1, DirectoryMode(0b111_111_111));
                break;
            case "symlinked-unsafe-ancestor":
                var resolved = Path.Combine(root, "t");
                Directory.CreateDirectory(resolved);
                Directory.CreateSymbolicLink(parent, resolved);
                overrides[resolved] = new UnixFileFacts(UnixFileKind.Directory, euid + 1, DirectoryMode(0b111_111_111));
                break;
            case "tighten-ineffective":
                overrides[dir] = new UnixFileFacts(UnixFileKind.Directory, euid, DirectoryMode(0b111_101_101));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        var error = Should.Throw<PtyHostSocketDirUnsafeException>(() =>
            PtyHostSocketDirectory.EnsureSafe(dir, new OverridingProbe(PtyHostSocketDirectory.DefaultProbe, overrides)));

        error.Message.ShouldBe(UnsafeCode);
    }

    [Test]
    [Arguments(107, false)]
    [Arguments(108, true)]
    public void Socket_path_length_is_bounded(int length, bool refused)
    {
        var root = NewRoot();
        var padding = length - 33 - (root.Length + 1);
        padding.ShouldBeGreaterThan(0, $"precondition: the temp root {root} leaves room for a {length}-byte socket path");
        var settings = new SessionRunnerSettings
        {
            SessionLogPath = root,
            PtyHostSocketDir = root + "/" + new string('a', padding),
        };
        var dir = settings.ResolvedPtyHostSocketDir;
        System.Text.Encoding.UTF8.GetByteCount(dir + "/" + Guid.NewGuid().ToString("N")).ShouldBe(length);

        if (refused)
        {
            Should.Throw<PtyHostSocketDirUnsafeException>(() =>
                PtyHostSocketDirectory.EnsureSafe(dir, PtyHostSocketDirectory.DefaultProbe)).Message.ShouldBe(TooLongCode);
            return;
        }

        PtyHostSocketDirectory.EnsureSafe(dir, PtyHostSocketDirectory.DefaultProbe);
        File.GetUnixFileMode(dir).ShouldBe(OwnerOnly, "a 107-byte socket path is accepted");
    }

    [Test]
    [Arguments("regular-file")]
    [Arguments("symlink")]
    [Arguments("foreign-owner")]
    public async Task Unsafe_socket_file_is_refused_before_connect(string shape)
    {
        static string Manifest(string root, Guid _) => Path.Combine(root, "p", "host");
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-sock",
            peerCheck: PtyHostPeerCheckMode.Enforce, manifestPipeName: Manifest,
            serverPipeName: shape == "symlink" ? (_, id) => PtyHostProtocol.PipeNameFor(id) : null,
            listen: shape != "regular-file");
        world.Answer(world.SessionId, world.HostInstance);
        Directory.CreateDirectory(Path.GetDirectoryName(world.ManifestPipeName)!);
        if (shape == "regular-file")
            File.WriteAllText(world.ManifestPipeName, "not a socket");
        if (shape == "symlink")
            File.CreateSymbolicLink(world.ManifestPipeName, PtyHostProtocol.PipeNameFor(world.SessionId));
        if (shape == "foreign-owner")
            PtyHostPeerPolicy.FileProbeOverride.Value = new OverridingProbe(PtyHostSocketDirectory.DefaultProbe,
                new Dictionary<string, UnixFileFacts?>
                {
                    [world.ManifestPipeName] = new(UnixFileKind.Socket, PtyHostStrangerWorld.Euid() + 1, 0xC000 | 0b111_101_101),
                });
        PtyHostPeerPolicy.PeerReaderOverride.Value = world.ProvenPeer();

        var elapsed = Stopwatch.StartNew();
        var adopted = await world.AdoptAsync();
        elapsed.Stop();

        adopted.ShouldBe(0, "an unsafe socket file is not adopted");
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1), "a refusal is not retried for the 5 s connect budget");
        world.Connections.ShouldBe(0, "the socket file is refused before connect");
        world.Frames.ShouldBeEmpty();
        AssertHeld(world, "adoption");
    }

    [Test]
    public async Task Absent_socket_retries_inside_the_budget()
    {
        var elapsed = Stopwatch.StartNew();
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-sock",
            peerCheck: PtyHostPeerCheckMode.Enforce, manifestPipeName: (root, _) => Path.Combine(root, "p", "host"),
            listenDelay: TimeSpan.FromSeconds(1));
        world.Answer(world.SessionId, world.HostInstance);
        PtyHostPeerPolicy.PeerReaderOverride.Value = world.ProvenPeer();

        (await world.AdoptAsync()).ShouldBe(1, "an absent socket keeps retrying until the host binds");
        elapsed.Stop();

        world.Runtime.Get(world.SessionId).Status.ShouldBe("Running");
        elapsed.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Peer_uid_must_match_the_runner()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-sock",
            peerCheck: PtyHostPeerCheckMode.Enforce);
        world.Answer(world.SessionId, world.HostInstance);
        PtyHostPeerPolicy.PeerReaderOverride.Value = _ => new PtyHostPeerCredentials(world.StandIn.Id, PtyHostStrangerWorld.Euid() + 1);

        (await world.AdoptAsync()).ShouldBe(0, "a peer with the host pid but another uid is not the host");

        await world.PeerClosedAsync(TimeSpan.FromSeconds(5));
        world.Connections.ShouldBe(1);
        world.Frames.ShouldBeEmpty("the uid is checked before the hello");
        AssertHeld(world, "adoption");
    }

    [Test]
    public async Task Unsafe_directory_fails_the_launch_before_spawn()
    {
        var root = NewRoot();
        var target = Path.Combine(root, "sock-target");
        Directory.CreateDirectory(target, OwnerOnly);
        var link = Path.Combine(root, "sock-link");
        Directory.CreateSymbolicLink(link, target);
        var settings = FakeHostSettings(root);
        settings.PtyHostSocketDir = link;
        var sessionId = Guid.NewGuid();
        var runtime = new SessionRunnerRuntime(Options.Create(settings), new PtyHostStrangerWorld.RecordingLogger());
        var pidFile = Path.Combine(root, "fake-host.pid");
        try
        {
            Exception error;
            using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "c1653-unsafe-dir-launch"))
                error = await Should.ThrowAsync<Exception>(() => runtime.StartAsync(Launch(sessionId, root), CancellationToken.None));

            var innermost = Innermost(error);
            innermost.ShouldBeOfType<PtyHostSocketDirUnsafeException>(error.ToString());
            innermost.Message.ShouldBe(UnsafeCode);
            File.Exists(pidFile).ShouldBeFalse("no host is spawned for an unsafe socket directory");
            (Directory.Exists(settings.PtyHostLogDir) ? Directory.GetFiles(settings.PtyHostLogDir) : []).ShouldBeEmpty();
            File.Exists(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId)).ShouldBeFalse();
            File.Exists(PtyHostProtocol.PipeNameFor(sessionId)).ShouldBeFalse("there is no /tmp fallback");
            runtime.List().ShouldNotContain(s => s.SessionId == sessionId, "the failed launch is removed");
        }
        finally
        {
            KillPidFile(pidFile);
            await runtime.DisposeAsync();
        }
    }

    [Test]
    public async Task Legacy_real_host_is_adopted_after_owned_enforce()
    {
        var root = NewRoot();
        var target = Path.Combine(root, "sock-target");
        Directory.CreateDirectory(target, OwnerOnly);
        var link = Path.Combine(root, "sock-link");
        Directory.CreateSymbolicLink(link, target);
        var legacy = new SessionRunnerSettings { SessionLogPath = root, PtyHostLingerHours = 0.02 };
        var owned = new SessionRunnerSettings
        {
            SessionLogPath = root, PtyHostLingerHours = 0.02, PtyHostSocketDir = link,
            PtyHostSocketDirMode = PtyHostSocketDirMode.Owned, PtyHostPeerCheck = PtyHostPeerCheckMode.Enforce,
        };
        var sessionId = Guid.NewGuid();
        var runtimeA = new SessionRunnerRuntime(Options.Create(legacy), new PtyHostStrangerWorld.RecordingLogger());
        var logger = new PtyHostStrangerWorld.RecordingLogger();
        SessionRunnerRuntime? runtimeB = null;
        RunnerSessionDto? dto = null;
        try
        {
            using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "c1653-legacy-real-host-launch"))
                dto = await runtimeA.StartAsync(new RunnerLaunchRequest(sessionId, "/bin/sh", ["-c", "read line"],
                    new Dictionary<string, string>(), root, 80, 24,
                    AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow)), CancellationToken.None);
            dto.Status.ShouldBe("Running");
            await runtimeA.DisposeAsync();
            PtyHostManifest.TryLoad(PtyHostManifest.PathFor(legacy.PtyHostManifestDir, sessionId))!
                .PipeName.ShouldBe(PtyHostProtocol.PipeNameFor(sessionId), "Legacy launches keep the /tmp name");

            runtimeB = new SessionRunnerRuntime(Options.Create(owned), logger);
            using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "c1653-legacy-real-host-adoption"))
            {
                (await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None))
                    .ShouldBe(1, "a legacy host is adopted through the socket-file and peer checks");
                runtimeB.Get(sessionId).Status.ShouldBe("Running");
                (await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None))
                    .ShouldBe(0);
            }

            var lines = logger.Text.Split('\n').Where(line => line.Contains(LegacyToken, StringComparison.Ordinal)).ToArray();
            lines.ShouldBe([$"{LegacyToken}: session {sessionId}"], "the legacy path is logged once per session");
            logger.Text.ShouldContain(UnsafeCode, customMessage: "the sweep diagnostic reports the unsafe directory");
            logger.Text.ShouldNotContain(MismatchToken);
            runtimeB.Get(sessionId).Status.ShouldBe("Running", "the directory diagnostic never gates adoption");
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
    public async Task Forged_legacy_peer_is_refused()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-sock",
            peerCheck: PtyHostPeerCheckMode.Enforce, socketDirMode: PtyHostSocketDirMode.Owned);
        world.Answer(world.SessionId, world.HostInstance);

        (await world.AdoptAsync()).ShouldBe(0, "a legacy path gets the same peer check under Owned and Enforce");

        await world.PeerClosedAsync(TimeSpan.FromSeconds(5));
        world.Frames.ShouldBeEmpty();
        AssertHeld(world, "adoption");
    }

    [Test]
    public async Task Fresh_launch_refuses_a_foreign_peer_before_hello()
    {
        var root = NewRoot();
        var settings = FakeHostSettings(root);
        var sessionId = Guid.NewGuid();
        var canary = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(settings.ResolvedPtyHostSocketDir, OwnerOnly);
        var logger = new PtyHostStrangerWorld.RecordingLogger();
        await using var peer = await PtyHostFakeHost.FakePeer.StartAsync(
            Path.Combine(settings.ResolvedPtyHostSocketDir, sessionId.ToString("N")),
            new HelloAckMessage(PtyHostProtocol.Version, "c1653-fake", sessionId, PtyHostStatus.WaitingForLaunch, [], Guid.NewGuid()));
        var runtime = new SessionRunnerRuntime(Options.Create(settings), logger);
        var pidFile = Path.Combine(root, "fake-host.pid");
        try
        {
            Exception error;
            using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "c1653-foreign-peer-launch"))
                error = await Should.ThrowAsync<Exception>(() => runtime.StartAsync(
                    Launch(sessionId, root, new Dictionary<string, string> { ["C1653_CANARY"] = canary }), CancellationToken.None));

            var innermost = Innermost(error);
            innermost.ShouldBeOfType<PtyHostPeerRefusedException>(error.ToString());
            innermost.Message.ShouldBe(MismatchToken, "the refusal message is the fixed code only");
            try { await peer.PeerClosedAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { throw new TimeoutException($"the peer never saw the runner close its connection; StartAsync threw {error}"); }
            peer.HelloSeen.ShouldBeFalse("the peer check runs before the hello");
            peer.Frames.ShouldBeEmpty("no Launch, and so no launch environment, reaches a foreign peer");
            logger.Text.Split('\n').Where(line => line.Contains(MismatchToken, StringComparison.Ordinal))
                .ShouldBe([$"{MismatchToken}: launch refused for session {sessionId}"]);

            File.Exists(pidFile).ShouldBeTrue("the fake host source ran");
            var hostPid = int.Parse(File.ReadAllText(pidFile).Trim());
            var probe = new SystemProcessLivenessProbe();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (probe.IsAlive(hostPid, DateTime.UtcNow) && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            probe.IsAlive(hostPid, DateTime.UtcNow).ShouldBeFalse("a refused fresh launch tears down the host it spawned");
        }
        finally
        {
            KillPidFile(pidFile);
            await runtime.DisposeAsync();
        }
    }

    private static void AssertHeld(PtyHostStrangerWorld world, string verb)
    {
        world.StandIn.WaitForExit(500).ShouldBeFalse("a peer refusal never kills the manifest's host");
        File.Exists(world.ManifestPath).ShouldBeTrue("a peer refusal keeps the manifest");
        PtyHostStrangerWorld.HashFile(world.ManifestPath).ShouldBe(world.ManifestHash);
        var dto = world.Runtime.Get(world.SessionId);
        dto.Status.ShouldBe("Starting");
        dto.Pending.ShouldBe(UnprovenReason);
        world.ExitedEvents(world.SessionId).ShouldBe(0);
        world.LogLines(MismatchToken).ShouldBe([$"{MismatchToken}: {verb} refused for session {world.SessionId}"]);
    }

    private static string NewRoot()
    {
        Path.GetTempPath().ShouldBe("/tmp/", "precondition: the owned socket paths are sized for a /tmp temp root");
        return TestSessionLogRoot.Create("c1653-sock");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static SessionRunnerSettings FakeHostSettings(string root) => new()
    {
        SessionLogPath = root,
        PtyHostSourceDir = PtyHostFakeHost.WriteFakeHostSource(root),
        PtyHostConnectTimeoutSec = 5,
        PtyHostLingerHours = 0.02,
        PtyHostSocketDirMode = PtyHostSocketDirMode.Owned,
        PtyHostPeerCheck = PtyHostPeerCheckMode.Enforce,
    };

    private static RunnerLaunchRequest Launch(Guid sessionId, string root, Dictionary<string, string>? env = null) =>
        new(sessionId, "/bin/sh", ["-c", "read line"], env ?? new Dictionary<string, string>(), root, 80, 24,
            AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow));

    private static uint DirectoryMode(int permissions) => 0x4000 | (uint)permissions;

    private static Exception Innermost(Exception error)
    {
        while (error.InnerException is { } inner) error = inner;
        return error;
    }

    private static void KillPidFile(string pidFile)
    {
        if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid))
            KillBestEffort(pid);
    }

    private static void KillBestEffort(int? pid)
    {
        if (pid is not int value) return;
        try { using var process = Process.GetProcessById(value); process.Kill(entireProcessTree: true); }
        catch { /* Already gone. */ }
    }
}
