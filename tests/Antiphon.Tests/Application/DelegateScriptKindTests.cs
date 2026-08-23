using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0084 S2's caller-facing half: <c>scripts/delegate.ps1 -Kind</c>. The script is what an
/// agent actually runs, so these drive the REAL script under pwsh against a stub server and assert
/// on the JSON body it posts — a string-match on the source would pass just as happily if the flag
/// never reached the request.
///
/// <para>The one thing that must not regress is the omitted case: a caller who never heard of
/// -Kind must produce byte-for-byte the request they produced before the flag existed, because the
/// server would then resolve a kind from the role policy that the caller did not choose.</para>
/// </summary>
[Category("Integration")]
public sealed class DelegateScriptKindTests
{
    [Test]
    public async Task Kind_Grok_is_posted_as_agentKind()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite", "-Kind", "Grok");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Grok");
        body.RootElement.GetProperty("kind").GetString().ShouldBe("Worker", "-Kind is a different axis from worker/orchestrator");
    }

    [Test]
    public async Task an_omitted_Kind_sends_no_agentKind_at_all()
    {
        // Absent, not "ClaudeCode": the field being missing is what lets the role policy decide,
        // and it is the difference between "the caller chose Claude" and "the caller said nothing".
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("agentKind", out _)
            .ShouldBeFalse("an omitted -Kind must leave the request exactly as it was before the flag existed");
    }

    [Test]
    public async Task Kind_Codex_is_posted_as_agentKind()
    {
        // CARD-0099 S3. Until this slice, Codex was the value this file used as its EXAMPLE of a
        // refused kind — the ValidateSet is the caller-facing half of the allowlist, so widening one
        // without the other leaves the flag rejected at the prompt no matter what the server admits.
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite", "-Kind", "Codex");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Codex");
        body.RootElement.GetProperty("kind").GetString().ShouldBe("Worker", "-Kind is a different axis from worker/orchestrator");
    }

    [Test]
    public async Task an_undelegatable_Kind_is_refused_by_the_script_before_any_request()
    {
        // ValidateSet is the cheap half of the allowlist: a typo costs nothing, and the caller
        // finds out at the prompt rather than through a 422 the server had to compose.
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite", "-Kind", "OpenCode");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("OpenCode");
        server.RequestCount.ShouldBe(0, "a rejected flag must not reach the server");
    }

    [Test]
    public async Task the_scripts_ValidateSet_is_exactly_the_servers_allowlist()
    {
        // The two halves are separate files in separate languages, and CARD-0099 is the second time
        // they had to move together. A ValidateSet narrower than the server silently makes a kind
        // undispatchable; a ValidateSet wider than it turns a clean refusal into a 422 round trip.
        var lines = await File.ReadAllLinesAsync(
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1"));
        var line = lines
            .Select(l => l.Trim())
            .First(l => l.StartsWith("[ValidateSet('ClaudeCode'", StringComparison.Ordinal));

        foreach (var kind in AgentTaskService.DelegatableKinds)
            line.ShouldContain($"'{kind}'");
        foreach (var kind in Enum.GetValues<AgentKind>())
        {
            if (AgentTaskService.DelegatableKinds.Contains(kind))
                continue;
            line.ShouldNotContain($"'{kind}'");
        }
    }

    [Test]
    public async Task the_resolved_kind_is_echoed_back_to_the_caller()
    {
        // The caller may have chosen nothing and still be running on Grok (a role promoted in
        // config), so the ECHO comes from the server's answer, not from the flag.
        using var server = new StubApi(agentKind: "Grok");
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Goal", "write it");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Grok");
    }

    [Test]
    public async Task a_ClaudeCode_task_is_announced_exactly_as_it_always_was()
    {
        using var server = new StubApi(agentKind: "ClaudeCode");
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("queued task");
        run.Output.ShouldNotContain("ClaudeCode", customMessage: "the default kind is not news");
    }

    [Test]
    public async Task IgnoreSubscriptionQuota_sends_ignoreSubscriptionQuota_true()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(
            server, "-Role", "Test", "-Goal", "run anyway", "-IgnoreSubscriptionQuota");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreSubscriptionQuota").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task an_omitted_IgnoreSubscriptionQuota_sends_no_ignoreSubscriptionQuota_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("ignoreSubscriptionQuota", out _)
            .ShouldBeFalse("the flag is sent only when chosen, matching -Kind / -ExpectAbout");
    }

    // ---- harness -------------------------------------------------------------------------------

    // The script runner itself lives in DelegateScriptRunner so S6's end-to-end test invokes
    // delegate.ps1 exactly the way this one does — same host flags, same environment, same
    // argument-list quoting.
    private static Task<(int ExitCode, string Output)> RunDelegateAsync(StubApi server, params string[] args) =>
        DelegateScriptRunner.RunAsync(server.BaseUrl, args);

    /// <summary>
    /// The smallest thing that can answer POST /api/agent-tasks and keep what it was sent.
    /// HttpListener rather than a test host: the point is to exercise the SCRIPT, over real HTTP,
    /// exactly as an agent runs it.
    /// </summary>
    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _agentKind;

        public StubApi(string agentKind = "ClaudeCode")
        {
            _agentKind = agentKind;
            var port = FreePort();
            BaseUrl = $"http://localhost:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
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
                catch (Exception) { return; /* stopped */ }

                RequestCount++;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw)) LastBody = JsonDocument.Parse(raw);
                }

                var payload = Encoding.UTF8.GetBytes(
                    $$"""
                    {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
                     "status":"Queued","modelLevel":"High","warning":null,"agentKind":"{{_agentKind}}"}
                    """);
                context.Response.StatusCode = 201;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { /* already stopped */ }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { /* pump is best-effort */ }
            _listener.Close();
            LastBody?.Dispose();
            _cts.Dispose();
        }
    }
}
