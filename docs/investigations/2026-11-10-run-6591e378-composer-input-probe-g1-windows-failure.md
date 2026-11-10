# Windows failure: ComposerInputProbeTests, agents-pty-all-group-1

Read-only extract of the single failure in chunk `agents-pty-all-group-1` of qualification run `6591e378`. The qualification was still running. Nothing in that run, AppHost, the production runner, Docker, or any session was touched.

## Run

| Field | Value |
|---|---|
| Run | `6591e378` (`C487_INVOCATION=6591e378c04d47bc898cf0927352b01b`) |
| Branch | `rc/2026-11-15` = `ac6d00022e940ee3b90584c7d313567b4491c498` |
| Checkout | `C:\Antiphon\releases\checkout-4dd2a426` (HEAD matches that SHA) |
| Host | `DESKTOP-KTLKPIF`, Windows 10.0.19045, win-x64, .NET 9.0.22 |
| Runner | TUnit 1.44.0.0, Microsoft Testing Platform 2.2.2 |
| Log dir | `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378` |
| Wall | start `2026-11-10T15:01:25.5375832Z`, complete `2026-11-10T15:03:00.1203474Z`, `runWallSeconds` 94.368 |
| Build | `buildSeconds` 655.862, `buildSlot` `not-used` |

TRX `ResultSummary`: outcome Failed, total 599, executed 599, passed 598, failed 1, error 0, timeout 0, aborted 0, inconclusive 0.

Console (`agents-pty-all-group-1-tests.log`) matches: `failed: 1`, `succeeded: 598`, `skipped: 0`, duration `1m 33s 455ms`.

## Files read

Name filter `agents-pty-all-group-1*` on the log directory:

| Name | Bytes | Role |
|---|---|---|
| `agents-pty-all-group-1.trx` | 937333 | results |
| `agents-pty-all-group-1-tests.log` | 2589 | console |
| `agents-pty-all-group-1.timing.json` | 2523 | phase timing |
| `agents-pty-all-group-1-execution\` | directory | diagnostic output |

Inside `agents-pty-all-group-1-execution`, a `*console*` filter and a `*.log` filter matched nothing. The console log names the diagnostic file `agents-pty-all-group-1-execution\log_261110150125810.diag`. The failure node in that diag repeats the TRX assertion. The failed `UnitTestResult` has no `StdOut` and no `StdErr`.

## Failed test

One failure.

- Class: `Antiphon.Agents.Pty.Tests.ComposerInputProbeTests`
- Method: `An_unrendered_token_is_re_typed_up_to_the_write_cap_and_no_further`
- `executionId`: `814a0c1a-d083-455b-84c8-645c70601cca`
- `testId`: `239ce650-3b5f-e59e-ee27-612b47ecf718`
- Start `2026-11-10T15:01:29.8160739+00:00`, end `2026-11-10T15:01:30.9277239+00:00`
- Duration `00:00:01.1116500` (console: `1s 111ms`)
- Outcome: Failed

Exact assertion message from the TRX `ErrorInfo` (same text in the console log and in the diag at `2026-11-10T15:01:30.9357823+00:00`):

```
ShouldAssertException: result.Writes
    should be
3
    but was
2

Additional Info:
    three writes inside the budget, including the first
```

Expected `result.Writes` 3. Actual `result.Writes` 2.

Stack top frames (TUnit then omitted the rest):

```
at Antiphon.Agents.Pty.Tests.ComposerInputProbeTests.An_unrendered_token_is_re_typed_up_to_the_write_cap_and_no_further()
    in C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.Agents.Pty.Tests\ComposerInputProbeTests.cs:line 130
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

That `Throw` / `ThrowForNonSuccess` / `HandleNonSuccessAndDebuggerNotification` trio is repeated three times in the trace. Line 130 is `result.Writes.ShouldBe(3, "three writes inside the budget, including the first")`. Line 131 (`composer.Writes.Count(w => w == "zzdeadbeef").ShouldBe(3)`) did not run.

The test calls `ComposerInputProbe.RunAsync` with `FakeComposer { DeafForever = true }` and `FastOptions(timeoutMs: 1000, retypeMs: 100, maxWrites: 3)` (`ComposerInputProbeTests.cs` lines 122-131). `DeafForever` never renders the token. The probe (`ComposerInputProbe.cs` lines 123-140) writes the token once, then on each poll:

1. returns `NeverAppeared` when `clock.Elapsed >= options.Timeout` (the 1000ms `Stopwatch` budget);
2. otherwise retypes when `writes < MaxWrites` and `clock.Elapsed - lastWrite >= RetypeInterval` (100ms);
3. then `await Task.Delay(options.PollInterval)` (10ms).

The timeout check is before the retype check. A poll delay that wakes after the budget therefore returns the write count it already has and never issues the next write. The assertion failed on that returned count. The probe did not throw, and the TRX counters record `timeout=0`.

## What ran at the same time

125 `UnitTestResult` rows overlap `[2026-11-10T15:01:29.8160739Z, 2026-11-10T15:01:30.9277239Z]`. This test is the only Failed row in that window and in the chunk. The diag command line is one treenode filter covering the whole group (`ComposerInputProbeTests*` together with the other `Antiphon.Agents.Pty.Tests` classes in group 1), so they share one process.

Same-class methods in flight across that window:

| Test | Duration | Outcome |
|---|---|---|
| `A_token_that_never_renders_fails_and_the_launch_is_told` | 1.0508190s | Passed (started 0.3µs later) |
| `A_token_that_renders_late_is_still_a_pass` | 1.3627629s | Passed (still running at the failure end) |
| `A_composer_that_will_not_clear_fails_even_though_the_token_rendered` | 1.0550933s | Passed |
| `A_lingering_token_that_clears_on_a_later_kill_line_still_passes` | 1.0542832s | Passed |

Longer overlapping tests from other classes (duration over 3s; all Passed):

| Duration | Test |
|---|---|
| 8.0098677s | `Settle_failure_detail_summarises_the_polls` |
| 7.0269848s | `Retries_respect_settle_and_attempt_limits` |
| 6.3814224s | `Navigation_candidates_are_observed_before_confirmation` (argument U+000E) |
| 6.2413346s | `An_unmovable_wrong_highlight_withholds_Enter(immovable)` |
| 5.8780766s | `Settle_trace_fires_on_gate_changes_not_per_poll(static-hold)` |
| 5.7908855s | `Recurring_dialogs_cannot_renew_the_readiness_deadline` |
| 5.3339348s | `A_dialog_whose_highlight_never_moves_gets_no_Enter` |
| 4.7943194s | `Navigation_candidates_are_observed_before_confirmation` (argument ESC `[B`) |
| 4.1874340s | `Interrupted_probes_share_the_token_write_limit` |
| 4.0817154s | `C464_Real_fixture_is_invariant_at_every_byte_boundary` |
| 3.2157084s | `A_swallowed_Enter_is_retried_only_while_the_same_target_is_selected` |
| 3.1998425s | `Requested_effort_is_applied(Fable 5·1, xhigh, xhigh, 2, 1)` |
| 3.1997530s | `Requested_effort_is_applied(Fable 5·1, xhigh, high, 1, 2)` |
| 3.1996987s | `Requested_effort_is_applied(Nimble 9, medium, medium, 2, 1)` |

Console and diag lines adjacent to the failure are other tests passing. The diag record immediately before the failure node is `Windows_missing_pairs_fall_back_and_preserve_request` (Passed, 0.0561496s) and `A_composer_that_will_not_clear_fails_even_though_the_token_rendered` (Passed). There is no sharing-violation text, no poll-expiry text, and no stderr.

## Passing siblings

`ComposerInputProbeTests` contributed 15 results: 14 passed, 1 failed.

| Passed test | Duration |
|---|---|
| `A_composer_that_will_not_clear_fails_even_though_the_token_rendered` | 1.0550933s |
| `A_lingering_token_that_clears_on_a_later_kill_line_still_passes` | 1.0542832s |
| `A_modal_interrupts_before_each_probe_write_or_verdict(AwaitingToken)` | 0.0041968s |
| `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeClearRetry)` | 0.0405472s |
| `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeFirstClear)` | 0.0000403s |
| `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeFirstToken)` | 0.0000783s |
| `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeResponsive)` | 0.0125983s |
| `A_modal_interrupts_before_each_probe_write_or_verdict(BeforeRetype)` | 0.0124821s |
| `A_reading_composer_answers_with_one_write_and_one_kill_line` | 0.0199762s |
| `A_token_that_never_renders_fails_and_the_launch_is_told` | 1.0508190s |
| `A_token_that_renders_late_is_still_a_pass` | 1.3627629s |
| `The_probe_never_sends_a_carriage_return` | 0.0199463s |
| `The_token_can_never_be_read_as_a_command` | 0.0311761s |
| `The_token_is_derived_from_the_session_so_a_log_line_is_attributable` | 0.0002006s |

They bound the cause. The modal, single-write, late-render, clear, and kill-line paths all passed in this same process. The sibling that uses the same `DeafForever` composer, `A_token_that_never_renders_fails_and_the_launch_is_told`, asserts only `Outcome == NeverAppeared` (budget `timeoutMs: 300`, default retype 100s so it writes once). It passed in 1.0508190s, about 3.5 times its 300ms budget. A short write count is invisible to that sibling.

## Classification

Timing/load. The failing assertion is on returned state (`Writes` 2 versus 3), and that state is how many retypes fit in a 1000ms `Stopwatch` budget before the timeout return.

Quoted evidence:

- TRX message: `result.Writes should be 3 but was 2`, additional info `three writes inside the budget, including the first`. No `TimeoutException`, no sharing violation, no poll-expiry string. `ResultSummary` `timeout=0`, `error=0`.
- Body duration `00:00:01.1116500` sits just past the configured `timeoutMs: 1000`. That is the probe returning from `clock.Elapsed >= options.Timeout`, not the runner aborting the test.
- Unstarved, the 100ms retype interval places writes at about 0, 100ms, and 200ms, then the loop idles until 1000ms with `Writes == 3`. `Writes == 2` means the second write landed inside the last 100ms, and the next loop observation took the timeout branch (checked first) instead of the retype branch. A `Task.Delay(10ms)` stretched by this chunk does that.
- The same-clock sibling with a 300ms budget took 1.0508190s and still passed, because it does not count writes. 125 other results overlapped the window, including the multi-second startup and effort tests listed above.

This is not a deterministic "the probe always stops at two writes" failure. The production loop issues the third write once `elapsed - lastWrite >= 100ms` while `elapsed < 1000ms`.

## Why this reached the rc run

Group 1 runs `ComposerInputProbeTests` in one process with the rest of the 599-test filter. The only assertion that needs three wall-clock retypes inside a 1s budget is this method. Every other method in the class either keeps the default 100s retype (one write) or asserts outcome and kill-line counts. A poll delay that overshoots the retype window fails only here, so the rest of the class stays green (14 passed beside this one failure) and the chunk has no separate serialized pass for the write cap.

## Debug handoff

Cause: under `agents-pty-all-group-1` load, the 10ms poll delay in `ComposerInputProbe.RunAsync` woke after the 1000ms `Stopwatch` budget had already passed the third retype. `DeafForever` made the probe return `NeverAppeared` with `Writes == 2`. The test required 3 (`retypeMs: 100`, `maxWrites: 3`). Fix the budget-versus-delay race in a Debug task. Do not treat a quiet timeout widening as the repair; the count is the contract under test.
