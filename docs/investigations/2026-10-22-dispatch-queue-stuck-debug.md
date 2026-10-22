# Seven queued tasks on server2: capacity and admission latency

Debug task `679d6ab5`, 2026-10-22. Read-only production investigation: no code change, restart, task cancellation, session release or retry. The requested report and Backlog filing are the only mutations.

**Finding:** the queue was slow, not permanently wedged. Five of the seven tasks launched without intervention by 20:07:02 UTC. The two remaining tasks encountered genuine capacity exhaustion: four Blocked Code owners retained live seats alongside six active tasks. Earlier free-seat observations were real, but did not establish that the dispatcher had completed its next inspection, leased checkout creation and admission pass. Inspection-state churn and long serial maintenance sweeps add avoidable latency.

All times below are **UTC**; server log timestamps have `+01:00` and are converted here. The brief mixes a BST label with UTC event times; the persisted timestamps govern this reconstruction.

## Identity and evidence

- Loaded server `/api/version`: `0714a9a89e4bff65aae7e0b4b94098694aeb931c`, capability `land-v2`.
- Investigation checkout: branch `feat/card-task-679d6ab5`, initial HEAD `f26bc076b2049a0e7202b60941d554854cda368a`. A Git comparison against the loaded server commit confirmed that the dispatch, inspection, capacity, wake and start-ref-fetch paths discussed here are unchanged; unrelated dispatcher differences are outside this diagnosis.
- Antiphon project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`, board `8988ca03-7414-47ad-b0b6-51556c701703`.
- Read the scoped task list, each of the seven task details/events, scoped pipeline, attention, runner slots, occupancy samples, relevant land requests, and four Blocked owners. Details use the `summary` member for task status, not the envelope itself.
- Server evidence: `C:\src\Antiphon\server\logs\antiphon-20261022_002.log`, retrieved using the owner in `docs/logs.md`; `scripts/logs.ps1 -Source desktop-server` succeeded. Hangfire dashboard read returned HTTP 200. Dispatch is a hosted service, not a Hangfire recurring job.
- Raw local evidence is ignored under `C:\Antiphon\worktrees\card-task-679d6ab5\.antiphon\queue-debug`: task/detail JSON, pipeline, attention, occupancy samples, selected server log lines, card-search results, land requests and the two release candidates' normalized transcripts. This report retains the essential facts independently of those generated payloads.

## The seven tasks

Statuses below were observed during 20:06-20:11 UTC. Later admissions are shown rather than treating an old Held event as current status.

| Task | Work | Created | Observed outcome | What its hold actually waited for |
|---|---|---|---|---|
| `5145b46f-31d7-4ded-ac61-386b917da732` | Review CARD-1502 S1 | 19:03:23 | Dispatched 20:01:54 | Capacity, then desktop checkout and mirror preparation. Worktree log 20:00:10; mirror saved 20:01:10. |
| `1757a065-8fcc-424a-8ac6-0b51020e6995` | Code CARD-1483 S2 | 19:11:15 | Dispatched 20:05:55 | Its own sibling inspection, mostly capacity, then repository lease and checkout/preparation. |
| `e28759db-5cb9-40c2-bdb0-12124bbc0997` | Review CARD-0167 S5 repair | 19:11:18 | Dispatched 20:07:02 | Its own sibling inspection, mostly capacity, then repository lease. Mirror saved 20:06:41. Despite its title, this follows the ordinary explicit-base Worktree inspection path. |
| `40ce7992-74d2-48a9-bbd0-010d872a6fcc` | Code CARD-1404 | 19:39:33 | Queued | Last Held at 20:03:46: server2 capacity 10/10. No desktop worktree yet. |
| `a773cf3a-6370-406f-a19d-25e8dd539389` | Review CARD-1506 | 19:40:36 | Queued | Last Held at 20:03:46: server2 capacity 10/10. No desktop worktree yet. |
| `aee2f1a6-c3ac-409e-922a-cc4f8803e4f3` | Review CARD-1486 S3 | 19:42:06 | Dispatched 20:01:55 | Capacity, then checkout/preparation. Mirror saved 20:01:46. |
| `0c8e45c6-b0b1-4013-8920-b7f46cc6fbe6` | Review CARD-0266 | 19:57:18 | Dispatched 20:04:25 | Capacity, lease held by land `c1f2ee9f`, then checkout/preparation. |

The older pair's `heldSince=19:12:08` is the beginning of their **queue stint**, not one continuously executing inspection. For example, `1757a065` alternated inspection at 19:12:08, capacity at 19:12:37, inspection at 19:23:02, capacity at 19:25:07, and later lease/capacity/inspection holds. Its 19:27:44 HeldAged event already attributed 781 of 935 seconds to runner capacity. At the attention snapshot around 20:06, `e28759db` had `leaseWait=302s; prepWait=0s; runnerWait=2256s; otherWait=694s; class=runner`. CARD-1404 had runnerWait 1160s versus leaseWait 279s; CARD-1506 had runnerWait 1046s versus leaseWait 301s. These are event-derived ledgers, not continuous profiling.

## What “same-card base inspection is pending” means

`AgentTaskDispatcher.cs:848-902` queues an observation for the **queued task itself** before its first desktop worktree cut. `CardSiblingInspectionQueue` owns two background workers and a channel/in-flight bound of 32. Each worker opens a fresh service scope and invokes `CardSiblingInspectionService`; there is no delegate agent assigned to perform the inspection. One `WorktreeBaseInspection` deadline covers database discovery, Git gate admission and Git observations; the default is five seconds.

`CardSiblingInventory.ReadAsync(..., warningsOnly: true)` includes Succeeded and Blocked same-card worktree siblings. A Blocked sibling is inventory, not by itself an indefinite prerequisite. A genuine pending sibling land produces a different hold naming that task and branch. Missing knowledge of pending integration produces another distinct hold. These seven task histories do not identify a same-card pending-land owner as their sustained blocker.

The inspection does not run desktop settlement sync and does not acquire the repository mutation lease. The claim subsequently acquires that lease and revalidates intent, sibling metadata and frozen refs. An observation stale at claim is invalidated, immediately re-enqueued and reported as pending again.

The workers were completing, not stuck forever: server log lines 87278-87279 record both old tasks timing out at 19:12:13 with 13/11 Git commands; lines 97274-97275 show new attempts at 19:55:58 with 10/9 commands; lines 99064/99068 show further completions at 20:03:08/09. CARD-1483 had about 10-11 raw siblings and seven candidate tips; CARD-0167 had 20 raw siblings and 11 candidate tips. Later dispatch proves the condition cleared. A bounded warning inspection can finish incomplete yet still allow an explicit-base claim when pending-land knowledge is available; a timeout does not establish an infinite blocker.

There is a confirmed structural defect already tracked by **CARD-1457**: `TickCoreAsync(ready)` loads a limited ready subset, but line 640 reconciles the shared inspection store against only that subset. `SiblingInspectionResults.Reconcile` then evicts completed observations and cancels in-flight observations belonging to other queued tasks. The ready batch is at most eight, and the defect also applies to a smaller subset that omits another queued task. Fix: reconcile against all queued IDs, or reconcile only on a full tick. Preserve claim-time checks. Runtime logs do not expose individual cache evictions, so this investigation confirms the defect in deployed code and observed churn, but does not assign every pending episode to it.

Other relevant existing cards: **CARD-0924** for inspection-budget pressure, **CARD-1349** for bounded sibling inspection/mirror-ready admission, and **CARD-1431** for misleading last-reason reporting. No duplicate was filed for these.

## Why free seats did not mean immediate launch

The capacity counter is freshly queried when admission reaches `RemoteHoldForAsync` (`AgentTaskDispatcher.cs:1232-1303`):

`Created/Starting/Running/Stopping desktop sessions + other prepared-but-unlaunched queued tasks + in-flight remote preparations`.

It does not count Failed/Stopped sessions. It counts a Blocked task's session while that session remains live. Runner `/slots` describes physical occupying sessions and excludes Exited sessions; it does not include future mirror reservations. The pipeline host total includes those reservations. The count and last Held text are separate observations.

The occupancy audit demonstrates the seats really freed and were consumed again:

| Sample time | Total in flight | Sessions | Prepared launches | In-flight mirrors |
|---|---:|---:|---:|---:|
| 19:57:57 | 10 | 10 | 0 | 0 |
| 19:58:58 | 8 | 8 | 0 | 0 |
| 19:59:57 | 7 | 7 | 0 | 0 |
| 20:00:57 | 8 | 7 | 0 | 1 |
| 20:01:57 | 9 | 9 | 0 | 0 |
| 20:03:57 | 10 | 9 | 1 | 0 |
| 20:05:57 | 9 | 9 | 0 | 0 |
| 20:06:57 | 10 | 9 | 1 | 0 |
| 20:07:58 | 10 | 10 | 0 | 0 |

The physical slots read at 20:10:52 independently confirmed 10/10, no occupying orphan. A final read at **20:16:48** still showed 10/10, CARD-1404 and CARD-1506 Queued, and both proposed release candidates Blocked. The earlier Exited/KilledByRequest session was explicitly nonoccupying. This does not support a permanently cached occupancy value or continued charging of that killed session.

The hosted dispatcher polls, so it is **not event-only**. Its nominal five seconds is a delay between completed ticks, not a five-second upper bound on re-evaluation. Full ticks await maintenance sweeps before ordinary admission. Each sweep has a 60-second cooperative budget and 30-second abandonment grace on its own scope. At 19:52:19 compaction continuation overran; delivery watchdog was abandoned at 19:53:49; the tick completed degraded at 19:54:39. Long sweeps therefore add minutes before a capacity-only task is reconsidered. The fast path between sweeps is driven by repository lease waiters; seat release does not signal it, and capacity-held tasks lose their lease-waiter membership. Worktree construction and lease contention add further time. The first post-free checkout was logged at 20:00:10 and its launch at 20:01:54, so re-evaluation demonstrably resumed.

**New Backlog CARD-1519**, GUID `73abdcbe-e363-476e-a617-943baed5fd79`, records the missing bounded capacity-admission wake path. Smallest fix to design: a coalesced, serialized admission-only pass serviced independently of long maintenance work, or bounded admission before/among sweeps. Preserve fresh capacity checks, single claims, lease fences, owned contexts and shutdown. Do not lower timeouts or run overlapping ticks on one DbContext. Required witness: a freed seat admits an eligible capacity-held task within a documented bound while a maintenance sweep is deliberately slow, without duplicate launch. This is a latency finding, not a claim that all seven waits were caused solely by sweeps.

## Repository lease and start-ref-fetch

The actual task holds name successive owners, not one abandoned fetch:

- `996ed989` land acquired at 19:41:47; a verification-window fence was reported at 19:42:57; its request completed and Landed was recorded 19:48:03.
- `8461f217` land acquired at 19:49:48 and Landed was recorded 19:57:35.
- `c1f2ee9f` land acquired at 20:01:32 and Landed was recorded 20:03:02.
- `43ac5232` land was itself held by **dispatch `e28759db`** at 20:06:02, then Landed at 20:11:33. The dispatcher therefore obtained the lease between lands.

For the specific start-ref notice, `c1f2ee9f`'s durable events say:

> 19:57:36 Land yields ... a39ce738 (start-ref-fetch).
>
> 19:57:38 Repository mutation lease is occupied by task none (start-ref-fetch); status=unknown.

At 19:57:42.025 the server logged successful fetch of `87066db5667c2423ebf2a424377ab798a48b22b0`; task `0c8e45c6` was then created at 19:57:42.495 in the server log. `StartRefAvailability` fetches before creating a task row, so `task none` is expected. `a39ce738` is a preflight identity, not an agent to cancel. Its default 30-second network deadline covers lease polling, fetch and probe; the code disposes the lease and clears its waiter. This observed fetch finished within seconds. The land notification was confirmed at 20:01:18, well after the fetch had ended: a delivered Held notice is historical evidence, not a current owner read.

There was contention and verification fencing, but **no evidence of sustained starvation by repeated start-ref-fetch operations**. Multiple completed lands and intervening dispatches disprove a continuously held abandoned fetch in this interval. The read-only `recover-repository-children.ps1 -Repository C:\src\Antiphon` preview refused because `landing.lock` was actively open; it made no recovery. Later the observed land completed. No journal deletion, lock removal or restart is justified by the historical notice.

## Smallest operator action now

At 20:10:52 the four retained Blocked owners were:

| Task | Card | Live session | Settlement evidence |
|---|---|---|---|
| `f9e4bfe6` | CARD-0167 | `d4e3d499-1e96-4459-a622-c16f9bda96c7` | Complete report; remoteSync Pending / runner_sync_lease_busy; no syncDebt |
| `259765b1` | CARD-1502 | `861e5ba9-9dd5-4848-b4f3-e7d2bd3fb0d4` | Complete report; Pending / runner_sync_lease_busy; no syncDebt |
| `c2af4f70` | CARD-0266 | `3768cd86-9cd9-4a1f-b578-37556645e654` | Complete report; Unavailable / runner_sync_timeout; no syncDebt |
| `84f61b86` | CARD-1506 | `01fa7a93-328c-43cd-b71a-ccf209cdf1a7` | Complete report; Pending / runner_sync_lease_busy; no syncDebt |

This is the existing **CARD-1065** seat-retention problem, with sync handling also covered by CARD-1081/CARD-1133 and associated work. These owners are not orphans. Their Blocked state does not hold the queued inspection indefinitely, but their live processes consume seats. No Reply is warranted for the complete reports.

Two candidates were checked beyond task prose at approximately 20:12-20:14 UTC:

- `84f61b86`: normalized transcript sequence 12 is the complete marked report; sequence 13 is end_turn at 19:34:18, with no later entry. Runner mirror `git status --porcelain=v1` was empty; mirror HEAD and `git ls-remote origin refs/heads/feat/card-task-84f61b86` both equal `ab1ea6da5bb1ddbd0cf67c0f95d54bfb25fdce01`.
- `259765b1`: sequence 120 is the complete marked report; sequence 121 is end_turn at 19:00:53, with no later entry. Runner mirror status was empty; mirror HEAD and origin both equal `28a325193e8e74336b399583842b7a50b27e61d4`.

Mirror reads used the observed filesystem owner UID 1654. An initial root Git read refused dubious ownership; no safe.directory setting was changed.

The smallest operational action is the existing **explicit single-seat release**, one or both of these idle completed owners, retaining their task/branch/report for reviewed-source landing recovery. Re-read their task/slot state immediately before applying, because this is a dated observation. From the canonical checkout, the caller can run:

```powershell
pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId server2 -SessionId 01fa7a93-328c-43cd-b71a-ccf209cdf1a7 -Reason 'Completed CARD-1506 report; clean mirror and pushed ab1ea6da5bb1ddbd0cf67c0f95d54bfb25fdce01 verified; preserve Code landing owner'
pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId server2 -SessionId 861e5ba9-9dd5-4848-b4f3-e7d2bd3fb0d4 -Reason 'Completed CARD-1502 report; clean mirror and pushed 28a325193e8e74336b399583842b7a50b27e61d4 verified; preserve Code landing owner'
pwsh -NoProfile -File scripts/runner-slots.ps1 list -RunnerId server2
```

The script sends the existing operator-authenticated `POST /api/session-runners/server2/slots/{sessionId}/release` with a required reason and never prints the token. `RunnerSlotService.AuditAsync` records the session Stopped and the audit incident; it does not cancel the Code task or delete its branch. This avoids converting a still-needed Code landing owner to Canceled, which reviewed-source recovery refuses. It is not an instruction to release active Working tasks. Do not use release-orphans for these non-orphan owners. The read-only Debug did **not** execute these commands and does not claim their outcome. Recheck the two queued task details/pipeline after release; checkout/inspection/lease work can still delay admission.

## Follow-up and verification

Complete duplicate searches were made with `card.ps1 search ... -Board Antiphon -All` for inspection, sweep, capacity, start-ref-fetch, dispatch, slow sweep, capacity release, maintenance sweep and dispatch tick. Candidate cards were read in full. CARD-1457, CARD-0924, CARD-1431, CARD-1349, CARD-1065 and CARD-0633 already cover their stated mechanisms; CARD-1519 is the new distinct Backlog item. No duplicate cards or dispatches were created.

Prioritize the small CARD-1457 reconciliation fix; plan CARD-1519's bounded capacity wake separately. Retain the existing Blocked-seat/sync work and avoid inventing an automatic kill policy. Preserve correct pending-land, verification-window and claim checks.

No builds or tests were run: production behavior was unchanged. Verification consisted of live API/log chronology, deployed-code comparison, normalized transcript reads, clean mirror checks and exact origin-tip equality. No result claims a green product test gate, a completed repair or a successful operator release.

--- next stage ---
next: decide
handoff: Caller may release the two verified idle completed-owner seats with the exact commands above while preserving their Code landing identities. Schedule CARD-1457's narrow fix and Plan CARD-1519. Five original tasks self-recovered; do not restart, duplicate, or Reply to their complete sibling reports.
artifact: docs/investigations/2026-10-22-dispatch-queue-stuck-debug.md
