using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

[Category("Unit")]
public class ReviewPublishSummaryTests
{
    [Test]
    public void C2110_S1_unit_Binding_has_no_previous_iteration_fallback()
    {
        var prior = Output("categorise", 1, new CategorisationOutcome("stale-category", 1, 1, 1, 1, 1, 1, []));
        prior.Iteration = 1;
        var current = Output("categorise", 2, new CategorisationOutcome("current-category", 2, 2, 2, 2, 2, 2, []));
        var publish = Output("publish", 3, new { });
        var walk = Walk(prior);
        new ReviewInputResolver(walk, publish).Latest("categorise").ShouldBeNull();
        walk.Run.Nodes.Add(current);
        new ReviewInputResolver(walk, publish).Latest("categorise").ShouldBeSameAs(current);
    }

    [Test]
    public void C2110_S1_unit_Binding_cannot_read_fan_out_children()
    {
        var parent = Output("categorise", 1, new { marker = "parent" });
        var child = Output("categorise", 2, new { marker = "child" });
        child.ItemKey = "fan-out-owner";
        var consumer = Output("publish", 3, new { });
        new ReviewInputResolver(Walk(parent, child), consumer).Latest("categorise").ShouldBeSameAs(parent);
    }

    [Test]
    public void C2110_S1_unit_Binding_excludes_rows_at_or_after_consumer()
    {
        var prior = Output("select", 1, new { marker = "prior" });
        var at = Output("select", 2, new { marker = "at" });
        var later = Output("select", 3, new { marker = "future" });
        var consumer = Output("publish", 2, new { });
        new ReviewInputResolver(Walk(prior, at, later), consumer).Latest("select").ShouldBeSameAs(prior);
    }

    [Test]
    public void C2110_S1_unit_Latest_failure_shadows_an_older_usable_value()
    {
        var old = Output("select", 1, Selection("OLD"));
        var latest = Output("select", 2, Selection("LATEST"));
        var consumer = Output("publish", 3, new { });
        var resolver = new ReviewInputResolver(Walk(old, latest), consumer);

        latest.Status = ReviewNodeStatus.Failed;
        var failed = ReviewInputResolver.Read<SelectOutput>(resolver.Latest("select"));
        latest.Status = ReviewNodeStatus.FailedContinued;
        var degraded = ReviewInputResolver.Read<SelectOutput>(resolver.Latest("select"));
        latest.OutputJson = null;
        var missing = ReviewInputResolver.Read<SelectOutput>(resolver.Latest("select"));

        degraded.ShouldNotBeNull().Selected.ShouldBe(["LATEST"]);
        failed.ShouldBeNull("a failed row shadows an older success");
        missing.ShouldBeNull("a row without output shadows an older success");
    }

    [Test]
    public void C2110_S1_unit_Publish_refuses_a_recollate_after_verification()
    {
        var original = Output("collate", 1, Collate("original title"));
        var verify = Output("verify", 2, new VerifyOutput(["A"], [], true));
        var recollate = Output("collate", 3, Collate("different title, identical ID"));
        var consumer = Output("publish", 4, new { });
        var walk = Walk(original, verify, recollate);
        ReviewInputResolver.Read<CollateOutput>(original)!.Candidates.Single().Cid.ShouldBe("A");
        ReviewInputResolver.Read<CollateOutput>(recollate)!.Candidates.Single().Cid.ShouldBe("A");
        new ReviewInputResolver(walk, verify).LatestOf("CollateFindings").ShouldBeSameAs(original);
        new ReviewInputResolver(walk, consumer).Latest("collate").ShouldBeSameAs(recollate);

        ReviewPublishInputs.TryResolve(walk, consumer, Binding, out _, out var failure).ShouldBeFalse();
        failure.ShouldBe("The declared collate result was not the input to the declared verification.");
    }

    [Test]
    public void C2110_S1_unit_Publish_refuses_kept_ids_outside_verified_collate()
    {
        var collate = Output("collate", 1, Collate("original"));
        var verify = Output("verify", 2, new VerifyOutput(["A", "Z"], [], true));
        var consumer = Output("publish", 3, new { });
        var walk = Walk(collate, verify);
        var rejected = ReviewPublishInputs.TryResolve(walk, consumer, Binding, out _, out var failure);
        verify.OutputJson = ReviewWalk.ToJson(new VerifyOutput(["A"], [], true));
        var accepted = ReviewPublishInputs.TryResolve(walk, consumer, Binding, out var inputs, out _);

        accepted.ShouldBeTrue();
        inputs!.Verify.Kept.ShouldBe(["A"]);
        rejected.ShouldBeFalse();
        failure.ShouldBe("The declared verification kept IDs outside its collate result.");
    }

    [Test]
    public void C2110_S1_unit_Explicit_publish_requires_usable_typed_artifacts()
    {
        var collate = Output("collate", 1, Collate("original"));
        var verify = Output("verify", 2, new VerifyOutput(["A"], [], false));
        var consumer = Output("publish", 3, new { });
        var missing = Resolve(Walk(collate));
        var good = verify.OutputJson;
        verify.OutputJson = "{";
        var malformed = Resolve(Walk(collate, verify));
        verify.OutputJson = "{}";
        var wrongShape = Resolve(Walk(collate, verify));
        verify.OutputJson = good;
        verify.Status = ReviewNodeStatus.Failed;
        var failed = Resolve(Walk(collate, verify));
        verify.Status = ReviewNodeStatus.Skipped;
        var skipped = Resolve(Walk(collate, verify));
        verify.Status = ReviewNodeStatus.FailedContinued;
        var degraded = Resolve(Walk(collate, verify));
        var badCollate = Resolve(Walk(Output("collate", 1, new { candidates = new object?[] { null } }), verify));

        skipped.ShouldBe((true, (string?)null));
        degraded.ShouldBe((true, (string?)null));
        foreach (var invalid in new[] { missing, malformed, wrongShape, failed })
            invalid.ShouldBe((false, "Publish requires a usable declared verify result."));
        badCollate.ShouldBe((false, "Publish requires a usable declared collate result."));
        var legacy = Walk(collate);
        ReviewPublishConfiguration.TryRead(legacy.Workflow, legacy.Workflow.Node("publish")!, out var noInputs).ShouldBeTrue();
        noInputs.ShouldBeNull();

        (bool Accepted, string? Failure) Resolve(ReviewWalk walk) =>
            (ReviewPublishInputs.TryResolve(walk, consumer, Binding, out _, out var failure), failure);
    }

    [Test]
    public void C2110_S1_unit_Custom_body_escapes_dynamic_values_and_never_reparses_them()
    {
        var walk = Walk();
        walk.Run.Subject.Title = "Title <b>raw</b>\n@someone #123 https://example.test {{round}}";
        walk.Run.Subject.Url = "UNBOUND_SUBJECT_URL";
        var finding = new ReviewFinding
        {
            Key = "F1", Severity = ReviewSeverity.Major, FilePath = "path\x60\n<x>", Title = "title [link](https://example.test)",
            Body = "UNBOUND_FINDING_BODY",
        };
        var verify = new VerifyOutput(["A"], [new("g", "none", "f", "a", "UNBOUND_VERIFIER_DETAIL")], true);
        var body = Render(walk, "## {{run.title}}\n\n{{defaultBody}}\n\n{{verification}}", [finding], verify);

        body.ShouldStartWith("## Title &#60;b&#62;raw&#60;&#47;b&#62; &#64;&#8203;someone &#35;&#8203;123 ");
        body.ShouldContain("https&#58;&#8203;&#47;&#47;example&#46;&#8203;test &#123;&#123;round&#125;&#125;");
        body.ShouldContain("path&#96; &#60;x&#62;");
        body.ShouldContain("1 findings were disproved by the verifier and dropped.");
        foreach (var raw in new[] { "<b>", "@someone", " #123 ", "https://example.test", "{{round}}", "UNBOUND_" })
            body.ShouldNotContain(raw);
        walk.Warnings.ShouldBeEmpty();
    }

    [Test]
    public void C2110_S1_unit_Oversized_custom_render_falls_back_without_a_partial_body()
    {
        var walk = Walk();
        var findings = new[] { new ReviewFinding { Key = "F1", Severity = ReviewSeverity.Major, FilePath = "path", Title = "kept" } };
        const string template = "{{run.title}}\n{{findings}}";
        var baseline = Render(walk, template, findings, null);
        var available = ReviewSummaryRenderer.MaxCustomBodyBytes - System.Text.Encoding.UTF8.GetByteCount(baseline);
        walk.Run.Subject.Title = new string('X', available);
        var atLimit = Render(walk, template, findings, null);
        walk.Run.Subject.Title += "Y";
        var overLimit = Render(walk, template, findings, null);

        System.Text.Encoding.UTF8.GetByteCount(atLimit).ShouldBe(ReviewSummaryRenderer.MaxCustomBodyBytes);
        atLimit.ShouldStartWith("XXXXX");
        overLimit.ShouldBe("**Antiphon review**, round 1 (iteration 2)\n\nFindings:\n- **F1** major · path: kept\n");
        walk.Warnings.ShouldBe(["Invalid or oversized Publish summary template; used the default summary."]);
    }

    [Test]
    public void C2110_S1_unit_Authored_fallback_escapes_and_truncates_complete_records()
    {
        var walk = Walk();
        var hostile = new ReviewFinding
        {
            Key = "F1", Severity = ReviewSeverity.Major, FilePath = "src/`code`.cs",
            Title = "[title](https://example.test)\r\n@someone\n/approve\n<!-- hidden -->",
        };
        var oversized = new ReviewFinding
        {
            Key = "F2", Severity = ReviewSeverity.Major, FilePath = new string('`', 70000), Title = "OMITTED RECORD",
        };

        var body = Render(walk, "{{unknown}}", [hostile, oversized], null);

        body.ShouldStartWith("**Antiphon review**, round 1 (iteration 2)\n\nFindings:\n");
        body.ShouldContain("src&#47;&#96;code&#96;&#46;&#8203;cs: &#91;title&#93;");
        body.ShouldContain("&#64;&#8203;someone &#47;approve &#60;&#33;&#45;&#45; hidden &#45;&#45;&#62;\n");
        body.ShouldNotContain("@someone");
        body.ShouldNotContain("\n/approve");
        body.ShouldNotContain("<!--");
        body.ShouldNotContain("**F2**");
        body.ShouldNotContain("OMITTED RECORD");
        body.ShouldEndWith("_Some findings were omitted from this summary due to the size limit. View all findings in Antiphon._\n");
        System.Text.Encoding.UTF8.GetByteCount(body).ShouldBeLessThanOrEqualTo(ReviewSummaryRenderer.MaxCustomBodyBytes);
    }

    [Test]
    public void C2110_S1_unit_Custom_summary_uses_the_same_LF_bytes_for_CRLF_and_LF_templates()
    {
        var finding = new ReviewFinding { Key = "F1", Severity = ReviewSeverity.Major, FilePath = "path", Title = "Visible finding" };
        const string template = "Custom heading\n\n{{findings}}\n\n{{blastRadius}}\n";
        var lf = Render(Walk(), template, [finding], null);
        var crlf = Render(Walk(), template.ReplaceLineEndings("\r\n"), [finding], null);

        System.Text.Encoding.UTF8.GetBytes(crlf).ShouldBe(System.Text.Encoding.UTF8.GetBytes(lf));
        lf.ShouldStartWith("Custom heading\n\nFindings:\n- **F1** major · path: Visible finding\n\n**Blast radius across all iterations**\n");
        lf.ShouldNotContain("\r");
    }

    private static readonly ReviewPublishConfiguration Binding = new("collate", "verify", "categorise", "select");

    private static ReviewWalk Walk(params ReviewNodeExecution[] rows) =>
        new(null!, null!, new ReviewRun { Round = 1, Subject = new ReviewSubject(), Nodes = [.. rows] },
            new ReviewIteration { Iteration = 2 }, ReviewWorkflowYaml.LoadDefault(), TimeProvider.System);

    private static ReviewNodeExecution Output(string nodeId, int sequence, object output) =>
        new()
        {
            Id = Guid.NewGuid(), NodeId = nodeId, Sequence = sequence, Iteration = 2,
            Status = ReviewNodeStatus.Succeeded, OutputJson = ReviewWalk.ToJson(output),
        };

    private static CollateOutput Collate(string title) =>
        new([new("A", "group", "none", ReviewSeverity.Major, 0.99, "path", 1, null, true, "anchor", title, "body", null, [], null)], [], []);

    private static SelectOutput Selection(string id) =>
        new("sources", [], [new(id, $"Title {id}", "category", ReviewSeverity.Major, 1, true, "trigger")]);

    private static string Render(ReviewWalk walk, string? template, IReadOnlyList<ReviewFinding> findings, VerifyOutput? verify)
    {
        var node = walk.Workflow.Node("publish")!;
        if (template is not null)
            node = node with { Params = new Dictionary<string, object?> { ["summaryTemplate"] = template } };
        return ReviewSummaryRenderer.Render(walk, node, null, null, findings, [], 0, verify, false).Body;
    }
}
