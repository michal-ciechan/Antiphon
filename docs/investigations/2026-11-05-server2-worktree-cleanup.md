# Server2 worktree cleanup — 2026-11-05

No worktrees qualified for deletion under the brief's 24-hour last-touch rule. Deleted **0**, reclaimed **0 bytes**. The disk remains **81% used**. The inventory and report are complete; disk pressure remains unresolved.

## Scope and authority

Task `374d7fa0`, branch `feat/card-task-374d7fa0`, runner container on server2. The operator authorized deleting old terminal/orphan worktrees without another approval. That authority was applied without asking for confirmation, subject to the brief's explicit exclusions.

Only literal directories matching `^/work/worktrees/task-[0-9a-f]{8}$` entered the deletion inventory. The current task, standing agents, nonterminal tasks and anything touched within 24 hours were protected. No directories, Docker objects, branches or other data were deleted. No salvage pushes or worktree-prune commands were needed because there were no deletion candidates. The only source change is this report.

Inventory started **2026-11-05T04:55:44.456Z**; fixed eligibility cutoff **2026-11-04T04:55:44.456Z**. Task scope snapshots completed **2026-11-05T04:58:15.339Z**. These are observations on a running host, not an atomic host snapshot.

## Counts and disk footprint

| Measure | Count / allocated size |
|---|---:|
| Exact-format worktrees inventoried | 487 |
| Allocated bytes, sum of per-tree `du -s -B1` | 347,052,113,920 (347.052 GB / 323.217 GiB) |
| Root directory mtime older than the cutoff | 328 |
| Trees with any observed metadata touch within 24 hours | 487 |
| Eligible for deletion | 0 |
| Deleted / deletion failures | 0 / 0 |
| Space reclaimed by this task | 0 bytes |
| Orphans after complete task lookup | 0 |
| Standing-agent worktrees found in the exact-format inventory | 0 |
| Inventory errors / ambiguous task prefixes | 0 / 0 |
| Heads contained in an origin ref | 485 |
| Heads without observed origin containment | 2 |
| Additional suffixed directories excluded by the path rule | 17 (4.513 GB / 4.203 GiB) |

Terminal ownership: 458 Succeeded, 9 Failed, 10 Canceled. Protected nonterminal ownership: 9 Dispatched (including this task), 0 Working, 0 Queued, 1 Blocked.

Mutually exclusive keep reasons:

| Reason | Count |
|---|---:|
| Touched within 24 h | 477 |
| Protected task: Dispatched | 8 |
| Protected task: Blocked | 1 |
| Current cleanup task | 1 |

The recent-touch restriction overlaps all the task-state exclusions: **all 487 trees have recent metadata**, including the 477 terminal-task trees.

## Why old directories were kept

The earliest observed last-touch timestamp anywhere in the inventory was **2026-11-04T23:48:57.461Z**, only about five hours before inspection. 328 root directories have old mtimes, but the brief also explicitly counts the worktree Git `HEAD` and `logs/HEAD` mtimes. Those recent timestamps exclude every tree.

For example, `/work/worktrees/task-006e0b89` has directory mtime `2026-11-03T03:50:40.333Z`, while its Git `logs/HEAD` file has mtime `2026-11-04T23:49:02.446Z`. The last reflog entry itself records a commit at `2026-11-03T04:09:00Z`. Filesystem reflog freshness and commit-event age differ. **452 terminal-task reflogs** have mtimes between `2026-11-04T23:48:00Z` and `2026-11-04T23:50:00Z`; the cause of that file refresh was not established. The explicit filesystem timestamp rule was retained.

Observed timestamps below are lower bounds on the newest filesystem change: the maximum mtime/ctime of the root directory, its `.git` file, Git `HEAD`, and Git `logs/HEAD`. Deep scans were unnecessary because every tree was already protected by recent metadata. Reading filesystem access times was not used as activity evidence.

## Ownership and publication checks

Read the brief's project-scoped task list from September 1 first, then expanded to every project without a date cutoff and with `includeChecks=true`, plus an explicitly project-scoped `unscoped=only` query. This yielded **24,891 task rows** across **15 projects** and the unscoped bucket. The list endpoint is unpaginated; no server-side Take or default date window applies. Every exact-format worktree mapped to one full task UUID with no missing or ambiguous prefixes. The server's agent list contained 2,940 agents, including 8 protected standing/orchestrator entries; none owned a candidate worktree.

Fetched origin once in `/work/repos/antiphon` and inspected each worktree with `git branch -r --contains HEAD`. All exact-format worktrees belong to that shared repository.

No salvage branch was pushed because no tree was eligible for deletion. Two retained heads had no observed origin containment:

| Worktree | State | HEAD | Disposition |
|---|---|---|---|
| /work/worktrees/task-29664acc | Succeeded | `584a34eaea2712ed51ca444c968519dbdb34c4cb` | Kept: Touched within 24 h; salvage only before a future authorized deletion |
| /work/worktrees/task-34125850 | Blocked | `72a8014a92188a8c587c08008b820c51d179b4d3` | Kept: Protected task: Blocked; salvage only before a future authorized deletion |

No builds or tests were run, as instructed. Inventory and disk-usage commands ran as foreground children and were awaited; there was no detached delete job because the candidate list was empty.

## Filesystem before and after

Before (bytes):

```text
Filesystem                           1B-blocks         Used    Available Use% Mounted on
/dev/mapper/ubuntu--vg-ubuntu--lv 756634804224 584067854336 141756018688  81% /work
```

After (bytes):

```text
Filesystem                           1B-blocks         Used    Available Use% Mounted on
/dev/mapper/ubuntu--vg-ubuntu--lv 756634804224 585375526912 140448346112  81% /work
```

The filesystem used bytes increased by **1,307,672,576** and available bytes fell by the same amount during observation. This was not deletion reclamation: no deletes occurred. Other work continued on the host, and this task also wrote its small evidence set. The two df samples do not attribute host-wide changes to individual processes.

## Recommendations only — nothing here was deleted

| Visible consumer | Allocated bytes / observation |
|---|---:|
| `/work/repos` | 939,835,392 (0.940 GB) |
| `/work/evidence` | 404,807,680 (0.405 GB; grows while this audit writes evidence) |
| `/work/workspace-bindings` | 3,694,592 |
| `/work/cache` | 393,216 |
| `/work/test-evidence` | 49,152 |

These are container-visible `/work` consumers; the shared filesystem df includes other host activity and storage that this mount view does not attribute. Per-tree du sums and a later full-root du are separate live samples and may differ as other tasks write outputs. The full-root `du -s -B1 /work/worktrees` sample was **352,665,645,056 bytes**.

Docker's read-only `docker system df` result:

```text
TYPE            TOTAL     ACTIVE    SIZE      RECLAIMABLE
Images          14        2         5.731GB   5.541GB (96%)
Containers      2         1         63B       0B (0%)
Local Volumes   13        1         380.8MB   28.11kB (0%)
Build Cache     0         0         0B        0B
```

The Docker daemon visible to this container reports **5.541 GB reclaimable image storage**, the largest measured non-worktree opportunity. Any image removal requires a separately authorized operation. Volumes offer only **28.11 kB** of reported reclamation and are out of scope. No Docker prune or volume operation was run.

For worktree reclamation under the existing rule, repeat the inventory once the observed November 4 23:48–23:49 metadata refresh is more than 24 hours old, **after November 5 23:50 UTC**, and recheck current task state and all newer filesystem activity. That time alone does not establish eligibility. If the operator wants to discount that metadata refresh sooner, the explicit last-touch exclusion must be changed in a new brief; this task did not make that policy change.

## Retained exact-format worktrees

All paths below were validated literally. Sizes are allocated bytes expressed as GiB; completedAt comes from the server. The timestamp is the newest observed metadata touch, with age measured against the audit start. `Origin=yes` means at least one origin ref contained the observed HEAD after fetch; it does not imply uncommitted files were published. No retained tree was modified for salvage.

| Path | State | completedAt (UTC) | GiB | Observed touch (UTC) | Age h | HEAD | Origin | Keep reason |
|---|---|---|---:|---|---:|---|---|---|
| /work/worktrees/task-006e0b89 | Succeeded | 2026-11-03T04:09:32.410758Z | 0.219 | 2026-11-04T23:49:02.446Z | 5.11 | `a5c9fdbb73433ef579cc8e144b137e99eca945ce` | yes | Touched within 24 h |
| /work/worktrees/task-00a0b2d1 | Succeeded | 2026-11-04T12:44:05.920555Z | 0.220 | 2026-11-04T23:49:01.917Z | 5.11 | `36ba2a14f3cf62e3d56787b9b504b9e4d10ea60c` | yes | Touched within 24 h |
| /work/worktrees/task-00efc28c | Succeeded | 2026-11-02T16:30:19.977254Z | 0.248 | 2026-11-04T23:49:00.389Z | 5.11 | `8bbad39c51625e88e0f5883136928b879e3ba3bb` | yes | Touched within 24 h |
| /work/worktrees/task-038a3b2f | Succeeded | 2026-11-04T03:30:48.216903Z | 0.219 | 2026-11-04T23:48:58.369Z | 5.11 | `1e45c327b598548b43537d54c7e2922075a199cb` | yes | Touched within 24 h |
| /work/worktrees/task-03e7cd6d | Succeeded | 2026-11-05T00:24:12.140202Z | 0.220 | 2026-11-05T00:23:22.202Z | 4.54 | `7190d151cba68c2a46d99b1d60d7baaf4e3388c9` | yes | Touched within 24 h |
| /work/worktrees/task-045f22a4 | Canceled | 2026-11-02T19:21:40.160601Z | 0.216 | 2026-11-04T23:49:00.893Z | 5.11 | `0e88f5adba7249c356f3b06e1626a9250525156b` | yes | Touched within 24 h |
| /work/worktrees/task-046e892d | Succeeded | 2026-11-03T10:46:10.290042Z | 0.218 | 2026-11-04T23:49:00.109Z | 5.11 | `b0374226426b8aada42e8d370b95a4e53c4612cf` | yes | Touched within 24 h |
| /work/worktrees/task-04beb796 | Succeeded | 2026-11-02T22:22:47.917175Z | 0.217 | 2026-11-04T23:48:58.537Z | 5.11 | `6d268b2b887dc6311787180e61d0db597f41dc86` | yes | Touched within 24 h |
| /work/worktrees/task-053fe121 | Succeeded | 2026-11-02T19:50:36.470402Z | 0.338 | 2026-11-04T23:49:00.601Z | 5.11 | `faaf94b2d2172775acb701e5073d1e43b6204895` | yes | Touched within 24 h |
| /work/worktrees/task-0665f172 | Succeeded | 2026-11-02T14:11:50.005682Z | 1.822 | 2026-11-04T23:49:05.106Z | 5.11 | `e5d08e1509e8f20ca46d8dbf0197ffebf39498eb` | yes | Touched within 24 h |
| /work/worktrees/task-072cf7b1 | Succeeded | 2026-11-02T22:55:08.082459Z | 3.005 | 2026-11-04T23:49:01.717Z | 5.11 | `907edaebbd301af95629c31095f1b530803baea2` | yes | Touched within 24 h |
| /work/worktrees/task-08469fcb | Succeeded | 2026-11-02T13:47:46.506047Z | 0.371 | 2026-11-04T23:49:00.909Z | 5.11 | `7bd2f112e37792c8a42f12b1f7039e8dd5971b91` | yes | Touched within 24 h |
| /work/worktrees/task-08a2e7f3 | Succeeded | 2026-11-04T20:21:22.005014Z | 0.220 | 2026-11-04T23:49:06.294Z | 5.11 | `8062eafee4c23144112d1e45a3ce54b111a16526` | yes | Touched within 24 h |
| /work/worktrees/task-08c004b5 | Succeeded | 2026-11-02T18:48:16.93571Z | 1.825 | 2026-11-04T23:49:01.333Z | 5.11 | `2aab4d4d07888e49d8ddcadaee75d58e29d6fc98` | yes | Touched within 24 h |
| /work/worktrees/task-08cca1b8 | Succeeded | 2026-11-04T11:24:59.106092Z | 0.220 | 2026-11-04T23:49:02.946Z | 5.11 | `aa33d8f639c9c6ede9a54fc454472f756abba039` | yes | Touched within 24 h |
| /work/worktrees/task-0af9a4e5 | Succeeded | 2026-11-03T23:27:14.64837Z | 0.218 | 2026-11-04T23:48:58.793Z | 5.11 | `38af7a7c593c276ff1a4db33e4d08b6565d283bb` | yes | Touched within 24 h |
| /work/worktrees/task-0b9a5438 | Succeeded | 2026-11-02T18:53:48.699313Z | 0.272 | 2026-11-04T23:48:58.057Z | 5.11 | `3a4f73374a027204ddd79b32070c5f8ebcd44222` | yes | Touched within 24 h |
| /work/worktrees/task-0be34a95 | Succeeded | 2026-11-02T20:25:45.5682Z | 1.839 | 2026-11-04T23:49:06.002Z | 5.11 | `a29f9beabaf8c9d7ed5305f8459836b56e418036` | yes | Touched within 24 h |
| /work/worktrees/task-0c3b7de3 | Succeeded | 2026-11-04T11:58:28.531316Z | 0.220 | 2026-11-04T23:48:59.537Z | 5.11 | `5ab8e14fbd0d8f3c34e1303d2a93733ea79b1565` | yes | Touched within 24 h |
| /work/worktrees/task-0c839a5d | Succeeded | 2026-11-03T11:01:34.234264Z | 0.219 | 2026-11-04T23:49:04.362Z | 5.11 | `c438868d63a0ccc413dfa5e18c86592d2b5ad011` | yes | Touched within 24 h |
| /work/worktrees/task-0ce3bb38 | Succeeded | 2026-11-03T05:36:30.96542Z | 0.217 | 2026-11-04T23:48:57.849Z | 5.11 | `284413498f37c3d98de77f0c8d12446fca4fcada` | yes | Touched within 24 h |
| /work/worktrees/task-0e8a1b50 | Succeeded | 2026-11-04T18:00:46.125014Z | 0.220 | 2026-11-04T23:49:01.789Z | 5.11 | `cc1f3248633a06bb3ff2e18d38ff0c3b7706c7bf` | yes | Touched within 24 h |
| /work/worktrees/task-0eeb3129 | Succeeded | 2026-11-03T06:02:38.715482Z | 0.217 | 2026-11-04T23:49:06.134Z | 5.11 | `a5ce15c96f2def98c4c886dc6febe245b291cb34` | yes | Touched within 24 h |
| /work/worktrees/task-0efa38ca | Succeeded | 2026-11-04T16:18:54.692381Z | 0.220 | 2026-11-04T23:48:57.701Z | 5.11 | `c00fb9354ae72cc099c49b4bfed5d2d5808c66a2` | yes | Touched within 24 h |
| /work/worktrees/task-0f317c85 | Succeeded | 2026-11-03T06:46:07.168659Z | 0.222 | 2026-11-04T23:49:01.157Z | 5.11 | `19062e44923759ec3ee55075e5835acbe52f13f0` | yes | Touched within 24 h |
| /work/worktrees/task-0f359985 | Succeeded | 2026-11-02T13:14:17.646523Z | 0.313 | 2026-11-04T23:48:58.637Z | 5.11 | `10dc40003b049f120a6b8a4961367be381f670f8` | yes | Touched within 24 h |
| /work/worktrees/task-0f4dc1cc | Succeeded | 2026-11-03T06:29:18.858316Z | 0.217 | 2026-11-04T23:49:02.854Z | 5.11 | `ad28ef5ecadd5251f553af7e163304359ac4741c` | yes | Touched within 24 h |
| /work/worktrees/task-11a0b0b8 | Succeeded | 2026-11-05T02:17:58.269312Z | 0.220 | 2026-11-05T02:17:07.379Z | 2.64 | `cdd837f3b6d0455ccf927c04fe1c2c3e63dd6e63` | yes | Touched within 24 h |
| /work/worktrees/task-11a6d8bb | Dispatched | — | 1.239 | 2026-11-05T03:39:47.029Z | 1.27 | `35bb34ad7fe28541d8194f18e2bde10d211db6a4` | yes | Protected task: Dispatched |
| /work/worktrees/task-122f2dcf | Succeeded | 2026-11-04T22:42:22.833083Z | 0.220 | 2026-11-04T23:48:58.685Z | 5.11 | `3e219083e348ebe058971be56250c8adbbf5c8f9` | yes | Touched within 24 h |
| /work/worktrees/task-1296ccbd | Succeeded | 2026-11-03T07:08:54.645118Z | 0.226 | 2026-11-04T23:49:05.554Z | 5.11 | `d08bf72b657d9f9fcff61c63f241b0cb52e418a0` | yes | Touched within 24 h |
| /work/worktrees/task-12ba9a25 | Succeeded | 2026-11-03T17:53:19.772213Z | 0.220 | 2026-11-04T23:49:04.658Z | 5.11 | `c683806d24216d6b6cd0ead5587ade54c791dfa6` | yes | Touched within 24 h |
| /work/worktrees/task-12c87cda | Succeeded | 2026-11-04T07:35:19.951608Z | 0.220 | 2026-11-04T23:49:03.966Z | 5.11 | `20bf6533a514d78cac49941a2f2741451591b022` | yes | Touched within 24 h |
| /work/worktrees/task-131fbc4c | Succeeded | 2026-11-04T00:49:44.93332Z | 0.223 | 2026-11-04T23:49:05.246Z | 5.11 | `5ba8777d40537b5f1d0a50729c07520f56d3568f` | yes | Touched within 24 h |
| /work/worktrees/task-14adea19 | Succeeded | 2026-11-03T00:50:10.651156Z | 1.901 | 2026-11-04T23:49:01.029Z | 5.11 | `3f58c41cc02b93eacef1d221e65a2b465eeec9cb` | yes | Touched within 24 h |
| /work/worktrees/task-16a9c9be | Succeeded | 2026-11-03T20:30:14.24343Z | 0.218 | 2026-11-04T23:49:04.834Z | 5.11 | `f422433cd3dc8843dffe34653076273bdbd00ab8` | yes | Touched within 24 h |
| /work/worktrees/task-17fe9eec | Succeeded | 2026-11-04T01:10:21.490484Z | 0.218 | 2026-11-04T23:48:58.613Z | 5.11 | `92a48622c5bd5d1826fb2b7e7ef1bc458beccd70` | yes | Touched within 24 h |
| /work/worktrees/task-189595a5 | Succeeded | 2026-11-04T09:48:22.899113Z | 0.223 | 2026-11-04T23:49:01.001Z | 5.11 | `3bf42baf4996a8505bb4231634ab0dc71a539241` | yes | Touched within 24 h |
| /work/worktrees/task-19a14614 | Succeeded | 2026-11-04T17:42:29.339474Z | 0.220 | 2026-11-04T23:49:00.653Z | 5.11 | `b2cf128a63e74998d650360f7e4c4a2a1a262ce3` | yes | Touched within 24 h |
| /work/worktrees/task-1a3ccb7a | Succeeded | 2026-11-03T21:30:18.099834Z | 0.219 | 2026-11-04T23:49:01.173Z | 5.11 | `868704eddf0022bd88329f445759274fc4bafff4` | yes | Touched within 24 h |
| /work/worktrees/task-1a8785ed | Succeeded | 2026-11-04T00:39:54.665083Z | 1.829 | 2026-11-04T23:49:00.469Z | 5.11 | `46fbc13d8ec7b904f6a75827868bad3f9b75a361` | yes | Touched within 24 h |
| /work/worktrees/task-1aa2da7c | Succeeded | 2026-11-03T20:30:25.683621Z | 0.218 | 2026-11-04T23:49:03.226Z | 5.11 | `0df4f6beee5f2ad422de2da99a0027728fc6e24b` | yes | Touched within 24 h |
| /work/worktrees/task-1b618bf5 | Succeeded | 2026-11-02T20:33:13.994802Z | 3.004 | 2026-11-04T23:48:59.341Z | 5.11 | `18bc8a19151be1a3f206a9107a0170c386337564` | yes | Touched within 24 h |
| /work/worktrees/task-1c022492 | Succeeded | 2026-11-04T14:03:20.561725Z | 0.220 | 2026-11-04T23:49:02.009Z | 5.11 | `960e18bbe606ebeaa25aa5615147661381bdb90b` | yes | Touched within 24 h |
| /work/worktrees/task-1c0653a5 | Succeeded | 2026-11-04T19:15:53.107577Z | 0.220 | 2026-11-04T23:49:04.426Z | 5.11 | `66cbd06b11b633b74e72fce43e056423ffa46681` | yes | Touched within 24 h |
| /work/worktrees/task-1c2d93f1 | Succeeded | 2026-11-04T16:10:54.056839Z | 0.220 | 2026-11-04T23:49:03.238Z | 5.11 | `a865a50be2ad629eb29402780556a75affb6b23c` | yes | Touched within 24 h |
| /work/worktrees/task-1c44bc80 | Succeeded | 2026-11-04T16:33:38.874836Z | 0.220 | 2026-11-04T23:49:02.025Z | 5.11 | `e19c2c3091dd2601676400e01996cacda5c941f0` | yes | Touched within 24 h |
| /work/worktrees/task-1c4f8dd2 | Succeeded | 2026-11-04T23:16:26.593696Z | 0.220 | 2026-11-04T23:49:04.702Z | 5.11 | `05da67f7853cad8c5c3726a29173976e1b5017a8` | yes | Touched within 24 h |
| /work/worktrees/task-1cf13574 | Succeeded | 2026-11-04T09:00:18.709796Z | 0.220 | 2026-11-04T23:49:06.334Z | 5.11 | `57f2317712e8f03c2be86cb91f26048e1f336990` | yes | Touched within 24 h |
| /work/worktrees/task-1d023ed2 | Succeeded | 2026-11-03T22:10:27.628351Z | 0.300 | 2026-11-04T23:49:01.905Z | 5.11 | `97e77f9f1fd6f71e2719bc661b48f8f89f129029` | yes | Touched within 24 h |
| /work/worktrees/task-1dc468a4 | Succeeded | 2026-11-04T07:49:53.597835Z | 0.220 | 2026-11-04T23:49:05.142Z | 5.11 | `d9a2979d64c9669f3afa9bcc20de12631cc9cb5e` | yes | Touched within 24 h |
| /work/worktrees/task-1dcb8d90 | Succeeded | 2026-11-04T01:02:16.191592Z | 0.222 | 2026-11-04T23:49:02.365Z | 5.11 | `c5fc45a03b99251e097a51987a30309bb6ba6585` | yes | Touched within 24 h |
| /work/worktrees/task-1e52322c | Succeeded | 2026-10-31T17:59:02.141124Z | 0.230 | 2026-11-04T23:49:01.389Z | 5.11 | `d64bb3201d817f736446c6dd2cddd9e4de434660` | yes | Touched within 24 h |
| /work/worktrees/task-1ed0ea30 | Succeeded | 2026-11-04T00:10:00.52975Z | 0.218 | 2026-11-04T23:49:03.906Z | 5.11 | `12c9b5a2ff92748fefca00d33d577d91a47a8722` | yes | Touched within 24 h |
| /work/worktrees/task-1edbc845 | Canceled | 2026-11-05T04:39:10.024023Z | 2.021 | 2026-11-05T03:23:42.855Z | 1.53 | `f47be567b5545c0014775ff18be9f423714ee14a` | yes | Touched within 24 h |
| /work/worktrees/task-1ee29680 | Succeeded | 2026-11-04T19:09:02.908765Z | 0.220 | 2026-11-04T23:49:00.145Z | 5.11 | `28d19dd24a16d6993ae1aa0262cb4ddb481905e0` | yes | Touched within 24 h |
| /work/worktrees/task-203ef46a | Succeeded | 2026-11-03T09:49:43.494999Z | 0.220 | 2026-11-04T23:48:58.325Z | 5.11 | `7a0fa1d1398ce9d1c12431ee1e9733d0f69ee436` | yes | Touched within 24 h |
| /work/worktrees/task-212363b0 | Succeeded | 2026-11-04T09:42:43.501167Z | 0.220 | 2026-11-04T23:49:05.166Z | 5.11 | `6c590029723c8d9b5b15b033be7f2682d88ce0b6` | yes | Touched within 24 h |
| /work/worktrees/task-230155cc | Succeeded | 2026-11-02T18:11:04.679409Z | 1.823 | 2026-11-04T23:49:04.974Z | 5.11 | `8b8dc6268aaac866f33b51dcb82a4fa29afa0dc1` | yes | Touched within 24 h |
| /work/worktrees/task-230d1f5d | Succeeded | 2026-11-03T23:45:23.457843Z | 0.218 | 2026-11-04T23:49:00.573Z | 5.11 | `639f78a890f35093fe8366a6d2881b1dc649f005` | yes | Touched within 24 h |
| /work/worktrees/task-23889c9b | Dispatched | — | 1.947 | 2026-11-05T04:22:46.443Z | 0.55 | `3ec62c653a80c819fff5fecfd7b2ff8ea1bf6874` | yes | Protected task: Dispatched |
| /work/worktrees/task-23fce9a6 | Succeeded | 2026-11-04T16:53:11.347192Z | 0.220 | 2026-11-04T23:49:05.778Z | 5.11 | `8b9a107d6bcb6357e305a26ee716d0b0275b5776` | yes | Touched within 24 h |
| /work/worktrees/task-24291915 | Succeeded | 2026-11-02T19:39:48.333837Z | 1.823 | 2026-11-04T23:49:00.413Z | 5.11 | `cac23c108fc1baa8ad3f6006ac57a31950006ee4` | yes | Touched within 24 h |
| /work/worktrees/task-24eb34b0 | Succeeded | 2026-11-03T03:01:33.546317Z | 0.323 | 2026-11-04T23:48:59.909Z | 5.11 | `895a13714b4a7354854e6312c21f112b6230b3a1` | yes | Touched within 24 h |
| /work/worktrees/task-250763e5 | Succeeded | 2026-11-04T00:40:12.441567Z | 0.222 | 2026-11-04T23:49:00.497Z | 5.11 | `7ead8f06662f15bff260836f86dd5027b5384820` | yes | Touched within 24 h |
| /work/worktrees/task-27ca134f | Succeeded | 2026-11-04T19:39:31.805587Z | 0.220 | 2026-11-04T23:49:00.713Z | 5.11 | `db19cbbfc661860c3fb747342638bb873548ac4f` | yes | Touched within 24 h |
| /work/worktrees/task-27e2f0b5 | Dispatched | — | 0.371 | 2026-11-05T04:47:38.042Z | 0.14 | `64e9c7cf9918848d0bc899292d7da67c6ef25ea3` | yes | Protected task: Dispatched |
| /work/worktrees/task-291753f8 | Succeeded | 2026-11-04T16:35:30.341944Z | 0.220 | 2026-11-04T23:49:04.638Z | 5.11 | `ad14029193bc6c634df37acb0662cb73d9fd1943` | yes | Touched within 24 h |
| /work/worktrees/task-29664acc | Succeeded | 2026-11-04T09:57:37.902423Z | 0.220 | 2026-11-04T23:49:03.314Z | 5.11 | `584a34eaea2712ed51ca444c968519dbdb34c4cb` | no | Touched within 24 h |
| /work/worktrees/task-29cd334a | Succeeded | 2026-11-04T07:26:46.226883Z | 0.220 | 2026-11-04T23:49:04.926Z | 5.11 | `20bf6533a514d78cac49941a2f2741451591b022` | yes | Touched within 24 h |
| /work/worktrees/task-2a6ecbbd | Succeeded | 2026-11-03T06:41:39.907969Z | 0.217 | 2026-11-04T23:49:03.254Z | 5.11 | `99e4cb1df9db38fa1356b8f6733653d501c03911` | yes | Touched within 24 h |
| /work/worktrees/task-2b89e41c | Succeeded | 2026-11-03T09:56:58.393321Z | 0.218 | 2026-11-04T23:48:58.393Z | 5.11 | `6ed8ff3d2937445d3a2e63e8c842ef346d47a574` | yes | Touched within 24 h |
| /work/worktrees/task-2bc285a9 | Succeeded | 2026-11-03T21:22:53.00978Z | 0.240 | 2026-11-04T23:49:05.634Z | 5.11 | `84bc860893652e49b3ffbeb34490825ff9b6d700` | yes | Touched within 24 h |
| /work/worktrees/task-2c8d00db | Succeeded | 2026-11-03T04:28:29.835815Z | 0.221 | 2026-11-04T23:49:02.518Z | 5.11 | `9c0721c86a240b67fb8e2aa6f9fd77e160cefec7` | yes | Touched within 24 h |
| /work/worktrees/task-2deb2419 | Succeeded | 2026-11-03T06:47:59.908393Z | 0.217 | 2026-11-04T23:49:02.309Z | 5.11 | `cd8ea504f55bbbd3494f00e1170cc991d171e1f9` | yes | Touched within 24 h |
| /work/worktrees/task-2e1bab40 | Succeeded | 2026-11-02T20:55:58.839907Z | 1.830 | 2026-11-04T23:49:02.970Z | 5.11 | `63f40f991376d6e1956c155e26fa6c96c08c4bff` | yes | Touched within 24 h |
| /work/worktrees/task-2e6da28d | Succeeded | 2026-11-03T01:13:00.18802Z | 3.006 | 2026-11-04T23:49:03.118Z | 5.11 | `7dc9e1168a2b4f714a4e3e29ac99e2aad485524c` | yes | Touched within 24 h |
| /work/worktrees/task-2ede815a | Dispatched | — | 0.591 | 2026-11-05T04:44:47.447Z | 0.18 | `ef3dea0c2252dd2307a0cacd5317946981b4bff7` | yes | Protected task: Dispatched |
| /work/worktrees/task-2f58c570 | Succeeded | 2026-11-04T14:23:30.409913Z | 0.220 | 2026-11-04T23:49:04.766Z | 5.11 | `da0106cbe7c500bade6659d4700b4a6cabe2b148` | yes | Touched within 24 h |
| /work/worktrees/task-306148cc | Succeeded | 2026-11-04T11:08:12.893501Z | 0.220 | 2026-11-04T23:49:02.670Z | 5.11 | `785da8bf7a596ba1b4dbcb6947d98c8c8b735d80` | yes | Touched within 24 h |
| /work/worktrees/task-314297be | Canceled | 2026-11-04T12:46:08.93082Z | 0.220 | 2026-11-04T23:49:05.194Z | 5.11 | `96882045e00c74f60f72a52aad51e47789b9d8a4` | yes | Touched within 24 h |
| /work/worktrees/task-31873171 | Succeeded | 2026-11-04T02:02:42.478039Z | 0.221 | 2026-11-04T23:48:59.361Z | 5.11 | `59b7ba0e8d29a114839311aaaa4f0f2ff0921e23` | yes | Touched within 24 h |
| /work/worktrees/task-31e20f5a | Succeeded | 2026-11-04T09:18:20.686805Z | 0.242 | 2026-11-04T23:49:05.298Z | 5.11 | `870a6d8f1636d8304415e2967dcefe8ca57ac628` | yes | Touched within 24 h |
| /work/worktrees/task-32354421 | Succeeded | 2026-11-03T09:17:40.47884Z | 0.217 | 2026-11-04T23:49:00.701Z | 5.11 | `cdec24b86c5676a1b055d240ba444729e26c625e` | yes | Touched within 24 h |
| /work/worktrees/task-333e1bd2 | Succeeded | 2026-11-03T09:54:14.289641Z | 0.219 | 2026-11-04T23:49:04.910Z | 5.11 | `da8f1a9d0c896f8b7d34f8243f275dbcb7301a2d` | yes | Touched within 24 h |
| /work/worktrees/task-33847aaa | Succeeded | 2026-11-04T02:28:53.418998Z | 0.219 | 2026-11-04T23:49:02.193Z | 5.11 | `3de1fb13c89c7a414352782c1ae35e36888e705c` | yes | Touched within 24 h |
| /work/worktrees/task-34125850 | Blocked | 2026-11-04T10:08:10.464915Z | 0.220 | 2026-11-04T23:49:01.697Z | 5.11 | `72a8014a92188a8c587c08008b820c51d179b4d3` | no | Protected task: Blocked |
| /work/worktrees/task-344b9bdc | Succeeded | 2026-11-02T23:35:19.351742Z | 1.826 | 2026-11-04T23:49:04.814Z | 5.11 | `9c880429039a1f10bbca7b2782fd3bba3b34ace8` | yes | Touched within 24 h |
| /work/worktrees/task-3501177a | Succeeded | 2026-11-03T00:25:57.028909Z | 1.824 | 2026-11-04T23:48:59.449Z | 5.11 | `9c880429039a1f10bbca7b2782fd3bba3b34ace8` | yes | Touched within 24 h |
| /work/worktrees/task-35623cb8 | Succeeded | 2026-11-04T08:34:43.913042Z | 0.220 | 2026-11-04T23:49:03.126Z | 5.11 | `45c7328b846ed7ca9e3770e3bf4581f3149c2282` | yes | Touched within 24 h |
| /work/worktrees/task-359d84c5 | Succeeded | 2026-11-02T21:00:36.732086Z | 2.159 | 2026-11-04T23:48:57.957Z | 5.11 | `69045e3350b7857a45a6327055aa9090a61a554b` | yes | Touched within 24 h |
| /work/worktrees/task-362f7434 | Succeeded | 2026-11-05T01:02:29.450966Z | 1.943 | 2026-11-05T00:30:48.188Z | 4.42 | `96d7cde36af3868502900d77d5f8ab8fd7504941` | yes | Touched within 24 h |
| /work/worktrees/task-36a5ec03 | Succeeded | 2026-11-03T04:03:14.337073Z | 1.951 | 2026-11-04T23:49:04.618Z | 5.11 | `6e7c8c0f69ed722c2f507ae9b46a6c4da5e1d3a5` | yes | Touched within 24 h |
| /work/worktrees/task-36b3dc05 | Succeeded | 2026-11-04T16:20:37.960985Z | 0.220 | 2026-11-04T23:49:01.869Z | 5.11 | `546921b0dc33e32c46d96e7f75bcdfcb2039f9d0` | yes | Touched within 24 h |
| /work/worktrees/task-370083f1 | Succeeded | 2026-11-03T22:48:38.606334Z | 0.224 | 2026-11-04T23:48:57.905Z | 5.11 | `e5fc097bc69c7505192a3bb525f45086449ae176` | yes | Touched within 24 h |
| /work/worktrees/task-374d7fa0 | Dispatched | — | 0.220 | 2026-11-05T04:54:02.485Z | 0.03 | `28cdefab326782f2c91ca361768a7252dc875ec7` | yes | Current cleanup task |
| /work/worktrees/task-378de644 | Succeeded | 2026-11-03T21:52:17.415362Z | 0.218 | 2026-11-04T23:49:01.125Z | 5.11 | `2cad8a419ec3644560a9aebdcd5f768897df981b` | yes | Touched within 24 h |
| /work/worktrees/task-37b01055 | Succeeded | 2026-11-04T12:42:04.650806Z | 0.220 | 2026-11-04T23:49:05.502Z | 5.11 | `96d06ebc333ebbb3a516583a1bc741ce07822c8c` | yes | Touched within 24 h |
| /work/worktrees/task-38030aae | Succeeded | 2026-11-02T12:28:10.306914Z | 0.313 | 2026-11-04T23:49:04.918Z | 5.11 | `ab0e1410f7cb58751ec5e8866e6b1e6a67c96ad3` | yes | Touched within 24 h |
| /work/worktrees/task-38d3c07b | Succeeded | 2026-11-04T03:09:29.915212Z | 0.219 | 2026-11-04T23:48:59.381Z | 5.11 | `46c81ff31092b5e592e5cd11fa3f806a0692dbe1` | yes | Touched within 24 h |
| /work/worktrees/task-394d7d1a | Succeeded | 2026-11-02T13:08:33.192288Z | 1.819 | 2026-11-04T23:48:59.201Z | 5.11 | `9af65cdcac8b15aca4fa4d6a0f302974f22d3b88` | yes | Touched within 24 h |
| /work/worktrees/task-39a11efd | Succeeded | 2026-11-02T20:03:27.716841Z | 1.824 | 2026-11-04T23:49:06.358Z | 5.11 | `4374424e754c8b224669ee5a98a9bda0400cf9f4` | yes | Touched within 24 h |
| /work/worktrees/task-39c0fd93 | Succeeded | 2026-11-03T17:08:05.685668Z | 0.861 | 2026-11-04T23:49:01.997Z | 5.11 | `55581453eba557bb005f83a8e8675715b2b123e0` | yes | Touched within 24 h |
| /work/worktrees/task-39e2eabd | Succeeded | 2026-11-03T00:16:53.949424Z | 0.330 | 2026-11-04T23:49:04.554Z | 5.11 | `991b59357b5167df633ad3c94d9d0fe9ccce612d` | yes | Touched within 24 h |
| /work/worktrees/task-3a179edb | Succeeded | 2026-11-04T13:52:36.85552Z | 0.220 | 2026-11-04T23:49:02.229Z | 5.11 | `efd610c937db23899a4bac85e731419f538bc1ca` | yes | Touched within 24 h |
| /work/worktrees/task-3af9f716 | Succeeded | 2026-11-03T04:04:17.541298Z | 1.902 | 2026-11-04T23:49:04.786Z | 5.11 | `06c9e77eaf60b12bffaf34256f179be581a0aab9` | yes | Touched within 24 h |
| /work/worktrees/task-3be64a0a | Succeeded | 2026-11-03T01:14:34.404524Z | 1.824 | 2026-11-04T23:49:01.925Z | 5.11 | `e7de1ab498629de66cf38e223c2b6e056593767f` | yes | Touched within 24 h |
| /work/worktrees/task-3c2aac7d | Succeeded | 2026-11-05T01:03:50.844508Z | 0.220 | 2026-11-05T01:02:46.135Z | 3.88 | `12ea53d4352b6850c8e0e97e1e501b7e08fca17a` | yes | Touched within 24 h |
| /work/worktrees/task-3e4381e4 | Succeeded | 2026-11-03T21:45:13.136659Z | 0.222 | 2026-11-04T23:48:57.693Z | 5.11 | `79f8a376d35f54d801c8547d328f851a7fa8f61f` | yes | Touched within 24 h |
| /work/worktrees/task-3f46a92a | Succeeded | 2026-11-03T03:07:38.063794Z | 1.825 | 2026-11-04T23:48:57.829Z | 5.11 | `960120797bf326b3ed0190db5581924f94b777e5` | yes | Touched within 24 h |
| /work/worktrees/task-3f52b55a | Succeeded | 2026-11-05T02:18:49.655448Z | 0.220 | 2026-11-05T01:35:23.932Z | 3.34 | `467b00c25ba04fb2a6cbe583079e06211c93c8a7` | yes | Touched within 24 h |
| /work/worktrees/task-3fa792e4 | Succeeded | 2026-11-02T21:42:31.496876Z | 2.159 | 2026-11-04T23:49:02.882Z | 5.11 | `e2a886bfa26cedefc7f6f98e31fe52f9b8dceaf6` | yes | Touched within 24 h |
| /work/worktrees/task-3fd6747e | Failed | 2026-11-04T23:18:19.686023Z | 0.220 | 2026-11-04T23:49:01.965Z | 5.11 | `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c` | yes | Touched within 24 h |
| /work/worktrees/task-4047a80b | Succeeded | 2026-11-04T15:44:01.235767Z | 0.419 | 2026-11-04T23:49:02.898Z | 5.11 | `d4c88ddde554ae9f01fad65f27bc893175271b51` | yes | Touched within 24 h |
| /work/worktrees/task-406bca79 | Succeeded | 2026-11-04T20:07:09.671625Z | 0.220 | 2026-11-04T23:48:59.837Z | 5.11 | `03bb1e5d1f31ef1ddb34f23549c32c7ecaac111a` | yes | Touched within 24 h |
| /work/worktrees/task-421e496e | Succeeded | 2026-11-02T12:20:34.461143Z | 1.895 | 2026-11-04T23:49:05.014Z | 5.11 | `f5283ad087512bde775f4a43669441e4790f7d86` | yes | Touched within 24 h |
| /work/worktrees/task-4230f0b9 | Succeeded | 2026-11-03T21:35:00.33351Z | 0.861 | 2026-11-04T23:49:00.857Z | 5.11 | `8401321fdb26b8e729065cd2afe6d6a3ac47b216` | yes | Touched within 24 h |
| /work/worktrees/task-42e16a50 | Succeeded | 2026-11-03T09:43:10.172696Z | 0.217 | 2026-11-04T23:48:59.477Z | 5.11 | `346e1684c2d633183f5a7dc915e7a2f9f8e292a1` | yes | Touched within 24 h |
| /work/worktrees/task-436ebdc6 | Succeeded | 2026-11-03T03:57:04.137746Z | 1.825 | 2026-11-04T23:48:58.489Z | 5.11 | `c2f305044afd500ffeb93031abd1382b81f23640` | yes | Touched within 24 h |
| /work/worktrees/task-43f316b5 | Succeeded | 2026-11-03T17:55:36.699065Z | 0.218 | 2026-11-04T23:48:57.645Z | 5.11 | `e108286b2bf1319a3951883db6aee6209a26cbd7` | yes | Touched within 24 h |
| /work/worktrees/task-44670038 | Succeeded | 2026-11-03T16:35:04.327816Z | 0.218 | 2026-11-04T23:49:05.574Z | 5.11 | `fdc4d4ddc8e4b412d15fa08a550ff6383a6de400` | yes | Touched within 24 h |
| /work/worktrees/task-4468a62d | Succeeded | 2026-11-03T05:24:53.75384Z | 0.217 | 2026-11-04T23:48:58.929Z | 5.11 | `fae47f85d86476741645815f43ac50910ef73a5d` | yes | Touched within 24 h |
| /work/worktrees/task-449c541d | Succeeded | 2026-11-04T14:23:51.188211Z | 0.220 | 2026-11-04T23:49:03.726Z | 5.11 | `9d81df9b28a5d054fb57c79bbc124ef55f9ca006` | yes | Touched within 24 h |
| /work/worktrees/task-4521968e | Succeeded | 2026-11-02T14:04:04.882982Z | 0.313 | 2026-11-04T23:49:00.965Z | 5.11 | `97c185b0ba67bb5c12ff26a1bb3fcb4b88882116` | yes | Touched within 24 h |
| /work/worktrees/task-4555c064 | Succeeded | 2026-11-03T06:26:40.618616Z | 0.217 | 2026-11-04T23:49:04.850Z | 5.11 | `851bae3a19fbfa9126da72543f959dccef9aebb4` | yes | Touched within 24 h |
| /work/worktrees/task-455c5755 | Succeeded | 2026-11-03T03:35:51.762341Z | 1.951 | 2026-11-04T23:49:02.349Z | 5.11 | `55c4fb1b625d99d31575f7e947b00ec6165f52d0` | yes | Touched within 24 h |
| /work/worktrees/task-456cfb77 | Succeeded | 2026-11-03T16:29:58.383238Z | 0.218 | 2026-11-04T23:48:59.193Z | 5.11 | `ad3c2dbe7c2809dbb6c240b6c40caa276d51f073` | yes | Touched within 24 h |
| /work/worktrees/task-459edfef | Succeeded | 2026-11-04T20:09:03.685629Z | 0.220 | 2026-11-04T23:48:58.001Z | 5.11 | `86ccf5602e9122d45185cacadfbfa1e49bc74bad` | yes | Touched within 24 h |
| /work/worktrees/task-46541b3e | Succeeded | 2026-11-03T09:41:31.656611Z | 0.218 | 2026-11-04T23:49:04.694Z | 5.11 | `16e94bdf3d3691c4aaa63e69fd48263d4c2a79ab` | yes | Touched within 24 h |
| /work/worktrees/task-47d31366 | Succeeded | 2026-11-05T02:54:21.566748Z | 0.272 | 2026-11-05T01:21:42.240Z | 3.57 | `249d4654d2410c4a3959a492ee0165e8a75b75f9` | yes | Touched within 24 h |
| /work/worktrees/task-47f9be84 | Succeeded | 2026-11-04T05:08:52.128337Z | 1.834 | 2026-11-04T23:49:03.750Z | 5.11 | `3d2d5261aab5e1a2759f929d0ce37c17820e0abf` | yes | Touched within 24 h |
| /work/worktrees/task-4809596b | Succeeded | 2026-11-04T00:50:09.926302Z | 0.218 | 2026-11-04T23:49:00.609Z | 5.11 | `551d02699009822c4e3b4d59a393aed8ea728721` | yes | Touched within 24 h |
| /work/worktrees/task-48e15e43 | Succeeded | 2026-11-02T12:46:08.013351Z | 1.819 | 2026-11-04T23:49:02.654Z | 5.11 | `b032722199a0f32e9d03da52d3611351508dcc82` | yes | Touched within 24 h |
| /work/worktrees/task-4987d1e9 | Succeeded | 2026-11-04T02:04:07.798288Z | 0.219 | 2026-11-04T23:49:03.090Z | 5.11 | `679215d8fc3751f474171f23ab2b796d3caa5377` | yes | Touched within 24 h |
| /work/worktrees/task-4a75ce55 | Succeeded | 2026-11-04T09:03:06.654645Z | 0.220 | 2026-11-04T23:49:01.673Z | 5.11 | `a780da61e9af980af7605a5d7a5cd36ab7bc09c0` | yes | Touched within 24 h |
| /work/worktrees/task-4aa6e6f8 | Succeeded | 2026-11-02T12:48:17.789571Z | 1.819 | 2026-11-04T23:48:58.341Z | 5.11 | `d4345c226ecef44b3aca333ac3d56a4ed55d7f70` | yes | Touched within 24 h |
| /work/worktrees/task-4af5405a | Succeeded | 2026-11-04T11:50:02.980578Z | 0.220 | 2026-11-04T23:49:00.233Z | 5.11 | `8c9f808ab835d49f29c626502706bb6dcd218820` | yes | Touched within 24 h |
| /work/worktrees/task-4b2ef72c | Succeeded | 2026-11-03T08:42:00.246954Z | 0.217 | 2026-11-04T23:48:59.797Z | 5.11 | `53d8002188e39646801a6a29937b769fd7af0dc1` | yes | Touched within 24 h |
| /work/worktrees/task-4b605c04 | Succeeded | 2026-11-02T17:01:50.731558Z | 0.242 | 2026-11-04T23:49:02.410Z | 5.11 | `35da6d8cc83a6c256464713180dcbb940109f50c` | yes | Touched within 24 h |
| /work/worktrees/task-4b9d4604 | Succeeded | 2026-11-04T16:29:02.60242Z | 0.220 | 2026-11-04T23:49:03.574Z | 5.11 | `566605c092dfaed7f3ad0affb1f36aa2af39d2fc` | yes | Touched within 24 h |
| /work/worktrees/task-4c2d90db | Succeeded | 2026-11-04T11:57:28.267799Z | 0.220 | 2026-11-04T23:49:03.634Z | 5.11 | `b5f97136167d942484aa23ea8660cf50cc800eff` | yes | Touched within 24 h |
| /work/worktrees/task-4c956d48 | Succeeded | 2026-11-02T19:02:24.56439Z | 0.241 | 2026-11-04T23:48:59.993Z | 5.11 | `2aab4d4d07888e49d8ddcadaee75d58e29d6fc98` | yes | Touched within 24 h |
| /work/worktrees/task-4d4e6cda | Succeeded | 2026-11-03T09:25:23.002566Z | 0.217 | 2026-11-04T23:48:59.609Z | 5.11 | `46cb2960717363f6b62378a3940637d8e5398c1d` | yes | Touched within 24 h |
| /work/worktrees/task-4ed13efe | Succeeded | 2026-11-04T02:35:48.492794Z | 0.222 | 2026-11-04T23:48:58.305Z | 5.11 | `49417516e211643fa5d1bed5c5920a06dca6c4ef` | yes | Touched within 24 h |
| /work/worktrees/task-4f64e784 | Succeeded | 2026-11-03T18:25:52.548685Z | 0.218 | 2026-11-04T23:49:05.330Z | 5.11 | `4ca16a502dfaf16056d8ade00891f62ceac2ddfa` | yes | Touched within 24 h |
| /work/worktrees/task-506ad7e2 | Succeeded | 2026-11-02T16:30:51.283719Z | 1.823 | 2026-11-04T23:49:02.870Z | 5.11 | `09e96de2d48523e924e661434300ac548cfeb9cf` | yes | Touched within 24 h |
| /work/worktrees/task-516b3a0a | Succeeded | 2026-11-03T03:21:50.018505Z | 3.006 | 2026-11-04T23:49:01.085Z | 5.11 | `91aa1636816e64bf4cf08aa4b631207f0e9df47a` | yes | Touched within 24 h |
| /work/worktrees/task-52410842 | Succeeded | 2026-11-04T20:06:15.194981Z | 0.220 | 2026-11-04T23:48:59.545Z | 5.11 | `5233faefdedd2d024fcbbabeb02e1e9146b03a70` | yes | Touched within 24 h |
| /work/worktrees/task-5242d604 | Succeeded | 2026-11-05T02:23:25.95076Z | 0.272 | 2026-11-05T01:04:06.942Z | 3.86 | `96d7cde36af3868502900d77d5f8ab8fd7504941` | yes | Touched within 24 h |
| /work/worktrees/task-53edd261 | Succeeded | 2026-11-02T21:59:35.555703Z | 2.160 | 2026-11-04T23:49:01.249Z | 5.11 | `e2a886bfa26cedefc7f6f98e31fe52f9b8dceaf6` | yes | Touched within 24 h |
| /work/worktrees/task-54c47f10 | Succeeded | 2026-11-03T01:41:20.348125Z | 1.903 | 2026-11-04T23:49:02.990Z | 5.11 | `ad9bf0c14bb0248b6be070e6c9df509503e1b646` | yes | Touched within 24 h |
| /work/worktrees/task-551ea479 | Succeeded | 2026-11-02T16:04:55.782371Z | 1.900 | 2026-11-04T23:48:58.089Z | 5.11 | `a538f98b294d2f6f395345b325178cd65054b179` | yes | Touched within 24 h |
| /work/worktrees/task-55b2b668 | Succeeded | 2026-11-03T23:55:20.586292Z | 0.218 | 2026-11-04T23:48:58.029Z | 5.11 | `c0666337845eb7d08078384b5aa04300940b04df` | yes | Touched within 24 h |
| /work/worktrees/task-56b0f9aa | Succeeded | 2026-11-04T23:31:04.906508Z | 0.220 | 2026-11-04T23:48:59.389Z | 5.11 | `6f0e6129757909a8818b9e2e5f9d6d7b0f445e29` | yes | Touched within 24 h |
| /work/worktrees/task-56bf971d | Succeeded | 2026-11-04T14:05:39.569597Z | 0.220 | 2026-11-04T23:49:00.181Z | 5.11 | `7948e04a007aecdb05f90612a738191057299d2b` | yes | Touched within 24 h |
| /work/worktrees/task-56e7e9b7 | Succeeded | 2026-11-04T02:04:47.56609Z | 0.222 | 2026-11-04T23:48:58.849Z | 5.11 | `cd1697f3373ccd74e47f60d67e65441826e06f89` | yes | Touched within 24 h |
| /work/worktrees/task-577b2ee0 | Succeeded | 2026-11-04T18:36:03.999082Z | 0.220 | 2026-11-04T23:48:59.937Z | 5.11 | `3a03ed5a9cb5291f545090d0d9c88db1ce0f769d` | yes | Touched within 24 h |
| /work/worktrees/task-57e28053 | Succeeded | 2026-11-03T20:46:12.494339Z | 0.300 | 2026-11-04T23:49:01.733Z | 5.11 | `c2f1f83a30b324af81149918e8a6360565b95344` | yes | Touched within 24 h |
| /work/worktrees/task-5864e36c | Succeeded | 2026-11-02T13:33:43.64022Z | 1.820 | 2026-11-04T23:49:03.078Z | 5.11 | `c53d2583ccce9f4da7808846f7dadc44112016a3` | yes | Touched within 24 h |
| /work/worktrees/task-5a6278c8 | Succeeded | 2026-11-02T19:19:00.063415Z | 0.239 | 2026-11-04T23:49:06.246Z | 5.11 | `289eef0ba68c7a98e36e03576f1fca9174bbe571` | yes | Touched within 24 h |
| /work/worktrees/task-5a733e90 | Succeeded | 2026-11-03T23:38:39.256501Z | 0.218 | 2026-11-04T23:49:01.353Z | 5.11 | `b7ac06a864b9f8ff4d1e8c5ae7c7bb97c3420bc2` | yes | Touched within 24 h |
| /work/worktrees/task-5aaa94f1 | Succeeded | 2026-11-03T19:52:08.886057Z | 0.218 | 2026-11-04T23:49:06.110Z | 5.11 | `f422433cd3dc8843dffe34653076273bdbd00ab8` | yes | Touched within 24 h |
| /work/worktrees/task-5b21d0be | Succeeded | 2026-11-02T14:08:17.654971Z | 1.823 | 2026-11-04T23:49:02.914Z | 5.11 | `2db78f306716a62279880a030f4263911f877dbb` | yes | Touched within 24 h |
| /work/worktrees/task-5b7656a9 | Succeeded | 2026-11-03T09:00:26.049033Z | 0.217 | 2026-11-04T23:49:03.938Z | 5.11 | `7fcbc999eec061fccc9adb621f358537e0df1855` | yes | Touched within 24 h |
| /work/worktrees/task-5b84b043 | Succeeded | 2026-11-03T01:20:05.360569Z | 0.338 | 2026-11-04T23:49:02.690Z | 5.11 | `329ee2b50faa80def7cd56cb77e84b74c9119373` | yes | Touched within 24 h |
| /work/worktrees/task-5bdb0e6f | Succeeded | 2026-11-04T01:02:49.587032Z | 0.218 | 2026-11-04T23:49:01.853Z | 5.11 | `92a48622c5bd5d1826fb2b7e7ef1bc458beccd70` | yes | Touched within 24 h |
| /work/worktrees/task-5d97c7d9 | Succeeded | 2026-11-03T06:08:30.343143Z | 0.265 | 2026-11-04T23:49:03.538Z | 5.11 | `e1a7614f6605e5a6b6d84494bb5c95a111d92d53` | yes | Touched within 24 h |
| /work/worktrees/task-5e05fc80 | Succeeded | 2026-11-05T01:19:59.960059Z | 0.870 | 2026-11-05T01:01:36.188Z | 3.90 | `249d4654d2410c4a3959a492ee0165e8a75b75f9` | yes | Touched within 24 h |
| /work/worktrees/task-5e9340ec | Succeeded | 2026-11-03T19:19:42.691779Z | 0.218 | 2026-11-04T23:48:59.701Z | 5.11 | `5529ba181eaa5e54f09add8cd92d27c9719705cc` | yes | Touched within 24 h |
| /work/worktrees/task-5f63ea92 | Succeeded | 2026-11-03T17:24:33.992285Z | 0.223 | 2026-11-04T23:49:03.282Z | 5.11 | `c9ab2497cd6034385b4d168261f529feff1df954` | yes | Touched within 24 h |
| /work/worktrees/task-5fd6ac3e | Succeeded | 2026-11-02T19:19:42.504191Z | 1.824 | 2026-11-04T23:48:58.141Z | 5.11 | `0e88f5adba7249c356f3b06e1626a9250525156b` | yes | Touched within 24 h |
| /work/worktrees/task-5fefd110 | Succeeded | 2026-11-03T09:30:48.593553Z | 0.224 | 2026-11-04T23:48:57.601Z | 5.11 | `dd5837a40ecb438563dfb6d161909e02d433998e` | yes | Touched within 24 h |
| /work/worktrees/task-5ff161fd | Succeeded | 2026-11-02T16:05:39.262244Z | 1.826 | 2026-11-04T23:49:05.482Z | 5.11 | `09022f2fe0f16e82fa1bcd914fb9f428bd893eb7` | yes | Touched within 24 h |
| /work/worktrees/task-605c6f4e | Succeeded | 2026-11-05T01:56:28.746413Z | 0.622 | 2026-11-05T01:54:24.809Z | 3.02 | `24f1ea0abafa8a192dfb538b3fe4f5791f893f64` | yes | Touched within 24 h |
| /work/worktrees/task-606b7d8d | Succeeded | 2026-11-05T00:28:57.932823Z | 0.622 | 2026-11-05T00:27:55.461Z | 4.46 | `ecd249f36aaadfd71d0502a90715a50eb5d1d932` | yes | Touched within 24 h |
| /work/worktrees/task-60cb0a38 | Succeeded | 2026-11-04T09:20:42.183661Z | 0.223 | 2026-11-04T23:49:00.481Z | 5.11 | `1c8b553b7e1ac4a0f83290652a01c1146b39c248` | yes | Touched within 24 h |
| /work/worktrees/task-613f2230 | Succeeded | 2026-11-05T01:07:08.899512Z | 1.942 | 2026-11-05T01:06:33.272Z | 3.82 | `1dcad366c1da29fec39139eb22f96936f98d96d3` | yes | Touched within 24 h |
| /work/worktrees/task-62afb5fc | Failed | 2026-11-02T23:02:17.492345Z | 0.217 | 2026-11-04T23:49:02.249Z | 5.11 | `907edaebbd301af95629c31095f1b530803baea2` | yes | Touched within 24 h |
| /work/worktrees/task-632e079b | Succeeded | 2026-11-02T19:38:46.997784Z | 1.823 | 2026-11-04T23:48:58.009Z | 5.11 | `e2cfadf36b928ffd0b3b9bbc3d492e4e83befc7b` | yes | Touched within 24 h |
| /work/worktrees/task-6379c50f | Canceled | 2026-11-03T07:27:15.569528Z | 0.217 | 2026-11-04T23:49:00.525Z | 5.11 | `74178698ea316af2902c5ef653e8ecedecf3c698` | yes | Touched within 24 h |
| /work/worktrees/task-64fb3f5d | Canceled | 2026-11-05T04:39:09.23004Z | 1.943 | 2026-11-05T03:33:39.887Z | 1.37 | `fbbb008824a8a168113e3af87d8308e582721286` | yes | Touched within 24 h |
| /work/worktrees/task-654750f8 | Succeeded | 2026-11-02T21:18:57.839069Z | 2.159 | 2026-11-04T23:48:58.189Z | 5.11 | `7500c1ea01c0a00bfd47529c7f8788a35c4caacc` | yes | Touched within 24 h |
| /work/worktrees/task-6578802d | Succeeded | 2026-11-04T13:35:42.212501Z | 0.220 | 2026-11-04T23:49:01.565Z | 5.11 | `f93fdaf5ca3620acbc21cacad97dee797f9a5e95` | yes | Touched within 24 h |
| /work/worktrees/task-67640bd1 | Succeeded | 2026-11-02T21:00:54.376755Z | 0.221 | 2026-11-04T23:49:05.838Z | 5.11 | `c9c92e50b49ddc242e4d0dfc5d742242ad719e98` | yes | Touched within 24 h |
| /work/worktrees/task-67e63dbb | Failed | 2026-11-02T14:00:05.027381Z | 0.216 | 2026-11-04T23:49:02.602Z | 5.11 | `43ae7371d7e9e173e33b69efdc061ac2891c9d98` | yes | Touched within 24 h |
| /work/worktrees/task-684dc939 | Succeeded | 2026-11-04T02:11:25.07255Z | 0.243 | 2026-11-04T23:49:01.421Z | 5.11 | `026dca6a54dfe8a319aa0a7e9972f96f51ac0bed` | yes | Touched within 24 h |
| /work/worktrees/task-689df4fd | Succeeded | 2026-11-03T06:50:51.572361Z | 0.217 | 2026-11-04T23:48:59.589Z | 5.11 | `59a1e15dd1329df378ec5715766086244862a875` | yes | Touched within 24 h |
| /work/worktrees/task-692b70e4 | Succeeded | 2026-11-04T10:51:34.823164Z | 0.220 | 2026-11-04T23:49:04.190Z | 5.11 | `9bd9edbe010f894d1e30e292d3d9184797a55f56` | yes | Touched within 24 h |
| /work/worktrees/task-6949514d | Succeeded | 2026-11-04T16:15:50.0529Z | 0.220 | 2026-11-04T23:49:04.966Z | 5.11 | `87eff55f66e1a745d02d8d1906c95bce42988711` | yes | Touched within 24 h |
| /work/worktrees/task-6b6471d0 | Succeeded | 2026-11-03T22:10:33.311158Z | 0.219 | 2026-11-04T23:49:01.585Z | 5.11 | `f58111666ada41ad13ccf0d7f965ca312fba7902` | yes | Touched within 24 h |
| /work/worktrees/task-6c1b0f87 | Succeeded | 2026-11-04T19:02:25.624711Z | 0.220 | 2026-11-04T23:49:02.345Z | 5.11 | `95e80dde7576b1666fdc1c3c196d57216fbc32e3` | yes | Touched within 24 h |
| /work/worktrees/task-6c3a5d56 | Succeeded | 2026-11-03T21:06:17.046576Z | 0.224 | 2026-11-04T23:49:02.930Z | 5.11 | `508b5fda96abb63bc26bcd42433e636e14d958e5` | yes | Touched within 24 h |
| /work/worktrees/task-6c414224 | Succeeded | 2026-11-02T18:34:46.466814Z | 1.322 | 2026-11-04T23:49:03.298Z | 5.11 | `4d7169394d974205638bdca45b6e336715e241f3` | yes | Touched within 24 h |
| /work/worktrees/task-6c65bbc8 | Succeeded | 2026-11-04T03:45:47.091604Z | 0.218 | 2026-11-04T23:49:06.150Z | 5.11 | `cef79651f6c92bd4c19dca5c7c0e324163ecfa5e` | yes | Touched within 24 h |
| /work/worktrees/task-6db87da1 | Succeeded | 2026-11-03T22:44:51.124513Z | 0.227 | 2026-11-04T23:49:00.433Z | 5.11 | `13d5f15e0e307ef6fd308db3c3e0ab348e3b9b7a` | yes | Touched within 24 h |
| /work/worktrees/task-6dfad742 | Succeeded | 2026-11-04T21:06:18.547228Z | 0.220 | 2026-11-04T23:49:06.322Z | 5.11 | `4d30e022607354205a363bdaebacbefb78565704` | yes | Touched within 24 h |
| /work/worktrees/task-6e58ba06 | Succeeded | 2026-11-03T17:32:19.370329Z | 1.828 | 2026-11-04T23:49:02.474Z | 5.11 | `892db366c6232263e854b7bd510b557593e3c00d` | yes | Touched within 24 h |
| /work/worktrees/task-6f3ab7aa | Succeeded | 2026-11-03T09:04:39.736533Z | 0.218 | 2026-11-04T23:48:59.681Z | 5.11 | `bb0947982a3c170c14ee93d72b4a4f635436bb40` | yes | Touched within 24 h |
| /work/worktrees/task-6f66c43e | Succeeded | 2026-11-03T19:31:06.942542Z | 0.222 | 2026-11-04T23:48:58.733Z | 5.11 | `4f9999e70e74c693ddb40ce5b3690f75993d04f4` | yes | Touched within 24 h |
| /work/worktrees/task-6f817352 | Succeeded | 2026-11-02T17:39:25.105055Z | 1.823 | 2026-11-04T23:49:03.182Z | 5.11 | `f41d357de0a31bf2344cbed719de53bb206acd20` | yes | Touched within 24 h |
| /work/worktrees/task-6fd10cb5 | Succeeded | 2026-11-03T08:17:32.046146Z | 0.217 | 2026-11-04T23:48:59.805Z | 5.11 | `6bc6be278e8bf9f1353703e84d2f4928dc39250e` | yes | Touched within 24 h |
| /work/worktrees/task-6ff01870 | Succeeded | 2026-11-03T07:26:07.960039Z | 0.217 | 2026-11-04T23:49:03.594Z | 5.11 | `f547852cdcde0fad6708e036072171a8b0a0f16d` | yes | Touched within 24 h |
| /work/worktrees/task-6ffbb647 | Succeeded | 2026-11-03T22:10:23.882858Z | 0.218 | 2026-11-04T23:49:00.085Z | 5.11 | `1cdcbb408a51183fc0d1840c8a430cb8cd34e0ed` | yes | Touched within 24 h |
| /work/worktrees/task-7114858c | Succeeded | 2026-11-03T09:12:04.12211Z | 0.217 | 2026-11-04T23:48:57.809Z | 5.11 | `7fcbc999eec061fccc9adb621f358537e0df1855` | yes | Touched within 24 h |
| /work/worktrees/task-727a57dd | Dispatched | — | 2.310 | 2026-11-05T04:44:45.603Z | 0.18 | `4ef4cfd2c435aca17cbb0658dca95a27415a8bb3` | yes | Protected task: Dispatched |
| /work/worktrees/task-7289f7c7 | Succeeded | 2026-11-04T11:59:43.415145Z | 0.220 | 2026-11-04T23:49:01.105Z | 5.11 | `20046c38083e605fbe8eca9f8e93a3afb8b1ce20` | yes | Touched within 24 h |
| /work/worktrees/task-737b54c5 | Succeeded | 2026-11-02T19:18:14.682101Z | 2.850 | 2026-11-04T23:49:01.149Z | 5.11 | `4d7169394d974205638bdca45b6e336715e241f3` | yes | Touched within 24 h |
| /work/worktrees/task-73837e64 | Succeeded | 2026-11-04T11:16:18.767305Z | 0.223 | 2026-11-04T23:49:02.842Z | 5.11 | `398111a74eae9f5b57cdbc30cfd725b7ea5383c7` | yes | Touched within 24 h |
| /work/worktrees/task-73c027ae | Succeeded | 2026-11-05T02:57:37.808916Z | 0.851 | 2026-11-05T02:44:09.234Z | 2.19 | `f6f380c08e3468a5b98415e14f72380377fc6f6e` | yes | Touched within 24 h |
| /work/worktrees/task-73c94e0b | Succeeded | 2026-11-05T01:32:55.659035Z | 1.943 | 2026-11-05T01:12:52.935Z | 3.71 | `467b00c25ba04fb2a6cbe583079e06211c93c8a7` | yes | Touched within 24 h |
| /work/worktrees/task-7519a931 | Succeeded | 2026-11-03T02:38:59.093985Z | 1.902 | 2026-11-04T23:48:57.989Z | 5.11 | `0cab341017478283169d007bb6cd28cbe5e744a5` | yes | Touched within 24 h |
| /work/worktrees/task-75c9410c | Succeeded | 2026-11-04T12:26:56.578316Z | 0.220 | 2026-11-04T23:49:02.081Z | 5.11 | `36ba2a14f3cf62e3d56787b9b504b9e4d10ea60c` | yes | Touched within 24 h |
| /work/worktrees/task-78e606d5 | Succeeded | 2026-11-04T02:40:04.456565Z | 1.830 | 2026-11-04T23:49:01.305Z | 5.11 | `1c263f03f8d1ec55994ed20bb5f70e879720d226` | yes | Touched within 24 h |
| /work/worktrees/task-79245209 | Succeeded | 2026-11-03T10:28:20.790915Z | 0.218 | 2026-11-04T23:49:04.130Z | 5.11 | `9d8260fa24716e73d7c60a18ecf14f694933aaac` | yes | Touched within 24 h |
| /work/worktrees/task-7982c828 | Succeeded | 2026-11-03T19:40:01.0376Z | 0.218 | 2026-11-04T23:49:05.234Z | 5.11 | `f6c15619feeb8d08de8014f858debae2c9e32569` | yes | Touched within 24 h |
| /work/worktrees/task-79d3d1ba | Succeeded | 2026-11-03T17:57:49.048254Z | 0.218 | 2026-11-04T23:49:01.061Z | 5.11 | `e56e4d1b6a727b3127b473307b860b7117980f09` | yes | Touched within 24 h |
| /work/worktrees/task-79f6a820 | Succeeded | 2026-11-03T02:45:54.975683Z | 1.902 | 2026-11-04T23:49:04.530Z | 5.11 | `3aadf16ed2f288c1381f52ee2a2dac91fa26c798` | yes | Touched within 24 h |
| /work/worktrees/task-7ab0d65a | Succeeded | 2026-11-04T13:10:09.127658Z | 0.220 | 2026-11-04T23:49:01.485Z | 5.11 | `b2b32af452e65146d240285dff913bfdc0a7f944` | yes | Touched within 24 h |
| /work/worktrees/task-7b966444 | Succeeded | 2026-11-03T03:12:09.838633Z | 0.223 | 2026-11-04T23:49:01.445Z | 5.11 | `93f10fbb7934bf0652d612e142143e74ab8a6bc3` | yes | Touched within 24 h |
| /work/worktrees/task-7c02ed68 | Succeeded | 2026-11-03T10:01:27.49753Z | 0.218 | 2026-11-04T23:49:01.613Z | 5.11 | `2d9cd9232ac668b23f1c82721ed630029499b04a` | yes | Touched within 24 h |
| /work/worktrees/task-7c150718 | Succeeded | 2026-11-03T01:27:03.315641Z | 1.824 | 2026-11-04T23:49:04.090Z | 5.11 | `0d2bc0cd6ac2cc9cc71acc12c5a136bf537fcc98` | yes | Touched within 24 h |
| /work/worktrees/task-7c36e01b | Succeeded | 2026-11-03T23:35:18.780993Z | 0.226 | 2026-11-04T23:49:00.197Z | 5.11 | `c052585d9b43de14ecc28015a19652fc31b92e55` | yes | Touched within 24 h |
| /work/worktrees/task-7c7132b3 | Failed | 2026-11-04T23:34:09.674506Z | 0.220 | 2026-11-04T23:49:02.634Z | 5.11 | `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c` | yes | Touched within 24 h |
| /work/worktrees/task-7d7761ce | Succeeded | 2026-11-02T13:07:02.878917Z | 0.313 | 2026-11-04T23:49:03.562Z | 5.11 | `2b1e7494a9b100982e4e1f95a1be622fb8e918f9` | yes | Touched within 24 h |
| /work/worktrees/task-7dfff223 | Succeeded | 2026-11-03T09:14:54.564749Z | 0.217 | 2026-11-04T23:48:59.857Z | 5.11 | `92c8f72fef700f961e151061d888edbea7d9fbca` | yes | Touched within 24 h |
| /work/worktrees/task-7eb005f3 | Succeeded | 2026-11-04T09:32:38.360261Z | 0.220 | 2026-11-04T23:49:06.222Z | 5.11 | `aea4a99b9c00d22a42d0b325559d68064e8a8842` | yes | Touched within 24 h |
| /work/worktrees/task-7f1d7c3b | Succeeded | 2026-11-02T18:22:37.312688Z | 1.856 | 2026-11-04T23:49:02.862Z | 5.11 | `3a4f73374a027204ddd79b32070c5f8ebcd44222` | yes | Touched within 24 h |
| /work/worktrees/task-7f675cf6 | Succeeded | 2026-11-04T15:52:26.993266Z | 0.220 | 2026-11-04T23:48:58.445Z | 5.11 | `b2c03db92e00fb09c2aac487155a4abae4967621` | yes | Touched within 24 h |
| /work/worktrees/task-80170e29 | Succeeded | 2026-11-03T08:43:35.224751Z | 0.217 | 2026-11-04T23:49:03.026Z | 5.11 | `c52a96a026bf321c7a3866cb442297be54b17fb9` | yes | Touched within 24 h |
| /work/worktrees/task-801ad6c3 | Succeeded | 2026-11-04T17:52:17.687551Z | 0.220 | 2026-11-04T23:49:04.858Z | 5.11 | `a3cd0fe6b0627d6141f0ede4c6528709d15bd6bf` | yes | Touched within 24 h |
| /work/worktrees/task-80663342 | Succeeded | 2026-11-02T19:18:41.834446Z | 0.767 | 2026-11-04T23:49:05.874Z | 5.11 | `dc8baf408cdddcc6892f82586a7157e660c19301` | yes | Touched within 24 h |
| /work/worktrees/task-815b655c | Succeeded | 2026-11-02T13:08:12.419731Z | 2.797 | 2026-11-04T23:49:04.890Z | 5.11 | `fcf45564cddb49592de17b75f29bdb6d835b511c` | yes | Touched within 24 h |
| /work/worktrees/task-81e40a23 | Succeeded | 2026-11-02T14:25:03.986933Z | 1.819 | 2026-11-04T23:49:01.757Z | 5.11 | `87f4eeabe3ae24b1c24e27ab91bb555a70fc748d` | yes | Touched within 24 h |
| /work/worktrees/task-81ea09a8 | Succeeded | 2026-11-03T17:57:45.97123Z | 0.218 | 2026-11-04T23:49:04.678Z | 5.11 | `1d7a353970d0d67f49370d58d764949921a7219d` | yes | Touched within 24 h |
| /work/worktrees/task-82074cc2 | Succeeded | 2026-11-03T02:47:50.083147Z | 1.837 | 2026-11-04T23:49:02.910Z | 5.11 | `c6b35eb1b247eca61a98e22480239b5262dc32c4` | yes | Touched within 24 h |
| /work/worktrees/task-821a7c5b | Succeeded | 2026-11-04T14:59:46.799557Z | 0.220 | 2026-11-04T23:49:03.854Z | 5.11 | `2e07f8d38bf520817bc25f317835831141825c69` | yes | Touched within 24 h |
| /work/worktrees/task-8225fae1 | Succeeded | 2026-11-04T01:15:43.273151Z | 0.219 | 2026-11-04T23:49:03.166Z | 5.11 | `a823fb360c6b4a544824350b3dce8c4d3030052c` | yes | Touched within 24 h |
| /work/worktrees/task-83096283 | Succeeded | 2026-11-02T13:08:09.426902Z | 1.819 | 2026-11-04T23:48:58.517Z | 5.11 | `99e618ea848677cf08b6cdfeb7ed516fc415a75c` | yes | Touched within 24 h |
| /work/worktrees/task-833a515a | Succeeded | 2026-11-03T10:57:44.229509Z | 0.218 | 2026-11-04T23:48:59.133Z | 5.11 | `37a427aaedc43ebbd59de1aae26ccb7f45f79746` | yes | Touched within 24 h |
| /work/worktrees/task-833bd9b4 | Succeeded | 2026-11-03T20:42:14.891796Z | 0.220 | 2026-11-04T23:48:58.273Z | 5.11 | `41841661c9c0efd53b684cdf4e6a633c9ec89015` | yes | Touched within 24 h |
| /work/worktrees/task-8347d02a | Succeeded | 2026-11-04T10:23:06.711342Z | 0.220 | 2026-11-04T23:49:04.758Z | 5.11 | `e0863fc727bd18af87eba2776aef24ae375fa1b1` | yes | Touched within 24 h |
| /work/worktrees/task-84692314 | Succeeded | 2026-11-04T00:30:00.287035Z | 0.219 | 2026-11-04T23:49:03.006Z | 5.11 | `f66bfdd42b7c07777d48d6dc750309af2795dce9` | yes | Touched within 24 h |
| /work/worktrees/task-84b44899 | Succeeded | 2026-11-04T10:04:44.991758Z | 0.220 | 2026-11-04T23:49:00.809Z | 5.11 | `10e7a2db37b6242ee3cd0fcb424ab3e691506461` | yes | Touched within 24 h |
| /work/worktrees/task-84c4fa59 | Succeeded | 2026-11-03T20:43:11.911846Z | 0.218 | 2026-11-04T23:49:03.214Z | 5.11 | `19660b25260af3c7ef02f87f987b332bcbd7912a` | yes | Touched within 24 h |
| /work/worktrees/task-84d0a026 | Succeeded | 2026-11-03T20:21:00.412105Z | 0.218 | 2026-11-04T23:48:59.641Z | 5.11 | `ed67056c5e82389e95fdb82932f67396195a89f3` | yes | Touched within 24 h |
| /work/worktrees/task-8592d536 | Canceled | 2026-11-05T04:39:10.842255Z | 1.941 | 2026-11-05T03:27:40.781Z | 1.47 | `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` | yes | Touched within 24 h |
| /work/worktrees/task-86a12341 | Succeeded | 2026-11-04T01:37:13.53805Z | 0.219 | 2026-11-04T23:48:59.969Z | 5.11 | `ba6a84d3648e2e9f379cbdb034ddc0744999bd34` | yes | Touched within 24 h |
| /work/worktrees/task-86a941a3 | Succeeded | 2026-11-05T02:47:29.598881Z | 0.869 | 2026-11-05T02:45:49.126Z | 2.17 | `8f86f2c7a49149e0f60d5a1167563495465ee534` | yes | Touched within 24 h |
| /work/worktrees/task-8719af3e | Succeeded | 2026-11-04T14:04:10.714218Z | 0.220 | 2026-11-04T23:48:58.745Z | 5.11 | `76e953e2b95ad87f20359a0d2b986ae887ec6f5c` | yes | Touched within 24 h |
| /work/worktrees/task-87232051 | Succeeded | 2026-11-04T12:53:09.097888Z | 0.220 | 2026-11-04T23:49:03.482Z | 5.11 | `4c12963577c8b9bca176d76609f942475237cfc5` | yes | Touched within 24 h |
| /work/worktrees/task-87cbe3ef | Succeeded | 2026-11-03T02:35:42.869219Z | 0.330 | 2026-11-04T23:49:03.370Z | 5.11 | `d9d9b217a913cded71e72b277d344da06591c81b` | yes | Touched within 24 h |
| /work/worktrees/task-87d98f50 | Succeeded | 2026-11-03T09:38:54.243635Z | 0.218 | 2026-11-04T23:49:01.217Z | 5.11 | `a2584ef6e30029cae4332aec469ada1e9e95c729` | yes | Touched within 24 h |
| /work/worktrees/task-880edccc | Canceled | 2026-11-05T04:40:34.107694Z | 0.641 | 2026-11-04T23:53:23.268Z | 5.04 | `595c46a4ebfc7ea391dd65c478213844f5181407` | yes | Touched within 24 h |
| /work/worktrees/task-88cb4849 | Succeeded | 2026-11-02T21:51:34.38406Z | 1.825 | 2026-11-04T23:49:02.622Z | 5.11 | `df823414b9536f4b277e8d7a109ae7901be692c1` | yes | Touched within 24 h |
| /work/worktrees/task-894ccf98 | Succeeded | 2026-11-03T17:13:35.407684Z | 0.218 | 2026-11-04T23:49:03.390Z | 5.11 | `c04630cd75d0fc00431478b72cd47ed1923c4365` | yes | Touched within 24 h |
| /work/worktrees/task-89cb5516 | Canceled | 2026-11-02T22:05:54.160106Z | 0.217 | 2026-11-04T23:49:00.681Z | 5.11 | `359bd5d489c861c0936b477964c04dd80233fb13` | yes | Touched within 24 h |
| /work/worktrees/task-8a3a40d6 | Succeeded | 2026-11-03T01:35:29.006135Z | 0.316 | 2026-11-04T23:49:00.329Z | 5.11 | `3d96c6a27ef4d23de5f5b8fb3121aaa95a44c027` | yes | Touched within 24 h |
| /work/worktrees/task-8ac2299c | Succeeded | 2026-11-04T18:11:12.052107Z | 0.220 | 2026-11-04T23:49:04.038Z | 5.11 | `83a43ddec29fa4031394806df14e25ab05d27239` | yes | Touched within 24 h |
| /work/worktrees/task-8acb8a4d | Succeeded | 2026-11-04T09:06:39.686569Z | 0.220 | 2026-11-04T23:49:00.373Z | 5.11 | `ba313ea3c764e08c15c69d514b7aa2d8eeaa3e1a` | yes | Touched within 24 h |
| /work/worktrees/task-8b043bee | Succeeded | 2026-11-03T07:09:06.986856Z | 0.220 | 2026-11-04T23:49:03.730Z | 5.11 | `88dcc247087b1e0515aa2471f7865a5b1f7441c6` | yes | Touched within 24 h |
| /work/worktrees/task-8b317bb8 | Succeeded | 2026-11-02T21:39:52.933927Z | 3.007 | 2026-11-04T23:48:58.977Z | 5.11 | `bac78948348cbf690d9742edfb0651212e215868` | yes | Touched within 24 h |
| /work/worktrees/task-8b609b8a | Succeeded | 2026-11-04T18:50:07.272716Z | 0.220 | 2026-11-04T23:48:58.593Z | 5.11 | `d19fa358334dd37861cdc6966291f2a50508c5af` | yes | Touched within 24 h |
| /work/worktrees/task-8b9a4097 | Succeeded | 2026-11-03T16:51:18.287898Z | 0.218 | 2026-11-04T23:49:00.733Z | 5.11 | `c320980084484b74c9c9a301cabb0bd7c9b09db9` | yes | Touched within 24 h |
| /work/worktrees/task-8bc75abf | Succeeded | 2026-11-03T23:37:16.124128Z | 0.218 | 2026-11-04T23:48:58.661Z | 5.11 | `622bce61318f60fd615a1e1839a8fa59361080a8` | yes | Touched within 24 h |
| /work/worktrees/task-8bd5eef3 | Succeeded | 2026-11-03T01:12:59.721561Z | 1.902 | 2026-11-04T23:48:58.821Z | 5.11 | `0cab341017478283169d007bb6cd28cbe5e744a5` | yes | Touched within 24 h |
| /work/worktrees/task-8c49a43b | Succeeded | 2026-11-03T21:03:46.312301Z | 0.218 | 2026-11-04T23:49:02.742Z | 5.11 | `4b34692d0fb3a5c3206448259729e5deedfcd44c` | yes | Touched within 24 h |
| /work/worktrees/task-8e38d51f | Succeeded | 2026-11-04T04:30:36.815011Z | 0.222 | 2026-11-04T23:49:04.574Z | 5.11 | `3749c84334afb2fadc8ad345402db9fad79e11a8` | yes | Touched within 24 h |
| /work/worktrees/task-8f51d41b | Succeeded | 2026-11-03T23:10:39.668476Z | 0.218 | 2026-11-04T23:49:05.970Z | 5.11 | `622bce61318f60fd615a1e1839a8fa59361080a8` | yes | Touched within 24 h |
| /work/worktrees/task-8faaee74 | Succeeded | 2026-11-02T13:31:11.584393Z | 0.239 | 2026-11-04T23:48:58.757Z | 5.11 | `4a0abd68507a5e3105bf7b258849b1abb033890a` | yes | Touched within 24 h |
| /work/worktrees/task-90e0bae3 | Succeeded | 2026-11-03T15:13:33.687997Z | 0.215 | 2026-11-04T23:49:02.386Z | 5.11 | `679b696bd178e3c8bb14b32fcd04a1c26b148435` | yes | Touched within 24 h |
| /work/worktrees/task-92047d67 | Succeeded | 2026-11-02T21:26:35.581313Z | 0.316 | 2026-11-04T23:49:01.777Z | 5.11 | `fe2b5e418dda1ff93772dc6d64ffa1ece9ae6ec9` | yes | Touched within 24 h |
| /work/worktrees/task-927b4796 | Succeeded | 2026-11-04T03:05:55.248232Z | 0.230 | 2026-11-04T23:49:05.030Z | 5.11 | `cef79651f6c92bd4c19dca5c7c0e324163ecfa5e` | yes | Touched within 24 h |
| /work/worktrees/task-92a2c7bb | Succeeded | 2026-11-02T13:09:49.995011Z | 1.824 | 2026-11-04T23:48:59.757Z | 5.11 | `3c66ddd5465007908f6d752aea55a829205c0ac8` | yes | Touched within 24 h |
| /work/worktrees/task-92fb5616 | Succeeded | 2026-11-02T17:27:28.412659Z | 1.826 | 2026-11-04T23:48:59.109Z | 5.11 | `fc8c91ee7c21d14dc485ac69b2a8d82662880a70` | yes | Touched within 24 h |
| /work/worktrees/task-931a28cf | Succeeded | 2026-11-04T16:33:52.033829Z | 0.220 | 2026-11-04T23:49:04.882Z | 5.11 | `45d7c050cf10fca49a6b2d4062baffb0a062f8c9` | yes | Touched within 24 h |
| /work/worktrees/task-9360f65d | Succeeded | 2026-11-03T08:59:57.752725Z | 0.217 | 2026-11-04T23:49:06.266Z | 5.11 | `62beb934d40f616d8a3e26482fb1a3f7deb00154` | yes | Touched within 24 h |
| /work/worktrees/task-93a16649 | Succeeded | 2026-11-05T02:16:14.76859Z | 0.220 | 2026-11-05T01:39:19.169Z | 3.27 | `e76378e45adf6d8f8dcb92af1adf17b345ad4f84` | yes | Touched within 24 h |
| /work/worktrees/task-9478d178 | Succeeded | 2026-11-04T01:47:46.204579Z | 0.219 | 2026-11-04T23:49:02.710Z | 5.11 | `c3be0bc55c94c0217472458107dcdf738e6b4b2c` | yes | Touched within 24 h |
| /work/worktrees/task-9602645b | Succeeded | 2026-11-04T19:45:37.890766Z | 0.220 | 2026-11-04T23:49:00.769Z | 5.11 | `7ec786aa187668f250364ce294f3138d73c894d5` | yes | Touched within 24 h |
| /work/worktrees/task-9688c1d8 | Succeeded | 2026-11-03T02:33:13.120051Z | 1.825 | 2026-11-04T23:49:03.682Z | 5.11 | `70ce0759ae213a91418f10f56479d0e567830f4f` | yes | Touched within 24 h |
| /work/worktrees/task-96f2fc86 | Succeeded | 2026-11-04T07:45:57.191355Z | 0.220 | 2026-11-04T23:49:01.465Z | 5.11 | `3ebd2ad1b28b66a3209b277163d0bbc7833724cc` | yes | Touched within 24 h |
| /work/worktrees/task-977203e7 | Succeeded | 2026-11-04T18:49:10.1203Z | 0.220 | 2026-11-04T23:49:05.406Z | 5.11 | `96c12d6336d6942baa604b5aa10748f57f14f8bb` | yes | Touched within 24 h |
| /work/worktrees/task-97a782cd | Succeeded | 2026-11-02T20:54:58.860987Z | 1.824 | 2026-11-04T23:48:58.249Z | 5.11 | `a621a6944bd9961bc51fb77d6c0a299e21ee38fb` | yes | Touched within 24 h |
| /work/worktrees/task-97d93984 | Succeeded | 2026-11-03T17:40:45.490882Z | 1.828 | 2026-11-04T23:49:05.086Z | 5.11 | `892db366c6232263e854b7bd510b557593e3c00d` | yes | Touched within 24 h |
| /work/worktrees/task-9843b768 | Succeeded | 2026-11-04T06:09:39.750903Z | 0.219 | 2026-11-04T23:49:02.422Z | 5.11 | `e59406e1f995f4ab677dd793ade7bae789fde491` | yes | Touched within 24 h |
| /work/worktrees/task-988017cf | Succeeded | 2026-11-03T05:43:01.175094Z | 0.217 | 2026-11-04T23:49:03.198Z | 5.11 | `958af85ee6069a204e827074f7f05fcb8c4d371d` | yes | Touched within 24 h |
| /work/worktrees/task-99e23919 | Succeeded | 2026-11-04T21:37:48.271442Z | 0.220 | 2026-11-04T23:49:05.746Z | 5.11 | `b318fde5e924371dbee6ed3c6fc7da28941b54df` | yes | Touched within 24 h |
| /work/worktrees/task-9ac68027 | Succeeded | 2026-11-04T00:07:28.977996Z | 0.230 | 2026-11-04T23:49:05.654Z | 5.11 | `b33cf1b2caa0aec3eb1d65a87a935680e82b6ee2` | yes | Touched within 24 h |
| /work/worktrees/task-9aef59e9 | Succeeded | 2026-11-02T15:16:57.873733Z | 1.822 | 2026-11-04T23:49:02.165Z | 5.11 | `7a6bf8ddfca056bb47b623724d6b111d58edcb05` | yes | Touched within 24 h |
| /work/worktrees/task-9b6f690f | Succeeded | 2026-11-03T21:48:09.293193Z | 0.224 | 2026-11-04T23:49:06.078Z | 5.11 | `13a36c7eac0ba1b148d151fa17e00dd0d123359e` | yes | Touched within 24 h |
| /work/worktrees/task-9da7d9cf | Succeeded | 2026-11-02T17:24:35.548583Z | 0.251 | 2026-11-04T23:49:05.514Z | 5.11 | `269daf8fee45081464df99b122b845ae1c4d1b13` | yes | Touched within 24 h |
| /work/worktrees/task-9e9cf510 | Succeeded | 2026-11-03T17:20:08.808823Z | 0.218 | 2026-11-04T23:48:58.777Z | 5.11 | `4282088c9fb633f0ece763a8123d1e379666c536` | yes | Touched within 24 h |
| /work/worktrees/task-9ee6f8ca | Succeeded | 2026-11-03T22:17:56.27393Z | 0.227 | 2026-11-04T23:49:03.146Z | 5.11 | `f1d72c4f9f8f2040dbe93b3f453d2aad7d45af2a` | yes | Touched within 24 h |
| /work/worktrees/task-9f29e134 | Succeeded | 2026-11-03T23:04:28.365181Z | 0.218 | 2026-11-04T23:49:02.121Z | 5.11 | `01e0a62045111ea390a8865fd28bd8c52c08bd40` | yes | Touched within 24 h |
| /work/worktrees/task-9f577891 | Succeeded | 2026-11-04T20:14:27.33127Z | 0.220 | 2026-11-04T23:49:05.178Z | 5.11 | `abbda246b055dfebc4503862f53c69d8448b29ad` | yes | Touched within 24 h |
| /work/worktrees/task-9f89bb21 | Succeeded | 2026-11-02T20:42:47.455781Z | 0.348 | 2026-11-04T23:49:04.478Z | 5.11 | `3ab6fc3840693e96b1a7a11ad24324dc3c5435ee` | yes | Touched within 24 h |
| /work/worktrees/task-a14e0f34 | Succeeded | 2026-11-03T10:39:59.305665Z | 0.218 | 2026-11-04T23:49:04.166Z | 5.11 | `9d8260fa24716e73d7c60a18ecf14f694933aaac` | yes | Touched within 24 h |
| /work/worktrees/task-a1fb2dc7 | Succeeded | 2026-11-04T00:34:50.395585Z | 0.218 | 2026-11-04T23:48:59.569Z | 5.11 | `81e732dd42252ae1314840a87c98603faeeca25f` | yes | Touched within 24 h |
| /work/worktrees/task-a22bdfed | Succeeded | 2026-11-04T14:47:13.885441Z | 0.221 | 2026-11-04T23:49:05.738Z | 5.11 | `28f2c5302ea6785bd5809fa9d3efe1dd91beab2f` | yes | Touched within 24 h |
| /work/worktrees/task-a382d6ef | Succeeded | 2026-11-03T06:15:48.076479Z | 0.217 | 2026-11-04T23:49:00.137Z | 5.11 | `15f1bfffeedbf2ff0046e14f2c501925c3f534a4` | yes | Touched within 24 h |
| /work/worktrees/task-a3ce2552 | Succeeded | 2026-11-02T23:08:36.138708Z | 0.217 | 2026-11-04T23:48:59.261Z | 5.11 | `24def89c7f44c3f56176539c39c7e11818b5c161` | yes | Touched within 24 h |
| /work/worktrees/task-a3ff6d0b | Succeeded | 2026-11-03T00:35:02.682235Z | 1.828 | 2026-11-04T23:49:00.541Z | 5.11 | `4c399355c9e12791f6e50bb2641eccae7617b6ea` | yes | Touched within 24 h |
| /work/worktrees/task-a41890aa | Succeeded | 2026-11-04T15:39:03.40971Z | 0.220 | 2026-11-04T23:49:05.786Z | 5.11 | `3b5001ee55988ba43fb4a1543e4a184866b03e52` | yes | Touched within 24 h |
| /work/worktrees/task-a42d888f | Succeeded | 2026-11-04T13:19:18.360418Z | 0.220 | 2026-11-04T23:49:03.890Z | 5.11 | `da0106cbe7c500bade6659d4700b4a6cabe2b148` | yes | Touched within 24 h |
| /work/worktrees/task-a437c696 | Succeeded | 2026-11-03T09:26:03.01081Z | 0.217 | 2026-11-04T23:48:59.721Z | 5.11 | `f83e656f3960eeab62de886fa68dbe6d878154cb` | yes | Touched within 24 h |
| /work/worktrees/task-a45306f3 | Succeeded | 2026-11-03T23:31:46.74627Z | 0.218 | 2026-11-04T23:48:58.577Z | 5.11 | `639f78a890f35093fe8366a6d2881b1dc649f005` | yes | Touched within 24 h |
| /work/worktrees/task-a494d175 | Succeeded | 2026-11-02T17:20:01.697436Z | 1.823 | 2026-11-04T23:48:57.733Z | 5.11 | `f41d357de0a31bf2344cbed719de53bb206acd20` | yes | Touched within 24 h |
| /work/worktrees/task-a4a77bb8 | Succeeded | 2026-11-03T03:04:48.735434Z | 2.160 | 2026-11-04T23:49:06.126Z | 5.11 | `70ce0759ae213a91418f10f56479d0e567830f4f` | yes | Touched within 24 h |
| /work/worktrees/task-a549ddb0 | Succeeded | 2026-11-02T12:50:44.969814Z | 0.313 | 2026-11-04T23:49:05.894Z | 5.11 | `cabedfbb3c31fa6275c2952330becd5d835b73a1` | yes | Touched within 24 h |
| /work/worktrees/task-a5f3d226 | Succeeded | 2026-11-03T10:15:25.106597Z | 0.218 | 2026-11-04T23:49:06.050Z | 5.11 | `b2ea37b4d981a2c75c8f97fd2c3e3475a8fbdcbb` | yes | Touched within 24 h |
| /work/worktrees/task-a695169d | Succeeded | 2026-11-04T13:33:37.546911Z | 0.229 | 2026-11-04T23:49:03.790Z | 5.11 | `0236351d5b57bf3b2d083a24751adaf62bcff439` | yes | Touched within 24 h |
| /work/worktrees/task-a6c5c347 | Succeeded | 2026-11-04T19:06:17.129553Z | 0.220 | 2026-11-04T23:49:05.986Z | 5.11 | `84e19071e25abc7a595fddfac145b187dc5f2d11` | yes | Touched within 24 h |
| /work/worktrees/task-a7108dd6 | Succeeded | 2026-11-02T15:49:18.47443Z | 0.243 | 2026-11-04T23:49:05.042Z | 5.11 | `9b93c143ace45df5d03580c1985337807d7dc9b8` | yes | Touched within 24 h |
| /work/worktrees/task-a753db19 | Succeeded | 2026-11-03T23:07:48.067045Z | 0.221 | 2026-11-04T23:49:05.154Z | 5.11 | `a42b70583bb57e1557a6b51878d1eb74ee6a03e3` | yes | Touched within 24 h |
| /work/worktrees/task-a7de4ea4 | Succeeded | 2026-11-02T22:17:38.978371Z | 1.824 | 2026-11-04T23:49:05.450Z | 5.11 | `6d268b2b887dc6311787180e61d0db597f41dc86` | yes | Touched within 24 h |
| /work/worktrees/task-a8bd6706 | Succeeded | 2026-11-02T17:38:31.927724Z | 2.723 | 2026-11-04T23:49:03.134Z | 5.11 | `9be1461ce11311b2083bfec05c125cc5f2cbb6fa` | yes | Touched within 24 h |
| /work/worktrees/task-a921ab9d | Succeeded | 2026-11-03T11:14:04.778922Z | 0.218 | 2026-11-04T23:49:05.770Z | 5.11 | `b2d974a9ca92f2fbaac6f2ff494c1359697fa3bf` | yes | Touched within 24 h |
| /work/worktrees/task-a97c9731 | Succeeded | 2026-11-03T21:32:44.407198Z | 0.218 | 2026-11-04T23:49:03.990Z | 5.11 | `77262cf7254b8ae0c4df91772e959c40c759e17e` | yes | Touched within 24 h |
| /work/worktrees/task-a9f45276 | Succeeded | 2026-11-03T22:45:08.005675Z | 0.221 | 2026-11-04T23:49:00.937Z | 5.11 | `babc58867db8772721a13a8d3be6f45ab47ff47a` | yes | Touched within 24 h |
| /work/worktrees/task-aa58fb53 | Failed | 2026-11-04T09:28:49.529577Z | 0.220 | 2026-11-04T23:49:03.846Z | 5.11 | `dd30b167ac522e5462f52f3307ef861e648fb0bf` | yes | Touched within 24 h |
| /work/worktrees/task-ab00c63e | Succeeded | 2026-11-02T16:31:30.950306Z | 1.823 | 2026-11-04T23:49:04.370Z | 5.11 | `dae23c2dbfe58053c20af3fb7a2b869fc2061046` | yes | Touched within 24 h |
| /work/worktrees/task-ab6cf694 | Succeeded | 2026-11-01T20:26:17.285445Z | 0.403 | 2026-11-04T23:49:02.514Z | 5.11 | `c6d739ac8766369bd95bfd611ec1033567bc6bcd` | yes | Touched within 24 h |
| /work/worktrees/task-ac3dd93d | Succeeded | 2026-11-02T20:23:22.173338Z | 3.006 | 2026-11-04T23:49:02.325Z | 5.11 | `2d523e7eb7cd76e4438a12e8c2b27ed0bfc2b6ab` | yes | Touched within 24 h |
| /work/worktrees/task-acd85970 | Succeeded | 2026-11-04T00:41:48.639686Z | 0.218 | 2026-11-04T23:48:58.953Z | 5.11 | `47a44f52053b8e330782e0ed9a6932c93f7239f5` | yes | Touched within 24 h |
| /work/worktrees/task-ace48db2 | Succeeded | 2026-11-02T21:39:43.341129Z | 0.217 | 2026-11-04T23:48:57.721Z | 5.11 | `dc1cbc7a3b5233b9efccd971203b8598a9fec3a4` | yes | Touched within 24 h |
| /work/worktrees/task-ad29ef52 | Succeeded | 2026-11-03T00:52:58.863107Z | 0.316 | 2026-11-04T23:49:05.382Z | 5.11 | `36c8287552141a4a15df7c7bc7864d6eb3955a1a` | yes | Touched within 24 h |
| /work/worktrees/task-adbd81fe | Succeeded | 2026-11-04T23:50:30.579337Z | 0.220 | 2026-11-04T23:49:00.153Z | 5.11 | `d95d3f2cdce02fd95bf2145ecee979769cfc4c3c` | yes | Touched within 24 h |
| /work/worktrees/task-ae6457ac | Succeeded | 2026-11-04T21:20:48.292317Z | 0.220 | 2026-11-04T23:49:02.181Z | 5.11 | `240231e86c90707cd6e0d0c88affcd5bddfb4247` | yes | Touched within 24 h |
| /work/worktrees/task-aea81717 | Succeeded | 2026-11-02T14:10:30.57406Z | 1.823 | 2026-11-04T23:48:58.625Z | 5.11 | `e660864f969d39f63124307bc0fca4a30ec05ed0` | yes | Touched within 24 h |
| /work/worktrees/task-aeecbb03 | Succeeded | 2026-11-03T03:10:07.074575Z | 1.825 | 2026-11-04T23:49:01.281Z | 5.11 | `7f12e2d1bbbf1a6451e22346b49f9c92ac368e62` | yes | Touched within 24 h |
| /work/worktrees/task-b02344a7 | Failed | 2026-11-03T10:42:12.263009Z | 0.217 | 2026-11-04T23:48:59.093Z | 5.11 | `4b1fbc955749b28eeb51f6e1c18e474e17c78df1` | yes | Touched within 24 h |
| /work/worktrees/task-b02d7898 | Succeeded | 2026-11-04T18:07:43.604938Z | 0.220 | 2026-11-04T23:48:59.409Z | 5.11 | `a01d907dc2cfaf32643afc07e944d59391fbf93f` | yes | Touched within 24 h |
| /work/worktrees/task-b054e10c | Succeeded | 2026-11-04T07:38:56.619419Z | 0.220 | 2026-11-04T23:49:01.941Z | 5.11 | `d960340b5480ba0a1aa4ee15cc45ab81d3000286` | yes | Touched within 24 h |
| /work/worktrees/task-b0bdc516 | Succeeded | 2026-11-03T22:07:39.865152Z | 0.218 | 2026-11-04T23:49:04.322Z | 5.11 | `cc7fc10615158d9d44fd82fe41c2c260d783b156` | yes | Touched within 24 h |
| /work/worktrees/task-b124e131 | Succeeded | 2026-11-02T18:51:25.392058Z | 1.823 | 2026-11-04T23:49:00.789Z | 5.11 | `a00962fd6a055c513fa567c3a930fb875660dbef` | yes | Touched within 24 h |
| /work/worktrees/task-b1cdbed3 | Succeeded | 2026-11-04T11:43:52.223767Z | 0.220 | 2026-11-04T23:49:00.341Z | 5.11 | `ee9585d3247b3c99e8898c774a5561eec6ac0505` | yes | Touched within 24 h |
| /work/worktrees/task-b26811ff | Succeeded | 2026-11-03T22:26:10.922515Z | 0.218 | 2026-11-04T23:49:00.885Z | 5.11 | `2383ac1bc55b9a05e433884b200352074d4a21f9` | yes | Touched within 24 h |
| /work/worktrees/task-b2d063aa | Succeeded | 2026-11-03T05:20:23.862555Z | 0.217 | 2026-11-04T23:49:01.045Z | 5.11 | `b477d7ccf4faafa94cb91fd785ced348192885dc` | yes | Touched within 24 h |
| /work/worktrees/task-b384b934 | Succeeded | 2026-11-02T23:55:19.733768Z | 1.824 | 2026-11-04T23:49:05.058Z | 5.11 | `49f5a8236f9e3782c522c910ee54edff0752134e` | yes | Touched within 24 h |
| /work/worktrees/task-b38dbb26 | Succeeded | 2026-11-03T22:17:07.819885Z | 0.218 | 2026-11-04T23:49:04.954Z | 5.11 | `cc7fc10615158d9d44fd82fe41c2c260d783b156` | yes | Touched within 24 h |
| /work/worktrees/task-b3bd8985 | Succeeded | 2026-11-04T13:17:34.670558Z | 0.223 | 2026-11-04T23:48:58.465Z | 5.11 | `5dac249b9bae245cf5b59bd20bdb63a27ea1ace9` | yes | Touched within 24 h |
| /work/worktrees/task-b3c01691 | Succeeded | 2026-11-04T17:05:12.560172Z | 0.220 | 2026-11-04T23:49:05.698Z | 5.11 | `1ef88240b3c5ee2c759db958e5fb0b635b1248cc` | yes | Touched within 24 h |
| /work/worktrees/task-b578f67a | Succeeded | 2026-11-04T13:00:44.048546Z | 0.220 | 2026-11-04T23:48:58.713Z | 5.11 | `9b8c5cb06bb61fbf4c7d13d1c90ef50097dd5373` | yes | Touched within 24 h |
| /work/worktrees/task-b5b7324a | Failed | 2026-11-05T03:59:42.878953Z | 1.943 | 2026-11-05T03:24:00.832Z | 1.53 | `841898902a61f213e573f20e7258b06cb3c289fc` | yes | Touched within 24 h |
| /work/worktrees/task-b6a88568 | Succeeded | 2026-11-04T02:53:15.071646Z | 0.219 | 2026-11-04T23:49:00.761Z | 5.11 | `0d3643ef25f69a0c88d9d6f1a5c9436938cb1b14` | yes | Touched within 24 h |
| /work/worktrees/task-b6b7abdc | Succeeded | 2026-11-02T21:15:45.868401Z | 0.343 | 2026-11-04T23:48:57.929Z | 5.11 | `6211b45ccb9c33b1b5d24723bc3f8fec05761157` | yes | Touched within 24 h |
| /work/worktrees/task-b842c9bf | Succeeded | 2026-11-03T02:48:48.96668Z | 1.902 | 2026-11-04T23:49:06.142Z | 5.11 | `28a7c683f0bf0fc2055112a8cdfadd784b3d3649` | yes | Touched within 24 h |
| /work/worktrees/task-b8f46d8a | Succeeded | 2026-11-02T13:19:28.404255Z | 0.313 | 2026-11-04T23:49:05.926Z | 5.11 | `2b1e7494a9b100982e4e1f95a1be622fb8e918f9` | yes | Touched within 24 h |
| /work/worktrees/task-b922ebf8 | Succeeded | 2026-11-02T14:00:40.112008Z | 0.247 | 2026-11-04T23:49:04.018Z | 5.11 | `dc9eafeeef2c8b5c8c3a5f771be4f4444ddec7f4` | yes | Touched within 24 h |
| /work/worktrees/task-ba1cbbcc | Succeeded | 2026-11-03T00:47:31.781669Z | 2.159 | 2026-11-04T23:48:59.885Z | 5.11 | `49f5a8236f9e3782c522c910ee54edff0752134e` | yes | Touched within 24 h |
| /work/worktrees/task-ba83460f | Succeeded | 2026-11-03T23:00:07.938443Z | 0.218 | 2026-11-04T23:49:05.290Z | 5.11 | `fa0166fb5debbf9f2a2bec9cb556a755532259a7` | yes | Touched within 24 h |
| /work/worktrees/task-ba8ec885 | Succeeded | 2026-11-04T19:42:04.518606Z | 0.220 | 2026-11-04T23:49:02.982Z | 5.11 | `5b2c5a024499f442552f28c065c19379214318a0` | yes | Touched within 24 h |
| /work/worktrees/task-ba9f47c2 | Succeeded | 2026-11-02T18:28:21.827898Z | 1.823 | 2026-11-04T23:49:01.885Z | 5.11 | `95e6d23982f9be66893d5b60aed48a0473b0e583` | yes | Touched within 24 h |
| /work/worktrees/task-baacd1ef | Succeeded | 2026-11-02T19:40:10.035588Z | 0.766 | 2026-11-04T23:49:03.702Z | 5.11 | `dc8baf408cdddcc6892f82586a7157e660c19301` | yes | Touched within 24 h |
| /work/worktrees/task-bacbedc4 | Succeeded | 2026-11-03T05:20:50.70524Z | 0.265 | 2026-11-04T23:49:05.794Z | 5.11 | `28bd4056478e8ec2871939540035dfd71d1f9171` | yes | Touched within 24 h |
| /work/worktrees/task-bb0cd0ec | Succeeded | 2026-11-03T10:33:11.564312Z | 0.218 | 2026-11-04T23:49:03.458Z | 5.11 | `b0374226426b8aada42e8d370b95a4e53c4612cf` | yes | Touched within 24 h |
| /work/worktrees/task-bb924129 | Succeeded | 2026-11-03T10:03:13.933529Z | 0.218 | 2026-11-04T23:48:59.045Z | 5.11 | `974b0c17f754caffa78ef06f7eb1ba59456d25cb` | yes | Touched within 24 h |
| /work/worktrees/task-bbd026a3 | Succeeded | 2026-11-03T06:01:11.629424Z | 0.217 | 2026-11-04T23:49:03.522Z | 5.11 | `3ae8fc6fcaed32629f7acaf83b2190f31a025507` | yes | Touched within 24 h |
| /work/worktrees/task-bbd446f6 | Succeeded | 2026-11-05T03:12:06.680218Z | 1.943 | 2026-11-05T03:10:36.484Z | 1.75 | `ce537a8368493d570d2264e01a609eec9ac6e3f3` | yes | Touched within 24 h |
| /work/worktrees/task-bbe3899e | Succeeded | 2026-11-03T19:26:38.746776Z | 0.228 | 2026-11-04T23:48:59.241Z | 5.11 | `8e7a72fa2206ba740a27aae32de4ea94c87e1fb7` | yes | Touched within 24 h |
| /work/worktrees/task-bce1e9ec | Succeeded | 2026-11-02T21:17:13.275761Z | 0.217 | 2026-11-04T23:49:02.041Z | 5.11 | `b7745271a20601d7da2a1d86385d7f0fbbf91266` | yes | Touched within 24 h |
| /work/worktrees/task-bd1133db | Succeeded | 2026-11-03T02:45:18.104269Z | 0.394 | 2026-11-04T23:49:00.745Z | 5.11 | `8ccf236338dc2d7df2aec891f95ef9cd66618e73` | yes | Touched within 24 h |
| /work/worktrees/task-bdaa665c | Succeeded | 2026-11-04T13:24:43.401896Z | 0.220 | 2026-11-04T23:49:03.546Z | 5.11 | `16b6f7b09313eec81a88259646ab6a87a1338a71` | yes | Touched within 24 h |
| /work/worktrees/task-bf34cd7b | Succeeded | 2026-11-03T22:30:08.940672Z | 0.218 | 2026-11-04T23:49:03.270Z | 5.11 | `f58111666ada41ad13ccf0d7f965ca312fba7902` | yes | Touched within 24 h |
| /work/worktrees/task-c0cea8f5 | Succeeded | 2026-11-04T09:20:15.262874Z | 0.220 | 2026-11-04T23:48:58.409Z | 5.11 | `ba313ea3c764e08c15c69d514b7aa2d8eeaa3e1a` | yes | Touched within 24 h |
| /work/worktrees/task-c0e71ca7 | Succeeded | 2026-11-04T02:31:53.699436Z | 0.223 | 2026-11-04T23:49:05.682Z | 5.11 | `802dccaaa423fe119214ba975fc09ae90a9cd3f5` | yes | Touched within 24 h |
| /work/worktrees/task-c1c1c031 | Succeeded | 2026-11-04T01:41:02.691155Z | 0.219 | 2026-11-04T23:49:05.602Z | 5.11 | `90caa930e06bb27e112c7c0e89433c16f582f942` | yes | Touched within 24 h |
| /work/worktrees/task-c1cb4001 | Succeeded | 2026-11-02T23:42:52.565933Z | 1.824 | 2026-11-04T23:49:04.738Z | 5.11 | `71b100052e2ea19b8565b27f3d26c4294d12928c` | yes | Touched within 24 h |
| /work/worktrees/task-c238f9ca | Succeeded | 2026-11-03T07:53:20.436169Z | 0.221 | 2026-11-04T23:49:02.209Z | 5.11 | `61636195d4278262668f5bf5ec15a1a42767aa8a` | yes | Touched within 24 h |
| /work/worktrees/task-c28b88b9 | Succeeded | 2026-11-03T21:23:14.185212Z | 0.218 | 2026-11-04T23:48:57.493Z | 5.11 | `38e67ae1579dc71da848fb2c89cc9c55aaa83543` | yes | Touched within 24 h |
| /work/worktrees/task-c389a57b | Succeeded | 2026-11-03T19:39:54.078402Z | 0.218 | 2026-11-04T23:49:04.414Z | 5.11 | `67c9ce03f39c92f69bd70729246c1307d3bb29ac` | yes | Touched within 24 h |
| /work/worktrees/task-c3ba1792 | Dispatched | — | 2.068 | 2026-11-05T04:48:30.300Z | 0.12 | `7198f531dcd3adfd6e40a21a440edd055a9d669c` | yes | Protected task: Dispatched |
| /work/worktrees/task-c3ba3e10 | Succeeded | 2026-11-03T09:07:58.118508Z | 0.217 | 2026-11-04T23:49:01.549Z | 5.11 | `e7c2ce97636db523e022bf2be10d290884383636` | yes | Touched within 24 h |
| /work/worktrees/task-c3ef4038 | Succeeded | 2026-11-03T07:54:13.57533Z | 0.217 | 2026-11-04T23:48:59.161Z | 5.11 | `bf08bf4d3ee6f801edf53b5e0e81a9ea98005026` | yes | Touched within 24 h |
| /work/worktrees/task-c44579d7 | Succeeded | 2026-11-03T18:59:44.752171Z | 0.218 | 2026-11-04T23:49:04.238Z | 5.11 | `e38dcea2838e1b340339c3dde94b256689d15c57` | yes | Touched within 24 h |
| /work/worktrees/task-c682149c | Succeeded | 2026-11-04T00:28:17.776817Z | 1.829 | 2026-11-04T23:49:03.766Z | 5.11 | `b327cced9af3b4e1353b40c2e07363b38fafff98` | yes | Touched within 24 h |
| /work/worktrees/task-c7653f3f | Succeeded | 2026-11-02T20:15:56.539399Z | 0.217 | 2026-11-04T23:48:57.765Z | 5.11 | `c715f486d3a6cf0656ebf899d656f851c3b0193d` | yes | Touched within 24 h |
| /work/worktrees/task-c7ae562e | Succeeded | 2026-11-03T04:40:34.333024Z | 1.032 | 2026-11-04T23:48:58.549Z | 5.11 | `204745bc854e99559d47398189a85cbbf4d91193` | yes | Touched within 24 h |
| /work/worktrees/task-c7e4a230 | Succeeded | 2026-11-03T09:20:12.905215Z | 0.218 | 2026-11-04T23:49:05.434Z | 5.11 | `3163976062d9107a2b01dbd6b161601edf123f1d` | yes | Touched within 24 h |
| /work/worktrees/task-c7f02607 | Succeeded | 2026-11-04T08:55:15.972104Z | 0.220 | 2026-11-04T23:49:06.014Z | 5.11 | `68053ab3604ca404a6b911f81da730b394003a5a` | yes | Touched within 24 h |
| /work/worktrees/task-c7fd24a5 | Succeeded | 2026-11-03T01:12:25.12442Z | 1.834 | 2026-11-04T23:49:02.061Z | 5.11 | `0b724fea08ba15ddb02f989caea295f3f4ac81f5` | yes | Touched within 24 h |
| /work/worktrees/task-c81faedc | Succeeded | 2026-11-04T02:01:36.973072Z | 1.830 | 2026-11-04T23:49:05.618Z | 5.11 | `3de1fb13c89c7a414352782c1ae35e36888e705c` | yes | Touched within 24 h |
| /work/worktrees/task-c8274f67 | Succeeded | 2026-11-04T15:00:55.74738Z | 0.220 | 2026-11-04T23:48:57.529Z | 5.11 | `b710dbaaca1fc4505459e3d4a78cf08677af8cef` | yes | Touched within 24 h |
| /work/worktrees/task-c9cb7349 | Succeeded | 2026-11-03T19:48:04.453312Z | 0.218 | 2026-11-04T23:49:04.598Z | 5.11 | `ed67056c5e82389e95fdb82932f67396195a89f3` | yes | Touched within 24 h |
| /work/worktrees/task-ca347801 | Succeeded | 2026-11-03T02:33:51.249322Z | 1.825 | 2026-11-04T23:49:03.414Z | 5.11 | `bce601bf589563a591fc7d3d360f36525a7a3a1d` | yes | Touched within 24 h |
| /work/worktrees/task-cb6b4d80 | Succeeded | 2026-11-04T11:16:44.174212Z | 0.223 | 2026-11-04T23:49:00.297Z | 5.11 | `575a9bcfd6431c690ce8eb01ab3158e2de325d86` | yes | Touched within 24 h |
| /work/worktrees/task-cc42b674 | Succeeded | 2026-11-04T01:19:39.829608Z | 0.218 | 2026-11-04T23:49:03.354Z | 5.11 | `6a474e251f4f005a30235469f5ba921b1c907a98` | yes | Touched within 24 h |
| /work/worktrees/task-ccd13def | Succeeded | 2026-11-03T01:01:13.636395Z | 0.217 | 2026-11-04T23:49:05.666Z | 5.11 | `d456b2d9146527a813ce721b52fc10cae53d7127` | yes | Touched within 24 h |
| /work/worktrees/task-ccde064e | Succeeded | 2026-11-02T19:18:01.738027Z | 1.831 | 2026-11-04T23:48:58.113Z | 5.11 | `afa651cf274b889b8daee7a9c8f9e7c0e1b58eac` | yes | Touched within 24 h |
| /work/worktrees/task-ccfe679f | Succeeded | 2026-11-04T14:11:19.180888Z | 0.220 | 2026-11-04T23:49:02.145Z | 5.11 | `92356c1c36f45290b46ad1028d35ac80c45d7e65` | yes | Touched within 24 h |
| /work/worktrees/task-cde14651 | Succeeded | 2026-11-03T22:47:19.658438Z | 0.228 | 2026-11-04T23:49:06.314Z | 5.11 | `6fb6f5c088c0da3c1aa248fadf66a0e2823f0c41` | yes | Touched within 24 h |
| /work/worktrees/task-d092ee10 | Succeeded | 2026-11-02T21:59:44.344021Z | 0.217 | 2026-11-04T23:48:59.317Z | 5.11 | `ef52220e0fd4e8901ab3d53a376da6b4e37969f4` | yes | Touched within 24 h |
| /work/worktrees/task-d0b57b9c | Succeeded | 2026-11-03T20:11:10.817978Z | 0.241 | 2026-11-04T23:48:59.009Z | 5.11 | `c106f6dca4a6a7f13ae2b367ac3ce3bf850f4e96` | yes | Touched within 24 h |
| /work/worktrees/task-d0ce3b58 | Succeeded | 2026-11-02T18:19:16.7555Z | 0.244 | 2026-11-04T23:49:06.206Z | 5.11 | `1f79161b8fb16b6c23a6d9eab243ff9dc5cbd302` | yes | Touched within 24 h |
| /work/worktrees/task-d19080aa | Succeeded | 2026-11-04T20:54:06.857026Z | 0.220 | 2026-11-04T23:49:04.054Z | 5.11 | `9d2e68f4643a0b827149c8ea6d34f79f2e14737a` | yes | Touched within 24 h |
| /work/worktrees/task-d2564da4 | Succeeded | 2026-11-04T20:04:07.018092Z | 0.220 | 2026-11-04T23:49:04.938Z | 5.11 | `92a576d316b89ee72e519f219ae6231e0436f5c1` | yes | Touched within 24 h |
| /work/worktrees/task-d2c3674f | Succeeded | 2026-11-03T17:22:02.373691Z | 1.828 | 2026-11-04T23:49:05.814Z | 5.11 | `890f33d4002a2afb456c7a3c2de03af0f3b15a6d` | yes | Touched within 24 h |
| /work/worktrees/task-d340e01f | Succeeded | 2026-11-02T12:49:11.426357Z | 1.824 | 2026-11-04T23:49:02.642Z | 5.11 | `3c66ddd5465007908f6d752aea55a829205c0ac8` | yes | Touched within 24 h |
| /work/worktrees/task-d4b80a8c | Succeeded | 2026-11-02T17:38:52.590624Z | 1.823 | 2026-11-04T23:48:57.469Z | 5.11 | `169ed60c103bddddd2f9afcfdf017e5da382b6e1` | yes | Touched within 24 h |
| /work/worktrees/task-d5e8910e | Succeeded | 2026-11-05T03:37:06.54814Z | 1.943 | 2026-11-05T02:54:28.346Z | 2.02 | `35bb34ad7fe28541d8194f18e2bde10d211db6a4` | yes | Touched within 24 h |
| /work/worktrees/task-d67c4ca2 | Succeeded | 2026-11-03T20:35:07.381067Z | 0.218 | 2026-11-04T23:48:58.873Z | 5.11 | `f9d52f91ec1b35c21f5d8d6f73b9c5a4e5859fc9` | yes | Touched within 24 h |
| /work/worktrees/task-d6b0addf | Succeeded | 2026-11-03T00:36:33.778988Z | 1.824 | 2026-11-04T23:49:01.193Z | 5.11 | `265271be949d659f4fa7ba219453ce0f077a3068` | yes | Touched within 24 h |
| /work/worktrees/task-d707a486 | Succeeded | 2026-11-04T14:30:59.776625Z | 0.220 | 2026-11-04T23:49:04.346Z | 5.11 | `a8ed78d9342d08256e141507dc90e59a57f54e76` | yes | Touched within 24 h |
| /work/worktrees/task-d7614fb7 | Succeeded | 2026-11-02T15:27:24.92987Z | 1.841 | 2026-11-04T23:49:05.006Z | 5.11 | `056f7ff59f6a67aff2a96073c6fa2ec60ba29cda` | yes | Touched within 24 h |
| /work/worktrees/task-d8072595 | Succeeded | 2026-11-03T02:36:31.495144Z | 1.824 | 2026-11-04T23:49:02.502Z | 5.11 | `adf7c4d2c9bcea9fbadf0b9cf61e5c84d2d022ff` | yes | Touched within 24 h |
| /work/worktrees/task-d84c5448 | Succeeded | 2026-11-02T13:39:14.205972Z | 0.313 | 2026-11-04T23:48:59.781Z | 5.11 | `25e1d0f0e6e666f2152da631936f12a2c3a3bdf7` | yes | Touched within 24 h |
| /work/worktrees/task-d86bdf31 | Succeeded | 2026-11-02T20:31:08.254674Z | 0.217 | 2026-11-04T23:49:01.377Z | 5.11 | `bedb350de80f2db29ae5ce8d362fff0131f8c53b` | yes | Touched within 24 h |
| /work/worktrees/task-d8d8a562 | Failed | 2026-11-03T07:54:57.774615Z | 0.217 | 2026-11-04T23:48:59.033Z | 5.11 | `d08bf72b657d9f9fcff61c63f241b0cb52e418a0` | yes | Touched within 24 h |
| /work/worktrees/task-d94b0c85 | Succeeded | 2026-11-03T20:51:28.365616Z | 0.218 | 2026-11-04T23:49:05.950Z | 5.11 | `ffc8f8fc109f9520c24c8a0c277943911e850ad7` | yes | Touched within 24 h |
| /work/worktrees/task-d99c29ee | Succeeded | 2026-11-03T19:40:58.791228Z | 0.862 | 2026-11-04T23:48:59.505Z | 5.11 | `962e3d0aabd1c8a51416d1cf4e3a55801d7b1f64` | yes | Touched within 24 h |
| /work/worktrees/task-d9de1c95 | Succeeded | 2026-11-02T12:50:05.293442Z | 1.822 | 2026-11-04T23:49:01.933Z | 5.11 | `253f2aba5febfd3d1cd0b7871864a90af73e09ab` | yes | Touched within 24 h |
| /work/worktrees/task-da0006c8 | Succeeded | 2026-11-03T21:09:13.601146Z | 0.861 | 2026-11-04T23:48:58.901Z | 5.11 | `61dda8df4ebf0669c9f146493088687f41ef63c5` | yes | Touched within 24 h |
| /work/worktrees/task-da1c6eeb | Succeeded | 2026-11-03T03:23:50.557558Z | 1.825 | 2026-11-04T23:49:05.858Z | 5.11 | `a1c95a0144c5e9c2e0559c47ea0b15b29ca1d62f` | yes | Touched within 24 h |
| /work/worktrees/task-da5dcdfe | Succeeded | 2026-11-02T17:46:18.943992Z | 1.823 | 2026-11-04T23:49:02.281Z | 5.11 | `e216cc4af796ece143bfea1f9a1296fda33b0cb4` | yes | Touched within 24 h |
| /work/worktrees/task-da743107 | Succeeded | 2026-11-04T08:23:36.307713Z | 0.220 | 2026-11-04T23:49:02.105Z | 5.11 | `dd49f9250855d948b7f500e48a5ae70ba75c0b89` | yes | Touched within 24 h |
| /work/worktrees/task-db8d32c0 | Succeeded | 2026-11-03T03:48:51.457322Z | 0.219 | 2026-11-04T23:49:03.050Z | 5.11 | `faf08c36a538e346e4c3cf49ba075e199b74ba96` | yes | Touched within 24 h |
| /work/worktrees/task-dc12c438 | Succeeded | 2026-11-04T18:54:45.449846Z | 1.952 | 2026-11-04T23:48:59.061Z | 5.11 | `a01d907dc2cfaf32643afc07e944d59391fbf93f` | yes | Touched within 24 h |
| /work/worktrees/task-dc60bbd7 | Succeeded | 2026-11-04T00:08:36.967762Z | 0.218 | 2026-11-04T23:49:03.074Z | 5.11 | `6815f88b39f515e2c48b4521c31c7cb98800f84e` | yes | Touched within 24 h |
| /work/worktrees/task-dcfe6922 | Succeeded | 2026-11-02T18:09:29.035089Z | 0.228 | 2026-11-04T23:49:04.510Z | 5.11 | `30cb5279bd4e471ba9ac67676f1ad5cad76a36bb` | yes | Touched within 24 h |
| /work/worktrees/task-dd1fb91b | Succeeded | 2026-11-02T19:44:18.213932Z | 0.216 | 2026-11-04T23:49:04.494Z | 5.11 | `41897f471a40107ce1d2378ed563d7a0b214bd2c` | yes | Touched within 24 h |
| /work/worktrees/task-dd35b058 | Canceled | 2026-11-04T12:46:22.046901Z | 0.220 | 2026-11-04T23:49:00.669Z | 5.11 | `96882045e00c74f60f72a52aad51e47789b9d8a4` | yes | Touched within 24 h |
| /work/worktrees/task-dd90f1b7 | Succeeded | 2026-11-03T19:05:09.422753Z | 0.862 | 2026-11-04T23:49:05.474Z | 5.11 | `2e7023b4280fe8e5d8630fd6a29cf169e7db2624` | yes | Touched within 24 h |
| /work/worktrees/task-df8ec44a | Succeeded | 2026-11-03T05:42:46.901296Z | 0.217 | 2026-11-04T23:49:01.821Z | 5.11 | `7b920d84aae97139998f940ee2cce3f457bb6e70` | yes | Touched within 24 h |
| /work/worktrees/task-e056f0ff | Succeeded | 2026-11-04T18:53:12.577042Z | 0.220 | 2026-11-04T23:48:59.285Z | 5.11 | `ffa1546a23b3a0ca2a176cd3d35dda5657a71d1b` | yes | Touched within 24 h |
| /work/worktrees/task-e0c34d39 | Succeeded | 2026-11-02T18:28:39.241515Z | 1.823 | 2026-11-04T23:49:04.866Z | 5.11 | `8b8dc6268aaac866f33b51dcb82a4fa29afa0dc1` | yes | Touched within 24 h |
| /work/worktrees/task-e0de6690 | Succeeded | 2026-11-03T22:48:15.182137Z | 0.218 | 2026-11-04T23:49:01.325Z | 5.11 | `01e0a62045111ea390a8865fd28bd8c52c08bd40` | yes | Touched within 24 h |
| /work/worktrees/task-e23c0f83 | Succeeded | 2026-11-04T12:40:58.546641Z | 0.220 | 2026-11-04T23:49:04.074Z | 5.11 | `8c4040aaa2ff8ad178f959c3d0d06231a7eb652a` | yes | Touched within 24 h |
| /work/worktrees/task-e24018e3 | Succeeded | 2026-11-04T22:49:35.747679Z | 0.220 | 2026-11-04T23:49:05.254Z | 5.11 | `38726af4e68d243334f04f8d7bcef308f5527a59` | yes | Touched within 24 h |
| /work/worktrees/task-e28c82d0 | Succeeded | 2026-11-03T23:47:06.026272Z | 0.218 | 2026-11-04T23:49:03.334Z | 5.11 | `591fafecbe85bb9c0cb65a94824e37dcd39892a5` | yes | Touched within 24 h |
| /work/worktrees/task-e36a0edf | Succeeded | 2026-11-05T00:48:20.268318Z | 0.220 | 2026-11-05T00:47:24.375Z | 4.14 | `3943cf4c07f1d4272501a444dabe70940e9ae5f1` | yes | Touched within 24 h |
| /work/worktrees/task-e379bcd9 | Succeeded | 2026-11-04T12:36:22.31197Z | 0.220 | 2026-11-04T23:48:58.429Z | 5.11 | `2124102d9d487ba385b788915558d8da6c0c7054` | yes | Touched within 24 h |
| /work/worktrees/task-e38f32c6 | Succeeded | 2026-11-04T05:43:42.355412Z | 0.220 | 2026-11-04T23:49:04.998Z | 5.11 | `7042e6c75ea3f26e6ad16428e935e991bbc913b3` | yes | Touched within 24 h |
| /work/worktrees/task-e418768c | Succeeded | 2026-11-03T03:08:07.791864Z | 0.394 | 2026-11-04T23:49:02.089Z | 5.11 | `afa709e369a188b265f2b365038fd462db413aaa` | yes | Touched within 24 h |
| /work/worktrees/task-e438ae14 | Dispatched | — | 0.220 | 2026-11-05T04:46:17.218Z | 0.16 | `28cdefab326782f2c91ca361768a7252dc875ec7` | yes | Protected task: Dispatched |
| /work/worktrees/task-e45ab432 | Succeeded | 2026-11-04T07:57:22.352127Z | 0.220 | 2026-11-04T23:49:02.161Z | 5.11 | `05a7054fb82236b2c37ef5990ca9ad6e323f40a4` | yes | Touched within 24 h |
| /work/worktrees/task-e5258e8c | Succeeded | 2026-11-04T11:38:08.732771Z | 0.220 | 2026-11-04T23:49:04.394Z | 5.11 | `2b137d81dd991cd196daf8b4934bfdd5b5997af8` | yes | Touched within 24 h |
| /work/worktrees/task-e52b2bed | Succeeded | 2026-11-02T17:13:48.672674Z | 0.891 | 2026-11-04T23:49:04.982Z | 5.11 | `db58e77b4595598048cd8ab9cb2f6784eb556b9d` | yes | Touched within 24 h |
| /work/worktrees/task-e63ff4c2 | Succeeded | 2026-11-03T20:29:28.246824Z | 0.218 | 2026-11-04T23:48:58.165Z | 5.11 | `33c80715e832ce04e272121ba0819deb81224e15` | yes | Touched within 24 h |
| /work/worktrees/task-e72099f7 | Succeeded | 2026-11-02T12:40:44.338117Z | 1.819 | 2026-11-04T23:48:57.741Z | 5.11 | `b208243f3eb59681387c5eaf3393360e7a5e1b0e` | yes | Touched within 24 h |
| /work/worktrees/task-e778bd07 | Succeeded | 2026-11-04T23:46:32.644013Z | 0.220 | 2026-11-04T23:49:05.586Z | 5.11 | `419d8feb90ef21e10b8e713fc483a12a48579b66` | yes | Touched within 24 h |
| /work/worktrees/task-e7c70293 | Succeeded | 2026-11-02T20:08:29.462923Z | 1.455 | 2026-11-04T23:49:00.013Z | 5.11 | `e2cfadf36b928ffd0b3b9bbc3d492e4e83befc7b` | yes | Touched within 24 h |
| /work/worktrees/task-e7d4f999 | Succeeded | 2026-11-02T14:21:00.082939Z | 0.226 | 2026-11-04T23:49:00.917Z | 5.11 | `afcc1b26d6ba6b8c9c4ba6c3f2440ac18aa7dea5` | yes | Touched within 24 h |
| /work/worktrees/task-e80ac465 | Succeeded | 2026-11-05T03:37:39.788251Z | 0.220 | 2026-11-05T03:01:20.339Z | 1.91 | `f6f380c08e3468a5b98415e14f72380377fc6f6e` | yes | Touched within 24 h |
| /work/worktrees/task-e814f8ee | Succeeded | 2026-11-03T23:45:01.300634Z | 0.241 | 2026-11-04T23:48:57.781Z | 5.11 | `65194d01476a4a2ea4dfcfea05a44d86df2c39f1` | yes | Touched within 24 h |
| /work/worktrees/task-e8242d52 | Succeeded | 2026-11-04T19:36:53.360203Z | 0.220 | 2026-11-04T23:49:00.637Z | 5.11 | `66cbd06b11b633b74e72fce43e056423ffa46681` | yes | Touched within 24 h |
| /work/worktrees/task-e857ef3c | Succeeded | 2026-11-04T03:51:16.059979Z | 0.221 | 2026-11-04T23:48:59.745Z | 5.11 | `e214f1d1c8db33120d4abd5dd68e23a3d48ce226` | yes | Touched within 24 h |
| /work/worktrees/task-e8784f40 | Succeeded | 2026-11-02T19:43:23.673266Z | 3.004 | 2026-11-04T23:49:05.358Z | 5.11 | `4bd1473f0b59620db8832eb781a57a8d1da6e78c` | yes | Touched within 24 h |
| /work/worktrees/task-e87df8c7 | Succeeded | 2026-11-03T22:09:03.874367Z | 0.218 | 2026-11-04T23:48:57.661Z | 5.11 | `2c6e7cebbddc8ee6dc33ec0ab7fb152992f77998` | yes | Touched within 24 h |
| /work/worktrees/task-e937b0c3 | Succeeded | 2026-11-03T05:04:02.542808Z | 0.217 | 2026-11-04T23:49:04.270Z | 5.11 | `8ee214394e54ebad5050eb56ba41daa831eb5d00` | yes | Touched within 24 h |
| /work/worktrees/task-ea7eebeb | Succeeded | 2026-11-02T13:20:00.058116Z | 0.215 | 2026-11-04T23:49:01.797Z | 5.11 | `93876186a4cda86120203b09cd18999bfc44a8cc` | yes | Touched within 24 h |
| /work/worktrees/task-eab3a4fc | Succeeded | 2026-11-04T21:14:21.210278Z | 0.220 | 2026-11-04T23:49:03.614Z | 5.11 | `3b75cf53f879ccf21a8a62438cded5c1ab4d5c16` | yes | Touched within 24 h |
| /work/worktrees/task-eb43fc0e | Succeeded | 2026-11-03T07:43:14.750153Z | 0.217 | 2026-11-04T23:49:05.910Z | 5.11 | `e47abeddbdd13c489bf8fda6573e0c684499407b` | yes | Touched within 24 h |
| /work/worktrees/task-ec6f244f | Succeeded | 2026-11-03T10:06:50.085752Z | 0.224 | 2026-11-04T23:48:57.621Z | 5.11 | `2921957643833588c201a9fcc0c84bd98676242e` | yes | Touched within 24 h |
| /work/worktrees/task-ece4f83a | Succeeded | 2026-11-02T17:05:34.714682Z | 1.887 | 2026-11-04T23:49:06.026Z | 5.11 | `6dbd129b791e0ca13b2cff1a0eb5930cd5af53e2` | yes | Touched within 24 h |
| /work/worktrees/task-ecf8b748 | Succeeded | 2026-11-03T00:47:50.528615Z | 2.717 | 2026-11-04T23:49:03.110Z | 5.11 | `aaa1a7592ae74eee0a414e8e40dabe9fc8c53b0c` | yes | Touched within 24 h |
| /work/worktrees/task-ed18c421 | Succeeded | 2026-11-02T20:01:29.822072Z | 1.830 | 2026-11-04T23:49:03.654Z | 5.11 | `2a5f324a0708ebefe59c7aad1485c51a968c2393` | yes | Touched within 24 h |
| /work/worktrees/task-ed8199e4 | Succeeded | 2026-11-03T17:38:29.287202Z | 0.861 | 2026-11-04T23:49:04.382Z | 5.11 | `3f6dfd0f1eb1dd8e50a6c23d8ecb025c2b9aef0f` | yes | Touched within 24 h |
| /work/worktrees/task-eeee4db1 | Succeeded | 2026-11-03T11:15:20.045723Z | 0.218 | 2026-11-04T23:49:05.214Z | 5.11 | `e17675ebde23e1da2500d1df565225348ec15426` | yes | Touched within 24 h |
| /work/worktrees/task-ef4c37b4 | Succeeded | 2026-11-03T18:36:53.493101Z | 0.218 | 2026-11-04T23:49:01.957Z | 5.11 | `4cedfe0a2c9a244299f4b6f99468d5a767184ba3` | yes | Touched within 24 h |
| /work/worktrees/task-f059674d | Dispatched | — | 2.184 | 2026-11-05T04:30:59.570Z | 0.41 | `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a` | yes | Protected task: Dispatched |
| /work/worktrees/task-f0717652 | Succeeded | 2026-11-03T01:00:17.814356Z | 1.824 | 2026-11-04T23:49:02.962Z | 5.11 | `eace3de1eb99fd6c6b690ca1f79d1ca2668482c0` | yes | Touched within 24 h |
| /work/worktrees/task-f123dae7 | Succeeded | 2026-11-03T16:30:10.6609Z | 0.218 | 2026-11-04T23:49:01.649Z | 5.11 | `61840ae4cede2ecf98d7a5f07fa578ae5b9828de` | yes | Touched within 24 h |
| /work/worktrees/task-f1a11dd3 | Failed | 2026-11-03T22:39:32.677323Z | 0.218 | 2026-11-04T23:48:57.513Z | 5.11 | `97e77f9f1fd6f71e2719bc661b48f8f89f129029` | yes | Touched within 24 h |
| /work/worktrees/task-f2976027 | Succeeded | 2026-11-04T19:16:13.93719Z | 0.220 | 2026-11-04T23:49:05.706Z | 5.11 | `422c6f0b965285c778afbda9f18c860157319051` | yes | Touched within 24 h |
| /work/worktrees/task-f2d12082 | Succeeded | 2026-11-03T05:02:27.398823Z | 1.825 | 2026-11-04T23:49:01.525Z | 5.11 | `7d2dfc7ecc48f942b445a98f3185a77b43c60d00` | yes | Touched within 24 h |
| /work/worktrees/task-f2ed8c32 | Succeeded | 2026-11-04T12:22:09.683893Z | 0.220 | 2026-11-04T23:49:05.526Z | 5.11 | `96882045e00c74f60f72a52aad51e47789b9d8a4` | yes | Touched within 24 h |
| /work/worktrees/task-f2f59326 | Succeeded | 2026-11-04T11:41:02.805624Z | 0.220 | 2026-11-04T23:48:57.873Z | 5.11 | `bbf9d43e5a7969699f93b1cdd6476cb2b0c06ca5` | yes | Touched within 24 h |
| /work/worktrees/task-f303b3ec | Succeeded | 2026-11-04T12:50:30.545122Z | 0.230 | 2026-11-04T23:49:00.061Z | 5.11 | `c3118c9dbed4ba53215202ef64b92f58e859718c` | yes | Touched within 24 h |
| /work/worktrees/task-f35f1237 | Succeeded | 2026-11-04T22:02:04.604999Z | 0.220 | 2026-11-04T23:49:03.434Z | 5.11 | `e85c53930a553f38ab461ceeae1620d124e98bb6` | yes | Touched within 24 h |
| /work/worktrees/task-f3973a3a | Succeeded | 2026-11-02T14:19:54.191784Z | 1.842 | 2026-11-04T23:48:57.461Z | 5.11 | `d9587915cd4a82edef4c75ac434d500bf103d02f` | yes | Touched within 24 h |
| /work/worktrees/task-f4830a56 | Succeeded | 2026-11-03T09:26:30.620983Z | 0.221 | 2026-11-04T23:49:00.033Z | 5.11 | `d6b174d6906dc8b4707af491577c5e29ac01ed8d` | yes | Touched within 24 h |
| /work/worktrees/task-f4cf239f | Succeeded | 2026-11-02T14:29:10.983493Z | 0.238 | 2026-11-04T23:48:59.657Z | 5.11 | `2d0533c2817d3ce948fbceb34461031f4c6fe734` | yes | Touched within 24 h |
| /work/worktrees/task-f5e96e75 | Succeeded | 2026-11-04T17:45:02.382151Z | 0.220 | 2026-11-04T23:49:02.542Z | 5.11 | `33af3d5c78d2b56ad9b8148c5850089d835352ac` | yes | Touched within 24 h |
| /work/worktrees/task-f5eb193f | Succeeded | 2026-11-04T14:37:05.763846Z | 0.220 | 2026-11-04T23:49:02.798Z | 5.11 | `d9107394e0faed72bd253da96eb17f657b89598b` | yes | Touched within 24 h |
| /work/worktrees/task-f674c79a | Canceled | 2026-11-05T04:39:11.920461Z | 1.084 | 2026-11-05T03:57:43.912Z | 0.97 | `cde2e91277cd064f62d9b5ed7ac7062989b0f360` | yes | Touched within 24 h |
| /work/worktrees/task-f67b2762 | Succeeded | 2026-11-04T06:09:25.428233Z | 0.220 | 2026-11-04T23:49:00.277Z | 5.11 | `8a211a55ab77eccf6f851437bba4a74e87718e87` | yes | Touched within 24 h |
| /work/worktrees/task-f686ba8a | Succeeded | 2026-11-03T06:22:25.603187Z | 0.217 | 2026-11-04T23:49:00.261Z | 5.11 | `9cf4f74fa9c704b2e767fee8f0708df928330008` | yes | Touched within 24 h |
| /work/worktrees/task-f6e3cc3c | Succeeded | 2026-11-04T13:49:16.686384Z | 0.220 | 2026-11-04T23:49:04.250Z | 5.11 | `bbbc64ff391dde0887eb39cb9b695f4e09dccd8f` | yes | Touched within 24 h |
| /work/worktrees/task-f7703a17 | Succeeded | 2026-11-04T10:10:21.408144Z | 0.220 | 2026-11-04T23:49:00.353Z | 5.11 | `62f4aaf99f1817a6953fbdc07ac846bf28602642` | yes | Touched within 24 h |
| /work/worktrees/task-f893d1a3 | Succeeded | 2026-11-04T08:43:23.600051Z | 0.220 | 2026-11-04T23:48:59.221Z | 5.11 | `1470340559889c2985ea722173181ea25c51b47c` | yes | Touched within 24 h |
| /work/worktrees/task-f9696a82 | Succeeded | 2026-11-03T09:18:18.370813Z | 0.217 | 2026-11-04T23:48:57.797Z | 5.11 | `62beb934d40f616d8a3e26482fb1a3f7deb00154` | yes | Touched within 24 h |
| /work/worktrees/task-fbb67b8e | Succeeded | 2026-11-03T09:28:40.59648Z | 0.218 | 2026-11-04T23:48:58.213Z | 5.11 | `3163976062d9107a2b01dbd6b161601edf123f1d` | yes | Touched within 24 h |
| /work/worktrees/task-fcf7ce80 | Succeeded | 2026-11-03T03:45:30.520529Z | 3.008 | 2026-11-04T23:48:57.545Z | 5.11 | `c4e7aa06e9f8c119f7dd59f52e38ed413293c9f2` | yes | Touched within 24 h |
| /work/worktrees/task-fda2f329 | Succeeded | 2026-11-03T06:23:21.761195Z | 0.217 | 2026-11-04T23:49:00.313Z | 5.11 | `fa431565179c5506be7632f9984c0c7b1e472ffc` | yes | Touched within 24 h |
| /work/worktrees/task-ff7ab869 | Succeeded | 2026-11-04T20:36:06.672096Z | 0.220 | 2026-11-04T23:49:06.286Z | 5.11 | `0a88f5a7ed89d92a1bccdddced6493336eb19261` | yes | Touched within 24 h |
| /work/worktrees/task-ff8f8f3f | Succeeded | 2026-11-04T11:38:29.695111Z | 0.220 | 2026-11-04T23:49:02.942Z | 5.11 | `5ab8e14fbd0d8f3c34e1303d2a93733ea79b1565` | yes | Touched within 24 h |
| /work/worktrees/task-ffe310fe | Succeeded | 2026-11-04T00:51:06.510931Z | 0.218 | 2026-11-04T23:49:03.722Z | 5.11 | `47a44f52053b8e330782e0ed9a6932c93f7239f5` | yes | Touched within 24 h |

## Suffixed directories outside the deletion allowlist

These were measured but were never deletion candidates. Every path fails the exact required regex and was kept for that reason.

| Path | GiB | Reason |
|---|---:|---|
| /work/worktrees/task-cffa0c4b-uA | 0.215 | Outside exact allowed path format |
| /work/worktrees/task-cffa0c4b-uB | 0.224 | Outside exact allowed path format |
| /work/worktrees/task-cffa0c4b-uC | 0.275 | Outside exact allowed path format |
| /work/worktrees/task-cffa0c4b-wD | 0.384 | Outside exact allowed path format |
| /work/worktrees/task-cffa0c4b-wE | 0.214 | Outside exact allowed path format |
| /work/worktrees/task-cffa0c4b-wF | 0.214 | Outside exact allowed path format |
| /work/worktrees/task-cffa0c4b-wG | 0.216 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-combined | 0.236 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-laneA | 0.236 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-laneB | 0.245 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-laneC | 0.296 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-wE | 0.214 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-wF | 0.232 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-wG | 0.232 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-wI | 0.213 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-wM | 0.345 | Outside exact allowed path format |
| /work/worktrees/task-e38d6427-wX | 0.214 | Outside exact allowed path format |

## External evidence

Evidence root: `/work/evidence/task-374d7fa0`. Generated payloads remain outside Git. Key records: `validated-paths.txt`, `inventory-start.json`, `inventory.json`, `inventory.jsonl`, per-project `tasks-*.json`, `all-tasks.json`, `decisions.json`, `excluded-paths.json`, `df-before.txt`, `df-after.txt`, `other-consumers.txt`, and `docker-system-df.txt`.

--- next stage ---
next: done
handoff: No eligible worktree deletions; disk pressure remains. Repeat after metadata ages or commission a different explicit cleanup scope.
artifact: docs/investigations/2026-11-05-server2-worktree-cleanup.md
