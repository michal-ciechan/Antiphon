# CARD-1722: session-runner Git test timeout budgets

Code owner / landing owner: ab37b2cf. Task base: 1038d07e66a54b630a1bc1d394923bc7049b73dd.
Test-only repair for Windows rc run 472c7495: ColdResetRemovesDeletedProjectResidue hit its 10s test bound at 10.14s. No product changes, assertion changes, internal deadline changes or policy JSON changes.

## Inventory and implementation

Scan every explicit Timeout of at most 30 seconds in tests/Antiphon.SessionRunner.Tests, following actual fixture constructors and Git helpers. Git-spawning inventory: six non-custody C0824WorkspaceSlot classes, C0824RunnerBranchRefsTests, and four C1677Retention classes. RunnerWorkspace and PhoneHome classes without such explicit limits need no change. TempSweep/TempFamilyRegistry, Card1636/Card1653, Pty leak cleanup and live Herdr cases do not construct Git repositories in their short-timeout cases and retain their limits.

S1: commit a metadata witness and independent full-method census before modifying budgets. The witness reads each actual method/class TimeoutAttribute and requires max(60s, 2*recorded p95). At base every Git budget row must fail its actual budget assertion. The independent census refuses missing, duplicate or extra method rows. Pure retention methods have explicit preservation assertions.

S2: apply the measured Git-only budgets, retaining assembly integer-millisecond attribute style. All-Git classes get class-level attributes; mixed classes retain their original class timeout and receive method overrides only where scratch Git is created. Existing custody 60s budget and assertions remain unchanged.

## Performance budget

Linux .NET 9 / pinned SDK 10.0.204, native default output and UseAppHost=false; TEMP=/tmp. Baseline build is separately gated, then one gated batch of fresh-host full-class timing samples. The brief requests ten samples, but the standing repeat-proof cap allows at most five without a cited Review flake exception. Collect five and disclose the missing five; never call this ten. The Windows rc incident is not a Linux reproduced timeout. Empirical nearest-rank p95 at n=5 equals max, provisional for Windows; collect Windows timings on the next CARD-1722 rc qualification.

Budget formula: max(60s commissioned Git startup floor, ceil(2*p95 seconds)). The 60s floor is an explicit CARD-1722 exception to Linux 2*p95 due to the repeated loaded Windows process-startup incident; no claim that subsecond Linux samples alone establish the Windows percentile. The timing table below is the measurement reference. Fresh repositories are necessary subjects of these tests (reset, retention, branch deletion); setup cannot be shared without removing the state transition under test. No containers, DB or web hosts are needed for the affected integration classes. Git/IO and fixture preparation/body/teardown are included in each TRX duration; individual phases remain unknown, unattributed remainder is not zero. Each sample records source, UTC, load and exact membership; admission and build are separate logs.

Checkpoint estimates are provisional cold build plus retained baseline costs; Unit has no comparable measured p95. Default finite slot admission is 45 minutes, execution deadlines derive from selected row estimates; no full-assembly run is planned. Unit is required cross-cutting metadata/policy coverage and does not prove delivery, landing, leases or persistence. Whole affected classes prove their existing assertions survive the metadata-only repair. No selection is expected to run over 30 minutes; a selection that does requires caller-commissioned Debug, not a timeout increase.

## Verification design

- V-1: exhaustive short-timeout source inventory and bounded timing batch; preserve per-case min/median/p95/max and before/after budgets. Requested extra samples stay disclosed, not passed.
- V-2: actual Timeout metadata enforces the Git floor and measured p95 for every inventoried method; baseline red and fixed green.
- V-3: independent census covers every test method in the eleven affected classes except explicitly named pure methods; pure budgets are preserved.
- R-1: full eleven affected integration classes plus unchanged custody, including cold-reset deleted-project residue and native Linux filesystem/process cases.
- R-2: whole SessionRunner Unit lane including both timeout witnesses and linked classification/native-Linux guards.
- R-3: whole Antiphon.Tests Unit lane.
- R-4: full MechanicalPcContractTests, TaskPlatformGuidanceTests, TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests, SlowTestTripwireTests, NativeLinuxPolicyWitnessTests in Antiphon.Tests.
- R-5: full task-range evidence diff, OS witness inventory, clean receipt validation, fetched-master merge-tree and remote publication check. No changed OS-selected behavior or startup impact; restart: none. Native Windows qualification/measurements remain pending rc work, not a Linux pass.

### Checkpoints

Every invocation selects at most three rows. S1 runs CP-1 and expects genuine budget assertion red; S2 runs CP-2/CP-3 then CP-4/CP-5, committed HEAD bound explicitly. Build reuse only within a committed slice group. TEMP=/tmp is inherited by tool and drivers.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-ab37-red/ | red-witness | /*/*/C1722GitTimeoutBudgetTests*/* | V-2, V-3 | all listed, 0 failed | 1 | 4 | true | TEMP=/tmp |
| CP-2 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-ab37-green/ | affected | /*/*/(C0824RunnerBranchRefsTests*)\|(C0824WorkspaceSlotColdResetTests*)\|(C0824WorkspaceSlotNativeRehomeTests*)\|(C0824WorkspaceSlotOverflowTests*)\|(C0824WorkspaceSlotProcessScanTests*)\|(C0824WorkspaceSlotRehomeTests*)\|(C0824WorkspaceSlotResetTests*)\|(C1677RetentionDispatchTests*)\|(C1677RetentionEngineTests*)\|(C1677RetentionInventoryTests*)\|(C1677RetentionNativeProcessTests*)\|(C0824WorkspaceSlotCustodyTests*)/* | R-1 | all listed, 0 failed | 198 | 5 | true | TEMP=/tmp |
| CP-3 | S2 | CP-2 | runner-unit | /*/*/*/*[Category=Unit] | V-2, V-3, R-2 | >= 1 executed, 0 failed | 1 | 3 | true | TEMP=/tmp |
| CP-4 | S2 | tests/Antiphon.Tests -> bin-ab37-policy/ | unit | /*/*/*/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 15 | true | TEMP=/tmp |
| CP-5 | S2 | CP-4 | required-guards | /*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/* | R-4 | all listed, 0 failed | 7 | 3 | true | TEMP=/tmp |

## Mutation handoff

All PC-1 variants (eleven class/method budget sites) remain pending for method-scoped SourceLanding Mutation. Candidate path: docs/superpowers/mutations/2026-11-06-ab37b2cf-git-timeout-candidates.md. Mutation owns deliberate lower-budget mutants, red/restore/green and missing-control discovery. Code does not run mutants. Next ordinary stage: Review when implementation and ordinary verification complete; no direct land.

## Measured cases

| Class / case | n | min s | median s | p95 s | max s | before / after s |
|---|---:|---:|---:|---:|---:|---|
| C0824RunnerBranchRefsTests / C0824_absent_refs_are_idempotent_success | 5 | 0.087 | 0.135 | 0.164 | 0.164 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_ancestor_tip_is_deleted | 5 | 0.105 | 0.112 | 0.161 | 0.161 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_branch_with_trailing_newline_refused | 5 | 0.062 | 0.106 | 0.146 | 0.146 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_checked_out_branch_refuses_and_keeps_refs | 5 | 0.081 | 0.144 | 0.216 | 0.216 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_deletes_local_and_tracking_refs_at_expected_sha | 5 | 0.113 | 0.122 | 0.152 | 0.152 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_missing_expected_object_refuses | 5 | 0.055 | 0.094 | 0.136 | 0.136 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_missing_repository_refuses_unavailable | 5 | 0.096 | 0.123 | 0.143 | 0.143 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_non_delegate_branch_refused(feat/other) | 5 | 0.070 | 0.121 | 1.602 | 1.602 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_non_delegate_branch_refused(master) | 5 | 0.052 | 0.092 | 0.316 | 0.316 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_non_delegate_branch_refused(rc/2026-11-01) | 5 | 0.072 | 0.101 | 0.181 | 0.181 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_non_repository_directory_refuses_unavailable | 5 | 0.080 | 0.103 | 0.141 | 0.141 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_ref_moved_between_read_and_delete_refuses | 5 | 0.071 | 0.130 | 0.156 | 0.156 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_unpublished_local_tip_refuses(local-branch-ahead) | 5 | 0.093 | 0.204 | 0.522 | 0.522 | 30 / 60 |
| C0824RunnerBranchRefsTests / C0824_unpublished_local_tip_refuses(tracking-moved) | 5 | 0.063 | 0.125 | 0.157 | 0.157 | 30 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ColdLeaseWipesNativeOutput | 5 | 0.284 | 0.511 | 2.063 | 2.063 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ColdRequestOnLeasedSlotPendsUntilRelease(operator) | 5 | 0.239 | 0.470 | 0.831 | 0.831 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ColdRequestOnLeasedSlotPendsUntilRelease(schedule) | 5 | 0.292 | 0.489 | 1.096 | 1.096 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ColdResetQuarantinesWhatALeaseWould(ignored-evidence) | 5 | 0.309 | 0.356 | 0.788 | 0.788 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ColdResetQuarantinesWhatALeaseWould(reflog-only-commit) | 5 | 0.415 | 0.474 | 0.648 | 0.648 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ColdResetQuarantinesWhatALeaseWould(untracked) | 5 | 0.433 | 0.590 | 1.319 | 1.319 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ColdResetRemovesDeletedProjectResidue | 5 | 0.528 | 0.599 | 0.880 | 0.880 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_LeaseWithoutColdKeepsNative | 5 | 0.373 | 0.478 | 0.621 | 0.621 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_MissedBoundaryRunsOnceAtStart | 5 | 0.156 | 0.260 | 0.326 | 0.326 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_OperatorColdResetFreeSlotWipes | 5 | 0.324 | 0.417 | 0.684 | 0.684 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_ScheduleOffMarksNone | 5 | 0.161 | 0.249 | 0.336 | 0.336 | 10 / 60 |
| C0824WorkspaceSlotColdResetTests / C0824_WeeklyBoundaryMarksOnce | 5 | 0.463 | 0.623 | 1.999 | 1.999 | 10 / 60 |
| C0824WorkspaceSlotNativeRehomeTests / C0824_Real0555DirectoryRehomes | 5 | 0.282 | 0.503 | 0.698 | 0.698 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_AllLeasedGrowsOverflowSlot | 5 | 0.409 | 0.687 | 0.890 | 0.890 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_DiskBudgetRefuses(free_floor) | 5 | 0.403 | 0.468 | 1.003 | 1.003 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_DiskBudgetRefuses(pool_budget_with_quarantine) | 5 | 0.582 | 0.649 | 3.298 | 3.298 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_IdleOverflowRetiredGuarded | 5 | 0.757 | 0.867 | 2.151 | 2.151 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_IdleOverflowWithLateWorkQuarantinedNotRemoved(reflog-only) | 5 | 0.575 | 0.740 | 1.279 | 1.279 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_IdleOverflowWithLateWorkQuarantinedNotRemoved(unpushed-head) | 5 | 0.627 | 0.755 | 1.142 | 1.142 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_IdleOverflowWithLateWorkQuarantinedNotRemoved(untracked) | 5 | 0.661 | 0.988 | 1.097 | 1.097 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_LeasedRecentOrQuarantinedNeverRetired | 5 | 0.699 | 1.197 | 2.184 | 2.184 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_OverflowCapRefuses | 5 | 0.579 | 0.701 | 1.220 | 1.220 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_ReleasingSlotWaitsBeforeGrowing(completes) | 5 | 0.411 | 0.915 | 1.152 | 1.152 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_ReleasingSlotWaitsBeforeGrowing(expires) | 5 | 0.676 | 0.934 | 1.391 | 1.391 | 10 / 60 |
| C0824WorkspaceSlotOverflowTests / C0824_SlotNumberNeverReusedWhileDirectoryExists | 5 | 1.011 | 1.389 | 2.845 | 2.845 | 10 / 60 |
| C0824WorkspaceSlotProcessScanTests / C0824_CwdHolderDefersThenQuarantines | 5 | 1.054 | 1.093 | 1.468 | 1.468 | 10 / 60 |
| C0824WorkspaceSlotProcessScanTests / C0824_HolderExitsBeforeRetryReleasesNormally | 5 | 0.744 | 1.092 | 3.253 | 3.253 | 10 / 60 |
| C0824WorkspaceSlotProcessScanTests / C0824_OpenFdHolderDefersThenQuarantines | 5 | 0.982 | 1.236 | 4.108 | 4.108 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_IdentityBrokenDirectoryRehomes | 5 | 0.316 | 0.409 | 1.006 | 1.006 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_InjectedFsFailureQuarantinesAndRehomes(EACCES) | 5 | 0.351 | 0.426 | 0.854 | 0.854 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_InjectedFsFailureQuarantinesAndRehomes(EBUSY) | 5 | 0.292 | 0.389 | 2.665 | 2.665 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_InjectedFsFailureQuarantinesAndRehomes(ENAMETOOLONG) | 5 | 0.308 | 0.420 | 0.747 | 0.747 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_QuarantinedDirectoryBoundHoldsPool | 5 | 1.099 | 1.275 | 2.523 | 2.523 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_QuarantineReleaseDispositions(AcceptLoss) | 5 | 0.236 | 0.377 | 0.513 | 0.513 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_QuarantineReleaseDispositions(Pushed) | 5 | 0.359 | 0.370 | 0.581 | 0.581 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_QuarantineReleaseDispositions(Removed) | 5 | 0.252 | 0.518 | 0.627 | 0.627 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_QuarantineReleaseUnknownDispositionRefused() | 5 | 0.246 | 0.459 | 1.540 | 1.540 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_QuarantineReleaseUnknownDispositionRefused(Bogus) | 5 | 0.268 | 0.447 | 0.472 | 0.472 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_QuarantineReleaseUnknownDispositionRefused(pushed) | 5 | 0.255 | 0.424 | 1.452 | 1.452 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_RehomeLoopGuardRetiresSlot | 5 | 1.021 | 1.048 | 1.727 | 1.727 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit(primary) | 5 | 0.149 | 0.275 | 1.372 | 1.372 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit(sibling) | 5 | 0.219 | 0.337 | 0.912 | 0.912 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit(symlink) | 5 | 0.311 | 0.509 | 0.753 | 0.753 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_ReplacementFailureMarksOnlySlotFailed | 5 | 0.340 | 0.491 | 0.774 | 0.774 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_SharingViolationViaFileSystemSeam | 5 | 0.207 | 0.240 | 0.449 | 0.449 | 10 / 60 |
| C0824WorkspaceSlotRehomeTests / C0824_UnpushedCheckFailureQuarantinesNotResets | 5 | 0.188 | 0.284 | 0.597 | 0.597 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_EvidenceRetainedBeforeWipe | 5 | 0.248 | 0.475 | 0.592 | 0.592 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_FlagOffRunnerKeepsTaskMirror | 5 | 0.159 | 0.274 | 1.018 | 1.018 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_FreeSlotDetachedAtPublishedShaWithNoTaskRefs | 5 | 0.214 | 0.511 | 0.529 | 0.529 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_LeaseResetsToExactShaFromUnrelatedState | 5 | 0.312 | 0.477 | 0.842 | 0.842 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_PoolBoundedAcrossSequentialAndConcurrentLeases | 5 | 1.999 | 2.682 | 5.199 | 5.199 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_ResetNeverTouchesOutsideSlot | 5 | 0.365 | 0.653 | 0.737 | 0.737 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_ReviewLeaseIsExactTreeNeverSubjectBranch | 5 | 0.220 | 0.369 | 0.450 | 0.450 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_VerifyQuarantinesSilentCheckout | 5 | 0.314 | 0.726 | 1.182 | 1.182 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_WarmResetKeepsNativeOutputByteAndMtime | 5 | 0.367 | 0.644 | 0.966 | 0.966 | 10 / 60 |
| C0824WorkspaceSlotResetTests / C0824_WarmResetWipesEveryNonKeepListClass | 5 | 0.425 | 0.805 | 2.883 | 2.883 | 10 / 60 |
| C1677RetentionDispatchTests / Recent_use_by_this_runner_keeps_the_tree(live-session) | 5 | 0.305 | 0.391 | 0.637 | 0.637 | 15 / 60 |
| C1677RetentionDispatchTests / Recent_use_by_this_runner_keeps_the_tree(mirror-then-retain) | 5 | 0.213 | 0.262 | 1.542 | 1.542 | 15 / 60 |
| C1677RetentionDispatchTests / Recent_use_by_this_runner_keeps_the_tree(window-elapsed) | 5 | 0.304 | 0.413 | 0.765 | 0.765 | 15 / 60 |
| C1677RetentionEngineTests / Absent_path_is_already_absent_without_prune | 5 | 0.109 | 0.181 | 0.284 | 0.284 | 15 / 60 |
| C1677RetentionEngineTests / Appsettings_secret_survives_every_retention_boundary(admission) | 5 | 0.166 | 0.205 | 0.311 | 0.311 | 15 / 60 |
| C1677RetentionEngineTests / Appsettings_secret_survives_every_retention_boundary(locked) | 5 | 0.171 | 0.266 | 0.388 | 0.388 | 15 / 60 |
| C1677RetentionEngineTests / Appsettings_secret_survives_every_retention_boundary(phase-b) | 5 | 0.176 | 0.260 | 0.379 | 0.379 | 15 / 60 |
| C1677RetentionEngineTests / Appsettings_secret_survives_every_retention_boundary(recovery-dry) | 5 | 0.131 | 0.215 | 0.280 | 0.280 | 15 / 60 |
| C1677RetentionEngineTests / Appsettings_secret_survives_every_retention_boundary(recovery-execute) | 5 | 0.119 | 0.256 | 0.277 | 0.277 | 15 / 60 |
| C1677RetentionEngineTests / Clean_pushed_old_mirror_is_removed_without_force_or_prune | 5 | 0.282 | 0.327 | 0.513 | 0.513 | 15 / 60 |
| C1677RetentionEngineTests / Confinement_refuses_outside_scope(leaf-id-mismatch) | 5 | 0.108 | 0.206 | 0.265 | 0.265 | 15 / 60 |
| C1677RetentionEngineTests / Confinement_refuses_outside_scope(nested-under-root) | 5 | 0.114 | 0.187 | 0.249 | 0.249 | 15 / 60 |
| C1677RetentionEngineTests / Confinement_refuses_outside_scope(outside-root) | 5 | 0.134 | 0.146 | 0.258 | 0.258 | 15 / 60 |
| C1677RetentionEngineTests / Confinement_refuses_outside_scope(symlinked-leaf) | 5 | 0.104 | 0.184 | 0.242 | 0.242 | 15 / 60 |
| C1677RetentionEngineTests / Cross_device_retiring_root_moves_nothing | 5 | 0.163 | 0.264 | 0.363 | 0.363 | 15 / 60 |
| C1677RetentionEngineTests / Dirty_tree_is_kept(non-ignored-nested-repo) | 5 | 0.153 | 0.182 | 0.287 | 0.287 | 15 / 60 |
| C1677RetentionEngineTests / Dirty_tree_is_kept(status-unavailable) | 5 | 0.119 | 0.159 | 0.975 | 0.975 | 15 / 60 |
| C1677RetentionEngineTests / Dirty_tree_is_kept(tracked) | 5 | 0.137 | 0.169 | 0.223 | 0.223 | 15 / 60 |
| C1677RetentionEngineTests / Dirty_tree_is_kept(untracked-nested-file) | 5 | 0.122 | 0.154 | 1.388 | 1.388 | 15 / 60 |
| C1677RetentionEngineTests / Doubt_inside_collapsed_ignored_repository_keeps_the_tree(control-name) | 5 | 0.214 | 0.294 | 0.333 | 0.333 | 15 / 60 |
| C1677RetentionEngineTests / Doubt_inside_collapsed_ignored_repository_keeps_the_tree(link-directory) | 5 | 0.237 | 0.282 | 0.327 | 0.327 | 15 / 60 |
| C1677RetentionEngineTests / Doubt_inside_collapsed_ignored_repository_keeps_the_tree(link-file) | 5 | 0.170 | 0.308 | 0.322 | 0.322 | 15 / 60 |
| C1677RetentionEngineTests / Doubt_inside_collapsed_ignored_repository_keeps_the_tree(unreadable) | 5 | 0.248 | 0.289 | 0.358 | 0.358 | 15 / 60 |
| C1677RetentionEngineTests / Dry_run_on_a_clean_unheld_residue_removes_nothing | 5 | 0.120 | 0.185 | 0.297 | 0.297 | 15 / 60 |
| C1677RetentionEngineTests / Dry_run_reports_would_remove_and_mutates_nothing | 5 | 0.131 | 0.208 | 0.390 | 0.390 | 15 / 60 |
| C1677RetentionEngineTests / Execute_rechecks_under_the_lock_before_the_move(file-appears) | 5 | 0.136 | 0.192 | 0.655 | 0.655 | 15 / 60 |
| C1677RetentionEngineTests / Execute_rechecks_under_the_lock_before_the_move(holder-appears) | 5 | 0.153 | 0.214 | 0.722 | 0.722 | 15 / 60 |
| C1677RetentionEngineTests / Holder_keeps_the_tree(holder) | 5 | 0.140 | 0.193 | 0.305 | 0.305 | 15 / 60 |
| C1677RetentionEngineTests / Holder_keeps_the_tree(scan-unknown) | 5 | 0.137 | 0.215 | 1.144 | 1.144 | 15 / 60 |
| C1677RetentionEngineTests / Ignored_nested_repo_goes_with_the_tree | 5 | 0.233 | 0.301 | 0.430 | 0.430 | 15 / 60 |
| C1677RetentionEngineTests / Ignored_nested_repository_secret_keeps_bytes_and_registration(·antiphon/seed, ·env) | 5 | 0.232 | 0.260 | 0.430 | 0.430 | 15 / 60 |
| C1677RetentionEngineTests / Ignored_nested_repository_secret_keeps_bytes_and_registration(bin/tool, credentials/token) | 5 | 0.219 | 0.297 | 0.505 | 0.505 | 15 / 60 |
| C1677RetentionEngineTests / Ignored_secret_files_survive_admission_on_dry_run_and_execute | 5 | 0.300 | 0.482 | 0.655 | 0.655 | 15 / 60 |
| C1677RetentionEngineTests / Live_origin_must_hold_the_branch(decoy-tail-match) | 5 | 0.175 | 0.232 | 0.997 | 0.997 | 15 / 60 |
| C1677RetentionEngineTests / Live_origin_must_hold_the_branch(deleted-on-origin) | 5 | 0.147 | 0.226 | 0.793 | 0.793 | 15 / 60 |
| C1677RetentionEngineTests / Live_origin_must_hold_the_branch(origin-advanced-unknown-tip) | 5 | 0.189 | 0.205 | 0.355 | 0.355 | 15 / 60 |
| C1677RetentionEngineTests / Live_origin_must_hold_the_branch(origin-reset-behind) | 5 | 0.177 | 0.245 | 0.799 | 0.799 | 15 / 60 |
| C1677RetentionEngineTests / Live_origin_must_hold_the_branch(origin-unavailable) | 5 | 0.165 | 0.216 | 0.369 | 0.369 | 15 / 60 |
| C1677RetentionEngineTests / Ls_remote_is_bounded | 5 | 1.309 | 1.436 | 2.037 | 2.037 | 15 / 60 |
| C1677RetentionEngineTests / Move_failure_leaves_the_tree_in_place | 5 | 0.149 | 0.168 | 0.488 | 0.488 | 15 / 60 |
| C1677RetentionEngineTests / Ownership_gates_refuse(locked) | 5 | 0.164 | 0.217 | 0.332 | 0.332 | 15 / 60 |
| C1677RetentionEngineTests / Ownership_gates_refuse(removal-journal) | 5 | 0.174 | 0.233 | 0.378 | 0.378 | 15 / 60 |
| C1677RetentionEngineTests / Ownership_gates_refuse(use-owner) | 5 | 0.184 | 0.269 | 0.340 | 0.340 | 15 / 60 |
| C1677RetentionEngineTests / Phase_b_keeps_an_ignored_secret_planted_after_the_move | 5 | 0.181 | 0.244 | 0.386 | 0.386 | 15 / 60 |
| C1677RetentionEngineTests / Phase_b_refusal_keeps_the_moved_tree_registered | 5 | 0.154 | 0.189 | 0.598 | 0.598 | 15 / 60 |
| C1677RetentionEngineTests / Repository_and_branch_must_match(branch-id-not-leaf) | 5 | 0.140 | 0.180 | 0.234 | 0.234 | 15 / 60 |
| C1677RetentionEngineTests / Repository_and_branch_must_match(detached) | 5 | 0.170 | 0.201 | 0.348 | 0.348 | 15 / 60 |
| C1677RetentionEngineTests / Repository_and_branch_must_match(foreign-repository) | 5 | 0.179 | 0.217 | 0.377 | 0.377 | 15 / 60 |
| C1677RetentionEngineTests / Repository_and_branch_must_match(other-branch) | 5 | 0.145 | 0.217 | 0.429 | 0.429 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_a_legitimate_residue_beside_a_stray_entry(DryRun) | 5 | 0.149 | 0.177 | 0.293 | 0.293 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_a_legitimate_residue_beside_a_stray_entry(Execute) | 5 | 0.133 | 0.186 | 0.411 | 0.411 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_a_residue_a_live_process_holds(DryRun) | 5 | 0.114 | 0.136 | 0.281 | 0.281 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_a_residue_a_live_process_holds(Execute) | 5 | 0.119 | 0.190 | 0.434 | 0.434 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_an_ignored_secret(DryRun) | 5 | 0.126 | 0.157 | 0.264 | 0.264 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_an_ignored_secret(Execute) | 5 | 0.145 | 0.173 | 0.262 | 0.262 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_an_upper_case_hex_residue(DryRun) | 5 | 0.144 | 0.179 | 0.416 | 0.416 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_keeps_an_upper_case_hex_residue(Execute) | 5 | 0.166 | 0.185 | 0.881 | 0.881 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_never_takes_a_foreign_residue_that_sorts_first(no-own-residue) | 5 | 0.106 | 0.199 | 0.491 | 0.491 | 15 / 60 |
| C1677RetentionEngineTests / Residue_retry_never_takes_a_foreign_residue_that_sorts_first(with-own-residue) | 5 | 0.166 | 0.240 | 1.559 | 1.559 | 15 / 60 |
| C1677RetentionEngineTests / Retiring_residue_is_retried_by_phase_b_only | 5 | 0.223 | 0.237 | 0.796 | 0.796 | 15 / 60 |
| C1677RetentionEngineTests / Retiring_root_inside_the_worktree_root_is_refused | 5 | 0.108 | 0.177 | 0.280 | 0.280 | 15 / 60 |
| C1677RetentionEngineTests / Slot_paths_are_refused(ledger-path) | 5 | 0.125 | 0.195 | 0.313 | 0.313 | 15 / 60 |
| C1677RetentionEngineTests / Slot_paths_are_refused(ws-leaf) | 5 | 0.103 | 0.195 | 0.269 | 0.269 | 15 / 60 |
| C1677RetentionEngineTests / Tree_not_registered_at_its_path_is_kept(DryRun) | 5 | 0.153 | 0.187 | 0.306 | 0.306 | 15 / 60 |
| C1677RetentionEngineTests / Tree_not_registered_at_its_path_is_kept(Execute) | 5 | 0.154 | 0.191 | 0.244 | 0.244 | 15 / 60 |
| C1677RetentionEngineTests / Unavailable_ignored_list_keeps_the_tree | 5 | 0.162 | 0.198 | 0.346 | 0.346 | 15 / 60 |
| C1677RetentionEngineTests / Unpushed_commit_is_kept(local-commit) | 5 | 0.123 | 0.180 | 0.304 | 0.304 | 15 / 60 |
| C1677RetentionEngineTests / Unpushed_commit_is_kept(pushed-by-url-tracking-stale) | 5 | 0.161 | 0.222 | 0.342 | 0.342 | 15 / 60 |
| C1677RetentionEngineTests / Unreadable_slot_ledger_keeps_a_removable_mirror | 5 | 0.158 | 0.198 | 0.266 | 0.266 | 15 / 60 |
| C1677RetentionEngineTests / Unrecognised_mode_is_kept_before_any_git() | 5 | 0.142 | 0.203 | 0.462 | 0.462 | 15 / 60 |
| C1677RetentionEngineTests / Unrecognised_mode_is_kept_before_any_git(Bogus) | 5 | 0.152 | 0.184 | 0.271 | 0.271 | 15 / 60 |
| C1677RetentionEngineTests / Unrecognised_mode_is_kept_before_any_git(dryrun) | 5 | 0.160 | 0.172 | 0.242 | 0.242 | 15 / 60 |
| C1677RetentionEngineTests / Unrecognised_mode_is_kept_before_any_git(execute) | 5 | 0.148 | 0.185 | 0.327 | 0.327 | 15 / 60 |
| C1677RetentionInventoryTests / Inventory_is_read_only | 5 | 0.171 | 0.242 | 1.079 | 1.079 | 15 / 60 |
| C1677RetentionInventoryTests / Inventory_lists_root_and_retiring_entries | 5 | 0.178 | 0.189 | 0.348 | 0.348 | 15 / 60 |
| C1677RetentionInventoryTests / Unmatched_name_is_refused_in_execute | 5 | 0.062 | 0.092 | 0.366 | 0.366 | 15 / 60 |
| C1677RetentionNativeProcessTests / Cwd_holder_keeps_the_tree | 5 | 0.395 | 0.711 | 1.238 | 1.238 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_retiring_residue_is_kept(cr, Execute) | 5 | 0.110 | 0.160 | 0.841 | 0.841 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_retiring_residue_is_kept(lf, DryRun) | 5 | 0.113 | 0.173 | 0.202 | 0.202 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_retiring_residue_is_kept(lf, Execute) | 5 | 0.113 | 0.151 | 0.214 | 0.214 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_retiring_residue_is_kept(ls, Execute) | 5 | 0.115 | 0.184 | 0.217 | 0.217 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_retiring_residue_is_kept(space, DryRun) | 5 | 0.105 | 0.135 | 0.191 | 0.191 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_retiring_residue_is_kept(space, Execute) | 5 | 0.093 | 0.113 | 0.269 | 0.269 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_task_leaf_is_kept(cr) | 5 | 0.107 | 0.148 | 0.178 | 0.178 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_task_leaf_is_kept(lf) | 5 | 0.109 | 0.151 | 0.229 | 0.229 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_task_leaf_is_kept(ls) | 5 | 0.108 | 0.147 | 0.890 | 0.890 | 10 / 60 |
| C1677RetentionNativeProcessTests / End_suffixed_task_leaf_is_kept(space) | 5 | 0.106 | 0.133 | 0.302 | 0.302 | 10 / 60 |
| C1677RetentionNativeProcessTests / Open_fd_holder_keeps_the_tree | 5 | 0.403 | 0.751 | 1.034 | 1.034 | 10 / 60 |
| C1677RetentionNativeProcessTests / Tree_is_removed_after_the_holder_exits | 5 | 0.703 | 0.951 | 2.261 | 2.261 | 10 / 60 |
| C1677RetentionNativeProcessTests / Unreadable_proc_root_is_unknown | 5 | 0.241 | 0.251 | 0.343 | 0.343 | 10 / 60 |


