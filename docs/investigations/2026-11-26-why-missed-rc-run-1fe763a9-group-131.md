# Why missed: release-cut default mode assertion in rc run 1fe763a9, group 131

## Introducing commit/card

CARD-1836 S6 changed `ReleaseCut.cs` in `3e98258227156a2c5d08efe5fdfbf8b0fb74fca6`.
The change reached master in `fe0c427c90df139c220d9f4d1935290a4649d40b`, with the generated index refreshed in
`ef381ddf1b57c08e045b7e3f6d38fe197e468c3a`. The previous Windows run `b8cbfb24` at
`2fd61b643ea4231fa732cd4969b5284d43b33f39` passed `C1313_ModeCredit`. The test and launcher are identical at the failing pin
`0cfc4ac1ffb41f5af159a0c1db423fe119950267`. The test's `C1313 default-full` row searched for the old inline
`SelectionMode = options.SelectionMode ?? ""` assignment. S6 computes `effectiveMode` once and passes it to the selection
attempt and nightly run. An omitted mode still resolves to `Full`.

The repair retains the launcher default and forwarding checks. It parses a release cut with no mode flag, uses a local Git
fixture to seal a selection attempt, and checks the bound Full mode and the absence of change-aware execution evidence.
On Linux, the original method reproduced the sole failing row at `6150023e5a8accc0a285c3ef15e4d1616372d726`
under the build-slot gate. The other six rows passed. The fix needs to be included in the next `rc/2026-12-06`
candidate before rerunning group 131.

## Approving reviews

The [group 140 investigation](2026-11-26-why-missed-rc-run-1fe763a9-group-140.md) records the same landing's Review and Merge receipts.

- Final/Full Review rounds 1 `97597b37-02bd-40df-a3b4-d37dcfc98268` and 2 `3718c4a5-7d4c-4fb6-9ef1-931f17bdd179`
  returned Found. Round 3 `bb48f316-a048-4982-b526-1b417f52a994` returned Clean at
  `8bf7563ec35a0e00d12ceab3e3536d79a6a953cb`, run `20261126-003937-c991`. It ran CP-1 through CP-6, the six touched classes
  (49), the Final guards and `DerivedImpactIndexTests` (169), and `LiveLaneRunnerTests` with `NightlyCoreGoldenTests` (34).
- Merge `19c11d61-f8e2-4b5a-b7c7-e1e90f171beb`, run `20261126-011516-5bc7`, ran the same six touched classes (49),
  `DerivedImpactIndexTests` (27), and guards including `PlanCheckpointClassSelectionGuardTests` (77 passed, one skipped).

## Outside the selection

`ChangeAwareReleaseTests` is an unchanged Integration class under `tests/Antiphon.Tests/Scripts/`. The [CARD-1836 plan](../superpowers/plans/2026-11-25-card-1836-incremental-windows-qualification-plan.md#checkpoints)
selected `NightlyChunkRunnerTests`, `RcProjectSelectionTests`, `RcForcedFullTests`, `RcDirtyOrderCSharpTests`,
`IncrementalSelectionSoundnessTests` and `ReleaseCutTests`. CP-6 covered the new qualification seam in `ReleaseCutTests`.

The Merge brief used the fixed subset and the hand-maintained table. Replaying `--merge-subset` for
`3a16983de58b40d8968d4628ea8748b46438b055..ef381ddf1b57c08e045b7e3f6d38fe197e468c3a` with the landed table omitted the class.
The `nightly-live-lane` match selected `LiveLaneRunnerTests` and `NightlyCoreGoldenTests`. The landed derived index maps
`ReleaseCut.cs` to `RcDirtyLaunchOrderTests` and has no `ChangeAwareReleaseTests` edge on any path in that range.
The index builder scans C# declarations under `src/`, `server/` and `tests/`, and matches whole tracked paths in string
literals. The old row built the script paths from separate `Path.Combine` arguments.
The [CARD-2082 inventory](../superpowers/plans/2026-11-26-card-2082-invariant-inventory.md#fixtures-with-a-current-tree-assertion-to-extract-or-isolate)
includes the class and `scripts/release-cut.ps1`, with `C1313_BaselineAuthority` as its example method.

## Cheapest pre-land check

`scripts/lib/Antiphon.Nightly/ReleaseCut.cs` or `scripts/release-cut.ps1` →
`/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` in the CARD-1782 table.

## Exemption

None. Group 140 shares the introducing landing and missed selection mechanism. Its stale `Incremental` token check
was a separate assertion in `RcDirtyLaunchOrderTests`.
