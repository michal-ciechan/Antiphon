using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0305 S4: the routing-pin command (Antiphon.Nightly.RoutingPinCli, which replaced scripts/routing-pin.ps1 in unit 24 of
/// the 2026-11-22 plan) in process on a recording transport, and the delegate command's two pin flags (Antiphon.Nightly.DelegateCli,
/// which replaced scripts/delegate.ps1 in unit 28) in process over real HTTP against a stub API. A string match on the source
/// would pass just as happily if the flag never reached the request body.
/// </summary>
[Category("Integration")]
public sealed class RoutingPinScriptTests
{
    [Test]
    public void C470_routing_pin_accepts_mutation()
    {
        var http = PinApi();
        var run = RunPin(http, "set", "--role", "Mutation", "--kind", "Codex", "--level", "Frontier");
        run.Exit.ShouldBe(0, run.All);
        http.Calls.Single().Method.ShouldBe("PUT");
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.GetProperty("role").GetString().ShouldBe("Mutation");
    }

    [Test]
    public Task C470_delegate_mutation_posts_shared_retained_directory() => AssertRetainedDispatch("Mutation");

    [Test]
    public Task C470_debug_bootstrap_posts_separate_worker() => AssertRetainedDispatch("Debug");

    private static async Task AssertRetainedDispatch(string role)
    {
        using var server = new StubApi();
        var directory = Path.Combine(Path.GetTempPath(), "card-task-aabbccdd");
        var goal = "code-task=ce744e22; sha=" + new string('a', 40)
            + "\nartifact: docs/superpowers/plans/2026-09-09-card-0470-code-mutation-split-plan.md"
            + "\nPC-1a..PC-13 (17 variants); review-required: yes; restart: server\n"
            + File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "server/Bundles/stage-mutation.md"));
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, "create", "--role", role, "--shared", "--dir", directory,
            "--card", "CARD-0470", "--goal", goal);
        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastCreateBody.ShouldNotBeNull().RootElement;
        body.GetProperty("role").GetString().ShouldBe(role);
        body.GetProperty("workspace").GetString().ShouldBe("Shared");
        body.GetProperty("workingDirectory").GetString().ShouldBe(directory);
        body.GetProperty("goal").GetString().ShouldBe(goal);
        body.TryGetProperty("agentId", out _).ShouldBeFalse();
        body.TryGetProperty("followUpOnTask", out _).ShouldBeFalse();
    }

    [Test]
    public void Set_puts_the_grain_provenance_and_route()
    {
        var http = PinApi();
        var run = RunPin(http, "set", "--role", "Plan", "--card", "CARD-0304", "--provenance", "Human", "--strength", "Required",
            "--kind", "Codex", "--level", "Frontier", "--reason", "CARD-0304 plans on Sol");

        run.Exit.ShouldBe(0, run.All);
        var call = http.Calls.Single();
        (call.Method, call.Path).ShouldBe(("PUT", "/api/routing-pins"));
        using var body = JsonDocument.Parse(call.Body!);
        body.RootElement.GetProperty("card").GetString().ShouldBe("CARD-0304");
        body.RootElement.GetProperty("role").GetString().ShouldBe("Plan");
        body.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
        body.RootElement.GetProperty("strength").GetString().ShouldBe("Required");
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Codex");
        body.RootElement.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
        body.RootElement.GetProperty("reason").GetString().ShouldBe("CARD-0304 plans on Sol");
        run.Output.Trim().ShouldBe("stage-wide  Plan  Human Required  Grok/High (grok-4.7)  test");
    }

    [Test]
    public void Set_Candidates_puts_the_list_in_order()
    {
        var http = PinApi();
        var run = RunPin(http, "set", "--role", "Plan", "--provenance", "Human", "--strength", "Required",
            "--candidates", "ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier", "--reason", "plan on fable, opus, then sol");

        run.Exit.ShouldBe(0, run.All);
        http.Calls.Single().Method.ShouldBe("PUT");
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.TryGetProperty("agentKind", out _).ShouldBeFalse();
        body.RootElement.TryGetProperty("modelLevel", out _).ShouldBeFalse();
        var candidates = body.RootElement.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Count.ShouldBe(3);
        candidates[0].GetProperty("agentKind").GetString().ShouldBe("ClaudeCode");
        candidates[0].GetProperty("modelLevel").GetString().ShouldBe("Frontier");
        candidates[1].GetProperty("agentKind").GetString().ShouldBe("ClaudeCode");
        candidates[1].GetProperty("modelLevel").GetString().ShouldBe("High");
        candidates[2].GetProperty("agentKind").GetString().ShouldBe("Codex");
        candidates[2].GetProperty("modelLevel").GetString().ShouldBe("Frontier");
    }

    [Test]
    public void Set_Kind_together_with_Candidates_is_refused_locally()
    {
        var http = PinApi();
        var run = RunPin(http, "set", "--role", "Plan", "--kind", "Grok", "--candidates", "ClaudeCode/Frontier,ClaudeCode/High");

        run.Exit.ShouldBe(1);
        run.Error.ShouldContain("shorthand");
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Get_prints_the_head_plus_count()
    {
        var http = PinApi();
        var run = RunPin(http, "get");

        run.Exit.ShouldBe(0, run.All);
        run.Output.ShouldContain("ClaudeCode/Frontier (fable)");
        run.Output.ShouldContain("+2:");
        run.Output.ShouldContain("ClaudeCode/High (opus)");
        run.Output.ShouldContain("Codex/Frontier (gpt-6-astra)");
        run.Output.Trim().ShouldBe("CARD-0304  Plan  Human Required  ClaudeCode/Frontier (fable) +2: ClaudeCode/High (opus), Codex/Frontier (gpt-6-astra)  test");
    }

    [Test]
    public void Set_without_a_card_writes_a_stage_wide_pin()
    {
        var http = PinApi();
        var run = RunPin(http, "set", "--role", "Code", "--provenance", "Human", "--strength", "Required", "--kind", "Grok", "--forbidden", "fable, opus");

        run.Exit.ShouldBe(0, run.All);
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.TryGetProperty("card", out _)
            .ShouldBeFalse("an omitted --card is what makes the pin stage-wide");
        var forbidden = body.RootElement.GetProperty("forbiddenAliases").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        forbidden.ShouldBe(["fable", "opus"]);
    }

    [Test]
    public void A_naive_NotBefore_is_refused_before_any_request()
    {
        var http = PinApi();
        var run = RunPin(http, "set", "--role", "Plan", "--card", "CARD-0301", "--not-before", "2026-09-03T00:00:00");

        run.Exit.ShouldBe(1);
        run.Error.ShouldContain("Naive");
        run.Error.ShouldStartWith("--not-before must be ISO-8601 UTC", Case.Sensitive);
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Get_filters_by_card_and_role()
    {
        var http = PinApi();
        var run = RunPin(http, "get", "--card", "CARD-0304", "--role", "plan");

        run.Exit.ShouldBe(0, run.All);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("GET", "/api/routing-pins?card=CARD-0304&role=Plan"));
    }

    [Test]
    public void Clear_looks_the_grain_up_and_deletes_by_id()
    {
        var http = PinApi();
        var run = RunPin(http, "clear", "--role", "Plan", "--card", "CARD-0304");

        run.Exit.ShouldBe(0, run.All);
        // The API takes an id; the caller thinks in grains. The lookup is the command's job.
        http.Calls.Select(c => c.Method + " " + c.Path).ShouldBe(
            ["GET /api/routing-pins?card=CARD-0304&role=Plan", "DELETE /api/routing-pins/22222222-2222-2222-2222-222222222222"]);
        run.Output.Trim().ShouldStartWith("cleared CARD-0304  Plan  Human Required", Case.Sensitive);

        // The stage grain does not match a card pin: nothing is deleted.
        var stage = PinApi();
        var none = RunPin(stage, "clear", "--role", "Plan");
        none.Exit.ShouldBe(0, none.All);
        none.Output.Trim().ShouldBe("No active routing pin for that grain.");
        stage.Calls.Select(c => c.Method).ShouldBe(["GET"]);
    }

    [Test]
    public void Malformed_candidates_and_command_lines_are_refused()
    {
        RoutingPinCli.ParseCandidates("ClaudeCode/Frontier, Grok ,", out _)!.ToJsonString().ShouldBe(
            """[{"agentKind":"ClaudeCode","modelLevel":"Frontier"},{"agentKind":"Grok"}]""");
        RoutingPinCli.ParseCandidates("Codex/", out var half).ShouldBeNull();
        half.ShouldBe("'Codex/' is not a Kind/Level pair or a bare Kind. Use ClaudeCode/Frontier or Grok.");
        RoutingPinCli.ParseCandidates("*/High", out var levelOnly).ShouldBeNull();
        levelOnly.ShouldBe("'*/High' is a level-only token; use --level for a one-candidate pin, not --candidates.");
        RoutingPinCli.ParseCandidates(" , ", out var empty).ShouldBeNull();
        empty.ShouldBe("set --candidates needs at least one Kind/Level or bare Kind token.");

        Should.Throw<NightlyUsageException>(() => RoutingPinCliOptions.Parse(["set", "--kind", "Grok"], null, null)).Message
            .ShouldBe("set requires --role. A pin is per STAGE; add --card to narrow it to one card");
        Should.Throw<NightlyUsageException>(() => RoutingPinCliOptions.Parse(["clear"], null, null)).Message
            .ShouldStartWith("clear requires --role");
        Should.Throw<NightlyUsageException>(() => RoutingPinCliOptions.Parse(["set", "--role", "Check"], null, null)).Message
            .ShouldStartWith("--role must be one of Investigate, Plan,");
        Should.Throw<NightlyUsageException>(() => RoutingPinCliOptions.Parse(["get", "--kind", "Grok"], null, null)).Message.ShouldBe("--kind does not apply to get");
        Should.Throw<NightlyUsageException>(() => RoutingPinCliOptions.Parse(["-Role", "Plan"], null, null));
        RoutingPinCliOptions.Parse([], null, null).Verb.ShouldBe("get");
    }

    [Test]
    public async Task Delegate_IgnoreRoutingPin_posts_ignoreRoutingPin_true()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "create", "--role", "Plan", "--goal", "plan it", "--ignore-routing-pin");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastCreateBody.ShouldNotBeNull();
        body.RootElement.GetProperty("ignoreRoutingPin").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task An_omitted_IgnoreRoutingPin_sends_nothing_at_all()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "create", "--role", "Plan", "--goal", "plan it");

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
            server.BaseUrl, "create", "--role", "Code", "--goal", "build it", "--card", "CARD-0305", "--pin");

        run.ExitCode.ShouldBe(0, run.Output);
        var pin = server.LastPinBody.ShouldNotBeNull();
        pin.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
        pin.RootElement.GetProperty("strength").GetString().ShouldBe("Required");
        pin.RootElement.GetProperty("role").GetString().ShouldBe("Code");
        // The RESOLVED kind, not the typed one: a caller who passed no --kind and got Grok from the
        // role policy still means "next time, the same".
        pin.RootElement.GetProperty("agentKind").GetString().ShouldBe("Grok");
        pin.RootElement.GetProperty("card").GetString()
            .ShouldBe("33333333-3333-3333-3333-333333333333");
        run.Output.ShouldContain("pinned CARD-0305");
        run.Output.ShouldContain("REPLACES the 2-candidate pin");
    }

    [Test]
    public void Delegate_and_routing_pin_scripts_are_ascii_only()
    {
        foreach (var name in new[] { "lib/Antiphon.Nightly/DelegateCli.cs", "lib/Antiphon.Nightly/RoutingPinCli.cs", "lib/Antiphon.Nightly/CapabilityCli.cs", "lib/Antiphon.Nightly/BoardCli.cs" })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name);
            File.Exists(path).ShouldBeTrue(path);
            var bytes = File.ReadAllBytes(path);
            var firstNonAscii = Array.FindIndex(bytes, static b => b > 127);
            firstNonAscii.ShouldBe(-1, $"{name} has a non-ASCII byte at offset {firstNonAscii}");
        }
    }

    [Test]
    public async Task Delegate_Pin_with_nothing_that_could_bind_a_card_is_refused_locally()
    {
        using var server = new StubApi();
        var run = await DelegateScriptRunner.RunAsync(
            server.BaseUrl, "create", "--role", "Code", "--goal", "build it", "--pin");

        // A stage-wide pin changes routing for every card; it must not fall out of one dispatch.
        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("stage-wide");
        server.RequestCount.ShouldBe(0, "a rejected flag must not create the task either");
    }

    // The routing pin API as the stub answers it: GET the list, DELETE 204, a PUT the stored pin.
    private static RecordingOperatorHttp PinApi() => new(call => call.Method switch
    {
        "GET" => new(200, ListJson),
        "DELETE" => new(204, ""),
        _ => new(200, PinJson),
    });

    private static BoardCommandRun.Result RunPin(RecordingOperatorHttp http, params string[] args) =>
        BoardCommandRun.Run(host => RoutingPinCli.Run(RoutingPinCliOptions.Parse(args, BoardCommandRun.Api, null), host, http.Transport));

    private const string PinJson =
        """
        {"id":"22222222-2222-2222-2222-222222222222","cardId":null,"cardIdentifier":null,
         "role":"Plan","provenance":"Human","strength":"Required","agentKind":"Grok",
         "modelLevel":"High","modelAlias":"grok-4.7","agentId":null,"forbiddenAliases":[],
         "notBefore":null,"notAfter":null,"reason":"test","sourceTaskId":null,
         "createdAt":"2026-09-01T00:00:00Z","updatedAt":"2026-09-01T00:00:00Z",
         "candidates":[{"agentKind":"Grok","modelLevel":"High","alias":"grok-4.7","availableNow":true,"unavailableReason":null}],
         "candidateCount":1}
        """;

    private const string ListJson =
        """
        {"pins":[{"id":"22222222-2222-2222-2222-222222222222",
         "cardId":"33333333-3333-3333-3333-333333333333","cardIdentifier":"CARD-0304",
         "role":"Plan","provenance":"Human","strength":"Required","agentKind":"ClaudeCode",
         "modelLevel":"Frontier","modelAlias":"fable","agentId":null,
         "forbiddenAliases":[],"notBefore":null,"notAfter":null,"reason":"test",
         "sourceTaskId":null,"createdAt":"2026-09-01T00:00:00Z",
         "updatedAt":"2026-09-01T00:00:00Z",
         "candidateCount":3,
         "candidates":[
           {"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","availableNow":true,"unavailableReason":null},
           {"agentKind":"ClaudeCode","modelLevel":"High","alias":"opus","availableNow":true,"unavailableReason":null},
           {"agentKind":"Codex","modelLevel":"Frontier","alias":"gpt-6-astra","availableNow":true,"unavailableReason":null}
         ]}]}
        """;

    private const string CreatedJson =
        """
        {"id":"44444444-4444-4444-4444-444444444444","shortId":"44444444","status":"Queued",
         "modelLevel":"High","warning":null,"agentKind":"Grok","noReplyRouting":true,
         "scopeOverlaps":[],"cardId":"33333333-3333-3333-3333-333333333333",
         "cardIdentifier":"CARD-0305","followUpMessage":null,
         "routing":{"complexity":null,"chainProvenance":null,"chainSource":"config",
          "source":"pin:CARD-0305 Code","candidates":[
            {"agentKind":"Grok","modelLevel":"Frontier","alias":"grok-4.7","outcome":"chosen","reason":null,"origin":"pin"},
            {"agentKind":"ClaudeCode","modelLevel":"High","alias":"opus","outcome":"skipped","reason":"already chose an earlier candidate","origin":"rolePolicy"}
          ],"available":[],"walked":true,"role":"Code","chainRole":null}}
        """;

    // The loopback API the delegate command runs against: agent-task creates, routing pin reads and writes.
    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public StubApi()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(out _listener);
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
