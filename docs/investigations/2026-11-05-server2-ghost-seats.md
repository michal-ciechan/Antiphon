# Server2 ghost and stale seats

Date: 2026-11-05. Investigate task `3c2aac7d-8e14-478f-9096-b4162ee9db09` on `feat/card-task-3c2aac7d`. Read-only: no process was stopped, no seat was released, no product code was edited.

Observed at 2026-11-05 00:59Z from the server2 runner container. `GET /api/session-runners/server2/slots` returned `declaredCapacity=10`, `occupied=9`, 11 remembered sessions. The same read at 00:48Z was already 9 occupied (the 00:45Z orchestrator sample was 8). One seat is free.

`docker exec antiphon-postgres` was not available here. This container's Docker socket is the nested daemon (one unrelated container, not `antiphon-postgres`). Host `172.30.0.1` refused TCP 5432 and 17280, and this user has no SSH key. Session and task rows below are the desktop API's projection of `AgentSessions` and `AgentTasks` (`GET /api/agent-tasks/{id}`), plus the runner process table and on-disk transcripts. `SessionStatus.Running = 2` (`server/Domain/Enums/SessionStatus.cs:8`), which is the integer the orchestrator already read.

## Server2 seats

Ages are the slot `ageSeconds` at 00:59Z. CPU is `ps` at 00:55Z. "Last transcript" is the last native record, not the runner pointer file.

| Session | Task | State | Class | Age | Safe action |
|---|---|---|---|---|---|
| `303df159-aadd-479e-abdc-ec70d6bf85bb` | `f1a11dd3` CARD-1661 Code Failed | claude pid 7285 alive, 1.1% CPU, no children. DB Running, `working=false`, LastSeenAt and last transcript 2026-11-03 22:39:55Z. Agent `task-f1a11dd3` Idle since 22:39:32Z. `openTask` empty, `orphan=false`. | GHOST | 26.7h | Single-seat release below. Mirror clean. |
| `c1a9c8f9-621d-4f4b-8037-aa683134e3b9` | `aa58fb53` CARD-1651 Code Failed | claude pid 20548 alive, 1.1% CPU, no children. DB Running, `working=false`, LastSeenAt and last transcript 2026-11-04 09:29:19Z. Agent `task-aa58fb53` Idle since 09:28:49Z. `openTask` empty, `orphan=false`. | GHOST | 15.6h | Single-seat release below. Mirror clean. |
| `c8ac56ad-8f36-44ea-a621-aec1f06daa8f` | `34125850` CARD-1514 Merge Blocked | claude pid 19840 alive, 1.9% CPU. DB Running, `working=false`. Last transcript 2026-11-04 10:08:09Z (`turn_duration`). LastSeenAt 11:23:09Z. `openTask` set, `orphan=false`. | STALE-BLOCKED | 16.2h | Do not force-release until the Decide is answered or the task is canceled. Two unpushed doc commits. |
| `9b09c47b-8ec7-43d4-ac1e-f3dcf55babbe` | `880edccc` CARD-1514 Merge Blocked | grok pid 23153 alive, 4.4% CPU, no child. DB Running, `working=false`. Last transcript 2026-11-05 00:26:49Z. Blocked round 1 at that time. | BLOCKED-LEGITIMATE | 1.2h | Leave the seat. Answer the land-graph question. |
| `3078cca3-af6c-449f-bcf5-09be01cdbd43` | `613f2230` CARD-1714 Mutation Dispatched | grok pid 7870 alive, 6.7% CPU, bash child since ~00:33Z. DB `working=true`. Last transcript 00:56Z. | WORKING | 1.8h | None. |
| `85ea2ee3-be22-44c8-b385-e2babfd50a51` | `73c94e0b` CARD-1717 Code Dispatched | grok pid 32402 alive, 8.2% CPU. DB `working=true`. Last transcript 00:54Z. | WORKING | 1.1h | None. |
| `f5294392-8ae0-49af-910f-b61319751847` | `362f7434` CARD-1717 Code Dispatched | grok pid 635 alive, 8.0% CPU, child compile still running at 00:55Z. DB `working=true`. Last transcript 00:52Z. | WORKING | 1.1h | None. |
| `8197ef51-6f67-41f5-a0de-4a5e40473684` | `5e05fc80` CARD-1718 Code Dispatched | grok pid 20845 alive, 15% CPU. DB `working=true`. Last transcript 00:56Z. | WORKING | 11m | None. |
| `feaccecf-7f8a-438e-991b-2964e561b477` | `3c2aac7d` this Investigate | grok pid 15790 alive. DB `working=true`. | WORKING | 11m | None. |
| `90baac1a-4956-4b36-9c0c-1335d8fd66cd` | `3fd6747e` CARD-0824 Review Failed | Exited, pid 8448 absent. DB session Failed, ended 2026-11-04 23:14:57Z. SIGKILL, stop request recorded. `occupiesCapacity=false`, `orphan=true`. | not a seat | 1.8h | None. |
| `2437ec69-6812-4973-be56-8611611ab206` | `7c7132b3` CARD-0824 Review Failed | Exited, pid 2772 absent. DB session Failed, ended 2026-11-04 23:30:41Z. SIGKILL, stop record not durable. `occupiesCapacity=false`, `orphan=true`. | not a seat | 1.5h | None. |

A twelfth manifest, `95eb6ccd-9024-4f81-820f-b9030cbc611e` (Merge `406bca79`, Succeeded, `KilledByRequest` at 2026-11-04 20:07:27Z), is not in the slot list and its pid is gone. It does not occupy a seat.

## Why the two ghosts still hold seats

Both tasks failed the same way. `failureCode=CompletedWithoutProgress`. The stored reason is `runner_reported_commit_not_pushed`, and the settlement text already said the mirror was `equal, dirty=false, inspection=ok`:

- CARD-1661 mirror `/work/worktrees/task-f1a11dd3` at `97e77f9f1fd6f71e2719bc661b48f8f89f129029`
- CARD-1651 mirror `/work/worktrees/task-aa58fb53` at `dd30b167ac522e5462f52f3307ef861e648fb0bf`

Re-checked at 00:55Z without fetching: `git status --porcelain` is empty on both, and `git ls-remote origin` for both branch tips equals those SHAs. Nothing unpushed, nothing dirty.

Each agent has one `DelegateCompletedWithoutProgress` incident at the failure instant, then an `AutoCompactFailed` (`BoundaryTimeout`) about ten minutes later. There is no `DelegateReleaseUnresolved` incident. The compact attempt is evidence the process was still alive after settlement, not the reason it was kept.

The terminal path did run, and it deliberately did not kill:

1. A Code Worktree `done` with no attributed push is Failed with `CompletedWithoutProgress` (`AgentTaskReplyService.TryClassifyCompletedWithoutProgressAsync`, `server/Application/Services/AgentTaskReplyService.cs:3894-3994`).
2. Settlement then sets `killSession` false for that failure code only (`AgentTaskReplyService.cs:1558-1562`). The comment at `1513-1515` says the zero-progress failure keeps the worktree for inspection and still calls release.
3. `ReleaseDelegateAsync` (`2848-2924`) reaches `TryHandleTaskAsync` first. `TerminalRunnerSeatReleaseOptions.AutomaticEnabled` defaults false (`TerminalRunnerSeatReleaseService.cs:17`) and `TryHandleTaskAsync` returns false immediately when it is off (`:71`). Parking is also off, so the Blocked arm in that method is not what held these Failed tasks.
4. With `killSession` false the method calls `PoolDelegateRelease.MarkIdleForJanitor` and returns (`2908-2924`). It does not call `KillAsync`. Both agents are Idle, `alwaysOn=false`, no board, no standing owner, updated at the completion timestamp.
5. That Idle row with `PoolIdleSince` set is the warm-pool mark. `SeatDesktopJoin` treats `Agent.Status == Idle && PoolIdleSince != null` as pooled (`SeatDesktopJoin.cs:100-106`). `RunnerSlotService.IsOrphan` is false for a warm pool delegate even when there is no open task (`RunnerSlotService.cs:31-32`). That is why both slots are `orphan=false` with an empty `openTaskId`. `release-orphans` skips them (`RunnerSlotService.cs:137`).
6. The dispatcher's pool-release sweep only selects pool delegates whose `PoolIdleSince` is null (`AgentTaskDispatcher.cs:9222-9226`). After step 4 these rows are invisible to the sweep that would have killed a forgotten terminal delegate (`9202-9210`).
7. The warm-pool janitor then refuses the kill. A proved Running session whose current task is Failed is added to `failedLiveSessionIds` (`AgentTaskDispatcher.cs:9469-9470`, rule in `FailedTaskSessionOwnership.cs:8-13` and `35-38`). Those agents are removed from retirement and logged as detection only (`9494-9516`: "the session is never killed"). The same sentence is the owner doc at `docs/session-runtime-invariants.md:474`. `PoolIdleRetireMinutes` defaults to 60 (`DelegationSettings.cs:1064`) and does not apply once the Failed-session hold matches.

So the process-release rule (killed, pooled warm, or owned by a standing agent) was satisfied on paper by the warm mark, and the later Failed-session hold then makes that warm mark permanent. The seat stays Running and counts toward the 10.

Task detail says `failedSession.summary = "session state unknown: release unconfirmed"` and `activity=unknown` (`FailedTaskSessionFacts.SeatHeld` vs `ReleaseUnknown`, `FailedTaskSessionFacts.cs:14-15`). That wording is only the two-minute listed-inventory check (`:39-61`). The database status is Running, the runner lists the session Running, and the pid is the process that started at dispatch (`acceptedStartedAt` equals `DispatchedAt`). The hold does not depend on that snapshot sentence.

## Safe release for the two ghosts

Preconditions, re-checked immediately before the call: the task is still Failed `CompletedWithoutProgress`, the slot's `openTaskId` is still empty, and `git ls-remote origin refs/heads/feat/card-task-<id>` still equals the mirror HEAD (both were equal and clean at 00:59Z). Do not use `release-orphans`; `orphan=false`, so the sweep will not select them.

Run where the operator token file is (the desktop). This runner does not have it.

```
pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId server2 -SessionId 303df159-aadd-479e-abdc-ec70d6bf85bb -Reason "CARD-1661 Failed CompletedWithoutProgress; mirror clean and pushed; seat still Running"
pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId server2 -SessionId c1a9c8f9-621d-4f4b-8037-aa683134e3b9 -Reason "CARD-1651 Failed CompletedWithoutProgress; mirror clean and pushed; seat still Running"
```

That is `POST /api/session-runners/server2/slots/{sessionId}/release` with `{ "reason": "..." }` and `X-Antiphon-Operator-Token` (`docs/ops-http.md` runner seats). It kills the process tree, deletes the runner manifest, stops the desktop session row, and writes `RunnerSlotForceReleased`. It does not delete the worktree. Re-read `GET /api/session-runners/server2/slots` afterwards. Releasing both drops occupied from 9 to 7 if nothing else launches.

## The stale blocked cut

`34125850` ("Cut the new rc line from master (rc/2026-11-05)") has been Blocked since 2026-11-04 10:08:10Z, round 2, `canAnswer=true`, `next=Decide`. The question says master moved past the pin and the report push was still failing. `refs/heads/rc/2026-11-05` is `1470340559889c2985ea722173181ea25c51b47c`, the commit named in that question, and that commit is an ancestor of the task HEAD. The seat is idle, not cutting.

The mirror `feat/card-task-34125850` is clean and two commits ahead of `origin/feat/card-task-34125850`:

- `8d88a05d5` docs(rc-cut): rc/2026-11-05 cut blocked
- `72a8014a9` docs(rc-cut): rc/2026-11-05 cut at the pin 147034055

Those two commits are local only. A seat release would not delete them; a later worktree deletion would. Answer or cancel is the task operation. Force-release is only after that decision, and only if those two commits are pushed or judged disposable.

`880edccc` is a real block from 00:26Z: origin/master moved, land-graph digest `76C9D73E`, fast-forward of `595c46a4e` rejected. Mirror clean, zero ahead of `origin/feat/card-task-880edccc`. Leave it.

## Desktop, briefly

`GET /api/session-runners/desktop/slots` at 00:48Z: `occupied=13`, `declaredCapacity` empty in the payload, 25 remembered sessions. Thirteen Running seats occupy capacity. This pass did not bind the nine orphans to tasks.

| Session | Task | State | Class | Age | Safe action |
|---|---|---|---|---|---|
| `df3b5125-d4de-4f31-b155-2674d4740a98` | `6d09bed7` Diagnose Dispatched "labels for CARD-1627" on standing `antiphon-diagnose` | DB Running, `working=false`, no last transcript. Task dispatched 2026-11-04 17:15Z onto a seat whose slot age is ~239h. | stale Dispatched standing seat | ~239h | Not a ghost. Needs a desktop pass before any release. |
| `cea73d57-3072-4cc2-8cb0-ab859e7d3415` | `53437ee8` Check Dispatched "check #1 on task 7d4b009e" on `antiphon-check-interpreter` | DB Running, `working=false`. Last transcript 2026-10-21 15:11Z. LastSeenAt 2026-11-05 00:42Z is a heartbeat. | stale Dispatched standing seat | ~239h | Same. |
| `067f9d0e-dafe-414f-b80c-89ad9f35a9f2` | `21a6fc03` Distill Dispatched "distill task 59ab5d9a" on `antiphon-output-distiller` | Task dispatched 2026-11-04 22:33Z. Last transcript 2026-10-28 16:53Z. `working=false`. | stale Dispatched standing seat | ~222h | Same. |
| `c66c4208-3bf5-43b3-8c71-4e2de01b99fe` | `250a07fb` CARD-1514 Custom Blocked "Start Windows Full rc run on rc/2026-11-01" | Blocked since 2026-11-01 10:27Z. Last transcript 2026-11-01 18:29Z. `rc/2026-11-01` exists (`10a826b9`). | STALE-BLOCKED | ~86h | Answer or cancel on the desktop. Do not force-release from this note. |
| nine `orphan=true` Running seats | no `openTaskId` | Seven have slot age 228–239h (`4f71db42`, `d1880154`, `b2f1a869`, `b788e4ec`, `8c1d3da6`, `384d59ee`, `e1626153`). Two are ~0.7h (`fb38e751`, `b8aad212`). Not warm-pool (`orphan=true`). | occupying orphans, tasks not bound | up to ~239h | Identify each before `release`. Not done here. |

## Not done, noted

The zero-progress path should not be able to mark a worktree delegate warm and then have the Failed-session hold exempt that same row from both the sweep and the janitor.

## Uncertainties

- `AutomaticEnabled` was not read from the live server config. The code default is false, and the observed Idle-without-kill outcome is what that default does. A production override to true would have returned from `TryHandleTaskAsync` before `MarkIdleForJanitor`; these agents are Idle, so that return did not take the seat.
- The "release unconfirmed" sentence was not traced to a specific occupancy-sample row. It does not change the Running pid.
- Desktop orphan session ids were not joined to their last task.
- `34125850`'s two doc commits were compared with the mirror's `origin/feat/card-task-34125850` ref and `git log`, not a second `ls-remote` of that branch.
