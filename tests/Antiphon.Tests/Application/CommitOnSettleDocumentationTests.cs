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
}
