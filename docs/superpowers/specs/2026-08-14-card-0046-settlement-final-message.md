# CARD-0046 — Settle on the response that ended the turn, not on the record that announced it

**Status:** Plan (not implemented). 2026-08-14.
**Card:** CARD-0046 "A delegate settles Succeeded on its own preamble - the tail where the verdict lives is discarded" (`e05fac79-a165-49df-8d60-8e63e0ecafd9`)
**Relates to:** CARD-0003 (delivery failures surfaced), CARD-0020 (phase-aware deadlines, `2026-08-10-card-0020-phase-aware-deadlines.md`), CARD-0041 (manual compaction ends the turn), CARD-0029/CARD-0037 (delivery ceilings).
**Supersedes:** the first draft of this file (written by task ff320d72 and never delivered — see §1.4). Its mechanism was right; its *timing model* and its model-tier reading were wrong, and both are corrected here against measurement.

---

## 1. What actually happens

### 1.1 Nothing stopped early. The reports are all still in the database.

Six of the seven tasks in the card's evidence table ran their turn to completion and wrote a full
final report. That report is in `TranscriptEntries` right now. What the caller received is the
narration that preceded it.

| task | role/tier | premature `TurnEnd` | the final message, discarded | stored `Result` |
|---|---|---|---|---|
| 1b1cdca5 | Code/**High** | seq 389, 22:00:31 | seq 390 — "**Shipped and pushed** — `ce48f50`…" (6 296 ch) | 447 ch narration |
| f2bf457c | Debug/**High** | — none — | seq 110 settled *with* the report | 5 421 ch — **control, correct** |
| 26421cf2 | Review/Frontier | seq 33, 07:47:36 (+3 earlier) | seq 34 — "**Verdict: keep as is…**" (6 195 ch) | 515 ch preamble |
| 68857095 | Review/Frontier | seq 40, 07:49:01 | seq 41 — "Verdict: **keep as is**…" (5 622 ch) | 546 ch narration |
| e0f79fef | Review/Frontier | seq 60, 07:49:20 | seq 61 — "**Verdict: the client-side move guard is sound…**" (4 804 ch) | 861 ch narration |
| 1d12d227 | Review/Frontier | seq 65, 07:50:43 | seq 66 — "**Verdict: keep as is.**…" (4 573 ch) | 1 133 ch narration |
| ff320d72 | Plan/Frontier | seq 97, 11:05:43 | seq 98 — "Plan delivered: `docs/superpowers/specs/2026-08-14-card-0046-…`" (5 850 ch) | 289 ch narration |

ff320d72's stored `Result` is *literally* the three "I'll start by…" sentences it emitted at seqs 2,
21 and 26, joined. Its actual report — and the file it names — exist. This is the task the caller
believed "died at 13m44 despite being told not to end its turn": it did not end its turn early, it
worked for 13m44, wrote its deliverable, and reported. **Settlement fired 0.73 s before the report
row was persisted.**

### 1.2 The mechanism

Claude Code writes one API response as **several JSONL records** — a `thinking` record, then a
`text` record — and stamps **every one of them** with the response's final `stop_reason`. Raw JSONL,
session 7f9d06a5 (task ff320d72), response `msg_011Ce2Xog1xCJs9P`:

```
2026-08-14T11:05:43.708Z  stop_reason=end_turn  content=[thinking]   (thinking text: "", signature only)
2026-08-14T11:06:01.701Z  stop_reason=end_turn  content=[text]       (5 850 chars — the report)
```

Then, in order:

1. `TranscriptNormalizer.FromAssistant` (`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:118`)
   appends a `TurnEnd` part to any record whose `stop_reason` ≠ `tool_use`. The thinking block's
   text is **empty** in every one of the 1 936 thinking blocks on this machine (signature only), so
   `FromAssistant` skips the `Thinking` part at :101 and the record yields **a bare `TurnEnd` and
   nothing else** — which is why the DB holds 1 366 `TurnEnd` rows and zero `Thinking` rows.
2. `AgentSessionRuntime.ObserveTranscriptAsync` handles **one entry at a time**. `IsTurnBoundary`
   accepts the bare TurnEnd (`AgentSessionRuntime.cs:261`), `IsUnseenTurnBoundaryAsync` says unseen,
   and `FlushQueueOnIdleAsync` runs — *before the next entry is even received*.
3. `AgentTaskReplyService.OnTurnEndAsync` → `ExtractMarkedTurnAsync`
   (`AgentTaskReplyService.cs:458`) takes `MAX(Sequence)` over `TurnEnd`, walks back to the last
   `UserPrompt`, and joins **every `AssistantText` persisted at that instant**. The narration
   between tool calls is non-empty, so the "no text yet — leave it running" guard at :93 does not
   fire. `SettleAsync` stamps **Succeeded**, prices the task, merges, releases the delegate and
   delivers the narration to the caller.
4. The text record lands 0.01–1.2 s later. Its `AssistantText` arrival re-triggers settlement
   (`AgentSessionRuntime.cs:219 → :350`) exactly as designed — but there is no Dispatched/Working
   task any more. It no-ops. The report is persisted and read by nothing.
5. The text record's own `TurnEnd` sibling is then discarded as a duplicate by the ApiCallId dedup
   (`:298-306`) — by construction, the boundary that gets acted on is always the earlier, bare one.

**Correction to the first draft: the file does not carry the marker seconds early.** The two lines
are written to the JSONL *together*, at the end of the response; the thinking record's `timestamp`
field is merely backdated to when its block finished. Measured persist gap between the two rows
(`TranscriptEntries.CreatedAt`, 300 ms tailer poll): **0.01–1.17 s**, against record-`Timestamp`
gaps of 1–30 s. 1d12d227 settled 9 ms after its bare TurnEnd was stored and 36 ms before its
report. This matters for the fix in two ways: **the grace window can be short**, and **any timing
rule must read `CreatedAt`, never `Timestamp`** — the backdated timestamp would expire a grace
window up to 30 s early.

Because the two records are written in a fixed order into an append-only file, this race is
**deterministic in direction** and can be closed by identity (shared `message.id` → `ApiCallId`),
not by timing.

### 1.3 The model-tier lead, tested — real, but it is a *frequency* effect on a model-agnostic bug

Nothing in the working/idle or turn-boundary code reads the model: `TranscriptKinds`,
`IsTurnBoundary`, `IsWorkingAsync`, the client `isWorking()` and the runner's
`TranscriptWorkingState` contain no model-conditional branch, and no record shape here is unique to
fable. What differs is **how often each model emits its final response as thinking-then-text**.
Measured over every JSONL under `~/.claude/projects/C--src-Antiphon/`:

| model | `end_turn` responses | emitted thinking-first, text-last | rate |
|---|---|---|---|
| claude-fable-5 | 52 | 31 | **60 %** |
| claude-opus-5 | 124 | 27 | **22 %** |
| claude-haiku-4-5 | 2 | 2 | (n too small) |
| claude-sonnet-5 | 2 | 0 | (n too small) |

Restricted to the response that **ends a transcript** — the one that settles a delegated task —
the gap is starker: **fable 10/11 (91 %), opus 2/10 (20 %)**.

So the caller's correlation is real and is worth ~4.5× in per-task failure probability, but the
conclusion "fable is broken" is wrong and would send the fix to the wrong place. Three things fall
out of the numbers that the 7-task sample cannot show:

- **It hits opus too.** 1b1cdca5 (High) has the identical shape and lost a 6 296-char report. Across
  all 27 settled tasks in the database, **13 (48 %) have a >500-char assistant message that landed
  after settlement**: 8/12 Frontier (67 %), 5/13 High (38 %).
- **Tier and role are perfectly confounded in the card's sample.** All five Frontier tasks were
  Review/Plan; both High tasks were Code/Debug. For a Review or Plan task the final message *is* the
  entire deliverable, so losing it looks like "the agent did nothing". For a Code task the
  deliverable is a commit, so 1b1cdca5 read as "completed its work" even though its report was
  destroyed by the same defect. **Severity tracks role; incidence tracks tier.**
- **The one control is a coin-flip, not a category.** f2bf457c is the single session in the seven
  whose final response was one record. Nothing about opus makes it safe.

### 1.4 The second defect: a turn that launched background subagents ends legitimately (26421cf2)

26421cf2 announced four `Agent` tool calls, each answered by a ToolResult reading `Async agent
launched successfully. (This tool result is internal metadata …)`, then wrote an announcement and
ended the turn for real (seq 18 text, seq 19 TurnEnd, same ApiCallId — this one is *not* the split
shape). Settlement harvested the announcement at 07:44:10. The session stayed warm in the pool, four
`<task-notification>` UserPrompts arrived at 07:45:25–07:47:33, and the delegate folded all four
reviews into a 6 195-char verdict at **07:48:06 — four minutes after its task was Succeeded and
priced**. The board shows no children because the built-in `Agent` tool is invisible to Antiphon
(`AgentTaskService.cs:129` refuses Workers).

Slice 1 does not help this shape: the announcement turn's text *had* landed. It needs its own rule.

### 1.5 What settlement checks about the deliverable: nothing

`SettleAsync` (`AgentTaskReplyService.cs:204`) stamps Succeeded on any non-empty, non-question text.
There is no check that the captured text is the turn-ending response's, no record of what the report
was built from, and no signal anywhere that a report is 289 characters of "I'll start by…". All
seven tasks settled through the identical path.

### 1.6 The card's "suspected contributor", tested

The card suspects `antiphon-delegate/SKILL.md`'s "Don't poll. End your turn; it will reach you"
licenses delegates to stop early. **Refuted as the mechanism for 6 of 7**: those delegates did not
end a turn early — settlement ran early. For 26421cf2 it is a plausible contributor (it did end a
turn awaiting background agents, which is also just correct Claude Code behaviour). The skill text
is still worth scoping (slice 6), but it is hygiene, and **no prompt change can fix any of this** —
which is exactly what ff320d72's death proved.

---

## 2. Design principles

- **The report is the text of the response that ended the turn.** That is already the contract the
  brief states ("Your final message is the entire report the caller receives"). Everything below is
  making settlement honour it.
- **Close the race by identity, not by timing.** The two records share one `message.id`, persisted
  as `TranscriptEntries.ApiCallId`. Deferring until *that response's own text* exists is exact; a
  sleep or debounce is not.
- **The acted-on boundary does not move.** The first-of-ApiCallId dedup in
  `IsUnseenTurnBoundaryAsync` is load-bearing for queue flush and the "Agent finished" toast
  (2026-08-06: 13 toasts in one second). The fix lives in `AgentTaskReplyService`'s extraction.
- **A deferral must have a backstop, because text-less `end_turn` responses are real.** 1 of 180 in
  the corpus: opus session cefed08a, `msg_011Ce1MCABw7CkGwKZXazgAj`, a lone thinking record with
  `end_turn`, followed 106 ms later by a *different* response carrying `API Error: Connection lost
  mid-response`. Without a backstop that task would sit Dispatched forever — and note this one
  should never have settled Succeeded either.
- **Nothing in the working/idle lockstep changes.** No slice touches `TranscriptKinds`' working
  rules, `IsWorkingAsync`, `isWorking()` or `TranscriptWorkingState`. Slice 4 adds classifiers used
  by settlement only.
- **Never judge the prose.** "Does this report carry an outcome?" is not decidable here. "Is this
  report the turn-ending response's own text?" is a fact, and it is the one slice 3 reports on.

---

## 3. Slices

Each slice is independently landable, green-buildable and revertable. Only slice 3 depends on
slice 1.

### Slice 1 — Settlement waits for the turn-ending response's own text

**Files:** `server/Application/Services/AgentTaskReplyService.cs` (`ExtractMarkedTurnAsync`,
`OnTurnEndAsync`), `server/Application/Settings/DelegationSettings.cs`,
`server/Application/Services/AgentTaskDispatcher.cs` (one new sweep call in `TickAsync`).

Replace the bare `MAX(Sequence) TurnEnd` read with:

1. Load the turn-ending `TurnEnd` **row** (max sequence), keep its `ApiCallId` and `CreatedAt`.
2. `ApiCallId == null` → behave exactly as today. This is the legacy/fake path: `SeedTurnAsync` in
   the existing suite stamps no ApiCallId on its TurnEnd, and `Antiphon.FakeClaude` emits no
   `message.id` at all, so all 26 existing tests keep their current path unchanged.
3. An `AssistantText` row with the **same `ApiCallId`** exists → the final message has landed;
   proceed (report per today's whole-turn join in this slice; slice 2 narrows it).
4. Otherwise, if `now − turnEnd.CreatedAt < FinalMessageGraceSeconds` → return `TurnOutcome.Nothing`
   and log at Debug. The task stays Dispatched; the text record's own `AssistantText` arrival
   re-triggers `OnTurnEndAsync` through the path that already exists
   (`AgentSessionRuntime.cs:219 → :350`) and settles with the report included. Measured worst case
   is 1.17 s.
   *Use `CreatedAt`, not `Timestamp`* — §1.2.
5. Past the grace → settle on whatever text exists, and return the fact that the final message was
   missing so slice 3 can make it loud. Until slice 3 lands, log it at Warning.

**New setting:** `DelegationSettings.FinalMessageGraceSeconds`, default **120**. Measured need is
~1.2 s; 120 s absorbs a tailer stall or a stream gap and still sits far under
`DeliveryFailTimeoutMinutes` (10). Not zero-able by accident: `<= 0` means "never defer" and must be
documented as the escape hatch.

**The sweep** — nothing re-triggers a genuinely text-less response, so the grace needs a clock.
`AgentTaskDispatcher.TickAsync` already runs `AutoEscalateStalledAsync`, `FailNeverStartedAsync` and
`RetireIdleWarmAgentsAsync` **before** its `queued.Count == 0` early return, on a 5 s
`PollIntervalSeconds` cadence. Add `SettleDeferredReportsAsync(ct)` there: for each Dispatched task
with a session whose latest `TurnEnd.CreatedAt` is older than the grace, call
`AgentTaskReplyService.OnTurnEndAsync` (a cheap no-op in every other case). Do **not** put this in
`SessionHealthHostedService` — its cadence is coarser and its concern is sessions, not tasks.

**Tests** — `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs`. Extend
`SeedTurnAsync` (line 830) so the TurnEnd can carry an `ApiCallId`, and add `SeedSplitTurnAsync`
producing the measured shape (narration text with id A → bare TurnEnd with id B → text with id B →
TurnEnd with id B):

- `a_turn_end_whose_own_response_has_not_written_its_text_does_not_settle` — narration + bare TurnEnd
  `B`, no `AssistantText B` → still Dispatched, `Result` null. **Red today**; this is the six-task
  failure verbatim.
- `the_final_messages_arrival_settles_the_task_with_the_report` — then insert `AssistantText B` and
  re-invoke → Succeeded, `Result` contains the verdict.
- `a_turn_end_with_no_api_call_id_settles_as_it_always_did` — explicit regression guard; the other 25
  tests are the broad one.
- `a_response_that_never_writes_text_settles_after_the_grace_window` — `FakeTimeProvider` advanced
  past the grace → settles on the available text (assertions grow in slice 3).
- `the_live_split_response_tail_settles_once_with_the_report` — replay session 7f9d06a5's exact tail
  (seq 97 bare TurnEnd, seq 98 text, seq 99 TurnEnd, one `ApiCallId`) through three
  `OnTurnEndAsync`/`AssistantText` invocations in arrival order; assert exactly one settlement and
  `Result` = the 5 850-char message.
- `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs`:
  `a_deferred_settlement_is_swept_after_the_grace_window` — drive `TickAsync`, assert the deferred
  task settles without any further transcript arriving.

### Slice 2 — The report *is* the final message

**File:** the same extraction.

With slice 1 guaranteeing the turn-ending response's text is present, build the report from the
`AssistantText` rows sharing the turn-ending `ApiCallId`, joined in sequence order (one response can
carry several text blocks), falling back to today's whole-turn join when `ApiCallId` is null. This is
the card's third "what good looks like" bullet.

Consequences, all improvements: `LooksLikeAQuestion` finally inspects the actual final message;
`BuildCompletionNote`'s head+tail excerpt excerpts the verdict rather than the preamble;
`ResolveSpillFileAsync` is unchanged. The control f2bf457c's report would become its 4 020-char final
message instead of 5 421 chars of joined chatter.

**Trade-off, taken deliberately:** a delegate that front-loads findings mid-turn and ends with
"done" loses that mid-turn text from `Result` (it stays in `TranscriptEntries`). The brief already
forbids that shape, and slice 3 makes a suspiciously thin report visible. Record the discarded
length in the `Completed` task event ("reported 4 573 chars (final message; 1 133 chars of mid-turn
narration not included)") so the loss is never silent.

**Tests:** `the_report_is_the_turn_ending_responses_own_text` (narration id A + final message id B →
`Result` is the final message alone); `a_response_split_over_several_text_blocks_is_joined_in_order`;
`a_marked_turn_settles_the_task_and_stores_the_report_verbatim` stays green because `SeedTurnAsync`
uses one id per turn.

### Slice 3 — A settlement that could not get the final message is loud, never silently Succeeded

**Files:** `AgentTaskReplyService.cs`, `server/Domain/Enums/AgentIncidentKind.cs` (add
`DelegateFinalMessageMissing = 18` — int column, **no migration**), mirroring
`RecordUncorrelatedReportAsync`'s incident+alert pattern (`AgentTaskReplyService.cs:116`).

When the grace backstop settles without the turn-ending response's text:

- **Some fallback text** → settle **Succeeded**, raise `DelegateFinalMessageMissing` (Warning) with
  an incident on the agent timeline, an `AgentTaskEventType.Warning` event naming what the report was
  built from, and a warning line prefixed to the completion note so the **caller** learns the report
  may be preamble.
- **No text at all** at grace expiry → **fail** the task through `AgentTaskDispatcher`'s existing
  `FailAsync` path, which already notifies the parent and feeds the retry/escalation ladder — the
  card's "fail or retry". This is the cefed08a "Connection lost mid-response" case, and failing is
  the correct verdict for it.

**Tests:** slice 1's grace test grows incident + completion-note assertions;
`a_turn_with_no_text_at_all_at_grace_fails_the_task` (parent gets a note, status Failed, reason
names the missing final message); `the_incident_is_raised_once_per_session` (mirrors the existing
uncorrelated-report dedup).

### Slice 4 — A turn that launched background subagents is not finished

**Files:** `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` (`TranscriptKinds`),
`AgentTaskReplyService.cs`, `DelegationSettings.cs`.

New classifiers, housed with `IsLocalCommandRecord`/`IsManualCompactBoundary` and used **by
settlement only** — no working rule may consume them:

- `IsTaskNotificationPrompt(kind, text)` — `UserPrompt` whose text starts with `<task-notification>`.
- `AsyncAgentLaunchMarker = "Async agent launched"` — the ToolResult text Claude Code returns for a
  background `Agent` spawn (pinned from ac09cffd seq 11).
- `TryReadNotifiedToolUseId(text, out id)` — reads `<tool-use-id>…</tool-use-id>` out of a
  notification. **This is the strong link:** the notification names the `toolu_…` id of the launch it
  answers, so launches and completions pair exactly rather than being counted.

Extraction changes:

1. Walking back from the turn-ending TurnEnd to the turn's prompt, **skip task-notification
   prompts** — the marked brief still owns the span. Without this the marker gate fails on every
   notification turn, `RecordUncorrelatedReportAsync` fires, and `FailNeverStartedAsync`'s
   uncorrelated branch (`AgentTaskDispatcher.cs:284`) kills the task at 10 minutes. **This is a
   hazard slice 4 creates and must fix in the same commit.**
2. Within the span, collect `ToolCall` rows with `ToolName == "Agent"` whose paired `ToolResult`
   (by `ToolUseId`) contains `AsyncAgentLaunchMarker`, and the notified tool-use ids from
   notification prompts.
3. Any launched id without a matching notification → return `Nothing`; the task stays Dispatched.
   Each notification's turn ends with a real TurnEnd that re-triggers extraction, and the last one
   settles with the real verdict (ac09cffd seq 34).
4. Grace: `DelegationSettings.SubagentGraceMinutes`, default **30**, measured from the span's last
   entry `CreatedAt` — a background subagent can die and never notify. On expiry settle on the
   latest final message plus a slice-3 incident. Swept by slice 1's `SettleDeferredReportsAsync`.

**Tests** (`AgentTaskReplyIntegrationTests.cs`, replaying ac09cffd seqs 9–35):

- `a_turn_that_launched_background_agents_does_not_settle_on_its_announcement`
- `a_task_notification_turn_is_not_an_uncorrelated_report` — the hazard from (1); red without the
  prompt skip.
- `the_last_subagent_notification_settles_the_task_with_the_verdict` — exactly one settlement,
  `Result` = the 6 195-char verdict.
- `a_synchronous_agent_call_settles_normally` — `Agent` ToolCall whose result carries no marker.
- `a_subagent_that_never_reports_settles_after_the_subagent_grace` + incident.
- Classifier unit tests next to the existing `TranscriptKinds` tests.

**Independence:** needs slice 1's plumbing (the turn-ending row), not slices 2–3.

### Slice 5 — Make the fake and the fixtures carry the real shape

**Files:** `src/Antiphon.FakeClaude/Program.cs` (`JsonAssistantLine`, ~line 476),
`tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs`, new
`tests/Antiphon.Tests/Agents/Fixtures/split-final-response.jsonl` (the two real lines from
7f9d06a5), `tests/Antiphon.Tests/Agents/TranscriptNormalizerTests.cs`.

fakeclaude today emits one record per turn with **no `message.id`**, so nothing below the service
layer can exercise any of the above through a real ConPTY. Two changes:

- Always emit `message.id` (`msg_fake_…`). Faithful, and safe: the AssistantText part is persisted
  before the TurnEnd part of the same record, so slice 1's check passes and no fake-driven test
  defers.
- `ANTIPHON_FAKE_SPLIT_FINAL=1` (opt-in, **default OFF**, like `ANTIPHON_FAKE_STDIN_CLIP`): write
  the turn as two records sharing one id — `content:[{type:"thinking",thinking:""}]` then
  `content:[{type:"text",…}]`, both `stop_reason:"end_turn"`.

**Tests:** `TranscriptNormalizerTests` over the fixture — a signature-only thinking record yields
**exactly one part, a `TurnEnd` carrying the ApiCallId** (this is the shape the whole plan rests on);
`FakeClaudeContractTests.A_split_final_response_reaches_the_server_as_a_bare_turn_end_then_text`.
Optionally a headed `ClaudeSplitResponseCanaryTests` alongside `ClaudeCompactionCanaryTests` /
`ClaudeInterruptCanaryTests` in `tests/Antiphon.Agents.Pty.Tests/`, to catch Claude Code changing the
record layout.

### Slice 6 — Scope the skill guidance to the caller (docs only)

**File:** `.claude/skills/antiphon-delegate/SKILL.md` (the "Don't poll. End your turn; it will reach
you" guidance, ~:121). Put it under an explicit **"For the caller"** heading and add a delegate-facing
rule: *your final message is the report; if you spawn background agents the task will not settle
until their notifications return, and your last message after folding them in is what the caller
receives.* No tests. Do not oversell it — §1.6.

### Slice 7 (optional, land last) — Retire the ChannelReplyDispatcher split-reply workaround

`ChannelReplyDispatcher.DispatchAsync` (`:125-130`) met this exact race on 2026-07-24 and defends
with defer-if-empty **plus a follow-up dispatch** — meaning Telegram routinely receives a reply split
into two messages for the same reason tasks lost their verdicts. Reusing slice 1's same-ApiCallId
rule there (defer instead of dispatch-then-follow-up) makes replies single messages, with the
follow-up path kept as the safety net. Behaviour is visible in a real chat, so land it separately
and last. Pinned by the existing dispatcher tests in
`tests/Antiphon.Tests/Application/ChannelBatchingTests.cs` and `ChannelBridgeTests.cs`, plus one new
defer-shape test.

### Slice 8 (optional) — Recover what was lost, read-only

13 of 27 settled tasks have a >500-char assistant message that landed after settlement. A read-only
report — `scripts/` or a `delegate.ps1 -LostReports` switch — listing task, session, stored `Result`
length and the recoverable final message would return real value immediately. **Do not rewrite
stored `Result` values in place:** a task row is a record of what the caller was actually handed, and
silently rewriting history would make the 2026-08-13/14 postmortem unreadable.

---

## 4. Landing order

1 (root fix, red test first) → 5 (fixtures/fake, so 2–4 can be pinned below the service layer) → 2
(contract) → 3 (visibility) → 4 (defect B) → 6 (docs) → 7/8 (optional).

Slice 1 alone converts every one of the six losses into a correct settlement, because the existing
`AssistantText` re-trigger already re-runs extraction and the extraction is not capped at the
TurnEnd sequence. Slices 2–4 are about making the result *right* and its failures *visible* rather
than about recovering the text.

---

## 5. What I could not determine, and what would settle it

1. **Whether `FinalMessageGraceSeconds = 120` is generous enough under a stream gap.** The measured
   in-session worst case is 1.17 s, but a server restart delivers the tail via
   `SyncTranscriptAsync` backfill and I did not measure that path's latency. Evidence: restart the
   server mid-response in the E2E fixture and time the gap; or instrument
   `SettleDeferredReportsAsync` to log every deferral it resolves and read a week of it.
2. **Whether `Async agent launched successfully.` is stable across Claude Code versions**
   (slice 4's discriminator). Same exposure as the local-command shapes; settle with a canary in
   `tests/Antiphon.Agents.Pty.Tests/` as `ClaudeLocalCommandCanaryTests` does. Until then it is a
   pinned constant with a fixture from ac09cffd, and phrase drift degrades safely — the task settles
   on the announcement turn, i.e. today's behaviour, rather than hanging.
3. **Whether a `<task-notification>` prompt can ever be the *only* prompt in a task's span** (a
   warm-pool delegate adopted mid-notification). If so, slice 4's prompt-skip could walk back past
   the brief into a previous task's span. Not observed. Settle by bounding the walk-back at the
   task's `DispatchedAt`, which is cheap and worth doing regardless.
4. **How often the split shape strands *chat* replies** (slice 7's value). The follow-up path hides
   it; counting `ChannelReply` sends whose session had a same-ApiCallId text arrive later would
   quantify it in a day.
5. **The idle-while-streaming worry is much smaller than the first draft claimed, but not zero.**
   Because both records are written at the end of the response, the window in which the session
   reads idle while text is still coming is the 0.01–1.2 s persist gap, not the 11–30 s the record
   timestamps suggest. A `WhenIdle` message can still be typed into a composer during it. Settle by
   instrumenting the queue for deliveries that land inside that window; file a card only if it
   happens.
6. **The deeper fix I did *not* take, and why.** `TranscriptNormalizer.FromAssistant` is the one
   place that knows a record produced no content other than its `TurnEnd`; suppressing the boundary
   there would fix settlement, the queue flush and the "Agent finished" toast in one stroke. It is
   rejected for now because the text-less `end_turn` (§2, cefed08a) would then produce **no boundary
   at all** and strand every `WhenIdle` delivery on that session — the exact failure CLAUDE.md
   records for 2026-07-29 and 2026-07-31 — and avoiding it needs a timer inside three lockstep
   implementations. Revisit only with a measurement of how often a bare `end_turn` record is the
   whole response.
7. **The card's evidence table needs a correction** (cards are write-once, so record it wherever
   CARD-0046's resolution is written): 1b1cdca5, 26421cf2, 68857095, e0f79fef, 1d12d227 and ff320d72
   did not stop early or die. Their reports are in `TranscriptEntries` at the sequences in §1.1 and
   have been recoverable since the moment they were written. "$41.11 for one usable report" is
   accurate about what the caller *received*; the work itself was done and paid for six times over.
