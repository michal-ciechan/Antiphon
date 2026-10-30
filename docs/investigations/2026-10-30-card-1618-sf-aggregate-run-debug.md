# Aggregate-run Debug record for CARD-1618 S-F

CP-41/42/43 completed GREEN in **22m55.370s** on Linux/server2: **35 executed, 35 passed, 0 failed, 0 skipped, 0 reruns**. This is the commissioned aggregate-run Debug record; phase-attribution defects are recorded below. It supplies Linux execution evidence to S-F acceptance, without native Windows, Full, R-2, S-F-cost or landing approval.

## Source, selection and provenance

Requested base: `985034e0aa541a4fb48519a48e5811dad9d37d11`. Actual clean receipt/build source: **`61e73d76ff19c4796de2ae6188676d117f37d059`** on `feat/card-task-a59a5491`. The only base-to-tested-tip change is this report's initial eleven lines (commit `61e73d76ff19c4796de2ae6188676d117f37d059`); executable/test/plan/cost source is identical to the requested base. Do not relabel these receipts as the base or the older S-F support-run SHA. Final report amendments do not change their binding.

Run `20261030-134015-c5f2`, 2026-10-30T13:40:20.5893126+00:00 through 2026-10-30T14:03:15.9591148+00:00; terminal state `done`, exit 0. Offline validation returned exactly:

```text
CHECKPOINT SOURCE VALID source=61e73d76ff19c4796de2ae6188676d117f37d059 rows=3
```

Host `4551c5a7852f`, Debian 12/Linux x64, 24 cores, SDK 10.0.403, MSBuild 18.9.15, test runtime .NET 9.0.22, TUnit 1.44.0 / MTP 2.2.2. Initial MemAvailable 103386 MiB. Load average began at 3.14/9.64/19.62, peaked in an observed sample at 47.46/30.54/25.25, and later fell to 13.55/23.83/23.46. This was a shared, varying-load host; no idle-host claim is made. Read-only broker samples showed budget 4, occupancy 3 then 2, no waiters. No other task or qualification was changed.

One serial checkpoint invocation selected only CP-41, CP-42 and CP-43, with one shared scheduled build `bin-c1199final`. The manifest has eight build definitions; seven were unused. The assembly-local `ProcessSpawnLimit` was one. No full suite, extra confidence selection, native test or timing-only repeat ran. Bootstrap/import were separately leased setup, not additional test builds. Bootstrap waited 0s, built in 5.42s (lease held 6s), zero errors; import waited 0s and imported all 54 rows. TEMP=/tmp, UseAppHost=false off Windows, node reuse disabled.

The unedited import and overlay remain at `.antiphon/sf-aggregate-debug/imported.yaml` and `.antiphon/sf-aggregate-debug/checkpoints.yaml`. Normalization verified identical 54 IDs, After/build/group/filter/count/estimate/serial fields, allowing only documented deadline fields and phase environment metadata. CP-1..35 use twice the per-case body table; CP-41/42/43 use 28/48/20m; shared-build floor 20m and acquisition allowance 10m. The tool derived 126m execution + 10m admission/hard-wall allowance for the selected 58 estimated minutes. The commissioning task's own ceiling was 90m; the run finished well before it. No blanket row override or timeout widening occurred.

The leased bootstrap command was (its outputs were removed after completion):

```sh
env TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label a59a5491-checkpoint-bootstrap -SlotWaitMinutes 25 -- dotnet build tools/Antiphon.Checkpoints -c Release --property:OutputPath=bin-sf-debug-tool/ --property:UseAppHost=false -nodeReuse:false
```

Import used the same gate with label `a59a5491-checkpoint-import`, then `dotnet tools/Antiphon.Checkpoints/bin-sf-debug-tool/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-30-card-1199-windows-runnable-plan.md --out .antiphon/sf-aggregate-debug/imported.yaml`. The import/overlay pair is retained; a new import must use a fresh evidence path.

The run command was:

```sh
env -u ANTIPHON_C1199_WINDOWS_SHAPE_FIXTURE TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-sf-debug-tool/Antiphon.Checkpoints.dll run .antiphon/sf-aggregate-debug/checkpoints.yaml --rows CP-41,CP-42,CP-43 --serial --max-wait 50s
```

Every observer exit 75 was followed by `wait --run 20261030-134015-c5f2 --max-wait 50s` until terminal exit 0 and proven executor death. All owned commands were awaited. Reproduction requires first rebuilding the removed tool through `scripts/build-slot.ps1` into the same fresh alternate output, then importing/validating the plan overlay; do not rerun merely to fill timing history.

## Row results and estimate/deadline check

Times below are seconds except estimates/deadlines. CP-41's manifest estimate of 24m includes the 10m shared build plus a 14m body; CP-42/43 estimates exclude that build.

| Row | Passed/executed | Slot wait | Row wall | Host wall | Startup including discovery | Case window | Host teardown | Plan body / deadline (min) | Over estimate by >25%? |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| CP-41 | 17/17 | 0 | 354.509 | 353.040 | 2.988 | 349.437 | 0.615 | 14 / 28 | No |
| CP-42 | 13/13 | 0 | 324.222 | 322.891 | 3.242 | 318.689 | 0.960 | 24 / 48 | No |
| CP-43 | 5/5 | 0 | 272.611 | 271.832 | 6.648 | 264.686 | 0.499 | 10 / 20 | No |

The shared build's scheduled wall was 324.276s, execution 317.249s, build phase 309.824s, admission wait 0s. Underlying dotnet build was 275.03s (4m35.03s), zero errors, 1416 warnings. CP-41 plus that whole scheduled build was 11.313m, below its 24m combined estimate. All rows and the build are below their estimates; no +25% flag applies.

Discovery is **unknown as a separate phase**: the existing receipt combines TUnit startup and discovery before the first TRX case. It must not be reported as zero or as an extra measured discovery run. Per-case fixture teardown is distinct from the host teardown column.

Whole-run decomposition (non-overlapping wall accounting):

| Component | Seconds |
|---|---:|
| Scheduled shared-build wall, including its preflight/verification | 324.276 |
| Three test hosts (nested startup/case/host-teardown values above) | 947.764 |
| Row wrapper/receipt overhead outside those hosts | 3.579 |
| Finalization/output cleanup | 98.322 |
| Remaining scheduler/report wall | 1.429 |
| Aggregate wall | 1375.370 |

Budget-clock wall 1375.294s, execution 1375.292s, admission-only union 0.002s. Every acquisition reported waited=0s; the 0.002s union is bookkeeping, not queue time. Raw TRX case-duration sum is 930.439s (15m30.439s), not aggregate wall. Finalization is separately measured; the fixture teardown and nested external calls below are not added to it.

## Per-case time, p50/p95 and Slow tripwire

Exact planned/executed class+method sets agree, 17/13/5, with no extra/missing identities. TRX categories are Unit for all 35; analytical group is `checkpoint/git-build`. All 35 explicit `ANTIPHON_TEST_PHASE_ID` values join uniquely to complete JSONL records; no name-only fallback was used. Three journals, no missing joins. Assembly MVID `1571a8bc003f46cea289561814e6f0c5` is the same across the three hosts.

Each method has **one successful Linux sample**. Its empirical p50=p95=max equals the observed value below, but n=1 does **not** establish a reliable per-method p95. These distributions must stay separate from Windows history. No repeat was run solely for measurement.

| Row/class | Samples across distinct cases | Case-duration p50 / p95 / max (s) | Sum (s) |
|---|---:|---:|---:|
| CP-41 | 17 | 19.475 / 35.873 / 35.873 | 348.225 |
| CP-42 | 13 | 23.812 / 46.569 / 46.569 | 317.710 |
| CP-43 | 5 | 60.814 / 71.833 / 71.833 | 264.504 |
| All cases | 35 | 22.934 / 69.311 / 71.833 | 930.439 |

The across-case p95 is descriptive, not a p95 for a repeated row or an individual test. The 5-second tripwire flags **33/35** cases; **none** is on the current Slow allowlist, by literal TRX-name or qualified-identity matching. None of the three classes has Category=Slow. S/T/O below mean CheckpointShapeConflictTests / CheckpointTrackedBaselineTests / CheckpointOutputConfinementTests, namespace Antiphon.Tests.Checkpoints. Every row below passed.

| CP / class.method | n=1 observed p50=p95=max (s) | Method watchdog (s) | Provisional ceil(2x observed) (s) | >=5s / allowlisted |
|---|---:|---:|---:|---|
| CP-41 S.missing_receipt_is_not_negative_success | 0.010 | none declared | 1 | no / no |
| CP-41 S.shape_modified_tracked | 19.475 | 120 | 39 | yes / no |
| CP-41 S.shape_changed_untracked | 22.934 | 120 | 46 | yes / no |
| CP-41 S.shape_existing_output_directory | 10.905 | 120 | 22 | yes / no |
| CP-41 S.shape_compiler_collision | 12.489 | 120 | 25 | yes / no |
| CP-41 S.shape_existing_stamp | 12.094 | 120 | 25 | yes / no |
| CP-41 S.shape_ignored_descendant | 34.614 | 120 | 70 | yes / no |
| CP-41 S.shape_clean_build | 27.562 | 240 | 56 | yes / no |
| CP-41 S.shape_existing_output_symlink | 8.013 | 120 | 17 | yes / no |
| CP-41 S.shape_staged_untracked | 22.653 | 120 | 46 | yes / no |
| CP-41 S.shape_symlink_on_file | 18.220 | 240 | 37 | yes / no |
| CP-41 S.shape_deleted_tracked | 17.141 | 240 | 35 | yes / no |
| CP-41 S.shape_deleted_untracked | 29.580 | 120 | 60 | yes / no |
| CP-41 S.shape_directory_on_file | 35.873 | 240 | 72 | yes / no |
| CP-41 S.shape_regular_user_move | 34.370 | 120 | 69 | yes / no |
| CP-41 S.shape_file_on_directory | 18.200 | 240 | 37 | yes / no |
| CP-41 S.shape_untracked_descendant | 24.091 | 120 | 49 | yes / no |
| CP-42 T.local_output_property_tampering_is_reported | 41.735 | 300 | 84 | yes / no |
| CP-42 T.imported_props_reference_tampering_is_reported | 46.569 | 300 | 94 | yes / no |
| CP-42 T.generated_target_file_tampering_is_reported | 24.943 | 300 | 50 | yes / no |
| CP-42 T.index_added_file_is_kept_and_indexed | 18.352 | 420 | 37 | yes / no |
| CP-42 T.fresh_checkout_run_executes_a_test | 30.153 | 420 | 61 | yes / no |
| CP-42 T.staged_ignored_file_is_kept_and_indexed | 22.052 | 420 | 45 | yes / no |
| CP-42 T.clean_untracked_output_build_exits_zero | 22.770 | 300 | 46 | yes / no |
| CP-42 T.tracked_snapshot_contains_committed_files | 4.825 | 60 | 10 | no / no |
| CP-42 T.transitive_reference_tampering_is_reported | 34.137 | 300 | 69 | yes / no |
| CP-42 T.staged_untracked_file_is_kept_and_indexed | 17.425 | 420 | 35 | yes / no |
| CP-42 T.intermediate_output_path_tampering_is_reported | 24.247 | 300 | 49 | yes / no |
| CP-42 T.dirty_tracked_tree_is_refused_and_unmodified | 6.691 | 180 | 14 | yes / no |
| CP-42 T.passing_script_requires_writable_source_receipt | 23.812 | 420 | 48 | yes / no |
| CP-43 O.unevaluable_import_is_refused_before_the_build | 13.305 | 180 | 27 | yes / no |
| CP-43 O.transitive_import_build_preserves_the_tracked_dependency | 69.311 | 300 | 139 | yes / no |
| CP-43 O.imported_props_build_preserves_the_tracked_dependency | 49.241 | 300 | 99 | yes / no |
| CP-43 O.conditional_import_build_preserves_the_tracked_dependency | 60.814 | 300 | 122 | yes / no |
| CP-43 O.directory_build_props_build_preserves_the_tracked_dependency | 71.833 | 300 | 144 | yes / no |

No method watchdog is below twice its observed successful Linux duration. No repeated-row measured p95 exists, so there is no defensible claim of p95 qualification or evidence-based deadline widening. Row candidates from twice the **observed host wall** (not across-case p95) are:

| Row | Existing deadline | Provisional 2x-host candidate | Decision |
|---|---:|---:|---|
| CP-41 | 28m | 707s, rounded 12m | Retain current executed plan; collect comparable history before reviewed tightening |
| CP-42 | 48m | 646s, rounded 11m | Retain current executed plan; collect comparable history before reviewed tightening |
| CP-43 | 20m | 544s, rounded 10m | Retain current executed plan; collect comparable history before reviewed tightening |

The build's inherited 48m deadline greatly exceeds this pass's roughly 11m rounded 2x scheduled-build wall; the plan's independent build floor is still 20m. This is its disclosed CARD-1199/CARD-1404 scheduler-coupling exception (`tools/Antiphon.Checkpoints/Execution/RunScheduler.cs:372`). Proposed separate repair: independent build and row budgets, retaining source/slot/cancellation custody, rather than allowing CP-42 to set the build clock. Do not tighten Windows/shared method clocks from this single Linux pass. Collect the next three naturally required native passes as the plan requires, then assess the timeout-audit sufficient-history threshold (20 comparable samples, ordinary runs within 14 days, CARD-1563); never pool Linux/Windows or failed/successful timings.

## Fixture phases and defects (report only)

| Row | Complete case envelopes (s) | Spans | Exclusive process (s) | Exclusive fixture (s) | Exclusive fs (s) | Unassigned remainder (s) |
|---|---:|---:|---:|---:|---:|---:|
| CP-41 | 349.387 | 380 | 225.219 | 0.771 | 0.003 | 123.394 |
| CP-42 | 318.685 | 0 | unknown | unknown | unknown | 318.685 |
| CP-43 | 264.682 | 0 | unknown | unknown | unknown | 264.682 |

CP-41 records 32 fixture-setup spans (2.372s inclusive, containing nested Git waits), 221 process-wait spans, 79 OurCode spans (122.226s inclusive), 32 filesystem spans and 16 link-teardown spans (0ms at recorder resolution). Only link teardown has those zero-duration observations; full fixture cleanup remains unknown. OurCode spans are not a separate coarse bucket and can remain in remainder. Nested spans are not additive wall. CP-42/43 envelopes have zero spans, so their full envelope is unassigned remainder, not proof of zero setup/build/Git/teardown cost. Process spawn and inner restore/build/graph/TUnit/receipt boundaries remain unknown; no unsupported external-call split is claimed.

Findings are inherited at the assigned base (source content is unchanged here), unresolved, and require separate repair work; no product/test/instrumentation fixes were made:

1. **Operation labels discarded, measurement incomplete.** `tests/Shared/TestTiming.cs:751` accepts only a fixed vocabulary; every one of CP-41's 380 spans is operation=`omitted`. Call sites such as `tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs:525`, :587 and :601 use shape-tool-checkpoint / shape-pwsh / shape-git, which are absent from that vocabulary. Proposed fix: admit those exact non-sensitive static tokens (and separate fixed receipt/build/graph labels), with a guard proving the production recorder retains them. Do not permit arbitrary command text or environment payloads. CARD-1514/R4/R6.
2. **Missing inner-phase attribution and full fixture lifecycle.** `tests/Antiphon.Tests/Checkpoints/CheckpointTrackedBaselineTests.cs:503`, :524, :541, :581; `tests/Antiphon.Tests/Checkpoints/CheckpointOutputConfinementTests.cs:455`; `tests/Antiphon.Tests/Checkpoints/CheckpointFixtureProcess.cs:23`; `tests/Antiphon.Tests/Checkpoints/CheckpointTestScope.cs:217` / :220. Proposed fix: instrument repository/home creation, seed build, individual Git/CLI calls, tool versus script graph/restore/build/run, receipt validation and the whole base cleanup hook, preserving existing cancellation/custody. Add explicit discovery timing at `tools/Antiphon.Checkpoints/Report/PhaseTimings.cs:24` or the host adapter, rather than inferring it from startup. Capture at next naturally required execution; do not repeat this green selection solely to refill spans. CARD-1514.
3. **Slow registry/category gap.** Classes at `tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs:12`, `tests/Antiphon.Tests/Checkpoints/CheckpointTrackedBaselineTests.cs:10` and `tests/Antiphon.Tests/Checkpoints/CheckpointOutputConfinementTests.cs:13` remain Unit only and have no matching entries in `tests/Antiphon.Tests/slow-tests-allowlist.txt`. 33 observed cases cross the 5s threshold. Proposed separate correction: document measured three-class Slow registry entries and app/category extraction under CARD-1314/CARD-1545/CARD-1546 while retaining every required identity and process limiter. This report does not silently recategorize or exclude them.
4. **Stale native class costs.** `scripts/lib/nightly-class-costs.json:3714` prices all six O cases at 25s and explicitly admits five early failures; S/T rows are absent. The five successful Linux O cases alone sum 264.504s (10.580 times that six-case price); historical successful native five-case control was 448.44s. Proposed fix remains the already-required S-F-cost: add S=17, T=13 and refresh O=6 from successful native CP-41..44, after accepted S-F and target S3 publication, preserving native sibling and source/run provenance. No Linux values were written into a Windows cost file. CARD-1404.

No AppHost, HTTP service, Docker/database container, migrations, database seed or database clone was needed: those phases are not applicable. No repeated per-test infrastructure setup defect was found. `CheckpointFixtureGitEnvironment.EnsureReady` (:187) caches Git/NuGet/restore template information once per host/assembly, not per test. Each row has its own host, which is an allowed assembly lifetime. Fresh Git repositories and graph/seed variants are the subject of these tests; O already seeds each variant once and reuses captured bytes for tool/script. Proposed future templates must preserve fresh-checkout absent-obj and independent index/output witnesses. No flaky failure was observed; one pass does not prove absence of flakiness.

## Windows cost check and preserved history

The plan's CP-41/42/43 model is **14 + 24 + 10 = 48m body + 10m outer shared build = 58m**, before queueing and orchestration. CP-44, if included on Windows, adds about 1m separately. That is distinct from Windows Full's **40-50m incremental body**: Full already pays the outer assembly build and the existing Windows sibling. New support-test cost/build-packing delta is separate; no current comparable Full baseline exists here and no fleet total is invented.

Linux successful bodies total 15m30.439s, with 15m47.764s of host wall and 22m55.370s overall. It would be wrong to carry that Linux wall directly into the Windows pricing file. A transparent, provisional check using the plan's retained native samples is:

| Model input/check | Seconds or ratio |
|---|---:|
| Seven unchanged shape methods, native per-case sum | 235.430s |
| Same seven, this Linux pass | 93.726s |
| Native/Linux ratio for those shapes | 2.512x |
| Ten ported shape cases, this Linux pass | 254.499s |
| Estimated native ports at shape-specific ratio | 10.655m |
| Native unchanged twelve + eight corrected T + five corrected O controls | 711.930 + 797.400 + 448.440 = 1957.770s |
| Modeled native successful 35-case body | **43.284m** |
| Sensitivity: all twelve unchanged native/Linux ratio | 4.120x; **50.107m** body |

Thus **40-50m is plausible; the 40m end looks low, and scheduling near 50m is safer**. The shape-specific estimate plus a 10m assembly build is about 53.3m; broad-ratio sensitivity is about 60.1m, bracketing the 58m checkpoint model. This is a cross-host/old-source sensitivity check, not successful native evidence or p95. The broader ratio is dominated by differently priced baseline work and is not a universal OS multiplier. Host load, SDK versions, fixture ports and missing detailed phases remain confounders. The plan's existing 48m body allocation is reasonable provisional scheduling; fresh native success must replace it. The cost file is a Windows model; its missing S/T and 25s O price plainly look low, not a reason to lower the plan forecast.

Historical failures are preserved, not replaced: investigation source `ade54222543381e2e1b31ad79d8ae33a4035f70b`, Windows 10 x64/NTFS, SDK 10.0.300, pwsh 7.6.9, Git 2.50.1.windows.1, three gate-only dirty edits, unknown load. Original aggregate 35m43.591s, including 23 early failures and the 56.273s existing Windows sibling, excluded a 9m56.13s build. `shape-native` was 771.72s, 7 passed/10 failed; `baseline-config-selected` had 8 passing controls/797.40s; `confinement-config-selected` had 5 passing controls/448.44s, outer 504.14s including 45s admission. The old cost receipt was `8f055f87/all-group-62`, five early O failures. These are plan-retained provenance, not reread desktop/native receipts. The plan-linked original investigation file is absent from this checkout, so its other exact run IDs/raw TRX/phases cannot be recovered here. Do not invent them, combine failed samples into a successful p95, or claim native qualification from this Linux run.

## Retained evidence, cleanup and handoff

Raw generated evidence remains ignored under `.antiphon/checkpoints/20261030-134015-c5f2/`, `.antiphon/sf-aggregate-debug/` and `/work/worktrees/task-a59a5491/.antiphon/c1199-timing/linux/61e73d76ff19c4796de2ae6188676d117f37d059/aggregate/`. The manifest/receipt hashes below and the essential receipt lines are committed in this Markdown record; raw JSON/TRX/log payloads are not force-added, following git-common. `.antiphon/sf-aggregate-debug/analysis.json` retains the complete planned/executed mapping, categories, case durations and explicit phase joins; `.antiphon/sf-aggregate-debug/hashes.json` inventories generated artifacts. The evaluated restore graph includes 22 projects, with an observed 35-project build closure including HostCleanup, Checkpoints and staged helpers; the graph is retained at `.antiphon/sf-aggregate-debug/project-reference-closure.json`.

| Artifact | SHA-256 |
|---|---|
| `.antiphon/checkpoints/20261030-134015-c5f2/report.json` | `c6f7b0cb5109cbe1baeba7cc04ee9a2a5a2839c50a7b346b9caab7874824a7a6` |
| `.antiphon/checkpoints/20261030-134015-c5f2/report.md` | `961e7115be062586b0dbdadf0bb295bcb23e07a70ee8c157e612d58b9c984d33` |
| `.antiphon/checkpoints/20261030-134015-c5f2/rows/CP-41/run.trx` | `03c9df9e1a305cfd1ac6a51358af27b8a40f9b303185c6c6bd5fe3c482422cfc` |
| `.antiphon/checkpoints/20261030-134015-c5f2/rows/CP-42/run.trx` | `a12baa5f11bb652cac2a3a2ed46294944dba6471a13cf4b9c05d55e5ff0683b5` |
| `.antiphon/checkpoints/20261030-134015-c5f2/rows/CP-43/run.trx` | `a02e9a36401f16657ef38b43da32e858d56ff8e32938367923aef7940c374ecc` |
| `.antiphon/sf-aggregate-debug/imported.yaml` | `b35a81cea65370eb316d9d24415b3a1a510ff91e66ee6e707c13493b8623b3e4` |
| `.antiphon/sf-aggregate-debug/checkpoints.yaml` | `904de85d94144e12dd243106042d8698194cfb73b37fdbf9b977c3d56c37959e` |
| `.antiphon/sf-aggregate-debug/project-reference-closure.json` | `57c44affe36668ec4771b8a778d98274777178b119f8041d5003f61eb1223688` |
| `.antiphon/sf-aggregate-debug/planned-identities.json` | `479f5b8c44fb6825f04e7dda2ad9572776fedc3e9c178c3322b39b05c88d2c41` |
| `/work/worktrees/task-a59a5491/.antiphon/c1199-timing/linux/61e73d76ff19c4796de2ae6188676d117f37d059/aggregate/chunk-35734864f7ba2f6ff53751e17b40d25298041b06226a1e46f94702973d5ccddd/cases-2809-507af2a77e654509ad2adee8b1dcbd7e.jsonl` | `45d5c316861eb16811bf0f1e38f2fe051339620ca1c98735d0ad5f2d6d2b5aed` |
| `/work/worktrees/task-a59a5491/.antiphon/c1199-timing/linux/61e73d76ff19c4796de2ae6188676d117f37d059/aggregate/chunk-3e57c173e0dfd802855a91c363cb3b30afb0a93c47aa1dfc91fcf4fba47e73a1/cases-13670-c7bb7b11307e436ab5dc5845f6809beb.jsonl` | `4384f9f292ba6a011d422b2cebb31db0c2f62c261665c98935ade3a6264aad77` |
| `/work/worktrees/task-a59a5491/.antiphon/c1199-timing/linux/61e73d76ff19c4796de2ae6188676d117f37d059/aggregate/chunk-64c50d8bcf22f41fadf369dda04b228ea793a3bee0085b3c87ee862b2c936a59/cases-12002-5b6ad5caf42047e5b3020729ba5ee6c9.jsonl` | `0e962462af47a398a73915e515ef0e2d6bbee11dadcc757d74d1f1ca7e659efa` |

Essential receipt/phase lines copied unedited from the terminal report:

```text
CHECKPOINT CP-41 commit=61e73d76ff19c4796de2ae6188676d117f37d059 build=ok filter=/*/*/CheckpointShapeConflictTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-a59a5491/.antiphon/checkpoints/20261030-134015-c5f2/rows/CP-41/run.trx slot=granted waited=0s dirty=0 source=61e73d76ff19c4796de2ae6188676d117f37d059 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-41 waited=0s executed=354.393s wall=354.509s
PHASES CP-41 slotWait=0s build=309.8244082s startup=2.9878086s testsWall=349.4373934s teardown=0.6148705s hostWall=353.0400719s
CHECKPOINT CP-42 commit=61e73d76ff19c4796de2ae6188676d117f37d059 build=reused filter=/*/*/CheckpointTrackedBaselineTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-a59a5491/.antiphon/checkpoints/20261030-134015-c5f2/rows/CP-42/run.trx slot=granted waited=0s dirty=0 source=61e73d76ff19c4796de2ae6188676d117f37d059 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-42 waited=0s executed=324.173s wall=324.222s
PHASES CP-42 slotWait=0s build=0s startup=3.2420988s testsWall=318.6894564s teardown=0.9596639s hostWall=322.8912199s
CHECKPOINT CP-43 commit=61e73d76ff19c4796de2ae6188676d117f37d059 build=reused filter=/*/*/CheckpointOutputConfinementTests/(imported_props_build_preserves_the_tracked_dependency*)|(directory_build_props_build_preserves_the_tracked_dependency*)|(conditional_import_build_preserves_the_tracked_dependency*)|(transitive_import_build_preserves_the_tracked_dependency*)|(unevaluable_import_is_refused_before_the_build*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a59a5491/.antiphon/checkpoints/20261030-134015-c5f2/rows/CP-43/run.trx slot=granted waited=0s dirty=0 source=61e73d76ff19c4796de2ae6188676d117f37d059 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-43 waited=0s executed=272.546s wall=272.611s
PHASES CP-43 slotWait=0s build=0s startup=6.6475395s testsWall=264.6860197s teardown=0.4986715s hostWall=271.8322295s
BUDGET wall=1375.294s execution=1375.292s admissionOnly=0.002s
```

Terminal cleanup: `cleanupRefusals=[]`, `cleanedOutputs=true`; tool removed `tests/Antiphon.Tests/bin-c1199final` and the exact run shadow copy after executor death. Bootstrap roots `tools/Antiphon.Checkpoints/bin-sf-debug-tool` and `src/Antiphon.HostCleanup/bin-sf-debug-tool` were inventoried with sizes/hashes, then removed by exact literal path after every owned run had completed. Receipts were retained. No source edits occurred while tests ran. No product, test, timeout, plan, graph, pins, policy or class-cost file changed. No native/desktop file, live d47848cc qualification, target branch, tag, deployment, publish, force push or rebase was touched.

This record completes the commissioned Linux aggregate execution and diagnostic reporting. Detailed R4/R6 attribution remains incomplete for the inherited instrumentation defects listed above; do not describe the record as complete inner-phase instrumentation. The caller owns S-F acceptance and separate repair triage. Existing CP-45/46/54 evidence remains bound to its original source/run; it is not relabeled into this run. All native slice verification, R-2 operator decision and S-F-cost prerequisites remain owed. No landability or Clean/Full certificate is claimed.

--- next stage ---
next: S-F acceptance by the S-F task (d2c87328) using this record
handoff: Accept the source-bound Linux CP-41/42/43 35/35 result and this Debug record; retain native verification, R-2 and S-F-cost obligations, and triage inherited timing/Slow registry gaps separately. Do not repeat this successful aggregate solely for timing.
artifact: docs/investigations/2026-10-30-card-1618-sf-aggregate-run-debug.md
