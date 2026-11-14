# C1620 host-load timing labels omitted from recorded phases

Debug `abd9b404`, 2026-11-14. Baseline: fetched master
`f701b2ba967d503427d7064fe4fffa17b8c1738d`, also the assigned branch's starting tip.
The Windows qualification report is commissioning evidence only; this task executes
on Linux and does not touch run `34d41b7e`, the desktop, or production services.

## Failure and cause

The baseline exact method
`TestTimingOperationLabelTests.C1620_EveryCallSiteOperationLabelIsKept` executes
one test and fails with exactly three dropped labels:

- `host-load world initialization`
- `host-load 100 durable receipts`
- `host-load cancel and join world`

The spans still exist in `C1514HostLoadWorld.InitializeAsync`, `SeedPrefixAsync`,
and `DisposeCoreAsync`. The recorder's `TestTimingSession.OperationLabels`
allowlist lacks the literals, so `BoundOperation` records `omitted`. This is
missing admission of new static instrumentation, not removed instrumentation or
a stale expectation. Keep the same labels and the strict existing test; admit
the three exact literals without allowing arbitrary operation text.

`git log -S 'host-load world initialization'` identifies
`6a4bea739d2e66e1f2da2f7bbcb65ca9ff2803d2` (original Debug commit
`74a4543adedd71365027c1343334f108cf4e9e46`) as the introducing change. Its diff
adds all three `BeginPhase` calls and does not change `tests/Shared/TestTiming.cs`.
Adjacent-commit execution confirms the boundary: parent
`ca3dff3c7d13862cb0b6132c97ab9af506ca468b` passes 1/1; this commit fails 1/1
with exactly the same three dropped labels. Each detached historical worktree
was built once and ran only the exact C1620 method. `git bisect start
--no-checkout <bad> <good>` with these tested adjacent bounds reports this SHA
as first bad. The bisect was reset without changing the assigned branch.

| Historical row | Passed / failed / skipped | Driver wall seconds | Slot wait seconds | Build/run seconds |
|---|---|---:|---:|---:|
| Parent build | 0 errors, 2091 warnings | 274.986 | 0 | 272.69 |
| Parent exact method | 1 / 0 / 0 | 54.260 | 45 | 5.448 |
| First-bad build | 0 errors, 2063 warnings | 218.354 | 0 | 216.47 |
| First-bad exact method | 0 / 1 / 0 | 36.039 | 15 | 13.089 |

Historical build/load metadata and all nonzero test results are retained under
`.antiphon/debug-abd9b404/`; no historical failure is replaced by a later green.

## Why Merge 221 missed it

Read-only task detail for Merge `4d6af62a` supplies the full stored report and
brief. It picked ten Debug `7bd634da` commits onto master, from
`ca8737e78170c9a441e216408da5fab636c34ca0` through
`e6c46693aa58766d73d323e37acd91fae31c2587`. The latter SHA is the final
documentation commit, not the introducing code change.

The recorded merge selection was:

- `C1514HostLoadHistoryTests`, `C1514HostLoadApiTests`,
  `C1514TaskLoadWindowTests`, `C1514HostLoadScriptTests`, twice: 37/37 each.
- `C1514HostLoadScenarioTests`: 2/2.
- Policy batch: `TestClassificationPolicyTests`, `TestClassificationGuardTests`,
  `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`,
  `NativeLinuxPolicyWitnessTests`: 53/53.
- Other guards: `DerivedImpactIndexTests`, `MechanicalPcContractTests`,
  `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`,
  `TestLaneCategoryGuardTests`: 54/54.

The merge report also records a solution build, two unchanged index generations,
pin comparison, land-graph and evidence-diff checks. It does not record
`TestTimingOperationLabelTests`. Passing the fixture's behavior tests cannot prove
its labels survive recorder admission: recording is opt-in, and those assertions
exercise receipts and lifecycle behavior rather than serialized operation names.
The guard union checks other invariants and does not include the timing-label
allowlist witness. Consequently its green results did not detect this omission.

The existing `timing-labels` impacted row already watches
`tests/Shared/TestTiming.cs` and added `BeginPhase(`/`BeginFixturePhase(` calls
under `tests/**`. The introducing diff adds matching calls. The stored report
contains no impacted-row output or execution of the selected timing witness;
the miss is an omitted impacted selection, not proof that the selector could
not identify the added calls.

The cheapest pre-land check is the two-test `TestTimingOperationLabelTests`
class. Extend the existing row with
`tests/Antiphon.Tests/**/C1514HostLoad*.cs` so edits to these fixtures select it
even when they move, remove, or rename existing instrumentation without adding
a matching line. Keep `tests/Shared/TestTiming.cs` and the generic added-call
detector. Run `scripts/impacted-classes.ps1` on the complete landing range and
execute its impacted rows alongside the hand-selected behavior tests.

## Baseline execution

All drivers take separate granted build-slot leases, use native default output
and `UseAppHost=false`; no own runs overlap. Evidence is ignored under
`.antiphon/debug-abd9b404/` (TRX, command metadata, full logs, and load samples).
The exact C1620 baseline: 0 passed / 1 failed / 0 skipped, driver exit 2,
6.498 s framework wall, 10.783 s driver wall, slot wait 0 s.
Baseline build: 0 errors, 2067 warnings, 209.19 s MSBuild, 227.675 s driver wall,
15 s admission. Baseline build load: `14.74 19.65 19.24 31/4094 20973`
to `17.17 19.78 19.47 6/3800 11705`; exact-test load:
`15.06 19.25 19.29 3/3862 12018` to `13.58 18.79 19.15 3/3822 12424`.

The same unchanged baseline also fails
`WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates`:
0 passed / 1 failed / 0 skipped, driver exit 2, 17.275 s framework wall,
21.336 s driver wall, slot wait 0 s. The unrostered
`VolumeRetirementContentTests` finding is inherited and belongs to Debug
`6ebbcb6e`; this repair does not alter its roster or pins.

## Verification scope and budget

Run the exact label method at each adjacent historical commit after one build
per commit, then the complete label class and recorder/lifecycle classes after
repair. Run C1514 host-load classes serially, with the four shared-server
classes alone; retain `NotInParallel("C1514HostLoadServer")`. Run the requested
guard union and impacted-selector checks. Each row is budgeted below ten
minutes of execution; slot admission is separate. Historical merge C1514 runs
were 5m39s and 8m21s; split unrelated host-load classes into separate rows.
No timeout, assertion, production behavior, or live qualification is changed.
Per-test durations and membership are in TRX. Enable timing journals for repair
runs; absent baseline phases and unobserved external-call detail remain unknown,
and wall time is distinct from summed nested spans. These are targeted Debug
checks, not full release qualification or native Windows evidence.

## Repair verification

Complete for C1620: the strict label witness and all named timing/host-load
checks pass. The requested guard union remains red only on the independently
reproduced inherited C1210 roster closure. This is not an all-green guard verdict.

All seven TUnit rows verified clean, unchanged source at
`8f9341a8999f3e3269a0eaa7eda9b15e82f1af3c`, tree
`2bb1d2490b93dca4bf0c2d77dc5cc626b4d44eab`. Command metadata records an empty
tracked status before and after every row and identical start/end HEADs.
Only this narrative report changes afterward. Baseline evidence was committed
first at `29a88f16136b359dd2d4f4222124820210e66527`; both slices were pushed.

Repair build: 0 errors, 2067 warnings, 264.06 s MSBuild, 265.755 s driver wall,
0 s slot wait. Driver and both historical builds also passed. The dedicated
`Antiphon.DerivedImpactIndex` driver was built before generation (0 errors,
0 warnings); both successive generations printed `derived-impact-index unchanged`.
Consequently no generated-index commit or nightly re-pin was needed.

Totals below exclude baseline and historical experiments: **198 results,
196 passed, 1 inherited failure, 1 native-Windows skip**. All execution rows
were below ten minutes. Admission was separately recorded; selector-tests
waited 45 s, the other repair TUnit rows waited 0 s.

| Row | Passed | Failed | Skipped | Framework wall | Driver wall seconds | Slot wait seconds |
|---|---:|---:|---:|---|---:|---:|
| labels | 2 | 0 | 0 | 6s 952ms | 13.73 | 0 |
| recorder | 19 | 0 | 0 | 13s 921ms | 19.275 | 0 |
| choke | 4 | 0 | 0 | 4m 31s 807ms | 277.225 | 0 |
| hostload-server | 38 | 0 | 0 | 6m 01s 807ms | 369.646 | 0 |
| hostload-other | 19 | 0 | 0 | 1m 15s 311ms | 79.801 | 0 |
| guards | 112 | 1 | 1 | 4m 49s 029ms | 292.827 | 0 |
| selector-tests | 2 | 0 | 0 | 46s 011ms | 100.236 | 45 |

Per-class membership and counts (passed/failed/skipped):

| Row | Class | P/F/S |
|---|---|---|
| labels | TestTimingOperationLabelTests | 2/0/0 |
| recorder | TestTimingLifecycleTests | 1/0/0 |
| recorder | TestTimingRecorderTests | 18/0/0 |
| choke | TestTimingChokePointTests | 4/0/0 |
| hostload-server | C1514HostLoadApiTests | 4/0/0 |
| hostload-server | C1514HostLoadHistoryTests | 22/0/0 |
| hostload-server | C1514TaskLoadWindowTests | 8/0/0 |
| hostload-server | C1514HostLoadScriptTests | 4/0/0 |
| hostload-other | C1514HostLoadAggregateTests | 3/0/0 |
| hostload-other | C1514HostLoadMigrationTests | 9/0/0 |
| hostload-other | C1514HostLoadRecommendationTests | 5/0/0 |
| hostload-other | C1514HostLoadScenarioTests | 2/0/0 |
| guards | MechanicalPcContractTests | 11/0/0 |
| guards | TaskPlatformGuidanceTests | 5/0/0 |
| guards | C1729LayeringGuardTests | 10/0/0 |
| guards | SlowTestRegistryMergeTests | 5/0/0 |
| guards | DerivedImpactIndexTests | 27/0/0 |
| guards | WindowsUnitPlatformAdmissionTests | 5/1/1 |
| guards | SlowTestTripwireTests | 2/0/0 |
| guards | TestClassificationPolicyTests | 42/0/0 |
| guards | TestLaneCategoryGuardTests | 1/0/0 |
| guards | NativeLinuxPolicyWitnessTests | 3/0/0 |
| guards | TestClassificationGuardTests | 1/0/0 |
| selector-tests | ImpactedClassesSelectorTests | 2/0/0 |

The current server row has 22 history cases, one more than Merge 221's historical
21; its total is therefore 38 rather than the historical 37. All four shared-server
classes ran alone in one process, retaining their session-shared fixture and
`NotInParallel("C1514HostLoadServer")`, with `--maximum-parallel-tests 1`.
The other host-load row also used one test at a time.

The sole failure is
`WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates`:
`c1210-unrostered-gate: Antiphon.Tests.Scripts.VolumeRetirementContentTests.Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent`.
The unchanged baseline produces this same message. It remains owned by Debug
`6ebbcb6e`; no roster assertion was loosened here. The skip is
`C1210_Windows_linux_only_outcomes_are_exact`, which needs native Windows.

### Recorded operation names

The completed shared-server journals contain 38 complete case rows and
`droppedPhaseCount=0`. All three names are preserved. Durations below
are sums of individual recorded spans, not process wall time or a p95 sample.

| Operation | Complete spans | Incomplete spans | Summed milliseconds | Maximum milliseconds |
|---|---:|---:|---:|---:|
| host-load world initialization | 43 | 0 | 9493 | 2796 |
| host-load 100 durable receipts | 23 | 0 | 131287 | 21423 |
| host-load cancel and join world | 43 | 0 | 2717 | 177 |

Per-test/group durations and membership remain in TRX; phase journals and trace
JSONL are retained beside each run. The choke-point overhead experiment uses its
own instrumentation controls, with the extra trace-file sink disabled to avoid
changing its measurement. TUnit reports its 100-external-span cap in the host-load
output; this does not truncate the separate operation journals above. Unobserved
external-call phases remain unknown, not zero. These single loaded-host runs are
not a newly measured p95 and introduce no new timeouts.

### Selector and publication checks

The shipped selector replay on the original Merge 221 range
`ca8737e78170c9a441e216408da5fab636c34ca0..e6c46693aa58766d73d323e37acd91fae31c2587`
selected the `timing-labels` kind and `TestTimingOperationLabelTests`, with no
owed or capped classes. The replay used the first-bad checkout's shipped table
and selector; the original stored merge report independently records what ran.

The repaired table also passed three path-only checks, each selecting this kind
and class without relying on added diff lines:

- `tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs`
- `tests/Antiphon.Tests/Application/C1514HostLoadHistoryTests.cs`
- `tests/Shared/TestTiming.cs`

Existing `ImpactedClassesSelectorTests.seed_kinds_globs_and_owner_sentences_stay_present`
and `label_remote_budget_and_checkpoint_ranges_select_their_rows` passed 2/2,
including the table audit and historical timing-label ranges.

A fresh fetch still placed origin/master at the baseline SHA. The pre-report
`git merge-tree --write-tree origin/master HEAD` check passed without conflicts,
producing tree `2bb1d2490b93dca4bf0c2d77dc5cc626b4d44eab`.
Both task-owned detached historical worktrees were clean and removed through
`git worktree remove` with exact literal absolute paths. Raw evidence stays
ignored in the assigned task worktree; essential results and provenance are
preserved in this committed report.

### Rerun commands

Build once, then select a row's exact filter below; every build and run takes
its own build-slot lease and uses the same properties and native default output.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1620-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1620-labels -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/TestTimingOperationLabelTests/*'
```

Exact filters executed (all under `tests/Antiphon.Tests`):

- labels: `/*/*/TestTimingOperationLabelTests/*`
- recorder: `/*/*/(TestTimingRecorderTests*)|(TestTimingLifecycleTests*)/*`
- choke: `/*/*/TestTimingChokePointTests/*`
- hostload-server: `/*/*/(C1514HostLoadHistoryTests*)|(C1514HostLoadApiTests*)|(C1514TaskLoadWindowTests*)|(C1514HostLoadScriptTests*)/*`
- hostload-other: `/*/*/(C1514HostLoadAggregateTests*)|(C1514HostLoadRecommendationTests*)|(C1514HostLoadMigrationTests*)|(C1514HostLoadScenarioTests*)/*`
- guards: `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/*`
- selector-tests: `/*/*/ImpactedClassesSelectorTests/(seed_kinds_globs_and_owner_sentences_stay_present)|(label_remote_budget_and_checkpoint_ranges_select_their_rows)`

### Load provenance

Full `/proc/loadavg` samples below include runnable/total process fields; the
host was oversubscribed and foreign activity was uncontrolled. Driver wall and
admission above are separate from framework execution and nested phase totals.

| Row | Start load | End load |
|---|---|---|
| base-red | `15.06 19.25 19.29 3/3862 12018` | `13.58 18.79 19.15 3/3822 12424` |
| base-inherited | `12.65 18.51 19.05 5/3816 12596` | `10.68 17.57 18.73 6/3814 13526` |
| bisect-good-test | `30.31 25.72 21.77 9/4114 18148` | `19.44 23.47 21.23 10/3885 29932` |
| bisect-bad-test | `34.18 26.37 22.71 23/4159 7016` | `35.06 27.49 23.23 9/4101 12373` |
| labels | `24.55 25.48 23.79 4/3945 13217` | `21.19 24.69 23.56 6/3905 2984` |
| recorder | `21.19 24.69 23.56 8/3911 3160` | `17.80 23.70 23.26 2/3749 31209` |
| choke | `17.80 23.70 23.26 4/3749 31241` | `44.43 32.51 26.81 17/4258 14492` |
| hostload-server | `44.43 32.51 26.81 15/4253 14524` | `11.17 21.92 24.52 6/3764 31163` |
| hostload-other | `11.17 21.92 24.52 6/3722 31207` | `8.94 18.58 23.13 6/3794 31835` |
| guards | `8.94 18.58 23.13 6/3794 31867` | `40.84 30.78 26.67 4/4252 4344` |
| selector-tests | `40.84 30.78 26.67 8/4252 4382` | `34.68 32.18 27.65 54/4135 14368` |

Standing authority covered this repair and publication to the assigned task
branch, so no further approval was requested. The test-only handoff is
`next: merge`; no production code, assertion, timeout, live host, or live run
was changed.
