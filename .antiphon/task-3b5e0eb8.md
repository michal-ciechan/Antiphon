# CARD-1593 S1 Final Review round 2 (task 3b5e0eb8)

Subject: task 5dbcf333 (continues 0baad17d), branch feat/card-task-5dbcf333, tip
`0eda11bfbded7b5f764ff475fcaaa01ddbc71d2b`. Base: rc/2026-10-27 `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`.
Plan: feat/card-task-d94003bc `0c6a9c88` plus the afa47db3 amendments and the round-2 plan text on the
branch. This review ran read-only on server2 (Linux). I changed no source, test or plan file. The only
commit is this report.

**Verdict: no regression and no defect. next: land.** Round 1's D-1, D-2 and D-3, and advisories
A-1 (release guard) and A-3 (warm-reuse LostAck delivery), are fixed. Every new witness goes red on
its intended assertion at the pre-product commit `01d060ab6`. The broad dispatch, admission and
receipt selection is green apart from one inherited class.

## Runs

There was one build-slot build per tree (`bin-r3b5/` at the tip, `bin-r3b5w/` at `01d060ab6` and
`bin-r3b5b/` at base, from git-archive exports). Every row then ran `--no-build` through
`scripts/build-slot.ps1`. The TRX files are in the session scratchpad (not committed).

| Row | Selection | Executed | Passed | Failed | Skipped | TRX SHA-256 |
|---|---|---|---|---|---|---|
| CP-1 | plan CP-1 (Lifecycle + LockOrder) | 39 | 39 | 0 | 0 | 27b86ac7a118ee56c6b044d1262700044254e80775080b25a1f50fc2d1db0d53 |
| CP-2 | plan CP-2 | 11 | 11 | 0 | 0 | 29c8917deb8a208a8d756e273aada38e3d501526cf29a58300f3f41c097b1187 |
| CP-3 | plan CP-3 | 36 | 36 | 0 | 0 | c10204b0398b4971dee989729e27bceb3301e0cab9255d347129eceed7f7661b |
| CP-4 | plan CP-4 (ReceiptFailureDeliveryTests) | 21 | 21 | 0 | 0 | 62b055a787114287a4fab1ba64d3aa96a08db849550472fe6e6b49cc1c9bbb45 |
| A | 23 dispatcher classes, whole (see below) | 800 | 800 | 0 | 0 | 3af06094a58292fa1a16bb10f69d6f484a3dea50daea2c916f7f0ceaa9a98af2 |
| B | 30 capacity/recovery/delivery class prefixes, whole | 975 | 969 | 6 (inherited) | 0 | fd8f92c0855a6416cea1c7bacd1ee6223c1b00a9cef7d53cf5abd8f3c9571ce4 |
| C | 13 service/integration classes, whole | 789 | 736 | 53 (env: 53300) | 0 | a7ae226e0d9e64a0407e7a626f453b37a8e41151bdd6e2cbf8f2129c19db6eea |
| C-rerun | the 5 classes with C failures, `--maximum-parallel-tests 4` | 369 | 369 | 0 | 0 | 44fde74d41e70cffa59b9ffea135014b98f8fb1b3014e7832e50a743aebcee71 |
| Witness | `01d060ab6` (round-2 tests, pre-product): V-15 x3, consume-failure x5, warm LostAck, release receipt | 10 | 5 | 5 (expected) | 0 | d2702dcef3fc03bb1b96b0efede06563af1377832321311f1412b1ba16130779 |
| Base | `f84228f2` PhoneHomeMirrorJoinTests | 6 | 0 | 6 (inherited) | 0 | abd7084b87c6bdbf5900c8a619c2968154a78664ab0d5b3daf0aafe5e0b4e43a |

**Batch A classes:** AgentTaskDispatchBaseGuardTests, DispatchFailure*, DispatcherPredicate*,
AgentTaskPoolTests, PinnedAgentKindTests, PoolProviderProjectionStampTests, PoolClaudeEffortTests,
ClaudeEffortByRoleDispatchTests, GrokDelegateDispatchTests, ModelAvailabilityDispatcherTests,
AbsentLaunchPolicy*, DelegationDispatchRecoveryBoundaryTests, AgentTaskStandingAgentDispatchTests,
AgentNamePinTests, CodexDelegateDispatchTests, ComplexityDispatcherTests, MutationDispatchTests,
RoutingPinCandidateDispatchTests, RoutingPinDispatcherTests, TaskPlatformDispatchTests,
RemotePoolFollowUpAdmissionTests, SubscriptionQuotaGateTests, SeatDesktopJoinTests.

**Batch B classes:** CapacityRecovery{Attention,Compatibility,GrantLiveness,Persistence,Policy,Queue,Refusal,Supervision,Transcript}Tests,
CapacityWaitOrphanSweep*, ApiErrorRecoveryService*, StandingRestartEscalation*,
HerdrSupervisionBackoff*, BlockedTaskPark*, TerminalRunnerSeatRelease*, DispatchBase*,
DispatchConcurrency*, PreDispatchFailureDelivery*, TaskLaunchTokenBoundary*,
RoutingExhaustedPlacement*, DefaultRunnerReroute*, ComplexityChainRace*,
RolePolicyToolBundleDispatch*, OutputDistillationDispatch*, GrokRulesTransaction*, PhoneHome*,
LaunchEnvLayers*, CheckNoteDeliveryHandoff*, AgentTaskCheckSweep*, AgentTaskReplyOverlay*.

**Batch C classes:** AgentTaskAgentKindTests, AgentTaskCheckScheduleTests,
AgentTaskCommitChildChainTests, AgentTaskOverdueDeadlineTests, AgentTaskReplyIntegrationTests,
AgentTaskServiceIntegrationTests, AttentionServiceTests, DelegationScopeHoldTests,
VerificationRoundDeliveryTests, WorktreeDefaultContinuationTests, WorktreeRetirementRaceTests.

**How the classes were chosen:** a grep for `RedeemOnAsync`, `CapacityRecoveryWait`,
`ReleaseClaimReservation`, `LaunchCommit`, `PoolIdleSince`, `PlaceOnStanding` and
`CapacityAdmission`, plus round 1's R1 and R2 lists. My sibling-inspection prefixes matched no class.
`DispatchSiblingInspectionTests` and `DispatcherSiblingInspectionSchedulingTests` were therefore not
run this round (round 1 ran them green at `cc30bf6d2`).

**Failures explained:**
- **B: inherited Linux red.** The 6 failures are all of `PhoneHomeMirrorJoinTests` (`C1032_Join*`).
  All 6 fail the same way at base: `JoinStoreMismatch` is missing the `runner_store_mismatch`
  warning, `JoinIdleRecipient` has an empty `BindingId`, and `Busy` and `CrashRetry` hit the 2-minute
  timeout.
- **C: environmental.** All 53 first-pass failures are Postgres `53300 too many clients` or EF's
  "transient failure" wrapper on the shared server2 database. An unthrottled rerun got worse
  (68 x 53300). With `--maximum-parallel-tests 4`, the same 5 classes ran 369/369 green.
- **Witness: all red as intended.** The 5 reds at `01d060ab6` fail on the intended assertions:
  - V-15 `pool-session-exit` and `standing-session-exit`: `stored.AgentSessionId should not be <S>`.
  - `commit-server-error`: `Failed but was Queued`.
  - `release_leaves_a_start_accepted_receipt`: `StartAccepted but was Canceled`.
  - `warm_reuse_lost_commit_ack_delivers_the_brief`: no queued brief.

  V-15 `stop` and the 4 older consume modes stay green there, as the plan says.

**Evidence check:** `check-evidence-diff.ps1 -Repository . -BaseRef f84228f2 -HeadRef 0eda11bf`
reports 7 commits, 0 entries and 0 violations.

**Performance:** batch B took 24.5 minutes. That is under the 30-minute Debug threshold, but it is
the heaviest selection. No row exceeded 30 minutes.

## Priority 1: regression and test strength

**ReceiptFailureDeliveryTests (21).**
- **The old fault:** an `InvalidOperationException` on the post-dispatch `StartAccepted` save
  (L5). The claim and launch had already committed, and only the receipt bookkeeping failed. The
  class proved that a receipt-write failure never costs the brief: it is delivered once, in order,
  after the queue cut, with the wait left `Admitted`.
- **Why that cannot be modelled now:** S1 removes L5. The receipt is written in the claim commit,
  so a post-launch receipt failure no longer exists.
- **The new fault** is a one-shot `40P01` on that in-claim save. Tick 1 asserts:
  - the task is `Queued` with no failure reason and no bound session;
  - the wait is `ActionPending` with AdmissionCount 0 and LaunchSessionId null;
  - no message is queued.

  Tick 2 dispatches once, and the wait is `StartAccepted` with `LaunchSessionId == session` and
  AdmissionCount 1.
- **Not weakened:** the delivery, queue-cut and transcript `UserPrompt` assertions after that are
  byte-identical to base. The diff touches only the tick-1 block, the fault type and the
  wait-state lines.
- **Behaviour change vs base:** a NON-transient fault on that save is now D-9 class 4. The task
  fails and the brief is never delivered, where base delivered it. That is the plan's design, and
  lifecycle `deterministic-consume-failure` (XX000) witnesses it.

**C540 (`.SiblingWarnings.cs:218-235`, `:256-286`).**
- A commit `IOException` now keeps the task `Queued`. There is no failure, no Dispatched event and
  no warning intent.
- The next tick then dispatches once (two for the custody case), with no Failed event.
- The before-SaveChanges throw still fails the task, and cancellation still stays Queued.
- These are stronger than before: they add the next-tick dispatch.

## Priority 2: F1/D-2 live-session recheck (`AgentTaskDispatcher.cs`)

- **The reread is fresh.** `SessionIsLiveAsync` (`:9495-9498`) is a new `AsNoTracking`
  `AnyAsync(Status in Starting|Running)`. The claim transaction is the default READ COMMITTED
  (`:5541`), so the read issued after `LockSessionKeyShareAsync` (`:8481`, `:8749`) sees the
  committed state of any writer that held S `FOR UPDATE`. It is not a stale tracked copy.
- **Nothing is spent on these exits.** Both exits return before any row edit, X lock, parallel key
  or provider lock. Rule P holds, because nothing is written under rank 7.
  - `SpawnFresh` falls through to the cold launch in the same transaction, and order 3->4->5->6
    holds.
  - `WaitForAgent` rolls back (`:5759-5767`) and releases nothing.
- **No new loop.** For a standing agent, the post-lock rule is `AlwaysOn ? WaitForAgent : SpawnFresh`
  (`:8751`), identical to base's pre-lock rule (`:8673-8674`). An AlwaysOn agent whose session is
  dead waits for the supervisor, exactly as base does; the post-lock check only covers the race
  window.
- **Pinned warm reuse** returns `WaitForAgent` only when the reloaded agent still has another live
  session; otherwise `SpawnFresh` (`:8482-8488`), matching base's pick-time rule.

## Priority 3: D-9/A-3 classification (`:8143-8170`) and the A-4 guard

The commit-catch branches:
- **Reread `Dispatched` on this session:** `LostAck`. This is the only way to detect a commit that
  landed (with 08006, the reread runs on a reopened connection).
  - Cold path: returns `Dispatched`, and interrupted-launch recovery launches.
  - Warm path: now delivers the brief (`:5747-5750`).
- **Transient SQLSTATE, or a non-Postgres fault, that rereads `Queued`:** `Refused`, so the task
  stays Queued. This is safe even if an in-doubt commit lands later: the task becomes
  `Dispatched` + `StartAccepted` on a `Starting` session, and recovery relaunches it. There is no
  double spend, because the next claim's `FOR UPDATE` serializes behind the in-flight commit.
- **Any other `PostgresException` at COMMIT:** the server rejected the commit, so it definitely
  rolled back. The code releases on a fresh leaf transaction and rethrows. The caller's
  `FailCapacityLaunchAsync` abandons, releases again (idempotent, because the wait is already
  Canceled) and fails the task. No reservation leaks.
- **Cancellation:** propagates, the transaction is disposed, and the task stays Queued.

The A-4 guard (`CapacityRecoveryService.cs:428-430`) is on the only shared method. There are two
callers:
- `CommitClaimWithoutLaunchAsync` (in-claim, wait `ActionPending` before any consume).
- `ReleaseFailedReservationAsync` (class-4 leaf).

A committed `StartAccepted` receipt is no longer cancelled. `C1593_release_leaves_a_start_accepted_receipt`
witnesses this, and it goes red pre-product.

## Priority 4: lock order and exactly-once

Round 2 adds no lock acquisition: only the plain read above, the release guard and the warm-path
delivery. I re-checked the touched rows:
- L1w: `:8480` previous owner (3), `:8481` S KEY SHARE (4), live reread, `:8490-8491` X FOR UPDATE
  and reload (5), then `RefuseParallelAsync` (6).
- L1s: `:8749` (4), reread, `:8752` (6).
- L1 cold: `:6192-6193`, flush then consume (7, 8) and commit.
- L2 gate: `:966` (`consume:false`).
- Release: `:8107-8119` (in-claim) and `:8197-8212` (fresh 7->8 leaf after the abandon).

Round 1's confirmation of the other rows still applies, because the round-2 diff does not touch
them.

The witnesses:
- **V-8** is the red-at-base witness for the S1 X lock, as round 1 verified.
- **V-10 continuation** is re-seated on an unpinned warm-reuse claim. It now asserts that the claim
  holds X (`AssertClaimHoldsWarmAgentAsync`), so the continuation's X write really is behind it.
  It is a structural guard: it cannot go red at base by construction (D-3 fix option 2).
- **V-15** now uses `PollInClaimSessionWaitAsync`, which requires the blocked backend to hold the
  `AgentTasks` row lock. So it is a real in-claim wait, and it was deterministic across 2 runs (CP-1
  and the witness run).

## Advisories (no rerun needed)

- **A-1. Deterministic non-Postgres commit faults have no retry bound.** Such a fault rereads
  `Queued` and keeps the task Queued on every tick, with one deduplicated warning. Base failed the
  task. Realistic commit faults are I/O and transient, so this is low risk. Consider a per-task
  bound in S2.
- **A-2. The transient set is narrow** (`40P01`/`40001`/`55P03`). A `57P01`/`57P03` (server
  restart) arriving as a `PostgresException` at COMMIT, or during the in-claim consume, is class 4
  and fails the task. Base also failed claims on these, so this is not a regression. 53300 was seen
  live on this host today.
- **A-3. Unlocked session-status writers are not serialized by `FOR KEY SHARE`.** These writers
  are `TerminalRunnerSeatReleaseService.cs:946` (`ExecuteUpdate`), `RunnerSlotService.cs:364` and
  `RunnerRetireService.cs:115`. Their implicit FOR NO KEY UPDATE does not conflict with KEY SHARE,
  so the F1 recheck closes the window only for writers that lock S `FOR UPDATE`, such as the exit
  observer. This is a residual race that base also has.
- **A-4. Two fallback branches have no witness:** AlwaysOn standing with a dead session ->
  `WaitForAgent`, and a pinned warm agent whose dead session has a live sibling -> `WaitForAgent`.
  Both mirror base pre-lock rules.
- **A-5. Misleading warning text.** `LostAck` writes the warning `capacity consume refused: <label>`
  (`:8157`), but the consume landed.
- **A-6. Redundant guard clause.** The `LaunchSessionId is not null` clause of the A-4 guard is
  redundant: no production path puts a wait in `ActionPending` with a launch session. It is
  harmless.
- **A-7. Stack trace lost (carried from round 1).** `FailCapacityLaunchAsync` uses `throw ex;` at
  `:8183`, which loses the stack trace.
- **A-8. Inherited Linux red.** `PostLandMutationCustodyTests.C478_*` is still red at base, and all
  6 `PhoneHomeMirrorJoinTests` cases are red at base on Linux.
- **A-9. Author detail file missing.** `.antiphon/task-5dbcf333.md` is not on the branch, so I took
  the CP filters from the plan's `### Checkpoints`.

## Not run

- The whole Unit lane: the brief excluded it. `ordinaryScopeCompleted` is therefore Interim, not
  Full.
- PCs: all stay pending for SourceLanding Mutation.
- Sealed checkpoint-tool receipts: unavailable because of the plan's 64 MiB reader cap (D-11), so
  `reviewedSourceClean` is false.
- `merge-tree`: not run (brief). The base is an ancestor of `origin/rc/2026-10-27` (`f30fddfd`),
  which is 23 commits ahead.
- Cleanup: the `bin-r3b5*` outputs and the scratch exports remain, because the brief said to
  delete nothing.
