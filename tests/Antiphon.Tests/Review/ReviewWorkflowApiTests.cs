using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The review engine API over HTTP against the real host and Postgres: demo seed, list, findings, one
/// submitted review, and a gate's AnotherRound decision; plus graph validation.
/// </summary>
[Category("Integration")]
[NotInParallel("ReviewEngine")]
[ClassDataSource<ReviewWorkflowApiTests.Factory>(Shared = SharedType.PerClass)]
public class ReviewWorkflowApiTests(ReviewWorkflowApiTests.Factory factory)
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
    [Arguments("AnotherRound", "Queued", "Intake", "another-round")]
    [Arguments("Approve", "Queued", "Approval", "approve")]
    [Arguments("Close", "Closed", "Done", "close")]
    public async Task S1_gate_validation_matches_durable_decision_transitions(
        string decision, string status, string stage, string outcome)
    {
        using var client = factory.CreateClient();
        var definition = ReviewWorkflowYaml.LoadDefault();
        var graphWithoutAnotherRound = definition with
        {
            Edges = definition.Edges.Where(edge => edge.From != "human-review" || edge.Label != "another-round").ToArray(),
        };
        graphWithoutAnotherRound.Edges.Count.ShouldBe(definition.Edges.Count - 1);

        // Walked gates need a route for approval. The other decisions take effect in the decision API.
        var variant = decision == "AnotherRound" ? graphWithoutAnotherRound : definition;
        var candidate = decision == "Approve" ? definition with
        {
            Edges = definition.Edges.Where(edge => edge.From != "human-review" || edge.Label != "approve").ToArray(),
        } : variant;
        using var validated = await client.PostAsJsonAsync(ValidatePath, WorkflowJson(candidate));
        validated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await validated.Content.ReadFromJsonAsync<WorkflowValidationDto>())!;
        if (decision == "Approve")
            validation.Diagnostics.ShouldContain(d => d.Code == "missing-successor" && d.NodeIds.Contains("human-review"));
        else
            validation.Valid.ShouldBeTrue(string.Join(", ", validation.Diagnostics.Select(d => d.Message)));

        var run = await SeedAsync("test/gate-transitions", variant);
        var gateId = await SeedWaitingGateAsync(run.Id);
        using var rejected = await client.PostAsJsonAsync($"/api/review-gates/{gateId}/decision", new { decision = "Reject" });
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var decided = await client.PostAsJsonAsync($"/api/review-gates/{gateId}/decision", new { decision });
        decided.StatusCode.ShouldBe(HttpStatusCode.OK, await decided.Content.ReadAsStringAsync());

        var updated = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{run.Id}");
        updated.GetProperty("workflowRevisionId").GetGuid().ShouldBe(run.WorkflowRevisionId!.Value);
        updated.GetProperty("status").GetString().ShouldBe(status);
        updated.GetProperty("stage").GetString().ShouldBe(stage);
        updated.GetProperty("gates").EnumerateArray().Single().GetProperty("decision").GetString().ShouldBe(decision);
        var nodes = updated.GetProperty("nodes").EnumerateArray().ToList();
        nodes.Single(node => node.GetProperty("nodeId").GetString() == "human-review").GetProperty("outcome").GetString().ShouldBe(outcome);
        if (decision == "AnotherRound")
        {
            updated.GetProperty("round").GetInt32().ShouldBe(run.Round + 1);
            updated.GetProperty("iterations").EnumerateArray()
                .ShouldContain(i => i.GetProperty("iteration").GetInt32() == 2 && i.GetProperty("trigger").GetString() == outcome);
            nodes.ShouldContain(node => node.GetProperty("nodeId").GetString() == "intake"
                && node.GetProperty("iteration").GetInt32() == 2 && node.GetProperty("status").GetString() == "Pending");
        }
        else
        {
            updated.GetProperty("iterations").GetArrayLength().ShouldBe(1);
        }
    }

    [Test]
    public async Task S1_gate_admission_uses_the_pinned_revision()
    {
        using var client = factory.CreateClient();
        var embedded = ReviewWorkflowYaml.LoadDefault();
        var variant = embedded with
        {
            Nodes = embedded.Nodes.Select(node => node.Id == "human-review"
                ? node with { Params = new Dictionary<string, object?>(node.Params) { ["decisions"] = new object[] { "close" } } }
                : node).ToArray(),
            Edges = embedded.Edges.Where(edge => edge.From != "human-review").ToArray(),
        };
        using var response = await client.PostAsJsonAsync(ValidatePath, WorkflowJson(variant));
        var validation = (await response.Content.ReadFromJsonAsync<WorkflowValidationDto>())!;
        validation.Valid.ShouldBeTrue(string.Join(", ", validation.Diagnostics.Select(d => d.Message)));

        var run = await SeedAsync("test/pinned-gate", variant);
        var gateId = await SeedWaitingGateAsync(run.Id);
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{run.Id}");
        before.GetProperty("gates").EnumerateArray().Single().GetProperty("allowedDecisions").EnumerateArray()
            .Select(value => value.GetString()).ShouldBe(["Close"]);

        using var refused = await client.PostAsJsonAsync($"/api/review-gates/{gateId}/decision", new { decision = "Approve" });
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "review_gate_decision_not_allowed");
        using var closed = await client.PostAsJsonAsync($"/api/review-gates/{gateId}/decision", new { decision = "Close" });
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> SeedWaitingGateAsync(Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var iteration = await db.ReviewIterations.SingleAsync(i => i.RunId == runId);
        iteration.HeadSha = new string('a', 40);
        var gate = new ReviewGate
        {
            Id = Guid.NewGuid(), RunId = runId, NodeId = "human-review", Kind = ReviewGateKind.ReviewGate,
            Iteration = 1, Status = ReviewQueueItemStatus.Unassigned, OpenedAt = SeedTime,
        };
        db.ReviewGates.Add(gate);
        db.ReviewNodeExecutions.Add(new ReviewNodeExecution
        {
            Id = Guid.NewGuid(), RunId = runId, NodeId = "human-review", Kind = ReviewNodeKind.Gate,
            Iteration = 1, Sequence = 2, Status = ReviewNodeStatus.Waiting, StartedAt = SeedTime,
        });
        await db.SaveChangesAsync();
        return gate.Id;
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

    [Test]
    [Arguments("nodes", 257)]
    [Arguments("edges", 1025)]
    public async Task S1_validation_refuses_oversized_graphs(string collection, int count)
    {
        using var client = factory.CreateClient();
        var graph = WorkflowJson(ReviewWorkflowYaml.LoadDefault());
        var element = graph[collection]!.AsArray()[0]!;
        graph[collection] = new JsonArray(Enumerable.Range(0, count).Select(_ => element.DeepClone()).ToArray());

        using var response = await client.PostAsJsonAsync(ValidatePath, graph);
        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "workflow_graph_limit");
    }

    [Test]
    public async Task S1_validation_refuses_deep_parameters()
    {
        using var client = factory.CreateClient();
        var graph = WorkflowJson(ReviewWorkflowYaml.LoadDefault());
        JsonNode nested = JsonValue.Create("leaf")!;
        for (var depth = 0; depth < 25; depth++)
            nested = new JsonArray(nested);
        graph["nodes"]!.AsArray()[0]!["params"]!["extra"] = nested;

        using var response = await client.PostAsJsonAsync(ValidatePath, graph);
        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "workflow_parameter_limit");
    }

    [Test]
    public async Task S1_validation_refuses_duplicate_nested_parameter_keys()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("""
            {"nodes":[{"id":"intake","kind":"Code","run":"Intake","params":{"extra":{"duplicate":1,"duplicate":2}}}]}
            """, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(ValidatePath, content);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "bad_request");
    }

    [Test]
    public async Task S1_validation_caps_diagnostic_fan_out()
    {
        using var client = factory.CreateClient();
        var graph = WorkflowJson(ReviewWorkflowYaml.LoadDefault());
        var edges = graph["edges"]!.AsArray();
        for (var index = 0; index < 128; index++)
            edges.Add(new JsonObject { ["from"] = $"missing-from-{index}", ["to"] = $"missing-to-{index}" });

        using var response = await client.PostAsJsonAsync(ValidatePath, graph);
        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "workflow_diagnostic_limit");
    }

    [Test]
    [Arguments("empty", "missing-entry", null)]
    [Arguments("group", "missing-successor", "group")]
    [Arguments("failure", "missing-failure-route", "delta")]
    [Arguments("implicit", "unavailable-input", "packs")]
    public async Task S1_validation_requires_executable_routes(string change, string expectedCode, string? nodeId)
    {
        using var client = factory.CreateClient();
        var graph = WorkflowJson(ReviewWorkflowYaml.LoadDefault());
        var edges = graph["edges"]!.AsArray();
        JsonNode From(string id) => edges.Single(edge => edge!["from"]!.GetValue<string>() == id)!;
        switch (change)
        {
            case "empty":
                graph = new JsonObject { ["nodes"] = new JsonArray(), ["edges"] = new JsonArray() };
                break;
            case "group":
                edges.Remove(From("group"));
                break;
            case "failure":
                From("delta")["label"] = "incremental|full";
                graph["nodes"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "delta")!
                    ["params"]!["onFailure"] = "continue";
                break;
            case "implicit":
                From("select")["to"] = "packs";
                break;
        }

        using var response = await client.PostAsJsonAsync(ValidatePath, graph);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var validation = (await response.Content.ReadFromJsonAsync<WorkflowValidationDto>())!;
        validation.Valid.ShouldBeFalse();
        validation.Diagnostics.ShouldContain(d => d.Code == expectedCode
            && (nodeId == null || d.NodeIds.Contains(nodeId)));
    }

    [Test]
    public async Task S1_validation_serializes_the_exact_parallel_edge_index()
    {
        using var client = factory.CreateClient();
        var graph = WorkflowJson(ReviewWorkflowYaml.LoadDefault());
        var edges = graph["edges"]!.AsArray();
        var original = edges.Single(edge => edge!["from"]!.GetValue<string>() == "route"
            && edge["label"]?.GetValue<string>() == "findings")!;
        var duplicateIndex = edges.Count;
        edges.Add(original.DeepClone());

        using var response = await client.PostAsJsonAsync(ValidatePath, graph);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostic = result.GetProperty("diagnostics").EnumerateArray()
            .Single(row => row.GetProperty("code").GetString() == "ambiguous-route");
        var edge = diagnostic.GetProperty("edge");
        edge.GetProperty("from").GetString().ShouldBe("route");
        edge.GetProperty("to").GetString().ShouldBe("await-fixes");
        edge.GetProperty("index").GetInt32().ShouldBe(duplicateIndex);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(code);
    }

    [Test]
    public async Task C2109_S1_Unknown_text_kind_is_a_node_diagnostic()
    {
        using var client = factory.CreateClient();
        await AssertInvalidKindAsync(client, JsonValue.Create("UnregisteredKind")!);

        using var malformed = new StringContent("{\"nodes\": [", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(ValidatePath, malformed);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task C2109_S1_Undefined_numeric_kind_is_a_node_diagnostic()
    {
        using var client = factory.CreateClient();
        await AssertInvalidKindAsync(client, JsonValue.Create(int.MaxValue)!);
    }

    [Test]
    public async Task C2109_S1_Validation_and_policy_read_leave_no_write()
    {
        using var client = factory.CreateClient();
        var definition = ReviewWorkflowYaml.LoadDefault();
        var run = await SeedAsync("test/policy-pure", definition);
        var before = await SnapshotAsync(run.Id);
        var externalCalls = factory.ExternalCalls;
        externalCalls.ShouldBe(0);
        var draft = WorkflowJson(definition);
        draft["nodes"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "intake")!["description"] = null;

        using var validated = await client.PostAsJsonAsync(ValidatePath, draft);
        validated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await validated.Content.ReadFromJsonAsync<WorkflowValidationDto>())!;
        validation.Valid.ShouldBeTrue();
        var warning = validation.Diagnostics.ShouldHaveSingleItem();
        warning.Severity.ShouldBe("Warning");
        warning.Code.ShouldBe("missing-description");
        warning.NodeIds.ShouldBe(["intake"]);

        var policy = await ReadPolicyAsync(client, run.Id);
        policy.RevisionId.ShouldBe(run.WorkflowRevisionId);
        policy.Hash.ShouldBe(definition.Hash());
        (await SnapshotAsync(run.Id)).ShouldBe(before);
        factory.ExternalCalls.ShouldBe(externalCalls);
    }

    [Test]
    public async Task C2109_S1_Failure_override_reports_effective_value_and_origin()
    {
        using var client = factory.CreateClient();
        var run = await SeedAsync("test/policy-failure", ReviewWorkflowYaml.LoadDefault());
        var nodes = (await ReadPolicyAsync(client, run.Id)).Nodes.ToDictionary(node => node.NodeId);
        nodes["categorise"].OnFailure.ShouldBe(new WorkflowPolicyValue<string>("stop", WorkflowPolicySource.InstanceOverride));
        nodes["verify"].OnFailure.ShouldBe(new WorkflowPolicyValue<string>("continue", WorkflowPolicySource.WorkflowRevision));
        nodes["intake"].OnFailure.ShouldBe(new WorkflowPolicyValue<string>("stop", WorkflowPolicySource.StepDefault));
    }

    [Test]
    public async Task S1_fan_out_policy_matches_failed_continued_execution()
    {
        using var client = factory.CreateClient();
        var embedded = ReviewWorkflowYaml.LoadDefault();
        var run = await SeedAsync("test/policy-fan-out", embedded with
        {
            Edges = embedded.Edges.Select(edge => edge.From == "review" ? edge with { To = "await-fixes" } : edge).ToArray(),
        });
        await QueueFanOutAsync(run.Id);

        var executor = factory.Services.GetServices<IHostedService>().OfType<ReviewWorkflowExecutor>().Single();
        (await executor.RunPendingAsync(CancellationToken.None)).ShouldBe(1);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{run.Id}");
        detail.GetProperty("status").GetString().ShouldBe("AwaitingFixes");
        var review = detail.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("nodeId").GetString() == "review");
        review.GetProperty("status").GetString().ShouldBe("FailedContinued");
        review.GetProperty("summary").GetString()!.ShouldContain("No context pack for group fixture");
        var policy = (await ReadPolicyAsync(client, run.Id)).Nodes.Single(node => node.NodeId == "review");
        policy.OnFailure.ShouldBe(new WorkflowPolicyValue<string>("continue", WorkflowPolicySource.InstanceOverride));
    }

    private async Task QueueFanOutAsync(Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.ReviewRuns.Include(row => row.Iterations).SingleAsync(row => row.Id == runId);
        run.Status = ReviewRunStatus.Queued;
        run.Iterations.Single().HeadSha = new string('b', 40);
        db.ReviewNodeExecutions.AddRange(
            new ReviewNodeExecution
            {
                Id = Guid.NewGuid(), RunId = runId, NodeId = "group", Kind = ReviewNodeKind.Code, Iteration = 1, Sequence = 2,
                Status = ReviewNodeStatus.Succeeded, StartedAt = SeedTime, CompletedAt = SeedTime,
                OutputJson = ReviewWalk.ToJson(new GroupOutput([new ReviewGroup("fixture", [], false, "Low", 1)])),
            },
            new ReviewNodeExecution
            {
                Id = Guid.NewGuid(), RunId = runId, NodeId = "review", Kind = ReviewNodeKind.FanOut, Iteration = 1, Sequence = 3,
                Status = ReviewNodeStatus.Pending,
            });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task C2109_S1_Gate_policy_uses_target_repo_and_optionality()
    {
        using var client = factory.CreateClient();
        var definition = ReviewWorkflowYaml.LoadDefault();
        var matching = await SeedAsync("Test/Policy-Gate-A", definition);
        var other = await SeedAsync("test/policy-gate-b", definition);
        matching.WorkflowRevisionId.ShouldBe(other.WorkflowRevisionId);
        foreach (var (run, off) in new[] { (matching, true), (other, false) })
        {
            var nodes = (await ReadPolicyAsync(client, run.Id)).Nodes.ToDictionary(node => node.NodeId);
            nodes["merge-gate"].GatesOff.ShouldBe(new WorkflowPolicyValue<bool>(off,
                off ? WorkflowPolicySource.RepoRule : WorkflowPolicySource.StepDefault));
            nodes["human-approve"].GatesOff.ShouldBe(new WorkflowPolicyValue<bool>(false, WorkflowPolicySource.StepDefault));
        }
    }

    [Test]
    public async Task C2109_S1_Chain_policy_uses_requested_revision_and_runtime_default()
    {
        using var client = factory.CreateClient();
        var embedded = ReviewWorkflowYaml.LoadDefault();
        var implicitChains = embedded with
        {
            Nodes = embedded.Nodes.Select(node => node.Id is "categorise" or "verify"
                ? node with { Params = node.Params.Where(param => param.Key != "chain").ToDictionary() }
                : node).ToArray(),
        };
        var explicitChain = implicitChains with
        {
            Name = "policy-variant",
            Nodes = implicitChains.Nodes.Select(node => node.Id == "categorise"
                ? node with { Params = new Dictionary<string, object?>(node.Params) { ["chain"] = "s1-decision" } }
                : node).ToArray(),
        };
        var run = await SeedAsync("test/policy-chain", implicitChains);
        var alternate = await SeedAsync("test/policy-variant", explicitChain);
        var pinned = await ReadPolicyAsync(client, run.Id);
        pinned.RevisionId.ShouldBe(run.WorkflowRevisionId);
        pinned.Nodes.Single(node => node.NodeId == "categorise").Chain
            .ShouldBe(new WorkflowPolicyValue<string>("decisions-default", WorkflowPolicySource.StepDefault));
        pinned.Nodes.Single(node => node.NodeId == "verify").Chain
            .ShouldBe(new WorkflowPolicyValue<string>("completions-cheap", WorkflowPolicySource.StepDefault));

        var selected = await ReadPolicyAsync(client, run.Id, alternate.WorkflowRevisionId);
        selected.RunId.ShouldBe(run.Id);
        selected.RevisionId.ShouldBe(alternate.WorkflowRevisionId);
        selected.Name.ShouldBe(explicitChain.Name);
        selected.Hash.ShouldBe(explicitChain.Hash());
        selected.Nodes.Single(node => node.NodeId == "categorise").Chain
            .ShouldBe(new WorkflowPolicyValue<string>("s1-decision", WorkflowPolicySource.WorkflowRevision));

        using var missing = await client.GetAsync(PolicyPath(run.Id, Guid.NewGuid()));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var missingRun = await client.GetAsync(PolicyPath(Guid.NewGuid()));
        missingRun.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var legacyGraph = await client.GetFromJsonAsync<JsonElement>($"/api/review-workflows/revisions/{run.WorkflowRevisionId}");
        using var legacy = await client.PostAsJsonAsync(ValidatePath, legacyGraph);
        legacy.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await legacy.Content.ReadFromJsonAsync<WorkflowValidationDto>())!.Valid.ShouldBeTrue();
    }

    [Test]
    public async Task C2109_S1_Policy_payload_omits_provider_material()
    {
        using var client = factory.CreateClient();
        var run = await SeedAsync("test/policy-output", ReviewWorkflowYaml.LoadDefault());
        using var response = await client.GetAsync(PolicyPath(run.Id));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain(Factory.ProviderSentinel);
        raw.ShouldNotContain("provider", Case.Insensitive);
        using var json = JsonDocument.Parse(raw);
        json.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["runId", "revisionId", "name", "hash", "nodes"], ignoreOrder: true);
        foreach (var node in json.RootElement.GetProperty("nodes").EnumerateArray())
        {
            node.EnumerateObject().Select(property => property.Name)
                .ShouldBe(["nodeId", "onFailure", "gatesOff", "chain"], ignoreOrder: true);
            foreach (var value in node.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Object))
                value.Value.EnumerateObject().Select(property => property.Name).ShouldBe(["value", "source"], ignoreOrder: true);
        }
    }

    private const string ValidatePath = "/api/review-workflows/validate";
    private static readonly DateTime SeedTime = new(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);

    private static string PolicyPath(Guid runId, Guid? revisionId = null) =>
        $"/api/review-runs/{runId}/workflow-policy" + (revisionId is null ? "" : $"?revisionId={revisionId}");

    private static async Task<WorkflowPolicyDto> ReadPolicyAsync(HttpClient client, Guid runId, Guid? revisionId = null) =>
        (await client.GetFromJsonAsync<WorkflowPolicyDto>(PolicyPath(runId, revisionId), ReviewWorkflowService.GraphJsonOptions))!;

    private static JsonObject WorkflowJson(WorkflowDefinition workflow) =>
        JsonSerializer.SerializeToNode(ReviewWorkflowService.ToDto(workflow, null), ReviewWorkflowService.GraphJsonOptions)!.AsObject();

    private static async Task AssertInvalidKindAsync(HttpClient client, JsonNode kind)
    {
        var graph = WorkflowJson(ReviewWorkflowYaml.LoadDefault());
        graph["nodes"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "categorise")!["kind"] = kind;
        using var response = await client.PostAsJsonAsync(ValidatePath, graph);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = (await response.Content.ReadFromJsonAsync<WorkflowValidationDto>())!;
        validation.Valid.ShouldBeFalse();
        var diagnostic = validation.Diagnostics.Single(d => d.Code == "invalid-kind");
        diagnostic.NodeIds.ShouldBe(["categorise"]);
        diagnostic.Severity.ShouldBe("Error");
    }

    private async Task<ReviewRun> SeedAsync(string repo, WorkflowDefinition definition)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = definition.Hash();
        var revision = await db.ReviewWorkflowRevisions.SingleOrDefaultAsync(row => row.Hash == hash);
        if (revision is null)
        {
            revision = new ReviewWorkflowRevision
            {
                Id = Guid.NewGuid(), Name = definition.Name, Hash = hash,
                GraphJson = WorkflowJson(definition).ToJsonString(), CreatedAt = SeedTime,
            };
            db.ReviewWorkflowRevisions.Add(revision);
        }

        var runId = Guid.NewGuid();
        var subject = new ReviewSubject { Id = Guid.NewGuid(), Host = ReviewHost.GitHub, Repo = repo, CreatedAt = SeedTime };
        var run = new ReviewRun
        {
            Id = runId, Subject = subject, SubjectId = subject.Id, Status = ReviewRunStatus.AtGate, Stage = ReviewStage.HumanGate,
            WorkflowRevisionId = revision.Id, WorkflowHash = hash, WorkflowName = definition.Name,
            StartedAt = SeedTime, UpdatedAt = SeedTime,
            Iterations = [new ReviewIteration { Id = Guid.NewGuid(), RunId = runId, Iteration = 1, Round = 1, Trigger = "manual", StartedAt = SeedTime }],
            Nodes = [new ReviewNodeExecution
            {
                Id = Guid.NewGuid(), RunId = runId, NodeId = "intake", Kind = ReviewNodeKind.Code, Iteration = 1, Sequence = 1,
                Status = ReviewNodeStatus.Succeeded, StartedAt = SeedTime, CompletedAt = SeedTime,
            }],
        };
        db.ReviewRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private async Task<string> SnapshotAsync(Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return JsonSerializer.Serialize(new
        {
            Runs = await db.ReviewRuns.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Revisions = await db.ReviewWorkflowRevisions.AsNoTracking().OrderBy(row => row.Id).ToListAsync(),
            Nodes = await db.ReviewNodeExecutions.AsNoTracking().Where(row => row.RunId == runId).OrderBy(row => row.Id).ToListAsync(),
            Iterations = await db.ReviewIterations.AsNoTracking().Where(row => row.RunId == runId).OrderBy(row => row.Id).ToListAsync(),
        });
    }

    public sealed class Factory : AntiphonWebAppFactory
    {
        public const string ProviderSentinel = "c2109-fixture-provider-material";
        private readonly List<ExternalCallSpy> _spies = [];
        public int ExternalCalls => _spies.Sum(spy => spy.Calls);

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.PostConfigure<ReviewExecutorSettings>(settings => settings.Enabled = false);
            services.PostConfigure<ReviewPollingSettings>(settings => settings.Enabled = false);
            services.PostConfigure<ReviewPolicySettings>(settings =>
            {
                settings.OnFailure["categorise"] = " STOP ";
                settings.OnFailure["verify"] = "   ";
                settings.OnFailure["review"] = "continue";
                settings.GatesOff["merge-gate"] = [" test/policy-gate-a "];
                settings.GatesOff["human-approve"] = ["test/policy-gate-a"];
            });
            services.PostConfigure<ReviewDecisionSettings>(settings =>
            {
                settings.Providers["s1-fixture"] = new() { ApiKey = ProviderSentinel };
                settings.Chains["s1-decision"] = [new() { Provider = "s1-fixture" }];
            });
            Refuse<ICodeHost>(services);
            Refuse<IDecisionModel>(services);
            Refuse<ICompletionModel>(services);
            Refuse<IReviewAgentRunner>(services);
        }

        private void Refuse<T>(IServiceCollection services) where T : class
        {
            var proxy = DispatchProxy.Create<T, ExternalCallSpy>();
            _spies.Add((ExternalCallSpy)(object)proxy);
            services.RemoveAll<T>();
            services.AddSingleton(proxy);
        }
    }

    public class ExternalCallSpy : DispatchProxy
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException($"Unexpected external call to {targetMethod?.Name}.");
        }
    }
}
