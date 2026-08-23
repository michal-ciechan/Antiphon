# CARD-0157 — Grok context fullness: measure, ingest, compute: plan

**Date:** 2026-08-23 · **Card:** CARD-0157 (`2ef4544b-692d-4436-8687-d3192e85951f`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `b834868` (branch `feat/card-task-8e48c2f1`). Every line number below was
re-read out of the code on that commit.

**Established fact, not re-derived here:** the Investigate stage (task `cf0fa086`, Grok,
read-only; findings recorded on the card 2026-08-23) is ground truth. Its five findings — the
500 000 static per-model ceiling lives only in the stdio initialize handshake and
`~/.grok/models_cache.json`, never in the tailed `updates.jsonl`; `turn_completed.usage.inputTokens`
is a per-user-turn loop SUM across that turn's model calls, resetting every user-turn, not
session-cumulative and not single-call occupancy; the CARD-0153 fixture's 18.7M was a 103-call
turn's sum; `auto_compact_completed.tokens_before/tokens_after` are genuine Grok-computed occupancy
numbers, currently not ingested; `cachedReadTokens` is a subset of `inputTokens`, never additive —
are designed against, not re-litigated. What this plan DID re-verify (cheap, on-disk, no live
session touched) is the exact wire field names, quoted in §"Wire shapes" below.

**Related:** CARD-0153 D5/S5 (the suppression gate this card flips), CARD-0082 (fullness machinery,
`ContextWindowSettings`), CARD-0080 (Grok normalizer/tailer), CARD-0083 (provider contract
catalog), CARD-0041 (CompactBoundary manual/auto semantics the ingested rows must respect),
CARD-0055 (delivery verification — the reason the compaction sweep must be gated before the flip).

---

## Verdict up front

1. **Ceiling (card q1): option (c), a catalog constant — `SelfReportedCeilingTokens: 500_000` on
   Grok's `ContextWindowUsageContract` — with an operator override and a drift canary.** A runtime
   stdio probe is real machinery (spawn + ACP handshake + auth coupling: an unauthenticated
   `GROK_HOME` parks on a device-code login, the catalog's own `BlockingStartupModal: FailFast`
   fact, `ProviderContractCatalog.cs:125-129`) crossing two process boundaries (the handshake would
   happen runner-side; the ceiling is consumed server-side in `SessionContextUsage`) to fetch a
   number that is static: `~/.grok/models_cache.json` on this machine says `"context_window":
   500000` for **both** models (grok-4.6 and grok-4.5), agreeing with the investigation's one-off
   stdio capture. Reading `models_cache.json` at runtime is rejected for the same
   process-boundary reason plus lifecycle: the file is lazily created by grok itself and lives in
   the runner machine's `GROK_HOME`; the server has no established pattern of reading provider-local
   files (every provider-config read in this repo is runner/pty-side). The catalog is exactly this
   repo's home for measured provider constants — every axis in `ProviderContractCatalog` is a
   hardcoded measured fact with an evidence string. The "silently wrong forever" risk is bounded
   two ways: `ContextWindowSettings.ModelOverrides` **beats** the catalog constant (operator fix
   without a deploy — possible because S2 also starts stamping the Grok model id onto rows), and a
   headed canary pins `models_cache.json`'s `context_window` at 500 000 so drift goes red in the
   canary lane. The card's "do not ship `ModelOverrides["grok"]=500000`" stands: the override map
   stays empty; the constant lives in the catalog where it carries evidence, and it is only safe
   at all because this card also fixes the numerator.

2. **Occupancy (card q2, the flagged open question): option (c), the combination.**
   `auto_compact_completed` is ingested as a usage-bearing `CompactBoundary` row carrying
   `InputTokens = tokens_after` — the authoritative, Grok-computed anchor. Between compactions, a
   `turn_completed` whose `usage.modelCalls == 1` is a genuine single-call window measurement and
   provides the continuous estimate. A multi-call turn's loop-sum **does not update the number at
   all** — stale-but-honest beats wrong-by-100×. This requires ingesting `modelCalls` (currently
   discarded), which is S1's plumbing. The pure-(a) alternative (compaction anchors only) was
   rejected because Grok compacts around 100–155K observed — under a third of the 500K window —
   and `models_cache.json` says `"compactions_remaining": 1`, so after the one compaction is spent
   a session could run for hours with a frozen badge while genuinely filling; single-call turns
   (every quick "answer this" turn is one) refresh it. The pure-(b) alternative loses the only
   authoritative number in the protocol and reads nothing during long tool-heavy stretches either.
   Dividing a multi-call sum by `modelCalls` was rejected explicitly: a loop **average** is not
   the live window (the investigation's own point).

3. **TokensOf (card q3): kind-aware via a declared contract fact, not an `if (kind == Grok)`.** A
   new `ProviderUsageAccounting` enum on `ContextWindowUsageContract` — `AdditiveCache` (Claude:
   input + cacheRead + cacheCreate + output ≈ window) vs `TurnSumInclusiveCache` (Grok: input
   already contains cache reads and is a turn sum). The same fact drives both behaviours it
   implies: Grok occupancy = `InputTokens` alone, and Grok occupancy-eligibility = single-call
   turns + compaction anchors only. One declared fact, two consequences, zero provider branching
   outside the catalog.

4. **The flip (card q4): Grok's `ContextWindowUsage` goes `Supported`; the CARD-0153 gate STAYS.**
   The gate in `SessionContextUsage.Compute` (`:79-86`) is contract-keyed, not Grok-keyed — it is
   the correct standing rule for any future kind added as Degraded + SelfReported-but-unwired, and
   it costs nothing to keep. Grok simply stops matching it. The named test
   `Fullness_suppression_is_kind_keyed_not_value_keyed` (`SessionContextUsageTests.cs:293-308`)
   goes red exactly as CARD-0153 predicted and is **reworked, not deleted**: the property it pinned
   (suppression keyed on the contract, not the token value) survives via a synthetic
   Degraded+SelfReported contract; the per-kind loop gets kind-shaped fixture rows. Pleasing
   detail: the 18.7M row that test feeds becomes null for Grok through *arithmetic* (a 103-call
   loop-sum is not occupancy-eligible) rather than through the gag — the 3740% number becomes
   unrepresentable, not just hidden. **The flip also requires a new guard**: the idle-compaction
   sweep currently gates Grok out only via the null fullness; once fullness is real, the sweep
   would type `/compact <focus text>` into a Grok session, and a Grok `/compact` writes **no user
   chunk** (`ProviderContractCatalog.cs:121-124`) — CARD-0055 delivery verification would read
   `NoTranscriptRecord` and walk the kill path on a healthy always-on session. The sweep gains an
   explicit `RefocusCompact.State == Supported` gate (Claude passes, `:89-92`; Grok is Unknown,
   `:152-155`, and Unknown behaves as Unsupported for enabling machinery — the catalog's own rule).

**One sentence:** ingest Grok's own occupancy numbers (compaction anchors + single-call turns,
`modelCalls` plumbed through), put the measured 500K ceiling in the catalog with an override and a
drift canary, stop adding cache on top for a provider whose input already contains it, flip the
contract to Supported, and gate the compaction sweep so the new number cannot trick it into typing
into a session that won't echo.

---

## Wire shapes, re-verified on disk 2026-08-23

From `C:\Users\lndco\.grok\sessions\C%3A%5Csrc%5CAntiphon\1636e434-…\updates.jsonl` (a real
grok 1.0.5 session; verbatim):

```json
{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"1636e434-…","update":{"sessionUpdate":"compaction_checkpoint","checkpoint_id":"8016d019-…","prompt_index_at_compaction":1,"checkpoint_file":"compaction_checkpoints/8016d019-….json","schema_version":1,"created_at":"2026-08-19T19:24:20.583418700+00:00"},"_meta":{"eventId":"1636e434-…-1549","agentTimestampMs":1787167460583}}}
{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"1636e434-…","update":{"sessionUpdate":"auto_compact_completed","tokens_before":106112,"tokens_after":34833,"summary_preview":null},"_meta":{"eventId":"1636e434-…-1550","agentTimestampMs":1787167460583}}}
```

- `auto_compact_completed` carries exactly `tokens_before` / `tokens_after` (snake_case ints) and
  `summary_preview`; `_meta.eventId` / `agentTimestampMs` ride in `params._meta` as on every row.
  A **no-op** variant exists (observed `tokens_before == tokens_after == 34833` when compacting an
  already-compacted session) — still a valid anchor.
- `compaction_checkpoint` carries checkpoint bookkeeping only, **no token numbers** — it stays
  skipped.
- `turn_completed.usage` carries `inputTokens, outputTokens, totalTokens, cachedReadTokens,
  cacheCreationTokens, reasoningTokens, modelCalls, apiDurationMs, costUsdTicks, numTurns` and
  `modelUsage` keyed by the model id (observed key: `"grok-4.6-build"`). `modelCalls == numTurns`
  in every sampled row; the card names `modelCalls`, so that is the field used.
- `~/.grok/models_cache.json`: `"context_window": 500000` and `"auto_compact_threshold_percent":
  80` for both grok-4.6 and grok-4.5; `"compactions_remaining": 1`. (The 80% threshold visibly
  does not describe the observed 100–155K compaction points against a 500K window — Grok's
  internal trigger basis is its own business; we report Grok's numbers, we do not model its
  trigger.)

Expected live badge behaviour after this card, from the investigation's samples: single-call
turns at 99–137K → **20–27%**; post-compaction anchors at 33–43K → **7–9%**. Honest small numbers,
not 3740%.

---

## What the code does today, re-read on `b834868`

- **Normalizer skips the signal.** `GrokTranscriptNormalizer.Normalize` dispatches only
  `user_message_chunk` / `agent_message_chunk` / `agent_thought_chunk` / `tool_call` /
  `turn_completed` (`GrokTranscriptNormalizer.cs:92-100`); the doc-comment (`:32-37`) names the
  compaction pair as deliberately skipped. `FromTurnCompleted` (`:185-223`) reads
  `inputTokens/outputTokens/cachedReadTokens/cacheCreationTokens` (`:205-212`) and discards
  `modelCalls` and `modelUsage`; Grok parts never carry `Model` (contrast
  `TranscriptNormalizer.cs:38-41`, Claude stamps it per CARD-0082).
- **Plumbing is Claude-shaped but additive-friendly.** `TranscriptPart`
  (`TranscriptNormalizer.cs:13-41`) → `RunnerTranscriptEvent`
  (`SessionRunnerContracts.cs:104-137`, additive-optional by design, `:126-137`) → tailer mapping
  (`GrokTranscriptTailer.cs:222-226`) → `AgentSessionRuntime.PersistTranscriptAsync`
  (`AgentSessionRuntime.cs:547-573`) → `TranscriptEntry` (`TranscriptEntry.cs:51-54`). The
  arrival-order rebase (`AgentSessionRuntime.cs:544-545`: `storedSeq = e.Sequence > maxSeq ?
  e.Sequence : maxSeq + 1`) matters below: after this ships, a runner-restart re-tail will insert
  *historical* compaction rows **above** the session's existing rows.
- **The computation.** `SessionContextUsage.Compute` (`SessionContextUsage.cs:55-110`): newest
  usage-bearing row by **sequence** (`:64-67`), `TokensOf` sums all four token fields (`:259-263`
  — the Claude-correct, Grok-double-counting formula), ceiling via
  `settings.ResolveCeiling(modelId)` (`:77`; `ContextWindowSettings.cs:25-49`, default 200 000,
  `ModelOverrides` empty in `server/appsettings.json:117`), CARD-0153 suppression gate at
  `:79-86`, >100% warning `:90-95`, auto-headroom warning `:97-104`, later-invalidator null-out
  `:106-107` (`IsInvalidator :247-252`, `IsAutoCompactBoundary :254-257`). `LoadFullnessAsync`'s
  query (`:134-155`) already fetches CompactBoundary rows; its projection lacks `Timestamp` and
  (obviously) `ModelCalls`.
- **The contract.** `ContextWindowUsageContract(State, Reason, CeilingSource)`
  (`server/Application/Dtos/ProviderContract.cs:80-83`); Grok's entry is Degraded + SelfReported
  with a reason that already promises this card (`ProviderContractCatalog.cs:112-115`); the
  Compaction axis reason says "The tailer currently skips these rows" (`:121-124`) — text that S2
  falsifies and must update. Enum: `ProviderContractEnums.cs:29-34`.
- **The sweep that must not be surprised.** `ContextCompactionService.IsContextWindowEligible`
  (`ContextCompactionService.cs:93-99`) admits Supported **and Degraded**, so Grok sessions
  already reach the fullness read (`:273-277`) and are saved only by the null (`:276-277`).
  Threshold is `ContextPercent` **default 50** (`ContextCompactionSettings.cs:19`); the prompt it
  would type is `CompactCommandName` + focus text (`:42-45`). Nothing in the file consults
  `RefocusCompact`.
- **Settlement is out of scope on purpose.** Task cost rollups (`DelegationUsageRollup.cs:47,62`,
  `AgentTaskReplyService.cs:403`) sum the stored usage fields — for a loop-sum provider that IS
  the correct spend number. Kind-aware arithmetic lives only in `SessionContextUsage`.

---

## Design

### D1 — `ModelCalls` plumbing (S1)

One new additive-optional field, `int? ModelCalls`, on the whole chain, defaulted null so a lagging
shadow-copied pty-host stays compatible (the exact pattern of `Model`, CARD-0082,
`SessionRunnerContracts.cs:134-137`):

| Where | Change |
|---|---|
| `TranscriptPart` (`TranscriptNormalizer.cs:41`) | `int? ModelCalls = null` after `Model` |
| `RunnerTranscriptEvent` (`SessionRunnerContracts.cs:137`) | same, additive-optional |
| Tailer mappings: `GrokTranscriptTailer.cs:222-226`, `TranscriptTailer.cs:270`, `CodexTranscriptTailer.cs:274` | pass `p.ModelCalls` (only Grok ever sets it) |
| `TranscriptEntry` (`TranscriptEntry.cs:54`) | `public int? ModelCalls { get; set; }` + EF migration `AddTranscriptEntryModelCalls` (nullable int, no backfill — null means "pre-carriage or non-Grok", and D3 treats null as not occupancy-eligible, which is the safe reading for legacy loop-sum rows) |
| `PersistTranscriptAsync` (`AgentSessionRuntime.cs:567`) | `ModelCalls = e.ModelCalls` |
| `TranscriptContextRow` (`SessionContextUsage.cs:15-24`) | `int? ModelCalls = null, DateTime? Timestamp = null` (Timestamp is needed by D3's ordering) |
| `LoadFullnessAsync` select (`SessionContextUsage.cs:142-155`) and `ContextCompactionService` select/mapping (`:240-250`, `:267-272`) | add `ModelCalls`, `Timestamp` |
| `DirectSessionRunnerClient` (`tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs:222`) | pass through |

### D2 — normalizer: ingest the anchor, keep the evidence, stamp the model (S2)

`GrokTranscriptNormalizer` changes, each pinned by a verbatim-row test:

1. **`auto_compact_completed` → one `CompactBoundary` part.**
   - `Text` = `` $"Context compacted (auto): tokens {before} -> {after}" `` (falls back to
     `"Context compacted (auto)"` when the fields are absent). The **`(auto)` marker is
     deliberate**: the provider row is literally named `auto_compact_completed` even when a manual
     `/compact` produced it (measured, `GrokCanaryTests.cs:468-475`), and the CARD-0041 working
     rules treat an auto/unknown boundary as pure housekeeping — which is correct for Grok in both
     trigger cases, because a Grok compaction writes no user chunk and no `turn_completed`
     (`ProviderContractCatalog.cs:121-124`): there is nothing to strand and no turn to end. It
     must never contain `(manual)` — that would make `IsManualCompactBoundary`
     (`SessionRunnerContracts.cs:188-191`) fire a turn-end queue flush off housekeeping.
   - `InputTokens = tokens_after` — the row is **usage-bearing**: it IS the post-compaction
     occupancy, directly consumable by D3 with no new columns. `tokens_before` stays in the text
     for operators (and greppable diagnosis); it feeds no arithmetic — it is superseded the moment
     the row lands.
   - Uuid/timestamp from `params._meta` as every other row (`GetMetaString`/`GetTimestamp`,
     `GrokTranscriptNormalizer.cs:253-273`); a missing `tokens_after` degrades to a plain
     (non-usage-bearing) boundary, which D3 treats as an invalidator — fullness honestly unknown
     rather than stale.
2. **`compaction_checkpoint` stays skipped** — no token payload (wire shape above), pure
   checkpoint bookkeeping.
3. **`FromTurnCompleted` additionally reads `usage.modelCalls` → `ModelCalls`**, and stamps
   **`Model`** from the first `usage.modelUsage` property name (observed `"grok-4.6-build"`).
   The model id makes `ContextWindowSettings.ModelOverrides` actually able to target Grok
   (substring match: a `"grok-4.6"` key matches `"grok-4.6-build"`) and gives the badge a real
   `ModelId`. A cancelled `turn_completed` has no usage block → both stay null (already the
   measured shape, `GrokTranscriptTailerTests.cs:55-56`).
4. Doc-comment (`:32-37`) updated to match.

Downstream safety of the new rows, checked against the standing rules: an `(auto)`/unmarked
boundary is housekeeping to all three working-rule implementations (CARD-0041), is not
`addedManualCompactBoundary` (`AgentSessionRuntime.cs:542`), triggers no flush, and is already a
cooldown row for the compaction sweep (`ContextCompactionService.cs:406`) — harmless, and moot
once D5 gates the sweep off Grok anyway.

### D3 — kind-aware computation (S3)

New enum in `ProviderContractEnums.cs`:

```csharp
public enum ProviderUsageAccounting
{
    AdditiveCache = 0,        // cache/output fields add on top of InputTokens (Claude; the legacy default)
    TurnSumInclusiveCache = 1 // InputTokens is a per-user-turn loop sum that already contains cache reads (Grok)
}
```

`ContextWindowUsageContract` (`ProviderContract.cs:80-83`) gains two additive-optional fields:
`ProviderUsageAccounting UsageAccounting = AdditiveCache` and `int? SelfReportedCeilingTokens =
null`. Only Grok's catalog entry sets them (`TurnSumInclusiveCache`, `500_000`).

`SessionContextUsage.Compute` changes (all keyed on `contract?.UsageAccounting`):

1. **Occupancy eligibility.** `AdditiveCache`: unchanged (`IsUsageBearing`, `:243-245`).
   `TurnSumInclusiveCache`: a row is occupancy-bearing iff
   `(Kind == CompactBoundary && InputTokens != null)` — the D2 anchor — or
   `(InputTokens != null && ModelCalls == 1 && !IsApiErrorStub)` — a single-call turn. A
   multi-call or `ModelCalls == null` row is **not** selected; the previous anchor stands
   (stale-but-honest). Legacy rows (all null `ModelCalls`) therefore read null until the first
   post-deploy anchor — correct: their loop-sums were never occupancy.
2. **`TokensOf`.** `AdditiveCache`: the existing four-field sum (`:259-263`).
   `TurnSumInclusiveCache`: `InputTokens` alone — `cachedReadTokens` is a subset (investigation
   finding 5), `cacheCreationTokens` is observed 0, and `outputTokens` is not window content of
   the measured call. The anchor row's `InputTokens` is `tokens_after` by construction.
3. **Ordering.** `AdditiveCache`: newest by `Sequence`, unchanged. `TurnSumInclusiveCache`: newest
   by `(Timestamp ?? DateTime.MinValue, Sequence)`. Reason: the arrival-order rebase
   (`AgentSessionRuntime.cs:544-545`) plus the deterministic re-tail from offset 0 means the first
   runner restart after this deploys will insert every *historical* `auto_compact_completed` of a
   live session **above** its current rows — sequence-newest would then anchor the badge to a
   stale mid-history `tokens_after` until the next turn. Grok rows carry provider-stamped
   `agentTimestampMs`, so timestamp ordering is reliable there; Claude keeps sequence-only
   semantics untouched. The same comparer defines "later" for the invalidator/headroom scans
   (`:97-107`).
4. **Ceiling resolution.** Extract `int? ResolveOverrideOrNull(string? modelId)` from
   `ContextWindowSettings.ResolveCeiling` (`ContextWindowSettings.cs:25-49`; `ResolveCeiling`
   keeps its exact behaviour, now calling it). `Compute` resolves:
   `settings.ResolveOverrideOrNull(modelId) ?? contract?.SelfReportedCeilingTokens ??
   settings-default` — operator override beats catalog constant beats 200K default. Claude path
   is bit-for-bit unchanged (`SelfReportedCeilingTokens` null). Applies at both call sites
   (`:71`, `:77`). Note: when the winning row is a boundary (Model null), `ModelId` falls back to
   `EffectiveModelId` (`:76`) — override matching still works when the operator keys on the
   session's model; with no overrides configured the constant wins regardless.
5. **The CARD-0153 gate (`:79-86`) is untouched** — after D6 no kind matches it, and it remains
   the correct default for any future Degraded+SelfReported kind.

### D4 — where the 500K plugs in, and how it can be corrected (S3/S4)

- Catalog: Grok `ContextWindowUsage` becomes
  `(Supported, reason citing CARD-0157 + the two sources (stdio modelState capture,
  models_cache.json context_window, both 500 000, measured 2026-08-23), SelfReported,
  UsageAccounting: TurnSumInclusiveCache, SelfReportedCeilingTokens: 500_000)`.
  `CeilingSource.SelfReported` keeps its name; its meaning is now "the ceiling is the provider's
  self-reported figure, recorded in the catalog with evidence" — enum doc-comment
  (`ProviderContractEnums.cs:26-34`) updated to say so.
- Operator correction path: a `ModelOverrides` entry (e.g. `"grok-4.6": 1000000`) wins without a
  deploy — meaningful now that D2 stamps the model id.
- Drift tripwire: a `GrokCanaryTests` addition (headed lane) asserts
  `models_cache.json` → `models.*.info.context_window == 500000`; when xAI moves the window the
  canary goes red and names the constant to update.

### D5 — the compaction-sweep guard (S4, lands WITH the flip, not after)

In `ContextCompactionService`, after the fullness threshold passes (`:278-279`) — or equivalently
as an early per-session check next to the kind lookup — require
`ProviderContractCatalog.For(kind).RefocusCompact.State == AgentTuiCapabilityState.Supported`
before enqueueing `CompactPrompt`. Today only Claude qualifies (`ProviderContractCatalog.cs:89-92`);
Grok is Unknown (`:152-155`). Without this, the first Grok session to hit 50% (`ContextPercent`
default, `ContextCompactionSettings.cs:19` — reachable once `compactions_remaining` is spent and
occupancy climbs) would receive a typed `/compact <focus>` whose delivery can never confirm (no
user chunk ⇒ `NoTranscriptRecord` ⇒ CARD-0055 failure handling against a healthy session), and
whose argument-carrying form is unmeasured on Grok besides. Detection (fullness, incidents,
attention) stays kind-open; only the *typing intervention* is gated on the measured contract —
the same shape as every CARD-0055/0056 lesson.

### D6 — the flip and its blast radius (S4)

- Catalog entry per D4; also update the **Compaction** axis reason (`:121-124`) — "the tailer
  currently skips these rows" becomes "auto_compact_completed is ingested as a usage-bearing
  (auto) CompactBoundary (CARD-0157); compaction_checkpoint stays skipped (no token payload)" —
  keeping the words `compaction_checkpoint` (`ProviderContractCatalogTests.cs:440` greps for it).
- `ProviderContractCatalogTests.ContextWindowUsage_ceiling_sources` (`:472-481`): Grok expectation
  → Supported, plus new assertions on `SelfReportedCeilingTokens == 500_000` and
  `UsageAccounting == TurnSumInclusiveCache`; `Degraded_reasons_name_the_weakness` (`:208-220`)
  stops seeing Grok's axis.
- `SessionContextUsageTests.Fullness_suppression_is_kind_keyed_not_value_keyed` (`:293-308`)
  reworked into two tests (see S-tests below) preserving the pinned property.
- `CLAUDE.md` gotcha (CARD-0153 bullet, "Grok's contextFullness badge is suppressed until the
  follow-up measurement card lands") — replaced by one line stating the new semantics: the badge
  is Grok's own numbers (single-call turns / compaction anchors) against the measured 500K window,
  updates only on those events, and a multi-call turn deliberately does not move it.
- Client: no change — it renders the server's `ContextFullness` and never computes its own.

### D7 — what does not move

- Settlement/cost rollups keep summing raw stored usage (correct spend accounting for a loop-sum
  provider). No change to `DelegationUsageRollup` / task settlement.
- No new columns beyond `ModelCalls`; no backfill migration — all three new rules recompute over
  stored rows by construction (the CARD-0041 pattern).
- `VerifiedPromptSubmitter`/boot prompts, working/idle rules, channel dispatch: untouched; D2's
  new row is housekeeping to all of them by the existing `(auto)` rule.
- The auto-headroom warning (`:97-104`) keeps its Claude semantics; a Grok boundary can only
  appear in its "later" window when non-usage-bearing (a usage-bearing one would have won the
  selection), i.e. the defensive missing-`tokens_after` case — acceptable log noise, noted in code.

---

## Slices

Standard dispatch pattern: each slice independently green, committed and pushed as it completes.

| # | Content | Commit shape |
|---|---|---|
| S1 | `ModelCalls` plumbing end to end (D1): part, event, three tailer mappings, entity + migration, persist, projections, test helper | `feat(context): CARD-0157 S1 ModelCalls carried transcript-part → entity` |
| S2 | Grok normalizer (D2): compaction ingest, `modelCalls`, `Model` stamp; fakegrok mirrors (below); doc-comment | `feat(grok): CARD-0157 S2 ingest auto_compact_completed as usage-bearing (auto) CompactBoundary` |
| S3 | Computation (D3/D4): enum + contract fields, kind-aware eligibility/TokensOf/ordering, ceiling precedence, `ResolveOverrideOrNull` | `feat(context): CARD-0157 S3 kind-aware occupancy + self-reported ceiling precedence` |
| S4 | The flip (D5/D6): catalog entry + reasons, sweep RefocusCompact gate, test reworks, canary, CLAUDE.md gotcha | `feat(grok): CARD-0157 S4 flip ContextWindowUsage to Supported; gate the compact sweep` |

Ordering is load-bearing: until S4, Grok stays suppressed by the (kept) gate even though S1–S3
machinery is live — every intermediate deploy is safe.

fakegrok additions (S2, so integration tests can drive the shapes): a typed `/compact` writes the
measured pair (`compaction_checkpoint` + `auto_compact_completed`, tokens via
`ANTIPHON_FAKE_COMPACT_TOKENS="106112,34833"` default, **no** `turn_completed`) mirroring
`GrokCanaryTests`' measurement; `ANTIPHON_FAKE_MODELCALLS` (default 1) sets `usage.modelCalls` so a
multi-call turn is representable (`src/Antiphon.FakeGrok/Program.cs:376-383` is the emit site).

---

## How we verify this and will not regress it

### S1/S2 — `GrokTranscriptTailerTests` additions (verbatim real rows, per the file's own convention `:9-17`)

- The real `auto_compact_completed` row (session `1636e434`, quoted above) → exactly one
  `CompactBoundary`: Text contains `(auto)` and `106112 -> 34833`, does NOT contain `(manual)`,
  `InputTokens == 34833`, `OutputTokens/CacheReadTokens/CacheCreationTokens` null, Uuid
  `…-1550`, timestamp from `agentTimestampMs`. No TurnEnd emitted.
- The real `compaction_checkpoint` row → zero parts.
- A tokens-less `auto_compact_completed` (fields removed) → plain boundary, `InputTokens` null.
- The existing `TurnCompletedRow` (`:49-50`) now also asserts `ModelCalls == 103` and
  `Model == "grok-4.6-build"`; the cancelled row (`:55-56`) asserts both null.
- Working-rule guard: a boundary appended after a completed turn leaves
  `TranscriptWorkingState.IsProvenIdle` true (housekeeping, not activity), and
  `IsManualCompactBoundary(kind, text)` is false for the emitted text.
- FakeGrok contract arm: `/compact` into fakegrok produces the pair through the real tailer with
  the same normalized result (keeps the fake honest against the canary's measured shape).

### S3 — `SessionContextUsageTests` Grok arm (fixtures modelled on the investigation's sessions `98c61e03` / `1636e434`)

With the Grok contract (Supported, TurnSumInclusiveCache, 500K):

1. A lone multi-call TurnEnd (`ModelCalls: 103, InputTokens: 18_747_424` — the CARD-0153 fixture
   number) → `Fullness` **null** (nothing occupancy-eligible), not 3740%, not 100%+.
2. A single-call TurnEnd (`ModelCalls: 1, InputTokens: 137_657, CacheReadTokens: 120_000`) →
   `Fullness == 137_657 / 500_000` and `TokensUsed == 137_657` exactly — **no cache add** (the
   kind-aware TokensOf pin).
3. Single-call turn then multi-call turn → fullness still anchored on the single-call row
   (stale-but-honest pin).
4. …then a usage-bearing boundary (`InputTokens: 34_833`) → `34_833 / 500_000` (compaction
   resets the number — the card's (iii)).
5. …then a newer single-call turn → the turn wins (between-compaction refresh).
6. Backfill/re-tail shape: a boundary with **older Timestamp but higher Sequence** than a
   single-call turn does not beat it (the D3 ordering pin — red under sequence-only selection).
7. Tokens-less boundary newest → `Fullness` null (invalidator, honest unknown).
8. Ceiling precedence: no override → 500 000 in `CeilingTokens`; `ModelOverrides
   {"grok-4.6": 400_000}` with row Model `grok-4.6-build` → 400 000; Claude rows with the same
   settings keep Configured behaviour (`ResolveCeiling` regression).
9. Claude regression: every existing additive-cache test unchanged and green.

Suppression rework (the CARD-0153-predicted red):

- `Fullness_suppression_is_contract_keyed` — a **synthetic** `ContextWindowUsageContract(Degraded,
  …, SelfReported)` suppresses the same rows a Supported contract computes; pins that the kept
  gate is contract-keyed, the exact property the old test held.
- `Every_kind_computes_or_abstains_per_its_contract` — the per-kind loop with kind-shaped rows:
  Claude computes from an additive row; Grok computes from a single-call row and returns null for
  the old 18.7M multi-call row (by arithmetic, not gag); Codex/OpenCode/Raw unchanged.

### S4 — flip + sweep

- `ProviderContractCatalogTests` updates per D6 (ceiling-source test asserts the constant and the
  accounting enum; Compaction reason still names `compaction_checkpoint`).
- `ContextCompactionSweepTests`: a Grok session at fullness ≥ `ContextPercent` does **not** get
  `CompactPrompt` enqueued (RefocusCompact gate — red without D5); a Claude session in the same
  state still does (no regression on the sweep's purpose).
- Integration (Antiphon.Tests, DB-backed): `PersistTranscriptAsync` over a
  `RunnerTranscriptEvent` sequence replaying the 98c61e03 shape (multi-call turns up and down,
  single-call turn, boundary) → `LoadFullnessAsync` returns the anchor-based number; scoped to the
  session the test created (shared-Postgres rule).
- `GrokCanaryTests` (headed lane) additions: after `/compact`, the `auto_compact_completed` row
  carries numeric `tokens_before`/`tokens_after` (wire-shape tripwire); `models_cache.json`
  `context_window == 500000` (ceiling-drift tripwire).

### What must stay green (the regression set)

`SessionContextUsageTests` (Claude arm), `ContextCompactionSweepTests`, `ProviderContractCatalogTests`,
`GrokTranscriptTailerTests`, `TranscriptNormalizerTests`, `GrokDelegateEndToEndTests` (settlement
sums untouched), the client vitest suite via `scripts/test-client.ps1` (no client change expected —
run it anyway), and the working/idle lockstep suites (the new row must be housekeeping everywhere).
Run `Antiphon.Tests` chunked by namespace per repo rule; `Antiphon.SessionRunner.Tests` and
`Antiphon.Agents.Pty.Tests` sequentially, never co-scheduled with `Antiphon.Tests`.

---

## Open questions for the operator (non-blocking; defaults chosen above)

1. **Occupancy = `InputTokens` alone on single-call turns** (no `outputTokens` add). Adding output
   (~15–20K observed) would slightly anticipate the next call's window at the cost of mixing an
   estimate into a measured number. Default: measured number only.
2. **`(auto)` marker on rows a manual `/compact` produced.** Chosen because the provider names the
   row `auto_compact_completed` in both cases and housekeeping is the correct working-rule
   treatment in both. If a `(grok)` trigger word is preferred for honesty, `IsAutoCompactBoundary`
   (`SessionContextUsage.cs:254-257`) would simply never match Grok rows — no behavioural
   difference in any shipped path.
3. **The kept suppression gate** could instead be deleted (no kind matches it after S4). Kept as
   the standing default for future Degraded+SelfReported kinds; delete costs a test rework with no
   safety gain.
