# CARD-0988: late post-trust poll must still record sign-in

Date: 2026-10-12. Card: Antiphon `692bc6a5-5824-455c-8ab1-5ae52405c9af`.
Inspected source: `593278bf2ebaa04c3eb615f2d3421ca800fc7710`. No CARD-0988 commit is on `origin/master`. `RunnerGrokAdapterReadyTests.cs` is the same blob on this branch and on `origin/master`.

## Root cause

`Post_trust_blank_or_sign_in_is_not_ready` drives a scripted trust screen, then a sign-in screen, with a real clock, a 10s outer budget and a 3s trust budget. `GrokReadyWait.WaitAsync` answers trust, then waits one poll. On the next loop it treats an exhausted trust budget, or an exhausted outer deadline, as terminal **before** it reads the screen the poll was waiting to see.

Under a loaded Unit lane the 50ms poll continuation can run after that budget. The scripted sign-in frame is already complete, but the wait returns `false` as `Trust` or `Deadline` and never calls `OnSignIn`. `RunnerGrokAdapter.LaunchBlock` stays null. The assertion `second.LaunchBlock!.Kind` then throws `NullReferenceException`. Run alone, the same class passes because the poll wakes inside the budget.

This is an ordering bug, not a short timeout. Widening the 3s or 10s budget hides the same skip. A read that is still pending stays cancelled at the budget: PC-24's held read must still finish as `Trust` and must not accept the late `Ready` frame.

## Decision

When the trust budget or the outer deadline is already exhausted, classify a snapshot only if `snapshotAsync` has already completed. Do not wait for one that is still pending.

- Sign-in still invokes `OnSignIn` and fails as `SignIn`, including when the outer deadline has also elapsed. That is what sets `ProviderSignInRequired`.
- A completed frame that is still the trust dialog fails as `Trust`.
- A completed non-sign-in frame after the outer deadline fails as `Deadline` and does not become ready.
- A pending snapshot is abandoned and the wait fails as it does today (`Trust` if only the trust budget elapsed, otherwise `Deadline`).

No assertion is loosened and no timeout is widened. The new test injects the late wake with `PollGateClock.Advance` and does not sleep.

## Files

- `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs` — classify an already-complete frame after the budget.
- `tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTests.cs` — `Late_poll_after_trust_still_records_sign_in_block`.

## Verification design

Ordinary scope is the new method plus the two classes that call `GrokReadyWait.WaitAsync`. The dispatch forbids the full Unit lane: a loaded lane is how the skip was observed, and it cannot prove the ordering. No landing, lease, or persistence class is affected. The closed row below is the whole ordinary run.

V-1. A poll that wakes at the 3s trust budget, and a poll that wakes at the 10s outer deadline, both see the already-complete sign-in screen, return not ready, set `ProviderSignInRequired`, write `y` once, and read two snapshots. Test: `RunnerGrokAdapterReadyTests.Late_poll_after_trust_still_records_sign_in_block`. Labels: `launchBlockAfterLateTrustPoll`, `launchBlockAfterLateDeadlinePoll`.

R-1. The existing post-trust sign-in arm, PC-24's held read (`trustExpiryOutcome`), and `GrokLinuxBlockingPromptTests.C1006_Trust_remains_blocked_until_cleared` (outcome `Trust` at 1000ms) stay as they are.

Case -> mutation -> red, run on the pre-slice `GrokReadyWait` before the production edit:

| Case | Mutation on pre-slice code | Red |
|---|---|---|
| V-1 | The loop already returns `Trust` or `Deadline` before reading the post-trust screen | `Late_poll_after_trust_still_records_sign_in_block` fails on `launchBlockAfterLateTrustPoll` because `LaunchBlock` is null |

### Positive controls

Pending for method-scoped SourceLanding Mutation. This round does not plant mutants.

| PC | Break guard by compiling production defect | Detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | Return `Fail(Trust)` at the top of the loop when the trust budget is already exhausted, before taking the completed snapshot. | `/*/*/RunnerGrokAdapterReadyTests/Late_poll_after_trust_still_records_sign_in_block` | `launchBlockAfterLateTrustPoll` |
| PC-2 | Return `Fail(Deadline)` at the top of the loop when the outer deadline is already exhausted, before taking the completed snapshot. Leave the trust path classifying. | `/*/*/RunnerGrokAdapterReadyTests/Late_poll_after_trust_still_records_sign_in_block` | `launchBlockAfterLateDeadlinePoll` |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c988/` | grok-ready-flake | `/*/*/(RunnerGrokAdapterReadyTests*)\|(GrokLinuxBlockingPromptTests*)/*` | V-1, R-1 | all listed, 0 failed | 26 | 12 |

### Cost

Ordinary floor is 12 minutes. S1 is this note. S2 is the wait change and the late-poll test. The pre-slice red run of V-1 is unlisted and is the only extra test run: it proves the new test fails before the production edit.

next: review
