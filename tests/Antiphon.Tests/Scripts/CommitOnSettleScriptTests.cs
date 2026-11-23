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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CommitOnSettleScriptTests
{
    [Test]
    public async Task NoCommit_posts_commitOnSettle_Never()
    {
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "-Role", "Docs", "-Goal", "x", "-NoCommit");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("commitOnSettle").GetString().ShouldBe("Never");
    }

    [Test]
    public async Task Omitted_NoCommit_sends_no_commitOnSettle()
    {
        using var server = new DelegateCreateStubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "-Role", "Docs", "-Goal", "x");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("commitOnSettle", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Delegate_has_no_Always_or_Agent_switch()
    {
        using var server = new DelegateCreateStubApi();
        foreach (var flag in new[] { "-CommitAlways", "-CommitAgent" })
        {
            var run = await DelegateScriptRunner.RunAsync(
                server.BaseUrl, "-Role", "Docs", "-Goal", "x", flag);
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

    [Test]
    public async Task Task_commit_script_posts_paths_and_message_to_the_commit_endpoint()
    {
        using var stub = new CommitApiStub(200, """{"sha":"abc1234","files":["x.md"]}""");
        var messageFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(messageFile, "task 1234abcd: x");
        var run = await RunTaskCommitAsync(stub.BaseUrl, "11111111-1111-1111-1111-111111111111", "tok",
            "-Paths", "x.md", "-MessageFile", messageFile);
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastPath.ShouldEndWith("/api/agent-tasks/11111111-1111-1111-1111-111111111111/commit");
        var body = stub.LastBody.ShouldNotBeNull().RootElement;
        body.GetProperty("paths")[0].GetString().ShouldBe("x.md");
        body.GetProperty("message").GetString().ShouldBe("task 1234abcd: x");
        stub.LastToken.ShouldBe("tok");
    }

    [Test]
    public async Task Task_commit_script_reports_a_409_refusal_verbatim_and_exits_nonzero()
    {
        using var stub = new CommitApiStub(409, """{"code":"ignored_path_staged","detail":"a.secret"}""");
        var messageFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(messageFile, "x");
        var run = await RunTaskCommitAsync(stub.BaseUrl, "11111111-1111-1111-1111-111111111111", "tok",
            "-Paths", "a.secret", "-MessageFile", messageFile);
        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("ignored_path_staged");
        run.Output.ShouldContain("a.secret");
    }

    [Test]
    public void Commit_on_settle_scripts_are_ascii_only()
    {
        foreach (var name in new[] { "lib/Antiphon.Nightly/ProjectCli.cs", "delegate.ps1", "task-commit.ps1" })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name);
            File.Exists(path).ShouldBeTrue(path);
            var bytes = File.ReadAllBytes(path);
            var firstNonAscii = Array.FindIndex(bytes, static b => b > 127);
            firstNonAscii.ShouldBe(-1, $"{name} has a non-ASCII byte at offset {firstNonAscii}");
        }
    }

    [Test]
    public async Task Task_commit_script_preserves_a_completed_commit_with_pending_inspection()
    {
        const string operation = "22222222-2222-2222-2222-222222222222";
        using var stub = new CommitApiStub(503, """{"code":"commit_inspection_pending","committed":true,"operationId":"22222222-2222-2222-2222-222222222222"}""");
        var messageFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(messageFile, "x");
            var run = await RunTaskCommitAsync(stub.BaseUrl, "11111111-1111-1111-1111-111111111111", "tok",
                "-Paths", "x.md", "-MessageFile", messageFile);
            run.ExitCode.ShouldNotBe(0);
            run.Output.ShouldContain("Commit succeeded; inspection pending");
            run.Output.ShouldContain("/commit/" + operation);
            run.Output.ShouldNotContain("commit refused");
        }
        finally { File.Delete(messageFile); }
    }

    private static async Task<(int ExitCode, string Output)> RunTaskCommitAsync(
        string api, string taskId, string token, params string[] args)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "task-commit.ps1"));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = api.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = token;
        startInfo.Environment["ANTIPHON_TASK_ID"] = taskId;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

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

    private sealed class CommitApiStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly int _status;
        private readonly string _response;

        public CommitApiStub(int status, string response)
        {
            _status = status;
            _response = response;
            BaseUrl = EphemeralHttpListener.BindLoopback(out _listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public string? LastPath { get; private set; }
        public string? LastToken { get; private set; }
        public JsonDocument? LastBody { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                LastPath = context.Request.Url?.PathAndQuery;
                LastToken = context.Request.Headers["X-Antiphon-Task-Token"];
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw))
                        LastBody = JsonDocument.Parse(raw);
                }

                var payload = Encoding.UTF8.GetBytes(_response);
                context.Response.StatusCode = _status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            LastBody?.Dispose();
            _cts.Dispose();
        }
    }
}
