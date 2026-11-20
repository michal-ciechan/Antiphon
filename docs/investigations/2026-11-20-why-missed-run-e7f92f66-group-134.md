# Why missed: run e7f92f66 group 134

One committed note for the two new causes in rc qualification run e7f92f66, chunk antiphon-all-group-134, on rc/2026-11-29 tip `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`. The other 450 tests in that chunk passed. Base master for the repair is `e0f381c48e840fb2ee614fd63571f8b26ca4a01d`.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

## Cause A: capped selector literals

`ImpactedClassesSelectorTests.C1801_why_missed_paths_select_their_classes_and_windows_filters` and `C1801_non_merge_land_notification_filter_names_every_class` pin two full alternation strings. On master and on the rc tip the selector's kept window for `server/Application/Services/AgentTaskLandNotificationService.cs` contains `LinkedUnattemptedQueueWakeTests` and not `C0747LandNoteExpireEndpointTests`. Every other pinned row (session runner, server git, git-in-process, pty, windows, linux-only census) still matches. Both failing assertions are that one swap: the why-missed `tests/Antiphon.Tests` filter stays 89 classes, and the non-merge land filter stays 38.

The index, not the selector, decides the window. `tests/derived-impact-index.json` records that path as cap 42, owed 17, first owed `C0747LandNoteExpireEndpointTests`. `scripts/impacted-classes.ps1` copies those edges. The strings are not an ordering, casing, or separator difference. Linux reproduces the same two strings the Windows run rejected.

### Introducing commit/card

introducing commit/card: `236eee404704af1181c76a99579b4b72e32cd6da` adds `LinkedUnattemptedQueueWakeTests` (C488). `64715d884fe0c82f7dc3c86bc5763c4d458ce9d4` regenerates the index and moves that class into the kept window, pushing `C0747LandNoteExpireEndpointTests` to the front of the owed list. `751f3a0bba4000234a6aa88508228e8583838824` pins the owed count at 17 in `DerivedImpactIndexTests` and is the landing commit of Code task `a18cc863`. The last edit of the selector literals is `3e74fffefdb1347be59f85c181d12bbfb64a4893` (2026-11-17), which still names the expire class and is an ancestor of the index refresh. No later commit touches `ImpactedClassesSelectorTests.cs`. The rc tip and current master have the same kept edge for this path, so later master index refreshes (including CARD-2004 and CARD-2005, which are on master and not on this rc) did not move this window again.

### Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Code task `a18cc863`, plan [2026-11-19-c488-fix-checkpoints.md](../superpowers/plans/2026-11-19-c488-fix-checkpoints.md). The closed list is the wake class, recovery and persistence slices, `CompletionNoteDurableSweepTests`, the CARD-1892 attempt methods, the nine-class guard union (floor 81), `WindowsUnitPlatformAdmissionTests`, and `DerivedImpactIndexTests`. This checkout has no Review outcome file for `a18cc863`.

### Outside the selection

why the failing test was outside that selection (lane/category/path): `ImpactedClassesSelectorTests` is Integration, and it is not in that closed list. The short merge guard union in [docs/testing-and-build.md](../testing-and-build.md) adds `DerivedImpactIndexTests` and `C1584_E2EExpandedBudget` and still does not name `ImpactedClassesSelectorTests`. `DerivedImpactIndexTests.capped_real_paths_keep_the_specific_class` only requires `PreDispatchFailureDeliveryTests` to be present and the owed count to be 17. That assertion stays green when the kept class identity changes. A whole-class selector run was measured at 260.5 s on 2026-11-15, so it was left off the short merge rows. CARD-1987, CARD-1995, CARD-2004, CARD-2005, and the later S6 index refreshes did not have to run it to land: their diffs do not include this test, and the merge lane does not select it from `tests/derived-impact-index.json`.

### Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/derived-impact-index.json` -> `/*/*/ImpactedClassesSelectorTests/*`

### Exemption

same cause as an already written note: none

### Proposed guard, not built

Compute the two expected alternations from the roster the selector already reads: kept index edges for the selected paths, union the hand classes, drop `windowsOnly`, and render with the selector's own ordering. A class the index keeps would match. A class the selector emits that the roster does not name would still fail. That removes this failure class. Adding the whole selector class to the short guard union would also have caught it, at the measured multi-minute cost those rows are avoiding.

## Cause B: C1336 omitted admission

`ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity` throws at `tests/fixtures/card1336-release-gate-followups.ps1:231` after `PASS C1336 N omitted`. The harness then exits 1, which is the Windows TRX's `ExitCode should be 0 but was 1`. The nightly result underneath is exit 3, phase `longpaths-not-enabled`. Linux reproduces that result on current master: candidate id `rc-2026-10-18-<sha12>` is correct, then the `-NoSync` branch refuses the fixture clone.

The fixture sets `$NightlySeams.Git = $null`, so the real git runs. `Write-C487Seams` answers `git config --get core.longpaths` with `true` only when that seam is installed. The clone is created with `git clone` and never sets local `core.longpaths`. `Invoke-AntiphonNightlyRunCore` refuses that checkout before `-WhatIf` can return 0. The candidate-id assertion is deliberately first, so this refusal cannot look like the old missing-id defect.

### Introducing commit/card

introducing commit/card: `afaf1f4a93d25d3dea8f83e93e0aa1fd58df6a6e` (Code task `016f2cc2`). It refuses `launch-rc`, the nightly `-NoSync` branch, and qualification preflight unless local `core.longpaths` is `true`. It updates the C1323 real checkout and the synthetic git seam. It does not update this fixture.

### Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Code task `016f2cc2`, plan [2026-11-19-longpaths-checkpoints.md](../superpowers/plans/2026-11-19-longpaths-checkpoints.md). The closed list is `scripts/test-longpaths-checkout.ps1`, `LongPathsCheckoutScriptTests`, `C1323_PlatformExecution`, `C545_ResultLine`, `C1698_RunPrintsToolchainWarning`, the nine-class guard union, `DerivedImpactIndexTests`, `audit-land-verification-graph.ps1 -Check`, and the pin digest. This checkout has no Review outcome file for `016f2cc2`.

### Outside the selection

why the failing test was outside that selection (lane/category/path): `ReleaseGateFollowupTests` is Integration. `C1336_NightlyCandidateIdentity` is not a row in that plan. The short merge guard union does not name it. The longpaths rows that do use a real checkout (`C1323_PlatformExecution`) were updated; this fixture was not, because it bypasses the seam.

### Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/lib/nightly-run-impl.ps1` -> `/*/*/ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity*`

### Exemption

same cause as an already written note: none

### Proposed guard, not built

A census of fixtures that set `NightlySeams.Git = $null` and invoke nightly-run with `-NoSync` would have listed this clone next to the C1323 checkout. The repair itself is the missing `git config --local core.longpaths true` on that clone. The census is not added here.
