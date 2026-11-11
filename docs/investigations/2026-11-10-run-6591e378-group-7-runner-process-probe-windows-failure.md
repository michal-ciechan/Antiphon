# Run 6591e378 group-7: RunnerProcessProbeTests Windows failure

Read-only extract of the one failed test in `antiphon-all-group-7` on desktop qualification run `6591e378` (`rc/2026-11-15` = `ac6d00022e940ee3b90584c7d313567b4491c498`). The qualification was still running. Nothing under the release checkout, the log directory, AppHost, the production runner, Docker, or any session was modified.

## Verdict

Timing/load wall-clock budget miss, not a deterministic assertion on returned state, and not a Docker-outage victim. The late-start result that the test checked was the expected shape (`TimedOut` true, `Started` false, `CleanupConfirmed` false). The next assertion, `elapsed.Elapsed < 2s`, failed because the stopwatch read `00:00:02.6420745`. Reap-after-release was not reached. The same files are unchanged on `origin/master` `bc71a95e7710532a9a68416f1d7b9e158fe96a15` (fetched 2026-11-11; 190 commits after `ac6d00022`; `git diff --stat` of `server/Infrastructure/Agents/Tui/` is empty).

## Where it ran

| Item | Value |
|---|---|
| Log directory | `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378` |
| TRX | `antiphon-all-group-7.trx` (568392 bytes) |
| Console | `antiphon-all-group-7-tests.log` (no separate console file under the `antiphon-all-group-7*` name) |
| Timing | `antiphon-all-group-7.timing.json` |
| Trace | `antiphon-all-group-7-execution\log_261111020924997.diag` |
| Computer | `DESKTOP-KTLKPIF` |
| Checkout under test | `C:\Antiphon\releases\checkout-4dd2a426` |
| Host | TUnit 1.44.0.0, 64-bit, Windows 10.0.19045, .NET 9.0.22 |
| Chunk window | `2026-11-11T02:09:24.4853951Z` to `2026-11-11T02:11:03.6741790Z` |
| TRX times | start `2026-11-11T02:09:25.2459394Z`, finish `2026-11-11T02:11:03.0470443Z` |
| Chunk result | total 294, executed 294, passed 293, failed 1, timeout 0, skipped 0 |
| Wall | tests.log `1m 37s 868ms`; timing `runWallSeconds` 99.022 |
| Timing file also records | `buildSeconds` 655.862, `buildSlot` `not-used`, `fixtureSetupSeconds` 4.522, p95 5.108s, max 11.127s |

Name filter `antiphon-all-group-7*` also listed groups 70, 71, 72, 74, 75, 76, 77, and 79. Those chunks were not the failure and were not read.

## Failed test

`Antiphon.Tests.AgentTui.RunnerProcessProbeTests.Late_process_start_after_the_one_second_deadline_is_reaped`

| Item | Value |
|---|---|
| Outcome | Failed |
| Duration | `00:00:02.8172957` (tests.log: `2s 817ms`) |
| Start | `2026-11-11T02:09:36.5215383+00:00` |
| End | `2026-11-11T02:09:39.3388340+00:00` |
| executionId | `22e4a092-3035-41b5-a4f2-7742eb85141a` |
| Source line | `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs:748` |
| Categories | Integration, Slow |
| Class limits | `[NotInParallel("RunnerProcessProbe")]`, `[ParallelLimiter<ProcessSpawnLimit>]` |

Exact TRX message:

```
ShouldAssertException: elapsed.Elapsed
    should be less than
00:00:02
    but was
00:00:02.6420745
```

Stack, top frames (the rest is the same awaiter pair, then `TUnit internals omitted`):

```
at Antiphon.Tests.AgentTui.RunnerProcessProbeTests.Late_process_start_after_the_one_second_deadline_is_reaped()
   in C:\Antiphon\releases\checkout-4dd2a426\tests\Antiphon.Tests\AgentTui\RunnerProcessProbeTests.cs:line 748
at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
at System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)
at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task, ConfigureAwaitOptions options)
```

The assertion that failed is the wall-clock check. Shouldly stops on the first failure, so these earlier checks passed:

```
result.TimedOut.ShouldBeTrue();
result.Started.ShouldBeFalse();
result.CleanupConfirmed.ShouldBeFalse();
elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
```

Not reached, so this run does not show whether the late start was reaped:

```
reaper.TrackedProcessCount.ShouldBe(1);
releaseStart.TrySetResult();
await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
```

The stopwatch starts only after the stubbed `StartProcessAsync` is entered. Test wall 2.817s minus measured 2.642s is about 175ms of setup before that stopwatch (`CreateScratch`, helper script, `startEntered` wait). The 2.642s is the wait for `RunAsync` after the start seam, not fixture time.

`RunAsync` creates `new CancellationTokenSource(_timeout)` with `_timeout` of 1 second before it calls the start seam, then waits with `startTask.WaitAsync(linked.Token)`. A prompt cancel should therefore finish in under one second of stopwatch time. `CleanupConfirmed == false` matches `InterruptedBeforeStart` when `startTask` is already assigned, which it is once the seam has been entered. The returned state is the late-start timeout shape. The timer or `WaitAsync` observation was late by about 1.6s past the 1s deadline, and 642ms past the 2s test budget.

## Stdout and stderr on the failed result

Stdout on this result (tests.log and TRX agree):

- Assembly banners: test hosts do not launch the production session runner, and Hangfire server is disabled.
- testcontainers.org connected to Docker at `npipe://./pipe/docker_engine`, Server Version 29.5.3, Kernel `6.18.58.1-microsoft-standard-WSL2`, API 1.54, Docker Desktop, Total Memory 7.76 GB.
- Container `3e7fdfbb8f54` created, started, and ready (`pg_isready` on `antiphon_test`). Log timestamps inside that block run from 0.13s to 1.98s.
- `ANTIPHON_TEST_PHASE_ID Antiphon.Tests.AgentTui.RunnerProcessProbeTests.1.1.Late_process_start_after_the_one_second_deadline_is_reaped.1.1.0`

Stderr, one line:

```
C1420 schema-cache image key=<omitted> ms=3412
```

That is a schema-cache timing line, not a Docker connection error. 3412ms is longer than this test's 2.817s wall, so the cache build is not inside the measured 2.642s. It is process stderr attached to the result. Docker was up: the client connected and the container became ready. There is no sharing-violation text, no poll-expiry text, and the TRX `timeout` counter is 0.

The diag trace records the same message and the same 2.8172957 duration. It does not add another exception.

## What ran at the same time

55 `UnitTestResult` rows overlap `02:09:36.521Z`–`02:09:39.339Z`. The only `RunnerProcessProbeTests` row in that window is the failure. The class is `NotInParallel("RunnerProcessProbe")`, so its siblings did not run beside it.

Heavy overlaps, all Passed, all starting at `02:09:36.521Z` unless noted:

AgentTuiLaunchResolverTests, 21 tests, 4.337s–5.486s, still running when this test ended:

- `ModelTier_maps_against_the_resolved_profile_kind_and_omits_raw` 5.486s
- `Resolver_test_schemas_start_profile_empty_and_do_not_share_profiles` 5.407s
- `C0167_sentinel_key_value_reaches_env_and_nothing_else` 5.368s
- `C0167_no_endpoint_leaves_env_and_args_byte_identical` 5.252s
- `Resolve_fails_closed_for_disabled_profile_and_unknown_model` 5.191s
- `C0167_unsupported_kind_refuses_at_resolve` 5.162s
- `C0167_direct_mode_suppresses_the_project_endpoint` 5.156s
- `C0167_agent_endpoint_outranks_agent_launch_env_and_override_outranks_all` 5.112s
- `C0167_codex_endpoint_appends_five_args_after_extra_args_and_tripwire_passes` 5.108s
- `T2_declared_model_argument_appends_the_tier_alias_once` 5.046s
- `Resolve_appends_exact_model_as_separate_arguments` 4.970s
- `Resolve_uses_installation_default_when_agent_has_no_profile` 4.932s
- `C0167_project_endpoint_outranks_project_default_env_on_the_same_name` 4.831s
- `Resolve_injects_managed_secrets_and_keeps_wrapper_profiles_secret_free` 4.823s
- `C0167_agent_launch_env_outranks_project_endpoint` 4.823s
- `Grok_resolve_appends_model_and_uses_structured_activity` 4.745s
- `Resolve_omits_model_argument_when_agent_has_no_exact_model` 4.710s
- `Resolve_expands_dollar_env_on_profile_args_and_leaves_extra_args_literal` 4.675s
- `T4_exact_model_on_a_blank_field_profile_is_model_argument_unsupported` 4.520s
- `T1_blank_model_argument_name_suppresses_the_tier_alias` 4.491s
- `Explicit_TierModelAlias_wins_over_ModelTier` 4.337s

`AgentTuiModelArgumentMigrationTests.T9_null_grok_revision_gains_model_flag_raw_stays_null` 4.802s, same start.

AgentTuiSecretProtectorTests, 10 tests, 2.303s–3.131s, overlapping the whole failure:

- `Restored_key_ring_under_wrong_certificate_is_not_ready` 3.131s (start 02:09:36.586)
- `Configured_protector_rejects_dtd_bearing_key_ring_xml` 2.979s
- `Restored_key_ring_with_permissive_file_acl_is_rejected_without_repair` 2.938s (start 02:09:36.613)
- `Configured_protector_rejects_oversized_key_ring_xml` 2.904s
- `Valid_revocation_xml_is_not_treated_as_a_persisted_key` 2.863s (start 02:09:36.724)
- `Provider_created_key_file_is_tightened_before_acceptance` 2.778s (start 02:09:36.616)
- `Restored_key_file_symlink_is_rejected_without_mutating_target` 2.742s (start 02:09:36.710)
- `Configured_protector_rejects_corrupt_extra_key_xml` 2.700s (start 02:09:36.710)
- `Readiness_check_rejects_insecure_key_ring_permissions_without_repairing_them` 2.526s (start 02:09:36.874)
- `Configured_protector_rejects_unreadable_persisted_key_material` 2.303s (start 02:09:37.142)

Shorter overlaps that also passed: `Corrupt_key_xml_present_at_startup_is_not_ready` 0.351s, `Legacy_resolution_rejects_selected_profile_when_resolver_is_absent` 0.144s, `Already_cancelled_first_caller_does_not_create_shared_work` 0.141s, plus the remaining sub-50ms rows in the 55.

Other qualification chunks: among `*.timing.json` in that log directory, the only file overlapping this window is group-7 itself. `antiphon-all-group-8.timing.json` starts at `2026-11-11T02:11:03Z`, when group-7 completed. The load in evidence is inside this test host, not a second group.

## Passing siblings in RunnerProcessProbeTests

32 passed, 1 failed, 33 results. Passed names:

- `Blocking_path_inspection_observes_the_one_second_deadline` 1.016s
- `Bounded_startup_reports_forced_tree_termination_as_not_clean` 8.580s
- `Bounded_startup_that_exits_on_stdin_close_is_clean` 8.060s
- `Executable_path_rejects_an_existing_non_executable_file`
- `Executable_path_rejects_execute_access_denied_to_current_identity`
- `Helper_resolves_the_current_powershell_executable_cross_platform` 0.480s
- `Inherited_path_over_the_ceiling_drops_relative_entries` 0.514s
- `Inherited_path_over_the_ceiling_is_bounded_on_an_entry_boundary_and_the_probe_runs` 0.892s
- `Inherited_path_with_nothing_bounded_is_still_refused_before_start(all-missing)`
- `Inherited_path_with_nothing_bounded_is_still_refused_before_start(only-entry-over-ceiling)`
- `Inherited_path_with_nothing_bounded_is_still_refused_before_start(only-relative-entries)`
- `Invalid_utf8_output_is_discarded_as_unusable` 0.468s
- `Oversized_inherited_non_path_value_is_still_refused_before_start`
- `Probe_cancellation_kills_the_tree_and_redacts_secret_canaries` 0.703s
- `Probe_caps_combined_stdout_and_stderr_and_signals_truncation` 0.704s
- `Probe_excludes_undeclared_host_environment_and_keeps_declared_values` 0.489s
- `Probe_preserves_argument_boundaries_without_shell_interpretation` 0.540s
- `Probe_redacts_common_credential_names_in_assignments_and_json` 0.559s
- `Probe_redacts_credential_shaped_output_without_persisting_raw_values` 0.483s
- `Probe_redacts_many_same_line_assignments_near_the_output_limit_within_budget` 11.127s
- `Probe_rejects_excessive_aggregate_redaction_material_before_start`
- `Probe_rejects_more_redaction_values_than_environment_entries_before_start`
- `Probe_timeout_kills_the_entire_process_tree` 10.069s
- `Reaper_host_stop_kills_every_already_started_tracked_process` 10.406s
- `Reaper_shutdown_waits_for_a_starting_guard_then_kills_and_rejects_future_starts` 0.090s
- `Required_file_rejects_a_file_without_read_access`
- `Required_file_rejects_read_access_denied_to_current_identity`
- `Truncated_output_is_discarded_instead_of_returning_secret_fragments` 0.477s
- `Unconfirmed_primary_cleanup_is_owned_and_completed_by_the_reaper` 10.434s
- `Unexpected_exception_after_registration_transfers_process_to_reaper_cleanup` 0.884s
- `Working_directory_rejects_traverse_access_denied_to_current_identity`
- `Working_directory_requires_search_access_on_unix_and_exists_on_windows`

The sibling that uses the same 1s timeout and the same `< 2s` budget is `Blocking_path_inspection_observes_the_one_second_deadline`. It passed in `00:00:01.0157571` at `2026-11-11T02:10:26Z`–`02:10:27Z` (diag), about 47s after this failure, on the path-check deadline rather than `RunAsync`. The chunk's only failure is this one test, so `RunnerProcessProbeRedactionTests` in the same TRX also passed.

The long siblings (`Probe_timeout_kills_the_entire_process_tree` 10.069s, reaper tests around 10s, bounded-startup tests around 8s) are expected waits. They passed, and they did not overlap this test.

## Classification

Timing/load. Evidence:

- The failed check is `elapsed.Elapsed`, expected less than `00:00:02`, actual `00:00:02.6420745`.
- The returned-state checks immediately above it passed. This is not an assertion that the probe returned the wrong timeout or start flags.
- TRX `timeout` count is 0. The test method finished. This is not a harness poll expiry.
- No sharing-violation text in the result output.
- One sample. The same-chunk sibling deadline test met the same 2s budget in 1.016s once the opening herd had moved on. That bounds a deterministic extra delay in the shared 1s timer, and it does not by itself prove the `RunAsync` path is fast when idle.
- The opening herd is the 21 resolver tests plus the migration test and 10 protector tests above, all in this process at the same start instant.

## Docker-down window

Cited window: `16:46Z`–`16:55Z`. This chunk ran `2026-11-11T02:09:24Z`–`02:11:03Z`, about nine hours after `16:55Z` on 2026-11-10. `docs/investigations/2026-11-10-docker-outage-victim-chunks.txt` on `origin/feat/card-task-88c9a263` (`37548b733fb934875875130e32a192bab7cebd29`) lists only:

- `antiphon-all-group-115.trx`
- `antiphon-all-group-116.trx`
- `antiphon-all-group-117.trx`
- `antiphon-all-group-118.trx`

Group-7 is not on that list. The failed result's stdout shows Docker connected and the test container ready.

## Master since ac6d00022

`git fetch origin master` on 2026-11-11 left `origin/master` at `bc71a95e7710532a9a68416f1d7b9e158fe96a15`. `git rev-list --count ac6d00022e940ee3b90584c7d313567b4491c498..origin/master` is 190.

No commit in that range touches:

- `server/Infrastructure/Agents/Tui/RunnerProcessProbe.cs`
- `server/Infrastructure/Agents/Tui/RunnerProcessReaper.cs`
- `server/Infrastructure/Agents/Tui/RunnerProcessCleanup.cs`
- `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs`
- `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeRedactionTests.cs`
- `tests/Antiphon.Tests/AgentTui/RunnerProbePowerShellFixtureTests.cs`

`git diff --stat ac6d00022 origin/master -- server/Infrastructure/Agents/Tui/` is empty. The 2s line is still line 748, blamed to `3949403c94e4b7295ec8f6ae2669f8ab6178f5d3` (`fix(agent-tui): harden discovery and probe recovery`, 2026-08-13). The failure is not already fixed on master.

## Why this rc missed it

The 2s cap is one second of slack over a one-second `CancellationTokenSource`, and the stopwatch starts after that source is already running. CARD-0050's comment in the same test file records that full-suite parallel load made a cold pwsh start exceed 6s and made 1s probe timeouts fire, so tests whose scenario does not need the timeout use 30s. This test does need the timeout, and the 2s wall check stayed. A quiet path-check sibling met that same budget later in this chunk. A Docker-outage repair would not cover this chunk: Docker was up, and group-7 is not in the victim list. The product lines that were actually checked returned the late-start shape; the rc failure is the budget under the opening parallel herd.

## Debug handoff

Cause: `Late_process_start_after_the_one_second_deadline_is_reaped` returned the expected late-start flags and then failed `elapsed < 2s` at 2.642s while the group-7 host was in its opening parallel herd (21 launch-resolver tests at 4.3–5.5s plus 10 secret-protector tests at 2.3–3.1s). Not Docker-down. Not fixed on `origin/master` `bc71a95e7710532a9a68416f1d7b9e158fe96a15`. Reap-after-release was not observed because the budget assertion aborted the test.

File: `docs/investigations/2026-11-10-run-6591e378-group-7-runner-process-probe-windows-failure.md`
