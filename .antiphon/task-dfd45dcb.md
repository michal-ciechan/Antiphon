# Final Review dfd45dcb: CARD-1486 S1 (Code task ab161f15)

Subject `feat/card-task-ab161f15` at `9daf7f39d403967b5d5069bfc6e0344f5512e24e`, base `b0c0f89b8`. Verdict: **clean**, no defects under the standing verdict policy. There are four disclosures (D-a to D-d) and one Backlog suggestion.

## Receipt (own run at the reviewed SHA, server2, checkpoint tool)

Run `20261022-155223-5560`, `start --plan .antiphon/plan-1486.md --rows CP-2,CP-11` (an unedited copy of the plan on master at 75fac630c). `wait` exit 0. `scripts/validate-checkpoint-receipt.ps1 -Evidence …/report.json -ExpectedSourceSha 9daf7f39d…`: `CHECKPOINT SOURCE VALID rows=2`, exit 0.

```
CHECKPOINT CP-2 commit=9daf7f39d403967b5d5069bfc6e0344f5512e24e build=ok filter=/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)|(AgentTaskReplyIntegrationTests*)/(Reply_after_a_retired_nudge*)|(unmarked_waiting_answer_then*)|(Second_bare_done*) executed=3 passed=3 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=9daf7f39d403967b5d5069bfc6e0344f5512e24e sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=9daf7f39d403967b5d5069bfc6e0344f5512e24e build=ok filter=/*/Antiphon.Tests.Application/(AgentTaskReplyIntegrationTests*)|(ClosingReportNudgeLifecycleTests*)|(BlockedReplyReportRetentionTests*)/* executed=347 passed=347 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=9daf7f39d403967b5d5069bfc6e0344f5512e24e sourceState=clean buildSource=verified
unlisted: none   verdict: GREEN exit=0   outputs: deleted bin-c1486-s1-green/, bin-c1486-final/
```

CP-1 is the red row. Per the brief I did not re-run it. Its record at 480b9861d (run 20261022-152359-82a0) shows 3 executed, 1 passed and 2 failed on the named assertions (`newAsk/secondAsk should not be <retired id>`), with the guard green. 480b9861d is exactly the tip without the `RearmNeverTypedClosingNudgeAsync` call, so the record is that mutant. Between 15:54 and 15:58 the owner read returned http 502 for about 3.5 minutes. The server recovered and the run completed bound.

Other runs: the documented `dotnet run --project tools/Antiphon.Checkpoints` built the tool itself, and `wait` and `validate` used that dll. No other builds or tests ran.

Checks:
- `git merge-tree --write-tree origin/master(3ac3a1d97) 9daf7f39d`: clean (tree 0f02d2f21).
- `check-evidence-diff.ps1 -BaseRef b0c0f89b8 -HeadRef 9daf7f39d`: commits=3 entries=1 violations=0.
- Tree clean.

## (1) Nudge state at Reply x arm

The re-arm runs only in `AnswerAsync`'s Blocked arm. It runs under `queue.GetLock(session)` (the answer gate) before the task is set to Working.

| Ask state at Reply | Re-arm | Outcome on the reopened turn |
|---|---|---|
| no ask | no-op | first unmarked or bare-`done` boundary asks once (unchanged) |
| Pending, never attempted | keeps the columns | the old ask is queued ahead of the Reply wire (FIFO, WhenIdle). Once typed it resolves `NudgeStillOutstandingAsync`. No second ask. |
| Pending, attempted (DeliveryAttempts>0) | keeps | late-confirm or retry types it as the bound ask. If it parks or is later canceled: D-b. |
| Sent | keeps | the next qualifying boundary settles or Blocks (bound kept, T-4) |
| Canceled, `SentAt` null (CARD-1474 retire, waiting Block) | clears all three columns | exactly one fresh ask. Witnesses `Reply_after_a_retired_nudge_rearms_the_one_ask` and `unmarked_waiting_answer_then…`. |
| Canceled after Sent | keeps (`SentAt` set) | bound kept |
| row missing, or id null with Seq set (enqueue threw) | clears | one fresh ask. See D-c for the stamp-failure sub-case. |
| legacy (Seq and Id null) | no-op | `NudgeStillOutstandingAsync` returns false, so it settles anyway. Not stranded. |
| ask from an older attempt | n/a | requeue clears all three columns (`AgentTaskService.cs:3863`, CARD-0349). The released-seat answer (`TryAcceptReleasedSeatAnswerAsync`) resumes as attempt+1 through that requeue. |

Other arms:
- Normal Reply on Working/Dispatched (overlay): untouched, correct, because the ask may be live.
- Failed/Canceled: not answerable through `AnswerAsync`. Retry goes through the requeue path above.
- Unmarked-waiting answer: this is the Blocked arm (row Canceled by `BlockUnmarkedWaitingAsync`'s `CancelAsync`), covered by the integration witness.

## (2) Two-ask race

- **Re-arm vs the flush.** The re-arm reads the row status under the same per-session semaphore that `CancelUnderLockAsync` and the flush delivery hold. A row the flush has claimed stays Pending with DeliveryAttempts>0 until its verdict, so the re-arm keeps it.
- **No Canceled-to-Sent transition.** Every Sent writer (`SessionMessageQueueService.cs` :1090, :1507, :2614, :2948, :3303) works on Pending rows or on the interrupted Sent run. Late-confirm only scans Pending lists. So a re-armed column set cannot coexist with a Pending or claimed bound row.
- **Settlement after a re-arm.** `RetireObsoleteClosingNudgeAsync` reads `task.ReportNudgeMessageId`, which is now the new id, and retires it through `CancelPendingIfUntypedAsync` exactly as before. No stale ask is typed into a settled task.
- **Test coverage.** No test settles while the re-armed ask is still Pending. That case is covered generically by the CARD-1474 `ClosingReportNudgeLifecycleTests` (in CP-11), which key on the column.

## (3) Narrowings

- **Pending/Sent keep.** Correct: clearing either would allow an unbound second ask. The residual strand is D-b. It is not reachable through the state this change claims to fix, and S3 (D-4) owns the deadline.
- **Legacy left alone.** It cannot strand, because the settle-anyway path settles it.

## (4) Red record and mutants

- **Drop the call:** red. This is the CP-1 record at 480b9861d, both witnesses on the named assertion.
- **Treat Pending as never typed:** survives every CP-2/CP-11 test (D-a).
- **Clear unconditionally:** survives every CP-2/CP-11 test (D-a).

Each test can go red: the two witnesses went red in CP-1, and the guard asserts Blocked, the same id and the counts. So none of the three tests is a test that cannot fail.

## (5) Contract and verdict scope

`git diff b0c0f89b8..9daf7f39d -- server src client` is +31 lines in `AgentTaskReplyService.cs` only: the call and the new method. The Reply contract text, `BlockedReplyContract`, the settlement arms and the nudge wording are all unchanged.

## (6) Master-only: the brief's premise is stale (D-d)

`origin/rc/2026-10-20` is now at `0714a9a89`, not `ceb6e3d6b`. It carries:
- CARD-1474: `d7d1a6864`, `d9d06b53b`, `6e79f3e13` (`RetireObsoleteClosingNudgeAsync` is at rc `:4371`)
- CARD-1472: `08cffff1f`, `da10b538b`

So G-4's strand is live on rc. Both S1 commits pass a per-commit `merge-tree --merge-base <c>^ rc <c>` with exit 0, but I have not built them on rc.

## Disclosures

- **D-a. The narrowing's guard is untested.** No test in the three classes reaches the re-arm with a Pending or Sent row. In every `BlockedReplyReportRetentionTests` Reply the sync fixture has no ask, and `AgentTaskReplyIntegrationTests` has only two `AnswerAsync` calls, both with Canceled rows. Two mutants survive the whole CP-11 scope: "treat Pending as never typed" and "clear unconditionally". This is not a finding under the policy, because every test can fail. The SourceLanding Mutation will see these survivors.
- **D-b. A kept ask can still strand.** An ask kept Pending at the Reply strands Working if it later parks or is canceled without `SentAt` (`CancelParkedIfStaleAsync`, truncation park). The re-arm runs only at the Reply, and the waiting sweep cannot reach a reopened task (G-3). This was already true before the change, and S3 owns it. It needs an exhausted-delivery ask that is still Pending across a Block and a Reply.
- **D-c. A rare second ask after a failed stamp.** Suppose `StampNudgeRowAsync` throws after the enqueue created the row (a DB fault). The columns then have Seq set and Id null, and the real Pending row is unbound, so CARD-1474 cannot retire it. A Reply clears the columns, and the unbound row plus a fresh ask can both be typed. Before this change the same state stranded Working forever. D-1 defines an Id-null ask as never typed, so the plan accepted this. It is fault-only.
- **D-d. The rc port is owed.** See (6).

## Backlog suggestion (one)

Add guard tests: a Reply with a Pending ask and a Reply with a Sent ask both keep `ReportNudgeMessageId` and send no second ask. This kills the D-a mutants. Optionally, have the re-arm bind an Id-null ask by the same latest-Delegation-row lookup that `StampNudgeRowAsync` uses before clearing (D-c).
