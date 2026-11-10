# Why missed: C0824 migration hook timeout

One committed note for the merge-batch hook timeout on
`C0824RefOnlySnapshotMigrationTests.Downgrade_refuses_recorded_snapshot_and_preserves_its_bytes`.
CARD-1781.

## Introducing commit/card

introducing commit/card: `e52383fc4fccac803fd521bcbbdbfa41e79a8013` removed
`[ParallelLimiter<ProcessSpawnLimit>]` from this class, and
`52e13fdca8dcb780ef4afd3ca9247bc32d28bfe4` added
`[NotInParallel(nameof(C0824RefOnlySnapshotMigrationTests))]` while leaving the hook's
60s cancel in place (CARD-1439, master batch 155). The task-branch originals are
`1eb65fb8a9bb97ce523f30b7f4d0feaa782594f0` and
`b693a327224eccf3ba38421abaa753cf74dcd98a`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No review task for this card is recorded in this checkout. Code task 118b1cfd saw three
  hook failures past 60000ms, then landed the class key and the limiter removal. The class
  filter after that change passes on its own (4/4 at load around 8 in the batch rerun, and
  4/4 on each of the five loaded runs below).

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was not outside
the class. Lane Integration, category Slow, path
`tests/Antiphon.Tests/Application/C0824RefOnlySnapshotMigrationTests.cs`. The key stops this
class overlapping itself. It does not change how long one `MigrateAsync` takes while the
rest of a 528-test batch is on the same host. A quiet class run stays under 60s, so the
selection Code ran could not show the batch tail.

## What the phases actually cost

Host: this server2 runner, 24 cores, 2026-11-10. Source before the budget change:
`52e13fdca8dcb780ef4afd3ca9247bc32d28bfe4`. Five back-to-back class runs plus one run beside
`C1514HostLoadMigrationTests` and `Card1619ExitMigrationIdTests`, all while four
`sha256sum /dev/zero` workers ran. Load at each start was 31.8, 30.3, 36.8, 43.3, 25.8,
and 25.1. Every run passed (class 4/4 each time; the combined run 15/15).

`MigrationTestDatabase.CreateAsync` records `db-create` around `CREATE DATABASE … TEMPLATE template0`
and `db-migrate` around the baseline `MigrateAsync`. Percentiles are linear.

| Phase | n | p50 | p95 | max |
|---|---:|---:|---:|---:|
| db-create | 18 | 59ms | 149ms | 169ms |
| db-migrate, park baseline (the failing method) | 12 | 9.2s | 17.1s | 17.1s |
| db-migrate, previous baseline (Additive) | 6 | 25.7s | 34.3s | 34.4s |
| db-cleanup | 18 | 48ms | 103ms | 176ms |

The parallel run did not stretch this class: Additive migrate 27.4s, park migrate 8.0s and
7.6s, create still under 100ms. CREATE DATABASE is not the cost, so restoring
`ProcessSpawnLimit` does not match the measurement and would undo CARD-1439. A shared
`NotInParallel` key with the two other classes also does not shorten the migrate. The hook
is one wait for `CreateAsync` to apply the baseline. Two walls were both 60s: the setup
`CancellationTokenSource`, and the class `[Timeout(60_000)]`, which TUnit 1.44 copies onto
hooks in the class (`TimeoutAttribute.OnHookRegistered`). 60s is 3.5x the park p95 and only
1.75x the longer baseline's p95. Both walls are now 70s, 2x that 34.3s p95. The batch kill
(park setup still running at 60s, load around 45) did not reappear in these 12 park samples,
including the run that started at load 43. 70s does not claim to cover a stall longer than 70s.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/**/C0824RefOnlySnapshotMigrationTests.cs` -> `/*/*/C0824RefOnlySnapshotMigrationTests/*`

Read `db-migrate` from `ANTIPHON_TEST_PHASE_ROOT` on that filter while the host is loaded.
A passing count with no phase line does not see this timeout.

## Exemption

same cause as an already written note: none
