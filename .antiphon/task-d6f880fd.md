# CARD-1619 native Linux policy rows

Baseline at untouched assigned source `13565c5a480d8f30a52aa4679a5f1681ef2a8c70`.

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Antiphon.Tests | 15 | 0 | 0 |
| Antiphon.SessionRunner.Tests | 1 | 2 | 0 |
| Antiphon.Agents.Pty.Tests | 1 | 2 | 0 |
| Antiphon.PtyHost.Tests | 1 | 2 | 0 |

Both `Policy_rows_require_pinned_attribute` and `Markers_do_not_expand_policy_exclusions` fail in SessionRunner, Pty and PtyHost. The pinned-attribute contract passes in all four suites. Only Antiphon.Tests compiles NativeLinuxPolicyWitnessMutationTests (12/12 passing). All reported diagnostics are `reverse-coverage`; there are no missing-marker, missing-row or legacy-digest-mismatch diagnostics. Native bodies were not selected.

All four builds passed with zero errors. Drivers ran sequentially through scripts/build-slot.ps1, TEMP=/tmp, UseAppHost=false, OutputPath=bin-d6f880fd/, TUNIT_MAX_PARALLEL_TESTS=1. Raw logs, fresh TRX and source-bound timings are retained under /work/worktrees/task-d6f880fd/.antiphon/evidence/d6f880fd/.

## Exact baseline diagnostics

### Antiphon.Tests

No policy diagnostic.

### Antiphon.SessionRunner.Tests

```text
reverse-coverage suite=session-runner row=Card1619LinuxCustodyStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxCustodyStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxCustodyStopIntentTests signature=Dead_host_recovery_records_intent_and_remains_unknown()
reverse-coverage suite=session-runner row=Card1619LinuxCustodyStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxCustodyStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxCustodyStopIntentTests signature=Explicit_release_records_signal_without_fabricating_custody()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Blocked_record_write_still_signals_within_bound()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Cross_process_recorded_identity_kills_pending_child()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Custody_invocation_publishes_memory_before_helper_and_records_after()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Kill_all_records_each_owned_generation()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Orphan_and_pending_kills_keep_identity()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Own_stop_preserves_SIGKILL_and_intent()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Proc_stat_parser_handles_spaces_and_parentheses_in_comm()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Restart_recovers_requested_intent()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Reused_pid_generation_missing_identity_and_previous_boot_are_refused()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Unobserved_or_failed_kill_retains_stop_debt()
reverse-coverage suite=session-runner row=Card1619LinuxStopIntentTests concrete=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests declaring=Antiphon.SessionRunner.Tests.Card1619LinuxStopIntentTests signature=Unwritable_record_directory_does_not_block_stop()
reverse-coverage suite=session-runner row=Card1619RunnerExitTests concrete=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests declaring=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests signature=Dead_host_manifest_preserves_signal()
reverse-coverage suite=session-runner row=Card1619RunnerExitTests concrete=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests declaring=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests signature=Exit_while_detached_survives_adoption()
reverse-coverage suite=session-runner row=Card1619RunnerExitTests concrete=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests declaring=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests signature=Http_exit_and_list_keep_signal()
reverse-coverage suite=session-runner row=Card1619RunnerExitTests concrete=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests declaring=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests signature=Old_host_adoption_keeps_signal_unknown()
reverse-coverage suite=session-runner row=Card1619RunnerExitTests concrete=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests declaring=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests signature=Phone_home_exit_and_recovery_keep_signal()
reverse-coverage suite=session-runner row=Card1619RunnerExitTests concrete=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests declaring=Antiphon.SessionRunner.Tests.Card1619RunnerExitTests signature=Stale_generation_does_not_replace_exit_evidence()
```

### Antiphon.Agents.Pty.Tests

```text
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=External_SIGKILL_preserves_signal()
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=External_SIGTERM_preserves_signal()
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=Fast_exit_is_replayed_once()
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=Normal_nonzero_preserves_code()
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=Normal_zero_has_known_no_signal()
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=Porta_exitSignal_private_contract_holds()
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=Signal_exit_preserves_actual_zero_and_separate_signal()
reverse-coverage suite=agents-pty row=Card1619UnixExitTests concrete=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests declaring=Antiphon.Agents.Pty.Tests.Card1619UnixExitTests signature=Unsupported_contract_keeps_signal_unknown(System.String)
```

### Antiphon.PtyHost.Tests

```text
reverse-coverage suite=pty-host row=Card1619LinuxHostExitTests concrete=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests declaring=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests signature=Exited_attach_and_status_replay_identical_evidence()
reverse-coverage suite=pty-host row=Card1619LinuxHostExitTests concrete=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests declaring=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests signature=Live_SIGKILL_matches_manifest()
reverse-coverage suite=pty-host row=Card1619LinuxHostExitTests concrete=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests declaring=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests signature=Live_SIGTERM_matches_manifest()
reverse-coverage suite=pty-host row=Card1619LinuxHostExitTests concrete=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests declaring=Antiphon.PtyHost.Tests.Card1619LinuxHostExitTests signature=Normal_exit_code_survives_detached_host()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Attempt_eviction_keeps_the_latest_256_in_request_order()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Blocked_record_write_still_signals_within_bound()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Crash_after_send_before_update_is_unknown()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Crash_before_send_is_only_requested()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Fallback_records_SIGHUP_separately()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Host_kill_is_not_child_wait_evidence()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Issued_update_follows_syscall()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Natural_exit_race_is_not_our_kill()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Planted_issued_record_and_self_signal_remain_unconfirmed()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Signal_precedes_durable_record_when_storage_works()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Slow_helper_does_not_hold_gate_over_unrelated_stop()
reverse-coverage suite=pty-host row=Card1619StopIntentTests concrete=Antiphon.PtyHost.Tests.Card1619StopIntentTests declaring=Antiphon.PtyHost.Tests.Card1619StopIntentTests signature=Write_failure_still_stops_and_preserves_live_issuer_attribution(System.Boolean)
```

## Pending

Add the required rows, recompute policyHash with owner tooling, rebuild and verify all four suites plus the requested census/roster checks and real land-graph -Check.
