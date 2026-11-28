using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

public partial class ReviewEndToEndApiTests
{
    [Test]
    public async Task C2110_S1_pg_Declared_publish_inputs_use_the_latest_row_of_the_named_node_in_this_iteration()
    {
        const int number = 21101;
        var pr = factory.Host.Open(number, "Explicit provenance", "dana", BaseSha, HeadSha, Files, Diff);
        var graph = PublishGraph(new Dictionary<string, object?> { ["inputs"] = new[] { "collate", "verify" } });
        var verify = graph.Node("verify")!;
        graph = graph with
        {
            Nodes = [.. graph.Nodes, verify with { Id = "verify-decoy" }],
            Edges = [.. graph.Edges.Where(e => e.From != "verify"),
                new("verify", "verify-decoy", null, null, null, null), new("verify-decoy", "publish", null, null, null, null)],
        };
        var named = PublishCandidate("A", "Named verify finding A", 13);
        var decoy = PublishCandidate("B", "Decoy verify finding B", 14);
        var id = await WalkPublishAsync(number, graph,
        [
            Producer("collate", ReviewNodeKind.Join, 1, new CollateOutput([named, decoy], [], [])),
            Producer("verify", ReviewNodeKind.Completion, 2, new VerifyOutput(["A"], [], true)),
            Producer("verify-decoy", ReviewNodeKind.Completion, 3, new VerifyOutput(["B"], [], true)),
        ]);

        await using var reader = factory.Services.CreateAsyncScope();
        var db = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.ReviewRuns.AsNoTracking().Include(r => r.Iterations).Include(r => r.Nodes).SingleAsync(r => r.Id == id);
        run.Status.ShouldBe(ReviewRunStatus.AwaitingFixes);
        var iteration = run.Iterations.ShouldHaveSingleItem();
        iteration.HeadSha.ShouldBe(HeadSha);
        var rows = run.Nodes.OrderBy(n => n.Sequence).ToList();
        rows.Take(3).Select(n => n.NodeId).ShouldBe(["collate", "verify", "verify-decoy"]);
        rows.Take(3).ShouldAllBe(n => n.Iteration == iteration.Iteration && n.Status == ReviewNodeStatus.Succeeded && n.ItemKey == null);
        ReviewWalk.Output<VerifyOutput>(rows[1])!.Kept.ShouldBe(["A"]);
        ReviewWalk.Output<VerifyOutput>(rows[2])!.Kept.ShouldBe(["B"]);
        var publish = rows.Single(n => n.NodeId == "publish");
        publish.Status.ShouldBe(ReviewNodeStatus.Succeeded, publish.Summary);
        var output = ReviewWalk.Output<PublishOutput>(publish).ShouldNotBeNull();
        output.Inline.ShouldBe(["F1"]);
        var receipt = Receipt(pr, output);
        receipt.Posted.ReviewId.ShouldBe(output.ReviewId);
        receipt.Target.Repo.ShouldBe("acme/widgets");
        receipt.Target.PrNumber.ShouldBe(number);
        receipt.Review.CommitSha.ShouldBe(iteration.HeadSha);
        var finding = await db.ReviewFindings.AsNoTracking().SingleAsync(f => f.RunId == id);
        var acceptedComment = receipt.Posted.Comments.ShouldHaveSingleItem();
        finding.ExternalCommentId.ShouldBe(acceptedComment.CommentId);
        finding.ExternalThreadId.ShouldBe(acceptedComment.ThreadId);
        finding.Posted.ShouldBeTrue();

        await AssertDeclaredContextAsync();

        receipt.Review.Body.ShouldContain(named.Title, Case.Sensitive, "Publish must use the declared Verify execution");
        receipt.Review.Body.ShouldNotContain(decoy.Title);
        var inline = receipt.Review.Comments.ShouldHaveSingleItem();
        (inline.Path, inline.Line).ShouldBe((named.File, named.Line));
    }

    private async Task AssertDeclaredContextAsync()
    {
        const int number = 21102;
        var pr = factory.Host.Open(number, "Declared context", "dana", BaseSha, HeadSha, Files, Diff);
        var graph = PublishGraph(new Dictionary<string, object?>
        {
            ["inputs"] = new[] { "select", "verify", "collate", "categorise" },
            ["summaryTemplate"] = Lines("{{defaultBody}}", "", "{{categorisation}}", "", "{{blastRadius}}", "", "{{categories}}", "", "{{selectedChecks}}"),
        });
        graph = graph with
        {
            Nodes = [.. graph.Nodes, graph.Node("categorise")! with { Id = "categorise-decoy" }, graph.Node("select")! with { Id = "select-decoy" }],
        };
        var candidate = PublishCandidate("A", "Finding with context", 13);
        var id = await WalkPublishAsync(number, graph,
        [
            Producer("categorise", ReviewNodeKind.Decision, 1, new CategorisationOutcome("bugfix", 1, 2, 1, 2, 1, 1, ["iteration"])),
            Producer("categorise-decoy", ReviewNodeKind.Decision, 2, new CategorisationOutcome("DECOY", 5, 5, 5, 5, 5, 5, ["DECOY"])),
            Producer("select", ReviewNodeKind.Decision, 3, new SelectOutput("fixture", [],
            [
                new("ZZZ", "Last check", "fixture", ReviewSeverity.Nit, null, true, "trigger"),
                new("AAA", "First check", "fixture", ReviewSeverity.Major, null, true, "trigger"),
                new("REJECTED", "Rejected check", "fixture", ReviewSeverity.Major, null, false, "trigger"),
            ]), ReviewNodeStatus.FailedContinued),
            Producer("select-decoy", ReviewNodeKind.Decision, 4, new SelectOutput("fixture", [],
                [new("DECOY", "Decoy check", "fixture", ReviewSeverity.Major, null, true, "trigger")])),
            Producer("collate", ReviewNodeKind.Join, 5, new CollateOutput([candidate], [], [])),
            Producer("verify", ReviewNodeKind.Completion, 6, new VerifyOutput(["A"], [], true)),
        ], arrange: run =>
        {
            run.BlastReach = 3;
            run.BlastExternalContract = 3;
            run.BlastData = 2;
            run.BlastRollbackAtRelease = 2;
            run.BlastReversalAfterMonths = 2;
            run.BlastExposure = 1;
            run.Categories = ["feature", "external-contract"];
        });

        await using var reader = factory.Services.CreateAsyncScope();
        var db = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var publish = await db.ReviewNodeExecutions.AsNoTracking().SingleAsync(n => n.RunId == id && n.NodeId == "publish");
        publish.Status.ShouldBe(ReviewNodeStatus.Succeeded, publish.Summary);
        var receipt = Receipt(pr, ReviewWalk.Output<PublishOutput>(publish).ShouldNotBeNull());
        receipt.Target.PrNumber.ShouldBe(number);
        receipt.Review.CommitSha.ShouldBe(HeadSha);
        receipt.Review.Body.ShouldContain(Lines(
            "**Categorisation this iteration** · bugfix",
            "- Reach: 1 · One place",
            "- External contract: 2 · Unlikely",
            "- Persistent data: 1 · None",
            "- Rollback at release: 2 · Easy",
            "- Reversal after months: 1 · Trivial",
            "- Exposure: 1 · Internal only",
            "**Categories this iteration** · iteration", "",
            "**Blast radius across all iterations**",
            "- Reach: 3 · A module",
            "- External contract: 3 · Possible",
            "- Persistent data: 2 · Unlikely",
            "- Rollback at release: 2 · Easy",
            "- Reversal after months: 2 · Easy",
            "- Exposure: 1 · Internal only", "",
            "**Categories across all iterations** · feature, external&#45;contract", "",
            "**Selected checks**",
            "- AAA · First check",
            "- ZZZ · Last check").ReplaceLineEndings("\n"));
        receipt.Review.Body.ShouldNotContain("DECOY");
        receipt.Review.Body.ShouldNotContain("REJECTED");
        var runLink = FixtureRunLink(id);
        receipt.Review.Body.ShouldStartWith($"**Antiphon review**, round 1 (iteration 1)\n\n{runLink}\n");
        receipt.Review.Body.Split(runLink, StringSplitOptions.None).Length.ShouldBe(2);
        receipt.Review.Comments.ShouldHaveSingleItem().Body.ShouldEndWith(runLink + Environment.NewLine);
    }

    [Test]
    public async Task C2110_S1_pg_Custom_summary_cannot_post_a_clean_or_below_bar_round()
    {
        var graph = PublishGraph(new Dictionary<string, object?>
        {
            ["inputs"] = new[] { "collate", "verify" },
            ["summaryTemplate"] = Lines("Custom fixture {{round}}", "", "{{findings}}", "{{belowBar}}"),
        });
        var below = PublishCandidate("A", "Below the bar", 13) with { Confidence = 0.01 };
        var nit = PublishCandidate("B", "Summary nit", 14) with { Severity = ReviewSeverity.Nit };
        var outside = PublishCandidate("C", "Outside hunk", 55) with { InDiff = false };
        var inline = PublishCandidate("D", "Inline control", 13);

        var clean = await PostFixtureAsync(21103, graph, []);
        var belowOnly = await PostFixtureAsync(21104, graph, [below]);
        var eligible = await PostFixtureAsync(21105, graph, [nit, outside, inline]);

        clean.Pr.Read(p => p.AcceptedReviews.Count).ShouldBe(0);
        clean.Findings.ShouldBeEmpty();
        belowOnly.Pr.Read(p => p.AcceptedReviews.Count).ShouldBe(0);
        belowOnly.Output.BelowBar.ShouldBe(["F1"]);
        belowOnly.Findings.ShouldHaveSingleItem().Posted.ShouldBeFalse();
        eligible.Output.SummaryOnly.ShouldBe(["F1", "F2"]);
        eligible.Output.Inline.ShouldBe(["F3"]);
        var review = Receipt(eligible.Pr, eligible.Output).Review;
        review.Body.ShouldBe(Lines("Custom fixture 1", "", "Findings:", "- **F3** major · src&#47;Lease&#46;&#8203;cs: Inline control",
            "", "Listed here only:", "- F1 nit · src&#47;Lease&#46;&#8203;cs&#58;&#8203;14: Summary nit",
            "- F2 major · src&#47;Lease&#46;&#8203;cs&#58;&#8203;55: Outside hunk", "", FixtureRunLink(eligible.RunId), "").ReplaceLineEndings("\n"));
        var comment = review.Comments.ShouldHaveSingleItem();
        (comment.Path, comment.Line).ShouldBe((inline.File, inline.Line));
        eligible.Findings.ShouldAllBe(f => f.Posted);
    }

    [Test]
    public async Task C2110_S1_pg_Publish_respects_host_capability_and_pr_identity()
    {
        var graph = PublishGraph(new Dictionary<string, object?>
        {
            ["inputs"] = new[] { "collate", "verify" },
            ["summaryTemplate"] = "Only supported PRs post\n\n{{findings}}",
        });
        var candidate = PublishCandidate("A", "Above bar with custom summary", 13);
        var supported = await PostFixtureAsync(21106, graph, [candidate]);
        var capabilities = factory.Host.Capabilities;
        factory.Host.Capabilities = capabilities with { CanPostReviews = false };
        try
        {
            var incapable = await PostFixtureAsync(21107, graph, [candidate]);
            incapable.Output.ReviewId.ShouldBeNull();
            incapable.Findings.ShouldHaveSingleItem().Posted.ShouldBeFalse();
            incapable.Pr.Read(p => p.AcceptedReviews.Count).ShouldBe(0);

            factory.Host.Capabilities = capabilities with { SupportsCommitStatuses = false };
            var noPr = await PostFixtureAsync(21108, graph, [candidate], hasPr: false);
            noPr.Output.ReviewId.ShouldBeNull();
            noPr.Findings.ShouldHaveSingleItem().Posted.ShouldBeFalse();
            noPr.Pr.Read(p => p.AcceptedReviews.Count).ShouldBe(0);
            factory.Host.ReviewPostAttempts.Where(t => t.PrNumber is 21106 or 21107 or null)
                .Select(t => t.PrNumber).ShouldBe(new int?[] { 21106 });
            Receipt(supported.Pr, supported.Output).Review.Body.ShouldContain(candidate.Title);
        }
        finally
        {
            factory.Host.Capabilities = capabilities;
        }
    }

    [Test]
    public async Task C2110_S1_pg_Authored_summary_budget_includes_the_recipient_backlink()
    {
        var candidate = PublishCandidate("A", "Budget boundary finding", 55) with { InDiff = false };
        var settings = factory.Services.GetRequiredService<IOptions<ReviewSettings>>().Value;
        var publicBaseUrl = settings.PublicBaseUrl;
        try
        {
            settings.PublicBaseUrl = "https://antiphon.test";
            const string section = "\nListed here only:\n- F1 major · src&#47;Lease&#46;&#8203;cs&#58;&#8203;55: Budget boundary finding";
            const int repetitions = 50;
            var titleBytes = (ReviewSummaryRenderer.MaxCustomBodyBytes - System.Text.Encoding.UTF8.GetByteCount(section)
                - 2 * repetitions) / repetitions;
            // The fallback is short. The template by itself fits, while the final body with the link does not.
            var graph = PublishGraph(new Dictionary<string, object?>
            {
                ["summaryTemplate"] = string.Concat(Enumerable.Repeat("{{run.title}}\n\n", repetitions)) + "{{findings}}",
            });
            var repeatedTitle = new string('X', titleBytes);
            var pr = factory.Host.Open(21113, "Budget fixture", "dana", BaseSha, HeadSha, Files, Diff);
            var id = await WalkPublishAsync(21113, graph,
            [
                Producer("collate", ReviewNodeKind.Join, 1, new CollateOutput([candidate], [], [])),
                Producer("verify", ReviewNodeKind.Completion, 2, new VerifyOutput(["A"], [], true)),
            ], arrange: run => run.Subject.Title = repeatedTitle);

            await using var reader = factory.Services.CreateAsyncScope();
            var db = reader.ServiceProvider.GetRequiredService<AppDbContext>();
            var publish = await db.ReviewNodeExecutions.AsNoTracking().SingleAsync(n => n.RunId == id && n.NodeId == "publish");
            var body = Receipt(pr, ReviewWalk.Output<PublishOutput>(publish).ShouldNotBeNull()).Review.Body;
            System.Text.Encoding.UTF8.GetByteCount(body).ShouldBeLessThanOrEqualTo(ReviewSummaryRenderer.MaxCustomBodyBytes);
            body.ShouldStartWith("**Antiphon review**");
            body.ShouldContain(candidate.Title);
            body.Split(FixtureRunLink(id), StringSplitOptions.None).Length.ShouldBe(2);
        }
        finally
        {
            settings.PublicBaseUrl = publicBaseUrl;
        }
    }

    [Test]
    public async Task C2110_S1_pg_Truncated_summary_leaves_omitted_findings_unposted()
    {
        var graph = PublishGraph(new Dictionary<string, object?> { ["summaryTemplate"] = "{{unknown}}" });
        var candidates = Enumerable.Range(1, 12).Select(index => PublishCandidate($"C{index}", new string('<', 1000), 50 + index)
            with { File = new string('\x60', 1000), InDiff = false }).ToList();
        var fixture = await PostFixtureAsync(21117, graph, candidates);

        var accepted = Receipt(fixture.Pr, fixture.Output);
        var body = accepted.Review.Body;
        System.Text.Encoding.UTF8.GetByteCount(body).ShouldBeLessThanOrEqualTo(ReviewSummaryRenderer.MaxCustomBodyBytes);
        body.ShouldContain("Some findings were omitted");
        fixture.Output.SummaryOnly.Count.ShouldBeGreaterThan(0);
        fixture.Output.SummaryOnly.Count.ShouldBeLessThan(candidates.Count);
        foreach (var finding in fixture.Findings)
        {
            var visible = fixture.Output.SummaryOnly.Contains(finding.Key);
            body.Contains($"- {finding.Key} major · ", StringComparison.Ordinal).ShouldBe(visible);
            finding.Posted.ShouldBe(visible);
            finding.ExternalThreadUrl.ShouldBe(visible ? accepted.Posted.Url : null);
        }
    }

    [Test]
    public async Task C2110_S1_pg_Pinned_hidden_coverage_falls_back_to_visible_recipient_findings()
    {
        var graph = PublishGraph(new Dictionary<string, object?>
        {
            ["inputs"] = new[] { "collate", "verify" },
            ["summaryTemplate"] = "A template stored before validation\n\n~~~md\n{{findings}}\n~~~",
        });
        var candidate = PublishCandidate("A", "Visible summary finding", 55) with { InDiff = false };
        var fixture = await PostFixtureAsync(21114, graph, [candidate]);
        var review = Receipt(fixture.Pr, fixture.Output).Review;
        review.Body.ShouldStartWith("**Antiphon review**, round 1 (iteration 1)\n");
        review.Body.ShouldNotContain("~~~");
        review.Body.ShouldContain("Listed here only:\n- F1 major · src&#47;Lease&#46;&#8203;cs&#58;&#8203;55: " + candidate.Title);
        var html = Markdig.Markdown.ToHtml(review.Body);
        html.ShouldContain("<li>F1 major · ");
        html.ShouldContain(candidate.Title + "</li>");
        fixture.Findings.ShouldHaveSingleItem().Posted.ShouldBeTrue();
        review.Comments.ShouldBeEmpty();
    }

    [Test]
    public async Task C2110_S1_pg_Pinned_Join_cannot_execute_Publish()
    {
        var pr = factory.Host.Open(21118, "Invalid Publish kind", "dana", BaseSha, HeadSha, Files, Diff);
        var graph = PublishGraph(new Dictionary<string, object?>());
        graph = graph with { Nodes = graph.Nodes.Select(n => n.Id == "publish" ? n with { Kind = ReviewNodeKind.Join } : n).ToList() };
        var candidate = PublishCandidate("A", "Must never post", 55) with { InDiff = false };
        var id = await WalkPublishAsync(21118, graph,
        [
            Producer("collate", ReviewNodeKind.Join, 1, new CollateOutput([candidate], [], [])),
            Producer("verify", ReviewNodeKind.Completion, 2, new VerifyOutput(["A"], [], true)),
        ]);
        await using var reader = factory.Services.CreateAsyncScope();
        var db = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.ReviewRuns.AsNoTracking().Include(r => r.Nodes).SingleAsync(r => r.Id == id);
        run.Status.ShouldBe(ReviewRunStatus.Failed);
        var attempted = run.Nodes.Where(n => n.NodeId == "publish").ShouldHaveSingleItem();
        attempted.Status.ShouldBe(ReviewNodeStatus.Failed);
        attempted.Summary!.ShouldContain("Publish requires a Code node.");
        pr.Read(p => p.AcceptedReviews.Count).ShouldBe(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C2110_S1_pg_Legacy_default_body_and_inline_comments_are_exact(bool configured)
    {
        var graph = PublishGraph(new Dictionary<string, object?>());
        var inline = PublishCandidate("A", "Inline <literal>", 13) with { Check = "SEED-0004", Suggestion = "replacement\n" };
        var nit = PublishCandidate("B", "Summary nit", 16) with { Severity = ReviewSeverity.Nit };
        var outside = PublishCandidate("C", "Outside hunk", 55) with { InDiff = false };
        var below = PublishCandidate("D", "Below the bar", 22) with { Confidence = 0.01 };
        var dropped = PublishCandidate("E", "Dropped by verify", 33);
        var settings = factory.Services.GetRequiredService<IOptions<ReviewSettings>>().Value;
        var publicBaseUrl = settings.PublicBaseUrl;
        settings.PublicBaseUrl = configured ? " https://ANTIPHON.test/review-base/// " : null;
        var observations = new List<(ReviewNodeStatus Status, Guid RunId, CodeHostReview Review)>();
        try
        {
            foreach (var (status, index) in new[] { ReviewNodeStatus.Succeeded, ReviewNodeStatus.Skipped, ReviewNodeStatus.FailedContinued }
                .Select((value, index) => (value, index)))
            {
                var fixture = await PostFixtureAsync(21109 + index, graph, [inline, nit, outside, below, dropped],
                    new VerifyOutput(["A", "B", "C", "D"], [new("fixture", "none", dropped.File, dropped.Anchor, "disproved")],
                        status == ReviewNodeStatus.Succeeded), status);
                fixture.Output.Inline.ShouldBe(["F1"]);
                fixture.Output.SummaryOnly.ShouldBe(["F2", "F3"]);
                fixture.Output.BelowBar.ShouldBe(["F4"]);
                fixture.Findings.Select(f => f.Key).ShouldBe(["F1", "F2", "F3", "F4"]);
                var accepted = Receipt(fixture.Pr, fixture.Output);
                accepted.Review.CommitSha.ShouldBe(HeadSha);
                var posted = accepted.Posted.Comments.ShouldHaveSingleItem();
                fixture.Findings[0].ExternalCommentId.ShouldBe(posted.CommentId);
                fixture.Findings[0].ExternalThreadId.ShouldBe(posted.ThreadId);
                fixture.Findings[1].ExternalThreadUrl.ShouldBe(accepted.Posted.Url);
                observations.Add((status, fixture.RunId, accepted.Review));
            }
        }
        finally
        {
            settings.PublicBaseUrl = publicBaseUrl;
        }

        foreach (var (status, runId, review) in observations)
        {
            var link = $"[View this review in Antiphon](<https://antiphon.test/review-base/reviews/{runId:D}>)";
            var note = status == ReviewNodeStatus.Succeeded ? "1 findings were disproved by the verifier and dropped."
                : "_These findings were not verified: no verifier is configured._";
            string[] backlink = configured ? [link, ""] : [];
            review.Body.ShouldBe(Lines(["**Antiphon review**, round 1 (iteration 1)", "", ..backlink, "Findings:",
                "- **F1** major · \x60src/Lease.cs\x60: Inline <literal>", "", "Listed here only:",
                "- F2 nit · \x60src/Lease.cs:16\x60: Summary nit", "- F3 major · \x60src/Lease.cs:55\x60: Outside hunk", "",
                "1 more below this round's bar are kept in Antiphon.", "", note, ""]), status.ToString());
            var comment = review.Comments.ShouldHaveSingleItem();
            (comment.Path, comment.Line).ShouldBe((inline.File, inline.Line));
            var fence = new string('\x60', 3);
            comment.Body.ShouldBe(Lines(["**F1 · major** Inline <literal>", "", "Body for A", "", fence + "suggestion",
                "replacement", fence, "", "_Check SEED-0004_", "", ..backlink]), status.ToString());
        }
    }

    private async Task<(Guid RunId, ScriptedPr Pr, PublishOutput Output, List<ReviewFinding> Findings)> PostFixtureAsync(
        int number, WorkflowDefinition graph, IReadOnlyList<FindingCandidate> candidates, VerifyOutput? verify = null,
        ReviewNodeStatus status = ReviewNodeStatus.Succeeded, bool hasPr = true)
    {
        var pr = factory.Host.Open(number, "Publish fixture", "dana", BaseSha, HeadSha, Files, Diff);
        var id = await WalkPublishAsync(number, graph,
        [
            Producer("collate", ReviewNodeKind.Join, 1, new CollateOutput(candidates, [], [])),
            Producer("verify", ReviewNodeKind.Completion, 2,
                verify ?? new VerifyOutput(candidates.Select(c => c.Cid).ToList(), [], true), status),
        ], hasPr: hasPr);

        await using var reader = factory.Services.CreateAsyncScope();
        var db = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var publish = await db.ReviewNodeExecutions.AsNoTracking().SingleAsync(n => n.RunId == id && n.NodeId == "publish");
        publish.Status.ShouldBe(ReviewNodeStatus.Succeeded, publish.Summary);
        var output = ReviewWalk.Output<PublishOutput>(publish).ShouldNotBeNull();
        var findings = await db.ReviewFindings.AsNoTracking().Where(f => f.RunId == id).OrderBy(f => f.Key).ToListAsync();
        return (id, pr, output, findings);
    }

    private static ScriptedReviewReceipt Receipt(ScriptedPr pr, PublishOutput output)
    {
        var id = output.ReviewId.ShouldNotBeNull();
        return pr.Read(p =>
        {
            p.AcceptedReviews.Count.ShouldBe(1);
            p.AcceptedReviews.ShouldContainKey(id);
            return p.AcceptedReviews[id];
        });
    }

    private static string FixtureRunLink(Guid id) => $"[View this review in Antiphon](<https://antiphon.test/reviews/{id:D}>)";

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    private static WorkflowDefinition PublishGraph(IReadOnlyDictionary<string, object?> parameters)
    {
        var graph = ReviewWorkflowYaml.LoadDefault();
        return graph with
        {
            Nodes = graph.Nodes.Select(n => n.Id == "publish" ? n with { Params = parameters } : n).ToList(),
            Edges = graph.Edges.Select(e => e.From == "publish" ? e with { To = "await-fixes" } : e).ToList(),
        };
    }

    private static FindingCandidate PublishCandidate(string id, string title, int line) =>
        new(id, "fixture", "none", ReviewSeverity.Major, 0.99, "src/Lease.cs", line, null, true,
            $"anchor-{id}", title, $"Body for {id}", null, [], null);

    private static ReviewNodeExecution Producer(string id, ReviewNodeKind kind, int sequence, object output,
        ReviewNodeStatus status = ReviewNodeStatus.Succeeded) =>
        new()
        {
            Id = Guid.NewGuid(), NodeId = id, Kind = kind, Iteration = 1, Sequence = sequence, Status = status,
            OutputJson = ReviewWalk.ToJson(output), StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
        };

    private async Task<Guid> WalkPublishAsync(int number, WorkflowDefinition graph, List<ReviewNodeExecution> rows,
        bool hasPr = true, Action<ReviewRun>? arrange = null)
    {
        using var deadline = new CancellationTokenSource(StepTimeout);
        var ct = deadline.Token;
        var run = await SeedPublishAsync(number, graph, rows, hasPr, arrange, ct);
        var executor = factory.Services.GetServices<IHostedService>().OfType<ReviewWorkflowExecutor>().Single();
        await executor.RunPendingAsync(ct);

        // The hosted poll can win the claim. Read through a fresh scope until its walk is parked.
        await using var reader = factory.Services.CreateAsyncScope();
        var states = reader.ServiceProvider.GetRequiredService<AppDbContext>().ReviewRuns.AsNoTracking().Where(r => r.Id == run.Id);
        while (await states.Select(r => r.Status).SingleAsync(ct) is ReviewRunStatus.Queued or ReviewRunStatus.Running)
            await Task.Delay(25, ct);
        return run.Id;
    }

    private async Task<ReviewRun> SeedPublishAsync(int number, WorkflowDefinition graph, List<ReviewNodeExecution> rows,
        bool hasPr, Action<ReviewRun>? arrange, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        graph = graph with { Name = $"publish-fixture-{Guid.NewGuid():N}" };
        var revision = new ReviewWorkflowRevision { Id = Guid.NewGuid(), Name = graph.Name, Hash = graph.Hash(), CreatedAt = now };
        revision.GraphJson = System.Text.Json.JsonSerializer.Serialize(
            ReviewWorkflowService.ToDto(graph, revision.Id), ReviewWorkflowService.GraphJsonOptions);
        var subject = new ReviewSubject
        {
            Id = Guid.NewGuid(), Host = ReviewHost.GitHub, Repo = "acme/widgets", PrNumber = hasPr ? number : null, Title = "Publish fixture",
            AuthorLogin = "dana", OpenedByLogin = "dana", BaseRef = "main", HeadRef = $"fix/pr-{number}",
            BaseSha = BaseSha, HeadSha = HeadSha, CreatedAt = now,
        };
        var run = new ReviewRun
        {
            Id = Guid.NewGuid(), Subject = subject, Status = ReviewRunStatus.Queued, Stage = ReviewStage.Publish,
            WorkflowName = revision.Name, WorkflowRevisionId = revision.Id, WorkflowHash = revision.Hash, StartedAt = now, UpdatedAt = now,
            Iterations = [new() { Id = Guid.NewGuid(), Iteration = 1, Round = 1, Trigger = "opened", BaseSha = BaseSha, HeadSha = HeadSha, StartedAt = now }],
            Nodes = rows,
        };
        arrange?.Invoke(run);
        await using var seed = factory.Services.CreateAsyncScope();
        var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ReviewWorkflowRevisions.Add(revision);
        db.ReviewRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }
}
