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
force-delete safeguards, history, FK definitions and error propagation. Run final CP-1/2/3.

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
R-3: Whole Unit lane required by the Final profile and stage instructions (the narrow brief
also says no whole Unit; the higher-priority Final requirement governs). No full assembly run.
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
21 minutes for final S2, plus authoring, receipts and review preparation. No repetition after green.
The checkpoint tool bootstrap is the only planned unlisted build and takes a build slot.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1253/` | cascade-fks | `/*/*/(ProjectCascadeForeignKeyTests*)/*` | V-1, V-2, V-3, V-4 | all 11 cases, 0 failed/skipped; S1 first run intentionally red | 11 | 8 |
| CP-2 | S2 | CP-1 | deletion-regressions | `/*/*/(ProjectDeletionTests*)\|(BoardProjectArchiveApiTests*)\|(CardFilePrivacyOwnershipAcceptanceTests*)/*` | R-1, R-2 | all 38 cases, 0 failed/skipped | 38 | 3 |
| CP-3 | S2 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed/skipped | 1 | 7 |
| CP-4 | S2 | CP-1 | deletion-consumers | `/*/*/(SessionStateWarmupTests*)\|(CardFileBoardLookupTests*)/*` | R-4 | all 61 cases, 0 failed/skipped | 61 | 3 |

S1 runs CP-1 by explicit row selection before the S2 implementation; S2 reruns the same row
and runs CP-2/CP-3/CP-4 once. Outputs from red S1 are removed before the final fresh build.
