# CARD-0294 (remainder) — standing authority, one-action continue, structured blocked note, bound-chat escalation

**Date:** 2026-09-03

**Plan task:** c91616dd (Frontier, plan only; no production code changed by this pass)

**Card:** CARD-0294 — Orchestrator misses delegates that stop awaiting approval; surface and follow up promptly (GitHub #27)

**Prior work on this card, verified at `e2717498`:** plan `e7975ce0`
(`docs/superpowers/plans/2026-09-01-card-0294-unmarked-approval-wait-blocked.md`) and code `d7fafc80`
(on master; 13 files, 797 insertions; 4 + 2 + 6 + 5 test references to `UnmarkedWaiting`). The
incident sentence `Please approve this design and I'll begin the recorded TDD cycles.` is pinned in
`DelegationUnitTests`, `AgentTaskReplyIntegrationTests:187,220` and
`AgentTaskDeliveryWatchdogTests:787,830,968`.

**Related:** CARD-0256 (#22, empty Stopped Grok session — a crash class, `StoppedBeforeFirstPrompt`,
untouched here), CARD-0286 (`CompletedWithoutProgress` on a marked `done`), CARD-0033 (`AnswerAsync`,
`BlockedContextDto`), CARD-0338 (machine-turn plain text reaches the bound chat), CARD-0248 / CARD-0159
(nudge and settle-anyway contract), CARD-0233 (`ChannelReplyLost` notice).

---

## 1. Verdict up front

1. **`d7fafc80` closed the incident's exact shape and nothing else.** A nudged child that stays idle on
   the same unmarked boundary for 5 minutes now Blocks (`UnmarkedWaiting`), the parent gets a
   `[task … waiting]` note at T+0 and the `[task … blocked]` note at T+5, `BlockedQuestion` (Critical,
   `Reply`) shows in attention. That covers expected-behaviour **1** for the idle case and **5** for the
   nudged-idle case. It deliberately left **3** (prior authority) out of scope, did not touch **2**
   (the note is a header plus the child's prose — no reason, no proposed follow-up), and relies on
   CARD-0338 for **4** without a bound on what happens when the parent does not reply.
2. **Three holes remain against the acceptance tests, and this plan closes each with a slice.**
   - *Acceptance 3 — "resumed by the parent through a single explicit continuation action"*: there is
     no continuation action. The parent must compose a reply by hand, and the task carries nothing
     that records what the user already authorised. **S1** adds `StandingAuthority` on the task
     (`delegate.ps1 -Authority`), a `Continue` verb (`-Continue <id>`, `POST …/continue`) that replays
     it, and a `Continue` attention action.
   - *Acceptance 2 — "receives the request as a task event and can send a follow-up without reading
     terminal scrollback"*: the Blocked note has no `reason:` / `asks:` / `next:`; the drawer's
     `BlockedContextDto` cannot say *why* it is blocked or that a continue is available. **S2** adds
     four fixed lines above the body and three fields to the DTO.
   - *Acceptance 1 / 4 — "never terminal `done`" and "claims done with neither completion evidence nor
     a terminal blocker is detected and escalated"*: a second unmarked turn end after a delivered nudge
     still settles **Succeeded** (`UnmarkedAfterNudge`, `AgentTaskReplyService.cs:2224-2227`). A Code
     worktree child that answered the nudge with "Still waiting for your approval." and changed nothing
     is reported `done`. CARD-0286's zero-progress probe runs only on a marked `done`. **S5** applies
     that same narrow probe to the unmarked-after-nudge arm and Blocks instead of Succeeding.
3. **Expected-behaviour 3 is decided: prompt, never infer; auto-continue only on an explicit
   per-dispatch flag, at most once.** The harness cannot judge whether "start the remaining epics"
   matches "please approve this design" — CARD-0159 rejected that kind of text judgement and this plan
   does not reintroduce it. What the harness *can* do is carry the authority verbatim on the task,
   put it in the child's brief (so the child does not stop in the first place), name it in the Blocked
   note with the one command that replays it, and — when the dispatching parent said so explicitly
   with `-AutoContinue` — replay it once without a round trip. A second wait on the same task always
   Blocks for a human. **S3.**
4. **Expected-behaviour 4 gets a bounded server backstop.** Today the only path from a Blocked child to
   the user's chat is: WhenIdle note → parent turn → CARD-0338 follow-up. It fails silently when the
   parent is busy (note Pending), when the parent answers `NO_REPLY`, and when the parent session is
   dead. `BlockedTaskNotifier` does not help: it pages only `DigestEnabled` channels, and on this box
   `Digest.Enabled` is `false` (`appsettings.json:233`) with zero `digestEnabled` channels
   (`GET /api/channels`, checked this pass), so `AwayDigestHostedService` never runs. **S4** adds a
   5-minute sweep on `AgentSupervisorHostedService` that sends one server-composed notice to the
   parent's bound chat when no chat reply was published for the Blocked note.
5. **Not conflated with #22 / CARD-0256.** That class is a dead session with zero transcript rows,
   classified by `AgentTaskLiveness` and failed by the dead-session reconciler. Every slice here is
   gated on a live session with a report-boundary `TurnEnd`; `StoppedBeforeFirstPrompt`, the repeat
   guard and `AgentTaskLiveness` are not edited. S5 is explicitly not a second crash detector.

### Coverage matrix

| Card item | Shipped `d7fafc80` | This plan |
|---|---|---|
| EB1 child awaiting approval reports blocked, not done | idle ≥ 5 min after the nudge → Blocked | S5: second unmarked end on a zero-progress Code worktree → Blocked, not Succeeded |
| EB2 structured blocked event: request, reason, proposed follow-up | `Blocked` event + note with the child's prose | S2: `reason:` / `asks:` / `authority:` / `next:` lines; `BlockedContextDto.Reason`, `.Authority`, `.CanContinue` |
| EB3 prior authority → parent prompted to continue; auto only where explicit | out of scope | S1: authority on the task, `-Continue` one action; S3: `-AutoContinue`, once |
| EB4 channel parent surfaces the question promptly | CARD-0338 S1 delivers the parent's reply | S4: 5-minute notice to the bound chat when no reply was published |
| EB5 watchdog/SLA, bounded interval, transcript preserved | `UnmarkedWaitingMinutes` (5), session kept | S4 (Blocked with nobody told), S5 (done-claim without evidence, unmarked half); marked half is CARD-0286 |
| AT1 approval request → blocked, never done | idle case | S5 (Code/Worktree). Non-Code or Shared second unmarked end stays Succeeded `report=unmarked` by CARD-0159/0227 design — see §6 |
| AT2 parent receives request as a task event, follows up without scrollback | note exists | S2 |
| AT3 single explicit continuation with prior authorisation | — | S1 |
| AT4 done-claim with no evidence and no blocker → detected, escalated | — (CARD-0286 for marked) | S5 + S4 |
| AT5 Coesite E-03 regression | idle shape pinned | §7 row "Coesite E-03 end to end" |

---

## 2. Verified current-code facts (line refs at `e2717498`)

- **Settlement arms.** `ClassifyReportAsync` (`AgentTaskReplyService.cs:2157-2227`): Check → own
  classifier; `done` → `TryClassifyCompletedWithoutProgressAsync` (`:2096-2149`, Code + Worktree +
  probe available + 0 commits + 0 changes → `Failed`/`CompletedWithoutProgress`, fail-open otherwise)
  else Succeeded/Marked; `blocked` → Blocked/Marked; `failed`; `LooksLikeAQuestion` (`:2692`, last two
  non-empty lines end in `?`) → Blocked/`QuestionHeuristic`; live + never nudged → `NudgeForClosingLineAsync`
  (`:2285`) and null; nudged + same boundary → null; nudged + later boundary → **Succeeded**
  `UnmarkedAfterNudge` or `FinalMessageMissing` (`:2224-2227`).
- **The 5-minute Block.** `AgentTaskDispatcher.SettleDeferredReportsAsync` (`:1518-1552`) →
  `AgentTaskReplyService.BlockUnmarkedWaitingAsync` (`:2140-2226` in the diff; `Status = Blocked`,
  `ReportEvidence = UnmarkedWaiting`, `Result = body`, cancels the pending child nudge, `DeliverToParentAsync`
  with `git=` header, `PublishAsync`). Event detail: "Turn ended without `[antiphon-report:…]`; asked once
  and the session stayed idle. Waiting on a human."
- **The parent note.** `DeliverToParentAsync` (`:1485-1516`) → `DelegationReportFormatter.BuildCompletionNote`
  (`:358-396`): header `[task <id> blocked] title · alias · duration · $cost · … · report=unmarked · git=…`,
  optional `warning` between header and body (outside `FitReport`, so excerpting cannot remove it), then
  the body. Enqueued WhenIdle, `Origin = Delegation`, `ConversationKey = task:{RootTaskId:N}`,
  `SourceTaskId = task.Id`. The T+0 waiting note uses `task-wait:{Id:N}` (`:2110-2133`).
- **Answering.** `AnswerAsync(taskId, message, origin, round, ct)` (`:277-357`): Blocked → `Working`,
  `Replied` event with `BlockedQuestion.RepliedEventDetail(origin, round, message)`, WhenIdle enqueue of
  `TaskMarker + "\n\n" + message`; `round` is the count of Blocked/Conflicted events. `AnswerOrigin` is
  `Web = 0, Cli = 1, Channel = 2`. `POST /api/agent-tasks/{id}/reply` body `ReplyToAgentTaskRequest`
  (`message`, `round?`, `origin?`). `delegate.ps1 -Reply` (`scripts/delegate.ps1:286`).
- **Task row.** `AgentTask` has `Goal`, `Title`, `ParentSessionId`, `ParentTaskId`, `ReplyTo`,
  `ReportNudgedAt/Sequence/MessageId`, `ReportEvidence`, `FailureCode`, `Result` — no field records
  what the caller was authorised to do. `CreateAgentTaskRequest` (`AgentTaskDtos.cs`) ends at
  `AllowUnauthenticatedProvider`; nothing carries authority.
- **Blocked projection.** `BlockedContextDto(Kind, Round, BlockedAt, Question, Context, PriorRounds,
  Progress, CanAnswer, CannotAnswerReason, MergeTaskId)` (`AgentTaskDtos.cs:262-272`); `BlockedKind`
  is `Question, MergeConflict, CostCeiling, RoutingExhausted`. `BlockedContextBuilder.AttentionPrimary`
  (`:68-75`) prefers `FailureReason`, then `BlockedQuestion.TryExtract` (needs a `?`), then whole `Result`
  — so an UnmarkedWaiting row's "question" is the entire narration.
- **Attention.** `BuildBlockedAsync` (`AttentionService.cs:244-316`) emits `BlockedQuestion` Critical with
  actions `Reply, Cancel, Escalate`; `AttentionAction` enum is `Reply=0, Retry=1, Cancel=2, Escalate=3`.
  `UnmarkedWaiting = 23` is the newest `AttentionKind` (`AttentionDtos.cs:190-197`); **next free is 24**.
  `CallerNoteUndelivered` (`:1024-1095`) fires at `DeliveryFailTimeoutMinutes` (10), Warning, detection only.
- **Pager.** `AwayDigestHostedService` (`Infrastructure/Supervision/AwayDigestHostedService.cs:17`) returns
  immediately when `Digest.Enabled` is false; `BlockedTaskNotifier.SweepAsync` (`:26-58`) pages only
  `ChatChannels.DigestEnabled` rows, dedups on any `HumanNotified` event newer than the latest
  Blocked/Conflicted event, writes `HumanNotified` with `Detail = channelId.ToString("D")`.
- **Chat follow-up.** `ChannelReplyDispatcher.DispatchMachineTurnAttachmentsAsync` (`:996-1160`):
  candidates are Sent, unsettled rows with origin Delegation / Check / System / Scheduled (`:1046-1054`);
  `AdmitsMachineTurnText` (`:1152-1155`) admits `ChannelBridgeSettings.MachineTurnTextOrigins`
  (default Delegation, Check, …); exact `NO_REPLY` with no markers returns **before any claim** (`:1069-1071`);
  a published reply stamps the consumed rows `ChannelReplySettledAt` (`SettleAsync`, `:519-525`). So
  "this Blocked note produced a chat reply" is exactly `note.ChannelReplySettledAt != null`.
- **Supervisor cadence.** `AgentSupervisorHostedService` ticks every `TickSeconds` and runs
  one-minute sub-sweeps (`ChannelReplySweepPeriod`, `QueuedInputSweepPeriod`, `:23-37`). That is the
  home for a sweep that must run regardless of digest configuration.
- **In-turn questions.** `HasOpenQuestionToolAsync` (`:364-388`) recognises only
  `GrokQuestionTool.AskUserQuestionName`. Codex has no question tool in the normaliser; its approval
  waits are prose, which is why the incident child looked idle rather than "asking".
- **Live channels on this box** (checked): seven catalog rows, all `digestEnabled: false`. The Slack
  rows (`general`, DM `D0BRT8UJCPQ`) are bound to agent `Slack Test`.

---

## 3. Slices

### S1 — Standing authority on the task; `Continue` is one action

**Columns (EF migration `AddAgentTaskStandingAuthority`, precedent
`20260830210805_AddSessionTerminationAndTaskFailureCode`):**

- `AgentTask.StandingAuthority string?` — the caller's own words, trimmed, ≤ 2 000 chars (422 above).
- `AgentTask.AutoContinueOnWait bool` (default false) — S3's switch, added here so there is one migration.
- `AgentTask.AutoContinuedAt DateTime?` — S3's once-only stamp.

**Request:** `CreateAgentTaskRequest` gains `string? Authority = null, bool AutoContinue = false`.
`AutoContinue` without `Authority` is 422 `auto_continue_needs_authority`. `AgentTaskService.CreateAsync`
copies both to the row; the merge-child clone (`:1537-1569`) copies `StandingAuthority` and leaves
`AutoContinueOnWait` false (a conflict resolver has no approval wait to skip).

**`delegate.ps1`:** `-Authority <text>` and `-AuthorityFile <path>` on the create set (long text by
file, the `card.ps1 -DescriptionFile` convention — `delegate.ps1` has no file form for `-Goal` today,
so this is the first), `-AutoContinue` (switch, refuses without `-Authority` client-side too), and a
new parameter set `-Continue <taskId>` → `POST /api/agent-tasks/{id}/continue`.
Output on success: `Continued task <id> with its standing authority. It will resume and report back.`

**Brief.** When `StandingAuthority` is set, the composed brief carries, immediately before
`ReportingContract`:

```
--- standing authority from your caller ---
"<authority>"
Do not stop to ask for approval that this already grants. If you would otherwise pause for a
go-ahead on something it covers, proceed and say so in one line of your report. If what you
need is NOT covered, end with the blocked token and say exactly what is missing.
```

This is the cheapest fix in the card: the Coesite child would have read "start the remaining epics one
after another" in its own prompt.

**Verb.** `AgentTaskReplyService.ContinueWithAuthorityAsync(Guid taskId, AnswerOrigin origin, CancellationToken ct)`:

- 404 when missing; 409 `not_blocked` unless `Status == Blocked`; 409 `not_a_question` unless
  `ClassifyBlocked` says `BlockedKind.Question` (merge conflicts, cost ceiling and routing exhaustion are
  not answered by authority); 409 `no_authority` when `StandingAuthority` is null/blank.
- Otherwise calls `AnswerAsync(taskId, ContinueMessage(task), origin, round: null, ct)` where

  ```
  Continue. Your caller's standing authority: "<authority>". It covers what you asked; proceed
  without further approval. If it does not, say exactly what is missing and end with the blocked token.
  ```

  The `Replied` event detail is `BlockedQuestion.RepliedEventDetail(origin, round, message)` prefixed
  `continued with standing authority — `, so the timeline distinguishes a replayed authority from a typed answer.
- Route: `POST /api/agent-tasks/{id}/continue`, body `{ origin?: "Web"|"Cli" }` (default Web), returns
  `AgentTaskSummaryDto` like `/reply`. Documented next to `/reply` in `docs/antiphon-api.md:224`.

**Projection.** `BlockedContextDto` gains `string? Authority`, `bool CanContinue`
(`Kind == Question && CanAnswer && Authority != null`), `DateTime? AutoContinuedAt`. `AgentTaskDetailDto`
(or wherever `Goal` is exposed) exposes `standingAuthority` and `autoContinueOnWait`.

**Attention / client.** `AttentionAction.Continue = 4` (append; do not renumber). `BuildBlockedAsync`
puts `Continue` first in the action list when `CanContinue`; the row and `BlockedQuestionCard` render
"Continue with authority" above the reply box, showing the authority text in a quote. `client/src/api/attention.ts`
union gains `'Continue'`; `attentionVisuals.test.ts` totality picks it up; the drawer posts to `/continue`.

### S2 — The Blocked note and the Blocked context say why, what it asks, and what to do

For every Blocked-on-question writer — `SettleAsync` on Marked `blocked` and `QuestionHeuristic`,
`BlockUnmarkedWaitingAsync`, and S5 — `DeliverToParentAsync` passes a new `BlockedNoteBits` that
`BuildCompletionNote` renders **between the header and the body, in the `warning` slot's position**
(outside `FitReport`, so an excerpted body can never drop it):

```
[task 1234abcd blocked] E-03 downloader · terra · 12m03 · report=unmarked · git=no changes
reason: waiting-unmarked — ended a turn without the closing line; asked once; idle 5m
asks: Please approve this design and I'll begin the recorded TDD cycles.
authority: "start the remaining Coesite downloader epics one after another" (given at dispatch)
next: pwsh -File scripts/delegate.ps1 -Continue 1234abcd  — replays the authority as the answer
      or -Reply 1234abcd "<answer>" · or relay `asks:` to your chat now and end your turn

<report body>
```

Without authority the last two lines are:

```
authority: none given at dispatch
next: -Reply 1234abcd "<answer>" if you can answer it; otherwise put `asks:` in your reply to
      the chat now — do not answer this note with NO_REPLY
```

- `reason:` is a fixed vocabulary keyed on `ReportEvidence` — `marked-blocked` (Marked),
  `question-line` (QuestionHeuristic), `waiting-unmarked` (UnmarkedWaiting), `waiting-no-progress`
  (S5) — plus the one-clause gloss above. No free text, so a parent can grep it.
- `asks:` is `BlockedQuestion.TryExtract(Result)` when it finds a question; otherwise the **last
  non-empty line** of the report, capped at 240 chars. Positional, not NLP: `LooksLikeAQuestion` is not
  edited and no approval-phrase matcher is added (the prior plan's rejection stands).
- `BlockedContextBuilder` writes the same `asks:` into `BlockedContextDto.Question` for the
  `UnmarkedWaiting`/`waiting-no-progress` evidence classes (today it is the whole narration) and fills
  the new `Reason` (the vocabulary word). `AttentionPrimary` uses it too, so `FormatPing` and the
  attention row show the ask, not the first 400 chars of the report.
- The T+0 `[task … waiting]` note (`EnqueueParentWaitingNoteAsync`) gains one line when authority is
  set: `authority on file — `-Continue 1234abcd` becomes available if it Blocks; `-Refine` now if you
  want it typed sooner.`

The `Blocked` event detail is unchanged (it is already specific per writer); the note is the
parent-facing structured event, the DTO is the UI-facing one, `AgentTaskChanged` on the bus is the
trigger for both. No new bus event.

### S3 — Explicit auto-continue, once per task

Policy, decided: **off by default; on only for a task dispatched with `-AutoContinue` (which requires
`-Authority`); fires at most once per task; applies to `BlockedKind.Question` only; the second wait
Blocks normally.** The dispatching parent is the one who received the user's instruction and is the
one that pre-declares "any approval wait in this task is answered by this authority"; the harness
never decides that a wait *matches*.

At the point each question-writer would call `DeliverToParentAsync` with the Blocked note:

1. Write the `Blocked` event and set `Status = Blocked` exactly as today (auditable; `round` advances).
2. If `AutoContinueOnWait && StandingAuthority is not null && AutoContinuedAt is null`:
   - set `AutoContinuedAt = now`, call `ContinueWithAuthorityAsync(task.Id, AnswerOrigin.Authority, ct)`
     — new `AnswerOrigin.Authority = 3` — which flips the row to `Working` and enqueues the continue
     message on the child; `Replied` event detail prefixed `auto-continued once with standing authority — `.
   - **Do not** send the Blocked note. Send instead one WhenIdle parent note, `ConversationKey
     task-continue:{Id:N}`, body:

     ```
     [task 1234abcd continued] E-03 downloader · auto-continued once with the standing authority
     asks: Please approve this design and I'll begin the recorded TDD cycles.
     The next wait on this task will Block for you. Nothing to do now.
     ```
   - `PublishAsync`.
3. Otherwise (flag off, no authority, or already used) → the S2 Blocked note as today.

`BlockedTaskNotifier` and S4 never see the auto-continued row because it is `Working` again on the
same tick. Cancel-of-pending-child-nudge in `BlockUnmarkedWaitingAsync` still runs first.
`AutoContinuedAt` is shown in the drawer timeline and on `BlockedContextDto` so a second, real Block
reads "auto-continue was already used at hh:mm".

### S4 — A Blocked child reaches the bound chat within 5 minutes even if the parent does not reply

New `BlockedChildEscalationService.SweepAsync` called from `AgentSupervisorHostedService` on a
one-minute period (`BlockedEscalationSweepPeriod`, beside `:23-37`). Not `AwayDigestHostedService`
(inert when `Digest.Enabled` is false, which is this box's state). New setting
`DelegationSettings.BlockedParentNoticeMinutes = 5` (`<= 0` disarms).

Candidate row: `Status == Blocked`, `Role != Check`, `ClassifyBlocked == Question`, `ReplyTo == Session`,
`ParentSessionId` set, latest Blocked/Conflicted event older than `BlockedParentNoticeMinutes`.

Skip when any of:

- the Blocked parent note row (Origin Delegation, `SourceTaskId == task.Id`, `ConversationKey ==
  task:{RootTaskId:N}`, created at/after the Blocked event) has `ChannelReplySettledAt != null` — a chat
  reply was published from the parent turn that consumed it. Pending, Sent-without-reply, and
  `NO_REPLY` (which returns before claim) all **fire**;
- a `HumanNotified` event newer than the latest Blocked event has `Detail == "<targetChannelId>"` (same
  detail shape `BlockedTaskNotifier` writes, so the two never double-send to one channel and neither
  suppresses the other's channel);
- the parent is not channel-bound: no `Enabled` `ChatChannel` with `AgentId == parent session's AgentId`.
  Then the existing `BlockedQuestion` attention row (and `BlockedTaskNotifier` where digests exist) is
  the surface; nothing new.

Target channel: the catalog row named by the parent session's newest Channel-origin queued message
(`ConversationKey` `{provider}:{externalId}`, the same resolution `DispatchMachineTurnAttachmentsAsync`
uses at `:1085-1096`); if there is none, every enabled channel bound to that agent.

Send via `ChatChannelService.SendAsync(channelId, text, new ChannelSendOptions { ReplyHandle = channel.ReplyHandle }, ct)`:

```
❓ Delegate 1234abcd needs an answer — E-03 downloader
Please approve this design and I'll begin the recorded TDD cycles.
Blocked 5m, no reply published. Tell <parent agent name> here what to do, or answer in Antiphon (Attention → Reply / Continue).
```

Then `HumanNotified` event with `Detail = channelId.ToString("D")`. One Warning log line; no
`AgentIncident` (the Blocked row is already the record, and CARD-0338 §4 kept "no new alert sink").
Parent session dead or Stopped: the predicate does not read parent liveness, so the notice still goes
— the user hears that the child is waiting even though the orchestrator cannot relay it.

### S5 — Second unmarked end on a zero-progress Code worktree Blocks instead of Succeeding

In `ClassifyReportAsync`, at the settle-anyway return (`:2224-2227`), when the evidence would be
`UnmarkedAfterNudge` (not `FinalMessageMissing`), run exactly CARD-0286's predicate
(`TryClassifyCompletedWithoutProgressAsync`'s gates: `Role == Code`, `Workspace == Worktree`,
`DispatchedAt`, `WorktreePath`, probe resolvable, `arm.Available`, no commit, no file change; fail-open
on any probe failure). On zero progress return `(Blocked, UnmarkedNoProgress, body, null)` with new
`AgentTaskReportEvidence.UnmarkedNoProgress = 7`. `SettleAsync` then keeps the session and worktree
(its Blocked branch already does), writes the `Blocked` event with detail
"Ended a second turn without `[antiphon-report:…]` after the nudge, and the worktree shows no
post-dispatch commit or change. Waiting on a human — not settled as done.", and S2's note carries
`reason: waiting-no-progress`. `AnswerAsync` / `-Continue` resume it.

Why this is consistent with the prior plan's "no git gate": that plan refused git for the **first**
unmarked end (the nudge is the right first move) and refused it as a *settlement-to-Succeeded* gate.
CARD-0286 then accepted the narrow Code+Worktree probe for a **marked** done. S5 applies the identical
narrow probe to the only remaining path that can turn an approval wait into a terminal `done`, and it
Blocks (recoverable, session kept) rather than Fails. Non-Code roles, Shared checkouts (unattributable,
CARD-0227) and unavailable git are unchanged: still Succeeded `report=unmarked`, which the orchestrator
bundle already says to read as unverified.

### S6 — Guidance and docs

- `server/Bundles/orchestrator.md`: replace "When a delegate asks a question, answer it with -Reply" with
  a short paragraph: a `[task … blocked]` note carries `reason:` / `asks:` / `next:`; if `authority:` names
  something, `-Continue <id>` is the one action; otherwise `-Reply` if you can answer, else put `asks:`
  in your chat reply now — never `NO_REPLY` a blocked note. Dispatch with `-Authority "<the user's own
  words>"` whenever the user has pre-approved a sequence, and `-AutoContinue` only when they said to
  run without check-ins.
- `.claude/skills/antiphon-delegate/SKILL.md:248-260`: the `-Authority` / `-AutoContinue` / `-Continue`
  rows in the parameter table and the "asks a question comes back blocked" bullet.
- `docs/orchestration-loop.md`: one paragraph under the Blocked/answer material naming S1–S5 and the
  5-minute escalation; `docs/antiphon-api.md:224`: the `/continue` route and the new
  `CreateAgentTaskRequest` fields; `docs/session-runtime-invariants.md` only if it lists settlement arms
  (add `UnmarkedNoProgress`).

---

## 4. What this plan does not do

- No approval-phrase or "looks like waiting" classifier; `LooksLikeAQuestion` is not edited (negative
  pin stays).
- No inference that an authority *matches* an ask. Auto-continue is a per-dispatch declaration, once.
- No change to Herdr status handling (CARD-0286 invariant), `AgentTaskLiveness`, the dead-session
  reconciler, `StoppedBeforeFirstPrompt`, or the CARD-0256 repeat guard.
- No new hosted service, no new `AgentIncidentKind`, no new `AttentionKind` (BlockedQuestion already
  covers the state; S1 adds an *action*, not a kind). S4 rides `AgentSupervisorHostedService`.
- No git gate on the first unmarked end, on non-Code roles, or on Shared checkouts.
- No change to the WhenIdle delivery of notes to the parent (a Now-mode interrupt of a working
  orchestrator was rejected by CARD-0250/0338 and is not reopened).

---

## 5. Enum and setting numbers (live source of truth is the file, re-read at code time)

| Enum / setting | Live last value | This plan takes |
|---|---|---|
| `AgentTaskReportEvidence` | `UnmarkedWaiting = 6` | `UnmarkedNoProgress = 7` |
| `AnswerOrigin` | `Channel = 2` | `Authority = 3` |
| `AttentionAction` | `Escalate = 3` | `Continue = 4` |
| `AttentionKind` | `UnmarkedWaiting = 23` | none |
| `AgentTaskEventType` | `LandedWithResidue = 24` | none (`Replied` with origin/prefix) |
| `DelegationSettings` | `UnmarkedWaitingMinutes = 5` | `BlockedParentNoticeMinutes = 5` |

---

## 6. Known residue, stated so it is not mistaken for a gap

- A Plan/Review/Debug/Docs child, or any Shared-checkout child, that answers the nudge with unmarked
  prose still settles Succeeded `report=unmarked`. That is CARD-0159's decision (git is not
  attributable there and clean trees are legitimate). The orchestrator bundle already reads
  `report=unmarked` as unverified; S2's `asks:` convention does not apply to Succeeded notes.
- `HasOpenQuestionToolAsync` still recognises only Claude's question tool. Adding Codex's
  `request_user_input` (if its JSONL ever emits one) is a normaliser card, not this one.
- S4 notifies the bound chat; it does not parse chat replies into `AnswerAsync`. The user's reply goes
  to the orchestrator as an ordinary inbound message, and the orchestrator runs `-Continue` / `-Reply`.
  `AnswerOrigin.Channel` already exists for a future direct path.

---

## 7. Test matrix

Existing pins that must stay green: everything in `d7fafc80` (`AgentTaskDeliveryWatchdogTests`
unmarked-waiting block, `AgentTaskReplyIntegrationTests` :182-259, `AttentionServiceTests`
UnmarkedWaiting rows, `DelegationUnitTests` negative `LooksLikeAQuestion` pin), CARD-0286
`CompletedWithoutProgress`, CARD-0248 same-boundary/undelivered-nudge, CARD-0302 Check exemptions,
CARD-0338 `ChannelMachineTurnTextTests` / `ChannelFollowUpAttachmentTests` machine-turn text.

| Layer | Test |
|---|---|
| Application (`AgentTaskServiceIntegrationTests`) | **S1 create:** `Authority` lands on the row trimmed; > 2 000 chars is 422; `AutoContinue` without `Authority` is 422; merge child copies authority, not the flag; the composed brief contains the `--- standing authority ---` block only when set. |
| Application (`AgentTaskReplyIntegrationTests`) | **S1 continue:** Blocked (QuestionHeuristic) + authority → `/continue` flips to Working, enqueues marker + continue message quoting the authority, `Replied` detail starts `continued with standing authority`. 409 on Dispatched, on `MergeConflict`, and with no authority (message names `-Reply`). |
| Application | **S2 note shape:** for each of Marked `blocked`, QuestionHeuristic, UnmarkedWaiting, UnmarkedNoProgress the parent note has `reason:` with the right word, `asks:` equal to the extracted question or last non-empty line (240-char cap), `authority:` line, `next:` naming `-Continue` only when authority exists; the four lines survive a body that `FitReport` excerpts. `BlockedContextDto.Reason/Authority/CanContinue` match. |
| Application | **S2 waiting note:** T+0 note carries the authority line only when authority is set. |
| Application | **S3 once:** `-AutoContinue` + authority, child Blocks (each of the three writers) → row is Working on the same call, `AutoContinuedAt` set, `Replied` origin `Authority`, no `[task … blocked]` note, one `[task … continued]` note with key `task-continue:`; a second Block on the same task sends the normal Blocked note and does not continue. Flag without authority never reaches the row (422). Merge-conflict Blocked never auto-continues. |
| Application (`BlockedChildEscalationServiceTests`, new) | **S4 fires:** Blocked question, parent channel-bound, Blocked note Pending, +5 min → one `SendAsync` to the resolved channel with the `asks:` text, `HumanNotified` detail = channel id; second sweep sends nothing. **S4 respects a published reply:** note `ChannelReplySettledAt` set → nothing. **S4 fires on NO_REPLY:** note Sent, `ChannelReplySettledAt` null → fires. **S4 not bound:** no enabled channel for the parent agent → nothing. **S4 clock:** at 4m59 nothing; `BlockedParentNoticeMinutes = 0` disarms. **S4 dead parent:** parent session Stopped → still fires. **S4 + BlockedTaskNotifier:** a `HumanNotified` for a *different* channel does not suppress. Shared-Postgres: seeded ids only. |
| Application (`AgentTaskDeliveryWatchdogTests`) | **S5 blocks:** Code + Worktree, delivered nudge, later unmarked boundary, probe reports 0 commits / 0 changes → `Blocked`, `UnmarkedNoProgress`, session Running, worktree kept, S2 note `reason: waiting-no-progress`; `AnswerAsync` resumes. **S5 does not steal:** same shape with one changed file → Succeeded `UnmarkedAfterNudge` (existing test `a_later_unmarked_boundary_still_settles_unmarked_after_nudge_not_this_arm` gains the probe stub); Plan role, Shared workspace, probe unavailable, `FinalMessageMissing` → unchanged. |
| Application | **Coesite E-03 end to end (AT5):** dispatch Code/Worktree/terra with `-Authority "start the remaining Coesite downloader epics one after another"`; child's marked-prompt turn ends with the incident sentence, no token; assert T+0 waiting note with the authority line; advance 5 min, sweep → Blocked `UnmarkedWaiting`, note has `reason: waiting-unmarked`, `asks:` = the sentence, `authority:` quoted, `next:` names `-Continue`; `BlockedQuestion` attention action list starts with `Continue`; `/continue` → Working with the authority replayed. Variant with `-AutoContinue`: no Blocked note, `[task … continued]` note, Working; child Blocks again → normal Blocked note. |
| Unit (`DelegationUnitTests`) | `LooksLikeAQuestion(incident sentence)` still false (unchanged pin). `asks:` extraction: trailing `?` → question; no `?` → last non-empty line; 240-char cap. |
| Client vitest | `attentionVisuals.test.ts` / action rendering picks up `'Continue'`; `BlockedQuestionCard.test.tsx` renders the authority quote and posts to `/continue`. |
| Script | `delegate.ps1 -AutoContinue` without `-Authority` exits non-zero with the message before any HTTP call; `-Continue <id>` posts to `/continue` and prints the success line. |

Run per `docs/testing-and-build.md`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0294b/ -- --treenode-filter "/*/Antiphon.Tests.Application/*/*"
pwsh -File scripts/test-client.ps1
```

`Antiphon.Agents.Pty.Tests` and `Antiphon.SessionRunner.Tests` are not touched. Delete every
`bin-card0294b` directory afterwards. The migration must be applied on live Postgres before deploy
(CARD-0256's close note is the precedent for confirming it).

---

## 8. Sequencing and risks

**PR 1 = S1 + S2 + S6** (one migration, the verb, the note, the docs). **PR 2 = S3** (small; depends on
S1's columns). **PR 3 = S4** (independent of S1–S3 but reads S2's `asks:` helper — land after PR 1).
**PR 4 = S5** (independent; smallest diff; can go first if the implementer prefers). Each PR keeps
`d7fafc80`'s tests green unchanged.

| Risk | Standing |
|---|---|
| Auto-continue replays an authority that does not cover the ask | Bounded: once per task, the child re-Blocks with the same question, the parent answers. Cost is one child turn. Flag is off by default and refused without authority. |
| `asks:` last-line heuristic picks a bad line | It is positional, capped, and shown beside the full body; it never decides status. Marked/`?` questions use the existing extractor. |
| S4 double-pages with `BlockedTaskNotifier` | Dedup is per channel id on `HumanNotified.Detail`; the two target different channel sets by construction. |
| S4 pages for a Blocked child the orchestrator is already typing an answer to | A published reply stamps `ChannelReplySettledAt` and suppresses it; a reply still being typed at +5 min costs one duplicate line in chat, which is the right side to err on for this card. |
| S5 false Block on a Code worktree that legitimately changed nothing | Only after a delivered nudge *and* a second unmarked end *and* zero progress; a marked `done` on the same tree is already `CompletedWithoutProgress` (CARD-0286). Session kept; `-Reply "continue"` or `-Continue` is the recovery. Fail-open on probe error. |
| Migration lands beside another concurrent one | Same shape as CARD-0256/0260; rebase and re-check the model snapshot before merge. |
| Implementer takes a stale enum number | §5 is a snapshot; the file is the source of truth. |
