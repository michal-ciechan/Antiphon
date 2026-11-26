# Why missed: release-cut mode assertion in rc run 1fe763a9, group 140

## Introducing commit/card

CARD-1836 S6 first put `Incremental` in `ReleaseCut.cs` in `3e98258227156a2c5d08efe5fdfbf8b0fb74fca6`.
The S6 implementation `2088f4989878dafc92cbe719f0cb27fb598a8e0d` retained the qualification seam. It reached master in
`fe0c427c90df139c220d9f4d1935290a4649d40b`, followed by index regeneration `ef381ddf1b57c08e045b7e3f6d38fe197e468c3a`.
The previous Windows run `b8cbfb24` at `2fd61b643ea4231fa732cd4969b5284d43b33f39` passed group 140.
The class was identical at the failing pin `0cfc4ac1ffb41f5af159a0c1db423fe119950267`, where all six methods failed in
the shared `AssertOffSwitch` check for the presence of the word `Incremental` in the compiled cut's source.
`OnlyDirtyAndSampleLaunch` reproduced the same assertion on Linux at `aa1d0f9f7102abf12535dcb39647179b1259f4cb`.

[CARD-1836 D-6](../superpowers/plans/2026-11-25-card-1836-incremental-windows-qualification-plan.md#decisions)
keeps Full as the public default and reserves incremental execution for qualification. The shipped PowerShell launcher
and compiled CLI parser accept Full, Shadow and ChangeAware. The S6 fixture constructs an in-process Incremental
request, which is qualification-only even if it falls back to Full. The repaired guard exercises the compiled CLI's
Full default and explicit mode refusal and checks the launcher's allowed modes and Full default.

## Approving reviews

- Final/Full Review round 1 `97597b37-02bd-40df-a3b4-d37dcfc98268` and round 2 `3718c4a5-7d4c-4fb6-9ef1-931f17bdd179`
  returned Found. Round 3 `bb48f316-a048-4982-b526-1b417f52a994` returned Clean at
  `8bf7563ec35a0e00d12ceab3e3536d79a6a953cb`, with validated checkpoint run `20261126-003937-c991`.
  It ran CP-1 through CP-6, the six touched classes (49), the Final guards and `DerivedImpactIndexTests` (169),
  and `LiveLaneRunnerTests` with `NightlyCoreGoldenTests` (34).
- Merge `19c11d61-f8e2-4b5a-b7c7-e1e90f171beb` reported validated run `20261126-011516-5bc7` with
  `DerivedImpactIndexTests` (27), its guard row including `PlanCheckpointClassSelectionGuardTests` (77 passed,
  one skipped), and the same six CARD-1836 classes (49). The Merge brief specified those rows.

## Outside the selection

`RcDirtyLaunchOrderTests` is an Integration class in [the script tests](../../tests/Antiphon.Tests/Scripts/RcDirtyLaunchOrderTests.cs).
The [plan](../superpowers/plans/2026-11-25-card-1836-incremental-windows-qualification-plan.md#checkpoints)
listed it under existing tests affected. The executable Review rows instead selected `NightlyChunkRunnerTests`,
`RcProjectSelectionTests`, `RcForcedFullTests`, `RcDirtyOrderCSharpTests`, `IncrementalSelectionSoundnessTests`
and `ReleaseCutTests`. The source-reading assertion was in the untouched Integration class.

The derived index at the landing mapped `scripts/lib/Antiphon.Nightly/ReleaseCut.cs` to `RcDirtyLaunchOrderTests`.
Merge's `--merge-subset` uses the fixed subset and the hand-maintained table. Replaying the landing range
`3a16983de58b40d8968d4628ea8748b46438b055..ef381ddf1b57c08e045b7e3f6d38fe197e468c3a` with its table omitted the
class. The matching `nightly-live-lane` row supplies `LiveLaneRunnerTests` and `NightlyCoreGoldenTests`.
The ordinary selector resolves the derived edge for `ReleaseCut.cs`.

The [CARD-2082 inventory](../superpowers/plans/2026-11-26-card-2082-invariant-inventory.md#fixtures-with-a-current-tree-assertion-to-extract-or-isolate)
already lists this class and `AssertOffSwitch` among the fixtures with a current-tree assertion to extract or isolate.

## Cheapest pre-land check

`scripts/lib/Antiphon.Nightly/ReleaseCut.cs` or `scripts/release-cut.ps1` →
`/*/*/RcDirtyLaunchOrderTests/OnlyDirtyAndSampleLaunch` in the CARD-1782 table. This single method exercises
the shared guard without setting up a Git fixture.

## Exemption

None.
