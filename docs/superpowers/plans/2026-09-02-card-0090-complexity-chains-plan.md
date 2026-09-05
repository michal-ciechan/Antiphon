# CARD-0090 — Complexity chains: Hard/Medium/Easy ordered (kind, level) fallback, consume holds, Block-for-a-human when exhausted

**Date:** 2026-09-02 (Plan pass, task 91e797fa — design only; no production code changed)
**Card:** CARD-0090 "Complexity-tiered delegation: Hard/Medium/Easy agent+model fallback chains, escalate to the user when none are available"
**Consumes (shipped, verified this pass):** CARD-0022 + CARD-0309 `ModelAvailabilityHold` / `ModelAvailability.IsHeldAsync` / `RequireAsync` / `GET /api/model-availability` / dispatcher `SkippedModelAvailability`; CARD-0305 `RoutingPin` / `RoutingPinService.ResolveAsync` / `EnforceForbiddenAliases` / provenance Human-vs-Auto; CARD-0136 `SubscriptionQuotaGate`; CARD-0099 Codex as a delegate kind; CARD-0084 `ModelLevelAliases.For(kind, level)`; CARD-0072/0022 `ApiErrorRecoveryService.ApplyWallAsync` + `AgentTaskReplyService.HandleApiErrorTurnAsync`.

---

## Addendum (2026-09-02, CARD-0322 Plan pass, task 084537e1) — build the walker list-in

CARD-0322 (`docs/superpowers/plans/2026-09-02-card-0322-routing-pin-candidates-plan.md`) is the second list source for the walker below: a routing pin's ordered `Candidates`. To keep it a *source* and not a second walker, build `WalkAsync` as two pieces from day one — the Code pass reads that plan's §"Walker contract" before writing `ComplexityRoutingService`:

- a pure, DB-free `RoutingCandidates.Compose(pinDecision, chain?, requestKind?, requestLevel?, resolveAgainstRolePolicy)` → ordered, deduped complete pairs + `Source` (`chain:Hard` | `pin:CARD-0301 Plan` | …) + per-candidate `Origin` (`pin` | `chain` | `rolePolicy`) + `Walked` (count ≥ 2). The Required-pin-wins / Preferred-pin-prepends rows of §Decision 5 live here.
- `WalkCandidatesAsync(IReadOnlyList<Candidate>, WalkContext, ct)` — the four filters of §Decision 3, in order, with no knowledge of where the list came from. `WalkAsync(complexity, …)` is `Compose` + `WalkCandidatesAsync`.

`Walk` and `ComplexityRoutingDto` carry `Source` and `Origin`. Nothing else in this plan moves. Also note: the enum tails quoted under "Ground truth" are stale — `AgentTaskEventType` now ends at `LandRefused = 22`; append after that, re-read at code time. The `DelegationSettings.ComplexityChains` shipped defaults under §Entities are superseded by an operator decision relayed to the CARD-0322 pass on 2026-09-02: defaults stay EMPTY until a human sets them (routing policy changed nine times in three days), and auto-resume onto an already-listed candidate when capacity returns is wanted. That decision is not yet on CARD-0090's revision history; the orchestrator should record it there before the Code pass starts.

---

## The card is stale on two points — read this first

| Card says (2026-08-19) | True today (2026-09-02, checked in code) |
|---|---|
| "No existing fallback-on-unavailability mechanism at all … detecting out-of-tokens is manual and reactive" | **Closed.** `ModelAvailabilityHold` table exists; CARD-0022 writes `AutoDetected` rows from parsed wall stubs (per-model, two subclasses), CARD-0309 writes `Manual` rows (`PUT /api/model-availability/{kind}/{alias}`, alias `*` kind-wide). `AgentTaskService.CreateAsync` and `AgentControlService.StartAsync` 409 `model_disabled` with an `available` list; `AgentTaskDispatcher.TickAsync` skips already-queued work whose alias is held; `AttentionKind.ModelAvailabilityHold = 24` rows show each hold with a `ClearHold` action. |
| "`delegate.ps1 -Kind` only accepts ClaudeCode/Grok; Codex is not a dispatchable delegate kind; 'Medium → Codex' is not achievable" | **Closed by CARD-0099 (Done 2026-08-30).** `scripts/delegate.ps1:40` is `[ValidateSet('ClaudeCode', 'Grok', 'Codex')]`; `AgentTaskService.DelegatableKinds = [ClaudeCode, Grok, Codex]`; `ModelLevelAliases.ForCodex` maps Frontier → `gpt-5.6-sol`, High → `gpt-5.6-terra`, Medium/Low → `gpt-5.6-luna`. **"Medium → Codex" is achievable today with no prerequisite work.** |

Everything below therefore builds **one** thing: an ordered candidate list per complexity tier that the create path and the dispatcher walk, skipping candidates the *existing* availability signals say are unusable, and a durable, visible terminal state when the whole list is unusable. No second detection mechanism, no second pause table, no probing.

---

## Decision

### 1. Complexity is a caller-declared axis; the chain replaces kind/level resolution for that dispatch only

`-Complexity Hard|Medium|Easy` on `delegate.ps1` → `complexity` on `POST /api/agent-tasks`. Omitted = today's behaviour, byte-for-byte. No LLM pre-classification (card Q1): every other axis in this system is caller-chosen and consistent; auto-classification is CARD-0303's open question and stays there.

**Composition with Role (card Q5, "the single biggest fork"):** the chain answers *which (kind, level)*; Role keeps answering everything else — `RolePolicy` timeouts, `RecommendedInFlight`, `EscalateTo`, the check schedule, brief text, Test/Debug boundary. When a chain applies, `ResolveLevel` / `ResolveAgentKind` are **not** consulted for kind/level (a chain candidate is a complete pair — the chain's author already encoded the tier). One chain per complexity tier, **not** a per-(complexity, role) matrix: the requester's example is per-complexity, and a 3×11 matrix is config nobody maintains. If a role-specific chain is ever wanted it is a card pin or stage pin with candidates (see §"Follow-up card").

`-Complexity` **with** explicit `-Kind`/`-Level` is **422**: an explicit pair is a single candidate the caller chose, and the shipped rule (CARD-0309, CARD-0305) is that an explicit choice is never silently rerouted. One or the other.

### 2. The chain is its own small table with routing-pin provenance — not a new grain on `RoutingPins`, not appsettings

Checked both shipped surfaces against the ask:

- **`RoutingPin`** is keyed `(CardId?, Role)` and answers "for this card+stage, which single (kind, level) SHOULD run". Making it carry a complexity grain means a nullable `Role`, a third filtered unique index, and every pin reader (`FindActiveAsync`, the dispatcher's `NotBefore` lookup, `ListAsync`, the pipeline DTO, `routing-pin.ps1`) learning to skip chain rows. The CARD-0305 plan explicitly kept pins single-valued and named the chain as CARD-0090's. Do not shoehorn.
- **`DelegationSettings.RolePolicy`** (appsettings) is the provenance-less fallback. The operator's routing preference changed **nine times in three days** (`feedback_prefer_grok_dispatch.md`, 2026-08-30 → 2026-09-01); a config edit + AppHost restart is the wrong cadence, and the CARD-0305 plan already ruled that `RolePolicy.Kind` "is a config deploy, not a Human pin".

So: **`ComplexityChains` table**, keyed by `Complexity`, one active row per tier, carrying `Provenance` (reuse `RoutingPinProvenance` — Human/Auto, same overwrite rule: Auto never replaces Human, 409 `complexity_chain_human`), `Reason`, `NotAfter` lazy expiry, ordered `Candidates`. **Config defaults** in `DelegationSettings.ComplexityChains` fill a tier with no active row (exactly RolePolicy's relationship to pins). This *is* the routing-pin mechanism extended — same provenance enum, same overwrite rule, same Reason cap, same lazy expiry, same script shape, same 409 family, and (the important part) **one resolver**: a pin contributes a 1-candidate list, a chain an N-candidate list, and the same filters run over either. It is not a third mechanism; it is the list-shaped case of the second one, in a table whose key matches its grain.

### 3. "Unavailable" is defined by signals that already exist (card Q2/Q3)

A candidate `(kind, level)` → `alias = ModelLevelAliases.For(kind, level)` is **skipped** when any of these hold, checked in this order, each recorded per candidate:

1. `kind ∉ AgentTaskService.DelegatableKinds`, or Orchestrator task and `kind != ClaudeCode` (the existing clamp) — *"not a delegate kind"*.
2. Stage-wide pin `ForbiddenAliases` contains `alias` (CARD-0305 rule, same Human-card-pin exemption) — *"forbidden by stage pin"*.
3. `ModelAvailability.IsHeldAsync(kind, alias)` — *"held until … (manual|session-limit|per-model cap)"*. This is CARD-0022's detected walls **and** CARD-0309's manual holds in one call. This is the whole "out of tokens" answer; no new detection.
4. `SubscriptionQuotaGate.EvaluateAsync(kind, key)` returns a refusing verdict and `ignoreSubscriptionQuota` is false — *"quota N% remaining, resets in …"*. This is the requester's "above some limit", precisely: CARD-0136's threshold, nothing invented.

**Not** unavailable: slow, expensive, `MaxCostUsdPerRoot` (per root, not per model), `RecommendedInFlight` (advisory WIP, CARD-0304), `MaxConcurrentTasks`. Do not add a cost/latency arm.

Proactive **and** reactive, one predicate (card Q3): proactive at create and again at dispatch (§Dispatcher); reactive because CARD-0022 already writes the hold when a turn dies on a wall — the reroute hook (S5) simply re-runs the same predicate afterwards. No `/usage` polling.

### 4. Exhausted = **Blocked for a human**, never a guess (card Q4)

A create that delegated the choice to a chain and finds **no** candidate available does **not** 409 by default and does **not** pick from another tier. It returns **200** with the task in `Status = Blocked`, no session, `FailureReason = "routing exhausted: Hard chain …"`, one `Blocked` event listing every candidate and why it was skipped, the parent's existing blocked-completion note (`EnqueueBlockedParentNoteAsync`), and an `AttentionKind.RoutingExhausted = 25` Error row. Why Blocked rather than CARD-0309's 409: a 409 hands the decision back to the *caller*, and the caller is usually an orchestrator agent — which would then pick a kind itself, i.e. exactly the silent fallback the requester forbade. The task row is the durable decision request; the human answers it with `Retry` (re-walks the chain), the new `Reroute` (an explicit pick), `Cancel`, or by clearing a hold. `refuseIfExhausted: true` (`-RefuseIfExhausted`) is available for scripted callers who want the 409 `routing_exhausted` instead.

When capacity returns (a hold expires or is cleared, a quota sample recovers), the dispatcher tick **re-walks the chain for Blocked-for-routing tasks and requeues them** with a `Rerouted` event. That is the same semantics CARD-0309 gave `ignoreModelDisabled` ("sit queued until the hold clears, then dispatch") — the chain's own first available candidate is not a guess. A human who does not want that cancels the task.

Not a card column, not an alert sink, not `CardStatus.NeedsDecision`: a fleet-capacity fact would flip every queued card at once, and the card-lifecycle rule is that decisions live on the attention feed. If the operator later wants the phone badge to count these, `AttentionSummaryDto.Decisions` can include kind 25 in one line.

### 5. Pins outrank chains; explicit outranks everything

| Situation | Result |
|---|---|
| `-Complexity Hard`, no pin | walk the Hard chain |
| `-Complexity Hard`, **Required** pin (card or stage) naming kind/level | the pin's pair is the **only** candidate. Held → 409 `model_disabled` + pin coda exactly as today. Warning event `complexity chain bypassed by the human required … pin`. Never reroute a Human instruction. |
| `-Complexity Hard`, **Preferred** pin | pin's pair (kind ?? chain-head kind, level ?? role level) is **prepended** as candidate 0, then the chain. Skipped like any other if held. |
| `-Complexity Hard`, stage pin `ForbiddenAliases=fable` | fable candidate skipped (not 409) — the chain has somewhere else to go. Human card pin exemption unchanged. |
| explicit `-Kind`/`-Level` + `-Complexity` | **422** |
| explicit `-Kind`/`-Level`, no `-Complexity`, held | 409 `model_disabled` — **unchanged** |
| no `-Complexity`, no `-Kind`, RolePolicy resolves to a held alias | 409 `model_disabled` — **unchanged** (RolePolicy is a single answer, not a chain; the operator opts into fallback by declaring complexity) |

---

## Ground truth (checked, not guessed)

- `AgentTaskService.CreateAsync` order today (`server/Application/Services/AgentTaskService.cs:312-437`): card bind → `RoutingPinService.ResolveAsync` → standing-agent kind settle → `ResolveLevel` / `ResolveAgentKind` → `EnforceForbiddenAliases` → `SubscriptionQuotaGate.EnforceAsync` → `ModelAvailability.RequireAsync` (or `GetActiveHoldAsync` warning when `ignoreModelDisabled`) → insert. The chain walk slots in **where `ResolveLevel`/`ResolveAgentKind` run**, and the three gates after it become per-candidate filters instead of a single throw. For a non-chain create, nothing moves.
- `AgentTaskDispatcher.TickAsync` (`:384-441`) skips a queued task on pin `NotBefore`, then on `IsHeldAsync(task.AgentKind, ResolveDispatchAliasAsync(task))`. Both leave the task Queued with a `Held` event. The chain re-walk goes **inside** the second branch, only for `task.Complexity != null`.
- `ApiErrorRecoveryService.ApplyWallAsync` (`:329-390`) already upserts the AutoDetected hold **before** resolving the row (`WallModelPaused`, or `NextAttemptAt = reset + 2 min` for SessionLimit). `AgentTaskReplyService.HandleApiErrorTurnAsync` then Fails the open task on a terminal reason, or defers it on a scheduled resume. By the time either runs, `IsHeldAsync` already excludes the walled alias — so the reactive reroute is a re-walk, not a parse.
- `RequeueAsync` (`AgentTaskService.cs`) stops the delegate, increments `Attempt`, drops the ephemeral agent, keeps `Result`/`FailureReason` as the next attempt's handoff, and re-queues on the **same row** with new kind/level fields intact. Worktree fields survive, so a rerouted attempt lands in the same worktree — which is the manual "redispatch on Grok pointed at the existing diff" pattern the operator used twice on 2026-09-01.
- `BlockRepeatAsync` (`:1649`) is the precedent for a Blocked task with **no session**: `Status = Blocked`, `FailureReason` text, `AgentSessionId = null`, `Blocked` event, parent note. `AttentionService.BuildBlockedAsync` today lists every Blocked non-Check task as `BlockedQuestion` with a `Reply` action — routing-blocked tasks must be **carved out** of that (a Reply has no session to land in) and given their own kind.
- Enum tails today: `AttentionKind.ModelAvailabilityHold = 24` (next 25), `AttentionAction.ClearHold = 10` (next 11), `AgentTaskEventType.ApiErrorDeferred = 15` (next 16), `AgentTaskFailureCode` ends at `CompletedWithoutProgress = 2`. Append, never renumber; re-read at code time.
- `AttentionItemDto` already carries optional `CardId`, `BoardId`, `ModelKind`, `ModelAlias` — no DTO change needed for the new row beyond using them.
- `delegate.ps1 -Pin` writes a Human Required pin from the **resolved** kind/level. With `-Complexity`, `-Pin` must pin what the chain chose (the resolved pair) — same code path, and it means "next time, exactly this", which correctly *removes* the card+stage from chain fallback. Print that consequence.
- `AgentTaskPipelineStageDto.Blocked` (CARD-0304) already lists Blocked tasks per stage; an additive optional field marks the routing-exhausted ones.

---

## Entities

### `TaskComplexity` (new enum, `server/Domain/Enums/AgentTaskEnums.cs`)

```
Hard = 0, Medium = 1, Easy = 2
```

Wire is the member name (`"Hard"`), same rule as `AgentModelLevel`. `Medium` collides in spelling with `AgentModelLevel.Medium` on purpose (the requester's word); they are different JSON fields and different script parameters (`-Complexity Medium` vs `-Level Medium`). Say so in the doc.

### `AgentTask.Complexity` (new nullable column)

`TaskComplexity? Complexity`. Non-null = "this task's kind/level was chosen by a chain and may be re-chosen by it" (dispatch re-walk, reactive reroute, Blocked→Queued resume). Set null by `Reroute` (an explicit human pick ends chain governance for that task). Migration `AddAgentTaskComplexityAndComplexityChains`.

### `ComplexityChain` (new table `ComplexityChains`)

| Column | Type | Notes |
|---|---|---|
| `Id` | guid | |
| `Complexity` | `TaskComplexity` | one active row per value |
| `CandidatesJson` | string | ordered `[{"agentKind":"ClaudeCode","modelLevel":"Frontier"}, …]`; 1..8 entries; duplicates (same kind+level) 422; every kind ∈ `DelegatableKinds` |
| `Provenance` | `RoutingPinProvenance` | reuse; Human outranks Auto |
| `Reason` | string | capped 400 |
| `NotAfter` | DateTime? | UTC; lazy self-clear (a temporary "Grok for everything until Friday") |
| `SourceTaskId` | guid? | audit, from `X-Antiphon-Task-Token` |
| `CreatedAt` / `UpdatedAt` / `ClearedAt` | | active = `ClearedAt == null` |

Filtered unique index `IX_ComplexityChains_Complexity_Active` on `(Complexity)` WHERE `ClearedAt IS NULL`.

### `DelegationSettings.ComplexityChains` (config defaults)

`Dictionary<string, List<ComplexityCandidate>>` (`ComplexityCandidate { AgentKind Kind; AgentModelLevel Level; }`), consulted only when no active row exists for the tier. Shipped defaults — the requester's heads (Hard → fable, Medium → Codex, Easy → Grok) with fallbacks taken from their own 2026-08-25 four-way split, **not** an invention:

```json
"ComplexityChains": {
  "Hard":   [ {"Kind":"ClaudeCode","Level":"Frontier"}, {"Kind":"ClaudeCode","Level":"High"}, {"Kind":"Codex","Level":"Frontier"}, {"Kind":"Grok","Level":"Frontier"} ],
  "Medium": [ {"Kind":"Codex","Level":"High"},          {"Kind":"Grok","Level":"Frontier"},     {"Kind":"ClaudeCode","Level":"High"} ],
  "Easy":   [ {"Kind":"Grok","Level":"Frontier"},       {"Kind":"Codex","Level":"Medium"},      {"Kind":"ClaudeCode","Level":"Medium"} ]
}
```

(fable → opus → sol → grok-4.6; terra → grok-4.6 → opus; grok-4.6 → luna → sonnet.) Startup validator: 1..8 candidates, delegatable kinds, no duplicate pairs, all three tiers present. **The operator is expected to `PUT` over these as Human on day one** — the execution notes say so.

---

## Resolution — `ComplexityRoutingService.WalkAsync`

New `server/Application/Services/ComplexityRoutingService.cs` (reads chains, walks candidates). Depends on `ModelAvailability`, `SubscriptionQuotaGate?`, `RoutingPinService?`, `IOptions<DelegationSettings>`, `TimeProvider`.

```
record Candidate(AgentKind Kind, AgentModelLevel Level, string Alias);
record CandidateOutcome(Candidate Candidate, string Outcome /* chosen|skipped */, string? Reason);
record Walk(TaskComplexity Complexity, RoutingPinProvenance? ChainProvenance, string ChainSource /* pin|config */,
            IReadOnlyList<CandidateOutcome> Outcomes, Candidate? Chosen, IReadOnlyList<string> Available);
Task<Walk> WalkAsync(TaskComplexity complexity, AgentTaskKind taskKind, AgentTaskRole role,
                     RoutingPinService.Decision pin, Guid? cardId, Agent? subscriptionOwner,
                     bool ignoreSubscriptionQuota, CancellationToken ct);
```

Steps: load active chain row else config default → build candidate list (Required pin → single; Preferred pin → prepend) → for each candidate apply filters §Decision 3 in order, recording the first reason → `Chosen` = first survivor → `Available` = `ModelAvailability.ListAvailableAsync` (the same list the 409 carries today, for the operator sentence).

### In `CreateAsync`

```
bind card
pinDecision = RoutingPinService.ResolveAsync(...)            // unchanged
if request.Complexity is null:
    level/kind = ResolveLevel/ResolveAgentKind (unchanged)  →  EnforceForbiddenAliases → quota gate → Require   // unchanged
else:
    422 if request.AgentKind or request.ModelLevel is set
    walk = ComplexityRoutingService.WalkAsync(...)
    if walk.Chosen is null:
        if request.RefuseIfExhausted → throw RoutingExhaustedException (409 routing_exhausted, extension complexityRouting)
        else → insert task Blocked (FailureReason = RoutingExhaustedPrefix + sentence), Blocked event with the walk, parent note, return 200 (Status Blocked, Warning = sentence, Routing = walk)
    else:
        agentKind/level = walk.Chosen; Warning += "skipped …" when any Outcome is skipped
        quota gate: already applied per candidate — do NOT re-Enforce (it would throw for an ignore-less override case already filtered)
        Require: already applied per candidate — skip the throw path; keep the ignoreModelDisabled branch unreachable (chain + ignoreModelDisabled is 422: "a chain skips a held candidate; there is nothing to ignore")
insert task snapshot (AgentKind, ModelLevel, Complexity)
Created event detail += " complexity=Hard candidate 3/4 grok-4.6; skipped fable (held until 2026-09-04T00:00:00Z, manual), opus (quota 4%, resets in 2h), gpt-5.6-sol (held, per-model cap)"
```

`AgentTaskCreatedDto` gains optional `Complexity` and `Routing` (the `Walk` as a DTO: `ComplexityRoutingDto { complexity, chainProvenance, chainSource, candidates:[{agentKind, modelLevel, alias, outcome, reason}], available }`). The same DTO is the `complexityRouting` problem-details extension on the 409 and the body of the Blocked event.

### Dispatcher (inside the queued foreach, replacing the plain `IsHeld` skip for chain tasks)

```
if task.Complexity is null → existing behaviour (Held event, SkippedModelAvailability++)
else if IsHeld(task.AgentKind, alias) or quota would refuse:
    walk = WalkAsync(task.Complexity, …, pin for task.CardId/Role, …)
    if walk.Chosen is { } c and (c.Kind, c.Level) != (task.AgentKind, task.ModelLevel):
        task.AgentKind = c.Kind; task.ModelLevel = c.Level; Rerouted event "fable held → grok-4.6 (Hard chain 4/4) at dispatch"; fall through to spawn
    else if walk.Chosen is null:
        task.Status = Blocked; FailureReason = routing exhausted sentence; Blocked event; parent note; TickResult.BlockedRoutingExhausted++; continue
```

Plus, once per tick before the queued loop: `ResumeRoutingBlockedAsync` — for every `Status == Blocked && Complexity != null && FailureReason starts with RoutingExhaustedPrefix`, `WalkAsync`; if `Chosen` → `Status = Queued`, set kind/level, `Rerouted` event "capacity returned: fable hold cleared; requeued on fable (Hard chain 1/4)", clear `FailureReason`, `TickResult.ResumedRoutingBlocked++`. Cheap (DB-only); at most a handful of rows.

A **Required** pin for the task's card+role is honoured in both places exactly as at create (single candidate; if it is held the task simply stays Queued with the existing `Held` event — a Human instruction is never rerouted by a tick).

### Reactive — `AgentTaskReplyService.HandleApiErrorTurnAsync` (S5)

Before the existing Fail/defer arms, when `task.Complexity != null` and the classification is `Wall` (both subclasses — the hold for the walled alias is already written by `ApplyWallAsync`): `AgentTaskService.RerouteOnWallAsync(task, walledAlias)`:

- Count prior `Rerouted` events on the task; if `>= chain.Candidates.Count` → fall through to Blocked (loop guard: each candidate at most once per task per wall cascade).
- `WalkAsync`; if `Chosen` → `RequeueAsync(task, Rerouted, level, detail)` with kind updated, detail: `"fable hit a usage wall (You've reached your Fable 5 limit…); rerouted to grok-4.6 (Hard chain 4/4). The prior attempt left NO report — check {WorkingDirectory or WorktreePath} for uncommitted work before redoing anything."` `RequeueAsync` kills the session and drops the ephemeral agent, as escalation does.
- If none → Blocked (routing exhausted), same as create.
- SessionLimit subclass: **switch immediately** when an alternative exists (the operator did exactly this both times on 2026-09-01 rather than waiting for 23:50); only when the chain has nothing else does CARD-0022's single same-session resume stay scheduled. Required-pinned tasks keep CARD-0022's behaviour untouched.

The `WallModelPaused` incident text gains one clause when a reroute happened: `"… rerouted to {alias} as task attempt {n}"`.

---

## HTTP / script / `delegate.ps1`

```
GET    /api/complexity-chains                      → { chains: [ { complexity, candidates:[{agentKind, modelLevel, alias, availableNow, unavailableReason}], provenance|null, source: "pin"|"config", reason, notAfter, updatedAt } ×3 ] }
PUT    /api/complexity-chains/{complexity}         upsert active row; body { candidates:[{agentKind, modelLevel}], provenance: "Human"|"Auto", reason?, notAfter? }
DELETE /api/complexity-chains/{complexity}         clear → config default again; 204 if already clear
POST   /api/agent-tasks/{id}/reroute               { agentKind, modelLevel }  — Blocked-for-routing or Queued only; sets Complexity = null, kind/level explicit, Rerouted event, Requeue; Require applies (409 model_disabled if the human picked a held alias)
```

GET evaluates each candidate's availability **now** (holds + quota) so the panel and the script can show *why* a chain is exhausted without a dispatch. 409 `complexity_chain_human` on Auto-over-Human. 422: unknown complexity, empty/duplicate/oversized candidates, non-delegatable kind, `notAfter` in the past.

`POST /api/agent-tasks` request gains `complexity` (`TaskComplexity?`) and `refuseIfExhausted` (bool, default false). `AgentTaskSummaryDto` gains `complexity` (chip: "Hard 3/4 → grok-4.6").

`scripts/complexity-chain.ps1` (ASCII-only, `ANTIPHON_API`, `card.ps1` verb shape):

```
complexity-chain.ps1 get   [-Json]                                 # shows each candidate with available/held/quota now
complexity-chain.ps1 set   -Complexity Hard -Candidates ClaudeCode/Frontier,Codex/Frontier,Grok/Frontier
                           [-Provenance Human|Auto] [-Reason r] [-NotAfter 2026-09-05T00:00:00Z]
complexity-chain.ps1 clear -Complexity Hard
```

`-Candidates` is `Kind/Level` pairs, comma-separated, order preserved. Not a `routing-pin.ps1` verb (that script is card+stage grain; this is neither).

`scripts/delegate.ps1`:

- `-Complexity Hard|Medium|Easy` (`[ValidateSet]`), sent as `complexity` only when chosen. Refused locally (exit 1, same wording as the 422) when combined with `-Kind`/`-Level`.
- `-RefuseIfExhausted` → `refuseIfExhausted: true`.
- `-Reroute <taskId> -Kind X -Level Y` → `POST …/reroute` (new parameter set alongside `-Retry`/`-Escalate`).
- Output after create: `routed Hard -> grok-4.6 (candidate 4/4); skipped fable (held until …, manual), opus (quota 4%), gpt-5.6-sol (held, per-model cap)`. For a Blocked result: `BLOCKED - routing exhausted: Hard chain has no available candidate: fable …; opus …; gpt-5.6-sol …; grok-4.6 …  A human decides: clear a hold (model-availability.ps1 clear), wait for a reset, or delegate.ps1 -Reroute <id> -Kind .. -Level ..  Do NOT pick a kind yourself.` — the last sentence is addressed to the orchestrator reading it.
- `-Pin` with `-Complexity`: pins the **chosen** pair as Human Required and prints `pinned … (this removes CARD-x Plan from Hard-chain fallback; clear the pin to restore it)`.

---

## Escalate-to-a-human surface (the terminal case, concretely)

| Layer | What appears |
|---|---|
| Task row | `Status = Blocked`, `AgentSessionId = null`, `Complexity` set, `FailureReason = "routing exhausted: Hard chain — fable held until 2026-09-04T00:00:00Z (manual); opus quota 4% (resets in 2h); gpt-5.6-sol held (per-model cap, no reset stated); grok-4.6 held (manual, no re-enable time)"`. `Blocked` event carries the full walk. |
| Parent session | Existing `EnqueueBlockedParentNoteAsync` completion note: `[task 7f3a2b91 blocked] routing exhausted … A human must choose; do not pick a kind yourself.` The orchestrator relays it to the operator's channel instead of guessing (rule added to `docs/orchestration-loop.md`). |
| Attention feed | `AttentionKind.RoutingExhausted = 25`, Severity **Error**, **one row per exhausted chain** (grouped, the `RecentCriticalIncident` discipline — five Hard tasks during one fable outage is one row, not five). `TaskId` = oldest blocked task, `CardId`/`BoardId` when every grouped task shares one card, else null. Title `Hard chain exhausted`; Headline the sentence above plus `N tasks waiting`; Evidence lists each task short id + card + role and each candidate's reason. Actions `[OpenDrawer, OpenCard]` (per-task Retry/Reroute/Cancel live in the task drawer). Rows disappear when the last blocked task is resumed, rerouted or cancelled — recency-is-lifecycle, no ack. |
| `BuildBlockedAsync` | **Excludes** routing-blocked tasks (they are not a question and have no session for `Reply`). |
| Pipeline (CARD-0304) | `AgentTaskPipelineBlockedDto.RoutingExhausted: bool` (additive, default false) so the phone view can label the row. |
| Client | `RoutingExhausted` in the `AttentionKind` union + `attentionVisuals` totality (label "Routing exhausted", `danger`); task drawer shows a **Reroute** control on a Blocked-for-routing task: kind/level selects populated from `GET /api/model-availability` `available` → `POST …/reroute`. `ComplexityChainPanel` on the orchestrator attention tab next to `ModelAvailabilityPanel`: three rows, candidates in order, each green/red with the live reason. **Read-only in v1**; writes are the script (CARD-0305 precedent). |
| Card | Unchanged. `CardWorkTransitionService` already treats Blocked as in-progress. No `NeedsDecision`, no column, no alert sink. |

Auto-resume when capacity returns (§Decision 4) writes a `Rerouted` event and the attention row goes away on its own.

---

## Slices

### S1 — Chain table, config defaults, service, HTTP, script

`TaskComplexity`; `ComplexityChain` + migration (also adds `AgentTasks.Complexity`); `DelegationSettings.ComplexityChains` + validator; `ComplexityChainService` (GET/PUT/DELETE, Human-overwrite rule, lazy `NotAfter`); `ComplexityRoutingService.WalkAsync` (pure walk over injected availability/quota/pins); `ComplexityChainEndpoints`; `scripts/complexity-chain.ps1`. Tests: PUT/GET/DELETE + 409/422 matrix; Auto-over-Human refused; config default used when no row; walk skips held / quota-low / forbidden / non-delegatable and picks the first survivor; walk with Required pin is single-candidate; Preferred pin prepends; GET shows per-candidate `availableNow`. Script HttpListener pins (`RoutingPinScriptTests` shape).

### S2 — Create path + `delegate.ps1 -Complexity`

`CreateAgentTaskRequest.Complexity` / `RefuseIfExhausted`; 422 on explicit+complexity and on `ignoreModelDisabled`+complexity; `CreateAsync` branch per §Resolution; `AgentTaskCreatedDto.Routing`; Created-event detail; `RoutingExhaustedException` (409 `routing_exhausted`, `complexityRouting` extension); Blocked-on-exhausted insert (repeat-block shape) + parent note; `delegate.ps1 -Complexity`, `-RefuseIfExhausted`, output lines, `-Pin` consequence line. Tests: `-Complexity Hard` + fable held → task on candidate 2 with Warning naming the skip; all held → 200 Blocked with FailureReason + Blocked event + parent note enqueued; `refuseIfExhausted` → 409 with extension; explicit+complexity 422; no complexity → every existing `ModelAvailabilityCreateTests` / `RoutingPinCreateTests` byte-identical; Required pin + complexity → pin pair, Warning `chain bypassed`; Preferred pin + complexity → pin pair first; stage forbid → skipped not 409; Orchestrator + Hard chain → non-Claude candidates skipped. `DelegateScriptKindTests` for the body property and the local 422.

### S3 — Dispatcher re-walk, Blocked→Queued resume, attention row, `Reroute`

Dispatcher branch + `ResumeRoutingBlockedAsync` + `TickResult` counters (additive, default 0); `AgentTaskEventType.Rerouted = 16`; `AttentionKind.RoutingExhausted = 25` grouped projection + `BuildBlockedAsync` carve-out; `POST /api/agent-tasks/{id}/reroute` + `delegate.ps1 -Reroute`; pipeline `RoutingExhausted` flag. Tests (`FakeTimeProvider`): Queued Hard task on fable, fable held before tick → dispatched on next candidate with `Rerouted` event; all held → Blocked; Blocked task + hold cleared → Queued with event; Required-pinned chain task + held → stays Queued with `Held` (never rerouted); attention: 3 blocked Hard tasks → one row, gone after resume; `BlockedQuestion` rows exclude routing-blocked; reroute on Blocked → Queued, `Complexity` null, kind/level explicit; reroute to a held alias → 409 `model_disabled`; reroute on Working → 409.

### S4 — Client + docs

Client: `AttentionKind` union + totality + label; `ComplexityChainPanel` (read-only); Reroute control in the task drawer; `complexity` chip on task cards (`homeTasksModel` if it renders kind/level). Docs: `docs/antiphon-api.md` (routes, `complexity`, `refuseIfExhausted`, `reroute`, 409 codes `routing_exhausted` / `complexity_chain_human`); `docs/orchestration-loop.md` §2 Tiers ("Complexity chains" paragraph: when to pass `-Complexity`, that explicit `-Kind` is never rerouted, and **on `routing exhausted` relay to the operator — never pick a kind yourself**); `docs/agent-kinds.md` gotcha next to the CARD-0022/0309 paragraph; `.claude/skills/antiphon-delegate/SKILL.md` options table row for `-Complexity`; note in the CARD-0022 plan's execution notes that CARD-0090 consumes `IsHeldAsync`. Replace the routing-habit memory's chronological log with `complexity-chain.ps1 set` lines (execution note).

### S5 — Reactive reroute on a usage wall

`AgentTaskService.RerouteOnWallAsync`; hook in `HandleApiErrorTurnAsync` ahead of the Fail/defer arms for `Complexity != null`; loop guard by `Rerouted` count; handoff sentence naming the checkout/worktree; incident clause. Tests: Fable-5 fixture stub (`ANTIPHON_FAKE_API_ERROR=rate_limit_model`) on a Working Hard task → hold written (already), task **Requeued** on the next candidate, session stopped, ephemeral agent removed, `Rerouted` event; session-limit stub → switch when an alternative exists, else CARD-0022's single resume unchanged; chain exhausted at the wall → Blocked not Failed; non-chain task → byte-identical to today (`ApiErrorRecoveryServiceTests` green); Required-pinned task → untouched; second wall on the rerouted attempt → next candidate; N+1th → Blocked. Runs under the Pty/session owner rules — read `docs/session-runtime-invariants.md` before touching `HandleApiErrorTurnAsync`; the kill goes through `StopDelegateAsync`, never a raw session kill.

**Ordering: S1 → S2 → S3 → S4 as one PR (the card's stated value: chain + skip + visible terminal). S5 as a second PR** — different owner area (session runtime), the highest-hazard slice (kills a session, requeues into a worktree with WIP), and independently valuable. If the Code pass is short on time, S5 is the slice to leave for a follow-up dispatch **on this card**, not to skip silently.

---

## Split verdict (the requester left it open)

**One card, not three.** Classification (`-Complexity`), the chain walk, and the Blocked-for-a-human terminal are one mechanism with one resolver; splitting them ships a chain that cannot say what happens when it runs out, or a terminal state nothing reaches. S5 is a second PR on the same card.

**One follow-up card, filed by this pass: CARD-0322** *"Ordered candidate lists on routing pins (stage/card)"* — the same `WalkAsync` over a pin's `Candidates` instead of its single kind/level. Deliberately **out** of CARD-0090: the requester asked for a complexity axis, and the operator's live role-shaped rules ("Plan → fable or opus", "Plan → Sol or Grok") are exactly what a multi-candidate *stage pin* would express — arguably more useful day-to-day than the complexity axis, and worth its own decision rather than smuggling in here. `WalkAsync` is written so that slice is a candidate-list source, not a second walker.

Auto-classification stays CARD-0303.

---

## What this card does not do

- A second availability signal, pause list, `/usage` or `/usage-credits` polling, remaining-% guessing.
- Rerouting an **explicit** `-Kind`/`-Level` or a **Required** pin, at create, dispatch, or on a wall. Ever.
- Falling back **across** tiers (Hard exhausted → try Medium's list). The requester forbade it; Blocked is the answer.
- A per-(complexity, role) matrix. Candidates on `RoutingPin` (follow-up card). Card-level default complexity.
- `CardStatus.NeedsDecision`, a card column, an alert-sink route, a Telegram push (whatever already forwards attention rows forwards this one; nothing new is wired).
- Changing `RolePolicy` defaults, `MaxConcurrentTasks`, `RecommendedInFlight`, or CARD-0022's wall parser / FakeClaude modes.
- A client editor for chains (script + 409 detail + read-only panel in v1, CARD-0305/0309 precedent).
- Rewriting Queued **non-chain** tasks when a chain or hold changes (CARD-0305 rule stands: they keep their snapshot).

---

## Test matrix (run per `docs/testing-and-build.md`; forward slash on OutputPath; sequential with Pty tests for S5)

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/*/ComplexityChain*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/*/ComplexityRouting*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/*/ModelAvailability*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/*/RoutingPin*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/*/AgentTaskDispatcher*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/*/AttentionServiceTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/*/ApiErrorRecovery*/*"      # S5
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0090/ -- --treenode-filter "/*/Antiphon.Tests.Scripts/*/*"
pwsh -File scripts/test-client.ps1
```

Delete `bin-card0090*` after. Shared-Postgres tests seed by id.

| Layer | Pin |
|---|---|
| Unit | `WalkAsync`: filter order (kind clamp → forbid → held → quota); first survivor; Required single; Preferred prepend; empty → `Chosen` null with every reason filled |
| Application | S1 HTTP/service matrix; config default vs row; Human-overwrite 409; `NotAfter` lazy clear |
| Application | S2 create matrix incl. **byte-identical no-complexity path** (existing 0022/0305/0309 create tests untouched and green) |
| Application | S3 dispatcher re-walk / Blocked / resume / Required-untouched; attention grouped row; `BlockedQuestion` carve-out; reroute matrix |
| Application | S5 wall reroute matrix; loop guard; non-chain unchanged |
| Script | `complexity-chain.ps1` bodies; `delegate.ps1 -Complexity` property, local 422, `-Reroute`, `-RefuseIfExhausted` omitted-when-unset |
| Client vitest | totality includes `RoutingExhausted`; panel renders three chains; reroute control posts |

---

## Risks

| Risk | Standing |
|---|---|
| Orchestrator reads the Blocked note and picks a kind itself | The note and `delegate.ps1` output both say "do NOT pick a kind yourself"; `docs/orchestration-loop.md` rule; the operator's own instruction is the reason. Cannot be enforced in code — it is a brief rule, stated where the agent reads. |
| Auto-resume surprises an operator who wanted the work parked | Same semantics CARD-0309 gave `ignoreModelDisabled`; `Rerouted` event is visible; Cancel is the park. Documented. |
| Reroute cascade loop on a flapping quota sample | Loop guard = chain length per task; quota gate only refuses on a **fresh** sample (CARD-0136), stale passes. |
| Kill of a Working session on a wall loses WIP | `RequeueAsync` → `StopDelegateAsync` is the escalation path already in production; worktree survives; handoff sentence names it. No raw kill. |
| SessionLimit switch abandons a resume that would have worked in 20 min | Operator did exactly this twice by hand; the chain exists to not wait. Only when the chain has an alternative; else CARD-0022 resume stands. |
| Config default chain is "wrong" for today's preference | It is Auto-level and `PUT`-over-able before first use; execution notes tell the operator to write the Human rows first. |
| `Medium` name collision | Different field/parameter; documented; `ValidateSet` catches a swapped flag. |
| Enum tails move before Code | Re-read `AttentionDtos.cs`, `AgentTaskEnums.cs` at code time; append. |
| S5 touches session-runtime invariants | Second PR; read the owner doc; tests on the Fable-5 and session-limit fixtures both ways. |

---

## Execution notes

- **Day one after deploy**, the operator writes Human rows (config defaults ship empty). Replace the routing-habit chronological log with these, e.g. today's live rule:
  ```
  complexity-chain.ps1 set -Complexity Hard   -Candidates ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier,Grok/Frontier -Provenance Human -Reason "plan-grade work: fable, opus, sol, then grok"
  complexity-chain.ps1 set -Complexity Medium -Candidates Grok/Frontier,Codex/High,ClaudeCode/High -Provenance Human -Reason "execute on grok; terra then opus if grok is out"
  complexity-chain.ps1 set -Complexity Easy   -Candidates Grok/Frontier,Codex/Medium,ClaudeCode/Medium -Provenance Human -Reason "cheap checks"
  ```
  Stage pins (`routing-pin.ps1`) keep working unchanged; a Required stage pin wins over the chain for that role, which is how "Plan is fable, full stop" coexists with `-Complexity`.
- Orchestrator habit: `delegate.ps1 -Role Plan -Complexity Hard -Card CARD-x` instead of `-Kind ClaudeCode -Level Frontier`; the latter remains correct when the operator named the model.
- **Keying (CARD-0332):** chains are now `(Role?, Complexity)`. A CARD-0090 row is the any-role
  fallback; a role cell outranks it as a whole. Walker contract unchanged. See CARD-0332.
- The CARD-0022 plan's "CARD-0090: unavailable = `ModelAvailability.IsHeld`; do not invent a second pause list" is honoured by construction — `WalkAsync` has no other availability input.
- Enum values, `available` list membership and 409 sentence shapes cite CARD-0022/0309; do not restate them differently here.

---

## Verification design

S5 only (S1–S4 already shipped). Added at Code because this plan predates the CARD-0146 section contract.

### Proves it works now
- V-1: Fable-5 wall on a Working Hard task requeues on the next candidate, kills the session through `StopDelegateAsync`, drops the ephemeral agent, writes `Rerouted` with the worktree/checkout handoff, and records `rerouted to {alias} as task attempt {n}` · integration · `ComplexityWallRerouteTests.Fable_5_wall_on_a_Working_Hard_task_requeues_on_the_next_candidate` · Queued on opus, `Stopper.Killed` contains the session, agent row gone, hold written, incident clause present
- V-2: Session-limit stub switches immediately when an alternative exists and cancels the scheduled same-session resume · integration · `Session_limit_with_an_alternative_switches_immediately` · Queued on next candidate, recovery `ResolvedReason = Rerouted`, no `ApiErrorDeferred`
- V-3: Session-limit stub with no alternative keeps CARD-0022's single resume · integration · `Session_limit_with_no_alternative_keeps_the_CARD_0022_resume` · stays Working, `NextAttemptAt` set, session not killed
- V-4: Chain exhausted at the wall Blocks, does not Fail · integration · `Chain_exhausted_at_the_wall_blocks_instead_of_failing` · `Status = Blocked`, `FailureReason` starts with `routing exhausted:`, parent note enqueued
- V-5: Non-chain task on Fable-5 is byte-identical to CARD-0022 · integration · `Non_chain_task_fails_on_Fable_5_as_today` plus `ApiErrorRecoveryServiceTests` · Failed with `WallModelPaused`, no `Rerouted`
- V-6: Required-pinned chain task is untouched · integration · `Required_pinned_task_is_untouched_on_a_Fable_5_wall` · Failed on fable, no `Rerouted`
- V-7: Second wall on the rerouted attempt takes the next candidate · integration · `Second_wall_on_the_rerouted_attempt_takes_the_next_candidate` · Grok after opus wall
- V-8: N+1th wall is Blocked by the loop guard · integration · `Nth_plus_one_wall_is_Blocked_by_the_loop_guard` · Blocked while opus is still available

### Guards the regression
- R-1: a Required pin is silently rerouted on a wall · caught by V-6 because `Rerouted` count must stay 0 and kind/level stay the pin pair
- R-2: a session-limit with nowhere else to go Blocks instead of scheduling CARD-0022's resume · caught by V-3 because status stays Working and `NextAttemptAt` is set
- R-3: a wall cascade loops past the chain length · caught by V-8 because 3 prior `Rerouted` events Block even though opus is free
- R-4: the wall path kills the session outside `StopDelegateAsync` / `RequeueAsync` · caught by V-1 because `RecordingSessionStopper.Killed` is the only kill seam the test sees
- R-5: a non-chain Working task is pulled into the chain walker · caught by V-5 because `Complexity` stays null and the task Fails with `WallModelPaused`

### Positive controls  (Build runs each: break, see red, revert, see green — and reports all three)
- PC-1: break the session-limit-no-alternative fallthrough by deleting `&& !sessionLimitHasScheduledResume` in `RerouteOnWallAsync`; expect `Session_limit_with_no_alternative_keeps_the_CARD_0022_resume` red
- PC-2: break the Required-pin guard by making `PinForbidsReroute` return `false`; expect `Required_pinned_task_is_untouched_on_a_Fable_5_wall` red
- PC-3: break the loop guard by deleting the `reroutedCount >= walk.Outcomes.Count` arm; expect `Nth_plus_one_wall_is_Blocked_by_the_loop_guard` red
- PC-4: break the kill-through-`StopDelegateAsync` contract by commenting out `await StopDelegateAsync(task, ct);` in `RequeueAsync`; expect `Fable_5_wall_on_a_Working_Hard_task_requeues_on_the_next_candidate` red (`Stopper.Killed` empty)

### Out of scope
- FakeClaude `ANTIPHON_FAKE_API_ERROR=rate_limit_model` through a live pty: the fixture text is the same parser input; the pty lane is CARD-0022's and stays green there (`FakeClaudeContractTests`). S5 is the settle-path re-walk.
- CARD-0322 pin candidate lists.
- Client / `delegate.ps1` (S4).

### Cost
- suites forced: `ComplexityWallRerouteTests`, `ApiErrorRecovery*`, `ComplexityDispatcher*`, `ComplexityCreate*`, `ComplexityRouting*`, `AgentTaskReplyIntegrationTests` API-error subset if a filter exists, else the class is too large — prefer the named S5 class plus `ApiErrorRecoveryServiceTests`
- verification floor ≈ 8 min (isolated-schema clones + reply settle path)
