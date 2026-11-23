using System.IO.Abstractions.TestingHelpers;
using System.Xml;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The rc qualification skill's helpers trx-failures, watch-tasks and watch-master (TrxFailures, WatchTasks, WatchMaster),
/// in process. They replaced scripts/orchestration/trx-failures.ps1, watch-tasks.ps1 and watch-master.ps1 (unit 25 of the
/// 2026-11-22 plan); these cases are the C487 smoke rows of the deleted driver scripts/test-orchestration-watchers.ps1 (its
/// watch-qual rows moved to WatchQualTests in unit 6), under their row names, with new parse, scope and refusal cases. TRX
/// and state files are in a MockFileSystem, HTTP is a recording transport, git is a FakeGit and the clock a FakeTimeProvider
/// whose sleeps advance it, so nothing touches the network, a repository or a real wait.
/// Not ported: "helpers resolve PSScriptRoot outside param defaults for 5.1 -File" (a PowerShell 5.1 mechanic; the C#
/// takes the checkout from the CLI) and the helpers' PowerShell parse check (the ASCII rule stays, for the C# sources).
/// </summary>
[Category("Unit")]
public sealed class OrchestrationWatcherTests
{
    private const string ScopeId = "11111111-1111-1111-1111-111111111111";
    private const string StubApi = "http://stub.invalid";
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static string Repo(params string[] parts) => Path.Combine(new[] { DelegateScriptRunner.RepoRoot }.Concat(parts).ToArray());

    // A path under the mock file system's temp directory (the same shape on Windows and Linux).
    private static string P(MockFileSystem files, params string[] parts) => files.Path.Combine(new[] { files.Path.GetTempPath() }.Concat(parts).ToArray());

    private static string SyntheticTrx() => File.ReadAllText(Repo("tests", "Antiphon.Tests", "Scripts", "Fixtures", "orchestration-synthetic.trx"));

    private static string WithOutcome(string text, string outcome)
    {
        var xml = new XmlDocument();
        xml.LoadXml(text);
        ((XmlElement)xml.SelectSingleNode("//*[local-name()='UnitTestResult'][@outcome='Failed']")!).SetAttribute("outcome", outcome);
        return xml.OuterXml;
    }

    private static NightlyHost WatcherHost(StringWriter output, StringWriter error, FakeTimeProvider clock, FakeGit? git = null, List<TimeSpan>? sleeps = null) => new()
    {
        Clock = clock,
        Git = (git ?? new FakeGit()).Run,
        Launcher = new RecordingLauncher(),
        Processes = new FakeProcessTable(),
        Probe = new FakeHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Sleep = span =>
        {
            if (sleeps == null) throw new InvalidOperationException("no sleep expected");
            sleeps.Add(span);
            clock.Advance(span);
        },
    };

    private static string Trx(MockFileSystem files, params string[] paths)
    {
        var output = new StringWriter();
        TrxFailures.Run(paths, WatcherHost(output, new StringWriter(), new FakeTimeProvider(Start)), files).ShouldBe(0, "trx-failures always answers 0");
        return output.ToString().Replace("\r\n", "\n").TrimEnd('\n');
    }

    // ---- trx-failures ----

    [Test]
    [Arguments("Timeout")]
    [Arguments("Aborted")]
    [Arguments("Error")]
    public void Extractor_reports_the_outcome_as_a_failure(string outcome)
    {
        // RCQUAL extractor reports Timeout/Aborted/Error as a failure.
        var files = new MockFileSystem();
        files.AddFile(P(files, "logs", outcome + ".trx"), new MockFileData(WithOutcome(SyntheticTrx(), outcome)));
        var output = Trx(files, P(files, "logs", outcome + ".trx"));
        output.ShouldContain("TEST Synthetic.WindowsInputFails");
        output.ShouldNotContain("no failures");
    }

    [Test]
    public void Extracts_failed_test_and_duration_only_with_message_three_frames_and_stdout()
    {
        // RCQUAL extracts failed test and duration only; RCQUAL extracts message three frames and stdout.
        var files = new MockFileSystem();
        files.AddFile(P(files, "logs", "synthetic.trx"), new MockFileData(SyntheticTrx()));
        var output = Trx(files, P(files, "logs", "synthetic.trx"));
        output.ShouldContain("TEST Synthetic.WindowsInputFails");
        output.ShouldNotContain("TEST Synthetic.Passes");
        output.ShouldContain("DURATION 00:00:01.2500000");
        output.ShouldContain("Expected Windows-shaped input to remain intact.");
        output.ShouldContain("Synthetic.Third()");
        output.ShouldNotContain("Synthetic.Fourth()");
        output.ShouldContain("Synthetic diagnostic output only.");
        output.Split('\n').ShouldBe([
            "FILE " + P(files, "logs", "synthetic.trx"),
            "TEST Synthetic.WindowsInputFails",
            "DURATION 00:00:01.2500000",
            "MESSAGE Expected Windows-shaped input to remain intact.",
            "STACK (first 3 frames)",
            @"   at Synthetic.First() in C:\synthetic\Test.cs:line 10",
            "   at Synthetic.Second()",
            "   at Synthetic.Third()",
            "STDOUT Synthetic diagnostic output only.",
        ]);
    }

    [Test]
    public void Bounds_the_same_message_string_and_stdout()
    {
        var xml = new XmlDocument();
        xml.LoadXml(SyntheticTrx());
        xml.SelectSingleNode("//*[local-name()='Message']")!.InnerText = new string(' ', 1600) + "MESSAGE-END";
        xml.SelectSingleNode("//*[local-name()='StdOut']")!.InnerText = new string('s', 2600) + "STDOUT-END";
        var files = new MockFileSystem();
        files.AddFile(P(files, "scratch", "long.trx"), new MockFileData(xml.OuterXml));
        var output = Trx(files, P(files, "scratch", "long.trx"));
        output.ShouldContain("MESSAGE " + new string(' ', 1500) + "\n");
        output.ShouldNotContain("MESSAGE-END");
        output.ShouldContain("STDOUT " + new string('s', 2500));
        output.ShouldNotContain("STDOUT-END");
        output.ShouldNotContain("TRX-READ-ERROR");
    }

    [Test]
    public void Non_dotnet_stack_keeps_its_first_three_lines()
    {
        var xml = new XmlDocument();
        xml.LoadXml(SyntheticTrx());
        xml.SelectSingleNode("//*[local-name()='StackTrace']")!.InnerText = "one\n\ntwo\nthree\nfour";
        var files = new MockFileSystem();
        files.AddFile(P(files, "scratch", "js.trx"), new MockFileData(xml.OuterXml));
        var output = Trx(files, P(files, "scratch", "js.trx")).Split('\n');
        var stack = Array.IndexOf(output, "STACK (first 3 frames)");
        output.Skip(stack + 1).Take(4).ShouldBe(["one", "two", "three", "STDOUT Synthetic diagnostic output only."]);
    }

    [Test]
    public void Reports_no_failures_with_zero_exit()
    {
        var files = new MockFileSystem();
        files.AddFile(P(files, "scratch", "green.trx"), new MockFileData(WithOutcome(SyntheticTrx(), "Passed")));
        Trx(files, P(files, "scratch", "green.trx")).ShouldBe("no failures");
    }

    [Test]
    public void Continues_across_bad_missing_and_valid_inputs()
    {
        var files = new MockFileSystem();
        files.AddFile(P(files, "logs", "incomplete.trx"), new MockFileData("<TestRun"));
        files.AddFile(P(files, "logs", "synthetic.trx"), new MockFileData(SyntheticTrx()));
        var output = Trx(files, P(files, "logs", "incomplete.trx"), P(files, "logs", "synthetic.trx"), P(files, "scratch", "missing.trx"));
        output.ShouldContain("TRX-READ-ERROR " + P(files, "logs", "incomplete.trx") + ": ");
        output.ShouldContain("TRX-READ-ERROR " + P(files, "scratch", "missing.trx") + ": ");
        output.ShouldContain("TEST Synthetic.WindowsInputFails");
    }

    [Test]
    public void No_input_and_unreadable_input_are_diagnostic_not_a_green_result()
    {
        // RCQUAL no input is diagnostic not a green result; RCQUAL unreadable input is diagnostic not a green result.
        var files = new MockFileSystem();
        Trx(files).ShouldBe("TRX-READ-ERROR no input paths supplied.");
        files.AddFile(P(files, "logs", "incomplete.trx"), new MockFileData("<TestRun"));
        var unreadable = Trx(files, P(files, "logs", "incomplete.trx"));
        unreadable.ShouldStartWith("TRX-READ-ERROR " + P(files, "logs", "incomplete.trx") + ": ");
        unreadable.ShouldNotContain("no failures");
        files.AddFile(P(files, "logs", "other.trx"), new MockFileData("<TestRun xmlns=\"urn:other\" />"));
        Trx(files, P(files, "logs", "other.trx")).ShouldBe("TRX-READ-ERROR " + P(files, "logs", "other.trx") + ": Not a supported TRX TestRun.");
    }

    [Test]
    public void Extractor_accepts_separate_positional_paths()
    {
        var files = new MockFileSystem();
        var paths = TrxFailures.Parse([P(files, "scratch", "green.trx"), P(files, "logs", "synthetic.trx")]);
        paths.ShouldBe([P(files, "scratch", "green.trx"), P(files, "logs", "synthetic.trx")]);
        files.AddFile(P(files, "scratch", "green.trx"), new MockFileData(WithOutcome(SyntheticTrx(), "Passed")));
        files.AddFile(P(files, "logs", "synthetic.trx"), new MockFileData(SyntheticTrx()));
        var output = Trx(files, paths.ToArray());
        output.ShouldContain("FILE " + P(files, "logs", "synthetic.trx"));
        output.ShouldContain("TEST Synthetic.WindowsInputFails");
        output.ShouldNotContain("TRX-READ-ERROR");
        Should.Throw<NightlyUsageException>(() => TrxFailures.Parse(["--path", "a.trx"])).Message.ShouldContain("--path");
    }

    // ---- watch-tasks ----

    private sealed record TaskWatch(int Exit, string Output, string Error);

    // The task list as the stub answers it: one task, the status the test sets.
    private static RecordingOperatorHttp TaskList(Func<string> body) => new(call =>
    {
        call.Uri.ShouldBe(StubApi + "/api/agent-tasks?projectId=" + ScopeId, "a scoped request only");
        return new NightlyReportHttpResponse(200, body());
    });

    private static string OneTask(string status) => """{"items":[{"id":"synthetic-task","status":"STATUS","role":"Debug","title":"fixture"}]}""".Replace("STATUS", status);

    private static TaskWatch WatchTasksOnce(MockFileSystem files, RecordingOperatorHttp http, params string[] extra)
    {
        string[] arguments = ["--project-id", ScopeId, "--state-file", P(files, "scratch", "tasks.json"), "--api", StubApi, "--once", .. extra];
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = WatchTasks.Run(WatchTasksOptions.Parse(arguments, null, null), WatcherHost(output, error, new FakeTimeProvider(Start)), files, http.Transport);
        return new TaskWatch(exit, output.ToString(), error.ToString());
    }

    [Test]
    public void Task_baseline_uses_scoped_envelope_then_rearm_reports_new_transitions_once()
    {
        var files = new MockFileSystem();
        files.AddDirectory(P(files, "scratch"));
        var status = "Blocked";
        var http = TaskList(() => OneTask(status));

        // RCQUAL task baseline uses scoped envelope.
        var baseline = WatchTasksOnce(files, http);
        baseline.Exit.ShouldBe(0, baseline.Error);
        baseline.Output.ShouldNotContain("TASK-BLOCKED");
        baseline.Output.ShouldNotContain("TASK-WATCH-ERROR");
        baseline.Output.ShouldContain("watch-tasks window ended; re-arm");
        files.File.ReadAllText(P(files, "scratch", "tasks.json")).Trim().ShouldBe("""{"scope":"projectId=11111111-1111-1111-1111-111111111111","lastStatus":{"synthetic-task":"Blocked"}}""");

        // RCQUAL task rearm emits new failed transition; RCQUAL task rearm suppresses duplicate transition.
        status = "Failed";
        WatchTasksOnce(files, http).Output.ShouldContain("TASK-FAILED synthetic-task Debug fixture");
        WatchTasksOnce(files, http).Output.ShouldNotContain("TASK-FAILED");
        http.Calls.Count.ShouldBe(3);
    }

    [Test]
    [Arguments("Blocked")]
    [Arguments("Failed")]
    public void Task_rearm_emits_the_repeat_after_Working_and_suppresses_an_unchanged_repeat(string terminal)
    {
        var files = new MockFileSystem();
        files.AddDirectory(P(files, "scratch"));
        var status = terminal;
        var http = TaskList(() => OneTask(status));
        WatchTasksOnce(files, http);
        status = "Working";
        var working = WatchTasksOnce(files, http).Output;
        status = terminal;
        var changed = WatchTasksOnce(files, http).Output;
        var unchanged = WatchTasksOnce(files, http).Output;
        working.ShouldNotContain("TASK-WORKING");
        changed.ShouldContain("TASK-" + terminal.ToUpperInvariant() + " synthetic-task");
        unchanged.ShouldNotContain("TASK-" + terminal.ToUpperInvariant());
    }

    [Test]
    public void Task_watcher_uses_API_and_token_environment_defaults()
    {
        var files = new MockFileSystem();
        files.AddDirectory(P(files, "scratch"));
        var http = TaskList(() => OneTask("Blocked"));
        // The CLI passes ANTIPHON_API and ANTIPHON_TASK_TOKEN in; no --api.
        var options = WatchTasksOptions.Parse(["--project-id", ScopeId, "--state-file", P(files, "scratch", "tasks.json"), "--once"], StubApi + "/", "synthetic-offline-token");
        var output = new StringWriter();
        WatchTasks.Run(options, WatcherHost(output, new StringWriter(), new FakeTimeProvider(Start)), files, http.Transport).ShouldBe(0);
        output.ToString().ShouldNotContain("TASK-WATCH-ERROR");
        http.Calls.ShouldHaveSingleItem().Headers[OperatorApi.TaskTokenHeader].ShouldBe("synthetic-offline-token");
        WatchTasksOptions.Parse(["--project-id", ScopeId, "--state-file", "s.json"], null, null).Api.ShouldBe(OperatorApi.DefaultApi);
        WatchTasksOptions.Parse(["--project-id", ScopeId, "--state-file", "s.json"], null, null).TaskToken.ShouldBe("");
    }

    [Test]
    public void Task_watcher_explicitly_baselines_legacy_state()
    {
        var files = new MockFileSystem();
        files.AddFile(P(files, "scratch", "tasks.json"), new MockFileData("""{"scope":"projectId=11111111-1111-1111-1111-111111111111","seen":["synthetic-task|Blocked","synthetic-task|Failed"]}"""));
        var run = WatchTasksOnce(files, TaskList(() => OneTask("Failed")));
        run.Output.ShouldContain("TASK-STATE-UPGRADE");
        run.Output.ShouldNotContain("TASK-FAILED");
        files.File.ReadAllText(P(files, "scratch", "tasks.json")).ShouldContain("\"lastStatus\":{\"synthetic-task\":\"Failed\"}");
    }

    [Test]
    public void Task_watcher_reports_an_invalid_envelope_and_a_failed_request_and_keeps_going()
    {
        var files = new MockFileSystem();
        files.AddDirectory(P(files, "scratch"));
        WatchTasksOnce(files, TaskList(() => """{"invalid":true}""")).Output.ShouldContain("TASK-WATCH-ERROR Task list envelope has no items.");
        var failed = WatchTasksOnce(files, new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(503, "busy")));
        failed.Exit.ShouldBe(0);
        failed.Output.ShouldContain("TASK-WATCH-ERROR HTTP 503 busy");
        files.File.Exists(P(files, "scratch", "tasks.json")).ShouldBeFalse("a failed poll writes no state");
    }

    [Test]
    public void Task_watcher_refuses_a_state_of_another_scope_or_without_a_map()
    {
        var files = new MockFileSystem();
        files.AddFile(P(files, "scratch", "tasks.json"), new MockFileData("""{"scope":"boardId=22222222-2222-2222-2222-222222222222","lastStatus":{}}"""));
        var http = TaskList(() => OneTask("Failed"));
        var other = WatchTasksOnce(files, http);
        other.Exit.ShouldBe(1);
        other.Error.ShouldContain("Watcher state scope differs; use a separate StateFile.");
        files.File.WriteAllText(P(files, "scratch", "tasks.json"), """{"scope":"projectId=11111111-1111-1111-1111-111111111111"}""");
        WatchTasksOnce(files, http).Error.ShouldContain("Watcher state has no lastStatus map.");
        files.File.WriteAllText(P(files, "scratch", "tasks.json"), "{not json");
        WatchTasksOnce(files, http).Error.ShouldContain("state-file-unreadable");
        http.Calls.ShouldBeEmpty("an unusable state stops the watch before any request");
    }

    [Test]
    public void Task_watcher_scopes_a_board_and_takes_exactly_one_scope()
    {
        var options = WatchTasksOptions.Parse(["--board-id", "22222222-2222-2222-2222-222222222222", "--state-file", "s.json"], null, null);
        options.Scope.ShouldBe("boardId=22222222-2222-2222-2222-222222222222");
        Should.Throw<NightlyUsageException>(() => WatchTasksOptions.Parse(["--state-file", "s.json"], null, null));
        Should.Throw<NightlyUsageException>(() => WatchTasksOptions.Parse(["--project-id", ScopeId, "--board-id", ScopeId, "--state-file", "s.json"], null, null));
        Should.Throw<NightlyUsageException>(() => WatchTasksOptions.Parse(["--project-id", "not-a-guid", "--state-file", "s.json"], null, null));
        Should.Throw<NightlyUsageException>(() => WatchTasksOptions.Parse(["--project-id", ScopeId, "--state-file", "s.json", "--max-minutes", "30"], null, null));
    }

    // ---- watch-master ----

    private static string WatchMasterOnce(FakeGit git, string from)
    {
        var output = new StringWriter();
        var options = WatchMasterOptions.Parse(["--from", from, "--once"], @"C:\scratch\repo");
        WatchMaster.Run(options, WatcherHost(output, new StringWriter(), new FakeTimeProvider(Start), git)).ShouldBe(0);
        return output.ToString();
    }

    [Test]
    public void Master_watcher_reports_remote_movement_and_ignores_an_unchanged_remote()
    {
        var git = new FakeGit(new string('b', 40) + "\trefs/heads/master" + Environment.NewLine);
        var moved = WatchMasterOnce(git, new string('a', 40));
        moved.ShouldContain("MASTER MOVED " + new string('b', 40));
        moved.ShouldContain("master watch done; re-arm if still waiting");
        WatchMasterOnce(git, new string('b', 40)).ShouldNotContain("MASTER MOVED");
        WatchMasterOnce(git, new string('B', 40)).ShouldNotContain("MASTER MOVED", Case.Sensitive, "the SHA is compared ignoring case");
        var call = git.Calls[0];
        call.WorkingDirectory.ShouldBe(@"C:\scratch\repo");
        call.Arguments.ShouldBe(["ls-remote", "origin", "refs/heads/master"]);
    }

    [Test]
    public void Master_watcher_reports_transport_failure_and_a_missing_ref()
    {
        var failed = WatchMasterOnce(new FakeGit("synthetic transport failure", exitCode: 1), new string('a', 40));
        failed.ShouldContain("MASTER-WATCH-ERROR git ls-remote failed.");
        failed.ShouldNotContain("MASTER MOVED");
        WatchMasterOnce(new FakeGit("", exitCode: 0), new string('a', 40)).ShouldContain("MASTER-WATCH-ERROR Remote returned no valid master ref.");
    }

    [Test]
    public void Master_watcher_accepts_successful_git_with_stderr_warning()
    {
        // The git seam reads stderr and stdout together: a warning line beside the ref is not fatal.
        var git = new FakeGit("synthetic git warning" + Environment.NewLine + new string('b', 40) + "\trefs/heads/master" + Environment.NewLine);
        var output = WatchMasterOnce(git, new string('a', 40));
        output.ShouldContain("MASTER MOVED " + new string('b', 40));
        output.ShouldNotContain("MASTER-WATCH-ERROR");
    }

    [Test]
    public void Master_watcher_takes_a_full_sha_and_defaults_its_repository_to_the_checkout()
    {
        Should.Throw<NightlyUsageException>(() => WatchMasterOptions.Parse(["--from", "abc123"], "repo")).Message.ShouldContain("40-hex");
        Should.Throw<NightlyUsageException>(() => WatchMasterOptions.Parse(["--from", new string('a', 40)], null)).Message.ShouldContain("--repository");
        var options = WatchMasterOptions.Parse(["--from", new string('a', 40)], "checkout");
        (options.Repository, options.Remote, options.PollSeconds, options.MaxMinutes, options.Once).ShouldBe(("checkout", "origin", 30, 28, false));
        WatchMasterOptions.Parse(["--from", new string('a', 40), "--repository", "other", "--remote", "upstream"], "checkout").Repository.ShouldBe("other");
        Should.Throw<NightlyUsageException>(() => WatchMasterOptions.Parse(["--from", new string('a', 40), "--poll-seconds", "0"], "checkout"));
    }

    // ---- both watchers' window ----

    [Test]
    public void Watchers_clamp_sleep_to_the_remaining_window()
    {
        // RCQUAL watch-tasks.ps1 / watch-master.ps1 clamps sleep to remaining window: a 1-minute window and 300-second polls.
        var files = new MockFileSystem();
        files.AddDirectory(P(files, "scratch"));
        var clock = new FakeTimeProvider(Start);
        var sleeps = new List<TimeSpan>();
        var output = new StringWriter();
        var tasks = WatchTasksOptions.Parse(["--project-id", ScopeId, "--state-file", P(files, "scratch", "tasks.json"), "--api", StubApi, "--max-minutes", "1", "--poll-seconds", "300"], null, null);
        WatchTasks.Run(tasks, WatcherHost(output, new StringWriter(), clock, sleeps: sleeps), files, TaskList(() => OneTask("Blocked")).Transport).ShouldBe(0);
        sleeps.ShouldHaveSingleItem().ShouldBe(TimeSpan.FromMinutes(1));
        output.ToString().ShouldContain("watch-tasks window ended; re-arm");

        sleeps.Clear();
        var master = WatchMasterOptions.Parse(["--from", new string('b', 40), "--max-minutes", "1", "--poll-seconds", "300"], @"C:\scratch\repo");
        var git = new FakeGit(new string('b', 40) + "\trefs/heads/master");
        WatchMaster.Run(master, WatcherHost(new StringWriter(), new StringWriter(), new FakeTimeProvider(Start), git, sleeps)).ShouldBe(0);
        sleeps.ShouldHaveSingleItem().ShouldBe(TimeSpan.FromMinutes(1));
        git.Calls.Count.ShouldBe(1, "the window ends at the first sleep's end");
    }

    // ---- the skill and the sources ----

    [Test]
    public void Skill_uses_supported_Monitor_arguments_and_repository_watchers()
    {
        var skill = File.ReadAllText(Repo(".claude", "skills", "antiphon-rc-qualification", "SKILL.md"));
        skill.ShouldContain("`timeout_ms: 1800000`");
        skill.ShouldNotContain("persistent:");
        skill.ShouldContain("REPLACE any private watcher copies");
        foreach (var command in new[] { "watch-tasks --project-id", "watch-qual --log-dir", "watch-master --from", "trx-failures " })
            skill.ShouldContain("scripts\\antiphon.cmd " + command);
        skill.ShouldNotContain("scripts/orchestration/");
    }

    [Test]
    public void Ported_sources_are_ascii_and_have_no_twin()
    {
        foreach (var path in new[] { "scripts/lib/Antiphon.Nightly/TrxFailures.cs", "scripts/lib/Antiphon.Nightly/WatchTasks.cs", "scripts/lib/Antiphon.Nightly/WatchMaster.cs",
                     "scripts/lib/Antiphon.Scripts/AgentHelperCommands.cs" })
            File.ReadAllBytes(Repo(path.Split('/'))).Count(b => b > 127).ShouldBe(0, path + " is ASCII-only");
        foreach (var path in new[] { "scripts/orchestration/trx-failures.ps1", "scripts/orchestration/watch-tasks.ps1", "scripts/orchestration/watch-master.ps1",
                     "scripts/test-orchestration-watchers.ps1" })
            File.Exists(Repo(path.Split('/'))).ShouldBeFalse(path + " is ported to C#");
    }
}
