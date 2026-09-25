using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0585 V-1..V-4. The checkpoint manifest is a standing rule, so it has to reach a fresh
/// delegate in every copy that carries it — the owner doc, the two orchestration copies and the
/// three stage bundles — and the doc's own schema table has to stay readable by the server's
/// selection validator (D-3 forward compatibility: the follow-up that lifts the Interim gate must
/// need no doc change). Static contract text: proof of what an agent is told, never that it obeyed.
/// </summary>
[Category("Unit")]
public sealed class CheckpointManifestDocumentationTests
{
    private const string DocRelativePath = "docs/testing-and-build.md";

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static string ReadRepoFile(params string[] relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Path.Combine(relative)));

    private static string ReadCollapsed(params string[] relative) => Collapse(ReadRepoFile(relative));

    [Test]
    public void the_checkpoint_phrases_are_pinned_in_every_copy()
    {
        var doc = ReadCollapsed("docs", "testing-and-build.md");
        foreach (var phrase in new[] { "### Checkpoint manifest (CARD-0585)", "CP-n", "run-checkpoint.ps1", "closed list", "EstimatedMinutes", "-MinExecuted" })
            doc.ShouldContain(phrase, Case.Insensitive, DocRelativePath);

        var loop = ReadCollapsed("docs", "orchestration-loop.md");
        foreach (var phrase in new[] { "checkpoints:", "### Checkpoints" })
            loop.ShouldContain(phrase, Case.Insensitive, "docs/orchestration-loop.md");

        var skill = ReadCollapsed(".claude", "skills", "antiphon-delegate", "SKILL.md");
        foreach (var phrase in new[] { "checkpoints:", "### Checkpoints" })
            skill.ShouldContain(phrase, Case.Insensitive, ".claude/skills/antiphon-delegate/SKILL.md");

        var agents = ReadCollapsed("AGENTS.md");
        foreach (var phrase in new[] { "### Checkpoints", "CARD-0585" })
            agents.ShouldContain(phrase, Case.Insensitive, "AGENTS.md");

        var code = ReadCollapsed("server", "Bundles", "stage-code.md");
        foreach (var phrase in new[] { "### Checkpoints", "closed list", "unlisted", "stub, not done" })
            code.ShouldContain(phrase, Case.Insensitive, "server/Bundles/stage-code.md");

        ReadCollapsed("server", "Bundles", "stage-test-design.md")
            .ShouldContain("| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |", Case.Insensitive,
                "server/Bundles/stage-test-design.md");

        var review = ReadCollapsed("server", "Bundles", "stage-review.md");
        foreach (var phrase in new[] { "CP-n lines", "cannot go red" })
            review.ShouldContain(phrase, Case.Insensitive, "server/Bundles/stage-review.md");
    }

    [Test]
    public void the_doc_section_is_a_selection_readable_table()
    {
        // The same validator the server runs over an Interim verificationSelection
        // (InterimVerificationPolicy.HasSelectionRows): a heading, a separator row, >= 2 table rows.
        InterimVerificationPolicy.HasSelectionRows(
            ReadRepoFile("docs", "testing-and-build.md"), "Checkpoint manifest (CARD-0585)")
            .ShouldBeTrue($"{DocRelativePath} section must hold a markdown table the selection validator accepts");
    }

    [Test]
    public void the_code_recipe_carries_the_checkpoints_pointer()
    {
        var skill = ReadCollapsed(".claude", "skills", "antiphon-delegate", "SKILL.md");
        skill.ShouldContain("checkpoints: <plan artifact path>@<full plan commit sha> section", Case.Insensitive);
        skill.ShouldContain("-ExpectAbout <cp-estimated-minutes-sum+authoring>", Case.Insensitive);

        var loop = ReadCollapsed("docs", "orchestration-loop.md");
        loop.ShouldContain("sum of its", Case.Insensitive);
        loop.ShouldContain("`EstimatedMinutes` column", Case.Insensitive);
    }

    /// <summary>CARD-0589 V-6: the owner doc carries the build-slot contract a delegate and an operator need.</summary>
    [Test]
    public void the_doc_has_the_build_slots_section()
    {
        var doc = ReadRepoFile("docs", "testing-and-build.md");
        var start = doc.IndexOf("### Build slots (CARD-0589)", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"{DocRelativePath} must have the \"### Build slots (CARD-0589)\" section");
        var next = doc.IndexOf("\n## ", start, StringComparison.Ordinal);
        var section = Collapse(next < 0 ? doc[start..] : doc[start..next]);
        foreach (var phrase in new[]
                 {
                     "SessionRunner:BuildSlots", "ANTIPHON_BUILD_SLOTS_URL", "scripts/build-slot.ps1", "BUILD SLOT waiting",
                     "BUILD SLOT unleased", "exit 4", "-NoSlot", "Directory.Build.rsp", "-nodeReuse:false", "MSBuildNodeCount",
                 })
            section.ShouldContain(phrase, Case.Sensitive, $"{DocRelativePath} Build slots section");
    }

    /// <summary>CARD-0589 V-6: Code reports each checkpoint row's slot outcome and wait.</summary>
    [Test]
    public void the_code_bundle_reports_slot_per_row()
    {
        var code = ReadCollapsed("server", "Bundles", "stage-code.md");
        var checkpoints = code[code.IndexOf("CHECKPOINTS:", StringComparison.Ordinal)..];
        checkpoints = checkpoints[..checkpoints.IndexOf("ROUND:", StringComparison.Ordinal)];
        checkpoints.ShouldContain("slot=", Case.Sensitive, "the CHECKPOINTS sentence of server/Bundles/stage-code.md");
        checkpoints.ShouldContain("waited=", Case.Sensitive, "the CHECKPOINTS sentence of server/Bundles/stage-code.md");
    }

    [Test]
    public void the_review_bundle_names_each_checkpoint_defect()
    {
        var review = ReadCollapsed("server", "Bundles", "stage-review.md");
        foreach (var phrase in new[]
                 {
                     "missing row", "zero count", "unlisted build/test run without a reason", "cannot go red",
                 })
            review.ShouldContain(phrase, Case.Insensitive, "server/Bundles/stage-review.md");
    }
}
