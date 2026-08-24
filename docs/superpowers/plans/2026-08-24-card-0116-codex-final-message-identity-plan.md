# CARD-0116 — Codex final-message identity: close the "may be PREAMBLE" false positive at the normalizer: plan

**Date:** 2026-08-24 · **Card:** CARD-0116 (`b72f454a-6262-4c02-aad8-075f94fe38a1`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `4bfec3b` (branch `feat/card-task-1e9627a9`). Every line number below was
re-read out of the code on that commit.

**Established fact, not re-derived here:** the Investigate stage (task `f44b4adb`, findings on the
card 2026-08-24) is ground truth. Its verdict — this is a Codex **normalizer attribution
mismatch**, not a timing gap and not a too-short grace: `CodexTranscriptNormalizer` stamps
`task_complete.turn_id` onto `TurnEnd.ApiCallId` (`CodexTranscriptNormalizer.cs:331`) but emits the
final `AgentMessage` through `Part(...)` with `ApiCallId = null` (`:386-387`), so the generic
identity gate in `AgentTaskReplyService.FinalMessageOf` (`:1427-1438`, literal
`t.ApiCallId == end.ApiCallId`) can structurally never recognize a genuine Codex final answer,
waits the full `FinalMessageGraceSeconds` (120 s, `DelegationSettings.cs:390`), and labels the
correct verdict "may be PREAMBLE". Both real rollouts (tasks `2e152d49`, `be0ccc71`) show the true
final answer landing 65 ms and 213 ms **before** `task_complete` — is designed against, not
re-litigated. What this plan DID re-verify (cheap, on-disk) is every wire shape and consumer quoted
below.

**Related:** CARD-0046 (the identity gate + grace this card makes work for Codex; its slices 2/3
define the final-vs-narration contract this plan must not weaken), CARD-0099 S1 (the Codex
normalizer), CARD-0108 (the adapter fix that made real Codex round trips observable), CARD-0080
(Grok normalizer, the other identity-carrying peer).

---

## Verdict up front

**Fix at the source, in `CodexTranscriptNormalizer`, with zero changes to
`AgentTaskReplyService`/`AgentTaskDispatcher`:** stamp the enclosing `payload.turn_id` as
`ApiCallId` onto exactly one kind of thread item — an `AgentMessage` whose `phase` is
`"final_answer"` — so a Codex final answer satisfies the existing generic gate the same way a
Claude `message.id` or a Grok `promptId` already does. The settlement path stays
provider-agnostic; the warning's real-truncation teeth are untouched (a Codex turn that genuinely
ends with no final answer still has no `AssistantText` with the TurnEnd's identity and still warns
after the grace); and the failure direction under any future Codex schema drift is
**warn-over-cautiously**, never **certify-narration-as-verdict**.

The six open decisions, resolved (full reasoning in "Design"):

1. **Identity scope:** the `phase == "final_answer"` AgentMessage ONLY. Not commentary, not
   Reasoning, not UserMessage. Codex's `turn_id` is a *turn* id, not a per-API-call id — stamping
   every AgentMessage would make `FinalMessageOf` join measured `phase:"commentary"` messages into
   the caller's report and zero out `NarrationDiscardedChars`, reintroducing CARD-0046 slice 2's
   defect from the other side.
2. **Flat dialect:** explicitly left on the degraded path, unchanged and pinned. Flat
   `agent_message` carries `phase:"final_answer"` but no turn id (measured,
   `codex-exec-turn.jsonl:8`); Antiphon launches only the TUI. No buffering, no manufactured match.
3. **Final predicate:** require `phase == "final_answer"`. Commentary is measurably the same item
   type with a different phase (`codex-tui-multi-turn.jsonl`: 3 AgentMessages — 1 `commentary`,
   2 `final_answer`), so "any same-turn AgentMessage" is wrong today, not just fragile tomorrow.
4. **`last_agent_message`:** test-only integrity cross-check. Never normalized as a row, never a
   runtime fallback.
5. **Out-of-order/restart:** no new machinery — the existing three retriggers (AssistantText
   persist, turn-end settlement, 5 s dispatcher sweep) already converge once the identity exists;
   pinned by a deferred-then-settle test. Historic warned tasks stay immutable **by construction**
   (ingestion-time fix + `(Uuid, Kind)` dedup means a re-tail cannot retro-stamp stored rows).
6. **Safety contract:** per-provider normalizer contract pins ("the turn-ending response's own
   text shares the TurnEnd's ApiCallId") for Codex, Claude AND Grok — the generic pin lives at the
   normalizer layer, not as provider arms in settlement — plus the two settlement pins the card
   demands: the real-rollout shape settles clean, and a genuine truncation still warns.

**Provider scope, confirmed (the card's open question):** Claude and Grok have never shown this
pattern because their normalizers *structurally* carry the identity, not because nobody hit the
gap. Claude: one assistant JSONL record fans out into AssistantText/Thinking/ToolCall/TurnEnd parts
that all share `message.id → apiCallId` (`TranscriptNormalizer.cs:98`, text at `:119-122`, TurnEnd
at `:144-148` — same local variable, cannot diverge). Grok: `EmitPending` stamps the pending turn's
`promptId` on Thinking/AssistantText (`GrokTranscriptNormalizer.cs:256-273`) and `FromTurnCompleted`
stamps the same `promptId` on TurnEnd (`:201`, `:227`). Codex is the only normalizer where the two
sides of the equality come from different rows with no shared stamp. There IS one latent Grok edge
worth pinning while we are here: chunks that arrive with no `promptId` pend under the empty key and
flush with `ApiCallId = null` (`EmitPending`, `:258`) while the TurnEnd would still carry
`promptId` — if Grok ever stopped stamping `_meta.promptId` on chunks, the identical 120 s false
warning would appear there. So this is a **narrow Codex-only code fix** plus a **generic
three-provider contract pin** so the next dialect that drops the identity goes red in a normalizer
test instead of crying wolf in production for days.

---

## Wire shapes and consumers, re-verified on `4bfec3b`

### The TUI dialect (what Antiphon launches)

From the checked-in real rollout `tests/Antiphon.SessionRunner.Tests/Fixtures/codex-tui-turn.jsonl`
(codex-tui 0.147.0, measured 2026-08-20):

- Line 10 — the final answer: `event_msg / item_completed` with **`payload.turn_id =
  "01a01fbe-be6d-74f3-b1a1-a99e5e82ed3b"`** and `item = {type: "AgentMessage", id: "msg_0c20…",
  content: [{type:"Text", text:"b2526831"}], phase: "final_answer"}` at 15:16:34.293Z.
- Line 13 — the boundary: `task_complete` with the **same `turn_id`**,
  `last_agent_message: "b2526831"`, at 15:16:34.342Z (49 ms later; the two live rollouts measured
  65 ms and 213 ms).
- `codex-tui-multi-turn.jsonl` — mid-turn narration is the SAME item type with
  **`phase: "commentary"`** (1 commentary vs 2 final_answer AgentMessages in the file).

### The flat dialect (codex exec / Desktop — never launched by Antiphon)

`codex-exec-turn.jsonl:8`: `event_msg / agent_message` with `phase: "final_answer"` and **no
turn id anywhere in the payload**; its `task_complete` (line 11) does carry `turn_id`.

### The normalizer today

- `FromThreadItem` (`CodexTranscriptNormalizer.cs:207-253`) receives the `payload` (which holds
  `turn_id`) but never reads it; the AgentMessage arm (`:228-236`) emits via `Part(...)`
  (`:386-387`), which passes `ApiCallId` as null. The item's `phase` is not read anywhere in the
  repo today (grepped: zero `final_answer` consumers).
- `FromTaskComplete` (`:301-336`) reads `payload.turn_id` (`:305`) and emits
  `TurnEnd(ApiCallId: turnId, StopReason: "end_turn")` (`:327-334`).
- `CodexTranscriptTailer.cs:274` already forwards `p.ApiCallId` onto `RunnerTranscriptEvent`, so
  the stamp needs no plumbing beyond the normalizer.

### The gate and its clocks (unchanged by this plan)

`AgentTaskReplyService.ExtractMarkedTurnAsync` (`:1180-1330`): loads the latest TurnEnd, the marked
prompt, and the turn's `AssistantText` rows with their `ApiCallId`s; `FinalMessageOf` (`:1427-1438`)
is the literal identity match; `ResolveFinalMessageState` (`:1469-1481`) measures
`UtcNow() - end.CreatedAt` against the grace; `NeverArrived` falls back to the whole-turn join
labelled by `FinalMessageMissingWarning` (`:845-848`, the "may be PREAMBLE" text) with the Warning
event (`:440-446`) and the once-per-session `DelegateFinalMessageMissing` incident (`:478-487`,
`:870-876`). The narration split at `:1320` (`t.ApiCallId != end.ApiCallId`) is what decision 1
must not break. `AgentTaskDispatcher.SettleDeferredReportsAsync` (`:1244-1294`) is the 5 s sweep;
its arm (1) (`:1278-1293`) re-invokes settlement only while no `AssistantText` carries the
TurnEnd's `ApiCallId`.

### Consumers of `AssistantText.ApiCallId` — why the stamp is safe

Audited every non-settlement consumer:

- **`DelegationUsageRollup`** (`:29-50`): the row source filters `t.InputTokens != null` **before**
  grouping, and Codex AgentMessage rows carry no usage — a stamped text row never enters the
  rollup at all. Cost accounting is bit-for-bit unchanged. (Even inside a group it would be
  harmless: `g.Max(...)` per field.)
- **`AgentSessionRuntime.IsUnseenTurnBoundaryAsync`** (`:311-345`): arm (2)'s ApiCallId dedup is
  gated to `entry.Kind == TranscriptKinds.TurnEnd` (`:329`) — an AssistantText sharing the
  TurnEnd's id cannot suppress a boundary.
- Everything else (`SessionTranscriptDto`, `SessionRunnerDtos`, `AgentSessionService.cs:982`,
  `SessionRunnerHttpClient.cs:496`) is pass-through projection.

---

## Design

### D1 — the stamp: `final_answer` AgentMessage only (decision 1 + 3)

In `FromThreadItem`, read `var turnId = Fit(GetString(payload, "turn_id"))` once, and in the
`AgentMessage` arm emit `ApiCallId: turnId` **iff `GetString(item, "phase") == "final_answer"`**;
a commentary/absent-phase AgentMessage keeps `ApiCallId = null` exactly as today.

Why not every AgentMessage (decision 3's option a): `FinalMessageOf` joins ALL texts with the
TurnEnd's id (`:1436`), and the narration split counts everything else (`:1320`). Codex's
`turn_id` spans the whole turn, so stamping commentary would (a) splice measured mid-turn
commentary INTO the report delivered to the caller and (b) report `NarrationDiscardedChars = 0`
while doing it — the delegate's "I'll start by reading the spec" would once again arrive dressed
as the verdict, silently. That is the exact defect CARD-0046 slice 2 exists to prevent; the
identity must mean "this row IS the turn-ending response's own text", which for Codex is precisely
the `final_answer` phase.

Why not Reasoning/UserMessage (decision 1's "coherent per-turn attribution" option): `ApiCallId`
is assistant-record attribution by contract (`TranscriptNormalizer.cs:25-27`); no provider stamps
UserPrompt. Nothing anywhere reads `Thinking.ApiCallId` for Codex (TUI Reasoning items are
empty-text and mostly not emitted, normalizer doc `:37-39`), so stamping it buys nothing and
widens the blast radius of a turn-id-semantics mistake. Minimal wins.

Drift direction (the safety invariant): if a future codex-cli renames or drops `phase`, the stamp
stops happening, `AssistantText.ApiCallId` reverts to null, and the warning returns as an
over-cautious false positive — today's behaviour, loud and diagnosable — while narration can never
be certified as the final message. Every alternative predicate ("any AgentMessage", "the last
AgentMessage before task_complete") fails in the dangerous direction instead.

Multiple `final_answer` messages in one turn (unobserved, but representable): all get the same
stamp, `FinalMessageOf` joins them in sequence order — identical to Claude's several text blocks
of one response (`:1423-1425`). Correct by analogy and pinned by nothing extra.

### D2 — flat dialect: explicitly degraded, pinned, unchanged (decision 2)

`FromFlatAgent` (`:185-193`) is not touched. The flat dialect's final message has no turn id to
carry, and the two ways to manufacture one are both worse than the warning:

- **Buffering** the final text until `task_complete` arrives would hold a real answer hostage to a
  row that may never come — a crash mid-turn would mean the answer is never ingested at all, which
  loses text to fix a label. The normalizer's streaming contract (parts out per line in) also has
  no re-emission path, and the dialect latches exist precisely because double-emission is the
  hazard here.
- **Retro-stamping server-side** (rebind null-id text to the TurnEnd at settlement) is the
  Codex-specific settlement arm this plan rejects wholesale — it would be a weak match dressed as
  identity, exactly what the investigation forbids ("do not manufacture a weak text match
  silently").

Consequence, stated honestly: a flat-dialect Codex session delegated through Antiphon would still
warn after 120 s. Antiphon launches only the TUI (`CodexTranscriptNormalizer.cs:21-25`), so this is
a mode we have never launched; if `codex exec` delegation is ever built, the safe design is a new
decision then (likely `last_agent_message`-corroborated, loudly). A fixture pin freezes the
degraded shape so the choice stays explicit rather than forgotten.

### D3 — `last_agent_message`: evidence in tests, never a row (decision 4)

`task_complete.last_agent_message` duplicated the final text verbatim in both real rollouts and
the fixture. It is used two ways, both offline:

- **Fixture integrity assertion:** the normalizer test asserts the stamped AssistantText's text
  equals the fixture's `last_agent_message` — an independent cross-check that the stamp landed on
  the right row.
- **Headed drift canary:** the existing `CodexAdapterIntegrationTests` round trip (headed,
  opt-in via `HeadedCodexGate`) gains an assertion that the live rollout's final AssistantText and
  TurnEnd share an ApiCallId — the tripwire that fires when a real codex-cli upgrade changes the
  schema, before any delegate report does.

It is NOT normalized as a row (would double-emit the answer — the exact thing the dialect latches
exist to prevent) and NOT a runtime recovery fallback (duplicate content of unknown truncation
behaviour; a silent weak match).

### D4 — out-of-order, restart, and history (decision 5)

No new machinery. With the identity present, the three existing retriggers converge in every
ordering:

| Order | Path |
|---|---|
| Text then task_complete (measured normal: 49/65/213 ms) | Turn-end settlement finds `finalMessage` → `Landed` → settles immediately, warning never considered |
| TurnEnd persisted first, text lands late (ingestion stall, backfill) | First settlement returns `Deferred` (`:1295-1298`); the AssistantText persist re-invokes settlement (`AgentSessionRuntime.cs:216-222`) → settles clean |
| Both events missed live | Dispatcher sweep (`:1278-1293`): `landed` now true → sweep does NOT re-settle-with-warning; the turn-end retrigger's own settlement stands |
| Backfill sequence rebase | A rebased text row lands above the TurnEnd but the query window is `prompt.Sequence < seq < nextPrompt` with **no cap at the TurnEnd** (`:1206-1211`) — still in window |

Genuine truncation is untouched: a Codex turn whose `task_complete` arrives with commentary but no
`final_answer` message has a TurnEnd with `ApiCallId = T` and no text carrying `T` — `Pending`
inside the grace, `NeverArrived` + warning + incident after it, exactly as designed.

**History is immutable by construction, no flag needed** (decision 6's recommendation, adopted):
the fix is ingestion-time; stored rows are never rewritten. A runner restart re-tails from offset
0 and the normalizer now produces stamped parts, but `PersistTranscriptAsync`'s `(Uuid, Kind)`
dedup drops them as already-stored — pre-fix rows keep their null `ApiCallId`, and already-settled
tasks with the warning keep their events verbatim. One transitional wrinkle, accepted and noted:
a Codex turn *straddling* the deploy restart (text stored pre-restart with null id, task_complete
post-restart) produces at most one more false warning; the next turn is clean.

### D5 — what deliberately does not change

- `AgentTaskReplyService`, `AgentTaskDispatcher`, `DelegationSettings` — zero edits. The
  120 s grace, the warning text, the incident, the sweep cadence all stand.
- The generic gate stays generic: no `if (kind == Codex)` anywhere server-side. The provider
  knowledge lives where every other dialect fact lives (the runner's normalizer, per CARD-0099 /
  CARD-0080 precedent).
- Grok/Claude normalizers: no behaviour change — S3 adds contract *pins* only.
- Doc-comments that must update: `CodexTranscriptNormalizer` class remarks (the AgentMessage
  mapping now names the identity rule and the flat-dialect exception);
  `DelegationSettings.cs:374-382` (the identity contract paragraph currently reads as
  Claude-only — name all three providers' identity sources so the next reader knows the gate is
  satisfied per-provider by the normalizers).

---

## Slices

Standard dispatch pattern: each slice independently green, committed and pushed as it completes.
Build stage runs on Codex per the operator's instruction.

| # | Content | Commit shape |
|---|---|---|
| S1 | Normalizer stamp (D1) + `CodexTranscriptNormalizerTests`/`CodexTranscriptTailerTests` pins: fixture identity equality, commentary stays null, flat dialect unchanged, re-tail determinism still holds, `last_agent_message` cross-check | `fix(codex): CARD-0116 S1 stamp turn_id onto the final_answer AgentMessage` |
| S2 | Settlement pins (no production change): Codex-shaped clean settle without warning; Codex-shaped genuine truncation still warns; deferred-then-settle ordering | `test(delegation): CARD-0116 S2 codex final-message settlement pins` |
| S3 | Cross-provider identity contract pins (Claude + Grok normalizer tests), headed `CodexAdapterIntegrationTests` drift assertion, doc-comment updates | `test(delegation): CARD-0116 S3 per-provider final-message identity contract pins` |

Ordering note: S1 alone closes the live false positive; S2/S3 are the locks. Deploying S1 requires
a session-runner restart (`pwsh -File scripts/restart-session-runner.ps1`) — the normalizer runs
runner-side and the tailer's parts flow through the existing `RunnerTranscriptEvent.ApiCallId`
field, so no server deploy is strictly required, though both together is the normal cadence.

---

## How we verify this and will not regress it

### S1 — `Antiphon.SessionRunner.Tests`

`CodexTranscriptNormalizerTests` additions (the file's existing real-fixture convention):

1. **The false-positive closer, at the source:** normalizing `codex-tui-turn.jsonl` yields a final
   `AssistantText` with `ApiCallId == "01a01fbe-be6d-74f3-b1a1-a99e5e82ed3b"` — equal to the
   `TurnEnd.ApiCallId` — and text equal to the fixture's `task_complete.last_agent_message`
   (decision 4's integrity cross-check). Red today (`ApiCallId` is null).
2. **Narration stays narration:** in `codex-tui-multi-turn.jsonl`, the `phase:"commentary"`
   AgentMessage normalizes with `ApiCallId == null`; only the `final_answer` rows carry their
   turns' ids, each equal to its own turn's TurnEnd id (two turns, two distinct ids — pins that
   the stamp is per-turn, not sticky state).
3. **Flat dialect pinned degraded:** `codex-exec-turn.jsonl`'s AssistantText keeps
   `ApiCallId == null` while its TurnEnd carries the turn id — the decision-2 shape, frozen so a
   future change to it is a decision, not an accident.
4. Existing pins stay green: `A_re_tail_from_offset_zero_reproduces_identical_parts` (the stamp
   must be stateless per line), dialect-latch tests, usage-delta tests.
5. `CodexTranscriptTailerTests`: the identity survives the tailer mapping onto
   `RunnerTranscriptEvent` (through `CodexTranscriptTailer.cs:274`).

### S2 — `Antiphon.Tests` (`Application` namespace, `AgentTaskReplyIntegrationTests` style, DB-backed, assertions scoped to the test's own session per the shared-Postgres rule)

1. **The card's required false-positive pin:** a marked Codex-shaped turn persisted exactly as the
   real task `2e152d49` rollout normalizes — narration `AssistantText(ApiCallId: null)`, final
   `AssistantText(ApiCallId: "T", 974 chars)`, `TurnEnd(ApiCallId: "T", StopReason: "end_turn")`
   65 ms later — settles **immediately** (no grace consumed): `Succeeded`, `Result` == the final
   message verbatim, narration chars named in the Completed event, **no** Warning event, **no**
   `DelegateFinalMessageMissing` incident, caller note carries no "may be PREAMBLE". Red today
   (falls through the whole 120 s and warns).
2. **The card's required true-truncation pin:** the same shape with the `final_answer` row REMOVED
   (commentary only — a real preamble-only turn), clock advanced past
   `FinalMessageGraceSeconds` → settles on the join **with** the warning, the Warning event and
   the incident. This is the Codex twin of the kept Claude pin and proves the fix did not weaken
   detection.
3. **Deferred-then-settle ordering (decision 5):** persist the TurnEnd first, invoke settlement
   (must defer, task stays Dispatched, no warning), then persist the final text and re-invoke →
   settles clean. Pins that a backfill/stall ordering converges without the warning.
4. Existing pins stay green untouched: `a_response_that_never_writes_text_settles_after_the_grace_window`,
   `a_turn_end_whose_own_response_has_not_written_its_text_does_not_settle`,
   `the_final_messages_arrival_settles_the_task_with_the_report`,
   `a_turn_end_with_no_api_call_id_settles_as_it_always_did`,
   `the_report_is_the_turn_ending_responses_own_text`, and the
   `usage_repeated_across_one_api_calls_entries_is_counted_once` cost pin (the stamped text row is
   token-less and pre-filtered by `DelegationUsageRollup.cs:31`).

### S3 — the generic contract, pinned per provider at the source layer

- `TranscriptNormalizerTests` (Claude): the turn-ending record's AssistantText and TurnEnd parts
  share one `ApiCallId` (today true by shared local variable — the pin makes it a contract).
- `GrokTranscriptTailerTests` (Grok): the fixture turn's flushed AssistantText `ApiCallId` equals
  the `turn_completed` TurnEnd's `promptId` — closes the latent empty-key edge as a red test the
  day Grok stops stamping `_meta.promptId` on chunks.
- `CodexAdapterIntegrationTests` (headed, opt-in): after the real round trip, the normalized
  final AssistantText and TurnEnd share an ApiCallId — live schema-drift tripwire against future
  codex-cli versions (the fixture pins can't see a live schema change; this can).

### Run protocol

`Antiphon.SessionRunner.Tests` via `dotnet run --project tests/Antiphon.SessionRunner.Tests
--property:OutputPath=bin-c116/` (daemons hold `bin/`; forward slash; delete `bin-c116` dirs
after). `Antiphon.Tests` chunked by namespace per repo rule, never co-scheduled with the pty test
projects. Headed lane stays opt-in (`ANTIPHON_HEADED_CODEX_TESTS`-gated).

---

## Open questions for the operator (non-blocking; defaults chosen above)

1. **Thinking rows:** Codex `final_answer` is the only stamped kind; Reasoning could carry the
   turn id "for symmetry" with Claude. Default NO — no consumer, wider blast radius, and Codex TUI
   Reasoning is empty-text anyway.
2. **Flat dialect future:** if `codex exec` delegation is ever wanted, the degraded-path pin (S1
   test 3) is the marker to revisit — likely a `last_agent_message`-corroborated design, loudly,
   as its own card.
3. **Warning copy:** the "may be PREAMBLE" text is accurate once it fires only on real
   identity-absence; no rewording planned. If the operator wants the warning to name the provider
   and the identity rule for faster triage, that is a one-string change to
   `FinalMessageMissingWarning` — deliberately left out of scope.
