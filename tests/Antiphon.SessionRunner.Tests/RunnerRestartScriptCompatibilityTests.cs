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
        foreach (var file in new[] { "restart-session-runner.ps1", "session-runner-restart-health.ps1", "run-daemon.ps1", "nightly-health.ps1", "lib/nightly-health.ps1", "lib/nightly-common.ps1" })
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
        if (!hang && !OperatingSystem.IsWindows())
            Skip.Test("CARD-1669: a 200 reaches Identity, and AntiphonRestartTcpOwner.Find reads the iphlpapi.dll TCP owner table, which exists only on Windows");
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

    [Test]
    [Arguments("pwsh.exe", false), Arguments("powershell.exe", false)]
    [Arguments("pwsh.exe", true), Arguments("powershell.exe", true)]
    public async Task Missing_logs_is_an_absent_pid_without_creating_the_directory(string shell, bool callOperator)
    {
        using var f = new RestartFixture();
        var helper = RestartFixture.Quote(Path.Combine(RestartFixture.Repo, "scripts", "session-runner-restart-health.ps1"));
        var entry = $$"""
            $ErrorActionPreference='Stop'
            . '{{helper}}'
            $logDir=Join-Path $PSScriptRoot 'logs'
            if (Test-Path -LiteralPath $logDir) { throw 'fixture must start without logs' }
            $p=New-RunnerRestartPlatform -Root $PSScriptRoot
            $census=& $p.Census
            if ($null -ne $census.supervisor -or $null -ne $census.wrapper) { throw 'missing logs must mean absent PIDs' }
            if (Test-Path -LiteralPath $logDir) { throw 'PID census created logs' }
            $controls=New-Object 'System.Collections.Generic.List[string]'
            $p.Control={ param($op,$identity) $controls.Add($op) }.GetNewClosure()
            $p.Probe={ param($ms) @{ status=200; result='fixture-healthy' } }
            $p.Identity={ @{ pid=$PID } }
            $p.Verify={ param($identity) $true }
            $r=Invoke-RunnerRestart -Platform $p -TimeoutSec 1
            @{ result=$r; controls=@($controls.ToArray()); logsExists=(Test-Path -LiteralPath $logDir) } | ConvertTo-Json -Depth 8 -Compress
            """;
        if (callOperator)
            File.WriteAllText(Path.Combine(f.Root, "missing-logs-entry.ps1"), entry);
        var run = await f.Script(callOperator ? "& (Join-Path $PSScriptRoot 'missing-logs-entry.ps1')" : entry, shell);
        run.Exit.ShouldBe(0, run.Output);
        var json = JsonDocument.Parse(run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last()).RootElement;
        json.GetProperty("result").GetProperty("outcome").GetString().ShouldBe("healthy", run.Output);
        json.GetProperty("controls").EnumerateArray().Select(x => x.GetString()).ToArray()
            .ShouldBe(["write-state", "stop-service", "delete-pids", "start-task"], run.Output);
        json.GetProperty("logsExists").GetBoolean().ShouldBeFalse(run.Output);
    }

    [Test]
    [Arguments("pwsh.exe", "directory"), Arguments("powershell.exe", "directory")]
    [Arguments("pwsh.exe", "locked"), Arguments("powershell.exe", "locked")]
    [Arguments("pwsh.exe", "invalid"), Arguments("powershell.exe", "invalid")]
    [Arguments("pwsh.exe", "overflow"), Arguments("powershell.exe", "overflow")]
    [Arguments("pwsh.exe", "zero"), Arguments("powershell.exe", "zero")]
    [Arguments("pwsh.exe", "wrapper-directory"), Arguments("powershell.exe", "wrapper-directory")]
    [Arguments("pwsh.exe", "hard-directory"), Arguments("powershell.exe", "hard-directory")]
    [Arguments("pwsh.exe", "hard-invalid"), Arguments("powershell.exe", "hard-invalid")]
    [Arguments("pwsh.exe", "wait-directory"), Arguments("powershell.exe", "wait-directory")]
    [Arguments("pwsh.exe", "wait-invalid"), Arguments("powershell.exe", "wait-invalid")]
    [Arguments("pwsh.exe", "missing"), Arguments("powershell.exe", "missing")]
    [Arguments("pwsh.exe", "live"), Arguments("powershell.exe", "live")]
    public async Task Pid_read_errors_stop_restart_before_any_control(string shell, string state)
    {
        using var f = new RestartFixture();
        var helper = RestartFixture.Quote(Path.Combine(RestartFixture.Repo, "scripts", "session-runner-restart-health.ps1"));
        File.WriteAllText(Path.Combine(f.Root, "pid-entry.ps1"), $$"""
            $ErrorActionPreference='Stop'
            . '{{helper}}'
            $logDir=Join-Path $PSScriptRoot 'logs'
            $null=New-Item -ItemType Directory -Path $logDir -Force
            $pidPath=Join-Path $logDir 'session-runner.supervisor.pid'
            $lock=$null
            switch ('{{(state.StartsWith("hard-") || state.StartsWith("wait-") ? state.Split('-').Last() : state)}}') {
                'directory' { $null=New-Item -ItemType Directory -Path $pidPath }
                'locked' { [IO.File]::WriteAllText($pidPath, [string]$PID); $lock=[IO.File]::Open($pidPath, 'Open', 'ReadWrite', 'None') }
                'invalid' { [IO.File]::WriteAllText($pidPath, 'not-a-pid') }
                'overflow' { [IO.File]::WriteAllText($pidPath, '999999999999999999999') }
                'zero' { [IO.File]::WriteAllText($pidPath, '0') }
                'wrapper-directory' {
                    [IO.File]::WriteAllText($pidPath, [string]$PID)
                    $pidPath=Join-Path $logDir 'session-runner.service.pid'
                    $null=New-Item -ItemType Directory -Path $pidPath
                }
                'live' { [IO.File]::WriteAllText($pidPath, [string]$PID) }
            }
            try {
                $p=New-RunnerRestartPlatform -Root $PSScriptRoot
                $controls=New-Object 'System.Collections.Generic.List[string]'
                $p.Control={ param($op,$identity) $controls.Add($op) }.GetNewClosure()
                $p.Probe={ param($ms) @{ status=200; result='fixture-healthy' } }
                $p.Identity={ @{ pid=$PID } }
                $p.Verify={ param($identity) $true }
                $r=Invoke-RunnerRestart -Platform $p -TimeoutSec 1 {{(state.StartsWith("hard-") ? "-Hard" : state.StartsWith("wait-") ? "-WaitOnly" : "")}}
                @{ result=$r; controls=@($controls.ToArray()); pidExists=(Test-Path -LiteralPath $pidPath); helperGlobal=$false }
            } finally { if ($lock) { $lock.Dispose() } }
            """);
        var run = await f.Script("""
            $ErrorActionPreference='Stop'
            $r=& (Join-Path $PSScriptRoot 'pid-entry.ps1')
            $r.helperGlobal=[bool](Get-Command Get-RunnerProcessIdentity -ErrorAction SilentlyContinue)
            $r | ConvertTo-Json -Depth 8 -Compress
            """, shell);
        run.Exit.ShouldBe(0, run.Output);
        var json = JsonDocument.Parse(run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last()).RootElement;
        json.GetProperty("helperGlobal").GetBoolean().ShouldBeFalse(run.Output);
        var result = json.GetProperty("result");
        var controls = json.GetProperty("controls").EnumerateArray().Select(x => x.GetString()).ToArray();
        if (state is "missing" or "live")
        {
            result.GetProperty("outcome").GetString().ShouldBe("healthy", run.Output);
            controls.ShouldBe(state == "missing" ? ["write-state", "stop-service", "delete-pids", "start-task"] : ["write-state", "stop-service"], run.Output);
            json.GetProperty("pidExists").GetBoolean().ShouldBe(state == "live", run.Output);
        }
        else
        {
            result.GetProperty("outcome").GetString().ShouldBe("action-failed", run.Output);
            result.GetProperty("exitCode").GetInt32().ShouldBe(1, run.Output);
            result.GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace(run.Output);
            controls.ShouldBeEmpty(run.Output);
            json.GetProperty("pidExists").GetBoolean().ShouldBeTrue(run.Output);
        }
    }

    [Test]
    [Arguments("pwsh.exe", "2026-07-01", "2026-06-30T23:30:00.0000000Z")]
    [Arguments("powershell.exe", "2026-07-01", "2026-06-30T23:30:00.0000000Z")]
    [Arguments("pwsh.exe", "2026-12-01", "2026-12-01T00:30:00.0000000Z")]
    [Arguments("powershell.exe", "2026-12-01", "2026-12-01T00:30:00.0000000Z")]
    public async Task Nightly_health_closures_resolve_helpers_under_the_call_operator(string shell, string date, string expectedDue)
    {
        using var f = new RestartFixture();
        var lib = RestartFixture.Quote(Path.Combine(RestartFixture.Repo, "scripts", "lib"));
        File.WriteAllText(Path.Combine(f.Root, "nightly-entry.ps1"), $$"""
            $ErrorActionPreference='Stop'
            . (Join-Path '{{lib}}' 'nightly-common.ps1')
            . (Join-Path '{{lib}}' 'nightly-health.ps1')
            $api=New-NightlyProductionWindmillApi -Config ([pscustomobject]@{
                HasCredentials=$true; BaseUrl='https://nightly.invalid/'; Workspace='fixture'; Token='synthetic'; ScriptPath='u/fixture/nightly'
            })
            @{ registration=(& $api.GetRegistration); schedule=(& $api.GetSchedule); jobs=@(& $api.GetJobs) }
            """);
        var run = await f.Script($$"""
            $ErrorActionPreference='Stop'
            $global:requests=New-Object 'System.Collections.Generic.List[string]'
            function global:Invoke-WebRequest {
                param($Method,$Uri,$Headers,[switch]$UseBasicParsing,$TimeoutSec)
                if ($Method -ne 'GET' -or $Headers.Authorization -ne 'Bearer synthetic' -or $TimeoutSec -ne 30) { throw 'wrong HTTP contract' }
                $global:requests.Add([string]$Uri)
                $data=$null
                switch -Wildcard ([string]$Uri) {
                    '*/scripts/get/p/u/fixture/nightly' { $data=@{ hash='fixture-hash'; tag='fixture-tag'; path='u/fixture/nightly' } }
                    '*/schedules/get/u/fixture/nightly' { $data=@{ enabled=$false; paused=$true; tag='schedule-tag'; args=@{key='value'}; worker_version='worker'; server_version='server' } }
                    '*/jobs/list?*' {
                        $data=@(1..6 | ForEach-Object {
                            @{ id="j$_"; type='CompletedJob'; success=($_ -ne 5); schedule_path='u/fixture/nightly';
                               result=@{ nativeRunId='untrusted-inline'; localDueDate='{{date}}' } }
                        })
                        $data[3].success='true'
                        $data+=@{ id='manual'; type='CompletedJob'; success=$true; result=@{ nativeRunId='untrusted-inline'; localDueDate='{{date}}' } }
                        $data+=@{ id='running'; type='QueuedJob'; running=$true; schedule_path='u/fixture/nightly' }
                    }
                    '*/get_result/j2' { throw 'fixture result unavailable' }
                    '*/get_result/j3' { $data=@{ nativeRunId='r3' }; break }
                    '*/get_result/j*' { $data=@{ nativeRunId='native-run'; sha='fixture-sha'; localDueDate='{{date}}' } }
                    default { throw 'unexpected HTTP route' }
                }
                [pscustomobject]@{ Content=(ConvertTo-Json -InputObject $data -Depth 8 -Compress) }
            }
            $r=& (Join-Path $PSScriptRoot 'nightly-entry.ps1')
            $r.requests=@($global:requests.ToArray())
            $r.globalHelpers=@('Invoke-NightlyWindmillHttp','ConvertFrom-NightlyWindmillJob','Get-NightlyDueUtcForLondonDate','Get-NightlyLondonTimeZone' | ForEach-Object {
                [bool](Get-Command $_ -ErrorAction SilentlyContinue)
            })
            $r | ConvertTo-Json -Depth 10 -Compress
            """, shell);
        run.Exit.ShouldBe(0, run.Output);
        var json = JsonDocument.Parse(run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last()).RootElement;
        json.GetProperty("globalHelpers").EnumerateArray().All(x => !x.GetBoolean()).ShouldBeTrue(run.Output);
        var registration = json.GetProperty("registration");
        registration.GetProperty("hash").GetString().ShouldBe("fixture-hash");
        registration.GetProperty("script").GetBoolean().ShouldBeTrue();
        registration.GetProperty("tag").GetString().ShouldBe("fixture-tag");
        var schedule = json.GetProperty("schedule");
        schedule.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        schedule.GetProperty("paused").GetBoolean().ShouldBeTrue();
        schedule.GetProperty("workerVersion").GetString().ShouldBe("worker");
        schedule.GetProperty("serverVersion").GetString().ShouldBe("server");
        schedule.GetProperty("args").GetProperty("key").GetString().ShouldBe("value");
        var jobs = json.GetProperty("jobs").EnumerateArray().ToArray();
        jobs.Length.ShouldBe(8);
        jobs.Select(x => x.GetProperty("status").GetString()).ShouldBe(["success", "unknown", "unknown", "unknown", "failed", "unknown", "unknown", "running"]);
        jobs[0].GetProperty("nativeRunId").GetString().ShouldBe("native-run");
        jobs[0].GetProperty("sha").GetString().ShouldBe("fixture-sha");
        jobs[0].GetProperty("scheduledFor").GetString().ShouldBe(expectedDue, "due-time helper must resolve transitively and preserve London DST");
        foreach (var index in new[] { 1, 2, 5, 6 })
            jobs[index].GetProperty("localDueDate").GetString().ShouldBeEmpty();
        jobs[6].GetProperty("scheduled").GetBoolean().ShouldBeFalse();
        var requests = json.GetProperty("requests").EnumerateArray().Select(x => x.GetString()).ToArray();
        requests.Length.ShouldBe(8);
        requests[2].ShouldBe("https://nightly.invalid/api/w/fixture/jobs/list?script_path_exact=u%2Ffixture%2Fnightly&per_page=20");
        requests.Skip(3).Select(x => x!.Split('/').Last()).ShouldBe(["j1", "j2", "j3", "j4", "j5"]);
    }

    // Debug 4ca0643a (2026-11-12): the orchestrator ran `pwsh -Command "& '...\restart-session-runner.ps1'"`
    // and stop-service failed with "The term 'Get-RunnerProcessIdentity' is not recognized". Under the call
    // operator the dot-sourced helpers live in the script scope, and a GetNewClosure() block resolves commands
    // from its own dynamic module and the global scope only; `-File` runs the script in the global scope and
    // hid it. This drives the real platform from a call-operator entry. Process listing is a global
    // Get-Process fake naming this child, whose path is never the Debug runner, so nothing is stopped.
    [Test, Arguments("pwsh.exe"), Arguments("powershell.exe")]
    public async Task Real_platform_closures_resolve_helpers_under_the_call_operator(string shell)
    {
        using var f = new RestartFixture();
        var helper = RestartFixture.Quote(Path.Combine(RestartFixture.Repo, "scripts", "session-runner-restart-health.ps1"));
        File.WriteAllText(Path.Combine(f.Root, "call-operator-entry.ps1"), $$"""
            $ErrorActionPreference='Stop'
            . '{{helper}}'
            $p=New-RunnerRestartPlatform -Root $PSScriptRoot
            New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'logs') | Out-Null
            Set-Content -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'logs') 'session-runner.supervisor.pid') -Value $PID
            $r=[ordered]@{ census=$null; stopService=$null; stopSupervisor=$null; verify=$null }
            try { $r.census=(& $p.Census).supervisor.pid } catch { $r.census=$_.Exception.Message }
            try { & $p.Control 'stop-service' $null; $r.stopService='ok' } catch { $r.stopService=$_.Exception.Message }
            try { & $p.Control 'stop-supervisor' @{ pid=$PID; startTimeUtc='2000-01-01T00:00:00.0000000Z'; path='synthetic' }; $r.stopSupervisor='ok' } catch { $r.stopSupervisor=$_.Exception.Message }
            $me=& $p.Process $PID
            $me.path=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\Antiphon.SessionRunner\bin\Debug\net9.0\Antiphon.SessionRunner.exe'))
            try { $r.verify=[bool](& $p.Verify $me) } catch { $r.verify=$_.Exception.Message }
            $r
            """);
        var driver = $$"""
            $ErrorActionPreference='Stop'
            $global:C4caListed=0
            function global:Get-Process {
                [CmdletBinding()] param([Parameter(Position=0)][string[]]$Name, [int[]]$Id)
                if ($PSBoundParameters.ContainsKey('Id')) { return Microsoft.PowerShell.Management\Get-Process -Id $Id -ErrorAction Stop }
                $global:C4caListed++
                if ($Name -contains 'Antiphon.SessionRunner') { return [pscustomobject]@{ Id = $PID } }
            }
            $r=& (Join-Path $PSScriptRoot 'call-operator-entry.ps1')
            $r.listed=$global:C4caListed
            $r.helperGlobal=[bool](Get-Command Get-RunnerProcessIdentity -ErrorAction SilentlyContinue)
            $r.pid=$PID
            $r | ConvertTo-Json -Compress
            """;
        var result = await f.Script(driver, shell);
        result.Exit.ShouldBe(0, result.Output);
        var json = JsonDocument.Parse(result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Last()).RootElement;
        json.GetProperty("helperGlobal").GetBoolean().ShouldBeFalse("the witness must keep the helpers out of the global scope");
        json.GetProperty("stopService").ToString().ShouldBe("ok", result.Output);
        json.GetProperty("listed").GetInt32().ShouldBeGreaterThan(0, "stop-service reached the process listing");
        json.GetProperty("stopSupervisor").ToString().ShouldBe("Process identity changed before stop", result.Output);
        json.GetProperty("census").ToString().ShouldBe(json.GetProperty("pid").ToString(), result.Output);
        json.GetProperty("verify").ToString().ShouldBe("True", result.Output);
    }
}
