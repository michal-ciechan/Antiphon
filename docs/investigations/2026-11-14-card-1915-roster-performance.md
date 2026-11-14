# CARD-1915 migration roster performance

**Bounded measurement and plan amendment complete; exact remaining execution is listed below. Required verification is not green while inherited failures or unmeasured selections remain.**

Debug 71aaddba; measurement only. Completed 22/26 original rows and 1 repeat. 1955 executed / 1947 passed / 8 failed / 0 skipped in original rows. Status and remaining obligations are stated below.

Source: current master at task start, verified with `git ls-remote origin refs/heads/master`: **1e09a851acef35a3d08eec4a79be2214d51c64df**. Plan source `e5c926262a2d141b92379d3228bb5631730d4e13` on `origin/feat/card-task-ee0bb3d9`, `docs/superpowers/plans/2026-11-14-card-1915-seed-transcript-aggregation-plan.md`. The assigned branch already started at this master. A fresh detached scratch worktree at this SHA kept measurements separate from the report commits. No CARD-1915 implementation.

Host: Debian 12 Linux x64, 24 logical CPUs, .NET SDK 10.0.404 / runtime 9.0.23 / TUnit 1.44.0. Cold means empty native project bin/obj in a fresh checkout; shared NuGet/compiler/OS and baked PostgreSQL schema caches remained available. Every build/test used `scripts/build-slot.ps1 -SlotWaitMinutes 10`; builds used `-p:UseAppHost=false -m:1`, runs the same property and `--no-build`. Each row ran alone and serially. The messaging graph was independently cold-built; CP-32 remains unmeasured.

Evidence root (ignored): `/work/worktrees/task-71aaddba/.antiphon/evidence/card-1915-roster/`. Per-row JSON/log/TRX, exact commands, 1/5/15-minute loads at invocation/admission/exit, phase JSONL, class census, DLL hashes and source/host identity are retained there. TRX supplies counts and assertion verdicts. Phase recording was enabled; instrumentation overhead was not separately measured.

## Builds

Seconds. Slot wait is the broker's integer-second value. Admission wall additionally includes wrapper startup/HTTP; execution is grant-to-wrapper-exit, including driver startup and lease release.

| Graph | Execution s | Slot wait s | Admission wall s | Load 1/5/15 start → end | Result |
|---|---:|---:|---:|---|---|
| Antiphon.Tests, cold complete graph (includes Checkpoints and staged helpers) | 234.5 | 150 | 151.7 | 15.37/23.43/24.14 → 20.02/21.65/22.93 | exit 0 |
| Antiphon.Messaging.Tests, independently cold graph | 16.6 | 0 | 0.9 | 16.16/20.47/22.47 → 14.84/19.97/22.28 | exit 0 |
| Antiphon.DerivedImpactIndex, cold standalone driver | 4.2 | 0 | 1.0 | 18.74/21.21/22.75 → 17.88/20.99/22.67 | exit 0 |
| Messaging in main worktree, partially warm dependencies | 19.7 | 0 | 1.0 | 20.02/21.65/22.93 → 18.74/21.21/22.75 | exit 0 |

Provisional cold-build caps: main graph **8 min** (rounded above 2 × 234.507 s), messaging **35 s**, derived-index driver **10 s**. Separate admission cap stays **10 min**. n=1 per cold graph is not p95. Checkpoints was built transitively in the main cold graph; no independent cold Checkpoints timing is claimed.

## Original roster and repeats

P50 is the sample median (mean of two observations for n=2); max preserves every observed run. One or two observations **do not estimate p95**. Recommended original-row caps are rounded 2× observed max, provisional under CARD-1915/CARD-1404; rows needing a cap above 10 min must be split. Execution includes all startup/fixture/teardown and uninstrumented gaps.

| Row | Run | Executed / passed / failed / skipped | Execution s | Slot wait s | Admission wall s | Load 1/5/15 start → end |
|---|---|---|---:|---:|---:|---|
| CP-8 | first complete | 60/60/0/0 | 138.4 | 0 | 0.8 | 14.61/19.84/22.22 → 19.30/20.51/22.19 |
| CP-9 | first complete | 107/107/0/0 | 90.3 | 0 | 1.1 | 19.30/20.51/22.19 → 18.53/20.06/21.88 |
| CP-10 | first complete | 125/125/0/0 | 161.9 | 0 | 1.3 | 18.53/20.06/21.88 → 18.21/18.96/21.12 |
| CP-11 | first complete | 89/89/0/0 | 283.4 | 0 | 1.6 | 18.21/18.96/21.12 → 12.81/19.21/21.16 |
| CP-12 | first complete | 70/67/3/0 | 79.9 | 0 | 1.2 | 12.81/19.21/21.16 → 10.76/16.96/20.19 |
| CP-13 | first complete | 24/24/0/0 | 112.2 | 0 | 0.6 | 10.76/16.96/20.19 → 11.26/15.67/19.39 |
| CP-14 | first complete | 57/57/0/0 | 129.7 | 0 | 1.1 | 11.26/15.67/19.39 → 9.21/13.69/18.19 |
| CP-15 | first complete | 20/20/0/0 | 306.0 | 0 | 0.6 | 9.21/13.69/18.19 → 29.06/19.05/18.77 |
| CP-16 | first complete | 14/14/0/0 | 145.2 | 0 | 1.4 | 29.06/19.05/18.77 → 41.16/27.10/21.75 |
| CP-17 | first complete | 91/91/0/0 | 205.9 | 0 | 1.1 | 41.16/27.10/21.75 → 20.67/24.39/21.79 |
| CP-18 | first complete | 29/29/0/0 | 676.9 | 0 | 1.0 | 20.67/24.39/21.79 → 21.00/21.64/21.54 |
| CP-18 | repeat | 29/29/0/0 | 588.6 | 120 | 121.0 | 21.00/21.64/21.54 → 33.50/22.05/21.00 |
| CP-19 | first complete | 544/540/4/0 | 417.6 | 0 | 1.6 | 33.50/22.05/21.00 → 18.96/24.61/23.29 |
| CP-20 | first complete | 32/32/0/0 | 83.2 | 0 | 1.1 | 18.96/24.61/23.29 → 14.50/21.70/22.37 |
| CP-21 | first complete | 35/35/0/0 | 110.8 | 45 | 46.3 | 14.50/21.70/22.37 → 13.62/18.05/20.85 |
| CP-22 | first complete | 75/74/1/0 | 235.9 | 0 | 1.1 | 13.62/18.05/20.85 → 9.59/13.78/18.51 |
| CP-23 | first complete | 128/128/0/0 | 376.6 | 15 | 15.9 | 9.59/13.78/18.51 → 10.33/12.05/16.16 |
| CP-24 | first complete | 63/63/0/0 | 495.1 | 150 | 152.4 | 36.75/31.23/23.74 → 33.25/27.72/25.24 |
| CP-25 | first complete | 85/85/0/0 | 137.5 | 195 | 196.2 | 33.25/27.72/25.24 → 39.85/33.08/27.99 |
| CP-26 | first complete | 25/25/0/0 | 91.4 | 120 | 121.0 | 39.85/33.08/27.99 → 33.38/29.07/27.21 |
| CP-27 | first complete | 41/41/0/0 | 90.5 | 135 | 136.2 | 33.38/29.07/27.21 → 49.92/39.76/31.90 |
| CP-28 | first complete | 127/127/0/0 | 280.3 | 60 | 61.5 | 49.92/39.76/31.90 → 27.73/27.85/28.90 |
| CP-29 | first complete | 114/114/0/0 | 308.5 | 120 | 121.6 | 27.73/27.85/28.90 → 20.50/27.47/28.86 |
| CP-30 | interrupted; no terminal TRX | unknown | unknown | 180 | 180.7 | 20.50/27.47/28.86 → 13.54/20.91/25.93 |
| CP-31 | not run | unknown | unknown | unknown | unknown | unknown |
| CP-32 | not run | unknown | unknown | unknown | unknown | unknown |
| CP-33 | not run | unknown | unknown | unknown | unknown | unknown |

| Row | n | p50 s | max s | Proposed original-row execution cap |
|---|---:|---:|---:|---|
| CP-8 | 1 | 138.4 | 138.4 | 300 s |
| CP-9 | 1 | 90.3 | 90.3 | 210 s |
| CP-10 | 1 | 161.9 | 161.9 | 330 s |
| CP-11 | 1 | 283.4 | 283.4 | 570 s |
| CP-12 | 1 | 79.9 | 79.9 | 180 s |
| CP-13 | 1 | 112.2 | 112.2 | 240 s |
| CP-14 | 1 | 129.7 | 129.7 | 270 s |
| CP-15 | 1 | 306.0 | 306.0 | split; 630 s would exceed 10 min |
| CP-16 | 1 | 145.2 | 145.2 | 300 s |
| CP-17 | 1 | 205.9 | 205.9 | 420 s |
| CP-18 | 2 | 632.8 | 676.9 | split; 1380 s would exceed 10 min |
| CP-19 | 1 | 417.6 | 417.6 | split; 840 s would exceed 10 min |
| CP-20 | 1 | 83.2 | 83.2 | 180 s |
| CP-21 | 1 | 110.8 | 110.8 | 240 s |
| CP-22 | 1 | 235.9 | 235.9 | 480 s |
| CP-23 | 1 | 376.6 | 376.6 | split; 780 s would exceed 10 min |
| CP-24 | 1 | 495.1 | 495.1 | split; 1020 s would exceed 10 min |
| CP-25 | 1 | 137.5 | 137.5 | 300 s |
| CP-26 | 1 | 91.4 | 91.4 | 210 s |
| CP-27 | 1 | 90.5 | 90.5 | 210 s |
| CP-28 | 1 | 280.3 | 280.3 | 570 s |
| CP-29 | 1 | 308.5 | 308.5 | split; 630 s would exceed 10 min |
| CP-30 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-31 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-32 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-33 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |

Completed-run serial interval: **7322.3 s** (includes repeat admissions and gaps). Original-row sequential-equivalent execution sum: **4957.2 s**; all completed rows including repeats: **5545.8 s**. Broker slot wait sum: **960 s**; full admission wall sum: **987.7 s**. Test-case-duration sum from TRX: **8919.7 s** for original rows; this alternative case-level sequential equivalent can exceed wall because cases run concurrently within a row. Builds are separate. This observed span crosses measurement windows; a single uninterrupted full-roster wall was not measured.

## Recorded phases

Known spans are the time union of recorded complete phase intervals, avoiding nested/parallel double-counting. Remainder includes uninstrumented work and test bodies; it is not assigned to a cause. First/tail are elapsed grant-to-first-recorded-case and last-recorded-case-to-wrapper-exit, not separately traced fixture verdicts. Detailed DB clone/migration, filesystem, host, network and process spans remain in `phase-summary.json`/JSONL; absent phases are unknown, not zero.

| Run | First case delay s | Tail s | Recorded phase union s | Unattributed remainder s |
|---|---:|---:|---:|---:|
| CP-10 | 27.6 | 13.8 | 84.6 | 77.3 |
| CP-11 | 38.5 | 11.4 | 32.5 | 250.9 |
| CP-12 | 19.8 | 8.2 | 50.1 | 29.8 |
| CP-13 | 26.9 | 3.0 | 12.1 | 100.1 |
| CP-14 | 28.8 | 9.3 | 90.5 | 39.2 |
| CP-15 | 18.7 | 163.7 | 101.5 | 204.5 |
| CP-16 | 30.3 | 5.7 | 82.5 | 62.7 |
| CP-17 | 24.1 | 15.1 | 71.8 | 134.1 |
| CP-18-repeat | 23.9 | 11.7 | 66.0 | 522.6 |
| CP-18 | 24.8 | 9.4 | 93.1 | 583.8 |
| CP-19 | 31.2 | 22.5 | 76.4 | 341.2 |
| CP-20 | 22.4 | 5.6 | 57.5 | 25.6 |
| CP-21 | 21.2 | 12.2 | 56.6 | 54.2 |
| CP-22 | 22.4 | 3.7 | 33.9 | 202.0 |
| CP-23 | 32.7 | 6.4 | 67.1 | 309.5 |
| CP-24 | 34.6 | 11.5 | 186.3 | 308.8 |
| CP-25 | 24.8 | 13.0 | 18.5 | 119.0 |
| CP-26 | 24.4 | 7.0 | 46.9 | 44.6 |
| CP-27 | 31.8 | 21.3 | 18.2 | 72.2 |
| CP-28 | 40.3 | 6.2 | 92.2 | 188.1 |
| CP-29 | 30.4 | 17.7 | 134.2 | 174.3 |
| CP-8 | 80.0 | 34.2 | 70.8 | 67.6 |
| CP-9 | 29.3 | 18.6 | 23.0 | 67.2 |

## Inherited failures


CP-12: 3 failures on unchanged master.

- `Antiphon.Tests.Application.AgentTaskMidTurnRefineTests.C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt(flush-transport-failure-then-retry)`: ShouldAssertException: row.Status should be QueuedMessageStatus.Pending but was QueuedMessageStatus.Sent
- `Antiphon.Tests.Application.AgentTaskMidTurnRefineTests.C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(no-prompt-enter-only)`: ShouldAssertException: after.Status should be QueuedMessageStatus.Pending but was QueuedMessageStatus.Sent
- `Antiphon.Tests.Application.AgentTaskMidTurnRefineTests.C1169_AgedScreenOnlyRefinementFollowsInterruptedSentRecovery(no-prompt-retype)`: ShouldAssertException: after.DeliveryAttempts should be 2 but was 1

CP-19: 4 failures on unchanged master.

- `Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1150_Running_cut_is_discovered_and_delivered_once`: ShouldAssertException: Task `world.ResumeAsync(seeded)` should throw System.OperationCanceledException but did not
- `Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets(watchdog-first-recovery, 65)`: ShouldAssertException: total should be 65 but was 67
- `Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets(running-recovery-missing-brief, 65)`: ShouldAssertException: total should be 65 but was 67
- `Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets(running-recovery-existing-brief, 70)`: ShouldAssertException: total should be 70 but was 72

CP-22: 1 failures on unchanged master.

- `Antiphon.Tests.Scripts.ImpactedClassesSelectorTests.C1801_why_missed_paths_select_their_classes_and_windows_filters`: ShouldAssertException: the expected SessionRunner tuple lacks C1514SamplerTickCpuTests*, while the actual tuple includes it. Full expected/actual tuples are in TRX.

CP-19's SQL-count failures are tracked by **CARD-1917** (`630fa6d6-82c6-4820-a37a-c30fa2713ffa`), and its missing cancellation by **CARD-1918** (`aea0b1d9-21fc-4c24-a0bf-09c6fef3d418`). Full CARD-1522, CARD-1541 and CARD-1543 were inspected; all were Done with historical signatures. No open matching card was returned. CP-22 is tracked by **CARD-1919** (`00b79d4b-8e74-4c9d-95ac-aa19504176c9`); full CARD-1866/1867 and CARD-1819 were inspected and cover different advisory/historical work. Full SQL rosters remain in TRX; only assertion cores are reproduced here.

CP-12's three refinement assertions are tracked by **CARD-1916**, card `5c0943f7-cf68-47f2-a68f-c41a12a4496f`, Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703`. Full duplicate searches and inspection of CARD-1169 (Done) and CARD-1517 (different Claude-interrupt scope) preceded creation. These are assertion failures, not slot/build/fixture failures; root cause is not claimed.

## Exact original filters

Markdown table escaping only: remove each backslash before a pipe when passing the filter as one literal argv value. These were copied from the plan, with no class or namespace broadening. Main project applies except CP-32 (Messaging.Tests). Run command: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <row> -SlotWaitMinutes 10 -- dotnet run --project tests/<Project> --no-build -p:UseAppHost=false -- --treenode-filter <filter> --report-trx --results-directory <owned-evidence-path>`.

| Row | Exact filter |
|---|---|
| CP-8 | `/*/*/(AbandonedLandingReconcilerTests*)\|(AdditiveMigrationRuleWitnessTests*)\|(AgentBundleAttachmentTests*)\|(AgentChannelServiceIntegrationTests*)/*` |
| CP-9 | `/*/*/(AgentControlServiceIntegrationTests*)\|(AgentSessionCancelledTurnTests*)\|(AgentSessionRuntimeTests*)\|(AgentTaskCatchUpSettlementTests*)/*` |
| CP-10 | `/*/*/(AgentTaskDispatchBaseGuardTests*)\|(AgentTaskInternalDecisionMigrationTests*)\|(AgentTaskLandApprovalPersistenceTests*)\|(AgentTaskLandHeldNotificationTests*)/*` |
| CP-11 | `/*/*/(AgentTaskLandMonitoringTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandingPersistenceTests*)/*` |
| CP-12 | `/*/*/(AgentTaskMidTurnRefineTests*)\|(AgentTaskReplyOverlayTests*)\|(AgentTaskStandingAgentDispatchTests*)\|(AgentTaskWorktreeBaseMigrationTests*)/*` |
| CP-13 | `/*/*/(AgentTuiPersistenceTests*)\|(AgentTuiSecretMigratorTests*)\|(AppDbContextModelTests*)\|(C0824RefOnlyIdentityTests*)/*` |
| CP-14 | `/*/*/(C0824RefOnlySnapshotMigrationTests*)\|(C1514HostLoadMigrationTests*)\|(C1729LayeringGuardTests*)\|(CapacityAdmissionLifecycleTests*)/*` |
| CP-15 | `/*/*/(CapacityRecoveryCompatibilityTests*)\|(Card1619ExitMigrationIdTests*)\|(Card1619ExitPersistenceTests*)\|(Card1773TranscriptPromptStampMigrationTests*)/*` |
| CP-16 | `/*/*/(CardCleanupSchedulingSchemaTests*)\|(CardFilePrivacyMigrationTests*)\|(CardPlatformTests*)\|(CardThreadEndpointTests*)/*` |
| CP-17 | `/*/*/(CardVerificationPolicyTests*)\|(CardWorktreeMirrorBindingTests*)\|(ChannelBridgeTests*)\|(ChannelOutboundDeliveryTests*)/*` |
| CP-18 | `/*/*/(ChannelOutboundDurabilitySchemaTests*)\|(ChannelOutboundMigrationTests*)\|(ChannelOutboundRecoveryTests*)\|(ChannelOutboundRetentionTests*)/*` |
| CP-19 | `/*/*/(CheckpointSourceApprovalTests*)\|(DataRetentionServiceTests*)\|(DelegationDispatchRecoveryBoundaryTests*)\|(DispatchBaseNotificationTests*)/*` |
| CP-20 | `/*/*/(DispatchConcurrencyMigrationTests*)\|(DurableRunnerSpillReceiptTests*)\|(ExpectationSnapshotTests*)\|(GitIgnorePreviewTests*)/*` |
| CP-21 | `/*/*/(HangfireLeaseDocumentationTests*)\|(HangfirePostgreSqlStorageTests*)\|(HerdrPlacementSettingsTests*)\|(HostCleanupOrchestrationTests*)/*` |
| CP-22 | `/*/*/(ImpactedClassesSelectorTests*)\|(LandRequestWriteDiagnosticTests*)\|(LegacyCheckNotePublicationTests*)\|(OutboundConversionPublicCreateTests*)/*` |
| CP-23 | `/*/*/(OutputDistillationApplyRaceTests*)\|(OutputDistillationDeliveryTests*)\|(OutputDistillationMigrationTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` |
| CP-24 | `/*/*/(PipelineDefinitionMigrationShapeTests*)\|(PipelineDefinitionMigrationTests*)\|(PipelineSpeedIntegrationTests*)\|(PoolClaudeEffortMigrationTests*)/*` |
| CP-25 | `/*/*/(PostLandMutationAdmissionTests*)\|(ProjectCascadeForeignKeyTests*)\|(RoutingCandidateParseTests*)\|(RoutingPinCandidateTests*)/*` |
| CP-26 | `/*/*/(RunnerDefaultMigrationTests*)\|(RunnerDefaultPlacementTests*)\|(RunnerDefaultSettingsTests*)\|(RunnerDefaultsWireTests*)/*` |
| CP-27 | `/*/*/(RunnerSlotEndpointTests*)\|(ScopeResolverAreaTests*)\|(SeatOccupancySamplerTests*)\|(SessionRunnerEventPumpTests*)/*` |
| CP-28 | `/*/*/(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionTests*)\|(SessionStateWarmupTests*)\|(SpecialistExecutionIdentityTests*)/*` |
| CP-29 | `/*/*/(StaleWorkingTranscriptWatchdogTests*)\|(StandingRestartEscalationTests*)\|(StandingSessionOwnershipTests*)\|(StandingSpecialistRoutingMigrationTests*)/*` |
| CP-30 | `/*/*/(TaskPlatformDispatchTests*)\|(TaskPlatformPersistenceTests*)\|(TestDbFixtureLifecycleTests*)\|(TranscriptHotPathMigrationTests*)/*` |
| CP-31 | `/*/*/(WorkerWorkspaceDefaultMigrationTests*)\|(WorkingTranscriptRecoveryTests*)\|(WorktreeBaseSelectionTests*)\|(WorktreeDefaultCallerTests*)/*` |
| CP-32 | `/*/*/(InboxConversationMigrationTests*)/*` |
| CP-33 | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)\|(ReleaseImpactEnvelopeTests*)\|(TestTimingOperationLabelTests*)/*` |

## Plan amendment: S8 roster replacements

This table amends the S8 checkpoint roster in plan **e5c926262a2d141b92379d3228bb5631730d4e13** on this task branch. The plan task branch was not changed. Retain the exact original filters above except **CP-15 → RP-15a–c**, **CP-18 → RP-18a–d**, **CP-19 → RP-19a–d**, and **CP-23 → RP-23a–b**. These four requested replacements yield 35 roster selections before the additional CP-24 amendment below. Every proposed replacement cap below is at most 600 seconds. **The replacements have not been run as standalone filters**: these are provisional commissioning proposals, not new execution receipts or measured p95. These replacements supersede the four original CP rows for Code commissioning; copy their IDs and exact filters into the executable checkpoint manifest. Every replacement inherits **After=S8, Build=CP-8, Covers=R-1,R-2, Expect=all listed, 0 failed**. Minimum selected-test counts from the unchanged-source TRX partition census: RP-15a/b/c=14/3/3; RP-18a/b/c/d=11/5/7/6; RP-19a/b/c/d=4/483/42/15; RP-23a/b=81/47. All 721 original test results belong to exactly one replacement, with no uncovered or overlapping case in this census; this is filter coverage validation, not a standalone execution receipt. Unchanged original rows retain their After/Build/Covers/Expect/Min values. Required execution remains open where recorded below.

Bounds use the maximum observed class envelope (or summed method-case durations for method splits), plus the maximum observed process startup/tail gap for the original row. This deliberately retains CP-15's 163.7-second tail. Original-class overlaps and repeated startup mean summing the proposed bounds does not predict the original wall or a speedup. Validate these exact replacement selections independently with load/cache state and separate admission/execution. The caps below retain the original class/method attribution sample; they are provisional estimates, not a measured p95 or proof of standalone runtime. Additional comparable samples are needed before claiming p95.

| Proposed row | Replaces | Provisional execution estimate s | Provisional cap s | Exact filter |
|---|---|---:|---:|---|
| RP-15a | CP-15 | 292.8 | 600 | `/*/*/(CapacityRecoveryCompatibilityTests*)\|(Card1619ExitPersistenceTests*)/*` |
| RP-15b | CP-15 | 237.2 | 480 | `/*/*/Card1619ExitMigrationIdTests*/*` |
| RP-15c | CP-15 | 252.0 | 510 | `/*/*/Card1773TranscriptPromptStampMigrationTests*/*` |
| RP-18a | CP-18 | 158.3 | 330 | `/*/*/(ChannelOutboundDurabilitySchemaTests*)\|(ChannelOutboundMigrationTests*)\|(ChannelOutboundRetentionTests*)/*` |
| RP-18b | CP-18 | 247.2 | 510 | `/*/*/ChannelOutboundRecoveryTests*/Process_death_preserves_ownership_at_each_boundary*` |
| RP-18c | CP-18 | 244.1 | 510 | `/*/*/ChannelOutboundRecoveryTests*/(Recovery_preserves_settled_worker_and_publication_boundaries*)\|(Running_converter_reconciles_after_dispatcher_death*)` |
| RP-18d | CP-18 | 135.2 | 300 | `/*/*/ChannelOutboundRecoveryTests*/(Resume_held_after_conversion_returns_to_ready*)\|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)\|(Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head*)\|(Broker_ack_before_process_death_remains_uncertain_without_replay*)\|(Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not*)\|(Expired_publishing_lease_is_uncertain_until_explicit_retry*)` |
| RP-19a | CP-19 | 160.6 | 330 | `/*/*/CheckpointSourceApprovalTests*/*` |
| RP-19b | CP-19 | 163.2 | 330 | `/*/*/(DataRetentionServiceTests*)\|(DelegationDispatchRecoveryBoundaryTests*)/*` |
| RP-19c | CP-19 | 258.7 | 540 | `/*/*/DispatchBaseNotificationTests*/C508*` |
| RP-19d | CP-19 | 136.8 | 300 | `/*/*/DispatchBaseNotificationTests*/(C1349*)\|(C540*)` |
| RP-23a | CP-23 | 282.8 | 570 | `/*/*/OutputDistillationDeliveryTests*/*` |
| RP-23b | CP-23 | 132.8 | 270 | `/*/*/(OutputDistillationApplyRaceTests*)\|(OutputDistillationMigrationTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` |

Retained measured rows use the rounded 2×max caps in the original-row table. Unmeasured retained rows have **unknown** compliant execution caps: the old 8-minute (CP-33 4-minute) values are historical provisional allocations, not newly validated limits. CP-32 must remain in the messaging project, CP-33 in the guard lane. No CP-34/native Windows measurement or release-wide coverage is claimed.

CP-18 alone cost 676.870 / 588.635 seconds (both 29/29), so its original 8-minute cap is empirically too small; one run also exceeded 10 minutes. CP-15's long uninstrumented tail should be investigated under CARD-1514 before attempting fixture optimization: `TestDbFixtureLifecycle.DisposeCoreAsync` awaits pool stop, owned-container disposal, image-bake completion and orphan sweep, but these samples do not identify which wait caused the gap. No timeout, assertion, fixture or production setting was changed.


### Additional measured cap amendment: CP-24

CP-24's first complete run was 495.092 seconds (63/63 passed); rounded 2×max is 1020 seconds. Replace it with the following three S8 / Build=CP-8 / Covers=R-1,R-2 / Expect=all listed, 0 failed selections. The original RP-15/18/19/23 amendment remains unchanged. This makes **37 roster selections** in CP-8..CP-33 after all five replacements (the other plan rows remain unchanged).

| Proposed row | Replaces | Min selected tests | Provisional estimate s | Provisional cap s | Exact filter |
|---|---|---:|---:|---:|---|
| RP-24a | CP-24 | 15 | 202.4 | 420 | `/*/*/(PipelineDefinitionMigrationShapeTests*)\|(PipelineDefinitionMigrationTests*)\|(PoolClaudeEffortMigrationTests*)/*` |
| RP-24b | CP-24 | 9 | 263.5 | 540 | `/*/*/PipelineSpeedIntegrationTests*/C1281_WiredDeliveryRecovery*` |
| RP-24c | CP-24 | 39 | 199.3 | 420 | `/*/*/PipelineSpeedIntegrationTests*/(C1281_B*)\|(C1281_Admission*)\|(C1281_WiredRoundtrip*)\|(C1288*)\|(C1289*)\|(C1300*)\|(C1303*)` |

These filters partition the 63 recorded cases without overlap. Migration estimate sums class envelopes plus 46.067 seconds observed startup/tail; method splits sum selected case durations plus that same overhead. These are attribution-based bounds, not independently measured filters or p95. No standalone replacement execution is claimed.

### Additional measured cap amendment: CP-29

CP-29's first run passed 114/114 in 308.465 seconds; rounded 2×max is 630 seconds. Replace it with these S8 / Build=CP-8 / Covers=R-1,R-2 / Expect=all listed, 0 failed rows. Together with the five replacements above, this gives **38 roster selections** in CP-8..CP-33. These are unexecuted standalone filters with provisional bounds from class envelopes plus 48.139 seconds startup/tail.

| Proposed row | Replaces | Min selected tests | Provisional estimate s | Provisional cap s | Exact filter |
|---|---|---:|---:|---:|---|
| RP-29a | CP-29 | 57 | 211.3 | 450 | `/*/*/(StandingSpecialistRoutingMigrationTests*)\|(StaleWorkingTranscriptWatchdogTests*)/*` |
| RP-29b | CP-29 | 57 | 145.3 | 300 | `/*/*/(StandingRestartEscalationTests*)\|(StandingSessionOwnershipTests*)/*` |

The two filters partition all 114 recorded cases without overlap. CP-29's budget repeat remains outstanding in the caller's specified execution order; no p95 or standalone runtime validation is claimed.

## Measurement windows, completeness and provenance

The original plan estimated **102 min** for the 26 rows before builds, admission or repeats. The first 90-minute commission ended at **2026-11-14T04:43:01.657Z**: 16/26 original rows, 1500 executed / 1492 passed / 8 failed / 0 skipped, plus CP-18 repeat 29/29. Its completed-prefix serial interval was **4341.6 s**, original-row execution sum **3553.9 s**, all-completed execution sum **4142.6 s**, broker wait **180 s**, admission wall **198.9 s**. The interrupted CP-24 attempt spent approximately **415.9 s** including termination; observed roster-driver wall through that stop was **4757.5 s**. No terminal count/execution credit was assigned. Original logs, partial results and phase shards are preserved under `original-interrupted-CP-24/`; `stop-receipt.json` records no owned descendants remaining live. No Docker/volume cleanup was attempted; fixture cleanup of an interrupted attempt is unqualified.

The extension clock was anchored at the first observed UTC time, **2026-11-14T04:46:35Z**, for 45 minutes, with hard measurement stop **2026-11-14T05:31:35Z**. Extension driver window: **2026-11-14T04:49:14.332Z → 2026-11-14T05:31:37.457Z** (2543.1 s, including termination/cleanup). Ordered selections were CP-24, CP-25..CP-33, then repeats CP-11/15/19/23. 6 completed extension selections: **455 executed / 455 passed / 0 failed / 0 skipped**. Terminal extension receipts: CP-24, CP-25, CP-26, CP-27, CP-28, CP-29.

Exact original rows still unmeasured: **CP-30, CP-31, CP-32, CP-33**. Requested repeats still unmeasured: **CP-11, CP-15, CP-19, CP-23**. Additional budget repeats still unmeasured: **CP-24, CP-28, CP-29** (lower estimate threshold: 240 seconds, CP-33 120 seconds; the explicit caller priority list was preserved). The full-roster serial wall and sequential-equivalent execution are unknown. The updated completed-run interval in the table spans the pause and the historical interrupted attempt; it is not pure useful execution. First completed samples total **4957.2 s**; all terminal samples total **5545.8 s**; repeated cases are not deduplicated in the all-sample count. Actual per-driver wall sum is **6533.5 s**, excluding builds and interrupted attempts; add the historical interrupted attempt and any extension interruption separately.

Extension interrupted **CP-30** at **2026-11-14T05:31:35.257Z**, wall **348.8 s**. No terminal TRX/complete-duration credit is assigned. The partial result directory and log remain. The hard-stop signal began at **2026-11-14T05:31:35.113Z**; `extension-stop.json` verified **0 recorded owned descendants live** at 05:31:37.115Z. CP-30 spent 180 s waiting for its slot and 168.085 s from admission through termination; the latter is a partial elapsed cost, not a completed selection runtime. Fixture cleanup is unqualified after interruption.

Scratch worktrees remain at `/work/worktrees/task-71aaddba-measure` and `/work/worktrees/task-71aaddba-messaging-measure`, with native build outputs available for continuation. Both measurement trees remain pinned to **1e09a851acef35a3d08eec4a79be2214d51c64df**. Tracked source clean: **true**; original DLL hashes unchanged: **true**. No CARD-1915 implementation, product/test repair, assertion/timeout change, restart, deployment, branch switch, reset or rebase occurred. CARD-1916..1919 remain with the separate Debug; no edits or tracker mutations were made for them during this extension. The plan's D-1 scope decision remains with the caller.

--- next stage ---
next: code
handoff: Apply the S8 amendment RP-15a–c, RP-18a–d, RP-19a–d, RP-23a–b, RP-24a–c and RP-29a–b. Validate the replacement filters and retained-row caps; complete the exact missing original rows/repeats listed above before claiming the performance prerequisite complete. Inherited failures CARD-1916..1919 are owned by the separate Debug. No implementation or land approval is given by this measurement report.
artifact: /work/worktrees/task-71aaddba/docs/investigations/2026-11-14-card-1915-roster-performance.md
