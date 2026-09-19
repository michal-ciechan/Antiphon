using System.Net;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0493 S2: <c>delegate.ps1</c> prints a <c>diagnosis:</c> line on a JSON bind 400
/// relating the served SHA to checkout HEAD. Exit code and retry behaviour stay unchanged.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptBindDiagnosisTests
{
    private const string IncidentRoleBody =
        "The JSON value could not be converted to Antiphon.Server.Application.Dtos.CreateAgentTaskRequest. Path: $.role | LineNumber: 0 | BytePositionInLine: 18.";

    private const string UnknownServedSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task a_422_is_reported_without_a_diagnosis()
    {
        using var server = new StubApi
        {
            CreateStatus = 422,
            CreateBody = """{"code":"validation_failed","errors":{"Goal":["A goal is required."]}}""",
        };
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "-Role", "Mutation", "-Goal", "x");

        run.ExitCode.ShouldNotBe(0, run.Output);
        run.Output.ShouldContain("validation_failed");
        run.Output.ShouldNotContain("diagnosis:");
        server.CreateCalls.ShouldBe(1);
        server.VersionCalls.ShouldBe(0);
    }

    [Test]
    public async Task a_bind_400_with_an_unknown_served_build_is_diagnosed()
    {
        using var repo = await CreateRepoAsync();
        using var server = Bind400(IncidentRoleBody, VersionJson(UnknownServedSha));
        var run = await RunInAsync(server, repo, "-Role", "Mutation", "-Goal", "x");

        run.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("diagnosis: served build aaaaaaa rejected 'Mutation' for 'role'");
        run.Output.ShouldContain("is not a commit this checkout knows");
        run.Output.ShouldContain("restart-apphost.ps1");
        run.Output.ShouldContain("do not re-dispatch under a different -Role");
        run.Output.ShouldContain("could not be converted");
        server.CreateCalls.ShouldBe(1);
        server.VersionCalls.ShouldBe(1);
    }

    [Test]
    public async Task a_bind_400_counts_commits_ahead_of_an_ancestor_build()
    {
        using var repo = await CreateRepoAsync();
        using var server = Bind400(IncidentRoleBody, VersionJson(repo.C1));
        var run = await RunInAsync(server, repo, "-Role", "Mutation", "-Goal", "x");

        run.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("HEAD " + repo.C2[..7] + " is 1 commit(s) ahead of the served build");
    }

    [Test]
    public async Task a_bind_400_with_head_equal_to_the_served_build_says_genuinely_unknown()
    {
        using var repo = await CreateRepoAsync();
        using var server = Bind400(IncidentRoleBody, VersionJson(repo.C2));
        var run = await RunInAsync(server, repo, "-Role", "Mutation", "-Goal", "x");

        run.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("HEAD equals the served build; the value is genuinely unknown to this source");
    }

    [Test]
    public async Task a_bind_400_on_another_field_is_diagnosed_generically()
    {
        using var repo = await CreateRepoAsync();
        var body =
            "The JSON value could not be converted to Antiphon.Server.Application.Dtos.CreateAgentTaskRequest. Path: $.workspace | LineNumber: 0 | BytePositionInLine: 18.";
        using var server = Bind400(body, VersionJson(UnknownServedSha));
        var run = await RunInAsync(server, repo, "-Role", "Code", "-Worktree", "-Goal", "x");

        run.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("rejected 'Worktree' for 'workspace'");
    }

    [Test]
    public async Task a_bind_400_with_version_unavailable_still_diagnoses()
    {
        using var repo = await CreateRepoAsync();
        using var server = new StubApi
        {
            CreateStatus = 400,
            CreateBody = IncidentRoleBody,
            VersionStatus = 404,
            VersionBody = "",
        };
        var run = await RunInAsync(server, repo, "-Role", "Mutation", "-Goal", "x");

        run.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("GET " + server.BaseUrl.TrimEnd('/') + "/api/version failed (404)");
        run.Output.ShouldContain("rejected 'Mutation' for 'role'");
        run.Output.ShouldContain("restart-apphost.ps1");
        server.CreateCalls.ShouldBe(1);
    }

    private static StubApi Bind400(string createBody, string versionBody) => new()
    {
        CreateStatus = 400,
        CreateBody = createBody,
        VersionStatus = 200,
        VersionBody = versionBody,
    };

    private static string VersionJson(string sha) =>
        "{\"version\":\"" + sha + "\",\"capabilities\":[\"land-v2\"]}";

    private static Task<(int ExitCode, string Output)> RunInAsync(
        StubApi server, TwoCommitRepo repo, params string[] args) =>
        DelegateScriptRunner.RunAsync(
            server.BaseUrl, environment: null, workingDirectory: repo.Path, args);

    private static async Task<TwoCommitRepo> CreateRepoAsync()
    {
        var repo = new ScratchGitRepo("c493-bind");
        await repo.CommitFileAsync("a.txt", "one");
        var c1 = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.CommitFileAsync("b.txt", "two");
        var c2 = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        return new TwoCommitRepo(repo, c1, c2);
    }

    private sealed class TwoCommitRepo(ScratchGitRepo inner, string c1, string c2) : IDisposable
    {
        public string Path => inner.Path;
        public string C1 { get; } = c1;
        public string C2 { get; } = c2;
        public void Dispose() => inner.Dispose();
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public StubApi()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public int CreateStatus { get; init; } = 201;
        public string CreateBody { get; init; } =
            """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode"}""";
        public int VersionStatus { get; init; } = 200;
        public string VersionBody { get; init; } = """{"version":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","capabilities":["land-v2"]}""";
        public int CreateCalls { get; private set; }
        public int VersionCalls { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                var path = context.Request.Url?.AbsolutePath ?? "";
                int status;
                string body;
                if (path.Equals("/api/version", StringComparison.OrdinalIgnoreCase))
                {
                    VersionCalls++;
                    status = VersionStatus;
                    body = VersionBody;
                }
                else
                {
                    CreateCalls++;
                    status = CreateStatus;
                    body = CreateBody;
                }

                var payload = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
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
            _cts.Dispose();
        }
    }
}
