# CARD-1314 test duration buckets

Linux per-test durations for the frozen Unit roster are measured. Windows per-test durations are absent from this runner. The allowlist should stay a class-level Slow registry; measured buckets belong on the CARD-1313 timing record.

No tests were run. No product or test source was edited.

## Sources

Primary Linux aggregate, read here:

- TRX `/work/evidence/task-45e84b80/roster/run.trx`, SHA-256 `601898784f2d9fb394ce5cefabd1710b5b968366e3f9182233b07cf1e5deb5ce`, matching `roster/audit.json`.
- Run recorded in `docs/investigations/2026-10-16-rc2-final-linux-roster.md` on `origin/feat/card-task-45e84b80` at `fedcce723e78262f2dbfbff86fed01848e90943d`.
- Candidate `b7265a05aa8773f08dbf457b93c7561e7874822d`. Assembly informational version `1.0.0+82606e9440df27ad1d2bdb8810ed7f17b01b0f49`.
- `roster/timing.json` outer wall 2181.108s, exit 0. TRX `Times` start `2026-10-16T10:19:10.342448Z`, finish `2026-10-16T10:55:25.512686Z`, wall 2175.170s. Slot wait 0. `TUNIT_MAX_PARALLEL_TESTS` unset.
- Counters: 4,527 passed, 0 failed, 0 skipped, 373 classes. Every `UnitTestResult.duration` was parsed. Every definition resolved to `Antiphon.Tests.dll`. Zero durations landed on a bucket edge.

Shape check, same frozen Linux roster at the earlier qualification, hashes matching `docs/investigations/2026-10-15-card-1287-linux-unit.md`:

- `/work/evidence/task-67a7e42a/raw/final1/run.trx` SHA-256 `8ba9fb28bbc832502d10b2033849e5f89f6c3da793f1ab0bb4eb938d3d419d75` (4,516 cases).
- `/work/evidence/task-67a7e42a/raw/final2/run.trx` SHA-256 `8af7763a3494b29610960aa5ae5a64221b1aaf4563edecb85a87d31f1f4a1b85` (4,516 cases).

Windows roster narrative is `docs/investigations/2026-10-16-rc2-final-windows-roster.md` on `origin/feat/card-task-ec89e544` at `23d9cfccdd7fa9900b11222c633858e5f3f7cbb2`. It publishes aggregate walls only. The per-test files it names are on the Windows host:

- `C:\src\Antiphon\.antiphon\investigations\rc2-ec89e544\gate1\run.trx` SHA-256 `87b7e776e29ebce46e74d1ca2d841c1137eab59f107bf5daa0eac75bdef6a1eb`
- `C:\src\Antiphon\.antiphon\investigations\rc2-ec89e544\gate2\run.trx` SHA-256 `e67da70f7e36b18f9a8ca558d9c35a18a8b7173a05be987eaa97a7946388c59e`
- Adjacent `cases.json` is named in that same evidence root.

The earlier Windows qualification TRX files named in `docs/investigations/2026-10-15-card-1287-windows-unit.md` are also on that host: `C:\src\Antiphon\.antiphon\investigations\card-1287-b7430380\gate1\run.trx` (`c7a1cdd3cbebd0df7ebb280ac03faadfc470e326199440665d1509abb6ec2c67`) and `gate2\run.trx` (`864499a81a8cdf77a9b91207b8d739564d8bbce0df4c012fe002a69f8733773e`). There is no `/work/evidence/task-ec89e544`. This runner cannot read `C:\`. Those four TRX files, and their `cases.json` siblings, are the missing per-test rows.

Published Windows aggregate walls, from those reports, are TRX 226.415s and 209.167s (RC2) and 228.102s and 215.023s (CARD-1287 finals). They are start-to-finish walls, not bucket sums.

Roster membership was taken from `docs/investigations/2026-10-15-card-1287-linux-unit-roster.txt` (373 names) and `docs/investigations/2026-10-15-card-1287-windows-unit-roster.txt` (361 names). Intersection: 343 classes on both, 30 Linux-only, 18 Windows-only.

## How the buckets were cut

Each case uses the TRX `duration` attribute, converted to 100-nanosecond ticks. Edges are exact:

| Bucket | Rule |
|---|---|
| under 100 ms | `< 00:00:00.1000000` |
| 100 ms to under 1 s | `>= 100 ms` and `< 1 s` |
| 1 s to under 10 s | `>= 1 s` and `< 10 s` |
| 10 s to under 1 min | `>= 10 s` and `< 60 s` |
| 1 min and longer | `>= 60 s` |

Summed duration adds those attributes. It is test time, and cases overlap. Occupied wall is the merged `startTime`/`endTime` interval of the cases in that bucket, truncated to milliseconds. Bucket occupations overlap each other, so the occupied-wall column is not a partition of the 2175s TRX wall. The result span from the earliest case start to the latest case end is 2123.5s. Peak concurrency on that span is 79. Summed time 2970.2s over that span is about 1.4 cases in flight on average: the peak is a burst of short cases. The ten cases at 1 minute or longer occupy 942.9s of wall against 1062.0s of sum.

## Linux buckets

Project: all 4,527 cases are `Antiphon.Tests`. No SessionRunner, Pty, messaging, E2E, or client project is in this TRX.

| Bucket | Cases | Share of cases | Summed duration | Share of summed time | Occupied wall |
|---|---:|---:|---:|---:|---:|
| under 100 ms | 3715 | 82.1% | 58.1s | 2.0% | 11.1s |
| 100 ms to under 1 s | 563 | 12.4% | 163.3s | 5.5% | 43.0s |
| 1 s to under 10 s | 180 | 4.0% | 608.4s | 20.5% | 386.6s |
| 10 s to under 1 min | 59 | 1.3% | 1078.3s | 36.3% | 998.9s |
| 1 min and longer | 10 | 0.2% | 1062.0s | 35.8% | 942.9s |

The 3,715 cases under 100 ms are 82.1% of cases, 2.0% of summed time, and 11.1s of occupied wall. The 69 cases at 10s or longer are 1.5% of cases and 72.1% of summed time (2140.4s).

### Where the time sits

| Namespace | Classes | Cases | Summed duration | Share |
|---|---:|---:|---:|---:|
| `Antiphon.Tests.Scripts` | 11 | 126 | 1493.2s | 50.3% |
| `Antiphon.Tests.Checkpoints` | 40 | 313 | 971.5s | 32.7% |
| `Antiphon.Tests.Application` | 213 | 2767 | 231.9s | 7.8% |
| `Antiphon.Tests.Agents` | 36 | 324 | 123.1s | 4.1% |
| `Antiphon.Tests.TestHelpers` | 11 | 185 | 72.4s | 2.4% |
| `Antiphon.Tests.Infrastructure` | 39 | 547 | 50.7s | 1.7% |
| `Antiphon.Tests.Infrastructure.Resilience` | 6 | 46 | 11.1s | 0.4% |
| `Antiphon.TestSupport` | 1 | 1 | 7.0s | 0.2% |

`Antiphon.Tests.Scripts` is 11 classes and 126 cases, half of summed time. `Antiphon.Tests.Checkpoints` is 40 classes and 313 cases, another third. `Antiphon.Tests.Application` is 213 classes and 2,767 cases, 7.8% of summed time. `Antiphon.Tests.AgentTui` is 4 classes, 78 cases, 5.0s.

The 30 classes present only on the Linux roster are 368 cases (8.1%) and 2407.9s of summed time (81.1%). The 343 classes on both rosters are 4,159 cases and 562.2s (18.9%).

| Class | Cases | Sum | Max | Roster |
|---|---:|---:|---:|---|
| `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` | 21 | 724.5s | 108.0s | Linux roster only |
| `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` | 39 | 473.5s | 129.1s | Linux roster only |
| `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests` | 6 | 296.1s | 176.8s | Linux roster only |
| `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` | 13 | 235.9s | 36.2s | Linux roster only |
| `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` | 17 | 223.9s | 25.0s | Linux roster only |
| `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` | 13 | 187.6s | 66.1s | Linux roster only |
| `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` | 16 | 72.0s | 17.5s | Linux roster only |
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` | 25 | 69.3s | 13.4s | both rosters |
| `Antiphon.Tests.Scripts.RollingProductionMountTests` | 12 | 63.1s | 11.1s | Linux roster only |
| `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` | 33 | 60.8s | 2.0s | both rosters |
| `Antiphon.Tests.Checkpoints.PlanCoverageCommandTests` | 10 | 42.6s | 11.5s | Linux roster only |
| `Antiphon.Tests.Infrastructure.CodexRootExecContractTests` | 4 | 35.5s | 35.4s | Linux roster only |
| `Antiphon.Tests.Application.ChannelOutboundStorageTests` | 33 | 31.3s | 20.7s | both rosters |
| `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` | 10 | 26.8s | 7.7s | both rosters |
| `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` | 2 | 26.1s | 16.5s | both rosters |
| `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` | 4 | 25.8s | 14.6s | both rosters |
| `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` | 24 | 23.3s | 10.1s | Linux roster only |
| `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` | 46 | 17.5s | 0.6s | both rosters |
| `Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests` | 3 | 12.9s | 12.9s | both rosters |
| `Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests` | 12 | 12.3s | 2.5s | both rosters |

Mechanism of the leaders, read at this checkout. The RC2 roster report says the test inputs matched candidate `b7265a05` aside from that report. These line citations were not re-diffed against `82606e94`.

- `RetiredTempContainerHostTests` is `[Category("Unit")]`. Methods carry `[ParallelLimiter<ProcessSpawnLimit>]`. They build a `C1008HostFixture` docker trace and call `f.Run("retire-temp-containers", ...)` (`tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs:10-36`).
- `HostJqPrerequisiteScriptTests` is `[Category("Unit")]` with a class `[Timeout(600_000)]`. Methods carry `ProcessSpawnLimit`. The fixture starts `/usr/bin/bash` through `ProcessStartInfo` (`tests/Antiphon.Tests/Scripts/HostJqPrerequisiteScriptTests.cs:11-17`, `:1552-1554`). The class states it requires native Linux.
- `PlanCoverageBindingTests.historical_plans_bind_without_suppression` runs `CoverageCommand` twice on each of two real plan files and then parses the matched sources with Roslyn (`tests/Antiphon.Tests/Checkpoints/PlanCoverageBindingTests.cs:164-203`). That one case is 176.753s in the TRX (`duration="00:02:56.7525363"`). The sibling checklist bind has `[Timeout(240_000)]` at line 208. The class is Unit. Four of its six cases are under 100 ms; two cases are the entire 296.1s.
- `CheckpointTrackedBaselineTests` is Unit, `[ParallelLimiter<ProcessSpawnLimit>]`, `[Timeout(300_000)]`, and the file comment says real builds must detect changes (`tests/Antiphon.Tests/Checkpoints/CheckpointTrackedBaselineTests.cs:9-18`). Methods call `RequireLinux()`.
- `CheckpointShapeConflictTests` is the same real-build limiter pattern (`tests/Antiphon.Tests/Checkpoints/CheckpointShapeConflictTests.cs:10-14`).
- `RetiredTempContainerScriptTests` is Unit with `ProcessSpawnLimit` and loads real `scripts/c590-real.ps1` and `scripts/deploy-server2.ps1` through the PowerShell parser (`tests/Antiphon.Tests/Scripts/RetiredTempContainerScriptTests.cs:8-22`).
- `RollingProductionMountTests` states that one Bash child per method invokes the real production function (`tests/Antiphon.Tests/Scripts/RollingProductionMountTests.cs:10-20`).

Leading classes inside each bucket:

- under 100 ms: `TaskCompletionContinuationTests` 47 cases, 1.5s, 3% of the bucket sum; `OutputDistillationGateTests` 82 cases, 1.4s, 2% of the bucket sum; `LandReceiptScanCacheTests` 100 cases, 1.2s, 2% of the bucket sum.
- 100 ms to under 1 s: `WorktreeIgnoredContentClassifierTests` 46 cases, 17.5s, 11% of the bucket sum; `TestClassificationPolicyTests` 13 cases, 9.8s, 6% of the bucket sum; `HostJqPrerequisiteScriptTests` 11 cases, 8.5s, 5% of the bucket sum.
- 1 s to under 10 s: `HostJqPrerequisiteScriptTests` 20 cases, 86.2s, 14% of the bucket sum; `WorktreeCleanupPresentationTests` 30 cases, 59.1s, 10% of the bucket sum; `CheckpointConfinementLedgerTests` 14 cases, 54.6s, 9% of the bucket sum.
- 10 s to under 1 min: `RetiredTempContainerHostTests` 11 cases, 300.5s, 28% of the bucket sum; `CheckpointTrackedBaselineTests` 11 cases, 226.5s, 21% of the bucket sum; `CheckpointShapeConflictTests` 12 cases, 196.5s, 18% of the bucket sum.
- 1 min and longer: `RetiredTempContainerHostTests` 4 cases, 382.4s, 36% of the bucket sum; `HostJqPrerequisiteScriptTests` 3 cases, 317.7s, 30% of the bucket sum; `PlanCoverageBindingTests` 2 cases, 295.9s, 28% of the bucket sum.

The time leaders are those script and checkpoint classes. `Antiphon.Tests.AgentTui` sums to 5.0s. Allowlist reasons that name PostgreSQL, real Git, or a pty integration belong to classes outside this 373-class selection.

### Same roster on 15 October

| Run | Cases | <100 ms | <1 s | <10 s | <1 min | longer | Summed | Result span |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| primary | 4527 | 3715 | 563 | 180 | 59 | 10 | 2970.2s | 2123.5s |
| final1 | 4516 | 3672 | 576 | 199 | 58 | 11 | 3529.7s | 2475.7s |
| final2 | 4516 | 3752 | 508 | 183 | 62 | 11 | 3449.5s | 2382.1s |

`primary` is the 16 October RC2 TRX. The 15 October pair is the same 373-class roster with 4,516 cases; the RC2 report accounts for the extra 11 cases. Sixty case identities were at least 10s in all three runs. On those 60, the RC2 duration averaged 0.86 times the mean of the two October 15 durations. The bucket shape is the same run to run: about 3,700 cases under 100 ms, and 10 or 11 cases at a minute or longer. Host load moves the tail by tens of percent. A single run is the wrong thing to freeze as a budget.

### Cases at 5 seconds and the allowlist

`scripts/test-duration-tripwire.ps1:115` flags a TRX row at 5 seconds or more whose class is absent from `tests/Antiphon.Tests/slow-tests-allowlist.txt`. This TRX has 112 such rows, in 21 classes. All 112 are off the allowlist.

The allowlist at the compiled SHA `82606e9440df27ad1d2bdb8810ed7f17b01b0f49` has 148 class rows. None of those names are in the 373-class roster. The current checkout has since dropped `SessionRuntimeActionDeferralTests`; that class is also outside the roster. The 21 classes that actually produced the 5-second rows were absent from the compiled allowlist, including every class in the top-20 table above.

The registry is class-scoped. `TestClassificationMetadata` records `unmarked-registered` when a listed class is not `Slow`, `method-level-slow` when `Slow` is on a method, and `unregistered-marked` when a `Slow` class is missing (`tests/Shared/TestClassificationMetadata.cs:157-173`). A duration bucket is a per-case, per-host, per-SHA measurement. Writing it into the allowlist either marks these Unit classes `Slow`, which changes lane membership, or adds a non-Slow row, which fails the guard. The file should keep its current job: fully-qualified `Slow` class names with an adjacent reason.

### Classes on both rosters

These are the largest summed classes that the Windows roster also contains. Their Windows durations are the gap.

| Class | Cases | Sum | Max |
|---|---:|---:|---:|
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` | 25 | 69.3s | 13.4s |
| `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` | 33 | 60.8s | 2.0s |
| `Antiphon.Tests.Application.ChannelOutboundStorageTests` | 33 | 31.3s | 20.7s |
| `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` | 10 | 26.8s | 7.7s |
| `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` | 2 | 26.1s | 16.5s |
| `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` | 4 | 25.8s | 14.6s |
| `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` | 46 | 17.5s | 0.6s |
| `Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests` | 3 | 12.9s | 12.9s |

### Slowest cases

Every case below passed.

### Slowest 50 of 3715 cases in under 100 ms

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 0.0999s | `changed_inventory_is_refused(link)` | `Antiphon.Tests.Checkpoints.CheckpointOwnedOutputTests` |
| 2 | 0.0996s | `follow_marker_returns_checkout_when_the_marker_resolves` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 3 | 0.0995s | `C1121_OnlyWhitelistedEvidenceReusesNegativeScan(positive-conflict)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 4 | 0.0994s | `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(automatic, False)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 5 | 0.0992s | `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(schema, False)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 6 | 0.0992s | `Captured_ready_size_is_qualified(80, 24, Ready, 9880df906bfb5a68b503efa004787aeced48c42adadae2a822544f3634c8106a)` | `Antiphon.Tests.Agents.GrokStartupSizeTests` |
| 7 | 0.0990s | `C1121_CacheLifetimeAndCapacityFailClosed(invalid-elapsed-refuses)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 8 | 0.0989s | `FromRows_sums_stints_ignores_the_previous_stint_and_runs_the_open_one_to_now` | `Antiphon.Tests.Application.DispatchHoldLedgerTests` |
| 9 | 0.0988s | `C1151_Whitelist_requires_positive_evidence(identity-changed)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 10 | 0.0987s | `Captured_ready_size_is_qualified(200, 60, Ready, 91e86f128ea4c56f61169f4b953c9683f924658f99dbf93c9f50bffd11e5dca7)` | `Antiphon.Tests.Agents.GrokStartupSizeTests` |
| 11 | 0.0986s | `Captured_ready_size_is_qualified(100, 30, Ready, 5f3383c28a383a8a8082cbe32cc07103a5493b9f6c4304f704d15007eaff038f)` | `Antiphon.Tests.Agents.GrokStartupSizeTests` |
| 12 | 0.0986s | `Captured_ready_size_is_qualified(160, 50, Ready, 9d7e0dcdbd5de66b652424a12054324d741d172f8f1dd8b4bd5717e012eadd5a)` | `Antiphon.Tests.Agents.GrokStartupSizeTests` |
| 13 | 0.0986s | `Captured_ready_size_is_qualified(120, 30, Ready, d7b6fa93c8506ed054f6dc285a891749a94572ba31f81c34665e6f386a32882d)` | `Antiphon.Tests.Agents.GrokStartupSizeTests` |
| 14 | 0.0986s | `Entrypoint_heal_chowns_the_tmp_directory_once` | `Antiphon.Tests.Infrastructure.CodexRootExecContractTests` |
| 15 | 0.0984s | `Captured_ready_size_is_qualified(40, 20, Unknown, 63511d86d3a4b6c9551112eaeef518d4dd6b23658fce8c96b5923014af1c2d3d)` | `Antiphon.Tests.Agents.GrokStartupSizeTests` |
| 16 | 0.0984s | `C1121_OnlyWhitelistedEvidenceReusesNegativeScan(positive-outcome-legacy)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 17 | 0.0983s | `C1151_Whitelist_requires_positive_evidence(brief-state-unknown)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 18 | 0.0982s | `C1121_OnlyWhitelistedEvidenceReusesNegativeScan(positive-delivery-failure)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 19 | 0.0982s | `C1151_Whitelist_requires_positive_evidence(commit-recovery-pending)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 20 | 0.0981s | `wait_leaves_the_tool_copy_while_the_finished_executor_is_still_alive` | `Antiphon.Tests.Checkpoints.WaitCommandTests` |
| 21 | 0.0976s | `Index_orders_a_later_placed_card_ahead_of_an_older_same_rank_card` | `Antiphon.Tests.Application.CardTaskFileRendererTests` |
| 22 | 0.0970s | `C1121_OnlyWhitelistedEvidenceReusesNegativeScan(positive-dispatch-base)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 23 | 0.0970s | `C1151_Whitelist_requires_positive_evidence(api-recovery-unresolved)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 24 | 0.0968s | `C1151_Whitelist_requires_positive_evidence(api-recovery-unknown)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 25 | 0.0967s | `Status_and_specialist_populations_match_contract` | `Antiphon.Tests.Application.DispatchConcurrencyPolicyTests` |
| 26 | 0.0966s | `Git_checkout_with_no_instruction_files_is_Unconfigured` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 27 | 0.0962s | `C1151_Whitelist_requires_positive_evidence(session-row-missing)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 28 | 0.0960s | `Labels_and_comments_downgrade_before_state_transitions_lose_detail` | `Antiphon.Tests.Application.TrackerSyncSummaryFormatterTests` |
| 29 | 0.0959s | `an_oversized_composition_throws_and_names_what_to_shrink` | `Antiphon.Tests.Application.InstructionBundleTests` |
| 30 | 0.0958s | `C711_TargetRaceRefusalPolicy(no-approval, True, False, False)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 31 | 0.0957s | `C1151_Whitelist_requires_positive_evidence(session-terminal)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 32 | 0.0957s | `C711_TargetRaceRefusalPolicy(no-lease, True, False, False)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 33 | 0.0956s | `C1121_OnlyWhitelistedEvidenceReusesNegativeScan(positive-destination-failed)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 34 | 0.0953s | `C1151_Whitelist_requires_positive_evidence(model-reply-present)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 35 | 0.0953s | `C711_TargetRaceRefusalPolicy(reason:push_rejected, False, False, True)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 36 | 0.0951s | `C1151_Whitelist_requires_positive_evidence(prompt-only-queued)` | `Antiphon.Tests.Application.BootStallPolicyTests` |
| 37 | 0.0951s | `C1121_OnlyWhitelistedEvidenceReusesNegativeScan(positive-verdict-no-submit-output)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 38 | 0.0948s | `Different_senders_same_conversation_do_not_merge` | `Antiphon.Tests.Application.ChannelInboundDebouncerTests` |
| 39 | 0.0946s | `Push_reports_the_head_it_pushed` | `Antiphon.Tests.Application.RemoteWorktreeMirrorTests` |
| 40 | 0.0946s | `Plain_echo_takes_precedence_over_an_earlier_cursor_separated_match` | `Antiphon.Tests.Agents.CodexResponseAnalyzerTests` |
| 41 | 0.0945s | `C1121_OnlyWhitelistedEvidenceReusesNegativeScan(positive-generation-null-bound)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 42 | 0.0945s | `Same_sender_messages_within_window_merge_into_one_flush` | `Antiphon.Tests.Application.ChannelInboundDebouncerTests` |
| 43 | 0.0943s | `Push_failure_reports_and_does_not_invent_a_sha` | `Antiphon.Tests.Application.RemoteWorktreeMirrorTests` |
| 44 | 0.0939s | `json_round_trips_camel_case_and_enum_names` | `Antiphon.Tests.Application.InstructionFileStampTests` |
| 45 | 0.0936s | `Git_checkout_with_instruction_files_is_CheckoutAsCwd` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 46 | 0.0932s | `C1121_CacheLifetimeAndCapacityFailClosed(concurrent-publications-keep-own-binding)` | `Antiphon.Tests.Application.LandReceiptScanCacheTests` |
| 47 | 0.0929s | `C613_DivergentOwnTipIsProgress(probe-unavailable)` | `Antiphon.Tests.Application.TaskCompletionContinuationTests` |
| 48 | 0.0928s | `Standard_exes_project_with_mirror_and_home` | `Antiphon.Tests.Application.CodexPhoneHomeProjectionTests` |
| 49 | 0.0927s | `Different_conversations_never_merge` | `Antiphon.Tests.Application.ChannelInboundDebouncerTests` |
| 50 | 0.0922s | `C613_OffBranchFilesRemainProgress(metadata-unavailable)` | `Antiphon.Tests.Application.TaskCompletionContinuationTests` |


### Slowest 50 of 563 cases in 100 ms to under 1 s

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 0.9951s | `Exact_secret_redaction_handles_repetitive_adversarial_input_within_linear_budget` | `Antiphon.Tests.AgentTui.RunnerProcessProbeRedactionTests` |
| 2 | 0.9938s | `Ephemeral_ports_differ_across_instances` | `Antiphon.Tests.Agents.FakeLlmApiSelfTests` |
| 3 | 0.9740s | `C1058_Final_link_checks_are_independent` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 4 | 0.9603s | `Composer_change_or_blocker_restarts_settle` | `Antiphon.Tests.Agents.GrokStartupReadinessTests` |
| 5 | 0.9602s | `Settle_requires_two_observations_and_elapsed_time` | `Antiphon.Tests.Agents.GrokStartupReadinessTests` |
| 6 | 0.9601s | `C487_G073(full)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 7 | 0.9496s | `C487_G073(simple)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 8 | 0.9455s | `Smoke_script_is_valid_PowerShell` | `Antiphon.Tests.AgentTui.AgentTuiSmokeScriptTests` |
| 9 | 0.9405s | `executor_log_records_each_failed_owner_read_without_stopping_work` | `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` |
| 10 | 0.9336s | `C487_G074` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 11 | 0.9192s | `C1058_Receipt_formatter_uses_open_descriptor` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 12 | 0.9115s | `C1058_Final_permission_checks_are_independent` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 13 | 0.9027s | `C1025_Provision_serializes_and_rechecks` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 14 | 0.8956s | `each_row_and_rerun_rechecks_owner_before_launch` | `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` |
| 15 | 0.8947s | `C994_Wrapper_retirement_proof_is_typed` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 16 | 0.8924s | `Workflow_calls_real_guard_and_propagates_failure` | `Antiphon.Tests.Infrastructure.EvidencePolicyWorkflowTests` |
| 17 | 0.8843s | `C1213_DocsCiteGitCommonAndBothBranchForms` | `Antiphon.Tests.Application.RunnerBranchContractDocumentationTests` |
| 18 | 0.8676s | `C487_G068` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 19 | 0.8637s | `Quiet_fallback_does_not_complete_an_empty_snapshot` | `Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests` |
| 20 | 0.8554s | `C1155_ConditionalDirectiveFailsClosed` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 21 | 0.8512s | `C487_G076(empty)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 22 | 0.8419s | `C1058_Digest_is_bound_to_open_descriptor` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 23 | 0.8399s | `C487_G076(malformed)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 24 | 0.8321s | `C487_G076(missing)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 25 | 0.8310s | `C1155_BlockCommentMethodIsNotCounted` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 26 | 0.8303s | `C1155_RemovedAndRenamedMethodDiffersFromTheRoster` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 27 | 0.8302s | `C1155_SourceWithoutConditionalDirectiveIsDiscovered` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 28 | 0.8300s | `C1155_ExtraAttributeAfterTestIsDiscovered` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 29 | 0.8299s | `C1155_CharLiteralQuoteDoesNotHideTheNextTest` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 30 | 0.8298s | `C1155_VerbatimEscapedQuoteDoesNotHideTheNextTest` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 31 | 0.8298s | `C1155_CommentedOutDeclarationIsNotCounted` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 32 | 0.8297s | `C1155_InterpolatedStringDoesNotHideTheNextTest` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 33 | 0.8297s | `C1155_RawStringDoesNotHideTheNextTest` | `Antiphon.Tests.Application.DispatchBaseWarningRosterTests` |
| 34 | 0.8268s | `C913_Net9_probe_rejects_warm_caches_and_missing_packs` | `Antiphon.Tests.Infrastructure.CodexRunnerImageContractTests` |
| 35 | 0.8181s | `C1058_Version_probe_uses_open_descriptor` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 36 | 0.7987s | `C1054_Jq_row_records_found_and_resolved_paths` | `Antiphon.Tests.Infrastructure.JqRunnerImageContractTests` |
| 37 | 0.7934s | `Repeated_trust_frames_receive_only_one_enter` | `Antiphon.Tests.Agents.RunnerCodexAdapterReadyTests` |
| 38 | 0.7704s | `C1006_Fixture_bytes_and_provenance_are_pinned` | `Antiphon.Tests.Agents.GrokLinuxBlockingPromptTests` |
| 39 | 0.7703s | `terminal_publication_waits_for_driver_exit_and_lease_disposal` | `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` |
| 40 | 0.7694s | `C443_D9_DiagnosticsSurviveEitherEnvelopeExtreme(False, 0, 0, True, True)` | `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` |
| 41 | 0.7331s | `Save_metadata_cannot_cross_contexts` | `Antiphon.Tests.Infrastructure.DockerDeliveryBarrierTests` |
| 42 | 0.7165s | `C1058_Staged_unlink_failure_refuses` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 43 | 0.6960s | `C1025_Provision_admits_host_prerequisites` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 44 | 0.6920s | `C1011_startup_accepts_observed_trust_or_absence(True)` | `Antiphon.Tests.Agents.GrokFreshWorktreeQualificationTests` |
| 45 | 0.6825s | `Concurrent_first_use_readers_all_get_the_one_token_and_none_throw` | `Antiphon.Tests.Infrastructure.OperatorTokenFileTests` |
| 46 | 0.6628s | `Captured_idle_frames_stay_ready_while_spinner_redraws` | `Antiphon.Tests.Agents.GrokStartupReadinessTests` |
| 47 | 0.6393s | `C1058_False_probe_uses_open_descriptor` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 48 | 0.6274s | `C1058_Provision_removes_only_owned_stage_link` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 49 | 0.6252s | `each_build_rechecks_owner_immediately_before_launch` | `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` |
| 50 | 0.6226s | `Unknown_or_blocked_current_frames_never_become_ready` | `Antiphon.Tests.Agents.GrokStartupReadinessTests` |


### Slowest 50 of 180 cases in 1 s to under 10 s

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 9.929s | `C994_Resume_never_runs_container_cleanup` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 2 | 9.911s | `C487_G069(ordinary)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 3 | 9.727s | `C994_Cleanup_receipts_redact_unapproved_fields` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 4 | 9.687s | `C1025_Explicit_tool_phases_never_deploy` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 5 | 9.681s | `C994_Wrapper_task_and_land_census_is_complete` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 6 | 9.638s | `Spawning_classes_carry_a_class_timeout` | `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` |
| 7 | 9.536s | `C1105_Previous_generation_roster_is_derived_from_its_compose` | `Antiphon.Tests.Scripts.RollingProductionMountTests` |
| 8 | 9.281s | `c983_driver_aliases_bind_on_the_exit_assertion` | `Antiphon.Tests.Checkpoints.PlanCoveragePcTests` |
| 9 | 8.739s | `C994_Whole_candidate_set_precedes_removal` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 10 | 8.614s | `C487_G065` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 11 | 8.583s | `C994_Host_context_is_strict` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 12 | 8.499s | `C1025_Transport_is_bounded_and_reaped` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 13 | 8.367s | `C487_G066` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 14 | 8.330s | `c835_eight_pc_risks_are_visible` | `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` |
| 15 | 8.230s | `C994_Host_main_admission_is_required` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 16 | 8.031s | `C994_Drain_uses_one_deadline` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 17 | 8.019s | `C994_Drain_waits_for_exit_and_removes_containers` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 18 | 7.723s | `C1025_Provision_cleans_only_owned_staging` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 19 | 7.705s | `Disabling_the_probe_does_not_disable_effort_resolution(True)` | `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` |
| 20 | 7.467s | `C487_G067(inherits)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 21 | 7.440s | `shape_conflict_rule(existing-stamp)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 22 | 7.375s | `C487_G067(no-inherits)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 23 | 7.327s | `C1025_Final_recycle_guard_remains` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 24 | 7.069s | `C994_Secrets_match_read_only_files` | `Antiphon.Tests.Scripts.RollingProductionMountTests` |
| 25 | 6.970s | `Model_has_the_evidence_and_state_columns_and_the_migration_adds_only_them` | `Antiphon.Tests.Application.HerdrSupervisionFailureEvidenceTests` |
| 26 | 6.957s | `Registry_matches_compiled_metadata` | `Antiphon.TestSupport.TestClassificationGuardTests` |
| 27 | 6.954s | `shape_conflict_rule(existing-output-symlink)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 28 | 6.924s | `shape_conflict_rule(compiler-collision)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 29 | 6.321s | `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` |
| 30 | 6.107s | `shape_conflict_rule(existing-output-directory)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 31 | 6.101s | `C994_Host_retirement_proof_is_typed` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 32 | 5.913s | `entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` |
| 33 | 5.762s | `C1025_Provision_requires_missing_jq` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 34 | 5.723s | `C1025_Provision_requalifies_published_jq` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 35 | 5.693s | `C1025_Check_has_no_install_effects` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 36 | 5.665s | `no_Check_comparison_survives_outside_the_allowlist` | `Antiphon.Tests.Application.SpecialistRoleContractTests` |
| 37 | 5.573s | `C1025_Check_qualifies_deployment_shell` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 38 | 5.543s | `C1025_Receipt_rejects_canonical_lookup_with_unapproved_target` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 39 | 5.420s | `C994_Compose_model_preserves_service_specific_mounts` | `Antiphon.Tests.Scripts.RollingProductionMountTests` |
| 40 | 5.215s | `stat_without_a_change_time_hashes_each_entry_at_admission` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` |
| 41 | 5.144s | `rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` |
| 42 | 5.140s | `C1058_Open_descriptor_must_match_canonical_leaf` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 43 | 5.058s | `Source_zip_expansion_accepts_exact_budget_and_refuses_one_extra_byte` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 44 | 4.902s | `A_trust_dialog_that_will_not_clear_fails_the_launch_with_a_named_block` | `Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests` |
| 45 | 4.863s | `dirty_tracked_tree_is_refused_and_unmodified` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 46 | 4.667s | `ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` |
| 47 | 4.620s | `tracked_snapshot_contains_committed_files` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 48 | 4.447s | `shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` |
| 49 | 4.244s | `Inline_and_zip_sources_share_the_expanded_budget` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 50 | 4.116s | `C458_EfMappingsStoreNullableTextWithStableWorkspaceOrdinals` | `Antiphon.Tests.Application.WorkerWorkspaceDefaultStorageTests` |


### Slowest 50 of 59 cases in 10 s to under 1 min

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 54.30s | `C994_Exited_owned_containers_are_removed_without_volumes` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 2 | 36.75s | `C994_Production_mount_topology_is_proven` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 3 | 36.17s | `imported_props_reference_tampering_is_reported` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 4 | 35.38s | `Provider_exec_into_the_runner_names_uid_1654` | `Antiphon.Tests.Infrastructure.CodexRootExecContractTests` |
| 5 | 32.98s | `C994_Final_absence_is_observed` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 6 | 32.25s | `C994_Host_task_and_land_census_is_complete` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 7 | 32.09s | `C994_Absent_cleanup_is_idempotent` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 8 | 31.31s | `C994_Census_and_inspect_fail_closed` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 9 | 31.20s | `transitive_reference_tampering_is_reported` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 10 | 24.97s | `shape_conflict_rule(clean-build)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 11 | 24.87s | `fresh_checkout_run_executes_a_test` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 12 | 24.37s | `C994_Retire_recovers_exited_and_absent_temp` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 13 | 21.39s | `C994_Host_preview_never_mutates` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 14 | 20.68s | `Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 15 | 20.04s | `C994_Receipt_write_and_copy_fail_closed` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 16 | 18.79s | `intermediate_output_path_tampering_is_reported` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 17 | 18.47s | `shape_conflict_rule(ignored-descendant)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 18 | 18.30s | `local_output_property_tampering_is_reported` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 19 | 18.28s | `generated_target_file_tampering_is_reported` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 20 | 17.45s | `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` | `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` |
| 21 | 17.42s | `clean_untracked_output_build_exits_zero` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 22 | 17.18s | `shape_conflict_rule(changed-untracked)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 23 | 16.99s | `C994_Preview_reports_pending_volume_proof` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 24 | 16.96s | `shape_conflict_rule(untracked-descendant)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 25 | 16.45s | `Spawn_timeouts_do_not_exceed_ten_minutes` | `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` |
| 26 | 16.32s | `index_added_file_is_kept_and_indexed` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 27 | 16.25s | `shape_conflict_rule(symlink-on-file)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 28 | 15.94s | `staged_ignored_file_is_kept_and_indexed` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 29 | 15.93s | `staged_untracked_file_is_kept_and_indexed` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 30 | 15.59s | `shape_conflict_rule(staged-untracked)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 31 | 15.37s | `shape_conflict_rule(deleted-tracked)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 32 | 15.21s | `shape_conflict_rule(regular-user-move)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 33 | 15.12s | `shape_conflict_rule(modified-tracked)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 34 | 14.79s | `C994_Rollout_lock_covers_final_census` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 35 | 14.58s | `c866_fixed_checks_clear_same_obligations` | `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` |
| 36 | 14.40s | `C1025_Receipt_persistence_is_required` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 37 | 14.26s | `shape_conflict_rule(deleted-untracked)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 38 | 14.04s | `C994_Cleanup_receipt_is_required_before_recycle` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 39 | 13.56s | `shape_conflict_rule(directory-on-file)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 40 | 13.56s | `shape_conflict_rule(file-on-directory)` | `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` |
| 41 | 13.39s | `C487_G069(optin)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 42 | 13.28s | `C1058_Changed_leaf_cannot_emit_success_proof` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 43 | 13.25s | `passing_script_requires_writable_source_receipt` | `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` |
| 44 | 12.88s | `Heredoc_fed_docker_exec_keeps_stdin_open` | `Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests` |
| 45 | 12.54s | `C994_Bridge_transport_preserves_literal_context` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |
| 46 | 12.39s | `C994_Image_receipt_lookup_is_bound` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 47 | 12.22s | `C1105_Temp_cleanup_uses_previous_generation` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 48 | 11.64s | `C1058_Metadata_syntax_refuses` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 49 | 11.54s | `frozen_checklist_preserves_all_72_obligations` | `Antiphon.Tests.Checkpoints.PlanCoverageCommandTests` |
| 50 | 11.14s | `C994_Named_volume_identity_and_topology_are_exact` | `Antiphon.Tests.Scripts.RollingProductionMountTests` |


### All 10 cases at 1 min or longer

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 176.75s | `historical_plans_bind_without_suppression` | `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests` |
| 2 | 129.11s | `C1025_Preflight_precedes_every_phase` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 3 | 119.13s | `historical_row_timeout_checklist_binds_the_advisory_method` | `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests` |
| 4 | 108.03s | `C994_Each_removal_rechecks_live_proofs` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 5 | 102.10s | `C1025_Receipt_requires_complete_current_proof` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 6 | 97.42s | `C994_Audit_image_is_pinned_and_required` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 7 | 89.53s | `C994_Container_service_and_state_are_restricted` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 8 | 87.41s | `C994_Removal_failure_and_crash_retry_preserve_progress` | `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` |
| 9 | 86.48s | `C1025_Preflight_is_fresh_for_each_entry` | `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` |
| 10 | 66.08s | `C994_Verifier_manifest_is_strict` | `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` |


## Windows

No per-test bucket table. The missing rows are the four TRX paths listed under Sources, plus the `cases.json` files the Windows reports say sit beside them.

What is already known, from the published aggregate tables only:

| Run | Cases | Classes | TRX wall | Outer wall |
|---|---:|---:|---:|---:|
| RC2 gate1 | 4,537 | 361 | 226.415s | 336.086s, including 105s slot wait |
| RC2 gate2 | 4,537 | 361 | 209.167s | 212.726s |
| CARD-1287 gate1 | 4,526 | 361 | 228.102s | 233.641s |
| CARD-1287 gate2 | 4,526 | 361 | 215.023s | 218.501s |

That wall is much shorter than the Linux TRX wall. On Linux, 81.1% of summed time is in the 30 classes the Windows roster omits. The short Windows wall is consistent with that omission. It does not measure the 343 shared classes on Windows, and it does not fill any Windows bucket.

## What to do with the tail

Stated default, for the caller to accept:

1. Leave `tests/Antiphon.Tests/slow-tests-allowlist.txt` as the Slow-class registry. Do not add a bucket column or per-case rows.
2. Recover Windows buckets by copying the existing RC2 `gate1/run.trx` and `gate2/run.trx` (hashes above) if they are still at `C:\src\Antiphon\.antiphon\investigations\rc2-ec89e544`. That is a copy, not a new suite run.
3. If those files are gone, the smallest replacement is the CARD-1313 timing record, appended by the roster runner that already exists: host, SHA, roster identity, outer wall, TRX wall, executed/passed/failed/skipped, and for each class the case count, summed duration, and max duration, plus one line for every case at 10 seconds or longer (class, method, display name, duration). Those fields rebuild this bucket table. A second full TRX archive is larger than the decision needs.
4. Spend the Linux wall on the measured leaders: the Linux-only script and checkpoint classes (native process, Bash, PowerShell, and real `CoverageCommand` / real-build cases), which are Unit and off the allowlist. The 3,715 sub-100ms cases occupy 11.1s of wall at a peak concurrency of 79, so raising parallelism does not shorten this roster. Replacing the host, jq, and real-build proofs with fakes would change what those classes certify. CARD-1313's change-aware selection is the place that can keep them off an unrelated diff, using a fresh timing record rather than a static bucket in the allowlist.

## Not done, noted

No Slow retag, allowlist edit, or timing-record writer was added.

## Uncertainties

- Windows per-case durations are unread. The copy in item 2 resolves that.
- Occupied wall truncates 7-digit timestamps to milliseconds.
- `duration` is the TRX case duration, not slot-queue time. This Linux run waited 0s.
- The October 15 pair shows the same shape and a slower tail on this shared host. One RC2 pass is not a percentile.
- SessionRunner, Pty, messaging, E2E, and client suites are outside both frozen rosters.
- Class mechanisms were read at checkout `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`. The RC2 report says the test inputs matched the candidate; this investigation did not re-diff each cited file against `82606e94`.

--- next stage ---
next: decide
handoff: Accept the CARD-1314 default: keep slow-tests-allowlist.txt as the Slow registry; copy Windows RC2 gate1/gate2 TRX from C:\src\Antiphon\.antiphon\investigations\rc2-ec89e544, else append a CARD-1313 timing record (host, SHA, roster, wall, per-class sum/max, cases >=10s). Linux summed time is 81% in the 30 Linux-only script and checkpoint classes.
artifact: docs/investigations/2026-10-16-card-1314-test-duration-buckets.md
