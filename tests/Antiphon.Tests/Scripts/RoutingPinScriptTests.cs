using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0305 S4: <c>scripts/routing-pin.ps1</c> and <c>delegate.ps1</c>'s two pin flags, driven as
/// the REAL scripts under pwsh against a stub API. A string match on the source would pass just as
/// happily if the flag never reached the request body.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RoutingPinScriptTests
{
    [Test]
    public async Task Set_puts_the_grain_provenance_and_route()
    {
        using var server = new StubApi();
        var run = await RunPinAsync(
            server, "set", "-Role", "Plan", "-Card", "CARD-0304",
            "-Provenance", "Human", "-Strength", "Required",
            "-Kind", "Codex", "-Level", "Frontier", "-Reason", "CARD-0304 plans on Sol");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("PUT");
        server.LastPath.ShouldBe("/api/routing-pins");
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("card").GetString().ShouldBe("CARD-0304");
        body.RootElement.GetProperty("role").GetString().ShouldBe("Plan");
        body.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
        body.RootElement.GetProperty("strength").GetString().ShouldBe("Required");
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Codex");
        body.RootElement.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
    }

    [Test]
    public async Task Set_without_a_card_writes_a_stage_wide_pin()
    {
        using var server = new StubApi();
        var run = await RunPinAsync(
            server, "set", "-Role", "Code", "-Provenance", "Human", "-Strength", "Required",
            "-Kind", "Grok", "-Forbidden", "fable, opus");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("card", out _)
            .ShouldBeFalse("an omitted -Card is what makes the pin stage-wide");
        var forbidden = body.RootElement.GetProperty("forbiddenAliases").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        forbidden.ShouldBe(["fable", "opus"]);
    }

    [Test]
    public async Task A_naive_NotBefore_is_refused_before_any_request()
    {
        using var server = new StubApi();
        var run = await RunPinAsync(
            server, "set", "-Role", "Plan", "-Card", "CARD-0301",
            "-NotBefore", "2026-09-03T00:00:00");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("Naive");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Get_filters_by_card_and_role()
    {
        using var server = new StubApi();
        var run = await RunPinAsync(server, "get", "-Card", "CARD-0304", "-Role", "Plan");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("GET");
        server.LastPath.ShouldBe("/api/routing-pins?card=CARD-0304&role=Plan");
    }

    [Test]
    public async Task Clear_looks_the_grain_up_and_deletes_by_id()
    {
        using var server = new StubApi();
        var run = await RunPinAsync(server, "clear", "-Role", "Plan", "-Card", "CARD-0304");

        run.ExitCode.ShouldBe(0, run.Output);
        // The API takes an id; the caller thinks in grains. The lookup is the script's job.
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe("/api/routing-pins/22222222-2222-2222-2222-222222222222");
    }

    [Test]
    public async Task Delegate_IgnoreRoutingPin_posts_ignoreRoutingPin_true()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "-Role", "Plan", "-Goal", "plan it", "-IgnoreRoutingPin");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastCreateBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreRoutingPin").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task An_omitted_IgnoreRoutingPin_sends_nothing_at_all()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "-Role", "Plan", "-Goal", "plan it");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastCreateBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("ignoreRoutingPin", out _)
            .ShouldBeFalse("an omitted flag must leave the request exactly as it was before it existed");
    }

    [Test]
    public async Task Delegate_Pin_PUTs_a_human_required_pin_for_the_bound_card()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "-Role", "Code", "-Goal", "build it", "-Card", "CARD-0305", "-Pin");

        run.ExitCode.ShouldBe(0, run.Output);
        var pin = server.LastPinBody.ShouldNotBeNull();
        pin.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
        pin.RootElement.GetProperty("strength").GetString().ShouldBe("Required");
        pin.RootElement.GetProperty("role").GetString().ShouldBe("Code");
        // The RESOLVED kind, not the typed one: a caller who passed no -Kind and got Grok from the
        // role policy still means "next time, the same".
        pin.RootElement.GetProperty("agentKind").GetString().ShouldBe("Grok");
        pin.RootElement.GetProperty("card").GetString()
            .ShouldBe("33333333-3333-3333-3333-333333333333");
        run.Output.ShouldContain("pinned CARD-0305");
    }

    [Test]
    public async Task Delegate_Pin_with_nothing_that_could_bind_a_card_is_refused_locally()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "-Role", "Code", "-Goal", "build it", "-Pin");

        // A stage-wide pin changes routing for every card; it must not fall out of one dispatch.
        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("stage-wide");
        server.RequestCount.ShouldBe(0, "a rejected flag must not create the task either");
    }

    private static Task<(int ExitCode, string Output)> RunPinAsync(StubApi server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "routing-pin.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = server.BaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
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

        public StubApi()
        {
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
        public string? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public JsonDocument? LastBody { get; private set; }
        public JsonDocument? LastCreateBody { get; private set; }
        public JsonDocument? LastPinBody { get; private set; }
        public int RequestCount { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                RequestCount++;
                LastMethod = context.Request.HttpMethod;
                var path = context.Request.Url?.PathAndQuery;
                LastPath = path;
                JsonDocument? body = null;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw))
                        body = JsonDocument.Parse(raw);
                }

                if (body is not null)
                {
                    LastBody = body;
                    if (path?.StartsWith("/api/agent-tasks", StringComparison.Ordinal) == true)
                        LastCreateBody = body;
                    else if (path?.StartsWith("/api/routing-pins", StringComparison.Ordinal) == true)
                        LastPinBody = body;
                }

                if (context.Request.HttpMethod == "DELETE")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    continue;
                }

                var payload = Encoding.UTF8.GetBytes(
                    path?.StartsWith("/api/agent-tasks", StringComparison.Ordinal) == true
                        ? CreatedJson
                        : path?.StartsWith("/api/routing-pins", StringComparison.Ordinal) == true
                            && context.Request.HttpMethod == "GET"
                            ? ListJson
                            : PinJson);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        private const string PinJson =
            """
            {"id":"22222222-2222-2222-2222-222222222222","cardId":null,"cardIdentifier":null,
             "role":"Plan","provenance":"Human","strength":"Required","agentKind":"Grok",
             "modelLevel":"High","modelAlias":"grok-4.6","agentId":null,"forbiddenAliases":[],
             "notBefore":null,"notAfter":null,"reason":"test","sourceTaskId":null,
             "createdAt":"2026-09-01T00:00:00Z","updatedAt":"2026-09-01T00:00:00Z"}
            """;

        private const string ListJson =
            """
            {"pins":[{"id":"22222222-2222-2222-2222-222222222222",
             "cardId":"33333333-3333-3333-3333-333333333333","cardIdentifier":"CARD-0304",
             "role":"Plan","provenance":"Human","strength":"Required","agentKind":"Codex",
             "modelLevel":"Frontier","modelAlias":"gpt-5.6-sol","agentId":null,
             "forbiddenAliases":[],"notBefore":null,"notAfter":null,"reason":"test",
             "sourceTaskId":null,"createdAt":"2026-09-01T00:00:00Z",
             "updatedAt":"2026-09-01T00:00:00Z"}]}
            """;

        private const string CreatedJson =
            """
            {"id":"44444444-4444-4444-4444-444444444444","shortId":"44444444","status":"Queued",
             "modelLevel":"High","warning":null,"agentKind":"Grok","noReplyRouting":true,
             "scopeOverlaps":[],"cardId":"33333333-3333-3333-3333-333333333333",
             "cardIdentifier":"CARD-0305","followUpMessage":null}
            """;

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
