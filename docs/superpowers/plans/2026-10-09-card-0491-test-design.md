# CARD-0491 test design: an explicit, opt-in Grok mid-turn interrupt that never kills the session

TestDesign task `a00623c6` for `docs/superpowers/plans/2026-09-27-card-0491-plan.md` (plan commit
`a3c4d26b46a4645b20901bf21b69bb2d3f9572d7`, copied unchanged onto `feat/card-task-a00623c6`; branch
base `89e4f769c0a15bb417d015dc0efc843e1c1eee34`). Every source citation is at the branch base.
No production, test or tool code was edited; no build or test ran. This file is the checkpoint
`--plan` input for Code: the tool reads the first `### Checkpoints` table below. The plan's fix
design S1-S2 stands; §1 records the corrections the measured tree forces, §2 fixes the evidence
order and result vocabulary the tests pin, and §3 appends the verification design the bundle
requires. Where this file and the plan's "Verification design" differ, this file wins. Post-land
Mutation is paused: the positive controls are specified and stay pending.

**Fail-closed rule every test pins.** One conditional `\x03` is written to a delegate's pty only
when every positive condition of §2.2 holds, read in the order of §2.1, under the session's queue
lock, after the marked refinement row is persisted. Any unknown, unreadable, stale or ambiguous
observation withholds the key, keeps the row `Pending` and reports the refusal by name. The key is
never raw input, never `\x1b`, never retried on an uncertain outcome, and never followed by a kill,
park or stop. A written key is reported as `interruptWritten: written` and nothing more; a
refinement is `delivered` only from a complete matching `UserPrompt` transcript row, the existing
CARD-0055 verdict. A cancelled boundary (`TurnEnd` with `stop_reason=cancelled`) flushes the queue
and is never a report boundary, so the task stays Working. CARD-0079 remains the only automatic
stop of a Working session; nothing here is automatic.

## 1. Plan corrections the measured tree forces

- **T-1, slice order.** The plan's CP-3 ran the headed canary after S1-S2. The plan's own risk
  section makes the canary the thing that decides whether the opt-in path may be enabled for the
  installed Grok. The canary therefore becomes slice **S0**, test code only, run first on the
  Windows headed lane; S1 ships with the admission setting defaulted from S0's measured result
  (§2.2 P-1). CP-1 below is that row.
- **T-2, FakeGrok has no Ctrl+C arm.** `src/Antiphon.FakeGrok/Program.cs` lines 755-767 model a
  cancelled `turn_completed` only for a submit into a working composer
  (`ANTIPHON_FAKE_SUBMIT_WHILE_WORKING`). S2 adds `ANTIPHON_FAKE_CTRL_C_CANCELS=1`: a `\x03` byte
  with an empty composer while a turn is in flight appends `turn_completed stop_reason=cancelled`
  (no usage block, the measured 1.0.5 shape), clears the in-flight turn and shows the idle title;
  a `\x03` with a draft clears the draft and leaves the turn in flight. The byte is recorded in
  `ANTIPHON_FAKE_INPUT_LOG`. Tool code, not production; it is the Windows end-to-end substitute
  for the real TUI and is declared as such in the delivery inventory.
- **T-3, the refine fixtures have no runner.** `AgentTaskRefineTests.ScopeFactory` (lines 220-249)
  builds `AgentSessionRuntime` on `EmptySessionRunnerClient`, and
  `LandReceiptScanHarness.ScriptedTranscriptRunner` throws on input. The new class registers a
  recording runner shaped like `RemoteControlRecoveryHarness.ScriptedRcRunner` (lines 309-410:
  `ConditionalCalls`, `RawInputs`, scripted `GetAsync`, `GetSnapshotAsync`, `GetCapabilitiesAsync`,
  `GetTranscriptAsync`), as the `ISessionRunnerClient` the runtime is constructed on. Missing setup
  is listed under Inspection.
- **T-4, monotonic deadlines.** `GrokCanaryTests.WaitForUpdateAsync` (lines 657-668) polls on
  `DateTime.UtcNow`. The new canary class gets its own `WaitForRowAsync` on
  `Stopwatch.GetTimestamp()`; S1 adds no wait loop at all (one catch-up, one runner read, one
  snapshot, one write, each on the existing client deadlines).
- **T-5, the interrupt option is refused by name, the refinement itself behaves as today.** The
  plan's "reject the option for Queued, Blocked, settled, non-Grok, or unowned sessions" is read as:
  a Blocked or settled task throws today's `ConflictException` with nothing persisted; a Queued task
  amends its brief as today; a Dispatched/Working task on any kind still gets its `WhenIdle` row;
  and in every one of those the response names the refusal (`interruptWritten: refused:<reason>`).
  No request is silently downgraded, and no message is thrown away because a flag could not be
  honoured. This is a stated default the orchestrator may overturn; §3 marks the rows it changes.
- **T-6, the existing cancelled-boundary behaviour is already in the tree.**
  `AgentSessionRuntime.IsTurnBoundary` line 488 flushes on `Cancelled`; `TranscriptKinds.IsReportBoundary`
  (`SessionRunnerContracts.cs` lines 467-468) excludes it;
  `AgentTaskReplyIntegrationTests.a_cancelled_turn_end_does_not_settle_the_task_and_says_so` pins the
  task staying Working. S2 adds the response statuses, the FakeGrok arm, the docs and the delivery
  test; it does not touch those three lines, and R-2 reruns their pins.
- **T-7, no new timer, no broadcast.** Nothing in this design watches queue age, and the Grok kind
  conjunct is a whitelist of one. Claude and Codex requests are refused by name (`kind-not-grok`).

## 2. The admission the tests pin

### 2.1 Evidence order (S1, `AgentTaskReplyService.RefineAsync` with `interruptCurrentTurn: true`)

1. Validate the message and resolve the task exactly as today (lines 596-603).
2. Replay check: a `requestId` already bound to a refinement row of this task
   (`ConversationKey == $"refine:{taskId:N}:{requestId:N}"`) returns the recorded result; no row,
   event or key is produced.
3. Today's status switch (lines 611-690): Queued amends the brief, Blocked and settled throw, and
   Dispatched/Working persist the `Refined` event and the `WhenIdle` row, unchanged. For every
   branch but Dispatched/Working the interrupt outcome is `refused:task-status` and the method
   returns.
4. Take the session's queue lock (`SessionMessageQueueService.GetLock`, the lock
   `OnTurnEndAsync` holds at line 1478), then `CatchUpTranscriptAsync` for the session.
5. Build `MidTurnInterruptFacts` from: the task re-read under the lock; the session row; the
   runner's `GetAsync` (status, `AcceptedStartedAt`, `LastSequence`), with a thrown
   `NotFound`/transport error recorded as `runner-missing`; `TranscriptWorkingStateQuery` through
   `SessionMessageQueueService.IsWorkingAsync` (line 5111); the row re-read; `HasOpenQuestionToolAsync`
   (lines 556-580) and `IsModalBlockedAsync`; the runner snapshot classified by
   `GrokComposerScreen.Classify`; the capability probe for `ConditionalMaintenanceInputV1`; the
   `DelegationSettings.GrokMidTurnInterruptEnabled` flag.
6. `MidTurnInterruptPolicy.Decide(facts)` (pure). A refusal records `refused:<reason>` and releases
   the lock.
7. One `SendConditionalInputAsync(session, new RunnerConditionalInputRequest(session.StartedAt,
   runnerLastSequence, "\x03"))` through `AgentSessionRuntime` (line 1540). `Written` records
   `written`; any other outcome, or a thrown client error, records `not-confirmed:<outcome>`
   (`unknown` for a throw). The row is never changed by this step.
8. Release the lock, persist the outcome against the `Refined` event, publish, and return the
   summary with `interruptWritten` and `refinementDelivered: pending`.

### 2.2 Positive conditions (all must hold; one flip refuses by the named reason)

| P | Condition | Refusal |
|---|---|---|
| P-1 | `GrokMidTurnInterruptEnabled` is true (default set by S0: true only when CP-1 measured a cancelled boundary from one `\x03` on an empty composer) | `disabled` |
| P-2 | request `interruptCurrentTurn` is true | `not-requested` (today's path, no interrupt reported) |
| P-3 | task status Dispatched or Working under the lock | `task-status` |
| P-4 | `task.AgentSessionId` is a Guid equal to the row's `AgentSessionId` | `no-session` / `row-session-mismatch` |
| P-5 | `session.AgentKind == Grok` | `kind-not-grok` |
| P-6 | `session.Status == Running` | `session-not-running` |
| P-7 | runner reports the session and it has not exited | `runner-missing` / `runner-exited` |
| P-8 | runner `AcceptedStartedAt` is non-null and equals `session.StartedAt` | `generation-unproven` / `generation-mismatch` |
| P-9 | working after catch-up (`IsWorkingAsync` true) | `not-working` |
| P-10 | the marked row is `Pending` with `DeliveryAttempts == 0` | `already-sent` (Sent) / `row-not-pending` (Canceled) / `row-attempted` |
| P-11 | no open question tool and not modal-blocked | `question-open` / `modal-blocked` |
| P-12 | snapshot carries the session generation and classifies `Empty` | `composer-not-empty` (Draft) / `composer-unreadable` (Unreadable or wrong generation) |
| P-13 | runner advertises `ConditionalMaintenanceInputV1` | `conditional-input-unsupported` |

`GrokComposerScreen.Classify(rendered)`: `Empty` when the last non-blank line is exactly the
measured empty-composer prompt from CP-1's fixture (`tests/Antiphon.Tests/Fixtures/grok-empty-composer.txt`,
trailing spaces ignored); `Draft` when that line starts with the prompt glyph and carries any
other character; `Unreadable` for a null or blank screen, a screen whose last non-blank line is
not a prompt line (overlay, question tool, pager), or a prompt line that is not the last
non-blank line.

### 2.3 Result vocabulary

`interruptWritten` is one of `written`, `already-sent`, `refused:<reason>` (reasons from §2.2),
`not-confirmed:<stale-observation|generation-mismatch|missing|exited|unsupported|unknown>`.
`refinementDelivered` is `pending` from the request and becomes `delivered` only when the queue
row's `DeliveryVerdict` is the confirmed value from a complete `UserPrompt`; `delegate.ps1 -Status`
reads it from the queue row. `delegate.ps1 -Refine <task> <message> -Interrupt` posts
`interruptCurrentTurn = $true` and a fresh `requestId` and prints both fields on separate lines.

## 3. Verification design

### Inspection

Bodies read in full: the plan at `a3c4d26b`; `tests/Antiphon.Tests/Application/AgentTaskRefineTests.cs`
(whole, 287 lines: 7 methods, 9 results, `ScopeFactory`, `SeedTaskAsync`, `TempWorkspace`);
`tests/Antiphon.Agents.Pty.Tests/GkSession.cs` (eligibility 16-24, `LaunchArgs` 99-101,
`ReadUpdates` 114-170, `GrokUpdateRow` 219-220); `GrokCanaryTests.cs` lines 1-50 and 327-410 (the
Esc canary, its waits and cleanup) and 636-673 (helpers); `ClaudeInterruptCanaryTests.cs` 30-60
and 140-160; `GkSessionFollowUpBehaviorTests.cs`; `DelegateScriptRunnerSwitchTests.cs` 1-50;
`GrokDelegateEndToEndTests.cs` 1-130 and the Windows skip guards; `LandReceiptScanHarness.cs`
member index and `ScriptedTranscriptRunner` 360-405; `RemoteControlRecoveryHarness.cs` 300-410
(`ScriptedRcRunner`, `ConditionalCalls`, `RawInputs`, `AdvertiseConditional`);
`RemoteControlMaintenanceQueueTests.cs` 430-450 (the `Unknown` transport pin). Production:
`AgentTaskReplyService.cs` 540-710 (`HasOpenQuestionToolAsync`, `RefineAsync`) and 2560-2650;
`SessionMessageQueueService.cs` 740-800 (row insert), 1350-1420 (SendNow gates), 1474-1510
(`OnTurnEndAsync` under `GetLock`), 5105-5115 (`IsWorkingAsync`); `AgentSessionRuntime.cs` 59-102
(constructors), 470-495 (`IsTurnBoundary`), 1530-1548 (`SendConditionalInputAsync`);
`RemoteControlRecoveryService.cs` 325-392 (the existing fenced-write order this design mirrors)
and 648-672; `src/Antiphon.SessionRunner/Program.cs` 300-400 (`/input`, `/conditional-input`);
`src/Antiphon.SessionRunner.Contracts/ConditionalInput.cs` (whole) and `SessionRunnerContracts.cs`
401-410, 467-468; `GrokTranscriptNormalizer.cs` header and 370-390; `src/Antiphon.FakeGrok/Program.cs`
735-770 and its env-knob roster; `scripts/delegate.ps1` 255-285 and 894-906;
`server/Api/Endpoints/AgentTaskEndpoints.cs` 262-278; `AgentTaskDtos.cs` 629-632; the enums
`AgentKind`, `SessionStatus`, `QueuedMessageStatus`, `AgentTaskStatus`;
`SessionQueuedMessage.DeliveryVerdict` (line 115). Documents: `docs/testing-and-build.md`
(manifest 295-452, runner tool 453-464, build slots, PC execution 826-918),
`docs/session-runtime-invariants.md` 485-505 (the named-test deferral style).

Boundaries and where each lands: every `AgentTaskStatus` value (V-1 rows `status-*`, `positive`,
`positive-dispatched`; V-3 M2 `blocked-status`, `queued-status`, `settled-succeeded`); every
`AgentKind` but Grok (V-1 `kind-*`; M2 `kind-claude`); every `SessionStatus` but Running (V-1
`session-*`; M2 `session-stopping`); runner missing versus exited versus generation null versus
unequal (V-1, M2); working versus a boundary already persisted (V-1 `not-working`; M2
`already-sent-at-boundary`); row Pending/Sent/Canceled and attempts 0/1 (V-1); question tool open
versus answered (V-1; M2 `open-question`); composer Empty/Draft/Unreadable, blank, null, overlay,
prompt-not-last, trailing spaces (V-2; M2); capability present/absent (V-1; M2); every
`ConditionalInputOutcomes` value plus a throw (M3); same versus different `requestId` (M4);
complete, head-only and absent `UserPrompt` after the cancelled boundary; a flush transport failure
(M5); real Grok with an empty composer versus a draft (V-6). Excluded: Raw/OpenCode sessions beyond
the kind flip (no delivery contract exists); queue age or priority (plan: no timer); Esc on any kind
(plan: not substituted); ACP `session/cancel` (plan: out of scope).

Missing setup the new tests need (all in `tests/`, none in production):

1. `RecordingInterruptRunner : ISessionRunnerClient` in `tests/Antiphon.Tests/TestHelpers/`:
   `ConditionalCalls`, `RawInputs`, `KillCalls`; scripted `Session` (status, `AcceptedStartedAt`,
   `LastSequence`), `ThrowOnGet`, `Snapshot` (rendered, generation), `NextEntries` per session,
   `AdvertiseConditional`, `ConditionalOutcomeOverride`, `ThrowOnConditional`, `ThrowOnInputOnce`,
   and `OnConditional` (a callback the order proof uses to count rows at the moment of the write).
2. `AgentTaskMidTurnRefineTests.ScopeFactory`: the refine fixture's factory with the recording
   runner registered as `ISessionRunnerClient` and `AgentSessionRuntime` built on it; `SeedTaskAsync`
   gains `AgentKind kind = AgentKind.Grok` and `SessionStatus status = Running`; a `SeedWorkingAsync`
   that ingests `[UserPrompt 40 (brief), ToolCall 41]` through the runtime so `IsWorkingAsync` is
   true from persisted rows, not fixture SQL.
3. `tests/Antiphon.Tests/Fixtures/grok-empty-composer.txt`: the ready-screen tail CP-1 logs
   (`MeasurementLog` output), committed by the S0 slice with the measured `grok --version` line
   as its first comment line.
4. `GrokMidTurnInterruptCanaryTests.WaitForRowAsync` on `Stopwatch.GetTimestamp()` (T-4) and
   `GrokVersion()` (runs `grok.exe --version`, logged, never asserted).
5. FakeGrok `ANTIPHON_FAKE_CTRL_C_CANCELS` and a turn hold for the end-to-end arm: Code reuses
   `ANTIPHON_FAKE_REPORT_HOLD` if it holds the turn in flight, otherwise adds
   `ANTIPHON_FAKE_TURN_HOLD_MS` (T-2).

### Delivery inventory

One delivery path is touched and one is added.

**Path A, the refinement (existing, now reachable sooner).** Producer: `RefineAsync` persisting one
`WhenIdle` row (`EnqueueAsync`, `SessionMessageQueueService.cs` lines 740-800) with the task marker
and `ConversationKey refine:<task>:<requestId>`. Destination: the delegate's pty, typed by the flush
`OnTurnEndAsync` runs under the session lock after a boundary. Persistence boundary: the row insert
(`SaveChangesAsync` line 784) and the `Sent` stamp on flush. Recovery: a failed flush reverts the row
to `Pending` for the next boundary and the stranded-queue sweep (`FlushStrandedQueuesAsync`).
Observable receipt: the row's `DeliveryVerdict` confirmed from a complete `UserPrompt` transcript row
whose sequence is above the row's delivery baseline. Durable identity: `task.Id` → `Refined` event →
`row.Id`/`ConversationKey` → `RawInputs` body → `TranscriptEntries(session).UserPrompt`.

**Path B, the key (new).** Producer: step 7 of §2.1, one `RunnerConditionalInputRequest` whose
`Input` is `\x03`, fenced on `session.StartedAt` and the runner's `LastSequence`. Destination: the
runner's `/sessions/{id}/conditional-input` and the Grok pty. Persistence boundary: none for the
key itself; the outcome string is persisted against the `Refined` event. Recovery: none, by design
(an uncertain outcome is reported, never retried). Observable receipt: a `TurnEnd` with
`stop_reason=cancelled` for the same session and generation, which is also what triggers Path A's
flush. Durable identity: `requestId` → outcome on the event → the cancelled `TurnEnd` sequence →
the Path A `UserPrompt` above it.

Producer-to-recipient evidence and substitutes:

- **Real queue, scripted pty (V-3 M5).** The row is persisted by the production `RefineAsync`, the
  cancelled boundary is ingested through the production runtime from the recording runner's
  `NextEntries`, the flush is the production `OnTurnEndAsync`, and the typed body is what the
  recording runner received in `RawInputs`. The `UserPrompt` receipt is ingested through the same
  runtime path and judged by the production verdict. Substitute declared: the pty and the Grok
  process are the recording runner; what it cannot prove is that a real TUI consumed `\x03` and
  wrote the cancelled row. That is V-6 (real Grok, S0) and V-5 (FakeGrok through the real runner,
  tailer and normalizer on Windows).
- **Busy recipient:** M5 `cancelled-then-prompt` (the turn was in flight when the row was queued;
  the key makes the boundary; the flush types; the prompt confirms). M2 `already-sent-at-boundary`
  (the natural boundary arrives first; the flush types; the key is withheld and reported).
- **One already eligible:** M2 `already-sent-at-boundary` and V-5, where the brief is already
  delivered when the refinement is queued.
- **Crash or enqueue failure at each handoff:** M3 `throws` (client error on the key: outcome
  `not-confirmed:unknown`, row Pending, no retry); M5 `flush-transport-failure-then-retry` (the
  flush's input throws once: row reverted Pending, attempts 1, typed on the next boundary); M4
  `after-not-confirmed` (an HTTP retry of the same request creates nothing and presses nothing).
- A request, queue insert, `Sent` flag, `ConditionalCalls` entry, `interruptWritten: written` or a
  screen redraw is accepted as delivery by no assertion below.

### Proves it works now

Slice S0 (Windows headed lane, test code only; `tests/Antiphon.Agents.Pty.Tests/GrokMidTurnInterruptCanaryTests.cs`,
`[Category("Headed")] [Category("OptIn")] [Category("HeadedCanary")] [Explicit] [NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]`, `GkSession.SkipIfNotEligible()` first). Real `grok.exe`,
`GkSession.LaunchArgs`, modern backend, 120x30, fresh cwd, cleanup as `GrokCanaryTests` lines 400-404.

- V-6: a single Ctrl+C on an empty composer cancels the turn and leaves the session alive | headed |
  `GrokMidTurnInterruptCanaryTests.Ctrl_C_on_an_empty_composer_mid_tool_turn_cancels_the_turn_and_keeps_the_session_alive` |
  submit "Use run_terminal_command to run exactly: powershell -Command Start-Sleep 90; then reply done."
  wait (`WaitForRowAsync`, 2 min) for a `tool_call` row; `rowsBefore = ReadUpdates.Count`; write
  exactly `"\x03"` once; wait 30 s for a `turn_completed`; decisive:
  `newRows.Count(r => r.Kind == "turn_completed").ShouldBe(1)` and that row's
  `StopReason.ShouldBe("cancelled")`; `runner.Exited.IsCompleted.ShouldBeFalse()`; typed
  `GK-AFTER-CTRLC` echoes on screen within 5 s; then submit "Reply with exactly GK-SECOND-TURN-OK and use no tools"
  and expect a `user_message_chunk` containing it and a later `turn_completed` with
  `stop_reason == "end_turn"`. Logs: `grok --version`, row kinds in order, `_meta` of the cancelled
  row, the ready-screen tail (the fixture source). Mutation: the design cannot be mutated in
  production; its red is a changed vendor contract (no cancelled row, or the process exits).
- V-6b: Ctrl+C with a draft does not kill the session | headed |
  `GrokMidTurnInterruptCanaryTests.Ctrl_C_with_a_draft_in_the_composer_keeps_the_session_alive` |
  same tool turn; type `GK-DRAFT` with no Enter; one `\x03`; decisive: `runner.Exited.IsCompleted.ShouldBeFalse()`
  after 10 s and the composer echoes typed text afterwards; whether a cancelled row appears is
  logged, not asserted (the server refuses on a draft regardless, P-12).

Slice S1 (policy lane, no database; `tests/Antiphon.Tests/Application/MidTurnInterruptPolicyTests.cs`
and `GrokComposerScreenTests.cs`, `[Category("Unit")]`). Positive fixture `Positive()`: enabled true,
requested true, status Working, task session A, row session A, kind Grok, session Running, runner
found and not exited, runner generation equal to A's `StartedAt`, working true, row Pending with
attempts 0, no open question, not modal-blocked, composer `Empty`, conditional capability true.
Each row changes exactly one member from a fresh copy; expected outcomes are hand-written.

- V-1: the whitelist admits only the positive shape | policy |
  `MidTurnInterruptPolicyTests.C0491_AdmitsTheKeyOnlyWhenEveryPositiveHolds(string flip)`, 33 rows |
  `MidTurnInterruptPolicy.Decide(facts).ShouldBe(expected, flip)`. Mutation per row: delete that
  conjunct in `Decide`; the row's `ShouldBe` goes red.

| Rows | Flip | Decision |
|---|---|---|
| `positive`, `positive-dispatched` | none; status Dispatched | `Admit` |
| `not-requested` | requested false | `Refuse("not-requested")` |
| `setting-disabled` | enabled false | `Refuse("disabled")` |
| `status-queued`, `status-blocked`, `status-succeeded`, `status-failed`, `status-canceled` | status | `Refuse("task-status")` |
| `session-null` | task session null | `Refuse("no-session")` |
| `row-session-differs` | row session B | `Refuse("row-session-mismatch")` |
| `kind-claude`, `kind-codex`, `kind-opencode`, `kind-raw` | kind | `Refuse("kind-not-grok")` |
| `session-created`, `session-starting`, `session-stopping`, `session-stopped`, `session-failed` | session status | `Refuse("session-not-running")` |
| `runner-missing` | runner not found | `Refuse("runner-missing")` |
| `runner-exited` | runner exited | `Refuse("runner-exited")` |
| `generation-unknown` | runner generation null | `Refuse("generation-unproven")` |
| `generation-mismatch` | runner generation A's `StartedAt` minus one hour | `Refuse("generation-mismatch")` |
| `not-working` | working false | `Refuse("not-working")` |
| `row-sent` | row Sent | `Refuse("already-sent")` |
| `row-canceled` | row Canceled | `Refuse("row-not-pending")` |
| `attempts-one` | attempts 1 | `Refuse("row-attempted")` |
| `open-question` | question open | `Refuse("question-open")` |
| `modal-blocked` | modal true | `Refuse("modal-blocked")` |
| `composer-draft` | `Draft` | `Refuse("composer-not-empty")` |
| `composer-unreadable` | `Unreadable` | `Refuse("composer-unreadable")` |
| `conditional-unsupported` | capability false | `Refuse("conditional-input-unsupported")` |

- V-2: the composer classifier is fail-closed | policy |
  `GrokComposerScreenTests.C0491_ClassifiesTheComposer(string arm)`, 8 rows |
  `GrokComposerScreen.Classify(screen).ShouldBe(expected, arm)`. Rows: `empty-measured` (the
  fixture file) → `Empty`; `empty-trailing-spaces` → `Empty`; `draft-ascii` (prompt + `GK-DRAFT`) →
  `Draft`; `draft-marker` (prompt + `[antiphon-task:`) → `Draft`; `null-screen` → `Unreadable`;
  `blank-screen` (whitespace only) → `Unreadable`; `overlay-no-prompt` (the Ctrl+G tasks overlay
  shape, no prompt line) → `Unreadable`; `prompt-not-last` (prompt line followed by a non-blank
  line) → `Unreadable`. Mutation: return `Empty` for a null screen; `null-screen` goes red.

Slice S1 (CLI; `tests/Antiphon.Tests/Scripts/DelegateScriptInterruptSwitchTests.cs`, `[Category("Unit")]`,
the `Script()`/`Block()` readers of `DelegateScriptRunnerSwitchTests`).

- V-4: the switch is refine-only and the output never conflates the two statuses | policy | three
  methods: `Interrupt_is_a_refine_only_switch` (declared once, `ParameterSetName = 'Refine'`, absent
  from Create/Reply/Continue); `Interrupt_posts_the_flag_and_a_fresh_request_id` (the Refine case
  body contains `interruptCurrentTurn = [bool]$Interrupt` and `requestId = [guid]::NewGuid()`);
  `Interrupt_output_prints_interruptWritten_and_refinementDelivered_on_separate_lines` (the block
  contains `$summary.interruptWritten` and `$summary.refinementDelivered` in two `Write-Output`
  lines and the non-interrupt line "will land between its turns" is unchanged). Mutation: delete
  the `refinementDelivered` output line; the third method goes red.

Slice S1 (managed PostgreSQL lane; `tests/Antiphon.Tests/Application/AgentTaskMidTurnRefineTests.cs`,
`[Category("Integration")] [NotInParallel("AgentQueue")]`, fixture per T-3 with the recording
runner scripted: session Running, `AcceptedStartedAt == session.StartedAt`, `LastSequence 41`,
`NextEntries` = nothing new, snapshot = the fixture screen with the session generation, capability
advertised, outcome `Written`). Every method asserts `KillCalls == 0`, task status unchanged, and
that no `RawInputs` entry equals `"\x03"` or `"\x1b"`.

- V-3: the service builds the facts in §2.1 order and acts on the decision | integration | five
  methods, 31 results:

Method 1, `C0491_InterruptWritesOneConditionalCtrlCAfterTheMarkedRow(string arm)`, rows
`working`, `dispatched`. Decisive, in order: exactly one Pending row for the session with the task
marker, the message and `ConversationKey == $"refine:{task.Id:N}:{requestId:N}"`;
`ConditionalCalls` has exactly one entry: the session id, `Input == "\x03"`,
`ExpectedAcceptedStartedAt == session.StartedAt`, `ExpectedLastSequence == 41`; `OnConditional`
observed `1` persisted row at the moment of the write (row precedes key); `RawInputs` empty;
response `interruptWritten == "written"` and `refinementDelivered == "pending"`; exactly one
`Refined` event; the row is still Pending (a key is not delivery).

Method 2, `C0491_RefusalKeepsTheRowPendingAndSendsNoKey(string flip)`, 14 rows. Decisive on
every row: `ConditionalCalls` empty; the response's `interruptWritten` equals the named refusal;
task status unchanged.

| Row | Arrange | Also expect |
|---|---|---|
| `setting-disabled` | `GrokMidTurnInterruptEnabled = false` | one Pending row; `refused:disabled` |
| `kind-claude` | session kind ClaudeCode | one Pending row; `refused:kind-not-grok` |
| `session-stopping` | session Stopping | one Pending row; `refused:session-not-running` |
| `runner-missing` | `ThrowOnGet` NotFound | one Pending row; `refused:runner-missing` |
| `runner-exited` | runner status Exited | `refused:runner-exited` |
| `generation-mismatch` | runner generation one hour earlier | `refused:generation-mismatch` |
| `generation-unproven` | runner generation null | `refused:generation-unproven` |
| `already-sent-at-boundary` | `NextEntries` = `[TurnEnd end_turn 42]` so the catch-up flushes | `RawInputs == [row.Body]` typed once by the flush, row Sent, `interruptWritten == "already-sent"` |
| `open-question` | `ToolCall` 41 is a question tool with no `ToolResult` | `refused:question-open` |
| `composer-draft` | snapshot prompt line + `GK-DRAFT` | `refused:composer-not-empty` |
| `composer-unreadable` | snapshot null | `refused:composer-unreadable` |
| `conditional-unsupported` | `AdvertiseConditional = false` | `refused:conditional-input-unsupported`; `RawInputs` empty (no raw fallback) |
| `queued-status` | task Queued (T-5) | brief amended as today, 0 rows, `refused:task-status` |
| `blocked-status` | task Blocked (T-5) | today's `ConflictException` ("ANSWER"), 0 rows, 0 events |

Method 3, `C0491_UncertainWriteLeavesTheRowPendingAndNeverRetries(string outcome)`, 7 rows:
`ConditionalOutcomeOverride` in `stale-observation`, `generation-mismatch`, `missing`, `exited`,
`unsupported`, `unknown`, and `throws` (`ThrowOnConditional` = `HttpRequestException`). Decisive:
`ConditionalCalls.Count == 1`; row Pending with attempts 0; `interruptWritten == $"not-confirmed:{outcome}"`
(`unknown` for `throws`); `KillCalls == 0`; task Working.

Method 4, `C0491_ReplayOfTheSameRequestIsIdempotent(string arm)`, 4 rows: `after-written`,
`after-not-confirmed` (override `unknown`), `after-refused` (kind ClaudeCode), each a second
`RefineAsync` with the same `requestId`; decisive: rows 1, `Refined` events 1, `ConditionalCalls`
count unchanged (1, 1, 0) and the second response equal to the first. `different-request-id`:
a second request with a fresh id and the same still-working script yields 2 rows, 2 events,
2 calls (distinct requests are distinct; the live evidence is the gate).

Slice S2 (same class and lane).

Method 5, `C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(string arm)`,
4 rows, arranged as Method 1 `working` after a written key.

| Row | Then | Decisive |
|---|---|---|
| `cancelled-then-prompt` | ingest `[TurnEnd cancelled 42]` through the runtime; then `[UserPrompt 43 = row.Body, ToolCall 44]` | after the boundary: task Working, `RawInputs == [row.Body]`, row Sent, `DeliveryVerdict` null; after the prompt: `DeliveryVerdict` is the confirmed value and `refinementDelivered` reads `delivered`; no settlement event |
| `cancelled-then-head-only-prompt` | prompt is `row.Body[..300]` | row Sent; `DeliveryVerdict` not confirmed; `refinementDelivered == "pending"`; `RawInputs.Count == 1` (no retype) |
| `cancelled-then-no-prompt` | only `[ToolCall 43]` | as above, `RawInputs.Count == 1` |
| `flush-transport-failure-then-retry` | `ThrowOnInputOnce`; ingest cancelled 42, then `[TurnEnd end_turn 43]` | after 42: row Pending, attempts 1, task Working; after 43: `RawInputs == [row.Body]`, row Sent |

Mutations for V-3: Method 1 red when the key is written before the row (`OnConditional` sees 0),
when the input is `\x1b`, or when `refinementDelivered` is set from the key; Method 2 red when a
conjunct is skipped or when `SendInputAsync` is used as a fallback; Method 3 red when the write is
retried or the outcome is reported as `written`; Method 4 red when the replay lookup is skipped;
Method 5 red when a cancelled `TurnEnd` is treated as a report boundary or a head-only prompt
confirms.

Slice S2 (docs; `tests/Antiphon.Tests/Application/MidTurnInterruptDocumentationTests.cs`, `[Category("Unit")]`).

- V-7: the invariant doc defers to the named tests and uses no absolute words | policy, file read |
  `MidTurnInterruptDocumentationTests.C0491_InvariantNamesTheTestsAndAvoidsAbsolutes`, 1 result;
  reads `docs/session-runtime-invariants.md` with the `RunnerBranchContractDocumentationTests.Read`
  pattern; the CARD-0491 paragraph contains `Pinned by` followed by
  `AgentTaskMidTurnRefineTests.C0491_InterruptWritesOneConditionalCtrlCAfterTheMarkedRow`,
  `C0491_RefusalKeepsTheRowPendingAndSendsNoKey` and
  `C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt`, and that
  paragraph contains none of `always`, `never`, `every` (case-insensitive, whole words). Mutation:
  drop one named test from the paragraph; red.

Slice S2 (Windows ConPTY lane; `GrokDelegateEndToEndTests`, existing Windows and staged-binary
skips, FakeGrok per T-2).

- V-5: the key reaches a real child through the real runner and the refinement comes back as a
  complete `UserPrompt` | end-to-end |
  `GrokDelegateEndToEndTests.a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt`,
  1 result; FakeGrok child env `ANTIPHON_FAKE_CTRL_C_CANCELS=1`, the turn hold, `ANTIPHON_FAKE_INPUT_LOG`.
  Decisive, in transcript order: `UserPrompt` (brief) < `TurnEnd cancelled` < `UserPrompt` whose
  text is the complete refinement body with the task marker < `TurnEnd end_turn`; the input log
  holds exactly one `\x03`; the refine response was `interruptWritten == "written"`; the row is
  Sent with the confirmed verdict; the task is Working until FakeGrok's report settles it; the
  session is Running throughout. Mutation: delete the FakeGrok cancel arm or the server key;
  the cancelled row never appears and the ordered assertion goes red.

### Guards the regression

- R-1: today's refine contract (WhenIdle Pending row, Queued brief amendment, Blocked and settled
  refusals, empty refusal, file spill) is unchanged by the request plumbing | `AgentTaskRefineTests`
  (9 results); decisive `queued.Status.ShouldBe(QueuedMessageStatus.Pending)` and
  `CountAsync(...).ShouldBe(0)` on the Queued method.
- R-2: a cancelled turn end flushes but never settles | `AgentTaskReplyIntegrationTests`
  `a_cancelled_turn_end_does_not_settle_the_task_and_says_so`,
  `a_prior_internal_continue_does_not_make_a_cancelled_turn_a_report`,
  `the_next_end_turn_after_a_cancel_settles_normally` (3 results); decisive: the task status
  assertions after the cancelled boundary.
- R-3: the conditional-input transport reports `Unknown` on a lost reply and the recovery graph
  withholds on it (the client this design reuses untouched) | `RemoteControlMaintenanceQueueTests`
  (21 results); decisive `write.Outcome.ShouldBe(ConditionalInputOutcomes.Unknown)` at line 442.
- R-4: `delegate.ps1` parameter sets are unchanged by the new switch | `DelegateScriptRunnerSwitchTests`
  (3 results); decisive `Runner_is_a_create_only_parameter`.

### Guard inventory

| G | Plan ref and safety-critical guard | PC |
|---|---|---|
| G-1 | S1 / P-1: a disabled setting withholds the key | PC-1 |
| G-2 | S1 / P-5: a non-Grok session withholds the key (no broadcast) | PC-2 |
| G-3 | S1 / P-3: Queued, Blocked and settled tasks get no key | PC-3 |
| G-4 | S1 / P-6: a session not Running gets no key | PC-4 |
| G-5 | S1 / P-7: a missing or exited runner session gets no key | PC-5 |
| G-6 | S1 / P-8: an unproven or different generation gets no key | PC-6 |
| G-7a | S1 evidence order step 4: the transcript catch-up precedes the decision | PC-7 |
| G-7b | S1 / P-9: a session that is not working gets no key | PC-8 |
| G-8 | S1 / P-10: a row already Sent, Canceled or attempted gets no key | PC-9 |
| G-9 | S1 / P-11: an open question or modal withholds the key | PC-10 |
| G-10 | S1 / P-12: only an `Empty` composer admits; `Unreadable` refuses | PC-11 |
| G-11 | S1 / P-13: no raw `SendInputAsync` fallback when the runner lacks the capability | PC-12 |
| G-12a | S1: the marked row is persisted before the key | PC-13 |
| G-12b | S1: an uncertain outcome is never retried | PC-14 |
| G-13 | S1: the key is exactly one `\x03`, never Esc | PC-15 |
| G-14 | S1: a non-`Written` outcome reports `not-confirmed:<outcome>` and leaves the row Pending | PC-16 |
| G-15 | S1: a replayed `requestId` creates no row, event or key | PC-17 |
| G-16 | S2 / existing `IsReportBoundary`: a cancelled boundary never settles the task | PC-18 |
| G-17 | S2: `delivered` only from a complete `UserPrompt` | PC-19 |
| G-18 | S2: no kill, park or stop on any interrupt outcome | PC-20 |
| G-19 | S2: `interruptWritten: written` never sets `refinementDelivered` | PC-21 |
| G-20 | S2: the CLI prints the two statuses separately | PC-22 |
| G-21 | S0: the real vendor contract (one `\x03`, empty composer, cancelled row, session alive) | none: a measurement of the installed binary, not a production guard; its red is a vendor change, caught by rerunning CP-1 before raising the setting default |
| G-22 | T-2: the FakeGrok cancel arm | none: test tooling, exercised by V-5 |

Guards 22, mapped 20, justified-none 2, duplicate maps 0. The two `Refuse` families that share a
reason (`no-session`/`row-session-mismatch`, `runner-missing`/`runner-exited`, `already-sent`/
`row-not-pending`/`row-attempted`, `question-open`/`modal-blocked`) are one conjunct each in
`Decide` and one PC each; their sub-reasons are distinguished by V-1 rows, not by separate code.

### Positive controls

Mutation runs each PC method-scoped after land: baseline, red, exact restore, green, with the
method prefix as the filter so every argument row runs and the red is the named row's assertion.
Code runs only V/R rows; Review judges this roster before land. A zero-test run, build failure or
fixture error is not a red. Filters: `POLICY` =
`/*/*/MidTurnInterruptPolicyTests/C0491_AdmitsTheKeyOnlyWhenEveryPositiveHolds*`; `M1` =
`/*/*/AgentTaskMidTurnRefineTests/C0491_InterruptWritesOneConditionalCtrlCAfterTheMarkedRow*`;
`M2` = `.../C0491_RefusalKeepsTheRowPendingAndSendsNoKey*`; `M3` =
`.../C0491_UncertainWriteLeavesTheRowPendingAndNeverRetries*`; `M4` =
`.../C0491_ReplayOfTheSameRequestIsIdempotent*`; `M5` =
`.../C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt*`.

- PC-1: in `MidTurnInterruptPolicy.Decide` delete the `Enabled` conjunct; expect `POLICY` red at
  `setting-disabled` and `M2` red at `setting-disabled`'s `ConditionalCalls.ShouldBeEmpty()`.
- PC-2: delete the `Kind == Grok` conjunct; expect `POLICY` red at `kind-claude` (and the three
  other kinds) and `M2` red at `kind-claude`.
- PC-3: admit `Blocked` in the status conjunct; expect `POLICY` red at `status-blocked`.
- PC-4: admit `Stopping`; expect `POLICY` red at `session-stopping` and `M2` red at `session-stopping`.
- PC-5: treat a thrown runner `GetAsync` as found-and-running in `RefineAsync` fact building;
  expect `M2` red at `runner-missing`'s `ConditionalCalls.ShouldBeEmpty()`.
- PC-6: compare generations only when the runner value is non-null and otherwise admit; expect
  `POLICY` red at `generation-unknown`.
- PC-7: remove the `CatchUpTranscriptAsync` call before the decision; expect `M2` red at
  `already-sent-at-boundary`'s `ConditionalCalls.ShouldBeEmpty()` (the stale working state admits
  the key).
- PC-8: delete the `Working` conjunct; expect `POLICY` red at `not-working`.
- PC-9: delete the row-status/attempts conjunct; expect `POLICY` red at `row-sent`, `row-canceled`
  and `attempts-one`.
- PC-10: delete the question/modal conjunct; expect `POLICY` red at `open-question` and `M2` red
  at `open-question`.
- PC-11: in `GrokComposerScreen.Classify` return `Empty` for a null or blank screen; expect
  `/*/*/GrokComposerScreenTests/C0491_ClassifiesTheComposer*` red at `null-screen` and
  `blank-screen`, and `M2` red at `composer-unreadable`.
- PC-12: on `conditional-input-unsupported` call `_runtime.SendInputAsync(session, "\x03")`
  instead of refusing; expect `M2` red at `conditional-unsupported`'s `RawInputs.ShouldBeEmpty()`.
- PC-13: move the conditional write before `queue.EnqueueAsync`; expect `M1` red at the
  `OnConditional` row-count assertion (`ShouldBe(1)` sees 0).
- PC-14: on `Unknown` issue the conditional write a second time; expect `M3` red at `unknown`'s
  `ConditionalCalls.Count.ShouldBe(1)`.
- PC-15: write `"\x1b"` instead of `"\x03"`; expect `M1` red at `Input.ShouldBe("\x03")`.
- PC-16: map every outcome to `written`; expect `M3` red at `stale-observation`'s
  `interruptWritten.ShouldBe("not-confirmed:stale-observation")`.
- PC-17: skip the replay lookup (step 2); expect `M4` red at `after-written`'s row count
  `ShouldBe(1)` (2 rows, 2 calls).
- PC-18: in `TranscriptKinds.IsReportBoundary` drop the `Cancelled` exclusion; expect `M5` red at
  `cancelled-then-prompt`'s `Status.ShouldBe(AgentTaskStatus.Working)` after the boundary
  (R-2's three results also go red; the named red is `M5`).
- PC-19: set the confirmed `DeliveryVerdict` from `PromptSubmissionMatch.IsConfirmedBy` alone
  (head match) in the verification pass; expect `M5` red at `cancelled-then-head-only-prompt`'s
  `refinementDelivered.ShouldBe("pending")`.
- PC-20: on a `not-confirmed` outcome call `_runnerClient.KillAsync(session)`; expect `M3` red at
  `KillCalls.ShouldBe(0)`.
- PC-21: set `refinementDelivered = "delivered"` whenever the key is `Written`; expect `M1` red at
  `refinementDelivered.ShouldBe("pending")`.
- PC-22: in `scripts/delegate.ps1` print `refinementDelivered` from `$summary.interruptWritten`;
  expect `/*/*/DelegateScriptInterruptSwitchTests/Interrupt_output_prints_interruptWritten_and_refinementDelivered_on_separate_lines`
  red.

Batchable (different files and methods, no interaction): PC-11 with PC-22; PC-18 with PC-15.
Every other control runs alone. PC-5, PC-7, PC-12, PC-13, PC-14, PC-16, PC-17, PC-20 and PC-21
mutate `AgentTaskReplyService.RefineAsync` or its helper and run one at a time.

### Out of scope

- A timer, age or priority policy that cancels a busy turn (plan: explicitly not in this slice).
- Esc for any kind, Codex and Claude mid-turn delivery, ACP `session/cancel`, a change to Grok's
  `config.toml` or `follow_up_behavior` (plan).
- The body transport, spill ceiling and pointer path (`AgentTaskRefineTests` R-1 pins them).
- A real `OnTurnEndAsync` typing into a real Grok TUI on Linux: no Linux Grok lane exists; the
  Windows FakeGrok row (V-5) and the headed canary (V-6) are the substitutes, both declared.
- `modal-blocked` as an integration row: `IsModalBlockedAsync` depends on remote-control state
  that the refine fixture does not model; the conjunct is pinned at the policy seam (V-1) and by
  PC-10.
- Whole Unit, Integration or assembly runs: none; the closed table below is the ordinary scope.

### Checkpoints

CP-1 and CP-9 run on the Windows desktop checkout (real `grok.exe` and ConPTY); every other row
runs on the Linux runner mirror or Windows alike. Managed lanes only: no production runner, no
AppHost. Off Windows the checkpoint tool adds `UseAppHost=false`. Four isolated builds (S0 Pty,
S1 server, S2 server, S2 Windows end-to-end); every other row reuses its slice's build. CP-1 and
CP-9 are blocked, not skipped green, when the binary, the flag or ConPTY is unavailable: a skipped
result fails the row's `0 skipped` expectation. Omit `-Runner` and `-Platform`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S0 | `tests/Antiphon.Agents.Pty.Tests -> bin-c0491-canary/` | grok-interrupt-live | `/*/*/GrokMidTurnInterruptCanaryTests/*` | V-6, V-6b | 2 executed, 0 failed, 0 skipped; a real cancelled boundary and a later confirmed prompt | 2 | 24 | true | `ANTIPHON_HEADED_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c0491-policy/` | policy | `/*/*/(MidTurnInterruptPolicyTests*)\|(GrokComposerScreenTests*)/*` | V-1, V-2 | 41 executed, 0 failed/skipped | 41 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-2 | cli-switch | `/*/*/(DelegateScriptInterruptSwitchTests*)\|(DelegateScriptRunnerSwitchTests*)/*` | V-4, R-4 | 6 executed, 0 failed/skipped | 6 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | CP-2 | midturn-refine | `/*/*/AgentTaskMidTurnRefineTests/(C0491_InterruptWritesOneConditionalCtrlCAfterTheMarkedRow*)\|(C0491_RefusalKeepsTheRowPendingAndSendsNoKey*)\|(C0491_UncertainWriteLeavesTheRowPendingAndNeverRetries*)\|(C0491_ReplayOfTheSameRequestIsIdempotent*)` | V-3 (M1-M4) | 27 executed, 0 failed/skipped | 27 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | CP-2 | existing-refine | `/*/*/AgentTaskRefineTests/*` | R-1 | 9 executed, 0 failed/skipped | 9 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | CP-2 | rc-transport | `/*/*/RemoteControlMaintenanceQueueTests/*` | R-3 | 21 executed, 0 failed/skipped | 21 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c0491-delivery/` | cancelled-delivery | `/*/*/AgentTaskMidTurnRefineTests/C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt*` | V-3 (M5) | 4 executed, 0 failed/skipped | 4 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8a | S2 | CP-7 | cancelled-boundary | `/*/*/AgentTaskReplyIntegrationTests/(a_cancelled_turn_end_does_not_settle_the_task_and_says_so*)\|(a_prior_internal_continue_does_not_make_a_cancelled_turn_a_report*)\|(the_next_end_turn_after_a_cancel_settles_normally*)` | R-2 | 3 executed, 0 failed/skipped | 3 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8b | S2 | CP-7 | interrupt-docs | `/*/*/MidTurnInterruptDocumentationTests/*` | V-7 | 1 executed, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S2 | `tests/Antiphon.Tests -> bin-c0491-e2e/` | fakegrok-interrupt-e2e | `/*/*/GrokDelegateEndToEndTests/a_mid_turn_interrupt_refinement_reaches_a_working_fakegrok_delegate_as_a_complete_UserPrompt*` | V-5 | 1 executed, 0 failed, 0 skipped (Windows ConPTY, staged fakegrok.exe) | 1 | 14 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

TUnit 1.44 rejects the original CP-8 nested filter because a `/` inside a parenthesized
expression is an unexpected operator. That row is split, same `After` and build: `CP-8a` is
the three `AgentTaskReplyIntegrationTests` methods (3 executed) and `CP-8b` is
`MidTurnInterruptDocumentationTests` (1 executed).

Run through the checkpoint tool, one run per committed slice:
`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-09-card-0491-test-design.md --after S0 --expected-source-sha <S0 full sha>`
on the Windows desktop, then `--after S1` and `--after S2` (CP-9 on Windows, CP-7, CP-8a and
CP-8b on either); `wait` while the exit is 75; exit 4 is a slot timeout to report, never an unleased retry.
Bootstrap the tool through `scripts/build-slot.ps1` if it is not built; do not wrap the checkpoint
run in a second slot. Commit and push before each group and do not edit source while a run is in
flight. A red row is fixed and rerun as the same row; a production fix found in S2 reruns CP-2
through CP-6 as the same rows. Delete the four row-owned `bin-c0491-*` outputs after the last
green run. Run `scripts/check-evidence-diff.ps1` over the full Code task range. CP-1 is the gate
for S1's setting default: a red CP-1 ships S1 with `GrokMidTurnInterruptEnabled = false` and the
CP-1 measurement log attached to the Code report; S1-S2 still run their rows.

### Cost

- Ordinary V/R floor for Code (sum of EstimatedMinutes, CP-1..CP-9) = **83 minutes**, estimated:
  CP-1 24 (one Pty build about 4 minutes plus two real Grok tool turns, each held up to 90 s, with
  the 2-minute and 30-second waits bounded), CP-2 6 (one server build about 4 minutes warm plus 41
  policy results), CP-3 2, CP-4 12, CP-5 5, CP-6 8 (21 slow integration results, reused build),
  CP-7 7 (one build plus 4 results), CP-8a 4, CP-8b 1, CP-9 14 (one Windows build plus a real FakeGrok
  launch, a held turn and settlement). Reuse saves five builds against building per row, about
  15 minutes. Slot waits are outside the floor.
- Authoring and review: about **60 minutes** (S0 canary 15, S1 policy and service 30, S2 FakeGrok,
  docs and end-to-end 15).
- PC floor for Mutation (pending, post-land): 22 controls, 20 run alone and one batch of two pairs,
  each cycle baseline/red/restore/green on a method filter at about 2.5 minutes with a warm build
  (CP-2's and CP-7's builds reused as the baseline outputs; one rebuild per red and per green):
  **about 165 minutes** = 20 x 3 cycles x 2.5 + 2 batches x 3 cycles x 2.5. PC-18 is the longest
  (it also reruns R-2's results) and is budgeted at 5 per cycle inside that total.
- Total estimated: 83 (V/R) + 60 (authoring) + 165 (PC, pending) = **308 minutes**, of which 143
  minutes is this card's Code dispatch.

## 4. Collisions and slice order

S0 is test code only in `tests/Antiphon.Agents.Pty.Tests` and the fixture file; it collides with
nothing in flight. S1 touches `AgentTaskReplyService.RefineAsync`, `AgentTaskDtos.ReplyToAgentTaskRequest`,
`DelegationSettings`, `scripts/delegate.ps1` and adds two policy types; a Code task already in
flight on `AgentTaskReplyService.cs` defers S1. S2 touches `src/Antiphon.FakeGrok/Program.cs`,
`docs/session-runtime-invariants.md`, the delegate skill doc and `GrokDelegateEndToEndTests.cs`.
The first Code slice is S0 on the Windows desktop checkout; S1 and S2 may run on server2.
