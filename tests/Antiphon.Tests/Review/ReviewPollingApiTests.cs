using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// Code host polling (design 8.5) against the real host, executor and Postgres, with the scripted
/// code host, decision model, reviewers and verifier of <see cref="ReviewEndToEndApiTests"/>. The test
/// drives <see cref="ReviewPoller.PollOnceAsync"/> pass by pass (the timer is off): an open PR gets a
/// run, a push gets a new iteration, a collaborator's comment gets one round with its steer, an
/// untrusted comment and a repeated comment get nothing, and a closed PR finishes the run.
/// </summary>
[Category("Integration")]
[NotInParallel("ReviewEndToEnd")]
[ClassDataSource<ReviewPollingApiTests.Factory>(Shared = SharedType.PerClass)]
public class ReviewPollingApiTests(ReviewPollingApiTests.Factory factory)
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    private static readonly string BaseSha = new('a', 40);
    private static readonly string HeadSha = new('b', 40);
    private static readonly string FixedHeadSha = new('c', 40);

    private const string Repo = "acme/widgets";
    private const string MixedGroup = "concurrency+correctness+tests";
    private const string LeaseAnchor = "finally { _lock.Release(); }";

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

    private static readonly IReadOnlyList<CodeHostChangedFile> FixFiles = [new("src/Lease.cs", CodeHostFileStatus.Modified, 1, 1)];

    [Before(Test)]
    public async Task ResetAsync()
    {
        await factory.ResetAsync();
        factory.Decisions.Reset();
        factory.Reviewer.Reset();
    }

    [Test]
    public async Task Polling_starts_runs_follows_pushes_acts_once_on_trusted_comments_and_finishes_closed_prs()
    {
        const int number = 301;
        var pr = factory.Host.Open(number, "Release the lease after the save", "dana", BaseSha, HeadSha, Files, Diff);
        factory.Host.Open(302, "Spike: do not review", "dana", BaseSha, HeadSha, Files, Diff).Labels.Add("antiphon:skip");
        factory.Host.Open(303, "Draft: lease timeout", "dana", BaseSha, HeadSha, Files, Diff).IsDraft = true;
        factory.Decisions.Categorise(externalContract: 0.2);
        factory.Decisions.Yes("applies_seed_0001", 0.9).Yes("applies_seed_0002", 0.6).Yes("applies_seed_0003", 0.05)
            .Yes("applies_seed_0004", 0.8).Yes("applies_seed_0005", 0.2).Yes("applies_seed_0006", 0.7).Yes("applies_seed_0007", 0.1);
        factory.Reviewer.Script(number, 1, MixedGroup,
            new ScriptedFinding("SEED-0004", "major", 0.9, "src/Lease.cs", LeaseAnchor, "The lock is released before the flush completes",
                "The release runs while the save's flush is still pending."));
        using var client = factory.CreateClient();
        var poller = factory.Services.GetRequiredService<ReviewPoller>();

        // 1. The open PR gets a run; the skip-labelled PR and the draft do not.
        var repo = await PassAsync(poller);
        (repo.OpenPullRequests, repo.RunsStarted, repo.RunsRefreshed, repo.CommandsHandled).ShouldBe((3, 1, 0, 0));
        var runs = await RunsByPrAsync(client);
        runs.Keys.ToList().ShouldBe([number], "302 is labelled antiphon:skip and 303 is a draft");
        var id = runs[number];
        var run = await WaitForAsync(client, id, "AwaitingFixes", iterations: 1);
        Iteration(run, 1).GetProperty("trigger").GetString().ShouldBe("opened");

        // 2. Nothing moved: the pass refreshes nothing.
        repo = await PassAsync(poller);
        (repo.RunsStarted, repo.RunsRefreshed, repo.CommandsHandled).ShouldBe((0, 0, 0));

        // 3. The author pushes: the pass refreshes the run, which starts iteration 2 (round 2).
        factory.Decisions.Yes("still_present_f1", 0.9);
        pr.Push(FixedHeadSha, FixFiles, FixDiff);
        repo = await PassAsync(poller);
        (repo.RunsStarted, repo.RunsRefreshed).ShouldBe((0, 1));
        run = await WaitForAsync(client, id, "AwaitingFixes", iterations: 2);
        var second = Iteration(run, 2);
        second.GetProperty("trigger").GetString().ShouldBe("push");
        second.GetProperty("round").GetInt32().ShouldBe(2);
        second.GetProperty("headSha").GetString().ShouldBe(FixedHeadSha);

        // 4. A collaborator asks for a round with a steer; a NONE user's recheck is ignored.
        factory.Host.Comment(number, "9001", "lee", "COLLABORATOR", "/antiphon review focus on X");
        factory.Host.Comment(number, "9002", "mallory", "NONE", "/antiphon recheck");
        repo = await PassAsync(poller);
        (repo.RunsStarted, repo.RunsRefreshed, repo.CommandsHandled).ShouldBe((0, 0, 1));
        run = await WaitForAsync(client, id, "AwaitingFixes", iterations: 3);
        var third = Iteration(run, 3);
        third.GetProperty("trigger").GetString().ShouldBe("recheck");
        third.GetProperty("round").GetInt32().ShouldBe(2, "a requested round re-reviews in the same round");
        var wait = Node(run, 2, "await-fixes");
        wait.GetProperty("outcome").GetString().ShouldBe("recheck");
        wait.GetProperty("summary").GetString()!.ShouldContain("requested by lee (comment:9001)");
        factory.Reviewer.Requests.ShouldContain(r => r.Pack.Contains("Human steer: focus on X"), "the steer reaches the reviewers");
        pr.Read(p => p.Reactions.ToList()).ShouldBe([("9001", "eyes")], "only the trusted comment is acknowledged");
        var reply = pr.Read(p => p.ConversationReplies.ShouldHaveSingleItem());
        reply.ShouldStartWith("Round 2 queued, steer: focus on X.");
        reply.ShouldContain("<!-- antiphon:comment:9001 -->");

        // 5. The same comments on the next pass, and after a restart (a new poller), are not acted on again.
        repo = await PassAsync(poller);
        repo.CommandsHandled.ShouldBe(0);
        var restarted = ActivatorUtilities.CreateInstance<ReviewPoller>(factory.Services);
        repo = await PassAsync(restarted);
        (repo.RunsStarted, repo.RunsRefreshed, repo.CommandsHandled).ShouldBe((0, 0, 0));
        factory.Host.CommentListings.Last().ShouldBeLessThan(DateTime.UtcNow.AddMinutes(-15), "a new poller looks back two intervals");
        run = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}");
        run.GetProperty("iterations").GetArrayLength().ShouldBe(3);
        pr.Read(p => p.ConversationReplies.Count).ShouldBe(1);

        // 6. The PR is closed: the next pass finishes the run.
        pr.Close();
        repo = await PassAsync(poller);
        repo.RunsRefreshed.ShouldBe(1);
        run = await WaitForAsync(client, id, "Closed", iterations: 3);
        run.GetProperty("stage").GetString().ShouldBe("Done");

        // The polling endpoint reports the last pass.
        var polling = await client.GetFromJsonAsync<JsonElement>("/api/review-polling");
        polling.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        polling.GetProperty("intervalSeconds").GetInt32().ShouldBe(600);
        polling.GetProperty("commandPrefix").GetString().ShouldBe("/antiphon");
        var last = polling.GetProperty("lastPass");
        last.GetProperty("ok").GetBoolean().ShouldBeTrue();
        last.GetProperty("repos").EnumerateArray().Single().GetProperty("runsRefreshed").GetInt32().ShouldBe(1);
    }

    /// <summary>One pass; it must succeed for the one configured repo.</summary>
    private static async Task<ReviewPollingRepoPassDto> PassAsync(ReviewPoller poller)
    {
        var pass = await poller.PollOnceAsync("test", CancellationToken.None);
        var repo = pass.Repos.ShouldHaveSingleItem();
        repo.Error.ShouldBeNull();
        pass.Ok.ShouldBeTrue();
        return repo;
    }

    private static async Task<Dictionary<int, string>> RunsByPrAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/review-runs?repo={Uri.EscapeDataString(Repo)}")).GetProperty("items").EnumerateArray()
        .ToDictionary(r => r.GetProperty("subject").GetProperty("prNumber").GetInt32(), r => r.GetProperty("id").GetString()!);

    /// <summary>Polls the run until it reaches <paramref name="status"/> with <paramref name="iterations"/> iterations; a Failed run fails the test.</summary>
    private static async Task<JsonElement> WaitForAsync(HttpClient client, string id, string status, int iterations)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (true)
        {
            var run = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}");
            var current = run.GetProperty("status").GetString();
            var count = run.GetProperty("iterations").GetArrayLength();
            if (current == status && count == iterations)
                return run;
            if (current == "Failed")
                throw new InvalidOperationException($"Run {id} failed while waiting for {status}: {run}");
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Run {id} did not reach {status} with {iterations} iterations within {StepTimeout}; it is {current} with {count}: {run}");
            await Task.Delay(100);
        }
    }

    private static JsonElement Iteration(JsonElement run, int iteration) =>
        run.GetProperty("iterations").EnumerateArray().Single(i => i.GetProperty("iteration").GetInt32() == iteration);

    private static JsonElement Node(JsonElement run, int iteration, string nodeId) =>
        run.GetProperty("nodes").EnumerateArray()
            .Last(n => n.GetProperty("iteration").GetInt32() == iteration && n.GetProperty("nodeId").GetString() == nodeId
                && n.GetProperty("itemKey").ValueKind == JsonValueKind.Null);

    /// <summary>The real host with the code host, decision model, reviewers and verifier scripted, and polling configured but its timer off.</summary>
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
            });
            services.PostConfigure<ReviewCompletionSettings>(s =>
            {
                s.Providers["scripted-verifier"] = new ReviewCompletionProvider { Dialect = "scripted", Model = ScriptedCompletions.Model };
                s.Chains["completions-cheap"] = [new ReviewDecisionChainEntry { Provider = "scripted-verifier" }];
            });
            services.PostConfigure<ReviewExecutorSettings>(s => s.PollSeconds = 1);
            services.PostConfigure<ReviewPollingSettings>(s =>
            {
                s.Enabled = false;
                s.IntervalSeconds = 600;
                s.Repos = [new ReviewPollingRepo { Repo = Repo }];
            });
        }
    }
}
