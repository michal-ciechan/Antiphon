# Why the server2 state-init chown is slow

Confirmed. Standing `state-init` container `8720a67f4158` (image `antiphon-server2/server:09e48c5d6f1d`, started `2026-10-17T21:06:54.035594118Z`) is still inside `own_tree /work`. At `2026-10-17T21:54:32Z` it was running and `antiphon-runner-session-runner-1` (`a6457bc89508`) was still `created`. The slowness is one `fork`/`exec` of GNU `chown` per path, on a preserved volume of 776 worktrees. Sampled paths are already uid 1654, and the script does not skip those. Waiting for exit 0 is the safe recovery. A kill plus a native `chown -R` does not make the compose re-run fast. There is no skip flag.

## What the loop does

`docker-compose.server2-runner.yml` runs `state-init` as root with entrypoint `/bin/sh /stack/init-state.sh` (lines 18-20). The only environment entry is `CODEX_HOME_REQUIRED=1` (lines 21-25). Volumes are `work:/work`, `runner-state:/runner-state`, and the host Codex directory at `/codex-home` (lines 26-32). `session-runner` waits on `service_completed_successfully` (lines 158-160), which is exit 0, not the image healthcheck.

The script in the running container matches `docker/stack/init-state.sh` lines 72-105. `own_tree` is called on `/state`, then `/work`, then `/runner-state`. `/state` in this container is the image layer (the runner-state volume is mounted at `/runner-state`). The live process list was pid 1 `sh` and child pid 38 `find /work -ignore_readdir_race -exec sh -c '...' sh 1654 1654 /tmp/tmp.O6sdWK1rAL {} +`.

For each path the inner shell does this:

1. `chown -h 1654:1654 "$path"` with stderr discarded. There is no `stat` and no "already owned" test before that call.
2. If that returns success, the path is done.
3. If it fails and the path still exists or is a symlink, it runs `chown -h` again and, on a second failure, appends the word `fail` to `owned_status` and exits 1.
4. If it fails and the path is already gone, the path is ignored.

`{} +` batches arguments into one shell. One observed batch held 1483 paths under `/work/worktrees/task-25f93db0/`. The shell then runs `/usr/bin/chown` once per path. `chown` is GNU coreutils 9.1, not a shell builtin. `find` had been alive for the whole run (41 minutes at the first `docker top`), so the walk was already on `/work`.

`owned_status` is a gate, not a receipt. The live file `/tmp/tmp.O6sdWK1rAL` was created at 21:06, mode `0600`, size 0 at 21:54:32Z. A non-empty file or a non-zero `find` status makes `own_tree` return 1. The file is deleted either way (`init-state.sh` lines 96-100). Nothing in `scripts/c590-remote.sh` or the compose file reads that file or a path count. The only success signal is stdout `state-init owned uid=1654` and exit 0. Container logs were empty at 21:48Z because that line is printed after all three trees.

`unhealthy` is the server image `HEALTHCHECK` (`Dockerfile` line 72, `curl` of `:8080/health`). This entrypoint never serves that port. It does not block `depends_on`.

Readdir order of `/work` at 21:52Z was `archive`, `repos`, `evidence`, `worktrees`, `test-evidence`, `review-evidence`. `find` is depth-first, so the walk is inside `worktrees` and still has `test-evidence`, `review-evidence`, and then all of `/runner-state` ahead of it. `/codex-home` is chowned as one directory before `own_tree` (mode `0700`, uid 1654) and is not walked.

## Why this used to finish quickly

Two different things used to be fast.

Before CARD-1168 the script was one process: `chown -R "$uid:$gid" /state /work /runner-state` (`0819ebccdc5866f2648e51700e4c632cc03987e7`, 2026-10-10). `0aa2193e796f695a0cb243314a3029d944748737` added `-h` so a dangling symlink is owned instead of followed. The contract tests pin that shape: `CodexRunnerImageContractTests` rejects `chown -R` in `init-state.sh`, and `C1168_State_init_owns_a_dangling_symlink` requires exactly two `chown -h` calls inside `own_tree`. The per-path walk exists so a name that vanishes after `find` has listed it (the live Grok store on the preserved runner-state volume) is not a failure. `docs/docker-stack.md` lines 276-282 say the same thing.

Timed runs of the old one-process chown, and of this script on a fresh volume:

| Run | Image | Start | Finish | Duration |
|---|---|---|---|---|
| Standing compose-up after the WAL race, `chown -R` (`docs/investigations/2026-10-09-card-1105-tmp-assets-missing.md`) | `antiphon-server2/server:8892b7b759d9` | `2026-10-09T19:29:46.402Z` | `2026-10-09T19:29:46.790Z` | 388 ms |
| Same container, earlier exit 1 on `worktrees.db-wal` | same | `2026-10-09T11:10:35.819Z` | `2026-10-09T11:10:36.221Z` | 402 ms |
| Temp `state-init` `ac048a240b9f`, this same script and tag | `antiphon-server2/server:09e48c5d6f1d` | `2026-10-16T17:57:23.838Z` | `2026-10-16T17:57:24.018Z` | 180 ms |

The 180 ms temp run was at temp deploy, on `antiphon-runner-temp_work`. Temp now has 68 worktree directories; those were created later by the running temp runner. That timing is a freshly seeded volume.

This recovery did not recycle `antiphon-runner_work`. `e7a6d639` `docs/investigations/2026-10-17-server2-recycle-recovery.md` line 66 recorded 776 directories under `worktrees/` and one under `repos/`. `15982940` records the pre-removal audit `repositories=800 partial=800`. The same count was still there at 21:52Z: 776 worktree directories, all uid 1654. The preserved tree, including node_modules, is what changed. An already-correct owner does not shorten the loop.

## Ownership sample (read-only)

Measured inside `8720a67f4158`. No `chown`, no kill, no second compose. `app` is uid 1654 (`getent passwd 1654`).

| Path | Owner | When |
|---|---|---|
| `/work`, `/work/repos`, `/work/repos/antiphon`, `/work/worktrees`, `/state`, `/runner-state`, `/runner-state/grok` | `1654:1654` | 21:52:03Z |
| `/codex-home` | `1654:1654` mode `0700` | 21:52:03Z |
| 776/776 direct children of `/work/worktrees` | uid 1654; `! -user 1654` count 0 | 21:52:03Z |
| Unvisited worktree `task-d05f6940` and eight files under its `.antiphon/` | `1654` / `app` | 21:53:41Z |
| Current batch file `.../task-47c95281/scripts/fixtures/c1192-workspace-contract.sh` | `1654:1654` | 21:53:41Z |
| `task-005ea326/client/node_modules` and `.../node_modules/@adobe` | `1654:1654` | 21:54:32Z |

`task-d05f6940` has neither `node_modules` nor `client/node_modules`. Of the 131 worktrees already entered, 16 have one of those directories. Of the 645 not yet entered, 61 do. The mix ahead is the same kind of mix already walked.

Progress from directory ctime `>= 1792271214` (the 21:06:54Z start), snapshot `2026-10-17T21:52:03Z` (elapsed 2709 s):

- 195 s on `/work` before the first worktree directory (`task-9133fca0`).
- Then 2512 s to enter 131 worktree directories. Mean 19.2 s. Largest gap 100.5 s (`task-dc2740ed` to `task-eb42ac54`).
- Newest entered name at that snapshot: `task-47c95281`, which was the live batch.

The dispatch note of about 59/776 at 40 minutes is a lower figure than this ctime count. ETA below uses the ctime series. The same count at `2026-10-17T21:57:56Z` was 150 (`find -newerct '2026-10-17 21:06:54 UTC'`), 19 more directories in 353 s, 18.6 s each.

Counts still ahead, taken at 21:54:32Z: `test-evidence` 128 paths, `review-evidence` 86770 paths, `archive` 3 paths, `/runner-state` 41848 paths. Top-level directory counts: `archive` 1, `repos` 1, `evidence` 42, `test-evidence` 1, `review-evidence` 46. Temp's runner bind-mounts `/var/lib/docker/volumes/antiphon-runner_runner-state/_data/grok` at `/state/grok`. That tree is live. `/work` is not mounted on temp (`antiphon-runner-temp_work` is a different volume). Disk: 491G used, 186G free on the host LV.

## Options

**(a) Wait. Recommended.** From the 21:52:03Z snapshot, 645 worktrees at 19.2 s is 3 h 26 min. The 21:57:56Z count (150 entered, 626 left, 18.6 s each) puts the end of the worktree walk near `2026-10-18T01:12Z`. The tail (86770 + 41848 + 128 paths) is a few extra minutes if the walk stays near the observed batch (1483 paths in a shell that was 3 seconds old), and under about 15 minutes if batches are slower. Planning figure: around `01:25Z`. No data change: sampled inodes are already 1654, and `chown -h` to the same owner does not rewrite file bytes. `owned_status` stays empty unless a still-present path cannot be owned. The in-flight `compose up` continues on exit 0 and starts `a6457bc89508`. No operator approval beyond the recovery that is already running.

Watch from pwsh, read-only. Single quotes keep the pipe on server2:

```
ssh mc@server2 'docker inspect -f "status={{.State.Status}} exit={{.State.ExitCode}} finished={{.State.FinishedAt}}" 8720a67f4158'
ssh mc@server2 'docker logs 8720a67f4158'
ssh mc@server2 'docker exec 8720a67f4158 find /work/worktrees -mindepth 1 -maxdepth 1 -newerct "2026-10-17 21:06:54 UTC" -printf "%p\n" | wc -l'
```

Success is `exited`, exit 0, and the line `state-init owned uid=1654`. The session-runner container then leaves `created`. `unhealthy` on state-init can be ignored. The third command prints how many of the 776 worktree directories have been entered.

**(b) Full census of paths not owned by 1654.** A `find /work ! -user 1654` walks the same tree the chown is walking and contends with it. Depth-1 is already 0 mismatches, and the file sample is 1654. Not worth running while state-init is in `/work`. It would not change the wait.

**(c) Kill state-init, `chown -R -h` in a one-off container, let compose run state-init again.** This needs operator approval. `docs/docker-stack.md` says not to stop main's container by hand. Killing `8720a67f4158` fails the in-flight `compose up`. GNU `chown -R -h` (coreutils 9.1, the same behaviour `dind-entrypoint.sh` lines 35-53 document for `/state/codex/tmp`) would finish `/work` as one process, in minutes rather than hours, and would not follow symlinks. The compose re-run still calls `own_tree` and still forks once per path: already-correct ownership does not make it fast. Net time is a fast chown plus another full slow walk. `/runner-state/grok` is live through temp's bind, so a `chown -R` there can still hit a vanished WAL name. Two chowns at once on `/work` (the live `find` and a one-off) should not be started. Do not do (c) for this recovery.

**(d) Skip flag.** None. The script and the compose service have no environment variable that skips `own_tree`. The contract tests require those three calls.

## Recommendation

Leave `8720a67f4158` running. Do not start a second compose, a one-off `chown`, or a full-tree ownership find. Use the watch commands in (a). Expect `state-init owned uid=1654` around `2026-10-18T01:25Z`, then the created session-runner to start.

## Ready-to-file card

Title: Batch state-init `chown -h` so a preserved server2 work volume does not fork once per path.

Scope: `docker/stack/init-state.sh`, `tests/Antiphon.Tests/Infrastructure/CodexRunnerImageContractTests.cs`, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`, the one-path sentence in `docs/docker-stack.md` (about line 279).

The CARD-1168 behaviour stays: a path that disappears after it was listed is success; a path that is still there and cannot be owned fails; `chown -h` owns a dangling symlink and does not follow it; `/codex-home` stays a single directory; the three `own_tree` roots stay `/state`, `/work`, `/runner-state`. `owned_status` stays an in-container fail gate. No receipt count is added.

Implementation to test: in the `find -exec` shell, call `chown -h "$uid:$gid" -- "$@"` once for the whole batch, and run today's per-path loop only when that call fails. A batch whose paths already exist (including paths already owned by 1654) is then one process per batch instead of one process per path. The observed batch was 1483 paths. Keep `chown -R` out of `init-state.sh` unless those contract tests are updated on purpose; `dind-entrypoint.sh` already uses `chown -R -h` only for `/state/codex/tmp`, and its `|| true` hides a real permission error, which `own_tree` must not do. `/runner-state/grok` stays on this walk because temp bind-mounts it during `redeploy-old`.

## Not done, noted

No code change: batch `chown -h` inside `own_tree`, falling back to the per-path loop only when a batch fails.
