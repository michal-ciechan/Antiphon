using System.Diagnostics;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1653 S1: an identity refusal at adoption is not proof that the manifest's host pid is
/// the process behind the pipe, so the runner must never kill that pid or forget its manifest.
/// It registers a held pending row (<c>pty_host_identity_unproven</c>) the server already turns
/// into an incident, and the vanish sweep releases that row once the host pid is dead.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public class Card1653RefusalRetainsHostTests
{
    private const string MismatchToken = "pty_host_identity_mismatch";
    private const string UnprovenReason = "pty_host_identity_unproven";

    [Test]
    [Arguments("other-session")]
    [Arguments("other-instance")]
    [Arguments("missing-instance")]
    public async Task Refused_identity_retains_the_host_as_a_pending_row(string shape)
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-adopt");
        var stranger = Guid.NewGuid();
        var (session, instance) = shape switch
        {
            "other-session" => (stranger, (Guid?)world.HostInstance),
            "other-instance" => (world.SessionId, (Guid?)stranger),
            "missing-instance" => (world.SessionId, (Guid?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        world.Answer(session, instance);
        var id = world.SessionId;

        (await world.AdoptAsync()).ShouldBe(0, "a refused identity is not adopted");
        await world.PeerClosedAsync(TimeSpan.FromSeconds(5));
        world.Requests.ShouldBeEmpty("nothing past hello, not even Shutdown, reaches an unproven peer");
        world.StandIn.WaitForExit(500).ShouldBeFalse("a refused identity never kills the manifest's host");
        File.Exists(world.ManifestPath).ShouldBeTrue("a refused identity keeps the manifest");
        PtyHostStrangerWorld.HashFile(world.ManifestPath).ShouldBe(world.ManifestHash);
        var dto = world.Runtime.Get(id);
        dto.Status.ShouldBe("Starting");
        dto.Pending.ShouldBe(UnprovenReason);
        dto.HostPid.ShouldBe(world.StandIn.Id);
        dto.ExitReason.ShouldBe("Unknown", "a held row has not exited, so it carries no exit reason");
        world.ExitedEvents(id).ShouldBe(0, "the server must not see a false exit for a held row");
        world.Logger.Text.ShouldContain($"{MismatchToken}: adoption refused for session {id}");
        if (shape != "missing-instance")
            foreach (var format in new[] { "D", "N" })
                world.Logger.Text.ShouldNotContain(stranger.ToString(format), Case.Insensitive,
                    "the refusal names the session only, never the identity the stranger claimed");

        await world.RestartAsync();
        world.StandIn.WaitForExit(500).ShouldBeFalse("a runner restart never kills a held row's host");

        (await world.AdoptAsync()).ShouldBe(0, "the restarted runner refuses the same identity again");
        var again = world.Runtime.Get(id);
        again.Status.ShouldBe("Starting");
        again.Pending.ShouldBe(UnprovenReason);
        world.StandIn.WaitForExit(500).ShouldBeFalse();
        File.Exists(world.ManifestPath).ShouldBeTrue();
    }

    [Test]
    public async Task Stop_of_a_pending_identity_row_never_kills_its_host()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true, rootPrefix: "c1653-adopt");
        world.Answer(Guid.NewGuid(), world.HostInstance);
        var id = world.SessionId;
        (await world.AdoptAsync()).ShouldBe(0);

        await world.Runtime.KillAsync(id, TimeSpan.FromSeconds(2), CancellationToken.None);

        world.StandIn.WaitForExit(500).ShouldBeFalse("stopping a held identity row never kills the unproven host");
        var dto = world.Runtime.Get(id);
        dto.Status.ShouldBe("Starting");
        dto.Pending.ShouldBe(UnprovenReason);

        world.Runtime.SweepVanishedSessions(new SystemProcessLivenessProbe())
            .ShouldBeEmpty("the host is alive, so the held row is not released");
        world.Runtime.Get(id).Status.ShouldBe("Starting");
        world.Runtime.Get(id).Pending.ShouldBe(UnprovenReason);
        File.Exists(world.ManifestPath).ShouldBeTrue();
    }

    [Test]
    public async Task Pending_identity_row_is_released_once_its_host_is_dead()
    {
        var (deadChild, deadChildStart) = StartKillAndReap();
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true,
            rootPrefix: "c1653-adopt", childPid: deadChild, childStartTimeUtc: deadChildStart);
        world.Answer(Guid.NewGuid(), world.HostInstance);
        var id = world.SessionId;
        (await world.AdoptAsync()).ShouldBe(0);
        var probe = new SystemProcessLivenessProbe();

        world.Runtime.SweepVanishedSessions(probe)
            .ShouldBeEmpty("the release is keyed on the live host pid, not the dead child");
        world.Runtime.Get(id).Status.ShouldBe("Starting");
        File.Exists(world.ManifestPath).ShouldBeTrue();

        world.StandIn.Kill(entireProcessTree: true);
        world.StandIn.WaitForExit(5_000).ShouldBeTrue();
        world.Runtime.SweepVanishedSessions(probe).ShouldBe([id], "a dead host releases its held row");

        var dto = world.Runtime.Get(id);
        dto.Status.ShouldBe("Exited");
        dto.ExitReason.ShouldBe("ProcessVanished");
        dto.Pending.ShouldBeNull("an exited row no longer raises the pending incident");
        world.ExitedEvents(id).ShouldBe(1);
        File.Exists(world.ManifestPath).ShouldBeFalse("the release deletes the retained manifest");

        world.Runtime.SweepVanishedSessions(probe).ShouldBeEmpty();
        world.ExitedEvents(id).ShouldBe(1, "the release publishes SessionExited exactly once");
    }

    [Test]
    public async Task Vanish_sweep_releases_only_identity_pending_rows()
    {
        var root = TestSessionLogRoot.Create("c1653-adopt");
        var settings = new SessionRunnerSettings { SessionLogPath = root };
        var events = new SessionRunnerEventHub();
        var logger = new PtyHostStrangerWorld.RecordingLogger();
        var probe = new SystemProcessLivenessProbe();

        var workspace = CreatePendingRow(settings, events, logger, "workspace_ownership_conflict", out var workspaceManifest);
        await using (workspace)
        {
            workspace.MarkVanishedIfDead(probe).ShouldBeFalse("only identity-pending rows are released by the vanish sweep");
            var dto = workspace.ToDto();
            dto.Status.ShouldBe("Starting");
            dto.Pending.ShouldBe("workspace_ownership_conflict");
            File.Exists(workspaceManifest).ShouldBeTrue();
        }

        var identity = CreatePendingRow(settings, events, logger, UnprovenReason, out _);
        await using (identity)
            identity.MarkVanishedIfDead(probe).ShouldBeTrue("the fixture's dead host does release an identity row");
    }

    [Test]
    public async Task Non_identity_adoption_failure_keeps_the_exit_path()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true,
            rootPrefix: "c1653-adopt", statusReply: PtyHostStrangerWorld.StatusReplyMode.Error);
        world.Answer(world.SessionId, world.HostInstance);
        var id = world.SessionId;

        (await world.AdoptAsync()).ShouldBe(0);

        var dto = world.Runtime.Get(id);
        dto.Status.ShouldBe("Exited");
        dto.Pending.ShouldBeNull();
        File.Exists(world.ManifestPath).ShouldBeFalse("a non-identity failure keeps today's exit path (D-3)");
        world.Logger.Text.ShouldNotContain(MismatchToken);
    }

    private static SessionRunnerRuntime.RunnerSession CreatePendingRow(SessionRunnerSettings settings,
        SessionRunnerEventHub events, PtyHostStrangerWorld.RecordingLogger logger, string reason, out string manifestPath)
    {
        var (deadHost, deadHostStart) = StartKillAndReap();
        var manifest = new PtyHostManifest
        {
            SessionId = Guid.NewGuid(),
            PipeName = PtyHostProtocol.PipeNameFor(Guid.NewGuid()),
            HostPid = deadHost,
            HostStartTimeUtc = deadHostStart,
            Cols = 80, Rows = 24, CreatedAtUtc = DateTime.UtcNow,
            AcceptedStartedAt = SessionGeneration.Normalize(DateTime.UtcNow),
        };
        manifestPath = PtyHostManifest.PathFor(settings.PtyHostManifestDir, manifest.SessionId);
        manifest.SaveAtomic(manifestPath);
        return SessionRunnerRuntime.RunnerSession.CreatePendingConflict(manifest, settings, events, logger, reason);
    }

    private static (int Pid, DateTime StartTimeUtc) StartKillAndReap()
    {
        using var process = PtyHostStrangerWorld.StartStandIn();
        var pid = process.Id;
        var started = process.StartTime.ToUniversalTime();
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5_000).ShouldBeTrue();
        return (pid, started);
    }
}
