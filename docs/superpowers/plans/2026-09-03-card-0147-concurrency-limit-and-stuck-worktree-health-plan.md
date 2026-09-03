# CARD-0147 — refuse new dispatches past a small in-flight cap, and surface stuck `feat/card-task-*` worktrees

**Plan pass, 2026-09-03 (task 253847fc). Design only; no production code is changed by this plan.**

**Card:** CARD-0147 — Warn/refuse when too many delegate tasks are in flight, with an override flag; plus a related finding about tasks silently vanishing from tracking (stuck worktree init, no terminal state, no notification).

**Verified against:** worktree `feat/card-task-253847fc` @ `7126b93f`. CARD-0147 and CARD-0146 read from the live board today. CARD-0146 is **Backlog, unplanned, unbuilt** — this plan does not wait on it.

---

## Verdict up front

**Refuse at create, not at the dispatcher tick.** `POST /api/agent-tasks` (and therefore `delegate.ps1`) returns **409 `concurrency_limit`** when a new non-specialist task would push either (1) the fleet open-count or (2) that role's open-count past a small default. The override is `-IgnoreConcurrencyLimit` / `ignoreConcurrencyLimit: true`, one-shot, recorded as a `Warning` event. The existing dispatcher cap `Delegation:MaxConcurrentTasks` (6) stays as the process safety net and is **not** lowered.

**Group = `AgentTaskRole`.** CARD-0146's Investigate / Design / Build / Verify / Merge vocabulary has not landed. Per-group uses the roles already on the row (Plan, Code, Debug, …). Revisit the grouping key when CARD-0146 ships; do not invent a parallel stage enum here.

**Health check is detection only.** A periodic sweep cross-references git's `feat/card-task-*` registrations and branches against `AgentTasks` and writes `WorktreeHealthFinding` rows. `GET /api/attention` projects them as `AttentionKind.OrphanWorktree`. Nothing is pruned, healed, cancelled, or re-dispatched.

---

## Why the existing caps do not do this

| Knob | Where | What it does today | Why the 2026-08-22/23 miss still happened |
|---|---|---|---|
| `DelegationSettings.MaxConcurrentTasks = 6` | `AgentTaskDispatcher.TickAsync` `:353-358` | **Skip** a queued non-specialist when Dispatched+Working ≥ 6. Task stays Queued. Caller already heard `queued task <id>`. | Five creates in one turn all returned 200. The tick then started them (5 < 6). Silent fan-out. |
| `RolePolicyEntry.RecommendedInFlight = 1` (CARD-0304, Review) | `GET /api/agent-tasks/pipeline` | Advisory. `atOrAboveRecommendation` is a read. Comment and tests pin: **never refuses create, never changes the dispatcher cap.** | Did not exist on 2026-08-22. Even now an orchestrator that does not read the pipeline is unconstrained. |
| Shared-writer lease (CARD-0063) | dispatcher | Holds a Shared writer whose scope intersects a running one. Worktree tasks are **outside** the lease (the parallelism worktrees exist to give). | The miss was worktree-isolated on purpose. The lease correctly let them through. |
| `MaxTasksPerRoot` / `MaxCostUsdPerRoot` / `MaxDepth` | `AgentTaskService.CreateAsync` `:289-306` | Per-tree runaway guards. 409 `conflict`. | Independent trees (five cards) do not share a root. |
| CARD-0096 batch `P` | planned, unbuilt | Card-level "start N, keep P in flight" for the standing orchestrator. | Different layer (cards, not tasks). Orthogonal; this card does not wait on it. |

The operator correction (`feedback_prefer_sequential_dispatch`) is: **heavy parallelism is the exception**. The tool must push back at the moment the orchestrator types the next `delegate.ps1`, with a count-vs-limit sentence, not queue a fifth task and hope someone reads the pipeline.

---

## CARD-0146 status (do not block)

CARD-0146 is Backlog, rank 10, unplanned. Its stage list (Investigate → Plan/design → Test/verification design → Build → Verify → Merge/cleanup) is a future prompt-and-handoff vocabulary, not a column on `AgentTasks`.

Interim grouping is `AgentTaskRole`. Suggested mapping **when** CARD-0146 lands, not a schema change in this card:

| CARD-0146 stage | Today's role | Notes |
|---|---|---|
| Investigate | `Debug` (sometimes `Custom`) | Evidence-gathering. Cheap-model-appropriate. |
| Plan / design fix | `Plan` | Frontier. |
| Test / verification design | `Plan` or `Coverage` | No dedicated role today. |
| Build | `Code` | "Execute" is a UI alias for Code (CARD-0304). |
| Verify | `Test` / `Review` | |
| Merge / cleanup | `Merge` / `Commit` / `Deploy` | Land is now `-Land`, not a role. |

A later CARD-0146 plan that adds a real stage field should switch this card's per-group predicate to that field and leave `RecommendedInFlight` keyed on it. Until then, **pick `-Role` honestly** — three unlabelled `Custom` investigates only hit the absolute cap.

---

## Ground truth (checked, not guessed)

Line numbers at `7126b93f`.

- Create is `AgentTaskService.CreateAsync` (`:131`). Persist is `_db.AgentTasks.Add` + `SaveChanges` at `:719-756`. Existing create 409s (quota, model-disabled, provider sign-in, routing pin, depth, cost, per-root task count) all fire **before** that insert.
- `CreateAgentTaskRequest` already has `IgnoreSubscriptionQuota`, `IgnoreModelDisabled`, `IgnoreRoutingPin`, `AllowUnauthenticatedProvider` (`AgentTaskDtos.cs:56-131`). `delegate.ps1` mirrors them as switches and only sends the JSON property when the switch is on (`:410-413`).
- `delegate.ps1` `Invoke-Antiphon` (`:224-241`) prints the server's problem-details body on failure and `exit 1`. A 409 `detail` sentence is what the orchestrator reads. No extra CLI formatting is required.
- Dispatcher in-flight predicate: non-specialist `Dispatched || Working` (`AgentTaskDispatcher.cs:280-282`, `:353-358`). Specialists (`Check`/`Distill`/`Diagnose`, `AgentTaskRoles.IsSpecialist`) spawn no process and already bypass the cap (`AgentTaskEnums.cs:41-51, 296-314`).
- Pipeline `inFlight` is the same Dispatched/Working cut (`AgentTaskPipelineStatusService.cs:140-141`). Queued is a separate collection. Blocked is a separate collection and **does not** consume `RecommendedInFlight`.
- Worktree branch for a delegated task: `DelegationWorktreeService.CreateForTaskAsync` passes identifier `"task-{shortId}"` (`:267`); `WorktreeManager.BuildBranchName` prefixes `feat/card-` (`:17, :334`) → **`feat/card-task-<8-hex>`**. Directory is `card-task-<id>` under `Git:WorktreeBasePath`.
- `IWorktreeManager.ListAsync` already filters to Antiphon `feat/card-*` worktrees under the base path. Porcelain parsing knows `locked` / `prunable` (`WorktreePorcelainEntry` `:1027-1032`) but **drops them** on the way to `WorktreeInfo` (`WorktreeInfo.cs` has no lock fields).
- CARD-0220 (**Done**): `CreateAsync` heals registered-but-missing for **that** task, `worktree add` has a 180 s budget, a timeout is `TimeoutException`, a failed add rolls back, a pre-dispatch failure goes through `FailAndNotifyAsync`. The original `7a3b3eaa` / `7af4be46` "directory is gone, task never terminal" shape is healed or failed-and-notified on the **next tick of the same task**. It does **not** survey every other `feat/card-task-*` registration, and it does not tell the orchestrator about a sibling's zombie worktree.
- CARD-0231 `FailureUnacknowledged` covers a task that **did** reach `Failed` with `DispatchedAt == null`. The vanish case never Failed.
- CARD-0035 attention kinds are append-only. Last shipped: `ScheduleMisfired = 26` (`AttentionDtos.cs:218`). Client `ATTENTION_VISUALS` is a total `Record<AttentionKind, …>` pinned by `attentionVisuals.test.ts` `ALL_KINDS`.
- `AgentIncident.AgentId` is already nullable (CARD-0247), but incidents are append-only and have no `TaskId`. A condition that appears and clears wants a finding row, not another incident storm.
- No `pg_advisory_xact_lock` helper exists in application services. Concurrent `CreateAsync` calls on the shared test Postgres are the race that would recreate the five-at-once miss if the gate only read-then-inserts.

---

## Decisions

### D1. Create-time 409, not a dispatcher skip

The miss is five `queued task` lines in one turn. A tick-level skip leaves those tasks Queued; the orchestrator believes they started; the health check then has five more zombies to explain.

Gate in `AgentTaskService.CreateAsync`, after the existing 409s (quota / sign-in / model-disabled / pin / depth / cost) and **before** `_db.AgentTasks.Add`. Same shape as `subscription_quota_low`: problem-details `code`, a sentence in `detail`, an extension payload, an override flag that records a `Warning` and proceeds.

Do **not** fail, cancel, or hold tasks that are already open when this ships. The card is explicit: only new creates.

### D2. What counts as open

Non-specialist tasks in **`Queued | Dispatched | Working`**.

| Status | Counts? | Why |
|---|---|---|
| Queued | **yes** | Same-turn fan-out is all Queued until the next tick. Excluding it makes the gate a no-op for the miss. |
| Dispatched, Working | **yes** | Occupying a worker. |
| Blocked | **no** | Waiting on a human. Starting something else while a question is outstanding is allowed (and is how CARD-0304 treats Blocked vs `RecommendedInFlight`). |
| Succeeded / Failed / Canceled | **no** | Settled. |
| `Check` / `Distill` / `Diagnose` | **no** | Already outside `MaxConcurrentTasks`. A system at the create cap must still interpret checks. |

Live `-OnAgent` follow-up (`FollowUpOnTask` resolved to a still-running agent): **exempt**. It queues on an existing process; it is sequential continuation, not fan-out. A follow-up whose prior agent has retired (fresh delegate, new process) **counts**.

Retry / requeue / refine / answer / land: not creates. Ungated.

UI `DelegateModal` and any other `POST /api/agent-tasks` caller hit the same gate. The override is a body flag; the modal does not need a checkbox in v1 (it already surfaces `detail` via `getApiErrorMessage`). Script is the orchestrator's path.

### D3. Two numbers, conservative defaults

```
DelegationSettings.MaxOpenTasks = 3           // absolute create cap; new
RolePolicy[role].RecommendedInFlight = 1      // already shipped; now also the per-role create cap when non-null
MaxConcurrentTasks = 6                        // unchanged; dispatcher process cap
```

- Absolute: `openCount >= MaxOpenTasks` → 409, axis `absolute`.
- Per-role: `RecommendedInFlightFor(role)` is an int **and** `openCountFor(role) >= that` → 409, axis `role`. `Custom` (no RolePolicy entry, recommendation null) has **no** per-role gate; the absolute cap is the backstop. Specialists are not in RolePolicy for this purpose.
- Either axis failing is enough. Check absolute first, then role, so a mixed pile-up names the fleet number.
- `MaxOpenTasks` must be a positive integer (startup validator, same shape as `RecommendedInFlight`). 0 or negative fails start. Null is not offered — there is always an absolute cap.
- Do **not** lower `MaxConcurrentTasks`. Override fan-out still has a process ceiling of 6; raising that is a config edit, not a flag. Override does **not** bypass the dispatcher cap.
- CARD-0304's pipeline remains advisory **as a read model**. This card amends the "never refuses create" sentence in `RolePolicyEntry.RecommendedInFlight`'s doc-comment and in `AgentTaskServiceIntegrationTests` that pin independence from the **dispatcher** cap (those dispatcher assertions stay). Add tests that create **does** refuse when the recommendation is met.

Worked examples with the defaults (3 absolute, 1 per named role):

| Already open | New create | Result |
|---|---|---|
| 1 Plan Working | Code | 200 (different role, absolute 2/3) |
| 1 Plan + 1 Code + 1 Debug | anything non-specialist | 409 absolute 3/3 |
| 1 Debug Working | Debug | 409 role Debug 1/1 |
| 2 Custom Queued | Custom | 200 (Custom has no per-role cap; absolute 3/3 after this insert) |
| 3 Custom Queued | Custom | 409 absolute 3/3 |
| 1 Plan, override | Plan | 200 + Warning event |
| Check at 3/3 open | Check | 200 (specialist) |
| 6 Dispatched via override | 7th with override | 200 Queued; dispatcher skips until a slot frees (`MaxConcurrentTasks`) |

That is sequential-by-default: two named-role tasks of the same kind cannot run together without the flag, and a mixed pile cannot exceed three.

### D4. Override flag name: `-IgnoreConcurrencyLimit`

Not `-ForceParallel`. The existing family is `-IgnoreSubscriptionQuota`, `-IgnoreModelDisabled`, `-IgnoreRoutingPin`. The flag does not force the dispatcher to run more processes; it ignores the **create** gate for this one request.

JSON: `ignoreConcurrencyLimit: true` on `CreateAgentTaskRequest`, default false, omitted by `delegate.ps1` unless the switch is set (same as the other Ignore* flags — `DelegateScriptKindTests` pins the omitted-means-absent contract).

On override: still insert; append `AgentTaskEventType.Warning` with a sentence that names both counts and both limits (so a later reader can see it was deliberate). Do not also skip `MaxConcurrentTasks`.

### D5. 409 shape

New `ConcurrencyLimitException : HttpException` (409, code `concurrency_limit`), same family as `SubscriptionQuotaLowException`.

`detail` (the orchestrator-visible sentence), exactly this information, in this order:

1. Which axis (`3 tasks already in flight (limit 3)` or `1 Debug task already in flight (limit 1)`).
2. The occupants, as `shortId Role Status` (and `(stuck: …)` when a live `WorktreeHealthFinding` names that task — D8).
3. The coda: `Prefer working through these sequentially before starting more. Re-send with ignoreConcurrencyLimit=true if the user asked for parallel work this turn.`

Extension payload (camelCase):

```json
{
  "concurrency": {
    "axis": "absolute",
    "role": null,
    "count": 3,
    "limit": 3,
    "open": [
      { "taskId": "…", "shortId": "7a3b3eaa", "role": "Debug", "status": "Working", "title": "…", "stuck": "locked initializing; directory gone" }
    ],
    "override": "ignoreConcurrencyLimit"
  }
}
```

For the role axis, `role` is the enum name and `open` is that role's occupants only. Absolute axis lists all open non-specialists (cap the list at 12 in the extension; the sentence can say `and N more`).

### D6. Serialize check + insert

Five parallel POSTs that each see `openCount = 0` would all insert. Wrap the count-and-insert in one EF transaction and take a Postgres advisory xact lock first:

```
SELECT pg_advisory_xact_lock(hashtext('antiphon.delegation.max-open-tasks'));
```

Released on commit/rollback. Tests share one Postgres: the lock serialises concurrent creates across tests for the duration of the transaction (milliseconds). Do not hold it across HTTP I/O.

A small helper `DelegationOpenGate` (new) owns: lock, count, throw or return the occupant list for the Warning. `CreateAsync` calls it. Keep it out of the dispatcher.

### D7. Health check: surface, do not fix

New hosted service `WorktreeHealthHostedService` (Application service + Infrastructure host, same split as the dispatcher). Interval: `Git:WorktreeHealthIntervalSeconds` default **60**. Runs at startup after a short delay and then on the interval.

Scan method on `IWorktreeManager` with a **default empty implementation** (the CARD-0328 fake-friendly pattern):

```
Task<IReadOnlyList<DelegateWorktreeScanEntry>> ScanDelegateWorktreesAsync(
    string repoPath, CancellationToken ct)
```

Real manager:

1. `git worktree list --porcelain` (already parsed, including `locked` / `lockReason`).
2. Keep entries whose branch is `feat/card-task-*` (identifier starts with `task-` after the `feat/card-` prefix). Card-spawn `feat/card-{identifier}` is out of scope.
3. `git branch --list feat/card-task-*` for dangling branches with no worktree.
4. Return path, branch, shortId, locked, lockReason, directoryExists, `.git` file exists, registered.

Repos to scan: distinct `RepoPath` from (a) `.antiphon/worktrees/*.json` metadata (the janitor's set), (b) `AgentTask.RepoPath` where `WorktreePath` or `WorktreeBranch` is set, (c) `Delegation:AllowedRoots` that are git repos. Deduped. A missing repo is a skip, not a failure of the sweep.

Match `shortId` → `AgentTask.Id` via `DelegationReportFormatter.Short` (first 8 hex of the guid). Unparseable names are ignored.

### D8. What is an orphan (narrow on purpose)

CARD-0328 measured 16 registered worktrees of which 10 were settled leftovers. Surfacing every kept-for-review branch would drown the attention feed. Surface **only** shapes that make the limiter lie or that match the vanish:

| Shape | Severity | Occupies a create slot? |
|---|---|---|
| Registered + **locked** (esp. `initializing`) + directory missing or no `.git` file | Error | If the matched task is Queued/Dispatched/Working, **yes** — and the 409 names it `stuck:` |
| Registered `feat/card-task-*` with **no** AgentTask row | Error | No (undercount — git has a workspace, the limiter does not know) |
| Matched task is Queued/Dispatched/Working **and** (locked or directory missing) | Error | Yes |
| `feat/card-task-*` **branch**, no worktree, **no** task row | Warning | No |

**Not this card** (janitor / CARD-0328 residue / LeftForHuman):

- Succeeded / Failed / Canceled with a clean leftover worktree or merged branch.
- Blocked task's intact worktree (the Merge/answer path needs it).
- `feat/card-{cardIdentifier}` card-spawn worktrees.
- Remote `origin/feat/card-task-*` (163 on 2026-09-02; a different cleanup).

The sweep **upserts** `WorktreeHealthFinding` by `(RepoPath, Branch, Shape)`: set `LastSeenAt` while present, set `ClearedAt` when the shape is gone. Uncleared rows are the attention predicate. No auto-heal, no `worktree remove`, no `branch -D`, no `FailAsync`. CARD-0220 continues to heal **at create of that same task**; this sweep does not call it.

### D9. Attention kind

`AttentionKind.OrphanWorktree = 27` (append, never renumber). Warning for dangling-branch-no-task; Error for the locked/missing/no-task-row shapes. Detection only.

`AttentionItemDto`: `TaskId` set when resolved; `SessionId`/`AgentId` null when there is no task. Headline names the branch and the shape in the row's own words (`feat/card-task-7a3b3eaa locked initializing; directory C:\Antiphon\worktrees\card-task-7a3b3eaa is gone; task 7a3b3eaa still Working`). Evidence is the porcelain lock reason plus `delegate.ps1 -Status <id>` when a task exists.

Actions: `OpenDrawer` if there is a task; `Cancel` if the task is still open; nothing that deletes git state. No `Retry` (retrying the same id re-enters CARD-0220's heal, which is correct, but the attention row must not pretend that is automatic).

Do **not** add this kind to `LIVENESS_KINDS` (home-rail task badges). It is a git/tracking condition; DeadSession / NeverStarted / FailureUnacknowledged already badge the task-shaped cousins. The attention panel is the surface.

Client: `client/src/api/attention.ts` union, `ATTENTION_VISUALS` entry (label `Stuck worktree`, colour `danger` for Error / `warning` for Warning — the Record is per-kind, so pick `danger` and let severity grouping colour the row), `ALL_KINDS` in `attentionVisuals.test.ts`. Icon: `TbGitBranch` or reuse `TbPlugConnectedX`.

### D10. Interaction with CARD-0220 / CARD-0231 / CARD-0328

- CARD-0220 remains the **self-heal at create-for-this-task**. The sweep must not call `TryHealStaleRegistrationAsync`.
- CARD-0231 remains the **failed-before-dispatch reminder**. If CARD-0220's fail-and-notify path runs, this finding clears when the task leaves Queued/Dispatched/Working (or when git is cleaned by hand).
- CARD-0328 residue / janitor TTL remain the **settled leftover** path. This card does not steal that population.

### D11. Docs the create 409 must join

- `docs/antiphon-api.md` POST `/api/agent-tasks` 409 list (alongside `subscription_quota_low`).
- `docs/orchestration-loop.md` §1 (the cycle): sequential-by-default; 409 `concurrency_limit` means wait or `-IgnoreConcurrencyLimit` if the user asked.
- `server/Bundles/orchestrator.md` (the copy the standing orchestrator actually carries): one short paragraph, same rule.
- `scripts/delegate.ps1` switch comment, same shape as `-IgnoreSubscriptionQuota`.
- `RolePolicyEntry.RecommendedInFlight` doc-comment: still the pipeline's advisory number; **create now refuses when it is met**, unless `ignoreConcurrencyLimit`.

AGENTS.md is the index; do not duplicate the rule there.

---

## Implementation slices

### S1 — Create-time gate

**Files:** `DelegationSettings.cs`, `CreateAgentTaskRequest`, new `ConcurrencyLimitException.cs`, new `DelegationOpenGate.cs` (or equivalent), `AgentTaskService.CreateAsync`, `Program.cs` (validator), `docs/antiphon-api.md`.

1. `MaxOpenTasks` default 3, positive-int validator.
2. `DelegationOpenGate.EnsureCanCreateAsync(role, ignore, ct)`: advisory lock, count non-specialist Queued/Dispatched/Working (and per-role), join uncleared findings for `stuck:` labels, throw or return occupant snapshot for the Warning.
3. Exempt `AgentTaskRoles.IsSpecialist` and live `FollowUpOnTask` before calling the gate.
4. Override: skip throw, add `Warning` event after insert (the event needs the task id).
5. Tests in a new `AgentTaskConcurrencyLimitTests` (shared-Postgres: scope assertions to the tasks the test created; do not assert global counts — Gotcha #24). Pin:
   - 3 open + 4th create → 409 `concurrency_limit`, detail contains `3` and `limit 3`.
   - 1 Debug open + 2nd Debug → 409 axis role, detail contains `Debug`.
   - 1 Plan + 1 Code + 1 Debug + 4th mixed → 409 absolute.
   - Custom has no per-role gate (2 Custom + 1 Plan is 200; 3 Custom + 4th is 409 absolute).
   - Specialist at 3/3 still creates.
   - Live follow-up at 3/3 still creates; retired-agent follow-up does not.
   - `ignoreConcurrencyLimit: true` at 3/3 creates and writes a Warning event naming the counts.
   - Override still does not change `MaxConcurrentTasks` dispatcher behaviour (reuse the CARD-0304 independence assertion style).
   - Two concurrent creates when `MaxOpenTasks = 1`: exactly one 200, one 409 (the lock).
   - Already-open tasks are not modified.

**Verify:** `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0147/ -- --treenode-filter "/*/*/AgentTaskConcurrencyLimitTests/*"` then delete `bin-c0147` dirs.

### S2 — Script flag + CARD-0304 comment/test amendment

**Files:** `scripts/delegate.ps1`, `DelegateScriptKindTests.cs`, `AgentTaskServiceIntegrationTests.cs` (the RecommendedInFlight "never refuses" wording — replace with "does not change MaxConcurrentTasks"; add a pointer to S1), `server/Bundles/orchestrator.md`, `docs/orchestration-loop.md`.

1. `[switch]$IgnoreConcurrencyLimit` next to the other Ignore* switches; send `ignoreConcurrencyLimit = $true` only when set.
2. Pin send-when-set and omit-when-unset (copy `IgnoreSubscriptionQuota_sends_…` / `an_omitted_IgnoreSubscriptionQuota_sends_no_…`).
3. Orchestrator bundle: sequential-by-default, 409 means wait or the flag if the user asked, never silently reroute.

**Verify:** `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0147s2/ -- --treenode-filter "/*/*/DelegateScriptKindTests/*"` and the RecommendedInFlight facts in `AgentTaskServiceIntegrationTests` / `AgentTaskPipelineStatusTests` (pipeline advisory numbers unchanged).

### S3 — Worktree health sweep + attention row

**Files:** `WorktreeHealthFinding` entity + migration, `GitSettings.WorktreeHealthIntervalSeconds`, `IWorktreeManager.ScanDelegateWorktreesAsync` + `WorktreeManager` impl + porcelain already parsed, `WorktreeHealthService` (Application) + `WorktreeHealthHostedService`, `AttentionKind.OrphanWorktree`, `AttentionService.GetAsync`, client attention union + visuals + `ALL_KINDS`, `docs/antiphon-api.md` attention list if one exists.

1. Finding table: `Id, RepoPath, Branch, Path, TaskId?, Shape, Detail, FirstSeenAt, LastSeenAt, ClearedAt?`. Unique `(RepoPath, Branch, Shape)` filtered where `ClearedAt IS NULL`.
2. Sweep is idempotent and scoped to the rows it wrote (Gotcha #24). A git failure on one repo logs Warning and continues.
3. Attention projection reads uncleared findings; first-match order: after `FailureUnacknowledged` / `DeadSession` for a task that has both (those name the cause more usefully). A finding **without** a task always appears (nothing else can claim it).
4. S1's 409 occupant `stuck:` reads the same uncleared rows (no git on the create path).
5. Tests: `WorktreeManagerTests` scan parsing (locked + missing dir, dangling branch); `WorktreeHealthServiceTests` classify / upsert / clear; `AttentionServiceTests` one Error row for locked-missing with an open task, one Warning for a dangling branch with no task, gone when `ClearedAt` set. Client `attentionVisuals.test.ts` still totals.

**Verify:** the new test classes + `AttentionServiceTests` (class filter, not the Application namespace) + `pwsh -File scripts/test-client.ps1 attentionVisuals.test`. Rebuild `client/dist` only if an E2E is added; none is required in v1.

### S4 — Docs only if S1–S3 did not already touch them

`docs/antiphon-api.md` 409 paragraph for `concurrency_limit`. `docs/orchestration-loop.md` sequential default. Confirm `RecommendedInFlight` comment matches D3. No AGENTS.md change unless the owner doc's one-line index needs a new row (it does not; orchestration-loop already owns this area).

---

## Out of scope

- CARD-0146 stage vocabulary, prompt templates, next-stage reporting.
- CARD-0096 card-batch N/P (card-level; different engine).
- Lowering `MaxConcurrentTasks` from 6.
- Per-board / per-project caps (unbound tasks have neither; CARD-0304 already rejected this).
- Silently healing or pruning from the health sweep.
- Remote `origin/feat/card-task-*` cleanup.
- UI checkbox for the override (409 `detail` is enough; add later if operators create from the modal).
- Counting Blocked as open.
- Making `RecommendedInFlight` a dispatcher skip (would silently queue, which is the thing this card refuses).

---

## Files (expected)

| Slice | Touch |
|---|---|
| S1 | `DelegationSettings.cs`, `AgentTaskDtos.cs`, `ConcurrencyLimitException.cs` (new), `DelegationOpenGate.cs` (new), `AgentTaskService.cs`, `Program.cs`, `AgentTaskConcurrencyLimitTests.cs` (new), `docs/antiphon-api.md` |
| S2 | `scripts/delegate.ps1`, `DelegateScriptKindTests.cs`, `AgentTaskServiceIntegrationTests.cs`, `server/Bundles/orchestrator.md`, `docs/orchestration-loop.md` |
| S3 | `WorktreeHealthFinding.cs` (new), migration, `GitSettings.cs`, `IWorktreeManager.cs`, `WorktreeManager.cs`, `WorktreeHealthService.cs` (new), hosted service (new), `AttentionDtos.cs`, `AttentionService.cs`, `client/src/api/attention.ts`, `attentionVisuals.ts`, `attentionVisuals.test.ts` |
| S4 | leftover doc sentences |

~10–14 h, `-Worktree`. Server first (S1 is the operator-visible refuse; S3 is the vanish). Land S1 before starting S3 so a Code agent that runs long does not ship the sweep without the gate.

---

## Acceptance

- A fourth non-specialist `delegate.ps1` while three are Queued/Dispatched/Working exits 1 with a sentence that contains the actual count and the limit, and does not insert a row.
- The same command with `-IgnoreConcurrencyLimit` queues, and the task has a Warning event naming the counts.
- A second `Debug` while one Debug is open refuses even if the absolute count is 1.
- Specialists and live follow-ups are unaffected.
- A locked `feat/card-task-*` registration whose directory is gone appears on `GET /api/attention` as `OrphanWorktree` and is named `stuck:` on a subsequent 409, without anyone having to poll `-Status`.
- The sweep does not delete that worktree.
- CARD-0304 pipeline numbers and `MaxConcurrentTasks = 6` dispatcher behaviour stay as they are.

[antiphon-plan: CARD-0147]
