# CARD-0022 — Per-model usage-limit pause (two wall subclasses), shared with CARD-0309

**Date:** 2026-09-01 (Plan pass, task 8dbd3497 — design only; no code changed)
**Card:** CARD-0022 "Notice usage-limit exhaustion, show what's left and when it resets, resume after"
**Diagnosis:** done on the card (Grok task 2ce34da8, 2026-09-01). This plan verifies that against the shipped code and designs the remaining slices.

**Sources (verified this pass):** CARD-0022 (incl. 2026-08-20 provider addendum and 2026-09-01 two-subclass verdict), CARD-0309 (Backlog, no plan), CARD-0090 / CARD-0304 / CARD-0305 (adjacent, not this card), spec `docs/superpowers/specs/2026-08-17-usage-limit-and-api-error-resilience.md` (D7 fleet-wide `UsageLimitState` is **superseded**), CARD-0071/0072/0083/0136/0143 shipped code, `ApiErrorClassifier` / `ApiErrorRecoveryService` / `ApiErrorRetrySchedule`, `SubscriptionQuotaGate`, `ProviderContractCatalog.UsageLimitSignal`, `ModelLevelAliases`, `AgentTaskDispatcher` skip loop, FakeClaude `ANTIPHON_FAKE_API_ERROR=rate_limit`, this machine's catalog (`Claude` Supported/structural, Grok/Codex Unknown).

---

## Decision

The 2026-08-17 spec's D7 (one `UsageLimitState` row, pause the whole fleet) is **wrong grain**. Live evidence: Fable 5 exhausted while Sonnet 5, Haiku 4.5, and every Grok session stayed healthy. Pause **that model on that kind**, leave every other model running.

There are two Wall subclasses, both still `ApiErrorClassification.Wall` structurally (`rate_limit` / 429). Text after classification decides recovery:

| Subclass | Measured text | Reset | Recovery |
|---|---|---|---|
| **Session-limit** | `"You've hit your session limit · resets 6:10pm (Europe/London)"` (CARD-0072 fixture / FakeClaude `rate_limit`) | Yes, in text | Parse it. Pause **the session's model** until `ResetAt + 2 min`. One same-session resume at that instant (spec S5b). |
| **Per-model cap** | `"You've reached your Fable 5 limit. Run /usage-credits to continue or switch models with /model."` (2026-09-01 incident, identical twice) | **None** | Pause that model with `DisabledUntil = null`. **Never** same-model-nudge (CARD-0072's 30-minute WallPrompt died in 1.1 s on this incident). Do not invent a Thursday. |

Unparseable Wall text is treated as the per-model cap, **not** the 30-minute ladder. Blind retry against an unknown wall is how the incident wasted a turn.

The pause is a durable **`ModelAvailabilityHold`** row keyed `(Kind, ModelAlias)`. CARD-0022 writes `Source = AutoDetected`. CARD-0309 (unplanned) writes `Source = Manual` onto the **same table**, same reader, same dispatcher skip, same create-time 409. This card ships the table, the auto writer, the skip, and the 409 so a detected hold actually fails fast; CARD-0309 adds PATCH/manual tooling and does not migrate again.

Do **not** implement CARD-0090 fallback chains. Do **not** poll `/usage-credits` (Forbidden until a headed probe proves it is read-only). Do **not** build the spec's singleton `UsageLimitState`. Do **not** claim Grok/Codex detection — catalog stays `Unknown`.

---

## What is already shipped (do not rebuild)

| Piece | Where | This card |
|---|---|---|
| Structural stub carriage `IsApiError` / `ApiErrorClass` / `ApiErrorStatus` | CARD-0072 S1 | Detection stays structural |
| `ApiErrorClassifier`: `rate_limit`/429 → `Wall` (text unused) | `ApiErrorClassifier.cs` | Keep. Subclass is a **second** parse |
| Reply/settlement guards (error text is not a report) | CARD-0071 | Unchanged |
| Transient/Unknown/NeedsHuman recovery ladder | CARD-0072 S5a, `ApiErrorRecoveryService` | Unchanged |
| Wall recovery today | `ApiErrorRetrySchedule.WallEntryRungMinutes = 30`, then hourly; incident `WallUnparsed` | **Replace** for both subclasses |
| Proactive quota samples + 409 `subscription_quota_low` | CARD-0143 / CARD-0136, keyed by **profile/kind**, Claude poll = Unknown | Different grain. Do not conflate. Gate stays. |
| `UsageLimitSignal` axis | CARD-0083 | Claude Supported; Grok/Codex Unknown. Update Claude's reason only |

`UsageLimitResetParser` and `UsageLimitState` **do not exist**. Spec S4/S6 were never built. This card owns them, redesigned.

---

## Shared state (this card defines it; CARD-0309 hooks here)

### Entity `ModelAvailabilityHold`

Table `ModelAvailabilityHolds`. Active = `ClearedAt == null`. Filtered unique index `IX_ModelAvailabilityHolds_Kind_ModelAlias_Active` on `(Kind, ModelAlias)` where `ClearedAt IS NULL`.

| Column | Type | Notes |
|---|---|---|
| `Id` | guid | |
| `Kind` | `AgentKind` | ClaudeCode today; Grok/Codex rows are legal so CARD-0309 can disable them by hand |
| `ModelAlias` | string | Canonical family alias: `fable`, `opus`, `sonnet`, `haiku`, `grok-4.6`, `gpt-5.6-sol`, … — **not** `claude-fable-5` |
| `Source` | `ModelAvailabilitySource` | `AutoDetected = 0`, `Manual = 1`. This card only writes 0 |
| `DisabledUntil` | `DateTime?` | UTC. Null = until cleared (per-model cap, or a CARD-0309 open-ended manual hold) |
| `HitAt` | DateTime | |
| `ClearedAt` | DateTime? | |
| `RawText` | string? | Stub text, capped |
| `SourceSessionId` | guid? | AutoDetected only |
| `SourceTaskId` | guid? | When the stub sat on a delegate |
| `Reason` | string | Short: `"session-limit resets 18:10 Europe/London"` / `"Fable 5 per-model cap (no reset stated)"` |

No remaining-% column. Claude has no measured remaining-quota readout. If a fresh CARD-0143 sample exists for that kind it may be **shown** on the attention row; it is not stored here (different grain: profile vs model).

### Canonical alias

New pure `ModelAlias.Normalize(kind, raw)`:

- Claude family names in TUI text: `"Fable 5"` / `"Fable"` / `"claude-fable-5"` / `"fable"` → `fable`. Same for opus, sonnet, haiku (include the measured `"Haiku 4.5"` form).
- Unknown text → null; caller then uses the session's launch alias (`session.EffectiveModelId` if it is already an alias, else `ModelLevelAliases.For(kind, agent-or-task.ModelLevel)`).
- The stub row itself is `model: "<synthetic>"` — **never** read the stub's `TranscriptEntry.Model` as the paused model.

Pin the Fable-5 incident string and the session-limit fixture string.

### Reader (the CARD-0309 hook)

Static/DI `ModelAvailability.IsHeld(kind, alias, now)` and `ListHeld(now)` / `ListAvailable(now)`.

- Held if an active row exists AND (`DisabledUntil` is null OR `now < DisabledUntil`).
- A sweep (or lazy check on read) sets `ClearedAt = now` when `DisabledUntil <= now`. Auto-resume is **by construction**, no Hangfire job.
- `ListAvailable` = every alias `ModelLevelAliases` knows for `DelegatableKinds`, minus currently held. This is the "what's still available" sentence CARD-0309 wants.

CARD-0309 later: `POST/PATCH` that upserts `Source = Manual` (a Manual row outranks AutoDetected on the same key — one active row; a later auto-detect refreshes `RawText`/`HitAt` but must **not** shorten a human `DisabledUntil`). This card does not implement that write path; it leaves the column and the outrank rule in the reader comments / a failing-or-skipped test named for CARD-0309 so the contract is visible.

### Create-time 409 (shipped here so the hold is not silent)

`AgentTaskService.CreateAsync` and `AgentControlService.StartAsync` (same two doors as CARD-0136), **after** kind/quota gates, call `ModelAvailability.Require(kind, alias)`.

- Active hold → `ConflictException` 409 `model_disabled`, detail: `fable is disabled until 2026-09-01T17:12:00Z (session-limit); available: opus, sonnet, haiku, grok-4.6, gpt-5.6-sol, gpt-5.6-terra, gpt-5.6-luna`. When `DisabledUntil` is null: `fable is disabled (per-model cap, no reset stated); available: …`.
- Problem-details extension `modelAvailability: { kind, modelAlias, disabledUntil, source, available: [...] }`.
- No override flag on this card (CARD-0136's `ignoreSubscriptionQuota` is a different rule). CARD-0309 may add `ignoreModelDisabled`.
- Internal AlwaysOn / channel / check-interpreter starts: they launch a **named agent**, not a tier alias chosen at create. Gate them on the agent's resolved alias too — a Haiku check interpreter must keep starting while fable is held. A Fable orchestrator AlwaysOn restart **is** refused unless CARD-0309 later adds an override; document that, do not silently reroute (CARD-0136 rule).

`delegate.ps1` already prints 409 detail. No client UI on this card.

### Dispatcher skip (the original "pause dispatch, resume automatically")

Tasks **already queued** when the wall hits must not spawn. In `AgentTaskDispatcher.TickAsync`, inside the queued foreach, after the concurrency/scope skips:

- Resolve `alias = ModelLevelAliases.For(task.AgentKind, task.ModelLevel)` (exact `agent.ModelId` wins when the task is pinned to a standing agent that has one).
- If `IsHeld` → increment `SkippedModelAvailability`, write one `Held` event the first time (same once-per-task trace as scope holds), `continue`. Task stays `Queued`.
- When the hold clears, the next tick dispatches it. That **is** automatic resume of queued work.

Add `SkippedModelAvailability` to `TickResult` (additive; existing callers ignore extra counters only if we keep the record compatible — add a field with default 0).

Check-role tasks: skip if the interpreter's alias is held (Haiku today — will almost never trip). Do not skip Checks because *fable* is held.

Do **not** change `MaxConcurrentTasks`. A fable hold must not starve sonnet slots.

---

## Slices

### S0 — Second fixture + FakeClaude mode (no behaviour change)

The session-limit shape is already FakeClaude `ANTIPHON_FAKE_API_ERROR=rate_limit` and `TranscriptNormalizerTests`. Add:

- Fixture `tests/Antiphon.Tests/Agents/Fixtures/api-error-fable-limit.jsonl` (or next to existing CARD-0072 fixtures): the 2026-09-01 stub, structural `error: rate_limit`, `apiErrorStatus: 429`, text exactly `"You've reached your Fable 5 limit. Run /usage-credits to continue or switch models with /model."`, `model: "<synthetic>"`.
- FakeClaude: `ANTIPHON_FAKE_API_ERROR=rate_limit_model` (or `rate_limit=fable`) emits that text. Default `rate_limit` stays the session-limit sentence. `FakeClaudeContractTests` pins both.
- `TranscriptNormalizerTests`: both stubs stamp `IsApiError` / class / status; text preserved.

Without this, every later slice is driving only the old wall.

### S1 — Wall subclass parser (pure)

`UsageLimitWallParser` (name beats `UsageLimitResetParser` — reset is only one arm).

Input: `(now, text, fallbackAlias)`. Output:

```
record UsageLimitWall(
    UsageLimitWallKind Kind,          // SessionLimit | ModelCap
    string ModelAlias,
    DateTime? ResetAt,                // UTC, SessionLimit only
    string? ResetZoneId,
    string RawText)
```

- Reset parse: `resets 6:10pm (Europe/London)` / `6:10 pm` / 24h form → next occurrence in that **named** zone at-or-after `now`, DST-correct via `TimeZoneInfo.FindSystemTimeZoneById`. Tests **must** include the London-vs-UTC hour trap the spec named. Unparseable reset → `ResetAt = null` → `ModelCap`.
- Model parse: `"your Fable 5 limit"` / `"your Sonnet 5 limit"` etc. If absent, `fallbackAlias`. If both absent, return null (caller logs and does not write a hold — better than pausing a guessed model).
- `+ 2 minutes` is applied by the recovery writer, not the parser.

Classifier stays structural. Parser is not a classification input.

`ProviderContractCatalog` Claude `UsageLimitSignal` reason update: structural Wall; **some** stubs state a reset, the per-model cap does not. `StatesResetTime` stays `true` (the session-limit subclass does); do not flip the axis to Unknown.

### S2 — Write the hold; stop the 30-minute same-model nudge

On Wall adopt in `ApiErrorRecoveryService.BuildNewRowAsync` (the one place every stub becomes a schedule):

1. `wall = UsageLimitWallParser.Parse(...)`.
2. Upsert `ModelAvailabilityHold` for `(session.Kind, wall.ModelAlias)`: `Source = AutoDetected`, `RawText`, `SourceSessionId`, `HitAt = now`. `DisabledUntil = wall.ResetAt + 2min` or null. If an active **Manual** hold exists, do not shorten `DisabledUntil` (CARD-0309 contract; this card can pin with a Manual row inserted in a test).
3. **SessionLimit** (`ResetAt` present): `NextAttemptAt = DisabledUntil` (one resume at reset, not the 30-minute rung). Keep the existing wall-death cap (3 consecutive → park).
4. **ModelCap** / parse failure: `Resolve(row, WallModelPaused)` — `NextAttemptAt = null`. No WallPrompt. CARD-0071 already fails the open task. Incident 22 stays; message names the subclass and the hold.
5. Delete / stop using `WallUnparsedFailureReason` as the default Wall path. Keep it only if parse returns null (no alias at all).

Clear holds: `ModelAvailability.SweepExpiredAsync` from `AgentSupervisorHostedService`'s minute pass (DB-only, milliseconds). Lazy clear on `IsHeld` is also required so a create 409 cannot fire 1 s after reset because the sweep has not run.

### S3 — Dispatcher skip + create/start 409

As in "Shared state" above. Tests:

- Queued fable task + active fable hold → not dispatched; sonnet task on the same tick **is**.
- Hold with `DisabledUntil` in the past → cleared, then dispatched.
- `CreateAsync` ClaudeCode Frontier (→ fable) + active fable hold → 409 `model_disabled` listing remaining aliases.
- `CreateAsync` Grok Frontier while fable is held → 200.
- Check interpreter Haiku while fable is held → start still works.

### S4 — Attention + "what's left"

New `AttentionKind.ModelAvailabilityHold = 24` (append, never renumber). Projected in `AttentionService` from **active** holds (the recency-is-lifecycle rule: no ack). Severity **Error**. Headline:

- SessionLimit: `fable exhausted — resets 18:10 Europe/London (in 23m); dispatch paused for fable`
- ModelCap: `fable exhausted (no reset stated); dispatch paused for fable; available: opus, sonnet, haiku, grok-4.6, …`

Evidence: raw stub text, source session, `DisabledUntil`. Remaining %: include CARD-0143 `GetLatestAsync` only when a **fresh** sample exists for that kind; otherwise omit (Claude will omit). Client: add the enum member and a label; no new banner. Mobile home band 1 consumes new kinds automatically if it already switches on the enum — verify; if it has an exhaustive switch, add the case.

Optional: CARD-0304's `GET /api/agent-tasks/pipeline` may later list holds. **Not this card.** A thin `GET /api/model-availability` (active holds + available aliases) is in scope so CARD-0309/0304/orchestrators have a read model without scraping attention. Map it next to `/api/agent-tasks/areas`.

### S5 — Headed `/usage` probe (opt-in, no poller)

`ClaudeUsageCommandCanaryTests` `[Explicit]` `[HeadedCanary]`: idle non-exhausted session, type `/usage` (not `/usage-credits`). Classify: overlay vs inline, read-only vs spend/redeem (Codex `/usage` picker is the hazard class, CARD-0141). `/usage-credits` stays Forbidden — do not type it. Result recorded in the catalog `SubscriptionUsagePoll` reason and this plan's execution notes. **No live sweep** regardless of outcome in v1.

### S6 — Docs

- Spec D7: strike fleet-wide `UsageLimitState`; point here. Leave S1–S3/S5a history.
- `docs/agent-kinds.md` / session-runtime-invariants: new gotcha — usage walls are **per-model**; CARD-0072's 30-minute WallPrompt is not recovery for a per-model cap; `/usage-credits` is not a readout.
- CARD-0309 card (when someone plans it): "table, reader, 409, dispatcher skip already exist; this card adds Manual upsert, optional `ignoreModelDisabled`, UI/`card.ps1` toggle, and the outrank rule tests if not already green."
- CARD-0090: "unavailable" = `ModelAvailability.IsHeld`; do not invent a second pause list.

---

## Grok / Codex

Catalog `UsageLimitSignal` stays `Unknown`. This card does **not** repeat CARD-0083 S1. The hold table accepts those kinds so a future captured wall, or CARD-0309's manual disable of `grok-4.6`, writes the same row. Do not map Grok's documented hook class `rate_limit` (which includes 503/529) onto Wall.

---

## What this card does not do

- CARD-0090 Hard/Medium/Easy fallback chains, or auto kind-switch of the dead task onto Grok.
- CARD-0309 manual PATCH/UI/`card.ps1` (state is ready; writes are not).
- CARD-0305 per-task routing pins (a pin naming a held model should 409 with the same sentence — CARD-0305's dispatcher consults `IsHeld` when it exists).
- CARD-0304 pipeline-status field for holds.
- Polling `/usage` or `/usage-credits`.
- Remaining-quota % invented from nothing.
- Fleet-wide pause, `UsageLimitState` singleton.
- Disconnecting already-live Fable sessions (they stay Running/idle after the stub; AlwaysOn Fable orchestrator restart is the 409 case above).
- Changing CARD-0071 settlement (error text still not a report). The mid-turn task **Fails** on ModelCap (visible beats "Succeeded"); it stays Working only while a SessionLimit resume is scheduled.

---

## Test matrix

| Layer | Test |
|---|---|
| Unit | `UsageLimitWallParserTests`: session-limit + zone trap; Fable 5 incident string → `ModelCap`/`fable`/null reset; Sonnet/Haiku forms; fallback alias; unparseable → ModelCap |
| Unit | `ModelAlias.Normalize` table |
| Unit | `ApiErrorClassifierTests`: Fable 5 text is still Wall (class unchanged) |
| Application | Recovery: session-limit stub → hold with `DisabledUntil`, `NextAttemptAt` at that instant, WallPrompt fires then; Fable 5 stub → hold with null until, **no** enqueue, task Failed |
| Application | Dispatcher: fable held, sonnet queued on same tick → only sonnet dispatches; expired hold auto-clears |
| Application | Create 409 `model_disabled` + available list; Grok create while fable held succeeds |
| Application | `AttentionService` projects kind 24 from an active hold; gone after `ClearedAt` |
| Agents / Pty | FakeClaude both modes; normalizer fixture for Fable 5 |
| Headed `[Explicit]` | `/usage` idle probe (S5) |

Run per `docs/testing-and-build.md`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0022/ -- --treenode-filter "/*/*/UsageLimitWallParserTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0022/ -- --treenode-filter "/*/*/ApiErrorClassifierTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0022/ -- --treenode-filter "/*/*/ApiErrorRecovery*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0022/ -- --treenode-filter "/*/*/AgentTaskDispatcher*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0022/ -- --treenode-filter "/*/*/AttentionServiceTests/*"
```

Forward slash on OutputPath. Sequential with Pty tests. Delete `bin-card0022*` after.

---

## Sequencing and risks

**Order: S0 → S1 → S2 (hold + stop the nudge) → S3 (skip + 409) → S4 → S6. S5 can land anytime, does not gate.** S2 is the incident's cause; S3 is what stops the next fable Plan from launching.

| Risk | Disposition |
|---|---|
| Session-limit is actually account-wide, and we only pause fable | Other models hit it and get their own holds. Eventual, not instant. Better than pausing healthy Sonnet. |
| "Fable 5" regex misses a reword | Fallback to session launch alias; pin the incident string; unrecognised model name with a reset still SessionLimit on the fallback alias |
| Stub `model: "<synthetic>"` used as the key | Forbidden in the writer; tests assert the hold key is `fable` not `<synthetic>` |
| 30-minute WallPrompt still fires if S2 misses a path | Pin: Fable 5 fixture through `EnsureAdoptedAsync` → zero `EnqueueAsync` |
| Create 409 steals CARD-0309 | It **is** CARD-0309 item 2 for AutoDetected. CARD-0309 keeps Manual writes + UI. One mechanism. |
| AlwaysOn Fable orchestrator cannot restart | Documented; do not silent-reroute. Operator starts it on another model or waits. |
| Queued fable tasks sit until Thursday with no reset | Attention Error + 409 on new creates. Human reroutes or CARD-0090 later. Do not invent a clear time. |
| Grok 503 classified as Wall via a future copy-paste | Catalog Unknown; no Grok writer in this card. |
| `/usage-credits` is a spend action | Never typed. S5 only probes `/usage`. |

---

## Execution notes

- First production proof: one disposable Fable session driven into the cap is **not** required (unaffordable, spec testing stance). Fixtures + the already-captured 2026-09-01 JSONL are the proof. After deploy, the next natural Fable cap should write a hold, 409 the next `delegate.ps1 -Tier Frontier`, and leave Gym Stat / school-revision Sonnet orchestrators running.
- Live already-held work from today's incident is debris, not a failed fix.
- CARD-0309 plan, when picked: start from "Shared state" above. Do not design a second table.
