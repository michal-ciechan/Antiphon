# Why missed: post-land early warning on Windows group 130

One committed note for rc run 6591e378, chunk antiphon-all-group-130, rc/2026-11-15 =
`ac6d00022e940ee3b90584c7d313567b4491c498`. Windows reported `PostLandEarlyWarningTests`
14 failed in `Antiphon.Tests`. The sibling extract is
`docs/investigations/2026-11-10-run-6591e378-post-land-early-warning-g130-windows-failure.md`
on `origin/feat/card-task-35446c0f` at `912617f3889e362db05d42986d1bd924e11fe5f0`. CARD-1781.

## Failure

Eleven methods threw `UnauthorizedAccessException` from `Fixture.Dispose` while deleting a
git fixture. Git for Windows marks the loose object read-only. `Directory.Delete` then
fails on that object. The leaf in the Windows message was
`892e7e5ca82d5b42ef2fc9fb4e1d8494d0a9a7`. Eight of those methods had already finished
their assertions. The three `Real_slot_*` methods threw inside `RunRealSlotAsync` before
the caller's assertions ran.

Three methods returned the wrong state. `ConvertFrom-Json` turns the ISO `startedAt`
into a `DateTime`. `[string]` of that value is the invariant `MM/dd/yyyy HH:mm:ss` form,
and `[datetime]::Parse` then uses the current culture. On en-GB, `11/10/2026` is 11
October, so a stamp 30 minutes old is about 30 days old. `Test-PostLandHourly` returns
false, and `Test-PostLandPidReused` treats a live pid as reused. A fresh lock is
classified stale. `Start_inside_sixty_minutes_is_hourly` got `run` instead of
`skipped-hourly`. `Code_range_plans_namespace_shards_and_one_lane` got `run` instead of
`skipped-busy`. `Stale_lock_reclaim_is_exclusive` acquired twice (`acquired 261384 |
acquired 78040`).

This is not a timeout and not an expected Windows skip. Linux `Directory.Delete` ignores
a read-only file, and this runner's default culture is invariant, so the class was green
here without a seam. The red witness at `d8333499a828f66383fbf74446408f9f93367573` sets
`POST_LAND_WARNING_CULTURE=en-GB` and marks the loose object and its parent directory
read-only. That run was 4 failed, 0 succeeded:
`Start_inside_sixty_minutes_is_hourly` (`run` vs `skipped-hourly`),
`Code_range_plans_namespace_shards_and_one_lane` (`run` vs `skipped-busy`),
`Stale_lock_reclaim_is_exclusive` (`acquired 23714 | acquired 23715`), and
`Readonly_git_object_disposes` (`Access to the path
'/tmp/post-land-git-HvJgdB/.git/objects/ee/8c1ee49b4799bbd170233915a897c19e3b55e1' is denied`).

The fix reads the raw ISO `startedAt` token with invariant `RoundtripKind` and treats an
unspecified kind as UTC. Fixture dispose clears the read-only bit, then deletes, via
`ReadOnlyDirectory.Delete`. `tests/impacted-classes.json` is pinned by
`ImpactedClassesSelectorTests` and is not edited here. No row was added to
`tests/expected-platform-skips.json`.

## Introducing commit/card

introducing commit/card: `549cce24fa463a73f8a33a82e86b536b9affe82e` (CARD-1784) for
`Fixture.Dispose` calling `Directory.Delete`; `b0d0378d4cd4771596edda0a1fb1470e10f16a37`
(CARD-1784) for `Test-PostLandHourly`'s `[string]` plus `[datetime]::Parse`;
`da4f168de5a771adf5dab22336826d19b4ec613b` (CARD-1784) for the same parse in
`Test-PostLandPidReused`, which is what makes a fresh live lock look stale.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `c270da97-c8b6-4017-9ac3-419c18a474d2`, scope Full, reviewed source
  `6fa1d709ecbbe1991404dbecbbf14d6a21930c72`. CP-1 checkpoint run
  `20261109-233605-966c` ran `/*/*/PostLandEarlyWarningTests/*` at 20/20. The guard batch
  on a scratch merge onto master `e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab` was 78/78,
  including `TestLaneCategoryGuardTests` and
  `TestClassificationPolicyTests` / `TestClassificationGuardTests` /
  `SlowTestRegistryMergeTests` / `SlowTestTripwireTests` /
  `NativeLinuxPolicyWitnessTests`. `check-os-witnesses` exited 0. The review goal said
  nothing runs on the desktop.

Rounds 1 and 2 (`968281fc-ccb7-45ff-90cc-fd0ef35d211f`,
`dfecf4e4-5058-40d8-a7fd-cd1dec7d1c5a`) recorded Found and did not approve.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the methods are
`[Category("Unit")]` in `tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs` and
were inside the CP-1 class filter. They passed on that Linux checkpoint. The Windows
Full lane that failed them is native desktop group 130. That host's git marks loose
objects read-only, and its culture is en-GB. The approving selection never ran there.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/post-land-early-warning.ps1`, `scripts/lib/post-land-early-warning.ps1`, `tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs` -> `/*/*/PostLandEarlyWarningTests/*`

Proposed row id `post-land-early-warning`, not applied. A Linux run of that filter catches
this cause once the en-GB culture seam and the read-only dispose witness are in the class.
Those are in this fix. A Windows pre-land run is not required for this cause.

## Exemption

same cause as an already written note: none

The first note for this cause records `none`. `docs/investigations/2026-11-09-why-missed-landing-split-budget.md` is a different group-130 cause.
