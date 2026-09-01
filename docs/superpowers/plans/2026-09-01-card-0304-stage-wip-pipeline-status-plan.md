# CARD-0304 — Advisory stage WIP limits and a pipeline-status read model

**Date:** 2026-09-01  
**Card:** CARD-0304 — Configurable recommended per-stage WIP limits + a pipeline status endpoint  
**Outcome:** add global, role-based *recommendations* and one read-only aggregation endpoint. Do not add a task status, dispatch gate, board/project setting, workflow tracker behaviour, or client UI in this card.

## Decision

1. Put the recommendation on `DelegationSettings.RolePolicy[role]`, as nullable `RolePolicyEntry.RecommendedInFlight`. The role already owns model tier, timeout, escalation, and optional agent-kind policy (`server/Application/Settings/DelegationSettings.cs:290-315`, `:597-690`), while `MaxConcurrentTasks` is a global real cap (`:10-20`). This is a global deployment setting, not a per-project or per-board field.

   A task may have neither `ProjectId` nor `CardId` (`server/Domain/Entities/AgentTask.cs:30-39`, `:108-123`), so a board/project rule would leave normal manual and cross-repo delegation undefined. Capacity and checkout contention are also fleet-wide. Default the named, user-dispatchable roles (Plan, Code, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge) to `1`; `Custom` has no limit unless configuration adds a `RolePolicy` entry and `Check` remains hidden machinery. `null` means “no recommendation”.

   A recommendation never refuses task creation or changes `AgentTaskDispatcher`'s actual `MaxConcurrentTasks` cap or `SerialiseSharedWriters` lease. The endpoint exposes the configured limit plus whether its in-flight count is at/over it; CARD-0301 and future automation decide how to display or act on it.

2. Use the existing durable task roles as the v1 stage taxonomy: Plan, Code, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge, and Custom (`server/Domain/Enums/AgentTaskEnums.cs:20-43`). Omit `Check`, whose contract is an internal interpretation task that already bypasses queue/cap concerns (`:33-42`). “Execute” is a UI alias for `Code`, not a new enum or status.

3. Add `GET /api/agent-tasks/pipeline` as a global, read-only projection. It returns every user-visible stage, including empty stages, in enum order. Map it in the existing `/api/agent-tasks` group before `/{id}`, alongside `/summary` and `/areas` (`server/Api/Endpoints/AgentTaskEndpoints.cs:15-62`). Do not add a board/project filter in v1: the source task query is fleet-wide and every returned row carries card identity, so a board consumer can filter without making unbound tasks disappear.

4. Model “ready for the next stage” as a query, not stored state. A row appears in the **Code** stage’s `ready` collection when it is the latest created Plan task for one bound card (ties break by task id), is `Succeeded` with `CompletedAt`, and has a verified `DeliverablePath` under `docs/superpowers/plans/` ending `.md`. Settlement already extracts a report-named `docs/*.md` path and verifies it on disk or the worktree branch (`server/Application/Services/AgentTaskReplyService.cs:1771-1813`), so this reuses `DeliverablePath`/`DeliverableRef` rather than probing Git.

   The candidate card must not be archived, terminal, or in `NeedsDecision`; and it must have no open Code task and no Code task created after that Plan completed. A canceled Code task which was never dispatched does not consume readiness; a queued, dispatched, working, blocked, or already-attempted later Code task does. This is deliberately the single proven Plan → Code bridge. Do not infer a generic Code → Review/Deploy graph. Existing card lifecycle remains independent and continues to honor human moves (`docs/agent-card-lifecycle.md:36-65`).

## Why a new endpoint is warranted

`GET /api/agent-tasks` is useful input, not the pipeline contract. It already exposes status and role, hides Checks by default, accepts a comma-separated `status` filter, and preserves active rows outside a recent-history window (`AgentTaskEndpoints.cs:31-40`; `AgentTaskService.cs:782-816`). CARD-0093 correctly found the old delegations board did not use that filter; the current client now uses a rolling seven-day `since` query and supports status (`client/src/api/agentTasks.ts:229-294`).

It still has no role/card/board/project filter; it omits events from summaries; a historical `Held` event is not proof of a current lease; its rolling window can omit the successful Plan; and Plan → Code readiness is a cross-task/card decision. The dispatcher writes one `Held` event on entry and no release event (`AgentTaskDispatcher.cs:278-405`), so a client cannot derive current queue reason correctly. The new route is consequently a thin, no-write aggregation over existing `AgentTasks`, `AgentTaskEvents`, `TranscriptEntries`, and `Cards`, not pipeline machinery or a second source of truth.

## Response contract

Add DTOs in `server/Application/Dtos/AgentTaskPipelineDtos.cs` using normal camel-case serialization:

```json
{
  "asOf": "2026-09-01T12:00:00Z",
  "recommendationsAreAdvisory": true,
  "stages": [
    {
      "role": "Plan",
      "recommendedInFlight": 1,
      "inFlightCount": 1,
      "atOrAboveRecommendation": true,
      "inFlight": [{ "taskId": "…", "shortId": "…", "title": "…", "status": "Working", "card": { "id": "…", "identifier": "CARD-0304", "title": "…" }, "agentName": "…", "dispatchedAt": "…", "lastActivityAt": "…" }],
      "queued": [{ "taskId": "…", "shortId": "…", "title": "…", "card": null, "createdAt": "…", "queueReason": "sharedCheckoutLease", "heldBy": [{ "taskId": "…", "shortId": "…", "title": "…" }] }],
      "blocked": [],
      "ready": []
    },
    {
      "role": "Code",
      "recommendedInFlight": 1,
      "inFlightCount": 0,
      "atOrAboveRecommendation": false,
      "inFlight": [],
      "queued": [],
      "blocked": [],
      "ready": [{ "card": { "id": "…", "identifier": "CARD-0304", "title": "…" }, "sourcePlanTaskId": "…", "sourcePlanShortId": "…", "readySince": "…", "deliverablePath": "docs/superpowers/plans/2026-09-01-card-0304-stage-wip-pipeline-status-plan.md", "deliverableRef": null }]
    }
  ]
}
```

`inFlight` is exactly `Dispatched` or `Working`, matching the current hard-cap predicate (`AgentTaskDispatcher.cs:232-245`, `:297-303`). `Blocked` is returned separately so decision-bound work is not lost, but it does not consume the recommendation. `lastActivityAt` is the maximum persisted transcript timestamp (falling back to transcript creation time) after `DispatchedAt`, or `DispatchedAt` when no transcript exists. It is an observation, not a second liveness policy; `TaskProgressPolicy` intentionally has richer novelty/working semantics for stalls (`server/Application/Services/TaskProgressPolicy.cs:44-166`).

Every Queued task appears in `queued`. Its `queueReason` is `sharedCheckoutLease` only when the present lease calculation finds serialising holders; otherwise it is `awaitingDispatch` (the tick, global cap, or pinned agent can be responsible, and the projection must not overclaim). `heldBy` is populated only in the first case. Sort task collections by `CreatedAt`, then task id, and ready rows by `readySince`, then card identifier.

## Implementation slices

### S1 — Extend role policy with advisory WIP recommendations

**Files:** `server/Application/Settings/DelegationSettings.cs`, `server/Program.cs`, `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs`.

1. Add nullable `RecommendedInFlight` to `RolePolicyEntry`, documenting global advisory capacity and null as unbounded.
2. Set the decision’s default `1` values in the in-code default `RolePolicy` dictionary. Do not add a partial `RolePolicy` object to `server/appsettings.json`: binding a partial dictionary could replace the carefully tuned default role entries.
3. Add one resolver/validator that accepts only a positive configured value or null, fails startup clearly for zero/negative values, and is registered through the current `Configure<DelegationSettings>` seam (`server/Program.cs:129`).
4. Pin defaults, override, Custom/null, invalid values, and that a recommendation does not alter `MaxConcurrentTasks` dispatch behavior.

### S2 — Extract the current live lease calculation

**Files:** `server/Application/Services/AgentTaskDispatcher.cs`, new `server/Application/Services/SharedWriterLeaseProjection.cs`, `tests/Antiphon.Tests/Application/DelegationScopeHoldTests.cs`.

1. Extract only the pure “given active writers and one queued task, which serialising holders exist?” part of `ScopeHolder`/`ScopeOverlap` (`AgentTaskDispatcher.cs:243-405`, `:3220-3279`) into an application helper. It takes the current `AreaMapLoader`, `SerialiseSharedWriters`, and minimal task coordinates; it returns holder ids/display facts and never writes events or status.
2. Switch the dispatcher to the helper without changing its behavior: repo-key comparison, ReadOnly/Check exclusions, worktree warning rather than hold, allow-weight areas, and unscoped Shared-writer serialization all remain exact.
3. The pipeline service consumes the same helper. It must not infer a live hold from `AgentTaskEventType.Held`.
4. Keep the current hold suite green and add released-historical-hold, unscoped Shared, ReadOnly, worktree, and same-repository-subdirectory coverage. The existing isolated integration harness is at `DelegationScopeHoldTests.cs:18-160`.

### S3 — Build the pipeline read model and readiness query

**Files:** new `server/Application/Services/AgentTaskPipelineStatusService.cs`, new `server/Application/Dtos/AgentTaskPipelineDtos.cs`, `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated migration, new `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`.

1. Create a scoped, read-only service using `AppDbContext`, `IOptions<DelegationSettings>`, `AreaMapLoader`, `TimeProvider`, and S2’s lease helper; register it beside `AgentTaskService` (`server/Program.cs:271-274`). Use `AsNoTracking`, bounded input-population queries, and in-memory DTO assembly. It must not update rows, generate events, contact Git, or run dispatch.
2. Aggregate transcript activity by active task session after its dispatch time. Preserve the dispatch fallback and do not copy stall classification.
3. Classify every queued row against the active snapshot with S2, producing current lease holders or `awaitingDispatch`.
4. Implement the Plan → Code predicate above: latest Plan per `CardId`, card-state join, verified plan-path check, and same-card later/open Code suppression. Return no report body.
5. Add `IX_AgentTasks_CardId_Role_CreatedAt` next to the existing card/status indexes (`AppDbContext.cs:1451-1457`) and generate its migration with `dotnet ef migrations add`; never hand-author migrations. It is a query-support index, not a persisted ready status/table.
6. Cover empty stages; configured/absent limits; Working/Dispatched activity; ordinary versus currently lease-held queue; Blocked separate; Check exclusion; ready inclusion; later/open Code, non-success latest Plan, missing/wrong deliverable, terminal/NeedsDecision suppression; and deterministic sort. Scope assertions to created rows because the test assembly shares PostgreSQL (`docs/testing-and-build.md:34-42`).

### S4 — Expose and contract-test the route

**Files:** `server/Api/Endpoints/AgentTaskEndpoints.cs`, `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`; add `tests/Antiphon.E2E/ContractSnapshotTests.cs` only if CARD-0301 consumes the route in the same landing.

1. Map `GET /api/agent-tasks/pipeline` before `/{id}` and delegate solely to S3. Existing list, summary, areas, and detail behavior stay unchanged; this is a public GET with no mutation.
2. Add an HTTP test for literal-route ordering, camel-case fields, empty stage rows, and the advisory flag. Use the established Program-host guard; a test host must never reach the production runner (`docs/testing-and-build.md:46-53`).
3. Do not alter `client/src`, SignalR invalidation, CARD-0093’s list UX, or a consumer in this card. CARD-0301 owns the TanStack Query hook and phone layout; if it lands concurrently, it owns the real-backend fixture/snapshot.

## Test matrix and verification

| Layer | Coverage |
|---|---|
| Settings | Defaults, override, Custom/null, invalid values, and unchanged hard process cap. |
| Lease / dispatcher | Current holder truth, released historical Held, unscoped Shared, ReadOnly, worktree, repo-root semantics. |
| Pipeline service | Stage buckets, activity fallback, saturation, queue reasons, Check exclusion, every Plan → Code readiness inclusion/exclusion. |
| HTTP | Literal route, camel-case contract, public read behavior, no regression to current agent-task routes. |
| Database | Composite query index plus CLI-generated migration applies. |

Run new and adjacent TUnit classes with `dotnet run --project tests/Antiphon.Tests`, never `dotnet test`, and use a forward-slash alternate `OutputPath` only when local daemons hold `bin/`. Run `Antiphon.Tests` separately from `Antiphon.Agents.Pty.Tests`; do not start a second runner or allow a test host to use the production runner (`docs/testing-and-build.md:10-17`, `:46-60`). No client build/browser E2E is required because this card deliberately adds no client consumer.

## Explicitly out of scope

- Hard WIP admission control, create-time 409s, queue reordering, automatic throttling, or changes to `MaxConcurrentTasks`/`SerialiseSharedWriters`.
- Per-board/per-project WIP persistence or board workflow/YAML configuration.
- A generalized workflow graph, `Execute` enum, durable `Ready` task status, or automatic Plan → Code dispatch.
- CARD-0301’s phone view and CARD-0093’s delegations-history UX.
- Liveness/stall policy changes: this projection reports activity but leaves `TaskProgressPolicy` and attention behavior intact.
