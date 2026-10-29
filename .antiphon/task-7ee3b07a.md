# CARD-1593 S3 Round A r3 Final Review (7ee3b07a)

**Verdict: clean.** I found no defect, no regression and no new fail-open. D-1 is closed. I ran the whole Unit lane (sharded) and the named integration classes at Full scope. Every red is explained: none reproduces when its class runs alone, at the tip or at master.

- **Subject.** `feat/card-task-6dd8c417` at `6564c5757dd0ec7c347fe50275f1334498f9e073` (task 6dd8c417). It is a fast-forward of `25e0d313c` (round 2) and `8a8b9bde3` (Round A). The real base is `fe73cfd2eed86de506a6a307e870c44b05b94451`.
- **Product code since round 2: unchanged.** `git diff 25e0d313c..HEAD` touches only `tests/Antiphon.Tests/Application/CapacityRecoverySupervisionTests.C1593S3.cs` (+30/-15).
- **Merge-tree.** `git merge-tree --write-tree origin/master(c49efbdcabd6a333f1d80b279236388c189d6f18) HEAD` exits 0 with tree `7b8a2f26e7a9b8f04003a506b9640a82fba298f9`.
- **Evidence diff.** `scripts/check-evidence-diff.ps1 -Repository . -BaseRef fe73cfd2e… -HeadRef 6564c575…` exits 0: commits=17, entries=0, violations=0.
- **Checkpoint tool not used.** The S1 D-11 reader cap still applies (plan, Checkpoints), so there are no sealed receipts. Hence `reviewedSourceClean: false`.

## Build and runs

There was one slot build: `tests/Antiphon.Tests`, `--property:OutputPath=bin-r7ee3/`, `UseAppHost=false`. It took lease `7ed4fd93`, waited 90 s, held 170 s, and had 0 errors. Every run below is `dotnet exec --no-build` of that DLL inside `scripts/build-slot.ps1`, with `TEMP=TMP=TMPDIR=/tmp`.

TRX copies are under `.antiphon/review-7ee3b07a/`. That path is gitignored and is not committed. It also holds `summary.log`, `cmp.log`, the metadata counter `unitcount.ps1` and `trx.js`.

### Named integrations (CP-1..CP-6 plus round 2's set): 332/332

One invocation: `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)|(CapacityLockOrderTests*)|(WallRerouteDispatchTests*)|(ReceiptFailureDeliveryTests*)|(AgentTaskDispatchBaseGuardTests*)|(CapacityRecoverySupervisionTests*)|(StandingRestartEscalationTests*)|(AgentSupervisionTests*)|(StandingRestartAccountingTests*)|(AgentControlServiceIntegrationTests*)|(CapacityRecoveryQueueTests*)|(CapacityRecoveryTaskTests*)/*`. Lease `2eb9d488`, 3m54s, TRX sha256 `57c6271feca527f0…`.

| Class | Passed |
|---|---|
| CapacityAdmissionLifecycleTests | 28 |
| CapacityLockOrderTests | 17 |
| WallRerouteDispatchTests | 15 |
| ReceiptFailureDeliveryTests | 21 |
| AgentTaskDispatchBaseGuardTests | 97 |
| CapacityRecoverySupervisionTests | 24 |
| StandingRestartEscalationTests | 52 |
| AgentSupervisionTests | 10 |
| StandingRestartAccountingTests | 21 |
| AgentControlServiceIntegrationTests | 33 |
| CapacityRecoveryQueueTests | 8 |
| CapacityRecoveryTaskTests | 6 |

The CP map is all green:

| CP | Selection | Result |
|---|---|---|
| CP-1 | Lifecycle 28 + LockOrder 17 | 45 |
| CP-2 | WallReroute 15 + ReceiptFailure 21 | 36 |
| CP-3 | BaseGuard | 97 |
| CP-4 | Supervision | 24 |
| CP-5 | StandingRestartEscalation 52 + AgentSupervision 10 | 62 |
| CP-6 | `C1593_S3_path_blocked_on_row_holds_no_provider_lock` | 1 |

The A-1 witness passed in this loaded combined run: `C1593_S3_REREAD tick=2.07s held=2.06s cleared=True`, test duration 2.66 s.

### Unit lane, sharded (Full)

I partitioned the lane by the first letter of the class name across every namespace. Each shard uses the lane's own predicate, `/*/*/<class OR group>/*[Category=Unit]`, so method-level Unit tests on classes with another category are included, for example `MatrixDefaultComplexityCreateTests.The_stamp_predicate…`. `--list-tests` ignores `--treenode-filter`: it listed all 20,200 entries both with and without the filter, so it cannot give the Unit count.

I checked completeness instead against an independent metadata count of the built DLL (`unitcount.ps1`, System.Reflection.Metadata). That count finds every `[Test]` whose method or class chain carries `Category("Unit")`: 468 classes, 3,881 methods, 5,978 static cases.

- **Identity.** The union of the shard TRXs has exactly 468 classes and 3,881 methods. Nothing is missing and nothing is extra.
- **Per-method counts.** Each method matches except the 9 `MethodDataSource` methods, which expand at runtime: 5,978 − 9 + 218 = **6,187**. That is exactly the executed total.

| Shard | Filter (class segment) | Total | Failed | Skipped | Duration | Lease |
|---|---|---|---|---|---|---|
| u01 | B,E,F,J,K,L,M,N,Q,U,V,X,Y,Z | 704 | 0 | 3 | 6m03s | 02f5a4ae |
| u02 | A | 481 | 0 | 3 | 1m14s | 065771bf |
| u03 | Ch | 547 | 0 | 1 | 23m46s | 709a1ad9 |
| u04 | C other (first try) | capped at 45 min, no TRX, see note 1 | – | – | – | 6bb274bb |
| u04b | C other: `(C0*)…(C9*)\|(Ca*)…(Cz*)` minus `Ch`, plus `(C_*)` | 374 | 0 | 3 | 19m23s | 4099ec29 |
| u05 | D | 580 | 0 | 6 | 1m14s | b47df3d6 |
| u06 | G,H,I | 654 | 0 | 12 | 9m54s | a523dabf |
| u07 | O,P | 753 | 0 | 8 | 6m11s | 38143bab |
| u08 | R (first try) | capped at 45 min, no TRX | – | – | – | 424efa20 |
| u08a | RemoteScriptContractTests `C1*` | capped at 45 min, no TRX | – | – | – | c151a368 |
| u08c | R minus Rem (first try) | capped at 45 min, no TRX, see note 2 | – | – | – | c151a368 |
| u08a1 | RSC `C110*` | 216 | 0 | 0 | 15m02s | 4932fd25 |
| u08a2 | RSC `C111*`..`C119*` | 50 | 0 | 0 | 7m23s | 4932fd25 |
| u08a3 | RSC other `C1*` | 112 | 0 | 0 | 36m13s | 4932fd25 |
| u08b | RSC non-`C1*` | 121 | 0 | 0 | 8m31s | c151a368 |
| u08c1 | Retired* | 37 | 0 | 0 | 20m01s | 749722f7 |
| u08c2 | Rolling* | 45 | 0 | 0 | 30m39s | 749722f7 |
| u08c3 | other R (Rea/Rec/Ref/Rel/RemoteControl/RemoteVerification/RemoteWorktree/Rep/Rer/Res/Rev/RolePolicy/Rou/Row/Run) | 498 | **1** | 0 | 6m35s | 749722f7 |
| u09 | S,T | 717 | 0 | 7 | 4m35s | 4099ec29 |
| u10 | W | 298 | 0 | 1 | 1m37s | 4099ec29 |
| **Sum** | | **6,187** | **1** | **44** | | |

- **Skips.** All 44 skips are `Skip.Test` or `SkipTestException` by construction: Windows-only behaviour (ConPTY, sharing-mode locks, drive-letter paths, the CARD-0382 Grok argv guard, CARD-1310/1318 custody, the Windows delivery profile and similar), or Codex not installed.
- **The 1 failure.** `RunnerClaudeAdapterEffortPromptTests.A_dialog_appearing_during_the_minimum_floor_is_resolved` failed with `early should be True but was False`, a timing assertion on the SessionRunner Claude adapter that is outside the diff. Alone it is green at the tip (10/10, TRX `7166bd71…`) and at master c49efbdca (10/10, TRX `2de196b2…`). It went red only while u08c3 overlapped batch 3 on a loaded host, so it is a load flake, not a regression.
- **Note 1 (u04).** My first C-other filter also listed uppercase operands such as `(CH*)`. The pinned TUnit 1.44 class match is case-insensitive, so it re-ran every `Checkpoint*` class and hit the cap. Its single printed failure, `CheckpointTrackedBaselineTests.fresh_checkout_run_executes_a_test` (OperationCanceled), came at the SIGTERM, so it is a cap artifact. That class is green in u03.
- **Note 2 (capped R runs).** Each failure printed in a capped R run is accounted for.
  - The `C1105_Resume_post_removal_census…` and `C1008_Recycle_refuses_uninspectable_git` lines are OperationCanceled at the SIGTERM. Both classes are green in u08a1–u08a3.
  - `ResilienceBudgetTests.Narrower_parent_deadlines_win` failed with elapsed 6.4 s against < 5 s (a fake-clock pump under load). It is green in u08c3 and green alone at the tip and at master (7/7 each, TRX `ad1a984f…` and `d105e7c7…`).
  - `RetiredTempContainerHostTests.C994_Removal_failure_and_crash_retry_preserve_progress` failed with exit 143 against 77 (a harness kill under load). It is green alone at the tip (21/21, TRX `92a7be84…`) and in u08c1. The master run of that class was stopped by me once the tip was green, so it is not evidence.
- **Known Linux reds from the brief.** None reproduced inside the Unit selection: those classes are either not Unit-tagged or green here.

## D-1 closed and the test change is sound

`0040616e7` moves both waiter polls, in `InDoubtStandIn` (`CapacityRecoverySupervisionTests.C1593S3.cs:1018-1034`) and `BoundedRereadStandIn` (`:1164-1182`), onto a short-lived connection with no open transaction. That removes the per-transaction `pg_stat_activity` snapshot that caused D-1. It also closes round 2's A-8.

The poll is scoped by `datname = current_database()`, and each test owns a cloned database (`TestDbFixture` is per-database, not per-schema; the class is also `[NotInParallel]`). A foreign `FOR UPDATE` waiter therefore cannot release the stand-in early.

The witness still goes red without the bound. With the pre-fix product, the reread never clears, so the stand-in releases only at its 25 s safety. The reread then sees the published commit and enqueues, which fails `elapsed < 12 s`, `WaitClearedBeforeRelease`, `HeldUntilWaitCleared` and `Adapter.Started == false`. The assertions are unchanged from round 2, whose red run at 515130f8a stands. `6564c5757` only adds a log line.

## Fresh read of the whole S3 product change

- **Lock order and no lock held across IO.**
  - The supervisor tick disposes its transaction before `TryHandleCapacityWaitAsync`.
  - The runner `ListAsync`, composition and Herdr resolve run before `BeginTransactionAsync` (`AgentControlService.cs:560-575`).
  - The reservation takes session rows `FOR UPDATE` (4), then the agent (5), then its writes, saved in `AcceptAsync` (`:1005`). Only after that does the consume take the provider advisory lock (7) and write the wait and provider rows (8).
  - `RedeemOnAsync` with the `Admitted` receipt does the provider save last. CP-6 asserts that nothing follows it except COMMIT.
  - The enqueue runs after the commit. `LandDeliveryBoundary` is a production no-op singleton (`Program.cs:493`).
  - The own-scope helpers (`ConsumeAdmittedAsync`, `ChargeFailedStartAsync`, `ReleaseAgentReservationAsync`) are 7→8 leaves that run with no reservation open.
  - Under the provider lock, `CapacityRecoveryService` only reads Agents, AgentSessions and AgentTasks (AsNoTracking). The one 4/7 cross-process cycle on re-keyed queue rows is the plan's recorded L16'/D-S3-10 item: Round B, detected as 40P01 and handled by S-T.
  - DI is sound: `AgentControlService` is scoped, and `CapacityRecoveryService`, `LandDeliveryBoundary` and `IServiceScopeFactory` are all registered, so the optional constructor parameters resolve.
- **Exactly-once D-9.**
  - S-0 skips (launch-queue owned, live resume `:591`, duplicate key, compaction) all run before any consume.
  - S-1 `capacity_consume_refused` writes nothing and neither charges nor releases.
  - S-2 and S-2h cancel the agent's ActionPending or revalidation wait with the count unchanged.
  - S-3 records the failure and then charges once. A throw after a committed consume refuses with `not-action-pending`, so there is no double spend.
  - S-T (40P01/40001/55P03 anywhere in the chain) rolls back and logs, with no backoff and no charge.
  - S-4 (gated only) disposes the reservation and rereads under `FOR UPDATE`, bounded at 120 s (the injected bound in tests). Committed means one enqueue. Not committed or timeout rethrows the original exception with its stack.
  - A commit canceled by `ct` is caught by the gated arm; the reread fails fast and the OCE propagates.
  - S-0c (card arm) consumes after the spawn and pointer save, with any failure caught and logged.
  - No launch happens without a spend on the gated arms, and no wait is stranded.
- **Standing-restart regression risk.** The ungated paths (`admission: null`) keep base semantics. The fresh pre-check still refuses an ungated fresh start while an unfinished wait exists (`standing_automatic_fresh_capacity_required`), as base did. StandingRestartEscalation 52, AgentSupervision 10, StandingRestartAccounting 21 and AgentControlServiceIntegration 33 are green, and so is the whole Unit lane.
- **Ungated narrowing** (`catch … when (capacityAdmission is not null)`, `:868`) is acceptable. An ungated commit throw propagates exactly as base did, and the C510 sync-after-commit pin requires that.
- **Changed input wait.** The only new wait is the S-4 reread. It is released by the in-doubt holder's commit or rollback, or by its 120 s `CommandTimeout`. It is not a session input wait, so parking does not apply.

## Advisories (no Review rerun needed)

- **A-6 and A-7 (round 2) still stand.** A-6: after a reread timeout, the S-3 bookkeeping is bounded by 30 s per blocked command. A-7: the card-arm `startedAt` read sits outside the S-0c `try`.
- **A-9 (process, not subject).** The `docs/testing-and-build.md` "Unit lane checkpoint shards" text says class shards miss method-level Unit tests. The mixed form `/*/*/(X*)|(Y*)/*[Category=Unit]` is verified here to select the exact Unit set, including method-level tags. TUnit 1.44 class patterns match case-insensitively, so a letter partition must not list both cases.
- **A-10 (cost).** On loaded server2 the R script classes dominate the lane: RemoteScriptContractTests about 67 min serial under `ProcessSpawnLimit` = 1, Rolling 31 min, Retired 20 min. `Ch*` takes 24 min. Shards need these split out up front. Slot waits ran 0–26 min.

## Deviations

- **Wall time.** About 7h40m (02:10–09:52 UTC), against the 4 h lane cap and the 5 h budget. Four capped shards each lost 45 min, and slot waits added up to 26 min each.
- **Sequential rule.** The last shards ran three per lease (each with its own 45 min timeout and TRX). From 08:47, batch 4 (R-other) ran concurrently with batch 3 (RSC), and the two compare runs overlapped them. I did this to finish inside the day. The one load flake (EffortPrompt) came from that overlap and is disproved alone at the tip and at master.
- **No PCs.** PCs stay pending for the method-scoped SourceLanding Mutation.

## Cleanup

I removed all 33 `bin-r7ee3` directories, by literal path, and the master `git archive` export in scratch. No git worktree was created. I did not touch the subject branch.
