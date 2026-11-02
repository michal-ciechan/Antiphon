using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading.Channels;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1636/CARD-1653: a manifest whose host pid is a disposable stand-in process, and a fake
/// host behind the session's pipe that answers hello as told. The adoption sweep reaches the pipe
/// because the stand-in is alive at the manifest's start time. Every frame past hello is recorded.
/// </summary>
internal sealed class PtyHostStrangerWorld : IAsyncDisposable
{
    public enum StatusReplyMode { Running, Error }

    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(60));
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly TaskCompletionSource<(Guid Session, Guid? Host)> _answer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ChannelReader<RunnerServerSentEvent>> _eventReaders = [];
    private readonly List<RunnerServerSentEvent> _events = [];
    private readonly StatusReplyMode _statusReply;
    private TaskCompletionSource _peerClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _server;

    private PtyHostStrangerWorld(Process standIn, string root, StatusReplyMode statusReply)
    {
        StandIn = standIn;
        _statusReply = statusReply;
        Settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostLingerHours = 0.02 };
        Runtime = NewRuntime();
    }

    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid HostInstance { get; } = Guid.NewGuid();
    public SessionRunnerSettings Settings { get; }
    public RecordingLogger Logger { get; } = new();
    public SessionRunnerRuntime Runtime { get; private set; }
    public Process StandIn { get; }
    public string ManifestPath => PtyHostManifest.PathFor(Settings.PtyHostManifestDir, SessionId);
    public string ManifestHash { get; private set; } = "";
    public IReadOnlyList<string> Requests => _requests.ToArray();

    public static Process StartStandIn() => Process.Start(OperatingSystem.IsWindows()
        ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 120 127.0.0.1")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
        : new ProcessStartInfo("/bin/sleep", "120") { UseShellExecute = false })!;

    public static async Task<PtyHostStrangerWorld> StartAsync(bool recordHostInstance,
        string rootPrefix = "c1636-adopt", int? childPid = null, DateTime? childStartTimeUtc = null,
        StatusReplyMode statusReply = StatusReplyMode.Running)
    {
        // The sweep only reaches the pipe for a live host pid with a matching start. The stand-in
        // is a disposable process of this test; the world kills it on dispose.
        var standIn = StartStandIn();
        var world = new PtyHostStrangerWorld(standIn, TestSessionLogRoot.Create(rootPrefix), statusReply);
        try
        {
            new PtyHostManifest
            {
                SessionId = world.SessionId,
                HostInstanceId = recordHostInstance ? world.HostInstance : null,
                PipeName = PtyHostProtocol.PipeNameFor(world.SessionId),
                HostPid = standIn.Id,
                HostStartTimeUtc = standIn.StartTime.ToUniversalTime(),
                ChildPid = childPid,
                ChildStartTimeUtc = childStartTimeUtc,
                Cols = 80, Rows = 24, CreatedAtUtc = DateTime.UtcNow,
                AcceptedStartedAt = SessionGeneration.Normalize(DateTime.UtcNow),
            }.SaveAtomic(world.ManifestPath);
            world.ManifestHash = HashFile(world.ManifestPath);
            var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            world._server = world.ServeAsync(listening);
            await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return world;
        }
        catch { await world.DisposeAsync(); throw; }
    }

    public static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public void Answer(Guid session, Guid? host) => _answer.TrySetResult((session, host));

    public async Task<int> AdoptAsync()
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "pty-host-stranger-adoption-sweep");
        return await Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), _stop.Token);
    }

    /// <summary>Completes once the fake host saw the runner close the current connection.</summary>
    public Task PeerClosedAsync(TimeSpan timeout) => Volatile.Read(ref _peerClosed).Task.WaitAsync(timeout);

    /// <summary>SessionExited events published for <paramref name="sessionId"/> by every runtime of this world.</summary>
    public int ExitedEvents(Guid sessionId)
    {
        foreach (var reader in _eventReaders)
            while (reader.TryRead(out var evt))
                _events.Add(evt);
        return _events.Count(e => e.EventName == SessionRunnerEventNames.SessionExited
            && e.Json.Contains(sessionId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Disposes the runtime (a runner restart) and builds a new one over the same state.</summary>
    public async Task RestartAsync()
    {
        await Runtime.DisposeAsync();
        Runtime = NewRuntime();
    }

    private SessionRunnerRuntime NewRuntime()
    {
        var runtime = new SessionRunnerRuntime(Options.Create(Settings), Logger);
        _eventReaders.Add(runtime.Subscribe(_stop.Token));
        return runtime;
    }

    private async Task ServeAsync(TaskCompletionSource listening)
    {
        while (!_stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PtyHostProtocol.PipeNameFor(SessionId), PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            TaskCompletionSource? closed = null;
            try
            {
                listening.TrySetResult();
                await pipe.WaitForConnectionAsync(_stop.Token);
                closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _peerClosed, closed);
                if (await PtyHostFraming.ReadAsync(pipe, _stop.Token) is not HelloMessage) continue;
                var (session, host) = await _answer.Task.WaitAsync(_stop.Token);
                await PtyHostFraming.WriteAsync(pipe, new HelloAckMessage(PtyHostProtocol.Version, "c1636-fake",
                    session, PtyHostStatus.Running, [], host), _stop.Token);
                while (await PtyHostFraming.ReadAsync(pipe, _stop.Token) is { } request)
                {
                    _requests.Enqueue(request.GetType().Name);
                    PtyHostMessage? reply = request switch
                    {
                        StatusRequestMessage when _statusReply == StatusReplyMode.Error =>
                            new ErrorMessage("c1653", "fixture"),
                        StatusRequestMessage => new StatusReplyMessage(PtyHostStatus.Running, StandIn.Id,
                            StandIn.StartTime.ToUniversalTime(), 80, 24, 0, null, null),
                        AttachMessage => new AttachedMessage(0, 0),
                        _ => null,
                    };
                    if (reply is not null) await PtyHostFraming.WriteAsync(pipe, reply, _stop.Token);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
            finally
            {
                await pipe.DisposeAsync();
                closed?.TrySetResult();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        _stop.Cancel();
        if (_server is not null) try { await _server; } catch { }
        try { if (!StandIn.HasExited) StandIn.Kill(entireProcessTree: true); } catch { }
        StandIn.Dispose();
        _stop.Dispose();
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
