# g138 selector expectation repair, round 2

Debug `93af5925`, Linux runner, 2026-11-14. Starting master:
`21c9f51b4a01c3bd07547c46a5e6da4e58e6c243`. The assigned branch keeps that
history and cherry-picks the three original Debug `abd9b404` commits with `-x`.
Their historical results remain in the original C1620 investigation.

## Required red before the expectation update

Clean cherry-picked source `0c335d3878345f56ef76e036a88b63d70f789a02`
builds with 0 errors and 2067 warnings (249.06 seconds MSBuild, 45 seconds
build-slot admission). The exact method
`ImpactedClassesSelectorTests.C1801_why_missed_paths_select_their_classes_and_windows_filters`
then fails: 0 passed / 1 failed / 0 skipped, exit 2, 9.901 seconds framework
wall, 14.029 seconds wrapper wall, zero slot wait.

The full emitted Antiphon.Tests filter differs from `WhyMissedTestsFilter`
only by `|(TestTimingOperationLabelTests*)`, between `TestSelectionTests*`
and `TimeoutAuditTests*`. The emitted SessionRunner filter equals
`WhyMissedSessionRunnerFilter` exactly. This confirms the intended new fixture
selection rather than a selector defect. Retain the full-string tuple checks.

The red ran with empty tracked status before and after, identical start/end
SHA, and a granted slot. Load before: `17.41 20.16 20.54 4/3815 14290`;
after: `15.66 19.63 20.36 12/3887 17917`. Initial host load was
`15.61 16.74 19.55 5/4197 13223`.

Ignored evidence is under `/work/worktrees/task-93af5925/.antiphon/g138-round2/`:
`selector-red.log`, `selector-red.json`, `selector-red/run.trx`, phase journals.
The initial build log is `.antiphon/g138-round2-red-build.log` and the full
selector response is `.antiphon/g138-why-missed.json`.

Each subsequent build/test takes its own build-slot lease; test rows run
serially, with native default build outputs and `UseAppHost=false` consistently.
Retain counts, per-test durations, exact filters, source/load/status metadata
and phase journals. Slot admission is separate from execution; absent phases
and unmeasured external detail remain unknown. Each execution row is budgeted
below ten minutes, using the original investigation's loaded-host measurements
as provisional guidance. No production timeout or live run is changed.

## Repair and verification outcome

The repair adds `|(TestTimingOperationLabelTests*)` in the selector's emitted
position in `WhyMissedTestsFilter`. The assertion still pins the entire filter.
The SessionRunner expectation needs no change, confirmed both by exact comparison
of the emitted tuple and the complete 42-case selector run.

Verified source: `97f8c9970396fd30f270f27fdb5e0626652466c8`.
Every final row recorded identical before/after HEAD and empty tracked status.
The final build passed with 0 errors and 2067 warnings: 285.11 seconds MSBuild,
287.958 seconds wrapper wall, zero slot wait. All drivers obtained their own
granted lease. No source changes were made during builds or tests.

**Final results: 263 passed / 0 failed / 1 skipped.** The required baseline
red above is additional and remains recorded. All execution rows were below ten
minutes; no full assembly or unrequested broad selection was run.

| Evidence row | Class/selection | P/F/S | Framework wall seconds | Wrapper wall seconds | Slot wait seconds |
|---|---|---|---:|---:|---:|
| selector-green | ImpactedClassesSelectorTests* | 42/0/0 | 232.018 | 274.369 | 30 |
| labels | TestTimingOperationLabelTests* | 2/0/0 | 6.503 | 40.481 | 30 |
| host-api | C1514HostLoadApiTests | 4/0/0 | 61.092 | 124.702 | 60 |
| host-history | C1514HostLoadHistoryTests | 22/0/0 | 236.433 | 239.886 | 0 |
| host-script | C1514HostLoadScriptTests | 4/0/0 | 83.415 | 87.612 | 0 |
| host-aggregate | C1514HostLoadAggregateTests | 3/0/0 | 5.559 | 10.108 | 0 |
| host-migration | C1514HostLoadMigrationTests | 9/0/0 | 147.954 | 152.372 | 0 |
| host-recommendation | C1514HostLoadRecommendationTests | 5/0/0 | 6.056 | 10.893 | 0 |
| host-scenario | C1514HostLoadScenarioTests | 2/0/0 | 6.609 | 11.835 | 0 |
| spawn-limiter | ProcessSpawnLimitTests* | 57/0/0 | 16.324 | 23.639 | 0 |
| guards | Exact requested guard union below | 113/0/1 | 335.079 | 343.039 | 0 |

The seven host-load classes ran alone, serially, totaling **49/49**. The guard
skip is `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`:
its child skip-roster audit requires native Windows. The earlier investigation's
C1210 roster-closure failure is absent on this current master base; this repair
does not change its roster. No claim of native Windows execution is made.

Host-load journals preserve the three labels: `host-load world initialization`
(35 spans), `host-load 100 durable receipts` (18), and `host-load cancel and join world`
(35). Aggregate `droppedPhaseCount` is zero. Eight fixture records with no test ID
carry the recorder's `outcome=incomplete` value while `incomplete=false`; they
are distinct from the 49 passing TRX test outcomes. Unobserved external details
remain unknown, not zero. Durations are loaded-host observations, not p95 claims.

### Generated index and pins

The dedicated `Antiphon.DerivedImpactIndex` driver built first: 0 errors,
0 warnings, 3.81 seconds MSBuild, 6.024 seconds wrapper wall. Both no-build
generations printed `derived-impact-index unchanged` (36.777 and 42.363 seconds
wrapper wall; zero admission). No index amendment was needed.

Recomputed pins both match master, so no re-pin commit was needed:

- policyHash: `ecc5d4fabc326645bf8b7e7970cdc22c1a9d195a83e737a66ccf157f9cb0c0c1`
- PinnedInputDigest: `67e50252fffbb65df7cf45d9187bb8a9cdaaec9ef12333103b883c60f313f3b8`

The initial ad hoc pin-reader command omitted `nightly-common.ps1`; its failure
is retained as `pins.log`/`pins.json`. Correcting the command's imports passed
(`pins-complete-imports`, exit 0). This was a command setup error, not a source
or policy-hash failure.

### Reproduction and evidence

Use one lease per invocation. All test rows use this common form, with the
table's class and row substituted literally; the JSON metadata retains the
complete executed argv:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g138-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g138-selector -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/ImpactedClassesSelectorTests*/*' --maximum-parallel-tests 1
```

The exact guards filter was:

```text
/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/*
```

Each row under the ignored absolute evidence root above has `<row>.log`,
`<row>.json`, `<row>/run.trx` and `<row>/timing/`. JSON records exact commands,
source SHA, clean tracked status, wall time, and `/proc/loadavg` before/after;
TRX records exact membership and per-test durations. Peak sampled final-build
load was `46.51 29.92 23.95 7/4403 28554`; the guard row ended at
`12.08 20.57 20.71 8/3842 21742`. These are task-owned targeted checks on the
oversubscribed runner; no live host, Windows qualification or service was touched.

### Commit provenance and merge handoff

Original source commits were each cherry-picked with `-x`, committed and pushed:

- `29a88f16136b359dd2d4f4222124820210e66527` -> `1874ccabddd80fd40671656249c0989f535a035e`
- `8f9341a8999f3e3269a0eaa7eda9b15e82f1af3c` -> `26757ce6a6c09e60eafd6f87400d5941a387da67`
- `d144f8d25931506878e14c5a240af9a157d1d52f` -> `0c335d3878345f56ef76e036a88b63d70f789a02`

The round-two red was recorded and pushed at
`fb03da07ecc61fcba7f2e7c7f9c41ff494ce241b` before the one-line expectation
fix at `97f8c9970396fd30f270f27fdb5e0626652466c8`.

A fresh `git fetch origin master` still observes
`21c9f51b4a01c3bd07547c46a5e6da4e58e6c243`. The pre-report
`git merge-tree --write-tree origin/master HEAD` succeeds without conflicts,
tree `6189a2d66a086dfe22b15a2b13f066088aca45a0`. The final amendment only
records these results; executable source is unchanged from the verified SHA.
Proceed under the caller's standing repair authority with `next: merge`.
