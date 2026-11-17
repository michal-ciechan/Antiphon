# Why missed: C1514 native factory test kept the pre-CARD-1893 HistoryPath default

WHY-MISSED for rc/2026-11-24 run `06863906`, chunk `session-runner-all-profile-17`,
`C1514HostLoadNativeTests.Windows_ProductionFactoryPreservesHostSamples`.
Determination and fix: [2026-11-17-c1514-native-history-path.md](2026-11-17-c1514-native-history-path.md).

## Introducing commit/card

introducing commit/card: 9cf3e491705247bc03814e6afed77a6534805dfd (CARD-1893), cherry-pick of
`ccd0d185d9e4a447f7411cd9b528adbdf282778d` (branch production change `40730e2ebae97b8060bece919fb7cbd441da9573`),
landed in Merge 253 (index regen `870a93d21`). It removed the bind-time `HistoryPath ??=` default
from `HostStatsRoutes.AddHostStats` on purpose; the stale assertion is in
`tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeTests.cs:38` (`4d0105ea1`, CARD-1514).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 92c62298 (CARD-1893 collector hardening, Final Review round 1, reviewed `0f1f7bf4892a1dfa8005f910917b0885ccddb571`),
  scope Full, selection: CP-2 `/*/*/(C1893CollectorDiagnosticsTests*)|(C1514HostLoadSpoolTests*)|(C1514HostLoadWireTests*)|(C1514HostLoadSampleTests*)|(C1514HostLoadAuthorityTests*)|(HostStatsEndpointTests*)|(HostStatsSamplerTests*)|(HostStatsStoreTests*)|(RunnerCapabilitiesTests*)/*`,
  CP-3 the 11-class guard union, CP-4 `(HostTopConsumerUpgradeReplayTests*)|(C1514HostLoadHistoryTests*)`,
  `DerivedImpactIndexTests`, `TestTimingOperationLabelTests`, `ImpactedClassesSelectorTests`.
- Merge 253: the hand merge subset for its range, `scripts/impacted-classes.ps1 -MergeSubset -Range 61259342c..870a93d21`
  resolves to kinds `host-stats,native-linux-gate-roster,remote-scripts,spawn-limit-rosters,test-lane-category`
  and only `(ProcessSpawnLimitTests*)|(RunnerCapabilitiesTests*)` on `tests/Antiphon.SessionRunner.Tests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): native Windows qualification
lane (suite session-runner) for the Windows arm; category Integration; path
`tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeTests.cs`.
The Linux arm `Linux_ProductionFactoryPreservesHostSamples` shares `VerifyAsync` and fails on Linux
for the same reason (CP-1 of the determination note), so a Linux row would have caught it. Three
selections left it out:

1. The CARD-1893 plan's CP-2 hand-listed nine classes and omitted the older class that asserts the
   same option. The Review reran the plan rows.
2. The Code/Review selector's derived classes for the range do include `C1514HostLoadNativeTests`
   (edges from `HostLoadSpool.cs`, `HostLoadContracts.cs`), but no stage ran `derivedClasses`, and
   `-MergeSubset` stays the hand table, whose `host-stats` kind (`src/Antiphon.SessionRunner/HostStats*.cs`)
   selects only `RunnerCapabilitiesTests`.
3. The derived index has no edge from `src/Antiphon.SessionRunner/HostStatsRoutes.cs`, the file that
   lost the default, to this class: the test calls the extension method `AddHostStats` and never
   names the type `HostStatsRoutes`, and the index builds edges from type-name uses only.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`src/Antiphon.SessionRunner/HostStats*.cs`, `src/Antiphon.SessionRunner/HostLoad*.cs` -> `/*/*/(C1514HostLoadNativeTests*)|(C1893CollectorDiagnosticsTests*)/*`

On Linux the native class runs its Linux arm (about 1 s) and skips the Windows arm; the kind should
carry `"windows": "required"` so `windowsFilters` owes
`/*/*/C1514HostLoadNativeTests/Windows_ProductionFactoryPreservesHostSamples` to the native lane.
Until the table changes (held by CARD-1820 S1R5 and the stages behind it), a Merge brief whose range
touches those globs adds the row
`CP-merge-subset-C1514HostLoadNativeTests | tests/Antiphon.SessionRunner.Tests | /*/*/C1514HostLoadNativeTests/Linux_ProductionFactoryPreservesHostSamples | Min 1`.
A second, separate gap: the derived index misses extension-method callers (point 3 above).

## Exemption

none
