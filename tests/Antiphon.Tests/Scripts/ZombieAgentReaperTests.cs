using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The zombie agent census and reaper (Antiphon.Nightly.ZombieAgentReaper, scripts/reap-zombie-agents.ps1 until unit 22 of the
/// 2026-11-22 plan) in process. The census, incident, warm-pool, class B, class C, Z3 and unreachable rows are those of the deleted
/// driver scripts/test-reap-zombie-agents.ps1 (CARD-0221 S2). Every observation is the test's Machine: a fake process table, runner
/// and database, recorded kill requests and tree kills, and a liveness answer. The manifests, ansi logs and transcripts are files
/// under a temporary root, the clock is fake and the verify wait advances it. Nothing reads or kills a process of this machine.
/// </summary>
[Category("Unit")]
public sealed class ZombieAgentReaperTests
{
    private const string AntiphonCwd = @"C:\src\Antiphon";
    private const string PtyHostPath = @"C:\logs\antiphon\session-runner\pty-hosts\bin\Antiphon.PtyHost.exe";
    private const string ClaudePath = @"C:\Users\lndco\AppData\Local\claude\claude.exe";
    private const string IncidentSession = "71bd54b1-0000-4000-8000-000000000001";
    private const string ClassCSession = "cccccccc-0000-4000-8000-000000000001";

    [Test]
    public void Census_of_21_runner_claimed_agents_and_one_operator_claude_has_no_positives()
    {
        using var work = new Work();
        var machine = Census();
        var run = Run(work, machine, "2026-08-28T22:00:00Z");
        var c = run.Result;
        c.ExitCode.ShouldBe(0, "census exit 0 (no positives)");
        var claimed = c.Rows.Where(row => row.IdentityMethod == "I1").ToArray();
        claimed.Length.ShouldBe(21, "census 21 runner-claimed (I1)");
        c.Ignored.Count.ShouldBe(1, "census 1 ignored");
        c.Ignored[0].Pid.ShouldBe(27592, "census ignored pid is operator claude 27592");
        c.Ignored[0].RulesFailed.ShouldContain("operator", Case.Sensitive, "census ignored reason is operator-launched");
        claimed.Count(row => row.Exe == "codex.exe").ShouldBe(6, "census Codex three-hop matched (6)");
        c.Positives.Count.ShouldBe(0, "census 0 positives");
        c.KillCalls.Count.ShouldBe(0, "census no kill calls");
        machine.Posts.ShouldBeEmpty();
        machine.TreeKills.ShouldBeEmpty();

        claimed.ShouldAllBe(row => row.RulesFailed == "Z4 not a zombie class (warm/standing/live)" && row.DbStatus == "Running");
        claimed.Where(row => row.Exe == "codex.exe").ShouldAllBe(row => row.TreeKillPid > 30000 && row.TreeKillPid < 30022, "the pty host is the codex tree root");
        c.ClassCounts.ShouldBe(new ZombieClassCounts(0, 0, 0, 0, 1, 0, 0));
        run.Output.ShouldContain("Processes (agent-shaped): 22   ignored: 1   unidentified: 0   positives: 0");
        run.Output.ShouldContain("Dry run: nothing was killed. Re-run with --execute to act on PoolExpired (the default --class).");
        File.Exists(c.ReportPath.ShouldNotBeNull()).ShouldBeTrue();
    }

    [Test]
    public void Incident_0ea601b2_is_a_PoolExpired_positive_that_execute_kills_through_the_server()
    {
        using var work = new Work();
        var machine = new Machine();
        Incident(machine, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z");
        var inc = Run(work, machine, "2026-08-28T12:00:00Z").Result;
        inc.ExitCode.ShouldBe(3, "incident dry-run exit 3");
        inc.Positives.Count.ShouldBe(1, "incident 1 positive");
        var positive = inc.Positives[0];
        positive.Class.ShouldBe("PoolExpired", "incident class A PoolExpired");
        positive.KillPath.ShouldBe("server", "incident kill path is server");
        positive.IdentityMethod.ShouldBe("I1", "incident identity I1");
        positive.Pid.ShouldBe(17088, "incident pid 17088");
        positive.Agent.ShouldBe("task-0ea601b2");
        positive.CpuDeltaPct.ShouldBe(92.5);
        machine.Posts.ShouldBeEmpty("a dry run sends no kill");

        var execute = new Machine();
        Incident(execute, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z");
        var run = Run(work, execute, "2026-08-28T12:00:00Z", "--execute", "--class", "PoolExpired");
        var incX = run.Result;
        incX.ExitCode.ShouldBe(0, "incident execute exit 0");
        execute.Posts.Count.ShouldBeGreaterThanOrEqualTo(1, "incident execute posted a kill");
        execute.Posts[0].ShouldContain("/api/sessions/71bd54b1", Case.Sensitive, "incident execute uses server kill path");
        execute.Posts[0].ShouldBe("http://localhost:17202/api/sessions/" + IncidentSession + "/kill");
        incX.KillCalls[0].Method.ShouldBe("POST", "incident execute is POST");
        execute.TreeKills.ShouldBeEmpty();
        run.Output.ShouldContain("killed       pid 17088  PoolExpired  server  " + IncidentSession);
        run.Output.ShouldContain("Killed and verified: 1   failed: 0   positives: 1   ignored: 0");
    }

    [Test]
    public void Warm_pool_task_a503916a_twenty_minutes_old_is_not_a_positive()
    {
        using var work = new Work();
        const string session = "a503916a-0000-4000-8000-000000000001";
        const string agent = "a503916a-0000-4000-8000-0000000000aa";
        var machine = new Machine();
        machine.Table.Add(Proc(9001, 4, "Antiphon.PtyHost.exe", PtyHostPath, "Antiphon.PtyHost.exe", "2026-08-28T22:00:00Z", 40000000, 0.2));
        machine.Table.Add(Proc(9002, 9001, "claude.exe", ClaudePath, "claude.exe", "2026-08-28T22:00:00Z", 400000000, 1.0));
        machine.Runner(session, 9002, 9001, "2026-08-28T22:00:00Z");
        machine.Session(session, "Running", "2026-08-28T22:00:00Z", null);
        machine.Agent(agent, "task-a503916a", session, pool: true, status: "Idle");
        machine.TaskRow("a503916a-0000-4000-8000-000000000002", agent, session, "Succeeded", "2026-08-28T22:17:00Z", worktree: null);
        var w = Run(work, machine, "2026-08-28T22:37:00Z").Result;
        w.ExitCode.ShouldBe(0, "warm-pool task-a503916a exit 0");
        w.Positives.Count.ShouldBe(0, "warm-pool task-a503916a not a positive (Z4 MinDoneMinutes)");
        var row = w.Rows.SingleOrDefault(r => r.Pid == 9002);
        row.ShouldNotBeNull("warm-pool row present");
        (row.RulesFailed.Contains("Z4") || row.RulesFailed.Contains("Z6")).ShouldBeTrue("warm-pool fails Z4 or Z6: " + row.RulesFailed);
        row.RulesFailed.ShouldBe("Z6 process only 37 min old (< 120); Z4 not a zombie class (warm/standing/live)");
    }

    [Test]
    public void Failed_and_runner_claimed_is_class_B_and_is_never_killed()
    {
        using var work = new Work();
        const string session = "bbbbbbbb-0000-4000-8000-000000000001";
        var machine = new Machine();
        machine.Table.Add(Proc(8001, 4, "Antiphon.PtyHost.exe", PtyHostPath, "Antiphon.PtyHost.exe", "2026-08-20T07:00:00Z", 40000000, 0.2));
        machine.Table.Add(Proc(8002, 8001, "claude.exe", ClaudePath, "claude.exe", "2026-08-20T07:00:00Z", 400000000, 1.0));
        machine.Runner(session, 8002, 8001, "2026-08-20T07:00:00Z");
        // The status as the database stores the enum: 5 is Failed.
        machine.Session(session, 5, "2026-08-20T07:00:00Z", "2026-08-20T08:00:00Z");
        var b = Run(work, machine, "2026-08-28T12:00:00Z", "--execute", "--class", "EndedButAlive").Result;
        var row = b.Rows.Single(r => r.Pid == 8002);
        row.Class.ShouldBe("ReconcilerOwned", "Failed+claimed is class B");
        row.RulesFailed.ShouldBe("Z4 reconciler re-adopts Failed (CARD-0056)");
        row.DbStatus.ShouldBe("Failed");
        b.Positives.Count.ShouldBe(0, "class B is not a positive");
        machine.Posts.Count.ShouldBe(0, "class B no kill call even with -Execute -Class EndedButAlive");
        machine.TreeKills.ShouldBeEmpty();
        b.KillCalls.Count.ShouldBe(0, "class B PassThru KillCalls empty");
        b.ExitCode.ShouldBe(0);
    }

    [Test]
    public void Class_C_waits_for_quiet_activity_then_tree_kills_from_the_pty_host()
    {
        using var recent = new Work();
        var c5Machine = ClassC(recent);
        recent.Transcript(AntiphonCwd, "2026-08-28T11:50:00Z");
        var c5 = Run(recent, c5Machine, "2026-08-28T12:00:00Z").Result;
        var c5Row = c5.Rows.Single(r => r.Pid == 7002);
        c5Row.RulesFailed.Contains("Z5").ShouldBeTrue("class C recent transcript fails Z5: " + c5Row.RulesFailed);
        c5Row.RulesFailed.ShouldBe("Z5 activity 0.2 h ago (< 6 QuietHours)");
        c5.Positives.Count.ShouldBe(0, "class C Z5 fail is not a positive");

        using var quiet = new Work();
        var c5okMachine = ClassC(quiet);
        quiet.Transcript(AntiphonCwd, "2026-08-28T04:00:00Z");
        quiet.AnsiLog(ClassCSession, "2026-08-28T04:00:00Z");
        var run = Run(quiet, c5okMachine, "2026-08-28T12:00:00Z", "--execute", "--class", "EndedButAlive");
        var c5ok = run.Result;
        c5ok.Positives.Count.ShouldBe(1, "class C quiet 7h is a positive");
        var positive = c5ok.Positives[0];
        positive.Class.ShouldBe("EndedButAlive", "class C name");
        positive.KillPath.ShouldBe("taskkill", "class C kill path is taskkill");
        positive.IdentityMethod.ShouldBe("I2", "class C identity is I2 (pty-host manifest)");
        var taskkill = c5ok.KillCalls.Where(call => call.Method == "TASKKILL").ToArray();
        taskkill.Length.ShouldBeGreaterThanOrEqualTo(1, "class C execute records taskkill");
        taskkill[0].Uri.ShouldContain("7001", Case.Sensitive, "class C taskkill targets pty-host ancestor");
        c5okMachine.TreeKills.ShouldBe([7001]);
        c5okMachine.Posts.ShouldBeEmpty();
        c5ok.ExitCode.ShouldBe(0);

        // The ansi log alone is activity too: a recent one fails Z5 without any transcript.
        using var ansi = new Work();
        var ansiMachine = ClassC(ansi);
        ansi.AnsiLog(ClassCSession, "2026-08-28T10:00:00Z");
        Run(ansi, ansiMachine, "2026-08-28T12:00:00Z").Result.Rows.Single(r => r.Pid == 7002).RulesFailed.ShouldBe("Z5 activity 2.0 h ago (< 6 QuietHours)");

        // A recent end is not yet class C.
        using var ended = new Work();
        var endedMachine = ClassC(ended);
        Run(ended, endedMachine, "2026-08-20T09:00:00Z", "--min-done-minutes", "1").Result.Rows.Single(r => r.Pid == 7002).Class.ShouldBe("EndedButAlive");
        Run(ended, endedMachine, "2026-08-20T09:00:00Z").Result.Rows.Single(r => r.Pid == 7002).RulesFailed.ShouldContain("Z4 EndedAt younger than -MinDoneMinutes");
    }

    [Test]
    public void Pid_reuse_fails_Z3()
    {
        using var work = new Work();
        const string session = "dddddddd-0000-4000-8000-000000000001";
        const string agent = "dddddddd-0000-4000-8000-0000000000aa";
        var machine = new Machine();
        machine.Table.Add(Proc(6001, 4, "Antiphon.PtyHost.exe", PtyHostPath, "Antiphon.PtyHost.exe", "2026-08-20T06:00:00Z", 40000000, 0.2));
        machine.Table.Add(Proc(6002, 6001, "claude.exe", ClaudePath, "claude.exe", "2026-08-20T06:00:00Z", 400000000, 1.0));
        machine.Runner(session, 6002, 6001, "2026-08-20T07:00:00Z");
        machine.Session(session, "Running", "2026-08-20T07:00:00Z", null);
        machine.Agent(agent, "stale", session, pool: true, status: "Running");
        machine.TaskRow("dddddddd-0000-4000-8000-0000000000bb", agent, session, "Succeeded", "2026-08-20T08:00:00Z", worktree: null);
        var z = Run(work, machine, "2026-08-28T12:00:00Z").Result;
        var row = z.Rows.Single(r => r.Pid == 6002);
        row.RulesFailed.Contains("Z3").ShouldBeTrue("pid reuse fails Z3: " + row.RulesFailed);
        row.Class.ShouldBe("PoolExpired");
        z.Positives.Count.ShouldBe(0, "pid reuse is not a positive");
        z.ExitCode.ShouldBe(0);
    }

    [Test]
    public void The_runner_the_database_or_the_process_table_not_answering_exits_2_with_no_verdicts()
    {
        using var work = new Work();
        var runnerDown = Census();
        runnerDown.RunnerDown = true;
        var u = Run(work, runnerDown, "2026-08-28T22:00:00Z");
        u.Result.ExitCode.ShouldBe(2, "runner unreachable exit 2");
        u.Result.Rows.Count.ShouldBe(0, "runner unreachable no verdicts");
        u.Result.ReportPath.ShouldBeNull();
        u.Output.ShouldContain("ABORT: session-runner at http://localhost:17204 did not answer GET /sessions: connection refused");
        runnerDown.Queries.ShouldBe(0, "the database is not asked once the runner did not answer");

        var runner500 = Census();
        runner500.RunnerAnswer = new NightlyReportHttpResponse(500, "boom");
        Run(work, runner500, "2026-08-28T22:00:00Z").Output.ShouldContain("did not answer GET /sessions: HTTP 500 boom");

        var dbDown = Census();
        dbDown.DbDown = true;
        var u2 = Run(work, dbDown, "2026-08-28T22:00:00Z");
        u2.Result.ExitCode.ShouldBe(2, "db unreachable exit 2");
        u2.Result.Rows.Count.ShouldBe(0, "db unreachable no verdicts");
        u2.Output.ShouldContain("ABORT: the database did not answer (docker exec antiphon-postgres psql ... exit 1):");
        u2.Output.ShouldContain("Error response from daemon: No such container: antiphon-postgres");

        var dbGarbage = Census();
        dbGarbage.DbText = "psql: warning";
        var u3 = Run(work, dbGarbage, "2026-08-28T22:00:00Z");
        u3.Result.ExitCode.ShouldBe(2);
        u3.Output.ShouldContain("ABORT: the database did not answer (JSON parse): ");

        var noTable = Census();
        noTable.TableFailure = new PlatformNotSupportedException("the census reads the Windows process table.");
        var u4 = Run(work, noTable, "2026-08-28T22:00:00Z");
        u4.Result.ExitCode.ShouldBe(2);
        u4.Output.ShouldContain("ABORT: the process table could not be read: the census reads the Windows process table.");
    }

    [Test]
    public void A_server_kill_that_fails_falls_back_to_the_runner_then_to_a_tree_kill()
    {
        using var work = new Work();
        var runnerFallback = new Machine { KillAnswer = uri => uri.StartsWith("http://localhost:17202/", StringComparison.Ordinal) ? new NightlyReportHttpResponse(503, "down") : null };
        Incident(runnerFallback, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z");
        var viaRunner = Run(work, runnerFallback, "2026-08-28T12:00:00Z", "--execute");
        viaRunner.Result.ExitCode.ShouldBe(0, viaRunner.Output);
        runnerFallback.Posts.ShouldBe(["http://localhost:17202/api/sessions/" + IncidentSession + "/kill", "http://localhost:17204/sessions/" + IncidentSession + "/kill"]);
        viaRunner.Output.ShouldContain("server kill failed  " + IncidentSession + "  HTTP 503 down  falling back to runner");
        viaRunner.Output.ShouldContain("killed       pid 17088  PoolExpired  runner  " + IncidentSession);
        runnerFallback.TreeKills.ShouldBeEmpty();

        var neither = new Machine { KillAnswer = _ => throw new HttpRequestException("refused") };
        Incident(neither, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z");
        var viaTree = Run(work, neither, "2026-08-28T12:00:00Z", "--execute", "--runner-url", "http://runner.test:9/", "--server-url", "http://server.test:9/");
        viaTree.Result.ExitCode.ShouldBe(0, viaTree.Output);
        neither.Posts.ShouldBe(["http://server.test:9/api/sessions/" + IncidentSession + "/kill", "http://runner.test:9/sessions/" + IncidentSession + "/kill"]);
        neither.TreeKills.ShouldBe([10756], "the tree kill starts at the pty host");
        viaTree.Result.KillCalls.Select(call => call.Method + " " + call.Uri).Last().ShouldBe("TASKKILL pid:10756");
        viaTree.Output.ShouldContain("runner kill failed  " + IncidentSession + "  refused  falling through to taskkill");
        viaTree.Output.ShouldContain("killed       pid 17088  PoolExpired  taskkill  " + IncidentSession);
    }

    [Test]
    public void A_leaf_still_alive_after_the_verify_window_exits_1()
    {
        using var work = new Work();
        var machine = new Machine();
        Incident(machine, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z");
        machine.StillAlive.Add(17088);
        var run = Run(work, machine, "2026-08-28T12:00:00Z", "--execute", "--kill-verify-seconds", "3");
        run.Result.ExitCode.ShouldBe(1);
        run.Output.ShouldContain("STILL ALIVE  pid 17088  after 3s");
        run.Output.ShouldContain("Killed and verified: 0   failed: 1   positives: 1   ignored: 0");
        machine.AliveChecks.ShouldBe(6, "polled every 500 ms for 3 s of the fake clock");
        using var report = JsonDocument.Parse(File.ReadAllText(run.Result.ReportPath.ShouldNotBeNull()));
        report.RootElement.GetProperty("exitCode").GetInt32().ShouldBe(1);
        report.RootElement.GetProperty("killCalls")[0].GetProperty("Method").GetString().ShouldBe("POST");
    }

    [Test]
    public void Execute_skips_classes_outside_class_and_limit_kills_the_oldest_first()
    {
        using var work = new Work();
        var machine = ClassC(work);
        work.AnsiLog(ClassCSession, "2026-08-28T04:00:00Z");
        Incident(machine, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z", agentId: "aaaaaaaa-0000-4000-8000-0000000000aa", task: "0ea601b2-0000-4000-8000-000000000001");
        const string older = "72bd54b1-0000-4000-8000-000000000001";
        Incident(machine, 20756, 27088, older, "2026-08-19T07:45:00Z", agentId: "aaaaaaaa-0000-4000-8000-0000000000bb", task: "1ea601b2-0000-4000-8000-000000000001");
        var run = Run(work, machine, "2026-08-28T12:00:00Z", "--execute", "--limit", "2");
        run.Result.Positives.Count.ShouldBe(3);
        run.Result.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("--limit 2: killing the 2 oldest of 3 positives this run.");
        // The two oldest are the class C process (2026-08-20 07:00) and the older pool delegate; class C is not in --class.
        run.Output.ShouldContain("skip         pid 7002  class EndedButAlive not in --class PoolExpired");
        machine.Posts.ShouldBe(["http://localhost:17202/api/sessions/" + older + "/kill"]);
        machine.TreeKills.ShouldBeEmpty();
        run.Output.ShouldContain("Killed and verified: 1   failed: 0   positives: 3   ignored: 0");

        // Both classes, ignoring case, any separator.
        var both = ClassC(work);
        Incident(both, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z");
        Run(work, both, "2026-08-28T12:00:00Z", "--execute", "--class", "endedbutalive; POOLEXPIRED").Result.ExitCode.ShouldBe(0);
        both.TreeKills.ShouldBe([7001]);
        both.Posts.ShouldBe(["http://localhost:17202/api/sessions/" + IncidentSession + "/kill"]);
    }

    [Test]
    public void The_identity_ladder_after_the_runner_and_the_pre_filters()
    {
        using var work = new Work();
        string Sid(int n) => "eeeeeeee-0000-4000-8000-" + n.ToString("D12");
        var machine = new Machine();
        foreach (var n in new[] { 3, 4, 5, 6 }) machine.Session(Sid(n), "Running", "2026-08-01T00:00:00Z", null);
        machine.Agent("eeeeeeee-0000-4000-8000-0000000000a4", "Named-Four", Sid(4), pool: false, status: "Running", slug: "slug-four");
        machine.TaskRow("t5", "eeeeeeee-0000-4000-8000-0000000000a5", Sid(5), "Working", null, worktree: @"C:\Antiphon\worktrees\card-task-0000abcd");
        machine.TaskRow("t6", "eeeeeeee-0000-4000-8000-0000000000a6", Sid(6), "Working", null, worktree: null, workingDirectory: @"C:\x\card-task-0000ef01");
        const string old = "2026-08-01T00:00:00Z";
        machine.Table.Add(Proc(6101, 4, "claude.exe", ClaudePath, "claude.exe --session-id " + Sid(3).ToUpperInvariant(), old));
        machine.Table.Add(Proc(6102, 4, "grok.exe", @"C:\grok\grok.exe", "grok.exe --name SLUG-FOUR", old));
        machine.Table.Add(Proc(6103, 4, "codex.exe", @"C:\npm\codex.exe", "codex.exe", old, cwd: @"C:\Antiphon\worktrees\card-task-0000ABCD"));
        machine.Table.Add(Proc(6104, 4, "claude.exe", ClaudePath, "claude.exe --add-dir \"C:\\x\\card-task-0000ef01\"", old));
        machine.Table.Add(Proc(6105, 4, "claude.exe", @"C:\Program Files\WindowsApps\Claude\claude.exe", "claude.exe --session-id " + Sid(3), old));
        machine.Table.Add(Proc(6106, 4, "claude.exe", ClaudePath, "claude.exe --resume " + Sid(3), old));
        machine.Table.Add(Proc(6107, 4, "claude.exe", ClaudePath, "claude.exe --session-id " + Sid(7), old));
        machine.Table.Add(Proc(6110, 4, "explorer.exe", @"C:\Windows\explorer.exe", "explorer.exe", old));
        machine.Table.Add(Proc(6109, 6110, "herdr.exe", @"C:\herdr\herdr.exe", "herdr.exe", old));
        machine.Table.Add(Proc(6108, 6109, "claude.exe", ClaudePath, "claude.exe", old));
        machine.Table.Add(Proc(6111, 4, "node.exe", @"C:\node\node.exe", "node.exe --session-id " + Sid(3), old));
        var result = Run(work, machine, "2026-08-28T12:00:00Z").Result;

        ZombieAgentRow Row(int pid) => result.Rows.Single(row => row.Pid == pid);
        (Row(6101).IdentityMethod, Row(6101).SessionId).ShouldBe(("I3", Sid(3)));
        (Row(6102).IdentityMethod, Row(6102).SessionId, Row(6102).Agent).ShouldBe(("I4", Sid(4), "Named-Four"));
        (Row(6103).IdentityMethod, Row(6103).SessionId).ShouldBe(("I5", Sid(5)), "the cwd names the task's worktree");
        (Row(6104).IdentityMethod, Row(6104).SessionId).ShouldBe(("I5", Sid(6)), "the command line names the task's working directory");
        (Row(6105).Class, Row(6105).RulesFailed).ShouldBe(("Ignored", "WindowsApps"));
        (Row(6106).Class, Row(6106).RulesFailed).ShouldBe(("Unclaimed", "Z2 identity unresolved"), "--resume is never an identity");
        (Row(6107).IdentityMethod, Row(6107).Class, Row(6107).RulesFailed).ShouldBe(("I3", "Unclaimed", "Z2 no AgentSessions row"));
        Row(6108).Class.ShouldBe("Unclaimed", "a herdr parent before the operator's explorer is not operator-launched");
        result.Rows.ShouldNotContain(row => row.Pid == 6111 || row.Pid == 6110 || row.Pid == 6109, "only agent-shaped processes are rows");
        result.Ignored.Select(row => row.Pid).ShouldBe([6105]);
        result.Unidentified.Select(row => row.Pid).ShouldBe([6106, 6108], ignoreOrder: true);
        result.ClassCounts.ShouldBe(new ZombieClassCounts(0, 0, 0, 3, 1, 2, 0));
        result.ExitCode.ShouldBe(0);
    }

    [Test]
    public void The_report_holds_the_allow_listed_rows_and_the_options_parse()
    {
        using var work = new Work();
        var machine = new Machine();
        Incident(machine, 10756, 17088, IncidentSession, "2026-08-20T07:45:00Z");
        var result = Run(work, machine, "2026-08-28T12:00:00Z", "--min-working-set-gb", "0.4").Result;
        result.ReportPath.ShouldBe(Path.Combine(work.Reports, "20260828T120000Z.json"));
        using var report = JsonDocument.Parse(File.ReadAllText(result.ReportPath!));
        var root = report.RootElement;
        root.GetProperty("now").GetString().ShouldBe("2026-08-28T12:00:00.0000000Z");
        root.GetProperty("execute").GetBoolean().ShouldBeFalse();
        root.GetProperty("classFilter").GetString().ShouldBe("PoolExpired");
        (root.GetProperty("quietHours").GetInt32(), root.GetProperty("minDoneMinutes").GetInt32(), root.GetProperty("minWorkingSetGB").GetDouble()).ShouldBe((6, 120, 0.4));
        root.GetProperty("counts").GetProperty("PoolExpired").GetInt32().ShouldBe(1);
        root.GetProperty("counts").GetProperty("Positives").GetInt32().ShouldBe(1);
        root.GetProperty("exitCode").GetInt32().ShouldBe(3);
        root.GetProperty("killCalls").GetArrayLength().ShouldBe(0);
        var row = root.GetProperty("rows").EnumerateArray().Single();
        row.EnumerateObject().Select(property => property.Name).ShouldBe(["pid", "exe", "start", "workingSetGB", "cpuDeltaPct", "identityMethod", "sessionId", "dbStatus",
            "agent", "class", "rulesFailed", "killPath", "largeWs", "runnerClaimed"]);
        (row.GetProperty("pid").GetInt32(), row.GetProperty("class").GetString(), row.GetProperty("start").GetString()).ShouldBe((17088, "PoolExpired", "2026-08-20T07:45:00.0000000Z"));
        (row.GetProperty("workingSetGB").GetDouble(), row.GetProperty("largeWs").GetBoolean(), row.GetProperty("runnerClaimed").GetBoolean()).ShouldBe((0.466, true, true));

        var defaults = ZombieAgentReaperOptions.Parse([], @"C:\profile", @"C:\repo");
        (defaults.Execute, defaults.RunnerUrl, defaults.ServerUrl, defaults.SessionLogPath, defaults.PgContainer, defaults.PgUser, defaults.PgDatabase)
            .ShouldBe((false, "http://localhost:17204", "http://localhost:17202", @"C:\logs\antiphon\session-runner", "antiphon-postgres", "antiphon", "antiphon"));
        (defaults.Class, defaults.QuietHours, defaults.MinDoneMinutes, defaults.MinWorkingSetGB, defaults.PidReuseToleranceSec, defaults.KillVerifySeconds, defaults.Limit)
            .ShouldBe(("PoolExpired", 6, 120, 0.0, 5, 20, 0));
        defaults.ReportPath.ShouldBe(Path.Combine(@"C:\repo", "logs", "zombie-agents"));
        defaults.UserProfile.ShouldBe(@"C:\profile");
        defaults.ExecuteClasses.ShouldBe(new[] { "PoolExpired" });
        ZombieAgentReaperOptions.Parse(["--class", "PoolExpired,EndedButAlive"], "", "").ExecuteClasses.Contains("endedbutalive").ShouldBeTrue();
        Should.Throw<NightlyUsageException>(() => ZombieAgentReaperOptions.Parse(["--min-working-set-gb", "big"], "", ""));
        Should.Throw<NightlyUsageException>(() => ZombieAgentReaperOptions.Parse(["--limit", "five"], "", ""));
        Should.Throw<NightlyUsageException>(() => ZombieAgentReaperOptions.Parse(["--now", "2026-08-28T12:00:00Z"], "", ""), "the clock is the host's");
        ZombieAgentReaper.ClaudeProjectDirectory(@"C:\src\Antiphon").ShouldBe("C--src-Antiphon");
        ZombieAgentReaper.CardTaskLeaf(@"C:\w\card-task-0EA601B2\sub").ShouldBe("card-task-0ea601b2");
        ZombieAgentReaper.CardTaskLeaf(@"C:\w\card-task-0ea601b2").ShouldBe("card-task-0ea601b2");
        ZombieAgentReaper.CardTaskLeaf(@"C:\w\card-task-0ea601b").ShouldBeNull();
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "ZombieAgentReaper.cs")).Count(b => b > 127)
            .ShouldBe(0, "the reaper source is ASCII-only");
    }

    // ---- fixtures ----

    private static string CensusId(int n) => "aaaaaaaa-0000-0000-0000-" + n.ToString("D12");

    private static ZombieProcess Proc(int pid, int parent, string name, string path, string commandLine, string created, long workingSet = 400000000, double cpu = 1.0,
        string cwd = AntiphonCwd) => new(pid, parent, name, path, commandLine, cwd, DateTime.Parse(created, null, System.Globalization.DateTimeStyles.AdjustToUniversal), workingSet, cpu);

    /// <summary>New-CensusFiles: 21 runner-claimed agents (10 claude, 5 grok, 6 codex three hops down), one operator claude and a 22nd session without a process.</summary>
    private static Machine Census()
    {
        const string started = "2026-08-28T12:00:00Z";
        var machine = new Machine();
        string[] kinds = [.. Enumerable.Repeat("claude", 10), .. Enumerable.Repeat("grok", 5), .. Enumerable.Repeat("codex", 6)];
        for (var i = 1; i <= 21; i++)
        {
            var hostPid = 30000 + i;
            int child;
            machine.Table.Add(Proc(hostPid, 4, "Antiphon.PtyHost.exe", PtyHostPath, "Antiphon.PtyHost.exe", started, 40000000, 0.1));
            if (kinds[i - 1] == "codex")
            {
                child = 40000 + i;
                machine.Table.Add(Proc(child, hostPid, "cmd.exe", @"C:\Windows\System32\cmd.exe", "cmd.exe /c codex", started, 8000000, 0.1));
                machine.Table.Add(Proc(41000 + i, child, "node.exe", @"C:\Program Files\nodejs\node.exe", "node.exe", started, 20000000, 1.0));
                machine.Table.Add(Proc(42000 + i, 41000 + i, "codex.exe", @"C:\Users\lndco\AppData\Roaming\npm\codex.exe", "codex.exe", started, 300000000, 2.0));
            }
            else
            {
                child = 50000 + i;
                var grok = kinds[i - 1] == "grok";
                machine.Table.Add(Proc(child, hostPid, grok ? "grok.exe" : "claude.exe", grok ? @"C:\Users\lndco\AppData\Local\grok\grok.exe" : ClaudePath,
                    grok ? "grok.exe" : "claude.exe", started, 400000000, 5.0));
            }
            machine.Runner(CensusId(i), child, hostPid, started);
            machine.Session(CensusId(i), "Running", started, null);
            machine.Agent(CensusId(100 + i), "agent-" + i, CensusId(i), pool: false, status: "Running");
        }
        machine.Table.Add(Proc(27580, 4, "WindowsTerminal.exe", @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe", "WindowsTerminal.exe",
            "2026-08-28T10:00:00Z", 50000000, 0.2, @"C:\src\ClaudeBot"));
        machine.Table.Add(Proc(27590, 27580, "cmd.exe", @"C:\Windows\System32\cmd.exe", "cmd.exe", "2026-08-28T10:00:00Z", 5000000, 0.1, @"C:\src\ClaudeBot"));
        machine.Table.Add(Proc(27592, 27590, "claude.exe", ClaudePath, "claude.exe --name ClaudeBot", "2026-08-28T10:00:00Z", 520000000, 1.5, @"C:\src\ClaudeBot"));
        machine.Runner(CensusId(22), 59999, 59998, started);
        machine.Session(CensusId(22), "Running", started, null);
        return machine;
    }

    /// <summary>The 0ea601b2 / 71bd54b1 incident: a Worktree pool delegate whose task Succeeded, still Running under its pty host a week on.</summary>
    private static void Incident(Machine machine, int hostPid, int leafPid, string session, string created, string agentId = "aaaaaaaa-0000-4000-8000-0000000000aa",
        string task = "0ea601b2-0000-4000-8000-000000000001")
    {
        const string worktree = @"C:\Antiphon\worktrees\card-task-0ea601b2";
        machine.Table.Add(Proc(hostPid, 4, "Antiphon.PtyHost.exe", PtyHostPath, "Antiphon.PtyHost.exe", created, 40000000, 0.2, worktree));
        machine.Table.Add(Proc(leafPid, hostPid, "claude.exe", ClaudePath, "claude.exe", created, 500000000, 92.5, worktree));
        machine.Runner(session, leafPid, hostPid, created);
        machine.Session(session, "Running", created, null, worktree);
        machine.Agent(agentId, "task-0ea601b2", session, pool: true, status: "Running");
        machine.TaskRow(task, agentId, session, "Succeeded", "2026-08-20T07:55:22Z", worktree);
    }

    /// <summary>New-ClassCFiles: a Stopped session whose claude outlives it under a pty host the runner lost (its manifest names the session).</summary>
    private static Machine ClassC(Work work)
    {
        var machine = new Machine();
        machine.Table.Add(Proc(7001, 4, "Antiphon.PtyHost.exe", PtyHostPath, "Antiphon.PtyHost.exe", "2026-08-20T07:00:00Z", 40000000, 0.2));
        machine.Table.Add(Proc(7002, 7001, "claude.exe", ClaudePath, "claude.exe --session-id " + ClassCSession, "2026-08-20T07:00:00Z"));
        machine.Session(ClassCSession, "Stopped", "2026-08-20T07:00:00Z", "2026-08-20T08:00:00Z");
        var manifests = Directory.CreateDirectory(Path.Combine(work.SessionLog, "pty-hosts", "manifests")).FullName;
        File.WriteAllText(Path.Combine(manifests, "7001.json"), "{\n  \"sessionId\": \"" + ClassCSession.ToUpperInvariant() + "\",\n  \"hostPid\": 7001\n}");
        return machine;
    }

    private static (ZombieAgentReaperResult Result, string Output) Run(Work work, Machine machine, string now, params string[] args)
    {
        var output = new StringWriter();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(now));
        var host = new NightlyHost
        {
            Clock = clock,
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Sleep = span => clock.Advance(span),
        };
        var options = ZombieAgentReaperOptions.Parse(["--report-path", work.Reports, "--session-log-path", work.SessionLog, .. args], work.Profile, work.Root);
        var result = ZombieAgentReaper.Run(options, host, new System.IO.Abstractions.FileSystem(), machine);
        return (result, output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>The machine: its process table, runner and database, and what --execute asked of it.</summary>
    private sealed class Machine : IZombieAgentSeams
    {
        public List<ZombieProcess> Table { get; } = [];
        public JsonArray RunnerSessions { get; } = [];
        public JsonObject Db { get; } = new() { ["sessions"] = new JsonArray(), ["agents"] = new JsonArray(), ["tasks"] = new JsonArray() };
        public bool RunnerDown { get; set; }
        public NightlyReportHttpResponse? RunnerAnswer { get; set; }
        public bool DbDown { get; set; }
        public string? DbText { get; set; }
        public Exception? TableFailure { get; set; }
        public Func<string, NightlyReportHttpResponse?>? KillAnswer { get; init; }
        public List<string> Posts { get; } = [];
        public List<int> TreeKills { get; } = [];
        public HashSet<int> StillAlive { get; } = [];
        public int Queries { get; private set; }
        public int AliveChecks { get; private set; }

        public void Runner(string session, int pid, int hostPid, string started) => RunnerSessions.Add(new JsonObject
        {
            ["sessionId"] = session, ["pid"] = pid, ["hostPid"] = hostPid, ["status"] = "Running", ["startedAt"] = started, ["backend"] = "PtyHost",
        });

        public void Session(string id, JsonNode status, string started, string? ended, string cwd = AntiphonCwd) => Db["sessions"]!.AsArray().Add(new JsonObject
        {
            ["id"] = id, ["status"] = status, ["startedAt"] = started, ["endedAt"] = ended, ["cwd"] = cwd, ["agentKind"] = "ClaudeCode",
        });

        public void Agent(string id, string name, string session, bool pool, string status, string? slug = null) => Db["agents"]!.AsArray().Add(new JsonObject
        {
            ["id"] = id, ["name"] = name, ["slug"] = slug ?? name, ["isPoolDelegate"] = pool, ["status"] = status, ["persistentSessionId"] = session,
            ["workingDirectory"] = AntiphonCwd,
        });

        public void TaskRow(string id, string agent, string session, string status, string? completed, string? worktree, string workingDirectory = AntiphonCwd) =>
            Db["tasks"]!.AsArray().Add(new JsonObject
            {
                ["id"] = id, ["agentId"] = agent, ["agentSessionId"] = session, ["status"] = status, ["completedAt"] = completed, ["workspace"] = "Worktree",
                ["workingDirectory"] = worktree ?? workingDirectory, ["worktreePath"] = worktree,
            });

        public NightlyReportHttpResponse Http(string method, string uri, TimeSpan timeout)
        {
            if (method == "GET")
            {
                uri.ShouldEndWith("/sessions");
                timeout.ShouldBe(TimeSpan.FromSeconds(15));
                if (RunnerDown) throw new HttpRequestException("connection refused");
                return RunnerAnswer ?? new NightlyReportHttpResponse(200, RunnerSessions.ToJsonString());
            }
            method.ShouldBe("POST");
            timeout.ShouldBe(TimeSpan.FromSeconds(30));
            Posts.Add(uri);
            return KillAnswer?.Invoke(uri) ?? new NightlyReportHttpResponse(200, """{"ok":true}""");
        }

        public ZombieDatabaseAnswer Query(string container, string user, string database, string sql)
        {
            Queries++;
            (container, user, database).ShouldBe(("antiphon-postgres", "antiphon", "antiphon"));
            sql.ShouldContain("FROM \"AgentSessions\"");
            if (DbDown) return new ZombieDatabaseAnswer(1, "Error response from daemon: No such container: antiphon-postgres\n");
            return new ZombieDatabaseAnswer(0, DbText ?? Db.ToJsonString() + "\n");
        }

        public IReadOnlyList<ZombieProcess> Processes() => TableFailure != null ? throw TableFailure : Table;

        public void TreeKill(int pid) => TreeKills.Add(pid);

        public bool IsAlive(int pid)
        {
            AliveChecks++;
            return StillAlive.Contains(pid);
        }
    }

    private sealed class Work : IDisposable
    {
        public Work()
        {
            Root = Directory.CreateTempSubdirectory("zombie-agents-test-").FullName;
            Directory.CreateDirectory(SessionLog);
            Directory.CreateDirectory(Profile);
        }

        public string Root { get; }
        public string Reports => Path.Combine(Root, "reports");
        public string SessionLog => Path.Combine(Root, "session-runner");
        public string Profile => Path.Combine(Root, "profile");

        /// <summary>A Claude transcript of the cwd's project, last written at the time.</summary>
        public void Transcript(string cwd, string written)
        {
            var project = Directory.CreateDirectory(Path.Combine(Profile, ".claude", "projects", ZombieAgentReaper.ClaudeProjectDirectory(cwd))).FullName;
            var file = Path.Combine(project, "transcript.jsonl");
            File.WriteAllText(file, "{}\n");
            File.SetLastWriteTimeUtc(file, DateTime.Parse(written, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        }

        /// <summary>The session's ansi log, last written at the time.</summary>
        public void AnsiLog(string session, string written)
        {
            var file = Path.Combine(SessionLog, session.Replace("-", "") + ".ansi.log");
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, DateTime.Parse(written, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
