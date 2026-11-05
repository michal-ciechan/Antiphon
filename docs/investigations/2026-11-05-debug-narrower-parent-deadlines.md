# Debug: Narrower_parent_deadlines_win and the TRX-less Unit row

Debug task `86a941a3`. Subject is Astra Review `3f52b55a` of `origin/feat/card-task-73c94e0b` at `467b00c25ba04fb2a6cbe583079e06211c93c8a7` (CARD-1717 S4a). Evidence read on this host: `/tmp/3f52b55a-evidence/docs/investigations/2026-11-05-review-card-1717-a.md` section B-6, plus the copied checkpoint run `/tmp/3f52b55a-evidence/checkpoint-run/`. No product or test code was changed.

## Outcome

Two separate facts. Neither is a defect introduced by the S4a diff.

1. `ResilienceBudgetTests.Narrower_parent_deadlines_win` failed once inside the killed Unit host. The same class is green in isolation at master `cc8845f030d934507e7a0d2db7d939be06d9f2bc` and at the S4a tip. The resilience sources under test are the same blobs on both commits. The 10.4s reading is the fake-clock pump stepping past a 5s parent deadline while the timeout continuation was still queued. It is a pre-existing harness flake, not an S4a product failure.
2. CP-29 has no TRX because the checkpoint execution budget killed the test host while Unit tests were still finishing. The host was not hung, and this method was not the test in progress at the kill.

## What the 584 / 5 / no-TRX line is

Run `20261105-013711-58df`. Request `totalTimeoutMinutes` 30, `hardWallTimeoutMinutes` 60, `rowTimeoutMinutes` 20. Receipt budget: `executionLimitSeconds` 1800, `executionSeconds` 1801.105, `admissionOnlySeconds` 255.039, `terminalReason` `execution-budget-exceeded`, exit 5. Wall 34m16s.

Completed rows, which are the 584 passes and 5 skips:

| Row | Passed | Failed | Skipped | Execution |
|---|---:|---:|---:|---:|
| CP-23 | 155 | 0 | 0 | 137.223s |
| CP-24 | 55 | 0 | 0 | 31.944s |
| CP-26 | 326 | 0 | 5 | 162.371s |
| CP-27 | 47 | 0 | 0 | 155.355s |
| CP-28 | 1 | 0 | 0 | 7.413s |
| CP-29 Unit | none | none | none | 535.215s, then killed |

155+55+326+47+1 = 584. The five skips are the CP-26 Windows skips. CP-29 contributed no count.

CP-29 in `report.json`: `state` `execution-budget-exceeded`, `exitCode` 5, `trx` null, `executed`/`passed`/`failed`/`skipped` null, `timings` null. Source capture at the end of the row is `not_observed`. Its own manifest cap is `timeoutMinutes` 15 and `estimatedMinutes` 5. 535s is under that 15-minute row cap. The run-level 30-minute execution budget expired first. The cold build phase (739.650s in the CP-23 phase line) counts as execution, not admission. Estimates on the manifest sum to 22, so `RowTimeout.DeriveTotalMinutes` would have been `max(30, 2*22+10)` = 54 minutes. The request overrode that with an explicit 30.

## Why there is no TRX

`rows/CP-29/` contains `console.log` only (98 lines, 6650 bytes). The log has the TUnit banner, then skips and one failure, then git fixture stdout, then more skips. It has no run-summary footer and no results path.

`ProcessDriver.RunAsync` waits on the test host with the budget token. On `OperationCanceledException` it calls `Kill(entireProcessTree: true)` and rethrows (`tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs`, the cancel path after `WaitForExitAsync`). Microsoft.Testing.Platform writes the TRX when the host exits on its own. A tree kill mid-run leaves no TRX. That is what `trx=n/a` and `executed=n/a` record. Finalization after the kill was 0.294s.

The host was still making progress. The last raw stdout is `PlanCatalogServiceTests` (`[Category("Unit")]`) creating `/tmp/antiphon-plans-*` repositories and committing `2026-08-16-card-0035-stuck-work-view.md` and `2026-08-26-card-0034-review-and-react-plan.md`. After that, `SessionDeliveryProfileTests` (`[Category("Unit")]`) records Windows skips, and the log ends on `Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind`. Those two classes overlap because the Unit host runs tests in parallel. Neither line is a hang signature.

`Narrower_parent_deadlines_win` is earlier in the same console. It had already failed in 1s 448ms. The host kept running after that failure. The missing TRX is not this method blocking the process.

Smallest fix for the missing TRX: do not put `/*/*/*/*[Category=Unit]` in one row under an explicit 30-minute total timeout, and do not estimate that row at 5 minutes. Shard the lane (the existing Unit-shard rule) and set `--total-timeout` from the real sum. Raising only CP-29's 15-minute row cap would not have saved this run. The checkpoint tool's kill-on-budget behavior is doing what the receipt says.

## The failure that did occur

Console text, copied from `/tmp/3f52b55a-evidence/checkpoint-run/rows/CP-29/console.log`:

```
failed Narrower_parent_deadlines_win (1s 448ms)
  ShouldAssertException: elapsed
      should be less than
  00:00:07
      but was
  00:00:10.4000000
    at ResilienceBudgetTests.Narrower_parent_deadlines_win() line 315
```

The method loops parent deadlines of 3s, 5s, and 10s. The bound at line 315 is `seconds + 2`. `00:00:07` is the 5-second iteration, so the 3-second iteration had already passed. `10.4000000` is exactly 52 steps of the 200ms pump (`ResilienceTestHost.Pump` in `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceTestSupport.cs`).

What the product does on that iteration:

- `ResilienceBudget.Start` narrows the 120s settings total to the 5s owner deadline (`src/Antiphon.Resilience/ResilienceBudget.cs`).
- The outer HTTP timeout generator arms `Budget.Remaining` at the start of the call (`ResiliencePipelineCache.BuildHttp`). That value is 5s, not 10.4s.
- Polly 8.4.2 `TimeoutResilienceStrategy` turns that into `CancellationTokenSource.CancelAfter` on a source constructed with the pipeline `TimeProvider` (the test's `FakeTimeProvider`).
- `AdmittedReadHandler` maps `TimeoutRejectedException` to `TaskCanceledException`. The test's `ThrowAsync<TaskCanceledException>` succeeded. The only failed assertion is the fake-clock upper bound.

The pump advances 200ms and then `await Task.Delay(1)` until `work.IsCompleted`. The timeout continuation is the async state machine in `TimeoutResilienceStrategy.ExecuteCore`, which uses `ConfigureAwait(context.ContinueOnCapturedContext)` and the default of that flag is false. When the fake-time timer fires inside `Advance`, cancellation can complete the delay on the pump thread while the rest of the pipeline continuation waits on the thread pool. Until that continuation marks the send task complete, the pump takes another 200ms step. The assertion then reads `time.GetUtcNow()`, which has moved past the deadline by every extra step.

10.4s is 5.0s plus 27 extra steps (5.4s). The allowed slack is 2s (10 steps). The original host's wall for this method was only 1.448s, which is a tight pump loop, not a 10-second real wait. The run snapshot's load was `4.92 12.79 23.25` on 24 cores, and CP-29 acquired its build slot while 3 of 4 slots were occupied. That is the condition where a queued continuation loses the race to the pump.

## Isolation runs

Filter `/*/*/(ResilienceBudgetTests*)/*`. Seven methods in the class, one lease per run, `UseAppHost=false`, native output, `TEMP=/tmp`. No failure message, because nothing failed.

| Commit | Run | Lease | Result | Duration |
|---|---|---|---|---|
| `cc8845f030d934507e7a0d2db7d939be06d9f2bc` | 1 | `50b89294-2cca-4780-ac02-90b3711192b3` | 7 passed, 0 failed, 0 skipped, exit 0 | 8.514s |
| same | 2 | `6214fc60-288b-4395-a3e8-4ddd5ffe974c` | 7/0/0, exit 0 | 6.808s |
| same | 3 | `0974d37c-eed8-46e7-b845-fed7fb5040fd` | 7/0/0, exit 0 | 3.713s |
| `467b00c25ba04fb2a6cbe583079e06211c93c8a7` | 1 | `7926c2ba-a208-4a29-a980-851dcfc246f4` (waited 60s) | 7/0/0, exit 0 | 6.565s |
| same | 2 | `f4a92682-bd34-427b-ba82-963282e3ec8e` | 7/0/0, exit 0 | 4.612s |
| same | 3 | `1b84a033-bb3e-4355-8cd3-f548231a687d` | 7/0/0, exit 0 | 4.531s |

Pass rate of the class, and of `Narrower_parent_deadlines_win` inside it: 3/3 at master, 3/3 at the S4a tip.

Blobs identical at both commits (`git diff --stat` of `src/Antiphon.Resilience` and `tests/Antiphon.Tests/Infrastructure/Resilience` is empty):

| File | Blob |
|---|---|
| `ResilienceBudgetTests.cs` | `16dcc5c3e76f28b10b93206a42dbe7c9a09d3d5a` |
| `ResilienceTestSupport.cs` | `ce82e3fb3009cf24b76149473c2324c476b80003` |
| `ResiliencePipelineCache.cs` | `6ded622b93a6b407e4da03a6c3bd8de60194489b` |
| `ResilienceBudget.cs` | `3eafada23e9a085ef972f000602230c84a65bd73` |

S4a build of `tests/Antiphon.Tests` at `467b00c`: lease `3ef05b4a-67e9-4aa3-b7a8-891afe2f9368`, 0 errors, 4m10.89s. Master build: lease `6c8ddae4-3208-4d6c-b1c4-ef75d0516473`, exit 0.

## Smallest fix for the flake

Do not loosen `elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(seconds + 2))`. The product timeout is already armed for the owner deadline.

Change the pump used by this method so that once fake time has reached the owner deadline it stops calling `Advance` and waits on the real clock (`WaitAsync`) for the send task. A continuation that was already due then completes with `elapsed` still at the deadline, inside the existing 2s slack. A product path that never cancels at that deadline fails the real wait instead of reporting a pumped-forward fake clock. `AdvanceAfterAsync` in the same helper is already the one-boundary form of this. The open `Pump` loop is what manufactures the 10.4s number.

This is a test-harness change on master. It is not a change to guarded worktree removal.

## What this does not decide

Windows-native CP-25 and the Windows skips in B-6 were not run. This host was not allowed to touch the desktop while qualification `b19a9880` was live. No card was filed.
