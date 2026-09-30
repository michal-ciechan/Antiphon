using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RunnerBranchContractDocumentationTests
{
    [Test]
    public void Orchestration_rules_name_the_branch_guard_and_all_recoveries()
    {
        var text = Read("docs/orchestration-loop.md");
        text.ShouldContain("never ask a runner-bound Worktree");
        text.ShouldContain("runner_mirror_diverged");
        text.ShouldContain("runner_mirror_publish_failed");
        text.ShouldContain("runner_mirror_unavailable");
        text.ShouldContain("runner_sync_diverged");
        text.ShouldContain("-RecoverReviewedSource");
    }

    [Test]
    public void Runtime_owner_keeps_unpublished_runner_commits()
    {
        Read("docs/session-runtime-invariants.md").ShouldContain("never removed without `Force`");
        Read(".claude/skills/antiphon-orchestrator/SKILL.md").ShouldContain("never ask the delegate to rebase");
    }

    [Test]
    public void Operations_document_the_new_wire_and_removal_guard()
    {
        var text = Read("docs/ops-http.md");
        text.ShouldContain("WorkspacePublish = 32");
        text.ShouldContain("PublishedSha");
        text.ShouldContain("phone_home_unpublished_work");
    }

    private static string Read(string relative)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "docs", "orchestration-loop.md")))
                return File.ReadAllText(Path.Combine(directory, relative));
            directory = Path.GetDirectoryName(directory)!;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
