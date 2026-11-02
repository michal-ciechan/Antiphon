# CARD-1503 Review round 1 (task 9da7d9cf): defects found

Subject: feat/card-task-ab00c63e tip dae23c2dbfe58053c20af3fb7a2b869fc2061046, range
5376ac529987b9e7b2019773cb4d784cba8007bf..dae23c2dbfe58053c20af3fb7a2b869fc2061046 (5 commits; 2 files).
Code landing owner: ab00c63e (full GUID not in the brief; the caller has it).

## Verdict

The retained-field invariant holds and the witnesses are not vacuous. But this change makes a question
asked after a Reply SILENT to the caller and hides it on the operator's attention surfaces. Before the
change, that question reached both. That is a regression (D-1, D-2). Not landable.

## Defects

**D-1: a question-Block after a Reply never reaches the caller (silent Blocked task, regression).**
- Where: `server/Application/Services/AgentTaskReplyService.cs:4068` (the question arm returns `task.Result!`
  as the settled body), then `:3064` (`EnqueueParentNoteAsync` digests `report` = the retained body) and
  `server/Application/Services/SessionMessageQueueService.cs:751-779` (CARD-0320: drop a same-SourceTaskId,
  same-ContentDigest Delegation note, or one whose digest equals `AgentTasks.CompletionNoteDigest`; that
  stamp is written once, at the first note, `CompletionNoteStamp.cs:28-32`).
- Failure: the task is Blocked by a runner sync, so its marked report was already delivered. The caller
  Replies. The delegate's next turn ends unmarked with "Should I also port the fix to the rc branch?". The
  settlement Blocks with the retained report, its digest equals the delivered note's digest, and the queue
  drops the note. The caller gets no note, so it sees neither the question nor the `callerWarning` line
  (`:1369`). Parking is off by default (CARD-1083), so `owesCompletion` is false for a Blocked task and there
  is no durable obligation. Nothing releases or re-surfaces the task: a stall that nobody is told about.
- Why it is this change: the review probe (uncommitted, scratch worktree, same build) shows it.
  `R1503Probe_question_after_reply_reaches_the_caller`: at the tip, caller notes before=1 after=1 (red).
  With the pre-change classifier arm restored (mutant M1), before=1 after=2, and the note contains the
  question (green). The worker's own memory note says the same thing ("callerWarning never delivered").
  The token-only (CARD-1472) and unmarked-ack (CARD-1486 S2) arms already had this property. That was
  tolerable there because nothing was asked. For a question it turns an answered-path into a stall.
- Fix: give the retained-question settlement a distinct note identity. For example, compute the
  parent-note digest over the report plus the settlement event id (or the question) for retained-body
  settlements, for both the queue dedupe and the durable row. Keep the CARD-0320 replay dedupe for the same
  settlement. Add a producer-to-recipient witness: after the question settlement, the caller session's
  queue holds one new Delegation note for the task, and that note names the question.

**D-2: the operator surfaces show the retained report as "the question".**
- Where: `server/Application/Services/BlockedContextBuilder.cs:95-103` (`AttentionPrimary`) and `:112-131`
  (`QuestionAndContext`, the Blocked drawer's current question). Both read `task.Result`, which is now the
  retained report. Also `AgentTaskReplyService.cs:2994-2998` (`BlockedNote.Format` asks/reason, built from
  `report` = the retained report; it reads `reason: marked-blocked — ended with the blocked token`, which is
  false here).
- Failure: the probe `R1503Probe_question_after_reply_is_the_attention_primary` is red at the tip. The
  primary is "Implemented CARD-1472 in the runner mirror …|--- next stage ---|next: review|handoff: continue",
  not the question. Under M1 the primary is the question (green). The question survives only in the event
  log: the Blocked event "Delegate asked: …" and the new Warning.
- Fix: when the latest Blocked event is the question Block (for example, `RetainedReportStands` plus an
  `AskedPrefix` Blocked detail), derive `asks`/question/attention from that event
  (`BlockedQuestion.QuestionFromEventDetail`) instead of `task.Result`. Give the note a question reason
  word. Witness it with AttentionPrimary/BlockedContext plus the note `asks:` line.

## What holds (verified)

- Invariant: the question arm on `RetainedReportStands` returns (Blocked, Marked, retained).
  `retainedQuestion` is a strict subset of `retainedUnmarked`, so Result, evidence, handoff, deliverable and
  result file all skip recomputation. A NEW marked report still takes the `done`/`blocked`/`failed` arms
  above. A first unmarked question with no retained report keeps the old path byte-for-byte (QuestionHeuristic,
  `Delegate asked:` Block). The non-question unmarked arm is unchanged. The settlement still ends Blocked,
  with the question in the Blocked event (same `BlockedEventDetail` bounding as before) and the Warning.
- Witnesses: the Unmarked_question witness was red pre-fix per the worker. I reproduced the same red with
  mutant M1 (evidence QuestionHeuristic, expected Marked). M2 (Warning dropped) is red: `events.Single` finds
  no match. M3 (the retained-field skip disabled for retainedQuestion) is red on NextStage. The First_unmarked
  witness is green.

## Advisories

- A-1: the caller warning carries the raw last paragraph of the delegate's question, uncapped and not
  flattened. It sits in the warning area, which is presented as server text, while the note's `asks:` line
  caps at 240 characters and flattens. This is latent while D-1 holds. Route it through
  `BlockedNote.ExtractAsks` when fixing D-1.
- A-2: M3 is caught by NextStage. No assertion pins DeliverablePath independently, because the fixture
  values are equal both ways.
- A-3: "with a question (N characters)" counts the whole turn body, not the question.
- rc: `git merge-tree` onto origin/rc/2026-11-01 (10a826b9d) is clean, and rc carries CARD-1486 S2
  (`RetainedReportStands`). An rc port is optional, not required. Master-only stays the default once
  D-1/D-2 are fixed.

## Evidence

Checkpoint run 20261102-164225-cfe8 (plan `.antiphon/r1503/plan.md`, gitignored):
- CHECKPOINT CP-1 commit=dae23c2dbfe58053c20af3fb7a2b869fc2061046 build=ok filter=/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/* executed=21 passed=21 failed=0 skipped=0 slot=granted sourceState=clean buildSource=verified
- CHECKPOINT CP-2 commit=dae23c2dbfe58053c20af3fb7a2b869fc2061046 build=reused filter=/*/Antiphon.Tests.Application/(AgentTaskReplyIntegrationTests*)|(AgentTaskReplyOverlayTests*)/* executed=362 passed=362 failed=0 skipped=0 slot=granted sourceState=clean buildSource=verified
- verdict GREEN exit=0, unlisted none, wall 17m45s.

Unlisted runs, each with a stated reason and each under build-slot: one mutant/probe build of
tests/Antiphon.Tests in a detached scratch worktree at dae23c2db, with env-switched mutants M1/M2/M3 and two
probe tests (none committed). Runs: M0 4 tests (2 probes red, 2 witnesses green); M1 4 (witness red, probes
green); M2 1 red; M3 1 red. Also one LandGraphAudit Release build for `-Check`. The scratch worktree was
removed afterwards.

Gates:
- land graph `-Check`: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE.
  I did not run `-Update`.
- check-evidence-diff over 5376ac529..dae23c2db: commits=5, violations=0, exit 0.
- `git merge-tree --write-tree` onto origin/master 6dbd129b7 is clean; onto origin/rc/2026-11-01 10a826b9d it
  is clean.
