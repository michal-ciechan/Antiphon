using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The card command's close and move (Antiphon.Nightly.CardCli, which replaced scripts/card.ps1 in unit 27 of the 2026-11-22
/// plan) print the deployment prompt after a terminal move, after the card, tracker and task settlement lines, and make no
/// extra request for it. In process against a stub transport; nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class CardCloseDeploymentPromptScriptTests
{
    private const string CardId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string BoardId = "22222222-2222-2222-2222-222222222222";

    [Test]
    public async Task close_prints_deployment_prompt_after_card_tracker_and_task_settlement()
    {
        var api = new StubApi();
        var run = await RunAsync(api, "close", "CARD-0001", "--reason", "finished");

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
        run.Output.ShouldContain("GitHub      closed #1 (https://example.test/1)");
        api.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task terminal_move_prints_prompt_but_nonterminal_move_does_not()
    {
        var terminalApi = new StubApi();
        var terminal = await RunAsync(terminalApi, "move", "CARD-0001", "--to", "Done");
        terminal.ExitCode.ShouldBe(0, terminal.Output);
        terminal.Output.ShouldContain("deployment not verified", Case.Insensitive);
        terminalApi.PatchCount.ShouldBe(1);

        var activeApi = new StubApi();
        var active = await RunAsync(activeApi, "move", "CARD-0001", "--to", "Working");
        active.ExitCode.ShouldBe(0, active.Output);
        active.Output.ShouldNotContain("deployment not verified", Case.Insensitive);
        activeApi.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task close_without_task_settlement_still_prints_prompt()
    {
        var api = new StubApi(includeSettlement: false);
        var run = await RunAsync(api, "close", "CARD-0001", "--reason", "finished");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("deployment not verified", Case.Insensitive);
        run.Output.ShouldNotContain("still working");
        api.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task failed_patch_exits_nonzero_without_success_or_deployment_prompt()
    {
        var api = new StubApi(failPatch: true);
        var run = await RunAsync(api, "close", "CARD-0001", "--reason", "finished");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldNotContain("moved to");
        run.Output.ShouldNotContain("deployment not verified", Case.Insensitive);
        run.Output.ShouldContain("stale token");
        api.PatchCount.ShouldBe(1);
    }

    [Test]
    public async Task prompt_is_advisory_and_makes_no_extra_api_request()
    {
        var api = new StubApi();
        var run = await RunAsync(api, "close", "CARD-0001", "--reason", "finished");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("canonical checkout", Case.Insensitive);
        api.PatchCount.ShouldBe(1);
        api.Requests.ShouldBe(4); // card, columns, limits, PATCH; no version or runner probe
    }

    private static Task<(int ExitCode, string Output)> RunAsync(StubApi api, params string[] args)
    {
        var run = CardCommandRun.Run(api.Transport, args, taskToken: "");
        return Task.FromResult((run.Exit, run.Output + run.Error));
    }

    private sealed class StubApi(bool includeSettlement = true, bool failPatch = false)
    {
        public int PatchCount { get; private set; }
        public int Requests { get; private set; }

        public NightlyReportHttp Transport => (method, uri, _, _) =>
        {
            Requests++;
            var path = new Uri(uri).AbsolutePath;
            if (method == "GET" && path == "/api/cards/CARD-0001")
                return new(200, $$"""{"id":"{{CardId}}","identifier":"CARD-0001","status":"Working","title":"Example","importance":"Normal","urgency":"Normal","rank":0,"concurrencyToken":"11111111-1111-1111-1111-111111111111","boardId":"{{BoardId}}","labels":[]}""");
            if (method == "GET" && path == $"/api/boards/{BoardId}/columns")
                return new(200, """[{"id":"33333333-3333-3333-3333-333333333333","name":"Working","stateKey":"Working","cardStatus":"Working","isTerminal":false,"columnOrder":0},{"id":"44444444-4444-4444-4444-444444444444","name":"Done","stateKey":"Done","cardStatus":"Done","isTerminal":true,"columnOrder":1}]""");
            if (method == "GET" && path == "/api/cards/limits")
                return new(200, """{"maxReasonLength":4000}""");
            if (method == "PATCH" && path == $"/api/cards/{CardId}")
            {
                PatchCount++;
                if (failPatch) return new(409, """{"detail":"stale token"}""");
                var settlement = includeSettlement ? ""","taskSettlement":{"leftOpen":["task-1"],"canceled":[]}""" : "";
                return new(200, $$"""{"card":{"id":"{{CardId}}","identifier":"CARD-0001","status":"Done","title":"Example","importance":"Normal","urgency":"Normal","rank":0,"labels":[]},"trackerPush":{"trackerKind":"GitHubIssues","outcome":"Closed","externalKey":"#1","url":"https://example.test/1"}{{settlement}}}""");
            }
            return new(404, """{"detail":"not stubbed"}""");
        };
    }
}
