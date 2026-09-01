# CARD-0305 — Per-card/stage routing pins (Human vs Auto), distinct from CARD-0309 holds

**Date:** 2026-09-01 (Plan pass, task a873f12a — design only; no production code changed)
**Card:** CARD-0305 "Per-task/stage routing pin (agent/model/tier/thinking), tagged human-decided vs auto-decided"
**Distinct from:** CARD-0309 / CARD-0022 `ModelAvailabilityHold` (fleet: is this kind+alias **usable**). This card: for **this card+role**, which kind/tier **should run**. Do not merge the tables. A pin naming a held model calls `ModelAvailability.Require` and surfaces the same 409 `model_disabled` + available list; it does not become a hold.

**Sources (verified this pass):** CARD-0305 (examples table), CARD-0309 plan (`docs/superpowers/plans/2026-09-01-card-0309-manual-model-availability-hold-plan.md`), CARD-0022 Require/skip, CARD-0304 pipeline DTOs (`QueueReason` today `sharedCheckoutLease` | `awaitingDispatch`), `AgentTaskService.CreateAsync` / `ResolveAgentKind` / `ResolveLevel`, `RolePolicyEntry.Kind` (unset everywhere), `CreateAgentTaskRequest`, `delegate.ps1` `-Kind`/`-Level`/`-Card`/`-Agent`, `ModelLevelAliases`, `CodexLaunchArgs.ReasoningEffort` (derived from `ModelLevel` — no separate task column). `ModelAvailabilityHold` is still plan-only.

---

## Decision

**A separate `RoutingPins` table keyed on card+role (and a stage-wide row when `CardId` is null).** Not new columns on `AgentTask`. A task is an attempt; the pin is the standing instruction that the **next** create must read. At create, the resolved `AgentKind` / `ModelLevel` / `AgentId` are snapshotted onto the task as today. Provenance lives on the pin, not on the task.

Two grains, one table:

| Grain | `CardId` | Example |
|---|---|---|
| Stage-wide | null | Plan: Codex Frontier, forbid `fable`; Code: Grok; Debug: Grok preferred |
| Per-card | set | CARD-0304 Plan → Codex Frontier Required Human; CARD-0301 Plan → ClaudeCode Frontier Required Human, `NotBefore=2026-09-03` |

**Human vs Auto is overwrite protection, not a second key.** One active row per `(CardId, Role)` (and one stage-wide per `Role`). Human cannot be replaced by Auto (409 `routing_pin_human`). Human replaces Auto. Human replaces Human (explicit). Auto replaces Auto.

**CARD-0301's "fable, hold until 2026-09-03" is `NotBefore` on that card's pin, not a fleet hold.** Stage-wide Plan forbids fable; the card pin **overrides** that forbid. The work may be created Queued; the dispatcher skips until `NotBefore` **and** the alias is not held. Fleet "fable disabled" remains CARD-0309.

RolePolicy stays the provenance-less fallback (today's `ResolveLevel` / `ResolveAgentKind`). Setting `RolePolicy.Plan.Kind` is a config deploy, not a Human pin — that is why CARD-0301 would have been washed away by "everything moves off fable" if we only had RolePolicy.

Reasoning-effort / "thinking" is `ModelLevel` (Codex `model_reasoning_effort` and Grok/Claude ladders already key on it). No new effort column.

---

## Ground truth (checked, not guessed)

Create already resolves kind then level (`AgentTaskService.cs:312-313`): explicit request → RolePolicy → ClaudeCode / role default level. That resolution is **per task**, forgotten the moment the process exits. The examples on the card live in chat and `feedback_prefer_grok_dispatch.md`. A later `delegate.ps1 -Role Plan -Card CARD-0304` with no `-Kind` still gets ClaudeCode Frontier (fable).

`AgentTask.AgentId` is a standing-agent pin for **that task only**. Follow-up `-OnAgent` copies the prior task's kind. Neither is card+stage.

CARD-0309 / CARD-0022: after kind/quota, `Require(kind, alias)` 409s new creates when held; dispatcher skips already-queued. Pins must run **before** Require (so the alias Require sees is the pinned one) and must not invent a second skip list for fleet unavailability.

Pipeline `QueueReason` is a string; adding `routingPinNotBefore` is additive. Do not hang pin **writes** off `/api/agent-tasks/pipeline`.

Check-role never binds a card (`AgentTask.CardId` docs). No pin for `Check`. `Custom` is allowed.

---

## Entity `RoutingPin`

Table `RoutingPins`. Active = `ClearedAt == null`.

| Column | Type | Notes |
|---|---|---|
| `Id` | guid | |
| `CardId` | guid? | null = stage-wide. FK Cards ON DELETE SET NULL (a deleted card must not trap pins; archived cards keep them) |
| `Role` | `AgentTaskRole` | Plan/Code/Debug/… not Check |
| `Provenance` | `RoutingPinProvenance` | `Auto = 0`, `Human = 1` |
| `Strength` | `RoutingPinStrength` | `Preferred = 0`, `Required = 1` |
| `AgentKind` | `AgentKind?` | null = do not constrain kind |
| `ModelLevel` | `AgentModelLevel?` | null = role policy level. Alias is `ModelLevelAliases.For(kind, level)` |
| `AgentId` | guid? | standing agent; same allowlist as `delegate.ps1 -Agent` (not a pool delegate) |
| `ForbiddenAliases` | string? | comma-separated canonical aliases, e.g. `fable`. Stage-wide "not fable". Empty = none |
| `NotBefore` | DateTime? | UTC. Dispatcher skip while `now < NotBefore`. CARD-0301 |
| `NotAfter` | DateTime? | UTC. Pin self-clears (lazy `ClearedAt`) when past — Auto expiry, not a hold |
| `Reason` | string | capped 400. "operator: CARD-0301 stays on fable until Thursday" |
| `SourceTaskId` | guid? | which delegate wrote it (audit) |
| `CreatedAt` / `UpdatedAt` | DateTime | |
| `ClearedAt` | DateTime? | |

Indexes:

- `IX_RoutingPins_CardId_Role_Active` unique (`CardId`, `Role`) WHERE `CardId IS NOT NULL AND ClearedAt IS NULL`
- `IX_RoutingPins_Role_Stage_Active` unique (`Role`) WHERE `CardId IS NULL AND ClearedAt IS NULL`

`ForbiddenAliases` stored as text (no extra table). Normalize each token with `ModelAlias.Normalize` when CARD-0022's helper exists; until then a small local map (`fable`/`Fable 5`/`claude-fable-5` → `fable`, same for opus/sonnet/haiku/grok-4.6/gpt-5.6-*).

---

## Resolution at `CreateAsync` (after card binding, before quota / Require)

`RoutingPinResolver.Resolve(cardId, role, request)` returns a `RoutingDecision` (kind, level, agentId, pinId, provenance, notes).

**Precedence (first match that constrains the field wins per field, but a card pin is chosen as a whole over a stage pin):**

1. **Card+role pin** if present (Human or Auto — there is only one).
2. Else **stage-wide pin** for that role.
3. Else today's `ResolveAgentKind` / `ResolveLevel` / request `AgentId`.

Then apply request overrides:

| Request | Preferred pin | Required Human pin |
|---|---|---|
| omits Kind/Level | use pin | use pin |
| Kind/Level **matches** pin | use request | use request |
| Kind/Level **conflicts** | use request; Warning event "overrode preferred pin" | **409 `routing_pin_conflict`** naming the pin (card, role, kind, level, reason). `-IgnoreRoutingPin` / `ignoreRoutingPin: true` proceeds and does **not** clear the pin (one-shot). |
| `-Agent` / `-OnAgent` conflicts with pin `AgentId` or Required kind | same as Kind conflict | same 409 |

A **card Human Required** pin may name an alias the **stage** pin forbids (CARD-0301 vs "not fable"). Stage `ForbiddenAliases` apply only when the chosen grain is the stage pin, or when there is no card pin and the request's resolved alias is in the list → 409 `routing_pin_forbidden` (`fable is forbidden for Plan (human stage pin); available under that pin: Codex/Grok …`).

Explicit request without `-Pin` does **not** write a pin (one-shot dispatch). `-Pin` / PUT writes.

Follow-up `-OnAgent` is an explicit continuation: Required Human pin that disagrees 409s; Preferred yields.

Orchestrator kind clamp (ClaudeCode only) still applies after the pin. A Human pin of Grok on an Orchestrator task is 422 as today.

**Then** CARD-0136 quota, **then** CARD-0022/0309 `Require(kind, alias)` if `ModelAvailability` exists.

If Require 409s and a Required pin named that alias, append: `this {card} {role} is pinned to {alias} (human); the available list does not satisfy the pin — wait until the hold clears, pass ignoreModelDisabled to queue, or REPLACE the pin`. Do not silently pick from `available`. Preferred pin + held alias: **do not auto-fallback** in v1 (CARD-0090). Same 409. Operator changes Kind or pin.

`ignoreRoutingPin` and `ignoreModelDisabled` are different flags. Never conflate.

Create with `NotBefore` in the future: **200 Queued** (the pin is why the work exists). Do not 409. Dispatcher skip until `NotBefore`. Opposite of fleet-hold 409, on purpose.

Event on create when a pin applied: `Dispatched`-style Created detail already names kind; add `pin=human required CARD-0301 Plan fable notBefore=…` / `pin=stage human Plan forbids fable`.

---

## Dispatcher

Inside the queued foreach, after scope/concurrency skips, **before** spawn:

1. If the task's card+role pin (or the stage pin if none) has `NotBefore > now` → skip, `Held` event once `routing pin not before {until}`, `TickResult.SkippedRoutingPin++` (additive default 0). Same shape as CARD-0022 skip.
2. Then existing/planned `IsHeld` skip for the **already-resolved** task alias (snapshotted at create). Do not re-resolve the pin at spawn — create already snapshotted. Changing a pin does not rewrite Queued tasks (Human pin change is a new PUT; operator retries or waits). Document: a Queued task keeps the kind it was created with; PUT a pin then `-Retry` if you need existing rows to move.

---

## HTTP / script / `delegate.ps1`

```
GET    /api/routing-pins?card=CARD-0304&role=Plan
GET    /api/routing-pins                  (all active; include stage-wide)
PUT    /api/routing-pins                  upsert
DELETE /api/routing-pins/{id}             clear (ClearedAt=now), 204 if already clear
```

PUT body (camelCase):

```json
{
  "card": "CARD-0301",
  "role": "Plan",
  "provenance": "Human",
  "strength": "Required",
  "agentKind": "ClaudeCode",
  "modelLevel": "Frontier",
  "forbiddenAliases": [],
  "notBefore": "2026-09-03T00:00:00Z",
  "reason": "operator: plan on fable after the weekly cap"
}
```

Stage-wide: omit `card`. Auto PUT on a Human row → 409 `routing_pin_human`. 422 unknown card, Check role, non-delegatable kind, `notBefore` in the past, `notAfter` <= `notBefore`, standing `agentId` that is a pool delegate.

Provenance is **asserted by the caller**, not inferred from the bearer. The orchestrator records Human when the operator said so; Auto when it chose. `SourceTaskId` from `X-Antiphon-Task-Token` when present.

`scripts/routing-pin.ps1` (ASCII, `card.ps1` verbs, `ANTIPHON_API`):

```
routing-pin.ps1 get   [-Card CARD-0304] [-Role Plan] [-Json]
routing-pin.ps1 set   -Role Plan [-Card CARD-0304] -Provenance Human -Strength Required
                      [-Kind Codex] [-Level Frontier] [-Forbidden fable]
                      [-NotBefore 2026-09-03T00:00:00Z] [-Reason r]
routing-pin.ps1 clear -Role Plan [-Card CARD-0304]
```

`delegate.ps1`:

- Reads are server-side; no extra flag to honor a pin.
- `-Pin` : after a successful create, PUT a **Human** pin from this invocation's Role/Card/Kind/Level (Required). Stage-wide if `-Card` omitted and title has no card — refuse 422 ("-Pin without a card would write a stage-wide pin; pass -Card or use routing-pin.ps1").
- `-IgnoreRoutingPin` : send `ignoreRoutingPin: true`.

Do not overload `card.ps1`.

---

## Mapping the examples

| Example | Row |
|---|---|
| CARD-0304 Plan Sol | Card=0304, Role=Plan, Human, Required, Kind=Codex, Level=Frontier |
| CARD-0301 Plan fable until 2026-09-03 | Card=0301, Role=Plan, Human, Required, Kind=ClaudeCode, Level=Frontier, NotBefore=2026-09-03T00:00:00Z |
| Plan: Sol or Grok, not fable | Card=null, Role=Plan, Human, Preferred, Kind=Codex, Level=Frontier, ForbiddenAliases=`fable` (explicit `-Kind Grok` allowed; `-Kind ClaudeCode -Level Frontier` 409) |
| Execute always Grok, never fable | Card=null, Role=Code, Human, Required, Kind=Grok, ForbiddenAliases=`fable` |
| Investigate Grok first, one at a time | Card=null, Role=Debug, Human, Preferred, Kind=Grok. "One at a time" is CARD-0304 `RecommendedInFlight=1`, already shipped |
| Most Investigate = Debug+Grok, no per-card instruction | No card pin. Stage Debug pin above, or RolePolicy.Kind=Grok (config, no provenance) |

No fallback chain "Grok then Fable then Sol" (CARD-0090). Preferred Grok + human follow-up is a second dispatch.

---

## Interaction with CARD-0309 (do not merge)

```
CreateAsync
  bind card
  RoutingPinResolver.Resolve   → kind, level, alias
  quota gate
  ModelAvailability.Require(kind, alias)   // if the type exists; else no-op
  insert AgentTask snapshot
```

| Situation | Result |
|---|---|
| Pin fable, fable not held, NotBefore past | 200, Codex/Claude as pinned |
| Pin fable, fable held | 409 `model_disabled` + available list + pin coda. No silent reroute |
| Pin fable, NotBefore future, fable not held | 200 Queued; dispatcher skip until NotBefore |
| Pin fable, NotBefore future, fable held | 200 Queued; skip until **both** clocks |
| Stage forbids fable, no card pin, request fable | 409 `routing_pin_forbidden` |
| Stage forbids fable, CARD-0301 Human Required fable | pin wins; then Require/NotBefore as above |
| `ignoreModelDisabled` | CARD-0309: queue despite hold. Pin still applies. |
| `ignoreRoutingPin` | this card: ignore pin for this create. Require still applies to the **request's** kind |

Dispatcher: `NotBefore` skip is this card; `IsHeld` skip is CARD-0309. Two counters, two Held-event texts.

If `ModelAvailability` is not on master yet, S3's Require call is `GetService` optional — tests that need the 409 pin+hold combo seed a fake `IModelAvailability` or skip until CARD-0022/0309 land. Do **not** implement `ModelAvailabilityHolds` here.

---

## Slices

### S1 — Table, resolver, create-time apply, Human overwrite rule

Entity + migration + `RoutingPinService` (PUT/GET/DELETE + `Resolve`). `CreateAsync` calls `Resolve` after card bind. Created event names the pin. Tests: CARD-0304-shaped pin without `-Kind` → Codex Frontier; no pin → ClaudeCode as today; Human PUT then Auto PUT 409; Human replaces Auto; Check role 422; explicit Kind vs Required Human 409; `-IgnoreRoutingPin` 200; stage ForbiddenAliases vs card Required override; `NotBefore` create still 200 Queued.

### S2 — Dispatcher skip for `NotBefore`

`TickAsync` skip + Held event + `SkippedRoutingPin`. Test: Queued task, pin NotBefore +1 h → not dispatched; FakeTimeProvider past NotBefore → dispatches. Does not require CARD-0309.

### S3 — CARD-0309 handshake

When `IModelAvailability` is registered: Require after Resolve; 409 body includes pin coda for Required pins. Test with a stub: pin fable + held fable → 409 `model_disabled` listing available **and** mentioning the pin. If the interface is absent, skip the test with a named ignore for CARD-0309.

### S4 — HTTP + `routing-pin.ps1` + `delegate.ps1 -Pin` / `-IgnoreRoutingPin`

Endpoints next to agent-tasks. Script HttpListener pins like `DelegateScriptKindTests`. ASCII-only script.

### S5 — Pipeline + docs (thin)

Additive optional `routingPin` on `AgentTaskPipelineReadyDto` and stage DTO (id, provenance, kind, level, notBefore) — null when none. QueueReason `routingPinNotBefore` when S2 skipped. CARD-0304 tests that ignore unknown fields stay green if they bind by name.

Docs: `docs/orchestration-loop.md` §2 (pins beat RolePolicy; Human survives RolePolicy edits); `docs/antiphon-api.md` 409 codes `routing_pin_conflict` / `routing_pin_forbidden` / `routing_pin_human`; pointer from CARD-0309 plan execution notes ("pins consume Require, they do not write holds").

No client drawer in v1 (409 detail + script are the operator surface, same as CARD-0136/0309). CARD-0301 phone UI can consume GET later.

---

## What this card does not do

- Merge with `ModelAvailabilityHolds` or write holds from a pin.
- CARD-0090 fallback chains / auto kind-switch when the pinned alias is held.
- Per-project/per-board pins (fleet + card, like tasks).
- A new `ReasoningEffort` column (ModelLevel is the axis).
- Auto-upsert a pin on every default create (noise; CARD-0303 diagnose agent PUTs Auto when it chooses).
- Changing RolePolicy shipped Kind defaults (still unset → ClaudeCode).
- Overloading `card.ps1`.
- Rewriting already-Queued tasks when a pin changes.

---

## Test matrix

| Layer | Test |
|---|---|
| Application | Create `-Card CARD-x -Role Plan` with Human Required Codex Frontier pin, omitted Kind → task.AgentKind Codex, ModelLevel Frontier |
| Application | No pin → ClaudeCode (today) |
| Application | Human pin then Auto PUT → 409 `routing_pin_human` |
| Application | Explicit `-Kind Grok` vs Required Human Codex → 409 `routing_pin_conflict`; with ignoreRoutingPin → Grok, pin unchanged |
| Application | Stage Plan ForbiddenAliases fable + request Frontier Claude → 409 `routing_pin_forbidden`; CARD-0301 card Required fable → allowed |
| Application | NotBefore future → 200 Queued; dispatcher skip; after clock, dispatch |
| Application | S3 stub: Required fable + IsHeld → 409 `model_disabled` + pin sentence |
| Script | `set`/`get`/`clear` bodies; `delegate.ps1 -Pin` PUTs Human Required; `-Pin` without card 422; `-IgnoreRoutingPin` property omitted-when-unset |
| Pipeline | ready row carries pin when present; queued reason `routingPinNotBefore` |
| Shared-Postgres | seed card+pin ids only |

Run: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0305/ -- --treenode-filter "/*/*/RoutingPin*"` and AgentTask create/dispatcher filters. Forward slash. Delete `bin-card0305*`. No Pty/client required.

---

## Sequencing and risks

**Order: S1 → S2 → S4 → S5. S3 when `IModelAvailability` exists (same PR if CARD-0309/0022 already imported the type; otherwise optional test).** One PR.

| Risk | Standing |
|---|---|
| Orchestrator claims Human | Provenance is a recorded claim. Reason + SourceTaskId. Acceptable. |
| Stage "not fable" blocks 0301 | Card pin is a whole-row override. Test pins it. |
| Queued task ignores later PUT | Documented. Retry. No silent rewrite. |
| NotBefore create vs 0309 409 confusion | Opposite on purpose: dated **pin** queues; fleet **hold** 409s. 409 text names which. |
| RolePolicy.Kind vs stage pin | Pin wins. RolePolicy remains fallback when no pin. |
| CARD-0303 diagnose agent | Reads GET; writes Auto PUT. No extra API. |

---

## Execution notes

After deploy, replace `feedback_prefer_grok_dispatch.md` with:

```
routing-pin.ps1 set -Role Plan -Provenance Human -Strength Preferred -Kind Codex -Level Frontier -Forbidden fable -Reason "planning off fable"
routing-pin.ps1 set -Role Code -Provenance Human -Strength Required -Kind Grok -Forbidden fable
routing-pin.ps1 set -Card CARD-0304 -Role Plan -Provenance Human -Strength Required -Kind Codex -Level Frontier
routing-pin.ps1 set -Card CARD-0301 -Role Plan -Provenance Human -Strength Required -Kind ClaudeCode -Level Frontier -NotBefore 2026-09-03T00:00:00Z
```

A later "everything off fable" is another stage PUT (Human) or RolePolicy edit — CARD-0301's row stays until Human clears it.
