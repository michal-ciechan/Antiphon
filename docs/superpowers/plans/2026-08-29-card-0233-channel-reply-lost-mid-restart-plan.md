# CARD-0233 — plan: a completed channel turn classified as lost because a mid-turn launch note stole its identity (2026-08-29)

Plan only; nothing here is built. Four slices (S1 owning-prompt, S2 extraction window, S3
distinguishable loss reasons, S4 launch-note vs pending channel), plus two operator decisions
that change who hears about the next miss.

**Card:** CARD-0233 (`67e978df-af94-4874-8a0e-5d0ba901e39c`) — AZ Care Telegram reply silently
lost during a mid-delivery session restart cascade (2026-08-27 ~21:03 BST).
**Incident:** session `276811ea-624b-4e32-941b-bb660197d486`, queued message
`bf09ed01-abd7-4b50-9f68-22ef5d4f6de9`, `SentAt` 20:06:54Z, `ChannelReplyLost` 20:38:26Z.
**Precedent:** CARD-0067 (durable correlations, no silent miss) and CARD-0068 (sequence-window
follow-up). Neither was tested against a launch-note `QueuedUserPrompt` landing *inside* the
channel turn they are matching.

## Verdict

**The card's working hypothesis is replaced, not confirmed.** Dispatch does not store a sequence
baseline on the queued message, and the restart did not invalidate sequences — they continued
monotonically 720 → 721 → … → 742 (`PersistTranscriptAsync` rebases across relaunches,
`AgentSessionRuntime.cs:591-594`). A completed assistant turn answering the Telegram message
**does exist**, byte-identical to the queued body, and `DispatchAsync` ran twice against that
turn. Both attempts attributed the turn to the **wrong prompt**, so `PromptsMatch` failed, the
correlation stayed owed, and the TTL sweep wrote `ChannelReplyLost` / `StaleTtl` with the text
"no turn matching the message completed" — a lie. The restart cascade is still causal: it is
why the stealing prompt (the fresh-session bootstrap) was typed into a composer that was
already working.

This is a one-off in the live tables (`AgentIncidents.Kind = 21` has this single row; the
mid-turn steal shape on channel-bound agents is this single `QueuedUserPrompt` 734). The
mechanism is standing, not accidental.

## 1. The turn that completed — raw records, not the card's memory

JSONL (sidecar `how: exact`, `resumeLaunch: false`):
`C:\Users\lndco\.claude\projects\C--src-ClaudeBot-agents-az-care\276811ea-624b-4e32-941b-bb660197d486.jsonl`
created `2026-08-27T20:07:15.883Z`, 73 lines. DB `TranscriptEntries` for the same session id
agree on sequence, kind and text.

| seq | kind | timestamp UTC | created UTC | text |
|---|---|---|---|---|
| 720 | TurnEnd | 19:13:45 | 19:13:51 | previous turn (seq 700, "3 suggestions for certificates", **settled** 19:13:52) |
| **721** | **UserPrompt** | **20:07:11.324** | **20:07:16.152** | `[Telegram "AZ Care" — Mike Ciechan 21:03] Give me message to Phil asking for quote for all 3 certificates` |
| 722–733 | ToolCall/Result | 20:07:19–20:07:39 | | memory / CLAUDE.md / git / property notes |
| **734** | **QueuedUserPrompt** | **20:07:32.347** | **20:07:40.061** | `New session started. Follow your CLAUDE.md session-start ritual now … then reply READY.` (`ChannelPreamble.BootstrapBody`) |
| 735–740 | ToolCall/Result | 20:07:48–20:08:21 | | letting checklist / memory log commit |
| **741** | **AssistantText** | **20:08:25.060** | **20:08:26.713** | `Hi Phil,` / need EPC + CP12 + EICR / Flat B, 39 Springfield Road / `Thanks, Ola Zawojska` (497 chars) |
| **742** | **TurnEnd** | **20:08:25.060** | **20:08:29.693** | `end_turn` |

JSONL line 10 is the user record for 721; line 42 is `queue-operation` at 20:07:32 (Claude's
composer queue, normalized to `QueuedUserPrompt` by `TranscriptNormalizer.FromAttachment`);
line 71 is the assistant record with `stop_reason=end_turn` that produced 741+742.

Queued body `bf09ed01` and UserPrompt 721 are **byte-identical**, including the em-dash
(`e2 80 94`). `PromptsMatch` would have succeeded if dispatch had looked at 721. No later
`UserPrompt` / `QueuedUserPrompt` / `TurnEnd` exists on this session after 742 — dispatch had
exactly those two chances, then silence until TTL.

## 2. What dispatch actually did (server log, BST = UTC+1)

`server/logs/antiphon-20260827_001.log`:

```
21:08:29.439 [WRN] Turn on session 276811ea… (prompt seq 700) matched NONE of the 1 channel
                 correlation(s) still owed a reply for telegram:-5031013177
21:08:29.763 [WRN] Turn on session 276811ea… (prompt seq 734) matched NONE of the 1
                 channel correlation(s) still owed a reply for telegram:-5031013177
21:08:30.077 [INF] Broadcasting SessionFinished for session 276811ea… (AZ Care)
21:38:26.823 [ERR] CHANNEL REPLY LOST: … never answered — no turn matching the message
                 completed within 30 minutes. Oldest owed since 2026-08-27 20:06:54Z.
                 Message ids: bf09ed01-…
```

Never seq 721. Two failures, two different wrong prompts, 324 ms apart, then idle, then TTL
from `SentAt` + 30 minutes (`PendingReplyTtlMinutes` default, `ChannelBridgeSettings.cs:25`).

### 2.1 First miss — AssistantText-triggered dispatch still sees the previous TurnEnd

`ObserveTranscriptAsync` (`AgentSessionRuntime.cs:236-264`) persists **one** runner event, then
if the event is `AssistantText` calls `DispatchChannelRepliesAsync` (`:263-264`). Claude's
`end_turn` record is two parts (AssistantText then TurnEnd). So:

1. AssistantText 741 lands (`CreatedAt` 20:08:26.713Z). Dispatch runs. Latest `TurnEnd` in the
   table is still **720**. Preceding prompt = latest `UserPrompt|QueuedUserPrompt` with seq < 720
   = **700** (yesterday-hour's "3 suggestions" prompt). `PromptsMatch(new 21:03 body, seq 700)`
   fails. Window for 700 is capped at 721 anyway, so the new `Hi Phil` text is not even in it.
2. TurnEnd 742 lands 253 ms later (`CreatedAt` 20:08:29.693Z). Dispatch runs again.

This is the inverse of the 2026-07-24 stop-marker-before-text case that justified the
AssistantText re-trigger (`ChannelBridgeTests.A_turn_whose_stop_marker_precedes_the_text_still_replies`).
Here the text arrives **before** its own TurnEnd is stored, so the re-trigger attributes the
new text to the **previous** turn.

### 2.2 Second miss — CARD-0154 ranks the in-turn launch note as the turn's identity

`DispatchAsync` (`ChannelReplyDispatcher.cs:178-198`):

```
turnEndSeq = max(TurnEnd)                            // now 742
userPrompt = latest UserPrompt|QueuedUserPrompt
             where Sequence < turnEndSeq             // 734, the bootstrap
```

CARD-0154 widened that query so a channel message that exists *only* as `QueuedUserPrompt`
(typed into a busy composer, no `UserPrompt`) still matches. Ranking is "latest of either
kind", so a `QueuedUserPrompt` that landed **inside** an already-open `UserPrompt` turn wins.
`PromptsMatch(channel body, BootstrapBody)` fails. `_dispatched` is never written, so
`DispatchFollowUpAsync` is a no-op (`OnTurnEndAsync` `:129-130`).

### 2.3 Even a naive "use 721" fix would still drop the reply

`QueryTurnWindowAsync` (`:723-745`) caps AssistantText at the next `UserPrompt` **or**
`QueuedUserPrompt`. Owning = 721 ⇒ `nextPromptSeq` = 734 ⇒ AssistantText 741 (seq 741 > 734)
is **excluded**. Empty window ⇒ `"no assistant text yet; correlations stay pending"` (`:236-240`)
⇒ same TTL. S1 without S2 is not a fix.

`DispatchFollowUpAsync` is not the owner of this miss. CARD-0068's window is the extraction
helper the main path also uses; that is why S2 touches it.

## 3. Why the stealing prompt exists — the restart cascade, confirmed from the log

The session id is persistent (AlwaysOn AZ Care). Sequences were **not** reset (721 follows 720
of the previous incarnation). What the cascade *did* do:

| BST | UTC | what |
|---|---|---|
| 21:03:55 | 20:03:55 | channel message enqueued (Origin=Channel, seq 85) |
| 21:04:19–21:05:06 | 20:04:19–20:05:06 | first delivery: `NoTranscriptRecord` past seq 720, always-on killed |
| 21:05:48 | 20:05:48 | `Claude conversation for session was not found; starting fresh with the same id` |
| 21:05:51 | 20:05:51 | child start (sidecar `childStartUtc`; `resumeLaunch: false`) |
| 21:05:54 | 20:05:54 | re-adoption of pid 46540 / host 13308 |
| 21:06:32 | 20:06:32 | RC did not arm (20 s) |
| 21:07:10 | 20:07:10 | `AdoptionRefused` (C0/C4, 450 files) — bind still racing the exact-id file, created 20:07:15Z |
| 21:07:16 | 20:07:16 | stranded-queue watchdog delivered the channel message; UserPrompt 721 persisted |
| 21:07:11–21:07:26 | 20:07:11–20:07:26 | `/remote-control` (15 chars) retried 3×, no composer evidence — turn already running |
| 21:07:26 | 20:07:26 | RC given up; `DeliverLaunchNoteAsync` runs next (`AgentSessionService.cs:388-394`) |
| 21:07:32 | 20:07:32 | Claude `queue-operation` → QueuedUserPrompt 734 = `ChannelPreamble.BootstrapBody` |

`DeliverLaunchNoteAsync` (`AgentSessionService.cs:1461-1479`) enqueues the bootstrap
**Mode.Now**, and Mode.Now **has no `SessionQueuedMessage` row** (`SessionMessageQueueService.cs:163-239`,
CARD-0164 B4). That is why `SessionQueuedMessages` for this session has no bootstrap row — the
seven historical `New session started.` rows in the DB are all 2026-08-16, other sessions,
Origin=System. The launch path typed the ritual into a composer that was already mid-turn;
Claude queued it; the normalizer recorded `QueuedUserPrompt`.

Launch order is structural: RC → launch note (Now) → `FlushSessionAsync` of anything that sat
Pending during Starting. The watchdog delivered the channel message *during* RC retries, so
by the time the note typed, the turn was live.

Same-day channel messages on this session (seq 79–84, 16:48–19:13Z) all settled within minutes.
Seq 85 is the first that overlapped a fresh-session bootstrap.

## 4. Hypothesis verdict

| claim | verdict | evidence |
|---|---|---|
| Restart invalidated a stored sequence baseline on the queued message | **Refuted.** Dispatch stores no baseline on the row. Sequences continued 720→742. | `ChannelReplyDispatcher.cs:173-198`; `AgentSessionRuntime.cs:591-594`; DB |
| A completed assistant turn answering "message to Phil" exists | **Confirmed.** | JSONL line 71; DB seq 721/741/742; body hex match |
| Dispatch never ran | **Refuted.** Two WRN lines at TurnEnd-time. | log 21:08:29.439 / .763 |
| The restart cascade is unrelated | **Refuted.** It is why BootstrapBody was typed mid-turn. | `starting fresh`; Mode.Now note; QueuedUserPrompt 734 |
| Replacement root cause | **CARD-0154's "latest UserPrompt\|QueuedUserPrompt before latest TurnEnd" plus the AssistantText re-trigger seeing the previous TurnEnd, with extraction also capping on in-turn QueuedUserPrompt.** | §2.1–2.3 |

## 5. Other occurrences

- `AgentIncidents.Kind = 21`: **one row**, this one (`FailureReason = StaleTtl`).
- Mid-turn steal (a `QueuedUserPrompt` sitting between a `UserPrompt` and that turn's `TurnEnd`,
  no later `UserPrompt` before the TurnEnd) on **channel-bound** agents: **one row**, seq 734
  on this session. The orchestrator session `cefed08a` has dozens of `QueuedUserPrompt`s (task
  completion notes) but is not channel-bound and does not route through this dispatcher.
- Same session, same day, six earlier Channel messages settled normally. Same dispatcher,
  no in-turn bootstrap.
- `matched NONE` WRNs fire routinely on this agent when a non-channel turn ends while a
  correlation is still owed (seq 533/547/570/620/689 earlier on 08-27). Those later matched.
  This one never did, because the matching prompt was never the one `DispatchAsync` read.

Class: standing mechanism, one live hit. A future AlwaysOn restart that races a pending
channel message will reproduce it.

## 6. Why the operator saw silence in Telegram

`RecordIncidentAsync` did raise an `Alerts` row (`538d5f64`, Critical,
`supervisor:ChannelReplyLost:8acdd711-…`, 20:38:26.980Z). `ChannelAlertRouter` only fans to
channels with `AlertMinSeverity != null` (`ChannelAlertRouter.cs:48-52`). **Every**
`ChatChannels` row currently has `AlertMinSeverity` null, including AZ Care
(`-5031013177`) and Family. Routing enabled defaults true; there is simply no sink.
Attention `RecentCriticalIncident` (`AttentionService.cs:42, 798`) would have listed it
for 24 hours in the Antiphon UI, then dropped it. The originating chat was never told.

This matches the card: the incident exists in our tables and is not itself a Telegram
message. CARD-0171 already decided the alert-sink path is the wrong megaphone for a
per-conversation fact.

## 7. What exists today (the pieces the fix reuses)

- Durable correlation on `SessionQueuedMessage` (Body, ConversationKey, ChannelReplySettledAt)
  — CARD-0067. Do not rebuild a correlation map. Do not re-open a settled row.
- `PromptsMatch` containment of a 120-char probe — works; 721 vs body is identical.
- CARD-0154 test `A_queued_channel_prompt_still_routes_the_reply`: a turn whose **only**
  prompt is `QueuedUserPrompt` must still match. S1 must keep that fallback.
- CARD-0154 / CARD-0068 test `Follow_up_stops_once_the_next_queued_prompt_starts`: a
  `QueuedUserPrompt` **after** the settled turn's TurnEnd must cap follow-up. S2 must keep
  that cap for *next-turn* queued prompts, and drop it only for *in-turn* ones.
- `LossReason` already has `StaleTtl` vs `Unroutable` (`ChannelReplyDispatcher.cs:49-57`);
  both write Critical `ChannelReplyLost`. The StaleTtl message is hardcoded to "no turn
  matching the message completed" (`:443`).
- `ReviewReplyDispatcher.cs:78-86` copies the same latest-of-either-kind query. Same steal,
  no channel; still fix in lockstep so the two dispatchers do not drift (CARD-0154 comment
  says they share the kind set).
- Launch note is Mode.Now on purpose (`AgentSessionService.cs:1462-1466`): a WhenIdle enqueue
  on a resume whose transcript still reads working would strand. The defect is Now-mode
  **racing a Channel-origin row that is already in flight**, not Now-mode itself.

## 8. The fix, in this order

### S1 — owning prompt is the turn opener, not the latest of either kind

`ChannelReplyDispatcher.DispatchAsync` (`:192-198`) and the copy in
`ReviewReplyDispatcher` (`:80-86`).

Given latest TurnEnd `T` and previous TurnEnd `T0` (or 0):

```
window = prompts (UserPrompt | QueuedUserPrompt) with T0 < seq < T
owning = latest UserPrompt in window, else latest QueuedUserPrompt in window, else none
```

This incident: window (720, 742) contains UserPrompt 721 and QueuedUserPrompt 734 → own
**721**. CARD-0154: window contains only QueuedUserPrompt → own that.

Do not switch to timestamps. CARD-0068 already settled that (`CreatedAt` is the batch stamp;
`Timestamp` is nullable). Sequence order in a live or catch-up persist is file order.

Verification: new `ChannelBridgeTests` method that inserts this session's shape (UserPrompt
channel body, QueuedUserPrompt = `ChannelPreamble.BootstrapBody`, AssistantText, TurnEnd)
and asserts one Telegram reply with the AssistantText and `ChannelReplySettledAt` set. Existing
`A_queued_channel_prompt_still_routes_the_reply` must stay green.

### S2 — in-turn QueuedUserPrompt must not cap the extraction window

`QueryTurnWindowAsync` (`ChannelReplyDispatcher.cs:723-745`), shared with follow-up.

A `QueuedUserPrompt` caps the window **only if it is a next-turn opener**: there is a TurnEnd
between `promptSeq` and that queued prompt. An in-turn queued prompt (no TurnEnd between the
owning prompt and it) is not a cap.

| shape | cap | result |
|---|---|---|
| this incident: 721, then 734 queued, then 741 text, then 742 TurnEnd | no cap at 734 (734 < 742) | 741 included |
| `Follow_up_stops_once_the_next_queued_prompt_starts`: TurnEnd of A, then queued B, then B's text | B caps (TurnEnd of A sits between A and B) | B's text not sent as A's follow-up |
| CARD-0154 queued-only turn | next UserPrompt or next-turn queued | unchanged |

S1 without S2 leaves 741 outside the window. Both land together; they are one Code slice if
the tests fit, two if review wants them separable. Either way S2 is not optional.

Verification: the S1 fixture already requires 741 to be the reply body (not empty, not the
bootstrap). Add the existing follow-up-stops-on-queued-prompt test to the same run so S2
cannot "fix" 741 by deleting the CARD-0154 cap.

### S3 — the incident must say which of the three silences happened

`LossReason` + `ReportLostAsync` (`:438-444`, `:488-496`) + `AbandonStaleCorrelationsAsync`.
Before settling a TTL row, look at the transcript:

| observed | `FailureReason` | human sentence |
|---|---|---|
| no matching UserPrompt/QueuedUserPrompt after `SentAt` | `StaleTtl` (keep) | no turn matching the message completed within N minutes |
| matching prompt, no TurnEnd after it | `TurnIncomplete` (new) | a matching prompt was recorded but no turn completed within N minutes |
| matching prompt **and** TurnEnd **and** AssistantText, never settled | `TurnUnmatched` (new) | a turn completed (prompt seq X, text Y chars) but the dispatcher did not route it |

All three stay Critical `ChannelReplyLost`. The point is the next operator does not need a
manual DB dig to tell "agent never answered" from "agent answered and we dropped it". Include
the prompt seq and a 80-char excerpt of the unmatched UserPrompt in the incident message
(already clipped at `AgentIncident.MessageMaxLength`).

Pin with three `ChannelReplyDurabilityTests` additions: TTL with no prompt; TTL with prompt
and no TurnEnd; TTL with this incident's shape (prompt + text + TurnEnd, dispatch skipped or
forced to miss). The third must assert `FailureReason == "TurnUnmatched"` and that the
message names the prompt seq.

### S4 — do not Mode.Now a launch note over a Channel-origin in-flight turn

`DeliverLaunchNoteAsync`. If the session has a Channel-origin row that is Pending or Sent
with `ChannelReplySettledAt == null`, enqueue the launch note **WhenIdle** (Origin=System)
instead of Now. The channel preamble already rides `--append-system-prompt`; the ritual can
wait one turn. WhenIdle is the existing fallback when Now throws (`:1487-1488`).

This stops the steal from being manufactured. S1/S2 still have to exist: Claude's own queue
can enqueue other bodies mid-turn (operator typing, a completion note), and CARD-0154's
queued-only channel message remains legal.

Verification: a launch of a preamble-configured agent with a Pending Channel row asserts the
note is WhenIdle / Origin=System and is not typed until after that channel turn's TurnEnd.
`AgentSystemPromptLaunchTests` currently expect `SubmittedBodies = [BootstrapBody]` on a
clean start — keep that for the no-pending-channel case.

## 9. Operator decisions

1. **S4 default.** Recommend **WhenIdle behind an owed Channel row**. The human in the chat
   is waiting on *this* question; the session-start ritual is for the agent's workspace, not
   for them. Say if you would rather the ritual always win (Now), accepting that S1/S2 are
   then the only safety net on every AlwaysOn restart that races a pending Telegram message.
2. **Who is told on `ChannelReplyLost`.** Recommend a **targeted send to the originating
   conversation** (CARD-0171's `ChatChannelService.SendAsync` shape: per-trigger, addressed
   from `ConversationKey`, not `ChannelAlertRouter`). The alert-sink path is empty today
   (every `AlertMinSeverity` is null) and, if someone fills it, would dump every Critical
   into Family. The originating chat is the one that asked. Say if you want that, or
   Attention-feed-only (status quo, 24 h then gone), or both.
3. **Correlation-centric dispatch (optional S5).** S1+S2 are sufficient for this incident:
   the latest TurnEnd *was* the channel turn, just mis-identified. A later unmatched turn
   completing first would still leave an earlier completed channel turn unexamined — today's
   "latest TurnEnd only" design, documented as legitimate when the operator types while a
   chat message is in flight (`:252-256`). Widening `DispatchAsync` to walk open correlations
   against any prompt in `(T0, T)` is a larger change and is **not required** to stop this
   miss. Recommend **not in this card** unless you want the dispatcher to recover a
   completed-but-misidentified turn on a *subsequent* unrelated TurnEnd as well.

## 10. Not planned

- Re-opening `ChannelReplySettledAt` or making `_dispatched` durable. CARD-0067 left the
  watermark process-memory on purpose; this miss never reached follow-up.
- Changing `PromptsMatch` (bodies already match).
- Resetting transcript sequences on relaunch (they are correct).
- Auto-sending the `Hi Phil` text now. The correlation is already settled as lost; a
  one-shot operator send from the transcript is a human action, not a code path.
- Filling in `AlertMinSeverity` on AZ Care or Family as a substitute for decision 2.
- Killing or restarting session `276811ea`. It has been idle since 20:08:25Z; the sidecar
  still points at this JSONL.

## AGENTS.md gotcha to add when this lands

> **A mid-turn `QueuedUserPrompt` must not become the channel-reply turn's identity**
> (CARD-0233): `DispatchAsync` used to take the latest `UserPrompt|QueuedUserPrompt` before
> the latest `TurnEnd` (CARD-0154's queued-only widen). A fresh-session bootstrap typed
> Mode.Now into a composer that was already answering a Telegram message landed as
> `QueuedUserPrompt` inside that turn; dispatch attributed the `Hi Phil` answer to
> `ChannelPreamble.BootstrapBody`, `PromptsMatch` failed, and 30 minutes later
> `ChannelReplyLost` claimed "no turn matching the message completed". Owning prompt is
> now the latest **UserPrompt** in `(prevTurnEnd, thisTurnEnd)`, falling back to
> `QueuedUserPrompt` only when that window has none; an in-turn queued prompt does not
> cap `QueryTurnWindowAsync`. `DeliverLaunchNoteAsync` yields to an owed Channel row
> (WhenIdle) instead of racing it.

## Suggested Code slices

| slice | owner | files | verification |
|---|---|---|---|
| S1+S2 | opus | `ChannelReplyDispatcher.cs`, `ReviewReplyDispatcher.cs`, `ChannelBridgeTests.cs` (new fixture = this incident; existing CARD-0154 pair stay green) | `--treenode-filter "/*/Antiphon.Tests.Application/ChannelBridgeTests/*"` plus `ReviewReplyDispatcherTests` |
| S3 | sonnet | `ChannelReplyDispatcher.cs` `LossReason`/`ReportLostAsync`, `ChannelReplyDurabilityTests.cs` | that class |
| S4 | sonnet | `AgentSessionService.DeliverLaunchNoteAsync`, `AgentSystemPromptLaunchTests` | that class; the new "pending Channel → WhenIdle" case |
