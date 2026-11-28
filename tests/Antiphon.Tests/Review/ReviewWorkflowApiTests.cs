using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Review;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The review engine API over HTTP against the real host and Postgres: demo seed, list, findings, one
/// submitted review, and a gate's AnotherRound decision; plus graph validation.
/// </summary>
[Category("Integration")]
[NotInParallel("ReviewEngine")]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ReviewWorkflowApiTests(AntiphonWebAppFactory factory)
{
    [Before(Test)]
    public Task ResetAsync() => factory.ResetAsync();

    [Test]
    public async Task A_submitted_review_and_another_round_decision_queue_the_next_round()
    {
        using var client = factory.CreateClient();

        using var seeded = await client.PostAsync("/api/review-runs/demo", null);
        seeded.StatusCode.ShouldBe(HttpStatusCode.OK, await seeded.Content.ReadAsStringAsync());
        var seededRuns = (await seeded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();
        seededRuns.Count.ShouldBe(10);
        var seededIds = seededRuns.Select(r => r.GetProperty("id").GetString()).ToHashSet();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/review-runs?status=AtGate");
        var atGate = list.GetProperty("items").EnumerateArray()
            .Where(r => seededIds.Contains(r.GetProperty("id").GetString()))
            .Single(r => r.GetProperty("subject").GetProperty("repo").GetString() == "acme/billing");
        atGate.GetProperty("round").GetInt32().ShouldBe(3);
        atGate.GetProperty("openBlockers").GetInt32().ShouldBe(1);
        var runId = atGate.GetProperty("id").GetString()!;

        var findings = (await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{runId}/findings"))
            .GetProperty("items").EnumerateArray().ToList();
        var blocker = findings.Single(f => f.GetProperty("severity").GetString() == "Blocker");
        var minor = findings.Single(f => f.GetProperty("severity").GetString() == "Minor");
        minor.GetProperty("status").GetString().ShouldBe("Open");

        using var submitted = await client.PostAsJsonAsync($"/api/review-runs/{runId}/responses", new
        {
            responses = new object[]
            {
                new { findingId = minor.GetProperty("id").GetString(), action = "Dismiss", reasonCategory = "FalsePositive", text = "The client retries with backoff." },
                new { findingId = blocker.GetProperty("id").GetString(), action = "WillFix" },
            },
        });
        submitted.StatusCode.ShouldBe(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
        var responded = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .ToDictionary(f => f.GetProperty("key").GetString()!);
        responded["F4"].GetProperty("status").GetString().ShouldBe("Dismissed");
        var dismissal = responded["F4"].GetProperty("events").EnumerateArray().Last();
        dismissal.GetProperty("action").GetString().ShouldBe("Dismiss");
        dismissal.GetProperty("reasonCategory").GetString().ShouldBe("FalsePositive");
        dismissal.GetProperty("scope").GetString().ShouldBe("Instance");
        dismissal.GetProperty("syncState").GetString().ShouldBe("Pending");
        responded["F1"].GetProperty("status").GetString().ShouldBe("WillFix");
        responded["F1"].GetProperty("syncState").GetString().ShouldBe("Pending");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{runId}");
        var gate = detail.GetProperty("gates").EnumerateArray().Single(g => g.GetProperty("status").GetString() != "Done");
        gate.GetProperty("kind").GetString().ShouldBe("ReviewGate");
        gate.GetProperty("viewerCanDecide").GetBoolean().ShouldBeTrue();
        gate.GetProperty("allowedDecisions").EnumerateArray().Select(d => d.GetString()).ShouldBe(["Approve", "AnotherRound", "Close"]);
        gate.GetProperty("passes").EnumerateArray().ShouldContain(p => p.GetProperty("login").GetString() == "tomas-v");
        var gateId = gate.GetProperty("id").GetString();
        var revisionId = detail.GetProperty("workflowRevisionId").GetString();
        var revision = await client.GetFromJsonAsync<JsonElement>($"/api/review-workflows/revisions/{revisionId}");
        revision.GetProperty("revisionId").GetString().ShouldBe(revisionId);
        revision.GetProperty("name").GetString().ShouldBe("pr-review");

        const string steer = "Re-check the temp path on the Windows agents too.";
        using var decided = await client.PostAsJsonAsync($"/api/review-gates/{gateId}/decision", new { decision = "AnotherRound", steerPrompt = steer });
        decided.StatusCode.ShouldBe(HttpStatusCode.OK, await decided.Content.ReadAsStringAsync());
        var after = await decided.Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("round").GetInt32().ShouldBe(4);
        after.GetProperty("status").GetString().ShouldBe("Queued");
        after.GetProperty("stage").GetString().ShouldBe("Intake");
        var decidedGate = after.GetProperty("gates").EnumerateArray().Single(g => g.GetProperty("id").GetString() == gateId);
        decidedGate.GetProperty("decision").GetString().ShouldBe("AnotherRound");
        decidedGate.GetProperty("steerPrompt").GetString().ShouldBe(steer);
        var viewer = await client.GetFromJsonAsync<JsonElement>("/api/review-me");
        decidedGate.GetProperty("decidedBy").GetString().ShouldBe(viewer.GetProperty("login").GetString());
        after.GetProperty("iterations").EnumerateArray()
            .ShouldContain(i => i.GetProperty("iteration").GetInt32() == 4 && i.GetProperty("trigger").GetString() == "another-round");
        after.GetProperty("nodes").EnumerateArray()
            .ShouldContain(n => n.GetProperty("nodeId").GetString() == "intake" && n.GetProperty("iteration").GetInt32() == 4
                && n.GetProperty("status").GetString() == "Pending");

        var reread = (await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{runId}/findings")).GetProperty("items").EnumerateArray()
            .ToDictionary(f => f.GetProperty("key").GetString()!, f => f.GetProperty("status").GetString());
        reread["F1"].ShouldBe("WillFix");
        reread["F4"].ShouldBe("Dismissed");

        using var again = await client.PostAsJsonAsync($"/api/review-gates/{gateId}/decision", new { decision = "Approve" });
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var queue = await client.GetFromJsonAsync<JsonElement>("/api/review-queue?scope=All");
        queue.GetProperty("items").EnumerateArray().ShouldNotContain(i => i.GetProperty("gate").GetProperty("id").GetString() == gateId);
    }

    [Test]
    public async Task C2110_S1_pg_validate_attaches_publish_field()
    {
        using var client = factory.CreateClient();
        var graph = await client.GetFromJsonAsync<WorkflowGraphDto>("/api/review-workflows/default", ReviewWorkflowService.GraphJsonOptions);
        graph.ShouldNotBeNull();
        var publish = graph.Nodes.Single(n => n.Id == "publish");
        var parameters = new Dictionary<string, object?>
        {
            ["inputs"] = new[] { "collate", "verify", "categorise" },
            ["summaryTemplate"] = "{{defaultBody}}\n\n{{categorisation}}",
        };
        var valid = await ValidateAsync(parameters);
        parameters["inputs"] = "verify";
        var invalid = await ValidateAsync(parameters);

        valid.GetProperty("valid").GetBoolean().ShouldBeTrue(valid.ToString());
        invalid.GetProperty("valid").GetBoolean().ShouldBeFalse();
        var diagnostic = invalid.GetProperty("diagnostics").EnumerateArray()
            .Single(d => d.GetProperty("code").GetString() == "invalid-input");
        diagnostic.GetProperty("severity").GetString().ShouldBe("Error");
        diagnostic.GetProperty("nodeIds").EnumerateArray().Select(id => id.GetString()).ShouldBe(["publish"]);
        diagnostic.GetProperty("field").GetString().ShouldBe("inputs");

        async Task<JsonElement> ValidateAsync(IReadOnlyDictionary<string, object?> values)
        {
            var edited = graph with { Nodes = graph.Nodes.Select(n => n.Id == publish.Id ? n with { Params = values } : n).ToList() };
            using var response = await client.PostAsJsonAsync("/api/review-workflows/validate", edited,
                ReviewWorkflowService.GraphJsonOptions);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }

    [Test]
    public async Task C2110_S1_pg_validate_rejects_hidden_publish_coverage()
    {
        using var client = factory.CreateClient();
        var graph = (await client.GetFromJsonAsync<WorkflowGraphDto>("/api/review-workflows/default", ReviewWorkflowService.GraphJsonOptions))
            .ShouldNotBeNull();
        var cases = new (string Label, string Template)[]
        {
            ("fence", "```md\n{{findings}}\n```"),
            ("HTML comment", "<!--\n{{defaultBody}}\n-->"),
            ("inline code", "Literal `text\n{{findings}}\ncontinues`"),
            ("HTML block", "<div>\n{{findings}}\n</div>"),
            ("link destination", "[link](\n{{findings}}\n)"),
            ("indented code", "    {{defaultBody}}"),
        };
        var results = new List<(string Label, JsonElement Result)>();
        foreach (var (label, template) in cases)
        {
            var edited = graph with
            {
                Nodes = graph.Nodes.Select(n => n.Id == "publish"
                    ? n with { Params = new Dictionary<string, object?> { ["summaryTemplate"] = template } } : n).ToList(),
            };
            using var response = await client.PostAsJsonAsync("/api/review-workflows/validate", edited, ReviewWorkflowService.GraphJsonOptions);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            results.Add((label, await response.Content.ReadFromJsonAsync<JsonElement>()));
        }

        foreach (var (label, result) in results)
        {
            result.GetProperty("valid").GetBoolean().ShouldBeFalse(label);
            var diagnostic = result.GetProperty("diagnostics").EnumerateArray()
                .Single(d => d.GetProperty("code").GetString() == "invalid-summary-template");
            diagnostic.GetProperty("nodeIds").EnumerateArray().Select(id => id.GetString()).ShouldBe(["publish"]);
            diagnostic.GetProperty("field").GetString().ShouldBe("summaryTemplate");
            diagnostic.GetProperty("message").GetString()!.ShouldContain("visible");
        }
    }

    [Test]
    public async Task The_default_graph_validates_and_a_loop_without_a_gate_does_not()
    {
        using var client = factory.CreateClient();

        var graph = await client.GetFromJsonAsync<JsonElement>("/api/review-workflows/default");
        graph.GetProperty("name").GetString().ShouldBe("pr-review");
        graph.GetProperty("hash").GetString()!.Length.ShouldBe(64);
        using var valid = await client.PostAsJsonAsync("/api/review-workflows/validate", graph);
        var validResult = await valid.Content.ReadFromJsonAsync<JsonElement>();
        validResult.GetProperty("valid").GetBoolean().ShouldBeTrue(validResult.ToString());

        using var invalid = await client.PostAsJsonAsync("/api/review-workflows/validate", new
        {
            name = "broken",
            revisionId = (string?)null,
            hash = (string?)null,
            sections = Array.Empty<object>(),
            nodes = new object[]
            {
                new { id = "packs", kind = "Code", run = "BuildPacks", description = "d", @params = new { } },
                new { id = "verify", kind = "Completion", run = "VerifyFindings", description = "d", @params = new { outputs = new[] { "disputed" } } },
            },
            edges = new object[]
            {
                new { from = "packs", to = "verify", label = (string?)null, maxIterations = (int?)null, isBackEdge = false },
                new { from = "verify", to = "packs", label = "disputed", maxIterations = (int?)null, isBackEdge = true },
                new { from = "verify", to = "ghost", label = "disputed", maxIterations = (int?)null, isBackEdge = false },
            },
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.OK, await invalid.Content.ReadAsStringAsync());
        var result = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("valid").GetBoolean().ShouldBeFalse();
        var codes = result.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()).ToList();
        codes.ShouldContain("unbounded-cycle");
        codes.ShouldContain("unknown-node");
    }
}
