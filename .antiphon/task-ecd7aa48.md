CARD-1190 repair 3 is green and pushed. Specialist delete releases its row locks before the session kill, and a ready-gate ProcessExit becomes the not-ready failure only when this cleanup's kill produced that stamp.

Bound to CARD-1190. Worktree `/work/worktrees/task-ecd7aa48`, branch `feat/card-task-ecd7aa48`, commit `f5bc6c6aa37d3569c965626bd09735273285a942` (fast-forward from `a3231ebc2`; source Code task `65f76244`). `git ls-remote origin refs/heads/feat/card-task-ecd7aa48` equals HEAD. Specialist `DeleteAsync` is `AgentService.DeleteAsync`.

Lock order on paths that touch both tables:

| Path | Before | After |
|---|---|---|
| Launch-failure catch, `AgentSessionService.cs:399` | Session, then agent | Same |
| Exit observer, `CloseSessionOnExitAsync` (`AgentSessionRuntime.cs:202`) | Session `FOR UPDATE`, then agent update | Same |
| Start reservation, `AgentControlService.cs:525` | Sessions, then agent | Same |
| `StopAsync`, `AgentControlService.cs:1146` | Session, then agent. Preceding `LockAgentAsync` is autocommit and ends with the statement | Same |
| Re-adoption, `SessionReconciliationService.cs:648` | Session `FOR UPDATE`, then agent update | Same |
| Reconciliation close, `SessionReconciliationService.cs:207` | Session only | Same |
| Herdr `ObserveAsync` | Agent lock; session read `AsNoTracking`, no session write | Same |
| Queue `LoadAgentForQueueUpdateAsync` | Agent only | Same |
| Specialist `DeleteAsync`, `AgentService.cs:806` | Agent row held across `KillAsync`'s session write | Session then agent, commit, kill with no row locks, then session then agent for the delete. A non-specialist delete still takes no agent lock |

`LaunchKillOwnsProcessExit` owns the stamp when the kill reported this generation dead, `StartedAt` matches, `EndedAt` is at or after the kill's issued time, the observed reason is absent, Unknown, or `KilledByRequest`, `FailureReason` has no `ProcessExited`, and the session exit code equals the code the kill observed (null or 0 when the kill reported no code). An in-window `ProcessExited` crash stays `ProcessExit`. A stamp from just before the kill stays `ProcessExit`. The kill's own exit-code-0 stamp becomes `SystemRequest` and the not-ready failure. That in-window test failed on `ece7d9347` (rewritten to `SystemRequest`) and passes here.

The barrier confirms locks from `ReaderExecutedAsync` and `NonQueryExecutedAsync`, after the statement has returned. The ready-gate test proves the session row is held with `FOR UPDATE NOWAIT` (`55P03`). A granted tuple lock lives on the tuple header; `pg_locks` lists it only for a waiter.

D4 is left. A recipient `UserPrompt` check needs the queue and reply path CARD-1069 owns. `SessionMessageQueueService` and `AgentTaskReplyService` were not edited.

One isolated build, `OutputPath=bin-c1190/`, `UseAppHost=false`, lease `cc493799-4485-4f90-a31c-213acacba360`, waited=0s, 0 errors. Later runs used `--no-build` on that output. Ready-gate ran on this tree immediately before the commit; the other filters ran at `f5bc6c6aa`. Every lease waited=0s.

- CP-1 `/*/*/AgentSessionLaunchFailureTests/A_ready_gate_*`: total 8, failed 0, succeeded 8, lease `4ae14710-e141-4419-bf9a-50f9c60ad25b`
- CP-2 `/*/*/SessionGenerationExitTests/C502_V4*`: total 2, failed 0, succeeded 2, lease `190bcf58-a81f-4ce1-9d7e-8e3a0c5b6193`
- CP-3 `/*/*/AgentControlServiceIntegrationTests/*`: total 33, failed 0, succeeded 33, lease `696de94f-6b1d-4ef0-acb6-eb05f7c45b19`
- CP-4 `/*/*/AgentServiceIntegrationTests/*`: total 54, failed 0, succeeded 54, lease `d60c4675-169a-4295-b28a-ff8f0c7fdf6e`
- CP-5 `/*/*/SpecialistStartIntentTests/*`: total 4, failed 0, succeeded 4, lease `4231e649-488c-40bd-8a6e-53e617932814`
- CP-6 `/*/*/StandingSpecialistSeatTests/*`: total 12, failed 0, succeeded 12, lease `61e0459f-e812-43f6-9966-8db9c4ee898a`
- CP-7 `/*/*/StandingSessionSwitchConcurrencyTests/*`: total 24, failed 0, succeeded 24, lease `97e6daab-41f7-4b69-b412-6e8ee90370bd`

The ten known base reds stayed outside these filters. The whole Unit lane was not run. `check-evidence-diff.ps1` over `dbfc1a094..f5bc6c6aa`: commits=4, entries=0, violations=0. `git merge-tree --write-tree` against fetched `origin/master` `4e064b7ff5fd7704220aab75935097fd26090af8` was clean (tree `94b28a65db564f5919b57b9b7b15b3624c8b765c`, exit 0). `bin-c1190` directories are removed.

Positive controls stay pending for method-scoped SourceLanding Mutation. This round does not discharge them. Post-land Mutation was not run. Restart: none.


Verification commit for the runs above is f5bc6c6aa37d3569c965626bd09735273285a942. This file is the full report.
