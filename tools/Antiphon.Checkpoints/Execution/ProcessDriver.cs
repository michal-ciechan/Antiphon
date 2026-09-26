namespace Antiphon.Checkpoints;

public sealed class ProcessDriver : IDriver
{
    private Process? _current;

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

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        StreamWriter? log = null;
        if (!string.IsNullOrWhiteSpace(request.LogPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.LogPath))!);
            log = new StreamWriter(request.LogPath, append: true) { AutoFlush = true };
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data, stdout, log, request.OnOutput);
        process.ErrorDataReceived += (_, e) => Capture(e.Data, stderr, log, request.OnOutput);

        if (!process.Start())
            return new DriverResult(ExitCodes.Invalid, "", "process did not start");

        _current = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(entireProcessTree: true);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_current, process))
                _current = null;
            log?.Dispose();
        }

        return new DriverResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public void Kill(bool entireProcessTree)
    {
        var process = _current;
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
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
