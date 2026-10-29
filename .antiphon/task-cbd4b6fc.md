# CARD-1593 S3 Round A r3 Final Review (cbd4b6fc)

**Verdict: clean. D-1 is closed, there is no regression, and the whole Unit lane ran green at Full scope.**

- Subject: `feat/card-task-6dd8c417` at `6564c5757dd0ec7c347fe50275f1334498f9e073`. The owner is task 6dd8c417. Round A was a8eb1f8b (`8a8b9bde3`) and round 2 was fe424853 (`25e0d313c`). Base is `fe73cfd2e`.
- `git merge-tree --write-tree origin/master(e30ba1237) HEAD` exits 0 with tree `6b0efc2b8`.
- `check-evidence-diff.ps1 -BaseRef fe73cfd2e -HeadRef 6564c5757` gives commits=17, entries=0, violations=0.
- The product code is unchanged since the round 2 review: `0040616e7` and `6564c5757` touch only `CapacityRecoverySupervisionTests.C1593S3.cs`.

## (a) D-1 (round 2) is CLOSED, and A-8 is closed with it

- Both `BoundedRereadStandIn` and `InDoubtStandIn` now poll `pg_stat_activity` on a short-lived connection with no open transaction, so the frozen in-transaction snapshot is gone.
- The `datname = current_database()` filter is a sound scope. `IsolatedTestSchema` clones a database per test (`Database=test_...`, `TestDbFixture.cs:67`), so another test's `FOR UPDATE` waiter cannot be seen.
- If a foreign waiter did appear, the stand-in would release early. The reread would then succeed and `Adapter.Started` would become true, which turns the test red. A foreign waiter therefore cannot produce a false green.
- The witness can still go red. If the reread had no bound, nothing would clear the waiter, so the 25 s safety would fire. `WaitClearedBeforeRelease` would then be false and the elapsed time would exceed 12 s, so both asserts would fail.
- The witness under a loaded host (three lanes running) printed `C1593_S3_REREAD tick=2.05s held=2.04s cleared=True`, which is on the injected 2 s bound.

## (c) Fresh read of the whole S3 product change: no defect

- **Lock order.** The supervisor and card starts now consume inside the reservation: `RedeemOnAsync(receipt: Admitted)` takes the provider advisory lock last, after the agent and session rows.
  - I checked every `TakeProviderLockAsync` holder in `CapacityRecoveryService` (:141, 255, 343, 463, 515, 608, 668, 720, 910, 1452). They read `AgentSessions` and `Agents` only through `AsNoTracking` and never take a row lock on them.
  - The reservation writes no wait or provider row before it takes the provider lock. The base `ExecuteUpdate` on the wait inside `AcceptAsync` was removed.
  - So there is no agent-to-provider cycle. `CapacityLockOrderTests` (17, including CP-6) is green.
  - The only IO held under the in-process queue locks is the S-4 reread. Its row-lock wait is bounded by `CommandTimeout` (120, clamped when below 1; `AgentControlService.cs:1064`). That makes the bound finite, which is round 2's A-6 shape.
- **D-9, exactly once.**
  - S-0: an already-stamped key or a non-candidate wait returns before the start.
  - Pre-check: a grant mismatch or moved key gives S-1 (`capacity_consume_refused`), with no release and no charge.
  - Commit: spend and launch happen together. A rollback spends nothing.
  - S-4 lost ack: a reread under `FOR UPDATE` checks the pointer and the generation (the resume path bumps `StartedAt` through `SessionGeneration.Next`). If the start committed, it enqueues. If not, the original exception is rethrown.
  - S-3: `RecordStartFailure`, then `ChargeFailedStartAsync`. If the commit had landed, that charge refuses (`not-action-pending` or `grant-mismatch`), so the grant is never double-spent.
  - S-2/S-2h: `ReleaseAgentReservationAsync` cancels the wait and leaves `AdmissionCount` unchanged.
  - S-T: transient 40P01, 40001 or 55P03 leaves the grant reserved and takes no backoff.
  - S-0c: the card consume runs after the pointer save. A throw or refusal is logged and the launch stands (Risks 3 and 14).
- **Ungated narrowing.** With `admission == null`, `CommitOrEnqueueAsync` lets the commit exception propagate exactly as base did, and the S-3 charge is skipped.
- **Widened state gate.** `RedeemOnAsync` now accepts `Admitted && NeedsRevalidationGrant` for the Launch receipt too, which opens the dispatcher pre-claim gate. This is intended and recorded (plan Risk 5, D-S3-1), and it matches the base `RedeemAsync` semantics. BaseGuard 97, WallReroute 15 and ReceiptFailure 21 are green.
- **DI.** `CapacityRecoveryService` is a singleton (`Program.cs:774`) and `AgentControlService` has a single constructor, so the optional parameters resolve in production. `LandDeliveryBoundary.ReachedAsync` is a no-op virtual.
- **Standing restart.** StandingRestartEscalation 52, AgentSupervision 10, StandingRestartAccounting 21 and AgentControlServiceIntegration 33 are all green.
- **Changed input waits.** There are none. The only new wait is the S-4 row-lock reread. What releases it is the commit holder ending, or `CommandTimeout`, whose 120 s default is the grant bound. Parking is not involved.

## Unit lane (Full)

- There was one build of `tests/Antiphon.Tests` (`OutputPath=bin-rcbd4b6f/`, `UseAppHost=false`, lease 3cfb96b3, 152 s, 0 errors).
- Every shard was `dotnet exec --no-build` under `timeout`.
- I used three build-slot leases: lane A f397f07c, lane B dc2bc63d, lane C 96a1c810. Lane C waited 931 s for its slot.
- **Deviation from "sequentially".** The lanes ran concurrently, and the shards inside each lane ran one after another.
  - Reason: 7ee3b07a measured about 202 min of serial shard time at this same tip, before slot waits, and that exceeds the 3 h 30 min hard limit.
  - No shard went red, so the overlap caused no load ambiguity.
- **Completeness.** A `System.Reflection.Metadata` scan of the built dll finds 3,881 `Category("Unit")` methods. The TRX union over the 14 Unit shards finds 3,881 (class, method) pairs. Missing 0, extra 0.
  - That gives 6,195 results: 6,151 passed, 44 NotExecuted, 0 failed. 8 of them are the duplicate method row, so there are 6,187 unique results, which matches 7ee3b07a.
  - `--list-tests` ignores `--treenode-filter`, so I used the metadata identity check as the proof instead.
- **The 44 skips** are runtime `OperatingSystem.IsWindows()` skips (sampled: GrokRulesTransportCompatibility 12, TimeoutTests). The per-shard counts are identical to 7ee3b07a.
- **No reds, so there was no master comparison to run.** The listed known Linux reds (AgentStartRecovery, PhoneHomeMirrorJoin, CodexTranscriptTailer, CheckpointRecoveryLinux and others) have no Unit-category methods, so they are not in this lane. `RemoteScriptContractTests.C1192_Cleanup_consumer_requires_positive_absence` was green in a1.

### Raw shard summary (summary.log)
```
SHARD c1-int end rc=0 wall=236s  total: 332  failed: 0  succeeded: 332  skipped: 0  duration: 3m 53s 930ms 
SHARD a1-RSC-C1other end rc=0 wall=1693s  total: 112  failed: 0  succeeded: 112  skipped: 0  duration: 28m 10s 983ms 
SHARD b1-Rolling end rc=0 wall=1621s  total: 45  failed: 0  succeeded: 45  skipped: 0  duration: 26m 59s 037ms 
SHARD c2-Cother end rc=0 wall=1141s  total: 374  failed: 0  succeeded: 371  skipped: 3  duration: 18m 59s 942ms 
SHARD a2-RSC-C110 end rc=0 wall=772s  total: 216  failed: 0  succeeded: 216  skipped: 0  duration: 12m 51s 587ms 
SHARD c3-GHI end rc=0 wall=499s  total: 654  failed: 0  succeeded: 642  skipped: 12  duration: 8m 17s 870ms 
SHARD a3-RSC-C111x end rc=0 wall=342s  total: 50  failed: 0  succeeded: 50  skipped: 0  duration: 5m 40s 048ms 
SHARD c4-misc end rc=0 wall=310s  total: 704  failed: 0  succeeded: 701  skipped: 3  duration: 5m 08s 534ms 
SHARD b2-Ch end rc=0 wall=1299s  total: 547  failed: 0  succeeded: 546  skipped: 1  duration: 21m 38s 318ms 
SHARD a4-RSC-rest end rc=0 wall=261s  total: 121  failed: 0  succeeded: 121  skipped: 0  duration: 4m 20s 147ms 
LANE A done 2026-10-29T13:49:41Z
SHARD c5-OP end rc=0 wall=304s  total: 753  failed: 0  succeeded: 745  skipped: 8  duration: 5m 02s 394ms 
SHARD c6-ST end rc=0 wall=218s  total: 717  failed: 0  succeeded: 710  skipped: 7  duration: 3m 36s 302ms 
SHARD b3-Retired end rc=0 wall=789s  total: 37  failed: 0  succeeded: 37  skipped: 0  duration: 13m 06s 701ms 
LANE B done 2026-10-29T14:02:23Z
SHARD c7-Rlight end rc=0 wall=316s  total: 498  failed: 0  succeeded: 498  skipped: 0  duration: 5m 14s 846ms 
SHARD c8-A end rc=0 wall=80s  total: 481  failed: 0  succeeded: 478  skipped: 3  duration: 1m 18s 985ms 
SHARD c9-D end rc=0 wall=74s  total: 580  failed: 0  succeeded: 574  skipped: 6  duration: 1m 13s 363ms 
SHARD c10-W end rc=0 wall=91s  total: 298  failed: 0  succeeded: 297  skipped: 1  duration: 1m 29s 251ms 
SHARD c11-method end rc=0 wall=2s  total: 8  failed: 0  succeeded: 8  skipped: 0  duration: 1s 085ms 
LANE C done 2026-10-29T14:06:51Z
```

Shard names: a = RemoteScriptContractTests method splits (C1other 112, C110 216, C111x 50, rest 121 = 499). b = Rolling 45, Ch 547, Retired 37. c = c1-int (named integrations), Cother 374, GHI 654, misc letters 704, OP 753, ST 717, R-light 498, A 481, D 580, W 298, c11 = the `MatrixDefaultComplexityCreateTests.The_stamp_predicate_...` method row, 8 cases (all 8 also ran in misc).

Named integrations (c1-int, 332/332). These cover the CP-1..CP-6 union:

| Class | Tests |
|---|---|
| Lifecycle | 28 |
| LockOrder (includes CP-6 `C1593_S3_path_blocked_on_row_holds_no_provider_lock`) | 17 |
| WallReroute | 15 |
| ReceiptFailure | 21 |
| BaseGuard | 97 |
| RecoverySupervision | 24 |
| StandingRestartEscalation | 52 |
| AgentSupervision | 10 |
| StandingRestartAccounting | 21 |
| AgentControlServiceIntegration | 33 |
| RecoveryQueue | 8 |
| RecoveryTask | 6 |

CP map:

| CP | Classes | Green |
|---|---|---|
| CP-1 | Lifecycle 28 + LockOrder 17 | 45 |
| CP-2 | WallReroute 15 + ReceiptFailure 21 | 36 |
| CP-3 | BaseGuard | 97 |
| CP-4 | RecoverySupervision | 24 |
| CP-5 | StandingRestartEscalation 52 + AgentSupervision 10 | 62 |
| CP-6 | LockOrder `C1593_S3_path_blocked_on_row_holds_no_provider_lock` | 1 |

- **Performance.** The longest shards were RSC-C1other at 28 min and Rolling at 27 min. Both are under the 30 min Debug bar, but only just, on a loaded host. The Unit lane took 70 min wall with three lanes, against 163 min summed shard wall.
- **Receipts.** There are no sealed checkpoint-tool receipts (the reader cap, and this brief prescribes `dotnet exec` shards), so `reviewedSourceClean` is false.
- **PCs.** Every PC stays pending for method-scoped SourceLanding Mutation.

## Advisories (no rerun needed)

- **A-6 and A-7 (round 2) still stand.** The product is unchanged.
  - A-6: the tick bound after a reread timeout is about 120 s plus 30 s per blocked fresh-scope command.
  - A-7: a throw from the `startedAt` read in `ConsumeCardAdmissionAsync` takes S-3, not S-0c.
- **A-9 (new).** A throw from `ChargeFailedStartAsync` or `ReleaseAgentReservationAsync` inside the supervisor's catch blocks (`AgentSupervisorService.cs:250-285`, 611, 616) escapes to the per-agent catch in `TickAsync` and is logged as a Warning. The failure has already been recorded, and the grant expires at D-12. This is bounded and not a defect.

## Cleanup

- I removed all 33 `bin-rcbd4b6f` directories one at a time by literal path.
- Generated TRX and logs stay under the gitignored `.antiphon/rv/`.
- The subject branch was not touched.
