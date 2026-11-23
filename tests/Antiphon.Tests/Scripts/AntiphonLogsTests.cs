using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The logs command (AntiphonLogs.Run), in process. It replaced scripts/logs.ps1 (unit 25 of the 2026-11-22 plan), which had
/// no test of its own. The checkouts and log files are in a MockFileSystem, git a FakeGit (the primary worktree is its first
/// listed record), HTTP a recording transport and ssh a RecordingLauncher, so nothing reads a real log, server or host.
/// </summary>
[Category("Unit")]
public sealed class AntiphonLogsTests
{
    private const string Api = "http://127.0.0.1:17202";
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private sealed record LogsRun(int Exit, string[] Lines, string Error, FakeGit Git, RecordingLauncher Launcher, RecordingOperatorHttp Http);

    /// <summary>A linked worktree (the CLI's checkout) of a primary checkout, each with its logs, and a temp root.</summary>
    private sealed class LogsWorld
    {
        public LogsWorld()
        {
            Main = P("work", "antiphon");
            Linked = P("work", "antiphon-wt");
            Temp = P("temp");
            Pty = P("pty-hosts", "logs");
            Files.AddDirectory(Main);
            Files.AddDirectory(Linked);
            Files.AddDirectory(Temp);
        }

        public MockFileSystem Files { get; } = new();

        public string Main { get; }

        public string Linked { get; }

        public string Temp { get; }

        public string Pty { get; }

        public string P(params string[] parts) => Files.Path.Combine(new[] { Files.Path.GetTempPath() }.Concat(parts).ToArray());

        public void Log(string path, string text, int minutesOld = 0)
        {
            Files.AddFile(path, new MockFileData(text));
            Files.File.SetLastWriteTimeUtc(path, Now.AddMinutes(-minutesOld));
        }

        public LogsRun Run(string source, int tail = 100, Func<RecordingOperatorHttp.Call, NightlyReportHttpResponse>? answer = null, int sshExit = 0,
            string? tokenFile = null, int gitExit = 0)
        {
            var git = new FakeGit();
            git.Reply("--show-toplevel", new NightlyGitResult(gitExit, gitExit == 0 ? Linked + Environment.NewLine : "fatal: not a git repository" + Environment.NewLine));
            git.Reply("list", new NightlyGitResult(0, "worktree " + Main + "\nHEAD " + FakeGit.DefaultSha + "\nbranch refs/heads/master\n\nworktree "
                + Linked + "\nHEAD " + FakeGit.DefaultSha + "\n"));
            var launcher = new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = sshExit });
            var http = new RecordingOperatorHttp(answer ?? (call => throw new InvalidOperationException("no request expected: " + call.Uri)));
            var output = new StringWriter();
            var error = new StringWriter();
            var host = new NightlyHost
            {
                Clock = new FakeTimeProvider(new DateTimeOffset(Now)),
                Git = git.Run,
                Launcher = launcher,
                Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe(),
                Out = output,
                Error = error,
                Trace = TextWriter.Null,
            };
            var parsed = AntiphonLogsOptions.Parse(["--source", source, "--tail", tail.ToString()], Linked, Temp, Api, "task-token", tokenFile ?? P("operator-token"));
            var options = new AntiphonLogsOptions
            {
                Source = parsed.Source, Tail = parsed.Tail, Checkout = parsed.Checkout, TempRoot = parsed.TempRoot, PtyHostLogs = Pty, Api = parsed.Api,
                TaskToken = parsed.TaskToken, OperatorTokenFile = parsed.OperatorTokenFile,
            };
            var exit = AntiphonLogs.Run(options, host, Files, http.Transport);
            var lines = output.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return new LogsRun(exit, lines, error.ToString().Trim(), git, launcher, http);
        }
    }

    [Test]
    public void Desktop_server_tails_the_newest_server_log_of_the_primary_worktree()
    {
        var world = new LogsWorld();
        var logs = world.Files.Path.Combine(world.Main, "server", "logs");
        world.Log(world.Files.Path.Combine(logs, "antiphon-20261122.log"), "old 1\nold 2\n", minutesOld: 60);
        var newest = world.Files.Path.Combine(logs, "antiphon-20261123.log");
        world.Log(newest, "line 1\nline 2\nline 3\n");
        world.Log(world.Files.Path.Combine(world.Linked, "server", "logs", "antiphon-20261124.log"), "the linked worktree's log\n");

        var run = world.Run("Desktop-Server", tail: 2);

        run.Exit.ShouldBe(0, run.Error);
        run.Lines.ShouldBe(["--- " + newest + " ---", "line 2", "line 3"]);
        run.Git.Calls.Select(call => call.Line).ShouldBe(["GIT rev-parse --show-toplevel", "GIT worktree list --porcelain"]);
        run.Git.Calls[0].WorkingDirectory.ShouldBe(world.Linked);
    }

    [Test]
    public void Apphost_and_fake_gateway_tail_their_file_and_a_missing_file_fails()
    {
        var world = new LogsWorld();
        world.Log(world.Files.Path.Combine(world.Main, "logs", "apphost.log"), "a\nb\nc");
        world.Run("apphost", tail: 2).Lines.ShouldBe(["b", "c"]);
        var missing = world.Run("fake-gateway");
        missing.Exit.ShouldBe(1);
        missing.Error.ShouldBe("Log file not found: " + world.Files.Path.Combine(world.Main, "logs", "fake-gateway.log") + ".");
    }

    [Test]
    public void Session_runner_reads_the_supervisor_file_then_the_newest_runner_serilog()
    {
        var world = new LogsWorld();
        world.Log(world.Files.Path.Combine(world.Main, "logs", "session-runner.log"), "supervisor\n");
        world.Run("session-runner").Lines.ShouldBe(["supervisor"], "no runner Serilog file yet");
        var serilog = world.Files.Path.Combine(world.Temp, "antiphon-logs", "session-runner-20261123.log");
        world.Log(world.Files.Path.Combine(world.Temp, "antiphon-logs", "session-runner-20261122.log"), "older\n", minutesOld: 30);
        world.Log(serilog, "runner\n");
        world.Run("session-runner").Lines.ShouldBe(["supervisor", "--- " + serilog + " ---", "runner"]);
    }

    [Test]
    public void Pty_host_tails_the_newest_log_and_reports_when_there_is_none()
    {
        var world = new LogsWorld();
        var none = world.Run("pty-host");
        (none.Exit, none.Error).ShouldBe((1, "No log files found at " + world.Files.Path.Combine(world.Pty, "*.log") + "."));
        none.Git.Calls.ShouldBeEmpty("the pty host logs are not under the checkout");
        var log = world.Files.Path.Combine(world.Pty, "pty-1.log");
        world.Log(log, "pty start\n");
        world.Run("pty-host").Lines.ShouldBe(["--- " + log + " ---", "pty start"]);
    }

    [Test]
    public void Hangfire_sends_the_operator_token_and_names_the_dashboard_command()
    {
        var world = new LogsWorld();
        world.Files.AddFile(world.P("operator-token"), new MockFileData("operator-secret\n"));
        var run = world.Run("hangfire", answer: _ => new NightlyReportHttpResponse(200, "<html>dashboard</html>"));

        run.Exit.ShouldBe(0, run.Error);
        run.Lines.ShouldBe(["Hangfire dashboard: HTTP 200. Open it with scripts/antiphon hangfire-dashboard and inspect Failed and Recurring Jobs; its in-memory history is not exposed as a log file."]);
        var call = run.Http.Calls.ShouldHaveSingleItem();
        (call.Method, call.Uri).ShouldBe(("GET", Api + "/hangfire"));
        call.Headers[OperatorApi.OperatorTokenHeader].ShouldBe("operator-secret");
        string.Join("\n", run.Lines).ShouldNotContain("operator-secret");

        var unauthorized = world.Run("hangfire", answer: _ => new NightlyReportHttpResponse(401, ""));
        unauthorized.Lines.ShouldHaveSingleItem().ShouldStartWith("Hangfire dashboard: HTTP 401.");
        var noToken = world.Run("hangfire", tokenFile: world.P("missing-token"));
        noToken.Exit.ShouldBe(1);
        noToken.Error.ShouldStartWith("Operator token file not found at ");
    }

    [Test]
    public void Transcripts_print_each_live_session_tail_as_json_lines()
    {
        var world = new LogsWorld();
        var run = world.Run("transcripts", tail: 2, answer: call => call.Path switch
        {
            "/api/agents" => new NightlyReportHttpResponse(200, """[{"name":"idle","liveSession":null},{"name":"Sol","liveSession":{"id":"s-1"}}]"""),
            "/api/sessions/s-1/transcript?since=0" => new NightlyReportHttpResponse(200, """{"entries":[{"seq":1},{"seq":2},{"seq":3,"text":"done"}]}"""),
            _ => throw new InvalidOperationException("unexpected " + call.Uri),
        });

        run.Exit.ShouldBe(0, run.Error);
        run.Lines.ShouldBe(["--- Sol s-1 ---", """{"seq":2}""", """{"seq":3,"text":"done"}"""]);
        run.Http.Calls.ShouldAllBe(call => call.Headers[OperatorApi.TaskTokenHeader] == "task-token");

        var none = world.Run("transcripts", answer: _ => new NightlyReportHttpResponse(200, """[{"name":"idle"}]"""));
        (none.Exit, none.Error).ShouldBe((1, "No live agent sessions were returned; use the session id from an agent/task record for a historical transcript."));
        var failed = world.Run("transcripts", answer: _ => new NightlyReportHttpResponse(500, "boom"));
        (failed.Exit, failed.Error).ShouldBe((1, "Antiphon GET /api/agents failed: HTTP 500 boom"));
    }

    [Test]
    public void Server2_runner_runs_ssh_on_this_console_and_fails_on_its_exit()
    {
        var world = new LogsWorld();
        var run = world.Run("server2-runner", tail: 50);
        run.Exit.ShouldBe(0, run.Error);
        var ssh = run.Launcher.Launches.ShouldHaveSingleItem();
        ssh.FileName.ShouldBe("ssh");
        ssh.Arguments.ShouldBe(["-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2",
            "docker logs --tail 50 antiphon-runner-session-runner-1; docker exec antiphon-runner-session-runner-1 sh -lc 'tail -n 50 /state/runner-logs/session-runner-*.log /state/logs/dockerd.log 2>&1'"]);
        (ssh.Custody, ssh.CaptureOutput).ShouldBe((NightlyCustody.Watched, false));

        var failed = world.Run("server2-runner", sshExit: 255);
        (failed.Exit, failed.Error).ShouldBe((1, "server2 runner log retrieval failed (ssh/docker exit 255)."));
    }

    [Test]
    public void Server2_deploy_inventories_then_reports_the_evidence_gap()
    {
        var world = new LogsWorld();
        var run = world.Run("server2-deploy");
        (run.Exit, run.Error).ShouldBe((1, "No durable server2 deployment-evidence location is configured; see docs/logs.md."));
        run.Launcher.Launches.ShouldHaveSingleItem().Arguments[^1].ShouldContain("-path /home/mc/antiphon-server2/secrets -prune");
        var failed = world.Run("server2-deploy", sshExit: 255);
        failed.Error.ShouldBe("server2 deployment-evidence inventory failed (ssh exit 255).");
    }

    [Test]
    public void A_checkout_git_cannot_read_fails_and_the_flags_are_checked()
    {
        var world = new LogsWorld();
        var run = world.Run("apphost", gitExit: 128);
        (run.Exit, run.Error).ShouldBe((1, "Could not resolve the Git checkout containing " + world.Linked + "."));

        Should.Throw<NightlyUsageException>(() => AntiphonLogsOptions.Parse(["--source", "kernel"], "c", "t", null, null, "f")).Message.ShouldContain("--source");
        Should.Throw<NightlyUsageException>(() => AntiphonLogsOptions.Parse(["--source", "apphost", "--tail", "0"], "c", "t", null, null, "f"));
        Should.Throw<NightlyUsageException>(() => AntiphonLogsOptions.Parse([], "c", "t", null, null, "f")).Message.ShouldContain("--source");
        var options = AntiphonLogsOptions.Parse(["--source", "APPHOST"], "c", "t", null, null, "f");
        (options.Source, options.Tail, options.Api, options.PtyHostLogs).ShouldBe(("apphost", 100, OperatorApi.DefaultApi, AntiphonLogs.PtyHostLogDirectory));
    }

    [Test]
    public void The_docs_name_the_command_and_the_source_is_ascii()
    {
        var doc = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docs", "logs.md"));
        doc.ShouldContain("scripts/antiphon logs --source <name> --tail 100");
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "AntiphonLogs.cs")).Count(b => b > 127).ShouldBe(0);
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "logs.ps1")).ShouldBeFalse("logs is a command");
    }
}
