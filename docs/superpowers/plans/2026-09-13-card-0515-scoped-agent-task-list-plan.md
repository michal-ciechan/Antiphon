# CARD-0515: Scope the delegated-work list, and make scope visible

Date: 2026-09-13. Stage: Plan; verification design is a separate TestDesign stage.
Based on Investigate task `cc9000c7`
([report](../../investigations/2026-09-13-card-0515-cross-board-task-leakage.md)) and
checkout `c2c067d3af4bd73931257ea4bf42bf290d41ac5b`.

## Outcome and scope

`GET /api/agent-tasks` is a fleet-wide list whose rows carry no board or project
identity, and whose unknown query keys are silently discarded. A caller asking an
Antiphon-board question therefore receives gym-stat, markdown-package and slides rows,
believes it filtered, and cannot tell from any field that it did not.

Fix both halves. Give the endpoint a real scope predicate, and give every row a scope
label so a client-side scoping mistake is visible in the payload rather than inferred
from a working directory. Keep the legitimately fleet-wide surfaces fleet-wide, and say
so in the documents an orchestrator actually reads.

This is a list-contract and labelling change. It does not alter how project identity is
*assigned* (CARD-0115 S1 provenance rules stand), does not backfill historical rows,
does not add a board predicate to the pipeline or attention projections, and changes
nothing about dispatch, settlement, landing or card movement.

## Ground truth

Verified against `c2c067d3` and the live store on 2026-09-13.

| Claim or proposed shortcut | Evidence | Consequence |
|---|---|---|
| `boardId` / `projectId` are unsupported filters. | The handler binds exactly `rootId`, `since`, `status`, `includeChecks` ([AgentTaskEndpoints.cs:32](../../../server/Api/Endpoints/AgentTaskEndpoints.cs)). Minimal-API model binding discards unrecognised query keys without error. | The caller gets 200 and an unfiltered list. Adding the two keys is necessary but not sufficient: the next misspelling (`board`, `board_id`, `projectName`) fails the same silent way. |
| Scripts depend on the unfiltered list, so the shape is frozen. | The published contract says "scripts and `delegate.ps1` depend on that" ([docs/antiphon-api.md:245](../../antiphon-api.md)). `delegate.ps1` issues **no** list GET — only `/{id}`, `/areas`, and POSTs. `card.ps1` and the check interpreter do not call it. | The only production caller is `client/src/api/agentTasks.ts`. The compatibility constraint is narrower than documented; correct the document. |
| Project identity must be derived at query time. | `AgentTask.ProjectId` already exists and is persisted ([AgentTask.cs:68](../../../server/Domain/Entities/AgentTask.cs)), set once at create from caller provenance and inherited from the parent ([AgentTaskService.cs:832](../../../server/Application/Services/AgentTaskService.cs)). | Scope is a stored column, not a computation. Filtering is a `Where`, not a join-and-guess. |
| Project can be inferred from the working directory. | The field's own contract forbids it: "never from a filesystem path: sibling worktrees make path matching unsafe" (CARD-0115 S1). | `WorkingDirectory` / `RepoPath` stay **display** data. No path-derived scope predicate, in this plan or later. |
| A strict project filter would hide live Antiphon work. | Open rows (Queued/Dispatched/Working/Blocked), live store: Antiphon 7, gym-stat 3, markdown-package 1, and one null-project row whose `Role` is `Check` — a specialist, already hidden by the default `includeChecks=false`. | Every non-specialist open row carries a project today. Strict scoping costs nothing in the default active view. |
| A strict project filter is equally safe on history. | Of 4,033 `AgentTasks`, 2,908 have a null `ProjectId` (named: Antiphon 728, gym-stat 212, markdown-package 147, school-revision 24, slides 13, torquay-leander 1). | On a history window a strict filter silently drops ~72% of rows. Unscoped rows need explicit accounting, never a silent drop. |
| The reported leak would survive a project filter. | All five reproduced foreign rows carry a non-null `ProjectId` (`4aa3b3a3` slides, `58b504c4`/`58c746cc`/`6a1fd14f` gym-stat, `92952c11` markdown-package). | A `projectId` predicate suppresses exactly the observed leak. |
| Board is as strong a predicate as project. | `AgentTask` stores no `BoardId`; board is reachable only through `CardId → Card.BoardId` ([Card.cs:11](../../../server/Domain/Entities/Card.cs)). Three of the five leaked rows have `CardId` null. | Board and project are different-strength predicates. A board filter cannot classify a card-less row; project can. Support both, and never treat them as interchangeable. |
| The row already reveals its scope. | The summary DTO carries `CardId` and a denormalised `CardIdentifier` and nothing else ([AgentTaskDtos.cs:165](../../../server/Application/Dtos/AgentTaskDtos.cs), [ToSummary](../../../server/Application/Services/AgentTaskService.cs)). | Only `workingDirectory` hints at the repository, and `CARD-nnnn` is board-scoped, so two boards' `CARD-0039` are indistinguishable. |
| The board header is scoped like its rows. | `GetListSummaryAsync` aggregates every non-specialist row with no scope predicate ([AgentTaskService.cs:1417](../../../server/Application/Services/AgentTaskService.cs)). | A scoped row list under a fleet header is the trap that produced this incident; `/summary` must accept the same scope or the header lies. |
| Adding DTO fields is free. | `client/src/test/fixtures/contract/agent-tasks.json` is asserted by [ContractSnapshotTests.cs:317](../../../tests/Antiphon.E2E/ContractSnapshotTests.cs), with a dedicated summary-field drift test at :324. | Every field addition requires regenerating that fixture in the same commit. |

## Decisions

### D-1. Reject unknown query keys on this route

Validate the request's query keys against the route's supported set and return `400`
`unknown_query_parameter`, naming the offending key and listing the supported ones.
Applies to `GET /api/agent-tasks` only.

This is the anti-recurrence guard. Without it, `?boardId=` is fixed and `?board=` still
returns the fleet with a 200. The single production caller builds its query string in one
function (`queryForAgentTasks`), so no legitimate key is at risk; ad-hoc cache-busting
keys (`_=`, `t=`) would now be refused, which is the intended loudness.

This is the one behaviour-breaking element of the plan. It is small and separable — see
*Decisions remaining* if the caller wants it deferred to its own slice.

### D-2. Put scope identity on every row

Add to `AgentTaskSummaryDto`:

| Field | Source |
|---|---|
| `projectId`, `projectName` | stored `task.ProjectId`; else the project of the bound card's board |
| `boardId`, `boardName` | `task.CardId → Card.BoardId → Board` only |
| `scopeSource` | `Task` (stored project), `Card` (derived from the card's board), `None` |

`scopeSource=None` means *no trustworthy scope*, and is rendered as "unscoped". It is
never bucketed into the viewer's board and never guessed from `workingDirectory`.

Resolve in batch, following the existing `LoadCardIdentifiersAsync` pattern: one query
over the page's distinct card ids joined to `Boards` and `Projects`, one over the page's
distinct `ProjectId` values. Two queries per list call regardless of row count — no
per-row lookup, no lazy navigation.

This slice alone makes the reported incident self-evident on inspection, and changes no
existing behaviour.

### D-3. Scope predicate on the list

Add three query parameters:

- `projectId` (Guid) — matches resolved project identity (stored, or card-derived).
- `boardId` (Guid) — matches resolved board; card-less rows can never match it.
- `unscoped` — `exclude` | `include` | `only`. Default `exclude` when a scope parameter
  is present. Meaningless and refused when neither is.

Both `boardId` and `projectId` may be supplied; if the board's project differs from
`projectId`, refuse `422 scope_conflict` rather than silently intersecting. Supplying
only `boardId` does **not** widen to that board's project — a board question gets a
board answer.

With neither parameter present the route behaves exactly as today: same rows, same
order, same bare-array body.

### D-4. Exclusions are never silent

A scoped request returns an envelope:

```json
{
  "scope": { "projectId": "…", "projectName": "Antiphon", "boardId": null,
             "boardName": null, "unscoped": "exclude" },
  "items": [ "…AgentTaskSummaryDto…" ],
  "excluded": {
    "total": 4,
    "unscoped": 1,
    "byProject": [ { "projectId": "…", "projectName": "gym-stat", "count": 3 } ]
  }
}
```

An unscoped request keeps the bare array. The polymorphism is keyed on a parameter the
caller had to opt into, so no existing caller can receive the new shape, and no new
caller can receive the envelope without having asked to be scoped.

The accounting must be in the body. A response header carries the same information and
is missed the same way the absent filter was.

### D-5. `/summary` takes the same scope; `/pipeline` and `/attention` stay fleet-wide

`GET /api/agent-tasks/summary` accepts `boardId` / `projectId` / `unscoped` and applies
the identical predicate through a shared `AgentTaskScope` helper, so a scoped header can
never disagree with its scoped rows.

`GET /api/agent-tasks/pipeline` and `GET /api/attention` remain fleet-global with no
scope parameter. Both are deliberate fleet projections
([AgentTaskEndpoints.cs:48](../../../server/Api/Endpoints/AgentTaskEndpoints.cs),
[AttentionService.cs:15](../../../server/Application/Services/AttentionService.cs)), and a
human-attention queue that hides another repository's blocked delegate is a worse defect
than the one being fixed. Scoping either is a separate decision on a separate card.

### D-6. Client: label first, then offer scope

1. Every row shows a scope chip — project name, or "unscoped" — in `TaskChip` and the
   `DelegationsHistory` row. `CARD-nnnn` alone is ambiguous across boards.
2. `DelegationsBoard` and `DelegationsHistory` gain a scope selector (reusing
   `useBoards()`), defaulting to **All boards**, so today's view is unchanged until a
   human chooses otherwise.
3. When a scope is selected, render `excluded` as a visible line: *"hidden by scope: 3
   gym-stat, 1 unscoped"*. A filtered view that silently shrinks is the same class of
   defect.
4. Unscoped headers read "Fleet — all boards".
   `PipelineStagesPanel` already says "Fleet-wide stage glance"; leave it.
5. `queryForAgentTasks` and `agentTaskKeys.list` take the scope into the cache key, and
   the response type becomes a discriminated union on whether a scope was requested.

### D-7. Documentation

- [docs/antiphon-api.md](../../antiphon-api.md) list entry: the three new parameters, the
  envelope, the unknown-key refusal, and the new row fields. Delete the false clause
  "scripts and `delegate.ps1` depend on that" — replace with the web client, which does.
  Add to `/summary` that it takes the same scope; add to `/pipeline` an explicit
  "fleet-wide across every board; there is no board filter".
- [docs/ops-http.md](../../ops-http.md) has no row for listing delegated work at all,
  which is why an orchestrator reached for the endpoint unaided. Add one, leading with
  `?projectId=`, and state that an omitted scope returns every board.
- Same sentence for `GET /api/attention` wherever it is listed.
- [AGENTS.md](../../../AGENTS.md), *Cards and tracker*: one line — a delegated-work
  listing needs an explicit `projectId`/`boardId`; an unscoped list is the whole fleet.

### D-8. Rejected alternatives

| Rejected | Why |
|---|---|
| Derive project from `WorkingDirectory` / `RepoPath`. | Forbidden by the field's own contract (CARD-0115 S1): sibling worktrees make path matching unsafe. Paths stay display-only. |
| Backfill `ProjectId` on the 2,908 historical rows. | Not derivable trustworthily for card-less rows, and a confidently wrong scope is worse than an honest "unscoped". Separate card if ever wanted. |
| Return every row with an `inScope` boolean and no server filter. | Leaves the curl/agent caller — the actual victim here — still receiving foreign rows. |
| Report exclusions in a response header. | Miss-prone in exactly the way the missing filter was. |
| Always return the envelope. | Cleaner, and genuinely tempting given that the only in-repo caller is the client. Recorded as a live decision below rather than taken unilaterally. |

## Implementation slices and coverage targets

| Slice | Content | Files |
|---|---|---|
| S1 | Scope identity on the row: DTO fields, batch resolution, `scopeSource`, UI chip, contract fixture regenerated. No behaviour change. | `AgentTaskDtos.cs`, `AgentTaskService.cs` (`ToSummary`, new `LoadScopeLabelsAsync`), `client/src/api/agentTasks.ts`, `TaskChip.tsx`, `DelegationsHistory.tsx`, `client/src/test/fixtures/contract/agent-tasks.json` |
| S2 | Scope predicate + envelope: `AgentTaskScope` helper, `boardId`/`projectId`/`unscoped`, `422 scope_conflict`, `excluded` accounting, `/summary` scope. | `AgentTaskEndpoints.cs`, `AgentTaskService.cs` (`ListAsync`, `GetListSummaryAsync`), `AgentTaskDtos.cs` |
| S3 | Unknown-key refusal (D-1) — separable, behaviour-breaking. | `AgentTaskEndpoints.cs` |
| S4 | Client scope selector, excluded-accounting line, header wording, cache keys. | `DelegationsBoard.tsx`, `DelegationsHistory.tsx`, `client/src/api/agentTasks.ts` |
| S5 | Docs. | `docs/antiphon-api.md`, `docs/ops-http.md`, `AGENTS.md` |

S1 is independently shippable and is the slice that would have made the incident
visible. S2 depends on S1's resolution helper. S3 and S4 depend on S2. S5 follows S2/S3.

## Acceptance cases for TestDesign

Server:

1. No scope parameter → response is a bare JSON array, identical rows and order to
   pre-change behaviour (regression fence on the legacy shape).
2. `?projectId=<Antiphon>` → envelope; `items` contains only Antiphon rows; the three
   gym-stat rows appear in `excluded.byProject`.
3. `?projectId=<X>` with a stored-project row and a card-derived row → both in `items`,
   with `scopeSource` `Task` and `Card` respectively.
4. `?projectId=<X>&unscoped=exclude` (default) → a null-project row is absent from
   `items` and counted in `excluded.unscoped`.
5. `unscoped=include` → that row is in `items`; `unscoped=only` → only such rows.
6. `unscoped` without `boardId`/`projectId` → refused.
7. `?boardId=<B>` → a card-less row in the same project never matches.
8. `?boardId=<B>&projectId=<other>` → `422 scope_conflict`.
9. `?board=<B>` (unknown key) → `400 unknown_query_parameter` naming `board` (S3).
10. `?boardId=<unknown guid>` → empty `items`, and `excluded.total` equals the row count
    it withheld — the leak's exact reproduction now returns nothing rather than 12 rows.
11. Scope combines with `status`, `since`, `rootId`, `includeChecks` without changing
    their existing semantics.
12. Row labels: `projectName`/`boardName` populated; `scopeSource=None` for a row with
    neither stored project nor card; no row's scope is ever derived from
    `workingDirectory`.
13. Query count is constant (2 scope queries) for a 1-row and a 200-row list — no N+1.
14. `/summary?projectId=<X>` counts agree exactly with the row count of
    `?projectId=<X>&status=<all>`; unscoped `/summary` is unchanged.

Client:

15. `queryForAgentTasks` emits scope keys only when a scope is chosen; the cache key
    distinguishes two different scopes.
16. Scope selector defaults to All boards and renders the bare-array response.
17. A scoped view renders the `excluded` line with the per-project counts.
18. Contract snapshot test passes against the regenerated fixture.

## Delivery and decisions remaining

Two decisions belong to the orchestrator, not to Code:

- **Envelope shape.** Option A (planned): envelope only on a scoped request, bare array
  otherwise — compatible, mildly polymorphic. Option B: always the envelope — cleaner,
  one in-repo caller to migrate, and it breaks ad-hoc `curl` callers *loudly*, which is
  arguably the desired outcome given that the documented script dependency does not
  exist. A is planned; B is a one-word change to D-4 if preferred.
- **S3 (unknown-key refusal).** Planned as its own slice precisely so it can ship later
  or not at all. Shipping S2 without S3 fixes the reported spelling and leaves the
  general silent-key class open.

Not in this plan: any board predicate on `/pipeline` or `/attention`; any backfill of
historical `ProjectId`; any path-derived scope.

Next stage: **testdesign**.
