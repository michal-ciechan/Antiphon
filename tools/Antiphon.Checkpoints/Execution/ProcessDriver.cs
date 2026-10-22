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
    public IProcessHandle Create(ProcessStartInfo startInfo) =>
        OperatingSystem.IsWindows() ? new WindowsJobProcessHandle(startInfo) : new SystemProcessHandle(startInfo);

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
        public void Kill(bool entireProcessTree)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        public void Dispose() => _process.Dispose();
    }
}

public sealed class ProcessDriver : IConfinedDriver
{
    private readonly IProcessHandleFactory _factory;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IProcessHandle, byte> _running = new();

    public ProcessDriver(IProcessHandleFactory? factory = null) => _factory = factory ?? new SystemProcessHandleFactory();

    public string? DotnetShim { get; init; }

    public BuildConfinement.LedgerCapture CaptureLedger(DriverRequest request) =>
        Confines(request)
            ? BuildConfinement.CaptureLedger(WorkingDirectoryOf(request), request.Arguments)
            : BuildConfinement.LedgerCapture.None;

    private bool Confines(DriverRequest request) =>
        string.IsNullOrWhiteSpace(DotnetShim)
        && request.FileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        && request.Arguments.Count > 0
        && request.Arguments[0].Equals("build", StringComparison.OrdinalIgnoreCase);

    private static string WorkingDirectoryOf(DriverRequest request) =>
        string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Environment.CurrentDirectory : request.WorkingDirectory;

    public async Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken)
    {
        var arguments = request.Arguments;
        BuildConfinement.Baseline? baseline = null;
        var workingDirectory = WorkingDirectoryOf(request);
        if (Confines(request))
        {
            var confined = BuildConfinement.Prepare(workingDirectory, request.Arguments, request.Environment, cancellationToken, request.Ledger);
            if (confined.Refusal is not null)
            {
                AppendLog(request.LogPath, confined.Detail);
                var code = ExitCodes.Invalid;
                return new DriverResult(code, "", confined.Detail);
            }

            arguments = confined.Arguments;
            baseline = confined.Baseline;
        }

        var (fileName, resolved) = Resolve(request with { Arguments = arguments });
        var psi = CreateStartInfo(fileName, workingDirectory);
        foreach (var argument in resolved)
            psi.ArgumentList.Add(argument);
        if (request.Environment is not null)
            foreach (var (name, value) in request.Environment)
                psi.Environment[name] = value;

        new Antiphon.Build.BuildDriverEnvironment().Apply(psi);

        using var process = _factory.Create(psi);
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        StreamWriter? log = null;
        if (!string.IsNullOrWhiteSpace(request.LogPath))
        {
            var logDirectory = Path.GetDirectoryName(Path.GetFullPath(request.LogPath))!;
            var logReason = SafeWrite.OpenAppend(request.LogPath, SafeWrite.Under(logDirectory), out var logStream);
            if (logReason is not null || logStream is null)
                return new DriverResult(ExitCodes.Invalid, "", "refused: " + logReason);
            log = new StreamWriter(logStream) { AutoFlush = true };
        }

        var outputGate = new object();
        var started = false;
        var processExit = 0;
        DateTimeOffset? exitedAt = null;
        string? earlyDetail = null;
        OperationCanceledException? cancelled = null;
        BuildConfinement.Settlement? settlement = null;
        try
        {
            if (!process.Start())
            {
                earlyDetail = "process did not start";
            }
            else
            {
                started = true;
                _running.TryAdd(process, 0);
                process.BeginRead(
                    line => { lock (outputGate) Capture(line, stdout, log, request.OnOutput); },
                    line => { lock (outputGate) Capture(line, stderr, log, request.OnOutput); });
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                processExit = process.ExitCode;
                exitedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException ex)
        {
            if (started)
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
            }

            cancelled = ex;
        }
        finally
        {
            if (baseline is not null)
            {
                settlement = BuildConfinement.Settle(baseline);
                if (settlement.Detail.Length > 0)
                {
                    lock (outputGate)
                        log?.Write(settlement.Detail);
                }
            }

            _running.TryRemove(process, out _);
            log?.Dispose();
        }

        if (settlement?.Kind == "tampered")
            return new DriverResult(ExitCodes.Invalid, stdout.ToString(), settlement.Detail + stderr.ToString());
        if (settlement?.Kind is not null)
            return new DriverResult(ExitCodes.Invalid, stdout.ToString(), settlement.Detail + stderr.ToString());
        if (earlyDetail is not null)
            return new DriverResult(ExitCodes.Invalid, "", earlyDetail);
        if (cancelled is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancelled).Throw();
        if (!started)
            return new DriverResult(ExitCodes.Invalid, "", "process did not start");
        return new DriverResult(processExit, stdout.ToString(), stderr.ToString()) { Exited = exitedAt };
    }

    internal static void AppendLog(string? logPath, string text)
    {
        if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrEmpty(text))
            return;
        try
        {
            var logDirectory = Path.GetDirectoryName(Path.GetFullPath(logPath))!;
            var reason = SafeWrite.OpenAppend(logPath, SafeWrite.Under(logDirectory), out var stream);
            if (reason is not null || stream is null)
                return;
            using var writer = new StreamWriter(stream) { AutoFlush = true };
            writer.Write(text);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
