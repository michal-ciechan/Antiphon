# Why missed: WriteBehindFileSystemTests had no lane category

One committed note for a master red found by Code task 3d4bf7c3 (CARD-1796), which would have stopped
the rc/2026-11-13 cut checks. CARD-1781.
Debug task b73aeb1d reproduced it on Linux at master `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa`.

## Failure

Guard batch `/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuard*)/*`
on `tests/Antiphon.Tests` at that SHA: 78 run, 69 passed, 9 failed, 0 skipped.
All nine failures report one violation from `TestClassificationMetadata` (`tests/Shared/TestClassificationMetadata.cs:227`):

```
lane-xor Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests
```

Failing methods: `TestClassificationGuardTests.Registry_matches_compiled_metadata`, `TestClassificationPolicyTests.C487_G068`,
`C1383_Rebase_preserves_distinct_appended_pairs` (target-only, common-base), `C1383_Layout_opt_in_is_scoped(non-repository)`
and four `C1383_Valid_reason_pairs_are_accepted` cases.

Fix: `[Category("Unit")]` on the class. It drives a `MockFileSystem`, plus one small temp-directory parity check against
`System.IO`, the same shape as other Unit classes such as `TimeoutAuditTests`. No policy or registry row was needed.
After the fix the same batch is 78/78 and the class is 8/8.

## Introducing commit/card

introducing commit/card: 6a371bc38888a77325fbdc24e55e61850ed05e68 (no card)

`feat(io): add Antiphon.IO.WriteBehind, a write-behind IFileSystem decorator` added the class and compiled it into
`Antiphon.Tests` through a `<Compile Link=...>` item. The commit was pushed straight to master by the operator. It did not go
through Code, Review or a Merge batch, so no stage selection ran against it.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- none. The commit was a direct push with no Review task.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit lane, `Unit`, `tests/Shared/TestClassificationGuardTests.cs`
and `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs`.

No selection ran before the push. The guard batch runs per task in Code/Review Final and in a Merge batch's final-tree run at land.
A direct push to master skips all of these. The guards are `Unit`, so the nightly Unit lane and rc qualification would only
catch the red after it was already on master.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/**/*.cs` -> `/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(TestLaneCategoryGuard*)/*`

For a direct push, which has no Merge, the cheapest gate is a pre-push check for `refs/heads/master` that runs the same guard
filter (about 2 minutes of tests after a build). Proposed card: "Pre-push guard batch for direct master pushes". It would add a
`scripts/hooks` pre-push entry, opt-in per checkout, that builds `tests/Antiphon.Tests` under a build slot and runs the
classification guard filter above. It refuses the push on any failure, and an explicit `--no-verify` stays the operator's override.
Running the batch earlier in the nightly would not prevent the red, because the nightly runs after the push.

## Exemption

same cause as an already written note: none
