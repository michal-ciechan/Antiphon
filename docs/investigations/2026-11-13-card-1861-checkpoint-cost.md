# CARD-1861: checkpoint build and driver costs

The slow run was healthy, with three separate costs: compilation/custody, selector
replay, and admission. The safe first repair removes the regeneration driver's
dependency on the entire Antiphon.Tests project. The full historical selection has
not been rerun, and a typical Code/Review wall time below 15 minutes is **not yet
demonstrated**. The remaining work has existing owners or a new performance card.

## Historical source and evidence

- Run `20261112-235906-610d`, source
  `73292703cc2ccbcab9f11d0d1616ae2bef33bea5`, Code task `042b61c0`.
- Original root:
  `/work/worktrees/task-042b61c0/.antiphon/checkpoints/20261112-235906-610d`.
  Read-only copies of report JSON/Markdown, executor log, host, manifest, four TRX
  files and CP-4 case journal are retained in this task's ignored
  `.antiphon/c1861-historical/`. No original evidence was edited.
- Debian 12, 24 cores, .NET SDK 10.0.404, test runtime .NET 9.0.23;
  initial available memory 99572 MiB; load average 10.40/16.90/9.24.
  Shared broker budget 4, grant maxcpucount 6, serial checkpoint execution.
- Original `report.json` SHA256:
  `2091550e690dba102ccb97f3f57b4c0d60ea0cf5a6887fd4120164673c6bbdf5`;
  `report.md`: `0bce90979a4dcf2dfa0f6a7fe552cef5ef65fb6b8c18ec76e80583ae5696bd2e`;
  `executor.log`: `c50ca2c89cae6631b81b30899fa3e66f2cf55c73c3a90374324937333196d98f`;
  CP-4 `run.trx`: `79d981f2cf414645c54d3475d8e1cbe0c4996434aea7f267469ef4d6cf8d0668`.
- Committed narrative/unedited historical CHECKPOINT lines:
  `docs/investigations/2026-11-13-card-1860-code-evidence.md` on
  `origin/feat/card-task-042b61c0`, report tip `e7a0013a5`.
  CARD-1859 Review's supplied 31m22s is a separate observation: its raw records
  are unavailable here, so it is not merged into this sample or its percentiles.

## Phase reconstruction (seconds)

Build wall includes confinement, ledger and source work around the build driver;
the compiler column is the build log's MSBuild `Time Elapsed`, nested inside it,
not CPU time or a separately instrumented csc phase. Build wait is also inside build wall.
Row slot wait is separate from the three host phases. Startup includes discovery:
there is no separate discovery timestamp. Tests wall is the first-start to
last-end interval from TRX; it is not the sum of overlapping case durations.

| Row | Project / count | Build wall (compiler inside) | Build slot wait | Row slot wait | Startup + discovery | Tests wall | Host teardown | Row wall |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| CP-0 | native driver prerequisite, command | 368.897 (367.680) | 0 | included in command, 0 | n/a | n/a | unknown | 368.897 (same command, count once) |
| CP-3 | SessionRunner restart compatibility, 20 pass / 22 native skips | 151.192 (42.710) | 0 | 0 | 2.290 | 70.380 | 0.663 | 74.447 |
| CP-4 | Antiphon.Tests, 84 pass | 365.434 (194.100) | 0 | 240 | 15.541 | 518.150 | 0.643 | 775.190 |
| CP-5 | SessionRunner limiter, 2 pass; reuses CP-3 | 0, reused | 0 | 0 | 3.211 | 0.078 | 0.708 | 4.974 |
| CP-6 | Pty limiter, 2 pass | 224.038 (43.590) | 30 | 75 | 2.883 | 0.015 | 0.674 | 79.597 |

The three isolated builds' receipt execution accounting is respectively
108.527, 324.065 and 132.632 seconds. Their build-driver durations are 107.052,
313.859 and 129.323 seconds; neither is compiler-only elapsed time. Compiler to
full-build-wall gaps are 108.482, 171.334 and 180.448 seconds (the last includes
30 seconds admission). Those gaps are not all hashing: the records do not split
every preflight, graph evaluation, ledger and seal subphase. Keep the remainder
unattributed rather than claiming it all as an available saving.

The scheduler budget clock reports 2202.354 seconds wall = 1857.229 execution +
345.125 admission-only union. The outer report clock says 2202.434 seconds; its
start/end envelope differs by 0.080 seconds. The row/build walls above, plus
156.339 seconds finalization, sum to 2200.106 seconds, leaving 2.327 seconds outer
orchestration remainder. Host phases do not fill each row wall: its remaining
source/driver/report time is also remainder, not teardown or discovery.
Finalization includes 156.105 seconds output cleanup, 0.095 evidence, and 0.139
output discovery. Report publication is logged separately at 0.208 seconds.

Total ordinary outcome: 108 passed, 0 failed, 22 Windows-only skips. The source
validator accepts CP-0/4/5/6; the complete native gate remains incomplete because
CP-3 skips native cases. This investigation does not change that historical verdict.

## Percentiles and dominant cases

Nearest-rank p95: sort passed TRX result durations ascending and take index
`ceil(0.95 * N) - 1`. These are observed **case-duration distributions within one
run**, not repeated measurements of one method or a checkpoint-run p95.

| Selection | N passed results | Case-duration p95 (s) | Maximum (s) |
|---|---:|---:|---:|
| CP-3 | 20 | 5.4760103 | 14.8792200 |
| CP-4 | 84 | 32.2283038 | 143.0709389 |
| CP-4 DerivedImpactIndexTests subset | 27 | 138.4136509 | 143.0709389 |
| CP-5 | 2 | 0.0779356 | 0.0779356 |
| CP-6 | 2 | 0.0146576 | 0.0146576 |

There is **no measured run/row/build p95**: one comparable observation is a
provisional sample. No timeout is changed using these heterogeneous percentiles.
Bounded next collection: retain this task's one green and the next ordinary Review
receipt, then the next three naturally required comparable runs, grouped by source,
platform, SDK and load, under CARD-1861/1862. Do not launch extra full runs to grow N.

CP-4's DerivedImpactIndexTests summed cases cost 517.720 seconds; limiter cases
sum to 29.370 seconds, with overlap. The leading methods are
`merge_subset_matches_the_hand_selector` 143.071s,
`s1_replay_matches_declaration_and_index_edges` 138.414s,
`windows_only_classes_stay_out_of_linux_filters` 66.131s,
`capped_real_paths_keep_the_specific_class` 40.051s,
`committed_index_matches_the_builder` 32.228s, and
`override_file_names_exist_and_are_undeclared` 23.325s. The real regeneration
method is only 10.984s. Code inspection shows repeated fresh selector processes
and full tests-tree walks; individual walk/CLI/IO subspans were not recorded, so
the attribution is a hypothesis for CARD-1862, not measured census-only time.
The phase journal's empty child phases/processes cannot prove zero fixture/IO cost.

## Implemented repair and its proof

The driver now source-links `DerivedImpactIndexBuilder.cs` and
`tests/Shared/PlatformTaxonomy.cs`, using their existing Roslyn 5.9.0 and TUnit.Core
1.44.0 packages. It no longer references Antiphon.Tests. The builder and taxonomy
bytes, Program.cs, regeneration script, command-line contract and index format
are unchanged. There is no new cache, skipped row, timeout or retry.

The structural witness fails on the old graph before trying to execute a driver.
Its green continuation runs the actual regeneration script -NoBuild twice in a
new indexed Git fixture. Three assertion groups prove graph exclusion, real
stale-index regeneration with reverse type edges and both OS labels, and a
byte-identical unchanged second result. These are script assertion groups,
**not three TUnit executed test results**; command-row TUnit counters stay n/a.

Verification plan: `docs/superpowers/plans/2026-11-13-card-1861-checkpoint-cost-plan.md`.
Red test commit: `c88bcfd9c01bcfb98d03fa0b4495563abe37ecf5`.
Production/green tested commit: `3e568966bb1f0617812bf0ebd03138ec3cd6f7e9`.
Red run `20261113-005058-9eee`: CP-1 exit 1 at the intended
`ASSERT driver build graph must contain no ProjectReference`, 2.818s execution,
225s slot wait, dirty=0, stable source. No fixture/build error earns this red.

Green behavior run `20261113-005521-dff0`: CP-0 build and CP-2 witness both exit 0;
three script assertion groups passed, none failed. The fresh native-default build
log elapsed is 3.20s, row execution 4.273s, admission 90s. The witness takes 7.719s
execution with zero admission. Whole narrow run: outer wall 104.221s (budget wall
104.136s = 14.119s execution + 90.017s admission-only).

That first CP-0 held a lease but its shell command omitted an explicit node cap.
Command rows do not inject maxcpucount. Retain it as diagnostic timing rather than
crediting it as the final capped build. This is an introduced verification issue,
corrected in plan-only commit `91952602bd431bcf86240b44b479b8e1fa12e71b`.
The correction adds CP-3, a native-output Rebuild at the historical
`-maxcpucount:1`; no production or witness bytes changed after S2. The same three
script assertion groups were not repeated.

Matched-cap run `20261113-005940-e7d7`: CP-3 exit 0, 0 warnings/errors, MSBuild
elapsed **4.03s**, row execution **5.414s**, admission **30s**, outer wall **37.358s**
(budget wall 37.249s = 7.234s execution + 30.016s admission-only). Both current
green reports pass the tool's source validator at their own exact SHA. These are
command rows, so buildSource=notApplicable is the honest envelope; no isolated
test-build stamp or TUnit count is invented.

The matched-cap observed build-log reduction is **363.65s** (367.68 -> 4.03s,
98.9%); command execution falls **363.390s** (368.804471 -> 5.414230s). This is a
single historical cold graph versus a forced standalone Rebuild with warm restored
assets, not a controlled repeated benchmark or p95. The new graph truly contains
only the driver, and the first fresh native build independently took 3.20s.
Host load also differs: original 10.40/16.90/9.24, first green 27.12/29.45/27.70,
matched-cap run 35.46/29.52/27.82; all on 24-core Debian 12 / SDK 10.0.404.
No full-selection speedup is inferred from subtracting unrelated queue times.

Green report SHA256:
`4f012c68c5c50b163c468510f394fbbfbf62029754dcad7c1706fbff21d6e8e3`;
matched-cap report:
`caccb722be3f8987740abd560b16154278e36155a23bdbc674fa2a0b55754adf`.

The only unlisted build is the declared checkpoint-tool bootstrap in this fresh
worktree: 32.27 seconds compiler elapsed, 11 warnings, 0 errors, 121 seconds slot
wait, 33 seconds lease held. It uses native output and UseAppHost=false. Scheduled
checkpoint commands take their own leases. No test/build ran outside the gate.

## Remaining reductions and a defensible 15-minute target

| Change / owner | Measured cost or saving available | Risk / disposition |
|---|---|---|
| Standalone driver (this change) | 367.68 -> 4.03s build-log elapsed, 363.65s observed reduction; matched node cap, one historical cold graph versus a standalone Rebuild. First fresh build 3.20s. | Same exact sources compiled twice; small fixture covers real semantics. Host load/restore state differ, native Windows timing is unmeasured. No cache invalidation risk. |
| Reuse one build for compatible rows | CP-5 already records 0 build seconds through CP-3 reuse; a second runner build would cost about 151.192s at the observed first-build rate (counterfactual estimate, not a measured saving). | Already supported. Same project, After, properties, Repeat and owned output only. CP-3/4/6 are different projects, so merging those build identities is invalid. CARD-1493 owns reuse lint. |
| Reuse unchanged outputs across runs | CARD-1629 records a separate server2 experiment: cold 170s versus unchanged rebuild 29s (141s observed difference); fresh OutputPath 203s. This does not measure our three graphs' saving. | Existing CARD-1629, no duplicate. Requires exact source fingerprint, SDK/platform/properties/repeat and verified owned outputs; current checkpoint custody deliberately requires fresh output. No automatic relaxation here. |
| Faster selector census/replay | The two dominant methods expose 281.485s total elapsed to investigate; 200s is a target, **not measured savings**. Full class tests wall is 518.150s. | New CARD-1862 (`cb4dbdad-79eb-4f6d-83d2-605e7eaabdd3`). Keep every replay/assertion and fresh inputs; no stale cross-call cache or dropped required method. |
| Batched cleanup preflight | 156.105s current cleanup cost; possible savings unknown. | Existing CARD-1327, no duplicate. Keep per-entry ownership/link/content/containment and race checks. Optimizing away those checks would change what a green receipt proves. |
| Driver prerequisite / narrower selection at plan creation | Driver cost can be omitted only when no selected test consumes it; avoiding an unneeded full CP-4 would expose its 535.020s execution and 365.434s build wall. Those are observed costs, **not savings for the same proof**. | Never silently skip a committed checkpoint row or mandatory guard. Use the reviewed diff/dependency selection (CARD-1820) and retain owed lanes. CARD-1860 explicitly names the whole class, so it cannot use this omission. |
| Restore caching | No measured incremental saving. NuGet package cache is already shared (CARD-1629); this run still restored project assets into cold outputs. | Do not add --no-restore to a fresh isolated graph or copy absolute-path obj trees. Further work belongs with CARD-1629/1493 artifact-cache design. |

A 15-minute hard wall guarantee is impossible with independently queued shared
admission. For a typical compatible narrow Code/Review plan, first remove needless
driver graph compilation and duplicate equivalent builds; keep the named proof.
For the exact CARD-1860 selection, even zero admission plus this driver saving
still exceeds 15 minutes. Reaching that target while retaining all cases needs the
selector, custody and incremental-build work above; these savings cannot honestly
be added as if already measured or independent. The new narrow driver checkpoint
is measured separately below; it is not a rerun of the historical full selection.

No slot budget, build-slot semantics, process limit, live runner/session, host,
Docker object or volume was changed. Work proceeded under the caller's standing
authority; no further approval was needed. Mutation was explicitly waived.

## Current unedited checkpoint lines and checks

```text
CHECKPOINT CP-1 commit=c88bcfd9c01bcfb98d03fa0b4495563abe37ecf5 build=n/a filter=pwsh -NoProfile -File scripts/test-derived-impact-driver.ps1 -EvidenceRoot .antiphon/c1861-red executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=225s dirty=0 source=c88bcfd9c01bcfb98d03fa0b4495563abe37ecf5 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-0 commit=3e568966bb1f0617812bf0ebd03138ec3cd6f7e9 build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --nologo executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=90s dirty=0 source=3e568966bb1f0617812bf0ebd03138ec3cd6f7e9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=3e568966bb1f0617812bf0ebd03138ec3cd6f7e9 build=n/a filter=pwsh -NoProfile -File scripts/test-derived-impact-driver.ps1 -EvidenceRoot .antiphon/c1861-green executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=3e568966bb1f0617812bf0ebd03138ec3cd6f7e9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=91952602bd431bcf86240b44b479b8e1fa12e71b build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -t:Rebuild -p:UseAppHost=false -maxcpucount:1 --nologo executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=30s dirty=0 source=91952602bd431bcf86240b44b479b8e1fa12e71b sourceState=clean buildSource=notApplicable
```

Tool validators: S2 source `3e568966bb1f0617812bf0ebd03138ec3cd6f7e9`, 2 rows,
exit 0; S3 source `91952602bd431bcf86240b44b479b8e1fa12e71b`, 1 row, exit 0.
All rows capture dirty=0 and stable clean source. Minimal verification omits the
whole Unit lane, unrelated classes and the historical full selection; no assertion
or required named historical row is removed or reclassified by the optimization.
All commands finished; no checkpoint executor remains to be awaited.

Pre-report evidence-diff over base `cc48c70050a2932eddf4abe18cea8659d478c33e`
through S3: commits=3, entries=0, violations=0, exit 0. Fetched origin/master;
merge-tree at S3 exits 0, tree `c487c83192afbbce1674b605764c1d2ad3bba9dd`.
The final report commit changes only this narrative; the caller report gives its
published SHA and the final full-range evidence-diff/merge-tree results.
