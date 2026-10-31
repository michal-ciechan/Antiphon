# Native Windows rerun of qualification run d47848cc failures (task e960ffeb)

Outcome: of the 58 failed results in d47848cc, **54 now pass (FIXED)** and **4 still fail**:
3 with the same cause as before and 1 with a new cause that the old failure was hiding.

- Subject: rc tip `fc064a68247b0186918dfb1f04465e6be0475389` (origin/rc/2026-10-27). Test commits on
  the task branch differ from it only by this report file (`git diff --name-only` = this file).
  Host: desktop Windows 10 Pro 19045, .NET 9.0.22, TUnit 1.44.
- Old run: d47848cc (candidate rc cde8def41), 22,780 results, 58 failed (31 causes).
- Precondition, checked 2026-10-31 ~10:45Z: old supervisor PID 283840 and child PID 317412 are not
  running, `commission-exit.txt` = 1, and `C:\Antiphon\verification\native-run.lock` is absent. That
  path is the run's `-CoordinationRoot` per its `launch-rc.ps1`, so the native lock was released.
  Under `C:\Antiphon\releases` I only read the old TRX files (`...\logs\2026-10-30-0824-d47848cc\*.trx`)
  and the launch and heartbeat files. Nothing there was changed.
- Method: each project ran through `scripts/run-checkpoint.ps1` rows (build slot granted,
  `waited=0s` each). There was one build per project. Later Antiphon.Tests and SessionRunner rows
  used `-NoBuild` on the same stamped output. Each row ran inside the release-gate shared native
  lock (`Enter-ReleaseGateNativeLock`, CoordinationRoot `C:\Antiphon\verification`, RunId
  `native-rerun-e960ffeb`, lane `rc`). The lock was released after every row (verified absent).
  Filters are method-level ORs `/*/*/*/(Method*)|...`, with one `-Expect` token per method.
  Builds used the Windows default arguments, so the normal native apphosts were present (the
  qualification and the owed C1022/C1614 commands require them). `UseAppHost=false` was not used.
  E2E served a freshly built `client/dist` (`npm ci` + `npm run build` under a build slot).
- Note on two `-NoBuild` rows (`e960-tests-ckpt`, `e960-tests-scripts`): the build-source receipt
  names the commit before this report was updated. `e960-tests-ckpt` therefore exits 2
  (`checkpoint-build-source.json receipt-missing-or-mismatching`) even though its 9 tests passed.
  The only change since the build (d775d8ad) is this file.

## Rows run

| Row | Project | Build | Executed | Passed | Failed | Row exit | Evidence (worktree-relative) |
|---|---|---|---|---|---|---|---|
| e960-ptyhost | Antiphon.PtyHost.Tests | ok (41 s) | 2 | 2 | 0 | 0 | `.antiphon/checkpoints/e960-ptyhost-20261031-104813-b9d5/run.trx` |
| e960-sessionrunner | Antiphon.SessionRunner.Tests | ok | 2 | 1 | 1 | 1 | `.antiphon/checkpoints/e960-sessionrunner-20261031-105121-70f1/run.trx` |
| e960-sessionrunner-r2 (whole `C1022BackendLaunchTests` class, owed-command shape) | Antiphon.SessionRunner.Tests | reused | 9 | 8 | 1 | 1 | `.antiphon/checkpoints/e960-sessionrunner-r2-20261031-105537-80fa/run.trx` |
| e960-agentspty | Antiphon.Agents.Pty.Tests | ok | 1 | 1 | 0 | 0 | `.antiphon/checkpoints/e960-agentspty-20261031-105650-f90a/run.trx` |
| e960-tests-app | Antiphon.Tests | ok (12 m 34 s) | 22 | 22 | 0 | 0 | `.antiphon/checkpoints/e960-tests-app-20261031-105948-0330/run.trx` |
| e960-tests-ckpt | Antiphon.Tests | reused | 9 | 9 | 0 | 2 (receipt note above) | `.antiphon/checkpoints/e960-tests-ckpt-20261031-111822-c80b/run.trx` |
| e960-tests-scripts | Antiphon.Tests | reused | 16 | 13 | 3 | 1 | `.antiphon/checkpoints/e960-tests-scripts-20261031-112009-fb8b/run.trx` |
| e960-e2e | Antiphon.E2E | ok (3 m 58 s) | 13 | 13 | 0 | 0 | `.antiphon/checkpoints/e960-e2e-20261031-112755-860e/run.trx` (15 m 49 s) |

## Results per failed test

| Test | Project (old chunk) | Before (d47848cc) | After (fc064a68) | Status |
|---|---|---|---|---|
| RemoteControlRecoveryPtyIntegrationTests.C514_Health_request_reaches_busy_and_idle_recipients | Antiphon.Tests (g48) | FAIL | PASS 10.3 s | FIXED |
| LandRestartReplayTests.C1379_OldHeldReplaysBeforeConcurrentNewRequest | Antiphon.Tests (g79) | FAIL | PASS 50.8 s | FIXED |
| AgentReportStoreBudgetTests, all 20 failed cases (Artifact_preservation x2, Budget_diagnostics x4, Caller_cancellation x4, Canceled_fast_path x2, Concurrent_stores, Gate_wait, Io_deadline x2, Preparation_timeout, Unrelated_cancellation x2, Write_stall) | Antiphon.Tests (g80) | FAIL x20 | PASS x20 (each ≤4.0 s) | FIXED |
| PlanCoverageCensusTests x6 (census_compares_resolved_identities_not_counts, census_public_cli_is_deterministic_and_read_only, census_rejects_unselected_or_non_test_roster_methods, census_reports_unmapped_selection_without_dynamic_discovery, checklist_flag_rejects_invalid_or_conflicting_input, opt_out_preserves_legacy_reports_and_declared_counts), RunSchedulerTests.builds_never_overlap_even_with_free_slots_and_width_two, CheckpointRepeatHostTests.native_failure_in_middle_ordinal_survives_later_passes | Antiphon.Tests (g104) | FAIL x8, cause unexplained | PASS x8 | FIXED. The old cause did not recur, and because nothing failed, the new census/TRX/scheduler diagnostics produced no failure evidence. |
| CheckpointGitEncodingScriptTests.C1608_invalid_utf8_git_output_is_refused | Antiphon.Tests (g105) | FAIL | PASS | FIXED |
| RepositoryMutationLeaseTests.C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion | Antiphon.Tests (g110) | FAIL 5.0 s | FAIL 16.6 s | **STILL FAILING, same cause** |
| LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal | Antiphon.Tests (g111) | FAIL 11.0 s | FAIL 15.1 s | **STILL FAILING, same cause** |
| ReleaseGatePublicationTests.C599_AssetRecovery | Antiphon.Tests (g120) | FAIL | PASS 43.2 s | FIXED |
| QualificationCheckoutSourceTests.C885_MarkedAutocrlfClone_stays_checkpoint_clean | Antiphon.Tests (g124) | FAIL | PASS | FIXED |
| VerifyDindRunnerScriptTests.Containment_residue_counts_subdirectories_only (2 cases) | Antiphon.Tests (g124) | FAIL | PASS | FIXED |
| PosixHarnessToolTests.Cutover_w1_completes_without_chmod_on_path | Antiphon.Tests (g124) | FAIL | PASS 11.6 s | FIXED |
| PosixHarnessToolTests.Snapshot_cases_complete_without_chmod_or_stat_on_path | Antiphon.Tests (g124) | FAIL | PASS 10.4 s | FIXED |
| NativeLinuxPolicyWitnessMutationTests.Lookalike_attribute_identity_is_rejected / Marker_pin_and_origin_are_enforced / New_exclusion_without_marker_fails | Antiphon.Tests (g124) | FAIL x3 | PASS x3 | FIXED |
| PosixHarnessToolTests.Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path | Antiphon.Tests (g124) | FAIL 1.2 s (fixture symlink collision) | FAIL 77.1 s (`/bin/sh` seam) | **NEW FAILURE** (the old cause is fixed; this one was hidden behind it) |
| DirectoryLinkFixtureWindowsTests.C1061_Junction_command_joins_live_root_and_pipe_holders | Antiphon.Tests (g126) | FAIL | PASS 16.8 s | FIXED |
| NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute | Antiphon.Tests (g128) | FAIL | PASS | FIXED |
| RemoteScriptContractTests.C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids | Antiphon.Tests (profile-1716) | FAIL | PASS 48.9 s | FIXED |
| RemoteScriptContractTests.Custody_residue_counts_subdirectories_only | Antiphon.Tests (profile-1762) | FAIL | PASS | FIXED |
| AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch1(landed) | Antiphon.E2E | FAIL (receipt stall) | PASS 69 s (sibling `already-present` 60 s) | FIXED |
| AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch2(residue-cleanup) | Antiphon.E2E | FAIL | PASS 80 s (sibling `preoperation-refusal` 40 s) | FIXED |
| AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix_Batch3(operation-refusal) | Antiphon.E2E | FAIL | PASS 60 s (sibling `conflict` 51 s) | FIXED |
| AgentTaskLandDeliveryE2ETests.C467_V30_ReceiptSaveFailureNeverRetypes(receipt) | Antiphon.E2E | FAIL | PASS 133 s (sibling `verdict` 103 s) | FIXED |
| AgentTaskLandDeliveryE2ETests.C488_ApprovalPollingCannotConfirm | Antiphon.E2E | FAIL | PASS 77 s | FIXED |
| AgentTaskLandDeliveryE2ETests.C488_ApprovalQueueInsertCrashReusesRow | Antiphon.E2E | FAIL | PASS 109 s | FIXED |
| WorktreeRetirementDeliveryE2ETests.C459_AdmittedWorkspaceInputReachesRecipient(busy-then-released) | Antiphon.E2E | FAIL | PASS 67 s (siblings `idle` 67 s, `claim-first` 32 s) | FIXED |
| HostCustodyTests.C1614_Windows_tracked_linger_releases_job | PtyHost.Tests (pty-host g1) | FAIL | PASS | FIXED |
| NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute | PtyHost.Tests (pty-host g2) | FAIL | PASS | FIXED |
| NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute | SessionRunner.Tests (session-runner g6) | FAIL | PASS | FIXED |
| NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute | Agents.Pty.Tests (agents-pty g3) | FAIL | PASS 0.8 s | FIXED |
| C1022BackendLaunchTests.Configured_owned_host_runs_modern | SessionRunner.Tests (session-runner g4) | FAIL 1.6 s | FAIL 4.9 s, 2 of 2 runs | **STILL FAILING, same cause** |

The C467/C488/C459 land-delivery stalls did not recur, so the new receipt-timeout diagnostics had
no failure to describe and nothing was captured from them.

## Still failing: detail

### C1022BackendLaunchTests.Configured_owned_host_runs_modern (SessionRunner.Tests)

- Before: `UnauthorizedAccessException: Access to the path 'Antiphon.PtyHost.exe' is denied.` at
  `FileSystem.RemoveDirectoryRecursive` <- `C1022BackendLaunchTests.LaunchAsync` (C1022BackendLaunchTests.cs:126).
- After (both runs): `UnauthorizedAccessException: Access to the path 'Antiphon.Agents.Pty.dll' is denied.`
  The stack runs `RemoveDirectoryRecursive` <- the `() => Directory.Delete(root, true)` lambda
  (C1022BackendLaunchTests.cs:116) <- `OwnedProcessDirectoryCleanup.RunAsync` cleanup
  (OwnedProcessDirectoryCleanup.cs:79) <- `TestOwnedPtyHost.ScopeAsync` (TestOwnedPtyHost.cs:245).
- The body completed. The host log shows `pty backend: ModernConPty (requested 'modern')`, the conpty
  hashes verified, and both capability JSON lines printed (host pid 390760, child 448268, OpenConsole
  111940, staged copy `%TEMP%\antiphon-backend-seam-<guid>\pty-hosts\bin\<stamp>\`). Every captured
  owned process exited within the 10 s join budget (no AggregateException). Only then was the delete
  refused, on a file of the staged host copy.
- About one minute later the directory was gone, and none of the three pids, or any process started
  from that directory, was alive. So the hold is transient after the owned processes exit.
- The failure is deterministic for the `Configured` case only. Its siblings
  `Unset_owned_host_runs_modern` and `Declared_modern_overrides_inherited_inbox` use the same
  `LaunchAsync` path and pass. In both versions the cause is the same: the delete runs while
  something still holds a staged host file. The C1022 repair (join the owned handles, then delete)
  does not close this. What holds the file was not established. Candidates are an uncaptured
  process in the session tree or a delayed release of the mapped image after exit.

### RepositoryMutationLeaseTests.C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion (Antiphon.Tests)

- Before: `ShouldAssertException: Directory.EnumerateFiles(Path.Combine(lease.CommonDirectory, "antiphon", "children")) should be empty but had 1 item`
  (`...\antiphon-c448-<guid>\canonical\.git\antiphon\children\<id>.json`), RepositoryMutationLeaseTests.cs:170.
- After: the same assertion, with one child record left at
  `%TEMP%\antiphon-c448-d862c08e...\canonical\.git\antiphon\children\93495fb5....json`.
  RepositoryMutationLeaseTests.cs:178, rethrown at :195, 16.6 s. The same cause: the owned-Git
  child journal record is not removed once cancellation completes.

### LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal (Antiphon.Tests)

- Before: `ShouldAssertException: JournalFiles(children) should be empty but had 1 item`, LandingGitTests.cs:590, 11.0 s.
- After: the same assertion at the same line, 15.1 s, with one record left at
  `%TEMP%\c1076-budget-repok5bz0u2u.z4p\.git\antiphon\children\ea076712....json`. The same cause: after
  the RunOptions budget kills the slow push, its child journal record is not cleared on Windows.
  This looks like the same defect as C448: a killed or cancelled owned Git child leaves its
  `.git/antiphon/children` record behind.

### PosixHarnessToolTests.Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path (Antiphon.Tests)

- Before: `IOException: Cannot create '%TEMP%\posix-hide-<id>\Microsoft.Management.Infrastructure.Native.Unmanaged.dll' because a file or directory with the same name already exists.`
  at `File.CreateSymbolicLink` <- `HiddenPath.Without` (PosixHarnessToolTests.cs:124), during fixture setup. That cause is fixed.
- After: `ShouldAssertException: result.Exit should be 0 but was 1` (PosixHarnessToolTests.cs:86), 77.1 s.
  The harness `scripts/test-qualification-timing.ps1` passes every other check and then prints
  `FAIL capture-identity-exit-0`, `FAIL capture-identity-exit-1` and `FAILED 2`. Each FAIL follows
  `Exception calling "Start" with "0" argument(s): "An error occurred trying to start process '/bin/sh' with working directory '...\tests\Antiphon.Tests\bin-e960-tests'. The system cannot find the file specified."`.
  The capture seam written by the harness sets `$psi.FileName = "/bin/sh"`
  (scripts/test-qualification-timing.ps1:1046), which cannot run on Windows. The case was added on
  2026-10-27 in 926fceb6e. In d47848cc it never ran, because the fixture failed first. This is a new
  defect: the harness case is not portable to Windows.

## Next action

1. A Debug task on rc for C448 + C1076, which look like one defect: a cancelled or killed owned Git
   child leaves `.git/antiphon/children/<id>.json` on Windows. Rerun natively with
   `/*/*/RepositoryMutationLeaseTests/C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion`
   and `/*/*/LandingGitTests/C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal`.
2. A Debug task on rc for C1022 `Configured_owned_host_runs_modern`: find what still holds the
   staged host file after the owned processes exit (only the Configured case fails), and fix the
   cleanup without loosening the assertion. Rerun natively with
   `/*/*/C1022BackendLaunchTests/*`.
3. A Debug task on rc for the qualification-timing `capture-identity` seam: make the `/bin/sh` seam
   at line 1046 Windows-capable, or gate the case to Linux with an explicit, witnessed exclusion.
   Rerun natively with
   `/*/*/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path`.
4. Start the fresh Full run after those land, or now if the operator accepts these 4 as known failures.
