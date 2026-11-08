# Why missed: LinuxOnly census rows (rc/2026-11-12 cut, A5)

Debug task da90742c. Merge task 5f346008 stopped the rc/2026-11-12 cut at check A5 on master
584224f0e. `pwsh -NoProfile -File scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy`
gave 5 passed, 1 failed. The failing row was `C1323 markers-and-rows-agree`.

## The defect

`Get-C1323LinuxOnlyCensus` (`scripts/test-rc-platform.ps1:588`) makes one key per class from the
`[NativeLinuxOnly]` markers: `suite|class|<sorted marked methods joined by commas>`. It makes one key
per prefixed rc policy row the same way, and the two key sets must be equal. So a class with several
Linux-only methods needs ONE row that lists all of them. CARD-1775 marked three methods in
`tests/Antiphon.Tests/Infrastructure/Server2ProductionServerContractTests.cs` (lines 246, 289, 447). It
gave each one its own one-method row in `tests/test-execution-policy.json`. The census then reported
one `marker-without-row` for the three-method key and three `row-without-marker` entries. The failure
is deterministic and does not depend on the platform.

Fix (master only, c9197e7f59): the three rows become one row that lists all three methods, with a
broader reason. `policyHash` was resealed from 3d54ece0... to 822a7dd1..., and `PinnedInputDigest`
was repinned from 9e79dffc... to b507aab8... (`test-live-lane-run.ps1 -PrintInputDigest`). After the
fix, A5 gave 6/6 and 62/62.

## Introducing commit/card

introducing commit/card: 986297da8546857622f62a647c9284ccf10299ee (CARD-1775,
"exclude the Linux deploy witnesses from the Windows lane", 2026-11-08 06:50 UTC). It added two
one-method rows (`WhatIf_prints_the_plan_and_calls_no_docker`, `Execute_refusals_stop_before_docker`),
and the census was already red there. 14c6025027 (the CARD-1775 round-2 repair, 07:51) added a third
one-method row for `Execute_drains_large_compose_output_without_echoing_a_secret`. b35fb137ac (12:22)
resealed the hash and the digest on the combined tree without changing the row shape.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 34664e17, round 1, Final/Full, subject 5f1bba8b3d (the pre-rebase form of 986297da85). The selection
  was the plan's CP-2 (`Server2ProductionServerContractTests*|Server2StagingServerContractTests*|DockerStackContractTests*`)
  and related rows. The verdict was NOT CLEAN on B-1 and B-2. Its A-6 noted that "the two Linux-only
  methods carry policy rows with reasons" and accepted the per-method shape.
- e278d330, round 2, Final/Full, diff only 5f1bba8b3d..37244c6a08. It ran checkpoint 20261108-081909-277f:
  CP-2 (138), CP-5 `DockerStackDocumentationTests` (18), CP-6 `LiveLaneRunnerTests` (4), CP-7
  `NativeLinuxPolicyWitnessTests` with six guard classes (45), and CP-8 `WindowsUnitPlatformAdmissionTests` (5).
  The verdict was CLEAN. Its check of the new row was "Policy row (platform windows, Linux-only reason)
  matches the `[NativeLinuxOnly]` attribute". That compares the reason with the attribute. It does not
  check the one-row-per-class grammar.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the A5 pair is
`scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` and `-Case C1686_LinuxOnlyCensus`.
Its compiled wrappers are `ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy` and
`.C1686_LinuxOnlyCensus` (`tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs`, Unit,
selection set `release-gate.platform`). Neither was in any CARD-1775 checkpoint row or review selection:

- The CARD-1744 cutover plan's checkpoint table (CP-1..CP-4) was written by hand around the compose
  and deploy contracts. It names no policy-census row, even though the slice edits
  `tests/test-execution-policy.json`.
- The rule that catches this already exists. `tests/test-selection-policy.json` rule
  `P-LINUX-ONLY-CENSUS` routes `tests/test-execution-policy.json` to `release-gate.platform`, which is
  `ReleaseGatePlatformTests`. No pre-land stage runs the change-aware selector's output. Code and
  Review run the plan's closed checkpoint list, and Merge runs its fixed subset. So the selector picked
  the right class, but only the rc cut actually ran that check.
- The compiled marker witness that Review did run, `NativeLinuxPolicyWitnessTests`, keys each row by
  `suite|class|methods`. It accepts any number of rows per class as long as each marked method is
  covered and no key is duplicated. That is why three one-method rows passed there. The one-row-per-class
  grammar lives only in the PowerShell census.
- Hash and digest reseals (b35fb137ac) check that the seal matches the content. They do not check the
  row shape.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/test-execution-policy.json` and any `*.cs` diff that adds or removes a `[NativeLinuxOnly(` line ->
`/*/*/ReleaseGatePlatformTests/(LinuxOnlyWindowsExclusionPolicy)|(C1686_LinuxOnlyCensus)`.
The no-build equivalent is
`pwsh -NoProfile -File scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy`
(about 26 s on server2, no build, no slot). The C1686 case adds 62 fixture rows in about the same time.

For the CARD-1783 fixed Merge subset, the proposal is to add the A5 pair as one cheap row: the two
`test-rc-platform.ps1` cases above, run without a build, failing on any FAIL line or a nonzero harness
exit. It is about 1 minute in total and needs no Postgres, Docker or slot. A Merge that ran it on
986297da85 would have stopped CARD-1775 there instead of at the rc cut.

## Exemption

same cause as an already written note: none
