# Windows g86 AgentTaskAutoTitleTests failure

Read-only extract from qualification run `dce78ee0` (invocation `dce78ee0a35a4b08be30d8e0e8ef0295`), log directory `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\`. Sources: `antiphon-all-group-86.trx`, `antiphon-all-group-86-tests.log`, `antiphon-all-group-86.timing.json`, and `antiphon-all-group-86-execution\log_261110010822178.diag`. No other `antiphon-all-group-86*.diag` file is in that directory. The execution folder contains only that diag (2,288,328 bytes).

Host line from the tests log: TUnit v1.44.0.0, 64-bit, Microsoft Windows 10.0.19045, win-x64, .NET 9.0.22, Microsoft Testing Platform v2.2.2. Computer name on the TRX result: `DESKTOP-KTLKPIF`.

## Failed test

Exact name: `a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout`

Fully qualified: `Antiphon.Tests.Application.AgentTaskAutoTitleTests.a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout`

Phase id: `Antiphon.Tests.Application.AgentTaskAutoTitleTests.1.1.a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout.1.1.0`

TRX: `executionId="da121393-49fa-4c66-acf4-56fc151fedba"` `testId="6017e40b-1415-9431-bead-c30daabfd1b9"` `outcome="Failed"`

Duration: `00:00:07.8251964` (tests log: `7s 825ms`). Start `2026-11-10T01:08:54.5853297+00:00`. End `2026-11-10T01:09:02.4105261+00:00`.

Category recorded on the diag node: `Integration`. Source attribute on the class is `[Category("Integration")]` and `[NotInParallel("DiagnoseTitle")]` (`AgentTaskAutoTitleTests.cs` lines 22-24 in checkout `C:\Antiphon\releases\checkout-42be26dd`). The class is not listed in `tests/Antiphon.Tests/slow-tests-allowlist.txt`.

## Error message

Tests log lines 4-8, and the same text in TRX `ErrorInfo` (around lines 3129-3133) and diag lines 4244-4250:

```
failed a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout (7s 825ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: The diagnose worker never created a run.
    at Antiphon.Tests.Application.AgentTaskAutoTitleTests.Harness.WaitForDiagnoseRunAsync(Guid specialistId) in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\AgentTaskAutoTitleTests.cs:546
    at Antiphon.Tests.Application.AgentTaskAutoTitleTests.a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout() in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\AgentTaskAutoTitleTests.cs:178
       --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

TRX `Message` element: `TimeoutException: The diagnose worker never created a run.`

Diag `ErrorTestNodeStateProperty`: `Explanation = [Test Failure] TimeoutException: The diagnose worker never created a run.` and `Exception = TUnit.Engine.Exceptions.TestFailedException: TimeoutException: The diagnose worker never created a run.`

Standard output on the result is only:

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.AgentTaskAutoTitleTests.1.1.a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout.1.1.0
```

## Stack (complete recorded trace; fewer than 40 lines)

TRX `StackTrace` and diag `TrxExceptionProperty` / exception frames:

```
   at Antiphon.Tests.Application.AgentTaskAutoTitleTests.Harness.WaitForDiagnoseRunAsync(Guid specialistId) in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\AgentTaskAutoTitleTests.cs:line 546
   at Antiphon.Tests.Application.AgentTaskAutoTitleTests.a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout() in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\AgentTaskAutoTitleTests.cs:line 178
   --- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

The diag file location property for this node says the test method starts at line 170, which is the `[Test]` attribute. The throw site inside the test is line 178, the `await h.WaitForDiagnoseRunAsync(specialist.Id)` call. Lines 181-184 (title unchanged, diagnose task `Canceled`, ledger `DiagnosisOutcome.DegradedTimeout`) are not in the stack.

## Timeout, process, git, pwsh, and path text

Present in the failure records:

- Timeout text: `TimeoutException: The diagnose worker never created a run.` Tests-log duration `7s 825ms`. TRX duration `00:00:07.8251964`. Diag `Duration = 00:00:07.8251964`.
- Chunk wall from the tests log: `duration: 1m 33s 366ms`. Timing file `runWallSeconds` `94.493`, `startedAt` `2026-11-10T01:08:21.8423254Z`, `completedAt` `2026-11-10T01:09:56.5004694Z`. TRX `Times` start `2026-11-10T01:08:22.4232531Z`, finish `2026-11-10T01:09:55.6894817Z`.
- TRX counters: `total="636" executed="636" passed="635" failed="1" error="0" timeout="0"`. The platform `timeout` counter is 0. The run info text is `Exit code indicates failure: '2'.`
- Tests-log summary: `total: 636`, `failed: 1`, `succeeded: 635`, `skipped: 0`.
- Paths:
  - `C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\AgentTaskAutoTitleTests.cs` lines 546 and 178
  - `C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll`
  - `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\antiphon-all-group-86-execution\log_261110010822178.diag`
  - `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\antiphon-all-group-86.trx`
  - `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\Antiphon.Tests-windows-net9.0-report.html`

Absent from the failure message, stack, standard output, and the diag node for this test: any `git` text, any `pwsh` text, and any process-spawn text. The timing file's chunk-level process figures are `processSpawnSeconds` `0.036` and `processWaitSeconds` `0.97` (`buildSlot` `not-used`, `buildSeconds` `131.056`, `queueWaitSeconds` `0`, `fixtureSetupSeconds` `4.335`).

Harness poll that throws, checkout `AgentTaskAutoTitleTests.cs` lines 530-546: 200 attempts, one database query per attempt, `Task.Delay(25)` between attempts, then `throw new TimeoutException("The diagnose worker never created a run.")`. 200 delays of 25 ms is 5.0 s. The recorded 7.825 s is that full budget plus about 2.8 s of queries. The test sets `DiagnoseWaitSeconds = 4` (line 173) and only then starts `RunTitleAsync` on the thread pool (line 177). The product wait is inside `DiagnoseService.RunTitleAsync` after `RunAsync` is entered (`DiagnoseService.cs` lines 127-128). This result died on the earlier poll.

## Same-class durations (serialized)

`[NotInParallel("DiagnoseTitle")]` is only on this class. The 22 class results do not overlap. 21 passed. The failure sits between two sub-second neighbours that use the same `WaitForDiagnoseRunAsync` helper.

| Outcome | Duration | Start (UTC) | Test |
|---|---|---|---|
| Passed | 00:00:08.9614835 | 2026-11-10T01:08:35.7941175+00:00 | the_unavailable_incident_dedups_per_minute |
| Passed | 00:00:02.0887453 | 2026-11-10T01:08:44.7564133+00:00 | a_card_bound_task_is_prefixed_when_the_answer_omits_the_identifier |
| Passed | 00:00:01.0552096 | 2026-11-10T01:08:46.8458795+00:00 | a_good_answer_replaces_the_title_and_writes_Diagnosed |
| Passed | 00:00:00.5271006 | 2026-11-10T01:08:47.9016054+00:00 | DiagnoseEnabled_false_is_byte_identical_to_today |
| Passed | 00:00:00.2908247 | 2026-11-10T01:08:48.4298977+00:00 | a_specialist_row_is_never_queued(Diagnose) |
| Passed | 00:00:00.3586628 | 2026-11-10T01:08:48.7214447+00:00 | an_explicit_Title_is_not_queued |
| Passed | 00:00:00.2697257 | 2026-11-10T01:08:49.0809679+00:00 | a_specialist_row_is_never_queued(Distill) |
| Passed | 00:00:00.2745251 | 2026-11-10T01:08:49.3514679+00:00 | a_specialist_row_is_never_queued(Check) |
| Passed | 00:00:00.2228497 | 2026-11-10T01:08:49.6266632+00:00 | a_long_untitled_goal_is_queued_and_keeps_the_fallback_title |
| Passed | 00:00:00.6493561 | 2026-11-10T01:08:49.8507092+00:00 | a_rejected_answer_leaves_the_title_and_ledgers_RejectedGate(91 chars) |
| Passed | 00:00:00.7479735 | 2026-11-10T01:08:50.5009359+00:00 | a_rejected_answer_leaves_the_title_and_ledgers_RejectedGate(3 lines) |
| Passed | 00:00:00.4522223 | 2026-11-10T01:08:51.2495874+00:00 | a_short_untitled_goal_is_not_queued |
| Passed | 00:00:01.0154442 | 2026-11-10T01:08:51.7024806+00:00 | a_rejected_answer_leaves_the_title_and_ledgers_RejectedGate(Hello, 1 words) |
| Passed | 00:00:00.9422311 | 2026-11-10T01:08:52.7186839+00:00 | a_title_changed_before_apply_is_SkippedAlreadyTitled |
| Passed | 00:00:00.3250442 | 2026-11-10T01:08:53.6615815+00:00 | an_empty_result_ledgers_DegradedEmpty |
| Passed | 00:00:00.1995158 | 2026-11-10T01:08:53.9872448+00:00 | a_spent_budget_creates_no_seat_row_and_ledgers_DegradedBudget |
| Passed | 00:00:00.1896989 | 2026-11-10T01:08:54.1874262+00:00 | a_held_alias_creates_no_seat_row_and_ledgers_DegradedHeld |
| Passed | 00:00:00.2069290 | 2026-11-10T01:08:54.3778445+00:00 | a_full_backlog_ledgers_DegradedBusy |
| Failed | 00:00:07.8251964 | 2026-11-10T01:08:54.5853297+00:00 | a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout |
| Passed | 00:00:00.1262895 | 2026-11-10T01:09:02.4202165+00:00 | an_answer_equal_to_the_fallback_is_RejectedGate |
| Passed | 00:00:00.1371542 | 2026-11-10T01:09:02.5473408+00:00 | a_rejected_answer_leaves_the_title_and_ledgers_RejectedGate(contains marker) |
| Passed | 00:00:00.1370575 | 2026-11-10T01:09:02.6850249+00:00 | a_failed_run_ledgers_DegradedFailed |

`the_unavailable_incident_dedups_per_minute` uses the same `DiagnoseWaitSeconds = 4` and calls `WaitForDiagnoseRunAsync` three times (lines 291-311). It passed in 8.961 s at the start of the chunk. The next two tests after the failure also call `WaitForDiagnoseRunAsync` and passed in 0.126 s and 0.137 s.

## Other tests overlapping the failure window

59 TRX results intersect `01:08:54.5853297Z` to `01:09:02.4105261Z`, including this failure, so 58 others. 9 results were already running at the failure start. Peak concurrency across the chunk, sampled at each result start, was 28. Chunk duration spread: first result start `2026-11-10T01:08:35Z`, last end `2026-11-10T01:09:54Z`, 636 results. Timing-file distribution: p50 `0.072` s, p95 `3.264` s, max `22.176` s. This failure is the 5th slowest name in that file.

Longest other results that overlap the window:

| Duration | Test |
|---|---|
| 00:00:22.1757275 | C680_QueueAndExpectationKeepDialogInputPending |
| 00:00:08.8778007 | Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged |
| 00:00:07.7785359 | Warm_grok_invalid_rules_revision_cannot_acquire_or_overwrite_the_live_session(pending) |
| 00:00:06.3928917 | C566_Auto_refused_delete_finishes_failed_with_reason_and_caller_receipt |
| 00:00:03.3546205 | C1043_FreshSubjectWitness |
| 00:00:01.2840377 | C680_AllAutomatedWritersUseTheGuard |
| 00:00:01.1908730 | Force_delete_removes_card_workflow_runs_and_stages |
| 00:00:01.0368196 | the_loop_raises_on_the_grace_timer_and_wakes_on_a_fence_signal |

## Diagnosis

Load/timing. It is not a deterministic assertion on diagnose state.

The stack stops at `WaitForDiagnoseRunAsync` line 546. The outcome assertions at lines 181-184 never ran, so this is not a wrong `DiagnosisOutcome` or a canceled-status mismatch. The recorded 7.825 s matches the helper's full 200-step poll (5.0 s of 25 ms delays plus the queries), which is what that helper does when no `Queued` `Diagnose` row appears. The same helper succeeded in this chunk on the previous class tests (0.19-1.0 s), on the next two class tests (0.126 s and 0.137 s), and on `the_unavailable_incident_dedups_per_minute`, which uses the same 4-second diagnose wait. The class itself was not running in parallel with itself. The shared chunk was: 9 tests already in flight when this one started, 58 others overlapping it, peak 28. A Unit-lane or quiet single-class run does not execute this Integration class under that database load, which is why the poll expiry was not seen before this Windows rc chunk.
