# CARD-1472 plan: preserve the retained report across a blocked Reply

Source: investigation `docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md`
(Debug fc28850a). Card: CARD-1472 (`882485f5-616f-43e8-a98c-b8d25d048691`). Code task 858e9361.
Base: `ec26d0fba3eb6f8a6dc13eca112814b764ff9c81`.

## Ground truth

| # | Observed | Code at base | Consequence |
|---|---|---|---|
| T-1 | A Reply to a task Blocked only by a sync refusal (`runner_sync_lease_busy`, identity mismatch) reopens it as a new model turn. | `AgentTaskReplyService.AnswerAsync`: the Blocked arm sets `Working` and `RepliedAtSequence`; the wire is `TaskMarker + "\n\n" + message` only. | The child is never told that its next completion must be a complete report again. "Accepted, stop" produces an acknowledgement or a bare token. |
| T-2 | Three Grok Code tasks (b92ba604, 08f282dd, 7d4b009e) answered the reopened turn with only `[antiphon-report:<id> done]`. | `SettleAsync` reads the token, `body` is empty, `ClassifyReportAsync` returns Succeeded/Marked, and the tail assigns `Result = ""`, re-parses the empty handoff (`NextStage = null`, `NextHandoff = null`) and resolves no deliverable (`DeliverablePath = null`). | The retained substantive report, its handoff and its artifact are replaced by nothing. The transcript still holds the original. |
| T-3 | The last-line token parser is strict and correct (`DelegationReportFormatter.TryReadReportVerdict`). | Lowercase prefix, exact short id, `done|blocked|failed`; a wrong id is not a token. | Do not loosen the parser. A wrong-id token stays unmarked and takes the existing one-nudge path. |
| T-4 | `ReportNudgedAt` bounds the closing-line ask to one per task and Reply does not clear it. | `ClassifyReportAsync` unmarked arm and `NudgeForClosingLineAsync`. | The token-only arm reuses the same bound and the same CARD-0248 delivery gates; it adds no second ask mechanism. |
| T-5 | A sync-blocked settlement keeps `Result`, sets `NextStage = Decide` with the "Runner sync blocked" handoff. | `SettleAsync` `remoteBlock` branch. | "Retained report" means the row's `Result`, `NextStage`, `NextHandoff`, `DeliverablePath`, `DeliverableRef`, `ResultFilePath` at the moment of the Reply. |

## Decisions

- **D-1 Every Blocked-task Reply wire restates the complete-report contract.** `AnswerAsync`'s Blocked arm appends `DelegationReportFormatter.BlockedReplyContract(taskId)` after the caller's text: the next completion must be the complete final report again (outcome, files, counts, `--- next stage ---` block), an acknowledgement or the token alone is not a report, and the exact task-specific closing lines are restated. It applies to sync-blocked and question-blocked tasks alike (the card: "including a reporting-only/accepted reply"). The Replied event's `InputBody` carries the same text, so the input-file route serves it when the runner-bound wire is spilled to a pointer.
- **D-2 A token-only completion never overwrites a retained report.** `SettleAsync` treats a valid verdict with an empty body on a task whose `Result` is non-empty as "the later turn added nothing": `Result`, `ResultFilePath`, `NextStage`, `NextHandoff`, `DeliverablePath`, `DeliverableRef` keep their values and the settlement event says so. No English is parsed and no old verdict is copied across attempts.
- **D-3 A token-only `done` does not settle Succeeded.** First occurrence on a live session: the existing bounded nudge (`ReportNudgedAt`, one ask) is issued with token-only wording and the task stays Working. After the ask was delivered and the child again ends a turn with only the token (or the session is dead): the task Blocks with the retained report intact, the caller is told the turn added nothing, and a further Reply reopens it under the same rule. Token-only `blocked` / `failed` keep their verdict's status (D-2 still keeps the retained fields).
- **D-4 Complete replacement reports are unchanged.** A non-empty body after a Reply goes through the normal classification, sync and progress checks and replaces the report exactly as today. The reply watermark (`RepliedAtSequence`) and the `PreReply` selection are untouched.
- **D-5 Scope.** No settings, deploys, migrations or sync redesign. `AgentTaskDispatcher.cs` and the land protocol are not touched (other Code tasks own them).

## Slices

### S1 (tests): `BlockedReplyReportRetentionTests`

New `tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs` on `RunnerSettlementWorld` (bare origin, desktop worktree, runner clone, isolated schema, real `AgentTaskReplyService`). The fixture pushes runner work, holds the desktop repository lease and settles a complete marked report through the real service with the controlled sync clock advanced past the budget, which Blocks the task on `runner_sync_lease_busy` with the report retained (the measured shape). The Reply goes through `AgentTaskReplyService.AnswerAsync`; the next turn is seeded through `TurnSeeding` with the actual queued wire as its prompt and judged by `OnTurnEndAsync`.

| Method | Red at base | What it pins |
|---|---|---|
| `Bare_done_after_blocked_reply_preserves_retained_report` | yes (Succeeded, `Result = ""`) | V-1: retained `Result`/`NextStage`/`NextHandoff`/`DeliverablePath`/`DeliverableRef`/`ResultFilePath` unchanged, no Succeeded publication, no Completed event, the task stays Working with the one bounded ask recorded. |
| `Blocked_reply_wire_requires_a_complete_report_and_closing_token` | yes (wire is marker + text) | V-2: the actual `SessionQueuedMessages` row for the delegate session starts with the task marker, keeps the caller's text, and restates the complete-report requirement with the exact `[antiphon-report:<id> done]` and `blocked` lines after the caller's text; the Replied event stores the same body. |
| `Complete_replacement_report_settles_once` | no (guard) | V-3: a full marked replacement report after the Reply settles Succeeded with the new body and handoff; a second boundary observation adds no event and changes nothing. |
| `Wrong_task_token_cannot_settle_the_reopened_task` | no (guard) | V-4: a token for another task id is unmarked: one nudge, Working, retained report unchanged, no Completed event. |
| `Genuine_marked_blocked_reply_remains_blocked` | no (guard) | V-5: a complete `blocked` report after the Reply settles Blocked/Marked with the new question as `Result`. |

### S2 (fix)

- `server/Application/Services/DelegationReportFormatter.cs`: `BlockedReplyContract(Guid taskId)` (D-1).
- `server/Application/Services/AgentTaskReplyService.cs`: Blocked-arm wire (D-1); `ClassifyReportAsync` token-only arm with the shared CARD-0248 nudge gates and token-only nudge wording (D-3); `SettleAsync` retained-field guard and settlement wording (D-2).

## Verification design

V-1 to V-5 are the five methods above; the two red witnesses are V-1 and V-2. No PCs are proposed by this plan; the SourceLanding Mutation owns any deliberate mutants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-report-retain-red/` | retained-report-red | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/*` | two red witnesses, three guards | 5 executed, first two fail for named behavior, 3 pass, 0 skipped | 5 | 6 |
| CP-2 | S2-fix | `tests/Antiphon.Tests -> bin-report-retain-green/` | retained-report-green | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/*` | all five methods | 5 passed, 0 failed/skipped | 5 | 6 |

Closed list; each committed slice group runs through the checkpoint tool on server2 (wait until the exit is not 75). No full suite or Windows run. Alternate outputs are removed after the run.

### Cost

Ordinary floor is 12 minutes (6 + 6).

## RC

The defect exists on `rc/2026-10-20` (the AppHost line). The fix commits touch `AgentTaskReplyService.cs`, `DelegationReportFormatter.cs` and the new test file; the Code report states whether they cherry-pick cleanly onto `origin/rc/2026-10-20` by themselves (this plan document does not travel to rc).
