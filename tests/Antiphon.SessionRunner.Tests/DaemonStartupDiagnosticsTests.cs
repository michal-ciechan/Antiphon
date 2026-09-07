using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
public class DaemonStartupDiagnosticsTests
{
    [Test, Arguments("pwsh.exe", false, 1), Arguments("powershell.exe", false, 1), Arguments("pwsh.exe", true, 1), Arguments("powershell.exe", true, 1)]
    public async Task Build_failure_retries_without_launching_failed_output(string shell, bool failFirst, int runs)
        => await Exercise(shell, failFirst, runs, true);
    [Test, Arguments("pwsh.exe"), Arguments("powershell.exe")]
    public async Task Every_relaunch_builds_and_gets_a_fresh_attempt(string shell) => await Exercise(shell, false, 2, true);
    [Test, Arguments("pwsh.exe"), Arguments("powershell.exe")]
    public async Task Nonrunner_without_build_keeps_its_launch_behavior(string shell) => await Exercise(shell, false, 1, false);

    private async Task Exercise(string shell, bool failFirst, int runs, bool build)
    {
        using var f = new RestartFixture();
        var state = Path.Combine(f.Root, "svc.state"); var log = Path.Combine(f.Root, "svc.log"); var service = Path.Combine(f.Root, "svc.cmd");
        File.WriteAllText(state, "running");
        File.WriteAllText(service, $"@echo off\r\necho %ANTIPHON_STARTUP_ATTEMPT%,%ANTIPHON_STARTUP_SUPERVISOR_PID%,%ANTIPHON_STARTUP_SUPERVISOR_START%>> \"{f.Root}\\children.txt\"\r\n" +
            (runs == 2 ? $"if exist \"{f.Root}\\once\" (echo stopped> \"{state}\") else (echo once> \"{f.Root}\\once\")\r\n" : $"echo stopped> \"{state}\"\r\n"));
        var daemon = RestartFixture.Quote(Path.Combine(RestartFixture.Repo, "scripts", "run-daemon.ps1"));
        var script = $$"""
            $global:buildCalls=0
            $env:ANTIPHON_STARTUP_ATTEMPT='driver-sentinel'
            function dotnet {
                $global:buildCalls++
                [IO.File]::AppendAllText('{{RestartFixture.Quote(Path.Combine(f.Root, "builds.txt"))}}', "build`n")
                'synthetic build output'
                $global:LASTEXITCODE=0
                if ({{(failFirst ? "$true" : "$false")}} -and $global:buildCalls -eq 1) { $global:LASTEXITCODE=17 }
            }
            & '{{daemon}}' -Name '{{(build ? "session-runner" : "generic")}}' -WorkDir '{{RestartFixture.Quote(f.Root)}}' -Exe '{{RestartFixture.Quote(service)}}' -LogFile '{{RestartFixture.Quote(log)}}' -ServicePidFile '{{RestartFixture.Quote(Path.Combine(f.Root, "svc.pid"))}}' -StateFile '{{RestartFixture.Quote(state)}}' {{(build ? "-BuildProjectDir 'fixture-only'" : "")}}
            if($env:ANTIPHON_STARTUP_ATTEMPT -ne 'driver-sentinel') { throw 'parent environment changed' }
            """;
        var r = await f.Script(script, shell); r.Exit.ShouldBe(0, r.Output);
        var artifact = Path.Combine(RestartFixture.Repo, ".antiphon", "c420-evidence", "daemon-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(artifact);
        File.Copy(log, Path.Combine(artifact, "supervisor.log"));
        File.WriteAllText(Path.Combine(artifact, "driver-output.txt"), r.Output);
        var lines = File.ReadAllLines(log);
        var records = lines.Where(l => l.Contains("ANTIPHON_STARTUP ")).Select(l => JsonDocument.Parse(l[(l.IndexOf("ANTIPHON_STARTUP ", StringComparison.Ordinal) + 17)..]).RootElement.Clone()).ToArray();
        var children = File.ReadAllLines(Path.Combine(f.Root, "children.txt")); children.Length.ShouldBe(runs);
        var builds = File.Exists(Path.Combine(f.Root, "builds.txt")) ? File.ReadAllLines(Path.Combine(f.Root, "builds.txt")).Length : 0;
        builds.ShouldBe(build ? runs + (failFirst ? 1 : 0) : 0, "every relaunch must build; " + r.Output);
        var launches = records.Where(x => x.GetProperty("event").GetString() == "wrapper-started").ToArray(); launches.Length.ShouldBe(runs);
        await f.DecodeCapturedMilestones(string.Join('\n', lines) + "\n", launches[0].GetProperty("producerPid").GetInt32(), launches[0].GetProperty("producerStartTimeUtc").GetString()!, "supervisor", "supervisor-stopping");
        launches.Select(x => x.GetProperty("attemptId").GetString()).Distinct().Count().ShouldBe(runs);
        foreach (var launch in launches)
        {
            launch.GetProperty("version").GetInt32().ShouldBe(1); launch.GetProperty("wrapperPid").GetInt32().ShouldBeGreaterThan(0);
            launch.GetProperty("wrapperPid").GetInt32().ShouldNotBe(launch.GetProperty("producerPid").GetInt32());
            if (build) children.ShouldContain($"{launch.GetProperty("attemptId").GetString()},{launch.GetProperty("producerPid").GetInt32()},{launch.GetProperty("producerStartTimeUtc").GetString()}");
        }
        if (failFirst)
        {
            records.First(x => x.GetProperty("event").GetString() == "build-complete").GetProperty("exitCode").GetInt32().ShouldBe(17);
            records.First(x => x.GetProperty("event").GetString() == "retry-scheduled").GetProperty("elapsedMs").GetDouble().ShouldBe(5000);
        }
        if (runs == 2) records.Single(x => x.GetProperty("event").GetString() == "retry-scheduled").GetProperty("elapsedMs").GetDouble().ShouldBe(3000);
        File.WriteAllLines(Path.Combine(artifact, "children.txt"), children);
    }
}
