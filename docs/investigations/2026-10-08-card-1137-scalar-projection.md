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
  properties, rendered as `Name=value` lines (exact type-tagged encodings since the F-2 repair below;
  strings quoted and escaped, `null` distinct from `"null"` and from any byte array); the navigation
  (constant `null` in both JSON strings) is excluded. Same assertion, same message
  ("pending bytes/status/attempt evidence retained"); nothing weakened or deleted. A property type
  the renderer does not support throws (see "Repair F-1/F-2" for the exact contract).
- **D-2. Same fix at the two other named sites**, obviously local, assertion meaning preserved:
  `ReviewEvidenceRecoveryTests.C1043_RecoveryPreservesHistory` (AgentTask, AgentSession list,
  AgentTaskLandNotification list; previously the JSON of `AsNoTracking` rows, whose navigations were
  `null` or empty default collections) and
  `CardFilePrivacySyncAcceptanceTests.Dry_run_leaves_existing_files_pins_tokens_ignore_index_and_HEAD_unchanged`
  (Board, Card list; same shape). Order of list items is unchanged (the same queries).
- **D-3. Deterministic recurrence guard** `EntityGraphSerializationGuardTests` (Unit, no DB; an
  offline `AppDbContext` model). **Removed deliberately in repair 3** (see "Repair 3" below); kept
  here as the record of what rows CP-1 and CP-39 ran:
  - `Tests_do_not_serialize_navigation_entities_straight_from_a_DbSet`: assembly-wide source census;
    a `JsonSerializer.Serialize*(` whose argument is `[await] x.<DbSet>...` fails when its result
    type after projections (F-1 repair) is, or carries, an entity with navigations. One pre-existing out-of-scope site is listed as explicit debt:
    `RemoteControlModalPersistenceTests.cs:134` (`RemoteControlModalEpisodes`, navigation
    `AgentSession`), for a Backlog card.
  - `Release_and_sweep_sources_serialize_only_payloads_and_navigation_free_values`: in
    `TerminalRunnerSeatReleaseTests`, `RunnerSeatOrphanSweepTests`, `RunnerSeatReleaseFixture` and
    `RunnerSeatLiveSeatWarmupTests`, every `JsonSerializer.Serialize*` argument must be a `new ...`
    payload literal, an allowlisted value (`invalidation.Payload`, `items`,
    `await db.RunnerSeatReleases.ToListAsync()`) or (F-1 repair) a DbSet query with a
    navigation-free result; the allowlisted entity `RunnerSeatRelease` must stay
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
| V-5 | `EntityGraphSerializationGuardTests` (30 results: the 9 above plus 10 `Scanners_admit_navigation_free_projections` and 11 `Scanners_flag_navigation_bearing_results`) | F-1: projections to scalars, strings, records, anonymous objects and dictionaries are admitted by both scanners; navigation-bearing results stay flagged with the serialized type. |
| V-6 | `EntityScalarSnapshotTests` (11 results: 9 `Distinct_supported_values_never_render_alike` groups, `Nullable_properties_distinguish_null_default_and_another_value`, `Unsupported_types_throw_naming_type_and_property`) | F-2: exact encodings at Half/TimeOnly/byte[]/DateTime/decimal/enum/floating/cross-type/string boundaries; nullables render null, default and another value three ways; unsupported types throw naming type and property. |
| R-4 | `C1043_RecoveryPreservesHistory` (1), `Dry_run_leaves_existing_files_pins_tokens_ignore_index_and_HEAD_unchanged` (5), `Pending_delivery_prevents_release` (1) | The three call sites keep their assertions under the new encodings. |
| V-7 | `EntityScalarSnapshotTests` runtime-type rows (repair 3): 14 `Unsupported_runtime_values_throw_naming_property_and_runtime_type`, 4 `Unsupported_declared_types_throw_before_the_value_is_read`, `Unsupported_roots_throw_naming_the_root_or_item` | F-2 round 2: an object-typed property holding an empty or non-empty collection, an entity, a delegate, JsonDocument, JsonElement or any value without an exact encoding throws naming the property and runtime type; unsupported declared types throw before the value is read; a root sequence whose element type is not an entity throws even when empty. |
| V-8 | `EntityScalarSnapshotTests.Snapshot_covers_exactly_the_reflected_non_navigation_properties` (6: SessionQueuedMessage, AgentTask, AgentSession, AgentTaskLandNotification, Board, Card) | The snapshot renders exactly the reflected non-navigation properties (EF navigation metadata is the oracle; 57 for SessionQueuedMessage), each changes the snapshot when changed, a navigation does not. Replaces the removed scanner guard. |
| R-5 | `TestClassificationGuardTests`, `SlowTestTripwireTests` after the class deletion | Deleting `EntityGraphSerializationGuardTests` leaves the registry classification green. |

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
| CP-39 | R1 | `tests/Antiphon.Tests -> bin-c1137r/` | linux-r1-guard | `/*/*/EntityGraphSerializationGuardTests/*` | V-1, V-5 | exact 30 results, 0 failed/skipped | 30 | 3 | true | n/a |
| CP-40 | R1 | `CP-39` | linux-r1-snapshot | `/*/*/EntityScalarSnapshotTests/*` | V-6 | exact 11 results, 0 failed/skipped | 11 | 2 | true | n/a |
| CP-41 | R1 | `CP-39` | linux-r1-pending-alone | `/*/*/TerminalRunnerSeatReleaseTests/Pending_delivery_prevents_release*` | V-2, R-4 | exact 1 results, 0 failed/skipped | 1 | 2 | true | n/a |
| CP-42 | R1 | `CP-39` | linux-r1-review-recovery-site | `/*/*/ReviewEvidenceRecoveryTests/C1043_RecoveryPreservesHistory*` | R-4 | exact 1 results, 0 failed/skipped | 1 | 3 | true | n/a |
| CP-43 | R1 | `CP-39` | linux-r1-cardfile-dry-run-site | `/*/*/CardFilePrivacySyncAcceptanceTests/Dry_run_leaves_existing_files_pins_tokens_ignore_index_and_HEAD_unchanged*` | R-4 | exact 5 results, 0 failed/skipped | 5 | 3 | true | n/a |
| CP-44 | R1 | `CP-39` | linux-r1-warmup-guard | `/*/*/RunnerSeatLiveSeatWarmupTests/*` | R-1 | exact 1 results, 0 failed/skipped | 1 | 2 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-45 | R1 | `CP-39` | linux-r1-orphan-class | `/*/*/RunnerSeatOrphanSweepTests/*` | R-1 | exact 17 results, 0 failed/skipped | 17 | 3 | true | n/a |
| CP-46 | R1 | `CP-39` | linux-r1-release-class | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-1 | exact 39 results, 0 failed/skipped | 39 | 3 | true | n/a |
| CP-47 | R1 | `CP-39` | linux-r1-registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-3 | exact 3 results, 0 failed/skipped | 3 | 2 | true | n/a |
| CP-48 | R1 | `CP-39` | linux-r1-combined-01 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-49 | R1 | `CP-39` | linux-r1-combined-02 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-50 | R1 | `CP-39` | linux-r1-combined-03 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-51 | R1 | `CP-39` | linux-r1-combined-04 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-52 | R1 | `CP-39` | linux-r1-combined-05 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-53 | R1 | `CP-39` | linux-r1-combined-06 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-54 | R1 | `CP-39` | linux-r1-combined-07 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-55 | R1 | `CP-39` | linux-r1-combined-08 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-56 | R1 | `CP-39` | linux-r1-combined-09 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-57 | R1 | `CP-39` | linux-r1-combined-10 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-58 | R1 | `CP-39` | linux-r1-combined-11 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-59 | R1 | `CP-39` | linux-r1-combined-12 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-60 | R2 | `tests/Antiphon.Tests -> bin-c1137r2/` | linux-r2-snapshot | `/*/*/EntityScalarSnapshotTests/*` | V-7, V-8 | exact 35 results, 0 failed/skipped | 35 | 3 | true | n/a |
| CP-61 | R2 | `CP-60` | linux-r2-pending-alone | `/*/*/TerminalRunnerSeatReleaseTests/Pending_delivery_prevents_release*` | V-2, R-4 | exact 1 results, 0 failed/skipped | 1 | 2 | true | n/a |
| CP-62 | R2 | `CP-60` | linux-r2-review-recovery-site | `/*/*/ReviewEvidenceRecoveryTests/C1043_RecoveryPreservesHistory*` | R-4 | exact 1 results, 0 failed/skipped | 1 | 3 | true | n/a |
| CP-63 | R2 | `CP-60` | linux-r2-cardfile-dry-run-site | `/*/*/CardFilePrivacySyncAcceptanceTests/Dry_run_leaves_existing_files_pins_tokens_ignore_index_and_HEAD_unchanged*` | R-4 | exact 5 results, 0 failed/skipped | 5 | 3 | true | n/a |
| CP-64 | R2 | `CP-60` | linux-r2-warmup-guard | `/*/*/RunnerSeatLiveSeatWarmupTests/*` | R-1 | exact 1 results, 0 failed/skipped | 1 | 2 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-65 | R2 | `CP-60` | linux-r2-orphan-class | `/*/*/RunnerSeatOrphanSweepTests/*` | R-1 | exact 17 results, 0 failed/skipped | 17 | 3 | true | n/a |
| CP-66 | R2 | `CP-60` | linux-r2-release-class | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-1 | exact 39 results, 0 failed/skipped | 39 | 3 | true | n/a |
| CP-67 | R2 | `CP-60` | linux-r2-registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-3, R-5 | exact 3 results, 0 failed/skipped | 3 | 2 | true | n/a |
| CP-68 | R2 | `CP-60` | linux-r2-combined-01 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-69 | R2 | `CP-60` | linux-r2-combined-02 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-70 | R2 | `CP-60` | linux-r2-combined-03 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-71 | R2 | `CP-60` | linux-r2-combined-04 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-72 | R2 | `CP-60` | linux-r2-combined-05 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-73 | R2 | `CP-60` | linux-r2-combined-06 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-74 | R2 | `CP-60` | linux-r2-combined-07 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-75 | R2 | `CP-60` | linux-r2-combined-08 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-76 | R2 | `CP-60` | linux-r2-combined-09 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-77 | R2 | `CP-60` | linux-r2-combined-10 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-78 | R2 | `CP-60` | linux-r2-combined-11 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |
| CP-79 | R2 | `CP-60` | linux-r2-combined-12 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | V-3 | exact 56 results, 0 failed/skipped | 56 | 3 | true | n/a |

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

## Repair F-1/F-2

Code task `fcd3f992`, branch `feat/card-task-fcd3f992` from the reviewed `0624a7079` (Final Review
`1b429692`; original Code/landing owner `98b5e24f`). Test-side only: no production change, no
migration, no AppHost or runner restart. Rows CP-39..CP-59 (`After` R1) are this repair's ordinary
scope; the whole Unit lane is not part of it (AGENTS.md forbids an unbounded broad run).

- **F-1 (`EntityGraphSerializationGuardTests`).** The scanners judged the originating DbSet, so a
  safe `Select(m => new { m.Id, m.Body })` was flagged. They now resolve the serialized result type
  along the query chain: `Select` projections (the lambda parameter's member chains, and a `new` of
  a model entity type), element operators (`Single*`, `First*`, `Find*`, ...), member access, and
  scalar aggregates (`Count*`, `Any*`, ...). STJ builds metadata per declared type, so the type
  decides, not whether a navigation is loaded. Scalar, string, record, anonymous and dictionary
  projections are admitted by both scanners; a result that is, or carries, a navigation-bearing
  entity stays flagged and is reported with its type; an unreadable expression keeps the
  originating entity (fails closed). The release/sweep scanner additionally admits a DbSet query
  with a navigation-free result; variables (`message`) stay rejected. New cases:
  `Scanners_admit_navigation_free_projections` (10) and `Scanners_flag_navigation_bearing_results`
  (11).
- **F-2 (`EntityScalarSnapshot`).** Exact type-tagged leaf encodings: `Half`/`float`/`double` carry
  round-trip text plus the IEEE bit pattern, `TimeOnly` round-trip `O` (full ticks), `byte[]` as
  `b64:<base64>` (never the null sentinel, never a quoted string), `DateTime` `O` plus `Kind`,
  `DateTimeOffset` `O` (offset kept), `DateOnly` `O`, `TimeSpan` `c`, `decimal` with its scale,
  enums as full type name, member name and underlying value, integers tagged with their type.
  Composite rendering is limited to model entity types and sequences; a property whose declared
  type is unsupported throws even while null, and any other runtime value throws, naming the type
  and the property path. The helper XML documentation states the same contract. New
  `EntityScalarSnapshotTests` (11 results). The 57 `SessionQueuedMessage` scalars and the three call
  sites are unchanged; each site compares two renderings by the same helper, so the encoding change
  does not alter an assertion's meaning.

### Repair quick mutations (Code-stage probes, not post-land PCs)

Scratch build `bin-c1137mut/` with the two classes
(`/*/*/(EntityGraphSerializationGuardTests*)|(EntityScalarSnapshotTests*)/*`), restored with
`git checkout -- tests/` after each batch; the mutated rows are attributable per parameter row.

| Batch | Temporary mutation (line it names) | Red |
|---|---|---|
| 1 | `SerializedNavigationEntity`: `break` before the chain walk (the originating DbSet decides again) | all 10 `Scanners_admit_navigation_free_projections` rows (also 4 flag rows whose expected type differs) |
| 1 | `Scalar`: `TimeOnly` default format; `Half` renders `"Half:"`; `byte[]` without `b64:`; `DateTime` without `Kind`; decimal scale normalised; enum as underlying value only; `double` without bits; integers untagged; `Quote` without backslash escaping | each of the 9 `Distinct_supported_values_never_render_alike` groups ("Half(1) and Half(2)", "TimeOnly(12:34) and TimeOnly(12:34)", "null and byte[3]{158,233,101}", ...), and `Nullable_properties_distinguish_null_default_and_another_value` |
| 1 | `Render`: the final unsupported-type `throw` becomes a silent `return` | `Unsupported_types_throw_naming_type_and_property` (`Version` value should throw) |
| 2 | `Projected`: the bare lambda parameter does not flow into the result | flag rows `m => m`, `new { Message = m }`, dictionary `["message"] = m`, `new QueuedRow(m.Id, m.Body, m)` |
| 2 | `Projected`: `ToString()` no longer exempt | admit row `m.AgentSession.ToString()` |
| 2 | `Render`: declared-type check disabled | `Unsupported_types_throw_naming_type_and_property` (`UnsupportedProbe.Link` null should throw) |

Batch 1: 25 of 41 red; batch 2: 6 of 41 red; restored source 41/41 green. These probes do not
discharge any PC; the original PC-1..PC-3 and the warm-up PC-1..PC-2 remain pending for SourceLanding
Mutation, and the rows above are offered as additional PC candidates.

### Repair results (R1)

Source `7a703ae1b7911b20c70df6af80847b30cd9a83bb` (clean, `buildSource=verified`; `validate`
reports `CHECKPOINT SOURCE VALID rows=21`). Checkpoint run `20261008-083436-5358`, `--after R1
--serial`, one test build (`bin-c1137r/`, 246 s, lease `c9725255-5734-4e81-88a0-4c280492a989`, slot
waited 0 s; the report's `builds: 2` counts the unselected S1 entry `bin-c1137p/`, which was not
built), every row `slot=granted` (waited 0 s except CP-50, 135 s), `unlisted: none`, wall 38m49s,
verdict GREEN. Host load average 18-28 during the run.

| Rows | Result |
|---|---|
| CP-39 V-1/V-5 guard | 30/30 green |
| CP-40 V-6 snapshot boundaries | 11/11 green |
| CP-41 `Pending_delivery_prevents_release` | 1/1 green |
| CP-42 `C1043_RecoveryPreservesHistory` | 1/1 green |
| CP-43 card-file dry-run site | 5/5 green |
| CP-44 warm-up guard | 1/1 green |
| CP-45 orphan class | 17/17 green |
| CP-46 release class | 39/39 green |
| CP-47 registry guard | 3/3 green |
| CP-48..CP-59 V-3 combined 56 | **12 of 12 green** (56/56 each); host wall 110-164 s per row |

<details><summary>R1 CHECKPOINT lines (unedited)</summary>

```text
CHECKPOINT CP-39 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=ok filter=/*/*/EntityGraphSerializationGuardTests/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/EntityScalarSnapshotTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-41 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/Pending_delivery_prevents_release* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-42 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/ReviewEvidenceRecoveryTests/C1043_RecoveryPreservesHistory* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-43 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/CardFilePrivacySyncAcceptanceTests/Dry_run_leaves_existing_files_pins_tokens_ignore_index_and_HEAD_unchanged* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-44 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/RunnerSeatLiveSeatWarmupTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-44/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-45 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/RunnerSeatOrphanSweepTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-45/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-46 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-46/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-47 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-47/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-48 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-48/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-49 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-49/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-50 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-50/run.trx slot=granted waited=135s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-51 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-51/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-52 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-52/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-53 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-53/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-54 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-54/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-55 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-55/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-56 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-56/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-57 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-57/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-58 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-58/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
CHECKPOINT CP-59 commit=7a703ae1b7911b20c70df6af80847b30cd9a83bb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fcd3f992/.antiphon/checkpoints/20261008-083436-5358/rows/CP-59/run.trx slot=granted waited=0s dirty=0 source=7a703ae1b7911b20c70df6af80847b30cd9a83bb sourceState=clean buildSource=verified
```

</details>

### Repair run inventory (every build and test driver, all under the build-slot gate)

| # | What | Outcome | Duration | Lease |
|---|---|---|---|---|
| 1 | Dev build `tests/Antiphon.Tests -> bin-c1137dev/` (UseAppHost=false), first compile of the repair | ok, 0 errors | 118 s | `97535567-cceb-4e40-8a93-bcbc778ba2a6`, waited 0 s |
| 2 | Dev run, the two unit classes (`/*/*/(EntityGraphSerializationGuardTests*)\|(EntityScalarSnapshotTests*)/*`) at `ff1d453c6` source | 41/41 green | 8 s | `7734cb05-3acb-44da-aa6d-5452753a96ff`, waited 0 s |
| 3 | Mutation batch 1 build `bin-c1137mut/` | ok | 130 s | `5c064b46-4b5e-4995-bf6d-a222a3cf9944`, waited 0 s |
| 4 | Mutation batch 1 run, same filter | 25 failed / 16 passed (intended red) | 9 s | `93e6db98-40d6-4d63-85cf-746fc5657649`, waited 0 s |
| 5 | Mutation batch 2 build `bin-c1137mut/` | ok | 67 s | `a349aeb1-c1db-4308-9253-6545eedb3bb1`, waited 0 s |
| 6 | Mutation batch 2 run, same filter | 6 failed / 35 passed (intended red) | 9 s | `c738d274-f06d-4a47-a911-fbb18f9db998`, waited 0 s |
| 7 | Checkpoint tool bootstrap `tools/Antiphon.Checkpoints -> bin-c1137rdrv/` | ok | 3 s | `9bf9f63c-79ef-49b0-9a44-2824bd07451b`, waited 0 s |
| 8 | Checkpoint run `20261008-083436-5358` (one build + 21 rows, above) | GREEN 21/21 | 38m49s | build `c9725255-5734-4e81-88a0-4c280492a989`; one lease per row in `executor.log` |

Runs 1-6 are Code-stage development and mutation probes (reason: compile the repair and prove each
new test red), not ordinary evidence; the ordinary evidence is run 8 alone. Runs 3-6 restored with
`git checkout -- tests/`; all `bin-c1137dev`, `bin-c1137mut` and `bin-c1137rdrv` directories were
removed with a root-confined loop; the green checkpoint run deleted its own `bin-c1137r/`.

## Repair 3: drop the scanner guard, runtime-type snapshot

Code task `26526802`, branch `feat/card-task-26526802` from the reviewed `6ec083b8d` (Final Review
`5d0ad279`, findings F-1/F-2; original Code/landing owner `98b5e24f`). Test-side only: no
production change, no migration, no AppHost or runner restart. Rows CP-60..CP-79 (`After` R2) are
this repair's ordinary scope; the whole Unit lane is not part of it (AGENTS.md forbids an unbounded
broad run). The CARD-1137 flake fix itself (D-1/D-2) is unchanged.

- **Scanner guard removed, deliberately** (commit `c6a0cb7e3`). `EntityGraphSerializationGuardTests`
  was introduced on this branch (not on master) and is not needed for the fix. A static source
  scanner cannot establish the type `JsonSerializer` will serialize: round 1 found it over-broad,
  round 2 (F-1) found it rejecting safe Boolean projections (`m.AgentSession != null`) and admitting
  nested entity projections; every round finds another edge. Its 30 cases (scanner census,
  release/sweep allowlist, scanner self-tests, admit/flag projection rows) are deleted with the
  scanner; no other assertion changed. What it protected and what protects it now:
  - *the rule* (never default-options STJ on an EF entity graph; compare a scalar projection): stated
    with its reason in the `TerminalRunnerSeatReleaseTests` class header (serializing an EF entity
    graph takes System.Text.Json's process-wide default-options metadata lock for seconds and stalls
    the in-process runner's `RunnerSessionExitedEvent` publish; compare `EntityScalarSnapshot.Of`),
    with pointers in the `ReviewEvidenceRecoveryTests` and `CardFilePrivacySyncAcceptanceTests`
    headers;
  - *snapshot completeness* (the old `Snapshot_changes_when_any_settable_non_navigation_property_changes`,
    6 types): moved, not weakened, to `EntityScalarSnapshotTests.Snapshot_covers_exactly_the_reflected_non_navigation_properties`
    (V-8), same 6 entity types. It no longer asks the helper which properties are navigations: EF's
    navigation metadata is the oracle, the snapshot's line names must equal the reflected
    non-navigation properties exactly and in order (57 for `SessionQueuedMessage`, navigation
    `AgentSession`), each settable one must change the snapshot when changed, and setting a navigation
    must not. A scalar added to an entity later is compared automatically;
  - *recurrence of the stall*: the 12 combined-filter repetitions (CP-68..CP-79) and the class rows.
  - The one pre-existing out-of-scope site the scanner listed as debt,
    `RemoteControlModalPersistenceTests.cs:134`, is unchanged and still wants its Backlog card.
- **F-2 fixed: runtime types** (commit `c4424d763`). `EntityScalarSnapshot` checks the declared type
  of every non-navigation property (even while null) and the runtime type of every value. Supported
  scalars are exactly the tagged encodings already in place (string, `byte[]`, char, bool, enums,
  date/time types, Guid, decimal, floating types with bit patterns, tagged integers) and their
  nullables. An `object`-typed property holding a collection (empty or not: `List`, array,
  `Dictionary`, `IEnumerable`), an entity, a delegate, or anything else throws naming the property
  and the runtime type. The root must be an entity or an `IEnumerable<T>` of an entity type (a
  non-entity sequence throws even when empty; a null or non-entity item throws naming `[i]`).
  Owned types and primitive collections are unsupported. **JsonDocument/JsonElement are
  unsupported**: by reflection none of `SessionQueuedMessage`, `AgentTask`, `AgentSession`,
  `AgentTaskLandNotification`, `Board`, `Card` has a property of either type (the declared-type check
  at the three call sites would throw if one appeared), so the shared `Json:` encoding is gone rather
  than split. The helper XML documentation states this contract. The three call sites are unchanged
  and compare two renderings by the same helper, so their assertions keep their meaning.

### Repair 3 quick mutations (Code-stage probes, not post-land PCs)

Scratch build `bin-c1137r2mut/`, filter `/*/*/EntityScalarSnapshotTests/*` (35), source restored with
`git checkout -- tests/` after each batch; each red row is attributable to one mutation.

| Batch | Temporary mutation (`EntityScalarSnapshot.cs` line) | Red |
|---|---|---|
| A | `:128` an unsupported runtime value renders `?` instead of throwing | all 14 `Unsupported_runtime_values_*` rows ("should throw ... but did not", e.g. `Version`, `empty List<int>`, `delegate`) |
| A | `:50` root sequence accepted without an entity element type | `Unsupported_roots_throw_naming_the_root_or_item` (`new List<int>()` did not throw) |
| A | `:122` `RenderEntity` also skips `DeliveryAttempts` | `Snapshot_covers_exactly_*(SessionQueuedMessage)`: "the snapshot must render each of its 57 reflected non-navigation properties ... and no navigation (AgentSession)" |
| B | `:125` declared-type check disabled | `Unsupported_declared_types_*` rows `UriProbe.Link`, `ListProbe.Tags`, `JsonDocumentProbe.Document` (null values); the `JsonElement` row stays green because its non-null default is caught by the runtime check |
| C | `:95`/`:103` JsonDocument/JsonElement re-admitted with a shared `Json:` encoding | runtime rows `JsonDocument`, `JsonElement`; declared rows `JsonDocument`, `JsonElement` |
| C | `:134` navigation rule inverted (`entity.IsOwned()`) | `Snapshot_covers_exactly_*` for SessionQueuedMessage, AgentTask, AgentSession, Board, Card (AgentTaskLandNotification has no navigation and stays green) |

Batch A 16 of 35 red, batch B 3 of 35, batch C 9 of 35; restored source is CP-60. These probes
discharge no PC: PC-1..PC-3 (PC-2 named the removed scanner and is void with it; its protection is
now the class-header rule) and the warm-up PC-1..PC-2 stay pending for SourceLanding Mutation; the
rows above are offered as additional PC candidates.
