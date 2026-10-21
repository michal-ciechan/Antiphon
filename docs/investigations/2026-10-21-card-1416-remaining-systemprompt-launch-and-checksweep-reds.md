# CARD-1416: remaining AgentSystemPromptLaunchTests and AgentTaskCheckSweepTests reds

Status: **all five reds are confirmed stale tests or fixtures. None is a product regression.**
Each first-failing commit was found by a build-and-run bisect on the server2 Linux runner. The
product change in each commit was deliberate, and the owner doc records it. The mechanisms are
traced to file:line. The Launch_note mechanism also has a row dump taken at the failing
assertion.

Scope is the part of CARD-1416 not taken by the parallel Code task, which owns M1, M2 and
`Second_start_on_a_live_session_is_a_no_op_and_does_not_rebootstrap`. Prior analysis:
`docs/investigations/2026-10-21-card-1416-windows-rc-bridge-harness-red-cluster.md` (M3, M4) on
`feat/card-task-35ac0c3e` at `327dcf8b`.

## Reproduction on Linux (master `47e6916c7`)

One build:
`scripts/build-slot.ps1 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1416b/`
(0 errors). Then one process per class through `build-slot.ps1` with
`--treenode-filter "/*/*/<Class>/*"`.

| Class | total | failed | notes |
|---|---:|---:|---|
| AgentSystemPromptLaunchTests (plain Linux) | 29 | **29** | every test: `ConflictException: Agent executable 'cmd.exe' was not found on PATH` at `AgentExecutableResolver.cs:95` |
| AgentSystemPromptLaunchTests (with a `cmd.exe` stub on PATH) | 29 | 5 | the same 5 names and messages as the Windows run |
| AgentTaskCheckSweepTests | 36 | 1 | `the_flush_cancels_a_settled_check_already_typed_once_before_it_retypes`: Status `Pending`, expected `Canceled` (line 733) |

New Linux finding: `BridgeQueueHarness.cs:246` and `AgentSystemPromptLaunchTests.cs:769` set
`Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe")`. On Linux `Environment.SystemDirectory`
is empty, so the value is a bare `cmd.exe` and `EnsureSpawnable` resolves it on PATH. The class is
therefore entirely red on Linux. The earlier "none is Windows-specific" holds per assertion, not
per class. Measurement workaround: an executable `#!/bin/sh exit 0` file named `cmd.exe` in a
scratch directory prepended to PATH. The harness never spawns it.

## Bisect method

Detached scratch worktrees, default `bin/`, `-p:UseAppHost=false`. Older csproj copies FakeClaude
into `bin/.../fakeclaude/`, which collides with the Linux apphost file `fakeclaude`. One build and
one exact-method run per step, through `build-slot.ps1`. Steps whose build failed were skipped
(exit 125).

Pitfall found during the bisect: the CARD-1259 WIP commits `dd039a405` and `04c38d088` cannot run
tests. `BeforeAssembly` fails with `PendingModelChangesWarning` because the schema arrives in a
later commit. A naive bisect scores them as "good". Both were discarded and the endpoints were
re-tested on runnable commits (below).

## Findings

| Test | First failing commit | Intended? | Verdict | Proposed fix |
|---|---|---|---|---|
| `Start_with_system_prompt_append_passes_flag_on_fresh_launch` | `344125472` (2026-09-09) | yes | test-stale | allow (assert) the single `StandingFreshSelected` incident instead of "no incidents" |
| `Resume_not_found_fallback_delivers_fresh_bootstrap` | `344125472` | yes | test-stale | rewrite to the strict-resume contract: 1 adapter, no bootstrap, continuity held `NativeSessionMissing` (or delete as already covered) |
| `Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap` | `344125472` | yes | test-stale | delete: the same-ID fallback it pins no longer exists |
| `Launch_note_yields_to_an_owed_channel_row` | two causes: `3dabf80a6` (2026-09-25) marks the body; `09c4c9a5f`+`bccea18b7` (2026-10-15) makes the note type early | yes / yes | test-stale (assertion + fixture) | expect the marked channel body; make the OnSubmitted UserPrompt visible to `SessionStateStore` |
| `the_flush_cancels_a_settled_check_already_typed_once_before_it_retypes` | `64ec5aad5` (2026-09-25) | yes | test-stale (fixture) | make the caller session live in the harness runner (`RecordingRunnerClient.ListAsync`) |

### L1-L3: CARD-0466 strict standing continuity (`344125472`) — confirmed by bisect

- Measured: at `1178affcc` (= `344125472^`) the class gives **24/24 passed**. At `344125472`
  it gives 4 failed of 24, the four names and messages recorded in `14e692a9f`. `Second_start`
  is the fourth; it belongs to the parallel Code task.
- `Start_with_system_prompt...` (assertion `AgentSystemPromptLaunchTests.cs:130`,
  `AgentIncidents.AnyAsync` is True). An explicit Fresh start of a standing ClaudeCode agent now
  writes an audit incident: `AgentControlService.cs:850-862`,
  `Kind = fresh ? StandingFreshSelected ...`, "Accepted explicit fresh conversation ...".
  That was added in `344125472`. `StandingSessionSelectionTests.cs:145` already asserts
  exactly one `StandingFreshSelected` for Fresh. The owner doc,
  `docs/session-runtime-invariants.md` "Standing conversation continuity (CARD-0466)", says
  "Fresh explicitly creates a new ID and keeps the old row".
  New expectation: exactly one incident, `Kind == StandingFreshSelected`, `SessionId` the new
  session. The rest of the test (flag, single bootstrap, all rows Sent) still passes.
- The two fallback tests (assertions `:327` `Created.Count` 1 instead of 2, and `:350` index out of
  range). `344125472` removed the `catch (ResumeTargetMissingException)` same-ID relaunch from
  `AgentSessionService`, which included the comment "Effective fresh: same session row, brand-new
  conversation — it must BOOTSTRAP". The failure now classifies `ContinuityUnavailable` and calls
  `StandingContinuityState.HoldAsync(..., NativeSessionMissing)` (`AgentSessionService.cs:477-478`).
  The owner doc says: "A failed resume never creates a replacement under the same ID." New
  expected behaviour: one adapter (the `--resume` attempt), no bootstrap typed, the session
  Failed, and `AgentSupervisionState.ContinuityReason == NativeSessionMissing` with a
  `StandingContinuityHeld` incident. `AgentSessionLaunchFailureTests` and
  `StandingContinuityRecoveryTests` already reference `NativeSessionMissing`. If they cover
  this path, the two tests can be deleted rather than rewritten.

### L4: `Launch_note_yields_to_an_owed_channel_row` — two independent stale points, confirmed by bisect and a row dump

1. **Channel marker (`3dabf80a6`, CARD-0584).** Measured: at `aef818b58` (= `3dabf80a6^`) the
   test **passes**. At `3dabf80a6` the first assertion fails with a single body
   `[antiphon-channel:<row-id:N>] [Telegram "AZ Care" — Mike 21:03] Give me message to Phil`.
   The queue wraps an untouched legacy Pending Channel row at delivery
   (`SessionMessageQueueService.cs:806/1054`, `ChannelPromptCorrelation.Mark`,
   `ChannelPromptCorrelation.cs:16`). This is the documented contract
   ("Only untouched legacy Pending rows may be wrapped", `docs/session-runtime-invariants.md`
   CARD-0584). The test still compares the unmarked literal. Windows and pre-CARD-1259 runs
   (`d994c20b7`, measured here) show only this symptom.
2. **Note types inside the owed turn (CARD-1259 S2, `09c4c9a5f` with schema `bccea18b7`).**
   Measured on runnable commits: `f0b38259c` (= `09c4c9a5f^`) shows a single marked body,
   which is good for this symptom. `bccea18b7` shows both bodies: the channel row, then
   `[session …] [System note … resumed after a restart …]`. `09c4c9a5f` alone cannot run
   (pending schema).
   Mechanism (row dump at the failing assertion on `47e6916c7`, scratch-only instrumentation):
   - Queue rows: Channel seq 1 `Sent`; System seq 2 `Sent`. So the CARD-0233 yield did choose
     `WhenIdle` / `Origin=System` (`AgentSessionService.cs:3425-3438`).
   - Transcript: seq 1 UserPrompt (channel) at 53.504; seq 2 UserPrompt (note) at 54.468. There is
     no TurnEnd. The note row was sent at 53.917, after the channel UserPrompt.
   - The SQL working query `SessionMessageQueueService.IsWorkingAsync` returns **True** at that
     point. The queue does not use it: `ReadWorkingAsync` (`SessionMessageQueueService.cs:5401-5402`)
     reads `_states.IsWorkingAsync` whenever `SessionStateStore` is registered and `Enabled`
     (default true, `SessionStateSettings.cs:5`). The projection is from CARD-0701 R1,
     `ea65b6441`.
   - `09c4c9a5f` registers `SessionStateStore` in `BridgeQueueHarness` (`BridgeQueueHarness.cs:176`)
     for the first time. The test's `OnSubmitted` writes the UserPrompt with the static
     `BridgeQueueHarness.InsertEntryAsync`, a raw `AppDbContext` insert that bypasses the store.
     The store keeps reading idle, and the WhenIdle note flushes into the owed turn.
   - The harness already has the store-aware pattern: `MarkWorkingAsync`
     (`BridgeQueueHarness.cs:500-507`) takes `BeginWriteAsync` and calls `ReconcileAsync`
     after the direct insert.
   Production ingests UserPrompts through the runtime and the store, so the store-bound working
   signal is the intended design. This is a fixture not migrated by CARD-1259, the same family as
   M1, and not a product defect. Uncertainty: the store's load/caching point was not traced line
   by line. The fixture explanation rests on the bypassing insert, the store-routed
   `ReadWorkingAsync`, and the SQL-vs-store disagreement in the dump.

New expectation: first `SubmittedBodies` is
`[ChannelPromptCorrelation.Mark(channelRowId, channelBody)]`. After the TurnEnd (inserted
store-visibly), it is `[marked channel body, RestartResumeBody-with-tag]`. The two DB checks on
the System note stay.

### C1: `the_flush_cancels_a_settled_check_already_typed_once_before_it_retypes` — confirmed by bisect and code

- Measured: `c050f6b28` (2026-09-22, last change to the test file) **passes**. The bisect over
  `c050f6b28..d994c20b7` gives first bad **`64ec5aad5`**
  "fix(CARD-0696): persist mentions and preserve outage input state". Its parent `2c3f3f18f` is good.
- Mechanism: `64ec5aad5` added a liveness and transport admission gate at the top of
  `DeliverNextLockedAsync`. On master that is `SessionMessageQueueService.cs:2206-2213`:
  `!await _runtime.IsLiveOrUnknownAsync(rulesSession, ct)` returns `FlushResult.Nothing`.
  At `2c3f3f18f` the method only required `Status == Running`. The CARD-0132 superseded-check
  cancellation (`SessionMessageQueueService.cs:2372-2396`) runs after that gate. The check-sweep
  harness's `RecordingRunnerClient.ListAsync` (`AgentTaskCheckSweepTests.cs:1573-1574`) returns
  `[]`, so the caller session is local and not live
  (`AgentSessionRuntime.cs:1958-1965` → `ListLocalLiveSessionsAsync`). The flush exits before
  the sweep and the note stays Pending. The test's own last assertion (`Runner.Writes` not empty)
  presumes a live, typeable caller.
- Intended: the owner doc entry "Immediate phone-home refusals preserve state (CARD-0696)" says
  automatic and immediate input checks the connection before mutating input state. No product
  defect: a non-live caller cannot be retyped into, and the next live flush still cancels the
  check before typing. The behaviour change is that DB-only superseded-check cleanup now waits
  for liveness. Whether that matters is a design question, not a regression.

## Linux and Windows

All five mechanisms are OS-independent. They are DI and fixture wiring, a documented wire marker,
and control flow. The `cmd.exe` spawn target is the only OS-specific item, and it is a separate
Linux-only fixture red covering the whole AgentSystemPromptLaunchTests class.

## Remaining uncertainty

- L4(2): the `SessionStateStore` load/caching point for the seeded session was not traced.
  The fix should be verified by running the test, not inferred.
- L2/L3: whether the existing CARD-0466 suites already cover the resume-not-found hold closely
  enough to delete rather than rewrite the two fallback tests was not checked test by test.
- L1: the incident kind is inferred from code (`AgentControlService.cs:850-862`) and the sibling
  assertion at `StandingSessionSelectionTests.cs:145`. The row itself was not dumped.

## Not done, noted

- Fix ideas, test-only: L1 assert one `StandingFreshSelected`. L2/L3 rewrite or delete for strict
  resume. L4 expect `ChannelPromptCorrelation.Mark(...)` and reconcile the store after the
  `OnSubmitted` insert. C1 have `RecordingRunnerClient.ListAsync` report the caller session
  `Running`. Separately, make the launch-test `Exe` portable (e.g. `/bin/sh` off Windows) so the
  class can join a Linux lane.
