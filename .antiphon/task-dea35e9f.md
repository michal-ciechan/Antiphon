# CARD-1610 S2+S3 fix re-check (Review dea35e9f)

Subject: origin feat/card-task-52f54ede at a54e6dd9eba03a9a610deb11cb7d6d3f2ba4bd99 (Code owner task 52f54ede). Fix range fbc03d16a..a54e6dd9e. Prior review: db82c1e8.

**Verdict: NOT landable yet.** The fix closes the earlier review's D-1 (Cancel gate), D-3 (outcome-flip witness) and the unbounded part of D-2. It also adds one new product defect in the D-2 fix: the report watch closes on the very first scan after `EndedAt`. A late report whose transcript rows are stored after the exit is recorded is then dropped (probe red, below). The hold, phase, ceiling and counting semantics did not change and are green.

## Defects

### D-1 (new, product, S3): watch closes at session end with no transcript-drain grace, so late reports stored after the exit are dropped
- **Where:**
  - server/Application/Services/LateFailedReportForwarder.cs:18-28: `ended` = EndedAt set and Stopped/Failed. The first scan with no candidate then calls `CloseReportWatch("final_transcript_scanned", deliveryImpossible: true)`.
  - server/Application/Services/TaskSupervisionNoticeWriter.cs:61: a new watch is due immediately (`NextReportScanAt = now`).
  - The Failed settlement then calls `SupervisionChanged` → `Recovery.Check`, which runs the targeted pass at once.
- **Failure:** a task fails because its session exited, or the session exits soon after failing. The targeted pass runs in the same second and sees `ended`. It scans the rows *stored so far*, finds no report and closes the watch durably. If the runner's last transcript rows are persisted after that, nothing ever scans them again: the closed watch is out of the due set and is never reopened. Those rows can be the final report turn, delayed by remote (server2) phone-home ingestion lag or by a reconnect replay after transport loss. That is exactly the "report arrived after the Failed verdict" case S3 exists to forward.
  - Probe `Probe_rdea_report_rows_stored_after_end_are_still_forwarded` (scratch build of the tip): session Stopped/EndedAt=now, `RecoverAsync`, then 5 s later the report rows land (`h.FinishAsync`), then +61 min `RecoverAsync`. Result: **red**, `LateFailedParent` exists `should be True but was False`.
  - The author's own witness `C1610_Failure_watch_closes_without_a_report` closes at the same clock instant as EndedAt, so it encodes zero grace.
  - Before the fix the watch never closed, so this report would have been forwarded on a later scan. This is a regression introduced by the D-2 fix.
- **Why:** the repository already states that terminal status is not transcript-EOF evidence. docs/session-runtime-invariants.md (CARD-0519 S10): "a terminal session status alone does not close outbound recovery windows". Only a generation-bound `TerminalComplete` snapshot is EOF evidence, and legacy/unreachable runners leave windows open. "Final stored transcript window" needs the same proof.
- **Fix:** close on `ended` only with EOF evidence. Use one of:
  - (a) a generation-bound `TerminalComplete` snapshot, as in CARD-0519 S10; or
  - (b) a drain rule: close only on a scan at least N minutes after `EndedAt` (e.g. ≥ 2 backoff steps or ≥ 15 min, to be stated in the doc) that saw no frontier growth since a previous post-`EndedAt` scan.
  
  Keep the legacy-identity and superseded closes as they are. Add a witness: rows stored after EndedAt but before the drain bound are forwarded. Keep the existing "ended, no report: closes" arm, advanced past the bound.

## Brief checks

1. **Earlier D-1 (Cancel gate): CLOSED.**
   - `CancelAsync` (AgentTaskService.cs:2974-2988) holds the queue gate only around `CancelCoreAsync`: reload, attempt check, stop, Canceled save. It then releases the gate and calls `AfterCancelCommitAsync`, which does seat release, workspace release and publish.
   - CP-93 `TerminalRunnerSeatReleaseTests/*` 41/41, including `Cancellation_reconciles_without_second_stop`.
   - CP-94 `CapacityLockOrderTests` 17/17. CP-95 `WorktreeRetirementRaceTests` 54/54.
   - CP-16 49/49 includes the Continue-races-Cancel and two-concurrent-Continues witnesses.
   - Other gate holders added on the branch:
     - `HoldCeilingLockedAsync` (AgentTaskDispatcher.CeilingHold.cs:40) and `AnswerCeilingAsync` (AgentTaskReplyService.CeilingHold.cs:42): neither calls terminal seat or workspace release under the gate.
     - The only other new `GetLock` is Cancel's.
   - Lock order stays settle gate → queue gate. Cancel takes the queue gate only.
   - The `KillAsync` that Cancel runs under the gate does not take the queue gate. Its exit-path probes run on other threads and only wait.
2. **Earlier D-2 (unbounded watch): PARTLY CLOSED.**
   - Unknown identity is not watchable: the writer closes it at creation, and recovery closes pre-existing null or default rows as `legacy_identity_unknown`.
   - The backoff is bounded and documented: 1/2/4/8/15/30 then 60 min, checked on the 15-min sweep.
   - Closed watches leave both `due` and `Unresolved()`. CP-97 6/6 shows real pruning resumes. `C1610_Closed_watch_backlog_cannot_starve_a_new_late_report` is green.
   - Probe: alive session, 6 days of recovery passes. The watch stays open, `Unresolved`, next scan ≤ 60 min. A late report written then is forwarded and the task stays Failed. **Green.**
   - Probe: report stored before end, then ended. Forwarded, closed `report_forwarded`, unprotected. **Green.**
   - Probe: report rows stored after end. **Red** (new D-1 above).
   - **Resumed successor:** a closed watch is never reopened, and a resumed generation is not attributed (witnessed by the test's resume arm). A late report written after a resume is not forwarded. That is acceptable: a resume changes `StartedAt`, so `SourceCurrentAsync` fails, and Retry or Reply makes a new attempt with its own settlement.
   - **Live sessions:** no real late report from a live session is dropped. The cost is latency: up to about 75 min once backoff reaches 60 min (60 min + 15-min sweep).
3. **Earlier D-3: CLOSED.** I re-ran M3 (late note confirm flips Status to Succeeded) on an env-switched scratch build. `C1610_Failed_late_report_parent_has_complete_UserPrompt` went 3/3 red at `after.Status should be Failed but was Succeeded`.
4. **Advisories from db82c1e8:**
   - **(a) A-1 not taken.** TaskDeadlinePolicy.cs:202 still calls `IsWorkingAsync` on every evaluation. No rejection reason is recorded: the plan has none and the author report `.antiphon/task-52f54ede.md` is not on the branch.
   - **(b) A-2 taken.** Supervision recovery runs in its own DI scope and is caught. Completion recovery continues, and the error is rethrown after (`C1610_Supervision_fault_does_not_skip_completion_recovery` green). The wrap is per pass, not per item; see N-2.
   - **(c) A-3 taken and sound.** The descendant closure plus `RootTaskId != held.Id` excludes the held task's own children. A genuine replacement gets its parent's root or its own id (AgentTaskService.cs:1437), so it is still counted. CP-16 is green.
   - **(d) A-4 taken.** Indexes on SessionId and RecipientSessionId, plus a filtered due index (`Purpose = 1` = FailureDelegate, verified against the enum).
5. **Migration 20261030214946:**
   - Up adds 4 nullable columns and 3 indexes. Down drops all of them. Purely additive.
   - The table comes from this same unlanded series (20261030114738/143219), so on deploy it is empty and the index builds are instant.
   - Steady-state size is about one FailureDelegate row per Failed attempt (the backfill covers all historical Failed tasks, so thousands to tens of thousands), plus one row per hold episode and one per forwarded late report. A later non-concurrent CREATE INDEX would block writes for well under a second.
   - Master's newest migration is 20261028170715, so the order is clean. CARD-1619's 20261030141651 is on rc, and on master it must be recreated after 20261030214946.
6. **Unchanged deadlines:**
   - TaskDeadlinePolicy and CeilingHoldFacts are byte-identical across the fix range. The server tree at adf841712 equals the tip, so the seven mutants are fully restored.
   - CP-15 12/12, CP-16 49/49 (`Phase_beats_ceiling`, `Held_task_keeps_phase_supervision`, `Final_cap_requires_operator_extend`, `Hold_keeps_occupancy`).
   - CP-22 36/36, CP-25 39/39, CP-38 13/13, CARD-0079 CP-90 11/11 and CP-91 2/2.
7. **Merge trials:**
   - `merge-tree origin/master (cbe3e6b42) HEAD`: clean, tree 7b89014739345a9a962752641d4ed0937e10b3c5.
   - origin/rc/2026-10-27 is now ac6bd1b5b (f0caf7762 is an ancestor, so top-up 44 has landed). A whole-branch merge conflicts in 33 files, as expected for a master-based branch. The rc port is separate cherry-pick work and was not simulated.

## Non-blocking notes
- **N-1.** On an ended source, an oversized or over-limit report window (`late_report_window_unavailable` / `_limit`) has its ReasonCode overwritten to `final_transcript_scanned`, so a report that may exist reads as none. Keep a distinct close reason.
- **N-2.** The supervision pass is wrapped as a whole. A deterministic poison row is first in the stable `due` order and nothing is saved on a throw, so it starves the whole supervision batch every sweep. Wrap per item, or back off the failing row.
- **N-3.** The scale witness seeds only *closed* watches. Open live watches are bounded by session lifetime (S1 holds the seat) and by the 60-min cap. With Take(24) per sweep, about 96 scans per hour are available.
- **N-4 (disclosure).** The checkpoint CLI bootstrap (`dotnet run --project tools/Antiphon.Checkpoints -- start`) built the tool outside a slot, because it is needed to start the tool. Every test build and run took a slot.

## Evidence
- **Checkpoint run** 20261030-224228-b692, extract `.antiphon/review-dea35e9f-plan.md` (rows CP-15/16/17/19/22/25/38/90/91/92/93/94/95/97 from the plan).
  - One build, `unlisted: none`, 14 green, 0 red, 0 skipped. 335 tests: 12+49+27+22+36+39+13+11+2+6+41+17+54+6.
  - `validate`: `CHECKPOINT SOURCE VALID source=a54e6dd9e… rows=14`, sourceState=clean, buildSource=verified.
  - `wait` from the shadow copy exited 2 only on `TOOL CLEANUP Retained … current-executor-image`. The report verdict is GREEN exit=0.
- **Scratch build-slot runs** (detached worktree at the tip, bin-rdea/):
  - probes: 3 total, 2 passed, 1 failed (D-1).
  - M3: 3 total, 3 failed (killed).
  - The scratch worktree was removed with `git worktree remove` on its exact path. No bin-* remains in the checkout.
- **check-evidence-diff** 698bf1b73..a54e6dd9e: commits=52, violations=0.
- **Scope:** narrow re-check as the brief commissioned. The whole Unit lane and the other plan rows were not rerun, so ordinaryScopeCompleted is Interim.
