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
  "scripts and `delegate.ps1` depend on that". Name the real production list callers:
  the web client, `scripts/checkpoint-task.ps1`, and `scripts/prune-test-data.ps1`.
  `delegate.ps1` still does not list. PowerShell callers read `.items` off the envelope.
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

## Verification design

TestDesign task `a390d2fd`, 2026-09-14; inspected checkout `8b8fe86a`, including
landed plan `a17ceec3`. This section appends verification; the fix design above is
preserved. The commissioning brief resolves D-4 to **Option B: always an envelope**
and requires **S3 to ship with S2**. These decisions supersede the bare-array
expectations in D-3/D-4/D-6 and acceptance cases 1/16, and the conditional response
union. All callers consume one envelope type. Neither decision remains open.

For assertions, an omitted scope has `scope: null`, `items` with the legacy rows
and CreatedAt-ascending order, and `excluded: { total: 0, unscoped: 0, byProject: [] }`.
A selected scope echoes the supplied IDs, resolved names (null for unknown IDs),
and effective unscoped mode. `None` means no resolved project, not merely a null
stored ProjectId. Exclusions are computed from the candidate set **after** root,
status, time-window and specialist predicates, **before** scope. Every withheld
resolved-project row belongs in byProject, including another board in the requested
project. Thus `excluded.total = excluded.unscoped + sum(byProject.count)` and
`items.Count + excluded.total = candidate.Count`. Group by project ID, not name.
For matching board/project parameters use conjunction; only a known board whose
project differs produces scope_conflict. An unknown ID never becomes omitted scope.
These are explicit test interpretations of D-2 through D-5, not new dispatch rules.

### Inspection

- `tests/Antiphon.Tests/Application/AgentTaskProjectScopeTests.cs`: all six test
  bodies, CreateService/CreateContext, SeedProjectBoard/SeedCard/SeedTask and
  TempWorkspace read | stored provenance, parent inheritance, retry and no path
  inference -> V-1, R-3, R-8; creation policy remains a regression fence.
- `tests/Antiphon.Tests/Application/AgentTaskCardBindingTests.cs`: bodies of
  `specialist_rows_are_hidden_from_the_list_unless_asked`,
  `the_summary_exposes_the_card_id_and_identifier`,
  `a_history_window_hides_old_terminal_tasks_but_keeps_old_open_work`,
  `fleet_summary_matches_the_unfiltered_delegations_list`, plus TaskRow,
  CreateService/CreateContext and project/board/card seed helpers read |
  all three specialist roles, list/detail identity, terminal versus open history,
  full-history summary -> R-8, R-10, R-11.
- `AgentTaskServiceIntegrationTests.cs`: SeedTaskAsync/CreateService/CreateContext
  and TempWorkspace read | nearest general service constructor/seed pattern;
  its shared-store fixture is not suitable for exact fleet counts. New scope tests
  use the isolated fixture above instead. Unrelated dispatch bodies are not a new
  test target on this card.
- `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs`, and
  `AntiphonWebAppFactory.cs` configuration, reset and disposal bodies read;
  `AgentReplyStyleEndpointTests.cs` class setup and HTTP round-trip pattern read |
  real PostgreSQL, factory-owned cloned database, real route binding/middleware,
  runner refusal and disabled seats -> all server R rows. Despite its historical
  name, CreateIsolatedSchemaAsync now clones a **database**, not a SearchPath schema.
- `CardFilePrivacyBoundaryAcceptanceTests.cs` RejectPrivateSelect interceptor
  body read | nearest command-interceptor pattern -> R-12. No existing reusable
  scope-query counter was found; Code must add the counter described below.
- `tests/Antiphon.E2E/ContractSnapshotTests.cs`:
  `Delegated_task_board_and_drawer_contracts`, its fixed five-task seed,
  DelegateAgent/Task, SnapshotAsync/Scrub/Normalize/FixturesDir read;
  `Fixtures/SharedApp.cs`, AntiphonAppFixture startup and IsolatedSessionRunner
  startup/ownership read; both `agent-tasks.json` and `agent-task-detail.json`
  payloads read | real HTTP projection and repeated comparison -> V-3, R-17.
  Line 317 captures the list, line 319 captures detail; line 324 is a comment
  explaining `Home_tasks_rail_contract`, not a separate summary-field assertion.
- `client/src/features/delegations/{DelegationsBoard,DelegationsHistory,TaskChip}.test.tsx`:
  every body and local summary/detail/serveTasks helper read |
  windows, lanes, history order, virtualization, drawers and new selectors/labels
  -> R-14 through R-16. Existing handlers return arrays and require migration.
- `client/src/test/utils.ts`, setup.ts, mocks/server.ts and default-handler setup
  read; `client/src/api/boards.test.tsx` useUpdateCardContent and useReopenCard
  hook/invalidation bodies and boardStub read | nearest fixture for new
  `api/agentTasks.test.tsx`: fresh QueryClient, Mantine env=test, real hooks/MSW,
  unhandled requests fail -> R-13. queryForAgentTasks is private today; exercise
  its emitted URL through useAgentTasks rather than exporting it just for tests.
- `client/src/features/home/{HomePage,MobileHomePage}.test.tsx`: every body and
  seed helper read; both production consumers and both delegation Storybook
  seedClient/withHistoryData bodies read | always-envelope migration also reaches
  DesktopHomePage, ToReadBadge, MobileHomePage and cached story data -> R-19.
- Production ListAsync/GetListSummaryAsync, LoadCardIdentifiersAsync/ToSummary,
  list/summary endpoint bodies and ParseStatuses, Board.ProjectId, ValidationException
  and BadRequestException read | existing predicates and binding -> R-1 through
  R-12. `status=all` is not a supported token: case 14 uses **omitted status** or
  the explicit enum list, not the literal word `all`.

Required setup to implement (missing today, fully specified here):

1. Add `tests/Antiphon.Tests/Application/AgentTaskScopedListTests.cs` (alias **S**
   below), `AgentTaskScopedListEndpointTests.cs` (**E**), and a task-local fixture
   `tests/Antiphon.Tests/TestHelpers/ScopedAgentTaskListFixture.cs`. Both classes
   are Category Integration. S owns a cloned database per test. E owns a factory
   per test, retaining AntiphonWebAppFactory's refusing runner and disabled hosted
   seats; seed only after boot, dispose the host before its store. Do not adopt
   the shared per-session factory for exact unscoped counts. Seed via DbContext,
   not POST dispatch. Reuse the constructor pattern already read; no new queue,
   dispatcher or real provider is needed.
2. Fixture F: projects X=Antiphon, Y=gym-stat; boards B1/B2 in X, C in Y; cards
   with the same identifier `CARD-0039` on B1 and C. Nine non-specialist tasks,
   all Working, fixed distinct CreatedAt values, deterministic IDs/costs:

   | Task | Stored project | Card's board | Resolved identity/source |
   |---|---|---|---|
   | x1 | X | B1 | X/B1/Task |
   | x2 | null | B1 | X/B1/Card |
   | x3 | X | none | X/null/Task |
   | x4 | X | B2 | X/B2/Task |
   | y1 | Y | C | Y/C/Task |
   | y2 | Y | none | Y/null/Task |
   | y3 | Y | B1 | Y/B1/Task (stored precedence) |
   | n1 | null | none | null/null/None |
   | n2 | null | none | null/null/None |

   Give n1/n2 paths indistinguishable from x1 (including a sibling worktree and
   RepoPath); give y2 an X-looking path. At least two X tasks share a root and
   another has a different root. Default project X -> x1..x4, exclusions 5,
   unscoped 2, Y 3. Board B1 -> x1,x2,y3, exclusions 6: X 2, Y 2, unscoped 2.
   B1+X -> x1,x2, exclusions 7: X 2, Y 3, unscoped 2. Inclusion of y3 by board
   alone is intentional: stored project and bound board are independent facts.
   With include, append n1/n2 to the ordinary matches; with only, return exactly
   n1/n2. Add zero-task and all-None variants. Compare exact ordered IDs and
   dictionaries derived from named seed constants, never the production resolver.
3. Extend F for boundary tables: each open state Queued/Dispatched/Working/Blocked;
   each terminal Succeeded/Failed/Canceled with CompletedAt at T-1 microsecond,
   T, T+1 microsecond and null; old CreatedAt; two roots; each specialist role
   Check/Distill/Diagnose, inside X/Y/None. Use fixed UTC T and PostgreSQL precision,
   not sleeps. Evaluate all 3 scope kinds (project, board, matching both) x 3
   explicit modes x includeChecks false/true = 18 combinations against the
   independent expected-ID oracle; add omitted-mode equivalence for each scope
   kind. Layer each legacy predicate individually, then one combined request.
   Status whitespace/case/duplicates retain ParseStatuses semantics; invalid
   status still 422. Identical CreatedAt ties have no promised tie ordering.
4. Query counter: fixture-local DbCommandInterceptor observes successful sync
   and async reader/scalar commands on the actual list DbContext. Reset after
   seeding/warmup; use fresh contexts for the measured calls. Count actual SQL
   (including untagged commands), retain command text for diagnosis, classify the
   card/board/project join and stored-project batch by their tables/projection.
   In R-12, 1 then 200 rows each populate **both** lookup sets; the 200-row arm
   has 200 distinct cards and stored project IDs so identity-map caching cannot
   conceal per-row calls. Assert both item counts and labels, exactly 2 label
   SELECTs in each arm, and equal total command counts. Separate empty/no-ID
   cases permit skipped empty-set lookups (0..2), never per-row work. Tags alone,
   mocked repository calls, equal zero counts or timing thresholds are insufficient.
5. Client fixtures need a typed envelope builder and boards handler, including
   two boards in X and one in Y. Use real hooks in the component tests, no hook
   mocks. Seed response data by request query so a missing query key returns the
   recognizable fleet rows and fails the DOM assertion. Give X and Y distinct
   list rows, counters and exclusion lines; hold the X response with a deferred
   promise while switching to Y to expose stale cache/response crossover.
   Reset handlers/QueryClient/localStorage/history after each case. Preserve the
   existing 20-second global test budget.
6. HTTP refusal expectations: unscoped without an ID, and unrecognized mode
   (including empty supplied value) -> 422 validation_failed on both routes;
   scope_conflict -> 422; unknown list key -> 400 unknown_query_parameter. The
   current BadRequestException fixes its code to bad_request: Code needs a
   compatible HttpException-based coded error for D-1, not a ValidationException
   that would silently return 422. Assert real Problem Details code and status.
7. The contract fixture starts an isolated runner process. Keep its global
   NotInParallel attribute and add the existing E2E assembly's
   ParallelLimiter<ProcessSpawnLimit> to the touched contract class; it currently
   lacks that attribute. This is inherited harness safety, not a new production
   delivery guard. Do not co-schedule it with the native/Pty assemblies.

### Delivery inventory

No new or changed asynchronous **outcome delivery** path exists in S1-S5. These
are read-only list/summary projections and client HTTP queries. Producer =
AgentTaskService projection; destination = requesting HTTP client and rendered
list; persistence boundary = existing AgentTask/Card/Board/Project rows; durable
row identity = AgentTask.Id plus resolved project/board IDs; recovery = repeat
the GET with the same query; observable result = response items/accounting and
client DOM under that selected scope. There is no new delivery ledger, queue,
enqueue handoff, session input or delivery acknowledgement to recover.

R-14/R-15 cover a client with a pending prior request and one ready for a response,
through real client query hooks and MSW. MSW substitutes HTTP response production:
it proves client receipt/rendering and cache separation, not server filtering or
native delivery. E tests use the real server route/EF/PostgreSQL and prove the
response body, not browser rendering. R-17 connects real server JSON with the
checked-in client fixture. These substitutes are combined deliberately; none is
claimed as session delivery proof. Busy/eligible session recipients, crash/enqueue
recovery and complete UserPrompt evidence are excluded because no session input
path changes. If Code changes any outcome producer or queue path, return to
TestDesign: Review must reject acceptance stopping before recipient evidence,
and every new delivery/recovery guard must receive its own PC. GET success,
business completion, queue insert, Sent flags and transport acknowledgements
cannot stand in for that evidence.

### Proves it works now

These are Code's required future executions, not runs claimed by TestDesign.

- V-1: stored provenance, relational filtering, request validation, exact accounting
  and legacy behavior | TUnit + real PostgreSQL/HTTP | Unit lane and the four
  named integration classes in the command recipe below | every intended method
  executes nonzero and R-1..R-12 pass; no production runner launch.
- V-2: client consumes envelopes, issues matching list/summary queries, keeps scope
  caches apart, labels rows and exposes exclusions | Vitest/MSW + TypeScript/Vite |
  six named test files, client build/lint and Storybook build below | R-13..R-16,
  R-19 pass, both contract-backed stories build with the envelope and board cache.
- V-3: contract regeneration in the same implementation slice as DTO/envelope
  changes | real Kestrel/PostgreSQL, isolated E2E runner | exact
  `ContractSnapshotTests.Delegated_task_board_and_drawer_contracts` | first capture
  both files, then a second execution compares both existing files without writes;
  no CAPTURED line in the acceptance run. Review the full diff for pre-existing
  missing summary fields; do not silently bless unrelated drift.
- V-4: documentation names projectId/boardId/unscoped, always-envelope shape,
  coded unknown-key refusal and matching summary scope; explicitly fleet-wide
  pipeline/attention; false delegate.ps1 list dependency removed | document review |
  inspect S5 diff and `git diff --check` | examples agree with E tests. No text-only
  test is credited for server behavior. Existing AGENTS instruction-import contract
  must remain intact if its requested line is added.

### Guards the regression

Names below are exact methods/test titles Code must implement unless marked
existing. S/E expand to the two new classes named in setup. B/H/C mean the existing
DelegationsBoard/DelegationsHistory/TaskChip test files. A means new
`client/src/api/agentTasks.test.tsx` (describe `agentTasks scope`).

| ID | Acceptance cases / regression | Test and decisive assertion |
|---|---|---|
| R-1 | 1 (Option B), legacy order/shape | E.Unscoped_list_always_returns_envelope: root is object, scope null, zero exclusions, items exactly F ordered IDs; add empty fleet. Compare legacy field values, not merely count. |
| R-2 | 2, 3, 7; strict predicates | E.Project_and_board_predicates_select_exact_rows: F project X/B1/B1+X expected sets above, scoped echo IDs/names/mode exact. Foreign project cannot pass project X; cardless x3 and other-board x4 cannot pass B1. |
| R-3 | 3, 12; precedence/fallback | S.Stored_project_wins_over_card_project: y3 -> Y/Task under project Y, excluded under X; S.Card_project_fallback_remains_scoped: x2 included in X with Card, not in unscoped=only; query leaves its persisted ProjectId null. |
| R-4 | 4, 5; modes | E.Unscoped_modes_have_exact_membership: 18 combinations plus omitted-mode controls from setup; None rows counted/returned once, include never admits foreign resolved rows, only never admits resolved rows. Verify all-None and zero-task variants. |
| R-5 | 6, 8; invalid intent | E.Unscoped_without_scope_is_refused, E.Invalid_unscoped_mode_is_refused, E.Conflicting_board_project_is_refused: both list/summary status/code assertions; conflict in exclude/include/only, matching IDs succeed. |
| R-6 | 9; typo silently widens | E.Unknown_list_query_keys_are_refused: board, board_id, projectName, `_`, t, valid keys mixed with one unknown, unknown empty value -> 400/code, offending key and full supported set rootId/since/status/includeChecks/projectId/boardId/unscoped. Supported keys (also ordinary binding casing) remain accepted. No global middleware expansion to other routes. |
| R-7 | 10; unknown means fleet | E.Unknown_scope_ids_never_widen: unknown board, unknown project, Guid.Empty, unknown board+known project; default empty items and excluded.total=9 for F. Include/only can return n1,n2 only. Malformed GUID still binding 400. Distinguish absent key from syntactically valid unknown value. |
| R-8 | 12; honest labels | S.Project_labels_match_resolved_identity and S.Board_labels_follow_only_bound_card: assert names+IDs for x1/x2/x3/y3 and colliding CARD-0039 rows in list and detail, null board on x3. S.Paths_never_supply_scope: n1/n2 stay null/None in both output and fresh DB; y2 remains Y despite X-looking paths. Existing card identifier test also passes. |
| R-9 | 2, 4, 10; accounting | E.Exclusions_partition_the_candidate_set: exact F counts for X/B1/B1+X under each mode, returned+excluded=9. Add Y2 with same name as Y but distinct ID and a withheld row; expect two keyed buckets. Foreign rows removed by legacy predicates are absent from excluded. No double counting a row mismatching both IDs. |
| R-10 | 11; legacy filters | S.Root_filter_precedes_scope_accounting, S.Status_filter_precedes_scope_accounting, S.History_boundary_preserves_open_work, S.Specialists_require_include_checks: use setup's fixed boundary rows and oracle; exact item IDs and excluded counts, individually and combined. At T terminal included, below T or null completion terminal omitted; all old open states retained unless status/root excludes them. Check/Distill/Diagnose all hidden by default in X/Y/None. |
| R-11 | 14; lying header | E.Scoped_summary_matches_full_history_rows: scopes project/board/both plus each mode and unscoped/unknown IDs; independently assert Active=Dispatched+Working, Blocked, distinct Runs, sum of own CostUsd, complete ByStatus and its sum against seeded expected rows **and** matching unwindowed list. Seed different statuses/costs/roots, including old terminal rows and specialists. Summary always excludes specialists, even when a separate list includes them. Existing fleet_summary_matches_the_unfiltered_delegations_list also passes. |
| R-12 | 13; N+1 | S.Scope_lookup_command_count_is_constant: 1/200 distinct-key arms, exact 2 label SELECTs per call, total commands equal, counts 1/200 and correct labels; empty/None variants never exceed 2 label SELECTs. Counter cannot be satisfied by no execution. |
| R-13 | 15; URL/cache/invalidation | A titles `list requests preserve every scope parameter`, `summary requests preserve every scope parameter`, `list caches cannot cross scopes`, `summary caches cannot cross scopes`, `task mutations invalidate every scoped summary`: actual MSW URLs and hook result IDs/counters; vary project, board, both and mode independently, omitted/exclude normalize consistently, preserve since/status/includeChecks. Seed two scoped summary keys then cancel a fake task through real mutation hook and assert both query states invalidated. List/detail prefix invalidation remains. |
| R-14 | 16 (Option B), 17; board UI | B titles `board defaults to the fleet envelope`, `board selection scopes rows and counters`, `board shows exact exclusions`: default All boards and Fleet — all boards, then X-board -> Y-board -> All; capture identical scope on list/summary and no scope after reset. Resolve delayed X after Y and assert no X rows, counters or exclusions under Y. Check visible hidden-by-scope counts including no matching items, and hide line only when total=0. Pipeline remains explicitly fleet-wide. |
| R-15 | 16 (Option B), 17; history UI | H titles `history defaults to the fleet envelope`, `history selection scopes rows and counters`, `history shows exact exclusions`: same scope-switch and delayed-response assertions as board; settled filter and 7-day/Show all behavior preserved. Empty scoped items must still show exclusions. |
| R-16 | 12/client labels | C `chip labels resolved and unscoped tasks`; H `history labels resolved and unscoped tasks`: render Task/Card/None rows, scoped within each row, exact project name or unscoped, never path-derived X on n1/n2; duplicate card identifiers remain distinguishable by scope. |
| R-17 | 18; stale JSON contract | Existing ContractSnapshotTests.Delegated_task_board_and_drawer_contracts: regenerate list **and detail** fixtures. Extend fixed seed with X/Y project/board/cards so Task/Card/None and populated names occur; preserve deterministic IDs/times, root filter and existing five tasks. Then compare both fixtures on a second run. A fixture existing only after the acceptance run is not a pass. |
| R-18 | D-5, D-7; accidental fleet scoping/docs | V-4 diff review: no new scope predicate/parameters in AgentTaskPipelineStatusService or AttentionService and no board-selection arguments passed to their hooks; pipeline wording still Fleet-wide stage glance. Tests proving unrelated pipeline/attention business behavior are excluded because those services are unchanged. |
| R-19 | always-envelope consumers | Existing HomePage.test.tsx and MobileHomePage.test.tsx suites after migrating seed handlers: desktop directory-union assertion still includes both C:\\src\\antiphon and C:\\wt\\card-216; mobile `shows running work as soon as tasks arrive while attention remains pending` still shows its task. Add HomePage `unread badge consumes envelope items` using a recent Succeeded unread task and assert To read (1). Storybook seeds whole envelopes; derive settled items from fixture.items and seed useBoards data. |

Boundary exclusions: database-corrupt dangling card/board/project references are
not seeded by disabling referential integrity; persisted null is covered instead.
Board.ProjectId is required. Archived boards retain identity and get a positive
case in R-8; archive is not deletion and must not erase a task's label. Case
combinations with multiple simultaneous invalid inputs do not prescribe which
error wins; each invalid input is independently refused. No pagination contract
exists here, so 200 is a cardinality probe, not a page-size limit. Resource
authorization is not changed: this scope feature is a visibility contract, not a
new access-control boundary.

### Guard inventory

Safety-critical here means preventing falsely scoped results, hidden work or
misleading scope/accounting, including inherited filters touched by this change.
Independent route, cache and rendering guards have separate controls. The query
budget and snapshot guard are included too, although neither grants authority.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-3 project equality excludes foreign resolved projects | PC-1 |
| G-2 | D-2 card fallback remains scoped when stored project is null | PC-2 |
| G-3 | D-2 stored project wins over conflicting card project | PC-3 |
| G-4 | D-3 board equality does not widen to same-project/cardless rows | PC-4 |
| G-5 | D-3 default/exclude omits None rows | PC-5 |
| G-6 | D-3 include adds only None rows to scoped matches | PC-6 |
| G-7 | D-3 only excludes every resolved row | PC-7 |
| G-8 | D-3 unscoped needs an explicit scope ID | PC-8 |
| G-9 | D-3 invalid unscoped mode is refused | PC-9 |
| G-10 | D-3 conflicting board/project is refused, even with include/only | PC-10 |
| G-11 | D-1/S3 unknown list key is a coded 400, never an ignored filter | PC-11 |
| G-12 | D-3 unknown supplied scope never falls back to fleet | PC-12 |
| G-13 | D-2 resolved project ID/name is projected accurately | PC-13 |
| G-14 | D-2 board ID/name comes only from bound card | PC-14 |
| G-15 | D-2/D-8 paths never create identity; None remains honest | PC-15 |
| G-16 | D-4 total accounts for all and only withheld candidates | PC-16 |
| G-17 | D-4 None exclusions are counted explicitly | PC-17 |
| G-18 | D-4 byProject accounts by ID, including same-project other boards | PC-18 |
| G-19 | D-3 legacy root restriction survives before accounting | PC-19 |
| G-20 | D-3 legacy status restriction survives before accounting | PC-20 |
| G-21 | D-3 history retains open work irrespective of age | PC-21 |
| G-22 | D-3 all specialist roles remain opt-in | PC-22 |
| G-23 | D-5 summary uses the same scope predicate as list | PC-23 |
| G-24 | D-2 two batch label queries, no N+1 | PC-24 |
| G-25 | D-4 Option B every list response is an envelope | PC-25 |
| G-26 | D-6 client list URL carries selected scope | PC-26 |
| G-27 | D-5/D-6 client summary URL carries selected scope | PC-27 |
| G-28 | D-6 list cache isolates every scope dimension | PC-28 |
| G-29 | D-5/D-6 summary cache isolates every scope dimension | PC-29 |
| G-30 | D-6 TaskChip shows trustworthy scope or unscoped | PC-30 |
| G-31 | D-6 history row shows trustworthy scope or unscoped | PC-31 |
| G-32 | D-6 board visibly reports exclusions even for empty items | PC-32 |
| G-33 | D-6 history visibly reports exclusions even for empty items | PC-33 |
| G-34 | D-6 board selected scope drives both queries and fleet reset | PC-34 |
| G-35 | D-6 history selected scope drives both queries and fleet reset | PC-35 |
| G-36 | D-5 scoped summary caches are all invalidated after task changes | PC-36 |
| G-37 | S1/acceptance 18 checked-in list/detail fixtures detect field drift | PC-37 |
| G-38 | D-6 unscoped board header explicitly says Fleet — all boards | PC-38 |
| G-39 | D-6 unscoped history header explicitly says Fleet — all boards | PC-39 |
| G-40 | D-3 terminal CompletedAt boundary is inclusive | PC-40 |
| G-41 | D-4 response scope metadata echoes the actual selected scope | PC-41 |

No unmapped or untested safety-critical guard is deferred. Existing provenance
assignment, dispatch and delivery guards are outside the changed area and retain
their own plans. No new guard is claimed for static documentation or build steps.

### Positive controls

All defects below are temporary, compiling production-code mutations.
Each runs separately unless the
Mutation stage proves file/method independence. Tests and expected assertions
stay intact. S/E and A/B/H/C expand exactly as in R. For data-driven methods the
exact method selector executes its arguments; require the indicated failing arm,
not an unrelated assertion or fixture failure. Code implements tests and runs
V/R; ordinary Review judges them before land. Mutation reports break, red,
restore, green **after confirmed land**, with a result for every PC.

- PC-1: break G-1 by replacing resolved-project equality with true while preserving
  other predicates; expect E.Project_and_board_predicates_select_exact_rows red
  at X items == [x1,x2,x3,x4] (y1/y2/y3 wrongly present).
- PC-2: break G-2 by returning null instead of card-project fallback; expect
  S.Card_project_fallback_remains_scoped red at X IDs containing x2 and x2 source Card.
- PC-3: break G-3 by preferring card project before task.ProjectId; expect
  S.Stored_project_wins_over_card_project red at y3 project == Y/source Task.
- PC-4: break G-4 by using the board's project equality in place of board equality;
  expect E.Project_and_board_predicates_select_exact_rows red at B1 IDs == [x1,x2,y3].
- PC-5: break G-5 by admitting None rows in the default/exclude branch; expect
  E.Unscoped_modes_have_exact_membership red at project-X exclude IDs == x1..x4.
- PC-6: break G-6 by making include accept every candidate; expect
  E.Unscoped_modes_have_exact_membership red at X include excluding y1/y2/y3.
- PC-7: break G-7 by making only accept all scoped matches as well as None;
  expect E.Unscoped_modes_have_exact_membership red at only IDs == [n1,n2].
- PC-8: break G-8 by removing the unscoped-without-ID refusal; expect
  E.Unscoped_without_scope_is_refused red at response status == 422 on each route.
- PC-9: break G-9 by parsing an invalid mode as exclude; expect
  E.Invalid_unscoped_mode_is_refused red at 422/validation_failed for `banana`.
- PC-10: break G-10 by skipping the known-board project conflict check; expect
  E.Conflicting_board_project_is_refused red at 422/scope_conflict, including only.
- PC-11: break G-11 by omitting list query-key validation; expect
  E.Unknown_list_query_keys_are_refused red at `?board=B1` status 400/code
  unknown_query_parameter. Keep B1 formatted as its seeded GUID.
- PC-12: break G-12 by clearing an unknown board ID before filtering; expect
  E.Unknown_scope_ids_never_widen red at unknown-board default items empty and total 9.
- PC-13: break G-13 by projecting null projectName for resolved rows; expect
  S.Project_labels_match_resolved_identity red at x1.projectName == Antiphon.
- PC-14: break G-14 by projecting null boardName despite a bound card; expect
  S.Board_labels_follow_only_bound_card red at x2.boardName == B1's seeded name.
- PC-15: break G-15 by assigning n1/n2 the fixture's X project through a
  WorkingDirectory match when no provenance exists (typed Guid constant in the
  mutation); expect S.Paths_never_supply_scope red at n1/n2 projectId null/source None.
- PC-16: break G-16 by returning excluded.total=0; expect
  E.Exclusions_partition_the_candidate_set red at X exclude total == 5.
- PC-17: break G-17 by returning excluded.unscoped=0; expect
  E.Exclusions_partition_the_candidate_set red at X exclude unscoped == 2.
- PC-18: break G-18 by grouping excluded resolved rows by project name and taking
  the first ID; expect E.Exclusions_partition_the_candidate_set red at separate
  Y and Y2 dictionary entries with exact counts (no merged same-name bucket).
- PC-19: break G-19 by dropping the rootId Where; expect
  S.Root_filter_precedes_scope_accounting red at exact candidate-derived item IDs
  and excluded total for the selected root.
- PC-20: break G-20 by dropping the requested-status Where; expect
  S.Status_filter_precedes_scope_accounting red at absence of the seeded Working
  row when requesting only terminal statuses.
- PC-21: break G-21 by removing the open-state disjunction from the since predicate;
  expect S.History_boundary_preserves_open_work red at old Blocked ID retained.
  The inclusive T assertion remains in the same method's ordinary boundary table.
- PC-22: break G-22 by retaining only Role != Check when includeChecks=false;
  expect S.Specialists_require_include_checks red at seeded Distill/Diagnose IDs absent.
- PC-23: break G-23 by bypassing the scope helper only in GetListSummaryAsync;
  expect E.Scoped_summary_matches_full_history_rows red at the X ByStatus/Active
  counts equal independently seeded X rows, not fleet counts.
- PC-24: break G-24 by replacing the stored-project batch with a foreach issuing
  an awaited AsNoTracking project query for each task, preserving returned labels;
  expect S.Scope_lookup_command_count_is_constant red at 200-row label SELECTs == 2
  and total commands == 1-row total. A fake increment to a test counter is invalid.
- PC-25: break G-25 by returning envelope.Items from the no-scope endpoint branch;
  expect E.Unscoped_list_always_returns_envelope red at JSON root Object.
- PC-26: break G-26 by omitting boardId from the list URL builder; expect A
  `list requests preserve every scope parameter` red at captured boardId == B1.
- PC-27: break G-27 by omitting projectId from the summary URL builder; expect A
  `summary requests preserve every scope parameter` red at captured projectId == X.
- PC-28: break G-28 by removing scope dimensions from agentTaskKeys.list; expect A
  `list caches cannot cross scopes` red at Y result IDs after X cache was populated.
- PC-29: break G-29 by removing scope dimensions from agentTaskKeys.summary; expect A
  `summary caches cannot cross scopes` red at Y counters after X cache was populated.
- PC-30: break G-30 by removing the scope label JSX in TaskChip; expect C
  `chip labels resolved and unscoped tasks` red at within(n1 chip).getByText('unscoped').
- PC-31: break G-31 by removing the history row scope label JSX; expect H
  `history labels resolved and unscoped tasks` red at within(n1 row).getByText('unscoped').
- PC-32: break G-32 by suppressing the board exclusion line when items is empty;
  expect B `board shows exact exclusions` red at visible Y/unscoped counts in the
  empty-items/nonzero-exclusions arm.
- PC-33: break G-33 by suppressing the history exclusion line when items is empty;
  expect H `history shows exact exclusions` red at the same empty-items receipt assertion.
- PC-34: break G-34 by passing empty scope options to the board list hook while
  keeping selected label/summary; expect B `board selection scopes rows and counters`
  red at list request boardId == selected B1 and no foreign item under B1.
- PC-35: break G-35 by passing empty scope options to the history list hook while
  keeping selected label/summary; expect H `history selection scopes rows and counters`
  red at list request boardId == selected B1 and no foreign item under B1.
- PC-36: break G-36 by restricting post-mutation summary invalidation to only the
  unscoped exact key; expect A `task mutations invalidate every scoped summary`
  red at both seeded scoped query states isInvalidated == true.
- PC-37: break G-37 by projecting the literal projectName `PC-37 drift` from
  ToSummary while keeping both checked-in fixtures intact; expect exact
  ContractSnapshotTests.Delegated_task_board_and_drawer_contracts red at
  SnapshotAsync Normalize(existing).ShouldBe(Normalize(scrubbed)). Restore exact
  production source bytes, rebuild, rerun the same method and verify neither
  fixture is rewritten or recaptured.
- PC-38: break G-38 by replacing the board's unscoped heading with `Delegations`;
  expect B `board defaults to the fleet envelope` red at visible Fleet — all boards.
- PC-39: break G-39 by replacing the history's unscoped heading with `History`;
  expect H `history defaults to the fleet envelope` red at visible Fleet — all boards.
- PC-40: break G-40 by changing CompletedAt >= windowStart to CompletedAt >
  windowStart while retaining the open-state disjunction; expect
  S.History_boundary_preserves_open_work red at the terminal ID completed exactly
  at T being present. This guard is independent of the open-state guard PC-21.
- PC-41: break G-41 by setting the response envelope's echoed projectId to null
  for a project-X request without changing filtering; expect
  E.Project_and_board_predicates_select_exact_rows red at scope.projectId == X.

Scope-mode validation and shared scope predicates must be shared by list/summary
as D-5 requires; the route table nevertheless exercises both to catch missed
wiring. PC red/restore/green must rebuild restored sources with fresh timestamps;
zero tests, compile errors, host boot errors and missing fixtures are not red.
Do not widen tests or timeouts to force a result. SourceLanding Mutation keeps
evidence externally, restores exact tracked/index bytes, never commits from its
snapshot, and returns repairs to separate Code/Review/land work.

### Out of scope

- No dispatch, settlement, landing, card movement, project backfill, session
  transport or delivery change; hence no live model, Telegram, busy-session queue
  battery or UserPrompt receipt can add evidence for these GET predicates.
- Pipeline/attention scoping and Home's existing filesystem workspace grouping
  are separate product decisions. Home must consume envelope.items without
  changing that grouping. No path matching enters the new server scope helper.
- Native Pty/FakeClaude tests and browser screenshot suites are not needed for
  this projection change; contract E2E uses its already isolated runner. Client
  DOM tests prove visible text/state, not pixel layout or a live deployment.
- Full-assembly repetition after each fix/PC is excluded. The current owner
  `docs/testing-and-build.md` fast lane is Unit plus named integrations; broad
  nightly coverage is not credited as having run or as an operational backstop.

### Cost

All times below are **estimates**, not measurements from this TestDesign turn.
No build/tests were run here. Preconditions: pinned .NET SDK, Docker available,
Node dependencies installed; cold `npm ci` is budgeted below. Tests/builds execute
in the foreground, sequential project lanes, with source frozen per run. Code
checkpoints implementation before a long run. Use a task-owned `bin-c515/` output
with a forward slash and fresh TRX directories per invocation; verify nonzero
method counts in fresh TRX. Record and remove only producer-owned output paths
after canonical absolute containment checks, per the build owner.

Code recipe from repository root (client commands use `--prefix client`):

```powershell
npm ci --prefix client
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c515/ --nologo
dotnet build tests/Antiphon.E2E --property:OutputPath=bin-c515/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c515/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c515-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c515/ -- --treenode-filter '/*/Antiphon.Tests.Application/(AgentTaskScopedListTests*)|(AgentTaskScopedListEndpointTests*)|(AgentTaskProjectScopeTests*)|(AgentTaskCardBindingTests*)/*' --report-trx --report-trx-filename scope.trx --results-directory .antiphon/c515-scope
pwsh -File scripts/test-client.ps1 agentTasks.test.tsx DelegationsBoard.test.tsx DelegationsHistory.test.tsx TaskChip.test.tsx HomePage.test.tsx MobileHomePage.test.tsx
npm run build --prefix client
npm run lint --prefix client
npm run build-storybook --prefix client
dotnet run --project tests/Antiphon.E2E --no-build --property:OutputPath=bin-c515/ -- --treenode-filter '/*/*/ContractSnapshotTests/Delegated_task_board_and_drawer_contracts' --report-trx --report-trx-filename contract.trx --results-directory .antiphon/c515-contract-compare
pwsh -File scripts/test-duration-tripwire.ps1 -Trx .antiphon/c515-scope/scope.trx
git diff --check
```

For capture, preserve exact copies then remove only the two named contract files
and execute that same E2E method with results directory `.antiphon/c515-contract-capture`.
Review recaptured JSON, checkpoint it, then run the compare command above. Capture
is setup, not acceptance. If fixture seeding changes C# after a build, rebuild
before either run. Reused result-directory names need fresh task-owned suffixes.
Read a pre-existing failure at the base commit using its exact failing method;
report inherited failures separately instead of silently accepting them.

Every server PC uses `dotnet run --project tests/Antiphon.Tests
--property:OutputPath=bin-c515-pc/ -- --treenode-filter '/*/*/Class/ExactMethod'`
with Class/ExactMethod replaced by its literal S/E name from its PC row. No class
wildcard for PC cycles. PC-37 uses the exact E2E command above, rebuilding when
needed. Every client PC uses `pwsh -File scripts/test-client.ps1` plus its exact
file and `-t` with the anchored full describe/title; for example
`agentTasks.test.tsx -t '^agentTasks scope list caches cannot cross scopes$'`.
Require one intended title (or the explicit data arms), the named decisive red
assertion, restoration, then that same title green. No whole-file PC runs.

| Owner / work | Estimated minutes |
|---|---:|
| Code setup: dependency install 2 + both isolated .NET builds 4 + client build/lint 3 + Storybook build 2 + fixture capture 3 | 14 |
| Code ordinary V/R: Unit 3 + four integration classes including all boundary arms 4 + six client files 2 + contract compare 3 + TRX/duration/diff/doc audit 2 | 14 |
| **Code verification floor (setup + V/R)** | **28** |
| Mutation setup: clean baseline build/discovery and owned output/evidence setup | 4 |
| Mutation PC-1..PC-25, PC-40, PC-41: 27 x (red build/run 1.5 + restore 0.25 + green build/run 1.5) | 87.75 |
| Mutation PC-26..PC-36, PC-38, PC-39: 13 x (red 0.35 + restore 0.10 + green 0.35) | 10.40 |
| Mutation PC-37: contract red 3 + restore 0.25 + contract green 3 | 6.25 |
| Mutation receipt/TRX audit and restoration/output inventory | 3.10 |
| **Mutation floor (every PC red/restore/green + setup/audit)** | **111.5** |
| **Total verification floor = Code 28 + Mutation 111.5** | **139.5** |

Authoring implementation/tests is additional: estimate 90 minutes; ordinary
Review judgment is additional, estimate 20 minutes. Code's dispatch estimate is
therefore 118 minutes before defects; Mutation needs 111.5 minutes plus investigation
of surviving mutations. These are planning floors, not timeout ceilings.

Savings: compared with rebuilding both .NET graphs for each of the four ordinary
class runs, build-once saves an estimated 12 minutes (4 x 4 minus 4), while
preserving class coverage. The six-file client run saves an estimated 4 minutes
against an assumed 6-minute full client run. Budgeted PC batching/sharding savings
are **0 minutes**: most controls share service or API/client files, sourced Mutation
has one managed snapshot, and no safe independent batching is assumed. No control
or boundary arm is removed to claim savings; no current full-server-suite duration
is asserted.

Handoff audit: touched test bodies and nearest fixtures read; acceptance cases
18/18 mapped (1/16 amended by authorized Option B); guards=41, mapped=41,
missing=0, duplicate PC mappings=0. All 41 PCs have a compilable defect,
exact executable test selector and decisive assertion once Code
implements the named tests. Setup omissions are enumerated above; no unverifiable
seam or human choice remains. Next stage: **code**, then separate ordinary Review,
confirmed land and SourceLanding Mutation.

## Review repair (Code continuation)

Activation requires **`restart: server`** and a **client rebuild** (envelope list,
scoped `/summary` aggregates, History/Delegations heading and scope UI). `restart: none`
is incorrect. Original Code task `65ffa1fa` remains the landing owner.
