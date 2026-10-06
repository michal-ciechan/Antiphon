# CARD-1108 S2 Final Review

Outcome: clean. No regression of existing behaviour and no reachable fail-open. The complete S2 ordinary scope (plan rows CP-6..CP-12) plus the registry guard reran green at the reviewed SHA in one checkpoint-tool run, and a local PC-3 mutation of the gate went red on the named method and was reverted. Three disclosures are on CARD-1129 (Backlog), none a defect under the verdict policy.

- subjectTaskId: `8c28c2c8-0794-44d0-804b-46c59a6de44e` (Code owner; landing owner unchanged)
- Reviewed ref: `refs/heads/feat/card-task-8c28c2c8` at `cee20ce9041b2cea9ff86fa7372163ffc75138ee` (tested source `a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2`; the tip adds the report commit; parent `9463e1fba9a086b152df7517cb892f5f6c7e3fd2`)
- Review branch: `feat/card-task-d18e99d2`, worktree `/work/worktrees/task-d18e99d2`, same tip plus this report
- Round: Final (profile v1). ordinaryScopeCompleted: Full. PC-1..PC-7 and the carried CARD-1065 controls stay pending for method-scoped SourceLanding Mutation.
- Master: `origin/master` is `f4ae747a6b8ab6f8f8aeee025cfbd0bcafdeb1b4` (one commit past the base, CARD-1122 test). `git merge-tree --write-tree --name-only f4ae747a6 cee20ce90` exit 0, no conflicting path; the branch merges cleanly without a rebase.

## Checkpoint audit against the plan

The plan's `### Checkpoints` has seven S2 rows, CP-6..CP-12. The Code report records all seven from run `20261006-225557-e8c0` at `a4186c148` with `sourceState=clean buildSource=verified`: 6, 39, 12, 7, 7, 13, 17, failed 0 skipped 0. The seven CHECKPOINT lines in the Code report are byte-identical to the tool's own `report.md` in the Code worktree (`/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/`), and the tool footer reads `unlisted: none`. TRX rosters there equal the plan's expected sets, not merely Min: CP-6 six reclaim methods, CP-7 39, CP-8 Delivery 4 + Release 3 + Resume 3 + SyncRecovery 2, CP-9 Projection 2 + Publication 3 + RunnerIdentity 2, CP-10 Lifetime 6 + Registration 1, CP-11 DeferredKill 3 + Endpoint 8 + Rules 2, CP-12 17. No row missing, no zero count.

Unlisted runs each carry a reason and a slot lease. The Code session transcript (`GET /api/sessions/ba1d7f44-5385-4024-9964-2b0974ce7ab3/transcript`) shows seven `build-slot.ps1` labels: `c1108-red`, `c1108-red-test`, `c1108-green`, `c1108-green-test`, `c1108-green-projection`, `c1108-checkpoint-bootstrap`, `c1108-registry-guard`, one per driver (two test builds, four test runs, the driver build); the checkpoint tool took its own slots. No driver ran unleased. The red-first measurement is real: the session's terminal log for lease `0be7267f-1611-41cc-8d41-3b80a7b57fc6` reads `failed C1108_ScheduledSweepIsGatedAndBoundedPerRun`, `first.Visited should be 5 but was 15`, `Additional Info: G-4`, total 1 failed 1 succeeded 0 skipped 0.

Evidence guard over the reviewed range: `EVIDENCE result commits=2 entries=1 violations=0` (the one entry is the Code report markdown).

## Independent rerun at the reviewed SHA

One hand-made eight-row manifest (CP-1..CP-8 map to plan CP-6..CP-12 plus the registry guard), one checkpoint-tool run `20261006-232024-3cf0` (`start --plan ... --serial --expected-source-sha cee20ce90...`), one isolated build into `bin-c1108r2-review/`, every row serial with its own slot lease. Platform read before the run: `GET /api/runner-defaults` revision 2 names global runner `server2`; `GET /api/session-runners` shows `server2` draining and not accepting, `server2-temp` (Linux, 5/10) and `desktop` (Windows, 0/2) accepting. No runner pin, no platform pin. `validate --evidence report.json --expected-source-sha cee20ce90...` printed `CHECKPOINT SOURCE VALID source=cee20ce9041b2cea9ff86fa7372163ffc75138ee rows=8`. The tool deleted `bin-c1108r2-review/` on green; the driver output `bin-c1108r2-drv/` and the mutation output `bin-c1108r2-mut/` (28 directories) were removed with a root-confined rm; `git status` is clean.

```
CHECKPOINT CP-1 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=ok filter=/*/*/BlockedTaskParkReclaimTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=164.5044133s startup=36.8676287s testsWall=56.3502807s teardown=1.2443179s hostWall=94.4622367s
CHECKPOINT CP-2 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=39.05014s testsWall=48.5107434s teardown=1.8831271s hostWall=89.4440101s
SLOW CLASS Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests 1105s tests=39 (CP-2)
CHECKPOINT CP-3 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(BlockedTaskParkReleaseTests*)|(BlockedTaskSyncRecoveryTests*)|(BlockedTaskParkResumeTests*)|(BlockedTaskParkDeliveryTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=41.6452252s testsWall=271.1911536s teardown=1.8361889s hostWall=314.6725677s
SLOW CLASS Antiphon.Tests.Application.BlockedTaskParkReleaseTests 97s tests=3 (CP-3)
SLOW CLASS Antiphon.Tests.Application.BlockedTaskParkDeliveryTests 96s tests=4 (CP-3)
CHECKPOINT CP-4 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(TaskParkPublicationTests*)|(TaskParkRunnerIdentityTests*)|(BlockedTaskParkProjectionTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=42.7633796s testsWall=25.2957368s teardown=1.3186381s hostWall=69.3777542s
CHECKPOINT CP-5 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(DispatcherSweepLifetimeTests*)|(DispatcherSweepLifetimeRegistrationTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=41.2107946s testsWall=9.1740128s teardown=1.3974997s hostWall=51.7823069s
CHECKPOINT CP-6 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(RunnerSlotEndpointTests*)|(PhoneHomeDeferredKillTests*)|(RunnerSlotRulesTests*)/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=0s startup=49.8417141s testsWall=10.0780207s teardown=1.488353s hostWall=61.4080878s
CHECKPOINT CP-7 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/RunnerSeatOrphanSweepTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=0s startup=51.2223564s testsWall=37.6173585s teardown=2.3902053s hostWall=91.2299196s
SLOW CLASS Antiphon.Tests.Application.RunnerSeatOrphanSweepTests 334s tests=17 (CP-7)
CHECKPOINT CP-8 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-d18e99d2/.antiphon/checkpoints/20261006-232024-3cf0/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=7.3567601s testsWall=1.9965233s teardown=1.0461299s hostWall=10.3994129s
unlisted: none (the tool ran no other build or test command)
wall: 15m53s  sequential-equivalent: 13m07s  builds: 1  max-concurrent-builds: 1  rows: 8 green 0 red 0 skipped
outputs: deleted bin-c1108r2-review/
verdict: GREEN exit=0
```

TRX rosters equal the expected sets: CP-1 `C1065_LegacySweepRequiresFreshPublicationAndIdleWindow`, `C1065_ClaimAndReplyInvalidateLegacyCandidate`, `C1108_LegacyWindowIsServerAnchored`, `C1108_ScheduledSweepIsGatedAndBoundedPerRun`, `C1108_ReconcileJobCountsOnlyReleases`, `C1108_DispatcherHookRunsTheGatedSweep`; CP-3 Delivery 4, Release 3, Resume 3, SyncRecovery 2; CP-4 Projection 2, Publication 3, RunnerIdentity 2; CP-5 Lifetime 6, Registration 1; CP-6 DeferredKill 3, Endpoint 8, Rules 2; CP-8 `TestClassificationGuardTests` 1, `SlowTestTripwireTests` 2. Every outcome Passed. The `After=all` rows and the S3/S4 rows were not run, as the brief scopes.

## Local mutation (brief item d)

PC-3 shape, applied to the working tree only: the two gate lines `if (discovery.NextLegacySweepAt is { } due && now < due) return LegacyReclaimResult.None;` replaced by `_ = discovery.NextLegacySweepAt;`. Build `bin-c1108r2-mut/` under lease `92e54ad0-8a35-427e-9241-a0ce4e4d32db` (107 s); run of `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun` under lease `147b1aa9-3046-4e3e-90f1-9385aa444ae0`, `--no-build`: total 1, failed 1, succeeded 0, skipped 0; message `second should be LegacyReclaimResult { Visited = 0, ... } but was LegacyReclaimResult { Visited = 5, Registered = 5, Released = 0, Eligible = 5, Cap = 5, ... }`, label `G-3`. Reverted with `git checkout`; `git diff` empty. This is a Review probe, not the Mutation cycle; PC-3 stays pending.

## Code review against the brief

(a) Default-off and inert. `ReclaimScheduledAsync` returns `None` before touching `LegacyGate`, the clock, `NextLegacySweepAt`, `LegacySweptAt` or `LastLegacyReclaim` unless `Enabled` and `ReclaimExisting` are both true and the park service is registered; `ReclaimLegacyAsync` keeps its own enabled check and the transaction check. Both options default false (`BlockedTaskParkingOptions.cs`), pinned. The hook test's two inert fixtures assert zero parks, null stamps and no cursor row. A Working session is never touched: the page is `Status == Blocked` only, `RegisterLegacyAsync` re-checks Blocked, `TryHandleTaskAsync` is unchanged, and V-2 seeds a Working row and asserts no park, status Working, session Running. The publication gate (`PrepareAsync`, `VerifyAsync`, `TryReserveAsync`, the CAS) is not in the diff.

(b) Less often or fewer rows only. The gate adds three pre-conditions in front of the unchanged `ReclaimLegacyAsync(32, 3)`; the primitive adds only the cap and read-only counters (the post-visit ledger read is `AsNoTracking` and does not disturb `CanOwnTransaction`). Interval boundary by reading: `now < due` with `due = sweptAt + interval`, so 119 s is gated and exactly 120 s runs; `now` is read after the gate and before the sweep, so the bound is start-to-start. Overlap: `WaitAsync(0, ct)` never queues; the second caller returns `None` and the test holds the gate and asserts `None` with the cursor unchanged. Wrap: with N < pageSize the page is all N rows once and `cap = N` ends the run after one pass; with N > pageSize * passBudget the passes advance the cursor without wrapping and stop at the cap; the `visited >= cap` break inside the page stops a wrapped tail from revisiting (the ungated (2, 3) arm on eight rows visits 6). Crash between rows: the per-row try/catch and the cursor commit after a caught failure are unchanged, so the poison row is skipped once and parked on the next run (crash arm). A process crash loses only the in-memory `NextLegacySweepAt` (one extra sweep, as D-2 states); the cursor is durable.

(c) `Released` excludes visits and unconfirmed attempts: it increments only when `IsConfirmed` sees the attempt's full-identity ledger row (`Confirmed`, `ConfirmedAt`, `ActionId`, outcome Released/AlreadyExited/AlreadyAbsent). Negative control at the row level: V-23's releasing run visits eight rows, holds six (`park_dirty`, `park_binding_missing`, `park_ownership_ambiguous`, Working, Unknown, poison) and asserts `Released == confirmed ledger count == 2`; `C1108_ReconcileJobCountsOnlyReleases` asserts `JobAsync() == 0`, `Visited == 1`, `Released == 0`, zero confirmed rows. The job adds `.Released` only.

(d) The hook test drives the real `AgentTaskDispatcher.ReleaseUnownedPoolDelegatesAsync` constructed by the fixture with `terminalSeatRelease:` set to the real service, then reads the real singleton; `JobAsync` constructs the real `RunnerSlotReconcileJob`. No stub.

(e) The three owner sentences are true at this SHA (callers and `(32, 3)`; overlap gate, interval, one-visit bound and the cap; confirmed releases only) and were rewritten from the post-CARD-1112 wording (the base includes `98b35554e`). Each pin is a case-sensitive `ShouldContain` on the real file, so a changed sentence fails its pin; the new pins on `antiphon-api.md`, `ops-http.md` and the option default follow the same rule. No assertion weakened or deleted: V-23 gains two assertions, V-24 is untouched, the Projection pins replace three sentences one-for-one and add three.

New tests can go red: the Code task's own red (Visited 15) and this review's PC-3 probe (second call Visited 5) are both outcome assertions on the new methods.

## Disclosures (CARD-1129, Backlog)

1. A later sweep recounts already-parked rows as released: a parked task stays Blocked and in the page, `TryHandleTaskAsync` returns at `Parked`, and the confirmed ledger row makes `released++` again, so the job's return and the per-run log line carry every earlier release on every run. By reading; no executed arm covers a second run after a release. Not a regression (the pre-S2 return counted visits) and not a fail-open.
2. The once-per-run bound holds for a stable Blocked set; if rows leave Blocked between passes the wrap can revisit a row within the cap. Rare with pageSize 32, bounded, no safety check skipped.
3. The 119 s gate arm is proven by reading only; the test asserts 0 s and 120 s.

Also noted: a cancelled or throwing sweep still stamps `NextLegacySweepAt` through the `finally` (less often only); the owner sentence's "unless ReclaimIntervalSeconds is 0" omits that a negative value is treated as 0.

## Review evidence

subjectTaskId: 8c28c2c8-0794-44d0-804b-46c59a6de44e
reviewedSourceSha: cee20ce9041b2cea9ff86fa7372163ffc75138ee
reviewedSourceClean: true
ordinaryScopeCompleted: Full
