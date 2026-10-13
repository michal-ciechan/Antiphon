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
