# CARD-1105 ResumeRecycle stopped at census Unstable

Diagnosis only. The resume was not run again. Read-only SSH inspected the host journal, container and volume identity, and temp's Grok process. No `docker` stop, rm, start, compose, volume change, or deploy phase was run for this read.

Evidence for this attempt: `C:\src\Antiphon\.antiphon\rolling-server2\c727bc742a3246d7`. `deploy-parent.manifest.json` has `sourceSha=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `operationId=c10084fb9431d31da48edb781dd54e5ebebf7`, `resume=true`. Both `c590-result.json` copies are `{"accepted":false,"diagnosis":"RecycleTaskCensusUnknown cause=Unstable","exit":2}`. Orchestrator log `rollout7.log` records the same diagnosis and `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`. Wrapper `host-jq-redeploy-old.json` is `mode=check`, `outcome=existing`, `installed=false`, `observedAtUtc=2026-10-09T15:49:34.3896812Z`. `deploy-parent/command.log` is only the `GithubTokenAbsent` warning. There is no `recycle.json`, `generation.json`, or `build.log`.

The pin fix is what this checkout runs: `feat/card-task-ba8f35bd` is `89e4f769c0a15bb417d015dc0efc843e1c1eee34`. The digest compare passed. The new refusal is later, inside the host census.

## What Unstable compares

The census walks two server-filtered closures, twice. Open work is `Queued,Dispatched,Working,Blocked`. Pending land is `landPending=true`. Each scope follows `excluded.byProject` with the same filter. A pass snapshot is the scope observations (sorted item ids plus excluded counts), the reduced task rows (`id`, `status`, `runnerId`, `projectId`, `scopeSource`, both land timestamps), and, for the open closure, each row's land proof.

Two different compares emit the same token.

| Compare | Where | What differs |
|---|---|---|
| Second pass against the first pass | `scripts/deploy-server2.ps1` 562-684 (`$pass -eq 1` and the compressed snapshot `-cne` the previous one). Host twin: `scripts/c590-remote.sh` 4218-4339 (`pass` 2, `snapshot != previous`) | A change between the two reads of this run |
| Fresh snapshot against the journal | `scripts/c590-remote.sh` 4414-4415, when `C1008_RESUME=1` or `C1008_ACTIVE=1`. `jq -Sc` of the fresh closures must equal `jq -Sc` of journal `.tasks` | Any difference from the snapshot stored before removal |

The journal copy is written once, on the non-resume path, at `c590-remote.sh` 5924-5926 (`.tasks=$tasks`). `c1008_refuse` rewrites the journal only when `C1008_ACTIVE=1` (3834-3838). This resume calls `c1008_status_proof` at 5852, before `C1008_ACTIVE` is set at 5886, so an Unstable there does not save.

`cause=Unstable` carries no task id. The receipt's `reads` and `snapshot` are the way to see which side moved.

## This run's two passes agree

`deploy-parent/census-deploy-parent-server2.json` has `"refusal": ""`, so `c1008_tasks_collect` returned success. Both passes, and the wrapper receipt `census-redeploy-old-server2.json`, record the same counts:

| Read | Pass 1 | Pass 2 |
|---|---|---|
| project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` open | items=3, excludedTotal=2 | items=3, excludedTotal=2 |
| project `160cda04-2245-45c6-8570-ff5e5a28e8b6` open | items=2, excludedTotal=3 | items=2, excludedTotal=3 |
| project `d4ea7ae9-…` land | items=0, excludedTotal=0 | items=0, excludedTotal=0 |

Wrapper reads are lines 19-128 of `census-redeploy-old-server2.json`. Host reads are lines 6-132 of `census-deploy-parent-server2.json`. The 3/2 pair and the 2/3 pair are the two projects in one closure: Antiphon lists 3 open rows and withholds the other project's 2; the other project lists those 2 and withholds Antiphon's 3. Those numbers do not move between the two samples, and they do not move between the wrapper census and the host census of this run.

The five open rows are the same in both receipts, all `Blocked`, land proof null:

| Task | Runner | Project | Card |
|---|---|---|---|
| `05dae6a9-5d35-4134-9b53-53a840719dc7` | server2-temp | `d4ea7ae9-…` | CARD-1136 Code |
| `c75cd4bc-0730-4d27-a6c3-b63817d50db5` | server2-temp | `d4ea7ae9-…` | CARD-1105 Review |
| `962f430d-4dc4-4f32-8513-904cb3cdd855` | server2-temp | `d4ea7ae9-…` | CARD-1105 Debug |
| `a01b0301-62e9-4866-a0b5-b7153a998af0` | desktop | `160cda04-…` | CARD-0072 Investigate |
| `0726e42d-b3a0-4aa6-a962-8043229a6b60` | desktop | `160cda04-…` | none, Custom |

Detail reads in the receipt are those five ids on each pass (`05dae6a9`, `c75cd4bc`, `962f430d`, then `a01b0301`, `0726e42d`). No status or `runnerId` differs between the passes.

## The journal still has a sixth task

Host journal `/home/mc/antiphon-server2/recycle/c10084fb9431d31da48edb781dd54e5ebebf7.json` at this read: 198185 bytes, mtime `2026-10-09T11:10:42.843207Z`, not a symlink, `phase=recreating`, `outcome=verificationPending`, `ownedRemoved=true`, `sourceSha=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `previousSha=4358939ecd85d6e7ff0941f970879499cb930e3d`. Same size and mtime as the resume-mismatch read. This attempt did not rewrite it.

Journal `.tasks.open` has 6 rows. Five match the table above. The sixth is:

`d600e398-332f-401b-8315-c3d8217c4b98`, status `Dispatched`, runner `server2-temp`, project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, both land timestamps null.

Journal scope counts match that set: Antiphon ids=4, excludedTotal=2; project `160cda04-…` ids=2, excludedTotal=4. Land tasks are empty. That is the snapshot `c1008_status_proof` compared at 4415. The fresh host snapshot has 5 open rows and excluded totals 2 and 3, so `jq -Sc` differs. That is the refusal.

The same six rows, with `d600e398` still `Dispatched`, are in:

- check-census `C:\src\Antiphon\.antiphon\rolling-server2\c727210cce35cb9c\census-check-census-server2.json` (`rollout5.log` lines 3-4: `open=6 boundOpen=0 landPending=0` for both runners; server2-temp `boundOpen=4`)
- the `HostComposeFailed` run `c727676499a853c6`, wrapper and `deploy-parent/census-deploy-parent-server2.json`
- the digest-mismatch resume `c727d186a0fda983\census-redeploy-old-server2.json` (that run never reached a host census)

`GET /api/agent-tasks/d600e398-332f-401b-8315-c3d8217c4b98` now: `status=Succeeded`, `role=Code`, `runnerId=server2-temp`, `cardIdentifier=CARD-1150`, title `CARD-1149/1150 S3+S4 running-session recovery`, `completedAt=2026-10-09T12:37:47.275104Z`, `landRequestedAt=null`, `landStartedAt=null`, `landRequest.state=Completed`, `landRequest.terminalEventId=699cfbe5-4aa2-405a-901f-0e85617345a4`. Completion is after the 12:32:45Z census that still listed it as `Dispatched`, and after the journal mtime. Succeeded is outside the open filter. A completed land is outside `landPending=true`. The row is in neither closure.

The other five are still `Blocked` on the same runners. `GET /api/session-runners/server2/status` at this read: `buildVersion=4358939ecd85d6e7ff0941f970879499cb930e3d`, `available=false`, `dispatchEligible=false`, `draining=true`, `redirectTo=server2-temp`, `acceptingNewWork=false`, `retiredAt=null`, `sessions=0`, `runnerSessions=null`, `queuedTasks=0`. `server2-temp`: `buildVersion=d985af05b5ace2f13ec3f0d886c15992e9c13c3f`, available and accepting, `sessions=3`, `runnerSessions=3`, `queuedTasks=0`. The earlier 12:32Z read had temp `sessions=4`, which included the `d600e398` seat.

`GET /api/agent-tasks/pipeline` at `2026-10-09T15:57:13Z` has `inFlightAgainstCap=1`. That one row is this Debug task `ba8f35bd-bf08-43a7-ae38-7262f833110f`, `runnerId=desktop`, project `d4ea7ae9-…`, `status=Dispatched`, `createdAt=2026-10-09T15:50:40.030838Z`. Host-jq for the failed resume is `15:49:34Z`, and neither census receipt contains `ba8f35bd`, so the open set at the refusal was the five Blocked rows. A census taken while this task stays `Dispatched` or `Working` adds a desktop Antiphon row the journal does not have.

## Retry would fail again

The two passes of this run already agreed. The mismatch is the saved sixth row. `d600e398` is `Succeeded` with a `Completed` land, so the open closure will not grow that row back. A plain retry of the same `-ResumeRecycle` compares the live closure to the same journal `.tasks` and returns `RecycleTaskCensusUnknown cause=Unstable` again. While `ba8f35bd` is still open it adds a second difference. After `ba8f35bd` leaves the open set, `d600e398` alone still fails the compare.

Canceling the five Blocked tasks widens the diff. They are the rows that still match the journal. `d600e398` is not Blocked. The earlier cancels of `55662594` and `bae50f99` are already outside this set. `docs/orchestration-loop.md` names cancel as recovery for a dead Blocked session; that is not this refusal, and an orchestrator cancel of these five does not make the snapshot match.

## Host state after this attempt

Unchanged where the recycle is concerned.

| Object | This read | Journal / earlier diagnosis |
|---|---|---|
| `392f011e1f2b` `antiphon-runner-session-runner-1` | `created`, not started, image `antiphon-server2/session-testing:8892b7b759d9` | `.recreated.owned` session-runner |
| `23c97aac84b7` `antiphon-runner-state-init-1` | `exited` 1, started `2026-10-09T11:10:35.819720237Z`, finished `2026-10-09T11:10:36.221454907Z`, image `antiphon-server2/server:8892b7b759d9` | same Exited container |
| `4cabb8afc676` `antiphon-runner-build-slots-1` | Up 9 days (healthy), `session-testing:a8b4e9e5a071` | not in the owned set |
| `927bb1ad3d60` `antiphon-runner-temp-session-runner-1` | Up 3 days (healthy), `session-testing:d985af05b5ac` | temp runner, untouched |
| `antiphon-runner_work`, `_runner-tmp`, `_dind-data` | `CreatedAt` `2026-10-09T11:09:57Z` | `.recreated.volumes` |
| `antiphon-runner_runner-state` | `CreatedAt` `2026-09-22T19:10:50Z` | preserved |

`c1008_refuse` did not reach `c1008_save`. Phase stays `recreating`, outcome stays `verificationPending`.

## What to do next

The pre-authorized `-ResumeRecycle` is spent. Another run of the same command is a check-with-me gate, and the command would refuse again on this script.

1. Leave the host as it is. Do not resume, do not start `392f011e1f2b`, do not edit the journal, do not compose by hand. Temp keeps its sessions. Main stays drained to temp.

2. Code fix in `scripts/c590-remote.sh` 4414-4415, then the same resume only after that script is what `c590-real.ps1` will scp. On resume, when journal `.phase` is `recreating` or `verified`, do not require the whole closure to byte-match `.tasks`. Keep the two-pass check at 4339, `RecycleBoundTasks`, and `RecycleLandInFlight`. Those three already passed here: the passes agree, nothing open is bound to `server2`, and land items are 0. Temp is the accepting runner, so its open set keeps changing; a narrower allowance that only drops `d600e398` still refuses the next temp or desktop dispatch. The pre-removal phases can keep the byte compare.

   Add the case next to `RemoteScriptContractTests.C1105_Resume_digest_pins_previous_sha12` (`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` 1658). Saved journal phase `recreating`, `.tasks` containing a `Dispatched` `server2-temp` row, live census omitting that row as `Succeeded` with `landRequest.state=Completed`: resume does not return `cause=Unstable` and removes nothing further. A live row bound to `server2` still returns `RecycleBoundTasks`. A `landPending` row still returns `RecycleLandInFlight`. A second pass that changes an open row still returns `cause=Unstable` from 4339. Leave the wrapper vector `unstable` in `RollingVolumeRecycleScriptTests.cs` 215-218 as the two-pass refusal.

3. After that fix, the chown race (CARD-1168) is still inside `compose up`. Temp container `927bb1ad3d60` still has `grok` PID 14237. `docker exec` `ls` of `/state/grok` at this read: `worktrees.db` is 40960 bytes, mtime `2026-10-09 12:52:16.218170117 +0000`, owner `app`. `worktrees.db-wal` and `worktrees.db-shm` are absent. The directory mtime is `2026-10-09 14:20:14.617700948 +0000`. The missing WAL is the same gap as the previous read. The process is up, so the WAL can reappear during `chown -R`. This is not a quiet moment. Quiescing that Grok is an operator action. `-KillSessions` is human-only.

Not a recovery: editing journal `.tasks` by hand, `docker start` of `392f011e1f2b`, or a hand `compose up`. A new `redeploy-old` without `-ResumeRecycle` still hits the existing journal.

### Recommendation

Do not run `-ResumeRecycle` again on this script. Dispatch a Code change for the recreating-phase census compare in (2), with the test above. After that script is what will be copied to the host, the operator quiesces temp Grok and then runs, from the canonical desktop checkout:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a -Phase redeploy-old -ResumeRecycle c10084fb9431d31da48edb781dd54e5ebebf7
```

Same operation id, same SHA, same phase. That command needs the operator's go. If `up` returns `HostComposeFailed` again, stop. Do not open a second journal and do not remove the Created or Exited containers.
