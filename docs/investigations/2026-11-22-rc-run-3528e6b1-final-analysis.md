# Windows Full run 3528e6b1 final analysis

Windows Full ended with **TESTS**. Two of thirteen suites had failed chunks. The 1,291 TRX files contain 78 failed cases across 59 class/method identities. One additional chunk timed out with 37 planned results missing. The next integrated rc SHA needs a fresh Windows Full. The verified seed puts the recorded failures first.

## Source and verdict

| Field | Recorded value |
| --- | --- |
| Run and ref | `3528e6b1`, `rc/2026-11-30` |
| Candidate | `rc-2026-11-30-bd34a7ff6da9` |
| Tested SHA | `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` |
| Start / summary completion | `2026-11-21T07:35:30.0287008Z` / `2026-11-22T02:45:08.3031202Z` |
| Duration | 68,978.274 s (19 h 9 min 38 s) |
| Selection | Requested and effective `Full`, coverage scope `Full`, attempt 1, fallback reason `baseline-absent` |
| Summary verdict | `outcome=TESTS`, `succeeded=false`, `testsPassed=false`, `coverageComplete=false`, `dispatchProgress.liveness=closed` |
| Final state | `phase=final`, exit 1, `reportDelivered=true`, `completedAt=2026-11-22T02:46:24.4710757Z` |
| Heartbeat | `observedAtUtc=2026-11-22T02:46:17.8701094Z`, `hangSuspected=false` |
| Repair status checked against | Remote master `6f6a789e07f031496ae99e58ee6e563d30837a64`. Remote rc still matched the tested SHA. |

Evidence root `C:\Antiphon\releases\candidates\rc-2026-11-30-bd34a7ff6da9\logs\2026-11-21-0735-3528e6b1`. Paths in the tables are relative to this root. Read `summary.json`, 1,291 top-level TRX files and 1,294 timing sidecars. The same run ID and final state appear in `C:\Antiphon\releases\state-3528e6b1\last-run.json` and `C:\Antiphon\releases\task-3528e6b1\own-last-run.json`. The heartbeat is in the latter directory. The separate timing root is `C:\Antiphon\releases\candidates\rc-2026-11-30-bd34a7ff6da9\logs\timing-3528e6b1`.

All four build steps passed with exit 0: npm ci, npm run build, client lint and dotnet build Antiphon.sln. Parsed TRX totals were 27,139 Passed, 78 Failed and 31 NotExecuted. Each TRX file's failed counter matched its failed result rows. The suite rollup uses the runner's chunk verdicts. A passing chunk can still carry a coverage reason, as group 111 did.

## Suite outcomes

| Required and selected suite | Outcome | Passing chunks | Failed chunks | Timed out, included in failed |
| --- | --- | ---: | ---: | ---: |
| `antiphon` | FAIL | 951 | 33 | 1 |
| `session-runner` | FAIL | 131 | 1 | 0 |
| `pty-host` | PASS | 53 | 0 | 0 |
| `agents-pty` | PASS | 29 | 0 | 0 |
| `messaging` | PASS | 1 | 0 | 0 |
| `client` | PASS | 1 | 0 | 0 |
| `scripts` | PASS | 1 | 0 | 0 |
| `e2e` | PASS | 77 | 0 | 0 |
| `markdown-pdf` | PASS | 12 | 0 | 0 |
| `observability` | PASS | 1 | 0 | 0 |
| `server-git` | PASS | 1 | 0 | 0 |
| `resilience` | PASS | 1 | 0 | 0 |
| `git-in-process` | PASS | 1 | 0 | 0 |

### Summary reasons verbatim

```text
failed-row
unexecuted-outcome:Antiphon.Tests.Scripts.QualificationTimingTests.Hostile_files_keep_the_publication_inside_the_budget,Antiphon.Tests.Scripts.QualificationTimingTests.A_fifo_journal_is_never_opened
process-exit-2
missing-trx
timeout antiphon
unexecuted-outcome:Antiphon.Tests.Checkpoints.CheckpointSourceStateTests.tool_snapshot_of_a_non_utf8_name_is_unknown_not_stamped_absent
missing-expanded-row union Antiphon.Tests.Application.AgentRemoteControlGateTests.1.1.create_grok_with_remote_control_true_is_refused_and_persists_no_row.1.1.0,Antiphon.Tests.Application.AgentRemoteControlGateTests.1.1.create_grok_with_flag_omitted_stores_false.1.1.0,Antiphon.Tests.Application.AgentRemoteControlGateTests.1.1.patch_claude_rc_on_to_grok_profile_omitting_flag_is_refused_and_row_unchanged.1.1.0,Antiphon.Tests.Application.AgentRemoteControlGateTests.1.1.patch_claude_rc_on_to_grok_profile_with_flag_false_succeeds.1.1.0,Antiphon.Tests.Application.AgentRemoteControlGateTests.1.1.patch_grok_agent_with_remote_control_true_is_refused.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1213_OwnershipBothForms.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1213_OwnershipPathGuards.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_OnlyDoneAuthorizes.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_GenerationMustMatch.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_TerminalTaskRequired.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1230_TempCheckoutIsRefusedByName.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1230_ConfiguredReportRootDecidesTheTempRefusal.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_UniqueOrdinaryOwner.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_ReopenBeforeIntentRevokes.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_IntentBeforeReopenKeepsFence.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_ActiveConsumersHold.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_RecoveryDebtHolds.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_SnapshotAttemptIsFresh.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_SnapshotReportIsFresh.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_NoDbLockAcrossIo.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_SharedAndBorrowedExcluded.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_SourceLandingExcluded.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_SlotAndCanonicalExcluded.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_DiscoverPostDoneAttempts.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_ExactCardBinding.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_DuplicateDoneIsIdempotent.1.1.0,Antiphon.Tests.Application.CardDoneWorktreeCleanupTests.1.1.C1017_DoneSaveIsAtomic.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_ConfirmedNoPushBinds.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_FinalReportWins.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_AppendPreservesHistory.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_HeaderSnapshotAndGetAgree.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_ContinuingSyncFailure.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_AutomaticEligibility.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_SettlementsSerialize.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_PreparationBeforeLock.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1043_SettlementRollback.1.1.0,Antiphon.Tests.Application.ReviewEvidenceResettlementTests.1.1.C1082_PendingContinuationRefusesStrictRebind.1.1.0
```

### Summary warnings verbatim

```text
stale-roster:Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation
stale-roster:Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_extraction_is_closed
stale-roster:Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_admits_only_an_authenticated_private_origin
stale-roster:Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_file_omits_credentials
stale-roster:Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Recheck_target_refuses_a_changed_fingerprint
stale-roster:Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Valid_token_refuses_protected_names_and_keeps_host_state
stale-roster:Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.State_volume_named_in_the_plan_is_refused_before_delete
stale-roster:Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Manual_apply_refuses_until_the_operation_token_matches
stale-roster:Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Refused_apply_keeps_the_ready_preview
stale-roster:Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Scripted_apply_refuses_until_the_operation_token_matches
stale-roster:Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Resume_refuses_until_the_operation_token_matches
stale-roster:Antiphon.SessionRunner.Tests.RunnerChildClaudeTokenInheritanceTests.Claude_launch_seeds_trust_in_the_runner_config_dir
stale-roster:Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs
stale-roster:Antiphon.Tests.Application.C0824RefOnlySyncTests.Reported_tip_and_mirror_publication_keep_existing_contract
stale-roster:Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Cold_build_preserves_native_obj_and_terminal_receipt
stale-roster:Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Real_tampering_keeps_a_red_terminal_report
stale-roster:Antiphon.Tests.Application.LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B2_ReusedInodeIsANewDirectoryIdentity
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_BytesChangedDuringTheWalkAreKept
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerStopsTheWalk
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToAnUnwalkedDirectoryHolds
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToTheWalkedDirectoryHolds
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept
stale-roster:Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles
```

### Tolerated skips

The summary recorded 27 platform skips and one live-context skip.

| Test identity | Reason class |
| --- | --- |
| `Antiphon.Tests.Application.C0824RefOnlySyncTests.Reported_tip_and_mirror_publication_keep_existing_contract` | `platform` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Recheck_target_refuses_a_changed_fingerprint` | `platform` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Valid_token_refuses_protected_names_and_keeps_host_state` | `platform` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.State_volume_named_in_the_plan_is_refused_before_delete` | `platform` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Refused_apply_keeps_the_ready_preview` | `platform` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Manual_apply_refuses_until_the_operation_token_matches` | `platform` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Scripted_apply_refuses_until_the_operation_token_matches` | `platform` |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests.Resume_refuses_until_the_operation_token_matches` | `platform` |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_extraction_is_closed` | `platform` |
| `Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests.C680_LiveNudgeRefusesPermissionAndQuestionDialogs` | `live-context` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B2_ReusedInodeIsANewDirectoryIdentity` | `platform` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_BytesChangedDuringTheWalkAreKept` | `platform` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt` | `platform` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerStopsTheWalk` | `platform` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToAnUnwalkedDirectoryHolds` | `platform` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToTheWalkedDirectoryHolds` | `platform` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept` | `platform` |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles` | `platform` |
| `Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `platform` |
| `Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `platform` |
| `Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `platform` |
| `Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Cold_build_preserves_native_obj_and_terminal_receipt` | `platform` |
| `Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Cold_build_preserves_native_obj_and_terminal_receipt` | `platform` |
| `Antiphon.Tests.Checkpoints.CheckpointColdBuildTests.C1734_Real_tampering_keeps_a_red_terminal_report` | `platform` |
| `Antiphon.Tests.Application.LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet` | `platform` |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_admits_only_an_authenticated_private_origin` | `platform` |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_file_omits_credentials` | `platform` |
| `Antiphon.SessionRunner.Tests.RunnerChildClaudeTokenInheritanceTests.Claude_launch_seeds_trust_in_the_runner_config_dir` | `platform` |

## Failed chunks and coverage gaps

| Suite | Chunk | Failed TRX cases | Exit | Evidence |
| --- | --- | ---: | ---: | --- |
| `antiphon` | `all-group-26` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-39` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-group-45` | 3 | 2 | Failed TRX rows |
| `antiphon` | `all-group-46` | 3 | 2 | Failed TRX rows |
| `antiphon` | `all-group-47` | — | 1 | Watchdog timeout, no TRX |
| `antiphon` | `all-group-49` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-50` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-53` | 8 | 2 | Failed TRX rows |
| `antiphon` | `all-group-54` | 5 | 2 | Failed TRX rows |
| `antiphon` | `all-group-60` | 3 | 2 | Failed TRX rows |
| `antiphon` | `all-group-61` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-group-64` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-71` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-75` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-76` | 3 | 2 | Failed TRX rows |
| `antiphon` | `all-group-81` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-82` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-group-86` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-group-92` | 3 | 2 | Failed TRX rows |
| `antiphon` | `all-group-94` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-113` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-121` | 8 | 2 | Failed TRX rows |
| `antiphon` | `all-group-123` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-group-134` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-group-136` | 2 | 2 | Failed rows and two unadmitted skips |
| `antiphon` | `all-group-137` | 6 | 2 | Failed TRX rows |
| `antiphon` | `all-group-138` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-group-139` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-group-141` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-profile-404` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-split-12` | 1 | 2 | Failed TRX rows |
| `antiphon` | `all-split-13` | 2 | 2 | Failed TRX rows |
| `antiphon` | `all-split-15` | 4 | 2 | Failed TRX rows |
| `session-runner` | `all-group-1` | 1 | 2 | Failed TRX rows |

- **Lost results in `antiphon/all-group-47`.** Its timing record has `why=timeout`, `trxMissing=true`, `testCount=0` and 3,601.261 s elapsed. The 37 planned IDs in `groups-planned-antiphon.json` exactly equal the 37 missing IDs in the summary's expanded-row union. The affected classes are `AgentRemoteControlGateTests`, `CardDoneWorktreeCleanupTests` and `ReviewEvidenceResettlementTests`. The execution diagnostic `antiphon-all-group-47-execution/log_261121152749640.diag:1968` reports an in-progress ReviewEvidenceResettlement node. The hang capture `antiphon-all-group-47-tests.log.hang.txt:2` records `active=2`. Debug task `4699a1a9` reproduced a held redirected pipe after the Git root exited. On the tested SHA, `server/Infrastructure/Git/LandingGit.cs:362-366` waits for root exit under a budget, then waits for both pipe readers without a deadline. The Windows capture and reproduction support that mechanism. The exact test and owning process for the outstanding pipe remain unconfirmed on Windows.
- **Group 47 repair status.** Master contains test containment at `50f6f5506584a20c29dfb6975c45763237c9e627` and the held-pipe witness follow-up `cbab2558abe1cccd228b8db00503ed99b7ff8781` (source task `bb3346e1` at `08fcdf6b5dbe9c97d099b9a42a5dfde90cd6532d`). The production change remains parked on CARD-2053. Task `4699a1a9` produced a branch repair, and Review `dac03e90` found blockers in the revised product change at `8fa8148983c5d5513624081a17e227976d19891b`. Replay all three classes on Windows and retain complete TRX.
- **Timing-only gap in `antiphon/all-group-51`.** Its sidecar says `why=timing-read-refused` and `trxMissing=false`. The TRX records 182 passed, matching all 182 planned rows. Client and scripts each have a passing exit-0 suite log, phase `complete`, and the expected `no-trx:client` or `no-trx:scripts` marker. Group 47 is the only timed-out chunk and the only unexpected missing TRX.
- **Three unadmitted skips.** Group 136 skipped `QualificationTimingTests.Hostile_files_keep_the_publication_inside_the_budget` and `A_fifo_journal_is_never_opened`. Group 111 skipped `CheckpointSourceStateTests.tool_snapshot_of_a_non_utf8_name_is_unknown_not_stamped_absent`. The fixtures require POSIX FIFOs or a byte-named filesystem. The Windows coverage verdict recorded all three as unexecuted. The current source still has the guards and the method names are absent from the Windows execution-policy file. Coverage disposition remains open. Verify selection or skip admission and native counts for the relevant chunks. [Timing guards](../../tests/Antiphon.Tests/Scripts/QualificationTimingTests.cs#L442) · [Filename guard](../../tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs#L36)

## Failure dispositions

| Disposition | Failed TRX cases | Class/method identities |
| --- | ---: | ---: |
| OPEN | 9 | 8 |
| BRANCH / REVIEW | 1 | 1 |
| FIXED-ON-MASTER | 68 | 50 |
| OWED-WINDOWS-CONFIRMATION | 78 | 59 |

Windows confirmation overlaps the three repair statuses. It covers every original failed case, using the current equivalent when a fixture was renamed or split. These counts exclude group 47's lost cases, the three unadmitted skips and the separate master failures. Case counts by family are A 42, A? 2, B 19, C 15. The case table links every row to its evidence and repair disposition below.

| Code | Family and evidence | Fix status at the observed master |
| --- | --- | --- |
| A1 | Alias-schema contention. On the tested SHA the test helper repeatedly issued `ALTER TABLE "AgentTasks" ADD COLUMN IF NOT EXISTS "RequestedModelAlias"` in callers' open transactions. This takes `ACCESS EXCLUSIVE`, including when the column already exists. Debug's PostgreSQL sample recorded the granted lock and a blocked `AccessShareLock` reader. TRX and execution diagnostics show transient Npgsql errors, ~30 s command waits, two `40P01` deadlocks (g50/g81), or dispatch state fallout. Groups 39, 45, 49, 50 and 86 have independent replays or lock evidence. The remaining assignments use matching timeout/deadlock or dispatch signatures. A lock graph from the original Windows fixture database is unavailable. [Group 86](2026-11-21-why-missed-rc-run-3528e6b1-group-86.md) · [Group 49](2026-11-21-why-missed-rc-run-3528e6b1-group-49-queue-slot.md) | Master `1cdc778c13c445543e18ca4048d4806d7b1f1994` skips recurring DDL once the column is visible. `7bf8274c41775617bb89ad123a2164034e0ae80a` is task `815e48f4`'s original tip. See `tests/Antiphon.Tests/TestHelpers/RequestedModelAliasSchema.cs:103-122`. |
| A2 | Suspected DB contention and barrier failure in g61. The identity-changed case has a 30,025 ms Npgsql timeout in `antiphon-all-group-61-execution/log_261121210247950.diag:1206-1217`. The concurrent-writers case reports that both writers failed to reach the barrier in 20 s. The TRX and diagnostic do not bind either case to alias DDL. | OPEN. Replay both with the master alias change and capture the blocked command and barrier arrivals if they recur. |
| B1 | C0824 role gate g92. Count 0 or state Leased despite release reconciliation. Debug `a5d2c107` ran the affected class on Linux at rc and master (20/20 both). Windows holder-scan deferral before cleanup matches both outcomes. [Group 92](2026-11-21-why-missed-rc-run-3528e6b1-group-92-slot-role-gate.md) · [Holder scan](../../src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs#L407) | OPEN. Capture native release response and ledger, including probe completeness and defer detail. |
| B2 | C1960 g94. At `C1960QuarantineReleaseRouteTests.cs:209` the setup's dirty slot has zero quarantine entries, before the route assertion. A holder-scan deferral is plausible. The TRX lacks the pre-route release result. The registration-epoch fix from CARD-1960 predates the tested SHA and covers a different symptom. | OPEN. Capture runner settlement response and the pre-route slot/quarantine ledger in a native Debug. |
| B3 | Profile 404. The concurrent idempotence case left the checkout present. Debug's registration-move sharing-conflict hypothesis fits. The retained TRX has a directory-exists assertion without the retirement verdict. | Task `e6f4c3dd` pushed `1a48bee0aa136a051043dfad533603cd1d9a1e4f`. Review `64dec2d8` blocked the land. Windows retirement outcome and reason are owed. |
| B4 | FT-2 split 12/13/15. Cleanup tried to delete read-only Windows loose Git objects. The retained TRX stacks reach fixture disposal. [FT-2 investigation](2026-11-22-why-missed-rc-run-3528e6b1-ft2-readonly-git-objects.md) | Master `e0e747772967f8ed4e2085e79aac21c4b056fa3b` landed task `8436af52`'s repair (`2476a9e31e6945f38fb7c1b9e4b574366070b032`). |
| B5 | g134 C1199 roster. The harness row `c1199-roster-35` found 37 unique methods with no shape conflict. The two additions are portable CARD-2006 cases. | Master `20d801e8a003d7026dfc31e77d933ad39533705a`, from Debug `739fddea`. |
| B6 | g134 C1323. The offline Windows child reports `deferred-evidence-write-failed` for `timing-offline-windows/setup-phases.json` with access denied. The original trace did not capture the failing filesystem call or sharing owner. [Group 134](2026-11-21-why-missed-rc-run-3528e6b1-group-134-c1323.md) | Master `6972fca9efc9e62318bc3bbb62937c934aa03765` retries atomic rename on transient Windows sharing/access failures. The native replay must confirm coverage of this denial. |
| B7 | g137 dirty-order corpus. Read-only loose objects caused recursive scratch repo cleanup to throw. The corpus surfaced the exceptions as mismatches. [Group 137](2026-11-21-why-missed-rc-run-3528e6b1-group-137.md) | Master `eb58756d8b6482ee8830c019faad696473b896ad`, with witness `a507f34244c13bd95abbd22e6783ad0803c11726`, task `4b454b79`. |
| B8 | g39 C0824 redirect. The TRX includes `workspace_slot_release_unknown` and the task stayed on `grok-linux`. Debug `072b9661` passed the redirect case on Linux at both pins. Its other g39 case reproduced the alias lock. | OPEN. Capture the old runner's release reply, Held event and slot ledger in the Windows replay. |
| C1 | g113 OEM code-page mismatch in the fixture's Git stdout comparison. [Group 113](2026-11-21-why-missed-rc-run-3528e6b1-group-113.md) | Master `831245467f933c207b356e45f8d0a6733b9d2259` sets fixture Git stdout to UTF-8. |
| C2 | g123 project contract expected HostCleanup only. Checkpoint source identity added the direct GitInProcess reference. | Master `648ae96a88ed49c6573f85fd9771f41c0133f418` pins the isolated dependency graph. Replay `csproj_references_only_isolated_libraries` or the full contract class. |
| C3 | g136/g139 corpus loader. Windows tried the Linux PowerShell assembly path and reported load failures across shadow/differential fixtures. The differential also includes dirty-order coverage. [Group 136](2026-11-21-why-missed-rc-run-3528e6b1-group-136.md) · [Group 139](2026-11-21-why-missed-rc-run-3528e6b1-group-139.md) | Master `6c8a127e12c5f5f7dfc6ae776ac62b3cb0cc595c` repaired runtime resolution. `383b5ffffd8a0968b186f3d0ddbf875949c8ef7c` and `ee059be31acd8f0b8837afc3d6049ef6264023c0` later converted the corpus to plain values and removed the assembly shim. B7 covers dirty-order cleanup. |
| C4 | g137 `LaunchStampIsCheaper`. The original A6 row gives full 207.937 ms and stamp 267.306 ms, failing `stamp-cheaper-than-full-read`. Startup and Git state reads are plausible cost drivers. The current in-process method still enforces `stampMs < fullMs`. | OPEN. The g137 Debug left this timing assertion open. Replay with the A6 row and host-load/phase measurements. See `tests/Antiphon.Tests/Scripts/RcRunEvidenceTests.cs:794`. |
| C5 | g137 `OneEvidenceRootPerRun`. The TRX shows `fault-one-warning`. Windows resolved real Git instead of the fault-only `git.cmd` stub. [Group 137](2026-11-21-why-missed-rc-run-3528e6b1-group-137.md) | Master `e0c3e7d4f64a04cf6189a4d91969b829736987ca` switched the fault to Git child config. The current in-process test came through `733bb687f718aaf950273ab843b6a664df034b51`. |
| C6 | g138 Astra flush fixture stopped at the missing `chmod` command on Windows. [Group 138](2026-11-21-why-missed-rc-run-3528e6b1-group-138.md) | Master `5a361d834a0415e8841a9a86ad67450912fffc4d` added a portable write-failure seam. The port carries it forward at `e0553ad184d623112566d28de4eb063f6a659f93`. |
| C7 | g138 manifest harness reached its 45 s deadline. Debug reproduced it on Linux by adding 230 ms per Git invocation to its 198-process fixture. The chunk TRX was retained. [Group 138](2026-11-21-why-missed-rc-run-3528e6b1-group-138.md) | Master `33d1d4aad87618c8bf805ab1c300c8e35b717c09` bounds fixture work per case. Replay all three `ConfigFixtureScriptAndLockInputs` families. |
| C8 | g139 selector replay used the wrong owner inventory. The TRX names `6591e378-composer-probe`. Debug `9e6138ac` reproduced the same assertion on Linux. [Group 139](2026-11-21-why-missed-rc-run-3528e6b1-group-139.md) | Master `c0c9178c2f311b908c6e6a48df25b5b09f055242` replays the historical fixture through the committed owner inventory. |
| C9 | g141 duration roster drift, 772 checked entries for 773 source entries. [Group 141](2026-11-21-why-missed-rc-run-3528e6b1-group-141.md) | Master `75a2f311e20863355625fc10e5efd72132f6cd80` regenerated the roster. Debug task `fe7f88e7` verified 6/6 on Linux. |
| C10 | g45 C959 fixture lacked the TUI resolver. After the ladder move it picked GPT-6.2 Sol despite the standing profile's GPT-6.1 Sol pin. [C959 investigation](2026-11-21-why-missed-rc-run-3528e6b1-group-45-c959-old-cli.md) | Master `556bddbd2096c7ed6df9840ca9b11e20e9158d70` and `d3823553d6fda95d5ba44be70a46db1dfafbf82b`. Debug `02bed7b0` verified the exact case on master. |
| C11 | g49 last-slot timeout. The race holds a real admission transaction, repeated alias DDL blocked the observer, and the polling deadline did not bind the probe open/query. [Group 49](2026-11-21-why-missed-rc-run-3528e6b1-group-49-queue-slot.md) | Master `79ff0d00aa4f88ffcac61226121038301a95b2cc` bounds and diagnoses the fixture, with the A1 fix. `ca142832d98293cbacbf7d4dec4d3de79abcb7c7` records the evidence. |
| C12 | g71 C475 ConPTY recovery. The original `finally` assertion masked whether the attempt reached the pre-write cut. The Linux replay skipped the affected arm. [Group 71](2026-11-21-why-missed-rc-run-3528e6b1-group-71.md) | OPEN. Debug `f1382575` requires Windows evidence for `WriteReached`, PTY diagnostics, queue status, attempts and verdict. |
| C13 | SessionRunner g1. C1722's Git method budget omitted the qualification-fence witness. [SessionRunner g1](2026-11-21-why-missed-rc-run-3528e6b1-session-runner-group-1.md) | Master `c9f00d5b47a3b36e63dea0c82a3e3bd39e645478` adds the budget row. |

The separate master reds in the brief have independent evidence. C1714's status-trust failure was repaired by `3cd3e5da4835c3d5b5d9d91c60e8b177c543ef38` (Debug `739fddea`, Review `f2371396`). C1199 final-closure documentation was repaired by `a12d4bc9a29fac8f9ee9c29a7e85965c388b89f8` plus the index at `6f6a789e07f031496ae99e58ee6e563d30837a64` (Debug `50794518`, source tip `399f17f05aeca0f1311acdb81227f5250ae07ac5`). These two tests contribute zero failed TRX rows in run 3528e6b1. The C1199 failure in g134 was B5, the required-roster test. Include C1714's native custody witness and the final-closure documentation check if their fixes are in the integrated selection. [C1714 and C1199 Debug](2026-11-21-why-missed-master-reds-c1714-c1199.md) · [Final closure](2026-11-22-why-missed-c1199-final-closure-documentation.md)

## Failed TRX cases

Each row is one `UnitTestResult` with outcome `Failed`. Class and method come from the TRX definition. Case parameters come from the TRX display name. Durations use the original `hh:mm:ss.fffffff` format. Messages are the first 160 characters of the XML-decoded `ErrorInfo/Message`, with line breaks, control characters and table pipes escaped for display.

| # | TRX file | Class and method | Parameters | Duration | Family / evidence | Fix status | Windows | First 160 message characters |
| ---: | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `antiphon-all-group-113.trx` | `Antiphon.Tests.Checkpoints.CheckpointIndexPathsTests.Head_and_stage_listing_read_from_files_are_git_s` | — | `00:00:03.7225080` | C / C1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: CheckpointIndexPaths.StageListing(found[1])\n    should be\n"100755 eaa5fa8755fc20f08d0b3da347a5d1868404e462 0\ta.txt\u0000100644 23d3550c64` |
| 2 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_first` | `(Fresh)` | `00:00:31.7791815` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 3 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_first` | `(AlreadyPresent)` | `00:00:31.7202717` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 4 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_before_acquire` | `(AlreadyPresent)` | `00:00:33.1738105` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 5 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_before_acquire` | `(Fresh)` | `00:00:33.5064406` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 6 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_first` | `(ResumePublication)` | `00:00:35.3045857` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 7 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_before_acquire` | `(ResumePublication)` | `00:00:35.3561874` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 8 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_first` | `(CleanupRetry)` | `00:00:37.1700918` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 9 | `antiphon-all-group-121.trx` | `Antiphon.Tests.Infrastructure.LandingAdmissionControlTests.C448_V14_follow_up_dispatch_before_acquire` | `(CleanupRetry)` | `00:00:37.4117865` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 10 | `antiphon-all-group-123.trx` | `Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests.csproj_references_only_host_cleanup` | — | `00:00:00.1729388` | C / C2 | FIXED-ON-MASTER | Owed | `ShouldAssertException: references\n    should be\n["../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj"]\n    but was (case sensitive comparison)\n["../../s` |
| 11 | `antiphon-all-group-134.trx` | `Antiphon.Tests.Scripts.ReleaseGatePlatformTests.C1199_WindowsRequiredRoster` | — | `00:00:03.7640538` | B / B5 | FIXED-ON-MASTER | Owed | `ShouldAssertException: result.ExitCode\n    should be\n0\n    but was\n1\n\nAdditional Info:\n    FAIL C1323 c1199-roster-35 - count=37 unique=37 shape_conflict_rule=0` |
| 12 | `antiphon-all-group-134.trx` | `Antiphon.Tests.Scripts.ReleaseGatePlatformTests.C1323_PlatformExecution` | — | `00:00:31.0375942` | B / B6 | FIXED-ON-MASTER | Owed | `ShouldAssertException: result.ExitCode\n    should be\n0\n    but was\n1\n\nAdditional Info:\n    WARNING: the git floor could not be read from docker/git-minimum-vers` |
| 13 | `antiphon-all-group-136.trx` | `Antiphon.Tests.Scripts.RcShadowSampleCSharpTests.NonBooleanProvenanceFailureInvalidates` | — | `00:00:00.0813706` | C / C3 | FIXED-ON-MASTER | Owed | `InvalidOperationException: 22 mismatched\nflag-prov-object: Could not load file or assembly 'C:\opt\powershell\System.Management.Automation.dll'. The system cann` |
| 14 | `antiphon-all-group-136.trx` | `Antiphon.Tests.Scripts.RcShadowSampleCSharpTests.CarriedChunkResultInvalidatesCarries` | — | `00:00:00.0115947` | C / C3 | FIXED-ON-MASTER | Owed | ` InvalidOperationException: 13 mismatched\nchunk-missing: Could not load file or assembly 'C:\opt\powershell\System.Management.Automation.dll'. The system cannot  ` |
| 15 | `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.RcDirtyOrderCSharpTests.SameTipPassCannotClearFailure` | — | `00:00:01.9808263` | B / B7 | FIXED-ON-MASTER | Owed | `InvalidOperationException: 1 mismatched\ndirty-same-tip: Access to the path '3536c7178b8184899e0b1e2779d3cf76bb92f1' is denied.` |
| 16 | `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.RcDirtyOrderCSharpTests.CaseDistinctKeysStaySeparate` | — | `00:00:01.3205924` | B / B7 | FIXED-ON-MASTER | Owed | `InvalidOperationException: 1 mismatched\ndirty-case-fix: Access to the path '39a3c67d8f41e40282ec5ae0b16385b3dc6589' is denied.` |
| 17 | `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.RcDirtyOrderCSharpTests.CarryWithOpenFailureRefuses` | — | `00:00:01.2953821` | B / B7 | FIXED-ON-MASTER | Owed | `InvalidOperationException: 1 mismatched\ndirty-carry-discharged: Access to the path '39a3c67d8f41e40282ec5ae0b16385b3dc6589' is denied.` |
| 18 | `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.RcDirtyOrderCSharpTests.UnknownShaRefuses` | — | `00:00:03.5269098` | B / B7 | FIXED-ON-MASTER | Owed | `InvalidOperationException: 3 mismatched\ndirty-unknown-fix: Access to the path '419e1283da757720c8aa9e74ce196502d2a684' is denied.\ndirty-unknown-green: Access to` |
| 19 | `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.RcRunEvidenceTests.LaunchStampIsCheaper` | — | `00:00:06.3653713` | C / C4 | OPEN | Owed | `ShouldAssertException: result.ExitCode\n    should be\n0\n    but was\n1\n\nAdditional Info:\n    A6 fixtureLaunchMs=508.01 sampleFiles=4 sampleBytes=16777216 fullMs=2` |
| 20 | `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.RcRunEvidenceTests.OneEvidenceRootPerRun` | — | `00:00:25.8540216` | C / C5 | FIXED-ON-MASTER | Owed | `ShouldAssertException: result.ExitCode\n    should be\n0\n    but was\n1\n\nAdditional Info:\n    [2026-11-21T09:13:48.5571146Z] WARNING: the git floor could not be re` |
| 21 | `antiphon-all-group-138.trx` | `Antiphon.Tests.Scripts.AstraFixesBTests.FlushFailure` | — | `00:00:01.9422590` | C / C6 | FIXED-ON-MASTER | Owed | `ShouldAssertException: result.ExitCode\n    should be\n0\n    but was\n1\n\nAdditional Info:\n    \u001b[31;1m&: \u001b[0mC:\Antiphon\releases\checkout-18c3d990\script` |
| 22 | `antiphon-all-group-138.trx` | `Antiphon.Tests.Scripts.RcInputManifestTests.ConfigFixtureScriptAndLockInputs` | — | `00:00:45.4532788` | C / C7 | FIXED-ON-MASTER | Owed | `TimeoutException: ScriptHarness test-rc-input-manifest.ps1 case ConfigFixtureScriptAndLockInputs exceeded 00:00:45 execution budget. completion root=False stdou` |
| 23 | `antiphon-all-group-139.trx` | `Antiphon.Tests.Scripts.IncrementalSelectionSoundnessTests.Recorded_owner_map_status_matches_the_committed_map` | — | `00:00:00.1753857` | C / C8 | FIXED-ON-MASTER | Owed | ` ShouldAssertException: status\n    should be\n"selected"\n    but was\n"missed"\n    difference\nDifference     \|  \|    \|    \|    \|    \|    \|    \|    \|   \n            ` |
| 24 | `antiphon-all-group-139.trx` | `Antiphon.Tests.Scripts.RcShadowDirtyDifferentialTests.CorpusMatchesGolden` | — | `00:00:07.2064127` | C / C3 | FIXED-ON-MASTER | Owed | `InvalidOperationException: 46 mismatched\nflag-prov-object: Could not load file or assembly 'C:\opt\powershell\System.Management.Automation.dll'. The system cann` |
| 25 | `antiphon-all-group-141.trx` | `Antiphon.Tests.TestHelpers.ExpectedDurationSyncTests.RosterMatchesCostFile` | — | `00:00:00.0401939` | C / C9 | FIXED-ON-MASTER | Owed | `ShouldAssertException: roster.Classes.Count\n    should be\n773\n    but was\n772` |
| 26 | `antiphon-all-group-26.trx` | `Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests.Incomplete_settlements_never_authorize_release` | — | `00:00:30.8630159` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 27 | `antiphon-all-group-39.trx` | `Antiphon.Tests.Application.C0824RefOnlyContinuationTests.Pending_park_debt_keeps_exact_publication_and_mode` | — | `00:00:33.6822275` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 28 | `antiphon-all-group-39.trx` | `Antiphon.Tests.Application.C0824RefOnlyContinuationTests.Runner_redirect_requires_old_custody_and_rejects_desktop` | — | `00:00:28.0742648` | B / B8 | OPEN | Owed | ` ShouldAssertException: moved.RunnerId\n    should be\n"redirect-target"\n    but was\n"grok-linux"\n    difference\nDifference     \|  \|    \|    \|    \|    \|    \|    \|  ` |
| 29 | `antiphon-all-group-45.trx` | `Antiphon.Tests.Application.DelegateBranchDeliveryTests.C1213_QueuedBranchesReachRecipient` | — | `00:01:02.5995961` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 30 | `antiphon-all-group-45.trx` | `Antiphon.Tests.Application.DelegateBranchDeliveryTests.C1213_QueuedBranchRecovery` | — | `00:01:52.7396627` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 31 | `antiphon-all-group-45.trx` | `Antiphon.Tests.Application.CodexCliObservationTests.C959_Old_version_with_floor_never_refuses` | — | `00:00:54.7979192` | C / C10 | FIXED-ON-MASTER | Owed | ` ShouldAssertException: args[args.IndexOf("--model") + 1]\n    should be\n"gpt-6.1-sol"\n    but was\n"gpt-6.2-sol"\n    difference\nDifference     \|                   ` |
| 32 | `antiphon-all-group-46.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionTests.C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, ResumePublication, dispatch-before-acquire)` | `00:00:43.1397976` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 33 | `antiphon-all-group-46.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionTests.C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, CleanupRetry, dispatch-first)` | `00:00:44.4303664` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 34 | `antiphon-all-group-46.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionTests.C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, Fresh, dispatch-first)` | `00:00:31.1535611` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 35 | `antiphon-all-group-49.trx` | `Antiphon.Tests.Application.DispatchConcurrencyAdmissionTests.Concurrent_last_queue_slot_has_one_winner` | — | `00:03:01.0203261` | C / C11 | FIXED-ON-MASTER | Owed | `TimeoutException: Test 'Concurrent_last_queue_slot_has_one_winner' timed out after 00:03:00\n\n--- Task Status: WaitingForActivation ---\n\n--- Timeout Handler Stac` |
| 36 | `antiphon-all-group-50.trx` | `Antiphon.Tests.Application.AgentTaskLandSourcePersistenceTests.C498_MonitorWaitsForResolverCheckpoint` | — | `00:00:02.2487292` | A / A1 | FIXED-ON-MASTER | Owed | `PostgresException: 40P01: deadlock detected\n\nDETAIL: Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string t` |
| 37 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, AlreadyPresent, dispatch-first)` | `00:00:34.9160010` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 38 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, Fresh, dispatch-first)` | `00:00:35.1349820` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 39 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, AlreadyPresent, dispatch-before-acquire)` | `00:00:37.0447079` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 40 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, Fresh, dispatch-before-acquire)` | `00:00:37.1643069` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 41 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, ResumePublication, dispatch-first)` | `00:00:39.8672315` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 42 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, ResumePublication, dispatch-before-acquire)` | `00:00:42.2696442` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 43 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, CleanupRetry, dispatch-first)` | `00:00:45.0244770` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 44 | `antiphon-all-group-53.trx` | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests.C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther` | `(follow-up, CleanupRetry, dispatch-before-acquire)` | `00:00:43.9661006` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == writerId)).Status\n    should be\nAgentTaskStatus.Dispatched\n    but was\nAgent` |
| 45 | `antiphon-all-group-54.trx` | `Antiphon.Tests.Application.WorktreeRetirementRaceTests.C664_LandRequestRefusedAfterAdmission_LeavesNoExtraLaunch` | — | `00:00:30.4361007` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 46 | `antiphon-all-group-54.trx` | `Antiphon.Tests.Application.WorktreeRetirementRaceTests.C664_LandRequestThenOutcome_ReleasesAndRetirementClaimAccepted` | — | `00:00:30.5184700` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 47 | `antiphon-all-group-54.trx` | `Antiphon.Tests.Application.WorktreeRetirementRaceTests.C664_OptionalWorkExpiredAfterAdmission_LeavesNoActiveLaunch` | — | `00:00:31.7285463` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: stored.Status\n    should be\nAgentTaskStatus.Canceled\n    but was\nAgentTaskStatus.Failed` |
| 48 | `antiphon-all-group-54.trx` | `Antiphon.Tests.Application.WorktreeRetirementRaceTests.C664_CredentialProbeFailAfterAdmission_LeavesNoActiveLaunch` | — | `00:00:32.0824124` | A / A1 | FIXED-ON-MASTER | Owed | `ShouldAssertException: stored.FailureCode\n    should be\nAgentTaskFailureCode.AuthenticationRequired\n    but was\nnull\n\nAdditional Info:\n    Dispatch failed befor` |
| 49 | `antiphon-all-group-54.trx` | `Antiphon.Tests.Application.WorktreeRetirementRaceTests.C459_LandReservesWorkspace` | — | `00:00:30.6934682` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 50 | `antiphon-all-group-60.trx` | `Antiphon.Tests.Application.BlockedTaskSyncRecoveryTests.C1065_SyncDebtRecoversWithoutMintingApproval` | — | `00:00:36.4022427` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 51 | `antiphon-all-group-60.trx` | `Antiphon.Tests.Application.BlockedTaskSyncRecoveryTests.C1088_RemoteNoSourceChangeStoresNoSyncDebt` | — | `00:00:34.8332193` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 52 | `antiphon-all-group-60.trx` | `Antiphon.Tests.Application.BlockedTaskSyncRecoveryTests.C1089_OtherWriterClaimStaysPendingThenBecomesReady` | — | `00:00:33.2280572` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 53 | `antiphon-all-group-61.trx` | `Antiphon.Tests.Application.BootStallDetectionTests.C1151_Boot_branch_statement_counts` | `(identity-changed-before-event, 23, 1)` | `00:00:30.6682365` | A? / A2 | OPEN | Owed | `ShouldAssertException: (await world.TaskAsync()).Attempt\n    should be\n2\n    but was\n1` |
| 54 | `antiphon-all-group-61.trx` | `Antiphon.Tests.Application.BootStallDetectionTests.C1151_Warning_writer_revalidates_the_episode_identity` | `(concurrent-writers)` | `00:00:40.7206309` | A? / A2 | OPEN | Owed | `ShouldAssertException: warnings.Count\n    should be\n1\n    but was\n0\n\nAdditional Info:\n    Warning: Could not record BootStallDetected for task 8f2ee1fa; detecti` |
| 55 | `antiphon-all-group-64.trx` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Running_converter_reconciles_after_dispatcher_death` | — | `00:00:49.4673635` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 56 | `antiphon-all-group-71.trx` | `Antiphon.Tests.Application.SessionQueueReceiptPlumbingTests.C475_QueueCommitAndTransportRecovery` | `(attempt-before-write)` | `00:00:18.1565175` | C / C12 | OPEN | Owed | ``ShouldAssertException: Task `attempt`\n    should throw\nSystem.InvalidOperationException\n    but did not`` |
| 57 | `antiphon-all-group-75.trx` | `Antiphon.Tests.Application.DurableRunnerSpillReceiptTests.Grok_rules_launch_brief_uses_runner_ceiling_despite_a_modern_desktop_profile` | — | `00:00:36.8288500` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 58 | `antiphon-all-group-76.trx` | `Antiphon.Tests.Application.TaskParkPublicationTests.C1213_LocalParkBothForms` | — | `00:00:31.9339128` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 59 | `antiphon-all-group-76.trx` | `Antiphon.Tests.Application.TaskParkPublicationTests.C1065_WorkspaceModesPreservePublicationAuthority` | — | `00:00:32.1690025` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 60 | `antiphon-all-group-76.trx` | `Antiphon.Tests.Application.TaskParkPublicationTests.C1065_PublicationReceiptCannotAuthorizeChangedAttempt` | — | `00:00:32.4866891` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 61 | `antiphon-all-group-81.trx` | `Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification` | `(concurrent capture)` | `00:00:03.7817136` | A / A1 | FIXED-ON-MASTER | Owed | `PostgresException: 40P01: deadlock detected\n\nDETAIL: Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string t` |
| 62 | `antiphon-all-group-82.trx` | `Antiphon.Tests.Application.TaskParkRunnerIdentityTests.C1065_RemoteIdentityCaptureBindsEpisodeBeforePublication` | — | `00:00:34.3446920` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 63 | `antiphon-all-group-82.trx` | `Antiphon.Tests.Application.TaskParkRunnerIdentityTests.C1065_LocalEvidenceMapsToTypedRunnerProofWithoutFiction` | — | `00:00:31.5402014` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 64 | `antiphon-all-group-86.trx` | `Antiphon.Tests.Application.DispatchConcurrencyDispatchTests.Put_racing_claim_observes_one_complete_policy` | — | `00:00:30.4257320` | A / A1 | FIXED-ON-MASTER | Owed | `InvalidOperationException: An exception has been raised that is likely due to a transient failure.` |
| 65 | `antiphon-all-group-86.trx` | `Antiphon.Tests.Application.DispatchConcurrencyDispatchTests.Competing_claims_admit_only_one_task` | — | `00:03:01.0088139` | A / A1 | FIXED-ON-MASTER | Owed | `TimeoutException: Test 'Competing_claims_admit_only_one_task' timed out after 00:03:00\n\n--- Task Status: WaitingForActivation ---\n\n--- Timeout Handler Stack Tra` |
| 66 | `antiphon-all-group-92.trx` | `Antiphon.Tests.Application.C0824WorkspaceSlotRoleGateTests.Removed_role_slot_is_released_normally` | — | `00:00:03.6134480` | B / B1 | OPEN | Owed | `ShouldAssertException: (await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>()\n                .ReconcileAsync(CancellationToken.None).Wa` |
| 67 | `antiphon-all-group-92.trx` | `Antiphon.Tests.Application.C0824WorkspaceSlotRoleGateTests.None_stops_new_leases_while_releases_continue` | `(reconcile-pass)` | `00:00:04.5973634` | B / B1 | OPEN | Owed | `ShouldAssertException: (await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>()\n                .ReconcileAsync(CancellationToken.None).Wa` |
| 68 | `antiphon-all-group-92.trx` | `Antiphon.Tests.Application.C0824WorkspaceSlotRoleGateTests.None_stops_new_leases_while_releases_continue` | `(hosted-loop)` | `00:00:14.8352780` | B / B1 | OPEN | Owed | ` ShouldAssertException: free.State\n    should be\n"Free"\n    but was\n"Leased"\n    difference\nDifference     \|  \|    \|    \|    \|    \|    \|   \n               \| \\|/  ` |
| 69 | `antiphon-all-group-94.trx` | `Antiphon.Tests.Application.C1960QuarantineReleaseRouteTests.Route_releases_real_quarantine_end_to_end` | — | `00:00:11.6542540` | B / B2 | OPEN | Owed | ``ShouldAssertException: world.Pool.Ledger().QuarantinedDirectories\n    should have single item but had\n0\n    items and was\n[] (System.Collections.Generic.List`1[`` |
| 70 | `antiphon-all-profile-404.trx` | `Antiphon.Tests.Application.C0824DesktopLegacyReleasePolicyTests.Double_run_is_idempotent` | `(concurrent)` | `00:00:13.5354934` | B / B3 | BRANCH / REVIEW | Owed | `ShouldAssertException: Directory.Exists(handle.Path)\n    should be\nFalse\n    but was\nTrue\n\nAdditional Info:\n    the tree is gone` |
| 71 | `antiphon-all-split-12.trx` | `Antiphon.Tests.Application.LandNonCodeSkipTests.C_FT2_BuildDefinitionByteStillBuilds` | — | `00:00:01.3364232` | B / B4 | FIXED-ON-MASTER | Owed | `UnauthorizedAccessException: Access to the path '0777149c519bcccde56ea48422b78037103ebd' is denied.` |
| 72 | `antiphon-all-split-13.trx` | `Antiphon.Tests.Application.LandNonCodeSkipTests.C_FT2_EmbeddedManifestSkipUnchanged` | `(missing)` | `00:00:00.7533227` | B / B4 | FIXED-ON-MASTER | Owed | `UnauthorizedAccessException: Access to the path '0d746b709bf032f260d7db33ea37bc477a1f0b' is denied.` |
| 73 | `antiphon-all-split-13.trx` | `Antiphon.Tests.Application.LandNonCodeSkipTests.C_FT2_EmbeddedManifestSkipUnchanged` | `(stale)` | `00:00:01.3049390` | B / B4 | FIXED-ON-MASTER | Owed | `UnauthorizedAccessException: Access to the path '37b48c7bf78fb1dfbf92ec02447088af2ea35b' is denied.` |
| 74 | `antiphon-all-split-15.trx` | `Antiphon.Tests.Application.LandNonCodeSkipTests.C_FT2_MalformedTargetManifestBuilds` | `(malformed)` | `00:00:00.9997987` | B / B4 | FIXED-ON-MASTER | Owed | `UnauthorizedAccessException: Access to the path 'c5269996f59a60ab24dad29a1f9fa554ca1536' is denied.` |
| 75 | `antiphon-all-split-15.trx` | `Antiphon.Tests.Application.LandNonCodeSkipTests.C_FT2_StaleTargetManifestBuilds` | `(stale-count)` | `00:00:00.9185493` | B / B4 | FIXED-ON-MASTER | Owed | `UnauthorizedAccessException: Access to the path '886a5e6192efb5a81fa37b8382fabb374e99e1' is denied.` |
| 76 | `antiphon-all-split-15.trx` | `Antiphon.Tests.Application.LandNonCodeSkipTests.C_FT2_StaleTargetManifestBuilds` | `(stale-digest)` | `00:00:00.8436095` | B / B4 | FIXED-ON-MASTER | Owed | `UnauthorizedAccessException: Access to the path '37b48c7bf78fb1dfbf92ec02447088af2ea35b' is denied.` |
| 77 | `antiphon-all-split-15.trx` | `Antiphon.Tests.Application.LandNonCodeSkipTests.C_FT2_MalformedTargetManifestBuilds` | `(missing)` | `00:00:00.7113282` | B / B4 | FIXED-ON-MASTER | Owed | `UnauthorizedAccessException: Access to the path '0d746b709bf032f260d7db33ea37bc477a1f0b' is denied.` |
| 78 | `session-runner-all-group-1.trx` | `Antiphon.SessionRunner.Tests.C1722GitTimeoutBudgetTests.Budget_table_covers_every_git_method` | — | `00:00:00.0614479` | C / C13 | FIXED-ON-MASTER | Owed | `ShouldAssertException: recorded\n    should be\n["C0824RunnerBranchRefsTests.C0824_absent_refs_are_idempotent_success", "C0824RunnerBranchRefsTests.C0824_ancestor` |

## Failed-first seed and next launch

- Seed `C:\Antiphon\releases\state-3528e6b1\seed-3528e6b1-61cfe0c5.json`.
- SHA-256 `a46d1a1522ae84758368735a442d69ba7ef1a0ac2676b28f2393c453bc37a549`.
- The checked-in `scripts/export-nightly-stopped-run-seed.ps1` from the pinned RC checkout exported 59 failed-first class/method identities and 1,292 chunk records. It used the original run directory, the sibling timing directory, and the exact run ID, candidate ID and SHA. The cached core for PowerShell 7.6.10 was present. The exporter uses `FileMode.CreateNew` (`scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs:494`).
- Cross-checking against the original TRX rows found an exact match after collapsing parameters. Group 47 is the sole unfinished seed chunk, recorded as `started=true`, `completed=false`. The two log-only suites are outside the seed's TRX/plan chunk census. Group 111's explicit skip gives the seed a completed chunk. Its coverage reason still requires attention.
- The repository intake `PriorFailureHistory.InitializeDispatch` accepted the file with the matching digest and source-candidate acknowledgement, with an empty fallback reason. The checked implementation limits both the observation and creation timestamps to 14 days old, with up to five minutes of future skew. The dispatcher resolves historical methods against current selection, so verify mapping on the new SHA. [Intake](../../scripts/lib/Antiphon.Nightly/PriorFailureHistory.cs#L743) · [Ordering contract](../testing-and-build.md#nightly)

### Smallest diagnostic replay

1. After the native lock clears, run the 33 TRX-bearing failed chunks listed above plus the full three-class group 47 selection. The old group 47 filter was `/*/Antiphon.Tests.Application/(AgentRemoteControlGateTests*)|(CardDoneWorktreeCleanupTests*)|(ReviewEvidenceResettlementTests*)/*`. Use the new candidate's manifests to resolve current group IDs and filters. Keep replacements when tests moved, including the g123 contract method and all three g138 manifest arms.
2. Include group 111 and validate the three unadmitted skips across g111/g136 against the new candidate's Windows selection. Add affected witnesses the new selection identifies, including the alias-lock and atomic-rename witnesses when their fixes are integrated. Replay group 51 only if timing verification is required or the new selection includes it. Client and scripts had passing suite logs.
3. Route the OPEN rows by failure evidence. Start with g39/g92/g94 runner release responses and ledgers, g61 blocked commands and barrier arrivals, g71 ConPTY pre-write evidence, and g137 stamp timing. Profile 404 is a blocked repair branch with native evidence owed. Group 47's product issue remains on CARD-2053. Confirm the fixed rows on Windows and keep the result by candidate SHA and exact selection.

### Qualification decision and launch command

> The line's new tip is tested from scratch under the rc profile. A partial run or a green under a different policy is never reused.

[Release gates, Fix](../release-gates.md#cutting-testing-and-publishing-a-candidate) (`docs/release-gates.md:579-582`). The tag-backing rule also requires that “the tagged SHA itself has an admitted all-executed Windows Full” and says “seeds never give credit.” [Tag and promotion backing](../release-gates.md#tag-and-promotion-backing-card-1820-s5-decision-d-5)

The diagnostic replay can establish whether the repairs work. Release credit requires a **fresh Windows Full** for the integrated rc SHA. Use the seed for ordering inside that Full. The observed remote rc SHA still points to the failed candidate, so the next SHA and its checkout are inputs from integration. Once the new candidate is journaled and pinned in its private checkout, fill in `$checkout`, `$sha` and `$candidateId` from that candidate. The explicit `-AcceptCrossCandidateSeed` switch records the caller's acknowledgement. The `PriorFailuresPath`, `PriorFailuresDigest` and `SeedSourceCandidateId` arguments bind the file and source.

Prepare an unused supervisor directory and state root using the qualification skill's preflight and any required pre-run slot procedure. The launch record and command below use the checked-in supervisor set copied from the integrated checkout. Execute after the preceding native owner releases its lock. [Supervisor launch procedure](../../.claude/skills/antiphon-rc-qualification/SKILL.md#supervisor-launch-set-card-1667)

```powershell
# Set these three from the newly integrated, journaled rc candidate.
$checkout = '<private candidate checkout path>'
$sha = '<full SHA of integrated rc candidate>'
$candidateId = '<journaled candidate ID>'

$releaseRoot = 'C:\Antiphon\releases'
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$taskRoot = Join-Path $releaseRoot "task-$runId"
$stateRoot = Join-Path $releaseRoot "state-$runId"
$logRoot = Join-Path $releaseRoot "candidates\$candidateId\logs"
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$seedPath = 'C:\Antiphon\releases\state-3528e6b1\seed-3528e6b1-61cfe0c5.json'
$seedDigest = 'a46d1a1522ae84758368735a442d69ba7ef1a0ac2676b28f2393c453bc37a549'

$null = New-Item -ItemType Directory -Path $taskRoot -ErrorAction Stop
Copy-Item -LiteralPath (Get-ChildItem -LiteralPath "$checkout\scripts\qualification" -Filter '*.ps1' -File).FullName -Destination $taskRoot
$record = [ordered]@{
    checkoutRoot = $checkout
    expectedSha = $sha
    nightlyArguments = @(
        '-NoSync', '-Profile', 'rc', '-Trigger', 'rc', '-SelectionMode', 'Full',
        '-Ref', 'rc/2026-11-30', '-ExpectedSha', $sha, '-CandidateId', $candidateId,
        '-CheckoutRoot', $checkout, '-StateRoot', $stateRoot, '-LogRoot', $logRoot,
        '-ReleaseRoot', $releaseRoot, '-RunId', $runId,
        '-PriorFailuresPath', $seedPath, '-PriorFailuresDigest', $seedDigest,
        '-AcceptCrossCandidateSeed', '-SeedSourceCandidateId', 'rc-2026-11-30-bd34a7ff6da9'
    )
}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$taskRoot\launch-record.json" -Encoding utf8
& $pwsh -NoProfile -File "$taskRoot\preflight.ps1" -CheckoutRoot $checkout
if ($LASTEXITCODE -ne 0) { throw 'Qualification preflight refused the candidate.' }

$starter = ('"{0}" -NoProfile -File "{1}\start-supervisor.ps1" -CheckoutRoot "{2}" -TaskRoot "{1}" -LaunchScript "{1}\launch-rc.ps1" -LogRoot "{3}" -StateRoot "{4}" -RunId "{5}"' -f $pwsh, $taskRoot, $checkout, $logRoot, $stateRoot, $runId)
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $starter }
```

## Not done, noted

No qualification, build, test or restart was started in this investigation. The only change in the run's state root was the new seed. The repo change is this note.
