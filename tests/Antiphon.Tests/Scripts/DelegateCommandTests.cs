using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The delegate command's own command line (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of the
/// 2026-11-22 plan): the verb first, the task (and a message) positional, kebab-case flags per verb, the files that carry long
/// text, and the verbs no script driver covered. In process on a recording transport and a mock file system.
/// </summary>
[Category("Unit")]
public sealed class DelegateCommandTests
{
    private const string TaskId = "11111111-1111-1111-1111-111111111111";

    [Test]
    [Arguments("", "the first argument is the verb")]
    [Arguments("dispatch", "the first argument is the verb (create, status")]
    [Arguments("--role", "the first argument is the verb")]
    public void A_verb_comes_first(string first, string needle)
    {
        var http = Ok("{}");
        var run = DelegateCommandRun.Run(http.Transport, first.Length == 0 ? [] : [first, "Code"]);
        run.Exit.ShouldBe(2, run.All);
        run.Error.ShouldContain(needle);
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Usage_errors_name_the_flag_and_send_nothing()
    {
        foreach (var (args, needle) in new (string[], string)[]
                 {
                     (["status"], "which task? Pass it after the verb: scripts/antiphon delegate status <task>"),
                     (["status", TaskId, "extra"], "unexpected argument 'extra'"),
                     (["create", "stray", "--goal", "x"], "create takes no positional argument, not 'stray'"),
                     (["create", "--goal", "x", "--message", "m"], "--message does not apply to create"),
                     (["create", "--goal", "x", "--Role", "Code"], "unknown flag --Role"),
                     (["create", "--goal", "x", "--role", "Coder"], "--role must be one of Investigate, Plan"),
                     (["create", "--goal", "x", "--expect-about", "0"], "--expect-about must be from 1 to 1440 minutes"),
                     (["create", "--goal", "x", "--source-landing", "abc"], "--source-landing must be a GUID"),
                     (["create", "--goal", "x", "--env-override", "NOEQUALS"], "--env-override takes NAME=value"),
                     (["reroute", TaskId, "--kind", "Grok"], "reroute requires --kind and --level"),
                     (["finding", TaskId, "--clean"], "finding requires --stage"),
                     (["flag", TaskId], "flag requires --verdict"),
                     (["finding", TaskId, "--stage", "Review", "--clean", "--reviewed-source-clean", "yes"], "--reviewed-source-clean must be true or false"),
                     (["reply", TaskId, "a", "--message", "b"], "pass the message after the task or as --message, not both"),
                     (["list-areas", "--verify", "x"], "--verify does not apply to list-areas"),
                 })
        {
            var http = Ok("{}");
            var run = DelegateCommandRun.Run(http.Transport, args);
            run.Exit.ShouldBe(2, string.Join(" ", args) + ": " + run.All);
            run.Error.ShouldContain(needle, Case.Sensitive, string.Join(" ", args));
            http.Calls.ShouldBeEmpty();
        }
    }

    [Test]
    public void Choices_ignore_case_and_are_sent_in_their_listed_spelling()
    {
        var http = Ok(DelegateCommandRun.Created());
        var run = DelegateCommandRun.Run(http.Transport,
            ["create", "--role", "code", "--goal", "x", "--kind", "grok", "--level", "high", "--platform", "linux", "--stage", "verify"]);

        run.Exit.ShouldBe(0, run.All);
        using var body = JsonDocument.Parse(http.Calls.Single().Body!);
        body.RootElement.GetProperty("role").GetString().ShouldBe("Code");
        body.RootElement.GetProperty("agentKind").GetString().ShouldBe("Grok");
        body.RootElement.GetProperty("modelLevel").GetString().ShouldBe("High");
        body.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Linux");
        body.RootElement.GetProperty("stage").GetString().ShouldBe("Verify");
        body.RootElement.GetProperty("kind").GetString().ShouldBe("Worker");
        // An orchestrator with the default role is a Plan.
        var orchestrator = Ok(DelegateCommandRun.Created());
        DelegateCommandRun.Run(orchestrator.Transport, ["create", "--orchestrator", "--goal", "own it"]).Exit.ShouldBe(0);
        using var plan = JsonDocument.Parse(orchestrator.Calls.Single().Body!);
        (plan.RootElement.GetProperty("kind").GetString(), plan.RootElement.GetProperty("role").GetString()).ShouldBe(("Orchestrator", "Plan"));
    }

    [Test]
    public void Long_text_comes_from_files_sent_as_is()
    {
        var goal = "line one with `backticks` and $(subshell) and \"quotes\"\r\nline two & | ^ %PATH%\n";
        var answer = "the answer\nover two lines";
        var files = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(DelegateCommandRun.WorkingDirectory, "goal.md")] = new(goal),
            [Path.Combine(DelegateCommandRun.WorkingDirectory, "answer.md")] = new(answer),
        });
        var http = Ok(DelegateCommandRun.Created());

        var create = DelegateCommandRun.Run(http.Transport, ["create", "--role", "Code", "--goal-file", "goal.md", "--title", "t"], files: files);
        var reply = DelegateCommandRun.Run(http.Transport, ["reply", TaskId, "--message-file", "answer.md"], files: files);

        create.Exit.ShouldBe(0, create.All);
        reply.Exit.ShouldBe(0, reply.All);
        JsonDocument.Parse(http.Calls[0].Body!).RootElement.GetProperty("goal").GetString().ShouldBe(goal);
        var replied = JsonDocument.Parse(http.Calls[1].Body!).RootElement;
        replied.GetProperty("message").GetString().ShouldBe(answer);
        replied.GetProperty("origin").GetString().ShouldBe("Cli");
        reply.Output.Trim().ShouldBe($"Answered task {TaskId}. It will resume and report back.");

        foreach (var (args, needle) in new (string[], string)[]
                 {
                     (["create", "--goal", "x", "--goal-file", "goal.md"], "Pass --goal or --goal-file, not both."),
                     (["create", "--goal-file", "absent.md"], "--goal-file 'absent.md' does not exist."),
                     (["create", "--role", "Code"], "A --goal (or --goal-file) is required. Write it as an outcome, not a procedure."),
                     (["reply", TaskId], "Pass the answer after the task: scripts/antiphon delegate reply <taskId> \"your answer\" (or --message-file <path>)"),
                     (["refine", TaskId, "   "], "Pass the message after the task: scripts/antiphon delegate refine <taskId> \"your message\""),
                 })
        {
            var refused = Ok("{}");
            var run = DelegateCommandRun.Run(refused.Transport, args, files: files);
            run.Exit.ShouldBe(1, run.All);
            run.Error.ShouldContain(needle, Case.Sensitive, string.Join(" ", args));
            refused.Calls.ShouldBeEmpty();
        }
    }

    [Test]
    public void Positionals_may_sit_among_the_flags()
    {
        var http = Ok("""{"status":"Working","interruptWritten":null,"refinementDelivered":null}""");
        var run = DelegateCommandRun.Run(http.Transport, ["refine", "--interrupt", TaskId, "steer now"]);

        run.Exit.ShouldBe(0, run.All);
        http.Calls.Single().Path.ShouldBe($"/api/agent-tasks/{TaskId}/refine");
        JsonDocument.Parse(http.Calls.Single().Body!).RootElement.GetProperty("message").GetString().ShouldBe("steer now");
        // A null status prints nothing (Write-Output $null printed nothing).
        run.Output.Trim().ShouldBe($"Refined task {TaskId}. The message will land between its turns; its report will note it.");
    }

    [Test]
    public void Finding_flag_continue_and_reroute_post_their_bodies()
    {
        var http = Ok("{}");
        DelegateCommandRun.Run(http.Transport, ["flag", TaskId, "--verdict", "noisy", "--note", "too long"]).Output.Trim()
            .ShouldBe($"flagged distillation on task {TaskId} as Noisy");
        DelegateCommandRun.Run(http.Transport, ["continue", TaskId]).Output.Trim()
            .ShouldBe($"Continued task {TaskId} with its standing authority. It will resume and report back.");
        DelegateCommandRun.Run(http.Transport, ["reroute", TaskId, "--kind", "Codex", "--level", "Frontier"]).Output.Trim()
            .ShouldBe($"rerouted task {TaskId} to Codex/Frontier (chain governance ended)");
        var finding = DelegateCommandRun.Run(http.Transport,
            ["finding", TaskId, "--stage", "Review", "--found", "hole in X", "--reviewed-source-sha", new string('a', 40), "--reviewed-source-clean", "False"]);
        finding.Output.Trim().ShouldBe($"recorded Review found on task {TaskId}");

        http.Calls.Select(call => call.Method + " " + call.Path).ShouldBe([
            $"POST /api/agent-tasks/{TaskId}/distillation/feedback",
            $"POST /api/agent-tasks/{TaskId}/continue",
            $"POST /api/agent-tasks/{TaskId}/reroute",
            $"POST /api/agent-tasks/{TaskId}/finding",
        ]);
        var bodies = http.Calls.Select(call => JsonDocument.Parse(call.Body!).RootElement).ToList();
        bodies[0].GetProperty("verdict").GetString().ShouldBe("Noisy");
        bodies[0].GetProperty("note").GetString().ShouldBe("too long");
        bodies[1].GetProperty("origin").GetString().ShouldBe("Cli");
        (bodies[2].GetProperty("agentKind").GetString(), bodies[2].GetProperty("modelLevel").GetString()).ShouldBe(("Codex", "Frontier"));
        bodies[3].GetProperty("found").GetBoolean().ShouldBeTrue();
        bodies[3].GetProperty("detail").GetString().ShouldBe("hole in X");
        bodies[3].GetProperty("reviewedSourceClean").GetBoolean().ShouldBeFalse();

        foreach (var (args, needle) in new (string[], string)[]
                 {
                     (["finding", TaskId, "--stage", "Review", "--clean", "--found", "x"], "Pass --found \"what\" or --clean, not both."),
                     (["finding", TaskId, "--stage", "Review"], "Pass --found \"what you found\" or --clean."),
                 })
        {
            var refused = Ok("{}");
            var run = DelegateCommandRun.Run(refused.Transport, args);
            (run.Exit, run.Error.Trim()).ShouldBe((1, needle));
            refused.Calls.ShouldBeEmpty();
        }
    }

    [Test]
    public void List_areas_and_worktree_health_print_the_server_facts()
    {
        var http = new RecordingOperatorHttp(call => call.Path.StartsWith("/api/agent-tasks/areas", StringComparison.Ordinal)
            ? new NightlyReportHttpResponse(200, """{"repoPath":"C:/repo","sourcePath":"C:/repo/antiphon.areas.json","areas":[{"name":"server","weight":"Allow","paths":["server/**"]},{"name":"docs","paths":["docs/**","*.md"]}]}""")
            : new NightlyReportHttpResponse(200, """{"findingCount":1,"findings":[{"branch":"feat/card-task-aabbccdd","shortId":"aabbccdd","detail":"task Failed but branch remains"}]}"""));

        var areas = DelegateCommandRun.Run(http.Transport, ["list-areas", "--areas-dir", "C:/repo"]);
        var health = DelegateCommandRun.Run(http.Transport, ["worktree-health"]);

        areas.Output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ShouldBe([
            "Areas in C:/repo/antiphon.areas.json:",
            "  server  [allow]",
            "      server/**",
            "  docs",
            "      docs/**",
            "      *.md",
            "",
            "An area is added when two tasks collide in it, named for the work, not the folder.",
        ]);
        health.Output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ShouldBe([
            "1 stuck delegate worktree finding(s) (detection only; nothing pruned):",
            "  [Error] feat/card-task-aabbccdd  task aabbccdd  task Failed but branch remains",
        ]);
        http.Calls.Select(call => call.Method + " " + call.Uri).ShouldBe([
            "GET " + DelegateCommandRun.Api + "/api/agent-tasks/areas?directory=C%3A%2Frepo",
            "POST " + DelegateCommandRun.Api + "/api/agent-tasks/worktree-health",
        ]);
        http.Calls[1].Body.ShouldBe("{}");

        var none = new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(200, """{"repoPath":"C:/elsewhere","areas":[]}"""));
        DelegateCommandRun.Run(none.Transport, ["list-areas"]).Output.Trim()
            .ShouldBe("No areas declared for C:/elsewhere - every --scope token is read as a path or a label.");
        // Without --areas-dir the checkout is the working directory (git answers "not a repository" here).
        none.Calls.Single().Uri.ShouldBe(DelegateCommandRun.Api + "/api/agent-tasks/areas?directory=" + Uri.EscapeDataString(DelegateCommandRun.WorkingDirectory));
    }

    [Test]
    public void Cleanup_verification_prints_the_answer_as_json()
    {
        var http = Ok("""{"taskId":"11111111-1111-1111-1111-111111111111","removed":true}""");
        var run = DelegateCommandRun.Run(http.Transport, ["cleanup-verification", TaskId]);

        run.Exit.ShouldBe(0, run.All);
        JsonDocument.Parse(run.Output).RootElement.GetProperty("removed").GetBoolean().ShouldBeTrue();
        http.Calls.Single().Path.ShouldBe($"/api/agent-tasks/{TaskId}/cleanup-verification");
    }

    [Test]
    public void A_failed_request_prints_the_servers_answer_whole_and_exits_1()
    {
        const string conflict = """{"code":"concurrency_limit","population":"open","axis":"absolute","canOverride":true,"occupants":[{"shortId":"aabbccdd","stage":"Code"}]}""";
        var http = new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(409, conflict));
        var run = DelegateCommandRun.Run(http.Transport, ["create", "--role", "Code", "--goal", "x"]);

        run.Exit.ShouldBe(1, run.All);
        run.Error.Trim().ShouldBe("Antiphon POST /api/agent-tasks failed: " + conflict);

        var empty = new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(404, ""));
        DelegateCommandRun.Run(empty.Transport, ["status", TaskId]).Error.Trim()
            .ShouldBe($"Antiphon GET /api/agent-tasks/{TaskId} failed: Response status code does not indicate success: 404 (Not Found).");

        var down = DelegateCommandRun.Run((_, _, _, _) => throw new HttpRequestException("No connection could be made (127.0.0.1:17202)"), ["continue", TaskId]);
        (down.Exit, down.Error.Trim()).ShouldBe((1, $"Antiphon POST /api/agent-tasks/{TaskId}/continue failed: No connection could be made (127.0.0.1:17202)"));
    }

    [Test]
    public void Capability_refusals_come_before_any_request_and_never_name_the_token()
    {
        var files = new MockFileSystem();
        var store = Path.Combine(DelegateCommandRun.WorkingDirectory, "caps");
        var environment = DelegateCommandRun.Environment();
        var http = Ok(DelegateCommandRun.Created());

        var missing = DelegateCommandRun.Run(http.Transport, ["status", TaskId, "--capability", "ops"],
            new DelegateCliEnvironment { Api = DelegateCommandRun.Api, CapabilityStore = store, WorkingDirectory = environment.WorkingDirectory }, files);
        (missing.Exit, missing.Error.Trim()).ShouldBe((1,
            "capability 'ops' is not installed under " + store + "; ask the operator to run scripts\\antiphon.cmd capability issue"));

        files.AddFile(Path.Combine(store, "ops.dpapi"), new MockFileData([1, 2, 3]));
        var unreadable = DelegateCommandRun.Run(http.Transport, ["status", TaskId],
            new DelegateCliEnvironment { Api = DelegateCommandRun.Api, Capability = "ops", CapabilityStore = store, WorkingDirectory = environment.WorkingDirectory }, files,
            unprotect: _ => throw new InvalidOperationException("not DPAPI"));
        (unreadable.Exit, unreadable.Error.Trim()).ShouldBe((1, "capability 'ops' could not be read from " + store));

        var opened = DelegateCommandRun.Run(http.Transport, ["create", "--role", "Docs", "--goal", "x", "--capability", "ops"],
            new DelegateCliEnvironment { Api = DelegateCommandRun.Api, CapabilityStore = store, WorkingDirectory = environment.WorkingDirectory }, files,
            unprotect: _ => "capability-SECRET"u8.ToArray());
        opened.Exit.ShouldBe(0, opened.All);
        opened.All.ShouldNotContain("SECRET");
        http.Calls.Single().Headers["X-Antiphon-Task-Token"].ShouldBe("capability-SECRET");
        http.Calls.Single().Headers["X-Antiphon-Caller"].ShouldBe("delegate.ps1");
    }

    [Test]
    public void Status_names_the_command_for_a_ceiling_decision()
    {
        var http = Ok("""
            {"summary":{"id":"11111111-1111-1111-1111-111111111111","status":"Working","title":"t","kind":"Worker","role":"Code","modelLevel":"High"},
             "ceilingHold":{"episodeId":"22222222-2222-2222-2222-222222222222","heldAt":"2026-11-02T11:00:00+01:00","nextCeilingMinutes":240,
              "lastTranscriptKind":null,"lastTranscriptAgeSeconds":360.5,"phaseKind":null,"capReached":false}}
            """);
        var run = DelegateCommandRun.Run(http.Transport, ["status", TaskId]);

        run.Exit.ShouldBe(0, run.All);
        var lines = run.Output.Replace("\r\n", "\n").Split('\n');
        lines.ShouldContain("Ceiling hold: episode 22222222-2222-2222-2222-222222222222; held 2026-11-02T10:00:00Z; bound 240 min; last transcript unknown 360s");
        lines.ShouldContain($"Decide: scripts/antiphon delegate reply {TaskId} --ceiling-action Continue|Extend --episode-id 22222222-2222-2222-2222-222222222222 [--extend-minutes M]");
        lines.ShouldContain("Land: Not requested (legacy receipt evidence, if any, is unverified).");
        lines.ShouldContain("Publication: Unconfirmed; cleanup: NotStarted");
    }

    private static RecordingOperatorHttp Ok(string body) => new(_ => new NightlyReportHttpResponse(200, body));
}
