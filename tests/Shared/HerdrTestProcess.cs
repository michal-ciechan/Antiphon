using System.Diagnostics;
using System.Text;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Owned native Herdr wire peer. The child, rather than the test process, binds its endpoint.</summary>
internal sealed class HerdrTestProcess : IAsyncDisposable
{
    private const string OwnedFixtureScript = """
param([string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$assembly = [System.Reflection.Assembly]::LoadFrom($AssemblyPath)
$endpointType = $assembly.GetType('Antiphon.SessionRunner.Tests.FakeHerdrEndpoint', $true)
$transportType = $assembly.GetType('Antiphon.SessionRunner.Tests.FakeHerdrTransport', $true)
$endpoint = [Activator]::CreateInstance($endpointType, [object[]]@($null))
$transport = [Activator]::CreateInstance($transportType, [object[]]@($endpoint))
$transport.Bind()
[Console]::Out.WriteLine('READY ' + $endpoint.Path)
while ($true) {
    $stream = $transport.AcceptAsync([System.Threading.CancellationToken]::None, [Action]{ }).GetAwaiter().GetResult()
    try {
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $false, 1024, $true)
        $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false), 1024, $true)
        $writer.AutoFlush = $true
        $line = $reader.ReadLine()
        if ($line) {
            $request = $line | ConvertFrom-Json
            $writer.WriteLine('{"id":"' + $request.id + '","result":{"type":"pong","version":"0.8.2","protocol":20}}')
        }
        $writer.Dispose(); $reader.Dispose()
    } finally { $stream.Dispose() }
}
""";
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

    private readonly string _scriptPath;
    public Process Process { get; }
    public string? EndpointPath { get; }
    public string Identity => $"{Process.Id}:{Process.StartTime.ToUniversalTime().Ticks}";

    private HerdrTestProcess(Process process, string scriptPath, string? endpointPath = null)
    {
        Process = process;
        _scriptPath = scriptPath;
        EndpointPath = endpointPath;
    }

    public static async Task<HerdrTestProcess> StartAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        return await StartScriptAsync(Script, endpoint, ownedFixture: false, cancellationToken);
    }

    public static async Task<HerdrTestProcess> StartOwnedFixtureAsync(string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        return await StartScriptAsync(OwnedFixtureScript, assemblyPath, ownedFixture: true, cancellationToken);
    }

    private static async Task<HerdrTestProcess> StartScriptAsync(string script, string argument,
        bool ownedFixture, CancellationToken cancellationToken)
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
            if (line != "READY" && !(ownedFixture && line?.StartsWith("READY ", StringComparison.Ordinal) == true))
                throw new IOException($"Herdr peer child did not bind '{argument}': {line}; {await child.StandardError.ReadToEndAsync(startup.Token)}");
            return new HerdrTestProcess(child, scriptPath, ownedFixture ? line![6..] : argument);
        }
        catch
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
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
        Process.Dispose();
        File.Delete(_scriptPath);
    }
}
