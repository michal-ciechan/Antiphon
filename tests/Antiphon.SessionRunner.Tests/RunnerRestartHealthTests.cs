using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public partial class RunnerRestartHealthTests
{
    [Test]
    public async Task Default_budget_accepts_100_second_startup()
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = 100000;
        f.Config["records"] = new[] { Record("build-start", 0), Record("launch-requested", 52000), Record("adoption-sweep-start", 54000), Record("adoption-sweep-end", 98000) };
        var r = await f.Run(); r.Outcome.ShouldBe("healthy"); r.Wait.ShouldBe(100000);
        r.Json.GetProperty("timeoutSec").GetInt32().ShouldBe(180);
        r.Json.GetProperty("firstHealth200ObservedAtUtc").GetString().ShouldBe("2026-09-07T10:01:40.0000000Z");
    }
    [Test]
    public async Task Explicit_60_second_wait_can_be_continued_without_restart()
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = 100000;
        var expired = await f.Run("pwsh.exe", "-TimeoutSec", "60"); expired.Outcome.ShouldBe("wait-expired"); expired.Exit.ShouldBe(2); expired.Wait.ShouldBe(60000);
        expired.Json.GetProperty("firstHealth200ObservedAtUtc").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        f.Config["startMs"] = 60000; f.Config["forbid"] = true;
        var continued = await f.Run("pwsh.exe", "-WaitOnly"); continued.Outcome.ShouldBe("healthy"); continued.Wait.ShouldBe(40000);
        continued.Mutations.ShouldBe(expired.Mutations);
    }
    [Test]
    [Arguments(0)] [Arguments(3600000)] [Arguments(-3600000)]
    public async Task Wall_clock_jumps_do_not_change_wait_budget(int jump)
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = 999999; f.Config["utcJump"] = jump; f.Config["actionMs"] = 7000;
        var r = await f.Run("pwsh.exe", "-TimeoutSec", "180"); r.Wait.ShouldBe(180000); r.Outcome.ShouldBe("wait-expired");
        r.Json.GetProperty("commandElapsedMs").GetDouble().ShouldBe(187000);
        r.Trace.Count(t => t.GetProperty("op").GetString() == "probe").ShouldBe(90);
    }
    [Test]
    [Arguments(1)] [Arguments(750)]
    public async Task Probe_and_sleep_are_clamped_to_remaining_budget(int remaining)
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = 999999; f.Config["firstProbeMs"] = 5000 - 2000 - remaining;
        var r = await f.Run("pwsh.exe", "-WaitOnly", "-TimeoutSec", "5"); r.Wait.ShouldBe(5000);
        var probes = r.Trace.Where(t => t.GetProperty("op").GetString() == "probe").ToArray();
        probes.Length.ShouldBe(2); probes[1].GetProperty("value").GetDouble().ShouldBe(remaining);
        r.Trace.Last().GetProperty("value").GetDouble().ShouldBe(remaining);
    }
    [Test, Arguments(1001, 0), Arguments(0, 1001), Arguments(500, 501)]
    public async Task Late_200_is_not_in_budget_success(int probeMs, int verifyMs)
    {
        using var f = new RestartFixture(); f.Config["firstProbeMs"] = probeMs; f.Config["verifyMs"] = verifyMs;
        var r = await f.Run("pwsh.exe", "-WaitOnly", "-TimeoutSec", "1"); r.Outcome.ShouldBe("wait-expired"); r.Exit.ShouldBe(2);
        r.Json.GetProperty("firstHealth200ObservedAtUtc").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        r.Json.GetProperty("lastProbeResult").GetString().ShouldBe("http-200-late");
        r.Json.GetProperty("runner").GetProperty("pid").GetInt32().ShouldBe(900001);
        r.Output.ShouldContain("http-200-late");
    }
    [Test, Arguments(200, false), Arguments(503, false), Arguments(200, true), Arguments(503, true)]
    public async Task Foreign_listener_expiry_names_actual_path_even_with_Hard(int status, bool hard)
    {
        using var f = new RestartFixture(); f.Config["foreign"] = true; f.Config["status"] = status;
        var r = await f.Run("pwsh.exe", hard ? "-Hard" : "-WaitOnly", "-TimeoutSec", "1");
        r.Outcome.ShouldBe("wait-expired"); r.Exit.ShouldBe(2);
        r.Json.GetProperty("portOwner").GetProperty("pid").GetInt32().ShouldBe(900003);
        r.Json.GetProperty("portOwner").GetProperty("path").GetString().ShouldBe(@"C:\fixture\other-build\Antiphon.SessionRunner.exe");
        r.Output.ShouldContain(@"PID 900003; path: C:\fixture\other-build\Antiphon.SessionRunner.exe");
        r.Output.ShouldContain("not stopped, even with -Hard");
        if (status == 200) r.Json.GetProperty("lastProbeResult").GetString().ShouldBe("http-200-identity-unconfirmed");
        r.Trace.SkipWhile(t => t.GetProperty("op").GetString() != "probe")
            .All(t => t.GetProperty("op").GetString() is "probe" or "sleep").ShouldBeTrue();
    }
    [Test, Arguments("pwsh.exe", "probe"), Arguments("pwsh.exe", "clock"), Arguments("pwsh.exe", "utc"),
        Arguments("powershell.exe", "probe"), Arguments("powershell.exe", "clock"), Arguments("powershell.exe", "utc")]
    public async Task Wait_exception_still_emits_one_final_result_without_more_controls(string shell, string operation)
    {
        using var f = new RestartFixture(); f.Config["failWaitOperation"] = operation; f.Config["firstProbeMs"] = 123;
        var r = await f.Run(shell, "-WaitOnly"); // Fixture requires exactly one final JSON line, last in output.
        r.Exit.ShouldBe(1); r.Outcome.ShouldBe("wait-failed"); r.Mutations.ShouldBeEmpty();
        r.Json.GetProperty("operation").GetString().ShouldBe("health-wait");
        r.Json.GetProperty("error").GetString().ShouldBe("injected wait failure: " + operation);
        r.Output.ShouldContain("Background startup has not been stopped");
        if (operation == "clock")
        {
            r.Json.GetProperty("waitElapsedMs").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
            r.Json.GetProperty("commandElapsedMs").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        }
        else r.Wait.ShouldBe(123);
        if (operation == "utc") r.Json.GetProperty("observedAtUtc").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }
    [Test]
    public async Task Immediate_200_needs_no_initial_sleep()
    {
        using var f = new RestartFixture(); var r = await f.Run("pwsh.exe", "-WaitOnly"); r.Outcome.ShouldBe("healthy"); r.Wait.ShouldBe(0);
        r.Trace.Length.ShouldBe(1); r.Trace[0].GetProperty("op").GetString().ShouldBe("probe");
    }
    [Test]
    [Arguments(0, false, false)] [Arguments(204, false, false)] [Arguments(302, false, false)]
    [Arguments(404, false, false)] [Arguments(503, false, false)] [Arguments(200, true, false)] [Arguments(200, false, true)] [Arguments(200, false, false)]
    public async Task Health_requires_200_from_the_expected_runner(int status, bool foreign, bool reused)
    {
        using var f = new RestartFixture(); f.Config["status"] = status; f.Config["foreign"] = foreign; f.Config["reused"] = reused;
        f.Config["records"] = new[] { Record("build-start", 0) };
        var r = await f.Run("pwsh.exe", "-WaitOnly", "-TimeoutSec", "1");
        r.Outcome.ShouldBe(status == 200 && !foreign && !reused ? "healthy" : "wait-expired");
    }
    [Test]
    [Arguments(0, false)] [Arguments(10000, false)] [Arguments(999999, false)] [Arguments(0, true)] [Arguments(999999, true)]
    public async Task WaitOnly_never_changes_daemon_control_state(int healthyAt, bool missing)
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = healthyAt; f.Config["noSupervisor"] = missing; f.Config["forbid"] = true;
        var r = await f.Run("pwsh.exe", "-WaitOnly", "-TimeoutSec", "12"); r.Mutations.ShouldBeEmpty();
        r.Outcome.ShouldBe(healthyAt < 12000 ? "healthy" : "wait-expired");
        File.ReadAllText(Path.Combine(f.Root, "state")).ShouldBe("stopped-sentinel"); File.ReadAllText(Path.Combine(f.Root, "pid")).ShouldBe("pid-sentinel");
    }
    [Test]
    [Arguments("-Hard")] [Arguments("-KillSessions")] [Arguments("0")] [Arguments("-1")]
    public async Task Invalid_arguments_fail_before_any_control_operation(string arg)
    {
        using var f = new RestartFixture(); f.Config["forbid"] = true;
        var r = int.TryParse(arg, out _) ? await f.Run("pwsh.exe", "-TimeoutSec", arg) : await f.Run("pwsh.exe", "-WaitOnly", arg);
        r.Exit.ShouldBe(1); r.Outcome.ShouldBe("action-failed"); r.Mutations.ShouldBeEmpty(); r.Wait.ShouldBe(0);
    }
    [Test]
    [Arguments("soft")] [Arguments("hard")] [Arguments("wait")]
    public async Task Expiry_has_no_restart_or_stop_side_effects(string mode)
    {
        using var f = new RestartFixture(); f.Config["healthyAt"] = 999999;
        var args = new List<string> { "-TimeoutSec", "1" }; if (mode == "hard") args.Add("-Hard"); if (mode == "wait") args.Add("-WaitOnly");
        var r = await f.Run("pwsh.exe", args.ToArray()); r.Outcome.ShouldBe("wait-expired");
        r.Trace.SkipWhile(t => t.GetProperty("op").GetString() != "probe").All(t => t.GetProperty("op").GetString() is "probe" or "sleep").ShouldBeTrue("no mutation after wait starts");
        r.Output.ShouldContain("-WaitOnly -TimeoutSec 180"); r.Output.ShouldContain("has not stopped background startup");
    }
    [Test]
    [Arguments("stop-service")] [Arguments("start-task")]
    public async Task Action_errors_report_the_failing_operation(string op)
    {
        using var f = new RestartFixture(); f.Config["failOperation"] = op; f.Config["noSupervisor"] = true;
        var r = await f.Run(); r.Exit.ShouldBe(1); r.Outcome.ShouldBe("action-failed"); r.Wait.ShouldBe(0);
        r.Json.GetProperty("operation").GetString().ShouldBe(op); r.Output.ShouldContain("injected operation failure: " + op);
        r.Trace.Last().GetProperty("op").GetString().ShouldBe(op);
    }
}
