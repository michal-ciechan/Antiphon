# CARD-1253 remaining cascade foreign keys

Original Code/landing owner: f3a5b136-0402-405d-88cd-4b3875b5d366.
Base: c637b74eea51bb88cf0d2137c05672692ab909dc (fast-forward from assigned 2a64db19bea886657526100ff92a6af4aad2f67f).
Round: Final. Restart: server after landing; no restart during Code.

## Implementation

S1: Commit tests first. Real PostgreSQL fixtures exercise both project force-delete and
board delete, with another project and sibling board, and a failure after card deletion.
Run CP-1 against unchanged production code to establish the guarded defects.

S2: Explicitly delete nudges selected by doomed comment IDs and decision questions selected
by doomed session IDs before those parents, within the existing caller transaction. Preserve
force-delete safeguards, history, FK definitions and error propagation. Run final CP-1/2/4.

The Done cleanup executors remain unregistered. Activating them requires coordinating
deletion with in-flight recovery records; activation is outside this task.

## Verification design

V-1: `Nudge_audit_comments_are_deleted_only_with_their_board_or_project` (2 cases): literal
absence and survivor-body/FK assertions; destination session deliberately belongs elsewhere.
V-2: `Decision_questions_are_deleted_by_session_even_when_the_task_belongs_elsewhere`
(2 cases): absence and preserved answers, with a task in another project.
V-3: `Later_failure_restores_nudges_or_questions_and_all_parents` (4 cases): exact injected
failure after card deletion; fresh context proves dependents, parents and owner links restored.
V-4: `Restrictive_task_history_survives_while_card_cleanup_is_removed` (2 cases): actual
restrictive rows survive with their task, while card cleanup endpoint/target/generation/revision
are removed in order. `Restrictive_foreign_key_inventory_requires_an_explicit_deletion_probe`
compares the EF model against a hand-written 15-link inventory.
R-1: Full `ProjectDeletionTests` (20), including existing workflow refusal, last-board workflow
preservation, confirmation, agent detachment, session and CARD-1244 cleanup/rollback cases.
R-2: Full `BoardProjectArchiveApiTests` (2) and `CardFilePrivacyOwnershipAcceptanceTests` (16).
R-3: Deferred disclosure: the proposed bounded whole Unit follow-up is not run, per the
caller's closing scope clarification. It is outside this task's remaining verification and
is not a passing result. The earlier broad CP-3 attempt and its timeout remain recorded below.
No further whole Unit run or wait is authorized on this loaded host. No full assembly run.
R-4: Full `SessionStateWarmupTests` (16) and `CardFileBoardLookupTests` (45), discovered by
the delete-path caller audit: committed/rolled-back session projection and board lookup invalidation.
Manual M-1: inspect the complete model snapshot for every Restrict/NoAction link into Card,
CardComment, CardRevision, AgentSession, AgentTask, Board and Project. Confirm the runtime
inventory matches it. M-2: evidence-policy diff, pushed-ref equality, clean tree, merge-tree.

### Foreign-key audit

| Link | Ordinary probe | Expected classification |
|---|---|---|
| ExpectationNudge.AuditCommentId -> CardComment | V-1, V-3 | newly fixed |
| AgentTaskDecisionQuestion.AgentSessionId -> AgentSession | V-2, V-3 | newly fixed |
| AgentTask.ParentTaskId -> AgentTask | V-4 child parent retained | parent task survives |
| AgentTaskDispatchWarningIntent.TaskId -> AgentTask | V-4 warning retained | parent task survives |
| AgentTaskLandRequest.TaskId -> AgentTask | V-4 request retained | parent task survives |
| AgentTaskLanding.TaskId -> AgentTask | V-4 landing retained | parent task survives |
| Board.ProjectId -> Project | V-1/V-2/V-4 and R-1 | handled by cascade order |
| CardWorktreeCleanup.CardId -> Card | V-4 and R-1 CARD-1244 | handled by cascade order |
| CardWorktreeCleanup.DoneRevisionId -> CardRevision | V-4 and R-1 CARD-1244 | handled by cascade order |
| CardWorktreeCleanupTarget.TaskId -> AgentTask | V-4 and R-1 CARD-1244 | target deleted, task survives |
| TaskWorktreeMirrorBinding.TaskId -> AgentTask | V-4 binding retained | parent task survives |
| TaskWorktreeRetirement.TaskId -> AgentTask | V-4 retirement retained | parent task survives |
| VerificationExecution.TaskId -> AgentTask | V-4 execution retained | parent task survives |
| WorktreeCleanupAttempt.TaskId -> AgentTask | V-4 attempt retained | parent task survives |
| Workflow.ProjectId -> Project | R-1 workflow refusal/last-board preservation | cannot delete referenced project through these services |

All 15 are Restrict; no explicit NoAction link targets these parents. The scan also inspected
all other target links, which are Cascade or SetNull (no omitted default-delete links).

### Pending positive controls

Mutation owns deliberate mutants, red/restore/green and missing-control discovery.
PC-1: omit/misorder nudge deletion, V-1 (board and project variants).
PC-2: broaden nudge selection, V-1 (sibling and other-project variants).
PC-3: omit/misorder question deletion, V-2 (board and project variants).
PC-4: select questions by task ownership, V-2 cross-project task variant.
PC-5: broaden question selection, V-2 (sibling and other-project variants).
PC-6: move dependent deletion outside caller transaction, V-3 (nudge/question x board/project).
PC-7: delete surviving task/history, V-4 (board and project variants).
PC-8: omit cleanup leaf ordering, V-4 endpoint/target/generation variants.
PC-9: remove workflow safeguard, R-1 force-delete workflow refusal variant.
All pending SourceLanding Mutation; candidate artifacts belong under docs/superpowers/mutations/.

### Cost

One build per committed source group, shared by all its rows. Expected 8 minutes for red S1,
14 minutes for the scoped final S2 rows, plus authoring, receipts and review preparation.
The historical Unit attempt is disclosed below, not queued again. No repetition after green.
The checkpoint tool bootstrap is the only planned unlisted build and takes a build slot.

### Repair F1 (Review 3407870c)

Repair Code task: bb848929-c0b9-4862-94fe-b1929ed9c255; original landing owner unchanged.
Production baseline: 0cf2d997e138996a9f5af3644b0072c562a74785.
V-5: `Holding_nudge_refuses_delete_preserves_composer_and_allows_audited_release`
(board/project x Attempting/Uncertain/Unconfirmed, 6 cases) exercises the real HTTP routes,
409 code/detail, retained graph and real queue gate, then operator release audit and successful delete.
V-6: `Nonholding_nudge_allows_delete_while_another_board_hold_survives`
(board/project x None/Confirmed/Refused/Submitted/Released, 10 cases) covers successful scoped
deletion while an unrelated audit board still holds the surviving session.
R-5: Full `ExpectationDirectDeliveryTests` and `ExpectationHoldReleaseEndpointTests`
exercise the shared holding-state predicate used by queue, release and deletion.

| Behaviour | Named test | What it asserts |
|---|---|---|
| Delete refuses an audit nudge holding a surviving session | Holding_nudge_refuses_delete_preserves_composer_and_allows_audited_release | Both routes return 409 with release route; graph and composer hold survive; audited operator release permits deletion |
| Terminal nudge deletion remains scoped | Nonholding_nudge_allows_delete_while_another_board_hold_survives | Five non-holding states delete; a different board's hold remains effective |

PC-10 pending: omit refusal or omit one holding state, V-5 (board/project x three states: 6 variants).
PC-11 pending: broaden blocker outside doomed comments, V-6 (board/project: 2 variants).
PC-12 pending: treat Released as holding, V-5 release-followed-by-delete (board/project: 2 variants).
PC-13 pending: move refusal after a destructive statement, V-5 (board/project: 2 variants); the command interceptor requires zero non-query SQL before refusal.
PC-1..PC-13 remain pending SourceLanding Mutation, including missing-control discovery.

Repair S1 runs CP-1 red against unchanged production, then repair S2 reruns CP-1 plus
CP-2/CP-4/CP-5 after implementation and the requested master merge. One test-project build
per committed source group; a second build is necessary for red/green source separation.
R-3 remains deferred under the explicit repair brief. R-4 retained for cumulative coverage.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-bb848929/` | cascade-fks | `/*/*/(ProjectCascadeForeignKeyTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6 | 27 cases, 0 failed/skipped; S1 first run intentionally red | 27 | 8 |
| CP-2 | S2 | CP-1 | deletion-regressions | `/*/*/(ProjectDeletionTests*)\|(BoardProjectArchiveApiTests*)\|(CardFilePrivacyOwnershipAcceptanceTests*)/*` | R-1, R-2 | all 38 cases, 0 failed/skipped | 38 | 3 |
| CP-4 | S2 | CP-1 | deletion-consumers | `/*/*/(SessionStateWarmupTests*)\|(CardFileBoardLookupTests*)/*` | R-4 | all 61 cases, 0 failed/skipped | 61 | 3 |

| CP-5 | S2 | CP-1 | expectation-hold-regressions | `/*/*/(ExpectationDirectDeliveryTests*)\|(ExpectationHoldReleaseEndpointTests*)/*` | R-5 | 18 cases, 0 failed/skipped | 18 | 4 |

S1 runs CP-1 by explicit row selection before the S2 implementation; S2 reruns the same row
and runs CP-2/CP-4 once. Outputs from red S1 are removed before the final fresh build.

CP-3 is retained as historical evidence, outside the executable checkpoint table: its original
whole Unit filter was `/*/*/*/*[Category=Unit]`, with a 7-minute estimate and unchanged
21-minute deadline. Removing its executable row does not change its recorded timeout or receipts.

## Code outcome (2026-10-14)

Implementation SHA: `44fb572e67122b18847378a4f733b0560209b323`. V-1 through V-4 and
R-1/R-2/R-4 passed: 110 integration cases, zero failures/skips. The eight new failure
cases were red on unchanged production before the fix. All 15 audit links have live
probes; no further restrictive-FK deletion blocker was found.

R-3 bounded follow-up: NOT RUN, DEFERRED by the caller; not a completion gate for this
scoped handoff. The earlier CP-3 attempt reached its unchanged 21-minute deadline without a terminal TRX.
Its partial log contains 19 failures and 37 Windows-only skip messages. A focused base
probe at `c637b74eea51bb88cf0d2137c05672692ab909dc` executed all 19 failed methods and
reproduced all 19 failures. The initial base filter selected zero tests; the documented
method-prefix wildcard repair reused that build. Both attempts are retained as unlisted,
necessary failure-triage evidence. The 19 failures are inherited; the 37 Windows-only skips
remain unqualified. The Unit timeout itself was not repeated at base.

CP-1/CP-2/CP-4 clean receipts validated at the implementation SHA. The entire selection
does not qualify as clean whole Unit evidence. The caller explicitly closed further Unit
work as out of scope. V-1 through V-4 and R-1/R-2/R-4 are the completed ordinary verification
for this handoff; next is Review with R-3 deferred and its historical evidence disclosed.
No timeout or assertion was loosened. All PC-1 through PC-9 variants remain pending for
SourceLanding Mutation. Original landing owner remains `f3a5b136-0402-405d-88cd-4b3875b5d366`.

Full report, unedited checkpoint lines, per-class counts, native skip roster and pending
PC variants: `.antiphon/task-f3a5b136.md`.
