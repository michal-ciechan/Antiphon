# CARD-1137: scalar projection instead of entity-graph serialization

Date: 2026-10-08. Code task `98b5e24f`. Branch `feat/card-task-98b5e24f` from `1123a7ede`
(root cause: `docs/investigations/2026-10-08-card-1137-release-stall.md`, Investigate `6ac659a5`;
earlier warm-up remedy: `docs/investigations/2026-10-08-card-1137-warmup-remedy.md`).
Test-side only. No production change, no migration, no AppHost or runner restart.

## Change

- **D-1. `Pending_delivery_prevents_release` compares a scalar projection.** Lines 1008/1013 of
  `TerminalRunnerSeatReleaseTests.cs` used `JsonSerializer.Serialize(SessionQueuedMessage)` with
  default options. They now use `EntityScalarSnapshot.Of(db, message)`
  (`tests/Antiphon.Tests/TestHelpers/EntityScalarSnapshot.cs`), which renders every public readable
  property by reflection, in ordinal name order, without System.Text.Json, so it never takes the
  process-wide `JsonSerializerOptions.Default` caching-context lock the runner's
  `SessionRunnerEventHub.Publish` (`SessionRunnerRuntime.cs:4197`) waits on.
- **What is compared, before and after.** Before: the default STJ JSON of the message: every public
  property of `SessionQueuedMessage` (57 scalar properties: ids, `Body`, `Status`, `Origin`,
  `DeliveryAttempts`, `LastDeliveryStartedAt`, `HoldUntil`, `CreatedAt`, verdict/maintenance/rules
  fields, ...) plus `"AgentSession": null` (the navigation is never loaded here: a fresh context
  before, `ChangeTracker.Clear()` + no `Include` after). After: every one of those same scalar
  properties, rendered as `Name=value` lines (strings quoted and escaped, `DateTime` as round-trip
  `O` plus `Kind`, enums by type and name, `null` distinct from `"null"`); the navigation (constant
  `null` in both JSON strings) is excluded. Same assertion, same message
  ("pending bytes/status/attempt evidence retained"); nothing weakened or deleted. A property type
  the renderer does not understand throws instead of being skipped.
- **D-2. Same fix at the two other named sites**, obviously local, assertion meaning preserved:
  `ReviewEvidenceRecoveryTests.C1043_RecoveryPreservesHistory` (AgentTask, AgentSession list,
  AgentTaskLandNotification list; previously the JSON of `AsNoTracking` rows, whose navigations were
  `null` or empty default collections) and
  `CardFilePrivacySyncAcceptanceTests.Dry_run_leaves_existing_files_pins_tokens_ignore_index_and_HEAD_unchanged`
  (Board, Card list; same shape). Order of list items is unchanged (the same queries).
- **D-3. Deterministic recurrence guard** `EntityGraphSerializationGuardTests` (Unit, no DB; an
  offline `AppDbContext` model):
  - `Tests_do_not_serialize_navigation_entities_straight_from_a_DbSet`: assembly-wide source census;
    a `JsonSerializer.Serialize*(` whose argument is `[await] x.<DbSet>...` for a DbSet whose entity
    has navigations fails. One pre-existing out-of-scope site is listed as explicit debt:
    `RemoteControlModalPersistenceTests.cs:134` (`RemoteControlModalEpisodes`, navigation
    `AgentSession`), for a Backlog card.
  - `Release_and_sweep_sources_serialize_only_payloads_and_navigation_free_values`: in
    `TerminalRunnerSeatReleaseTests`, `RunnerSeatOrphanSweepTests`, `RunnerSeatReleaseFixture` and
    `RunnerSeatLiveSeatWarmupTests`, every `JsonSerializer.Serialize*` argument must be a `new ...`
    payload literal or an allowlisted value (`invalidation.Payload`, `items`,
    `await db.RunnerSeatReleases.ToListAsync()`); the allowlisted entity `RunnerSeatRelease` must stay
    navigation-free.
  - `Scanners_flag_the_CARD_1137_shapes`: the two scanners flag the original lines 1008/1013 and a
    `Boards` serialize, and accept payload literals and `RunnerSeatReleases`.
  - `Snapshot_changes_when_any_settable_non_navigation_property_changes` (6 entity types): changing
    any one settable non-navigation property changes the snapshot; setting a navigation does not.
  The rule is also stated in the `TerminalRunnerSeatReleaseTests` class header.

## Verification design

| ID | Test | Promise |
|---|---|---|
| V-1 | `EntityGraphSerializationGuardTests` (9 results) | Guard and snapshot completeness, D-3. |
| V-2 | `TerminalRunnerSeatReleaseTests.Pending_delivery_prevents_release` alone | D-1 keeps the 21 input/shape iterations green. |
| V-3 | Combined `(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)`, 56 results, 30 serial repetitions, one host each | 0 red runs expected; baseline 2/20 with the warm-up, 3/16 before it. |
| V-4 | Instrumented timing (scratch only, see Results) | Longest runner `RunnerSessionExitedEvent` publish serialize per run is tens of ms, not seconds. |
| R-1 | `RunnerSeatLiveSeatWarmupTests` (1), `RunnerSeatOrphanSweepTests` (17), `TerminalRunnerSeatReleaseTests` (39) | No behaviour change. |
| R-2 | `ReviewEvidenceRecoveryTests` (21), `CardFilePrivacySyncAcceptanceTests` (35) | D-2 sites stay green as whole classes. |
| R-3 | `TestClassificationGuardTests`, `SlowTestTripwireTests` | New Unit class needs no Slow entry; 3 results. |

Repeat budget: V-3's 30 repetitions exceed the default `repeat-proof` budget (3 normal + 2 loaded).
The flake was demonstrated by the warm-up remedy's checkpoint run `20261008-013108-9e3c`
(CP-20/CP-22 red, 53/56) and the S1 investigation (3/16); the revised budget of 30 serial runs is
the caller brief's (Code `98b5e24f`), because a 0/20 record cannot be told from luck at a ~10 % rate.
Each repetition is its own process: the stall is a first-use-per-process lock, so an in-host
`--repeat` would not reproduce it.

Not in scope: the whole Unit lane (the brief's ordinary scope is the rows below; AGENTS.md
forbids an unbounded broad run), namespaces, the full assembly, `Antiphon.Agents.Pty.Tests`.

### Quick mutations (Code-stage probes, not post-land PCs)

| PC | Temporary mutation | Filter | Expected red |
|---|---|---|---|
| PC-1 | `TerminalRunnerSeatReleaseService.HasPendingDeliveryAsync` first runs `ExecuteUpdate(DeliveryAttempts + 1)` on the session's messages (the release path changes pending-delivery state) | `/*/*/TerminalRunnerSeatReleaseTests/Pending_delivery_prevents_release` | snapshot mismatch `DeliveryAttempts=0` vs `=1`, "pending bytes/status/attempt evidence retained" |
| PC-2 | Lines 1008/1013 restored to the original `JsonSerializer.Serialize(...)` | `/*/*/EntityGraphSerializationGuardTests/(Release_and_sweep*)\|(Tests_do_not*)` | both name `TerminalRunnerSeatReleaseTests.cs:1008`/`:1013` |
| PC-3 | `EntityScalarSnapshot.Render` skips the property `DeliveryAttempts` | `/*/*/EntityGraphSerializationGuardTests/Snapshot_changes*` (SessionQueuedMessage) | "SessionQueuedMessage.DeliveryAttempts must be part of the snapshot" |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1137p/` | linux-snapshot-guard | `/*/*/EntityGraphSerializationGuardTests/*` | V-1 | exact 9 results, 0 failed/skipped | 9 | 6 | true | n/a |
| CP-2 | S1 | `CP-1` | linux-pending-alone | `/*/*/TerminalRunnerSeatReleaseTests/Pending_delivery_prevents_release*` | V-2 | exact 1 results, 0 failed/skipped | 1 | 2 | true | n/a |
| CP-3 | S1 | `CP-1` | linux-warmup-guard | `/*/*/RunnerSeatLiveSeatWarmupTests/*` | R-1 | exact 1 results, 0 failed/skipped | 1 | 2 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-4 | S1 | `CP-1` | linux-orphan-class | `/*/*/RunnerSeatOrphanSweepTests/*` | R-1 | exact 17 results, 0 failed/skipped | 17 | 3 | true | n/a |
| CP-5 | S1 | `CP-1` | linux-release-class | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-1 | exact 39 results, 0 failed/skipped | 39 | 3 | true | n/a |
| CP-6 | S1 | `CP-1` | linux-registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-3 | exact 3 results, 0 failed/skipped | 3 | 2 | true | n/a |
| CP-7 | S1 | `CP-1` | linux-review-recovery-class | `/*/*/ReviewEvidenceRecoveryTests/*` | R-2 | all 21, 0 failed/skipped | 21 | 6 | true | n/a |
| CP-8 | S1 | `CP-1` | linux-cardfile-privacy-class | `/*/*/CardFilePrivacySyncAcceptanceTests/*` | R-2 | all 35, 0 failed/skipped | 35 | 6 | true | n/a |
| CP-9 | S1 | `CP-1` | linux-combined-01 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-10 | S1 | `CP-1` | linux-combined-02 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-11 | S1 | `CP-1` | linux-combined-03 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-12 | S1 | `CP-1` | linux-combined-04 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-13 | S1 | `CP-1` | linux-combined-05 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-14 | S1 | `CP-1` | linux-combined-06 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-15 | S1 | `CP-1` | linux-combined-07 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-16 | S1 | `CP-1` | linux-combined-08 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-17 | S1 | `CP-1` | linux-combined-09 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-18 | S1 | `CP-1` | linux-combined-10 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-19 | S1 | `CP-1` | linux-combined-11 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-20 | S1 | `CP-1` | linux-combined-12 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-21 | S1 | `CP-1` | linux-combined-13 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-22 | S1 | `CP-1` | linux-combined-14 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-23 | S1 | `CP-1` | linux-combined-15 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-24 | S1 | `CP-1` | linux-combined-16 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-25 | S1 | `CP-1` | linux-combined-17 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-26 | S1 | `CP-1` | linux-combined-18 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-27 | S1 | `CP-1` | linux-combined-19 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-28 | S1 | `CP-1` | linux-combined-20 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-29 | S1 | `CP-1` | linux-combined-21 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-30 | S1 | `CP-1` | linux-combined-22 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-31 | S1 | `CP-1` | linux-combined-23 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-32 | S1 | `CP-1` | linux-combined-24 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-33 | S1 | `CP-1` | linux-combined-25 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-34 | S1 | `CP-1` | linux-combined-26 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-35 | S1 | `CP-1` | linux-combined-27 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-36 | S1 | `CP-1` | linux-combined-28 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-37 | S1 | `CP-1` | linux-combined-29 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-38 | S1 | `CP-1` | linux-combined-30 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |

## Results

Source `160f9206b0d43772b9281e34f7635a0be4daf400` (clean, `buildSource=verified`; `validate`
reports `CHECKPOINT SOURCE VALID rows=38`). Checkpoint run `20261008-043649-0779`, `--serial`, one
build (`bin-c1137p/`, 165 s, slot waited 0 s), every row `slot=granted waited=0s`, `unlisted: none`,
wall 68m21s, verdict GREEN. Ambient server2 load only (load average 15-34 during this session).

| Rows | Result |
|---|---|
| CP-1 V-1 guard | 9/9 green |
| CP-2 V-2 `Pending_delivery_prevents_release` alone | 1/1 green |
| CP-3 warm-up guard | 1/1 green |
| CP-4 orphan class | 17/17 green |
| CP-5 release class | 39/39 green |
| CP-6 registry guard | 3/3 green |
| CP-7 `ReviewEvidenceRecoveryTests` | 21/21 green |
| CP-8 `CardFilePrivacySyncAcceptanceTests` | 35/35 green |
| CP-9..CP-38 V-3 combined 56 | **30 of 30 green** (56/56 each); host wall 95-143 s per row |

Before: 3/16 red (no warm-up), 2/20 red (warm-up), 2/8 red in the instrumented investigation.
After: 0/30, plus 5/5 in the timing runs below. At an unchanged 10 % rate, 0/35 has probability
about 2.5 %; the timing evidence, not the count, is what shows the mechanism is gone.

**V-4 timing (unlisted runs, scratch only).** A detached scratch worktree at `160f9206` with
throwaway markers: a `Stopwatch` around `SessionRunnerEventHub.Publish`'s `JsonSerializer.Serialize`
(`SessionRunnerRuntime.cs:4197`) and around the test's `EntityScalarSnapshot.Of(db, message)`,
appended to a trace file. One build (`bin-c1137i/`, 118 s lease), five serial runs of the combined
filter through `build-slot.ps1`; the worktree was removed afterwards, nothing committed.

| Run | Load avg (1 min) | Result | Exited-event serializes | Longest exited-event serialize | Waits > 1 s | Longest owner snapshot |
|---|---:|---|---:|---:|---:|---:|
| t1 | 17.9 | 56/56 | 16 | 49 ms | 0 | 177 ms |
| t2 | 20.6 | 56/56 | 16 | 54 ms | 0 | 286 ms |
| t3 | 25.7 | 56/56 | 16 | 137 ms | 0 | 157 ms |
| t4 | 33.7 | 56/56 | 16 | 191 ms | 0 | 136 ms |
| t5 | 25.3 | 56/56 | 16 | 22 ms | 0 | 194 ms |

The investigation measured 2.5-9.9 s for the same wait with 5-6 waiters over 1 s per run. The
longest wait is now the first `RunnerSessionExitedEvent` metadata build itself (tens of ms, up to
191 ms at load average 34), with no waiter behind another serialize. The owner snapshot's first
call (reflection, no STJ, no shared lock) is 136-286 ms and blocks nobody.

**Quick mutations** (Code-stage probes; scratch builds `bin-c1137mut/`, restored, directories
deleted): PC-1 red, 1 failed, `Pending_delivery_prevents_release` on
"pending bytes/status/attempt evidence retained" (`DeliveryAttempts=0` vs `=1`), the 6 snapshot
cases green beside it. PC-2 + PC-3 batched (different files and methods): 3 failed of 9,
`Release_and_sweep_...` naming `:1008 serializes 'message'` and `:1013`, `Tests_do_not_...` naming
`:1013 serializes SessionQueuedMessages`, and `Snapshot_changes_...(SessionQueuedMessage)` on
"SessionQueuedMessage.DeliveryAttempts must be part of the snapshot". Restored green is CP-1/CP-2.
PC-1..PC-3 stay pending for SourceLanding Mutation.

**Backlog (not fixed here):** `RemoteControlModalPersistenceTests.cs:134` serializes
`RemoteControlModalEpisode` (navigation `AgentSession`) with default options for `ShouldNotContain`
checks. It is out of this brief's named sites; the guard lists it as explicit debt (`DbSetDebt`).

<details><summary>CHECKPOINT lines (unedited)</summary>

```text
CHECKPOINT CP-1 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=ok filter=/*/*/EntityGraphSerializationGuardTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/Pending_delivery_prevents_release* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/RunnerSeatLiveSeatWarmupTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/RunnerSeatOrphanSweepTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/ReviewEvidenceRecoveryTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/CardFilePrivacySyncAcceptanceTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-25/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-26/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-27 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-28 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-29 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-30 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-33/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-34 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=160f9206b0d43772b9281e34f7635a0be4daf400 build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-98b5e24f/.antiphon/checkpoints/20261008-043649-0779/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=160f9206b0d43772b9281e34f7635a0be4daf400 sourceState=clean buildSource=verified
```

</details>
