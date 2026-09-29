using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Antiphon.Tests.Scripts;

// Direct protocol peer for the Linux helper. Every probe owns its socket and process.
internal sealed class LinuxSupervisorProbe : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly Process _supervisor;
    private Socket? _socket;
    private NetworkStream? _stream;
    private readonly string _controlDirectory;

    internal string Nonce { get; } = Guid.NewGuid().ToString("N");
    internal string ResultsDirectory { get; } = Path.Combine(Path.GetTempPath(), "c806-probe-results-" + Guid.NewGuid().ToString("N"));
    internal int SupervisorId => _supervisor.Id;
    internal bool SupervisorExited => _supervisor.HasExited;
    internal string RootFrame { get; private set; } = "";

    internal LinuxSupervisorProbe(string caseName, long failsafeMilliseconds = 7000)
    {
        _controlDirectory = Path.Combine(Path.GetTempPath(), "c806-probe-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_controlDirectory);
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var socketPath = Path.Combine(_controlDirectory, "owner.sock");
        _listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        _listener.Listen(1);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory()
        };
        foreach (var arg in new[] { ScriptHarnessProcessFixture.HelperPath, "linux-owner", socketPath, Nonce,
                     ScriptHarnessProcess.ResolvePowerShell(), ScriptHarnessProcessFixture.ScriptPath, caseName,
                     ResultsDirectory, failsafeMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "v1", "-HelperPath", ScriptHarnessProcessFixture.HelperPath })
            start.ArgumentList.Add(arg);
        _supervisor = Process.Start(start) ?? throw new InvalidOperationException("Supervisor did not start.");
    }

    internal async Task<string> ConnectAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _socket = await _listener.AcceptAsync(limit.Token);
        _stream = new NetworkStream(_socket, ownsSocket: false);
        return await LinuxScriptHarnessProcess.ReadFrameAsync(_stream, limit.Token);
    }

    internal async Task SendAsync(string frame)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _stream!.WriteAsync(Encoding.UTF8.GetBytes(frame + "\n"), limit.Token);
    }

    internal async Task<string> StartAsync()
    {
        await SendAsync("START " + Nonce);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return RootFrame = await LinuxScriptHarnessProcess.ReadFrameAsync(_stream!, limit.Token);
    }

    internal async Task<string> ReadAsync()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await LinuxScriptHarnessProcess.ReadFrameAsync(_stream!, limit.Token);
    }

    internal async Task WaitExitedAsync(TimeSpan? timeout = null)
    {
        using var limit = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        await _supervisor.WaitForExitAsync(limit.Token);
    }

    internal void Disconnect() { _stream?.Dispose(); _socket?.Dispose(); }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_supervisor.HasExited && _stream is not null)
                await SendAsync("STOP " + Nonce);
            if (!_supervisor.HasExited) await _supervisor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            if (!_supervisor.HasExited) _supervisor.Kill(entireProcessTree: true);
        }
        finally
        {
            Disconnect();
            _listener.Dispose();
            _supervisor.Dispose();
            if (Directory.Exists(ResultsDirectory)) Directory.Delete(ResultsDirectory, true);
            if (Directory.Exists(_controlDirectory)) Directory.Delete(_controlDirectory, true);
        }
    }
}
