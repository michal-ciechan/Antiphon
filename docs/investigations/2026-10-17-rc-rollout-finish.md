# RC rollout finish, redeploy-old through retire-temp (task c0d49725)

Stopped at gate 6. `redeploy-old` refused `RecycleLandUnknown` before any container change. Smoke/Both, `drain-temp`, and `retire-temp` were not run. No retry: the rollout doc treats this token as a stop, and it does not allow a same-class tooling repair.

Requested image SHA for every phase: `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.
Canonical checkout and `GET /api/version`: `ece7cb2e105cf60b5114034aec226272759ff7cd`. `/health` was Healthy. AppHost and the desktop runner were not restarted. The checkout was not switched or pulled.

The doc names gate 6 `redeploy-old`. There is no separate `upgrade-old` phase. That is the command that ran.

## Preflight (2026-10-17, before the phase)

| Check | Result |
|---|---|
| `GET /api/agent-tasks?landPending=true&projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | `items` empty |
| `server2` | `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `draining=true`, `redirectTo=server2-temp`, `retireWhenIdle=false`, `acceptingNewWork=false`, `sessions=0`, `queuedTasks=0`, `runnerSessions=0`, slots `occupied=0` |
| `server2-temp` | `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, `acceptingNewWork=true`, `dispatchEligible=true`, `draining=false`, counters 0 |

## Phases

| Phase | UTC | Exit | Result |
|---|---|---|---|
| `redeploy-old` | start `2026-10-17T04:40:38.2861481Z`, end `2026-10-17T04:41:12.9485372Z` | 2 | `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`, `DIAGNOSIS=RecycleLandUnknown` |
| smoke `server2` and `verify-card0849-caches.ps1 -Case Both` | not started | | |
| `drain-temp` | not started | | |
| `retire-temp` | not started | | |

Command, from `C:\src\Antiphon`:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase redeploy-old
```

## Receipts

Evidence root: `C:\src\Antiphon\.antiphon\rolling-server2\c7275f99c4566f48`

- `deploy-parent\c590-result.json`: `accepted=false`, `diagnosis=RecycleLandUnknown`, `exit=2`
- `deploy-parent\census-deploy-parent-server2.json`: `refusal=RecycleLandUnknown`
- `census-redeploy-old-server2.json`: wrapper census completed and the phase reached `deploy-parent`
- `host-jq-redeploy-old.json`: host jq check ran before the host case

The wrapper open list for project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` had 5 items and did not include `55eaef83-6250-46d0-a422-a44661a9eb5f`. The host open list for the same path had 6 items. The host census row for that id, with no `landRequest` merged, was `status=Queued`, `runnerId=desktop`, `projectId=null`, `scopeSource=None`, both land timestamps null. The last host read was `GET /api/agent-tasks/55eaef83-6250-46d0-a422-a44661a9eb5f` (HTTP 200), then the detail/summary proof refused. A later read of that task showed `summary.status=Succeeded` and `landRequest=null`. Other open rows in the host receipt are desktop tasks, not `server2`.

## After the refusal

`server2` was still `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, drained, idle, redirect `server2-temp`, not accepting. `server2-temp` was still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. Standing `server2` `buildVersion` is not the requested SHA. `server2-temp` is not retired.

A later `redeploy-old` needs an explicit decision. This pass did not start one.

## Authorized retry (2026-10-17)

One new `redeploy-old` was authorized after `55eaef83` had settled. Preflight: land-pending `items` empty; no Queued or Dispatched task on `server2` or `server2-temp`. `be14f4d6` was Dispatched on desktop. `server2` was still drained and idle on `7507b3cf`; `server2-temp` was still accepting `09e48c5d`.

| Phase | UTC | Exit | Result |
|---|---|---|---|
| `redeploy-old` | start `2026-10-17T04:46:13.0431861Z`, end `2026-10-17T04:46:33.6622924Z` | 2 | `HostCaseFailed deploy-parent exit=2`, `DIAGNOSIS=RecycleLandUnknown` |

Evidence root: `C:\src\Antiphon\.antiphon\rolling-server2\c727f40d601ff14e`

The host open list had 6 items. The row with no `landRequest` merged was `c066e2f4-2472-44f3-9717-1e7a453cb1e0`, `status=Dispatched`, `runnerId=desktop`, `role` not stored on the census row, `projectId=null`, `scopeSource=None`. A later read of that task was `summary.status=Succeeded`, `role=Check`, `landRequest=null`. Same token as the first refusal, different task. No second retry. Smoke/Both, `drain-temp`, and `retire-temp` were not started.

After this refusal, `server2` was still drained and idle on `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce` with redirect `server2-temp`. `server2-temp` was still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.

## Three further attempts authorized (2026-10-17)

Up to three more `redeploy-old` runs were authorized for the `RecycleLandUnknown` race. Stop on the first exit 0, on a changed refusal token, or on a census task that is Working with a land in flight. The script was not edited.

Preflight before attempt 1: land-pending `items` empty; no Queued or Dispatched task on `server2` or `server2-temp`. Desktop task `2422f580-d862-4f6c-9293-dfb8021cbea3` was Dispatched, role Code.

| Attempt | UTC | Exit | Receipt |
|---|---|---|---|
| 1 of 3 | start `2026-10-17T04:49:08.5400755Z`, end `2026-10-17T06:18:40.2673050Z` | 2 | `C:\src\Antiphon\.antiphon\rolling-server2\c7275058e9744a15` |
| 2 of 3 | not started | | |
| 3 of 3 | not started | | |

Attempt 1 diagnosis: `RecycleGitAuditUnknown audit check=tmp-git status=1 repo=/tmp/card1259-base-d636249e`. The host census `refusal` is empty, so the land race did not fire. `deploy-parent\recycle.json` is `phase=preflight`, `outcome=refused`, operation `c1008ffe0deb1d99946b6940cf84f675909ad`, `stopReceipts` and `removeReceipts` empty, volume outcomes `pending`. The token is not `RecycleLandUnknown`, so attempts 2 and 3 were not run. Smoke/Both, `drain-temp`, and `retire-temp` were not started.

After this refusal, `server2` was still drained and idle on `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce` with redirect `server2-temp`. `server2-temp` was still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.

## tmp-git repair (2026-10-17)

The audit's `consider_tmp_git` command is `find <checkout> -xdev -mindepth 1 -name .git -print0 -prune -o -type f -name HEAD -print0`. Status 1 is that `find` exiting 1. Reproduced as uid 1654 inside `antiphon-runner-session-runner-1` with the audit's empty git config (`GIT_CONFIG_GLOBAL=/dev/null`, `GIT_CONFIG_SYSTEM=/dev/null`, `core.commitGraph=false`, `gc.writeCommitGraph=false`). On the live mount the same command now exits 0, stderr empty, one match `/tmp/card1259-base-d636249e/.git`. No directory in that tree is unreadable or owned by anyone but `app`. The earlier status 1 was not reproducible; a read error during the long read-only walk is the remaining explanation.

The path is a linked base checkout of `/work/repos/antiphon`, not its own object store. `.git` is `gitdir: /work/repos/antiphon/.git/worktrees/card1259-base-d636249e`. Detached HEAD `41289ba905c64a99a12bd42369fbaa74987bfdc3`. `git rev-list --count HEAD --not --remotes=origin` is 0. Stash count 0. Porcelain 0. `git --git-dir` `diff-index --cached --quiet HEAD` is 0. Reflog has 1 line. `/work` was not modified.

The same `find` was run on every other `.git` parent under `/tmp`: 216 repos, 0 offenders. No other directory was archived or removed. No other container was touched.

Archive, hashes matched before removal:

| Source | Archive | sha256 | Size | Specials |
|---|---|---|---|---|
| `/tmp/card1259-base-d636249e` | `C:\src\Antiphon\.antiphon\server2-rescue-20261016\r5\card1259-base-d636249e.tar` | `58e5012749c22061f57e931c6f555fd8ce6f4444a98ae706ade92fd0f167c2ae` | 167792640 | none |

Container, server2 host, and desktop sha256 were the same. Removal was `docker exec -u 1654:1654` of that one path. It is absent. The worktree admin index still matches HEAD. Staging tars under container `/tmp` and server2 `/tmp` were removed.

## Redeploy after the tmp-git repair (2026-10-17)

One new `redeploy-old` was authorized at `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. A settled `RecycleLandUnknown` (CARD-1317) may be retried up to three times, about 60 seconds apart. A different token or a second git-audit refusal stops the rollout. The script was not edited.

Preflight: land-pending `items` empty (`excluded.total=0`). Queued/Dispatched on the project was desktop Review `c5ca49ae-2c36-4386-b4b3-663d17ea0fa1` only. No Queued or Dispatched task on `server2` or `server2-temp`. `server2` was drained and idle on `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, redirect `server2-temp`, counters 0. `server2-temp` was accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, counters 0.

| Attempt | UTC | Exit | Receipt | Diagnosis |
|---|---|---|---|---|
| authorized run | start `2026-10-17T06:33:14.2953432Z`, end `2026-10-17T06:33:49.5199369Z` | 2 | `C:\src\Antiphon\.antiphon\rolling-server2\c727e2276123cc58` | `RecycleLandUnknown` |
| retry 1 of 3 | start `2026-10-17T06:35:56.9745858Z`, end `2026-10-17T08:07:19.2750228Z` | 2 | `C:\src\Antiphon\.antiphon\rolling-server2\c727e5269bca698d` | `RecycleGitAuditUnknown audit check=for-each-ref status=128 repo=repos/antiphon/.git/worktrees/card1259-base-d636249e` |
| retry 2 of 3 | not started | | | |
| retry 3 of 3 | not started | | | |

The authorized run's host census row with no `landRequest` merged was `761a19e7-d116-4600-bff1-b1a7e750df72`, `status=Queued`, `runnerId=desktop`, `scopeSource=None`. A later read was `summary.status=Succeeded`, `role=Diagnose`, `landRequest=null`. Same settled-land class. After about 60 seconds the land list was empty again and server2 queue hits were still 0. Runners were unchanged, so retry 1 started.

Retry 1 host census `refusal` is empty, so the land race did not fire. The audit then refused `for-each-ref` status 128 on `repos/antiphon/.git/worktrees/card1259-base-d636249e`. That command is `consider_tips`: `git --git-dir=<directory> for-each-ref --format='%(objectname)'`. The path is the admin git directory of the checkout archived and removed above. The live checkout `/tmp/card1259-base-d636249e` stays absent. This pass did not modify `/work`. This is a second git-audit refusal and a different token from `tmp-git status=1`, so retries 2 and 3 were not started. Smoke/Both, `drain-temp`, and `retire-temp` were not started.

`deploy-parent\recycle.json` for `c727e5269bca698d` is `phase=preflight`, `outcome=refused`, operation `c1008001b165462094cb4a27f484ffe6fc850`, `composeDigest` `0c0d1515ccc640731beca6942613a1d489c3c364898eb35e0bd02dcc441e1147`, image `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835` (the previous image), `stopReceipts` and `removeReceipts` empty, volume outcomes `pending`.

After this refusal, `server2` was still drained and idle on `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce` with redirect `server2-temp`. `server2-temp` was still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. Standing `server2` `buildVersion` is not the requested SHA. A later `redeploy-old` needs an explicit decision. This pass did not start one.

## Stale admin cleanup and pre-check (2026-10-17)

This pass did not run `redeploy-old`. Reads, archives, and prunes were `docker exec -u 1654:1654` in `antiphon-runner-session-runner-1`, with the audit environment (`GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`, `GIT_TERMINAL_PROMPT=0`, `LC_ALL=C`, `GIT_CONFIG_GLOBAL=/dev/null`, `GIT_CONFIG_SYSTEM=/dev/null`, `GIT_NO_REPLACE_OBJECTS=1`, `core.commitGraph=false`, `gc.writeCommitGraph=false`). The script was not edited.

`/work/repos/antiphon/.git/worktrees/card1259-base-d636249e` was still present. Its `gitdir` file is `/tmp/card1259-base-d636249e/.git`, and that checkout is absent. There is no `locked` file and no `config.worktree`. HEAD is `41289ba905c64a99a12bd42369fbaa74987bfdc3`. `for-each-ref`, `rev-parse --verify HEAD`, and `diff-index --cached --quiet --ignore-submodules=none HEAD` all exited 0 in the live container. `refs/` has no files. `git worktree prune -n -v` named only that entry. The earlier helper refusal was `for-each-ref` status 128 on this same admin directory.

Archive, hashes matched container, server2 host, and desktop before removal:

| Source | Archive | sha256 | Size | Specials |
|---|---|---|---|---|
| `/work/repos/antiphon/.git/worktrees/card1259-base-d636249e` | `C:\src\Antiphon\.antiphon\server2-rescue-20261016\r5\card1259-base-d636249e-worktree-admin.tar` | `df3589b770c88df8f1aa1432da16ac4058bc6345b33d54c9e2fdbff9cb3ea86f` | 1024000 | none |
| `/tmp/antiphon-e2e-channel-repos/ba15ddfae6134bbcbe69050723771327/work/.git/worktrees/card-CARD-0001` | `r5\card-CARD-0001-worktree-admin.tar` | `d6f8b325717ff3b98ff6773c87137131a00518a602fbd8743fe4cf58159de6b3` | 10240 | none |
| `/tmp/c1105-audit-hKPnq6/work/repo/.git/worktrees/c1005-master` | `r5\c1005-master-worktree-admin.tar` | `20cacd169a87239967873baa4667256115b359a5f15f1d9a0af12132a163d6a4` | 10240 | none |

Each removal was `git --git-dir=<that repo> worktree prune -v` with no `--expire=now`. The dry run named only the archived entry. Each admin directory is absent. Staging tars in the container and on the server2 host were removed. The two extra admins had empty `refs/`, an index that matched HEAD, and a `gitdir` target that was already gone. Their prunes were on those fixture repos, not on `/work/repos/antiphon`.

Pre-check, about two minutes: 813 `.git/worktrees/*` admin directories under `/work/repos`, `/work/worktrees`, and `/tmp`. After the prunes, a second walk found 0 admins whose `gitdir` target is absent. On the first walk, 0 admins failed `for-each-ref`, `rev-parse --verify HEAD`, `rev-parse --git-common-dir`, or the stale-index check except the two plain stale rows above. 1006 `.git` entries were opened at their real git directory: 992 passed `for-each-ref` and `rev-parse --verify HEAD`. `/work/repos/antiphon` common dir passed both; HEAD is `c6cee79f88d82710515106fb0228f817219871cc`. The 16 fixture paths, `/work/repos/slides`, the two slides worktrees, and the five broken-pointer checkouts are absent. The three rescue checkouts exist (`task-2d835afa`, `task-768488ed`, `/work/review-evidence/6c3dfed9/probe`). Eight registered `/tmp` worktrees remain (the ninth was `card1259-base-d636249e`); each `for-each-ref`, HEAD, and `consider_tmp_git` find exited 0, and each `.git` entry inside them is the checkout's own pointer file.

Fourteen `/tmp` paths are not plain stale admin dirs and were left in place. The audit find root is `/work`, and none of these paths sit inside the eight registered tmp checkouts. Thirteen are git directories whose `for-each-ref` exits 0 and whose `rev-parse --verify HEAD` exits 128 with `Needed a single revision` (no commit). One path is not a git repository (`/tmp/tmp.ZaQPQ72T0b/.git`, both commands exit 128). The thirteen are `review610-4614eb6a325045449076eafb1284b3f6` `slow-git`, `child-link`, `nested-container`, and `wait-stop`; `/tmp/c723-01a13963854d74efa8d6c3a567c3c965`; `/tmp/antiphon-review-b2f5300d/repository-probe-5da874f2b7544075898f04801b5e6166`; `/tmp/c1199-inherited-review-e4e370dccc844b95b8703003fd2a3b7f`; `/tmp/c1199-why-YTHU`; and five `ledger-scratch` directories under `/tmp/c1199-review-c7cf7371`.

The fresh Deploy task runs, from `C:\src\Antiphon`:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase redeploy-old
```

If that exits 0, the remaining phases are smoke (`runner-drain.ps1 status -RunnerId server2`, uid 1654 `codex --version`, `pwsh --version`, `dotnet --version`, then `verify-card0849-caches.ps1 -Case Both -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184`), then `deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase drain-temp -WaitIdleMinutes 240`, then the same command with `-Phase retire-temp`.
--- feat/card-task-77abf8f0 15982940e9f4e51da74649516896aff2edef06ba 2026-10-17T16:22:19+01:00 ---
# 2026-10-17 rc rollout finish — redeploy-old stop

Deploy task `77abf8f0` on `feat/card-task-77abf8f0`. Target `-Sha` `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. Canonical checkout and `GET /api/version` stayed `69978ec1a246cd7a860f88a364eafc53a416aff7`. AppHost and the desktop runner were not restarted. No scripts were edited.

Preflight at start: server2 drained and idle (`acceptingNewWork=false`, `redirectTo=server2-temp`, sessions/runnerSessions/queuedTasks all 0, build `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, container `fc2983ea4991` running since `2026-10-11T13:42:57Z`). server2-temp accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` land-pending was empty before each start. A Running land on `187ee0ab-d84b-4ffe-9f87-30cdc92b01aa` (CARD-1320 Code) cleared before the first start.

Smoke, `drain-temp`, and `retire-temp` were not run.

## Attempt 1 — census retry class

- Start `2026-10-17T12:12:55Z`, wrapper pid 1568, exit 2 about `2026-10-17T12:13:34Z`.
- Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727e81ce041ca91`.
- Diagnosis verbatim: `RecycleTaskCensusUnknown cause=Unstable`.
- Host case: `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`.
- Pass 1 of the deploy-parent census gained desktop check `c8dac380-0774-4ed1-9e59-7b1fbbd7d728` (`check #1 on task 77abf8f0`, Queued, scopeSource None). A later read showed it Succeeded with `landRequest` null.
- No container stop. Standing container start time unchanged.

## Attempt 2 — second census retry

- Start `2026-10-17T12:18:24Z`, wrapper pid 33776, exit 2 about `2026-10-17T12:20:17Z`. About 60s after attempt 1.
- Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727ce33eabc1a49`.
- Diagnosis verbatim: `RecycleTaskCensusUnknown cause=Unstable`.
- Pass 1 gained desktop Diagnose `fcd95bb3-764d-421c-911d-b809775f3cf6` (`labels for CARD-1287`, Queued). A later read showed it Succeeded with `landRequest` null.
- No container stop. Standing container start time unchanged.

## Attempt 3 — stop, containers removed

- Start `2026-10-17T12:23:48Z`, wrapper pid 36816, exit 2 at `2026-10-17T15:19:44Z`.
- Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c72724f3f2dfd7b8`.
- Local redeploy-old census passed. deploy-parent then audited and removed containers before its own census refused.
- Audit helper container `abc517209651` (`boring_herschel`) ran the old image `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835` from `2026-10-17T13:51:05Z`. Audit summary line: `repositories=800 partial=800`. No `RecycleGitAuditUnknown` line. Helper tmpfs stayed about 20M of 2.0G. The helper is gone.
- `deploy-parent/recycle.json` operation `c100808382374392e4ce1ac7dde48ca3b195f`: `phase=containersRemoved`, `outcome=refused`, `ownedRemoved=true`, `dryRun=false`.
- Stop and remove receipts (2 each): `fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832` and `2c96347842e49022771fccc34f74bb04549ceeb603a5944d860b12f438953cfe`.
- Volume outcomes all `pending`: `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data`.
- Diagnosis verbatim: `RecycleLandInFlight 4ce63b7a-db63-4189-90a5-96a55abb03b3`.
- Stderr verbatim: `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`.
- That task was a Succeeded Plan on server2-temp titled `Plan release branch model amendment`, `landRequestedAt` `2026-10-17T15:17:43Z`, `landRequest.state=Running` when read just after the stop. This token is not in the settled-census retry class. Both allowed retries were already used.

## State at stop

- `antiphon-runner-session-runner-1` is absent. server2 `available=false`, `dispatchEligible=false`, `acceptingNewWork=false`, `draining=true`, `redirectTo=server2-temp`, `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f`, `processBootId=9a6d75cc-09cb-4034-818e-440ceb9e2d2f`, `disconnectReason=transport_abort`, `runnerSessions` null, sessions 0, queuedTasks 0.
- server2-temp still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184` (`antiphon-runner-temp-session-runner-1` up). It was serving work (5 sessions, 1 queued) at the stop read.
- Images `antiphon-server2/session-testing:09e48c5d6f1d` and `:7507b3cfe0e2` are still present. The old image id matches the recycle journal.
- No second deploy was started after the stop. Smoke, drain-temp, and retire-temp were not started.
--- feat/card-task-41d7a523 ce4b503c3034bbbe36e342c7b2fa522b91075cfd 2026-10-18T11:29:04+01:00 ---
# 2026-10-18 server2 preserve recovery — stopped at RecycleResumeRequired

Deploy task `41d7a523` on `feat/card-task-41d7a523`. Manual recovery of standing server2 after operation `c100808382374392e4ce1ac7dde48ca3b195f` (`phase=containersRemoved`, `outcome=refused`, `RecycleLandInFlight`). Diagnosis `e7a6d63987bc72ddfc775fefed157b32e5525040` on `feat/card-task-69c07e08`. Target `-Sha` `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. No `-ResumeRecycle`. No volume was deleted.

Canonical checkout stayed detached at `69978ec1a246cd7a860f88a364eafc53a416aff7`. AppHost and the desktop runner were not restarted. Prior stop record: `15982940e9f4e51da74649516896aff2edef06ba` on `feat/card-task-77abf8f0`.

Smoke, the Plan canary, `verify-card0849-caches.ps1 -Case Both`, `drain-temp`, and `retire-temp` were not run. `acceptingNewWork` is still false.

## Census

`check-census` from `C:\src\Antiphon`. Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

| UTC | Receipt | server2 boundOpen | landPending both | server2-temp |
|---|---|---|---|---|
| 2026-10-17T21:03:48Z | `c7274ec9e0eca0d5` | 0 | 0 | accepting `09e48c5d`, build matches |
| 2026-10-17T21:06:36Z | `c727a9d3facd97dd` | 0 | 0 | accepting `09e48c5d` (immediately before compose) |
| 2026-10-17T21:22:18Z | `c727d2eab3d70d34` | 0 | 0 | during state-init |
| 2026-10-17T21:53:59Z | `c727692a143d16de` | 0 | 0 | during state-init |
| 2026-10-17T22:55:24Z | `c72766b99d7b4884` | 0 | 0 | during state-init |
| 2026-10-17T23:56:56Z | `c727618b2ba6ed10` | 0 | 0 | during state-init |
| 2026-10-18T00:35:30Z | `c72778175a41489f` | 0 | 0 | accepting `09e48c5d` (immediately before the seal) |

server2-temp `boundOpen` moved 5 → 3 → 2 while it kept accepting the target SHA. It was not drained.

## Compose up

2026-10-17T21:06:52Z on server2, as `mc`. Backup `stack.env.c100808382374392e4ce1ac7dde48ca3b195f`. Only `SOURCE_REVISION=09e48c5d6f1d404cfea8ec0d992270424fd2a184` and `SOURCE_SHA12=09e48c5d6f1d` changed. `BUILD_SLOTS_SHA12` stayed `a8b4e9e5a071`. Then `docker compose -p antiphon-runner -f /work/repos/antiphon/docker-compose.server2-runner.yml --project-directory /work/repos/antiphon up -d --no-build`. Host checkout was already `09e48c5d`.

`antiphon-runner-state-init-1` ran `init-state.sh` `own_tree /work` from 2026-10-17T21:06:54Z and exited 0 at 2026-10-18T00:32:45Z. `antiphon-runner-session-runner-1` (`a6457bc89508`) then started. Image `antiphon-server2/session-testing:09e48c5d6f1d` id `sha256:a9be72ebb98bddae896fd93649897cdc7d8c59449a36629e4dd2100fd956b50c`. The wrapper's trailing `docker compose ps` hit a local CR in the remote script (`unknown shorthand flag: '\r'`) after `COMPOSE_EXIT=0`. Compose itself succeeded.

Confirmed on the new session-runner:

- mounts: `antiphon-runner_work` `/work`, `antiphon-runner_runner-state` `/state`, `antiphon-runner_dind-data` `/var/lib/docker`, `antiphon-runner_runner-tmp` `/tmp`
- `/tmp` mode `1777`
- uid 1654 read one byte of `PhoneHome__SecretPath` `/run/antiphon/phone-home` (`phone-home-readable=true`); the byte was not printed
- `curl http://127.0.0.1:8080/health` returned `Healthy` and `docker info` exited 0 by 2026-10-18T00:34:45Z; container health `healthy`
- build-slots container left as it was

`GET /api/session-runners/server2/status` at 2026-10-18T00:34:45Z:

- `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f`
- `processBootId=0e14a3d4-ba81-4ae9-a956-515188b82233` (new boot; store unchanged)
- `available=true` `dispatchEligible=true` `draining=true` `redirectTo=server2-temp` `acceptingNewWork=false`
- `sessions=0` `runnerSessions=0` `queuedTasks=0`

## Seal

2026-10-18T00:35:45Z. Backup `/home/mc/antiphon-server2/recycle/c100808382374392e4ce1ac7dde48ca3b195f.json.before-preserve-seal` (still `containersRemoved` / `refused`). Sealed file:

- `phase=completed` `outcome=completed` `diagnosis=preserve-seal volumes kept`
- outcomes `kept` for `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data`

Those three volumes and `antiphon-runner_runner-state` still exist. Copy of the sealed file: `C:\src\Antiphon\.antiphon\rolling-server2\c727de92cd220c2a\recycle-preserve-seal.json`.

## redeploy-old

2026-10-18T00:37:40Z–00:37:42Z from `C:\src\Antiphon`, no `-ResumeRecycle`. Exit 2.

Wrapper output:

```
System.Threading.Tasks.VoidTaskResult
Rolling deploy stopped: RecycleResumeRequired
```

Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727de92cd220c2a`. Host jq check passed (`jq-1.7.1`, digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, `outcome=existing`, `sshExit=0`). `census-redeploy-old-server2.json` is the empty receipt created before the throw; the phase never reached its task census, `deploy-parent`, cache verify, or drain clear.

`Assert-NoIncompleteRecycle` prints every journal with `sourceSha` equal to the rollout SHA, `project=antiphon-runner`, and `phase!="completed"`. After the seal, these five remain:

| operationId | phase | outcome |
|---|---|---|
| `c1008001b165462094cb4a27f484ffe6fc850` | preflight | refused |
| `c10084fa7d62ec9ba4845bf071445f0d516b1` | preflight | refused |
| `c1008bd48f43e1fa54a19ba070d3ea30986b9` | preflight | refused |
| `c1008ce5bf1ed6d4b4ed7918d807e8489c390` | preflight | refused |
| `c1008ffe0deb1d99946b6940cf84f675909ad` | preflight | refused |

The sealed operation is no longer in that set. None of these five files were edited.

## State at stop

Standing server2 is up on `09e48c5d`, store `f519bd08-e53a-47d1-adb1-2ab33475446f`, healthy, drained onto server2-temp, `acceptingNewWork=false`, counters 0. server2-temp still accepts `09e48c5d`. Volumes named above are present. Drain was not cleared.

Another `redeploy-old` without a change to those five journals throws `RecycleResumeRequired` again. `-ResumeRecycle` on `c100808382374392e4ce1ac7dde48ca3b195f` is the wrong command: `phase=completed` with outcomes `kept` refuses, and a resume that still sees `pending` deletes the three volumes.

## Archive, then stand-down

Operator approval on 2026-10-18: archive the five refused-preflight journals, nothing deleted. Re-read at 2026-10-18T10:27:25Z: each was still `phase=preflight`, `outcome=refused`, `ownedRemoved=false`, `stopReceipts=[]`, `removeReceipts=[]`, `sourceSha=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, project `antiphon-runner`.

At 2026-10-18T10:28:08Z those five files were moved with `mv` (not `rm`) from `/home/mc/antiphon-server2/recycle/` to `/home/mc/antiphon-server2/recycle/archived-refused/`, names unchanged. The sealed journal `c100808382374392e4ce1ac7dde48ca3b195f.json` was not moved and was still `phase=completed` with the three outcomes `kept`. Copies: `C:\src\Antiphon\.antiphon\rolling-server2\c727de92cd220c2a\archived-refused\archived-refused\`.

No census and no `redeploy-old` ran after that move. The orchestrator stood this task down at its ceiling before step 3. A new Deploy task owns archive confirmation, `redeploy-old`, smoke, `drain-temp`, and `retire-temp`.
--- feat/card-task-26205ec4 73af32ba70b67f661485858b42e8836a37a6b87f 2026-10-18T13:06:09+01:00 ---
# 2026-10-18 server2 rollout finish - stopped at census

Deploy task `26205ec4` on `feat/card-task-26205ec4`. Target `-Sha` `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. No `-ResumeRecycle`. No volume was deleted. No journal was edited or moved by this task.

Prior records: `fcc7fd7caf940cffb5ad7845992fa0c60e23d50b` and tip `ce4b503c3034bbbe36e342c7b2fa522b91075cfd` on `feat/card-task-41d7a523`; diagnosis `e7a6d63987bc72ddfc775fefed157b32e5525040` on `feat/card-task-69c07e08`.

Canonical checkout stayed detached at `69978ec1a246cd7a860f88a364eafc53a416aff7`. `GET /api/version` was that same SHA with `land-v2`. `/health` was Healthy. AppHost and the desktop runner were not restarted.

## Journals

Re-read at 2026-10-18T10:37Z. The five refused-preflight journals were already in `/home/mc/antiphon-server2/recycle/archived-refused/` (moved by task `41d7a523` at 2026-10-18T10:28:08Z). Each was still the only copy, `phase=preflight`, `outcome=refused`, `ownedRemoved=false`, `stopReceipts=[]`, `removeReceipts=[]`, `sourceSha=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, project `antiphon-runner`. They were not moved again.

| operationId | diagnosis |
|---|---|
| `c1008001b165462094cb4a27f484ffe6fc850` | `RecycleGitAuditUnknown` for-each-ref status 128 `repos/antiphon/.git/worktrees/card1259-base-d636249e` |
| `c10084fa7d62ec9ba4845bf071445f0d516b1` | `RecycleGitAuditUnknown` for-each-ref status 128 `repos/antiphon/.git/worktrees/master` |
| `c1008bd48f43e1fa54a19ba070d3ea30986b9` | `RecycleWorktreeDirty` status `worktrees/task-2d835afa` |
| `c1008ce5bf1ed6d4b4ed7918d807e8489c390` | `RecycleWorktreeDirty` index-content `worktrees/task-71fd0d46` |
| `c1008ffe0deb1d99946b6940cf84f675909ad` | `RecycleGitAuditUnknown` tmp-git status 1 `/tmp/card1259-base-d636249e` |

Sealed journal `c100808382374392e4ce1ac7dde48ca3b195f` was read only: `phase=completed`, `outcome=completed`, `diagnosis=preserve-seal volumes kept`, outcomes `kept` for `antiphon-runner_work`, `antiphon-runner_runner-tmp`, and `antiphon-runner_dind-data`. Other top-level journals were not opened for edit. None of them is an unfinished `antiphon-runner` journal for this SHA. The only same-SHA `antiphon-runner` journal left in `/home/mc/antiphon-server2/recycle/` is the sealed one.

Copies of the five archived files: `C:\src\Antiphon\.antiphon\rolling-server2\c7275c8685ffd5da\archived-refused\`.

## Runners

`GET /api/session-runners/server2/status` at 2026-10-18T10:37:14Z:

- `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f`
- `processBootId=0e14a3d4-ba81-4ae9-a956-515188b82233`
- `available=true` `dispatchEligible=true` `draining=true` `redirectTo=server2-temp` `acceptingNewWork=false`
- `sessions=0` `runnerSessions=0` `queuedTasks=0`

server2-temp was accepting the same SHA, store `1559ba5a-92e3-42a4-a891-74bce5267a2e`, `sessions=4` `runnerSessions=4` `queuedTasks=0`.

`antiphon-runner-session-runner-1` (`a6457bc89508`) was Up (healthy) on `antiphon-server2/session-testing:09e48c5d6f1d`. The three kept volumes and `antiphon-runner_runner-state` were present. Temp containers were still up.

## Census stop

`check-census` from `C:\src\Antiphon` at 2026-10-18T10:38:38Z-10:38:41Z. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7275c8685ffd5da`. Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

| Runner | open | boundOpen | landPending |
|---|---|---|---|
| server2 | 12 | 0 | 0 |
| server2-temp | 12 | 4 | 0 |

landPending was 0. server2 had no Queued or Dispatched task. server2-temp still had two Dispatched tasks, re-read after the census:

| id | status | role | title |
|---|---|---|---|
| `6b367dd6-e20f-4e3c-8a3b-e1070cd8620c` | Dispatched | Review | Review npm fixture network id |
| `b2d71f07-1259-4d73-9b2d-240e54f16068` | Dispatched | Review | Review orchestrator bundle trim |

Both had null land timestamps. Also bound to server2-temp, not Dispatched: Blocked Code `89bf7b58-b629-46ca-9b4e-500fd29014c0` (CARD-1320 S3 rebase onto master) and Blocked Code `bd6bce3d-816e-469f-a30d-0621ab09b644` (CARD-1320 S4 rebase onto master, land Completed).

This is not the settled-task census race. `redeploy-old` was not run in that pass. The continuation below is the later approval.

## Continuation: redeploy-old and gate 7 smoke

Operator reply on this task: the two Dispatched Reviews on server2-temp are expected, and the Blocked Code tasks are not a gate. The hard conditions are `landPending=0` and server2 `boundOpen=0`.

`check-census` from `C:\src\Antiphon` at 2026-10-18T10:43:34Z. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727a047200160e8`. server2 `boundOpen=0` `landPending=0`. server2-temp `boundOpen=4` `landPending=0`.

One `redeploy-old` at 2026-10-18T10:43:50Z-10:44:00Z, no `-ResumeRecycle`. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c72721ffbe14c3d9`. Host jq check passed (`jq-1.7.1`, digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, `outcome=existing`). `verify-runner-caches` accepted with `schema=2` `kind=cold` `smoke=not-run`. Mounts included `antiphon-runner_work` `/work`, `antiphon-runner_runner-state` `/state`, `antiphon-runner_dind-data` `/var/lib/docker`, `antiphon-runner_runner-tmp` `/tmp`, and the three cache volumes. The phase skipped `deploy-parent` because `buildVersion` was already the target SHA. Drain clear reason recorded on the runner is `CARD-0727 rolling upgrade complete`.

`GET /api/session-runners/server2/status` after that phase:

- `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f` (unchanged)
- `processBootId=0e14a3d4-ba81-4ae9-a956-515188b82233` (unchanged)
- `acceptingNewWork=true` `draining=false` `dispatchEligible=true` `available=true`
- `sessions=0` `runnerSessions=0` `queuedTasks=0`

Container `antiphon-runner-session-runner-1` stayed `a6457bc89508` on `antiphon-server2/session-testing:09e48c5d6f1d`. server2-temp stayed accepting the same SHA with 4 sessions.

Gate 7 version smoke at 2026-10-18T10:44:39Z, exit 0:

- `runner-drain.ps1 status -RunnerId server2` showed `acceptingNewWork=true` and the target `buildVersion`
- uid 1654 `codex --version` printed `codex-cli 0.160.0`
- `pwsh --version` printed `PowerShell 7.5.4`
- `dotnet --version` printed `10.0.402`

The status row also has `codexCliVersionError=stderr_output` with `codexCliVersion=0.160.0`. The uid 1654 exec itself exited 0.

A second `check-census` at 2026-10-18T10:45Z, receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727d5770d4bf76d`, still had server2 `boundOpen=0` and `landPending=0` on both runners. server2-temp `boundOpen=4`.

The Plan canary, `verify-card0849-caches.ps1 -Case Both`, `drain-temp`, and `retire-temp` had not been run at that point. Canonical checkout stayed `69978ec1a246cd7a860f88a364eafc53a416aff7`.

## Continuation: canary, Both, and drain-temp

Plan canary `a648697f` on server2 (CARD-1311) was Queued at 2026-10-18T10:47Z, Dispatched at 10:52:10Z, and Succeeded at 10:53:32Z. Session `5e1bdc76-e7ac-4e2e-bfe0-b47ed515c29a` ended 10:53:24Z. It pushed `e6963c2e7f56c8a5d6c9a9396f306153c26ba420` on `feat/card-task-a648697f`. `docs/investigations/2026-10-18-server2-canary.md` at that commit ends with `canary: ok`. No land was requested.

`verify-card0849-caches.ps1` requires checkout HEAD to equal `-Sha`. Canonical stayed at `69978ec1a246cd7a860f88a364eafc53a416aff7`. Both ran from a detached worktree `C:\Antiphon\worktrees\c849-both-26205ec4` at `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, 2026-10-18T10:55:15Z-10:55:32Z, exit 0:

```
C849_BOTH kind=cold schema=2 digestType=none runners=2 writableVolumes=3 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0
```

Receipt copy: `C:\src\Antiphon\.antiphon\c849-c84932264738a8ab43a8`.

`check-census` at 2026-10-18T10:56:19Z, receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7279fa60cdb3480`: server2 `boundOpen=0` `landPending=0`, server2-temp `boundOpen=3` `landPending=0`.

`drain-temp -WaitIdleMinutes 240` started at 2026-10-18T10:56Z from `C:\src\Antiphon`. By 10:57:28Z server2-temp was `draining=true`, `redirectTo=server2`, `retireWhenIdle=true`, `acceptingNewWork=false`, `retiredAt` empty, `sessions=3`. Standing server2 stayed accepting on the target SHA and store `f519bd08-e53a-47d1-adb1-2ab33475446f`.

Temp seats fell from 3 to 2 at 11:11Z when Review `6b367dd6` Succeeded and session `10d3e112-c311-46de-a230-18870b325a3f` stopped. At 11:24Z the two seats still Running were:

| session | age | task | task status |
|---|---|---|---|
| `81088675-4185-4faf-aa2d-d458d9bc8339` | 81933 s | `89bf7b58-b629-46ca-9b4e-500fd29014c0` CARD-1320 S3 | Failed, session still Running |
| `ee2f68fa-b194-4c77-8738-0435a9258246` | 75913 s | `bd6bce3d-816e-469f-a30d-0621ab09b644` CARD-1320 S4 | Failed, session still Running |

`openTaskId` on both slots was null. `orphan=false`. Neither session was released. The local wait was stopped at 11:24:37Z so this report could be written inside the task budget. The server drain was left in place: temp still `draining=true`, `redirectTo=server2`, `retireWhenIdle=true`, `acceptingNewWork=false`, `sessions=2`. Standing server2 was accepting with `sessions=1`. `retire-temp` was not run.

## Continuation: resumed drain-temp stopped at container cleanup

Refinement `C:\src\Antiphon\.antiphon\task-26205ec4-refinement-20261018105345.md` arrived after that hold. It said the canary passed and to run Both, then `drain-temp -WaitIdleMinutes 240`, then `retire-temp`. Both had already exited 0, so it was not run again. The drain wait was restarted. No session was released.

`check-census` from `C:\src\Antiphon` at 2026-10-18T11:29:21Z. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727572d74c3d7ab`. Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

| Runner | open | boundOpen | landPending |
|---|---|---|---|
| server2 | 10 | 1 | 0 |
| server2-temp | 10 | 0 | 0 |

landPending was 0. server2 `boundOpen=1` is work on the standing runner. The same two temp slots were still Running: `81088675-4185-4faf-aa2d-d458d9bc8339` (pid 22351) and `ee2f68fa-b194-4c77-8738-0435a9258246` (pid 7641), both `openTaskId` null. Temp was already `draining=true`, `redirectTo=server2`, `retireWhenIdle=true`, `acceptingNewWork=false`, `retiredAt` empty, `sessions=2`.

`drain-temp -WaitIdleMinutes 240` restarted from `C:\src\Antiphon` at 2026-10-18T11:30:57Z. No `-ResumeRecycle`. Because temp was already draining with that redirect and `retireWhenIdle`, the phase did not POST drain again. It exited 2 at 2026-10-18T11:46:22Z. Printed text:

```
Rolling deploy stopped: HostCaseFailed retire-temp-containers exit=2
```

Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7272d7884949a76`. The host-case diagnosis is `RecycleTaskCensusUnknown cause=Http status=502` on the open-task list for project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, `elapsedMs=3095`. The cause is `Http`, not `Unstable`, so it was not retried. `retire-temp` was not run.

Temp had already retired itself for idle before that census: `idleObservedAt=2026-10-18T11:43:08.890144+00:00`, `retiredAt=2026-10-18T11:45:09.0716214+00:00`, `retireReason=idle`, `sessions=0`, `runnerSessions=null`, `available=false`, `dispatchEligible=false`, `acceptingNewWork=false`, `disconnectReason=transport_abort`. Cleanup receipt `outcome=refused`, `removals=[]`, `finalCensus=null`, operation `c994396e44cdae944edf85623d0fc18d5574`.

Read after the stop:

- Standing server2 `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, store `f519bd08-e53a-47d1-adb1-2ab33475446f`, boot `0e14a3d4-ba81-4ae9-a956-515188b82233`, `acceptingNewWork=true`, `draining=false`, `sessions=0`, `runnerSessions=0`, `queuedTasks=0`.
- `antiphon-runner-session-runner-1` still `a6457bc89508`, Up (healthy), image `sha256:a9be72ebb98bddae896fd93649897cdc7d8c59449a36629e4dd2100fd956b50c` (`antiphon-server2/session-testing:09e48c5d6f1d`). Build-slots `4cabb8afc676` left Up.
- Temp containers still present and exited: `6a2838d8e7fb` session-runner Exited (0), `ac048a240b9f` state-init Exited (0).
- Volumes still present: `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data`, `antiphon-runner_runner-state`, the three `antiphon-runner-cache-*` volumes, and `antiphon-runner-temp_work`, `antiphon-runner-temp_runner-tmp`, `antiphon-runner-temp_dind-data`, `antiphon-runner-temp_runner-state`. No volume was deleted.
- `GET /health` was Healthy. `GET /api/version` was `69978ec1a246cd7a860f88a364eafc53a416aff7` with `land-v2`. Canonical HEAD stayed that SHA. AppHost and the desktop runner were not restarted.
- `GET /api/session-runners/server2-temp/slots` then returned 503 `phone_home_unavailable`.

The `VoidTaskResult` line in the phase output is the same benign host-output line seen on the exit-0 `redeploy-old`.

## Continuation: idle temp, drain-temp cleanup, retire-temp audit stop

Operator decision on this task: the two stale server2-temp sessions could be released, then `drain-temp` and `retire-temp` re-run. Both tasks were already terminal before any release call.

| task | status | session | session status | working | endedAt |
|---|---|---|---|---|---|
| `89bf7b58-b629-46ca-9b4e-500fd29014c0` | Failed | `81088675-4185-4faf-aa2d-d458d9bc8339` | Stopped | false | `2026-10-18T11:42:19.487146Z` |
| `bd6bce3d-816e-469f-a30d-0621ab09b644` | Failed | `ee2f68fa-b194-4c77-8738-0435a9258246` | Stopped | false | `2026-10-18T11:42:19.792764Z` |

`check-census` at 2026-10-18T11:52:54Z, exit 0, receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7273c2ebee3950f`. server2 `boundOpen=0` `landPending=0`. server2-temp `boundOpen=0` `landPending=0`. Temp status was already `retiredAt=2026-10-18T11:45:09.0716214Z`, `retireReason=idle`, `sessions=0`, `runnerSessions=null`, `available=false`. `GET /api/session-runners/server2-temp/slots` returned 503 `phone_home_unavailable`. No `runner-slots.ps1 release` was sent. The documented per-session route kills that session's process tree (`POST /api/session-runners/{runnerId}/slots/{sessionId}/release`), and these two sessions had already ended with temp occupancy 0.

`drain-temp -WaitIdleMinutes 240` from `C:\src\Antiphon` at 2026-10-18T11:53:09Z-11:54:13Z. No `-ResumeRecycle`. Exit 0. Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727cb56f213782e`. Container cleanup `outcome=completed`, `finalCensus` empty, operation `c9943b7ac031fc7746f489d7923bf48d9158`. Removed exited containers `6a2838d8e7fb` and `ac048a240b9f`. Temp project containers were then absent. The four `antiphon-runner-temp_*` volumes were still present.

`retire-temp` from `C:\src\Antiphon` then exited 2 at 2026-10-18T12:04:12Z. Printed text:

```
DIAGNOSIS=RecycleGitAuditUnknown audit check=gitdir-entry status=0 repo=repos/antiphon
Rolling deploy stopped: HostCaseFailed retire-temp-runner exit=2
```

Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c7274aeaa51dc996`. The inner container census completed with empty candidates. The runner recycle journal `c1008a24aceabd9ff4ec9a174752ae3ca8475` is `phase=preflight`, `outcome=refused`, `ownedRemoved=false`, `stopReceipts` empty, `removeReceipts` empty. Volume outcomes stayed `pending` for `antiphon-runner-temp_work`, `antiphon-runner-temp_runner-tmp`, `antiphon-runner-temp_dind-data`, and `antiphon-runner-temp_runner-state`. The journal was not edited. This token is not `RecycleLandUnknown` or `RecycleTaskCensusUnknown cause=Unstable`, so it was not retried.

After the stop, standing server2 was still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`, store `f519bd08-e53a-47d1-adb1-2ab33475446f`, `sessions=0`. Container `a6457bc89508` was still Up (healthy) on `antiphon-server2/session-testing:09e48c5d6f1d`. Build-slots `4cabb8afc676` was left Up. Main volumes and the three cache volumes were still present. Temp containers were absent. `GET /health` was Healthy. `GET /api/version` and canonical HEAD stayed `69978ec1a246cd7a860f88a364eafc53a416aff7` with `land-v2`. AppHost and the desktop runner were not restarted. No volume was deleted.
