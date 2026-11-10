using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
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

    /// <summary>CARD-1136 / F5 V-16: shell operators end the wrapper's lease before a second driver.</summary>
    [Test]
    public void the_chained_operator_pitfall_is_documented()
    {
        var doc = ReadRepoFile("docs", "testing-and-build.md");
        var start = doc.IndexOf("### Build slots (CARD-0589)", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var next = doc.IndexOf("\n## ", start, StringComparison.Ordinal);
        var section = Collapse(next < 0 ? doc[start..] : doc[start..next]);
        section.ShouldContain("Never chain a second driver after the gated command with a shell operator: "
            + "the lease ends when build-slot.ps1 returns, so `-- dotnet build ... && dotnet exec ...` "
            + "runs the exec unleased and Review flags it.", Case.Sensitive, "CARD-1136 V-16");
    }

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
        // (InterimVerificationPolicy.HasSelectionRows): the section's first contiguous
        // table has a separator and at least two rows. A later table does not count.
        InterimVerificationPolicy.HasSelectionRows(
            ReadRepoFile("docs", "testing-and-build.md"), "Checkpoint manifest (CARD-0585)")
            .ShouldBeTrue($"{DocRelativePath} section must hold a markdown table the selection validator accepts");
    }

    /// <summary>
    /// CARD-0592: the schema table is the selection. Deleting its separator must fail even
    /// though the worked-example table later in the same section still has a separator and two rows.
    /// </summary>
    [Test]
    public void a_deleted_schema_separator_is_not_rescued_by_the_worked_example()
    {
        const string section = "Checkpoint manifest (CARD-0585)";
        var doc = ReadRepoFile("docs", "testing-and-build.md");
        var lines = doc.ReplaceLineEndings("\n").Split('\n');
        var headerAt = Array.FindIndex(lines, line => line.Trim() == "| Column | Meaning |");
        headerAt.ShouldBeGreaterThanOrEqualTo(0, "schema header");
        lines[headerAt + 1].Trim().ShouldBe("|---|---|", "schema separator follows the header");
        var exampleAt = Array.FindIndex(lines, headerAt + 2, line => line.Trim().StartsWith("|---|---|---|", StringComparison.Ordinal));
        exampleAt.ShouldBeGreaterThan(headerAt, "worked-example separator stays in the section");
        var mutated = string.Join('\n', lines.Where((_, index) => index != headerAt + 1));
        const string joined = "| Column | Meaning |\n| `CP` |";
        mutated.ShouldContain(joined, Case.Sensitive, "the schema body now follows the header with no separator");
        mutated.ShouldContain(lines[exampleAt], Case.Sensitive, "the worked-example separator stays");

        InterimVerificationPolicy.HasSelectionRows(mutated, section)
            .ShouldBeFalse("a later worked-example table must not satisfy a schema table with no separator");
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

    /// <summary>CARD-0723 V-18: the checkpoint tool is named in every copy a delegate reads.</summary>
    [Test]
    public void the_checkpoint_tool_is_pinned_in_every_copy()
    {
        var doc = ReadCollapsed("docs", "testing-and-build.md");
        foreach (var phrase in new[] { "### Checkpoint runner tool (CARD-0723)", "tools/Antiphon.Checkpoints", "wait", "exit 75" })
            doc.ShouldContain(phrase, Case.Insensitive, DocRelativePath);

        ReadCollapsed("AGENTS.md").ShouldContain("CARD-0723", Case.Insensitive, "AGENTS.md");
        ReadCollapsed(".claude", "skills", "antiphon-delegate", "SKILL.md")
            .ShouldContain("Antiphon.Checkpoints", Case.Insensitive, ".claude/skills/antiphon-delegate/SKILL.md");
        ReadCollapsed("docs", "orchestration-loop.md")
            .ShouldContain("Antiphon.Checkpoints", Case.Insensitive, "docs/orchestration-loop.md");
        ReadCollapsed("server", "Bundles", "stage-code.md")
            .ShouldContain("checkpoint tool", Case.Insensitive, "server/Bundles/stage-code.md");
        ReadCollapsed("server", "Bundles", "stage-review.md")
            .ShouldContain("checkpoint-tool run", Case.Insensitive, "server/Bundles/stage-review.md");
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

    /// <summary>
    /// CARD-0620: <c>Min</c> is the executed-test floor and <c>EstimatedMinutes</c> is the time
    /// budget. The schema rows stay mutually exclusive, and the owner doc's sample table imports
    /// those two cells into different fields.
    /// </summary>
    [Test]
    public void min_count_and_estimated_minutes_stay_distinct_columns()
    {
        const string sampleHeader = "| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |";
        var doc = ReadRepoFile("docs", "testing-and-build.md");
        var lines = doc.ReplaceLineEndings("\n").Split('\n');

        var min = SchemaRow(lines, "Min");
        min.ShouldContain("a floor on **TUnit executed test results**, and nothing else", Case.Sensitive, "Min schema");
        min.ShouldNotContain("minute", Case.Insensitive, "Min is a count");

        var estimate = SchemaRow(lines, "EstimatedMinutes");
        estimate.ShouldContain("Estimated wall-clock **minutes**", Case.Sensitive, "EstimatedMinutes schema");
        estimate.ShouldNotContain("executed test", Case.Insensitive, "EstimatedMinutes is a time");
        estimate.ShouldNotContain("MinExecuted", Case.Sensitive, "EstimatedMinutes is a time");

        var collapsed = Collapse(doc);
        collapsed.ShouldContain(
            "`Min` is a count and `EstimatedMinutes` is a time; neither is derived from the other, and a row carries both.",
            Case.Sensitive,
            DocRelativePath);
        collapsed.ShouldContain("`Min` on CP-1 is **3**", Case.Sensitive, DocRelativePath);
        collapsed.ShouldContain("not 9, which is how many minutes the row is budgeted", Case.Sensitive, DocRelativePath);
        collapsed.ShouldContain("Cost's ordinary floor for this pair is 9 + 2 = 11 minutes.", Case.Sensitive, DocRelativePath);
        collapsed.ShouldContain("a time, never the `Min` counts", Case.Sensitive, DocRelativePath);

        lines.Count(line => line.Trim() == sampleHeader).ShouldBe(1, "one sample checkpoint table");
        var headerAt = Array.FindIndex(lines, line => line.Trim() == sampleHeader);
        var table = new List<string>();
        for (var i = headerAt; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
            table.Add(lines[i]);
        table.Count.ShouldBe(4, "header, separator, CP-1, CP-2");

        var imported = PlanTableImporter.ImportMarkdown("### Checkpoints\n\n" + string.Join('\n', table) + "\n");
        imported.ExitCode.ShouldBe(0, imported.Error);
        var manifest = imported.Manifest.ShouldNotBeNull();
        var cp1 = manifest.Checkpoints.Single(row => row.Id == "CP-1");
        cp1.MinExecuted.ShouldBe(3, "Min is the executed-test floor");
        cp1.EstimatedMinutes.ShouldBe(9, "EstimatedMinutes is the time budget");
        var cp2 = manifest.Checkpoints.Single(row => row.Id == "CP-2");
        cp2.MinExecuted.ShouldBeNull("a non-TUnit row has no execution floor");
        cp2.EstimatedMinutes.ShouldBe(2, "EstimatedMinutes stays the time budget when Min is n/a");

        var bundle = ReadRepoFile("server", "Bundles", "stage-test-design.md");
        bundle.ShouldContain(sampleHeader, Case.Sensitive, "server/Bundles/stage-test-design.md");
        bundle.ShouldContain("Min=-MinExecuted. EstimatedMinutes target 2x p95.", Case.Sensitive, "server/Bundles/stage-test-design.md");

        ReadCollapsed("docs", "orchestration-loop.md").ShouldContain(
            "`EstimatedMinutes` column (a time budget, not the `Min` execution counts)",
            Case.Sensitive,
            "docs/orchestration-loop.md");
    }

    private static string SchemaRow(string[] lines, string column)
    {
        var prefix = "| `" + column + "` |";
        var matches = lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        matches.Length.ShouldBe(1, column + " schema row");
        return matches[0];
    }
}
