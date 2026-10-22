# CARD-1395: re-arm follow-ups

Final Review of CARD-1382. The sequence comparison that was meant to ignore a previous turn's TurnEnd never sees that row: stored sequences are strictly monotonic and one session's actions run in that order. The witness rewrote CreatedAt and reinserted an action at the old sequence. A launch prompt saved before cancel can still be drained afterwards and re-arm, because that comparison replaced the save-clock filter. A prompt stored while a re-armed turn is still running, followed by that turn's later TurnEnd, stays the master fail-open.

## Decisions

- Remove the TurnEnd and interrupt sequence comparisons. They reject only an action the store cannot admit.
- Before any real re-arm is remembered, a UserPrompt with CreatedAt at or before PhaseStartedAt does not re-arm. That is the launch prompt drained after StartAsync has entered AwaitingInput.
- A follow-up whose sequence is after the remembered re-arm still re-arms when it was saved in the same burst as a TurnEnd. Processing that TurnEnd moves PhaseStartedAt past the follow-up, so the save-clock filter must not apply once a re-arm has been remembered.
- Do not bind a TurnEnd to a later prompt. Prompt B processed during turn A, with A's TurnEnd stored after B, still returns the attempt to AwaitingInput. That needs a turn identity.

## Verification

Commit the launch-prompt witness red, then the filter. One checkpoint row. No Unit lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1395-rearm/` | integration-rearm-follow-up | `/*/*/AgentSessionCancelledTurnTests/C1382_Rearm_*` | V-1 | all listed, 0 failed/skipped | 10 | 20 |

CARD-1402 alignment: this filter selects the 10 `C1382_Rearm_*` methods of the class as landed (the nine from CARD-1382/1395 S1 plus the same-burst witness), so the row's roster and Min are 10. The Code report's 16/16 claim was the whole class (15 methods) plus one `RunAttemptStallDetectorTests` method, a selection this table never listed; it is an unlisted run, not this row's evidence.

### Cost

Ordinary floor is 20 minutes.
