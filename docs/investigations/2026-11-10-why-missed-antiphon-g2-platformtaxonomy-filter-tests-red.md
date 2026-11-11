# Why missed: antiphon g2 PlatformTaxonomy filter tests red

rc run `d88de419` on rc/2026-11-16 (`6f8741d872d63507827014f972ccf71196722d4a`), Windows chunk
`antiphon-all-group-2`: `PlatformTaxonomyFilterTests` 2 failed and `PlatformTaxonomyClassFilterTests`
1 failed. Each failure is the lane assertion
`Environment.GetEnvironmentVariable("ANTIPHON_PLATFORM_FILTER").ShouldBe(...)` reading `null`.

Classification: TEST FIXTURE DEFECT, not an OS difference and not a taxonomy defect. Only the
CARD-1832 plan's CP-1 (`include`) and CP-2 (`exclude`) rows set the variable. Every ordinary run
(rc Full, nightly, impacted selection, developer) leaves it unset. Linux reproduces it at the cut
with the variable unset: `/*/*/(PlatformTaxonomyFilterTests*)|(PlatformTaxonomyClassFilterTests*)/*`
total 3, failed 3. The Windows lane runs the two `LinuxOnly` methods only because its
`[Platform!=LinuxOnly]` suffix is S5, which is not in the cut. After S5 the unannotated sibling
would still run on both lanes and still fail.

Fix on `feat/card-task-076f7110`: the lane assertion applies only when the variable is set. A
set value must still match, so the V-2 witness keeps its red. Under the CP-1 filter with
`exclude`, 2 of 2 fail. Under the CP-2 filter with `include`, 1 of 1 fails. With an unfiltered
selection and `include`, the sibling fails (1 of 3). The attribute-shape assertions still run
unconditionally. No attribute, `Derive`, lane filter or policy file changed.

## Introducing commit/card

introducing commit/card: 387397c60c885a048cbf68e44d39ea40f4e44192 (CARD-1832). It is the rc pick of
`b343feb9cb510b53a7dc136ae3f4a794e01d7234`, the S1 red witness for platform filter selection.
`acaa9d9b1ef3ab32c5f7776913aa964bdbe70550` (S2) is the brief's handle for the card in the cut. It
added `Derive` only and did not touch the failing classes.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 9308b48d (`77f9e2f646db1ed08f06747223b1ceb8eb77344f`, S1+S2 clean at `22e52129be63a576ab4f7ff702767f160ab8e02c`).
  Scope: Full. Selection:
  - CP-1 `/*/*/(PlatformTaxonomyFilterTests*)|(PlatformTaxonomyClassFilterTests*)/*[Platform=LinuxOnly]` with `ANTIPHON_PLATFORM_FILTER=include`: 2/2.
  - CP-2 `/*/*/PlatformTaxonomyFilterTests*/*[Platform!=LinuxOnly]` with `exclude`: 1/1.
  - CP-3 `/*/*/PlatformTaxonomyDerivationTests/*`: 7/7.
  - Impacted `LandGraphAuditProjectInstanceTests`: 1/1.
  - Guard union: 78/78. It covered TestClassificationPolicy, MechanicalPcContract, C1729LayeringGuard, TaskPlatformGuidance, SlowTestRegistryMerge, NativeLinuxPolicyWitness, SlowTestTripwire, TestClassificationGuard and TestLaneCategoryGuard.
  - V-1, V-2 and V-3 mutants under the CP rows.

## Outside the selection

why the failing test was outside that selection (lane/category/path): lane = Linux checkpoint rows only;
category = `Unit`; path = `tests/Antiphon.Tests/PlatformTaxonomyFilterTests.cs` and
`tests/Antiphon.Tests/PlatformTaxonomyClassFilterTests.cs`. The classes were inside the selection
by name, but only inside an environment that set `ANTIPHON_PLATFORM_FILTER`. Every row that ran
them was a checkpoint row with an `Environment` cell. No plan row, Review run or impacted selection
ran them the way every Full, nightly and developer run does, with the variable unset. Review
proved the designed red under the wrong filter and never the green with no filter lane. No whole-assembly
`Antiphon.Tests` run of the CARD-1832 picks happened before the cut, so the rc Windows Full run was
the first unfiltered execution.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/PlatformTaxonomy*FilterTests.cs` -> `/*/*/(PlatformTaxonomyFilterTests*)|(PlatformTaxonomyClassFilterTests*)/*`

Impacted rows run with no `Environment` cell, so this row is the unset-variable run: 3 executed,
0 failed, in under 1 s of test time on the existing `Antiphon.Tests` build. More generally, a
witness whose body reads harness environment needs one plan row that runs it with that
environment unset.

## Exemption

same cause as an already written note: none.
