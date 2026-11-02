using System.Diagnostics;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1636: the per-session pipe name is not a peer identity. Non-custody adoption must only
/// adopt the process behind the pipe when its helloAck names the manifest's session and, when the
/// manifest recorded one, the manifest's host instance. Custody adoption already refused both.
/// A stand-in process plays the live host pid so the adoption sweep reaches the pipe; the pipe
/// itself is a fake host that answers as told.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(15_000)]
public class Card1636AdoptionIdentityTests
{
    [Test]
    public async Task Hello_naming_another_session_is_not_adopted()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true);
        var stranger = Guid.NewGuid();
        world.Answer(stranger, world.HostInstance);

        (await world.AdoptAsync()).ShouldBe(0, "a hello for another session must not be adopted as this one");

        await AssertRefusedAsync(world, stranger);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hello_from_another_host_instance_is_not_adopted(bool omitHostInstance)
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true);
        var stranger = Guid.NewGuid();
        world.Answer(world.SessionId, omitHostInstance ? null : stranger);

        (await world.AdoptAsync()).ShouldBe(0, "the manifest recorded a host instance the hello does not name");

        await AssertRefusedAsync(world, stranger);
    }

    [Test]
    public async Task Hello_naming_the_manifest_session_and_host_is_adopted()
    {
        await using var world = await PtyHostStrangerWorld.StartAsync(recordHostInstance: true);
        world.Answer(world.SessionId, world.HostInstance);

        (await world.AdoptAsync()).ShouldBe(1);

        world.Runtime.Get(world.SessionId).Status.ShouldBe("Running");
        world.Requests.ShouldContain(nameof(StatusRequestMessage));
        world.Requests.ShouldContain(nameof(AttachMessage));
        world.Logger.Text.ShouldNotContain(MismatchToken);
    }

    /// <summary>
    /// A manifest written before hosts recorded their instance id still adopts on a matching
    /// session, so hosts that outlive the upgrade are not refused and killed; another session
    /// is still refused.
    /// </summary>
    [Test]
    public async Task Manifest_without_a_host_instance_adopts_only_its_own_session()
    {
        await using (var legacy = await PtyHostStrangerWorld.StartAsync(recordHostInstance: false))
        {
            legacy.Answer(legacy.SessionId, Guid.NewGuid());
            (await legacy.AdoptAsync()).ShouldBe(1);
            legacy.Runtime.Get(legacy.SessionId).Status.ShouldBe("Running");
        }

        await using var stranger = await PtyHostStrangerWorld.StartAsync(recordHostInstance: false);
        var other = Guid.NewGuid();
        stranger.Answer(other, Guid.NewGuid());
        (await stranger.AdoptAsync()).ShouldBe(0);
        await AssertRefusedAsync(stranger, other);
    }

    /// <summary>
    /// The real host records the instance id its hello carries, so a restarted runner adopts it
    /// through the comparison rather than through the legacy no-instance path.
    /// </summary>
    [Test]
    public async Task Real_host_records_its_hello_instance_and_is_adopted()
    {
        var root = TestSessionLogRoot.Create("c1636-adopt");
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostLingerHours = 0.02 };
        var sessionId = Guid.NewGuid();
        var runtimeA = new SessionRunnerRuntime(Options.Create(settings), new PtyHostStrangerWorld.RecordingLogger());
        RunnerSessionDto? dto = null;
        SessionRunnerRuntime? runtimeB = null;
        try
        {
            dto = await runtimeA.StartAsync(new RunnerLaunchRequest(sessionId,
                OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
                OperatingSystem.IsWindows() ? [] : ["-c", "read line"],
                new Dictionary<string, string>(), root, 80, 24,
                AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow)), CancellationToken.None);
            dto.Status.ShouldBe("Running");
            await runtimeA.DisposeAsync();

            var manifest = PtyHostManifest.TryLoad(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId))!;
            manifest.HostInstanceId.ShouldNotBeNull("the host records the instance its hello carries");
            manifest.HostInstanceId.ShouldNotBe(Guid.Empty);

            var logger = new PtyHostStrangerWorld.RecordingLogger();
            runtimeB = new SessionRunnerRuntime(Options.Create(settings), logger);
            (await runtimeB.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None)).ShouldBe(1);
            runtimeB.Get(sessionId).Status.ShouldBe("Running");
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

    private const string MismatchToken = "pty_host_identity_mismatch";

    /// <summary>
    /// CARD-1653 changed the refused row from Exited to a held pending row: a refused identity
    /// must leave the manifest's host pid alive and its manifest in place. The refusal itself is
    /// still asserted by the adoption count, nothing past hello, the session not Running and the
    /// fixed log token.
    /// </summary>
    private static async Task AssertRefusedAsync(PtyHostStrangerWorld world, Guid stranger)
    {
        await world.PeerClosedAsync(TimeSpan.FromSeconds(5));
        world.Requests.ShouldBeEmpty("nothing past hello is sent to a host that named another identity");
        var dto = world.Runtime.Get(world.SessionId);
        dto.Status.ShouldBe("Starting");
        dto.Pending.ShouldBe("pty_host_identity_unproven");
        dto.HostPid.ShouldBe(world.StandIn.Id);
        world.StandIn.WaitForExit(500).ShouldBeFalse("a refused identity never kills the manifest's host");
        File.Exists(world.ManifestPath).ShouldBeTrue("a refused identity keeps the manifest");
        world.Logger.Text.ShouldContain($"{MismatchToken}: adoption refused for session {world.SessionId}");
        foreach (var format in new[] { "D", "N" })
            world.Logger.Text.ShouldNotContain(stranger.ToString(format), Case.Insensitive,
                "the refusal names the session only, never the identity the stranger claimed");
    }

    private static void KillBestEffort(int? pid)
    {
        if (pid is not int value) return;
        try { using var process = Process.GetProcessById(value); process.Kill(entireProcessTree: true); }
        catch { /* Already gone. */ }
    }
}
