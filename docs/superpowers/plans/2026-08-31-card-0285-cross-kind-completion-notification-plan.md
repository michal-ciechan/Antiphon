# CARD-0285 — Cross-kind delayed task-completion notification: is CARD-0264's fix Claude-only?

**Date:** 2026-08-31 (Plan/Debug pass — diagnosis and design only; nothing built)
**Status:** The three named instances are fully explained. CARD-0264's idle-false-working bug is not the cause of any of them, and is not evidenced as still open after its deploy. A different, kind-agnostic gap remains: WhenIdle notes to a busy caller, plus poll-shrink.

**Sources:** `GET /api/agent-tasks/{id}`, `delegate.ps1 -Status`, Postgres (`AgentTasks` / `AgentTaskEvents` / `SessionQueuedMessages` / `TranscriptEntries` / `AgentSessions`), `server/logs/antiphon-20260831.log` (timestamps `+01:00`), `git show 0398a1ee`, and the three normalizers plus `SessionMessageQueueService.IsWorkingBatchAsync`.

All times below are UTC unless marked BST (`+01:00`).

## Verdict up front

1. **The card's framing mixes caller kind with delegate kind.** Every named note was delivered into the same Claude orchestrator session `cefed08a-fd4a-42a0-8c76-0fbf82cf6b20`. `IsWorkingAsync` inspects **that** transcript, not the delegate's. Grok vs Codex vs fable on the *worker* cannot produce a kind-specific delay on this path.

2. **Card correction:** task `4b3b71b5` is **Codex** (`gpt-5.6-sol`, `AgentKind=2`), not fable. `0504ae08` is Claude/fable. `331c0505` is Codex terra. There is **no Grok example** in the three. Grok *can* be a caller (151 sessions, 216 Delegation notes historically) but none of tonight's named stalls were on a Grok caller.

3. **None of the three is a live CARD-0264 regression.** `0504ae08` settled 9 minutes *before* commit `0398a1ee` and 25 minutes before that binary could have been serving. `4b3b71b5` and `331c0505` settled ~9 hours after the first post-fix AppHost start — and in both windows the caller was mid-turn doing real work. WhenIdle waited; a `-Status` poll then shrunk the note (CARD-0132). The full report never became a live turn **because the caller already read it**, not because the session was falsely working while idle.

4. **`IsWorkingAsync`'s exists-rule is kind-agnostic** over normalized `TurnEnd` / activity rows. The equal-timestamp trigger that defeated the *old* override is Claude-only (measured: 1,325/1,567 Claude `AssistantText`→`TurnEnd` pairs share a timestamp; **0/117 Codex, 0/126 Grok** since 2026-08-20). Grok and Codex already emit `TurnEnd` with a *later* timestamp than the preceding `AssistantText`, so the CARD-0264 equal-ts tie cannot fire on those callers.

5. **What remains is not a transcript-shape bug.** Completion notes are `MessageSendMode.WhenIdle` into the parent (`AgentTaskReplyService.DeliverToParentAsync`). A busy orchestrator — 8 minutes merging CARD-0263, 12 minutes merging CARD-0254, or 7 hours blocked on `AskUserQuestion` — will not see the note until that turn ends. If it polls in the meantime, CARD-0132 replaces the body with `Report withheld`. Silence is therefore **not** evidence the child is still running. The task row (`Status` / `CompletedAt`) already is.

Do not change `IsWorkingAsync` per kind. Do not reopen CARD-0264 on this evidence. The follow-up that is actually load-bearing for humans is already filed: **CARD-0267** (attention row for an aging undelivered caller note). The follow-up for the orchestrator itself is a contract change: treat the task row as the finish signal; treat the `[task done]` turn as a delayed, possibly-shrunk echo.

---

## Q1 — Timelines vs CARD-0264 deploy

**Fix commit:** `0398a1ee` `fix(queue): correlate working backfill timestamps` at **2026-08-31T00:31:57+01:00** = `2026-08-30T23:31:57Z`. Ancestor of current `HEAD`.

**First AppHost start that can be serving it:** `2026-08-31 00:47:36 +01:00` = `2026-08-30T23:47:36Z` (`Now listening on http://[::]:63699`). Earlier restarts that night (`00:28:04 +01:00`) predate the commit.

Caller for all three: `cefed08a`, `AgentKind=ClaudeCode`, still Running.

### Task `0504ae08` — CARD-0261 investigation, fable, **before the fix**

| Clock (Z) | Event |
|---|---|
| 23:06:37 | Dispatched to session `bc47957c` (fable) |
| 23:09:22 | Caller `TurnEnd` seq 26722 — last idle before this incident |
| 23:21:34 | `[task fce28fc8 done]` UserPrompt starts a new caller turn (CARD-0263 merge) |
| **23:22:09** | **Settled Succeeded** (2,765 chars). Note enqueued 23:22:10, Origin=Delegation, WhenIdle, seq 688 on `cefed08a` |
| 23:22:46 | Caller text: "CARD-0261's investigation already finished and pushed — but I never got a `[task done]` notification" |
| 23:22:56 | `GET /api/agent-tasks/0504ae08` — `LastPolledResultAt` set. 47 s after settle |
| 23:23:10 | Caller text: "This confirms the CARD-0264 bug is still live right now (the fix hasn't merged yet)" — **misdiagnosis**: the session was not idle |
| 23:27:01–23:28:08 | Caller runs `restart-apphost.ps1` (CARD-0263 deploy). Server log: `Application started` 00:28:04 +01:00 |
| **23:29:55** | Caller `TurnEnd` seq 26779 (`end_turn`) — first idle since 23:21:34 |
| 23:30:01 | `AssistantText` + `TurnEnd` seq 26780/26781, **equal timestamp** (Claude same-line pair) |
| 23:30:02 | Note shrunk (`NoteShrunk`: polled 23:22:56; withheld 2,948 chars). Delivery starts |
| 23:30:03 | UserPrompt seq 26782: `[task 0504ae08 done]` **stub** ("Report withheld") |
| 23:30:34 | Late-confirmed Sent (CARD-0055 grace window; 3 Enter retries, then body appeared past baseline 26779) |

The 8-minute wait is the CARD-0263 merge turn, including an AppHost restart the orchestrator itself issued. `IsWorkingAsync` was right. The orchestrator's own "CARD-0264 is still live" line is how this card got its framing.

### Task `4b3b71b5` — CARD-0270 plan, **Codex sol**, after the fix

| Clock (Z) | Event |
|---|---|
| 08:16:22 | Created; Held behind `cb8e70d8` (shared-writer serialisation) |
| 08:35:51 | Dispatched, reused warm `task-cb8e70d8` (`gpt-5.6-sol`) |
| **08:47:37** | **Settled Succeeded** (824 chars). Note enqueued 08:47:38, queue seq 700 |
| 08:40–09:00 | Caller is merging CARD-0254 / processing CARD-0270 — continuous `ToolCall`/`ToolResult` through seq 27394 |
| 08:50:07 | AppHost restart (`Application started` 09:50:07 +01:00) *during that turn* |
| 08:50:50 | `331c0505` polled (this task not yet) |
| 08:59:27 | `GET /api/agent-tasks/4b3b71b5` — `LastPolledResultAt` |
| **09:00:17** | Caller `TurnEnd` seq 27395 |
| 09:00:08 (ts) / seq 27396 | Stale backfill: `ToolResult` "Restarting Antiphon AppHost..." (the CARD-0264 *shape*, after the fix). Exists-rule ranks it idle |
| 09:00:23 | `AssistantText` + `TurnEnd` 27397/27398, equal ts |
| 09:00:24 | Both 4b3b71b5 and 331c0505 notes shrunk. 4b3b71b5 delivery starts (one message per turn) |
| 09:00:25 | UserPrompt seq 27399: `[task 4b3b71b5 done]` stub |
| 09:00:56 | Late-confirmed Sent (baseline 27395) |
| 09:00:33 | Caller: "That's the delayed CARD-0270 plan notification catching up — already handled" |

~13 minutes, all genuine work. Post-fix backfill at 27396 did **not** strand the flush — the flush ran on the 27395 `TurnEnd` and the exists-rule would have ignored 27396 anyway.

### Task `331c0505` — CARD-0254 build, Codex terra, after the fix

| Clock (Z) | Event |
|---|---|
| 08:28:11 | Dispatched to worktree session `86777c84` (`gpt-5.6-terra`) |
| **08:49:02** | **Settled Succeeded** (1,898 chars). Note enqueued 08:49:03, queue seq 701 (behind 4b3b71b5) |
| 08:50:50 | Polled — 1 m 48 s after settle |
| 09:00:24 | First shrink (withheld 2,155 chars) — same flush that took 4b3b71b5 |
| 09:01:06 | Second shrink (withheld 460 chars) after 4b3b71b5's turn ended; watchdog delivers |
| 09:01:07 | UserPrompt seq 27402: `[task 331c0505 done]` stub |
| 09:01:10 | Log: `Stranded-queue watchdog delivered a pending message to idle session cefed08a` |
| 09:01:20 | Caller: "Another delayed notification for CARD-0254 — already fully handled" |

The second hop is CARD-0055's one-message-per-turn rule, not a kind bug. Watchdog firing on an idle session is the *fixed* path working.

### Contrast that proves poll-shrink is the "never arrived" illusion

| Task | Poll? | Wait | What the UserPrompt contained |
|---|---|---|---|
| `0504ae08` | yes, +47 s | 8 min | stub |
| `4b3b71b5` | yes, +12 min | 13 min | stub |
| `331c0505` | yes, +2 min | 12 min | stub |
| `27e0f9f8` (this afternoon, fable→Claude, **not named**) | **no** | 10 min | **full report** |
| `1af1c670` (Codex→Claude, overnight, **not named**) | no | **6.8 h** | full report |

`1af1c670` settled `00:26:36Z` (after `0398a1ee` was committed). The caller had issued a `ToolCall` at `00:15:40Z` whose `ToolResult` is "The user answered: …" at `07:15:37Z`. Seven hours blocked on a human question, then the note flushed on the next `TurnEnd`. That is WhenIdle doing what it says, post-fix, for 6.8 hours, without a false-working verdict.

Post-fix Delegation notes (created ≥ `00:47` BST): Claude callers have the only `>10 m` waits (4 of them, including the two named Codex tasks). Codex callers 9/10 `<30 s`; Grok callers 3/4 `<30 s`. Long waits track **who the caller is** (the busy Claude orchestrator), not who the worker is.

---

## Q2 — Does CARD-0264's mechanism generalize?

CARD-0264's machinery:

- Completion note: `EnqueueAsync(..., MessageSendMode.WhenIdle, Origin=Delegation)` on the **parent** session.
- Flush gates: enqueue-time, turn-end, stranded-queue watchdog — all `!IsWorkingAsync`.
- `IsWorkingBatchAsync` (`SessionMessageQueueService.cs:2606`): session is working iff there exists an activity row with `Sequence > lastEndSeq` whose own timestamp does not prove it predates the last end (`Ts == null || Ts >= lastEndTs`). Client `isWorking()` in `transcriptModel.ts:101` is the same exists-rule.

That rule is expressed entirely in normalized `TranscriptEntries.Kind` (`TurnEnd`, `SessionRestartBoundary`, manual `CompactBoundary`, interrupt prefix vs activity). It does not branch on `AgentKind`. It does not read Claude JSONL, Grok ACP, or Codex rollout.

What *does* have to be true for it to be correct on a Grok or Codex **caller**:

- That kind's normalizer must emit a `TurnEnd` when the turn is actually over.
- Activity after that end must carry timestamps that are not older than the end (or the exists-rule will correctly ignore them as stale backfill).
- A false `TurnEnd` (CARD-0282's fable phantom) makes the session look **idle** — the opposite failure, premature flush, not a strand.

Measured since 2026-08-20, `AssistantText` immediately before `TurnEnd`:

| Caller kind | pairs | equal ts | assistant earlier | assistant later |
|---|---:|---:|---:|---:|
| Claude (1) | 1,567 | **1,325** | 242 | 0 |
| Codex (2) | 117 | **0** | 117 | 0 |
| Grok (4) | 126 | **0** | 126 | 0 |

The equal-ts tie that beat the pre-`0398a1ee` group-max override is a Claude JSONL fact (one assistant record yields `AssistantText` + `TurnEnd` with one `timestamp`). Grok coalesces chunks and stamps `TurnEnd` from `turn_completed` (later event). Codex stamps `TurnEnd` from a later `task_complete` row. The old bug **could not have fired** on Grok/Codex callers in this database.

Stale post-end activity (the backfill *shape*) after `2026-08-31 00:00Z`: 9 rows, 2 sessions, **Claude only**. Seq 27396 on `cefed08a` this morning is one of them. The exists-rule is exactly the code that ranks those idle.

Tests already pin the incident shape without naming a kind (`A_stale_backfill_above_a_clean_same_timestamp_turn_end_reads_idle`, plus the mixed real-activity guard) in `SessionMessageQueueServiceTests.cs`. They exercise `TranscriptKinds`, so they cover any caller whose normalizer emits those kinds.

---

## Q3 — How each kind represents "a turn ended"

### Claude — `TranscriptNormalizer` (`src/Antiphon.SessionRunner/TranscriptNormalizer.cs`)

- Source: Claude Code JSONL `type: "assistant"` with `message.stop_reason`.
- `TurnEnd` is emitted on the **same record** as thinking/text/tool_use, same uuid, same timestamp, whenever `stop_reason` is non-empty and not `"tool_use"`.
- CARD-0282 (shipped): a fable `tool_use` block stamped `end_turn` no longer gets a phantom `TurnEnd`. Residual sibling-record hole (bare thinking record of the same response, no `tool_use` on that line) is documented on that card — false **idle**, not false working.
- Interrupt / `/compact` / `queued_command` are extra end/exclusion predicates that Grok/Codex largely do not need.
- `queued_command` attachments become `QueuedUserPrompt` and are excluded from activity (enqueue timestamp can predate file order).

### Grok — `GrokTranscriptNormalizer` (`src/Antiphon.SessionRunner/GrokTranscriptNormalizer.cs`)

- Source: ACP `updates.jsonl`. Stateful: message/thought chunks coalesce per `promptId` and flush when `turn_completed` arrives.
- `turn_completed` → `TurnEnd` with `stop_reason` **verbatim** (`end_turn`, `cancelled` for Esc, and measured `error` — 10 rows, all real ends: next activity is a new `UserPrompt` or nothing).
- Trailing chunk after `turn_completed` (file order ≠ eventId order) is emitted immediately with its **earlier** timestamp. The class remarks say this is why the timestamp override exists. The exists-rule handles it: higher seq, older ts → idle.
- `FlushPending()` on a dead child emits accumulated text and **deliberately no `TurnEnd`** — `SessionRestartBoundary` on relaunch is the end. Until then a Grok caller reads working (WhenIdle waits). That is the abandoned-turn case, not a silent-finished case.
- `auto_compact_completed` → `CompactBoundary` without `(manual)` — housekeeping, neither activity nor an end. Correct: Grok compaction writes no user chunk and no `turn_completed`.
- CARD-0159 (separate): a Grok **delegate** `cancelled` `TurnEnd` can settle a task on narration. That is settlement of the worker, not notification of the parent. On a Grok *caller*, `cancelled` still correctly unblocks WhenIdle.

### Codex — `CodexTranscriptNormalizer` (`src/Antiphon.SessionRunner/CodexTranscriptNormalizer.cs`)

- Source: rollout JSONL. Two dialects (flat `event_msg/agent_message` vs TUI `item_completed`); Antiphon launches the TUI dialect.
- `task_complete` → `TurnEnd` with a **synthesized** `stop_reason: "end_turn"` (Codex has no such field; `AgentSessionRuntime.IsTurnBoundary` keys on that string). Errors still end the turn so the queue flushes.
- `AssistantText` comes from an earlier `item_completed{AgentMessage}` / `agent_message` row — different event, later `task_complete` timestamp. Matches the 0 equal-ts measurement.
- Compaction (`compacted` + `context_compacted`) is mid-turn housekeeping and is **not** a `TurnEnd`. Correct, and the opposite of Claude's manual `/compact`.
- If `task_complete` never arrives, there is no `TurnEnd` and a Codex **caller** reads working until a restart boundary. Post-fix Codex-caller notes are fast (9/10 `<30 s`), so this is not currently stranding notifications. It is the residual Codex-caller risk if a rollout is truncated.

No analogue of CARD-0282's phantom `TurnEnd` is in the Grok or Codex catalogs. The thing to watch is the opposite: a too-early `turn_completed` / `task_complete` while tools are still running. Not observed in the TurnEnd/stop_reason census above.

---

## Q4 — How the orchestrator should trust "finished" across kinds

**Do not unify on the caller's transcript as the settlement signal.** Settlement already happened on the *delegate's* `TurnEnd` (that is what `AgentTaskReplyService` observed). The `[task done]` UserPrompt is a delivery echo onto a WhenIdle parent queue. Absence of that echo means "parent has not been idle since settle" (and maybe "parent already polled"), not "child still running".

### What is already true, for every kind

- `AgentTasks.Status` / `CompletedAt` is the finish fact. `GET /api/agent-tasks/{id}` and `delegate.ps1 -Status` read it. The three named incidents discovered the result that way, in 47 s / 2 min / 12 min.
- Check-ins cannot substitute: they are canceled at settle, and they are themselves WhenIdle notes to the same busy parent. All three named tasks had `checkCount = 0` (they finished before `expectedDurationMinutes`).

### Proposed direction (build later; not this pass)

**S1 — Contract, not code.** Bundle / orchestrator instructions: a missing `[task done]` turn is not evidence the child is running. Poll the task row (or the task list) when the answer matters. The turn, when it lands, may be a CARD-0132 stub. This matches what `cefed08a` already did, and is the only kind-agnostic way to make silence untrustworthy-as-running *today*.

**S2 — Build CARD-0267** (already Backlog, CARD-0264 secondary #2). An attention row for a Delegation/Check note Pending on the caller past ~10 minutes. That is the human-visible "this finished and the echo has not landed" signal. It would have fired on `1af1c670` overnight and on the 12-minute morning pair. It does **not** inject a UserPrompt, so it does not wake a blocked orchestrator — it tells an operator the queue is stuck.

**S3 — Do not SendNow-while-working.** Typing into a live composer is how CARD-0266's orphan-fusion happens and how CARD-0132's `queued_command` duplicates happened. WhenIdle exists to not do that. A one-line "settled" ping that bypasses `IsWorkingAsync` would re-open those cards for a shorter body.

**S4 — Cheap lock-in tests, not a new mechanism.** Add two `IsWorkingAsync` shapes next to the CARD-0264 tests: (a) Grok/Codex-like `AssistantText` at T, `TurnEnd` at T+δ, then a stale backfill above the end → idle; (b) Grok trailing chunk (higher seq, older ts) after `TurnEnd` → idle. The exists-rule already implies both; the tests stop a future kind-specific "equal ts keeps sequence verdict" comment from coming back.

**S5 — Out of scope here, name it.** A non-composer side channel the *agent* can see (a synthetic UserPrompt that is not the report, a tool result, a dedicated `/api/agent-tasks?unread=1` the orchestrator is told to call on a timer) would close the "busy for 7 hours on AskUserQuestion" hole without interrupting the turn. That is a product change, not a normalizer change, and it is the same design for Grok, Claude, and Codex callers.

### Explicitly rejected

- Kind-switching `IsWorkingAsync` (Claude equal-ts vs Grok later-ts vs Codex `task_complete`). The exists-rule already covers all three measured shapes.
- Treating the three named tasks as proof CARD-0264's fix is Claude-only and therefore incomplete. The delay was on a Claude **caller** for every named task; the fix is not Claude-shaped.
- Re-deriving Grok/Codex "turn ended" from raw ACP/rollout inside the queue service. The normalizers exist so the queue does not do that.

---

## What a later Code pass should and should not touch

| Do | Do not |
|---|---|
| S1 instruction-bundle wording if the orchestrator keeps misreading silence | `IsWorkingBatchAsync` kind branches |
| CARD-0267 attention row | `MessageSendMode.Now` for completion notes |
| S4 tests for Grok/Codex timestamp pairing | Re-opening CARD-0264 as a live defect on this evidence |
| | Changing Claude/Grok/Codex `TurnEnd` emission to "match" each other |

CARD-0266 / 0268 / 0269 remain CARD-0264's own follow-ups (composer orphan, catch-up/subscribe crack, log wording). They are not required to close CARD-0285's question.

---

## Decision that is the caller's

CARD-0285 can close as "CARD-0264 holds; remaining gap is WhenIdle-to-busy-caller + poll-shrink, already owned by CARD-0267 + the task-row contract" — or stay open only for S1 (bundle wording) / S4 (lock-in tests). There is no kind-specific notification bug to design a third mechanism for on this evidence.
