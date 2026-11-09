# Run 231f573c (rc/2026-11-13) — `antiphon-all-group-4` Windows failure: Card1277PtyHostFixtureTests

Read-only extraction from the desktop qualification logs, 2026-11-09. No build or test was run.

- Run: `231f573c834141c2acab4fe0f0ef8847`, rc/2026-11-13, candidate checkout
  `C:\Antiphon\releases\checkout-0b36ae35` at `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`
  (the same SHA as this investigation's base; the test file is byte-identical).
- Logs: `C:\Antiphon\releases\candidates\rc-2026-11-13-653f1145de5b\logs\2026-11-09-0440-231f573c\`
  `antiphon-all-group-4.trx`, `antiphon-all-group-4-tests.log`, `antiphon-all-group-4.timing.json`,
  `antiphon-all-group-4-execution\log_261109054452910.diag` (the only file in the execution folder).

## Verdict (short)

A **test-side wait race**, not a product regression. The test polls the fake's JSONL only until the
`"type":"user"` record appears, then asserts on the assistant record that fakeclaude appends
*later* in the same turn. Under Windows load the poll woke in the gap between the two appends.
Both the JSONL and the screen snapshot show the fake was frozen exactly between "user record
appended" and "response painted": the transcript holds only the user line, and the screen ends at
`SUBMITTED:` with no `FAKE response to:` line. It is the first failure in 22 Windows Full runs.

## Failed test

| Field | Value |
|---|---|
| Test | `Antiphon.Tests.Agents.Card1277PtyHostFixtureTests.Claude_two_write_submit_is_a_normalizer_turn` |
| Category | `Integration`; class has `[NotInParallel(nameof(Card1277PtyHostFixtureTests))]`, `[ParallelLimiter<ProcessSpawnLimit>]` (limit 1) |
| Duration | `00:00:01.0230051` (start `05:44:56.0556930Z`, end `05:44:57.0786981Z`) |
| Failed line | `Card1277PtyHostFixtureTests.cs:107` (the `assistant.IndexOf(nonce)` assert); `:118` is the `finally` rethrow frame |
| Chunk result | total 140, passed 139, failed 1; TUnit duration 5 s 958 ms; run wall 6.671 s; MTP exit code 2 |
| Machine | `DESKTOP-KTLKPIF`, `ProcessorCount: 8`, `TotalAvailableMemoryBytes(GB): 34` (diag lines 14, 16) |
| Runtime | TUnit v1.44.0.0, .NET 9.0.22, Microsoft Testing Platform v2.2.2, win-x64, Windows 10.0.19045 |

### Error message (TRX `ErrorInfo/Message`; ESC shown as `\u001b`)

```
ShouldAssertException: assistant.IndexOf(nonce, StringComparison.Ordinal)
    should be greater than or equal to
0
    but was
-1

Additional Info:
    assistant text lost the nonce past the screen cut. screen=\u001b[2J\u001b[m\u001b[H\u001b]0;C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\bin\Debug\net9.0\fakeclaude\fakeclaude.exe\u0007\u001b[?25hFake Claude ready
\u001b]0;✳\u0007[antiphon-channel:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa] queued-channel-recovery-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
SUBMITTED:[antiphon-channel:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa] queued-channel-recovery-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb

jsonl={"type":"user","uuid":"674ff261-171f-478d-800c-88b0f6a840d7","timestamp":"2026-11-09T05:44:56.7264237Z","message":{"role":"user","content":"[antiphon-channel:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa] queued-channel-recovery-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}

user=[antiphon-channel:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa] queued-channel-recovery-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
assistant=
```

### Stack (complete as recorded; TUnit trimmed internals, under 40 lines)

```
   at Antiphon.Tests.Agents.Card1277PtyHostFixtureTests.Claude_two_write_submit_is_a_normalizer_turn() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Agents\Card1277PtyHostFixtureTests.cs:line 107
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Agents.Card1277PtyHostFixtureTests.Claude_two_write_submit_is_a_normalizer_turn() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Agents\Card1277PtyHostFixtureTests.cs:line 118
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at Antiphon.Tests.Agents.Card1277PtyHostFixtureTests.Claude_two_write_submit_is_a_normalizer_turn() in C:\Antiphon\releases\checkout-0b36ae35\tests\Antiphon.Tests\Agents\Card1277PtyHostFixtureTests.cs:line 118
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
   at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
   at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

StdOut: only `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Agents.Card1277PtyHostFixtureTests.1.1.Claude_two_write_submit_is_a_normalizer_turn.1.1.0`.

## Process-spawn / limiter / pwsh / git / XDG_CACHE_HOME / timeout text

- **Process spawn:** the only child is `fakeclaude.exe` under ConPTY via `PtyAgentRunner`
  (`...\bin\Debug\net9.0\fakeclaude\fakeclaude.exe`, visible in the OSC title of the screen
  snapshot). The banner and the prompt echo both arrived (the test passed lines 93-95 and 105).
- **Limiter:** `[ParallelLimiter<ProcessSpawnLimit>]` (`ProcessSpawnLimit.Limit == 1`) and
  `[NotInParallel(nameof(Card1277PtyHostFixtureTests))]`. The diag holds no limiter text.
- **pwsh / git / XDG_CACHE_HOME:** none of them appears in the TRX, the tests log or the diag for
  this test. The test and fakeclaude spawn neither git nor pwsh. `ChildEnv` copies the parent
  environment, removes `ANTIPHON_FAKE_LF_ENTER` and sets `ANTIPHON_FAKE_NATIVE_HOME`.
- **Timeout text:** diag line 18 `TESTINGPLATFORM_DEFAULT_HANG_TIMEOUT: ''` (none set). No test
  timeout fired. The poll's 8 s budget (`PollFileAsync(..., TimeSpan.FromSeconds(8))`, lines
  97-99) was **not** exhausted. The predicate returned true once the user line appeared at
  `05:44:56.7264Z` (+0.67 s into the test).
- Desktop PATH versions (read now, not stamped by the run): `git version 2.50.1.windows.1`
  (`/mingw64/bin/git` in Git Bash), `PowerShell 7.6.9` (WindowsApps `pwsh`).

## Neighbouring tests and host load

The chunk ran 140 tests in about 1.1 s of wall time. Every test in the class filter started inside
`05:44:56.05–05:44:57.14Z`. These ran concurrently with the failing test:

| Test | Duration (s) |
|---|---|
| Trace_publishes_only_coherent_semantic_snapshots | 1.227 |
| Dead_composer_fails_the_prompt_send_with_enter_withheld | 1.037 |
| **Claude_two_write_submit_is_a_normalizer_turn (FAILED)** | **1.023** |
| Refusal_and_durable_status_round_trip_through_both_route_families | 1.008 |
| Server_runner_status_round_trip_survives_lost_post_reply | 1.004 |
| C461_G015_Distinct_guard_wire | 0.991 |
| Invalid_prefixes_missing_panes_and_unknown_operations_keep_typed_status | 0.988 |
| All_disposal_outcomes_round_trip(Unknown/Refused/Closed) | 0.979 / 0.733 / 0.699 |
| Old_runner_refuses_before_any_disposal_request | 0.822 |
| C461_G109_Typed_status_receipt | 0.758 |
| A_real_runner_heartbeat_lands_when_the_server_counter_is_ahead | 0.695 |
| Registration_advertises_the_epoch_the_accepted_connection_runs_at | 0.565 |
| Grok_jsonl_reply_keeps_the_nonce_past_the_screen_cut (same class, ran after, passed) | 0.552 |

Chunk p50 was 0.025 s, p95 0.979 s and max 1.227 s (`antiphon-all-group-4.timing.json`). Host load
hints: 8 logical processors, and about 15 HTTP and Kestrel wire tests of roughly 0.7–1.0 s each were
in flight together with the ConPTY child. The adjacent `antiphon-all-group-3` chunk finished at
`05:44:52.618Z`, 65 ms before group-4 started, so the two did not overlap. The artefacts hold no
per-process CPU or idle sample.

### History of this test on Windows Full runs (all read-only from `releases\candidates\*\logs`)

The test passed in 21 of 21 prior runs. Durations ranged from 0.41 to 2.47 s, and since 2026-10-24
they have usually been 0.41–0.55 s (for example a6410818 0.454 s, 4e0d1159 0.515 s and c3f65ddb
0.432 s). This run took 1.023 s, about twice the recent median, and it is the first failure. The
chunk's class set is almost the same as a6410818's group-3: it drops
`ClaudeDialogWatchdogCanaryTests` and adds `GrokLinuxBlockingPromptTests`. It is not a new pairing.

## Diagnosis

1. **The wait predicate is narrower than the assertions.** In
   `tests/Antiphon.Tests/Agents/Card1277PtyHostFixtureTests.cs:97-99`, `PollFileAsync` finishes as
   soon as the file contains `"type":"user"`. Line 100 then snapshots the screen, and line 107
   asserts on the **assistant** record. Line 111 asserts on the `FAKE response to:` screen line.
2. **fakeclaude writes in this order** (`src/Antiphon.FakeClaude/Program.cs` `SubmitTurn`):
   `SUBMITTED:` echo (690). Then `AppendTranscript(JsonUserLine(text))` (713). Then
   `FAKE response to:` and `Crunched for 1s` (742-745). The assistant record or records come last
   (752-761: an optional thinking record, then `JsonAssistantLine`, which also carries the
   TurnEnd). The test's poll can therefore observe the window between 713 and 761.
3. **The evidence pins the poll inside that window.** The `jsonl=` capture has only the user record
   (timestamp `05:44:56.7264Z`). The `screen=` capture ends at the `SUBMITTED:` line with no
   `FAKE response to:`. If the test had passed line 107, line 111 would also have failed. Both
   captures come from the same instant: after line 713 and before line 742.
4. **Why the window opened now:** load, not a code change. The fake's first serialisation of a new
   record type is slow on a cold process. The source comment at 710-711 says "first-call JSON
   serializer warm-up takes long enough to stall the drain loop". Under the 8-CPU desktop with about
   15 concurrent HTTP and wire tests, the gap between the user and assistant appends outlasted one
   50 ms poll tick. The doubled duration of 1.02 s against a median of about 0.45 s fits a starved
   process.
5. **File sharing is not a factor.** The test reads with `FileShare.ReadWrite`. `AppendTranscript`
   retries `IOException` up to 100 times (1012-1020), so no record was lost. It was simply late.

### Why it was missed before the rc run

- The test was written as a **Linux PtyHost pin** (class summary, lines 14-18; commit
  `5e21b61a2`, 2026-10-15). Its pre-land verification ran as a narrow filter on an otherwise idle
  host, where the fake's user-to-assistant gap stays far below one 50 ms tick.
- On Windows it has passed 21 times in Full runs. The race needs the poll to wake inside a gap of a
  few tens of milliseconds while the CPU is contended, so a single green run, or even 21 of them,
  cannot expose it. Nothing in the pre-rc gate runs the class under the chunk's real concurrency.
- The sibling Grok test in the same class waits for the screen cut first (lines 51-52) and polls
  for `turn_completed` (line 54), which is the end of the turn. Only the Claude test polls for the
  start of the turn. A review of the shared pattern would have flagged the gap.

### Suggested fix (for the Debug stage; no code change made here)

Make the poll predicate match what is asserted. Wait until the JSONL holds a `"type":"assistant"`
record, which fakeclaude writes last and which carries the TurnEnd. Better still, wait first for the
screen to contain `FAKE response to:` (`WaitForOutputAsync`, as the Grok test does). Take the
`SnapshotText()` only after that. The budget stays at 8 s. This removes the race without widening
any timeout or loosening any assertion.
