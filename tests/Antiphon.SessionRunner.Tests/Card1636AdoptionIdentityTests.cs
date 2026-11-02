using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
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
public class Card1636AdoptionIdentityTests
{
    [Test]
    public async Task Hello_naming_another_session_is_not_adopted()
    {
        await using var world = await StrangerWorld.StartAsync(recordHostInstance: true);
        var stranger = Guid.NewGuid();
        world.Answer(stranger, world.HostInstance);

        (await world.AdoptAsync()).ShouldBe(0, "a hello for another session must not be adopted as this one");

        world.AssertRefused(stranger);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hello_from_another_host_instance_is_not_adopted(bool omitHostInstance)
    {
        await using var world = await StrangerWorld.StartAsync(recordHostInstance: true);
        var stranger = Guid.NewGuid();
        world.Answer(world.SessionId, omitHostInstance ? null : stranger);

        (await world.AdoptAsync()).ShouldBe(0, "the manifest recorded a host instance the hello does not name");

        world.AssertRefused(stranger);
    }

    [Test]
    public async Task Hello_naming_the_manifest_session_and_host_is_adopted()
    {
        await using var world = await StrangerWorld.StartAsync(recordHostInstance: true);
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
        await using (var legacy = await StrangerWorld.StartAsync(recordHostInstance: false))
        {
            legacy.Answer(legacy.SessionId, Guid.NewGuid());
            (await legacy.AdoptAsync()).ShouldBe(1);
            legacy.Runtime.Get(legacy.SessionId).Status.ShouldBe("Running");
        }

        await using var stranger = await StrangerWorld.StartAsync(recordHostInstance: false);
        var other = Guid.NewGuid();
        stranger.Answer(other, Guid.NewGuid());
        (await stranger.AdoptAsync()).ShouldBe(0);
        stranger.AssertRefused(other);
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
        var runtimeA = new SessionRunnerRuntime(Options.Create(settings), new RecordingLogger());
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

            var logger = new RecordingLogger();
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

    private static void KillBestEffort(int? pid)
    {
        if (pid is not int value) return;
        try { using var process = Process.GetProcessById(value); process.Kill(entireProcessTree: true); }
        catch { /* Already gone. */ }
    }

    private sealed class StrangerWorld : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(60));
        private readonly ConcurrentQueue<string> _requests = new();
        private readonly TaskCompletionSource<(Guid Session, Guid? Host)> _answer =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Process _standIn;
        private Task? _server;

        private StrangerWorld(Process standIn, string root)
        {
            _standIn = standIn;
            Settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostLingerHours = 0.02 };
            Runtime = new SessionRunnerRuntime(Options.Create(Settings), Logger);
        }

        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid HostInstance { get; } = Guid.NewGuid();
        public SessionRunnerSettings Settings { get; }
        public RecordingLogger Logger { get; } = new();
        public SessionRunnerRuntime Runtime { get; }
        public IReadOnlyList<string> Requests => _requests.ToArray();

        public static async Task<StrangerWorld> StartAsync(bool recordHostInstance)
        {
            // The sweep only reaches the pipe for a live host pid with a matching start; refused
            // adoption then kills that pid, so it must be a disposable process of this test.
            var standIn = Process.Start(OperatingSystem.IsWindows()
                ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 120 127.0.0.1")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
                : new ProcessStartInfo("/bin/sleep", "120") { UseShellExecute = false })!;
            var world = new StrangerWorld(standIn, TestSessionLogRoot.Create("c1636-adopt"));
            try
            {
                new PtyHostManifest
                {
                    SessionId = world.SessionId,
                    HostInstanceId = recordHostInstance ? world.HostInstance : null,
                    PipeName = PtyHostProtocol.PipeNameFor(world.SessionId),
                    HostPid = standIn.Id,
                    HostStartTimeUtc = standIn.StartTime.ToUniversalTime(),
                    Cols = 80, Rows = 24, CreatedAtUtc = DateTime.UtcNow,
                    AcceptedStartedAt = SessionGeneration.Normalize(DateTime.UtcNow),
                }.SaveAtomic(PtyHostManifest.PathFor(world.Settings.PtyHostManifestDir, world.SessionId));
                var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                world._server = world.ServeAsync(listening);
                await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return world;
            }
            catch { await world.DisposeAsync(); throw; }
        }

        public void Answer(Guid session, Guid? host) => _answer.TrySetResult((session, host));

        public async Task<int> AdoptAsync()
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "c1636-adoption-sweep");
            return await Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), _stop.Token);
        }

        public void AssertRefused(Guid stranger)
        {
            Requests.ShouldBeEmpty("nothing past hello is sent to a host that named another identity");
            Runtime.Get(SessionId).Status.ShouldBe("Exited");
            Logger.Text.ShouldContain($"{MismatchToken}: adoption refused for session {SessionId}");
            foreach (var format in new[] { "D", "N" })
                Logger.Text.ShouldNotContain(stranger.ToString(format), Case.Insensitive,
                    "the refusal names the session only, never the identity the stranger claimed");
        }

        private async Task ServeAsync(TaskCompletionSource listening)
        {
            while (!_stop.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(PtyHostProtocol.PipeNameFor(SessionId), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try
                {
                    listening.TrySetResult();
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    if (await PtyHostFraming.ReadAsync(pipe, _stop.Token) is not HelloMessage) continue;
                    var (session, host) = await _answer.Task.WaitAsync(_stop.Token);
                    await PtyHostFraming.WriteAsync(pipe, new HelloAckMessage(PtyHostProtocol.Version, "c1636-fake",
                        session, PtyHostStatus.Running, [], host), _stop.Token);
                    while (await PtyHostFraming.ReadAsync(pipe, _stop.Token) is { } request)
                    {
                        _requests.Enqueue(request.GetType().Name);
                        PtyHostMessage? reply = request switch
                        {
                            StatusRequestMessage => new StatusReplyMessage(PtyHostStatus.Running, _standIn.Id,
                                _standIn.StartTime.ToUniversalTime(), 80, 24, 0, null, null),
                            AttachMessage => new AttachedMessage(0, 0),
                            _ => null,
                        };
                        if (reply is not null) await PtyHostFraming.WriteAsync(pipe, reply, _stop.Token);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { }
                finally { await pipe.DisposeAsync(); }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            _stop.Cancel();
            if (_server is not null) try { await _server; } catch { }
            try { if (!_standIn.HasExited) _standIn.Kill(entireProcessTree: true); } catch { }
            _standIn.Dispose();
            _stop.Dispose();
        }
    }

    internal sealed class RecordingLogger : ILogger<SessionRunnerRuntime>
    {
        private readonly object _gate = new();
        private string _text = "";
        public string Text { get { lock (_gate) return _text; } }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate) _text += formatter(state, exception) + (exception is null ? "" : " | " + exception) + "\n";
        }
    }
}
