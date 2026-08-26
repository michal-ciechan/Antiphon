# CARD-0036 — "Catch up on what happened while I was away": the push half, designed

**Date:** 2026-08-26
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0036 (`93ae6cdb-8638-4b3b-8d41-563dbc93d548`), story S6 in `docs/product/user-stories.md:133`
**Model followed:** `docs/features/010-home-tasks-section/proposal.md` (a "what exists, verified against
the code" section first, with file paths; then the design; then what it costs its neighbours).
**Narrowed by:** `docs/superpowers/specs/2026-08-17-mobile-thread-and-plan-surfacing.md` §D3/§2/§9.2,
which shipped the *pull* half as band 3 of the mobile home and left 0036 "push + digest-as-prompt".

## Verdict, in one screen

The card asks four questions. The answers, each argued in §2:

| Question | Answer |
|---|---|
| Pushed or pulled? | **Both, with different content.** Pull is shipped (mobile band 3) and stays the rich surface. Push is Telegram, composed **server-side**, sent as a **channel message** through `ChatChannelService.SendAsync` — never typed into any agent's terminal. |
| Trigger? | **Two triggers, two message kinds.** (a) A *state change worth waking for* sends one small **ping** at once — and the only state in that set is **a task becoming Blocked** (a question, a cost ceiling, a merge conflict: the three ways a delegate stops until a human acts). (b) A *time-based* **digest** at configured local times (default 08:00 and 18:00), sent **silently** (`disable_notification`), covering everything since the previous digest. |
| How much fits? | One Telegram message: **hard cap 3 500 chars** (the `TrackerSyncSummaryFormatter.MaxChars` convention under the 4 096 platform cap), **target under 1 500**, five rows per section then `+ N more`, roots only (children roll up). |
| Actions from the digest? | **Answer a blocked delegate by replying to its ping in Telegram** — the reply-quote's 160-char excerpt carries the task's short id, and the answer goes through the *existing* `AgentTaskReplyService.AnswerAsync`. A typed fallback (`1a2b3c4d: your answer`) and `/digest` (send one now). Nothing else in v1; cancel/retry/escalate stay one tap away on the phone home. |

The reason "digest-as-prompt" is rejected outright, not sized: the reader of this message is a
human on a phone, not an agent, so the CARD-0027/0037 pty ceilings (§1.6) simply never apply to
the digest. The only thing that is ever typed into a session is the human's *answer*, which takes
the same transcript-confirmed queue path a web reply takes today.

**What is genuinely new** (§3): one projection service, one pure formatter, one hosted service,
one inbound branch in the channel bridge, two `ChatChannel` columns, one appended
`AgentTaskEventType`, one settings section, two endpoints, one script, one toggle on the
Channels page. No new message-bus contract, no Telegram features the adapter does not already
carry.

---

## 1. What exists today (verified against the code, 2026-08-26)

### 1.1 The pull half is shipped — and it is the spec for what the digest should say

- `client/src/features/home/awayDelta.ts:55-105` — `computeAwayDelta(tasks, cards, lastSeenUtc,
  nowUtcMs, plans)`: settled tasks in the window (excluding `role === 'Check'`, `:77`), cards whose
  `startedAt`/`completedAt` fall in the window (`:81-89`), plans modified in the window,
  `settledSpendUsd` = the sum of each settled task's **own** `costUsd` (`:91`, with the reasoning at
  `:46-51`: never the subtree, so a parent and child settling together are not double-counted;
  never "all spend in the window", which is not derivable from cumulative rows).
- The window's start is a **per-browser** `localStorage` stamp (`AWAY_LAST_SEEN_KEY`, `:15`;
  `readLastSeen`/`stampLastSeen` `:123-131`), falling back to the last 24 h on a first visit
  (`AWAY_FALLBACK_HOURS`, `:18`).
- Rendered by `AwayBand` in `client/src/features/home/MobileHomePage.tsx:319-412`: settled tasks
  first (six rows, then `+ n more settled`, `MAX_AWAY_TASK_ROWS` `:310`), each with the first
  sentence of its report (`firstSentence`, `awayDelta.ts:112-120`, over the detail DTO's `result`),
  then card changes, then plans, then one dimmed spend line (`:398-402`). Empty state is one line:
  "Nothing finished while you were away." (`:343-345`).
- The stamp is taken on mount (`:79-86`), so the window is "previous visit → this visit".

Two properties of this that the push half must NOT inherit: the window is **per device** (a phone
and a laptop disagree about "since when"), and the spend figure is **client-computed from the
summary list**, which is fine for a screen but is the wrong place for a number that will be
quoted from a chat message.

### 1.2 The "needs you" projection exists and already names the actions

- `GET /api/attention` → `AttentionService.GetAsync` (`server/Application/Services/AttentionService.cs:114`),
  fleet-global, read-only, degrading (`:15-35`). Kinds in `server/Application/Dtos/AttentionDtos.cs`
  (`AttentionKind`, `:10-95`): `BlockedQuestion` is Critical and carries `[Reply, Cancel, Escalate]`
  (`AttentionService.cs:212-224`), with the delegate's question in `Evidence` — `FailureReason`
  when the dispatcher or the conflict path blocked it, else `Result` (the delegate's own words)
  (`:202-206`). `SubtreeCostUsd` is on every row (`AttentionDtos.cs:147-162`,
  `AttentionService.LoadSubtreeCostsAsync` `:866`).
- The mobile home renders Critical/Error attention rows as band 1 with the reply box inline
  (`MobileHomePage.tsx:200-270`, `BlockedReplyRow` from `client/src/features/attention/`).

The digest must not re-derive stuckness (the spec's §D4 rule, and CARD-0035's non-membership
rule at `AttentionService.cs:17-23`): "needs you" rows in the digest are **`AttentionKind.BlockedQuestion`
rows from this projection**, nothing computed twice.

### 1.3 The blocked-delegate story (S3): the machinery is complete, the *reach* is the gap

- Answering: `AgentTaskReplyService.AnswerAsync(taskId, message)`
  (`server/Application/Services/AgentTaskReplyService.cs:224-256`) — refuses unless `Status ==
  Blocked` (`:236`) or the session is gone (`:238`), sets `Working`, writes a `Replied` event, and
  enqueues `TaskMarker + answer` **WhenIdle** with `QueuedMessageOrigin.Delegation` (`:246-249`) —
  i.e. transcript-confirmed delivery (CARD-0055), never a raw type.
- Endpoint: `POST /api/agent-tasks/{id}/reply` (`server/Api/Endpoints/AgentTaskEndpoints.cs:69-78`);
  `{id}` accepts a full guid **or the 8-hex short id** via `AgentTaskService.ResolveTaskIdAsync`
  (`server/Application/Services/AgentTaskService.cs:702-728`; ambiguity and not-found are typed
  exceptions with readable messages). The short id is `DelegationReportFormatter.Short`
  (`server/Application/Services/DelegationReportFormatter.cs:19`).
- Shell: `scripts/delegate.ps1 -Reply <taskId> "answer"` (`:91-101`, `:151-157`).
- Three writers put a task into `Blocked`: the settlement path's question detector
  (`AgentTaskReplyService.cs:390`, `LooksLikeAQuestion` `:1521`), the dispatcher's per-root cost
  ceiling (`server/Application/Services/AgentTaskDispatcher.cs:266-268`, "Run cost ceiling
  reached"), and a merge-back conflict (`AgentTaskReplyService.cs:976-982`, `Conflicted` event).
  `AttentionService.BuildBlockedAsync` already dates a block from the latest `Blocked` **or**
  `Conflicted` event (`:185-198`) for exactly this reason.

**What does not exist:** nothing *tells* anyone a task became Blocked. The alert sources in the
codebase (`grep 'Source: "'` — `supervisor`, `delegation`, `reconciler`, `log`, `bridge`) raise for
uncorrelated reports (`AgentTaskReplyService.cs:205-207`), API-error deaths (`:711`, `:776`),
check-interpreter outages, bind failures — **never for Blocked, Failed or Succeeded**. A blocked
delegate is discovered by opening the app. That is the loop the card wants closed.

### 1.4 Telegram outbound: two existing server-side paths, and the right one is the targeted one

| Path | Where | Selects the chat by | Voice / shape | Fit for the digest |
|---|---|---|---|---|
| Alert sinks | `ChannelAlertRouter.RouteAsync` (`server/Application/Services/ChannelAlertRouter.cs:37-61`) → `AlertDigestFlusher.FlushDueAsync` (`:102-160`), ticked by `server/Infrastructure/Supervision/AlertDigestFlushHostedService.cs:27-71` | `ChatChannel.AlertMinSeverity` (severity alone) | `"Antiphon alerts:"` + one line per grouped alert, `MinMinutesBetweenSends` 5 (`AlertsSettings.cs:13`), dedup-grouped | **No.** Severity-selected sinks would receive every quota and stall alert alongside; the grouping loses the one-message-per-question shape §2.4 needs; and CARD-0171 already rejected this route for the same reason (`TrackerSyncNotifier.cs:15-18`). |
| Targeted send | `ChatChannelService.SendAsync(id, text)` (`server/Application/Services/ChatChannelService.cs:87-103`) — no alert row, no throttle, refuses a disabled channel (`:95-100`), produces a `ChannelReply` onto `channels.outbound` | the caller names the channel | plain text; the gateway renders markdown → Telegram HTML (`docs/telegram.md`) | **Yes.** Precedent: `TrackerSyncNotifier` (`server/Application/Services/TrackerSyncNotifier.cs:47-141`), opt-in per board, formatter capped at 3 500 (`TrackerSyncSummaryFormatter.MaxChars`, `:19`), failures returned as reasons never exceptions. |

Two facts about the send surface that shape §2.4/§3:

- `SendAsync` exposes only `text`. `ChannelReply` also carries `ReplyToMessageId` (honoured by the
  Telegram adapter at `src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs:659`) and
  `RawOverrides` (merged into `sendMessage` at `:661`; `disable_notification` is the documented
  use, `docs/telegram.md:80`). A **silent** send and a **quoted confirmation** both need an
  overload that passes those through.
- **The server never learns the Telegram `message_id` of anything it sent.** The adapter reads it
  (`SendResult.Sent(sentId)`, `TelegramChannelAdapter.cs:511-513`, `:607`) and it stays in the
  gateway; there is no receipt topic (`src/Antiphon.Messaging/` has no ack contract). So no design
  here may rely on "the message we sent" as a key — see §2.4 for why the reply-quote *excerpt* is
  the correlation instead.

Also: `docs/antiphon-api.md:203` still lists `POST /api/channels/{id}/send`; `server/Api/Endpoints/ChannelEndpoints.cs:29`
says it deliberately does not exist (CARD-0171). Fix the doc line in S5.

### 1.5 Telegram inbound: everything routes to a bound agent or nowhere

`ChannelBridgeService.HandleInboundAsync` (`server/Application/Services/ChannelBridgeService.cs:94-149`):
upsert the channel row (`:100-105`), drop Kafka duplicates (`:110-115`), then **`if (!channel.Enabled
|| channel.AgentId is not Guid agentId) return;`** (`:117`) — an unbound channel's message is
recorded and ignored; a bound one is debounced into the agent's session as a prompt (`:140-149`,
`FlushLaneAsync` `:156`). There is no server-side interpretation of inbound text at all (the
`AgentMentionRouter` is agent-output → agent, unrelated).

What inbound *does* carry: `ChannelMessage.ReplyTo` (`src/Antiphon.Messaging/ChannelMessage.cs:35`,
`ReplyReference { ChannelMessageId, Excerpt }` `:114-118`), populated by the Telegram adapter from
`reply_to_message` with **the first 160 chars of the quoted message's text** (`TelegramChannelAdapter.cs:392-401`).
The adapter ingests `message`, `edited_message`, `channel_post`, `edited_channel_post` only
(`:254`) — **no `callback_query`**, so inline-keyboard buttons would need adapter, contract and
server work, and would still lack the sent-id the callback must reference (§1.4).

### 1.6 The CARD-0027/0037 ceilings — real, and irrelevant to a server-composed digest

`PtyDeliveryProfile` (`server/Application/Services/PtyDeliveryProfile.cs`) resolves the ceilings
per backend: inbox conhost **900 B brief / 3 000 chars reply / 1 024 B tripwire**; modern **43 200 B
/ 14 400 chars / 86 400 B**, all single-write (AGENTS.md, CARD-0037 steps 3-4). This deployment
runs `modern`, but the inbox numbers are still what a machine without the redistributable gets.
The ceilings bind **anything typed into a pty**. The digest is never typed; the human's *answer* is,
and it goes through `SessionMessageQueueService` like every other delegate reply, with the same
oversize incident and spill path — no new size rule is needed for it.

### 1.7 Cost: tracked in one place, summed in three, surfaced in six — none of them a push

- **Stored:** `AgentTask.CostUsd` (`server/Domain/Entities/AgentTask.cs:130`) with `TokensIn`,
  `CacheReadTokens`, `CacheCreationTokens`, `TokensOut` (`:121-129`) and `CostPricingVersion`
  (`:138`; 0 = pre-CARD-0023 figure, ~10x high). Priced by `DelegationCost.Estimate`
  (`server/Application/Services/DelegationCost.cs:43`, `PricingVersion = 2`, `:31`) at settlement
  (`AgentTaskReplyService.cs:410`), re-priced by `DelegationCostBackfillService`.
- **Per root:** `SUM(CostUsd) WHERE RootTaskId == root` — the dispatcher's budget gate
  (`AgentTaskDispatcher.cs:2150-2153`, `RootIsOverBudgetAsync`) and the create-time refusal
  (`AgentTaskService.cs:210-214`), both against `DelegationSettings.MaxCostUsdPerRoot` = **$50**
  (`server/Application/Settings/DelegationSettings.cs:40`). Subtree cost per task: `AgentTaskService.cs:950-956`,
  `AttentionService.cs:866-890`, `CardThreadService.cs:282-286` (three copies of one walk).
- **Surfaced today:** (1) the delegate completion note's header — `DelegationReportFormatter.BuildCompletionNote`
  appends `$0.000` to `[task 1a2b3c4d done] title · tier · duration · $cost` (`DelegationReportFormatter.cs:196-202`)
  — this is the "inside a delegate report nobody reads" spot the card names; (2) the check
  interpreter's own line (`AgentTaskCheckService.cs:456`); (3) `AgentTaskSummaryDto.CostUsd` /
  `SubtreeCostUsd` (`server/Application/Dtos/AgentTaskDtos.cs:106`, `:114`) on the delegations
  board and `TaskDrawer`; (4) `AttentionItemDto.SubtreeCostUsd`; (5) the card thread
  (`CardThreadDtos.cs:78-79`); (6) mobile band 3's `settledSpendUsd` (§1.1).
- **Adjacent and worth one line:** `SubscriptionUsageSample.RemainingPercent` / `ResetsAt`
  (`server/Domain/Entities/SubscriptionUsageSample.cs:25-28`), read by `SubscriptionUsageReader.GetLatestAsync`
  (`server/Application/Services/SubscriptionUsageReader.cs:22`). Present only when
  `SubscriptionUsageMonitoring` is on and a sample exists (CARD-0143); Claude never has one.

### 1.8 What a window can be computed from (all timestamps already stored)

| Fact | Column | Written at |
|---|---|---|
| task settled (Succeeded/Failed/Canceled) | `AgentTask.CompletedAt` (`AgentTask.cs:85`) | settlement / failure paths |
| task became Blocked, and why | `AgentTaskEvent{Type: Blocked \| Conflicted, At, Detail}` (`AgentTask.cs:149-156`; `AgentTaskEventType` `AgentTaskEnums.cs:92-130`) | `AgentTaskReplyService.cs:429-430`, `:981-982`; dispatcher `BlockAsync` |
| task failed, reason | `AgentTaskEvent{Failed}` + `AgentTask.FailureReason` | `AgentTaskReplyService.cs:614`, `:688`, `:993`; `AgentTaskDispatcher.cs:2216` |
| card started / done | `Card.StartedAt` / `CompletedAt` (`Card.cs:24-25`) | `CardService.cs:844`, `CardLifecycleTransitions.cs:46` |
| card moved column (incl. → Review / Blocked) | `CardRevision{Kind: Move, FromStatus, ToStatus, CreatedAt}` (`CardRevision.cs:52-55`, `:81`) | `CardRevisionLog.cs:39` |
| incidents / alerts | `AgentIncident.CreatedAt`, `Alert.CreatedAt` | many |

Retention comfortably exceeds any away window: tasks 180 days, sessions 90, queued messages and
transcripts 30 (`server/Application/Settings/RetentionSettings.cs:11-17`).

### 1.9 What does not exist (the build list, before design)

No server-side notion of "when you last looked"; no per-channel digest opt-in; no formatter for a
human-facing delta; no scheduled sender; no push on Blocked; no inbound branch that can turn a
Telegram message into a task answer; no `GET` that returns the away delta as JSON (the client
computes it from three list endpoints).

---

## 2. Design decisions — the card's four questions, answered

### 2.1 Pushed or pulled? Both — and the push is the *summary*, the pull is the *detail*

Pull is shipped and good (§1.1); it answers "what got done?" with report sentences, links, plans.
The push exists because the pull requires *opening the app*, and the card's person is on a phone,
hours away, possibly deciding whether to open anything at all. So the push is **the triage layer**:
enough to decide *nothing needs me* or *this one thing does*, with the detail one tap away.

Consequently the push is **short by design** (§2.3), carries **no report bodies** (one sentence
per settled root, the same `firstSentence` rule as band 3), and its "Needs you" section is the
only part meant to be *acted on* from the chat.

### 2.2 Trigger: a state change wakes you; the clock catches you up

The spec (§D4) named the one candidate worth waking a phone for: Critical attention — a blocked
question, a parked channel reply. Parked channel replies already reach the ops alert sinks as
Critical (`AgentIncidentKind.ChannelReplyLost`, CARD-0067). That leaves **Blocked**, which today
reaches nobody (§1.3), and which S3 calls "the single highest-value interaction in the system — a
blocked delegate is burning nothing and delivering nothing until answered."

**Wake set = {task became Blocked}.** All three causes (question, cost ceiling, conflict) — each
is "stopped until a human acts". Not in the wake set, deliberately:

- *Failed*: a failure with retries left is machinery; a terminal one needs a decision, but not
  within minutes — it belongs in the next digest, under its own heading, with the reason.
- *Succeeded / card Done*: news, not a summons. Digest.
- *Card → Review*: work waiting on a laptop reader; nothing a phone can do. Digest.
- *Warning-level attention* (never-started, overdue, stalled): CARD-0153 made these detection-only
  and they self-resolve or become Blocked/Failed. Digest, counted.

**One ping per block, idempotent.** The sweep (§3.4) pings a task whose latest `Blocked`/`Conflicted`
event has no later `HumanNotified` event (new `AgentTaskEventType.HumanNotified = 17`, appended —
the enum's own rule at `AttentionDtos.cs:8` applies to `AgentTaskEventType` too). A task answered
on the web before the sweep runs (status no longer Blocked) is never pinged. A task that blocks
twice is pinged twice — that is two questions.

**Digest cadence: fixed local times, not an interval, not presence.** There is no operator
presence signal to key off (§1.9), and an interval drifts to 03:00. Default `["08:00", "18:00"]`
in `Digest:TimeZone` (default `Europe/London`) — start of day and end of day are when "since I
left" is a real question. `/digest` in the chat (§2.4) and `POST /api/digest/send` are the
on-demand pulls for the hours in between.

**The window is server-side, per channel:** `ChatChannel.DigestLastSentAt`. Window =
`(DigestLastSentAt, now]`; first ever = last 24 h, and the heading says so (band 3's own rule,
`awayDelta.ts:17-18`). Stamped **after** a successful produce, so a broker failure re-covers the
window next time (a duplicate line is readable; a lost one is not — CARD-0067's ordering
argument, applied in the harmless direction).

**Silence is a state.** A scheduled digest with an empty delta sends **one line** if anything is
running (`Quiet since 08:00 · 2 running · nothing needs you`) — the spec's "supervised, not dead"
— and **nothing** if the fleet is idle and nothing changed. A ping is never silent (`disable_notification`
false); a digest always is (true).

### 2.3 How much fits: one message, five rows a section, roots only

- Telegram: 4 096 chars after entity parsing (`docs/telegram.md:86`); the bridge already trims
  agent replies at 4 000 (`ChannelBridgeSettings.MaxReplyChars`, `:22`); the tracker formatter
  caps at 3 500 to leave room for its own ellipsis. **Hard cap 3 500**, same constant convention.
- A phone shows roughly 10–14 short lines before scrolling. **Target ≤ 1 500 chars**: heading,
  up to 5 rows in each of 3 sections, a running line, a spend line.
- **Roots only.** A root's children are its implementation detail; a settled child of a still-open
  root is not news, and a root's settlement subsumes its children. Cost is therefore the **root's
  subtree cost** at settlement — exactly what `MaxCostUsdPerRoot` gates, so the number in the
  message is the number the budget is measured in. (This differs from band 3's per-task own-cost
  sum on purpose; §3.1 says how both stay honest.)
- **Folds, never silent drops:** `+ N more` after five rows per section (the tracker formatter's
  `MaxIdentifiersPerLine` shape). Row text is one line: identifier · verb · first sentence, the
  `settledTaskLine` rule from `MobileHomePage.tsx:407-412`.
- **Check tasks are excluded** (`role == Check`, the band-3 rule at `awayDelta.ts:75-78`) and so
  is every Info/Warning alert — those have the sink path.

Reading order (the S1 urgency order, collapsed):

```
While you were away · since 08:00 (10h)

❗ Needs you (1)
• 1a2b3c4d CARD-0210 delegate boards — asked: "Should the Merge role also…"  ← reply to the ping, or start a message with 1a2b3c4d:

✗ Failed (1)
• CARD-0118 fixup — merge-back failed: conflict in AgentTaskReplyService.cs  ($4.21)

✓ Finished (3)
• CARD-0210 stop minting delegate boards — landed on master, 14 tests green  ($2.88)
• CARD-0181 stale sidecar claim — plan doc written  ($1.10)
• CARD-0136 quota gate — verified on live  ($0.62)

▶ Running (2) — longest 3h12m (CARD-0036 plan)

Spend: $8.81 on work that settled · biggest root $4.21 · Codex 71% left, resets Fri
```

**Deliberately not shown:** report bodies; child tasks; check-in readings; incidents below Error
(and Error/Critical ones go via the alert sinks already — a digest that repeats them is the
"Antiphon alerts:" voice twice); plans changed (laptop news); per-session state; links per row
(one optional footer link to the mobile home when `Digest:PublicBaseUrl` is set).

### 2.4 Actions from the digest: answer by replying; everything else is one tap away

**Answering a blocked delegate — the loop the card asked to close.** The ping for a block is its
own Telegram message whose **first line is the key**:

```
❓ task 1a2b3c4d needs an answer — CARD-0210 delegate boards
Should the Merge role also inherit the caller's DenyDirectEdits, or only Code? (blocked 14:02, $1.37 so far)
```

The human taps *Reply* on it and types the answer. Inbound, the bridge sees `ReplyTo.Excerpt` =
the first 160 chars of *that* message (§1.5), which by construction contains `task 1a2b3c4d`; the
new branch parses the short id, calls `AgentTaskReplyService.AnswerAsync` — the same call `POST
/api/agent-tasks/{id}/reply` makes — and sends a quoted confirmation (`ReplyToMessageId` = the
inbound `ChannelMessageId`, which *is* known): `Answered task 1a2b3c4d — it will resume and report
back.` A refusal (`not waiting for an answer`, `session no longer available`, ambiguous/unknown
short id) is echoed back verbatim from the existing exception messages; nothing is retried.

Why the excerpt and not a button or a sent-id: the server never learns sent ids (§1.4), the
adapter never ingests `callback_query` (§1.5), and the contract has no button concept — three
changes across two repos for something the reply-quote gives for free. A reply to the *digest*
(rather than a ping) is ambiguous when two tasks are blocked, so the digest's Needs-you rows carry
the short id and the typed fallback is documented in the row itself: a message beginning
`<8hex>:` or `/reply <8hex> ` is an answer to that task. Each ping being its own message is also
what makes the ping *loud* and the digest *quiet* without two channels.

**`/digest`** — send the digest now, to this channel, window since its last stamp (and re-stamp).
The one pull verb, for the hours between the scheduled sends.

**Not in v1:** `/cancel`, `/retry`, `/escalate`. They are lower-frequency, each is destructive or
expensive, and each is a one-tap action in band 1 of the mobile home (`MobileHomePage.tsx:210`).
The ping's optional footer link lands there. Add them later as the same `/<verb> <8hex>` shape
if lived-with use asks for it.

**Safety of the inbound branch (the one shared-code risk in this design):** it runs *only* on
channels with `DigestEnabled`, *only* on the two exact shapes (a reply whose excerpt matches
`task <8hex>`, or text matching `^/?(reply\s+)?[0-9a-f]{8}:`), and *before* the bound-agent gate —
then `return`s, so a recognised answer is never also typed into a bound agent. Any other text on
a digest-enabled channel falls through unchanged. Recommended deployment is Mike's DM
(`8738110514`), which is not bound to an agent; a family group should not be a digest channel at all.

---

## 3. Concrete design — what to build

### 3.1 `AwayDigestProjection` — the delta as a server-side JSON projection

`server/Application/Services/AwayDigestProjection.cs`, scoped, read-only, every query
`AsNoTracking` (the `AttentionService`/`CardThreadService` doc-comment contract).

`ComputeAsync(DateTime sinceUtc, DateTime nowUtc, CancellationToken)` → `AwayDigestDto`:

| Section | Source | Rule |
|---|---|---|
| `NeedsYou` | `AttentionService.GetAsync` rows with `Kind == BlockedQuestion` | reuse, never re-derive; carry `ShortId`, title, first sentence of `Evidence`, `SinceUtc`, `SubtreeCostUsd`, and `IsNew = SinceUtc > sinceUtc` (new since the window vs still waiting) |
| `Failed` | `AgentTasks` where `ParentTaskId == null && Status == Failed && CompletedAt ∈ window`, `Role != Check` | reason = first line of `FailureReason`; cost = subtree |
| `Finished` | same predicate with `Status ∈ {Succeeded, Canceled}` + `Cards` where `CompletedAt ∈ window` | first sentence of `Result` (`firstSentence` rule, ported); cards show `CARD-nnnn title — done` |
| `Review` | `CardRevisions` `Kind == Move && ToStatus == Review && CreatedAt ∈ window` (`CardRevisionLog.cs:39`) | count + up to 5 identifiers; a card moved *out* of Review inside the window is dropped |
| `Running` | `AgentTasks` `ParentTaskId == null && Status ∈ {Dispatched, Working}` | count, the longest-running root (title, elapsed since `DispatchedAt`) |
| `Spend` | `SettledSpendUsd` = Σ subtree cost of every root in `Failed ∪ Finished`; `BiggestRoot`; `RootsOverHalfBudget` (subtree ≥ 50 % of `MaxCostUsdPerRoot` among running roots) | subtree via one shared helper — extract the walk that `AgentTaskService.cs:950-956` / `AttentionService.cs:866-890` / `CardThreadService.cs:282-286` each own into `AgentTaskCostWalk` and call it from all four (a fourth copy is how numbers drift) |
| `Subscription` | `SubscriptionUsageReader.GetLatestAsync` | one line per provider with a fresh sample; absent otherwise |

Also `FirstWindow: bool` (no stamp → 24 h) and `SinceUtc`/`UntilUtc` echoed. Endpoint
`GET /api/digest?since=<iso>&until=<iso>` returns it (route-map entry next to `/api/attention`,
`docs/antiphon-api.md:266`). Band 3 keeps its client-side calculation for now; switching it to
this endpoint is a one-line follow-up once the numbers are seen to agree, and is listed under
§6 rather than done here — the two spend definitions (own-cost-of-settled-tasks vs
subtree-of-settled-roots) must be reconciled in the UI copy first, not silently.

### 3.2 `AwayDigestFormatter` — pure, capped, tested on strings

`server/Application/Services/AwayDigestFormatter.cs`, `static`, model `TrackerSyncSummaryFormatter`.
`FormatDigest(AwayDigestDto, DigestSettings, DateTimeOffset localNow)` → string; `FormatPing(AttentionItemDto blocked)`
→ string; `FormatQuiet(...)`. Constants: `MaxChars = 3500`, `RowsPerSection = 5`, `SentenceChars = 140`.
Markdown that the gateway renders (`docs/telegram.md`): bold section heads, plain bullets, no
tables, no links unless `PublicBaseUrl` set. The ping's first line is the contract §2.4 depends on
— pin it with a test that parses it back with the same regex the inbound branch uses.

### 3.3 Channel opt-in, settings, silent sends

- Migration: `ChatChannel.DigestEnabled bool default false`, `ChatChannel.DigestLastSentAt timestamptz null`
  (`server/Domain/Entities/ChatChannel.cs`, `AppDbContext`). `UpdateChatChannelRequest` gains
  `bool? DigestEnabled` (`server/Application/Dtos/ChatChannelDtos.cs:27-34`); `ChatChannelDto`
  exposes both. `ChannelsPage.tsx` gets a switch beside the alert-severity `Select` (`:140-150`).
- `DigestSettings` (`server/Application/Settings/DigestSettings.cs`, section `Digest`):
  `Enabled` (default **false** — the `ChannelBridge.Enabled` / `LogTap.Enabled` ship-off convention),
  `SendTimesLocal` `["08:00","18:00"]`, `TimeZone` `"Europe/London"`, `WakeOnBlocked` `true`,
  `SweepSeconds` `60`, `MaxChars` `3500`, `RowsPerSection` `5`, `PublicBaseUrl` `null`.
- `ChatChannelService.SendAsync(Guid id, string text, ChannelSendOptions? options, CancellationToken)`
  overload — `Silent` → `RawOverrides {"disable_notification": true}`, `ReplyToMessageId`. The
  existing two-arg method delegates to it; `TrackerSyncNotifier` is untouched.

### 3.4 `AwayDigestHostedService` — one timer, two jobs

`server/Infrastructure/Supervision/AwayDigestHostedService.cs`, model `AlertDigestFlushHostedService`
(`:27-71`): returns at once when `Digest:Enabled` is false (log once, Information); otherwise a
`PeriodicTimer(SweepSeconds)`; every tick, in a fresh scope:

1. **Wake sweep** (`BlockedTaskNotifier.SweepAsync`): for each digest-enabled channel, tasks with
   `Status == Blocked` whose latest `Blocked|Conflicted` event is newer than their latest
   `HumanNotified` event → `FormatPing` → `SendAsync(loud)` → write `HumanNotified` event with
   `Detail = "<channelId>"`. Send-then-record; a throw records nothing and the next tick retries
   (a duplicate ping is a real question asked twice, acceptable; a lost one is the failure the
   card is about).
2. **Schedule check** (`AwayDigestNotifier.SendDueAsync`): a scheduled time is *due* for a channel
   when the local clock has passed it and `DigestLastSentAt` is earlier than that local time
   today. Compute → format (or `FormatQuiet`/nothing per §2.2) → `SendAsync(silent)` → stamp.

`POST /api/digest/send` `{ channelId?: guid, since?: iso }` runs step 2 for one channel (or all
digest-enabled ones) immediately, and is what `scripts/digest.ps1 [-Channel <id|title>] [-Since <iso>]`
calls (same shape as `scripts/github-sync.ps1`; exits 1 on a `send_failed`). Register alongside
`TrackerSyncNotifier` (`server/Program.cs:432`) and the hosted service next to `:456`.

### 3.5 The inbound branch — `DigestReplyHandler`

Insert in `ChannelBridgeService.HandleInboundAsync` after the duplicate check (`:110-115`) and
before the bound-agent gate (`:117`):

```
if (channel.DigestEnabled && await _digestReplies.TryHandleAsync(channel, message, ct))
    return;
```

`TryHandleAsync` recognises exactly: (a) `message.ReplyTo?.Excerpt` matching `task ([0-9a-f]{8})`
→ answer = `message.Text`; (b) `message.Text` matching `^/?(reply\s+)?([0-9a-f]{8}):\s*(.+)$`
(single-line or multi-line) → answer = group 3; (c) `^/digest\b` → `SendDueAsync(channel, force)`.
Anything else → `false`. (a)/(b) call `AgentTaskService.ResolveTaskIdAsync` then
`AgentTaskReplyService.AnswerAsync`; success and every typed exception reply through
`SendAsync(options: ReplyToMessageId = message.ChannelMessageId)`. The handler never throws to
the consume loop (the bridge's own rule at `:140-144` — a broken flush is dropped-with-alert;
here a failed confirmation is a Warning log, because the *answer* has already been enqueued and
the task's status change is visible in the UI).

### 3.6 Docs

`docs/antiphon-api.md` route map (`GET /api/digest`, `POST /api/digest/send`, the new PATCH
field; **delete the stale `:203` line**); `docs/telegram.md` — a "Digest channel" subsection
(what the two message shapes are, how a reply is matched, the `disable_notification` use);
one AGENTS.md gotcha bullet: *the digest is composed server-side and never typed; the only typed
thing is a human answer, through the normal queue* — so nobody later "improves" it into a prompt.

---

## 4. Failure and empty states

| State | Behaviour |
|---|---|
| `Digest:Enabled` false (default) | hosted service logs once and returns; endpoints still work (manual send is allowed — it is a human asking) |
| enabled, no channel has `DigestEnabled` | tick does nothing; `POST /api/digest/send` returns `{ sent: false, reason: "no_digest_channel" }` |
| broker/gateway down | `SendAsync` throws → Warning log, **no stamp, no `HumanNotified` event** → next tick/send re-covers; no alert is raised (the alert path needs the same broker) |
| channel disabled | `ConflictException("channel_disabled")` → reason on the response, Warning log, skipped (never a side door — `ChatChannelService.cs:95-100`) |
| nothing changed, something running | one quiet line; stamped |
| nothing changed, nothing running | no message; **still stamped** (so the next window starts here, not at the last real digest) |
| first ever digest for a channel | window = 24 h, heading says `last 24h` |
| a blocked task answered before the sweep | not Blocked any more → no ping |
| reply to a ping whose task already resumed | `AnswerAsync` conflict text echoed: "…is not waiting for an answer." |
| reply to the *digest* message (excerpt has no `task <8hex>`) | not matched → falls through; on an unbound channel that means ignored — so the digest's Needs-you rows say how to answer (§2.3) |
| short id ambiguous / unknown | the existing `ResolveTaskIdAsync` messages echoed back |
| delta too long after folds | `MaxChars` truncation with an ellipsis, never a second message (the tracker formatter's rule) |
| `TimeZone` invalid | fail at startup validation with the id in the message (an operator typo must not silently become UTC) |

---

## 5. What this costs the surfaces it shares screen with

- **The Telegram DM** gains two quiet messages a day plus one loud one per block. On a busy day
  (several blocks) the DM becomes a queue of questions — which is what S3 wants it to be; the
  scheduled digest lists what is *still* waiting so nothing is lost to scroll.
- **The alert sinks** are untouched, and must stay so: a Blocked ping is not an alert row, so it
  does not appear in `GET /api/alerts` or the ops digest. If the operator later wants it there,
  that is one `RaiseAsync` in `BlockedTaskNotifier`, not a redesign.
- **`ChannelBridgeService.HandleInboundAsync`** grows one guarded branch (§3.5). The cost is the
  possibility that a message on a digest-enabled, agent-bound channel that happens to match
  `^[0-9a-f]{8}:` is consumed as an answer. The mitigation is the gate plus the recommendation
  that the digest channel is the DM, not a bound chat; the tests pin that an unrecognised message
  on a digest-enabled bound channel still reaches the agent.
- **Channels page** grows one switch per row.
- **Mobile band 3 and the desktop home** are untouched. The one thing this design asks of band 3
  later is to agree on the spend definition (§3.1) — not part of this card.
- **Three cost-walk copies become one helper** (§3.1) — a small refactor in `AgentTaskService`,
  `AttentionService`, `CardThreadService`, pinned by their existing tests.

---

## 6. Slices, tiers, tests

Each slice leaves the app shippable; S1 has no migration and no behaviour change.

| Slice | Contents | Tests (in `tests/Antiphon.Tests/Application/`) | Tier |
|---|---|---|---|
| **S1** projection + formatter + `GET /api/digest` | §3.1, §3.2, the cost-walk helper | `AwayDigestProjectionTests` (window edges inclusive/exclusive, roots only, Check excluded, Review moved-out dropped, first-window flag — every assertion scoped to rows the test made, per the shared-Postgres rule); `AwayDigestFormatterTests` (cap, folds, ping first-line round-trips through the inbound regex, quiet/empty shapes) | Codex terra |
| **S2** opt-in + schedule + send | §3.3, §3.4 step 2, `POST /api/digest/send`, `scripts/digest.ps1`, Channels toggle | `AwayDigestNotifierTests` modelled on `TrackerSyncNotifierTests.cs:27-241` (sends to enabled channels only, disabled → reason, throwing producer → no stamp, due-time arithmetic across a DST boundary with `TimeProvider`, silent override present) | Codex terra |
| **S3** Blocked pings | §3.4 step 1, `HumanNotified` event | `BlockedTaskNotifierTests` (one ping per block, re-block re-pings, answered-before-sweep no ping, throw → no event) | Codex terra |
| **S4** inbound answers + `/digest` | §3.5 | `ChannelBridgeTests` additions (`A_reply_to_a_ping_answers_the_task_and_never_reaches_the_agent`, `An_unrecognised_message_on_a_digest_channel_still_routes_to_the_bound_agent`, `A_typed_short_id_prefix_answers_the_task`, `A_stale_reply_echoes_the_conflict`); `AgentTaskReplyIntegrationTests` unchanged | Grok/Codex |
| **S5** docs + AGENTS.md bullet + `antiphon-api.md:203` fix | §3.6 | — | Codex luna |
| **Verify** | live: enable on the DM, `scripts/digest.ps1`, block a delegate on a throwaway task, reply from the phone, confirm the `Replied` event and the queue row | — | Codex luna |

Order S1 → S2 → S3 → S4; S3 and S4 are independent of each other after S2.

---

## 7. Decisions that are the operator's, not mine

1. **Which chat is the digest channel** — recommended Mike's DM (`8738110514`); a group makes the
   inbound branch's ambiguity real.
2. **Send times / timezone** — defaults `08:00`, `18:00` Europe/London; are two enough, and is
   there a weekend rule?
3. **Should a *terminal* root failure also wake?** Argued no (§2.2); flip `WakeOnFailed` in if
   lived-with use says otherwise — it is the same sweep with one more predicate.
4. **`PublicBaseUrl`** for a footer deep link to the mobile home — needs the proxy hostname
   (`antiphon.desktop.codeperf.net` via the `proxy` skill, or nothing).
5. **Where the card wants the doc**: the card text asks for `docs/features/`; the brief asked for
   `docs/superpowers/plans/`. This is the latter; moving it to `docs/features/012-away-digest/proposal.md`
   is a rename.
