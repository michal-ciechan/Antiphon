### Checkpoints

Test project `tests/Antiphon.Tests` unless stated; isolated outputs `bin-c688a/` (R1), `bin-c688b/` (R2),
`bin-c688c/` (R3), forward slash; one build per round, every other row `--no-build`; on server2
`run-checkpoint.ps1` adds `UseAppHost=false` itself. `Min` is the count of `[Test]` methods in the named
classes at `ada146ea` (reproduced with `grep -c "^\s*\[Test"`) plus the new methods, argument-expanded rows
counted per argument, minus up to 10 % on rows whose classes lose tests under the D-12 migration; a
floor, not a census.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S7 | `tests/Antiphon.Tests -> bin-c688a/` | landing-git-real | `/*/*/(LandingGitTests*)\|(LandWorkspaceTests*)/*` | V-1, V-2, V-3, R-1 | all listed, 0 failed | 28 | 10 |
| CP-2 | S1-S7 | CP-1 | landing-unit | `/*/*/(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)\|(LandingGitProfileTests*)\|(LandingRemovalPolicyControlTests*)/*` | V-14, R-1 | all listed, 0 failed | 37 | 3 |
| CP-3 | S1-S7 | CP-1 | landing-fake-protocol | `/*/*/(AgentTaskLandSourceFreshnessTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)\|(AgentTaskLandFailureDiagnosticTests*)/*` | V-9, V-10, V-11, R-1 | all listed, 0 failed | 85 | 9 |
| CP-4 | S1-S7 | CP-1 | landing-real-protocol-a | `/*/*/(AgentTaskLandPublicationTests*)\|(AgentTaskLandPreparationIdentityTests*)\|(AgentTaskLandRefusedRetryTests*)\|(AgentTaskLandRecoveryTests*)\|(AgentTaskLandIndexLockTests*)/*` | V-4, V-5, V-6, V-7, V-8, V-12, V-13, V-15, V-16, V-18, V-19, R-1 | all listed, 0 failed | 52 | 14 |
| CP-5 | S1-S7 | CP-1 | landing-real-protocol-b | `/*/*/(InterimVerificationLandGitTests*)\|(LandSourceIdentityTests*)\|(LandingSourceFreshnessTests*)\|(WorktreeRemovalAuthorityTests*)\|(AgentTaskLandStageOutcomeTests*)/*` | V-17, R-1 | all listed, 0 failed | 42 | 10 |
| CP-6 | S1-S7 | CP-1 | landing-cleanup-slow | `/*/*/(AgentTaskLandBoundaryTests*)\|(AgentTaskLandCleanupSafetyTests*)\|(WorktreeLandingCleanupRetryTests*)\|(AgentTaskLandRemovalMatrixTests*)\|(AgentTaskLandCheckpointMatrixTests*)\|(AgentTaskLandIdentityMatrixTests*)/*` | R-1 (cleanup contract, D-6) | all listed, 0 failed | 36 | 10 |
| CP-7 | S1-S7 | CP-1 | worktree-tooling | `/*/*/(WorktreeManagerTests*)\|(WorktreeManagerSafetyTests*)\|(WorktreeManagerGitIntegrationTests*)\|(PostLandMutationWorktreeTests*)\|(AgentTaskLandVerifierTests*)/*` | V-1(e) companions, R-1 | all listed, 0 failed | 60 | 8 |
| CP-8 | S1-S7 | `tests/Antiphon.E2E -> bin-c688e/` | land-delivery-e2e | `/*/*/AgentTaskLandDeliveryE2ETests/*` | R-1 (real server child, isolated runner per docs/testing-and-build.md) | all listed, 0 failed | 23 | 25 |
| CP-9 | S8-S9 | `tests/Antiphon.Tests -> bin-c688b/` | verification-unit | `/*/*/(LandVerificationInputsTests*)\|(AgentTaskLandingStateTests*)\|(ControlledLandingGitTests*)/*` | V-20, V-21, R-2 | all listed, 0 failed | 32 | 5 |
| CP-10 | S8-S9 | CP-9 | verification-skip-protocol | `/*/*/(AgentTaskLandVerificationSkipTests*)\|(AgentTaskLandPublicationTests*)\|(AgentTaskLandPreparationIdentityTests*)\|(AgentTaskLandRefusedRetryTests*)\|(InterimVerificationLandGitTests*)\|(LandingGitTests*)/*` | V-22, V-23, R-2 | all listed, 0 failed | 64 | 14 |
| CP-11 | S8-S9 | n/a | docs-named | `git grep -n -e "LandNonBuildableGlobs" -e "target_checked_out_elsewhere" -e "canonical=" -- AGENTS.md docs/orchestration-loop.md docs/bootstrap.md docs/testing-and-build.md docs/logs.md` | S9 | ≥ 6 matching lines across all five files, exit 0 | n/a | 1 |
| CP-12 | S10 | `tests/Antiphon.Tests -> bin-c688c/` | artifacts | `/*/*/(LandVerificationArtifactsTests*)\|(AgentTaskLandVerifierTests*)\|(AgentTaskLandPublicationTests*)/*` | V-24, V-25, R-3 | all listed, 0 failed | 27 | 8 |
| CP-13 | S10 | n/a | docs-artifacts | `git grep -n "land/<hash>-artifacts\|LandVerificationArtifacts" -- docs/testing-and-build.md` | S10 docs | ≥ 1 matching line, exit 0 | n/a | 1 |

