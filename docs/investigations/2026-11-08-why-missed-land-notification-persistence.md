# Why missed: land-notification persistence

One committed note for the rc/2026-11-10 group-59 failure of `AgentTaskLandNotificationPersistenceTests` (run e737e9fe). The Windows TRX at `C:\Antiphon\releases\candidates\rc-2026-11-10-74a8004f405b\logs\2026-11-08-0144-e737e9fe\` was not readable from this runner. Linux reproduced the class at the cut and at master.

## Introducing commit/card

introducing commit/card: `c53a0fe802a147da9f2dc5ae1579e92438aea60d` (CARD-1514)

That commit is the master cherry-pick of `53c50bab7db2aa07f4b44aca6fa1f87c8d77dad1` via `8ba54fed4ea85fe59f704b257a7eea9bb2b71e6c`. It adds `server/Migrations/20261107104114_HostLoadRetentionCatchup.cs`, whose `Down` throws `NotSupportedException` ("Retention catch-up state cannot be discarded by a downgrade."). It is on rc tip `74a8004f405bc54666fea33889839f0ff4d62ec7` and not on the previous cut `0e7aef0379a073c206e8398dcaed539bc33eaf9b`.

At the rc tip, `C467_V17_UpgradeAndLegacyEvidence` still calls `MigrateAsync` down to the migration before `AddDurableLandDelivery` on a database that is already current. That walk executes `HostLoadRetentionCatchup.Down`. Linux at the cut: 12 total, 11 passed, 1 failed, 0 skipped. Failure: `NotSupportedException: Retention catch-up state cannot be discarded by a downgrade.` at `HostLoadRetentionCatchup.Down` line 42, called from the test at line 103.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `8da334c5` (round 1, source `0cc57cca7dcb6bc506bfecbde9f22638fe92e308`) is Interim under a commissioned Final profile. It predates the catch-up migration. Migration rows were CP-M1 `/*/*/C1514HostLoadMigrationTests*/*` (6/6) and CP-M2 `/*/*/C0824RefOnlySnapshotMigrationTests*/*` (4/4). The Unit lane hit its row deadline and did not complete.
- Review `d56a0975` (round 2, subject `6e21459f78b7697976beafee49df60934531515b`, which contains the catch-up) records `ordinaryScopeCompleted: Interim`. It ran CP-3 `/*/*/(C1514HostLoadMigrationTests*)|(C0824RefOnlySnapshotMigrationTests*)/*` (12/12). The round was not clean (sampler tick p95, D-1), so it did not approve the land.
- Review `a28cac94` (round 3, test source `e62f9f0ca18be1a6a917d934ada7f9280f705383`) records `ordinaryScopeCompleted: Interim` and is clean for the D-1 diff only. The diff does not touch a migration. The selection was CP-7 `/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/(Linux_*)|(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*)` (7/7).
- Review `c7a30141` (master integration, subject `1705a3ee8c8102562addd96f65fe4c8c94e34ced`) is CLEAN with `reviewedSourceClean` true. It reran the test-design CP-1, CP-2, CP-3, and CP-7 rows plus completion CP-M1 `/*/*/C1514HostLoadMigrationTests*/*` (8/8). It states the whole Unit lane was never part of this card's receipts. Code's earlier CP-M2 receipt for `C0824RefOnlySnapshotMigrationTests` was not rerun in that round.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, category `Integration`, path `tests/Antiphon.Tests/Application/AgentTaskLandNotificationPersistenceTests.cs`.

The closed migration packet is `C1514HostLoadMigrationTests` and `C0824RefOnlySnapshotMigrationTests`. The plan repaired `C0824` because a new refusing `Down` breaks a historical downgrade that starts from the newest template. `C467_V17` is the other caller of that downgrade, and the introducing commit does not touch its file, so a touched-class selection does not name it. The deferred Unit lane would not have run it either: the class is `[Category("Integration")]`.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Migrations/*.cs` -> `/*/*/AgentTaskLandNotificationPersistenceTests/*`

Any migration after `AddDurableLandDelivery` whose `Down` throws fails `C467_V17_UpgradeAndLegacyEvidence`. The method filter is `/*/*/AgentTaskLandNotificationPersistenceTests/C467_V17_UpgradeAndLegacyEvidence*`.

## Exemption

same cause as an already written note: none

## Already on master

Test-only repair `e6132c64d0f6f2e86bedca483403ef2fd7301e39` (cherry-pick of `72771947e0b5188223b484515063dc6ea0554927` on `feat/card-task-114129b4`) builds the legacy subject from the pre-durable-land schema and leaves the `Down` refusal in place. Linux at master `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`: the same class filter, 12 passed, 0 failed, 0 skipped. No assertion was loosened.
