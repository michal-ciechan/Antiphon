using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// Manual intake through a fake code host, then the executor's walk over the real host and Postgres:
/// intake, delta and categorise (a fake decision model with fixed answers) up to the stop point.
/// </summary>
[Category("Integration")]
[ClassDataSource<ReviewExecutorApiTests.Factory>(Shared = SharedType.PerClass)]
public class ReviewExecutorApiTests(ReviewExecutorApiTests.Factory factory)
{
    private static readonly TimeSpan WalkTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task Manual_intake_runs_through_categorise_and_stops_at_select()
    {
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = "acme/widgets", prNumber = 77 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var run = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = run.GetProperty("id").GetString()!;
        var subject = run.GetProperty("subject");
        subject.GetProperty("title").GetString().ShouldBe(FakeCodeHost.Pr.Title);
        subject.GetProperty("authorLogin").GetString().ShouldBe("dana");
        subject.GetProperty("headSha").GetString().ShouldBe(FakeCodeHost.Pr.HeadSha);
        subject.GetProperty("url").GetString().ShouldBe(FakeCodeHost.Pr.Url);

        var detail = run;
        var deadline = DateTime.UtcNow + WalkTimeout;
        while (detail.GetProperty("stage").GetString() != "Select" && detail.GetProperty("status").GetString() != "Failed")
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Run {id} did not reach Select within {WalkTimeout}: {detail}");
            await Task.Delay(100);
            detail = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}");
        }

        detail.GetProperty("status").GetString().ShouldBe("Running", detail.ToString());
        detail.GetProperty("stage").GetString().ShouldBe("Select");
        var blast = detail.GetProperty("blastRadius");
        blast.GetProperty("reach").GetInt32().ShouldBe(2);
        blast.GetProperty("externalContract").GetInt32().ShouldBe(2, "p(yes) 0.2 maps to 1 + round(0.8)");
        blast.GetProperty("data").GetInt32().ShouldBe(4, "p(yes) 0.7 maps to 1 + round(2.8)");
        blast.GetProperty("rollbackAtRelease").GetInt32().ShouldBe(2);
        blast.GetProperty("reversalAfterMonths").GetInt32().ShouldBe(3);
        blast.GetProperty("exposure").GetInt32().ShouldBe(2);
        detail.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ShouldBe(["bugfix", "persistent-data"]);
        detail.GetProperty("models").EnumerateArray().Select(m => m.GetString()).ShouldBe([FakeDecisions.Model]);

        var nodes = detail.GetProperty("nodes").EnumerateArray().Where(n => n.GetProperty("iteration").GetInt32() == 1)
            .ToDictionary(n => n.GetProperty("nodeId").GetString()!);
        nodes["intake"].GetProperty("status").GetString().ShouldBe("Succeeded");
        nodes["intake"].GetProperty("summary").GetString()!.ShouldStartWith("2 files, +14 -3, size S");
        nodes["intake"].GetProperty("summary").GetString()!.ShouldContain("M src/Lease.cs (+12 -3)");
        nodes["delta"].GetProperty("status").GetString().ShouldBe("Succeeded");
        nodes["delta"].GetProperty("outcome").GetString().ShouldBe("full");
        var categorise = nodes["categorise"];
        categorise.GetProperty("status").GetString().ShouldBe("Succeeded", categorise.ToString());
        categorise.GetProperty("kind").GetString().ShouldBe("Decision");
        categorise.GetProperty("outcome").GetString().ShouldBe("bugfix");
        categorise.GetProperty("provider").GetString().ShouldBe("jev");
        categorise.GetProperty("model").GetString().ShouldBe(FakeDecisions.Model);
        categorise.GetProperty("transport").GetString().ShouldBe("Http");
        categorise.GetProperty("inputTokens").GetInt64().ShouldBe(1200);
        categorise.GetProperty("costUsd").GetDecimal().ShouldBe(0.0005m);
        nodes["select"].GetProperty("status").GetString().ShouldBe("Pending");
        nodes["select"].GetProperty("summary").GetString().ShouldBe(ReviewWorkflowExecutor.StopSummary);

        var asked = factory.Decisions.Requests.ShouldHaveSingleItem();
        asked.Questions.Select(q => q.Name).ShouldBe(["change_kind", "external_contract", "persistent_data", "reach",
            "rollback_at_release", "reversal_after_months", "exposure"]);
        asked.State.ShouldContain(FakeCodeHost.Pr.Title);
        asked.State.ShouldContain("src/Lease.cs");
        asked.State.ShouldContain(FakeCodeHost.Diff.Trim());
    }

    /// <summary>The real host with the code host and the decision model swapped for fakes.</summary>
    public sealed class Factory : AntiphonWebAppFactory
    {
        public FakeDecisions Decisions { get; } = new();

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<ICodeHost>();
            services.AddSingleton<ICodeHost>(new FakeCodeHost());
            services.RemoveAll<IDecisionModel>();
            services.AddSingleton<IDecisionModel>(Decisions);
        }
    }

    public sealed class FakeCodeHost : ICodeHost
    {
        public static readonly CodeHostPullRequest Pr = new(77, "Release the lease after the save", "The lease leaked when the save threw.",
            "dana", null, "main", new string('a', 40), "fix/lease", new string('b', 40), "https://github.example/acme/widgets/pull/77",
            CodeHostPullRequestState.Open, false);

        public const string Diff = "diff --git a/src/Lease.cs b/src/Lease.cs\n+        finally { lease.Release(); }\n";

        public bool Serves(ReviewHost kind) => kind == ReviewHost.GitHub;

        public CodeHostCapabilities Capabilities { get; } = new(false, true, true, true);

        public Task<CodeHostPullRequest> GetPullRequestAsync(CodeHostTarget target, CancellationToken ct) => Task.FromResult(Pr);

        public Task<IReadOnlyList<CodeHostChangedFile>> ListChangedFilesAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CodeHostChangedFile>>(
            [
                new("src/Lease.cs", CodeHostFileStatus.Modified, 12, 3),
                new("tests/LeaseTests.cs", CodeHostFileStatus.Added, 2, 0),
            ]);

        public Task<string> GetDiffAsync(CodeHostTarget target, CodeHostRange range, CancellationToken ct) => Task.FromResult(Diff);
    }

    /// <summary>Answers every question with a fixed, confident answer, as provider <c>jev</c> would.</summary>
    public sealed class FakeDecisions : IDecisionModel
    {
        public const string Model = "jev-fake-1.0";

        public List<DecisionRequest> Requests { get; } = [];

        public bool Serves(string dialect) => true;

        public Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add(request);
            var served = new DecisionServedBy(options.ProviderId, "Http", Model);
            DecisionAnswer Pick(string name, string value) => new(name, DecisionQuestionKind.Score, value, 0.9, null, false);
            DecisionAnswer YesNo(string name, double yes) => new(name, DecisionQuestionKind.Noul, yes >= 0.5 ? "yes" : "no",
                Math.Max(yes, 1 - yes), new Dictionary<string, double> { ["yes"] = yes, ["no"] = 1 - yes }, false);
            var answers = new Dictionary<string, DecisionAnswer>
            {
                ["change_kind"] = Pick("change_kind", "bugfix") with { Kind = DecisionQuestionKind.Choice },
                ["external_contract"] = YesNo("external_contract", 0.2),
                ["persistent_data"] = YesNo("persistent_data", 0.7),
                ["reach"] = Pick("reach", "A few"),
                ["rollback_at_release"] = Pick("rollback_at_release", "Easy"),
                ["reversal_after_months"] = Pick("reversal_after_months", "Moderate"),
                ["exposure"] = Pick("exposure", "Low"),
            };
            return Task.FromResult(new DecisionResult(answers, new DecisionUsage(1200, 40), 0.0005m, "actual", served));
        }
    }
}
