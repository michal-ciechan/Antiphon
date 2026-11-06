Batch 91 verified successfully; publication is blocked because master advanced and the assigned branch contract forbids rewriting pushed history.

Base master: f8455526033cd36c152338197ea07ea5d4f7f412.
Verified code tip: 056da6695f1fa7288879ce7a7a58d9839d18be32, pushed on feat/card-task-e8915f2c.
Current fetched master: b37fc7bbd4d2357a7391675c45254a2c34c89b04. No master or rc ref was pushed by this task.
Preparation loops: 1; target-race observations: 1; re-pick loops: 0; master push attempts/retries: 0.

The final fetch found two new master commits: 6594ade3bbec04dfe2790d19e9ddd0742430e0f5 (park proof identity/remote reads) and b37fc7bbd4d2357a7391675c45254a2c34c89b04 (park fixture template). Read-only merge-tree completed without conflicts, tree 31c1acef18277989fb6d16fa419b8b79b3edf268. All four prepared commits remain '+' in git cherry origin/master HEAD. The combined tree at current master has not been built/tested and is not credited by these receipts.

Required continuation: commission a fresh assigned Merge branch/checkout rooted at current master, re-pick the four original source commits with -x, recompute the seals, and run only the target-race solution build, land-graph -Check and policy/contract classes. Reuse this completed publication-class evidence; do not repeat the green 31-minute publication selection for this target race. Keep master stable until the fresh helper publishes. This branch cannot be re-prepared in place: its runner contract says 'never rebase, amend or reset a pushed commit'; Git-common also forbids merging the target into the task branch or creating/switching branches. Standing merge/land authority was exercised without an approval pause; it does not remove that branch restriction.

## Changes and conflict resolution

Retained the exact CARD-0824 Windows exclusion for C0824TransportGitTests.Scratch_git_deadline_includes_inherited_output_drains, CARD-1731 snapshot restore optimization and isolation/work witnesses, Slow registrations, both deliverable plans and the mutation candidate. Ten files changed; no production source changed.

The scratch-Git pick conflicted only at policyHash and PinnedInputDigest. Both were recomputed before completing its cherry-pick. No conflict-marker commit was made. All four picks used cherry-pick -x and were individually pushed. The three ledger picks were conflict-free. The ledger source branch does not change the policy/seals, despite the brief's summary. The ten deliverable files are byte-identical to the prior batch-90 verified resolution, on the newer assigned master base.

## Commit and patch-ID proof

| Prepared SHA | Source SHA | Prepared stable patch-ID | Source stable patch-ID |
|---|---|---|---|
| b866624ad6934624231dadeb29e329267ceaf6dc | ce49cd02ed49e87cf93b5194ae29cfc7aea24ea7 | e38ddb1d31a2df632dc7498b5a0286e6638d83bb | 59f4ded2d4f13aa63b393467b2fef200ac4db1ac |
| 2139c50e2fdda3e7ef9e5ab6fe61a396263051ff | acc9f5f0a0044ed500b500a9e176496503dd1439 | 4acb0395cea8a629c5b926387ed35c71278d3d0b | 4acb0395cea8a629c5b926387ed35c71278d3d0b |
| 05b341ed166fabd56b05a902ee72207f8c64b2e8 | 436e196c407ee3ec798fb640b75de21915c09993 | 9bc90e4fac2b866d9949c6899075d486994c0694 | 9bc90e4fac2b866d9949c6899075d486994c0694 |
| 056da6695f1fa7288879ce7a7a58d9839d18be32 | dbd0631edf823fd21c66116401b7ca6c60b607bf | f39b00f4947e113bd8a94986c7cb0929d82208b9 | f39b00f4947e113bd8a94986c7cb0929d82208b9 |

The three ledger picks have exact raw patch-ID equality. The scratch pick intentionally substitutes current-tree seals. Its source and prepared zero-context deltas, omitting only changed seal lines, index metadata and hunk coordinates, have the same SHA-256: d9487d7588f1d85c9a9c38f1a7578450b4fe723db85efa71ef220bc86ff7ae19. Raw mismatch is retained above and is not represented as an unchanged pick.

## Input digests

| Input | Base recomputed/stored | Final recomputed/stored |
|---|---|---|
| policyHash | 0ac7b7c0dfcf6949ef1714fcfa02d4acd58e6258a8556209248338408f8f46a2 | 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72 |
| PinnedInputDigest | f510daab843687c8a873d4d3167e01088be50987e796d693729db0418ed16ada | 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939 |

Get-NightlyPolicyHash and -PrintInputDigest were recomputed at base and after all picks. Stored values equal computed values. Only tests/test-execution-policy.json changed among the five protected census inputs: the exact exclusion object plus its seal. Protected libraries/discovery input are unchanged. The initial policy-hash helper invocation omitted nightly-common.ps1 and produced missing-function diagnostics; it was immediately corrected with the complete imports before any source mutation, yielding the valid base values above.

## Verification

All test runs verify committed code tip 056da6695f1fa7288879ce7a7a58d9839d18be32 using its native default solution outputs, UseAppHost=false and --no-build. No source edits occurred during the build/tests. No desktop run occurred. All owned foreground commands are awaited before settlement. No detached/external executor was used.

- Base land-graph -Check: PASS. Final -Check: PASS. Both report digest D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, count 69, phase=pass2, approved. Selected manifest/evaluation/target pins records are unchanged. Never ran -Update.
- Justified infrastructure build: missing native land-graph evaluator built once under its own slot, 0 warnings/errors, 6.91 seconds; slot wait 120 seconds.
- One serialized Antiphon.sln build: 0 errors, 1887 warnings, 8m43.37s; slot wait 15 seconds. No NETSDK1047 and no rebuild.
- check-evidence-diff code range: commits=4, entries=0, violations=0. git diff --check passed.
- Policy/contract row: 108 passed, 0 failed/skipped, 9m50.181s. Full four-class policy sweep 47/47; mechanical/platform guidance 16/16; NativeLinuxPolicyWitnessTests 3/3; LinuxOnlyWindowsExclusionPolicy and all other ReleaseGatePlatformTests green; scratch-Git class 3/3; census pin and policyHash consumers green. RcCoverageAdmissionTests is 1/1 TUnit method requiring all seven exact named harness assertions (7/7).
- Mechanical instruction rehearsal: 38 passed, 0 failed, exit 0. Slot wait 271 seconds; execution/lease hold 28 seconds.
- Report-policy consumer: 2 passed, 0 failed/skipped, 3m00.927s. Slot wait 135 seconds. Uses real isolated API/database; no frontend assets required. The fixture logged unavailable localhost:19092 broker diagnostics; both acceptance cases passed.
- Publication class: 15/15; C599 snapshot class: 2/2; combined 17 passed, 0 failed/skipped, 31m18.201s. One invocation/lease, no repeated green case. Slot wait 240 seconds; lease held 1884 seconds.
- Total: 127/127 TUnit cases plus 38/38 mechanical assertions. No CARD-1733 allocator test was selected or timed out. No publication test timed out.

PERFORMANCE FOLLOW-UP REQUIRED: the publication selection exceeded the repository's 30-minute Debug threshold. The caller should dispatch a Debug performance task before scheduling another full run, using source 056da6695f1fa7288879ce7a7a58d9839d18be32, the exact publication filter below, host dfcc116be984, runId=merge91/groupId=publication/attempt=1, header epoch 2026-11-06T04:04:51.3360369Z and the per-case durations below. This delegate did not sub-delegate. This is an observed cost finding, with introduced/inherited attribution unresolved; the acceptance cases passed. No retry was used to conceal cost.

The unchanged execution cap is 300 seconds. ExecutionLedger / Joins / Markers took 183.195 / 192.427 / 187.778 seconds. ExecutionLedgerChunks took 206.929 seconds versus the batch-89 reference of 266 seconds (4m26s). Every case completed below the cap; no LOUD timeout warning was triggered. These are TRX durations, including wrapper/process overhead; optional detailed body/restore phases were unavailable as described below.

The source-branch red/green evidence and standing merge/land authority authorize the test-only master integration without a separate Review. No approval pause was required. The intended publication is master-only, not rc activation; no target ref, tag or running service was changed.

## Performance and provenance

Host dfcc116be984; Linux x64, Debian 12; SDK 10.0.403, runtime .NET 9.0.22, TUnit 1.44.0 / MTP 2.2.2. Observed load samples included 35.91 during build and 62.16 during publication queueing. Slot waits are reported separately from run durations.

Phase recording is enabled for the two Antiphon.Tests runs with runId=merge91, attempt=1, suite=antiphon, groupId=policy-contract/publication. Each completed record carries exact test identity, assembly moduleVersionId, outcome and offsets. The policy records contain no detailed phases/processes: individual database, HTTP, filesystem, fixture and teardown costs therefore remain unknown/unattributed, not proven zero. E2E phase recording was requested but no corresponding file was emitted; its phase costs remain unknown. C1731_TIMING_ROOT was not set, so the optional detailed snapshot filesystem/restore counters were not retained; this run records total case durations without attributing those unobserved costs. No green case was rerun for instrumentation. TRX durations and exact case membership are preserved below before output cleanup. No production timeout, assertion, retry or execution budget was widened.

## Reproduction

Build (one slot): `TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge91-final-solution -- dotnet build Antiphon.sln -p:UseAppHost=false -m:1 --nologo`.

Each test invocation uses a separate slot: `TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label <row> -- dotnet run --project <project> --no-build -p:UseAppHost=false -- --treenode-filter '<filter>' --report-trx --report-trx-filename <row>.trx --results-directory <fresh-absolute-directory>`.

Policy filter: `/*/*/(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(ReleaseGatePlatformTests*)|(RcCoverageAdmissionTests*)|(LiveLaneRunnerTests*)|(C0824TransportGitTests*)|(RcFailureHistoryTests*)|(ReleaseGateActivationTests*)|(InterimVerificationReadinessTests*)|(ReleaseGatePolicyTests*)/*` in tests/Antiphon.Tests.

Publication filter: `/*/*/(ReleaseGatePublicationAuthorityTests*)|(C599SnapshotTests*)/*` in tests/Antiphon.Tests. Report-consumer filter: `/*/*/ReleaseGateReportDeliveryTests*/*` in tests/Antiphon.E2E.

Mechanical rehearsal: `TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge91-mechanical -- pwsh -NoProfile -File scripts/test-mechanical-pc-contract.ps1 -ResultsDirectory <fresh-absolute-directory>`.

Audit: `TEMP=/tmp pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll`.

Outer command bounds were 25 minutes for each build/policy row, 20 minutes for E2E, 15 minutes for mechanical and 45 minutes including queue for publication. These are finite command containment bounds, not changes to any per-test budget. Publication keeps its existing 300-second execution cap.

## Exact executed membership and per-test durations

| Row | Class | Test | Outcome | Seconds |
|---|---|---|---|---:|
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(docs/orchestration-loop·md) | Passed | 0.045 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V05b_CodePreparesUnboundCandidates | Passed | 0.063 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(server/Bundles/orchestrator·md) | Passed | 0.064 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V06_OwnerDocHoldsTheContract | Passed | 0.027 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V06b_WorkedExamplesAreIllustrative | Passed | 0.021 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(·claude/skills/antiphon-delegate/SKILL·md) | Passed | 0.034 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V05_MutationBundleFailsClosedAtLow | Passed | 0.063 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(server/Bundles/README·md) | Passed | 0.005 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(server/Bundles/delegate-basics·md) | Passed | 0.002 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_V05c_ReviewJudgesAdequacyWithoutMutants | Passed | 0.012 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_PolicyHash | Passed | 0.135 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_QualificationRevision | Passed | 0.135 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_QualifiedRepository | Passed | 0.135 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_QualifiedProject | Passed | 0.135 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_FutureMonitor | Passed | 0.135 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_ScriptHash | Passed | 0.135 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_RunIdentity | Passed | 0.137 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_QualificationReceipt | Passed | 0.137 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_MonitorVerdict | Passed | 0.137 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C545_WatchdogInstance | Passed | 0.142 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_ReadFailure | Passed | 0.143 |
| policy-contract | Antiphon.Tests.Application.ReleaseGateActivationTests | C599_FinalRecovery | Passed | 0.039 |
| policy-contract | Antiphon.Tests.Application.TaskPlatformGuidanceTests | Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning | Passed | 0.003 |
| policy-contract | Antiphon.Tests.Application.TaskPlatformGuidanceTests | Stage_bundles_leave_twenty_characters_below_the_size_cap | Passed | 0.003 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_MonitorAge | Passed | 0.144 |
| policy-contract | Antiphon.Tests.Application.TaskPlatformGuidanceTests | Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes | Passed | 0.003 |
| policy-contract | Antiphon.Tests.Application.TaskPlatformGuidanceTests | Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes | Passed | 0.014 |
| policy-contract | Antiphon.Tests.Application.ReleaseGateActivationTests | C599_RollbackLatch | Passed | 0.038 |
| policy-contract | Antiphon.Tests.Application.ReleaseGateActivationTests | C599_QueuedReadiness | Passed | 0.095 |
| policy-contract | Antiphon.Tests.Application.MechanicalPcContractTests | C479_R06_LowNeverDiscoversAndTierRowsStayLow | Passed | 0.035 |
| policy-contract | Antiphon.Tests.Application.ReleaseGateActivationTests | C599_Admission | Passed | 0.099 |
| policy-contract | Antiphon.Tests.TestHelpers.SlowTestTripwireTests | unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not | Passed | 0.092 |
| policy-contract | Antiphon.Tests.TestHelpers.SlowTestTripwireTests | allowlist_file_exists_and_has_spawn_lane_entries | Passed | 0.118 |
| policy-contract | Antiphon.Tests.Application.ReleaseGateActivationTests | C599_Defaults | Passed | 0.162 |
| policy-contract | Antiphon.Tests.Application.TaskPlatformGuidanceTests | Review_guidance_preserves_every_instruction | Passed | 0.267 |
| policy-contract | Antiphon.Tests.Application.InterimVerificationReadinessTests | C544_DisabledByDefault | Passed | 0.611 |
| policy-contract | Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | Pinned_attribute_contract_matches_native_platform | Passed | 0.639 |
| policy-contract | Antiphon.Tests.Application.C0824TransportGitTests | Scratch_git_deadline_includes_inherited_output_drains | Passed | 2.050 |
| policy-contract | Antiphon.Tests.Application.C0824TransportGitTests | Scratch_git_preserves_caller_cancellation | Passed | 0.002 |
| policy-contract | Antiphon.Tests.Application.C0824TransportGitTests | Scratch_git_returns_output_and_preserves_nonzero_exit | Passed | 0.029 |
| policy-contract | Antiphon.TestSupport.TestClassificationGuardTests | Registry_matches_compiled_metadata | Passed | 3.702 |
| policy-contract | Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Source_only_attribute_does_not_rescue_first_rebase | Passed | 4.148 |
| policy-contract | Antiphon.Tests.Scripts.LiveLaneRunnerTests | LockBeforeSlot | Passed | 5.074 |
| policy-contract | Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Union_collapsed_lines_are_rejected(different-reasons-same-class) | Passed | 7.488 |
| policy-contract | Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Rebase_preserves_distinct_appended_pairs(common-base) | Passed | 7.509 |
| policy-contract | Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | Markers_do_not_expand_policy_exclusions | Passed | 7.823 |
| policy-contract | Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Union_collapsed_lines_are_rejected(same-reason-different-classes) | Passed | 7.693 |
| policy-contract | Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | Policy_rows_require_pinned_attribute | Passed | 7.913 |
| policy-contract | Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Rebase_preserves_distinct_appended_pairs(target-only) | Passed | 5.537 |
| policy-contract | Antiphon.Tests.Application.ReleaseGateActivationTests | C599_CompletionHandoffs | Passed | 9.939 |
| policy-contract | Antiphon.Tests.Application.ReleaseGateActivationTests | C599_MasterDueBoundary | Passed | 9.960 |
| policy-contract | Antiphon.Tests.Scripts.LiveLaneRunnerTests | WrapperOnlyPaidManifest | Passed | 16.246 |
| policy-contract | Antiphon.Tests.Scripts.LiveLaneRunnerTests | ChildEnvironmentIsScoped | Passed | 3.971 |
| policy-contract | Antiphon.Tests.Scripts.LiveLaneRunnerTests | DeadlineJoinsOwnedChildren | Passed | 3.471 |
| policy-contract | Antiphon.Tests.Scripts.RcCoverageAdmissionTests | Windows_and_linux_obligations_remain_exact | Passed | 6.854 |
| policy-contract | Antiphon.Tests.Scripts.RcFailureHistoryTests | EvidenceUnion | Passed | 35.212 |
| policy-contract | Antiphon.Tests.Scripts.RcFailureHistoryTests | RefusesIncompleteEvidence | Passed | 95.919 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePlatformTests | C1199_WindowsRequiredRoster | Passed | 13.876 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePlatformTests | C1323_PlatformExecution | Passed | 89.645 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePlatformTests | LinuxOnlyWindowsExclusionPolicy | Passed | 9.590 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePlatformTests | C1686_LinuxOnlyCensus | Passed | 23.790 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePlatformTests | C1415_SkipExclusions | Passed | 6.999 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePlatformTests | C1323_PlatformPolicy | Passed | 50.675 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePolicyTests | C599_ProfileSchema | Passed | 10.259 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePolicyTests | C599_E2EWatchdog | Passed | 10.159 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePolicyTests | C599_ProfileSuites | Passed | 9.562 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePolicyTests | C599_MetadataParsers | Passed | 8.074 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePolicyTests | C599_EligibilityCensus | Passed | 5.958 |
| policy-contract | Antiphon.Tests.Scripts.ReleaseGatePolicyTests | C599_ExpandedCoverage | Passed | 9.990 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_ReadRegistry_retains_legacy_layout_behavior | Passed | 0.002 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Layout_opt_in_is_scoped(other-assembly) | Passed | 0.001 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G059 | Passed | 1.388 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Layout_opt_in_is_scoped(non-repository) | Passed | 1.329 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted(\n, False) | Passed | 1.364 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted(\n, True) | Passed | 1.721 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted(\n, False) | Passed | 2.695 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Detached_reason_is_rejected(eof) | Passed | 1.910 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Header_after_records_is_rejected(interrupts-first-pair) | Passed | 1.031 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Header_after_records_is_rejected(between-pairs) | Passed | 1.559 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Row_without_adjacent_reason_is_rejected(second-row) | Passed | 1.577 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Row_without_adjacent_reason_is_rejected(bare-row) | Passed | 1.251 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Detached_reason_is_rejected(overwritten) | Passed | 1.817 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Detached_reason_is_rejected(blank) | Passed | 2.140 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | Missing_slow_class_is_named | Passed | 1.906 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted(\n, True) | Passed | 4.023 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G060 | Passed | 1.403 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G069(optin) | Passed | 25.238 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G062(duplicate-fqn) | Passed | 4.069 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G076(missing) | Passed | 1.001 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G075 | Passed | 1.398 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G074 | Passed | 1.820 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G073(full) | Passed | 0.822 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G073(simple) | Passed | 1.226 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G072 | Passed | 0.001 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G071 | Passed | 0.000 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G070 | Passed | 0.002 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G061 | Passed | 2.320 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G076(empty) | Passed | 1.716 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G068 | Passed | 1.420 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G067(no-inherits) | Passed | 25.053 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G067(inherits) | Passed | 14.471 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G066 | Passed | 18.360 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G065 | Passed | 17.664 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G064 | Passed | 2.124 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G063 | Passed | 1.490 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G062(shared-simple) | Passed | 0.705 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G069(ordinary) | Passed | 18.101 |
| policy-contract | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G076(malformed) | Passed | 0.806 |
| report-consumer | Antiphon.E2E.ReleaseGateReportDeliveryTests | C599_ReportReadback | Passed | 6.018 |
| report-consumer | Antiphon.E2E.ReleaseGateReportDeliveryTests | C599_ReportRecovery | Passed | 67.131 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_ExecutionLedgerMarkers | Passed | 187.778 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PinnedPolicyBindings | Passed | 125.425 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_ReceiptReadback | Passed | 132.580 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PinnedPolicySuiteFields | Passed | 100.236 |
| publication | Antiphon.Tests.Scripts.C599SnapshotTests | Finally_restores_after_a_throwing_mutation | Passed | 31.075 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PinnedPolicyFields | Passed | 79.083 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PinnedPolicyBlobFields | Passed | 60.002 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_ExecutionLedgerJoins | Passed | 192.427 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PinnedPolicyIdentityFields | Passed | 131.116 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PublisherTestMode | Passed | 34.294 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PinnedPolicySchema | Passed | 75.969 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_ExecutionLedgerReceipt | Passed | 120.263 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_ExecutionLedger | Passed | 183.195 |
| publication | Antiphon.Tests.Scripts.C599SnapshotTests | Restore_runs_once_and_only_writes_changed_bytes | Passed | 17.174 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_PinnedPolicy | Passed | 102.791 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_ExecutionLedgerCounts | Passed | 87.554 |
| publication | Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests | C599A_ExecutionLedgerChunks | Passed | 206.929 |

## Build-slot receipts

All direct build/test drivers were gated. Mechanical is a non-dotnet fixture rehearsal: its wrapper warning about -maxcpucount does not represent a nested build.

```text
base-tool-build.log
BUILD SLOT granted lease=58f5c511-faa3-40c9-b549-df48eeb1c251 waited=120s maxcpucount=6
BUILD SLOT released lease=58f5c511-faa3-40c9-b549-df48eeb1c251 held=7s
solution-build.log
BUILD SLOT granted lease=ff37850d-ddc2-4217-8258-7f75b13f0bb5 waited=15s maxcpucount=6
BUILD SLOT released lease=ff37850d-ddc2-4217-8258-7f75b13f0bb5 held=524s
policy.log
BUILD SLOT granted lease=c56c93e6-17d9-44a3-8d50-6db6e9c601fc waited=0s maxcpucount=6
BUILD SLOT released lease=c56c93e6-17d9-44a3-8d50-6db6e9c601fc held=594s
mechanical.log
BUILD SLOT granted lease=f1d6a126-3391-4fc1-8a59-2a08090a2aa3 waited=271s maxcpucount=6
BUILD SLOT released lease=f1d6a126-3391-4fc1-8a59-2a08090a2aa3 held=28s
report-consumer.log
BUILD SLOT granted lease=848d48b8-f9c0-42b8-95eb-c9de3af6a536 waited=135s maxcpucount=6
BUILD SLOT released lease=848d48b8-f9c0-42b8-95eb-c9de3af6a536 held=185s
publication.log
BUILD SLOT granted lease=e4f8fe7f-5b71-4488-b5ec-21a287175a3e waited=240s maxcpucount=6
BUILD SLOT released lease=e4f8fe7f-5b71-4488-b5ec-21a287175a3e held=1884s
```

Compiled artifact SHA-256 before cleanup:

- /work/worktrees/task-e8915f2c/tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll: 0119a707db428e2ce5dda99ff5f13b0f419ba5520583b5d7be62d8d8fc766fd2

- /work/worktrees/task-e8915f2c/tests/Antiphon.E2E/bin/Debug/net9.0/Antiphon.E2E.dll: 2854225c8b3ecc0f18f423e4ba04e67048321766fc12563c3e64cde102460951

Antiphon.Tests moduleVersionId in both policy and publication phase headers: 708994434b7346c289ecee48663480eb. All 127 TRX case identities/outcomes/durations are retained above.

## Cleanup

All 104 owned native bin/obj directories, E2E TestOutput, test-result/timing directories and the task scratch-output root were deleted using exact literal absolute paths. Both audit canaries were confirmed absent. No Docker volume, other checkout or other session was removed. Raw generated output was removed after preserving counts, exact membership, timings and build/slot provenance in this report.

--- next stage ---
next: merge
handoff: Commission a fresh assigned branch at current master; re-pick originals and run only target-race build, land-graph and policy/contract checks. Reuse the completed publication evidence. Separately dispatch the required Debug performance follow-up for the 31m18s publication selection.
artifact: /work/worktrees/task-e8915f2c/.antiphon/task-e8915f2c.md
