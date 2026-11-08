# Why missed: standing-specialist, worktree-base, and runner-default rewinds

rc qualification run `e737e9fe` on `rc/2026-11-10` (`74a8004f405bc54666fea33889839f0ff4d62ec7`) failed three Integration tests. The Windows TRX directory `C:\Antiphon\releases\candidates\rc-2026-11-10-74a8004f405b\logs\2026-11-08-0144-e737e9fe\` is not mounted on this runner. The same three methods failed on Linux at that commit and passed on master.

## Failure

Each failure is `NotSupportedException: Retention catch-up state cannot be discarded by a downgrade.` thrown from `HostLoadRetentionCatchup.Down` (`server/Migrations/20261107104114_HostLoadRetentionCatchup.cs:42`). EF runs that Down first because the fixture clones the tip and then migrates backward.

| Test | Call |
|---|---|
| `StandingSpecialistRoutingMigrationTests.Card0415_V25_real_upgrade_preserves_primary_tasks_and_holds_without_fabricating_qualification` | `MigrateAsync` to the migration before `AddSpecialistExecutionIdentity` (`StandingSpecialistRoutingMigrationTests.cs:42` at the rc cut). Group 74. |
| `WorktreeBaseSelectionTests.C508_BaseFieldsUpgradeAndRoundTrip` | `MigrateAsync` to the migration before `AddWorktreeBaseSelection` (`WorktreeBaseSelectionTests.cs:357` at the rc cut). Group 74. |
| `RunnerDefaultMigrationTests.Existing_schema_upgrades_and_legacy_remote_imports_once` | `RewindToPlatformMigration` (`RunnerDefaultTests.cs:615` at the rc cut). Group 75. |

Linux reproduction at `74a8004f405bc54666fea33889839f0ff4d62ec7`, filter `/*/*/(StandingSpecialistRoutingMigrationTests*)|(WorktreeBaseSelectionTests*)|(RunnerDefaultMigrationTests*)/*`: 25 total, 3 failed, 22 passed, 0 skipped, 45s, exit 2. Same filter at master `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`: 25 passed, 0 failed, 0 skipped, 1m 03s, exit 0.

## Classification

Fixture defect, not a product bug, not Windows-only, and not flaky. CARD-1514 load telemetry S1 made downgrade throw on purpose. The first throwing Down is `ff7cceaf4f65bed039f5b7f11dd726d27e762607` (`HostLoadTelemetryS1`). Membership followed in `4ab2687899f99d8226dec8853661c155f1e9363c`. The exception these three tests hit is the latest Down, added by `c53a0fe802a147da9f2dc5ae1579e92438aea60d` (`HostLoadRetentionCatchup`). All three commits are on the rc cut. The tests still cloned the tip and called `IMigrator.MigrateAsync` toward an older id, so the new Down ran.

Master already repairs the fixtures in `517b705036e1496388a562835c03d3e15b522370` (`test: build host-load downgrade fixtures forward`). That commit is an ancestor of `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`. It starts the database at the historical migration, or at `20261106032116_AddRefOnlyParkWorkspaceSnapshot`, and walks forward. The assertions are unchanged. No further product or test edit is required for this chunk.

## Why review missed it

Card: CARD-1514, load telemetry S1. Code subject `924f8ae2-e285-400d-8efe-fcd1a16a9ced`. The three classes are `[Category("Integration")]`. `WorktreeBaseSelectionTests` is also `Slow`. A Unit-lane filter cannot select them.

The reviews that approved the migration ran only the new migration classes:

| Review | Commissioned | Ordinary scope | Migration selection actually run |
|---|---|---|---|
| `8da334c5-0568-4f17-89ad-613169da7f35` round 1, outcome Found, source `0cc57cca7dcb6bc506bfecbde9f22638fe92e308` | Final | Interim | CP-M1 `/*/*/C1514HostLoadMigrationTests*/*` 6/6 and CP-M2 `/*/*/C0824RefOnlySnapshotMigrationTests*/*` 4/4. Report `f50c0b425fb4592eb92a30b98e46d27db94a7703`. |
| `d56a0975-41cb-4814-b8b8-9fef68fd2d63` round 2, outcome Found, source `6e21459f78b7697976beafee49df60934531515b` | Final | Interim | CP-3 `/*/*/(C1514HostLoadMigrationTests*)\|(C0824RefOnlySnapshotMigrationTests*)/*` 12/12. Report `667071fcf003ce089374806755ced7ef710c9a76`. |
| `a28cac94-c7cb-499f-8f0f-c0b05e5461e4` round 3, outcome Clean, source `036bbcba09178ded1e2319664f6df008305a6370` | Final | Interim | CP-7 native overhead only, 7/7. No migration rewind. Report `41adfec8551e6fd095243979d12fbda5f30e6e5e`. |
| `c7a30141-b0f6-4a0a-9e74-2a051ad0e8e7` integration diff, outcome Clean, tip `1705a3ee8c8102562addd96f65fe4c8c94e34ced` | Final | Unknown | CP-M1 `C1514HostLoadMigrationTests` 8/8. CP-M2 was explicitly not rerun. Report `436e0d35924b588e398344202798052080474f39`. |

`C1514HostLoadMigrationTests` asserts that these Downs throw. That is the opposite of running the older callers that still expected Down to succeed. Those callers live in other files, so a "touched class plus named guard" selection never opened them.

## Cheapest pre-land check (CARD-1782)

Glob: `server/Migrations/**`

Filter: `/*/*/(StandingSpecialistRoutingMigrationTests*)|(WorktreeBaseSelectionTests*)|(RunnerDefaultMigrationTests*)/*`

That is the CARD-0403 combined class filter for this chunk. It is Integration, so it must not be narrowed with `[Category=Unit]`. On this host it was 25 cases and about one minute once the assembly was built. CARD-1782's broader migrations row (every class that calls `MigrateAsync` with a target, `IMigrator`, or `MigrationTestDatabase`) includes these three and would have caught them too.
