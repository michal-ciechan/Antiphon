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

Pending.
