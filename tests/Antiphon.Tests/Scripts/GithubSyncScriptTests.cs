using System.Diagnostics;
using System.Net;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0171 S2 — <c>scripts/github-sync.ps1</c>. Drives the real script under pwsh against a
/// local <see cref="HttpListener"/> standing in for the Antiphon API.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GithubSyncScriptTests
{
    [Test]
    public async Task Notify_forwards_the_query_parameter()
    {
        using var server = new StubApi(CleanBody());
        var run = await RunAsync(server, "-Notify");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastQuery.ShouldBe("?notify=true");
        server.LastPath.ShouldBe("/api/tracker-sync/run");
    }

    [Test]
    public async Task Without_Notify_the_query_is_empty()
    {
        using var server = new StubApi(CleanBody());
        var run = await RunAsync(server);

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastQuery.ShouldBeEmpty();
    }

    [Test]
    public async Task A_board_error_exits_1()
    {
        // Before CARD-0171 this exited 0, so a failed sync was a green Windmill job.
        const string body = """
            {"boards":[{"boardId":"11111111-1111-1111-1111-111111111111","boardName":"Antiphon board",
            "issuesPulled":0,"commentsIn":0,"commentsOut":0,"labelsChanged":0,"stateChanges":0,
            "creates":0,"externalReopens":0,"skips":[],"changes":[],
            "error":"GitHub returned 401 Unauthorized"}],"concurrentRunSkipped":false,"notifications":[]}
            """;
        using var server = new StubApi(body);
        var run = await RunAsync(server);

        run.ExitCode.ShouldBe(1, run.Output);
        run.Output.ShouldContain("error=GitHub returned 401 Unauthorized");
        run.Output.ShouldContain("board(s) reported an error");
    }

    [Test]
    public async Task A_clean_body_exits_0_and_prints_the_notification_lines()
    {
        const string body = """
            {"boards":[{"boardId":"11111111-1111-1111-1111-111111111111","boardName":"Antiphon board",
            "issuesPulled":12,"commentsIn":2,"commentsOut":1,"labelsChanged":3,"stateChanges":1,
            "creates":0,"externalReopens":1,"skips":[],
            "changes":[{"kind":"CommentIn","cardIdentifier":"CARD-0170","externalKey":"#10","url":null}]}],
            "concurrentRunSkipped":false,
            "notifications":[
              {"boardId":"11111111-1111-1111-1111-111111111111","sent":true,
               "channelId":"caee9d25-b751-4401-a295-3b7e242842aa","reason":null},
              {"boardId":"22222222-2222-2222-2222-222222222222","sent":false,
               "channelId":null,"reason":"notify_channel_unset"}]}
            """;
        using var server = new StubApi(body);
        var run = await RunAsync(server, "-Notify");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("commentsIn=2");
        run.Output.ShouldContain("reopens=1");
        run.Output.ShouldContain("changes=1");
        run.Output.ShouldContain("notified board 11111111-1111-1111-1111-111111111111 -> channel caee9d25-b751-4401-a295-3b7e242842aa");
        run.Output.ShouldContain("NOT notified board 22222222-2222-2222-2222-222222222222: notify_channel_unset");
    }

    [Test]
    public async Task A_board_id_targets_the_per_board_endpoint()
    {
        using var server = new StubApi(CleanBody());
        var run = await RunAsync(server, "-BoardId", "8988ca03-7414-47ad-b0b6-51556c701703", "-Notify");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastPath.ShouldBe("/api/boards/8988ca03-7414-47ad-b0b6-51556c701703/tracker/sync");
        server.LastQuery.ShouldBe("?notify=true");
    }

    [Test]
    public async Task A_non_success_response_exits_1()
    {
        using var server = new StubApi("""{"title":"boom"}""", statusCode: 500);
        var run = await RunAsync(server);

        run.ExitCode.ShouldBe(1, run.Output);
    }

    private static string CleanBody() =>
        """
        {"boards":[{"boardId":"11111111-1111-1111-1111-111111111111","boardName":"Antiphon board",
        "issuesPulled":0,"commentsIn":0,"commentsOut":0,"labelsChanged":0,"stateChanges":0,
        "creates":0,"externalReopens":0,"skips":[],"changes":[]}],
        "concurrentRunSkipped":false,"notifications":[]}
        """;

    private static Task<(int ExitCode, string Output)> RunAsync(StubApi server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "github-sync.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-BaseUrl");
        startInfo.ArgumentList.Add(server.BaseUrl.TrimEnd('/'));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        return RunProcessAsync(startInfo);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _body;
        private readonly int _statusCode;

        public StubApi(string body, int statusCode = 200)
        {
            _body = body;
            _statusCode = statusCode;
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = $"http://localhost:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public string LastPath { get; private set; } = "";
        public string LastQuery { get; private set; } = "";

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                LastPath = context.Request.Url?.AbsolutePath ?? "";
                LastQuery = context.Request.Url?.Query ?? "";

                var payload = Encoding.UTF8.GetBytes(_body);
                context.Response.StatusCode = _statusCode;
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
