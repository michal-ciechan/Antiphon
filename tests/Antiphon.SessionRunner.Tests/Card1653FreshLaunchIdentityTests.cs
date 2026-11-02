using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Antiphon.PtyHost.Client;
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
[Timeout(15_000)]
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
            PtyHostSourceDir = WriteFakeHostSource(root),
            PtyHostConnectTimeoutSec = 5,
            PtyHostLingerHours = 0.02,
        };
        var logger = new PtyHostStrangerWorld.RecordingLogger();
        await using var peer = await FakePeer.StartAsync(sessionId,
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

    /// <summary>
    /// The launcher needs the host executable and its deps file. The executable is a shell
    /// intermediary that starts a copied sleep whose name contains <c>PtyHost</c> (so the runner's
    /// teardown name check accepts it), records and prints its pid, and exits 0. The shadow copy
    /// keeps only the host's deps closure, so the deps file names the sleep copy as an asset.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static string WriteFakeHostSource(string root)
    {
        var source = Path.Combine(root, "fake-host-src");
        Directory.CreateDirectory(source);
        var sleep = File.Exists("/usr/bin/sleep") ? "/usr/bin/sleep" : "/bin/sleep";
        var host = Path.Combine(source, "fakePtyHost.exe");
        File.Copy(sleep, host);
        var script = Path.Combine(source, PtyHostLauncher.HostExeName);
        File.WriteAllText(script,
            "#!/bin/sh\n"
            + "\"$(dirname \"$0\")/fakePtyHost.exe\" 60 </dev/null >/dev/null 2>&1 &\n"
            + $"echo $! > '{Path.Combine(root, "fake-host.pid")}'\n"
            + "echo $!\n"
            + "exit 0\n");
        File.WriteAllText(Path.Combine(source, "Antiphon.PtyHost.deps.json"), """{ "c1653": ["fakePtyHost.exe"] }""");
        const UnixFileMode executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(script, executable);
        File.SetUnixFileMode(host, executable);
        return source;
    }

    /// <summary>
    /// A peer squatting the session's pipe name. It answers hello with the given ack, records each
    /// later frame, and closes on the first one so an unchecked Launch fails at once.
    /// </summary>
    private sealed class FakePeer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        private readonly ConcurrentQueue<(string Type, string Json)> _frames = new();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly NamedPipeServerStream _pipe;
        private Task? _server;

        private FakePeer(Guid sessionId) =>
            _pipe = new NamedPipeServerStream(PtyHostProtocol.PipeNameFor(sessionId), PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        public IReadOnlyList<(string Type, string Json)> Frames => _frames.ToArray();

        public static Task<FakePeer> StartAsync(Guid sessionId, HelloAckMessage ack)
        {
            var peer = new FakePeer(sessionId);
            peer._server = peer.ServeAsync(ack);
            return Task.FromResult(peer);
        }

        public Task PeerClosedAsync(TimeSpan timeout) => _closed.Task.WaitAsync(timeout);

        private async Task ServeAsync(HelloAckMessage ack)
        {
            try
            {
                await _pipe.WaitForConnectionAsync(_stop.Token);
                if (await PtyHostFraming.ReadAsync(_pipe, _stop.Token) is not HelloMessage) return;
                await PtyHostFraming.WriteAsync(_pipe, ack, _stop.Token);
                if (await PtyHostFraming.ReadAsync(_pipe, _stop.Token) is { } frame)
                    _frames.Enqueue((frame.GetType().Name, JsonSerializer.Serialize<PtyHostMessage>(frame)));
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally
            {
                await _pipe.DisposeAsync();
                _closed.TrySetResult();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            if (_server is not null) try { await _server; } catch { }
            _stop.Dispose();
        }
    }
}
