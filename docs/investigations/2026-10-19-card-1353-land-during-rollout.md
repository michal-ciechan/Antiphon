# CARD-1353: lands during a server2 rolling rollout (investigation)

Task `2ff87ca6` (Investigate) on `feat/card-task-2ff87ca6` from `81cfb267a0af5c0ddb65e8b238af355067bee9cc`.
Read-only: no test lane, no deploy phase, no server2 container was touched. Live reads were GET only
while a real rollout sat in `drain-old` at `81cfb267` (server2 `draining=true redirectTo=server2-temp
sessions=1`, server2-temp `acceptingNewWork=true buildVersion=81cfb267...`, `landPending` closure empty,
`/api/version` `81cfb267...` with `land-v2`, read 2026-10-19T17:2xZ).

## Outcome

Confirmed. A land never executes on, binds to, or reads any session runner; it is desktop-server git
work on the task's desktop worktree. The rollout breaks because the **host** recycle helper re-runs the
task census after it has already stopped and removed the standing container, and that census refuses
`RecycleLandInFlight` for *any* row in the project's `landPending` closure, with no runner condition.
The 2026-10-17 stop is reconstructed below from the committed records and the stored land request row.
`drain-old` has no land check at all. The four census-race cards are already merged and leave the land
rule untouched. Recommended change: narrow the land rule to lands bound to the recycled runner, and turn
the residual post-removal refusal into a bounded wait.

## 1. Who executes a land

| Step | Where it runs | Evidence |
|---|---|---|
| Request | `delegate.ps1 -Land` POSTs `/api/agent-tasks/{id}/land/v2` to the desktop server | `scripts/delegate.ps1:845` |
| Admission | `AgentTaskLandService.RequestAsync`: Worktree-only, Succeeded (or recovery), `land_running` is the only same-task conflict, then sets `LandRequestedAt`. Reads no runner, drain, or recycle state | `server/Application/Services/AgentTaskLandService.cs:78-137`, `:304` |
| Queue and worker | `AgentTaskLandQueue` singleton drained by `AgentTaskLandHostedService`, a `BackgroundService` in the server process (the desktop AppHost) | `server/Program.cs:439`; `server/Infrastructure/Orchestration/AgentTaskLandHostedService.cs:10-25` |
| Execution | `RunLeasedAsync` takes the repository lease, then `AgentTaskLandingProtocol.RunAsync` does fetch, rebase, verify, fast-forward, push and cleanup on `task.WorktreePath` / `task.RepoPath` | `AgentTaskLandService.cs:513-640`; `server/Application/Services/AgentTaskLandingProtocol.cs:37,299` |
| Git | `LandingGit`: target push `sha:targetFullRef`, source republish `--force-with-lease`; cleanup deletes only the **local** source ref via `update-ref -d`. The only `push origin --delete` in the server is `GitService.DeleteBranchAsync`, called from `WorkflowEngine` only | `server/Infrastructure/Git/LandingGit.cs:990,1000,1015`; `server/Infrastructure/Git/GuardedWorktreeRemoval.cs:286-306`; `server/Infrastructure/Git/GitService.cs:307`; `server/Application/Services/WorkflowEngine.cs:851` |
| Paths | A server2-bound task's worktree is a desktop path. Live: this task is `runnerId=server2-temp`, `worktreePath=C:\Antiphon\worktrees\card-task-2ff87ca6`, `repoPath=C:\src\Antiphon`, `remoteWorktreePath=null`. The incident task `4ce63b7a` is `runnerId=server2-temp`, `worktreePath=C:\Antiphon\worktrees\card-task-4ce63b7a` | `GET /api/agent-tasks/{id}` (2026-10-19) |

No land file reads `RunnerId` or runner sessions: a grep of `AgentTaskLandService.cs`,
`AgentTaskLandingProtocol.cs`, `AgentTaskLandSourceResolver.cs` and `LandOperationFactory.cs` for
`RunnerId` is empty; the two `AgentSessions` reads (`AgentTaskLandService.cs:276,886`) are merge-helper
writer detection. The runner mirror is removed only by card-done worktree retirement
(`server/Application/Services/TaskWorktreeRetirementService.cs:360-390`, best effort) and by the
dispatcher's drain rebind of a *Queued* task (`server/Application/Services/AgentTaskDispatcher.cs:1072`),
never by a land. The runner status counters a drain waits on count sessions and queued unlaunched tasks
bound to that runner (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:723-740`);
a land creates neither.

Answer: a land needs neither the draining standing runner, its workspace volume, nor its container. The
task's runner binding reaches the rollout only as the `runnerId` field of a census row, and the land
rule does not even look at that field.

## 2. What each phase checks, and why 2026-10-17 refused after removal

### Wrapper (`scripts/deploy-server2.ps1`)

- `drain-old` (`:1098-1117`): POST `/drain` with `redirectTo=server2-temp`, then `Wait-RunnerStatus`
  until `sessions`, `runnerSessions` and `queuedTasks` are all zero (`:1113-1117`). **No census, no land
  check.**
- `redeploy-old` (`:1118-1160`): `Assert-RecycleTaskCensus -RunnerId 'server2'` at `:1141`, then
  `Invoke-HostCase 'deploy-parent'` at `:1144`. The wrapper's check is **before** anything destructive.
- `drain-temp` (`:1161-1178`) -> `Wait-TempContainerRetirement` (`:918`) -> `Invoke-TempContainerCleanup`
  census at `:890` before the `retire-temp-containers` host case; `retire-temp` census at `:1197` before
  `retire-temp-runner`. Both before destructive work on the wrapper side.
- `Assert-RecycleTaskCensus` (`:640-791`) walks two closures: open work
  (`&status=Queued,Dispatched,Working,Blocked`) and `&landPending=true` (`:668`). Any row with
  `landRequestedAt` or `landStartedAt` throws `RecycleLandInFlight <id>` (`:726-729`); any open row
  whose detail has `landRequest.state` in Queued/Held/Running/NeedsResolution throws the same
  (`:747-749`). Neither test mentions `runnerId`. The only runner-conditioned rule is `RecycleBoundTasks`
  (`:730-732`). The `landPending` filter is `LandRequestedAt != null || LandStartedAt != null`
  (`server/Application/Services/AgentTaskService.cs:2713`).

### Host helper (`scripts/c590-remote.sh`, `deploy-parent` -> `c1008_recycle "$HOST_PROJECT"`)

`c1008_status_proof` (`:6105`) ends with `c1008_tasks` (`:6171`), whose `c1008_tasks_collect`
(`:5876`) refuses `RecycleLandInFlight` for any `landPending` row (`:5963-5964`) and for any open row with
a live `landRequest` (`:5995-5996`), again with no runner condition. `c1008_recycle` (`:7660`) calls that
proof **four times**:

| Call | Line | Relative to destructive steps |
|---|---|---|
| preflight | `:7702` | before `c1008_lock` (`:7714`) |
| post-lock | `:7715` | before audit 1 (`:7791`), stop (`:7810`), remove (`:7822`) |
| post-removal | `:7831` | after journal `phase=containersRemoved` (`:7828`) and audit 2 (`:7829`) |
| per volume | `:7839` | inside the loop, before each `docker volume rm` (`:7845`) |

Once `C1008_ACTIVE=1` the proof also runs `c1008_tasks_match_record` (`:6174-6175`, `:6073-6103`); its
relaxed `containersRemoved` compare still requires every live `land.tasks` key to be in the saved record
(`:6098-6101`), but `RecycleLandInFlight` fires first inside `c1008_tasks_collect`. Between the post-lock
proof and the post-removal proof sit two publication audits of up to 3 h each (`docs/docker-stack.md:336`,
"Both audits of a recycle ... have that budget each"); on 2026-10-17 the first ran about 86 minutes.

### Reconstructed 2026-10-17 timeline (attempt 3 of task `77abf8f0`)

Source: `docs/investigations/2026-10-17-rc-rollout-finish.md:170-187`,
`docs/investigations/2026-10-17-server2-recycle-recovery.md:85-126,285`, and the stored request row.

| UTC | Event |
|---|---|
| 12:23:48 | `redeploy-old` starts; wrapper census (`:1141`) passes; host preflight and post-lock proofs pass |
| about 12:24 to 13:50 | audit 1 (`:7791`); containers `fc2983ea4991` and `2c96347842e4` stopped and removed (`:7810`, `:7822`); journal `c100808382374392e4ce1ac7dde48ca3b195f` `phase=containersRemoved ownedRemoved=true` |
| 13:51:05 | audit 2 helper starts (`:7829`) |
| 15:17:43.438 | `LandRequested` on `4ce63b7a` (Succeeded Plan, `runnerId=server2-temp`); `landStartedAt` 15:17:44.132 |
| about 15:19 | post-removal proof (`:7831`) reads the `landPending` closure, refuses `RecycleLandInFlight 4ce63b7a` at `:5964`; `c1008_refuse` writes `outcome=refused` with volumes `pending` |
| 15:19:44 | wrapper exit 2, `HostCaseFailed deploy-parent exit=2` |
| 15:23:24.666 | `Landed` event on `4ce63b7a` (land took 5 min 41 s) |

The refusal came from the host census at `:7831`, which is ordered after `docker stop` and `docker rm`.
The wrapper census at `:1141` had passed hours earlier. The task that tripped it lived on `server2-temp`
and on a desktop worktree; nothing about it touched `antiphon-runner_work`. The volumes survived only
because the refusal is ordered before `docker volume rm` (`:7845`); recovery then needed the manual
preserve path recorded in `2026-10-17-rc-rollout-finish.md:189-282`.

Answer: yes, the hazard check is ordered after a destructive step (container stop and removal) and
before the next one (volume removal). The wrapper-side check is not the problem.

## 3. Is a land during drain-old unsafe today?

No. `drain-old` runs no census and waits only on runner-bound sessions and queued tasks
(`deploy-server2.ps1:1113-1117`; counters at `PhoneHomeRunnerDirectory.cs:723-740`), which a land never
creates. A land requested during `drain-old` runs to completion on the desktop. Its only effect on the
rollout is that a land still pending when `redeploy-old` starts makes the wrapper census at `:1141`
refuse before any host work, which is safe and retryable. The "no lands during drain-old" freeze is an
operating practice, not a check: the live orchestrator instructions (GET `/api/orchestrator-instructions`
2026-10-19, 6.6 KB) contain no land sentence at all, and the written guidance is
`docs/docker-stack.md:22` ("Before starting, verify no land is pending") and `:345` ("Keep the no-new-land
interval throughout reclaim"), plus the counter-proposal in
`2026-10-17-server2-recycle-recovery.md:285-289` (a server-side `recycle_land_hold` 409 for the whole of
`redeploy-old`), which would institutionalise the freeze this card rejects.

The unsafe window is only the host-side `redeploy-old` interval from `c1008_lock` (`:7714`) to the last
volume proof (`:7839`), and the same shape in `retire-temp`'s `c1008_recycle "$TEMP_PROJECT"` (`:8395`,
`:8619`) and `retire-temp-containers`' `c994_status_proof` (`:8205-8224`, same `c1008_tasks` rule).

## 4. Smallest change that lets lands proceed in every phase

Sites that encode the land rule (all four must change together or the two censuses disagree):

- wrapper: `scripts/deploy-server2.ps1:726-729` (closure row), `:747-749` (detail state), the forgiveness
  `Test-RecycleForgivenDrift` (`:611-638`), and the `landPending` count in `check-census` (`:787`, `:1221`);
- host: `scripts/c590-remote.sh:5963-5964`, `:5995-5996`, the pass-1 forgiveness (`:6018-6040`) and
  `c1008_tasks_match_record` land-key rules (`:6098-6101`);
- docs: `docs/docker-stack.md:236-258` and `:313`, `:345`; `docs/orchestration-loop.md:29-31`
  (verify queued lands finished before/after the operation);
- tests: `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`
  `C1087_Census_filters_open_work_and_pending_lands` (vectors `pending-requested`/`pending-started`,
  `:186-205`), `C1008_Busy_routed_and_land_in_flight_refuse` (`:410-418`, "retained owner/pending land"),
  `C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` (`:1259-1303`, host script at
  `:1306`), `tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs:125`,
  `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs:12-21` (doc pins), and the
  CARD-1008 plan rows PC-9/PC-49/PC-51 that name the current behaviour
  (`docs/superpowers/plans/2026-10-03-card-1008-rolling-volume-recycle-and-retire-temp-plan.md:468,823,991`).

Candidates evaluated:

| Candidate | Finding |
|---|---|
| (a) move the land check before removal | Already true for the wrapper (`:1141`) and the host preflight/post-lock proofs (`:7702`, `:7715`). The post-removal proofs (`:7831`, `:7839`) also carry the bound-work, routing and reference checks the volume removal needs, so they cannot simply move. "Wait rather than refuse" fits: `c1008_status_proof` already polls nothing, but the wrapper has the `Wait-RunnerStatus` 30 s poll pattern (`:961-972`) and lands finish in minutes (5 m 41 s above). A bounded poll of `c1008_tasks` on `RecycleLandInFlight` while `C1008_ACTIVE=1` would have let 2026-10-17 proceed about four minutes later. |
| (b) exclude lands that do not need the recycled volume | By section 1 that is every land. The honest form of (b) is to drop the land rule, or, as a hedge consistent with `RecycleBoundTasks`, keep it only for rows whose `runnerId` equals the recycled runner. It is a predicate change at the four sites above plus the forgiveness and match-record rules, with `check-census` still reporting the count. It would have passed 2026-10-17 (`runnerId=server2-temp`). |
| (c) attribute or execute the land on the accepting runner | Moot. The land never executes on a runner (section 1), and the census ignores `runnerId` for lands, so re-attributing a task row changes nothing and misstates where the task ran. Rejected. |

Recommendation for Plan: (b) narrowed by `runnerId`, combined with (a)'s bounded wait for the residual
runner-bound lands at the two post-removal proofs (`:7831`, `:7839`) and at the wrapper call sites
(`:890`, `:1141`, `:1197`). That removes every refusal for lands on the accepting runner or the desktop
and leaves a short hold, not a stop, for a land of a task that last ran on the runner being recycled.
Docs then drop the "no lands during redeploy-old" practice.

## 5. Overlap with CARD-1317, CARD-1319, CARD-1324, CARD-1326

All four are already on master, `rc/2026-10-15`, and this branch (`git log HEAD`: `e6ffa1bd6` 1317,
`da326d763` 1319, `d7b8bbd25` 1324, `867476095` 1326; merges `1347e6e8b`, `9a438ce57`, `9b4d3c89a`,
`1f8c83371`). Their review column on the board lags the code. They tolerate census drift from tasks
*without* lands (settled-without-land, pass-0 leave, pass-1 no-land appearance) and leave the land rule
untouched; CARD-1326 explicitly pins that a land-timestamp or `landPending` appearance still refuses
(`RollingVolumeRecycleScriptTests.cs:1259-1303`, `docs/docker-stack.md:248-257`). None is a prerequisite
and none delivers any part of CARD-1353; a CARD-1353 change must rewrite the C1326 vectors and the
"Mutating phases and the host census refuse `RecycleLandInFlight` on the row itself" sentence
(`docs/docker-stack.md:256-257`). CARD-1326's open decisions (c) and (d) are orthogonal.

## Not done, noted

- Fix idea (one line): narrow both censuses' land refusal to `runnerId == <recycled runner>` and make the
  host post-removal proofs poll instead of refuse, then drop the no-land practice from the docs.
- Not run: any test lane, any `deploy-server2.ps1` phase, any server2 command.
- Not read: the desktop-only receipts under `.antiphon/rolling-server2/` (gitignored, not on this runner);
  the timeline uses the committed records plus the stored `LandRequested`/`Landed` events.

## Remaining uncertainties

- Publication audit cross-effect: a land republishes the source branch on origin with
  `--force-with-lease` (`LandingGit.cs:1015`). If a task's runner mirror is still on the recycled work
  volume and its tip is the pre-rebase SHA, the audit's unpublished-tip rule (`c590-remote.sh:6952-6956`,
  `docs/docker-stack.md:336`, CARD-1105 replay 3b) could refuse `RecycleUnpublishedWork`. That is
  independent of rollout timing (a land before the rollout has the same effect) and did not fire in the
  2026-10-17 audits (800 repositories), so it is not this card's mechanism; a Plan that removes the land
  rule should state it as a known, separately audited risk.
- The exact second of the post-removal proof read is not stored; "about 15:19" is bounded by the land
  start (15:17:44) and the wrapper exit (15:19:44).
- Whether the operator wants any residual land hold for runner-bound lands at all, given section 1, is a
  policy choice for Plan.
