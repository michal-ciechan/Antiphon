# CARD-0988: late post-trust poll must still record sign-in

Date: 2026-10-12. Card: Antiphon `692bc6a5-5824-455c-8ab1-5ae52405c9af`.
Inspected source: `593278bf2ebaa04c3eb615f2d3421ca800fc7710`. No CARD-0988 commit is on `origin/master`. At that inspection `RunnerGrokAdapterReadyTests.cs` matched `origin/master`. The repair adds the cancellation tests on this branch.

## Root cause

`Post_trust_blank_or_sign_in_is_not_ready` drives a scripted trust screen, then a sign-in screen, with a real clock, a 10s outer budget and a 3s trust budget. `GrokReadyWait.WaitAsync` answers trust, then waits one poll. On the next loop it treats an exhausted trust budget, or an exhausted outer deadline, as terminal **before** it reads the screen the poll was waiting to see.

Under a loaded Unit lane the 50ms poll continuation can run after that budget. The scripted sign-in frame is already complete, but the wait returns `false` as `Trust` or `Deadline` and never calls `OnSignIn`. `RunnerGrokAdapter.LaunchBlock` stays null. The assertion `second.LaunchBlock!.Kind` then throws `NullReferenceException`. Run alone, the same class passes because the poll wakes inside the budget.

This is an ordering bug, not a short timeout. Widening the 3s or 10s budget hides the same skip. A read that is still pending inside the in-budget bound stays cancelled at that bound: PC-24's held read must still finish as `Trust` and must not accept the late `Ready` frame. An expired-budget read that is still pending is cancelled by the owned token in Decision and is not awaited.

## Decision

When the trust budget or the outer deadline is already exhausted, classify a snapshot only if `snapshotAsync` has already completed. Do not wait for one that is still pending.

- Sign-in still invokes `OnSignIn` and fails as `SignIn`, including when the outer deadline has also elapsed. That is what sets `ProviderSignInRequired`.
- A completed frame that is still the trust dialog fails as `Trust`.
- A completed non-sign-in frame after the outer deadline fails as `Deadline` and does not become ready.
- A pending snapshot is cancelled through an owned `CancellationTokenSource` linked to the caller token. `TryTakeCompleted` cancels and disposes that source on every exit, including when the read is still pending, and does not await it. Cancelling it does not extend the deadline or the trust budget. The wait still fails as `Trust` if only the trust budget elapsed, otherwise `Deadline`. An already-completed snapshot, including `ProviderSignInRequired`, is classified as it was at `a0ab543a3`.
- A snapshot that is not consumed has a fault observer attached immediately after it is created and before any `IsCompleted` or `IsFaulted` test. The observer is `ContinueWith` with `OnlyOnFaulted | ExecuteSynchronously`, `CancellationToken.None`, and `TaskScheduler.Default`. A continuation attached to an already-faulted task still runs and reads `task.Exception`. A snapshot that is consumed is observed by its consumer (`GetResult`). The test gate `SnapshotFaultGate` faults in the old gap between those two checks; production leaves it unset.

No assertion is loosened and no timeout is widened. The new test injects the late wake with `PollGateClock.Advance` and does not sleep.

## Files

- `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs` — classify an already-complete frame after the budget, cancel a pending read, and observe an unconsumed snapshot fault before any completion test.
- `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTests.cs` — `Late_poll_after_trust_still_records_sign_in_block`, `Expired_trust_budget_cancels_pending_read`, `Expired_outer_deadline_cancels_pending_read`, `Expired_budget_observes_fault_between_status_checks`.

## Verification design

Ordinary scope is the new method plus the two classes that call `GrokReadyWait.WaitAsync`. The dispatch forbids the full Unit lane: a loaded lane is how the skip was observed, and it cannot prove the ordering. No landing, lease, or persistence class is affected. The closed rows below are the whole ordinary run.

V-1. A poll that wakes at the 3s trust budget, and a poll that wakes at the 10s outer deadline, both see the already-complete sign-in screen, return not ready, set `ProviderSignInRequired`, write `y` once, and read two snapshots. Test: `RunnerGrokAdapterReadyTests.Late_poll_after_trust_still_records_sign_in_block`. Labels: `launchBlockAfterLateTrustPoll`, `launchBlockAfterLateDeadlinePoll`.

V-2. A poll that wakes after the trust budget, whose next snapshot stays pending, returns not ready as `Trust` and cancels that read before returning. Test: `RunnerGrokAdapterReadyTests.Expired_trust_budget_cancels_pending_read`. Label: `pendingTrustReadCancelled`.

V-3. A poll that wakes after the outer deadline, whose next snapshot stays pending, returns not ready as `Deadline` and cancels that read before returning. Test: `RunnerGrokAdapterReadyTests.Expired_outer_deadline_cancels_pending_read`. Label: `pendingDeadlineReadCancelled`.

V-4. A snapshot that faults after `IsFaulted` was false and before `IsCompleted` is read is not consumed, returns not ready as `SnapshotFailure`, and raises no `UnobservedTaskException`. Test: `RunnerGrokAdapterReadyTests.Expired_budget_observes_fault_between_status_checks`. Label: `unobservedSnapshotFault`.

R-1. The existing post-trust sign-in arm, PC-24's held read (`trustExpiryOutcome`), and `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` (outcome `Trust` at 1000ms) stay as they are.

Case -> mutation -> red:

| Case | Mutation on pre-slice code | Red |
|---|---|---|
| V-1 | The loop already returns `Trust` or `Deadline` before reading the post-trust screen | `Late_poll_after_trust_still_records_sign_in_block` fails on `launchBlockAfterLateTrustPoll` because `LaunchBlock` is null |
| V-2 | `TryTakeCompleted` passes the caller token and leaves the post-trust read pending | `Expired_trust_budget_cancels_pending_read` fails on `pendingTrustReadCancelled` (`sawCancel` is false). Red on `a0ab543a3`: 2 failed, 0 passed |
| V-3 | `TryTakeCompleted` passes the caller token and leaves the post-deadline read pending | `Expired_outer_deadline_cancels_pending_read` fails on `pendingDeadlineReadCancelled` (`sawCancel` is false). Red on `a0ab543a3`: 2 failed, 0 passed |
| V-4 | The fault observer is only the `IsFaulted` branch or the still-incomplete continuation, so a fault between those checks is unobserved | `Expired_budget_observes_fault_between_status_checks` fails on `unobservedSnapshotFault` (count was 1). Red on the pre-fix check order at `37085cdfa`: 1 failed, 0 passed |

### Positive controls

Pending for method-scoped SourceLanding Mutation. This round does not plant mutants.

| PC | Break guard by compiling production defect | Detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | Return `Fail(Trust)` at the top of the loop when the trust budget is already exhausted, before taking the completed snapshot. | `/*/*/RunnerGrokAdapterReadyTests/Late_poll_after_trust_still_records_sign_in_block` | `launchBlockAfterLateTrustPoll` |
| PC-2 | Return `Fail(Deadline)` at the top of the loop when the outer deadline is already exhausted, before taking the completed snapshot. Leave the trust path classifying. | `/*/*/RunnerGrokAdapterReadyTests/Late_poll_after_trust_still_records_sign_in_block` | `launchBlockAfterLateDeadlinePoll` |
| PC-3 | On the exhausted trust-budget path, pass the caller token and do not cancel the pending snapshot. | `/*/*/RunnerGrokAdapterReadyTests/Expired_trust_budget_cancels_pending_read` | `pendingTrustReadCancelled` |
| PC-4 | On the exhausted outer-deadline path, pass the caller token and do not cancel the pending snapshot. | `/*/*/RunnerGrokAdapterReadyTests/Expired_outer_deadline_cancels_pending_read` | `pendingDeadlineReadCancelled` |
| PC-5 | Delete the unconditional fault observer that runs before any completion test, leaving `SnapshotFaultGate` able to fault the snapshot in that gap. | `/*/*/RunnerGrokAdapterReadyTests/Expired_budget_observes_fault_between_status_checks*` | `unobservedSnapshotFault` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c988/` | grok-ready-flake | `/*/*/(RunnerGrokAdapterReadyTests)\|(GrokLinuxBlockingPromptTests*)/*` | V-1, V-2, V-3, V-4, R-1 | all listed, 0 failed, 0 skipped | 29 | 12 |
| CP-2 | S2 | CP-1 | grok-ready-observe | `/*/*/RunnerGrokAdapterReadyTests/Expired_budget_observes_fault_between_status_checks*` | V-4 | all listed, 0 failed, 0 skipped | 1 | 3 |

`RunnerGrokAdapterReadyTests` has no `*`, so the row does not select `RunnerGrokAdapterReadyTestsPty`. The second operand keeps `*` because a class segment with no `*` is one literal class name and selects zero tests.

### Cost

Ordinary floor is 15 minutes. S1 is this note. S2 is the wait change, the late-poll test, the cancellation tests, and the fault-observation test. The pre-slice red run of V-1 is unlisted. The pre-slice red run of V-2 and V-3 is also unlisted: on `a0ab543a3` both failed and none passed, because `sawCancel` stayed false. The pre-fix red run of V-4 is unlisted: on the check-order at `37085cdfa` the one test failed and none passed, because `unobservedSnapshotFault` was 1. CP-1 and CP-2 are the ordinary green run. CP-2 reuses the CP-1 build.

next: review
