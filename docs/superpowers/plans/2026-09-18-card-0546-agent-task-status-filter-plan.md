# CARD-0546: the agent-task status filter is correct; pin it, and fix the idiom that misread it

Plan task `3fcef28a`, 2026-09-18, inspected checkout `26633049` (`feat/card-task-3fcef28a`; line
numbers below are at that SHA). Read-only against code; no fix was built. The brief asked for the
acceptance guards and the regression test plan in this stage, so `## Verification design` is included
and the next stage is Code.

Owners: [HTTP](../../ops-http.md), [route map](../../antiphon-api.md),
[testing](../../testing-and-build.md), [orchestration loop](../../orchestration-loop.md),
[conventions](../../project-context.md).

## Disposition in five lines

1. **The premise is wrong: the server filter works.** `Working` is a real, distinct enum value, the
   `status=` parameter parses it case-insensitively, the query translates to `WHERE "Status" IN (...)`,
   and an unrecognised value already answers `422 validation_failed`. Every status that has rows today
   returns them; `Working` returned `[]` on 2026-09-16 and today because the caller's pipeline prints a
   blank row for *any* array, empty or not (ground truth G-8..G-10).
2. **The root cause is a PowerShell idiom, not a query.** On the machine's PowerShell 7.6.6,
   `Invoke-RestMethod ... | Select-Object ...` emits the JSON array as one `Object[]`; only the
   parenthesised or `@()`-wrapped form enumerates rows. Every "empty" observation on the card used the
   bare form; every observation that showed rows used parentheses (G-9).
3. **No server code changes.** The card's ask 2 ("fix the query") has nothing to fix. Ask 3 is settled
   as keep-422 (D-1). What ships is regression coverage the filter has never had (D-3), a documented
   client idiom and the purpose-built occupancy read (D-4), and a corrected orchestrator-skill note.
4. **CARD-0541 is not touched.** Its shape is different: `boardId` is not a bound parameter on the
   route, so minimal-API binding drops the *key* silently. That is exactly CARD-0515's D-1/S3
   (`400 unknown_query_parameter`) plus its `boardId` predicate, both planned and test-designed,
   not yet coded (D-5).
5. **The card's own text should be corrected at landing** by the orchestrator, not by Code, because
   `docs/cards/` is generated from the board (D-6).

## Ground truth

| # | The card assumes | What the code and the record say | Evidence |
|---|---|---|---|
| G-1 | `Working` may not be a real status distinct from `Dispatched`. | `AgentTaskStatus { Queued=0, Dispatched=1, Working=2, Blocked=3, Succeeded=4, Failed=5, Canceled=6 }`. Stored as an integer column with no value converter; indexed. | [AgentTaskEnums.cs:81](../../../server/Domain/Enums/AgentTaskEnums.cs), [AppDbContext.cs:1717,1810](../../../server/Infrastructure/Data/AppDbContext.cs) |
| G-2 | `Working` may be unreachable or a display-only value. | It is written on the Blocked→answered transition and on the API-error deferred resume path, among others. `GET /{id}` and the list both read `task.Status` verbatim: `ToSummary` passes it through unchanged. | [AgentTaskReplyService.cs:321](../../../server/Application/Services/AgentTaskReplyService.cs), [AgentTaskService.cs:1595](../../../server/Application/Services/AgentTaskService.cs) |
| G-3 | The `status=` parameter may be case-sensitive or lack a parsing branch. | `ParseStatuses` splits on commas (trim, drop empties), `Enum.TryParse(ignoreCase: true)` + `Enum.IsDefined`, distinct. Present since 2026-08-28 (`856372b28`), unchanged since. | [AgentTaskEndpoints.cs:355](../../../server/Api/Endpoints/AgentTaskEndpoints.cs) |
| G-4 | The query may silently build to zero rows. | `ListAsync` applies `requested.Contains(t.Status)` (an `IN` list), then the specialist-role filter unless `includeChecks`, then the optional `since` window which never trims non-settled rows. | [AgentTaskService.cs:1595-1624](../../../server/Application/Services/AgentTaskService.cs) |
| G-5 | An unrecognised value silently returns `[]`. | It throws `ValidationException("status", "'X' is not a valid agent task status.")`, which the middleware maps to `422 validation_failed` with `errors.status`. Live: `?status=Bogus` → 422. | [ValidationException.cs:6](../../../server/Application/Exceptions/ValidationException.cs), [ExceptionMiddleware.cs:74](../../../server/Api/Middleware/ExceptionMiddleware.cs) |
| G-6 | The bug may be a regression around 2026-09-16. | The list route, `ParseStatuses` and `ListAsync` at the SHA deployed during the observation window (`0c99f7e6`, master reflog 09-16 10:57 BST) are byte-identical to HEAD on these paths. `NotSpecialist` is also unchanged. | `git show 0c99f7e6:...` compared during this plan |
| G-7 | Other status values may also return empty. | Live on 2026-09-18 (server `6ab13481`, 2004 non-specialist rows): `Dispatched` 3, `Blocked` 5, `Succeeded` 1774, `Failed` 138, `Canceled` 84 rows; `Queued` and `Working` `[]` because the unfiltered list (with and without `includeChecks`) contains zero rows in either status right now. `working`, `WORKING`, `Dispatched,Working` all accepted. | curl probes in this plan's task |
| G-8 | Direct GET said `Working` while the list said empty, on the same server. | Both tasks (`7dcb662f`, `c9a3d250`) were Codex Worktree rows created 10:13Z, held in `Working` by the API-error deferred resume, later Canceled. Neither is a specialist role, so nothing in G-4 hides them. The server log records path only (no query string), so the 10:29Z list response cannot be read back; nothing in code or data explains an empty result. | task events; `server/logs/antiphon-20260916.log` |
| G-9 | The list query itself returned `[]`. | The reporting session's transcript shows two forms. Parenthesised `(Invoke-RestMethod ...) \| Select-Object` printed rows at 08:35Z and 10:13Z and printed *nothing* at 10:28:33Z (genuinely zero `Dispatched` rows: both tasks were `Working`). Bare `Invoke-RestMethod ... \| Select-Object ... \| Format-Table` at 10:28:46Z, 10:29:12Z (`status=Working`) and 11:35:33Z printed a header plus one blank row, which an enumerated empty array can never produce. | `~/.claude/projects/C--src-Antiphon/39c6eb3a-5e96-4f67-9b09-253c106a1d64.jsonl`, 2026-09-16 |
| G-10 | (replication) | Today, with 2-3 `Dispatched` rows confirmed by curl, the bare form prints one blank row; `ForEach-Object { $_.GetType().Name }` prints `Object[]` once, and with parentheses prints `PSCustomObject` three times. `Invoke-RestMethod` in PowerShell 7.6.6 has no `-NoEnumerate` switch; the array is emitted as one object. | PowerShell probes in this plan's task |
| G-11 | The create-time 409 counts by a different mechanism. | True and irrelevant here: the concurrency gate and `/summary` count `Dispatched or Working` on the same column. `/api/agent-tasks/pipeline` (CARD-0304) lists in-flight rows per stage from the same column and is the purpose-built occupancy read. | [AgentTaskService.cs:1627](../../../server/Application/Services/AgentTaskService.cs), live `/pipeline` |
| G-12 | The filter has coverage that should have caught this. | No test exercises `ListAsync` with a status, `ParseStatuses`, or `GET /api/agent-tasks?status=`. The only list test is an E2E contract snapshot by `rootId`. | grep over `tests/` |
| G-13 | `checkpoint-task.ps1` uses the same query and may be wrong too. | It wraps the response in `@($response)` before iterating, so it enumerates correctly. | [checkpoint-task.ps1:114](../../../scripts/checkpoint-task.ps1) |
| G-14 | `docs/ops-http.md` shows the safe idiom. | Its PowerShell block uses the bare `Invoke-RestMethod ... \| Select-Object` form for `/api/agents` and `/api/cards`; the cards example also selects fields off a `{ cards, truncated }` envelope. The orchestrator skill's §5 declares the status filter a known bug citing this card. | [ops-http.md:100-105](../../ops-http.md), [SKILL.md:86](../../../.claude/skills/antiphon-orchestrator/SKILL.md) |

## Decisions

### D-1. An unrecognised status value stays `422 validation_failed`, not `400`

Keep the existing behaviour and pin it. `ValidationException` is the project-wide "bad value"
response and the middleware maps it to 422 with a structured `errors` map; the route already uses it.
`400` on this API means a *missing required* filter (`GET /api/cards` unfiltered) or, under CARD-0515
D-1, an *unknown query key*. A misspelt value is neither. Clients gain nothing from a renumbering and
`delegate.ps1` special-cases neither code.

Rejected: switch to 400 (breaks the convention for one route, no consumer benefit); accept-and-ignore
unknown values (the exact silent failure the card fears; it is not the current behaviour and must not
become it). A comma list with one bad entry is refused whole, never partially applied (pinned).

### D-2. No server code change

Every branch the card asked to check exists and is correct at HEAD and at the SHA that served the
observation. A "defensive" rewrite of `ParseStatuses` or `ListAsync` would be a change without a
failing test. Numeric input (`?status=2`) is accepted today through `Enum.TryParse`; it is neither
pinned nor rejected here (out of scope; nothing sends it).

### D-3. Regression guards at two levels, in two new test classes

Service level (`AgentTaskListStatusFilterTests`, shared `TestDbFixture`, every assertion scoped by a
fresh `RootTaskId` so it is exact on the shared database): each of the seven values round-trips, a
comma list unions, the `since` window keeps non-settled rows, and the specialist filter is the only
thing that hides a `Working` row. Endpoint level (`AgentTaskListEndpointTests` on
`AntiphonWebAppFactory`, isolated schema): case-insensitive parsing, comma lists, the 422 shape.

Rejected: only an E2E `ContractSnapshot` case (snapshots pin payload shape, not filter semantics, and
the E2E project is the slow lane); a single class (the two failure classes the card names live in
different layers and `ParseStatuses` is private to the endpoint).

### D-4. Fix the root cause where it lives: the documented client idiom

`docs/ops-http.md` gets a three-line pitfall note and its list examples move to the enumerating form;
the block gains the occupancy read (`status=Dispatched,Working,Blocked`) and points at
`GET /api/agent-tasks/pipeline` as the purpose-built check. `docs/antiphon-api.md` states the 422 and
case-insensitivity on the route line. The orchestrator skill's §5 bullet stops calling the status
filter a bug and states the idiom instead; the `boardId` caveat stays because it is real (D-5).

Rejected: a new `scripts/agent-tasks.ps1 -Status` wrapper (scope creep; `checkpoint-task.ps1` already
shows the correct `@()` idiom, and the skill can carry a one-line rule).

### D-5. CARD-0541 is CARD-0515's work; note the shape, do not fix it here

`boardId` is not a bound parameter on the route, so minimal-API binding discards the key without
error (G-14 in CARD-0515's own ground truth). CARD-0515 (InProgress) has planned the `boardId` /
`projectId` predicates and S3 unknown-key `400 unknown_query_parameter`, with verification designed
and 41 controls. The shared shape between 0541 and 0546 is only apparent: 0541 is a silently dropped
*key*; 0546 was a client rendering artefact on a correctly validated *value*. Recommendation for the
orchestrator: link CARD-0541 to CARD-0515 (or close it as a duplicate) rather than open a third plan.

### D-6. Correct the card at landing, through the board

The card's title and body assert a server bug. When this lands, the orchestrator appends a
correction to CARD-0546 with `card.ps1 edit -DescriptionFile` (prior text pulled from history first
and preserved), naming G-9/G-10 and the idiom. Code must not edit `docs/cards/` (generated).

## Implementation slices

### S1. Service-level guards (new file)

`tests/Antiphon.Tests/Application/AgentTaskListStatusFilterTests.cs`, `[Category("Integration")]`,
contexts from `TestDbFixture.CreateDbContextOptions()`, service built like
`AgentTaskCheckScheduleTests.CreateService`. Seed rows with the minimal shape used by
`AgentTaskConcurrencyLimitTests` (Id, RootTaskId, Title, Goal, Kind, Role, Status, Workspace,
WorkingDirectory, CreatedAt), all under one fresh `RootTaskId` per test, `Role = Code` unless the test
is about specialists. Never assert a global count. Tests V-1..V-5 below.

### S2. Endpoint-level guards (new file)

`tests/Antiphon.Tests/Application/AgentTaskListEndpointTests.cs`, `[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerClass)]`,
`[Category("Integration")]`, seeds through a scoped `AppDbContext` from `_factory.Services`, reads
with `_factory.CreateClient()`, asserts on the raw JSON (`JsonDocument`) so an enum rename cannot pass
by deserialising into the old name. Tests V-6..V-9 below.

### S3. Documents and the orchestrator skill

- `docs/ops-http.md` PowerShell block: add the pitfall note; rewrite the `/api/agents` example as
  `@(Invoke-RestMethod ...) | Select-Object ...`; rewrite the cards example as
  `(Invoke-RestMethod ...).cards | Select-Object ...`; add an occupancy example
  `@(Invoke-RestMethod "$api/api/agent-tasks?status=Dispatched,Working,Blocked" -Headers $h) | Select-Object id, role, status, cardIdentifier`
  with a sentence pointing at `GET /api/agent-tasks/pipeline` (CARD-0304) and `/summary`'s
  `byStatus` as the fleet-wide cross-check.
- `docs/antiphon-api.md:253`: append "names are case-insensitive; an unrecognised value is
  `422 validation_failed` (`errors.status`); `boardId`/`projectId` are not bound until CARD-0515".
- `.claude/skills/antiphon-orchestrator/SKILL.md` §5 first bullet: split into (a) the `boardId`
  caveat (CARD-0541/CARD-0515, real) and (b) the idiom rule: wrap `Invoke-RestMethod` in `@()` or
  parentheses before piping, or the array prints as one blank row; prefer `/pipeline` for occupancy.

No production file changes. Commit S1+S2 together once green, S3 separately.

## Acceptance table (guard granularity)

| Guard | Layer | Asserts | Red when |
|---|---|---|---|
| V-1 | S1 | Seven rows, one per status, under one root; for each value `ListAsync(root, [value], false, null)` returns exactly that row, and the returned summary's `Status` equals the seeded value | the `IN` predicate is inverted or dropped; `ToSummary` stops passing `Status` through |
| V-2 | S1 | `[Dispatched, Working, Blocked]` returns exactly those three ids; `[Working]` alone returns only the Working row | union becomes intersection, or duplicates collapse a value |
| V-3 | S1 | With `since = now-1d`: a Working row created 10 days ago is kept; a Succeeded row completed 10 days ago is trimmed; a Succeeded row completed 1 hour ago is kept | the "non-settled rows survive the window" branch is lost |
| V-4 | S1 | A `Working` row with `Role = Check` is absent with `includeChecks: false` and present with `true`; the `Role = Code` Working row is present both ways | specialist hiding leaks or over-hides |
| V-5 | S1 | `statuses: null` and `statuses: []` both return all seven rows under the root | the empty-list guard `{ Count: > 0 }` turns into "match nothing" |
| V-6 | S2 | `?status=Working`, `?status=working`, `?status=WORKING` each return the seeded Working row with JSON `"status":"Working"` | `ignoreCase` is lost or the enum serialises numerically |
| V-7 | S2 | `?status=Dispatched,Working` returns both seeded rows; `?status=Working,` (trailing comma) and `?status= Working ` (padding) return the Working row | trim/empty handling regresses |
| V-8 | S2 | `?status=Bogus` → 422, body `code == "validation_failed"`, `errors.status[0]` contains `Bogus`; `?status=Working,Bogus` → 422 with no partial result; `?status=99` → 422 | validation is skipped, softened to `[]`, or `IsDefined` is dropped |
| V-9 | S2 | `?status=Working&includeChecks=false` omits a seeded `Role = Check` Working row; `includeChecks=true` includes it | default-hiding regresses over HTTP |

## Out of scope (not folded)

- CARD-0541 / CARD-0515: `boardId`/`projectId` predicates and unknown-key 400 (D-5).
- Numeric status input (D-2).
- Logging the query string in `CurrentUserMiddleware` so a future "empty list" can be replayed from
  the server log. Useful, separate card if wanted.
- The `/api/cards` example's envelope mismatch beyond the one-line rewrite in S3.

## Verification design

Stage folded in by the brief ("write acceptance guards and a regression test plan").

### Inspection

- G-1..G-5 are read from source at `26633049`; G-6 from `git show 0c99f7e6:` on the three files;
  G-7 and G-10 are live probes against `http://localhost:17202` (server `6ab13481`) recorded in the
  plan task; G-9 is the reporting session's transcript, filtered to 2026-09-16 tool calls whose
  input contains `agent-tasks?` and `status=`.
- Reviewer check: run the bare and parenthesised `Invoke-RestMethod` forms against
  `?status=Dispatched` once; the bare form must print one blank row while curl shows rows. That
  reproduces the card in under a minute and is the fact the doc note states.

### Delivery inventory

Two new test files (S1, S2), three document edits (S3), no production change, no migration, no
config. Nothing to restart; `docs/ops-http.md` and the skill are read by orchestrators at dispatch
time, so the note is live at merge.

### Proves it works now

`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c546/ -- --treenode-filter "/*/*/AgentTaskListStatusFilterTests/*"`
then the same with `AgentTaskListEndpointTests`. Expected: 9 guards green (V-1..V-9), each in
seconds; both classes are `Integration` and need the testcontainer Postgres. Delete `bin-c546`
directories afterwards. Do not run the namespace-wide filter for this card (testing guide, CARD-0307).

### Guards the regression

The guard table above is the inventory. Each guard names the single line whose mutation turns it
red, so the positive controls are one-line edits.

### Positive controls (for Mutation)

| PC | Mutation (restore after) | Expected red |
|---|---|---|
| PC-1 | `AgentTaskService.ListAsync`: `requested.Contains(t.Status)` → `!requested.Contains(t.Status)` | V-1, V-2, V-6 |
| PC-2 | same method: delete the `if (statuses is { Count: > 0 })` block | V-1, V-2 (all seven rows come back) |
| PC-3 | same method: drop the `\|\| (t.Status != Succeeded && ...)` branch of the `since` predicate | V-3 (old Working row trimmed) |
| PC-4 | same method: `if (!includeChecks)` → `if (includeChecks)` | V-4, V-9 |
| PC-5 | `ToSummary`: pass `AgentTaskStatus.Dispatched` instead of `task.Status` | V-1 (status assertion), V-6 |
| PC-6 | `ParseStatuses`: `ignoreCase: true` → `false` | V-6 (`working`, `WORKING` → 422) |
| PC-7 | `ParseStatuses`: replace `throw new ValidationException(...)` with `continue` | V-8 (200 `[]` instead of 422) |
| PC-8 | `ParseStatuses`: remove `\|\| !Enum.IsDefined(status)` | V-8 (`?status=99` → 200) |
| PC-9 | `ParseStatuses`: drop `StringSplitOptions.TrimEntries \| RemoveEmptyEntries` | V-7 (trailing comma / padding → 422) |

Run each PC with the method-scoped filter for the guard named, red then green, per the mutation
runner rules in the testing guide. Nine controls, roughly 15-25 minutes total including builds.

### Cost

Code: about 250 lines of tests, 30 lines of docs. Verification: two class-scoped runs, seconds
each after the build. Mutation: nine one-line controls.

--- next stage ---
next: code
handoff: CARD-0546 plan (docs/superpowers/plans/2026-09-18-card-0546-agent-task-status-filter-plan.md): no server change; add AgentTaskListStatusFilterTests (V-1..V-5) and AgentTaskListEndpointTests (V-6..V-9) per the acceptance table, then S3 doc/skill edits for the Invoke-RestMethod @() idiom and /pipeline occupancy read; run only the two class filters; do not touch CARD-0541/0515 scope.
artifact: docs/superpowers/plans/2026-09-18-card-0546-agent-task-status-filter-plan.md
