# CARD-1851 Code evidence (Final, Linux)

Both assigned defects are repaired. Cumulative required Linux selections: **83 passed,
0 unresolved failures**. **17 Windows argument rows remain pending**, plus the caller-owned
canonical desktop call-operator restart. No native Windows or mutation acceptance is claimed.

Original Code / landing owner: `2eeb94f5-6f28-4c2c-8b5a-f53a505460f7`.
Branch: `feat/card-task-2eeb94f5`. Worktree: `/work/worktrees/task-2eeb94f5`.
Base: `aa5e14b61b4df4be16afbbc9b1d81ce49de5bc58`.
Production repair: `bd71f33edb5a582ff027a82fc22351a951d084d2`.
Latest tested code/test SHA: `d3840e0d1ce302d58f47d742d905d2a1ed7f5e23`.
The later report-only commit does not replace these actual tested SHAs.

Plan: `docs/superpowers/plans/2026-11-12-card-1851-script-closures-plan.md`.
Unbound pending pack: `docs/superpowers/mutations/2026-11-12-card-1851-script-closures.md`.
All meaningful slices were committed and pushed on the assigned branch. No source was edited
while a checkpoint run was in flight. Restart: **none**; no live host/session control was run.

## Changes and outcomes

The PID reader catches only `FileNotFoundException`. Directory/permission, exclusive-lock,
missing-parent and malformed/overflow/zero PID errors propagate before restart controls.
The shared reader is tested for both supervisor and service-wrapper files. Missing-file and
live child PID controls retain their expected restart paths. Process identity lookup behavior
outside PID-file reads is unchanged.

All three nightly API closures capture their HTTP, conversion and due-time helpers as variables.
The due-time helper accepts the real captured timezone as an optional parameter so its transitive
script-local lookup also works under `&`. Ordinary callers retain default London zone resolution.
Tests fake only the HTTP cmdlet and assert actual registration/schedule/job conversion, request
shape, five-fetch cap, result failure handling and summer/winter UTC. Helpers stay absent globally.

| ID | Actual outcome |
|---|---|
| V-1 | PASS on pwsh/Linux: nine PID states, including seven refusal cases with zero control calls and two positive controls; nine PowerShell 5.1 arguments pending |
| V-2 | PASS on pwsh/Linux: both London dates and all three API closures; two PowerShell 5.1 arguments pending |
| R-1 | PASS: four existing executable compatibility cases; five PowerShell 5.1 rows and one pwsh native TCP-owner row pending |
| R-2 | PASS: Antiphon ProcessSpawnLimitTests 57, LiveLaneRunnerTests 4, NightlyCoreTests 1, SessionRunner ProcessSpawnLimitTests 2, Pty ProcessSpawnLimitTests 2 |
| R-3 | PASS: C544_ProductionJobAdapter and C545_JobResultFetch, one result each |
| V-3 | ASCII byte checks: zero non-ASCII bytes in both production helpers and nightly entry/common. OS declaration/lint complete at tested SHA, native obligation pending. Evidence history check clean after unshallow fetch; merge-tree clean at tested SHA. Full final-tip checks are repeated after publishing this report. |

The full base..HEAD impacted-class resolution selects `nightly-live-lane`, both named nightly
classes, the touched compatibility class and ProcessSpawnLimitTests in all three projects.
All are covered. The brief's minimal-testing exception omits the policy guard union because
no rostered policy test was touched. No whole Unit lane, assembly sweep or deliberate PC ran.

## Historical failures and costs

- Initial tool bootstrap was gated (`slot=granted waited=0s`, 17 seconds) in native default
  output. Plan import refused the original OR operands before any row ran; two small pushed
  plan corrections added the required operand wildcards. No product test failed in that refusal.
- `20261112-213014-8514`, SHA `61269247655320ba4805e132f825401ca7794855`:
  13 fixture failures, 15 skips, because the shell resolver refuses the host's symlinked PATH
  entry. This is infrastructure evidence, not the intended product red. Use only child
  `PATH=/opt/powershell:$PATH TEMP=/tmp` here; host PATH was not changed.
- `20261112-213245-6a92`, same SHA: zero tests; refused the previous retained build-source
  stamp. Evidence/outputs were retained; a new committed output name was used, never deleted
  or overwritten to bypass custody.
- Intended red `20261112-213350-5e3e`, SHA `04e4e8977a4be3f7cd5416b3855548ae4b887a81`:
  6 passed, 7 failed, 15 skipped. Five PID errors actually reached delete-pids/start-task;
  both nightly cases actually failed the script-local HTTP helper lookup. Production files
  at this SHA are byte-identical to the task base, confirming the assigned inherited defects.
- `20261112-213801-5088`, SHA `633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b`:
  CP-1 11 passed, 2 failed, 15 skipped; CP-2 62 passed; CP-3/4/5 each 2 passed. The two nightly
  failures exposed a new fixture error: PowerShell wildcard switch also matched the generic
  result arm after the missing-result arm. A committed `break` repaired that fake without
  loosening any assertion. Production was already repaired. Wall 588.697 s, no 30-minute run.
- Final `20261112-214826-e631`, SHA `d3840e0d1ce302d58f47d742d905d2a1ed7f5e23`:
  CP-1 15 passed, 17 skipped; CP-4 2 passed. Wall 188.620 s. The repair also added zero PID and
  unreadable-wrapper arguments. CP-2/3/5 stay at their actual prior SHA; their implementation
  and fixtures are unchanged. No repeat was run after final green.

Every checkpoint build/row held a granted slot and reported `waited=0s`; all leases released.
Builds were serial, and Antiphon.Tests/Pty.Tests did not co-run. Latest CP-1 build 64.268 s,
execution 51.789 s; CP-4 execution 3.763 s. Earlier CP-2 build 210.876 s/execution 59.385 s;
CP-3 execution 14.477 s; CP-5 build 84.134 s/execution 4.558 s. Full raw phase/timing records
are retained with each report/TRX. New test case setup/body/teardown and filesystem subdivisions
without spans are unknown/remainder. No timeout was increased and no assertion loosened.

The checkpoint tool was the closed-list driver. The only extra build was its gated bootstrap.
Read-only lint, source audits and receipt validation are not additional test selections.
The infrastructure rerun and the two genuine fix-driven reruns are recorded above; no loaded
repetitions, flake claim or exception to repeat-proof limits was used.

## Provenance and certificate limitations

Checkpoint wait verified each terminal report binding. Original CHECKPOINT lines below retain
their full actual SHA, source-state and build-source tokens. Fresh TRX inspection confirmed
each named class/method and nonzero counts. The accompanying ignored provenance audit compares
each executable row's source fingerprints and SHA with its raw TRX counts; it never edits receipts.

Strict `validate` accepts final CP-4 at `d3840e0d1ce302d58f47d742d905d2a1ed7f5e23`.
It refuses CP-1 with `row_failed` because of the 17 native skips, despite zero failures.
It refuses selected CP-2/3/4/5 from the earlier aggregate with `report_failed` because that
aggregate includes the repaired CP-1 fixture failure. These limitations are preserved:
**no complete zero-skip aggregate certificate or reviewedSourceClean assertion is claimed**.
Ordinary Review must establish its own required source-qualified gate at the reviewed tip;
native rows remain the orchestrator's explicitly assigned obligation.

At tested SHA the OS lint records the unchanged pre-existing Windows TCP-owner skip at line 39
in base/head as non-behavior. Real file IO and real TimeZoneInfo are driven through production
factories. Its broad startup-path census includes `scripts/lib/nightly-health.ps1`; native script
qualification is pending. No loaded server/runner binary changes or restart was performed.

Manual history checking initially refused `shallow_or_unknown_history`; a normal filtered
`git fetch --unshallow origin master` resolved it. At tested SHA evidence-diff reported
7 commits, 0 entries, 0 violations; merge-tree returned `94a43d071146c912e96b905e720ab4b6e9130f26`
against origin/master `aa5e14b61b4df4be16afbbc9b1d81ce49de5bc58`. Final report delivery records
the full-range post-report-commit check and exact remote branch tip separately.

## Pending Mutation and native work

PC-1/swallowed-read; PC-2/registration-http-name, schedule-http-name, jobs-http-name,
result-http-name; PC-3/conversion-name, due-name, timezone-name: **all eight variants pending**
for method-scoped SourceLanding Mutation, including missing-control discovery. No mutant cycle
is credited by ordinary regression red/green or nightly results.

Caller owes native Windows CP-1 at the reviewed/published source (16 PowerShell 5.1 arguments
plus one pwsh native TCP case skipped here), and canonical desktop `&` restart acceptance.
Use current runner defaults/catalogue for that Windows-only placement. This task read both
routes and did not dispatch or pin a host. Proceed to ordinary Review; original Code ID above
remains the landing owner. The caller commissions SourceLanding Mutation after publication.

## Original checkpoint receipts

The following lines are copied verbatim from the retained tool report files, including failures.

### Run 20261112-213014-8514

~~~text
CHECKPOINT CP-1 commit=61269247655320ba4805e132f825401ca7794855 build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=13 passed=0 failed=13 skipped=15 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-213014-8514/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=61269247655320ba4805e132f825401ca7794855 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=23.665s wall=23.710s
PHASES CP-1 slotWait=0s build=77.3053117s startup=2.0467463s testsWall=20.1480278s teardown=0.6942206s hostWall=22.8890053s
BUDGET wall=115.055s execution=115.054s admissionOnly=0.001s
~~~

### Run 20261112-213245-6a92

~~~text
CHECKPOINT CP-1 commit=61269247655320ba4805e132f825401ca7794855 build=failed filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-build-source dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-1 waited=0s executed=0.000s wall=0.000s
BUDGET wall=12.131s execution=12.131s admissionOnly=0.000s
~~~

### Run 20261112-213350-5e3e

~~~text
CHECKPOINT CP-1 commit=04e4e8977a4be3f7cd5416b3855548ae4b887a81 build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=13 passed=6 failed=7 skipped=15 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-213350-5e3e/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=04e4e8977a4be3f7cd5416b3855548ae4b887a81 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=56.415s wall=56.465s
PHASES CP-1 slotWait=0s build=50.6882191s startup=2.687165s testsWall=52.2946931s teardown=0.5699272s hostWall=55.5517983s
BUDGET wall=122.647s execution=122.647s admissionOnly=0.001s
~~~

### Run 20261112-213801-5088

~~~text
CHECKPOINT CP-1 commit=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=13 passed=11 failed=2 skipped=15 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-213801-5088/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=62.090s wall=62.131s
PHASES CP-1 slotWait=0s build=74.7283932s startup=2.5061121s testsWall=58.2660491s teardown=0.5267751s hostWall=61.2989478s
CHECKPOINT CP-4 commit=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-213801-5088/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=4.890s wall=4.948s
PHASES CP-4 slotWait=0s build=0s startup=3.1898248s testsWall=0.1189876s teardown=0.7970453s hostWall=4.1058576s
CHECKPOINT CP-2 commit=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b build=ok filter=/*/*/(LiveLaneRunnerTests*)|(NightlyCoreTests*)|(ProcessSpawnLimitTests*)/* executed=62 passed=62 failed=0 skipped=0 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-213801-5088/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=59.385s wall=59.434s
PHASES CP-2 slotWait=0s build=210.875915s startup=8.8105641s testsWall=48.9680531s teardown=0.7312239s hostWall=58.5098412s
CHECKPOINT CP-3 commit=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b build=reused filter=/*/*/NightlyVerificationContractTests/(C544_ProductionJobAdapter*)|(C545_JobResultFetch*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-213801-5088/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=14.477s wall=14.527s
PHASES CP-3 slotWait=0s build=0s startup=7.5769753s testsWall=5.2176332s teardown=0.6951206s hostWall=13.4897296s
CHECKPOINT CP-5 commit=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-213801-5088/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=633c599f7b4ebaf3bbf747fe7a31f7253f4ca30b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=4.558s wall=4.607s
PHASES CP-5 slotWait=0s build=84.1337906s startup=2.8221133s testsWall=0.0072318s teardown=1.0261137s hostWall=3.855459s
BUDGET wall=588.697s execution=588.691s admissionOnly=0.006s
~~~

### Run 20261112-214826-e631

~~~text
CHECKPOINT CP-1 commit=d3840e0d1ce302d58f47d742d905d2a1ed7f5e23 build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=15 passed=15 failed=0 skipped=17 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-214826-e631/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=d3840e0d1ce302d58f47d742d905d2a1ed7f5e23 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=51.789s wall=51.860s
PHASES CP-1 slotWait=0s build=64.2677239s startup=2.9701512s testsWall=47.2549092s teardown=0.61762s hostWall=50.8426823s
CHECKPOINT CP-4 commit=d3840e0d1ce302d58f47d742d905d2a1ed7f5e23 build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-2eeb94f5/.antiphon/checkpoints/20261112-214826-e631/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=d3840e0d1ce302d58f47d742d905d2a1ed7f5e23 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=3.763s wall=3.816s
PHASES CP-4 slotWait=0s build=0s startup=2.587145s testsWall=0.0741569s teardown=0.4412787s hostWall=3.1025804s
BUDGET wall=188.620s execution=188.618s admissionOnly=0.002s
~~~
