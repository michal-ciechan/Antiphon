# CARD-0322 — Ordered candidate lists on routing pins: a stage/card pin becomes an N-candidate list walked by CARD-0090's walker

**Date:** 2026-09-02 (Plan pass, task 084537e1 — design only; no production code changed)
**Card:** CARD-0322 "Ordered candidate lists on routing pins (stage/card): multi-candidate pins walked with the CARD-0090 chain mechanics"
**Builds on (plan landed, NOT built — checked 2026-09-02, card in Review, no open task):** CARD-0090 `docs/superpowers/plans/2026-09-02-card-0090-complexity-chains-plan.md` — `ComplexityRoutingService.WalkAsync`, the Blocked-for-routing terminal (`AttentionKind.RoutingExhausted`, Retry / `reroute` / Cancel, auto-resume when capacity returns), the `Rerouted` event, the never-reroute-an-explicit-choice rule.
**Extends (shipped, verified in code this pass):** CARD-0305 `RoutingPin` / `RoutingPinService` (`ResolveAsync`, `EnforceForbiddenAliases`, `FindActiveAsync`, `ToDto`/`ToRef`), `PUT/GET/DELETE /api/routing-pins`, `scripts/routing-pin.ps1`, `delegate.ps1 -Pin` / `-IgnoreRoutingPin`, pipeline `RoutingPinRefDto` on stage and `ready` rows, `queueReason=routingPinNotBefore`, dispatcher `NotBefore` skip; CARD-0022/0309 `ModelAvailability.IsHeldAsync`; CARD-0136 `SubscriptionQuotaGate.EvaluateAsync`; CARD-0099 Codex delegate kind.

---

## Read this first — sequencing and two operator decisions that bind this card

1. **CARD-0090 is not executed.** Its plan is the contract; none of `ComplexityRoutingService`, `TaskComplexity`, `RoutingExhausted`, `Rerouted`, `POST …/reroute` exist on master today. **Do not start this card until CARD-0090 S1–S3 are Done.** This plan writes no walker and no terminal state of its own — it feeds the walker a second list source and reuses the terminal unchanged. The one thing it asks of CARD-0090's Code pass is the walker's *shape* (§"Walker contract"); that request is recorded as an addendum at the top of the CARD-0090 plan so it is built list-in from day one rather than refactored later.
2. **No guessed defaults.** The operator decided on CARD-0090 (2026-09-02) that seeded/default chains stay EMPTY until a human sets them, because routing policy changed nine times in three days. The same applies here: **no seeded pins, no default candidate list, no implicit "…and then the role policy" appended to a Required pin.** A one-candidate pin stays exactly one candidate. The only list the system ever walks is one a human (or an Auto writer, subject to the Human-overwrite rule) wrote.
3. **Auto-resume is wanted.** Resuming a Blocked-for-routing task on a candidate the operator already listed, when capacity returns, is executing the instruction, not a new decision. It is not a violation of never-silently-reroute. This plan inherits that verbatim.

---

## What the card is for (restated in one line)

The operator's live routing rules are ROLE-shaped fallback lists in prose — "Plan → fable or opus", "Plan → Sol or Grok, not fable", "Investigate → Grok first, fable/Sol follow-up". Each is one stage pin with an ordered candidate list. This card lets `RoutingPin` carry that list, so a held head candidate falls through to the next one the operator named, and an exhausted list lands in CARD-0090's Blocked-for-routing terminal instead of a 409 handed to an agent that will guess.

---

## Decisions

### D1. Column shape: `CandidatesJson` **replaces** `AgentKind`/`ModelLevel` on `RoutingPin`; the DTO keeps `agentKind`/`modelLevel`/`modelAlias` as the **head candidate**

Two sources of truth for "the head" is the bug this plan refuses to ship: a writer that fills the list but not the columns (or the reverse) would route a Plan onto whatever the stale column said. So the entity gets one field and every reader goes through it.

| Column | Type | Notes |
|---|---|---|
| `CandidatesJson` | `character varying(1000)`, nullable | ordered `[{"agentKind":"ClaudeCode","modelLevel":"Frontier"}, {"agentKind":"Grok","modelLevel":null}, …]`. Enum **member names** on disk (same as the wire and as CARD-0090's `ComplexityChains.CandidatesJson`). Null/empty = no kind/level constraint (today's `AgentKind == null && ModelLevel == null` — a forbid-only or agent-only pin). |
| ~~`AgentKind`~~, ~~`ModelLevel`~~ | dropped | |

`RoutingPin` gains a non-mapped `IReadOnlyList<RoutingCandidate> Candidates` (parsed from the JSON, cached) and `RoutingCandidate? Head => Candidates.FirstOrDefault()`. `RoutingCandidate(AgentKind? AgentKind, AgentModelLevel? ModelLevel)` is a **partial** pair on purpose: a pin's candidate fills what the request left open, exactly as today's single pin does (`-Kind Grok` only = "Grok, level from the request or the role policy"). It is resolved to a complete pair against the request and the role policy *before* the walker sees it (§Resolution). CARD-0090's chain candidates are complete pairs and stay so; the walker only ever receives complete pairs.

Validation on write (`PutRoutingPinRequest`): 1..8 candidates; a candidate must name a kind or a level (both null → 422); every named kind ∈ `AgentTaskService.DelegatableKinds`; duplicates (same kind+level, nulls compared) → 422; `agentKind`/`modelLevel` on the request remain the **1-candidate shorthand** and are 422 when sent together with `candidates` ("either the shorthand or the list"); `agentId` (standing agent) with **more than one** candidate → 422 ("a standing agent is one program — pin the agent, or list candidates, not both"); `Check` role, `notBefore`/`notAfter`, forbidden-alias normalisation unchanged.

**Migration `AddRoutingPinCandidates`** (hand-written with the `[DbContext]`/`[Migration]` attributes in the file and the snapshot edited by hand — the CARD-0305 migration's own note explains why: running daemons lock `bin/`). One `Up`, three steps, in order:

1. `AddColumn CandidatesJson` (nullable, max 1000) and `AddColumn AgentTasks.RoutingPinId uuid null` (§D5).
2. Backfill: `UPDATE "RoutingPins" SET "CandidatesJson" = <json> WHERE "AgentKind" IS NOT NULL OR "ModelLevel" IS NOT NULL`, where `<json>` is `json_build_array(json_build_object('agentKind', CASE "AgentKind" … END, 'modelLevel', CASE "ModelLevel" … END))::text` mapping the stored ordinals to member names (`AgentModelLevel` Frontier=0 … Low=3 is confirmed; **read `AgentKind.cs` for its ordinals at code time**, do not copy them from here). Every active and cleared row is backfilled — history stays readable.
3. `DropColumn AgentKind`, `DropColumn ModelLevel`.

`Down` re-adds the two columns and backfills the **head** from the JSON (best effort; a list collapses to its first entry). Deploy order is the ordinary one: the new binary migrates at startup; an old binary against the migrated schema would fail on the missing columns, so restart through `scripts/restart-apphost.ps1`, never side-by-side.

**Wire compatibility.** `RoutingPinDto.agentKind` / `modelLevel` / `modelAlias` = the head candidate's (null when the list is empty). For every pin that exists today (all single-candidate after the backfill) the JSON returned is byte-identical to yesterday's, plus two additive fields: `candidates: [{agentKind, modelLevel, alias, availableNow, unavailableReason}]` and `candidateCount`. `RoutingPinRefDto` (the 409 `routingPin` extension and both pipeline surfaces) keeps `agentKind`/`modelLevel` as the head and gains `candidateCount` (int). So `routing-pin.ps1 get`, `delegate.ps1 -Pin` (writes `agentKind`/`modelLevel` shorthand), the client's `RoutingPinRefDto`, and every `RoutingPin*` test keep working before they are touched.

### D2. Strength with a list

| Strength | Meaning with N candidates | Exhausted (no candidate available) |
|---|---|---|
| **Required** | "one of these, in this order, never anything else." The list is the whole candidate set. | N ≥ 2 → **Blocked-for-routing** (CARD-0090 terminal, unchanged). N = 1 → **409 `model_disabled` + pin coda, exactly as today** (see D3). |
| **Preferred** | "try these first, then the request/role policy." The list, then ONE appended candidate: what today's no-pin resolution yields (request fields ?? `RolePolicy` ?? ClaudeCode / role level), deduped, tagged `origin=rolePolicy` in the walk. | Same as Required for the same N (the terminal is about "nothing available", not about strength). |

The appended role-policy candidate on a Preferred pin is **not** a guessed default: it is the shipped, provenance-less resolution that runs today when no pin exists, and "then the role policy" is the strength's own definition on the card. It is never appended to a Required list. Stage `ForbiddenAliases` still filter it (a Preferred Plan pin `[Codex/Frontier, Grok/Frontier]` with stage forbid `fable` will not fall through to fable — the walker's filter 2 removes it and the walk exhausts).

### D3. The single-candidate case is byte-identical to today; chain mechanics switch on at N ≥ 2

Define `walked = (effective candidate count, after explicit-request narrowing, ≥ 2)`.

- **Not walked** (0 or 1 candidate): today's `CreateAsync` path runs unchanged — `ResolveLevel`/`ResolveAgentKind`, `EnforceForbiddenAliases`, quota gate, `RequireAsync` with the Required-pin coda, `ignoreModelDisabled` queues, dispatcher `Held` on a held alias. Every CARD-0305 and CARD-0309 create test stays green with no edits. A one-candidate Required pin on a held alias is a **409 with the coda**, never Blocked — the card's "must keep working unchanged" clause, and CARD-0090's own table row ("Required pin naming kind/level: the pin's pair is the only candidate; held → 409 exactly as today").
- **Walked** (≥ 2): the composed list goes to CARD-0090's walker; first survivor is the task's kind/level; exhausted → Blocked-for-routing or 409 `routing_exhausted` when `refuseIfExhausted` is set; the task is stamped `RoutingPinId` (§D5) so dispatch re-walk, auto-resume and the reactive wall reroute govern it.

Why the switch is at N ≥ 2 and not "any pin": the operator opted into fallback by writing a list. A one-candidate pin is an instruction to run *that*, and its failure mode (409 + `ignoreModelDisabled` to queue until the hold clears) is the shipped contract the operator already uses.

`ignoreModelDisabled` on a walked create: the caller cannot know server-side that the pin is a list, so it is **not** 422 (unlike CARD-0090's chain + `ignoreModelDisabled`). It is simply moot: a chosen candidate needs nothing ignored; an exhausted list still goes Blocked-for-routing (auto-resume *is* "queue until capacity returns", made visible). The Warning says `ignoreModelDisabled had no effect: this pin lists N candidates and is walked`.

### D4. Explicit request fields are never changed by a walk — the walk ranges only over what the caller left open

This is the one sentence that generalises CARD-0305's per-field rule and CARD-0090's "explicit is never rerouted":

1. Filter the pin's candidates to those **compatible** with the request's explicit fields (`candidate.AgentKind is null or == request.AgentKind`, same for level).
2. If the filtered list is **empty**: Required → 409 `routing_pin_conflict` (message now lists every candidate: `… is REQUIRED and lists ClaudeCode/Frontier (fable), ClaudeCode/High (opus); this request asks for kind Codex`); Preferred → request wins, warning `Overrode preferred … pin` (today's text), single candidate, not walked.
3. If the filtered list has **one** entry → single candidate, not walked. (`-Kind ClaudeCode -Level High` against `[fable, opus]` runs opus and, if opus is held, 409s — the caller named the pair.)
4. If it has **≥ 2** entries → walked over exactly those, in pin order. (`-Kind ClaudeCode` against `[ClaudeCode/Frontier, ClaudeCode/High, Grok/Frontier]` walks fable then opus; the caller fixed the kind and left the tier open, and the operator ordered the tiers.)

`ignoreRoutingPin: true` remains one-shot and whole-row: the pin (list and all) is not applied; the request's own resolution runs as today; the pin is not cleared.

### D5. Composition with a `-Complexity` chain on the same create — CARD-0090's v1 table generalised, verified still right

CARD-0090 is unbuilt, so its plan table is the contract to check against, and its two rows generalise without change:

| Pin on this card+role | `-Complexity` on the create | Candidate list handed to the walker |
|---|---|---|
| none | Hard | the Hard chain (CARD-0090, unchanged) |
| **Required**, N candidates | Hard | **the pin's N candidates only.** The chain is not consulted; Warning event `complexity chain bypassed by the human required … pin (N candidates)`. N = 1 → single candidate → 409 on hold, exactly CARD-0090's row. |
| **Preferred**, N candidates | Hard | **pin's N candidates first (in order), then the chain's, deduped.** The role-policy candidate is **not** appended — a chain replaces role resolution for that dispatch (CARD-0090 §1). N = 1 → CARD-0090's "prepended as candidate 0" row. |
| any | Hard, with explicit `-Kind`/`-Level` | 422 (CARD-0090 rule, unchanged; the pin is irrelevant because the create is refused first) |
| card pin and stage pin both exist | any | the **card** pin's list, as a whole row; lists never concatenate across grains (CARD-0305 precedence, unchanged) |

Why "Required pin wins outright" is still right: a Required pin is an instruction for *this* card+stage; a chain is a tier-wide default that — per the operator — starts empty and is written by the same hand. When both exist, the narrower instruction is the one the operator meant for this work. Strength decides list composition; provenance decides only overwrite protection (an Auto Required pin still binds, as today; the Auto-card-pin forbid rule still applies inside the walker's filter 2).

### D6. A walked task carries `AgentTask.RoutingPinId` so the dispatcher, the resume sweep and the wall reroute know it may be re-chosen

CARD-0090 uses `AgentTask.Complexity != null` as "this task's kind/level was chosen by a list and may be re-chosen by it". A pin-walked task with no complexity needs the same marker: **`AgentTask.RoutingPinId` (guid, nullable, no FK)** — the pin whose list chose it, for audit; governance is re-resolved by grain, not by id. Set on a walked create; null on a not-walked create; set to null by `POST …/reroute` (an explicit human pick ends list governance, same as it clears `Complexity`).

Everywhere CARD-0090 tests `task.Complexity != null`, this card widens it to `task.Complexity != null || task.RoutingPinId != null` and the re-walk composes the list **from the current active pin for `(task.CardId, task.Role)`** (card grain, else stage), plus the chain when `Complexity` is set:

- **Dispatcher (CARD-0090 S3 branch):** snapshot alias held or quota-refused → re-walk → new survivor → `Rerouted` event `fable held → opus (CARD-0301 Plan pin 2/3) at dispatch`; none → Blocked-for-routing. A pin **cleared since create** → no list → the task keeps its snapshot and gets today's `Held` event (governance ended with the pin). A pin **replaced** since create → the new list is walked (the operator changed the instruction; the task could not run anyway).
- **`ResumeRoutingBlockedAsync`:** same widening; Blocked-for-routing tasks whose pin list now has a survivor are re-queued with `Rerouted` `capacity returned: … (Plan stage pin 1/2)`.
- **S5 wall reroute:** same widening; loop guard = composed list length (pin's, or pin+chain).
- **Required single-candidate pin** (not walked): unchanged — stays Queued with `Held`; a human instruction is never rerouted by a tick.

This is a **narrow, stated exception** to CARD-0305's "changing a pin never rewrites Queued tasks": it applies only to a task that was walked at create, only when its snapshot alias cannot run, and only to move it to another candidate the operator listed. A not-walked queued task keeps its snapshot exactly as today.

### D7. Attention: reuse `AttentionKind.RoutingExhausted`, group per exhausted **list source**

CARD-0090 groups one Error row per exhausted chain. The grouping key becomes the list source string the walker already records: `chain:Hard`, `pin:CARD-0301 Plan`, `pin:stage Plan`, `pin+chain:CARD-0301 Plan/Hard`. Title `Plan stage pin exhausted` / `CARD-0301 Plan pin exhausted`; headline, evidence, `[OpenDrawer, OpenCard]` actions, per-task Retry / Reroute / Cancel, `BuildBlockedAsync` carve-out, "rows disappear when the last task resumes" — all unchanged. `FailureReason` keeps CARD-0090's `routing exhausted: ` prefix; the sentence names the pin: `routing exhausted: CARD-0301 Plan pin (human, required) — fable held until … (manual); opus quota 4% (resets in 2h)`.

### D8. Never-silently-reroute, restated for lists

- An explicit `-Kind`/`-Level` field is never changed by a walk — at create, at dispatch, on a wall (D4).
- A one-candidate Required pin is never rerouted (D3; today's 409 / `Held`).
- Choosing candidate k of a multi-candidate pin because 1..k−1 are unavailable is executing the operator's ordering, not rerouting; so is resuming a Blocked task on a listed candidate when capacity returns (operator decision, CARD-0090).
- Beyond the list, never. Exhausted → Blocked for a human, who answers with Retry, `reroute`, Cancel, a cleared hold, or a new PUT.
- `delegate.ps1 -Pin` after a walked create pins the **chosen** pair as a one-candidate Human Required pin (CARD-0090's rule) and prints the consequence: `pinned CARD-0301 Plan to ClaudeCode/High (human, required) — this REPLACES the 3-candidate pin; clear it to restore the list`.

---

## Walker contract (the one ask of CARD-0090's Code pass — mirrored as an addendum in that plan)

CARD-0090's plan signs `WalkAsync(TaskComplexity complexity, …)` and builds the candidate list inside. For this card to be a list *source* and not a second walker, split it in two at build time:

```
// Pure composition — no DB, unit-tested on its own. ALL strength/complexity/explicit-narrowing rules live here.
static RoutingCandidateList RoutingCandidates.Compose(
    RoutingPinService.Decision pin,          // Pin (chosen grain) + StagePin, as shipped
    IReadOnlyList<Candidate>? chain,         // null when no -Complexity
    AgentKind? requestKind, AgentModelLevel? requestLevel,
    Func<AgentKind?, AgentModelLevel?, Candidate> resolveAgainstRolePolicy)   // ResolveAgentKind/ResolveLevel closure
  → (IReadOnlyList<Candidate> Candidates /* complete pairs, deduped */, string Source /* "chain:Hard" | "pin:CARD-0301 Plan" | … */,
     IReadOnlyList<string> Origins /* per candidate: pin|chain|rolePolicy */, bool Walked /* Count >= 2 */)

// The walk — no knowledge of where the list came from.
Task<Walk> WalkCandidatesAsync(IReadOnlyList<Candidate> candidates, WalkContext ctx, CancellationToken ct)
  WalkContext(AgentTaskKind TaskKind, AgentTaskRole Role, RoutingPin? StagePin, bool StageForbidExempt /* Human card pin */,
              Agent? SubscriptionOwner, bool IgnoreSubscriptionQuota, string Source)

// CARD-0090's WalkAsync(complexity, …) becomes Compose(...) + WalkCandidatesAsync(...). Nothing else in that plan moves.
```

`Walk` gains `Source` and per-candidate `Origin`; `ComplexityRoutingDto` (the 200 body, the 409 extension, the Blocked event) carries both. The class name stays `ComplexityRoutingService` (CARD-0090 owns it; renaming is churn).

Filters, order and reasons are CARD-0090 §3 verbatim: delegatable kind / orchestrator clamp → stage `ForbiddenAliases` (Human card pin exempt) → `IsHeldAsync` → quota verdict. Nothing is added here; a pin's candidates meet the same four predicates. `EnforceForbiddenAliases` keeps running on the not-walked path only (on a walked create the walker's filter 2 is the same check with a skip instead of a throw).

---

## Ground truth (checked in code this pass, not from the cards)

- `RoutingPinService.ResolveAsync` (`server/Application/Services/RoutingPinService.cs`) returns `Decision(Pin, StagePin, CardIdentifier, AgentKind?, ModelLevel?, AgentId?, Warning, EventNote, Ignored)`; conflicts are computed per field against `pin.AgentKind`/`pin.ModelLevel`; `Applied => Pin != null && !Ignored`. `CreateAsync` (`AgentTaskService.cs:319-341`) overlays `pinDecision.AgentKind ?? request.AgentKind` onto the request, then (`:371-375`) `ResolveLevel` / `ResolveAgentKind` / `EnforceForbiddenAliases`, then quota, then `RequireAsync` with the Required-pin coda (`:427-437`). This is exactly where D3's `walked` fork goes: the `Decision` grows a `Candidates` list; the overlay becomes the composer; the three gates become per-candidate filters only when `walked`.
- `AgentTaskDispatcher.TickAsync` (`:386-441`) does the pin `NotBefore` skip (card pin, else stage) then the `IsHeldAsync` skip on `ResolveDispatchAliasAsync(task)`. CARD-0090's re-walk goes inside the second branch; this card widens its guard.
- `AgentTaskPipelineStatusService` (`:117-127`, `:176-178`, `:246`, `:259-270`) loads active pins raw from `_db.RoutingPins` and projects `RoutingPinService.ToRef` onto stage rows and `ready` rows. After D1 it reads `pin.Head`; `ToRef` adds `CandidateCount`. No new query.
- `routing-pin.ps1` (`Format-PinLine`) prints `agentKind modelLevel (modelAlias)`; `delegate.ps1 -Pin` (`:353-372`) PUTs `agentKind`/`modelLevel` from `$created` — both keep working through the shorthand.
- Client: `RoutingPinRefDto` (`client/src/api/agentTasks.ts:182-193`) is consumed only by `homeTasksModel.ts:285-291` (`routingPin?.notBefore` for the queue-reason line). The `ready` row does not render the pin's kind today, so "head + count" is a new, small chip, not a change to an existing one.
- Snapshot: `AppDbContextModelSnapshot.cs:2423-2489` (entity) and `:3757` (navigation) must be edited by hand with the migration.
- Enum tails today: `AttentionKind.ModelAvailabilityHold = 24`; `AgentTaskEventType` ends at `LandRefused = 22` (CARD-0090's plan says 15 — stale; **CARD-0090 appends after 22, and this card appends nothing**). Re-read at code time.
- Memory-side ground truth for the examples: `feedback_prefer_grok_dispatch.md` (Plan → fable/opus, +sol since 2026-08-30; Execute → Grok) and `feedback_execute_wip_2_on_grok.md`. After deploy those become `routing-pin.ps1 set … -Candidates …` lines (Execution notes) — written by the operator, not seeded.

---

## Entities and wire

### `RoutingCandidate` (new record, `server/Domain/Entities/RoutingPin.cs` or a sibling file)

`RoutingCandidate(AgentKind? AgentKind, AgentModelLevel? ModelLevel)` with `Describe()` → `ClaudeCode/Frontier`, `Grok`, `*/High`. JSON via `System.Text.Json` with `JsonStringEnumConverter`; the entity parses `CandidatesJson` once and caches.

### `RoutingPin` changes

- remove `AgentKind`, `ModelLevel`; add `CandidatesJson` (string?, max 1000), `Candidates` (not mapped), `Head` (not mapped).
- `RoutingPinService.Describe`/`EventNote` print the head plus `+N` (`pin=the human required CARD-0301 Plan routing pin ClaudeCode/Frontier (fable) +2`).

### `AgentTask.RoutingPinId` (guid?, no FK, same migration)

### `PutRoutingPinRequest`

adds `IReadOnlyList<RoutingCandidateRequest>? Candidates` (`{agentKind, modelLevel}`); keeps `AgentKind`/`ModelLevel` as the shorthand; validation per D1.

### `RoutingPinDto`

`agentKind`/`modelLevel`/`modelAlias` = head; adds `candidates: [{agentKind, modelLevel, alias, availableNow, unavailableReason}]`, `candidateCount`. `GET /api/routing-pins` evaluates `availableNow` per candidate the way CARD-0090's `GET /api/complexity-chains` does (holds + quota via the walker's filters, no dispatch) so `routing-pin.ps1 get` can say *why* a Plan pin is exhausted. The pipeline endpoint does **not** evaluate availability (frozen aggregation).

### `RoutingPinRefDto`

adds `candidateCount` (int). Present in the 409 `routingPin` extension and both pipeline surfaces.

### `RoutingPinService.Decision`

adds `IReadOnlyList<RoutingCandidate> Candidates` (the chosen grain's list, **before** narrowing; empty when none). `AgentKind`/`ModelLevel` on the record become the head's (kept so the not-walked path is untouched).

### `AgentTaskCreatedDto` / events

No new fields beyond CARD-0090's `Routing` (its `Source` now reads `pin:CARD-0301 Plan`). Created-event detail on a walked create: `pin=… candidate 2/3 opus; skipped fable (held until 2026-09-04T00:00:00Z, manual)`.

### HTTP

```
PUT    /api/routing-pins    body gains "candidates":[{"agentKind":"ClaudeCode","modelLevel":"Frontier"},{"agentKind":"ClaudeCode","modelLevel":"High"}]
                            agentKind/modelLevel remain the 1-candidate shorthand; both forms together → 422
GET    /api/routing-pins    each pin: candidates[] with availableNow/unavailableReason, candidateCount; agentKind/modelLevel = head
409    routing_pin_conflict message lists the candidates; routingPin extension carries candidateCount
```

No new routes. `POST /api/agent-tasks/{id}/reroute` (CARD-0090) additionally nulls `RoutingPinId`.

### `scripts/routing-pin.ps1`

```
routing-pin.ps1 set -Role Plan [-Card CARD-0301] -Candidates ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier
                    -Strength Required|Preferred -Provenance Human|Auto [-Forbidden …] [-NotBefore …] [-NotAfter …] [-Reason r]
routing-pin.ps1 get  →  stage-wide  Plan  Human Required  ClaudeCode/Frontier (fable) +2: ClaudeCode/High (opus), Codex/Frontier (gpt-5.6-sol)  …
routing-pin.ps1 get -Json  →  full candidates[] with availableNow / unavailableReason
```

`-Candidates` grammar mirrors `complexity-chain.ps1`: comma-separated, order preserved, each token `Kind/Level` or bare `Kind` (kind-only, tier from the request or role policy). A level-only candidate stays reachable through the `-Level` shorthand (one candidate) and the API; the list grammar does not grow a `*/Level` form until someone needs one. `-Kind`/`-Level` together with `-Candidates` is refused locally (exit 1) with the 422's wording. `clear` is unchanged. ASCII-only.

### `scripts/delegate.ps1`

No new parameters. Output after a walked create reuses CARD-0090's line (`routed CARD-0301 Plan pin -> opus (candidate 2/3); skipped fable (held until …, manual)`) and its Blocked line (`BLOCKED - routing exhausted: … A human decides … Do NOT pick a kind yourself.`). `-Pin` consequence line per D8.

---

## Slices

### S1 — Schema, service, HTTP, script (no behaviour change for existing pins)

`RoutingCandidate`; `RoutingPin.CandidatesJson`/`Candidates`/`Head`; migration `AddRoutingPinCandidates` (add + backfill + drop; `AgentTasks.RoutingPinId`); DbContext + snapshot; `PutRoutingPinRequest.Candidates` + D1 validation; `RoutingPinDto`/`RoutingPinRefDto` head + `candidates` + `candidateCount`; `ToDto`/`ToRef`/`Describe`/`EventNote`; `Decision.Candidates`; pipeline `ToRef` through `Head`; `GET` per-candidate `availableNow`; `routing-pin.ps1 -Candidates` + `get` format. Tests: backfill round-trip (seed the old column shape in an isolated schema, migrate, `Head` equals); every existing `RoutingPinServiceTests` / `RoutingPinCreateTests` / `RoutingPinDispatcherTests` / `AgentTaskPipelineStatusTests` / `RoutingPinScriptTests` green **unedited**; list validation matrix (0, 9, duplicate, both-null, non-delegatable kind, shorthand+list, agentId+2); GET shows `availableNow=false` with the hold's reason; script `set -Candidates` body, `-Kind` + `-Candidates` local refusal, `get` head-plus-count line.

### S2 — Composition and the walked create

`RoutingCandidates.Compose` cases for pins (D2, D4, D5) on top of CARD-0090's chain cases; `CreateAsync` fork on `Walked` (not walked → today's code, byte-identical); `RoutingPinId` snapshot; reuse CARD-0090's Blocked-on-exhausted insert, `RoutingExhaustedException`, `Routing` DTO, event detail; `ignoreModelDisabled` moot-warning; `delegate.ps1` output and `-Pin` consequence. Tests (`RoutingPinCandidateCreateTests`): Required `[fable, opus]`, fable held → opus with Warning naming the skip and `RoutingPinId` set; both held → 200 Blocked with `FailureReason` naming the pin, parent note, `refuseIfExhausted` → 409 `routing_exhausted`; Required `[fable]` held → 409 `model_disabled` + coda **unchanged**; Preferred `[sol, grok]` both held, role policy → opus → opus with `origin=rolePolicy`; Preferred + stage forbid fable, role policy → fable → Blocked (forbid filters the fallback); explicit `-Kind ClaudeCode` vs `[fable, opus, grok]` → walks fable, opus only; explicit `-Kind Codex` vs Required `[fable, opus]` → 409 conflict listing both; explicit `-Kind ClaudeCode -Level High` → opus single, not walked; `ignoreRoutingPin` → today's resolution, pin untouched; Required 3-candidate pin + `-Complexity Hard` → pin list only, `chain bypassed` warning; Preferred 2-candidate pin + `-Complexity Hard` → pin candidates then chain, no role-policy append; card list vs stage list → card list only; Orchestrator + `[Grok/Frontier, ClaudeCode/Frontier]` → Grok skipped by clamp, ClaudeCode chosen; `agentId` + 1 candidate still works.

### S3 — Dispatcher, resume, reroute, wall, attention

Widen CARD-0090's guards to `RoutingPinId != null`; re-resolve grain at re-walk; cleared pin → snapshot kept + `Held`; replaced pin → new list; `reroute` nulls `RoutingPinId`; S5 loop guard on composed length; attention grouping key per source with pin titles. Tests: Queued walked task on fable, fable held before tick → dispatched on opus with `Rerouted`; all held → Blocked; hold cleared → resumed with `Rerouted`; pin cleared between create and tick → `Held`, snapshot kept; single-candidate Required → `Held`, never rerouted; three Blocked Plan tasks on one stage pin → one `RoutingExhausted` row titled `Plan stage pin exhausted`, gone after resume; wall on a walked task (S5 fixture) → requeued on the next listed candidate; `reroute` → `RoutingPinId` null.

### S4 — Client and docs

Client: `RoutingPinRefDto.candidateCount?: number`; a chip on `ready` rows and the stage header when `routingPin` is present: `pin: fable` / `pin: fable +2` (`homeTasksModel` helper + `TaskCard` render + vitest). Docs: `docs/antiphon-api.md` (PUT `candidates`, GET shape, 409 text, `candidateCount`); `docs/orchestration-loop.md` §2 (a stage pin with candidates is how the operator's role-shaped fallback is recorded; how it composes with `-Complexity`; on `routing exhausted` relay to the operator — never pick a kind yourself, same rule as CARD-0090); `docs/agent-kinds.md` one gotcha line; CARD-0305 plan execution notes get a pointer (“lists: CARD-0322”).

**Ordering: S1 → S2 → S3 → S4, one PR, after CARD-0090 S1–S3 are Done.** If CARD-0090's Code pass built `WalkAsync` with the list assembled inside, S2 starts with the split in §"Walker contract" (half a day) before anything else.

**Estimate:** S1 1.0 d, S2 1.0 d, S3 0.75 d, S4 0.5 d → **~3.25 days build**, +0.5 d if the walker needs the split first. Verification floor (the test matrix above, run per namespace) ~1.5 h per slice.

---

## What this card does not do

- A second walker, a second terminal state, a second attention kind, a second availability signal. Everything terminal is CARD-0090's.
- Seed any pin or any default candidate list; append the role policy to a Required list; extend a one-candidate pin into a list by itself.
- Change a one-candidate pin's behaviour in any way (409 on hold with coda, `Held` at dispatch, `ignoreModelDisabled` queues).
- Concatenate lists across grains (card + stage), or across a Required pin and a chain.
- Reroute an explicit field, at create, dispatch, or on a wall.
- Add `-Candidates` to `delegate.ps1` (pins are written by `routing-pin.ps1`; `-Pin` pins the chosen pair).
- A client editor for lists (script + 409 detail + read-only chip, CARD-0305/0090 precedent).
- Per-candidate `NotBefore`/`ForbiddenAliases`/`AgentId` (all stay pin-level).
- Level-only tokens in the `-Candidates` grammar (`-Level` shorthand covers it).

---

## Test matrix and commands (per `docs/testing-and-build.md`; forward slash on OutputPath)

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0322/ -- --treenode-filter "/*/*/RoutingPin*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0322/ -- --treenode-filter "/*/*/ComplexityRouting*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0322/ -- --treenode-filter "/*/*/ModelAvailability*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0322/ -- --treenode-filter "/*/*/AgentTaskDispatcher*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0322/ -- --treenode-filter "/*/*/AgentTaskPipelineStatus*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0322/ -- --treenode-filter "/*/*/AttentionServiceTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0322/ -- --treenode-filter "/*/Antiphon.Tests.Scripts/*/*"
pwsh -File scripts/test-client.ps1
```

Delete `bin-card0322*` after. Shared-Postgres tests seed by id; the backfill test uses `TestDbFixture.CreateIsolatedSchemaAsync` and applies the migration to a schema seeded with the pre-migration columns.

| Layer | Pin |
|---|---|
| Unit | `Compose`: Required list is the whole set; Preferred appends role policy once, deduped; explicit narrowing (empty / one / many); card list beats stage list; Required + chain = pin only; Preferred + chain = pin then chain, no role-policy append; `Walked` iff ≥ 2 |
| Application | S1 validation matrix; backfill round-trip; **every existing RoutingPin*/ModelAvailability* test green unedited** |
| Application | S2 create matrix (above), incl. the one-candidate 409+coda row unchanged |
| Application | S3 dispatcher / resume / reroute / wall / attention grouping |
| Script | `-Candidates` body and order; local refusal of shorthand+list; `get` head `+N` line; `delegate.ps1 -Pin` consequence line |
| Client vitest | `candidateCount` optional; chip renders `pin: fable +2`; queue-reason line unchanged |

---

## Risks

| Risk | Standing |
|---|---|
| CARD-0090's Code pass builds the walker with the list assembled inside | Addendum at the top of the CARD-0090 plan names the split; if it lands anyway, S2 starts with the half-day refactor. Not a second walker either way. |
| Dropping `AgentKind`/`ModelLevel` while an old binary runs | Migrations run at startup of the new binary; restart through `restart-apphost.ps1`; the old binary is never left running against the new schema. Two active pins exist today (`routing-pin.ps1 get`, 2026-09-02: stage Plan Human Required `ClaudeCode/Frontier`; stage Code Human Required `Grok` with **no level** — a kind-only candidate, which is why `RoutingCandidate` is a partial pair and the backfill emits `"modelLevel":null`). The backfill is trivial. |
| Preferred's role-policy fallback picks a model the operator is currently "off" | The stage `ForbiddenAliases` list is the tool for that, and the walker filters it; documented next to the strength table. |
| `-Kind X` narrowing surprises a caller who expected the whole list | It cannot: explicit fields are never changed (D4); the narrowing is printed in the Created detail and `delegate.ps1` output. |
| A replaced pin re-walks a queued task the operator wanted parked | Only when the snapshot alias cannot run anyway, only onto a listed candidate, with a `Rerouted` event; Cancel is the park. Stated as an explicit exception to the CARD-0305 rule. |
| Enum ordinals in the backfill SQL | Read `AgentKind.cs` at code time; the round-trip test catches a wrong CASE arm. |
| Client chip crowds the `ready` row on the phone | One short chip, only when a pin exists; `+N` only when N > 1. |

---

## Execution notes

- **Day one after deploy** the operator (not the migration, not a seed) writes today's live rules; these replace the memory log's prose:
  ```
  routing-pin.ps1 set -Role Plan  -Provenance Human -Strength Preferred -Candidates ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier -Reason "plan on fable, opus, then sol"
  routing-pin.ps1 set -Role Code  -Provenance Human -Strength Required  -Candidates Grok/Frontier -Forbidden fable -Reason "execute on grok, never fable"
  routing-pin.ps1 set -Role Debug -Provenance Human -Strength Preferred -Candidates Grok/Frontier,ClaudeCode/Frontier,Codex/Frontier -Reason "investigate on grok first"
  ```
  Whether Plan is Required or Preferred is the operator's call at that moment; this plan ships no opinion in the database.
- CARD-0301's card pin (`ClaudeCode/Frontier`, `NotBefore` 2026-09-03) is a one-candidate pin and is untouched by the migration except for the JSON shape.
- Orchestrator habit is unchanged: `delegate.ps1 -Role Plan -Card CARD-x` with no `-Kind`; the pin does the rest. `-Kind` remains correct when the operator named the model, and it is never rerouted.
- Enum values, 409 sentence shapes and the Blocked/attention text cite CARD-0090; do not restate them differently here.
