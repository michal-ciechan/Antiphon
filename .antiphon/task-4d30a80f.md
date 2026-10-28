# Final Review 4d30a80f: CARD-1591 redo (subject 036ad60d)

Reviewed: feat/card-task-036ad60d at adb3787154d28519b0526192aa67b2ab590768e2 (base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50), server2/Linux.

Outcome: found 4 defects, so this cannot land. The write allow-set is unchanged and there is no new typing path. The problem is the attempt charge: it sets `DeliveryAttempts = 1` plus a start and floor on rows that were never typed. Many existing paths read `DeliveryAttempts == 0`, `NeverAttempted` or `DeliveryAttempts > 0` as a statement about typing, so those rows are now treated as typed.

## Priority 1: write allow-set (clean)

- `ProviderDialogObservationService.Refusal` and `CheckDialogInput` do not appear in the diff.
- The runner's `state`/`known` computation is identical. `Adopted`, `InputUncertain` and `ScreenState` are informational only.
- `DialogDeliveryAdmission.Describe` returns null exactly when `Refusal` is null. Both the code and the test's sample loop show this.
- `ModalBlockReasonAsync` and `IsModalBlockedLockedAsync` keep the same truthiness.
- The flush and SendNow still return the same withhold.
- Grepping the added lines for Send, Write, Input, Enter or paste found no terminal call.
- `ChargeDurableWithholdAsync` is a database UPDATE only, and `ListPendingNotesAsync` only reads.
- The runner witness confirms the claimed behaviour. Adopted idle, draft, Working and dialog screens each return ObservationUnknown with no input bytes. A known-history Working screen returns Written. A known dialog returns ModalBlocked.

## Defects

### D-1: a charged optional brief no longer expires, and is typed after its deadline
- **Where:** `SessionMessageQueueService.DialogAdmission.cs` (`ChargeDurableWithholdAsync`) together with `SessionMessageQueueService.cs:2208` (`CancelExpiredBriefsAsync` needs `DeliveryAttempts == 0`) and `:2235`/`:2249` (`CancelJustClaimedExpiredBriefsAsync` and `FirstInputDeadline` need `== 1`).
- **Failure:**
  1. An optional brief (`ExecutionTaskId` plus `ExecutionDeadlineAt`) is pending while the withhold is adopted, uncertain, screen-unknown or generation-mismatch. It is charged to 1.
  2. Its deadline passes. When the withhold clears, `CancelDeadBriefsAsync` skips it, the claim raises it to 2, and the just-claimed expiry check skips it too.
  3. The stale brief is typed and the Dispatched optional task is never canceled.
  4. At base the same brief is canceled as "Optional work expired before execution."
- **Why:** an attempt count is being used to mean "withheld".
- **Fix:** record the withhold in its own field (for example `WithheldAt`/`WithholdCount`) instead of `DeliveryAttempts`, `LastDeliveryStartedAt` and `LastDeliveryBaselineSequence`. Alternatively, gate every attempts==0/==1 predicate on "typed".

### D-2: charged notes block standing switch and automatic recovery
- **Where:** `StandingQueueSwitchPolicy.cs:8` (`NeverAttempted`), as used by:
  - `AgentControlService.cs:637`, which throws `standing_resume_delivery_pending`
  - `StandingRestartEscalationPolicy.CanTransfer`, which refuses supervised fresh recovery
  - `CheckCompactionContinuationService.cs:627/727`, which no longer fails stalled occupants or cancels a retired task's briefs
- **Failure:** the withheld notes on an adopted or unknown-observation orchestrator are charged. Switching the standing agent to another conversation is then refused, and so is automatic fresh recovery, until a human resolves the "attempted" input. At base those untouched notes transferred.
- **Why:** this is the exact recovery path for the stuck orchestrator CARD-1591 is about.
- **Fix:** the same as D-1. A withheld row must stay `NeverAttempted`.

### D-3: a charged standing-resume note gets a bogus NoTranscriptRecord verdict and an incident
- **Where:** `SessionMessageQueueService.ResumeReceipt.cs:65` (`CanWriteResumeInputAsync`) leads to `HandleDeliveryFailureAsync`.
- **Failure:**
  1. Right after a standing resume, the resume note (System, `RuntimeActionKey`) is withheld with GenerationMismatch or adopted. GenerationMismatch is charged.
  2. On the next flush the fence is Ready, the note has attempts 1 and no verdict, so a NoTranscriptRecord failure is adjudicated.
  3. That writes `DeliveryVerdict`/`DeliveryVerdictAt` onto a never-typed row and records an Error `DeliveryVerificationFailed` incident ("Message delivery could not be verified ... returned to the queue").
  4. The always-on kill is skipped only by accident: the charge leaves `LastDeliveryGeneration` null, so a second Warning says "Destructive recovery was declined".
- **Why:** a note that was never typed is treated as a failed delivery.
- **Fix:** the same as D-1, plus a witness for the resume note.

### D-4: the transcript floor lets a never-typed note be confirmed or parked by another prompt
- **Where:**
  - `SessionMessageQueueService.cs:3040`: `LateConfirmAttemptedMessagesAsync` now admits charged rows
  - `AgentTaskLandNotificationService.cs:~253`: the land-note receipt needs `DeliveryAttempts > 0`, which a charged row has, plus the floor the charge sets. `PendingNoteWithholdTests.AssertFloorAsync` asserts that `LandNoteReceipt.Prompts` is now non-null for a charged row.
- **Failure:** after the charge, any UserPrompt above the floor that contains the note's 200-character head does one of two things:
  - It marks the row Sent/LateConfirmed, or its land note Confirmed, although it was never typed. Examples are an identical earlier note typed in a previous run while 340 drain, or an operator quoting the note. That note is then lost.
  - If the head matches but the full body does not, it parks the row at MaxAttempts with a truncation incident.
- **Base:** untyped rows were never confirmed or parked from transcript text.
- **What the "transcript floor" is (brief item 5):** the `LastDeliveryBaselineSequence` and backdated start. It does not block a later real delivery, but it can wrongly discharge one.
- **Fix:** do not give a withheld row a delivery floor.

### Missing test
There is no producer-to-recipient witness for activation, i.e. what happens once the withhold clears. Nothing shows that each charged note is typed exactly once and confirmed by a real UserPrompt, rather than being late-confirmed, parked, expired or dropped. Nothing shows that expired briefs, the resume note and standing switch behave as at base.

## Brief questions

- **(2) Accounting.** The charge is idempotent: `WHERE attempts == 0` with `ExecuteUpdate`. Concurrent flushes, a second turn-end and SendNow cannot spin or double-charge, and the test confirms 1 stays 1.
  - **What happens to the 340-note backlog on the next flush:** the 338 untouched notes go to attempts 1, with start = generation − 1 µs and floor = the transcript max.
  - **What happens on activation:** each note first goes through late-confirm (D-4). The rest are delivered in head-run order with 2 attempts left. Expired optional briefs are typed (D-1).
  - **Ordering:** unchanged.
- **(3) Alert.**
  - Defaults: 900 s, at least 1 note.
  - Both settings are configurable; zero disables it.
  - It raises one Warning per session through `ConditionKey pending-notes-aged:<session>`, never one per note.
- **(4) Route.**
  - It reads only: the observation read is bounded at 2 s and nothing is charged.
  - It has the same exposure as the existing `GET /queue`, which already returns bodies.
  - The page is clamped to 1–100 rows with an exclusive `since` sequence. A session id from another agent returns that session's notes, just as `/queue` does.

## Advisories (do not block)

- **A-1:** `PendingNotesAged` has no session-status filter, so every Stopped or Failed session with Pending notes older than 15 minutes raises a Warning. The title says "withheld" even when there is no withhold (a busy queue). Each feed build scans every aged pending row in the fleet.
- **A-2:** with `MaxDeliveryAttempts = 1`, the charge parks every withheld note at once. The backdated `LastDeliveryStartedAt` (generation − 1 µs) then makes `ParkedMessageSweepService:61` eligible to discard System, Delegation, Check and Supervision notes.
- **A-3:** in the unobservable-baseline arm, start = now, so `DeliveryGenerationChanged` is false:
  - `CancelDeadBriefsAsync` keeps a settled task's untyped brief.
  - `HeldBackTypingBlocksTheComposerAsync` can hold delivery behind a row that was never typed.
- **A-4:** ScreenUnknown and GenerationMismatch are often transient (mid-redraw, resume lag), yet both are charged as "durable". That widens D-1 to D-4 beyond the adopted case.
- **A-5:** a page of 100 note bodies has no byte limit.

## Evidence (fresh, this session)

**Build:** `build-slot.ps1` `dotnet build` of Antiphon.Tests (222 s) and Antiphon.SessionRunner.Tests (15 s), with `OutputPath=bin-r4d30/`. Both exit 0. All 34 `bin-r4d30` directories were deleted afterwards.

**Server witnesses**, filter `/*/*/(DialogDeliveryAdmissionTests*)|(PendingNoteWithholdTests*)|(ClaudeDialogDeliveryTests*)/*`: total 4, passed 4, failed 0, skipped 0.

| Test | Duration |
|---|---|
| C1591_NamesEachWithholdAndNeverAllowsAWrite | 0.04 s |
| C1591_NamesWithholdsChargesOnceAndAlerts | 3.9 s |
| C680_QueueAndExpectationKeepDialogInputPending | 17.9 s |
| C680_AllAutomatedWritersUseTheGuard | 3.8 s |

No 53300 in the log.

**Runner witnesses**, `/*/*/ClaudeDialogInputGuardTests/*`: total 3, passed 3, including C1591_AdoptedIdleDraftDialogAndWorkingWriteNothing.

**Red witnesses** (temporary edits in this worktree only, restored and verified with `git diff --quiet`):

| Mutation | Result |
|---|---|
| A: `ChargeDurableWithholdAsync` made a no-op | C1591_NamesWithholdsChargesOnceAndAlerts failed: DeliveryAttempts expected 1, was 0 |
| C: runner `Adopted` forced false | C1591_AdoptedIdleDraftDialogAndWorkingWriteNothing failed: `dialog.Adopted` expected True, was False |
| B: `BuildPendingNotesAgedItemsAsync` returns empty | C1591_NamesWithholdsChargesOnceAndAlerts failed: `aged.Count` expected 1, was 0 |

**Client:** `test-client.ps1 src/features/attention/attentionVisuals.test.ts -TypecheckProject client/tsconfig.app.json` gave typecheck 0, lint 0, and vitest 1 file / 31 tests passed.

**Evidence diff:** `check-evidence-diff.ps1` over base..HEAD gave commits=2, violations=0.

**Not run:**
- The whole Unit lane and the full integration classes. The brief said "No whole Unit", so the scope is Interim.
- Checkpoint-tool receipts: the brief gives no plan table, so there are no CP-n rows to match.
- PCs remain pending.
