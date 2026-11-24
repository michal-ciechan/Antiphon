using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
public sealed class CommitOnSettleScriptTests
{
    [Test]
    public async Task NoCommit_posts_commitOnSettle_Never()
    {
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "create", "--role", "Docs", "--goal", "x", "--no-commit");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("commitOnSettle").GetString().ShouldBe("Never");
    }

    [Test]
    public async Task Omitted_NoCommit_sends_no_commitOnSettle()
    {
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "create", "--role", "Docs", "--goal", "x");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("commitOnSettle", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Delegate_has_no_Always_or_Agent_switch()
    {
        using var server = new DelegateCreateStubApi();
        foreach (var flag in new[] { "--commit-always", "--commit-agent" })
        {
            var run = await DelegateScriptRunner.RunAsync(
                server.BaseUrl, "create", "--role", "Docs", "--goal", "x", flag);
            run.ExitCode.ShouldNotBe(0);
        }

        server.RequestCount.ShouldBe(0);
    }

    // The project command (Antiphon.Nightly.ProjectCli, which replaced scripts/project.ps1 in unit 24 of the 2026-11-22 plan)
    // runs in process on a recording transport that answers as the project API.
    [Test]
    [Arguments("On", "On")]
    [Arguments("Off", "Off")]
    [Arguments("Inherit", "Inherit")]
    public void Project_set_CommitOnSettle_puts_the_value(string value, string expected)
    {
        var http = CommitPolicyProjectApi();
        var run = RunProject(http, "set", "antiphon", "--commit-on-settle", value);

        run.Exit.ShouldBe(0, run.All);
        http.Calls.Select(c => c.Method).ShouldBe(["GET", "PUT"]);
        using var put = JsonDocument.Parse(http.Calls[^1].Body!);
        put.RootElement.GetProperty("commitOnSettle").GetString().ShouldBe(expected);
        run.Output.ShouldContain("commitOnSettle=" + expected);
    }

    [Test]
    public void Project_set_PUT_carries_the_GET_fields_unchanged()
    {
        var http = CommitPolicyProjectApi();
        var run = RunProject(http, "set", "antiphon", "--commit-on-settle", "Off");

        run.Exit.ShouldBe(0, run.All);
        using var body = JsonDocument.Parse(http.Calls[^1].Body!);
        var put = body.RootElement;
        put.GetProperty("name").GetString().ShouldBe("antiphon");
        put.GetProperty("gitRepositoryUrl").GetString().ShouldBe("https://example.test/antiphon.git");
        put.GetProperty("baseBranch").GetString().ShouldBe("master");
        put.GetProperty("constitutionPath").GetString().ShouldBe("AGENTS.md");
        put.GetProperty("gitHubIntegrationEnabled").GetBoolean().ShouldBeFalse();
        put.TryGetProperty("defaultLaunchEnv", out _).ShouldBeFalse();
        put.TryGetProperty("localRepositoryPath", out _).ShouldBeFalse("a null field the GET answered is not re-sent");
    }

    // The task-commit command (Antiphon.Nightly.TaskCommit, which replaced scripts/task-commit.ps1 in unit 25 of the 2026-11-22
    // plan) runs in process on a recording transport that answers as the commit endpoint; the message file is a mock file.
    [Test]
    public void Task_commit_script_posts_paths_and_message_to_the_commit_endpoint()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """{"sha":"abc1234","files":["x.md"]}"""));
        var run = RunTaskCommit(http, "task 1234abcd: x", "--paths", "x.md", "--message-file", MessageFile);

        run.Exit.ShouldBe(0, run.All);
        run.Output.Trim().ShouldBe("committed abc1234 x.md");
        var call = http.Calls.ShouldHaveSingleItem();
        (call.Method, call.Uri).ShouldBe(("POST", BoardCommandRun.Api.TrimEnd('/') + "/api/agent-tasks/11111111-1111-1111-1111-111111111111/commit"));
        using var body = JsonDocument.Parse(call.Body!);
        body.RootElement.GetProperty("paths")[0].GetString().ShouldBe("x.md");
        body.RootElement.GetProperty("message").GetString().ShouldBe("task 1234abcd: x");
        call.Headers["X-Antiphon-Task-Token"].ShouldBe("tok");
    }

    [Test]
    public void Task_commit_splits_comma_separated_paths_and_reads_the_message_as_utf8()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """{"sha":"abc1234","files":["a.md","b.md"]}"""));
        var run = RunTaskCommit(http, "\uFEFFtask: caf\u00e9", "--paths", "a.md, ,b.md", "--message-file", MessageFile);

        run.Exit.ShouldBe(0, run.All);
        run.Output.Trim().ShouldBe("committed abc1234 a.md, b.md");
        using var body = JsonDocument.Parse(http.Calls.ShouldHaveSingleItem().Body!);
        body.RootElement.GetProperty("paths").EnumerateArray().Select(path => path.GetString()).ShouldBe(["a.md", "b.md"]);
        body.RootElement.GetProperty("message").GetString().ShouldBe("task: caf\u00e9", "the BOM is dropped");
    }

    [Test]
    public void Task_commit_script_reports_a_409_refusal_verbatim_and_exits_nonzero()
    {
        var http = new RecordingOperatorHttp(_ => new(409, """{"code":"ignored_path_staged","detail":"a.secret"}"""));
        var run = RunTaskCommit(http, "x", "--paths", "a.secret", "--message-file", MessageFile);
        run.Exit.ShouldBe(1);
        run.Error.Trim().ShouldBe("""commit refused: {"code":"ignored_path_staged","detail":"a.secret"}""");
        run.Output.ShouldBeEmpty();
    }

    [Test]
    public void Task_commit_refuses_without_a_token_task_id_or_message_file()
    {
        var http = new RecordingOperatorHttp(_ => throw new InvalidOperationException("no request expected"));
        RunTaskCommitAs(http, "x", token: "", args: ["--paths", "x.md", "--message-file", MessageFile]).Error.Trim().ShouldBe("ANTIPHON_TASK_TOKEN is required.");
        RunTaskCommitAs(http, "x", taskId: "", args: ["--paths", "x.md", "--message-file", MessageFile]).Error.Trim().ShouldBe("ANTIPHON_TASK_ID is required.");
        var missing = RunTaskCommitAs(http, null, args: ["--paths", "x.md", "--message-file", MessageFile]);
        (missing.Exit, missing.Error.Trim()).ShouldBe((1, "--message-file '" + MessageFile + "' does not exist."));
        RunTaskCommit(http, "x", args: ["--message-file", MessageFile]).Exit.ShouldBe(2, "--paths is required");
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Commit_on_settle_scripts_are_ascii_only()
    {
        foreach (var name in new[] { "lib/Antiphon.Nightly/ProjectCli.cs", "lib/Antiphon.Nightly/DelegateCli.cs", "lib/Antiphon.Nightly/TaskCommit.cs" })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name);
            File.Exists(path).ShouldBeTrue(path);
            var bytes = File.ReadAllBytes(path);
            var firstNonAscii = Array.FindIndex(bytes, static b => b > 127);
            firstNonAscii.ShouldBe(-1, $"{name} has a non-ASCII byte at offset {firstNonAscii}");
        }
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "task-commit.ps1")).ShouldBeFalse("task-commit is a command");
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1")).ShouldBeFalse("delegate is a command");
    }

    [Test]
    public void Task_commit_script_preserves_a_completed_commit_with_pending_inspection()
    {
        const string operation = "22222222-2222-2222-2222-222222222222";
        var http = new RecordingOperatorHttp(_ => new(503, """{"code":"commit_inspection_pending","committed":true,"operationId":"22222222-2222-2222-2222-222222222222"}"""));
        var run = RunTaskCommit(http, "x", "--paths", "x.md", "--message-file", MessageFile);
        run.Exit.ShouldBe(1);
        run.Error.ShouldContain("Commit succeeded; inspection pending");
        run.Error.ShouldContain("GET /api/agent-tasks/11111111-1111-1111-1111-111111111111/commit/" + operation + " using the task token");
        run.All.ShouldNotContain("commit refused");
    }

    private const string MessageFile = "commit-message.txt";

    // The message file holds message (null: no file); the CLI's environment is the API, the token and the task id.
    private static BoardCommandRun.Result RunTaskCommitAs(RecordingOperatorHttp http, string? message, string token = "tok",
        string taskId = "11111111-1111-1111-1111-111111111111", params string[] args)
    {
        var files = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        if (message != null) files.AddFile(MessageFile, new System.IO.Abstractions.TestingHelpers.MockFileData(Encoding.UTF8.GetBytes(message)));
        return BoardCommandRun.Run(host => Antiphon.Nightly.TaskCommit.Run(Antiphon.Nightly.TaskCommitOptions.Parse(args, BoardCommandRun.Api, token, taskId), host,
            files, http.Transport));
    }

    private static BoardCommandRun.Result RunTaskCommit(RecordingOperatorHttp http, string message, params string[] args) =>
        RunTaskCommitAs(http, message, args: args);

    private static BoardCommandRun.Result RunProject(RecordingOperatorHttp http, params string[] args) =>
        BoardCommandRun.Run(host => Antiphon.Nightly.ProjectCli.Run(Antiphon.Nightly.ProjectCliOptions.Parse(args, BoardCommandRun.Api, null), host,
            new System.IO.Abstractions.FileSystem(), http.Transport));

    // The project API as the script driver's stub answered it: the one project in an array for the list, the project for the PUT.
    private static RecordingOperatorHttp CommitPolicyProjectApi() => new(call =>
    {
        const string project = """
            {"id":"11111111-1111-1111-1111-111111111111","name":"antiphon",
             "gitRepositoryUrl":"https://example.test/antiphon.git","baseBranch":"master",
             "constitutionPath":"AGENTS.md","gitHubIntegrationEnabled":false,
             "notificationsEnabled":false,"createdAt":"2026-09-15T00:00:00Z",
             "updatedAt":"2026-09-15T00:00:00Z","defaultLaunchEnv":{"KEEP":"yes"},
             "localRepositoryPath":null,"commitOnSettle":null,"effectiveCommitOnSettle":true}
            """;
        if (call.Method != "PUT") return new(200, "[" + project + "]");
        using var put = JsonDocument.Parse(call.Body!);
        return new(200, project.Replace("\"commitOnSettle\":null", "\"commitOnSettle\":\"" + put.RootElement.GetProperty("commitOnSettle").GetString() + "\""));
    });
}
