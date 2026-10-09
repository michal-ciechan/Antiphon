# CARD-1105 redeploy-old stopped at HostComposeFailed

Diagnosis only. Nothing on server2 was stopped, removed, started, or mounted for this read. The host journal `c10087844d55007a24e079503d713f1ff6940` was listed (phase and outcome only) and not modified. Read at 2026-10-09T11:21Z.

Evidence run: `C:\src\Antiphon\.antiphon\rolling-server2\c727676499a853c6` (manifest `deploy-parent.manifest.json`, 375 bytes, `sourceSha=8892b7b759d96b3b5d6fa20518caf2c79c6be81a`, `operationId=c10084fb9431d31da48edb781dd54e5ebebf7`, `resume=false`). Orchestrator log `rollout5.log` records `DIAGNOSIS=HostComposeFailed` and `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2` after `HEAD is now at 8892b7b759`. Both `c590-result.json` copies are 61 bytes: `{"accepted":false,"diagnosis":"HostComposeFailed","exit":2}`.

## Where it stopped

`redeploy-old` got through admission, the git audit, the controlled stop, volume removal, image build, volume recreate, and the checkout seed. It stopped on `docker compose up` of project `antiphon-runner`, because `state-init` exited 1.

Completed before the failure, from `deploy-parent/recycle.json` (198,185 bytes, schema 1) and `deploy-parent/command.log` (1,108 bytes, 20 lines):

| Step | Evidence |
|---|---|
| Git audit passed, then was stored | `deploy-parent/audit.txt` line 391: `repositories=390 partial=390`. File is 178,141 bytes, 1,420 lines, 390 `entry` lines, 1,029 `tip` lines. No `audit check=` line and no `RecycleUnpublishedWork`. The journal copy of `.audit` is the same 178,141 characters. `c590-remote.sh` stores that audit before `docker stop` (around 5929-5934) and requires the post-removal audit to match before it deletes volumes. Volumes were deleted, so the second audit matched. |
| Old containers stopped and removed | `ownedRemoved=true`. `stopReceipts=2`, `removeReceipts=2`. Owned ids `5d9e09695ab3` (session-runner, was running) and `574beddc5794` (state-init, was exited). `build-slots` was not in the owned set. |
| Three volumes removed | `antiphon-runner_work` outcome `removed`, original `CreatedAt` 2026-09-22T19:10:50Z. `antiphon-runner_runner-tmp` removed, original 2026-09-30T08:08:35Z. `antiphon-runner_dind-data` removed, original 2026-09-22T19:10:50Z. |
| Preserved | `antiphon-runner_runner-state` created 2026-09-22T19:10:50Z. Three cache volumes created 2026-10-02T08:38:26Z, 08:38:32Z, and 08:38:39Z. |
| Disk | `diskBefore.bytes=56598335488`, `diskAfter.bytes=191584874496` on `/dev/mapper/ubuntu--vg-ubuntu--lv` (about 52.7 GiB free to about 178.4 GiB free, delta +134,995,539,008 bytes). |
| Images built | `antiphon-server2/session-testing:8892b7b759d9` id `sha256:a935d19329ba5afb2353c95ae4c7aa9fc8e54e4fb02f1b44b7394664296f1dce` created 2026-10-09T11:07:19Z. `antiphon-server2/server:8892b7b759d9` id `sha256:e67dfda503d4134c0d6a9a8ac8b333bc9b5f3312c6a4bdd5f92605ab211f4059` created 2026-10-09T11:09:42Z. Previous tags `4358939ecd85` are still present (`sha256:743279186cff4b4ca4507c6a64d7a139caced22dd2f3c757ced00048c9eba620` and `sha256:d94f62fe7e2b2fd9622046c6046e8b9784b8f4ea9bf2d3ee9850bc041e451295`). |
| Replacement volumes created | `command.log` lines 2-7. Live `CreatedAt` 2026-10-09T11:09:57Z for `antiphon-runner_work`, `antiphon-runner_runner-tmp`, and `antiphon-runner_dind-data`. Journal `.recreated.volumes` records the same timestamps. |
| state-init seed succeeded | `command.log` line 9: `state-init owned uid=1654`. That line is printed only after `chown -R` returns (`docker/stack/init-state.sh` lines 72-73). |
| Checkout seeded | `runner-checkout-seed.txt` (33 bytes): `seeded path=/work/repos/antiphon`. `command.log` line 10: `Cloning into '/work/repos/antiphon'...`. |
| compose up of `antiphon-runner` failed | `command.log` lines 11-19 create `antiphon-runner-state-init-1` and `antiphon-runner-session-runner-1`, start state-init, then `service "state-init" didn't complete successfully: exit 1`. `c590-remote.sh` lines 6046-6050 append `compose logs` and `write_result false HostComposeFailed 2`. |

The compose error, from the host case log (`command.log` line 20):

```
antiphon-runner-state-init-1  | chown: changing ownership of '/runner-state/grok/worktrees.db-wal': No such file or directory
```

Live inspect of that container: id `23c97aac84b711d5ee35663054a25a73fbc94b80ca6f7ec72bed4fe267f89239`, image `antiphon-server2/server:8892b7b759d9`, status exited, exit code 1, `State.Error` empty, started 2026-10-09T11:10:35.819Z, finished 2026-10-09T11:10:36.221Z (about 400 ms). The session-runner container `392f011e1f2b77b07265c0edf53dc8953715cde1ad38a2ad0c0e662fc4c50de8` is `created` and was never started.

Journal after the failure: phase `recreating`, outcome `verificationPending`, `ownedRemoved=true`, `previousSha=4358939ecd85d6e7ff0941f970879499cb930e3d`. The same fields are on the host file `/home/mc/antiphon-server2/recycle/c10084fb9431d31da48edb781dd54e5ebebf7.json`. `c1008_record_recreated` (`c590-remote.sh` 5658-5675) writes that phase when the replacement volumes and containers exist, including after a failed `up` (6046-6047 runs it before the refusal).

`WARN GithubTokenAbsent` is `command.log` line 1, from `ensure_runner_github_token_dir`. It is a warning, not the refusal.

## Current state of the old runner

Read-only `docker ps -a`, `docker volume inspect` (name, driver, CreatedAt, compose/cache labels), and container State/Mounts. No volume was mounted for this diagnosis.

Main project `antiphon-runner`:

| Container | Status | Image |
|---|---|---|
| `392f011e1f2b` `antiphon-runner-session-runner-1` | Created, not started | `antiphon-server2/session-testing:8892b7b759d9` |
| `23c97aac84b7` `antiphon-runner-state-init-1` | Exited (1) at 11:10:36Z | `antiphon-server2/server:8892b7b759d9` |
| `4cabb8afc676` `antiphon-runner-build-slots-1` | Up 9 days (healthy) | `antiphon-server2/session-testing:a8b4e9e5a071` |

`build-slots` has no volume mounts. These two runner/state-init ids and images are the journal's `.recreated.owned` set, which a later `-ResumeRecycle` compares (`c590-remote.sh` 5858-5868).

Volumes that exist now:

| Volume | CreatedAt | Role |
|---|---|---|
| `antiphon-runner_work` | 2026-10-09T11:09:57Z | Replacement. The 2026-09-22 volume is gone. Not mounted for this read. Holds the seeded clone only. |
| `antiphon-runner_runner-tmp` | 2026-10-09T11:09:57Z | Replacement. The 2026-09-30 volume is gone. |
| `antiphon-runner_dind-data` | 2026-10-09T11:09:57Z | Replacement. The 2026-09-22 volume is gone. |
| `antiphon-runner_runner-state` | 2026-09-22T19:10:50Z | Preserved. Same identity as `.preserved`. |
| three `antiphon-runner-cache-*` | 2026-10-02T08:38Z | Preserved. |
| four `antiphon-runner-temp_*` | 2026-10-05T22:46:32Z | Unchanged. |

`antiphon-runner_runner-state` `grok/worktrees.db` is a regular file, 40,960 bytes, mode 644, uid 1654, mtime 2026-10-09T03:30:05Z. `worktrees.db-wal` and `worktrees.db-shm` were absent at 11:21Z. `fuser`/`lsof` had no holder of the db file itself at that instant.

Host `df` on the Docker root: `avail_kb=180533752` (about 172.2 GiB) on `/`.

Phone-home at 11:21Z (`GET /api/session-runners/{id}/status`):

- `server2`: `buildVersion=4358939ecd85d6e7ff0941f970879499cb930e3d`, `available=false`, `dispatchEligible=false`, `unavailableReason=stale` on the list route, `draining=true`, `redirectTo=server2-temp`, `retireWhenIdle=false`, `retiredAt=null`, `acceptingNewWork=false`, `sessions=0`, `queuedTasks=0`, `runnerSessions=null`. Slots route returns 503 `phone_home_unavailable`.
- `server2-temp`: `buildVersion=d985af05b5ace2f13ec3f0d886c15992e9c13c3f`, available and dispatch-eligible, not draining, `retiredAt=null`, `sessions=4`, `runnerSessions=4`, `queuedTasks=0`, capacity 10, occupied 4. Slots: 4 Running, 1 Exited. Container `927bb1ad3d60` Up 3 days (healthy); its state-init `19026e1037d9` Exited (0) 3 days ago.

Temp's other three private volumes and its container were not recreated. The rollout did not touch them.

Journal index (phase and outcome only): `c10084fb9431d31da48edb781dd54e5ebebf7` is the only `antiphon-runner` journal for SHA `8892b7b759d9`, and it is `recreating` / `verificationPending`. `c10087844d55007a24e079503d713f1ff6940` is still `preflight` / `refused` for SHA `51f175dbf738`. Three other `preflight` / `refused` journals are for other SHAs.

## Root cause

Not a missing image, port, network, disk, or compose render. Both new images exist and are the images the failed containers were created from. The runner publishes no host port. Free space rose by about 126 GiB before `up`. `RecycleComposeMismatch` was not returned; `up` ran.

`state-init` runs `docker/stack/init-state.sh` under `set -eu`. Line 72 is:

```
chown -R "$uid:$gid" /state /work /runner-state
```

`/runner-state` is the preserved `antiphon-runner_runner-state` volume. That volume's `grok` directory is the live Grok home of server2-temp:

- `docker-compose.server2-runner.temp.yml` line 14 bind-mounts `${RUNNER_GROK_STORE_DIR}` at `/state/grok`.
- `case_deploy_temp_runner` sets that variable to the mountpoint of `antiphon-runner_runner-state` plus `/grok` (`c590-remote.sh` 6198-6207).
- Temp container `927bb1ad3d60` mountinfo for a Grok child: `/var/lib/docker/volumes/antiphon-runner_runner-state/_data/grok` is mounted on `/state/grok`, on top of temp's own `antiphon-runner-temp_runner-state` mount at `/state`.
- At 11:22Z, `lsof` showed two `grok` processes in that temp container (cgroup `927bb1ad3d60`, PIDs 14237 and 18732) with write FDs on `logs/unified.jsonl`, a `memtrace` file, and session `events.jsonl` under that same host path. `models_cache.json` in that directory was rewritten between 11:20:29Z and 11:22:17Z.

SQLite deletes `worktrees.db-wal` when a connection checkpoints. `chown -R` had already listed that name and then got `ENOENT`. `set -eu` turned that into exit 1. Compose reported the service failure. The seed `compose run state-init` (`c590-remote.sh` 1226) won the same race a few seconds earlier and printed `state-init owned uid=1654`. The `compose up` dependency lost it.

The first chown and the clone do not start temp's Grok processes. Those processes were already running (temp has been up for 3 days with 4 sessions). They stay running through this failure because temp was not part of the recycle.

## Recovery

`docs/apphost-runbook.md` has no redeploy-old recovery. The owner is `docs/docker-stack.md` (volume recycling, lines 237-243 and 314-322; rollback, lines 457-459) and `scripts/deploy-server2.ps1`.

Do this, and nothing else, until the human step finishes:

1. Leave `server2` drained to `server2-temp`. Do not clear either drain, do not run `drain-temp` or `retire-temp`, and do not `docker stop` / `rm` / `up` / volume-remove the Created or Exited main containers. Those ids are what resume compares. Removing them makes phase `recreating` return `RecycleResumeMismatch`.
2. Do not start a new `redeploy-old` without `-ResumeRecycle`. `runnerSessions` is null while `buildVersion` is still `4358939ecd85d6e7ff0941f970879499cb930e3d`. `deploy-server2.ps1` lines 1026-1035 call `Assert-ZeroCounters` (lines 335-347), which throws `RunnerCounterUnknown server2 runnerSessions`. That is the refusal `docs/docker-stack.md` lines 239-243 describe. `Assert-NoIncompleteRecycle` (lines 305-327, `RecycleResumeRequired`) runs only when `buildVersion` already equals the target SHA, so it is not the refusal today, but this journal is the unfinished one it would find later.

The documented next command is human-only `-ResumeRecycle`. An orchestrator does not run it. From the canonical desktop checkout (deploy scripts refuse a linked worktree), not from this worktree:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8892b7b759d96b3b5d6fa20518caf2c79c6be81a -Phase redeploy-old -ResumeRecycle c10084fb9431d31da48edb781dd54e5ebebf7
```

Why this command, and why it is not a volume replay: phase is `recreating` and outcome is `verificationPending`, with `ownedRemoved=true` and every target volume `removed`. `c590-remote.sh` lines 5858-5871 check that the live volumes and the non-broker containers still equal `.recreated`, then return before any removal. They match as of 11:21Z (volume CreatedAt 2026-10-09T11:09:57Z, runner-state 2026-09-22T19:10:50Z, container ids above). Resume then continues `deploy-parent`: rebuild the same SHA, seed, and `compose up`. It does not delete the replacement generation (`c590-remote.sh` 5656-5657).

That `up` runs `init-state.sh` line 72 again while temp's Grok processes still have the shared directory open. The same `ENOENT` can recur. The seed chown already succeeded once under that load, so a retry can also succeed. It is not deterministic.

A human who wants the chown not to race stops the temp Grok writers first, then runs the command above. That interrupts in-flight temp sessions. It is outside orchestrator autonomy (`docs/orchestration-loop.md` lines 26 and 52-59: `-KillSessions` is human-only; do not touch the standing runner outside the rolling phases). Autonomy does allow a plain `redeploy-old` (lines 21-22). This failure is not that shape: the no-resume retry refuses, and `-ResumeRecycle` is the human step.

If resume returns `HostComposeFailed` again, stop. Keep this operation id. Do not open a second journal. A code change that makes the recursive chown tolerate a missing path under `/runner-state/grok` cannot be applied by this resume: resume requires `sourceSha` `8892b7b759d96b3b5d6fa20518caf2c79c6be81a` (`c590-remote.sh` 5831-5833), and that SHA contains the current `chown -R`.

Not the next step:

- Rollback by recreating old at `4358939ecd85`. The previous images are still on the host, and accepting temp is the live rollback (`docs/docker-stack.md` lines 457-459). A redeploy of the old SHA is another `redeploy-old`, which refuses while `runnerSessions` is null, and it cannot restore the removed work volume.
- Hand `docker compose up`, hand `chown`, or hand volume restore. Not documented. Main volume replacement stays inside the script.

After a successful `redeploy-old`, that phase clears old's drain itself (`deploy-server2.ps1` 1054-1060) only once `buildVersion` is the new SHA and dispatch is eligible. Gate 7 (smoke, canary pinned to `server2`, cache verify) is still required before `drain-temp`. Do not start gate 8 from this failure.

### What was removed

The old work volume's git content was removed on purpose after a clean audit: 390 repositories, all partial, 1,029 tips, no unpublished-work refusal, no dirty-worktree refusal. `runner-tmp` and `dind-data` were removed and recreated empty. `runner-state`, including the Grok store temp is using, was not removed. The three cache volumes were not removed. Temp's four volumes were not removed. The new work volume contains the seeded `--filter=blob:none --no-checkout` clone and nothing recovered from the old volume. No unpublished git commit was reported lost. Grok login state was not deleted.

## Temp meanwhile

Safe to keep scheduling on server2-temp. It is available, accepting, not draining, 4 of 10 seats occupied, 4 running sessions, queued tasks 0. Old is already redirected there. There is no outage and no disk clock (about 172 GiB free). Do not delete or chown `antiphon-runner_runner-state` while temp is up: temp's `/state/grok` is that directory. The Created main session-runner is not running, so it is not a second writer. New work that must run on server2 waits until old accepts again. Temp has six free seats, so this is not full. Nothing in this failure requires stopping those four sessions in order to keep the service up.
