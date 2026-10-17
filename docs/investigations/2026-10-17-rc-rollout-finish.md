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
