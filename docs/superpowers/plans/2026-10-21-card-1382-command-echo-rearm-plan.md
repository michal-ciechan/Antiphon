# CARD-1382: re-arm an interrupted attempt without a stuck command-echo stall

Master-only. Inspected at `326c91c7759522751213bf2c1bd24a1a9a1cb66e` (this branch tip, equal to `origin/master`). The card was read from the Antiphon board on 2026-10-21. The dropped F1 implementation was read at `81b6e30068789a46af0a7a6faf7bafd122f063b3` (`feat/card-task-67776504` / `feat/card-task-5fd19a50`) and is not on this tip. Verification design is in this plan; next is Code. No builds, tests, or live interruptions were run.

The defect is not moot. Master never re-arms the interrupted attempt, so a follow-up has no stall coverage. The echo-before-wrapper order that failed three Final Reviews is still how live ingest works, so copying the dropped F1 method would arm a stall on a healthy slash command.

## Ground truth

| What the card assumes | What the code does at this SHA | Smallest change |
|---|---|---|
| After S4, a cancelled turn sits in `AwaitingInput`, which the stall scanner does not select. | `AgentSessionService.cs` transitions to `AwaitingInput` only on exact `StopReason=cancelled` (around line 283). `RunAttemptStallDetector.ScanAsync` selects only `StreamingTurn` with `LastEventAt` older than `StallTimeoutMs` (default 300000). | Keep that scanner. Do not add an `AwaitingInput` kill. |
| A later follow-up is typed into the Running session and the attempt stays `AwaitingInput`. | Nothing on master transitions `AwaitingInput` back to `StreamingTurn`. `ResumeInterruptedAttemptAsync` does not exist. `TryCreateManualRunAttemptAsync` returns without a new attempt while the latest attempt is non-terminal. | Re-arm that same attempt from the transcript action for a real `UserPrompt`. |
| Live persist order is echo, then wrapper. Three reviews failed because F1 treated the echo as the follow-up. | `SessionRunnerEventPump.DispatchEventAsync` calls `ObserveTranscriptAsync` with one event. That persists one line, admits one `SessionRuntimeAction`, and hints the worker before the next line. `IsRawLocalCommandEcho` returns false when the invoked set is empty, so a raw `/status` is not housekeeping until its `<command-name>` wrapper is stored. CARD-0041 measured the raw echo before that wrapper. | The echo's phase-2 action must be correct while the wrapper row is absent. The wrapper's later action must put an echo-only arm back in `AwaitingInput`. |
| The dropped repair's window and "phase 2 sees both rows" comment are the fix. | At `81b6e300`, `ResumeInterruptedAttemptAsync` assumes the wrapper is already stored because "phase 2 follows the persist batch". `C1322_Follow_up_prompt_rearms_interrupted_attempt` seeds the echo and the wrapper, then calls the action once. That is the order the reviews rejected. | Do not port that method or that seed. Witness persist-and-drain of the echo, then persist-and-drain of the wrapper. |
| F2 usage stamping, adapter stop-reason checks, and idle-Working release already landed. | `CodexTranscriptNormalizer` consumes usage on `task_complete` and `turn_aborted`. `RunnerCodexAdapter.TryBuildTranscriptVerdictAsync` forwards `StopReason` and uses `IsReportBoundary`. The invariants doc already says an interrupted wait is not an automatic release. | Leave those paths alone. |

## Decisions

- **D-1 — Re-arm the interrupted attempt itself.** On phase 2 of a committed `UserPrompt`, load the latest incomplete attempt for that Running session. A prompt that cannot be a command echo calls `RunAttemptStateMachine.Transition` to `StreamingTurn`. That is the stall clock: `LastEventAt` becomes now, and the existing scanner kills the attempt after `StallTimeoutMs` with no output. Queued rows, interrupt markers, wrappers, stdout, compaction continuations, and task notifications do not re-arm. A second attempt is not created.
- **D-2 — A slash-prefixed echo is not progress and does not stick a stall.** A trimmed `UserPrompt` that starts with `/` and is not itself a `<command-name>` / `<local-command-stdout>` record is indistinguishable from a real slash-prefixed prompt until a same-window wrapper exists. Arm `StreamingTurn` provisionally so later terminal output can refresh `LastEventAt` through the existing `RecordActivityAsync` path, and so `/status explain the remaining work` is still scanned. Keep `PhaseStartedAt` at the `AwaitingInput` instant. Do not call `Transition` for this arm: `Transition` would move `PhaseStartedAt` and close the input-wait in `PhaseDurationsJson`.
- **D-3 — The wrapper action restores the input-wait.** When phase is the provisional `StreamingTurn` (the `AwaitingInput` duration key is absent) and every `UserPrompt` / `QueuedUserPrompt` with `CreatedAt >= PhaseStartedAt` is housekeeping under the command names in that same window, set phase back to `AwaitingInput` and set `LastEventAt = PhaseStartedAt`. Do not call `Transition`. The scanner ignores `AwaitingInput`, and the deadline is the original input-wait clock, not the echo's arm time. A real `Transition` writes the `AwaitingInput` duration key; while that key is present a later wrapper must not restore.
- **D-4 — The window is this input-wait's admission clock.** `TranscriptEntry.CreatedAt` is the persist clock (`PersistTranscriptCoreAsync` sets it to `now`), not the provider timestamp. Command names come only from prompt rows with `CreatedAt >= attempt.PhaseStartedAt`. An older `/status` wrapper stays outside the window, so `/status explain the remaining work` still arms. The query selects text only. The whole-session name set from review `04046b8b` stays rejected.
- **D-5 — No grace timer.** The echo action runs with an empty invoked set. The wrapper action runs later, after that row is committed. A silent local command that outlasts `StallTimeoutMs` before its wrapper commits can still be killed by the existing scanner; output during the provisional turn refreshes `LastEventAt` and defers that. A grace would also delay a real slash-prefixed prompt, and a short grace loses to a slow `/compact`. No new scheduler and no new kill path.
- **D-6 — Replay does not move the deadline.** `CreatedAt <= PhaseStartedAt` is still the previous turn. While already `StreamingTurn`, a prompt with `CreatedAt <= LastEventAt` does not refresh `LastEventAt`. A newer non-housekeeping prompt during a provisional arm sets `LastEventAt` to now and stays in the window, so the later wrapper does not restore over it.
- **D-7 — Placement is the live catalogue.** `GET /api/runner-defaults` is revision 2 with an empty `kindDefaults` list and a global preference. `GET /api/session-runners` returned one eligible Windows runner, one eligible Linux runner, and one draining Linux runner. Re-read at dispatch. This checkpoint needs no OS and no host: omit `-Runner` and `-Platform`. Lane names below are test categories.

### Rejected

- Porting `ResumeInterruptedAttemptAsync` from `81b6e300` unchanged. Its comment assumes the wrapper is in the database when the echo action runs. The pump persists one line and hints before the next line. Review `5fd19a50` failed on that order.
- Seeding the wrapper before the echo action. That is how CP-11 stayed green on the dropped branch.
- Refusing every slash-prefixed prompt. Review `04046b8b` required `/status explain the remaining work` to re-arm when the only wrapper is older than `PhaseStartedAt`.
- A grace period before any slash-prefixed arm. The wrapper is not guaranteed within a few seconds (CARD-0041's `/compact` records the wrapper after the command). The restore path covers the wrapper whenever it commits before the stall kill.
- Teaching `RunAttemptStallDetector` to kill `AwaitingInput`. That is the false kill S4 removed. Bare input-wait stays unscanned.
- A new attempt, a new column, or a parking deadline. `AwaitingInput` is already non-terminal. Parking stays disabled.

### Ordering the fix relies on

1. One runner transcript event becomes one `PersistTranscriptAsync` commit and one hint (`ObserveTranscriptAsync`). The echo's phase-2 drain can observe an empty invoked set. That is legal. Provisional arm is the response, not a classification that the wrapper already exists.
2. The wrapper is a later `UserPrompt` (CARD-0041 file order). Its own commit happens before its phase-2 drain. The echo row is then durable, `CreatedAt >= PhaseStartedAt` includes both, and `IsHousekeepingPrompt` matches the echo to the wrapper's command name. Restore runs on that drain.
3. If the wrapper was already committed before the echo drain, the echo is housekeeping on the first drain. Phase stays `AwaitingInput` and `LastEventAt` stays the input-wait clock. Both orders end there.
4. A non-echo `UserPrompt` takes `Transition`. The closed `AwaitingInput` duration is the marker that this `StreamingTurn` is real. A later `/status` in that turn does not restore it.
5. A real stall is the existing scan: `StreamingTurn`, `LastEventAt` older than `StallTimeoutMs`, then kill and dispose. Non-echo follow-ups enter that set at the prompt action. Unmatched slash-prefixed prompts enter it at the provisional arm. Echoes leave it when the wrapper commits, before a later scan.

### Input-wait / release checklist

Re-arm does not release the seat, stop the process, or enable parking. With `BlockedTaskParking:Enabled=false` there is no automatic input-wait release deadline (CARD-1083). The only kill remains `RunAttemptStallDetector` on `StreamingTurn`. Restore returns the attempt to the unscanned phase and puts `LastEventAt` back on `PhaseStartedAt`. No new release deadline and no new kill path.

## Slices

One slice. Commit the red test, run CP-1, then commit the fix and run CP-1 green. Master-only. Push after each commit.

| Slice | Files | Code budget |
|---|---|---:|
| S1 — echo-order re-arm | `server/Domain/StateMachine/RunAttemptStateMachine.cs`: add provisional arm and restore. Provisional arm requires `AwaitingInput`, sets `StreamingTurn` and `LastEventAt`, and leaves `PhaseStartedAt` and `PhaseDurationsJson` unchanged. Restore requires that provisional `StreamingTurn`, sets `AwaitingInput`, sets `LastEventAt = PhaseStartedAt`, and leaves `PhaseStartedAt`, `CompletedAt`, and the duration JSON unchanged. Expose whether the duration JSON already has an `AwaitingInput` entry. `server/Application/Services/AgentSessionRuntime.cs`: from `ExecuteActionPhaseAsync` phase 2, when `row.Kind` is `UserPrompt`, call the new re-arm before `ConfirmSentRefinementsAsync`. Implement the D-1..D-6 rules there. Publish `RunAttemptChanged` on arm, real transition, and restore, using the attempt's card board. `tests/Antiphon.Tests/Application/AgentSessionCancelledTurnTests.cs`: one new method. `docs/session-runtime-invariants.md`: extend the CARD-1322 paragraph with the re-arm, the echo restore, and the unchanged parking rule. | 45m |

`AgentSessionCancelledTurnTests` is already on `tests/Antiphon.Tests/slow-tests-allowlist.txt`. Do not edit the allowlist. Do not edit the Codex normalizer, runner adapters, capacity recovery, or attention text.

The class is already `[Category("Slow")]`. The new method stays in that class. CP-1 runs only the new method. The two existing methods are not part of this closed list.

## Verification design

Operator scope: **one red witness plus one narrow green. No Unit lane, no namespace, no whole class, no whole assembly.**

- **V-1 / S1:** `AgentSessionCancelledTurnTests.C1382_Echo_before_wrapper_does_not_stick_streaming_turn`. Drive the existing real `StartAsync` cancellation so the attempt is `AwaitingInput` and the session is Running. Register `SessionStateStore`, `SessionRuntimeActionCoordinator`, and this same `AgentSessionRuntime` in the test container (persist throws without the store; `DrainPageAsync` resolves the runtime from the container). Advance the fake clock so admission `CreatedAt` is strictly after `PhaseStartedAt`. `PersistTranscriptAsync` only the raw `/status` echo, then `DrainPageAsync` once. Then `PersistTranscriptAsync` only `<command-name>/status</command-name>`, then `DrainPageAsync` once. Do not insert both rows before the first drain, and do not start the coordinator's background worker. Reload from a new context: phase is `AwaitingInput`, `LastEventAt` equals the pre-echo value, session Running, adapter kill count 0. Advance the clock past the configured stall timeout (keep the production 300000 default; ten minutes matches the existing stall witness) and scan with a new `RunAttemptStallDetector`: stalled 0, kill 0. Then persist and drain `Continue after interruption`: phase `StreamingTurn`, `LastEventAt` equals the clock. Drain that same prompt again: `LastEventAt` unchanged. Scan once before the timeout (0) and once after (1), and the adapter kill count is 1. In the same method, a second fixture persists a `/status` wrapper with `CreatedAt` before `PhaseStartedAt`, then persists and drains `/status explain the remaining work`: phase `StreamingTurn`, and a later scan kills exactly once. Intended red on this master: the echo half stays `AwaitingInput` with kill 0, and the non-echo follow-up is still `AwaitingInput`, so the expected kill count of 1 fails. A harness exception before that assertion is not red. A fix that arms the echo and never restores fails the echo half. A fix that never arms `/status explain the remaining work` fails the second fixture.

### Execution

Commit and push the compiling test first. Run only CP-1 at that SHA and require the behavioral failure above. Build or fixture errors, skips, and zero tests are not red. Commit and push the fix. Rebuild and run CP-1 once green. That pair is the ordinary scope.

Use `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this-path> --rows CP-1 --expected-source-sha <full-sha>`. The row takes its own build slot. Await `wait` while the exit is 75. Do not edit source during the run. Keep the unedited CHECKPOINT line. Validate the green receipt. Remove the owned `bin-c1382` directory afterward. Run `scripts/check-evidence-diff.ps1` over base..HEAD.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1382/` | integration-echo-order | `/*/*/AgentSessionCancelledTurnTests/C1382_Echo_before_wrapper_does_not_stick_streaming_turn` | V-1 | all listed, 0 failed/skipped | 1 | 8 |

### Cost

Ordinary Code floor is 8 minutes for the green row. The red run of the same row is about another 8 minutes and is a rerun, not a second row. Authoring and inspection account for the rest of the 45-minute S1 budget.
