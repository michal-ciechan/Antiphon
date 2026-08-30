# CARD-0159 — a cancelled Grok turn settled a task `Succeeded` on its own narration: plan

**Card:** CARD-0159 · **Task:** 0bfe009e (Plan, Frontier) · **Date:** 2026-08-30 · **Code re-read on:** `33dd9ed`
**Related:** CARD-0046 (final message is the report), CARD-0055 (Sent needs a `UserPrompt` row),
CARD-0024 (identity without completeness → `Truncated`), CARD-0117 (brief-row precedence),
CARD-0137 (overlay-aware delivery), CARD-0080 (Grok ACP ingestion), CARD-0227 (Shared git
evidence is unattributable), CARD-0040 (a `Succeeded` settle moves the card to Review).

This is a design document. No production code was written for it.

## Verdict up front

1. **The card's inference is confirmed from the code, and it is worse than the card says.**
   `AgentTaskReplyService.ExtractMarkedTurnAsync` needs exactly four things to hand
   `SettleAsync` a report: the session's newest `TurnEnd` row, a prompt before it carrying the
   task marker, at least one non-error `AssistantText` row after that prompt, and (Claude only,
   in practice) that the turn-ending response has written its own text. `SettleAsync` then sets
   `Succeeded` unless one of the last two lines ends with `?`. **Nothing reads `StopReason`,
   nothing looks at git, nothing checks the report against the contract the brief asked for, and
   the "final message" discriminator CARD-0046 added is structurally inert on Grok** — Grok's
   normalizer stamps one `ApiCallId` (the `promptId`) on every row of a turn, so "the response
   that ended the turn" *is* the whole turn's coalesced narration, and `NarrationDiscardedChars`
   is always 0. The 489 characters were not a bug in the join; they were the join working as
   written.

2. **What ended the turn was a `turn_completed` with `stop_reason = "cancelled"`** — not the
   overlay's own timeout, and not the first `Mode:"Now"` send (that one *worked*: it answered the
   `ask_user_question` popup at 18:26:19Z and the delegate resumed real work for 38 seconds). The
   first send returned a **false 409** because CARD-0055's confirm loop looks for a `UserPrompt`
   row and an overlay answer produces a `tool_call_update`, which the Grok normalizer skips. The
   operator, told it had failed, sent it again at 18:26:57Z into a session that was now mid-turn.
   Grok's TUI treats a submitted prompt during a running turn as *cancel the turn, start a new
   one*: `turn_completed cancelled` at 18:26:57.644, `user_message_chunk "Proceed as planned"` at
   18:26:57.687 (43 ms). `AgentSessionRuntime.IsTurnBoundary` counts a Grok `cancelled` end as a
   boundary **on purpose** (so WhenIdle deliveries flush — correct), and the task-reply observer
   hangs off the same trigger with no way to tell "idle" from "reported". Settlement fired 41 ms
   after the row was stored, and `ReleaseDelegateAsync` then **killed the session 1.5 s later —
   killing the turn that was about to do the work.**

3. **The gap is general, the trigger is Grok-specific today.** Claude's API never emits
   `cancelled`, a Claude Esc writes a `[Request interrupted…` user record and no `TurnEnd`, and
   Codex's normalizer ingests no abort at all — so this exact chain is Grok's. But "settle on
   whatever `AssistantText` exists at the newest boundary" is kind-agnostic, and an `end_turn`
   whose final message is "Proceeding with S1 and S2…" (a model that stops to narrate its plan)
   settles `Succeeded` on every kind today.

4. **Fix, recommended:** four slices, in this order. **S1** a `cancelled` boundary is an idle
   boundary but never a report boundary (structural, one predicate, closes the incident exactly).
   **S2** the report contract gains a closing verdict line the delegate must emit
   (`[antiphon-report:<id> done|blocked|failed]`); an `end_turn` without it gets **one** marked
   nudge, and only after that settles — with the evidence class recorded on the row, in the
   Completed event and in the caller's header, so a `Succeeded` that was not positively reported
   is never silent. **S3** git facts at settlement, observability only: the completion header
   says `git=2 commits, 5 files` or `git=no changes`, and a Worktree task in a code-producing role
   that changed nothing gets a Warning the caller reads above the report. **S4** the Grok
   normalizer segments `AssistantText` per response (text → tool call → text are separate rows)
   so CARD-0046's rule means the same thing on Grok as on Claude. The overlay-answer half —
   a marked, transcript-confirmable way to answer an in-turn question popup, and detecting the
   popup — is a **separate card** (§7); it is the delivery side and this card is the settlement
   side, exactly as CARD-0117 and CARD-0024 were split.

5. **Explicitly rejected:** a "does this text look like narration" classifier as a gate (the
   repo refuses generic shape-matching for good reason — CARD-0047 — and a future-tense heuristic
   would misread half the real reports); git evidence as a gate (Plan/Review/investigation tasks
   legitimately change nothing, and Shared checkouts are unattributable, CARD-0227); a new
   `AgentTaskStatus` for "unverified" (ripples through every consumer of `IsSettled` for a
   distinction the evidence column and header line carry just as well); making settlement read
   the check interpreter's "LOOKS STUCK" reading (a model's opinion must not gate a state machine).

## 1. What the code does today

### 1.1 The trigger

`AgentSessionRuntime` persists each runner transcript event and, for every entry where
`IsTurnBoundary` is true and the boundary is unseen, calls the three reply observers and the
queue flush (`AgentSessionRuntime.cs:339`, `:424-:471`):

```csharp
private static bool IsTurnBoundary(SessionRunnerTranscriptEvent entry) =>
    (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == "end_turn")
    || (entry.Kind == TranscriptKinds.TurnEnd && entry.StopReason == "cancelled")   // Grok Esc — measured, CARD-0080 S1
    || TranscriptKinds.IsInterruptPrompt(entry.Kind, entry.Text);
```

The comment on the `cancelled` arm says why it is there: *"the queue must flush on it or every
WhenIdle delivery strands until some later turn completes."* That is right for the queue. It is
the only place the two consumers' needs diverge, and nothing downstream re-reads the reason.

### 1.2 Settlement — `AgentTaskReplyService.OnTurnEndAsync` → `ExtractMarkedTurnAsync` → `SettleAsync`

`ExtractMarkedTurnAsync` (`AgentTaskReplyService.cs:1316`):

| Step | What it requires | Where the incident's turn stood |
|---|---|---|
| newest `TurnEnd` row, by sequence | exists | seq 36, `StopReason=cancelled`, `ApiCallId=08600b09…` (the promptId) |
| the last `TurnPrompt` with `Sequence < turnEnd` | exists, has text | seq 1 — the brief pointer (seq 38 "Proceed as planned" was not yet persisted: `CreatedAt` 18:27:05) |
| marker gate | prompt text contains `[antiphon-task:c9c86f92]` | yes — the brief |
| API-error stub | `end` is not a stub | not a stub |
| async subagents | none unanswered | none |
| final-message gate (CARD-0046) | `AssistantText` rows whose `ApiCallId == end.ApiCallId` | seq 35, 489 chars, same promptId → `Landed` |
| report | that text | the 489-char join, `NarrationDiscardedChars = 0` |

`SettleAsync` (`:383`) then: `Result = report`, `Status = LooksLikeAQuestion(report) ? Blocked :
Succeeded`, cost roll-up, spill/deliverable pointers, the `Completed` event ("Delegate reported
489 characters."), `MergeBackAsync`, scope drift, `ReleaseDelegateAsync` (a Worktree delegate is
**killed** here — `IDelegateSessionStopper.KillAsync`), `DeliverToParentAsync`.

**Positive-evidence check: none.** Concretely, none of these are consulted anywhere on the path:
`TranscriptEntry.StopReason`; whether the branch has commits (`DelegationWorktreeService.
TryMergeBackAsync` runs `rev-list --count` **only when `MergeTargetRef` is set** — with no target
it commits whatever is dirty and returns `LeftForHuman` unconditionally, `:238`); whether the
report has the shape `ReportingContract` asked for ("Lead with the outcome in one line…",
`DelegationReportFormatter.cs:123` — a request with no reader); whether the task's card moved;
whether tests ran. The one content check, `LooksLikeAQuestion`, is a `?` on the last two lines.

### 1.3 Why CARD-0046's discriminator did not help — per kind

CARD-0046 made "the report is the turn-ending response's OWN text, not the turn's join" the
rule, keyed on `ApiCallId`. What `ApiCallId` *means* differs by normalizer:

| Kind | `ApiCallId` on `AssistantText` | `ApiCallId` on `TurnEnd` | Net effect of the CARD-0046 rule |
|---|---|---|---|
| Claude | Anthropic `message.id` — one per API response | the ending response's id | works: narration from earlier responses is discarded and named |
| Codex | `turn_id` only on `phase == final_answer` (`CodexTranscriptNormalizer.cs:242`) | `turn_id` | works when a final answer exists; a turn with none → `NeverArrived` after the grace → settles on the join **with a warning** |
| Grok | `promptId` — **one per turn**, and all `agent_message_chunk`s are coalesced into ONE row at `turn_completed` (`GrokTranscriptNormalizer.cs:227,264`) | `promptId` | **inert**: the "final message" is the whole turn's narration; the 489 chars are five status lines glued with no separator |

So on Grok the report is always the join, and the join is always everything the delegate said
in the turn. `a_turn_end_with_no_api_call_id_settles_as_it_always_did` pins the legacy path; no
test pins "one id per turn".

### 1.4 What the interrupt path does on the other kinds

- **Claude:** `TranscriptNormalizer.cs:142` emits `TurnEnd` for any `stop_reason` other than
  `tool_use` (`end_turn`/`stop_sequence`/`max_tokens`). An Esc writes `[Request interrupted…`
  (a user record) and **no `TurnEnd`**; the newest `TurnEnd` is then a pre-brief one (or none), so
  the walk-back lands on an unmarked or absent prompt and nothing settles. The queue still
  flushes (`IsInterruptPrompt`). Correct by accident of shape, not by design.
- **Codex:** `task_complete → TurnEnd` (`CodexTranscriptNormalizer.cs:171`), no `stop_reason`
  (stored null). An abort is not ingested at all, so a Codex delegate interrupted mid-task stays
  `Working` until `TaskDeadlinePolicy`. That is the *opposite* failure and out of scope here, but
  §7 names it.

## 2. The incident, reconstructed from the stored rows and the raw file

Sources: `AgentTasks`/`AgentTaskEvents`/`TranscriptEntries`/`SessionQueuedMessages` for task
`c9c86f92` (session `53a16758`), and Grok's own
`~/.grok/sessions/C%3A%5CAntiphon%5Cworktrees%5Ccard-task-c9c86f92/53a16758-…/updates.jsonl`
(116 lines, still on disk). Server log for 2026-08-23 is past its 5-day retention; the runner
log survives but records no input writes. Times UTC.

| When | Source | What |
|---|---|---|
| 17:55:08 | events | Dispatched, Worktree `card-task-c9c86f92` on `feat/card-task-c9c86f92`, **no merge target** |
| 17:55:54 | jsonl 1 | brief pointer typed (seq 1, marked) |
| 17:56–17:57 | jsonl 2–85 | 26 tool calls — the delegate reads the plan, the code, the tests |
| 17:57:07.170 | jsonl 86–87 | `tool_call ask_user_question` — "Proceeding with S1+S2… Any preference before I start?" with options *Proceed as planned (Recommended)* / *Hold* |
| 17:58:23 | queue | operator's `-Refine` "Proceed as planned…" queued **WhenIdle** (row 2). It never went out: the session read *working* (no `TurnEnd` since seq 1) for the whole hour |
| 18:25:41 | events | check #? interpreter reading: **"LOOKS STUCK — … asked user question at #27 … 28m ago. No response received; session idle since"** |
| ~18:26:1x | (Mode:Now #1) | operator sends "Proceed as planned" `Mode:"Now"` — typed straight into the popup (the popup is not in `TerminalOverlayContract.DetectFragments` for Grok, which knows only `"c copy session ID"`) |
| 18:26:19.151 | jsonl 91 | `tool_call_update … completed`: **"User has answered your questions: … = \"Proceed as planned (Recommended)\""** — the send WORKED |
| 18:26:21–18:26:54 | jsonl 92–113, 115 | thought "The user confirmed to proceed. Let me implement S1 and S2 now", `todo_write`, greps, reads, two `pwsh` runs, thought "Now let me implement all the changes" — **real work resumed** |
| ~18:26:49–55 | (Mode:Now #1) | CARD-0055 confirm loop finds no `UserPrompt` row (an overlay answer is a `tool_call_update`, which `GrokTranscriptNormalizer` skips), re-presses Enter at +7/+14 s (an empty Enter into a working Grok composer is a no-op — no `turn_completed` between 18:26:19 and 18:26:57), gives up → **409 `NoTranscriptRecord`** to the operator |
| ~18:26:57 | (Mode:Now #2) | operator re-sends the same text. The session is working, so CARD-0137 S5's Esc is withheld by design (`TryDismissOverlayAsync` gates on `working == false`), and the body + Enter go into the composer of a running turn |
| 18:26:57.644 | jsonl 114 | `turn_completed stop_reason=cancelled prompt_id=08600b09…` **(with a usage block — the normalizer's comment that cancelled carries none is true of Esc, not of this shape)** |
| 18:26:57.687 | jsonl 116 | `user_message_chunk "Proceed as planned"` — a new turn opens 43 ms later |
| 18:26:58.086–.168 | rows 34–36 | server stores Thinking (1 803 chars), `AssistantText` (489), `TurnEnd cancelled` |
| 18:26:58.209 | events | **`Completed` "Delegate reported 489 characters."** then `Completed` "Branch … kept — no merge target was set." — 41 ms after the row |
| 18:26:59.223 | session | `EndedAt` — `ReleaseDelegateAsync` killed the Worktree delegate, and with it the "Proceed as planned" turn that had just started |
| 18:27:04.875 | queue | the WhenIdle `-Refine` finally started its first delivery attempt against the boundary — into a dead session (runner logged two unhandled-exception 500s at 19:27:04 local) |
| 18:27:05 | rows 37–38 | the trailing Thinking chunk and the "Proceed as planned" prompt are stored, after the task is already gone |

Two facts the card did not have: (a) the popup was **answered successfully by the first send**
and the task was 38 seconds into real implementation when it was cancelled; (b) the second send
is what produced the `cancelled` boundary. The operator was acting on a false 409.

**Is "submit into a working turn cancels it" measured?** Not by a canary. It is established here
by the raw ordering (`turn_completed cancelled` 43 ms before the `user_message_chunk`, no Esc
possible from the code path because the working gate was true) and by Grok's own README (Enter =
"Send message", Esc = "Cancel current operation"). S0 below pins it.

## 3. Answers to the brief's questions

**Q1 — the code path.** §1.1–1.2. `AgentSessionRuntime.IsTurnBoundary` → `AgentTaskReplyService.
OnTurnEndAsync` → `ExtractMarkedTurnAsync` → `SettleAsync` (`Succeeded` at `:391`).
`AgentTaskDispatcher.SettleDeferredReportsAsync` (`:1366`) is the only other caller and only
re-invokes `OnTurnEndAsync` after the CARD-0046 grace windows.

**Q2 — any positive signal required?** No. See the table in §1.2. It accepts whatever
`AssistantText` exists between the marked prompt and the newest boundary, unconditionally, with
the CARD-0046 narrowing applying to Claude only.

**Q3 — what triggered the `TurnEnd`.** A `stop_reason=cancelled` `turn_completed`, produced by
Grok when the second `Mode:"Now"` prompt was submitted into a running turn. Not the overlay's
timeout (the overlay had been answered 38 s earlier), not a side effect of forcing text into the
popup (that send did exactly what the operator wanted). **Scope: the chain that *produced* the
cancel is Grok-and-overlay-specific; the settlement that *accepted* it is general** — it would
accept any Grok `cancelled` boundary (a human's Esc in the delegate's terminal, a
`POST /sessions/{id}/input` Esc, a CARD-0137 S5 Esc on an idle session) and, on every kind, any
`end_turn` whose text is narration.

**Q4 — other Grok overlays with the same focus-stealing property.** From Grok's README /
CHANGELOG on this machine (1.0.x) and the measured session files:

| Overlay | Opens on | Notes for this card |
|---|---|---|
| **questions popup** | `ask_user_question` tool (22 uses across local Grok sessions), **MCP form-input / URL-consent** ("through the same popup used for questions"), **hook confirm** ("hooks can now ask the user to confirm a tool call"), **credit-limit upsell** ("the same question modal") | typed text + Enter answers it (measured 18:26:19); answer is a `tool_call_update`, not a `UserPrompt`; not detected by `DetectFragments`; the turn stays *working* throughout |
| tool-approval prompt | `run_terminal_command` etc. when not in auto/always-approve mode | new sessions default to auto mode since 1.0.x; a delegate launched with approvals on would park exactly like the popup |
| `/usage` panel | `/usage` | CARD-0137's shape; the only fragment in the catalog |
| `/login` copy-link overlay, `/load`/resume picker, `/agents` and extensions modals (Ctrl+L, `/plugins`), TODO panel (Ctrl+T), debug panel (Ctrl+P), quit confirm (Ctrl+D) | slash/keys | not tool-driven; a delegate will not open them on its own |

They need the same *delivery-side* scrutiny as CARD-0137 (detect, answer, confirm) — **not
solved in this card** (§7). What this card must do about them is narrower: make sure that
however a stuck popup gets un-stuck, the resulting boundary cannot settle the task falsely.

**Q6 — consistency with CARD-0117 / CARD-0024.** Both fixes replaced a *negative* inference
("no evidence of X" ⇒ act) with a *positive* requirement, and both kept the weaker path but made
it loud and non-destructive rather than deleting it: CARD-0024 kept identity but added
completeness and parked instead of retyping; CARD-0117 made the brief's own row outrank the
transcript and stopped the kill. S1+S2 follow the same shape: a `cancelled` boundary is a fact
about idleness, not about completion; a report without its closing line is settled only after
the delegate was asked once, and then with its evidence class on the record.

## 4. Design

### 4.1 S1 — a `cancelled` boundary is never a report boundary

`TranscriptKinds` gains the stop-reason vocabulary that already exists as string literals in two
places (`AgentSessionRuntime.cs:340,346`, `RunnerGrokAdapterTurnCompleteTests`):

```csharp
public static class StopReasons { public const string EndTurn = "end_turn"; public const string Cancelled = "cancelled"; }

/// A TurnEnd the delegate FINISHED, as opposed to one that ended because something stopped it.
/// Null is the legacy/synthetic/Codex row and keeps today's behaviour; only a measured interrupt
/// value is excluded.
public static bool IsReportBoundary(string? kind, string? stopReason) =>
    kind == TurnEnd && !string.Equals(stopReason, StopReasons.Cancelled, StringComparison.Ordinal);
```

`ExtractMarkedTurnAsync`, immediately after loading `end` and before the walk-back:

```csharp
if (!TranscriptKinds.IsReportBoundary(end.Kind, end.StopReason))
    return new TurnOutcome(null, false, Interrupted: new(end.Sequence, end.Uuid, end.StopReason));
```

`OnTurnEndAsync` handles `Interrupted` by writing **one** `AgentTaskEventType.Warning` per
boundary (`Detail` = `"Turn interrupted (cancelled) at #36 — not a report; the task stays
Working."`, dedup on the boundary's sequence via the existing event table) and returning. The
task stays `Working`, the interrupted turn's text stays in `TranscriptEntries`, the queue flush is
untouched (`IsTurnBoundary` is not changed). `SettleDeferredReportsAsync` arm (1) must skip a
cancelled `end` too, or it would re-invoke settlement on it after the grace.

Why narrow to `cancelled`, not "anything but `end_turn`": Claude's `max_tokens` /
`stop_sequence` boundaries have never been measured here to mean "stopped, not finished", and
Codex's `TurnEnd` carries no reason at all; turning unmeasured values into non-boundaries could
strand real completions until the deadline — a new failure to fix a hypothetical one. Only the
value we have *measured* to mean "stopped, not finished" is excluded; anything else unusual is
worth a Warning event naming the reason, never a refusal to settle.

What this does to the incident: settlement declines at 18:26:58; the "Proceed as planned" turn
runs (it was 38 s into implementing); its `end_turn` walks back to the **unmarked** seq-38 prompt
→ `UncorrelatedReport` → a once-per-task `DelegateReportUncorrelated` Warning (CARD-0117 S1
scoping); the operator's **marked** `-Refine` row, still Pending, is typed at that idle point;
the delegate answers it ("already done — report follows") → marked `end_turn` → settles on a
real report. One spurious warning, no lost work, no false Succeeded. That path is the existing
CARD-0117 brief-row precedence doing its job, which is why the overlay-answer follow-up can be
its own card rather than a blocker here.

### 4.2 S2 — the report closes with a verdict line; an unmarked `end_turn` is asked once

**Contract.** `DelegationReportFormatter.ReportingContract` (typed brief and pointer alike) and
`server/Bundles/delegate-basics.md` gain:

> End your final message with one line, on its own: `[antiphon-report:<id> done]` if the work
> is complete, `[antiphon-report:<id> blocked]` if you need a decision or an answer to continue,
> `[antiphon-report:<id> failed]` if you could not do it. Nothing after it. Without that line the
> harness cannot tell your report from a status update and will ask you once.

A new token, not the task marker: `[antiphon-task:…]` is the *prompt* correlation token and is
scrubbed out of bodies typed into other sessions (`AgentTaskCheckService.ScrubTaskMarkers`); the
report token is read from `AssistantText` — the transcript, not the pty — so CARD-0027 clipping
cannot eat it, which is the one property the brief's marker does not have.

**Parsing.** `DelegationReportFormatter.TryReadReportVerdict(taskId, text, out verdict, out
body)` — last non-empty line, exact id, case-insensitive verdict, body = text without the line.
A token naming a *different* task id is not a verdict (a sub-orchestrator quoting its delegate).

**Settlement.** `TurnOutcome` gains `ReportVerdict? Verdict`. In `SettleAsync`:

| Verdict | Status | Evidence recorded |
|---|---|---|
| `done` | `Succeeded` | `Marked` |
| `blocked` | `Blocked` | `Marked` |
| `failed` | `Failed`, `FailureReason` = first line of the body | `Marked` |
| none, last-two-lines `?` | `Blocked` (today's heuristic, kept) | `QuestionHeuristic` |
| none, task not yet nudged, session live | **not settled**: `ReportNudgedAt = now`, one marked WhenIdle queue message (same shape as `AnswerAsync`): *"[antiphon-task:id] Your turn ended without the closing report line. If the work is finished, send the report now, ending with `[antiphon-report:id done]` (or `blocked` / `failed`). If it is not finished, continue."*; `Warning` event | — |
| none, already nudged (or `Role == Check`, or session dead) | `Succeeded` on the text, as today | `UnmarkedAfterNudge` / `Exempt` |
| CARD-0046 `FinalMessageMissing` | as today, then the same nudge rule | `FinalMessageMissing` |

Two new columns on `AgentTask`: `ReportEvidence` (enum: `Legacy=0, Marked, UnmarkedAfterNudge,
QuestionHeuristic, FinalMessageMissing, Exempt`) and `ReportNudgedAt DateTime?`. One migration,
existing rows `Legacy`.

**Surfaces.** The `Completed` event says `"Delegate reported 1 204 characters (verdict: done)."`
or `"… (no closing line; settled after one nudge)."`; `BuildCompletionNote`'s header gains
`report=marked|unmarked` next to `drift=`; the DTO carries `ReportEvidence` so the delegations
page can badge it; `DelegationReportFormatter.StatusWord` is unchanged. **A `Succeeded` with
`UnmarkedAfterNudge` is still `Succeeded`** to `CardWorkTransitionService`, the away digest and
the pool — that is a deliberate decision (D2 below), not an oversight.

**Why nudge-then-settle rather than never-settle.** A strict gate turns every non-compliant
model into a stranded task until the 240-minute deadline, then a `Failed` over work that may be
complete — recreating the "looks failed, actually succeeded" shape the memory warns about, at
the cost of the deadline's wait. One nudge costs one short turn, recovers the compliant case
(the model is told exactly what to type), and leaves the evidence class on the row for the
non-compliant case. The nudge is WhenIdle and marked, so it cannot double-type into a working
composer and its answer correlates through the existing gate.

**Why `Role == Check` is exempt.** The check interpreter's reading is consumed by
`AgentTaskCheckService` in its own format; nudging it would cost a model turn per check and
buys nothing — its verdict is never a "Succeeded the caller trusts".

### 4.3 S3 — git facts at settlement, observability only

`SettleAsync` already calls `DelegationWorktreeService.TryMergeBackAsync`, which commits the dirty
tree. After it (so the count includes the sweep commit), gather what `DelegateCheckProbe.
GatherGitAsync` already knows how to gather — but with a base that exists for the no-target
case. Today `CreateForTaskAsync` branches from `MergeTargetRef ?? "HEAD"` and **persists neither**
(`AgentTask.WorktreeId` is null for delegation worktrees — there is no `Worktree` row to read
`BaseRef` from — and the resolved SHA is only logged). S3 adds `AgentTask.WorktreeBaseSha`,
captured at creation with `GitWorkspaceService.GetHeadShaAsync` on the new worktree (folded into
S2's migration; legacy rows null → `git=base unknown`). Base = `MergeTargetRef ??
WorktreeBaseSha`; `commits = rev-list --count base..HEAD`, `files = diff --name-only base..HEAD`
count. `GatherGitAsync`'s "the task-branch range could not be determined" arm uses the same
column, so the check digest and the completion header agree. Shared/ReadOnly tasks:
`git=unattributable` (CARD-0227 rule, no counting).

- Header: `git=3 commits, 7 files` / `git=no changes` / `git=unattributable`.
- `Warning` event + caller line when `Workspace == Worktree`, role ∈ {`Code`, `Test`, `Commit`,
  `Coverage`, `Debug`, `Docs`}, `commits == 0`, and the report body does not contain
  "no changes" / "nothing to change" (a literal phrase list, not a classifier — the delegate is
  told in the contract to say "no changes needed" when that is the outcome): *"This Worktree task
  in a code role produced no commits on `feat/…`. Verify before merging."*

Never a gate, never a status change, never a kill: Plan/Review/investigation tasks produce
documents or nothing, and the operator reads the header. The incident's note would have read
`[task c9c86f92 done] … git=no changes` with the warning above the 489 characters — which is
the exact `git log` check the operator happened to make, now in the note by default.

### 4.4 S4 — Grok: one `AssistantText` row per response, so "final message" means the same thing

`GrokTranscriptNormalizer.AccumulateOrEmit` coalesces every `agent_message_chunk` of a
`promptId` into one row emitted at `turn_completed`. Change: a `tool_call` for the same
`promptId` **closes the current message segment** (emit the pending `AssistantText`/`Thinking`
then, with a per-segment `ApiCallId` of `{promptId}:{segmentIndex}`), and `turn_completed`
stamps its `ApiCallId` with the **last** segment's id. The rows then look like Claude's: text →
tool call → text are three responses; `FinalMessageOf` finds only the text after the last tool
call; `NarrationDiscardedChars` names the rest; the `CompletedPromptIds` post-completion arm keys
on the base `promptId` as today. Usage stays on the `TurnEnd` (unchanged).

Effect on the incident's turn (had it been `end_turn`): the last segment after the 18:26:43
tool calls had **no text** → CARD-0046 `NeverArrived` after the 120 s grace → settle with the
`FinalMessageMissing` warning → S2's nudge. Effect on a normal Grok report: the report is the
closing message, not five status lines glued together — the concatenation-with-no-separator the
card quotes is also this (the fake and the fixtures should gain the segment shape).

Cost: a normalizer test-suite update (`GrokTranscriptNormalizerTests`, fixtures from the
reference session). `DelegationUsageRollup` groups by `ApiCallId` and takes `Max` per group; on
Grok the usage numbers sit only on the `TurnEnd` row, so per-segment ids on the text rows add
groups whose usage is null (counted as 0) and the turn is still priced once — pin that with a
rollup test. CARD-0157's context badge reads the `TurnEnd` usage (unchanged). Droppable if the
build runs long: S1+S2 close the incident without it; S4 is what makes CARD-0046 true on Grok.

### 4.5 S0 — pin the mechanism

- `RunnerGrokAdapterTurnCompleteTests` already pins "cancelled is a completed turn" for the
  adapter; add the settlement-side twin in `AgentTaskReplyIntegrationTests`:
  `a_cancelled_turn_end_does_not_settle_the_task_and_says_so` (seeded with `SeedTurnAsync(…,
  stopReason: "cancelled")` — the helper needs the parameter; `BridgeQueueHarness` already takes
  one).
- fakegrok: `ANTIPHON_FAKE_SUBMIT_WHILE_WORKING=cancel` makes a prompt received mid-turn emit
  `turn_completed stop_reason=cancelled` then the `user_message_chunk` (the measured order, 43 ms
  apart), for `SessionMessageQueueGrokPtyIntegrationTests`.
- One headed `[Explicit]` Grok canary, `GrokSubmitWhileWorkingCanaryTests`: a long-running tool
  turn, a typed prompt with Enter, assert the raw `updates.jsonl` order. Keeps the fake honest the
  way `FakeVsRealClipParityTests` does.

## 5. Decisions for the operator

- **D1 — nudge once, then settle with evidence (recommended), or never settle without the
  line.** §4.2 argues for the nudge. Strict is a one-line change on top if the evidence column
  shows non-compliance is rare enough to make it cheap.
- **D2 — does `Succeeded` + `UnmarkedAfterNudge` still auto-move the card to Review
  (CARD-0040)?** Recommended **yes, unchanged in this card**; the transition sweep can read
  `ReportEvidence` later if unmarked settles turn out to be where false completions hide.
- **D3 — ship S4 (Grok segmentation) now or as its own card.** Recommended now, last slice,
  droppable: it is the only change that makes a *normal* Grok report stop being the turn's
  narration join.
- **D4 — verdict vocabulary.** `done|blocked|failed` recommended; `blocked` retires the `?`
  heuristic over time without deleting it.
- **D5 — the overlay-answer card (§7) is separate.** Recommended: file it, do not block this one.

## 6. Slices

| Slice | Scope | Areas |
|---|---|---|
| **S0** | tests that pin the mechanism (§4.5) — the settlement test is red on master, the fake knob, the `[Explicit]` canary | delegation, grok, tests |
| **S1** | `TranscriptKinds.StopReasons` + `IsReportBoundary`; `ExtractMarkedTurnAsync` `Interrupted` outcome; once-per-boundary Warning event; `SettleDeferredReportsAsync` skips cancelled ends; `AgentSessionRuntime` uses the constants | delegation |
| **S2** | report token in `ReportingContract` + `delegate-basics.md` (+ `orchestrator.md` roll-up line); `TryReadReportVerdict`; `ReportEvidence`/`ReportNudgedAt` columns + migration; nudge path; event/header/DTO surfaces; `LooksLikeAQuestion` kept as fallback; `Role == Check` exempt | delegation, docs |
| **S3** | `AgentTask.WorktreeBaseSha` captured in `CreateForTaskAsync`; git facts at settlement (base = `MergeTargetRef` ?? `WorktreeBaseSha`), `git=` header, code-role no-change Warning; `DelegateCheckProbe.GatherGitAsync` shares the base resolution | delegation |
| **S4** | Grok per-response segmentation; normalizer tests + fixtures; fakegrok emits segments | grok |
| **S5** | verification: `AgentTaskReplyIntegrationTests`, `AgentTaskDeliveryWatchdogTests`, `GrokTranscriptNormalizerTests`, `RunnerGrokAdapterTurnCompleteTests`, `SessionMessageQueueGrokPtyIntegrationTests`; one live Grok delegate through `delegate.ps1` that ends with the token; card close-out; AGENTS.md gotcha | tests, docs |

Dependencies: S1 alone closes the incident and is safe to ship first. S2 needs S1 (a nudge into
a cancelled turn would be answered by the *next* turn's end, which S1 makes correlate). S3 and
S4 are independent of each other and of S2.

## 7. Follow-up card (delivery side, not built here)

**"Answering an in-turn question popup is unmarked, unconfirmable and undetected."** The chain
in §2 has three delivery-side defects, each with a home in existing machinery:

1. **CARD-0055's confirm has no arm for a tool-answered prompt.** `GrokTranscriptNormalizer`
   skips `tool_call_update`; ingesting the `completed` update of an `ask_user_question` (and the
   MCP/hook popups that share it) as a `ToolResult` row (Grok emits none today) gives
   `WaitForTranscriptConfirmAsync` a row whose text contains the typed answer — the first send
   would have returned 200, and the second send would never have happened.
2. **No marked, immediate answer path.** `AnswerAsync` requires `Blocked`; `RefineAsync` is
   WhenIdle and cannot land while the popup holds the turn *working*; `Mode:"Now"` carries no
   marker and persists no row. A `-Answer -Now` (or `RefineAsync(mode: Now)`) that is allowed
   when the session's newest `ToolCall` is a known question tool with no result yet.
3. **The popup is not in `TerminalOverlayContract.DetectFragments`** for Grok; CARD-0137 S6's
   detector could name it (fragments to be measured from the rendered screen) — and unlike
   `/usage`, the right response is *answer*, not Esc.

Also noted for its own card: Codex ingests no turn abort (§1.4), so a Codex delegate interrupted
mid-task strands until the deadline.

## 8. Test coverage (new, by slice)

- S0/S1: `a_cancelled_turn_end_does_not_settle_the_task_and_says_so` (red on master);
  `a_cancelled_end_is_skipped_by_the_deferred_report_sweep`; `the_interrupted_warning_is_written_
  once_per_boundary`; `the_next_end_turn_after_a_cancel_settles_normally`.
- S2: `TryReadReportVerdict` unit cases (last line, wrong id, `blocked`/`failed`, body strip);
  `an_end_turn_without_the_closing_line_is_nudged_once_not_settled`; `the_nudged_delegates_
  marked_reply_settles_with_marked_evidence`; `a_second_unmarked_end_turn_settles_as_unmarked_
  after_nudge`; `a_check_role_task_is_never_nudged`; `a_failed_verdict_fails_the_task_with_the_
  first_line_as_reason`; the header carries `report=`.
- S3: `a_worktree_code_task_with_no_commits_warns_in_the_header`; `a_plan_task_with_no_commits_
  does_not_warn`; `a_shared_task_reports_git_unattributable`.
- S4: normalizer: `a_tool_call_closes_the_message_segment`; `turn_completed_carries_the_last_
  segments_id`; settlement: `a_grok_turn_whose_last_segment_is_empty_defers_then_warns`.
- Positive controls (this repo's habit): remove `IsReportBoundary` and S1's test must go red
  with exactly the incident's `Completed` event; remove the nudge and S2's test must show a
  `Succeeded` on narration.

## 9. What was NOT determined

- Whether Grok queues rather than cancels when the submitted prompt arrives while the model is
  mid-*stream* (as opposed to mid-tool-call). The incident was mid-tool. S0's canary should try
  both.
- The real popup's rendered fragments (for §7.3) — the pty screen was not captured.
- Whether a `cancelled` `turn_completed` *always* carries usage when the cancel is a re-prompt
  (this one did; the Esc shape measured in CARD-0080 did not). Harmless either way — usage is
  read off the `TurnEnd` row regardless of reason.

## 10. Environment / cleanup

No code, no builds, no `bin-*` directories created. Evidence read from the live database
(read-only queries), `~/.grok/sessions/...` and `%TEMP%\antiphon-logs\session-runner-20260823.log`.
