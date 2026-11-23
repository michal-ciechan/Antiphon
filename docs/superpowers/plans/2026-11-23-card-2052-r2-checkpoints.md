# CARD-2052 Review round 2 repair: checkpoints

Repair of Review 2ff6a0bc (round 2). Round: Final. Base `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91`; round-2
subject `aad1b18fe45761cec4667033b727c14a649b115c`. This table replaces
`2026-11-22-card-2052-r1-checkpoints.md` (and its rerun and named extracts) for this round.

D-1 (round 2): `DecisionStillHolds` accepted a decision that bound no index version (the Git fallback, or a
layout without a stamp). Now `CheckpointOutputLedger.DeleteRecorded` lets a bound decision through only while its
version still holds, and asks an unbound one again after content validation and the creation-identity lookup
(`SafeWrite.Preflight` without a scope): a tracked answer, or a check that cannot answer, keeps the entry. The
stamp now reads the index trailer last.

## Verification design

- V-1 (D-1 r2): `git_fallback_target_staged_during_its_hash_leaves_the_file_in_place`. A forwarding filesystem
  layer (`ForwardingNativeIo`, which is not the native one) sends every tracked check to real git through the real
  `CleanOwnedOutputs`. The target is staged once, when its content is opened for the hash that follows git's
  untracked answer. The target survives and the refusal names it. The two untracked owned siblings are removed, and
  zero in-process index reads take place. Red at `69508cdc7e7645b00c5d3490525bdb74fff3860d` (target unlinked),
  green at the fix `5aa936ede661dfdf1520e0dbc9fecc061b176d2b`.
- V-2: the six round-1 witnesses (D-1 r1, D-2, D-3) and the two reuse witnesses (index reads <= 10 for 46 entries)
  stay green.
- R-1: the eight classes Review ran. OutputCleanupTests runs by method prefix without its four rostered Windows
  methods, so no Linux row selects a Windows-only case and the tool's own `validate`, which refuses any skip, can
  pass.
- R-2: Final guards. `WindowsUnitPlatformAdmissionTests` runs by method prefix without
  `C1210_Windows_linux_only_outcomes_are_exact`.
- R-3: the round-1 `scripts/impacted-classes.ps1 -Range ecc9bfaa5..HEAD` selection (this round touches only files
  that round already touched). The four classes that execute zero cases on Linux (`CheckpointRecoveryWindowsTests`,
  `CheckpointWindowsAnchoredDeleteTests`, `CheckpointWindowsAnchoredPathDeleteWitnessTests`,
  `CardFilePrivacyPathTests`) move to the native Windows plan,
  `2026-11-23-card-2052-r2-checkpoints-windows.md`. `EvidenceFolderTests`, `CardFilePrivacyGitTests` and
  `RcBridgeProbePlatformTests` run on Linux without their Windows-only methods. Every Windows-only identity is in
  `WindowsUnitSkipRoster.WindowsOnly`. No migrations row.
- V-3: CP-9 builds the derived-impact driver in its native default output before CP-10, because
  `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` runs it with `-NoBuild`.

PCs stay pending for SourceLanding Mutation (mutation waived this round). Native Windows rows stay pending until a
Windows host runs them.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2052-r2/ | output-cleanup-linux | `/*/*/OutputCleanupTests/(a*)\|(b*)\|(c*)\|(d*)\|(e*)\|(f*)\|(g*)\|(h*)\|(i*)\|(j*)\|(l*)\|(m*)\|(n*)\|(o*)\|(p*)\|(r*)\|(s*)\|(t*)\|(u*)\|(wrapper_*)\|(windows_seal_docs_*)` | V-1,V-2,R-1 | 54 pass, 0 skipped | 54 | 15 | true | |
| CP-2 | all | CP-1 | named-seven | `/*/*/(CheckpointOwnedOutputTests*)\|(HostCleanupCheckpointExtractionTests*)\|(CheckpointToolCopyCleanupTests*)\|(PreflightCommandLineBudgetTests*)\|(SafeDeleteProcessBoundaryTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | R-1 | >= 100 executed, 0 failed, 0 skipped | 100 | 3 | true | |
| CP-3 | all | CP-1 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(ProcessSpawnLimitTests*)/*` | R-2 | >= 40 executed, 0 failed, 0 skipped | 40 | 8 | true | |
| CP-4 | all | CP-1 | platform-admission-linux | `/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_*)\|(C1210_Native_*)\|(C1210_Roster_*)\|(C1210_Audit_*)\|(C1210_Host_*)\|(C1226_*)` | R-2 | >= 6 executed, 0 failed, 0 skipped | 6 | 4 | true | |
| CP-5 | all | CP-1 | impacted-cleanup | `/*/*/(AnchoredPathDeleteWitnessTests*)\|(C1307ToolRemovalTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointExecutorLogTests*)\|(CheckpointLargeStampTests*)\|(CheckpointOrFilterTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointSelectedOutputTests*)\|(CheckpointStatePublicationTests*)\|(CheckpointTaskOwnershipTests*)\|(HostCleanupCheckpointAuthorityTests*)\|(ReapedProcStatIdentityTests*)\|(TempRootSweepAnchoredDeleteTests*)\|(UnsafeDeleteRecoveryTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointShadowCustodyTests*)/*` | R-3 | >= 60 executed, 0 failed, 0 skipped | 60 | 8 | true | TMPDIR=/tmp |
| CP-6 | all | CP-1 | evidence-folder-linux | `/*/*/EvidenceFolderTests/(red_run_*)\|(rerun_text_*)\|(green_run_*)\|(non_image_*)\|(host_and_git_*)\|(git_snapshot_*)` | R-3 | >= 6 executed, 0 failed, 0 skipped | 6 | 3 | true | TMPDIR=/tmp |
| CP-7 | all | CP-1 | card-privacy-git-linux | `/*/*/CardFilePrivacyGitTests/(Managed_*)\|(Unmerged_*)\|(Subdirectory_*)\|(Git_operation_*)\|(Literal_NUL_*)\|(AutoCommit_*)\|(Index_only_*)\|(Changed_generated_*)\|(Tracked_stale_*)\|(Containment_*)` | R-3 | >= 10 executed, 0 failed, 0 skipped | 10 | 3 | true | |
| CP-8 | all | CP-1 | rc-bridge-linux | `/*/*/RcBridgeProbePlatformTests/(Linux_probe_*)\|(Forced_*)\|(Docker_*)\|(Agent_*)\|(Session_*)\|(Health_*)` | R-3 | >= 6 executed, 0 failed, 0 skipped | 6 | 3 | true | |
| CP-9 | all | n/a | derived-impact-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --verbosity minimal` | V-3 | exit 0 | n/a | 5 | true | |
| CP-10 | all | CP-1 | impacted-selection | `/*/*/(CheckpointAdmissionContractTests*)\|(CheckpointImportTests*)\|(DerivedImpactIndexTests*)\|(IncrementalSelectionSoundnessTests*)\|(LandGeneratedArtifactTests*)\|(OuterRowBuildStampTests*)\|(PhaseJournalKillDiagnosticTests*)\|(RcInputManifestTests*)\|(RcInputOwnersMapTests*)\|(ReleaseImpactEnvelopeTests*)/*` | R-3,V-3 | >= 60 executed, 0 failed, 0 skipped | 60 | 12 | true | |
| CP-11 | all | CP-1 | impacted-runner | `/*/*/(RowRunnerTests*)\|(WaitCommandTests*)/*` | R-3 | >= 10 executed, 0 failed | 10 | 3 | true | |
| CP-12 | all | CP-1 | impacted-source-script | `/*/*/RunCheckpointSourceScriptTests*/*` | R-3 | >= 5 executed, 0 failed | 5 | 5 | true | |
| CP-13 | all | tests/Antiphon.Agents.Pty.Tests -> bin-c2052-r2-pty/ | pty-spawn-limit | `/*/*/ProcessSpawnLimitTests*/*` | R-2,R-3 | >= 1 executed, 0 failed | 1 | 8 | true | |
| CP-14 | all | tests/Antiphon.SessionRunner.Tests -> bin-c2052-r2-sr/ | sessionrunner-spawn-limit | `/*/*/ProcessSpawnLimitTests*/*` | R-2,R-3 | >= 1 executed, 0 failed | 1 | 8 | true | |
