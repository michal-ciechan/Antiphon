using System.Reflection;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
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
            text.ShouldContain("current push-gate holder");
            text.ShouldContain("CARD-1093");
            text.ShouldNotContain("enqueue-time snapshot");
        }

        // CARD-1200: the pipeline route doc names that same current holder.
        var api = Read("docs/antiphon-api.md");
        api.ShouldContain(AgentTaskPipelineStatusService.QueueReasonRemotePrep);
        api.ShouldContain(nameof(RemotePrepProgress.BehindTaskId));
        api.ShouldContain("current push-gate holder");
        api.ShouldContain("read under the gate lock");
        api.ShouldContain("at read time");
        api.ShouldContain("CARD-1093");
        api.ShouldContain("CARD-1100");
        api.ShouldNotContain("enqueue-time BehindTaskId");
        api.ShouldNotContain("it is not the exact current holder");

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
            "Pending evidence without a debt row: the progress read blocked this task; repair what the handoff names, then reply.");
        Read("server/Application/Services/SettlementSyncDebtAttention.cs").ShouldContain(
            "The task attempt, baseline or worktree changed; this debt will not be fast-forwarded.");
        Read("server/Application/Services/AttentionService.cs").ShouldContain(
            "SettlementSyncDebtAttention.Build");
        Read("server/Application/Services/AgentTaskService.cs").ShouldContain("new TaskSyncDebtDto");
    }

    /// <summary>
    /// CARD-1802 V-6. Rule 6 (never Reply to a complete report Blocked only by runner sync) and the
    /// CARD-1082 sync-debt design agree: lease contention alone never leaves Pending evidence without
    /// a debt row, in every place that explains Pending evidence, and rule 6's reason names are
    /// the server's own constants.
    /// </summary>
    [Test]
    public void C1802_rule_six_and_the_sync_debt_design_agree()
    {
        const string canonical =
            "Lease contention alone never leaves Pending evidence of a correlated complete report without a debt row (CARD-1802).";
        const string stale = "Pending evidence without a debt row means the task stayed Blocked and a reply is required.";

        var loop = Read("docs/orchestration-loop.md").Replace("\r\n", "\n");
        var rule6 = Slice(loop, "6. **Do not Reply", "7. **Master lands", "c1802-rule-6");
        var design = Line(loop, "CARD-1082. Delegation:RunnerSyncDebtOnSettlement", "c1802-design");
        var ops = Line(Read("docs/ops-http.md").Replace("\r\n", "\n"), "| Settlement sync debt (CARD-1082) |", "c1802-ops");
        var runtime = Line(Read("docs/session-runtime-invariants.md").Replace("\r\n", "\n"),
            "CARD-1082. Delegation:RunnerSyncDebtOnSettlement", "c1802-runtime");
        var script = Line(Read("scripts/delegate.ps1").Replace("\r\n", "\n"),
            "Write-Output 'Pending evidence without a debt row", "c1802-script");

        foreach (var (text, label) in new[]
        {
            (rule6, "c1802-rule-6"), (design, "c1802-design"), (ops, "c1802-ops"),
            (runtime, "c1802-runtime"), (script, "c1802-script"),
        })
        {
            Pin(text, canonical, label + "-canonical");
            text.ShouldNotContain(stale, Case.Sensitive, label + "-stale");
        }
        foreach (var relative in new[] { "docs/orchestration-loop.md", "docs/ops-http.md",
            "docs/session-runtime-invariants.md", "scripts/delegate.ps1" })
            Read(relative).ShouldNotContain(stale, Case.Sensitive, "c1802-stale-" + relative);

        // Rule 6 names the reason a Pending complete report still blocks on, and every runner_sync_
        // reason it names is a server constant.
        rule6.ShouldContain("`" + RemoteSettlementSyncReasons.ProgressUnverified + "`", Case.Sensitive, "c1802-rule-6-unverified");
        var constants = typeof(RemoteSettlementSyncReasons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet(StringComparer.Ordinal);
        var named = Regex.Matches(rule6, "runner_sync_[a-z_]+").Select(m => m.Value).Distinct().ToList();
        named.ShouldContain(RemoteSettlementSyncReasons.ProgressUnverified, "c1802-rule-6-names");
        foreach (var reason in named)
            constants.ShouldContain(reason, "c1802-rule-6-constant:" + reason);
        design.ShouldContain("the sweep fetches", Case.Sensitive, "c1802-design-fetches");
    }

    /// <summary>
    /// CARD-1805 (A-1..A-3). The CARD-1802 sentences cover a correlated complete report only. Bind-refusal
    /// recovery has no correlated report and keeps its lease-busy block (rule 6, the design, ops, the
    /// runtime invariants and the delegate.ps1 status line say so); the unfetched arm's unverified claim
    /// is a documented residual; and the unfetched gate's presence read separates absence from an error.
    /// </summary>
    [Test]
    public void C1805_bind_refusal_recovery_and_the_unfetched_residual_are_documented()
    {
        const string recovery = "Bind-refusal recovery has no correlated report, so its lease-contention result stays "
            + "Blocked on runner_sync_lease_busy with no debt row, and its handoff asks for the correlated completion "
            + "report that settles it (CARD-1805).";
        const string residual = "An unfetched owned tip settles with the report's claimed commit unverified: the sweep "
            + "fetches S and fast-forwards but never re-checks the claim, so a claimed commit that is not on S is not "
            + "caught after settlement; CARD-1109's attestation upgrade would close it (CARD-1805).";
        const string gate = "The unfetched gate reads object presence with git cat-file -e: only exit 1 is an unfetched "
            + "tip, and any other failure, such as an unreadable repository, is runner_sync_progress_unverified (CARD-1805).";
        const string scriptClause = "A bind-refusal recovery has no correlated report, so its handoff asks for one (CARD-1805).";

        var loop = Read("docs/orchestration-loop.md").Replace("\r\n", "\n");
        var rule6 = Slice(loop, "6. **Do not Reply", "7. **Master lands", "c1805-rule-6");
        var design = Line(loop, "CARD-1082. Delegation:RunnerSyncDebtOnSettlement", "c1805-design");
        var ops = Line(Read("docs/ops-http.md").Replace("\r\n", "\n"), "| Settlement sync debt (CARD-1082) |", "c1805-ops");
        var runtime = Line(Read("docs/session-runtime-invariants.md").Replace("\r\n", "\n"),
            "CARD-1082. Delegation:RunnerSyncDebtOnSettlement", "c1805-runtime");
        var script = Line(Read("scripts/delegate.ps1").Replace("\r\n", "\n"),
            "Write-Output 'Pending evidence without a debt row", "c1805-script");

        foreach (var (text, label) in new[] { (rule6, "c1805-rule-6"), (design, "c1805-design"), (ops, "c1805-ops"),
            (runtime, "c1805-runtime") })
            Pin(text, recovery, label + "-recovery");
        Pin(design, residual, "c1805-design-residual");
        Pin(runtime, residual, "c1805-runtime-residual");
        Pin(design, gate, "c1805-design-gate");
        Pin(script, scriptClause, "c1805-script-recovery");
        rule6.ShouldContain("never Blocks a correlated complete report", Case.Sensitive, "c1805-rule-6-narrowed");
        rule6.ShouldNotContain("never Blocks a complete report at all", Case.Sensitive, "c1805-rule-6-over-broad");
        design.ShouldContain("For a correlated report, Pending evidence without a debt row means the progress read, not the lease",
            Case.Sensitive, "c1805-design-narrowed");
        ops.ShouldContain("is the progress-blocked case for a correlated report:", Case.Sensitive, "c1805-ops-narrowed");
        recovery.ShouldContain(RemoteSettlementSyncReasons.LeaseBusy, Case.Sensitive, "c1805-recovery-reason");
        gate.ShouldContain(RemoteSettlementSyncReasons.ProgressUnverified, Case.Sensitive, "c1805-gate-reason");
    }

    private static string Slice(string text, string start, string end, string label)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.ShouldBeGreaterThanOrEqualTo(0, label + "-start");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        to.ShouldBeGreaterThan(from, label + "-end");
        return text[from..to];
    }

    private static string Line(string text, string start, string label)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.ShouldBeGreaterThanOrEqualTo(0, label + "-start");
        var to = text.IndexOf('\n', from);
        return to < 0 ? text[from..] : text[from..to];
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

    /// <summary>
    /// CARD-1213 S3. Owners cite git-common instead of restating git rules, and name both branch
    /// forms with the recorded WorktreeBranch as the source. Expected sentences are literals.
    /// </summary>
    [Test]
    public void C1213_DocsCiteGitCommonAndBothBranchForms()
    {
        var loop = Collapse(Read("docs/orchestration-loop.md"));
        Pin(loop, "`delegate-basics` carries `{{GIT-COMMON}}`, so each non-specialist delegate launches with "
            + "`server/Bundles/Fragments/git-common.md`.", "c1213-loop-carrier");
        Pin(loop, "Do not restate those rules in a brief; supply the per-task values only.", "c1213-loop-no-restate");
        loop.ShouldNotContain("commit-and-push each slice", Case.Sensitive, "c1213-loop-old-list");

        var runtime = Collapse(Read("docs/session-runtime-invariants.md"));
        Pin(runtime, "Two forms exist: `feat/card-task-<id>` and `feat/card-<n>-<stage>-<id>`.", "c1213-runtime-forms");
        runtime.ShouldContain("RunnerWorkspaceBindingTests.C1213_BindingBothBranchForms", Case.Sensitive, "c1213-runtime-mirror-test");

        // Review dd8ce0cf F1/F3, 3153a5c4 R2-F2: launch carriers per provider; Grok's --rules value is a
        // bootstrap naming the rules file. The runner contract keeps its protections.
        Pin(loop, "Claude Code receives `--append-system-prompt <text>` and Codex `-c developer_instructions=<text>`.",
            "c1213-loop-carriers");
        Pin(loop, "Grok receives a `GrokRulesPayload`: the runner writes it to a runner-owned `rules.md` and passes "
            + "`--rules` with a bootstrap instruction naming that file", "c1213-loop-grok-carrier");
        loop.ShouldNotContain("writes to a rules file and passes as `--rules`", Case.Sensitive, "c1213-loop-no-rules-file-arg");
        Pin(loop, "restates the rebase, amend, reset and force-push prohibitions in one sentence, because a recovered "
            + "session may have launched before git-common existed", "c1213-loop-recovered");
        loop.ShouldNotContain("every future dispatch gets the better version", Case.Sensitive, "c1213-loop-no-every-dispatch");
        runtime.ShouldContain("DelegationDispatchRecoveryBoundaryTests.C1213_Recovered_brief_keeps_branch_protections_without_git_common",
            Case.Sensitive, "c1213-runtime-recovered-test");

        var skill = Collapse(Read(".claude/skills/antiphon-orchestrator/SKILL.md"));
        Pin(skill, "copy the sibling task's recorded `WorktreeBranch` (`feat/card-<n>-<stage>-<id>` or "
            + "`feat/card-task-<id>`) rather than rebuilding it from the task id", "c1213-skill-branch");
        skill.ShouldNotContain("feat/card-task-XXXX", Case.Sensitive, "c1213-skill-legacy-example");
        // Review dd8ce0cf F2: the sibling is the creation base; the delegate stays on its own branch.
        Pin(skill, "Do not tell a delegate to fetch or check out a sibling's branch.", "c1213-skill-no-sibling-checkout");
        Pin(skill, "select the sibling's tip as the creation base: `-StartRef <full SHA>`", "c1213-skill-start-ref");
        skill.ShouldNotContain("fetch/checkout", Case.Sensitive, "c1213-skill-no-fetch-checkout");
        // F5: the report token settles; the handoff block only sets next=.
        Pin(skill, "A report with the token and no block settles, and its completion header says `next=unmarked`.",
            "c1213-skill-unmarked-settles");
        skill.ShouldNotContain("never settles", Case.Sensitive, "c1213-skill-no-never-settles");
        // F6: the original Code owner lands; the reviewed source is named with -FromTask.
        Pin(skill, "`-Land <owner> -FromTask <source> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>`",
            "c1213-skill-from-task");
        Pin(skill, "Use `-FromTask` or `-RecoverReviewedSource`, not both.", "c1213-skill-recovery-exclusive");
        skill.ShouldNotContain("Land against whichever task's branch", Case.Sensitive, "c1213-skill-no-branch-holder");

        var readme = Collapse(Read("server/Bundles/README.md"));
        Read("server/Bundles/README.md").ShouldContain(
            "(../../docs/orchestration-loop.md#delegate-branch-names-card-1213)", Case.Sensitive, "c1213-readme-link");
        // F4, 3153a5c4 R2-F5: refusals and the Grok preflight come before lane selection; no unconditional launch claim.
        Pin(readme, "applies its refusals and the Grok rules preflight before `ResolveLane` picks Relaunch or Notify",
            "c1213-readme-lanes");
        Pin(readme, "`PolicyRefreshServiceTests.Grok_legacy_policy_drift_refuses_before_kill_queue_or_stamp_mutation`",
            "c1213-readme-grok-preflight");
        readme.ShouldNotContain("and otherwise fall back to Notify.", Case.Sensitive, "c1213-readme-no-unconditional-fallback");
        readme.ShouldNotContain("never falls back to Notify", Case.Sensitive, "c1213-readme-no-never-falls-back");
        readme.ShouldNotContain("are guaranteed one", Case.Sensitive, "c1213-readme-no-guarantee");
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
        "Dirty, diverged and rewound Code stay Blocked with no debt row; an unfetched owned tip under lease contention is Pending with a debt row, and the sweep fetches its objects under the lease before the fast-forward (CARD-1802).",
        "The policy accepts only the task's own owned ref as the observed tip, and a Code report with no progress evidence under Pending stays Blocked.",
        "For a correlated report, a Blocked Pending settlement's warning says Runner sync unavailable and then reply, never Runner sync pending; its evidence still records Pending, and its block reason is never runner_sync_lease_busy (runner_sync_progress_unverified when the progress read gave no reason).",
        "Pending evidence without a debt row means the progress read, not the lease, blocked the task; repair what the handoff names, then reply.",
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
        "A Held debt is re-checked every 60 minutes by restamping NextAttemptAt 60 minutes ahead: the re-check reads no filesystem and matches no path, runs no Git, fast-forwards nothing, and does not supersede, because AgentTaskSyncDebts has no registration id that a complete retirement can be bound to. The attention warning stays until that binding exists.",
    ];

    private static readonly string[] RuntimeSentences =
    [
        "Delegation:RunnerSyncDebtOnSettlement defaults to true.",
        "Pending is never Confirmed.",
        "desktop-sync=pending",
        "synced later",
        "Dirty, diverged and rewound Code stay Blocked",
        "then reply",
        "backoff is 1, 2, 4, then 5 minutes",
        "-Land does not read RemoteSync.",
        "syncDebt",
        "A Held debt is re-checked hourly and stays Held: the re-check does not read the worktree and does not supersede the row.",
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
        "the progress read, not the lease, blocked the task; repair what the handoff names, then reply.",
        "RunnerSyncDebtAttentionMinutes defaults to 30.",
        "Held debt is re-checked hourly and stays Held, so the re-check does not clear its attention row.",
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
