using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0057. The schedule command (Antiphon.Nightly.ScheduleCli, which replaced scripts/schedule.ps1 in unit 24 of the
/// 2026-11-22 plan) in process, on a recording transport: each verb's routes and bodies, the preview printed before a create
/// and a spend refused without --accept-spend, the agent default from ANTIPHON_AGENT_ID, the prompt file read as UTF-8, the
/// problem answer's code, field errors and candidates, and the refusals before any request. Nothing leaves the process; the
/// real-server round trip is the opt-in Antiphon.E2E ScheduleCliE2ETests.
/// </summary>
[Category("Integration")]
public sealed class ScheduleCommandTests
{
    private const string Id = "55555555-5555-5555-5555-555555555555";
    private const string Agent = "66666666-6666-6666-6666-666666666666";

    private const string PreviewJson = """
        {"nextOccurrences":[{"utc":"2026-11-24T09:00:00Z","local":"2026-11-24 09:00 Europe/London"}],
         "target":{"agentName":"triage","agentLive":true,"agentAlwaysOn":false,"sessionStatus":"Idle","cardIdentifier":null},
         "effect":"enqueue WhenIdle","spend":"none","willStartSession":null,"environment":null,"warnings":["agent is not always-on"]}
        """;

    private const string RowJson = $$"""
        {"id":"{{Id}}","name":"Morning triage","repeatDescription":"once","nextFireAt":"2026-11-24T09:00:00Z",
         "nextFireAtLocal":null,"lastOutcome":null,"concurrencyToken":"77777777-7777-7777-7777-777777777777","enabled":true,
         "fires":[{"fireNumber":1,"outcome":"Delivered","detail":"queued"}]}
        """;

    [Test]
    public async Task New_previews_then_creates_a_prompt_schedule_for_the_default_agent()
    {
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "ScheduleCli.cs")).ShouldAllBe(b => b < 128);
        var prompt = "First line with `backticks` and $(Get-Date).\nSecond line, café.";
        var file = Path.Combine(Path.GetTempPath(), "schedule-prompt-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(file, prompt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var http = Api();
            var run = Run(http, Agent, "new", "--name", "Morning triage", "--fire-at", "2026-11-24T09:00:00Z", "--prompt-file", file);
            run.Exit.ShouldBe(0, run.All);
            http.Calls.Select(c => c.Method + " " + c.Path).ShouldBe(["POST /api/schedules/preview", "POST /api/schedules"]);
            http.Calls[0].Body.ShouldBe(http.Calls[1].Body, "the create sends the previewed body");
            using var body = JsonDocument.Parse(http.Calls[1].Body!);
            body.RootElement.GetProperty("name").GetString().ShouldBe("Morning triage");
            body.RootElement.GetProperty("kind").GetString().ShouldBe("Prompt");
            body.RootElement.GetProperty("repeat").GetString().ShouldBe("Once");
            body.RootElement.GetProperty("agent").GetString().ShouldBe(Agent, "ANTIPHON_AGENT_ID is the default agent");
            body.RootElement.GetProperty("promptText").GetString().ShouldBe(prompt, "read as UTF-8, its BOM dropped");
            body.RootElement.GetProperty("fireAt").GetString().ShouldBe("2026-11-24T09:00:00Z");
            body.RootElement.TryGetProperty("createdBy", out _).ShouldBeFalse();
            run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
            [
                "Preview",
                "  next  2026-11-24T09:00:00Z  (2026-11-24 09:00 Europe/London)",
                "  agent triage  live=True  alwaysOn=False  session=Idle",
                "  effect enqueue WhenIdle",
                "  spend  none",
                "  warn   agent is not always-on",
                "created " + Id,
                Id + "  Morning triage  once  next 2026-11-24T09:00:00Z  last ",
            ]);

            var json = Run(Api(), null, "new", "--name", "Morning triage", "--agent", "triage", "--prompt", "hi", "--fire-at", "2026-11-24T09:00:00Z", "--json");
            json.Exit.ShouldBe(0, json.All);
            JsonDocument.Parse(json.Output).RootElement.GetProperty("id").GetString().ShouldBe(Id, "--json prints only the created schedule");
        }
        finally { File.Delete(file); }
    }

    [Test]
    public void A_spending_card_schedule_needs_accept_spend()
    {
        var spend = new RecordingOperatorHttp(call => call.Path == "/api/schedules/preview"
            ? new(200, """{"nextOccurrences":[],"target":{"cardIdentifier":"CARD-0057","cardStatus":"Backlog","cardColumn":"Backlog","cardArchived":false},"effect":"move and spawn","spend":"session","willStartSession":true,"willMove":true,"environment":{"orchestratorPaused":false,"boardActiveCount":3,"boardCap":6,"modelAvailabilityHold":null,"assignedAgent":null,"defaultDefinition":"default"},"warnings":[]}""")
            : new(200, RowJson));
        var refused = Run(spend, null, "new", "--name", "Spawn it", "--card", " CARD-0057 ", "--to", "inprogress", "--start", "spawn", "--repeat", "daily", "--at-local", "07:30",
            "--days-of-week", "62", "--time-zone", "Europe/London");
        refused.Exit.ShouldBe(1);
        refused.Error.Trim().ShouldBe("This schedule would start a session (spend=session). Re-run with --accept-spend after reading the preview.");
        refused.Output.ShouldContain("  card  CARD-0057  status=Backlog  column=Backlog  archived=False");
        refused.Output.ShouldContain("  start  willStartSession=True  willMove=True");
        refused.Output.ShouldContain("  env    orchestratorPaused=False  board=3/6  hold=  assigned=  definition=default");
        spend.Calls.Select(c => c.Path).ShouldBe(["/api/schedules/preview"], "nothing is created");
        using (var preview = JsonDocument.Parse(spend.Calls[0].Body!))
        {
            var body = preview.RootElement;
            body.GetProperty("kind").GetString().ShouldBe("Card");
            body.GetProperty("cardId").GetString().ShouldBe(" CARD-0057 ");
            body.GetProperty("targetStatus").GetString().ShouldBe("InProgress");
            body.GetProperty("start").GetString().ShouldBe("Spawn");
            body.GetProperty("repeat").GetString().ShouldBe("Daily");
            body.GetProperty("atLocal").GetString().ShouldBe("07:30");
            body.GetProperty("daysOfWeek").GetInt32().ShouldBe(62);
            body.GetProperty("timeZoneId").GetString().ShouldBe("Europe/London");
            body.TryGetProperty("agent", out _).ShouldBeFalse();
            body.TryGetProperty("acceptSpend", out _).ShouldBeFalse();
        }

        var accepted = Run(spend, null, "new", "--name", "Spawn it", "--card", "CARD-0057", "--to", "InProgress", "--start", "Spawn", "--accept-spend",
            "--repeat", "Interval", "--every-minutes", "90");
        accepted.Exit.ShouldBe(0, accepted.All);
        spend.Calls.Select(c => c.Path).TakeLast(2).ShouldBe(["/api/schedules/preview", "/api/schedules"]);
        using var created = JsonDocument.Parse(spend.Calls[^1].Body!);
        created.RootElement.GetProperty("acceptSpend").GetBoolean().ShouldBeTrue();
        created.RootElement.GetProperty("everyMinutes").GetInt32().ShouldBe(90);
    }

    [Test]
    public void List_filters_by_an_agent_guid_and_a_card_and_prints_one_line_each()
    {
        var http = new RecordingOperatorHttp(_ => new(200, "{\"schedules\":[" + RowJson + "," + RowJson.Replace("\"nextFireAtLocal\":null", "\"nextFireAtLocal\":\"Tue 09:00\"") + "]}"));
        var run = Run(http, Agent, "list", "--card", " card-51 ");
        run.Exit.ShouldBe(0, run.All);
        http.Calls.Single().Path.ShouldBe("/api/schedules?agentId=" + Agent + "&cardId=card-51");
        run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            Id + "  Morning triage  once  next 2026-11-24T09:00:00Z  last ",
            Id + "  Morning triage  once  next Tue 09:00  last ",
        ]);

        Run(http, null, "list", "--agent", "triage-slug").Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/schedules", "a slug is not a filter the list takes");
        var json = Run(http, null, "list", "--json");
        JsonDocument.Parse(json.Output).RootElement.GetProperty("schedules").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public void Get_enable_disable_remove_and_fire_address_the_schedule_by_id()
    {
        var http = Api();
        var got = Run(http, null, "get", Id);
        got.Exit.ShouldBe(0, got.All);
        got.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
            [Id + "  Morning triage  once  next 2026-11-24T09:00:00Z  last ", "  fire #1  Delivered  queued"]);

        var disabled = RunAs(http, null, "task-secret", "disable", "--id", Id);
        disabled.Exit.ShouldBe(0, disabled.All);
        disabled.All.ShouldNotContain("task-secret");
        Run(http, null, "enable", Id).Exit.ShouldBe(0);
        Run(http, null, "remove", Id).Output.Trim().ShouldBe("removed " + Id);
        Run(http, null, "fire", Id).Output.Trim().ShouldBe("fired " + Id);
        http.Calls.Select(c => c.Method + " " + c.Path).ShouldBe(
        [
            "GET /api/schedules/" + Id,
            "GET /api/schedules/" + Id, "PATCH /api/schedules/" + Id,
            "GET /api/schedules/" + Id, "PATCH /api/schedules/" + Id,
            "DELETE /api/schedules/" + Id,
            "POST /api/schedules/" + Id + "/fire-now",
        ]);
        http.Calls[2].Body.ShouldBe("""{"concurrencyToken":"77777777-7777-7777-7777-777777777777","enabled":false}""");
        http.Calls[2].Headers["X-Antiphon-Task-Token"].ShouldBe("task-secret");
        http.Calls[4].Body.ShouldBe("""{"concurrencyToken":"77777777-7777-7777-7777-777777777777","enabled":true}""");
        http.Calls[5].Body.ShouldBeNull();
        http.Calls[6].Body.ShouldBeNull();
    }

    [Test]
    public void A_problem_answer_names_its_code_field_errors_and_candidates()
    {
        var http = new RecordingOperatorHttp(_ => new(409, """
            {"detail":"'51' matches more than one card.","code":"card_ambiguous","errors":{"CardId":["Pass the guid."]},
             "candidates":[{"boardName":"Antiphon","id":"c1","status":"Backlog","title":"First"},{"boardName":"Other","id":"c2","status":"Done","title":"Second"}]}
            """));
        var run = Run(http, null, "preview", "--card", "51", "--to", "Backlog", "--fire-at", "2026-11-24T09:00:00Z");
        run.Exit.ShouldBe(1);
        run.Error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            "Antiphon POST /api/schedules/preview failed: '51' matches more than one card.",
            "code card_ambiguous",
            "  CardId: Pass the guid.",
            "  Antiphon  c1  Backlog  First",
            "  Other  c2  Done  Second",
        ]);
    }

    [Test]
    [Arguments("prompt-and-file", "Pass --prompt or --prompt-file, not both.")]
    [Arguments("missing-file", "PromptFile 'no-such-prompt.txt' was not found.")]
    [Arguments("once", "Once requires --fire-at (UTC, e.g. 2026-09-04T09:00:00Z).")]
    [Arguments("interval", "Interval requires --every-minutes (1..10080).")]
    [Arguments("daily", "Daily requires --at-local (HH:mm).")]
    [Arguments("card", "A card schedule requires --to (Backlog, InProgress, or Review).")]
    public void A_refused_body_sends_nothing(string shape, string message)
    {
        var http = Api();
        string[] args = shape switch
        {
            "prompt-and-file" => ["preview", "--prompt", "a", "--prompt-file", "b", "--fire-at", "x"],
            "missing-file" => ["preview", "--prompt-file", "no-such-prompt.txt", "--fire-at", "x"],
            "once" => ["preview", "--prompt", "a"],
            "interval" => ["preview", "--prompt", "a", "--repeat", "Interval"],
            "daily" => ["preview", "--prompt", "a", "--repeat", "Daily"],
            _ => ["new", "--name", "n", "--card", "CARD-1", "--fire-at", "x"],
        };
        var run = Run(http, null, args);
        run.Exit.ShouldBe(1);
        run.Error.Trim().ShouldBe(message);
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse([], null, null, null)).Message.ShouldContain("list, get, preview, new");
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse(["get"], null, null, null)).Message.ShouldBe("get requires the schedule id");
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse(["get", Id, "--id", Id], null, null, null)).Message
            .ShouldBe("give the id once, not as an argument and --id");
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse(["new", "--prompt", "p"], null, null, null)).Message.ShouldBe("new requires --name");
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse(["preview", "--repeat", "Weekly"], null, null, null)).Message
            .ShouldBe("--repeat must be one of Once, Interval, Daily, not 'Weekly'");
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse(["preview", "--every-minutes", "ten"], null, null, null)).Message
            .ShouldBe("--every-minutes expects an integer, not 'ten'");
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse(["remove", Id, "--json"], null, null, null)).Message.ShouldBe("--json does not apply to remove");
        Should.Throw<NightlyUsageException>(() => ScheduleCliOptions.Parse(["list", Id], null, null, null)).Message.ShouldBe("list takes no id");
        var options = ScheduleCliOptions.Parse(["preview", "--agent", "mine"], "http://api.example/", "t", Agent);
        (options.Agent, options.Api, options.Repeat, options.Start).ShouldBe(("mine", "http://api.example", "Once", "None"), "--agent outranks ANTIPHON_AGENT_ID");
    }

    // The schedule API as the server answers it: the preview, a schedule row for the create, get and patch, 204 otherwise.
    private static RecordingOperatorHttp Api() => new(call => call.Path switch
    {
        "/api/schedules/preview" => new(200, PreviewJson),
        _ when call.Method == "DELETE" || call.Path.EndsWith("/fire-now", StringComparison.Ordinal) => new(204, ""),
        _ => new(200, RowJson),
    });

    // agentId is what the CLI reads from ANTIPHON_AGENT_ID; no task token.
    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, string? agentId, params string[] args) => RunAs(http, agentId, null, args);

    private static BoardCommandRun.Result RunAs(RecordingOperatorHttp http, string? agentId, string? taskToken, params string[] args) =>
        BoardCommandRun.Run(host => ScheduleCli.Run(ScheduleCliOptions.Parse(args, BoardCommandRun.Api, taskToken, agentId), host,
            new System.IO.Abstractions.FileSystem(), http.Transport));
}
