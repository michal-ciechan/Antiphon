# rc/2026-10-20 Windows qualification: interim failure census and triage

Investigate task 77c15d47, 2026-10-22, read-only on the desktop. Candidate
`rc-2026-10-20-ceb6e3d6b784`, sha `ceb6e3d6b78471db6831258699c493b8eb2d619b`, run
`27a1b370ab984049a08da85a184939e5`. Run directory
`C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784\logs\2026-10-22-0157-27a1b370`.
TRX, `*-tests.log` and the group 10/16 diag/stdout files were copied to the session scratchpad
and parsed there; nothing in the run directory was opened for write. Code was read at the rc tip
(this worktree, `ceb6e3d6b`) and compared with `origin/master` `e0080cfc6`.

State at 07:25Z: 15 antiphon launches finished (groups 1-9, 11-15; 14 TRX), group 10 was
watchdog-killed at 60 min, group 16 running since 06:54Z with 6 failures already in its stdout.
The harness never raised a hang flag (supervisor heartbeat `idleMinutes` 0.0 throughout).

The run cannot satisfy the Windows gate (group 10's kill), so the census below is the deliverable.
Nothing was rerun; every verdict is reconstructed from the TRX message/stack, the test and product
source at the rc tip, and the predecessor monitor notes.

## 1. Every failed test so far (56 results + 1 watchdog kill)

Groups 1-8: 1051 tests, all green. Counts per red group:

| Launch | Total | Failed | Start-finish (Z) | Classes |
|---|---:|---:|---|---|
| 9 | 123 | 6 | 02:50:58-02:53:11 | SessionMessageQueueDeliveryVerificationTests |
| 10 | 158 planned | killed | 02:53:12-03:53:02 | AgentTaskLandCheckpointMatrix / PersistenceFailure / Publication (97 passed, 0 failed, 1 in flight) |
| 11 | 120 | 4 | 03:53:20-04:29:32 | 4 land classes |
| 12 | 623 | 5 | 04:29:39-04:41:32 | 5 classes |
| 13 | 174 | 4 | 04:41:36-05:36:08 | 7 classes |
| 14 | 686 | 18 | 05:36:16-05:58:54 | 10 classes |
| 15 | 581 | 13 | 05:59:01-06:54:04 | 14 classes |
| 16 | in flight | 6 so far | 06:54:14- | AgentTaskLandSourceFreshnessTests and others |

Message heads are verbatim from the TRX `Message` element, trimmed. Cause letters refer to section 2.

| # | Launch | Class | Method | Message head | Cause |
|---|---|---|---|---|---|
| 1 | 9 | SessionMessageQueueDeliveryVerificationTests | C561_a_persist_failure_older_than_the_attempt_does_not_excuse_the_verdict | h.Adapter.Killed should be True but was False | F |
| 2 | 9 | SessionMessageQueueDeliveryVerificationTests | C561_a_no_composer_evidence_verdict_with_a_mark_keeps_the_card_0103_rules | h.Adapter.Killed should be True but was False | F |
| 3 | 9 | SessionMessageQueueDeliveryVerificationTests | C561_a_mixed_batch_takes_the_destructive_default | h.Adapter.Killed should be True but was False | F |
| 4 | 9 | SessionMessageQueueDeliveryVerificationTests | C561_after_a_refund_the_stranded_sweep_delivers_once_the_store_recovers(recovered) | Inputs.Count("the brief that must persist") should be 1 but was 0 | F |
| 5 | 9 | SessionMessageQueueDeliveryVerificationTests | C561_after_a_refund_the_stranded_sweep_delivers_once_the_store_recovers(still-failing) | same | F |
| 6 | 9 | SessionMessageQueueDeliveryVerificationTests | A_working_session_is_not_killed_when_the_record_never_arrives | h.Adapter.Killed should be False but was True (never abort a live turn over a bookkeeping doubt) | B? |
| 7 | 10 | AgentTaskLandPublicationTests (+2 classes) | watchdog kill while C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(directory, False) was in flight | no TRX; 97/158 nodes passed, 0 failed | G |
| 8 | 11 | AgentTaskLandBoundaryTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(BeforeRebaseIntent, dirty, False) | op should not be null but was (13.4 s) | H |
| 9 | 11 | AgentTaskLandBoundaryTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(BeforePushIntent, staged, False) | op should not be null but was (15.7 s) | H |
| 10 | 11 | AgentTaskLandBoundaryTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(RebaseStarted, staged, False) | op should not be null but was (13.8 s) | H |
| 11 | 11 | AgentTaskLandRemovalMatrixTests | C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries(2, submodule) | fired should be True but was False (25.5 s) | H |
| 12 | 12 | TurnEndGapRestartDeliveryTests | C1205_Watchdog_delivers_while_event_stream_remains_healthy | RecoveryOutcomes.Last() should be "CheckedWorking" but was "Unknown" | B? |
| 13 | 12 | TurnEndGapRestartDeliveryTests | C1205_Restart_delivers_five_gap_held_completions_once(False) | Caller.SubmittedBodies.Count should be 5 but was 0 | B? |
| 14 | 12 | TurnEndGapRestartDeliveryTests | C1205_Restart_delivers_five_gap_held_completions_once(True) | same | B? |
| 15 | 12 | TurnEndGapRestartDeliveryTests | C1205_Busy_tail_keeps_completion_obligations_pending | RecoveryOutcomes.Last() should be "CheckedWorking" but was "Unknown" | B? |
| 16 | 12 | AgentTaskReplyIntegrationTests | continue_replays_standing_authority_on_a_question_block | queued.Body should contain "Continue. Your caller's standing authority: ... proceed\nwithout further approval..." but was "[antiphon-task:b10c1c98]\nContinue. ..." | C |
| 17 | 13 | AgentTaskLandCleanupSafetyTests | C448_V18_CleanupRetryPreservesChangedWork(unregistered) | retry.Mode should be CleanupRetry but was ResumePublication | D |
| 18 | 13 | CheckNoteDeliveryHandoffTests | A_digest_note_reaches_a_busy_recipient_as_one_whole_prompt_after_recovery | CheckRow.DeliveryAttempts should be 1 but was 0 | B |
| 19 | 13 | CheckNoteDeliveryHandoffTests | A_dispatched_interpretation_reaches_a_busy_recipient_whole_after_recovery | CheckRow.DeliveryAttempts should be 1 but was 0 | B |
| 20 | 13 | VerificationRoundDeliveryTests | C544_BriefHandoffRecovery | task.Status should be Dispatched but was Blocked: warm inline busy=False: dispatch_brief_evidence_uncertain | F |
| 21-23 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(Prepared, untracked/staged/dirty, False) | op.RemoteConfirmedAt should not be null but was | A |
| 24 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_VerificationCannotFreezeOldTaskCoordinates(repository) | LastReason should be "task_coordinates_changed" but was "repository_lease_required" | A2 |
| 25-26 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_VerificationCannotFreezeOldTaskCoordinates(source-path / source-ref) | LastReason should be "task_coordinates_changed" but was "pending_operation_coordinates_changed" | A2 |
| 27-28 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(RebaseStarted, untracked/staged, False) | op.RemoteConfirmedAt should not be null but was | A |
| 29 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_VerificationCannotFreezeOldTaskCoordinates(target) | LastReason ... was "pending_operation_coordinates_changed" | A2 |
| 30-32 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(remote, dirty/staged/untracked, False) | op.RemoteConfirmedAt should not be null but was | A |
| 33 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(RebaseStarted, dirty, False) | same | A |
| 34-36 | 14 | AgentTaskLandBoundaryControlledTests | C448_V10_EachAcknowledgedBoundaryRechecksSource(BeforeRebaseIntent, untracked/dirty/staged, False) | same | A |
| 37 | 14 | AgentTaskWorktreeBaseResolverTests | T0442_V02_tip_order_is_ancestry_then_equal_tip_label(newer_review_at_ancestor) | selection.Decision should be Continue but was Unknown (19.1 s) | E |
| 38 | 14 | AgentTaskWorktreeBaseResolverTests | T0442_V06_open_writer_cannot_be_inherited(dispatched_original) | refused.Reason should be "requested_source_invalid" but was "inspection_timeout" (21.4 s) | E |
| 39 | 15 | DispatcherSiblingInspectionSchedulingTests | C1349_StaleInspectionCannotDispatch(adopted checkout) | `worktree list --porcelain` lines equal to "worktree <adopted>" should be 1 but was 0 | H |
| 40 | 15 | PostLandMutationDeliveryTests | C478_V09a_LandCrashMatrix(lost-wakeup, True) | h.Adapter.Inputs should be empty but had 2 items (the land receipt was typed) | B |
| 41 | 15 | PostLandMutationDeliveryTests | C478_V09a_LandCrashMatrix(queue-inserted, True) | h.Adapter.SubmittedBodies should contain "[land ... outcome=Landed] ..." | B |
| 42 | 15 | PostLandMutationDeliveryTests | C478_V09a_LandCrashMatrix(before-enqueue, True) | same | B |
| 43 | 15 | PostLandMutationDeliveryTests | C699_Startup_pages_past_a_full_batch_without_skipping_owed_tasks | TimeoutException: stamping the first page must not skip the next page (1 m 56 s) | E |
| 44 | 15 | PostLandMutationDeliveryTests | C478_V09c_SettlementCrashMatrix(before-submit, True) | adapter.Inputs should be empty but had 2 items (the completion receipt was typed) | B |
| 45 | 15 | PostLandMutationDeliveryTests | C478_V09c_SettlementCrashMatrix(after-receipt, True) | same | B |
| 46 | 15 | PostLandMutationDeliveryTests | C478_V09c_SettlementCrashMatrix(after-prompt, True) | same | B |
| 47 | 15 | PostLandMutationDeliveryTests | C478_V09b_WorkerCrashMatrix(before-submit, False) | ValidationException: One or more validation errors occurred (AgentTaskService.CreateCoreAsync via PostLandMutationWorld.CreateAsync:820) | H |
| 48 | 15 | PostLandMutationDeliveryTests | C478_G138_LandBusy | h.Adapter.Inputs should be empty but had 2 items | B |
| 49 | 15 | PostLandMutationDeliveryTests | C478_CompletionReplayAfterPartialEnqueueRetention | SessionQueuedMessages for the task should be 0 but was 1 | H |
| 50 | 15 | PostLandMutationDeliveryTests | C478_G153_CompletionBusy | adapter.Inputs should be empty but had 2 items | B |
| 51 | 15 | PostLandMutationDeliveryTests | C478_G136_LandQueueKey | h.Adapter.SubmittedBodies should contain "[land ...]" | B |
| 52 | 16 | AgentTaskLandSourceFreshnessTests | C488_LocalIdentityBoundaryMatrix | op.Publication should be Landed but was Refused: verification_tracked_tree_changed | A |
| 53 | 16 | AgentTaskLandSourceFreshnessTests | C488_DetachedFollowUpPublishesReviewedFix | op.VerifiedSourceSha should not be null: reason=verification_failed; verifier=tests failed; calls=1 (3 m 08 s) | E2 |
| 54 | 16 | AgentTaskLandSourceFreshnessTests | C494_DetachedFixVerifierFailurePreventsPublication | DirectoryNotFoundException ...\verify-artifacts\<id> (1 m 34 s) | E2 |
| 55 | 16 | AgentTaskLandSourceFreshnessTests | C488_DirtySourceFfRefuses | Landed expected, Refused: verification_tracked_tree_changed | A |
| 56 | 16 | AgentTaskLandSourceFreshnessTests | C488_SourceSequencerFfRefuses | Landed expected, Refused: verification_sequencer_changed | A |
| 57 | 16 | AgentTaskLandSourceFreshnessTests | C488_PostFfDirtyRefuses | Landed expected, Refused: verification_tracked_tree_changed | A |

Group 16 rows come from `antiphon-all-group-16-tests.log.stdout.tmp` at 07:25Z and will grow.

## 2. Cause groups and triage status

Every class above is **NEW relative to the 2026-10-21 triage**
(`docs/investigations/2026-10-21-rc-2026-10-20-windows-qualification-reds.md`): that run never
reached these classes (its section 5 lists `SessionMessageQueueDeliveryVerificationTests` and
`AgentTaskReplyIntegrationTests` among the pending heavy classes, and none of the 14 classes red on
10-21 appears here). The 10-21 causes A-H (PATH ceiling, evidence pack, pins, KeepSix, bridge
harness) are all green or excluded in this run: groups 1-8 are 1051/1051.

| Cause | Reds | Kind | On master? | Load or deterministic |
|---|---:|---|---|---|
| **A. CARD-1379 post-window source-worktree validation refuses what CARD-0688 D-2 made legal** | 17 (A) + 4 (A2) | product contract change, tests encode the superseded rule (same family as CARD-1449's C688 finding) | **not fixed**: `AgentTaskLandBoundaryControlledTests.cs` and `AgentTaskLandSourceFreshnessTests.cs` are byte-identical on master; CARD-1449's fix touched only the Mode relabel, C475 and C688 | deterministic (every dirty/staged/untracked case of four boundaries, every C488 residue test) |
| **B. Fixtures that mark a session Working/idle with a raw transcript insert, while the queue reads the loaded `SessionStateStore` projection** | 11 confirmed (B) + 11 plausible (B?) | stale test fixtures (the CARD-1373 class of defect; CARD-1373 repaired only `BridgeQueueHarness.MarkWorkingAsync`, not the independent helpers nor raw `TurnEnd` inserts) | **not fixed**: the five test files are identical on master | deterministic for B (all eight `busy=True` PostLand cases red, every `busy=False` case green) |
| **C. Reply body line endings** | 1 | Windows-only assumption: a raw string literal under `core.autocrlf=true` carries CRLF, the stored body is LF-normalised | not fixed (test and `BlockedNote.cs` unchanged on master) | deterministic on a CRLF checkout |
| **D. Fresh land relabelled ResumePublication on the ticketed resume** | 1 | product reporting regression, CARD-1449 | **fixed on master** `6ee09ad07` (`ticket is null &&` guard, `AgentTaskLandingProtocol.cs:192`); not on rc | deterministic (also visible as `mode=ResumePublication` in every B receipt text) |
| **E. Timeouts in real-git inspections** | 3 (E) | timing under load | not applicable | load (5 s inspection deadline, sibling cases green) |
| **E2. Real verifier through the live build-slot broker** | 2 | environment: the test uses `SessionRunnerBuildSlotGate` against the desktop runner while `availableMb` was under the 6144 MB floor (CARD-1461) | CARD-1461 in progress | load (verifier ran 1.5-3 min and failed/produced no artifacts exactly in the 06:54-06:58Z memory-floor window) |
| **F. Unexplained delivery-verdict reds** | 6 | undetermined; plausibly B (store projection) or timing | unknown | rerun needed |
| **G. Group 10 watchdog** | 1 kill | sizing: 158 land nodes estimated at 900 s took >3600 s, no hang | CARD-1404 (Review) / CARD-1410 | load-sensitive pacing, not a test defect |
| **H. Single-case reds whose matrix siblings passed** | 6 | undetermined | unknown | likely timing; rerun needed |

Master commits named in the brief and whether they bear on this run: CARD-1414/1415/1416 are on
rc already (their classes are green here). CARD-1440 (TranscriptHotPathQueryTests,
WorkingTranscriptRecoveryTests C1205 replace-frontier) and CARD-1452 (C100 migration pin) concern
classes not yet reached. CARD-1456 (RR_V4_RemoteReadFailure) and CARD-1449's RR/C475/C688 cases
belong to `AgentTaskLandRefusedRetryTests`, `LandingProtocolHarnessTests` and
`AgentTaskLandPublicationTests`: the first two are not yet reached; the third was inside group 10 and
its C688 case had not run when the kill came.

## 3. Mechanisms for the NEW groups

### A. CARD-1379 validates the source worktree after the verification window (21 reds)

- `server/Application/Services/AgentTaskLandingProtocol.cs:622-630` (`ValidateTicketAsync`,
  rc tip): for every path in `{land worktree, SourcePath}` when `ticket.SourceCanonical` is
  non-null, it runs `RequireNoIndexLockAsync`, `HasActiveSequencerAsync`
  (`verification_sequencer_changed`) and `status --porcelain=v1 -z --untracked-files=no`
  (`verification_tracked_tree_changed`). The source worktree is the live task checkout.
- The tests assert the CARD-0688 D-2/D-12 rule that the task worktree's tree is residue, not a
  precondition: `tests/Antiphon.Tests/Application/AgentTaskLandBoundaryControlledTests.cs:174-181`
  (`op.RemoteConfirmedAt.ShouldNotBeNull()` for `dirty/staged/untracked/switch`, cleanup
  `Refused`, `LastReason == source_dirty`) and
  `AgentTaskLandSourceFreshnessTests.cs:900-909` (`AssertLandedWithSourceResidueAsync`: Publication
  `Landed`, Cleanup `Refused`, LastReason = residue). The TRX shows `Refused` with
  `verification_tracked_tree_changed` (C488_DirtySourceFfRefuses, C488_PostFfDirtyRefuses,
  C488_LocalIdentityBoundaryMatrix) and `verification_sequencer_changed`
  (C488_SourceSequencerFfRefuses). The C448_V10 cases fail one assertion earlier
  (`RemoteConfirmedAt` null) so their `LastReason` is not in the TRX.
- Origin: the CARD-1449 investigation (`docs/investigations/2026-10-22-card-1449-land-protocol-base-reds.md`,
  "C688 (CARD-1449)") traced this validation to `f612cbe60` (CARD-1379; rc cherry-pick
  `20c38faa8`) and recorded the product caveat that D-2 excluded the source worktree; it re-budgeted
  only the C688 inventory test. The behavioural tests here were never re-examined.
- **A2** (4 reds, `C448_V10_VerificationCannotFreezeOldTaskCoordinates`): the verifier barrier
  changes a task coordinate during the window; the test expects `task_coordinates_changed`
  (`AgentTaskLandBoundaryControlledTests.cs:239`, product check at
  `AgentTaskLandingProtocol.cs:797`). Since f612cbe60 the windowed land re-enters `RunCoreAsync`
  with a ticket and the pending-operation identity check at `:828`
  (`pending_operation_coordinates_changed`) or the lease check at `:75`
  (`repository_lease_required`, for the `repository` case) fires first. Same outcome (refusal, no
  push), different reason string. Deterministic.
- Not a Windows assumption: nothing on this path is platform-conditional. These classes are
  `[Category("Slow")]` land tests outside the narrow checkpoint filters, which is why neither
  CARD-1379 nor CARD-1449 saw them (the CARD-1449 doc notes "Windows not run" and its Linux runs
  used three method filters).
- Untracked caveat: `--untracked-files=no` should ignore `new.txt`, yet the
  `(…, untracked, False)` cases are red too. Their refusal reason is not recorded; it may be the
  index-lock probe or the sequencer probe racing the fixture's own git in the source. Listed as an
  uncertainty, not a verdict.

### B. Working/idle marked by raw transcript inserts; the queue reads the store projection (11 + 11)

- `server/Application/Services/SessionMessageQueueService.cs:5402`
  (`ReadWorkingAsync`): `_states is { Enabled: true } ? _states.IsWorkingAsync(...) : IsWorkingAsync(db, ...)`.
  With the store registered (BridgeQueueHarness registers the singleton; CARD-1373 plan
  `docs/superpowers/plans/2026-10-20-card-1373-markworking-store-publish-plan.md`, ground-truth table)
  a transcript row written straight through EF is invisible until the store reloads.
  `FlushIfIdleAsync` (same file) calls `ReadWorkingAsync` and, seeing idle, types the row.
- CARD-1373 (on rc) repaired `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`
  `MarkWorkingAsync`/`MarkIdleAsync` (store `BeginWriteAsync` + `ReconcileAsync`) and explicitly
  left the "five independent helpers" alone (plan, "Every same-named helper" row).
- `PostLandMutationDeliveryTests.cs:1220` `SetWorkingAsync`: raw `db.TranscriptEntries.Add` of an
  AssistantText "busy" row (or a TurnEnd), `SaveChangesAsync`, no store publish.
  `ConfirmLandReceiptAsync` (`:~700-725`) calls it, then `FlushIfIdleAsync`, then asserts
  `h.Adapter.Inputs.ShouldBeEmpty("land-busy-does-not-submit")`. The TRX shows the receipt typed
  (two inputs: bracketed paste and Enter) in all eight `busy=True` rows (40-42, 44-46, 48, 50-51)
  while every `busy=False` sibling passed. `QueuedReceiptAssertions.cs:193` is the same helper
  shape.
- `CheckNoteDeliveryHandoffTests.cs:674-686`: marks Working through the repaired helper, then ends
  the turn with `h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, ...)` (raw, `:684`) instead
  of `MarkIdleAsync`, so the store still says Working, the flush withholds, and `DeliveryAttempts`
  stays 0 (`:686`; the sibling at `:494-496` is the same shape).
- **B?** rows (TurnEndGapRestartDeliveryTests "CheckedWorking" vs "Unknown" and 0 of 5 receipts;
  SessionMessageQueueDeliveryVerificationTests `A_working_session_is_not_killed`): the observed
  values are what a missing or stale store projection produces, but the harnesses
  (`TurnEndGapDeliveryHarness.cs`, the C561 blind harness) were not traced to a raw insert in this
  budget. Treated as the same family pending a rerun with the PostLand fixture repaired.
- Not Windows-specific and not load: the eight-of-eight / zero-of-eight split by `busy` is a
  deterministic signature. These files are unchanged on master, so master carries the same reds for
  these Slow classes.

### C. `continue_replays_standing_authority_on_a_question_block` (1)

- `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs:1115`
  `queued.Body.ShouldContain(BlockedNote.ContinueMessage(authority))`.
  `server/Application/Services/BlockedNote.cs:32-36` builds that message from a multi-line raw
  string literal ("... proceed" / "without further approval ..."); under `core.autocrlf=true`
  (this machine) the literal's line break is CRLF.
- The stored body is normalised: `AgentTaskReplyService.cs:473`
  (`$"{TaskMarker}\n\n{message.Trim()}".ReplaceLineEndings("\n")`) and
  `SessionMessageQueueService.cs:484` (`.Trim().ReplaceLineEndings("\n")`). The TRX expected text
  shows the break as a newline inside "proceed\nwithout"; the actual body starts with the task
  marker and is LF. On an LF checkout (Linux) both sides agree.
- Deterministic Windows-only assumption. Fix shape: compare with `ReplaceLineEndings("\n")` on
  both sides, or make the literal `\n`-explicit. Not fixed on master (both files unchanged).
- Uncertainty: I did not byte-compare `C:\Antiphon\releases\checkout\...\BlockedNote.cs`; the
  verdict rests on `git config core.autocrlf=true`, `text=auto` for the file, and the normalisation
  calls.

### D. `C448_V18_CleanupRetryPreservesChangedWork(unregistered)` (1)

- `AgentTaskLandCleanupSafetyTests.cs:162` expects `retry.Mode == CleanupRetry`; got
  `ResumePublication`. The relabel is `AgentTaskLandingProtocol.cs:191-192`
  (`if (op.SourcePinned || op.Phase != LandPhase.Inspected) op.Mode = ResumePublication;`), the
  branch CARD-1449 guarded with `ticket is null &&` on master (`6ee09ad07`, line 195 there).
- Only the `unregistered` variant is red; its siblings passed through the same assertion, so the
  relabel is reached only on that path's re-entry. Whether the master guard covers this entry was
  not proven here (the CARD-1449 report lists eight RR cases and says "rc red only at Mode"). The rc
  cherry-pick of `6ee09ad07` is the first thing to try; if it stays red it is a second entry path.
- Every B-row receipt text also prints `mode=ResumePublication` for a fresh land, the operator-facing
  symptom CARD-1449 describes.

### E. Inspection timeouts and `UntilAsync` timeouts (3) and E2 real verifier (2)

- `AgentTaskWorktreeBaseResolverTests` rows 37-38 ran 19-21 s and ended in `inspection_timeout` /
  `Decision.Unknown`; the resolver's inspection deadline is `GitSettings.WorktreeBaseInspectionTimeoutSeconds`
  (5 s default, CARD-1134). Twenty-plus sibling cases in the same launch passed. Load.
- `PostLandMutationDeliveryTests.C699_...` row 43: `UntilAsync` (`:1717`) timed out after 1 m 56 s.
  Plausibly load, possibly B (a paging pass withheld by a stale projection); undetermined.
- E2: `AgentTaskLandSourceFreshnessTests.ConfigureRealVerifier` (`:1165-1189`) wires
  `SessionRunnerBuildSlotGate` against `ANTIPHON_BUILD_SLOTS_URL` (the live desktop runner) and a
  real `LandingVerifier` (`server/Infrastructure/Git/LandingVerifier.cs:19-20`, "tests failed" is
  `result.Step + " failed"`). Row 53 ran 3 m 08 s and the fixture test run failed; row 54 ran
  1 m 34 s and never produced its artifacts directory. Both started inside 06:54-06:58Z, when the
  broker reported `availableMb` 5127 below `floorMb` 6144 with a `land-verify 0f9784a67773` lease
  active (task c58e21f5 `poll.log` 06:56:00Z) and the CARD-1461 investigation measured 220 of 356
  five-second samples under the floor in the preceding 30 minutes. The gate waits
  `BuildSlotWaitMinutes` then returns `Timeout` (`SessionRunnerBuildSlotGate.cs:62-65`). Load and
  environment: the test shares the production broker with the land verifier it competes with.

### F and H. Undetermined (12)

- F (C561 x5, VerificationRound C544): the kill/deliver verdicts are computed from captured
  generations and the working state; the values seen (`Killed` false when a kill is owed, 0 inputs
  after a refund, `dispatch_brief_evidence_uncertain`) are consistent with a stale working/transcript
  projection but no raw insert was traced in budget. Group 9 ran at 02:51Z, before any land-verify
  lease (first lease 05:52Z per `eta.md`), so load is unlikely for C561.
- H: `AgentTaskLandBoundaryTests` rows 8-10 (`op` null after the boundary fired; 3 of a 50-case
  matrix), `AgentTaskLandRemovalMatrixTests` row 11 (fence never fired for `(2, submodule)`),
  `DispatcherSiblingInspectionSchedulingTests` row 39 (adopted worktree missing from
  `worktree list`), `C478_V09b_WorkerCrashMatrix(before-submit, False)` (ValidationException while
  creating the task; the other matrix cells created theirs), `C478_CompletionReplayAfterPartialEnqueueRetention`
  (one leftover queue row; CARD-1120 closed this earlier). Each is one cell of a matrix whose
  siblings passed in the same process, which points at timing, but none was reconstructed.
  Rows 8-11 ran 04:06-04:25Z, before the first land-verify lease; rows 39, 47, 49 ran 06:17-06:44Z
  inside the memory-floor window.

### G. Group 10 watchdog kill

- `antiphon-all-group-10-execution/log_261022025311997.diag`: 97 nodes passed between 02:53:13Z and
  03:53:02Z, 0 failed, 98th (`C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(directory, False)`)
  started 03:53:02Z, last diag line 03:53:02Z = kill. Slowest nodes 220 s, 140 s, 135 s
  (`C448_V19_EveryPublicationRecoveryCutRejectsCrossedCoordinates`, `C448_F04_PushIntentAcknowledgement`).
  The group was progressing at every heartbeat; estimate 900 s versus >3600 s actual. This is the
  CARD-1404/1410 batcher sizing problem under this host's load, not a product or test defect.

## 4. Load versus deterministic

Host load windows (task c58e21f5 `eta.md` and `poll.log`, task a5303e1a `supervisor-console.log`,
CARD-1461): land-verify `0f9784a67773` held build-slot leases at 05:52, 06:11, 06:32Z with
available memory 2.1-3.6 GB, waited under the floor 06:35-06:55Z, held another lease 06:55-06:58Z;
free memory stayed 4.4-5.8 GB until 06:58Z and recovered to 9.4 GB by 07:06Z. The qualification's
own lease (f363397a) is single and never renewed, so the test host was unleased during all of it.

| Verdict | Rows | Evidence |
|---|---|---|
| Deterministic, independent of load | A, A2, B, C, D (36 rows) | whole matrices or whole `busy=True` halves red with siblings green; mechanisms are code paths with no timing; group 9's C561 and group 11-12's reds predate the first land-verify lease |
| Load-induced, plausible | E, E2, G (6 rows + the kill) | explicit `inspection_timeout`, `UntilAsync` timeout, real verifier through a broker under its memory floor at that minute, 60 min for 97 land nodes with no hang |
| Undetermined | F, H (12 rows) | single cells; five of them ran inside the memory-floor window, seven did not |

Expected on a quiet host: A-D stay red (36 of 57), E/E2/G go green, F/H need the rerun to tell.

## 5. Coverage by existing cards and proposed new cards

Searched with `card.ps1 search -Board Antiphon -All` for each class, reason string and symptom.

- Covered: G by CARD-1404 (Review) and CARD-1410; E2's environment by CARD-1461 (InProgress); D by
  CARD-1449 (landed on master, needs the rc pick). CARD-0630 (Backlog, PostLand receipt duplicates
  on a second ReconcileAsync) and CARD-1120 (Done) describe different PostLand failures and do not
  cover B. CARD-0739 (Backlog, ScaledTimeProvider for VerificationRound/CheckNote) is speed, not B.
  CARD-1388 (Done) added the `ValidateTicketAsync` refusal tests that A now contradicts.
- Not covered, filed by this task (see the report for the numbers):
  1. **Land refuses a dirty or sequencer-busy task worktree after the verification window; CARD-0688 D-2 tests red (21 cases)**.
     `AgentTaskLandingProtocol.ValidateTicketAsync` (`:622-630`) runs index-lock, sequencer and
     `status` probes in the source worktree since CARD-1379 and refuses with
     `verification_tracked_tree_changed` / `verification_sequencer_changed`. CARD-0688 D-2/D-12 made
     the task worktree's tree residue for guarded cleanup, and 17 behavioural tests
     (`AgentTaskLandBoundaryControlledTests.C448_V10_EachAcknowledgedBoundaryRechecksSource`
     dirty/staged/untracked x 4 boundaries, `AgentTaskLandSourceFreshnessTests.C488_*Refuses` and
     `C488_LocalIdentityBoundaryMatrix`) plus 4 reason-string cases
     (`C448_V10_VerificationCannotFreezeOldTaskCoordinates`) are red on rc and master. Decide the
     contract (drop the source-worktree `status`, or make it `--no-optional-locks` and reclassify
     dirt as residue, or accept the new rule and re-pin the tests), then fix one way. Observed in the
     Windows qualification of rc/2026-10-20 at ceb6e3d6b; platform-neutral.
  2. **Delivery fixtures mark Working/idle with raw transcript inserts; the queue reads the loaded store (CARD-1373 leftovers, 11+ reds)**.
     `ReadWorkingAsync` (`SessionMessageQueueService.cs:5402`) prefers the `SessionStateStore`
     projection; `PostLandMutationDeliveryTests.SetWorkingAsync` (`:1220`),
     `QueuedReceiptAssertions.SetWorkingAsync` (`:193`) and the raw `TurnEnd` inserts in
     `CheckNoteDeliveryHandoffTests` (`:684`, `:494`) never publish, so every `busy=True` PostLand
     case types the receipt into a busy session and both CheckNote busy-recipient tests never
     deliver after the turn ends. Route these helpers through `BridgeQueueHarness.MarkWorkingAsync`
     / `MarkIdleAsync` or the store lease, then rerun `TurnEndGapRestartDeliveryTests`,
     `SessionMessageQueueDeliveryVerificationTests` (C561 and `A_working_session_is_not_killed`)
     and `VerificationRoundDeliveryTests.C544` to see which of the 11 plausible rows clear.
  3. **`BlockedNote.ContinueMessage` assertion fails on CRLF checkouts**.
     `AgentTaskReplyIntegrationTests.cs:1115` compares the LF-normalised queued body
     (`AgentTaskReplyService.cs:473`, `SessionMessageQueueService.cs:484`) with a raw string literal
     from `BlockedNote.cs:32-36`, CRLF under `core.autocrlf=true`. Normalise both sides.

Not filed (covered or needs a rerun first): D (pick `6ee09ad07` to rc under CARD-1449), E/E2/G
(CARD-1461, CARD-1404), F/H (rerun the 12 cells on a quiet host; file only what stays red).

## Remaining uncertainties

- Nothing was rerun. The deterministic verdicts rest on whole-matrix signatures and code reading;
  a quiet-host rerun of the listed filters would confirm them in one pass.
- The `untracked` cells of A have no recorded reason (see A caveat).
- B? rows (TurnEndGap x4, C561 A_working_session, VerificationRound C544) were not traced to a raw
  insert; they may have other causes.
- CRLF verdict (C) was not byte-checked on the qualification checkout.
- Group 16 and later launches (155 remaining at 07:25Z) will add rows; `AgentTaskLandRefusedRetryTests`,
  `LandingProtocolHarnessTests`, `AgentTaskLandPublicationTests` (CARD-1449/1456) and
  `WorkingTranscriptRecoveryTests` / `TranscriptHotPathQueryTests` (CARD-1440) and the C100 pin
  (CARD-1452) are expected to add their documented reds unless the rc picks land first.

## Not done, noted

- Fix idea A: decide the source-worktree rule at `AgentTaskLandingProtocol.cs:622-630` (design decision).
- Fix idea B: a single store-aware helper for all SetWorking/MarkIdle fixtures (test-only, 30-60 min).
- Fix idea C: `ReplaceLineEndings("\n")` on both sides of the assertion (test-only, minutes).
- Fix idea D: cherry-pick `6ee09ad07` to rc/2026-10-20.
