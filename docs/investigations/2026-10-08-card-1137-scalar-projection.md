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

(pending)
