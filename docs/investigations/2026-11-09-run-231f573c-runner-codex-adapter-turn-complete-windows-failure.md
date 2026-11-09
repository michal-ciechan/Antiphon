# Windows failure: RunnerCodexAdapterTurnCompleteTests (g3)

Read-only extract from Windows qualification run `dce78ee0` (rc/2026-11-13, candidate logs `rc-2026-11-14-7bee43614495`, checkout `C:\Antiphon\releases\checkout-42be26dd`). The required filename carries `231f573c`; the evidence below is run `dce78ee0`. No test was re-run. No secrets are in this extract.

Sources:

- `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\antiphon-all-group-3-tests.log`
- `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\antiphon-all-group-3.trx`
- `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\antiphon-all-group-3.timing.json`
- `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\antiphon-all-group-3-execution\log_261109230325181.diag`

## Exact test

`Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests.C1322_Cancelled_verdict_cannot_become_screen_success`

- TRX `testId` `3506fd10-eb2d-af35-a602-5cb903db81b2`, `executionId` `c257df77-e284-4a42-84f3-5021797da006`
- outcome `Failed`, computer `DESKTOP-KTLKPIF`
- duration `00:00:06.9230016` (log: `6s 923ms`; timing.json: `6.923` seconds)
- start `2026-11-09T23:03:37.0555822+00:00`, end `2026-11-09T23:03:43.9785838+00:00`
- category Unit, TUnit 1.44.0.0, .NET 9.0.22, Windows 10.0.19045 win-x64
- assembly `C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll`

Chunk `antiphon-all-group-3`: total 409, executed 409, passed 408, failed 1, timeout 0, error 0, skipped 0. Log duration `2m 02s 912ms`. Timing: queue wait 0s, build 131.056s (`buildSlot` `not-used`), run wall 123.998s, fixture setup 3.646s, p50 0.031s, p95 3.888s, max 8.314s. Run info text: `Exit code indicates failure: '2'`.

## Error message

From `antiphon-all-group-3-tests.log` lines 4-15 and TRX `ErrorInfo` (lines 2284-2291). Same text in the diag `ErrorTestNodeStateProperty`.

```
failed C1322_Cancelled_verdict_cannot_become_screen_success (6s 923ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: interrupted.TurnCompleted
      should be
  False
      but was
  True

  Additional Info:
      an aborted turn must not become transcript or screen success
```

TRX message body:

```
ShouldAssertException: interrupted.TurnCompleted
    should be
False
    but was
True

Additional Info:
    an aborted turn must not become transcript or screen success
```

## Stack (complete; shorter than 40 lines)

`antiphon-all-group-3-tests.log` lines 13-28 and TRX lines 2292-2307:

```
    at Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests.C1322_Cancelled_verdict_cannot_become_screen_success() in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Agents\RunnerCodexAdapterTurnCompleteTests.cs:80
    at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    at Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests.C1322_Cancelled_verdict_cannot_become_screen_success() in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Agents\RunnerCodexAdapterTurnCompleteTests.cs:113
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

Line 80 is the failing assertion (`interrupted.TurnCompleted.ShouldBeFalse`). Line 113 is the async state-machine frame on the method's last statement (`legacy.ResponseText.ShouldBe("legacy answer")`). Shouldly stops at the first failure, so lines 81-112 did not run. Reaching line 80 means the earlier assertions in the same method passed, including `cancellationObserved.ShouldBeTrue` on line 79.

## Timeout, process, git, pwsh, and path text on this result

The failed result's own stdout is only:

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests.1.1.C1322_Cancelled_verdict_cannot_become_screen_success.1.1.0
```

There is no timeout, process, git, or pwsh text on this result. The TRX chunk counter `timeout` is 0. The test returned in 6.923s; its `CodexDoneMaxWaitMs` for this wait is 15_000, so this is not the max-wait timeout path.

Paths named on the failed result:

- source `C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Agents\RunnerCodexAdapterTurnCompleteTests.cs` lines 80 and 113
- assembly `C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll`
- diag `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\antiphon-all-group-3-execution\log_261109230325181.diag`

The client is `ScriptedCodexRunnerClient`. This test does not launch git, pwsh, or a pty host. Neighbouring disposal tests in the same chunk do (`--launch-timeout-sec 30`, `pwsh.exe` on a later C1515 denial). Those strings are not part of this failure.

## Same-class durations in the chunk

All nine `RunnerCodexAdapterTurnCompleteTests` results, joined from TRX `UnitTest` class name:

| Test | Outcome | Duration | Start (UTC) |
|---|---|---|---|
| The_reply_text_decides_IsAskingQuestion_not_the_screen | Passed | 0.139s | 23:03:36.900 |
| A_first_turn_with_no_transcript_yet_still_uses_baseline_zero | Passed | 0.166s | 23:03:36.893 |
| A_TurnEnd_row_past_the_prompt_baseline_completes_the_turn_with_transcript_reply_text | Passed | 0.023s | 23:03:37.040 |
| A_TurnEnd_at_or_below_the_baseline_does_not_complete_the_new_turn | Passed | 1.703s | 23:03:37.060 |
| A_transient_fetch_failure_on_a_later_turn_preserves_the_baseline_instead_of_replaying_the_previous_reply | Passed | 2.007s | 23:03:36.884 |
| A_session_that_never_visibly_works_reports_the_turn_incomplete_instead_of_scraping_the_status_bar | Passed | 4.137s | 23:03:36.892 |
| C907_captured_lifecycle_completes_screen_fallback | Passed | 4.134s | 23:03:36.896 |
| The_screen_fallback_completes_when_the_working_indicator_appeared_and_then_went | Passed | 4.311s | 23:03:36.889 |
| C1322_Cancelled_verdict_cannot_become_screen_success | Failed | 6.923s | 23:03:37.056 |

The three screen-lifecycle siblings that must burn `CodexDoneMaxWait` or the 2s transcript grace land at 4.13-4.31s and pass. The failed method is the only one that runs a screen-lifecycle control and then a second wait that can also burn that grace. It is about 2.6s longer than those siblings.

## Host load

195 TRX results overlap `23:03:37.056` to `23:03:43.979`. Slowest overlap:

| Duration | Outcome | Test |
|---|---|---|
| 8.314s | Passed | C0167_agent_launch_resolves_project_key_and_clear_removes_projection(Codex) |
| 7.191s | Passed | Disabling_the_probe_does_not_disable_effort_resolution(True) |
| 6.923s | Failed | C1322_Cancelled_verdict_cannot_become_screen_success |
| 5.408s | Passed | Dispose_awaits_exit_of_its_running_host_and_descendants |
| 4.645s | Passed | A_trust_dialog_that_will_not_clear_fails_the_launch_with_a_named_block |
| 4.311s | Passed | The_screen_fallback_completes_when_the_working_indicator_appeared_and_then_went |

Timing.json slowest also includes `Dispose_does_not_touch_another_clients_host` 7.757s, `Local_adapter_names_an_uncleared_effort_dialog` 7.675s, `Refusal_names_holder_and_never_stops_it(False)` 7.048s, and `Wait_for_turn_complete_does_not_succeed_on_a_stripped_empty_slow_start` 7.025s and 6.21s. Those are real pty-host tests running in the same process while this scripted unit test was polling.

## Diagnosis

Load/timing, not a deterministic misclassification of `cancelled`.

The assertion that failed is a state check: `interrupted.TurnCompleted` must be false (`RunnerCodexAdapterTurnCompleteTests.cs` line 80). The line above it passed, so the scripted hook did append a `TurnEnd` with `StopReasons.Cancelled`. A transcript verdict would still have failed this assertion only if `IsReportBoundary` treated that stop reason as success. It does not:

```
kind == TurnEnd && !string.Equals(stopReason, StopReasons.Cancelled, StringComparison.Ordinal)
```

(`SessionRunnerContracts.cs` lines 536-537). `TryBuildTranscriptVerdictAsync` sets `TurnCompleted` from that predicate (`RunnerCodexAdapter.cs` lines 303 and 321). A cancelled row that is visible before the screen verdict is sealed returns `TurnCompleted: false`.

The value `true` is what the screen fallback returns (`RunnerCodexAdapter.cs` lines 277-282: `TurnCompleted: screenDone`). That path is taken when the 2s grace after screen-done expires without a transcript boundary (`TranscriptGraceAfterScreenDone`, line 16; the break is lines 268-270). During that grace the loop does not take another snapshot, so `BeforeSnapshotAsync` cannot fire.

The test arms the hook for one specific snapshot (`RunnerCodexAdapterTurnCompleteTests.cs` lines 64-76):

```
client.IndicatorScreenReads = client.SnapshotReads + 4;
if (read == client.IndicatorScreenReads + 2)
    client.Append(TranscriptKinds.TurnEnd, stopReason: Cancelled);
```

`ScriptedCodexRunnerClient.GetSnapshotAsync` advances the output sequence only while the Working line is showing (lines 186-190). `CodexTurnScreenTracker.Observe` resets its quiet clock whenever that mark changes and returns done only after the indicator has gone and the mark has been still for `quietPeriod` ( `CodexDetectors.cs` lines 153-163). This test sets `doneQuietMs: 300` (test line 52). The adapter polls every 250ms (`TurnPollInterval`, `RunnerCodexAdapter.cs` line 17).

On a prompt poll the four working frames are attempts `S+1`..`S+4`, and the first quiet frame is `S+5`. If that gap stays under 300ms, screen-done waits for the second quiet frame, attempt `S+6`, which is exactly the hook. The cancelled row is appended inside that snapshot, the next loop checks the transcript first, and the test passes. If scheduling stretches the gap from the last working frame to the first quiet frame to 300ms or more, attempt `S+5` seals `screenDone`, the 2s grace elapses with no further snapshot, and the hook runs only inside the final `SnapshotTextAsync` (`RunnerTerminalSession.cs` lines 207-210) after the verdict is already `TurnCompleted: true`. Line 79 then passes and line 80 fails. That is this result.

The 6.923s duration matches that second grace. A single screen-fallback sibling in this same chunk, which always burns one 2s grace because its transcript throws, takes 4.13-4.31s. This method runs that control and then the interrupted wait. The extra ~2.6s is the second grace plus the short transcript turns, not a 15s max-wait.

This is why a pre-rc Unit run misses it. The pass window is the ~50ms between a 250ms poll and a 300ms quiet period. A class filter on a quiet machine stays inside that window, the transcript poll beats screen success, and the test is green. This Windows chunk had 195 overlapping results and live pty hosts in-process. The slack does not survive that schedule. One observation; the test was not re-run here, so this write-up does not claim it fails on every invocation. It fails when the inter-poll gap reaches the 300ms quiet period.
