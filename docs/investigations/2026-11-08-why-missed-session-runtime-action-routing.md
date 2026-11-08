# Why SessionRuntimeActionRoutingTests missed the rc/2026-11-10 run

Qualification run e737e9fe (group 65, one failure in `SessionRuntimeActionRoutingTests`) ran rc/2026-11-10 at `74a8004f405bc54666fea33889839f0ff4d62ec7`. The Windows TRX and tests log under `C:\Antiphon\releases\candidates\rc-2026-11-10-74a8004f405b\logs\2026-11-08-0144-e737e9fe\` are not readable from this runner. Linux reproduced the class at that SHA and at master `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`.

## Failure

At `74a8004f405b` the class was 35 executed, 34 passed, 1 failed, 0 skipped (2m 12s). The only failure is `C1259_review_recovery_uses_authorized_queue_identity_once(legacy-upgrade)` (365 ms):

```
NotSupportedException: Retention catch-up state cannot be discarded by a downgrade.
  at HostLoadRetentionCatchup.Down
     server/Migrations/20261107104114_HostLoadRetentionCatchup.cs:42
  at SessionRuntimeActionRoutingTests.C1259_review_recovery_uses_authorized_queue_identity_once
     tests/Antiphon.Tests/Application/SessionRuntimeActionRoutingTests.cs:334
```

Line 334 on that SHA is `MigrateAsync(previous)` after `CreateIsolatedSchemaAsync()`. `previous` is the migration before `20261015181237_SessionRuntimeActions`, so the rewind walks every later `Down`. The newest host-load `Down` throws before that predecessor is reached. The same class passes on master: 35/35 in 2m 42s. The failure is deterministic on Linux, so it is not a Windows-only timing miss and not a flake.

## Introducing commit

CARD-1514. The stack that fires on the rc tip is `c53a0fe802a147da9f2dc5ae1579e92438aea60d` (`20261107104114_HostLoadRetentionCatchup`, Down throws `NotSupportedException`). Two earlier host-load migrations on the same cut throw from `Down` as well, so the fixture was already invalid at the first refusal:

| Commit | Migration | Down |
|---|---|---|
| `ff7cceaf4f65bed039f5b7f11dd726d27e762607` | `20261107034430_HostLoadTelemetryS1` | throws |
| `4ab2687899f99d8226dec8853661c155f1e9363c` | `20261107040142_HostLoadSnapshotMembership` | throws |
| `c53a0fe802a147da9f2dc5ae1579e92438aea60d` | `20261107104114_HostLoadRetentionCatchup` | throws; this is the exception the test hits |

The refusal is the intended product behavior. The missed defect is the historical fixture that still clones the tip and migrates backward.

## Reviews that approved it

All three CARD-1514 S1 reviews recorded `ordinaryScopeCompleted: Interim`. None selected `SessionRuntimeActionRoutingTests`.

| Round | Task | Report commit | Reviewed SHA | Scope | Migration selection they ran |
|---|---|---|---|---|---|
| 1 | `8da334c5` | `f50c0b425fb4592eb92a30b98e46d27db94a7703` (`docs/investigations/2026-11-07-review-load-telemetry-s1-r1.md`) | `0cc57cca7dcb6bc506bfecbde9f22638fe92e308` | Interim. The report says the completed scope is only Interim under the Final profile. Unit hit the 45-minute deadline with no TRX. | CP-M1 `/*/*/C1514HostLoadMigrationTests*/*` 6/6. CP-M2 `/*/*/C0824RefOnlySnapshotMigrationTests*/*` 4/4 |
| 2 | `d56a0975` | `667071fcf003ce089374806755ced7ef710c9a76` (`.antiphon/task-d56a0975.md`) | `6e21459f78b7697976beafee49df60934531515b` | Interim. The report states no Unit lane. | CP-3 `/*/*/(C1514HostLoadMigrationTests*)\|(C0824RefOnlySnapshotMigrationTests*)/*` 12/12 |
| 3 | `a28cac94` | `41adfec8551e6fd095243979d12fbda5f30e6e5e` (`.antiphon/task-a28cac94.md`) | `036bbcba09178ded1e2319664f6df008305a6370` | Interim. Diff-only. | CP-7 native overhead only, 7/7. No migration class. |

Code's own completion plan (`docs/superpowers/plans/2026-11-07-card-1514-s1-verification-completion.md`) already knew that irreversible telemetry migrations invalidate fixtures which start a historical `Down` from the newest template. It named one adjacent class, `C0824RefOnlySnapshotMigrationTests`, ran it red, and repaired it. It did not name `SessionRuntimeActionRoutingTests`.

## Why this test was outside that selection

`SessionRuntimeActionRoutingTests` is `[Category("Integration")]` and `[Category("Slow")]` (`SessionRuntimeActionRoutingTests.cs`). It is not in the Unit lane, so the round-1 Unit deadline and the round-2 "no Unit lane" choice could not have executed it. The migration impact path was limited to the new host-load migration class plus the one historical fixture the plan named. `C1259_review_recovery_uses_authorized_queue_identity_once(legacy-upgrade)` still did `CreateIsolatedSchemaAsync()` then `MigrateAsync` back across `SessionRuntimeActions`, which walks the host-load `Down` methods.

The fixture repair landed later, on master only relative to this cut: `517b705036e1496388a562835c03d3e15b522370` (2026-11-08 05:04, cherry-pick of `5e6fefbe05af7971c216fa5f33fd530f561e6563`). For `legacy-upgrade` it starts the database at `MigrationTestDatabase.CreateReversibleTipAsync()` (`20261106032116_AddRefOnlyParkWorkspaceSnapshot`) instead of rewinding the tip. The rc tip `74a8004f40` is 2026-11-07 23:20. The qualification log folder is `2026-11-08-0144-e737e9fe`, before that repair. The class file is unchanged from the cut to master except that fixture commit.

## Cheapest pre-land check (CARD-1782 impacted-classes)

Add this path-to-class entry. A Unit category filter cannot see this class.

- Glob: `server/Migrations/*.cs`
- Filter: `/*/*/SessionRuntimeActionRoutingTests/C1259_review_recovery_uses_authorized_queue_identity_once*`

That one method is the historical rewind. It fails in a few hundred milliseconds once any later `Down` throws, which is what group 65 hit. The migrations kind's derive pattern `MigrateAsync\s*\(\s*[^)\s]` also matches this file; the static class list on the CARD-1782 table (`AppDbContextModelTests`, `C1514HostLoadMigrationTests`) does not name it. That table is not on master `3364197bf1` or on the rc cut.
