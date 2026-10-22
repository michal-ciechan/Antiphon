# CARD-1486 S1 - task ab161f15 (Code, landing owner)

Plan: `docs/superpowers/plans/2026-10-22-card-1486-reply-followups-plan.md` (on master at 75fac630c; not carried on
this branch, so no add/add; the checkpoint tool ran an unedited ignored copy at `.antiphon/plan-1486.md`).
Branch `feat/card-task-ab161f15`, base `b0c0f89b8e928a18d3a437194a13fde58a339602`. Master only (rc lacks CARD-1472/1474).

## Commits

- `480b9861da0cef6066a9098283116548344ea578` test: S1 witnesses (red x2) and the item-4 guard.
- `50f9ce6f0f3d6a74f74c33b9cc7d52b97236c145` fix: `AnswerAsync` Blocked arm calls `RearmNeverTypedClosingNudgeAsync`.
- this report commit; CP-2 and CP-11 run on it (results in the final message).

## Fix (D-1)

`AgentTaskReplyService.RearmNeverTypedClosingNudgeAsync`, called in `AnswerAsync`'s Blocked arm before the task turns
Working. It clears `ReportNudgedAt`, `ReportNudgedSequence` and `ReportNudgeMessageId` when an ask is recorded and it
was never typed: there is no message id, the row is missing, or the row is Canceled with `SentAt` = null (the states
CARD-1474's retirement and `BlockUnmarkedWaitingAsync` leave). A typed ask keeps the one-ask bound (T-4).

Two refinements of the plan's wording, both narrower than D-1:
- A row still Pending, or one marked Sent, keeps the columns. A Pending ask can still be typed, and it stays bound,
  so `NudgeStillOutstandingAsync` resolves once it is typed. Clearing it would let it be typed later as an unbound
  second ask.
- A legacy ask (both CARD-0248 columns null) is left alone. `NudgeStillOutstandingAsync` already settles it anyway,
  so it cannot strand.

## Tests

- `BlockedReplyReportRetentionTests.Reply_after_a_retired_nudge_rearms_the_one_ask`: seeds the CARD-1474 retired
  state (Canceled row with no `SentAt`, plus the three columns) on the sync-blocked fixture, then Reply and a bare
  `done`. It expects Working, a new Pending ask with a new id, one new "asked once for the complete report" Warning and
  all six retained fields unchanged.
- `AgentTaskReplyIntegrationTests.unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report`: an
  unmarked turn, then the ask, then `BlockUnmarkedWaitingAsync` (the ask is Canceled, untyped), then Answer. The next
  unmarked turn gets a fresh Pending ask. Once that ask is typed, a second unmarked turn settles
  Succeeded/`UnmarkedAfterNudge` with its own body.
- `BlockedReplyReportRetentionTests.Second_bare_done_after_a_typed_ask_blocks_with_retained_report` (guard): Reply,
  bare `done` (asked), mark the ask Sent, then bare `done` again. It expects Blocked/Marked, the same ask id, no
  second ask Warning, no Completed event and the six retained fields unchanged.

## CP-1 (red witness, at 480b9861)

```
CHECKPOINT CP-1 commit=480b9861da0cef6066a9098283116548344ea578 build=ok filter=/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)|(AgentTaskReplyIntegrationTests*)/(Reply_after_a_retired_nudge*)|(unmarked_waiting_answer_then*)|(Second_bare_done*) executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-ab161f15/.antiphon/checkpoints/20261022-152359-82a0/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=480b9861da0cef6066a9098283116548344ea578 sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.BlockedReplyReportRetentionTests.Reply_after_a_retired_nudge_rearms_the_one_ask (CP-1) ShouldAssertException: newAsk     should not be 2612eeb4-d909-48eb-bc71-07dabf2493a5     but was  Additional Info:     the retired, never-typed ask cannot be the reopened turn's ask -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.AgentTaskReplyIntegrationTests.unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report (CP-1) ShouldAssertException: secondAsk     should not be 5263fac8-0127-4583-bf7e-a153d9a35334     but was  Additional Info:     the canceled, never-typed ask is not the reopened turn's ask -> rows/CP-1/failures.md
```

Run `20261022-152359-82a0`, exit 1 (expected red). Both witnesses fail on the named behaviour: the reopened turn
still names the retired ask and no new ask goes out. The guard passed.

## Unlisted runs

- One build-slot `dotnet build tools/Antiphon.Checkpoints` to `bin-c1486-tool/` (the checkpoint tool itself, slot
  granted waited=0s); `import --plan` validated all 11 rows.
- CP-11 (the plan's final regression over the three reply-service integration classes) runs beside CP-2 on this tip
  because the round is Final and the change touches nudge state, which `ClosingReportNudgeLifecycleTests` covers. Its
  `Min` 151 is met with S1 alone; 161 is the count with all five slices.

## Checks

- `git merge-tree --write-tree origin/master 50f9ce6f0` against `1ba4ac9ff`: clean (tree 49acf4e5f).
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef b0c0f89b8 -HeadRef 50f9ce6f0`: commits=2 entries=0
  violations=0.

PCs: the plan proposes none. Deliberate mutants (for example, dropping the `RearmNeverTypedClosingNudgeAsync` call
or treating a Pending row as never typed) stay pending for SourceLanding Mutation.
