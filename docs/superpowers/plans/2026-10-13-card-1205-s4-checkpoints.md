# CARD-1205 S4 independent recovery watchdog

Code/landing owner: 7b7d644a-a776-42e8-bc12-3d57e5df5ace.
Task base: 7f8448798955a24427bc2279138bf9fb64bc4195.
Scope: S4 only. S3 checked recovery and SQL generation fence remain unchanged.
The explicit brief excludes the broad Unit lane. S5 receipt tests are deferred.

Test-first baseline: the first production-host witness requires one registered
StaleWorkingTranscriptHostedService. Removing its Program registration is PC-57;
the expected baseline red is hosts.Length expected 1, actual 0. The baseline
does not yet prove boot reconstruction; that extension and the remaining frozen
T18..T35 cases are still implementation work.

Tool bootstrap is an extra setup build only when this worktree has no executable
checkpoint tool: build-slot gated, UseAppHost=false, bin-c1205-s4-tool/.
No native runner, deployment, live message or server restart is required.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| S4-BASE | Tests | `tests/Antiphon.Tests -> bin-c1205-s4-base/` | registration-baseline | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/C1205_Hosted_restart_discovers_working_sessions_immediately*` | PC-57 baseline only | Exactly one assertion red on unchanged pre-S4 production | 1 | 6 |
| S4-CASES-BASE | Cases | `tests/Antiphon.Tests -> bin-c1205-s4-cases-base/` | scheduling-baseline | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/*` | T18..T35 test-first | Exactly 48 executed; missing watchdog probes/options/logs assertion red on unchanged production | 48 | 12 |
| S4-T33 | S4 | `tests/Antiphon.Tests -> bin-c1205-s4/` | queue-fixture-repair | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/C1205_Recovered_idle_schedules_only_coalesced_work*` | V-33,R-3 unresolved T33 | Exactly 1 green before final full-class CP-4 | 1 | 6 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1205-s4/` | watchdog | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/*` | V-13..19,V-27..35,R-3,R-4 | Exactly 48 green; frozen T18..T35 roster | 48 | 12 |

All S4 PCs remain pending for method-scoped SourceLanding Mutation:
PC-41..83, PC-101..110 and any additional implementation variants.

## Blocked baseline, 2026-10-13

Test-only source e8e4948b2a5422e10e1a873affc86dd19d062580 was pushed before
the baseline invocation. The tool's bound owner admission returned exit 7:

```text
CHECKPOINT owner owner-unverified
```

No run directory, row build, test execution or TRX was created. S4-BASE is
**not run**, not an assertion red. CP-4 is not run. All assigned V/R outcomes
remain unverified. The production registration and scheduler are not implemented.
Do not land this test-only slice alone.

The bound task-detail GET timed out independently at 15 and 20 seconds. The
card, runner-defaults and session-runners reads succeeded. The checkpoint owner
guard uses a 12-second read deadline and its existing three-minute uncertainty
budget; no deadline was changed and no unbound bypass was attempted.

Bootstrap succeeded through build-slot (0 errors, one pre-existing CS8602 warning
in TaskOwnerGuard.cs), slot=granted, waited=0s. Its alternate output was
bin-c1205-s4-tool/. An earlier launcher with an incorrect expected SHA was stopped
before owner admission completed and before it created any checkpoint directory;
it is excluded from proof. The correct-SHA invocation above also launched no rows.

Resume Code after task-detail reads can validate the owning task/session. Rebuild
the disclosed tool output if needed and run --after Tests --serial with the exact
committed HEAD as --expected-source-sha. Establish the registration assertion red
before adding production code. Extend T26 to prove cold boot reconstruction and
complete all 48 frozen T18..T35 cases, their test-first evidence, production S4,
and CP-4. S5 remains outside this task.

## Resumed test-first group

The registration baseline ran on f3238dec1a8751f228ec89aba20bf269ca28f6f2:
one executed, one assertion failure (expected hosts.Length 1, actual 0), no skip.
Run 20261013-070708-9cf5 has clean source and verified build provenance.
The 48 frozen cases are now authored before S4 production changes. A temporary
card-local reflection adapter lets unchanged production exercise its real cache
warmup when the scheduler is absent. Missing recovery is observed through actual
HTTP counts, missing disabled logs and missing startup options rejection. It does
not fake a recovery result. Remove the adapter in the implementation group.
Independent cap/fairness/fence guards still require their pending Mutation PCs;
baseline absence of the feature is not an independent mutation proof for each guard.

## S4 implementation group

Test-first source 6979735a1bbe7e9ff16a1edf167193f8d234e06e ran all 48 cases:
48 assertion failures, zero passed/skipped, no fixture/build errors. Run
20261013-072438-ff86 retains exact failures and clean/verified source provenance.
The production group removes the temporary cache-warmup adapter and calls the
concrete scheduler. No S3 or queue-service source was changed.
The capacity fixture allows 275 explicit scheduling passes for 1025 sessions;
four workers require 257 admission rounds plus discovery/reconciliation pages.
This is the load term from the frozen design, not a timeout increase.
The cleanup fixture's cursor after three two-row discovery passes selects the
fifth tracked ID: its next confirmed terminal page removes one, leaving four.
Host log capture explicitly injects the recovery host logger because Program's
Serilog setup does not provide the same provider capture as the service fixture.

## First fixed-source CP-4 and repairs

CP-4 on 7adb09c79ac24982042e48d85638f0cbc5103623 executed 48: 46 passed,
two failed, zero skipped (run 20261013-073557-ba40). T26 reached both real
host epochs and both checked Working probes, then exposed repeated Stop after
service disposal. Shutdown now shares one owned completion task, with cancellation
outside the monitor and the probe join in its finally. T25 still requires joining
the non-cooperative save; T26 requires successful host/provider teardown.
T33 was missing the production queue's 20ms pre-Enter timer advance. It now waits
for that actual timer registration, advances exactly 20ms and observes the held
adapter submission. No timeout, production queue assertion or receipt rule changed.

The next CP-4 ran 48: 47 passed, only T33 failed; T25 and both real host epochs
now pass. Run 20261013-074506-49bc tested b9f34b5294c96080de4f68d11c97f380d032f215.
T33's timer had not been reached: the fixture lacked GET /sessions, used by the
real queue's local-liveness read before adapter delivery. The card-local handler
now returns its actual controlled runner inventory; the worker logger is captured
for diagnostics. S4-T33 is a disclosed method-scoped repair run. It does not replace
the required final full-class CP-4. No timeout or assertion is loosened.
Run S4-T33 and CP-4 serially as one committed group with their shared isolated
build: --rows S4-T33,CP-4 --serial --expected-source-sha HEAD. The method row
precedes the full class in the manifest and does not discharge any additional PC.
Both rows have After=S4 because the checkpoint importer requires matching After
for a shared build. The first mismatched-After attempt was refused before any driver.

## Final verification blocked by terminal task ownership

The T33 inventory fixture repair is committed, but not yet executed. The final
combined group on ab32d51ad1a71c87e3098993bfe2f07365c04f01 was refused before
creating a run or launching a driver:

```text
CHECKPOINT owner owner-ended
```

A direct task-detail read confirmed task
7b7d644a-a776-42e8-bc12-3d57e5df5ace has status **Failed**, while its bound
session 9d16f51d-6cc9-4fcb-a954-3f1bbb4eae4c has status **Running**.
The existing checkpoint TaskOwnerGuard rejects a terminal task even with a live
session. No token was cleared, no ownership binding was changed, and no unbound
bypass or build-slot test substitute was attempted.

Latest executed CP-4 remains 47 passed / 1 failed / 0 skipped at
b9f34b5294c96080de4f68d11c97f380d032f215. Fresh TRX inspection confirms all
18 intended methods with their frozen argument counts (48 executions).
V-13,V-14,V-15,V-16,V-17,V-18,V-19,V-27,V-28,V-29,V-30,V-31,V-32,V-34,V-35
passed on that source; V-33 failed at the missing queue fixture inventory/timer
path, and its committed repair is unverified. R-4 passed; R-3 is partial because
T33 remains unverified after repair. These are not a Final green on the current tip.
S4-T33: not run. Required final CP-4: not run on the repaired source.
No Unit lane or manual production/deployment operation was run.

Caller must provide a live bound Code owner before executing the two final rows.
Rebuild the disclosed checkpoint tool if its owned output has been cleaned, then
run --rows S4-T33,CP-4 --serial with exact committed HEAD as expected source SHA;
wait until exit is not 75, inspect 1/48 exact counts and repair any remaining red.
No implementation decision is requested. Next stage remains Code, not Review.
Restart: none. All S4 PCs and variants remain pending for SourceLanding Mutation.

### Retained unedited checkpoint lines

Source report: .antiphon/checkpoints/20261013-070708-9cf5/report.md
```text
CHECKPOINT S4-BASE commit=f3238dec1a8751f228ec89aba20bf269ca28f6f2 build=ok filter=/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/C1205_Hosted_restart_discovers_working_sessions_immediately* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-7b7d644a/.antiphon/checkpoints/20261013-070708-9cf5/rows/S4-BASE/run.trx slot=granted waited=15s dirty=0 source=f3238dec1a8751f228ec89aba20bf269ca28f6f2 sourceState=clean buildSource=verified
```

Source report: .antiphon/checkpoints/20261013-072438-ff86/report.md
```text
CHECKPOINT S4-CASES-BASE commit=6979735a1bbe7e9ff16a1edf167193f8d234e06e build=ok filter=/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/* executed=48 passed=0 failed=48 skipped=0 trx=/work/worktrees/task-7b7d644a/.antiphon/checkpoints/20261013-072438-ff86/rows/S4-CASES-BASE/run.trx slot=granted waited=30s dirty=0 source=6979735a1bbe7e9ff16a1edf167193f8d234e06e sourceState=clean buildSource=verified
```

Source report: .antiphon/checkpoints/20261013-073557-ba40/report.md
```text
CHECKPOINT CP-4 commit=7adb09c79ac24982042e48d85638f0cbc5103623 build=ok filter=/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/* executed=48 passed=46 failed=2 skipped=0 trx=/work/worktrees/task-7b7d644a/.antiphon/checkpoints/20261013-073557-ba40/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=7adb09c79ac24982042e48d85638f0cbc5103623 sourceState=clean buildSource=verified
```

Source report: .antiphon/checkpoints/20261013-074506-49bc/report.md
```text
CHECKPOINT CP-4 commit=b9f34b5294c96080de4f68d11c97f380d032f215 build=ok filter=/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/* executed=48 passed=47 failed=1 skipped=0 trx=/work/worktrees/task-7b7d644a/.antiphon/checkpoints/20261013-074506-49bc/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=b9f34b5294c96080de4f68d11c97f380d032f215 sourceState=clean buildSource=verified
```
