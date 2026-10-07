# CARD-1108 S2 repeat Final Review (task 1dc6bc32)

Outcome: clean. No regression of existing behaviour and no reachable fail-open. The complete S2 ordinary scope (plan rows CP-6..CP-12) plus the registry guard reran green at the reviewed SHA in one checkpoint-tool run with a SHA-validated clean receipt; two local mutations (the gate and the visit loop) went red on the named methods and were reverted; the branch merge-trees cleanly onto current `origin/master`. The three disclosures already on CARD-1129 stand; nothing new rises to a defect under the verdict policy, and no new card was filed.

- subjectTaskId: `8c28c2c8-0794-44d0-804b-46c59a6de44e` (Code owner; landing owner unchanged)
- Reviewed ref: `refs/heads/feat/card-task-8c28c2c8` at `cee20ce9041b2cea9ff86fa7372163ffc75138ee` (tested source `a4186c148b8494daeb4ecbdd10deb3ab9b0b6cb2`; the tip adds the Code report commit; parent `9463e1fba9a086b152df7517cb892f5f6c7e3fd2`). `git ls-remote` confirms that ref still at `cee20ce90`.
- Review branch: `feat/card-task-1dc6bc32`, mirror `/work/worktrees/task-1dc6bc32`, same tip plus this report. Not rebased.
- Round: Final (profile v1). ordinaryScopeCompleted: Full. PC-1..PC-7 and the carried CARD-1065 controls stay pending for method-scoped SourceLanding Mutation.
- Previous Final Review of this SHA (task d18e99d2, `/work/worktrees/task-d18e99d2/.antiphon/task-d18e99d2.md`, also on `feat/card-task-d18e99d2` at `13832511c`): read first; its findings were re-derived here, not inherited. Its PC-3 mutation record (second same-clock call `Visited = 5` not 0, label `G-3`, build lease `92e54ad0`, run lease `147b1aa9`, total 1 failed 1) is reused as a confirmed report finding; this review ran two different mutations itself.

## Master drift and merge

`origin/master` is `eb1fe3407142f5ab11027fae7e6a0d138cc644fd` (the brief named `83939cfb3`; two CARD-1109 docs commits landed after it). Eight commits since the base `9463e1fba`: f4ae747a6, 34c764942, 3687e07bc, a00055a26, 2a02b6f2c, 83939cfb3, 5032ea9e5, eb1fe3407. `git merge-tree --write-tree` of `cee20ce90` onto each of `eb1fe3407`, `83939cfb3` and `f4ae747a6` exits 0 (tree ids `eddc53f01`, `5500a17da`, `37147aab2`): no conflicting path, no conflicting hunk, no rebase needed. The only S2-touched file master changed is `server/Application/Services/AgentTaskDispatcher.cs` (CARD-1082 S4b: a `SettlementSyncRecoveryService` field, constructor parameter, one `RunSweepAsync("settlement sync debt", ...)` registration and `RecoverSettlementSyncAsync` near line 7431); the S2 hunk is the single call at line 7443 inside `ReleaseUnownedPoolDelegatesAsync`, and the merged tree carries both. The merged `docs/session-runtime-invariants.md` carries the three S2 sentences at lines 109, 111 and 112.

## Checkpoint audit against the plan

The plan's `### Checkpoints` has seven S2 rows, CP-6..CP-12 (CP-9's Serial cell is blank; the Code run forced it with `--serial`). The Code report records all seven from run `20261006-225557-e8c0` at `a4186c148`, `sourceState=clean buildSource=verified`: 6, 39, 12, 7, 7, 13, 17, failed 0 skipped 0. The seven `CHECKPOINT` lines in `.antiphon/task-8c28c2c8.md` are byte-identical (`diff` empty) to the tool's own `report.md` in the Code worktree `/work/worktrees/task-8c28c2c8/.antiphon/checkpoints/20261006-225557-e8c0/`, whose footer reads `unlisted: none` / `rows: 7 green 0 red 0 skipped` / `verdict: GREEN exit=0`. Its TRX rosters equal the plan's expected sets, not merely Min: CP-6 Reclaim 6; CP-7 Seat 39; CP-8 Delivery 4 + Release 3 + Resume 3 + SyncRecovery 2; CP-9 Projection 2 + Publication 3 + RunnerIdentity 2; CP-10 Lifetime 6 + Registration 1; CP-11 DeferredKill 3 + Endpoint 8 + Rules 2; CP-12 Orphan 17; every result Passed. No row missing, no zero count.

Unlisted runs each carry a reason. The Code session transcript (`GET /api/sessions/ba1d7f44-5385-4024-9964-2b0974ce7ab3/transcript`) shows exactly seven `build-slot.ps1 -Label` strings (`c1108-red`, `c1108-red-test`, `c1108-green`, `c1108-green-test`, `c1108-green-projection`, `c1108-checkpoint-bootstrap`, `c1108-registry-guard`) against seven drivers (two `dotnet build tests/Antiphon.Tests` into `bin-c1108-red/` and `bin-c1108-green/`, one driver build, four `dotnet run ... --no-build` test runs); the checkpoint tool took its own slots. The only `-NoSlot` string in the transcript is the report's own sentence "No `-NoSlot`". The red-first measurement is real: the session terminal log `call-337a988c-...-74.log` under `/state/grok/sessions/%2Fwork%2Fworktrees%2Ftask-8c28c2c8/ba1d7f44-.../terminal/` holds lease `0be7267f-1611-41cc-8d41-3b80a7b57fc6`, `failed C1108_ScheduledSweepIsGatedAndBoundedPerRun`, label `G-4`, total 1 failed 1 succeeded 0 skipped 0.

Evidence guard over the reviewed range: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 9463e1fba... -HeadRef cee20ce90...` printed `EVIDENCE result commits=2 entries=1 violations=0` (the entry is the Code report markdown).

## Independent rerun at the reviewed SHA

Platform read before the run: `GET /api/runner-defaults` revision 2 names global runner `server2`; `GET /api/session-runners` shows `desktop` (windows, 0/2) accepting, `server2` (linux) draining and not accepting, `server2-temp` (linux, 4/10) accepting. No runner pin, no platform pin. Build-slot broker before the run: budget 4, occupied 1, no waiters.

One hand-made eight-row markdown-table manifest in the session scratchpad (rows CP-1..CP-8 map to plan CP-6..CP-12 plus the registry guard), one checkpoint-tool run `20261006-235630-6226` (`start --plan <manifest> --serial --expected-source-sha cee20ce90...`), one isolated build into `bin-c1108r3-review/`, every row serial under its own slot lease. The tool's driver was built once under `build-slot.ps1` (label `c1108r3-driver`, lease `482d0e43`, 5 s) into the separately named `bin-c1108r3-drv/`. `validate --evidence report.json --expected-source-sha cee20ce90...` printed `CHECKPOINT SOURCE VALID source=cee20ce9041b2cea9ff86fa7372163ffc75138ee rows=8`. The tool deleted `bin-c1108r3-review/` on green; `bin-c1108r3-drv/` and the mutation output `bin-c1108r3-mut/` (28 directories) were removed afterwards with a root-confined rm; `git status --short --untracked-files=all` is empty.

Tool report lines, unedited (`.antiphon/checkpoints/20261006-235630-6226/report.md`):

```
CHECKPOINT CP-1 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=ok filter=/*/*/BlockedTaskParkReclaimTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=122.6157619s startup=39.2338258s testsWall=54.3887735s teardown=2.0542368s hostWall=95.6768433s
CHECKPOINT CP-2 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/TerminalRunnerSeatReleaseTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=41.8299932s testsWall=51.0522915s teardown=2.371676s hostWall=95.2539605s
SLOW CLASS Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests 1208s tests=39 (CP-2)
CHECKPOINT CP-3 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(BlockedTaskParkReleaseTests*)|(BlockedTaskSyncRecoveryTests*)|(BlockedTaskParkResumeTests*)|(BlockedTaskParkDeliveryTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=41.0713376s testsWall=280.0269958s teardown=1.758867s hostWall=322.8572002s
SLOW CLASS Antiphon.Tests.Application.BlockedTaskParkDeliveryTests 111s tests=4 (CP-3)
SLOW CLASS Antiphon.Tests.Application.BlockedTaskParkReleaseTests 94s tests=3 (CP-3)
CHECKPOINT CP-4 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(TaskParkPublicationTests*)|(TaskParkRunnerIdentityTests*)|(BlockedTaskParkProjectionTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=38.9317737s testsWall=19.2125941s teardown=0.9980113s hostWall=59.142379s
CHECKPOINT CP-5 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(DispatcherSweepLifetimeTests*)|(DispatcherSweepLifetimeRegistrationTests*)/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=36.3125353s testsWall=8.4045572s teardown=1.3738589s hostWall=46.0909512s
CHECKPOINT CP-6 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(RunnerSlotEndpointTests*)|(PhoneHomeDeferredKillTests*)|(RunnerSlotRulesTests*)/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=0s startup=42.5498737s testsWall=7.7568381s teardown=1.3440656s hostWall=51.6507772s
CHECKPOINT CP-7 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/RunnerSeatOrphanSweepTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=0s startup=39.2444506s testsWall=31.9114534s teardown=1.6716946s hostWall=72.8275985s
SLOW CLASS Antiphon.Tests.Application.RunnerSeatOrphanSweepTests 352s tests=17 (CP-7)
CHECKPOINT CP-8 commit=cee20ce9041b2cea9ff86fa7372163ffc75138ee build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=cee20ce9041b2cea9ff86fa7372163ffc75138ee sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=4.3864161s testsWall=1.6026708s teardown=0.4825295s hostWall=6.4716164s
unlisted: none (the tool ran no other build or test command)
wall: 14m42s  sequential-equivalent: 12m39s  builds: 1  max-concurrent-builds: 1  rows: 8 green 0 red 0 skipped
outputs: deleted bin-c1108r3-review/
evidence: /work/worktrees/task-1dc6bc32/.antiphon/checkpoints/20261006-235630-6226/report.md
verdict: GREEN exit=0
```

TRX rosters equal the expected sets: CP-1 the six methods `C1065_ClaimAndReplyInvalidateLegacyCandidate`, `C1065_LegacySweepRequiresFreshPublicationAndIdleWindow`, `C1108_DispatcherHookRunsTheGatedSweep`, `C1108_LegacyWindowIsServerAnchored`, `C1108_ReconcileJobCountsOnlyReleases`, `C1108_ScheduledSweepIsGatedAndBoundedPerRun`; CP-3 Delivery 4, Release 3, Resume 3, SyncRecovery 2; CP-4 Projection 2, Publication 3, RunnerIdentity 2; CP-5 Lifetime 6, Registration 1; CP-6 DeferredKill 3, Endpoint 8, Rules 2; CP-8 `TestClassificationGuardTests.Registry_matches_compiled_metadata` 1 and `SlowTestTripwireTests` 2. Every outcome Passed. 104 results, 14m42s wall, one 123 s build. The `After=all` rows and the S3/S4 rows were not run, as the brief scopes.

## Local mutations (brief item d)

Both applied to the working tree only, after the checkpoint run had finished, each built once into `bin-c1108r3-mut/` under `build-slot.ps1` and run as one method-scoped `dotnet run --no-build` under its own lease, then reverted with `git checkout -- server/` (diff empty, no untracked files).

1. PC-5 shape, gate: `discovery.LegacySweptAt = clock.GetUtcNow();` inserted before the enabled check in `ReclaimScheduledAsync`. Build lease `5cefdd7c` (112 s), run lease `0f0174a4` (48 s), filter `/*/*/BlockedTaskParkReclaimTests/C1108_DispatcherHookRunsTheGatedSweep`: total 1, failed 1, passed 0; message `reclaimOff.State.LegacySweptAt should be null but was 10/05/2026 00:00:00 +00:00`, label `G-5`. The hook-level test went red through the real dispatcher hook.
2. PC-4 shape, visit loop: `var cap = pageSize * passBudget;` without the eligible minimum in `ReclaimLegacyAsync`. Build lease `8f9a988c` (59 s), run lease `7233821d` (62 s), filter `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun`: total 1, failed 1, passed 0; message `first.Visited should be 5 but was 15`, label `G-4`.
3. PC-3 shape (ignore `NextLegacySweepAt`), reused from the d18e99d2 report after confirming it there: same-clock second call `Visited = 5` not 0, label `G-3`.

These are Review probes, not the Mutation cycle; PC-3, PC-4 and PC-5 stay pending.

## Code review against the brief

(a) Default-off and inert. `BlockedTaskParkingOptions` defaults `Enabled=false`, `ReclaimExisting=false`, `ReclaimIntervalSeconds=120` (pinned); the section is bound in `Program.cs:407` and no committed appsettings or compose file sets `BlockedTaskParking`. `ReclaimScheduledAsync` returns `None` before touching `LegacyGate`, the clock, `NextLegacySweepAt`, `LegacySweptAt` or `LastLegacyReclaim` unless `Enabled` and `ReclaimExisting` are both true and the park service is registered; `ReclaimLegacyAsync` keeps its own enabled and transaction checks. The hook test's two inert fixtures assert zero parks, null stamps and no cursor row. A Working session is never touched: `NextLegacyPageAsync` selects `Status == Blocked` only, `RegisterLegacyAsync` re-checks Blocked, `TryHandleTaskAsync` is unchanged, and V-2 seeds a Working row and asserts no park, status Working, session Running. The D-3 publication gate (`PrepareAsync`, `VerifyAsync`, `TryReserveAsync`, the CAS) is not in the diff. `ReclaimScheduledAsync` is the only scheduled caller (dispatcher line 7443, job line 47); `ReclaimLegacyAsync(32, 3)` is called only from inside it.

(b) Less often or fewer rows only. The gate adds three pre-conditions in front of the unchanged `ReclaimLegacyAsync(32, 3)`; the primitive adds only the cap and read-only `AsNoTracking` counters. Interval boundary by reading: `now < due` with `due = sweptAt + interval`, so at 119 s the call is gated and at exactly 120 s it runs; `now` is read after the gate and before the sweep, so the bound is start-to-start. Overlap: `WaitAsync(0, ct)` never queues; the second caller returns `None`, and the test holds the gate and asserts `None` with the cursor unchanged. Wrap: with N rows below page size the page is all N once and `cap = N` ends the run after one pass; with more rows than `pageSize * passBudget` the passes advance the cursor and stop at the cap without wrapping; the `visited >= cap` break inside the page stops a wrapped tail from revisiting (the ungated `(2, 3)` arm on eight rows visits 6). Crash between rows: the per-row try/catch and the cursor commit after a caught failure are unchanged, so the poison row is skipped once and parked on the next run (crash arm, 120 s later). A process crash loses only the in-memory `NextLegacySweepAt` (one extra sweep, as D-2 states); the cursor is durable.

(c) `Released` excludes visits and unconfirmed attempts: it increments only when `IsConfirmed` sees the attempt's full-identity ledger row (`Confirmed`, `ConfirmedAt`, `ActionId`, outcome Released/AlreadyExited/AlreadyAbsent). Negative controls: `C1108_ReconcileJobCountsOnlyReleases` asserts `JobAsync() == 0`, `Visited == 1`, `Released == 0`, zero confirmed rows; the hook test and V-2 assert `Released == 0` on visited-but-held rows; V-23's releasing run holds six of eight rows and asserts `Released == confirmed ledger count == 2`. The job adds `.Released` only.

(d) The hook test drives the real `AgentTaskDispatcher` resolved from the fixture's DI container (constructed with `terminalSeatRelease:` the real service) through `ReleaseUnownedPoolDelegatesAsync`, then reads the real `TerminalRunnerSeatDiscoveryState` singleton; `JobAsync` constructs the real `RunnerSlotReconcileJob`. No stub. Mutation 1 above proves the path can go red.

(e) The three owner sentences are true at this SHA (callers and `(32, 3)`; overlap gate, interval, one-visit bound and the cap; confirmed releases only) and were rewritten from the post-CARD-1112 wording: the base `9463e1fba` includes `98b35554e`, and the diff's removed lines equal that base text at lines 109-112. Each pin is a case-sensitive `ShouldContain` on the real file, so a changed sentence fails its pin; the labels `c1065-reclaim-callers`, `c1065-reclaim-page`, `c1065-reclaim-count` are kept and three pins are added (`antiphon-api.md`, `ops-http.md`, the option default). No assertion weakened or deleted: V-23 gains two assertions, V-24 is untouched, `ReclaimAsync` still returns the visit count for its existing callers.

New tests can go red: the Code task's own red (Visited 15), this review's two probes and the d18e99d2 probe are all outcome assertions on the new methods.

## Disclosures

CARD-1129 (Backlog, `a335b385-1eed-4520-8bbf-4f50610dbacd`) already records the three from the previous review, re-derived here: a later sweep recounts already-parked rows as released; the once-per-run bound assumes a stable Blocked set; the 119 s gate arm is proven by reading only. Not duplicated. Same family as its "also noted" paragraph, no action: a `ReclaimLegacyAsync` that returns `None` from its own transaction check still stamps the interval through the `finally`, so the next scheduled call waits a full interval (less often only; scheduled callers hold no transaction).

## Review evidence

subjectTaskId: 8c28c2c8-0794-44d0-804b-46c59a6de44e
reviewedSourceSha: cee20ce9041b2cea9ff86fa7372163ffc75138ee
reviewedSourceClean: true
ordinaryScopeCompleted: Full
