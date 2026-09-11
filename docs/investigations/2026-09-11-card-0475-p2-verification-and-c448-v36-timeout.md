# CARD-0475 P2 defect verification, and the C448_V36 native timeout

Task `5818b581` (Code). Branch `feat/card-task-3bfe742a`, tip `2c794407`.

## Status of the three P2 defects

All three were already implemented in `2c794407` ("test: fix CARD-0475 model
validation and fixture receipt cleanup"), which existed only as an unpushed local
commit on a branch checked out in two worktrees. It has now been pushed to
`origin/feat/card-task-3bfe742a`.

| # | Review defect | Where it is closed |
|---|---|---|
| 1 | `ControlledLandingGit` returned unconditional success for `stash`/`config` and ignored unsupported arguments | `ValidateCommand` allow-list of whole argument vectors, called from the single `ExecuteAsync` chokepoint that both `RunAsync` and `RunOwnedAsync` funnel through, before fault hooks and before the owned-process `started` callback. The `stash`, `config`, `branch`, `commit-tree`, `check-ref-format`, `remote get-url` and `ls-remote` handlers were deleted. `C475_UnsupportedArgumentsCannotSucceedOrMutate` drives 39 invalid-argument vectors for recognized verbs and asserts no trace, no callback, no ref/head/remote mutation, no source-directory deletion and zero native process starts. |
| 2 | `C475_PumpIsJoinedBeforeFixtureDisposal` tested a private nested `CleanupAsync`; `PtyWorld.StartAsync` leaked on failure after launching the runner; `DisposeAsync` skipped runner cleanup when the pump throw | `PtyWorld.DisposeAsync` is now memoised (`_disposing ??= DisposeCoreAsync()`) and runs every cleanup step under `AttemptAsync`, collecting failures into one `AggregateException` instead of short-circuiting. `StartAsync` takes a `checkpoint` hook and wraps the whole post-launch body in `try/catch`, disposing the partly built world and attaching any cleanup failure as `error.Data["CleanupFailure"]`. `C475_PumpIsJoinedBeforeFixtureDisposal` now drives the real `world.DisposeAsync()`; `C475_FaultedPumpStillCleansFixtureResources` and `C475_FailedStartupCleansFixtureResources(launched\|seeded\|ready\|pumping)` cover the failure paths. All of them assert the recipient process exited, one `KillCalls`, no queued-message/transcript/session rows left, and cwd + transcript file gone. |
| 3 | `C475_AlreadyIdleWhenIdleHasRecipientReceipt` and `C475_MultilineWritesKeepPasteMarkers` checked only the recipient file | Both now assert `SessionQueueTranscriptPump.DestinationUserPromptsAsync(...)` equals the exact complete body. The already-idle case additionally asserts the persisted `DeliveryVerdict.Delivered` and `LastDeliveryBaselineSequence == 1`; the multiline case asserts `dto.LastDelivery.ConfirmedBy == DeliveryConfirmedBy.Transcript`. `ConfirmedBy` is transient (`DeliveryOutcome`/DTO only, never a column on `SessionQueuedMessage`), so the destination-database `UserPrompt` row is the strongest persisted transcript evidence available on the WhenIdle path — and `DeliveryVerdict.Delivered` alone would not have distinguished it, since a screen-only confirmation carries the same verdict (`SessionMessageQueueService.ToReceipt`). |

## Verification run at `2c794407`

Built once: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c5818/` (0 errors).

| Filter | Result |
|---|---|
| `/*/*/(AgentTaskLandAdmissionControlledTests*)\|(AgentTaskLandBoundaryControlledTests*)\|(AgentTaskLandConcurrencyControlledTests*)\|(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)\|(ControlledLandingGitTests*)\|(DelegationHarnessCensusTests*)/*` | 234/234 passed, 1m 01s |
| `/*/*/SessionQueueReceiptPlumbingTests/*` | 20/20 passed, 49.8s |
| `/*/*/AgentTaskLandConcurrencyTests/C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders` | 4/4 passed, 1m 43s |
| `/*/*/*/*[Category=Unit]` | 2053 total, 2052 passed, 1 skipped, 0 failed, 1m 05s |

The landing-consumer classes are the gap the allow-list change could plausibly have
broken and were not covered by the earlier focused run; they pass unchanged.

Duration tripwire on the plumbing TRX: one unlisted slow row,
`C475_ProactiveAndReactiveRecoveryShareOneEscBudget` at 9.354s. It is pre-existing —
the review measured 9.565s for the same test at `3c64f983`. The five new cases raised
the class from 15 to 20 expanded rows and 30.195s to 34.995s of body time and added no
slow row.

## C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders(True, True)

### What actually failed

`TimeoutException: The operation has timed out.` at
`AgentTaskLandConcurrencyTests.cs:82` — `await entered.Task.WaitAsync(TimeSpan.FromSeconds(60))`.
`entered` is set inside `h.Verifier.Barrier`, so the assertion is: *the land run must
reach verification within 60 seconds of wall clock*. Nothing about the land run's
correctness was in question; the `finally` then awaited the land run to completion
without further error, and total test wall time was 2m 19.8s.

`localMerge` (the second argument) is only read after line 82, when the already-entered
barrier lets the test call `TryMergeBackAsync`. The `(True, True)` and `(True, False)`
cases therefore execute **identical code up to the point that timed out**, and
`(True, False)` passed in 45.7s in the same run. The argument tuple is not causal.

### Cost structure of the phase that timed out

`LandingEvidence` (`ANTIPHON_C448_EVIDENCE=<dir>`) emits a per-git-command timeline.
Measured spans from `before_service` (start of `h.RunAsync()`) to the `prepared`
recovery pin, which is the last event before the verifier is invoked:

| Sample | reach-barrier | whole land run | headroom vs the 60s budget |
|---|---|---|---|
| This host, 2026-09-11 02:09 UTC, quiet, both `landFirst` cases | 11.5s / 11.5s | 30.9s / 30.4s | 5.2x |
| Review's own isolated rerun, 2026-09-10 21:37–21:40 UTC | 20.8s / 16.5s | 82.9s / 54.2s | 2.9x / 3.6x |
| Broad `real` lane, 2026-09-10 21:25 UTC | **> 60s** | (completed after release) | failed |

The phase is ~230 native `git.exe` spawns plus one owned `git rebase` child; each
non-owned command costs ~70–110 ms of pure process-spawn on this machine. Its wall
time is therefore dominated by Windows process-creation latency, which is exactly the
quantity that moves under external load. The same machine, same commit, no code
change, already shows a 1.8x swing between the two measured samples above.

### What the artifacts do and do not support

- The `real` lane ran strictly sequentially (every test starts at the microsecond the
  previous one ends), so in-assembly parallel load is ruled out.
- A uniform host slowdown is **not** sufficient on its own. The two tests immediately
  preceding the failure took 55.0s and 32.7s against historical durations of 53.0s and
  28.0s — only 1.04x and 1.17x. Reaching >60s from a 16.5–20.8s baseline needs ~3x.
  The excess was therefore localised to this test's pre-verification phase, not spread
  across the window.
- Nothing in the artifacts names the operation that stalled. The broad run did **not**
  have `ANTIPHON_C448_EVIDENCE` set, so no per-command timeline exists for the failing
  execution. The rerun that passed has a timeline, and it shows no gap over 5.6s.

**Cause remains unproven.** The defensible statement is that the test asserts a fixed
60s wall-clock budget over a ~230-process-spawn phase whose measured cost varies
1.8x on an idle machine, and that a single localised stall of ~40s in that phase
occurred once and did not reproduce.

### Recommendation

Do not widen the 60s budget and do not add a retry — that would hide a real stall in
the pre-verification landing path. Instead make a recurrence diagnosable at zero risk
to the assertion: set `ANTIPHON_C448_EVIDENCE=<dir>` when running the real/native lane,
so the next occurrence yields a per-command timeline that names the stalled operation
directly. Deciding whether to make that the standing procedure for the native lane is
a Review/Plan call, not a Code one.
