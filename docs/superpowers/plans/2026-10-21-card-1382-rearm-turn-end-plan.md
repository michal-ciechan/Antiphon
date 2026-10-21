# CARD-1382 repair: end a re-armed turn

Final Review 9820f87a. Master-only. S1 re-arms an interrupted attempt into `StreamingTurn` and never leaves it when the agent answers or a second interrupt arrives. `RunAttemptStallDetector` then kills the Running session. This repair returns that attempt to `AwaitingInput` and makes the echo window per wait. No new kill path. Parking stays disabled. Positive controls stay unbound.

## Decisions

- A `TurnEnd` on a re-armed `StreamingTurn`, for any stop reason, calls `Transition` to `AwaitingInput` when its `Sequence` is after the prompt that re-armed the turn. `CreatedAt` is the save clock and `PhaseStartedAt` is the processing clock, so a burst saved before processing is not a previous turn. A `TurnEnd` at or before that prompt's sequence is the previous turn and does not end the current one. `StartAsync` still owns the launch turn, including `Finishing`/`Succeeded`. The launch `StreamingTurn` has neither the open-wait marker nor an `AwaitingInput` duration, so its `TurnEnd` is ignored.
- A later `[Request interrupted` marker on a re-armed `StreamingTurn` uses the same sequence rule. A follow-up prompt saved before that end is processed still re-arms when its sequence is after the re-arming prompt.
- `Transition` into `AwaitingInput` sets `OpenInputWait`. `Transition` out of `AwaitingInput` clears it. Provisional arm and restore do not clear it. Provisional arm writes the marker when a wait that predates it has none, so a slash prompt on that wait can still restore. `IsProvisionalStreamingTurn` is that marker, so a later wait can restore an echo after an earlier wait already recorded an `AwaitingInput` duration.
- Echo-looking transcript rows after a real prompt do not move `LastEventAt`. The scanner still kills `StallTimeoutMs` after that prompt.

## Verification

Commit the witnesses, run CP-1 red, then commit the fix and run CP-1 and CP-2 green. No Unit lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1382-turn/` | integration-rearm-turn | `/*/*/AgentSessionCancelledTurnTests/C1382_Rearm_*` | V-1 | all listed, 0 failed/skipped | 9 | 20 |
| CP-2 | S1 | CP-1 | integration-rearm-scan | `/*/Antiphon.Tests.Application/(AgentSessionCancelledTurnTests*)\|(RunAttemptStallDetectorTests*)/*` | V-1, V-2 | all listed, 0 failed/skipped | 15 | 25 |

### Cost

Ordinary floor is 45 minutes. The red CP-1 run is a rerun of that row, not a third row.
