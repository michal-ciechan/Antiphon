# CARD-1861 round 1 review: standalone DerivedImpact driver

Verdict: **CLEAN**, no blockers. Final/Full ordinary scope.

- Subject: `origin/feat/card-task-87ef194c`, Debug task `87ef194c-4b05-4fab-85ef-1bb891863c50` (Codex).
- Reviewed range: `cc48c70050a2932eddf4abe18cea8659d478c33e..825d6ad84c646a2407bfa30a0b18f87bd10ba577` (4 commits).
- Host: server2 Linux (Debian 12, 24 cores), shared load average 25 to 35. Every build and test took a build slot.

## Change

`scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj` drops the
`ProjectReference` to `tests/Antiphon.Tests`. It now compiles
`tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs` and `tests/Shared/PlatformTaxonomy.cs`
by path as `Compile Include ... Link=`, so there is one source file and no copy. It also adds the
two direct packages (`Microsoft.CodeAnalysis.CSharp` 5.9.0, `TUnit.Core` 1.44.0).
`scripts/test-derived-impact-driver.ps1` is the new witness. The builder, taxonomy, `Program.cs`,
the regeneration script and the committed index are unchanged in this range.

## Old and new driver output is byte-identical

The old driver is master's `ProjectReference` build, compiled cold in a scratch clone at `cc48c7005`
in 205 s. The new driver is the tip build run through `scripts/regenerate-derived-impact-index.ps1 -NoBuild`.
Both drivers ran on the same trees, and each tree's index was restored between runs.

| Tree | Old SHA256 (prefix) | New SHA256 (prefix) | Result |
|---|---|---|---|
| master `cc48c70050a2` | d9e2936026a0c3e6 (= committed, "unchanged") | d9e2936026a0c3e6 | identical |
| `eb38ce6674ee` (master~15) | 9258c302b9a192d6 | 9258c302b9a192d6 | identical |
| `a0f7d75ccc82` (master~100) | b3d8d8e089ac6dfa | b3d8d8e089ac6dfa | identical |
| `6173cc88be1a` (master~200) | ceae1e812888ed29 | ceae1e812888ed29 | identical |
| master plus changed classes | 2b18101d73a2364e | 2b18101d73a2364e | identical |

The changed tree adds a new `[LinuxOnly]` class that references a server type, renames
`DerivedImpactIndexTests`, deletes `ChannelOutboundStorageTests.cs` and adds `[WindowsOnly]` to
`ProcessSpawnLimitTests`. Both drivers produced the same index for it, and the expected effects
appear: windowsOnly 32 and linuxOnly 2, the renamed class has 13 edges and the old name 0, and the
deleted class has 0. The resolved Roslyn and TUnit.Core versions match (5.9.0 and 1.44.0 in both
`deps.json` files).

## Inputs, rebuild, fresh checkout

- **Inputs:** the builder's only compile-time dependency from the test project is the
  `PlatformDeclaration` enum in `PlatformTaxonomy.cs`, which is linked. Class discovery is
  `git ls-files` plus Roslyn syntax, and the attribute names are a literal table in the builder.
  No reflection over the test assembly is used, and comparisons are ordinal.
- **Rebuild:** in a fresh clone at the tip, the builder's `Cap` was changed from 25 to 3. The script
  rebuilt the driver (dll mtime advanced) and printed `derived-impact-index changed`; the mutation
  was then restored. `committed_index_matches_the_builder` still byte-compares against the builder
  compiled into Antiphon.Tests, so a stale committed index still fails it.
- **Fresh checkout:** `regenerate-derived-impact-index.ps1` with no `-NoBuild` built the driver in
  5.05 s and printed `derived-impact-index unchanged`.

## Checkpoints and tests (reviewed SHA 825d6ad84)

Checkpoint run `20261113-011259-af9b` used `--rows CP-0,CP-2,CP-3 --serial`. Verdict GREEN, and
`validate` printed `CHECKPOINT SOURCE VALID rows=3`.

```
CHECKPOINT CP-0 ... exit=0 slot=granted waited=90s dirty=0 source=825d6ad84c646a2407bfa30a0b18f87bd10ba577 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-0 waited=90s executed=5.031s wall=95.148s
CHECKPOINT CP-2 ... exit=0 slot=granted waited=195s dirty=0 source=825d6ad84c646a2407bfa30a0b18f87bd10ba577 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-2 waited=195s executed=16.159s wall=211.348s
CHECKPOINT CP-3 ... exit=0 slot=granted waited=75s dirty=0 source=825d6ad84c646a2407bfa30a0b18f87bd10ba577 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-3 waited=75s executed=8.673s wall=83.790s
BUDGET wall=392.147s execution=32.050s admissionOnly=360.097s
```

- **CP-1 red at S1** (`c88bcfd9c`, scratch clone): exit 1 on
  `ASSERT driver build graph must contain no ProjectReference`, which is the intended red. The
  witness bytes are identical at S1 and at the tip.
- **`Antiphon.sln -m:1`:** 0 errors, 6m37.59s.
- **Tests:** from that native output, `DerivedImpactIndexTests` 27, `TestClassificationPolicyTests`
  41, `TestClassificationGuardTests` 1, `SlowTestRegistryMergeTests` 5, `SlowTestTripwireTests` 2
  and `NativeLinuxPolicyWitnessTests` 3 ran: 79 passed, 0 failed, 0 skipped, host duration 10m50s.
  An earlier attempt was cut by my own 590 s foreground limit, with no result and no leftover
  processes, and was rerun in full.
- **`check-evidence-diff`:** commits=4 entries=0 violations=0.
- **Merge-tree** against origin/master `cc48c7005` (unchanged): clean, tree `782aec387427`.

## Advisories (non-blocking, no card filed)

- `InternalsVisibleTo Antiphon.DerivedImpactIndex` in Antiphon.Tests.csproj is now unused.
- The witness checks `ProjectReference` only in the driver's own XML, not in imported props.
- The package pins duplicate the test project's, as other tools do. If they diverge,
  `committed_index_matches_the_builder` fails loudly; the index cannot drift silently.

## Unlisted runs

These ran outside the plan's checkpoint rows, each under its own lease:

- The checkpoint tool bootstrap build.
- The old-driver cold build.
- One lease around the old/new comparison loop. It runs no builds; the drivers are prebuilt.
- The fresh-checkout regeneration and the mutant regeneration.
- The CP-1 red.
- The solution build and the named-class test run.

Each one is a check the brief asked for. No PCs ran: mutation was waived by the operator, and any
PCs stay pending.
