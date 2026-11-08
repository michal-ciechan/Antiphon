# Why StandingRestartEscalationTests missed rc/2026-11-10

Debug task 5358bac5. Qualification run e737e9fe, group-72, rc cut
`74a8004f405bc54666fea33889839f0ff4d62ec7`. The Windows TRX and test log
were not mounted on this runner. A leased Linux run of the class at that
SHA produced 52 total, 3 failed, 49 passed, 0 skipped. The failures are
`C510_migration_starts_without_invented_history` arguments 0, 3, and 500.
Each throws `NotSupportedException: Retention catch-up state cannot be
discarded by a downgrade` from `HostLoadRetentionCatchup.Down`
(`server/Migrations/20261107104114_HostLoadRetentionCatchup.cs:42`),
reached by `MigrateAsync` at
`StandingRestartEscalationTests.Boundaries.cs:243`.

The same class at master `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` is
52 passed, 0 failed. The fixture repair is already on master. This note
changes no test and no product code.

## Introducing change

CARD-1514 host-load telemetry. Commit
`ff7cceaf4f65bed039f5b7f11dd726d27e762607` (2026-11-07 22:39:00 +0000),
cherry-pick of `f131c46a1ad28676d3a34284ac2d05a5d1c101c0` (2026-11-07
03:45:19 +0000) and `d8e139fe6b2ad1755c6d06b2a536df5ea012ce3c`. Message:
`feat(host-load): generate additive telemetry migration with safe
downgrade refusal`. It is an ancestor of the rc cut and is not an
ancestor of the previous cut `0e7aef0379a073c206e8398dcaed539bc33eaf9b`. `HostLoadTelemetryS1.Down`,
`HostLoadSnapshotMembership.Down`, and `HostLoadRetentionCatchup.Down`
throw `NotSupportedException` so a downgrade cannot discard durable rows.

`C510_migration_starts_without_invented_history` clones the fully
migrated tip with `TestDbFixture.CreateIsolatedSchemaAsync`, then calls
`IMigrator.MigrateAsync` for the migration before
`AddStandingAutomaticFreshRecovery`. EF walks `Down` from the tip and
hits the retention catch-up refusal. Standing-restart product services
do not explain the red: `AgentSupervisorService`,
`StandingRestartEscalationPolicy`, and `AgentControlService` have no
commits from `0e7aef037` to origin/master. The only Boundaries.cs change
between the rc cut and master is the fixture line in `517b705036`.

## Reviews that approved the land

Plan `docs/superpowers/plans/2026-11-07-card-1514-s1-verification-completion.md`
selects historical Down as two classes. CP-M1 is
`/*/*/C1514HostLoadMigrationTests*/*`. CP-OLD and CP-M2 are
`/*/*/C0824RefOnlySnapshotMigrationTests*/*`. The plan records that new
irreversible telemetry migrations invalidate a fixture that starts a
historical Down from the newest template, and it repairs that one class.
`StandingRestartEscalationTests` is absent from the checkpoint table.
CP-G is the named guard union (76). The Unit lane is deferred.

Land-approving reviews, both `ordinaryScopeCompleted: Interim`:

- Round 3 `a28cac94-c7cb-499f-8f0f-c0b05e5461e4`, subject
  `924f8ae2-e285-400d-8efe-fcd1a16a9ced`, reviewedSourceSha
  `036bbcba09178ded1e2319664f6df008305a6370`, reviewedSourceClean true,
  range `6e21459f7..036bbcba`. Next: land. The rerun was CP-7 only
  (7 passed, 0 failed). That round's diff touched no migration. Unit,
  CP-8, and Windows acceptance were left unrun. PC-1..102 stayed pending.
- Integration review `c7a30141-b0f6-4a0a-9e74-2a051ad0e8e7`, subject
  `2b953d03`, reviewedSourceSha
  `1705a3ee8c8102562addd96f65fe4c8c94e34ced`, reviewedSourceClean true,
  reviewRound 3, range `036bbcba..1705a3ee`. Next: land. Rerun: CP-1
  18/18, CP-2 35/35, CP-3 3/3, CP-7 7/7, CP-M1 8/8
  (`C1514HostLoadMigrationTests`). CP-4, CP-5, CP-6, CP-M2, and CP-G
  were left on earlier Code receipts. The Unit lane remained owed.

Round 1 `8da334c5-0568-4f17-89ad-613169da7f35` (Interim, 265 passed and
1 failed, Unit timed out with no TRX) and round 2
`d56a0975-41cb-4814-b8b8-9fef68fd2d63` (Interim) sent the work back to
code. They did not approve the land.

## Why this test was outside that selection

`StandingRestartEscalationTests` is `[Category("Integration")]`,
`[Category("Slow")]`, and `[NotInParallel]`, and it is allowlisted in
`tests/Antiphon.Tests/slow-tests-allowlist.txt`. It sits outside the
Unit lane those reviews left owed. The method lives in
`tests/Antiphon.Tests/Application/StandingRestartEscalationTests.Boundaries.cs`.
The historical-Down filters name `C1514HostLoadMigrationTests` and
`C0824RefOnlySnapshotMigrationTests`. This method calls
`GetService<IMigrator>()` and `MigrateAsync(migrations[index - 1])`
after `CreateIsolatedSchemaAsync`, the same shape the plan repaired for
C0824.

Debug `114129b4-1293-465e-8917-dceed0d2ef58` later reproduced the same
`HostLoadRetentionCatchup.Down` throw on the rc SHA for
`C467_V17_UpgradeAndLegacyEvidence` and fixed that test in
`72771947e0b5188223b484515063dc6ea0554927`. Its green selection was
`TestTimingOperationLabelTests` plus
`AgentTaskLandNotificationPersistenceTests` (14/0),
`C1514HostLoadMigrationTests` plus
`C0824RefOnlySnapshotMigrationTests` plus `AppDbContextModelTests`
(15/0), and the guard batch of the policy sweep,
`MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, and
`C1729LayeringGuardTests` (75/0). `StandingRestartEscalationTests` was
outside that selection. `72771947` is not an ancestor of origin/master.

Debug `5a1ec910-f557-4cd5-8dc3-188ab0fc465b` then swept downgrade
callers. Red at `6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa` (Boundaries.cs
and the catch-up migration identical to the rc cut): 368 tests, 52
failed, 316 passed. Every failure was the same `NotSupportedException`,
including these three argument rows. Fix
`5e6fefbe05af7971c216fa5f33fd530f561e6563` landed on master after the rc
cut as `517b705036e1496388a562835c03d3e15b522370`. That commit replaces
`CreateIsolatedSchemaAsync` with
`MigrationTestDatabase.CreateReversibleTipAsync` in this method and
keeps the assertions. The qualification ran the unfixed fixture.

## Cheapest pre-land check (CARD-1782)

Migration kind glob: `server/Migrations/**` (the kind also lists
`server/Infrastructure/Data/AppDbContext.cs` and
`server/Infrastructure/Data/**/*Model.cs`). The static class list is
`AppDbContextModelTests` and `C1514HostLoadMigrationTests`. Downgrade
callers are found by scanning `tests/**/*.cs` for `MigrateAsync(<target>)`,
`IMigrator`, `MigrationTestDatabase`, and `.Down(`. Boundaries.cs matches
`IMigrator` and `MigrateAsync(<arg>)`. A whole class wins over a method
subset, so the selector entry is:

- glob: `server/Migrations/**`
- filter: `/*/*/StandingRestartEscalationTests/*`

The three failing rows alone are
`/*/*/StandingRestartEscalationTests/C510_migration_starts_without_invented_history*`.

`tests/impacted-classes.json` was not in the tree when the host-load
reviews approved land. Once the migration glob matches, that class
filter is the check that includes this fixture.
