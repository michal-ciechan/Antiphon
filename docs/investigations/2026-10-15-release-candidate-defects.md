# Release candidate 2026-10-15 defects and incomplete gates

Candidate source: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
No product/test fixes were made. This file is the caller's repair fan-out input.
Results below are observations, with causal source attribution distinguished from
historical execution. All pieces finished. E2E has 54 passes, 110 failures and 3 required skips.

## D-1 — Checkpoint tool dependency contract assertion

- Step/class: Linux roster; `Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests`.
- Test: `csproj_has_no_project_references`, source line 22.
- Exact assertion message:

```text
ShouldAssertException: DockerStackDocuments.Read("tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj")
    should not contain (case insensitive comparison)
"<ProjectReference"
    but was actually
"<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFra..."
```

- Actual offending input: `<ProjectReference Include="../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj" />`.
- Classification: **regression in the retained contract test / architectural contract mismatch**, reproduced once in class isolation; not a demonstrated product runtime failure or a flake. Full roster: 4,515 pass / 1 fail / 0 skip. Isolated class: 3 pass / 1 fail / 0 skip, same assertion, exit 2, 24.027s outer wall including 15s slot wait.
- Introducing input commit: `367c8ee4cdcc877aa71f1a46d30c410d628edc3b`, CARD-0987 S1a0. Git blame and its patch identify the new reference; the morning baseline lacks it. The refresh plan explicitly calls for a one-way tool-to-HostCleanup reference. This is source attribution, not a historical checkout execution or bisection.
- Smallest proposed fix: reconcile the test with the approved one-way dependency, asserting the exact permitted HostCleanup reference and preventing unintended graph expansion. Preserve the required dependency; verify tool packaging/installability if the old no-reference contract was protecting standalone distribution. Do this in a separately commissioned Code task, then ordinary Review/land and a new candidate.
- Evidence: `/work/evidence/task-1c14bf28/raw/roster/run.trx`, `/work/evidence/task-1c14bf28/raw/rerun-project-contract/run.trx`, and `project-reference-introduction.patch` under the same raw root.

## D-2 — Stack smoke cannot enumerate Linux listeners

- Step: `pwsh -NoProfile -File verify-dev-stack.ps1 -SkipBrowser`, through a granted host slot.
- Class: script/platform environment; `verify-dev-stack.ps1:79`.
- Exact error: `The term 'Get-NetTCPConnection' is not recognized as a name of a cmdlet, function, script file, or executable program. Check the spelling of the name, or if a path was included, verify that the path is correct and try again.`
- Classification: **environment / Windows-only smoke driver on native Debian**, exit 1 after 4.120s outer wall. No product regression is established.
- Introducing line commit: `b7e17830250947ebee43e2da63db0a5302bb283b` (Windows smoke implementation; no card established). This is source attribution, not a historical execution.
- Smallest proposed fix: a separately commissioned portable listener probe and explicit isolated endpoint parameters, retaining the Windows path. Without that work, run this smoke on its intended Windows stack and use isolated E2E for Linux application checks.
- Fixed production ports are hard-coded and there is no isolated-port option. No shared production stack was started, stopped or restarted. The HTTP-health portion was not reached and remains incomplete.
- Evidence: `/work/evidence/task-1c14bf28/raw/smoke/console.log` and `result.json`.

## D-3 — Stack smoke expects a dev Postgres container absent here

- Step/check: stack smoke, `Postgres container`.
- Exact result: `Postgres container           FAIL  not Up — check 'docker ps -a --filter name=antiphon-postgres'`.
- Classification: **environment**, not a demonstrated product regression. Docker daemon and owned disposable-network checks passed. The host's testcontainers do not supply the specifically named Aspire dev container.
- Introducing commit/card: no causal product commit established; this host does not run the documented Windows Aspire dev stack.
- Smallest proposed fix: make an isolated smoke accept explicit owned stack endpoints/resources, or collect the intended Windows-stack receipt. Do not create or recycle the shared production stack for this task.
- Evidence: the same smoke console. This failure was observed before D-2 terminated the script; neither is a passing stack-smoke receipt.

## D-4 — Windows-only land delivery fixture (69 failed cases)

Classification: **environment**, not a Linux product regression. These cases fail the shared fixture admission before their delivery bodies. Exact error:

```text
ShouldAssertException: OperatingSystem.IsWindows()
    should be
True
    but was
False
```

Source: `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:59`. The line originates in `96b5f4d9c5b01f3312b07b9e5dc519ffceda8b49` (native Land delivery fixture; CARD-0467 workflow). Smallest action: execute these native requirements on Windows with staged ConPTY/FakeGrok; a Linux port is separate work, not a skip/waiver. No introducing regression in this cut is established. Affected class and case identities:

- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(already-present)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C498_FailureOutcomeReachesCaller(False)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalPollingCannotConfirm`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalReceiptSaveFailureNeverRetypes`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalQueueInsertCrashReusesRow`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(old-time)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V24_BlockedWriterThenReleaseDeliversBothNotes`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V26_HardCrashAfterQueueInsertReusesRow`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V27_LostRequestWakeupRecoversAtBoot`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V28_LostFlushWakeupRecoversOnIdleCaller`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V30_ReceiptSaveFailureNeverRetypes(receipt)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V30_ReceiptSaveFailureNeverRetypes(verdict)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(landed)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(old-sequence)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(partial-middle)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(wrong-sha)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(wrong-id)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(wrong-session)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(QueuedUserPrompt)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence(Sent-only)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalBusyCallerDoesNotBlock`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix(busy, raw)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C498_FailureOutcomeReachesCaller(True)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewEvidenceCrashRecovers`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V23_BusyCallerDoesNotBlockAnotherLand`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix(idle, spill)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalDeliveryCrashMatrix`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(prompt-before-verdict)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(before-typing)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalLostFlushRecovers`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(queue-committed-before-wakeup)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(queue-inserted-before-outbox-link)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(queue-before-commit)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(enqueue-exception)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(settlement-before-commit)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(settlement-before-save)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(residue-cleanup)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix(busy, spill)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix(idle-lost-wakeup)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(preoperation-refusal)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix(busy, Apply)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalEnqueueFailureRecovers`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix(idle, Apply)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ApprovalOutcomeReceiptMatrix(stale-A)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(attempt)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix(idle, raw)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V31_EnqueueFailureRecoversAutomatically`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(conflict)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V29_RealOutcomeProducerMatrix(operation-refusal)`
- `Antiphon.E2E.AgentTaskLandDeliveryE2ETests.C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(busy)`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_CollapsedWarningsWaitForBusyCaller`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_PreTypingCrashRecoversWarnings`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_QueueInsertCrashReusesRows`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningWaitsForCaller`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_ReceiptCrashDoesNotRetype`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_PostPromptCrashDoesNotRetype`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_ProjectionCrashRecoversOriginalPairs`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_PreEnqueueCrashRecoversWarnings`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_ClaimCrashRecoversCollapsedWarnings`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_CollapsedWarningsReachIdleCaller`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C508_MismatchWarningReachesCaller`
- `Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests.C540_EnqueueFailureRetriesWarnings`
- `Antiphon.E2E.WorktreeRetirementDeliveryE2ETests.C459_LaunchReservationRecoveryRetainsOwnership(lost-enqueue)`
- `Antiphon.E2E.WorktreeRetirementDeliveryE2ETests.C459_AdmittedWorkspaceInputReachesRecipient(idle)`
- `Antiphon.E2E.WorktreeRetirementDeliveryE2ETests.C459_AdmittedWorkspaceInputReachesRecipient(busy-then-released)`
- `Antiphon.E2E.WorktreeRetirementDeliveryE2ETests.C459_AdmittedWorkspaceInputReachesRecipient(claim-first)`
- `Antiphon.E2E.WorktreeRetirementDeliveryE2ETests.C459_LaunchReservationRecoveryRetainsOwnership(unknown-start)`

## D-5 — Chromium native library absent (35 failed cases)

Classification: **environment**. npm ci/build/lint, E2E build and generated Chromium installation all passed; browser launch does not. Exact hook and leaf errors:

```text
BeforeTestException: BeforeTest hook failed: Target page, context or browser has been closed
error while loading shared libraries: libglib-2.0.so.0: cannot open shared object file: No such file or directory
```

Source: `PlaywrightFixture.InitializeAsync`; no causal product introducing commit/card is established for this host prerequisite. Smallest action: provision the complete Playwright Chromium OS dependencies in the test host/image and prove a headless launch, then collect fresh E2E evidence. No browser repair, fixture bypass or host package installation was made in this pass. Full launch errors are retained in the TRX. Affected class and case identities:

- `Antiphon.E2E.AgentE2ETests.Agents_page_creates_agent_and_assigns_card_to_queue`
- `Antiphon.E2E.BoardE2ETests.Board_user_can_review_real_worktree_diff_and_send_inline_comment`
- `Antiphon.E2E.BoardE2ETests.Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session`
- `Antiphon.E2E.BoardE2ETests.Board_running_session_shows_stop_button`
- `Antiphon.E2E.BoardE2ETests.Board_failed_claude_resume_missing_session_shows_recovery_actions`
- `Antiphon.E2E.BoardE2ETests.Board_stopped_claude_session_shows_terminal_overlay_and_resume_button`
- `Antiphon.E2E.BoardE2ETests.Board_user_can_edit_workflow_md_and_reload_persists_version`
- `Antiphon.E2E.BoardE2ETests.Board_spawn_reuses_existing_worktree_outside_current_worktree_root`
- `Antiphon.E2E.BoardE2ETests.Board_manual_xterm_command_marks_session_as_streaming_turn`
- `Antiphon.E2E.BoardE2ETests.Board_user_can_drag_card_between_columns_and_reload_persists_move`
- `Antiphon.E2E.BoardE2ETests.Board_user_can_create_board_create_card_and_open_card_modal`
- `Antiphon.E2E.DirectoryAutocompleteE2ETests.Working_directory_shows_autocomplete_hints_for_typed_path`
- `Antiphon.E2E.DispatchConcurrencySettingsE2ETests.Project_override_clear_and_live_receipts_preserve_inheritance`
- `Antiphon.E2E.OrchestratorE2ETests.Orchestrator_user_can_view_snapshot_and_pause_resume`
- `Antiphon.E2E.PipelineGlanceE2ETests.C557_combined_glance_on_phone`
- `Antiphon.E2E.ProjectDeleteE2ETests.Deleting_a_project_that_owns_a_board_is_a_409_not_a_500`
- `Antiphon.E2E.ProjectDeleteE2ETests.An_empty_project_deletes_without_force`
- `Antiphon.E2E.ProjectDeleteE2ETests.Forced_delete_takes_the_board_and_its_cards_with_it`
- `Antiphon.E2E.ProjectDeleteE2ETests.Deletion_impact_reports_what_would_be_destroyed`
- `Antiphon.E2E.ProjectDeleteE2ETests.Deleting_the_last_board_deletes_its_project`
- `Antiphon.E2E.ProjectDeleteE2ETests.Project_delete_dialog_warns_about_attached_work_before_destroying_it`
- `Antiphon.E2E.ProjectDeleteE2ETests.Deleting_one_of_two_boards_leaves_the_project_and_the_other_board`
- `Antiphon.E2E.ProjectDeleteE2ETests.Board_can_be_deleted_from_the_project_screen_and_warns_when_it_is_the_last_one`
- `Antiphon.E2E.SmokeE2ETests.Full_stack_smoke_test_browser_to_api_to_db`
- `Antiphon.E2E.SmokeE2ETests.Health_endpoint_returns_ok_via_http_client`
- `Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_via_api_returns_no_content`
- `Antiphon.E2E.WorkflowDeleteTests.Delete_running_workflow_returns_error_status`
- `Antiphon.E2E.WorkflowDeleteTests.Create_workflow_sanitizes_special_chars_in_branch_name`
- `Antiphon.E2E.WorkflowDeleteTests.Create_workflow_uses_antiphon_prefixed_branch_name`
- `Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_button_shows_confirm_and_removes_card`
- `Antiphon.E2E.WorkflowDeleteTests.Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard`
- `Antiphon.E2E.WorkflowOutputTests.Artifact_content_endpoint_returns_output_content`
- `Antiphon.E2E.WorkflowOutputTests.Outputs_tab_shows_artifacts_after_workflow_completes`
- `Antiphon.E2E.WorkflowOutputTests.Stage_execution_stores_output_content_in_db`
- `Antiphon.E2E.WorkflowOutputTests.Completed_workflow_artifacts_api_returns_entries`

## D-6 — Three required F-5 canaries skip under the automated environment

Class: `Antiphon.E2E.ChannelOutboundIsolatedTests`. Classification: **regression in verification disposition / incomplete coverage**, with an unavailable required opt-in environment. Exact skip reason: `F-5 requires all three isolated opt-ins.` All three tests call `RequireOptIn()`, which requires `ANTIPHON_HEADED_TESTS`, `ANTIPHON_BROKER_TESTS` and `ANTIPHON_CHANNEL_OUTBOUND_ISOLATED` equal to `1`. The automated safe environment clears headed/live inputs and the RC exclusions contain no row for this class.

The guard and composed fixture originate in `e28d079bc78597dd74e3cdc98a349329e9a616c2` (isolated F-5 / CARD-0418 source); it postdates RC profile commit `4c0d5dd28d8af2783f9b3ea985fcab7e355b8124` (CARD-0599). This is source-history attribution, not a historical gate replay. Smallest fix: have the policy and canary owners explicitly bind the separate headed/Linux canary obligation and an owned, reasoned profile exclusion, or provide a truly automated refusing variant. Preserve the separate canary obligation and update the policy hash; do not clear this candidate by silently enabling flags or ignoring required skips. Affected cases:

- `Antiphon.E2E.ChannelOutboundIsolatedTests.FakeGrok_outbound_mode_reads_frozen_request_and_invokes_a_tool`
- `Antiphon.E2E.ChannelOutboundIsolatedTests.Static_logger_reaches_owned_host_file_after_restart`
- `Antiphon.E2E.ChannelOutboundIsolatedTests.Four_sources_convert_only_for_the_selected_conversation`

## D-7 — Isolated channel session never reaches Running

- Class/test: `Antiphon.E2E.ChannelE2ETests.SignalR_channel_send_routes_to_card_group_for_live_session`.
- Exact initial error: `TimeoutException: Session a8a81191-1818-46e2-9f67-027dfc8cc3f6 did not reach Running status.`.
- Classification: **environment (probable fixture portability), with runner failure cause not fully resolved**; not a confirmed flake. One class-only rerun reproduced the timeout (0 pass / 1 fail / 0 skip, exit 2, 84.833s wall).
- The retained application log records isolated-runner `/sessions` HTTP 500, followed by a kill of that same owned session. The fixture hard-codes `cmd.exe` at `AntiphonAppFixture.cs:642`; this executable is absent here. That line originates in `d04ab36d26d9bafea6d93396120e1fcdf6638161` (CARD-0009). This is a plausible incompatible input, not proof that it is the sole HTTP 500 cause; no exact introducing commit for the opaque runner failure is established.
- Smallest action: collect this fixture on Windows or commission a portable owned raw process and retain the runner failure body/log. Preserve session ownership and the current timeout; do not widen it.
- Evidence: initial `raw/e2e/run.trx`, class-only `raw/e2e-rerun-channel/run.trx`, and retained ChannelE2ETests application/notes logs.

## D-8 — Contract snapshot: Pipeline_status_contract

- Class/test: `Antiphon.E2E.ContractSnapshotTests.Pipeline_status_contract`.
- Classification: **regression in contract evidence / snapshot drift**; intended API evolution versus acceptance regression still needs owner inspection. All five cases failed again in the class-only rerun (0 pass / 5 fail / 0 skip, 76.510s wall). No base execution or first-bad bisection was performed.
- Exact assertion headline: `ShouldAssertException: Normalize(existing)`; `.ShouldBe(Normalize(pretty/scrubbed))` compares the stored fixture with the current response. The first displayed “should be” value is the live response; “but was” is the stored fixture.
- Exact additional error: `Backend contract for /api/agent-tasks/pipeline drifted from pipeline.json. If the change is intentional, verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.`
- Observed difference: The assertion truncates each compared value at 5,000 characters; the visible prefixes are identical. The differing suffix was not retained by Shouldly. Do not infer its exact field from that truncated output.
- Introducing commit/card: Exact introducing commit/card unresolved. The fixture was last captured in `e40bd3571e98855e1401ccc6c91d59f2440bcd84` (CARD-0557); later pipeline changes need owner comparison.
- Smallest fix: verify the intended current response and its frontend/Storybook consumers, then refresh only this reviewed fixture; repair product behavior instead if the old contract remains required. Do not blanket recapture all fixtures.
- Complete unedited assertion: `/work/evidence/task-1c14bf28/raw/e2e/Pipeline_status_contract-assertion.txt`; initial and isolated TRX remain retained.

## D-9 — Contract snapshot: Backlog_quadrant_contract

- Class/test: `Antiphon.E2E.ContractSnapshotTests.Backlog_quadrant_contract`.
- Classification: **regression in contract evidence / snapshot drift**; intended API evolution versus acceptance regression still needs owner inspection. All five cases failed again in the class-only rerun (0 pass / 5 fail / 0 skip, 76.510s wall). No base execution or first-bad bisection was performed.
- Exact assertion headline: `ShouldAssertException: Normalize(existing)`; `.ShouldBe(Normalize(pretty/scrubbed))` compares the stored fixture with the current response. The first displayed “should be” value is the live response; “but was” is the stored fixture.
- Exact additional error: `Backend contract for /api/cards?status=Backlog drifted from cards-backlog.json. If the change is intentional, verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.`
- Observed difference: The live response (the ShouldBe argument) contains cardFileVisibility, hasPrivateNotes, codeVerificationPolicy, reviewVerificationPolicy and requiredPlatform; the checked-in fixture lacks these fields.
- Introducing commit/card: Field source history includes `e6fcff5f67295b8721e71d48eeceecccd6892b45` (card file fields), `0489e33fa942dbcf40d9246ac46bcb9584abbaa6` (CARD-0544 verification policy), and `99d153a4a666f954c57c5321eeab9372de86cd64` (CARD-0710 platform). No single historical first failing run was established.
- Smallest fix: verify the intended current response and its frontend/Storybook consumers, then refresh only this reviewed fixture; repair product behavior instead if the old contract remains required. Do not blanket recapture all fixtures.
- Complete unedited assertion: `/work/evidence/task-1c14bf28/raw/e2e/Backlog_quadrant_contract-assertion.txt`; initial and isolated TRX remain retained.

## D-10 — Contract snapshot: Delegated_task_board_and_drawer_contracts

- Class/test: `Antiphon.E2E.ContractSnapshotTests.Delegated_task_board_and_drawer_contracts`.
- Classification: **regression in contract evidence / snapshot drift**; intended API evolution versus acceptance regression still needs owner inspection. All five cases failed again in the class-only rerun (0 pass / 5 fail / 0 skip, 76.510s wall). No base execution or first-bad bisection was performed.
- Exact assertion headline: `ShouldAssertException: Normalize(existing)`; `.ShouldBe(Normalize(pretty/scrubbed))` compares the stored fixture with the current response. The first displayed “should be” value is the live response; “but was” is the stored fixture.
- Exact additional error: `Backend contract for /api/agent-tasks?rootId=aaaaaaaa-0000-0000-0000-000000000001 drifted from agent-tasks.json. If the change is intentional, verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.`
- Observed difference: The live response contains requiredPlatform, runnerId, requirementSource, observedPlatform and runnerDefaultsRevision after scopeSource; the checked-in fixture stops at scopeSource.
- Introducing commit/card: The platform fields originate in `99d153a4a666f954c57c5321eeab9372de86cd64` (CARD-0710); later runner-selection fields also changed. This identifies an input change, not a complete bisection.
- Smallest fix: verify the intended current response and its frontend/Storybook consumers, then refresh only this reviewed fixture; repair product behavior instead if the old contract remains required. Do not blanket recapture all fixtures.
- Complete unedited assertion: `/work/evidence/task-1c14bf28/raw/e2e/Delegated_task_board_and_drawer_contracts-assertion.txt`; initial and isolated TRX remain retained.

## D-11 — Contract snapshot: Home_tasks_rail_contract

- Class/test: `Antiphon.E2E.ContractSnapshotTests.Home_tasks_rail_contract`.
- Classification: **regression in contract evidence / snapshot drift**; intended API evolution versus acceptance regression still needs owner inspection. All five cases failed again in the class-only rerun (0 pass / 5 fail / 0 skip, 76.510s wall). No base execution or first-bad bisection was performed.
- Exact assertion headline: `ShouldAssertException: Normalize(existing)`; `.ShouldBe(Normalize(pretty/scrubbed))` compares the stored fixture with the current response. The first displayed “should be” value is the live response; “but was” is the stored fixture.
- Exact additional error: `Backend contract for /api/home/tasks drifted from home-tasks.json. If the change is intentional, verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.`
- Observed difference: The live response contains requiredPlatform, runnerId and observedPlatform after completedAt; the checked-in fixture lacks them.
- Introducing commit/card: Those DTO fields originate in `04c6830e3c3a745a68d832e02d8bbe96fab1521a` (CARD-0710). This is source attribution; no historical checkout run was performed.
- Smallest fix: verify the intended current response and its frontend/Storybook consumers, then refresh only this reviewed fixture; repair product behavior instead if the old contract remains required. Do not blanket recapture all fixtures.
- Complete unedited assertion: `/work/evidence/task-1c14bf28/raw/e2e/Home_tasks_rail_contract-assertion.txt`; initial and isolated TRX remain retained.

## D-12 — Contract snapshot: Agent_files_review_threads_and_content_contracts

- Class/test: `Antiphon.E2E.ContractSnapshotTests.Agent_files_review_threads_and_content_contracts`.
- Classification: **regression in contract evidence / snapshot drift**; intended API evolution versus acceptance regression still needs owner inspection. All five cases failed again in the class-only rerun (0 pass / 5 fail / 0 skip, 76.510s wall). No base execution or first-bad bisection was performed.
- Exact assertion headline: `ShouldAssertException: Normalize(existing)`; `.ShouldBe(Normalize(pretty/scrubbed))` compares the stored fixture with the current response. The first displayed “should be” value is the live response; “but was” is the stored fixture.
- Exact additional error: `Backend contract for /api/agents/6ef16e0f-81d4-4efb-9c51-4246487555dd/files drifted from agent-files.json. If the change is intentional, verify the frontend stories against the new shape, delete the fixture, and re-run to re-capture.`
- Observed difference: The live response includes an untracked CLAUDE.md entry before notes/report.md; the old fixture contains only notes/report.md and README.md. Additional review/hash values differ. This is an added entry, not just Linux ordering.
- Introducing commit/card: Exact introducing commit/card unresolved; investigate agent instruction-file creation and the contract scenario before changing the fixture.
- Smallest fix: verify the intended current response and its frontend/Storybook consumers, then refresh only this reviewed fixture; repair product behavior instead if the old contract remains required. Do not blanket recapture all fixtures.
- Complete unedited assertion: `/work/evidence/task-1c14bf28/raw/e2e/Agent_files_review_threads_and_content_contracts-assertion.txt`; initial and isolated TRX remain retained.

## D-13 — Native execution diagnostic adapter drops Error outcomes

- Step/function: E2E UID reconciliation, `scripts/lib/nightly-coverage.ps1:Get-NightlyDiagnosticStateName` / `ConvertFrom-NightlyDiagnosticLog`.
- Classification: **regression in verification adapter compatibility**, not a product failure or a green waiver.
- Exact observed discrepancy: native MTP diagnostics contain 167 in-progress records, 54 Passed, 110 `ErrorTestNodeStateProperty`, and 3 Skipped terminal records. The repository adapter returns only 57 terminal UIDs (54 Passed + 3 Skipped), leaving all 110 failed UIDs missing. It matches `FailedTestNodeStateProperty` but does not recognize `ErrorTestNodeStateProperty`; unknown states are discarded for execution parsing.
- Source introduction: `86c2dbb4579f8e2cc1228b4d4248907db47b60ec`, CARD-0487 (“parse real TUnit diagnostics and reconcile UIDs”). The raw evidence is pinned TUnit 1.44 / MTP 2.2.2, matching the adapter’s supported versions.
- An independent external raw-state audit, explicitly not a repository repair or qualification receipt, observes all 167 expected terminal UIDs, zero missing/extra/duplicates, with the same 54/110/3 outcomes as TRX. The official adapter stays incomplete; no source or gate predicate was changed.
- Smallest fix: recognize native Error state as a non-passing terminal outcome and test a real failing TUnit diagnostic/TRX pair, preserving strict UID/version checks and red verdicts. Do not suppress the missing-UID check or map errors to pass.
- Evidence: `raw/e2e/diagnostics/`, `uid-reconciliation.json` (official adapter), `raw-state-audit.json` (independent observation), and the initial TRX.

## Repair boundary

Fixes need separately commissioned Code/Review/land work. Keep this candidate’s failures and costs. Recut a new immutable candidate after repairs and collect the full automated Windows RC profile, publication authority/ledger and accepted report receipts. No Interim, publication or train acceptance is authorized by these local diagnostics.
