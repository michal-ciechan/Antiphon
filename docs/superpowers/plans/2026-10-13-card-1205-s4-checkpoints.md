# CARD-1205 S4 independent recovery watchdog

Code/landing owner: repair task 0ced9d05-9c38-478e-81ba-3dc9c9375223
(branch `feat/card-task-0ced9d05`). Original S4 Code task: 902bc6d6-3b26-465f-bdef-ff32f5920dd5; predecessor 7b7d644a-a776-42e8-bc12-3d57e5df5ace.
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
| S4-T33 | Repair | `tests/Antiphon.Tests -> bin-c1205-s4-repair/` | queue-fixture-repair | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/C1205_Recovered_idle_schedules_only_coalesced_work*` | V-33,R-3 unresolved T33 | Exactly 1 green before final full-class CP-4 | 1 | 6 |
| S4-F1 | Repair | `tests/Antiphon.Tests -> bin-c1205-s4-repair/` | reconciliation-wakeup | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/C1205_Reconciliation_preserves_owned_recovery_wakeup*` | F1,V-33,R-3 | Four variants; after-commit/two-probes red on b02129ed9, four green after repair | 4 | 6 |
| S4-F2 | Repair | `tests/Antiphon.Tests -> bin-c1205-s4-repair/` | hosted-backlog | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/C1205_Hosted_backlog_bound_includes_poll_refill*` | F2,V-27,V-28,R-3 | One hosted fake-clock witness | 1 | 6 |
| CP-4 | Repair | `tests/Antiphon.Tests -> bin-c1205-s4-repair/` | watchdog | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/*` | V-13..19,V-27..35,R-3,R-4 | Exactly 53 green; T18..T35 plus F1/F2 repair witnesses | 53 | 12 |

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
This is an explicit pass budget, not an elapsed-time upper bound or a timeout increase. T62 separately binds the hosted poll-refill allowance.
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

## Continuation admission, 2026-10-13

Task 902bc6d6 starts at ab32d51ad1a71c87e3098993bfe2f07365c04f01,
which already contains S4 production and the T33 inventory repair. The predecessor
remote tip is the same SHA; no cherry-pick is needed. Final S4 scope is the
explicit slice selection: S4-T33 followed by the whole 48-case CP-4 class, covering
V-13..19, V-27..35, R-3 and R-4. The brief excludes Unit and S5.

Preserved baseline runs 20261013-070708-9cf5 and 20261013-072438-ff86
are inspected in the predecessor's checkpoint store. The latter tests committed
6979735a1bbe7e9ff16a1edf167193f8d234e06e, with unchanged pre-S4 production:
48 assertion failures (missing probes, registration, validation and disabled logs),
zero passed/skipped. These are expected defect reds, not fixture/build failures;
the green-only receipt validator rejects them as `row_failed`. Their source
envelopes and actual TRX counts must be checked directly. Independent guard PCs
remain pending for Mutation. No full assembly run or additional proof repetition
is needed after CP-4 is green.

Runner defaults/catalogue were refreshed at admission; eligible Linux and Windows
capacity was advertised. No runner or OS pin is introduced. A build-slot-gated
isolated checkpoint-tool bootstrap is the only additional setup build because
this new worktree has no tool executable. No restart or manual live acceptance
is required for S4.

## Continuation T32 fixture repair

The first continuation run 20261013-080440-6e09 passed S4-T33=1/1 and
CP-4=48/48 on 2bfb8433934c6c1b0353c28a332b3cbd82be8db2. During source
inspection, T32's backoff response was found to request 30 seconds while the
fixture admitted only the default five-second maximum delay. Its observed
30-second timer could therefore be the attempt timer, leaving backoff unproved.

The fixture now admits that 30-second delay without changing any timeout, requires
the actual retry log and a live delay timer, and records the logical read token
outside the retry pipeline. All three T32 variants assert token cancellation
synchronously at the exact ten-second watchdog budget before joining. Teardown
releases controlled I/O and expires the unchanged inner budget if an assertion
fails. PC-77's headers/body/backoff variants remain pending for Mutation.
Run the unchanged whole-class CP-4 on this committed repair; the additional run
is justified by the strengthened source, not unchanged proof repetition. S4-T33's
successful method run remains evidence and its case is also present in CP-4.

The strengthened CP-4 passed all 48 cases on e9eca807a8c44169f8a2dd0264e97ff3fcf5c36c
(run 20261013-081416-2abe). The S4 pending-PC handoff is now the
[unbound Mutation candidate](../mutations/2026-10-13-card-1205-s4-candidate.md).
The final artifact commit changes documentation only; one final CP-4 run certifies
that exact committed HEAD with the unchanged strengthened 48-case selection.
This source-qualification run is the last ordinary repetition; none is required
after green. No full assembly, Unit or S5 selection is added.

## F1/F2 repair admission

Repair/landing owner: 0ced9d05-9c38-478e-81ba-3dc9c9375223,
branch feat/card-task-0ced9d05, based on b02129ed95d1389f7b43c7ed5d77db46b062e0a3.
Original S4 Code task: 902bc6d6-3b26-465f-bdef-ff32f5920dd5.
Run S4-F1,S4-F2 test-first, then S4-T33,S4-F1,S4-F2,CP-4 after repair.
One isolated build per committed group, exact HEAD, serial rows. NO Unit or S5.
The tool bootstrap is an additional gated setup build because this worktree has no executable.
No restart or live manual work. CP-4 now has 53 executions (48 inherited + 4 F1 + 1 F2).
All existing S4 PCs plus PC-191/192 remain pending, including their variants.
### Repair source and bound audit

F1 test-only source 6d88088bf160604312eddf381fcd7cc3f03ffb6d retains production
exactly from b02129ed95d1389f7b43c7ed5d77db46b062e0a3. Run
20261013-090627-4855 executes four F1 rows: before-commit and after-return pass;
after-commit and two-probes fail at Accepted expected [session], actual [].
These are assertion reds, not build/fixture errors. The first baseline build
(20261013-090330-b077) failed from an invalid Shouldly overload and executed zero;
the overload was corrected without changing the exact assertion or timeout.
An earlier launcher refused an incorrectly transcribed expected SHA before any run.

F2 corrects the timeout-only statement in session-runtime-invariants, the S4 plan
and test design; updates the historical manifest pass-budget wording and the
scheduler/host comments; and adds the hosted T62 row. Discovery counts all local
Running candidates, including idle, and allows cursor wrap. Finite timing requires
N<=1024, stable ownership, bounded successful scans and bounded commit/join delay.
Worker refill is poll-driven. Overflow/non-cooperative I/O remains visible debt.
The corrected formulas are in the plan, design and runtime owner; no timeout,
assertion or retry policy is relaxed. New PC-191 wake-membership/duplicate variants
and PC-192 poll-cadence/continuous-refill variants remain pending for Mutation.