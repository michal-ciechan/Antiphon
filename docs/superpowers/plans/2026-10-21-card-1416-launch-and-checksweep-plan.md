# CARD-1416: the remaining launch and CheckSweep stale tests (L1-L4, C1)

Investigation: `docs/investigations/2026-10-21-card-1416-remaining-systemprompt-launch-and-checksweep-reds.md`
(Investigate task 45340222, `1a31988176564b347f5576b18249b607484ffddd`).
Test-only: no product code changes. Every changed expectation cites the product line it follows.

## Slices

- S1 (L1) `Start_with_system_prompt_append_passes_flag_on_fresh_launch`: expect exactly one
  `StandingFreshSelected` incident on the new session (`AgentControlService.cs:850-862`).
- S2 (L2, L3) `Resume_not_found_fallback_delivers_fresh_bootstrap` is rewritten as
  `Resume_not_found_holds_continuity_without_a_fresh_bootstrap`: one `--resume` adapter, nothing
  typed, session Failed / `ContinuityUnavailable`, supervision held `NativeSessionMissing`, one
  `StandingContinuityHeld` incident (`AgentSessionService.cs:475-477`). It is kept rather than
  deleted: the CARD-0466 suites (`AgentSessionLaunchFailureTests`, `StandingContinuityRecoveryTests`)
  use a `StartupOutput` needle on a direct launch or the recovery fixture, not the `ThrowOnStart`
  path through the full control-service launch chain. `Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap`
  is deleted; the same-ID fallback it pinned no longer exists.
- S3 (L4) `Launch_note_yields_to_an_owed_channel_row`: expect `ChannelPromptCorrelation.Mark(rowId, body)`
  and write the `OnSubmitted` UserPrompt and the later TurnEnd under a `SessionStateStore` write
  lease plus `ReconcileAsync` (the `BridgeQueueHarness.MarkWorkingAsync` shape).
- S4 (C1) `the_flush_cancels_a_settled_check_already_typed_once_before_it_retypes`:
  `RecordingRunnerClient` gains a `LiveSessions` set reported Running by `ListAsync`; only this test
  adds the caller, so the CARD-0696 liveness gate (`SessionMessageQueueService.cs:2206-2208`) admits it.

The `/bin/sh` off-Windows executable is already on the base (93d74092a); Windows keeps `cmd.exe`.
`tests/test-execution-policy.json` has no rows for either class, so nothing is un-excluded.

## Verification design

V-1 each of the four kept tests passes (L4's store reconcile is proved by running it, not inferred).
V-2 no other case in `AgentSystemPromptLaunchTests` (which also holds the Launch_note test) or
`AgentTaskCheckSweepTests` regresses. The red witness is the five tests failing at base
`ed419bd2c` (Unlisted below). Every PC stays pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S4 | `tests/Antiphon.Tests -> bin-c1416c/` | prompt-launch-class | `/*/*/AgentSystemPromptLaunchTests/*` | V-1, V-2 | all listed, 0 failed | 28 | 7 |
| CP-2 | S1-S4 | CP-1 | check-sweep-class | `/*/*/AgentTaskCheckSweepTests/*` | V-1, V-2 | all listed, 0 failed | 36 | 3 |

### Unlisted

One red witness at base `ed419bd2c` before S1-S4 (`bin-c1416w/`): the five target methods in one
process, 5 executed, 5 failed.

### Cost

Ordinary floor 10 minutes. The brief limits this round to these two class rows; the Unit lane is
not run, because the edits are confined to private members of two Integration classes.
