# CARD-0964 Code report

The fallback test now carries method-level unkeyed `[NotInParallel]`. The Linux reflection guard went red against the original keyed metadata, green after the fix, and red again after temporarily removing the method attribute. Production code is unchanged.

## Change and scope

- `tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs`: add unkeyed isolation only to `A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete`.
- `tests/Antiphon.Agents.Pty.Tests/ConPtyEnvironmentIsolationGuardTests.cs`: inspect compiled method IL, including async/iterator state-machine bodies, for the ConPTY directory constant and an environment setter. Require an unkeyed scheduling attribute on the original method or its class. The two known mutators are census floors; a scanner that finds nothing fails. Named assertion: `conpty-env-mutators-are-unkeyed-not-in-parallel`.

Method-level isolation is the smallest test-only fix: it excludes every concurrent test in this assembly while the exclusive process-wide directory override is installed, without adding a shared key to every modern reader or changing a production seam. `WindowsPtyArgvNativeTests` already has unkeyed class-level isolation. Its metadata also passes the new guard.

## Environment mutator census

Searched all test projects for `ANTIPHON_CONPTY_DIR`, `DirectoryEnvVar`, and `Environment.SetEnvironmentVariable`, including the whole Pty project and `Antiphon.Tests`.

| Project | Method | Writes | Scheduling after fix |
|---|---|---|---|
| Antiphon.Agents.Pty.Tests | PtyBackendContractTests.A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete | Set temporary empty directory; restore previous value in finally (including clearing it when previous is null) | Unkeyed method attribute; existing Headed class key retained |
| Antiphon.Agents.Pty.Tests | WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv(modern) | Set staged directory; restore previous value in finally | Existing unkeyed class attribute |
| Antiphon.Tests and every other test project | None found | None | No edits |

`PtyBackendEnvGuard` fixtures clear `ANTIPHON_PTY_BACKEND`, a different variable. They do not mutate `ANTIPHON_CONPTY_DIR`. There are no other directory-variable fixtures or setters in the searched test tree.

## Modern backend reader census

The appendix records individual methods and exact source call sites in the Pty project and the directly relevant Antiphon.Tests files. Additional indirect readers:

- `WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv(modern)` passes the backend parameter to `PtyAgentRunner`.
- `FakeClaudeContractTests.LaunchClippingFakeOnModernPtyAsync` serves `A_bracketed_paste_is_not_clipped_even_with_the_clip_model_armed`, `A_43KB_bracketed_paste_arrives_whole_with_clipping_armed`, `A_collapsed_paste_still_produces_composer_evidence`, and `A_collapsed_pastes_transcript_record_carries_the_full_body`.
- `CodexMcpBootProbeTests.ProbeAsync` serves `Baseline_scratch_cwd`, `Baseline_repo_cwd_incident_shape`, and `Suppressed_repo_cwd`.
- `ConPtyHost.FindRedistConPty` serves `PtyBracketedPasteContractTests.A_modern_conpty_delivers_the_markers_unchanged` and `PtyPasteMarkerExperiments`. In the latter, `Backends` serves `Markers_by_conpty_backend`, `Host_selftest`, and `Real_claude_by_conpty_backend`; `Real_claude_controls_and_size_sweep` and `Real_claude_delivery_envelope` also probe directly. Its trial helper selects modern when a redistributable DLL is supplied.
- `PtyBackendEnvGuardTests.The_suite_ignores_an_inherited_pty_backend` resolves the default policy, which its assembly guard forces to inbox; it is listed conservatively in the appendix.
- Other projects have readers in `Antiphon.PtyHost.Tests/HostCustodyTests`, `Antiphon.SessionRunner.Tests/{PtyHostAdoptionTests,PtyBackendSeamTests,RunnerCustodyTests}`, `Antiphon.E2E/{OutputDistillationApplyCanaryTests,Fixtures/LandDeliveryFixture}`, and each project's `PtyBackendEnvGuard`. Those execute in separate processes and have no directory-variable mutator found here.

## Verification and inherited failures

All executions use `pwsh -NoProfile -File scripts/run-checkpoint.ps1` directly and take a host build lease. Literal filter pipes and trailing class wildcards are preserved. There is no `Antiphon.Tests` Unit lane: this change is confined to the Pty test project. The full Pty project run covers its Unit selection as well as all discovered integration tests; Windows-only execution remains pending.

Full-project baseline at start SHA `808677658cc418dc439d906de4526aaea32e231a`: 469 executed, 453 passed, 16 failed, 210 skipped. The cross-cutting invariant is process-wide backend probing; the brief explicitly requires one full run at base and one at final SHA. Measured baseline cost: 25 seconds holding the build slot. Budget for final full run: about one minute. The final caller response supplies final-SHA checkpoint lines and the comparison verdict; this report is committed before those runs so they certify the final pushed SHA.

The actual baseline has the brief's 13 failures plus three further inherited Windows-only failures. The exact 16-result failure multiset is appended below: 11 parser failures, one CommandLineLengthTests shell32.dll failure, one GrokNativeSessionStoreTests Missing-versus-Unavailable failure, one FakeGrokContractTests Windows assertion failure, and two WindowsPtyArgvNativeTests cases rejecting Linux. Compare the final run against the entire multiset, including both native argv argument cases.

### Red / green / mutation

- CP-RED: 1 executed, 0 passed, 1 failed, 0 skipped. Guard added on top of unchanged base tests at `c22dea0dfbca894617b01e2422003bf8bcef8320`. It fails specifically for `PtyBackendContractTests.A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete` at the named assertion.
- CP-GREEN: 6 executed, 6 passed, 0 failed, 34 skipped at fix SHA `5141ef892e7f3c80849791244ef494ca22eb97a3`. Full named classes: guard, backend contract, custody.
- CP-MUTATION-RED: 1 executed, 0 passed, 1 failed, 0 skipped. Temporary removal of only the added unkeyed attribute makes the same named assertion fail. This is explicitly a dirty diagnostic run, not a clean source certificate. Committed head was pushed before the scratch mutation. No scratch source was committed.
- Restored `PtyBackendContractTests.cs` from the literal scratch directory `/tmp/c964-mutation.hJ6NG2`; `cmp` passed, timestamp refreshed, `git diff --exit-code` and `git status --short` were empty before writing this report. Final runs rebuild from restored source into a different output directory.
- Invocation correction: the first CP-GREEN command mistakenly used an invalid SHA (`b1`) and exited 2 before building/running. Corrected CP-GREEN is the clean receipt below.
- Setup correction: initial CP-MUTATION used unavailable `python3`, so no mutation occurred; that extra unchanged-source diagnostic ran 1 test and passed. It is not positive-control evidence. Actual CP-MUTATION-RED used the patch tool and produced the required assertion failure.

### Final commands

After this report's commit is pushed, run CP-FINAL once, then CP-FOCUSED with its output reused. Bind both to the full final SHA using `-ExpectedSourceSha`.

```sh
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-FINAL -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c964-final/ -Filter '/*/*/*/*' -ExpectedSourceSha <final-sha> -ResultsRoot .antiphon/c964-checkpoints
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-FOCUSED -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-c964-final/ -NoBuild -Filter '/*/*/(ConPtyEnvironmentIsolationGuardTests*)|(PtyBackendContractTests*)|(PtyCustodyTests*)/*' -MinExecuted 6 -Expect ConPtyEnvironmentIsolationGuardTests,PtyBackendContractTests -ExpectedSourceSha <final-sha> -ResultsRoot .antiphon/c964-checkpoints
```

## Windows handoff

Run a separate native Windows task at the final pushed SHA, in the Pty test project, with exactly:

```text
/*/*/(PtyBackendContractTests*)|(PtyCustodyTests*)|(WindowsPtyArgvNativeTests*)|(LaunchArgvGuardTests*)|(ModernConPtyCommandLineTests*)/*
```

Do not count Linux skips or the static guard as native Windows behavioral evidence. PCs remain pending for method-scoped SourceLanding Mutation. Next stage is Final Review; this Code task is landing owner, and a plain land follows a clean Final Review.

## Prior checkpoint receipts (verbatim)

```text
CHECKPOINT CP-BASE commit=808677658cc418dc439d906de4526aaea32e231a build=ok filter=/*/*/*/* executed=469 passed=453 failed=16 skipped=210 trx=/work/worktrees/task-fbb1b139/.antiphon/c964-checkpoints/CP-BASE-20261002-143032-5431/run.trx slot=granted waited=0s dirty=0 source=808677658cc418dc439d906de4526aaea32e231a sourceState=clean buildSource=verified
CHECKPOINT CP-RED commit=c22dea0dfbca894617b01e2422003bf8bcef8320 build=ok filter=/*/*/ConPtyEnvironmentIsolationGuardTests*/* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-fbb1b139/.antiphon/c964-checkpoints/CP-RED-20261002-143219-f3de/run.trx slot=granted waited=0s dirty=0 source=c22dea0dfbca894617b01e2422003bf8bcef8320 sourceState=clean buildSource=verified
CHECKPOINT CP-GREEN commit=5141ef892e7f3c80849791244ef494ca22eb97a3 build=ok filter=/*/*/(ConPtyEnvironmentIsolationGuardTests*)|(PtyBackendContractTests*)|(PtyCustodyTests*)/* executed=6 passed=6 failed=0 skipped=34 trx=/work/worktrees/task-fbb1b139/.antiphon/c964-checkpoints/CP-GREEN-20261002-143335-c597/run.trx slot=granted waited=0s dirty=0 source=5141ef892e7f3c80849791244ef494ca22eb97a3 sourceState=clean buildSource=verified
CHECKPOINT CP-MUTATION commit=5141ef892e7f3c80849791244ef494ca22eb97a3 build=ok filter=/*/*/ConPtyEnvironmentIsolationGuardTests*/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fbb1b139/.antiphon/c964-checkpoints/CP-MUTATION-20261002-143430-6a16/run.trx slot=granted waited=0s dirty=0 source=5141ef892e7f3c80849791244ef494ca22eb97a3 sourceState=clean buildSource=verified
CHECKPOINT CP-MUTATION-RED commit=5141ef892e7f3c80849791244ef494ca22eb97a3 build=ok filter=/*/*/ConPtyEnvironmentIsolationGuardTests*/* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-fbb1b139/.antiphon/c964-checkpoints/CP-MUTATION-RED-20261002-143520-10bc/run.trx slot=granted waited=0s dirty=1 source=5141ef892e7f3c80849791244ef494ca22eb97a3+dirty:d1274589fa7b29cebc4b81f4f2b29bc0797f0330563c2663bcbd4c379d5e00fd sourceState=dirty buildSource=verified
```

## Baseline failure multiset

```text
FAILED Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.A_backslash_not_before_a_quote_is_literal
FAILED Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.A_trailing_backslash_is_not_swallowed_by_the_closing_quote
FAILED Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.Plain_and_whitespace_arguments_round_trip
FAILED Antiphon.Agents.Pty.Tests.CommandLineLengthTests.Measure_equals_the_line_the_child_parses
FAILED Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.Portas_format_does_not_round_trip_the_shape_that_shredded_production
FAILED Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.ParseArgv_is_the_real_parser
FAILED Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.An_embedded_quote_survives_as_one_argument
FAILED Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.The_old_doubling_rule_would_have_shredded_the_embedded_quote_case
FAILED Antiphon.Agents.Pty.Tests.ModernConPtyCommandLineTests.A_backslash_immediately_before_a_quote_is_doubled
FAILED Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.The_old_doubling_rule_is_caught_before_the_process_is_created
FAILED Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.A_truncated_bundle_is_caught_by_LENGTH_not_by_presence
FAILED Antiphon.Agents.Pty.Tests.LaunchArgvGuardTests.An_argument_lost_off_the_end_is_reported_as_missing_rather_than_as_a_mismatch
FAILED Antiphon.Agents.Pty.Tests.GrokNativeSessionStoreTests.Probe_distinguishes_missing_found_and_unavailable_storage
FAILED Antiphon.Agents.Pty.Tests.FakeGrokContractTests.C467_BusyGatePreservesNativePromptAndTurnEnd
FAILED Antiphon.Agents.Pty.Tests.WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv
FAILED Antiphon.Agents.Pty.Tests.WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv
```

## Reader and mutator source call sites

```text
tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs:55: A_ready_claude_answers_the_input_probe_and_the_composer_is_left_empty:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs:114: Ctrl_u_clears_a_single_typed_line_from_the_rendered_composer:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeOverlayCanaryTests.cs:37: Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeOverlayCanaryTests.cs:72: One_Esc_dismisses_the_model_overlay_and_restores_the_composer:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs:48: Control_auto_connects_a_bridge_session_before_the_first_user:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs:91: Settings_file_prevents_auto_connect_and_keeps_startup_records:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs:143: Remote_control_slash_command_still_arms_after_settings_off:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs:40: One_Esc_dismisses_the_remote_control_management_menu:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs:91: C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitConfirmCanaryTests.cs:63: Enter_on_an_empty_composer_is_a_no_op:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitConfirmCanaryTests.cs:133: A_collapsed_pastes_jsonl_record_carries_the_full_body_not_the_placeholder:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs:50: An_untrusted_directory_blocks_the_tui_and_is_detected_within_seconds:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs:99: The_2_1_258_dialog_is_the_highlighted_list_layout:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ClaudeTrustPromptCanaryTests.cs:134: A_trusted_directory_never_reads_as_blocked:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/CodexCanaryTests.cs:65: One_real_turn_writes_UserMessage_AgentMessage_token_count_and_task_complete:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/CodexCanaryTests.cs:156: The_rollout_is_lazy_and_Enter_on_an_empty_composer_submits_nothing:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/CodexCanaryTests.cs:211: The_TUI_does_not_print_its_session_id_and_a_typed_newline_does_not_submit:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/CodexComposerCanaryTests.cs:104: Composer_submit_contract_on_the_modern_backend:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/CodexDoneDetectionCanaryTests.cs:65: Turn_lifecycle_contract_on_the_modern_backend:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/CodexMcpBootProbeTests.cs:85: ProbeAsync:                 await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/CodexOverlayCanaryTests.cs:40: Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ConPtyHost.cs:115: FindRedistConPty:         if (ConPtyRedistributable.TryLocate(out var shipped, out _))
tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs:1290: LaunchClippingFakeOnModernPtyAsync:         if (!ConPtyRedistributable.TryLocate(out _, out var why))
tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs:1300: LaunchClippingFakeOnModernPtyAsync:         var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs:187: Fake_and_real_claude_agree_that_a_bracketed_paste_is_not_clipped:         if (!ConPtyRedistributable.TryLocate(out _, out var why))
tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs:197: Fake_and_real_claude_agree_that_a_bracketed_paste_is_not_clipped:         var fake = await FakeAsync(lines, backend: "modern");
tests/Antiphon.Agents.Pty.Tests/FakeVsRealClipParityTests.cs:203: Fake_and_real_claude_agree_that_a_bracketed_paste_is_not_clipped:             var got = await RealClaudeAsync(lines, backend: "modern");
tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:93: Composer_submit_contract_on_the_modern_backend:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:233: Turn_end_signals_vs_turn_completed_and_updates_flush_latency:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:344: Esc_interrupt_shape_in_updates_jsonl:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:426: Session_recap_shape_and_trigger:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:557: Fresh_cwd_and_fresh_grok_home_launch_blocking:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokCanaryTests.cs:588: Fresh_cwd_and_fresh_grok_home_launch_blocking:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionCanaryTests.cs:35: Resume_of_an_unknown_id_exits_nonzero_fast_and_creates_nothing:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionCanaryTests.cs:81: Create_writes_the_session_directory_at_launch:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionCanaryTests.cs:131: Resume_of_that_directory_starts:             await using (var create = new PtyAgentRunner("modern"))
tests/Antiphon.Agents.Pty.Tests/GrokNativeSessionCanaryTests.cs:151: Resume_of_that_directory_starts:             await using var resume = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokQuestionPopupCanaryTests.cs:53: Popup_present_after_ask_user_question_usage_does_not_match_answer_clears_esc_does_not:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokSignInCanaryTests.cs:32: Detector_matches_the_live_sign_in_screen_within_10s:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokSubmitWhileWorkingCanaryTests.cs:38: Submit_during_a_tool_turn_writes_cancelled_then_the_new_user_chunk:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokSubmitWhileWorkingCanaryTests.cs:112: Submit_during_a_streaming_text_turn_writes_cancelled_then_the_new_user_chunk:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokSubmitWhileWorkingCanaryTests.cs:191: Queued_follow_up_during_a_tool_turn_is_silent_on_jsonl_until_drain:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokUsageOverlayCanaryTests.cs:40: Esc_on_an_idle_empty_composer_is_a_noop_and_a_body_still_renders:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/GrokUsageOverlayCanaryTests.cs:78: One_Esc_dismisses_the_usage_overlay_and_restores_the_composer:             await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ModernPtyDa1Tests.cs:49: RequireShippedDll:         if (!ConPtyRedistributable.TryLocate(out var dll, out var why))
tests/Antiphon.Agents.Pty.Tests/ModernPtyDa1Tests.cs:63: Modern_child_first_output_arrives_without_the_da1_stall:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ModernPtyDa1Tests.cs:93: The_da1_reply_is_consumed_and_does_not_leak_to_the_child:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/ModernPtyDa1Tests.cs:209: WaitForQuiet_on_modern_returns_false_from_launch_under_continuous_output:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:37: RequireShippedDll:         if (!ConPtyRedistributable.TryLocate(out var dll, out var why))
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:57: The_flag_defaults_off:         var decision = PtyBackendPolicy.Resolve(requested);
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:105: A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete:             Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, dir);
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:108: A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete:             var missing = PtyBackendPolicy.Resolve("modern");
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:114: A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete:             var halfStaged = PtyBackendPolicy.Resolve("modern");
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:121: A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete:             Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, previous);
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:140: Asking_for_the_modern_backend_runs_the_child_under_our_own_OpenConsole:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/PtyBackendContractTests.cs:171: The_production_write_path_delivers_the_markers_on_the_modern_backend:         await using var probe = await NodeStdinProbe.StartAsync(chunkLog: false, decset2004: true, backend: "modern");
tests/Antiphon.Agents.Pty.Tests/PtyBackendEnvGuard.cs:73: The_suite_ignores_an_inherited_pty_backend:         PtyBackendPolicy.Resolve().Backend.ShouldBe(
tests/Antiphon.Agents.Pty.Tests/PtyBracketedPasteContractTests.cs:90: A_modern_conpty_delivers_the_markers_unchanged:         var redist = ConPtyHost.FindRedistConPty();
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:25: Root_exit_before_subscription_is_replayed_and_closes_input:         await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:48: Failed_tracked_attempt_cannot_spawn_again_on_the_same_runner:         await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:90: Output_drain_cancellation_never_returns_an_exit_observation:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:124: C478_G202_KillIsNotReceipt:         await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:202: C478_G197_QueryFailure:         await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:261: Tracked_runner_seals_drains_and_observes_original_job:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:312: C478_G198_NonemptyJob:         var runner = new PtyAgentRunner("modern") { CustodyNative = new NativeProbe() };
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:381: C478_G200_ProducerSeal:         await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
tests/Antiphon.Agents.Pty.Tests/PtyCustodyTests.cs:538: RequireModern:         if (!ConPtyRedistributable.TryLocate(out var dll, out var reason))
tests/Antiphon.Agents.Pty.Tests/PtyKillProcessTreeTests.cs:37: RequireShippedDll:         if (!ConPtyRedistributable.TryLocate(out var dll, out var why))
tests/Antiphon.Agents.Pty.Tests/PtyKillProcessTreeTests.cs:73: Modern_runner_KillAsync_reaps_grandchild_and_releases_held_directory:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs:207: Real_claude_controls_and_size_sweep:         var redist = ConPtyHost.FindRedistConPty();
tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs:253: Real_claude_delivery_envelope:         var redist = ConPtyHost.FindRedistConPty();
tests/Antiphon.Agents.Pty.Tests/PtyPasteMarkerExperiments.cs:463: Visible:         var redist = ConPtyHost.FindRedistConPty();
tests/Antiphon.Agents.Pty.Tests/WindowsPtyArgvNativeTests.cs:42: Windows_backends_keep_exact_native_argv:                 Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, conPtyDir);
tests/Antiphon.Agents.Pty.Tests/WindowsPtyArgvNativeTests.cs:78: Windows_backends_keep_exact_native_argv:                     Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, previousConPtyDir);
tests/Antiphon.Tests/Agents/ClaudeEffortPromptCanaryTests.cs:40: Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer:         await using var runner = new PtyAgentRunner("modern");
tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs:309: RequireRedistributable:         if (!ConPtyRedistributable.TryLocate(out _, out var why))
tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs:576: Launch_args_reach_the_child_process:         if (backend == "modern" && !ConPtyRedistributable.TryLocate(out _, out var why))
```

The lexical call-site appendix labels the final tuple-returning iterator in PtyPasteMarkerExperiments as its preceding method `Visible`; that call is actually `Backends` at line 464, described above.
