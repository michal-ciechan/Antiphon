# Server2 worktree cleanup, second attempt — 2026-11-05

Deleted **344 of 505 inventoried worktrees**, retaining **161**. All deletions succeeded; no fallback recursive deletions were needed. Removed **278.931 GB / 259.775 GiB** of inventoried allocated files. Filesystem available space increased by **276.893 GB / 257.877 GiB**; disk usage fell from **82% to 44%**. No builds or tests were run.

## Authority and eligibility

Task: `9e4c7963` (`9e4c7963-2d06-468a-bf62-6e23e1c30e35`). Assigned branch: `feat/card-task-9e4c7963`. Execution took place directly in the server2 runner mirror, with evidence under `/work/evidence/task-9e4c7963`.

The operator's standing instruction authorized this cleanup without another approval. It was exercised without asking for confirmation. This attempt replaces the first attempt's filesystem-touch age with the owning task's server-recorded terminal time.

Only literal absolute directories matching `^/work/worktrees/task-[0-9a-f]{8}(-[A-Za-z0-9._-]+)?$` were eligible. The eight hexadecimal characters bind the directory, including every suffix, to its task UUID. A terminal task means Succeeded, Failed or Canceled; its `completedAt` must be strictly earlier than **2026-11-04T05:06:23.381Z**, a fixed 24-hour cutoff recorded at inventory start **2026-11-05T05:06:23.380Z**. A task exactly at the cutoff is retained.

The orphan rule was directory birth time or oldest ordinary-file ctime strictly older than 48 hours (cutoff **2026-11-03T05:06:23.381Z**), excluding `.git` metadata. **No orphans were found**, so no deletion relied on filesystem age. Git metadata mtimes were never used to establish task age.

Working, Dispatched, Queued and Blocked tasks, standing agents/orchestrators, and this cleanup's own worktree were protected. The standing-agent lookup included always-on agents, standing specialist owners and orchestrator names; 8 standing entries were observed and none owned an eligible worktree. No directories outside the validated worktree list were deleted. No Docker objects or volumes were changed. Repository metadata was updated only by the requested fetch, normal Git worktree removal and prune.

## Inventory and counts

The first attempt's `inventory.json` and `excluded-paths.json` were the starting list, unioned with a fresh directory listing. All 17 previously excluded suffixed paths now match the authorized pattern and were deleted. Every one of the 505 paths mapped unambiguously to a task; there were no Git ownership, HEAD or size-measurement errors.

Read the requested project-scoped list since September 1 with `includeChecks=true`, then the fleet-wide list with the same cutoff and checks included. The project list returned 7,281 rows; the fleet list returned 24,074 rows with `excluded.total=0`. List rows expose status and timestamps directly; immediate pre-deletion task-detail reads expose them under `summary`. Each selected task's full ID, terminal state and completion cutoff were rechecked from task detail before deletion. Standing-agent observations were refreshed per batch.

| Measure | Count |
|---|---:|
| Inventoried literal worktree paths | 505 |
| Exact unsuffixed paths | 488 |
| Suffixed paths | 17 |
| Eligible and deleted | 344 |
| Retained | 161 |
| Orphan / ambiguous task prefix | 0 / 0 |
| Failed removal / fallback deletion | 0 / 0 |
| Salvage attempts / branches pushed / failures | 0 / 0 / 0 |
| Deleted paths still present / registered after prune | 0 / 0 |
| Retained paths missing at reconciliation | 0 |

Deleted task states: 336 Succeeded, 3 Canceled, 5 Failed. Counts refer to directories; the suffixes share their parent task's terminal state and completion timestamp.

| Retention reason | Count |
|---|---:|
| Terminal completedAt within 24 hours | 152 |
| Protected task: Dispatched | 7 |
| Protected task: Blocked | 1 |
| Own worktree | 1 |

## Salvage, removal and final reconciliation

Fetched origin once in the owning repository, `/work/repos/antiphon`, before containment checks. Every one of the 344 deleted HEADs was contained in at least one `origin/` ref according to `git -C <worktree> branch -r --contains HEAD`; no salvage push was necessary. No branch was deleted.

The validated `delete-list.json` supplied one literal path per removal invocation. Each directory's `.git` pointer and shared common directory were rechecked to establish its owning repository. Ran `git -C /work/repos/antiphon worktree remove --force <one-validated-path>` in 18 batches (17 batches of 20 and one of 4), recording per-path command outcomes and inventoried allocated bytes. All 344 Git removals exited 0 and left their paths absent; `rm -rf` was never invoked.

Commands ran as supervised foreground children and were awaited to completion under the execution contract; no detached jobs or unowned processes were left. Batch drivers ran sequentially. The brief's detached/nohup preference was superseded by the mandatory foreground execution contract. Disk usage was captured before and after every batch.

Ran `git -C /work/repos/antiphon worktree prune --verbose` once after removal; it exited 0 with no residual entries to report. Reconciliation at **2026-11-05T05:16:01.018Z** found all 344 candidate paths absent, no corresponding worktree registrations, all 161 retained paths present and no new worktree paths relative to the inventory. This verifies cleanup at that observation time on a running shared host.

| Batch | Deleted | Inventoried allocated GB removed | Filesystem used after |
|---|---:|---:|---:|
| 1 | 20 | 13.351 | 80% |
| 2 | 20 | 14.900 | 78% |
| 3 | 20 | 18.660 | 75% |
| 4 | 20 | 20.742 | 73% |
| 5 | 20 | 20.426 | 70% |
| 6 | 20 | 13.881 | 68% |
| 7 | 20 | 17.511 | 66% |
| 8 | 20 | 16.819 | 63% |
| 9 | 20 | 13.303 | 61% |
| 10 | 20 | 15.240 | 59% |
| 11 | 20 | 19.782 | 57% |
| 12 | 20 | 16.377 | 54% |
| 13 | 20 | 12.680 | 53% |
| 14 | 20 | 13.608 | 51% |
| 15 | 20 | 18.950 | 48% |
| 16 | 20 | 11.972 | 46% |
| 17 | 20 | 16.798 | 44% |
| 18 | 4 | 3.931 | 44% |

The sum of per-directory allocated sizes is 278,931,058,688 bytes. The observed filesystem used-space reduction is 276,892,893,184 bytes. These are distinct measurements on a live shared filesystem, so the report does not attribute every byte of the `df` delta solely to this cleanup.

## Filesystem before and after

Before, in bytes:

```text
Filesystem                           1B-blocks         Used    Available Use% Mounted on
/dev/mapper/ubuntu--vg-ubuntu--lv 756634804224 589491601408 136332271616  82% /work
```

After, in bytes:

```text
Filesystem                           1B-blocks         Used    Available Use% Mounted on
/dev/mapper/ubuntu--vg-ubuntu--lv 756634804224 312598708224 413225164800  44% /work
```

## Retained worktrees

The following list records every retained path, owning task prefix, server state, server completion timestamp, inventoried allocated size and reason. Full task UUIDs and repository identities are preserved in `inventory.json` and `reconciliation.json`.

| Path | Task | State | completedAt (UTC) | Allocated GB | Reason |
|---|---|---|---|---:|---|
| /work/worktrees/task-00a0b2d1 | 00a0b2d1 | Succeeded | 2026-11-04T12:44:05.920555Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-03e7cd6d | 03e7cd6d | Succeeded | 2026-11-05T00:24:12.140202Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-08a2e7f3 | 08a2e7f3 | Succeeded | 2026-11-04T20:21:22.005014Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-08cca1b8 | 08cca1b8 | Succeeded | 2026-11-04T11:24:59.106092Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-0c3b7de3 | 0c3b7de3 | Succeeded | 2026-11-04T11:58:28.531316Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-0e8a1b50 | 0e8a1b50 | Succeeded | 2026-11-04T18:00:46.125014Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-0efa38ca | 0efa38ca | Succeeded | 2026-11-04T16:18:54.692381Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-11a0b0b8 | 11a0b0b8 | Succeeded | 2026-11-05T02:17:58.269312Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-11a6d8bb | 11a6d8bb | Dispatched | — | 1.331 | Protected task: Dispatched |
| /work/worktrees/task-122f2dcf | 122f2dcf | Succeeded | 2026-11-04T22:42:22.833083Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-12c87cda | 12c87cda | Succeeded | 2026-11-04T07:35:19.951608Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-189595a5 | 189595a5 | Succeeded | 2026-11-04T09:48:22.899113Z | 0.240 | Terminal completedAt within 24 hours |
| /work/worktrees/task-19a14614 | 19a14614 | Succeeded | 2026-11-04T17:42:29.339474Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1c022492 | 1c022492 | Succeeded | 2026-11-04T14:03:20.561725Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1c0653a5 | 1c0653a5 | Succeeded | 2026-11-04T19:15:53.107577Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1c2d93f1 | 1c2d93f1 | Succeeded | 2026-11-04T16:10:54.056839Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1c44bc80 | 1c44bc80 | Succeeded | 2026-11-04T16:33:38.874836Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1c4f8dd2 | 1c4f8dd2 | Succeeded | 2026-11-04T23:16:26.593696Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1cf13574 | 1cf13574 | Succeeded | 2026-11-04T09:00:18.709796Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1dc468a4 | 1dc468a4 | Succeeded | 2026-11-04T07:49:53.597835Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1edbc845 | 1edbc845 | Canceled | 2026-11-05T04:39:10.024023Z | 2.171 | Terminal completedAt within 24 hours |
| /work/worktrees/task-1ee29680 | 1ee29680 | Succeeded | 2026-11-04T19:09:02.908765Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-212363b0 | 212363b0 | Succeeded | 2026-11-04T09:42:43.501167Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-23889c9b | 23889c9b | Dispatched | — | 2.111 | Protected task: Dispatched |
| /work/worktrees/task-23fce9a6 | 23fce9a6 | Succeeded | 2026-11-04T16:53:11.347192Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-27ca134f | 27ca134f | Succeeded | 2026-11-04T19:39:31.805587Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-27e2f0b5 | 27e2f0b5 | Dispatched | — | 0.399 | Protected task: Dispatched |
| /work/worktrees/task-291753f8 | 291753f8 | Succeeded | 2026-11-04T16:35:30.341944Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-29664acc | 29664acc | Succeeded | 2026-11-04T09:57:37.902423Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-29cd334a | 29cd334a | Succeeded | 2026-11-04T07:26:46.226883Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-2ede815a | 2ede815a | Dispatched | — | 2.533 | Protected task: Dispatched |
| /work/worktrees/task-2f58c570 | 2f58c570 | Succeeded | 2026-11-04T14:23:30.409913Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-306148cc | 306148cc | Succeeded | 2026-11-04T11:08:12.893501Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-314297be | 314297be | Canceled | 2026-11-04T12:46:08.93082Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-31e20f5a | 31e20f5a | Succeeded | 2026-11-04T09:18:20.686805Z | 0.260 | Terminal completedAt within 24 hours |
| /work/worktrees/task-34125850 | 34125850 | Blocked | 2026-11-04T10:08:10.464915Z | 0.236 | Protected task: Blocked |
| /work/worktrees/task-35623cb8 | 35623cb8 | Succeeded | 2026-11-04T08:34:43.913042Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-362f7434 | 362f7434 | Succeeded | 2026-11-05T01:02:29.450966Z | 2.086 | Terminal completedAt within 24 hours |
| /work/worktrees/task-36b3dc05 | 36b3dc05 | Succeeded | 2026-11-04T16:20:37.960985Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-374d7fa0 | 374d7fa0 | Succeeded | 2026-11-05T05:01:34.941276Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-37b01055 | 37b01055 | Succeeded | 2026-11-04T12:42:04.650806Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-3a179edb | 3a179edb | Succeeded | 2026-11-04T13:52:36.85552Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-3c2aac7d | 3c2aac7d | Succeeded | 2026-11-05T01:03:50.844508Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-3f52b55a | 3f52b55a | Succeeded | 2026-11-05T02:18:49.655448Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-3fd6747e | 3fd6747e | Failed | 2026-11-04T23:18:19.686023Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-4047a80b | 4047a80b | Succeeded | 2026-11-04T15:44:01.235767Z | 0.450 | Terminal completedAt within 24 hours |
| /work/worktrees/task-406bca79 | 406bca79 | Succeeded | 2026-11-04T20:07:09.671625Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-449c541d | 449c541d | Succeeded | 2026-11-04T14:23:51.188211Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-459edfef | 459edfef | Succeeded | 2026-11-04T20:09:03.685629Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-47d31366 | 47d31366 | Succeeded | 2026-11-05T02:54:21.566748Z | 0.292 | Terminal completedAt within 24 hours |
| /work/worktrees/task-47f9be84 | 47f9be84 | Succeeded | 2026-11-04T05:08:52.128337Z | 1.970 | Terminal completedAt within 24 hours |
| /work/worktrees/task-4a75ce55 | 4a75ce55 | Succeeded | 2026-11-04T09:03:06.654645Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-4af5405a | 4af5405a | Succeeded | 2026-11-04T11:50:02.980578Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-4b9d4604 | 4b9d4604 | Succeeded | 2026-11-04T16:29:02.60242Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-4c2d90db | 4c2d90db | Succeeded | 2026-11-04T11:57:28.267799Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-52410842 | 52410842 | Succeeded | 2026-11-04T20:06:15.194981Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-5242d604 | 5242d604 | Succeeded | 2026-11-05T02:23:25.95076Z | 0.292 | Terminal completedAt within 24 hours |
| /work/worktrees/task-56b0f9aa | 56b0f9aa | Succeeded | 2026-11-04T23:31:04.906508Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-56bf971d | 56bf971d | Succeeded | 2026-11-04T14:05:39.569597Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-577b2ee0 | 577b2ee0 | Succeeded | 2026-11-04T18:36:03.999082Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-5e05fc80 | 5e05fc80 | Succeeded | 2026-11-05T01:19:59.960059Z | 0.934 | Terminal completedAt within 24 hours |
| /work/worktrees/task-605c6f4e | 605c6f4e | Succeeded | 2026-11-05T01:56:28.746413Z | 0.668 | Terminal completedAt within 24 hours |
| /work/worktrees/task-606b7d8d | 606b7d8d | Succeeded | 2026-11-05T00:28:57.932823Z | 0.668 | Terminal completedAt within 24 hours |
| /work/worktrees/task-60cb0a38 | 60cb0a38 | Succeeded | 2026-11-04T09:20:42.183661Z | 0.240 | Terminal completedAt within 24 hours |
| /work/worktrees/task-613f2230 | 613f2230 | Succeeded | 2026-11-05T01:07:08.899512Z | 2.086 | Terminal completedAt within 24 hours |
| /work/worktrees/task-64fb3f5d | 64fb3f5d | Canceled | 2026-11-05T04:39:09.23004Z | 2.086 | Terminal completedAt within 24 hours |
| /work/worktrees/task-6578802d | 6578802d | Succeeded | 2026-11-04T13:35:42.212501Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-692b70e4 | 692b70e4 | Succeeded | 2026-11-04T10:51:34.823164Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-6949514d | 6949514d | Succeeded | 2026-11-04T16:15:50.0529Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-6c1b0f87 | 6c1b0f87 | Succeeded | 2026-11-04T19:02:25.624711Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-6dfad742 | 6dfad742 | Succeeded | 2026-11-04T21:06:18.547228Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-727a57dd | 727a57dd | Dispatched | — | 3.405 | Protected task: Dispatched |
| /work/worktrees/task-7289f7c7 | 7289f7c7 | Succeeded | 2026-11-04T11:59:43.415145Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-73837e64 | 73837e64 | Succeeded | 2026-11-04T11:16:18.767305Z | 0.239 | Terminal completedAt within 24 hours |
| /work/worktrees/task-73c027ae | 73c027ae | Succeeded | 2026-11-05T02:57:37.808916Z | 0.914 | Terminal completedAt within 24 hours |
| /work/worktrees/task-73c94e0b | 73c94e0b | Succeeded | 2026-11-05T01:32:55.659035Z | 2.086 | Terminal completedAt within 24 hours |
| /work/worktrees/task-75c9410c | 75c9410c | Succeeded | 2026-11-04T12:26:56.578316Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-7ab0d65a | 7ab0d65a | Succeeded | 2026-11-04T13:10:09.127658Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-7c7132b3 | 7c7132b3 | Failed | 2026-11-04T23:34:09.674506Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-7eb005f3 | 7eb005f3 | Succeeded | 2026-11-04T09:32:38.360261Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-7f675cf6 | 7f675cf6 | Succeeded | 2026-11-04T15:52:26.993266Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-801ad6c3 | 801ad6c3 | Succeeded | 2026-11-04T17:52:17.687551Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-821a7c5b | 821a7c5b | Succeeded | 2026-11-04T14:59:46.799557Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-8347d02a | 8347d02a | Succeeded | 2026-11-04T10:23:06.711342Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-84b44899 | 84b44899 | Succeeded | 2026-11-04T10:04:44.991758Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-8592d536 | 8592d536 | Canceled | 2026-11-05T04:39:10.842255Z | 2.085 | Terminal completedAt within 24 hours |
| /work/worktrees/task-86a941a3 | 86a941a3 | Succeeded | 2026-11-05T02:47:29.598881Z | 0.934 | Terminal completedAt within 24 hours |
| /work/worktrees/task-8719af3e | 8719af3e | Succeeded | 2026-11-04T14:04:10.714218Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-87232051 | 87232051 | Succeeded | 2026-11-04T12:53:09.097888Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-880edccc | 880edccc | Canceled | 2026-11-05T04:40:34.107694Z | 0.688 | Terminal completedAt within 24 hours |
| /work/worktrees/task-8ac2299c | 8ac2299c | Succeeded | 2026-11-04T18:11:12.052107Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-8acb8a4d | 8acb8a4d | Succeeded | 2026-11-04T09:06:39.686569Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-8b609b8a | 8b609b8a | Succeeded | 2026-11-04T18:50:07.272716Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-931a28cf | 931a28cf | Succeeded | 2026-11-04T16:33:52.033829Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-93a16649 | 93a16649 | Succeeded | 2026-11-05T02:16:14.76859Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-9602645b | 9602645b | Succeeded | 2026-11-04T19:45:37.890766Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-96f2fc86 | 96f2fc86 | Succeeded | 2026-11-04T07:45:57.191355Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-977203e7 | 977203e7 | Succeeded | 2026-11-04T18:49:10.1203Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-9843b768 | 9843b768 | Succeeded | 2026-11-04T06:09:39.750903Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-99e23919 | 99e23919 | Succeeded | 2026-11-04T21:37:48.271442Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-9e4c7963 | 9e4c7963 | Dispatched | — | 0.236 | Own worktree |
| /work/worktrees/task-9f577891 | 9f577891 | Succeeded | 2026-11-04T20:14:27.33127Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-a22bdfed | a22bdfed | Succeeded | 2026-11-04T14:47:13.885441Z | 0.237 | Terminal completedAt within 24 hours |
| /work/worktrees/task-a41890aa | a41890aa | Succeeded | 2026-11-04T15:39:03.40971Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-a42d888f | a42d888f | Succeeded | 2026-11-04T13:19:18.360418Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-a695169d | a695169d | Succeeded | 2026-11-04T13:33:37.546911Z | 0.245 | Terminal completedAt within 24 hours |
| /work/worktrees/task-a6c5c347 | a6c5c347 | Succeeded | 2026-11-04T19:06:17.129553Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-aa58fb53 | aa58fb53 | Failed | 2026-11-04T09:28:49.529577Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-adbd81fe | adbd81fe | Succeeded | 2026-11-04T23:50:30.579337Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-ae6457ac | ae6457ac | Succeeded | 2026-11-04T21:20:48.292317Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-b02d7898 | b02d7898 | Succeeded | 2026-11-04T18:07:43.604938Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-b054e10c | b054e10c | Succeeded | 2026-11-04T07:38:56.619419Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-b1cdbed3 | b1cdbed3 | Succeeded | 2026-11-04T11:43:52.223767Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-b3bd8985 | b3bd8985 | Succeeded | 2026-11-04T13:17:34.670558Z | 0.240 | Terminal completedAt within 24 hours |
| /work/worktrees/task-b3c01691 | b3c01691 | Succeeded | 2026-11-04T17:05:12.560172Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-b578f67a | b578f67a | Succeeded | 2026-11-04T13:00:44.048546Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-b5b7324a | b5b7324a | Failed | 2026-11-05T03:59:42.878953Z | 2.086 | Terminal completedAt within 24 hours |
| /work/worktrees/task-ba8ec885 | ba8ec885 | Succeeded | 2026-11-04T19:42:04.518606Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-bbd446f6 | bbd446f6 | Succeeded | 2026-11-05T03:12:06.680218Z | 2.086 | Terminal completedAt within 24 hours |
| /work/worktrees/task-bdaa665c | bdaa665c | Succeeded | 2026-11-04T13:24:43.401896Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-c0cea8f5 | c0cea8f5 | Succeeded | 2026-11-04T09:20:15.262874Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-c3ba1792 | c3ba1792 | Dispatched | — | 3.137 | Protected task: Dispatched |
| /work/worktrees/task-c7f02607 | c7f02607 | Succeeded | 2026-11-04T08:55:15.972104Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-c8274f67 | c8274f67 | Succeeded | 2026-11-04T15:00:55.74738Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-cb6b4d80 | cb6b4d80 | Succeeded | 2026-11-04T11:16:44.174212Z | 0.240 | Terminal completedAt within 24 hours |
| /work/worktrees/task-ccfe679f | ccfe679f | Succeeded | 2026-11-04T14:11:19.180888Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-d19080aa | d19080aa | Succeeded | 2026-11-04T20:54:06.857026Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-d2564da4 | d2564da4 | Succeeded | 2026-11-04T20:04:07.018092Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-d5e8910e | d5e8910e | Succeeded | 2026-11-05T03:37:06.54814Z | 2.086 | Terminal completedAt within 24 hours |
| /work/worktrees/task-d707a486 | d707a486 | Succeeded | 2026-11-04T14:30:59.776625Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-da743107 | da743107 | Succeeded | 2026-11-04T08:23:36.307713Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-dc12c438 | dc12c438 | Succeeded | 2026-11-04T18:54:45.449846Z | 2.096 | Terminal completedAt within 24 hours |
| /work/worktrees/task-dd35b058 | dd35b058 | Canceled | 2026-11-04T12:46:22.046901Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e056f0ff | e056f0ff | Succeeded | 2026-11-04T18:53:12.577042Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e23c0f83 | e23c0f83 | Succeeded | 2026-11-04T12:40:58.546641Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e24018e3 | e24018e3 | Succeeded | 2026-11-04T22:49:35.747679Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e36a0edf | e36a0edf | Succeeded | 2026-11-05T00:48:20.268318Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e379bcd9 | e379bcd9 | Succeeded | 2026-11-04T12:36:22.31197Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e38f32c6 | e38f32c6 | Succeeded | 2026-11-04T05:43:42.355412Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e438ae14 | e438ae14 | Succeeded | 2026-11-05T05:00:42.9621Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e45ab432 | e45ab432 | Succeeded | 2026-11-04T07:57:22.352127Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e5258e8c | e5258e8c | Succeeded | 2026-11-04T11:38:08.732771Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e778bd07 | e778bd07 | Succeeded | 2026-11-04T23:46:32.644013Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e80ac465 | e80ac465 | Succeeded | 2026-11-05T03:37:39.788251Z | 0.237 | Terminal completedAt within 24 hours |
| /work/worktrees/task-e8242d52 | e8242d52 | Succeeded | 2026-11-04T19:36:53.360203Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-eab3a4fc | eab3a4fc | Succeeded | 2026-11-04T21:14:21.210278Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f059674d | f059674d | Dispatched | — | 2.345 | Protected task: Dispatched |
| /work/worktrees/task-f2976027 | f2976027 | Succeeded | 2026-11-04T19:16:13.93719Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f2ed8c32 | f2ed8c32 | Succeeded | 2026-11-04T12:22:09.683893Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f2f59326 | f2f59326 | Succeeded | 2026-11-04T11:41:02.805624Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f303b3ec | f303b3ec | Succeeded | 2026-11-04T12:50:30.545122Z | 0.247 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f35f1237 | f35f1237 | Succeeded | 2026-11-04T22:02:04.604999Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f5e96e75 | f5e96e75 | Succeeded | 2026-11-04T17:45:02.382151Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f5eb193f | f5eb193f | Succeeded | 2026-11-04T14:37:05.763846Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f674c79a | f674c79a | Canceled | 2026-11-05T04:39:11.920461Z | 1.164 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f67b2762 | f67b2762 | Succeeded | 2026-11-04T06:09:25.428233Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f6e3cc3c | f6e3cc3c | Succeeded | 2026-11-04T13:49:16.686384Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f7703a17 | f7703a17 | Succeeded | 2026-11-04T10:10:21.408144Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-f893d1a3 | f893d1a3 | Succeeded | 2026-11-04T08:43:23.600051Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-ff7ab869 | ff7ab869 | Succeeded | 2026-11-04T20:36:06.672096Z | 0.236 | Terminal completedAt within 24 hours |
| /work/worktrees/task-ff8f8f3f | ff8f8f3f | Succeeded | 2026-11-04T11:38:29.695111Z | 0.236 | Terminal completedAt within 24 hours |

## Evidence and validation

Generated evidence remains outside Git at `/work/evidence/task-9e4c7963`. The only committed file is this investigation report.

- `start.json`, `validated-paths.txt`, `excluded-paths.json`: fixed cutoffs and allowed path set.
- `project.json`, `fleet.json`, `standing.json`: server ownership and protection observations.
- `inventory.json`, `inventory.jsonl`, `delete-list.json`: per-path task ownership, completion time, owning repository, HEAD, allocated bytes and decision.
- `deletions.jsonl`, `batch-01.json` through `batch-18.json`: fresh task observations, containment refs, deletion outcomes and per-batch disk measurements.
- `df-before.txt`, `df-after.txt`, `prune.json`, `worktrees-after.txt`, `reconciliation.json`: final filesystem and registration reconciliation.
- `inventory.mjs`, `delete-batch.mjs`, `write-report.mjs`: local operation/report scripts.

Validation: 344 unique deletion receipts; 344 successful Git removals; zero fallback deletions; zero salvage needs; zero selected paths or registrations remaining; all 161 retained paths present. Builds/tests intentionally omitted as required. Report formatting checked with `git diff --check` before commit.

--- next stage ---
next: done
handoff: Cleanup complete; 344 old terminal-task worktrees removed, 161 worktrees retained under the completion-age and protection rules.
artifact: docs/investigations/2026-11-05-server2-worktree-cleanup-2.md
