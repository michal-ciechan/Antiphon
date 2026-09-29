using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Antiphon.Tests.Scripts;

internal sealed class LinuxScriptHarnessProcess : IOwnedScriptProcess
{
    private readonly ScriptProcessRequest _request;
    private readonly Socket _listener;
    private readonly Process _supervisor;
    private readonly string _nonce = Guid.NewGuid().ToString("N");
    private NetworkStream? _control;
    private Socket? _socket;
    private int _pgid;
    private int _sid;
    private long _supervisorStart;
    private int _rootPid;
    private long _rootStart;
    private bool _terminated;

    public StreamReader Stdout => _supervisor.StandardOutput;
    public StreamReader Stderr => _supervisor.StandardError;
    internal int SupervisorId => _supervisor.Id;
    internal int ConfirmedGroupId => _pgid;
    internal bool SupervisorHasExited => _supervisor.HasExited;
    internal StreamWriter RootStdin => _supervisor.StandardInput;
    internal void DisconnectControl() { _control?.Dispose(); _socket?.Dispose(); }

    internal LinuxScriptHarnessProcess(ScriptProcessRequest request)
    {
        _request = request;
        var helper = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
        if (!File.Exists(helper)) throw new FileNotFoundException("ScriptHarness Linux owner helper was not staged.", helper);
        Directory.CreateDirectory(request.ControlDirectory);
        var socketPath = Path.Combine(request.ControlDirectory, "owner.sock");
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            _listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            _listener.Listen(1);
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory()
            };
            foreach (var arg in new[] { helper, "linux-owner", socketPath, _nonce, request.Executable,
                         request.Script, request.CaseName, request.ResultsDirectory,
                         ((long)(request.ExecutionBudget + request.CleanupBudget).TotalMilliseconds).ToString(CultureInfo.InvariantCulture), "v1" })
                start.ArgumentList.Add(arg);
            if (request.AdditionalArguments is not null)
                foreach (var arg in request.AdditionalArguments) start.ArgumentList.Add(arg);
            _supervisor = Process.Start(start) ?? throw new InvalidOperationException("Linux ScriptHarness owner did not start.");
            if (!request.KeepStdinOpen) _supervisor.StandardInput.Close();
        }
        catch
        {
            _listener.Dispose();
            Directory.Delete(request.ControlDirectory, true);
            throw;
        }
    }

    public async Task<int> StartAndWaitForRootAsync(CancellationToken cancellationToken)
    {
        _socket = await _listener.AcceptAsync(cancellationToken);
        _control = new NetworkStream(_socket, ownsSocket: false);
        var hello = (await ReadFrameAsync(_control, cancellationToken)).Split(' ');
        if (hello.Length != 6 || hello[0] != "HELLO" || hello[1] != _nonce ||
            !int.TryParse(hello[2], out var pid) || !long.TryParse(hello[3], out var start) ||
            !int.TryParse(hello[4], out var pgid) || !int.TryParse(hello[5], out var sid))
            throw new InvalidDataException("Invalid Linux ScriptHarness ownership handshake.");
        var observed = ReadIdentity(pid);
        if (pid != _supervisor.Id || pid <= 1 || pgid <= 1 ||
            pid != pgid || pid != sid || pgid == ReadIdentity(Environment.ProcessId).Group ||
            observed.Start != start || observed.Group != pgid || observed.Session != sid)
            throw new InvalidDataException("Linux ScriptHarness owner identity does not match private session.");
        _pgid = pgid;
        _sid = sid;
        _supervisorStart = start;
        await WriteFrameAsync(_control, "START " + _nonce, cancellationToken);
        var rootFrame = (await ReadFrameAsync(_control, cancellationToken)).Split(' ');
        if (rootFrame.Length != 4 || rootFrame[0] != "ROOT" || rootFrame[1] != _nonce ||
            !int.TryParse(rootFrame[2], out _rootPid) || !long.TryParse(rootFrame[3], out _rootStart) ||
            _rootPid <= 1 || _rootStart <= 0)
            throw new InvalidDataException("Linux ScriptHarness root identity was not reported.");
        var exitFrame = (await ReadFrameAsync(_control, cancellationToken)).Split(' ');
        if (exitFrame.Length != 5 || exitFrame[0] != "EXIT" || exitFrame[1] != _nonce ||
            !int.TryParse(exitFrame[2], out var exitPid) || !long.TryParse(exitFrame[3], out var exitStart) ||
            !int.TryParse(exitFrame[4], out var exitCode) || exitPid != _rootPid || exitStart != _rootStart)
            throw new InvalidDataException("Linux ScriptHarness root exit receipt did not match root identity.");
        return exitCode;
    }

    public async Task TerminateAsync(CancellationToken cancellationToken)
    {
        if (_terminated) return;
        _terminated = true;
        if (_supervisor.HasExited)
            throw new IOException("Linux ScriptHarness supervisor exited before explicit stop; group custody is unknown.");
        if (_control is null)
        {
            // Before a valid handshake, disconnect is the supervisor's safe stop path.
            _listener.Dispose();
            return;
        }
        await WriteFrameAsync(_control, "STOP " + _nonce, cancellationToken);
    }

    public async Task ConfirmDeadAsync(CancellationToken cancellationToken)
    {
        await _supervisor.WaitForExitAsync(cancellationToken);
        if (_pgid == 0) return;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executing = new List<int>();
            foreach (var path in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(path), out var pid)) continue;
                LinuxIdentity identity;
                try { identity = ReadIdentity(pid); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if (identity.Group == _pgid && identity.Session == _sid && identity.State is not ('Z' or 'X'))
                    executing.Add(pid);
            }
            if (executing.Count == 0) return;
            try { await Task.Delay(20, cancellationToken); }
            catch (OperationCanceledException ex)
            {
                throw new IOException("Linux ScriptHarness group still has executing members at cleanup deadline: " +
                    string.Join(",", executing), ex);
            }
        }
    }

    public void CloseStreams()
    {
        _supervisor.StandardOutput.Dispose();
        _supervisor.StandardError.Dispose();
        _supervisor.StandardInput.Dispose();
        _control?.Dispose();
        _socket?.Dispose();
    }

    public void Dispose()
    {
        _control?.Dispose();
        _socket?.Dispose();
        _listener.Dispose();
        _supervisor.Dispose();
    }

    private static LinuxIdentity ReadIdentity(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var end = stat.LastIndexOf(") ", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Malformed /proc identity.");
        var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new LinuxIdentity(fields[0][0], int.Parse(fields[2], CultureInfo.InvariantCulture),
            int.Parse(fields[3], CultureInfo.InvariantCulture), long.Parse(fields[19], CultureInfo.InvariantCulture));
    }

    private readonly record struct LinuxIdentity(char State, int Group, int Session, long Start);

    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var one = new byte[1];
        while (bytes.Length <= 4096)
        {
            if (await stream.ReadAsync(one, token) == 0) throw new EndOfStreamException("ScriptHarness control disconnected.");
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.WriteByte(one[0]);
        }
        throw new InvalidDataException("ScriptHarness control frame exceeds 4096 bytes.");
    }

    private static Task WriteFrameAsync(Stream stream, string message, CancellationToken token) =>
        stream.WriteAsync(Encoding.UTF8.GetBytes(message + "\n"), token).AsTask();
}
