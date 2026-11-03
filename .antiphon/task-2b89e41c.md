# Debug 2b89e41c: TempVolumesRetained stop at deploy-temp (read-only diagnosis)

Base master a2584ef6e30029cae4332aec469ada1e9e95c729. No code changes, nothing deleted or posted.

## Outcome

The deploy-temp run **did not stop at gate-1 admission. It deployed the temp runner** and then
refused at the last step, the hold-lift `/drain/clear` POST. That refusal is a script bug, not
leftover state. `Invoke-RunnerRequest` (`scripts/deploy-server2.ps1:279-283`, added in 85776c1c8e
"recheck retirement under the clear lock") runs `Assert-TempRetirementClearable` under FD8 on
**every** `POST server2-temp /drain/clear`. That includes the post-verification clear at `:1331`
(`reason = 'CARD-0849 cache verification passed'`). By then `deploy-temp-runner` has just created
the four temp-private volumes for the new container, so the census sees them and throws bare
`TempVolumesRetained` from `:1207`. That throw is the packet's `{error:"TempVolumesRetained"}` in
clear mode, and it carries no volume name, which matches the log.

This is not a one-token fix: the guard has to be scoped to the retirement-epoch clear. So I made no
edit.

## Evidence

### Server, read live 2026-11-03 ~09:55Z (`GET $ANTIPHON_API/api/session-runners/server2-temp/status`, task token)

```
"runnerId":"server2-temp","runnerStoreId":"431d93dc-c6a1-4946-874a-db30be3f80d6","epoch":1,
"available":true,"dispatchEligible":true,"platform":"linux",
"buildVersion":"919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd","reconnects":1,
"acceptingNewWork":false,"draining":true,"drainedAt":"2026-11-03T09:40:42.3565938+00:00",
"drainReason":"CARD-0948 temp deployment hold","redirectTo":"server2","retireWhenIdle":false,
"retiredAt":null,"retireReason":null,"sessions":0,"queuedTasks":0,"runnerSessions":0
```

The catalogue row shows `platformObservedAt 2026-11-03T09:50:06Z`, `unavailableReason "draining"`,
`occupied 0`. server2 (main) is `buildVersion 8edda9996…`, available, accepting, not draining, with
5 sessions.

So `retiredAt` is **null**. The 2026-10-30T00:45:02Z retirement was cleared by this run, as
`temp-retirement-clear.json` records. The temp runner is up on the target SHA and held. The
orchestrator's "dispatch-eligible no, platform unknown" reading is stale or wrong: it now reads
eligible, linux.

The field that carries `retiredAt` is the `retiredAt` property of
`GET /api/session-runners/{id}/status` (docs/ops-http.md:143). The catalogue `GET /api/session-runners`
does not carry it.

### Reconstructed run sequence (`deploy-temp` branch, `:1286-1335`)

1. The status showed a retired offline placeholder. The run checked seed counters, the old-runner
   redirect and `Assert-TempProjectAbsent` (`temp-project-containers.txt` was empty).
2. `Assert-TempRetirementClearable` passed, so at that moment none of the four volumes existed and
   no journal was unresolved.
3. The reactivation `/drain/clear` ran under the lock, and its census passed again. The run wrote
   `temp-retirement-clear.json`.
4. The `/drain` hold POST matches `drainedAt 09:40:42` and the "temp deployment hold" reason.
5. Three host cases each printed `DIAGNOSIS=` with an empty value, which is
   `write_result true '' 0` (`c590-remote.sh:113`): `runner-cache-seed`, then `deploy-temp-runner`
   (whose own host recheck at `:9800` passed before compose created the volumes), then
   `verify-runner-caches`. These are the "3 DIAGNOSIS= lines".
6. `Wait-RunnerStatus` saw the runner eligible on 919d8d42. The status had draining set, so the run
   POSTed `/drain/clear` with 'CARD-0849 cache verification passed'. `Invoke-RunnerRequest` took the
   lock, ran the census, found the new runner's volumes and threw `TempVolumesRetained` (exit 2).

Why the tests missed it: the fake verifier `scripts/fixtures/c727-fake-verify.ps1:73-86` returns a
static `{"volumes":[],"journals":[]}` for the whole run. Its "creating" deploy never makes volumes
appear. The reuse witness in `VolumeRetirementWrapperTests.cs:84-100` sets `draining=false`, so the
post-verify clear POST is never made.

## Answers

### (1) Volumes and journals on the host

**Not directly observed.** `ssh mc@server2` from this mirror fails `Host key verification failed`,
because the container has no `~/.ssh`. The local `docker` CLI talks to this runner's nested dind
(`docker info` Name=4551c5a7852f), not the host: its `c1609r…temp_*` volumes are test-fixture
leftovers. So I have no `docker volume ls`, `docker system df -v` or journal listing to quote.

Inferred with high confidence:
- All four of `antiphon-runner-temp_work`, `_runner-tmp`, `_dind-data` and `_runner-state` exist.
  They were created today between 09:40 and 09:50Z by `deploy-temp-runner`.
- They are mounted by the running temp container: the server sees it live, with heartbeats and
  `processBootId 440fe75d…`.
- Journals: every journal under `/home/mc/antiphon-server2/retirement/` was Completed, closed,
  refused-without-effects or preview-only with no live owner. Otherwise steps 2-3 would have refused
  `TempRetirementUnresolved` or `TempRetirementOwnerActive`.

To confirm, a human or desktop shell can run these read-only commands:

```
ssh mc@server2 'docker volume ls --filter name=antiphon-runner-temp_ --format "{{.Name}} {{.CreatedAt}}"'
ssh mc@server2 'docker ps -a --filter label=com.docker.compose.project=antiphon-runner-temp --format "{{.Names}} {{.Status}} {{.Mounts}}"'
ssh mc@server2 'ls -la /home/mc/antiphon-server2/retirement/'
```

### (2) Server view of server2-temp

The server sees `retiredAt` null, draining, `acceptingNewWork` false, and available and
dispatch-eligible on 919d8d42 with zero work. See the Evidence section.

### (3) Why "retired start" and then the volumes exist; why retire-temp refuses

It was a genuine retired start, and the volumes did not exist then. This run created them by
deploying the new temp runner. The placeholder is not in a "cleared offline, volumes left behind"
state: it is a live, freshly deployed, held runner.

`retire-temp -DryRun` refuses `TempRunnerNotRetired` correctly.
`Invoke-TempContainerCleanup` → `Assert-TempCleanupStatus` (`:1005-1011`) requires
`retireWhenIdle=true` and `available`, `dispatchEligible` and `acceptingNewWork` all false. The temp
runner has `retireWhenIdle false` and `available true`.

**Do not try to make retire-temp pass.** That path exists to delete these volumes, and it would
destroy the runner the rollout just stood up.

### (4) Value in the volumes

Not inspected (no host access). The runner registered at about 09:50Z and has been held since
09:40Z with `acceptingNewWork false`. It shows sessions, runnerSessions and queuedTasks of 0, and
one reconnect. So it has never taken a task. `_work` should hold no task worktrees or unpushed
branches, and `_runner-state` holds only that runner's own fresh boot state plus whatever seed/Grok
material deploy copied in.

These are not disposable leftovers, though. They are the **live** state of the gate-1 runner that
the rollout needs for gates 2-8. Nothing should delete them.

## Safe path forward (deletes nothing)

The rollout is functionally at the end of gate 1. Only the hold-lift is missing. Re-running
`deploy-temp` as-is would hit the same refusal. With `buildVersion == Sha` and `available` true, the
recreate block is skipped, but `verify-runner-caches` reruns and the same `/drain/clear` census
fires.

Two choices, both the operator's:

- **A (scripted, recommended).** Commission a Code fix: run the under-lock census only for the
  retirement-epoch clear. Either pass an explicit switch from the `:1307` call only, or under the
  lock re-read the status and require `retiredAt` set before running the census. For the post-verify
  clear, instead re-assert `retiredAt` null under the lock.
  - Add a fake-verify witness: a census that returns the four volumes after `deploy-temp-runner`,
    with the temp runner still draining at the post-verify clear.
  - Land the fix, then rerun `deploy-server2.ps1 -Rolling -Sha 919d8d42… -Phase deploy-temp` from
    the canonical checkout. It takes the reuse branch: wait eligible → `verify-runner-caches` →
    `/drain/clear` → wait `acceptingNewWork`. No recreate, no deletion.
- **B (manual, faster; operator approval needed because it is not a named phase).**
  `pwsh -NoProfile -File scripts/runner-drain.ps1 clear -RunnerId server2-temp -Reason "CARD-0849 cache verification passed"`
  This is exactly the POST the script would have made, but without FD8. Then check
  `runner-drain.ps1 status -RunnerId server2-temp` shows `acceptingNewWork true` and
  `buildVersion 919d8d42…`, and continue with `-Phase drain-old`. drain-old checks only that temp
  is accepting on the SHA.

In either case, do not run `retire-temp`, `retire-runner-volumes.ps1` or `-Close`: there is no
unresolved journal, and the volumes are live.

On "what the code does for each start state": gate 1 never accepts present temp volumes for a
retired or cleared-offline start. Both go through `Assert-TempRetirementClearable` before the
first mutation. A current start (same SHA and available) skips the census at admission but, before
the fix, still hits it at the hold-lift whenever the runner is draining.
