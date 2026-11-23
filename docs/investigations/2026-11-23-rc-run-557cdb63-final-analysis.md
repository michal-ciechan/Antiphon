# Windows Full run 557cdb63 final analysis

Run **557cdb63** finished with outcome **TESTS**, with **seven failed test cases and seven coverage gaps**. Twelve processes returned exit 8 after a single expected Windows skip each. The qualification harness explicitly accepted all twelve. Six of the seven prior seed identities passed on Windows. The seventh identity was retired and its replacement passed.

## Run and evidence

| Field | Value |
| --- | --- |
| Candidate | `rc-2026-12-03-ecc9bfaa5bfa` on `rc/2026-12-03` |
| Candidate SHA | `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91` |
| Windows Full | 1,296 dispatch entries. The summary records 1,288 pass and 8 FAIL rows, including one coverage failure at process exit 0. |
| Timing | The owner's `last-run.json` records 2026-11-22 20:52:35Z to 2026-11-23 12:15:37Z. The test summary records 20:53:16.909Z to 12:15:23.326Z, 55,326.417 s (15.37 h). |
| Verdict | `dispatchProgress.liveness=closed`, `succeeded=false`, `testsPassed=false`, `coverageComplete=false` |
| Log directory | `C:\Antiphon\releases\candidates\rc-2026-12-03-ecc9bfaa5bfa\logs\2026-11-22-2052-557cdb63` |
| State directory | `C:\Antiphon\releases\state-557cdb63` |
| Timing directory | Sibling `timing-557cdb63` beside the log directory |
| Analysis base | `766242de67d9db25f9bf018425e93ad769363f4c` on `origin/master` |

Evidence filenames below are relative to the log directory unless specified. Joining `UnitTestResult.testId` to `TestDefinitions/UnitTest.id` across all 1,294 TRX files yielded **27,664 Passed, 7 Failed and 36 NotExecuted**. The 36 comprise 31 tolerated skips and five unadmitted skips. Two more planned UIDs have no result row. The summary has no recorded harness hang.

## Seven exit-2 chunks

Each row has one failing TRX case. Fix SHAs marked master are ancestors of the analysis base. Group 141 had a published repair in flight at that base. [Existing run-group investigations](2026-11-22-rc-run-79d9e6f9-failures.md)

| TRX | Failing method | TRX evidence and existing cause | Fix SHA |
| --- | --- | --- | --- |
| `antiphon-all-group-90.trx` | `GrokStartupReadyOrderingTests.Work_waits_for_ready_rules_ack_and_complete_prompt` | C778 reported failed readiness, rules pending, and zero writes/submits. The test's 1.5 s ready deadline could expire before it released readiness. | Master `85dfa57a356c347a52835db32aa2e035c2721f58` |
| `antiphon-all-group-97.trx` | `DelegationScopeHoldTests.a_worktree_task_dispatches_over_a_shared_holder_with_one_warning` | The sink recorded three launches where the assertion expected one. The dispatcher tick also launched foreign queued rows in the shared fixture. | Master `1133a656642b658f0045ff8ce0d2d150a95c0812` |
| `antiphon-all-group-139.trx` | `C487ReadOnlyCleanupWitnessTests.C487_WindowsReadOnlyWitness` | Child exit 1 at `c487-harness.ps1:7`. The text-loaded harness had an empty `$PSScriptRoot` and `Join-Path` rejected its empty path. | Master `38271cf9643adbed3706550538bc0f67af2f8bc4` |
| `antiphon-all-group-140.trx` | `NightlyHealthTests.C487_G109` | The failure was `["G109 sha mismatch :: "]`. The fixture omitted today's due-job date, so the expected SHA reason never fired in the overnight window. | Master `c4a7763cb2b93e495144d21185913f19fd0e18e9` |
| `antiphon-all-group-141.trx` | `ControlledLandingGitTests.C688_UnregisterDropsHeadFilesWithoutTouchingSetAsideOrRecreatedPath(False)` | `no_follow_delete_failed` on the retired admin `gitdir` reported a sharing violation. The repair note identifies the fake's single delete walk as the affected call. The TRX does not identify the holder. The `True` sibling passed. | In flight `035424bdd1cf25d516fd7d07f387a694b6c3fe63` (fake uses the revalidating walk). Task `294b9500` also carries the disposition repair `2315c7c271a8902fad7e2c95f2c0b282431f194e` and updated native witness at tip `3ac4a0bbb3f1d54cb835403b4b23ca19c58ffcc3`. |
| `resilience-all.trx` | `ResilienceAssemblyTests.Mirror_holds_the_resilience_tests` | Expected one `StaleWorkingTranscriptWatchdogTests` declaration, got three. The old census also counted two fixture strings in `RcInputOwnersMapTests.cs:193,196`. | Master `61deca9b29815ae66840317fae717fc546362379` |
| `session-runner-all-group-5.trx` | `WindowsShellGuardAuditTests.Every_command_interpreter_literal_is_behind_a_windows_guard` | `C1652_UNGUARDED_INTERPRETER` named the `cmd.exe` literal at `C0824WindowsSlotHolderTests.cs:63`. The method's native Windows gate was outside the audit's recognized guard shape. | Master `bd4c23a08ef3dec406cdc305781b0589bca8d6bd` |

The Resilience case ran and failed its assertion. The separate linked-fixture compile failure described in the older investigation has repair `16d67c35fc2e31ef2668a4b58c45b7dcd9ee3470`, which is also on master.

## Twelve exit-8 chunks

Every row below is suite `antiphon`, project `tests/Antiphon.Tests/Antiphon.Tests.csproj`. Each corresponding `antiphon-<chunk>-tests.log:3-4` names the selected method and its skip reason. The logs report `Zero tests ran` with total 1, failed 0, succeeded 0, skipped 1. The TRX has one `NotExecuted` row with a matching skip trace. All twelve summary rows have `processExitTolerated=true`, `result=pass`, and empty `evidenceReasons`. Selection and startup succeeded. MTP exit 8 here reflects the expected skip of the only selected case. [Skip matching and exit handling](../../scripts/lib/Antiphon.Nightly/ChunkVerdict.cs#L149) · [Summary result binding](../../scripts/lib/Antiphon.Nightly/NightlyChunkRunner.cs#L636)

| Chunk | Test | Reason in the log |
| --- | --- | --- |
| `all-profile-2144` | `RemoteScriptContractTests.C1105_Resume_extraction_is_closed` | C1008 native Linux host contract requires bash, flock and POSIX filesystem semantics. |
| `all-profile-990` | `LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet` | The fixture uses a PATH shim named `dotnet`. Windows process start resolves `dotnet.exe`. |
| `all-profile-1882` | `WorktreeNoFollowDeleteTests.C1717_B2_ReusedInodeIsANewDirectoryIdentity` | Linux inode reuse witness. |
| `all-profile-1883` | `WorktreeNoFollowDeleteTests.C1717_B3_BytesChangedDuringTheWalkAreKept` | Linux mid-walk receipt check. |
| `all-profile-1884` | `WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt` | Linux late-marker walk. |
| `all-profile-1885` | `WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerStopsTheWalk` | Linux late-marker walk. |
| `all-profile-1886` | `WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToAnUnwalkedDirectoryHolds` | Linux mid-walk receipt check. |
| `all-profile-1887` | `WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToTheWalkedDirectoryHolds` | Linux mid-walk receipt check. |
| `all-profile-1891` | `WorktreeNoFollowDeleteTests.C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept` | Linux inotify-timed writer. |
| `all-profile-1892` | `WorktreeNoFollowDeleteTests.C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles` | Linux `renameat2` ancestor exchange. |
| `all-profile-2337` | `RollingVolumeRecycleScriptTests.C1008_Host_proof_admits_only_an_authenticated_private_origin` | C1008 native Linux host contract. |
| `all-profile-2338` | `RollingVolumeRecycleScriptTests.C1008_Host_proof_file_omits_credentials` | C1008 native Linux host contract. |

## All five coverage reasons

**Group 142 and suite union.** The three summary entries below refer to the same two missing cases. Both are manual benchmarks marked class-level `[Explicit]` and catalogued as diagnostics. Full discovery retained both UIDs (`antiphon-profile-discovery/log_261122210401999.diag:1376,1379`) and `timing-557cdb63/groups-planned-antiphon.json` assigned both to group 142. The actual launch filter includes the class (`antiphon-all-group-142-execution/log_261122213131991.diag:10`). The execution events and TRX contain neither method. The log counted 93 passes against 95 planned UIDs. This is a discovery/admission mismatch for explicit benchmarks. The qualification planner and diagnostic-lane policy own it. [Manual benchmark declaration](../../tests/Antiphon.Tests/TestHelpers/RcManifestScaleTests.cs#L11) · [Diagnostic catalog](../../tests/live-lanes.json#L2120) · [Chunk coverage checks](../../scripts/lib/Antiphon.Nightly/ChunkVerdict.cs#L338) · [Suite union](../../scripts/lib/Antiphon.Nightly/NightlySuiteRunner.cs#L557)

~~~text
missing-expanded-row Antiphon.Tests.TestHelpers.RcManifestScaleTests.1.1.ScaleSamples(System.Threading.CancellationToken).1.1.0,Antiphon.Tests.TestHelpers.RcManifestScaleTests.1.1.ScaleReplay(System.Threading.CancellationToken).1.1.0
missing-required-class Antiphon.Tests.TestHelpers.RcManifestScaleTests
missing-expanded-row union Antiphon.Tests.TestHelpers.RcManifestScaleTests.1.1.ScaleSamples(System.Threading.CancellationToken).1.1.0,Antiphon.Tests.TestHelpers.RcManifestScaleTests.1.1.ScaleReplay(System.Threading.CancellationToken).1.1.0
~~~

**Groups 135 and 51.** Each method below has a `NotExecuted` TRX row with an intentional platform skip. None of the five identities is in `tests/expected-platform-skips.json` at the candidate or analysis base. Both chunks exited 0 and separately recorded `coverageComplete=false`. The coverage owner is the Windows expected-skip roster, alongside the tests' owners for the precise skip contracts.

~~~text
unexecuted-outcome:Antiphon.Tests.Scripts.QualificationTimingTests.Launch_path_opens_no_timing_file,Antiphon.Tests.Scripts.QualificationTimingTests.Fifo_sidecar_does_not_delay_launch,Antiphon.Tests.Scripts.QualificationTimingTests.Unsafe_journals_are_refused_by_the_bounded_reader,Antiphon.Tests.Scripts.QualificationTimingTests.Linux_owned_launch_is_a_known_custody_gap
unexecuted-outcome:Antiphon.Tests.Application.ReviewEvidenceResettlementTests.C2053_GitHelperDeadlineNamesTestAndHelper
~~~

| Log and line | Test | Skip evidence |
| --- | --- | --- |
| `antiphon-all-group-135-tests.log:13-14` | `QualificationTimingTests.Launch_path_opens_no_timing_file` | FIFOs and unix mode 000 are POSIX properties. [Source](../../tests/Antiphon.Tests/Scripts/QualificationTimingTests.Runs.cs#L328) |
| `antiphon-all-group-135-tests.log:18-19` | `QualificationTimingTests.Fifo_sidecar_does_not_delay_launch` | FIFOs are POSIX properties. [Source](../../tests/Antiphon.Tests/Scripts/QualificationTimingTests.Runs.cs#L188) |
| `antiphon-all-group-135-tests.log:23-24` | `QualificationTimingTests.Unsafe_journals_are_refused_by_the_bounded_reader` | FIFOs and unix mode 000 are POSIX properties. [Source](../../tests/Antiphon.Tests/Scripts/QualificationTimingTests.Runs.cs#L276) |
| `antiphon-all-group-135-tests.log:28-29` | `QualificationTimingTests.Linux_owned_launch_is_a_known_custody_gap` | The Windows custody path uses a Job Object. [Source](../../tests/Antiphon.Tests/Scripts/QualificationTimingTests.Runs.cs#L382) |
| `antiphon-all-group-51-tests.log:3-4` | `ReviewEvidenceResettlementTests.C2053_GitHelperDeadlineNamesTestAndHelper` | Linux shell witness for a Git child that exits while a descendant holds stdout. [Source](../../tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs#L35) |

## All 31 tolerated skips and 28 stale-roster warnings

The warnings and tolerated rows name the same 28 unique methods. The test source scan returned zero Windows source gaps at both snapshots. The main assembly discovery contains 27 identities and the SessionRunner discovery contains the remaining identity. Each other native suite discovery contains zero of these 28 identities. `NightlySuiteRunner.Complete` compares the shared roster against each suite's discovery and accumulates the per-suite misses. The warnings therefore identify tests outside an individual suite and give no evidence of a deletion or rename. Warning scoping belongs to the qualification harness. The warning list is separate from coverage reasons. [Suite comparison](../../scripts/lib/Antiphon.Nightly/NightlySuiteRunner.cs#L566) · [Set comparison](../../scripts/lib/Antiphon.Nightly/SuiteCompletionParts.cs#L32) · [Source-gap scan](../../scripts/lib/Antiphon.Nightly/ExpectedSkipScan.cs#L143)

The following gives every method and the case counts from `summary.json:toleratedSkips` and the joined TRX. The 12 profile cases are also listed above. Thirty cases are `platform`. The Claude canary is the one `live-context` case.

| Fully qualified class | Methods and cases | Disposition |
| --- | --- | --- |
| `Antiphon.Tests.Application.C0824RefOnlyCleanupTests` | `Remote_cleanup_still_uses_its_binding_and_generation` (3: `Retirement`, `Ready`, `Busy`) | Platform |
| `Antiphon.Tests.Application.C0824RefOnlySyncTests` | `Reported_tip_and_mirror_publication_keep_existing_contract` | Platform |
| `Antiphon.Tests.Checkpoints.CheckpointSourceStateTests` | `tool_snapshot_of_a_non_utf8_name_is_unknown_not_stamped_absent` | Platform |
| `Antiphon.Tests.Checkpoints.CheckpointColdBuildTests` | `C1734_Cold_build_preserves_native_obj_and_terminal_receipt` (2: `tool`, `script`), `C1734_Real_tampering_keeps_a_red_terminal_report` | Platform |
| `Antiphon.Tests.Scripts.QualificationTimingTests` | `A_fifo_journal_is_never_opened`, `Hostile_files_keep_the_publication_inside_the_budget` | Platform |
| `Antiphon.Tests.Scripts.VolumeRetirementApplyGateTests` | `Recheck_target_refuses_a_changed_fingerprint`, `Valid_token_refuses_protected_names_and_keeps_host_state`, `State_volume_named_in_the_plan_is_refused_before_delete`, `Refused_apply_keeps_the_ready_preview`, `Manual_apply_refuses_until_the_operation_token_matches`, `Scripted_apply_refuses_until_the_operation_token_matches`, `Resume_refuses_until_the_operation_token_matches` | Platform |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests` | `C1105_Resume_extraction_is_closed` | Platform |
| `Antiphon.Tests.Agents.ClaudeDialogWatchdogCanaryTests` | `C680_LiveNudgeRefusesPermissionAndQuestionDialogs` | Live context |
| `Antiphon.SessionRunner.Tests.RunnerChildClaudeTokenInheritanceTests` | `Claude_launch_seeds_trust_in_the_runner_config_dir` | Platform |
| `Antiphon.Tests.Application.LandGraphEvaluationTests` | `EvaluationBudgetRefusesSlowDotnet` | Platform |
| `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests` | `C1717_B2_ReusedInodeIsANewDirectoryIdentity`, `C1717_B3_BytesChangedDuringTheWalkAreKept`, `C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt`, `C1717_B3_LateRepositoryMarkerStopsTheWalk`, `C1717_B3_NameAddedToAnUnwalkedDirectoryHolds`, `C1717_B3_NameAddedToTheWalkedDirectoryHolds`, `C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept`, `C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles` | Platform |
| `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests` | `C1008_Host_proof_admits_only_an_authenticated_private_origin`, `C1008_Host_proof_file_omits_credentials` | Platform |

## Native confirmation and remaining evidence

The prior seed is `C:\Antiphon\releases\state-79d9e6f9\seed-79d9e6f9-eabe80e4.json`. Its SHA-256 `e0ce0bace5af76656db4343aac282bd11f8351c5f12b08045e03e44242357e62` matches `dispatch-schedule.json:historyDigest`. The five chunks in the table occupy dispatch ordinals 0–4 and have `priorFailure=true`.

| Prior method | Native Windows receipt |
| --- | --- |
| `ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy` | Passed, group 134, `43342bec-b649-bfb9-ea4f-bff6c06f186e` |
| `WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates` | Passed, group 134, `d238892d-8a3d-c52b-38b6-416c26b1ab04` |
| `MergeHighRadiusSubsetTests.merge_subset_runs_every_impacted_row_the_batch_diff_selects` | Passed, group 137, `026bfd80-077e-9595-12f7-656461ce6475` |
| `MergeHighRadiusSubsetTests.merge_subset_without_a_migration_is_exactly_the_fixed_classes` | Passed, group 137, `1c4c8255-7bd9-b53e-bc9c-6c62d9f92c1a` |
| `LiveLaneInventoryTests.EveryLiveGateHasDisposition` | Passed, group 138, `f8193508-044b-0828-69f1-2c8c593ad78f` |
| `NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute` | Passed in the seeded Antiphon project, group 144, `9e3e4be9-b628-2513-9b61-7cfcef232d79` |
| `CheckpointToolProjectContractTests.csproj_references_only_host_cleanup` | No exact row. Commit `648ae96a88ed49c6573f85fd9771f41c0133f418` renamed and expanded this witness. `csproj_references_only_isolated_libraries` passed in group 123 (`6f759d18-f8a0-78f9-c843-ed785572ece5`). [Replacement witness](../../tests/Antiphon.Tests/Infrastructure/CheckpointToolProjectContractTests.cs#L25) |

Native evidence still owed on the next integrated candidate covers each of the seven exit-2 cases after its repair, especially the C688 deleting `False` case and the new C847 held-entry witness once the in-flight repair is integrated. Full closure also owes a correct disposition for both explicit benchmarks and all five intentional platform skips. The retired identity stays in the seed under the exact-name carry rule. Its replacement already has native passing evidence. The 31 rostered skips already have admissible Windows evidence.

## Failed-first seed

| Field | Value |
| --- | --- |
| Path | `C:\Antiphon\releases\state-557cdb63\seed-557cdb63-865f7412.json` |
| File SHA-256 | `eea76b5e11a1c9034c91ccbef92b20789d2944b000cb2dd733aa02f1a7ed5a16` |
| Created at UTC | `2026-11-23T12:23:35.8593144Z` |
| Source observed at UTC | `2026-11-23T12:15:23.3259292+00:00` |
| Source run and candidate | `557cdb63`, `rc-2026-12-03-ecc9bfaa5bfa` |
| Format | `antiphon-nightly-stopped-seed-v1`, the cross-candidate ordering envelope |
| Methods and cases | 15 method identities for 15 case records: seven current failed TRX cases, five current `NotExecuted` TRX cases, two current missing planned cases, and one historical failed case under the retired identity. |

Used `StoppedSeedFiles.Export` in the retained candidate Release assembly and the same envelope hash and exact-identity reconciliation as the previous seed note (`a1d72edd67b9b579297600bd223b6fb81f0b7b0d` on `origin/feat/card-task-eabe80e4`). The exporter supplied seven failures, which matched the independent TRX join. Added the five unadmitted skips and two missing planned cases as ordering errors because the schema accepts `Failed`, `Error`, `Timeout` and `Aborted` in `failedTests`. Each of these seven rows retains its actual observation in `sourceOutcome` (`NotExecuted` or `Missing`). The single carried row is the prior `csproj_references_only_host_cleanup` failure. The seed grants dispatch ordering only. The destination plan can ignore a retired or excluded method, and an identity in a whole-class chunk can prioritize the chunk. [Exporter](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L254) · [Envelope hash](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L56) · [Seed schema](../../scripts/lib/Antiphon.Nightly/DispatchSchedule.cs#L677) · [Matching and ordering](../../scripts/lib/Antiphon.Nightly/DispatchSchedule.cs#L263)

The exported plan has 1,294 native chunks. It marks 1,293 complete for ordering and group 142 started and incomplete. The dispatch adds the two non-TRX lanes `client/all` and `scripts/all`. All 2,599 retained evidence digests matched an independent SHA-256 check. The previous seed's digest and the final seed's 15 identities also matched independent reads.

The launcher's `PriorFailureHistory.InitializeDispatch` accepted the final seed, current master as destination, cross-candidate acknowledgement, and the stated source candidate. It returned source run `557cdb63`, 15 identities, and an empty fallback reason. A 64-zero digest produced `history-digest-mismatch`. Both calls used an empty owned-directory argument and the existing Release DLL so they exercised actual intake in-process. The intake, schema and seed reader source files are unchanged between the source and destination SHAs. [Launcher call](../../scripts/lib/Antiphon.Nightly/NightlyTests.cs#L294) · [Intake](../../scripts/lib/Antiphon.Nightly/PriorFailureHistory.cs#L752) · [File digest validation](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L86)

Append to the next candidate's normal Windows Full launch:

~~~powershell
-PriorFailuresPath 'C:\Antiphon\releases\state-557cdb63\seed-557cdb63-865f7412.json' -PriorFailuresDigest 'eea76b5e11a1c9034c91ccbef92b20789d2944b000cb2dd733aa02f1a7ed5a16' -AcceptCrossCandidateSeed -SeedSourceCandidateId 'rc-2026-12-03-ecc9bfaa5bfa'
~~~

## Verdict for the next candidate

A green Windows Full requires native replay of the seven repaired red cases on the integrated candidate. Full currently requires two explicit diagnostics that do not execute in the group launch, and five intended platform skips lack Windows roster entries. Six prior seed identities and the replacement checkpoint dependency witness already have native passing TRX. The harness accepted the twelve exit-8 returns and kept the 28 roster warnings outside its coverage verdict.

## Not done, noted

Plan ownership for the explicit diagnostic admission, the five skip entries, and the per-suite scope of stale-roster diagnostics.
