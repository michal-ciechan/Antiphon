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
| CP-2 | S1 | CP-1 | unit-lane | `/*/*/*/*[Category=Unit]` | V-4 | all selected Unit cases, 0 failed | 1000 | 10 |

### Cost

Ordinary floor is 35 minutes.

## RC

CARD-1382 and CARD-1395 are master only (neither `ab2a1683e` nor `02aaaf471` is on `rc/2026-10-20`), so this follow-up is master only too; nothing to cherry-pick.
