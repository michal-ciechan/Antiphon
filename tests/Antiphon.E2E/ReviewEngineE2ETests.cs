using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.E2E.Fixtures;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

/// <summary>
/// The review engine's pages against the real server: the real host, workflow executor, Postgres and
/// local git code host over a throwaway repository, with only the model seams scripted (decisions,
/// reviewer agents, verifier; <see cref="ReviewEngineScenario"/>). The browser takes one change from
/// New review through round 1's finding, the author's reply, a pushed fix and Refresh, to round 2's
/// approval gate, which the author cannot decide and another viewer approves.
/// </summary>
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("OptIn")]
public class ReviewEngineE2ETests
{
    /// <summary>
    /// One executor chain (intake to the next parked state, or a decision to await-merge) with a
    /// one-second poll. Measured 0.8 to 2.4s on the desktop (2026-11-22); the budget leaves room for a
    /// loaded host.
    /// </summary>
    private const float StepTimeoutMs = 30_000;

    private const string Reply = "Moving the release after the flush.";

    private readonly PlaywrightFixture _browser = new();
    private ReviewEngineScenario _scenario = null!;
    private AntiphonAppFixture _app = null!;

    [Before(Test)]
    public async Task SetupAsync()
    {
        _scenario = new ReviewEngineScenario();
        _app = new AntiphonAppFixture
        {
            UsePrebuiltFrontend = true,
            ConfigureOwnedHost = _scenario.Configure,
            ConfigureOwnedServices = _scenario.ConfigureServices,
        };
        await _app.InitializeAsync();
        await _browser.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _browser.DisposeAsync();
        await _app.DisposeAsync();
        _scenario.Dispose();
    }

    /// <summary>Measured 62s for the whole test (about 50s of it the fixture's container, migrations and host boot; 10s of browser scenario).</summary>
    [Test]
    [Timeout(150_000)]
    public async Task A_local_change_is_reviewed_fixed_and_approved_by_a_non_author(CancellationToken cancellationToken)
    {
        var diagnostics = TestDiagnostics.ForCurrentTest();
        var clock = Stopwatch.StartNew();
        var git = _scenario.Git;
        var firstHead = git.HeadSha;
        var (author, _) = await _browser.NewPageAsync();
        IBrowserContext? reviewerContext = null;
        IPage? reviewer = null;
        var passed = false;
        try
        {
            // 1. Intake: New review, Local git, the repo and both refs; the title defaults to the head commit's subject.
            await author.GotoAsync($"{_app.PlaywrightAddress}/reviews");
            await Expect(author.GetByTestId("review-runs-empty")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await author.GetByTestId("review-runs-new").ClickAsync();
            var form = author.GetByTestId("review-intake-form");
            await Expect(form).ToBeVisibleAsync();
            await form.GetByText("Local git", new() { Exact = true }).ClickAsync();
            await form.GetByTestId("review-intake-repo").FillAsync(ReviewEngineScenario.Repo);
            await form.GetByTestId("review-intake-base").FillAsync(LocalReviewRepo.BaseBranch);
            await form.GetByTestId("review-intake-head").FillAsync(LocalReviewRepo.HeadBranch);
            await form.GetByTestId("review-intake-submit").ClickAsync();
            await Expect(author).ToHaveURLAsync(new Regex("/reviews/[0-9a-f-]{36}$"), new() { Timeout = 15_000 });
            var runId = new Uri(author.Url).Segments.Last();
            diagnostics.Note($"[review-e2e] run {runId} created at {clock.Elapsed.TotalSeconds:0.0}s");

            // 2. Round 1 publishes the scripted major and parks on the author.
            var header = author.GetByTestId("review-run-header");
            await Expect(header).ToContainTextAsync("Awaiting fixes", new() { Timeout = StepTimeoutMs });
            diagnostics.Note($"[review-e2e] round 1 awaiting fixes at {clock.Elapsed.TotalSeconds:0.0}s");
            await Expect(header).ToContainTextAsync("waiting on author");
            await Expect(header).ToContainTextAsync("Release the lease after the save");
            await Expect(header).ToContainTextAsync($"{LocalReviewRepo.HeadBranch} → {LocalReviewRepo.BaseBranch} @ {Short(firstHead)}");
            await Expect(header).ToContainTextAsync(ReviewEngineScenario.AuthorName);
            await Expect(author.GetByTestId("review-run-round").Locator("[aria-label='Round 1']")).ToBeVisibleAsync();

            var f1 = author.GetByTestId("review-finding-F1");
            await Expect(f1).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(f1).ToContainTextAsync(ReviewEngineScenario.FindingTitle);
            await Expect(f1).ToContainTextAsync("major");
            var detail = author.GetByTestId("review-finding-detail");
            await Expect(detail).ToContainTextAsync($"F1 · {ReviewEngineScenario.FindingTitle}");
            await Expect(detail).ToContainTextAsync("src/Lease.cs:12");
            await Expect(detail).ToContainTextAsync(ReviewEngineScenario.LeaseAnchor);
            await Expect(detail).ToContainTextAsync("not posted inline");
            await PlaywrightFixture.CapturePageAsync(author, "round1_findings");

            var round1 = await RunAsync(runId);
            round1.GetProperty("status").GetString().ShouldBe("AwaitingFixes");
            round1.GetProperty("subject").GetProperty("host").GetString().ShouldBe("Local");
            round1.GetProperty("subject").GetProperty("authorLogin").GetString().ShouldBe(ReviewEngineScenario.Author);
            round1.GetProperty("subject").GetProperty("baseSha").GetString().ShouldBe(git.BaseSha);
            round1.GetProperty("subject").GetProperty("headSha").GetString().ShouldBe(firstHead);
            Node(round1, 1, "route").GetProperty("outcome").GetString().ShouldBe("findings");
            Node(round1, 1, "await-fixes").GetProperty("status").GetString().ShouldBe("Waiting");
            Node(round1, 1, "publish").GetProperty("summary").GetString()!.ShouldContain("kept in Antiphon");

            // 3. The author answers Will fix through the composer and posts it.
            await detail.GetByTestId("review-action-WillFix").ClickAsync();
            await detail.GetByLabel("Reply text").FillAsync(Reply);
            var submit = author.GetByTestId("review-submit-replies");
            await Expect(submit).ToBeEnabledAsync();
            await Expect(submit).ToContainTextAsync($"Post 1 reply to the code host as {ReviewEngineScenario.AuthorName} via Antiphon");
            await submit.ClickAsync();
            await Expect(author.GetByText("Posted 1 reply")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(author.GetByTestId("review-finding-thread")).ToContainTextAsync(Reply, new() { Timeout = 15_000 });
            await Expect(f1).ToContainTextAsync("will fix", new() { Timeout = 15_000 });

            // A local host has no thread: the executor settles the reply as not applicable.
            var answered = await WaitForFindingAsync(runId, "F1", f => f.GetProperty("syncState").GetString() == "NotApplicable");
            answered.GetProperty("status").GetString().ShouldBe("WillFix");
            var willFix = answered.GetProperty("events").EnumerateArray().ShouldHaveSingleItem();
            willFix.GetProperty("action").GetString().ShouldBe("WillFix");
            willFix.GetProperty("actorLogin").GetString().ShouldBe(ReviewEngineScenario.Author);
            willFix.GetProperty("text").GetString().ShouldBe(Reply);
            willFix.GetProperty("syncState").GetString().ShouldBe("NotApplicable");

            // 4. The author pushes the fix; Refresh starts round 2, which is clean but needs a human approval.
            var fixedHead = git.PushFix();
            await author.GetByTestId("review-run-refresh").ClickAsync();
            await Expect(author.GetByText("Refresh requested", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(header).ToContainTextAsync("At gate", new() { Timeout = StepTimeoutMs });
            diagnostics.Note($"[review-e2e] round 2 at gate at {clock.Elapsed.TotalSeconds:0.0}s");
            await Expect(header).ToContainTextAsync("waiting on humans");
            await Expect(header).ToContainTextAsync($"@ {Short(fixedHead)}");
            await Expect(author.GetByTestId("review-run-round").Locator("[aria-label='Round 2']")).ToBeVisibleAsync();
            await Expect(f1).ToContainTextAsync("fixed", new() { Timeout = 15_000 });

            var authorGate = author.GetByTestId("review-gate-panel");
            await Expect(authorGate).ToContainTextAsync("Approval gate");
            await Expect(authorGate).ToContainTextAsync("blast radius: external contract 5 >= 4");
            await Expect(authorGate.GetByTestId("review-gate-blocked")).ToHaveTextAsync("You are the author");
            await Expect(authorGate.GetByRole(AriaRole.Button, new() { Name = "Decide gate" })).ToBeDisabledAsync();
            await PlaywrightFixture.CapturePageAsync(author, "round2_gate_author");

            var atGate = await RunAsync(runId);
            atGate.GetProperty("round").GetInt32().ShouldBe(2);
            atGate.GetProperty("needsHumanApproval").GetBoolean().ShouldBeTrue();
            Node(atGate, 1, "await-fixes").GetProperty("outcome").GetString().ShouldBe("push");
            Node(atGate, 2, "delta").GetProperty("outcome").GetString().ShouldBe("incremental");
            Node(atGate, 2, "route").GetProperty("outcome").GetString().ShouldBe("clean");
            Node(atGate, 2, "needs-human-approval").GetProperty("outcome").GetString().ShouldBe("yes");
            Node(atGate, 2, "human-approve").GetProperty("status").GetString().ShouldBe("Waiting");
            (await FindingsAsync(runId))["F1"].GetProperty("status").GetString().ShouldBe("Fixed", "still_present said no");
            var gate = atGate.GetProperty("gates").EnumerateArray().ShouldHaveSingleItem();
            gate.GetProperty("kind").GetString().ShouldBe("ApprovalGate");
            gate.GetProperty("viewerCanDecide").GetBoolean().ShouldBeFalse();
            var gateId = gate.GetProperty("id").GetString()!;

            // The server refuses the author too, not only the UI.
            using (var byAuthor = await _app.HttpClient.PostAsJsonAsync($"/api/review-gates/{gateId}/decision", new { decision = "Approve" }, cancellationToken))
                byAuthor.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await byAuthor.Content.ReadAsStringAsync(cancellationToken));

            // 5. The timeline shows both iterations: the manual intake and the push.
            await author.GetByRole(AriaRole.Tab, new() { Name = "Timeline" }).ClickAsync();
            var it1 = author.GetByTestId("review-iteration-1");
            await Expect(it1).ToContainTextAsync("it1 · R1");
            await Expect(it1).ToContainTextAsync("manual intake");
            await Expect(it1).ToContainTextAsync($"{Short(git.BaseSha)}..{Short(firstHead)}");
            var it2 = author.GetByTestId("review-iteration-2");
            await Expect(it2).ToContainTextAsync("it2 · R2");
            await Expect(it2).ToContainTextAsync("push");
            await Expect(it2).ToContainTextAsync($"..{Short(fixedHead)}");
            var iterations = atGate.GetProperty("iterations").EnumerateArray()
                .Select(i => (i.GetProperty("iteration").GetInt32(), i.GetProperty("round").GetInt32(), i.GetProperty("trigger").GetString()))
                .ToList();
            iterations.ShouldBe([(1, 1, "manual"), (2, 2, "push")]);

            // 6. Another viewer finds the gate in the queue and approves it.
            reviewerContext = await _browser.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Review-Viewer"] = ReviewEngineScenario.Reviewer },
            });
            reviewer = await reviewerContext.NewPageAsync();
            diagnostics.Attach(reviewer);
            await reviewer.GotoAsync($"{_app.PlaywrightAddress}/review-queue");
            await reviewer.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex("^All") }).ClickAsync();
            var row = reviewer.GetByTestId($"review-queue-row-{runId}");
            await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(row).ToContainTextAsync("Approval gate");
            await row.GetByRole(AriaRole.Link, new() { Name = "Release the lease after the save" }).ClickAsync();
            await Expect(reviewer).ToHaveURLAsync(new Regex($"/reviews/{runId}$"));

            var reviewerGate = reviewer.GetByTestId("review-gate-panel");
            await Expect(reviewerGate).ToContainTextAsync("Approval gate", new() { Timeout = 15_000 });
            await Expect(reviewerGate.GetByTestId("review-gate-blocked")).ToHaveCountAsync(0);
            await reviewerGate.GetByRole(AriaRole.Radio, new() { Name = "Approve", Exact = true }).CheckAsync();
            var decide = reviewerGate.GetByTestId("review-gate-decide");
            await Expect(decide).ToHaveTextAsync($"Approve {Short(fixedHead)}");
            await decide.ClickAsync();
            await Expect(reviewer.GetByText("Approve: recorded")).ToBeVisibleAsync(new() { Timeout = 15_000 });

            // 7. The run walks on to await-merge; both viewers' pages follow.
            var reviewerHeader = reviewer.GetByTestId("review-run-header");
            await Expect(reviewerHeader).ToContainTextAsync("Awaiting merge", new() { Timeout = StepTimeoutMs });
            diagnostics.Note($"[review-e2e] approved, awaiting merge at {clock.Elapsed.TotalSeconds:0.0}s");
            var decided = reviewer.GetByTestId("review-gate-decided");
            await Expect(decided).ToContainTextAsync("Approve");
            await Expect(decided).ToContainTextAsync(ReviewEngineScenario.Reviewer);
            await PlaywrightFixture.CapturePageAsync(reviewer, "approved_reviewer");
            var followed = Stopwatch.StartNew();
            await Expect(header).ToContainTextAsync("Awaiting merge", new() { Timeout = StepTimeoutMs });
            diagnostics.Note($"[review-e2e] the author's page showed Awaiting merge {followed.Elapsed.TotalSeconds:0.0}s after the reviewer's");

            var done = await RunAsync(runId);
            done.GetProperty("status").GetString().ShouldBe("AwaitingMerge");
            done.GetProperty("subject").GetProperty("headSha").GetString().ShouldBe(fixedHead);
            var decidedGate = done.GetProperty("gates").EnumerateArray().ShouldHaveSingleItem();
            decidedGate.GetProperty("status").GetString().ShouldBe("Done");
            decidedGate.GetProperty("decision").GetString().ShouldBe("Approve");
            decidedGate.GetProperty("decidedBy").GetString().ShouldBe(ReviewEngineScenario.Reviewer);
            Node(done, 2, "human-approve").GetProperty("outcome").GetString().ShouldBe("approve");
            Node(done, 2, "base-moved").GetProperty("outcome").GetString().ShouldBe("unchanged");
            Node(done, 2, "await-merge").GetProperty("status").GetString().ShouldBe("Waiting");
            _scenario.Reviewer1.Requests.Where(r => r.Round == 1).ShouldContain(r => r.AssignedChecks.Contains("SEED-0004"));
            _scenario.Reviewer1.Requests.ShouldContain(r => r.Round == 2);
            _scenario.Decisions.Requests.ShouldContain(r => r.Questions.Any(q => q.Name == "still_present_f1"));

            diagnostics.Note($"[review-e2e] scenario finished at {clock.Elapsed.TotalSeconds:0.0}s");
            passed = true;
        }
        finally
        {
            if (reviewer is not null)
                await PlaywrightFixture.CapturePageAsync(reviewer, passed ? "reviewer_pass_final" : "reviewer_FAIL_final");
            await PlaywrightFixture.CaptureOnCompletionAsync(author, passed);
            if (reviewerContext is not null)
                await reviewerContext.DisposeAsync();
        }
    }

    private static string Short(string sha) => sha[..7];

    private async Task<JsonElement> RunAsync(string runId) =>
        await _app.HttpClient.GetFromJsonAsync<JsonElement>($"/api/review-runs/{runId}");

    private async Task<Dictionary<string, JsonElement>> FindingsAsync(string runId) =>
        (await _app.HttpClient.GetFromJsonAsync<JsonElement>($"/api/review-runs/{runId}/findings")).GetProperty("items").EnumerateArray()
        .ToDictionary(f => f.GetProperty("key").GetString()!);

    private async Task<JsonElement> WaitForFindingAsync(string runId, string key, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(StepTimeoutMs);
        while (true)
        {
            var finding = (await FindingsAsync(runId))[key];
            if (done(finding))
                return finding;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Finding {key} of run {runId} did not settle within {StepTimeoutMs / 1000:0}s: {finding}");
            await Task.Delay(200);
        }
    }

    /// <summary>The iteration's last top-level execution of <paramref name="nodeId"/>.</summary>
    private static JsonElement Node(JsonElement run, int iteration, string nodeId) =>
        run.GetProperty("nodes").EnumerateArray()
            .Last(n => n.GetProperty("iteration").GetInt32() == iteration && n.GetProperty("nodeId").GetString() == nodeId
                && n.GetProperty("itemKey").ValueKind == JsonValueKind.Null);
}
