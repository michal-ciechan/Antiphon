# CARD-0966 Code report

Four baseline ownership fixture failures are repaired: the full ownership class passed 5/5 and the dedicated model check passed 1/1. Adjacent classes reproduce exactly the same 40 failures and 3 passes at base and fixed source. Both method-scoped scratch controls produced the intended assertion failures. CP-7 verifies the final restored, published source after committing this evidence; its source-bound receipt is in the task's final settlement report.

## Design note (before source edits)

Base: `e4706adabfb74c5498da506dcce25e1e098ad1f8`; CP-1-base-ownership executed 5, passed 1, failed 4, skipped 0.

- Upgrade (0/2/500): PostgreSQL `42703: column "PinLastNotifiedHash" of relation "AgentSessions" does not exist` at `PreContinuityDbContext.SaveChangesAsync`. The helper excludes continuity fields but still maps later columns on sessions, tasks, agents, projects, boards and cards. This is a test fixture defect, not a missing production column. Restrict the seeded entity mappings using the predecessor migration's frozen target model, retaining inserts into the actual predecessor schema. After applying only the continuity migration, query only the columns it has rather than materializing today's entire entity.
- Recreate/resume: base asserts `standing_resume_not_owned` but receives `conflict`. `AgentControlService.StartInteractiveSessionAsync` calls `AgentExecutableResolver.EnsureSpawnable` before `FindResumableSessionAsync`/`StandingSessionOwnership.RequireAsync`; the fake registry configures `Path.Combine(Environment.SystemDirectory, "cmd.exe")`, which is nonexistent on Linux. Configure an existing executable for the fake adapter; retain the documented foreign-owner code and preservation assertions. Capture the original exception message during the scratch regression to confirm this exact path.

Files: `tests/Antiphon.Tests/Application/StandingSessionOwnershipTests.cs` and its private `PreContinuityDbContext` only. No production, shared fixture, migration, snapshot, selection-test or client changes planned.

Verification: direct `scripts/run-checkpoint.ps1`, commit/push before ordinary runs, literal filters and unique output roots. Full ownership class (5), separate C561 model case (1), adjacent StandingSessionQueueSwitchTests, StandingSessionSwitchConcurrencyTests and StandingContinuityRecoveryTests. One method-scoped scratch regression per fixed test method (upgrade expands 0/2/500), then exact restoration and green. Unit conditional on production/shared test changes. SourceLanding PCs remain pending; Code spot-checks do not discharge them.

### Checkpoints

All rows use `tests/Antiphon.Tests` and build a separate output with a leased direct checkpoint invocation. No frozen plan exists; this table records the brief's named scopes and the explicit scratch-control requirement.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | base | `tests/Antiphon.Tests -> bin-c966-base/` | base-ownership | `/*/*/(StandingSessionOwnershipTests*)/*` | both baseline signatures | 5 executed, reproduce four failures | 5 | 4 |
| CP-2 | fix | `tests/Antiphon.Tests -> bin-c966-fixed/` | fixed-ownership | `/*/*/(StandingSessionOwnershipTests*)/*` | four repaired cases and model consistency | 5 pass | 5 | 4 |
| CP-3 | fix | `tests/Antiphon.Tests -> bin-c966-model/` | model | `/*/*/(StandingSessionOwnershipTests*)/C561_model_and_migration_agree*` | dedicated model/snapshot check | 1 pass | 1 | 4 |
| CP-4 | fix | `tests/Antiphon.Tests -> bin-c966-adjacent/` | adjacent | `/*/*/(StandingSessionQueueSwitchTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingContinuityRecoveryTests*)/*` | neighbouring ownership, switch and continuity guards | full classes including concurrency partials; classify inherited failures separately | 43 | 8 |
| CP-5 | scratch | `tests/Antiphon.Tests -> bin-c966-control-exe/` | control-exe | `/*/*/(StandingSessionOwnershipTests*)/Deleting_and_recreating_the_same_name*` | revert the portable fake executable | 1 fails at ownership-code assertion, original exception identifies executable validation | 1 | 4 |
| CP-6 | scratch | `tests/Antiphon.Tests -> bin-c966-control-upgrade/` | control-upgrade | `/*/*/(StandingSessionOwnershipTests*)/Upgrade_backfills*` | perturb seeded recovery count | all 0/2/500 fail at persisted backfill assertion | 3 | 4 |
| CP-7 | restored | `tests/Antiphon.Tests -> bin-c966-restored/` | restored-ownership | `/*/*/(StandingSessionOwnershipTests*)/*` | exact restored source after controls | 5 pass, git diff empty | 5 | 4 |

No Unit row unless production or shared test code changes, as the brief explicitly requires.

CP-6 reruns=1: the first input-perturbation control correctly observed 1/3/501 instead of 0/2/500, but Shouldly displayed `state.ConsecutiveFailures` for the second assertion because both counter assertions occupied one source line. Commit `1003bc0ccd138de7a846c8603402363b8fddaefd` splits those unchanged assertions. Repeat the same method/three arguments in `bin-c966-control-upgrade-rerun/` to retain unambiguous named `state.RestartBackoffFailures` red evidence. This diagnostic correction does not change the test's expected values or product behavior.

Unlisted comparative run CP-4B-base-adjacent is necessary because CP-4 exposes unchanged Linux fake-executable failures (and gate timeouts before their intended preflight): run the same full three-class filter against a clean detached worktree at the starting SHA. Project/output `tests/Antiphon.Tests -> bin-c966-base-adjacent/`, 43-case floor, results under this task's checkpoint root. Baseline checkout: `/tmp/c966-controls.yvs49E/base`. No branch rewrite, rebase, merge or production/shared-fixture changes.

## Baseline receipt

```text
CHECKPOINT CP-1-base-ownership commit=e4706adabfb74c5498da506dcce25e1e098ad1f8 build=ok filter=/*/*/(StandingSessionOwnershipTests*)/* executed=5 passed=1 failed=4 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-1-base-ownership-20261002-153849-6b61/run.trx slot=granted waited=0s dirty=0 source=e4706adabfb74c5498da506dcce25e1e098ad1f8 sourceState=clean buildSource=verified
```

## Fixed class and model receipts

```text
CHECKPOINT CP-2-fixed-ownership commit=aad0d1a7373d7269c602b2ba1ebad1620a820d1c build=ok filter=/*/*/(StandingSessionOwnershipTests*)/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-2-fixed-ownership-20261002-154454-a453/run.trx slot=granted waited=1s dirty=0 source=aad0d1a7373d7269c602b2ba1ebad1620a820d1c sourceState=clean buildSource=verified
CHECKPOINT CP-3-model commit=aad0d1a7373d7269c602b2ba1ebad1620a820d1c build=ok filter=/*/*/(StandingSessionOwnershipTests*)/C561_model_and_migration_agree* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-3-model-20261002-155040-07c5/run.trx slot=granted waited=0s dirty=0 source=aad0d1a7373d7269c602b2ba1ebad1620a820d1c sourceState=clean buildSource=verified
```

## Adjacent and executable-control receipts

```text
CHECKPOINT CP-4-adjacent commit=aad0d1a7373d7269c602b2ba1ebad1620a820d1c build=ok filter=/*/*/(StandingSessionQueueSwitchTests*)|(StandingSessionSwitchConcurrencyTests*)|(StandingContinuityRecoveryTests*)/* executed=43 passed=3 failed=40 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-4-adjacent-20261002-155356-aecc/run.trx slot=granted waited=300s dirty=0 source=aad0d1a7373d7269c602b2ba1ebad1620a820d1c sourceState=clean buildSource=verified
CHECKPOINT CP-5-control-exe commit=aad0d1a7373d7269c602b2ba1ebad1620a820d1c build=ok filter=/*/*/(StandingSessionOwnershipTests*)/Deleting_and_recreating_the_same_name* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-5-control-exe-20261002-160708-9fb8/run.trx slot=granted waited=0s dirty=1 source=aad0d1a7373d7269c602b2ba1ebad1620a820d1c+dirty:c28d6438a2a51a555ef8a8ca1a5d83c6c7fcfbd809fe084ec9cbc8d0b1a3286c sourceState=dirty buildSource=verified
```

## Unchanged-base adjacent receipt

```text
CHECKPOINT CP-4B-base-adjacent commit=e4706adabfb74c5498da506dcce25e1e098ad1f8 build=ok filter=/*/*/(StandingSessionQueueSwitchTests*)|(StandingSessionSwitchConcurrencyTests*)|(StandingContinuityRecoveryTests*)/* executed=43 passed=3 failed=40 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-4B-base-adjacent-20261002-160500-fee6/run.trx slot=granted waited=45s dirty=0 source=e4706adabfb74c5498da506dcce25e1e098ad1f8 sourceState=clean buildSource=verified
```

## Adjacent baseline classification

Both base and fixed selections executed 43: passed 3, failed 40, skipped 0. Exact class + argument-expanded test-name comparison found 0 changed outcomes.

| Class | Executed | Passed | Failed |
|---|---:|---:|---:|
| Antiphon.Tests.Application.StandingContinuityRecoveryTests | 11 | 0 | 11 |
| Antiphon.Tests.Application.StandingSessionQueueSwitchTests | 8 | 0 | 8 |
| Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests | 24 | 3 | 21 |

The unchanged shared StandingRecoveryFixture still configures cmd.exe on Linux. Direct launch/refusal cases fail at executable validation; concurrency gates consequently time out before preflight. These failures were reproduced at the starting SHA, not repaired in this ownership-only patch. No StandingSessionSelectionTests or shared fixture file was edited. Coordinate a shared portability repair with CARD-0950 separately.

Inherited failing cases:

- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.An_unsuccessful_explicit_retry_restores_the_same_continuity_hold`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(first)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(incompatible)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(legacy-pointer-lost)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(malformed)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Default_start_distinguishes_first_launch_from_unavailable_prior_identity(missing)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(fresh)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(retry)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Held_retry_selection_and_fresh_have_separate_accepted_decisions(selection)`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Infrastructure_failure_with_stale_missing_text_does_not_hold_continuity`
- `Antiphon.Tests.Application.StandingContinuityRecoveryTests.Missing_native_target_holds_after_one_resume_without_create`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(False)`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.A_corrupt_current_pointer_cannot_transfer_another_owners_pending_input(True)`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Any_prior_delivery_evidence_refuses_switch_and_fresh`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Only_unattempted_messages_move_atomically_and_keep_order_and_routing`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Blocked)`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Dispatched)`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Open_execution_on_either_history_or_current_target_refuses_selection(Working)`
- `Antiphon.Tests.Application.StandingSessionQueueSwitchTests.Target_late_confirmation_and_open_task_guards_remain_intact`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(automatic)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(default)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(fresh)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Concurrent_starts_and_supervisor_reserve_one_generation(selection)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Failure_after_reservation_writes_rolls_back_owner_pointer_hold_queue_and_decision`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Failed, True)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Running, True)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(Stopped, True)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(False)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(True)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(card)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(delivery)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(execution)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(generation)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(owner)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_rechecks_changes_committed_after_runner_preflight(pointer)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(before-spawn)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(saved-running)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(start-rpc)`
- `Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Stop_supersedes_launch_before_spawn_and_before_typing`

## Upgrade-control receipt

```text
CHECKPOINT CP-6-control-upgrade commit=aad0d1a7373d7269c602b2ba1ebad1620a820d1c build=ok filter=/*/*/(StandingSessionOwnershipTests*)/Upgrade_backfills* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-6-control-upgrade-20261002-161220-b82b/run.trx slot=granted waited=15s dirty=1 source=aad0d1a7373d7269c602b2ba1ebad1620a820d1c+dirty:42572c884d58201f3de1cabe19cb6b334821f9978b56ab334f99fca9da6c0833 sourceState=dirty buildSource=verified
```

## Named upgrade-control rerun receipt

```text
CHECKPOINT CP-6-control-upgrade-rerun commit=1003bc0ccd138de7a846c8603402363b8fddaefd build=ok filter=/*/*/(StandingSessionOwnershipTests*)/Upgrade_backfills* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-840ae3fa/.antiphon/checkpoints/CP-6-control-upgrade-rerun-20261002-161712-e799/run.trx slot=granted waited=0s dirty=1 source=1003bc0ccd138de7a846c8603402363b8fddaefd+dirty:4b1e98caa815f4a0b6271b94c7cb99da505dd66761621e85ab1ef586f8bc2beb sourceState=dirty buildSource=verified
```

## Implementation and handoff

- Source change: `tests/Antiphon.Tests/Application/StandingSessionOwnershipTests.cs` only, including its private `PreContinuityDbContext`. Fake launch validation uses the current executable; legacy seeded entities exclude properties/relationships absent from the frozen `OrderSpecialistHealthRequests` target model; reads after the continuity migration project only available columns. Assertions and expected error semantics are retained. Two counter assertions occupy separate lines so Shouldly correctly identifies the backfill assertion.
- Code commits: `aad0d1a7373d7269c602b2ba1ebad1620a820d1c`, then diagnostic-only `1003bc0ccd138de7a846c8603402363b8fddaefd`. Both pushed on `feat/card-task-840ae3fa`, fast-forward from `e4706adabfb74c5498da506dcce25e1e098ad1f8`.
- Durable evidence copy: `docs/investigations/2026-10-02-card-0966-standing-session-ownership.md`; working report: `.antiphon/task-840ae3fa.md`. This document is committed before the final ordinary run to keep that run's source receipt clean and bound to the published HEAD. The final CP-7 receipt is included verbatim in the settlement message.
- Executable control: restoring the original cmd.exe configuration fails the unchanged ownership-code assertion; captured original exception names `AgentExecutableResolver.EnsureSpawnable` line 95 and `AgentControlService.StartInteractiveSessionAsync` line 476. A valid fake executable reaches the existing `standing_resume_not_owned` contract. This is a fixture/platform defect, not a product error-code defect.
- Upgrade control: perturbing seed `ConsecutiveFailures` to `oldCount + 1` completes the real predecessor seed and migration, then all 0/2/500 cases fail the unchanged persisted `RestartBackoffFailures` assertion (1/3/501). CP-6 reruns=1 corrects only Shouldly's misleading same-line expression diagnostics. No fixture/build/zero-test failure is claimed as this assertion control.
- Restoration: both mutations restored byte-for-byte using the saved producer-owned source. Final source SHA-256: `396e6b6b550aa3616513b111e90e855b9cd308ffa64e859e9bc48babe5e3ba64`; `git diff --exit-code` passed before this report commit. No mutation is committed or pushed.
- Adjacent comparison: all 43 argument-expanded names unique on both arms; 0 roster differences and 0 changed outcomes. The 40 inherited cases are listed above. No shared StandingRecoveryFixture or CARD-0950 StandingSessionSelectionTests change; no cross-card shared-source land serialization is needed for this patch.
- Unit lane deliberately omitted: the brief requires it only for production/shared test changes; this patch changes one test file and its private helper. No production, migration, snapshot, Attention/client, configuration or contract change. Restart: none. No deployment or land performed.
- Formal SourceLanding controls remain pending: this Code round and the two fixture spot-checks discharge none of the standing-continuity PC-1..PC-7 or PC-561-27 requirements. No new product guard or PC introduced.
- Landing owner: original Code task `840ae3fa-6cd1-439e-a9cb-ee9b2b0cc83c`. Next: Final Review, then caller's plain land of that original task if clean; inherited shared fixture failures are a separate repair coordinated with CARD-0950. No decision outside CARD-0966 required.

Rerun the repaired class from the reviewed checkout (substitute its full published SHA):

```sh
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name review-c966-ownership -Project tests/Antiphon.Tests -OutputPath bin-c966-review/ -Filter '/*/*/(StandingSessionOwnershipTests*)/*' -MinExecuted 5 -Expect 'Deleting_and_recreating_the_same_name,Upgrade_backfills,C561_model' -ExpectedSourceSha <full-reviewed-sha>
```

The checkpoint table above gives the exact model/adjacent filters and all other scopes. Original base clone `/tmp/c966-controls.yvs49E/base` was removed only after its owned test driver exited and its TRX/receipt were retained under this task's checkpoint root.
