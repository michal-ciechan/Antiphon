using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1653: a fake pty-host source the launcher accepts and a peer squatting a session's pipe.
/// Shared by the S1 fresh-launch identity class and the S2 socket directory class.
/// </summary>
internal static class PtyHostFakeHost
{
    /// <summary>
    /// The launcher needs the host executable and its deps file. The executable is a shell
    /// intermediary that starts a copied sleep whose name contains <c>PtyHost</c> (so the runner's
    /// teardown name check accepts it), records and prints its pid, and exits 0. The shadow copy
    /// keeps only the host's deps closure, so the deps file names the sleep copy as an asset.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static string WriteFakeHostSource(string root)
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
    /// A peer squatting a session's pipe. It answers hello with the given ack, records each later
    /// frame, and closes on the first one so an unchecked Launch fails at once.
    /// </summary>
    internal sealed class FakePeer : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
        private readonly ConcurrentQueue<(string Type, string Json)> _frames = new();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly NamedPipeServerStream _pipe;
        private Task? _server;
        private volatile bool _helloSeen;

        private FakePeer(string pipeName) =>
            _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        /// <summary>Frames read after hello.</summary>
        public IReadOnlyList<(string Type, string Json)> Frames => _frames.ToArray();

        /// <summary>The runner wrote a hello to this peer.</summary>
        public bool HelloSeen => _helloSeen;

        public static Task<FakePeer> StartAsync(Guid sessionId, HelloAckMessage ack) =>
            StartAsync(PtyHostProtocol.PipeNameFor(sessionId), ack);

        public static Task<FakePeer> StartAsync(string pipeName, HelloAckMessage ack)
        {
            var peer = new FakePeer(pipeName);
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
                _helloSeen = true;
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
