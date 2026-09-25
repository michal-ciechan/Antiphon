using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0710 V-10. The real delegate and card scripts against a loopback capture.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegatePlatformScriptTests
{
    [Test]
    public async Task Omission_and_explicit_any_have_distinct_payloads()
    {
        using var omitted = new DelegateCreateStubApi(Created("Any", "server2"));
        var without = await DelegateScriptRunner.RunAsync(omitted.BaseUrl, ["-Role", "Code", "-Goal", "inherit"]);
        without.ExitCode.ShouldBe(0, without.Output);
        omitted.LastBody!.RootElement.TryGetProperty("requiredPlatform", out _).ShouldBeFalse();

        using var any = new DelegateCreateStubApi(Created("Any", "server2"));
        var with = await DelegateScriptRunner.RunAsync(any.BaseUrl, ["-Role", "Code", "-Goal", "reset", "-Platform", "Any"]);
        with.ExitCode.ShouldBe(0, with.Output);
        any.LastBody!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Any");
        with.Output.ShouldContain("platform: Any");
    }

    [Test]
    public async Task Desktop_alias_preserves_workspace()
    {
        using var server = new DelegateCreateStubApi(Created("Any", "desktop"));
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            ["-Role", "Code", "-Goal", "stay shared", "-Shared", "-Runner", "desktop"]);
        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody!.RootElement;
        body.GetProperty("runnerId").GetString().ShouldBe("desktop");
        body.GetProperty("workspace").GetString().ShouldBe("Shared");
    }

    [Test]
    public async Task Remote_guards_and_platform_forwarding()
    {
        using var invalid = new DelegateCreateStubApi();
        var refused = await DelegateScriptRunner.RunAsync(invalid.BaseUrl, ["-Role", "Code", "-Goal", "nope", "-Platform", "Mac"]);
        refused.ExitCode.ShouldNotBe(0);
        invalid.RequestCount.ShouldBe(0);

        using var remote = new DelegateCreateStubApi(Created("Linux", "server2"));
        var sent = await DelegateScriptRunner.RunAsync(remote.BaseUrl,
            ["-Role", "Code", "-Goal", "linux", "-Runner", "server2", "-Platform", "Linux"]);
        sent.ExitCode.ShouldBe(0, sent.Output);
        var body = remote.LastBody!.RootElement;
        body.GetProperty("runnerId").GetString().ShouldBe("server2");
        body.GetProperty("workspace").GetString().ShouldBe("Worktree");
        body.GetProperty("requiredPlatform").GetString().ShouldBe("Linux");
    }

    [Test]
    public async Task Server_mismatch_is_reported_without_retry()
    {
        using var server = new DelegateCreateStubApi(
            """{"title":"runner_platform_mismatch","status":409,"detail":"Runner 'server2' is linux and cannot run a Windows task."}""",
            statusCode: 409);
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            ["-Role", "Code", "-Goal", "windows on linux", "-Runner", "server2", "-Platform", "Windows"]);
        run.ExitCode.ShouldNotBe(0, run.Output);
        run.Output.ShouldContain("runner_platform_mismatch");
        server.RequestCount.ShouldBe(1);
    }

    private static string Created(string platform, string runner) =>
        $$"""{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode","requiredPlatform":"{{platform}}","runnerId":"{{runner}}"}""";
}

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardPlatformScriptTests
{
    [Test]
    public async Task New_edit_and_reset_forward_platform()
    {
        using var created = new CardPlatformStub();
        var board = "c7100000-aaaa-bbbb-cccc-0000000000bb";
        var fresh = await Run(created.BaseUrl, ["new", "-Board", board, "-Title", "platform card", "-Platform", "Windows"]);
        fresh.ExitCode.ShouldBe(0, fresh.Output);
        created.LastCreate!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Windows");
        fresh.Output.ShouldContain("platform");

        using var edited = new CardPlatformStub();
        var edit = await Run(edited.BaseUrl, ["edit", "CARD-0710", "-Platform", "Linux", "-Reason", "need linux"]);
        edit.ExitCode.ShouldBe(0, edit.Output);
        edited.LastPatch!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Linux");

        using var reset = new CardPlatformStub();
        var any = await Run(reset.BaseUrl, ["edit", "CARD-0710", "-Platform", "Any", "-Reason", "reset"]);
        any.ExitCode.ShouldBe(0, any.Output);
        reset.LastPatch!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Any");
    }

    private static async Task<(int ExitCode, string Output)> Run(string api, string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "card.ps1"));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = api.TrimEnd('/');
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class CardPlatformStub : IDisposable
    {
        private const string CardId = "c7100000-aaaa-bbbb-cccc-0000000000cc";
        private const string Token = "c7100000-aaaa-bbbb-cccc-000000000001";
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public CardPlatformStub()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public JsonDocument? LastCreate { get; private set; }
        public JsonDocument? LastPatch { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                var path = context.Request.Url!.AbsolutePath;
                var response = Card();
                if (context.Request.HttpMethod == "GET" && path == "/api/cards/limits")
                    response = """{"maxTitleLength":300,"maxDescriptionLength":20000,"maxPrivateNotesLength":20000,"maxReasonLength":4000,"maxActorLength":200,"maxAliasLength":64,"maxAliasWords":6}""";
                else if (context.Request.HttpMethod == "POST" && path.Contains("/cards", StringComparison.Ordinal))
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    LastCreate = JsonDocument.Parse(await reader.ReadToEndAsync());
                    response = Card("Windows");
                }
                else if (context.Request.HttpMethod == "PATCH")
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    LastPatch = JsonDocument.Parse(await reader.ReadToEndAsync());
                    var platform = LastPatch.RootElement.TryGetProperty("requiredPlatform", out var value) ? value.GetString() : "Any";
                    response = Card(platform);
                }

                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        private static string Card(string platform = "Any") =>
            $$"""{"id":"{{CardId}}","boardId":"c7100000-aaaa-bbbb-cccc-0000000000bb","identifier":"CARD-0710","title":"platform card","status":"Backlog","importance":"Normal","urgency":"Normal","rank":1,"importanceProvenance":"Auto","labels":[],"concurrencyToken":"{{Token}}","revisionCount":2,"requiredPlatform":"{{platform}}"}""";

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            LastCreate?.Dispose();
            LastPatch?.Dispose();
            _cts.Dispose();
        }
    }
}
