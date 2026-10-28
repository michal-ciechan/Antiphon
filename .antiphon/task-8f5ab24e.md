# Final Review 8f5ab24e: CARD-1591 round 2 (subject ec0aee49)

Reviewed: feat/card-task-ec0aee49 at ecce221dcf730658416da6eb3430d3d05ecaf49b (base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50), server2/Linux.

Outcome: found 1 defect, a regression. Adding the four withhold columns broke the CARD-1149/1153/1161 absent-launch hold through its closed column census. With that census mismatched, a never-launched delegation task now goes to Failed instead of being held Blocked with its original input. The round-1 defects D-1 to D-4 are fixed. The write allow-set is unchanged.

## Defect

### D-1: the new columns disable the absent-launch hold (fail closed to Failed)
- **Where:** `server/Application/Services/AbsentLaunchPolicy.cs:17` (`MessageColumns` census). It is read at `AgentTaskDispatcher.cs:~3504`, where `knownColumns = model properties SetEquals MessageColumns`, and gated by `IsNeverAttempted` (`KnownMessageColumns: true`).
- **Failure:** `SessionQueuedMessage` now has `WithholdCount`, `FirstWithheldAt`, `LastWithheldAt` and `LastWithholdReason`, which are not in the census. So:
  1. `KnownMessageColumns` is false on every evaluation.
  2. `HoldUnderLockAsync` returns NotThisShape.
  3. A Dispatched task whose session the runner never knew is failed, when it should be held Blocked with `dispatch_launch_absent` and its brief preserved.
- **Measured:** `DelegationDispatchRecoveryBoundaryTests` gives 41/381 red at the tip and 381/381 green at the base (same class alone, same host, same filter). Typical messages are "held.Status should be Blocked but was Failed" and "runner.Certifies should be >0 but was 0". The red tests are all C1149_*, C1153_* and two C1161_Dead_session_grace hold cases.
- **Also red:** `AbsentLaunchPolicyTests.C1149_Queue_columns_are_accounted_for` (1 of 124 in the census run). It asserts the census against the entity's properties, so it goes red by construction once the columns exist.
- **Why:** this is a recovery-path regression. A launch that never started loses its Blocked hold and its original-input custody. The census exists precisely to force the new columns to be classified.
- **Fix:**
  - Add the four columns to `MessageColumns`.
  - Classify them in `IsNeverAttempted`. A withhold record never types, so any withhold values should still count as never-attempted. If the author prefers, require `WithholdCount: 0`, but say which and why.
  - Re-run `DelegationDispatchRecoveryBoundaryTests` and `AbsentLaunchPolicyTests` in full.
  - Then grep for any other closed census over `SessionQueuedMessage`. `EntityScalarSnapshotTests` and `ChannelOutboundContractTests` were green.

## Priority 1: write allow-set and old-field readers (clean)

- **Allow-set unchanged:** `ProviderDialogObservationService.Refusal` and `CheckDialogInput` are not in the diff. The runner's `known`/`state` computation is identical, and `classified` is only added as informational `ScreenState`. The `Adopted`/`InputUncertain`/`ScreenState` fields are read only by `DialogDeliveryAdmission.Describe`, which returns null exactly when `Refusal` is null.
- **No new typing path:** the added lines contain no terminal Send, Write, Input, Enter or paste call. `ChargeDurableWithholdAsync` is one guarded `ExecuteUpdate` of the four new columns only. `ListPendingNotesAsync` only reads.
- **New fields have no delivery readers:** they are read only by `AttentionService.BuildPendingNotesAgedItemsAsync` and the pending-notes DTO.
- **Old fields untouched:** `DeliveryAttempts`, `LastDeliveryStartedAt`, `LastDeliveryBaselineSequence` and `LastDeliveryGeneration` are never written by the withhold. So expiry (`CancelExpiredBriefs`/`FirstInputDeadline`), `NeverAttempted`/`CanTransfer`, the resume receipt, late-confirm and the land-note receipt all see a withheld row exactly as at base. Round-1 D-1 to D-4 are closed in the code, not only in the tests.
- **Red witnesses** (temporary mutation that re-adds the first redo's attempt+start+floor charge after the withhold record; restored, `git diff --quiet` clean, rebuilt): 7 of the 8 `PendingNoteWithholdTests` went red, each on the expected assertion.

  | Test | Red assertion | Covers |
  |---|---|---|
  | NamesWithholdsChargesOnceAndAlerts | attempts 1 | |
  | ExpiredOptionalBriefCancelsAfterWithhold | Sent, not Canceled | D-1 |
  | WithheldNotesStayTransferable | NeverAttempted false | D-2 |
  | ResumeNoteIsNotFailedAfterWithhold | NoTranscriptRecord | D-3 |
  | QuotedHeadDoesNotConfirmOrParkAWithheldNote | left Pending/parked | D-4 |
  | ClearWithholdTypesEachNoteOnce | attempts 2 | |
  | OneAttemptCapDoesNotParkAWithhold | Parked | |

  `PendingNotePageBoundsBodyBytes` stayed green, as expected. No 53300 in any log.

## Priority 2: migration

- **Forward:** three nullable columns plus `WithholdCount integer NOT NULL DEFAULT 0`. A constant default is metadata-only on PG11+, so there is no table rewrite and only a brief lock. No data changes.
- **Snapshot:** the snapshot and Designer agree, and `AppDbContextModelTests` passed 3/3, including `Current_model_matches_migration_snapshot`.
- **Down:** drops the four columns and loses only the withhold record.
- **Live database:** not touched, per the brief. The first redo (a39d5c54/adb37871) is on neither origin/master nor origin/rc/2026-10-27, so no row was ever charged and the 340-row backlog is unaffected by provenance.

## Priority 3: accounting and activation

- **Writes:** one record per note, idempotent through `WithholdCount == 0 AND attempts 0 AND start/floor/verdict null`. Concurrent flushes, SendNow, turn-end, restart and generation change cannot spin or flood. Under READ COMMITTED, the second update matches 0 rows.
- **Activation witness** (`C1591_ClearWithholdTypesEachNoteOnce`): it goes through the real `FlushSessionAsync` to the fake adapter, and `OnSubmitted` writes complete UserPrompt plus TurnEnd transcript rows. Its 3 notes are typed once, in order, and confirmed Delivered with attempts 1. That is in-harness producer-to-recipient evidence. It is not a 340-note or a live runner run.

## Priority 4: alert and route

- **Alert:**
  - One Warning per session (`ConditionKey pending-notes-aged:<session>`).
  - Defaults are 900 s and 1 note; both are configurable, and 0 disables it.
  - Stopped and Failed sessions are excluded; this was witnessed.
  - There is no explicit acknowledgement verb. The row clears when no qualifying notes remain (typed, canceled, or attempts > 0).
- **Route:**
  - Read-only: it charges nothing and returns 404 for a missing session.
  - `since` is exclusive. The limit is clamped to 1–100.
  - The page holds at most 256 KiB of bodies, except that the first note is always included.
  - It has the same exposure as the existing `GET /messages` (no per-agent ownership check on either).

## Advisories (do not block)
- **A-1 docs drift:** the docs and comment still describe the first redo. Living docs; fix with the D-1 Code pass.
  - `docs/ops-http.md` (CARD-0680 section) still says the other withholds "record one attempt and then stop". It also omits the 256 KiB page bound.
  - `docs/session-runtime-invariants.md` (around line 1506) says a durable withhold "records one delivery attempt and a transcript floor". Both statements are false for the final code.
  - `client/src/api/attention.ts`: the PendingNotesAged comment says "still under the delivery-attempt cap".
- **A-2 stale withhold alert:** the withhold record is permanent. Once the withhold clears, notes still pending for another reason past 900 s raise "withheld" although nothing is withheld. Examples are a long Working turn, a herdr block or a rules hold. Also, `LastWithheldAt` never moves; it always equals `FirstWithheldAt`.
- **A-3 transferred notes:** a note moved to another session keeps its old withhold record.

## Evidence (fresh, this session)

**Builds:** all through `build-slot.ps1` with `OutputPath=bin-r8f5/` and exit 0.

| Build | Tree | Time |
|---|---|---|
| Antiphon.Tests | tip | 188 s |
| Antiphon.Tests | mutated | 227 s |
| Antiphon.Tests | tip, clean rebuild | 118 s |
| Antiphon.SessionRunner.Tests | tip | 16 s |
| Antiphon.Tests | base, from a `git archive` copy in scratchpad, `bin-r8f5b/` | 250 s |

**Test runs:**

| Filter or class | Total | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|---|
| (PendingNoteWithholdTests\|DialogDeliveryAdmissionTests\|ClaudeDialogDeliveryTests\|AppDbContextModelTests) | 14 | 14 | 0 | 0 | Run twice: first build and clean rebuild. The first run also included ClaudeDialogWatchdogCanaryTests, which was skipped as native opt-in. |
| SessionRunner ClaudeDialogInputGuardTests | 3 | 3 | 0 | 0 | |
| Existing affected classes at tip | 594 | 553 | 41 | 0 | See note below |
| DelegationDispatchRecoveryBoundaryTests alone, tip | 381 | 340 | 41 | 0 | |
| DelegationDispatchRecoveryBoundaryTests alone, base | 381 | 381 | 0 | 0 | |
| AbsentLaunchPolicyTests + ChannelOutboundContractTests + EntityScalarSnapshotTests, tip | 124 | 123 | 1 | 0 | The failure is C1149_Queue_columns_are_accounted_for |

The existing affected classes were RemoteControlRecovery, SessionMessageQueueWedgedHead, LandReceiptScanCache, StandingResumeReceiptReadiness, ParkedMessageSweepService, PermissionWaitSurface, SessionRuntimeActionTemporaryBlock and DelegationDispatchRecoveryBoundary. All 41 failures are in DelegationDispatchRecoveryBoundaryTests.

**Evidence diff:** `check-evidence-diff.ps1` over base..tip gave commits=4 and violations=0.

**Not run:**
- The whole Unit lane: the brief said "No whole Unit", so `ordinaryScopeCompleted` is Interim.
- Client checks: the author ran them, and the only client change is the kind union, a visual entry and a type.
- The checkpoint tool: there is no plan `### Checkpoints` table for this redo.
- PCs remain pending.

**Not cleaned up:** the `bin-r8f5`/`bin-r8f5b` directories and the scratchpad base copy were left in place, because the brief said "delete nothing".
