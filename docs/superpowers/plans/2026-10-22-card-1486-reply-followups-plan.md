# CARD-1486 plan: CARD-1472 reply follow-ups

Card: CARD-1486 (`42cd4202-184a-464e-b5ea-146cc6dd15c5`, Low/Normal). Plan task `1cc0a524`.
Base: `b0c0f89b8e928a18d3a437194a13fde58a339602` (master, contains CARD-1472 `c1a0153a2` and
CARD-1474 `25e8908dd`). Source: Final Review `062e5688` advisories on CARD-1472; investigation
`docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md`.
Every line reference below is `server/Application/Services/AgentTaskReplyService.cs` at base unless named.

## Ground truth

| # | Card assumes | Code at base | Verdict |
|---|---|---|---|
| G-1 | (item 1) A body made only of non-token closing lines replaces `Result`/`NextStage`. | `IsTokenOnlyAfterRetainedReport` (:4182) needs `IsNullOrWhiteSpace(body)`. `[antiphon-finding:x clean]` + token leaves a non-empty body, so the `done` arm (:4002) settles Succeeded with `Result` = the finding line. `PipelineHandoff.TryParse` then nulls `NextStage`/`NextHandoff`, and `ResolveDeliverableAsync` nulls the deliverable (:1269-1287). The same holds for a runner task's `[antiphon-progress:<id> commit=…]` line, which the runner contract asks for (`DelegationReportFormatter.cs:227`). | Confirmed. |
| G-2 | (item 1) An unmarked acknowledgement after the nudge replaces the report. | Unmarked arm (:4026-4058). Reply never clears `ReportNudgedAt` (T-4 of CARD-1472). So a reopened task that was already asked goes straight to settle-anyway on its first unmarked turn. Otherwise one ask goes out and the second unmarked turn settles anyway. Either way the result is Succeeded/`UnmarkedAfterNudge` with `Result` = the acknowledgement and the handoff and deliverable recomputed from it. A trailing `?` Blocks it as `QuestionHeuristic` with the same replacement. | Confirmed. The `?` arm stays out of scope (D-6). |
| G-3 | (item 2) A token-only ask that fails to enqueue leaves the task Working with no automatic deadline, and the unmarked arm had the same gap after a Reply. | A deadline does exist. The dispatcher sweep (`AgentTaskDispatcher.cs:4813`) reaches `BlockUnmarkedWaitingAsync` after `UnmarkedWaitingMinutes` (default 5) from `ReportNudgedAt`, whether or not the ask was delivered. That method returns early (:4444-4452) when any `AssistantText` in the whole session holds a valid token for this task. Every reopened task already has its original marked report in the transcript, and a token-only boundary is itself a token, so it never Blocks. If it did Block, it would overwrite `Result` with `turn.Report ?? ""` (:4472). | Confirmed, with a refinement: the deadline exists but cannot reach a reopened task, and it does not preserve the retained report. |
| G-4 | (not named) | CARD-1474 `RetireObsoleteClosingNudgeAsync` runs on every settlement (:1575), including the sync-block Blocked settlement. It cancels a never-typed nudge, which then has `SentAt` = null, and leaves `ReportNudgedAt`/`ReportNudgeMessageId` set. `BlockUnmarkedWaitingAsync` also cancels the nudge (:4482). After a Reply, `NudgeStillOutstandingAsync` (:4210) sees no `SentAt` and returns true on every later unmarked or bare-`done` boundary, so no new ask goes out and nothing settles. The sweep requires `boundary == ReportNudgedSequence`, so it never fires. The task stays Working with no release. | New defect; a CARD-1474 interaction on master only. It is the most severe shape here. |
| G-5 | (item 3) The contract text mentions `blocked`, which Distill and Diagnose briefs forbid. | `BlockedReplyContract(taskId)` is role-blind. No report classifier Blocks a specialist. A Blocked specialist row comes only from non-report writers, for example `SessionMessageQueueService.DispatchBrief.cs:241`. If such a row is replied to and answers `blocked`, `ClassifyCheck/Distill/DiagnoseReport` settles it Succeeded/Exempt (:4078-4127). | Confirmed but cosmetic: the outcome is benign. Drop it (D-7). |
| G-6 | (item 3) The contract does not repeat the Review finding-marker line. | The dispatch closing adds the finding-marker line when `task.Stage != null` (`DelegationReportFormatter.cs:946`); `BlockedReplyContract` omits it. A stage outcome is recorded once per stage task (`RecordDelegateStageOutcomeAsync`, :5222-5233), except in the Review repair arm. There a report without the line records `Unreported` and can revoke an earlier Clean finding. | Confirmed. The impact is small but real for a reopened Review. |
| G-7 | (item 4) There are no tests for the second-bare-token Block arm or the bare blocked/failed arms. | `BlockedReplyReportRetentionTests` has 5 methods, and none reaches :3990-3997. | Confirmed. These are guards only; they are green at base. |
| G-8 | (not named) | Requeue (`AgentTaskService.cs:3840-3866`) clears `RepliedAt*` and the nudge columns but not `Result`. So `Result` alone does not show that a turn was reopened. | It constrains D-3. |
| G-9 | (rc) | `origin/rc/2026-10-20` (`ceb6e3d6b`) has neither CARD-1472 nor CARD-1474. | See RC below. |

## Decisions

- **D-1 Fix G-4 first (S1).** In `AnswerAsync`'s Blocked arm, when the recorded ask was never typed, clear the three nudge columns. "Never typed" means `ReportNudgeMessageId` is null, or its row is missing or has `SentAt` = null. This re-arms the one ask for the reopened turn. A typed ask keeps the bound (one delivered ask per task, T-4). It is the same session, so the CARD-0349 cross-session reasoning for keeping the tuple does not apply. Rejected: (a) treating a Canceled row as answered in `NudgeStillOutstandingAsync`, which settles anyway with no ask ever delivered; (b) clearing on every Reply, which removes the bound CARD-1472 relies on.
- **D-2 Closing-lines-only is token-only (S4).** In `IsTokenOnlyAfterRetainedReport`, a body counts as empty when every non-blank line is this task's own harness marker: `[antiphon-finding:<short> …]` (the `TryReadFindingLine` prefix) or `[antiphon-progress:<taskId:D> commit=…]`. A new helper in `DelegationReportFormatter` does the test. No English is parsed, and another task's marker is not a closing line. Rejected: treating a `found` detail as new content. One line is not a report, and the nudge asks for the complete one anyway.
- **D-3 An unmarked turn after the ask on a reopened marked report Blocks with the retained report (S2).** Scope: `RepliedAtSequence != null` (attempt-scoped, so G-8 does not apply), `ReportEvidence == Marked` and a non-empty `Result`. Where the unmarked arm would settle anyway, it returns Blocked with the retained body. `SettleAsync` keeps every retained field, `ReportEvidence` included. The event and the caller warning give the unmarked turn's length and transcript sequence and say that the retained report stands. Rejected: (a) the normal path, which silently publishes Succeeded with an acknowledgement as the report; (b) parsing "Acknowledged". Risk accepted: a substantive unmarked report after a Reply to a marked `blocked` question also Blocks. That fails closed: the caller sees a Blocked task, a warning and the transcript. An `UnmarkedWaiting`/`QuestionHeuristic` retained row (not Marked) keeps today's behaviour.
- **D-4 Let the waiting deadline reach reopened tasks and keep the retained report (S3).** In `BlockUnmarkedWaitingAsync`, the token scan only counts entries with `Sequence > RepliedAtSequence`. A turn that is token-only or closing-lines-only after a retained report (D-2 predicate) does not count as a token. When D-3's retained-report predicate holds, the Block leaves `Result` and `ReportEvidence` unchanged and delivers the retained report to the parent. The dispatcher's sweep gate (`AgentTaskDispatcher.cs:4813`) is unchanged.
- **D-5 Role-aware contract: the finding line only (S5).** `BlockedReplyContract` takes the task's stage (or the task). When `Stage != null` it adds the dispatch closing's finding-marker sentence with the exact `found`/`clean` tokens. Everything else stays the same.
- **D-6 Scope.** The `LooksLikeAQuestion` arm, nudge wording, settings, the dispatcher, the land protocol, CARD-1474's retire step and the token parser are untouched. The Reply wire stays caller text first, contract after.
- **D-7 Drop:** specialist wording (G-5) and next-stage wording for non-stage roles. Both are cosmetic; the outcome is benign.

## Recommendation

| Slice | Item | Do? | Why |
|---|---|---|---|
| S1 | G-4 (item 2, the severe half) | **Do** | A reopened task can stay Working forever with no ask and no release. |
| S2 | item 1b | **Do** | It closes the measured acknowledgement shape (8 of 9 warnings) once the ask is spent. It fails closed. |
| S3 | item 2 | **Do** | It gives the enqueue-failure and untyped-ask cases a real deadline and stops the sweep wiping the report. |
| S4 | item 1a + item 4 | **Do** | The Review finding-line shape loses the report; the guards pin CARD-1472 arms. |
| S5 | item 3 (Review line) | Optional | It costs about 20 minutes and only matters for a reopened Review in the repair arm. Drop it if the budget is tight. |
| drop | item 3 (specialists) | **Drop** | Cosmetic (G-5). |

Order: S1 → S2 → S3, because S3 reuses S2's retained-report predicate and S2's guard relies on S1. S4 and S5 do not depend on S1-S3. All five edit `AgentTaskReplyService.cs` or the same test class, so one Code task runs them in sequence. Each slice is its own tests commit and fix commit, and each can land on its own in that order.

## Slices

All new tests live in `tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs` on the existing `BlockedBySyncAsync` fixture, unless a test names `AgentTaskReplyIntegrationTests`. The fixture is the CARD-1472 measured shape: a runner Code task Blocked on `runner_sync_lease_busy` with its marked report retained.

### S1 re-arm a never-typed ask on Reply (~45 min)

Fix: `AnswerAsync` Blocked arm (D-1).

| Test | Red at base | Pins |
|---|---|---|
| `Reply_after_a_retired_nudge_rearms_the_one_ask` | yes: no new Warning, `ReportNudgeMessageId` unchanged, silent Working | Seed the CARD-1474 state after the block: a Canceled delegation row with `SentAt` = null as `ReportNudgeMessageId`, plus `ReportNudgedAt`/`ReportNudgedSequence`. Then Reply and send a bare `done`. Expect a new token-only ask (a new Warning containing "retained report", a new Pending row, Working) and an unchanged retained report. |
| `AgentTaskReplyIntegrationTests.unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report` | yes: stuck Working after the Answer | The existing UnmarkedWaiting → Answer flow. The next unmarked turn gets one fresh ask. Mark it typed; a second unmarked turn settles Succeeded/`UnmarkedAfterNudge` with the new body. This also guards D-3's scope (the retained row is not Marked). |
| `Second_bare_done_after_a_typed_ask_blocks_with_retained_report` | no (guard, item 4) | Reply, bare `done` (asked), mark the ask typed, bare `done` again. Expect Blocked/Marked, all six retained fields unchanged, no second ask, no Completed event. |

### S2 an unmarked turn after the ask keeps a retained marked report (~45 min)

Fix: `ClassifyReportAsync` unmarked arm and `SettleAsync` retained-field guard (D-3). One predicate (`RetainedReportStands`) is shared with S3.

| Test | Red at base | Pins |
|---|---|---|
| `Unmarked_ack_after_the_ask_blocks_with_retained_report` | yes: Succeeded, `Result` = "Acknowledged…" | Reply, then "Acknowledged. Stopping." (unmarked; asked). Mark the ask typed, then "Acknowledged, nothing changed." (unmarked). Expect Blocked, retained `Result`/`NextStage`/`NextHandoff`/`DeliverablePath`/`DeliverableRef`/`ResultFilePath`/`ReportEvidence`, and a warning naming the unmarked turn. |

Guard: S1's `unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report`, which is included in CP-4.

### S3 the waiting deadline reaches reopened tasks (~45 min)

Fix: `BlockUnmarkedWaitingAsync` (D-4).

| Test | Red at base | Pins |
|---|---|---|
| `Untyped_token_only_ask_blocks_after_waiting_minutes_with_retained_report` | yes: stays Working | Reply, bare `done`. The ask is recorded and stays Pending and untyped. Rewind `ReportNudgedAt` by 6 minutes and call `BlockUnmarkedWaitingAsync`. Expect Blocked, the retained fields unchanged, the nudge Canceled and the parent note carrying the retained report. |
| `Untyped_unmarked_ask_after_reply_blocks_with_retained_report` | yes: stays Working (the original token is in the transcript) | The same with an unmarked acknowledgement. |

Guard: the existing `AgentTaskReplyIntegrationTests.block_unmarked_waiting_then_answer_resumes_the_same_session`.

### S4 closing-lines-only bodies and the bare blocked/failed arms (~40 min)

Fix: `IsTokenOnlyAfterRetainedReport` plus the `DelegationReportFormatter` closing-line helper (D-2).

| Test | Red at base | Pins |
|---|---|---|
| `Closing_lines_only_after_blocked_reply_preserve_retained_report` | yes: Succeeded, `Result` = finding line | Reply, then `[antiphon-finding:<id> clean]` + `[antiphon-progress:<id> commit=<sha>]` + done token. Expect Working, one token-only ask and the retained fields unchanged. |
| `Bare_blocked_after_blocked_reply_blocks_with_retained_report` | no (guard, item 4) | Blocked/Marked, retained fields unchanged. |
| `Bare_failed_after_blocked_reply_fails_with_retained_report` | no (guard, item 4) | Failed, `FailureReason` = "Delegate reported failed.", retained fields unchanged. |

### S5 Review finding line in the Reply contract (optional, ~25 min)

Fix: `DelegationReportFormatter.BlockedReplyContract` and its `AnswerAsync` call (D-5).

| Test | Red at base | Pins |
|---|---|---|
| `Blocked_reply_wire_to_a_stage_task_restates_the_finding_marker` | yes: no finding token on the wire | Set `Stage = Code` on the blocked row, then Reply. The queued wire contains `FindingToken(id,"found")` and `FindingToken(id,"clean")` after the caller's text and before the report tokens. |

Guard: the existing `Blocked_reply_wire_requires_a_complete_report_and_closing_token` (the stage-less wire is unchanged).

## CARD-1083 checklist (S1, S3 change when a waiting task is released)

- S1 releases nothing by itself. It restores the one ask, so the reopened turn can settle or reach the S3 deadline.
- S3: a reopened Working task whose ask is outstanding and whose session is idle on the asked boundary is Blocked by the dispatcher sweep after `Delegation:UnmarkedWaitingMinutes` (default 5; 0 disables it). Blocked keeps the session and its seat. Nothing is killed.
- After the Block, parking is disabled (`BlockedTaskParking:Enabled` = false), so there is no automatic release deadline. A Reply, Cancel or caller action releases it.

## Verification design

The red witnesses are S1 ×2, S2 ×1, S3 ×2, S4 ×1 and S5 ×1. The guards are S1 ×1 (item 4), S4 ×2 (item 4) and the existing methods named above. This plan proposes no PCs; the SourceLanding Mutation owns deliberate mutants. A red row reports the expected assertion failures as the witness, not as failures. Any other red is checked at base before it is attributed (git-common).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c1486-s1-red/` | c1486-s1-red | `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)\|(AgentTaskReplyIntegrationTests*)/(Reply_after_a_retired_nudge*)\|(unmarked_waiting_answer_then*)\|(Second_bare_done*)` | S1 red ×2, item-4 guard | 3 executed; the two red witnesses fail for the named behaviour; the guard passes; 0 skipped | 3 | 7 |
| CP-2 | S1-fix | `tests/Antiphon.Tests -> bin-c1486-s1-green/` | c1486-s1-green | `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)\|(AgentTaskReplyIntegrationTests*)/(Reply_after_a_retired_nudge*)\|(unmarked_waiting_answer_then*)\|(Second_bare_done*)` | S1 | 3 passed, 0 failed/skipped | 3 | 7 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c1486-s2-red/` | c1486-s2-red | `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)\|(AgentTaskReplyIntegrationTests*)/(Unmarked_ack_after_the_ask*)\|(unmarked_waiting_answer_then*)` | S2 red, scope guard | 2 executed; `Unmarked_ack_after_the_ask*` fails (Succeeded with the ack); the guard passes; 0 skipped | 2 | 6 |
| CP-4 | S2-fix | `tests/Antiphon.Tests -> bin-c1486-s2-green/` | c1486-s2-green | `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)\|(AgentTaskReplyIntegrationTests*)/(Unmarked_ack_after_the_ask*)\|(unmarked_waiting_answer_then*)` | S2 | 2 passed, 0 failed/skipped | 2 | 6 |
| CP-5 | S3-tests | `tests/Antiphon.Tests -> bin-c1486-s3-red/` | c1486-s3-red | `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)\|(AgentTaskReplyIntegrationTests*)/(Untyped_*)\|(block_unmarked_waiting_then*)` | S3 red ×2, existing guard | 3 executed; both `Untyped_*` fail (stay Working); the guard passes; 0 skipped | 3 | 6 |
| CP-6 | S3-fix | `tests/Antiphon.Tests -> bin-c1486-s3-green/` | c1486-s3-green | `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)\|(AgentTaskReplyIntegrationTests*)/(Untyped_*)\|(block_unmarked_waiting_then*)` | S3 | 3 passed, 0 failed/skipped | 3 | 6 |
| CP-7 | S4-tests | `tests/Antiphon.Tests -> bin-c1486-s4-red/` | c1486-s4-red | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/(Closing_lines_only*)\|(Bare_*)` | S4 red, item-4 guards ×2 | 3 executed; `Closing_lines_only*` fails (Succeeded, finding line as Result); 2 pass; 0 skipped | 3 | 6 |
| CP-8 | S4-fix | `tests/Antiphon.Tests -> bin-c1486-s4-green/` | c1486-s4-green | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/(Closing_lines_only*)\|(Bare_*)` | S4 | 3 passed, 0 failed/skipped | 3 | 6 |
| CP-9 | S5-tests | `tests/Antiphon.Tests -> bin-c1486-s5-red/` | c1486-s5-red | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/(Blocked_reply_wire*)` | S5 red, existing wire guard | 2 executed; the stage-wire method fails; 1 passes; 0 skipped | 2 | 6 |
| CP-10 | S5-fix | `tests/Antiphon.Tests -> bin-c1486-s5-green/` | c1486-s5-green | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/(Blocked_reply_wire*)` | S5 | 2 passed, 0 failed/skipped | 2 | 6 |
| CP-11 | final | `tests/Antiphon.Tests -> bin-c1486-final/` | c1486-final | `/*/Antiphon.Tests.Application/(AgentTaskReplyIntegrationTests*)\|(ClosingReportNudgeLifecycleTests*)\|(BlockedReplyReportRetentionTests*)/*` | regression over the reply service's three integration classes | all executed pass, 0 failed/skipped (161 with all five slices) | 151 | 15 |

This is a closed list. Each committed slice group runs through the checkpoint tool on server2, waiting until the exit is not 75. There is no full suite, Unit lane or Windows run. A dropped slice drops its two rows, and the Code report states CP-11's real executed count. Alternate outputs are removed after each run. In the markdown table, `\|` is the escaped `|` of the TUnit OR grammar.

### Cost

The ordinary floor is 77 minutes (62 targeted + 15 regression). Without S5 it is 65.

## RC

The AppHost runs `rc/2026-10-20` (`ceb6e3d6b`), which carries neither CARD-1472 nor CARD-1474.

- S1 fixes a CARD-1474 interaction, so it does not apply to rc.
- S2, S4 and S5 edit CARD-1472 code (the token-only arm, `IsTokenOnlyAfterRetainedReport`, `BlockedReplyContract`) and its test class. On rc each would be a modify/delete or content conflict.
- S3's whole-session token-scan defect does exist on rc (since CARD-0294 `d7fafc803`), but rc can reach it only after a Reply on a marked task whose ask was never typed, and its retention half needs S2.

Verdict: **master-only**. If CARD-1472 and CARD-1474 are ever ported to rc, these commits follow them in landing order, proved with a per-commit `merge-tree --merge-base` chain. This plan document does not travel to rc.
