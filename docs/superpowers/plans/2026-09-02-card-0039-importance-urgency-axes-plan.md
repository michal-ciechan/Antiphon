# CARD-0039 — Importance and urgency as two stored axes; `priority` becomes a derived sort key

**Date:** 2026-09-02 (Plan pass, task 15c0a80b — design only; no production code changed, no tests run)
**Card:** CARD-0039 "Cards need urgency and importance as separate axes, not one priority number"
**Supersedes:** nothing. This is the first design for the card. It touches the same surfaces as
CARD-0098 (rank within a tier), CARD-0100 (card relationships), CARD-0094/0096 (orchestrator
backlog-by-priority, batch control) and CARD-0031's *Up next* rail; §"What this card does not do"
draws those lines.

**Sources (verified this pass):** CARD-0039, CARD-0019 (Done — the edit endpoint), CARD-0004 (Done —
`docs/cards/` generation), CARD-0026/0029/0022/0031 (the card's own examples), CARD-0098, CARD-0100,
CARD-0151, CARD-0094, CARD-0096 (Backlog neighbours); `scripts/card.ps1`;
`server/Domain/Entities/{Card,CardRevision}.cs`, `server/Application/Dtos/{BoardDtos,HomeTaskDtos,
AgentDtos}.cs`, `server/Application/Services/{CardService,BoardService,OrchestratorService,
HomeTaskService,CardTaskFileRenderer,CardRevisionLog,ExternalTrackerSyncService,
TrackerBidirectionalSyncService,TrackerSyncMarkers,WorkflowDefinitionLoader,AgentService}.cs`,
`server/Infrastructure/IssueTrackers/{GitHubIssuesTracker,JiraTracker,LinearTracker}.cs`,
`server/Infrastructure/Data/AppDbContext.cs`, `server/Program.cs` (enum JSON policy),
`client/src/api/{boards,homeTasks,agents}.ts`, `client/src/features/board/{boardShapeModel,
boardVisuals}.ts`, `{BoardPage,CardRow,BoardCard,CardModal,CardEditModal,CardHistory,
CardListSection}.tsx`, `client/src/features/agents/AgentAddWorkModal.tsx`, the tests named in the
test matrix, `docs/antiphon-api.md`, `docs/orchestration-loop.md`, `docs/workflow-tracker-block.md`,
`docs/bootstrap.md` (migrations), and the live board at 17202 (`GET /api/cards?boardId=` for the
Antiphon board, 318 cards) on 2026-09-02.

---

## Verdict up front

**Two stored axes, names not numbers, and `priority` leaves the API.** A card gets `importance`
(`Low | Normal | High | Critical`, default `Normal`) and `urgency` (`Normal | Soon | Now`, default
`Normal`) plus an optional `dueAt`. Everything else is derived at read time from those three:
`effectiveUrgency` (the stored urgency escalated by a near or passed `dueAt`), `quadrant` (the
Eisenhower cell) and `rank` (one integer, lower sorts first, the only thing sort sites read). The
stored `priority` column is renamed to `Importance` by the migration under a mapping that treats
the old numbers as the hint the card says they are; `priority` is removed from every request and
response, and a request that still sends it is a 400, not a silent no-op.

**The scoping question from the task prompt, answered from the code.** Nothing enforces a 0–3
range today:

- The server rejects only a negative value (`CardService.cs:1091` on create, `:1135` on edit).
  There is no upper bound, no clamp. Two Done cards carry `4`.
- `card.ps1` sends any `-Priority` ≥ 0 verbatim; `-1` is its "leave alone" sentinel
  (`scripts/card.ps1:85-88`, `:352`, `:378`). It validates nothing about the range.
- `docs/antiphon-api.md` never mentions the field. The "P0–P3" convention lives only in the
  client's chip list (`boardShapeModel.ts:23` `PRIORITIES = [0,1,2,3]`), its opacity ramp
  (`boardVisuals.ts:30`) and the words "P0 first" in `docs/orchestration-loop.md:83`.

**Worse than undocumented: the scale runs in two directions at once.** The client, the Home rail
and the docs read **0 as most important**; the server's own sort sites read **higher as more
important**:

| Site | Direction | Effect today |
|---|---|---|
| `boardShapeModel.ts:116-123` `orderCards` | ascending, P0 first | Board columns read P0 at the top — because the client re-sorts and its comment says the server payload "puts P3 at the top" |
| `boardVisuals.ts:39-43` badge colour | 0 = danger, 1 = warning | P0 is red |
| `HomeTaskService.cs:411` *Next* ordering | ascending | P0 first (consistent with the client) |
| `BoardService.cs:253` `ToDetailDto` | **descending** | Payload order is P3-first; the client works around it |
| `OrchestratorService.cs:527` dispatch candidates | **descending** | **Auto-dispatch picks a P3 card before a P0 card.** No client re-sort exists here — this is the live behaviour |
| `CardTaskFileRenderer.cs:169` `docs/cards/` index | **descending** | Each index group lists `p3` before `p0` |
| `GitHubIssuesTracker.cs:369-395`, `JiraTracker.cs:123-149` | **higher = more** | `critical` → 5, `high` → 4, a GitHub `p0` label → **5** |
| `LinearTracker.cs:155` | raw Linear int | Linear's 1 = urgent … 4 = low passes through unmapped, so on the same tracker scale it is inverted relative to GitHub/Jira |
| `TrackerSyncMarkers.cs:93` export label | 0 = "no priority" → no label | A P0 card exports with **no** priority label; a P3 card exports `priority:3` |

A bare integer with no named direction is what produced this; the fix is names in the API and a
single `CardRanking` function that every sort site calls.

**"Everything is P0" is mostly the default.** `CreateCardRequest.Priority = 0` (`BoardDtos.cs:143`)
and 0 is the top of the client's scale, so every card filed by an agent without an explicit rating
lands at the top. Live numbers, Antiphon board, 2026-09-02:

| priority | all cards (318) | open cards (101: Backlog 66, Review 32, InProgress 3) |
|---|---|---|
| 0 | 196 | 69 |
| 1 | 60 | 16 |
| 2 | 46 | 7 |
| 3 | 14 | 9 |
| 4 | 2 | 0 |

Two-thirds of the open board is at the value that means both "critical" and "nobody said". That
value is unrecoverable as data and the migration must not pretend otherwise (§Decision 8).

**Two of the card's constraints are already met; one sub-item is moot.**

- *"CARD-0019 first, or at least alongside"* — done: `PATCH /api/cards/{id}/content` is
  revision-logged with a required reason (`CardService.UpdateContentAsync`, `CardRevisionLog`).
- *"Do not add a required field without a sane default"* — both new enums default to `Normal`;
  `dueAt` is optional. A create with none of them set is exactly today's create.
- *"0 cards in In Progress and 0 in Done"* — no longer true (217 Done, 3 InProgress, 32 Review) and
  `docs/cards/` is generated from the board (CARD-0004), so columns are the record and are being
  maintained. No work here.

---

## Decision

Ten decisions, each with what it replaces and why.

### 1. Two stored fields, not a computed `priority`

`Importance` and `Urgency` are what a human actually knows about a card; a single number is a
projection of them that loses the CARD-0022/CARD-0026 distinction the card opens with. The
alternative — keep `priority` stored and derive a quadrant from labels or age — keeps the lie in
the schema. Rejected.

### 2. Names in the API, integers only in the database

`Program.cs:252` already registers `JsonStringEnumConverter(allowIntegerValues: false)` for every
enum, so `"importance": "High"` is the wire shape for free and `"importance": 7` or `"Hgih"` is a
400 with no code written. The enums:

```csharp
public enum CardImportance { Low = 0, Normal = 1, High = 2, Critical = 3 }
public enum CardUrgency    { Normal = 0, Soon = 1, Now = 2 }
```

Integer order is *higher = more* in both, matching the server's existing `>` comparisons and the
tracker adapters' scale; it never appears on the wire, so the client's "0 is top" convention has
nothing left to disagree with.

### 3. Importance is a human rating and does not drift

Four levels, default `Normal`. `Critical` is reserved for "changes how everything else gets done or
is actively costing us"; the migration hands it to nobody (§8) so that on day one the word means
something.

### 4. Urgency is a human rating *plus* an optional date, and the date is what moves

The card's worry is that a stored urgency rots like `priority` did. Three of its four examples
(CARD-0029 "corrupting results now", CARD-0022 "nothing is broken until it happens", CARD-0026
"every day it stays red…") are judgments about the *cost of delay*, not derivable from any field
Antiphon holds; age is explicitly not a proxy (an old idea is not an urgent one — aging schemes are
the classic way to make a backlog lie). What *is* real and derivable is a by-date. So:

- `Urgency` stored, default `Normal`.
- `DueAt` optional. `effectiveUrgency = max(Urgency, urgencyImpliedBy(DueAt, now))` where a date
  within **14 days** implies `Soon` and within **3 days** (or passed) implies `Now`. Both constants
  live in `CardRanking`, not configuration.
- **No automatic decay.** An urgent card that nobody touches is not less urgent; it is stale
  evidence. Staleness is *surfaced* (an `urgentSince` timestamp on the card — set when Urgency is
  raised, cleared when lowered — that the Home rail and CARD-0031's *Up next* can render as
  "rated Now 12d ago, still Backlog"), never silently changed.

### 5. Blocking is its own signal, not urgency

Agreed with the card. A card that other cards wait on will get a `blocks N` chip when CARD-0100
lands relationships; this plan neither adds nor fakes it. `CardRanking` gets one documented
extension point (`blockedDependants`, default 0, not yet wired) so CARD-0100 can decide whether it
feeds `rank` or stays a separate column in the reading order.

### 6. The derived triple: `effectiveUrgency`, `quadrant`, `rank`

All computed in one static `CardRanking` (server, `Application/Services`) from
`(Importance, Urgency, DueAt, now)` and mirrored in one client module (`cardRanking.ts`) for
fixtures and optimistic renders; a test pins the two tables equal.

`rank = 13 − (3·importance + 2·effectiveUrgency)`, lower sorts first:

| importance \ effective urgency | Now | Soon | Normal |
|---|---|---|---|
| Critical | **0** | 2 | 4 |
| High | 3 | 5 | 7 |
| Normal | 6 | 8 | 10 |
| Low | 9 | 11 | 13 |

The weights encode `docs/orchestration-loop.md:83` ("prefer a card that changes how everything else
gets done over one more feature"): `Critical/Normal` (4) beats `Normal/Now` (6), but `Low/Now` (9)
— the standing-red test — beats the undifferentiated default `Normal/Normal` (10). Ties break by
`DueAt` (earliest first, null last) then `CreatedAt`.

`quadrant` is the cell, for the phone views and CARD-0094's grouping: important = `High` or
`Critical`; urgent = effective urgency ≠ `Normal`.

| | urgent | not urgent |
|---|---|---|
| important | `DoFirst` | `Schedule` |
| not important | `Clear` | `Someday` |

(`Clear`, not Eisenhower's "Delegate" — that word means something else in this codebase.)
`rank` is not monotone across quadrant bands (`Normal/Now` = 6 sits in `Clear`, `High/Normal` = 7 in
`Schedule`); bands answer "what kind", `rank` answers "what next", and the board shows both.

### 7. `priority` leaves the API; two named exceptions keep an alias

A field that meant three things (stored 0-is-top, stored higher-is-more, a sort key) must not
survive under the same name with a fourth. `CardDto`, `CardRevisionDto`, `HomeTaskItemDto`,
`AgentQueueCardDto` drop `Priority`; `CreateCardRequest` and `UpdateCardContentRequest` drop it and
are annotated `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` so a stale caller
sending `priority` gets a 400 instead of a silently ignored field (System.Text.Json's default is to
ignore unknown members — which is exactly how an old `card.ps1 -Priority 0` would "succeed" while
changing nothing). The only known external callers are `card.ps1` and the client, both updated here.

Exceptions, because their consumers are user data we cannot migrate:

- Workflow prompt templates: `{{ card.priority }}` and `{{ issue.priority }}`
  (`WorkflowDefinitionLoader.cs:217,226`) keep resolving, to `rank`; `card.importance`,
  `card.urgency`, `card.effective_urgency`, `card.quadrant`, `card.due_at` are added and
  `CreateDefaultContent` uses the new names.
- The tracker-managed GitHub label keeps its `priority:` prefix (`docs/workflow-tracker-block.md`
  documents it as sync-owned) but carries the importance *name*: `priority:critical`,
  `priority:high`, `priority:low`, nothing for `Normal`. `GitHubIssuesTracker.ParsePriority` already
  understands those words.

### 8. Migration: the old numbers are a hint, and the hint is recorded

Column `Cards.Priority` → `Cards.Importance` with:

| old `priority` (client scale, 0 = top) | new `Importance` | why |
|---|---|---|
| 0 | `Normal` | indistinguishable from "not rated" (the create default); 69 open cards |
| 1 | `High` | explicitly rated one below the top |
| 2 | `Normal` | explicitly rated middle |
| 3, 4 | `Low` | explicitly rated bottom |

`Urgency` = `Normal`, `DueAt` = null, `UrgentSince` = null everywhere. Nobody becomes `Critical`.

Any deliberately rated P0 among the 69 open ones lands at `Normal`, below the 16 open P1s — the
plan cannot tell which those are, and neither can the data. That is the honest cost of 0 having
been the default, and it is reversible: the
migration also inserts one `ContentEdit` revision per **open, unarchived** card (~101 rows,
`RevisionNumber = RevisionCount + 1`, `RevisionCount` bumped, all superseded fields null,
`Reason = "CARD-0039: importance derived from legacy priority N"`, `EditedBy = "migration"`), so
every card's history says what it was and the rollout slice (S6) prints the open ex-P0 list for a
hand re-rate. Done/Canceled cards get the mapping silently; their importance decides nothing.

Rejected: mapping 0 → `Critical` (85 of 101 open cards would be `High`/`Critical`; the axis would be
noise on day one and the operator would downgrade sixty cards instead of upgrading five), and
keeping a `LegacyPriority` column (a second field to forget to drop; the revision row is the
record CARD-0019 built for exactly this).

`CardRevisions.Priority` (the superseded value) is renamed and remapped by the same CASE, and
`Urgency`/`DueAt` superseded columns are added, so a post-migration content edit snapshots all
three.

### 9. What reads it — every consumer named, none left as "a field nobody looks at"

| Consumer | Today | After |
|---|---|---|
| Board column order (`BoardService.ToDetailDto`, `boardShapeModel.orderCards`) | descending, client re-sorts ascending | both by `rank` asc; the client comment explaining the workaround is deleted |
| Board bands over `BAND_THRESHOLD` (`priorityBands`, `CardListSection`) | one band per P-number | one band per `quadrant`, `DoFirst` first, `rank` within |
| Board filter chips (`PRIORITIES`, `priorityMix`, `p0Count`) | P0–P3 chips | importance chips + an **urgent** toggle (effective urgency ≠ Normal); `criticalCount` where `p0Count` fed the column signal |
| Row/card badges (`CardRow`, `BoardCard`, `CardModal`) | `P{n}` on every row | importance word for `Critical`/`High`/`Low` only, coloured; a second badge `now` (danger) / `soon` (warning) / `due 3d` when a date drives it; **nothing** for the default — 70 % of rows lose a meaningless chip |
| Orchestrator auto-dispatch (`OrchestratorService.cs:527`) | descending — P3 first | materialise, then `rank` asc, `CreatedAt` — fixes the live inversion |
| Home *Next* (`HomeTaskService.cs:411`) and CARD-0031's *Up next* | ascending P | `rank` asc; `HomeTaskItemDto` carries `importance`, `effectiveUrgency`, `quadrant`, `rank`, `urgentSince`; `TaskCard` shows the same two badges |
| `docs/cards/` front matter and index (`CardTaskFileRenderer`) | `priority: n`, index by descending P, `` `p{n}` `` | `importance:`, `urgency:`, `due:` lines; index by `rank`; `` `critical` `` / `` `now` `` bits |
| Agent queue (`AgentQueueCardDto`) | shows P | shows importance/urgency; order stays `QueuePosition` |
| Tracker import (`ExternalTrackerSyncService.cs:335`) | copies the 0–5 tracker scale into `priority` | `CardImportance.FromTrackerScale`: 5 → Critical, 4 → High, 2–3 → Normal, 1 → Low, 0 → Normal; `LinearTracker` maps its 1…4 onto the tracker scale (5,4,2,1) so all three adapters agree |
| Tracker export (`TrackerBidirectionalSyncService.cs:472,692`) | `priority:{n}` | `priority:{name}` (§7) |
| `card.ps1` | `-Priority n` | `-Importance` / `-Urgency` (ValidateSet), `-DueAt`, `-ClearDueAt`; `-Priority` kept for one release as a hard error naming the replacements; `get` prints `importance/urgency  rank n`; `-Limits` prints the allowed names |
| `GET /api/cards/limits` | four length ceilings | plus `importanceValues`, `urgencyValues` from `Enum.GetNames`, pinned equal by the existing limits test |
| `docs/orchestration-loop.md:83` "P0 first" | | "lowest `rank` first" — the formula already prefers the card that changes how everything else gets done |

### 10. One function owns the order

`CardRanking.Rank(importance, urgency, dueAt, now)` is the only place the formula exists on the
server; every sort site above calls it in memory (the dispatch candidate set, the board's loaded
cards, the Home rows and the renderer all materialise before sorting already, or are small enough
to). No persisted `Rank` column — it would go stale with the clock. One integration-flavoured test
creates a `Critical/Now` card *after* a `Low/Normal` card and asserts it comes first from all four
server sort sites, which is the regression test the current inversion never had.

---

## Data model after this card

```
Card
  Importance    int (CardImportance)  not null  default 1 (Normal)     -- renamed from Priority
  Urgency       int (CardUrgency)     not null  default 0 (Normal)
  DueAt         timestamptz           null
  UrgentSince   timestamptz           null      -- set when Urgency rises above Normal, cleared when it returns
CardRevision (superseded values on ContentEdit)
  Importance    int?                            -- renamed from Priority
  Urgency       int?
  DueAt         timestamptz?
```

`CardDto` adds `importance`, `urgency`, `dueAt`, `urgentSince`, `effectiveUrgency`, `quadrant`,
`rank`; drops `priority`. `CardRevisionDto` adds the three superseded fields; drops `priority`.
`CreateCardRequest(…, CardImportance Importance = Normal, CardUrgency Urgency = Normal,
DateTime? DueAt = null, …)`. `UpdateCardContentRequest(…, CardImportance? Importance = null,
CardUrgency? Urgency = null, DateTime? DueAt = null, bool ClearDueAt = false, …)` — null means
unchanged as today, and `ClearDueAt` is the tri-state for the date. The "at least one field"
validation names the new fields.

---

## Slices

Sequential, Shared workspace (S1–S4 all touch `CardService`/`BoardDtos`; a worktree would just
defer the conflicts). Server before client; the client bundle on 17203 must be rebuilt before any
browser check.

### S1 — Domain, ranking, migration (server only)

- `CardImportance`, `CardUrgency`, `CardQuadrant` enums in `Domain/Enums`.
- `Card`: rename `Priority` → `Importance`, add `Urgency`, `DueAt`, `UrgentSince`. `CardRevision`:
  rename `Priority` → `Importance`, add `Urgency`, `DueAt`. `AppDbContext` mappings.
- `CardRanking` static class: `EffectiveUrgency`, `Quadrant`, `Rank`, the two due-date constants,
  the tracker-scale mapping, the `blockedDependants` extension point (accepted, ignored).
- Migration `AddCardImportanceUrgency` via `dotnet ef migrations add … --project server` with the
  server stopped (`docs/bootstrap.md` §Creating EF Migrations), then hand-written `Sql(...)` for the
  CASE remap on both tables, the revision-row insert (open, unarchived cards only), and the
  `RevisionCount` bump — insert **before** remap so the reason text reads the old value.
- Tests: `CardRankingTests` (the 12-cell table, both due-date boundaries, tie-breaks, quadrant,
  tracker-scale mapping), compile-fixes in `KanbanPersistenceTests`, `ContractSnapshotTests`
  seeding, `CardTaskFileRendererTests` helper.
- Estimate: 2–3 h.

### S2 — API contract, `card.ps1`, API docs

- DTOs and requests per §Data model; `Disallow` unmapped members on the two request records;
  validation messages; `CardRevisionLog.AppendContentEdit` snapshots the three fields;
  `UpdateContentAsync` maintains `UrgentSince`; `CardLimitsDto` gains the two name lists.
- `card.ps1`: parameters, header comment, `Write-CardLine`, `-Limits`, the "Nothing to change"
  message, the `-Priority` hard error. ASCII only (PowerShell 5.1 fallback).
- `docs/antiphon-api.md` cards section: fields, defaults, the 400 for `priority`, the limits
  endpoint.
- Tests: `CardCorrectionIntegrationTests` (edit each field, superseded snapshot, `ClearDueAt`,
  `UrgentSince` set/cleared, `priority` in the body → 400 at the endpoint), limits-equality test,
  `CardCliE2ETests` for the new switches.
- Estimate: 2–3 h.

### S3 — Server consumers

- `BoardService.ToDetailDto`, `OrchestratorService` dispatch candidates (`DispatchCandidate` carries
  the three fields; sort after materialising), `HomeTaskService` (`CardRow`, `RankedItem`,
  `HomeTaskItemDto`), `CardTaskFileRenderer` (front matter, index order, index bits),
  `AgentService` queue DTO, `WorkflowDefinitionLoader` variables and default content.
- Tests: the four-site ordering test (§10), `OrchestratorServiceIntegrationTests` dispatch order,
  `HomeTaskServiceIntegrationTests` *Next* order, `CardTaskFileRendererTests` front matter/index,
  `WorkflowDefinitionLoaderTests` variables.
- Estimate: ~2 h.

### S4 — Tracker sync

- `ExternalTrackerSyncService` import via `CardImportance.FromTrackerScale`; `LinearTracker` scale
  fix; `TrackerSyncMarkers.PriorityLabel(CardImportance)`; both bidirectional-sync call sites.
- `docs/workflow-tracker-block.md`: the managed label now carries a name.
- Tests: `IssueTrackerAdapterTests` (Linear 1 → 5, GitHub `p0` → 5 → Critical),
  `OrchestratorServiceIntegrationTests:206,344` (`Importance.ShouldBe(High)` / `Critical`),
  `TrackerBidirectionalSyncTests` export label.
- Estimate: 1–2 h.

### S5 — Client

- `boards.ts`, `homeTasks.ts`, `agents.ts` DTOs; `cardRanking.ts` mirror with a table-equality test
  against a fixture exported from the server test.
- `boardShapeModel.ts`: `orderCards` by `rank` (delete the workaround comment), `quadrantBands`,
  importance filter + urgent toggle, `importanceMix`/`criticalCount`; `BoardPage` chips and the
  phone popover; `CardListSection` band headers.
- `boardVisuals.ts`: `importanceBadgeColor`, `urgencyBadge`; `CardRow`, `BoardCard`, `CardModal`
  (detail + create form: two selects and a date input, defaults `Normal`/`Normal`/empty),
  `CardEditModal` (selects, date, clear), `CardHistory` (superseded values),
  `AgentAddWorkModal` (importance select; drop the 0 default), `TaskCard` badges + "rated Now Nd
  ago" line from `urgentSince`.
- Tests: `boardShapeModel.test.ts`, `CardRow`/`BoardCard`/`CardModal`/`CardEditModal`/`CardHistory`
  tests, `TaskCard.test.tsx`, fixture updates in `HomePage`/`MobileHomePage`/`TasksSection`/
  `HomeTaskModal` tests (`priority: 1` → the new fields). `pwsh -File scripts/test-client.ps1`.
- Estimate: 4–6 h.

### S6 — Rollout, docs, close

- `dev-backup.ps1` first. Restart via `scripts/restart-apphost.ps1`; migrations apply on startup;
  confirm the log shows no `[ERR]`/`[FTL]` and the counts match the survey table above
  (`GET /api/cards?boardId=` → 0 Critical, 16 open High, 9 open Low, ~101 new revision rows).
- Print the open ex-P0 list (from the revision reasons) for the operator's hand re-rate with
  `card.ps1 edit … -Importance critical -Reason …`; this plan does not guess which ones.
- `docs/orchestration-loop.md:83`, `docs/agent-card-lifecycle.md` (a short "Importance and
  urgency" section — it owns card-state semantics), `AGENTS.md` unchanged (its card.ps1 front door
  says nothing about priority).
- Rebuild the client bundle, browser-check a Backlog column and the Home rail through the
  browser-harness lane, close the card with the verdict.
- Estimate: 1–2 h plus the operator's re-rate pass.

Total: roughly 12–18 h of agent time across six dispatches.

---

## What this card does not do

- **Manual order within a tier** (CARD-0098). `rank` has 12 distinct values and `CreatedAt` breaks
  ties; drag-to-reorder is a separate stored ordinal that CARD-0098 owns. The word "rank" here is
  the derived sort key; CARD-0098 should name its field `ordinal` or `position` to avoid the
  collision.
- **Blocked-by relationships** (CARD-0100) — §Decision 5.
- **Effort estimates and the stage-transition audit** (CARD-0151). `DueAt` is a by-date, not an
  estimate.
- **Importing due dates from trackers** (Jira `duedate`, Linear `dueDate`, GitHub milestone
  `due_on`). Natural follow-on; the field exists for it. Not here.
- **Any UI to bulk re-rate.** `card.ps1 edit` with a reason, one card at a time, is the supported
  operation; the re-rate list in S6 is a printout.
- **Board-configurable weights or due-date windows.** Two constants, one function, YAGNI until a
  second board disagrees.

## Left open, deliberately

- Whether `Clear`-quadrant work should be *dispatched* differently (a cheap fix on a small model)
  is CARD-0090/0196's routing question; this card only orders.
- `urgentSince` staleness rendering on the Home rail lands as one line in S5; whether it should also
  surface in `GET /api/attention` is for CARD-0031's owner to decide once it is visible.
- The `Disallow` annotation is deliberately narrow (two request records). Widening it to every
  request DTO is a separate hygiene card if it proves its worth here.

---

## Test matrix

| Slice | Server (TUnit, `dotnet run --project tests/Antiphon.Tests`, chunk by namespace) | Client (Vitest, `scripts/test-client.ps1`) | E2E |
|---|---|---|---|
| S1 | `CardRankingTests` (new); `KanbanPersistenceTests` | — | `ContractSnapshotTests` compile |
| S2 | `CardCorrectionIntegrationTests`; limits equality | — | `CardCliE2ETests` |
| S3 | four-site ordering (new); `OrchestratorServiceIntegrationTests`; `HomeTaskServiceIntegrationTests`; `CardTaskFileRendererTests`; `WorkflowDefinitionLoaderTests` | — | — |
| S4 | `IssueTrackerAdapterTests`; `TrackerBidirectionalSyncTests`; `TrackerSyncEndpointTests` | — | — |
| S5 | — | `boardShapeModel`, `cardRanking` (new), `CardRow`, `BoardCard`, `CardModal`, `CardEditModal`, `CardHistory`, `TaskCard`, home fixtures | `BoardE2ETests` create/open card |
| S6 | — | — | browser check on the built bundle |

Run the full `Antiphon.Tests` once after S4 (chunked), then only the touched namespaces.

## Sequencing and risks

- **The migration is one-way on `0` vs `2`** (both → `Normal`). The revision row keeps the old
  number; nothing is lost, but a naive "rollback" would need the reasons, not the column. Take
  `dev-backup.ps1` in S6 and say so in the commit.
- **`Disallow` unmapped members** turns any unknown JSON key into a 400 on those two requests. The
  known callers are covered; an unknown Windmill script would fail loudly, which is the intent.
- **`OrchestratorService` sort moves from SQL to memory.** The candidate set is already filtered
  to active-column, unowned, session-less cards — tens of rows, not thousands. If a board ever
  grows past that, a stored-only pre-sort (`Importance desc, Urgency desc`) narrows it before the
  clock-dependent pass.
- **Client and server land in different slices**; between S4 and S5 the built client is broken on
  `card.priority`. Land S5 the same day, or dispatch S1–S5 as one worktree run and merge together.
- **`docs/cards/` is untracked and generated** (CARD-0004); no slice commits it. The renderer
  change in S3 shows up on the next sync.

## Execution notes

- Build to an alternate output while the daemons hold `bin/`: `--property:OutputPath=bin-c39/`
  (forward slash), delete the `bin-c39` directories before finishing.
- Migration creation needs the server stopped (`stop-server.ps1`), then
  `dotnet ef migrations add AddCardImportanceUrgency --project server`, then restart via
  `scripts/restart-apphost.ps1`. Never hand-write the scaffold; hand-write only the `Sql(...)`
  bodies inside it.
- `card.ps1` stays ASCII.
- Sort by `rank` **ascending** everywhere. If a reviewer sees `OrderByDescending` next to
  importance in a diff, that is the bug this card exists to end.
