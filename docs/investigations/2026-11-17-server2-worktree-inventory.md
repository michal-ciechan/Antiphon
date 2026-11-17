# server2 worktree and mirror inventory (2026-11-17)

Task 3283ed4f, Investigate, READ-ONLY. Operator ask 2026-11-17: "Can we clean up old worktrees on server2 and desktop", step 1.
Nothing was deleted, moved, pruned, restarted or written outside this note. Master read: `29ba0c5002e642c4f4f91805be7e17660a40906d`
(`git fetch origin master` at 19:03Z; equal to this branch's base). Observations 19:03Z-19:35Z UTC from inside the runner
container (hostname `d0d4c9a390d9`, runner id `server2`, `/work` = host volume `antiphon-runner_work`).

## Outcome

- 106 entries under `/work/worktrees`, **164.3 GiB** together (one `du -sx`, 168,234 MiB). All 106 are registered worktrees of
  `/work/repos/antiphon` (`git worktree list --porcelain`: 186 registered in total = main checkout + these 106 + 1 under `/work/review-evidence` + 78 under `/tmp`; 0 `prunable`, 0 `locked`).
- The bulk is fresh work, not old residue: **56 mirrors (91.8 GiB) belong to tasks that ended less than 24 h ago** (53 under 12 h).
  The largest single tree is `task-fc32e512` (17.0 GiB, CARD-1960 Review, ended 6.5 h ago).
- Proven old and safe (class A): **23 mirrors, 27.5 GiB**. 18 of them (18.1 GiB) carry a Held desktop sync debt, which the sanctioned
  retention job treats as a keep reason. Without them: **5 mirrors, 9.4 GiB**.
- `/tmp` (volume `antiphon-runner_runner-tmp`, same host LV) is **190.3 GiB, larger than `/work/worktrees`**: 148,096 top-level entries,
  48,209 top-level directories older than 24 h, 78 registered scratch worktrees totalling 102.4 GiB. The temp sweep is shipped OFF.
  This is the larger reclaim and is outside the sanctioned worktree tool.

| Class | Count | GiB | Meaning |
|---|---:|---:|---|
| A SAFE-TO-DELETE | 23 | 27.48 | terminal task, branch on live origin and equal to HEAD, clean, no holder, ended > 24 h |
| of which A1 (no sync debt) | 5 | 9.42 | the sanctioned tool's own rules should already take these; why it has not is UNOBSERVED (below) |
| of which A2 (desktop sync debt Held) | 18 | 18.13 | content-safe; the retention job keeps them (`no open sync debt` rule) |
| B SAFE-AFTER-PUSH | 5 | 3.37 | HEAD or commits not on the task's live origin branch |
| C KEEP | 66 | 110.08 | 56 inside the window (91.79), 7 live slots (17.58), 3 dirty with a real doc edit (0.70) |
| D ORPHAN (auxiliary, no branch) | 12 | 25.00 | detached helper trees named `task-<id>-<suffix>`; HEAD in a live origin ref; clean |
| Total | 106 | 165.93 | sum of per-tree `du` (hard-link sharing makes it exceed the single-pass 164.3 GiB) |

No task among the 91 task ids is Queued, Dispatched, Working or Blocked; all are Succeeded (85), Failed (2) or Canceled (4), and every
one reads `Land: Not requested` and `Publication: Unconfirmed; cleanup: NotStarted` (`scripts/delegate.ps1 -Status`). So no mirror is
held for a pending land.

## Method (all read-only)

| Fact | Command / source |
|---|---|
| Size | `du -sx --block-size=1M <path>` per tree (standalone), plus one pass over `/work/*` |
| Created / modified | `stat -c %w` (birth) and `%y` of the tree; mtime of `<gitdir>/HEAD` |
| Task facts | `pwsh -NoProfile -File scripts/delegate.ps1 -Status <8hex>` for all 91 ids (3 needed a 150 s retry) |
| Pushed | one `git ls-remote origin 'refs/heads/*'` at 19:03Z (6,778 refs) compared to `git -C <path> rev-parse HEAD`; detached trees checked with `merge-base --is-ancestor` against the live ref SHAs |
| Dirty | `git -C <path> status --porcelain` (ignored files excluded) |
| Holders | every `/proc/<pid>/cwd` and `/proc/<pid>/fd/*` in the container (112 processes) |
| Registration | `git -C /work/repos/antiphon worktree list --porcelain` |
| Slots | `/work/workspace-slots/ledger.json` (`slots`, `quarantinedDirectories`) |

Two side effects, both metadata only: the first `git status` pass ran without `GIT_OPTIONAL_LOCKS=0`, so git may have refreshed the
stat cache in some worktree `index` files (no content, ref or tree change; later passes set it). `git fetch origin master` updated the
shared `refs/remotes/origin/master` of `/work/repos/antiphon`.

## Sanctioned mechanism

`docs/docker-stack.md` "Runner worktree retention (CARD-1677)" is the sanctioned owner for `/work/worktrees/task-<8hex>`:
server job `antiphon:runner-worktree-retention`, every 30 min, `Enabled=true`, `Execute=true`, `Runners=["server2"]`,
`MinTerminalHours=12` shipped (CARD-1794 observed a 24 h override in force on 2026-11-08), at most 25 removals per tick.
The runner moves a tree to `/work/worktrees-retiring/<leaf>.<request8>` then runs `git worktree remove` without `--force`; it never
runs `rm`, prune or fetch. The doc names the operator one-shot as "the sanctioned replacement for hand deletion":

```
POST /api/agent-tasks/worktree-residue/runner-retention/server2/execute   (header X-Antiphon-Operator-Token; 202 { runnerId, runId })
GET  /api/agent-tasks/worktree-residue/runs/{runId}?page=n                (follow every page until finishedAt)
POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview   (DryRun census; operator caller, a child token is refused)
```

It is running: the runner log `/state/runner-logs/session-runner-20261117.log` has 44 `owner="WorkspaceRetain"` lock lines today
(10 in the 19:00 tick, 19:00:08-19:01:19) and 68 over 2026-11-10..16. `/work/worktrees-retiring` is empty. The per-tree verdicts
(`would_remove` / kept reason) are stored only in the residue run rows; there is no list route and the runner log does not carry
them, so **the kept reason for each A tree is UNOBSERVED**. Resolve: an operator `preview` call (or the latest scheduled run id from
the server log `antiphon.runner-worktree-retention.tick` line) and read its `RunnerMirror` rows.

Not covered by the tool: names other than `task-<8hex>` (they appear as `RunnerInventory` `identity_unknown` rows and "are never
acted on"), slots, `/work/repos`, `/work/cache`, Docker volumes. `/tmp` has its own CARD-1677 S2 temp sweep, shipped
`TempSweep:Enabled=false`, `Execute=false`, registered families only (`claude-scratch` has its own false flag).
`docs/host-cleanup.md` (CARD-0826) is explicitly "not operational ... Do not activate cleanup".

## Class A: safe to delete (23, 27.48 GiB)

Every row: terminal task, no land requested or pending, local HEAD equal to the live `refs/heads/feat/card-task-<id>` SHA,
`status --porcelain` empty, no process cwd/fd under the path, not locked, ended more than 24 h ago (110-247 h).
In the 23 trees 17 still hold `.antiphon/task-<id>.md` (ignored); CARD-1794 keeps such a tree `report_unpreserved` only while the task
row's `Result` is empty. `-Status` printed the report body for e7d7d78b; `Result` for the others is UNOBSERVED.

### A1: no sync debt (5, 9.42 GiB)

| Path | GiB | Created (UTC) | Task ended (UTC) | Age h | State | Card / role | HEAD | Pushed vs live origin | Dirty | Holder | Desktop sync debt |
|---|---:|---|---|---:|---|---|---|---|---:|---|---|
| `/work/worktrees/task-2b953d03` | 2.34 | 2026-11-07 09:58 | 2026-11-09T01:14 | 210.3 | Canceled | - Code | `1705a3ee8c` | EQUAL | 0 | no | - |
| `/work/worktrees/task-0ddd898d` | 2.28 | 2026-11-10 16:35 | 2026-11-12T12:24 | 127.1 | Failed | CARD-1820 Code | `cb931f0890` | EQUAL | 0 | no | - |
| `/work/worktrees/task-f4320f2f` | 2.27 | 2026-11-10 03:43 | 2026-11-12T12:24 | 127.1 | Failed | CARD-1820 Code | `682375f5d5` | EQUAL | 0 | no | - |
| `/work/worktrees/task-741fb04b` | 2.27 | 2026-11-10 09:02 | 2026-11-10T09:34 | 177.9 | Succeeded | - Debug | `ebd2b2bdad` | EQUAL | 0 | no | - |
| `/work/worktrees/task-91085723` | 0.26 | 2026-11-13 03:34 | 2026-11-13T04:09 | 111.3 | Succeeded | CARD-1836 Code | `9ec9b850cb` | EQUAL | 0 | no | - |
### A2: desktop sync debt Held (18, 18.13 GiB)

`-Status` reads e.g. `Desktop sync debt: Held source=<head> confirmed= reason=settlement_sync_source_changed attempts=1` with
`Runner sync: Pending runner_sync_lease_busy`. The debt is the desktop checkout's; the `source` SHA equals the mirror HEAD and the live
origin branch tip, so deleting the server2 mirror loses no commit. The retention job will keep these until the debt clears.

| Path | GiB | Created (UTC) | Task ended (UTC) | Age h | State | Card / role | HEAD | Pushed vs live origin | Dirty | Holder | Desktop sync debt |
|---|---:|---|---|---:|---|---|---|---|---:|---|---|
| `/work/worktrees/task-ec9dfd70` | 3.72 | 2026-11-08 07:28 | 2026-11-08T09:24 | 226.1 | Succeeded | - Merge | `d18822e623` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-7555284f` | 3.54 | 2026-11-08 15:32 | 2026-11-08T16:19 | 219.2 | Succeeded | CARD-1783 Review | `eaf879a9f7` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-4f5166ec` | 3.20 | 2026-11-07 16:01 | 2026-11-07T16:36 | 242.9 | Succeeded | CARD-1728 Merge | `b6052173f4` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-c6ca6929` | 2.27 | 2026-11-08 19:20 | 2026-11-08T19:20 | 216.2 | Succeeded | CARD-1788 Review | `ea50afd907` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-f87fdda9` | 2.26 | 2026-11-08 07:02 | 2026-11-08T07:26 | 228.1 | Succeeded | - Merge | `3364197bf1` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-934cdb25` | 0.26 | 2026-11-08 19:39 | 2026-11-08T19:39 | 215.8 | Succeeded | CARD-1764 Review | `f7001bbaa8` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-4292c713` | 0.26 | 2026-11-08 12:38 | 2026-11-08T12:38 | 222.9 | Succeeded | CARD-1782 Review | `13ed70c73f` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-a2845b6c` | 0.25 | 2026-11-08 13:04 | 2026-11-08T13:04 | 222.4 | Succeeded | CARD-1778 Review | `de40bdb3a0` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-5b1ea772` | 0.25 | 2026-11-08 06:54 | 2026-11-08T07:25 | 228.1 | Succeeded | CARD-1776 Review | `ed63b5861d` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-c7a30141` | 0.25 | 2026-11-07 19:45 | 2026-11-07T20:25 | 239.1 | Succeeded | - Review | `436e0d3592` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-bc7a9fdd` | 0.25 | 2026-11-07 14:49 | 2026-11-07T15:18 | 244.2 | Succeeded | CARD-1756 Review | `6fb21150eb` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-b0e54eca` | 0.23 | 2026-11-09 10:44 | 2026-11-09T11:19 | 200.2 | Succeeded | CARD-1809 Merge | `214114346f` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-2abcd1c0` | 0.23 | 2026-11-08 16:01 | 2026-11-08T16:30 | 219.0 | Succeeded | CARD-1780 Review | `9c096f32fb` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-6155693e` | 0.23 | 2026-11-08 21:12 | 2026-11-08T21:24 | 214.1 | Succeeded | - Merge | `d608b09ba7` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-94b17435` | 0.23 | 2026-11-08 22:55 | 2026-11-08T23:21 | 212.1 | Succeeded | CARD-1773 Debug | `f1a220891e` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-bee64303` | 0.23 | 2026-11-08 17:47 | 2026-11-08T18:23 | 217.1 | Succeeded | CARD-1783 Merge | `57bbfc99d1` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-da90742c` | 0.23 | 2026-11-08 16:08 | 2026-11-08T16:23 | 219.1 | Succeeded | - Debug | `a4341ba538` | EQUAL | 0 | no | Held |
| `/work/worktrees/task-e7d7d78b` | 0.23 | 2026-11-07 12:10 | 2026-11-07T12:28 | 247.0 | Succeeded | CARD-1746 Investigate | `a06d96a501` | EQUAL | 0 | no | Held |
## Class B: safe after push (5, 3.37 GiB)

| Path | Unpushed commits (live origin check) | Proposed salvage |
|---|---|---|
| `/work/worktrees/c0824-compose-out` | 35 commits on no origin ref, tip `353d5cecb041be9c549d99deb5387f4a6849b1ce`; 34 subjects exist on origin/master (rebased lands); one does not: `5623b9d909 test(card-0824): regenerate combined-tree impact index after the roots repair` | `git -C /work/worktrees/c0824-compose-out push origin HEAD:refs/heads/salvage/c0824-compose-out` |
| `/work/worktrees/c1886-s1-integrated` | 11 commits, tip `827ab0ee91857ab40996128ac763047094436e81`; 10 subjects on master; not: `827ab0ee91 chore: regenerate derived impact index after CARD-1886 S1` | `git -C /work/worktrees/c1886-s1-integrated push origin HEAD:refs/heads/salvage/c1886-s1-integrated` |
| `/work/worktrees/task-63cdf1df-land` | 2 commits, tip `48c8b0685eb0584e0c0cc7571d028098f4d5da55` (task 63cdf1df, CARD-1799 Merge, Canceled); both subjects on master | `git -C /work/worktrees/task-63cdf1df-land push origin HEAD:refs/heads/salvage/task-63cdf1df-land` |
| `/work/worktrees/task-34125850` | HEAD `72a8014a92188a8c587c08008b820c51d179b4d3` = live branch tip `1470340559` + 2 rc-cut docs commits; HEAD equals live `refs/heads/salvage/stale-merge-34125850-reports` | none needed: already salvaged; sanctioned tool keeps it (HEAD not on its branch) |
| `/work/worktrees/task-9d003ac4` | local branch `merge184` not on origin; 0 commits outside origin refs; HEAD `2aef004ce5 docs: Merge batch 184 report` | none needed; operator decision (branch is not the task branch, so the tool keeps it) |

The B rows are clean (0 porcelain lines) and have no holder.

| Path | GiB | Created (UTC) | Task ended (UTC) | Age h | State | Card / role | HEAD | Pushed vs live origin | Dirty | Holder | Desktop sync debt |
|---|---:|---|---|---:|---|---|---|---|---:|---|---|
| `/work/worktrees/c0824-compose-out` | 2.29 | 2026-11-14 21:58 | 2026-11-14T21:58 | 69.5 | - | -  | `353d5cecb0` | NO_REMOTE_BRANCH +35 | 0 | no | - |
| `/work/worktrees/task-63cdf1df-land` | 0.32 | 2026-11-09 03:16 | 2026-11-09T03:17 | 208.2 | Canceled | CARD-1799 Merge | `48c8b0685e` | NO_REMOTE_BRANCH +2 | 0 | no | - |
| `/work/worktrees/c1886-s1-integrated` | 0.30 | 2026-11-13 23:58 | 2026-11-14T00:01 | 91.5 | - | -  | `827ab0ee91` | NO_REMOTE_BRANCH +11 | 0 | no | - |
| `/work/worktrees/task-9d003ac4` | 0.24 | 2026-11-12 02:31 | 2026-11-12T03:29 | 136.0 | Succeeded | - Merge | `2aef004ce5` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-34125850` | 0.22 | 2026-11-04 08:45 | 2026-11-05T19:19 | 288.2 | Canceled | - Merge | `72a8014a92` | DIVERGED +2 | 0 | no | - |
## Class C: keep (66, 110.08 GiB)

### Live CARD-0824 slots (7, 17.58 GiB)

Ledger (`/work/workspace-slots/ledger.json`, 7 slots): `ws-01-d4` Leased 4f199f91, `ws-02-d2` Leased d2eef712, `ws-03-d2` Leased
3283ed4f (this task), `ws-04-d2` Free at the 19:02Z read and leased by task 7f4a215b at 19:03:31Z (claude cwd holder), `ws-05`,
`ws-06`, `ws-07` Free. Live holders: ws-01-d4 (5 processes), ws-02-d2 (grok), ws-03-d2, ws-04-d2. No retired `ws-NN-dK` directory
is left: the 6 ledger quarantine entries (`/work/worktrees/ws-01`, `ws-04`, `ws-03`, `ws-01-d2`, `ws-01-d3`, `ws-02`) all read `isOpen:false`,
`outcome: removed: operator 2026-11-17 slot cleanup`, and none of those paths exists. `/work/workspace-slots` holds only the ledger.

| Path | GiB | Created (UTC) | Task ended (UTC) | Age h | State | Card / role | HEAD | Pushed vs live origin | Dirty | Holder | Desktop sync debt |
|---|---:|---|---|---:|---|---|---|---|---:|---|---|
| `/work/worktrees/ws-04-d2` | 4.05 | 2026-11-16 19:52 | 2026-11-17T19:03 | 0.4 | - | -  | `2e3d1e5da9` | DIVERGED +2 | 0 | yes | - |
| `/work/worktrees/ws-06` | 2.40 | 2026-11-16 23:39 | 2026-11-17T01:09 | 18.3 | - | -  | `321f98ec99` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/ws-01-d4` | 2.32 | 2026-11-17 01:05 | 2026-11-17T18:55 | 0.6 | - | -  | `728b74ae3f` | DIVERGED +1 | 0 | yes | - |
| `/work/worktrees/ws-03-d2` | 2.30 | 2026-11-16 21:49 | 2026-11-17T19:02 | 0.5 | - | -  | `29ba0c5002` | EQUAL | 0 | yes | - |
| `/work/worktrees/ws-05` | 2.30 | 2026-11-16 18:04 | 2026-11-17T01:13 | 18.3 | - | -  | `321f98ec99` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/ws-07` | 2.30 | 2026-11-17 00:00 | 2026-11-17T01:16 | 18.2 | - | -  | `321f98ec99` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/ws-02-d2` | 1.90 | 2026-11-17 02:28 | 2026-11-17T19:01 | 0.5 | - | -  | `9fe86f00b3` | DIVERGED +1 | 0 | yes | - |
### Dirty with a real change (3, 0.70 GiB)

| Path | Porcelain |
|---|---|
| `/work/worktrees/task-d989d9d8` | `?? docs/investigations/2026-11-10-run-ae84478d-profile-16-HostLoadNativeOverhead-red-windows-failure.md` |
| `/work/worktrees/task-ed0f7942` | ` M docs/investigations/2026-11-08-server2-work-reclaim-log.md` |
| `/work/worktrees/task-f23857e7` | ` M docs/investigations/2026-11-08-why-missed-server-repository-mirror-windows.md` |

Each is an uncommitted investigation note. Keep until someone decides whether to commit it to a salvage ref.

| Path | GiB | Created (UTC) | Task ended (UTC) | Age h | State | Card / role | HEAD | Pushed vs live origin | Dirty | Holder | Desktop sync debt |
|---|---:|---|---|---:|---|---|---|---|---:|---|---|
| `/work/worktrees/task-d989d9d8` | 0.24 | 2026-11-12 09:51 | 2026-11-12T09:57 | 129.5 | Canceled | - Debug | `56e96eabf6` | EQUAL | 1 | no | - |
| `/work/worktrees/task-ed0f7942` | 0.23 | 2026-11-08 15:05 | 2026-11-08T15:25 | 220.1 | Succeeded | - Deploy | `a41e77962f` | EQUAL | 1 | no | Held |
| `/work/worktrees/task-f23857e7` | 0.23 | 2026-11-08 21:22 | 2026-11-08T21:36 | 213.9 | Succeeded | - Debug | `50c36bf27b` | EQUAL | 1 | no | - |
### Inside the retention window (56, 91.79 GiB)

Tasks that ended under 24 h before 19:30Z (53 under 12 h, 3 at 12-24 h). All terminal. If retention works as documented, it
picks them up as they age past `MinTerminalHours`. Re-inventory after 24 h before touching any of them.

| Path | GiB | Created (UTC) | Task ended (UTC) | Age h | State | Card / role | HEAD | Pushed vs live origin | Dirty | Holder | Desktop sync debt |
|---|---:|---|---|---:|---|---|---|---|---:|---|---|
| `/work/worktrees/task-fc32e512` | 16.98 | 2026-11-17 09:51 | 2026-11-17T12:59 | 6.5 | Succeeded | CARD-1960 Review | `e216751fc7` | EQUAL | 0 | no | - |
| `/work/worktrees/task-277baf57` | 4.54 | 2026-11-17 14:54 | 2026-11-17T17:02 | 2.5 | Succeeded | CARD-1969 Code | `c5a5367c94` | EQUAL | 0 | no | - |
| `/work/worktrees/task-d1556676` | 3.07 | 2026-11-17 08:59 | 2026-11-17T10:51 | 8.6 | Succeeded | CARD-1958 Merge | `57563753c7` | EQUAL | 0 | no | - |
| `/work/worktrees/task-aaabade9` | 3.02 | 2026-11-17 03:39 | 2026-11-17T09:46 | 9.7 | Succeeded | CARD-1960 Code | `1e82c08aeb` | EQUAL | 0 | no | - |
| `/work/worktrees/task-a3b6865d` | 2.79 | 2026-11-17 15:04 | 2026-11-17T16:16 | 3.2 | Succeeded | CARD-1960 Merge | `7d67cf9318` | EQUAL | 0 | no | - |
| `/work/worktrees/task-3e32f1e6` | 2.45 | 2026-11-17 17:04 | 2026-11-17T17:31 | 2.0 | Succeeded | CARD-1969 Review | `3015db7a60` | EQUAL | 0 | no | - |
| `/work/worktrees/task-50b780b8` | 2.43 | 2026-11-17 16:18 | 2026-11-17T17:20 | 2.2 | Succeeded | CARD-1961 Merge | `38c64361b9` | EQUAL | 0 | no | - |
| `/work/worktrees/task-52dac4e7` | 2.36 | 2026-11-17 11:43 | 2026-11-17T13:19 | 6.2 | Succeeded | CARD-1969 Code | `5ef455952f` | EQUAL | 0 | no | - |
| `/work/worktrees/task-cad08e40` | 2.35 | 2026-11-17 06:39 | 2026-11-17T08:27 | 11.0 | Succeeded | CARD-1964 Code | `4cfec30c83` | EQUAL | 0 | no | - |
| `/work/worktrees/task-6ea96bcb` | 2.34 | 2026-11-17 08:56 | 2026-11-17T10:42 | 8.8 | Succeeded | CARD-1888 Code | `cd222da3c6` | EQUAL | 0 | no | - |
| `/work/worktrees/task-7f6ac898` | 2.34 | 2026-11-17 03:39 | 2026-11-17T08:56 | 10.6 | Succeeded | CARD-1957 Merge | `7901e9ba53` | EQUAL | 0 | no | - |
| `/work/worktrees/task-23a610fb` | 2.33 | 2026-11-17 08:56 | 2026-11-17T10:53 | 8.6 | Succeeded | CARD-1890 Code | `80cd228dcb` | EQUAL | 0 | no | - |
| `/work/worktrees/task-a0e750a9` | 2.33 | 2026-11-17 13:21 | 2026-11-17T14:08 | 5.4 | Succeeded | CARD-1969 Review | `8518aa31d6` | EQUAL | 0 | no | - |
| `/work/worktrees/task-0ca7a8e1` | 2.33 | 2026-11-17 14:58 | 2026-11-17T15:41 | 3.8 | Succeeded | CARD-1883 Review | `d53c176b10` | EQUAL | 0 | no | - |
| `/work/worktrees/task-233b0e13` | 2.33 | 2026-11-17 13:01 | 2026-11-17T14:32 | 5.0 | Succeeded | CARD-1960 Debug | `06ab6d3cc1` | EQUAL | 0 | no | - |
| `/work/worktrees/task-69bc3cb2` | 2.33 | 2026-11-17 10:56 | 2026-11-17T11:31 | 8.0 | Succeeded | CARD-1890 Review | `80b5494f58` | EQUAL | 0 | no | - |
| `/work/worktrees/task-82a60f8b` | 2.33 | 2026-11-17 17:33 | 2026-11-17T18:23 | 1.1 | Succeeded | CARD-1969 Merge | `29ba0c5002` | EQUAL | 0 | no | - |
| `/work/worktrees/task-5e090a01` | 2.32 | 2026-11-17 06:40 | 2026-11-17T08:50 | 10.7 | Succeeded | - Debug | `b103bdf8b9` | EQUAL | 0 | no | - |
| `/work/worktrees/task-2cd01c86` | 2.32 | 2026-11-17 13:50 | 2026-11-17T14:50 | 4.7 | Succeeded | CARD-1890 Merge | `d7e4e7deca` | EQUAL | 0 | no | - |
| `/work/worktrees/task-926788ad` | 2.32 | 2026-11-17 10:55 | 2026-11-17T13:33 | 5.9 | Succeeded | CARD-1964 Merge | `1f303bed04` | EQUAL | 0 | no | - |
| `/work/worktrees/task-fde20c05` | 2.32 | 2026-11-17 14:51 | 2026-11-17T15:33 | 3.9 | Succeeded | - Debug | `aff28fc2af` | EQUAL | 0 | no | - |
| `/work/worktrees/task-cd7aa04a` | 2.32 | 2026-11-17 12:48 | 2026-11-17T13:48 | 5.7 | Succeeded | - Debug | `45680c87e0` | EQUAL | 0 | no | - |
| `/work/worktrees/task-a89a352d` | 2.32 | 2026-11-17 06:38 | 2026-11-17T07:57 | 11.5 | Succeeded | CARD-1820 Review | `e6481c8869` | EQUAL | 0 | no | - |
| `/work/worktrees/task-d1a53b65` | 2.32 | 2026-11-17 10:45 | 2026-11-17T11:35 | 7.9 | Succeeded | CARD-1888 Review | `7ff50fb5b3` | EQUAL | 0 | no | - |
| `/work/worktrees/task-fcd04446` | 2.32 | 2026-11-17 11:37 | 2026-11-17T13:39 | 5.8 | Succeeded | CARD-1888 Code | `9920f248af` | EQUAL | 0 | no | - |
| `/work/worktrees/task-ca3fe53e` | 2.31 | 2026-11-17 13:41 | 2026-11-17T14:53 | 4.6 | Succeeded | CARD-1888 Review | `b132bc2324` | EQUAL | 0 | no | - |
| `/work/worktrees/task-292c3299` | 2.30 | 2026-11-17 06:50 | 2026-11-17T07:08 | 12.4 | Succeeded | - Docs | `64d1bf5e68` | EQUAL | 0 | no | - |
| `/work/worktrees/task-de583ac6` | 2.30 | 2026-11-17 14:34 | 2026-11-17T15:02 | 4.5 | Succeeded | CARD-1960 Review | `9272ab23d7` | EQUAL | 0 | no | - |
| `/work/worktrees/task-fae5c49d` | 0.44 | 2026-11-17 14:10 | 2026-11-17T15:26 | 4.1 | Succeeded | CARD-1961 Code | `d262e8e2c0` | EQUAL | 0 | no | - |
| `/work/worktrees/task-8b8bd64c` | 0.31 | 2026-11-17 13:07 | 2026-11-17T14:56 | 4.6 | Succeeded | CARD-1883 Code | `22d410e1c8` | EQUAL | 0 | no | - |
| `/work/worktrees/task-0729df64` | 0.30 | 2026-11-17 11:23 | 2026-11-17T12:31 | 7.0 | Succeeded | CARD-1883 Code | `f44a5775ce` | EQUAL | 0 | no | - |
| `/work/worktrees/task-fb6ac229` | 0.29 | 2026-11-17 08:29 | 2026-11-17T09:07 | 10.4 | Succeeded | CARD-1964 Review | `e0b3fd7fb8` | EQUAL | 0 | no | - |
| `/work/worktrees/task-3802ef67` | 0.29 | 2026-11-17 08:52 | 2026-11-17T09:18 | 10.2 | Succeeded | - Review | `bc261d3700` | EQUAL | 0 | no | - |
| `/work/worktrees/task-ca5bf90c` | 0.29 | 2026-11-17 15:28 | 2026-11-17T15:51 | 3.6 | Succeeded | CARD-1961 Review | `70d34283eb` | EQUAL | 0 | no | - |
| `/work/worktrees/task-636394f6` | 0.29 | 2026-11-17 12:33 | 2026-11-17T13:06 | 6.4 | Succeeded | CARD-1883 Review | `45d84cf7e6` | EQUAL | 0 | no | - |
| `/work/worktrees/task-10145984` | 0.27 | 2026-11-17 08:20 | 2026-11-17T08:28 | 11.0 | Succeeded | CARD-1920 Review | `be53f2b45d` | EQUAL | 0 | no | - |
| `/work/worktrees/task-2048f2d0` | 0.27 | 2026-11-17 10:25 | 2026-11-17T10:43 | 8.8 | Succeeded | CARD-1969 Plan | `241f894639` | EQUAL | 0 | no | - |
| `/work/worktrees/task-32a9a06f` | 0.27 | 2026-11-17 07:39 | 2026-11-17T08:05 | 11.4 | Succeeded | CARD-1890 Review | `8e28b62b98` | EQUAL | 0 | no | - |
| `/work/worktrees/task-43193a26` | 0.27 | 2026-11-17 08:08 | 2026-11-17T08:31 | 11.0 | Succeeded | CARD-1889 Review | `f8256e071e` | EQUAL | 0 | no | - |
| `/work/worktrees/task-4f978baa` | 0.27 | 2026-11-17 07:57 | 2026-11-17T08:06 | 11.4 | Succeeded | CARD-1889 Plan | `83686e7822` | EQUAL | 0 | no | - |
| `/work/worktrees/task-52484483` | 0.27 | 2026-11-17 10:56 | 2026-11-17T11:09 | 8.3 | Succeeded | CARD-1969 Plan | `7622b7a702` | EQUAL | 0 | no | - |
| `/work/worktrees/task-52aeb04f` | 0.27 | 2026-11-17 07:46 | 2026-11-17T07:55 | 11.6 | Succeeded | CARD-1889 Review | `ad5480101f` | EQUAL | 0 | no | - |
| `/work/worktrees/task-54b64112` | 0.27 | 2026-11-17 07:00 | 2026-11-17T07:13 | 12.3 | Succeeded | CARD-1888 Review | `7695efa131` | EQUAL | 0 | no | - |
| `/work/worktrees/task-5db7c600` | 0.27 | 2026-11-17 07:27 | 2026-11-17T07:37 | 11.9 | Succeeded | CARD-1890 Plan | `6c8f1b4f1f` | EQUAL | 0 | no | - |
| `/work/worktrees/task-8626ac28` | 0.27 | 2026-11-17 09:51 | 2026-11-17T10:04 | 9.4 | Succeeded | CARD-1969 Plan | `804edf5f80` | EQUAL | 0 | no | - |
| `/work/worktrees/task-9dbbe5bf` | 0.27 | 2026-11-17 10:45 | 2026-11-17T10:54 | 8.6 | Succeeded | CARD-1969 Review | `c9d2bcded6` | EQUAL | 0 | no | - |
| `/work/worktrees/task-b66f32eb` | 0.27 | 2026-11-17 11:20 | 2026-11-17T11:40 | 7.8 | Succeeded | CARD-1969 Review | `bdd6503ff2` | EQUAL | 0 | no | - |
| `/work/worktrees/task-ce86e086` | 0.27 | 2026-11-17 11:12 | 2026-11-17T11:18 | 8.2 | Succeeded | CARD-1969 Review | `8a6253d0db` | EQUAL | 0 | no | - |
| `/work/worktrees/task-ef70b56b` | 0.27 | 2026-11-17 07:28 | 2026-11-17T07:44 | 11.8 | Succeeded | CARD-1889 Plan | `95e6a1b5ce` | EQUAL | 0 | no | - |
| `/work/worktrees/task-f4a56d33` | 0.27 | 2026-11-17 07:58 | 2026-11-17T08:18 | 11.2 | Succeeded | CARD-1920 Plan | `dc12d620a1` | EQUAL | 0 | no | - |
| `/work/worktrees/task-ba15e8f5` | 0.25 | 2026-11-17 14:28 | 2026-11-17T14:39 | 4.8 | Succeeded | - Investigate | `2d1cb9e801` | EQUAL | 0 | no | - |
| `/work/worktrees/task-be9d04c4` | 0.25 | 2026-11-17 17:35 | 2026-11-17T17:48 | 1.7 | Succeeded | - Investigate | `3243fc7c7c` | EQUAL | 0 | no | - |
| `/work/worktrees/task-324876aa` | 0.25 | 2026-11-17 07:11 | 2026-11-17T07:15 | 12.2 | Succeeded | - Review | `2fc588c165` | EQUAL | 0 | no | - |
| `/work/worktrees/task-39f3b245` | 0.25 | 2026-11-17 10:06 | 2026-11-17T10:31 | 9.0 | Succeeded | CARD-1969 Review | `57d7a637e5` | EQUAL | 0 | no | - |
| `/work/worktrees/task-a84df3ad` | 0.25 | 2026-11-17 09:10 | 2026-11-17T09:22 | 10.1 | Succeeded | - Investigate | `827aad83cd` | EQUAL | 0 | no | - |
| `/work/worktrees/task-c747c0c4` | 0.25 | 2026-11-17 09:37 | 2026-11-17T09:46 | 9.7 | Succeeded | - Investigate | `c684172fc9` | EQUAL | 0 | no | - |
## Class D: orphan auxiliary trees (12, 25.00 GiB)

Detached-HEAD worktrees a task made beside its own mirror (`-base`, `-master`, `-s1tip`, `-checks`, `-repair`, `-measure`,
`-coverage2`, `-s2`). There is no branch; each HEAD is contained in a live origin ref (origin/master or the named task branch, checked
against the 19:03Z `ls-remote` SHAs); all are clean with no holder. The task columns below are the prefix task's. Only
`task-3e32f1e6-s2` has its parent mirror still present. The sanctioned tool never acts on these names. They are content-safe, but
removing them needs an operator decision because no sanctioned tool covers them; they are not in the class A list.
`task-3e32f1e6-s2` (CARD-1969 Review, 2.0 h) is inside the window.

| Path | GiB | Created (UTC) | Task ended (UTC) | Age h | State | Card / role | HEAD | Pushed vs live origin | Dirty | Holder | Desktop sync debt |
|---|---:|---|---|---:|---|---|---|---|---:|---|---|
| `/work/worktrees/task-8e452527-checks` | 3.53 | 2026-11-14 08:22 | 2026-11-14T13:00 | 78.5 | Succeeded | CARD-1915 Code | `1af25a62f2` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-71aaddba-measure` | 2.69 | 2026-11-14 03:15 | 2026-11-14T05:33 | 85.9 | Succeeded | CARD-1915 Debug | `1e09a851ac` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-8e452527-repair` | 2.68 | 2026-11-14 10:07 | 2026-11-14T13:00 | 78.5 | Succeeded | CARD-1915 Code | `a46bd7af4b` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-37cb8e72-master` | 2.31 | 2026-11-13 17:50 | 2026-11-13T18:49 | 96.7 | Succeeded | - Debug | `c1fd1b7bc1` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-15406ecc-coverage2` | 2.29 | 2026-11-14 17:38 | 2026-11-14T17:56 | 73.6 | Succeeded | CARD-1892 Debug | `8cec1e74e7` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-ac7f437c-base` | 2.29 | 2026-11-14 19:02 | 2026-11-14T19:52 | 71.6 | Succeeded | - Code | `f701b2ba96` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-37cb8e72-s1base` | 2.29 | 2026-11-13 17:51 | 2026-11-13T18:49 | 96.7 | Succeeded | - Debug | `70fd156d06` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-37cb8e72-s1tip` | 2.29 | 2026-11-13 17:51 | 2026-11-13T18:49 | 96.7 | Succeeded | - Debug | `6a42fc4d78` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-3e32f1e6-s2` | 1.35 | 2026-11-17 17:06 | 2026-11-17T17:31 | 2.0 | Succeeded | CARD-1969 Review | `5bce70af16` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-8e452527-base` | 1.34 | 2026-11-14 09:49 | 2026-11-14T13:00 | 78.5 | Succeeded | CARD-1915 Code | `ca8737e781` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-0a2ad036-base` | 1.32 | 2026-11-14 11:36 | 2026-11-14T12:41 | 78.8 | Succeeded | CARD-1901 Code | `ca8737e781` | NO_REMOTE_BRANCH | 0 | no | - |
| `/work/worktrees/task-71aaddba-messaging-measure` | 0.62 | 2026-11-14 03:18 | 2026-11-14T05:33 | 85.9 | Succeeded | CARD-1915 Debug | `1e09a851ac` | NO_REMOTE_BRANCH | 0 | no | - |
## Largest 15 items under /work/worktrees

| # | Path | GiB | Class | Task | Reason |
|---:|---|---:|---|---|---|
| 1 | `/work/worktrees/task-fc32e512` | 16.98 | C | Succeeded CARD-1960 Review | inside retention window (ended 6.5 h ago) |
| 2 | `/work/worktrees/task-277baf57` | 4.54 | C | Succeeded CARD-1969 Code | inside retention window (ended 2.5 h ago) |
| 3 | `/work/worktrees/ws-04-d2` | 4.05 | C | - -  | live CARD-0824 slot (Leased in ledger) |
| 4 | `/work/worktrees/task-ec9dfd70` | 3.72 | A | Succeeded - Merge | A; desktop sync debt Held (retention holds sync_debt) |
| 5 | `/work/worktrees/task-7555284f` | 3.54 | A | Succeeded CARD-1783 Review | A; desktop sync debt Held (retention holds sync_debt) |
| 6 | `/work/worktrees/task-8e452527-checks` | 3.53 | D | Succeeded CARD-1915 Code | auxiliary detached worktree (task 8e452527 helper); HEAD in a live origin ref; clean=true |
| 7 | `/work/worktrees/task-4f5166ec` | 3.20 | A | Succeeded CARD-1728 Merge | A; desktop sync debt Held (retention holds sync_debt) |
| 8 | `/work/worktrees/task-d1556676` | 3.07 | C | Succeeded CARD-1958 Merge | inside retention window (ended 8.6 h ago) |
| 9 | `/work/worktrees/task-aaabade9` | 3.02 | C | Succeeded CARD-1960 Code | inside retention window (ended 9.7 h ago) |
| 10 | `/work/worktrees/task-a3b6865d` | 2.79 | C | Succeeded CARD-1960 Merge | inside retention window (ended 3.2 h ago) |
| 11 | `/work/worktrees/task-71aaddba-measure` | 2.69 | D | Succeeded CARD-1915 Debug | auxiliary detached worktree (task 71aaddba helper); HEAD in a live origin ref; clean=true |
| 12 | `/work/worktrees/task-8e452527-repair` | 2.68 | D | Succeeded CARD-1915 Code | auxiliary detached worktree (task 8e452527 helper); HEAD in a live origin ref; clean=true |
| 13 | `/work/worktrees/task-3e32f1e6` | 2.45 | C | Succeeded CARD-1969 Review | inside retention window (ended 2.0 h ago) |
| 14 | `/work/worktrees/task-50b780b8` | 2.43 | C | Succeeded CARD-1961 Merge | inside retention window (ended 2.2 h ago) |
| 15 | `/work/worktrees/ws-06` | 2.40 | C | - -  | live CARD-0824 slot (Free in ledger) |

## Other /work, /tmp, /state and host disk

| Path | MiB | Note |
|---|---:|---|
| `/work/worktrees` | 168,234 | above |
| `/work/review-evidence` | 1,301 | evidence; also holds one registered worktree `/work/review-evidence/task-4c90c926` |
| `/work/evidence` | 998 | evidence |
| `/work/repos` | 595 | main checkout `/work/repos/antiphon` (shared object store); never touch |
| `/work/retained` | 419 | 52 per-task directories (CARD-0824 retained custody), Nov 15-17 |
| `/work/merge-evidence`, `/work/code-evidence`, `/work/test-evidence` | 94 / 72 / 67 | evidence |
| `/work/workspace-bindings`, `/work/worktrees-retiring`, `/work/workspace-slots`, `/work/cache`, `/work/s5a-mut-*-9afb7a90` (3) | 9 / 1 / 1 / 1 / 1 each | `worktrees-retiring` is empty |
| `/work/retained/quarantine` | - | **does not exist** (`ls`: No such file or directory) |
| `/tmp` | 190,349 | `/tmp/claude-1654` 28,714; then compose/review scratch trees of 2.3-4.7 GiB each (`/tmp/c1877-comp-a`, `-b`, `/tmp/card-1915-review-compose`, ...) |
| `/state` | 39,841 | `session-runner` 22,587, `grok` 11,383, `codex` 4,230, `claude` 1,597 |
| NuGet packages / npm cache / nuget scratch | 4,368 / 1,202 / 34 | cache volumes |

Filesystem (`df -B1G /`, same LV `/dev/mapper/ubuntu--vg-ubuntu--lv` for `/`, `/work`, `/tmp`, `/state`, `/var/lib/docker`):
705 GiB size, 589 GiB used, 88 GiB free, 87 percent at 19:30Z (91 GiB free at 19:03Z).

Nested Docker (`docker system df` against the container's own dind daemon, data on volume `antiphon-runner_dind-data`):
images 20, 6.463 GB, 6.274 GB (97 percent) reclaimable; containers 2 (both exited); local volumes 17, 1.403 GB, 1.021 GB reclaimable
(16 with 0 links, of which 13 are tiny `c1609rda1b88c089c722bb7-n1-*` fixture volumes and three anonymous ones of 40-885 MB);
build cache 0. Read only; no volume touched.

**UNOBSERVED:** the outer host. `ssh mc@server2` from the container answers `Permission denied (publickey)`, and the mounted
`/var/run/docker.sock` is the nested dind daemon (`docker info` Name = this container), not the host daemon. So the host
`docker system df` (images, build cache, the antiphon-runner_* and server2-server volumes) and any holder in another container that
mounts `antiphon-runner_work` are not observed. Resolve from a host with trusted SSH: `ssh mc@server2 'df -h /; docker system df -v'`
and `ssh mc@server2 'docker ps --format "{{.Names}} {{.Mounts}}"'`. `/var/lib/docker` inside the container is root-owned (du read 1 MiB).

## Proposed cleanup command list (class A only)

Preferred: let the sanctioned tool do it. Operator, from a desktop shell with the operator token:
`POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview` first; read the run's `RunnerMirror` rows for the paths
below; then `POST .../runner-retention/server2/execute` and follow `GET /api/agent-tasks/worktree-residue/runs/{runId}` to
`finishedAt`. Expected: A1 removed if `Result` is stored; A2 kept `sync_debt` until the desktop debt clears.

Fallback hand form, only if the operator decides to bypass the tool's sync-debt hold, run inside
`antiphon-runner-session-runner-1` and only after re-checking each tree is still clean, equal and unheld at the moment of removal
(no `--force`, no `rm`, no prune), one command per path:
`git -C /work/repos/antiphon worktree remove <path>`

Paths (A1 first, then A2; largest first):

```
/work/worktrees/task-2b953d03
/work/worktrees/task-0ddd898d
/work/worktrees/task-f4320f2f
/work/worktrees/task-741fb04b
/work/worktrees/task-91085723
/work/worktrees/task-ec9dfd70
/work/worktrees/task-7555284f
/work/worktrees/task-4f5166ec
/work/worktrees/task-c6ca6929
/work/worktrees/task-f87fdda9
/work/worktrees/task-934cdb25
/work/worktrees/task-4292c713
/work/worktrees/task-a2845b6c
/work/worktrees/task-5b1ea772
/work/worktrees/task-c7a30141
/work/worktrees/task-bc7a9fdd
/work/worktrees/task-b0e54eca
/work/worktrees/task-2abcd1c0
/work/worktrees/task-6155693e
/work/worktrees/task-94b17435
/work/worktrees/task-bee64303
/work/worktrees/task-da90742c
/work/worktrees/task-e7d7d78b
```

## Not done, noted

- No preview or execute call: preview is refused to a child token, and both write a residue run. Per-tree kept reasons stay UNOBSERVED.
- Desktop worktrees (`C:\Antiphon\worktrees\card-task-*`) are out of reach from server2 and were not inventoried.
- Fix idea (not designed): enable the CARD-1677 S2 temp sweep in DryRun to size `/tmp` families, since `/tmp` (190 GiB) now outweighs the mirrors.
