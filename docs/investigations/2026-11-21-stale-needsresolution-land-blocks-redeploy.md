# Stale NeedsResolution land and redeploy-old (2026-11-21)

Debug task `cb7670b0`. Read-only except this note. No POST, PATCH, DELETE, restart, deploy, server2 host command, or production Postgres. Live reads are `GET` against `$ANTIPHON_API` at 2026-11-21T02:53:49Z. Source lines are this checkout, `493bee0ee3af2052575bc7a26e9a313669b2fc2d`. The running server is ancestor `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248` (`GET /api/version`). `scripts/deploy-server2.ps1` and `LandApproval.cs` are unchanged since that ancestor. `AgentTaskLandService.cs` differs only in conflict-detail text; the eligibility, supersede, and sweep-cancel strings are the same (`recovery_owner_ineligible` at live line 104, `task_no_longer_eligible` at live lines 507 and 1295).

## Outcome

The cancel of `ad11c378-16c7-4e0d-bffd-04e440b33d63` already removed it from the redeploy-old census. At 02:53:49Z the task is `Canceled`, its land request is `Canceled` with `reconciliationError=task_no_longer_eligible`, and both land timestamps are null. Fleet-wide, the only `landPending=true` row is on `server2-temp`, which redeploy-old does not refuse.

Do not request another land for this task. If the 02:18Z `redeploy-old` process has already exited `RecycleLandInFlight`, rerun that same phase. If it is still inside `Wait-RecycleTaskCensus`, leave it: the next 30-second poll no longer sees a `server2`-bound pending land.

## Live task

`GET /api/agent-tasks/ad11c378-16c7-4e0d-bffd-04e440b33d63`

| Field | Value |
|---|---|
| status | Canceled |
| role | Code |
| runnerId | server2 |
| workspace | Worktree |
| branch | feat/card-task-ad11c378 |
| card | CARD-1836 |
| completedAt | 2026-11-21T02:48:42Z |
| landRequestedAt | null |
| landStartedAt | null |
| currentLandRequestId | null |
| landRequest.id | d5d33a23-93f7-4c0b-966a-d5d447f43d35 |
| landRequest.state | Canceled |
| landRequest.requestedAt | 2026-11-19T23:02:25Z |
| landRequest.reconciliationError | task_no_longer_eligible |
| landRequest.terminalEventId | null |
| landRequest.recoveryMode | None |
| landRequest.expectedSourceSha | 5acf597f121ef3cb53371d193337393627a8d76d |
| landing.phase | Refused |
| landing.publication | Unconfirmed |

`completedAt` and `landRequest.lastEvaluatedAt` are the same second. Cancel does not write the land request. The sweep that runs within `Delegation:LandSweepSeconds` (default 5, `server/appsettings.json`) does, and the monitor stamps `LastEvaluatedAt` on every still-pending request (`AgentTaskLandMonitorService.cs:30`). The error string is the sweep/admission cancel, not the monitor (the monitor writes `land_request_mirror_disagreement` or null, and only while `IsPending`).

`GET /api/agent-tasks?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f&unscoped=include&includeChecks=true&landPending=true` returned one row: `05f4921e-fc7f-4f3e-ab91-e92974f0da79`, Succeeded, `runnerId=server2-temp`. `excluded.total=0`. The same project’s open list (`status=Queued,Dispatched,Working,Blocked`) had no `server2` row. An unscoped `landPending=true` list was the same single temp row. `ad11c378` is on neither list.

`git fetch origin master` at this read: `origin/master` is `328eafa3daa7052d47587df66513e951dd0208cb`. `git cherry origin/master 5acf597f121ef3cb53371d193337393627a8d76d` marks `ec01d5fc2`, `e34da3630`, and `7bb8c8764` patch-equivalent and `5acf597f1` not. That commit changes only `tests/derived-impact-index.json` (7 lines). The tip is not an ancestor of master.

## 1. How a NeedsResolution request becomes Superseded or Canceled

There is no route, verb, or CLI flag that sets a land request to `Superseded` or `Canceled` by itself. `LandRequestState` is `Queued, Held, Running, NeedsResolution, Completed, Superseded, Canceled` (`server/Domain/Enums/LandingEnums.cs:19`). The writes are:

**Cancel the owner, then the land sweep.** `POST /api/agent-tasks/{id}/cancel` (`AgentTaskEndpoints.cs:286-289`) calls `CancelAsync` / `CancelCoreAsync` (`AgentTaskService.cs:3145-3191`). That sets `Status=Canceled` and `CompletedAt`. It does not read or write `LandRequestedAt`, `CurrentLandRequestId`, or `LandRequestState`. `MergeHelperOutcome.RecordUnresolvedAsync` returns immediately unless the canceled row is a Merge helper (`MergeHelperOutcome.cs:14-16`). Canceling the Code owner therefore leaves the request `NeedsResolution` until the next sweep.

The sweep’s stale query is every task with `LandRequestedAt != null` whose status is neither Succeeded nor Blocked (`AgentTaskLandService.cs:1286-1290`). A pending request that fails `LandApproval.RequestStatusEligible` is set to `Canceled`, `IsPending=false`, `ReconciliationError=task_no_longer_eligible`, and `ClearPending` (`:1299-1306`, `:1595-1600`). `ClearPending` nulls `LandRequestedAt`, `LandVerifyFilter`, and `LandStartedAt`. It does not set `TerminalEventId` and does not add a land-terminal event. That is the row observed above. The same error is written if a dequeued run finds the task ineligible (`:495-513`, writer `admission-cancel`). A `NeedsResolution` request is not dequeued for another git attempt (`:629`, `:1323-1324`); the stale loop is what cancels it. The product’s own helper-ended warning names this path: resolve with a reviewed `-Land`, retry the helper, or cancel the owner (`MergeHelperOutcome.cs:23-26`). Docs do not spell the sweep out. `POST /api/agent-tasks/land-notifications/expire` settles aged notes only after the request is already not pending (`docs/ops-http.md` CARD-0747 row). It does not cancel a `NeedsResolution` request.

**A new reviewed land supersedes NeedsResolution.** Only `recoverReviewedSource: true` or `adoptFromTaskId` does it, and only when the current request is pending and `NeedsResolution` (`AgentTaskLandService.cs:158-159`, `:280-313`). The old row becomes `Superseded` with `TerminalEventId` set to a `LandSuperseded` event. The new row sets `LandRequestedAt` again, so the census keeps waiting on the replacement until that land finishes and `ClearPending` runs. Owner status must be Succeeded, Blocked, or Failed (`LandApproval.cs:135-136`, `AgentTaskLandService.cs:102-104`). **Canceled refuses `recovery_owner_ineligible`.** Queued, Held, and Running are not superseded (`docs/orchestration-loop.md:231-233`, `docs/ops-http.md` “Recover a reviewed Code landing owner”). A Refused operation does not trip `publication_unconfirmed` (`AgentTaskLandService.cs:194-197` throws only when phase is not Refused and publication is not inherited). This landing is `Refused`, so that gate would have been open. The Canceled status is what closes it now.

**Plain `-Land` does not settle the request.** With `RecoveryMode.None` and `NeedsResolution`, admission sets the same row back to `Queued` and, if the task is Blocked, flips it to Succeeded (`:174-186`). A reviewed-recovery conflict refuses `recovery_requires_explicit_request` (`:176-178`). `docs/orchestration-loop.md:239-240`: a plain `-Land` or a completed Merge helper cannot turn that request into an ordinary land.

**A finished Merge helper can Supersede, only for an ordinary conflict.** `ResolveConflictedParentAsync` (`AgentTaskReplyService.cs:2607-2634`) requires the parent still Blocked and Worktree. If the current request has `RecoveryMode != None`, it returns without changing the request. If the request is pending `NeedsResolution`, it sets the parent Succeeded, nulls the land timestamps, and marks the request `Superseded` with the Merged event as `TerminalEventId`. This request’s `recoveryMode` is `None`, but the owner is already Canceled, so this path no longer applies.

`RequestStatusEligible` (`LandApproval.cs:141-148`): ordinary lands require Succeeded; reviewed recovery requires Code plus Succeeded, Blocked, or Failed. Canceled matches neither.

## 2. What the census was tripping, and what clears it

`redeploy-old` calls `Wait-RecycleTaskCensus -RunnerId server2` after the drain and zero-counter checks (`scripts/deploy-server2.ps1:1408-1430`). The census walks `open` (`status=Queued,Dispatched,Working,Blocked`) and `land` (`landPending=true`) (`:784-785`). `landPending` is `LandRequestedAt != null || LandStartedAt != null` (`AgentTaskService.cs:2891`). It is not `landRequest.state`.

For each row, `runnerId == server2` is `$bound` (`deploy-server2.ps1:846`). Either land timestamp on a bound row throws `RecycleLandInFlight <id>` before the detail read (`:847-849`). The `land` kind then `continue`s (`:854`) and never consults `landRequest.state`. The state check runs only for open-list detail (`:868-874`): `Queued`, `Held`, `Running`, or `NeedsResolution` throws the same token when bound. Any other state must be `Completed`, `Superseded`, or `Canceled` and `terminalEventId` must be a GUID, or the census throws `RecycleLandUnknown` (`:873-874`). That terminal-event check does not apply to a Canceled task: Canceled is not in the open filter, and a null timestamp pair is not in `landPending`.

So the condition that was tripping was the timestamp check, not the state check. A Canceled task whose `landRequestedAt` was still set would still throw `RecycleLandInFlight` on the land list. After this sweep both timestamps are null and the state is `Canceled`, so `ad11c378` is in neither closure. `terminalEventId=null` does not matter unless some later open-list read included the row, which the status filter prevents.

No agent-task route changes `runnerId` after create (`AgentTaskEndpoints.cs` has cancel, retry, reroute, escalate, reply, land; reroute is kind and model only). There is no documented way to unbind this row. A Completed, Superseded, or sweep-Canceled request clears the timestamps via `ClearPending`. Supersession immediately sets `LandRequestedAt` on the new request (`AgentTaskLandService.cs:317`), so a replacement land stays in the census until it finishes.

The off-runner temp land is counted and recorded, not refused (`:844-849`, `docs/docker-stack.md:273-285`).

## 3. A new reviewed land is not available, and an empty diff is not a mode

This owner is Canceled, so `POST /api/agent-tasks/ad11c378-16c7-4e0d-bffd-04e440b33d63/land/v2` with `recoverReviewedSource` or `adoptFromTaskId` refuses `recovery_owner_ineligible`. Do not dispatch a replay in order to clear the census. The census is already clear, and a successful new request would set `LandRequestedAt` and block redeploy-old again until that land finished.

If the owner were still Succeeded, Blocked, or Failed, the minimal supersede would be a Clean Final Review that completed Full scope of the exact pushed tip, then:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land ad11c378-16c7-4e0d-bffd-04e440b33d63 -ExpectedSourceSha 5acf597f121ef3cb53371d193337393627a8d76d -ReviewEvidenceId <evidence-guid> -RecoverReviewedSource
```

Adoption from a fresh same-card Code worktree uses `-FromTask <source>` and a Review whose subject is that source (`docs/orchestration-loop.md:214-240`, `docs/ops-http.md` land and recovery rows). There is no empty-diff body. The only patch master does not already contain is `5acf597f1` (`tests/derived-impact-index.json`). A land of this tip is that index hunk, not an empty commit, and `AlreadyPresent` is exact remote containment (`docs/orchestration-loop.md:2526-2527`), which this tip does not have. Publishing that generated index is a separate card decision. It is not required to pass this census.

## 4. Flags, and when the wait expires

`deploy-server2.ps1` parameters (`:12-26`): `-LandWaitMinutes` defaults to 30, range 0..1440. There is no `-Report` parameter. `-Report` is an internal switch on `Assert-RecycleTaskCensus` (`:748`). Only `-Phase check-census` passes it (`:1528-1541`), and that phase counts `boundLandPending` instead of throwing. It authorizes nothing (`docs/docker-stack.md:31-41`).

`Wait-RecycleTaskCensus` (`:943-976`) starts its deadline at entry, `UtcNow + LandWaitMinutes`, then polls every 30 seconds. The first read is immediate. `-LandWaitMinutes 0` refuses on that first read. On expiry the throw is `RecycleLandInFlight <id> runner=<runner> waited=<n>polls`. The receipt field is `landWait`. No parameter skips a bound `RecycleLandInFlight`. `docs/docker-stack.md:10-11` and `:273-282` say a refusal stops the phase; do not force it. `-DryRun` and `-ResumeRecycle` still enter this wait when the build version, resume, or dry-run condition holds (`:1415-1430`). They are not an override of the land guard.

The phase was started at 02:18Z. The 30-minute clock starts when the census function is entered, after the host-jq check, runner status, zero counters, and drain checks, not at process start. A wait entered at 02:18Z expired at 02:48:00Z, before the sweep clear at 02:48:42Z. Those pre-checks are normally short, so the likely deadline was about 02:48Z–02:50Z. This runner cannot see the desktop process. At the 02:53:49Z read the row was already clear, so a process still inside the wait passes on its next poll, and a process that already exited needs a new invocation. A second `redeploy-old` must not be started while the first process is alive.

## Recommended command

If the 02:18Z process has exited `RecycleLandInFlight ad11c378-16c7-4e0d-bffd-04e440b33d63 runner=server2 waited=<n>polls`, rerun:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase redeploy-old
```

The first census read is immediate. With no `server2`-bound pending land it does not spend the 30 minutes.
