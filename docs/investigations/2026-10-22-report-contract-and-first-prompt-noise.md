# Closing-report and first-prompt noise, 2026-10-22

Investigator: Debug task `fc28850a-1fdf-4df1-a3d0-479c3ac52494`.
Source checkout: `a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49`.
Observed server `/api/version`: `ceb6e3d6b78471db6831258699c493b8eb2d619b`.
Fixed census interval: **2026-10-21 08:42:00Z inclusive to 2026-10-22 08:42:00Z exclusive**.

## Findings

The closing-token parser did not reject the three named completed reports. All three contain the correct task-specific `done` token on the last nonempty line, after the work's last tool call. Their task events explicitly record `(verdict: done)`.

There are two different triggers for the warning. Eight of the nine affected tasks had a complete report blocked by runner synchronization, were reopened by an “accepted, stop” Reply, and then answered with a plain acknowledgement without a closing token. The ninth, `ca843435`, acquired a phantom `end_turn` boundary on a response that continued calling tools. Its nudge stayed queued and reached the child after its real report had settled. This is an attribution/lifecycle problem, not evidence that a completed report needs a more permissive token parser.

There is also measured report loss: three of the reopened Code tasks eventually answered with only the `done` token. The server settled those tasks with an empty `Result`, null `NextStage`, and null `DeliverablePath`, replacing the earlier substantive report. The transcript still retains the original report.

Neither named first brief was lost. Both sessions have exactly one stored `UserPrompt` containing the entire original goal and reporting contract, followed by relevant assistant work. The warnings reflect an unverified delivery at the confirmation deadline. Their permanent-sounding “nothing can verify it” wording is wrong once later evidence exists. A screen-only verdict remains a real delivery risk, so suppressing every cold-start warning would hide failures.

Four distinct Backlog cards were filed, each with an assertion-red witness, bounded scope and its own two-row closed checkpoint list:

| Card | Scope | Code budget |
|---|---|---:|
| CARD-1472 | Preserve a retained report across a blocked Reply; restore the complete-report contract on that wire; refuse destructive token-only completion | 45–60 min |
| CARD-1473 | Reject a known sibling phantom report boundary using stored response identity and tool evidence | 45–60 min |
| CARD-1474 | Cancel obsolete, never-typed closing-line nudges and parent waiting notes when a valid report arrives | 45–60 min |
| CARD-1475 | Make first-prompt unverified-delivery diagnostics temporal and reconcile the owner documentation with the actual confirmation loop | 30 min |

CARD-1475 deliberately changes diagnostics, not delivery semantics or incident severity. Durable receipt-linked incident resolution would need a separate design. No claim is made that a wording change eliminates the underlying observation delay.

## Census and method

The Antiphon board is `8988ca03-7414-47ad-b0b6-51556c701703`; its project is `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.

1. Enumerated `GET /api/agent-tasks?boardId=<board>&since=2026-10-21T08:42:00Z`: 256 task summaries. The scope envelope explicitly excluded ten additional Antiphon-project rows and four rows from another project.
2. Expanded to `GET /api/agent-tasks?projectId=<project>&since=2026-10-21T08:42:00Z`: 266 summaries. This endpoint has no paging/limit parameter; its documented `since` trims settled rows by `CompletedAt` and retains all nonsettled rows.
3. Filtered Code, Review, Investigate and Plan: 250 summaries. Fetched every one's full task detail and events, then filtered event timestamps to the fixed interval. One Review, `7f19217d`, was created at 08:42:15Z and has no event in the interval; it is excluded from the 249-row event-active denominator.
4. Counted Warning events whose detail starts exactly `Turn ended without the closing report line`. There were **9 events on 9 distinct tasks**, one per task. Retrieved all nine complete retained normalized transcripts and inspected the warning-owning response and preceding reports.
5. Retrieved the two specified first-prompt sessions separately. `9b664084` belongs to a Test task, outside the four-role census; it was found in the project-scoped list rather than guessed from a partial board list.

| Role | Tasks with an event in interval | Dispatched in interval | Warning events / affected tasks |
|---|---:|---:|---:|
| Code | 107 | 102 | 7 / 7 |
| Review | 99 | 98 | 0 / 0 |
| Investigate | 20 | 19 | 1 / 1 |
| Plan | 23 | 21 | 1 / 1 |
| Total | 249 | 240 | 9 / 9 |

This is 3.61% of event-active tasks, or 3.75% of tasks dispatched in the window; all nine affected tasks were dispatched within it. By provider, the event-active denominator is 202 ClaudeCode tasks (4 affected) and 47 Grok tasks (5 affected). There were no Codex rows in this cohort. The brief's “Codex Debug” names the investigator, not the failing provider.

These are retained task/event counts, not a fleet-wide incident count or a count of terminal redraws. One warning event is not proof of one physical delivery: `08f282dd`'s recorded nudge prompt #222 contains the nudge text twice, while its event ledger has one warning. The historical queue rows are gone, so this investigation does not assign a transport-retry cause to that duplicate text. It does not support a claim of repeated server warning creation for the same task within this window.

### All affected tasks

All nine original substantive reports end in a valid `done` token. None of the warning-owning acknowledgements ends in `blocked`; the Blocked task statuses below were imposed by sync checks, not by a child asking a question.

| Task | Role/provider | Warning UTC | Trigger before warning | Original marked report sequence | Current outcome at capture |
|---|---|---|---|---:|---|
| b92ba604 | Code/Grok | Oct 21 12:23:37.666080 | lease-busy block, Reply, “CARD-1397 repair is accepted. Stopping here.” | 109 | Succeeded, empty result after token-only #121 |
| 08f282dd | Code/Grok | Oct 21 14:51:39.460603 | lease-busy block, Reply, “Accepted. Stopping here.” | 208 | Succeeded, empty result after token-only #228 |
| 7cf73fd6 | Code/Grok | Oct 21 14:55:26.534852 | lease-busy block, Reply, “Accepted. Stopping here.” | 127 | Succeeded, marked report |
| 7d4b009e | Code/Grok | Oct 21 15:20:54.415322 | lease-busy block, Reply, “Accepted. Stopped.” | 122 | Succeeded, empty result after token-only #134 |
| db515bb4 | Code/Grok | Oct 21 15:25:51.615229 | lease-busy block, Reply, “Accepted. Stopped.” | 198 | Succeeded, marked report |
| 7c30c896 | Plan/ClaudeCode | Oct 22 02:06:11.404693 | identity-mismatch sync refusal, Reply, “Acknowledged. Stopping.” | 121 | Canceled; report remained sync-blocked |
| 53dfb959 | Code/ClaudeCode | Oct 22 06:56:43.480015 | lease-busy block, Reply, unmarked acknowledgement | 121 | Succeeded, marked report |
| 242906fa | Code/ClaudeCode | Oct 22 06:58:24.007281 | lease-busy block, Reply, unmarked acknowledgement | 96 | Succeeded, marked report |
| ca843435 | Investigate/ClaudeCode | Oct 22 07:05:37.012961 | sibling phantom boundary during tool work | 191, later | Succeeded before stale nudge delivery |

Grok rules-refresh turns occur after the original report in `08f282dd` and `db515bb4`; their tools are in separate housekeeping turns, not tools appended after a report token in the same report. The task events still accepted the original `done` reports. The later warning follows the caller's real Reply.

## Named report timelines

### 53dfb959

Session `2181467b-d556-4fa1-8826-184d9af4739d` (`ClaudeCode`, `claude-fable-5-1`).

- Last work ToolCall #119; final report #121 at **06:50:45.510Z**, ending exactly `[antiphon-report:53dfb959 done]`; TurnEnd #122.
- At **06:52:43.788787Z**, task event: `Delegate reported 3,523 characters ... (verdict: done).` Status became Blocked, with `runner_sync_lease_busy` and instructions to reply for a fresh report. This is positive evidence that parsing succeeded.
- Caller Reply event at **06:56:29.061988Z**; UserPrompt #123 at **06:56:39.779Z**: task marker plus `Accepted. The report is final. Stop now and do not take another turn.`
- Assistant #124 at **06:56:41.638Z**: `Acknowledged. The report is final and no further action is taken.` TurnEnd #125. There is genuinely no token in this new response.
- Warning at **06:56:43.480015Z**; nudge UserPrompt #126 at **06:56:54.394Z**.
- Full report #127 at **06:57:07.523Z**, ending in the correct token; #128 TurnEnd. Completed **07:01:49.121821Z** after desktop sync.

### 242906fa

Session `90099c4e-55eb-424e-b810-fb9b3d87ce57` (`ClaudeCode`).

- Last work ToolCall #94; report #96 at **06:55:09.092Z**, correct closing token; TurnEnd #97.
- At **06:58:05.410642Z**, `Delegate reported 5,098 characters ... (verdict: done).` Blocked on `runner_sync_lease_busy`.
- Caller Reply **06:58:19.714433Z**; UserPrompt #98 at **06:58:21.089Z**, same “Accepted ... Stop now” wording.
- Assistant #99 at **06:58:22.891Z**: `Acknowledged. The report stands as final; no further action from this session.` TurnEnd #100. No token.
- Warning **06:58:24.007281Z**; nudge #101 **06:58:25.532Z**; marked full report #102/#103 **06:58:47.795Z**.
- Completed **07:02:06.458649Z**. Neither report length nor footer position caused the original block.

### ca843435

Session `815ab306-1982-4751-b3a9-c5ff6e175af9` (`ClaudeCode`, `claude-fable-5-1`).

| Sequence | Native timestamp UTC | Kind | Relevant content |
|---:|---|---|---|
| 1 | 07:05:19.857 | UserPrompt | Marked brief pointer |
| 5 | 07:05:29.676 | TurnEnd | `stopReason=end_turn` |
| 6 | 07:05:29.683 | AssistantText | “Brief read. ... I'll gather ... in parallel.” |
| 7 | 07:05:29.683 | TurnEnd | `stopReason=end_turn` |
| 8, 9, 10, 12 | 07:05:30.997–07:05:36.404 | ToolCall | Card, Git and source inspection |
| 189 | before final report | ToolCall | Last work tool call |
| 191, 192 | 07:19:17.486 | AssistantText, TurnEnd | Full report, exact `[antiphon-report:ca843435 done]` |
| 193 | 07:19:33.136 | UserPrompt | Obsolete “Your turn ended without the closing report line” nudge |
| 194, 195 | 07:19:44.629 | AssistantText, TurnEnd | Full marked report repeated |

Rows #5–#12 share response identity **`msg_011CgGrJrWdLLmVkHc21dPpT`** where applicable. The warning event is **07:05:37.012961Z**, almost fourteen minutes before the actual report. The normalizer suppresses `end_turn` only when that individual record contains `tool_use`; separate thinking/text siblings still create boundaries. The exact raw thinking record was not read; the normalized identity/ordering and the existing normalizer guard establish the residual shape.

The task Completed event is **07:19:33.048617Z**, explicitly `(verdict: done)`, **87 ms before** the stale nudge's native UserPrompt timestamp. The final report was accepted. Its success did not retire the queued request for the missing report. Native timestamps and server event timestamps are different clocks, so the 87 ms is descriptive ordering, not a latency benchmark; both stored sequences and the repeated assistant report independently establish the extra turn.

## Code and contract attribution

Line references below refer to the source checkout named at the top.

| Owner | Evidence and implication |
|---|---|
| `server/Application/Services/DelegationReportFormatter.cs:71` | `TryReadReportVerdict` takes the last nonempty trimmed line, requires the literal lowercase prefix, matching eight-character task id, closing bracket and `done`, `blocked` or `failed`. Id/verdict are case-insensitive. Markdown fences, a trailing next-stage block, wrong id or extra trailing prose would fail. None explains the named reports. |
| `DelegationReportFormatter.cs:396`, `:529`; `server/Bundles/delegate-basics.md:77` | The initial contract puts the stage block above the report token and requires nothing after the token. `BuildBrief` includes it server-side; pointer briefs direct the child to the full inbox file. There is no conflicting footer order in these templates. |
| `server/Application/Services/AgentTaskReplyService.cs:465` | Reply to a Blocked task sets Working and `RepliedAtSequence`; the wire at :476 is only task marker plus the caller's text. It does not carry a fresh complete-report reminder. “Accepted/stop” is still a new model turn, not a server-side acceptance primitive. |
| `server/Application/Services/TaskReportTurnSelector.cs:156` | The reply watermark correctly excludes the old report as the new answer. Searching the entire transcript for any old `done` token would violate this ownership fence. |
| `AgentTaskReplyService.cs:1203`, `:1231`, `:1265` | A valid token with no body can classify Succeeded; settlement assigns the empty body to `Result` and reparses an empty handoff. This explains the three measured empty-result settlements. |
| `src/Antiphon.SessionRunner/TranscriptNormalizer.cs:146` | CARD-0282's `hasToolUse` suppression is record-local. A separate sibling text/thinking record can still emit TurnEnd for the same response. |
| `TaskReportTurnSelector.cs:91`; `AgentTaskReplyService.cs:3416`, `:3574`, `:3984` | Selection and final-text identity do not reject a boundary whose same response has tool siblings. Once same-response preamble exists, the missing-final-text grace does not help: it is text, just not a completed report. The unmarked branch checks session liveness, not proof that the selected response is actually finished. |
| `AgentTaskReplyService.cs:4151`, `:4251` | Nudge records a Warning and child queue id, and queues the parent waiting note immediately. The parent copy says “Session is idle” although this path only established liveness. |
| `AgentTaskReplyService.cs:4347` | The explicit cancellation of `ReportNudgeMessageId` is in `BlockUnmarkedWaitingAsync`; the marked-report settlement path does not cancel it. Queue `CancelAsync` at `SessionMessageQueueService.cs:1198` is conditional on Pending and takes the queue lock; attempted input needs stricter handling than deleting a row. |
| `server/Application/Services/SettlementSyncDebtPolicy.cs:43` | Code with absent/indeterminate progress evidence can still block even with a Pending sync result. This is an existing deliberate residual, not evidence of an invalid report token. |

A diff from observed server SHA to investigation HEAD showed no change in the report formatter, selector or normalizer. The reply-service difference was Git auto-maintenance configuration; queue-service differences were caller-attribution metrics. The relevant parsing, nudge and confirmation behavior is therefore present in both source revisions. This is source comparison, not a claim that every runner binary was independently fingerprinted.

`ReportNudgedAt` limits the ask, and Reply does not clear it. The nine task ledgers contain one warning each despite several having two Replies. Requeue/retry is a different lifecycle and clears nudge state in `AgentTaskService.cs:3824`. Repetition of a visible notice can therefore mean another task, an old queued note, or transcript duplication; it must not be diagnosed from the prose alone.

## First-prompt verification

| Session / task | Stored first UserPrompt | Brief completeness audit | Subsequent evidence |
|---|---|---|---|
| `9b664084-88e9-4fa7-af6c-135822972c84` / `c58e21f5-1164-4579-9a7c-7f84ffc44d3c` | #1, **05:46:37.086Z**, 5,059 characters | Entire 3,190-character goal present after whitespace normalization, task marker and full report contract present; exactly one UserPrompt | Assistant #2 at 05:46:39.761Z begins the requested monitor work; later tool activity. Task failed at 07:05:40Z because the process exited, not because the first brief was absent. |
| `a55de0c0-b8b0-41f8-b4ef-7adbc171e413` / `77c15d47-ea8c-4deb-999a-9c422d5e4743` | #1, **07:18:04.944Z**, 5,349 characters | Entire 3,230-character goal present after whitespace normalization, task marker and full report contract present; exactly one UserPrompt | Assistant #2 at 07:18:12.577Z starts the requested triage; full report and Completed at 07:33:52.621188Z. |

The operator supplied warning times **05:47:07Z** and **07:18:34Z** are approximately 30 seconds after the respective native prompt timestamps, consistent with the default confirmation window. Those incident times were not independently retrieved: both owning-agent `/incidents` GETs now return 404, and both historical session delivery queues return no messages and `lastDelivery=null`. The transcripts remain available. A native prompt timestamp does not reveal when the server ingested it, so it cannot prove the verifier had that row during the window.

### What the runtime actually does

- `SessionMessageQueueService.cs:3656`: an unobservable baseline means **zero stored transcript rows**, not necessarily a permanently unbound or brand-new session.
- `:4022–4177`: the queued first-brief path already tries transcript confirmation for the normal bounded window, pulls via `CatchUpTranscriptAsync` on the unobservable path, and checks identity plus whole-body completeness. The first prompt need not wait for a transcript to exist before being typed; for some providers that submit creates the file.
- `:4236`: the null-baseline matcher uses a non-null native timestamp at/after the attempt-time floor; old resumed history and null timestamps cannot confirm. The generic matcher also admits certain submitted-queue/question shapes; the evidence in these two cases is an actual complete `UserPrompt`, so no weaker shape is needed.
- `:4090–4152`: at the deadline, Claude can retain a degraded screen-advance verdict. Codex/Grok require stronger observed submission evidence and reject a body still visible in the composer under the documented conditions. These two named sessions were Claude, not Codex.
- `:3408`: that fallback records `DeliveryUnverified`, deduped per session for ten minutes, Warning ordinarily and Critical when channel-bound. It does not kill or retype. `ToReceipt` at :3388 says `ConfirmedBy=Screen`, `Degraded=true`.
- `AgentSessionRuntime.cs:917–936`: catch-up fetches runner transcript and persists it; noncancellation errors fall back to existing evidence. Without desktop bind/persist logs, this investigation cannot distinguish slow discovery, pull failure, persistence delay or late streaming as the exact reason those two windows missed.
- `SessionMessageQueueService.cs:2894` late-confirms eligible pending/interrupted messages; `:2757` separately reconciles retained delivered-spill obligations. Neither is a general rewrite of old incidents. `AgentIncident` is explicitly append-only, and `AgentService.cs:238` returns historical messages unchanged.

The opening “Sent only when UserPrompt exists” rule in `docs/session-runtime-invariants.md:1097` has an explicit zero-baseline/boot scope-out in the same paragraph. Its older “nothing to confirm against” wording should not be read as “never try after submit”: CARD-0164's implemented queued-message loop does try. Boot `VerifiedPromptSubmitter` and queued delegate delivery are distinct paths. Durable receipt claims must still use matching complete transcript evidence, even when the initial transport was allowed a degraded verdict.

**Risk verdict:** no first-brief loss in either named case (2/2 present and acted on). This is not proof of zero loss fleet-wide. The source deliberately permits a Claude redraw-based fallback without a receipt; `SessionMessageQueueDeliveryVerificationTests.A_session_with_no_transcript_entries_keeps_the_legacy_screen_only_verdict` even pins the swallowed-submit/no-record shape. Therefore the warning is valid uncertainty at its timestamp, and permanently suppressing it for fresh sessions would hide a real class of risk. Correct the temporal diagnostic, preserve evidence and do not replay an already-proven brief.

## Duplicate check, cards and handoff

All searches used `scripts/card.ps1 search '<phrase>' -Board Antiphon -All -Json`, which completed enumeration with `truncated=false` and no continuation. Phrases included `closing report` (0), `report marker` (2), `reporting contract` (5), `unmarked` (31), `TurnEnd` (66), `nudge` (35), `sibling thinking` (1), `pending nudge` (0), `empty report` (0), `token-only` (1 unrelated), `DeliveryUnverified` (3), `first prompt` (15), `unobservable` (11), and `runner_sync_lease_busy` (10). Relevant hits were fetched in full through the card script.

- **CARD-0282, Done:** same-record phantom fix explicitly accepted the sibling residual and said to file a follow-up if it recurred. CARD-1473 is that follow-up.
- **CARD-0551, Done:** older missing-structured-report/bind-refusal recovery. These named original reports did carry the closing token and were accepted, so it does not cover the measured follow-ups.
- **CARD-1082, Done; CARD-1109 and CARD-1133, Review; CARD-1043, InProgress:** existing sync-debt, unfetched-Code progress, disclosure and Review re-settlement ownership. No duplicate sync-redesign card was filed. Current rows have overwritten the first sync evidence, so the exact original progress-read inputs cannot be recovered from their present projections.
- **CARD-0180 and CARD-0201, Done:** introduced the generic fallback incident and corrected a fake timestamp assumption. They do not make later verification impossible. CARD-1475 is limited to diagnostic accuracy and documentation.

New cards are all on Antiphon, confirmed Backlog:

| Card | Durable id | Proposed test class | Closed ordinary runs |
|---|---|---|---|
| CARD-1472 | `882485f5-616f-43e8-a98c-b8d25d048691` | `BlockedReplyReportRetentionTests` | CP-1: five cases, two expected behavior reds; CP-2: all five green |
| CARD-1473 | `088ff7e0-6b4e-4ada-9792-0cf1c1646e5b` | `SiblingToolReportBoundaryTests` | CP-1: five cases, one expected behavior red; CP-2: all five green |
| CARD-1474 | `7a263c05-ef04-420c-ad66-c5eae6016343` | `ClosingReportNudgeLifecycleTests` | CP-1: five cases, one expected behavior red; CP-2: all five green |
| CARD-1475 | `57ae0060-5972-4f62-8a57-a4efcd7bdef1` | `DeliveryUnverifiedDiagnosticTests` | CP-1: three cases, one expected copy red; CP-2: all three green |

The exact filters, isolated build outputs, `Min` floors, named guard roster and time estimates are in each card's `### Checkpoints` table. They are proposed future tests, **not executed evidence**. Each Code task commits its witness first, runs that committed red group through the checkpoint tool, implements the fix, then runs its committed green group. No suite-wide or Windows verification is requested. The report/selector/queue cards touch overlapping source; dispatch them sequentially under normal scope admission.

Start with CARD-1472 because empty-result settlement loses the pipeline handoff. CARD-1474 removes the stale extra-turn mechanism, then CARD-1473 narrows the trigger; CARD-1475 is lower-priority diagnostic accuracy. Do not use “Accepted ... stop” as a server-side completion operation. For an existing genuinely Blocked sync task, resolve the underlying sync condition and request a complete marked report if Reply is required. A `done` report is not evidence that its sync/progress gates have passed.

## Verification and limits of this investigation

No source code, settings, runtime processes or Windows qualification files were changed. No builds or tests ran. The only repository deliverable is this document; board writes created the four requested Backlog cards. Read commands had bounded timeouts; API requests used 35-second timeouts and task-token headers without printing credentials. No private-notes route, provider auth file or Codex home was accessed.

Evidence consists of the normalized transcript sequences and native timestamps quoted above, task event timestamps/details, current result projections, complete card search results and read-only source comparison. Working JSON captures were kept outside Git under `/tmp/fc28850a-evidence`; they are temporary diagnostics, not a promised durable artifact. This document retains the actionable facts and stable API ids. Transcript retention is finite, so the caller should use these ids promptly if raw replay is needed.

Reproduce through the documented API: resolve the API base from `ANTIPHON_API`, send `ANTIPHON_TASK_TOKEN` in `X-Antiphon-Task-Token`, GET the scoped task list, GET each `/api/agent-tasks/<short-id>` and its `summary.agentSessionId`, then GET `/api/sessions/<full-id>/transcript?since=0`. Task detail is an envelope (`summary`, `events`, `result`), not a flat summary. Filter event time, not just task creation time; native transcript time is not persistence time. Never read provider homes to work around a missing incident route.

--- next stage ---
next: code
handoff: Implement CARD-1472 first: preserve retained report/artifact through a blocked Reply, require a complete marked report on that wire, and reject destructive token-only completion. Use its closed two-row checkpoint list on server2; then address CARD-1474, CARD-1473 and CARD-1475 sequentially.
artifact: docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md
