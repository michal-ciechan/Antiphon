# c487-probe owned temp root

Code task / landing owner: `092a4d67-a75e-471e-9b8a-2aabd44501e6`.
Base: `1443b7ebc82277e9b21cc83d31a686bdb62208c2`.
Branch: `feat/card-task-092a4d67`.

## Implementation

`WithTempDirectoryAsync("c487-probe-")` creates the probe under the stable
owned root `antiphon-owned-c4870000000000000000000000000000` from
`OwnedTempRoot`, tracks that root, and at the start deletes only direct
children named `c487-probe-` plus 32 lowercase hex digits whose last write is
older than 24 hours. The parent must be the temp directory. A fresh sibling, a
same-length non-hex name, and a same-prefix directory outside that root stay.
The finally clears read-only attributes, skips reparse points, and retries the
existing `StartRefGit.DeleteDirectory`. Probe assertions are unchanged.
`TempSweep` stays off. This does not delete the historical top-level
`/tmp/c487-probe-*` trees.

## Performance budget

CP-1 is the cold build plus the classification class, including two small probe
builds. Later rows reuse that output. No repeat block after green. No whole
Unit lane. No positive controls. Cost floor is the sum of `EstimatedMinutes`
(51). Windows execution of the new witness is unobserved.

## Unlisted runs

- Red witness at `ebd3b1bf92b608da4f14da059e152628b73ba7b6`, before this plan:
  `/*/*/TestClassificationPolicyTests/C487_probe_stale_sibling_is_removed_at_start`
  failed 1/1. The created directory's parent was not the owned root.
  Build slot `waited=0s` for the build (`load1=12.31`) and the run (`load1=15.29`).
- One green rerun of that same filter after the fix, on the native default
  output, before the cold checkpoint, at `27e9ed0ff1167d4ae0b07e5b226dfd6f83f65df8`.
  Passed 1, failed 0, skipped 0, duration 4s 817ms. Build `load1=11.74`, slot
  `lease=72bb6863-39fc-42eb-875e-549f15dccb2a waited=0s held=220s`, 0 errors.
  Run `load1=15.01`, slot `lease=265da2fd-1fc7-4347-b3ef-88e2078ec5a0 waited=0s held=9s`.
  No `LINUX COMPILER` line. CP-1 repeats the class on an isolated output.

## Verification design

- V-1: at probe start, a direct child of the owned root named `c487-probe-`
  plus 32 lowercase hex digits, older than 24 hours, is removed. The sweep logs
  `removed=`.
- V-2: a child younger than 24 hours stays, a `c487-probe-` name that is not
  32 lowercase hex digits stays, and a directory with the exact prefix whose
  parent is not the owned root stays. Marker bytes compare after `\r\n` is
  folded to `\n`. Paths use `Path.GetFullPath`.
- V-3: the probe directory is created under that owned root, and the existing
  removal assertions still pass, including a read-only file inside the directory.
- V-4: `OwnedTempLeakTests` still removes a tracked root on release and on the
  assembly hook.
- R-1: `DerivedImpactIndexTests` passes. If `tests/derived-impact-index.json`
  changes, the driver is built first and the regen script is run twice, the
  second printing unchanged, and that json is the last commit.
- R-2: land-graph `-Check` exits 0.
- R-3: evidence diff over the full task base..HEAD, a conflict-free merge-tree
  against fetched `origin/master`, OS-witness inventory, and the nightly
  `policyHash`, `PinnedInputDigest`, and `LegacyExclusionDigest` recomputed
  and left unchanged unless a guard fails.
- R-4: the guard classes in CP-4 pass. `WindowsUnitPlatformAdmissionTests` is
  not in this Linux selection: a Windows skip is not a pass.

No PC variants. Mutation is waived for this Code task (operator 2026-11-12).
Every PC stays pending for method-scoped SourceLanding Mutation. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | fix | tests/Antiphon.Tests -> bin-c487-probe/ | probe-class | `/*/*/TestClassificationPolicyTests/*` | V-1, V-2, V-3 | 43 results, 0 failed/skipped, includes C487_probe_stale_sibling_is_removed_at_start | 43 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | owned-temp | `/*/*/OwnedTempLeakTests/*` | V-4 | 10 methods, 0 failed/skipped | 10 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-1 | 27 methods, 0 failed/skipped | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | CP-1 | guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-4 | 43 results, 0 failed/skipped | 43 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-2 | exit 0 | n/a | 5 | true | n/a |
