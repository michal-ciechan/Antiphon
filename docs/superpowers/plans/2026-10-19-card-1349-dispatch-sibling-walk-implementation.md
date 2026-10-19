# CARD-1349 implementation and historical verification

Frozen before final qualification on 2026-10-19. This document preserves historical outcomes; it does not claim that pending or red rows passed. The final stored report is .antiphon/task-f4fd378a.md, including later receipts and the actual tested SHA.

Original Code/landing owner: f4fd378a-75cf-4e60-b57a-db7bdf0ee62e. Branch: feat/card-task-f4fd378a. Worktree: /work/worktrees/task-f4fd378a. Base: d5dec9555bd726ad11bb30073c1733d6b3e7bb0e. Implementation plan: docs/superpowers/plans/2026-10-19-card-1349-dispatch-sibling-walk-plan.md. Verification: docs/superpowers/plans/2026-10-19-card-1349-dispatch-sibling-walk-test-design.md. Mutation: docs/superpowers/mutations/2026-10-19-card-1349-dispatch-sibling-walk-pending.md.

S1 bounds and batches both sibling readers with a shared operation deadline, command/candidate/raw-inventory caps and independent pending-land evidence. S2 adds a two-worker/32-item advisory queue, commits pending holds before enqueue and revalidates task intent, metadata and frozen refs under the claim lease. S3 records current mirror readiness under a row lock, preserves late resource evidence, publishes after commit and derives durable read-model readiness. S4 logs committed reason codes/counts and supplies prelaunch inspection attention with stable identity and bounded evidence.

Ordinary tests exposed missing-local warnings, reviewed-source warning leakage and stale event-count expectations; those were repaired without dropping source/session assertions. The new prelaunch Held history also prevented absent-launch Blocked custody; its exception now requires exact known inspection detail, Held type and pre-dispatch timestamp, with negative witnesses. Production file-backed briefs are checked by full queued-body UserPrompt and actual retained file payload. Mirror fixtures were corrected to owned branch names after a base reproduction, and their service lifetime now matches production while ValidateScopes remains enabled.

The caller refined the task at approximately 16:59 UTC: finish within 40 minutes, execute the remaining selections and permit at most one more repair cycle, reporting partial qualification honestly if needed. The final repair corrects a Shouldly overload; no further product/test repair cycle is planned before the time-boxed report.

The Final scope is whole Unit plus the ten explicitly named full affected classes in CP-34 and every ordinary V/R-1–21. No full assembly, native PTY/browser, live fleet manipulation, restart or deployment is commissioned. Restart target after caller-controlled land/activation: server; none performed. PC-1–PC-118 and every original/new variant remain pending (0 deliberately mutated). Mutation owns red/restore/green and missing-control discovery.

Historical red witness: CP-RED at 2950339ad83b8330236619c916b6eca8b455ec2c compiled and failed the contained-inventory assertion (actual Unknown instead of Target). Earlier compile failures are not red witnesses. All later compile-failed selections executed zero tests.

Base attribution: the isolated base run below reproduced all eight original mirror failures (14 executions: 6 passed/8 failed), caused by fixture branch names refused by the existing ownership check. The later singleton/scoped-service construction failures were exposed by newly enabled fixture validation; they are not claimed as the same inherited assertion failures.

Infrastructure-only exception: checkpoint-tool bootstrap through scripts/build-slot.ps1, OutputPath=bin-c1349-tool/, UseAppHost=false; 0 errors/4 warnings, compiler 6.40s, slot=granted waited=0s (held about 7s). No other unlisted build/test driver was used. The base-only three-row manifest is the required inherited-red diagnostic, not additional ordinary scope.

Completed red runs retained build residue and their validators cannot qualify them as green receipts. Source state/build provenance printed below remain historical facts; no receipt has been rewritten. Producer-owned output roots were removed manually after executors ended on 2026-10-19 around 17:04 UTC; receipts/JSON/TRX/logs remain ignored at their original locations.

## Committed slices

```text
42da6597aa27e726c2e68a017f441fb79ef8b0fe test(card-1349): witness contained sibling inventory exceeding candidate cap
d0a9d1d0dc22a69d20db0adb6993166696dd8b4f test(card-1349): import Git result type for contained inventory witness
2950339ad83b8330236619c916b6eca8b455ec2c test(card-1349): use an expression-compatible command assertion
8e8d8bd35ca241ead07bca3249f5525137aee596 feat(card-1349): bound and batch sibling inspection (S1, qualification pending)
425bdbea6fc5c11cc64b6f85ea1c1874e7160c0e feat(card-1349): inspect siblings off tick and fence consumption (S2, qualification pending)
4eaa6de01f302ccd8fbd81d86cafd613139bff1c feat(card-1349): refresh prepared workspace holds transactionally (S3, qualification pending)
a47e9d253f59b7fb45e5a4c10556d702d02c0c2b feat(card-1349): log committed blocks and explain inspection attention (S4, qualification pending)
760d05885d17324b5d9b0e86c2400ca0556e04ff fix: retain sibling diagnostics and reviewed-source exemption
406d03c531b3bb5d52b3c047e41649a68603ba9a test: add worker lifecycle readiness and diagnostics acceptance cases (WIP)
04fda4011d456aec7fcbc3135faf72655e0222cc fix: include bounded inspection counts and stable attention identity
d9b9f986fc8b87e630686fd7735ea52e2aa108da test: bind warning receipt and interrupted brief recovery cuts (WIP)
8a757845d83862a1df42dee32c7b1fc642a21fde test: preserve unrelated working seats and bind Final profile scope
5a099e4f7a4c2f80b1044593421229abf7501711 test: import launch DTO for warning recipient fixture
17700431fb2c7d9567bd54040d8188fc348c49c3 test: correct acceptance fixture types and local bindings
c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 test: repair inherited mirror fixtures to use owned task branches
3f17dcbbc75ba59394fa160ff1a76c81f49a1168 test: bind prelaunch history safety and production mirror service scopes
2bc002abfa5c13080456b6781f320ad017070ed4 fix: retain absent-launch custody after advisory sibling inspection holds
0f982f471d7ec5b6df7a02a66c1d644519430096 docs: bind recovery history checks and pending mutation controls
e7c62cb31b79fe8986a0a3e825804a0f566d6690 test: use exact complete-input assertion for file-backed briefs
```

## Historical checkpoint receipts

### 20261019-151514-72d6

--- checkpoint report ---
run: 20261019-151514-72d6   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-151514-72d6/manifest.resolved.yaml
commit: 42da6597aa27e726c2e68a017f441fb79ef8b0fe  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 42da6597aa27e726c2e68a017f441fb79ef8b0fe state=clean buildSource=unknown
CHECKPOINT CP-RED commit=42da6597aa27e726c2e68a017f441fb79ef8b0fe build=failed filter=/*/*/DispatchSiblingInspectionTests/C1349_ContainedInventory* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=42da6597aa27e726c2e68a017f441fb79ef8b0fe sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 1m52s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-red/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-151514-72d6/report.md
verdict: RED exit=2


### 20261019-151755-0a78

--- checkpoint report ---
run: 20261019-151755-0a78   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-151755-0a78/manifest.resolved.yaml
commit: d0a9d1d0dc22a69d20db0adb6993166696dd8b4f  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: d0a9d1d0dc22a69d20db0adb6993166696dd8b4f state=clean buildSource=unknown
CHECKPOINT CP-RED commit=d0a9d1d0dc22a69d20db0adb6993166696dd8b4f build=failed filter=/*/*/DispatchSiblingInspectionTests/C1349_ContainedInventory* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=d0a9d1d0dc22a69d20db0adb6993166696dd8b4f sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 3m22s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-red2/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-151755-0a78/report.md
verdict: RED exit=2


### 20261019-152154-9906

--- checkpoint report ---
run: 20261019-152154-9906   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-152154-9906/manifest.resolved.yaml
commit: 2950339ad83b8330236619c916b6eca8b455ec2c  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 2950339ad83b8330236619c916b6eca8b455ec2c state=clean buildSource=verified
CHECKPOINT CP-RED commit=2950339ad83b8330236619c916b6eca8b455ec2c build=ok filter=/*/*/DispatchSiblingInspectionTests/C1349_ContainedInventory* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-152154-9906/rows/CP-RED/run.trx slot=granted waited=0s dirty=0 source=2950339ad83b8330236619c916b6eca8b455ec2c sourceState=clean buildSource=verified
PHASES CP-RED slotWait=0s build=334.5330345s startup=45.1011771s testsWall=3.6599041s teardown=1.5725235s hostWall=50.3335983s
FAILED Antiphon.Tests.Application.DispatchSiblingInspectionTests.C1349_ContainedInventory (CP-RED) ShouldAssertException: selected.Decision     should be CardWorktreeBaseDecision.Target     but was CardWorktreeBaseDecision.Unknown  Additional Info:     64 ancestors -> rows/CP-RED/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 6m56s  sequential-equivalent: 0m50s  builds: 1  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-red3/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-152154-9906/report.md
verdict: RED exit=1


### 20261019-154959-7a8a

--- checkpoint report ---
run: 20261019-154959-7a8a   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/manifest.resolved.yaml
commit: a47e9d253f59b7fb45e5a4c10556d702d02c0c2b  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: a47e9d253f59b7fb45e5a4c10556d702d02c0c2b state=clean buildSource=verified
CHECKPOINT CP-1 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=ok filter=/*/*/DispatchSiblingInspectionTests/C1349_InspectionLimits* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=284.9944101s startup=41.0471936s testsWall=7.8709836s teardown=1.7910471s hostWall=50.7092297s
CHECKPOINT CP-2 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/DispatchSiblingInspectionTests/C1349_ContainedInventory* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=43.2169069s testsWall=8.2454257s teardown=1.2180077s hostWall=52.6803406s
CHECKPOINT CP-3 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/DispatchSiblingInspectionTests/C1349_DocumentationSourceIsPreserved* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=42.8205849s testsWall=4.6864405s teardown=1.204439s hostWall=48.7114644s
CHECKPOINT CP-4 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/DispatchSiblingInspectionTests/C1349_ReductionRemainsBounded* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=42.5722352s testsWall=6.4500354s teardown=1.2127371s hostWall=50.2350074s
CHECKPOINT CP-18 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/DispatchBaseNotificationTests/(C540_WarningRequiresCompletePrompt*)|(C540_WarningRequiresAttemptFloor*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-18 slotWait=0s build=0s startup=41.6121421s testsWall=6.2593055s teardown=1.4204889s hostWall=49.2919365s
CHECKPOINT CP-5 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/DispatchSiblingInspectionTests/C1349_PendingLandSurvivesLimits* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=43.3319813s testsWall=14.9166104s teardown=1.5199914s hostWall=59.7685833s
CHECKPOINT CP-19 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskWorktreeBaseResolverTests/(T0442_V03_card_guid_and_git_common_directory_define_identity*)|(T0442_V04_containment_and_completion_evidence_are_distinct*) executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-19 slotWait=0s build=0s startup=45.8743421s testsWall=8.5755508s teardown=1.5439855s hostWall=55.9938777s
CHECKPOINT CP-20 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskWorktreeBaseResolverTests/(T0442_V07_dirty_or_in_progress_checkout_is_excluded*)|(T0442_V08_local_commit_availability_is_explicit*) executed=10 passed=8 failed=2 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-20 slotWait=0s build=0s startup=46.2665231s testsWall=7.537992s teardown=1.2005636s hostWall=55.0050786s
FAILED Antiphon.Tests.Application.AgentTaskWorktreeBaseResolverTests.T0442_V08_local_commit_availability_is_explicit (CP-20) ShouldAssertException: auto.CandidateWarnings     should contain an element satisfying the condition (w.Contains(source.WorktreeBranch, Ordinal) && w.Contains(IIF((CS$<>8__locals1.scenario == "git_error"), "unknown", "no local commit"), Ordinal))     but does not -> rows/CP-20/failures.md
FAILED Antiphon.Tests.Application.AgentTaskWorktreeBaseResolverTests.T0442_V08_local_commit_availability_is_explicit (CP-20) ShouldAssertException: auto.CandidateWarnings     should contain an element satisfying the condition (w.Contains(source.WorktreeBranch, Ordinal) && w.Contains(IIF((CS$<>8__locals1.scenario == "git_error"), "unknown", "no local commit"), Ordinal))     but does not -> rows/CP-20/failures.md
CHECKPOINT CP-21 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(C540_IdenticalTipsCollapse*)|(C540_AncestorChainCollapses*)|(C540_DivergentTipsRemainVisible*)|(C540_ForkKeepsBothTips*)|(C540_PatchEquivalentSiblingsStillWarn*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-21 slotWait=0s build=0s startup=43.837888s testsWall=15.6089379s teardown=1.6290489s hostWall=61.0758752s
CHECKPOINT CP-22 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(C540_RedundantSiblingStillHolds*)|(C753_NeedsResolutionSiblingWarnsInsteadOfHolding*)|(C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold*) executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-22 slotWait=0s build=0s startup=39.8870296s testsWall=13.8806181s teardown=1.5844192s hostWall=55.3520668s
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold (CP-22) ShouldAssertException: intents.Count     should be 0     but was 1 -> rows/CP-22/failures.md
CHECKPOINT CP-23 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(C540_ObservationsRemainPinned*)|(C559_UnresolvableTipStillWarns*)|(C540_ProbeCensus*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-23 slotWait=0s build=0s startup=44.5204579s testsWall=15.5935149s teardown=1.3675392s hostWall=61.4815119s
CHECKPOINT CP-24 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V20_pending_land_precedes_source_selection_in_every_mode*)|(T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold*) executed=11 passed=5 failed=6 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-24 slotWait=0s build=0s startup=44.6910511s testsWall=19.717942s teardown=1.3816598s hostWall=65.7906527s
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode (CP-24) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>             e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held, ct))     should be 1     but was 4 -> rows/CP-24/failures.md
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode (CP-24) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>             e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held, ct))     should be 1     but was 4 -> rows/CP-24/failures.md
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode (CP-24) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>             e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held, ct))     should be 1     but was 4 -> rows/CP-24/failures.md
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode (CP-24) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>             e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held, ct))     should be 1     but was 4 -> rows/CP-24/failures.md
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode (CP-24) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>             e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held, ct))     should be 1     but was 4 -> rows/CP-24/failures.md
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V20_pending_land_precedes_source_selection_in_every_mode (CP-24) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>             e.AgentTaskId == held.Id && e.Type == AgentTaskEventType.Held, ct))     should be 1     but was 4 -> rows/CP-24/failures.md
CHECKPOINT CP-25 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V19_dispatch_rechecks_the_create_preview*)|(T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips*)|(T0442_V22_launch_blocks_when_preview_inputs_worsen*) executed=11 passed=9 failed=2 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-25/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-25 slotWait=0s build=0s startup=44.1408173s testsWall=20.8317099s teardown=1.3573237s hostWall=66.3298504s
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips (CP-25) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>                 e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Held, ct))     should be 0     but was 1 -> rows/CP-25/failures.md
FAILED Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests.T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips (CP-25) ShouldAssertException: (await db.AgentTaskEvents.AsNoTracking().CountAsync(e =>                 e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Held, ct))     should be 0     but was 1 -> rows/CP-25/failures.md
CHECKPOINT CP-26 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(C788_ReviewWithIncompleteInspectionBlocks*)|(C788_FreshCodeWithTargetPreviewKeepsSafeBase*)|(C788_ExplicitBaseOverridesIncompleteAutoGuard*) executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-26/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-26 slotWait=0s build=0s startup=41.9968335s testsWall=19.00793s teardown=1.4841685s hostWall=62.4889322s
CHECKPOINT CP-27 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V25_dispatch_adopts_owned_checkout_before_reselecting_invalid_source*)|(C499_V06_ARepairIsHeldWhileItsOwnerIsLanding*)|(C508_RepairRecordsOwnerAndSkipsSiblings*)|(C1213_SourceLandingKeepsLegacyIdentity*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-27 slotWait=0s build=0s startup=43.4230697s testsWall=13.2163681s teardown=1.2707583s hostWall=57.9101959s
CHECKPOINT CP-28 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_task_launches_while_a_land_holds_the_lease* executed=7 passed=6 failed=1 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-28 slotWait=0s build=0s startup=44.4378852s testsWall=18.2131338s teardown=1.3889729s hostWall=64.039992s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-28/failures.md
SLOW CLASS Antiphon.Tests.Application.RemoteWorkspacePreparerTests 64s tests=7 (CP-28)
CHECKPOINT CP-29 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease* executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-29 slotWait=0s build=0s startup=38.6595168s testsWall=10.5149189s teardown=1.2711374s hostWall=50.4455733s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-337b2706"     but was null -> rows/CP-29/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-40d9774b"     but was null -> rows/CP-29/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-2b348589"     but was null -> rows/CP-29/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-7c06fde8"     but was null -> rows/CP-29/failures.md
CHECKPOINT CP-30 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/RemoteWorkspacePreparerTests/(C1032_CaptureLateReassignment*)|(C1032_CaptureAttemptAndCancellation*)|(C1032_CaptureLostReplyRecovery*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-30 slotWait=0s build=0s startup=43.0514875s testsWall=18.5604452s teardown=1.2858193s hostWall=62.8977522s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureAttemptAndCancellation (CP-30) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-30/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureLostReplyRecovery (CP-30) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-30/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureLateReassignment (CP-30) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-30/failures.md
CHECKPOINT CP-31 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(C540_ClaimRollbackHasNoCollapsedIntent*)|(C540_ClaimLoserOwesNoIntent*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-31 slotWait=0s build=0s startup=40.2010727s testsWall=11.9460459s teardown=0.9936773s hostWall=53.1407959s
CHECKPOINT CP-32 commit=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b build=reused filter=/*/*/DispatchBaseNotificationTests/(C540_CollapsedProjectionIsAtomic*)|(C508_IntentProjectionCustody*)|(C508_IntentProjectionLostAcknowledgement*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=a47e9d253f59b7fb45e5a4c10556d702d02c0c2b sourceState=clean buildSource=verified
PHASES CP-32 slotWait=0s build=0s startup=42.9961079s testsWall=6.6658814s teardown=1.2950655s hostWall=50.9570549s
unlisted: none (the tool ran no other build or test command)
wall: 15m45s  sequential-equivalent: 20m49s  builds: 1  max-concurrent-builds: 1  rows: 13 green 7 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-all/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-154959-7a8a/report.md
verdict: RED exit=1


### 20261019-162901-e76e

--- checkpoint report ---
run: 20261019-162901-e76e   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-162901-e76e/manifest.resolved.yaml
commit: 8a757845d83862a1df42dee32c7b1fc642a21fde  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 8a757845d83862a1df42dee32c7b1fc642a21fde state=clean buildSource=unknown
CHECKPOINT CP-6 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatchSiblingInspectionTests/C1349_InspectionConfiguration* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-7 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LaterTaskReceivesBriefBeforeInspectionCompletes* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-8 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_StaleInspectionCannotDispatch* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-9 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_InspectionQueueRecovers* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-10 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LeasedReferenceFence* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-11 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_BriefHandoffRecovers* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-12 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_CaptureRefreshesReadyHold* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-13 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_LateCaptureCannotReplaceCurrentHold* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-14 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_ReadyProjectionSurvivesLostNotification* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-15 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_BlockedLogMatchesCommittedReason* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-16 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_InspectionAttentionExplainsRecovery* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-17 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/DispatchBaseNotificationTests/C1349_InspectionWarningsReachCaller* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-20 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/AgentTaskWorktreeBaseResolverTests/(T0442_V07_dirty_or_in_progress_checkout_is_excluded*)|(T0442_V08_local_commit_availability_is_explicit*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-22 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/AgentTaskDispatchBaseGuardTests/(C540_RedundantSiblingStillHolds*)|(C753_NeedsResolutionSiblingWarnsInsteadOfHolding*)|(C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-24 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V20_pending_land_precedes_source_selection_in_every_mode*)|(T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
CHECKPOINT CP-25 commit=8a757845d83862a1df42dee32c7b1fc642a21fde build=failed filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V19_dispatch_rechecks_the_create_preview*)|(T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips*)|(T0442_V22_launch_blocks_when_preview_inputs_worsen*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=8a757845d83862a1df42dee32c7b1fc642a21fde sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 5m17s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 16 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-acceptance/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-162901-e76e/report.md
verdict: RED exit=2


### 20261019-163514-ca5d

--- checkpoint report ---
run: 20261019-163514-ca5d   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-163514-ca5d/manifest.resolved.yaml
commit: 5a099e4f7a4c2f80b1044593421229abf7501711  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 5a099e4f7a4c2f80b1044593421229abf7501711 state=clean buildSource=unknown
CHECKPOINT CP-6 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatchSiblingInspectionTests/C1349_InspectionConfiguration* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-7 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LaterTaskReceivesBriefBeforeInspectionCompletes* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-8 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_StaleInspectionCannotDispatch* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-9 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_InspectionQueueRecovers* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-10 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LeasedReferenceFence* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-11 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_BriefHandoffRecovers* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-12 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_CaptureRefreshesReadyHold* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-13 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_LateCaptureCannotReplaceCurrentHold* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-14 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_ReadyProjectionSurvivesLostNotification* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-15 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_BlockedLogMatchesCommittedReason* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-16 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_InspectionAttentionExplainsRecovery* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-17 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/DispatchBaseNotificationTests/C1349_InspectionWarningsReachCaller* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-20 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/AgentTaskWorktreeBaseResolverTests/(T0442_V07_dirty_or_in_progress_checkout_is_excluded*)|(T0442_V08_local_commit_availability_is_explicit*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-22 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/AgentTaskDispatchBaseGuardTests/(C540_RedundantSiblingStillHolds*)|(C753_NeedsResolutionSiblingWarnsInsteadOfHolding*)|(C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-24 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V20_pending_land_precedes_source_selection_in_every_mode*)|(T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
CHECKPOINT CP-25 commit=5a099e4f7a4c2f80b1044593421229abf7501711 build=failed filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V19_dispatch_rechecks_the_create_preview*)|(T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips*)|(T0442_V22_launch_blocks_when_preview_inputs_worsen*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5a099e4f7a4c2f80b1044593421229abf7501711 sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 4m04s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 16 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-acceptance2/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-163514-ca5d/report.md
verdict: RED exit=2


### 20261019-164030-a0d5

--- checkpoint report ---
run: 20261019-164030-a0d5   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/manifest.resolved.yaml
commit: c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 state=clean buildSource=verified
CHECKPOINT CP-6 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=ok filter=/*/*/DispatchSiblingInspectionTests/C1349_InspectionConfiguration* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=213.7962693s startup=42.3775686s testsWall=3.4477497s teardown=1.5007882s hostWall=47.3260898s
CHECKPOINT CP-7 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LaterTaskReceivesBriefBeforeInspectionCompletes* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=0s startup=44.8885221s testsWall=12.4488215s teardown=1.2480664s hostWall=58.5854102s
CHECKPOINT CP-8 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_StaleInspectionCannotDispatch* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=40.9838106s testsWall=18.6797733s teardown=1.2152678s hostWall=60.8788515s
CHECKPOINT CP-9 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_InspectionQueueRecovers* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-9 slotWait=0s build=0s startup=44.8527256s testsWall=22.1942405s teardown=1.3589144s hostWall=68.4058804s
CHECKPOINT CP-10 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_LeasedReferenceFence* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=44.0225988s testsWall=13.5111352s teardown=1.6622771s hostWall=59.1960109s
CHECKPOINT CP-11 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_BriefHandoffRecovers* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-11 slotWait=0s build=0s startup=46.9121405s testsWall=18.7218633s teardown=1.236572s hostWall=66.8705757s
FAILED Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_BriefHandoffRecovers (CP-11) ShouldAssertException: held.Status     should be AgentTaskStatus.Blocked     but was AgentTaskStatus.Failed -> rows/CP-11/failures.md
FAILED Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_BriefHandoffRecovers (CP-11) ShouldAssertException: prompt.Text     should contain (case insensitive comparison) "c1349 retained input café Keep the entire owned brief."     but was actually "[antiphon-task:1e113caf] role=Code tier=High workspace=Worktree inspection worker [antiphon-task:1e1..." -> rows/CP-11/failures.md
FAILED Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests.C1349_BriefHandoffRecovers (CP-11) ShouldAssertException: prompt.Text     should contain (case insensitive comparison) "c1349 retained input café Keep the entire owned brief."     but was actually "[antiphon-task:6772286e] role=Code tier=High workspace=Worktree inspection worker [antiphon-task:677..." -> rows/CP-11/failures.md
CHECKPOINT CP-12 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/RemoteWorkspaceReadinessTests/C1349_CaptureRefreshesReadyHold* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-12 slotWait=0s build=0s startup=44.5100726s testsWall=2.9774654s teardown=1.4307609s hostWall=48.9182987s
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_CaptureRefreshesReadyHold (CP-12) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-12/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_CaptureRefreshesReadyHold (CP-12) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-12/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_CaptureRefreshesReadyHold (CP-12) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-12/failures.md
CHECKPOINT CP-13 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/RemoteWorkspaceReadinessTests/C1349_LateCaptureCannotReplaceCurrentHold* executed=10 passed=0 failed=10 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-13 slotWait=0s build=0s startup=41.8483636s testsWall=3.6925116s teardown=1.6817541s hostWall=47.222629s
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_LateCaptureCannotReplaceCurrentHold (CP-13) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-13/failures.md
CHECKPOINT CP-14 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/RemoteWorkspaceReadinessTests/C1349_ReadyProjectionSurvivesLostNotification* executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-14 slotWait=0s build=0s startup=46.6389467s testsWall=3.318952s teardown=1.891518s hostWall=51.8494168s
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification (CP-14) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-14/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification (CP-14) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-14/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification (CP-14) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-14/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification (CP-14) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-14/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification (CP-14) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-14/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification (CP-14) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-14/failures.md
CHECKPOINT CP-15 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_BlockedLogMatchesCommittedReason* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-15 slotWait=0s build=0s startup=44.2078102s testsWall=21.8793604s teardown=1.1878516s hostWall=67.2750222s
CHECKPOINT CP-16 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatchBaseInspectionDiagnosticsTests/C1349_InspectionAttentionExplainsRecovery* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-16 slotWait=0s build=0s startup=44.4727435s testsWall=8.9540083s teardown=1.5064554s hostWall=54.9332075s
CHECKPOINT CP-20 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/AgentTaskWorktreeBaseResolverTests/(T0442_V07_dirty_or_in_progress_checkout_is_excluded*)|(T0442_V08_local_commit_availability_is_explicit*) executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-20 slotWait=0s build=0s startup=45.1962224s testsWall=7.941459s teardown=1.3379989s hostWall=54.4756802s
CHECKPOINT CP-17 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/DispatchBaseNotificationTests/C1349_InspectionWarningsReachCaller* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-17 slotWait=0s build=0s startup=44.2111981s testsWall=77.0843029s teardown=1.397901s hostWall=122.6934019s
SLOW CLASS Antiphon.Tests.Application.DispatchBaseNotificationTests 77s tests=10 (CP-17)
CHECKPOINT CP-22 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(C540_RedundantSiblingStillHolds*)|(C753_NeedsResolutionSiblingWarnsInsteadOfHolding*)|(C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-22 slotWait=0s build=0s startup=45.3890465s testsWall=14.0574509s teardown=1.1977889s hostWall=60.6442862s
CHECKPOINT CP-24 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V20_pending_land_precedes_source_selection_in_every_mode*)|(T0442_V30_incomplete_dispatch_inspection_blocks_previewed_source_and_keeps_land_hold*) executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-24 slotWait=0s build=0s startup=42.9833336s testsWall=18.7011388s teardown=4.4801259s hostWall=66.1645982s
CHECKPOINT CP-25 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/AgentTaskDispatchBaseGuardTests/(T0442_V19_dispatch_rechecks_the_create_preview*)|(T0442_V21_completed_land_releases_hold_and_rechecks_remaining_tips*)|(T0442_V22_launch_blocks_when_preview_inputs_worsen*) executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-25/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-25 slotWait=0s build=0s startup=41.8213426s testsWall=22.7652192s teardown=1.4518252s hostWall=66.0383874s
CHECKPOINT CP-28 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_task_launches_while_a_land_holds_the_lease* executed=7 passed=0 failed=7 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-28 slotWait=0s build=0s startup=42.6298588s testsWall=3.4969777s teardown=1.1677904s hostWall=47.2946268s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-28/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-28/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-28/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-28/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-28/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-28/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-28/failures.md
CHECKPOINT CP-29 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-29 slotWait=0s build=0s startup=43.2925271s testsWall=13.4504293s teardown=1.5106121s hostWall=58.2535683s
CHECKPOINT CP-30 commit=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 build=reused filter=/*/*/RemoteWorkspacePreparerTests/(C1032_CaptureLateReassignment*)|(C1032_CaptureAttemptAndCancellation*)|(C1032_CaptureLostReplyRecovery*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=c41a5eb1568cdaa1cb255bbb4d7c84c6ef3ec490 sourceState=clean buildSource=verified
PHASES CP-30 slotWait=0s build=0s startup=42.4305924s testsWall=3.5425079s teardown=1.4744868s hostWall=47.4475874s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureLateReassignment (CP-30) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-30/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureLostReplyRecovery (CP-30) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-30/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureAttemptAndCancellation (CP-30) InvalidOperationException: Cannot consume scoped service 'Antiphon.Server.Application.Interfaces.IWorkspaceReservationJournal' from singleton 'Antiphon.Server.Application.Services.RemoteWorkspaceService'. -> rows/CP-30/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 14m17s  sequential-equivalent: 19m24s  builds: 1  max-concurrent-builds: 1  rows: 13 green 6 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-acceptance3/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-164030-a0d5/report.md
verdict: RED exit=1


### 20261019-165719-7a2a

--- checkpoint report ---
run: 20261019-165719-7a2a   manifest: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-165719-7a2a/manifest.resolved.yaml
commit: 0f982f471d7ec5b6df7a02a66c1d644519430096  branch: feat/card-task-f4fd378a  worktree: /work/worktrees/task-f4fd378a  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 0f982f471d7ec5b6df7a02a66c1d644519430096 state=clean buildSource=unknown
CHECKPOINT CP-11 commit=0f982f471d7ec5b6df7a02a66c1d644519430096 build=failed filter=/*/*/DispatcherSiblingInspectionSchedulingTests/C1349_BriefHandoffRecovers* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0f982f471d7ec5b6df7a02a66c1d644519430096 sourceState=clean buildSource=unknown
CHECKPOINT CP-12 commit=0f982f471d7ec5b6df7a02a66c1d644519430096 build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_CaptureRefreshesReadyHold* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0f982f471d7ec5b6df7a02a66c1d644519430096 sourceState=clean buildSource=unknown
CHECKPOINT CP-13 commit=0f982f471d7ec5b6df7a02a66c1d644519430096 build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_LateCaptureCannotReplaceCurrentHold* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0f982f471d7ec5b6df7a02a66c1d644519430096 sourceState=clean buildSource=unknown
CHECKPOINT CP-14 commit=0f982f471d7ec5b6df7a02a66c1d644519430096 build=failed filter=/*/*/RemoteWorkspaceReadinessTests/C1349_ReadyProjectionSurvivesLostNotification* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0f982f471d7ec5b6df7a02a66c1d644519430096 sourceState=clean buildSource=unknown
CHECKPOINT CP-28 commit=0f982f471d7ec5b6df7a02a66c1d644519430096 build=failed filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_task_launches_while_a_land_holds_the_lease* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0f982f471d7ec5b6df7a02a66c1d644519430096 sourceState=clean buildSource=unknown
CHECKPOINT CP-29 commit=0f982f471d7ec5b6df7a02a66c1d644519430096 build=failed filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0f982f471d7ec5b6df7a02a66c1d644519430096 sourceState=clean buildSource=unknown
CHECKPOINT CP-30 commit=0f982f471d7ec5b6df7a02a66c1d644519430096 build=failed filter=/*/*/RemoteWorkspacePreparerTests/(C1032_CaptureLateReassignment*)|(C1032_CaptureAttemptAndCancellation*)|(C1032_CaptureLostReplyRecovery*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=0f982f471d7ec5b6df7a02a66c1d644519430096 sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 4m48s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 7 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-recovery/
evidence: /work/worktrees/task-f4fd378a/.antiphon/checkpoints/20261019-165719-7a2a/report.md
verdict: RED exit=2


### 20261019-162924-b55a

--- checkpoint report ---
run: 20261019-162924-b55a   manifest: /tmp/c1349-f4fd378a-base/.antiphon/checkpoints/20261019-162924-b55a/manifest.resolved.yaml
commit: d5dec9555bd726ad11bb30073c1733d6b3e7bb0e  branch: HEAD  worktree: /tmp/c1349-f4fd378a-base  host: Debian GNU/Linux 12 (bookworm) cores=24
source: d5dec9555bd726ad11bb30073c1733d6b3e7bb0e state=clean buildSource=verified
CHECKPOINT CP-29 commit=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e build=reused filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease* executed=4 passed=0 failed=4 skipped=0 trx=/tmp/c1349-f4fd378a-base/.antiphon/checkpoints/20261019-162924-b55a/rows/CP-29/run.trx slot=granted waited=0s dirty=0 source=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e sourceState=clean buildSource=verified
PHASES CP-29 slotWait=0s build=321.1376458s startup=43.4404448s testsWall=9.6712396s teardown=2.057875s hostWall=55.1694898s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-a2b8b07e"     but was null -> rows/CP-29/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-39408322"     but was null -> rows/CP-29/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-33b23189"     but was null -> rows/CP-29/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease (CP-29) ShouldAssertException: prepared.RemoteWorktreePath     should be "/work/worktrees/task-004d66d9"     but was null -> rows/CP-29/failures.md
CHECKPOINT CP-28 commit=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e build=ok filter=/*/*/RemoteWorkspacePreparerTests/C672_prepared_task_launches_while_a_land_holds_the_lease* executed=7 passed=6 failed=1 skipped=0 trx=/tmp/c1349-f4fd378a-base/.antiphon/checkpoints/20261019-162924-b55a/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e sourceState=clean buildSource=verified
PHASES CP-28 slotWait=0s build=0s startup=41.7952331s testsWall=18.5909774s teardown=1.0585908s hostWall=61.4447746s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease (CP-28) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-28/failures.md
SLOW CLASS Antiphon.Tests.Application.RemoteWorkspacePreparerTests 67s tests=7 (CP-28)
CHECKPOINT CP-30 commit=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e build=reused filter=/*/*/RemoteWorkspacePreparerTests/(C1032_CaptureLateReassignment*)|(C1032_CaptureAttemptAndCancellation*)|(C1032_CaptureLostReplyRecovery*) executed=3 passed=0 failed=3 skipped=0 trx=/tmp/c1349-f4fd378a-base/.antiphon/checkpoints/20261019-162924-b55a/rows/CP-30/run.trx slot=granted waited=0s dirty=0 source=d5dec9555bd726ad11bb30073c1733d6b3e7bb0e sourceState=clean buildSource=verified
PHASES CP-30 slotWait=0s build=0s startup=57.5361693s testsWall=22.0766433s teardown=1.9504291s hostWall=81.5632416s
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureLostReplyRecovery (CP-30) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-30/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureAttemptAndCancellation (CP-30) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-30/failures.md
FAILED Antiphon.Tests.Application.RemoteWorkspacePreparerTests.C1032_CaptureLateReassignment (CP-30) TimeoutException: Expected 1 WorkspaceMirror request(s); saw 0. -> rows/CP-30/failures.md
SLOW CLASS Antiphon.Tests.Application.RemoteWorkspacePreparerTests 66s tests=3 (CP-30)
unlisted: none (the tool ran no other build or test command)
wall: 7m52s  sequential-equivalent: 3m20s  builds: 1  max-concurrent-builds: 1  rows: 0 green 3 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1349-base/
evidence: /tmp/c1349-f4fd378a-base/.antiphon/checkpoints/20261019-162924-b55a/report.md
verdict: RED exit=1

