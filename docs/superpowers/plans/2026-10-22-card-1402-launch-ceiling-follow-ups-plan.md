# CARD-1402: launch ceiling keyed to the cancelled TurnEnd; same-burst witness asserts the stall kill

Follow-ups from the CARD-1395 Final Review (clean at f51db694). CARD-1395 recorded the launch ceiling as the highest stored transcript sequence at the moment `WaitForTurnCompleteAsync` returned Cancelled (`AgentSessionService.StartAsync`). A follow-up stored after the cancelled TurnEnd but before that query sat at or below the ceiling, so it was treated as the launch prompt and dropped, which hid its stall. The same-burst witness asserted only the phase, not the kill. The CARD-1395 Code report claimed 16/16 for a CP-1 row whose filter selects the 10 `C1382_Rearm_*` methods.

## Decisions

- D-1 The launch ceiling is the stored sequence of the cancelled TurnEnd that ended the launch turn: the session's highest `TurnEnd` row with `StopReason` `cancelled` at the moment the wait returns. A follow-up stored after that row is above the ceiling and re-arms, even when it was stored before the ceiling query ran.
- D-2 When no cancelled TurnEnd row is stored yet at that moment, the ceiling stays the highest stored sequence. Stored sequences follow the runner's order, so nothing after that TurnEnd is stored before it, and the highest stored sequence is then at or before the TurnEnd. That is today's behaviour kept for that case, not a new one; only the Codex runner adapter reports Cancelled, and it reads that verdict from the runner's TurnEnd row before the server tail may have stored it.
- D-3 The same-burst witness also asserts the stall kill: a scan one second before StallTimeoutMs kills nothing; a scan one second after it kills both re-armed sessions (KillCount 1 each, adapter disposed, attempt Stalled, session Failed).
- D-4 The CARD-1395 plan's CP-1 row is amended to its actual roster (10 `C1382_Rearm_*` methods, Min 10). The 16/16 claim covered the whole class plus a `RunAttemptStallDetectorTests` method outside the row; that selection is recorded there as unlisted.

## Slices

- S1 `FakeAgentProtocolAdapter.OnTurnWait`; `C1402_Rearm_follow_up_saved_before_cancel_returns_still_rearms` (red before the fix); the stall-kill extension of `C1382_Rearm_same_burst_follow_up_after_provisional_slash_arm_still_rearms`; the ceiling fix in `AgentSessionService.StartAsync`; `docs/session-runtime-invariants.md`; the CARD-1395 plan amendment.

## Verification design

- V-1 `C1402_Rearm_follow_up_saved_before_cancel_returns_still_rearms`: red at the witness commit (the follow-up is dropped and the attempt stays AwaitingInput), green after the fix. One narrow run of that single method is the red proof; it is outside the table and stated in the report.
- V-2 `C1382_Rearm_same_burst_follow_up_after_provisional_slash_arm_still_rearms` with the kill assertions of D-3.
- V-3 The rest of `AgentSessionCancelledTurnTests` unchanged, in particular `C1382_Rearm_launch_prompt_saved_before_cancel_stays_waiting`: with no TurnEnd row stored, the D-2 fallback still drops the launch prompt.
- V-4 The Unit lane of `Antiphon.Tests` for the touched production file.
- PC-1 (Mutation, method-scoped): restore the `MaxAsync` over every row of the session in `StartAsync` → V-1 red at `armed.Attempt.Phase.ShouldBe(StreamingTurn)`.
- PC-2 (Mutation): drop the D-2 fallback (ceiling 0 when no cancelled TurnEnd row is stored) → `C1382_Rearm_launch_prompt_saved_before_cancel_stays_waiting` red.
- PC-3 (Mutation): exclude re-armed attempts (`RearmPromptSequence != 0`) in `RunAttemptStallDetector.ScanAsync` → V-1 and V-2 red at the second scan count.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1402-ceiling/` | cancelled-turn-class | `/*/*/AgentSessionCancelledTurnTests/*` | V-1, V-2, V-3 | all 16 methods, 0 failed/skipped | 16 | 25 |

### Descope (task b10154f3): CP-2 withdrawn, three inherited reds documented without receipts

CP-2 (group `unit-lane`, filter `/*/*/*/*[Category=Unit]`, Covers V-4) is withdrawn from the closed list above, and nothing replaces it as a receipt row. Reason: Code task 4a1a5eee ran it once at 0d2fdcbf648b05528e7dc0247d8e0ee22ad7fa8f and it hit the 30-minute deadline the tool derives from its 10-minute estimate (max(15, 3x10)) on the loaded server2 host (load average 38-62 on 24 cores, five Code sessions and another `Antiphon.Tests` build in flight), writing no TRX; Final Review 3d40a611 (clean at 465d1a5a490f4f4c6551fa7606abf328d8f1c3b4, report `.antiphon/task-3d40a611.md` on `feat/card-task-3d40a611`) did not run it, so its ordinary scope settled Interim, and an Interim Review cannot approve a land. Policy: the operator's standing testing policy is minimal testing, one red witness plus one narrow green per task, the rc roster as the gate, and no whole-Unit or whole-lane runs (stated in the Code task b10154f3 brief; the same policy the CARD-1233 plan records as D-7 and the CARD-1403, CARD-1435 and CARD-1449 plans record as "minimal testing per the brief"). CP-1 (`AgentSessionCancelledTurnTests`, the whole class, 16/16 with a validated clean receipt at 465d1a5a in Review run `20261022-073943-6a7c`) is the only receipt row of this plan. The V-4 line above stays as written for history; it has no receipt row, and under that policy its lane coverage is the rc roster gate's, not this plan's.

The three failures the partial CP-2 console showed are inherited. Review 3d40a611 ran each one narrowly at base 294fa3d1a81ff97abb285af3180d93ee4d4b25f6 (detached worktree, isolated build through `scripts/build-slot.ps1`) and at the tip 465d1a5a (its review-plan rows CR-1..3 in checkpoint run `20261022-071233-229d`), with identical results at both. None of them is in the area this card touches (`AgentSessionService.StartAsync`, `FakeAgentProtocolAdapter`, `AgentSessionCancelledTurnTests`; `RunAttemptStallDetector` is unchanged). The rows below are documentation, not receipts: no row of this plan runs them, their expected outcome is the known red, and no task of this card claims them green or fixes them.

| Row | Test | Narrow filter | Base 294fa3d1a | Tip 465d1a5a | Origin |
|---|---|---|---|---|---|
| NR-1 | `TestClassificationGuardTests.Registry_matches_compiled_metadata` (`tests/Shared`, Unit; the drift it reports is `TestSchemaSnapshotTests`) | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | 1 run, 1 failed: `missing classes` and `unregistered-marked Antiphon.Tests.TestHelpers.TestSchemaSnapshotTests` | same | Registry drift from CARD-1420's new `TestSchemaSnapshotTests` (plan `2026-10-21-card-1420-cached-test-schema-plan.md`); no open card found by the Review |
| NR-2 | `DispatchBaseWarningRosterTests.C1155_RosterNamesEveryDispatchWarningE2EMethod` (Unit) | `/*/*/DispatchBaseWarningRosterTests/C1155_RosterNamesEveryDispatchWarningE2EMethod` | 1 run, 1 failed: roster drift | same | Already recorded as inherited in the CARD-1422 plan (its CP-5 row) |
| NR-3 | `CheckpointShapeConflictTests.shape_conflict_rule` (Unit, 16 `[Arguments]` cases) | `/*/*/CheckpointShapeConflictTests/shape_conflict_rule` | 16 run, 15 failed: `toolNote should be "ok" but was "receipt-invalid: negative qualification mismatch"` | same | No card found by the Review (`card.ps1 search -All` on the test name and the message); the Review's backlog suggestion is to file one |

### Cost

Ordinary floor is 25 minutes (CP-1 only; CP-2 was withdrawn in the descope section above).

## RC

CARD-1382 and CARD-1395 are master only (neither `ab2a1683e` nor `02aaaf471` is on `rc/2026-10-20`), so this follow-up is master only too; nothing to cherry-pick.
