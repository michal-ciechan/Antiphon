# CARD-2052 round 4 Code (task 0a714ec4): checkpoint lines and receipt validation

Branch `feat/card-task-0a714ec4`, re-cut on master `0f5fea9f158bad684030538d4cb908e8c467ff94`. Tested source
`71835f0a8572f58bd3f1a785ad92e36b67f8aca6`. Plan `docs/superpowers/plans/2026-11-23-card-2052-r3-checkpoints.md`.

## Red witnesses at 57cfe7ac34304d8bfffcb65996826bf089e9f2d8 (build-slot direct run)

- `included_config_core_worktree_edited_before_unlink_leaves_the_file_in_place`: failed. `File.Exists(target)` was False.
- `discovered_repository_gone_before_final_check_leaves_the_file_in_place`: failed. `File.Exists(target)` was False.

## Superseded run 20261123-084813-418b at 2cc125b6f845f0c72a4e9fb15a7865a9934cec9c: RED (13 green, 2 red)

The two red rows were caused by this round's first identity read, which went through `File.ReadAllText` instead of
the injected IO, so the C987 virtual fixtures refused:

- CP-2 `HostCleanupCheckpointExtractionTests.Naming_policy_and_facades_preserve_admission`: 100/101.
- CP-5 `HostCleanupCheckpointAuthorityTests.Every_tool_caller_uses_the_injected_context`: 158/159.

Fixed in 71835f0a. The full table was rerun below.

## Run 20261123-092346-6a75 at 71835f0a8572f58bd3f1a785ad92e36b67f8aca6: GREEN

```text
source: 71835f0a8572f58bd3f1a785ad92e36b67f8aca6 state=clean buildSource=verified
CHECKPOINT CP-9 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --verbosity minimal executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=105s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-9 waited=105s executed=2.977s wall=108.510s
CHECKPOINT CP-1 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=ok filter=/*/*/OutputCleanupTests/(a*)|(b*)|(c*)|(d*)|(e*)|(f*)|(g*)|(h*)|(i*)|(j*)|(l*)|(m*)|(n*)|(o*)|(p*)|(r*)|(s*)|(t*)|(u*)|(wrapper_*)|(windows_seal_docs_*) executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-1/run.trx slot=granted waited=60s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=60s executed=27.151s wall=87.819s
CHECKPOINT CP-2 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/(CheckpointOwnedOutputTests*)|(HostCleanupCheckpointExtractionTests*)|(CheckpointToolCopyCleanupTests*)|(PreflightCommandLineBudgetTests*)|(SafeDeleteProcessBoundaryTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointCompatibilityTests*)/* executed=101 passed=101 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=16.923s wall=17.652s
CHECKPOINT CP-3 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)|(ProcessSpawnLimitTests*)/* executed=142 passed=142 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=84.214s wall=84.522s
CHECKPOINT CP-4 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_*)|(C1210_Native_*)|(C1210_Roster_*)|(C1210_Audit_*)|(C1210_Host_*)|(C1226_*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=32.121s wall=32.571s
CHECKPOINT CP-5 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/(AnchoredPathDeleteWitnessTests*)|(C1307ToolRemovalTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointExecutorLogTests*)|(CheckpointLargeStampTests*)|(CheckpointOrFilterTests*)|(CheckpointProcessIdentityTests*)|(CheckpointSelectedOutputTests*)|(CheckpointStatePublicationTests*)|(CheckpointTaskOwnershipTests*)|(HostCleanupCheckpointAuthorityTests*)|(ReapedProcStatIdentityTests*)|(TempRootSweepAnchoredDeleteTests*)|(UnsafeDeleteRecoveryTests*)|(CheckpointReceiptSafetyTests*)|(CheckpointShadowCustodyTests*)/* executed=159 passed=159 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=110.185s wall=110.491s
CHECKPOINT CP-6 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/EvidenceFolderTests/(red_run_*)|(rerun_text_*)|(green_run_*)|(non_image_*)|(host_and_git_*)|(git_snapshot_*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=9.700s wall=10.146s
CHECKPOINT CP-7 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/CardFilePrivacyGitTests/(Managed_*)|(Unmerged_*)|(Subdirectory_*)|(Git_operation_*)|(Literal_NUL_*)|(AutoCommit_*)|(Index_only_*)|(Changed_generated_*)|(Tracked_stale_*)|(Containment_*) executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=23.194s wall=23.508s
CHECKPOINT CP-8 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/RcBridgeProbePlatformTests/(Linux_probe_*)|(Forced_*)|(Docker_*)|(Agent_*)|(Session_*)|(Health_*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=8.590s wall=9.185s
CHECKPOINT CP-10 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/(CheckpointAdmissionContractTests*)|(CheckpointImportTests*)|(DerivedImpactIndexTests*)|(IncrementalSelectionSoundnessTests*)|(LandGeneratedArtifactTests*)|(OuterRowBuildStampTests*)|(PhaseJournalKillDiagnosticTests*)|(RcInputManifestTests*)|(RcInputOwnersMapTests*)|(ReleaseImpactEnvelopeTests*)/* executed=141 passed=141 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=281.337s wall=281.793s
CHECKPOINT CP-11 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/(RowRunnerTests*)|(WaitCommandTests*)/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=0s executed=16.145s wall=16.457s
CHECKPOINT CP-12 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/RunCheckpointSourceScriptTests*/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-12 waited=0s executed=102.938s wall=103.723s
CHECKPOINT CP-13 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=0s executed=3.465s wall=3.761s
CHECKPOINT CP-14 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-14 waited=0s executed=3.252s wall=3.559s
CHECKPOINT CP-15 commit=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 build=reused filter=/*/*/OutputCleanupTests/(included_config_core_worktree_*)|(discovered_repository_gone_*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-092346-6a75/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-15 waited=0s executed=8.985s wall=9.503s
BUDGET wall=1601.496s execution=1331.782s admissionOnly=269.714s
unlisted: none (the tool ran no other build or test command)
wall: 26m42s  sequential-equivalent: 15m03s  builds: 3  max-concurrent-builds: 1  rows: 15 green 0 red 0 skipped
outputs: deleted bin-c2052-r3/, bin-c2052-r3-pty/, bin-c2052-r3-sr/
verdict: GREEN exit=0
```

- The executor's `FINALIZATION phase=output-cleanup seconds=50.117` covers the three outputs.
- `validate --evidence .../20261123-092346-6a75/report.json --expected-source-sha 71835f0a8572f58bd3f1a785ad92e36b67f8aca6`
  returned `CHECKPOINT SOURCE VALID source=71835f0a8572f58bd3f1a785ad92e36b67f8aca6 rows=15` (exit 0). The receipt
  has no unrostered skips: every row reports skipped=0.

## Hygiene candidate run 20261123-085954-e803 at 9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e: GREEN

```text
source: 9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e state=clean buildSource=verified
CHECKPOINT CP-5 commit=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --verbosity minimal executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e build=ok filter=/*/*/OutputCleanupTests/(a*)|(b*)|(c*)|(d*)|(e*)|(f*)|(g*)|(h*)|(i*)|(j*)|(l*)|(m*)|(n*)|(o*)|(p*)|(r*)|(s*)|(t*)|(u*)|(wrapper_*)|(windows_seal_docs_*) executed=45 passed=45 failed=0 skipped=0 trx=/tmp/claude-1654/-work-worktrees-ws-07-d2/2c4f942c-b127-4569-aa0a-b7abb6ee6197/scratchpad/hyg/.antiphon/checkpoints/20261123-085954-e803/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)|(ProcessSpawnLimitTests*)/* executed=142 passed=142 failed=0 skipped=0 trx=/tmp/claude-1654/-work-worktrees-ws-07-d2/2c4f942c-b127-4569-aa0a-b7abb6ee6197/scratchpad/hyg/.antiphon/checkpoints/20261123-085954-e803/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_*)|(C1210_Native_*)|(C1210_Roster_*)|(C1210_Audit_*)|(C1210_Host_*)|(C1226_*) executed=6 passed=6 failed=0 skipped=0 trx=/tmp/claude-1654/-work-worktrees-ws-07-d2/2c4f942c-b127-4569-aa0a-b7abb6ee6197/scratchpad/hyg/.antiphon/checkpoints/20261123-085954-e803/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e build=reused filter=/*/*/EvidenceFolderTests/(red_run_*)|(rerun_text_*)|(green_run_*)|(non_image_*)|(host_and_git_*)|(git_snapshot_*) executed=6 passed=6 failed=0 skipped=0 trx=/tmp/claude-1654/-work-worktrees-ws-07-d2/2c4f942c-b127-4569-aa0a-b7abb6ee6197/scratchpad/hyg/.antiphon/checkpoints/20261123-085954-e803/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e build=reused filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=27 failed=0 skipped=0 trx=/tmp/claude-1654/-work-worktrees-ws-07-d2/2c4f942c-b127-4569-aa0a-b7abb6ee6197/scratchpad/hyg/.antiphon/checkpoints/20261123-085954-e803/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 17m36s  sequential-equivalent: 6m25s  builds: 1  max-concurrent-builds: 1  rows: 6 green 0 red 0 skipped
outputs: deleted bin-c2052-hyg/
verdict: GREEN exit=0
```

- validate: `CHECKPOINT SOURCE VALID source=9bc6fe62b1f7b4925006d8fa5de96dce3e48d52e rows=6` (exit 0).
