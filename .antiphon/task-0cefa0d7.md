# CARD-2052 r1 repair (task 0cefa0d7): unedited CHECKPOINT lines

## run 20261122-225246-ac11

```
CHECKPOINT CP-1 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=ok filter=/*/*/(OutputCleanupTests*)|(CheckpointOwnedOutputTests*)|(HostCleanupCheckpointExtractionTests*)|(CheckpointToolCopyCleanupTests*)|(PreflightCommandLineBudgetTests*)|(SafeDeleteProcessBoundaryTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointCompatibilityTests*)/* executed=154 passed=154 failed=0 skipped=4 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=35.468s wall=35.765s
CHECKPOINT CP-2 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(ProcessSpawnLimitTests*)/* executed=148 passed=148 failed=0 skipped=1 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=127.645s wall=128.364s
CHECKPOINT CP-3 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=reused filter=/*/*/(AnchoredPathDeleteWitnessTests*)|(C1307ToolRemovalTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointExecutorLogTests*)|(CheckpointLargeStampTests*)|(CheckpointOrFilterTests*)|(CheckpointProcessIdentityTests*)|(CheckpointSelectedOutputTests*)|(CheckpointStatePublicationTests*)|(CheckpointTaskOwnershipTests*)|(EvidenceFolderTests*)|(HostCleanupCheckpointAuthorityTests*)|(ReapedProcStatIdentityTests*)|(TempRootSweepAnchoredDeleteTests*)|(UnsafeDeleteRecoveryTests*)|(CheckpointReceiptSafetyTests*)|(CheckpointShadowCustodyTests*)/* executed=165 passed=164 failed=1 skipped=1 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=124.961s wall=125.622s
FAILED Antiphon.Tests.Checkpoints.TempRootSweepAnchoredDeleteTests.fifo_and_socket_inside_the_root_are_removed (CP-3) ShouldAssertException: BindSocket(socketPath)     should be 0     but was -1  Additional Info:     socket bind errno 0 -> rows/CP-3/failures.md
CHECKPOINT CP-4 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=reused filter=/*/*/(CheckpointRecoveryWindowsTests*)|(CheckpointWindowsAnchoredDeleteTests*)|(CheckpointWindowsAnchoredPathDeleteWitnessTests*)|(CardFilePrivacyGitTests*)|(CardFilePrivacyPathTests*)|(RcBridgeProbePlatformTests*)/* executed=20 passed=20 failed=0 skipped=33 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=39.894s wall=40.318s
CHECKPOINT CP-5 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=reused filter=/*/*/(CheckpointAdmissionContractTests*)|(CheckpointImportTests*)|(DerivedImpactIndexTests*)|(IncrementalSelectionSoundnessTests*)|(LandGeneratedArtifactTests*)|(OuterRowBuildStampTests*)|(PhaseJournalKillDiagnosticTests*)|(RcInputManifestTests*)|(RcInputOwnersMapTests*)|(ReleaseImpactEnvelopeTests*)/* executed=141 passed=141 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=305.647s wall=306.202s
CHECKPOINT CP-6 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=reused filter=/*/*/(RowRunnerTests*)|(WaitCommandTests*)/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=12.595s wall=12.903s
CHECKPOINT CP-7 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=reused filter=/*/*/RunCheckpointSourceScriptTests*/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=84.764s wall=85.349s
CHECKPOINT CP-8 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=4.680s wall=5.199s
CHECKPOINT CP-9 commit=824e04c9df32d51e16a069628b2691971ffb81f6 build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-225246-ac11/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=824e04c9df32d51e16a069628b2691971ffb81f6 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=3.894s wall=4.342s
unlisted: none (the tool ran no other build or test command)
wall: 31m54s  sequential-equivalent: 12m24s  builds: 3  max-concurrent-builds: 1  rows: 8 green 1 red 0 skipped
outputs: refused/kept bin-c2052-final/, bin-c2052-pty/, bin-c2052-sr/
verdict: RED exit=1
2026-11-22T23:29:22.7715986+00:00 FINALIZATION phase=output-cleanup seconds=0.113
```

## run 20261122-233041-9dbc

```
CHECKPOINT CP-3 commit=44694e7f92a9aec72bc97163f1f280665a82a9f0 build=ok filter=/*/*/(AnchoredPathDeleteWitnessTests*)|(C1307ToolRemovalTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointExecutorLogTests*)|(CheckpointLargeStampTests*)|(CheckpointOrFilterTests*)|(CheckpointProcessIdentityTests*)|(CheckpointSelectedOutputTests*)|(CheckpointStatePublicationTests*)|(CheckpointTaskOwnershipTests*)|(EvidenceFolderTests*)|(HostCleanupCheckpointAuthorityTests*)|(ReapedProcStatIdentityTests*)|(TempRootSweepAnchoredDeleteTests*)|(UnsafeDeleteRecoveryTests*)|(CheckpointReceiptSafetyTests*)|(CheckpointShadowCustodyTests*)/* executed=165 passed=165 failed=0 skipped=1 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-233041-9dbc/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=44694e7f92a9aec72bc97163f1f280665a82a9f0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=150.874s wall=151.242s
unlisted: none (the tool ran no other build or test command)
wall: 9m59s  sequential-equivalent: 2m31s  builds: 1  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-c2052-cp3/
verdict: GREEN exit=0
2026-11-22T23:44:58.2903978+00:00 FINALIZATION phase=output-cleanup seconds=49.046
```

## run 20261122-234551-91e8

```
CHECKPOINT CP-1 commit=686b20228c7a71dd571e5056f3975bd0f5a21a63 build=ok filter=/*/*/(OutputCleanupTests*)|(CheckpointOwnedOutputTests*)|(HostCleanupCheckpointExtractionTests*)|(CheckpointToolCopyCleanupTests*)|(PreflightCommandLineBudgetTests*)|(SafeDeleteProcessBoundaryTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointCompatibilityTests*)/* executed=154 passed=154 failed=0 skipped=4 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-234551-91e8/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=686b20228c7a71dd571e5056f3975bd0f5a21a63 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=27.655s wall=28.190s
unlisted: none (the tool ran no other build or test command)
wall: 7m34s  sequential-equivalent: 0m28s  builds: 1  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-c2052-named/
verdict: GREEN exit=0
2026-11-22T23:57:16.4011554+00:00 FINALIZATION phase=output-cleanup seconds=43.909
```

## Red/green

Witnesses committed red at e7b321968 / a03470bc95f5712b7d18bed5f66d9a7e4221828f (OutputCleanupTests 47 passed, 6 failed, 4 skipped:
four tracked targets unlinked, include and linked-worktree re-read misses); fix db0988e7f + 59f578da7 (OutputCleanupTests 53/0/4).
Direct runs went through build-slot (dotnet build + dotnet run --no-build, UseAppHost=false), outside the checkpoint table, to prove red then green.

## Receipt validation

- 20261122-225246-ac11: tool validate refuses the whole report (report_failed) for the CP-3 red, even with --rows.
- 20261122-233041-9dbc (CP-3 rerun, TMPDIR=/tmp): script validator skip_identity_unlisted for one pre-existing skip
  EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open (not rostered, not in the named eight).
- 20261122-234551-91e8 (CP-1 alone): scripts/validate-checkpoint-receipt.ps1 -> CHECKPOINT SOURCE VALID source=686b20228c7a71dd571e5056f3975bd0f5a21a63 rows=1;
  tool validate -> row_failed (it does not read the WindowsOnly roster for skips).

## Other

- evidence-diff (build-slot, waited 241 s): EVIDENCE result commits=9 entries=0 violations=0 base=ecc9bfaa5 head=824e04c9d.
- merge-tree vs origin/master d4b20888a: clean, tree 4d1cf2fc1308e82aaafa934974d98e950ae8bc37.
- derived impact index regenerated (build-slot): unchanged.
