# CARD-1610 S2+S3 joint Review (task db82c1e8)

Subject: origin feat/card-task-698bf6b9 at 9fd5b4ac7c0dc8f065e78498afd37d122808b0a8 (base 698bf1b73e8abf449d5569ea74ac8c82e80f92a1, 39 commits).
Verdict: NOT landable yet. There are two product defects and one witness gap. The hold, phase, fence and counting semantics are sound.

## Defects

### D-1: Cancel now holds the session queue gate, which regresses the server2 terminal seat release (product, S2)
- Where: server/Application/Services/AgentTaskService.cs:2974-2986 (new `CancelAsync` wrapper takes `_messageQueue.GetLock(sessionId)` for every Working task, then calls `CancelCoreAsync`).
- Failure: `CancelCoreAsync` calls `TerminalRunnerSeatReleaseService.TryHandleTaskAsync` while the wrapper still holds the gate. That service probes the same gate non-blockingly (`WaitAsync(0)`, TerminalRunnerSeatReleaseService.cs:1036-1038), sees it taken by Cancel itself, and records the release as `PendingDelivery`. It should run the normal qualification (`SettlementTooYoung` and so on). As a result, every Cancel of a Working task on a non-desktop runner (server2) parks its seat release behind a false pending-delivery hold.
- Evidence: `TerminalRunnerSeatReleaseTests/Cancellation_reconciles_without_second_stop` fails at the tip with 1/1 failed: `ReasonCode should be "SettlementTooYoung" but was "PendingDelivery"`. It passes 1/1 at base 698bf1b73, in a detached base build run through build-slot. The test file, its fixture and the service are unchanged by the branch, so this is a new regression and not an inherited one. The class is not in the plan's checkpoint census, which is why the author's 3,557-test part 2 did not catch it.
- Why: Cancel/seat custody must keep its existing release contract. The CARD-0667/1065 release path is the only thing that frees the runner seat after an explicit Cancel.
- Fix: do not hold the recipient gate across `TerminalRunnerSeatReleaseService.TryHandleTaskAsync`, `ReleaseTaskConsumersAsync` or other lock-probing post-commit work. Take the gate only around the reload, status check and Canceled save, and release it before the post-commit seat/workspace release. Alternatively, serialize Continue against Cancel through the task ConcurrencyToken (`AnswerCeilingAsync` already uses it) instead of the queue gate. Add `TerminalRunnerSeatReleaseTests` (class) to the S2 checkpoint rows.

### D-2: the FailureDelegate late-report watch never terminates, so retention is pinned forever and forwarding latency grows (product, S3)
- Where: server/Application/Services/TaskSupervisionNoticeService.cs:12-17 (`Unresolved`), :22-27 (`due` = any FailureDelegate with `ReportForwardedAt == null`, `Take(24)`), :57 (`NextAttemptAt = now+1min` forever), :28-40 (backfill of every historical Failed task). There is no close condition in LateFailedReportForwarder.cs.
- Failure: a FailureDelegate notice is closed only when a late report is forwarded, or when a later pass sees the task attempt change. In every other case it stays due and Unresolved forever. That covers the common case of a failed delegate that never writes a late report, and also the case where its session has already ended and the final transcript window has been scanned.
  - Probe `Probe_failure_report_watch_never_terminates_after_session_end`: a 20-minute model-wait failure, then the session is marked Stopped/EndedAt, then five daily `RecoverAsync` passes. After all of that, the notice still has `ReportForwardedAt=null`, is re-polled on every pass, and remains in `Unresolved()`. DataRetentionService uses `Unresolved()` to protect sessions, transcripts, queue rows and the whole task tree (DataRetentionService.cs:185-193, 249-255, 353-363, 423-434). So the transcript of every failed task with a known generation is retained indefinitely.
  - The general pass runs only on the 15-minute sweep (CompletionNoteWorkHostedService.cs:39) and takes 24 rows ordered by `NextAttemptAt`. The backfill turns every historical Failed row into a permanently due row. A late report written after the first targeted pass therefore waits behind the whole round-robin: about N_failed/24 × 15 minutes.
- Why: the plan (D-2/D-5) requires a bounded scan with normal backoff, and retention protection only for unresolved debt. A watch whose source generation has ended and whose final window is scanned has no remaining debt. A forever-due set breaks both requirements and the performance standard.
- Fix: close the watch with a durable reason in three cases:
  - Source session ended (EndedAt set) and `ScanFrontier` has reached its last sequence, after one final-window scan.
  - Identity is unknown (null generation, i.e. historical/legacy). Mark it not watchable at backfill instead of polling it forever.
  - Optionally, a bounded age.
  
  Apply exponential backoff to report-scan polls, and exclude closed watches from `due` and from `Unresolved()`. Add a witness for "ended session, no late report: watch closes and retention resumes", plus a scale witness showing a new late report is forwarded within one sweep despite N stale watches.

### D-3: the late-Failed witness cannot see an outcome flip after delivery (test adequacy, S3)
- Where: tests/Antiphon.Tests/Application/TaskSupervisionReceiptTests.cs:158-163 (the `late` arm of `ReceiptAsync`).
- Failure: mutant M3 sets the task's Status to Succeeded when the LateFailedParent notice is confirmed Told (TaskSupervisionNoticeService.ConfirmAsync). It **survived**: `C1610_Failed_late_report_parent_has_complete_UserPrompt` passed 3/3. The arm asserts Result, CompletedAt and FailureReason, but not Status, NextStage/NextHandoff, or the absence of Completed/Succeeded events after forwarding.
- Why: "a late Failed report never changes the outcome" is the core S3 invariant. Today it is unwitnessed at the point where the note is delivered.
- Fix: after the receipt, assert `Status == Failed`, unchanged NextStage/NextHandoff, a single Failed event and no Completed event, and keep the existing zero StageOutcome/PipelineHandoffAction assertions. Rerun M3; it must go red.

## Checks requested by the brief

1. **Nothing auto-kills a Working session.**
   - A Working, live, current-generation session at the ceiling is HELD: Status stays Working, with a typed episode and one CeilingParent question, and Stopper.Killed is empty. The only new stop-capable paths are explicit Cancel (unchanged stop semantics) and Retry (`Reset` plus existing requeue stop).
   - The hold changes no status, runner, session or reservation field, so every selector that counts Dispatched/Working still counts it. This was confirmed by the `C1610_Hold_keeps_occupancy` arms (DispatchConcurrencyPolicy absolute+role 409, the real local `DispatchQueuedAsync` cap, and the pipeline-status route counted twice), all green in CP-16. It also covers host budget, runner seats and keep-six by construction: those read the same Working population, and S2 does not edit them.
   - At 720: Continue and ordinary Extend refuse with `ceiling_cap_reached`. The task stays Working, counted, session Running, and is never failed or killed. Probe `Probe_final_cap_hold_stays_working_and_unkilled_for_hours`: a hold at 721 minutes, then 10×30 minutes of sweeps with fresh ToolCalls, kept the same episode and Status Working, with no kill and one parent question.
   - Only authenticated operator Extend (1..240) re-arms beyond 720, and Cancel still works. A 720 hold is not a terminal failure and not an auto-kill.
   - An idle (non-Working) session at the ceiling still uses the pre-existing non-destructive overdue failure, without a kill.
2. **Phase deadlines.**
   - `Select` lets a breached ModelWait, LocalExecution or ProviderInputWait win over any ceiling fraction. Held tasks still fail at 20 and 90 minutes (`C1610_Phase_beats_ceiling`, `C1610_Held_task_keeps_phase_supervision`; mutant M6 killed 4/4).
   - The CARD-1604 token_count heartbeat still adjusts ModelWait only (`ModelWaitElapsed`, policy arm `heartbeat`, green in CP-15).
3. **Late-Failed forwarding and fencing.**
   - The forwarder only inserts notices. It never settles, Replies, moves the card or runs a handoff. It is idempotent through the unique (TaskId, Attempt, SourceEventId, Purpose) index plus the unique queue `SourceTaskSupervisionNoticeId`, all durable, with no in-memory state.
   - The successor fence follows the plan's D-5. It does not refuse creating a successor. It forces `next=decide` and refuses automatic handoff reservation (`handoff_successor_held` / `successor_held`) when a newer same-card/role task exists since DispatchedAt. Mutant M4 killed 4 of 5; the surviving case is the correct different-role arm.
   - Migrations are additive: 13 nullable task columns plus one NOT NULL bool with default false, a new table, and a nullable queue column with a filtered unique index. Old rows read as not held.
   - Restart mid-hold restores the hold with no second question (CP-16 restart arm green).
4. **Races.**
   - Probe `Probe_two_continues_race_once`: exactly one ok and one `ceiling_episode_stale`, NextCeiling 481.
   - Probe `Probe_continue_vs_cancel_never_resurrects`: the task ends Canceled, a later Continue is refused stale, a later sweep leaves it Canceled, and there is exactly one stop.
   - Report-versus-hold, retry, cancel and generation races are green in CP-16. Lock order is the same in hold and answer (settle gate, then queue gate). See D-1 for the Cancel gate side effect.
5. **Deploy.** No Deploy-specific code and no scripts changed. Deploy gets the all-role hold exactly as plan D-1/D-4 states. Budgets, delegated drain slices and CeilingMinutes CLI are S4.
6. **Witnesses.**
   - Red-first history exists in the commits and reports: the hold class was 7/7 red at ad0f59d99 before d60815339, the controls were 13 red at cf80deb58, and the receipts were 21/21 red at 386acf115 before d248ec616.
   - My mutants used an env-switched mutant build of the tip:

| Mutant | Change | Result |
|---|---|---|
| M1 | hold writes Blocked (held counted as free) | killed: Working_ceiling_holds 5/5 and Hold_keeps_occupancy red |
| M2 | fail a held task at the ceiling anyway | killed: Working_ceiling_holds 5/5 red |
| M3 | late note flips the task to Succeeded at receipt | **survived** (D-3) |
| M4 | skip the successor fence | killed: 4 red |
| M5 | Continue beyond 720 | killed: ordinary-refusal red |
| M6 | ceiling fraction masks a breached phase | killed: 4/4 red |

7. **Checkpoint rerun (ONE run, one build, tip, server2/Linux).** Run 20261030-205522-2118, plan extract .antiphon/review-db82c1e8-plan.md. Validate result: `CHECKPOINT SOURCE VALID source=9fd5b4ac7… rows=8`, sourceState=clean, buildSource=verified.

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-15 | 12 | 12 | 0 | 0 |
| CP-16 | 44 | 44 | 0 | 0 |
| CP-17 | 22 | 22 | 0 | 0 |
| CP-22 | 36 | 36 | 0 | 0 |
| CP-23 | 167 | 167 | 0 | 0 |
| CP-26 | 59 | 59 | 0 | 0 |
| CP-28 | 382 | 382 | 0 | 0 |
| CP-92 | 6 | 6 | 0 | 0 |

   Total: 728/728, 0 failed, 0 skipped (the seven priority rows are 716).

   Extra build-slot runs on a scratch tip build:
   - 4 probes plus CapacityLockOrderTests: 21/21.
   - TerminalRunnerSeatReleaseTests Cancellation: 1 failed at the tip, 1 passed at base (D-1).
8. **Merge trials and pick list.**
   - `merge-tree origin/master (31f990317) HEAD` is clean, tree 981277e650a4bfbda3214a5b762efa8ee1f8f124.
   - Master's newest migration is 20261028170715, so 20261030114738 and 20261030143219 apply next in order. CARD-1619's 20261030141651 is not on master and must be regenerated after 20261030143219, as assumed.
   - origin/rc/2026-10-27 is now f0caf7762 (335b4fbb3 is its ancestor). The whole-branch merge-tree conflicts in 20 files, including S1's FailedTaskSessionFacts add/add. The rc port is separate cherry-pick work and was not simulated here.
   - Pick for a master land (S2+S3 together; skip docs/report-only commits b588cb5d0, be2e50a02, bf382e68c, ea232bbdf, 4af8050b1, f66dc8bdd, 0219ccebf, b4cea3d0e, 6fb9409eb, 9fd5b4ac7): 7d41e78f1 a5b9edb00 6654959c0 ad0f59d99 d60815339 6e02cde73 a914d2120 7ad98a0cb cf80deb58 7b798c1a1 77c0d1373 05468106d d1fec0c4d ab77fabdd 6334cada8 386acf115 d248ec616 2f46d43e6 f106d4fde 463d4027e 94aee9c42 4df55274c d7f51c2ec e34fc0742 5039ccb73 d5bda599b bbd0a546e d36e79f43 192f642b2, then the D-1/D-2/D-3 fixes. The plan doc edits ride in some of these; landing by rebase of the branch is simpler than picking.
   - Evidence guard: `check-evidence-diff.ps1` reported commits=39, violations=0.

## Advisories
- A-1 (performance): TaskDeadlinePolicy.cs:202 now calls `IsWorkingAsync` on every evaluation with a last entry. Before, this happened only past the phase preview fraction. That adds one query per open task per sweep and per projection. Measure, or batch it through `IsWorkingBatchAsync`.
- A-2 (isolation): CompletionNoteWorkHostedService.cs:95-96 runs supervision recovery first, outside the per-task try. Any throw there skips the SourceLanding completion-note recovery for that pass, and a deterministic poison row repeats every sweep. Wrap it separately.
- A-3: the successor fence counts same-card/role descendants of the held task itself as "successors". Exclude rows whose RootTaskId or ParentTaskId lineage is the held task.
- A-4: TaskSupervisionNotices has no index on SessionId or RecipientSessionId, which the retention anti-joins use.
- A-5 (disclosure): the checkpoint CLI bootstrap (`dotnet run --project tools/Antiphon.Checkpoints -- --help`, about 5 seconds) ran outside a build slot, because it is needed to start the tool. All other builds and tests took slots.

Cleanup: the scratch probe and base worktrees (with their bin-rdb82*/ outputs) were removed through `git worktree remove` on exact paths. No bin-c1610*/bin-rdb82* remains in the checkout. Checkpoint receipts are retained and gitignored.
