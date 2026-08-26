# CARD-0033 — "An agent is asking me something — answer it and move on": designed

**Date:** 2026-08-26
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0033 (`5b70a9f0-fa2c-464e-96af-9b73e8360da1`), story S3 in `docs/product/user-stories.md:80-96`
**Model followed:** `docs/features/010-home-tasks-section/proposal.md` (what exists, verified against
the code, with file paths; then the design; then what it costs its neighbours).
**Sibling:** CARD-0036's plan (`docs/superpowers/plans/2026-08-26-card-0036-away-digest-plan.md`,
commit `29433d3` — **only on `origin/feat/card-task-b25bdf26`, never merged to master**; S1–S3
shipped in `90248a0`/`0abeeed`, S4 unbuilt). §5 below is the contract the two share.

## Verdict, in one screen

**Reuse the drawer. Do not build a modal or a dedicated panel — and stop adding reply boxes.**
There are already **three** places to answer a blocked delegate, all posting the same
`POST /api/agent-tasks/{id}/reply`: the drawer (`TaskDrawer.tsx:251-287`), the desktop attention
panel (`AttentionPanel.tsx:437` → `BlockedReplyRow`), and the phone home's band 1
(`MobileHomePage.tsx:268` → the same `BlockedReplyRow`). A fourth surface would add a fourth thing
to keep consistent and would not shorten the path to an answer by a single click.

What is actually missing is not a box. Verified against the code, the story's four "needs" score:

| S3 needs on screen | Drawer | Attention row (desktop) | Phone band 1 | Telegram ping (0036 S3) |
|---|---|---|---|---|
| the question | buried: it is the **tail** of `Result`, rendered under "The delegate asked" as the whole report | **head** 400 chars of `Result` — the question is cut off on any long report | same head-400 evidence, `lineClamp={2}` | `Clean(Evidence, SentenceChars)` of that same head — the ping may not contain the question at all |
| the task goal | yes (`:211-215`), above the question | no | no | no |
| what it has done so far | no — only the timeline, and the `Replied` event carries no answer text | no | no | no |
| a reply box | yes | yes | yes | reply-to-ping (S4, unbuilt) |

So the card's work is in three places, none of them a new surface:

1. **One projection that isolates the question from the report** and carries the context
   ("so far": the delegate's own words before the question, prior Q&A rounds, commits and changed
   files on its branch, the last check-in digest). Served on the existing task detail and reused by
   the attention projection, so every surface — the drawer, both attention rows, and the Telegram
   ping — shows *the question*, not the first 400 characters of whatever preceded it.
2. **One component** (`BlockedQuestionCard`) rendered by the drawer at the **top** when a task is
   Blocked, and in a compact form by the two attention rows in place of the bare `BlockedReplyRow`.
   Reading order: question → reply box → goal → so far. Everything else in the drawer drops below.
3. **The answer becomes a record.** `AnswerAsync` gains an origin and a round: the `Replied` event
   stores the answer text (today it stores the constant string "Caller answered the delegate's
   question." — `AgentTaskReplyService.cs:245`), and an optional round number refuses an answer to a
   question the delegate has since replaced. This is the exact call 0036-S4 will make from a Telegram
   reply, so it is specified once here (§5) and S4 inherits it.

Out of scope, deliberately: any new notification (that is 0036 S3, shipped), the Telegram inbound
handler itself (0036 S4), and any change to *how* an answer is delivered to the session (the
WhenIdle queue and CARD-0055 confirmation are untouched).

---

## 1. What exists today (verified against the code, 2026-08-26)

### 1.1 Three writers put a task into Blocked, and they do not mean the same thing

| Writer | Where | What it stores | Can it be *answered*? |
|---|---|---|---|
| question detector at settlement | `AgentTaskReplyService.SettleAsync` `:391` (`LooksLikeAQuestion` `:1558-1569`: one of the last two non-empty lines ends in `?`) | `Result` = the delegate's **whole** final message; `Blocked` event "Delegate asked a question." (`:431-432`); `FailureReason` null | **Yes** — the session is kept alive precisely for this (`:461-476`) |
| merge-back conflict | `MergeBackAsync` `:977-991` | `Status = Blocked`, `FailureReason` = "Rebase onto … conflicted in N file(s)", **`Conflicted`** event (not `Blocked`), a Merge child task spawned (`:987`) | Technically (`AnswerAsync` only checks status + session): the delegate is idle after its report and would receive the text as an instruction. Usually the wrong verb — the Merge task resolves it and `ResolveConflictedParentAsync` `:1006-1027` flips the parent to Succeeded |
| dispatcher cost ceiling | `AgentTaskDispatcher.cs:266-268` → `BlockAsync` `:2156-2171` | `FailureReason` = "Run cost ceiling reached ($N)", `Blocked` event, task was **still Queued** | **No** — `AgentSessionId` is null, so `AnswerAsync` throws "The delegate's session is no longer available." (`:239-240`) |

`AttentionService.BuildBlockedAsync` (`AttentionService.cs:175-228`) already knows the first two
disagree on event type and dates a block from the latest `Blocked` **or** `Conflicted` event
(`:185-198`); `BlockedTaskNotifier.SweepAsync` (`BlockedTaskNotifier.cs:38`) does the same. Neither
distinguishes the three kinds on the row: all three are `AttentionKind.BlockedQuestion`, all three
carry `[Reply, Cancel, Escalate]` (`:224`).

### 1.2 The answer path: complete, thin, and untested at the service

- `AgentTaskReplyService.AnswerAsync(Guid taskId, string message, CancellationToken)`
  (`:225-255`): validates non-empty, loads the task, refuses unless `Status == Blocked` (`:237`) and
  a session exists (`:239`), sets `Working`, writes `Replied` with the constant detail (`:245`),
  enqueues `TaskMarker + "\n\n" + message` **WhenIdle** with `QueuedMessageOrigin.Delegation`
  (`:249-250`), publishes `AgentTaskChanged` (`:252`), returns the summary.
- Endpoint `POST /api/agent-tasks/{id}/reply` (`AgentTaskEndpoints.cs:69-78`) — `{id}` is a guid or
  the 8-hex short id via `AgentTaskService.ResolveTaskIdAsync` (`AgentTaskService.cs:722`). Body
  `ReplyToAgentTaskRequest(string Message)` (`AgentTaskDtos.cs:166`).
- Client hook `useReplyToAgentTask` (`client/src/api/agentTasks.ts:278-281`) invalidates
  `['agentTasks','list']` and `['agentTasks','detail']` (`:241-248`); `BlockedReplyRow` additionally
  invalidates `attentionKeys.all` (`BlockedReplyRow.tsx:43`). SignalR `AgentTaskChanged` invalidates
  list, detail, attention and thread keys (`useSignalRInvalidation.ts:128-140`).
- Shell: `scripts/delegate.ps1 -Reply <taskId> "answer"` (`:91-101`, `:151-157`).
- **No test exercises `AnswerAsync` itself.** `grep -rn AnswerAsync tests/` finds only
  `BlockedTaskNotifierTests`' harness helper (a hand-written status flip, `:248`) and the pty
  trust-prompt detector. `AgentTaskReplyIntegrationTests` pins the *block* side
  (`a_delegate_that_asks_a_question_comes_back_blocked_not_finished` `:770`,
  `a_blocked_delegate_keeps_its_session_and_agent` `:1682`) and `AttentionServiceTests` the row
  (`a_blocked_task_is_listed_as_needing_an_answer_and_offers_reply` `:47`); the drawer's
  `answers a blocked delegate instead of taking the work back` (`TaskDrawer.test.tsx:149`) mocks the
  POST. The one call this whole story leans on has no behavioural pin.

### 1.3 The drawer (`client/src/features/delegations/TaskDrawer.tsx`, 395 lines)

Reading order today, top to bottom: tier/role/workspace badges (`:127-148`) → metrics paper
(agent, elapsed, cost, subtree, tokens, directory, merge target; `:150-192`) → Transcript/Files
links (`:194-209`) → **Goal** (`:211-215`) → `failureReason` in an `Alert` titled **"Failed"**
(`:217-221`) → `result` as "The delegate asked" when Blocked, else "Report" (`:223-249`, markdown in
a 320px scroll area with the selection-delegate composer) → **"Answer it"** textarea + Send
(`:251-287`) → Timeline (`:289-302`) → Retry / Escalate / Cancel (`:304-366`).

Three concrete defects for a Blocked task:

- The question is the *last* line(s) of a `Result` that can be a full report; it renders inside a
  scroll area whose scroll position starts at the **top**. The operator scrolls the drawer to find the
  section, then scrolls the section to find the question.
- A conflict- or cost-blocked task shows its reason under a red alert titled **"Failed"** — it has
  not failed — and is offered the "Answer it" box, which for a cost block will 409.
- The Timeline shows `Replied — Caller answered the delegate's question.` with no text, so a second
  round of questions has no visible first round: the operator cannot see what they already said.

Opened only from `DelegationsBoard.tsx:205` (`?task=<guid>` read at `:38-40`); every other surface
deep-links there — `ProjectTasksPanel.tsx:52,59` and `attentionVisuals.targetOf`
(`attentionVisuals.ts:183-184`) both navigate to `/orchestrator?tab=delegations&task=<id>`.

### 1.4 The two attention rows

- Desktop: `AttentionPanel` on the Orchestrator page's "Needs attention" tab
  (`OrchestratorPage.tsx:47-68`), reached from the home rail's `NeedsAttentionBadge`
  (`HomePage.tsx:344-361`, renders nothing at zero). Row = badge, headline, `evidence` (head 400
  chars, `AttentionService.cs:951-955`), verbs; "Answer it" toggles `BlockedReplyRow` under the row
  (`AttentionPanel.tsx:372-373`, `:437`). Body click → the drawer.
- Phone: `MobileHomePage.NeedsYouRow` (`:205-300`) — evidence `lineClamp={2}`, the reply box is
  **open by default** for a Reply-capable row (`:210-211`, "on a phone the tap that would merely
  reveal the box is a tap the answer loses").

Both render the server's `Evidence`, so both inherit the head-truncation: for a question-blocked
task `primary` is `task.Result` (`AttentionService.cs:203-206`) and `Excerpt` keeps the first 400
characters (`:955`). A delegate that wrote three paragraphs of findings and then asked its question
puts *findings* on the row and the question past the ellipsis.

### 1.5 The Telegram ping (0036 S3, shipped) inherits the same truncation

`AwayDigestFormatter.FormatPing` (`AwayDigestFormatter.cs:40-44`) renders
`❓ task <8hex> needs an answer — <title>` then `Clean(blocked.Evidence, SentenceChars)` — a
sentence-length cut of the already head-cut evidence. The ping reliably carries the *short id* (what
S4 needs to route the reply) and unreliably carries the *question* (what the human needs to write
it). Fixing the projection (§3.1) fixes the ping without touching the formatter.

### 1.6 "What the delegate has done so far" — all the facts exist, none are on the task surface

- `DelegateCheckProbe.GatherAsync(task)` (`DelegateCheckProbe.cs:179`) already assembles, in
  process and deterministically, exactly this: `CheckGitFacts` (commits on the range, changed and
  untracked file counts, `:144-150`), the transcript tail (`:126`), pending queued messages,
  incidents (`:167-175`). It is invoked only by the scheduled check-in (`AgentTaskCheckService`),
  whose `Check` event stores the digest head (`AgentTaskEnums.cs:108-113`).
- Prior rounds: every `Blocked`/`Conflicted` event marks a question; every `Replied` event marks an
  answer — but the answer text is not stored (§1.2), and the question text is only ever the
  *current* `Result` (overwritten at the next settlement, `SettleAsync` `:388`).
- `Refined` events do carry their text (`AgentTaskReplyService.cs:294-296`, `:307-309`) — the
  precedent this design follows for `Replied`.

### 1.7 What does not exist (the build list, before design)

- A server-side notion of *the question* as distinct from *the report* (§3.1).
- A record of what was answered, by whom/where, and to which round (§3.2).
- Any surface showing goal + question + so-far together (§4).
- A stale-answer guard: a ping sent at 14:02 answered at 16:40, after the delegate was answered
  by someone else at 15:00 and has since asked a *different* question, is delivered to the new
  question (`AnswerAsync` checks status only). Latent today; real the day S4 ships, because Telegram
  replies are the slow path.

---

## 2. The surface decision, argued

**Question:** extend the drawer, or a dedicated surface (modal / panel)?

**Answer: the drawer, made question-first when Blocked — plus the same component in compact form
inside the two attention rows that already exist.** Reasons:

1. **Latency-to-answer is already one click from every noticing surface.** Home badge → attention
   tab → "Answer it" inline; phone band 1 → box already open; board chip → drawer. A modal would
   sit *on top of* one of those and add a dismissal. The card's own framing ("mostly about surfacing
   and latency-to-answer") argues against new chrome.
2. **The drawer is the only place with room for "so far".** Attention rows are a list; the phone is a
   phone. The full context — goal, the delegate's findings, commits, prior rounds — needs the
   drawer's width, and the drawer already has the Goal, the Result and the Timeline. The work is
   reordering and isolating, not relocating.
3. **One component, three hosts, one projection** keeps the three boxes from drifting (they have
   already drifted: the drawer's `Section` wrapper vs `BlockedReplyRow`'s bare stack, different
   invalidation sets). A dedicated surface would be a fourth host.
4. **The answer must be reachable from the phone and from Telegram (S4) with the same semantics.**
   That is a data-model property (§5), not a surface property; no surface choice buys it.

**What a modal *would* buy and why it is not enough:** focus (nothing else on screen) and a
keyboard-first flow. Both are had cheaper by auto-focusing the reply box when the drawer opens on a
Blocked task and by putting the question above the fold. If lived-with use shows people answering
from the board chip rather than from attention rows, a `?task=&answer=1` deep link that opens the
drawer scrolled to the card is a one-line addition; a modal is not.

**Answering from outside the web app** (the card's last question): yes — 0036 S4 (reply to the
ping in Telegram) and `delegate.ps1 -Reply`. Both go through the same `AnswerAsync`; §5 makes sure
the record they leave is identical.

---

## 3. Server design

### 3.1 `BlockedContextDto` on the task detail — the question, isolated

No new endpoint. `AgentTaskDetailDto` (`AgentTaskDtos.cs:128-138`) gains one nullable member,
non-null iff `Status == Blocked`:

```csharp
public sealed record BlockedContextDto(
    BlockedKind Kind,                       // Question | MergeConflict | CostCeiling
    int Round,                              // 1-based count of Blocked+Conflicted events so far
    DateTime BlockedAt,                     // latest Blocked/Conflicted event's At
    string Question,                        // Question: trailing question paragraph. Conflict/Cost: FailureReason
    string? Context,                        // Question: Result minus Question (delegate's own "so far"); else null
    IReadOnlyList<BlockedRoundDto> PriorRounds,   // earlier (question, answer) pairs, oldest first
    BlockedProgressDto? Progress,           // git + last check digest; null when unavailable
    bool CanAnswer,                         // Kind == Question && AgentSessionId != null
    string? CannotAnswerReason,             // "Run cost ceiling reached…" / "session no longer available"
    Guid? MergeTaskId);                     // MergeConflict only: the child resolving it

public sealed record BlockedRoundDto(int Round, string Question, DateTime AskedAt,
    string? Answer, DateTime? AnsweredAt, AnswerOrigin? AnsweredVia);

public sealed record BlockedProgressDto(
    string? Branch, IReadOnlyList<string> Commits, int ChangedFiles, int UntrackedFiles,
    string? LastCheckDigest, DateTime? LastCheckAt, string? Unavailable);

public enum BlockedKind { Question = 0, MergeConflict = 1, CostCeiling = 2 }
```

**Question extraction** — new `internal static class BlockedQuestion` next to
`LooksLikeAQuestion` (`AgentTaskReplyService.cs:1558`), sharing its rule so the two cannot
disagree: split on blank lines; the question is the **last paragraph** if any of its final two
lines ends in `?`, else the last two lines (the detector's window). `Context` is everything before
it, trimmed; null when empty. `LooksLikeAQuestion` is rewritten as `BlockedQuestion.TryExtract(report,
out question, out context)` and keeps its single existing test surface.

**Kind** derives from the latest block event: `Conflicted` → `MergeConflict`; `Blocked` with
`Result` non-empty and `FailureReason` null → `Question`; `Blocked` with `FailureReason` starting
"Run cost ceiling" → `CostCeiling` (the dispatcher's own string, `AgentTaskDispatcher.cs:267`;
match on the event detail, not on prose elsewhere).

**Rounds** are rebuilt from events in `GetAsync` (`AgentTaskService.cs:462-490`, which already loads
the ordered events at `:484-488`): walk `Blocked`/`Conflicted` as question markers and `Replied` as
answer markers. Because today's `Replied` detail is a constant, historical rounds render as
"(answered — text not recorded before CARD-0033)"; from §3.2 on, the text is there. The *question*
text of a prior round is unrecoverable (Result was overwritten) unless §3.2's `Blocked` event detail
starts carrying it — it does, see below.

**Progress** reuses `DelegateCheckProbe.GatherAsync(task, ct)` (`DelegateCheckProbe.cs:179`) and
maps `CheckGitFacts` (`:144-150`) plus the latest `Check` event's detail head. The probe is scoped
and cheap (one `git log`, one status); guard it behind a 2 s `CancellationTokenSource` and degrade
to `Unavailable = "<reason>"` — the drawer must never fail to open because git was slow.

**Attention reuse.** `AttentionService.BuildBlockedAsync` (`:202-206`) replaces
`task.Result` with `BlockedQuestion.TryExtract(...)`'s question for the Question kind, so the row's
`Evidence` and — through `FormatPing` — the Telegram ping both start with the question. The
`Kind`-specific verbs follow: `CostCeiling` rows drop `Reply` from `Actions` (the client already
hides verbs the row cannot use, `AttentionPanel.tsx:405-413`); `MergeConflict` keeps `Reply` but
the headline reads "Blocked — merge conflict; task <8hex> is resolving it" so a human does not answer
a question nobody asked. No enum member is added to `AttentionKind` (the CARD-0035 non-widening rule,
`AttentionService.cs:17-23`); the kind lives on the task detail, and the row only needs the verbs.

### 3.2 `AnswerAsync` — origin, round, and a real record

```csharp
public Task<AgentTaskSummaryDto> AnswerAsync(
    Guid taskId, string message, AnswerOrigin origin, int? round, CancellationToken ct)

public enum AnswerOrigin { Web = 0, Cli = 1, Channel = 2 }
```

- **Origin** is a fact about provenance, recorded on the `Replied` event detail as
  `Answered via {origin} (round {n}): {message}` — capped by `NewEvent`'s 4 000 (`:1575-1583`), so
  a long answer is truncated on the timeline and whole in the queue. The endpoint passes `Web`
  unless the request carries `Origin` (the delegate script sends `Cli`; S4 sends `Channel`).
  `QueuedMessageOrigin.Delegation` on the queued message is unchanged — that enum governs batching
  and ceilings, not provenance.
- **Round** is an optional compare-and-swap. When non-null and ≠ the task's current round
  (count of `Blocked`+`Conflicted` events), refuse with
  `ConflictException("Task <8hex> has moved on: it asked a new question (round N) since the one you are answering (round M).")`
  — surfaced as 409 with the existing problem-details shape. Null skips the check (today's
  behaviour, kept for `delegate.ps1`). The drawer and both attention rows always send the round they
  rendered.
- **The `Blocked` event detail carries the question** (today: the constant "Delegate asked a
  question.", `:432`). New: `Delegate asked: {question}` (4 000 cap). This is what makes prior
  rounds reconstructible from events alone and what lets `PriorRounds` show a question the current
  `Result` no longer holds. `Conflicted` and cost `Blocked` details already carry their reason.
- Request: `ReplyToAgentTaskRequest(string Message, int? Round = null, AnswerOrigin? Origin = null)`
  — additive, so every existing caller (`delegate.ps1:156`, `useReplyToAgentTask`) keeps working
  unchanged.
- Response: unchanged (`AgentTaskSummaryDto`). The client re-fetches detail via the existing
  invalidation.

Everything below the enqueue is untouched: WhenIdle, the marker, transcript-confirmed delivery
(CARD-0055), parking, and the `NoTranscriptRecord` handling all stay as they are.

### 3.3 Migration

None. Both records are event `Detail` text; rounds are derived. Pre-existing rows read as
"text not recorded" for prior rounds and render the current `Result` normally.

---

## 4. Client design

### 4.1 `BlockedQuestionCard` — one component, three hosts

New file `client/src/features/delegations/BlockedQuestionCard.tsx`. Props:
`{ detail: AgentTaskDetailDto; variant: 'full' | 'compact'; autoFocus?: boolean; onAnswered?: () => void }`.
It owns the form (`BlockedReplyRow`'s textarea, Send, Close and its invalidation set move here;
`BlockedReplyRow.tsx` becomes a thin wrapper that fetches `useAgentTask(taskId)` and renders the
compact card, so `AttentionPanel.tsx:437` and `MobileHomePage.tsx:268` need no edit beyond the
import). The `compact` variant needs the detail fetch the rows do not have today — one
`GET /api/agent-tasks/{id}` per *opened* reply row, not per row rendered.

**Reading order, `full` (the drawer), and why:**

1. **The question** — `blocked.question`, verbatim, `Text size="md"` in a bordered paper with a
   `Blocked · round N · since 14:02 · $1.37 so far` caption. First because it is the thing the
   human is here to read; the delegate wrote it as a question to a human, so it needs no framing.
2. **The reply box** — `Textarea autosize minRows={3}`, **autofocused** when the drawer opened
   onto a Blocked task, `Ctrl+Enter` sends. Send is disabled until non-empty; the button label is
   "Send answer" (`Question`) or "Tell the delegate" (`MergeConflict` — it is an instruction, not an
   answer). For `CostCeiling` the box is replaced by the reason and the setting that governs it
   (`Delegation:MaxCostUsdPerRoot`), with Cancel and Escalate as the offered verbs.
3. **Goal** — `detail.goal`, `lineClamp={4}` with "show all"; the operator usually remembers the
   goal and needs it only to disambiguate.
4. **So far** — three stacked, each omitted when empty, never an empty heading:
   - the delegate's own words before the question (`blocked.context`, rendered markdown,
     `ScrollArea.Autosize mah={240}`), labelled "Before it asked";
   - `blocked.progress`: branch, `N commits` with the one-line subjects, `M files changed`, and the
     last check digest with its time, labelled "On disk";
   - `blocked.priorRounds`: `Q (14:02) → A via web (14:10)` pairs, labelled "Earlier rounds".
5. Links — Transcript, Files (existing anchors, `TaskDrawer.tsx:194-209`).

**`compact` (attention rows, phone):** question (`lineClamp={4}`, tap to expand), the box, and a
single collapsed "So far" disclosure that opens 4 above. No goal (the row's title is the goal's
summary, and the phone's budget is the reason the row exists).

### 4.2 The drawer, reordered when Blocked

`TaskDetail` (`TaskDrawer.tsx:93-369`) gets one branch: when `detail.blocked` is non-null, render
`<BlockedQuestionCard variant="full" autoFocus />` **immediately after the badge row** (`:127-148`)
and **before** the metrics paper. Then:

- remove the "The delegate asked" title variant (`:224`) — `result` renders as "Report" only when
  not Blocked; the Blocked case is the card's `context`;
- remove the "Answer it" section (`:251-287`) — the card owns the form;
- the `failureReason` alert (`:217-221`) renders only when `summary.status === 'Failed'`; a
  Blocked reason is the card's question line;
- the Timeline's `Replied` and `Blocked` rows now show text (server change) — no client change,
  but the `Replied` detail can be 4 000 chars, so the Timeline item gets `lineClamp={3}` with
  expand.

Not Blocked: nothing moves. The drawer for Working/Succeeded/Failed tasks is byte-for-byte the
current one.

### 4.3 Deep link and focus

`DelegationsBoard.tsx:38-40` already opens the drawer from `?task=`. Add `&answer=1` handling:
when present, `BlockedQuestionCard` autofocuses (it does anyway on Blocked) and the drawer scrolls
to top (it opens there). This is the URL the Telegram ping's optional footer (`Digest:PublicBaseUrl`,
`DigestSettings.cs:15`, currently unset) should carry for the desktop case; on a phone the mobile
home band 1 is the right landing and needs no query.

### 4.4 API types

`client/src/api/agentTasks.ts`: add `BlockedContextDto`, `BlockedRoundDto`, `BlockedProgressDto`,
`BlockedKind`, `AnswerOrigin`; `AgentTaskDetailDto` gains `blocked: BlockedContextDto | null`
(`:131-142`); `useReplyToAgentTask` body becomes `{ message, round?, origin? }` (`:278-281`) with
`origin: 'Web'` set in the hook, not by callers.

---

## 5. The answering contract shared with 0036 S4

0036's S4 (`DigestReplyHandler`, its plan §3.5) parses a short id from the ping's first line or a
typed `<8hex>:` prefix, resolves it with `ResolveTaskIdAsync`, and calls `AnswerAsync`. So that S4
and this card cannot diverge, the contract is:

| Need | Web (this card) | Telegram (0036 S4) | `delegate.ps1` |
|---|---|---|---|
| task identity | guid in `?task=` | 8-hex short id from the ping / prefix, `ResolveTaskIdAsync` | either |
| the answer text | textarea | `message.Text` (or group 3 of the typed shape) | argument |
| origin | `Web` | `Channel` | `Cli` |
| round (stale guard) | `blocked.round` from detail — always sent | parsed from the ping when present; null otherwise | null |
| refusal surface | 409 → red toast, detail re-fetched, new question shown, **draft kept** | 409 message echoed as a quoted reply (S4's existing plan) | non-zero exit with the message |
| record left | `Replied — Answered via Web (round N): …` | `… via Channel …` | `… via Cli …` |

Two things S4 must adopt from here, both small:

1. **Include the round in the ping** when it is > 1: `❓ task 1a2b3c4d needs an answer (round 2) — title`.
   `FormatPing` (`AwayDigestFormatter.cs:40-44`) takes an `AttentionItemDto`, which has no round;
   either add `Round` to `AttentionItemDto` (an additive record field — allowed, it is not an enum
   member) or have `BlockedTaskNotifier` (`:46`) look it up from the events it already loaded
   (`:33`). The latter keeps the attention DTO unchanged; recommended. S4's excerpt regex becomes
   `task ([0-9a-f]{8})(?: needs an answer \(round (\d+)\))?`.
2. **Call the four-argument `AnswerAsync`** with `AnswerOrigin.Channel` — nothing else changes in
   S4's plan.

And one thing this card guarantees S4: the ping's second line is the **question**, because the
attention evidence now starts with it (§3.1). Without that, S4 would ship a reply-to-ping flow whose
ping does not reliably contain what is being asked.

---

## 6. What it deliberately does NOT show

- **The transcript.** The story's explicit anti-goal. The Transcript link stays; nothing inlines it.
- **Cost breakdown, tokens, elapsed** above the fold on a Blocked task — they move below the card.
  The card's caption carries one cost figure (`subtreeCostUsd`) and one time (since), which is what
  answering needs (is this worth continuing?).
- **The full timeline** in the card — only the Q→A rounds. The Timeline section stays below.
- **Retry / Escalate** in the card for the Question kind — the card's whole argument is "answer,
  don't take the work back" (`BlockedReplyRow.tsx:13-18`); those verbs stay in the drawer footer.
- **A second notification.** Noticing is S1/S6; the badge, band 1 and the ping exist.

---

## 7. What it costs the surfaces it shares screen with

- **The drawer for a Blocked task** pushes metrics, links, Report and Timeline below a card that is
  ~40 % of a laptop viewport. Accepted: on a Blocked task the answer is the only action that matters,
  and the metrics are one scroll away. Non-Blocked tasks lose nothing.
- **The desktop attention panel** rows grow when "Answer it" is open: the question paragraph and a
  collapsed disclosure are added to the existing textarea. Closed rows are unchanged in height. The
  detail fetch is per opened row.
- **The phone band 1** row grows by up to four lines of question (today: two lines of evidence,
  which — after §3.1 — *is* the question, so in practice the row grows by at most two lines) and a
  "So far" disclosure line. Band 2/3 move down by that much only while a block is open; band 1 is
  empty on a calm day and this changes nothing then.
- **The Telegram ping** gains ` (round N)` in its first line only when N > 1.
- **Nothing is taken from the board chips, the home tasks section or the thread panel.**

---

## 8. Failure and empty states

| State | Behaviour |
|---|---|
| answered elsewhere while the box is open (Telegram, another tab, CLI) | `AgentTaskChanged` invalidates detail; the card unmounts (task is Working) and a dimmed line "Answered via Channel at 15:00 — the delegate is working" replaces it; the draft is dropped (nothing to answer) |
| delegate re-blocked with a new question while typing | detail refresh changes `round`; the card re-renders the new question, **keeps the draft**, and shows "The question changed since you started — check it still fits" |
| 409 stale round on send | same as above, driven by the refusal instead of the push |
| 409 "not waiting for an answer" (raced with an answer) | toast, detail refetch, card unmounts |
| `CostCeiling` | no box; reason, the setting name, Cancel and Escalate; attention row has no Reply verb |
| `MergeConflict` | question line = the conflict reason with a link to the Merge child (`mergeTaskId`); button reads "Tell the delegate"; Cancel offered |
| `CanAnswer` false for any other reason (session row gone) | reason line "The delegate's session is no longer available" (the server's own text), Retry offered |
| `Result` present but no extractable question (cannot happen for `Question` kind — the detector's rule is the extractor's rule — but the DTO tolerates it) | `question` = last two lines, `context` = the rest |
| `progress` unavailable (not a repo, git slow, worktree gone) | "On disk" block omitted; `unavailable` shown as a dimmed reason only in `full` |
| no prior rounds | "Earlier rounds" omitted |
| historical rounds (pre-change events) | `Q: (not recorded before CARD-0033)` / `A: (not recorded …)` with times — never a blank |
| delivery fails after Send (session gone, parked) | the answer is Sent=false in the queue; the existing queue incidents and attention `ParkedMessage` rows handle it — the card shows "Queued for the delegate's next idle moment" on success, never "delivered" |
| detail fetch fails in a `compact` row | the row falls back to today's bare `BlockedReplyRow` form (question from `evidence`) so an answer is never blocked by the context fetch |

---

## 9. Slices, tiers, tests

| Slice | Content | Tests | Tier |
|---|---|---|---|
| **S1** server: question extraction + `BlockedContextDto` + attention/ping evidence starts with the question + `Blocked` event carries the question | `BlockedQuestion.TryExtract` (`AgentTaskReplyService.cs:1558` rewrite), `AgentTaskService.GetAsync` (`:462-490`), `AttentionService.BuildBlockedAsync` (`:202-206`), `DelegateCheckProbe` reuse, `AgentTaskDtos.cs` | new `BlockedQuestionTests` (paragraph/two-line/no-context/CRLF); `AgentTaskDetailBlockedContextTests` (three kinds, rounds from events, progress degraded); `AttentionServiceTests` addition `a_long_report_that_ends_in_a_question_puts_the_question_on_the_row`; `AwayDigestFormatterTests` addition `the_ping_carries_the_question_not_the_report_head` | Grok (Codex is out of credits, 2026-08-26) |
| **S2** server: `AnswerAsync(taskId, message, origin, round)`, `Replied` detail with text, `ReplyToAgentTaskRequest` additive fields, endpoint + `delegate.ps1 -Reply` passes `origin=Cli` | `AgentTaskReplyService.cs:225-255`, `AgentTaskEndpoints.cs:69-78`, `AgentTaskDtos.cs:166`, `scripts/delegate.ps1:151-157` | **new `AgentTaskAnswerTests`** — the first direct pins on `AnswerAsync`: answers a Blocked task and enqueues marker+text WhenIdle/Delegation; refuses non-Blocked; refuses no-session; **refuses a stale round**; null round skips the guard; `Replied` detail carries origin, round and text; `Blocked` detail carries the question | Grok |
| **S3** client: `BlockedQuestionCard`, drawer reorder, `BlockedReplyRow` wrapper, API types, `&answer=1` | §4 | `BlockedQuestionCard.test.tsx` (reading order by DOM order; kinds; draft kept on round change; Ctrl+Enter); `TaskDrawer.test.tsx` updates (`:149` now asserts the card is first and the "Failed" alert is absent on Blocked); `BlockedReplyRow.test.tsx` fallback-on-fetch-failure; `AttentionPanel.test.tsx` / `MobileHomePage` unchanged assertions still pass | Grok |
| **S4** (0036's) — not this card | ping round suffix, `AnswerOrigin.Channel` | as 0036 §6 | — |

Order S1 → S2 → S3; S1 and S2 are independent of each other (S2 only needs S1's `BlockedQuestion`
for the event detail — take that static class first). Each slice is mergeable alone: S1 alone fixes
the ping and the rows; S2 alone makes the timeline honest; S3 is the visible change.

Estimate: S1 ~2 h, S2 ~1.5 h, S3 ~3 h of delegate time; verification floor ~1 h each (the
`Antiphon.Tests.Application` chunk plus `scripts/test-client.ps1`).

---

## 10. Decisions that are the operator's, not mine

1. **Round as the stale guard, versus none.** Argued for (§1.7, §3.2); cost is one optional int on
   the wire and one comparison. Saying no removes the S4 ping suffix too.
2. **Should a `MergeConflict` block offer the reply box at all?** Designed yes with a different label
   ("Tell the delegate"), because a delegate told "resolve it yourself, the Merge task is cancelled"
   is a legitimate move and the session is alive. Removing it is deleting one branch.
3. **`DelegateCheckProbe` reuse for progress** runs a `git log`/`git status` inside a detail GET.
   Bounded to 2 s and degrading; the alternative is a separate `GET …/progress` the card fetches
   lazily. Designed inline because the drawer is already a single fetch and "so far" should not
   arrive a beat after the question.
4. **Where this doc lives.** The card asks for `docs/features/`; the brief asked for
   `docs/superpowers/plans/`. This is the latter, mirroring 0036; a rename to
   `docs/features/013-answer-blocked-delegate/proposal.md` is a `git mv`. Note that 0036's plan
   itself is still only on `origin/feat/card-task-b25bdf26` (`29433d3`) and should land on master.
