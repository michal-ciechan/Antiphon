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

    [Test]
    [Arguments("On", "On")]
    [Arguments("Off", "Off")]
    [Arguments("Inherit", "Inherit")]
    public async Task Project_set_CommitOnSettle_puts_the_value(string value, string expected)
    {
        using var stub = new ProjectApiStub();
        var run = await RunProjectAsync(stub.BaseUrl, "set", "antiphon", "-CommitOnSettle", value);

        run.ExitCode.ShouldBe(0, run.Output);
        stub.LastMethod.ShouldBe("PUT");
        stub.LastPutBody.ShouldNotBeNull()
            .RootElement.GetProperty("commitOnSettle").GetString().ShouldBe(expected);
    }

    [Test]
    public async Task Project_set_PUT_carries_the_GET_fields_unchanged()
    {
        using var stub = new ProjectApiStub();
        var run = await RunProjectAsync(stub.BaseUrl, "set", "antiphon", "-CommitOnSettle", "Off");

        run.ExitCode.ShouldBe(0, run.Output);
        var put = stub.LastPutBody.ShouldNotBeNull().RootElement;
        put.GetProperty("name").GetString().ShouldBe(ProjectApiStub.Canned.Name);
        put.GetProperty("gitRepositoryUrl").GetString().ShouldBe(ProjectApiStub.Canned.GitUrl);
        put.GetProperty("baseBranch").GetString().ShouldBe(ProjectApiStub.Canned.BaseBranch);
        put.TryGetProperty("defaultLaunchEnv", out _).ShouldBeFalse();
    }

    [Test]
    public void Commit_on_settle_scripts_are_ascii_only()
    {
        foreach (var name in new[] { "project.ps1", "delegate.ps1" })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name);
            File.Exists(path).ShouldBeTrue(path);
            var bytes = File.ReadAllBytes(path);
            var firstNonAscii = Array.FindIndex(bytes, static b => b > 127);
            firstNonAscii.ShouldBe(-1, $"{name} has a non-ASCII byte at offset {firstNonAscii}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunProjectAsync(string api, params string[] args)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "project.ps1"));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = api.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class ProjectApiStub : IDisposable
    {
        public static readonly (string Name, string GitUrl, string BaseBranch) Canned =
            ("antiphon", "https://example.test/antiphon.git", "master");

        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public ProjectApiStub()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public string? LastMethod { get; private set; }
        public JsonDocument? LastPutBody { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                LastMethod = context.Request.HttpMethod;
                JsonDocument? body = null;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw))
                        body = JsonDocument.Parse(raw);
                }

                if (context.Request.HttpMethod == "PUT" && body is not null)
                    LastPutBody = body;

                const string cannedObject = """
                    {"id":"11111111-1111-1111-1111-111111111111","name":"antiphon",
                     "gitRepositoryUrl":"https://example.test/antiphon.git","baseBranch":"master",
                     "constitutionPath":"AGENTS.md","gitHubIntegrationEnabled":false,
                     "notificationsEnabled":false,"createdAt":"2026-09-15T00:00:00Z",
                     "updatedAt":"2026-09-15T00:00:00Z","defaultLaunchEnv":{"KEEP":"yes"},
                     "commitOnSettle":null,"effectiveCommitOnSettle":true}
                    """;
                var payload = Encoding.UTF8.GetBytes(
                    context.Request.HttpMethod == "PUT" ? cannedObject : "[" + cannedObject + "]");
                context.Response.StatusCode = 200;
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
            LastPutBody?.Dispose();
            _cts.Dispose();
        }
    }
}
