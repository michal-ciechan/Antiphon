# CARD-1285 task-token loss: investigation and implementation evidence

Observed 2026-10-15 from `/work/worktrees/task-23d87a95`, base
`157321d8bbedcd93a3e2b8f4c43f4a098db4cf7f`. Original Code/landing owner:
`23d87a95-40ee-40a5-9ce9-81faded14d82`, branch `feat/card-task-23d87a95`.

## Root cause

The durable task hash and the raw credential have different lifetimes.
`AgentTaskService.NewToken` generates 32 cryptographically random bytes and hashes
its hex representation with SHA-256. Create, retry and follow-up paths persist
`TokenHash` but hand the raw token through process-static `RawTokens`. The ordinary
create path populates the cache after committing and publishing the create event.
`AgentTaskDispatcher.BuildEnv` performs a destructive `TryRemove`; a cache miss
silently leaves `ANTIPHON_TASK_TOKEN` absent. Thus a server restart between create
and dispatch loses the raw credential, as does a second composition for a fresh
boot-wedge process. The post-commit/cache-publication window is another source-level
way a dispatch can miss the cache; it was not separately observed live here.

Both the registry and managed-profile launch paths reach
`FinishDispatchedLaunchSpecAsync`, after the task/session claim commits and before
the launch queue. `PhoneHomeLaunchPolicy.Project` copies the environment, overriding
callback origin and provider homes. `RunnerContractMapper` transmits the dictionary;
`SessionRunnerRuntime` forwards `request.Env` into `LaunchMessage`. Those layers do
not remove task tokens. `-StartRef` changes source selection, not credential setup.

Standing interactive/card/orchestrator launch composers separately mint session
credentials and persist session hashes. Warm pool reuse transfers the previous
owner's hash to the new task, because the running process's environment cannot
change. Standing specialist reuse discards the unused task credential and uses its
existing session. Interrupted launch recovery attaches to the existing process;
it does not reconstruct its environment. These paths are outside the fresh-process
repair. Restarting a server does not invalidate an already-delivered token, because
its hash persists; restarting a runner cannot recover raw task tokens lost on the
server.

## Live evidence (read-only)

Task detail reads used the current task credential in an HTTP header only. Process
inspection read `/proc/<pid>/environ` in memory and emitted only task/session IDs,
process names, PIDs and a token-presence Boolean. No raw credential or credential
hash was printed, logged or written to an evidence file.

| Task | Role/provider | Created UTC | Server stopping UTC | Dispatched UTC | Missing-token evidence |
|---|---|---|---|---|---|
| `cd4110a7-5246-4374-b394-065c2460d539` | Review/Claude | 10:30:37 | 10:33:58.861 | 10:36:24 | Settled report says missing token, exit 7, unbound workaround; process has stopped. |
| `e7ca6378-5573-483d-916a-55fc5b68c786` | Code/Codex S6a | 14:41:23 | 14:46:32.791 | 14:50:01 | Settled report says two exit-7 refusals, zero tests. Process has stopped. |
| `fbba8d73-6262-4af2-8609-94122a5fb155` | Code/Codex CARD-1298 | 17:53:25 | 17:59:15.672 | 18:01:45 | Settled report discloses missing token and unbound row workaround. Process has stopped. |
| `a6d66bd8-08c9-4da0-8295-4a8ceed8cb0c` | Code/Codex CARD-1259 | 17:55:17 | 17:59:15.672 | 18:02:34 | Live Codex PID 29669 and code-mode PID 29934 both had task/session identity and tokenPresent=false. |

All four were Worker/Worktree tasks routed to the same registered Linux runner and
held across a server restart. The runner log
`/state/runner-logs/session-runner-20261015.log` records each quoted close as
`close=EndpointUnavailable:server_stopping`, followed by reconnection. The three
settled cases use report evidence, not a claim to have inspected dead processes.
The live missing-token session is `86406282-a613-47af-8cb4-74bb24090092`.

Controls: this task's session `d9efc95b-28d4-4266-953d-787fe6585020` had tokenPresent=true
in Codex PID 29757 and code-mode PID 30928. Its create/dispatch timestamps are
18:39:45 / 18:40:19, after the 18:36:49 shutdown and 18:39:01 reconnect. Live task
`024ec8fb-7f2b-4800-9154-f34b2d2ab2d2` also had tokenPresent=true. No claim is made
that all past missing-token incidents have been enumerated.

`GET /api/runner-defaults` returned revision 2, global runner `server2`;
`GET /api/session-runners` reported that Linux runner available and dispatchEligible,
with capacity 10 and occupied 5. This task used the assigned host, no routing or
capacity changes. `GET /api/version` matched the task base. One unbounded board
listing timed out at 15 seconds; a scoped listing with `since=2026-10-15T00:00:00Z`
and individual task detail reads provided the rows above.

## Owner guard and repair

Missing API/token or an invalid task ID fails immediately as `owner-unverified`.
With a token, each task read has a 12-second HTTP deadline; retry uncertainty is
bounded by three minutes, with three-second waits. HTTP failures, invalid task or
session identity, malformed responses and unknown states can exhaust that budget.
Existing drivers keep running during uncertainty; new work requires a live answer.
After the budget expires, reads continue so a definitive settlement still cancels
running work. This task's first checkpoint logged one `timeout after 12s`, then
continued. No owner-guard change, timeout extension or unbound run is part of this fix.

The repair validates the launch token against the task's current hash at the common
fresh-process boundary. Missing/blank/stale credentials are replaced and the new
hash saved before enqueue. Valid cached credentials are retained. Database-save
failure cannot release the launch spec. Raw tokens remain environment-only and
hashed at rest. No schema or runner change is needed.

## Qualification and activation

The plan is `docs/superpowers/plans/2026-10-15-card-1285-task-launch-token-plan.md`.
The explicit brief requests narrow Final verification without the whole Unit lane.
Fresh TRX identities, exact checkpoint lines, source receipts and outcomes are
recorded in the stored report `.antiphon/task-23d87a95.md`; raw generated evidence
stays ignored under `.antiphon/checkpoints/`. The red-first test commit is
`a64b2db0b6dfb5b9ebd443ad820b7740113f8736`.

The first run at that commit executed six cases but all stopped at the fixture's
dispatch-setup assertion. It is a historical fixture failure, not guarded-defect
proof (6m39s, build 313s). The corrected fixture seeds the persisted mirror and
progress baseline rather than trying to prepare a non-repository scratch folder.
At `ffcdaddcb520417d7b3d8022cfc5868755b18842`, the same six-case selection passed
the valid cached-token control and failed the other five variants specifically at
the independent database-at-enqueue token assertion (6m51s, build 285s). All three
missing-token provider/role combinations, stale cache and relaunch reached the
guarded defect. Both runs had clean source, verified build provenance, six fresh
TRX results, no skips, and granted build/row slots with zero wait. The initial
checkpoint-tool bootstrap was separately gated, granted after 15s, build 4.25s.

After ordinary Review, the caller lands the original Code task and commissions
method-scoped SourceLanding Mutation. PC-1 through PC-4 and all their named
variants remain pending. Activation needs a server restart, no runner restart.
A fresh dispatch after activation is needed for any currently tokenless task;
this change cannot modify the environment of its running process.
CHECKPOINT CP-1 commit=a64b2db0b6dfb5b9ebd443ad820b7740113f8736 build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/C1285_worker_launch_has_durable_task_token executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-23d87a95/.antiphon/checkpoints/20261015-184558-27e4/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a64b2db0b6dfb5b9ebd443ad820b7740113f8736 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=ffcdaddcb520417d7b3d8022cfc5868755b18842 build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/C1285_worker_launch_has_durable_task_token executed=6 passed=1 failed=5 skipped=0 trx=/work/worktrees/task-23d87a95/.antiphon/checkpoints/20261015-185455-de54/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=ffcdaddcb520417d7b3d8022cfc5868755b18842 sourceState=clean buildSource=verified

## Existing fixture attribution and final selection correction

At `4a31ec4e997b066cf3503adb1dc366a9f2e8ccb9`, all six new token variants passed
inside CP-2 (14 executed, 8 passed, 6 failed). The six failures were existing remote
preparation cases. CP-3 and CP-4 executed zero cases: the initial combined-class
filters omitted the trailing wildcards required by the pinned TUnit discovery.
They are incomplete results, not passes. The revised manifest corrects those
filters without changing the intended classes.

CP-5 reran exactly the six failing existing methods at task base
`157321d8bbedcd93a3e2b8f4c43f4a098db4cf7f` in detached checkout
`/tmp/c1285-task-base-23d87a95`. All six reproduced the same failures with clean
source and verified build provenance. The fixture used `feat/test-remote`;
`RemoteWorkspaceService.MirrorAsync` requires a recognized delegate branch whose
short ID equals the task ID. The fixture now seeds that matching branch, and the
new token test's progress baseline uses the same branch. No production mirror
rule, timeout or assertion changes. These inherited fixture repairs are included
so the whole affected class can qualify the launch change.

The base run cost 8m09s: build slot granted after 135s, build 237.96s, row slot
granted with zero wait, test host 92.34s. An initial request to place base evidence
outside its checkout was refused before any driver; the corrected run used its
normal evidence directory. A wait without the base repo-root also refused; the
corrected wait joined the same run. Neither refusal started additional tests.

The candidate Mutation pack is
`docs/superpowers/mutations/2026-10-15-card-1285-task-token-candidates.md`.
CHECKPOINT CP-2 commit=4a31ec4e997b066cf3503adb1dc366a9f2e8ccb9 build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/* executed=14 passed=8 failed=6 skipped=0 trx=/work/worktrees/task-23d87a95/.antiphon/checkpoints/20261015-190253-d496/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=4a31ec4e997b066cf3503adb1dc366a9f2e8ccb9 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=4a31ec4e997b066cf3503adb1dc366a9f2e8ccb9 build=reused filter=/*/*/(LaunchEnvLayersIntegrationTests)|(PinnedProfileLaunchSpecTests)|(CodexDelegateDispatchTests)|(GrokDelegateDispatchTests)|(BootWedgeRelaunchParityTests)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-23d87a95/.antiphon/checkpoints/20261015-190253-d496/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=4a31ec4e997b066cf3503adb1dc366a9f2e8ccb9 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4a31ec4e997b066cf3503adb1dc366a9f2e8ccb9 build=reused filter=/*/*/(CheckpointTaskOwnershipTests)|(CheckpointSlotExecutorTests)|(LaunchModelArgumentAppenderTests)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-23d87a95/.antiphon/checkpoints/20261015-190253-d496/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=4a31ec4e997b066cf3503adb1dc366a9f2e8ccb9 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=157321d8bbedcd93a3e2b8f4c43f4a098db4cf7f build=ok filter=/*/*/PhoneHomeTaskDispatchProjectionTests/(Runner_bound_boot_wedge_relaunch_keeps_the_runner_binding_and_projection*)|(Runner_bound_grok_task_reaches_the_queue_projected*)|(Runner_bound_claude_task_reaches_the_queue_projected*)|(Runner_bound_task_launch_reaches_runner_start_with_a_sourced_task_on_record*)|(Card_bound_runner_worktree_task_dispatches*)|(Runner_bound_grok_brief_spills_under_the_runner_cwd*) executed=6 passed=0 failed=6 skipped=0 trx=/tmp/c1285-task-base-23d87a95/.antiphon/checkpoints/20261015-191110-083e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=157321d8bbedcd93a3e2b8f4c43f4a098db4cf7f sourceState=clean buildSource=verified
