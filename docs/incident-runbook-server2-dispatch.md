# Server2 dispatch incidents, 2026-11-17

Two holds stopped new server2 work on the same night. They are different faults. Running sessions kept running in both. Read the hold text, then the matching section. The triage table is in section 3.

If the hold contains `RunnerUnavailable` and `has not completed recovery`, use section 1. If it contains `workspace_slot_pool_degraded`, use section 2.

Line numbers for code and for `docs/docker-stack.md` and `docs/ops-http.md` are this checkout, master `300c8a99d0d267a39791410b7540189ab702f251`. The CARD-1964 plan states that the phone-home files it touches match the desktop server that was running, `870a93d21f2b18c85a8b0905ab689b28e3099813`. This runbook did not re-diff that claim.

Sources, read at these SHAs:

- Phone-home investigation: `docs/investigations/2026-11-17-server2-phone-home-stale.md` on `origin/feat/card-task-e5d7e9da`, commit `7e105faca3a5b938b89919f368489d4e7226cf9d`. Not in master `300c8a99d`. Debug e5d7e9da. That note restarted nothing.
- Slot-pool investigation: `docs/investigations/2026-11-17-slot-pool-degraded-incident.md` in this tree (landed on master after batch 256).
- CARD-1960 / CARD-1961 plan: `docs/superpowers/plans/2026-11-17-card-1960-1961-workspace-slot-fixes-plan.md` on `origin/feat/card-task-87ebe4f0`, commit `1cb47ef5300ab1b7f053148b824d4d406d8a705b`. Plan only.
- CARD-1964 plan: `docs/superpowers/plans/2026-11-17-card-1964-phone-home-pump-stall-plan.md` on `origin/feat/card-task-2bef90c6`, commit `125222cb4072b9e19ce3e68711ea0b341f8d5ac9`. Amendment 1 answers round-1 review 21daa066. Round-2 review is pending. Plan only.

## 1. Incident A: stale phone-home runner

### Symptom

New server2 tasks stay Queued. The hold text from `DispatchHoldDetails.RunnerUnavailable` (`server/Application/Services/DispatchHoldDetails.cs:103-104`) is:

`Held: RunnerUnavailable: runner 'server2' is not dispatch-eligible (Phone-home runner has not completed recovery.); the task stays Queued.`

The parenthetical is the exception at `PhoneHomeRunnerDirectory.cs:164`. Tasks already running on server2 keep running. The desktop server in the investigation was `870a93d21f2b18c85a8b0905ab689b28e3099813`.

### Confirm

Read both routes. They do not say the same thing while the socket is still up.

`GET http://localhost:17202/api/session-runners` (catalogue, `docs/ops-http.md:138`). For `server2` while it is not dispatch-eligible and not draining or retired, the row is `available: false`, `dispatchEligible: false`, `unavailableReason: stale`, `capacity: null`. That projection is `PhoneHomeRunnerDirectory.DescribeAsync` (`PhoneHomeRunnerDirectory.cs:831-839`: `Available` and `DispatchEligible` are the same eligible bit, `Stale` is its inverse, `Capacity` is set only when eligible) plus `SessionRunnerCatalogue.IneligibleReason` (`SessionRunnerCatalogue.cs:123-124`) and `Row` (`:137-150`). `docs/ops-http.md:148` names the same reason order: for a runner that is not dispatch-eligible, `unavailableReason` is `retired`, then `draining`, then `stale` or `unavailable`.

`GET http://localhost:17202/api/session-runners/server2/status` (`docs/ops-http.md:149`). `available` here means the socket is open and the lease is inside 90 seconds (`PhoneHomeRunnerDirectory.cs:709-710`, `PhoneHomeContracts.cs:21`). It can be `true` while `dispatchEligible` is `false`.

Measured at 03:25-03:28Z, 15 samples (investigation, timeline and verdict 2): `available: true`, heartbeat about every 15 seconds, `pendingEvents` 0 (2 once), `disconnectReason` null, `epoch` 1, `dispatchEligible: false`, `lastCatchUpMs` frozen at 30. A successful List is what writes `lastCatchUpMs` (`PhoneHomeRecoveryPump.cs:290`). Frozen `lastCatchUpMs` plus `dispatchEligible: false` is this incident.

`disconnectReason: lease_expired` appears on the status route only while status `available` is false (`PhoneHomeRunnerDirectory.cs:723-731`). After heartbeats resume, that field goes null and eligibility stays false. That second shape is the one that sat for 35+ minutes.

`GET /api/session-runners/server2/slots` returned 503 during the investigation. It uses the same gate (`PhoneHomeRunnerDirectory.cs:163-164`). A 503 there does not mean the container is down.

### Server log

Desktop server log for this night: `C:\src\Antiphon\server\logs\antiphon-20261117.log` (the `server/logs/antiphon-<yyyyMMdd>.log` file on the main checkout). Grep these phrases:

- `Phone-home inventory refresh List for runner server2 epoch` and `failed (phone_home_request_timeout)`. Template: `PhoneHomeRecoveryPump.cs:264-266`. The code name is `phone_home_request_timeout` (`PhoneHomeContracts.cs:554`). The investigation has three of these, at 02:49:03, 02:50:04 and 02:51:05Z.
- `Phone-home inventory refreshes for runner server2 epoch` and `failed 3 times in a row` (`PhoneHomeRecoveryPump.cs:268-272`). Logged at 02:51:05Z.
- `Phone-home catch-up List for runner server2 failed` (`PhoneHomeRecoveryPump.cs:363-366`) and `Phone-home recovery pump cycle failed` (`:73`). Neither has appeared since 02:51 in the investigation. Their absence, with eligibility still false, is the stall. The pump is not retrying on a timer you can see.
- `Phone-home connection server2 epoch` plus `accepted:` (`SessionRunnerEndpoints.cs:311-314`) or `ended:` (`:422`). The earlier restart the same night ended epoch 2 with `request_aborted` at 00:25Z and accepted epoch 1 at 00:29:13Z.
- `Runner server2 unavailable for` (`RunnerAlarmCoordinator.cs:134`). Logged at 02:53:47Z as unavailable for 3.0 min. The `{Reason}` on that line is the status disconnect reason at the alarm (`snapshot.DisconnectReason`, `RunnerAlarmCoordinator.cs:135`). That it was the lease expiry is an inference: the investigation does not quote the field (verdict 1).
- `has not completed recovery` on the task hold, and later `released: hold cleared` (`AgentTaskDispatcher.cs:1153-1155`).

A lease expiry writes no log line of its own. `ReadSnapshot` clears `DispatchEligible` and returns (`PhoneHomeRunnerDirectory.cs:984-994`). The CARD-1964 plan (G-6) confirms there is still no logger there. Do not wait for a line that says the lease expired.

### Leave these alone

Do not restart the server2 runner container. Treat the sessions and pty hosts inside it as killed. Pty survival across a container restart is unverified, and the investigation calls that restart the worst recovery (investigation, recovery option 3). Running sessions and seats stay where they are.

Do not delete a worktree, a ledger, a seat, or a lock to clear this. Drain and drain/clear do not restore eligibility. `PUT /api/session-runners/server2/capacity` needs an eligible runner (investigation, recovery option 2). There is no operator route that restarts only the phone-home connection.

### Recovery

Restart the desktop AppHost from the main checkout (`C:\src\Antiphon`), not from a linked worktree. A worktree refuses with exit 3 and kills nothing (`docs/bootstrap.md`, restart gotcha; `docs/apphost-runbook.md:45-47`).

Check `logs/apphost.restart.lock` and `logs/apphost.launch.lock` first. Exit 3 means another restart or launch is in flight. A stamp younger than 15 minutes is still in flight even if the holding pid has exited, unless the dead-launch proof in the AppHost runbook says otherwise. Before the restart, `GET /api/version` must equal `git -C C:\src\Antiphon rev-parse HEAD`: the script builds that checkout and requires the served SHA to match HEAD (`scripts/restart-apphost.ps1:56-57`, `:187-214`, exit 5 at `:426-437`). `-ExpectedServerSha` is refused unless it already equals that HEAD, so it does not preserve an older served build.

```powershell
pwsh -NoProfile -File scripts/restart-apphost.ps1
```

What that does, from `docs/apphost-runbook.md:37-47` and `:76-85`, and `docs/bootstrap.md` (the restart gotcha and the watchdog paragraph): the script POSTs `/api/operator/shutdown` with reason `restart-apphost` (`docs/ops-http.md:154`), waits for that server pid to exit (default 20 seconds), then relaunches AppHost. The desktop session runner on port 17204 is not stopped. Postgres stays up. The server2 runner container is not part of this restart. The new server process accepts a new phone-home epoch; the sessions already running in the server2 container stay up, and the new connection's catch-up re-reads them (investigation, recovery option 4). The script reads the operator token and does not print it.

After the CARD-1744 window, production recovery is `scripts/restart-linux-server.ps1` and `scripts/restart-apphost.ps1` is the desktop rollback, and the Docker server's roles soft rollback is a server restart with `Delegation__WorkspaceSlotRoles=none`. That window is not recorded as run, so until then the desktop command above and the section 2 hot reload stay the production path (`docs/docker-stack.md`, Production server on the server2 host, and `docs/docker-stack.md:1064-1065`).

Success, all of these:

- `GET /api/session-runners/server2/status` shows `dispatchEligible: true`.
- The catalogue row shows a non-null `capacity` and `unavailableReason` null. `capacity` is omitted from the catalogue until the runner is dispatch-eligible (`SessionRunnerCatalogue.cs:90-91`).
- `lastCatchUpMs` moves. A healthy pump refreshes the List every `InventoryRefreshSeconds`, default 30 (`PhoneHomeRunnerSettings.cs:78`, `PhoneHomeRecoveryPump.cs:236-244`). The investigation's success line is a `lastCatchUpMs` that changes on that cadence.
- The server log has a new `Phone-home connection server2 epoch <N> accepted:` line.
- Held tasks log `Task <id> released: hold cleared` (`AgentTaskDispatcher.cs:1154`). The investigation named tasks `03afb6f5` and `3871f961` as the holds to watch.
- `GET /api/version` is unchanged from the SHA you read before the restart. For the diagnosed server that SHA is `870a93d21f2b18c85a8b0905ab689b28e3099813`. The script exits 5 if the served SHA is not the main checkout's source-root HEAD (`docs/apphost-runbook.md:40-42`). Do not pull or switch that checkout first. A restart builds whatever is on disk there (`docs/bootstrap.md`, the master-pointer gotcha).

The caller of this runbook records that this restart cleared the incident, with the new epoch accepted within about two minutes. Debug e5d7e9da did not run a restart (investigation lines 3 and 142), so that two-minute window is not a timestamp in the investigation. The only timed reconnect in that note is the earlier activation the same night: epoch 2 ended at 00:25Z, epoch 1 accepted at 00:29:13Z.

### Cost during a Windows qualification

An AppHost restart during a live Windows qualification can flake host-load tests. The investigation names run `06863906` and the C1514 host-load test, and says the flake can be rerun (investigation, recovery option 4). Prefer a gap between chunks when one is close. After the restart, read that run's chunk list. A host-load failure in a chunk that overlaps the restart is a rerun of the chunk. It is not a fixer and not a new card. The investigation does not record a chunk-list path. Where that run publishes its chunk list is UNOBSERVED in the sources for this runbook.

### Optional capture before the restart

The exact await the pump is parked on is UNOBSERVED. The investigation says that needs `dotnet-dump collect -p <server pid>` and `dumpasync`, which that read-only pass did not take.

If you take a stack first: `dotnet-stack report -p <server pid>`. That pid is the server project AppHost adds with `AddProject` (`Antiphon.AppHost/Program.cs:67`), not the session-runner executable and not a test host. A stack report does not show a parked async await, so it will not show the `WaitAsync` the investigation suspects (`SessionStateStore` gate or the phone-home send gate). Do not treat an empty-looking stack as proof the pump is idle. Skip the capture if server2 needs to accept work now. The restart does not depend on it.

### Durable fix

CARD-1964 is the fix for this path: bound the recovery steps, log every lease expiry and every refusal, and make lease re-recovery require only the List. The plan is on `origin/feat/card-task-2bef90c6` at `125222cb4072b9e19ce3e68711ea0b341f8d5ac9`. Amendment 1 answers round-1 review 21daa066 (Grok). Round-2 review is pending. No code from that plan is on master `300c8a99d`.

When it lands, activation is an AppHost restart from the main checkout at the new rc tip, and no server2 runner change (plan, Rollout). That later restart is a code activation: `GET /api/version` must equal the rc tip. It is not the "version unchanged" recovery above. Time it around any live Windows qualification the same way. Rollback in the plan is four `PhoneHomeRunner` timeout settings set to 0, then another restart. List-only re-recovery has no setting of its own.

## 2. Incident B: workspace slot pool degraded

### Symptom

Queued tasks stay Queued and retry. The server log line is `Remote workspace preparation for task <id> failed (attempt <n>); retry not before <time>: ...` (`RemoteWorkspacePreparer.cs:368-369`). The detail contains `workspace_slot_pool_degraded` and `the task stays Queued`.

The Held event is the slot hold, not that warning sentence (`RemoteWorkspacePreparer.cs:358-359`):

`workspace_slot_unavailable: workspace_slot_pool_degraded. Runner 'server2' has no workspace slot within its bound; the task stays Queued and is retried, never given a per-task mirror.`

`workspace_slot_pool_degraded` is `WorkspaceSlotCodes.PoolDegraded` (`WorkspaceSlotContracts.cs:12`). The preparer treats it as a hold and does not fall back to a `task-<id>` mirror (`RemoteWorkspacePreparer.cs:345-359`, `WorkspaceSlotContracts.cs:64-65`).

Backoff is 30 seconds times 2^(attempt-1), capped at 900 seconds (`RemoteWorkspacePreparer.cs:105-111`, `DelegationSettings.cs:734` and `:752`). The cap is 15 minutes. The investigation saw the backoff grow to 8 minutes, which is the 480-second step.

### Cause

The runner refuses a new slot lease when the number of open quarantined directories is greater than `MaxQuarantinedDirectories` (`RunnerWorkspaceSlotPool.cs:95-96`). The default is 4 (`PhoneHomeWorkspaceSlotSettings.cs:24`). The fifth open entry degrades the pool for every role. Spare free slots do not help. The check is the open count only.

`IsOpen` is `ClosedAt is null` (`RunnerWorkspaceSlotLedger.cs:78`). The ledger is written with camelCase (`RunnerWorkspaceSlotLedger.cs:109`), so the JSON fields are `quarantinedDirectories`, `isOpen`, `closedAt`.

At 01:13Z the investigation counted 5 open entries. At 01:40Z the same five were still open. Anything after 01:40Z is UNOBSERVED (CARD-1960 plan, G-11). Count again before you act.

### Count the open quarantines

Read the ledger inside the standing runner container. The investigation used container `antiphon-runner-session-runner-1` and path `/work/workspace-slots/ledger.json`. Print only `dirId`, `slotId`, `class`, `reason`, `branch`, and `bytes`. Do not print paths, heads, errors, or file contents, and do not paste the ledger. The runner image (`docker/session-runner-grok/Dockerfile`, target `session-testing`) installs `jq` and does not install `python3`. This count runs `jq` inside the container. No command in this runbook runs `python3` on the server2 host.

On the server2 host, bash:

```bash
docker exec antiphon-runner-session-runner-1 jq -r '[.quarantinedDirectories[]|select(.isOpen)] | "open \(length)", (.[] | "\(.dirId) \(.slotId) \(.class) \(.reason) \(.branch) \(.bytes)")' /work/workspace-slots/ledger.json
```

From the desktop, pwsh (PowerShell 7). The remote command is one single-quoted argument, so the jq program's double quotes and backslashes are not expanded by pwsh. The PowerShell form is UNTESTED on the desktop: verify it there by a first line `open <n>` and one line per open row. A jq error means the quotes were mangled. Do not paste the ledger.

```powershell
ssh -o BatchMode=yes -o ConnectTimeout=30 mc@server2 'docker exec antiphon-runner-session-runner-1 jq -r ''[.quarantinedDirectories[]|select(.isOpen)] | "open \(length)", (.[] | "\(.dirId) \(.slotId) \(.class) \(.reason) \(.branch) \(.bytes)")'' /work/workspace-slots/ledger.json'
```

`open` greater than 4 is the degraded pool. The 01:13Z rows, from the investigation table, were:

| dirId | slotId | class / reason | branch | GiB |
|---|---|---|---|---|
| 66ece2d86b23 | ws-01 | filesystem/reset | feat/card-task-2b159393 | 2.46 |
| c139d63622e5 | ws-04 | custody/lingering | feat/card-task-cd9b15c9 | 2.28 |
| 7ef5203b564f | ws-03 | filesystem/reset | feat/card-task-92c62298 | 0.42 |
| 2d0ef6e62dae | ws-01 | custody/lingering | feat/card-task-a0639c99 | 2.28 |
| 7b3f3b265073 | ws-01 | filesystem/reset | feat/card-task-e2208352 | 0.46 |

### Immediate mitigation

On the desktop main checkout, set `Delegation:WorkspaceSlotRoles` to `none` in `C:\src\Antiphon\server\appsettings.Development.json`. That file is local and is not in git. The value is a single string: `none`, not an array and not `[]` (`docs/docker-stack.md:1062-1063`, `WorkspaceSlotRoles.cs:7-10`).

The desktop AppHost hot-reloads that file. No restart. A hot reload applies only while no `Delegation__WorkspaceSlotRoles` environment variable shadows it (`docs/docker-stack.md:1066-1068`). Confirm the next task's `WorkspaceSlotDecision` event says `reason=roles_none`.

Queued tasks start on their next backoff retry, still up to 15 minutes later. The investigation changed the setting at about 01:26Z. The four queued tasks started on their existing retries between 01:27:42Z and 01:39:26Z, on `task-<id>` mirrors, with no restart. Cancel and re-create a task only when that wait matters.

This was done for the incident. Whether the live file is still `none` is UNOBSERVED from this runbook. Read the file before you change it.

### Leave these alone

Do not remove a quarantined directory by hand. `docs/docker-stack.md:1053-1056`: the directory stays `git worktree lock`ed, and the exit is the phone-home operation `WorkspaceSlotQuarantineRelease`. Do not edit `ledger.json`. Do not delete the directory from the host or from inside the container.

Do not restart the server2 runner to clear the count. Open quarantines are on the volume. A restart does not close them, and it drops the leased sessions.

Do not raise `PhoneHome__WorkspaceSlots__MaxQuarantinedDirectories` as the fix. The investigation's option 2 needs a runner recreate, drops the sessions leased at the time (ws-02 and ws-04 at 01:13Z), and only delays the next refusal.

### Release today, and release after CARD-1960

Today, no server route and no script sends `WorkspaceSlotQuarantineRelease`. The runner handles phone-home operation 44 (`PhoneHomeCommandDispatcher.cs:488`, `RunnerWorkspaceSlotPool.ReleaseQuarantineAsync` at `RunnerWorkspaceSlotPool.cs:942`). The server has `cold-reset` (`SessionRunnerEndpoints.cs:153` in the investigation's citation; the ops page is `docs/ops-http.md:408`) and `{slotId}/record-release` (`docs/ops-http.md:413`). `docs/ops-http.md:427-430` lists `WorkspaceSlotQuarantineRelease` under phone-home operations and says there is no HTTP route on the runner yet. `scripts/workspace-slots.ps1` has `cold-reset` only (investigation). `POST /sessions/workspace-slots/quarantine/{dirId}/release` does not exist. There is nothing to call.

After CARD-1960 is live, the plan's route is `POST /api/session-runners/{runnerId}/workspace-slots/quarantine/{dirId}/release` with the operator token and body `{disposition, reason, requester}`. Disposition is `Pushed`, `AcceptLoss`, or `Removed`. The script verb is `release` (`workspace-slots.ps1 -Runner <id> -DirId <12 hex> -Disposition Pushed -Reason <text>`). That route is not in this checkout. CARD-1960 needs an AppHost restart and no server2 rollout (plan, rollout step 1). CARD-1961 is the runner change and waits on the server2 rollout, which the plan says is held for CARD-1893.

The investigation's five entries passed the runner-equivalent unpushed and dirt checks at 01:15-01:18Z, so the plan says release them as `Pushed` once the route exists, one at a time, and re-read the ledger after each. Copy `.antiphon/c1956-review/` out of `2d0ef6e62dae` first if that Review evidence is wanted. `Pushed` deletes ignored residue. Re-check that the old lingering pids are gone before releasing the two `custody/lingering` rows. A `refused` or `removal_failed` answer stops the sequence. That entry stays for the operator.

### Why the quarantines opened

Release runs a warm `git clean` that excludes `obj/`, `bin/`, and `node_modules/` at any depth (`WorkspaceSlotContracts.cs:100` and `:107-120`, cited by the investigation). The exclusion keeps a nested `artifacts/obj` inside `bin-<card>/`, so the parent directory remains. The free-check then rejects that parent because its path segment is `bin-<card>`, not `bin`. The retry runs the same clean. The slot is quarantined `filesystem/reset` and re-homed. That is CARD-1961.

Checkpoint runs produce `bin-<row>/artifacts/obj` on purpose (`tools/Antiphon.Checkpoints/BuildConfinement.cs:508-540`, cited by the investigation). A briefing rule that forbids a custom output directory does not stop the checkpoint tool from writing one. Until CARD-1961 is on the server2 runner, do not put a custom `OutputPath` or a `bin-<name>/` directory in a task brief (the ordinary build rule is native default output, `docs/testing-and-build.md`, CARD-1629). Do not turn slot roles back on for Code, Debug, Review, or any other role that runs a checkpoint until the re-enable sequence below says so. Test fixtures that drop a `bin/` under an ignored control directory quarantine a slot the same way.

Custody `lingering` is separate: a `sleep` left in the background. Those entries do not close themselves.

### Re-enable sequence

Do not start this while CARD-1960 is still a plan. R-0 needs the release route live. R-2 needs CARD-1961 live on server2. The setting stays in `C:\src\Antiphon\server\appsettings.Development.json`. Every rollback sets `none` and waits for a `WorkspaceSlotDecision` of `reason=roles_none`. With leases still out, leave runner slot support enabled until those leases release (`docs/docker-stack.md:1045-1047`).

Read, at every step: the open-quarantine count from the command above, `attention` and `rehomes` in the same ledger (print those fields only), and the server log for `workspace_slot_pool_degraded`.

| Step | Needs | Setting | Advance when | Roll back when |
|---|---|---|---|---|
| R-0 | 1960 live | still `none` | Each open quarantine released, ledger open count 0 | `refused` or `removal_failed`: stop, leave that entry |
| R-1 | 1960 live; 1961 optional | `Plan,Docs,Investigate` | 4 hours, or at least 5 slot releases, and no new open quarantine | Any `filesystem/reset` quarantine |
| R-2 | 1961 live on server2 | `Plan,Docs,Investigate,TestDesign,Review,Check,Debug` | 8 hours, or at least 10 releases including at least 3 checkpoint-building tasks, and zero `filesystem/reset` | Any `residue:` quarantine, or 3 or more open entries of any class |
| R-3 | R-2 clean | the R-2 list plus `Code` | 24 hours at the 7-task server2 load, open quarantines never above 2 | Same as R-2 |
| R-4 | R-3 clean | `*` | Steady state | Same as R-2 |

A `custody/lingering` quarantine does not by itself roll back. Confirm the holder pid is gone, then release it as `Pushed`.

`*` is every role except Mutation (`WorkspaceSlotRoles.cs:6-8`). SourceLanding never leases (`WorkspaceSlotRoles.cs:96-102`). The plan says to check `rehomes` for ws-01 before R-1 or R-2. At plan time, one more ws-01 quarantine before about 16:00Z on 2026-11-17 would retire that slot (`MaxRehomesPerSlotPerDay` 3, `RunnerWorkspaceSlotPool.cs:826` in the plan's citation). Whether that window has passed is a clock fact; re-read `rehomes` rather than trust the date in the plan.

## 3. Triage

| What you see | Incident | First command |
|---|---|---|
| Hold `Held: RunnerUnavailable: runner 'server2' is not dispatch-eligible (Phone-home runner has not completed recovery.)` | A | `GET /api/session-runners` and `GET /api/session-runners/server2/status` |
| Catalogue `unavailableReason: stale`, `capacity: null`, and status `dispatchEligible: false` with `lastCatchUpMs` not moving | A | Grep `server/logs/antiphon-<date>.log` for `Phone-home inventory refresh List for runner server2` and `phone_home_request_timeout` |
| Status `available: true`, heartbeat fresh, `dispatchEligible: false` | A, stuck recovery, not a dead container | AppHost restart in section 1. Do not restart the server2 container |
| Hold or log contains `workspace_slot_pool_degraded`, task stays Queued, backoff up to 15 minutes | B | The ledger count command in section 2. `open` greater than 4 confirms it |
| Open quarantines are 4 or fewer, and the hold is still `workspace_slot_pool_degraded` | Not the 2026-11-17 pool incident | Re-read the count. Do not delete directories |
| Both holds at once | A blocks dispatch; B blocks slot prep | Confirm A first. A restart does not close quarantines. Roles `none` does not repair a stuck phone-home pump |

## 4. What we measured

Times are UTC on 2026-11-17. Counts are from the two investigations. This runbook did not re-read the live server, the ledger, or the desktop log.

| Fact | Value | Where |
|---|---|---|
| Desktop server during the phone-home stall | `870a93d21f2b18c85a8b0905ab689b28e3099813` | phone-home investigation, header |
| Lease | 90 seconds, heartbeat frames only | `PhoneHomeContracts.cs:21`, investigation verdict 1 |
| Epoch 1 accepted after the earlier restart | 00:29:13Z, capacity 10. Previous epoch ended 00:25Z `request_aborted` | investigation timeline |
| Inventory refresh List timeouts | 3, at 02:49:03, 02:50:04, 02:51:05Z, code `phone_home_request_timeout` | investigation timeline |
| Lease expiry | about 02:50:47Z. Alarm 02:53:47Z, "unavailable for 3.0 min" | investigation timeline. The 02:50:47 instant is inferred from the 90-second lease |
| Last pump List-related line | 02:51:15Z. No catch-up failure and no pump-cycle failure after that, through the end of the note (03:28Z) | investigation timeline |
| Status samples while stuck | 15 samples, 03:25-03:28Z: available true, eligible false, `lastCatchUpMs` 30 | investigation verdict 2 |
| SessionOutput on epoch 1 | 928,997 events, 260.6 MB, about 98/s average. Not the cause | investigation verdict 3 |
| Release kills that failed | 2, at 03:04:14Z (task d7c12587) and 03:07:03Z (task cad48ede). Both kept Idle | investigation verdict 4 |
| New-task holds | 03afb6f5 at 03:05:30Z, 3871f961 at 03:09:01Z. Aged to ERR at 03:20:32Z and 03:23:59Z | investigation timeline |
| Running tasks named at the time | 77238f98, 13eaf3b2, 32f24400, e96c2ae3, plus the 2 Idle seats | investigation, recovery option 3 |
| Stuck-recovery await | UNOBSERVED. No dump | investigation, UNOBSERVED |
| Stuck-lease restart outcome | UNOBSERVED in the investigation. The caller of this runbook: AppHost restart, new epoch within about two minutes, `/api/version` unchanged | this runbook's brief; investigation lines 3 and 142 |
| Fifth quarantine | 01:05:51Z, dirId 7b3f3b265073. Open count 5, limit 4 | slot investigation, Timings and Outcome |
| Ledger read | 01:13Z, five open. Pushed checks 01:15-01:18Z, all five clean, no writes | slot investigation |
| `workspace_slot_pool_degraded` lines | 19 by 01:21Z. Last line 01:24:14Z. 22 in total. None after the setting change | slot investigation, Outcome and Decision |
| Roles set to `none` | about 01:26Z, hot reload, no restart | slot investigation, Decision |
| Four queued tasks started | 83d30eec 01:27:42Z, e7394f29 01:31:18Z, e96c2ae3 01:39:14Z, 2e74cf4e 01:39:26Z | slot investigation, Decision |
| Ledger at 01:40Z | same five open. ws-02 still leased. No new quarantine | slot investigation, Decision |
| Ledger and roles after 01:40Z | UNOBSERVED | CARD-1960 plan, G-11 |
| Filesystem quarantines | 3 in about 9 hours (16:00 and 21:49 on 2026-11-16, 01:05 on 2026-11-17), plus 2 custody lingering | slot investigation, observed rate. 16:00Z on 2026-11-16 is 24 h before the plan's age-out at 16:00Z on 2026-11-17 |
| Hang site, runner CPU, and whether a container restart keeps pty hosts | UNOBSERVED | phone-home investigation, UNOBSERVED |

Notes:

- [2026-11-17 server2 phone-home runner stuck](investigations/2026-11-17-server2-phone-home-stale.md) is the Debug note. It is on `origin/feat/card-task-e5d7e9da` at `7e105faca3a5b938b89919f368489d4e7226cf9d`, not on master `300c8a99d`.
- [2026-11-17 workspace-slot pool degraded](investigations/2026-11-17-slot-pool-degraded-incident.md) is on master.
- CARD-1964 plan, round-2 review pending: `origin/feat/card-task-2bef90c6` at `125222cb4072b9e19ce3e68711ea0b341f8d5ac9`.
- CARD-1960 / CARD-1961 plan: `origin/feat/card-task-87ebe4f0` at `1cb47ef5300ab1b7f053148b824d4d406d8a705b`.
