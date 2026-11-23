using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0458. The project command's set --default-worker-workspace (Antiphon.Nightly.ProjectCli, which replaced
/// scripts/project.ps1 in unit 24 of the 2026-11-22 plan) in process against a recording stand-in for the project API that
/// keeps the stored values: the value round-trips by name or guid, the other fields are re-sent unchanged, the commit
/// policy and the workspace leave each other alone, and an invalid value never PUTs. Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class ProjectScriptWorkspaceTests
{
    [Test]
    [Arguments("Shared", false)]
    [Arguments("Shared", true)]
    [Arguments("Worktree", false)]
    [Arguments("Worktree", true)]
    [Arguments("Inherit", false)]
    [Arguments("Inherit", true)]
    public void C458_SetDefaultWorkerWorkspaceRoundTrips(string value, bool byGuid)
    {
        var stub = new WorkspaceProjectApi { Workspace = "Shared" };
        var run = stub.Run("set", byGuid ? WorkspaceProjectApi.Id : "antiphon", "--default-worker-workspace", value.ToLowerInvariant());
        run.Exit.ShouldBe(0, run.All);
        stub.PutCount.ShouldBe(1);
        stub.LastPut!.RootElement.GetProperty("defaultWorkerWorkspace").GetString().ShouldBe(value, "sent in its listed spelling");
        var expected = value == "Inherit" ? null : value;
        stub.Workspace.ShouldBe(expected);
        using var detail = JsonDocument.Parse(stub.Answer("GET", "/api/projects/" + WorkspaceProjectApi.Id).Body);
        detail.RootElement.GetProperty("defaultWorkerWorkspace").GetString().ShouldBe(expected);
        detail.RootElement.GetProperty("effectiveWorkerWorkspace").GetString().ShouldBe(expected ?? "Worktree");
        run.Output.ShouldContain("configuredWorkerWorkspace=" + (expected ?? "Worktree"));
        run.Output.ShouldContain("dispatchHonorsWorkspaceDefault=False");
    }

    [Test]
    public void C458_SetWorkspacePreservesOtherProjectFields()
    {
        var stub = new WorkspaceProjectApi();
        var run = stub.Run("set", "antiphon", "--default-worker-workspace", "Shared");
        run.Exit.ShouldBe(0, run.All);
        var body = stub.LastPut!.RootElement;
        body.GetProperty("name").GetString().ShouldBe("antiphon");
        body.GetProperty("gitRepositoryUrl").GetString().ShouldBe("https://example.test/repo.git");
        body.GetProperty("localRepositoryPath").GetString().ShouldBe(@"C:\src\repo");
        body.GetProperty("baseBranch").GetString().ShouldBe("release");
        body.GetProperty("constitutionPath").GetString().ShouldBe("AGENTS.md");
        body.GetProperty("gitHubIntegrationEnabled").GetBoolean().ShouldBeTrue();
        body.GetProperty("notificationsEnabled").GetBoolean().ShouldBeTrue();
        body.GetProperty("repositoryVisibility").GetString().ShouldBe("Private");
        body.TryGetProperty("commitOnSettle", out _).ShouldBeFalse();
        body.TryGetProperty("defaultLaunchEnv", out _).ShouldBeFalse();
        stub.CommitOnSettle.ShouldBe("Off");
        stub.DefaultLaunchEnv.ShouldBe("sentinel");
        stub.Requests.ShouldBe(["GET /api/projects", "PUT /api/projects/" + WorkspaceProjectApi.Id]);
    }

    [Test]
    public void C458_SetCommitPolicyPreservesWorkspace()
    {
        var stub = new WorkspaceProjectApi { Workspace = "Worktree" };
        var run = stub.Run("set", "antiphon", "--commit-on-settle", "Off");
        run.Exit.ShouldBe(0, run.All);
        stub.LastPut!.RootElement.TryGetProperty("defaultWorkerWorkspace", out _).ShouldBeFalse();
        stub.Workspace.ShouldBe("Worktree");
        stub.CommitOnSettle.ShouldBe("Off");
    }

    [Test]
    public void C458_InvalidWorkspaceNeverPuts()
    {
        var stub = new WorkspaceProjectApi();
        foreach (var bad in new[] { "ReadOnly", "unknown" })
        {
            var run = stub.Run("set", "antiphon", "--default-worker-workspace", bad);
            run.Exit.ShouldBe(2);
            run.Error.ShouldContain("--default-worker-workspace must be one of Shared, Worktree, Inherit, not '" + bad + "'");
        }
        stub.PutCount.ShouldBe(0);
        stub.Requests.ShouldBeEmpty();
    }

    // The ProjectStub of the script driver, as a transport: GET /api/projects answers the one project in an array, GET
    // /api/projects/{id} the project, and a PUT stores the workspace and commit policy it carries and answers the project.
    private sealed class WorkspaceProjectApi
    {
        public const string Id = "11111111-1111-1111-1111-111111111111";
        public string? Workspace { get; set; }
        public string? CommitOnSettle { get; set; } = "Off";
        public string DefaultLaunchEnv { get; set; } = "sentinel";
        public int PutCount { get; private set; }
        public JsonDocument? LastPut { get; private set; }
        public List<string> Requests { get; } = [];

        public BoardCommandRun.Result Run(params string[] args)
        {
            var http = new RecordingOperatorHttp(call =>
            {
                Requests.Add(call.Method + " " + call.Path);
                return Answer(call.Method, call.Path, call.Body);
            });
            return BoardCommandRun.Run(host => ProjectCli.Run(ProjectCliOptions.Parse(args, BoardCommandRun.Api, null), host,
                new System.IO.Abstractions.FileSystem(), http.Transport));
        }

        public NightlyReportHttpResponse Answer(string method, string path, string? body = null)
        {
            if (method == "PUT")
            {
                LastPut = JsonDocument.Parse(body!);
                var put = LastPut.RootElement;
                if (put.TryGetProperty("defaultWorkerWorkspace", out var workspace))
                    Workspace = workspace.GetString() is "Inherit" ? null : workspace.GetString();
                if (put.TryGetProperty("commitOnSettle", out var commit))
                    CommitOnSettle = commit.GetString();
                PutCount++;
            }
            var dto = new
            {
                id = Id, name = "antiphon", gitRepositoryUrl = "https://example.test/repo.git",
                localRepositoryPath = @"C:\src\repo", baseBranch = "release", constitutionPath = "AGENTS.md",
                gitHubIntegrationEnabled = true, notificationsEnabled = true, repositoryVisibility = "Private",
                defaultLaunchEnv = new { KEEP = DefaultLaunchEnv }, commitOnSettle = CommitOnSettle,
                effectiveCommitOnSettle = false, defaultWorkerWorkspace = Workspace,
                effectiveWorkerWorkspace = Workspace ?? "Worktree",
                dispatchHonorsWorkspaceDefault = false,
            };
            var json = JsonSerializer.Serialize(dto);
            return new NightlyReportHttpResponse(200, method == "GET" && path == "/api/projects" ? "[" + json + "]" : json);
        }
    }
}
