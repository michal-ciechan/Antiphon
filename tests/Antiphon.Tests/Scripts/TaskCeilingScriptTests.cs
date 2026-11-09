using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1610 S4a. The REAL <c>scripts/delegate.ps1</c> against a loopback stub: the Status
/// seat-held and ceiling lines come only from server fields, a declared create budget is checked
/// before any request, and the typed ceiling reply is an exclusive body with no <c>origin</c>.
/// Operator Extend sends the owner-only token header for that one request and never prints it.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TaskCeilingScriptTests
{
    private const string TaskId = "11111111-1111-1111-1111-111111111111";
    private const string EpisodeId = "22222222-2222-2222-2222-222222222222";
    private const string SessionId = "33333333-3333-3333-3333-333333333333";
    private const string TaskToken = "task-token-SENTINEL-7f3e";
    private const string OperatorToken = "operator-token-SECRET-91ab";
    private const string OperatorHeader = "X-Antiphon-Operator-Token";
    private const string SeatHeld = "Failed, session still Running: seat held";
    private const string RetryConsequence = "Retry stops this session and spends a new attempt; Cancel does not apply to a Failed task.";

    [Test]
    [Arguments("seat-held")]
    [Arguments("unknown")]
    [Arguments("ceiling-hold")]
    [Arguments("ceiling-hold-no-phase")]
    [Arguments("final-hold")]
    [Arguments("budget-resolved")]
    [Arguments("budget-requested")]
    [Arguments("budget-default")]
    [Arguments("legacy-detail")]
    public async Task C1610_Status(string arm)
    {
        using var stub = new CeilingStubApi { DetailJson = DetailJson(arm) };
        var run = await RunAsync(stub, tokenFile: true, ["-Status", TaskId]);

        run.ExitCode.ShouldBe(0, run.Output);
        AssertNoSecrets(run.Output);
        var request = stub.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("GET");
        request.Path.ShouldBe($"/api/agent-tasks/{TaskId}");
        request.Headers.ContainsKey(OperatorHeader).ShouldBeFalse();
        var lines = Lines(run.Output);
        var delegateAt = lines.FindIndex(l => l.StartsWith("Delegate: ", StringComparison.Ordinal));
        delegateAt.ShouldBeGreaterThanOrEqualTo(0, run.Output);
        var after = lines.Skip(delegateAt + 1).ToList();
        int At(string exact) => after.FindIndex(l => l == exact);
        int Starting(string prefix) => after.FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal));

        switch (arm)
        {
            case "seat-held":
            {
                var seat = At($"Seat: {SeatHeld}; activity working; session {SessionId}; failure source Ceiling; notice Told");
                seat.ShouldBeGreaterThanOrEqualTo(0, run.Output);
                after[seat + 1].ShouldBe(RetryConsequence, run.Output);
                break;
            }
            case "unknown":
                At($"Seat: session state unknown: release unconfirmed; activity unknown; session {SessionId}; failure source Ceiling; notice none")
                    .ShouldBeGreaterThanOrEqualTo(0, run.Output);
                run.Output.ShouldNotContain("seat held");
                run.Output.ShouldNotContain("Retry stops this session");
                break;
            case "ceiling-hold":
            {
                var ceiling = At("Ceiling: 240 min (rolePolicy); next bound 240 min; cap 720");
                ceiling.ShouldBeGreaterThanOrEqualTo(0, run.Output);
                var hold = after.FindIndex(l => l.StartsWith($"Ceiling hold: episode {EpisodeId}; held ", StringComparison.Ordinal)
                    && l.EndsWith("; bound 240 min; last transcript ToolCall 360s", StringComparison.Ordinal));
                var phase = Starting("Phase: LocalExecution 90-minute limit, deadline ");
                var decide = At($"Decide: delegate.ps1 -Reply {TaskId} -CeilingAction Continue|Extend -EpisodeId {EpisodeId} [-ExtendMinutes M]");
                hold.ShouldBeGreaterThan(ceiling, run.Output);
                phase.ShouldBeGreaterThan(hold, run.Output);
                decide.ShouldBeGreaterThan(phase, run.Output);
                run.Output.ShouldNotContain("Final hold");
                break;
            }
            case "ceiling-hold-no-phase":
                At("Phase: none - no phase deadline covers this tail; the ceiling is its only deadline")
                    .ShouldBeGreaterThanOrEqualTo(0, run.Output);
                Starting("Decide: ").ShouldBeGreaterThanOrEqualTo(0, run.Output);
                break;
            case "final-hold":
                At("Final hold at 720 minutes: Continue and Extend refuse. Operator Extend "
                    + "(-OperatorExtend -ExtendMinutes 1..240 -CeilingReason \"...\") or POST "
                    + $"/api/agent-tasks/{TaskId}/cancel").ShouldBeGreaterThanOrEqualTo(0, run.Output);
                run.Output.ShouldNotContain("Decide:");
                break;
            case "budget-resolved":
            {
                var ceiling = At("Ceiling: 600 min (requested); next bound 600 min; cap 720");
                ceiling.ShouldBeGreaterThanOrEqualTo(0, run.Output);
                after[ceiling + 1].ShouldBe("Ceiling reason: long review", run.Output);
                break;
            }
            case "budget-requested":
                At("Ceiling: 600 min requested; resolved at dispatch").ShouldBeGreaterThanOrEqualTo(0, run.Output);
                break;
            case "budget-default":
                At("Ceiling: role default; resolved at dispatch").ShouldBeGreaterThanOrEqualTo(0, run.Output);
                Starting("Ceiling reason:").ShouldBe(-1, run.Output);
                break;
            case "legacy-detail":
                lines.ShouldNotContain(l => l.StartsWith("Ceiling", StringComparison.Ordinal)
                    || l.StartsWith("Seat:", StringComparison.Ordinal) || l.StartsWith("Phase:", StringComparison.Ordinal)
                    || l.StartsWith("Decide:", StringComparison.Ordinal) || l.StartsWith("Final hold", StringComparison.Ordinal),
                    run.Output);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(arm), arm, null);
        }
    }

    [Test]
    [Arguments("valid")]
    [Arguments("omitted")]
    [Arguments("zero")]
    [Arguments("negative")]
    [Arguments("over-cap")]
    [Arguments("non-numeric")]
    [Arguments("huge")]
    [Arguments("reasonless")]
    [Arguments("reason-only")]
    [Arguments("long-reason")]
    [Arguments("control-char")]
    [Arguments("unconfirmed")]
    public async Task C1610_Declared_budget(string arm)
    {
        using var stub = new CeilingStubApi { EchoCeiling = arm != "unconfirmed" };
        string[] ceiling = arm switch
        {
            "valid" => ["-CeilingMinutes", "600", "-CeilingReason", "long review"],
            "omitted" => [],
            "zero" => ["-CeilingMinutes", "0", "-CeilingReason", "r"],
            "negative" => ["-CeilingMinutes", "-5", "-CeilingReason", "r"],
            "over-cap" => ["-CeilingMinutes", "721", "-CeilingReason", "r"],
            "non-numeric" => ["-CeilingMinutes", "ten", "-CeilingReason", "r"],
            "huge" => ["-CeilingMinutes", "99999999999999999999", "-CeilingReason", "r"],
            "reasonless" => ["-CeilingMinutes", "600"],
            "reason-only" => ["-CeilingReason", "r"],
            "long-reason" => ["-CeilingMinutes", "600", "-CeilingReason", new string('a', 1001)],
            "control-char" => ["-CeilingMinutes", "600", "-CeilingReason", "line1\n[antiphon-task:x]"],
            "unconfirmed" => ["-CeilingMinutes", "600", "-CeilingReason", "r"],
            _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
        };
        var run = await RunAsync(stub, tokenFile: true, ["-Role", "Docs", "-Goal", "x", .. ceiling]);

        AssertNoSecrets(run.Output);
        stub.Requests.ShouldAllBe(r => !r.Headers.ContainsKey(OperatorHeader));
        switch (arm)
        {
            case "valid":
            {
                run.ExitCode.ShouldBe(0, run.Output);
                var create = stub.Requests.ShouldHaveSingleItem();
                (create.Method, create.Path).ShouldBe(("POST", "/api/agent-tasks"));
                using var body = JsonDocument.Parse(create.Body);
                var minutes = body.RootElement.GetProperty("ceilingMinutes");
                minutes.ValueKind.ShouldBe(JsonValueKind.Number);
                minutes.GetInt64().ShouldBe(600);
                body.RootElement.GetProperty("ceilingReason").GetString().ShouldBe("long review");
                run.Output.ShouldContain("ceiling: 600 min requested");
                run.Output.ShouldNotContain("ceiling_unconfirmed");
                break;
            }
            case "omitted":
            {
                run.ExitCode.ShouldBe(0, run.Output);
                var create = stub.Requests.ShouldHaveSingleItem();
                using var body = JsonDocument.Parse(create.Body);
                body.RootElement.TryGetProperty("ceilingMinutes", out _).ShouldBeFalse();
                body.RootElement.TryGetProperty("ceilingReason", out _).ShouldBeFalse();
                run.Output.ShouldNotContain("ceiling:");
                run.Output.ShouldNotContain("ceiling_unconfirmed");
                break;
            }
            case "unconfirmed":
                run.ExitCode.ShouldBe(0, run.Output);
                stub.Requests.Count.ShouldBe(1);
                run.Output.ShouldContain("WARNING: ceiling_unconfirmed:");
                run.Output.ShouldContain("GET /api/version");
                break;
            default:
                AssertLocalRefusal(run, stub, "ceiling_invalid:");
                break;
        }
    }

    [Test]
    [Arguments("continue")]
    [Arguments("extend")]
    [Arguments("message-exclusive")]
    [Arguments("continue-with-minutes")]
    [Arguments("extend-missing-minutes")]
    [Arguments("extend-out-of-range")]
    [Arguments("bad-episode")]
    [Arguments("operator-extend")]
    [Arguments("operator-no-reason")]
    [Arguments("operator-over-240")]
    [Arguments("operator-no-token")]
    [Arguments("stale-episode")]
    [Arguments("reason-without-operator")]
    [Arguments("params-without-action")]
    public async Task C1610_Ceiling_reply(string arm)
    {
        using var stub = new CeilingStubApi();
        if (arm == "stale-episode")
        {
            stub.ReplyStatus = 409;
            stub.ReplyJson = """{"code":"ceiling_episode_stale","detail":"episode 1111 is stale; reload"}""";
        }
        var episode = arm == "bad-episode" ? "not-a-guid" : EpisodeId;
        string[] decision = arm switch
        {
            "continue" or "stale-episode" or "bad-episode" => ["-CeilingAction", "Continue"],
            "extend" => ["-CeilingAction", "Extend", "-ExtendMinutes", "60"],
            "message-exclusive" => ["-CeilingAction", "Continue", "hello"],
            "continue-with-minutes" => ["-CeilingAction", "Continue", "-ExtendMinutes", "30"],
            "extend-missing-minutes" => ["-CeilingAction", "Extend"],
            "extend-out-of-range" => ["-CeilingAction", "Extend", "-ExtendMinutes", "721"],
            "operator-extend" or "operator-no-token" =>
                ["-CeilingAction", "Extend", "-ExtendMinutes", "120", "-OperatorExtend", "-CeilingReason", "operator: long merge"],
            "operator-no-reason" => ["-CeilingAction", "Extend", "-ExtendMinutes", "120", "-OperatorExtend"],
            "operator-over-240" => ["-CeilingAction", "Extend", "-ExtendMinutes", "241", "-OperatorExtend", "-CeilingReason", "r"],
            // Review e52b2bed D-2: a reason is the operator's alone, and ceiling flags need an action.
            "reason-without-operator" => ["-CeilingAction", "Extend", "-ExtendMinutes", "60", "-CeilingReason", "r"],
            "params-without-action" => ["-ExtendMinutes", "60"],
            _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
        };
        var run = await RunAsync(stub, tokenFile: arm != "operator-no-token",
            ["-Reply", TaskId, "-EpisodeId", episode, .. decision]);

        AssertNoSecrets(run.Output);
        switch (arm)
        {
            case "continue":
            case "extend":
            case "operator-extend":
            {
                run.ExitCode.ShouldBe(0, run.Output);
                var reply = stub.Requests.ShouldHaveSingleItem();
                (reply.Method, reply.Path).ShouldBe(("POST", $"/api/agent-tasks/{TaskId}/reply"));
                using var body = JsonDocument.Parse(reply.Body);
                var root = body.RootElement;
                string[] keys = arm switch
                {
                    "continue" => ["ceilingAction", "episodeId"],
                    "extend" => ["ceilingAction", "episodeId", "extendMinutes"],
                    _ => ["ceilingAction", "episodeId", "extendMinutes", "operatorExtend", "ceilingReason"],
                };
                string.Join(",", root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal))
                    .ShouldBe(string.Join(",", keys.Order(StringComparer.Ordinal)), $"typed body only: {reply.Body}");
                root.GetProperty("episodeId").GetString().ShouldBe(EpisodeId);
                var action = arm == "continue" ? "Continue" : "Extend";
                root.GetProperty("ceilingAction").GetString().ShouldBe(action);
                if (arm == "extend") root.GetProperty("extendMinutes").GetInt32().ShouldBe(60);
                if (arm == "operator-extend")
                {
                    reply.Headers.GetValueOrDefault(OperatorHeader).ShouldBe(OperatorToken);
                    root.GetProperty("extendMinutes").GetInt32().ShouldBe(120);
                    root.GetProperty("operatorExtend").GetBoolean().ShouldBeTrue();
                    root.GetProperty("ceilingReason").GetString().ShouldBe("operator: long merge");
                }
                else
                {
                    reply.Headers.ContainsKey(OperatorHeader).ShouldBeFalse("an ordinary decision never carries the operator token");
                }
                run.Output.ShouldContain($"Decision recorded for task {TaskId} (episode {EpisodeId}): {action}. "
                    + "The session continues; -Status shows the next bound.");
                break;
            }
            case "operator-no-token":
                AssertLocalRefusal(run, stub, "operator_token_unavailable:");
                break;
            case "reason-without-operator":
                AssertLocalRefusal(run, stub, "ceiling_invalid: -CeilingReason on a reply applies only to -OperatorExtend.");
                break;
            case "params-without-action":
                AssertLocalRefusal(run, stub,
                    "ceiling_invalid: -EpisodeId, -ExtendMinutes, -OperatorExtend and -CeilingReason need -CeilingAction.");
                break;
            case "stale-episode":
                run.ExitCode.ShouldNotBe(0, run.Output);
                stub.Requests.ShouldHaveSingleItem().Path.ShouldBe($"/api/agent-tasks/{TaskId}/reply");
                run.Output.ShouldContain("ceiling_episode_stale");
                run.Output.ShouldContain("episode 1111 is stale; reload");
                run.Output.ShouldNotContain("Decision recorded");
                break;
            default:
                AssertLocalRefusal(run, stub, "ceiling_invalid:");
                break;
        }
    }

    private static void AssertLocalRefusal((int ExitCode, string Output) run, CeilingStubApi stub, string code)
    {
        run.ExitCode.ShouldBe(2, run.Output);
        run.Output.ShouldContain(code);
        stub.Requests.Count.ShouldBe(0, "a local refusal sends no request");
    }

    private static void AssertNoSecrets(string output)
    {
        output.ShouldNotContain("SENTINEL");
        output.ShouldNotContain("SECRET");
    }

    private static List<string> Lines(string output) =>
        output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).ToList();

    private static async Task<(int ExitCode, string Output)> RunAsync(CeilingStubApi stub, bool tokenFile, string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "c1610-s4a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var tokenPath = Path.Combine(directory, "operator-token");
            if (tokenFile) await File.WriteAllTextAsync(tokenPath, OperatorToken);
            var environment = new Dictionary<string, string?>
            {
                ["ANTIPHON_TASK_TOKEN"] = TaskToken,
                ["ANTIPHON_OPERATOR_TOKEN_FILE"] = tokenPath,
            };
            return await DelegateScriptRunner.RunAsync(stub.BaseUrl, environment, workingDirectory: null, args);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string DetailJson(string arm)
    {
        var failed = arm is "seat-held" or "unknown";
        var detail = new Dictionary<string, object?>
        {
            ["summary"] = new Dictionary<string, object?>
            {
                ["id"] = TaskId, ["shortId"] = "11111111", ["status"] = failed ? "Failed" : "Working",
                ["title"] = "c1610 hold", ["kind"] = "Worker", ["role"] = "Review", ["modelLevel"] = "High",
            },
            ["goal"] = "x",
        };
        if (failed)
        {
            detail["failedSession"] = new Dictionary<string, object?>
            {
                ["summary"] = arm == "seat-held" ? SeatHeld : "session state unknown: release unconfirmed",
                ["activity"] = arm == "seat-held" ? "working" : "unknown",
                ["taskId"] = TaskId, ["cardId"] = null, ["sessionId"] = SessionId,
                ["failureSource"] = "Ceiling", ["noticeState"] = arm == "seat-held" ? "Told" : null,
            };
        }
        if (arm is "ceiling-hold" or "ceiling-hold-no-phase" or "final-hold")
        {
            var cap = arm == "final-hold";
            detail["ceilingHold"] = new Dictionary<string, object?>
            {
                ["episodeId"] = EpisodeId, ["heldAt"] = "2026-11-02T10:00:00Z", ["attempt"] = 1,
                ["sessionStartedAt"] = "2026-11-02T06:00:00Z", ["initialMinutes"] = 240, ["initialSource"] = "rolePolicy",
                ["nextCeilingMinutes"] = cap ? 720 : 240, ["lastTranscriptKind"] = "ToolCall", ["lastTranscriptAgeSeconds"] = 360,
                ["phaseKind"] = arm == "ceiling-hold-no-phase" ? null : "LocalExecution",
                ["phaseLimitMinutes"] = arm == "ceiling-hold-no-phase" ? null : 90,
                ["phaseDeadlineAt"] = arm == "ceiling-hold-no-phase" ? null : "2026-11-02T11:00:00Z",
                ["capReached"] = cap, ["question"] = "Task 11111111 reached its ceiling. next=decide",
            };
        }
        if (arm == "ceiling-hold")
            detail["ceilingBudget"] = Budget(null, null, 240, "rolePolicy", 240);
        if (arm == "budget-resolved")
            detail["ceilingBudget"] = Budget(600, "long review", 600, "requested", 600);
        if (arm == "budget-requested")
            detail["ceilingBudget"] = Budget(600, "r", null, null, null);
        if (arm == "budget-default")
            detail["ceilingBudget"] = Budget(null, null, null, null, null);
        return JsonSerializer.Serialize(detail);

        static Dictionary<string, object?> Budget(int? requested, string? reason, int? resolved, string? source, double? next) => new()
        {
            ["requestedMinutes"] = requested, ["requestedReason"] = reason, ["resolvedMinutes"] = resolved,
            ["resolvedSource"] = source, ["nextCeilingMinutes"] = next, ["absoluteCapMinutes"] = 720,
        };
    }

    private sealed record StubRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body);

    /// <summary>Records every request; unrouted paths answer 404 and stay recorded, so a stray POST is visible.</summary>
    private sealed class CeilingStubApi : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<StubRequest> _requests = new();
        private readonly Task _pump;

        public CeilingStubApi()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(out _listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public string DetailJson { get; init; } = "{}";
        public bool EchoCeiling { get; init; } = true;
        public int ReplyStatus { get; set; } = 200;
        public string ReplyJson { get; set; } = $$"""{"id":"{{TaskId}}","status":"Working"}""";
        public IReadOnlyList<StubRequest> Requests => _requests.ToList();

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                string raw;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) raw = await reader.ReadToEndAsync();
                var headers = context.Request.Headers.AllKeys.Where(k => k is not null)
                    .ToDictionary(k => k!, k => context.Request.Headers[k] ?? "", StringComparer.OrdinalIgnoreCase);
                var method = context.Request.HttpMethod;
                var path = context.Request.Url!.AbsolutePath;
                _requests.Enqueue(new StubRequest(method, path, headers, raw));

                var (status, json) = (method, path) switch
                {
                    ("POST", "/api/agent-tasks") => (201, CreatedJson(raw)),
                    ("GET", $"/api/agent-tasks/{TaskId}") => (200, DetailJson),
                    ("POST", $"/api/agent-tasks/{TaskId}/reply") => (ReplyStatus, ReplyJson),
                    _ => (404, """{"code":"stub_unrouted"}"""),
                };
                var payload = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        private string CreatedJson(string body)
        {
            var created = new Dictionary<string, object?>
            {
                ["id"] = TaskId, ["shortId"] = "11111111", ["status"] = "Queued", ["modelLevel"] = "High",
                ["warning"] = null, ["agentKind"] = "ClaudeCode",
            };
            if (EchoCeiling && !string.IsNullOrWhiteSpace(body))
            {
                using var posted = JsonDocument.Parse(body);
                if (posted.RootElement.TryGetProperty("ceilingMinutes", out var minutes) && minutes.ValueKind == JsonValueKind.Number)
                    created["requestedCeilingMinutes"] = minutes.GetInt64();
            }
            return JsonSerializer.Serialize(created);
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
