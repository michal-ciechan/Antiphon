using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1305 D-2. The dev-stack listener probe must list a real loopback port
/// on a host where Get-NetTCPConnection is not a command.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerifyDevStackSmokeTests
{
    [Test]
    public async Task Linux_full_smoke_refuses_before_desktop_side_effects()
    {
        var root = Directory.CreateTempSubdirectory("c1323-desktop-smoke-").FullName;
        try
        {
            var wrapper = Path.Combine(root, "probe.ps1");
            var sentinelLog = Path.Combine(root, "sentinels.txt");
            await File.WriteAllTextAsync(sentinelLog, "");
            await File.WriteAllTextAsync(wrapper, """
                param([string]$TargetScript, [string]$SentinelLog)
                $ErrorActionPreference = 'Stop'
                function Stop-ExternalCommand([string]$Name) {
                    [System.IO.File]::AppendAllText($SentinelLog, "$Name`n")
                    throw "external-command-sentinel:$Name"
                }
                function Start-Job { Stop-ExternalCommand 'Start-Job' }
                function docker { Stop-ExternalCommand 'docker' }
                function Invoke-WebRequest { Stop-ExternalCommand 'Invoke-WebRequest' }
                function Invoke-RestMethod { Stop-ExternalCommand 'Invoke-RestMethod' }
                function ss { Stop-ExternalCommand 'ss' }
                function dotnet { Stop-ExternalCommand 'dotnet' }
                try {
                    & $TargetScript
                } catch {
                    [Console]::Error.WriteLine($_.Exception.Message)
                    exit 23
                }
                exit 0
                """);

            var start = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
            };
            foreach (var arg in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File", wrapper,
                         "-TargetScript", Path.Combine(DelegateScriptRunner.RepoRoot, "verify-dev-stack.ps1"),
                         "-SentinelLog", sentinelLog,
                     })
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanupDeadline.Token);
                throw;
            }

            var output = await stdout + await stderr;
            process.ExitCode.ShouldNotBe(0, output);
            var sentinelCalls = await File.ReadAllTextAsync(sentinelLog);
            if (OperatingSystem.IsWindows())
            {
                output.ShouldContain("external-command-sentinel:Start-Job");
                sentinelCalls.ShouldBe("Start-Job\n", "Windows must retain full Aspire smoke admission.");
            }
            else
            {
                output.ShouldContain("desktop-platform-required");
                sentinelCalls.ShouldBeEmpty(
                    "Full Aspire smoke must refuse before jobs, Docker, HTTP, listener or build commands.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Linux_listener_probe_lists_ports_without_GetNetTCPConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port.ToString();
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "verify-dev-stack.ps1");

        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-ProbeListeningPorts" })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        var error = await stderr;
        process.ExitCode.ShouldBe(0, error);
        var lines = (await stdout).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.ShouldContain(port);
    }
}
