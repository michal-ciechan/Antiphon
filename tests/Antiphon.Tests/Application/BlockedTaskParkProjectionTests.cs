using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>The slot route marks a live Blocked or Queued owner owned and carries its park (CARD-1124).</summary>
[Category("Unit")]
public sealed class BlockedTaskParkProjectionTests
{
    [Test]
    public void C1065_OccupancyTracksProcessesNotBlockedStatus()
    {
        var runtime = Read("docs/session-runtime-invariants.md");
        Require(runtime, "A Blocked session keeps its runner seat until parking releases it.", "c1065-seat-kept");
        Require(runtime, "OccupiesCapacity is true for any non-empty runner status other than Exited or Failed.", "c1065-occupies");
        Require(runtime, "OpenTaskId is the owner task: the latest Queued, Dispatched, Working or Blocked task bound to the session, so a live Blocked or Queued owner reads orphan=false (CARD-1124).", "c1065-orphan-owner");
        runtime.ShouldNotContain("remains orphan=true", Case.Sensitive, "c1124-no-stale-orphan");
        Require(runtime, "The slot DTO carries park: null, or the owner's current-attempt park id, state, reason code, release id and sync state. It is display-only and never release authority.", "c1124-slot-park");
        Require(runtime, "An unavailable runner inventory records inventoryState unavailable and does not zero desktop in-flight sessions.", "c1065-unavailable-inventory");
        Require(runtime, "Task detail exposes parkSync when the sync state is not NotRequired.", "c1065-parksync-detail");
        Require(runtime, "Attention SessionDisagreement keyed runner-seat-release:{releaseId} carries the park id, sync state, source SHA and reason, and omits report and answer text.", "c1065-attention-row");

        var ops = Read("docs/ops-http.md");
        Require(ops, "a live Blocked child is not an orphan", "c1124-ops-orphan-owner");
        Require(ops, "seat evidence carries `park=`", "c1124-ops-seat-park");
        Require(ops, "BlockedTaskParking:Enabled", "c1065-ops-enabled");
        Require(ops, "runner-seat-release:", "c1065-ops-attention-key");
        Require(ops, "parkSync", "c1065-ops-parksync");

        var api = Read("docs/antiphon-api.md");
        Require(api, "parkSync", "c1065-api-parksync");
        Require(api, "also counted live Blocked owners", "c1124-api-orphan-slots");
        Require(api, "BlockedTaskParking:Enabled", "c1065-api-enabled");
        Require(api, "runner-seat-release:", "c1065-api-attention-key");

        var lifecycle = Read("docs/agent-card-lifecycle.md");
        Require(lifecycle, "A Blocked session keeps its runner seat until parking releases it.", "c1065-lifecycle-seat");
        Require(lifecycle, "outside MaxOpenTasks and the role gate", "c1065-lifecycle-gate");
        Require(lifecycle, "SeatIdle", "c1065-lifecycle-seat-idle");
        Require(lifecycle, "SeatIdle for a live Blocked seat", "c1124-lifecycle-blocked-not-orphan");
        Require(lifecycle, "SlotOrphan", "c1065-lifecycle-slot-orphan");
        Require(lifecycle, "runner-seat-release:", "c1065-lifecycle-attention-key");

        Require(Read("server/Application/Services/SeatDesktopJoin.cs"),
            "<see cref=\"OpenTaskId\"/> is the owner task", "c1065-join-open-task");
        Require(Read("server/Application/Services/RunnerSlotService.cs"),
            "OccupiesCapacity", "c1065-slot-occupies");
    }

    [Test]
    public void C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers()
    {
        var runtime = Read("docs/session-runtime-invariants.md");
        Require(runtime, "BlockedTaskParking:Enabled defaults to false.", "c1065-enabled-default");
        Require(runtime, "ReclaimExisting defaults to false.", "c1065-reclaim-default");
        Require(runtime, "TerminalRunnerSeatRelease:AutomaticEnabled defaults to false.", "c1065-automatic-default");
        Require(runtime, "New publication and Blocked release require Enabled plus automatic release.", "c1065-new-park-gate");
        Require(runtime, "Legacy discovery additionally requires ReclaimExisting.", "c1065-legacy-gate");
        Require(runtime, "Disabling does not delete a publication receipt, does not abandon an accepted answer, and does not stop release reconciliation or sync-debt recovery.", "c1065-disable-keeps-recovery");
        Require(runtime, "Parking never stops a Working session.", "c1065-working-veto");
        Require(runtime, "park_resume_refused is written on each refused dispatch tick with no dedupe.", "c1065-resume-warn");
        Require(runtime, "Resume reads the desktop checkout, not runner operation 37.", "c1065-resume-desktop");
        Require(runtime, "git worktree prune prunes the whole repository.", "c1065-prune-repo");
        Require(runtime, "A missing mirror is recreated with worktree add only when the branch tip equals the parked SHA, and HEAD is never reset.", "c1065-worktree-add");
        Require(runtime, "The inspection lease ends before the dispatch claim.", "c1065-inspection-lease");
        Require(runtime, "The 422 follow_up_remote_pool_unsupported fires before the Blocked branch, so a confirmed park on a remote pool agent does not hear Reply.", "c1065-remote-422");
        Require(runtime, "HasConfirmedPublishedParkAsync is not scoped to the current attempt and accepts Resumed.", "c1065-park-identity");
        Require(runtime, "Continue requires question classification plus standing authority, then the accepted-answer path.", "c1065-continue");
        Require(runtime, "Only an explicit Reply after confirmed prerequisite publication continues a parked prerequisite.", "c1065-prerequisite");
        Require(runtime, "Report prose and a card moving to Done start nothing.", "c1065-no-inference");
        Require(runtime, "An old session TurnEnd cannot settle the new attempt.", "c1065-stale-turn");
        Require(runtime, "The caller UserPrompt receipt is proven on both paths: inline at completionSingleWriteBytes 86400, and at the production PtySingleChunkBytes 1024, where the transcript line is the pointer and the retained spill carries review-evidence and reviewed-sha (CARD-1104).", "c1065-v27-inline");
        Require(runtime, "ReclaimScheduledAsync runs from the dispatcher pool-release sweep and from RunnerSlotReconcileJob and calls ReclaimLegacyAsync(32, 3).", "c1065-reclaim-callers");
        Require(runtime, "The reclaim cursor is the singleton BlockedTaskParkReclaimCursors row Id 1 and advances after every visit, including failure.", "c1065-reclaim-cursor");
        Require(runtime, "The park bound is this release path (CARD-1083).", "c1065-park-bound");
        Require(runtime, "A short tail wraps. ReclaimScheduledAsync shares one overlap gate and, unless ReclaimIntervalSeconds is 0, runs at most once per ReclaimIntervalSeconds. One run visits each eligible Blocked row at most once and stops at the smaller of the eligible count and page size times the pass budget.", "c1065-reclaim-page");
        Require(runtime, "The reconcile job's released total counts confirmed releases only and does not include visit counts.", "c1065-reclaim-count");
        Require(Read("docs/antiphon-api.md"), "ReclaimIntervalSeconds", "c1108-api-interval");
        Require(Read("docs/ops-http.md"), "ReclaimIntervalSeconds", "c1108-ops-interval");
        Require(runtime, "A Held episode is not prepared again until its NextAttemptAt, stamped at now plus ReclaimHeldBackoffSeconds (default 600; 0 disables; a negative value is treated as 0), except park_workspace_reserved which stays immediate.", "c1108-held-backoff");
        Require(Read("docs/antiphon-api.md"), "`ReclaimHeldBackoffSeconds` defaults to 600; 0 disables the Held backoff and a negative value is treated as 0.", "c1108-api-backoff");
        Require(Read("docs/ops-http.md"), "ReclaimHeldBackoffSeconds defaults to 600", "c1108-ops-backoff");
        Require(runtime, "FreshLegacyWindow uses StableFor only as a duration and requires the server-clock interval since park.CreatedAt to reach 120 seconds; the runner's FirstObservedAt is ignored. An old CompletedAt is not the idle window.", "c1065-fresh-window");
        Require(runtime, "Known limits stay on CARD-1097 items 2-5 (resume reads the desktop checkout, the inspection lease window, confirm-path bookkeeping, and whole-repository prune); CARD-1103, CARD-1104, CARD-1129 and CARD-1135 are closed by the CARD-1108/1124 follow-up plan.", "c1141-known-limits");
        Require(runtime, "With Enabled and ReclaimExisting set, physical release still requires AutomaticEnabled.", "c1065-automatic-still-required");

        var loop = Read("docs/orchestration-loop.md");
        Require(loop, "the published seat was released", "c1065-published-seat");
        Require(loop, "A Blocked session keeps its runner seat until parking releases it.", "c1065-loop-seat");
        Require(loop, "Blocked is outside MaxOpenTasks and still occupies a runner seat.", "c1065-max-open");
        Require(loop, "orphan=true is not a count of free seats", "c1065-loop-orphan");
        loop.ShouldNotContain("count orphan=true", Case.Sensitive, "c1124-loop-no-count");
        Require(loop, "The 422 follow_up_remote_pool_unsupported fires before the Blocked branch, so a confirmed park on a remote pool agent does not hear Reply.", "c1065-loop-422");
        Require(loop, "including Blocked", "c1065-including-blocked");

        var agents = Read("AGENTS.md");
        Require(agents, "the park bound in [docs/session-runtime-invariants.md](docs/session-runtime-invariants.md) is dormant (CARD-1083)", "c1065-park-rule");
        Require(agents, "BlockedTaskParking:Enabled defaults to false", "c1065-agents-default");
        Require(agents, "Parking never stops a Working session.", "c1065-agents-working");

        var basics = Read("server/Bundles/delegate-basics.md");
        Require(basics, "Commit and push all assigned work before reporting blocked.", "c1065-push-before-blocked");
        Require(basics, "unless this task is SourceLanding, ReadOnly, or CommitOnSettle Never", "c1065-push-exclusion");
        Require(basics, "A parked session may be released and resumed from that pushed branch.", "c1065-resume-branch");
        Require(basics, "Parking will not autosave (CARD-1083).", "c1065-card-1083");

        foreach (var relative in new[] { ".claude/skills/antiphon-orchestrator/SKILL.md" })
        {
            var text = Read(relative);
            Require(text, "A Blocked child holds its seat until it is answered or cancelled.", "c1065-blocked-child:" + relative);
            Require(text, "do not leave one overnight", "c1065-overnight:" + relative);
            Require(text, "GET /api/session-runners/{id}/slots", "c1065-slots:" + relative);
            Require(text, "orphan=true is not a count of free seats", "c1065-orphan-count:" + relative);
            text.ShouldNotContain("count orphan=true", Case.Sensitive, "c1124-skill-no-count");
            Require(text, "(CARD-1083)", "c1065-card-1083:" + relative);
        }

        Require(Read("server/Bundles/stage-plan.md"),
            "only when the plan adds or changes a session that waits for input: what releases a session that waits for input, and after how long? Today nothing releases such a session automatically (CARD-1083).",
            "c1065-release-question");

        Require(Read("server/Application/Settings/BlockedTaskParkingOptions.cs"),
            "public bool Enabled { get; set; } = false;", "c1065-options-enabled");
        Require(Read("server/Application/Settings/BlockedTaskParkingOptions.cs"),
            "public int ReclaimIntervalSeconds { get; set; } = 120;", "c1108-options-interval");
        Require(Read("server/Application/Settings/BlockedTaskParkingOptions.cs"),
            "public int ReclaimHeldBackoffSeconds { get; set; } = 600;", "c1108-options-backoff");
        Require(Read("server/Application/Services/TerminalRunnerSeatReleaseService.cs"),
            "if (!options.Value.AutomaticEnabled) return false;", "c1065-automatic-return");

        var follow = Read("server/Application/Services/AgentTaskService.cs");
        var remote = follow.IndexOf("RefuseRemotePoolFollowUp(followAgent, retainedRunnerId, priorId);", StringComparison.Ordinal);
        var live = follow.IndexOf("if (sessionLive)", StringComparison.Ordinal);
        remote.ShouldBeGreaterThanOrEqualTo(0, "c1065-remote-refuse");
        live.ShouldBeGreaterThan(remote, "c1065-remote-before-blocked");
    }

    private static void Require(string text, string phrase, string label) =>
        text.ShouldContain(phrase, Case.Sensitive, label);

    private static string Read(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Antiphon.sln was not found.");
        return File.ReadAllText(Path.Combine(directory.FullName, relative));
    }
}
