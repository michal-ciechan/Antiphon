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

## Policy repair

Baseline evidence commit: a339d2ddf4cda667e5d4ea095fc0cf8debf99ead.

Added six rc Windows exclusion rows, owned by CARD-1619, using the established Linux-only reason prefix. All six classes already import Antiphon.TestSupport and carry the real pinned NativeLinuxOnlyAttribute; no marker, witness, baseline, legacy digest, assertion or production code changed.

| Suite | Class | Source inspection confirming Linux dependency |
|---|---|---|
| session-runner | Card1619LinuxCustodyStopIntentTests | RequireV1 calls RequireNativeLifecycle, requires delegated cgroup-v1 pids/freezer roots, checks /proc descendants and actual SIGKILL custody records. |
| session-runner | Card1619LinuxStopIntentTests | Fixture launches /bin/sh; tests inspect /proc stat/boot ID and LinuxProcessIdentity before real process signals, with /proc storage refusal. |
| session-runner | Card1619RunnerExitTests | NativeFixture launches /bin/sh, reads /proc PID start ticks and calls libc kill to test actual detached host/runner exits. |
| agents-pty | Card1619UnixExitTests | Child uses /bin/sh with the real Porta UnixPty backend and libc kill; validates actual signal/code observation. |
| pty-host | Card1619LinuxHostExitTests | Detached Linux host launches /bin/sh and checks /proc start ticks before libc kill; verifies manifest and live evidence. |
| pty-host | Card1619StopIntentTests | Real stop fixture launches /bin/sh, invokes libc kill and checks durable signal records and unwritable /proc diagnostics. |

The prior fafc0e86 report and commits f516c6fc11ab8545b6dde4e5168dafd4cdb9afec and 54d3fb068a71b05a4cafa6e1673502a0282ea3e3 were read. This follows their row pattern and retains synthetic fixture isolation. The referenced c6a737d3 report is absent from its origin branch; the actual base failures were independently reproduced above.

Get-NightlyPolicyHash from scripts/lib/nightly-common.ps1 and scripts/lib/nightly-policy.ps1 recomputed policyHash:

- Old: 7de962a40547f495be039cdde12a71e62d7ae4e21e0ada18b76982f87dae6387
- New: 833dc8223602c98959a9d6d0b571ca01401bb5a4210e074cf6d4344b78715004

Pinned attribute SHA-256 remains 31ca5740f4127fe7b3b0fc18639e75262cdb3ae49c3bc545e62a130b99d68ac4. Pinned legacy digest remains 8397984d90368a0ef1cbc5991610ae50328165b40ac2503559ae0562d72b554d. The new prefixed rows are witnessed without adding them to the frozen migration roster.

## Pending

Rebuild and verify all four suites plus the requested census/roster checks and real land-graph -Check.
