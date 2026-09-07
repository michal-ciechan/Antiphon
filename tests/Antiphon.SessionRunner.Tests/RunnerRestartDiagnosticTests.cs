using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public partial class RunnerRestartHealthTests
{
    private object Record(string name, int at, string attempt = "attempt-one", int code = 0) => new
    {
        atMs = at,
        record = new { version = 1, @event = name, utc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc).AddMilliseconds(at).ToString("O"),
            producer = "supervisor", producerPid = 900002, producerStartTimeUtc = "2026-09-07T09:58:00.0000000Z", attemptId = attempt, exitCode = code }
    };
    [Test, Arguments(false, "build-complete"), Arguments(true, "build-complete"),
        Arguments(false, "launch-error"), Arguments(true, "launch-error"),
        Arguments(false, "service-exit"), Arguments(true, "service-exit")]
    public async Task Retrying_startup_can_recover_or_expire(bool recover, string failedEvent)
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = recover ? 10000 : 999999;
        f.Config["records"] = new[] { Record("build-start", 0), Record(failedEvent, 2000, code: 17), Record("retry-scheduled", 4000, code: 17) };
        var r = await f.Run("pwsh.exe", "-TimeoutSec", "12"); r.Outcome.ShouldBe(recover ? "healthy" : "wait-expired");
        r.Json.GetProperty("lastObservedPhase").GetString().ShouldBe("retry-scheduled"); r.Json.GetProperty("errorCode").GetInt32().ShouldBe(17);
    }
    [Test]
    public async Task Progress_output_is_throttled_without_changing_deadline()
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = 999999;
        f.Config["records"] = new[] { Record("build-start", 0), Record("launch-requested", 52000), Record("adoption-sweep-start", 54000) };
        var r = await f.Run("pwsh.exe", "-TimeoutSec", "180"); r.Wait.ShouldBe(180000);
        r.Json.GetProperty("lastObservedPhase").GetString().ShouldBe("adoption-sweep-start");
        r.Json.GetProperty("phaseAgeMs").GetDouble().ShouldBe(126000);
        r.Output.Split('\n').Count(l => l.StartsWith("Continuing health wait")).ShouldBeLessThanOrEqualTo(12);
    }
    [Test]
    public async Task Final_result_matches_exit_and_first_observation()
    {
        using var f = new RestartFixture(); var r = await f.Run("pwsh.exe", "-WaitOnly");
        foreach (var key in new[] { "outcome", "mode", "waitElapsedMs", "commandElapsedMs", "timeoutSec", "observedAtUtc", "lastObservedPhase", "attemptId", "runner", "supervisor", "wrapper" })
            r.Json.TryGetProperty(key, out _).ShouldBeTrue(key);
        r.Json.GetProperty("commandElapsedMs").GetDouble().ShouldBeGreaterThanOrEqualTo(r.Wait);
        r.Json.GetProperty("observedAtUtc").GetString()!.ShouldMatch(@"\.\d{7}Z$");
        r.Output.ShouldNotContain("prompt-sentinel"); r.Output.ShouldNotContain("rules-sentinel");
    }
    [Test, Arguments("pwsh.exe"), Arguments("powershell.exe")]
    public async Task Only_current_attempt_and_process_records_define_phase(string shell)
    {
        using var f = new RestartFixture();
        await f.Run(shell, "-WaitOnly"); // writes config; no production operation
        var script = ParserPrelude(f) + """
            $r=New-RunnerMilestoneReader $p.LogPath
            function append($event, $attempt='current', $start='2026-09-07T09:58:00.0000000Z', $producerId=900002, $version=1) {
                $record=@{version=$version;event=$event;utc='2026-09-07T10:00:00.0000000Z';producer='supervisor';producerPid=$producerId;producerStartTimeUtc=$start;attemptId=$attempt}
                [IO.File]::AppendAllText($p.LogPath, ('ANTIPHON_STARTUP '+($record|ConvertTo-Json -Compress)+"`n"))
                Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            }
            append 'build-start' 'stale' '2026-09-07T09:57:00.0000000Z'
            if($r.Phase) { throw 'stale process supplied a phase' }
            append 'build-start' 'foreign' '2026-09-07T09:58:00.0000000Z' 900003
            if($r.Phase) { throw 'foreign producer supplied a phase' }
            append 'build-start'
            if($r.Phase.event -ne 'build-start') { throw 'current phase missing' }
            append 'adoption-sweep-start' 'stale-attempt'
            if($r.Phase.event -ne 'build-start') { throw 'stale attempt overwrote phase' }
            append 'build-start' 'stale' '2026-09-07T09:57:00.0000000Z'
            if($r.Phase.event -ne 'build-start') { throw 'stale process overwrote phase' }
            append 'retry-scheduled'
            if($r.Phase.event -ne 'retry-scheduled') { throw 'current retry missing' }
            append 'build-start' 'next'
            if($r.Attempt -ne 'next') { throw 'new attempt missing' }
            append 'build-start' 'current'
            if($r.Attempt -ne 'next') { throw 'retired attempt returned' }
            append 'legacy' 'next' '2026-09-07T09:58:00.0000000Z' 900002 99
            if($r.Phase) { throw 'unknown schema supplied a phase' }
            """;
        var run = await f.Script(script, shell); run.Exit.ShouldBe(0, run.Output);
    }
    [Test, Arguments("pwsh.exe"), Arguments("powershell.exe")]
    public async Task Diagnostic_reader_survives_rotation_gaps_and_partial_records(string shell)
    {
        using var f = new RestartFixture(); await f.Run(shell, "-WaitOnly");
        var script = ParserPrelude(f) + """
            $r=New-RunnerMilestoneReader $p.LogPath
            $line='ANTIPHON_STARTUP '+(@{version=1;event='build-start';utc='2026-09-07T10:00:00Z';producer='supervisor';producerPid=900002;producerStartTimeUtc='2026-09-07T09:58:00.0000000Z';attemptId='current'}|ConvertTo-Json -Compress)
            [IO.File]::WriteAllText($p.LogPath,$line.Substring(0,40))
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase) { throw 'partial record admitted' }
            [IO.File]::AppendAllText($p.LogPath,$line.Substring(40)+"`n")
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase.event -ne 'build-start') { throw 'completed record lost' }
            [IO.File]::AppendAllText($p.LogPath,"ANTIPHON_STARTUP {bad`n")
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase) { throw 'malformed record retained phase' }
            [IO.File]::WriteAllText($p.LogPath,'truncated')
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase) { throw 'truncation retained phase' }
            [IO.File]::AppendAllText($p.LogPath,('x'*300000)+"`n")
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase) { throw 'gap retained phase' }
            [IO.File]::AppendAllText($p.LogPath,$line+"`n")
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase.event -ne 'build-start') { throw 'did not recover after gap' }
            [IO.File]::Move($p.LogPath,$p.LogPath+'.old')
            [IO.File]::WriteAllText($p.LogPath,$line+"`n")
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase.event -ne 'build-start') { throw 'replacement did not recover' }
            [IO.File]::Delete($p.LogPath)
            Read-RunnerMilestones $r $p '2026-09-07T09:59:59Z'
            if($r.Phase) { throw 'missing log retained phase' }
            """;
        var run = await f.Script(script, shell); run.Exit.ShouldBe(0, run.Output);
    }
    private string ParserPrelude(RestartFixture f) => $"$ErrorActionPreference='Stop'\n. '{RestartFixture.Quote(Path.Combine(f.Root, "scripts", "session-runner-restart-health.ps1"))}'\n$p=New-RunnerRestartPlatform -Root $PSScriptRoot\n";
}
