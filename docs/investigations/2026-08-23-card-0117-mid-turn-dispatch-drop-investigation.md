# CARD-0117 investigation — mid-turn pool-delegate dispatch drop

Investigated 2026-08-23 (task `0700e656`), investigation only. No app code was changed. A live
Claude pool delegate (`task-d9151495`) was working in `C:\src\Antiphon` at investigation time, so
no new Codex delegate was launched onto that checkout.

---

## Verdict, in one sentence

The card's working hypothesis (the new task's **boot prompt** was typed into a Codex session that
was still mid-turn, and Codex swallowed the CR) is **refuted for the live miss**. The 9a518def
brief was never typed at all. What reuse actually typed was a `/compact …` user prompt; Codex ran
that as a Code turn; the marker-bearing brief stayed `Pending` with `deliveryAttempts = 0` until
the 10-minute watchdog failed the task on a **stale** `DelegateReportUncorrelated` incident left
by the previous task's compact turn on the same session.

---

## The live miss, re-read from the still-stored records

All of this is still on the live API as of 2026-08-23 (`GET /api/agent-tasks/9a518def`,
`…/393f9803`, `GET /api/sessions/51ee57fc-cea6-4418-8f60-29624720b0fb/transcript` and
`…/messages`). Session 51ee57fc, agent `task-c07fe06d` (`ff8f8b43`), Codex, shared
`C:\src\Antiphon`.

| UTC | What |
|---|---|
| 07:26:37 | Seq 1 `UserPrompt` `[antiphon-task:c07fe06d]` — this pool session's first task. |
| 07:49:28 | Seq 18 `TurnEnd`. |
| 08:06:07 | **393f9803 dispatched**: "Reused warm delegate … unrelated … focused /compact first". |
| 08:06:09 | Seq 19 `UserPrompt` `/compact This session is being handed NEW, unrelated work. Keep only context useful for: Build CARD-0112 S1-S5…` — **no task marker**. |
| 08:06:16–08:11:46 | Seq 20–23 `AssistantText` — Codex treats the compact as the work prompt and starts CARD-0112. |
| 08:11:46 | Seq 24 `TurnEnd`. `OnTurnEndAsync` for 393f9803 sees prompt seq 19 **without** `[antiphon-task:393f9803]` and assistant text → **`DelegateReportUncorrelated` incident, once per session**. Task is not settled. |
| 08:11:47 | Seq 25 `UserPrompt` `[antiphon-task:393f9803]` — the actual brief, flushed `WhenIdle` after that TurnEnd. |
| 08:11:53–08:12:20 | Seq 26–28 narration, seq 29 `TurnEnd` (33 s). |
| 08:14:32 | 393f9803 **Succeeded** on CARD-0046 `FinalMessageMissing` (120 s grace): "617 characters the turn produced BEFORE it". Agent goes Idle (`ReleaseDelegateAsync`). |
| 08:28:27 | **9a518def dispatched** onto the same warm row, again "unrelated … /compact first". |
| 08:29:18 | 9a518def **brief** queued (`SessionQueuedMessages` seq 5). |
| 08:30:03 | Seq 30 `UserPrompt` — 9a518def's `/compact … Continue and finish CARD-0112…`. No marker. |
| 08:30:06 | Seq 31 `AssistantText` "I'll resume from the existing CARD-0112 changes…" |
| 08:30:09 | Seq 32 `UserPrompt` — **the same compact text again**. |
| 08:30:44–08:37:02 | Seq 33–37 `AssistantText` continuing CARD-0112. **No `TurnEnd` after seq 29.** |
| 08:38:32 | 9a518def **Failed** by `FailNeverStartedAsync`, second branch: "Delegate reported but the result could not be attributed". |

Still on the queue today:

```
GET /api/sessions/51ee57fc-…/messages
  body starts [antiphon-task:9a518def]
  status Pending
  createdAt 2026-08-21T08:29:18Z
  deliveryAttempts 0
  parked false
```

That is the dropped "boot prompt". It was never stamped `Sent`. Codex's rollout containing zero
`9a518def` markers is exactly what a never-typed pointer predicts; the spill file
`.antiphon/task-9a518def-brief.md` existing is **not** evidence of typing —
`FitBriefForTyping` writes the spill before `EnqueueAsync`
(`AgentTaskDispatcher.cs:2217-2218`).

The investigator's "old-turn activity continuing" is also real, and it is seq 31–37: Codex doing
CARD-0112 because the compact's **focus argument is the new goal**. It is not leftover tool calls
from 393f9803. 393f9803's last `TurnEnd` is seq 29 at 08:12:20, sixteen minutes before 9a518def
was dispatched.

---

## 1. How a new task is placed on a warm pool delegate (current code)

`DispatchOneAsync` for `WorkspaceMode.Shared` calls `TryReuseWarmAgentAsync` **before** any spawn
(`AgentTaskDispatcher.cs:1244-1263`). On `Reused` it commits, then `DeliverReuseMessagesAsync`.

`TryReuseWarmAgentAsync` (`:1936-2068`):

- **Pinned pool delegate:** `LiveSessionIdOfAsync` must find a Starting/Running session; kind and
  `PoolProjectId` must match; then
  `pinned.Status != AgentStatus.Idle || pinned.PoolIdleSince is null` → `WaitForAgent`
  (`:1980-1981`). No `IsWorkingAsync` call. Occupancy is the **pool handshake**, not the composer.
- **Unpinned Shared:** shops `IsPoolDelegate && Status == Idle && PoolIdleSince != null` plus
  directory / tier / kind / project-scope / reservation (`:1985-2011`). Same: Idle-or-not.
- On take-over: agent → `Running`, `PoolIdleSince = null`, task `Dispatched`, return `Reused`.

`WaitForAgent` is tested as "agent row is Running" (`AgentTaskPoolTests.a_pinned_follow_up_waits_while_its_agent_is_still_working`, `:253-275`). It does **not** cover "agent row Idle, Codex still mid-turn on screen".

Idle is written in one production place for pool delegates: `ReleaseDelegateAsync`
(`AgentTaskReplyService.cs:1042-1053`), on settle (including unreported failure). That is
**task** settlement, which requires a `TurnEnd` that `ExtractMarkedTurnAsync` could judge.

Standing-agent placement (`PlaceOnStandingAgentAsync`, `AgentTaskDispatcher.cs:2122-2134`) is
stricter: another Dispatched/Working **task on this live session** → wait. Pool reuse has no
equivalent session-level occupancy check.

---

## 2. What reuse actually types, and which path types it

`DeliverReuseMessagesAsync` (`:2178-2218`):

1. If the previous task on this session has a different `RootTaskId` (and this is not a Check),
   enqueue
   `"/compact This session is being handed NEW, unrelated work. Keep only context useful for: {first 300 chars of Goal}"`
   (`:2196-2206`). One line. **No `[antiphon-task:]` marker.**
2. `FitBriefForTyping` (spill file + pointer for every non-Claude kind, Codex included —
   `PtyDeliveryCeilings.RequiresJoinSafeDelivery` is `kind != ClaudeCode`).
3. Enqueue the pointer.

Both go through `TryEnqueueReuseAsync` → `SessionMessageQueueService.EnqueueAsync(..., MessageSendMode.WhenIdle, ..., QueuedMessageOrigin.Delegation)` (`:2235-2236`).

That is **not** `RunnerCodexAdapter.SendPromptAsync` / `CodexSubmitConfirmation`.
`CodexSubmitConfirmation` is only called from the two Codex adapters' `SendPromptAsync`
(`RunnerCodexAdapter.cs:80`, `CodexAdapter.cs:98`). CARD-0108's submit-confirm therefore does
**not** cover this path. The queue types with `PtyInputEncoding.NormalizeBody` +
`WrapIfMultiline` + 20 ms + a separate `\r` (`SessionMessageQueueService.DeliverAsync`,
`:1182-1278`), then CARD-0055 `WaitForTranscriptConfirmAsync` (Enter-only re-press, 7 s /
3 attempts / 30 s) if the session is transcript-observable. Codex **is** a verified-delivery
kind (`IsVerifiedDeliverySessionAsync`, `:1525-1541`; `ProviderContractCatalog` Codex
`DeliveryVerification = Supported`).

`EnqueueAsync` delivers immediately when the session is live, accepting input, and
`!IsWorkingAsync` (`:210-224`). `IsWorkingAsync` (`:1909-1986`) is the transcript rule:
activity sequence/timestamp outranking the last `TurnEnd` / interrupt / manual compact /
restart boundary. It does not look at the Codex Working indicator.

---

## 3. Why Codex is blind to "mid-turn" once a TurnEnd exists

`CodexTranscriptNormalizer` maps `task_complete` → `TurnEnd` and **skips**
`CommandExecution` / `FileChange` / `McpToolCall` on purpose (`CodexTranscriptNormalizer.cs:246-249`):

> Nothing downstream needs a mid-turn activity signal from Codex — task_started/task_complete
> bracket the turn explicitly.

So after a `task_complete` is ingested, `IsWorkingAsync` is false until a **new mapped
UserPrompt / AssistantText / Thinking** lands. Continuing tool calls after a premature
`task_complete` would be invisible. That is a real hole, and it is **not** what happened to
9a518def: the previous TurnEnd (seq 29) is 16 minutes old, and the activity after 08:30 is a
**new** UserPrompt (the compact).

CARD-0027/0028 (composer clip / CR-swallow) apply to **typed** bodies. Codex's measured loss
mode is a swallowed Enter, not a clipped body (`CodexComposerCanaryTests` facts B and F: a
same-write trailing CR is a coin flip; a large typed body needs two Enters; a short pointer is
why the inline ceiling is 0). The 9a518def pointer was never typed, so those hazards did not
get a chance on the brief. They may have contributed to the compact taking ~96 s to show up as
seq 30 and appearing twice (seq 30 and 32, 6 s apart ≈ `ReEnterIntervalSeconds = 7`). That
duplicate is **not fully pinned** (see Uncertainties).

---

## 4. Why the watchdog told the "mangled brief" lie

`FailNeverStartedAsync` (`AgentTaskDispatcher.cs:399-498`), 10 minutes
(`DelegationSettings.DeliveryFailTimeoutMinutes`). Two arms:

1. `!TranscriptPromptSpan.HasTurnPromptSinceAsync` → "boot prompt was never delivered", and
   this arm is the one that names queued-message status (`:455-476`).
2. Else if **any** `AgentIncidentKind.DelegateReportUncorrelated` exists **for this session**
   (`:480-493`) → the "Delegate reported but the result could not be attributed … no task
   marker" string the card records.

Arm 2 does **not** require the incident to belong to **this** task. Recording is once per
session (`AgentTaskReplyService.RecordUncorrelatedReportAsync`, `:171-174`).

`TranscriptPromptSpan.IsHousekeepingPrompt` (`:88-92`) treats Claude's `<command-name>` wrappers,
compaction continuation, raw local-command echo (only when a wrapper in the same span named the
command), and task-notification prompts. `TranscriptKinds.IsLocalCommandRecord`
(`SessionRunnerContracts.cs:252-259`) is **only** those wrappers. A Codex rollout UserMessage
that is literally `/compact This session is being handed NEW…` is a normal `UserPrompt`.
`HasTurnPromptSinceAsync` for 9a518def is therefore **true** on seq 30/32.

The uncorrelated incident on 51ee57fc was raised at 08:11:46, for **393f9803's compact turn**
(seq 19 prompt has no marker; seq 20–23 is a report-shaped assistant text). It was still on the
session at 08:38:32. Combined with seq 30/32, arm 2 fires. Arm 1 — which would have said "the
brief is still queued Pending" — is unreachable.

That is also why 393f9803 itself looks "interrupted at ~8 minutes": its first turn was the
compact (08:06:09–08:11:46), the markered brief only ran 33 seconds, and CARD-0046 settled on
the narration.

---

## 5. Can this codebase type a new boot prompt into a Codex session that is still mid-turn?

**Reuse decision: yes, if the agent row is Idle.** Idle is settlement, not composer state. A
follow-up dispatched the moment `ReleaseDelegateAsync` runs will reuse even if Codex is still
drawing, and even if (hypothetical) tools are still running after a `task_complete`. Current
code will then `WhenIdle`-enqueue.

**Actual typing: only if `IsWorkingAsync` is false.** If the previous TurnEnd is ingested and
no later mapped activity exists, the queue types immediately. If a UserPrompt/AssistantText
after that TurnEnd exists, the message stays Pending until the next TurnEnd flush.

For 9a518def that second gate **held**: after seq 30, `IsWorkingAsync` is true, the brief was
never typed. The drop is "queued behind a compact that Codex ran as a work turn, then killed by
a poisoned watchdog", not "typed into a busy composer and CR-swallowed".

The compact **was** typed into an idle composer (seq 29 TurnEnd was 16 minutes old). Codex does
not treat `/compact …` as Claude-style local housekeeping. It records it as a user message and
answers the focus text. Confirmed twice on this one session (seq 19 and seq 30).

---

## 6. CARD-0140 (`9cf7187` and the S1–S4 commits behind it)

Re-read on this worktree at `9cf7187`. CARD-0140 S2/S3 change **cold** launch: pinned standing
agents resolve through `AgentTuiLaunchResolver` / `BuildLaunchSpecAsync`. The comment at
`AgentTaskDispatcher.cs:1267-1273` is explicit that **reuse is outside that pre-flight**
("they launch nothing"). `TryReuseWarmAgentAsync` and `DeliverReuseMessagesAsync` are
untouched. The only reuse-adjacent edit is a comment on `PlaceOnStandingAgentAsync` about what
a **fresh** spawn of a standing agent will launch. **Not this path.**

---

## 7. CARD-0113 is a different defect

CARD-0113 is `RunnerCodexAdapter` swallowing a failed transcript fetch into
`_transcriptBaselineSequence = 0` on turn N+1, so `WaitForTurnCompleteAsync` can certify the
**previous** turn's `TurnEnd` as this turn's. That is the adapter `SendPromptAsync` path. Pool
reuse does not call it. The "second task on a multi-turn Codex session" timing matches the
exposure window, but the 9a518def records are the queue + `/compact` + watchdog, not a
zero-baseline replay. 393f9803's own markered brief (seq 25) did land, one second after seq 24
— not the "instant stale complete" shape.

---

## Uncertainties (not blocking the verdict)

- **Why seq 30 and seq 32 are the same compact ~6 s apart.** Fits a CARD-0055 re-Enter overlapping
  a submit Codex had already recorded; not proven. Does not change "the brief was never typed".
- **51 s from 9a518def `DispatchedAt` (08:28:27) to brief `CreatedAt` (08:29:18).** Fits compact
  `EnqueueAsync` holding the per-session lock through an evidence/confirm window (15 s + 30 s)
  before the brief enqueue runs; not traced in logs for this session.
- **Whether 9a518def's compact turn ever wrote a `task_complete` that was not ingested.** Stored
  transcript ends at seq 37 `AssistantText` 08:37:02; watchdog killed at 08:38:32. The live
  miss's rollout grep was for the task id, not `task_complete`. Irrelevant to the brief never
  being typed.
- **Whether a premature Codex `task_complete` can fire while tools continue.** Normalizer
  comments and the measured 7-call / one-`task_complete` session say no; CARD-0046
  `FinalMessageMissing` on 393f9803 (TurnEnd with no turn-ending AssistantText) shows Codex
  TurnEnds that do not line up with a final message. Not required for this miss.
- **Live reproduction** of "dispatch onto a Codex pool delegate that is genuinely mid-turn" was
  not run: `task-d9151495` was a live Claude pool delegate, `working: true`, in the same
  `C:\src\Antiphon` checkout. The original miss is still fully reconstructable from stored
  transcript + queue, which is stronger than a new synthetic run.

---

## What this is, mechanically

Reuse of an **unrelated** Shared Codex pool delegate always types `/compact {goal}` first, with
no task marker, via the queue's WhenIdle path (not CARD-0108's adapter confirm). Codex records
and answers that as a user turn. That turn:

1. counts as "this task started" for the delivery watchdog (Codex `/compact` is not Claude
   housekeeping),
2. blocks the markered brief behind WhenIdle for as long as it runs,
3. on its own `TurnEnd`, raises `DelegateReportUncorrelated` (prompt has no marker), **once per
   session**, which then makes every later task on that session take `FailNeverStartedAsync`'s
   mangled-brief arm even when the brief is still `Pending` / never typed.

The 2026-08-21 miss is (2) + (3) on a compact turn that had not ended by T+10 minutes. The
boot prompt was not swallowed by a mid-turn composer; it never left the queue.
