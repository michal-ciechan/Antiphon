using System.Diagnostics;
using System.Text;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Owned native Herdr wire peer. The child, rather than the test process, binds its endpoint.</summary>
internal sealed class HerdrTestProcess : IAsyncDisposable
{
    internal const string OwnedFixtureMode = "ANTIPHON_C801_OWNED_PEER_MODE";
    public static string ShellPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh";
    public static string ShellName => OperatingSystem.IsWindows() ? "cmd.exe" : "sh";
    public static string[] InteractiveArgs => OperatingSystem.IsWindows()
        ? ["/d", "/q", "/k", "@echo off & prompt $G"] : ["-i"];

    public static string CreateOwnedUnixArgvChild(string root)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        Directory.CreateDirectory(root);
        var path = System.IO.Path.Combine(root, "owned-argv-child.sh");
        File.WriteAllText(path, "#!/bin/sh\nprintf '%s\\0' \"$@\" > \"$ANTIPHON_TEST_ARGV\"\nwhile :; do sleep 1; done\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    public static Process StartDummy()
    {
        var start = new ProcessStartInfo(ShellPath)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in InteractiveArgs) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new IOException("Owned shell child did not start.");
    }

    private const string Script = """
param([string]$Endpoint)
$ErrorActionPreference = 'Stop'
if ($IsWindows) {
    [Console]::Out.WriteLine('READY')
    while ($true) {
        $server = [System.IO.Pipes.NamedPipeServerStream]::new($Endpoint, [System.IO.Pipes.PipeDirection]::InOut, 1, [System.IO.Pipes.PipeTransmissionMode]::Byte, [System.IO.Pipes.PipeOptions]::Asynchronous)
        try {
            $server.WaitForConnection()
            $reader = [System.IO.StreamReader]::new($server, [System.Text.Encoding]::UTF8, $false, 1024, $true)
            $writer = [System.IO.StreamWriter]::new($server, [System.Text.UTF8Encoding]::new($false), 1024, $true)
            $writer.AutoFlush = $true
            $line = $reader.ReadLine()
            if ($line) {
                $request = $line | ConvertFrom-Json
                $id = $request.id
                $writer.WriteLine('{"id":"' + $id + '","result":{"type":"pong","version":"0.8.2","protocol":20}}')
            }
            $writer.Dispose(); $reader.Dispose()
        } finally { $server.Dispose() }
    }
} else {
    $listener = [System.Net.Sockets.Socket]::new([System.Net.Sockets.AddressFamily]::Unix, [System.Net.Sockets.SocketType]::Stream, [System.Net.Sockets.ProtocolType]::Unspecified)
    try {
        $listener.Bind([System.Net.Sockets.UnixDomainSocketEndPoint]::new($Endpoint))
        $listener.Listen(8)
        [Console]::Out.WriteLine('READY')
        while ($true) {
            $accepted = $listener.Accept()
            try {
                $stream = [System.Net.Sockets.NetworkStream]::new($accepted, $false)
                $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $false, 1024, $true)
                $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false), 1024, $true)
                $writer.AutoFlush = $true
                $line = $reader.ReadLine()
                if ($line) {
                    $request = $line | ConvertFrom-Json
                    $id = $request.id
                    $writer.WriteLine('{"id":"' + $id + '","result":{"type":"pong","version":"0.8.2","protocol":20}}')
                }
                $writer.Dispose(); $reader.Dispose(); $stream.Dispose()
            } finally { $accepted.Dispose() }
        }
    } finally { $listener.Dispose() }
}
""";

    private readonly string? _scriptPath;
    private readonly Task<string>? _stdout;
    private readonly Task<string>? _stderr;
    public Process Process { get; }
    public string? EndpointPath { get; }
    public string Identity => $"{Process.Id}:{Process.StartTime.ToUniversalTime().Ticks}";

    private HerdrTestProcess(Process process, string? scriptPath, string? endpointPath = null,
        Task<string>? stdout = null, Task<string>? stderr = null)
    {
        Process = process;
        _scriptPath = scriptPath;
        EndpointPath = endpointPath;
        _stdout = stdout;
        _stderr = stderr;
    }

    public static async Task<HerdrTestProcess> StartAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        return await StartScriptAsync(Script, endpoint, cancellationToken);
    }

    // Run the test executable with its own runtimeconfig/deps, as the other owned .NET
    // workers do. Loading this assembly into pwsh's default context conflicts with its BCL.
    private static Process StartOwnedProcess(string assemblyPath, string mode)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/FakeHerdrServerListenerTests/C801_CrashedOwnerLeaseReclaimed");
        start.Environment[OwnedFixtureMode] = mode;
        return Process.Start(start) ?? throw new IOException("Herdr owned peer did not start.");
    }

    public static async Task<HerdrTestProcess> StartOwnedFixtureAsync(string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        var child = StartOwnedProcess(assemblyPath, "serve");
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(15));
            // The test runner may print its banner before the assembly hook starts the peer.
            while (await child.StandardOutput.ReadLineAsync(startup.Token) is { } line)
            {
                if (line.StartsWith("READY ", StringComparison.Ordinal))
                    return new HerdrTestProcess(child, null, line[6..],
                        child.StandardOutput.ReadToEndAsync(), stderr);
            }
            throw new IOException($"Herdr peer child did not bind '{assemblyPath}': {await stderr}");
        }
        catch
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            await stderr;
            child.Dispose();
            throw;
        }
    }

    public static async Task<(int ExitCode, string StandardError)> ReclaimDeadLeasesInOwnedProcessAsync(string assemblyPath)
    {
        using var child = StartOwnedProcess(assemblyPath, "reclaim");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await child.WaitForExitAsync(deadline.Token);
            return (child.ExitCode, await stderr);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    private static async Task<HerdrTestProcess> StartScriptAsync(string script, string argument,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"c801-peer-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false), cancellationToken);
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add(argument);
        Process? child = null;
        try
        {
            child = Process.Start(start) ?? throw new IOException("Herdr peer child did not start.");
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(15));
            var line = await child.StandardOutput.ReadLineAsync(startup.Token);
            if (line != "READY")
                throw new IOException($"Herdr peer child did not bind '{argument}': {line}; {await child.StandardError.ReadToEndAsync(startup.Token)}");
            return new HerdrTestProcess(child, scriptPath, argument);
        }
        catch
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
                child.Dispose();
            }
            File.Delete(scriptPath);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        await Process.WaitForExitAsync();
        if (_stdout is not null) await _stdout;
        if (_stderr is not null) await _stderr;
        Process.Dispose();
        if (_scriptPath is not null) File.Delete(_scriptPath);
    }
}
