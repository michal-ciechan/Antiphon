# Four Queued tasks held 33-71 minutes while later tasks dispatched (2026-11-20)

Debug task `e89352a8`, base master `452dd5cfe2b0b1758c41e5dad2ef8bf141988514`. This was a read-only diagnosis:
HTTP GETs against the production server (`/api/version` `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`, `land-v2`)
and code reading. Nothing was restarted, cancelled or written to the board. The desktop server
Serilog file (`desktop-server` in [logs.md](../logs.md)) is a desktop file source with no HTTP
route, so it was not read from server2. The persisted task events below are the authoritative
dispatcher trace (`TraceHeldAsync` writes one `Held` event each time the hold detail changes).

## Finding

**Nothing was stuck.** Capacity, seats, the host load sample, routing pins, workspace slots and the
build-slot gate were not involved. All four tasks were held by the **same-card sibling-land hold**:
a card-bound Worktree task is not cut while another task on the same card, in the same repository,
has a land request in `Queued`, `Held` or `Running`. All four tasks are bound to long-lived busy
cards, CARD-1514 (three tasks) and CARD-1744 (one). Those cards had back-to-back lands in the
single, serial land queue, so the cards always had a pending land. The later tasks that dispatched
quickly (`3a5b76f5`, `460fa431` CARD-1836; `1ddb3ae6` CARD-1981; `652fbbe7` CARD-1820) are on cards
with no pending land.

**The defect: the wait cannot change the outcome.** Each held task carries an **explicit
full-SHA StartRef** (`worktreeBaseSource=Explicit`, `worktreeBaseRequestedRef` = a 40-hex master
SHA from the brief's "Base master <sha>"). The cut is that immutable commit whether or not the
sibling has landed. The tasks that were released were cut from the same pinned SHA, which still
lacks the sibling's commits, after waiting 47-71 minutes. The base resolver already treats any
explicit StartRef as final (`AgentTaskWorktreeBaseResolver.cs:95-98` returns `Target()`). Only the
dispatch-time sibling inspection holds it.

Kind of wait: **waiting for an event, not stuck forever, and not an operator-only setting**. The
hold lifts on the first dispatcher tick after the card has no pending same-repository land.

## The gate (file:line, base `452dd5cfe`)

- `server/Application/Services/AgentTaskDispatcher.cs:922-971`: for a Worktree task with
  `CardId`, no `WorktreePath`, and no repair or SourceLanding origin, the dispatcher peeks the
  inspection. A `Hold` result writes `HoldKind.SiblingLanding` and `continue`s (`:947-960`). The
  result is invalidated (`:951`), so the next tick re-enqueues the inspection. That writes
  `Held: same-card base inspection is pending.` (`:939-945`), then the hold again.
- `server/Application/Services/CardSiblingInspectionService.cs:172-195` (normal path): each pending
  same-repository sibling land holds (`return Result(Describe(...), "pending_land")`, `:194`). The
  only explicit-StartRef bypass (`:174-183`) requires the StartRef to **equal or descend from** the
  sibling's reviewed source SHA. A StartRef pinned to an older master never does.
- `CardSiblingInspectionService.cs:143-147` with `:256-260` (overflow path): when the card has
  more than `MaxInventoryRows` (`GitSettings.WorktreeBaseMaxInventoryRows`, default 256) same-card Worktree rows, inventory throws `candidate_limit`
  before the pending loop. The catch holds on `unresolvedPending.FirstOrDefault()` (ordered by task
  id). It does **no** containment check and **no** reviewed-source exemption. CARD-1514 is in this
  path: `b7e96a42` dispatched with `Same-card warning inspection incomplete: candidate_limit; 257
  raw, 0 candidates, 0 inspected, 241 omitted`. The held sibling named in the events also changed
  in task-id order (`188ceb65` → `ab9057c0` → `00e2b8c7`), which matches `orderby t.Id`.
- `server/Application/Services/CardSiblingInventory.cs:28-33`: "pending" means
  `IsPending && State in (Queued, Held, Running)`.
- Pipeline read: `GET /api/agent-tasks/pipeline?projectId=...` shows `queueReason:
  "siblingLandInFlight"` and `heldBy: cd9d14b8` for `dd6c075a` at 08:50:39Z.

## Evidence

### The four held tasks

| Task | Role / kind / runner | Card | Explicit StartRef | Created | Held until | Wait | Held behind (in order) |
|---|---|---|---|---|---|---|---|
| `73d0252f` | Plan / ClaudeCode High / server2 | CARD-1514 | `8a89aa0e…` | 07:32:54 | 08:43:46 → dispatched 08:46:41 | 71 min | `188ceb65`, `ab9057c0`, `00e2b8c7` |
| `2c673f12` | Code / Grok High / server2 | CARD-1514 | `8a89aa0e…` | 07:35:38 | 08:44:45 → dispatched 08:46:47 | 68 min | `188ceb65`, `ab9057c0`, `00e2b8c7` |
| `b7e96a42` | Custom / ClaudeCode High / desktop | CARD-1514 | `c6565bc9…` | 07:56:22 | 08:43:46 → dispatched 08:45:22 | 47 min | `00e2b8c7` |
| `dd6c075a` | Custom / Grok High / server2 | CARD-1744 | `76986030…` | 08:17:22 | **still Queued at 08:52:41** | 35+ min | `cd9d14b8` |

`2c673f12` recorded 803 `Held` events and `73d0252f` recorded 836. The hold detail flips between
the inspection-pending sentence and the sibling-land sentence on every re-inspection, which defeats
the `lastHeld` dedupe in `TraceHeldAsync` (`AgentTaskDispatcher.cs:1525`). That is roughly 700 rows
an hour for each held task. It is a secondary finding.

The `HeldAged` ledgers rule out the other gates. For example, `2c673f12` at 07:50:47 shows
`class=siblinginspection … leaseWait=0s; prepWait=0s; runnerWait=0s; capWait=0s`. After release,
each task moved through the normal path in 1-3 minutes: a 1-event repository lease wait behind
`cd9d14b8`'s land at 08:43:51, then remote prep, then `workspace_slot_decision … reason=all`, then
`prepared workspace is ready`, then `Dispatched`.

### The land convoy that kept the cards "pending"

The land worker is one global serial reader, and every land yields to queued dispatches first:

| Land task | Card | Requested | Admitted | Landed |
|---|---|---|---|---|
| `188ceb65` (Docs) | CARD-1514 | 07:22:01 | 07:23:05 | 07:38:03 |
| `b11c8906` (Code) | CARD-1514 | 07:33:57 | 07:49:36 | 08:00:20 |
| `ab9057c0` (Investigate) | CARD-1514 | 07:35:52 | 08:00:45 | 08:11:13 |
| `5782c600` (Code) | CARD-2005 | 07:40:19 | 08:11:22 | 08:22:41 |
| `00e2b8c7` (Code) | CARD-1514 | 07:54:44 | 08:22:48 | 08:43:40 |
| `cd9d14b8` (Custom) | CARD-1744 | 07:58:56 | 08:43:48 | running at 08:52 (`LandAged Warning: Land Running` 08:49:02) |

From 07:22 to 08:43, CARD-1514 always had at least one pending land. Each land runs for 10-21 minutes
and the queue was 3-4 deep, so admission waited up to 45 minutes (`cd9d14b8`). The hold therefore
lasted the whole convoy.

### Things that were checked and are not the cause

- **Stale server2 load sample (`sample_stale`, `inputsAt 07:06:09`)**: this is a shadow projection
  only. `HostLoadShadowCache` says "Nullable projection only", and [ops-http.md](../ops-http.md)
  says it "does not change task admission". `AgentTaskDispatcher.cs` has no reference to it. The
  cache is filled only by an unscoped `GET /api/hosts/{id}/load`
  (`HostLoadHistoryService.cs:50`), so `inputsAt` is the last time anyone read that route. A read at
  08:50:54 refreshed it and still returned `sample_stale`: `physicalHostKey: null`, `current: null`,
  `coverage 0` over the last hour. The server has no usable `HostLoadHistory:Bindings` sample for
  server2 at this SHA. That is a telemetry gap and does not block dispatch. `/api/hosts/stats` shows
  server2 `live`.
- **Seats and host budget**: server2 had declared capacity 10 with 6 occupied at 08:50, and
  `hosts[server2].remaining=4`. The ledgers show `runnerWait=0s` and `capWait=0s`.
- **Stage caps**: Code was 4 of 5 with `parallelRemaining 1`, and the project was at 8 of 12. The
  ledgers show `capWait=0`. The local `maxConcurrentTasks 2` applies to host `local` only.
- **Routing pins**: `{"pins":[]}`. Model availability had no hold.
- **Workspace slots (ws-NN)**: slot decisions came within 15 seconds of preparation (`reason=all`).
  No slot was exhausted or leaked.
- **Build-slot gate**: this gates builds inside sessions, not dispatch.
- **Keep-six**: an advisory setting. The server does not dispatch.

## Minimal fix (code slice brief)

**Rule:** an explicit StartRef that is an immutable commit id (ASCII hex, 40 or 64 characters,
case-insensitive) never waits for a pending same-card sibling land. The dispatcher cuts at once
and keeps today's post-land output for an uncontained sibling: the ordinary sibling warning on
the normal path, or the incomplete-inspection warning on the overflow path. A symbolic StartRef
(`master`, a branch or tag name) keeps the hold, because that ref can advance when the land
completes.

**Files:**
- `server/Application/Services/CardSiblingInspectionService.cs`
  - Add `bool immutableStart = task.WorktreeBaseRequestedRef is { } r && IsCommitId(r)`.
  - Pending loop (`:185-194`): when `immutableStart`, add `Describe(row, sha, state)` to `warnings`,
    remove it from `unresolvedPending`, and `continue` instead of
    `return Result(..., "pending_land")`. When the tip is unknown (`:185-186`), add the warning
    with a null sha instead of holding.
  - Overflow catch (`:256-260`): when `immutableStart`, pass `hold: null`, which dispatches with
    the `candidate_limit` incomplete-inspection warning.
  - Leave the reviewed-source exemption (`:174-183`) unchanged.
- Docs: in `docs/orchestration-loop.md`, the "Continuing a sibling's work" paragraph (~`:300`)
  and the sibling-hold sentence (~`:540-543`) should say that a pending sibling land holds dispatch
  only for an automatic or symbolic base. A full-SHA StartRef dispatches with the sibling warning.

**Red witnesses (commit them before the fix):**
1. `AgentTaskDispatchBaseGuardTests.SiblingWarnings.cs`:
   `C<card>_ImmutableStartRefDoesNotWaitForPendingSiblingLand`. Copy
   `C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold` (`:124`), but set
   `w.Task.WorktreeBaseRequestedRef` to the **master** tip SHA, which does not contain the
   sibling's reviewed source. The request is `Queued`, `IsPending=true`. Expect
   `await w.RunAsync([(sibling, 0)])` (Dispatched plus one sibling warning). At base it stays Queued,
   so `dispatched.Status.ShouldBe(Dispatched)` is red.
2. `DispatchSiblingInspectionTests.cs`: a sibling of `C1349_PendingLandSurvivesLimits` (`:339-404`)
   in the `"pending beyond raw cap"` world, with `WorktreeBaseRequestedRef` = master SHA. Expect
   `Dispatched` with a `worktree-base-inspection-*` warning intent. At base it is held (overflow
   catch).
3. Guard, already green and must stay green: `C1349_PendingLandSurvivesLimits("unrelated StartRef")`
   uses the symbolic `master` and must remain held.

**Checkpoint rows (Antiphon.Tests, serial, one build):**

| Row | Filter | Expectation |
|---|---|---|
| CP-1 red | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/C<card>_*` plus `/*/Antiphon.Tests.Application/DispatchSiblingInspectionTests/C<card>_*` | 2 red at the witness commit |
| CP-1 green | same | 2 green after the fix |
| CP-2 | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | green (C540/C753/C788 sibling family) |
| CP-3 | `/*/Antiphon.Tests.Application/DispatchSiblingInspectionTests/*` and `/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/*` | green (C1349 pending precedence, the "unrelated StartRef" hold) |
| CP-4 | `/*/Antiphon.Tests.Application/AgentTaskWorktreeBaseSyncDebtTests/*` | green (explicit StartRef identity unchanged) |

Mutation positive control: drop the `immutableStart` branch in the pending loop, which turns
witness 1 red. Drop it in the overflow catch, which turns witness 2 red.

**Follow-ups, separate cards and not this slice:**
- (a) The overflow catch holds on a pending sibling without a containment check, even for automatic
  bases. CARD-1514's more than 256 rows put every new CARD-1514 task in this path.
- (b) The hold-detail flip-flop writes about 700 `Held` events an hour for each held task. The
  inspection-pending sentence should not be re-traced while a sibling-land hold is being
  re-observed.
- (c) server2 load-history binding: `physicalHostKey null`, so there is no sample. The telemetry
  only.

## What the orchestrator can do now (no restart)

- **Recommended: wait.** `dd6c075a` releases on the first dispatcher tick after `cd9d14b8`'s land
  settles. That land has been running since 08:43:48. Recent lands took 10-21 minutes, so expect
  about 08:55-09:05Z, plus 1-3 minutes of remote prep. There is no risk. The other three already
  dispatched at 08:45-08:47.
- **`-Refine` does not help.** It changes the brief, not the card or base, so the hold is
  unchanged.
- **Cancel and re-dispatch with `-StartRef 7cf660265e80a8574c95d30a62772ceee3b6849d`**
  (`cd9d14b8`'s reviewed source, or any SHA that descends from it). This uses the existing
  reviewed-source exemption on the **normal** path only. If CARD-1744 has more than 256 Worktree
  rows, the overflow catch still holds it. It also changes the base to include round 1's branch.
  The risks are a lost queue position, a new task id, and the cancelled row's history.
- **Cancel and re-dispatch on a fresh card** (or with no `-Card`). This skips sibling inspection
  completely and dispatches on the next tick. The risks are lost card linkage and attribution, and
  an un-landed CARD-1744 sibling is not warned about. This matches the standing CARD-1349 advice:
  bind new work to a fresh card rather than a long-lived card with many kept branches, which
  applies to CARD-1514 (257 rows).
- Until the fix lands, do not bind new tasks to CARD-1514 or CARD-1744 while their lands are queued,
  or expect a delay of one land convoy.
