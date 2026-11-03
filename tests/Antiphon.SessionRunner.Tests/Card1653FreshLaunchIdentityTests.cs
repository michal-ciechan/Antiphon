using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1653 S1 (G2): a fresh launch sends <see cref="LaunchMessage"/>, whose environment carries
/// the session's secrets, only to a peer whose hello names this session, carries a host instance
/// and is still waiting for launch. A fake host source (a <c>/bin/sh</c> intermediary that starts a
/// copied <c>sleep</c> named like a host) satisfies the launcher; a test pipe server bound first at
/// the session's pipe name answers the hello as each case says and records every later frame.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
[NativeLinuxOnly("CARD-1653 fake pty-host is a /bin/sh script and a copied sleep; requires Linux")]
public class Card1653FreshLaunchIdentityTests
{
    private const string MismatchToken = "pty_host_identity_mismatch";

    [Test]
    [Arguments("other-session")]
    [Arguments("missing-instance")]
    [Arguments("running-status")]
    public async Task Fresh_launch_refuses_an_unproven_hello_before_Launch(string shape)
    {
        var root = TestSessionLogRoot.Create("c1653-launch");
        var sessionId = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var canary = Guid.NewGuid().ToString("N");
        var (ackSession, ackInstance, ackStatus) = shape switch
        {
            "other-session" => (stranger, (Guid?)Guid.NewGuid(), PtyHostStatus.WaitingForLaunch),
            "missing-instance" => (sessionId, (Guid?)null, PtyHostStatus.WaitingForLaunch),
            "running-status" => (sessionId, (Guid?)Guid.NewGuid(), PtyHostStatus.Running),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var settings = new SessionRunnerSettings
        {
            SessionLogPath = root,
            PtyHostSourceDir = PtyHostFakeHost.WriteFakeHostSource(root),
            PtyHostConnectTimeoutSec = 5,
            PtyHostLingerHours = 0.02,
        };
        var logger = new PtyHostStrangerWorld.RecordingLogger();
        await using var peer = await PtyHostFakeHost.FakePeer.StartAsync(sessionId,
            new HelloAckMessage(PtyHostProtocol.Version, "c1653-fake", ackSession, ackStatus, [], ackInstance));
        var runtime = new SessionRunnerRuntime(Options.Create(settings), logger);
        var pidFile = Path.Combine(root, "fake-host.pid");
        try
        {
            var ex = await Should.ThrowAsync<Exception>(() => runtime.StartAsync(new RunnerLaunchRequest(sessionId,
                "/bin/sh", ["-c", "read line"], new Dictionary<string, string> { ["C1653_CANARY"] = canary },
                root, 80, 24, AcceptedStartedAt: SessionGeneration.Normalize(DateTime.UtcNow)), CancellationToken.None));

            try { await peer.PeerClosedAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { throw new TimeoutException($"the peer never saw the runner close its connection; StartAsync threw {ex}"); }
            peer.Frames.Select(f => f.Type).ShouldBeEmpty("nothing past hello, above all no Launch, reaches an unproven peer");
            peer.Frames.ShouldNotContain(f => f.Json.Contains(canary), "the launch environment never reaches an unproven peer");

            var surfaced = ex.ToString();
            surfaced.ShouldContain(MismatchToken);
            logger.Text.ShouldContain($"{MismatchToken}: launch refused for session {sessionId}");
            foreach (var format in new[] { "D", "N" })
            {
                surfaced.ShouldNotContain(stranger.ToString(format), Case.Insensitive);
                logger.Text.ShouldNotContain(stranger.ToString(format), Case.Insensitive,
                    "the refusal names the session only, never the identity the peer claimed");
            }

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
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid))
                try { using var host = System.Diagnostics.Process.GetProcessById(pid); host.Kill(); } catch { }
            await runtime.DisposeAsync();
        }
    }
}
