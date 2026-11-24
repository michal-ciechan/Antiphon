using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0204: the orphaned pty-host census and reaper (Antiphon.Nightly.PtyHostReaper, which replaced
/// scripts/reap-orphaned-pty-hosts.ps1 in unit 38 of the 2026-11-22 plan; the script had no driver) in process. Every observation is
/// the test's Machine: the runner's session list and kill endpoint, the database's answer, a fake process table and a liveness
/// answer; the manifests and ansi logs are a mock file system, and the clock is fake (the kill verification advances it). The
/// guards: a dry run unless --execute, a database row protects a host whatever else holds, every rule must hold, a prerequisite
/// that does not answer kills nothing, and a kill goes only through the runner. Nothing reads or kills a process of this machine.
/// </summary>
[Category("Unit")]
public sealed class PtyHostReaperTests
{
    private const string Now = "2026-11-24T12:00:00Z";
    private const string Runner = "http://localhost:17204";
    private static readonly string Logs = OperatingSystem.IsWindows() ? @"C:\fixture\session-runner" : "/fixture/session-runner";
    private static readonly string Manifests = Path.Combine(Logs, "pty-hosts", "manifests");
    private const string CheckInterpreter = @"C:\logs\antiphon\check-interpreter";
    private const string Diagnose = @"C:\logs\antiphon\diagnose";
    private const string Distiller = @"C:\logs\antiphon\output-distiller";
    private const string KindTest = @"C:\Users\op\AppData\Local\Temp\antiphon-kind-test-1a2b";

    [Test]
    public void A_dry_run_finds_each_test_launch_shape_and_kills_nothing()
    {
        var machine = new Machine();
        var check = machine.Orphan(1, CheckInterpreter, ageMinutes: 90);
        var diagnose = machine.Orphan(2, Diagnose, ageMinutes: 600);
        var distiller = machine.Orphan(3, Distiller, ageMinutes: 45);
        var kind = machine.Orphan(4, KindTest, ageMinutes: 2000, exe: @"C:\Users\op\.local\bin\claude.exe", childName: "claude.exe");
        var run = Reap(machine);

        run.Result.ExitCode.ShouldBe(0, run.Output);
        run.Result.Verdicts.Where(v => v.Orphan).Select(v => (v.SessionId, v.Rule)).ShouldBe([
            (check, "test-raw-check-interpreter"), (diagnose, "test-raw-diagnose"), (distiller, "test-raw-output-distiller"), (kind, "kind-test-temp-dir")], ignoreOrder: true);
        run.Result.Verdicts.ShouldAllBe(v => v.Reasons == "");
        run.Output.ShouldContain("Manifests: 4   protected (DB row): 0   positive orphans: 4   not touched: 0");
        run.Output.ShouldContain($"  {"kind-test-temp-dir",-30} 1\n  {"test-raw-check-interpreter",-30} 1\n  {"test-raw-diagnose",-30} 1\n"
            + $"  {"test-raw-output-distiller",-30} 1\n");
        run.Output.ShouldContain("Dry run: nothing was killed. Re-run with --execute to kill the positive orphans through the runner.");
        machine.Posts.ShouldBeEmpty();
        machine.Queries.Single().ShouldBe("select \"Id\" from \"AgentSessions\" where \"Id\" in ('" + string.Join("','", run.Result.Verdicts.Select(v => v.SessionId)) + "');");
        // The positives, the oldest first.
        var lines = run.Output.Split('\n').ToList();
        lines.FindIndex(line => line.StartsWith(kind, StringComparison.Ordinal)).ShouldBeLessThan(lines.FindIndex(line => line.StartsWith(diagnose, StringComparison.Ordinal)));
    }

    [Test]
    public void A_session_with_a_database_row_is_protected_and_never_killed()
    {
        var machine = new Machine();
        var known = machine.Orphan(1, CheckInterpreter, ageMinutes: 600);
        var orphan = machine.Orphan(2, CheckInterpreter, ageMinutes: 300);
        machine.Rows.Add(known.ToUpperInvariant());
        var run = Reap(machine, "--execute");

        run.Result.ExitCode.ShouldBe(0, run.Output);
        var row = run.Result.Verdicts.Single(v => v.SessionId == known);
        (row.Protected, row.Orphan, row.Reasons).ShouldBe((true, false, "R1 has AgentSessions row (protected)"));
        machine.Posts.ShouldBe([Runner + "/sessions/" + orphan + "/kill"]);
        run.Output.ShouldContain("--- protected (have an AgentSessions row; never touched) ---\nSessionId");
        run.Output.ShouldContain("Killed and verified: 1   failed: 0   protected: 1   not touched: 0");
    }

    [Test]
    public void Every_rule_must_hold_and_a_failed_rule_is_printed_and_never_killed()
    {
        var machine = new Machine();
        var expected = new Dictionary<string, string>
        {
            [machine.Orphan(1, CheckInterpreter, hostAlive: false)] = "R2 host pid not alive",
            [machine.Orphan(2, CheckInterpreter, hostName: "notepad.exe")] = "R2 host pid is notepad.exe, not a pty-host (pid reused)",
            [machine.Orphan(3, CheckInterpreter, hostStartSkewSeconds: 30)] =
                "R2 host start 2026-11-24T09:00:30Z does not match manifest 2026-11-24T09:00:00Z (pid reused)",
            [machine.Orphan(4, CheckInterpreter, childAlive: false)] = "R3 child pid not alive",
            [machine.Orphan(5, Diagnose, childName: "node.exe")] = "R3 child is node.exe, expected cmd.exe",
            [machine.Orphan(6, KindTest, childName: "cmd.exe")] = "R3 child is cmd.exe, expected claude",
            [machine.Orphan(7, CheckInterpreter, exited: true)] = "R4 manifest records an exit (host is lingering, will leave on its own)",
            [machine.Orphan(8, @"C:\src\antiphon", exe: @"C:\Users\op\.local\bin\claude.exe", childName: "claude.exe")] =
                "R5 shape not recognised (claude.exe in C:\\src\\antiphon)",
            [machine.Orphan(9, Distiller, ansiBytes: 513)] = "R6 ansi log is 513 bytes - something was typed or printed",
            [machine.Orphan(10, Distiller, ansiBytes: -1)] = "R6 ansi log missing",
            [machine.Orphan(11, CheckInterpreter, ageMinutes: 29)] = "R7 only 29 min old (< 30)",
            [machine.Orphan(12, CheckInterpreter, runnerStatus: "Stopped")] = "R8 runner status is 'Stopped', not Running",
            [machine.Orphan(13, CheckInterpreter, runnerStatus: null)] = "R8 runner status is '', not Running",
        };
        var run = Reap(machine, "--execute");

        run.Result.ExitCode.ShouldBe(0, run.Output);
        foreach (var (session, reason) in expected)
        {
            var verdict = run.Result.Verdicts.Single(v => v.SessionId == session);
            (verdict.Orphan, verdict.Protected, verdict.Reasons).ShouldBe((false, false, reason), session);
        }
        machine.Posts.ShouldBeEmpty("a host that fails any rule is never killed");
        run.Output.ShouldContain("Manifests: 13   protected (DB row): 0   positive orphans: 0   not touched: 13");
        run.Output.ShouldContain("Killed and verified: 0   failed: 0   protected: 0   not touched: 13");
    }

    [Test]
    public void Execute_kills_through_the_runner_and_verifies_the_host_is_gone()
    {
        var machine = new Machine();
        var orphan = machine.Orphan(1, CheckInterpreter, ageMinutes: 120);
        var run = Reap(machine, "--execute");

        run.Result.ExitCode.ShouldBe(0, run.Output);
        machine.Posts.ShouldBe([Runner + "/sessions/" + orphan + "/kill"]);
        run.Result.KillRequests.ShouldBe([orphan]);
        run.Output.ShouldContain("killed       " + orphan + "  test-raw-check-interpreter  host 9001 gone");
        run.Output.ShouldContain("Killed and verified: 1   failed: 0   protected: 0   not touched: 0");
        run.Output.ShouldContain("Live Antiphon.PtyHost processes now: 0");
        run.Output.ShouldNotContain("Dry run");
    }

    [Test]
    public void A_host_that_outlives_its_kill_request_fails_the_run()
    {
        var machine = new Machine();
        var orphan = machine.Orphan(1, CheckInterpreter, ageMinutes: 120);
        machine.StillAlive.Add(9001);
        var run = Reap(machine, "--execute", "--kill-verify-seconds", "3");

        run.Result.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("STILL ALIVE  " + orphan + "  host 9001 after 3s");
        run.Output.ShouldContain("Killed and verified: 0   failed: 1   protected: 0   not touched: 0");
        run.Output.ShouldContain("Live Antiphon.PtyHost processes now: 1");
        machine.AliveChecks.ShouldBe(6, "every 500 ms for 3 s");
    }

    [Test]
    public void A_refused_kill_request_fails_the_run_and_the_next_orphan_is_still_tried()
    {
        var machine = new Machine();
        var refused = machine.Orphan(1, CheckInterpreter, ageMinutes: 500);
        var next = machine.Orphan(2, Diagnose, ageMinutes: 100);
        machine.KillAnswer = uri => uri.Contains(refused, StringComparison.Ordinal) ? new NightlyReportHttpResponse(404, "{\"error\":\"unknown session\"}") : null;
        var run = Reap(machine, "--execute");

        run.Result.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("KILL FAILED  " + refused + "  POST /kill: HTTP 404 {\"error\":\"unknown session\"}");
        run.Output.ShouldContain("killed       " + next + "  test-raw-diagnose  host 9002 gone");
        machine.Posts.ShouldBe([Runner + "/sessions/" + refused + "/kill", Runner + "/sessions/" + next + "/kill"]);
    }

    [Test]
    public void Limit_kills_the_oldest_positives_first()
    {
        var machine = new Machine();
        var middle = machine.Orphan(1, CheckInterpreter, ageMinutes: 200);
        var oldest = machine.Orphan(2, CheckInterpreter, ageMinutes: 300);
        machine.Orphan(3, CheckInterpreter, ageMinutes: 100);
        var run = Reap(machine, "--execute", "--limit", "2");

        run.Result.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("--limit 2: killing the 2 oldest of 3 positive orphans this run.");
        machine.Posts.ShouldBe([Runner + "/sessions/" + oldest + "/kill", Runner + "/sessions/" + middle + "/kill"]);
    }

    [Test]
    [Arguments("runner", "ABORT: session-runner at http://localhost:17204 did not answer GET /sessions: HTTP 503 starting")]
    [Arguments("database", "ABORT: the database did not answer (exit 2).")]
    [Arguments("endpoint", "ABORT: the database did not answer: The running server2 PostgreSQL container must publish the endpoint")]
    [Arguments("table", "ABORT: the process table could not be read: no table here")]
    [Arguments("manifest", "ABORT: the manifest ")]
    [Arguments("directory", "ABORT: the manifest directory ")]
    public void A_prerequisite_that_does_not_answer_aborts_before_any_kill(string failure, string line)
    {
        var machine = new Machine();
        machine.Orphan(1, CheckInterpreter, ageMinutes: 120);
        switch (failure)
        {
            case "runner": machine.RunnerAnswer = new NightlyReportHttpResponse(503, "starting"); break;
            case "database": machine.DbExit = 2; break;
            case "endpoint": machine.DatabaseFailure = new InvalidOperationException("The running server2 PostgreSQL container must publish the endpoint"); break;
            case "table": machine.TableFailure = new PlatformNotSupportedException("no table here"); break;
            case "manifest": machine.Disk.AddFile(Path.Combine(Manifests, "bad.json"), new MockFileData("{\"sessionId\":\"not-a-guid\",\"hostPid\":1}")); break;
            case "directory": machine.Disk.Directory.Delete(Manifests, recursive: true); break;
        }
        var run = Reap(machine, "--execute");

        run.Result.ExitCode.ShouldBe(2, run.Output);
        run.Output.ShouldContain(line);
        run.Output.ShouldNotContain("psql: error: connection refused");
        machine.Posts.ShouldBeEmpty();
        run.Result.Verdicts.ShouldBeEmpty();
    }

    [Test]
    public void No_manifests_is_nothing_to_do_and_asks_the_database_nothing()
    {
        var machine = new Machine();
        var run = Reap(machine, "--execute");
        run.Result.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("No manifests under " + Manifests + " - nothing to do.");
        machine.Queries.ShouldBeEmpty();
    }

    [Test]
    public void The_options_are_the_scripts_parameters_in_kebab_case_and_the_docs_name_the_command()
    {
        var defaults = PtyHostReaperOptions.Parse([]);
        (defaults.Execute, defaults.RunnerUrl, defaults.SessionLogPath, defaults.CheckInterpreterDir, defaults.DiagnoseDir, defaults.OutputDistillerDir, defaults.PgContainer,
            defaults.MinAgeMinutes, defaults.PidReuseToleranceSec, defaults.MaxBannerOnlyAnsiBytes, defaults.KillVerifySeconds, defaults.Limit)
            .ShouldBe((false, Runner, @"C:\logs\antiphon\session-runner", CheckInterpreter, Diagnose, Distiller, "", 30, 5, 512, 20, 0));
        var given = PtyHostReaperOptions.Parse(["--runner-url", "http://127.0.0.1:9/", "--min-age-minutes", "5", "--max-banner-only-ansi-bytes", "300", "--execute"]);
        (given.RunnerUrl, given.MinAgeMinutes, given.MaxBannerOnlyAnsiBytes, given.Execute).ShouldBe(("http://127.0.0.1:9", 5, 300, true));
        Should.Throw<NightlyUsageException>(() => PtyHostReaperOptions.Parse(["-Execute"]));
        Should.Throw<NightlyUsageException>(() => PtyHostReaperOptions.Parse(["--limit", "all"]));

        var repo = DockerStackDocuments.RepoRoot;
        File.Exists(Path.Combine(repo, "scripts", "reap-orphaned-pty-hosts.ps1")).ShouldBeFalse("the reaper is a command");
        File.ReadAllBytes(Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "PtyHostReaper.cs")).ShouldAllBe(b => b < 128, "ASCII only");
        File.ReadAllText(Path.Combine(repo, "docs", "testing-and-build.md")).ShouldContain("scripts\\antiphon.cmd reap-orphaned-pty-hosts");
        File.ReadAllText(Path.Combine(repo, "docs", "ops-http.md")).ShouldContain("reap-orphaned-pty-hosts");
    }

    // ---- the machine ----

    private static (PtyHostReaperResult Result, string Output) Reap(Machine machine, params string[] args)
    {
        var output = new StringWriter();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(Now));
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
        var options = PtyHostReaperOptions.Parse(["--session-log-path", Logs, .. args]);
        var result = PtyHostReaper.Run(options, host, machine.Disk, machine);
        return (result, output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>The machine: the runner, the database, the process table, the manifests, and what --execute asked of it.</summary>
    private sealed class Machine : IPtyHostReaperSeams
    {
        public MockFileSystem Disk { get; } = new();
        public JsonArray RunnerSessions { get; } = [];
        public NightlyReportHttpResponse? RunnerAnswer { get; set; }
        public List<string> Rows { get; } = [];
        public int DbExit { get; set; }
        public InvalidOperationException? DatabaseFailure { get; set; }
        public List<string> Queries { get; } = [];
        public List<PtyHostProcess> Table { get; } = [];
        public Exception? TableFailure { get; set; }
        public Func<string, NightlyReportHttpResponse?>? KillAnswer { get; set; }
        public List<string> Posts { get; } = [];
        public HashSet<int> StillAlive { get; } = [];
        public HashSet<int> Gone { get; } = [];
        public int AliveChecks { get; private set; }

        public Machine() => Disk.AddDirectory(Manifests);

        /// <summary>
        /// One pty-host manifest whose every rule holds unless a parameter says otherwise: host 9000+n started at 09:00, child 9100+n,
        /// a banner-only ansi log, no exit, a Running runner session and no database row. Returns the session id.
        /// </summary>
        public string Orphan(int n, string cwd, int ageMinutes = 60, string exe = @"C:\Windows\system32\cmd.exe", string childName = "cmd.exe", bool hostAlive = true,
            string hostName = "Antiphon.PtyHost.exe", int hostStartSkewSeconds = 0, bool childAlive = true, bool exited = false, int ansiBytes = 164,
            string? runnerStatus = "Running")
        {
            var session = $"0000{n:D4}-0000-4000-8000-000000000204";
            var hostStart = DateTime.Parse("2026-11-24T09:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            var ansi = Path.Combine(Logs, session.Replace("-", "") + ".ansi.log");
            var manifest = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["sessionId"] = session,
                ["pipeName"] = "antiphon-pty-" + n,
                ["hostPid"] = 9000 + n,
                ["hostStartTimeUtc"] = "2026-11-24T09:00:00.0000000Z",
                ["childPid"] = 9100 + n,
                ["exe"] = exe,
                ["cwd"] = cwd,
                ["ansiLogPath"] = ansi,
                ["createdAtUtc"] = DateTime.Parse(Now, null, System.Globalization.DateTimeStyles.AdjustToUniversal).AddMinutes(-ageMinutes).ToString("o"),
                ["exitCode"] = exited ? 0 : null,
            };
            Disk.AddFile(Path.Combine(Manifests, session.Replace("-", "") + ".json"), new MockFileData(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
            if (ansiBytes >= 0) Disk.AddFile(ansi, new MockFileData(new byte[ansiBytes]));
            if (hostAlive) Table.Add(new PtyHostProcess(9000 + n, hostName, hostStart.AddSeconds(hostStartSkewSeconds)));
            if (childAlive) Table.Add(new PtyHostProcess(9100 + n, childName, hostStart));
            if (runnerStatus != null) RunnerSessions.Add(new JsonObject { ["sessionId"] = session, ["status"] = runnerStatus, ["hostPid"] = 9000 + n });
            return session;
        }

        public NightlyReportHttpResponse Http(string method, string uri, TimeSpan timeout)
        {
            if (method == "GET")
            {
                (uri, timeout).ShouldBe((Runner + "/sessions", PtyHostReaper.RunnerDeadline));
                return RunnerAnswer ?? new NightlyReportHttpResponse(200, RunnerSessions.ToJsonString());
            }
            method.ShouldBe("POST");
            timeout.ShouldBe(PtyHostReaper.KillDeadline);
            Posts.Add(uri);
            var refused = KillAnswer?.Invoke(uri);
            if (refused != null) return refused;
            var session = uri.Split('/')[^2];
            var host = 9000 + int.Parse(session[4..8], System.Globalization.CultureInfo.InvariantCulture);
            if (!StillAlive.Contains(host)) Gone.Add(host);
            return new NightlyReportHttpResponse(202, "");
        }

        public ZombieDatabaseAnswer Query(string container, string user, string database, string sql)
        {
            (container, user, database).ShouldBe(("", "antiphon", "antiphon"));
            Queries.Add(sql);
            if (DatabaseFailure != null) throw DatabaseFailure;
            return DbExit != 0 ? new ZombieDatabaseAnswer(DbExit, "psql: error: connection refused") : new ZombieDatabaseAnswer(0, string.Join("\n", Rows) + "\n");
        }

        public IReadOnlyList<PtyHostProcess> Processes() => TableFailure != null ? throw TableFailure : Table.ToArray();

        public bool IsAlive(int pid)
        {
            AliveChecks++;
            return Table.Any(row => row.ProcessId == pid) && !Gone.Contains(pid);
        }

        public int LivePtyHosts() => Table.Count(row => row.Name == "Antiphon.PtyHost.exe" && !Gone.Contains(row.ProcessId));
    }
}
