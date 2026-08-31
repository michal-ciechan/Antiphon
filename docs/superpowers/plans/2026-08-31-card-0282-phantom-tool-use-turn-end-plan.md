# CARD-0282 — stop emitting a phantom TurnEnd for fable's tool_use records stamped end_turn

**Task 2db5abe0, Plan, 2026-08-31.** Design only; nothing built. Read with
docs/investigations/2026-08-31-card-0248-nudge-eaten-by-sweep-diagnosis.md (causal chain point 1),
which owns the incident evidence. This plan adds new measurements taken during planning that change
the shape of the fix's residual risk.

## The defect, one paragraph

`claude-fable-5` occasionally stamps `stop_reason:"end_turn"` on assistant JSONL records of a
response that called tools. `TranscriptNormalizer.FromAssistant`
(`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:141-147`) emits a `TurnEnd` part for any
non-empty stop_reason that isn't literally `"tool_use"`, so one record yields both a `ToolCall`
part and a phantom `TurnEnd` sharing the record's Uuid. Measured: 18 such rows across 3 sessions,
all fable, zero on every other model. The tools all ran and the turns all continued — the turn was
demonstrably not over.

## New measurements from this plan pass

All run against the production DB (`antiphon-postgres`) and the raw JSONL of the incident session.

**1. Every same-record phantom is `end_turn`.** Grouping TurnEnd rows that share a Uuid with a
ToolCall row by (Model, StopReason): exactly one bucket — `claude-fable-5 / end_turn / 18 rows /
3 sessions`. No `max_tokens`, no `stop_sequence` co-occurrence exists anywhere in the data. (For
context, the DB holds zero `max_tokens` TurnEnds at all; the 48 `stop_sequence` rows are all
CARD-0072 API-error stubs, text-only.)

**2. The quirk is per-RESPONSE, and it has a sibling-record hole.** The raw incident file
(`C:\Users\lndco\.claude\projects\C--src-Antiphon\ea371170-e5c2-4d41-9495-1e677b30dd54.jsonl` —
the Claude JSONL is named by the Antiphon session id) shows response `msg_011CeZ3VPvBbeuz1bhmFqSYi`
written as SEVEN records, every one stamped `end_turn`:

```
fdf0ba8c  end_turn  [thinking ""]        <- signature-only thinking record → bare TurnEnd (seq 5)
34caa40e  end_turn  [tool_use ToolSearch] → ToolCall + phantom TurnEnd (seq 6/7)
e471d826  end_turn  [tool_use Bash]       → same (seq 9/10)
... (four more tool_use records, seq 11..21, results interleaved as the tools finished)
```

This is a parallel tool batch: one API response, one message.id, each content block its own JSONL
record, each record stamped with the response's (wrong) stop_reason — the CARD-0046 rule that "every
record of a response carries its stop_reason" applied to a mis-stamped response. The thinking
record carries **no tool_use block**, so a per-record guard cannot suppress its bare TurnEnd.
Measured DB-wide: **4 such sibling phantoms** (TurnEnd sharing an ApiCallId with a ToolCall row of a
*different* Uuid, none on their own record), all fable, all `end_turn`, all bare (`Text` null),
across the same 3 sessions. Total phantom population: 22 = 18 same-record + 4 sibling.

**3. The quirk is rare, not systematic.** In the same session's raw file, fable stamped tool_use
records correctly 50 times (`stop_reason:"tool_use"`) and wrongly 6 times — one bad response out of
a session's worth of good ones. Mid-turn thinking/text sibling records of *correctly*-stamped
responses carry `tool_use` (12 + 3 records) and already produce no TurnEnd.

## The fix

### Guard (TranscriptNormalizer.FromAssistant)

Track whether the record carried a `tool_use` block while enumerating content, then scope the
TurnEnd emission:

```csharp
// CARD-0282: claude-fable-5 sometimes stamps stop_reason "end_turn" on the records of a
// response that called tools (measured: 22 phantom TurnEnds, 3 sessions, fable only — the
// tools all ran and the turns all continued). A record carrying a tool_use block whose
// stop_reason claims "end_turn" is not a turn end: its tool result is by construction still
// to come. Deliberately scoped to end_turn — a max_tokens truncation mid-tool-call is a
// genuinely dead turn (the tool never runs, no result will ever arrive) and suppressing ITS
// TurnEnd would strand the session reading "working" forever, the CARD-0041 failure class.
if (!string.IsNullOrEmpty(stopReason)
    && stopReason != "tool_use"
    && !(hasToolUse && stopReason == "end_turn"))
    parts.Add(new TranscriptPart(TranscriptKinds.TurnEnd, ...));
```

### Scoping decision: narrow (`end_turn` only), confirmed

The card asked whether the guard should be `end_turn`-scoped or a blanket "tool_use never gets
TurnEnd". Narrow is right, for three reasons:

1. **The evidence covers exactly `end_turn`.** All 22 measured phantoms are `end_turn`
   (measurement 1). No other stop_reason has ever co-occurred with tool_use in this database, in
   any fixture (`split-final-response.jsonl`, `api-error-stubs.jsonl`, `queued-command.jsonl` carry
   none), or in fakeclaude (which emits no tool_use at all — `end_turn` on text lines,
   `stop_sequence` on stubs).
2. **The error asymmetry runs the other way for real truncations.** A wrongly *suppressed* TurnEnd
   strands a session as "working" until the next real boundary — the CARD-0041 failure that once
   stranded a session for two days, and it would starve every WhenIdle delivery and the CPU
   watchdog's proven-idle check. A wrongly *emitted* TurnEnd self-corrects at the next activity
   row. So suppression must be limited to shapes where the turn provably continues: for
   `end_turn`+tool_use the transcript proves it (results followed, 87 further entries in the
   incident session); for `max_tokens`+tool_use the tool input is truncated, the tool cannot run,
   no result will arrive — that turn is genuinely dead and must keep its TurnEnd.
3. **`stop_sequence` stays untouched by construction.** The only real `stop_sequence` records are
   CARD-0072 synthetic API-error stubs, text-only — the guard's `hasToolUse` conjunct can never
   match them, and the `end_turn` conjunct makes that doubly true. No stub behaviour changes.

`cancelled` is Grok-only (`GrokTranscriptNormalizer`), never seen by this code path; Codex has its
own normalizer. Neither changes.

### The sibling-record residual: accept, document, monitor

The 4 bare sibling phantoms (measurement 2) are byte-for-byte indistinguishable, per-record, from
CARD-0046's *legitimate* split-final shape (signature-only thinking record, `end_turn`, bare
TurnEnd announcing a real end) — the normalizer is stateless per line and cannot tell them apart.
Options considered:

- **A (recommended): fix same-record at the normalizer, accept the sibling residual.** Impact is
  bounded per consumer class: for working/idle consumers the very next line (the first ToolCall of
  the same batch, written in the same flush) outranks the phantom, so exposure is one write. For
  latest-boundary consumers the sibling can sit as "latest TurnEnd" for the turn's duration, but
  settlement already refuses to finalize a boundary with no same-ApiCallId AssistantText
  (Pending → Deferred), and CARD-0248's settle-anyway gates (its own plan,
  2026-08-31-card-0248-settle-anyway-boundary-gates-plan.md) stop the sweep from settling on it.
  Residual population so far: 4 rows ever.
- **B (rejected): stateful tailer lookahead** — hold a bare TurnEnd until the next same-ApiCallId
  record and drop it if that record carries tool_use. Adds latency to the boundary signal on the
  hot evidence path, and CARD-0046 settlement deliberately keys on the bare-TurnEnd-first ordering.
- **C (follow-up if the residual bites): a shared DB-level phantom predicate** — "a TurnEnd with
  stop_reason end_turn whose ApiCallId also owns a ToolCall row is not a boundary." Proven sound by
  the measurements (it matches all 22 phantoms and no legitimate row: a real final response is
  text-only, so its id owns no ToolCall). It would touch the five latest-boundary consumers' EF
  queries, so it is a card of its own, to be filed only if post-deploy monitoring (below) shows
  sibling phantoms still causing incidents.

## TurnEnd consumers, assessed one by one

Complete list from a repo-wide sweep of `TranscriptKinds.TurnEnd` in server/ and
src/Antiphon.SessionRunner (the card's "at least seven" plus one runner-side consumer). Working
assumption confirmed for all: every one is falsely *triggered* by phantoms today; none silently
*relies* on them — a phantom's timing was always racy (it fired only if a sweep tick landed in the
tool-execution window), so no consumer could have built a dependable behaviour on it.

1. **Queue idle-flush** — `SessionMessageQueueService.cs:2619-2635` (and the working/idle read in
   the same file). Latest-end-vs-latest-activity; a same-record phantom holds "idle" from the
   TurnEnd row until the ToolResult arrives — minutes for a long Bash call — letting WhenIdle
   deliveries be typed into a working composer. Post-fix strictly better. Behavioural change to
   state plainly: WhenIdle messages queued during a long fable tool-turn now wait for the true turn
   end — that is the WhenIdle contract, not a regression.
2. **ChannelReplyDispatcher** — `:202` (turn window for reply extraction), `:483`
   (turn-complete-after-prompt gate, else `LossReason.TurnIncomplete`), `:866`. A phantom could
   mark a turn complete mid-turn and extract/dispatch a partial reply to a channel. Post-fix the
   window spans the real turn; replies get *longer* (all mid-turn text of the whole turn), which is
   the correct contract. The bare sibling residual can still satisfy `:483` early — noted under
   option A; rare (4 ever) and requires a channel prompt in flight during a quirky response.
3. **ReviewReplyDispatcher** — `:73`, same latest-TurnEnd windowing as (2), same assessment.
4. **ApiErrorRecoveryService** — `:136`, `:233`. Consumes only TurnEnd rows with
   `IsApiError == true` (synthetic stubs: stop_sequence, text-only, never tool_use). Unaffected by
   the phantoms and unaffected by the fix, in both directions.
5. **TranscriptTurnWindow** — `:26`, `:70-80`. Prev/this-boundary prompt attribution; phantoms
   subdivide a real turn so a prompt can be attributed to a fragment. Post-fix correct.
6. **AgentSessionRuntime.IsTurnBoundary** — `:344` (flush-on-idle trigger; requires
   `StopReason == EndTurn`, which the phantom carries) plus the `:389` same-ApiCallId dedup (added
   for CARD-0046's split responses — it already limited each quirky response to ONE false flush
   attempt; the rest of the batch was deduped). Post-fix no false trigger from same-record
   phantoms; the sibling residual can still cause one false flush *attempt*, and the delivery path
   re-checks working state before typing, unchanged in kind from today but far rarer.
7. **Delegation sweeps** — `AgentTaskReplyService.ExtractMarkedTurnAsync` `:1391` (highest TurnEnd
   is *the* report boundary), `AgentTaskDispatcher.SettleDeferredReportsAsync` `:1394`, and
   `TranscriptKinds.IsReportBoundary` (`SessionRunnerContracts.cs:315` — everything but
   `cancelled`). The phantom parked settlement in Deferred and armed CARD-0248's monotonic sweep.
   Post-fix the report boundary appears only at the real end. CARD-0248's own fix is independent
   and stays as designed — either fix alone would have prevented the incident; ship both. Not
   redesigned here.
8. **TranscriptWorkingState.Classify** — `src/Antiphon.SessionRunner/TranscriptWorkingState.cs:41`
   (runner-side: CPU watchdog `IsProvenIdle`, Herdr status label). Any TurnEnd ranks as an end; a
   phantom opens a false "proven idle" window mid-turn — exactly the state whose consequences the
   method's conservatism exists to prevent (a hot session read as killable). Post-fix correct. The
   server queue-service and client `isWorking()` mirrors need **no** lockstep change: the fix is
   upstream of all three, in the normalizer they all read from.

Non-consumers, for completeness: `RunnerCodexAdapter:211` / `RunnerGrokAdapter:249` read TurnEnd
rows produced by their own normalizers — untouched code paths.

## Tests

### Normalizer tests (`tests/Antiphon.Tests/Agents/TranscriptNormalizerTests.cs`)

New fixture `tests/Antiphon.Tests/Agents/Fixtures/fable-tool-use-end-turn.jsonl` — two VERBATIM
lines from the incident session's raw JSONL (path above), following the compact-boundary /
split-final-response fixture convention: the signature-only thinking record (`uuid fdf0ba8c…`) and
the ToolSearch tool_use record (`uuid 34caa40e…`), both `message.id msg_011CeZ3VPvBbeuz1bhmFqSYi`,
both `stop_reason:"end_turn"`, `model claude-fable-5`. (Builder: take the ToolSearch record, not a
Bash one — its input is innocuous; eyeball both lines for secrets before committing, per the
fixture rules.)

1. `A_tool_use_record_stamped_end_turn_yields_a_ToolCall_and_no_TurnEnd` — fixture line 2:
   exactly one part, kind ToolCall, ToolName `ToolSearch`, ApiCallId carried; no TurnEnd part.
   This is the fix's pin.
2. `A_genuine_end_of_turn_record_still_yields_a_TurnEnd` — inline text+`end_turn` record with no
   tool_use (the existing shape at line ~302 already half-covers this; add the explicit
   guard-must-not-overtrigger assertion that both AssistantText and TurnEnd are present and share
   the ApiCallId). Parameterize over `end_turn` and `stop_sequence` if cheap.
3. `A_max_tokens_truncation_mid_tool_call_still_yields_a_TurnEnd` — inline record: tool_use block
   plus `stop_reason:"max_tokens"` → ToolCall AND TurnEnd(`max_tokens`). This test IS the scoping
   decision made executable; its comment carries the asymmetry rationale (suppressing a dead
   turn's end strands the session as working — CARD-0041 class).
4. `The_sibling_thinking_record_of_a_mis_stamped_response_still_yields_a_bare_TurnEnd` — fixture
   line 1 → single bare TurnEnd. Deliberately pins the ACCEPTED residual (per-record it is
   indistinguishable from CARD-0046's legitimate split-final shape), so a future option-C fix has
   a measured base and nobody "fixes" it here by accident.

Existing tests stay green untouched: the `stop_reason:"tool_use"` no-TurnEnd test (`:279`), all
split-final tests (their records are text/thinking-only), all API-error-stub tests (stop_sequence,
no tool_use). No existing test constructs tool_use + end_turn — verified by grep.

### Consumer-level tests: none needed

Consumers' behaviour doesn't change — the phantom rows simply stop being written. Every consumer
test seeds `TranscriptEntries` directly and none seeds the ToolCall+TurnEnd same-Uuid pair.
CARD-0248's plan owns the changes to `AgentTaskDeliveryWatchdogTests` (the two tests that encode
its sweep bug as expected behaviour); nothing here touches them.

### fakeclaude: NOT needed for this card

fakeclaude emits no tool_use records at all today, so reproducing the quirk there means inventing a
whole tool-calling model first. The repo's pattern justifies a fakeclaude knob when the quirk's
*timing* matters end-to-end (CARD-0046's split-final race needed a live window between the bare
TurnEnd and the text record). This fix is a pure parse seam: the verbatim fixture pins the input
shape, the normalizer tests pin the output, and the tailer/ingestion path is shape-agnostic
(covered generically by existing tailer tests). If a future e2e ever needs a live phantom window
(e.g. to exercise option C), add `ANTIPHON_FAKE_TOOLUSE_END_TURN` then, with a
FakeClaudeContractTests contract test in the existing opt-in-knob style — noted here so that
decision is deliberate, not forgotten.

## Rollout notes

- The normalizer ships inside the SessionRunner assembly that pty-hosts shadow-copy at launch:
  sessions already running keep the old code (and can keep writing phantoms) until they restart.
  No forced restart needed — the quirk is rare and CARD-0248's gates cover the settlement arm.
- **No backfill.** The 22 existing phantom rows stay: the transcript mirror is append-only
  evidence, and all affected sessions are over.
- Post-deploy monitoring (re-run after a week of fable traffic; both counts should be frozen at
  18 / 4 for rows whose sessions predate the deploy, with zero new):

```sql
SELECT COUNT(*) FROM "TranscriptEntries" te WHERE te."Kind"='TurnEnd' AND EXISTS
  (SELECT 1 FROM "TranscriptEntries" tc WHERE tc."AgentSessionId"=te."AgentSessionId"
     AND tc."Uuid"=te."Uuid" AND tc."Kind"='ToolCall');           -- same-record: frozen at 18
SELECT COUNT(*) FROM "TranscriptEntries" te WHERE te."Kind"='TurnEnd' AND te."ApiCallId" IS NOT NULL
  AND EXISTS (SELECT 1 FROM "TranscriptEntries" tc WHERE tc."AgentSessionId"=te."AgentSessionId"
     AND tc."ApiCallId"=te."ApiCallId" AND tc."Kind"='ToolCall' AND tc."Uuid"<>te."Uuid")
  AND NOT EXISTS (SELECT 1 FROM "TranscriptEntries" tc2 WHERE tc2."AgentSessionId"=te."AgentSessionId"
     AND tc2."Uuid"=te."Uuid" AND tc2."Kind"='ToolCall');         -- sibling: grows only if the
                                                                  -- residual recurs → weigh option C
```

## Build steps (for the implementing task)

1. Extract the two fixture lines into `fable-tool-use-end-turn.jsonl` (verbatim; check for secrets;
   ensure the test csproj copies the Fixtures dir — the existing fixtures already do).
2. Apply the guard in `TranscriptNormalizer.FromAssistant` (set `hasToolUse` in the `tool_use`
   case; adjust the emission condition and its comment as sketched above).
3. Add the four tests.
4. Verify: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0282/`
   `--treenode-filter "/*/*/TranscriptNormalizerTests/*"` (seconds), then the Application namespace
   chunk to prove no consumer test regressed; delete `bin-card0282` dirs afterwards.

Estimated size: ~10 lines of production code, one fixture, four tests. The consumer analysis above
is the review checklist, not extra work.
