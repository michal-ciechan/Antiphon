# CARD-1314 Windows duration buckets

Windows per-test durations for the frozen Unit roster are measured from RC2 gate2. The Linux wall is the 30 classes this roster does not run. On the 343 shared classes the same 4,159 cases sum to 438.5s here against the published Linux 562.2s.

No tests were run. No product or test source was edited. The TRX files were read in place.

## Sources

Primary Windows aggregate, read here:

- TRX `C:\src\Antiphon\.antiphon\investigations\rc2-ec89e544\gate2\run.trx`, SHA-256 `e67da70f7e36b18f9a8ca558d9c35a18a8b7173a05be987eaa97a7946388c59e`, matching `hashes.json` and the hash named in `docs/investigations/2026-10-16-card-1314-test-duration-buckets.md`.
- `gate2\audit.json`: 4,537 passed, 0 failed, 0 skipped, 361 classes, `trxWallSeconds` 209.1668147.
- `gate2\timing.json`: outer wall 212.7262762s, exit 0, source `b7265a05aa8773f08dbf457b93c7561e7874822d`. Start `2026-10-16T10:25:30.1641798+00:00`, finish `2026-10-16T10:29:02.890456+00:00`.
- `gate2\stdout.log` line 1: `BUILD SLOT granted lease=04648ca3-d1c3-42dc-ac02-e13b225fd646 waited=0s maxcpucount=4`. Line 5082: `BUILD SLOT released lease=04648ca3-d1c3-42dc-ac02-e13b225fd646 held=212s`.
- `source-binding.json`: `productVersion` `1.0.0+b7265a05aa8773f08dbf457b93c7561e7874822d`, the candidate named in the Linux bucket report.
- TRX `Times` start `2026-10-16T10:25:32.8937549Z`, finish `2026-10-16T10:29:02.0605696Z`, wall 209.1668147s. Counters: `executed=4537`, `passed=4537`, `failed=0`, `notExecuted=0`. Every `UnitTestResult` joined a `TestMethod`. Every `codeBase` file name is `Antiphon.Tests.dll`. Zero durations landed on a bucket edge.

Shape check, same roster, same candidate, same assembly SHA-256 `A7AFBE4A95A89E26DBFE8F7892A03E9C2A4E7A631F47B9DC60463AEC0C114EE1`:

- `gate1\run.trx` SHA-256 `87b7e776e29ebce46e74d1ca2d841c1137eab59f107bf5daa0eac75bdef6a1eb`.
- `gate1\audit.json`: 4,537 passed, 0 failed, 0 skipped, 361 classes, `trxWallSeconds` 226.4145682.
- `gate1\timing.json`: outer wall 336.0864546s, exit 0. `gate1\stdout.log` line 3: `BUILD SLOT granted lease=2a9f228f-68eb-4d9e-9d25-853a67fd043d waited=105s maxcpucount=4`. Line 1 is `reason=memory_floor` at `elapsed=0m`. Gate1 is the shape check. The bucket table below is gate2.

Roster membership is `docs/investigations/2026-10-15-card-1287-linux-unit-roster.txt` (373 names) and `docs/investigations/2026-10-15-card-1287-windows-unit-roster.txt` (361 names). Intersection 343, Linux-only 30, Windows-only 18. Gate2's 361 classes match the Windows roster and `rc2-ec89e544\roster.txt` with no missing and no extra names. Linux figures below are the published ones in `docs/investigations/2026-10-16-card-1314-test-duration-buckets.md`. This pass did not re-parse the Linux TRX.

## How the buckets were cut

Same edges as that report. Each case uses the TRX `duration` attribute, converted to ticks. A case on an edge belongs to the higher bucket. None did.

| Bucket | Rule |
|---|---|
| under 100 ms | `< 00:00:00.1000000` |
| 100 ms to under 1 s | `>= 100 ms` and `< 1 s` |
| 1 s to under 10 s | `>= 1 s` and `< 10 s` |
| 10 s to under 1 min | `>= 10 s` and `< 60 s` |
| 1 min and longer | `>= 60 s` |

Summed duration adds those attributes. Cases overlap. Occupied wall is the merged `startTime`/`endTime` union of the cases in that bucket, truncated to milliseconds, then shown to 0.1s. Bucket occupations overlap, so the occupied-wall column is not a partition of the 209.167s TRX wall. Sums and shares are rounded half away from zero; a 0.1s column can miss the total by 0.1s. Case shares under 0.05% are shown as `<0.1%`.

The result span from the earliest case start to the latest case end is 180.4s. Peak concurrency on that span is 33. Summed time 586.1s over that span is 3.25 cases in flight on average. The recorded gate2 argv does not pass a test-parallelism flag.

## Windows

Project: all 4,537 cases are `Antiphon.Tests`. No SessionRunner, Pty, messaging, E2E, or client project is in this TRX.

| Bucket | Cases | Share of cases | Summed duration | Share of summed time | Occupied wall |
|---|---:|---:|---:|---:|---:|
| under 100 ms | 4101 | 90.4% | 35.3s | 6.0% | 11.6s |
| 100 ms to under 1 s | 324 | 7.1% | 108.2s | 18.5% | 39.4s |
| 1 s to under 10 s | 109 | 2.4% | 292.7s | 49.9% | 84.5s |
| 10 s to under 1 min | 1 | <0.1% | 11.8s | 2.0% | 11.8s |
| 1 min and longer | 2 | <0.1% | 138.1s | 23.6% | 138.1s |

The 4,101 cases under 100 ms are 90.4% of cases, 6.0% of summed time, and 11.6s of occupied wall. The 3 cases at 10s or longer are under 0.1% of cases and 25.6% of summed time (149.9s). Those two minute-scale cases do not overlap each other: their occupied wall equals their sum.

### Where the time sits

| Namespace | Classes | Cases | Summed duration | Share |
|---|---:|---:|---:|---:|
| `Antiphon.Tests.Checkpoints` | 30 | 195 | 141.8s | 24.2% |
| `Antiphon.Tests.Application` | 220 | 2924 | 105.4s | 18.0% |
| `Antiphon.Tests.Scripts` | 5 | 37 | 95.8s | 16.3% |
| `Antiphon.Tests.Agents` | 37 | 347 | 83.2s | 14.2% |
| `Antiphon.Tests.TestHelpers` | 12 | 142 | 59.2s | 10.1% |
| `Antiphon.Tests.AgentTui` | 5 | 143 | 51.9s | 8.9% |
| `Antiphon.Tests.Infrastructure` | 33 | 562 | 31.1s | 5.3% |
| `Antiphon.Tests.Infrastructure.Resilience` | 6 | 46 | 12.3s | 2.1% |
| `Antiphon.TestSupport` | 1 | 1 | 3.0s | 0.5% |
| `Antiphon.Tests.Migrations` | 2 | 4 | 1.9s | 0.3% |
| `Antiphon.Tests` | 4 | 23 | 0.3s | <0.1% |
| `Antiphon.Tests.ApiKeys` | 3 | 52 | 0.1s | <0.1% |
| `Antiphon.Tests.Domain.StateMachine` | 3 | 61 | 0.0s | <0.1% |

The 343 shared classes are 4,159 cases and 438.5s (74.8%). The 18 Windows-only classes are 378 cases and 147.6s (25.2%).

| Class | Cases | Sum | Max | Roster |
|---|---:|---:|---:|---|
| `Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests` | 3 | 76.9s | 76.9s | both rosters |
| `Antiphon.Tests.Checkpoints.TimeoutTests` | 8 | 63.2s | 61.2s | Windows roster only |
| `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` | 65 | 49.5s | 2.6s | Windows roster only |
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` | 25 | 48.9s | 6.6s | both rosters |
| `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` | 10 | 23.6s | 7.2s | both rosters |
| `Antiphon.Tests.Application.ChannelOutboundStorageTests` | 33 | 19.3s | 9.2s | both rosters |
| `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` | 2 | 18.7s | 11.8s | both rosters |
| `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` | 4 | 16.7s | 9.2s | both rosters |
| `Antiphon.Tests.Checkpoints.WaitCommandTests` | 12 | 16.4s | 6.9s | both rosters |
| `Antiphon.Tests.Application.SessionDeliveryProfileTests` | 7 | 13.5s | 2.0s | Windows roster only |
| `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` | 33 | 13.2s | 0.9s | both rosters |
| `Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests` | 12 | 10.9s | 2.1s | both rosters |

Six Windows-only classes account for 142.2s of the 147.6s Windows-only sum. The other 12 Windows-only classes sum to 5.4s. The six are `TimeoutTests` 63.2s, `AgentTuiSecretProtectorTests` 49.5s, `SessionDeliveryProfileTests` 13.5s, `LandingRemovalPolicyControlTests` 5.9s, `DelegationHarnessCensusTests` 5.4s, and `RunSchedulerTests` 4.6s.

Leading classes inside each bucket:

- under 100 ms: `LandingRemovalPolicyControlTests` 65 cases, 1.8s, 5% of the bucket sum; `WorktreeIgnoredContentClassifierTests` 16 cases, 1.0s, 3% of the bucket sum; `DockerStackContractTests` 115 cases, 1.0s, 3% of the bucket sum.
- 100 ms to under 1 s: `WorktreeCleanupPresentationTests` 33 cases, 13.2s, 12% of the bucket sum; `TestClassificationPolicyTests` 14 cases, 8.5s, 8% of the bucket sum; `OrchestratorWorkspaceLayoutTests` 11 cases, 8.1s, 7.5% of the bucket sum.
- 1 s to under 10 s: `AgentTuiSecretProtectorTests` 22 cases, 42.9s, 15% of the bucket sum; `TestClassificationPolicyTests` 8 cases, 40.5s, 14% of the bucket sum; `RunnerClaudeAdapterEffortPromptTests` 8 cases, 23.1s, 8% of the bucket sum.
- 10 s to under 1 min: `ProcessSpawnTimeoutPolicyTests` 1 case, 11.8s, the whole bucket. The case is `Spawn_timeouts_do_not_exceed_ten_minutes`.
- 1 min and longer: `RefreshGithubTokenScriptTests` 1 case, 76.93s, 56% of the bucket sum; `TimeoutTests` 1 case, 61.15s, 44% of the bucket sum.

Gate2 has 16 rows at 5 seconds or longer, in 9 classes: `TestClassificationPolicyTests` 6, `ProcessSpawnTimeoutPolicyTests` 2, `PlanCoverageGoldenTests` 2, and one each in `WaitCommandTests`, `ChannelOutboundStorageTests`, `TimeoutTests`, `PlanCoveragePcTests`, `RunnerClaudeAdapterEffortPromptTests`, and `RefreshGithubTokenScriptTests`. None of those nine names appear in `tests/Antiphon.Tests/slow-tests-allowlist.txt` at `b7265a05aa8773f08dbf457b93c7561e7874822d`.

### Same roster on gate1

| Run | Cases | <100 ms | <1 s | <10 s | <1 min | longer | Summed | Result span |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| gate2 | 4537 | 4101 | 324 | 109 | 1 | 2 | 586.1s | 180.4s |
| gate1 | 4537 | 4080 | 351 | 101 | 3 | 2 | 618.7s | 192.2s |

Both runs have the same two cases at a minute or longer. Gate1's three cases in the 10s bucket are `Spawn_timeouts_do_not_exceed_ten_minutes` 13.40s, `C487_G069(ordinary)` 13.01s, and `Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged` 11.34s. On gate2 the last two fall under 10s (6.59s and 9.24s) and only the spawn-timeout case stays in that bucket (11.80s). The bucket counts move. The minute-scale pair does not.

### Slowest cases

Every case below passed. Durations are rounded half away from zero. The one under-100ms case whose 4-decimal rounding would print `0.1000s` is floored, so it stays visibly inside its bucket (`duration="00:00:00.0999595"`). Two display names contain a newline; the table shows that newline as `\n`. Seventeen display-name groups are repeated across distinct `testId`s (16 pairs and one triple). The buckets count every result. None of those repeated names are in the 5-second tail.

### Slowest 50 of 4101 cases in under 100 ms

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 0.0999s | `C1213_CaseAndUnicode` | `Antiphon.Tests.Application.DelegateBranchNameTests` |
| 2 | 0.0995s | `Model_sql_fragments_have_no_carriage_returns` | `Antiphon.Tests.Infrastructure.AppDbContextModelTests` |
| 3 | 0.0988s | `A_healthy_launch_types_nothing` | `Antiphon.Tests.Agents.RunnerGrokAdapterTrustPromptTests` |
| 4 | 0.0984s | `tracks_literal_helper_arguments_without_executing_source` | `Antiphon.Tests.Checkpoints.PlanCoverageAssertionTests` |
| 5 | 0.0982s | `Explicit_antiphon_patterns_keep_their_bucket(·antiphon/inbox/1e3f·md, disposable)` | `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` |
| 6 | 0.0981s | `C613_AlternateEvidenceRoundTripsWithoutAuthority` | `Antiphon.Tests.Application.TaskCompletionContinuationTests` |
| 7 | 0.0979s | `Output_rename_and_link_swap_after_inspection_seals_the_opened_file` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 8 | 0.0976s | `C1083_OwnersKeepReplyAndReleaseDistinct` | `Antiphon.Tests.Application.RunnerBranchContractDocumentationTests` |
| 9 | 0.0975s | `C1061_Byte_and_text_reads_reject_hard_links` | `Antiphon.Tests.Application.ChannelReplyAttachmentReaderTests` |
| 10 | 0.0974s | `Snapshot_covers_exactly_the_reflected_non_navigation_properties(Antiphon.Server.Domain.Entities.SessionQueuedMessage)` | `Antiphon.Tests.TestHelpers.EntityScalarSnapshotTests` |
| 11 | 0.0965s | `a_declared_path_glob_covers_the_files_it_matches` | `Antiphon.Tests.Application.ScopeDriftTests` |
| 12 | 0.0964s | `Default_protected_paths_are_protected(x·user)` | `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` |
| 13 | 0.0948s | `Same_store_reconnect_consumes_retirement_clear_authorization` | `Antiphon.Tests.Application.PhoneHomeRunnerRetirementIdentityTests` |
| 14 | 0.0946s | `Protected_precedes_evidence_on_overlap` | `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` |
| 15 | 0.0946s | `Configured_protected_names_extend_but_never_replace_the_defaults` | `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` |
| 16 | 0.0945s | `a_path_that_matches_no_area_is_named_verbatim` | `Antiphon.Tests.Application.ScopeDriftTests` |
| 17 | 0.0925s | `Default_disposable_paths_are_disposable(·antiphon/inbox/1e3f·md)` | `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` |
| 18 | 0.0925s | `C650_Configurable_windows_validate_and_change_digest` | `Antiphon.Tests.Application.ExpectationDirectiveTests` |
| 19 | 0.0917s | `RecordIfMissing_does_not_overwrite_existing` | `Antiphon.Tests.CachingChatClientTests` |
| 20 | 0.0915s | `paths_inside_the_declared_areas_record_the_area_and_no_drift` | `Antiphon.Tests.Application.ScopeDriftTests` |
| 21 | 0.0912s | `C1061_Command_timeout_and_pipe_fault_remain_visible` | `Antiphon.Tests.TestHelpers.DirectoryLinkFixtureTests` |
| 22 | 0.0911s | `Dirty_or_active_worktree_is_kept` | `Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests` |
| 23 | 0.0909s | `Default_protected_paths_are_protected(·antiphon/report·md)` | `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` |
| 24 | 0.0904s | `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(no-lease-changed, False)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 25 | 0.0903s | `C1213_HexBounds` | `Antiphon.Tests.Application.DelegateBranchNameTests` |
| 26 | 0.0899s | `A_ref_does_not_widen_the_plan_path_refusal_boundary` | `Antiphon.Tests.Application.PlanCatalogServiceTests` |
| 27 | 0.0890s | `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:Inspected, False)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 28 | 0.0887s | `Teardown_matrix(starting)` | `Antiphon.Tests.TestHelpers.TestDbFixtureLifecycleTests` |
| 29 | 0.0879s | `The_bulleted_header_shape_yields_title_status_date_and_card` | `Antiphon.Tests.Application.PlanCatalogServiceTests` |
| 30 | 0.0875s | `Built_ins_are_always_present` | `Antiphon.Tests.Application.SlashCommandCatalogServiceTests` |
| 31 | 0.0868s | `RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(phase:RecoveryPinned, False)` | `Antiphon.Tests.Application.AgentTaskLandingStateTests` |
| 32 | 0.0868s | `Unavailable_owner_is_reported_without_retry_or_delete` | `Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests` |
| 33 | 0.0867s | `J6_blocked_or_failed_verdict_does_not_match(failed)` | `Antiphon.Tests.Application.DelegateBindRefusalRecoveryTests` |
| 34 | 0.0865s | `Cleared_retirement_preserves_unexpired_registration_lease_then_allows_replacement` | `Antiphon.Tests.Application.PhoneHomeRunnerRetirementIdentityTests` |
| 35 | 0.0863s | `Refusing_owner_is_reported_without_retry_or_delete` | `Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests` |
| 36 | 0.0863s | `Registered_and_unregistered_candidates_are_fully_accounted` | `Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests` |
| 37 | 0.0861s | `C1153_Owner_sentences_match_the_protocol` | `Antiphon.Tests.Application.SessionRunnerAbsenceEvidenceDocumentationTests` |
| 38 | 0.0860s | `Cleared_retirement_waits_a_full_lease_after_a_recent_heartbeat_and_disconnect` | `Antiphon.Tests.Application.PhoneHomeRunnerRetirementIdentityTests` |
| 39 | 0.0860s | `First_use_creates_one_owner_only_random_token_and_later_reads_return_it` | `Antiphon.Tests.Infrastructure.OperatorTokenFileTests` |
| 40 | 0.0859s | `C1213_ParseBothForms` | `Antiphon.Tests.Application.DelegateBranchNameTests` |
| 41 | 0.0859s | `Compose_withholds_the_target_for_unsupported_or_undefined_values(pin-head-kind-999)` | `Antiphon.Tests.Application.RolePolicyReadServiceTests` |
| 42 | 0.0858s | `requires_members_and_collection_predicates` | `Antiphon.Tests.Checkpoints.PlanCoverageAssertionTests` |
| 43 | 0.0853s | `completed_faulted_work_reports_failure_and_deletes_roots` | `Antiphon.Tests.Checkpoints.CheckpointTempScopeTests` |
| 44 | 0.0849s | `Compose_withholds_the_target_for_unsupported_or_undefined_values(pin-grok-inherits-policy-level-999)` | `Antiphon.Tests.Application.RolePolicyReadServiceTests` |
| 45 | 0.0836s | `Captured_sign_in_stays_blocked(200, 60, a15e03781cad5b5833512786eff0c06e5de35c1dbdd3e1f0f5810dac9517a3dc)` | `Antiphon.Tests.Agents.GrokStartupSizeTests` |
| 46 | 0.0835s | `C448_V36_EachAuthorityCoordinatePrecedesMutation(operation-namespace)` | `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` |
| 47 | 0.0832s | `windows_separators_and_leading_dot_slashes_are_normalised` | `Antiphon.Tests.Application.ScopeDriftTests` |
| 48 | 0.0832s | `Classifier_preserves_guarded_classes_and_release_requirements` | `Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests` |
| 49 | 0.0823s | `C650_Rejects_invalid_and_duplicate_directives` | `Antiphon.Tests.Application.ExpectationDirectiveTests` |
| 50 | 0.0822s | `Source_zip_manifest_rejects_unsafe_or_ambiguous_members(missing_entry)` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |

### Slowest 50 of 324 cases in 100 ms to under 1 s

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 0.9936s | `native_failure_in_middle_ordinal_survives_later_passes` | `Antiphon.Tests.Checkpoints.CheckpointRepeatHostTests` |
| 2 | 0.9758s | `C487_G073(full)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 3 | 0.9594s | `Sibling_dedicated_layout_is_Dedicated_when_the_precondition_holds(Grok)` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 4 | 0.9349s | `C665_UnlistedLinkIsRemovedWithoutTraversal` | `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` |
| 5 | 0.9271s | `C443_D9_PriorCaptureKeepsItsIdentityAndAvailability(mismatch)` | `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` |
| 6 | 0.9245s | `Nested_checkout_under_the_orch_folder_is_DedicatedNested_for_Claude` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 7 | 0.9149s | `Claude_declined_external_includes_is_DedicatedUnapproved_not_absent` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 8 | 0.9135s | `C665_JunctionSwappedInBetweenReadingsRefuses` | `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` |
| 9 | 0.9121s | `returns_75_with_progress_at_max_wait` | `Antiphon.Tests.Checkpoints.WaitCommandTests` |
| 10 | 0.9072s | `returns_6_when_executor_died` | `Antiphon.Tests.Checkpoints.WaitCommandTests` |
| 11 | 0.9028s | `State_table_and_session_column_match_the_snapshot` | `Antiphon.Tests.Infrastructure.OrchestratorInstructionsMigrationShapeTests` |
| 12 | 0.8986s | `Sibling_dedicated_layout_is_DedicatedUnapproved_without_the_precondition(Claude)` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 13 | 0.8868s | `C487_G074` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 14 | 0.8863s | `C487_G075` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 15 | 0.8696s | `RuleA_no_hand_rolled_DelegationWorktreeService_scoped_registration` | `Antiphon.Tests.TestHelpers.DelegationHarnessCensusTests` |
| 16 | 0.8690s | `Sibling_dedicated_layout_is_Dedicated_when_the_precondition_holds(Claude)` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 17 | 0.8545s | `C665_JunctionAtFirstReadingRefusesBeforeRetention` | `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` |
| 18 | 0.8480s | `C487_G076(missing)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 19 | 0.8424s | `Backoff_stays_inside_the_delay_cap` | `Antiphon.Tests.Infrastructure.Resilience.HttpReadResilienceTests` |
| 20 | 0.8292s | `X509_store_selection_disposes_unselected_candidates` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 21 | 0.8012s | `Configured_repository_logs_neither_secrets_nor_key_storage_details` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 22 | 0.7955s | `C487_G076(empty)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 23 | 0.7888s | `C1059_Source_reads_reject_directory_links` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 24 | 0.7856s | `C443_DiagnosticEnvelopeFitsFallbackUtf8(False, 0)` | `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` |
| 25 | 0.7850s | `Sibling_dedicated_layout_is_Dedicated_when_the_precondition_holds(Codex)` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 26 | 0.7776s | `Near_default_wire_cap_accepts_exact_serialized_size_and_refuses_one_more_byte` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 27 | 0.7759s | `C487_G076(malformed)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 28 | 0.7722s | `windows_chatty_row_drains_interleaved_stdout_and_stderr` | `Antiphon.Tests.Checkpoints.TimeoutTests` |
| 29 | 0.7655s | `C1213_DocsCiteGitCommonAndBothBranchForms` | `Antiphon.Tests.Application.RunnerBranchContractDocumentationTests` |
| 30 | 0.7625s | `Retry_after_delta_is_the_wait` | `Antiphon.Tests.Infrastructure.Resilience.HttpReadResilienceTests` |
| 31 | 0.7483s | `Repository_path_boundary_rechecks_links_and_rejects_directories` | `Antiphon.Tests.Application.AgentTaskDecisionQuestionTests` |
| 32 | 0.7463s | `Sibling_dedicated_layout_is_DedicatedUnapproved_without_the_precondition(Codex)` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 33 | 0.7335s | `C665_PathRecreatedBeforeUnregistrationIsNeverTouched` | `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` |
| 34 | 0.7269s | `An_unknown_ref_is_not_found` | `Antiphon.Tests.Application.PlanCatalogServiceTests` |
| 35 | 0.7248s | `create_run_preserves_finished_and_live_sibling_tool_copies` | `Antiphon.Tests.Checkpoints.CheckpointAppTests` |
| 36 | 0.7057s | `Restored_key_file_symlink_is_rejected_without_mutating_target` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 37 | 0.7032s | `C443_D9_DiagnosticsSurviveEitherEnvelopeExtreme(True, 32, 189, True, True)` | `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` |
| 38 | 0.6990s | `C487_G063` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 39 | 0.6751s | `C443_D9_DiagnosticsSurviveEitherEnvelopeExtreme(True, 1, 14, True, True)` | `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` |
| 40 | 0.6642s | `C443_BoundedCapturePreservesQualifyingTail(33, True)` | `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` |
| 41 | 0.6573s | `C443_BoundedCapturePreservesQualifyingTail(32, True)` | `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` |
| 42 | 0.6475s | `create_run_uses_the_manifest_results_root` | `Antiphon.Tests.Checkpoints.CheckpointAppTests` |
| 43 | 0.6454s | `X509_private_key_must_be_outside_content_root_and_key_ring(False)` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 44 | 0.6450s | `A_composer_that_will_not_clear_is_not_ready` | `Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests` |
| 45 | 0.6439s | `Image_version_row_accepts_only_the_successful_qualified_version(grok 1·0·41 (abcdef123456) [stable], 1, fail)` | `Antiphon.Tests.Infrastructure.GrokRunnerImageContractTests` |
| 46 | 0.6364s | `Git_checkout_with_instruction_files_is_CheckoutAsCwd` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 47 | 0.6364s | `Git_checkout_with_no_instruction_files_is_Unconfigured` | `Antiphon.Tests.Application.OrchestratorWorkspaceLayoutTests` |
| 48 | 0.6199s | `Repository_delete_callback_failures_are_not_sanitized` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 49 | 0.6168s | `Remote_adapter_reaches_the_replacement_connection_after_a_reconnect` | `Antiphon.Tests.Application.PhoneHomeDirectoryTests` |
| 50 | 0.6048s | `Shell_scripts_are_forced_lf_by_gitattributes` | `Antiphon.Tests.Infrastructure.ShellFixtureLineEndingContractTests` |

### Slowest 50 of 109 cases in 1 s to under 10 s

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 9.484s | `c983_driver_aliases_bind_on_the_exit_assertion` | `Antiphon.Tests.Checkpoints.PlanCoveragePcTests` |
| 2 | 9.239s | `Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 3 | 9.236s | `c866_fixed_checks_clear_same_obligations` | `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` |
| 4 | 7.206s | `Disabling_the_probe_does_not_disable_effort_resolution(True)` | `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` |
| 5 | 6.879s | `Spawning_classes_carry_a_class_timeout` | `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` |
| 6 | 6.870s | `completed_wait_requires_report_receipts` | `Antiphon.Tests.Checkpoints.WaitCommandTests` |
| 7 | 6.588s | `C487_G065` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 8 | 6.585s | `C487_G069(ordinary)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 9 | 6.441s | `C487_G067(inherits)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 10 | 6.372s | `C487_G069(optin)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 11 | 6.221s | `C487_G066` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 12 | 5.873s | `C487_G067(no-inherits)` | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` |
| 13 | 5.521s | `c835_eight_pc_risks_are_visible` | `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` |
| 14 | 4.988s | `log_io_failure_publishes_a_terminal_error_without_recursive_logging` | `Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests` |
| 15 | 4.755s | `A_trust_dialog_that_will_not_clear_fails_the_launch_with_a_named_block` | `Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests` |
| 16 | 4.588s | `terminal_report_waits_for_all_callbacks_and_log_flush` | `Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests` |
| 17 | 4.271s | `Source_zip_expansion_accepts_exact_budget_and_refuses_one_extra_byte` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 18 | 4.181s | `Zero_queue_limiter_rejects_without_retry_and_database_scope_is_disposed_before_delay` | `Antiphon.Tests.Infrastructure.Resilience.ResilienceCircuitTests` |
| 19 | 4.169s | `namespace_census_matches_compiled_checkpoint_cases` | `Antiphon.Tests.Checkpoints.CheckpointNamespaceCensusUsageTests` |
| 20 | 3.979s | `A_session_that_never_visibly_works_reports_the_turn_incomplete_instead_of_scraping_the_status_bar` | `Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests` |
| 21 | 3.854s | `Attach_without_launch_effort_preserves_current` | `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` |
| 22 | 3.644s | `no_Check_comparison_survives_outside_the_allowlist` | `Antiphon.Tests.Application.SpecialistRoleContractTests` |
| 23 | 3.610s | `Model_has_the_evidence_and_state_columns_and_the_migration_adds_only_them` | `Antiphon.Tests.Application.HerdrSupervisionFailureEvidenceTests` |
| 24 | 3.466s | `The_screen_fallback_completes_when_the_working_indicator_appeared_and_then_went` | `Antiphon.Tests.Agents.RunnerCodexAdapterTurnCompleteTests` |
| 25 | 3.336s | `Concurrent_first_use_readers_all_get_the_one_token_and_none_throw` | `Antiphon.Tests.Infrastructure.OperatorTokenFileTests` |
| 26 | 3.129s | `A_painted_but_deaf_tui_is_not_ready` | `Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests` |
| 27 | 3.089s | `Enter_is_not_the_affirmative_key` | `Antiphon.Tests.Agents.RunnerGrokAdapterTrustPromptTests` |
| 28 | 3.045s | `A_trust_dialog_that_does_not_clear_is_not_ready` | `Antiphon.Tests.Agents.RunnerGrokAdapterTrustPromptTests` |
| 29 | 3.015s | `Registry_matches_compiled_metadata` | `Antiphon.TestSupport.TestClassificationGuardTests` |
| 30 | 2.999s | `Inline_and_zip_sources_share_the_expanded_budget` | `Antiphon.Tests.Application.ChannelOutboundStorageTests` |
| 31 | 2.627s | `A_dialog_appearing_during_the_minimum_floor_is_resolved` | `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` |
| 32 | 2.553s | `Configured_protector_rejects_unreadable_persisted_key_material` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 33 | 2.508s | `RuleB_dispatcher_harnesses_call_AddDelegationWorktreeGraph` | `Antiphon.Tests.TestHelpers.DelegationHarnessCensusTests` |
| 34 | 2.501s | `Configured_protector_rejects_replaced_persisted_key_material` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 35 | 2.487s | `Protect_rejects_payload_whose_key_appears_and_disappears_during_cryptography` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 36 | 2.467s | `Configured_protector_rejects_corrupt_extra_key_xml` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 37 | 2.439s | `Provider_with_a_missing_key_ring_cannot_decrypt` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 38 | 2.387s | `Workflow_calls_real_guard_and_propagates_failure` | `Antiphon.Tests.Infrastructure.EvidencePolicyWorkflowTests` |
| 39 | 2.366s | `Configured_X509_protector_rechecks_current_user_store_certificate` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 40 | 2.364s | `Configured_protector_allows_and_observes_initial_key_generation` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 41 | 2.320s | `Unready_key_protection_rejects_even_a_preexisting_unprotected_key_ring` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 42 | 2.194s | `native_skip_or_missing_markers_cannot_certify_five` | `Antiphon.Tests.Checkpoints.CheckpointRepeatHostTests` |
| 43 | 2.189s | `Configured_X509_key_protection_survives_provider_restart` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 44 | 2.169s | `Disabling_the_probe_does_not_disable_effort_resolution(False)` | `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` |
| 45 | 2.138s | `parenthesized_class_filter_runs_both_methods_on_a_real_host` | `Antiphon.Tests.Checkpoints.RerunFilterHostTests` |
| 46 | 2.135s | `Cursor_moves_in_echo_keep_the_plain_stripping_verdict(False, Run a\u001b[1;6Hbc?\nDone·)` | `Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests` |
| 47 | 2.127s | `Cursor_moves_in_echo_keep_the_plain_stripping_verdict(False, Run \u001b[1Cabc?\nDone·)` | `Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests` |
| 48 | 2.124s | `The_corrected_screen_done_line_completes_when_no_transcript_exists` | `Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests` |
| 49 | 2.099s | `Configured_protector_rejects_oversized_key_ring_xml` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |
| 50 | 2.096s | `Restored_key_ring_with_permissive_file_acl_is_rejected_without_repair` | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` |

### The 1 case at 10 s to under 1 min

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 11.80s | `Spawn_timeouts_do_not_exceed_ten_minutes` | `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` |

### All 2 cases at 1 min and longer

| # | Duration | Case | Class |
|---:|---:|---|---|
| 1 | 76.93s | `Heredoc_fed_docker_exec_keeps_stdin_open` | `Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests` |
| 2 | 61.15s | `windows_row_timeout_kills_the_start_b_grandchild` | `Antiphon.Tests.Checkpoints.TimeoutTests` |

## Comparison with the Linux roster

Linux summed 2970.2s across 4,527 cases. Windows gate2 summed 586.1s across 4,537 cases. The TRX walls are 2175.170s and 209.167s. The gap is membership, measured on both sides.

### Linux-only script and checkpoint classes

All 30 Linux-only roster names have zero rows in gate1 and in gate2. Fifteen are checkpoint classes and six are script classes:

- Scripts: `HostJqPrerequisiteScriptTests`, `RefreshGithubTokenRemoteCommandTests`, `RetiredTempContainerHostTests`, `RetiredTempContainerScriptTests`, `RollingProductionMountTests`, `RollingVolumeRecycleCensusChurnTests`.
- Checkpoints: `CheckpointBuildBindingPathTests`, `CheckpointConfinementLedgerTests`, `CheckpointFixtureProcessTests`, `CheckpointOwnedOutputTests`, `CheckpointShapeConflictTests`, `CheckpointTaskOwnershipTests`, `CheckpointToolCopyCleanupTests`, `CheckpointTrackedBaselineTests`, `DetachedLauncherTests`, `PlanCoverageBindingTests`, `PlanCoverageCensusTests`, `PlanCoverageCommandTests`, `PlanCoverageHandleTests`, `RowRunnerTests`, `SafeDeleteProcessBoundaryTests`.

The other nine are also absent: `CheckpointToolProjectContractTests`, `CodexRootExecContractTests`, `CodexRunnerImageContractTests`, `GitMinimumVersionGuardTests`, `GithubCredentialHelperTests`, `GrokSha256PinTests`, `JqRunnerImageContractTests`, `OrchestratorInstructionsRendererTests`, `ControlledLandingGitTests`.

The Linux report gives those 30 classes 368 cases and 2407.9s, 81.1% of the Linux sum. Eleven of them are the published class leaders. Their published sums add to 2378.0s, so the other 19 Linux-only classes are the remaining 29.9s.

| Class | Linux cases | Linux sum | Linux max | Windows rows |
|---|---:|---:|---:|---|
| `Antiphon.Tests.Scripts.RetiredTempContainerHostTests` | 21 | 724.5s | 108.0s | 0 |
| `Antiphon.Tests.Scripts.HostJqPrerequisiteScriptTests` | 39 | 473.5s | 129.1s | 0 |
| `Antiphon.Tests.Checkpoints.PlanCoverageBindingTests` | 6 | 296.1s | 176.8s | 0 |
| `Antiphon.Tests.Checkpoints.CheckpointTrackedBaselineTests` | 13 | 235.9s | 36.2s | 0 |
| `Antiphon.Tests.Checkpoints.CheckpointShapeConflictTests` | 17 | 223.9s | 25.0s | 0 |
| `Antiphon.Tests.Scripts.RetiredTempContainerScriptTests` | 13 | 187.6s | 66.1s | 0 |
| `Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests` | 16 | 72.0s | 17.5s | 0 |
| `Antiphon.Tests.Scripts.RollingProductionMountTests` | 12 | 63.1s | 11.1s | 0 |
| `Antiphon.Tests.Checkpoints.PlanCoverageCommandTests` | 10 | 42.6s | 11.5s | 0 |
| `Antiphon.Tests.Infrastructure.CodexRootExecContractTests` | 4 | 35.5s | 35.4s | 0 |
| `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` | 24 | 23.3s | 10.1s | 0 |

`Antiphon.Tests.Scripts` on Linux is 11 classes, 126 cases, 1493.2s. On this Windows roster it is 5 classes, 37 cases, 95.8s, and 76.9s of that is `RefreshGithubTokenScriptTests`. `Antiphon.Tests.Checkpoints` on Linux is 40 classes, 313 cases, 971.5s. Here it is 30 classes, 195 cases, 141.8s, and 63.2s of that is the Windows-only `TimeoutTests`.

### Shared classes

The 343 shared classes are 4,159 cases on both hosts. Linux summed 562.2s. Gate2 summed 438.5s, 78.0% of that Linux figure. Case counts match on every class in the table. Gate1 is the second Windows run.

| Class | Cases | Linux sum | Linux max | Gate2 sum | Gate2 max | Gate1 sum | Gate1 max |
|---|---:|---:|---:|---:|---:|---:|---:|
| `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` | 25 | 69.3s | 13.4s | 48.9s | 6.6s | 61.2s | 13.0s |
| `Antiphon.Tests.Application.WorktreeCleanupPresentationTests` | 33 | 60.8s | 2.0s | 13.2s | 0.9s | 11.4s | 0.6s |
| `Antiphon.Tests.Application.ChannelOutboundStorageTests` | 33 | 31.3s | 20.7s | 19.3s | 9.2s | 23.0s | 11.3s |
| `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` | 10 | 26.8s | 7.7s | 23.6s | 7.2s | 24.7s | 7.4s |
| `Antiphon.Tests.Scripts.ProcessSpawnTimeoutPolicyTests` | 2 | 26.1s | 16.5s | 18.7s | 11.8s | 22.5s | 13.4s |
| `Antiphon.Tests.Checkpoints.PlanCoverageGoldenTests` | 4 | 25.8s | 14.6s | 16.7s | 9.2s | 17.3s | 9.6s |
| `Antiphon.Tests.Application.WorktreeIgnoredContentClassifierTests` | 46 | 17.5s | 0.6s | 6.6s | 0.4s | 6.1s | 0.3s |
| `Antiphon.Tests.Scripts.RefreshGithubTokenScriptTests` | 3 | 12.9s | 12.9s | 76.9s | 76.9s | 85.0s | 85.0s |
| `Antiphon.Tests.Agents.RunnerGrokAdapterTurnCompleteTests` | 12 | 12.3s | 2.5s | 10.9s | 2.1s | 10.7s | 2.1s |

Windows-fast, on both gate runs, against the published Linux sums:

- `WorktreeCleanupPresentationTests`: 13.2s and 11.4s against 60.8s. Max 0.9s and 0.6s against 2.0s. All 33 cases stay under 1s here; the Linux report puts 30 of them in the 1s-to-10s bucket at 59.1s.
- `WorktreeIgnoredContentClassifierTests`: 6.6s and 6.1s against 17.5s. Max stays under 1s on both hosts (0.4s and 0.3s against 0.6s).
- `ChannelOutboundStorageTests`: 19.3s and 23.0s against 31.3s. `Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged` is 9.24s and 11.34s against 20.68s.
- `C458_EfMappingsStoreNullableTextWithStableWorkspaceOrdinals`: 0.002s and 0.001s against 4.116s. At `b7265a05` the method builds an EF model with `UseNpgsql` and an unused connection string, then reads column metadata (`tests/Antiphon.Tests/Application/WorkerWorkspaceDefaultStorageTests.cs:39-49`).

Same order of magnitude on both hosts:

- `TestClassificationPolicyTests`: 48.9s and 61.2s against 69.3s. Gate1 max is 13.0s against the Linux max 13.4s; gate2 max is 6.6s.
- `RunnerClaudeAdapterEffortPromptTests`: 23.6s and 24.7s against 26.8s. `Disabling_the_probe_does_not_disable_effort_resolution(True)` is 7.206s and 7.410s against the published 7.705s.
- `ProcessSpawnTimeoutPolicyTests`: 18.7s and 22.5s against 26.1s.
- `PlanCoverageGoldenTests`: 16.7s and 17.3s against 25.8s.
- `RunnerGrokAdapterTurnCompleteTests`: 10.9s and 10.7s against 12.3s.

Windows-slow on the shared roster:

- `RefreshGithubTokenScriptTests`: 76.9s and 85.0s against 12.9s. The whole sum is `Heredoc_fed_docker_exec_keeps_stdin_open` (76.93s and 84.96s against 12.88s). At `b7265a05` that method scans every `docs/**/*.md` for a `docker exec` heredoc (`tests/Antiphon.Tests/Scripts/RefreshGithubTokenScriptTests.cs:69`, `:86`). Both Windows runs passed.

Windows time that the Linux roster never includes:

- `TimeoutTests.windows_row_timeout_kills_the_start_b_grandchild`: 61.15s on gate2 and 61.49s on gate1. At `b7265a05` the test skips unless `OperatingSystem.IsWindows()` and waits on `TimeSpan.FromMinutes(1)` (`tests/Antiphon.Tests/Checkpoints/TimeoutTests.cs:174`, `:177`, `:189`). Both runs passed, and both landed on that minute.
- `AgentTuiSecretProtectorTests`: 65 cases, 49.5s, max 2.6s. It is spread across the 1s-to-10s bucket (22 cases, 42.9s), not one long case.

## Not done, noted

No allowlist row, Slow retag, or timing-record writer was added.

## Uncertainties

- Occupied wall truncates the merged union to milliseconds.
- `duration` is the TRX case duration. Gate2's slot wait is 0s. Gate1's slot wait is 105s. Gate1's outer wall is 336.086s and its TRX wall is 226.415s.
- Gate1 and gate2 move `C487_G069(ordinary)` and the 14 MiB staging case across the 10s edge. The two minute-scale cases stay put. Two runs are not a percentile.
- The Linux class sums are the published RC2 figures. This pass did not re-parse the Linux TRX.
- `C458_EfMappingsStoreNullableTextWithStableWorkspaceOrdinals` is model inspection. The 4.116s Linux duration and the 0.002s Windows duration are the same method; this pass does not isolate first-use model compilation.
- Seventeen gate2 display names are shared by more than one `testId`. Buckets keep every result. Joining on display name alone drops 18 rows. None of those names are in the 5-second tail.
- SessionRunner, Pty, messaging, E2E, and client suites are outside both frozen rosters.

