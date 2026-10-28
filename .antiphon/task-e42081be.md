# CARD-1591 delta Final Review 3 (task e42081be)

Reviewed feat/card-task-ebec5b7d at b1ed7c4757e7e296cee687042e45594c716c0e0c (base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50), round-3 delta ecce221dc..b1ed7c475, on server2/Linux.

Outcome: clean. The absent-launch regression from round 2 is gone, and no new fail-open or regression was found. Five advisories, none blocking.

## Runs (one build per test project through build-slot, then --no-build exact filters)

| Selection | Total | Passed | Failed | Skipped |
|---|---|---|---|---|
| DelegationDispatchRecoveryBoundaryTests (whole class) | 382 | 382 | 0 | 0 |
| AbsentLaunchPolicyTests | 88 | 88 | 0 | 0 |
| PendingNoteWithholdTests | 9 | 9 | 0 | 0 |
| StandingSessionQueueSwitchTests | 9 | 9 | 0 | 0 |
| DialogDeliveryAdmissionTests | 1 | 1 | 0 | 0 |
| AppDbContextModelTests | 3 | 3 | 0 | 0 |
| (the six rows above, one run) | 492 | 492 | 0 | 0 |
| EntityScalarSnapshotTests + ChannelOutboundContractTests (closed censuses) | 37 | 37 | 0 | 0 |
| Antiphon.SessionRunner.Tests ClaudeDialogInputGuardTests | 3 | 3 | 0 | 0 |

All 13 C1591_* tests in Antiphon.Tests are in the 492-test run and passed. `scripts/check-evidence-diff.ps1` over base..HEAD: 7 commits, 0 violations. The whole Unit lane was not run, because the brief said not to. Build outputs `tests/Antiphon.Tests/bin-e42081be/` and `tests/Antiphon.SessionRunner.Tests/bin-e42081be/` (plus project bin-e42081be dirs) are left in the mirror, because the brief says to delete nothing. They are gitignored.

## Priority findings

1. **Absent-launch census.** The four columns are now in `AbsentLaunchPolicy.MessageColumns`. The dispatcher's `knownColumns` SetEquals check (AgentTaskDispatcher.cs:3504) is true again. `C1591_Withheld_brief_still_holds_absent_launch` seeds a withhold record on the brief and shows the task held Blocked with `DispatchLaunchAbsentReason` and the body unchanged.
   - `C1149_Queue_columns_are_accounted_for` is a reflection set-equality between entity properties and the census string, so an unclassified new column fails it by construction. The round-2 review measured exactly that: it was red at ecce221d.
   - The other enumerators do not read the withhold columns, so a withheld row still counts as never attempted everywhere. These are `StandingQueueSwitchPolicy.NeverAttempted`, its callers (AgentControlService:637, StandingRestartEscalationPolicy.CanTransfer, CheckCompactionContinuationService:627/628/727) and `IsNeverAttempted`.
   - The only other closed censuses are EntityScalarSnapshotTests and ChannelOutboundContractTests, and both are green.
   - The only reader of the columns as a condition is AttentionService.BuildPendingNotesAgedItemsAsync.
2. **ExecuteUpdate.** It is a single atomic UPDATE. Its predicate is: same session, Pending, DeliveryAttempts 0, no LastDeliveryStartedAt, no baseline, no verdict, and (WithholdCount 0 or a different reason). Under a concurrent flush, Postgres re-checks the predicate after the row lock, so a repeat of the same reason matches 0 rows. No row with a delivery attempt is touched. A run of identical reasons cannot spin. Nothing reads WithholdCount as a limit; the alert only checks > 0.
3. **AcceptAsync move-clear.** The clear happens after the :637 NeverAttempted and CanTransfer gates, and neither gate reads the withhold fields, so their behaviour is unchanged. The clear is on tracked entities and is saved by the AcceptAsync SaveChanges (line 887) inside the `reservation` transaction. That save commits with the agent pointer and the session, under the per-session queue locks taken at line 538.
4. **Documented limitation.** The alert can name a withhold after it cleared, but only for a note that is still Pending with DeliveryAttempts 0 on a session that is not Stopped or Failed. It is detection-only, at Warning level, one row per destination, deduplicated by ConditionKey, and it takes no action. The worst case is a misleading Warning row, not spam or input. This is acceptable for rc.
5. **Write allow-set.** The server diff of this delta has no Refusal or CheckDialogInput change and no new terminal/input/submit call.

## Advisories (non-blocking)

- A-1: If the withhold reason alternates between flushes (for example adopted then screen-unknown), WithholdCount grows by one per flush without bound. Each flush already issues this UPDATE, so there is no extra statement and no reader treats the count as a cap. The count measures reason changes, not time withheld. Consider documenting that if anyone starts to read it.
- A-2: `AbsentLaunchPolicyTests.C1591_Withhold_record_is_still_never_attempted` passes `KnownMessageColumns: true` as a constant, so it would have been green at ecce221d. It guards against someone adding withhold-pristine terms to the predicate. The regression witness is the DDRB integration test `C1591_Withheld_brief_still_holds_absent_launch`, which reads the real model census.
- A-3: The alert text says notes "are still withheld". Under the documented limitation, a note that is pending only because the session is Working, or is under HoldUntil, can be described that way. The wording could say "were withheld".
- A-4: The move-clear relies on EF snapshot change detection. If a row was loaded with WithholdCount 0 and an out-of-process writer recorded a withhold before the save, the 0 assignment is not a modification and the stale record moves with the note. Same-process flushes are excluded by the queue locks, so this needs a second server process and only affects the informational record.
- A-5: The fix commit 91b12b52 changed the expectations of an existing test (`C1591_NamesWithholdsChargesOnceAndAlerts`: WithholdCount 1 to 4 and 3) along with the semantics. This is consistent with the new design, but that test was not red-first for the new counting. `C1591_LaterWithholdReasonAdvancesLastOnce` (78f2b5af, before the fix) is the red-first witness.

## Scope note

The verification profile says Final, but the brief limited this round to narrow witnesses with "No whole Unit". So `ordinaryScopeCompleted` is Interim. The brief says `next: land` if clean. Whether that is enough for a land is the caller's call under the "Interim Review cannot approve a land" rule.

--- review evidence ---
subjectTaskId: ebec5b7d-d4b1-4be1-abbb-35c353585767
reviewedSourceSha: b1ed7c4757e7e296cee687042e45594c716c0e0c
reviewedSourceClean: false
ordinaryScopeCompleted: Interim
