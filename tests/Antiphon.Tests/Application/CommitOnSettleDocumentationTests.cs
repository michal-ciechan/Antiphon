using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class CommitOnSettleDocumentationTests
{
    [Test]
    public void Docs_name_the_flag_the_endpoint_and_the_policy()
    {
        var root = DelegateScriptRunner.RepoRoot;
        File.ReadAllText(Path.Combine(root, "AGENTS.md")).ShouldContain("-NoCommit");
        var skill = File.ReadAllText(Path.Combine(root, ".claude", "skills", "antiphon-delegate", "SKILL.md"));
        skill.ShouldContain("-NoCommit");
        skill.ShouldContain("Delegation:CommitOnSettle");
        var api = File.ReadAllText(Path.Combine(root, "docs", "antiphon-api.md"));
        api.ShouldContain("POST   /api/agent-tasks/{id}/commit");
        api.ShouldContain("commitOnSettle");
        File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"))
            .ShouldContain("Commit on settle");
    }

    // CARD-0527 A-16: the loop doc must name the two-leg recipe, every degraded header the hook
    // can now return, and the Worktree receipt-pending detail.
    [Test]
    public void Docs_name_the_two_leg_recovery_search_and_every_degraded_outcome()
    {
        var loop = File.ReadAllText(Path.Combine(
            DelegateScriptRunner.RepoRoot, "docs", "orchestration-loop.md"));
        loop.ShouldContain("never `--reflog`");
        loop.ShouldContain("`log --all …`");
        loop.ShouldContain("`log --walk-reflogs HEAD …`");
        loop.ShouldContain("uncommitted:N (history search unavailable)");
        loop.ShouldContain("uncommitted:N (repository inspection unavailable)");
        loop.ShouldContain("commit refused: status inspection unavailable");
        loop.ShouldContain("no commit needed (status inspection unavailable)");
        loop.ShouldContain("gated commit receipt pending (operation <id>)");
    }
}
