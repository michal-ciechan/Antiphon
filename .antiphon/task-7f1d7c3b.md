# CARD-1503 review round 1 D-1/D-2 fix (Code task 7f1d7c3b)

Branch feat/card-task-7f1d7c3b, worktree /work/worktrees/task-7f1d7c3b, continued from
dae23c2dbfe58053c20af3fb7a2b869fc2061046 (feat/card-task-ab00c63e; landing owner of the original Code: ab00c63e).

Commits:
- 54594953e344343984188c76f0059d6c24b53e25 test: red witnesses (D-1, D-2)
- 63cb5a1e8cb24f462162befa5fd0f242219bdd97 fix: D-1/D-2 + A-1/A-3 (tested tip)
- this report commit

## Design

D-1 (silent question). The only path that dropped the note is the legacy direct enqueue
(`DeliverToParentAsync` -> `EnqueueParentNoteAsync`). It is the only path that reaches the CARD-0320
SourceTaskId+digest and `CompletionNoteDigest` check. The keyed paths (durable Blocked-park outbox and the
profile-v1 completion obligation) are already one note per settlement event (`SourceLandNotificationId`), and
they skip that check. For a retained-question settlement only, the legacy note digest is now
`Compute(report + "\n\n[retained-question-block <Blocked event id>]")` (`RetainedQuestionBlock.NoteDigest`).
- A replay of the same settlement uses the same event object and the same digest, so it is still dropped.
  CARD-0320 is not weakened.
- A different settlement (a new question after another Reply) gets a new note.
- `CompletionNoteStamp` stamps only while unset. If the question note is ever the first stamp, its digest is
  never equal to a later genuine report's plain digest, so it can never suppress the next completion of the
  same report.
- The keyed paths keep the plain digest, because the park handoff check (`TaskParkPublicationService`) and the
  distillation identity check read `Compute(task.Result)`.

D-2 (surfaces). `BlockedNote.RetainedQuestion(task, latestBlockDetail)` returns the question when the
evidence is Marked and the latest Blocked event (no Conflicted after it) starts with `Delegate asked: `.
Only the CARD-1503 retained-question arm writes that combination; the plain question arm writes
QuestionHeuristic. The question then drives:
- `AttentionPrimary`: AttentionService now projects the event Detail.
- `BlockedContextBuilder` drawer question: context is a fixed "Asked after a Reply; retained report stands"
  line, and the reason is `question-after-reply`.
- `BlockedNote.Format`: the asks line is built from the question, and the new reason word `question-after-reply`
  carries the gloss "ended unmarked with a question after the Reply; the retained marked report stands".
All three are bounded through `ExtractAsks` (240-character cap, flattened to one line).
A-1: the warning or caller line ends `Delegate asked: <asks>`, which is capped and flattened.
A-3: "(N characters)" now counts the extracted question.
Not touched: delegate.ps1, AgentTaskDispatcher.cs, CreateAgentTaskRequest, AgentTask.cs, migrations, client.

## Witnesses (BlockedReplyReportRetentionTests)

- `Question_after_reply_notifies_the_caller_once`: checks four things.
  - The caller's notes go from n to n+1, and the new note's `asks:` line names the question.
  - A replay (OnTurnEndAsync again, plus the same note re-offered to the queue) leaves the count at n+1.
  - A later Reply followed by a genuine marked report brings it to n+2, and that note contains the new report.
  - Red at 54594953: "should be 2 but was 1".
- `Question_after_reply_is_the_question_on_every_surface`: uses a 2-line question longer than 240 characters.
  - Attention evidence, drawer Question and note `asks:` each equal the capped, flattened question, with reason
    `question-after-reply` and no "ended with the blocked token".
  - The drawer context does not contain the retained report.
  - The warning counts the question's characters and ends with the capped question.
  - Red at 54594953: the attention evidence was the retained report.
- Mutants (one build, uncommitted, restored with git checkout; this was a build-slot run, not a checkpoint row):
  - M1, digest identity reverted to `Compute(report)` in EnqueueParentNoteAsync: notifies_once is red, 2 vs 1.
  - M2, the RetainedQuestion branch removed from AttentionPrimary: every_surface is red on row.Evidence (the
    retained report).
  - Both runs: 2 failed / 0 passed.

## Checkpoint run 20261102-180214-3095

The plan is .antiphon/c1503-r2/plan.md (gitignored). Expected SHA 63cb5a1e8cb24f462162befa5fd0f242219bdd97.
Verdict GREEN exit=0, 7 rows green, one build, `unlisted: none`, wall 19m06s.
    CHECKPOINT CP-1 commit=63cb5a1e8cb24f462162befa5fd0f242219bdd97 build=ok filter=/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-7f1d7c3b/.antiphon/checkpoints/20261102-180214-3095/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 sourceState=clean buildSource=verified
    CHECKPOINT CP-3 commit=63cb5a1e8cb24f462162befa5fd0f242219bdd97 build=reused filter=/*/Antiphon.Tests.Application/(AgentTaskDetailBlockedContextTests*)|(AttentionServiceTests*)/* executed=177 passed=177 failed=0 skipped=0 trx=/work/worktrees/task-7f1d7c3b/.antiphon/checkpoints/20261102-180214-3095/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 sourceState=clean buildSource=verified
    CHECKPOINT CP-4 commit=63cb5a1e8cb24f462162befa5fd0f242219bdd97 build=reused filter=/*/Antiphon.Tests.Application/(DelegationReportFormatterTests*)|(DelegationQuestionDetectionTests*)/* executed=100 passed=100 failed=0 skipped=1 trx=/work/worktrees/task-7f1d7c3b/.antiphon/checkpoints/20261102-180214-3095/rows/CP-4/run.trx slot=granted waited=60s dirty=0 source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 sourceState=clean buildSource=verified
    CHECKPOINT CP-2 commit=63cb5a1e8cb24f462162befa5fd0f242219bdd97 build=reused filter=/*/Antiphon.Tests.Application/(AgentTaskReplyIntegrationTests*)|(AgentTaskReplyOverlayTests*)/* executed=362 passed=362 failed=0 skipped=0 trx=/work/worktrees/task-7f1d7c3b/.antiphon/checkpoints/20261102-180214-3095/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 sourceState=clean buildSource=verified
    CHECKPOINT CP-6 commit=63cb5a1e8cb24f462162befa5fd0f242219bdd97 build=reused filter=/*/Antiphon.Tests.Application/(ReadyGateFailureNoticeDeliveryTests*)|(AgentTaskDecisionQuestionIntegrationTests*)|(ReceiptFailureDeliveryTests*)|(RolePolicyToolBundleDispatchTests*)/* executed=49 passed=49 failed=0 skipped=0 trx=/work/worktrees/task-7f1d7c3b/.antiphon/checkpoints/20261102-180214-3095/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 sourceState=clean buildSource=verified
    CHECKPOINT CP-7 commit=63cb5a1e8cb24f462162befa5fd0f242219bdd97 build=reused filter=/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-7f1d7c3b/.antiphon/checkpoints/20261102-180214-3095/rows/CP-7/run.trx slot=granted waited=45s dirty=0 source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 sourceState=clean buildSource=verified
    CHECKPOINT CP-5 commit=63cb5a1e8cb24f462162befa5fd0f242219bdd97 build=reused filter=/*/Antiphon.Tests.Application/(PostLandMutationDeliveryTests*)|(DelegationDispatchRecoveryBoundaryTests*)/* executed=449 passed=449 failed=0 skipped=0 trx=/work/worktrees/task-7f1d7c3b/.antiphon/checkpoints/20261102-180214-3095/rows/CP-5/run.trx slot=granted waited=45s dirty=0 source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 sourceState=clean buildSource=verified

Validation:
- `validate` over all rows: `CHECKPOINT SOURCE INVALID reason=row_failed`. The only cause is CP-4 skipped=1.
  That test is `reported_repository_paths_normalize_relative_and_absolute_windows_forms`, which throws
  SkipTestException off Windows (CARD-0681) by construction and is unrelated to this change.
- `validate --rows CP-1,CP-2,CP-3,CP-5,CP-6,CP-7`: `CHECKPOINT SOURCE VALID source=63cb5a1e8cb24f462162befa5fd0f242219bdd97 rows=6`.
- CP-4 itself: 100 passed, 0 failed.

Unlisted runs, each under build-slot:
- Red build and run at 54594953: 2 tests, 2 failed.
- Fix build and run at 63cb5a1e, whole class: 23/23.
- Mutant build and run: 2/2 red.
- One LandGraphAudit Release build for `-Check`; its `bin-land-audit` directories were deleted.
- `checkpoint import` (validation of the table).

Scope deviation: the profile says Final (whole Unit lane), but the brief names the class set "once each".
I ran that set plus the BlockedNote unit classes, the drawer class and the park delivery class.
The whole Unit lane was NOT run (deferred to final; not passed).
`grep -rln BlockedContextBuilder tests` finds no file, so I used AgentTaskDetailBlockedContextTests and
AttentionServiceTests for the drawer and attention.

## Gates

- land graph `-Check`: approved, count=69, digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE.
  I did not run `-Update`.
- check-evidence-diff over 5376ac529..63cb5a1e: commits=7, entries=0, violations=0, exit 0.
- merge-tree onto origin/master 30cb5279bd4e and onto origin/rc/2026-11-01 10a826b9d: both clean.

## Pending for Mutation (method-scoped)

- PC-a: the digest identity in EnqueueParentNoteAsync. Witness Question_after_reply_notifies_the_caller_once.
- PC-b: the RetainedQuestion branch in AttentionPrimary. Witness Question_after_reply_is_the_question_on_every_surface.
- PC-c: the retained branch in QuestionAndContext and the reason (drawer). Same witness.
- PC-d: `BlockedNote.Format` retainedQuestion asks and reason. Same witness.
- PC-e: the AttentionService Blocked-only detail projection. Same witness.
- PC-f: the A-3 count and A-1 cap in UnmarkedQuestionRetainedDetail. Same witness.
