# CARD-0241 — Answering an in-turn Grok question popup is unmarked, unconfirmable, and undetected

**Date:** 2026-09-01 (Plan pass, task 18842ca1 — design only; no code changed)
**Card:** CARD-0241. Diagnosis 2026-08-30 (CARD-0159 §7) + verification addendum 2026-09-01 (task 5d26e82e). Both still accurate. Do not re-investigate.
**Related:** CARD-0159 (settlement half — shipped; a `cancelled` TurnEnd no longer settles Succeeded), CARD-0055 (Sent needs a confirming transcript row), CARD-0137 (overlay Esc recovery), CARD-0292 (inert queue-operation rows — **opposite policy**, do not copy).

**Sources (verified this pass):** `GrokTranscriptNormalizer.cs:25-27,98-107,179-193`, `SessionMessageQueueService.cs:153,169-223,1592-1624,1706,1876-1905,2024-2045,2774-2806`, `AgentTaskReplyService.cs:232-256,279-327,522-536`, `ProviderContractCatalog.cs:138-142`, `RemoteControlMenuScreen.cs`, `delegate.ps1:120-230`, `AgentTaskEndpoints.cs:100-123`, `AttentionDtos.cs:164` (`QueuedInputStuck = 20`), CARD-0294 plan (`UnmarkedWaiting = 21` on paper, unimplemented), incident `updates.jsonl` for session `53a16758` (task `c9c86f92`, still on disk).

---

## Decision

Three independent layers. S1+S2 alone would have returned 200 on the first "Proceed as planned" and the retry would never have happened.

1. **Ingest only the completed update of a question-tool** as `ToolResult` (S1). Do not ingest every `tool_call_update`. Do not widen CARD-0055 to all `ToolResult` rows (Claude already emits those for file/command output).
2. **A narrow confirm arm** (S2) matches the typed body against those question-tool `ToolResult` texts. CARD-0292's new kinds stay excluded; this is the opposite policy at the same site.
3. **`-Reply` grows a Now-capable overlay path** (S3). Not a new `-Answer -Now` flag. Blocked stays WhenIdle + task marker. An in-turn question-tool send is Now, **no marker prefix**, and **persists a queue row** so a 409 cannot be retried blind.
4. **Dedicated matcher** (S4), CARD-0292 `RemoteControlMenuScreen` shape: recognize-then-act. Act is **type the answer**, never Esc. Fragments stay out of Grok `DetectFragments` (S6 would Esc-dismiss a live question before the body is typed).

No new `AttentionKind`. Live enum ends at `QueuedInputStuck = 20`. CARD-0294 claims 21 on paper and has not shipped; this card does not take a number.

---

## Ground truth (checked, not guessed)

### Measured `ask_user_question` wire shape (incident `updates.jsonl`, 2026-08-23)

Not assumed. Lines 86 / 87 / 91 of
`C:\Users\lndco\.grok\sessions\C%3A%5CAntiphon%5Cworktrees%5Ccard-task-c9c86f92\53a16758-ab46-4f19-b955-3dc705680206\updates.jsonl`:

| Line | `sessionUpdate` | `title` | `status` | `_meta["x.ai/tool"]` | Payload |
|---|---|---|---|---|---|
| 86 | `tool_call` | **`ask_user_question`** | (empty) | `name=ask_user_question`, `kind=ask_user`, `label=Ask User` | `rawInput.questions[].options[].label` |
| 87 | `tool_call_update` | human question prefixed `Ask:` | (empty) | same name/kind | rendering; **not** an answer |
| 91 | `tool_call_update` | **empty** | **`completed`** | **absent** | `content.content.text` = `User has answered your questions: "<question>"="<option label>". You can now continue with the user's answers in mind.` |

Same `toolCallId` (`…-25`) on all three. The completed row cannot be recognized by `title` or `_meta`; it must join the opening `tool_call` via `toolCallId`.

`FromToolCall` (`:191-192`) stores `title` as `ToolName`. On the opening row that **is** `ask_user_question` (also true of the existing `read_file` fixture at `GrokTranscriptTailerTests.cs:39-40`, where title and `_meta.name` coincide). Prefer `_meta["x.ai/tool"].name` when present so a future title-as-prose opening row still gates.

`kind=ask_user` is the family tag. MCP form-input / URL-consent / hook-confirm "share the same popup" (CARD-0159 §3 table) — ingest a completed update when the **opening** call's meta `kind` is `ask_user` **or** `name` is `ask_user_question`. Do not invent MCP tool names; if a later canary shows a different `kind`, add it then.

Completed `read_file` updates (incident lines 88–90: `status=completed`, other toolCallIds, file-body content) stay skipped. Ingesting every completed update would flood `ToolResult` with command output.

### Why confirm never closed

`TryFindConfirmingRecordAsync` (`:2024-2045`) and the unobservable twin (`:1884-1905`) only read `UserPrompt` / `QueuedUserPrompt`. A `ToolResult` row, even if ingested, is invisible until that filter grows a **gated** arm. CARD-0292 added `QueueEnqueue`/`QueueDequeue`/`QueueRemove` as activity exclusions (`:2650`) — inert, must-not-confirm. Do not put question-tool `ToolResult` on that list.

`Mode:Now` (`EnqueueAsync:169-223`) types immediately, creates **no** queue row, so a false 409 has nothing to late-confirm. The operator's incident send was this path.

### Why `-Reply` cannot land today

| Verb | Gate | Mode | Marker |
|---|---|---|---|
| `AnswerAsync:232-256` | `Status == Blocked` else 409 | WhenIdle | `TaskMarker` + body (`:254-256`) |
| `RefineAsync:279-327` | Dispatched/Working | WhenIdle | `BuildRefinement` wraps marker at **both** ends (`DelegationReportFormatter.cs:522-536`) |
| Session `Mode:Now` | live session | Now | none, no row |

The popup holds the turn `working` (no `TurnEnd` since the brief). WhenIdle will not flush. Blocked is false (task still Dispatched). Now is the only send that types — and it 409s (defect 1).

### Marker placement (compose, don't fight)

The overlay answer is **the same turn as the brief**. `ExtractMarkedTurnAsync` already correlates on the brief's `UserPrompt` marker (seq 1 in the incident). Prefixing `[antiphon-task:…]` onto popup text would:

- fail option matching (`Proceed as planned (Recommended)` vs a marked paragraph),
- appear inside the completed `ToolResult` quoted answer, not as a `UserPrompt`,
- not help settlement, which is still looking at the original marked prompt.

**In-turn overlay send: type the answer only, no marker.** Settlement stays on the brief. Confirm matches the typed answer against `ToolResult.Text` (the measured wrapper contains the option label).

**Blocked `AnswerAsync`: unchanged** — WhenIdle + marker. That path starts the *next* turn after unblock.

### Overlay Esc vs answer

S6 (`DeliverAsync:1592-1603`) Escs when `DetectFragments` match the pre-send snapshot, **then** types. Putting the question popup in Grok `DetectFragments` (`:138-142`, still `["c copy session ID"]`) would dismiss the question and type into the composer of a working turn — the incident's second send. S5 reactive Esc (`:1618`) is already idle-gated and withheld while working; still do not teach it this modal.

S4 matcher is a separate type. If it matches: **do not Esc**; the body about to be typed is the answer.

---

## Slices

### S1 — Normalize question-tool `completed` updates to `ToolResult`

`GrokTranscriptNormalizer`:

- Keep skipping non-completed `tool_call_update` (line 87 shape).
- Bounded map `toolCallId → { name, kind }` filled from `tool_call` and from updates that still carry `_meta["x.ai/tool"]`. Cap like `_completedPromptIds` (64).
- On `sessionUpdate=tool_call_update` AND `status=completed`: if the mapped opening call is a question-tool (`name == "ask_user_question"` OR `kind == "ask_user"`), emit one `TranscriptPart(ToolResult, text=content.content.text, toolName=mapped name, toolUseId=toolCallId)`. Otherwise `[]`.
- Prefer `_meta["x.ai/tool"].name` over `title` when setting `ToolCall.ToolName` on the opening row (no change for current fixtures).
- JSON key is literally `"x.ai/tool"` (slash in the property name).

Copy incident lines 86/87/91 into `GrokTranscriptTailerTests` as verbatim fixtures (not the 818 KB file). Pin: 86 → `ToolCall` with `ToolName=ask_user_question`; 87 → still nothing; 91 → `ToolResult` whose text contains `Proceed as planned (Recommended)`; a completed `read_file` update → still nothing.

`ToolResult` stays **activity** (working/idle lockstep). The turn is still in flight; excluding it would hide the wedge from the watchdog.

### S2 — Narrow confirm arm (observable + unobservable + Mode:Now grace)

`TryFindConfirmingRecordAsync` and `TryFindUnobservableConfirmingRecordAsync`: also consider rows where

- `Kind == ToolResult`, and
- `ToolName` is `ask_user_question` **or** text starts with the measured prefix `User has answered your questions:`

then `TranscriptConfirm.Classify(body, text)` as today (identity + completeness). Do **not** add `ToolResult` without that gate. Do **not** add `QueueEnqueue`/`QueueDequeue`/`QueueRemove`. Claude `ToolResult` for Bash/Read stays non-confirming (different `ToolName`, different text).

Enter-only retries stay. After a successful overlay answer the composer is empty and the session is working — a re-Enter is a no-op (measured). Confirm should hit the `ToolResult` before the 30 s timeout.

Pin: delivery-verification test that inserts a question-tool `ToolResult` past the baseline and expects Sent / no second Enter; negative: a `read_file` / Claude `ToolResult` with overlapping text does **not** confirm.

### S3 — `-Reply` overlay Now; persist a row; no new flag

`AnswerAsync`:

| Task state | Open question-tool on the session? | Behaviour |
|---|---|---|
| Blocked | n/a | today: WhenIdle + `TaskMarker` + body |
| Dispatched / Working | yes (newest `ToolCall` with mapped question-tool name and no later `ToolResult` for that `ToolUseId`) | persist a Delegation queue row, `DeliverAsync` immediately (Now semantics), body = **trimmed answer only** |
| Dispatched / Working | no | 409 naming Refine (WhenIdle) vs Reply-on-Blocked vs Reply-on-open-question |
| else | | today |

"Open" is transcript-grounded after `CatchUpTranscriptAsync`, not a screen guess. Persist the row **before** typing so a later 409 has a baseline for late-confirm (the Mode:Now hole).

`delegate.ps1 -Reply` unchanged at the CLI (same POST `/api/agent-tasks/{id}/reply`). No `-Answer`, no `-Now` switch. Session `Mode:Now` stays row-less; S2 is what makes that send return 200 when it answered a popup.

Do not send `BuildRefinement` through the popup (marker at both ends, multi-line, Grok join-unsafe).

### S4 — `GrokQuestionPopup` matcher; withhold Esc

New static matcher beside `RemoteControlMenuScreen` (Application, DI-free). Conservative: two independent literals, measured by an `[Explicit]` headed canary (`GrokQuestionPopupCanaryTests`) against real Grok — the incident did not capture a pty screen (CARD-0159 §9). Until the canary lands, do **not** guess fragments from the JSONL question text (that is the tool payload, not the chrome).

Canary pins: popup present after `ask_user_question`; `/usage` screen does not match; one typed option + Enter clears it; Esc is **not** the answer path.

Wire:

- `DeliverAsync` S6: if matcher hits, **skip** Esc; type the body (it is the answer).
- `TryDismissOverlayAsync` / S5: if matcher hits, return false (no Esc).
- `DetectFragments` for Grok **unchanged**.

### S5 — fakegrok + tests (matrix below)

Opt-in fakegrok question-tool: opening `ask_user_question` holds the turn working, writes the three measured JSONL shapes, a submit while open writes the completed update (not a `user_message_chunk`), a second submit-while-working still cancels (CARD-0159 S0 knob stays honest).

---

## What this card does not do

- Codex abort ingestion (CARD-0159 §1.4 / this card's "own separate card" note).
- New `AttentionKind` (live next free is 21; CARD-0294's unimplemented `UnmarkedWaiting` already claims it on paper — re-read `AttentionDtos.cs` if that changes).
- Putting the popup in `DetectFragments` or Esc-dismissing it.
- Prefixing `TaskMarker` onto overlay answers.
- Widening confirm to every `ToolResult`.
- Copying CARD-0292's inert-kind exclusion list.
- Changing Blocked `AnswerAsync` or WhenIdle `RefineAsync` except the overlay branch on Reply.
- Backfilling the 2026-08-23 incident session.

---

## Test matrix

| Layer | Test |
|---|---|
| `Antiphon.SessionRunner.Tests` | Incident fixtures: opening `tool_call` → `ToolCall` `ToolName=ask_user_question`; rendering update → empty; completed question update → `ToolResult` with the option label; completed `read_file` update → empty; existing `Real_turn_rows_normalize…` still 6 kinds (no extra `ToolResult`) |
| `Antiphon.Tests` Application | Confirm: question-tool `ToolResult` past baseline confirms, no re-Enter; Claude/read_file `ToolResult` does not; `QueueEnqueue` still does not (CARD-0292 pin stays) |
| `Antiphon.Tests` Application | `AnswerAsync` Blocked still WhenIdle + marker; Working + open question-tool → Now, no marker, row persisted, Sent on `ToolResult`; Working + no question-tool → 409 pointing at Refine |
| `Antiphon.Tests` Application | `RefineAsync` still WhenIdle + `BuildRefinement`; Blocked still refused |
| `Antiphon.Agents.Pty.Tests` | fakegrok question-tool: enqueue-while-open writes completed update, no user chunk; Esc does not complete the tool |
| Headed `[Explicit]` | `GrokQuestionPopupCanaryTests` — screen literals + answer-clears; `GrokSubmitWhileWorkingCanaryTests` unchanged |
| Catalog | Grok `DetectFragments` still only `"c copy session ID"` |

Run per `docs/testing-and-build.md`: `dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-card0241/` (forward slash), then `Antiphon.Tests` filtered, then `Antiphon.Agents.Pty.Tests`. Delete `bin-card0241` dirs after. Do not co-schedule Tests and Pty.

---

## Sequencing and risks

**Order: S1 → S2 (closes the 409), S3, S4+canary, S5 alongside.** S2 hard-depends on S1. S3 hard-depends on S2 (otherwise Reply-Now still 409s). S4 can land with S3; without measured fragments it is withhold-Esc-only once the canary writes the literals.

| Risk | Disposition |
|---|---|
| Completeness match fails because `ToolResult` wraps the answer | Measured text **contains** the option label; `Classify` is contains-on-normalized. Pin with the incident string. A custom free-text answer must appear in the wrapper — canary |
| MCP popup uses a different `kind` than `ask_user` | Family gate is measured `kind=ask_user` OR `name=ask_user_question`. Add names only from a canary |
| Marker omitted → next TurnEnd does not correlate | Same turn; brief already marked. Pin ExtractMarkedTurn on a ToolResult-then-TurnEnd fixture |
| Reply-Now into a working non-popup still cancels | Open-question-tool gate after CatchUp; else 409 |
| Matcher false-positive Esc withhold on `/usage` | Two independent literals; `/usage` negative in the canary |
| CARD-0294 ships `AttentionKind = 21` first | This card adds none. Re-read the enum if a later slice wants a feed row |

---

## Execute notes (not this pass)

- Redact and check in the three incident JSONL lines as fixtures; do not commit the full `updates.jsonl`.
- Headed canary needs a real Grok session that calls `ask_user_question` (prompt it to ask a two-option question). Capture `SnapshotScreen` into the matcher constants.
- After S1+S2, a Mode:Now "Proceed as planned" against a live popup must return 200 with no second send.
