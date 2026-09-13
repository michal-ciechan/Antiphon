using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptWorktreeBaseTests
{
    [Test]
    [Arguments("omitted")]
    [Arguments("base_task")]
    [Arguments("fresh")]
    [Arguments("both_flags")]
    [Arguments("shared")]
    [Arguments("onagent_task")]
    public async Task T0442_V16(string name)
    {
        using var server = new StubApi();
        if (name is "both_flags" or "shared" or "onagent_task")
        {
            var refused = name switch
            {
                "both_flags" => await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Code", "-Goal", "x", "-Worktree", "-BaseTask", "abcd1234", "-FreshWorktree"),
                "shared" => await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Code", "-Goal", "x", "-Shared", "-FreshWorktree"),
                _ => await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Code", "-Goal", "x", "-Worktree", "-BaseTask", "abcd1234", "-OnAgent", "abcd1234"),
            };
            refused.ExitCode.ShouldNotBe(0);
            server.RequestCount.ShouldBe(0);
            return;
        }

        var args = name switch
        {
            "base_task" => new[] { "-Role", "Code", "-Goal", "x", "-Worktree", "-BaseTask", "abcd1234" },
            "fresh" => new[] { "-Role", "Code", "-Goal", "x", "-Worktree", "-FreshWorktree" },
            _ => new[] { "-Role", "Code", "-Goal", "x", "-Worktree" },
        };
        var created = await DelegateScriptRunner.RunAsync(server.BaseUrl, args);
        created.ExitCode.ShouldBe(0, created.Output);
        var body = server.LastBody.ShouldNotBeNull().RootElement;
        if (name == "omitted")
        {
            body.TryGetProperty("worktreeBaseTask", out _).ShouldBeFalse();
            body.TryGetProperty("freshWorktree", out _).ShouldBeFalse();
        }
        else if (name == "base_task")
            body.GetProperty("worktreeBaseTask").GetString().ShouldBe("abcd1234");
        else
            body.GetProperty("freshWorktree").GetBoolean().ShouldBeTrue();
    }

    [Test]
    [Arguments("continue")]
    [Arguments("wait")]
    [Arguments("fresh")]
    [Arguments("unknown_fallback")]
    public async Task T0442_V15(string name)
    {
        var json = name switch
        {
            "continue" => """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"Frontier","agentKind":"ClaudeCode","noReplyRouting":true,"worktreeBase":{"decision":"Continue","fallbackRef":"master","sourceTaskId":"22222222-2222-2222-2222-222222222222","sourceBranch":"feat/card-task-22222222","sourceSha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}""",
            "wait" => """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"Frontier","agentKind":"ClaudeCode","noReplyRouting":true,"worktreeBase":{"decision":"WaitForLand","reason":"waiting for land"}}""",
            "unknown_fallback" => """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"Frontier","agentKind":"ClaudeCode","noReplyRouting":true,"worktreeBase":{"decision":"Incomplete","fallbackRef":"HEAD","reason":"inspection_timeout"}}""",
            _ => """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"Frontier","agentKind":"ClaudeCode","noReplyRouting":true,"worktreeBase":{"decision":"Target","fallbackRef":"HEAD","reason":"fresh_target"}}""",
        };
        using var server = new StubApi(json);
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, "-Role", "Code", "-Goal", "x", "-Worktree", "-Card", "CARD-0442");
        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("base preview", Case.Insensitive);
        server.RequestCount.ShouldBe(1);
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _createJson;

        public StubApi(string? createJson = null)
        {
            _createJson = createJson ?? """{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode"}""";
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public JsonDocument? LastBody { get; private set; }
        public int RequestCount { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                RequestCount++;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw)) LastBody = JsonDocument.Parse(raw);
                }

                var payload = Encoding.UTF8.GetBytes(_createJson);
                context.Response.StatusCode = 201;
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
