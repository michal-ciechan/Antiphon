# CARD-1479 settlement sync registers a lease waiter

Date: 2026-10-22. Code task 00195a3a (Final round, minimal testing per the brief: the closed CP-1..CP-3
table only, no Unit lane, no whole-suite run). Source: Debug 16d74064,
`docs/investigations/2026-10-22-runner-sync-lease-busy.md`, audited at
`b59706530950ad5fb334c216a6f18a4e9bfd5103`.

`RemoteWorkspaceService.WhileLeaseBusyAsync` carries an ordinary settlement sync's cumulative wait in
`RunnerSyncLeaseWaits`, but nothing registers a `worktree-settlement` waiter in the shared
`RepositoryLeaseWaiters` turnstile, so `AgentTaskLandService.YieldToDispatchAsync` never sees a real
sync and serial lands keep re-taking the lease in the sync's retry gaps. The production registrations
are dispatch and start-ref-fetch only.

## Slices

### S1-test (red witness, committed first)

* `tests/Antiphon.Tests/Application/RunnerSyncLeaseFairnessTests.cs`: V-1 only, plus the shared
  world (a `LandingSafetyHarness` repository, a second runner-bound sync task registered in the same
  repository, one `RepositoryLeaseWaiters` shared by the land service and the sync service, one
  `FakeTimeProvider`).
* `server/Application/Services/RemoteWorkspaceService.cs`: the optional constructor parameter
  `RepositoryLeaseWaiters? leaseWaiters = null` stored and otherwise unused, so the red witness can
  hand the sync the land's registry and compile. The producer wiring is S1.

### S1 (fix and the remaining tests)

* `RemoteWorkspaceService`:
  1. At either actual busy boundary of an ordinary (non-`singleAttempt`) sync, the observation
     fetch or the mutation acquisition, `WhileLeaseBusyAsync` registers
     `(baseline.CanonicalCommonDirectory, task.Id, worktree-settlement)` with `Since` equal to the
     cumulative first-busy time `RunnerSyncLeaseWaits` already carries and
     `VerificationCompatible=false`. A repeat registration keeps the original `Since`
     (`RepositoryLeaseWaiters.Register`).
  2. The registration is cleared when that acquire succeeds after a wait, and at the end of
     `SyncCoreAsync` for every outcome other than `runner_sync_lease_waiting`: success, refusal,
     spent budget (`runner_sync_lease_busy`), timeout, exception, caller cancellation and the early
     eligibility exits (`NotApplicable`, branch/identity mismatch, baseline unavailable), so a task
     that is no longer eligible on its next re-hand drops its waiter.
  3. `singleAttempt` recovery (`SyncSettledAsync`, `SyncParkedAsync`) never registers and never
     clears: a five-minute debt backoff is not runnable demand.
  4. No custody or policy change: the repository lease, verification-window fence, 120 s cumulative
     budget, 10 s slice, `RunnerSyncLeaseWaits` semantics and the 90 s land yield bound are untouched.
* `tests/Antiphon.Tests/Application/RunnerSyncLeaseFairnessTests.cs`: V-2..V-7.

## Verification design

| ID | Method | Invariant |
|---|---|---|
| V-1 | `Busy_sync_makes_next_land_yield` | A real sync waiting on a held R registers a `worktree-settlement` waiter without any test calling `Register`; the next real land admission on that repository is Held with `LeaseYieldedToDispatchCode` before any Git mutation. Red at the base: the snapshot stays empty and the land is not yielded. After the lease is released the sync fast-forwards, the waiter is gone and the land completes. |
| V-2 | `Fetch_wait_registers_before_objects_are_local` | With S absent locally and the observation lease busy, the same waiter appears; no `fetch`, `update-ref` or `merge --ff-only` ran and the checkout is unchanged. |
| V-3 | `Waiter_survives_slices_with_original_since` | Two fresh service scopes each return `runner_sync_lease_waiting` after their slice; one waiter with the original `Since` survives between them and a land yields meanwhile; after release the third scope succeeds, the waiter is removed and the land lands. |
| V-4 | `Every_terminal_exit_clears_waiter` | One method, an internal table: acquisition after a wait, refusal (diverged tip), exhausted budget, exception under the lease, caller cancellation during the wait, and a changed-eligibility re-hand. No waiter remains after any arm. |
| V-5 | `Debt_backoff_leaves_no_waiter` | A `singleAttempt` debt sync against a busy lease returns `runner_sync_lease_busy` at once (Pending/retry pacing is the recovery service's unchanged mapping) and registers nothing at either boundary. |
| V-6 | `Verification_window_remains_exclusive` | With a real live verification window open and R released, an ordinary sync is still refused for its whole budget, modifies neither the checkout nor the branch, and clears its waiter on the terminal `runner_sync_lease_busy`; closing the window lets the next sync succeed. |
| V-7 | `Production_services_share_the_waiter_registry` | The booted Program's own `RemoteWorkspaceService` is driven against a held lease; the root `RepositoryLeaseWaiters` singleton shows the waiter and decides `Yield`, the scoped `AgentTaskLandService` holds that same instance, and the waiter is cleared once the sync completes. |
| R-1 | existing classes | `RunnerSettlementSyncTests`, `AgentTaskLandDispatchYieldTests`, `RepositoryLeaseWaitersTests` keep their outcomes (fast-forward/identity/retirement guards, 120 s budget and 10 s slices, bounded yield, registry cleanup). |

Positive controls: none planned in this task; deliberate mutants and missing-control discovery stay
with method-scoped SourceLanding Mutation (zero-PC plan).

Lane: portable .NET/PostgreSQL on the default runner (server2, Linux). No `-Runner`, no `-Platform`.
The land-admission tests use real git through `LandingSafetyHarness`; the clock is fake, so slices
and budgets are advanced, never slept. V-7 boots `AntiphonWebAppFactory` on its own cloned schema.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-test | `tests/Antiphon.Tests -> bin-c1479-red/` | sync-waiter-red | `/*/Antiphon.Tests.Application/RunnerSyncLeaseFairnessTests/Busy_sync_makes_next_land_yield` | V-1 red witness | 1 executed, 1 expected assertion failure; build succeeds | 1 | 6 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1479-green/` | sync-waiter-green | `/*/Antiphon.Tests.Application/RunnerSyncLeaseFairnessTests/*` | V-1..V-7 | all 7 methods passed, 0 failed/skipped | 7 | 9 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1479-regression/` | sync-custody-regression | `/*/Antiphon.Tests.Application/(RunnerSettlementSyncTests*)\|(AgentTaskLandDispatchYieldTests*)\|(RepositoryLeaseWaitersTests*)/*` | R-1 | all selected passed, 0 failed/skipped | 43 | 12 |

CP-3 floor is 30+6+7 existing `[Test]` methods; `[Arguments]` expansion raises the executed count.
CP-1 runs on the S1-test commit and is expected red (run exit 1); CP-2 and CP-3 run on the final tip.

### Cost

Ordinary floor 27 minutes (CP-1 6, CP-2 9, CP-3 12) plus the checkpoint tool bootstrap build.

## rc/2026-10-20

The Code report records whether the S1-test and S1 commits cherry-pick cleanly onto
`origin/rc/2026-10-20` by themselves (`git merge-tree --merge-base` per commit) and what they depend on.
