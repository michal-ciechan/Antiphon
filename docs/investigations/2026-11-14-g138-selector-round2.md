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
