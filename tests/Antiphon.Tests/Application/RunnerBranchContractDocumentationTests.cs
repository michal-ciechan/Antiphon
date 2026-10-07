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
    public void C1076_remote_prep_push_contract_is_documented()
    {
        foreach (var relative in new[] { "docs/orchestration-loop.md", "docs/ops-http.md" })
        {
            var text = Read(relative);
            text.ShouldContain("RemotePrepPushBudgetMinutes");
            text.ShouldContain("remote-prep-push");
            text.ShouldContain("repositoryLease");
            text.ShouldContain("remotePrep");
            text.ShouldContain("does not track origin");
            text.ShouldContain("BehindTaskId");
            text.ShouldContain("enqueue-time snapshot");
            text.ShouldContain("CARD-1093");
        }
    }

    [Test]
    public void Operations_document_the_new_wire_and_removal_guard()
    {
        var text = Read("docs/ops-http.md");
        text.ShouldContain("WorkspacePublish = 32");
        text.ShouldContain("PublishedSha");
        text.ShouldContain("phone_home_unpublished_work");
    }

    [Test]
    public void C1082_settlement_sync_debt_is_documented()
    {
        var loop = Read("docs/orchestration-loop.md");
        foreach (var sentence in LoopSentences)
            loop.ShouldContain(sentence);
        var runtime = Read("docs/session-runtime-invariants.md");
        foreach (var sentence in RuntimeSentences)
            runtime.ShouldContain(sentence);
        var ops = Read("docs/ops-http.md");
        foreach (var sentence in OpsSentences)
            ops.ShouldContain(sentence);
        var api = Read("docs/antiphon-api.md");
        foreach (var sentence in ApiSentences)
            api.ShouldContain(sentence);
        var script = Read("scripts/delegate.ps1");
        script.ShouldContain("Runner sync: Pending $($r.reason); origin=$($r.observedSha)");
        script.ShouldContain("Desktop sync debt:");
        script.ShouldContain(
            "Pending evidence without a debt row means the task stayed Blocked and a reply is required.");
        Read("server/Application/Services/SettlementSyncDebtAttention.cs").ShouldContain(
            "The task attempt, baseline or worktree changed; this debt will not be fast-forwarded.");
        Read("server/Application/Services/AttentionService.cs").ShouldContain(
            "SettlementSyncDebtAttention.Build");
        Read("server/Application/Services/AgentTaskService.cs").ShouldContain("new TaskSyncDebtDto");
    }

    private static readonly string[] LoopSentences =
    [
        "Delegation:RunnerSyncDebtOnSettlement defaults to true.",
        "runner_sync_lease_busy",
        "Pending is never Confirmed.",
        "A debt row is created only for the lease-contention case of a complete report that settles Succeeded or Failed.",
        "Dirty, diverged, and unfetched Code stay Blocked with no debt row.",
        "The policy accepts only the task's own owned ref as the observed tip, and a Code report with no progress evidence under Pending stays Blocked.",
        "A Blocked lease-busy settlement's warning says Runner sync unavailable and then reply, never Runner sync pending; its evidence still records Pending.",
        "Pending evidence without a debt row means the task stayed Blocked and a reply is required.",
        "desktop-sync=pending",
        "synced later",
        "No reply is needed",
        "the report's next= is kept",
        "-Land uses the pushed branch the same way it does for a synchronized owner.",
        "backoff is 1, 2, 4, then 5 minutes",
        "An empty debt table is 1 statement per tick.",
        "A synchronized settle is 30 statements and debtSql 0.",
        "A pending-review retry is 40 statements and debtSql 1.",
        "The kill switch off is 31 statements and debtSql 0.",
        "desktop sync still pending (lease contention)",
        "The task attempt, baseline or worktree changed; this debt will not be fast-forwarded.",
        "Origin moved past the recorded source; the sweep does not follow it.",
        "settlement-sync-debt:",
        "syncDebt",
        "RunnerSyncDebtAttentionMinutes defaults to 30.",
        "The desktop checkout is dirty. This debt stays Held.",
        "Review that exact pushed S in a Worktree Review with -StartRef",
        "A Held debt is re-checked every 60 minutes for its worktree registration only: it ends Superseded only when that one active Complete retirement is the current registration, has recorded directory and registration removal at or before the re-check, and the recorded path is absent. An earlier incarnation, a recreated registration, or any doubt keeps the row Held. The re-check never runs Git or fast-forwards.",
    ];

    private static readonly string[] RuntimeSentences =
    [
        "Delegation:RunnerSyncDebtOnSettlement defaults to true.",
        "Pending is never Confirmed.",
        "desktop-sync=pending",
        "synced later",
        "Dirty, diverged, and unfetched Code stay Blocked",
        "then reply",
        "backoff is 1, 2, 4, then 5 minutes",
        "-Land does not read RemoteSync.",
        "syncDebt",
        "A Held debt ends Superseded only after one completed retirement of the current registration has removed that worktree path.",
    ];

    private static readonly string[] OpsSentences =
    [
        "syncDebt",
        "Pending is never Confirmed.",
        "settlement-sync-debt:",
        "desktop sync still pending (lease contention)",
        "Delegation:RunnerSyncDebtOnSettlement defaults to true.",
        "runner_sync_lease_busy",
        "synced later",
        "Pending evidence without a debt row means the task stayed Blocked and a reply is required.",
        "RunnerSyncDebtAttentionMinutes defaults to 30.",
        "Held debt is re-checked hourly and ends Superseded only after a completed retirement of the current registration has removed its path, which clears its attention row.",
    ];

    private static readonly string[] ApiSentences =
    [
        "syncDebt",
        "Pending is never Confirmed.",
        "settlement-sync-debt:",
        "Delegation:RunnerSyncDebtOnSettlement defaults to true.",
        "runner_sync_lease_busy",
        "synced later",
        "RunnerSyncDebtAttentionMinutes defaults to 30.",
        "desktop-sync=pending",
    ];

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
