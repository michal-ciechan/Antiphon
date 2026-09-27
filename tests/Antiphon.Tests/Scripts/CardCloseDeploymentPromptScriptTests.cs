using System.Diagnostics;
using System.Net;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardCloseDeploymentPromptScriptTests
{
    private const string CardId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string BoardId = "22222222-2222-2222-2222-222222222222";

    [Test]
    public async Task close_prints_deployment_prompt_after_card_tracker_and_task_settlement()
    {
        using var api = new StubApi();
        var run = await RunAsync(api, "close", "CARD-0001", "-Reason", "finished");

        run.ExitCode.ShouldBe(0, run.Output);
        var card = run.Output.IndexOf("CARD-0001", StringComparison.Ordinal);
        var tracker = run.Output.IndexOf("GitHub", StringComparison.Ordinal);
        var task = run.Output.IndexOf("tasks       still working", StringComparison.Ordinal);
        var prompt = run.Output.IndexOf("deployment not verified", StringComparison.OrdinalIgnoreCase);
        card.ShouldBeGreaterThanOrEqualTo(0);
        tracker.ShouldBeGreaterThan(card);
        task.ShouldBeGreaterThan(tracker);
        prompt.ShouldBeGreaterThan(task);
        run.Output.ShouldContain("/api/version");
        run.Output.ShouldContain("check-daemon-build.ps1");
        api.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task terminal_move_prints_prompt_but_nonterminal_move_does_not()
    {
        using var terminalApi = new StubApi();
        var terminal = await RunAsync(terminalApi, "move", "CARD-0001", "-To", "Done");
        terminal.ExitCode.ShouldBe(0, terminal.Output);
        terminal.Output.ShouldContain("deployment not verified", Case.Insensitive);
        terminalApi.PatchCount.ShouldBe(1);

        using var activeApi = new StubApi();
        var active = await RunAsync(activeApi, "move", "CARD-0001", "-To", "Working");
        active.ExitCode.ShouldBe(0, active.Output);
        active.Output.ShouldNotContain("deployment not verified", Case.Insensitive);
        activeApi.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task close_without_task_settlement_still_prints_prompt()
    {
        using var api = new StubApi(includeSettlement: false);
        var run = await RunAsync(api, "close", "CARD-0001", "-Reason", "finished");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("deployment not verified", Case.Insensitive);
        run.Output.ShouldNotContain("still working");
        api.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task failed_patch_exits_nonzero_without_success_or_deployment_prompt()
    {
        using var api = new StubApi(failPatch: true);
        var run = await RunAsync(api, "close", "CARD-0001", "-Reason", "finished");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldNotContain("moved to");
        run.Output.ShouldNotContain("deployment not verified", Case.Insensitive);
        api.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task prompt_is_advisory_and_makes_no_extra_api_request()
    {
        using var api = new StubApi();
        var run = await RunAsync(api, "close", "CARD-0001", "-Reason", "finished");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("canonical checkout", Case.Insensitive);
        api.PatchCount.ShouldBe(1);
        api.Requests.ShouldBe(4); // card, columns, limits, PATCH; no version or runner probe
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(StubApi api, params string[] args)
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "card.ps1");
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = api.BaseUrl.TrimEnd('/');
        start.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
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
        private readonly bool _includeSettlement;
        private readonly bool _failPatch;

        public StubApi(bool includeSettlement = true, bool failPatch = false)
        {
            _includeSettlement = includeSettlement;
            _failPatch = failPatch;
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public int PatchCount { get; private set; }
        public int Requests { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                Requests++;
                var path = context.Request.Url?.AbsolutePath ?? "";
                var method = context.Request.HttpMethod;
                var status = 200;
                string body;
                if (method == "GET" && path == "/api/cards/CARD-0001")
                    body = $$"""{"id":"{{CardId}}","identifier":"CARD-0001","status":"Working","title":"Example","importance":"Normal","urgency":"Normal","rank":0,"concurrencyToken":"11111111-1111-1111-1111-111111111111","boardId":"{{BoardId}}","labels":[]}""";
                else if (method == "GET" && path == $"/api/boards/{BoardId}/columns")
                    body = """[{"id":"33333333-3333-3333-3333-333333333333","name":"Working","stateKey":"Working","cardStatus":"Working","isTerminal":false,"columnOrder":0},{"id":"44444444-4444-4444-4444-444444444444","name":"Done","stateKey":"Done","cardStatus":"Done","isTerminal":true,"columnOrder":1}]""";
                else if (method == "GET" && path == "/api/cards/limits")
                    body = """{"maxReasonLength":4000}""";
                else if (method == "PATCH" && path == $"/api/cards/{CardId}")
                {
                    PatchCount++;
                    if (_failPatch) { status = 409; body = """{"detail":"stale token"}"""; }
                    else
                    {
                        var settlement = _includeSettlement ? ""","taskSettlement":{"leftOpen":["task-1"],"canceled":[]}""" : "";
                        body = $$"""{"card":{"id":"{{CardId}}","identifier":"CARD-0001","status":"Done","title":"Example","importance":"Normal","urgency":"Normal","rank":0,"labels":[]},"trackerPush":{"trackerKind":"GitHubIssues","outcome":"Closed","externalKey":"#1","url":"https://example.test/1"}{{settlement}}}""";
                    }
                }
                else { status = 404; body = """{"detail":"not stubbed"}"""; }
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
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
