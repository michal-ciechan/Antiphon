Master port of rc top-ups 8 and 9 is prepared, pushed, and verified. Land the task branch. Do not push master from this task.

Product tip before this report commit: fc7c7fa4d84c51a88a63260e6d9636e53fdcd5bc on feat/card-task-ae45156c. Base and origin/master at the landability fetch: 718bd29f4a1504c64c491d607d31403fcb87dae2. Range cherry-picked with -x, oldest first: d1d090f766451797a3bd9b30c9d2966f2f8c034c..f3e302eb9925ecbd1607b0fd03e77c7cd1aa2e32 on origin/rc/2026-10-27. Eighteen commits. None were already on master. None were report-only .antiphon or docs/investigations commits. Stable patch-id of every pick matches its rc original (18/18). The rc commits were themselves -x copies; both trailers were passed through. No source edit after the picks. No assertion or timeout was loosened.

Net delta versus that origin/master: 35 files, 533 insertions, 120 deletions.

docs/orchestration-loop.md auto-merged on three SDK commits. The rc SDK-provenance paragraph is present and master's CARD-1514 section remains. No conflict markers. Patch-ids of those three picks still match.

Known inherited add/add paths were not in the range and were not modified. Master content kept. No assertion removed.

- .antiphon/task-af2b78c8.md 97dd5f63c8651d8b61b28520c3a8f61883d5af84
- docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md 644aca6f1c540c23b80cce8783df4a83aec93cf5
- tests/Antiphon.Tests/TestHelpers/ClaudeDialogFixture.cs c99b24f30194339f3e1707b25dac21dd157d770b

Approval files unchanged versus the base (Final Review constraint):

- LandVerificationBuildGraph.evaluation.txt cfef1e84b2a108d5e1001eb8d7a32b6352c0b532
- LandVerificationBuildGraph.manifest.txt d4009aa20b363c8762fe26a99658a45e11442f89
- LandVerificationBuildGraph.pins.txt 749d65387b4e9ae7f84fba8c7b6f42658b430dc7

Landability, fetched origin/master 718bd29f4a1504c64c491d607d31403fcb87dae2, product tip fc7c7fa4d84c51a88a63260e6d9636e53fdcd5bc: git merge-tree --write-tree exited 0, tree 9dced407907378a08f5d7f2a086b2deb2ecfc8f0, equal to that HEAD tree. No target was merged into this branch. Re-check after this report commit is in the caller message.

| rc original | master pick | subject |
| --- | --- | --- |
| f0fecaa135648cf35748fa49b946fab021a376b3 | 02f112d926412fb739a44c472950e0b775645bcd | test: witness portable land graph SDK provenance checks |
| fd979f1e81ea37d592aa9bcd1c16e146e0ed2d11 | 905f67be5098dceee08e6c8f4630d0eafb212915 | test: use owned audit fixture cleanup contract |
| 2d85434c843ea0ecc230bf97c3477f6f37cc005c | d4c0980429c427a68f031e9d7182a6e924a2b053 | fix: treat land graph evaluation SDK as author provenance |
| d67dbecff3b8dea6574be167abe53e4db1b3c4b8 | a06b3aceaa32fe54363babf64d2590d49677e8a3 | test: expose invalid SDK records and missing cross-SDK evaluation |
| 17e600530a0496e8fc28dbf84c722358ef21b822 | 6fe7e7adedd5dd54f862d004c4ce2111d4a957b3 | test: keep SDK glob witness within precheck property allowlist |
| 1daa1232fef66fe9694c056291ebf630ef1f4041 | 5187c1c87319ee2c63ecc3b48a9eb20871f67485 | fix: refuse SDK provenance outside committed roll-forward policy |
| c68fc2bfd3db251adfbad81585ce3984372ef9f6 | fd398e5fb80b922729e579ee1f91aba6df8a4927 | fix: evaluate the land graph when the checking SDK differs |
| 4e9fb836ce93afcd13f7eaa5e43a039455c92b23 | 09f58c609664170744379e3277526f11323f7aae | test: require land graph evaluation for host-equal SDK provenance |
| e7622789fcd30760998ed23525ab06544ac4f942 | f64cf363e9d2f9fcd8d4321d731edd90b9815280 | fix: always evaluate the land graph during audit check |
| 9f00008facfc48e75555c08df59887019b3f8dfe | 8e43d537824860db139156288912a1bee8586b32 | test: update board diff navigation and order terminal input |
| 57f0c26c4bf07ea31adbb70f09e5a931ab4e22a2 | 7e07c78a838932db34c63c482ed0525995388538 | test: use current workflows dashboard route in delete E2E |
| bae511d59f422f33747fd1422926a75b97451d9d | 3a55908af2c366448514586cd73b10830bd9fbff | test: cover native Windows Grok receipt grammar and unknown custody |
| 71f618a7e150b0be88e084afe2da225569173a0c | 1f50bc2534c2d11ba12ae5109b0f55280ec17df1 | test: repair terminal fixture identity ingestion and native filesystem setup |
| 3d345b419e6fd19c8ac63a7abec8fda2c7eacef0 | e054a29cbaa6ca8adf84b9b2eeb49615db2e99cb | test: control fixture scheduling clocks policy import and launch diagnostics |
| 2a1dc8b79442bb489cbcea8c5565ec1f4fc15538 | cad310b24965f3e45132836ce45581e7c276e93f | test: correct receipt assertion and force reply-before-confirmation ordering |
| e4dc37dac2b67cc745a2bf421212b5c629bd73a9 | ca8b4ab2fc88b3599081004c05c8f02cbe78b7b3 | test: repair Windows scripts fixture custody and shell boundaries |
| b9888dc0c9676528e57401fb036c2c5715a03ba2 | 1eb53327e40e3a1029b0ef372b878f08c6a4f051 | test: bind delivery crash cuts and cover retained evidence cleanup |
| f3e302eb9925ecbd1607b0fd03e77c7cd1aa2e32 | fc7c7fa4d84c51a88a63260e6d9636e53fdcd5bc | test: keep portable recovery witnesses outside native skip roster |

Verification at fc7c7fa4d84c51a88a63260e6d9636e53fdcd5bc. Host SDK 10.0.403. Every build and test driver took a host build slot. OutputPath=bin-ae45156c/ and UseAppHost=false. TUnit via dotnet run --no-build after the solution build. 49 bin-ae45156c directories were removed after the last test exited.

- Solution build: exit 0, 0 errors, 1252 warnings, 4m 46.31s. Slot lease 30226bdc-0978-4490-a12c-9db32445a927 waited 0s, held 287s.
- LandGraphEvaluationTests: total 104, failed 0, succeeded 104, skipped 0, 8m 46s 935ms. Lease 81a06a02-a3d1-4939-9dc1-5d048a2add99 waited 150s, held 533s.
- LandNonCodeSkipTests: total 111, failed 0, succeeded 111, skipped 0, 12m 55s 510ms. Lease 0f94d9e4-1a30-4f82-bc6c-644cdbe06397 waited 0s, held 779s.
- Docker target server-build: exit 0. Image efd786eda36a tagged antiphon-server-build:ae45156c. Build-arg SOURCE_REVISION=fc7c7fa4d84c51a88a63260e6d9636e53fdcd5bc. Lease b7f46845-c715-46cf-8ae9-feb3e7450c23 waited 75s, held 361s. Legacy-builder notice and existing publish warnings are not failures. Image was not published or deployed.

Narrow selections (one method, argument expansion included). Counts are passed / failed / skipped.

| Selection | P | F | S | Duration | Exit |
| --- | ---: | ---: | ---: | --- | ---: |
| WindowsDeliveryFixtureWitnessTests or CheckpointRecoveryFixtureTests | 8 | 0 | 0 | 4s 435ms | 0 |
| ScriptHarnessProcessContractTests/Windows_resolution_skips_alias_before_installed_executable | 1 | 0 | 0 | 805ms | 0 |
| RemoteScriptContractTests/C1192_Fixture_has_no_host_payload_tree_or_payload_bind | 1 | 0 | 0 | 1m 10s 250ms | 0 |
| RcQualificationStatusTests/C1500_StatusDoesNotMutate | 1 | 0 | 0 | 6s 464ms | 0 |
| CardRelationshipScriptTests/Block_fails_loudly_when_relationships_returns_404 | 1 | 0 | 0 | 1s 977ms | 0 |
| RollingVolumeRecycleScriptTests/C1008_Busy_routed_and_land_in_flight_refuse | 1 | 0 | 0 | 1m 32s 663ms | 0 |
| AgentTaskPipelineStatusTests/C557_pipeline_includes_ranked_backlog_contract | 0 | 0 | 0 | 230ms | 8 |
| AgentTaskPipelineEndpointTests/C557_pipeline_includes_ranked_backlog_contract | 1 | 0 | 0 | 30s 590ms | 0 |
| DispatcherSiblingInspectionSchedulingTests/C1349_InspectionQueueRecovers | 10 | 0 | 0 | 38s 162ms | 0 |
| RemoteControlRecoveryPtyIntegrationTests/C514_Idle_modal_clearance_delivers_three_complete_prompts | 0 | 0 | 1 | 13s 333ms | 8 |
| CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes | 42 | 0 | 0 | 7s 176ms | 0 |
| HerdrAlwaysOnChannelParityTests/AlwaysOn_channel_bound_survives_child_death_and_replies | 0 | 0 | 3 | 20s 582ms | 8 |
| GrokDelegateEndToEndTests/an_unmarked_grok_reply_inside_one_tailer_poll_of_the_nudge_settles_unmarked_after_nudge | 0 | 0 | 1 | 14s 945ms | 8 |
| GrokSignInRuntimeTests/A_sign_in_mode_fakegrok_pool_task_never_types_the_brief | 0 | 0 | 1 | 24s 945ms | 8 |
| SessionMessageQueuePtyIntegrationTests/Batched_multiline_body_passes_composer_delivery_verification_and_submits_once | 0 | 0 | 1 | 24s 282ms | 8 |
| SessionMessageQueueGrokPtyIntegrationTests/Queued_message_submits_through_the_real_runtime_runner_pty_path | 0 | 0 | 1 | 13s 746ms | 8 |
| SessionQueueReceiptPlumbingTests/C475_MultilineWritesKeepPasteMarkers | 0 | 0 | 1 | 26s 017ms | 8 |
| GrokRulesHttpAcceptanceTests/Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled | 0 | 0 | 1 | 861ms | 8 |
| BoardE2ETests/Board_manual_xterm_command_marks_session_as_streaming_turn (browser cache) | 1 | 0 | 0 | 1m 18s 937ms | 0 |
| WorkflowDeleteTests/Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard (browser cache) | 1 | 0 | 0 | 59s 025ms | 0 |
| AgentTaskLandDeliveryE2ETests/C467_RetainedEvidenceCompletesCleanup | 0 | 1 | 0 | 775ms | 2 |

The first C557 row selected zero tests because the method is on AgentTaskPipelineEndpointTests in the same file. That row is not a pass. The corrected class row executed 1 and passed. Kafka connection-refused lines during endpoint and E2E startup are existing broker-absent noise, not the verdict.

The first Board and Workflow E2E attempts failed in BeforeTest because PLAYWRIGHT_BROWSERS_PATH=/ms-playwright does not exist on this runner. Those two rows were rerun with PLAYWRIGHT_BROWSERS_PATH=/home/app/.cache/ms-playwright (chromium_headless_shell-1243). Both passed: board 1/0/0 in 1m 18s 937ms, workflow 1/0/0 in 59s 025ms. client/dist was built first (tsc and vite, exit 0). node_modules and dist stay uncommitted.

C467 failed before the ported body: LandDeliveryFixture.InitializeAsync asserts OperatingSystem.IsWindows() at line 59, which is already that line on origin/master (96b5f4d9c5b01f3312b07b9e5dc519ffceda8b49, outside this range). Linux result: expected True, actual False. The retained-evidence repair was not executed here. It remains a Windows obligation.

Exit 8 rows are all-skipped Windows obligations, not assertion failures. Reasons: ConPTY only on Windows (C514, Grok nudge, Grok sign-in, both pty queue tests, C475); CARD-0865 Linux standing/always-on channel parity (Herdr, 3 arguments); Owned Windows PtyHost test (Grok rules HTTP). Those repaired bodies were not executed on Linux. CheckpointRecoveryFixtureTests ran inside the witness row and passed; it did not skip.

No tags, no publish, no promote, master not pushed.
