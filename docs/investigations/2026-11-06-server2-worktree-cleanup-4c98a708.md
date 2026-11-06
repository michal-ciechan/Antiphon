# Server2 terminal-task worktree cleanup — 2026-11-06

Removed **202 of 325 worktrees**, retaining **123**. All Git removals succeeded. Filesystem use fell from **56% to 45%**, and available space rose by **72.775 GB / 67.777 GiB**. One unpublished reflog commit was preserved under a durable salvage ref. No builds or tests ran.

## Authority and scope

Task: `4c98a708-8d94-4f35-8bfb-ad9c2a306eee`. Assigned branch: `feat/card-task-4c98a708`. Source base: `df1fc145181a3bedf444f9113f822f30d3b136f6`.

The operator's explicit cleanup authority was exercised without requesting another approval. Eligibility used server-recorded `completedAt`, never filesystem mtimes. Fixed inventory time: **2026-11-06T14:35:27.426Z**; strict older-than-24-hours cutoff: **2026-11-05T14:35:27.426Z**. Only Succeeded, Failed or Canceled tasks completed before this cutoff were selected. A tree owned by any open task was retained. All 16 explicitly protected task prefixes were excluded, including every suffixed checkout. No retention or other setting changed; the retention-setting decision remains with Investigate.

The procedure follows the completion-age pass documented in `docs/investigations/2026-11-05-server2-worktree-cleanup-2.md` (commit `556398e429ab5f696f94e6779056d4469d734365`) and its external evidence in `/work/evidence/task-9e4c7963`. The earlier inventory report at `1e651e367d8bd699dedfc522d6034159f03484ed` was also read.

Execution ran directly inside the assigned server2 runner container, ID `dfcc116be9844b7533a5dbf65d933b82765e89f26125f73f0b4122ebfd478c28`, as UID/GID 1654. The task's server record identified runner `server2`; its own session `a8538cb6-05de-42cb-aa6b-ceeca7380049` was confirmed through this container's local runner before cleanup. An attempted `ssh mc@server2` inspection refused host-key verification; no SSH trust was bypassed. Direct in-container execution avoided that unavailable transport. No command executed on the desktop. The runner and its volumes were kept running and intact.

## Inventory, removal and verification

The complete project list returned **7,963** task rows. The complete fleet list returned **25,693** rows, with `excluded.total=0`; the fleet read was deliberate because the directory contains work from multiple projects. All 325 task-shaped paths mapped to tasks. The only excluded non-task directory was `/work/worktrees/.antiphon-test-evidence`, which was untouched.

Each batch refreshed the complete fleet-wide open-task list and standing-agent list. Each deletion re-read the owning task detail, checked terminal status and cutoff, rejected open/standing ownership, verified the literal directory and Git common-directory identity, and required an unchanged HEAD. All selected paths belonged to `/work/repos/antiphon`.

Fetched origin before containment checks. Checked HEAD, attached branch and available worktree/branch reflog tips against origin refs, preserving unpublished tips through `refs/antiphon/salvage/<task>/<sha>`. Current remote heads were additionally advertised through `git ls-remote --heads origin`; the final audit matched containing origin refs to their currently advertised tip SHAs, including the earlier batches. It recorded **669** publication proofs across deleted worktrees and their surviving branch reflogs. The separately retained salvage ref was revalidated after removal.

Removal used `git -C /work/repos/antiphon worktree remove --force <one-literal-absolute-path>`, in **eight batches of 25 and one batch of 2**, with a **10-second pause after each batch**. Every invocation's path was supplied literally on the batch command line and validated against the inventory. No fallback deletion was necessary. No `rm`, `git clean`, branch deletion, salvage push, or file copy was used. Secret-shaped ignored files were therefore never copied. Normal Git worktree prune completed successfully. All commands were supervised foreground children and awaited; none was detached or orphaned.

Two old terminal trees, `/work/worktrees/task-1edbc845` and `/work/worktrees/task-b5b7324a`, each had one nonignored porcelain status record before removal. The authorized force removal discarded these entries. Status payloads and file contents were not logged or copied; no unpublished commits were discarded.

At **2026-11-06T14:47:15.846Z**, reconciliation confirmed all **202** deleted directories absent and unregistered, all **123** retained trees present, and all **17** explicitly protected directories present and registered. No new task-shaped paths appeared during this pass. No removal failed or was refused. Deleted task states: **185 Succeeded, 12 Canceled, 5 Failed**. No own bin/obj/test-result outputs were created.

| Retention reason | Trees |
|---|---:|
| Terminal completed within 24 hours | 100 |
| Explicitly protected task (16 prefixes, one extra suffixed checkout) | 17 |
| Open task owner | 5 |
| This cleanup task | 1 |
| Total | 123 |

## Disk measurements

Inventoried allocated bytes removed: **73449377792** (73.449 GB / 68.405 GiB). Observed filesystem available-space increase: **72774676480** bytes (72.775 GB / 67.777 GiB). These differ because this is a running shared filesystem.

Before:

```text
Filesystem                           1B-blocks         Used    Available Use% Mounted on
/dev/mapper/ubuntu--vg-ubuntu--lv 756634804224 399232897024 326590976000  56% /work
```

After:

```text
Filesystem                           1B-blocks         Used    Available Use% Mounted on
/dev/mapper/ubuntu--vg-ubuntu--lv 756634804224 326458220544 399365652480  45% /work
```

| Batch | Removed | Filesystem use after |
|---|---:|---:|
| 1 | 25 | 55% |
| 2 | 25 | 53% |
| 3 | 25 | 52% |
| 4 | 25 | 50% |
| 5 | 25 | 49% |
| 6 | 25 | 48% |
| 7 | 25 | 46% |
| 8 | 25 | 46% |
| 9 | 2 | 45% |

## Salvage

- Worktree: `/work/worktrees/task-29664acc`. Preserved commit: `584a34eaea2712ed51ca444c968519dbdb34c4cb`. Durable local ref in `/work/repos/antiphon`: `refs/antiphon/salvage/29664acc/584a34eaea2712ed51ca444c968519dbdb34c4cb`. This ref remains after worktree removal; verified with `git rev-parse --verify`. No salvage branch was pushed.

## Retained worktrees

Every retained task-shaped directory is listed below. State and completion time are the inventory observations; open tasks may progress while the host remains live.

| Absolute path | Task state | completedAt UTC | Reason |
|---|---|---|---|
| /work/worktrees/task-00646fa6 | Canceled | 2026-11-06T03:35:59.432423Z | Terminal completed within 24 hours |
| /work/worktrees/task-013b9ff7 | Succeeded | 2026-11-05T20:25:00.520615Z | Terminal completed within 24 hours |
| /work/worktrees/task-01d698da | Failed | 2026-11-06T05:14:26.357882Z | Terminal completed within 24 hours |
| /work/worktrees/task-02710520 | Succeeded | 2026-11-06T01:01:57.10921Z | Terminal completed within 24 hours |
| /work/worktrees/task-03477baf | Succeeded | 2026-11-06T07:14:00.00372Z | Explicit protected task |
| /work/worktrees/task-0387f626 | Succeeded | 2026-11-06T11:21:04.289468Z | Terminal completed within 24 hours |
| /work/worktrees/task-044f7257 | Succeeded | 2026-11-06T05:23:03.346321Z | Terminal completed within 24 hours |
| /work/worktrees/task-07ab1e49 | Succeeded | 2026-11-06T00:22:07.254565Z | Terminal completed within 24 hours |
| /work/worktrees/task-0915902c | Failed | 2026-11-06T02:28:29.611062Z | Terminal completed within 24 hours |
| /work/worktrees/task-09d70215 | Succeeded | 2026-11-06T02:43:31.679124Z | Terminal completed within 24 hours |
| /work/worktrees/task-0b3f9d62 | Succeeded | 2026-11-06T10:00:24.524562Z | Terminal completed within 24 hours |
| /work/worktrees/task-0b9155a9 | Succeeded | 2026-11-06T09:08:17.711529Z | Explicit protected task |
| /work/worktrees/task-0ba30c05 | Succeeded | 2026-11-06T12:41:13.072144Z | Terminal completed within 24 hours |
| /work/worktrees/task-0cc54c85 | Succeeded | 2026-11-06T14:32:11.440606Z | Explicit protected task |
| /work/worktrees/task-11212cb7 | Failed | 2026-11-06T02:14:11.751169Z | Terminal completed within 24 hours |
| /work/worktrees/task-144136d6 | Succeeded | 2026-11-06T07:40:15.735347Z | Explicit protected task |
| /work/worktrees/task-1630c3fe | Succeeded | 2026-11-06T05:15:36.731568Z | Terminal completed within 24 hours |
| /work/worktrees/task-1c9ceb47 | Succeeded | 2026-11-05T22:34:11.853777Z | Terminal completed within 24 hours |
| /work/worktrees/task-1e279cab | Canceled | 2026-11-06T02:34:10.084527Z | Terminal completed within 24 hours |
| /work/worktrees/task-1f10d17d | Succeeded | 2026-11-05T23:45:53.924538Z | Terminal completed within 24 hours |
| /work/worktrees/task-204a327a | Succeeded | 2026-11-06T12:46:24.130145Z | Explicit protected task |
| /work/worktrees/task-23abede2 | Dispatched | — | Explicit protected task |
| /work/worktrees/task-27123eab | Succeeded | 2026-11-05T17:38:16.126569Z | Terminal completed within 24 hours |
| /work/worktrees/task-29e42131 | Succeeded | 2026-11-05T20:20:02.520544Z | Terminal completed within 24 hours |
| /work/worktrees/task-2b5414eb | Succeeded | 2026-11-05T21:53:54.052116Z | Terminal completed within 24 hours |
| /work/worktrees/task-2db0be9a | Succeeded | 2026-11-06T05:59:12.750673Z | Terminal completed within 24 hours |
| /work/worktrees/task-32c41b68 | Succeeded | 2026-11-06T10:22:15.66467Z | Terminal completed within 24 hours |
| /work/worktrees/task-340f1d0a | Succeeded | 2026-11-05T21:33:45.129575Z | Terminal completed within 24 hours |
| /work/worktrees/task-34125850 | Canceled | 2026-11-05T19:19:46.663998Z | Terminal completed within 24 hours |
| /work/worktrees/task-36e2134b | Succeeded | 2026-11-05T17:00:52.360676Z | Terminal completed within 24 hours |
| /work/worktrees/task-382b8506 | Succeeded | 2026-11-06T02:55:02.40046Z | Terminal completed within 24 hours |
| /work/worktrees/task-38ec4219 | Succeeded | 2026-11-05T22:40:03.25762Z | Terminal completed within 24 hours |
| /work/worktrees/task-39da4445 | Succeeded | 2026-11-06T13:44:21.546548Z | Terminal completed within 24 hours |
| /work/worktrees/task-39e1bd2f | Succeeded | 2026-11-06T10:14:57.925381Z | Explicit protected task |
| /work/worktrees/task-39f7bfe1 | Succeeded | 2026-11-06T01:15:09.918773Z | Terminal completed within 24 hours |
| /work/worktrees/task-420b5c90 | Succeeded | 2026-11-05T23:51:46.199895Z | Terminal completed within 24 hours |
| /work/worktrees/task-481b379a | Succeeded | 2026-11-06T08:07:07.891206Z | Terminal completed within 24 hours |
| /work/worktrees/task-4965fc83 | Succeeded | 2026-11-06T11:21:50.56469Z | Terminal completed within 24 hours |
| /work/worktrees/task-4aff2d15 | Succeeded | 2026-11-05T19:20:29.731346Z | Terminal completed within 24 hours |
| /work/worktrees/task-4c98a708 | Dispatched | — | Own worktree |
| /work/worktrees/task-5050b2de | Succeeded | 2026-11-06T10:01:43.650116Z | Terminal completed within 24 hours |
| /work/worktrees/task-50e9a890 | Canceled | 2026-11-06T02:34:09.354679Z | Terminal completed within 24 hours |
| /work/worktrees/task-50f25440 | Succeeded | 2026-11-05T22:20:16.696778Z | Terminal completed within 24 hours |
| /work/worktrees/task-51b0c438 | Failed | 2026-11-05T21:45:06.923656Z | Terminal completed within 24 hours |
| /work/worktrees/task-525e0201 | Succeeded | 2026-11-06T11:57:25.716511Z | Explicit protected task |
| /work/worktrees/task-52890135 | Canceled | 2026-11-05T18:46:57.572209Z | Terminal completed within 24 hours |
| /work/worktrees/task-53da6f14 | Succeeded | 2026-11-06T00:41:15.168248Z | Terminal completed within 24 hours |
| /work/worktrees/task-543d8c5b | Failed | 2026-11-06T00:23:50.242321Z | Terminal completed within 24 hours |
| /work/worktrees/task-5528528e | Succeeded | 2026-11-06T09:45:48.663883Z | Explicit protected task |
| /work/worktrees/task-555da802 | Failed | 2026-11-06T03:43:16.380319Z | Terminal completed within 24 hours |
| /work/worktrees/task-55b96312 | Succeeded | 2026-11-05T23:05:37.954261Z | Terminal completed within 24 hours |
| /work/worktrees/task-5960094e | Succeeded | 2026-11-06T02:32:36.193064Z | Terminal completed within 24 hours |
| /work/worktrees/task-5ae98d31 | Succeeded | 2026-11-05T19:30:02.428618Z | Terminal completed within 24 hours |
| /work/worktrees/task-5d3d7fb0 | Succeeded | 2026-11-06T08:47:58.202175Z | Terminal completed within 24 hours |
| /work/worktrees/task-5e5930cc | Succeeded | 2026-11-06T04:35:37.784702Z | Terminal completed within 24 hours |
| /work/worktrees/task-5ec136af | Succeeded | 2026-11-06T04:27:14.180569Z | Terminal completed within 24 hours |
| /work/worktrees/task-5fb6b1b2 | Dispatched | — | Open task owner: 5fb6b1b2-44dd-47c3-8a99-8eea293f1ad6 |
| /work/worktrees/task-5fb6b1b2-base | Dispatched | — | Open task owner: 5fb6b1b2-44dd-47c3-8a99-8eea293f1ad6 |
| /work/worktrees/task-642b8665 | Succeeded | 2026-11-06T02:29:15.022464Z | Explicit protected task |
| /work/worktrees/task-6532e78e | Succeeded | 2026-11-05T19:24:57.473669Z | Terminal completed within 24 hours |
| /work/worktrees/task-6569dd36 | Blocked | 2026-11-05T19:35:23.083284Z | Open task owner: 6569dd36-5266-4f91-b797-60e984e4be6c |
| /work/worktrees/task-65e6f8b0 | Succeeded | 2026-11-06T00:32:16.772232Z | Terminal completed within 24 hours |
| /work/worktrees/task-69ade330 | Failed | 2026-11-06T00:50:00.209829Z | Terminal completed within 24 hours |
| /work/worktrees/task-6f53cb5a | Succeeded | 2026-11-06T11:24:58.958832Z | Terminal completed within 24 hours |
| /work/worktrees/task-742b9c5c | Succeeded | 2026-11-06T01:43:55.024217Z | Terminal completed within 24 hours |
| /work/worktrees/task-7626aee3 | Succeeded | 2026-11-05T19:42:53.955058Z | Terminal completed within 24 hours |
| /work/worktrees/task-7d6aabc8 | Succeeded | 2026-11-06T07:49:05.32119Z | Terminal completed within 24 hours |
| /work/worktrees/task-7e4799eb | Failed | 2026-11-06T05:00:20.617097Z | Terminal completed within 24 hours |
| /work/worktrees/task-82cf72c1 | Failed | 2026-11-05T21:42:55.977828Z | Terminal completed within 24 hours |
| /work/worktrees/task-840f535c | Succeeded | 2026-11-06T05:22:12.854807Z | Terminal completed within 24 hours |
| /work/worktrees/task-84d42fbe | Blocked | 2026-11-06T06:56:57.641817Z | Explicit protected task |
| /work/worktrees/task-85a330bf | Succeeded | 2026-11-06T12:05:59.656626Z | Terminal completed within 24 hours |
| /work/worktrees/task-89ea871f | Succeeded | 2026-11-06T01:28:57.546439Z | Terminal completed within 24 hours |
| /work/worktrees/task-8a9f92ba | Blocked | 2026-11-06T06:08:46.784377Z | Open task owner: 8a9f92ba-1572-48a7-9f1d-6cac71c4aa50 |
| /work/worktrees/task-8b3bd292 | Succeeded | 2026-11-06T10:18:38.076913Z | Terminal completed within 24 hours |
| /work/worktrees/task-8b9b7a9a | Succeeded | 2026-11-06T06:15:16.605166Z | Terminal completed within 24 hours |
| /work/worktrees/task-8eb266f0 | Succeeded | 2026-11-06T01:05:28.7642Z | Terminal completed within 24 hours |
| /work/worktrees/task-8f14e2c9 | Succeeded | 2026-11-05T20:46:45.633694Z | Terminal completed within 24 hours |
| /work/worktrees/task-8f43dbed | Succeeded | 2026-11-06T03:25:15.951049Z | Terminal completed within 24 hours |
| /work/worktrees/task-9222cb00 | Succeeded | 2026-11-05T22:57:32.944722Z | Terminal completed within 24 hours |
| /work/worktrees/task-935ca9be | Succeeded | 2026-11-06T00:46:20.441423Z | Terminal completed within 24 hours |
| /work/worktrees/task-973b4d1b | Succeeded | 2026-11-06T00:05:46.199674Z | Terminal completed within 24 hours |
| /work/worktrees/task-a342b58a | Failed | 2026-11-06T04:04:39.20661Z | Terminal completed within 24 hours |
| /work/worktrees/task-a3555767 | Succeeded | 2026-11-06T05:39:32.498609Z | Terminal completed within 24 hours |
| /work/worktrees/task-a3a1bbee | Succeeded | 2026-11-05T19:16:37.579556Z | Terminal completed within 24 hours |
| /work/worktrees/task-a43ac839 | Succeeded | 2026-11-06T12:22:22.403131Z | Terminal completed within 24 hours |
| /work/worktrees/task-a8945a17 | Succeeded | 2026-11-05T19:13:01.276646Z | Terminal completed within 24 hours |
| /work/worktrees/task-a98c2d1e | Failed | 2026-11-06T03:43:41.133105Z | Terminal completed within 24 hours |
| /work/worktrees/task-a9b46304 | Canceled | 2026-11-06T02:34:08.68234Z | Terminal completed within 24 hours |
| /work/worktrees/task-aa3c54ff | Dispatched | — | Explicit protected task |
| /work/worktrees/task-aae1b064 | Succeeded | 2026-11-06T06:09:56.957871Z | Terminal completed within 24 hours |
| /work/worktrees/task-ab37b2cf | Succeeded | 2026-11-06T08:36:42.420305Z | Terminal completed within 24 hours |
| /work/worktrees/task-aeb2deb6 | Succeeded | 2026-11-06T04:50:30.689878Z | Terminal completed within 24 hours |
| /work/worktrees/task-b7c4a9b7 | Canceled | 2026-11-06T02:34:07.792798Z | Terminal completed within 24 hours |
| /work/worktrees/task-b8770621 | Succeeded | 2026-11-06T00:39:26.141319Z | Terminal completed within 24 hours |
| /work/worktrees/task-b8eac553 | Succeeded | 2026-11-05T23:59:38.943965Z | Terminal completed within 24 hours |
| /work/worktrees/task-bb0a6ce8 | Succeeded | 2026-11-06T02:21:10.412377Z | Terminal completed within 24 hours |
| /work/worktrees/task-bccb0c90 | Succeeded | 2026-11-06T02:55:33.322178Z | Terminal completed within 24 hours |
| /work/worktrees/task-c993f042 | Succeeded | 2026-11-05T19:20:35.886536Z | Terminal completed within 24 hours |
| /work/worktrees/task-ca9712bd | Succeeded | 2026-11-06T10:15:48.044238Z | Terminal completed within 24 hours |
| /work/worktrees/task-cdf07a4b | Dispatched | — | Open task owner: cdf07a4b-57d3-42d9-8519-58c046e0eac8 |
| /work/worktrees/task-cdf5415a | Succeeded | 2026-11-05T21:36:43.485819Z | Terminal completed within 24 hours |
| /work/worktrees/task-ce37c38f | Succeeded | 2026-11-06T09:30:01.595141Z | Terminal completed within 24 hours |
| /work/worktrees/task-d463ea3b | Succeeded | 2026-11-06T05:40:00.997649Z | Terminal completed within 24 hours |
| /work/worktrees/task-d5fd03f4 | Succeeded | 2026-11-06T04:48:31.283085Z | Terminal completed within 24 hours |
| /work/worktrees/task-d67a21b1 | Succeeded | 2026-11-06T00:54:54.485913Z | Terminal completed within 24 hours |
| /work/worktrees/task-d69fe301 | Failed | 2026-11-05T23:09:41.256436Z | Terminal completed within 24 hours |
| /work/worktrees/task-d8af9f97 | Succeeded | 2026-11-06T05:29:33.852065Z | Terminal completed within 24 hours |
| /work/worktrees/task-d8bdfd3a | Failed | 2026-11-06T00:07:52.342505Z | Terminal completed within 24 hours |
| /work/worktrees/task-d8f70e4c | Succeeded | 2026-11-05T22:51:39.429129Z | Terminal completed within 24 hours |
| /work/worktrees/task-e0a09fa9 | Succeeded | 2026-11-06T12:11:36.934372Z | Explicit protected task |
| /work/worktrees/task-e0c73157 | Working | 2026-11-06T06:00:35.508559Z | Explicit protected task |
| /work/worktrees/task-e0c73157-parity-base | Working | 2026-11-06T06:00:35.508559Z | Explicit protected task |
| /work/worktrees/task-e469bdcd | Succeeded | 2026-11-06T11:20:41.791323Z | Terminal completed within 24 hours |
| /work/worktrees/task-e4cb0636 | Succeeded | 2026-11-05T22:01:52.527488Z | Terminal completed within 24 hours |
| /work/worktrees/task-e830e940 | Succeeded | 2026-11-06T07:13:21.513083Z | Terminal completed within 24 hours |
| /work/worktrees/task-e8915f2c | Canceled | 2026-11-06T04:40:05.637081Z | Terminal completed within 24 hours |
| /work/worktrees/task-f565efa1 | Succeeded | 2026-11-06T11:37:05.012313Z | Terminal completed within 24 hours |
| /work/worktrees/task-f822e84d | Succeeded | 2026-11-05T22:22:00.559677Z | Terminal completed within 24 hours |
| /work/worktrees/task-f8ac5aa5 | Succeeded | 2026-11-05T20:15:48.110306Z | Terminal completed within 24 hours |
| /work/worktrees/task-fc1834e8 | Canceled | 2026-11-06T01:46:05.052818Z | Terminal completed within 24 hours |
| /work/worktrees/task-fd0ebdc3 | Succeeded | 2026-11-06T13:45:10.036921Z | Explicit protected task |
| /work/worktrees/task-fee547cb | Succeeded | 2026-11-06T08:57:48.680933Z | Explicit protected task |

## Deleted worktrees

| Absolute path | State at deletion | completedAt UTC |
|---|---|---|
| /work/worktrees/task-00a0b2d1 | Succeeded | 2026-11-04T12:44:05.920555Z |
| /work/worktrees/task-03e7cd6d | Succeeded | 2026-11-05T00:24:12.140202Z |
| /work/worktrees/task-05081946 | Succeeded | 2026-11-05T10:45:02.769447Z |
| /work/worktrees/task-08a2e7f3 | Succeeded | 2026-11-04T20:21:22.005014Z |
| /work/worktrees/task-08cca1b8 | Succeeded | 2026-11-04T11:24:59.106092Z |
| /work/worktrees/task-0c0e9de1 | Succeeded | 2026-11-05T08:32:24.423574Z |
| /work/worktrees/task-0c3b7de3 | Succeeded | 2026-11-04T11:58:28.531316Z |
| /work/worktrees/task-0c839994 | Succeeded | 2026-11-05T11:25:05.219557Z |
| /work/worktrees/task-0cd23a4c | Canceled | 2026-11-05T08:18:16.60105Z |
| /work/worktrees/task-0e8a1b50 | Succeeded | 2026-11-04T18:00:46.125014Z |
| /work/worktrees/task-0efa38ca | Succeeded | 2026-11-04T16:18:54.692381Z |
| /work/worktrees/task-11a0b0b8 | Succeeded | 2026-11-05T02:17:58.269312Z |
| /work/worktrees/task-11a6d8bb | Succeeded | 2026-11-05T05:31:33.024917Z |
| /work/worktrees/task-122f2dcf | Succeeded | 2026-11-04T22:42:22.833083Z |
| /work/worktrees/task-12c87cda | Succeeded | 2026-11-04T07:35:19.951608Z |
| /work/worktrees/task-133255e3 | Succeeded | 2026-11-05T07:58:37.022888Z |
| /work/worktrees/task-189595a5 | Succeeded | 2026-11-04T09:48:22.899113Z |
| /work/worktrees/task-1999cb95 | Succeeded | 2026-11-05T12:15:04.887258Z |
| /work/worktrees/task-19a14614 | Succeeded | 2026-11-04T17:42:29.339474Z |
| /work/worktrees/task-1c022492 | Succeeded | 2026-11-04T14:03:20.561725Z |
| /work/worktrees/task-1c0653a5 | Succeeded | 2026-11-04T19:15:53.107577Z |
| /work/worktrees/task-1c2d93f1 | Succeeded | 2026-11-04T16:10:54.056839Z |
| /work/worktrees/task-1c44bc80 | Succeeded | 2026-11-04T16:33:38.874836Z |
| /work/worktrees/task-1c4f8dd2 | Succeeded | 2026-11-04T23:16:26.593696Z |
| /work/worktrees/task-1c7946d0 | Succeeded | 2026-11-05T13:02:59.715746Z |
| /work/worktrees/task-1cf13574 | Succeeded | 2026-11-04T09:00:18.709796Z |
| /work/worktrees/task-1dc468a4 | Succeeded | 2026-11-04T07:49:53.597835Z |
| /work/worktrees/task-1edbc845 | Canceled | 2026-11-05T04:39:10.024023Z |
| /work/worktrees/task-1ee29680 | Succeeded | 2026-11-04T19:09:02.908765Z |
| /work/worktrees/task-212363b0 | Succeeded | 2026-11-04T09:42:43.501167Z |
| /work/worktrees/task-23889c9b | Succeeded | 2026-11-05T08:20:55.862065Z |
| /work/worktrees/task-23fce9a6 | Succeeded | 2026-11-04T16:53:11.347192Z |
| /work/worktrees/task-27ca134f | Succeeded | 2026-11-04T19:39:31.805587Z |
| /work/worktrees/task-27e2f0b5 | Succeeded | 2026-11-05T05:42:27.518826Z |
| /work/worktrees/task-291753f8 | Succeeded | 2026-11-04T16:35:30.341944Z |
| /work/worktrees/task-29664acc | Succeeded | 2026-11-04T09:57:37.902423Z |
| /work/worktrees/task-29cd334a | Succeeded | 2026-11-04T07:26:46.226883Z |
| /work/worktrees/task-2e0375f1 | Succeeded | 2026-11-05T07:19:44.102816Z |
| /work/worktrees/task-2ede815a | Succeeded | 2026-11-05T06:09:41.882261Z |
| /work/worktrees/task-2f58c570 | Succeeded | 2026-11-04T14:23:30.409913Z |
| /work/worktrees/task-306148cc | Succeeded | 2026-11-04T11:08:12.893501Z |
| /work/worktrees/task-314297be | Canceled | 2026-11-04T12:46:08.93082Z |
| /work/worktrees/task-31e20f5a | Succeeded | 2026-11-04T09:18:20.686805Z |
| /work/worktrees/task-35623cb8 | Succeeded | 2026-11-04T08:34:43.913042Z |
| /work/worktrees/task-362f7434 | Succeeded | 2026-11-05T01:02:29.450966Z |
| /work/worktrees/task-36b3dc05 | Succeeded | 2026-11-04T16:20:37.960985Z |
| /work/worktrees/task-374d7fa0 | Succeeded | 2026-11-05T05:01:34.941276Z |
| /work/worktrees/task-37b01055 | Succeeded | 2026-11-04T12:42:04.650806Z |
| /work/worktrees/task-37f1e778 | Succeeded | 2026-11-05T13:48:26.412045Z |
| /work/worktrees/task-3a179edb | Succeeded | 2026-11-04T13:52:36.85552Z |
| /work/worktrees/task-3b906877 | Canceled | 2026-11-05T06:39:51.431152Z |
| /work/worktrees/task-3c2aac7d | Succeeded | 2026-11-05T01:03:50.844508Z |
| /work/worktrees/task-3e640ea9 | Succeeded | 2026-11-05T06:30:09.618682Z |
| /work/worktrees/task-3f52b55a | Succeeded | 2026-11-05T02:18:49.655448Z |
| /work/worktrees/task-3fd6747e | Failed | 2026-11-04T23:18:19.686023Z |
| /work/worktrees/task-4047a80b | Succeeded | 2026-11-04T15:44:01.235767Z |
| /work/worktrees/task-406bca79 | Succeeded | 2026-11-04T20:07:09.671625Z |
| /work/worktrees/task-449c541d | Succeeded | 2026-11-04T14:23:51.188211Z |
| /work/worktrees/task-459edfef | Succeeded | 2026-11-04T20:09:03.685629Z |
| /work/worktrees/task-47169b54 | Succeeded | 2026-11-05T10:21:52.773537Z |
| /work/worktrees/task-47d31366 | Succeeded | 2026-11-05T02:54:21.566748Z |
| /work/worktrees/task-47f9be84 | Succeeded | 2026-11-04T05:08:52.128337Z |
| /work/worktrees/task-4a75ce55 | Succeeded | 2026-11-04T09:03:06.654645Z |
| /work/worktrees/task-4af5405a | Succeeded | 2026-11-04T11:50:02.980578Z |
| /work/worktrees/task-4b9d4604 | Succeeded | 2026-11-04T16:29:02.60242Z |
| /work/worktrees/task-4c2d90db | Succeeded | 2026-11-04T11:57:28.267799Z |
| /work/worktrees/task-52410842 | Succeeded | 2026-11-04T20:06:15.194981Z |
| /work/worktrees/task-5242d604 | Succeeded | 2026-11-05T02:23:25.95076Z |
| /work/worktrees/task-56b0f9aa | Succeeded | 2026-11-04T23:31:04.906508Z |
| /work/worktrees/task-56bf971d | Succeeded | 2026-11-04T14:05:39.569597Z |
| /work/worktrees/task-577b2ee0 | Succeeded | 2026-11-04T18:36:03.999082Z |
| /work/worktrees/task-5baa0ee3 | Succeeded | 2026-11-05T10:28:45.215259Z |
| /work/worktrees/task-5c10955b | Succeeded | 2026-11-05T06:57:04.67344Z |
| /work/worktrees/task-5e05fc80 | Succeeded | 2026-11-05T01:19:59.960059Z |
| /work/worktrees/task-605c6f4e | Succeeded | 2026-11-05T01:56:28.746413Z |
| /work/worktrees/task-606b7d8d | Succeeded | 2026-11-05T00:28:57.932823Z |
| /work/worktrees/task-60cb0a38 | Succeeded | 2026-11-04T09:20:42.183661Z |
| /work/worktrees/task-613f2230 | Succeeded | 2026-11-05T01:07:08.899512Z |
| /work/worktrees/task-64fb3f5d | Canceled | 2026-11-05T04:39:09.23004Z |
| /work/worktrees/task-6578802d | Succeeded | 2026-11-04T13:35:42.212501Z |
| /work/worktrees/task-67077ff7 | Succeeded | 2026-11-05T13:41:23.239938Z |
| /work/worktrees/task-692b70e4 | Succeeded | 2026-11-04T10:51:34.823164Z |
| /work/worktrees/task-6949514d | Succeeded | 2026-11-04T16:15:50.0529Z |
| /work/worktrees/task-6c1b0f87 | Succeeded | 2026-11-04T19:02:25.624711Z |
| /work/worktrees/task-6dfad742 | Succeeded | 2026-11-04T21:06:18.547228Z |
| /work/worktrees/task-71dacd6c | Succeeded | 2026-11-05T07:20:49.460585Z |
| /work/worktrees/task-727a57dd | Canceled | 2026-11-05T06:11:16.151927Z |
| /work/worktrees/task-7289f7c7 | Succeeded | 2026-11-04T11:59:43.415145Z |
| /work/worktrees/task-73837e64 | Succeeded | 2026-11-04T11:16:18.767305Z |
| /work/worktrees/task-73c027ae | Succeeded | 2026-11-05T02:57:37.808916Z |
| /work/worktrees/task-73c94e0b | Succeeded | 2026-11-05T01:32:55.659035Z |
| /work/worktrees/task-75c9410c | Succeeded | 2026-11-04T12:26:56.578316Z |
| /work/worktrees/task-75d1358a | Succeeded | 2026-11-05T08:59:57.510128Z |
| /work/worktrees/task-7ab0d65a | Succeeded | 2026-11-04T13:10:09.127658Z |
| /work/worktrees/task-7c7132b3 | Failed | 2026-11-04T23:34:09.674506Z |
| /work/worktrees/task-7eb005f3 | Succeeded | 2026-11-04T09:32:38.360261Z |
| /work/worktrees/task-7ef9aa63 | Succeeded | 2026-11-05T10:26:30.34398Z |
| /work/worktrees/task-7f675cf6 | Succeeded | 2026-11-04T15:52:26.993266Z |
| /work/worktrees/task-801ad6c3 | Succeeded | 2026-11-04T17:52:17.687551Z |
| /work/worktrees/task-821a7c5b | Succeeded | 2026-11-04T14:59:46.799557Z |
| /work/worktrees/task-8347d02a | Succeeded | 2026-11-04T10:23:06.711342Z |
| /work/worktrees/task-84b44899 | Succeeded | 2026-11-04T10:04:44.991758Z |
| /work/worktrees/task-8592d536 | Canceled | 2026-11-05T04:39:10.842255Z |
| /work/worktrees/task-86a941a3 | Succeeded | 2026-11-05T02:47:29.598881Z |
| /work/worktrees/task-8719af3e | Succeeded | 2026-11-04T14:04:10.714218Z |
| /work/worktrees/task-871c4033 | Succeeded | 2026-11-05T13:43:15.638026Z |
| /work/worktrees/task-87232051 | Succeeded | 2026-11-04T12:53:09.097888Z |
| /work/worktrees/task-880edccc | Canceled | 2026-11-05T04:40:34.107694Z |
| /work/worktrees/task-8a2a0e71 | Succeeded | 2026-11-05T14:33:51.893182Z |
| /work/worktrees/task-8ac2299c | Succeeded | 2026-11-04T18:11:12.052107Z |
| /work/worktrees/task-8acb8a4d | Succeeded | 2026-11-04T09:06:39.686569Z |
| /work/worktrees/task-8b609b8a | Succeeded | 2026-11-04T18:50:07.272716Z |
| /work/worktrees/task-931a28cf | Succeeded | 2026-11-04T16:33:52.033829Z |
| /work/worktrees/task-93a16649 | Succeeded | 2026-11-05T02:16:14.76859Z |
| /work/worktrees/task-93aae0fa | Succeeded | 2026-11-05T09:21:39.835297Z |
| /work/worktrees/task-9602645b | Succeeded | 2026-11-04T19:45:37.890766Z |
| /work/worktrees/task-96f2fc86 | Succeeded | 2026-11-04T07:45:57.191355Z |
| /work/worktrees/task-977203e7 | Succeeded | 2026-11-04T18:49:10.1203Z |
| /work/worktrees/task-9843b768 | Succeeded | 2026-11-04T06:09:39.750903Z |
| /work/worktrees/task-99e23919 | Succeeded | 2026-11-04T21:37:48.271442Z |
| /work/worktrees/task-9c415aaf | Succeeded | 2026-11-05T12:51:21.632867Z |
| /work/worktrees/task-9e4c7963 | Succeeded | 2026-11-05T05:17:37.501343Z |
| /work/worktrees/task-9f03d97e | Succeeded | 2026-11-05T14:06:33.47817Z |
| /work/worktrees/task-9f577891 | Succeeded | 2026-11-04T20:14:27.33127Z |
| /work/worktrees/task-a22bdfed | Succeeded | 2026-11-04T14:47:13.885441Z |
| /work/worktrees/task-a2cfcdc1 | Succeeded | 2026-11-05T08:13:34.81351Z |
| /work/worktrees/task-a41890aa | Succeeded | 2026-11-04T15:39:03.40971Z |
| /work/worktrees/task-a42d888f | Succeeded | 2026-11-04T13:19:18.360418Z |
| /work/worktrees/task-a4933b4f | Succeeded | 2026-11-05T08:13:28.307006Z |
| /work/worktrees/task-a695169d | Succeeded | 2026-11-04T13:33:37.546911Z |
| /work/worktrees/task-a6c5c347 | Succeeded | 2026-11-04T19:06:17.129553Z |
| /work/worktrees/task-a8d643be | Succeeded | 2026-11-05T08:37:17.158017Z |
| /work/worktrees/task-aa58fb53 | Failed | 2026-11-04T09:28:49.529577Z |
| /work/worktrees/task-adbd81fe | Succeeded | 2026-11-04T23:50:30.579337Z |
| /work/worktrees/task-ae6457ac | Succeeded | 2026-11-04T21:20:48.292317Z |
| /work/worktrees/task-b02d7898 | Succeeded | 2026-11-04T18:07:43.604938Z |
| /work/worktrees/task-b054e10c | Succeeded | 2026-11-04T07:38:56.619419Z |
| /work/worktrees/task-b055e88c | Succeeded | 2026-11-05T08:56:29.420673Z |
| /work/worktrees/task-b1cdbed3 | Succeeded | 2026-11-04T11:43:52.223767Z |
| /work/worktrees/task-b3bd8985 | Succeeded | 2026-11-04T13:17:34.670558Z |
| /work/worktrees/task-b3c01691 | Succeeded | 2026-11-04T17:05:12.560172Z |
| /work/worktrees/task-b578f67a | Succeeded | 2026-11-04T13:00:44.048546Z |
| /work/worktrees/task-b5b7324a | Failed | 2026-11-05T03:59:42.878953Z |
| /work/worktrees/task-b7350767 | Canceled | 2026-11-05T12:53:40.70457Z |
| /work/worktrees/task-ba8ec885 | Succeeded | 2026-11-04T19:42:04.518606Z |
| /work/worktrees/task-bbd446f6 | Succeeded | 2026-11-05T03:12:06.680218Z |
| /work/worktrees/task-bdaa665c | Succeeded | 2026-11-04T13:24:43.401896Z |
| /work/worktrees/task-c0cea8f5 | Succeeded | 2026-11-04T09:20:15.262874Z |
| /work/worktrees/task-c1e2a660 | Succeeded | 2026-11-05T12:03:59.083601Z |
| /work/worktrees/task-c3ba1792 | Succeeded | 2026-11-05T06:24:27.017213Z |
| /work/worktrees/task-c5d82758 | Succeeded | 2026-11-05T12:40:04.101339Z |
| /work/worktrees/task-c781f7f8 | Canceled | 2026-11-05T11:35:05.422612Z |
| /work/worktrees/task-c7f02607 | Succeeded | 2026-11-04T08:55:15.972104Z |
| /work/worktrees/task-c8274f67 | Succeeded | 2026-11-04T15:00:55.74738Z |
| /work/worktrees/task-c9ebcc1d | Succeeded | 2026-11-05T06:33:09.981358Z |
| /work/worktrees/task-cb6b4d80 | Succeeded | 2026-11-04T11:16:44.174212Z |
| /work/worktrees/task-ccfe679f | Succeeded | 2026-11-04T14:11:19.180888Z |
| /work/worktrees/task-d19080aa | Succeeded | 2026-11-04T20:54:06.857026Z |
| /work/worktrees/task-d2564da4 | Succeeded | 2026-11-04T20:04:07.018092Z |
| /work/worktrees/task-d5e8910e | Succeeded | 2026-11-05T03:37:06.54814Z |
| /work/worktrees/task-d6e48431 | Succeeded | 2026-11-05T06:34:19.980799Z |
| /work/worktrees/task-d707a486 | Succeeded | 2026-11-04T14:30:59.776625Z |
| /work/worktrees/task-d933f966 | Failed | 2026-11-05T11:15:23.065899Z |
| /work/worktrees/task-da743107 | Succeeded | 2026-11-04T08:23:36.307713Z |
| /work/worktrees/task-dc12c438 | Succeeded | 2026-11-04T18:54:45.449846Z |
| /work/worktrees/task-dd35b058 | Canceled | 2026-11-04T12:46:22.046901Z |
| /work/worktrees/task-e056f0ff | Succeeded | 2026-11-04T18:53:12.577042Z |
| /work/worktrees/task-e23c0f83 | Succeeded | 2026-11-04T12:40:58.546641Z |
| /work/worktrees/task-e24018e3 | Succeeded | 2026-11-04T22:49:35.747679Z |
| /work/worktrees/task-e36a0edf | Succeeded | 2026-11-05T00:48:20.268318Z |
| /work/worktrees/task-e379bcd9 | Succeeded | 2026-11-04T12:36:22.31197Z |
| /work/worktrees/task-e38f32c6 | Succeeded | 2026-11-04T05:43:42.355412Z |
| /work/worktrees/task-e3c30656 | Succeeded | 2026-11-05T09:33:06.183921Z |
| /work/worktrees/task-e438ae14 | Succeeded | 2026-11-05T05:00:42.9621Z |
| /work/worktrees/task-e45ab432 | Succeeded | 2026-11-04T07:57:22.352127Z |
| /work/worktrees/task-e5258e8c | Succeeded | 2026-11-04T11:38:08.732771Z |
| /work/worktrees/task-e778bd07 | Succeeded | 2026-11-04T23:46:32.644013Z |
| /work/worktrees/task-e80ac465 | Succeeded | 2026-11-05T03:37:39.788251Z |
| /work/worktrees/task-e8242d52 | Succeeded | 2026-11-04T19:36:53.360203Z |
| /work/worktrees/task-ea048200 | Succeeded | 2026-11-05T10:22:54.772793Z |
| /work/worktrees/task-eab3a4fc | Succeeded | 2026-11-04T21:14:21.210278Z |
| /work/worktrees/task-edbaee26 | Succeeded | 2026-11-05T13:04:54.219188Z |
| /work/worktrees/task-f059674d | Succeeded | 2026-11-05T05:29:02.971065Z |
| /work/worktrees/task-f2976027 | Succeeded | 2026-11-04T19:16:13.93719Z |
| /work/worktrees/task-f2ed8c32 | Succeeded | 2026-11-04T12:22:09.683893Z |
| /work/worktrees/task-f2f59326 | Succeeded | 2026-11-04T11:41:02.805624Z |
| /work/worktrees/task-f303b3ec | Succeeded | 2026-11-04T12:50:30.545122Z |
| /work/worktrees/task-f35f1237 | Succeeded | 2026-11-04T22:02:04.604999Z |
| /work/worktrees/task-f54a5386 | Succeeded | 2026-11-05T11:33:10.79717Z |
| /work/worktrees/task-f5e96e75 | Succeeded | 2026-11-04T17:45:02.382151Z |
| /work/worktrees/task-f5eb193f | Succeeded | 2026-11-04T14:37:05.763846Z |
| /work/worktrees/task-f674c79a | Canceled | 2026-11-05T04:39:11.920461Z |
| /work/worktrees/task-f67b2762 | Succeeded | 2026-11-04T06:09:25.428233Z |
| /work/worktrees/task-f6e3cc3c | Succeeded | 2026-11-04T13:49:16.686384Z |
| /work/worktrees/task-f7703a17 | Succeeded | 2026-11-04T10:10:21.408144Z |
| /work/worktrees/task-f85985fd | Succeeded | 2026-11-05T11:57:45.424975Z |
| /work/worktrees/task-f893d1a3 | Succeeded | 2026-11-04T08:43:23.600051Z |
| /work/worktrees/task-fada17c6 | Succeeded | 2026-11-05T12:22:57.426184Z |
| /work/worktrees/task-fc2bcb80 | Succeeded | 2026-11-05T13:13:56.500118Z |
| /work/worktrees/task-ff3ef62e | Succeeded | 2026-11-05T06:21:13.030663Z |
| /work/worktrees/task-ff7ab869 | Succeeded | 2026-11-04T20:36:06.672096Z |
| /work/worktrees/task-ff8f8f3f | Succeeded | 2026-11-04T11:38:29.695111Z |

## Evidence and remaining decisions

External evidence: `/work/evidence/task-4c98a708`. Files include `start.json`, `inventory.json`, the project/fleet/open-task-derived observations, `batch-01.json` through `batch-09.json`, `deletions.jsonl`, `salvage.jsonl`, `salvage-verified.json`, `origin-heads-after.txt`, `prune.json`, `worktrees-after.txt`, `df-before.txt`, `df-after.txt`, and `reconciliation.json`. Generated payloads remain outside Git; this Markdown is the durable narrative report.

No cleanup decision remains. Keep the retention-setting question with Investigate; this task changed no setting.

--- next stage ---
next: done
handoff: Cleanup complete; retention-setting question remains with Investigate.
artifact: docs/investigations/2026-11-06-server2-worktree-cleanup-4c98a708.md
