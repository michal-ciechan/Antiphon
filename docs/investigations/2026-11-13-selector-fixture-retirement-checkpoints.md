# Selector fixture retirement Debug verification

Task: 49ae044d. Base: 90ae19f65bc4aff58a8f10162614c94e14d42bde (master at dispatch).
Branch: feat/card-task-49ae044d. Mutation checks waived by the operator on 2026-11-12.

The red-first native-default build and exact new-method union are additional runs
required to reproduce both defects before their fix. At 512d58221 they executed
7 cases: 4 passed, 3 assertion failures, 0 skipped. Failures: ignored fixture Ready,
unpublished parent reaching its publication refusal, fixture location outside checkout.
The fixed selection also includes a real linked-worktree parent.
Safety refinement at 754306353: the additional exact method
Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof executed
1 case and failed its exit-code assertion (expected refusal 2, actual Ready 0).
This separately committed red witness required one additional native-default build
(384 seconds) and 23 seconds execution. The first checkpoint attempt
20261113-110706-6655 was stopped during its first build before any tests, to close
that edge; it receives no green credit. The fixed parent now requires origin and
the ordinary full publication proof; known disposable seed parents cannot substitute.

## Verification design

The following is the closed ordinary selection. CP-1 exercises the real Bash
retirement code through the existing fake Docker/HTTP host fixtures, including
preview, apply refusal gates, receipts, inventory and publication. The separate
Docker integration classes are excluded: this task prohibits touching Docker
volumes or a live host; no live retirement or deployment is authorized here.
CP-2 covers the fixture producer, requested guard union and touched script contracts.
No full assembly or nightly run is warranted for this scripts/tests fix.

Build the native-default Antiphon.DerivedImpactIndex driver through build-slot,
then run regenerate-derived-impact-index.ps1 -NoBuild through build-slot before
these rows. Commit changed generated index bytes as their own slice.
The checkpoint runner owns each isolated build and slot; run serially in this worktree.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-debug49-retirement/ | retirement | `/*/*/(VolumeRetirementContentTests*)\|(VolumeRetirementApplyGateTests*)\|(VolumeRetirementReceiptTests*)\|(VolumeRetirementHostProofTests*)\|(VolumeRetirementWrapperTests*)\|(RollingVolumeRecycleScriptTests*)\|(RollingVolumeRecycleCensusChurnTests*)\|(RetiredTempContainerScriptTests*)\|(RetiredTempContainerHostTests*)\|(RetireTempRunnerTimestampTests*)\|(C1008PlatformContractTests*)\|(C1008HostFixtureSetupInsertionTests*)\|(C0824BranchDeletionRecycleAuditTests*)/*` | safe fixture classification, all refusal boundaries, existing retirement/recycle contracts | all listed, 0 failed | 1 | 20 |
| CP-2 | all | tests/Antiphon.Tests -> bin-debug49-guards/ | producer-guards | `/*/*/(ReleaseImpactEnvelopeTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(ProcessSpawnLimitTests*)\|(ShellFixtureLineEndingContractTests*)\|(RemoteScriptContractTests*)\|(DockerStackDocumentationTests*)/*` | temp fixture producer, required guard union, process ownership and script/document contracts | all listed, 0 failed | 1 | 8 |

## Performance budget

Provisional 28-minute ordinary budget (20 + 8), including two isolated cold builds;
the first native-default red build measured 332 seconds, and red execution measured
67 seconds for 7 cases on this loaded Linux host. No comparable p95 is available;
existing test timeouts are unchanged. Each row is intended to remain below 30 minutes.
If a row exceeds that bound, retain its timings for caller Debug triage rather than
repeating it. Fresh host fixture state is necessary per destructive/refusal vector;
the existing per-session Compose seed is shared. Retain TRX durations, slot/build
timing and existing fixture phases in .antiphon/checkpoints and .antiphon/debug-49ae044d;
unattributed phases remain unknown. The strict process-spawn limiter serializes
these children inside the assembly, and checkpoint --serial prevents overlapping builds.
