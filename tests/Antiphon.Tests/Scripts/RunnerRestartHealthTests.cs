using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0420, CARD-0604: the desktop runner restart and its health wait (Antiphon.Nightly.RunnerRestart, which replaced
/// scripts/restart-session-runner.ps1 and scripts/session-runner-restart-health.ps1 in unit 30 of the 2026-11-22 plan), in
/// process on RunnerRestartWorld's fake platform: the budget, the clamps, the late and foreign 200s, wait failures, the
/// mode's control operations, the result line and the SHA stamp hint. The rows are those of the deleted
/// Antiphon.SessionRunner.Tests classes RunnerRestartHealthTests (with its RunnerRestartDiagnosticTests partial) and
/// RunnerRestartScriptCompatibilityTests, which ran the scripts under pwsh and powershell.exe behind an AST preflight; the
/// shell argument is gone with the scripts. Nothing leaves the process.
/// </summary>
[Category("Unit")]
public sealed class RunnerRestartHealthTests
{
    private static (double, System.Text.Json.Nodes.JsonObject) Record(string name, int at, string attempt = "attempt-one", int code = 0) =>
        RunnerRestartWorld.Record(name, at, attempt, code);

    [Test]
    public void Default_budget_accepts_100_second_startup()
    {
        using var f = new RunnerRestartWorld { HealthyAt = 100000 };
        f.Records.AddRange([Record("build-start", 0), Record("launch-requested", 52000), Record("adoption-sweep-start", 54000), Record("adoption-sweep-end", 98000)]);
        var r = f.Run();
        r.Outcome.ShouldBe("healthy");
        r.Wait.ShouldBe(100000);
        r.Json.GetProperty("timeoutSec").GetInt32().ShouldBe(180);
        r.Json.GetProperty("firstHealth200ObservedAtUtc").GetString().ShouldBe("2026-09-07T10:01:40.0000000Z");
    }

    [Test]
    public void Explicit_60_second_wait_can_be_continued_without_restart()
    {
        using var f = new RunnerRestartWorld { HealthyAt = 100000 };
        var expired = f.Run("--timeout-sec", "60");
        expired.Outcome.ShouldBe("wait-expired");
        expired.Exit.ShouldBe(2);
        expired.Wait.ShouldBe(60000);
        expired.Json.GetProperty("firstHealth200ObservedAtUtc").ValueKind.ShouldBe(JsonValueKind.Null);
        f.StartMs = 60000;
        f.Forbid = true;
        var continued = f.Run("--wait-only");
        continued.Outcome.ShouldBe("healthy");
        continued.Wait.ShouldBe(40000);
        continued.Mutations.ShouldBe(expired.Mutations);
    }

    [Test]
    [Arguments(0)]
    [Arguments(3600000)]
    [Arguments(-3600000)]
    public void Wall_clock_jumps_do_not_change_wait_budget(int jump)
    {
        using var f = new RunnerRestartWorld { HealthyAt = 999999, UtcJump = jump, ActionMs = 7000 };
        var r = f.Run("--timeout-sec", "180");
        r.Wait.ShouldBe(180000);
        r.Outcome.ShouldBe("wait-expired");
        r.Json.GetProperty("commandElapsedMs").GetDouble().ShouldBe(187000);
        r.Trace.Count(t => t.Op == "probe").ShouldBe(90);
    }

    [Test]
    [Arguments(1)]
    [Arguments(750)]
    public void Probe_and_sleep_are_clamped_to_remaining_budget(int remaining)
    {
        using var f = new RunnerRestartWorld { HealthyAt = 999999, FirstProbeMs = 5000 - 2000 - remaining };
        var r = f.Run("--wait-only", "--timeout-sec", "5");
        r.Wait.ShouldBe(5000);
        var probes = r.Trace.Where(t => t.Op == "probe").ToArray();
        probes.Length.ShouldBe(2);
        probes[1].Value.ShouldBe((double)remaining);
        r.Trace[^1].Value.ShouldBe((double)remaining);
    }

    [Test]
    [Arguments(1001, 0)]
    [Arguments(0, 1001)]
    [Arguments(500, 501)]
    public void Late_200_is_not_in_budget_success(int probeMs, int verifyMs)
    {
        using var f = new RunnerRestartWorld { FirstProbeMs = probeMs, VerifyMs = verifyMs };
        var r = f.Run("--wait-only", "--timeout-sec", "1");
        r.Outcome.ShouldBe("wait-expired");
        r.Exit.ShouldBe(2);
        r.Json.GetProperty("firstHealth200ObservedAtUtc").ValueKind.ShouldBe(JsonValueKind.Null);
        r.Json.GetProperty("lastProbeResult").GetString().ShouldBe("http-200-late");
        r.Json.GetProperty("runner").GetProperty("pid").GetInt32().ShouldBe(900001);
        r.Output.ShouldContain("http-200-late");
    }

    [Test]
    [Arguments(200, false)]
    [Arguments(503, false)]
    [Arguments(200, true)]
    [Arguments(503, true)]
    public void Foreign_listener_expiry_names_actual_path_even_with_Hard(int status, bool hard)
    {
        using var f = new RunnerRestartWorld { Foreign = true, Status = status };
        var r = f.Run(hard ? "--hard" : "--wait-only", "--timeout-sec", "1");
        r.Outcome.ShouldBe("wait-expired");
        r.Exit.ShouldBe(2);
        r.Json.GetProperty("portOwner").GetProperty("pid").GetInt32().ShouldBe(900003);
        r.Json.GetProperty("portOwner").GetProperty("path").GetString().ShouldBe(@"C:\fixture\other-build\Antiphon.SessionRunner.exe");
        r.Output.ShouldContain(@"PID 900003; path: C:\fixture\other-build\Antiphon.SessionRunner.exe");
        r.Output.ShouldContain("not stopped, even with --hard");
        if (status == 200) r.Json.GetProperty("lastProbeResult").GetString().ShouldBe("http-200-identity-unconfirmed");
        r.Trace.SkipWhile(t => t.Op != "probe").All(t => t.Op is "probe" or "sleep").ShouldBeTrue();
    }

    [Test]
    [Arguments("probe")]
    [Arguments("clock")]
    [Arguments("utc")]
    public void Wait_exception_still_emits_one_final_result_without_more_controls(string operation)
    {
        using var f = new RunnerRestartWorld { FailWaitOperation = operation, FirstProbeMs = 123 };
        var r = f.Run("--wait-only"); // Run requires exactly one result line, last in the output.
        r.Exit.ShouldBe(1);
        r.Outcome.ShouldBe("wait-failed");
        r.Mutations.ShouldBeEmpty();
        r.Json.GetProperty("operation").GetString().ShouldBe("health-wait");
        r.Json.GetProperty("error").GetString().ShouldBe("injected wait failure: " + operation);
        r.Output.ShouldContain("Background startup has not been stopped");
        if (operation == "clock")
        {
            r.Json.GetProperty("waitElapsedMs").ValueKind.ShouldBe(JsonValueKind.Null);
            r.Json.GetProperty("commandElapsedMs").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        else
        {
            r.Wait.ShouldBe(123);
        }
        if (operation == "utc") r.Json.GetProperty("observedAtUtc").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public void Immediate_200_needs_no_initial_sleep()
    {
        using var f = new RunnerRestartWorld();
        var r = f.Run("--wait-only");
        r.Outcome.ShouldBe("healthy");
        r.Wait.ShouldBe(0);
        r.Trace.Length.ShouldBe(1);
        r.Trace[0].Op.ShouldBe("probe");
    }

    [Test]
    [Arguments(0, false, false)]
    [Arguments(204, false, false)]
    [Arguments(302, false, false)]
    [Arguments(404, false, false)]
    [Arguments(503, false, false)]
    [Arguments(200, true, false)]
    [Arguments(200, false, true)]
    [Arguments(200, false, false)]
    public void Health_requires_200_from_the_expected_runner(int status, bool foreign, bool reused)
    {
        using var f = new RunnerRestartWorld { Status = status, Foreign = foreign, Reused = reused };
        f.Records.Add(Record("build-start", 0));
        var r = f.Run("--wait-only", "--timeout-sec", "1");
        r.Outcome.ShouldBe(status == 200 && !foreign && !reused ? "healthy" : "wait-expired");
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(10000, false)]
    [Arguments(999999, false)]
    [Arguments(0, true)]
    [Arguments(999999, true)]
    public void WaitOnly_never_changes_daemon_control_state(int healthyAt, bool missing)
    {
        using var f = new RunnerRestartWorld { HealthyAt = healthyAt, NoSupervisor = missing, Forbid = true };
        var r = f.Run("--wait-only", "--timeout-sec", "12");
        r.Mutations.ShouldBeEmpty();
        r.Outcome.ShouldBe(healthyAt < 12000 ? "healthy" : "wait-expired");
        File.ReadAllText(f.StatePath).ShouldBe("stopped-sentinel");
        File.ReadAllText(f.PidPath).ShouldBe("pid-sentinel");
    }

    [Test]
    [Arguments("--hard")]
    [Arguments("--kill-sessions")]
    [Arguments("0")]
    [Arguments("-1")]
    public void Invalid_arguments_fail_before_any_control_operation(string argument)
    {
        using var f = new RunnerRestartWorld { Forbid = true };
        var r = int.TryParse(argument, out _) ? f.Run("--timeout-sec", argument) : f.Run("--wait-only", argument);
        r.Exit.ShouldBe(1);
        r.Outcome.ShouldBe("action-failed");
        r.Mutations.ShouldBeEmpty();
        r.Wait.ShouldBe(0);
        r.Json.GetProperty("operation").GetString().ShouldBe("validate-arguments");
        r.Output.ShouldContain("action-failed: validate-arguments: --timeout-sec must be positive; --wait-only cannot be combined with --hard or --kill-sessions");
    }

    [Test]
    [Arguments("soft")]
    [Arguments("hard")]
    [Arguments("wait")]
    public void Expiry_has_no_restart_or_stop_side_effects(string mode)
    {
        using var f = new RunnerRestartWorld { HealthyAt = 999999 };
        var arguments = new List<string> { "--timeout-sec", "1" };
        if (mode == "hard") arguments.Add("--hard");
        if (mode == "wait") arguments.Add("--wait-only");
        var r = f.Run(arguments.ToArray());
        r.Outcome.ShouldBe("wait-expired");
        r.Trace.SkipWhile(t => t.Op != "probe").All(t => t.Op is "probe" or "sleep").ShouldBeTrue("no mutation after wait starts");
        r.Output.ShouldContain("Continue: scripts/antiphon restart-session-runner --wait-only --timeout-sec 180");
        r.Output.ShouldContain("has not stopped background startup");
    }

    [Test]
    [Arguments("stop-service")]
    [Arguments("start-task")]
    public void Action_errors_report_the_failing_operation(string op)
    {
        using var f = new RunnerRestartWorld { FailOperation = op, NoSupervisor = true };
        var r = f.Run();
        r.Exit.ShouldBe(1);
        r.Outcome.ShouldBe("action-failed");
        r.Wait.ShouldBe(0);
        r.Json.GetProperty("operation").GetString().ShouldBe(op);
        r.Output.ShouldContain("injected operation failure: " + op);
        r.Trace[^1].Op.ShouldBe(op);
    }

    // ---- the startup milestones during the wait (the RunnerRestartDiagnosticTests partial) ----

    [Test]
    [Arguments(false, "build-complete")]
    [Arguments(true, "build-complete")]
    [Arguments(false, "launch-error")]
    [Arguments(true, "launch-error")]
    [Arguments(false, "service-exit")]
    [Arguments(true, "service-exit")]
    public void Retrying_startup_can_recover_or_expire(bool recover, string failedEvent)
    {
        using var f = new RunnerRestartWorld { HealthyAt = recover ? 10000 : 999999 };
        f.Records.AddRange([Record("build-start", 0), Record(failedEvent, 2000, code: 17), Record("retry-scheduled", 4000, code: 17)]);
        var r = f.Run("--timeout-sec", "12");
        r.Outcome.ShouldBe(recover ? "healthy" : "wait-expired");
        r.Json.GetProperty("lastObservedPhase").GetString().ShouldBe("retry-scheduled");
        r.Json.GetProperty("errorCode").GetInt32().ShouldBe(17);
    }

    [Test]
    public void Progress_output_is_throttled_without_changing_deadline()
    {
        using var f = new RunnerRestartWorld { HealthyAt = 999999 };
        f.Records.AddRange([Record("build-start", 0), Record("launch-requested", 52000), Record("adoption-sweep-start", 54000)]);
        var r = f.Run("--timeout-sec", "180");
        r.Wait.ShouldBe(180000);
        r.Json.GetProperty("lastObservedPhase").GetString().ShouldBe("adoption-sweep-start");
        r.Json.GetProperty("phaseAgeMs").GetDouble().ShouldBe(126000);
        r.Output.Split('\n').Count(l => l.StartsWith("Continuing health wait", StringComparison.Ordinal)).ShouldBeLessThanOrEqualTo(12);
    }

    [Test]
    public void Final_result_matches_exit_and_first_observation()
    {
        using var f = new RunnerRestartWorld();
        var r = f.Run("--wait-only");
        foreach (var key in new[] { "outcome", "mode", "waitElapsedMs", "commandElapsedMs", "timeoutSec", "observedAtUtc", "lastObservedPhase", "attemptId", "runner", "supervisor", "wrapper" })
            r.Json.TryGetProperty(key, out _).ShouldBeTrue(key);
        r.Json.GetProperty("commandElapsedMs").GetDouble().ShouldBeGreaterThanOrEqualTo(r.Wait);
        r.Json.GetProperty("observedAtUtc").GetString()!.ShouldMatch(@"\.\d{7}Z$");
        r.Json.EnumerateObject().Select(p => p.Name).ToArray().ShouldBe(
        [
            "outcome", "mode", "exitCode", "timeoutSec", "waitElapsedMs", "commandElapsedMs", "observedAtUtc", "lastObservedPhase", "phaseAgeMs",
            "attemptId", "supervisor", "wrapper", "runner", "firstHealth200ObservedAtUtc", "lastProbeResult", "errorCode", "operation", "error",
            "commandEnteredAtUtc", "supervisorAlive", "runnerAlive", "portOwner", "relaunchRequestedAtUtc", "waitStartedAtUtc", "pollingIntervalMs",
        ]);
    }

    // ---- the SHA stamp hint and the documented arguments (RunnerRestartScriptCompatibilityTests) ----

    [Test]
    [Arguments("""{"build":{"commitSha":"unknown"}}""", "soft", 0, 1)]
    [Arguments("""{"build":{"commitSha":null}}""", "soft", 0, 1)]
    [Arguments("""{"build":{"commitSha":"abcdef1"}}""", "soft", 0, 1)]
    [Arguments("""{"build":{"commitSha":"1111111111111111111111111111111111111111"}}""", "soft", 0, 0)]
    [Arguments("""{"build":{"commitSha":"2222222222222222222222222222222222222222"}}""", "soft", 0, 0)]
    [Arguments("{}", "soft", 0, 0)]
    [Arguments("invalid JSON", "soft", 0, 0)]
    [Arguments("""{"build":{"commitSha":"unknown"}}""", "hard", 0, 0)]
    [Arguments("""{"build":{"commitSha":"unknown"}}""", "wait-only", 0, 1)]
    [Arguments("""{"build":{"commitSha":"unknown"}}""", "expiry", 2, 0)]
    [Arguments("""{"build":{"commitSha":"unknown"}}""", "action-failed", 1, 0)]
    public void Sha_stamp_hint_is_advisory(string capabilities, string mode, int exit, int hints)
    {
        using var f = new RunnerRestartWorld { CapabilitiesJson = capabilities };
        if (mode == "expiry") f.Status = 503;
        if (mode == "action-failed") f.FailOperation = "stop-service";
        string[] arguments = mode switch
        {
            "hard" => ["--hard"],
            "wait-only" => ["--wait-only"],
            "expiry" => ["--timeout-sec", "1"],
            _ => [],
        };

        var r = f.Run(arguments);

        r.Exit.ShouldBe(exit);
        var operations = mode switch
        {
            "hard" => new[] { "write-state", "stop-service", "stop-supervisor", "delete-pids", "start-task" },
            "wait-only" => Array.Empty<string>(),
            _ => new[] { "write-state", "stop-service" },
        };
        r.Mutations.ShouldBe(operations);
        r.Wait.ShouldBe(mode == "expiry" ? 1000 : 0);
        r.Output.Split('\n', StringSplitOptions.TrimEntries)
            .Count(line => line == "WARNING: first restart after the SHA stamp activation: re-run with --hard")
            .ShouldBe(hints, r.Output);
    }

    [Test]
    public void Documented_arguments_execute_the_expected_mode()
    {
        using var f = new RunnerRestartWorld();
        f.Run().Outcome.ShouldBe("healthy");
        f.Forbid = true;
        f.Run("--wait-only").Json.GetProperty("mode").GetString().ShouldBe("wait-only");
        f.HealthyAt = 10000;
        f.Run("--wait-only", "--timeout-sec", "1").Exit.ShouldBe(2);
        f.Run("--wait-only", "--hard").Exit.ShouldBe(1);
        Should.Throw<NightlyUsageException>(() => RunnerRestartOptions.Parse(["--timeout-sec", "soon"], "root")).Message.ShouldBe("--timeout-sec expects an integer, not 'soon'");
        Should.Throw<NightlyUsageException>(() => RunnerRestartOptions.Parse(["-Hard"], "root")).Message.ShouldBe("unexpected argument '-Hard'");
        var defaults = RunnerRestartOptions.Parse([], "root");
        (defaults.Hard, defaults.KillSessions, defaults.WaitOnly, defaults.TimeoutSec, defaults.RepoRoot).ShouldBe((false, false, false, 180, "root"));
    }

    [Test]
    public void A_soft_restart_with_a_recorded_supervisor_only_stops_the_service_and_a_missing_one_starts_the_task()
    {
        using var recorded = new RunnerRestartWorld();
        recorded.Run().Mutations.ShouldBe(["write-state", "stop-service"]);
        File.ReadAllText(recorded.StatePath).ShouldBe("running");
        File.ReadAllText(recorded.PidPath).ShouldBe("pid-sentinel");

        using var missing = new RunnerRestartWorld { NoSupervisor = true };
        var r = missing.Run("--kill-sessions");
        r.Mutations.ShouldBe(["write-state", "kill-sessions", "stop-service", "delete-pids", "start-task"]);
        r.Json.GetProperty("relaunchRequestedAtUtc").GetString().ShouldNotBeNull();
        File.ReadAllText(missing.PidPath).ShouldBe("");
    }
}
