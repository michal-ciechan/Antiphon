using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
        var loop = Read("docs/orchestration-loop.md");
        foreach (var text in new[] { loop, Read("docs/ops-http.md") })
        {
            text.ShouldContain(nameof(DelegationSettings.RemotePrepPushBudgetMinutes));
            text.ShouldContain(RepositoryChildPurposes.RemotePrepPush);
            text.ShouldContain(AgentTaskPipelineStatusService.QueueReasonRepositoryLease);
            text.ShouldContain(AgentTaskPipelineStatusService.QueueReasonRemotePrep);
            text.ShouldContain("does not track origin");
            text.ShouldContain(nameof(RemotePrepProgress.BehindTaskId));
            text.ShouldContain("enqueue-time snapshot");
            text.ShouldContain("CARD-1093");
        }

        loop.ShouldContain(AgentTaskPipelineStatusService.QueueReasonHostBudget);
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

    /// <summary>
    /// CARD-1083 V-1. Reply on a live Blocked task is progress, not seat release, and a disabled
    /// park supplies no deadline. Expected sentences are literals, not quotations of the plan file.
    /// </summary>
    [Test]
    public void C1083_OwnersKeepReplyAndReleaseDistinct()
    {
        const string reply =
            "An accepted Reply changes a live Blocked task to Working; it does not itself free the runner seat. "
            + "Use -Continue only for a Blocked question with standing authority; -Refine returns 409 on Blocked. "
            + "While parking is disabled, do not assume an automatic release deadline.";
        const string timing =
            "Do not infer a release deadline from the 120-second idle proof, the 120-second reclaim interval, "
            + "or the 600-second Held backoff. For a Blocked input wait, parking supplies no automatic release "
            + "deadline while disabled; with its gates enabled, publication and conditional release evidence still control release.";
        const string agentsQualification =
            "Treat the park rule as conditional release, with no automatic deadline while parking is disabled.";

        var loop = Collapse(Read("docs/orchestration-loop.md"));
        var lifecycle = Collapse(Read("docs/agent-card-lifecycle.md"));
        Pin(loop, reply, "c1083-loop-reply");
        Pin(lifecycle, reply, "c1083-lifecycle-reply");
        loop.ShouldNotContain("Reply frees the runner seat", Case.Sensitive, "c1083-loop-no-reply-frees");
        lifecycle.ShouldNotContain("Reply frees the runner seat", Case.Sensitive, "c1083-lifecycle-no-reply-frees");
        loop.ShouldNotContain("an automatic release deadline exists", Case.Sensitive, "c1083-loop-no-deadline");
        lifecycle.ShouldNotContain("an automatic release deadline exists", Case.Sensitive, "c1083-lifecycle-no-deadline");

        var runtime = Collapse(Read("docs/session-runtime-invariants.md"));
        Pin(runtime, timing, "c1083-runtime-timing");
        runtime.ShouldContain("BlockedTaskParking:Enabled defaults to false.", Case.Sensitive, "c1083-runtime-default-off");
        runtime.ShouldContain("Parking never stops a Working session.", Case.Sensitive, "c1083-runtime-working");
        runtime.ShouldContain(
            "A confirmed Exited or Failed status, or absence from a listed catalogue, drops that seat.",
            Case.Sensitive, "c1083-runtime-catalogue");
        runtime.ShouldNotContain("Infer a release deadline from the 120-second idle proof", Case.Sensitive, "c1083-runtime-no-infer");

        lifecycle.ShouldContain("Blocked stays open for the card.", Case.Sensitive, "c1083-card-open");
        lifecycle.ShouldContain("outside MaxOpenTasks and the role gate", Case.Sensitive, "c1083-gate-excluded");
        lifecycle.ShouldContain("SeatIdle", Case.Sensitive, "c1083-seat-idle");
        lifecycle.ShouldContain("SlotOrphan", Case.Sensitive, "c1083-slot-orphan");
        lifecycle.ShouldContain("runner-seat-release:", Case.Sensitive, "c1083-attention-key");
        lifecycle.ShouldContain("BlockedTaskParking:Enabled defaults to false", Case.Sensitive, "c1083-lifecycle-default-off");

        var agents = Read("AGENTS.md");
        Pin(agents, agentsQualification, "c1083-agents-conditional");
        agents.ShouldContain("BlockedTaskParking:Enabled defaults to false", Case.Sensitive, "c1083-agents-default-off");
        agents.ShouldContain("Parking never stops a Working session.", Case.Sensitive, "c1083-agents-working");

        Read(".claude/skills/antiphon-delegate/SKILL.md")
            .ShouldContain("a Blocked one needs `-Reply`, not this.", Case.Sensitive, "c1083-delegate-reply");
        var claude = Read("CLAUDE.md").Replace("\r\n", "\n").Trim();
        claude.ShouldBe("@AGENTS.md", "c1083-claude-import");
    }

    /// <summary>CARD-1083 V-2. The orchestrator skill keeps the seat until release is confirmed.</summary>
    [Test]
    public void C1083_OrchestratorSkillKeepsBlockedSeatUntilConfirmedRelease()
    {
        const string seat =
            "A live Blocked child keeps its runner seat; an accepted Reply resumes work and does not itself free the seat.";
        const string overnight =
            "Answer it within the session or surface the missing decision before leaving; do not leave one overnight.";
        const string slots =
            "At capacity, read GET /api/session-runners/{id}/slots: orphan=true is not a count of free seats; "
            + "a live Blocked owner reads orphan=false with its park field.";
        const string gates =
            "BlockedTaskParking:Enabled and TerminalRunnerSeatRelease:AutomaticEnabled default to false; "
            + "do not count a live Blocked seat free until its release is confirmed. "
            + "An Exited or Failed status, or absence from a listed catalogue, drops that seat.";
        const string working = "Parking never stops a Working session (CARD-1083).";
        const string verbs =
            "Use -Reply for a Blocked answer, -Continue only for a question with standing authority, "
            + "and -Refine only for Queued, Dispatched or Working tasks. "
            + "Follow docs/session-runtime-invariants.md for released-park admission and prerequisite publication.";

        var skill = Collapse(Read(".claude/skills/antiphon-orchestrator/SKILL.md"));
        foreach (var (sentence, label) in new[]
        {
            (seat, "c1083-skill-seat"),
            (overnight, "c1083-skill-overnight"),
            (slots, "c1083-skill-slots"),
            (gates, "c1083-skill-gates"),
            (working, "c1083-skill-working"),
            (verbs, "c1083-skill-verbs"),
        })
            Pin(skill, sentence, label);

        skill.ShouldNotContain("until it is answered or cancelled", Case.Sensitive, "c1083-skill-no-answer-frees");
        skill.ShouldNotContain("seat stays until an operator enables", Case.Sensitive, "c1083-skill-no-enable-frees");
        skill.ShouldNotContain("count orphan=true", Case.Sensitive, "c1083-skill-no-orphan-count");
        skill.ShouldNotContain("an accepted Reply frees the seat", Case.Sensitive, "c1083-skill-no-reply-frees");
    }

    private static void Pin(string text, string sentence, string label)
    {
        text.ShouldContain(sentence, Case.Sensitive, label);
        var removed = text.Replace(sentence, "", StringComparison.Ordinal);
        removed.ShouldNotBe(text, label + "-present");
        Should.Throw<ShouldAssertException>(() => removed.ShouldContain(sentence, Case.Sensitive, label));
    }

    private static string Collapse(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
