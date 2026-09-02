# CARD-0309 — Manual model/kind disable, timed auto-re-enable, fail-fast 409 with what's available

**Date:** 2026-09-01 (Plan pass, task c16069e0 — design only; no production code changed)
**Card:** CARD-0309 "Disabled model/kind must fail dispatch immediately, not after launch (with optional timed auto-re-enable)"
**Prerequisite:** CARD-0022 plan `docs/superpowers/plans/2026-09-01-card-0022-per-model-usage-limit-pause-plan.md` (`988012f8`). That card owns the **table, reader, dispatcher skip, create/start 409 `model_disabled`, GET read model, and AutoDetected writer**. This card is the **other writer** onto the same `ModelAvailabilityHold` row. Do not invent a second table, a second pause list, or a fleet-wide singleton.

**Sources (verified this pass):** CARD-0309 (full card text), CARD-0022 plan (Shared state + S3 + "CARD-0309 later"), CARD-0136 (`ignoreSubscriptionQuota`, `SubscriptionQuotaLowException`, create/start hooks not dispatcher), CARD-0304 (pipeline is a *read* aggregation — do not hang a write surface off it), `AgentTaskService.CreateAsync` / `DelegatableKinds`, `AgentControlService.StartAsync`, `ModelLevelAliases`, `AttentionKind` live enum (ends at `UnmarkedWaiting = 23`; CARD-0022 reserved **24** for `ModelAvailabilityHold`), `delegate.ps1`, `docs/antiphon-api.md` 409 codes. `ModelAvailabilityHold` / `model_disabled` **do not exist in code yet** — CARD-0022 is plan-only as of this pass.

---

## Decision

One mechanism. Two writers. Same 409.

| Writer | Source | When | `DisabledUntil` |
|---|---|---|---|
| CARD-0022 | `AutoDetected = 0` | Wall stub parsed | SessionLimit: reset+2min. ModelCap: null |
| **This card** | `Manual = 1` | Operator PUT | Timestamp, or null = until cleared |

1. **No second table.** `ModelAvailabilityHolds` keyed `(Kind, ModelAlias)`, active = `ClearedAt == null`, filtered unique index as CARD-0022 specified. Manual upserts that row. Auto-re-enable is CARD-0022's already-designed sweep + lazy `IsHeld` (`DisabledUntil <= now` → `ClearedAt = now`). No Hangfire job, no health-probe clear.
2. **Fail immediately at create/start, not at spawn.** CARD-0022 S3 already places `ModelAvailability.Require(kind, alias)` on `AgentTaskService.CreateAsync` and `AgentControlService.StartAsync` after kind/quota gates — the two doors a caller is still present at. 409 `model_disabled` with `available: [...]`. The dispatcher **skips** already-queued work (does not 409; there is nobody to hear it) and dispatches it when the hold clears. That is the CARD-0301 "sit queued-on-fable until Thursday" path for work that was already Queued; **new** creates 409.
3. **Kind-wide disable is alias `*`**, not a second key or a Kind-only table. `IsHeld(kind, alias)` is true if `(kind, alias)` **or** `(kind, *)` is active. AutoDetected never writes `*`. `ListAvailable` omits every alias of a kind that has an active `*`.
4. **Outrank (CARD-0022 left this as a named contract; this card implements it):** one active row per `(Kind, ModelAlias)`. Manual PUT on an AutoDetected row becomes `Source = Manual` and takes the operator's `DisabledUntil`. AutoDetected later may refresh `RawText` / `HitAt` / `SourceSessionId` / `Reason` evidence **only** — it must not change `DisabledUntil` or demote `Source` back to AutoDetected. Manual DELETE sets `ClearedAt` on whichever source is active (operator unpause, including of a detected cap).
5. **`ignoreModelDisabled` queues, it does not launch.** Unlike CARD-0136's `ignoreSubscriptionQuota` (which starts the process), this flag only bypasses the create/start 409. The dispatcher skip still applies. The task sits `Queued` until the hold clears (or the operator DELETE-clears it). There is **no** "spawn into a held model" flag in v1 — that is how this session burned wall-clock and dollars after fable was already known exhausted. Internal AlwaysOn / channel / check-interpreter starts do **not** pass the flag (CARD-0022: a Fable orchestrator restart is refused; do not silently reroute).

CARD-0304 pipeline is **not** this card's UI. CARD-0305 pins are **not** this card; when they exist they go through `CreateAsync` and get the same 409 for free. CARD-0090 fallback chains stay out.

---

## Prerequisite (S0) — import CARD-0022 shared state if it is not on master

CARD-0022 Code has not landed. This card cannot wait on a second table.

**If** `ModelAvailabilityHold`, `ModelAvailability`, `model_disabled`, dispatcher `SkippedModelAvailability`, and `GET /api/model-availability` exist: S0 is a no-op (assert the types, move on).

**If not:** implement **exactly** CARD-0022's "Shared state" + S3 + the GET half of S4. Copy, do not redesign:

- Entity + migration + `IX_ModelAvailabilityHolds_Kind_ModelAlias_Active`
- `ModelAlias.Normalize`, `ModelAvailability.IsHeld` / `ListHeld` / `ListAvailable` / `SweepExpiredAsync` / `Require`
- Dispatcher skip of queued tasks whose resolved alias is held
- `CreateAsync` / `StartAsync` 409 `model_disabled` + `modelAvailability` problem-details extension
- `GET /api/model-availability` (active holds + available aliases)
- `AttentionKind.ModelAvailabilityHold = 24` (re-read the live enum at code time; 24 is free today after `UnmarkedWaiting = 23`)
- Lazy clear on read so a 409 cannot fire 1 s after `DisabledUntil`

Do **not** implement CARD-0022's wall parser, FakeClaude fixtures, or AutoDetected writer in this card. Leave a failing-or-skipped test `auto_detected_writer_does_not_shorten_a_manual_DisabledUntil` for CARD-0022 S2 to turn green.

Cite CARD-0022 for column types, 409 sentence shape, and `ListAvailable` membership (`ModelLevelAliases` for `DelegatableKinds` minus held). Alias vocabulary: `fable`, `opus`, `sonnet`, `haiku`, `grok-4.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`.

---

## Ground truth (checked, not guessed)

### Why create/start, not the dispatcher, is the 409

CARD-0136 already measured this: `AgentTaskDispatcher.TickAsync` runs after the HTTP caller has gone. A gate there can only silently skip, which the card forbids for **new** work ("fail immediately", "informative error"). Queued work has no caller; skip-until-clear is the resume CARD-0022 named. Both are required. Neither is a second mechanism.

### Why `*` rather than exploding into four Claude rows

Kind-wide "Claude is out until Thursday" is the operator sentence on the card. Writing four alias rows drifts when a new family appears. `(ClaudeCode, *)` is one active row, one DELETE, and `IsHeld` ORs it with per-alias holds. Unique index still holds. Grok's ladder is already one alias (`grok-4.6`); `*` is still legal and equivalent.

### Why ignore does not launch

The incident this card exists to stop is launch-then-discover. A flag that then launches would recreate it. Queue-until-re-enable is the timed resume the card asked for, for work the operator explicitly parks. Already-queued tasks do not need the flag.

### Why not CARD-0304's pipeline

CARD-0304 shipped `GET /api/agent-tasks/pipeline` as a frozen advisory aggregation over tasks. Adding holds there is a contract change on a sibling card and still would not give a write. `GET /api/model-availability` is the read model CARD-0022 reserved for this. Pipeline may *later* join; not here.

---

## Slices

### S1 — Manual writer, kind-wide `*`, outrank, script

**Files:** `ModelAvailability` (upsert/clear methods CARD-0022's reader did not have), new `server/Api/Endpoints/ModelAvailabilityEndpoints.cs` next to Attention/AgentTask groups, `scripts/model-availability.ps1` (ASCII-only, `card.ps1` verb shape), tests.

**HTTP** (kebab-case, camelCase JSON):

```
GET    /api/model-availability
PUT    /api/model-availability/{kind}/{alias}
DELETE /api/model-availability/{kind}/{alias}
```

`kind` is the enum member name (`ClaudeCode`, `Grok`, `Codex`). `alias` is a known `ModelLevelAliases` value or `*` (case-insensitive; store canonical lowercase, `*` as `*`).

PUT body:

```json
{ "disabledUntil": "2026-09-04T00:00:00Z", "reason": "fable weekly cap; Plan on grok until Thursday" }
```

- `disabledUntil` omitted or `null` = open-ended (until DELETE). Past timestamp → **422**.
- `reason` optional; default `"manual hold"`. Capped (400 chars).
- Upsert the **active** row for that key: `Source = Manual`, `HitAt = now`, `RawText = null`, session/task ids null. If the active row was AutoDetected, **convert in place** (same `Id`): do not insert a second active row (unique index).
- 200 with the GET item shape. 422 unknown kind / non-delegatable kind (`Raw`, `OpenCode`) / unknown alias.

DELETE: `ClearedAt = now` on the active row (any Source). **204** if already clear (script-idempotent). Do not delete the row.

GET: CARD-0022's read model. Include `source` on each hold so the UI can say "detected" vs "manual". `available` is the 409 list.

**Reader extension:** `IsHeld(kind, alias, now)` also true when `(kind, *)` is active. Pin: fable-specific hold does not hold opus; `*` holds both.

**Outrank tests (the CARD-0022 named contract, now green):**

- Active Manual until Thursday + AutoDetected SessionLimit until 18:10 today → `DisabledUntil` still Thursday, `Source` still Manual, evidence fields may update.
- Active Manual open-ended (`null`) + AutoDetected with a reset → still `null`.
- Active AutoDetected + Manual PUT until Thursday → `Source = Manual`, `DisabledUntil` = Thursday.
- DELETE of AutoDetected or Manual → not held, create 200.

**Sweep:** Manual with `DisabledUntil` in the past is cleared by the same `SweepExpiredAsync` / lazy `IsHeld`. Pin: create Frontier (fable) after that instant is 200 without a DELETE.

**409 sentence (Manual):**

- Timed: `fable is disabled until 2026-09-04T00:00:00Z (manual); available: opus, sonnet, haiku, grok-4.6, gpt-5.6-sol, gpt-5.6-terra, gpt-5.6-luna`
- Open-ended: `fable is disabled (manual, no re-enable time); available: …`
- Kind-wide: `ClaudeCode is disabled until … (manual); available: grok-4.6, gpt-5.6-sol, gpt-5.6-terra, gpt-5.6-luna`

Same `code: model_disabled` and `modelAvailability: { kind, modelAlias, disabledUntil, source, available }` extension CARD-0022 specified. New `ModelDisabledException : HttpException` if S0 did not already add it (mirror `SubscriptionQuotaLowException`).

**Script** `scripts/model-availability.ps1` (ASCII, `ANTIPHON_API` like `delegate.ps1`):

```
model-availability.ps1 get [-Json]
model-availability.ps1 hold  -Kind ClaudeCode -Model fable [-Until 2026-09-04T00:00:00Z] [-Reason r]
model-availability.ps1 hold  -Kind ClaudeCode -Model * -Until 2026-09-04T00:00:00Z
model-availability.ps1 clear -Kind ClaudeCode -Model fable
```

`-Until` is ISO-8601 UTC (`Z` required, or a numeric offset). Naive local timestamps are 422 from the server if they parse as past, and the script should refuse a value with no offset rather than guess. No `card.ps1` overload — that script is cards.

**Tests:** `DelegateScriptKindTests`-style HttpListener pin for the script verbs; Application tests for PUT/DELETE/GET, `*`, outrank, expired Manual auto-clear, create 409 listing remaining aliases, Grok create while fable is Manually held → 200. Shared-Postgres: seed by id.

Do not kill live Fable sessions. Do not Fail queued fable tasks.

### S2 — `ignoreModelDisabled` (queue, do not spawn)

**Files:** `CreateAgentTaskRequest`, `StartAgentRequest`, `AgentTaskService.CreateAsync`, `AgentControlService.StartAsync`, `scripts/delegate.ps1`, `client/src/api/agentTasks.ts` + `agents.ts` (field only).

`bool IgnoreModelDisabled = false` on both request records, same "sent only when chosen" convention as `ignoreSubscriptionQuota`.

- Flag false + held alias → 409 (unchanged).
- Flag true + held alias → **200**, task `Queued` (create) or start **still refused** for a named-agent Start whose resolved alias is held? **Start is a launch.** Start with the flag still 409s — the only safe "ignore" is create-and-queue. Document: `-IgnoreModelDisabled` is a **delegate.ps1 / POST /api/agent-tasks** switch, not a start-anyway switch. `StartAsync` never ignores; AlwaysOn Fable stays down.
- Warning event on the task: `fable is held until {until}; queued, will dispatch when the hold clears (ignoreModelDisabled).`
- Dispatcher skip **unchanged** — no per-task override column.

`delegate.ps1 -IgnoreModelDisabled` sets the JSON property only when the switch is present (`DelegateScriptKindTests` pattern).

No new `AgentTask` column.

### S3 — Client: hold panel + attention visual + Clear

**Files:** `client/src/api/modelAvailability.ts` (new), `client/src/api/attention.ts` (`ModelAvailabilityHold` union member if CARD-0022 S4 did not add it), `attentionVisuals.ts` + `.test.ts` totality list, new `client/src/features/orchestrator/ModelAvailabilityPanel.tsx` on the existing attention tab.

- Panel: table of current holds (kind, alias, source, until or "until cleared", reason) and the available list. Actions: datetime-local + Hold, Clear. Calls PUT/DELETE. Empty state is one line: "All models available."
- Attention kind 24: label "Model held", color `danger`/`error`, hint names skip + 409. Action `ClearHold` **only if we add** `AttentionAction.ClearHold = 10` mapping to DELETE. CARD-0035's "actions name existing endpoints" rule: add the enum member when DELETE exists. Client button on the attention row calls DELETE for that hold's kind/alias (encode them on the item: CARD-0022's attention row must carry kind+alias in evidence or we add optional `modelKind`/`modelAlias` fields on `AttentionItemDto` — **prefer optional additive fields** over parsing evidence text). If CARD-0022's DTO has no place for kind/alias, this card adds `string? ModelKind`, `string? ModelAlias` to `AttentionItemDto` (null on every other kind).
- Totality test must list `ModelAvailabilityHold`. Mobile home band 1: if it exhaustively switches, add the case; if it already groups by severity, it follows.
- **No** 409 confirm-dialog "launch anyway" (CARD-0136 deferred that too). The panel and the 409 detail are the surface.

Viewport: desktop orchestrator tab is the operator tool; the panel is a short table, check it wraps at a narrow width. No new route.

### S4 — Docs

- `docs/antiphon-api.md`: add `model_disabled` to the 409 code list; document GET/PUT/DELETE `/api/model-availability` and `ignoreModelDisabled` (create only).
- `docs/agent-kinds.md` next to the CARD-0136 409 paragraph: per-model hold, Manual vs AutoDetected, create 409 with available list, dispatcher skip of queued work, `-IgnoreModelDisabled` queues, Start never ignores, no silent reroute.
- One sentence in `docs/orchestration-loop.md` §2 Launching: if `delegate.ps1` 409s `model_disabled`, pick an alias from `available` or wait until `disabledUntil`; do not retry the same kind/tier.
- CARD-0022 plan execution notes already point here; no edit required unless S0 imported the table — then leave a pointer that AutoDetected writer is still CARD-0022 S2.

---

## What this card does not do

- A second pause table, `UsageLimitState`, or fleet-wide pause.
- CARD-0022 wall subclass parser / FakeClaude fixtures / AutoDetected **writer** (S0 may import the table and skip).
- Spawn-anyway into a held model.
- Disconnecting live sessions.
- CARD-0090 fallback chains / auto kind-switch.
- CARD-0305 routing pins (they consume `Require` when they exist).
- CARD-0304 pipeline field for holds.
- Per-project / per-board holds (fleet-wide, like the table).
- Clear-on-next-successful-health-probe.
- Polling `/usage` or `/usage-credits`.
- Remaining-quota %.
- `card.ps1` verbs for holds.

---

## Test matrix

| Layer | Test |
|---|---|
| Application | **S1 PUT** Manual fable until Thursday → `IsHeld` true for fable, false for opus/Grok; create Frontier 409 `model_disabled` with `source=Manual` and available list; create Grok Frontier 200 |
| Application | **S1 open-ended** PUT without until → held until DELETE; DELETE → 204 and create 200 |
| Application | **S1 `*`** PUT ClaudeCode `*` → fable **and** haiku held; Grok available; DELETE `*` restores Claude aliases even if no per-alias rows |
| Application | **S1 outrank** Manual Thursday + AutoDetected 18:10 today does not shorten; AutoDetected + later Manual converts Source; DELETE of AutoDetected unpauses |
| Application | **S1 expiry** Manual `DisabledUntil` 1 s ago + `IsHeld`/`Require` → not held (lazy clear); sweep sets `ClearedAt` |
| Application | **S1 dispatcher** queued fable + Manual hold → skip; sonnet on same tick dispatches; after until, fable dispatches |
| Application | **S2** create with `ignoreModelDisabled` → 200 Queued, Warning event, **not** Dispatched while held; Start with the flag still 409; omit flag → 409 |
| Script | `hold`/`clear`/`get` POST bodies; `delegate.ps1 -IgnoreModelDisabled` sends the property; omitted sends nothing |
| Application | **S3 DTO** attention item for a Manual hold carries kind/alias; gone after clear |
| Client vitest | `attentionVisuals` totality includes `ModelAvailabilityHold`; panel Hold/Clear calls (mock fetch) |
| HTTP | GET/PUT/DELETE camelCase; 422 unknown alias; 422 past until; 204 double-clear |

Run per `docs/testing-and-build.md`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0309/ -- --treenode-filter "/*/*/ModelAvailability*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0309/ -- --treenode-filter "/*/*/AgentTask*/*"
pwsh -File scripts/test-client.ps1
```

Forward slash on OutputPath. Sequential with Pty if S0 touched dispatcher. Delete `bin-card0309*` after.

---

## Sequencing and risks

**Order: S0 (if needed) → S1 (writer + 409 + script) → S2 (ignore) → S3 (client) → S4 (docs).** S1 is the card. S2 is the park-until-Thursday hatch. S3 is the UI the card asked for. One PR if S0 is a no-op; if S0 imports the table, still one PR so Manual disable works the same day (the operator pain is this afternoon's fable hold, not the parser).

If a parallel CARD-0022 Code agent is in flight: rebase onto it; do not merge a second `ModelAvailabilityHolds`. The unique index will tell you.

| Risk | Standing |
|---|---|
| S0 duplicates CARD-0022 | Import the spec, skip the parser. CARD-0022 S2 then adds AutoDetected on the same table. Outrank test is the handshake. |
| `*` vs per-alias both active | Union (held if either). DELETE `*` does not clear a leftover fable row — document; `get` shows both. |
| `ignoreModelDisabled` mistaken for launch | Tests pin Queued + skip. Start ignores the flag. Name stays because it names the **create** rule it opts out of. |
| AlwaysOn Fable orchestrator stays down | Intended. Operator starts it on another model or waits. No silent reroute. |
| Naive `-Until 2026-09-04T00:00` as local | Script refuses missing offset. Server 422s past UTC. |
| AttentionKind 24 taken | Re-read `AttentionDtos.cs` at code time. Append, never renumber. |
| Live Fable sessions keep running | CARD-0022: do not disconnect. New creates 409; queued skip. |
| CARD-0305 pin of held fable | Pins consume `Require`; they do not write holds. A Required pin on a held alias is still 409 `model_disabled` with the available list plus a coda that the list does not satisfy the pin. |

---

## Execution notes

- After deploy, `model-availability.ps1 hold -Kind ClaudeCode -Model fable -Until <Thursday 00:00Z>` is the CARD-0301 memory-file replacement. The next `delegate.ps1 -Role Plan` (Frontier → fable) 409s with the available list; `-Kind Grok` proceeds.
- Do not Reply/Cancel Check-interpreter tasks as a workaround. Do not hand-edit agent ModelId to dodge the gate.
- CARD-0022 Code, when it lands, writes AutoDetected onto the same rows; the outrank tests are the contract it must keep green.
- CARD-0305 pins consume `Require`; they do not write holds. `ignoreRoutingPin` and `ignoreModelDisabled` stay distinct flags.
