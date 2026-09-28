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
public sealed class ProjectScriptWorkspaceTests
{
    [Test]
    [Arguments("Shared", false)]
    [Arguments("Shared", true)]
    [Arguments("Worktree", false)]
    [Arguments("Worktree", true)]
    [Arguments("Inherit", false)]
    [Arguments("Inherit", true)]
    public async Task C458_SetDefaultWorkerWorkspaceRoundTrips(string value, bool byGuid)
    {
        using var stub = new ProjectStub { Workspace = "Shared" };
        var run = await RunAsync(stub.BaseUrl, "set", byGuid ? ProjectStub.Id : "antiphon",
            "-DefaultWorkerWorkspace", value);
        run.ExitCode.ShouldBe(0, run.Output);
        stub.PutCount.ShouldBe(1);
        stub.LastPut!.RootElement.GetProperty("defaultWorkerWorkspace").GetString().ShouldBe(value);
        var expected = value == "Inherit" ? null : value;
        stub.Workspace.ShouldBe(expected);
        using var client = new HttpClient();
        using var detail = JsonDocument.Parse(await client.GetStringAsync(stub.BaseUrl + "/api/projects/" + ProjectStub.Id));
        detail.RootElement.GetProperty("defaultWorkerWorkspace").GetString().ShouldBe(expected);
        detail.RootElement.GetProperty("effectiveWorkerWorkspace").GetString().ShouldBe(expected ?? "Worktree");
        run.Output.ShouldContain("effectiveWorkerWorkspace=" + (expected ?? "Worktree"));
    }

    [Test]
    public async Task C458_SetWorkspacePreservesOtherProjectFields()
    {
        using var stub = new ProjectStub();
        var run = await RunAsync(stub.BaseUrl, "set", "antiphon", "-DefaultWorkerWorkspace", "Shared");
        run.ExitCode.ShouldBe(0, run.Output);
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
    }

    [Test]
    public async Task C458_SetCommitPolicyPreservesWorkspace()
    {
        using var stub = new ProjectStub { Workspace = "Worktree" };
        var run = await RunAsync(stub.BaseUrl, "set", "antiphon", "-CommitOnSettle", "Off");
        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastPut!.RootElement.TryGetProperty("defaultWorkerWorkspace", out _).ShouldBeFalse();
        stub.Workspace.ShouldBe("Worktree");
        stub.CommitOnSettle.ShouldBe("Off");
    }

    [Test]
    public async Task C458_InvalidWorkspaceNeverPuts()
    {
        using var stub = new ProjectStub();
        foreach (var bad in new[] { "ReadOnly", "unknown" })
        {
            var run = await RunAsync(stub.BaseUrl, "set", "antiphon", "-DefaultWorkerWorkspace", bad);
            run.ExitCode.ShouldNotBe(0);
        }
        stub.PutCount.ShouldBe(0);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string api, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "project.ps1"));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = api;
        start.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class ProjectStub : IDisposable
    {
        public const string Id = "11111111-1111-1111-1111-111111111111";
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        public string BaseUrl { get; }
        public string? Workspace { get; set; }
        public string? CommitOnSettle { get; set; } = "Off";
        public string DefaultLaunchEnv { get; set; } = "sentinel";
        public int PutCount { get; private set; }
        public JsonDocument? LastPut { get; private set; }

        public ProjectStub()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                if (context.Request.HttpMethod == "PUT")
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    LastPut?.Dispose();
                    LastPut = JsonDocument.Parse(await reader.ReadToEndAsync());
                    var body = LastPut.RootElement;
                    if (body.TryGetProperty("defaultWorkerWorkspace", out var workspace))
                        Workspace = workspace.GetString() is "Inherit" ? null : workspace.GetString();
                    if (body.TryGetProperty("commitOnSettle", out var commit))
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
                };
                var json = JsonSerializer.Serialize(dto);
                if (context.Request.HttpMethod == "GET" && context.Request.Url!.AbsolutePath == "/api/projects")
                    json = "[" + json + "]";
                var bytes = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _listener.Close();
            LastPut?.Dispose();
            _cts.Dispose();
        }
    }
}
