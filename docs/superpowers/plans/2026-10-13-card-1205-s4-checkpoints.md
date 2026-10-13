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
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1205-s4/` | watchdog | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests*/*` | V-13..19,V-27..35,R-3,R-4 | Exactly 48 green; frozen T18..T35 roster | 48 | 12 |

All S4 PCs remain pending for method-scoped SourceLanding Mutation:
PC-41..83, PC-101..110 and any additional implementation variants.
