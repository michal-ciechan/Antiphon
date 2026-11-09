# Why missed: raw-string fixture classes had no lane category

One committed note for a master red. Review 1c5142f8 (CARD-1507) reported
`TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` on a pristine-branch run.
Debug task 1c06e9f6 reproduced it on Linux at master `d861149d0738bef6019e1a437efc69d245512ab9`.
CARD-1781.

## Failure

`/*/*/TestLaneCategoryGuardTests/*` at that SHA: 1 run, 0 passed, 1 failed, 0 skipped.
`every_test_class_is_tagged_unit_xor_integration` (`tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs:53`)
reported five untagged classes, all raw-string fixtures inside `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`:

- `BareSharedTests`
- `ReadsRootTests`
- `OtherAssignmentTests`
- `ParameterOtherTests`
- `HostTests`

The guard is a line scan. It treats a `public sealed class` line inside a `"""` fixture as a class, then
keeps reading until the next such line. The next real `[Test]` falls in that window, so the fixture is
counted as a test class. None of the five lines had `[Category("Unit")]` or `[Category("Integration")]`.
They are not executable tests. `ProcessSpawnLimitTests` itself is already `[Category("Unit")]`.
`C1816OutsideFolderProbe` is `internal static` and is not in the failure.

The same scan of `ProcessSpawnLimitTests.cs` at the parent of `fabda59573` is 0 untagged. At the parent
of `c8fc4a413` (`324f66da355530a43b644b37a86c3c7719e0e6f1`) it already names `BareSharedTests`,
`ReadsRootTests`, and `HostTests`. CARD-1816 added the other two.

Fix (test fixtures plus this note and the guard-batch sentence): `[Category("Unit")]` on each of the five
fixture classes, the same lane as the real class in that file. They do not spawn a process, so they do not
gain a `ParallelLimiter`. The Roslyn census in `SharedCheckoutSourceCensus` does not read attributes.
The guard assertion is unchanged.

## Introducing commit/card

introducing commit/card: fabda595738775bfc98b4a487dce5543288e7883 (CARD-1810)

`test(CARD-1810): red witnesses for a shared-checkout class that loses both attributes` at 2026-11-09 14:32:28Z
added the raw-string classes `BareSharedTests`, `ReadsRootTests`, and `HostTests`. That is the first commit
on this master whose `ProcessSpawnLimitTests.cs` the guard scan marks untagged.

CARD-1816 widened it. `c8fc4a413cafc76f5e52c2262ba9419794f196ad` at 2026-11-09 15:46:40Z
(`test(card-1816): red witnesses for RepoRoot assignment, helper calls, and namespace scope`) added
`OtherAssignmentTests` and `ParameterOtherTests`. The green follow-up
`97d4c6708652617f113d043ad92738795b0ffe5b` changes the census and does not add a lane category.
Review 1c5142f8 names that pair, on master `97d4c6708`, as the land it saw. Both SHAs are ancestors of
`d861149d0738bef6019e1a437efc69d245512ab9`.

CARD-1816 landed WITHOUT a separate Review. The pair is a test-only red then green, cherry-picked onto
master. The checkout history after `ca952f100c` (`chore(CARD-1792): reseal policyHash ... on batch 143 master`)
is linear. There is no merge commit and no Review selection for CARD-1816 stored in this tree.
The Merge's guard batch did not include `TestLaneCategoryGuardTests`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- none for CARD-1810 or CARD-1816. No Review task, scope, or selection for either card is in this checkout.
- CARD-1810's own commit message records the selection it ran: `/*/*/(ProcessSpawnLimitTests*)/*`,
  52 total, 5 failed, 47 passed, 0 skipped. That selection does not include `TestLaneCategoryGuardTests`.
- Review 1c5142f8 (CARD-1507) is the report that found this red on a pristine-branch run. It is not an
  approving review of the introducing commit. This Debug did not re-read that review's checkpoint list;
  the dispatch states that the Merge guard batch omitted `TestLaneCategoryGuardTests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit lane, `Unit`,
`tests/Antiphon.Tests/TestHelpers/TestLaneCategoryGuardTests.cs`

Every Code brief and every Merge brief of this session names this eight-class union and does not name
`TestLaneCategoryGuardTests`:

`/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`

That union is not a constant in `server/Bundles/stage-code.md`, `server/Bundles/orchestrator.md`, or a
script. The task-facing sentence those briefs grow from is "batched guards (policy sweep, mechanical
contract, TaskPlatformGuidanceTests, layering rows)" in `docs/testing-and-build.md`,
`docs/orchestration-loop.md`, `TESTING.md`, `docs/release-gates.md`, and
`server/Application/Core/DelegationReportFormatter.cs`. It does not list the eight classes and it does
not name `TestLaneCategoryGuardTests`. Plan checkpoint rows copy the eight names
(for example `docs/superpowers/plans/2026-11-09-card-1802-complete-report-never-blocks-on-lease.md` CP-11
and `docs/superpowers/plans/2026-11-08-card-1790-pwsh-private-cache-plan.md` CP-2).

The Merge process fixed high-radius subset (`docs/orchestration-loop.md`, CARD-1783) is a different list
(`TestTimingOperationLabelTests`, `AppDbContextModelTests`, `ContractSnapshotTests`,
`CheckpointReceiptSafetyTests`, `HostCleanupCheckpointReceiptTests`, and the CARD-1801 land family).
It also omits `TestLaneCategoryGuardTests`.

The class is Unit. Per-task Final does not run the whole Unit lane; nightly and rc qualification do.
From `fabda59573` at 14:32Z to this Debug at 18:09Z (about three and a half hours, not a separate
twelve-hour window on this master) no land-path selection named the class, so nothing caught the red.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/**/*.cs` -> `/*/*/TestLaneCategoryGuardTests/*`

As a CARD-1801 `tests/impacted-classes.json` kind, not applied here because
`ImpactedClassesSelectorTests` pins the table:

```json
{
  "id": "test-lane-category",
  "globs": [
    "tests/Antiphon.Tests/**/*.cs"
  ],
  "classes": [
    "TestLaneCategoryGuardTests"
  ]
}
```

Any edit under the tree the guard scans selects the one census test. After a build that test was 5.5 s
on this host, including startup. The canonical guard batch now stated in
`docs/testing-and-build.md` (Default Code/Review recipe) and pointed at from the Merge process is the
eight-class union plus `TestLaneCategoryGuardTests*`.

## Exemption

same cause as an already written note: none

`docs/investigations/2026-11-09-why-missed-write-behind-category.md` is a different cause: a real class
missing `[Category("Unit")]`, reported by `TestClassificationMetadata` as `lane-xor`. This failure is the
line scan reading raw-string fixtures, and the session guard batch omitting `TestLaneCategoryGuardTests`.
