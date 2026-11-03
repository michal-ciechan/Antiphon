using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using Antiphon.PtyHost.Client;
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
/// CARD-1653 S2: the manifest can name another pipe (a legacy or owned path), the fake host can bind
/// another path, bind late or not at all, and <see cref="Frames"/> counts the hello too.
/// </summary>
internal sealed class PtyHostStrangerWorld : IAsyncDisposable
{
    public enum StatusReplyMode { Running, Error }

    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(60));
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentQueue<string> _frames = new();
    private int _connections;
    private readonly TaskCompletionSource<(Guid Session, Guid? Host)> _answer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ChannelReader<RunnerServerSentEvent>> _eventReaders = [];
    private readonly List<RunnerServerSentEvent> _events = [];
    private readonly StatusReplyMode _statusReply;
    private TaskCompletionSource _peerClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _server;

    private PtyHostStrangerWorld(Process standIn, string root, StatusReplyMode statusReply,
        PtyHostPeerCheckMode peerCheck, PtyHostSocketDirMode socketDirMode)
    {
        StandIn = standIn;
        Root = root;
        _statusReply = statusReply;
        Settings = new SessionRunnerSettings
        {
            SessionLogPath = root, PtyHostLingerHours = 0.02,
            PtyHostPeerCheck = peerCheck, PtyHostSocketDirMode = socketDirMode,
        };
        Runtime = NewRuntime();
    }

    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid HostInstance { get; } = Guid.NewGuid();
    public SessionRunnerSettings Settings { get; }
    public string Root { get; }
    public string ManifestPipeName { get; private set; } = "";
    public RecordingLogger Logger { get; } = new();
    public SessionRunnerRuntime Runtime { get; private set; }
    public Process StandIn { get; }
    public string ManifestPath => PtyHostManifest.PathFor(Settings.PtyHostManifestDir, SessionId);
    public string ManifestHash { get; private set; } = "";
    public IReadOnlyList<string> Requests => _requests.ToArray();

    /// <summary>Every frame the fake host read, the hello included.</summary>
    public IReadOnlyList<string> Frames => _frames.ToArray();

    /// <summary>Connections the fake host accepted; 0 means the client never connected.</summary>
    public int Connections => Volatile.Read(ref _connections);

    public static Process StartStandIn() => Process.Start(OperatingSystem.IsWindows()
        ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 120 127.0.0.1")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
        : new ProcessStartInfo("/bin/sleep", "120") { UseShellExecute = false })!;

    public static async Task<PtyHostStrangerWorld> StartAsync(bool recordHostInstance,
        string rootPrefix = "c1636-adopt", int? childPid = null, DateTime? childStartTimeUtc = null,
        StatusReplyMode statusReply = StatusReplyMode.Running,
        PtyHostPeerCheckMode peerCheck = PtyHostPeerCheckMode.Off,
        PtyHostSocketDirMode socketDirMode = PtyHostSocketDirMode.Legacy,
        Func<string, Guid, string>? manifestPipeName = null, Func<string, Guid, string>? serverPipeName = null,
        TimeSpan? listenDelay = null, bool listen = true)
    {
        // The sweep only reaches the pipe for a live host pid with a matching start. The stand-in
        // is a disposable process of this test; the world kills it on dispose.
        var standIn = StartStandIn();
        var world = new PtyHostStrangerWorld(standIn, TestSessionLogRoot.Create(rootPrefix), statusReply,
            peerCheck, socketDirMode);
        try
        {
            world.ManifestPipeName = manifestPipeName?.Invoke(world.Root, world.SessionId) ?? PtyHostProtocol.PipeNameFor(world.SessionId);
            var bind = serverPipeName?.Invoke(world.Root, world.SessionId) ?? world.ManifestPipeName;
            new PtyHostManifest
            {
                SessionId = world.SessionId,
                HostInstanceId = recordHostInstance ? world.HostInstance : null,
                PipeName = world.ManifestPipeName,
                HostPid = standIn.Id,
                HostStartTimeUtc = standIn.StartTime.ToUniversalTime(),
                ChildPid = childPid,
                ChildStartTimeUtc = childStartTimeUtc,
                Cols = 80, Rows = 24, CreatedAtUtc = DateTime.UtcNow,
                AcceptedStartedAt = SessionGeneration.Normalize(DateTime.UtcNow),
            }.SaveAtomic(world.ManifestPath);
            world.ManifestHash = HashFile(world.ManifestPath);
            if (!listen) return world;
            if (Path.IsPathRooted(bind)) Directory.CreateDirectory(Path.GetDirectoryName(bind)!);
            var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            world._server = world.ServeAsync(bind, listenDelay ?? TimeSpan.Zero, listening);
            if (listenDelay is null) await listening.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return world;
        }
        catch { await world.DisposeAsync(); throw; }
    }

    public static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public void Answer(Guid session, Guid? host) => _answer.TrySetResult((session, host));

    public async Task<int> AdoptAsync(IProcessLivenessProbe? probe = null)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "pty-host-stranger-adoption-sweep");
        return await Runtime.AdoptOrphanedHostsAsync(probe ?? new SystemProcessLivenessProbe(), _stop.Token);
    }

    /// <summary>The stand-in as the kernel would report the manifest's real host: its pid and the runner's uid.</summary>
    public Func<Microsoft.Win32.SafeHandles.SafePipeHandle, PtyHostPeerCredentials?> ProvenPeer() =>
        _ => new PtyHostPeerCredentials(StandIn.Id, OperatingSystem.IsWindows() ? null : Euid());

    /// <summary>Log lines that contain <paramref name="token"/>, for exact-line assertions.</summary>
    public IReadOnlyList<string> LogLines(string token) =>
        Logger.Text.Split('\n').Where(line => line.Contains(token, StringComparison.Ordinal)).ToArray();

    /// <summary>The effective uid of this test process, read independently of the code under test.</summary>
    public static uint Euid() => OperatingSystem.IsWindows() ? 0 : GetEuid();

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

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

    private async Task ServeAsync(string pipeName, TimeSpan listenDelay, TaskCompletionSource listening)
    {
        if (listenDelay > TimeSpan.Zero)
            try { await Task.Delay(listenDelay, _stop.Token); }
            catch (OperationCanceledException) { return; }
        while (!_stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            TaskCompletionSource? closed = null;
            try
            {
                listening.TrySetResult();
                await pipe.WaitForConnectionAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _peerClosed, closed);
                var hello = await PtyHostFraming.ReadAsync(pipe, _stop.Token);
                if (hello is not null) _frames.Enqueue(hello.GetType().Name);
                if (hello is not HelloMessage) continue;
                var (session, host) = await _answer.Task.WaitAsync(_stop.Token);
                await PtyHostFraming.WriteAsync(pipe, new HelloAckMessage(PtyHostProtocol.Version, "c1636-fake",
                    session, PtyHostStatus.Running, [], host), _stop.Token);
                while (await PtyHostFraming.ReadAsync(pipe, _stop.Token) is { } request)
                {
                    _frames.Enqueue(request.GetType().Name);
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

/// <summary>
/// CARD-1653 S2: the real file probe except for the named paths, whose facts the test chooses
/// (a foreign owner, a wide mode). Every other path is read from the kernel.
/// </summary>
internal sealed class OverridingProbe(IUnixFileProbe real, IReadOnlyDictionary<string, UnixFileFacts?> map) : IUnixFileProbe
{
    public UnixFileFacts? Lstat(string path) =>
        map.TryGetValue(Path.TrimEndingDirectorySeparator(path), out var facts) ? facts : real.Lstat(path);

    public uint Euid() => real.Euid();
}

/// <summary>
/// CARD-1653 S2: answers alive (as the system does) until a wrapped peer reader has run, then dead.
/// The sweep's pre-check sees the host alive; the peer check's start-time re-check sees it dead.
/// </summary>
internal sealed class FlipProbe : IProcessLivenessProbe
{
    private readonly SystemProcessLivenessProbe _real = new();
    private volatile bool _flipped;
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Func<Microsoft.Win32.SafeHandles.SafePipeHandle, PtyHostPeerCredentials?> Wrap(
        Func<Microsoft.Win32.SafeHandles.SafePipeHandle, PtyHostPeerCredentials?> reader) =>
        handle => { _flipped = true; return reader(handle); };

    public bool IsAlive(int pid, DateTime startedAt)
    {
        Interlocked.Increment(ref _calls);
        return !_flipped && _real.IsAlive(pid, startedAt);
    }

    public string? TryGetProcessName(int pid) => _real.TryGetProcessName(pid);

    public DateTime? TryGetStartTimeUtc(int pid) => _real.TryGetStartTimeUtc(pid);
}
