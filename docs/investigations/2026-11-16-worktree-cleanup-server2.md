# Server2 worktree cleanup: blocked by runner placement and access

Task: `016fd320`; observation window: 2026-11-16 13:59–14:01 UTC.

No worktrees, branches, registrations, retained evidence, containers, images or volumes were deleted. Reclaimed bytes: **0**. Standing operator authority already covers eligible cleanup; no additional approval was requested.

## Host identity and scope

The brief requires cleanup of the standing `server2` runner and explicitly protects `server2-temp`. This task actually runs in container hostname `b7f94212541f` on **server2-temp**:

- `/proc/self/mountinfo` maps `/work` to `/var/lib/docker/volumes/antiphon-runner-temp_work/_data` and `/state` to `antiphon-runner-temp_runner-state/_data`.
- `scripts/delegate.ps1 -Status 016fd320` reports `platform: Any on server2-temp; observed linux`.
- The canary task's stored result independently records this same hostname on server2-temp.
- The owning Git common directory is `/work/repos/antiphon/.git`. Its complete `git worktree list --porcelain` contains the repository registration and exactly the three task trees below. These registrations belong to temp, not standing server2.

The standing server2 filesystem was **not accessible or inventoried**. No claim of absence or cleanliness is made for it. The local filesystem census is included to document why nothing visible may be deleted.

## Read-only inventory and exact-path plan

Sizes are allocated bytes from sequential `du -sk` observations, multiplied by 1024. Task states were checked individually with `pwsh -NoProfile -File scripts/delegate.ps1 -Status <id>`. Terminal age uses the reported completion time; branch/HEAD publication uses live `git ls-remote origin` and `git branch -r --contains HEAD`. No untracked or tracked changes were reported by either other task tree's full porcelain status; both returned no commits from `rev-list HEAD --not --remotes=origin`. The repository has no `refs/stash`.

| Exact absolute path | Allocated bytes | Task | State | Branch | Published | Terminal age | Verdict | Reason |
| --- | ---: | --- | --- | --- | --- | --- | --- | --- |
| `/work/worktrees/task-016fd320` | 261566464 | 016fd320 | Dispatched | feat/card-task-016fd320 | Not checked at inventory; own report published afterward | Nonterminal | KEEP | Own active tree; protected temp volume |
| `/work/worktrees/task-54721a0f` | 261599232 | 54721a0f | Dispatched; running Review | feat/card-task-54721a0f | Yes; live origin tip equals HEAD `18e48c9cd6d9abaec0593779d508ef24512307a8` | Nonterminal | KEEP | Live task; protected temp volume |
| `/work/worktrees/task-9cbb56ed` | 261570560 | 9cbb56ed | Succeeded | feat/card-task-9cbb56ed | Yes; live origin tip equals HEAD `2f3d710bc407be6d4b18e4227f6f7b610f28ae83` | About 12 minutes; completed 13:48:52 UTC | KEEP | Younger than 12 hours; protected temp volume |
| `/work/repos/antiphon` | 87703552 | None | Repository registration | master | Not audited; no deletion candidate | Not applicable | KEEP | Shared repository; protected temp volume |
| Standing server2 `/work/worktrees`, `/work/repos`, `/work/retained` | Unknown | Unknown | Unknown | Unknown | Unknown | Unknown | KEEP | Target volume inaccessible; uncertainty requires preservation |

**Printed deletion plan: empty. There are no DELETE rows and no approved removal paths.** All visible paths remain. No branch deletion or `git worktree prune` was performed. `/work/retained`, `/work/workspace-slots`, and `/work/worktrees-retiring` do not exist in this temp namespace; that establishes nothing about standing server2. Slots and ledgers elsewhere were not accessed or modified.

## Sanctioned retention and access results

Read `docs/docker-stack.md` (manual main cleanup, workspace slots, runner retention), `docs/session-runtime-invariants.md` (runtime/custody safety), `docs/orchestration-loop.md` (retirement ownership), `docs/ops-http.md`, and the operator-token custody section of `docs/agent-credentials.md`.

The documented preferred mechanism is the server2 retention tick. It guards terminal age (default `MinTerminalHours=12`), publication, cleanliness, runner ownership, slots, land/sync debt, preserved results, live processes and recent use. Its preview census is paged; one-shot execute requires the operator header and must be followed until `finishedAt`. `/work/retained` slot evidence has a documented seven-day lifetime, but its protection and inaccessible location preclude any manual action here.

Attempted only the non-destructive preview:

`POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview`

The configured API returned **403 `caller_scope`**, detail: `A child task token cannot preview runner retention.` No run ID or census was issued. No execute call was made. The operator-token override is not configured and `/state/operator-token` is absent; no secret was displayed or copied. The actual retention configuration, advertised `workspaceRetentionV1`, and standing runner census could not be verified, so no manual fallback was admitted.

Docker access is to this container's **nested test daemon**: `docker ps` has no containers and `docker system df` reports one 4.446 MB image, zero containers, zero volumes and zero build cache. These are not host Docker totals. The documented read-only `ssh -o BatchMode=yes -o ConnectTimeout=8 mc@server2 ...` attempt stopped at **Host key verification failed**; trust checks were not bypassed. Host Docker space, standing runner container identity/open handles, and target worktree contents remain unknown.

Read-only API status succeeded for both runner IDs; both were available and accepting work on build `9b98828eb34eb552ce62bcde5a64dd045a872a88`. This is API liveness evidence only, not filesystem ownership or deletion authority.

## Disk accounting

`df -h /work`: 705 GiB total, 479 GiB used, 198 GiB available, 71% used. The mount is backed by host device `/dev/mapper/ubuntu--vg-ubuntu--lv`; observations therefore show backing-filesystem free space, not exclusive task usage.

| Observation | Available KiB (`df -Pk /work`) | Available bytes |
| --- | ---: | ---: |
| Before inventory | 206669224 | 211629285376 |
| After read-only checks, 14:00:44 UTC | 206667668 | 211627692032 |

Available space decreased by 1593344 bytes during concurrent activity. No deletion occurred; **0 bytes freed by this task**. Host Docker-level free/reclaimable space is unavailable from this runner; nested-daemon counts above must not be treated as host results.

## Required caller action

Place the cleanup worker on the standing `server2` runner with its `/work` volume, or run the documented retention preview from the caller's existing authorized operator context. Read all census pages, verify live `minTerminalHours` and every candidate's task/publication/cleanliness facts, write and print the exact DELETE inventory before executing, and follow the execute run to completion. Preserve the temporary runner and live qualification run throughout. The existing child token and nested Docker daemon cannot carry out that action. No builds or tests were run.
