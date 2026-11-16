# Why g138b missed the TestTiming CPU overhead witness

Task cc5b4299; Linux Debug started from current origin/master cd4165b16280e2a781acaa527b059c9c9cb71c75. The reported native Windows failure was cpuMedian 0.08846153846153847 against the unchanged strict 0.05 bound, body duration 64.57 seconds, run 160200cb on rc/2026-11-22 a71c45ab4d5703d480f4eee4c4e30adab45bc4f9. This task did not access or change either live qualification, restart services, or write rc/master.

## Diagnosis and scope

(A) measurement sensitivity is the supported explanation; a Windows causal reproduction remains outstanding. The benchmark's Process.TotalProcessorTime includes all managed and native threads in the test process, including other TUnit tests. Its class-level NotInParallel("C1514Choke") serializes only peers with that key, while the rc chunk contained 362 tests. The test already alternates OFF/ON repetitions and pair order, uses nine pairs, includes database/Git/filesystem work and ON serialization/flush, and measures process CPU. Those features do not exclude unrelated in-process CPU. External CPU hogs are a different load source: their CPU is not directly charged by this process clock.

The keyed constraint was introduced with the original witness in 83128c416968fd6884c3a42ebff3a988fcff2168 (2026-10-23). Commit 5c795b4882cf2ff399d27c0297af31ee574f1459 (2026-10-28) shortened each arm from ten to two seconds of in-process wall while retaining 5% CPU, reducing measurement volume. This is an inherited measurement flaw, not a newly introduced production regression.

No (B) hot-path change was found across the requested cuts. TestTimingChokePointTests.cs is byte-identical at 64e3d78c487d51570f708492ab67b5db6d0de9a9, a71c45ab4d5703d480f4eee4c4e30adab45bc4f9, and cd4165b16280e2a781acaa527b059c9c9cb71c75. TestTiming.cs differs only by three c1934 operation-label entries between the rc cuts and compose-fixture-delete between rc22 and master. BoundOperation remains an ordinal HashSet.Contains; no extra traversal or per-span work was introduced. Recent fixture spans execute only in their own fixture workloads. They do not establish a global recorder regression.

## Why prior verification did not establish isolation

The timing-labels row in tests/impacted-classes.json names TestTimingOperationLabelTests. It checks literal label admission, not runtime overhead or scheduler isolation. Merges 244/245 addressed label/fixture coverage, following the g138/g139 label failure. Running that label witness cannot validate C1514T_Overhead. A cheap label-only green is not a benchmark result.

The earlier docs/superpowers/plans/2026-10-29-card-1514-rc-full-speedups-plan.md, section "Timing-sensitive protection (D-3)", already calls out the keyed constraint and requires this exact witness alone on quiet Windows. The requirement was prose rather than an executable scheduling constraint.

The b7812833 final report at origin/feat/card-task-3820a5f1 (4e16fc96cea6d89ff8612550d3c7a4d97abe3fcb), docs/investigations/2026-11-15-run-b7812833-final-result.md, reports complete Full coverage on rc21 with four failed chunks and 23 failed cases. C1514T_Overhead is absent from its complete failure and tolerated-skip lists: this supports a previous pass, not a direct reading of its native TRX, which is unavailable here. Its old label failure was green in g139; that is a separate witness.

## Repair

Give C1514T_Overhead a method-level unkeyed NotInParallel constraint. TUnit 1.44 explicitly gives method constraints precedence over class constraints and documents an unkeyed constraint as exclusive against every other test. Keep the keyed class constraint and process-spawn limiter for the other methods. Keep the nine-pair estimator, equal-work checks, finite-ratio checks, dropped-span and record assertions, serialization/flush CPU charge, 5% CPU bound, 10% wall bound, and slow-recorder rejection control unchanged. No production code, threshold, timeout, policy input, or exclusion changes.

PairedOverheadStatisticsTests.Process_cpu_benchmark_cannot_overlap_unrelated_tests is the cheap pre-land scheduling witness. The full overhead method took 94 seconds ambient and 113 seconds with 24 hogs before repair, including setup, so it is not a cheap always-on guard addition. Retain the full benchmark as an explicit timing-family verification row; do not infer performance from the metadata guard. Global test-process isolation does not reserve the physical host or prove the unchanged wall bound robust on every saturated Windows desktop.

## Measurement protocol and retained limits

Every build and test driver used scripts/build-slot.ps1, native default bin/obj, UseAppHost=false, serial rows, TUnit dotnet run, fresh TRX directories. Historical snapshots are detached, unmodified checkouts; no branch switch/rebase or historical commit was performed. Each loaded run starts nproc=24 busy-loop sh children from /tmp/cc5b4299-evidence/hog.sh, records exact PIDs, kills only those owned PIDs and waits for each before returning. Ambient means no task-injected hog; this shared runner cannot be certified idle. Receipts retain /proc/loadavg, SHA, wall, cpuMedian, wallMedian, raw pair data and TRX. No Windows equivalence is claimed from Linux.

The first ambient row took 94.18 seconds and the loaded row 113.01 seconds. Sixty such serial repetitions alone require roughly 104 minutes, before builds and repair verification, exceeding the explicit 75-minute commission. The task requested a time-budget decision asynchronously while continuing necessary work. Report the actual bounded sample count; do not invent the requested ten-per-cell distribution or a measured p95 from a small sample. Raw evidence root: /tmp/cc5b4299-evidence.

## Repair verification completed before historical sampling

- Base: `cd4165b16280e2a781acaa527b059c9c9cb71c75` (origin/master at commission start).
- Red witness: `a1749ae43480bce876a667e8a19aea661c78f09d`; full dependency build succeeded in 490.93 s. Exact method `/*/*/PairedOverheadStatisticsTests/Process_cpu_benchmark_cannot_overlap_unrelated_tests`: 1 executed, 1 expected failure, keys were `["C1514Choke"]`. This exposes the inherited scheduling defect at the unchanged benchmark implementation.
- Fix: `529f35d85530259dca267ce75be5512fdecd67be`; test-project build succeeded in 190.69 s. That build used `--no-restore -p:BuildProjectReferences=false` against the full red build's unchanged production dependencies. All repaired-source runs retained `UseAppHost=false` and `BuildProjectReferences=false`; outputs stayed native. No production source changed.
- Named row `/*/*/(TestTimingChokePointTests*)|(PairedOverheadStatisticsTests*)|(TestTimingOperationLabelTests*)/*`: **19 passed, 0 failed/skipped**, 126.53 s execution. Membership: 4 choke-point, 13 statistics/isolation, 2 operation-label tests. The slow-recorder rejection control is included. Fresh TRX: `/tmp/cc5b4299-evidence/green-named/named.trx`.
- Fixed-source loaded exact overhead row: **1 passed**, 119.18 s including driver overhead; all 24 owned hogs killed and joined. Fresh TRX: `/tmp/cc5b4299-evidence/fixed-loaded-01/run.trx`.
- Driver built first in 7.32 s; both gated `scripts/regenerate-derived-impact-index.ps1 -NoBuild` calls succeeded. First `derived-impact-index changed`, second `derived-impact-index unchanged`. The generated index was committed separately as `a64d51e6aa0c69a9f0af735f7968b36e64c749d1`; it adds the new regression-test reference edge only.
- Required eleven-class guard union: **114 passed, 1 skipped, 0 failed**, 210.58 s execution. The skip is `C1210_Windows_linux_only_outcomes_are_exact`, whose child roster audit requires Windows. All eleven intended classes appear in the fresh TRX `/tmp/cc5b4299-evidence/guard-union/guards.trx`. C1729's unedited result is `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`; the three existing ambiguity diagnostics were not changed.
- `/*/*/ImpactedClassesSelectorTests/*`: **50 passed, 0 failed/skipped**, 250.35 s execution. This additional named row checks the regenerated selection edge; TRX `/tmp/cc5b4299-evidence/selector/selector.trx`.
- Policy hash verified `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`; normalized protected-input digest recomputed as `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`; legacy exclusion pin remains `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` and its guard passed. None of their inputs changed, so no re-pin was made.

Initial observed medians (fractions, strict CPU limit 0.05; small samples, not a measured p95):

| Source | Load | Runs | cpuMedian | wall median | Driver wall seconds |
|---|---|---:|---:|---:|---:|
| master `cd4165b16280` | ambient | 1 | -0.0009974723664825326 | -0.006084585947199194 | 94.18 |
| master `cd4165b16280` | 24 hogs | 1 | 0.00010116055239443964 | -0.0014112335966778155 | 113.01 |
| fixed `529f35d85530` | ambient, named row | 1 | 0.0020266275698563084 | -0.001472396391882122 | 126.53 for all 19 tests |
| fixed `529f35d85530` | 24 hogs | 1 | 0.0006092283124135197 | 0.03645731145631898 | 119.18 |

External saturation alone did not reproduce the Windows failure in these observations. This supports isolating unrelated in-process work, but does not prove which peer caused the native 8.85% sample. The measured benchmark setup/body/flush sample data and TRX are retained; unavailable subphase breakdowns remain unknown, not zero. Physical-host exclusivity and the requested ten-per-cell historical matrix are not credited by these initial rows. No timeout, median rule or acceptance threshold was relaxed. Mutation was waived by the operator in the commission.

## Historical Linux observations and final measurement limit

The benchmark blob is exactly `2344edf1acfa87a01e62a13f9ede57fbf0c7c818` at all three baseline revisions. `PairedOverheadStatistics.cs` and `TestTrace.cs` also have no diff across those revisions. Historical builds and measurements used the unmodified detached checkouts `/tmp/cc5b4299-rc21` and `/tmp/cc5b4299-rc22`, with their own native bin/obj. The assigned branch remained on its authorized fast-forward line; no source was changed or committed in either historical checkout.

Selection-audit clarification: the hand-maintained timing-labels row is only one part of selection. The generated reverse index already listed TestTimingChokePointTests for tests/Shared/TestTiming.cs at initial master cd4165b16280e2a781acaa527b059c9c9cb71c75. A read-only impacted-classes.ps1 -Path tests/Shared/TestTiming.cs -Json resolution also includes the choke-point class in its emitted Antiphon.Tests filter, with no cap exclusion. It would therefore be incorrect to blame a missing reverse edge. The retained g138 round-two and g139 compose-label investigation receipts list label, selector, host/fixture and guard rows, rather than an overhead row; the g139 note already identifies omitted impacted execution despite an applicable detector. The complete Merge 244/245 execution receipts were not retrieved here, so this report does not assert that every Merge execution omitted the benchmark. An isolated green would also not validate the old mixed-chunk CPU measurement. Execute the resolved rows with source-bound receipts and enforce process isolation, rather than adding a duplicate selector edge. The new index edge in this repair is only TestTimingChokePointTests.cs -> PairedOverheadStatisticsTests.

All collected baseline observations, including negative ratios (fractions, CPU limit 0.05):

| Row | Exact source SHA | cpuMedian | Wall median | Driver seconds | Exit | Load before / after |
|---|---|---:|---:|---:|---:|---|
| master-ambient-01 | cd4165b16280e2a781acaa527b059c9c9cb71c75 | -0.000997472366482533 | -0.00608458594719919 | 94.18 | 0 | 7.21 7.77 6.74 2/2979 22886 / 4.09 6.59 6.42 4/2991 4739 |
| master-loaded-01 | cd4165b16280e2a781acaa527b059c9c9cb71c75 | 0.00010116055239444 | -0.00141123359667782 | 113.01 | 0 | 3.54 6.30 6.33 3/3026 8397 / 25.55 13.85 9.09 25/2937 18764 |
| rc21-ambient-01 | 64e3d78c487d51570f708492ab67b5db6d0de9a9 | -0.000171151842547993 | 0.00385364973084531 | 89.54 | 0 | 4.95 8.99 8.89 4/2963 12483 / 4.04 7.61 8.40 5/2967 31270 |
| rc21-ambient-02 | 64e3d78c487d51570f708492ab67b5db6d0de9a9 | -0.0121453737588508 | -0.00458036038215015 | 90.92 | 0 | 25.60 14.48 10.80 2/2979 8432 / 7.74 11.37 10.05 1/2982 14183 |
| rc21-loaded-01 | 64e3d78c487d51570f708492ab67b5db6d0de9a9 | 0.00301195724379492 | -0.0112670545663833 | 115.38 | 0 | 3.88 7.46 8.34 3/2975 759 / 25.60 14.48 10.80 30/3004 8423 |
| rc21-loaded-02 | 64e3d78c487d51570f708492ab67b5db6d0de9a9 | -0.00623064213908296 | 0.00681348633088086 | 111.43 | 0 | 7.74 11.37 10.05 1/2982 14183 / 24.97 16.56 12.08 28/2985 20854 |
| rc22-ambient-01 | a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 | 0.00252293784838736 | -0.000325154075035012 | 90.4 | 0 | 6.44 10.59 10.98 3/2900 23422 / 2.56 8.15 10.07 1/2894 24705 |
| rc22-ambient-02 | a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 | 0.00245113520556081 | -0.0034758238957094 | 96.96 | 0 | 23.66 14.23 12.07 1/2895 25792 / 5.77 10.70 11.02 1/2831 27027 |
| rc22-loaded-01 | a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 | -0.00274522430225552 | -0.00381006103205438 | 113.41 | 0 | 2.56 8.15 10.07 1/2894 24705 / 23.66 14.23 12.07 27/2919 25792 |
| rc22-loaded-02 | a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 | 0.00358509413108243 | 0.00216443512286797 | 118.78 | 0 | 5.77 10.70 11.02 1/2831 27027 / 24.75 16.23 13.00 27/2936 28304 |

Every loaded row has a retained `hogs-joined.txt`; all 24 task-owned busy-loop processes were killed through their owned process handles and awaited before the next row. A high one-minute load average at the start of a subsequent ambient row is retained honestly: the hogs are gone, but the average decays slowly. Ambient is not proof of a physically idle host. No unrelated process or live qualification was stopped.

| Baseline stratum | Collected / requested | Remaining |
|---|---:|---:|
| master ambient | 1 / 10 | 9 |
| master loaded | 1 / 10 | 9 |
| rc21 ambient | 2 / 10 | 8 |
| rc21 loaded | 2 / 10 | 8 |
| rc22 ambient | 2 / 10 | 8 |
| rc22 loaded | 2 / 10 | 8 |

Baseline rows with a CPU median: **10 / 60**. Remaining requested standalone measurements: **50**. Rows without a usable CPU median, if any, are not credited in this count. The two repaired-source observations above are separate controls and do not substitute for baseline rows. The 75-minute time box was retained; no extension or waiver of the ten-per-cell request was received. This is incomplete historical sampling, not a completed distribution or a measured p95. The original Windows 8.85% event was not causally reproduced, so Windows confirmation remains owed on a later authorized cut. The live pinned Windows runs were untouched.

The supported diagnosis remains (A): an inherited measurement/scheduling flaw, with no evidence of a recent recorder hot-path regression (B). The source change removes TUnit peers from the process CPU interval while preserving the workload, flush CPU, all nine pairs, strict 5% CPU and 10% wall medians, finite-ratio guards, and existing rejection controls. External-only saturation did not reproduce the native failure in the observed Linux rows; this distinction is part of the result, not hidden as a production-overhead fix.

Cold historical build results (slot wait recorded separately in each log):

- rc21: elapsed 00:07:13.10, errors 0; /proc/loadavg before 3.43 5.18 7.62 2/2984 32633, after 4.79 8.89 8.86 4/2971 12551. Log: `/tmp/cc5b4299-evidence/rc21-build.log`.
- rc22: elapsed 00:06:07.69, errors 0; /proc/loadavg before 23.06 16.30 12.02 5/2955 22314, after 6.44 10.59 10.98 2/2878 23378. Log: `/tmp/cc5b4299-evidence/rc22-build.log`.

Reproduction uses the frozen SHA, an independent native build through the host gate, and one exact filter per fresh results root: `/*/*/TestTimingChokePointTests/C1514T_Overhead`. The retained helper `/tmp/cc5b4299-evidence/run-one.ps1 -Root <frozen-checkout> -Label <fresh-unique-label> -Mode ambient|loaded` wraps `dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false` through `scripts/build-slot.ps1`, records the raw JSON/TRX/CPU ratios and load, and joins owned hogs. Do not reuse an existing label. Completing the missing matrix needs a new time budget; it does not require changing the CPU threshold or the estimator. Once the commissioner resolves that incomplete sampling gate, the repair is test-only and follows the brief's Merge path.

Evidence retained in `/tmp/cc5b4299-evidence`: full build logs, fresh per-row TRX and raw nine-pair JSON, per-row receipts with SHA and load, PID custody/join records, deterministic regeneration outputs, and guard/selector results. Essential observations and costs are preserved here; generated payloads were not added to Git.
