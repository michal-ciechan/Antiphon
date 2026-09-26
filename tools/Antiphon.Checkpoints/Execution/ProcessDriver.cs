namespace Antiphon.Checkpoints;

public interface IProcessHandle : IDisposable
{
    bool Start();
    void BeginRead(Action<string?> stdout, Action<string?> stderr);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    bool HasExited { get; }
    int ExitCode { get; }
    void Kill(bool entireProcessTree);
}

public interface IProcessHandleFactory
{
    IProcessHandle Create(ProcessStartInfo startInfo);
}

internal sealed class SystemProcessHandleFactory : IProcessHandleFactory
{
    public IProcessHandle Create(ProcessStartInfo startInfo) => new SystemProcessHandle(startInfo);

    private sealed class SystemProcessHandle : IProcessHandle
    {
        private readonly Process _process;
        public SystemProcessHandle(ProcessStartInfo startInfo) =>
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        public bool Start() => _process.Start();
        public void BeginRead(Action<string?> stdout, Action<string?> stderr)
        {
            _process.OutputDataReceived += (_, e) => stdout(e.Data);
            _process.ErrorDataReceived += (_, e) => stderr(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        public Task WaitForExitAsync(CancellationToken token) => _process.WaitForExitAsync(token);
        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;
        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);
        public void Dispose() => _process.Dispose();
    }
}

public sealed class ProcessDriver : IDriver
{
    private readonly IProcessHandleFactory _factory;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IProcessHandle, byte> _running = new();

    public ProcessDriver(IProcessHandleFactory? factory = null) => _factory = factory ?? new SystemProcessHandleFactory();

    public string? DotnetShim { get; init; }

    public async Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken)
    {
        var (fileName, arguments) = Resolve(request);
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Environment.CurrentDirectory
            : request.WorkingDirectory;
        var psi = CreateStartInfo(fileName, workingDirectory);
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        if (request.Environment is not null)
            foreach (var (name, value) in request.Environment)
                psi.Environment[name] = value;

        using var process = _factory.Create(psi);
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        StreamWriter? log = null;
        if (!string.IsNullOrWhiteSpace(request.LogPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.LogPath))!);
            log = new StreamWriter(request.LogPath, append: true) { AutoFlush = true };
        }

        var outputGate = new object();
        if (!process.Start())
        {
            log?.Dispose();
            return new DriverResult(ExitCodes.Invalid, "", "process did not start");
        }
        _running.TryAdd(process, 0);
        process.BeginRead(
            line => { lock (outputGate) Capture(line, stdout, log, request.OnOutput); },
            line => { lock (outputGate) Capture(line, stderr, log, request.OnOutput); });
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process, entireProcessTree: true);
            // WaitForExitAsync also waits for redirected pipes. A descendant can retain a pipe
            // after its parent dies, even when the requested tree kill has returned.
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(drain.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (drain.IsCancellationRequested)
            {
                const string message = "process kill drain timed out after 10s; abandoning redirected output";
                lock (outputGate)
                {
                    log?.WriteLine(message);
                    request.OnOutput?.Invoke(message);
                }
            }
            throw;
        }
        finally
        {
            _running.TryRemove(process, out _);
            log?.Dispose();
        }

        return new DriverResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public void Kill(bool entireProcessTree)
    {
        foreach (var process in _running.Keys)
            KillProcess(process, entireProcessTree);
    }

    private static void KillProcess(IProcessHandle process, bool entireProcessTree)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public static ProcessStartInfo CreateStartInfo(string fileName, string workingDirectory) =>
        new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

    private (string FileName, IReadOnlyList<string> Arguments) Resolve(DriverRequest request)
    {
        var shim = DotnetShim;
        if (string.IsNullOrWhiteSpace(shim) || !request.FileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return (request.FileName, request.Arguments);
        if (shim.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-NonInteractive", "-File", shim };
            args.AddRange(request.Arguments);
            return ("pwsh", args);
        }

        return (shim, request.Arguments);
    }

    private static void Capture(string? line, System.Text.StringBuilder sink, StreamWriter? log, Action<string>? onOutput)
    {
        if (line is null)
            return;
        sink.AppendLine(line);
        log?.WriteLine(line);
        onOutput?.Invoke(line);
    }
}
