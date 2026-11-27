# Server2 disk protection verification

The full task base is `561591c562f31dd4fe0fc8f940f2ea7e947ce965`. Repair Review
b33367ef examined `9f101466bef25907f887b035dc6edef8238f4990`. That tip also
preserves the evidence sweep and its tests, documentation and controls on
`feat/card-2095-evidence-sweep-wip` for CARD-2095. The original landing owner is
`abfd18ed-34d4-4318-bbc6-18ae42d464d4`.

## Verification design

| ID | Invariant | Witness |
|---|---|---|
| V-5 | Both rollout routes use the most recent valid fresh sample of their shared backing filesystem. Dispatch holds below the 20 GiB floor and recovers at the floor. | `ShippedRunnersHoldOneByteBelowTheAbsoluteFloor`, `SharedFilesystemHoldsBothRoutesAndRecoversFromEither` |
| V-6 | With no usable sample, configured routes admit and log bounded missing, stale or invalid diagnostics for that filesystem. | `MissingStaleAndInvalidSamplesEmitBoundedDiagnostics` |
| V-7 | The row, scheduler and baseline check again at lease grant. Refusal launches zero build or test drivers on that lease and releases it. | `RowRechecksSpaceAfterAdmissionAndReleasesTheLease`, `SchedulerRechecksSpaceAfterAdmissionAndReleasesTheLease`, `BaselineRechecksSpaceAfterGrantAndReleasesTheLease` |
| V-8 | The current checkpoint tool evaluates each effective project/property combination before running the build. The current build-slot launcher supplies its guard to historical SDK projects. | `HistoricalProjectEvidenceDestinationsAreRefusedBeforeRestore`, `ReferenceSpecificPropertiesAreCheckedForEveryOccurrenceOfTheSameProject`, R-5 |
| V-9 | A held dispatch resumes on the same recipient. The production reconciliation scan discovers persisted brief debt after a failed enqueue. | `LowDiskRecoveryDeliversTheCompleteQueuedBrief` verifies exactly one complete matching UserPrompt on the original session for eligible, busy and failed enqueue cases. |
| R-3 | The shell, Windows cmd and PowerShell exe check space before any CLI bootstrap. The raw SDK entry checks before restore. | POSIX launcher witness, `Checkpoint_entry_refuses_low_space_before_publishing_or_launching`, native SDK and Windows witnesses below. |
| R-4 | The timer starts the outer host Docker builder-cache prune hourly with `until=24h`. | Read the timer and exact service command. Host activation follows landing. |
| R-5 | A normal leased historical project refuses protected paths before NuGet writes and accepts a safe relative output. | The historical build command and positive witness below. |

V-1 through V-4 and R-1 through R-2 moved to CARD-2095. SourceLanding controls
are listed in the [unbound candidate](../mutations/2026-11-27-server2-disk-protection-candidate.md).

## Performance budget

The 14-row round-2 review ran 48m28s and finished one row. Earlier Code
measured 8m44s through build-ready and 1m14s of row execution for the first
build row. Combine related classes and build `Antiphon.Tests` once. The
isolated executor owns the SDK and row leases. Use one shared Postgres fixture
with isolated schemas under width four for database cases. The estimates
below are provisional pending timings from this grouped run. Record the
lease waits, build and row durations in the run report and per-test times in
the TRX. The isolated output avoids colliding with the worktree's native
incremental build. No assertion or per-test deadline changes.

The disk invariant affects the configured dispatch routes, checkpoint
launchers and build placement. The impact selector caps the shared
`Directory.Build.targets` edge and returns a large dispatcher dependency set.
The recovery class alone measured 3,505s for its full inventory. A full build
and run of every downstream test project would exceed the 30-minute Debug
threshold. The dispatcher row checks the exact production recovery path and
the affected notification and custody classes. The recovery statement
budget stays method-scoped. The checkpoint tool, launchers, policy guards
and dispatcher classes below are the bounded Final selection. The full Unit
lane belongs to nightly and rc.

CARD-2093 repairs the three inherited plan-selection rows at
`9c119f33bf753c04431c45da984d006c23f40212`. Merge carries that repair.

## Native and manual witnesses

Use the real POSIX entry through
`scripts/antiphon build-slot --label checkpoint-linux-bootstrap -- node --test scripts/test-checkpoint-disk-guard.mjs`.
The Linux raw SDK entry runs `dotnet run --project tools/Antiphon.Checkpoints`
through a lease from an owned caller directory on a mount with less than
20 GiB free. It must print `workspace_disk_low` before restore. The SDK
returns its normal build error (1), while the Antiphon launchers return 9.

On Windows run the real cold cmd and published PowerShell exe against a
caller volume with less than 20 GiB free, using
`scripts/test-checkpoint-bootstrap-win.ps1`. Run Windows-only
`Card1277PtyHostFixtureTests` and `GrokDelegateEndToEndTests` before the rc
pick, along with the native startup rehearsal.

R-5 invokes `<current-checkout>/scripts/antiphon build-slot --label historical-build -- dotnet build sample.csproj`
from a scratch checkout that predates the guard. Set an evaluated
`MSBuildProjectExtensionsPath` to a fresh path under an evidence root
and require `workspace_evidence_write` before any restore output.
Run a second leased build with the default native directories and an
evaluated safe relative intermediate override. Require a produced assembly
and NuGet assets under the scratch checkout.

R-4 reads both systemd units. Install and enable the timer on the outer
host during activation. The dispatcher settings and code take effect with the
next server restart. The checkpoint CLI refreshes from its checkout.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | final | tests/Antiphon.Tests -> bin-e258-final/ | disk and confinement | `/*/*/(WorkspaceDiskAdmissionTests*)\|(CheckpointDiskAdmissionTests*)\|(CheckpointEvidencePlacementTests*)\|(BaselineComparerTests*)\|(CliEntryTests*)\|(CheckpointOutputConfinementTests*)/*` | V-5–V-8, R-3 | WorkspaceDiskAdmissionTests,CheckpointDiskAdmissionTests,CheckpointEvidencePlacementTests,BaselineComparerTests,CliEntryTests,CheckpointOutputConfinementTests | 6 | 8 | true |
| CP-2 | final | CP-1 | verification guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)\|(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)\|(LiveLaneCensusTests*)\|(CheckpointManifestDocumentationTests*)\|(StableBuildOutputDocumentationTests*)\|(DockerStackDocumentationTests*)/*` | policy, mechanical, layering and source guards | TestClassificationPolicyTests,TestClassificationGuardTests,SlowTestRegistryMergeTests,SlowTestTripwireTests,NativeLinuxPolicyWitnessTests,MechanicalPcContractTests,TaskPlatformGuidanceTests,C1729LayeringGuardTests,TestLaneCategoryGuardTests,TestFixtureCleanupGuardTests,PlanCheckpointClassSelectionGuardTests,LiveLaneRunnerTests,NightlyCoreGoldenTests,LiveLaneCensusTests,CheckpointManifestDocumentationTests,StableBuildOutputDocumentationTests,DockerStackDocumentationTests | 17 | 3 | true |
| CP-3 | final | CP-1 | live gate inventory | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | live gate inventory | LiveLaneInventoryTests | 2 | 1 | true |
| CP-4 | final | CP-1 | dispatch recovery | `/*/*/(PhoneHomeTaskDispatchProjectionTests*)\|(PreDispatchFailureDeliveryTests*)\|(AgentTaskLandHeldNotificationTests*)\|(AgentTaskLandNotificationParkFollowUpTests*)\|(AgentTaskLandNotificationPersistenceTests*)\|(AgentTaskLandNotificationReconcileLoadTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandNotificationRescanTests*)\|(AgentTaskLandReceiptTests*)\|(AgentTaskLandReceiptWatermarkSafetyTests*)\|(AgentTaskLandReceiptWatermarkTests*)\|(ExpectationDebtTests*)\|(LandReceiptScanCacheTests*)\|(ReceiptScanDestinationIntegrationTests*)/*` | V-9 and selected notification and receipt regressions | PhoneHomeTaskDispatchProjectionTests,PreDispatchFailureDeliveryTests,AgentTaskLandHeldNotificationTests,AgentTaskLandNotificationParkFollowUpTests,AgentTaskLandNotificationPersistenceTests,AgentTaskLandNotificationReconcileLoadTests,AgentTaskLandNotificationRecoveryTests,AgentTaskLandNotificationRescanTests,AgentTaskLandReceiptTests,AgentTaskLandReceiptWatermarkSafetyTests,AgentTaskLandReceiptWatermarkTests,ExpectationDebtTests,LandReceiptScanCacheTests,ReceiptScanDestinationIntegrationTests | 14 | 6 | true |
| CP-5 | final | CP-1 | checkpoint and runner neighbors | `/*/*/(RowVerbTests*)\|(RunSchedulerTests*)\|(CheckpointLauncherTests*)\|(CheckpointToolCommandTests*)\|(C1677DiskAlarmTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointTerminalReceiptTests*)\|(HostCleanupCheckpointReceiptTests*)\|(ProcessSpawnLimitTests*)\|(PhoneHomeRollingRunnerTests*)\|(RefOnlyProductionCompositionTests*)\|(DelegationTestServicesTests*)\|(DelegationHarnessCensusTests*)\|(Card1277PtyHostFixtureTests*)/*` | checkpoint and dispatch integration regression | RowVerbTests,RunSchedulerTests,CheckpointLauncherTests,CheckpointToolCommandTests,C1677DiskAlarmTests,CheckpointReceiptSafetyTests,CheckpointTerminalReceiptTests,HostCleanupCheckpointReceiptTests,ProcessSpawnLimitTests,PhoneHomeRollingRunnerTests,RefOnlyProductionCompositionTests,DelegationTestServicesTests,DelegationHarnessCensusTests,Card1277PtyHostFixtureTests | 14 | 7 | true |
| CP-6 | final | CP-1 | dispatcher budget | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | dispatch statement budgets | C1149_C1150_Statement_budgets | 1 | 1 | true |
| CP-7 | final | CP-1 | native exclusion policy | `/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | OS policy | ReleaseGatePlatformTests | 2 | 1 | true |
