# CARD-2052 r2 repair (task a3ce9932): checkpoint lines and receipt validation

Subject of Review 2ff6a0bc (round 2): aad1b18fe45761cec4667033b727c14a649b115c. Tested SHA: 32e16c27c492d7604b6b6d773d4d949f7eed64a9.
Plan: docs/superpowers/plans/2026-11-23-card-2052-r2-checkpoints.md (native Windows rows pending:
docs/superpowers/plans/2026-11-23-card-2052-r2-checkpoints-windows.md).

## Red/green (D-1 round 2)

- Red at 69508cdc7e7645b00c5d3490525bdb74fff3860d: OutputCleanupTests/git_fallback_target_staged_during_its_hash_leaves_the_file_in_place
  failed 1/1 (File.Exists(target) should be True but was False: "a file the index lists is never unlinked").
- Green at 5aa936ede661dfdf1520e0dbc9fecc061b176d2b: OutputCleanupTests 54 passed, 0 failed, 4 skipped (the rostered Windows methods).
- Both were direct build-slot runs (dotnet build plus dotnet run --no-build) to show red then green, outside the table. Two more
  build-slot runs probed filter grammar: `/*/*/OutputCleanupTests/!(windows_*)` ran 0 tests (exit 8), so negation is unavailable.
  The method-prefix form ran 54/0/0.

## run 20261123-010457-6935 (start --row-timeout 25m; derived total cap)

```
CHECKPOINT CP-9 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=n/a filter=dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --verbosity minimal executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-9 waited=0s executed=4.580s wall=5.270s
CHECKPOINT CP-1 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=ok filter=/*/*/OutputCleanupTests/(a*)|(b*)|(c*)|(d*)|(e*)|(f*)|(g*)|(h*)|(i*)|(j*)|(l*)|(m*)|(n*)|(o*)|(p*)|(r*)|(s*)|(t*)|(u*)|(wrapper_*)|(windows_seal_docs_*) executed=54 passed=54 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=25.411s wall=25.808s
CHECKPOINT CP-2 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/(CheckpointOwnedOutputTests*)|(HostCleanupCheckpointExtractionTests*)|(CheckpointToolCopyCleanupTests*)|(PreflightCommandLineBudgetTests*)|(SafeDeleteProcessBoundaryTests*)|(HostCleanupCheckpointCustodyTests*)|(HostCleanupCheckpointCompatibilityTests*)/* executed=101 passed=101 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=14.910s wall=15.193s
CHECKPOINT CP-3 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)|(ProcessSpawnLimitTests*)/* executed=142 passed=142 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=83.749s wall=84.322s
CHECKPOINT CP-4 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_*)|(C1210_Native_*)|(C1210_Roster_*)|(C1210_Audit_*)|(C1210_Host_*)|(C1226_*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=36.858s wall=37.499s
CHECKPOINT CP-5 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/(AnchoredPathDeleteWitnessTests*)|(C1307ToolRemovalTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointExecutorLogTests*)|(CheckpointLargeStampTests*)|(CheckpointOrFilterTests*)|(CheckpointProcessIdentityTests*)|(CheckpointSelectedOutputTests*)|(CheckpointStatePublicationTests*)|(CheckpointTaskOwnershipTests*)|(HostCleanupCheckpointAuthorityTests*)|(ReapedProcStatIdentityTests*)|(TempRootSweepAnchoredDeleteTests*)|(UnsafeDeleteRecoveryTests*)|(CheckpointReceiptSafetyTests*)|(CheckpointShadowCustodyTests*)/* executed=159 passed=159 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=131.082s wall=131.569s
CHECKPOINT CP-6 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/EvidenceFolderTests/(red_run_*)|(rerun_text_*)|(green_run_*)|(non_image_*)|(host_and_git_*)|(git_snapshot_*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=11.716s wall=12.035s
CHECKPOINT CP-7 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/CardFilePrivacyGitTests/(Managed_*)|(Unmerged_*)|(Subdirectory_*)|(Git_operation_*)|(Literal_NUL_*)|(AutoCommit_*)|(Index_only_*)|(Changed_generated_*)|(Tracked_stale_*)|(Containment_*) executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=42.164s wall=42.752s
CHECKPOINT CP-8 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/RcBridgeProbePlatformTests/(Linux_probe_*)|(Forced_*)|(Docker_*)|(Agent_*)|(Session_*)|(Health_*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=0s executed=17.818s wall=18.560s
CHECKPOINT CP-10 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/(CheckpointAdmissionContractTests*)|(CheckpointImportTests*)|(DerivedImpactIndexTests*)|(IncrementalSelectionSoundnessTests*)|(LandGeneratedArtifactTests*)|(OuterRowBuildStampTests*)|(PhaseJournalKillDiagnosticTests*)|(RcInputManifestTests*)|(RcInputOwnersMapTests*)|(ReleaseImpactEnvelopeTests*)/* executed=141 passed=141 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=345.853s wall=347.040s
CHECKPOINT CP-11 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/(RowRunnerTests*)|(WaitCommandTests*)/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-11/run.trx slot=granted waited=60s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=60s executed=12.765s wall=73.512s
CHECKPOINT CP-12 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=reused filter=/*/*/RunCheckpointSourceScriptTests*/* executed=25 passed=25 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-12/run.trx slot=granted waited=15s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-12 waited=15s executed=171.204s wall=186.662s
CHECKPOINT CP-13 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-13/run.trx slot=granted waited=90s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=90s executed=9.445s wall=100.205s
CHECKPOINT CP-14 commit=32e16c27c492d7604b6b6d773d4d949f7eed64a9 build=ok filter=/*/*/ProcessSpawnLimitTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-06/.antiphon/checkpoints/20261123-010457-6935/rows/CP-14/run.trx slot=granted waited=30s dirty=0 source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-14 waited=30s executed=5.577s wall=36.110s
unlisted: none (the tool ran no other build or test command)
wall: 31m05s  sequential-equivalent: 18m36s  builds: 3  max-concurrent-builds: 1  rows: 14 green 0 red 0 skipped
outputs: deleted bin-c2052-r2/, bin-c2052-r2-pty/, bin-c2052-r2-sr/
verdict: GREEN exit=0
2026-11-23T01:36:18.8715906+00:00 FINALIZATION phase=output-cleanup seconds=100.464
```

## Receipt validation

- scripts/validate-checkpoint-receipt.ps1 -Evidence report.json -ExpectedSourceSha 32e16c27c...: CHECKPOINT SOURCE VALID source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 rows=14 (exit 0)
- Antiphon.Checkpoints validate --evidence report.json --expected-source-sha 32e16c27c...: CHECKPOINT SOURCE VALID source=32e16c27c492d7604b6b6d773d4d949f7eed64a9 rows=14 (exit 0)

## Other

- evidence-diff (build-slot): EVIDENCE result commits=15 entries=1 violations=0 base=ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91 head=32e16c27c492d7604b6b6d773d4d949f7eed64a9
- merge-tree vs origin/master 6c7a56fe154b934f1d4550f165f7b863521fd522: clean, tree 8cd882ee4614ef556e2dbf33c0ec2503e67c75dc
- derived impact index regenerated (build-slot): changed, one line (OutputCleanupTests now depends on ForwardingNativeIo's file); committed.
