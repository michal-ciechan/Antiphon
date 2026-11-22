using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The whole default <c>pr-review</c> workflow over HTTP against the real host, executor and
/// Postgres, with a scripted code host, decision model, reviewer agents and verifier: a PR is taken
/// from intake to merge, once through a human approval and once through a bot approval.
/// </summary>
[Category("Integration")]
[NotInParallel("ReviewEndToEnd")]
[ClassDataSource<ReviewEndToEndApiTests.Factory>(Shared = SharedType.PerClass)]
public class ReviewEndToEndApiTests(ReviewEndToEndApiTests.Factory factory)
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    private static readonly string BaseSha = new('a', 40);
    private static readonly string HeadSha = new('b', 40);
    private static readonly string FixedHeadSha = new('c', 40);
    private static readonly string MovedBaseSha = new('d', 40);

    private const string MixedGroup = "concurrency+correctness+tests";
    private const string LeaseAnchor = "finally { _lock.Release(); }";
    private const string TestsAnchor = "public class LeaseTests { }";

    private const string Diff = """
        diff --git a/src/Lease.cs b/src/Lease.cs
        --- a/src/Lease.cs
        +++ b/src/Lease.cs
        @@ -10,4 +10,6 @@ public sealed class Lease
             public async Task SaveAsync(CancellationToken ct)
             {
        -        await _db.SaveChangesAsync(ct);
        +        try { await _db.SaveChangesAsync(ct); }
        +        finally { _lock.Release(); }
        +        _released = true;
             }
        diff --git a/tests/LeaseTests.cs b/tests/LeaseTests.cs
        new file mode 100644
        --- /dev/null
        +++ b/tests/LeaseTests.cs
        @@ -0,0 +1,2 @@
        +// covers the release
        +public class LeaseTests { }
        """;

    private const string FixDiff = """
        diff --git a/src/Lease.cs b/src/Lease.cs
        --- a/src/Lease.cs
        +++ b/src/Lease.cs
        @@ -11,3 +11,3 @@ public sealed class Lease
             {
        -        finally { _lock.Release(); }
        +        finally { await _flush; _lock.Release(); }
             }
        """;

    private static readonly IReadOnlyList<CodeHostChangedFile> Files =
    [
        new("src/Lease.cs", CodeHostFileStatus.Modified, 3, 1),
        new("tests/LeaseTests.cs", CodeHostFileStatus.Added, 2, 0),
    ];

    [Before(Test)]
    public async Task ResetAsync()
    {
        await factory.ResetAsync();
        factory.Decisions.Reset();
        factory.Reviewer.Reset();
    }

    [Test]
    public async Task A_pr_with_findings_is_fixed_human_approved_kept_up_to_date_and_merged()
    {
        const int number = 101;
        var pr = factory.Host.Open(number, "Release the lease after the save", "dana", BaseSha, HeadSha, Files, Diff);
        factory.Decisions.Categorise(externalContract: 0.2);
        Applies(seed0001: 0.9, seed0002: 0.6, seed0004: 0.8, seed0006: 0.7);
        factory.Reviewer.Script(number, 1, MixedGroup,
            new ScriptedFinding("SEED-0004", "major", 0.9, "src/Lease.cs", LeaseAnchor, "The lock is released before the flush completes",
                "The release runs while the save's flush is still pending."),
            new ScriptedFinding("SEED-0006", "nit", 0.8, "tests/LeaseTests.cs", TestsAnchor, "The test class has no test",
                "Add a test that saves and asserts the release."));
        using var client = factory.CreateClient();

        // 1. Intake.
        using var created = await client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = "acme/widgets", prNumber = number });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // 2. Round 1: a major and a nit; the run waits for fixes.
        var run = await WaitForAsync(client, id, "AwaitingFixes");
        run.GetProperty("stage").GetString().ShouldBe("AwaitingFixes");
        run.GetProperty("waitingOn").GetString().ShouldBe("Author");
        run.GetProperty("round").GetInt32().ShouldBe(1);
        run.GetProperty("botOnlyRounds").GetInt32().ShouldBe(1);
        Outcomes(run, 1).ShouldBe(
        [
            ("intake", "Succeeded", null), ("delta", "Succeeded", "full"), ("categorise", "Succeeded", "bugfix"), ("select", "Succeeded", null),
            ("group", "Succeeded", null), ("packs", "Succeeded", null), ("review", "Succeeded", null), ("collate", "Succeeded", null),
            ("verify", "Succeeded", null), ("publish", "Succeeded", null), ("route", "Succeeded", "findings"), ("await-fixes", "Waiting", null),
        ]);
        Reviewers(run, 1).ShouldBe([MixedGroup, "security"]);
        Node(run, 1, "select").GetProperty("summary").GetString()!.ShouldContain("4 selected: SEED-0001, SEED-0002, SEED-0004, SEED-0006");
        Node(run, 1, "verify").GetProperty("model").GetString().ShouldBe(ScriptedCompletions.Model);

        var findings = await FindingsAsync(client, id);
        findings.Count.ShouldBe(2);
        var major = findings["F1"];
        major.GetProperty("severity").GetString().ShouldBe("Major");
        major.GetProperty("checkId").GetString().ShouldBe("SEED-0004");
        major.GetProperty("lineStart").GetInt32().ShouldBe(13, "the anchor's line in the new file");
        major.GetProperty("posted").GetBoolean().ShouldBeTrue();
        major.GetProperty("externalThreadUrl").GetString()!.ShouldContain("#discussion_r");
        major.GetProperty("hunk").GetString()!.ShouldContain(LeaseAnchor);
        var nit = findings["F2"];
        nit.GetProperty("severity").GetString().ShouldBe("Nit");
        nit.GetProperty("posted").GetBoolean().ShouldBeTrue("round 1 lists nits in the summary");
        nit.GetProperty("externalThreadUrl").GetString()!.ShouldContain("#review-", Case.Sensitive, "a summary-only nit links to the review, not a thread");

        var review = pr.Read(p => p.Reviews.ShouldHaveSingleItem());
        review.CommitSha.ShouldBe(HeadSha);
        var comment = review.Comments.ShouldHaveSingleItem("the nit is in the summary only");
        (comment.Path, comment.Line).ShouldBe(("src/Lease.cs", 13));
        comment.Body.ShouldContain("F1");
        review.Body.ShouldContain("Listed here only");
        review.Body.ShouldContain("The test class has no test");
        pr.Read(p => p.Statuses.Where(s => s.Status.Context == "antiphon/review").Select(s => s.Status.State).First()).ShouldBe(CodeHostStatusState.Pending);
        var reviewStatus = pr.Status("antiphon/review", HeadSha).ShouldNotBeNull();
        reviewStatus.State.ShouldBe(CodeHostStatusState.Failure);
        reviewStatus.Description.ShouldBe("2 findings (1 major, 1 nit)");
        reviewStatus.TargetUrl.ShouldBe($"https://antiphon.test/reviews/{id}");

        // 3. The author answers Will fix; the reply reaches the PR thread.
        using (var answer = Request(HttpMethod.Post, $"/api/review-findings/{major.GetProperty("id").GetString()}/responses", "dana",
                   new { action = "WillFix", text = "Moving the release after the flush." }))
        using (var answered = await client.SendAsync(answer))
            answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());
        var synced = await WaitForFindingAsync(client, id, "F1", f => f.GetProperty("syncState").GetString() == "Synced");
        synced.GetProperty("status").GetString().ShouldBe("WillFix");
        synced.GetProperty("events").EnumerateArray().Single().GetProperty("syncState").GetString().ShouldBe("Synced");
        var reply = pr.Read(p => p.Replies.ShouldHaveSingleItem());
        reply.CommentId.ShouldBe("c1");
        reply.Body.ShouldContain("will fix this");
        reply.Body.ShouldContain("Moving the release after the flush.");

        // 4. The author pushes; round 2 is clean but the change now touches an external contract.
        factory.Decisions.Categorise(externalContract: 0.9).Yes("still_present_f1", 0.1);
        pr.Push(FixedHeadSha, [new("src/Lease.cs", CodeHostFileStatus.Modified, 1, 1)], FixDiff);
        (await client.PostAsync($"/api/review-runs/{id}/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        run = await WaitForAsync(client, id, "AtGate");
        run.GetProperty("round").GetInt32().ShouldBe(2);
        run.GetProperty("waitingOn").GetString().ShouldBe("Humans");
        run.GetProperty("needsHumanApproval").GetBoolean().ShouldBeTrue();
        run.GetProperty("blastRadius").GetProperty("externalContract").GetInt32().ShouldBe(5);
        var second = run.GetProperty("iterations").EnumerateArray().Single(i => i.GetProperty("iteration").GetInt32() == 2);
        second.GetProperty("trigger").GetString().ShouldBe("push");
        second.GetProperty("headSha").GetString().ShouldBe(FixedHeadSha);
        Node(run, 1, "await-fixes").GetProperty("outcome").GetString().ShouldBe("push");
        Node(run, 2, "delta").GetProperty("outcome").GetString().ShouldBe("incremental");
        Node(run, 2, "route").GetProperty("outcome").GetString().ShouldBe("clean");
        Node(run, 2, "needs-human-approval").GetProperty("outcome").GetString().ShouldBe("yes");
        Node(run, 2, "human-approve").GetProperty("status").GetString().ShouldBe("Waiting");
        factory.Decisions.Requests.ShouldContain(r => r.Questions.Any(q => q.Name == "still_present_f1"));
        factory.Reviewer.Requests.Where(r => r.Round == 2).ShouldAllBe(r => r.Pack.Contains("F2 [Nit, Open]"));

        findings = await FindingsAsync(client, id);
        findings["F1"].GetProperty("status").GetString().ShouldBe("Fixed", "still_present said no");
        findings["F2"].GetProperty("status").GetString().ShouldBe("Open", "an open nit is below round 2's bar");
        await WaitForFindingAsync(client, id, "F1", f => f.GetProperty("syncState").GetString() == "Synced");
        pr.Read(p => p.Replies.Count).ShouldBe(2, "the bot's fixed note is replied in the thread");
        pr.Read(p => p.Resolved.ToList()).ShouldBe(["t1"]);
        pr.Status("antiphon/review", FixedHeadSha)!.State.ShouldBe(CodeHostStatusState.Success);
        pr.Status("antiphon/approval", FixedHeadSha)!.State.ShouldBe(CodeHostStatusState.Pending);

        var gate = run.GetProperty("gates").EnumerateArray().ShouldHaveSingleItem();
        gate.GetProperty("kind").GetString().ShouldBe("ApprovalGate");
        gate.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldContain("blast radius: external contract 5 >= 4");
        var gateId = gate.GetProperty("id").GetString();

        // 5. The base moves while the gate is open; another user approves (the author may not).
        pr.MoveBase(MovedBaseSha, "docs/CHANGELOG.md");
        using (var byAuthor = Request(HttpMethod.Post, $"/api/review-gates/{gateId}/decision", "dana", new { decision = "Approve" }))
        using (var refused = await client.SendAsync(byAuthor))
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var byLee = Request(HttpMethod.Post, $"/api/review-gates/{gateId}/decision", "lee", new { decision = "Approve" }))
        using (var decided = await client.SendAsync(byLee))
            decided.StatusCode.ShouldBe(HttpStatusCode.OK, await decided.Content.ReadAsStringAsync());

        // 6. base-moved says moved, base-drift unaffected, the branch is updated and the PR waits for its merge.
        run = await WaitForAsync(client, id, "AwaitingMerge");
        Node(run, 2, "human-approve").GetProperty("outcome").GetString().ShouldBe("approve");
        Node(run, 2, "base-moved").GetProperty("outcome").GetString().ShouldBe("moved");
        Node(run, 2, "base-drift").GetProperty("outcome").GetString().ShouldBe("unaffected");
        Node(run, 2, "base-drift").GetProperty("summary").GetString()!.ShouldContain("no model call");
        Node(run, 2, "update-branch").GetProperty("outcome").GetString().ShouldBe("approved");
        pr.Read(p => p.BranchUpdates.ToList()).ShouldBe([FixedHeadSha]);
        var updatedHead = pr.Read(p => p.HeadSha);
        run.GetProperty("subject").GetProperty("headSha").GetString().ShouldBe(updatedHead);
        pr.Status("antiphon/approval", FixedHeadSha)!.Description.ShouldBe("Approved by lee");
        pr.Status("antiphon/approval", updatedHead)!.State.ShouldBe(CodeHostStatusState.Success, "the approval carries to the base-only head");
        factory.Decisions.Requests.ShouldNotContain(r => r.Questions.Any(q => q.Name == "base_change_affects_pr"));

        // 7. The PR is merged.
        pr.Merge();
        (await client.PostAsync($"/api/review-runs/{id}/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        run = await WaitForAsync(client, id, "Merged");
        run.GetProperty("stage").GetString().ShouldBe("Done");
        pr.Status("antiphon/approval", updatedHead)!.State.ShouldBe(CodeHostStatusState.Success);
        Outcomes(run, 2).ShouldBe(
        [
            ("intake", "Succeeded", null), ("delta", "Succeeded", "incremental"), ("categorise", "Succeeded", "bugfix"), ("select", "Succeeded", null),
            ("group", "Succeeded", null), ("packs", "Succeeded", null), ("review", "Succeeded", null), ("collate", "Succeeded", null),
            ("verify", "Succeeded", null), ("publish", "Succeeded", null), ("route", "Succeeded", "clean"),
            ("needs-human-approval", "Succeeded", "yes"), ("human-approve", "Succeeded", "approve"), ("base-moved", "Succeeded", "moved"),
            ("base-drift", "Succeeded", "unaffected"), ("update-branch", "Succeeded", "approved"), ("await-merge", "Succeeded", "merge"),
        ]);
        Node(run, 1, "await-fixes").GetProperty("outcome").GetString().ShouldBe("push");
    }

    [Test]
    public async Task A_low_risk_clean_pr_is_bot_approved_and_merged()
    {
        const int number = 102;
        var pr = factory.Host.Open(number, "Tidy the lease names", "dana", BaseSha, HeadSha, Files, Diff);
        factory.Decisions.Categorise(externalContract: 0.1);
        Applies(seed0001: 0.9, seed0002: 0.1, seed0004: 0.1, seed0006: 0.2);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = "acme/widgets", prNumber = number });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var run = await WaitForAsync(client, id, "AwaitingMerge");
        run.GetProperty("needsHumanApproval").GetBoolean().ShouldBeFalse();
        run.GetProperty("gates").GetArrayLength().ShouldBe(0);
        Reviewers(run, 1).ShouldBe(["correctness"]);
        Node(run, 1, "bot-approve").GetProperty("summary").GetString()!.ShouldContain("not checked: ci-green, no-open-human-threads");
        (await FindingsAsync(client, id)).ShouldBeEmpty();
        pr.Read(p => p.Reviews.Count).ShouldBe(0, "a clean round posts no review");
        pr.Read(p => p.Approvals.ToList()).ShouldBe([HeadSha]);
        pr.Status("antiphon/review", HeadSha)!.Description.ShouldBe("No findings above the bar");
        pr.Status("antiphon/approval", HeadSha)!.Description.ShouldBe("Approved by Antiphon");

        pr.Merge();
        (await client.PostAsync($"/api/review-runs/{id}/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        run = await WaitForAsync(client, id, "Merged");
        Outcomes(run, 1).ShouldBe(
        [
            ("intake", "Succeeded", null), ("delta", "Succeeded", "full"), ("categorise", "Succeeded", "bugfix"), ("select", "Succeeded", null),
            ("group", "Succeeded", null), ("packs", "Succeeded", null), ("review", "Succeeded", null), ("collate", "Succeeded", null),
            ("verify", "Succeeded", null), ("publish", "Succeeded", null), ("route", "Succeeded", "clean"),
            ("needs-human-approval", "Succeeded", "no"), ("bot-approve", "Succeeded", null), ("base-moved", "Succeeded", "unchanged"),
            ("await-merge", "Succeeded", "merge"),
        ]);
        pr.Read(p => p.BranchUpdates.Count).ShouldBe(0);
    }

    /// <summary>The seed checks' <c>applies</c> answers; the rest are clearly no.</summary>
    private void Applies(double seed0001, double seed0002, double seed0004, double seed0006)
    {
        factory.Decisions.Yes("applies_seed_0001", seed0001).Yes("applies_seed_0002", seed0002).Yes("applies_seed_0003", 0.05)
            .Yes("applies_seed_0004", seed0004).Yes("applies_seed_0005", 0.2).Yes("applies_seed_0006", seed0006).Yes("applies_seed_0007", 0.1);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string viewer, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(ReviewViewer.HeaderName, viewer);
        return request;
    }

    /// <summary>Polls the run until it reaches <paramref name="status"/>; a Failed run fails the test with its detail.</summary>
    private static async Task<JsonElement> WaitForAsync(HttpClient client, string id, string status)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (true)
        {
            var run = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}");
            var current = run.GetProperty("status").GetString();
            if (current == status)
                return run;
            if (current == "Failed")
                throw new InvalidOperationException($"Run {id} failed while waiting for {status}: {Failure(run)}");
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Run {id} did not reach {status} within {StepTimeout}; it is {current}: {run}");
            await Task.Delay(100);
        }
    }

    private static async Task<JsonElement> WaitForFindingAsync(HttpClient client, string id, string key, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (true)
        {
            var finding = (await FindingsAsync(client, id))[key];
            if (done(finding))
                return finding;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Finding {key} of run {id} did not settle within {StepTimeout}: {finding}");
            await Task.Delay(100);
        }
    }

    private static async Task<Dictionary<string, JsonElement>> FindingsAsync(HttpClient client, string id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}/findings")).GetProperty("items").EnumerateArray()
        .ToDictionary(f => f.GetProperty("key").GetString()!);

    private static string Failure(JsonElement run) => string.Join("; ", run.GetProperty("nodes").EnumerateArray()
        .Where(n => n.GetProperty("status").GetString() == "Failed")
        .Select(n => $"{n.GetProperty("nodeId").GetString()}[{(n.GetProperty("itemKey").ValueKind == JsonValueKind.String ? n.GetProperty("itemKey").GetString() : "")}]: {n.GetProperty("summary")}"));

    /// <summary>The iteration's top-level node path in execution order: node, status and outcome.</summary>
    private static List<(string Node, string Status, string? Outcome)> Outcomes(JsonElement run, int iteration) =>
        run.GetProperty("nodes").EnumerateArray()
            .Where(n => n.GetProperty("iteration").GetInt32() == iteration && n.GetProperty("itemKey").ValueKind == JsonValueKind.Null)
            .Select(n => (n.GetProperty("nodeId").GetString()!, n.GetProperty("status").GetString()!,
                n.GetProperty("outcome").ValueKind == JsonValueKind.String ? n.GetProperty("outcome").GetString() : null))
            .ToList();

    private static List<string> Reviewers(JsonElement run, int iteration) =>
        run.GetProperty("nodes").EnumerateArray()
            .Where(n => n.GetProperty("iteration").GetInt32() == iteration && n.GetProperty("nodeId").GetString() == "reviewer")
            .Select(n => n.GetProperty("itemKey").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static JsonElement Node(JsonElement run, int iteration, string nodeId) =>
        run.GetProperty("nodes").EnumerateArray()
            .Last(n => n.GetProperty("iteration").GetInt32() == iteration && n.GetProperty("nodeId").GetString() == nodeId
                && n.GetProperty("itemKey").ValueKind == JsonValueKind.Null);

    /// <summary>The real host with the code host, decision model, reviewer agents and verifier scripted.</summary>
    public sealed class Factory : AntiphonWebAppFactory
    {
        public ScriptedCodeHost Host { get; } = new();
        public ScriptedDecisions Decisions { get; } = new();
        public ScriptedReviewer Reviewer { get; } = new();
        public ScriptedCompletions Completions { get; } = new();

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<ICodeHost>();
            services.AddSingleton<ICodeHost>(Host);
            services.RemoveAll<IDecisionModel>();
            services.AddSingleton<IDecisionModel>(Decisions);
            services.RemoveAll<IReviewAgentRunner>();
            services.AddSingleton<IReviewAgentRunner>(Reviewer);
            services.RemoveAll<ICompletionModel>();
            services.AddSingleton<ICompletionModel>(Completions);
            services.PostConfigure<ReviewSettings>(s =>
            {
                s.AllowViewerHeader = true;
                s.DefaultViewerLogin = "ops";
                s.PublicBaseUrl = "https://antiphon.test";
            });
            services.PostConfigure<ReviewCompletionSettings>(s =>
            {
                s.Providers["scripted-verifier"] = new ReviewCompletionProvider { Dialect = "scripted", Model = ScriptedCompletions.Model };
                s.Chains["completions-cheap"] = [new ReviewDecisionChainEntry { Provider = "scripted-verifier" }];
            });
            services.PostConfigure<ReviewExecutorSettings>(s => s.PollSeconds = 1);
        }
    }
}
