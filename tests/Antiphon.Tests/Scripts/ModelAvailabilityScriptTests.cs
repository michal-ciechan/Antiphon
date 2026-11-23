using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0309 S1. The model-availability command (Antiphon.Nightly.ModelAvailabilityCli, which replaced
/// scripts/model-availability.ps1 in unit 24 of the 2026-11-22 plan) get/hold/clear in process, on a recording transport.
/// Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class ModelAvailabilityScriptTests
{
    private const string Answer = """
        {"holds":[],"available":["opus","sonnet","haiku","grok-4.7"],
         "id":"11111111-1111-1111-1111-111111111111","kind":"ClaudeCode",
         "modelAlias":"fable","source":"Manual","disabledUntil":"2026-09-04T00:00:00Z",
         "hitAt":"2026-09-01T00:00:00Z","reason":"weekly cap","rawText":null,
         "sourceSessionId":null,"sourceTaskId":null}
        """;

    [Test]
    public void Hold_puts_disabledUntil_and_reason()
    {
        var http = Stub();
        var run = Run(http, null, "hold", "--kind", "ClaudeCode", "--model", "fable", "--until", "2026-09-04T00:00:00Z", "--reason", "weekly cap");

        run.Exit.ShouldBe(0, run.All);
        var call = http.Calls.Single();
        call.Method.ShouldBe("PUT");
        call.Path.ShouldBe("/api/model-availability/ClaudeCode/fable");
        using var body = JsonDocument.Parse(call.Body!);
        body.RootElement.GetProperty("disabledUntil").GetString().ShouldBe("2026-09-04T00:00:00Z");
        body.RootElement.GetProperty("reason").GetString().ShouldBe("weekly cap");
        run.Output.Trim().ShouldBe("ClaudeCode  fable  Manual  until 2026-09-04T00:00:00Z  weekly cap");
    }

    [Test]
    public void Hold_star_encodes_the_alias()
    {
        var http = Stub();
        var run = Run(http, null, "hold", "--kind", "claudecode", "--model", "*", "--until", "2026-09-04T00:00:00+01:00");

        run.Exit.ShouldBe(0, run.All);
        http.Calls.Single().Method.ShouldBe("PUT");
        http.Calls.Single().Path.ShouldBe("/api/model-availability/ClaudeCode/%2A", "the kind in its listed spelling, the alias escaped");
    }

    [Test]
    public void Hold_without_Until_omits_disabledUntil()
    {
        var http = Stub();
        var run = Run(http, null, "hold", "--kind", "ClaudeCode", "--model", "fable");

        run.Exit.ShouldBe(0, run.All);
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.TryGetProperty("disabledUntil", out _).ShouldBeFalse();
        body.RootElement.GetProperty("reason").GetString().ShouldBe("manual hold");
    }

    [Test]
    public void Naive_Until_is_refused_before_any_request()
    {
        var http = Stub();
        var run = Run(http, null, "hold", "--kind", "ClaudeCode", "--model", "fable", "--until", "2026-09-04T00:00:00");

        run.Exit.ShouldBe(1);
        run.Error.ShouldContain("Naive");
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Clear_deletes_the_hold()
    {
        var http = new RecordingOperatorHttp(_ => new(204, ""));
        var run = Run(http, "task-secret", "clear", "--kind", "ClaudeCode", "--model", "fable");

        run.Exit.ShouldBe(0, run.All);
        run.Output.ShouldBeEmpty();
        var call = http.Calls.Single();
        (call.Method, call.Path, call.Body).ShouldBe(("DELETE", "/api/model-availability/ClaudeCode/fable", (string?)null));
        call.Headers["X-Antiphon-Task-Token"].ShouldBe("task-secret");
        run.All.ShouldNotContain("task-secret");
    }

    [Test]
    public void Get_prints_available()
    {
        var http = Stub();
        var run = Run(http, null, "get");

        run.Exit.ShouldBe(0, run.All);
        http.Calls.Single().Method.ShouldBe("GET");
        http.Calls.Single().Headers.ShouldBeEmpty();
        run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(["All models available.", "available: opus, sonnet, haiku, grok-4.7"]);
    }

    [Test]
    public void Get_lists_the_holds_and_json_prints_the_answer()
    {
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "ModelAvailabilityCli.cs")).ShouldAllBe(b => b < 128);
        var http = new RecordingOperatorHttp(_ => new(200, """
            {"holds":[{"kind":"Grok","modelAlias":"*","source":"Manual","disabledUntil":null,"reason":"quota"},
                      {"kind":"ClaudeCode","modelAlias":"fable","source":"Detected","disabledUntil":"2026-09-04T00:00:00Z","reason":null}],
             "available":["opus"]}
            """));
        var text = Run(http, null);
        text.Exit.ShouldBe(0, text.All);
        text.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            "holds:",
            "  Grok  *  Manual  until cleared  quota",
            "  ClaudeCode  fable  Detected  until 2026-09-04T00:00:00Z  ",
            "available: opus",
        ]);

        var json = Run(http, null, "--json");
        json.Exit.ShouldBe(0, json.All);
        using var printed = JsonDocument.Parse(json.Output);
        printed.RootElement.GetProperty("holds").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public void A_problem_answer_prints_its_detail_and_code()
    {
        var http = new RecordingOperatorHttp(_ => new(409, """{"detail":"Hold already active.","code":"model_hold_exists"}"""));
        var run = Run(http, null, "hold", "--kind", "Codex", "--model", "gpt-6.2-sol");
        run.Exit.ShouldBe(1);
        run.Error.ShouldBe("Antiphon PUT /api/model-availability/Codex/gpt-6.2-sol failed: Hold already active." + Environment.NewLine + "code model_hold_exists"
            + Environment.NewLine);
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => ModelAvailabilityCliOptions.Parse(["hold", "--kind", "ClaudeCode"], null, null)).Message
            .ShouldBe("hold requires --kind and --model");
        Should.Throw<NightlyUsageException>(() => ModelAvailabilityCliOptions.Parse(["clear", "--kind", "Gemini", "--model", "x"], null, null)).Message
            .ShouldBe("--kind must be one of ClaudeCode, Grok, Codex, not 'Gemini'");
        Should.Throw<NightlyUsageException>(() => ModelAvailabilityCliOptions.Parse(["clear", "--kind", "Grok", "--model", "x", "--until", "2026-09-04T00:00:00Z"], null, null)).Message
            .ShouldBe("--until does not apply to clear");
        Should.Throw<NightlyUsageException>(() => ModelAvailabilityCliOptions.Parse(["--model", "x"], null, null)).Message.ShouldBe("--model does not apply to get");
        Should.Throw<NightlyUsageException>(() => ModelAvailabilityCliOptions.Parse(["pause"], null, null));
        ModelAvailabilityCliOptions.Parse([], null, null).Verb.ShouldBe("get");
    }

    private static RecordingOperatorHttp Stub() => new(call => call.Method == "DELETE" ? new(204, "") : new(200, Answer));

    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, string? taskToken, params string[] args) =>
        BoardCommandRun.Run(host => ModelAvailabilityCli.Run(ModelAvailabilityCliOptions.Parse(args, BoardCommandRun.Api, taskToken), host, http.Transport));
}
