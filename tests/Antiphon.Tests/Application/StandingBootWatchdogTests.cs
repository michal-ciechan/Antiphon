using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S1-S3 and S5 (option A, detection only): the real <c>BootReplyWatchdogService</c>
/// sweep over a taskless AlwaysOn session on an isolated PostgreSQL database with a
/// <c>FakeTimeProvider</c>, through the new <c>StandingBootWatchFixture</c>. Design V-2, V-5,
/// V-6, V-7 and V-12 in
/// <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// Every method asserts the fail-closed rule: the recording stopper is empty, the runner's
/// kill/start/compaction-stop/input/release counters are zero, the session row keeps its status,
/// termination source, EndedAt and owner pointer, the supervision row is byte-equal to its seeded
/// snapshot (or still absent), and no queue row, park row, alias hold or probe is created.
/// </summary>
[Category("Integration")]
public class StandingBootWatchdogTests
{
    /// <summary>
    /// V-2 (S1-S3). A nine-minute real prompt, no model row, live matching runner listing, one
    /// AlwaysOn owner per provider kind whose delivery verification is Supported (Grok with
    /// GrokRulesState Ready). <c>TranscriptWorkingStateQuery</c> reads Working = true before and
    /// after. Sweeps at 9 and 21 minutes and real <c>AgentSupervisorService.TickAsync</c> ticks
    /// between them keep the session and pointer: Warning then Error receipts, nothing else.
    /// </summary>
    [Test]
    [Arguments("claude-code")]
    [Arguments("grok")]
    [Arguments("codex")]
    public Task C1156_Working_boot_keeps_its_session_and_supervisor_custody(string kind) =>
        Card1156Pending.Skip("S1-S3", nameof(C1156_Working_boot_keeps_its_session_and_supervisor_custody));

    /// <summary>
    /// V-5 (S3). One receipt per episode key and stage across repeated-tick, fresh-provider and
    /// concurrent-sweeps (two providers meeting at the writer's session lock); new-prompt,
    /// new-generation and new-resume-clock each mint a new key with its own receipt and the old
    /// episode leaves the current projection; legacy-cleared-watch (both watch columns null after
    /// an old raise with an old <c>bootSeq=</c> incident) self-heals through the existing
    /// predicate and records the new key once, never a second old-format receipt.
    /// </summary>
    [Test]
    [Arguments("repeated-tick")]
    [Arguments("fresh-provider")]
    [Arguments("concurrent-sweeps")]
    [Arguments("new-prompt")]
    [Arguments("new-generation")]
    [Arguments("new-resume-clock")]
    [Arguments("legacy-cleared-watch")]
    public Task C1156_Episodes_deduplicate_and_reopen_only_for_new_identity(string shape) =>
        Card1156Pending.Skip("S3", nameof(C1156_Episodes_deduplicate_and_reopen_only_for_new_identity));

    /// <summary>
    /// V-6 (S3). Faults on the writer's own context: read-fault (the dedup read), insert-fault
    /// (the INSERT command), save-fault (<c>SavingChangesAsync</c>), commit-fault (the transaction
    /// commit), publish-fault (the event bus throws after the commit), caller-cancel (the sweep's
    /// token is cancelled at the writer's lock statement and the cancellation propagates out of
    /// <c>SweepAsync</c>). No stop, no supervision mutation, no rolled-back row, nothing staged on
    /// the sweep's context; a publish fault keeps its one committed receipt; after the fault clears
    /// a fresh tick records exactly once.
    /// </summary>
    [Test]
    [Arguments("read-fault")]
    [Arguments("insert-fault")]
    [Arguments("save-fault")]
    [Arguments("commit-fault")]
    [Arguments("publish-fault")]
    [Arguments("caller-cancel")]
    public Task C1156_Telemetry_faults_preserve_custody_and_future_writes(string fault) =>
        Card1156Pending.Skip("S3", nameof(C1156_Telemetry_faults_preserve_custody_and_future_writes));

    /// <summary>
    /// V-7 (S3). Fresh evidence committed before the writer's final read prevents the insert:
    /// reply-during-pull (the real runtime catch-up stores an AssistantText from the runner
    /// transcript), queued-owner-before-insert, blocked-owner-before-insert,
    /// pointer-changed-before-insert, generation-changed-before-insert (each committed on a second
    /// connection as the writer's lock statement executes), session-lock-held (a second connection
    /// holds an uncommitted UPDATE of the session row; the writer returns inside 30 s, logs nothing
    /// above Debug, writes nothing; after rollback the next tick records once).
    /// </summary>
    [Test]
    [Arguments("reply-during-pull")]
    [Arguments("queued-owner-before-insert")]
    [Arguments("blocked-owner-before-insert")]
    [Arguments("pointer-changed-before-insert")]
    [Arguments("generation-changed-before-insert")]
    [Arguments("session-lock-held")]
    public Task C1156_Fresh_evidence_revokes_stale_emission(string change) =>
        Card1156Pending.Skip("S3", nameof(C1156_Fresh_evidence_revokes_stale_emission));

    /// <summary>
    /// V-12 (S5). Evidence variants that must never authorize recovery: runtime-absent (no runtime
    /// registered: stored-evidence detection), pull-fault (the runner transcript request throws),
    /// successful-unchanged-pull, runner-absent (the runner lists nothing), non-working (prompt then
    /// an interrupt marker: Working false), queued-prompt (QueuedUserPrompt only: the receipt says
    /// "queued prompt record" and "no reply observed", never "accepted" or "delivered", and the
    /// prompt is never resent), legacy-latch (seeded ConsecutiveFailures 2, LivenessLatchedAt set,
    /// a future NextRestartAt and nondefault unrelated fields: the row is byte-equal afterwards),
    /// terminal-task-history (a Succeeded and a Failed task on this session do not withhold the
    /// standing detection), non-alwayson-legacy-diagnostic (an owner with AlwaysOn false keeps the
    /// existing generic <c>bootSeq=</c> receipt and the existing disarm, with no supervision row
    /// and no stop). Every variant: empty stopper, zero runner counters, no queue or park row,
    /// message/byte snapshot unchanged, no probe enqueued.
    /// </summary>
    [Test]
    [Arguments("runtime-absent")]
    [Arguments("pull-fault")]
    [Arguments("successful-unchanged-pull")]
    [Arguments("runner-absent")]
    [Arguments("non-working")]
    [Arguments("queued-prompt")]
    [Arguments("legacy-latch")]
    [Arguments("terminal-task-history")]
    [Arguments("non-alwayson-legacy-diagnostic")]
    public Task C1156_Evidence_variants_never_authorize_recovery(string variant) =>
        Card1156Pending.Skip("S5", nameof(C1156_Evidence_variants_never_authorize_recovery));
}
