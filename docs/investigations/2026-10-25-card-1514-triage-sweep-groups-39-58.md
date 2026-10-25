# CARD-1514: Triage sweep groups 39–58

2026-10-25 UTC; Investigate task `dd3c1b63-8266-41ce-a93a-963be83a04f5`.

Completed the requested Linux sweep: **423 cases, 351 passed, 56 failed, 16 skipped**, covering exactly all **76 requested classes** in 17 groups. No unexpected class was selected. **29 classes in 13 groups failed**. Groups **39, 40, 47 and 53** passed completely. One targeted shared-state isolation rerun passed **1/1**; it does not erase the original batch failure.

This is a triage result, not confirmation of one common product root cause. Several fixture mechanisms are reconstructed below; unresolved failures still need Debug investigation. No product or test source was changed, no fix was designed, and the detached desktop qualification `a779085c` was untouched.

## Source identity and limits

- Task/report branch: `feat/card-task-dd3c1b63`, starting at `89933526f0ac7391c7a0b84bb3afb4585b73d7f2`. The branch was not switched, merged with the target, or rebased.
- Fetched master and **actually built/tested source**: `f0911378c59116609161a57141e6fa35bd7d388c`. Testing used an owned detached checkout at `/work/worktrees/task-dd3c1b63/.antiphon/triage-master`; tracked source was clean before and after execution, including the isolation rerun.
- Later fetched master: `3c94a31d4b3f44628d5d0d0122616d29c1de7ca5`. Its delta from the tested SHA is confined to `scripts/test-release-gate.ps1`, `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatFixture.cs`, and the script classes `ReleaseTagAllocatorTests`, `RunCheckpointRepeatScriptTests`, `ScriptHarnessCancellationTests`. None of the 76 selected class files, server/runner source, or shared TestHelpers changed. These results remain attributed to the built SHA, not relabeled as a run of the later tip.
- Original Windows rc candidate in the brief: `a97194ee7a4adf215454fd82c453ba46362596ee`. Linux passes on newer source do not establish the old Windows cause, prove an rc fix, or provide native qualification.
- All source citations below refer to **the tested SHA**. `A` denotes the stored batch TRX `all/all.trx`; each failing row below gives its exact TRX `testId`, display name including argument vector, and test transcript line. The transcript is `all.log` in the evidence root. These are direct TRX runs, not validated checkpoint receipts; the commissioned direct driver avoids the checkpoint reader's DLL size limit.
- No broad Unit lane was run. The two explicitly requested classes `OrchestratorInstructionsRendererTests` and `PhoneHomeDirectoryTests` carry Unit metadata and were included by exact class name as requested.

## Execution and retained evidence

Host `0c18bb7f591e`: Debian 12/Linux x64, 24 logical CPUs, .NET 9.0.21, TUnit 1.44.0, Microsoft Testing Platform 2.2.2. Shared-host load was present. The batch used `--maximum-parallel-tests 4`; process/Git limiter attributes remained active.

| Invocation | Result | Measured execution / driver wall | Slot admission | Hard bound |
|---|---|---|---|---|
| One cold isolated build | exit 0; 0 errors, 1,197 warnings | MSBuild 3m42.66s; lease held 223s | granted, 0s | 900s including at most 5m slot wait; TERM then KILL after 20s |
| All 76 classes | exit 2; 351 pass / 56 fail / 16 skip | MTP 6m29.079s; outer driver 393.845s | granted, 0s; lease held 393s | 1500s including at most 3m slot wait; TERM then KILL after 20s |
| Exact alarm-notifier method in fresh process | exit 0; 1 pass / 0 fail / 0 skip | MTP 21.001s; outer driver 26.319s | granted, 0s; lease held 25s | 360s including at most 2m slot wait; TERM then KILL after 20s |

Bounds were provisional collection limits under the commissioned 60-minute budget; no prior per-method p95 sample was supplied. No production or test timeout was widened, and no invocation exceeded 30 minutes. Across this heterogeneous single batch, observed case p95 was 5.251s, maximum 108.771s (passing `BlockedTaskParkReleaseTests.C1065_ReportPublicationPrecedesPhysicalRelease`); this is not a repeated per-method p95.

Timing capture was enabled with `ANTIPHON_TEST_PHASE_ROOT` and run/group/attempt/suite metadata. The batch contains 423 completed timing cases. The shared DB fixture recorded container start 7.883s and migration 43.442s. Captured phases are partial: readiness, clone/restore, individual external-call and teardown attribution remain unknown where not emitted; unmeasured time is remainder, not zero. Several fixture records retain `outcome=incomplete` even with completed phase spans; they do not override TRX verdicts. Native test IDs in TRX stdout join timing rows; TRX GUIDs are distinct.

Ignored evidence root: `/work/worktrees/task-dd3c1b63/.antiphon/triage-dd3c1b63`. It contains the class/group manifest, exact combined filter, commands, source cleanliness observations, DLL digest, logs, TRXs, parsed rows, timing JSONL, and producer-owned output inventory/restoration record. Essential rows are preserved in this committed report. All 33 inventoried `bin-dd3c1b63/` directories were removed after all drivers exited. Generated evidence is not committed. The owned detached test checkout was removed after source cleanliness and output cleanup were recorded.

Built `Antiphon.Tests.dll`: **69040640 bytes**; SHA-256 `81b634e99f4ffad256505084017e05ad6e78b429a2f05dacd48927f88bac2e6f`.

| Evidence | SHA-256 |
|---|---|
| `all/all.trx` | `630e5cdeec09840780b6b35e900fdc67260db321fe97e6fba7da336269544953` |
| `alarm-isolated/isolated.trx` | `19fd2f53454a0c77327349ee202d406638dabe8938432b71c2a4b811d474ee2c` |
| `build.log` | `330874dc2c88c40ae8d59807cb29cc65e945c43cc6717569fb7c4340685a56b3` |
| `all.log` | `a0f1ba63da2fd197cadc9f61d03d7df3e9f64c2fa0c4419992ca717643675da5` |
| `alarm-isolated.log` | `31370cf7438b883d82b8509fe9562ea1ffee2ab4cdb922b26bf7ea96e32c46da` |

## Results by group and class

Counts are **passed / failed / skipped** on Linux. A passing row means only that no failure was observed in this run. Cluster labels are guesses unless the mechanism section explicitly confirms them.

| Group | Class | P / F / S | Cluster / outstanding platform coverage |
|---|---|---:|---|
| 39 | AgentTaskInputFallbackTests | 13 / 0 / 0 | No Linux failure observed |
| **39 total** | **1 classes** | **13 / 0 / 0** | No reproduced Linux failure |
| 40 | AgentTaskLandEnqueueTests | 6 / 0 / 0 | No Linux failure observed |
| 40 | SessionRuntimeActionTemporaryBlockTests | 4 / 0 / 0 | No Linux failure observed |
| 40 | BlockedTaskSyncRecoveryTests | 4 / 0 / 0 | No Linux failure observed |
| 40 | TaskParkPublicationTests | 4 / 0 / 0 | No Linux failure observed |
| 40 | PermissionWaitTests | 12 / 0 / 0 | No Linux failure observed |
| **40 total** | **5 classes** | **30 / 0 / 0** | No reproduced Linux failure |
| 41 | GrokRulesCompactionRecoveryTests | 3 / 8 / 0 | DI / asynchronous runtime-action fixture; no action worker |
| 41 | DurableRunnerSpillReceiptTests | 10 / 1 / 0 | CARD-0680 dialog-observation fixture candidate |
| 41 | TaskPlatformDispatchTests | 10 / 1 / 0 | dispatch admission / fixture eligibility unresolved |
| 41 | CardFileIgnoreAcceptanceTests | 11 / 0 / 0 | No Linux failure observed |
| **41 total** | **4 classes** | **34 / 10 / 0** | Debug follow-up required |
| 42 | HostOccupancySampleEndpointTests | 10 / 0 / 0 | No Linux failure observed |
| 42 | ChannelBatchingTests | 10 / 0 / 0 | No Linux failure observed |
| 42 | OrchestratorInstructionsRendererTests | 10 / 0 / 0 | No Linux failure observed |
| 42 | AgentTaskStandingAgentDispatchTests | 8 / 2 / 0 | DI: runtime fixture lacks SessionStateStore/action processing |
| 42 | DelegationScopeHoldTests | 8 / 2 / 0 | worktree dispatch / fixture graph unresolved |
| 42 | ExternalTrackerSyncLandingColumnTests | 4 / 1 / 0 | fixture cleanup FK ordering |
| **42 total** | **6 classes** | **50 / 5 / 0** | Debug follow-up required |
| 43 | RunnerSlotEndpointTests | 10 / 0 / 0 | No Linux failure observed |
| 43 | DispatcherAdmissionSchedulingTests | 3 / 0 / 0 | No Linux failure observed |
| 43 | WatchdogServiceTests | 7 / 3 / 0 | CARD-0680 dialog observation; ANSI/menu and caller receipt also unresolved |
| 43 | AgentSessionInterruptedLaunchResumeTests | 9 / 0 / 0 | No Linux failure observed |
| 43 | BlockedTaskParkReleaseTests | 2 / 1 / 0 | warm reuse / fixture eligibility unresolved |
| 43 | SessionMessageQueueBootWedgeTests | 10 / 0 / 0 | No Linux failure observed |
| **43 total** | **6 classes** | **41 / 4 / 0** | Debug follow-up required |
| 44 | RemoteCompletionSpillTests | 9 / 0 / 0 | No Linux failure observed |
| 44 | CapacityRecoveryQueueTests | 6 / 2 / 0 | channel correlation prefix / stale body assertions |
| 44 | SessionGenerationExitTests | 8 / 1 / 0 | runtime-action fixture or event timing; gate never observed |
| **44 total** | **3 classes** | **23 / 3 / 0** | Debug follow-up required |
| 46 | SessionRunnerEventPumpTests | 8 / 0 / 0 | No Linux failure observed |
| 46 | SessionGenerationDeliveryOverlapTests | 4 / 0 / 0 | No Linux failure observed |
| 46 | DelegateLaunchArgvIntegrityTests | 0 / 0 / 7 | Windows-only runtime/argv cases; native coverage owed |
| 46 | StandingSessionQueueSwitchTests | 6 / 2 / 0 | CARD-0680 dialog-observation fixture candidate |
| 46 | StartRefRepairAdoptionTests | 6 / 2 / 0 | Shouldly diagnostic failure hides a failing Git-trace predicate |
| **46 total** | **5 classes** | **24 / 4 / 7** | Debug follow-up required |
| 47 | PhoneHomeLaunchTransportTests | 7 / 0 / 0 | No Linux failure observed |
| 47 | SessionMessageQueueSpillTests | 7 / 0 / 0 | No Linux failure observed |
| 47 | MultiRunnerDirectoryTests | 7 / 0 / 0 | No Linux failure observed |
| 47 | PhoneHomeDirectoryTests | 8 / 0 / 0 | No Linux failure observed |
| **47 total** | **4 classes** | **29 / 0 / 0** | No reproduced Linux failure |
| 48 | CardServiceTrackerPushTests | 0 / 6 / 0 | DI lifetime: reused disposed scoped AppDbContext |
| 48 | GrokDelegateEndToEndTests | 0 / 0 / 6 | Windows-only runtime/argv cases; native coverage owed |
| 48 | DefaultRunnerPinTests | 6 / 0 / 0 | No Linux failure observed |
| 48 | ExpectationReceiptTests | 5 / 1 / 0 | CARD-0680 dialog-observation fixture candidate |
| 48 | DelegateScriptAdoptTests | 5 / 1 / 0 | stale CLI output expectation (added clean field) |
| 48 | ChannelOutboundRetentionTests | 5 / 1 / 0 | global transcript query / shared-state candidate |
| **48 total** | **6 classes** | **21 / 9 / 6** | Debug follow-up required |
| 50 | HostStatsEndpointTests | 5 / 0 / 0 | No Linux failure observed |
| 50 | PhoneHomeSessionRoutingTests | 5 / 0 / 0 | No Linux failure observed |
| 50 | ClosingReportNudgeLifecycleTests | 5 / 0 / 0 | No Linux failure observed |
| 50 | MutationDispatchTests | 4 / 1 / 0 | dispatch admission / fixture eligibility unresolved |
| **50 total** | **4 classes** | **19 / 1 / 0** | Debug follow-up required |
| 52 | HostEndpointTests | 4 / 0 / 0 | No Linux failure observed |
| 52 | DispatcherRemotePrepStarvationTests | 0 / 4 / 0 | remote-prep scheduling / fixture timing unresolved |
| 52 | HerdrLabelFollowWireTests | 4 / 0 / 0 | No Linux failure observed |
| 52 | MatrixChildReceiptTests | 0 / 4 / 0 | CARD-0680: fake adapter has no enabled dialog observations |
| 52 | MultiRunnerProjectionTests | 4 / 0 / 0 | No Linux failure observed |
| 52 | ClaudeDialogDeliveryTests | 2 / 0 / 0 | No Linux failure observed |
| 52 | PhoneHomeSchedulePreviewTests | 4 / 0 / 0 | No Linux failure observed |
| **52 total** | **7 classes** | **18 / 8 / 0** | Debug follow-up required |
| 53 | RunnerRetireJobTests | 4 / 0 / 0 | No Linux failure observed |
| 53 | RunnerCatalogueTests | 4 / 0 / 0 | No Linux failure observed |
| **53 total** | **2 classes** | **8 / 0 / 0** | No reproduced Linux failure |
| 54 | EffectiveSettingsPipelineTests | 3 / 0 / 0 | No Linux failure observed |
| 54 | PhoneHomeDeferredKillTests | 3 / 0 / 0 | No Linux failure observed |
| 54 | DefaultRunnerEligibilityTests | 3 / 0 / 0 | No Linux failure observed |
| 54 | PhoneHomeRecoveryEligibilityTests | 13 / 0 / 0 | No Linux failure observed |
| 54 | PhoneHomeSpillTransportTests | 5 / 0 / 0 | No Linux failure observed |
| 54 | DelegationBriefCeilingPtyTests | 1 / 0 / 2 | Windows-only runtime/argv cases; native coverage owed |
| 54 | DispatchConcurrencyMigrationTests | 2 / 1 / 0 | migration fixture uses current EF model on old schema |
| 54 | PhoneHomeRunnerRetirementCycleTests | 1 / 0 / 0 | No Linux failure observed |
| **54 total** | **8 classes** | **31 / 1 / 2** | Debug follow-up required |
| 55 | CardTrackerPushApiTests | 0 / 2 / 0 | After(Test) cleanup FK ordering |
| 55 | SessionMessageQueueSupervisionTests | 3 / 0 / 0 | No Linux failure observed |
| **55 total** | **2 classes** | **3 / 2 / 0** | Debug follow-up required |
| 56 | PhoneHomeRestartResumeTests | 2 / 0 / 0 | No Linux failure observed |
| 56 | HealthEndpointTests | 1 / 1 / 0 | stale capability-list expectation |
| 56 | HostCleanupOrchestrationTests | 1 / 1 / 0 | migration fixture uses current EF model on old schema |
| **56 total** | **3 classes** | **4 / 2 / 0** | Debug follow-up required |
| 57 | UnixDelegateLaunchArgvTests | 0 / 2 / 0 | DI disposal, not an observed argv byte mismatch |
| 57 | CardCleanupSchedulingSchemaTests | 0 / 1 / 0 | migration fixture expects named historical target to be latest |
| 57 | ChannelPreamblePresetEndpointTests | 0 / 1 / 0 | stale preamble contract or product copy; intent unresolved |
| 57 | ChannelOutboundComposedTransportTests | 1 / 0 / 0 | No Linux failure observed |
| **57 total** | **4 classes** | **1 / 4 / 0** | Debug follow-up required |
| 58 | RunnerAlarmNotifierTests | 0 / 1 / 0 | confirmed shared-state assertion; isolated method passes |
| 58 | CompletionNoteDurableSweepTests | 1 / 0 / 0 | No Linux failure observed |
| 58 | GrokSignInRuntimeTests | 0 / 0 / 1 | Windows-only runtime/argv cases; native coverage owed |
| 58 | RunnerAlarmWiringTests | 0 / 1 / 0 | stale DI identity assertion after composite observer |
| 58 | GrokSignInIncidentTests | 1 / 0 / 0 | No Linux failure observed |
| 58 | SessionContextUsagePersistenceTests | 0 / 1 / 0 | DI: manually constructed runtime omits SessionStateStore |
| **58 total** | **6 classes** | **2 / 3 / 1** | Debug follow-up required |

## Failing methods and evidence rows

Every line below is one observed failed case. Argument vectors are retained, so the eight Grok and four remote-prep failures are not collapsed into one case. Failure messages are condensed without changing their observed expected/actual values. Source location is the first relevant test frame, or the actual cleanup method when TUnit reports only its hook frame.

| Group / class | Failed method / vector | One-line error | Source at tested SHA | A: testId; transcript line |
|---|---|---|---|---|
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(live, standing)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: live must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `65915f24-a2c5-9310-549d-4715c54f3005`; `all.log:1613` |
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(live, channel)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: live must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `8b1e39ed-2853-4658-7e09-4c173a631a88`; `all.log:1639` |
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(live, retired-pool)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: live must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `644c73c7-5c6c-5728-1e9e-3010c3c79331`; `all.log:1665` |
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(sync, standing)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: sync must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `b694396a-6ea7-6241-82db-3ff7b1570585`; `all.log:1691` |
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(sync, channel)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: sync must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `ac63c5e5-86d0-3a20-38fb-3b7cf8f73bf4`; `all.log:1717` |
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(sync, retired-pool)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: sync must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `6467c9dc-8440-b0a9-f8a5-ab9d8d1e8863`; `all.log:1743` |
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(blocked-herdr, standing)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: blocked-herdr must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `b15bfd80-39ca-d3a3-fbaa-3e155c8d72f3`; `all.log:1769` |
| 41 / GrokRulesCompactionRecoveryTests | `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(in-flight, standing)` | ShouldAssertException: await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null) should be 1 but was 0 Additional Info: in-flight must create the trigger before another recovery lane can mask its absence | `tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:131` | `7e92e889-b021-d40d-0e02-bcb2ffb4278b`; `all.log:1795` |
| 41 / DurableRunnerSpillReceiptTests | `Queued_brief_is_written_by_the_runner_when_a_busy_recipient_becomes_eligible` | ShouldAssertException: h.Adapter.SubmittedBodies should not be empty but was | `tests/Antiphon.Tests/Application/DurableRunnerSpillReceiptTests.cs:457` | `641eaa63-6d61-185e-b4ea-599093b10f2f`; `all.log:418` |
| 41 / TaskPlatformDispatchTests | `C772_Allowed_hosts_and_kinds_still_dispatch` | ShouldAssertException: stored.Status should be AgentTaskStatus.Dispatched but was AgentTaskStatus.Queued | `tests/Antiphon.Tests/Application/TaskPlatformDispatchTests.cs:240` | `908929e1-a65c-9784-9a92-160691db315f`; `all.log:638` |
| 42 / AgentTaskStandingAgentDispatchTests | `a_marked_turn_on_the_standing_session_settles_the_pinned_task_and_leaves_the_agent_alone` | ShouldAssertException: settled.Status should be AgentTaskStatus.Succeeded but was AgentTaskStatus.Dispatched Additional Info: the turn-end trigger fires for a session the dispatcher did not launch | `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs:353` | `457581dc-80a5-d564-0198-71d1142703b4`; `all.log:135` |
| 42 / AgentTaskStandingAgentDispatchTests | `an_unmarked_turn_on_the_standing_session_is_nudged_and_still_leaves_the_agent_alone` | ShouldAssertException: stored.ReportNudgedAt should not be null but was | `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs:398` | `fbe604f9-eaa0-b4a5-9983-8068e626bbf7`; `all.log:158` |
| 42 / DelegationScopeHoldTests | `two_worktree_tasks_in_one_area_both_run` | ShouldAssertException: (await verify.AgentTasks.SingleAsync(t => t.Id == second.Id)).Status should be AgentTaskStatus.Dispatched but was AgentTaskStatus.Failed | `tests/Antiphon.Tests/Application/DelegationScopeHoldTests.cs:200` | `036006d7-129a-f14a-dca6-b2c8045ee513`; `all.log:175` |
| 42 / DelegationScopeHoldTests | `a_worktree_task_dispatches_over_a_shared_holder_with_one_warning` | ShouldAssertException: (await verify.AgentTasks.SingleAsync(t => t.Id == isolated.Id)).Status should be AgentTaskStatus.Dispatched but was AgentTaskStatus.Failed Additional Info: a worktree collides at merge, not at dispatch | `tests/Antiphon.Tests/Application/DelegationScopeHoldTests.cs:170` | `1225e701-23b6-f9cc-0fd0-90eb22672026`; `all.log:197` |
| 42 / ExternalTrackerSyncLandingColumnTests | `Default_mode_still_moves_closed_to_terminal` | PostgreSQL 23503 deleting CardRevisions: FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId still references a revision (After(Test) cleanup for API cases). | `tests/Antiphon.Tests/Application/ExternalTrackerSyncLandingColumnTests.cs:338` | `e0567958-6f87-8544-ca0f-34aa70562a1f`; `all.log:1590` |
| 43 / WatchdogServiceTests | `C566_Auto_refused_delete_finishes_failed_with_reason_and_caller_receipt` | ShouldAssertException: prompts.Count(t => t.Text == note.Body) should be 1 but was 0 | `tests/Antiphon.Tests/Application/WatchdogServiceTests.cs:205` | `aa041439-5b3e-4fe0-c538-ed5b35f3e3b6`; `all.log:14` |
| 43 / WatchdogServiceTests | `C566_Unsafe_delete_refusal_preserves_a_pending_answer` | ProviderDialogInputRefusedException: Automated Claude input withheld: modal-blocked | `tests/Antiphon.Tests/Application/WatchdogServiceTests.cs:80` | `5fb553fc-c186-6d39-3345-496467e15873`; `all.log:40` |
| 43 / WatchdogServiceTests | `C566_Unsafe_delete_refusal_requires_the_active_exact_menu` | ShouldAssertException: sent should be 1 but was 0 Additional Info: ansi | `tests/Antiphon.Tests/Application/WatchdogServiceTests.cs:50` | `fd55d03a-b37f-2bc4-e9d7-18b89e641310`; `all.log:51` |
| 43 / BlockedTaskParkReleaseTests | `C1065_ParkedAgentIsReservedButNotWarm` | ShouldAssertException: (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>() .TryReuseWarmAgentAsync(reusable, world.Now, default)) should be ReuseOutcome.Reused but was ReuseOutcome.SpawnFresh Additional Info: G-111 otherwise reusable control | `tests/Antiphon.Tests/Application/BlockedTaskParkReleaseTests.cs:370` | `52f7e497-e16d-3d72-2500-38045c994c8b`; `all.log:227` |
| 44 / CapacityRecoveryQueueTests | `Card0412_V12_held_channel_row_delivers_after_grant` | ShouldAssertException: row.Body should be "channel body" but was "[antiphon-channel:d48adbafaf264f8cbb2823bdb2c65452] channel body" | `tests/Antiphon.Tests/Application/CapacityRecoveryQueueTests.cs:40` | `9217933a-af48-aefe-f21b-3f5c62425556`; `all.log:695` |
| 44 / CapacityRecoveryQueueTests | `Card0412_V12_unselected_held_row_stays_pending` | InvalidOperationException: Sequence contains no elements. | `tests/Antiphon.Tests/Application/CapacityRecoveryQueueTests.cs:107` | `c2056053-f146-d66b-dac1-0712a25881fa`; `all.log:750` |
| 44 / SessionGenerationExitTests | `C502_V8_A_exit_already_in_the_stream_behind_a_gated_event_is_consumed_after_B_and_ignored` | TimeoutException: The operation has timed out. | `tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs:397` | `0463d244-1d0d-786c-912a-f8732ab710bd`; `all.log:540` |
| 46 / StandingSessionQueueSwitchTests | `Only_unattempted_messages_move_atomically_and_keep_order_and_routing` | ShouldAssertException: adapter.SubmittedBodies should be ["Explicit recovery input once"] but was (case sensitive comparison) [] (System.Collections.Generic.List`1[System.String]) | `tests/Antiphon.Tests/Application/StandingSessionQueueSwitchTests.cs:214` | `db318f4b-6060-b395-a0a9-71ce2d11b254`; `all.log:2339` |
| 46 / StandingSessionQueueSwitchTests | `Target_late_confirmation_and_open_task_guards_remain_intact` | ShouldAssertException: done.AgentSessionId should be QueuedMessageStatus.Sent but was QueuedMessageStatus.Pending | `tests/Antiphon.Tests/Application/StandingSessionQueueSwitchTests.cs:77` | `0ed15a9c-ab4f-9ecd-6f07-26d33ac9c6fc`; `all.log:2365` |
| 46 / StartRefRepairAdoptionTests | `C675_RepairCutBelowBothOwnerTipsRefusesLineage` | NotImplementedException: The method or operation is not implemented. | `tests/Antiphon.Tests/Application/StartRefRepairAdoptionTests.cs:84` | `ad1833ac-ac2e-13b7-13bd-9773952aca8d`; `all.log:574` |
| 46 / StartRefRepairAdoptionTests | `C675_DetachedOwnerMirrorRefusesBeforeMutation` | NotImplementedException: The method or operation is not implemented. | `tests/Antiphon.Tests/Application/StartRefRepairAdoptionTests.cs:103` | `95c9a415-2031-21a3-5376-a4a671de5b9d`; `all.log:594` |
| 48 / CardServiceTrackerPushTests | `Tracker_close_payload_excludes_current_and_historical_private_notes` | ObjectDisposedException: AppDbContext was already disposed when CardService accessed Cards. | `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:35` | `c8b1c511-ce84-b444-a143-a1c59c936afa`; `all.log:762` |
| 48 / CardServiceTrackerPushTests | `MoveAsync_into_Done_on_a_linked_card_pushes_Closed` | ObjectDisposedException: AppDbContext was already disposed when CardService accessed Cards. | `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:60` | `f4b9ea43-2a89-1c43-d57c-2ee75273faae`; `all.log:777` |
| 48 / CardServiceTrackerPushTests | `Backlog_to_InProgress_on_a_linked_card_does_not_push` | ObjectDisposedException: AppDbContext was already disposed when CardService accessed Cards. | `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:116` | `1ba8e99d-9d5b-f847-dfbd-f9be629518f4`; `all.log:792` |
| 48 / CardServiceTrackerPushTests | `ReopenAsync_on_a_linked_card_pushes_Reopened` | ObjectDisposedException: AppDbContext was already disposed when CardService accessed Cards. | `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:141` | `d0569b5a-cfc2-2e20-cdd8-3c5b30c8eab5`; `all.log:807` |
| 48 / CardServiceTrackerPushTests | `Unlinked_card_move_has_null_TrackerPush_and_zero_writes` | ObjectDisposedException: AppDbContext was already disposed when CardService accessed Cards. | `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:91` | `78203cd1-10d5-7e6d-f25c-0ed405bc46cf`; `all.log:822` |
| 48 / CardServiceTrackerPushTests | `Fake_throwing_still_leaves_the_card_Done_with_Failed_reason` | ObjectDisposedException: AppDbContext was already disposed when CardService accessed Cards. | `tests/Antiphon.Tests/Application/CardServiceTrackerPushTests.cs:174` | `75a52e66-9d5e-18ef-1d6f-8625b6f33213`; `all.log:837` |
| 48 / ExpectationReceiptTests | `C650_New_session_does_not_adopt_old_attempt` | ShouldAssertException: replacementAdapter.Inputs should contain "Expectation nudge e4fe5d15-3602-42ae-aea0-92d57ee7a19f: 3 queued tasks held. Reply [expectation-ack:e4fe5d15-3602-42ae-aea0-92d57ee7a19f]" but was actually [] (System.Collections.Generic.List`1[System.String]) | `tests/Antiphon.Tests/Application/ExpectationReceiptTests.cs:179` | `aa5c7e8f-3241-ff54-fcd6-abb802e1c3e8`; `all.log:614` |
| 48 / DelegateScriptAdoptTests | `C675_StatusPrintsReviewEvidenceOfAReviewTask` | Expected contiguous reviewed <SHA>; ref ... output; CLI now emits reviewed <SHA>; clean ; ref ... . | `tests/Antiphon.Tests/Application/DelegateScriptAdoptTests.cs:53` | `cf09252d-3503-3e30-3e0f-4f1d5a06bd0a`; `all.log:256` |
| 48 / ChannelOutboundRetentionTests | `C519_Closed_roots_eventually_become_prunable` | ShouldAssertException: await db.TranscriptEntries.AnyAsync() should be False but was True | `tests/Antiphon.Tests/Application/ChannelOutboundRetentionTests.cs:240` | `5fc214b0-42b6-22a0-7b8a-11e4e4a6f3e6`; `all.log:1267` |
| 50 / MutationDispatchTests | `C470_next_card_code_dispatches_during_mutation` | ShouldAssertException: dispatched.Status should be AgentTaskStatus.Dispatched but was AgentTaskStatus.Queued | `tests/Antiphon.Tests/Application/MutationDispatchTests.cs:107` | `c374497d-4bab-ffcd-e6ab-3c725f118352`; `all.log:489` |
| 52 / DispatcherRemotePrepStarvationTests | `Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient(after-receipt, False)` | ShouldAssertException: peer.RequestCount(PhoneHomeOperation.WorkspaceMirror) should be 3 but was 0 | `tests/Antiphon.Tests/Application/DispatcherRemotePrepStarvationTests.cs:112` | `b78fd384-b169-350f-2f38-2169efc53def`; `all.log:310` |
| 52 / DispatcherRemotePrepStarvationTests | `Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient(after-receipt, True)` | ShouldAssertException: peer.RequestCount(PhoneHomeOperation.WorkspaceMirror) should be 3 but was 0 | `tests/Antiphon.Tests/Application/DispatcherRemotePrepStarvationTests.cs:112` | `81d96d7f-e45b-45cd-dfa6-547575cfa28a`; `all.log:337` |
| 52 / DispatcherRemotePrepStarvationTests | `Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient(queue-inserted, False)` | ShouldAssertException: peer.RequestCount(PhoneHomeOperation.WorkspaceMirror) should be 3 but was 0 | `tests/Antiphon.Tests/Application/DispatcherRemotePrepStarvationTests.cs:112` | `e653c81c-86ae-7626-1ae6-95fd38dbd053`; `all.log:364` |
| 52 / DispatcherRemotePrepStarvationTests | `Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient(lost-wakeup, False)` | ShouldAssertException: peer.RequestCount(PhoneHomeOperation.WorkspaceMirror) should be 3 but was 0 | `tests/Antiphon.Tests/Application/DispatcherRemotePrepStarvationTests.cs:112` | `bb3733a8-8753-cbcc-b68e-e52458455f15`; `all.log:391` |
| 52 / MatrixChildReceiptTests | `A_blocked_merge_child_note_reaches_the_parent_once_as_a_complete_user_prompt` | ShouldAssertException: row.Status should be QueuedMessageStatus.Sent but was QueuedMessageStatus.Pending | `tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs:247` | `192a8b87-c07b-b8e1-4432-d6bdc139d819`; `all.log:1840` |
| 52 / MatrixChildReceiptTests | `A_committed_blocked_child_note_is_recovered_once_by_the_stranded_sweep` | ShouldAssertException: await recovered.FlushStrandedQueuesAsync(default) should be 1 but was 0 | `tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs:116` | `ef173598-9329-42d5-7670-a2dfc3b57dbe`; `all.log:1976` |
| 52 / MatrixChildReceiptTests | `A_blocked_commit_child_note_and_the_settled_tasks_completion_each_reach_the_parent_once_in_queue_order` | ShouldAssertException: row.Status should be QueuedMessageStatus.Sent but was QueuedMessageStatus.Pending | `tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs:247` | `2d62943e-fedc-1e3f-8732-b32009827226`; `all.log:2092` |
| 52 / MatrixChildReceiptTests | `A_blocked_commit_child_note_reaches_a_busy_parent_once_after_turn_end` | ShouldAssertException: row.Status should be QueuedMessageStatus.Sent but was QueuedMessageStatus.Pending | `tests/Antiphon.Tests/Application/MatrixChildReceiptTests.cs:247` | `bb9be765-6b8f-1105-702f-d54bd70524b4`; `all.log:2205` |
| 54 / DispatchConcurrencyMigrationTests | `Upgrade_populated_database_preserves_task_rows` | DbUpdateException; PostgreSQL 42703: Projects.ProviderEndpointsJson does not exist in rewound schema. | `tests/Antiphon.Tests/Application/DispatchConcurrencyMigrationTests.cs:32` | `50826fd4-5f7a-184c-322f-8a569ce877fa`; `all.log:277` |
| 55 / CardTrackerPushApiTests | `Unlinked_card_patch_json_has_no_trackerPush_property` | PostgreSQL 23503 deleting CardRevisions: FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId still references a revision (After(Test) cleanup for API cases). | `tests/Antiphon.Tests/Application/CardTrackerPushApiTests.cs:48` | `aa9ea851-c52e-b02f-972f-660a6915a033`; `all.log:857` |
| 55 / CardTrackerPushApiTests | `Linked_card_patch_json_has_outcome_Closed` | PostgreSQL 23503 deleting CardRevisions: FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId still references a revision (After(Test) cleanup for API cases). | `tests/Antiphon.Tests/Application/CardTrackerPushApiTests.cs:48` | `063696df-ce13-d9dd-9ac4-2d71a474bf18`; `all.log:1227` |
| 56 / HealthEndpointTests | `C495_VersionAdvertisesLandV2` | ShouldAssertException: body.Capabilities should be ["land-v2"] but was (case sensitive comparison) ["land-v2", "operator-shutdown-v1"] | `tests/Antiphon.Tests/Application/HealthEndpointTests.cs:59` | `bca7ff56-9a73-8810-cede-177de3baba89`; `all.log:1821` |
| 56 / HostCleanupOrchestrationTests | `AddHostCleanup_upgrades_current_master_without_changing_existing_rows` | DbUpdateException; PostgreSQL 42703: AgentTasks.ReleasedSeatAnswer does not exist in predecessor schema. | `tests/Antiphon.Tests/Application/HostCleanupOrchestrationTests.cs:61` | `04d36da4-64d7-f50e-dcd6-58322cc77093`; `all.log:441` |
| 57 / UnixDelegateLaunchArgvTests | `Composed_delegate_argv_reaches_native_child(Investigate)` | InvalidOperationException: 'Antiphon.Server.Application.Services.CardSiblingInspectionQueue' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container. | `tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs:72` | `b474af18-983d-cf7c-594c-a3efbefde834`; `all.log:670` |
| 57 / UnixDelegateLaunchArgvTests | `Composed_delegate_argv_reaches_native_child(Code)` | InvalidOperationException: 'Antiphon.Server.Application.Services.CardSiblingInspectionQueue' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container. | `tests/Antiphon.Tests/Application/UnixDelegateLaunchArgvTests.cs:72` | `89818080-d5fa-9f35-406d-6eb9dab64e44`; `all.log:680` |
| 57 / CardCleanupSchedulingSchemaTests | `C1033_AdditiveUpgradePreservesHistory` | GetPendingMigrationsAsync expected empty after named historical upgrade; 16 later migrations remained. | `tests/Antiphon.Tests/Application/CardCleanupSchedulingSchemaTests.cs:58` | `d4158f69-e6df-cee2-2e71-fb9e5fbd1f5d`; `all.log:76` |
| 57 / ChannelPreamblePresetEndpointTests | `Preset_endpoint_returns_each_known_provider_and_404_for_unknown` | Slack preset equals current constant but lacks expected sentence: Prefer PDF for documents — Slack shows HTML files as a text snippet, not a document. | `tests/Antiphon.Tests/Application/ChannelPreamblePresetEndpointTests.cs:35` | `c1582153-012f-028c-583d-a3f55ac89372`; `all.log:1293` |
| 58 / RunnerAlarmNotifierTests | `a_note_is_a_pending_whenidle_system_row_hinted_and_never_typed_inline` | ShouldAssertException: db.SessionQueuedMessages.Count(message => message.AgentSessionId != harness.SessionId) should be 0 but was 19 | `tests/Antiphon.Tests/Application/RunnerAlarmNotifierTests.cs:40` | `5c018cbd-bc09-4a6a-d2d6-59acfde7cfa2`; `all.log:548` |
| 58 / RunnerAlarmWiringTests | `program_wires_the_loop_the_observers_and_the_default_exclusion` | Expected Observer identity AlarmWakeQueue; actual CompositeRunnerEligibilityObserver. | `tests/Antiphon.Tests/Application/RunnerAlarmWiringTests.cs:37` | `1432bb7f-83c7-e781-b64e-3cb863ec497a`; `all.log:2320` |
| 58 / SessionContextUsagePersistenceTests | `Persisted_98c61e03_shape_computes_anchor_based_fullness` | ShouldAssertException: stored.Count should be 5 but was 0 | `tests/Antiphon.Tests/Application/SessionContextUsagePersistenceTests.cs:82` | `8cb47438-440f-de65-171f-78ac592d17ad`; `all.log:517` |

## Mechanisms reconstructed from current source and stored results

1. **Confirmed shared-state dependency (group 58).** `RunnerAlarmNotifierTests.cs:40` counts every queue row outside its own session and expects zero. The batch TRX records **19** unrelated rows, after all its scoped row/flush/input assertions passed. The same exact method in a fresh test process passed 1/1 (TRX `alarm-isolated/isolated.trx`, testId `5c018cbd-bc09-4a6a-d2d6-59acfde7cfa2`). The failing batch assertion therefore depends on other fixtures' state; no product alarm failure is established. `ChannelOutboundRetentionTests.cs:237` also ages all transcript rows and `:240` expects a global empty transcript table; that failure has the same risk, but was not isolation-rerun.

2. **Confirmed disposed DI context (six group-48 cases).** `BridgeQueueHarness.cs:319` disposes the `AppDbContext` resolved from its retained scope, then returns that same scope at `:382`. The tests resolve `CardService` from it (for example `CardServiceTrackerPushTests.cs:35`, `:59`); the scoped service obtains the same disposed context and fails in `CardService.cs:1866`, before tracker assertions. This is a fixture lifetime mechanism reconstructed from source and six matching exception stacks.

3. **Confirmed missing runtime dependency / asynchronous fixture assumptions.** `SessionContextUsagePersistenceTests.cs:54` manually constructs `AgentSessionRuntime` without `SessionStateStore`; the default is null (`AgentSessionRuntime.cs:94`, `:74`). Transcript ingestion requires it at `:1262` and catches/logs the exception at `:1276`, matching the zero stored rows at test `:82`. The standing-agent harness registers runtime at `AgentTaskStandingAgentDispatchTests.cs:527` but has no state-store or action-coordinator registration; its two turn-end tests fail to settle/nudge after ingestion. Separately, `GrokRulesCompactionRecoveryTests.cs:118`/`:119` assumes observe/sync has executed recovery before the assertion at `:131`. The runtime persists and merely hints actions (`AgentSessionRuntime.cs:1257`, `:1273`); compaction recovery is action phase 3 at `:414`. `BridgeQueueHarness.HarnessOptions.StartRuntimeActions` is false unless set (`BridgeQueueHarness.cs:62`); the worker is conditional at `:267`, and this class does not enable it. Its eight live/sync/blocked/in-flight rows fail while the three explicit startup-recovery rows pass. Missing action execution is a confirmed fixture mechanism; this does not establish a production compaction failure. The group-44 stream gate waits for `SessionTranscript` at `SessionGenerationExitTests.cs:397`; publication now occurs in runtime action phase 0 (`AgentSessionRuntime.cs:392`). Worker setup and event-gate timing need a targeted reconstruction before assigning that timeout solely to timing.

4. **Confirmed current-model/historical-schema fixture mismatches.** `DispatchConcurrencyMigrationTests.cs:30` rewinds then saves current Project entities at `:32`; the transcript identifies missing `ProviderEndpointsJson`. `HostCleanupOrchestrationTests.cs:46` rewinds and saves current AgentTask entities at `:61`; the transcript identifies missing `ReleasedSeatAnswer`. `CardCleanupSchedulingSchemaTests.cs:56` migrates only to the named historical `AddCardCleanupScheduling` target, then `:58` expects no pending migrations; the exact stored error lists 16 newer migrations. These failures precede their intended upgrade assertions and are not evidence that the product upgrade loses data.

5. **Confirmed cleanup FK failures (groups 42/55).** `ExternalTrackerSyncLandingColumnTests.cs:338` and `CardTrackerPushApiTests.cs:48` delete CardRevisions before removing protected cleanup references. PostgreSQL reports SQLSTATE 23503 and the exact `FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId` constraint. Both group-55 cases fail in `After(Test)`, not their HTTP assertion bodies. This is cleanup failure evidence, not a reproduced tracker payload defect.

6. **Confirmed asynchronous DI disposal boundary (group 57).** `UnixDelegateLaunchArgvTests.cs:29` synchronously disposes its service provider. Both vectors end with the exception that `CardSiblingInspectionQueue` only implements `IAsyncDisposable`. No argv-byte assertion failure is reported; the observed failure does not demonstrate argument shredding. The method's Windows guard at `:21` throws rather than skips, so this Unix-only row must not be credited as a Windows qualification witness.

7. **Confirmed stale exact assertions.** `HealthEndpointTests.cs:59` expects only `land-v2`, while `VersionEndpoints.cs:22` advertises `land-v2` plus `operator-shutdown-v1`. `RunnerAlarmWiringTests.cs:37` requires Observer identity to be AlarmWakeQueue, while `Program.cs:351` registers a composite; `CompositeRunnerEligibilityObserver.cs:15` still forwards to AlarmWakeQueue. `DelegateScriptAdoptTests.cs:53` expects reviewed SHA immediately followed by ref, while `scripts/delegate.ps1:692` inserts the clean field. `CapacityRecoveryQueueTests.cs:40` expects plain channel body and `:107` selects by exact plain body; the stored row now has `[antiphon-channel:<id>]` correlation bytes (producer: `ChannelPromptCorrelation.cs:13`). These mismatches are observed without a source change. The preamble test first proves equality to the product constant at `ChannelPreamblePresetEndpointTests.cs:33`, then fails its older PDF sentence at `:35`; whether that copy change was intended still needs its owning requirement/history.

8. **Dialog-observation cluster: confirmed boundaries, incomplete attribution.** `WatchdogServiceTests.cs:80` receives `ProviderDialogInputRefusedException: modal-blocked`; `ProviderDialogObservationService.cs:39`–`:41` rejects ordinary input with that refusal. Its ANSI-menu vector and absent caller receipt are additional distinct failures. In `MatrixChildReceiptTests.cs:284`, the fake is registered without enabling dialog observations. The fake returns null while disabled (`FakeAgentProtocolAdapter.cs:392`), and null is refused by `ProviderDialogObservationService.cs:25`. This reconstructs a concrete CARD-0680 fixture incompatibility for that setup. The same cluster is plausible for queued spill, expectation replacement and standing-queue-switch delivery, but their persisted hold/refusal rows and matched-generation observations were not collected; their full causes remain unconfirmed.

9. **Assertion diagnostics hide the underlying trace finding (group 46).** Both `StartRefRepairAdoptionTests.cs:84`/`:103` reach a `ShouldNotContain` Git-trace predicate, then throw in Shouldly's `ExpressionStringBuilder.ToString(ExpressionType)`. The NotImplementedException is in diagnostic formatting, not the landing implementation. It must not be treated as proof that the asserted Git trace was safe; the trace entries that made the predicate fail remain to be captured.

## Remaining uncertainties and evidence needed

- Admission/reuse failures in groups 41, 42, 43 and 50 need the exact task FailureReason, Held events, warm-agent refusal decisions, and effective test fixture settings. Their statuses alone do not distinguish fixture graph omissions, capacity/shared-state interaction, or product admission defects. No Windows-path cause was confirmed.
- Group 52's remote-prep vectors do dispatch the local task (test `DispatcherRemotePrepStarvationTests.cs:95` passes) and observe queued remote tasks/held rows, but then see zero WorkspaceMirror requests at `:112` instead of three. Capture the asynchronous prep queue, peer pump and exact mirror-receipt state before calling this starvation or product loss.
- The group-44 event gate never enters within 5s; compare worker registration and durable phase progress against the intended gating boundary. A timeout is observed, not proof of a flaky clock.
- Dialog candidates need stored observation/refusal plus complete matching UserPrompt evidence, not screen text or submitted-body counts alone. No live product-session transcript was used.
- Preamble intent and the masked Git-trace predicate require their owning contract/history and actual trace respectively. Failed current-source assertions are not automatically product regressions.
- Windows-only cases listed below still need native execution. Current-master Linux passes do not classify the original rc failures as fixed or Windows-only.
- The final master tip advanced after the one commissioned build; its unselected test-script delta was inspected, but was not rebuilt. Do not credit this evidence as execution at that later SHA.

## Native rows still owed

Run only after the detached Windows qualification has released its native/process lane; use its owner-controlled host build-slot gate and a fresh isolated build. No desktop process, release lock, tag or running qualification was touched here.

| Group | Class | Linux skipped / other cases | Reason from source / transcript | Exact native class filter |
|---|---|---:|---|---|
| 46 | DelegateLaunchArgvIntegrityTests | 7 skipped | CommandLineToArgvW is Windows-only; `DelegateLaunchArgvIntegrityTests.cs:68` | `/*/*/DelegateLaunchArgvIntegrityTests*/*` |
| 48 | GrokDelegateEndToEndTests | 6 skipped | Windows raw-argv / ConPTY guards, `GrokDelegateEndToEndTests.cs:98`, `:126` | `/*/*/GrokDelegateEndToEndTests*/*` |
| 54 | DelegationBriefCeilingPtyTests | 2 skipped, 1 passed | Runtime cases require Windows ConPTY; `DelegationBriefCeilingPtyTests.cs:238` | `/*/*/DelegationBriefCeilingPtyTests*/*` |
| 58 | GrokSignInRuntimeTests | 1 skipped | Windows ConPTY; `GrokSignInRuntimeTests.cs:44` | `/*/*/GrokSignInRuntimeTests*/*` |

## Rerun recipe

The complete 76-class filter is preserved in `/work/worktrees/task-dd3c1b63/.antiphon/triage-dd3c1b63/filter.txt`; its class operands have the canonical trailing wildcard. The per-group rows below are independently rerunnable selections. Each OR is inside the class segment with parentheses, not an OR between full paths. Verify actual class membership and nonzero counters in fresh TRX output.

Build once in the intended source checkout:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label triage-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-triage/ --nologo --verbosity minimal
```

For a group, substitute its quoted FILTER and a fresh RESULTS directory; retain the process-tree hard bound in the invoking shell:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label triage-group -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-triage/ -- --treenode-filter 'FILTER' --maximum-parallel-tests 4 --report-trx --report-trx-filename run.trx --results-directory RESULTS
```

| Group | Exact class filter |
|---|---|
| 39 | `/*/*/(AgentTaskInputFallbackTests*)/*` |
| 40 | `/*/*/(AgentTaskLandEnqueueTests*)\|(SessionRuntimeActionTemporaryBlockTests*)\|(BlockedTaskSyncRecoveryTests*)\|(TaskParkPublicationTests*)\|(PermissionWaitTests*)/*` |
| 41 | `/*/*/(GrokRulesCompactionRecoveryTests*)\|(DurableRunnerSpillReceiptTests*)\|(TaskPlatformDispatchTests*)\|(CardFileIgnoreAcceptanceTests*)/*` |
| 42 | `/*/*/(HostOccupancySampleEndpointTests*)\|(ChannelBatchingTests*)\|(OrchestratorInstructionsRendererTests*)\|(AgentTaskStandingAgentDispatchTests*)\|(DelegationScopeHoldTests*)\|(ExternalTrackerSyncLandingColumnTests*)/*` |
| 43 | `/*/*/(RunnerSlotEndpointTests*)\|(DispatcherAdmissionSchedulingTests*)\|(WatchdogServiceTests*)\|(AgentSessionInterruptedLaunchResumeTests*)\|(BlockedTaskParkReleaseTests*)\|(SessionMessageQueueBootWedgeTests*)/*` |
| 44 | `/*/*/(RemoteCompletionSpillTests*)\|(CapacityRecoveryQueueTests*)\|(SessionGenerationExitTests*)/*` |
| 46 | `/*/*/(SessionRunnerEventPumpTests*)\|(SessionGenerationDeliveryOverlapTests*)\|(DelegateLaunchArgvIntegrityTests*)\|(StandingSessionQueueSwitchTests*)\|(StartRefRepairAdoptionTests*)/*` |
| 47 | `/*/*/(PhoneHomeLaunchTransportTests*)\|(SessionMessageQueueSpillTests*)\|(MultiRunnerDirectoryTests*)\|(PhoneHomeDirectoryTests*)/*` |
| 48 | `/*/*/(CardServiceTrackerPushTests*)\|(GrokDelegateEndToEndTests*)\|(DefaultRunnerPinTests*)\|(ExpectationReceiptTests*)\|(DelegateScriptAdoptTests*)\|(ChannelOutboundRetentionTests*)/*` |
| 50 | `/*/*/(HostStatsEndpointTests*)\|(PhoneHomeSessionRoutingTests*)\|(ClosingReportNudgeLifecycleTests*)\|(MutationDispatchTests*)/*` |
| 52 | `/*/*/(HostEndpointTests*)\|(DispatcherRemotePrepStarvationTests*)\|(HerdrLabelFollowWireTests*)\|(MatrixChildReceiptTests*)\|(MultiRunnerProjectionTests*)\|(ClaudeDialogDeliveryTests*)\|(PhoneHomeSchedulePreviewTests*)/*` |
| 53 | `/*/*/(RunnerRetireJobTests*)\|(RunnerCatalogueTests*)/*` |
| 54 | `/*/*/(EffectiveSettingsPipelineTests*)\|(PhoneHomeDeferredKillTests*)\|(DefaultRunnerEligibilityTests*)\|(PhoneHomeRecoveryEligibilityTests*)\|(PhoneHomeSpillTransportTests*)\|(DelegationBriefCeilingPtyTests*)\|(DispatchConcurrencyMigrationTests*)\|(PhoneHomeRunnerRetirementCycleTests*)/*` |
| 55 | `/*/*/(CardTrackerPushApiTests*)\|(SessionMessageQueueSupervisionTests*)/*` |
| 56 | `/*/*/(PhoneHomeRestartResumeTests*)\|(HealthEndpointTests*)\|(HostCleanupOrchestrationTests*)/*` |
| 57 | `/*/*/(UnixDelegateLaunchArgvTests*)\|(CardCleanupSchedulingSchemaTests*)\|(ChannelPreamblePresetEndpointTests*)\|(ChannelOutboundComposedTransportTests*)/*` |
| 58 | `/*/*/(RunnerAlarmNotifierTests*)\|(CompletionNoteDurableSweepTests*)\|(GrokSignInRuntimeTests*)\|(RunnerAlarmWiringTests*)\|(GrokSignInIncidentTests*)\|(SessionContextUsagePersistenceTests*)/*` |

## Not done, noted

No fix design, implementation, native rerun, deployment, source land or rc qualification was performed; only this evidence report is published.

## Ranked Debug handoff

Ranked by reproduced failing-case count, then breadth; this ranks investigation work, not established product severity. The counts below retain the original batch failures. The group-58 alarm method's isolated pass is explicitly noted.

| Rank | Group | Failed cases | Still-failing batch classes (failed case count) |
|---|---|---:|---|
| 1 | 41 | 10 | GrokRulesCompactionRecoveryTests (8); DurableRunnerSpillReceiptTests (1); TaskPlatformDispatchTests (1) |
| 2 | 48 | 9 | CardServiceTrackerPushTests (6); ExpectationReceiptTests (1); DelegateScriptAdoptTests (1); ChannelOutboundRetentionTests (1) |
| 3 | 52 | 8 | DispatcherRemotePrepStarvationTests (4); MatrixChildReceiptTests (4) |
| 4 | 42 | 5 | AgentTaskStandingAgentDispatchTests (2); DelegationScopeHoldTests (2); ExternalTrackerSyncLandingColumnTests (1) |
| 5 | 43 | 4 | WatchdogServiceTests (3); BlockedTaskParkReleaseTests (1) |
| 6 | 46 | 4 | StandingSessionQueueSwitchTests (2); StartRefRepairAdoptionTests (2) |
| 7 | 57 | 4 | UnixDelegateLaunchArgvTests (2); CardCleanupSchedulingSchemaTests (1); ChannelPreamblePresetEndpointTests (1) |
| 8 | 44 | 3 | CapacityRecoveryQueueTests (2); SessionGenerationExitTests (1) |
| 9 | 58 | 3 | RunnerAlarmNotifierTests (1; isolated 1/1 passed, shared-state dependency confirmed); RunnerAlarmWiringTests (1); SessionContextUsagePersistenceTests (1) |
| 10 | 55 | 2 | CardTrackerPushApiTests (2) |
| 11 | 56 | 2 | HealthEndpointTests (1); HostCleanupOrchestrationTests (1) |
| 12 | 50 | 1 | MutationDispatchTests (1) |
| 13 | 54 | 1 | DispatchConcurrencyMigrationTests (1) |

--- next stage ---
next: investigate
handoff: Commission Debug investigation for groups 41(10), 48(9), 52(8), 42(5), 43(4), 46(4), 57(4), 44(3), 58(3), 55(2), 56(2), 50(1), 54(1); preserve the 56 batch failures and isolated alarm pass, address confirmed fixture mechanisms separately, and collect admission/dialog/trace evidence for unresolved causes. Native rows remain owed for 46/48/54/58.
artifact: docs/investigations/2026-10-25-card-1514-triage-sweep-groups-39-58.md
