using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
public class RunnerRestartScriptCompatibilityTests
{
    [Test, Arguments("pwsh.exe"), Arguments("powershell.exe")]
    public async Task Touched_scripts_parse_as_ascii_on_both_shells(string shell)
    {
        using var f = new RestartFixture();
        foreach (var file in new[] { "restart-session-runner.ps1", "session-runner-restart-health.ps1", "run-daemon.ps1" })
        {
            var path = Path.Combine(RestartFixture.Repo, "scripts", file); File.ReadAllBytes(path).All(b => b <= 127).ShouldBeTrue(file);
            var r = await f.Script($"$e=$null; $t=$null; $null=[System.Management.Automation.Language.Parser]::ParseFile('{RestartFixture.Quote(path)}',[ref]$t,[ref]$e); if($e.Count) {{ $e; exit 1 }}", shell);
            r.Exit.ShouldBe(0, r.Output);
        }
        var help = File.ReadAllText(Path.Combine(RestartFixture.Repo, "scripts", "restart-session-runner.ps1")); help.ShouldContain("default 180"); help.ShouldContain("-WaitOnly -TimeoutSec 180");
    }
    [Test, Arguments("pwsh.exe"), Arguments("powershell.exe")]
    public async Task Documented_arguments_execute_the_expected_mode(string shell)
    {
        using var f = new RestartFixture();
        (await f.Run(shell)).Outcome.ShouldBe("healthy");
        f.Config["forbid"] = true;
        (await f.Run(shell, "-WaitOnly")).Json.GetProperty("mode").GetString().ShouldBe("wait-only");
        f.Config["healthyAt"] = 10000;
        (await f.Run(shell, "-WaitOnly", "-TimeoutSec", "1")).Exit.ShouldBe(2);
        (await f.Run(shell, "-WaitOnly", "-Hard")).Exit.ShouldBe(1);
    }
    [Test, Arguments("pwsh.exe", true), Arguments("powershell.exe", true), Arguments("pwsh.exe", false), Arguments("powershell.exe", false)]
    public async Task Hung_http_response_is_canceled_at_wait_deadline(string shell, bool hang)
    {
        using var f = new RestartFixture(); using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var clients = new List<TcpClient>(); var requests = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(cts.Token); lock (clients) clients.Add(client);
                    var buffer = new byte[4096]; var read = await client.GetStream().ReadAsync(buffer, cts.Token);
                    if (read == 0) continue;
                    Interlocked.Increment(ref requests);
                    accepted.TrySetResult();
                    if (!hang) await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cts.Token);
                }
            }
            catch (Exception) when (cts.IsCancellationRequested) { }
        });
        try
        {
            var helper = RestartFixture.Quote(Path.Combine(RestartFixture.Repo, "scripts", "session-runner-restart-health.ps1"));
            var script = $$"""
                $ErrorActionPreference='Stop'
                . '{{helper}}'
                $p=New-RunnerRestartPlatform -Root $PSScriptRoot
                $p.Probe={ param($ms) Invoke-RunnerHealthHttp 'http://127.0.0.1:{{port}}/health' $ms }
                $p.Census={ @{supervisor=$null;wrapper=$null} }
                $p.Identity={ Get-RunnerProcessIdentity ([AntiphonRestartTcpOwner]::Find({{port}})) }
                $p.Verify={ param($i) $i.pid -eq {{Environment.ProcessId}} }
                $p.Control={ throw 'forbidden fixture mutation' }
                $r=Invoke-RunnerRestart -Platform $p -WaitOnly -TimeoutSec 2
                $r | ConvertTo-Json -Compress
                """;
            var result = await f.Script(script, shell); result.Exit.ShouldBe(0, result.Output); accepted.Task.IsCompletedSuccessfully.ShouldBeTrue("hung endpoint accepted actual health request");
            var json = JsonDocument.Parse(result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Last()).RootElement;
            var evidence = Path.Combine(RestartFixture.Repo, ".antiphon", "c420-evidence", "http-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { shell, hang, port, result = json }));
            json.GetProperty("outcome").GetString().ShouldBe(hang ? "wait-expired" : "healthy");
            if (hang) { var elapsed = json.GetProperty("waitElapsedMs").GetDouble(); elapsed.ShouldBeGreaterThanOrEqualTo(2000); elapsed.ShouldBeLessThanOrEqualTo(3000); }
            using var after = new TcpClient(); await after.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            var before = Volatile.Read(ref requests);
            await after.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n"), cts.Token);
            while (Volatile.Read(ref requests) <= before) await Task.Delay(10, cts.Token);
        }
        finally { cts.Cancel(); listener.Stop(); await server; lock (clients) foreach (var client in clients) client.Dispose(); }
    }
}
