# CARD-1486 S2 Final Review - task 462e03ac

Subject: Code task f5e7bd9a-7389-4394-940a-70df58399d97, `feat/card-task-f5e7bd9a` at 5d4d9802519741995d0bdb3689312e74d08f7bdb (base 50a18c244). Verdict: clean, land.

## Receipt (own run, at the reviewed SHA)

Run 20261022-175703-e07f, manifest `.antiphon/c1486-s2-review.md` (ignored; one shared build `bin-c1486-rev/`, deleted by the tool). `validate-checkpoint-receipt.ps1 -Rows CP-4,CP-12,CP-11`: `CHECKPOINT SOURCE VALID source=5d4d98025 rows=3`, exit 0. Tree clean afterwards.

| Row | Filter | executed | passed | failed | skipped |
|---|---|---:|---:|---:|---:|
| CP-4 | `(BlockedReplyReportRetentionTests*)\|(AgentTaskReplyIntegrationTests*)/(Unmarked_ack_after_the_ask*)\|(unmarked_waiting_answer_then*)` | 2 | 2 | 0 | 0 |
| CP-12 | `BlockedReplyReportRetentionTests/(Reply_with_a_*)` | 2 | 2 | 0 | 0 |
| CP-11 | `(AgentTaskReplyIntegrationTests*)\|(ClosingReportNudgeLifecycleTests*)\|(BlockedReplyReportRetentionTests*)/*` | 350 | 350 | 0 | 0 |

`source state=clean buildSource=verified`, `unlisted: none`. The scratch mutation runs below are outside the table. They are stated review mutations, run through `scripts/build-slot.ps1`.

## (1) The rule: classifier outcomes (non-Check/Distill/Diagnose roles)

"Reopened" means `RepliedAtSequence != null`. It is set only by AnswerAsync's Blocked arm and cleared by Requeue.

| Turn | Nudge state | First attempt | Reopened, retained Marked + non-empty Result | Reopened, retained not Marked (UnmarkedWaiting/QuestionHeuristic/UnmarkedAfterNudge/FinalMessageMissing/Exempt) or empty Result |
|---|---|---|---|---|
| marked `done`/`blocked`/`failed` + body | any | Succeeded / Blocked / Failed with the NEW body (unchanged) | same: the NEW marked report settles | same |
| token-only (verdict, empty body) | any | CARD-1472 arm if Result is non-empty, else the normal verdict arms (unchanged) | CARD-1472 arm: `done` asks once, then Blocks with retained; `blocked`/`failed` keep retained (unchanged) | CARD-1472 arm when Result is non-empty (unchanged) |
| unmarked, trailing `?` | any | Blocked/QuestionHeuristic, body replaces Result | **unchanged: Blocked/QuestionHeuristic, the question REPLACES the retained Result** (D-6 out of scope; see Disclosure 1) | unchanged |
| unmarked ack / substantive / empty, live session | `ReportNudgedAt` null | ask once, stays Working | ask once, stays Working | ask once, stays Working |
| same | ask outstanding (untyped, same boundary, inside window) | Working (null) | Working (null) | Working (null) |
| same | ask spent (typed, later boundary) or session dead | Succeeded/UnmarkedAfterNudge (or FinalMessageMissing) with the new body (unchanged) | **NEW: Blocked/Marked, Result = retained, every retained field kept; event and caller warning `Turn #N ended unmarked (L characters) ... the retained report (marked, R characters) stands`** | Succeeded/UnmarkedAfterNudge with the new body (unchanged; the scope guard proves it, and mutation C makes it red) |

Nudge state across a Reply (S1 `RearmNeverTypedClosingNudgeAsync`): a missing row or a Canceled-never-typed ask is cleared, so the reopened turn gets a fresh ask. A Pending or Sent ask is kept; CP-12 is green, and M1/M2 turn it red. With a kept Sent ask, the first unmarked turn after the Reply goes straight to the spent arm. Under S2 that turn Blocks with the retained report without a fresh ask on this attempt (the one-ask bound, T-4).

An empty unmarked turn on a reopened Marked row now also Blocks with the retained report. Before S2 it settled Succeeded with an empty Result. This is an improvement.

## (2) The behaviour change: recoverable, not stranded, nothing lost

- The task ends **Blocked**, not Working. The session and seat are kept. Parking is off (`BlockedTaskParking:Enabled`=false, CARD-1083), so nothing releases it automatically. A Reply, a Cancel or a caller action releases it.
- Next step: Reply again (for example "send the complete report with the closing line"). The wire restates the contract. A marked report then settles with the new body. Another unmarked turn Blocks again at once with no ask, because the typed ask is kept. If the session is dead, Reply refuses ("session no longer available"); Requeue or Cancel the task.
- The substantive unmarked body is preserved in the transcript. The caller's Blocked note carries the retained report plus the warning naming `#seq`, the length and "Read that turn in the transcript". The body text itself is not copied into Result or the note. Surfaced, not silent.
- For a runner task, PrepareRemoteAsync and the progress checks are skipped on this arm. Nothing merges, and the pushed branch stays as it is. No work is lost.

## (3) SettleAsync `retainedUnmarked`

- The predicate is `verdict=="" && Blocked && Marked && RetainedReportStands`. This is the classifier's only unmarked arm that returns Marked, and the remote-sync block applies only to Succeeded.
- What it writes: `Result` = task.Result (the same value), `ReportEvidence` = Marked (the same). `FailureReason` is not written (null). `CompletedAt`/`ConcurrencyToken`/`Status` are re-stamped, as on the CARD-1472 token-only arm. Cost is rolled up.
- What it skips: `ResultFilePath`, `NextStage`, `NextHandoff`, `DeliverablePath`/`Ref` and the bundle. The nudge columns are not touched in SettleAsync.
- Re-settlement is idempotent: the same values come back, plus one more Blocked event.
- CARD-1474 `RetireObsoleteClosingNudgeAsync` runs in `afterPersist` unconditionally, so it also runs for this Blocked settlement.
- Mutation B shows the guard is load-bearing: the retained sync-block handoff `Decide` was recomputed to `Review`.

## (4) Red record and mutations (scratch detached worktrees at 5d4d98025, all removed)

The filter for every run was `(BlockedReplyReportRetentionTests*)|(AgentTaskReplyIntegrationTests*)/(Unmarked_ack_after_the_ask*)|(unmarked_waiting_answer_then*)|(Reply_with_a_*)`: the witness, the scope guard and the 2 S1 guards.

| Mutant | total/failed/skipped | Red (expected) | Green |
|---|---|---|---|
| A: remove `RetainedReportStands` from the unmarked arm, plus M1 (Pending treated as never typed) | 4/2/0 | witness: `after.Status should be Blocked but was Succeeded` (:307); Pending guard: `ReportNudgeMessageId ... but was null` (:244) | Sent guard, scope guard |
| B: remove `retainedUnmarked` from SettleAsync (all 3 uses), plus M2 (rearm clears unconditionally) | 4/3/0 | witness: `AssertRetained` `after.NextStage should be Decide but was Review` (:332 via :309); Pending guard (:244); Sent guard (:265) | scope guard |
| C (my choice): drop `ReportEvidence == Marked` from `RetainedReportStands` | 4/1/0 | scope guard `unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report`: `settled.Status should be Succeeded but was Blocked` (:942) | witness, both guards |

- I did not re-run the witness at 49550f80f. Mutant A, which removes the fix arm at the tip, reproduces the Code task's stated red (Succeeded) at the same assertion.
- I did not run the guards on master separately. They are green at the tip (CP-12). Master's S1 code is patch-id-identical to the carry (21575ca5/a9396c3b), and S2 does not touch the rearm.
- Every new test can fail.

## (5) Carry and landability (origin/master ebec1ecfe)

- The whole-branch `merge-tree --write-tree origin/master HEAD` exits 1. That is the expected S1-carry conflict.
- Scratch `git rebase --onto origin/master 50a18c244` exits 0. It dropped b69566496 and 78971ae1b ("patch contents already upstream"; patch-ids 21575ca5 = 27e994af1 and a9396c3b = 8b7edcdde). It replayed the 4 S2 commits. The rebased S2 code and test diff is byte-identical to `78971ae1b..5d4d98025`.
- `check-evidence-diff.ps1 -BaseRef 50a18c244 -HeadRef 5d4d98025`: commits=6 entries=1 (the report .md) violations=0.

## (6) rc (origin/rc/2026-10-20 0714a9a89)

- Cherry-picking 27e994af1, 8b7edcdde, 4152978e1, 49550f80f and febdf83df in that order gives exit 0 for each. The result touches 3 files (ReplyService, AgentTaskReplyIntegrationTests, BlockedReplyReportRetentionTests). It touches no plan doc. The report commit 5d4d98025 is dropped.
- The rc pick must carry S1 (27e994af1 + 8b7edcdde) first. S2 does not apply without it.
- I did not compile it on rc.
- The plan's "master-only" RC verdict is stale, because rc now carries CARD-1472/1474.

## Disclosures (no defect under the verdict policy)

1. After a Reply, a question-shaped unmarked turn (trailing `?`) still Blocks as QuestionHeuristic and replaces the retained marked Result. This is pre-existing and explicitly out of scope (G-2, D-6). S2 does not touch or worsen it. Backlog suggestion: extend D-3 retention to the `?` arm, or file the question as a Blocked event and keep Result.
2. The disclosed behaviour change: a substantive unmarked report after a Reply to a marked report Blocks with the old report. It fails closed: Blocked, a warning naming `#seq` and the length, and the transcript is preserved. It strands nothing and loses nothing.
3. When `turn.FinalMessageMissing` holds, the later FinalMessageMissing branch overwrites `callerWarning`. The caller's note then shows the final-message warning instead of the "retained report stands / #seq" line; the Blocked event still carries that line. The same pre-existing pattern applies to the CARD-1472 token-only arm.
4. A Blocked task waiting for a Reply has no automatic release deadline while parking is disabled (CARD-1083). A Reply, Cancel or caller action releases it.
