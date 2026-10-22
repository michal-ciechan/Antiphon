# CARD-1486 S3 - Code task f7bb72f2 report

Slice: S3 of `docs/superpowers/plans/2026-10-22-card-1486-reply-followups-plan.md` (D-4): the
waiting deadline (`BlockUnmarkedWaitingAsync`) reaches reopened tasks and keeps the retained report.

## Branch and base

- Branch `feat/card-task-f7bb72f2`, assigned at `713801a8f98c0403bb9981bcbe954e68110cbb19`. That
  commit did not contain S2 (`9516bfd35`), so the branch was fast-forwarded (no merge commit and no
  rewrite) to `origin/master` `db1e72fd34acd52c4ab2436de28f0d1af3578b42`, which carries S1 and S2.
- Commits:
  - `73ee2fb32221211312013d810022c067d8ee8d99` test: the S3 red witnesses
  - `60d4842268be8d1b8bb1871e5489beccf2e94ccb` fix
  - this report commit (the tested tip; the CP-6/CP-11 run starts at it)

## Change

`server/Application/Services/AgentTaskReplyService.cs`, `BlockUnmarkedWaitingAsync`:
- The token scan counts only `AssistantText` with `Sequence > RepliedAtSequence` when a Reply
  reopened the attempt. A non-reopened task scans the whole session, as before.
- A token entry that adds nothing to a retained report does not stop the sweep. The new
  `AddsNothingToRetainedReport` helper is `TryReadReportVerdict` plus the CARD-1472
  `IsTokenOnlyAfterRetainedReport`, so S4's closing-lines-only extension of that predicate reaches
  the sweep without further change.
- `keepRetained = RetainedReportStands(task) || AddsNothingToRetainedReport(task, turn.Report)`.
  When it holds, the Block leaves `Result` and `ReportEvidence` unchanged, the Blocked event says
  that the retained report stands, and the retained report goes to the parent. Otherwise the
  behaviour is unchanged (`UnmarkedWaiting`, `Result` = the turn).
- Not changed: the dispatcher sweep gate, the guards (`IsWorkingAsync`, interrupted, API-error,
  deferred, uncorrelated, pre-reply, the nudged-boundary equality), the nudge cancel, and the session
  and seat (nothing is killed; CARD-1083 parking stays off).

Deviation for Review: the second operand of `keepRetained`, `|| AddsNothingToRetainedReport(...)`,
goes beyond the plan's literal D-4. The plan's D-2 exclusion would let the sweep reach a reopened
row whose retained evidence is not Marked (for example `UnmarkedWaiting`) when the reopened turn
was a bare token. Without that operand, `Result` would become the bare token line. With it, the
retained row is kept as it stands.

Guard against blocking a task that is still progressing: a marked report with a body, sent after
the Reply, still stops the sweep, and so does any token when the task was not reopened. The boundary
must still be the nudged one. Residual risk: if a report's final text arrives as two
`AssistantText` rows, the row holding only the token is excluded on its own. The nudged-boundary gate
covers that case, because such a turn is not token-only for the classifier and would not have been
nudged at that boundary.

## Tests

`tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs` adds
`Untyped_token_only_ask_blocks_after_waiting_minutes_with_retained_report` and
`Untyped_unmarked_ask_after_reply_blocks_with_retained_report`. Both use one shared assertion
helper. Each test sets up a Reply, then the reopened turn, then one recorded ask that is still
untyped (`SentAt` null), then rewinds `ReportNudgedAt` by 6 minutes and runs the sweep. Each expects:
- Blocked, with `ReportEvidence` Marked, the six retained fields unchanged and the session kept;
- the ask Canceled;
- exactly one new caller note, carrying the retained body;
- no Completed event.

## CP-5 red (at 73ee2fb32)

```
CHECKPOINT CP-5 commit=73ee2fb32221211312013d810022c067d8ee8d99 build=ok filter=/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)|(AgentTaskReplyIntegrationTests*)/(Untyped_*)|(block_unmarked_waiting_then*) executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-f7bb72f2/.antiphon/checkpoints/20261022-185029-dc1d/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=73ee2fb32221211312013d810022c067d8ee8d99 sourceState=clean buildSource=verified
```

Run `20261022-185029-dc1d`, verdict RED exit=1. Both `Untyped_*` fail with `after.Status should be
Blocked but was Working`: they get past every fixture assertion, so the sweep returned early
because of the pre-reply token. The guard `block_unmarked_waiting_then_answer_resumes_the_same_session`
passes. The kept `bin-c1486-s3-red/` was deleted by hand.

## Landability

- `git merge-tree --write-tree origin/master HEAD` was clean at `ddd43ec2d`.
- rc `origin/rc/2026-10-20` (`0714a9a89`) carries CARD-1472 and CARD-1474 but no CARD-1486 commit. On
  its own, S3's test commit conflicts there (content conflict in
  `BlockedReplyReportRetentionTests.cs`), because the S1/S2 tests are missing. The per-commit
  `merge-tree --merge-base` chain below is clean, commit by commit (merge-tree only, not compiled):
  `27e994af1`, `8b7edcdde` (S1), `645c7b30b`, `bcd04d29e`, `9516bfd35` (S2), `73ee2fb32`, `60d484226` (S3).
  The rc pick must carry those, in that order, and leave out the plan and report docs. The S3 fix
  also needs S2's `RetainedReportStands` in order to compile.
- `scripts/check-evidence-diff.ps1` reported 0 violations over `db1e72fd3..60d484226` (2 commits)
  and over `713801a8f..60d484226` (33 commits).

## Run 2 red at 295d4bbed: the witness assertion was too strict

Run `20261022-185731-dea7` (CP-6 + CP-11) was started at `295d4bbed20b2c15c26cd894c48241b6a3586958`.
CP-6 was red: 3 executed, 1 passed, 2 failed. Both `Untyped_*` methods passed every behaviour
assertion: Blocked, Marked evidence, the six retained fields, the session kept, the ask Canceled.
They failed only on my own extra check, `CallerNotesAsync(...).ShouldBe(notesBefore + 1)`, which
expected 3 notes and found 2.

Cause: `SessionMessageQueueService.EnqueueAsync` skips a Delegation note with the same source task
and the same report digest (CARD-0320). The Block delivers the retained report, and the
sync-block note already carried that report, so no new row is written. This is existing behaviour
and not a fix defect. The plan asks only for "the parent note carrying the retained report".

Repair (test only): the count must stay unchanged, because delivering the waiting turn would write
a row with a new digest, and a caller note for the task must carry the retained body. CP-11 was
stopped while its build was running, since the tip had to change. Both kept `bin-*` directories
were deleted. No source was edited while that run was in flight.

Observation for the caller: a Block that keeps the retained report does not send the caller a new
note, because of the same-digest dedupe. The same holds for the S2 unmarked Block and for the
CARD-1472 token-only Block. The caller sees the re-Block on the task, its events and the attention
feed, not in a new session note. If the caller should be told, that needs a separate card: it is a
queue/notification decision, outside D-6.

## Final checkpoint run

CP-6 (S3 green) and CP-11 (the reply-service integration classes) run in one checkpoint run at
the tip that carries this report. Their CHECKPOINT lines are in the final message.

## Pending

- No PCs: the plan proposes none. SourceLanding Mutation owns deliberate mutants. Candidate
  mutants: drop the watermark filter, drop the `AddsNothingToRetainedReport` exclusion, or drop
  `keepRetained`.
- Next slice: S4 (closing-lines-only bodies and the bare blocked/failed arms).
