using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1451. The S2 repair amendment is folded into the CARD-1432 plan, V-9 cites only the
/// retention test, CP-15 records the review's executed count, the IsMutating doc comment sits
/// on IsMutating, and the D2 journal poll fails by the named assertion.
/// </summary>
[Category("Unit")]
public class Card1451PlanFoldTests
{
    [Test]
    public async Task Folded_plan_states_retained_operation_rule_and_v9_cites_only_the_retention_test()
    {
        var plan = await File.ReadAllTextAsync(PlanPath);
        plan.ShouldContain("of every Refused landing stay");
        var v9 = TableRow(plan, "V-9");
        v9.ShouldContain("C1432_unprotected_task_keeps_latest_and_refused_operation_pins");
        v9.ShouldNotContain("RR_V5_SettledRefusalIsNotAutomaticallyQueued");
        v9.ShouldNotContain("C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection");
        File.Exists(AmendmentPath).ShouldBeFalse("CARD-1451 folds the amendment into the plan");
    }

    [Test]
    public async Task Cp15_expect_records_the_count_the_review_ran()
    {
        var plan = await File.ReadAllTextAsync(PlanPath);
        var cp15 = TableRow(plan, "CP-15");
        cp15.ShouldContain("55 passed");
        cp15.ShouldNotContain("43 passed");
    }

    [Test]
    public async Task IsMutating_doc_comment_sits_on_IsMutating_and_CreatedNoChild_keeps_its_own()
    {
        var source = await File.ReadAllTextAsync(LandingGitPath);
        var mutating = DocBlockImmediatelyAbove(source, "internal static bool IsMutating(");
        mutating.ShouldContain("may write the repository");
        mutating.ShouldNotContain("created no child");
        var noChild = DocBlockImmediatelyAbove(source, "internal static bool CreatedNoChild(");
        noChild.ShouldContain("created no child");
        noChild.ShouldNotContain("may write the repository");
    }

    [Test]
    public async Task D2_journal_timeout_reaches_the_named_assertion()
    {
        var source = await File.ReadAllTextAsync(LandingGitTestsPath);
        var body = MethodBody(source, "C1432_UpdateRefsBatchRunsUnderTheChildJournal");
        body.ShouldContain("a fencing record exists while the batch runs");
        body.ShouldNotContain("CancelAfter");
        body.ShouldNotContain("poll.Token");
    }

    private static string TableRow(string plan, string id)
    {
        foreach (var line in plan.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("| " + id + " |", StringComparison.Ordinal))
                return line;
        }

        return "";
    }

    private static string DocBlockImmediatelyAbove(string source, string signature)
    {
        var index = source.IndexOf(signature, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, signature);
        var lines = source[..index].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var i = lines.Length - 1;
        while (i >= 0 && lines[i].Trim().Length == 0)
            i--;
        var block = new List<string>();
        while (i >= 0 && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
        {
            block.Add(lines[i]);
            i--;
        }

        block.Reverse();
        return string.Join('\n', block);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, signature);
        var next = source.IndexOf("\n    [Test]", start + signature.Length, StringComparison.Ordinal);
        if (next < 0)
            next = source.Length;
        return source[start..next];
    }

    private static string PlanPath => Path.Combine(RepoRoot, "docs", "superpowers", "plans",
        "2026-10-22-card-1432-repository-maintenance-plan.md");

    private static string AmendmentPath => Path.Combine(RepoRoot, "docs", "superpowers", "plans",
        "2026-10-22-card-1432-s2-repair-amendment.md");

    private static string LandingGitPath => Path.Combine(RepoRoot, "server", "Infrastructure", "Git", "LandingGit.cs");

    private static string LandingGitTestsPath => Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "Infrastructure",
        "LandingGitTests.cs");

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }
}
