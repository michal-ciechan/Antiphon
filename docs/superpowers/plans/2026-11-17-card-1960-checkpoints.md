# CARD-1960 checkpoint extract (Code task aaabade9)

This is the CARD-1960 plan extract that the reviewed
[CARD-1960/1961 workspace-slot fixes plan](2026-11-17-card-1960-1961-workspace-slot-fixes-plan.md)
asks for ("Rows A* belong to the CARD-1960 Code task ..., each run in its own plan extract").
Source: that plan at `1cb47ef5300ab1b7f053148b824d4d406d8a705b` (`feat/card-task-87ebe4f0`), reviewed
CLEAN in round 1 (Review 2a400c38, `8e7d811fc748f384718862ff84872e4358519d40`).

The only change is the checkpoint ids. `scripts/check-os-witnesses.ps1` reads `CP-<digits>` only and
throws `plan has no checkpoint rows` on `CP-A*` (round-1 advisory "OS-witness ids"), so CP-A`n` is CP-`n`
here (CP-A1 -> CP-1 ... CP-A13 -> CP-13). Every other cell is the plan's. The CARD-1961 rows (CP-B1..CP-B3)
stay in the plan for the CARD-1961 Code task. Positive controls stay pending; mutation is waived (operator 2026-11-12).

## Verification design

W-A1..W-A3 are `C1960QuarantineReleaseRouteTests` (CP-1); W-A4, W-A5 are `C1960WorkspaceSlotsScriptTests` (CP-2).
CP-3 keeps the cold-reset and record-release neighbours; CP-4..CP-10 and CP-12 are the CARD-1782 impacted
classes of the touched files; CP-11 refreshes `client/dist` for CP-12; CP-13 is the standing guard union.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | A1 | tests/Antiphon.Tests -> bin-c1960-route/ | portable-route | `/*/*/C1960QuarantineReleaseRouteTests/*` | W-A1..W-A3 | every method and case, 0 failed/skipped | 9 | 12 | true |
| CP-2 | A2 | tests/Antiphon.Tests -> bin-c1960-script/ | portable-script | `/*/*/C1960WorkspaceSlotsScriptTests/*` | W-A4, W-A5 | every case, 0 failed/skipped | 7 | 12 | true |
| CP-3 | A1 | tests/Antiphon.Tests -> bin-c1960-slotsrv/ | portable-slot-server-regression | `/*/*/(C0824WorkspaceSlotServerTests*)\|(C1656RecordReleaseHardeningTests*)/*` | DTO/route neighbours, cold-reset and record-release unchanged | both classes, 0 failed | 35 | 14 | true |
| CP-4 | A1 | tests/Antiphon.Tests -> bin-c1960-client-a/ | portable-client-impact-a | `/*/*/(AgentTaskInputFallbackTests*)\|(C1818RepositoryMaintenanceStatusTests*)\|(ClaudeDialogDeliveryTests*)\|(DefaultRunnerPinTests*)\|(DelegateBranchDeliveryTests*)\|(GrokRulesReadyOrderingTests*)\|(PhoneHomeBackpressureTests*)\|(PhoneHomeCompletedCardTransportTests*)\|(PhoneHomeConnectionTests*)\|(PhoneHomeImmediateSendTests*)\|(PhoneHomeQueuedTurnTests*)\|(PhoneHomeReadAdmissionTests*)/*` | CARD-1782 PhoneHomeRunnerClient callers (1/2) | every named class, 0 failed | 12 | 15 | true |
| CP-5 | A1 | tests/Antiphon.Tests -> bin-c1960-client-b/ | portable-client-impact-b | `/*/*/(PhoneHomeRecoveryEligibilityTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeSpillTransportTests*)\|(PhoneHomeStandingLaunchTests*)\|(PhoneHomeTaskDispatchProjectionTests*)\|(PtyConditionalReadClientTests*)\|(RunnerCapacityEndpointTests*)\|(RunnerCodexCliEvidenceTests*)\|(SessionMessageQueuePhoneHomeDropTests*)\|(TaskPlatformDispatchTests*)\|(TranscriptSinceClientTests*)/*` | CARD-1782 PhoneHomeRunnerClient callers (2/2) | every named class, 0 failed | 11 | 15 | true |
| CP-6 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-ops-a/ | portable-docs-ops-http-a | `/*/*/(AgentAttachHerdrTests*)\|(AgentBundleAttachmentTests*)\|(AgentSessionCancelledTurnTests*)\|(AgentSessionLaunchFailureTests*)\|(AgentSupervisionTests*)\|(AgentSystemPromptLaunchTests*)\|(AgentTaskCheckInterpreterTests*)\|(AgentTaskCheckScheduleTests*)\|(AgentTaskCheckSweepTests*)\|(AgentTaskDeadSessionReconciliationTests*)\|(AgentTaskDeliveryWatchdogTests*)\|(AgentTaskDispatchBaseGuardTests*)/*` | CARD-1782 rows for docs/ops-http.md (1/2) | every named class, 0 failed | 12 | 15 | true |
| CP-7 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-ops-b/ | portable-docs-ops-http-b | `/*/*/(AgentTaskDispatchFailureTests*)\|(AgentTaskInputSpillTests*)\|(AgentTaskInternalDecisionLifecycleTests*)\|(AgentTaskLandActiveSourceClaimTests*)\|(BlockedTaskParkProjectionTests*)\|(DockerStackDocumentationTests*)\|(InstructionBundleTests*)\|(InstructionFileStampTests*)\|(PipelineDefinitionDocumentationTests*)\|(RunnerBranchContractDocumentationTests*)\|(SessionRunnerAbsenceEvidenceDocumentationTests*)\|(StandingPipelinePolicyDocumentationTests*)/*` | CARD-1782 rows for docs/ops-http.md (2/2) | every named class, 0 failed | 12 | 15 | true |
| CP-8 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-stack-a/ | portable-docs-docker-stack-a | `/*/*/(ClaudeTokenRefreshOptInTests*)\|(CodexRootExecContractTests*)\|(CodexRunnerImageContractTests*)\|(DeployServer2ApiDefaultTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)\|(GrokRunnerImageContractTests*)\|(HangfireLeaseDocumentationTests*)\|(HangfirePostgreSqlStorageTests*)\|(HangfireStorageSelectionTests*)/*` | CARD-1782 rows for docs/docker-stack.md (1/2) | every named class, 0 failed | 10 | 12 | true |
| CP-9 | A3 | tests/Antiphon.Tests -> bin-c1960-docs-stack-b/ | portable-docs-docker-stack-b | `/*/*/(JqRunnerImageContractTests*)\|(RcBridgeProbePlatformTests*)\|(RemoteScriptContractTests*)\|(RunnerPushCredentialDocsTests*)\|(Server2ProductionServerContractTests*)\|(Server2SidecarStandingAgentsTests*)\|(Server2StagingServerContractTests*)\|(ServerImagePushCredentialTests*)\|(VolumeRetirementApplyGateTests*)\|(ZombieCensusPlatformTests*)/*` | CARD-1782 rows for docs/docker-stack.md (2/2) | every named class, 0 failed | 10 | 12 | true |
| CP-10 | A3 | tests/Antiphon.SessionRunner.Tests -> bin-c1960-docs-sr/ | linux-docs-gc-owner | `/*/*/C1818GcOwnerRobustnessTests/*` | CARD-1782 docker-stack.md SessionRunner row | every method, 0 failed | 10 | 8 | true |
| CP-11 | A1 | n/a | portable-frontend-build | `pwsh -NoProfile -Command 'npm --prefix client run build; exit $LASTEXITCODE'` | fresh client/dist for the contract row | exit 0 | n/a | 3 | true |
| CP-12 | A1 | tests/Antiphon.E2E -> bin-c1960-contract/ | portable-public-contract | `/*/*/ContractSnapshotTests/*` | CARD-1782 contracts kind (route, DTO) | every class method, 0 failed | 5 | 15 | true |
| CP-13 | A3 | tests/Antiphon.Tests -> bin-c1960-guards/ | portable-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | standing nine-class guard union (new classes' categories, layering) | 0 failed | 78 | 12 | true |

Notes on the counts (the plan's, renumbered):

- CP-1 Min is 9: W-A1 1, W-A2 7 cases, W-A3 1. Code added an eighth W-A2 case (a reason that holds the
  operator token), so CP-1 executes 10; Min stays the plan's floor.
- CP-2 Min is 7: W-A4 4 cases, W-A5 3 cases.
- CP-3 Min is 35: 25 + 10 results measured from source at c3cc0bb9b.
