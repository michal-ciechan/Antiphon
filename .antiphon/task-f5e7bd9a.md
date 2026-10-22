# CARD-1486 S2 - task f5e7bd9a (Code, Final round)

An unmarked turn after the closing-line ask now Blocks and keeps a retained marked report (plan D-3). The two S1 guards from Review dfd45dcb are added in the same commit as the S2 red witness.

- Branch `feat/card-task-f5e7bd9a`, runner mirror `/work/worktrees/task-f5e7bd9a`; landing owner: Code task f5e7bd9a.
- Plan: `docs/superpowers/plans/2026-10-22-card-1486-reply-followups-plan.md` on origin/master. The amendment tables are in the ignored `.antiphon/c1486-s2-red.md` and `.antiphon/c1486-s2-final.md`.

## Commits (base 50a18c2443e14d8aeafe5cc56062a1a5bb0e25f2)

| SHA | What |
|---|---|
| b69566496 | carry of S1 tests 27e994af1, which landed on master; same patch-id 21575ca5 |
| 78971ae1b | carry of S1 fix 8b7edcdde; same patch-id a9396c3b |
| 4152978e139a784403ac6a04c1168639e089bfc6 | S2 red witness `Unmarked_ack_after_the_ask_blocks_with_retained_report`, plus the guards `Reply_with_a_pending_ask_keeps_the_recorded_ask` and `Reply_with_a_sent_ask_keeps_the_recorded_ask` (the S1 seed helper now takes the row state) |
| 49550f80f3e38534c76f677f6d767f43a515dbda | test compile fix: AgentTaskEvent orders by `At` (it has no `CreatedAt`) |
| febdf83df512b4c36b26e2a5324f9c8c36238ec8 | fix: the new `RetainedReportStands` predicate (RepliedAtSequence set, ReportEvidence Marked, non-empty Result; S3 reuses it). The unmarked arm Blocks with the retained body where it would have settled anyway. SettleAsync's `retainedUnmarked` skips the handoff, deliverable and result-file recompute. The Blocked event and the caller warning name the turn's `#sequence`, its length and that the retained report stands. |

S1 landed on master before this task started, as 27e994af1, 8b7edcdde and the report e4db491e7. This branch is fast-forward-only from 50a18c244 and may not merge the target, so S1 is carried by cherry-pick. The land rebase drops the carries because their patch-ids match.

## Red witness (narrow build-slot run, stated)

I ran it as a build-slot run at 49550f80f in a detached worktree. That run is outside the checkpoint table. The checkpoint run of the red rows, 20261022-163524-594a, ended exit 5 (`timeout=total`, no TRX, nothing executed): the host slot budget was saturated at 4/4, so its build waited 825 s and its rows waited 13+ min for a slot. An earlier run, 20261022-162451-6a30 at 4152978e1, was stopped when a scratch build showed the test did not compile.

- `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)|(AgentTaskReplyIntegrationTests*)/(Unmarked_ack_after_the_ask*)|(unmarked_waiting_answer_then*)`: total 2, failed 1, succeeded 1, skipped 0. slot granted waited=360s.
- `Unmarked_ack_after_the_ask_blocks_with_retained_report` fails at line 307: `after.Status should be Blocked but was Succeeded`, evidence=UnmarkedAfterNudge, result-length=30 (the ack replaced the report). This is the planned red.
- The scope guard `unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report` passes.

## Guard mutation evidence (scratch only, never committed)

Each mutation was a build-slot run in a detached worktree at 4152978e1 with the `At` compile fix applied, filter `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/(Reply_with_a_*)`. Both mutations were restored afterwards (`git checkout`) and the worktree was removed.

| Mutant in `RearmNeverTypedClosingNudgeAsync` | Result | Named red |
|---|---|---|
| M1 "treat Pending as never typed": `row.SentAt is not null \|\| (row.Status != Canceled && row.Status != Pending)` | total 2, failed 1, succeeded 1 (slot waited=661s) | `Reply_with_a_pending_ask_keeps_the_recorded_ask` at :244: `ReportNudgeMessageId should be <seeded id> but was null` ("a Pending ask can still be typed, so the Reply keeps it"). The Sent guard stays green, as expected. |
| M2 "clear the columns unconditionally": the keep-condition becomes `... && false` | total 2, failed 2 (slot waited=631s) | Pending guard red at :244, and Sent guard red at :265: `ReportNudgeMessageId should be <seeded id> but was null` ("a typed ask is the task's one ask, so the Reply keeps it") |

Without a mutation, both guards are expected green at the S1 code. The final run's CP-12 records that.

## Final checkpoint run (at this report's commit)

`.antiphon/c1486-s2-final.md` (import validated, 3 rows; all rows share one build `bin-c1486-final/`):

- CP-4: the plan's S2 fix row.
- CP-12: the two guards.
- CP-11: the plan's regression row over AgentTaskReplyIntegrationTests, ClosingReportNudgeLifecycleTests and BlockedReplyReportRetentionTests. The plan's 161 included S3-S5, so this tip expects about 157, with min 151.

The CHECKPOINT lines are in the task's final message, because this report is committed before that run so that the tested source is the pushed tip.

## Landability

- Whole-branch `merge-tree --write-tree origin/master HEAD` (master 445df8a80) conflicts in BlockedReplyReportRetentionTests.cs. The cause is the S1 carry: both sides changed the file from the same base.
- The S2 commits 4152978e1, 49550f80f and febdf83df chained per commit with `--merge-base <parent>` onto origin/master apply with exit 0 (final tree d096817be). The land rebase drops the two carried commits (identical patch-ids), so the land is clean.
- rc `origin/rc/2026-10-20` (0714a9a89): the S1 and S2 chain 27e994af1, 8b7edcdde, 4152978e1, 49550f80f, febdf83df applies with exit 0 (tree 0ccb8f5b2). S2 alone conflicts without S1. So S2 is rc-eligible only after S1, in that order.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 50a18c244 -HeadRef febdf83df`: commits=5, entries=0, violations=0.

## Pending for Mutation

The plan proposes no PCs. The M1 and M2 scratch mutants above are guard-viability evidence only. The SourceLanding Mutation owns deliberate mutants for D-3, for example dropping `RetainedReportStands` from the unmarked arm, or dropping `retainedUnmarked` from SettleAsync's recompute guard.
