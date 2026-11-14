# CARD-1915 migration roster performance

**Incomplete: the brief's 90-minute time box prevents the remaining measurements; Code's performance prerequisite remains open.**

Debug 71aaddba; measurement only. Completed 16/26 original rows and 1 repeat. 1500 executed / 1492 passed / 8 failed / 0 skipped in original rows. Status and remaining obligations are stated below.

Source: current master at task start, verified with `git ls-remote origin refs/heads/master`: **1e09a851acef35a3d08eec4a79be2214d51c64df**. Plan source `e5c926262a2d141b92379d3228bb5631730d4e13` on `origin/feat/card-task-ee0bb3d9`, `docs/superpowers/plans/2026-11-14-card-1915-seed-transcript-aggregation-plan.md`. The assigned branch already started at this master. A fresh detached scratch worktree at this SHA kept measurements separate from the report commits. No CARD-1915 implementation.

Host: Debian 12 Linux x64, 24 logical CPUs, .NET SDK 10.0.404 / runtime 9.0.23 / TUnit 1.44.0. Cold means empty native project bin/obj in a fresh checkout; shared NuGet/compiler/OS and baked PostgreSQL schema caches remained available. Every build/test used `scripts/build-slot.ps1 -SlotWaitMinutes 10`; builds used `-p:UseAppHost=false -m:1`, runs the same property and `--no-build`. Each row ran alone and serially. The messaging cold build used an independent fresh worktree; CP-32 execution remains unmeasured.

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
| CP-8 | first | 60/60/0/0 | 138.4 | 0 | 0.8 | 14.61/19.84/22.22 → 19.30/20.51/22.19 |
| CP-9 | first | 107/107/0/0 | 90.3 | 0 | 1.1 | 19.30/20.51/22.19 → 18.53/20.06/21.88 |
| CP-10 | first | 125/125/0/0 | 161.9 | 0 | 1.3 | 18.53/20.06/21.88 → 18.21/18.96/21.12 |
| CP-11 | first | 89/89/0/0 | 283.4 | 0 | 1.6 | 18.21/18.96/21.12 → 12.81/19.21/21.16 |
| CP-12 | first | 70/67/3/0 | 79.9 | 0 | 1.2 | 12.81/19.21/21.16 → 10.76/16.96/20.19 |
| CP-13 | first | 24/24/0/0 | 112.2 | 0 | 0.6 | 10.76/16.96/20.19 → 11.26/15.67/19.39 |
| CP-14 | first | 57/57/0/0 | 129.7 | 0 | 1.1 | 10.60/15.46/19.30 → 9.21/13.69/18.19 |
| CP-15 | first | 20/20/0/0 | 306.0 | 0 | 0.6 | 8.80/13.53/18.11 → 29.06/19.05/18.77 |
| CP-16 | first | 14/14/0/0 | 145.2 | 0 | 1.4 | 29.06/19.05/18.77 → 41.16/27.10/21.75 |
| CP-17 | first | 91/91/0/0 | 205.9 | 0 | 1.1 | 41.16/27.10/21.75 → 20.67/24.39/21.79 |
| CP-18 | first | 29/29/0/0 | 676.9 | 0 | 1.0 | 19.82/24.15/21.73 → 21.00/21.64/21.54 |
| CP-18 | repeat | 29/29/0/0 | 588.6 | 120 | 121.0 | 32.86/25.68/22.99 → 33.50/22.05/21.00 |
| CP-19 | first | 544/540/4/0 | 417.6 | 0 | 1.6 | 33.50/22.05/21.00 → 18.96/24.61/23.29 |
| CP-20 | first | 32/32/0/0 | 83.2 | 0 | 1.1 | 17.76/24.27/23.19 → 14.50/21.70/22.37 |
| CP-21 | first | 35/35/0/0 | 110.8 | 45 | 46.3 | 13.41/20.33/21.87 → 13.62/18.05/20.85 |
| CP-22 | first | 75/74/1/0 | 235.9 | 0 | 1.1 | 13.62/18.05/20.85 → 9.59/13.78/18.51 |
| CP-23 | first | 128/128/0/0 | 376.6 | 15 | 15.9 | 9.08/13.47/18.33 → 10.33/12.05/16.16 |
| CP-24 | interrupted; no terminal TRX | unknown | unknown | unknown | unknown | unknown |
| CP-25 | not run | unknown | unknown | unknown | unknown | unknown |
| CP-26 | not run | unknown | unknown | unknown | unknown | unknown |
| CP-27 | not run | unknown | unknown | unknown | unknown | unknown |
| CP-28 | not run | unknown | unknown | unknown | unknown | unknown |
| CP-29 | not run | unknown | unknown | unknown | unknown | unknown |
| CP-30 | not run | unknown | unknown | unknown | unknown | unknown |
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
| CP-24 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-25 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-26 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-27 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-28 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-29 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-30 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-31 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-32 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |
| CP-33 | 0 | unknown | unknown | unmeasured; plan prerequisite incomplete |

Completed-run serial interval: **4341.6 s** (includes repeat admissions and gaps). Original-row sequential-equivalent execution sum: **3553.9 s**; all completed rows including repeats: **4142.6 s**. Broker slot wait sum: **180 s**; full admission wall sum: **198.9 s**. Test-case-duration sum from TRX: **6671.4 s** for original rows; this alternative case-level sequential equivalent can exceed wall because cases run concurrently within a row. Builds are separate. This interval is the whole roster only if all 26 original rows completed.

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

## Re-partition and handoff

Retain the exact original filters above except **CP-15 → RP-15a–c**, **CP-18 → RP-18a–d**, **CP-19 → RP-19a–d**, and **CP-23 → RP-23a–b**. This yields 35 proposed rows across the complete roster. Every proposed replacement cap below is at most 600 seconds. **The replacements have not been run as standalone filters**: these are provisional commissioning proposals, not new execution receipts or measured p95. Code must amend the closed checkpoint table before use; missing original measurements below remain a prerequisite.

Bounds use the maximum observed class envelope (or summed method-case durations for method splits), plus the maximum observed process startup/tail gap for the original row. This deliberately retains CP-15's 163.7-second tail. Original-class overlaps and repeated startup mean summing the proposed bounds does not predict the original wall or a speedup. Rerun these exact new selections and collect five comparable complete samples, with loads/cache state and separate admission/execution, under CARD-1915/CARD-1404; then derive caps near 2× p95.

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


## Completeness and remaining work

The plan's nominal 26-row estimates total **102 min**, before builds, waits and reruns; the commissioned Debug window is **90 min**. Measurement stopped at 2026-11-14T04:43:01.657Z (hard-timebox), reserving final publication time. The entire roster wall and entire-roster sequential equivalent are **unknown**, because these original rows remain unmeasured: **CP-24, CP-25, CP-26, CP-27, CP-28, CP-29, CP-30, CP-31, CP-32, CP-33**. The completed-prefix interval and sums above are not relabelled as a full roster.

Budget repeats still needed: **CP-11, CP-15, CP-19, CP-23** (using the lower 4-minute estimates, conservatively; CP-33 uses 2 minutes). CP-18's mandatory execution-cap repeat completed and both historical outcomes/costs remain recorded. Do not infer success or p95 for an unmeasured or interrupted row. The exact original filters above make the continuation a closed list. Commission an additional measurement window for these gaps, then validate/repack remaining caps before Code. The original plan's D-1 scope decision also remains with the caller.

Stop receipt: `stop-receipt.json`; last completed row **CP-23**; 0 recorded descendants remain live. CP-24 was interrupted without terminal TRX or measurement credit. Its approximate wall to the stop receipt was 415.9 s (including termination); exact completed execution duration and verdict counts are unknown. Its log/phase data remain retained, not silently deleted or assigned zero counts. Observed roster-driver wall through this interruption was 4757.5 s; this is spent time, not a complete-roster duration. On a hard stop, fixture-container cleanup is unqualified; no manual Docker or volume deletion was attempted.

Scratch worktrees remain at `/work/worktrees/task-71aaddba-measure` and `/work/worktrees/task-71aaddba-messaging-measure`, with native outputs available for continuation. Both HEADs remain the measured source SHA; tracked source cleanliness: **true**. Previously recorded DLL hashes unchanged: **true**. No product/test source repair, restart, deployment, branch switch, reset or rebase occurred. Report commits are on the assigned `feat/card-task-71aaddba` only.

--- next stage ---
next: code
handoff: Performance prerequisite incomplete. Finish the exact missing original rows and budget repeats above, then amend the plan with RP-15a–c, RP-18a–d, RP-19a–d, RP-23a–b and validated retained-row caps before starting implementation. Inherited failures are CARD-1916, CARD-1917, CARD-1918 and CARD-1919.
artifact: /work/worktrees/task-71aaddba/docs/investigations/2026-11-14-card-1915-roster-performance.md
