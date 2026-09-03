# CARD-0332 — Role × complexity matrix: `ComplexityChains` keyed by (Role?, Complexity), whole-row fallback to the any-role chain, nothing else moves

**Date:** 2026-09-03 (Plan pass, task 23d64095 — design only; no production code changed)
**Card:** CARD-0332 "Expand CARD-0090's ComplexityChains to a (role x complexity) matrix - separate Plan/Coding x Complex/Medium/Simple model chains"
**Builds on (shipped, verified in code this pass):** CARD-0090 S1–S4 (`2eabe706`, `11e7bf6c`, `2c246c0a`, `5f73b9ee` — all on master): `ComplexityChain` entity + `ComplexityChains` table, `RoutingCandidates.Compose`, `ComplexityRoutingService.WalkAsync` / `WalkCandidatesAsync` / `LoadChainAsync` / `FindActiveAsync`, `ComplexityChainService`, `/api/complexity-chains`, `scripts/complexity-chain.ps1`, `delegate.ps1 -Complexity` / `-RefuseIfExhausted` / `-Reroute`, dispatcher re-walk + `ResumeRoutingBlockedAsync`, `AttentionKind.RoutingExhausted`, `POST /api/agent-tasks/{id}/reroute`, `ComplexityChainPanel`. CARD-0090 S5 (reactive reroute on a wall) is **not** built; nothing here depends on it.
**Coordinates with:** CARD-0322 (pin candidate lists — plan landed, not built; §D5 confirms no conflict), CARD-0333 (settings UI — §"What CARD-0333 can rely on"), CARD-0352 (Diagnose seat — S1–S3 landed, the `complexity:*` label sweep S4 not yet; §S4 here is the consumer it hands to this card), CARD-0097 (RolePolicy visibility; unchanged by this card).

---

## Read this first — what is true today

| Card says | True today (2026-09-03, checked) |
|---|---|
| "CARD-0090 is already planned (Review column, plan landed, ~4.5–5 days scoped across S1–S5)" | **S1–S4 are built and on master.** The table, walker, endpoints, script, dispatcher re-walk, attention row, reroute and client panel all exist. This card is a keying change to shipped code, not a plan-time fork. |
| Q4 "if CARD-0090's own chains have real operator-set rows by the time this ships" | Live DB: **0 rows** in `ComplexityChains` (none active, none cleared); **0 of 1 300** `AgentTasks` carry `Complexity`. Nobody has used `-Complexity` yet. The migration below still handles a populated table (a CARD-0090 row becomes the any-role row with no data edit), and a test proves it. |
| "Plan/Coding × Complex/Medium/Simple" | The shipped axis is `TaskComplexity { Hard, Medium, Easy }` and CARD-0352 already fixed the card-label vocabulary to `complexity:hard\|medium\|easy` to match it. **No rename, no aliases**: Complex = Hard, Simple = Easy, Coding = `AgentTaskRole.Code`. Two vocabularies for one axis is the thing CARD-0352's plan refused. |
| CARD-0322 "reusable-walker shape (`RoutingCandidates.Compose` / `WalkCandidatesAsync`)" | Built exactly as CARD-0322 asked (`RoutingCandidates.cs`, `ComplexityRoutingService.WalkCandidatesAsync`). `WalkAsync` already takes `AgentTaskRole role`; the matrix keying changes only what `LoadChainAsync` reads and the chain label string. Neither `Compose` nor `WalkCandidatesAsync` changes signature. |
| — | One live routing pin: stage-wide **Code, Human, Required, Grok (kind only)**. While it stands, every `-Role Code -Complexity …` create resolves the pin's single pair and the Code row of the matrix is bypassed (CARD-0090 §5, unchanged). Execution notes say what to do about it. |

---

## Decisions

### D1 (card Q1). Cell set: any routable role may own a cell; none is forced to; the any-role chain is the fallback row

The matrix is **sparse**. A cell is `(Role, Complexity)` where `Role` is one of the eleven roles `delegate.ps1 -Role` accepts — `Plan, Code, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge, Custom` — or **null = any role**, which is CARD-0090's row unchanged. The user named Plan and Code; those are the cells the operator writes on day one. Every other routable role gets the same three-way split *available*, not *required*: a Debug/Hard cell is one `PUT` away, and until it exists Debug/Hard reads the any-role Hard chain.

**Refused (422) as a cell role:** `Check`, `Distill`, `Diagnose`. They are seat-pinned specialist roles (`AgentId` set at create, kind settled by the standing agent); a chain never routes them. Same carve-out `RoutingPinService` makes for pins ("furniture, not a card stage").

Why not a fixed 2×3 (Plan/Code only) with everything else on the any-role row: it is the same table with a validation rule that would be deleted the first time the operator wants "Debug: grok first". Why not a forced 11×3: CARD-0090's own reason ("config nobody maintains") and the operator's no-guessed-seed decision — a matrix that must be full before it works is a matrix nobody fills. Sparse with a fallback row is the shape `RoutingPins` already has (card grain over stage grain).

### D2 (card Q2). Same table, nullable `Role` column; one active row per (Role?, Complexity)

`ComplexityChains` gains `Role` (`AgentTaskRole?`, integer, null = any role). Nothing else on the row changes: `CandidatesJson`, `Provenance` (`RoutingPinProvenance`), `Reason`, `NotAfter` lazy expiry, `SourceTaskId`, `ClearedAt`. The Human-overwrite rule, the 1..8 / delegatable-kind / no-duplicate validation, the 400-char reason cap and the lazy self-clear are reused **by construction** — they are the same methods reading one more key column.

Not a new table: a second table would duplicate `ComplexityChainService` (validation, overwrite rule, expiry) and give the walker two readers for one grain. Not a JSON blob of the whole matrix on one row: the per-cell provenance/expiry the card asks to reuse is per-row state. `Role == null` for "any" follows `RoutingPin.CardId == null` for "stage-wide" — the codebase's existing spelling of an unscoped row.

**Uniqueness.** Postgres treats NULLs as distinct in a unique index, so the shipped `IX_ComplexityChains_Complexity_Active` cannot simply grow a column. Replace it with `IX_ComplexityChains_Role_Complexity_Active` on `(Role, Complexity)`, unique, **`NULLS NOT DISTINCT`** (PostgreSQL 16.14 live; Npgsql EF annotation `Npgsql:NullsDistinct = false`), filter `"ClearedAt" IS NULL`. If the annotation misbehaves in the hand-written migration, the fallback is two partial unique indexes (`WHERE "ClearedAt" IS NULL AND "Role" IS NULL` on `(Complexity)`; `… AND "Role" IS NOT NULL` on `(Role, Complexity)`); either satisfies the tests.

### D3 (card Q3). Empty-cell fallback: the any-role chain as a whole row, then config, then Blocked — never RolePolicy, never another tier, never a merge

Resolution for a walk on `(role, complexity)`, first hit wins, **whole row**:

1. active row `(role, complexity)` — the cell;
2. active row `(null, complexity)` — the any-role chain (CARD-0090's row);
3. `DelegationSettings.ComplexityChains[complexity]` — config default (role-agnostic; ships empty);
4. nothing → the walk has zero candidates → `Chosen == null` → **Blocked-for-routing** (200, task Blocked) or 409 `routing_exhausted` with `refuseIfExhausted` — CARD-0090's existing empty-chain terminal, with a sentence that names every place it looked:
   `routing exhausted: Plan/Hard chain is empty (no Plan/Hard row, no any-role Hard row, no config default). Set one with complexity-chain.ps1 set -Role Plan -Complexity Hard, or set -Complexity Hard for every role.`

Explicitly **not** done, and why:

- **No fall-through to `RolePolicy`** ("hardcoded safe default"). CARD-0090 §1: a chain replaces role resolution for that dispatch, and RolePolicy is a single answer, not a chain. A caller who passed `-Complexity` opted into chain routing; a missing chain is a configuration gap a human fills once, and Blocked is how the gap is seen. Silently running the role default would be the guess the requester forbade, and it would hide the gap indefinitely.
- **No cross-tier fallback** (Plan/Hard → Plan/Medium, or Plan/Hard → any-role Medium). CARD-0090 "What this card does not do", unchanged.
- **No concatenation** (cell candidates, then the any-role row's). A cell that exists is the operator's whole answer for that role, same as a card pin beats a stage pin as a whole row (CARD-0305) and lists never concatenate across grains (CARD-0322 D5). An operator who wants "Plan/Hard = fable, then whatever any-role Hard says" writes the full list in the cell.
- **No role dimension in `DelegationSettings.ComplexityChains`.** Config is the restart-cadence, provenance-less fallback (CARD-0334's propagation table confirms settings need a server restart); it ships empty by operator decision; DB rows are the write path. A role-keyed config section would be a second matrix to keep in step.

So: until an operator writes a role cell, **every `-Complexity` create behaves byte-for-byte as CARD-0090 shipped it.**

### D4 (card Q4). Backward compatibility: a CARD-0090 row *is* an any-role row

- Migration `AddComplexityChainRole`: `AddColumn Role integer NULL` — every existing row reads as `Role = NULL` = any role, which is exactly what it meant yesterday. No `UPDATE`. Drop the old index, create the new one. `Down`: clear (not delete — history stays readable) every active row with `Role IS NOT NULL`, drop the new index, recreate the old one, drop the column.
- `GET /api/complexity-chains` keeps returning `chains[]` with the three any-role entries first, in Hard/Medium/Easy order, each now carrying `role: null`; role cells are appended after them. A consumer that only reads the first three (the shipped panel test, `List_always_returns_three_tiers`) sees what it saw. Every entry gains fields; none loses one.
- `PUT|DELETE /api/complexity-chains/{complexity}` stay as the any-role writer (alias of `/any/{complexity}`). `complexity-chain.ps1 set -Complexity Hard …` with no `-Role` writes the any-role row, as today.
- `WalkAsync(complexity, taskKind, role, pin, cardId, owner, ignoreQuota, ct)` — signature unchanged; it already receives `role`. Every caller (`AgentTaskService.CreateAsync`, `AgentTaskDispatcher.WalkTaskChainAsync`) compiles and behaves identically when no role cell exists. `Walk.Source` stays `chain:Hard` when the any-role row answered, so `Walk_picks_the_first_survivor_and_records_held_skips` and the attention title `Hard chain exhausted` stay green **unedited**.
- Live data today: nothing to migrate (0 rows, 0 chain tasks). The compat test seeds a CARD-0090-shape row (`Role` null) and proves a Plan/Hard walk reads it.

### D5. CARD-0322 conflict check: none; the walker contract is untouched

CARD-0322 asks for `Compose(pin, chain?, chainLabel, requestKind, requestLevel, resolve)` + `WalkCandidatesAsync(list, ctx)` and lists the composition rows (Required pin wins outright; Preferred pin prepends, then the chain, deduped, no role-policy append when a chain is present; card list beats stage list). This card changes:

- `LoadChainAsync(TaskComplexity)` → `LoadChainAsync(AgentTaskRole role, TaskComplexity complexity)` (D3's order) returning, in addition, `ChainRole` (`AgentTaskRole?` — which cell answered; null = any-role or config).
- the `chainLabel` passed to `Compose`: `"Hard"` when the any-role row or config answered (unchanged), `"Plan/Hard"` when a role cell answered. `Source` therefore reads `chain:Hard` or `chain:Plan/Hard`. In `SourceOf`'s `pin+chain:` composite the chain tail drops the role when it equals the pin's role (it always does — both key on the task's role), so CARD-0322's example `pin+chain:CARD-0301 Plan/Hard` reads the same whichever row answered.

`Compose`, `WalkCandidatesAsync`, `WalkContext`, the four filters and their order, `RoutingCandidates.Candidate`/`Origin`, `PinSource`, the Required/Preferred rows, `RoutingPinId` widening (CARD-0322 D6), the `Rerouted` event and the reroute endpoint — none change. CARD-0322's attention grouping "per list source" composes with D7 below (chain-blocked tasks group per governing cell; pin-blocked per pin source).

**Ordering.** This card is small and edits the service CARD-0322 only *calls*; land it first. If CARD-0322 lands first anyway, this card's diff is the same two hunks plus the `RoutingPinId` guard it inherits.

### D6. Provenance across cells: an Auto write may not shadow a Human any-role row

Per cell the rule is CARD-0090's: Auto never replaces Human (409 `complexity_chain_human`). One new clause, because a role cell **outranks** the any-role row: **an Auto `PUT` to `(role, complexity)` is refused 409 `complexity_chain_human` when the any-role `(complexity)` row is Human** — writing Plan/Hard as Auto would silently route Plan off a list the operator wrote by hand, which is what the provenance rule exists to prevent. A Human write to a cell is always allowed (same hand). An Auto write to the any-role row never touches existing Human cells (they outrank it anyway). The 409 text names the row that blocked it and says "write it as Human, or clear the any-role row".

### D7. Attention: one `RoutingExhausted` row per *governing cell*

CARD-0090 groups per complexity. With a matrix, three blocked Plan/Hard tasks and two Code/Hard tasks are one exhaustion if both read the any-role Hard row, two if Plan has its own cell. So the grouping key is the cell that governed the walk: `(role cell exists ? role : null, complexity)`, recomputed from the current rows when the row is built (`FindActiveAsync` per distinct `(Role, Complexity)` among routing-blocked tasks — a handful of reads, only when such tasks exist). Titles: `Hard chain exhausted` (any-role — byte-identical to today) or `Plan/Hard chain exhausted`. Headline, evidence (`{shortId} {card} {role}`), actions, carve-out from `BuildBlockedAsync`, disappearance on resume — unchanged. No new column on `AgentTask`: `Complexity` still marks "chain-governed", and re-walks key on `(task.Role, task.Complexity)` against the rows *now*, which is what a changed instruction should do.

### D8. Vocabulary and the URL/script spelling of "any role"

Wire: `role: null`. URL: segment `any` (`/api/complexity-chains/any/Hard`; the two-segment form stays as an alias). Script: `-Role Any` or omit `-Role`. Prose and sentences: "any-role Hard chain". Source string: `chain:Hard` (unchanged). One word everywhere; the DTO never invents a pseudo-role enum member.

---

## Ground truth (checked in code, not from the cards)

- `ComplexityChain` (`server/Domain/Entities/ComplexityChain.cs`): `Id, Complexity, CandidatesJson(1000), Provenance, Reason(400), NotAfter, SourceTaskId, CreatedAt, UpdatedAt, ClearedAt`; `ParseCandidates`/`SerializeCandidates` with `JsonStringEnumConverter`. DbContext config at `AppDbContext.cs:1658-1673`; filtered unique index `IX_ComplexityChains_Complexity_Active`. Latest migration stamp `20260903230000_AddAgentTaskRepliedAt`; hand-written with `[DbContext]`/`[Migration]` attributes in the file and the snapshot edited by hand (daemons lock `bin/`) — CARD-0090's own migration is the template.
- `ComplexityRoutingService` (`server/Application/Services/ComplexityRoutingService.cs`): `WalkAsync(complexity, taskKind, role, pin, cardId, owner, ignoreQuota, ct)` = `LoadChainAsync(complexity)` → `RoutingCandidates.Compose(pin, chain, complexity.ToString(), null, null, resolve)` → `WalkCandidatesAsync`. `Walk(Complexity, ChainProvenance, ChainSource "pin"|"config", Source, Outcomes, Chosen, Available, Walked)` with `ExhaustedSentence()` / `SkippedWarning()` / `ToDto()`. `FindActiveAsync(complexity)` does the lazy `NotAfter` clear. `EvaluateAvailabilityNowAsync` feeds the GET's `availableNow`.
- `RoutingCandidates.Compose` (`RoutingCandidates.cs`): `chainLabel` is only used to build `Source` (`chain:{label}` / `pin+chain:{pinTail}/{label}`). Nothing else reads it.
- `ComplexityChainService`: `ListAsync` iterates Hard/Medium/Easy; `GetAsync(complexity)` → row or config; `UpsertAsync` (Human-overwrite 409, `NotAfter` in the past 422, `ValidateCandidates`); `ClearAsync` idempotent.
- `ComplexityChainEndpoints`: `GET /`, `PUT /{complexity}`, `DELETE /{complexity}`; `ParseComplexity` 422 text "Use Hard, Medium, or Easy."; PUT resolves the polling caller for `SourceTaskId`.
- `AgentTaskService.CreateAsync` (`:445-540`): the chain branch calls `WalkAsync` with `request.Role`; the Required-single-candidate 409 arm, the `RefuseIfExhausted` arm and the Blocked insert all read `routingWalk` fields that stay. `FormatComplexityCreatedDetail` (`:1817`) prints ` complexity={walk.Complexity} candidate i/n alias`. `RerouteAsync` (`:1255`) nulls `Complexity`; unchanged.
- `AgentTaskDispatcher` (`:640-750`): `BlockQueuedChainIfExhaustedAsync`, `ResumeRoutingBlockedAsync` (detail `capacity returned: requeued on {alias} ({task.Complexity} chain i/n)`), `WalkTaskChainAsync` (passes `task.Role`), `PinForbidsReroute`. Only the detail string changes.
- `AttentionService.BuildRoutingExhaustedItemsAsync` (`:340-400`): groups `blocked.GroupBy(t => t.Complexity)`, title `$"{complexity} chain exhausted"`. `AgentTaskPipelineStatusService:401` and `BlockedContextBuilder:62` classify by the `routing exhausted: ` prefix — unchanged.
- `AgentTaskRole` (`AgentTaskEnums.cs:26`): `Custom=0 … Merge=10, Check=11, Distill=12, Diagnose=13`. `delegate.ps1 -Role` `ValidateSet` is the eleven routable ones, default `Custom`. `RoutingPinService` refuses Check/Distill/Diagnose pins (`:526`).
- `RoutingPinStrength { Preferred=0, Required=1 }`; live active pin (2026-09-03): `Role=Code, CardId=null, Required, Human, AgentKind=Grok, ModelLevel=null`.
- Client: `client/src/api/complexityChains.ts` (`ComplexityChainDto`, `useComplexityChains`, 15 s refetch), `ComplexityChainPanel.tsx` (maps `chains`, keys by `chain.complexity`), `ComplexityChainPanel.test.tsx` (three-empty sentence; Hard with two candidates). `TaskDetailBody.tsx` renders the task's `complexity` chip — unchanged.
- Docs naming chains: `docs/antiphon-api.md:281-312`, `docs/orchestration-loop.md:159-166`, `.claude/skills/antiphon-delegate/SKILL.md:76`, `scripts/complexity-chain.ps1` header.
- Tests today: `ComplexityRoutingWalkTests` (8), `ComplexityRoutingComposeTests` (8), `ComplexityChainServiceTests` (9), `ComplexityChainHttpTests` (2), `ComplexityCreateTests` (11), `ComplexityDispatcherTests` (7), `ComplexityAttentionTests` (3), `Scripts/ComplexityChainScriptTests` (5). Seeding helper `SeedChainAsync(db, complexity, pairs…)` gains an optional `role` argument; every existing call keeps meaning any-role.
- CARD-0352: `Diagnose` role and `Diagnoses` ledger landed (S3, `9bd7e2e4`); the card-label sweep (S4) and `CardDiagnosisLabels` helper are **not** in the tree; 0 cards carry a `complexity:` label. Its plan hands "set `AgentTask.Complexity` from the label" to this card (§S4 below) and asks that label trust be checked (`/api/diagnoses/stats`) before routing flips onto it.

---

## Entities and wire

### `ComplexityChain.Role` (`AgentTaskRole?`, new nullable column)

Null = any role. DbContext: `entity.Property(c => c.Role)` (nullable), index per D2. Entity doc-comment updated: "One active row per (Role?, Complexity); a role cell outranks the any-role row as a whole; config fills a complexity with neither."

### `ComplexityRoutingService`

```
LoadChainAsync(AgentTaskRole role, TaskComplexity complexity, ct)
  → (Candidates, Provenance, ChainSource "pin"|"config", ChainRole: AgentTaskRole?)   // D3 order; ChainRole null for any-role/config
FindActiveAsync(AgentTaskRole? role, TaskComplexity complexity, ct)                    // role-aware; lazy NotAfter clear as today
Walk gains: AgentTaskRole Role (the task's), AgentTaskRole? ChainRole (which cell answered)
Walk.CellLabel  => ChainRole is { } r ? $"{r}/{Complexity}" : $"{Complexity}"          // "Plan/Hard" or "Hard"
Walk.ExhaustedSentence(): "routing exhausted: Plan/Hard chain — fable held …" (cell answered)
                          "routing exhausted: Hard chain — …" (any-role answered; byte-identical to today)
                          empty: the D3 sentence naming Plan/Hard, any-role Hard, config
WalkAsync: unchanged signature; passes role into LoadChainAsync; chainLabel = ChainRole is null ? complexity.ToString() : $"{role}/{complexity}"
EvaluateAvailabilityNowAsync: unchanged
static RoutableRoles = [Plan, Code, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge, Custom]   // the cell whitelist; also served on GET
```

`RoutingCandidates.SourceOf`: when composing `pin+chain:`, strip a leading `{pinRole}/` from the chain label (D5).

### `ComplexityChainService`

```
ListAsync(role?: AgentTaskRole?, ct)
  role null  → chains = [any-role Hard, Medium, Easy (row or config, as today)] + every active role cell (ordered by RoutableRoles order, then complexity)
  role given → chains = [effective Plan/Hard, Plan/Medium, Plan/Easy] with resolvedFrom per D3
GetAsync(role?, complexity, ct)         // the row itself: role cell, or any-role row/config when role is null
GetEffectiveAsync(role, complexity, ct) // D3 resolution + resolvedFrom
UpsertAsync(role?, complexity, request, sourceTaskId, ct)   // 422 on Check/Distill/Diagnose; D6 shadow rule; rest unchanged
ClearAsync(role?, complexity, ct)                           // idempotent, per cell
```

### DTOs (`ComplexityChainDtos.cs`) — additive

```
ComplexityChainDto  += role: AgentTaskRole? (null = any role)
                    += resolvedFrom: "role" | "any" | "config" | "none"
                       // list view: "role" for a cell row, "any"/"config"/"none" for the three any-role entries
                       // ?role= view: where the effective chain came from
ComplexityChainListDto += roles: AgentTaskRole[]   (RoutableRoles — what CARD-0333 renders as grid rows), complexities: ["Hard","Medium","Easy"]
ComplexityRoutingDto   += role: AgentTaskRole, chainRole: AgentTaskRole?   (Source already reads chain:Plan/Hard when a cell answered)
PutComplexityChainRequest: unchanged (role and complexity are route segments)
```

`source` (`"pin"|"config"`) is kept as-is for the shipped client and script.

### HTTP

```
GET    /api/complexity-chains                       any-role ×3 first, then every active role cell; + roles[], complexities[]
GET    /api/complexity-chains?role=Plan             the three EFFECTIVE Plan cells, each with resolvedFrom
PUT    /api/complexity-chains/{role}/{complexity}   upsert the cell; role = any|Plan|Code|…; body unchanged
DELETE /api/complexity-chains/{role}/{complexity}   clear the cell (204 if already clear)
PUT    /api/complexity-chains/{complexity}          alias of /any/{complexity}   (unchanged)
DELETE /api/complexity-chains/{complexity}          alias of /any/{complexity}   (unchanged)
```

422: unknown role; Check/Distill/Diagnose ("seat-pinned roles are not routed by chains"); the existing candidate/notAfter/complexity 422s. 409 `complexity_chain_human`: Auto over Human in the same cell (existing) **or** Auto cell write under a Human any-role row (D6). Route parsing: a segment that parses as `TaskComplexity` on the two-segment form is the alias; `any` is case-insensitive.

### `scripts/complexity-chain.ps1` (ASCII-only, same verbs)

```
complexity-chain.ps1 get   [-Role Plan] [-Json]
    get             prints any-role rows then cells:   Hard        pin/Human   fable -> opus -> grok-4.6
                                                       Plan/Hard   pin/Human   fable -> sol
    get -Role Plan  prints the three effective Plan cells, marking inheritance:
                                                       Plan/Hard   (own)       fable -> sol
                                                       Plan/Medium (via any)   grok-4.6 -> terra
                                                       Plan/Easy   (empty)     (empty - Blocked until set)
complexity-chain.ps1 set   [-Role Plan|Any] -Complexity Hard -Candidates ClaudeCode/Frontier,Codex/Frontier [-Provenance Human|Auto] [-Reason r] [-NotAfter iso]
complexity-chain.ps1 clear [-Role Plan|Any] -Complexity Hard
```

`-Role` `ValidateSet` = the eleven routable roles plus `Any`; omitted = `Any`. Header comment gains the D3 fallback order in one sentence.

### `scripts/delegate.ps1`

No new parameter for S1–S3 (the role is already on the create). Output line after a chain create becomes `routed Plan/Hard -> grok-4.6 (candidate 2/3; via any-role Hard chain); skipped fable (held …)` — the cell label and, when the any-role row answered, the `via` clause, read from `created.routing.chainRole`/`role`. `-Pin` consequence line names the cell: `(this removes CARD-x Plan from Plan/Hard-chain fallback; clear the pin to restore it)`. Blocked line unchanged (the sentence already names the cell).

### Events and text

- Created event detail: ` complexity=Hard chain=Plan/Hard candidate 2/3 gpt-5.6-sol; skipped fable (…)` (`chain=` is the cell that answered: `Plan/Hard`, `Hard` for any-role, `config` for a config default).
- Dispatcher `Rerouted` details: `capacity returned: requeued on opus (Plan/Hard chain 2/3)` / `fable held → opus (Hard chain 2/3) at dispatch` — `walk.CellLabel` in place of `task.Complexity`.
- Attention title/grouping per D7.
- `delegate.ps1` and the parent Blocked note already carry the `FailureReason` sentence, which now names the cell.

---

## What CARD-0333 can rely on (the contract this card ships)

- **Grid axes:** `GET /api/complexity-chains` → `roles[]` (eleven, in `RoutableRoles` order) × `complexities[]` (`Hard, Medium, Easy`). The any-role row is a twelfth "row" the UI should render, labelled "Any role" — it is the fallback the operator edits when they do not want per-role cells.
- **Per-cell effective view:** `?role=Plan` returns three entries with `resolvedFrom ∈ {role, any, config, none}` and `candidates[].availableNow`/`unavailableReason` evaluated now. "none" is the cell that will Block a `-Complexity` dispatch until set — render it as such.
- **Writes:** `PUT /api/complexity-chains/{role}/{complexity}` `{ candidates:[{agentKind, modelLevel}], provenance:"Human", reason?, notAfter? }`; `DELETE` the same path. A UI write is a **Human** write (an operator clicking is the human), so it can replace Auto rows and is never refused by D6.
- **Pins outrank cells** (CARD-0090 §5, unchanged): a Required pin on the card or stage for that role bypasses the cell; the UI should show `GET /api/routing-pins` alongside (CARD-0333's own note). Today's live stage-Code pin is the first example.
- **Propagation (CARD-0333's a/b/c question, answered from this side):** (a) every new `-Complexity` create reads the rows at create time — yes, already; (b) Queued chain tasks whose snapshot alias cannot run, and Blocked-for-routing tasks, are re-walked against the *current* rows at the next tick — yes, already (CARD-0090 S3); (c) a running session is never changed mid-flight — **no, and this card does not add it.** Non-chain Queued tasks keep their snapshot (CARD-0305 rule).
- **Not in this contract:** model enabled/disabled + reset (that is `GET /api/model-availability`, CARD-0022/0309), usage-remaining (CARD-0333's own open question), RolePolicy display (CARD-0097).

---

## Slices

### S1 — Schema, resolver, service, HTTP, script

`ComplexityChain.Role`; DbContext + hand-written migration `20260904000000_AddComplexityChainRole` + snapshot; `RoutableRoles`; `FindActiveAsync(role?, complexity)`; `LoadChainAsync(role, complexity)` with `ChainRole`; `Walk.Role`/`ChainRole`/`CellLabel`; sentences; `chainLabel` + `SourceOf` tail strip; `ComplexityChainService` role-aware `List/Get/GetEffective/Upsert/Clear` with the 422 role rule and the D6 shadow rule; endpoints (`{role}/{complexity}`, `?role=`, aliases); DTO fields; `complexity-chain.ps1 -Role`. Tests: **every existing Complexity\* test green unedited** (the seeding helper's new parameter defaults to null); compat: a null-role row seeded in the CARD-0090 shape resolves for Plan/Hard with `Source == "chain:Hard"`; a Plan/Hard cell wins over the any-role Hard row as a whole (no concatenation) with `Source == "chain:Plan/Hard"` and `ChainRole == Plan`; Code/Hard with no cell reads the any-role row (`ChainRole == null`); no cell, no any-role row, config present → config; nothing → `Chosen` null, sentence names all three; lazy `NotAfter` on a cell falls back to the any-role row on the next read; Human any-role + Auto cell PUT → 409 `complexity_chain_human`; Human cell over Human any-role allowed; Check/Distill/Diagnose → 422; unknown role → 422; `GET` list = three any-role entries first then cells with `role`/`resolvedFrom`; `GET ?role=Plan` `resolvedFrom` matrix (`role`/`any`/`config`/`none`); two-segment PUT/DELETE still write the any-role row; HTTP round-trip camelCase incl. `roles[]`; script: `set -Role Plan` PUTs `/api/complexity-chains/Plan/Hard`; no `-Role` PUTs `/api/complexity-chains/any/Hard` (the script always uses the three-segment form; the two-segment alias exists for callers written against CARD-0090); `get -Role Plan` hits `?role=Plan`; `clear -Role Plan` DELETEs `/api/complexity-chains/Plan/Hard`.

### S2 — Create / dispatch / attention wording and grouping, `delegate.ps1` output

`FormatComplexityCreatedDetail` `chain=`; dispatcher `Rerouted` details via `CellLabel`; `BuildRoutingExhaustedItemsAsync` grouping per governing cell (D7) and title; `delegate.ps1` routed line + `-Pin` consequence. Tests: create on Plan/Hard cell with head held → task on candidate 2 with Warning and Created detail naming `chain=Plan/Hard`; Code/Hard with only the any-role row → detail `chain=Hard`; all cells empty → Blocked with the D3 sentence and parent note; dispatcher: Queued Plan/Hard task, cell replaced by the operator between create and tick and snapshot alias held → walked against the new cell, `Rerouted` detail names `Plan/Hard chain`; Blocked Plan/Hard task, operator PUTs a Plan/Hard cell with an available candidate → resumed; attention: 3 Plan/Hard + 2 Code/Hard blocked, no cells → **one** row titled `Hard chain exhausted` with `5 tasks waiting`; add a Plan/Hard cell (still exhausted) → two rows (`Plan/Hard chain exhausted`, `Hard chain exhausted`); existing `Three_blocked_Hard_tasks_are_one_RoutingExhausted_row` unedited; `DelegateScriptKindTests`-shape test for the routed line.

### S3 — Client label and docs

`complexityChains.ts`: `role`, `resolvedFrom`, `roles`, `complexities`; `ComplexityChainPanel`: row label `Plan/Hard` vs `Hard (any role)`, key by `${role}-${complexity}`, empty sentence unchanged; vitest: list with one cell renders both labels. Docs: `docs/antiphon-api.md` (routes, `?role=`, `roles[]`, `resolvedFrom`, D6 409 clause), `docs/orchestration-loop.md` "Complexity chains" paragraph (the matrix, the fallback order in one sentence, "a Required stage pin still bypasses the cell"), `.claude/skills/antiphon-delegate/SKILL.md` `-Complexity` row (one clause: "walks the (role, complexity) cell, falling back to the any-role chain"), `scripts/complexity-chain.ps1` header, CARD-0090 plan execution notes get a two-line pointer ("keying: CARD-0332"). CARD-0333 builds the real grid; this panel change is the minimum that stops the shipped panel mislabelling a cell row as a duplicate "Hard".

### S4 — (optional, default OFF) card-label default for `-Complexity` — the consumer CARD-0352 handed here

`DelegationSettings.ComplexityFromCardLabel` (bool, **default false**). When true, `CreateAsync` with **no** `complexity`, **no** explicit `agentKind`/`modelLevel`, and a bound card carrying a `complexity:hard|medium|easy` label sets `request.Complexity` from the label before the existing branch runs; Warning `complexity Hard from CARD-x's label`; Created detail `complexity=Hard (card label)`. Explicit `complexity` wins; an explicit pair is left alone (never 422 a caller who asked for nothing). Off, the create path is byte-identical. Depends on CARD-0352 S4 landing the label sweep and a `CardDiagnosisLabels.Complexity(labels)` helper (not in the tree today — if it is still absent, S4 parses the `complexity:` prefix in one place and CARD-0352 S4 adopts it). Tests: flag off → unchanged; flag on + label → chain walk with the Warning; flag on + explicit `-Kind` → no default applied; flag on + no label → unchanged. **Why default off:** the operator's no-guessed-routing rule, and CARD-0352's own ask that label trust (`/api/diagnoses/stats`, human-overwrite rate) be read before routing flips onto it. **Why in this card at all:** it is the one piece that makes the matrix "apply to every orchestrator and workflow" (CARD-0333's phrasing) without every brief remembering `-Complexity`; the caller can drop it to a follow-up card at dispatch without touching S1–S3.

**Ordering:** S1 → S2 → S3 one PR; S4 a second commit on the same PR or a follow-up dispatch on this card. Land before CARD-0322 (D5).

**Estimate:** S1 1.0 d, S2 0.5 d, S3 0.5 d, S4 0.5 d → **2.0 days without S4, 2.5 with**; verification floor ~1 h per slice (the filters below, run per namespace). Design risk is low: the walker and terminal are shipped and tested; this is one key column and the strings that name it.

---

## What this card does not do

- A rename of `Hard/Medium/Easy` or wire aliases for Complex/Simple.
- A role dimension in `DelegationSettings.ComplexityChains` (config stays any-role, ships empty).
- Fall back to `RolePolicy`, to another complexity tier, or by concatenating a cell with the any-role row.
- Force any role to have a cell; seed any cell (operator decision, CARD-0090).
- Change how pins compose with chains (CARD-0090 §5 / CARD-0322 D5), the walker's filters, the Blocked terminal, auto-resume, `reroute`, or S5 reactive reroute (still unbuilt, still CARD-0090's).
- A client editor or grid (CARD-0333); RolePolicy display (CARD-0097); model usage-remaining (CARD-0333's question).
- Change the live session of a running delegate when a cell changes (CARD-0333 (c) — no).
- Route `Check`/`Distill`/`Diagnose` through chains.

---

## Test matrix and commands (per `docs/testing-and-build.md`; forward slash on `OutputPath`)

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/*/ComplexityChain*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/*/ComplexityRouting*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/*/ComplexityCreate*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/*/ComplexityDispatcher*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/*/ComplexityAttention*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/*/RoutingPin*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/*/AttentionServiceTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0332/ -- --treenode-filter "/*/Antiphon.Tests.Scripts/*/*"
pwsh -File scripts/test-client.ps1
```

Delete `bin-card0332*` after. Shared-Postgres tests seed by id; the migration compat test uses `TestDbFixture.CreateIsolatedSchemaAsync` with a CARD-0090-shape row.

| Layer | Pin |
|---|---|
| Unit | `Compose` unchanged (existing 8 green); `SourceOf` `pin+chain:` tail strip |
| Application | D3 order (cell → any → config → none), whole-row not concatenated, lazy expiry falls through, D6 409, role 422s, list/effective DTO shapes, aliases |
| Application | **every existing Complexity\* / RoutingPin\* test green unedited** |
| Application | S2 create/dispatch/attention matrix above |
| Script | `-Role` on set/get/clear; omitted = any-role; `delegate.ps1` routed line |
| Client vitest | panel labels a cell row and an any-role row distinctly; three-empty sentence unchanged |

---

## Risks

| Risk | Standing |
|---|---|
| Operator writes Plan/Code cells and forgets the any-role row; a `-Role Debug -Complexity Hard` create Blocks | By design (D3): Blocked is visible, the sentence names the fix, auto-resume picks it up the moment the row is written. `get -Role Debug` shows `(empty)` before it bites. |
| The live stage-Code Required pin silently bypasses the Code row of the matrix | CARD-0090 §5, unchanged and correct (a human pin outranks a chain). Execution notes tell the operator to clear or downgrade that pin if they want the Code cells to govern. |
| `NULLS NOT DISTINCT` annotation in a hand-written migration/snapshot | PG 16.14 supports it; two partial indexes are the documented fallback; the uniqueness test (`PUT` twice, second updates in place; two any-role rows cannot coexist) catches either shape. |
| Attention over-splits when roles share the any-role row | D7 groups per governing cell, so shared rows are one row; only a real cell makes a second one. |
| `Source` string change breaks CARD-0322's expectations | Any-role keeps `chain:Hard`; cell reads `chain:Plan/Hard`; the composite drops the duplicate role. CARD-0322's grouping is per source string and composes. |
| S4 routes on a wrong Diagnose label | Default off; explicit wins; the Warning names the label; CARD-0352's stats gate is the flip criterion. |
| Enum tails move before Code | No new enum members in this card. `RoutableRoles` is a static list — re-read `AgentTaskRole` at code time in case a routable role was added. |

---

## Execution notes

- **Day one after deploy** the operator writes the cells they named (Human, not seeded):
  ```
  complexity-chain.ps1 set -Role Plan -Complexity Hard   -Candidates ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier -Provenance Human -Reason "plan-grade: fable, opus, sol"
  complexity-chain.ps1 set -Role Plan -Complexity Medium -Candidates ClaudeCode/High,Codex/Frontier,Grok/Frontier       -Provenance Human -Reason "plan: opus, sol, grok"
  complexity-chain.ps1 set -Role Plan -Complexity Easy   -Candidates Grok/Frontier,Codex/High                           -Provenance Human -Reason "small plans on grok"
  complexity-chain.ps1 set -Role Code -Complexity Hard   -Candidates Grok/Frontier,Codex/Frontier,ClaudeCode/High       -Provenance Human -Reason "execute on grok; sol then opus"
  complexity-chain.ps1 set -Role Code -Complexity Medium -Candidates Grok/Frontier,Codex/High                           -Provenance Human -Reason "execute on grok, terra if out"
  complexity-chain.ps1 set -Role Code -Complexity Easy   -Candidates Codex/Medium,Grok/Frontier                         -Provenance Human -Reason "simple builds on luna"
  complexity-chain.ps1 set             -Complexity Hard   -Candidates ClaudeCode/Frontier,Grok/Frontier                  -Provenance Human -Reason "any-role fallback"
  ```
  (Candidates above are illustrative from the routing memory of 2026-09-01/03; the operator's current preference is the source of truth, and nothing is seeded.)
- **The live stage-Code Required pin (Grok, Human)** bypasses every Code cell. If the operator wants the Code cells to govern: `routing-pin.ps1 clear` it, or re-set it as `Preferred` (then it is candidate 0 ahead of the cell). Say this in the deploy note; it is the one surprise a Code dispatch will produce.
- Orchestrator habit is unchanged: `delegate.ps1 -Role Plan -Complexity Hard -Card CARD-x`. The role is already on the create; nothing new to pass. `-Kind`/`-Level` stay correct when the operator named the model and are never rerouted.
- `get -Role <r>` before dispatching a role for the first time answers "will this Block?" without a dispatch.
- CARD-0333: read §"What CARD-0333 can rely on"; the grid is `roles[] + Any` × `complexities[]`, cells from `?role=` per row or from the list in one call.
