# c487-probe owned temp root

Code task / landing owner: `092a4d67-a75e-471e-9b8a-2aabd44501e6`.
Base: `1443b7ebc82277e9b21cc83d31a686bdb62208c2`.
Branch: `feat/card-task-092a4d67`.

## Implementation

`WithTempDirectoryAsync("c487-probe-")` creates the probe under the stable
owned root `antiphon-owned-c4870000000000000000000000000000` from
`OwnedTempRoot`. The stable root is not tracked. Each probe directory is
tracked and released by `WithTempDirectoryAsync`, so `OwnedTempRoot.ReleaseAll`
does not delete the root or a fresh foreign sibling under it. At the start the
sweep deletes only direct children named `c487-probe-` plus 32 lowercase hex
digits whose last write is older than 24 hours, and it skips a probe-named
symlink or other reparse point. The parent must be the temp directory. A fresh
sibling, a same-length non-hex name, and a same-prefix directory outside that
root stay. The finally clears read-only attributes, skips reparse points, and
retries the existing `StartRefGit.DeleteDirectory`. Probe assertions are
unchanged. `TempSweep` stays off. This does not delete the historical top-level
`/tmp/c487-probe-*` trees.

## Performance budget

CP-1 is the cold build plus the classification class, including two small probe
builds. Later rows reuse that output. No repeat block after green. No whole
Unit lane. No positive controls. Cost floor is the sum of `EstimatedMinutes`
(56). Windows execution of the new witnesses is unobserved.

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
- Red witnesses at `2a323fc265e553968647ad1c4ee4d6d63bd6815a`, before the
  release fix:
  `/*/*/TestClassificationPolicyTests/(C487_probe_release_leaves_fresh_foreign_siblings*)|(C487_probe_stale_sweep_skips_a_probe_named_reparse_point*)`
  failed 2/2. Release deleted the owned root (`owned root must survive release`).
  The sweep logged `removed=1` and deleted the probe-named reparse point.
  Build slot `lease=d2f6c1a4-3463-4928-8396-a3de042a4e89 waited=0s held=198s`
  (`load1=5.05`). Run slot `lease=c95fb17b-7ac8-47f9-945a-4528468f85a4 waited=0s held=17s`
  (`load1=13.38`). No `LINUX COMPILER` line.
- One green rerun of that same filter after the fix, on the native default
  output, before the cold checkpoint, at `942d1fff5686943694565fdf47f626384b2e2067`.
  Passed 2, failed 0, skipped 0, duration 11s 952ms. Build `load1=4.31`, slot
  `lease=a524a2b5-b03d-4f0f-b40f-7436356fe72e waited=0s held=286s`, 0 errors.
  Run waited until `load1=14.41` (first sample `22.06`), slot
  `lease=1c66caa9-01cb-431d-a1ef-f826d13f5185 waited=0s held=16s`.
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
- V-5: `OwnedTempRoot.ReleaseAll` in a scratch `TMPDIR` leaves a fresh
  `c487-probe-` plus 32 lowercase hex digits, an uppercase `c487-probe-`
  near-miss, and an unrelated directory. Marker bytes compare after `\r\n` is
  folded to `\n`. Paths use `Path.GetFullPath`. The child process runs the
  real release. Windows execution is unobserved.
- V-6: the start sweep skips a probe-named symlink or other reparse point
  whose last write is older than 24 hours. The link stays, and the target
  marker bytes stay.
- R-1: CP-3 builds `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
  before CP-4 runs `DerivedImpactIndexTests`. If `tests/derived-impact-index.json`
  changes, the regen script is run twice, the second printing unchanged, and
  that json is the last commit.
- R-2: land-graph `-Check` exits 0.
- R-3: evidence diff over the full task base..HEAD, a conflict-free merge-tree
  against fetched `origin/master`, OS-witness inventory, and the nightly
  `policyHash`, `PinnedInputDigest`, and `LegacyExclusionDigest` recomputed
  and left unchanged unless a guard fails.
- R-4: the guard classes in CP-5 pass. `WindowsUnitPlatformAdmissionTests` is
  not in this Linux selection: a Windows skip is not a pass. The closed union
  executes 39 cases. `RunnerDefaultGuidanceTests` shares the guidance file and
  is outside this union. The first guard receipt executed 39, passed 39, failed
  0, and exited 3 on a floor of 43.

No PC variants. Mutation is waived for this Code task (operator 2026-11-12).
Every PC stays pending for method-scoped SourceLanding Mutation. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | fix | tests/Antiphon.Tests -> bin-c487-probe/ | probe-class | `/*/*/TestClassificationPolicyTests/*` | V-1, V-2, V-3, V-5, V-6 | 45 results, 0 failed/skipped, includes C487_probe_stale_sibling_is_removed_at_start | 45 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | fix | CP-1 | owned-temp | `/*/*/OwnedTempLeakTests/*` | V-4 | 10 methods, 0 failed/skipped | 10 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | fix | n/a | derived-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:${ANTIPHON_BUILD_SLOT_MAXCPUCOUNT:-6} --nologo` | R-1 | exit 0, native-default driver before CP-4 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | fix | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | R-1 | 27 methods, 0 failed/skipped | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | fix | CP-1 | guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-4 | 39 results, 0 failed/skipped | 39 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | fix | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-2 | exit 0 | n/a | 5 | true | n/a |
