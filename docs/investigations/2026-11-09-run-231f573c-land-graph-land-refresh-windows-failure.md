# Windows group 87: LandGraphLandRefreshTests

Read-only extract from qualification run `dce78ee0`. No code change.

## Sources

- Log directory: `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\`
- `antiphon-all-group-87.trx` (721194 bytes)
- `antiphon-all-group-87-tests.log` (6706 bytes)
- `antiphon-all-group-87.timing.json` (864 bytes)
- `antiphon-all-group-87-execution\log_261110011227344.diag` (1421438 bytes, 2659 lines). No other `antiphon-all-group-87*.diag`.
- Executing checkout: `C:\Antiphon\releases\checkout-42be26dd` at `7bee43614495293afbae1b0f4add019d11dacb9c` on `rc/2026-11-14`. The task brief named `rc/2026-11-13`. The stacks and `git rev-parse` of that checkout name `rc/2026-11-14`.

Host line from `antiphon-all-group-87-tests.log` line 2:

```
TUnit v1.44.0.0 | 64-bit | Microsoft Windows 10.0.19045 | win-x64 | .NET 9.0.22 | Microsoft Testing Platform v2.2.2
```

TRX `<Times>`: creation/start `2026-11-10T01:12:27.6635369Z`, finish `2026-11-10T01:15:44.7502101Z`. Computer `DESKTOP-KTLKPIF`.

TRX counters (line 7984): total 391, executed 391, passed 388, failed 3, error 0, timeout 0, aborted 0, inconclusive 0, notExecuted 0.

`antiphon-all-group-87-tests.log` lines 80-85:

```
Test run summary: Failed! - C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll (net9.0|x64)
  total: 391
  failed: 3
  succeeded: 388
  skipped: 0
  duration: 3m 17s 202ms
```

TRX `RunInfo` (line 7987): `Exit code indicates failure: '2'.` That is the testing-platform exit after the three failed tests.

`antiphon-all-group-87.timing.json`: `why` `timing-read-refused`, `lastPhase` `complete`, `buildSeconds` 131.056, `buildSlot` `not-used`, `runWallSeconds` 198.976, `queueWaitSeconds` 0, `testCount` 0, `p50Seconds` null, `p95Seconds` null, `maxSeconds` null, `slowest` empty. Per-test timing distillation did not run. Durations below are the TRX `duration` attributes.

## Failed tests

All three failures are in `Antiphon.Tests.Application.LandGraphLandRefreshTests`. They are the only failures in the chunk. Each StdOut is only the phase id. The diagnostic file repeats the same messages and the same truncated stacks. No failure record contains a git stderr line, a pwsh error, a process id, or the word timeout.

### 1. failed_card_note_does_not_block_the_refresh

- Fully qualified name: `Antiphon.Tests.Application.LandGraphLandRefreshTests.failed_card_note_does_not_block_the_refresh`
- TRX outcome `Failed`, duration `00:00:04.8597737`, start `2026-11-10T01:14:20.7243068+00:00`, end `2026-11-10T01:14:25.5840805+00:00`
- Log timing: `4s 859ms` (`antiphon-all-group-87-tests.log` line 4)
- Assertion site: `LandGraphLandRefreshTests.cs:285` (`result.Code` expected 0, was 1)

Message (`antiphon-all-group-87-tests.log` lines 5-9, same text in TRX `<Message>` lines 1594-1598):

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Code
    should be
0
    but was
1
```

Stack (`antiphon-all-group-87-tests.log` lines 10-23). Fourteen frames, then TUnit omitted the rest. This is the whole stored stack, under the 40-line cap.

```
at Antiphon.Tests.Application.LandGraphLandRefreshTests.failed_card_note_does_not_block_the_refresh() in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:285
at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
--- End of stack trace from previous location ---
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
--- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

StdOut:

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.LandGraphLandRefreshTests.1.1.failed_card_note_does_not_block_the_refresh.1.1.0
```

The test asserts `result.Code` before `result.Text` (`LandGraphLandRefreshTests.cs:285`). The notice text for this failure was not stored.

### 2. check_still_failing_after_refresh_fails_the_land

- Fully qualified name: `Antiphon.Tests.Application.LandGraphLandRefreshTests.check_still_failing_after_refresh_fails_the_land`
- TRX outcome `Failed`, duration `00:00:04.2917302`, start `2026-11-10T01:14:25.5898659+00:00`, end `2026-11-10T01:14:29.8815961+00:00`
- Log timing: `4s 291ms` (`antiphon-all-group-87-tests.log` line 27)
- Assertion site: `LandGraphLandRefreshTests.cs:232`

Message (`antiphon-all-group-87-tests.log` lines 28-33, TRX lines 1621-1626):

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Text
    should contain (case insensitive comparison)
"action=refuse reason=check-after-refresh"
    but was actually
"LAND-GRAPH-NOTICE action=refuse reason=commit-failed
"
```

Stack (`antiphon-all-group-87-tests.log` lines 34-47):

```
at Antiphon.Tests.Application.LandGraphLandRefreshTests.check_still_failing_after_refresh_fails_the_land() in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:232
at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
--- End of stack trace from previous location ---
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
--- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

StdOut:

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.LandGraphLandRefreshTests.1.1.check_still_failing_after_refresh_fails_the_land.1.1.0
```

This is the only failure whose stored product text names the script reason: `LAND-GRAPH-NOTICE action=refuse reason=commit-failed`.

### 3. new_project_refreshes_and_reports_the_new_count

- Fully qualified name: `Antiphon.Tests.Application.LandGraphLandRefreshTests.new_project_refreshes_and_reports_the_new_count`
- TRX outcome `Failed`, duration `00:00:03.9775186`, start `2026-11-10T01:14:48.7917713+00:00`, end `2026-11-10T01:14:52.7692899+00:00`
- Log timing: `3s 977ms` (`antiphon-all-group-87-tests.log` line 51)
- Assertion site: `LandGraphLandRefreshTests.cs:93` (`result.Code` expected 0, was 1)

Message (`antiphon-all-group-87-tests.log` lines 52-56, TRX lines 1679-1683):

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Code
    should be
0
    but was
1
```

Stack (`antiphon-all-group-87-tests.log` lines 57-70):

```
at Antiphon.Tests.Application.LandGraphLandRefreshTests.new_project_refreshes_and_reports_the_new_count() in C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs:93
at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
--- End of stack trace from previous location ---
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
--- TUnit internals omitted (run with --detailed-stacktrace for full trace) ---
```

StdOut:

```
ANTIPHON_TEST_PHASE_ID Antiphon.Tests.Application.LandGraphLandRefreshTests.1.1.new_project_refreshes_and_reports_the_new_count.1.1.0
```

Same as failure 1: the notice text was not stored because `result.Code.ShouldBe(0)` failed first (`LandGraphLandRefreshTests.cs:93`).

## Timeout, process, git, pwsh, and path text

Present in the failure records and the chunk header:

- Paths: `C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\Application\LandGraphLandRefreshTests.cs` at lines 93, 232, and 285. Assembly `C:\Antiphon\releases\checkout-42be26dd\tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.dll`. Results directory and diag directory under `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\2026-11-09-1306-dce78ee0\`. Timing root named in `antiphon-all-group-87.timing.json`: `C:\Antiphon\releases\candidates\rc-2026-11-14-7bee43614495\logs\timing-dce78ee0a35a4b08be30d8e0e8ef0295`.
- Process-shaped text: the single notice line `LAND-GRAPH-NOTICE action=refuse reason=commit-failed` on failure 2. Invocation id `C487_INVOCATION=dce78ee0a35a4b08be30d8e0e8ef0295` on tests.log line 1. Diag line 10 is the Microsoft.Testing.Platform host command (`--report-trx`, `--diagnostic`, `--treenode-filter` for `Antiphon.Tests.Application`). It is not the land-graph script.
- `RunInfo` exit code `2` (platform), not a per-test timeout.

Absent from the three failure messages, stacks, StdOut, the tests log body, and the matching diag exception properties:

- The word `timeout`. TRX counter `timeout="0"`.
- `git`, `pwsh`, a process id, `index.lock`, `nothing to commit`, or any other git stderr.
- A `TimeoutException`. The test method attribute is `[Timeout(60_000)]` and `Fixture.RunScript` throws `TimeoutException` after 45 seconds (`LandGraphLandRefreshTests.cs:840-844`). These failures are `ShouldAssertException` after about 4 seconds, so neither wait fired.

The notice on failure 2 is produced only here (`scripts\land-graph-land-refresh.ps1` on the executing checkout):

```
356: $commit = Invoke-Git @('commit', '-m', 'chore: refresh land-graph records for the landing batch', '--', $ManifestRelative, $EvaluationRelative, $PinsRelative)
357: if ($commit.Code -ne 0) { Refuse 'LAND-GRAPH-NOTICE action=refuse reason=commit-failed' }
```

`Refuse` writes that one line and exits 1 (`land-graph-land-refresh.ps1:29-31`). `$commit.Text` and `$commit.Error` are not written. `Invoke-Git` is `Invoke-Child 'git'` with a 120000 ms wait (`land-graph-land-refresh.ps1:78-79`). A child that hits that wait returns code 124 and `Error = 'timeout'` (`land-graph-land-refresh.ps1:60-63`). The whole test finished in 4.292 seconds, so that git wait did not expire. Git returned some other non-zero code, and the log does not contain it.

`Send-CardNote` runs only after a successful commit (`land-graph-land-refresh.ps1:379`). Failure 1 passes `-Card CARD-1753` and still died on `result.Code` before any card-note assertion. The card post is not what this log shows failing.

## Neighbour durations in the class

The class is `[ParallelLimiter<ProcessSpawnLimit>]`. In this chunk its methods do not overlap. Each start is the previous method's end.

| Outcome | Duration | Start (UTC) | Test |
|---|---:|---|---|
| Passed | 10.222s | 01:13:41.931 | real_audit_property_refusal_does_not_refresh |
| Passed | 26.162s | 01:13:52.155 | real_audit_changed_definition_refreshes_commits_only_land_graph_files_and_second_check_exits_zero |
| Passed | 0.014s | 01:14:18.317 | dispose_removes_a_readonly_git_object_through_startrefgit |
| Passed | 0.008s | 01:14:18.331 | instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden |
| Passed | 2.384s | 01:14:18.340 | current_graph_does_not_refresh |
| Failed | 4.860s | 01:14:20.724 | failed_card_note_does_not_block_the_refresh |
| Failed | 4.292s | 01:14:25.590 | check_still_failing_after_refresh_fails_the_land |
| Passed | 3.649s | 01:14:29.882 | pin_change_outside_the_batch_files_is_outside_batch |
| Passed | 3.922s | 01:14:33.531 | inherited_mismatch_mixed_with_a_batch_change_is_outside_batch |
| Passed | 3.706s | 01:14:37.453 | malformed_manifest_does_not_refresh |
| Passed | 2.311s | 01:14:41.159 | property_or_construct_refusal_does_not_refresh |
| Passed | 5.320s | 01:14:43.471 | changed_pin_on_a_batch_file_is_rewritten_and_committed |
| Failed | 3.978s | 01:14:48.792 | new_project_refreshes_and_reports_the_new_count |
| Passed | 2.605s | 01:14:52.770 | inherited_only_mismatch_warns_and_does_not_refresh |
| Passed | 5.155s | 01:14:55.375 | changed_csproj_refreshes_commits_only_land_graph_files_and_notifies |

Chunk of 391 tests: min 0.000s, p50 0.182s, p95 9.503s, max 47.789s. Slowest eight all passed. The max is `AddHostCleanup_upgrades_current_master_without_changing_existing_rows` at 47.789s, ended `01:13:55.941Z`, before the three failures.

## Host load during the failures

Tests whose TRX interval overlaps each failure: exactly one, the failure itself.

- `failed_card_note_does_not_block_the_refresh`: 1 in flight
- `check_still_failing_after_refresh_fails_the_land`: 1 in flight
- `new_project_refreshes_and_reports_the_new_count`: 1 in flight

The three failures ran serially, after the rest of the chunk had drained, at the same few-second duration as the passing methods beside them.

## Diagnosis

This is an assertion on returned state, not a load or timing failure.

`check_still_failing_after_refresh_fails_the_land` expected `action=refuse reason=check-after-refresh` and got `action=refuse reason=commit-failed` (`antiphon-all-group-87-tests.log` lines 28-33). That reason is the script's check that `git commit` of the three land-graph files returned non-zero (`land-graph-land-refresh.ps1:356-357`). The second-check refusal the test expected is the next step, line 359-360, and the log shows the script never got there. The test finished in 4.292s with no timeout text.

The other two failed the same way one assertion earlier: `result.Code` was 1 instead of 0 (`tests.log` lines 5-9 and 52-56). Their notice text is not in the log. Both are refresh paths that expect exit 0. They took 4.860s and 3.978s.

What a pre-rc watch can miss, from this log alone:

- Nothing stalled. The 60-second test timeout, the 45-second script wait, and the 120-second git wait all stayed idle. The TRX timeout counter is 0. A hang gate stays green.
- The chunk log has no git or pwsh error to search for. `Refuse` replaces the git commit output with one notice line, and two of the three tests never store that line because they assert `result.Code` first.
- Twelve other methods in this class passed in the same chunk, including three that do commit a refresh: `real_audit_changed_definition_refreshes_commits_only_land_graph_files_and_second_check_exits_zero` (26.162s), `changed_pin_on_a_batch_file_is_rewritten_and_committed` (5.320s), and `changed_csproj_refreshes_commits_only_land_graph_files_and_notifies` (5.155s). A sample that runs one of those and not these three cases stays green. Git commit as a mechanism was working on this host in the same minute. The failure is case-specific.

The log does not say which non-zero code git returned. Debug has to reproduce these three methods on this Windows checkout and keep `$commit.Text` and `$commit.Error`, which this run discarded.

These logs do not say whether an earlier Linux run of the same three methods passed.
