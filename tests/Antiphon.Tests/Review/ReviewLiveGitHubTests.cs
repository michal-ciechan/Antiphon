using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
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
/// The review flow against real GitHub (design 8): the real GitHub adapter signed in as the
/// antiphon-review GitHub App, over a real PR, with the decision model, reviewer and verifier
/// scripted so the run is deterministic and free. It posts a real review, a real thread reply and
/// real commit statuses, so it runs only on request: the <c>review-live-github</c> command
/// (<c>scripts/antiphon review-live-github --pr &lt;n&gt;</c>) reads the app key from Bitwarden into this
/// test run's environment and runs this class. Without those variables every test is skipped.
/// Use a scratch PR labelled <c>antiphon:live-check</c> (and <c>antiphon:skip</c>, so a live
/// instance's poller leaves it alone); the findings it posts are scripted, not a real review.
/// </summary>
[Category("Integration")]
[Category("LiveGitHub")]
[NotInParallel("ReviewLiveGitHub")]
[ClassDataSource<ReviewLiveGitHubTests.Factory>(Shared = SharedType.PerClass)]
public class ReviewLiveGitHubTests(ReviewLiveGitHubTests.Factory factory)
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(120);

    public const string AppIdVariable = "ANTIPHON_LIVE_GITHUB_APP_ID";
    public const string InstallationVariable = "ANTIPHON_LIVE_GITHUB_INSTALLATION_ID";
    public const string KeyVariable = "ANTIPHON_LIVE_GITHUB_APP_KEY";
    public const string RepoVariable = "ANTIPHON_LIVE_GITHUB_REPO";
    public const string PrVariable = "ANTIPHON_LIVE_GITHUB_PR";

    /// <summary>The run the first test leaves waiting for fixes; the polling test asks it for another round.</summary>
    private static string? _runId;

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static bool Configured => Env(AppIdVariable) is not null && Env(KeyVariable) is not null && Env(RepoVariable) is not null
        && int.TryParse(Env(PrVariable), out _);

    [Test]
    public async Task A_real_pr_is_reviewed_posted_answered_and_rechecked_through_the_app()
    {
        if (!Configured)
        {
            Skip.Test($"Set {AppIdVariable}, {KeyVariable}, {RepoVariable} and {PrVariable} (the review-live-github command) to run against GitHub.");
            return;
        }

        var repo = Env(RepoVariable)!;
        var number = int.Parse(Env(PrVariable)!);
        var github = factory.Services.GetServices<ICodeHost>().First(h => h.Serves(ReviewHost.GitHub));
        var target = new CodeHostTarget(ReviewCodeHostSettings.DefaultGitHubHostId, ReviewHost.GitHub, repo, number);

        // The scratch PR must be labelled, so this never posts scripted findings on a real PR.
        var pr = await github.GetPullRequestAsync(target, CancellationToken.None);
        pr.State.ShouldBe(CodeHostPullRequestState.Open, $"PR #{number} must be open");
        var diff = await github.GetDiffAsync(target, new CodeHostRange(pr.BaseSha, pr.HeadSha), CancellationToken.None);
        var (file, anchor) = FirstAddedLine(diff);

        factory.Decisions.Categorise(externalContract: 0.1);
        factory.Decisions.Yes("applies_seed_0001", 0.9).Yes("applies_seed_0002", 0.1).Yes("applies_seed_0003", 0.05)
            .Yes("applies_seed_0004", 0.1).Yes("applies_seed_0005", 0.1).Yes("applies_seed_0006", 0.1).Yes("applies_seed_0007", 0.1);
        factory.Reviewer.Script(number, 1, "correctness",
            new ScriptedFinding("SEED-0001", "minor", 0.9, file, anchor, "Live check: a scripted finding",
                "Posted by ReviewLiveGitHubTests to exercise inline comments, replies and thread resolution. Not a real review."));
        using var client = factory.CreateClient();

        // 1. Intake over the real PR; round 1 posts one inline finding and waits for fixes.
        using var created = await client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo, prNumber = number });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var run = await WaitForAsync(client, id, "AwaitingFixes");
        _runId = id;
        var finding = (await FindingsAsync(client, id)).Values.ShouldHaveSingleItem();
        finding.GetProperty("posted").GetBoolean().ShouldBeTrue();
        finding.GetProperty("externalThreadUrl").GetString()!.ShouldContain("#discussion_r");
        Console.WriteLine($"Run {id}: posted {finding.GetProperty("externalThreadUrl").GetString()}");
        NodeSummary(run, "delta").ShouldNotContain("not posted", Case.Insensitive, "the app may post statuses");

        // 2. A dismissal is replied in the GitHub thread, which is then resolved (G-12).
        using (var answer = new HttpRequestMessage(HttpMethod.Post, $"/api/review-findings/{finding.GetProperty("id").GetString()}/responses")
               {
                   Content = JsonContent.Create(new { action = "Dismiss", reasonCategory = "OutOfScope", text = "Live check: dismissed from Antiphon." }),
               })
        {
            answer.Headers.Add(ReviewViewer.HeaderName, "live-check");
            using var answered = await client.SendAsync(answer);
            answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());
        }

        var synced = await WaitForFindingAsync(client, id, f => f.GetProperty("syncState").GetString() is "Synced" or "Failed");
        synced.GetProperty("syncState").GetString().ShouldBe("Synced", synced.ToString());

        // 3. A recheck with no new push re-reads the PR and leaves the run waiting for fixes.
        (await client.PostAsync($"/api/review-runs/{id}/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await WaitForAsync(client, id, "AwaitingFixes");
    }

    /// <summary>
    /// Polling over real GitHub: a pass lists the labelled PR (which already has the run above), and
    /// a <c>/antiphon review</c> comment from the repo owner asks for another round, which the bot
    /// answers on the PR.
    /// </summary>
    [Test]
    [DependsOn(nameof(A_real_pr_is_reviewed_posted_answered_and_rechecked_through_the_app))]
    public async Task A_poll_pass_lists_the_pr_and_a_pr_comment_asks_for_another_round()
    {
        if (!Configured)
        {
            Skip.Test($"Set {AppIdVariable}, {KeyVariable}, {RepoVariable} and {PrVariable} (the review-live-github command) to run against GitHub.");
            return;
        }

        var repo = Env(RepoVariable)!;
        var number = int.Parse(Env(PrVariable)!);
        using var client = factory.CreateClient();
        var id = _runId ?? throw new InvalidOperationException("The review test above did not leave a run.");
        var before = (await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}")).GetProperty("iterations").GetArrayLength();

        // 1. A manual pass lists the open PRs and starts nothing new: the PR already has its run.
        var pass = await PollAsync(client);
        pass.GetProperty("ok").GetBoolean().ShouldBeTrue(pass.ToString());
        var mine = pass.GetProperty("repos").EnumerateArray().Single();
        mine.GetProperty("openPullRequests").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        mine.GetProperty("runsStarted").GetInt32().ShouldBe(0);

        // 2. The owner comments a command; the next pass queues a round and the bot replies on the PR.
        var steer = $"live check {DateTime.UtcNow:HHmmss}: look again";
        var commentId = Gh("api", $"repos/{repo}/issues/{number}/comments", "-f", $"body=/antiphon review {steer}", "--jq", ".id").Trim();
        pass = await PollAsync(client);
        pass.GetProperty("repos").EnumerateArray().Single().GetProperty("commandsHandled").GetInt32().ShouldBe(1, pass.ToString());
        var deadline = DateTime.UtcNow + StepTimeout;
        while ((await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}")).GetProperty("iterations").GetArrayLength() <= before)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"No new iteration after comment {commentId}.");
            await Task.Delay(250);
        }

        var replies = Gh("api", $"repos/{repo}/issues/{number}/comments", "--paginate", "--jq",
            ".[] | select(.user.login == \"antiphon-review[bot]\") | .body");
        replies.ShouldContain("queued", Case.Insensitive, "the bot answers the command on the PR");
        replies.ShouldContain(commentId, Case.Sensitive, "the reply carries the command's answer tag");
        // The only finding was dismissed above, so the new round is clean and the bot approves; the run
        // then stops at the merge gate. It stays on (GatesOff is cleared below), so the scratch PR is
        // never merged.
        var gated = await WaitForAsync(client, id, "AtGate");
        gated.GetProperty("gates").EnumerateArray().ShouldContain(g => g.GetProperty("kind").GetString() == "MergeGate" && g.GetProperty("status").GetString() != "Done");
    }

    private static async Task<JsonElement> PollAsync(HttpClient client)
    {
        using var polled = await client.PostAsync("/api/review-polling/poll", null);
        polled.StatusCode.ShouldBe(HttpStatusCode.OK, await polled.Content.ReadAsStringAsync());
        return (await polled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lastPass");
    }

    /// <summary>Runs gh as the operator (the PR comment must come from a trusted login, not the app).</summary>
    private static string Gh(params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("gh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"gh {string.Join(' ', arguments.Take(2))} failed ({process.ExitCode}): {error}");
        return output;
    }

    /// <summary>The first added line long enough to anchor on, and its file, from a unified diff.</summary>
    private static (string File, string Anchor) FirstAddedLine(string diff)
    {
        string? file = null;
        foreach (var line in diff.Split('\n'))
        {
            if (line.StartsWith("+++ b/", StringComparison.Ordinal))
                file = line[6..].TrimEnd('\r');
            else if (file is not null && line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal) && line.Trim().Length > 12)
                return (file, line[1..].TrimEnd('\r').Trim());
        }

        throw new InvalidOperationException("The PR adds no line long enough to anchor a finding on.");
    }

    private static string NodeSummary(JsonElement run, string nodeId) =>
        run.GetProperty("nodes").EnumerateArray().Last(n => n.GetProperty("nodeId").GetString() == nodeId).GetProperty("summary").GetString() ?? "";

    private static async Task<JsonElement> WaitForAsync(HttpClient client, string id, string status)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (true)
        {
            var run = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}");
            var current = run.GetProperty("status").GetString();
            if (current == status)
                return run;
            if (current == "Failed" || DateTime.UtcNow > deadline)
                throw new InvalidOperationException($"Run {id} is {current} while waiting for {status}: {run}");
            await Task.Delay(250);
        }
    }

    private static async Task<Dictionary<string, JsonElement>> FindingsAsync(HttpClient client, string id)
    {
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}/findings");
        var items = list.ValueKind == JsonValueKind.Array ? list : list.GetProperty("items");
        return items.EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!, f => f);
    }

    private static async Task<JsonElement> WaitForFindingAsync(HttpClient client, string id, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (true)
        {
            var finding = (await FindingsAsync(client, id)).Values.Single();
            if (done(finding))
                return finding;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The finding did not settle: {finding}");
            await Task.Delay(250);
        }
    }

    /// <summary>The real host and real GitHub adapter (app sign-in from the environment); decisions, reviewer and verifier scripted.</summary>
    public sealed class Factory : AntiphonWebAppFactory
    {
        public ScriptedDecisions Decisions { get; } = new();
        public ScriptedReviewer Reviewer { get; } = new();
        public ScriptedCompletions Completions { get; } = new();

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<IDecisionModel>();
            services.AddSingleton<IDecisionModel>(Decisions);
            services.RemoveAll<IReviewAgentRunner>();
            services.AddSingleton<IReviewAgentRunner>(Reviewer);
            services.RemoveAll<ICompletionModel>();
            services.AddSingleton<ICompletionModel>(Completions);
            services.PostConfigure<ReviewSettings>(s =>
            {
                s.AllowViewerHeader = true;
                s.DefaultViewerLogin = "live-check";
            });
            services.PostConfigure<ReviewCompletionSettings>(s =>
            {
                s.Providers["scripted-verifier"] = new ReviewCompletionProvider { Dialect = "scripted", Model = ScriptedCompletions.Model };
                s.Chains["completions-cheap"] = [new ReviewDecisionChainEntry { Provider = "scripted-verifier" }];
            });
            services.PostConfigure<ReviewExecutorSettings>(s => s.PollSeconds = 1);
            // The merge gate stays on whatever user-secrets say (the review-github-setup command turns it off for
            // the repo on the desktop): a live check must never merge the scratch PR.
            services.PostConfigure<ReviewPolicySettings>(s => s.GatesOff.Clear());
            // Manual passes only (the timer stays off), and only the labelled scratch PR, draft or not.
            services.PostConfigure<ReviewPollingSettings>(s =>
            {
                s.Enabled = false;
                s.Repos =
                [
                    new ReviewPollingRepo
                    {
                        Repo = Env(RepoVariable) ?? "unset/unset",
                        IncludeDrafts = true,
                        SkipLabels = [],
                        RequireLabel = "antiphon:live-check",
                    },
                ];
            });
            services.PostConfigure<ReviewCodeHostSettings>(s =>
            {
                var host = s.Hosts[ReviewCodeHostSettings.DefaultGitHubHostId];
                host.Token = null;
                host.App = new ReviewGitHubApp
                {
                    AppId = Env(AppIdVariable),
                    InstallationId = long.TryParse(Env(InstallationVariable), out var installation) ? installation : null,
                    PrivateKey = Env(KeyVariable),
                };
            });
        }
    }
}
