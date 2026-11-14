# CARD-1892 stale test sweep — 2026-11-14

Latest continuation: 2526 executed / 2524 passed / 2 failed / 4 skipped at current master8cec1e74. 1 capped row(s); 22 additional stdout pass observations. Final remaining coverage: 265 classes with no result and 3 partial; see the second-window ledger at the end.

The original bounded phase found four stale-contract families (two supplied, two additional) and separate fixture/native/infrastructure failures. **The caller refinement below adds one stale PhoneHome family, a standing-switch admission defect candidate, and two platform-fixture failures.** **The sweep is incomplete:** 504 Linux project/class candidates were inventoried; 382 have no result, 1 has only partial method coverage, and 4 selected classes have only skips. The commissioned 150-minute limit and failed broker prerequisite leave further execution owed. This report does not establish that the failure list is exhaustive and does not authorize landing.

After the caller-refinement rows at the end of this report: five stale-contract families; 17 distinct failing cases overall; 506 required classes, 380 unobserved and three partial. The full sweep remains incomplete.

Baseline captured from current origin/master at task start: **b559bfba2e9a420495122d27171352fe9b7e1ba4**. Assigned branch: feat/card-task-15406ecc. All ten native test binaries report source version **9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572**; that commit changes this investigation document only relative to the baseline. Subsequent task commits also change only this document. No product/test file was edited; the task applies no repair. Native-output builds and runs consistently used UseAppHost=false. This is direct Debug evidence, not a validated checkpoint/Review receipt or a reviewedSourceClean assertion.

Final origin/master fetch resolved to 8cec1e74e75819ef20756c228add987ce4ae4170. Execution remains bound to the b559bfba2 baseline and the documented 9fcc87bcb binaries; later master changes were not retested.

## Findings and ownership

| Exact failing test | Classification | Known/new and action | Windows/live-run mapping |
|---|---|---|---|
| `Antiphon.Tests.Application.LandRefusalDeliveryWitnessTests.C1196_Pretyping_transport_failure_recovers_the_same_row` | STALE-CONTRACT | CARD-1922; Known g54; repair Debug b4f39ca7; rows K1 | Runs; live g54 from caller. Source golden boundary b-application-9fd234bdfd738b4c. |
| `Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original` | STALE-CONTRACT | CARD-1923; Known g58; repair Debug 31c683a8; rows K2 | Runs; live g58 from caller. Source golden boundary b-application-1b385fb3e0942825. |
| `Antiphon.Tests.Application.GrokRulesCompactionRecoveryTests.Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(in-flight, standing)` | STALE-CONTRACT | CARD-1924; New family; reproduced alone in R001; rows F001, R001 | Runs; live numeric group unmapped. Source golden boundary b-application-1db57d97244c217d. |
| `Antiphon.SessionRunner.Tests.ClaudeAuthProbeTests.Fresh_config_dir_reports_logged_out_with_the_real_cli` | REAL-DEFECT (fixture isolation) | CARD-1927; R002 failed alone; R003 passed only with the inherited OAuth variable removed from the owned child; rows F005b, R002 | Runs if claude is on PATH; failure depends on inherited auth environment. Live chunk unmapped. |
| `Antiphon.SessionRunner.Tests.C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock` | STALE-CONTRACT | CARD-1926; New lock-isolation family; rows F006b | Portable runner test runs; live numeric chunk unmapped. |
| `Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_CrashedOwnerLeaseReclaimed` | REAL-DEFECT (fixture child host) | CARD-1928; Known bare-pwsh mechanism; this Herdr witness newly recorded; R006 reproduced without measurement phase variables; rows F009, R006 | Class runs and this child path executes; live numeric chunk unmapped. |
| `Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_ChangedAndSymlinkSocketsAreNotReclaimed` | REAL-DEFECT (fixture child host) | CARD-1928; Same family as crashed-owner child; R006 reproduced without measurement phase variables; rows F009, R006 | Method returns early on Windows; it does not exercise these Linux socket assertions there. No live Windows obligation for this body. |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Lost_host_leaves_unknown_not_receipt` | REAL-DEFECT (fixture cleanup) | CARD-1929; Related CARD-1614 in Review; current exact cleanup failure separately filed; rows P005 | NativeLinuxOnly: excluded from Windows; no live Windows chunk. |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Missing_tree_never_finalizes_naturally` | REAL-DEFECT (fixture fault injection) | CARD-1930; Related CARD-1614 in Review; current exact failure separately filed; rows P005 | NativeLinuxOnly: excluded from Windows; no live Windows chunk. |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Natural_descendant_exit_finalizes_without_release` | REAL-DEFECT (native completion/fixture, attribution unresolved) | CARD-1931; Required once-alone retry R004 failed again; not LOAD; rows P005, R004 | NativeLinuxOnly: excluded from Windows; no live Windows chunk. |
| `Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Populated_tree_refuses_entry_and_drain_success` | REAL-DEFECT (native witness/admission) | CARD-1932; Related CARD-1614 in Review; candidate-only source restriction remains material; rows P005 | NativeLinuxOnly: excluded from Windows; no live Windows chunk. |
| `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_adapter(main)` | REAL-DEFECT (fixture/infrastructure startup) | CARD-1934; B027 main readiness timeout; UID-isolated R005 failed during resource-reaper creation; not LOAD; rows B027, R005 | Class runs with Docker prerequisites; live numeric group unmapped. Source golden boundary b-application-de068477e74c8a56. |
| `Antiphon.Tests.Application.PostLandMutationDeliveryTests.C699_Recovery_error_sweeps_other_missed_events_without_waiting_fifteen_minutes` | REAL-DEFECT (completion recovery or fixture, attribution unresolved) | CARD-1935; B043 failed; required once-alone R007 failed identically; not LOAD; rows B043, R007 | Portable class runs; live numeric group unmapped. Source golden boundary b-application-aee89f81f90e3164. |

The additional CARD-1892 repairs are CARD-1924 (Grok late confirmation followed by an expected new body in the same flush) and CARD-1926 (slot lease incorrectly using Input as a workspace mutation lock owner). Coordinate the supplied families with b4f39ca7/CARD-1922 and 31c683a8/CARD-1923. The already-repaired AgentTaskMidTurnRefineTests family passed 39/39 in F008. No unrelated fixture failure below is attributed as introduced by CARD-1892.

### Exact messages and classification basis

#### Antiphon.Tests.Application.LandRefusalDeliveryWitnessTests.C1196_Pretyping_transport_failure_recovers_the_same_row

STALE-CONTRACT — CARD-1922. IOException at queue-before-typing now retains the charged Sent/null-verdict attempt; the fixture still expects Pending and an automatic second attempt.

```text
ShouldAssertException: (reverted.Status == QueuedMessageStatus.Pending
            && reverted.Attempts == 1
            && reverted.Baseline is not null
            && reverted.StartedAt is not null)
    should be
True
    but was
False

Additional Info:
    C1196:pretyping-reverted
```

Source frame: `at Antiphon.Tests.Application.LandRefusalDeliveryWitnessTests.C1196_Pretyping_transport_failure_recovers_the_same_row(CancellationToken ct) in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/LandRefusalDeliveryWitnessTests.cs:line 514`

#### Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original

STALE-CONTRACT — CARD-1923. Lost Input reply becomes an uncertain submission; the test expects Pending instead of the persisted Sent/null-verdict hold. Its later retry assumptions also need review.

```text
ShouldAssertException: row.Status
    should be
QueuedMessageStatus.Pending
    but was
QueuedMessageStatus.Sent
```

Source frame: `at Antiphon.Tests.Application.CodexCliObservationGapTests.C1029_Unobservable_timestamp_floor_is_original() in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/CodexCliObservationGapTests.cs:line 359`

#### Antiphon.Tests.Application.GrokRulesCompactionRecoveryTests.Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(in-flight, standing)

STALE-CONTRACT — CARD-1924. The prior Grok input is LateConfirmed, then the test expects a second body in that same flush. CARD-1892 composerless recovery returns after confirming receipt with no input write; drive a subsequent safe flush.

```text
ShouldAssertException: h.Adapter.SubmittedBodies
    should have single item but had
0
    items and was
[] (System.Collections.Generic.List`1[System.String])
```

Source frame: `at Antiphon.Tests.Application.GrokRulesCompactionRecoveryTests.Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(String lane, String population) in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/GrokRulesCompactionRecoveryTests.cs:line 172`

#### Antiphon.SessionRunner.Tests.ClaudeAuthProbeTests.Fresh_config_dir_reports_logged_out_with_the_real_cli

REAL-DEFECT (fixture isolation) — CARD-1927. The test overrides the probe environment accessor, while ProcessStartInfo still inherits the parent OAuth variable. A fresh config directory alone does not imply logged out. Production OAuth provisioning is valid; repair the witness environment.

```text
ShouldAssertException: dto.LoggedIn
    should be
False
    but was
True
```

Source frame: `at Antiphon.SessionRunner.Tests.ClaudeAuthProbeTests.Fresh_config_dir_reports_logged_out_with_the_real_cli() in /work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests.cs:line 199`

#### Antiphon.SessionRunner.Tests.C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock

STALE-CONTRACT — CARD-1926. The fixture uses held Input as the global workspace mutation lock owner. CARD-1892 deliberately removed Input/ConditionalInput from that lock. Hold a genuine workspace mutation to retain the lease-serialization witness.

```text
ShouldAssertException: entered.Task.IsCompleted
    should be
False
    but was
True

Additional Info:
    the lease entered the pool while the mutation lock was held
```

Source frame: `at Antiphon.SessionRunner.Tests.C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock(CancellationToken ct) in /work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests.cs:line 409`

#### Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_CrashedOwnerLeaseReclaimed

REAL-DEFECT (fixture child host) — CARD-1928. The owned PowerShell child cannot initialize the loaded test assembly and never publishes READY. No delivery/ownership assertion is reached. Inner module exception was not probed; do not assert a particular missing dependency.

```text
IOException: Herdr peer child did not bind '/work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.Tests.dll': ; \u001b[31;1mParentContainsErrorRecordException: \u001b[0m/tmp/c801-peer-a710fa2c590f4ab99d8206844a375d04.ps1:6\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1mLine |\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m   6 | \u001b[0m \u001b[36;1m$endpoint = [Activator]::CreateInstance($endpointType, [object[]]@($n\u001b[0m …\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m     | \u001b[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1mException calling "CreateInstance" with "2" argument(s): "The type\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1minitializer for '<Module>' threw an exception."\u001b[0m

```

Source frame: `at Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_CrashedOwnerLeaseReclaimed() in /work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs:line 239`

```text
IOException: Herdr peer child did not bind '/work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.Tests.dll': ; \u001b[31;1mParentContainsErrorRecordException: \u001b[0m/tmp/c801-peer-5a1d9ccb8d874d71a3a857159456adad.ps1:6\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1mLine |\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m   6 | \u001b[0m \u001b[36;1m$endpoint = [Activator]::CreateInstance($endpointType, [object[]]@($n\u001b[0m …\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m     | \u001b[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1mException calling "CreateInstance" with "2" argument(s): "The type\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1minitializer for '<Module>' threw an exception."\u001b[0m

```

Source frame: `at Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_CrashedOwnerLeaseReclaimed() in /work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs:line 239`

#### Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_ChangedAndSymlinkSocketsAreNotReclaimed

REAL-DEFECT (fixture child host) — CARD-1928. The PowerShell child fails its test-assembly module initializer before endpoint readiness.

```text
IOException: Herdr peer child did not bind '/work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.Tests.dll': ; \u001b[31;1mParentContainsErrorRecordException: \u001b[0m/tmp/c801-peer-36b99ed461d0488a9cdcc0426b80d0d3.ps1:6\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1mLine |\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m   6 | \u001b[0m \u001b[36;1m$endpoint = [Activator]::CreateInstance($endpointType, [object[]]@($n\u001b[0m …\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m     | \u001b[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1mException calling "CreateInstance" with "2" argument(s): "The type\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1minitializer for '<Module>' threw an exception."\u001b[0m

```

Source frame: `at Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_ChangedAndSymlinkSocketsAreNotReclaimed() in /work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs:line 395`

```text
IOException: Herdr peer child did not bind '/work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/bin/Debug/net9.0/Antiphon.SessionRunner.Tests.dll': ; \u001b[31;1mParentContainsErrorRecordException: \u001b[0m/tmp/c801-peer-8ca74ba1e389464c96948c195cea8f03.ps1:6\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1mLine |\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m   6 | \u001b[0m \u001b[36;1m$endpoint = [Activator]::CreateInstance($endpointType, [object[]]@($n\u001b[0m …\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m     | \u001b[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1mException calling "CreateInstance" with "2" argument(s): "The type\u001b[0m
\u001b[31;1m\u001b[0m\u001b[36;1m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[0m\u001b[36;1m\u001b[31;1m\u001b[31;1m\u001b[36;1m\u001b[31;1m\u001b[36;1m     | \u001b[31;1minitializer for '<Module>' threw an exception."\u001b[0m

```

Source frame: `at Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests.C801_ChangedAndSymlinkSocketsAreNotReclaimed() in /work/worktrees/task-15406ecc/tests/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests.cs:line 395`

#### Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Lost_host_leaves_unknown_not_receipt

REAL-DEFECT (fixture cleanup) — CARD-1929. After the expected lost-host/successor identity refusal, cleanup re-reads tracking through the invalidated original store binding and throws. Preserve production identity validation; retain owned cleanup identities before invalidating the store.

```text
VerificationCustodyException: verification_custody_identity_mismatch
```

Source frame: `at Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.Lost_host_leaves_unknown_not_receipt() in /work/worktrees/task-15406ecc/tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs:line 382`

#### Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Missing_tree_never_finalizes_naturally

REAL-DEFECT (fixture fault injection) — CARD-1930. Directory.Delete on the root-owned cgroup tree is denied in the observation hook, so the intended missing-tree condition is never injected. Repair qualified owned fault injection; do not weaken custody evidence or shared host permissions.

```text
IOException: Access to the path '/sys/fs/cgroup/pids/antiphon-custody/1c9eed5a-f213-411a-98d5-22230db44469/6d2cca1f-fc7c-4f5f-823b-6f58bf2a1f46/tree' is denied.
```

Source frame: `at Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Missing_tree_never_finalizes_naturally() in /work/worktrees/task-15406ecc/tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs:line 238`

#### Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Natural_descendant_exit_finalizes_without_release

REAL-DEFECT (native completion/fixture, attribution unresolved) — CARD-1931. A receipt never arrives after root/descendant exit before the existing 20-second cancellation. Capture native ownership/observation and lifecycle facts on the qualified lane; do not widen the timeout or invent terminal evidence.

```text
TaskCanceledException: A task was canceled.
```

Source frame: `at Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Natural_descendant_exit_finalizes_without_release() in /work/worktrees/task-15406ecc/tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs:line 179`

#### Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Populated_tree_refuses_entry_and_drain_success

REAL-DEFECT (native witness/admission) — CARD-1932. The fixture requires all sampled PIDs to remain alive across entry/drain observations, although HoldLine spawns transient 100ms sleep children. Exact failed PID identities were not captured. Candidate-only admission is also not enforced by its Linux/helper-presence guard. Post-run installed helper hashes match the repository; no stale-helper claim is made.

```text
ShouldAssertException: alive
    should be
True
    but was
False
```

Source frame: `at Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests.C1614_Populated_tree_refuses_entry_and_drain_success() in /work/worktrees/task-15406ecc/tests/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests.cs:line 455`

#### Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.C519_Queue_to_adapter(main)

REAL-DEFECT (fixture/infrastructure startup) — CARD-1934. Owned Redpanda startup fails before product assertions. The two other baseline argument variants passed. The retry failed at a different Docker startup stage, so a single root cause and CARD-1892 causation are unproved. Five further broker-dependent method rows were deferred.

```text
TimeoutException: The operation has timed out.
```

Source frame: `at Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.QueueToAdapterAsync(String kind, Boolean busy) in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs:line 42`

```text
TaskCanceledException: The operation was canceled.
```

Source frame: `at Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.QueueToAdapterAsync(String kind, Boolean busy) in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/ChannelOutboundUnifiedTransportTests.cs:line 42`

#### Antiphon.Tests.Application.PostLandMutationDeliveryTests.C699_Recovery_error_sweeps_other_missed_events_without_waiting_fifteen_minutes

REAL-DEFECT (completion recovery or fixture, attribution unresolved) — CARD-1935. Both expected completion stamps do not appear after a targeted recovery error signal. The exact SQL fault predicate and worker fallback behavior were not captured, so fixture drift/timing versus product recovery remains unresolved. This is not a Pending/uncertain-input expectation.

```text
TimeoutException: one targeted failure must sweep both the requested task and an unrelated missed event
```

Source frame: `at Antiphon.Tests.Application.PostLandMutationDeliveryTests.C699_Recovery_error_sweeps_other_missed_events_without_waiting_fifteen_minutes() in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/PostLandMutationDeliveryTests.CompletionRecovery.cs:line 70`

### Interpretation limits and why these were missed

CARD-1892 Code/Review evidence used explicit class lists. Its Code evidence explicitly says the wider derived closure was not run. The current committed index caps each production path at 25 consumers; its owed rows are obligations, not coverage. Indirect harness callers matter: both supplied failures are absent from the direct SessionMessageQueueService consumer list and are found by delivery-cut/outcome vocabulary. The new Grok compaction witness was not in the named Code roster; the slot-custody witness still states that Input owns the workspace mutation lock, directly contradicting commit 282999c2c. Caller cancellation/refinement repairs on current master do not repair those other assumptions.

The auth witness fails because a real child inherits OAuth despite its injected environment accessor; R002 stays red alone and R003 passes only after removing that variable in the owned child. No credential value or raw auth response was printed. This is a diagnostic environment comparison, not a source fix and not LOAD. The two Herdr startup failures reproduce in R006 with all measurement-added phase variables removed, so those phase variables do not explain their module-initializer failure. The inner exception remains unprobed; historical bare-pwsh failures elsewhere are context, not proof of its exact missing dependency.

The native custody failures relate to CARD-1614 (currently Review), whose description was inspected. Four exact current failures are separately filed, without altering that frozen branch. R004 repeats the natural-exit timeout alone and fails; it is not LOAD. The populated-tree test says candidate image only / standing runner must not execute it, while its executable guard only checks Linux and helper/root presence. This ordinary sweep of the shipped class is not candidate-image qualification. Post-run read-only hashes match installed common/enter/kill helpers to repository bytes (aec68b78151fe619ba461cbe7472e8558f66384c101940addc3fd9b68f3bcccc; e0aa13fd35fd88bb99dd792db355fa2f0d6d0143d13c9fb694a4dd43185e86b9; 68d965dac0d416247e2b123aa6551a5e450030270dc7f7024cf33f0abd6d03c8). No helper deployment/restart or stale-helper attribution was made.

The broker main variant fails in readiness, while trailing and machine pass. Its once-alone UID retry fails in resource-reaper creation. No delivery assertion is reached by the failing attempts. The retry does not pass, so it is not LOAD; five further broker-dependent method rows remain deferred. Successful argument variants measured 218.506–224.004 seconds each; future busy-adapter selection must split arguments by UID rather than put three successful variants in one ten-minute row.

No test/product fix, live Windows/desktop operation, host restart, shared-stack deployment, volume deletion or manual production cleanup was performed. Test fixtures owned their temporary resources. All test driver processes were joined before settlement. The 34d41b7e Windows files were not accessed from Linux. Only g54/g58 are live numeric mappings supplied by the caller; committed golden boundary IDs are source-roster references, not inferred live group numbers.

## Selection reconstruction

Range: 552331802f04cde48d672c48374c90d0d158a612..15afa484617287e68cbbd7e2ab15044305f53f80 (first CARD-1892 test commit parent through round-3/CARD-1905 evidence). Production inventory is the six paths below, including Input lock isolation and late-confirm attention closure. The original selector produced 132 hand/selected class names and 137 derived names before project/platform resolution.

```sh
pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range '552331802f04cde48d672c48374c90d0d158a612..15afa484617287e68cbbd7e2ab15044305f53f80' -Json
git log origin/master -i --grep='1892\|1905' --format='%H %s'
git diff --name-only 552331802f04cde48d672c48374c90d0d158a612..15afa484617287e68cbbd7e2ab15044305f53f80 -- server src
```

The uncapped diagnostic compiles an ignored copy of tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs, changing only Cap=25 to 100000 and ByteLimit=2000000 to 100000000, then writes its Utf8 result to ignored evidence instead of the tracked index. The original builder, production and test files remain unchanged. The union has 272 direct/reverse production consumer names; 257 are absent from the prior Code evidence named roster. An absent exception-file edge is not proof of no impact: static-member references and indirect callers require the vocabulary scan.

| Production path | Uncapped consumers | Retained cap | Owed | Names absent from prior Code evidence |
|---|---:|---:|---:|---:|
| server/Application/Exceptions/InputDeliveryUncertainException.cs | 0 | 0 | 0 | 0 |
| server/Application/Services/AgentSessionService.cs | 63 | 25 | 38 | 58 |
| server/Application/Services/AttentionService.cs | 46 | 25 | 21 | 41 |
| server/Application/Services/GrokRulesRefreshService.cs | 29 | 25 | 4 | 25 |
| server/Application/Services/SessionMessageQueueService.cs | 150 | 25 | 125 | 147 |
| src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs | 44 | 25 | 19 | 42 |

Vocabulary scans cover InputDeliveryUncertainException, LostReply/loseInputReply, transport failure, all new IOException constructions (including deferred Task.FromException), Pending plus fault/throw/transport/exception, DeliveryVerdict, LateConfirmed, ConditionalInput, REMOTE_FIXTURE_REFUSAL, Fault.Fired, QueueBeforeTyping, before-enqueue and after-submit. Class membership was audited with Roslyn syntax, removing two synthetic class names inside fixture strings. Census history: preliminary 472 Linux selections; syntax correction 470; deferred-IOException expansion 504. Nine statically Windows-only project/class selections are separate. The final class table is the auditable union, not a passing claim. Broader helper-only reverse edges were retained as diagnostic candidates in ignored evidence and are not asserted as complete transitive coverage.

## Results and cost

All recorded runs: executed **2477**, passed **2457**, failed **20**, skipped **166**. These are execution totals with intentional isolation/family repeats. Excluding the sanitized R003 auth diagnostic: executed 2476, passed 2456, failed 20, skipped 166. There are 13 distinct failing test cases in the completed evidence. Historical failed attempts stay failed; no aggregate clean full-suite result is claimed.

Rows are serial. Each build/run took a separate host slot; no lease wrapped a loop of builds. Direct builds used native default outputs. Test execution was capped below ten minutes; admission wait is separate. F004b wall 593.509s includes 15s slot wait; TUnit execution was 574.042s. Host load is the full unedited /proc/loadavg start/end sample. Pure timeouts were retried once alone (R004, R005 and R007); none passed.

| Row | Project | Exit | Executed/passed/failed/skipped | Wall s | Slot wait/held s | /proc/loadavg start | /proc/loadavg end | Exact selection |
|---|---|---:|---|---:|---|---|---|---|
| K1 a1 | Antiphon.Tests | 2 | 24/23/1/0 | 168.516 | 0/167 | 14.16 15.68 15.86 3/3971 1758 | 27.78 19.59 17.19 8/4133 16026 | `/*/*/LandRefusalDeliveryWitnessTests/*` |
| K2 a1 | Antiphon.Tests | 2 | 7/6/1/0 | 409.265 | 0/408 | 27.78 19.59 17.19 16/4151 16095 | 26.73 24.75 20.67 9/4367 28373 | `/*/*/CodexCliObservationGapTests/*` |
| S031 a1 | Antiphon.Tests | 0 | 62/62/0/0 | 161.778 | 60/101 | 21.65 23.71 20.47 5/4339 14753 | 12.32 20.52 19.94 4/4125 8472 | `/*/Antiphon.Tests.Application/SessionMessageQueueWedgedHeadTests/*` |
| S024 a1 | Antiphon.Tests | 0 | 28/28/0/0 | 119.06 | 30/88 | 12.32 20.52 19.94 6/4120 8553 | 29.54 22.17 20.48 6/4157 21176 | `/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/*` |
| S025 a1 | Antiphon.Tests | 0 | 5/5/0/0 | 169.167 | 45/123 | 29.54 22.17 20.48 7/4158 21176 | 21.21 20.95 20.25 8/4167 4288 | `/*/Antiphon.Tests.Application/SessionMessageQueueDeliveredSpillRecoveryTests/*` |
| F001 a1 | Antiphon.Tests | 2 | 144/143/1/14 | 150.768 | 60/90 | 22.47 20.84 20.23 9/4101 32540 | 22.08 22.11 20.81 6/4097 9503 | `/*/*/GrokRules*/*` |
| F002 a1 | Antiphon.Tests | 0 | 304/304/0/13 | 439.519 | 75/364 | 22.08 22.11 20.81 10/4105 9511 | 18.08 19.25 20.38 31/4135 1232 | `/*/*/SessionMessageQueue*/*` |
| F003 a1 | Antiphon.Tests | 0 | 230/230/0/1 | 132.804 | 30/102 | 18.08 19.25 20.38 22/4134 1237 | 22.47 19.06 20.07 16/4197 29282 | `/*/*/AgentSession*/*` |
| F004 a1 | Antiphon.SessionRunner.Tests | 8 | 0/0/0/0 | 63.794 | 60/2 | 22.47 19.06 20.07 19/4191 29290 | 21.07 20.08 20.40 26/4270 28212 | `/*/*/(C1514HostLoadSampleTests)\|(C1514HostLoadSpoolTests)\|(C1514HostLoadWireTests)/*` |
| F005 a1 | Antiphon.SessionRunner.Tests | 8 | 0/0/0/0 | 37.507 | 30/6 | 21.07 20.08 20.40 28/4268 28213 | 22.09 20.28 20.45 26/4181 9925 | `/*/*/(C1722GitTimeoutBudgetTests)\|(ClaudeAuthProbeTests)\|(ClaudeDialogInputGuardTests)\|(CodexAuthProbeTests)\|(CodexProviderAuthRoutingTests)\|(GrokAuthProbeTests)\|(PhoneHomeCommandDispatcherTests)\|(PhoneHomeConnectionServiceTests)\|(PtyConditionalReadTests)\|(RemoteControlConditionalInputTests)\|(RunnerAbsenceEvidencePhoneHomeTests)\|(RunnerMultilinePromptDeliveryTests)\|(RunnerReadJsonTests)\|(TranscriptSinceReadTests)/*` |
| F006 a1 | Antiphon.SessionRunner.Tests | 8 | 0/0/0/0 | 9.133 | 0/7 | 22.09 20.28 20.45 5/4182 9931 | 26.83 21.36 20.80 12/4281 11799 | `/*/*/(C0824WorkspaceSlotCustodyTests)\|(C0824WorkspaceSlotOverflowTests)\|(C0824WorkspaceSlotRehomeTests)\|(C0824WorkspaceSlotResetTests)/*` |
| F007 a1 | Antiphon.SessionRunner.Tests | 8 | 0/0/0/0 | 8.207 | 0/6 | 26.83 21.36 20.80 13/4287 11806 | 30.03 22.21 21.08 44/4312 13065 | `/*/*/(BlockedParkWireTests)\|(C1677RetentionDispatchTests)\|(C1677TempSweepDispatchTests)\|(C1817RunnerRepositoryMaintenanceTests)\|(C1818GcOwnerRobustnessTests)\|(Card1619RunnerExitTests)\|(CompletedCardWorkspaceRemovalTests)\|(HerdrAttachWorkspaceGuardTests)\|(HerdrLabelSnapshotTests)\|(HerdrPaneDisposalServiceTests)\|(HerdrSnapshotBoundedReadTests)\|(HostStatsProbeParseTests)\|(LegacyWorkspaceRemovalGateTests)\|(ProcessSpawnLimitTests)\|(RunnerCapacityCountTests)\|(RunnerCustodyReleaseTests)\|(RunnerStartupDiagnosticsTests)\|(RunnerWorkspaceBindingTests)\|(RunnerWorkspaceLinkResolutionTests)\|(RunnerWorkspaceServiceTests)\|(StandingRunnerCwdTests)\|(TerminalSeatReleaseTests)\|(UnixPtyArgvAdmissionTests)\|(WorkspaceParkPublicationTests)\|(WorkspaceSourceVerificationWireTests)/*` |
| F004b a1 | Antiphon.SessionRunner.Tests | 0 | 20/20/0/2 | 593.509 | 15/577 | 22.80 21.14 20.76 5/4175 855 | 21.45 22.05 21.81 29/4123 27954 | `/*/*/C1514HostLoad*/*` |
| F005b a1 | Antiphon.SessionRunner.Tests | 2 | 217/216/1/0 | 45.287 | 30/14 | 21.45 22.05 21.81 30/4100 28007 | 16.28 20.69 21.36 6/4088 4234 | `/*/*/(C1722GitTimeoutBudgetTests*)\|(ClaudeAuthProbeTests*)\|(ClaudeDialogInputGuardTests*)\|(CodexAuthProbeTests*)\|(CodexProviderAuthRoutingTests*)\|(GrokAuthProbeTests*)\|(PhoneHomeCommandDispatcherTests*)\|(PhoneHomeConnectionServiceTests*)\|(PtyConditionalReadTests*)\|(RemoteControlConditionalInputTests*)\|(RunnerAbsenceEvidencePhoneHomeTests*)\|(RunnerMultilinePromptDeliveryTests*)\|(RunnerReadJsonTests*)\|(TranscriptSinceReadTests*)/*` |
| F006b a1 | Antiphon.SessionRunner.Tests | 2 | 95/94/1/0 | 88.144 | 45/42 | 16.28 20.69 21.36 8/4088 4237 | 12.31 18.23 20.43 6/3948 6169 | `/*/*/C0824Workspace*/*` |
| F007b a1 | Antiphon.SessionRunner.Tests | 0 | 472/472/0/21 | 272.212 | 30/241 | 12.31 18.23 20.43 5/3949 6171 | 22.45 19.05 19.92 9/4170 22730 | `/*/*/(BlockedParkWireTests*)\|(C1677RetentionDispatchTests*)\|(C1677TempSweepDispatchTests*)\|(C1817RunnerRepositoryMaintenanceTests*)\|(C1818GcOwnerRobustnessTests*)\|(Card1619RunnerExitTests*)\|(CompletedCardWorkspaceRemovalTests*)\|(HerdrAttachWorkspaceGuardTests*)\|(HerdrLabelSnapshotTests*)\|(HerdrPaneDisposalServiceTests*)\|(HerdrSnapshotBoundedReadTests*)\|(HostStatsProbeParseTests*)\|(LegacyWorkspaceRemovalGateTests*)\|(ProcessSpawnLimitTests*)\|(RunnerCapacityCountTests*)\|(RunnerCustodyReleaseTests*)\|(RunnerStartupDiagnosticsTests*)\|(RunnerWorkspaceBindingTests*)\|(RunnerWorkspaceLinkResolutionTests*)\|(RunnerWorkspaceServiceTests*)\|(StandingRunnerCwdTests*)\|(TerminalSeatReleaseTests*)\|(UnixPtyArgvAdmissionTests*)\|(WorkspaceParkPublicationTests*)\|(WorkspaceSourceVerificationWireTests*)/*` |
| F009 a1 | Antiphon.SessionRunner.Tests | 2 | 232/230/2/0 | 58.57 | 30/28 | 11.86 17.53 19.33 8/4032 4103 | 17.43 17.67 19.23 5/4142 21494 | `/*/*/(AgentPinPublicationTests*)\|(FakeHerdrServerListenerTests*)\|(HerdrPaneDisposalRedactionTests*)\|(HostStatsSamplerTests*)\|(RunnerAbsenceEvidenceTests*)/*` |
| R002 a1 | Antiphon.SessionRunner.Tests | 2 | 1/0/1/0 | 68.232 | 60/7 | 17.43 17.67 19.23 9/4149 21557 | 28.68 21.53 20.50 8/4128 3255 | `/*/*/ClaudeAuthProbeTests*/Fresh_config_dir_reports_logged_out_with_the_real_cli*` |
| R003 a1 | Antiphon.SessionRunner.Tests | 0 | 1/1/0/0 | 52.691 | 45/7 | 28.68 21.53 20.50 8/4134 3269 | 23.42 20.94 20.33 39/4134 22386 | `/*/*/ClaudeAuthProbeTests*/Fresh_config_dir_reports_logged_out_with_the_real_cli*` [OAuth removed in owned child] |
| R001 a1 | Antiphon.Tests | 2 | 11/10/1/0 | 107.88 | 60/46 | 23.42 20.94 20.33 39/4134 22387 | 15.24 19.71 20.03 7/3966 22239 | `/*/*/GrokRulesCompactionRecoveryTests/Captured_native_boundary_reaches_one_durable_refresh_without_idle_input` |
| F008 a1 | Antiphon.Tests | 0 | 39/39/0/0 | 65.879 | 0/64 | 15.24 19.71 20.03 6/3969 22257 | 15.36 18.82 19.70 6/3979 26533 | `/*/*/AgentTaskMidTurnRefineTests/*` |
| P001 a1 | Antiphon.Agents.Pty.Tests | 0 | 67/67/0/33 | 8.013 | 0/7 | 15.36 18.82 19.70 28/3978 26534 | 14.61 18.60 19.62 15/4001 30564 | `/*/*/(Card1681PtyHangupTests*)\|(Card1685LinuxCustodyRunnerTests*)\|(GrokNativeSessionStoreTests*)\|(ProcessSpawnLimitTests*)\|(PtyCustodyTests*)\|(LinuxCgroupContainmentTests*)\|(PtyUtf8DecoderTests*)/*` |
| P002 a1 | Antiphon.IO.WriteBehind.Tests | 0 | 8/8/0/0 | 4.932 | 0/4 | 14.61 18.60 19.62 5/4001 30577 | 14.48 18.51 19.59 9/3983 1778 | `/*/*/WriteBehindFileSystemTests*/*` |
| P003 a1 | Antiphon.Messaging.Tests | 0 | 5/5/0/0 | 5.155 | 0/4 | 14.48 18.51 19.59 6/3982 1779 | 14.04 18.35 19.53 10/3950 6128 | `/*/*/GatewayMonitorStatusTests*/*` |
| P004 a1 | Antiphon.Observability.Tests | 0 | 8/8/0/0 | 5.769 | 0/5 | 14.04 18.35 19.53 8/3949 6130 | 13.48 18.16 19.46 9/3984 11156 | `/*/*/(LiveTimingTests*)\|(ExternalBoundaryTests*)\|(StartupEndpointTests*)/*` |
| P005 a1 | Antiphon.PtyHost.Tests | 2 | 66/62/4/15 | 36.983 | 0/36 | 13.48 18.16 19.46 10/3984 11157 | 9.64 16.62 18.89 7/3949 2178 | `/*/*/(Card1619StopIntentTests*)\|(HostCustodyTests*)\|(LinuxCgroupCustodyTests*)\|(LinuxCustodyReleaseHostTests*)\|(PipeTestClientDiagnosticsTests*)\|(VerificationCustodyStoreTests*)/*` |
| P006 a1 | Antiphon.Resilience.Tests | 0 | 15/15/0/0 | 20.548 | 15/5 | 9.64 16.62 18.89 5/3949 2182 | 8.27 15.83 18.58 9/3988 25626 | `/*/*/(HttpResilienceRegistrationTests*)\|(ResiliencePredicateTests*)/*` |
| P007 a1 | Antiphon.Server.Git.Tests | 0 | 61/61/0/0 | 4.088 | 0/3 | 8.27 15.83 18.58 10/3988 25631 | 8.33 15.72 18.53 6/3895 29021 | `/*/*/OwnedGitProcessContractTests*/*` |
| P008 a1 | Antiphon.E2E | 8 | 0/0/0/66 | 6.398 | 0/5 | 8.33 15.72 18.53 3/3895 29024 | 8.55 15.64 18.49 8/3986 971 | `/*/*/(AgentTaskLandDeliveryE2ETests*)\|(DispatchBaseWarningDeliveryE2ETests*)\|(OutputDistillationApplyCanaryTests*)/*` |
| P009 a1 | Antiphon.E2E | 0 | 14/14/0/0 | 8.865 | 0/8 | 8.55 15.64 18.49 5/3986 973 | 9.11 15.54 18.42 8/3967 10235 | `/*/*/LandDeliveryFixtureProbeTests*/*` |
| P010 a1 | Antiphon.E2E | 0 | 1/1/0/0 | 82.66 | 0/82 | 9.11 15.54 18.42 8/3967 10235 | 9.99 14.21 17.71 6/3905 11750 | `/*/*/BoardE2ETests*/Board_user_can_create_board_create_card_and_open_card_modal*` |
| P011 a1 | Antiphon.E2E | 0 | 1/1/0/0 | 95.628 | 15/80 | 9.99 14.21 17.71 7/3906 11752 | 8.56 12.51 16.76 5/3875 7341 | `/*/*/BoardE2ETests*/Board_user_can_drag_card_between_columns_and_reload_persists_move*` |
| P012 a1 | Antiphon.E2E | 0 | 1/1/0/0 | 93.997 | 0/93 | 8.56 12.51 16.76 6/3876 7344 | 15.21 13.30 16.59 6/3917 26104 | `/*/*/BoardE2ETests*/Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session*` |
| P013 a1 | Antiphon.E2E | 0 | 1/1/0/0 | 106.781 | 0/106 | 15.21 13.30 16.59 6/3917 26106 | 20.69 16.91 17.58 8/4056 12486 | `/*/*/BoardE2ETests*/Board_manual_xterm_command_marks_session_as_streaming_turn*` |
| R004 a1 | Antiphon.PtyHost.Tests | 2 | 1/0/1/0 | 65.061 | 31/33 | 25.70 18.99 18.19 22/4061 26613 | 24.43 20.15 18.66 3/3870 20596 | `/*/*/LinuxCgroupCustodyTests*/C1614_Natural_descendant_exit_finalizes_without_release*` |
| PB1 a1 | Antiphon.E2E | 0 | 3/3/0/0 | 201.821 | 45/155 | 38.60 26.39 21.13 7/4287 3442 | 14.82 22.42 20.74 4/3987 30162 | `/*/*/BoardE2ETests*/(Board_spawn_reuses_existing_worktree_outside_current_worktree_root*)\|(Board_user_can_edit_workflow_md_and_reload_persists_version*)\|(Board_user_can_review_real_worktree_diff_and_send_inline_comment*)` |
| PB2 a1 | Antiphon.E2E | 0 | 3/3/0/0 | 196.611 | 45/150 | 14.82 22.42 20.74 7/3985 30189 | 23.61 23.06 21.27 15/4253 15292 | `/*/*/BoardE2ETests*/(Board_stopped_claude_session_shows_terminal_overlay_and_resume_button*)\|(Board_failed_claude_resume_missing_session_shows_recovery_actions*)\|(Board_running_session_shows_stop_button*)` |
| P020 a1 | Antiphon.E2E | 8 | 0/0/0/1 | 66.068 | 60/4 | 23.61 23.06 21.27 22/4255 15299 | 23.02 23.62 21.62 6/3980 31362 | `/*/*/DelegationSequencingE2ETests*/An_orchestrator_runs_two_delegates_in_sequence_and_the_second_sees_the_firsts_work*` |
| B027 a1 | Antiphon.Tests | 2 | 3/2/1/0 | 534.816 | 30/504 | 23.02 23.62 21.62 5/3980 31372 | 12.77 12.41 16.32 30/3941 28667 | `/*/Antiphon.Tests.Application/ChannelOutboundUnifiedTransportTests/C519_Queue_to_adapter` |
| R005 a1 | Antiphon.Tests | 2 | 1/0/1/0 | 99.544 | 0/98 | 5.15 9.20 14.41 6/3758 25730 | 15.08 11.60 14.73 6/3793 16807 | `--filter-uid Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Queue_to_adapter(System.String).1.1.0` |
| R006 a1 | Antiphon.SessionRunner.Tests | 2 | 2/0/2/0 | 6.753 | 0/6 | 15.08 11.60 14.73 5/3794 17063 | 14.27 11.49 14.68 7/3805 22551 | `/*/*/FakeHerdrServerListenerTests*/(C801_CrashedOwnerLeaseReclaimed*)\|(C801_ChangedAndSymlinkSocketsAreNotReclaimed*)` [phase variables removed] |
| B111 a1 | Antiphon.Tests | 0 | 132/132/0/0 | 201.732 | 45/155 | 23.30 12.87 14.17 21/4168 14737 | 23.80 17.74 15.84 9/4160 22466 | `/*/Antiphon.Tests.Application/(ChannelOutboundRetryPolicyTests*)\|(ChannelOutboundStorageTests*)\|(ChannelPromptCorrelationTests*)\|(ChannelPromptCorrelationUnitTests*)\|(ChannelReplyDurabilityTests*)\|(ChannelReplyLateConfirmDispatchTests*)/*` |
| B186 a1 | Antiphon.Tests | 0 | 86/86/0/0 | 126.185 | 15/110 | 23.80 17.74 15.84 7/4163 22561 | 25.95 21.21 17.37 8/4153 15048 | `/*/Antiphon.Tests.Application/(RemoteCompletionSpillTests*)\|(RemoteControlMaintenanceQueueTests*)\|(RemoteControlModalPersistenceTests*)\|(RemoteControlModalWatchTests*)\|(RemoteControlRecoveryPtyIntegrationTests*)\|(RemoteControlRecoveryTests*)/*` |
| B187 a1 | Antiphon.Tests | 0 | 51/51/0/0 | 118.288 | 45/72 | 25.95 21.21 17.37 8/4153 15054 | 16.13 18.69 16.88 4/3996 31797 | `/*/Antiphon.Tests.Application/(RemoteExecutableLaunchTests*)\|(RemoteWorkspacePreparerTests*)\|(RemoteWorkspaceReadinessTests*)/*` |
| B043 a1 | Antiphon.Tests | 2 | 8/7/1/0 | 108.103 | 45/62 | 16.13 18.69 16.88 5/3996 31797 | 9.92 15.67 15.98 9/4098 19390 | `/*/Antiphon.Tests.Application/PostLandMutationDeliveryTests/(C699_Committed_settlement_signals_the_task_even_when_publication_is_interrupted*)\|(C699_Recovery_error_sweeps_other_missed_events_without_waiting_fifteen_minutes*)\|(C699_Startup_pages_past_a_full_batch_without_skipping_owed_tasks*)\|(C699_Settlement_event_recovers_failed_publication_without_polling*)\|(C699_Startup_recovers_a_missed_event_and_restarts_do_not_duplicate*)\|(C699_History_is_excluded_from_bounded_recovery_and_stamp_repair_queries*)\|(C699_Backstop_waits_fifteen_minutes_between_successful_sweeps*)\|(C478_V09a_LandProducerToCaller*)` |
| B044 a1 | Antiphon.Tests | 0 | 27/27/0/0 | 128.19 | 30/97 | 9.92 15.67 15.98 4/4098 19390 | 37.59 22.31 18.23 6/4334 14204 | `/*/Antiphon.Tests.Application/PostLandMutationDeliveryTests/(C478_V09b_AcceptedTaskToWorker*)\|(C478_V09c_SettledMutationToCaller*)\|(C478_V09a_LandCrashMatrix*)\|(C478_V09b_WorkerCrashMatrix*)\|(C478_V09c_SettlementCrashMatrix*)\|(C478_G140_LandDestination*)\|(C478_G141_LandIdentity*)\|(C478_G142_LandCompleteness*)` |
| B045 a1 | Antiphon.Tests | 0 | 8/8/0/0 | 144.024 | 75/68 | 37.59 22.31 18.23 11/4334 14213 | 13.19 18.73 17.52 4/3883 26445 | `/*/Antiphon.Tests.Application/PostLandMutationDeliveryTests/(C478_G143_LandFreshness*)\|(C478_G155_CompletionIdentity*)\|(C478_G157_LandNotReport*)\|(C478_G160_CompletionSession*)\|(C478_G149_CompletionPersist*)\|(C478_CompletionReplayAfterRetention*)\|(C478_CompletionReplayAfterPartialEnqueueRetention*)\|(C478_CompletionNoteInsertStampAtomicity*)` |
| R007 a1 | Antiphon.Tests | 2 | 1/0/1/0 | 153.313 | 45/106 | 26.79 21.92 18.90 11/4240 29380 | 22.46 21.43 19.14 9/4094 29234 | `/*/*/PostLandMutationDeliveryTests*/C699_Recovery_error_sweeps_other_missed_events_without_waiting_fifteen_minutes*` |
| B046 a1 | Antiphon.Tests | 0 | 8/8/0/0 | 117.417 | 60/56 | 22.46 21.43 19.14 12/4097 29361 | 20.50 21.22 19.37 9/4272 11470 | `/*/Antiphon.Tests.Application/PostLandMutationDeliveryTests/(C478_CompletionNoteScannerRepairsCanceledRow*)\|(C478_CompletionNoteDuplicateSkipRepairsStamp*)\|(C478_G150_CompletionEnqueue*)\|(C478_G151_CompletionQueueKey*)\|(C478_G152_CompletionWakeup*)\|(C478_G153_CompletionBusy*)\|(C478_G154_CompletionReceipt*)\|(C478_G156_CompletionRestore*)` |
| B047 a1 | Antiphon.Tests | 0 | 3/3/0/0 | 101.797 | 30/70 | 20.50 21.22 19.37 7/4266 11476 | 23.28 22.76 20.15 7/4170 5143 | `/*/Antiphon.Tests.Application/PostLandMutationDeliveryTests/(C478_G158_CompletionCompleteness*)\|(C478_G159_CompletionFreshness*)\|(C478_G161_CompletionSpill*)` |

F004-F007 are historical zero-test filter attempts (exit 8), not passes. F004b-F007b are their corrected filters using the repository's suffix-wildcard convention. P008 and P020 contain only genuine platform skips, not coverage of the native bodies. The S025 driver stopped at the next row boundary before S027 to regroup families; a diagnostic output-boundary exception did not interrupt a test. Later stop markers also act only between joined rows. Individual class startup cost motivated related prefix/family rows; prefix selections include adjacent family guards, inventoried from TRX. No source file was edited during a running selection.

### Builds and other prerequisites

| Prerequisite | Exit | Wall s including admission | Load start | Load end |
|---|---:|---:|---|---|
| build | 0 | 216.92 | 7.71 9.76 14.15 7/3903 23241 | 16.34 16.16 16.01 8/3979 596 |
| tests/Antiphon.SessionRunner.Tests | 0 | 192.423 | 30.62 21.45 17.95 12/4315 8160 | 27.00 25.13 20.15 4/4299 6553 |
| tests/Antiphon.Agents.Pty.Tests | 0 | 38.664 | 25.93 24.53 20.53 2/4457 25423 | 23.40 24.11 20.56 5/4304 6122 |
| tests/Antiphon.E2E | 0 | 58.266 | 23.40 24.11 20.56 8/4303 6123 | 29.22 25.60 21.29 8/4326 28699 |
| tests/Antiphon.IO.WriteBehind.Tests | 0 | 96.692 | 29.22 25.60 21.29 11/4326 28699 | 13.60 21.31 20.18 11/4143 29365 |
| tests/Antiphon.Messaging.Tests | 0 | 44.271 | 13.60 21.31 20.18 10/4144 29366 | 12.70 19.87 19.74 27/4189 15638 |
| tests/Antiphon.Observability.Tests | 0 | 111.463 | 12.70 19.87 19.74 37/4190 15639 | 27.29 22.02 20.46 8/4273 22748 |
| tests/Antiphon.PtyHost.Tests | 0 | 58.807 | 27.29 22.02 20.46 9/4273 22748 | 22.70 21.49 20.36 9/4204 29132 |
| tests/Antiphon.Resilience.Tests | 0 | 119.106 | 22.70 21.49 20.36 14/4204 29132 | 16.40 19.79 19.88 9/4132 7702 |
| tests/Antiphon.Server.Git.Tests | 0 | 86.732 | 16.40 19.79 19.88 9/4132 7702 | 23.69 20.45 20.06 7/4202 27552 |
| client-ci | 0 | 229.866 | 19.00 19.96 20.70 41/4312 27489 | 13.77 17.28 19.51 9/4304 24658 |
| client-build | 0 | 78.387 | 13.77 17.28 19.51 11/4303 24668 | 21.07 20.08 20.40 6/4199 380 |
| catalog-build | 0 | 59.371 | 15.16 17.79 19.73 16/4283 13051 | 30.26 21.22 20.77 16/4222 4050 |
| catalog-run | 0 | 67.79 | 30.26 21.22 20.77 22/4222 4050 | 20.51 19.93 20.34 51/4218 8430 |
| browser-install | 0 | 176.89 | 34.40 23.75 21.62 37/4426 2078 | 15.39 22.75 21.87 7/3917 23742 |

Two earlier auxiliary uncapped-builder prerequisites are retained in their logs (4.11s MSBuild body; 33s census lease) without contemporaneous dedicated load samples. That instrumentation omission is explicit; it does not affect test-row load samples. The later Roslyn catalog build/run above have measured load. npm ci/build prepared a fresh client/dist; Chromium was installed only under the task's ignored browsers directory. The browser rows use the isolated E2E fixture/runner and its production-runner guard. No client test run or product change was commissioned.

### Binary and receipt provenance

| Test project | Native binary SHA-256 | Product/source version |
|---|---|---|
| tests/Antiphon.Tests | fa2517d2317c5849b43d3dc4a249be16e20430b02a5c2a5867db8909607997f0 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.Agents.Pty.Tests | a2f3295915352128c6a6f9d7ccabb25a88186d821a7e99337f7d5b86c1200870 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.E2E | 6712de676e7eb034454e188a1a19889d8b6275ddfc03a95ab8f6bbadd9414b70 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.IO.WriteBehind.Tests | f4cd4f920463e1519eef69af79e6f200cbb8ef9322dd9713ffb73802877d7dc0 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.Messaging.Tests | a4dddab9771627e3329e300fdc1acc38869704b0aa3b82cbb4e13a46003a95d2 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.Observability.Tests | 71215c2627bc78dd11efda0b03250101090c9fecdc5a6e4096accd7e8d7ee4ef | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.PtyHost.Tests | d7b08a81c2e4da29b5035281c5a5ae0f819346892d521885f77113797354add2 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.Resilience.Tests | d19d340cdafce8503e2f7907a5e06dbf677ef14da0c3a289247578363912b1f6 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.Server.Git.Tests | a6fbb812a9bf127abad146ff4a2ac399919e581d4708c2878d560f58e14af044 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| tests/Antiphon.SessionRunner.Tests | 00ff7535e3d8422045cd2775c573e412cdee4efc52e63c57debd03368195aec0 | 1.0.0+9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 |
| Row | TRX SHA-256 |
|---|---|
| K1 a1 | b52f80e5faa0846ef8ab2cde13aef800b4e627626ef87461d150505aa6e9a6d7 |
| K2 a1 | b2797dc2b5b97c971d8b8c5d617e6363a4b6852d0cc4b33f8c1753872edf078d |
| S031 a1 | 9928ecfa5d4b268aa03ff9528bd1481e4fa6fe9aee3f1c15fdbd4f8e76428bc0 |
| S024 a1 | ff94302a089620c52507d8f8f53cc34225fcca1c198f4df296f068ff89070cf5 |
| S025 a1 | 2e32142d3aa4fdc18a9cc2e5da3fe11169528076f8b4a826e3ed5a80355d346f |
| F001 a1 | 3e744d681b9c45940f9f37754b7ce6fb1bb77e40029d0c580d8d9140446f31fe |
| F002 a1 | 68fe87ad667a619e25326c512fcaa4564094cf5fc4fc89449dfb51e0786386d2 |
| F003 a1 | c065cbe4235e7518798dd1791866eda92d95e1f836366a445f10c660ba1eb1b6 |
| F004 a1 | 63d1832bc3a4bd51252cd3550d92ecd09afa83be5acc34fea2fd6e1b854b05d8 |
| F005 a1 | d0d8d6c0c3a25e0109dc0ad72a5446ffb324edc7e60ec07413da70fb650e771b |
| F006 a1 | 6eb659f1b56bab202953d81459ecc1a066be059952463d91c467447cae918abb |
| F007 a1 | 0bb85e90c584d943a6ca64ea931276bb162df59b86f6ba0541d9798e061b03cc |
| F004b a1 | a3f56732dc4357dd00b04085c2f5638acae6b13dd78416ffe30218658c7450cd |
| F005b a1 | 14a306fada92f92ed4b8ed7363ba88f277c8f1b7ffe8841342b883dcf7decd52 |
| F006b a1 | 026479eee17ed6d466c5abe89ef754db820ea2f94300c4d38e68042bc94df475 |
| F007b a1 | 3e50c3a6ee0f2d0f587ddd38c038a0de3851783cf7d74605cc4f1df2a1306e95 |
| F009 a1 | f2c8850f1df5a20aa5306e6e158f9d43bc85bc42473ccad2f3e4d53eda419c68 |
| R002 a1 | ab0593b5a4c71c47e4bcae5fbeb13a16b547d90286b61a83b2093a9c9cbd7bd8 |
| R003 a1 | 1284933cc1c145c11237af65ea1f7ca6296fadd19025b378184eb22d8cd0b69a |
| R001 a1 | bee04811060553f62e6794d1a5a4822fb117251d24d886bca19a0c24a33f7832 |
| F008 a1 | 67aee300f95a4b2c9cdb5b99b4ba417fdb1e2a4b84271b76b8c6aaaefc571701 |
| P001 a1 | 5522d2afe4f1a9ab6ac8cb7093282063ec4862a833c1947c41b45968ee1c6c9e |
| P002 a1 | 46dfe754e2b3cb95300d1a3c8f00d04c902580bf515463f45b07d1dd41bebd86 |
| P003 a1 | cc8257bd79aa97979a008df8d9243a64d4f9a18038658caada7d6eb9082be796 |
| P004 a1 | af8e2abce68de84f53e5caf477bcc666677ba2de30cbcd3cd534dd8d894ebebb |
| P005 a1 | 6c79400d1bbda786a30514957167c8182654fa4d5c063684f4e0b228ea77a917 |
| P006 a1 | d2216af04075e7ea0b11a82d4bdcc056a84c8aee13e27ccd9f78c3c0e1a967ab |
| P007 a1 | 07d61a3415c058df2f8a584862e3fe8083ff9494b52a9c68da1b5d46c4bfe637 |
| P008 a1 | a0403c045070007fb8960f015200304b821e0f4b977d89ce6a02332eb8eab90a |
| P009 a1 | 5fa9e14a5f47e6efaaab165753bf69f89715cb1722af35f10d14405743eae72b |
| P010 a1 | 00ff5c48333a1a192595667a3d3edabfa81953e15c01e90f18d742f460e390c8 |
| P011 a1 | aec638da6079910dc6c140a62043dcc1575f7e69de0c987cf29c6f79b5b70978 |
| P012 a1 | a284d9125052d97db7dd33072329f77c288b0ae752e75ed3a84bc24f3aaea7ca |
| P013 a1 | 67b5c802009e7f8690bf7aedcc255d3fa9f2a7cb1a1088226a9e05b2aeefea31 |
| R004 a1 | 1f8ff406f7be732b8362f98e912e2e2e0c872638563c143ee2ff36cc6ddfc866 |
| PB1 a1 | 2f7cfc157c8db11866cbf3b8ecac1a03656806fe7809bed13e5d56fc7f7d0403 |
| PB2 a1 | 558714bdf7c6f34201cb834a78f68cd8570c1f7beb8d9ffea759682f4a815bc3 |
| P020 a1 | 9a8052ab49d1ff1c377853860e71d39299178993873440a06b7981c693fd5ee4 |
| B027 a1 | 35fc44065e36ef2da0de3cdb114870fac15c70a044f2a8c696df9328dade8f6e |
| R005 a1 | e4edbc920ab7a290652b4d8f180c9a74d50ba6b0184d5b88f1263fd96759430d |
| R006 a1 | 2230a0136c22e987f975a500c2716808f4bd4bfbed004dc047afbcb0bc2bdc72 |
| B111 a1 | a67f07e4c7d29395e0338784d7187b42bce9fd63651b8ae8fda9d3840fb60937 |
| B186 a1 | 4b9da3dab0fe1c33aba02f47ac63053175d4667f760c3e1100657ca39266e94e |
| B187 a1 | 4d5ed9a75bf35df61dda216e1866e968ecb0a3afa0206d7b208c9f2e49abcbb6 |
| B043 a1 | 6eb132f01ad5b326390eeb855200d3666a6bfb0e1c918d8f4b7d0270bde44d7a |
| B044 a1 | 35a4854292b82602e20a2a3cfcdd3b8b28324c66ee3640ac9e7984886585244e |
| B045 a1 | 7c830e306dd9d0f814278196be4cec05888e07751c6d0549d4566f722364484a |
| R007 a1 | 67072de5a582d4eb44d7a9abc5569ba02c759247ef5d9239cdf9a25c17534142 |
| B046 a1 | 666db7ec2c55b25e3b3b481c94f4cc75212ea3f677a736a56653eac990d0b912 |
| B047 a1 | fd06dbeafc88a3ed2660f7973a41a51871708235fd6ba146c7e5d2146d32e4b1 |

Generated JSON/TRX/logs, browser/cache assets and diagnostic builds remain ignored under /work/worktrees/task-15406ecc/.antiphon/sweep15406/. The tracked deliverable is this Markdown report only. These raw payloads are not pushed and can be retired with the worktree; essential counts, messages, hashes, filters and scope are preserved here.

## Coverage ledger and exact rerun selections

Census: 504 Linux project/class candidates; 121 have a full method selection represented, 1 have partial methods and 382 have no result. A full selection can still be red or skipped; it does not mean a clean qualification. 4 selected classes contain only runtime skips. The nine static Windows-only classes below were not attempted as Linux proof. Counts in the class table are unique case identities observed across ordinary runs, with a failure retained if any ordinary attempt failed; the sanitized auth diagnostic does not turn its class green.

Build the project once, then use its row filter; all commands remain gated. A class estimated/measured above ten minutes must be split into its listed method/argument rows, not launched as an unbounded class. Example commands:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1892-build -- dotnet build tests/Antiphon.Tests --nologo -p:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1892-rerun -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/GrokRulesCompactionRecoveryTests*/Captured_native_boundary_reaches_one_durable_refresh_without_idle_input*' --report-trx
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1892-slot-lock -- dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/C0824WorkspaceSlotCustodyTests*/C0824_LeaseWaitsForTheDispatcherMutationLock*' --report-trx
```

| Project | Class | Prior Code roster named | State | Unique pass/fail/skip | Evidence rows | Exact class filter (split large classes) |
|---|---|---|---|---|---|---|
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.Card1681PtyHangupTests | no | FULL SELECTION: passed observations | 30/0/0 | P001 | `/*/Antiphon.Agents.Pty.Tests/Card1681PtyHangupTests/*` |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.Card1685LinuxCustodyRunnerTests | no | FULL SELECTION: passed observations | 1/0/0 | P001 | `/*/Antiphon.Agents.Pty.Tests/Card1685LinuxCustodyRunnerTests/*` |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.GrokNativeSessionStoreTests | no | FULL SELECTION: passed observations | 20/0/3 | P001 | `/*/Antiphon.Agents.Pty.Tests/GrokNativeSessionStoreTests/*` |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.LinuxCgroupContainmentTests | no | FULL SELECTION: passed observations | 11/0/0 | P001 | `/*/Antiphon.Agents.Pty.Tests/LinuxCgroupContainmentTests*/*` |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.ProcessSpawnLimitTests | no | FULL SELECTION: passed observations | 2/0/0 | P001 | `/*/Antiphon.Agents.Pty.Tests/ProcessSpawnLimitTests/*` |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.PtyCustodyTests | no | FULL SELECTION: all skipped | 0/0/30 | P001 | `/*/Antiphon.Agents.Pty.Tests/PtyCustodyTests/*` |
| Antiphon.Agents.Pty.Tests | Antiphon.Agents.Pty.Tests.PtyUtf8DecoderTests | no | FULL SELECTION: passed observations | 3/0/0 | P001 | `/*/Antiphon.Agents.Pty.Tests/PtyUtf8DecoderTests*/*` |
| Antiphon.E2E | Antiphon.E2E.AgentTaskLandDeliveryE2ETests | no | FULL SELECTION: all skipped | 0/0/53 | P008 | `/*/Antiphon.E2E/AgentTaskLandDeliveryE2ETests/*` |
| Antiphon.E2E | Antiphon.E2E.BoardE2ETests | no | FULL SELECTION: passed observations | 10/0/0 | P010, P011, P012, P013, PB1, PB2 | `/*/Antiphon.E2E/BoardE2ETests/*` |
| Antiphon.E2E | Antiphon.E2E.DelegationSequencingE2ETests | no | FULL SELECTION: all skipped | 0/0/1 | P020 | `/*/Antiphon.E2E/DelegationSequencingE2ETests/*` |
| Antiphon.E2E | Antiphon.E2E.DispatchBaseWarningDeliveryE2ETests | no | FULL SELECTION: all skipped | 0/0/13 | P008 | `/*/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests/*` |
| Antiphon.E2E | Antiphon.E2E.LandDeliveryFixtureProbeTests | no | FULL SELECTION: passed observations | 14/0/0 | P009 | `/*/Antiphon.E2E/LandDeliveryFixtureProbeTests/*` |
| Antiphon.E2E | Antiphon.E2E.OutputDistillationApplyCanaryTests | no | NOT RUN | n/a | none | `/*/Antiphon.E2E/OutputDistillationApplyCanaryTests/*` |
| Antiphon.IO.WriteBehind.Tests | Antiphon.IO.WriteBehind.Tests.WriteBehindFileSystemTests | no | FULL SELECTION: passed observations | 8/0/0 | P002 | `/*/Antiphon.IO.WriteBehind.Tests/WriteBehindFileSystemTests/*` |
| Antiphon.Messaging.Tests | Antiphon.Messaging.Tests.Gateway.GatewayMonitorStatusTests | no | FULL SELECTION: passed observations | 5/0/0 | P003 | `/*/Antiphon.Messaging.Tests.Gateway/GatewayMonitorStatusTests/*` |
| Antiphon.Observability.Tests | Antiphon.Observability.Tests.ExternalBoundaryTests | no | FULL SELECTION: passed observations | 2/0/0 | P004 | `/*/Antiphon.Observability.Tests/ExternalBoundaryTests*/*` |
| Antiphon.Observability.Tests | Antiphon.Observability.Tests.LiveTimingTests | no | FULL SELECTION: passed observations | 4/0/0 | P004 | `/*/Antiphon.Observability.Tests/LiveTimingTests/*` |
| Antiphon.Observability.Tests | Antiphon.Observability.Tests.StartupEndpointTests | no | FULL SELECTION: passed observations | 2/0/0 | P004 | `/*/Antiphon.Observability.Tests/StartupEndpointTests*/*` |
| Antiphon.PtyHost.Tests | Antiphon.PtyHost.Tests.Card1619StopIntentTests | no | FULL SELECTION: passed observations | 13/0/0 | P005 | `/*/Antiphon.PtyHost.Tests/Card1619StopIntentTests/*` |
| Antiphon.PtyHost.Tests | Antiphon.PtyHost.Tests.HostCustodyTests | no | FULL SELECTION: passed observations | 4/0/15 | P005 | `/*/Antiphon.PtyHost.Tests/HostCustodyTests/*` |
| Antiphon.PtyHost.Tests | Antiphon.PtyHost.Tests.LinuxCgroupCustodyTests | no | FULL SELECTION: RED | 9/4/0 | P005, R004 | `/*/Antiphon.PtyHost.Tests/LinuxCgroupCustodyTests/*` |
| Antiphon.PtyHost.Tests | Antiphon.PtyHost.Tests.LinuxCustodyReleaseHostTests | no | FULL SELECTION: passed observations | 13/0/0 | P005 | `/*/Antiphon.PtyHost.Tests/LinuxCustodyReleaseHostTests/*` |
| Antiphon.PtyHost.Tests | Antiphon.PtyHost.Tests.PipeTestClientDiagnosticsTests | no | FULL SELECTION: passed observations | 3/0/0 | P005 | `/*/Antiphon.PtyHost.Tests/PipeTestClientDiagnosticsTests/*` |
| Antiphon.PtyHost.Tests | Antiphon.PtyHost.Tests.VerificationCustodyStoreTests | no | FULL SELECTION: passed observations | 20/0/0 | P005 | `/*/Antiphon.PtyHost.Tests/VerificationCustodyStoreTests/*` |
| Antiphon.Resilience.Tests | Antiphon.Tests.Infrastructure.Resilience.HttpResilienceRegistrationTests | no | FULL SELECTION: passed observations | 7/0/0 | P006 | `/*/Antiphon.Tests.Infrastructure.Resilience/HttpResilienceRegistrationTests/*` |
| Antiphon.Resilience.Tests | Antiphon.Tests.Infrastructure.Resilience.ResiliencePredicateTests | no | FULL SELECTION: passed observations | 8/0/0 | P006 | `/*/Antiphon.Tests.Infrastructure.Resilience/ResiliencePredicateTests*/*` |
| Antiphon.Server.Git.Tests | Antiphon.Tests.Infrastructure.OwnedGitProcessContractTests | no | FULL SELECTION: passed observations | 61/0/0 | P007 | `/*/Antiphon.Tests.Infrastructure/OwnedGitProcessContractTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.AgentPinPublicationTests | no | FULL SELECTION: passed observations | 6/0/0 | F009 | `/*/Antiphon.SessionRunner.Tests/AgentPinPublicationTests*/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.BlockedParkWireTests | no | FULL SELECTION: passed observations | 3/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/BlockedParkWireTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotCustodyTests | no | FULL SELECTION: RED | 18/1/0 | F006b | `/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotCustodyTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotOverflowTests | no | FULL SELECTION: passed observations | 12/0/0 | F006b | `/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotOverflowTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotRehomeTests | no | FULL SELECTION: passed observations | 18/0/0 | F006b | `/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotResetTests | no | FULL SELECTION: passed observations | 10/0/0 | F006b | `/*/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotResetTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1514HostLoadSampleTests | no | FULL SELECTION: passed observations | 6/0/0 | F004b | `/*/Antiphon.SessionRunner.Tests/C1514HostLoadSampleTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1514HostLoadSpoolTests | no | FULL SELECTION: passed observations | 6/0/0 | F004b | `/*/Antiphon.SessionRunner.Tests/C1514HostLoadSpoolTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1514HostLoadWireTests | no | FULL SELECTION: passed observations | 2/0/0 | F004b | `/*/Antiphon.SessionRunner.Tests/C1514HostLoadWireTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1677RetentionDispatchTests | yes | FULL SELECTION: passed observations | 14/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/C1677RetentionDispatchTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1677TempSweepDispatchTests | no | FULL SELECTION: passed observations | 10/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/C1677TempSweepDispatchTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1722GitTimeoutBudgetTests | yes | FULL SELECTION: passed observations | 98/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/C1722GitTimeoutBudgetTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1817RunnerRepositoryMaintenanceTests | no | FULL SELECTION: passed observations | 17/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/C1817RunnerRepositoryMaintenanceTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1818GcOwnerRobustnessTests | no | FULL SELECTION: passed observations | 10/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/C1818GcOwnerRobustnessTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.Card1619RunnerExitTests | no | FULL SELECTION: passed observations | 10/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/Card1619RunnerExitTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.ClaudeAuthProbeTests | no | FULL SELECTION: RED | 7/1/0 | F005b, R002, R003 | `/*/Antiphon.SessionRunner.Tests/ClaudeAuthProbeTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.ClaudeDialogInputGuardTests | no | FULL SELECTION: passed observations | 6/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/ClaudeDialogInputGuardTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.CodexAuthProbeTests | no | FULL SELECTION: passed observations | 9/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/CodexAuthProbeTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.CodexProviderAuthRoutingTests | no | FULL SELECTION: passed observations | 1/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/CodexProviderAuthRoutingTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests | no | FULL SELECTION: passed observations | 106/0/1 | F007b | `/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.FakeHerdrServerListenerTests | no | FULL SELECTION: RED | 18/2/0 | F009, R006 | `/*/Antiphon.SessionRunner.Tests/FakeHerdrServerListenerTests*/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.GrokAuthProbeTests | no | FULL SELECTION: passed observations | 3/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/GrokAuthProbeTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.HerdrAttachWorkspaceGuardTests | no | FULL SELECTION: passed observations | 4/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/HerdrAttachWorkspaceGuardTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.HerdrLabelSnapshotTests | no | FULL SELECTION: passed observations | 36/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/HerdrLabelSnapshotTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.HerdrPaneDisposalRedactionTests | no | FULL SELECTION: passed observations | 118/0/0 | F009 | `/*/Antiphon.SessionRunner.Tests/HerdrPaneDisposalRedactionTests*/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.HerdrPaneDisposalServiceTests | no | FULL SELECTION: passed observations | 72/0/20 | F007b | `/*/Antiphon.SessionRunner.Tests/HerdrPaneDisposalServiceTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.HerdrSnapshotBoundedReadTests | no | FULL SELECTION: passed observations | 1/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/HerdrSnapshotBoundedReadTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.HostStatsProbeParseTests | no | FULL SELECTION: passed observations | 11/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/HostStatsProbeParseTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.HostStatsSamplerTests | no | FULL SELECTION: passed observations | 7/0/0 | F009 | `/*/Antiphon.SessionRunner.Tests/HostStatsSamplerTests*/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests | no | FULL SELECTION: passed observations | 11/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/LegacyWorkspaceRemovalGateTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.PhoneHomeCommandDispatcherTests | yes | FULL SELECTION: passed observations | 45/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/PhoneHomeCommandDispatcherTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.PhoneHomeConnectionServiceTests | no | FULL SELECTION: passed observations | 17/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/PhoneHomeConnectionServiceTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.ProcessSpawnLimitTests | no | FULL SELECTION: passed observations | 2/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/ProcessSpawnLimitTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.PtyConditionalReadTests | no | FULL SELECTION: passed observations | 11/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/PtyConditionalReadTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RemoteControlConditionalInputTests | no | FULL SELECTION: passed observations | 4/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/RemoteControlConditionalInputTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerAbsenceEvidencePhoneHomeTests | no | FULL SELECTION: passed observations | 3/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidencePhoneHomeTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerAbsenceEvidenceTests | no | FULL SELECTION: passed observations | 81/0/0 | F009 | `/*/Antiphon.SessionRunner.Tests/RunnerAbsenceEvidenceTests*/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerCapacityCountTests | no | FULL SELECTION: passed observations | 3/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/RunnerCapacityCountTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerCustodyReleaseTests | no | FULL SELECTION: passed observations | 7/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/RunnerCustodyReleaseTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerMultilinePromptDeliveryTests | no | FULL SELECTION: passed observations | 2/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/RunnerMultilinePromptDeliveryTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerReadJsonTests | no | FULL SELECTION: passed observations | 3/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/RunnerReadJsonTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerStartupDiagnosticsTests | no | FULL SELECTION: passed observations | 5/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/RunnerStartupDiagnosticsTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerWorkspaceBindingTests | no | FULL SELECTION: passed observations | 20/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerWorkspaceLinkResolutionTests | no | FULL SELECTION: passed observations | 3/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/RunnerWorkspaceLinkResolutionTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.RunnerWorkspaceServiceTests | no | FULL SELECTION: passed observations | 35/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/RunnerWorkspaceServiceTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.StandingRunnerCwdTests | no | FULL SELECTION: passed observations | 25/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/StandingRunnerCwdTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.TerminalSeatReleaseTests | no | FULL SELECTION: passed observations | 38/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/TerminalSeatReleaseTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.TranscriptSinceReadTests | no | FULL SELECTION: passed observations | 7/0/0 | F005b | `/*/Antiphon.SessionRunner.Tests/TranscriptSinceReadTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.UnixPtyArgvAdmissionTests | no | FULL SELECTION: passed observations | 15/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/UnixPtyArgvAdmissionTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.WorkspaceParkPublicationTests | no | FULL SELECTION: passed observations | 10/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/WorkspaceParkPublicationTests/*` |
| Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.WorkspaceSourceVerificationWireTests | no | FULL SELECTION: passed observations | 4/0/0 | F007b | `/*/Antiphon.SessionRunner.Tests/WorkspaceSourceVerificationWireTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AbandonedLandingReconcilerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AbandonedLandingReconcilerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AbsentLaunchPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentAttachHerdrTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentAttachHerdrTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentChannelServiceIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentChannelServiceIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentControlServiceIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentControlServiceIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentReportStoreTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentReportStoreTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentSessionCancelledTurnTests | yes | FULL SELECTION: passed observations | 20/0/0 | F003 | `/*/Antiphon.Tests.Application/AgentSessionCancelledTurnTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentSessionInterruptedLaunchResumeTests | yes | FULL SELECTION: passed observations | 9/0/0 | F003 | `/*/Antiphon.Tests.Application/AgentSessionInterruptedLaunchResumeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentSessionLaunchFailureTests | yes | FULL SELECTION: passed observations | 79/0/0 | F003 | `/*/Antiphon.Tests.Application/AgentSessionLaunchFailureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentSessionLaunchQueueOwnershipTests | no | FULL SELECTION: passed observations | 7/0/0 | F003 | `/*/Antiphon.Tests.Application/AgentSessionLaunchQueueOwnershipTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentStartRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentStartRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentSupervisionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentSupervisionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentSystemPromptLaunchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentSystemPromptLaunchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskAnswerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskAnswerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskCheckInterpreterTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskCheckInterpreterTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskCheckScheduleTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskCheckScheduleTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskCheckSweepTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskCheckSweepTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskCommitEndpointTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskCommitEndpointTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskDeadSessionReconciliationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskDeadSessionReconciliationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskDeliveryWatchdogTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskDeliveryWatchdogTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskDispatcherWiringTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskDispatcherWiringTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskDispatchFailureTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskDispatchFailureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskInputFallbackTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskInputFallbackTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskInternalDecisionLifecycleTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskInternalDecisionLifecycleTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandActiveSourceClaimTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandActiveSourceClaimTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandAdmissionControlledTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandAdmissionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandAdmissionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandApprovalRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandApprovalRecoveryTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandContractEndpointTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandContractEndpointTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandEnqueueTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandEnqueueTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandFailureDiagnosticTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandFailureDiagnosticTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandHalfResetTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandHalfResetTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandHoldVisibilityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandHoldVisibilityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandMonitoringTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandMonitoringTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandNotificationParkFollowUpTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationParkFollowUpTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandNotificationPersistenceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationPersistenceTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandNotificationRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandNotificationRescanTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationRescanTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandPersistenceFailureTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandPublicationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandQueuedReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandQueuedReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandReceiptWatermarkSafetyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandReceiptWatermarkSafetyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskLandRequestTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskLandRequestTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskMidTurnRefineTests | no | FULL SELECTION: passed observations | 39/0/0 | F008 | `/*/Antiphon.Tests.Application/AgentTaskMidTurnRefineTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskOverdueDeadlineTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskOverdueDeadlineTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskPoolTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskPoolTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskRefineTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskRefineTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskReplyIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskReplyOverlayTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskReplyOverlayTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskReuseEnqueueTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskReuseEnqueueTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskServiceIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskSettlementRaceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskSettlementRaceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskStallEscalationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskStallEscalationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskStandingAgentDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskStandingAgentDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AgentTaskWorktreeLockOutcomeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AgentTaskWorktreeLockOutcomeTests/*` |
| Antiphon.Tests | Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.AgentTui/AgentTuiSecretProtectorTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.AnchoredPathDeleteWitnessTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/AnchoredPathDeleteWitnessTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AttentionApiTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AttentionApiTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AttentionFeedNoiseTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AttentionFeedNoiseTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AttentionServiceTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AttentionServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AwayDigestNotifierTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AwayDigestNotifierTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.AwayDigestProjectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/AwayDigestProjectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedReplyReportRetentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedSeatEscalationNotifierTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedSeatEscalationNotifierTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedTaskNotifierTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedTaskNotifierTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedTaskParkDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedTaskParkProjectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedTaskParkProjectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedTaskParkReclaimTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedTaskParkReleaseTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedTaskParkReleaseTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BlockedTaskSyncRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BlockedTaskSyncRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BoardServiceIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BoardServiceIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BootLivenessProbeScopeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BootLivenessProbeScopeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BootStallAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BootStallAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BootStallDetectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BootStallDetectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BootStallDocumentationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BootStallDocumentationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.BootWedgeRelaunchParityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/BootWedgeRelaunchParityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0747LandReceiptReplayTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0747LandReceiptReplayTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0747QueueRowExpiryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0747QueueRowExpiryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824DesktopLegacyReleaseSwitchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824DesktopLegacyReleaseSwitchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824PublishedBranchDeletionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824RefOnlyCleanupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824RefOnlyCleanupTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824RefOnlyDebtTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824RefOnlyDebtTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824RefOnlyLandingTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824RefOnlyLandingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824RefOnlyReadTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824RefOnlyReadTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824RefOnlyReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824RefOnlyReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824RefOnlySyncTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824RefOnlySyncTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C0824WorkspaceSlotServerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.C1307CliCancellationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/C1307CliCancellationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.C1307ToolRemovalTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/C1307ToolRemovalTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C1514HostLoadHistoryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C1514HostLoadHistoryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.C1677DiskAlarmTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/C1677DiskAlarmTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CapacityAdmissionLifecycleTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CapacityAdmissionLifecycleTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CapacityObsoleteQueueTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CapacityObsoleteQueueTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CapacityRecoveryAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CapacityRecoveryAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CapacityRecoverySupervisionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CapacityRecoverySupervisionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CapacityRecoveryTaskTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CapacityRecoveryTaskTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.Card1619ExitConsumerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/Card1619ExitConsumerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardClosedAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardClosedAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardCorrectionIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardCorrectionIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardFilePrivacyBoundaryAcceptanceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardFilePrivacyBoundaryAcceptanceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardFilePrivacyConcurrencyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardFilePrivacyConcurrencyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardFilePrivacyRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardFilePrivacyRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardPrivateNotesBoundaryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardPrivateNotesBoundaryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardReviewServiceIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardReviewServiceIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardSpawnModelArgumentTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardSpawnModelArgumentTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardVerificationPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardVerificationPolicyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardWorkTransitionServiceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardWorkTransitionServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CardWorktreeMirrorCleanupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CardWorktreeMirrorCleanupTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelBatchingTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelBatchingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelBridgeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelBridgeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelInboundRecoveryTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelInboundRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelIngressIncidentTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelIngressIncidentTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelInputWithheldIncidentTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelInputWithheldIncidentTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundCaptureTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundCaptureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundDeadlineTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundDeadlineTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundFailureRecordingTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundFailureRecordingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundMaterializationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundMaterializationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundMetadataRepairTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundRetryPolicyTests | no | FULL SELECTION: passed observations | 22/0/0 | B111 | `/*/Antiphon.Tests.Application/ChannelOutboundRetryPolicyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundStorageTests | no | FULL SELECTION: passed observations | 33/0/0 | B111 | `/*/Antiphon.Tests.Application/ChannelOutboundStorageTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundTrailingRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ChannelOutboundTrailingRecoveryTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests | no | PARTIAL: RED | 2/1/0 | B027, R005 | `/*/Antiphon.Tests.Application/ChannelOutboundUnifiedTransportTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelPromptCorrelationTests | no | FULL SELECTION: passed observations | 24/0/0 | B111 | `/*/Antiphon.Tests.Application/ChannelPromptCorrelationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelPromptCorrelationUnitTests | no | FULL SELECTION: passed observations | 8/0/0 | B111 | `/*/Antiphon.Tests.Application/ChannelPromptCorrelationUnitTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelReplyDurabilityTests | no | FULL SELECTION: passed observations | 25/0/0 | B111 | `/*/Antiphon.Tests.Application/ChannelReplyDurabilityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ChannelReplyLateConfirmDispatchTests | no | FULL SELECTION: passed observations | 20/0/0 | B111 | `/*/Antiphon.Tests.Application/ChannelReplyLateConfirmDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CheckCompactionAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CheckCompactionAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CheckCompactionAutomaticRestartTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CheckCompactionCrashTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CheckCompactionCrashTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CheckCompactionReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CheckCompactionReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CheckNoteDeliveryHandoffTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CheckNoteDeliveryHandoffTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointEofStallTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointEofStallTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointExecutorLogTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointLaunchCleanupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointLaunchCleanupTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointSourceExecutionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointSourceStateTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceStateTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointStatePublicationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointStatePublicationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointTaskOwnershipTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointTempScopeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointTempScopeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointTerminalReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointTerminalReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointTimingHarnessTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointTimingHarnessTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.CheckpointToolCopyCleanupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/CheckpointToolCopyCleanupTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ClaudeCredentialProbeDispatcherTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ClaudeCredentialProbeDispatcherTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ClaudeDialogDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ClaudeDialogDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ClaudeEffortByRoleDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ClaudeEffortByRoleDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ClosingReportNudgeLifecycleTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ClosingReportNudgeLifecycleTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CodexCliObservationGapTests | no | FULL SELECTION: RED | 6/1/0 | K2 | `/*/Antiphon.Tests.Application/CodexCliObservationGapTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CodexCliObservationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CodexCliObservationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CodexDelegateDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CodexDelegateDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CodexStartupDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CodexStartupDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.CompletionNoteDurableSweepTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/CompletionNoteDurableSweepTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ComplexityAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ComplexityAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ComplexityDispatcherTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ComplexityDispatcherTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ContextCompactionSweepTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ContextCompactionSweepTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DataRetentionServiceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DataRetentionServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DecisionCardNotifierTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DecisionCardNotifierTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DefaultRunnerPinTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DefaultRunnerPinTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DefaultRunnerRerouteTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DefaultRunnerRerouteTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DelegateBranchDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DelegateBranchDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DelegateBundleLaunchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DelegateBundleLaunchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DelegateCheckProbeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DelegateCheckProbeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DelegationBriefRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DelegationBriefRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DelegationScopeHoldTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DelegationScopeHoldTests/*` |
| Antiphon.Tests | Antiphon.Tests.Scripts.DerivedImpactIndexTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Scripts/DerivedImpactIndexTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DesktopLaunchMemoryAdmissionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DesktopLaunchMemoryAdmissionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DiagnosticsBundleServiceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DiagnosticsBundleServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DialogDeliveryAdmissionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DialogDeliveryAdmissionTests/*` |
| Antiphon.Tests | Antiphon.Tests.TestHelpers.DirectoryLinkFixtureTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.TestHelpers/DirectoryLinkFixtureTests*/*` |
| Antiphon.Tests | Antiphon.Tests.TestHelpers.DirectoryLinkFixtureWindowsTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.TestHelpers/DirectoryLinkFixtureWindowsTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DispatchBaseInspectionDiagnosticsTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DispatchBaseInspectionDiagnosticsTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DispatchBaseNotificationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DispatchBaseNotificationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DispatchConcurrencyDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DispatchConcurrencyDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DispatcherSweepLifetimeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DispatcherSweepLifetimeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DispatchHeldAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DispatchHeldAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DispatchHoldVisibilityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DispatchHoldVisibilityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.DockerDeliveryDatabaseTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/DockerDeliveryDatabaseTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.DurableRunnerSpillReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/DurableRunnerSpillReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ExpectationDirectDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ExpectationDirectDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ExpectationEscalationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ExpectationEscalationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ExpectationNoteDebtTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ExpectationNoteDebtTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ExpectationObservationAdapterTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ExpectationObservationAdapterTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ExpectationResponseTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ExpectationResponseTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.ExpectationWatchdogJobTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/ExpectationWatchdogJobTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ExpectationWatchdogScenarioTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ExpectationWatchdogScenarioTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.FileUseCaptureTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/FileUseCaptureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GhostSeatReleaseTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GhostSeatReleaseTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokCredentialProbeDispatcherTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GrokCredentialProbeDispatcherTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokDelegateDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GrokDelegateDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokNativeSessionResumeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GrokNativeSessionResumeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesChannelTests | no | FULL SELECTION: passed observations | 4/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesChannelTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesCompactionAcceptanceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GrokRulesCompactionAcceptanceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesCompactionRecoveryTests | no | FULL SELECTION: RED | 10/1/0 | F001, R001 | `/*/Antiphon.Tests.Application/GrokRulesCompactionRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesCompactionTests | no | FULL SELECTION: passed observations | 2/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesCompactionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesDispatchAcceptanceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GrokRulesDispatchAcceptanceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesFailureTests | yes | FULL SELECTION: passed observations | 7/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesFailureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesInitializationTests | yes | FULL SELECTION: passed observations | 24/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesInitializationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesLaunchRefusalTests | no | FULL SELECTION: passed observations | 3/0/2 | F001 | `/*/Antiphon.Tests.Application/GrokRulesLaunchRefusalTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesLiveMappedDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GrokRulesLiveMappedDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesQueueBarrierTests | no | FULL SELECTION: passed observations | 28/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesQueueBarrierTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesReadyOrderingTests | yes | FULL SELECTION: passed observations | 5/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesReadyOrderingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesReceiptTests | no | FULL SELECTION: passed observations | 16/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesReplayMatrixTests | no | FULL SELECTION: passed observations | 13/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesReplayMatrixTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesResumeMigrationTests | no | FULL SELECTION: passed observations | 6/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesResumeMigrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokRulesTransactionTests | no | FULL SELECTION: passed observations | 3/0/0 | F001 | `/*/Antiphon.Tests.Application/GrokRulesTransactionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.GrokStartupReadyOrderingTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/GrokStartupReadyOrderingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/HerdrAlwaysOnChannelParityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.HerdrLabelFollowFlowTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/HerdrLabelFollowFlowTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.HerdrLabelFollowTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/HerdrLabelFollowTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.HerdrPaneDisposalApplicationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/HerdrPaneDisposalApplicationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.HerdrStatusDisagreementTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/HerdrStatusDisagreementTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.HerdrSupervisionAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/HerdrSupervisionAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointAuthorityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.HostCleanupCheckpointCompatibilityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointCompatibilityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.HostCleanupCheckpointCustodyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointCustodyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.HostCleanupCheckpointReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointReceiptTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/HostCleanupWorktreeInventoryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.InterimVerificationPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/InterimVerificationPolicyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Api.KeepSixReminderTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Api/KeepSixReminderTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/LandEvidenceWarningDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.LandFailureDiagnosticTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/LandFailureDiagnosticTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.LandingGitTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/LandingGitTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/LandingRemovalPolicyControlTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.LandReceiptScanCacheTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/LandReceiptScanCacheTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.LandRefusalDeliveryWitnessTests | no | FULL SELECTION: RED | 23/1/0 | K1 | `/*/Antiphon.Tests.Application/LandRefusalDeliveryWitnessTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.LandRequestWriteDiagnosticTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/LandRequestWriteDiagnosticTests/*` |
| Antiphon.Tests | Antiphon.Tests.ApiKeys.LaunchEnvLayersIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.ApiKeys/LaunchEnvLayersIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.LinuxOwnedGitProcessTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/LinuxOwnedGitProcessTests/*` |
| Antiphon.Tests | Antiphon.Tests.TestHelpers.LiveFixtureTextTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.TestHelpers/LiveFixtureTextTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.LogRetentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/LogRetentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.MatrixChildReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/MatrixChildReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.MatrixDefaultComplexityCreateTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/MatrixDefaultComplexityCreateTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.MidTurnInterruptDocumentationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/MidTurnInterruptDocumentationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.MidTurnInterruptPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/MidTurnInterruptPolicyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ModelAvailabilityDispatcherTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ModelAvailabilityDispatcherTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OrchestratorInstructionsRefreshTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OrchestratorInstructionsRefreshTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OrchestratorServiceIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OrchestratorServiceIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OrchestratorStateProjectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OrchestratorStateProjectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OrchestratorTrackerCadenceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OrchestratorTrackerCadenceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OutputDistillationAdmissionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OutputDistillationAdmissionTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OutputDistillationApplyRaceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OutputDistillationApplyRaceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OutputDistillationCleanupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OutputDistillationCleanupTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OutputDistillationDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OutputDistillationDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OutputDistillationDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OutputDistillationDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.OutputDistillationProducerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/OutputDistillationProducerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.OwnedPtyFixtureCleanupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/OwnedPtyFixtureCleanupTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ParkedMessageSweepServiceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ParkedMessageSweepServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PendingNoteWithholdTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PendingNoteWithholdTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PendingSettlementDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PendingSettlementDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PermissionWaitTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PermissionWaitTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeBackpressureTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeBackpressureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeCompletedCardTransportTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeCompletedCardTransportTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.PhoneHomeConnectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/PhoneHomeConnectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeDeferredKillTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeDeferredKillTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.PhoneHomeEpochAgreementTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/PhoneHomeEpochAgreementTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeImmediateSendTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeImmediateSendTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeLaunchTransportTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeLaunchTransportTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeMirrorJoinTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeMirrorJoinTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeOutageMentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeOutageMentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomePendingInventoryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomePendingInventoryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeQueuedTurnTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeQueuedTurnTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeRollingRunnerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeRollingRunnerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeSessionRoutingTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeSessionRoutingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeSpillTransportTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeSpillTransportTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeStandingLaunchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeStandingLaunchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeStrandedQueueTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeStrandedQueueTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PhoneHomeTaskDispatchProjectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PhoneHomeTaskDispatchProjectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PinnedAgentKindTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PinnedAgentKindTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PinnedCodexProfileDispatchLaunchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PinnedCodexProfileDispatchLaunchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PinnedProfileLaunchSpecTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PinnedProfileLaunchSpecTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PipelineSpeedIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PipelineSpeedIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PolicyRefreshServiceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PolicyRefreshServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PoolClaudeEffortTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PoolClaudeEffortTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PoolProviderProjectionStampTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PoolProviderProjectionStampTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PostLandMutationDeliveryTests | no | FULL SELECTION: RED | 53/1/0 | B043, B044, B045, R007, B046, B047 | `/*/Antiphon.Tests.Application/PostLandMutationDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PostLandMutationWorkflowTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PostLandMutationWorkflowTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PostLandMutationWorktreeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PostLandMutationWorktreeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.PreDispatchFailureDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/PreDispatchFailureDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.ProcessSpawnLimitTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests/ProcessSpawnLimitTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ProviderQuotaRefusalAcceptanceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ProviderQuotaRefusalAcceptanceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.QueuedInputWatchdogTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/QueuedInputWatchdogTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.QuotaEvidenceWireTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/QuotaEvidenceWireTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.RcBridgeProbePlatformTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/RcBridgeProbePlatformTests/*` |
| Antiphon.Tests | Antiphon.Tests.Scripts.RcQualificationWatcherScriptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Scripts/RcQualificationWatcherScriptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ReadyGateFailureNoticeDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ReadyGateFailureNoticeDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ReceiptFailureDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ReceiptFailureDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ReceiptScanDestinationIntegrationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ReceiptScanDestinationIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ReceiptScanDestinationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ReceiptScanDestinationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RefocusBriefIdentityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RefocusBriefIdentityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Scripts.ReleaseImpactEnvelopeTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Scripts/ReleaseImpactEnvelopeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteCompletionSpillTests | no | FULL SELECTION: passed observations | 9/0/0 | B186 | `/*/Antiphon.Tests.Application/RemoteCompletionSpillTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteControlMaintenanceQueueTests | no | FULL SELECTION: passed observations | 32/0/0 | B186 | `/*/Antiphon.Tests.Application/RemoteControlMaintenanceQueueTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteControlModalPersistenceTests | no | FULL SELECTION: passed observations | 11/0/0 | B186 | `/*/Antiphon.Tests.Application/RemoteControlModalPersistenceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteControlModalWatchTests | no | FULL SELECTION: passed observations | 8/0/0 | B186 | `/*/Antiphon.Tests.Application/RemoteControlModalWatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteControlRecoveryPtyIntegrationTests | no | FULL SELECTION: passed observations | 9/0/0 | B186 | `/*/Antiphon.Tests.Application/RemoteControlRecoveryPtyIntegrationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteControlRecoveryTests | no | FULL SELECTION: passed observations | 17/0/0 | B186 | `/*/Antiphon.Tests.Application/RemoteControlRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteExecutableLaunchTests | no | FULL SELECTION: passed observations | 5/0/0 | B187 | `/*/Antiphon.Tests.Application/RemoteExecutableLaunchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteWorkspacePreparerTests | no | FULL SELECTION: passed observations | 27/0/0 | B187 | `/*/Antiphon.Tests.Application/RemoteWorkspacePreparerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RemoteWorkspaceReadinessTests | no | FULL SELECTION: passed observations | 19/0/0 | B187 | `/*/Antiphon.Tests.Application/RemoteWorkspaceReadinessTests/*` |
| Antiphon.Tests | Antiphon.Tests.TestHelpers.ReportBudgetWorkspacePortabilityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.TestHelpers/ReportBudgetWorkspacePortabilityTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.RepositoryMutationLeaseOwnerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/RepositoryMutationLeaseOwnerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/RepositoryMutationLeaseTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RestartFailureClassificationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RestartFailureClassificationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ReviewEvidenceRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ReviewEvidenceRecoveryTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ReviewEvidenceResettlementTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ReviewEvidenceResettlementTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ReviewLoopTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ReviewLoopTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RolePolicyToolBundleDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RolePolicyToolBundleDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RoutingExhaustedPlacementTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RoutingExhaustedPlacementTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RoutingPinCandidateDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RoutingPinCandidateDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RoutingPinDispatcherTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RoutingPinDispatcherTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerAlarmAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerAlarmAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerAlarmDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerAlarmDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerAlarmNotifierTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerAlarmNotifierTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerBranchContractDocumentationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerBranchContractDocumentationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/RunnerClaudeAdapterEffortPromptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/RunnerClaudeAdapterTrustPromptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.RunnerClaudeAdapterVerifiedPromptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/RunnerClaudeAdapterVerifiedPromptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerClaudeFinalLaunchArgvTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerClaudeFinalLaunchArgvTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerCodexCliEvidenceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerCodexCliEvidenceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/RunnerGrokAdapterReadyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerGrokCredentialProbeDispatcherTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerGrokCredentialProbeDispatcherTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerReadSourcePinTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerReadSourcePinTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerSeatOrphanSweepTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerSeatOrphanSweepTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerSettlementSyncTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerSettlementSyncTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerSlotEndpointTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerSlotEndpointTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.RunnerStartupStaleObservationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/RunnerStartupStaleObservationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerSyncLeaseFairnessTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerSyncLeaseFairnessTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RunnerTaskSettlementTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RunnerTaskSettlementTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.RunnerTerminalSessionGenerationTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/RunnerTerminalSessionGenerationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.RuntimeActionClaimQueryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/RuntimeActionClaimQueryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ScheduleCardActionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ScheduleCardActionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ScheduleSweepTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ScheduleSweepTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.ScopeDriftTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/ScopeDriftTests/*` |
| Antiphon.Tests | Antiphon.Tests.Scripts.ScriptHarnessProcessContractTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Scripts/ScriptHarnessProcessContractTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SeatOccupancyAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionGenerationDeliveryOverlapTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionGenerationDeliveryOverlapTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionHealthTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionHealthTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueBootWedgeTests | no | FULL SELECTION: passed observations | 10/0/0 | F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueBootWedgeTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueCodexPointerTests | yes | FULL SELECTION: passed observations | 28/0/0 | S024, F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueCodexPointerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueDeliveredSpillRecoveryTests | no | FULL SELECTION: passed observations | 5/0/0 | S025, F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueDeliveredSpillRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueDeliveredSpillTests | no | FULL SELECTION: passed observations | 9/0/0 | F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueDeliveredSpillTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueDeliveryVerificationTests | no | FULL SELECTION: passed observations | 125/0/0 | F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueDeliveryVerificationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueInterruptedAttemptTests | yes | FULL SELECTION: passed observations | 24/0/0 | F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueInterruptedAttemptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueuePhoneHomeDropTests | yes | FULL SELECTION: passed observations | 5/0/0 | F002 | `/*/Antiphon.Tests.Application/SessionMessageQueuePhoneHomeDropTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueServiceTests | no | FULL SELECTION: passed observations | 26/0/0 | F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueWedgedHeadTests | no | FULL SELECTION: passed observations | 62/0/0 | S031, F002 | `/*/Antiphon.Tests.Application/SessionMessageQueueWedgedHeadTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionReconciliationServiceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionReconciliationServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRunnerAbsenceEvidenceDocumentationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRunnerAbsenceEvidenceDocumentationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRunnerEventPumpTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.SessionRunnerGenerationWireTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/SessionRunnerGenerationWireTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRunnerHeldEventExitMatrixTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRuntimeActionDeferralTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRuntimeActionRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRuntimeActionRoutingTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRuntimeActionTemporaryBlockTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRuntimeActionTemporaryBlockTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionRuntimeActionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionRuntimeActionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionStateCacheReadTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionStateCacheReadTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionStateCommitTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionStateCommitTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionStatePinQueryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionStatePinQueryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionStateWarmupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionStateWarmupTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SessionTerminationSourcePersistenceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SessionTerminationSourcePersistenceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SettlementSyncRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SettlementSyncRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SpecialistAttemptEvidenceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SpecialistAttemptEvidenceTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SpecialistExecutionIdentityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SpecialistExecutionIdentityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SpecialistHealthAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SpecialistHealthAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SpecialistInputTransportTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SpecialistInputTransportTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SpecialistPublicationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SpecialistPublicationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SpecialistQualificationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SpecialistQualificationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SpecialistStartIntentTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SpecialistStartIntentTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.SpecialistToolPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/SpecialistToolPolicyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StaleWorkingTranscriptWatchdogTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingBootAttentionTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingBootAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingBootDocumentationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingBootDocumentationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingBootQueueWitnessTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingBootQueueWitnessTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingBootStatementBudgetTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingBootStatementBudgetTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingBootWatchdogTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingBootWatchdogTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingContinuityAttentionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingContinuityAttentionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingRestartEscalationTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingResumeReceiptReadinessTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingResumeReceiptReadinessTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingSessionQueueSwitchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingSessionQueueSwitchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingSessionSelectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingSessionSelectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingSessionSwitchConcurrencyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.StandingSpecialistSeatTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/StandingSpecialistSeatTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.StartRefGitDirectoryCleanupTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.SubscriptionQuotaGateDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/SubscriptionQuotaGateDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskCeilingControlTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskCeilingControlTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskCompletionProgressPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskCompletionProgressPolicyTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskInputAttentionCostTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskInputAttentionCostTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskInputReadFailureTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskInputReadFailureTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskInputWeakArmBoundaryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskInputWeakArmBoundaryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskLaunchTokenBoundaryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskLaunchTokenBoundaryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskPlatformDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskPlatformDispatchTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskProgressPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskProgressPolicyTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskProgressStallSweepTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskProgressStallSweepTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskSupervisionReceiptTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskSupervisionReceiptTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TaskSupervisionVisibilityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TaskSupervisionVisibilityTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TerminalTaskResidueTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TerminalTaskResidueTests/*` |
| Antiphon.Tests | Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests | yes | NOT RUN | n/a | none | `/*/Antiphon.Tests.TestHelpers/TestLaneCategoryGuardTests/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.TestOwnedPtyHostIdentityTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/TestOwnedPtyHostIdentityTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Agents.TestOwnedPtyHostPolicyTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Agents/TestOwnedPtyHostPolicyTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TranscriptBindingIncidentTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TranscriptBindingIncidentTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TranscriptPromptSpanTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TranscriptPromptSpanTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TranscriptWorkingStateQueryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TranscriptWorkingStateQueryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.UnixDelegateLaunchArgvTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/UnixDelegateLaunchArgvTests/*` |
| Antiphon.Tests | Antiphon.Tests.Checkpoints.UnsafeDeleteRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Checkpoints/UnsafeDeleteRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.VerificationRoundDeliveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.VerificationRoundDispatchTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/VerificationRoundDispatchTests*/*` |
| Antiphon.Tests | Antiphon.Tests.Application.VerificationRoundSettlementTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/VerificationRoundSettlementTests/*` |
| Antiphon.Tests | Antiphon.Tests.Scripts.VolumeRetirementHostProofTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Scripts/VolumeRetirementHostProofTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.WatchdogServiceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/WatchdogServiceTests/*` |
| Antiphon.Tests | Antiphon.Tests.TestHelpers.WindowsDeliveryFixtureWitnessTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.TestHelpers/WindowsDeliveryFixtureWitnessTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.WorkingTranscriptRecoveryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/WorkingTranscriptRecoveryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.WorktreeBaseSelectionTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/WorktreeBaseSelectionTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.WorktreeCleanupJournalTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/WorktreeCleanupJournalTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.WorktreeDefaultCallerTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/WorktreeDefaultCallerTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.WorktreeLandingCleanupRetryTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/WorktreeLandingCleanupRetryTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.WorktreeLockDiagnosticsTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/WorktreeLockDiagnosticsTests/*` |
| Antiphon.Tests | Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Infrastructure/WorktreeNoFollowDeleteTests/*` |
| Antiphon.Tests | Antiphon.Tests.Application.WorktreeRetirementRaceTests | no | NOT RUN | n/a | none | `/*/Antiphon.Tests.Application/WorktreeRetirementRaceTests/*` |

### Required execution still owed

The remaining class/method selections below need a further commissioned serial sweep. The five broker method rows require a working, measured bootstrap first. Large class rows were split from the source method inventory using the committed Windows cost roster; those estimates are provisional and actual rows still require the ten-minute ceiling. Static source grep/absence from a prior evidence roster is not runtime coverage.

| Row | Project | Exact filter | Reason |
|---|---|---|---|
| B028 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ChannelOutboundUnifiedTransportTests/C519_Queue_to_busy_adapter` | broker readiness/resource-reaper startup failed in the baseline main variant and its once-alone retry; product assertions not reached; prerequisite repair required |
| B029 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ChannelOutboundUnifiedTransportTests/C519_Partial_prompt_requires_complete_receipt` | broker readiness/resource-reaper startup failed in the baseline main variant and its once-alone retry; product assertions not reached; prerequisite repair required |
| B030 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ChannelOutboundUnifiedTransportTests/C519_Enabled_loss_notice_reaches_adapter` | broker readiness/resource-reaper startup failed in the baseline main variant and its once-alone retry; product assertions not reached; prerequisite repair required |
| B031 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ChannelOutboundUnifiedTransportTests/C519_Size_refusal` | broker readiness/resource-reaper startup failed in the baseline main variant and its once-alone retry; product assertions not reached; prerequisite repair required |
| B032 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ChannelOutboundUnifiedTransportTests/C519_Converter_handoff` | broker readiness/resource-reaper startup failed in the baseline main variant and its once-alone retry; product assertions not reached; prerequisite repair required |
| B075 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(AgentTaskLandReceiptTests*)\|(AgentTaskLandReceiptWatermarkSafetyTests*)/*` | Time box; no result claimed |
| B099 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(C0824DesktopLegacyReleaseSwitchTests*)\|(C0824RefOnlyCleanupTests*)\|(C0824RefOnlyDebtTests*)\|(C0824RefOnlyLandingTests*)\|(C0824RefOnlyReadTests*)\|(C0824RefOnlyReceiptTests*)/*` | Time box; no result claimed |
| B112 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(CheckCompactionAttentionTests*)\|(CheckCompactionAutomaticRestartTests*)\|(CheckCompactionCrashTests*)\|(CheckCompactionReceiptTests*)\|(CheckNoteDeliveryHandoffTests*)/*` | Time box; no result claimed |
| B118 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(CodexDelegateDispatchTests*)\|(CodexStartupDeliveryTests*)/*` | Time box; no result claimed |
| B140 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(ExpectationDirectDeliveryTests*)\|(ExpectationEscalationTests*)\|(ExpectationNoteDebtTests*)\|(ExpectationObservationAdapterTests*)\|(ExpectationResponseTests*)\|(ExpectationWatchdogScenarioTests*)/*` | Time box; no result claimed |
| B150 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(LandEvidenceWarningDeliveryTests*)\|(LandReceiptScanCacheTests*)\|(LandRequestWriteDiagnosticTests*)/*` | Time box; no result claimed |
| B161 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(OutputDistillationApplyRaceTests*)\|(OutputDistillationCleanupTests*)\|(OutputDistillationDeliveryTests*)\|(OutputDistillationDispatchTests*)\|(OutputDistillationProducerTests*)/*` | Time box; no result claimed |
| B163 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(PendingNoteWithholdTests*)\|(PendingSettlementDeliveryTests*)/*` | Time box; no result claimed |
| B175 | Antiphon.Tests | `/*/Antiphon.Tests.Application/PreDispatchFailureDeliveryTests/*` | Time box; no result claimed |
| B183 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(ReceiptFailureDeliveryTests*)\|(ReceiptScanDestinationIntegrationTests*)\|(ReceiptScanDestinationTests*)/*` | Time box; no result claimed |
| B193 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(RunnerAlarmAttentionTests*)\|(RunnerAlarmDeliveryTests*)\|(RunnerAlarmNotifierTests*)\|(RunnerBranchContractDocumentationTests*)\|(RunnerClaudeFinalLaunchArgvTests*)\|(RunnerCodexCliEvidenceTests*)/*` | Time box; no result claimed |
| B203 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(SessionGenerationDeliveryOverlapTests*)\|(SessionHealthTests*)\|(SessionReconciliationServiceTests*)\|(SessionRunnerAbsenceEvidenceDocumentationTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | Time box; no result claimed |
| B208 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(SpecialistAttemptEvidenceTests*)\|(SpecialistExecutionIdentityTests*)\|(SpecialistHealthAttentionTests*)\|(SpecialistInputTransportTests*)\|(SpecialistPublicationTests*)\|(SpecialistQualificationTests*)/*` | Time box; no result claimed |
| B213 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(StandingRestartEscalationTests*)\|(StandingResumeReceiptReadinessTests*)\|(StandingSessionQueueSwitchTests*)\|(StandingSessionSelectionTests*)\|(StandingSessionSwitchConcurrencyTests*)\|(StandingSpecialistSeatTests*)/*` | Time box; no result claimed |
| B033 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1153_Real_client_certificate_holds_original_input*)\|(C1153_Real_client_bad_evidence_keeps_failure*)\|(C1153_Final_certificate_is_revalidated_under_lock*)\|(C1153_Certificate_age_is_monotonic*)\|(C1153_Working_or_unknown_inventory_withholds*)\|(C1153_Only_new_cold_dispatch_prepares_evidence*)` | Time box; no result claimed |
| B034 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1153_Nonpristine_brief_never_closes_identity*)\|(C1153_Certificate_failure_does_not_leak_a_hold_on_later_save*)\|(C1153_Due_hold_statement_ceiling*)\|(C1149_Absent_launch_is_blocked_with_original_input*)\|(C1591_Withheld_brief_still_holds_absent_launch*)\|(C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero*)` | Time box; no result claimed |
| B035 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1149_Listed_or_unknown_runner_is_never_absence*)\|(C1149_Changed_or_working_attempt_is_untouched*)\|(C1149_Different_reason_or_attempted_brief_still_uses_failure_policy*)\|(C1149_Native_attempt_keeps_the_failure_path*)\|(C1149_Failed_hold_does_not_persist_on_a_later_save*)\|(C1149_Caller_note_has_one_complete_user_prompt*)` | Time box; no result claimed |
| B036 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1149_Unknown_native_evidence_is_not_unattempted*)\|(C1149_Whitelist_gap_keeps_previous_failure*)\|(C1151_Brief_and_spill_remain_byte_identical*)\|(C1151_Ceiling_failure_note_has_one_complete_user_prompt*)\|(C1213_Recovered_brief_keeps_branch_protections_without_git_common*)\|(C1150_Working_probe_failure_keeps_the_committed_brief*)` | Time box; no result claimed |
| B037 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1150_Outer_envelope_and_receipt_floor_through_the_queue*)\|(C1150_Concurrent_producers_ensure_one_brief*)\|(C1150_Existing_brief_and_spill_are_byte_identical*)\|(C1150_Uncertain_evidence_cannot_create_a_replacement*)\|(C1150_Old_attempt_evidence_does_not_suppress_current_brief*)\|(C1150_Inline_brief_mentioning_spill_paths_is_not_held*)` | Time box; no result claimed |
| B038 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1150_Real_producers_race_to_one_brief_and_keep_the_followup*)\|(C1150_Late_ensure_after_remote_receipt_and_payload_release*)\|(C1150_Local_spill_must_be_present_and_intact*)\|(C1150_Spill_pointer_forms_fail_closed*)\|(C1150_Brief_decision_table_flips_one_condition*)\|(C1161_Failed_caller_note_has_one_complete_user_prompt*)` | Time box; no result claimed |
| B039 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1182_Gone_parent_stamps_the_lost_note_once*)\|(C1186_Recovered_failure_preserves_obligation_guidance*)\|(C1161_Dead_session_grace_is_monotonic*)\|(C1161_Owner_names_the_monotonic_grace*)\|(C1150_Pre_input_refusal_keeps_the_resume_failure_path*)\|(C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command*)` | Time box; no result claimed |
| B040 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal*)\|(C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient*)\|(C1150_Resumed_launch_post_input_bookkeeping_flips_one_step*)\|(C1150_Resume_input_rule_classifies_every_delivery_result*)\|(C1150_Running_cut_is_discovered_and_delivered_once*)\|(C1150_Insert_before_flush_cut_reuses_pending_row*)` | Time box; no result claimed |
| B041 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1150_Followup_survives_both_queue_orders*)\|(C1150_Discovery_gates_withhold_recovery*)\|(C1150_Readiness_or_persistence_fault_preserves_custody*)\|(C1150_Watchdog_first_recovers_before_failure*)\|(C1149_Watchdog_first_holds_absent_launch*)\|(C1150_Recovery_exhaustion_is_visible_without_replacement*)` | Time box; no result claimed |
| B042 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence*)\|(C1149_C1150_Working_full_tick_safety*)` | Time box; no result claimed |
| B055 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(AgentAttachHerdrTests*)\|(AgentChannelServiceIntegrationTests*)\|(AgentControlServiceIntegrationTests*)\|(AgentReportStoreTests*)\|(AgentStartRecoveryTests*)\|(AgentSupervisionTests*)/*` | Time box; no result claimed |
| B058 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(AgentTaskCheckInterpreterTests*)\|(AgentTaskCheckScheduleTests*)\|(AgentTaskCheckSweepTests*)/*` | Time box; no result claimed |
| B073 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(AgentTaskLandNotificationParkFollowUpTests*)\|(AgentTaskLandNotificationRecoveryTests*)\|(AgentTaskLandNotificationRescanTests*)/*` | Time box; no result claimed |
| B105 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)\|(CapacityObsoleteQueueTests*)\|(CapacityRecoveryAttentionTests*)\|(CapacityRecoverySupervisionTests*)\|(CapacityRecoveryTaskTests*)/*` | Time box; no result claimed |
| B109 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(ChannelBatchingTests*)\|(ChannelBridgeTests*)\|(ChannelInboundRecoveryTests*)\|(ChannelIngressIncidentTests*)\|(ChannelInputWithheldIncidentTests*)\|(ChannelOutboundCaptureTests*)/*` | Time box; no result claimed |
| B119 | Antiphon.Tests | `/*/Antiphon.Tests.Application/CompletionNoteDurableSweepTests/*` | Time box; no result claimed |
| B127 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(DelegationBriefRecoveryTests*)\|(DelegationScopeHoldTests*)/*` | Time box; no result claimed |
| B134 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(DispatchBaseNotificationTests*)\|(DispatchConcurrencyDispatchTests*)\|(DispatchHeldAttentionTests*)/*` | Time box; no result claimed |
| B136 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/*` | Time box; no result claimed |
| B184 | Antiphon.Tests | `/*/Antiphon.Tests.Application/RefocusBriefIdentityTests/*` | Time box; no result claimed |
| B194 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(RunnerGrokCredentialProbeDispatcherTests*)\|(RunnerReadSourcePinTests*)\|(RunnerSeatOrphanSweepTests*)/*` | Time box; no result claimed |
| B204 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(SessionRuntimeActionDeferralTests*)\|(SessionRuntimeActionRecoveryTests*)\|(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionTemporaryBlockTests*)\|(SessionRuntimeActionTests*)\|(SessionStateCacheReadTests*)/*` | Time box; no result claimed |
| B061 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskDeliveryWatchdogTests/*` | Time box; no result claimed |
| B074 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandQueuedReceiptTests/*` | Time box; no result claimed |
| B092 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/*` | Time box; no result claimed |
| B098 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(C0747LandReceiptReplayTests*)\|(C0747QueueRowExpiryTests*)/*` | Time box; no result claimed |
| B110 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(ChannelOutboundDeadlineTests*)\|(ChannelOutboundDeliveryTests*)\|(ChannelOutboundFailureRecordingTests*)\|(ChannelOutboundMaterializationTests*)\|(ChannelOutboundMetadataRepairTests*)\|(ChannelOutboundRecoveryTests*)/*` | Time box; no result claimed |
| B114 | Antiphon.Tests | `/*/Antiphon.Tests.Checkpoints/(CheckpointTempScopeTests*)\|(CheckpointTerminalReceiptTests*)\|(CheckpointToolCopyCleanupTests*)/*` | Time box; no result claimed |
| B115 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(ClaudeCredentialProbeDispatcherTests*)\|(ClaudeDialogDeliveryTests*)\|(ClaudeEffortByRoleDispatchTests*)/*` | Time box; no result claimed |
| B125 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DelegateBranchDeliveryTests/*` | Time box; no result claimed |
| B131 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DialogDeliveryAdmissionTests/*` | Time box; no result claimed |
| B138 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/DockerDeliveryDatabaseTests/*` | Time box; no result claimed |
| B139 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DurableRunnerSpillReceiptTests/*` | Time box; no result claimed |
| B156 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(MatrixChildReceiptTests*)\|(MatrixDefaultComplexityCreateTests*)/*` | Time box; no result claimed |
| B165 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(PhoneHomeBackpressureTests*)\|(PhoneHomeCompletedCardTransportTests*)\|(PhoneHomeDeferredKillTests*)\|(PhoneHomeImmediateSendTests*)\|(PhoneHomeLaunchTransportTests*)\|(PhoneHomeMirrorJoinTests*)/*` | Time box; no result claimed |
| B166 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(PhoneHomeOutageMentionTests*)\|(PhoneHomePendingInventoryTests*)\|(PhoneHomeQueuedTurnTests*)\|(PhoneHomeRollingRunnerTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeSpillTransportTests*)/*` | Time box; no result claimed |
| B182 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ReadyGateFailureNoticeDeliveryTests/*` | Time box; no result claimed |
| B217 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(TaskProgressPolicyTests*)\|(TaskProgressStallSweepTests*)\|(TaskSupervisionReceiptTests*)\|(TaskSupervisionVisibilityTests*)/*` | Time box; no result claimed |
| B221 | Antiphon.Tests | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests/*` | Time box; no result claimed |
| B224 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(VerificationRoundDeliveryTests*)\|(VerificationRoundSettlementTests*)/*` | Time box; no result claimed |
| B227 | Antiphon.Tests | `/*/Antiphon.Tests.TestHelpers/WindowsDeliveryFixtureWitnessTests/*` | Time box; no result claimed |
| B001 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandActiveSourceClaimTests/C448_V29_ActiveSourceClaimHoldsEveryMode` | Time box; no result claimed |
| B002 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/(C467_V06_AtomicSettlementFaultMatrix*)\|(C448_F07_PublicationSettlementIsAtomic*)` | Time box; no result claimed |
| B003 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/(C448_F03_TargetIntentAcknowledgement*)\|(C448_F04_PushIntentAcknowledgement*)` | Time box; no result claimed |
| B004 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/(C448_F05_PublicationReceiptAcknowledgement*)\|(C448_F06_CleanupIntentAcknowledgement*)` | Time box; no result claimed |
| B005 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/(C448_F01_SourcePinResultMustBeAcknowledgedBeforeTheNextPin*)\|(C448_F04_PushResultAcknowledgementCannotAuthorizeCleanup*)` | Time box; no result claimed |
| B006 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/(C448_V23_DeliveryFailureKeepsTheCommittedPublicationAndEvent*)\|(C448_V23_NotificationFailureCannotReplaceConfirmedPublication*)` | Time box; no result claimed |
| B007 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/(C448_F07_RefusalEvidenceAndTerminalEventCommitTogether*)\|(C448_V16_AcknowledgedCheckpointsGateDependentMutations*)` | Time box; no result claimed |
| B008 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt*)\|(C494_LostPushAckReconcilesRealTargetBeforeSourceChecks*)` | Time box; no result claimed |
| B009 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C448_V09_PushAndConfirmationPreserveCompetingRemoteState*)\|(C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection*)` | Time box; no result claimed |
| B010 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent*)\|(C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment*)` | Time box; no result claimed |
| B011 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained*)\|(C448_V33_CleanupRetriesDoNotEmitAnotherPublication*)` | Time box; no result claimed |
| B012 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C448_V22_ConfirmedPublicationUsesSavedReceipt*)\|(C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut*)` | Time box; no result claimed |
| B013 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C448_V04_IgnoredFilesSurviveConfirmedPublication*)\|(C1512_ImageEvidenceIsRetainedBeforeCleanup*)` | Time box; no result claimed |
| B014 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C1512_UnavailableImageRetentionPreservesSource*)\|(C665_DisposableOnlyIgnoredContentIsRemovedOnLand*)` | Time box; no result claimed |
| B015 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C665_ProtectedIgnoredRefusalNamesPaths*)\|(C448_V09_PushExitCannotReplaceRemoteConfirmation*)` | Time box; no result claimed |
| B016 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C688_LandNeverRunsGitInTheSourceWorktree*)\|(C688_RebaseAndVerifyRunInTheLandWorktree*)` | Time box; no result claimed |
| B017 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C688_PushPrecedesCanonicalAdvance*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)` | Time box; no result claimed |
| B018 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance*)` | Time box; no result claimed |
| B019 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)\|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)` | Time box; no result claimed |
| B020 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C688_LocalMasterAheadOfOriginRefuses*)\|(C642_ProtocolInspectionsAreIdentityAndStatus*)` | Time box; no result claimed |
| B021 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskWorktreeLockOutcomeTests/(C443_BusyRecipientGetsOutcomeWhenIdle*)\|(C443_EligibleRecipientGetsOutcome*)` | Time box; no result claimed |
| B022 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskWorktreeLockOutcomeTests/(C443_BusyRecipientGetsUnlandedSiblingWarning*)\|(C443_EligibleRecipientGetsUnlandedSiblingWarning*)` | Time box; no result claimed |
| B023 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskWorktreeLockOutcomeTests/(C443_D9_RecipientGetsSummarizedSiblingsAndDiagnostics*)\|(HarnessBuiltLandKeepsLongSiblingInsideProductionInspectionDeadline*)` | Time box; no result claimed |
| B024 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/(C1143_UnloadableHeldEpisodeRestampsAndSkipsNextSweep*)\|(C1143_UnloadablePreparationEntryPointsRestamp*)\|(C1143_RestampCasIsDueBoundedAndIdempotent*)\|(C1143_BackwardClockOverlapKeepsTheCapturedDeadline*)\|(C1143_UnloadableSweepStatementBudget*)\|(C1143_LoadableEpisodeAndOtherRefusalsKeepTheirBehaviour*)\|(C1143_MissingAndReplacedEpisodesStayFailClosed*)\|(C1143_DisabledBusyAndUnknownKeepExistingBehaviour*)` | Time box; no result claimed |
| B025 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/(C1143_WorkingSessionAndWarningsAreUntouched*)\|(C1143_ReadOnlyProofReadersNeverRestamp*)\|(C1143_RestampFailureLeavesNoTrackedWrite*)\|(C1065_LegacySweepRequiresFreshPublicationAndIdleWindow*)\|(C1108_LegacyWindowIsServerAnchored*)\|(C1108_ScheduledSweepIsGatedAndBoundedPerRun*)\|(C1108_ReconcileJobCountsOnlyReleases*)\|(C1108_DispatcherHookRunsTheGatedSweep*)` | Time box; no result claimed |
| B026 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/(C1108_HeldEpisodesBackOffUntilNextAttempt*)\|(C1135_HeldRefusalsRestampAndRecordTheirReason*)\|(C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce*)\|(C1145_ReleasedCountsConfirmationsThisRunProduces*)\|(C1147_RolledBackConfirmationIsNotThisRunsRelease*)\|(C1108_AdvanceVerifiesSourceOnceWithoutBoundary*)\|(C1065_ClaimAndReplyInvalidateLegacyCandidate*)` | Time box; no result claimed |
| B048 | Antiphon.Tests | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/(Unpooled_remote_shared_release_never_uses_the_legacy_stopper*)\|(Remote_shared_release_keeps_intentional_warm_pooling*)\|(Working_session_keeps_ownership_and_visible_debt*)\|(Janitor_cannot_bypass_a_release_hold*)\|(Cancellation_reconciles_without_second_stop*)\|(Blocked_report_with_running_runner_requires_a_published_park*)\|(Failed_settlement_releases_without_success_branch*)\|(Settlement_delivery_precedes_release_and_survives_release_fault*)` | Time box; no result claimed |
| B049 | Antiphon.Tests | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/(Parent_receipt_rejects_ack_stale_or_partial_prompt*)\|(Attention_contains_release_identity_and_reason*)\|(Long_answer_keeps_complete_content_and_spill_receipt*)\|(Accepted_answer_after_release_is_delivered_once*)\|(Answer_receipt_rejects_ack_stale_or_partial_prompt*)\|(Legacy_ambiguous_answer_requires_fresh_absence_without_another_release*)\|(Answer_racing_release_preserves_one_owner*)\|(BeforeDispatch_budget_follows_the_test_clock*)` | Time box; no result claimed |
| B050 | Antiphon.Tests | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/(BeforeDispatch_late_hook_passes_at_sixty_seconds_and_fails_past_the_cap*)\|(Answer_recovery_preserves_round_and_admission_guards*)\|(Answer_stop_bypass_requires_the_exact_release_receipt*)\|(Answer_admission_guards_preserve_the_accepted_reply*)\|(Answer_fields_do_not_leak_into_a_later_attempt*)\|(Answer_keeps_workspace_and_report_context*)\|(Reply_on_live_local_or_warm_session_is_unchanged*)\|(Completed_attempt_registers_release_debt*)` | Time box; no result claimed |
| B051 | Antiphon.Tests | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/(Incomplete_settlements_never_authorize_release*)\|(Unsettled_blocked_or_queued_owner_is_preserved*)\|(Standing_warm_and_verification_owners_are_preserved*)\|(Settlement_age_has_its_own_safety_margin*)\|(Pending_delivery_prevents_release*)\|(Concurrent_reservations_have_one_winner*)\|(Unsupported_server_transport_never_falls_back_to_force*)` | Time box; no result claimed |
| B053 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AbandonedLandingReconcilerTests/*` | Time box; no result claimed |
| B054 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AbsentLaunchPolicyTests/*` | Time box; no result claimed |
| B056 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentSystemPromptLaunchTests/*` | Time box; no result claimed |
| B057 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskAnswerTests/*` | Time box; no result claimed |
| B059 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskCommitEndpointTests/*` | Time box; no result claimed |
| B060 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskDeadSessionReconciliationTests/*` | Time box; no result claimed |
| B062 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | Time box; no result claimed |
| B063 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskDispatchFailureTests/*` | Time box; no result claimed |
| B064 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskDispatcherWiringTests/*` | Time box; no result claimed |
| B065 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskInputFallbackTests/*` | Time box; no result claimed |
| B066 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskInternalDecisionLifecycleTests/*` | Time box; no result claimed |
| B067 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandAdmissionControlledTests/*` | Time box; no result claimed |
| B068 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandAdmissionTests/*` | Time box; no result claimed |
| B069 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandEnqueueTests/*` | Time box; no result claimed |
| B070 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandFailureDiagnosticTests/*` | Time box; no result claimed |
| B071 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandHoldVisibilityTests/*` | Time box; no result claimed |
| B072 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandMonitoringTests/*` | Time box; no result claimed |
| B076 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskOverdueDeadlineTests/*` | Time box; no result claimed |
| B077 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskPoolTests/*` | Time box; no result claimed |
| B078 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskRefineTests/*` | Time box; no result claimed |
| B079 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/*` | Time box; no result claimed |
| B080 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskReplyOverlayTests/*` | Time box; no result claimed |
| B081 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskReuseEnqueueTests/*` | Time box; no result claimed |
| B082 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/*` | Time box; no result claimed |
| B083 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskSettlementRaceTests/*` | Time box; no result claimed |
| B084 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskStallEscalationTests/*` | Time box; no result claimed |
| B085 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskStandingAgentDispatchTests/*` | Time box; no result claimed |
| B086 | Antiphon.Tests | `/*/Antiphon.Tests.AgentTui/AgentTuiSecretProtectorTests/*` | Time box; no result claimed |
| B087 | Antiphon.Tests | `/*/Antiphon.Tests.Checkpoints/AnchoredPathDeleteWitnessTests/*` | Time box; no result claimed |
| B088 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(AttentionApiTests*)\|(AttentionFeedNoiseTests*)/*` | Time box; no result claimed |
| B089 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AttentionServiceTests/*` | Time box; no result claimed |
| B090 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(AwayDigestNotifierTests*)\|(AwayDigestProjectionTests*)/*` | Time box; no result claimed |
| B091 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(BlockedReplyReportRetentionTests*)\|(BlockedSeatEscalationNotifierTests*)\|(BlockedTaskNotifierTests*)/*` | Time box; no result claimed |
| B093 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BlockedTaskParkProjectionTests/*` | Time box; no result claimed |
| B094 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BlockedTaskParkReleaseTests/*` | Time box; no result claimed |
| B095 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BlockedTaskSyncRecoveryTests/*` | Time box; no result claimed |
| B096 | Antiphon.Tests | `/*/Antiphon.Tests.Application/BoardServiceIntegrationTests/*` | Time box; no result claimed |
| B097 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(BootLivenessProbeScopeTests*)\|(BootStallAttentionTests*)\|(BootStallDetectionTests*)\|(BootStallDocumentationTests*)\|(BootWedgeRelaunchParityTests*)/*` | Time box; no result claimed |
| B100 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(C0824RefOnlySyncTests*)\|(C0824WorkspaceSlotServerTests*)/*` | Time box; no result claimed |
| B101 | Antiphon.Tests | `/*/Antiphon.Tests.Checkpoints/C1307CliCancellationTests/*` | Time box; no result claimed |
| B102 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/C1307ToolRemovalTests/*` | Time box; no result claimed |
| B103 | Antiphon.Tests | `/*/Antiphon.Tests.Application/C1514HostLoadHistoryTests/*` | Time box; no result claimed |
| B104 | Antiphon.Tests | `/*/Antiphon.Tests.Application/C1677DiskAlarmTests/*` | Time box; no result claimed |
| B106 | Antiphon.Tests | `/*/Antiphon.Tests.Application/Card1619ExitConsumerTests/*` | Time box; no result claimed |
| B107 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(CardClosedAttentionTests*)\|(CardCorrectionIntegrationTests*)\|(CardFilePrivacyBoundaryAcceptanceTests*)\|(CardFilePrivacyConcurrencyTests*)\|(CardFilePrivacyRecoveryTests*)\|(CardPrivateNotesBoundaryTests*)/*` | Time box; no result claimed |
| B108 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(CardReviewServiceIntegrationTests*)\|(CardSpawnModelArgumentTests*)\|(CardVerificationPolicyTests*)\|(CardWorkTransitionServiceTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | Time box; no result claimed |
| B113 | Antiphon.Tests | `/*/Antiphon.Tests.Checkpoints/(CheckpointExecutorLogTests*)\|(CheckpointLaunchCleanupTests*)\|(CheckpointSourceExecutionTests*)\|(CheckpointSourceStateTests*)\|(CheckpointStatePublicationTests*)\|(CheckpointTaskOwnershipTests*)/*` | Time box; no result claimed |
| B116 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ClosingReportNudgeLifecycleTests/*` | Time box; no result claimed |
| B117 | Antiphon.Tests | `/*/Antiphon.Tests.Application/CodexCliObservationTests/*` | Time box; no result claimed |
| B120 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(ComplexityAttentionTests*)\|(ComplexityDispatcherTests*)/*` | Time box; no result claimed |
| B121 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ContextCompactionSweepTests/*` | Time box; no result claimed |
| B122 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DataRetentionServiceTests/*` | Time box; no result claimed |
| B123 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DecisionCardNotifierTests/*` | Time box; no result claimed |
| B124 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(DefaultRunnerPinTests*)\|(DefaultRunnerRerouteTests*)/*` | Time box; no result claimed |
| B126 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(DelegateBundleLaunchTests*)\|(DelegateCheckProbeTests*)/*` | Time box; no result claimed |
| B128 | Antiphon.Tests | `/*/Antiphon.Tests.Scripts/DerivedImpactIndexTests/*` | Time box; no result claimed |
| B129 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DesktopLaunchMemoryAdmissionTests/*` | Time box; no result claimed |
| B130 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DiagnosticsBundleServiceTests/*` | Time box; no result claimed |
| B132 | Antiphon.Tests | `/*/Antiphon.Tests.TestHelpers/DirectoryLinkFixtureWindowsTests/*` | Time box; no result claimed |
| B133 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DispatchBaseInspectionDiagnosticsTests/*` | Time box; no result claimed |
| B135 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DispatchHoldVisibilityTests/*` | Time box; no result claimed |
| B137 | Antiphon.Tests | `/*/Antiphon.Tests.Application/DispatcherSweepLifetimeTests/*` | Time box; no result claimed |
| B141 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/ExpectationWatchdogJobTests/*` | Time box; no result claimed |
| B142 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/FileUseCaptureTests/*` | Time box; no result claimed |
| B143 | Antiphon.Tests | `/*/Antiphon.Tests.Application/GhostSeatReleaseTests/*` | Time box; no result claimed |
| B144 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(GrokCredentialProbeDispatcherTests*)\|(GrokDelegateDispatchTests*)\|(GrokNativeSessionResumeTests*)\|(GrokStartupReadyOrderingTests*)/*` | Time box; no result claimed |
| B145 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(HerdrAlwaysOnChannelParityTests*)\|(HerdrPaneDisposalApplicationTests*)\|(HerdrStatusDisagreementTests*)\|(HerdrSupervisionAttentionTests*)/*` | Time box; no result claimed |
| B146 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/(HostCleanupCheckpointAuthorityTests*)\|(HostCleanupCheckpointCompatibilityTests*)\|(HostCleanupCheckpointCustodyTests*)/*` | Time box; no result claimed |
| B147 | Antiphon.Tests | `/*/Antiphon.Tests.Application/HostCleanupWorktreeInventoryTests/*` | Time box; no result claimed |
| B148 | Antiphon.Tests | `/*/Antiphon.Tests.Application/InterimVerificationPolicyTests/*` | Time box; no result claimed |
| B149 | Antiphon.Tests | `/*/Antiphon.Tests.Api/KeepSixReminderTests/*` | Time box; no result claimed |
| B151 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/(LandingGitTests*)\|(LandingRemovalPolicyControlTests*)/*` | Time box; no result claimed |
| B152 | Antiphon.Tests | `/*/Antiphon.Tests.ApiKeys/LaunchEnvLayersIntegrationTests/*` | Time box; no result claimed |
| B153 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/LinuxOwnedGitProcessTests/*` | Time box; no result claimed |
| B154 | Antiphon.Tests | `/*/Antiphon.Tests.TestHelpers/LiveFixtureTextTests/*` | Time box; no result claimed |
| B155 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/LogRetentionTests/*` | Time box; no result claimed |
| B157 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(MidTurnInterruptDocumentationTests*)\|(MidTurnInterruptPolicyTests*)/*` | Time box; no result claimed |
| B158 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ModelAvailabilityDispatcherTests/*` | Time box; no result claimed |
| B159 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(OrchestratorInstructionsRefreshTests*)\|(OrchestratorServiceIntegrationTests*)/*` | Time box; no result claimed |
| B160 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(OrchestratorStateProjectionTests*)\|(OrchestratorTrackerCadenceTests*)/*` | Time box; no result claimed |
| B162 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ParkedMessageSweepServiceTests/*` | Time box; no result claimed |
| B164 | Antiphon.Tests | `/*/Antiphon.Tests.Application/PermissionWaitTests/*` | Time box; no result claimed |
| B167 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(PhoneHomeStandingLaunchTests*)\|(PhoneHomeStrandedQueueTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` | Time box; no result claimed |
| B168 | Antiphon.Tests | `/*/Antiphon.Tests.Agents/(PhoneHomeConnectionTests*)\|(PhoneHomeEpochAgreementTests*)/*` | Time box; no result claimed |
| B169 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(PinnedAgentKindTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(PinnedProfileLaunchSpecTests*)/*` | Time box; no result claimed |
| B170 | Antiphon.Tests | `/*/Antiphon.Tests.Application/PipelineSpeedIntegrationTests/*` | Time box; no result claimed |
| B171 | Antiphon.Tests | `/*/Antiphon.Tests.Application/PolicyRefreshServiceTests/*` | Time box; no result claimed |
| B172 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(PoolClaudeEffortTests*)\|(PoolProviderProjectionStampTests*)/*` | Time box; no result claimed |
| B173 | Antiphon.Tests | `/*/Antiphon.Tests.Application/PostLandMutationWorkflowTests/*` | Time box; no result claimed |
| B174 | Antiphon.Tests | `/*/Antiphon.Tests.Application/PostLandMutationWorktreeTests/*` | Time box; no result claimed |
| B176 | Antiphon.Tests | `/*/Antiphon.Tests/ProcessSpawnLimitTests/*` | Time box; no result claimed |
| B177 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ProviderQuotaRefusalAcceptanceTests/*` | Time box; no result claimed |
| B178 | Antiphon.Tests | `/*/Antiphon.Tests.Application/QueuedInputWatchdogTests/*` | Time box; no result claimed |
| B179 | Antiphon.Tests | `/*/Antiphon.Tests.Application/QuotaEvidenceWireTests/*` | Time box; no result claimed |
| B180 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/RcBridgeProbePlatformTests/*` | Time box; no result claimed |
| B181 | Antiphon.Tests | `/*/Antiphon.Tests.Scripts/RcQualificationWatcherScriptTests/*` | Time box; no result claimed |
| B185 | Antiphon.Tests | `/*/Antiphon.Tests.Scripts/ReleaseImpactEnvelopeTests/*` | Time box; no result claimed |
| B188 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/(RepositoryMutationLeaseOwnerTests*)\|(RepositoryMutationLeaseTests*)/*` | Time box; no result claimed |
| B189 | Antiphon.Tests | `/*/Antiphon.Tests.Application/RestartFailureClassificationTests/*` | Time box; no result claimed |
| B190 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ReviewLoopTests/*` | Time box; no result claimed |
| B191 | Antiphon.Tests | `/*/Antiphon.Tests.Application/RolePolicyToolBundleDispatchTests/*` | Time box; no result claimed |
| B192 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(RoutingExhaustedPlacementTests*)\|(RoutingPinCandidateDispatchTests*)\|(RoutingPinDispatcherTests*)/*` | Time box; no result claimed |
| B195 | Antiphon.Tests | `/*/Antiphon.Tests.Application/RunnerSettlementSyncTests/*` | Time box; no result claimed |
| B196 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(RunnerSlotEndpointTests*)\|(RunnerSyncLeaseFairnessTests*)/*` | Time box; no result claimed |
| B197 | Antiphon.Tests | `/*/Antiphon.Tests.Agents/(RunnerClaudeAdapterEffortPromptTests*)\|(RunnerClaudeAdapterTrustPromptTests*)\|(RunnerClaudeAdapterVerifiedPromptTests*)\|(RunnerGrokAdapterReadyTests*)\|(RunnerStartupStaleObservationTests*)\|(RunnerTerminalSessionGenerationTests*)/*` | Time box; no result claimed |
| B198 | Antiphon.Tests | `/*/Antiphon.Tests.Application/RuntimeActionClaimQueryTests/*` | Time box; no result claimed |
| B199 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(ScheduleCardActionTests*)\|(ScheduleSweepTests*)/*` | Time box; no result claimed |
| B200 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ScopeDriftTests/*` | Time box; no result claimed |
| B201 | Antiphon.Tests | `/*/Antiphon.Tests.Scripts/ScriptHarnessProcessContractTests/*` | Time box; no result claimed |
| B202 | Antiphon.Tests | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/*` | Time box; no result claimed |
| B205 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(SessionStateCommitTests*)\|(SessionStatePinQueryTests*)\|(SessionStateWarmupTests*)\|(SessionTerminationSourcePersistenceTests*)/*` | Time box; no result claimed |
| B206 | Antiphon.Tests | `/*/Antiphon.Tests.Agents/SessionRunnerGenerationWireTests/*` | Time box; no result claimed |
| B207 | Antiphon.Tests | `/*/Antiphon.Tests.Application/SettlementSyncRecoveryTests/*` | Time box; no result claimed |
| B209 | Antiphon.Tests | `/*/Antiphon.Tests.Application/SpecialistStartIntentTests/*` | Time box; no result claimed |
| B210 | Antiphon.Tests | `/*/Antiphon.Tests.Agents/SpecialistToolPolicyTests/*` | Time box; no result claimed |
| B211 | Antiphon.Tests | `/*/Antiphon.Tests.Application/StaleWorkingTranscriptWatchdogTests/*` | Time box; no result claimed |
| B212 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(StandingBootAttentionTests*)\|(StandingBootDocumentationTests*)\|(StandingBootQueueWitnessTests*)\|(StandingBootStatementBudgetTests*)\|(StandingBootWatchdogTests*)\|(StandingContinuityAttentionTests*)/*` | Time box; no result claimed |
| B214 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/*` | Time box; no result claimed |
| B215 | Antiphon.Tests | `/*/Antiphon.Tests.Application/SubscriptionQuotaGateDispatchTests/*` | Time box; no result claimed |
| B216 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(TaskCeilingControlTests*)\|(TaskInputAttentionCostTests*)\|(TaskInputReadFailureTests*)\|(TaskInputWeakArmBoundaryTests*)\|(TaskLaunchTokenBoundaryTests*)\|(TaskPlatformDispatchTests*)/*` | Time box; no result claimed |
| B218 | Antiphon.Tests | `/*/Antiphon.Tests.Application/TerminalTaskResidueTests/*` | Time box; no result claimed |
| B219 | Antiphon.Tests | `/*/Antiphon.Tests.TestHelpers/TestLaneCategoryGuardTests/*` | Time box; no result claimed |
| B220 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(TranscriptBindingIncidentTests*)\|(TranscriptPromptSpanTests*)\|(TranscriptWorkingStateQueryTests*)/*` | Time box; no result claimed |
| B222 | Antiphon.Tests | `/*/Antiphon.Tests.Application/UnixDelegateLaunchArgvTests/*` | Time box; no result claimed |
| B223 | Antiphon.Tests | `/*/Antiphon.Tests.Checkpoints/UnsafeDeleteRecoveryTests/*` | Time box; no result claimed |
| B225 | Antiphon.Tests | `/*/Antiphon.Tests.Scripts/VolumeRetirementHostProofTests/*` | Time box; no result claimed |
| B226 | Antiphon.Tests | `/*/Antiphon.Tests.Application/WatchdogServiceTests/*` | Time box; no result claimed |
| B228 | Antiphon.Tests | `/*/Antiphon.Tests.Application/WorkingTranscriptRecoveryTests/*` | Time box; no result claimed |
| B229 | Antiphon.Tests | `/*/Antiphon.Tests.Application/(WorktreeBaseSelectionTests*)\|(WorktreeCleanupJournalTests*)\|(WorktreeDefaultCallerTests*)/*` | Time box; no result claimed |
| B230 | Antiphon.Tests | `/*/Antiphon.Tests.Application/WorktreeLandingCleanupRetryTests/*` | Time box; no result claimed |
| B231 | Antiphon.Tests | `/*/Antiphon.Tests.Application/WorktreeRetirementRaceTests/*` | Time box; no result claimed |
| B232 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/(WorktreeLockDiagnosticsTests*)\|(WorktreeNoFollowDeleteTests*)/*` | Time box; no result claimed |
| X011 | Antiphon.Tests | `/*/Antiphon.Tests.Agents/OwnedPtyFixtureCleanupTests*/*` | Time box; no result claimed |
| X012 | Antiphon.Tests | `/*/Antiphon.Tests.Agents/TestOwnedPtyHostIdentityTests*/*` | Time box; no result claimed |
| X013 | Antiphon.Tests | `/*/Antiphon.Tests.Agents/TestOwnedPtyHostPolicyTests*/*` | Time box; no result claimed |
| X014 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandApprovalRecoveryTests*/*` | Time box; no result claimed |
| X015 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandContractEndpointTests*/*` | Time box; no result claimed |
| X016 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandHalfResetTests*/*` | Time box; no result claimed |
| X017 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationPersistenceTests*/*` | Time box; no result claimed |
| X018 | Antiphon.Tests | `/*/Antiphon.Tests.Application/AgentTaskLandRequestTests*/*` | Time box; no result claimed |
| X019 | Antiphon.Tests | `/*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests*/*` | Time box; no result claimed |
| X020 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ChannelOutboundTrailingRecoveryTests*/*` | Time box; no result claimed |
| X021 | Antiphon.Tests | `/*/Antiphon.Tests.Application/HerdrLabelFollowFlowTests*/*` | Time box; no result claimed |
| X022 | Antiphon.Tests | `/*/Antiphon.Tests.Application/HerdrLabelFollowTests*/*` | Time box; no result claimed |
| X023 | Antiphon.Tests | `/*/Antiphon.Tests.Application/LandFailureDiagnosticTests*/*` | Time box; no result claimed |
| X024 | Antiphon.Tests | `/*/Antiphon.Tests.Application/OutputDistillationAdmissionTests*/*` | Time box; no result claimed |
| X025 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ReviewEvidenceRecoveryTests*/*` | Time box; no result claimed |
| X026 | Antiphon.Tests | `/*/Antiphon.Tests.Application/ReviewEvidenceResettlementTests*/*` | Time box; no result claimed |
| X027 | Antiphon.Tests | `/*/Antiphon.Tests.Application/RunnerTaskSettlementTests*/*` | Time box; no result claimed |
| X028 | Antiphon.Tests | `/*/Antiphon.Tests.Application/TaskCompletionProgressPolicyTests*/*` | Time box; no result claimed |
| X029 | Antiphon.Tests | `/*/Antiphon.Tests.Application/VerificationRoundDispatchTests*/*` | Time box; no result claimed |
| X030 | Antiphon.Tests | `/*/Antiphon.Tests.Checkpoints/CheckpointEofStallTests*/*` | Time box; no result claimed |
| X031 | Antiphon.Tests | `/*/Antiphon.Tests.Checkpoints/CheckpointTimingHarnessTests*/*` | Time box; no result claimed |
| X032 | Antiphon.Tests | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointReceiptTests*/*` | Time box; no result claimed |
| X033 | Antiphon.Tests | `/*/Antiphon.Tests.TestHelpers/DirectoryLinkFixtureTests*/*` | Time box; no result claimed |
| X034 | Antiphon.Tests | `/*/Antiphon.Tests.TestHelpers/ReportBudgetWorkspacePortabilityTests*/*` | Time box; no result claimed |

### Static Windows-only census

| Project | Class | Reason |
|---|---|---|
| tests/Antiphon.Tests | AgentSessionServiceIntegrationTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | DelegateLaunchArgvIntegrityTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | GrokDelegateEndToEndTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | GrokSignInRuntimeTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | SessionMessageQueueGrokPtyIntegrationTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | DelegationBriefCeilingPtyTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | SessionMessageQueuePtyIntegrationTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | SessionQueueReceiptPlumbingTests | Committed windowsOnly lane; no Linux proof claimed |
| tests/Antiphon.Tests | ClaudeAdapterLocalShellTests | Committed windowsOnly lane; no Linux proof claimed |

### Actual adjacent family selections outside the original Linux census

| Project | Class | Rows |
|---|---|---|
| tests/Antiphon.Tests | Antiphon.Tests.Application.GrokRulesCompositionTests | F001 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.GrokRulesLiveEvidenceTests | F001 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.GrokRulesTransportCompatibilityTests | F001 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueGrokPtyIntegrationTests | F002 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueuePtyIntegrationTests | F002 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueSpillTests | F002 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.SessionMessageQueueSupervisionTests | F002 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.AgentSessionBackendTests | F003 |
| tests/Antiphon.Tests | Antiphon.Tests.Agents.AgentSessionSettingsTests | F003 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.AgentSessionRuntimeBoundedReadTests | F003 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.AgentSessionRuntimeTests | F003 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.AgentSessionRuntimeActivityTests | F003 |
| tests/Antiphon.Tests | Antiphon.Tests.Application.AgentSessionServiceIntegrationTests | F003 |
| tests/Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1514HostLoadNativeTests | F004b |
| tests/Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1514HostLoadAuthorityTests | F004b |
| tests/Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C1514HostLoadNativeOverheadTests | F004b |
| tests/Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotTimeoutBudgetTests | F006b |
| tests/Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotColdResetTests | F006b |
| tests/Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotNativeRehomeTests | F006b |
| tests/Antiphon.SessionRunner.Tests | Antiphon.SessionRunner.Tests.C0824WorkspaceSlotProcessScanTests | F006b |

## Tracker and handoff

| Card | State when filed | Scope |
|---|---|---|
| CARD-1927 | Backlog | Claude auth empty-config witness inherits OAuth token in its real CLI child |
| CARD-1934 | Backlog | C519 main queue-to-adapter case times out during owned Redpanda and resource-reaper startup |
| CARD-1935 | Backlog | C699 targeted recovery error does not produce both immediate completion stamps |
| CARD-1923 | Backlog | CARD-1892 stale Codex observation-gap test expects Pending after lost Input reply |
| CARD-1929 | Backlog | Linux lost-host custody witness cleanup re-reads an invalidated store binding |
| CARD-1930 | Backlog | Linux missing-tree custody fault cut cannot delete the root-owned cgroup tree |
| CARD-1931 | Backlog | Linux custody natural descendant exit never produces its terminal receipt |
| CARD-1932 | Backlog | Linux populated-tree custody witness assumes every sampled transient PID stays alive |
| CARD-1924 | Backlog | CARD-1892 stale Grok compaction test expects a new body in the late-confirm-only flush |
| CARD-1928 | Backlog | Herdr owned-peer tests fail loading their test assembly into the PowerShell child |
| CARD-1922 | Backlog | CARD-1892 stale pretyping land-refusal witness expects Pending after IOException |
| CARD-1926 | Backlog | CARD-1892 stale slot-lease witness assumes Input still owns the workspace mutation lock |

Searches used scripts/card.ps1 search -Board Antiphon -All and full candidate cards were inspected before filing. Related existing cards inspected include CARD-1056, CARD-0703, CARD-0744, CARD-1252, CARD-0877, CARD-0890, CARD-1614, CARD-0634 and CARD-1565. No task was spawned and no frozen Review branch was edited. Repair Debugs must preserve the new production uncertainty/lock contracts; the unrelated fixture/native/bootstrap issues need their own scoped diagnosis. The next serial sweep must execute the unobserved/partial ledger, not infer its result from the completed families.

Task publication history before this final report: 9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 (scope), 2637e291265a25231ca17362165eff1f8e09b66f (known failures/census), e16346cfaaef1ba0533ddf39acb28485f3b2bb74 (new Grok/lock findings). All were pushed to the assigned branch. Final publication/compatibility results are appended at settlement.



## Caller refinement: additional suspects — completed follow-up

The caller refinement arrived after the original 150-minute sweep and added Code c995680c's remaining suspects. Four additional serial rows ran in the same unchanged source snapshot, without a new build or a source fix. This is a limited follow-up; the original coverage ledger above is historical. C1029 was already confirmed STALE-CONTRACT in K2 (CARD-1923, supplied g58, repair Debug 31c683a8), and was not repeated without a source change. The caller states c995680c used older master 8c06c7e11 before Merge 224; none of these new results relies on that obsolete run.

**Follow-up: 22 executed, 18 passed, 4 failed, 3 skipped.** No pure timeout occurred, so no LOAD retry was needed. Across both phases: 2,499 executed, 2,475 passed, 24 failed, 169 skipped, 17 distinct failing cases. These are repeated-observation totals, not unique-case coverage. Excluding the earlier OAuth-removal diagnostic R003: 2,498 executed, 2,474 passed, 24 failed, 169 skipped.

PtyDeliveryCeilingsTests and UnixDeliveryCeilingTests were outside the original 504-class census; adding them makes 506 required classes. Both were selected fully here. StandingSessionSwitchConcurrencyTests and PhoneHomeOutageMentionTests now have the named method selected, but the rest of each class remains unrun: **123 classes have full selections (including four all-skipped), three are partial, and 380 have no result.** The sweep remains incomplete. The earlier table's NOT RUN entries for those two classes are superseded only for the exact methods below.

| Row | Exact selection | Executed/passed/failed/skipped | Wall seconds | Slot wait/held seconds | /proc/loadavg start | /proc/loadavg end |
|---|---|---|---|---|---|---|
| A001 | `/*/Antiphon.Tests.Application/StandingSessionSwitchConcurrencyTests/Reservation_and_real_queue_flush_serialize_in_both_orders` | 2/1/1/0 | 55.947 | 0/54 | 20.18 20.49 22.07 28/4013 2950 | 16.58 19.76 21.74 7/3767 6629 |
| A002 | `/*/Antiphon.Tests.Application/PtyDeliveryCeilingsTests/*` | 10/8/2/3 | 9.045 | 0/8 | 16.58 19.76 21.74 6/3767 6629 | 15.81 19.55 21.66 6/3802 7001 |
| A003 | `/*/Antiphon.Tests.Application/UnixDeliveryCeilingTests/*` | 9/9/0/0 | 10.156 | 0/9 | 15.81 19.55 21.66 7/3802 7003 | 14.70 19.18 21.52 5/3878 7545 |
| A004 | `/*/Antiphon.Tests.Application/PhoneHomeOutageMentionTests/In_flight_mention_Enter_late_confirms_without_retyping` | 1/0/1/0 | 36.869 | 0/36 | 14.70 19.18 21.52 6/3878 7545 | 12.93 18.18 21.08 8/3830 9817 |

All four rows use tests/Antiphon.Tests and the shared PostgreSQL fixture. Runs completed and were awaited. The inherited native binary source version remains 9fcc87bcb5c6b0af6e6ee3e51e30d75af3a00572 (only this report differs from b559bfba2e9a420495122d27171352fe9b7e1ba4). Follow-up was commissioned in report-only commit 5c1ff13733; subsequent report edits cannot alter that binary provenance. The final fetch still resolved origin/master to 8cec1e74e75819ef20756c228add987ce4ae4170. Selected test files and the directly relevant SessionMessageQueueService, AgentControlService, StandingQueueSwitchPolicy, PtyDeliveryProfile, PtyBackend and PtyDeliveryCeilings sources have no diff between the measured baseline and that fetched tip. This is a source comparison, not a rebuilt qualification of later master.

### Classification and repair ownership

| Exact failing method (Antiphon.Tests.Application) | Classification | Basis and card | Windows applicability |
|---|---|---|---|
| StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(True) | REAL-DEFECT candidate: standing-switch admission | CARD-1937. IOException correctly retains a Sent/null-verdict attempt, but AgentControlService:683-687 queries only Pending and history selection succeeds. The owner at docs/session-runtime-invariants.md:1651-1655 says delivery baseline/timestamp/verdict/settlement evidence refuses switching. The current assertion retains an explicit owner basis; replacing IOException with a definitive refusal alone would conceal the untested Sent path. No loss or duplicate delivery is proved; later target.Started assertion was not reached. Repair Debug must resolve the admission fence and any necessary owner-policy qualification. False variant passes. | Portable; same source golden boundary as PhoneHome below; live numeric chunk unmapped. |
| PtyDeliveryCeilingsTests.A_silent_runner_leaves_this_processes_own_decision_standing | REAL-DEFECT: test portability | CARD-1938. BuildProfile calls actual platform policy. PtyBackendPolicy.Resolve on non-Windows returns UnixPty before interpreting inbox; assertion demands InboxConhost before RequireRedistributable can skip. This is unrelated to CARD-1892. Preserve Linux backend resolution. | First assertion runs on Windows, where inbox is selectable; Linux mismatch does not imply a Windows red. Later modern portion requires redistributable. |
| PtyDeliveryCeilingsTests.An_inbox_server_is_not_raised_by_a_modern_runner | REAL-DEFECT: same test portability family | CARD-1938. Same real Linux resolution, no Windows guard, expected InboxConhost versus observed UnixPty. Not a timeout. | Executes on Windows; Linux-specific backend mismatch does not apply there. |
| PhoneHomeOutageMentionTests.In_flight_mention_Enter_late_confirms_without_retyping | STALE-CONTRACT | CARD-1936. Silent Enter response plus disconnect is uncertain Input and must remain Sent/null-verdict, while line176 asserts Pending. Fixture already inserts a complete UserPrompt and TurnEnd before disconnect. Repair initial status witness, then retain LateConfirmed, one charged attempt, no typing on successor and durable receipt assertions. Those later assertions were not reached in this run. | Portable; source golden boundary b-application-9e8ce8bfc9104381; live numeric chunk unmapped. |

Pty class source golden boundary: b-application-cdbd24da9a10e90f. Unix class boundary: b-application-53a443d2545f686a. These are committed roster references, not a read of the inaccessible Windows run. UnixDeliveryCeilingTests passed 9/9, including the committed Unix receipt and supported-limit witnesses. Pty class had three explicit Windows delivery-profile skips; these do not establish the skipped paths green.

The strict STALE-CONTRACT definition in this brief covers old uncertain-delivery/resend expectations; the unrelated Unix-versus-inbox fixture errors are therefore classified REAL-DEFECT (test portability), even though their platform assumption is outdated. No failure is attributed to load.

### Exact new failure messages

**Reservation_and_real_queue_flush_serialize_in_both_orders(True)** (A001)

```text
ShouldAssertException: Task `selection!`
    should throw
Antiphon.Server.Application.Exceptions.ConflictException
    but did not
at Antiphon.Tests.Application.StandingSessionSwitchConcurrencyTests.Reservation_and_real_queue_flush_serialize_in_both_orders(Boolean deliveryWins) in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/StandingSessionSwitchConcurrencyTests.Delivery.cs:line 74
```

**A_silent_runner_leaves_this_processes_own_decision_standing** (A002)

```text
ShouldAssertException: (await silentInbox.Value.RefreshAsync(CancellationToken.None))
            .Backend
    should be
DeliveryBackend.InboxConhost
    but was
DeliveryBackend.UnixPty
at Antiphon.Tests.Application.PtyDeliveryCeilingsTests.A_silent_runner_leaves_this_processes_own_decision_standing() in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs:line 285
```

**An_inbox_server_is_not_raised_by_a_modern_runner** (A002)

```text
ShouldAssertException: (await profile.Value.RefreshAsync(CancellationToken.None))
            .Backend
    should be
DeliveryBackend.InboxConhost
    but was
DeliveryBackend.UnixPty
at Antiphon.Tests.Application.PtyDeliveryCeilingsTests.An_inbox_server_is_not_raised_by_a_modern_runner() in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/PtyDeliveryCeilingsTests.cs:line 303
```

**In_flight_mention_Enter_late_confirms_without_retyping** (A004)

```text
ShouldAssertException: attempted.Status
    should be
QueuedMessageStatus.Pending
    but was
QueuedMessageStatus.Sent
at Antiphon.Tests.Application.PhoneHomeOutageMentionTests.In_flight_mention_Enter_late_confirms_without_retyping() in /work/worktrees/task-15406ecc/tests/Antiphon.Tests/Application/PhoneHomeOutageMentionTests.cs:line 176
```

### Exact reruns and evidence

These are measurement rows added because the caller named specific failures; they are not unlisted Code checkpoint substitutions. Build once through the host gate if recreating native outputs, retaining UseAppHost=false, then run each row serially. Historical exact commands:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-A001-a1 -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/StandingSessionSwitchConcurrencyTests/Reservation_and_real_queue_flush_serialize_in_both_orders' --report-trx --report-trx-filename A001-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/A001 --output Detailed
```

A001 TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/A001/A001-a1.trx; SHA-256 5c6be09df3e11a81e6ae1b28c0e49244c46a2b47c285f8942033d3bade8964aa.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-A002-a1 -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/PtyDeliveryCeilingsTests/*' --report-trx --report-trx-filename A002-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/A002 --output Detailed
```

A002 TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/A002/A002-a1.trx; SHA-256 c0355f645edb7d799e2b7778ca02bb53bb7c245212137d5c2d41f41edc2a206e.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-A003-a1 -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/UnixDeliveryCeilingTests/*' --report-trx --report-trx-filename A003-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/A003 --output Detailed
```

A003 TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/A003/A003-a1.trx; SHA-256 406355741521ba51662173542da20ddc07e86659124a08f69aa0703caa006173.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-A004-a1 -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/PhoneHomeOutageMentionTests/In_flight_mention_Enter_late_confirms_without_retyping' --report-trx --report-trx-filename A004-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/A004 --output Detailed
```

A004 TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/A004/A004-a1.trx; SHA-256 efee2a87e83c599442dcd29c423f6ad4b4a91cfbcaeafe52afbf55cc364a6805.

Generated receipts, logs, phase records and the follow-up driver remain ignored under /work/worktrees/task-15406ecc/.antiphon/sweep15406. No generated evidence was staged. The follow-up helper copied the earlier gated driver and extended its expired outer deadline for these four authorized rows; each execution still had a 590-second cap, with no timeout/assertion/retry relaxation.

### New tracker records and why missed

All three new records are Backlog: CARD-1936 (PhoneHome stale witness), CARD-1937 (standing-switch Sent admission), CARD-1938 (two ceiling portability witnesses). Searches used card.ps1 search -Board Antiphon -All. Exact PhoneHome and reservation method searches returned zero; standing_resume_delivery_pending returned CARD-0510 Done, whose full restart-escalation text was inspected and differs from this Sent history-selection path. Exact first ceiling failure returned zero; class search returned CARD-1022/0128/0161/0048. The closest CARD-1022 full text was inspected: broad inbox deprecation does not repair these Linux fixture guards. No duplicate card was treated as absent from a capped preview.

Why missed: fixed CARD-1892 checkpoint lists did not select these witnesses. The two concurrency/PhoneHome classes were found by the original dependency/vocabulary census but remained unexecuted at its limit; the caller's refinement added an explicit targeted execution. The ceiling platform errors are outside the uncertainty vocabulary and original census, and were added from the caller's Code failure list. C1029 was already part of the original known-failure measurement.

No source fixes were made and no live host, Windows run, desktop, Docker volume or restart was touched. The additional suspects are now measured; completing the exhaustive remaining sweep still needs a new time box.



## Second 150-minute coverage window — final results

Caller reopened the task at **2026-11-14 15:28:21 UTC**, with a hard stop at **17:58:21 UTC**. This window used a fresh detached scratch checkout at **8cec1e74e75819ef20756c228add987ce4ae4170**, fetched from origin/master at the window start (later than a4975c8e79). No source commits needed carrying forward, so the scratch required neither a merge nor a source cherry-pick. The report branch stayed fast-forward-only. No product or test source was edited. Earlier outcomes and costs above remain historical; no already-observed method was intentionally rerun. Static prior-method overlap audit: zero.

**This window: 2526 executed, 2524 passed, 2 failed, 4 skipped**, across 54 completed gate attempts. Combined observations across both windows and the refinement: 5025 executed, 4999 passed, 26 failed, 173 skipped. TRX totals exclude 22 explicit pass observations preserved from capped C015 stdout. Combined totals include repeated diagnostic observations; they do not mean unique-case coverage or a green suite.

**Final coverage:** 506 required project/classes (the original504 plus two caller-added ceiling classes), 238 with a full selection represented, 3 partial, **265 with no result**. Full selection includes red and skipped observations and is not a clean qualification. The exact remaining ledger below supersedes the earlier phase's pending lists. The caller's continuation named382 unobserved classes; the preceding targeted refinement had reduced that to380 and added two partial classes before this window started.

Build: exit0, 314.209s including admission; load start `20.36 19.91 20.98 8/4158 16870`, end `27.35 27.33 24.03 4/4250 12281`. Native binary `/tmp/task-15406ecc-coverage2/tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll`, source version `1.0.0+8cec1e74e75819ef20756c228add987ce4ae4170`, SHA-256 `01b79c9298b8120f17d95aaff6c594783e85c5a1d8fb03ab4a3a3d708c7870d3`. Every run used its native default output and UseAppHost=false under scripts/build-slot.ps1. Scratch tracked-source cleanliness and final branch checks are reported at settlement. This is Debug measurement, not a validated Review/checkpoint receipt or reviewedSourceClean assertion.

Late controlled setup correction: the original /tmp scratch made ReportWorkspace persist under TEMP, which AgentReportStore correctly refuses. A cross-filesystem worktree move was refused; a fresh detached checkout of the identical8cec1e74 commit was created at /work/worktrees/task-15406ecc-coverage2. The failed Set-Location setup attempt ran no build or test. Its successful native rebuild exited0 in 231.724s; load `27.90 24.58 21.96 8/4157 21420` -> `30.26 26.19 23.18 17/4058 23204`. Binary `/work/worktrees/task-15406ecc-coverage2/tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll`, version `1.0.0+8cec1e74e75819ef20756c228add987ce4ae4170`, SHA-256 `97eed8f69990ff99d8251b5d244c348c53f5357e87b21796129a81bd7528c98e`. Only the exact C544 controlled validation and required once-alone specialist timeout retry ran there; no code, assertion or timeout changed. The original outcomes remain recorded.

### Current-window findings and ownership

| Exact failing test | Classification | Evidence/action | Windows applicability |
|---|---|---|---|
| `Antiphon.Tests.Application.VerificationRoundDeliveryTests.C544_ReportRegeneratedFromSnapshot` | REAL-DEFECT (fixture TEMP-root precondition; corrected measurement setup) | CARD-1940; ReportWorkspace derives its claimed persistent Git root from the checkout; the sweep placed that checkout under /tmp. AgentReportStore correctly refuses TEMP roots, so missing-report regeneration never produced frozen rendering. Identical8cec1e74 source rebuilt outside TEMP passed exact validation R008. The initial setup choice caused this observation; no standard-checkout master regression or CARD-1892 Pending/resend expectation is demonstrated. CARD-1904 IO-budget debt is separate. No source fix or production refusal weakening occurred.; row(s) C132 | Portable test, but normal persistent C:\Antiphon rc checkout does not meet the TEMP precondition. A TEMP checkout on Windows can fail. Source boundary b-application-bf589370b5bd3368; live numeric unmapped. |
| `Antiphon.Tests.Application.WorktreeDefaultCallerTests.SpecialistFactoriesStayOnSeat` | LOAD | no new defect card (once-alone retry passed); C134 timed out in SpecialistTaskRunnerDeadlineTests.UntilAsync waiting five seconds for the specialist task row. Exact method R009 passed alone with the same source and unchanged five-second bound; its explicit TEMP seat directory is independent of checkout placement. No Pending/Sent or resend assertion is involved. No production or test repair applied.; row(s) C134 | Portable test; the same five-second polling bound can fail under Windows host contention. Live numeric chunk unmapped. |


**Antiphon.Tests.Application.VerificationRoundDeliveryTests.C544_ReportRegeneratedFromSnapshot** — C132 a1

```text
ShouldAssertException: TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson)
    should not be null but was

Additional Info:
    regenerated: frozen rendering
at Antiphon.Tests.Application.VerificationRoundDeliveryTests.AssertReceivedOnceAsync(C544DeliveryRig rig, Guid taskId, String row, String[] headerBits, String expectKind, Nullable`1 expectSpill) in /tmp/task-15406ecc-coverage2/tests/Antiphon.Tests/Application/VerificationRoundDeliveryTests.cs:line 1115
```

ReportWorkspace derives its claimed persistent Git root from the checkout; the sweep placed that checkout under /tmp. AgentReportStore correctly refuses TEMP roots, so missing-report regeneration never produced frozen rendering. Identical8cec1e74 source rebuilt outside TEMP passed exact validation R008. The initial setup choice caused this observation; no standard-checkout master regression or CARD-1892 Pending/resend expectation is demonstrated. CARD-1904 IO-budget debt is separate. No source fix or production refusal weakening occurred.


**Antiphon.Tests.Application.WorktreeDefaultCallerTests.SpecialistFactoriesStayOnSeat** — C134 a1

```text
TaskCanceledException: A task was canceled.
at Antiphon.Tests.Application.SpecialistTaskRunnerDeadlineTests.UntilAsync(Func`1 condition) in /tmp/task-15406ecc-coverage2/tests/Antiphon.Tests/Application/SpecialistTaskRunnerDeadlineTests.cs:line 180
```

C134 timed out in SpecialistTaskRunnerDeadlineTests.UntilAsync waiting five seconds for the specialist task row. Exact method R009 passed alone with the same source and unchanged five-second bound; its explicit TEMP seat directory is independent of checkout placement. No Pending/Sent or resend assertion is involved. No production or test repair applied.

### Per-row counts, durations, load and exact selection

| Row | Project | Exit | Executed/passed/failed/skipped | Wall s | Slot wait/held s | /proc/loadavg start | /proc/loadavg end | Exact filter or UID |
|---|---|---|---|---|---|---|---|
| C001 a1 | Antiphon.Tests | 8 | 0/0/0/4 | 259.985 | 210/49 | 22.08 25.88 23.67 6/4355 3643 | 24.28 24.94 23.82 8/4226 20760 | `/*/Antiphon.Tests.Application/(GrokRulesCompactionAcceptanceTests*)\|(GrokRulesDispatchAcceptanceTests*)\|(GrokRulesLiveMappedDispatchTests*)/*` |
| C002 a1 | Antiphon.Tests | 0 | 33/33/0/0 | 106.407 | 45/60 | 21.49 24.31 23.63 5/4332 21779 | 16.80 22.54 23.11 8/4158 9713 | `/*/Antiphon.Tests.Application/AgentControlServiceIntegrationTests/*` |
| C003 a1 | Antiphon.Tests | 0 | 40/40/0/0 | 230.915 | 120/110 | 16.05 22.09 22.95 4/4260 11902 | 13.43 16.96 20.57 4/4081 11361 | `/*/Antiphon.Tests.Application/(PhoneHomeBackpressureTests*)\|(PhoneHomeCompletedCardTransportTests*)\|(PhoneHomeDeferredKillTests*)\|(PhoneHomeImmediateSendTests*)\|(PhoneHomeLaunchTransportTests*)\|(PhoneHomeMirrorJoinTests*)/*` |
| C004 a1 | Antiphon.Tests | 0 | 31/31/0/0 | 84.123 | 60/22 | 13.43 16.96 20.57 4/4081 11361 | 33.02 21.38 21.75 12/4129 15940 | `/*/Antiphon.Tests.Agents/(PhoneHomeConnectionTests*)\|(PhoneHomeEpochAgreementTests*)/*` |
| C005 a1 | Antiphon.Tests | 0 | 7/7/0/0 | 185.757 | 90/93 | 33.02 21.38 21.75 17/4130 15941 | 28.82 25.19 23.19 61/4267 21531 | `/*/Antiphon.Tests.Application/PhoneHomeOutageMentionTests/(C696Red_Mention_before_first_List_is_persisted*)\|(C696Red_Mention_in_reconnect_gap_survives_restart*)\|(Busy_local_and_remote_mentions_wait_for_idle*)\|(Occurrence_replay_is_idempotent_but_identical_new_mentions_are_distinct*)\|(Split_line_and_completed_newline_accept_one_occurrence*)\|(Accepted_mention_keeps_original_target_after_source_ends*)\|(Queued_activity_failure_does_not_duplicate_or_block_recovery*)` |
| C006 a1 | Antiphon.Tests | 0 | 84/84/0/0 | 165.754 | 60/103 | 28.82 25.19 23.19 61/4267 21531 | 15.72 21.41 22.12 7/4054 14849 | `/*/Antiphon.Tests.Application/(PhoneHomePendingInventoryTests*)\|(PhoneHomeQueuedTurnTests*)\|(PhoneHomeRollingRunnerTests*)\|(PhoneHomeSessionRoutingTests*)\|(PhoneHomeSpillTransportTests*)\|(PhoneHomeStandingLaunchTests*)/*` |
| C007 a1 | Antiphon.Tests | 0 | 34/34/0/0 | 176.283 | 90/84 | 15.72 21.41 22.12 3/4055 14853 | 26.39 20.54 21.44 19/4097 27904 | `/*/Antiphon.Tests.Application/(PhoneHomeStrandedQueueTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` |
| C008 a1 | Antiphon.Tests | 0 | 107/107/0/0 | 275.465 | 105/167 | 26.39 20.54 21.44 11/4097 27904 | 16.40 19.63 21.02 9/3972 23657 | `/*/Antiphon.Tests.Application/(StandingBootAttentionTests*)\|(StandingBootDocumentationTests*)\|(StandingBootQueueWitnessTests*)\|(StandingBootStatementBudgetTests*)\|(StandingContinuityAttentionTests*)\|(StandingRestartEscalationTests*)/*` |
| C009 a1 | Antiphon.Tests | 0 | 66/66/0/0 | 142.633 | 60/80 | 16.40 19.63 21.02 16/3978 23657 | 12.81 17.01 19.85 11/4084 11032 | `/*/Antiphon.Tests.Application/(StandingResumeReceiptReadinessTests*)\|(StandingSessionQueueSwitchTests*)\|(StandingSessionSelectionTests*)\|(StandingSpecialistSeatTests*)/*` |
| C010 a1 | Antiphon.Tests | 0 | 22/22/0/0 | 162.113 | 105/55 | 12.81 17.01 19.85 10/4085 11033 | 21.90 19.91 20.57 4/4128 17554 | `/*/Antiphon.Tests.Application/StandingSessionSwitchConcurrencyTests/(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)\|(Obsolete_queued_launch_cannot_overwrite_a_newer_outcome*)\|(Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation*)\|(Failure_after_reservation_writes_rolls_back_owner_pointer_hold_queue_and_decision*)\|(Concurrent_starts_and_supervisor_reserve_one_generation*)\|(Reservation_rechecks_changes_committed_after_runner_preflight*)\|(Stop_supersedes_launch_before_spawn_and_before_typing*)` |
| C011 a1 | Antiphon.Tests | 0 | 22/22/0/0 | 82.207 | 0/80 | 21.90 19.91 20.57 5/4128 17554 | 13.07 17.41 19.62 7/3796 11177 | `/*/Antiphon.Tests.Application/(AttentionApiTests*)\|(AttentionFeedNoiseTests*)/*` |
| C012 a1 | Antiphon.Tests | 0 | 100/100/0/0 | 74.879 | 0/74 | 13.07 17.41 19.62 6/3796 11177 | 9.61 15.28 18.69 4/3895 23628 | `/*/Antiphon.Tests.Application/AttentionServiceTests/(a_task_holding_a_commit_recovery_obligation_older_than_two_minutes_is_an_error_row_with_the_recipe*)\|(a_commit_recovery_obligation_becomes_visible_only_after_two_full_minutes*)\|(a_resolved_commit_recovery_obligation_is_not_listed*)\|(a_failed_task_with_an_open_commit_recovery_obligation_is_not_listed*)\|(a_dead_session_holding_a_commit_recovery_obligation_is_listed_as_the_hold_not_the_death*)\|(commit_recovery_rows_cost_one_events_query_for_the_whole_open_set*)\|(a_historical_standing_incarnation_without_a_current_pointer_is_unowned*)\|(overlapping_session_leaks_have_one_full_feed_row_until_resolved*)\|(census_inspection_includes_every_candidate_beyond_the_feed_preview*)\|(a_running_pool_delegate_with_a_failed_task_older_than_twice_the_grace_is_unreleased*)\|(a_pool_delegate_inside_twice_the_grace_is_not_listed*)\|(a_stopping_session_older_than_five_minutes_is_stop_stuck*)\|(a_pending_remote_kill_remains_visible_without_claiming_an_unknown_session_is_live*)\|(a_live_session_no_agent_names_is_unowned*)\|(a_live_session_an_agent_names_is_not_unowned*)\|(remote_leak_rows_require_confirmed_liveness*)\|(the_last_census_result_is_one_row_per_class_with_candidates*)\|(Outbound_delivery_states_have_truthful_attention*)\|(A_needs_decision_card_is_a_critical_row_whose_evidence_is_the_move_reason*)\|(A_card_reopened_straight_into_needs_decision_is_listed_with_the_reopen_reason*)\|(A_card_reopened_then_moved_within_needs_decision_shows_the_newest_reason_once*)\|(An_unrated_non_operator_Backlog_import_is_a_warning_review_row*)\|(A_human_rating_clears_the_imported_issue_review_row*)\|(An_operator_author_is_not_an_imported_issue_review_row*)\|(a_blocked_task_is_listed_as_needing_an_answer_and_offers_reply*)\|(a_specialist_role_blocked_task_is_not_a_blocked_question*)\|(a_merge_conflict_dates_from_its_conflicted_event_not_from_dispatch*)\|(a_long_report_that_ends_in_a_question_puts_the_question_on_the_row*)\|(a_blocked_question_with_standing_authority_offers_continue_first*)\|(a_cost_ceiling_block_does_not_offer_reply*)\|(a_message_that_spent_its_delivery_attempts_is_listed_for_a_human*)\|(a_parked_message_on_a_channel_bound_agent_reads_critical*)\|(a_pending_message_that_has_attempts_left_is_not_parked*)\|(an_open_task_whose_session_is_dead_is_listed*)\|(a_dispatched_task_whose_session_has_written_nothing_is_listed*)\|(a_deferred_pending_brief_on_a_working_session_is_listed*)\|(a_pending_brief_on_an_idle_session_is_not_brief_undelivered*)\|(a_sent_brief_on_a_working_session_is_not_brief_undelivered*)\|(a_pending_brief_inside_the_delivery_window_is_not_brief_undelivered*)\|(a_succeeded_task_delegation_note_past_grace_is_caller_note_undelivered*)\|(a_check_note_recovers_the_task_from_conversation_key*)\|(a_caller_note_inside_the_delivery_window_is_not_listed*)\|(a_caller_note_at_exact_grace_is_not_listed*)\|(a_sent_caller_note_is_not_listed*)\|(a_canceled_caller_note_is_not_listed*)\|(an_unparseable_check_key_is_not_listed*)\|(a_delegation_note_on_the_delegate_session_is_not_a_caller_note*)\|(a_caller_note_row_disappears_once_sent_or_canceled*)\|(a_cardless_details_start_past_grace_with_no_prompt_is_a_warning*)\|(a_cardless_details_start_is_absent_at_the_two_minute_grace_and_present_just_after*)\|(blank_or_whitespace_details_does_not_raise_cardless_details_no_prompt*)\|(a_non_current_owner_does_not_raise_cardless_details_no_prompt*)\|(a_non_running_session_does_not_raise_cardless_details_no_prompt*)\|(a_card_owned_session_does_not_raise_cardless_details_no_prompt*)\|(a_resumed_session_does_not_raise_cardless_details_no_prompt*)\|(a_herdr_attached_session_does_not_raise_cardless_details_no_prompt*)\|(any_transcript_entry_suppresses_cardless_details_no_prompt*)\|(a_pending_ui_queue_row_suppresses_cardless_details_no_prompt*)\|(a_sent_ui_queue_row_suppresses_cardless_details_no_prompt*)\|(cardless_details_no_prompt_disappears_after_a_transcript_arrives*)\|(a_dispatched_task_still_inside_the_start_grace_is_not_listed*)\|(a_report_that_could_not_be_correlated_lists_its_task_once*)\|(a_dispatched_task_with_a_marked_report_in_the_transcript_is_report_unsettled*)\|(report_unsettled_clears_once_the_task_is_no_longer_open*)\|(a_dead_session_with_a_marked_report_stays_dead_session_not_report_unsettled*)\|(never_started_is_not_report_unsettled*)\|(an_uncorrelated_incident_without_this_tasks_token_is_not_report_unsettled*)\|(An_active_hold_is_an_error_row_and_clears_when_ClearedAt_is_set*)\|(A_manual_hold_row_carries_kind_alias_and_clears_after_DELETE*)\|(A_fallback_hold_row_does_not_call_the_retry_a_provider_reset*)\|(A_legacy_null_auto_detected_hold_keeps_the_no_reset_stated_headline*)\|(a_nudged_idle_task_with_no_token_is_unmarked_waiting*)\|(unmarked_waiting_clears_to_blocked_question_once_the_task_blocks*)` |
| C013 a1 | Antiphon.Tests | 0 | 68/68/0/0 | 52.251 | 0/51 | 9.61 15.28 18.69 6/3896 23629 | 9.30 14.26 18.16 17/3820 25434 | `/*/Antiphon.Tests.Application/AttentionServiceTests/(a_mid_turn_nudged_task_is_not_unmarked_waiting*)\|(a_dead_session_wins_over_unmarked_waiting*)\|(a_task_far_past_its_estimate_and_idle_at_the_prompt_is_listed*)\|(a_task_far_past_its_estimate_but_still_mid_turn_is_never_listed*)\|(a_working_stalled_task_is_listed_as_progress_stalled*)\|(ProgressStalled_beats_Overdue_and_loses_to_PastExpectedIdle_when_idle*)\|(a_pending_refinement_is_named_on_the_stall_headline*)\|(An_open_boot_reply_incident_on_a_live_session_is_liveness_probe_failed*)\|(C1160_Legacy_boot_row_states_silence_and_no_delivery_verdict*)\|(A_boot_prompt_that_was_answered_after_the_incident_is_no_longer_listed*)\|(A_boot_reply_incident_on_a_dead_session_is_not_listed*)\|(An_open_kind_43_incident_on_a_live_session_is_queued_input_stuck*)\|(A_closed_enqueue_episode_is_not_queued_input_stuck*)\|(A_kind_43_incident_on_a_dead_session_is_not_queued_input_stuck*)\|(a_running_idle_agent_with_a_nine_hour_old_transcript_and_no_task_is_outlived*)\|(a_running_agent_mid_turn_is_not_outlived*)\|(a_running_agent_with_a_one_hour_old_transcript_is_not_outlived*)\|(a_running_idle_agent_with_a_queued_task_is_not_outlived*)\|(a_running_agent_with_zero_transcript_rows_is_not_outlived*)\|(a_stopped_worktree_agent_untouched_for_three_days_is_a_leftover*)\|(a_sole_agent_on_a_same_named_empty_board_idle_three_days_is_a_leftover*)\|(a_sole_agent_on_a_named_board_that_holds_a_card_is_not_outlived*)\|(a_second_agent_sharing_the_empty_board_is_not_outlived*)\|(a_sole_agent_on_an_unrelated_named_empty_board_is_not_outlived*)\|(a_leftover_shape_touched_one_day_ago_is_not_outlived*)\|(an_always_on_live_idle_agent_is_not_outlived*)\|(a_channel_bound_live_idle_agent_is_not_outlived*)\|(a_pool_delegate_live_idle_agent_is_not_outlived*)\|(a_project_root_worker_on_a_live_card_board_is_not_outlived*)\|(a_task_dispatched_under_stall_minutes_does_not_run_the_workspace_probe*)\|(a_task_past_stall_minutes_runs_the_workspace_probe*)\|(the_summary_never_runs_the_workspace_probe*)\|(summary_counts_match_a_full_sweep_for_the_same_rows*)\|(summary_open_drops_recent_failures_and_counts_every_other_row*)\|(a_mid_turn_task_closing_on_its_ceiling_is_listed_before_the_sweep_fails_it*)\|(a_task_only_part_way_through_its_ceiling_is_not_listed_as_overdue*)\|(a_breached_deadline_says_the_sweep_is_about_to_fail_it*)\|(an_idle_task_keeps_the_more_explanatory_past_expected_row*)\|(a_task_whose_check_budget_ran_out_is_listed_as_unwatched*)\|(the_check_interpreters_reading_beats_the_digest_tail_when_one_was_stored*)\|(a_task_that_is_still_being_checked_is_not_listed*)\|(recent_incident_links_the_task_in_its_session_at_incident_time*)\|(recent_error_incidents_collapse_to_one_row_per_agent_and_kind*)\|(a_task_that_failed_today_is_carried_as_context*)\|(a_failure_older_than_the_recency_window_is_not_carried*)\|(a_never_dispatched_failure_still_armed_is_FailureUnacknowledged*)\|(an_orchestrator_investigation_incident_is_a_warning_process_row*)\|(an_orchestrator_workspace_incident_is_a_warning_process_row*)\|(once_disarmed_it_is_a_RecentFailure*)\|(a_misfire_is_a_warning_row_until_the_next_good_fire*)\|(a_session_the_database_wrote_off_but_the_runner_still_runs_is_listed*)\|(a_runner_session_with_no_database_row_is_listed_as_unclaimed*)\|(a_disagreement_says_the_dead_session_rows_above_it_are_wrong*)\|(a_session_both_sides_agree_is_running_is_not_a_disagreement*)\|(a_runner_session_that_has_exited_is_not_a_disagreement*)\|(a_runner_that_cannot_answer_omits_the_disagreement_rather_than_reporting_none*)\|(a_runner_that_cannot_answer_degrades_instead_of_throwing*)\|(a_runner_that_answers_is_recorded_as_consulted*)\|(rows_are_ranked_by_severity_and_then_oldest_first*)\|(A_card_in_progress_past_the_threshold_with_nobody_on_it_is_a_warning_row*)\|(A_stale_card_that_never_had_a_task_says_so*)\|(A_card_with_an_open_bound_task_is_not_stale*)\|(A_card_with_a_live_session_is_not_stale*)\|(A_card_owned_by_a_card_session_is_not_stale*)\|(A_card_under_the_threshold_is_not_stale*)\|(A_stale_card_with_no_history_dates_itself_from_started_at*)\|(A_card_in_review_is_never_stale*)` |
| C014 a1 | Antiphon.Tests | 0 | 84/84/0/0 | 108.815 | 0/107 | 9.30 14.26 18.16 10/3813 25435 | 11.87 13.63 17.49 3/3838 14439 | `/*/Antiphon.Tests.Application/(ChannelOutboundTrailingRecoveryTests*)\|(ChannelOutboundCaptureTests*)\|(ChannelOutboundDeadlineTests*)\|(ChannelOutboundDeliveryTests*)\|(ChannelOutboundFailureRecordingTests*)\|(ChannelOutboundMaterializationTests*)/*` |
| C015 a1 | Antiphon.Tests | 137 TIMEBOX/CAP | NO TRX — no counts claimed | 591.704 | n/a/n/a | 11.87 13.63 17.49 4/3839 14440 | 31.84 19.26 18.54 8/4137 9772 | `/*/Antiphon.Tests.Application/(ChannelOutboundMetadataRepairTests*)\|(ChannelOutboundRecoveryTests*)/*` |
| C016 a1 | Antiphon.Tests | 0 | 45/45/0/0 | 64.832 | 0/62 | 31.84 19.26 18.54 24/4137 9772 | 24.33 19.53 18.69 14/3997 31393 | `/*/Antiphon.Tests.Application/AgentTaskLandApprovalRecoveryTests/(C488_StandingJournalBlocksSourceAdvance*)\|(C488_UncertainSourceChildRetained*)\|(C488_ObservedCommitGatesAdvance*)\|(C488_ResolvedCommitGatesOperation*)\|(C488_OperationCommitGatesPreparation*)\|(C488_SourceFailureStopsTarget*)\|(C488_CleanLRetriesSavedAdvance*)\|(C488_CleanEAcknowledgesIntent*)\|(C488_UnrecordedFastForwardRefuses*)\|(C488_ThirdShaRecoveryRefuses*)\|(C488_RecoveryDestinationImmutable*)\|(C488_OriginalApprovalNeverAdoptsHead*)\|(C488_PreparationInputIsWitnessedP*)\|(C488_OriginalPinMatchesApproval*)\|(C488_PreparedPinMatchesPayload*)\|(C488_AutomaticReplacementRefuses*)\|(C488_TargetIntentPreventsReplacement*)\|(C488_ReplacementFailureKeepsPredecessor*)\|(C488_ConflictSupersessionAtomic*)\|(C488_VerifiedResumeKeepsOriginalApproval*)\|(C488_ChangedSourceNeedsNewApproval*)\|(C488_DerivationRetryIsExplicit*)\|(C488_DerivationOriginalMatches*)\|(C488_DerivationRequiresWitnessedRebase*)\|(C488_DerivationRequiresExactCurrentP*)\|(C488_DerivationAncestryUsesOriginal*)\|(C488_DerivationLineagePersists*)\|(C488_DerivationNeverSourceFastForwards*)\|(C488_DerivationAlwaysReverifies*)\|(C488_LegacyAutomaticMutationRefuses*)\|(C488_LegacyQueuedApprovalRequired*)\|(C488_LegacyUnpublishedRecoveryRefusesMovedRemote*)\|(C494_ServiceLegacyGuardStopsBeforeResolution*)\|(C494_ProtocolSnapshotGuardIsIndependent*)\|(C494_ServiceResumeRequiresCompleteSnapshot*)\|(C494_CompleteSnapshotResumesSameOperation*)\|(C488_VerifiedRetryRequiresSourceResolution*)\|(C488_LegacyPublishedCleanupWorks*)\|(C488_LegacyLateBindingOriginalExact*)\|(C488_LegacyLateBindingIdentityValid*)\|(C488_LegacyLateBindingIsOneTime*)\|(C488_LegacyLateBindingNeverFastForwardsP*)` |
| C015r01 a1 | Antiphon.Tests | 0 | 1/1/0/0 | 143.077 | 0/139 | 38.81 24.74 20.57 102/4327 12733 | 31.43 26.83 21.91 4/4058 11202 | `--filter-uid Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Process_death_preserves_ownership_at_each_boundary(System.String).5.1.0` |
| C015r02 a1 | Antiphon.Tests | 0 | 1/1/0/0 | 44.747 | 0/44 | 31.43 26.83 21.91 4/4066 11223 | 38.33 29.10 22.90 4/4087 13111 | `--filter-uid Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Expired_publishing_lease_is_uncertain_until_explicit_retry.1.1.0` |
| C017 a1 | Antiphon.Tests | 0 | 49/49/0/0 | 78.256 | 0/77 | 26.86 27.26 22.49 4/4050 14326 | 26.37 26.71 22.64 4/4017 17433 | `/*/Antiphon.Tests.Application/AgentTaskLandApprovalRecoveryTests/(C488_ResumeRequestOperationLinkMatches*)\|(C488_OriginalRequestProvenanceRetained*)\|(C488_PublishedPayloadSurvivesSourceMovement*)\|(C488_RecoveryPushUsesSavedPayload*)\|(C494_PushIntentWithoutContainmentRechecksRemoteSource*)\|(C494_PushIntentWithoutContainmentRechecksLocalSource*)\|(C494_UnreadablePushIntentTargetStaysUnconfirmed*)\|(C494_ContainedPushIntentChecksIdentityBeforeObservation*)\|(C494_FreshVerifiedDoesNotUseHistoricalPublicationShortcut*)\|(C488_InterruptedRebaseEvidenceRetained*)\|(C488_PublicationNeedsTargetContainment*)\|(C488_CleanupNeedsVerifiedReceipt*)\|(C494_GuardsAreScopedToTheirOwnOperation*)\|(C494_Schema3CheckpointCutsPreserveApproval*)\|(C494_LegacyPreparedRefusesThenFreshApprovalRepairs*)\|(C488_CleanupRetainsMovedSource*)\|(C488_CleanupRetainsUnknownContent*)\|(C488_NotificationFailureCannotUndoPublication*)\|(C488_SourceCheckpointCrashMatrix*)\|(C488_SourceAdvanceWorkerDeathMatrix*)\|(C488_SourceAdvanceResumeMatrix*)\|(C488_ReplacementAndSupersessionMatrix*)\|(C488_RebasedInputLineageMatrix*)\|(C488_VerifiedResumeMatrix*)\|(C488_LegacyRecoveryMatrix*)\|(C488_PublicationReconciliationMatrix*)` |
| C018 a1 | Antiphon.Tests | 0 | 154/154/0/0 | 92.151 | 0/91 | 26.37 26.71 22.64 5/4017 17436 | 11.83 21.69 21.25 5/3856 18816 | `/*/Antiphon.Tests.Application/(AbsentLaunchPolicyTests*)\|(AgentSystemPromptLaunchTests*)\|(DelegateBundleLaunchTests*)\|(DesktopLaunchMemoryAdmissionTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(PinnedProfileLaunchSpecTests*)/*` |
| C019 a1 | Antiphon.Tests | 0 | 195/195/0/0 | 175.134 | 0/173 | 11.83 21.69 21.25 8/3855 18847 | 10.88 16.66 19.35 10/3796 1979 | `/*/Antiphon.Tests.Application/(AgentTaskDeliveryWatchdogTests*)\|(ExpectationWatchdogScenarioTests*)\|(QueuedInputWatchdogTests*)\|(StaleWorkingTranscriptWatchdogTests*)\|(StandingBootWatchdogTests*)\|(WatchdogServiceTests*)/*` |
| C020 a1 | Antiphon.Tests | 0 | 120/120/0/0 | 103.989 | 0/102 | 10.88 16.66 19.35 4/3796 1983 | 11.14 15.03 18.48 4/3830 5194 | `/*/Antiphon.Tests.Application/(AgentTaskInputFallbackTests*)\|(ChannelInputWithheldIncidentTests*)\|(SpecialistInputTransportTests*)\|(TaskInputAttentionCostTests*)\|(TaskInputReadFailureTests*)\|(TaskInputWeakArmBoundaryTests*)/*` |
| C021 a1 | Antiphon.Tests | 0 | 49/49/0/0 | 195.435 | 0/194 | 11.14 15.03 18.48 4/3831 5195 | 8.62 11.83 16.53 7/3860 19401 | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationRecoveryTests/*` |
| C022 a1 | Antiphon.Tests | 0 | 9/9/0/0 | 38.144 | 0/37 | 8.62 11.83 16.53 7/3860 19401 | 8.00 11.29 16.18 7/3934 1077 | `/*/Antiphon.Tests.Application/AgentTaskRefineTests/*` |
| C023 a1 | Antiphon.Tests | 0 | 11/11/0/0 | 11.289 | 0/9 | 8.00 11.29 16.18 10/3935 1078 | 7.51 11.02 16.01 3/3830 15378 | `/*/Antiphon.Tests.Checkpoints/CheckpointLaunchCleanupTests/*` |
| C024 a1 | Antiphon.Tests | 0 | 18/18/0/0 | 62.48 | 0/61 | 7.51 11.02 16.01 3/3831 15384 | 11.85 11.90 16.01 5/3865 2241 | `/*/Antiphon.Tests.Application/(ClaudeCredentialProbeDispatcherTests*)\|(ClaudeDialogDeliveryTests*)\|(ClaudeEffortByRoleDispatchTests*)/*` |
| C025 a1 | Antiphon.Tests | 0 | 4/4/0/0 | 110.623 | 0/109 | 11.85 11.90 16.01 5/3865 2241 | 6.56 9.91 14.83 6/3868 10346 | `/*/Antiphon.Tests.Application/CodexCliObservationTests/(C959_Ladder_and_exact_models_share_floor*)\|(C959_Launcher_descriptors_are_observations*)\|(C959_Compatible_launch_keeps_model_and_runner*)\|(C959_Retry_and_cancellation_recheck*)` |
| C026 a1 | Antiphon.Tests | 0 | 4/4/0/0 | 317.202 | 0/315 | 6.56 9.91 14.83 7/3867 10348 | 11.19 9.92 13.28 5/3707 3366 | `/*/Antiphon.Tests.Application/CodexCliObservationTests/(C959_Probe_failure_never_refuses*)\|(C959_Stale_sample_never_refuses*)\|(C959_Unknown_version_never_refuses*)\|(C959_Old_version_with_floor_never_refuses*)` |
| C027 a1 | Antiphon.Tests | 0 | 85/85/0/0 | 141.663 | 0/140 | 11.19 9.92 13.28 6/3707 3366 | 9.48 10.00 12.86 6/3771 28500 | `/*/Antiphon.Tests.Application/(CodexDelegateDispatchTests*)\|(CodexStartupDeliveryTests*)/*` |
| C028 a1 | Antiphon.Tests | 0 | 7/7/0/0 | 35.483 | 0/35 | 9.48 10.00 12.86 8/3773 28503 | 8.25 9.65 12.64 5/3711 10236 | `/*/Antiphon.Tests.Infrastructure/ExpectationWatchdogJobTests/*` |
| C029 a1 | Antiphon.Tests | 0 | 49/49/0/0 | 57.285 | 0/56 | 8.25 9.65 12.64 5/3711 10236 | 9.04 9.53 12.42 7/3791 21568 | `/*/Antiphon.Tests.Application/(GrokCredentialProbeDispatcherTests*)\|(GrokDelegateDispatchTests*)\|(GrokNativeSessionResumeTests*)\|(GrokStartupReadyOrderingTests*)/*` |
| C030 a1 | Antiphon.Tests | 0 | 33/33/0/0 | 53.451 | 0/52 | 9.04 9.53 12.42 6/3791 21569 | 15.21 11.19 12.82 6/4009 12234 | `/*/Antiphon.Tests.ApiKeys/LaunchEnvLayersIntegrationTests/*` |
| C031 a1 | Antiphon.Tests | 0 | 8/8/0/0 | 132.063 | 75/56 | 15.21 11.19 12.82 5/4010 12235 | 32.93 21.01 16.28 17/4170 17587 | `/*/Antiphon.Tests.Application/(RunnerClaudeFinalLaunchArgvTests*)\|(TaskLaunchTokenBoundaryTests*)\|(UnixDelegateLaunchArgvTests*)/*` |
| C084 a1 | Antiphon.Tests | 0 | 46/46/0/0 | 97.605 | 30/63 | 43.78 25.32 17.91 12/4370 18670 | 38.80 29.35 20.12 19/4195 22484 | `/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1161_Dead_session_grace_is_monotonic*)\|(C1161_Owner_names_the_monotonic_grace*)\|(C1150_Pre_input_refusal_keeps_the_resume_failure_path*)\|(C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command*)\|(C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal*)` |
| C064 a1 | Antiphon.Tests | 0 | 96/96/0/0 | 187.704 | 0/185 | 38.80 29.35 20.12 10/4194 22485 | 28.07 30.71 22.60 35/3868 8712 | `/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)\|(CapacityObsoleteQueueTests*)\|(CapacityRecoveryAttentionTests*)\|(CapacityRecoveryTaskTests*)/*` |
| C054 a1 | Antiphon.Tests | 0 | 217/217/0/0 | 186.753 | 0/184 | 28.07 30.71 22.60 6/3874 8721 | 7.94 19.96 19.87 18/3879 12321 | `/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/(C527_equals_separator_delivers_exact_parent_receipt*)\|(C527_successful_empty_history_preserves_recovery_until_parent_receipt*)\|(C527_retry_prerequisite_failure_preserves_commit_until_complete_parent_receipt*)\|(C527_post_commit_inspection_failure_defers_settlement_until_complete_receipt*)\|(C527_policy_flip_after_commit_save_failure_recovers_exact_parent_receipt*)\|(C527_lost_history_and_repository_preserve_durable_recovery_until_parent_receipt*)\|(C527_recovery_obligation_save_failure_prevents_git_mutation*)\|(C527_literal_footprint_reaches_complete_parent_receipt_without_matching_neighbor*)\|(C527_branch_movement_after_failed_save_recovers_original_parent_receipt*)\|(C527_all_selected_reverted_delivers_no_op_without_Commit_child*)\|(C527_child_audit_distinguishes_ignore_exceptions_in_complete_parent_receipt*)\|(C527_repeated_report_selects_this_settlements_notification*)\|(C527_unavailable_child_audit_is_reported_to_parent*)\|(C527_completion_outbox_recovers_complete_receipt*)\|(C527_partial_or_incidental_task_commit_does_not_hide_dirty_work*)\|(C527_recovered_settlement_reports_new_dirty_work_without_committing_it*)\|(C527_spawned_child_audit_compares_independent_upstream_baselines*)\|(C527_obligation_free_history_search_failure_settles_once_with_an_uncommitted_note*)\|(C527_obligation_free_history_search_failure_on_a_clean_tree_still_reports_landed*)\|(C527_obligation_free_repository_inspection_failure_settles_once*)\|(C527_obligation_free_search_and_status_failure_refuses_the_commit_once*)\|(C527_resolved_no_commit_obligation_survives_a_failed_search_on_the_rehand*)\|(C527_a_worktree_settle_with_a_pending_gated_receipt_still_reports_merged*)\|(C527_selected_revert_delivers_exact_parent_receipt*)\|(C527_tier1_commits_the_transcript_footprint_and_delivers_committed_to_the_parent*)\|(C527_residual_dirty_path_outside_the_footprint_is_left_and_named*)\|(C527_report_named_path_is_footprint_when_the_transcript_is_empty*)\|(C527_notebook_path_edits_count_as_footprint*)\|(C527_unattributable_dirty_tree_spawns_a_commit_child_routed_by_the_card_pin*)\|(C527_commit_child_falls_back_to_role_policy_with_a_warning_when_no_pin_service*)\|(C527_NoCommit_task_leaves_the_tree_with_the_no_commit_header*)\|(C527_project_off_inherited_skips_with_todays_warning*)\|(C527_task_Always_beats_project_off*)\|(C527_Agent_policy_skips_tier1_and_spawns_the_child*)\|(C527_global_off_with_project_null_skips*)\|(C527_deleted_gitignore_refuses_spawns_the_child_and_warns*)\|(C527_held_lease_is_RepositoryBusy_and_spawns_the_child*)\|(C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr*)\|(C527_commit_child_audit_flags_an_ignored_path_committed_outside_the_gate*)\|(C527_commit_child_audit_flags_a_commit_without_the_gate_trailer*)\|(C527_commit_child_audit_flags_upstream_movement*)\|(C527_task_cap_leaves_the_header_and_warning*)\|(C527_gate_refusal_with_no_child_possible_renders_commit_refused*)\|(C527_tracked_then_ignored_path_refuses_the_whole_footprint*)\|(C527_foreign_staged_path_survives_tier1_untouched*)\|(C527_busy_parent_receives_the_committed_note_on_its_turn_end*)\|(C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit*)\|(C527_ineligible_tasks_never_run_the_hook*)\|(C527_edits_before_dispatch_are_not_in_the_footprint*)\|(C527_more_than_twenty_dirty_paths_are_all_attributed_from_the_transcript*)\|(C544_shared_commit_on_settle_mints_one_task_completion*)\|(C547_commit_child_audit_accepts_a_gated_commit_under_each_separator*)\|(C547_commit_child_audit_flags_a_body_prose_mention_without_a_trailer_block*)\|(C547_commit_child_audit_read_failure_is_unavailable_not_outside_the_gate*)\|(C547_overdue_sweep_past_the_hold_abandons_the_obligation_and_the_parent_hears_it*)\|(C547_overdue_sweep_holds_a_settled_gated_commit_until_the_rehand_records_it*)\|(C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds*)\|(C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending*)\|(C547_an_unresolved_obligation_from_another_digest_does_not_hold_this_settlement*)\|(C547_an_abandoned_obligation_is_invisible_to_a_later_same_digest_settlement*)\|(C714_Grok_batched_replay_uses_report_response*)\|(C714_Claude_ack_after_report_uses_report_response*)\|(C714_Claude_notification_final_report_still_settles*)\|(C714_Codex_final_answer_uses_turn_id*)\|(C714_Parent_receives_selected_report_once*)\|(C714_Grok_repeated_housekeeping_is_skipped*)\|(C714_Grok_rules_then_only_completion_cannot_settle*)\|(C714_Genuine_unmarked_turn_is_barrier*)\|(C714_Grok_unmarked_prompt_before_reminder_is_barrier*)\|(C714_Grok_unmarked_queued_prompt_before_reminder_is_barrier*)\|(C714_Claude_unmarked_prompt_before_notification_is_barrier*)\|(C714_Claude_auto_compaction_continuation_settles*)\|(C714_Codex_unmarked_user_turn_is_barrier*)\|(C714_Current_report_is_chosen_after_old_task_history*)\|(C714_Predispatch_current_marker_is_rejected*)\|(C714_Old_task_marker_is_never_current*)\|(C714_Reply_watermark_prevents_stale_report*)\|(C714_Grok_selected_report_waits_for_own_text*)\|(C714_Late_report_text_after_reminder_keeps_own_api_id*)\|(C714_Selected_error_or_cancel_does_not_fall_back*)\|(C1499_a_release_the_runner_answers_exited_then_not_found_is_a_normal_release*)\|(C1499_a_release_that_is_not_confirmed_still_records_the_Error*)\|(C1706_a_reply_merge_conflict_in_eighty_long_names_writes_a_bounded_conflicted_detail*)\|(C1706_a_reply_merge_conflict_at_the_task_cap_writes_a_bounded_cap_header*)\|(C1710_a_read_only_report_naming_a_directory_of_long_unicode_names_writes_a_bounded_valid_warning*)\|(C788_NoChangeFutureDispatchCleansUp*)\|(Internal_conversion_settles_with_usage_but_without_sources_or_follow_up*)\|(Deferred_is_durable_and_releases_runtime*)\|(a_failed_api_error_task_names_session_liveness*)\|(releasing_a_shared_task_whose_session_is_mid_turn_pools_it_and_warns*)\|(a_transient_stub_with_a_later_prompt_does_not_fail_the_task*)\|(a_transient_stub_with_a_later_queued_prompt_does_not_fail_the_task*)\|(a_needs_human_stub_with_a_later_prompt_does_not_fail_the_task*)\|(the_skip_arm_records_the_death_once_on_the_timeline*)\|(a_housekeeping_record_after_the_stub_is_not_a_resume*)\|(the_resumed_turns_marked_report_settles_the_task*)\|(a_second_stub_on_the_resumed_turn_defers_on_its_own_row*)\|(an_unmarked_resumed_turn_is_still_uncorrelated*)\|(Phone_parent_preserves_worker_table_and_stage_handoff*)\|(Phone_note_waits_for_a_busy_parent_and_lands_on_its_turn_end*)\|(Phone_note_survives_a_failed_delivery_and_is_redelivered_exactly_once*)\|(Grok_rules_refresh_with_task_marker_neither_settles_nor_nudges*)\|(a_marked_turn_settles_the_task_and_stores_the_report_verbatim*)\|(an_unmarked_turn_leaves_the_task_running*)\|(another_tasks_marker_does_not_settle_this_task*)\|(a_cancelled_turn_end_does_not_settle_the_task_and_says_so*)\|(a_prior_internal_continue_does_not_make_a_cancelled_turn_a_report*)\|(the_interrupted_warning_is_written_once_per_boundary*)\|(the_next_end_turn_after_a_cancel_settles_normally*)\|(an_end_turn_without_the_closing_line_is_nudged_once_not_settled*)\|(a_prior_internal_continue_does_not_turn_unmarked_narration_into_completion*)\|(a_narrow_policy_does_not_infer_design_approval_from_report_prose*)\|(the_first_unmarked_end_notifies_the_parent_as_well_as_nudging_the_child*)\|(the_waiting_note_names_authority_only_when_it_is_set*)\|(block_unmarked_waiting_then_answer_resumes_the_same_session*)\|(unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report*)\|(the_nudged_delegates_marked_reply_settles_with_marked_evidence*)\|(a_second_unmarked_end_turn_settles_as_unmarked_after_nudge*)\|(a_specialist_role_task_is_never_nudged*)\|(a_specialist_role_looks_stuck_blocked_token_settles_succeeded_exempt*)\|(a_specialist_role_trailing_question_settles_succeeded_exempt*)\|(a_specialist_role_failed_token_still_fails_the_task*)` |
| C104 a1 | Antiphon.Tests | 0 | 17/17/0/0 | 81.624 | 0/80 | 7.94 19.96 19.87 6/3891 12323 | 34.78 24.97 21.59 2/4022 15921 | `/*/Antiphon.Tests.Application/(PendingNoteWithholdTests*)\|(PendingSettlementDeliveryTests*)/*` |
| C067 a1 | Antiphon.Tests | 0 | 95/95/0/0 | 233.681 | 0/232 | 34.78 24.97 21.59 2/4023 15922 | 9.93 17.46 19.32 6/3805 4092 | `/*/Antiphon.Tests.Application/(ChannelBridgeTests*)\|(ChannelInboundRecoveryTests*)\|(ChannelIngressIncidentTests*)/*` |
| C090 a1 | Antiphon.Tests | 0 | 68/68/0/0 | 303.159 | 0/301 | 9.93 17.46 19.32 7/3805 4092 | 7.26 15.49 18.37 2/3570 26581 | `/*/Antiphon.Tests.Application/(DispatchBaseNotificationTests*)\|(DispatchHeldAttentionTests*)/*` |
| C115 a1 | Antiphon.Tests | 0 | 21/21/0/0 | 56.774 | 0/55 | 7.26 15.49 18.37 3/3571 26582 | 5.97 13.68 17.58 2/3482 27690 | `/*/Antiphon.Tests.Application/ReceiptFailureDeliveryTests/*` |
| C121 a1 | Antiphon.Tests | 0 | 69/69/0/0 | 78.687 | 0/77 | 5.97 13.68 17.58 4/3482 27690 | 5.89 11.89 16.63 7/3612 3061 | `/*/Antiphon.Tests.Application/(RoutingExhaustedPlacementTests*)\|(RoutingPinCandidateDispatchTests*)\|(RoutingPinDispatcherTests*)/*` |
| C034 a1 | Antiphon.Tests | 0 | 170/170/0/0 | 140.476 | 0/139 | 5.89 11.89 16.63 3/3612 3062 | 7.50 9.64 15.08 13/3920 1312 | `/*/Antiphon.Tests.Application/(AgentTaskCheckInterpreterTests*)\|(AgentTaskCheckScheduleTests*)\|(AgentTaskCheckSweepTests*)\|(AgentTaskCommitEndpointTests*)\|(AgentTaskDeadSessionReconciliationTests*)\|(AgentTaskDispatchFailureTests*)/*` |
| C068 a1 | Antiphon.Tests | 0 | 47/47/0/0 | 282.33 | 0/279 | 7.50 9.64 15.08 11/3919 1313 | 26.09 23.40 19.95 6/3807 2140 | `/*/Antiphon.Tests.Application/(CheckCompactionAttentionTests*)\|(CheckCompactionReceiptTests*)\|(CheckNoteDeliveryHandoffTests*)/*` |
| C075 a1 | Antiphon.Tests | 0 | 15/15/0/0 | 63.375 | 0/62 | 26.09 23.40 19.95 9/3808 2141 | 19.60 21.87 19.65 10/3993 27009 | `/*/Antiphon.Tests.Application/(DelegationBriefRecoveryTests*)\|(DelegationScopeHoldTests*)/*` |
| C114 a1 | Antiphon.Tests | 0 | 4/4/0/0 | 66.868 | 0/66 | 19.60 21.87 19.65 15/3994 27017 | 20.90 21.94 19.83 9/4031 11024 | `/*/Antiphon.Tests.Application/ReadyGateFailureNoticeDeliveryTests/*` |
| C120 a1 | Antiphon.Tests | 0 | 8/8/0/0 | 94.893 | 30/62 | 20.90 21.94 19.83 19/4032 11025 | 15.91 20.04 19.36 34/4032 20757 | `/*/Antiphon.Tests.Application/RolePolicyToolBundleDispatchTests/*` |
| C132 a1 | Antiphon.Tests | 2 | 21/20/1/0 | 249.182 | 0/246 | 15.91 20.04 19.36 35/4034 20759 | 14.41 19.04 19.23 5/3881 12242 | `/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/*` |
| C069 a1 | Antiphon.Tests | 0 | 5/5/0/0 | 51.026 | 0/49 | 14.41 19.04 19.23 3/3883 12244 | 10.37 17.17 18.58 2/3629 13850 | `/*/Antiphon.Tests.Application/ClosingReportNudgeLifecycleTests/*` |
| C099 a1 | Antiphon.Tests | 0 | 4/4/0/0 | 62.612 | 0/61 | 10.37 17.17 18.58 2/3629 13850 | 15.41 16.85 18.34 24/3953 6536 | `/*/Antiphon.Tests.Application/MatrixChildReceiptTests/*` |
| C105 a1 | Antiphon.Tests | 0 | 12/12/0/0 | 84.886 | 0/82 | 15.41 16.85 18.34 13/3953 6536 | 28.99 21.55 19.92 7/4284 11303 | `/*/Antiphon.Tests.Application/PermissionWaitTests/*` |
| C134 a1 | Antiphon.Tests | 2 | 62/61/1/0 | 132.886 | 15/116 | 28.99 21.55 19.92 8/4285 11304 | 33.37 25.51 21.56 18/4203 17368 | `/*/Antiphon.Tests.Application/(WorktreeDefaultCallerTests*)\|(WorktreeRetirementRaceTests*)/*` |
| C074 a1 | Antiphon.Tests | 0 | 7/7/0/0 | 173.313 | 30/141 | 33.37 25.51 21.56 19/4204 17369 | 24.96 24.28 21.71 11/4143 28994 | `/*/Antiphon.Tests.Application/DelegateBranchDeliveryTests/*` |
| R008 a1 | Antiphon.Tests | 0 | 1/1/0/0 | 101.707 | 45/54 | 22.46 24.50 22.81 30/4150 26187 | 18.64 23.64 22.75 10/3962 26423 | `/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/C544_ReportRegeneratedFromSnapshot` |
| R009 a1 | Antiphon.Tests | 0 | 1/1/0/0 | 89.008 | 15/73 | 18.64 23.64 22.75 14/3961 26438 | 23.30 23.06 22.57 21/4085 17305 | `/*/Antiphon.Tests.Application/WorktreeDefaultCallerTests/SpecialistFactoriesStayOnSeat` |

### C015 execution cap and preserved partial observations

The combined channel row exceeded its selection estimate and was stopped at the unchanged590-second execution cap (exit137;591.704s wall). No finalized TRX exists and no failing assertion was emitted. Detailed stdout records22 completed passes, with canonical ANTIPHON_TEST_PHASE_ID identifiers. Both selected source files have no diff from the committed discovery-fixture source808de327f593874469a8e84597ccb154f4994272 to measured8cec1e74, so its24-case roster identifies exactly two unfinished cases. Only those two were selected afterward, individually: conversion-dispatched and Expired_publishing_lease_is_uncertain_until_explicit_retry. This is a row-cost/cap event, not proof of host LOAD or a product assertion failure. The interrupted case received its once-alone attempt; the never-started case received its first attempt.

| Completed pass from capped stdout | Canonical UID |
|---|---|
| `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.C519_Acceptance_and_all_settlements_commit_together` | `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.1.1.C519_Acceptance_and_all_settlements_commit_together.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.C519_Projection_repair_never_reenters_producer` | `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.1.1.C519_Projection_repair_never_reenters_producer.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.C519_Projection_repair_cannot_regress_preview` | `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.1.1.C519_Projection_repair_cannot_regress_preview.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.C519_Bundle_stamp_uses_frozen_complete_actual_payload` | `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.1.1.C519_Bundle_stamp_uses_frozen_complete_actual_payload.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.C519_Projection_repairs_every_implied_task` | `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.1.1.C519_Projection_repairs_every_implied_task.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.C519_Fair_bounded_send_and_repair_budgets_are_independent` | `Antiphon.Tests.Application.ChannelOutboundMetadataRepairTests.1.1.C519_Fair_bounded_send_and_repair_budgets_are_independent.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Resume_held_after_conversion_returns_to_ready` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Resume_held_after_conversion_returns_to_ready.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Broker_ack_before_process_death_remains_uncertain_without_replay` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Broker_ack_before_process_death_remains_uncertain_without_replay.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(input-temporary-partial)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Process_death_preserves_ownership_at_each_boundary(System.String).1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(admission-committed)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Process_death_preserves_ownership_at_each_boundary(System.String).3.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(input-temporary-complete)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Process_death_preserves_ownership_at_each_boundary(System.String).2.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries(before-conversion-observation, Published, 1)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Recovery_preserves_settled_worker_and_publication_boundaries(System.String, Antiphon.Server.Domain.Enums.ChannelOutboundDeliveryState, System.Int32).1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Running_converter_reconciles_after_dispatcher_death` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Running_converter_reconciles_after_dispatcher_death.1.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary(conversion-task-committed)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Process_death_preserves_ownership_at_each_boundary(System.String).4.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries(ready-committed, Published, 1)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Recovery_preserves_settled_worker_and_publication_boundaries(System.String, Antiphon.Server.Domain.Enums.ChannelOutboundDeliveryState, System.Int32).2.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries(publishing-committed, PublishUncertain, 0)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Recovery_preserves_settled_worker_and_publication_boundaries(System.String, Antiphon.Server.Domain.Enums.ChannelOutboundDeliveryState, System.Int32).3.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries(producer-accepted, PublishUncertain, 1)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Recovery_preserves_settled_worker_and_publication_boundaries(System.String, Antiphon.Server.Domain.Enums.ChannelOutboundDeliveryState, System.Int32).4.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries(published-committed, Published, 1)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Recovery_preserves_settled_worker_and_publication_boundaries(System.String, Antiphon.Server.Domain.Enums.ChannelOutboundDeliveryState, System.Int32).5.1.0` |
| `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries(publication-commit-failed, PublishUncertain, 1)` | `Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Recovery_preserves_settled_worker_and_publication_boundaries(System.String, Antiphon.Server.Domain.Enums.ChannelOutboundDeliveryState, System.Int32).6.1.0` |

These22 stdout passes are kept separate from finalized TRX counters. They are sufficient to avoid repeating observed cases in this Debug sweep, but they are not validated checkpoint/Review receipts. The class coverage ledger uses their exact method/argument identities and retains the evidence limitation. Raw stdout path: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C015/C015-a1.stdout.log; SHA-256 2ad838ded261b2634728f870af1018ca3292f4bafad1d55f72882ef4a93b538c.

### Exact rerun commands and TRX provenance

**C001 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C001-a1 -SlotWaitMinutes 5 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(GrokRulesCompactionAcceptanceTests*)|(GrokRulesDispatchAcceptanceTests*)|(GrokRulesLiveMappedDispatchTests*)/*' --report-trx --report-trx-filename C001-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C001 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C001/C001-a1.trx; SHA-256 cddc957a6e4f9769293be5e2686501ce502d3f7ba35db90830cf53f45732640f.

**C002 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C002-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AgentControlServiceIntegrationTests/*' --report-trx --report-trx-filename C002-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C002 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C002/C002-a1.trx; SHA-256 85c0f98f28d5f6264f47387b1d2a71cb02ceddefcf900962df2381889334f475.

**C003 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C003-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(PhoneHomeBackpressureTests*)|(PhoneHomeCompletedCardTransportTests*)|(PhoneHomeDeferredKillTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomeLaunchTransportTests*)|(PhoneHomeMirrorJoinTests*)/*' --report-trx --report-trx-filename C003-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C003 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C003/C003-a1.trx; SHA-256 d7f1fef1eb398970fa013acb5c740c624c0e8d3c86d3b7544e88ac1757b52d4d.

**C004 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C004-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Agents/(PhoneHomeConnectionTests*)|(PhoneHomeEpochAgreementTests*)/*' --report-trx --report-trx-filename C004-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C004 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C004/C004-a1.trx; SHA-256 fc6c1f686c3c5771c47c9f6cb982647b6fa310312dee0b23b826094886147ac1.

**C005 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C005-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/PhoneHomeOutageMentionTests/(C696Red_Mention_before_first_List_is_persisted*)|(C696Red_Mention_in_reconnect_gap_survives_restart*)|(Busy_local_and_remote_mentions_wait_for_idle*)|(Occurrence_replay_is_idempotent_but_identical_new_mentions_are_distinct*)|(Split_line_and_completed_newline_accept_one_occurrence*)|(Accepted_mention_keeps_original_target_after_source_ends*)|(Queued_activity_failure_does_not_duplicate_or_block_recovery*)' --report-trx --report-trx-filename C005-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C005 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C005/C005-a1.trx; SHA-256 d294a0b9d77c46eb62a845234120f51db16bbdb95e05d6df2810c4c430655d4e.

**C006 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C006-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(PhoneHomePendingInventoryTests*)|(PhoneHomeQueuedTurnTests*)|(PhoneHomeRollingRunnerTests*)|(PhoneHomeSessionRoutingTests*)|(PhoneHomeSpillTransportTests*)|(PhoneHomeStandingLaunchTests*)/*' --report-trx --report-trx-filename C006-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C006 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C006/C006-a1.trx; SHA-256 e9c9e8ce82acd58467e33621f13b66cc0ca9d63931715557b8d7a511de36f6df.

**C007 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C007-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(PhoneHomeStrandedQueueTests*)|(PhoneHomeTaskDispatchProjectionTests*)/*' --report-trx --report-trx-filename C007-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C007 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C007/C007-a1.trx; SHA-256 c8bb05c1e29e5a4c9c51ca477c95a2e8eb13a0ec185f61c76d40ed5a95319476.

**C008 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C008-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(StandingBootAttentionTests*)|(StandingBootDocumentationTests*)|(StandingBootQueueWitnessTests*)|(StandingBootStatementBudgetTests*)|(StandingContinuityAttentionTests*)|(StandingRestartEscalationTests*)/*' --report-trx --report-trx-filename C008-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C008 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C008/C008-a1.trx; SHA-256 0899ab12e2e258aaf0a0c1a1a2c254721009b05a6d36b1dc1c5e2fedbe58f74f.

**C009 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C009-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(StandingResumeReceiptReadinessTests*)|(StandingSessionQueueSwitchTests*)|(StandingSessionSelectionTests*)|(StandingSpecialistSeatTests*)/*' --report-trx --report-trx-filename C009-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C009 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C009/C009-a1.trx; SHA-256 054c0c98541270f99f2f5185a66c0ff074104f1f854e50017db9209720adb51e.

**C010 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C010-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/StandingSessionSwitchConcurrencyTests/(Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation*)|(Obsolete_queued_launch_cannot_overwrite_a_newer_outcome*)|(Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation*)|(Failure_after_reservation_writes_rolls_back_owner_pointer_hold_queue_and_decision*)|(Concurrent_starts_and_supervisor_reserve_one_generation*)|(Reservation_rechecks_changes_committed_after_runner_preflight*)|(Stop_supersedes_launch_before_spawn_and_before_typing*)' --report-trx --report-trx-filename C010-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C010 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C010/C010-a1.trx; SHA-256 eab386356af255e71cd9ac5ac7359d244acf9243a5f7b2d6c33e7c5ea0910826.

**C011 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C011-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(AttentionApiTests*)|(AttentionFeedNoiseTests*)/*' --report-trx --report-trx-filename C011-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C011 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C011/C011-a1.trx; SHA-256 842952a4b53478bda320f0a35640d0becab6a4ef1e4b1cfedd38baf203473086.

**C012 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C012-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AttentionServiceTests/(a_task_holding_a_commit_recovery_obligation_older_than_two_minutes_is_an_error_row_with_the_recipe*)|(a_commit_recovery_obligation_becomes_visible_only_after_two_full_minutes*)|(a_resolved_commit_recovery_obligation_is_not_listed*)|(a_failed_task_with_an_open_commit_recovery_obligation_is_not_listed*)|(a_dead_session_holding_a_commit_recovery_obligation_is_listed_as_the_hold_not_the_death*)|(commit_recovery_rows_cost_one_events_query_for_the_whole_open_set*)|(a_historical_standing_incarnation_without_a_current_pointer_is_unowned*)|(overlapping_session_leaks_have_one_full_feed_row_until_resolved*)|(census_inspection_includes_every_candidate_beyond_the_feed_preview*)|(a_running_pool_delegate_with_a_failed_task_older_than_twice_the_grace_is_unreleased*)|(a_pool_delegate_inside_twice_the_grace_is_not_listed*)|(a_stopping_session_older_than_five_minutes_is_stop_stuck*)|(a_pending_remote_kill_remains_visible_without_claiming_an_unknown_session_is_live*)|(a_live_session_no_agent_names_is_unowned*)|(a_live_session_an_agent_names_is_not_unowned*)|(remote_leak_rows_require_confirmed_liveness*)|(the_last_census_result_is_one_row_per_class_with_candidates*)|(Outbound_delivery_states_have_truthful_attention*)|(A_needs_decision_card_is_a_critical_row_whose_evidence_is_the_move_reason*)|(A_card_reopened_straight_into_needs_decision_is_listed_with_the_reopen_reason*)|(A_card_reopened_then_moved_within_needs_decision_shows_the_newest_reason_once*)|(An_unrated_non_operator_Backlog_import_is_a_warning_review_row*)|(A_human_rating_clears_the_imported_issue_review_row*)|(An_operator_author_is_not_an_imported_issue_review_row*)|(a_blocked_task_is_listed_as_needing_an_answer_and_offers_reply*)|(a_specialist_role_blocked_task_is_not_a_blocked_question*)|(a_merge_conflict_dates_from_its_conflicted_event_not_from_dispatch*)|(a_long_report_that_ends_in_a_question_puts_the_question_on_the_row*)|(a_blocked_question_with_standing_authority_offers_continue_first*)|(a_cost_ceiling_block_does_not_offer_reply*)|(a_message_that_spent_its_delivery_attempts_is_listed_for_a_human*)|(a_parked_message_on_a_channel_bound_agent_reads_critical*)|(a_pending_message_that_has_attempts_left_is_not_parked*)|(an_open_task_whose_session_is_dead_is_listed*)|(a_dispatched_task_whose_session_has_written_nothing_is_listed*)|(a_deferred_pending_brief_on_a_working_session_is_listed*)|(a_pending_brief_on_an_idle_session_is_not_brief_undelivered*)|(a_sent_brief_on_a_working_session_is_not_brief_undelivered*)|(a_pending_brief_inside_the_delivery_window_is_not_brief_undelivered*)|(a_succeeded_task_delegation_note_past_grace_is_caller_note_undelivered*)|(a_check_note_recovers_the_task_from_conversation_key*)|(a_caller_note_inside_the_delivery_window_is_not_listed*)|(a_caller_note_at_exact_grace_is_not_listed*)|(a_sent_caller_note_is_not_listed*)|(a_canceled_caller_note_is_not_listed*)|(an_unparseable_check_key_is_not_listed*)|(a_delegation_note_on_the_delegate_session_is_not_a_caller_note*)|(a_caller_note_row_disappears_once_sent_or_canceled*)|(a_cardless_details_start_past_grace_with_no_prompt_is_a_warning*)|(a_cardless_details_start_is_absent_at_the_two_minute_grace_and_present_just_after*)|(blank_or_whitespace_details_does_not_raise_cardless_details_no_prompt*)|(a_non_current_owner_does_not_raise_cardless_details_no_prompt*)|(a_non_running_session_does_not_raise_cardless_details_no_prompt*)|(a_card_owned_session_does_not_raise_cardless_details_no_prompt*)|(a_resumed_session_does_not_raise_cardless_details_no_prompt*)|(a_herdr_attached_session_does_not_raise_cardless_details_no_prompt*)|(any_transcript_entry_suppresses_cardless_details_no_prompt*)|(a_pending_ui_queue_row_suppresses_cardless_details_no_prompt*)|(a_sent_ui_queue_row_suppresses_cardless_details_no_prompt*)|(cardless_details_no_prompt_disappears_after_a_transcript_arrives*)|(a_dispatched_task_still_inside_the_start_grace_is_not_listed*)|(a_report_that_could_not_be_correlated_lists_its_task_once*)|(a_dispatched_task_with_a_marked_report_in_the_transcript_is_report_unsettled*)|(report_unsettled_clears_once_the_task_is_no_longer_open*)|(a_dead_session_with_a_marked_report_stays_dead_session_not_report_unsettled*)|(never_started_is_not_report_unsettled*)|(an_uncorrelated_incident_without_this_tasks_token_is_not_report_unsettled*)|(An_active_hold_is_an_error_row_and_clears_when_ClearedAt_is_set*)|(A_manual_hold_row_carries_kind_alias_and_clears_after_DELETE*)|(A_fallback_hold_row_does_not_call_the_retry_a_provider_reset*)|(A_legacy_null_auto_detected_hold_keeps_the_no_reset_stated_headline*)|(a_nudged_idle_task_with_no_token_is_unmarked_waiting*)|(unmarked_waiting_clears_to_blocked_question_once_the_task_blocks*)' --report-trx --report-trx-filename C012-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C012 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C012/C012-a1.trx; SHA-256 310c4e9a9d348177b09972d0fc5d825b79f201073f9780c0eacfcfb9f2213b8b.

**C013 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C013-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AttentionServiceTests/(a_mid_turn_nudged_task_is_not_unmarked_waiting*)|(a_dead_session_wins_over_unmarked_waiting*)|(a_task_far_past_its_estimate_and_idle_at_the_prompt_is_listed*)|(a_task_far_past_its_estimate_but_still_mid_turn_is_never_listed*)|(a_working_stalled_task_is_listed_as_progress_stalled*)|(ProgressStalled_beats_Overdue_and_loses_to_PastExpectedIdle_when_idle*)|(a_pending_refinement_is_named_on_the_stall_headline*)|(An_open_boot_reply_incident_on_a_live_session_is_liveness_probe_failed*)|(C1160_Legacy_boot_row_states_silence_and_no_delivery_verdict*)|(A_boot_prompt_that_was_answered_after_the_incident_is_no_longer_listed*)|(A_boot_reply_incident_on_a_dead_session_is_not_listed*)|(An_open_kind_43_incident_on_a_live_session_is_queued_input_stuck*)|(A_closed_enqueue_episode_is_not_queued_input_stuck*)|(A_kind_43_incident_on_a_dead_session_is_not_queued_input_stuck*)|(a_running_idle_agent_with_a_nine_hour_old_transcript_and_no_task_is_outlived*)|(a_running_agent_mid_turn_is_not_outlived*)|(a_running_agent_with_a_one_hour_old_transcript_is_not_outlived*)|(a_running_idle_agent_with_a_queued_task_is_not_outlived*)|(a_running_agent_with_zero_transcript_rows_is_not_outlived*)|(a_stopped_worktree_agent_untouched_for_three_days_is_a_leftover*)|(a_sole_agent_on_a_same_named_empty_board_idle_three_days_is_a_leftover*)|(a_sole_agent_on_a_named_board_that_holds_a_card_is_not_outlived*)|(a_second_agent_sharing_the_empty_board_is_not_outlived*)|(a_sole_agent_on_an_unrelated_named_empty_board_is_not_outlived*)|(a_leftover_shape_touched_one_day_ago_is_not_outlived*)|(an_always_on_live_idle_agent_is_not_outlived*)|(a_channel_bound_live_idle_agent_is_not_outlived*)|(a_pool_delegate_live_idle_agent_is_not_outlived*)|(a_project_root_worker_on_a_live_card_board_is_not_outlived*)|(a_task_dispatched_under_stall_minutes_does_not_run_the_workspace_probe*)|(a_task_past_stall_minutes_runs_the_workspace_probe*)|(the_summary_never_runs_the_workspace_probe*)|(summary_counts_match_a_full_sweep_for_the_same_rows*)|(summary_open_drops_recent_failures_and_counts_every_other_row*)|(a_mid_turn_task_closing_on_its_ceiling_is_listed_before_the_sweep_fails_it*)|(a_task_only_part_way_through_its_ceiling_is_not_listed_as_overdue*)|(a_breached_deadline_says_the_sweep_is_about_to_fail_it*)|(an_idle_task_keeps_the_more_explanatory_past_expected_row*)|(a_task_whose_check_budget_ran_out_is_listed_as_unwatched*)|(the_check_interpreters_reading_beats_the_digest_tail_when_one_was_stored*)|(a_task_that_is_still_being_checked_is_not_listed*)|(recent_incident_links_the_task_in_its_session_at_incident_time*)|(recent_error_incidents_collapse_to_one_row_per_agent_and_kind*)|(a_task_that_failed_today_is_carried_as_context*)|(a_failure_older_than_the_recency_window_is_not_carried*)|(a_never_dispatched_failure_still_armed_is_FailureUnacknowledged*)|(an_orchestrator_investigation_incident_is_a_warning_process_row*)|(an_orchestrator_workspace_incident_is_a_warning_process_row*)|(once_disarmed_it_is_a_RecentFailure*)|(a_misfire_is_a_warning_row_until_the_next_good_fire*)|(a_session_the_database_wrote_off_but_the_runner_still_runs_is_listed*)|(a_runner_session_with_no_database_row_is_listed_as_unclaimed*)|(a_disagreement_says_the_dead_session_rows_above_it_are_wrong*)|(a_session_both_sides_agree_is_running_is_not_a_disagreement*)|(a_runner_session_that_has_exited_is_not_a_disagreement*)|(a_runner_that_cannot_answer_omits_the_disagreement_rather_than_reporting_none*)|(a_runner_that_cannot_answer_degrades_instead_of_throwing*)|(a_runner_that_answers_is_recorded_as_consulted*)|(rows_are_ranked_by_severity_and_then_oldest_first*)|(A_card_in_progress_past_the_threshold_with_nobody_on_it_is_a_warning_row*)|(A_stale_card_that_never_had_a_task_says_so*)|(A_card_with_an_open_bound_task_is_not_stale*)|(A_card_with_a_live_session_is_not_stale*)|(A_card_owned_by_a_card_session_is_not_stale*)|(A_card_under_the_threshold_is_not_stale*)|(A_stale_card_with_no_history_dates_itself_from_started_at*)|(A_card_in_review_is_never_stale*)' --report-trx --report-trx-filename C013-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C013 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C013/C013-a1.trx; SHA-256 71f06139a068e11bade29ed7d7ff568b310b1863609d3f656469b77505c5f8a5.

**C014 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C014-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(ChannelOutboundTrailingRecoveryTests*)|(ChannelOutboundCaptureTests*)|(ChannelOutboundDeadlineTests*)|(ChannelOutboundDeliveryTests*)|(ChannelOutboundFailureRecordingTests*)|(ChannelOutboundMaterializationTests*)/*' --report-trx --report-trx-filename C014-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C014 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C014/C014-a1.trx; SHA-256 015855a66d5b9f4d42d494c8ef39649f14a513d2127eb815d03483bb2ccbff3a.

**C015 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C015-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(ChannelOutboundMetadataRepairTests*)|(ChannelOutboundRecoveryTests*)/*' --report-trx --report-trx-filename C015-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C015 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C015/C015-a1.trx; SHA-256 not produced.

**C016 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C016-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AgentTaskLandApprovalRecoveryTests/(C488_StandingJournalBlocksSourceAdvance*)|(C488_UncertainSourceChildRetained*)|(C488_ObservedCommitGatesAdvance*)|(C488_ResolvedCommitGatesOperation*)|(C488_OperationCommitGatesPreparation*)|(C488_SourceFailureStopsTarget*)|(C488_CleanLRetriesSavedAdvance*)|(C488_CleanEAcknowledgesIntent*)|(C488_UnrecordedFastForwardRefuses*)|(C488_ThirdShaRecoveryRefuses*)|(C488_RecoveryDestinationImmutable*)|(C488_OriginalApprovalNeverAdoptsHead*)|(C488_PreparationInputIsWitnessedP*)|(C488_OriginalPinMatchesApproval*)|(C488_PreparedPinMatchesPayload*)|(C488_AutomaticReplacementRefuses*)|(C488_TargetIntentPreventsReplacement*)|(C488_ReplacementFailureKeepsPredecessor*)|(C488_ConflictSupersessionAtomic*)|(C488_VerifiedResumeKeepsOriginalApproval*)|(C488_ChangedSourceNeedsNewApproval*)|(C488_DerivationRetryIsExplicit*)|(C488_DerivationOriginalMatches*)|(C488_DerivationRequiresWitnessedRebase*)|(C488_DerivationRequiresExactCurrentP*)|(C488_DerivationAncestryUsesOriginal*)|(C488_DerivationLineagePersists*)|(C488_DerivationNeverSourceFastForwards*)|(C488_DerivationAlwaysReverifies*)|(C488_LegacyAutomaticMutationRefuses*)|(C488_LegacyQueuedApprovalRequired*)|(C488_LegacyUnpublishedRecoveryRefusesMovedRemote*)|(C494_ServiceLegacyGuardStopsBeforeResolution*)|(C494_ProtocolSnapshotGuardIsIndependent*)|(C494_ServiceResumeRequiresCompleteSnapshot*)|(C494_CompleteSnapshotResumesSameOperation*)|(C488_VerifiedRetryRequiresSourceResolution*)|(C488_LegacyPublishedCleanupWorks*)|(C488_LegacyLateBindingOriginalExact*)|(C488_LegacyLateBindingIdentityValid*)|(C488_LegacyLateBindingIsOneTime*)|(C488_LegacyLateBindingNeverFastForwardsP*)' --report-trx --report-trx-filename C016-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C016 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C016/C016-a1.trx; SHA-256 c6fb5bf082d623334dbe7195c38f9fa13665dfcfd61e280e5190b5152a60cbb6.

**C015r01 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C015r01-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --filter-uid 'Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Process_death_preserves_ownership_at_each_boundary(System.String).5.1.0' --report-trx --report-trx-filename C015r01-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C015r01 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C015r01/C015r01-a1.trx; SHA-256 f6be05a2d4345c0d4df26073df026498d99e5556dc76c22a5c1f2d98d73e2ecc.

**C015r02 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C015r02-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --filter-uid Antiphon.Tests.Application.ChannelOutboundRecoveryTests.1.1.Expired_publishing_lease_is_uncertain_until_explicit_retry.1.1.0 --report-trx --report-trx-filename C015r02-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C015r02 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C015r02/C015r02-a1.trx; SHA-256 ae444c05b19b77f1d10d04b86f6d1007eda288fa4a32f771895d08d9cc800cda.

**C017 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C017-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AgentTaskLandApprovalRecoveryTests/(C488_ResumeRequestOperationLinkMatches*)|(C488_OriginalRequestProvenanceRetained*)|(C488_PublishedPayloadSurvivesSourceMovement*)|(C488_RecoveryPushUsesSavedPayload*)|(C494_PushIntentWithoutContainmentRechecksRemoteSource*)|(C494_PushIntentWithoutContainmentRechecksLocalSource*)|(C494_UnreadablePushIntentTargetStaysUnconfirmed*)|(C494_ContainedPushIntentChecksIdentityBeforeObservation*)|(C494_FreshVerifiedDoesNotUseHistoricalPublicationShortcut*)|(C488_InterruptedRebaseEvidenceRetained*)|(C488_PublicationNeedsTargetContainment*)|(C488_CleanupNeedsVerifiedReceipt*)|(C494_GuardsAreScopedToTheirOwnOperation*)|(C494_Schema3CheckpointCutsPreserveApproval*)|(C494_LegacyPreparedRefusesThenFreshApprovalRepairs*)|(C488_CleanupRetainsMovedSource*)|(C488_CleanupRetainsUnknownContent*)|(C488_NotificationFailureCannotUndoPublication*)|(C488_SourceCheckpointCrashMatrix*)|(C488_SourceAdvanceWorkerDeathMatrix*)|(C488_SourceAdvanceResumeMatrix*)|(C488_ReplacementAndSupersessionMatrix*)|(C488_RebasedInputLineageMatrix*)|(C488_VerifiedResumeMatrix*)|(C488_LegacyRecoveryMatrix*)|(C488_PublicationReconciliationMatrix*)' --report-trx --report-trx-filename C017-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C017 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C017/C017-a1.trx; SHA-256 50baf65eee86c7f0c9d12761193f786e6bbfae096758c3313f4abd02ab6c7f69.

**C018 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C018-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(AbsentLaunchPolicyTests*)|(AgentSystemPromptLaunchTests*)|(DelegateBundleLaunchTests*)|(DesktopLaunchMemoryAdmissionTests*)|(PinnedCodexProfileDispatchLaunchTests*)|(PinnedProfileLaunchSpecTests*)/*' --report-trx --report-trx-filename C018-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C018 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C018/C018-a1.trx; SHA-256 a6b48de3cd1cb28ba00b86c954ff3d360f244ffef93f9cf7e60f99649e709a17.

**C019 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C019-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(AgentTaskDeliveryWatchdogTests*)|(ExpectationWatchdogScenarioTests*)|(QueuedInputWatchdogTests*)|(StaleWorkingTranscriptWatchdogTests*)|(StandingBootWatchdogTests*)|(WatchdogServiceTests*)/*' --report-trx --report-trx-filename C019-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C019 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C019/C019-a1.trx; SHA-256 aa907ff0ca0b0903ba30ca383d5d0bf0ee0665458cb7fd6fd341947a639d09c5.

**C020 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C020-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(AgentTaskInputFallbackTests*)|(ChannelInputWithheldIncidentTests*)|(SpecialistInputTransportTests*)|(TaskInputAttentionCostTests*)|(TaskInputReadFailureTests*)|(TaskInputWeakArmBoundaryTests*)/*' --report-trx --report-trx-filename C020-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C020 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C020/C020-a1.trx; SHA-256 de8e082b2541a0d0efc433fccc696d718d058cb21343b3f9a8ef969de3a8907f.

**C021 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C021-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AgentTaskLandNotificationRecoveryTests/*' --report-trx --report-trx-filename C021-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C021 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C021/C021-a1.trx; SHA-256 4ef1d58a0ad9a8ee19404e5b80a5bd52149e0b1c77cd21f7c7d9d2c755f750db.

**C022 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C022-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AgentTaskRefineTests/*' --report-trx --report-trx-filename C022-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C022 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C022/C022-a1.trx; SHA-256 c673dc991a3c52601fc567e68bbbd11f65071f81725a6b7022aafb6b424165fb.

**C023 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C023-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Checkpoints/CheckpointLaunchCleanupTests/*' --report-trx --report-trx-filename C023-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C023 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C023/C023-a1.trx; SHA-256 01bf99aa22bab792446954f4d05070d9b51879d5dd2bf08b3eadc57ee2794ee4.

**C024 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C024-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(ClaudeCredentialProbeDispatcherTests*)|(ClaudeDialogDeliveryTests*)|(ClaudeEffortByRoleDispatchTests*)/*' --report-trx --report-trx-filename C024-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C024 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C024/C024-a1.trx; SHA-256 ae62b4c1247f5f53ed735f364467010f2b0c300ffe3660ab27ce636241de268f.

**C025 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C025-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/CodexCliObservationTests/(C959_Ladder_and_exact_models_share_floor*)|(C959_Launcher_descriptors_are_observations*)|(C959_Compatible_launch_keeps_model_and_runner*)|(C959_Retry_and_cancellation_recheck*)' --report-trx --report-trx-filename C025-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C025 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C025/C025-a1.trx; SHA-256 70e8af7f3efd48653f3606ce4673a58ca14fba82b6df0c78e847df39b5e9d8c3.

**C026 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C026-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/CodexCliObservationTests/(C959_Probe_failure_never_refuses*)|(C959_Stale_sample_never_refuses*)|(C959_Unknown_version_never_refuses*)|(C959_Old_version_with_floor_never_refuses*)' --report-trx --report-trx-filename C026-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C026 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C026/C026-a1.trx; SHA-256 05ca76bfb0f205c6d750ddc57d3ed383473f7f3d7af4c44d7740ba7cb4e36c09.

**C027 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C027-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(CodexDelegateDispatchTests*)|(CodexStartupDeliveryTests*)/*' --report-trx --report-trx-filename C027-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C027 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C027/C027-a1.trx; SHA-256 61a8e5091663439e315f7fcd42c840fd6a00b99cd5581670dc1e517b6edeaa04.

**C028 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C028-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Infrastructure/ExpectationWatchdogJobTests/*' --report-trx --report-trx-filename C028-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C028 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C028/C028-a1.trx; SHA-256 6c6bb96ceec4bdbd2e87bd9ad96ff54683af4c9aed3a3b4ff66f1bdaa8ab2785.

**C029 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C029-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(GrokCredentialProbeDispatcherTests*)|(GrokDelegateDispatchTests*)|(GrokNativeSessionResumeTests*)|(GrokStartupReadyOrderingTests*)/*' --report-trx --report-trx-filename C029-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C029 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C029/C029-a1.trx; SHA-256 5d4ccd4052a559462dbb718988aa6fe0405d250da379aaece057a9e381ea94eb.

**C030 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C030-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.ApiKeys/LaunchEnvLayersIntegrationTests/*' --report-trx --report-trx-filename C030-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C030 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C030/C030-a1.trx; SHA-256 8e23a2369dc672145799a1c354b82612bba714f004ab354bac4445ca4e96830b.

**C031 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C031-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(RunnerClaudeFinalLaunchArgvTests*)|(TaskLaunchTokenBoundaryTests*)|(UnixDelegateLaunchArgvTests*)/*' --report-trx --report-trx-filename C031-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C031 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C031/C031-a1.trx; SHA-256 e396d0a9214f37826ce16c8f52619a181af3b70fbdc3fbb6d7d21876038da7d8.

**C084 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C084-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/DelegationDispatchRecoveryBoundaryTests/(C1161_Dead_session_grace_is_monotonic*)|(C1161_Owner_names_the_monotonic_grace*)|(C1150_Pre_input_refusal_keeps_the_resume_failure_path*)|(C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_local_command*)|(C1150_Pre_input_refusal_keeps_the_resume_failure_path_for_a_transport_refusal*)' --report-trx --report-trx-filename C084-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C084 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C084/C084-a1.trx; SHA-256 0f17f36c592c91812e8b02d4bc32c903a7b229a9f134342997208369f5a17431.

**C064 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C064-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(CapacityAdmissionLifecycleTests*)|(CapacityObsoleteQueueTests*)|(CapacityRecoveryAttentionTests*)|(CapacityRecoveryTaskTests*)/*' --report-trx --report-trx-filename C064-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C064 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C064/C064-a1.trx; SHA-256 b302118bd3a7e018e4e239ffa733b8708ac3a5076e74c21fd24112d4b390fc05.

**C054 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C054-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/(C527_equals_separator_delivers_exact_parent_receipt*)|(C527_successful_empty_history_preserves_recovery_until_parent_receipt*)|(C527_retry_prerequisite_failure_preserves_commit_until_complete_parent_receipt*)|(C527_post_commit_inspection_failure_defers_settlement_until_complete_receipt*)|(C527_policy_flip_after_commit_save_failure_recovers_exact_parent_receipt*)|(C527_lost_history_and_repository_preserve_durable_recovery_until_parent_receipt*)|(C527_recovery_obligation_save_failure_prevents_git_mutation*)|(C527_literal_footprint_reaches_complete_parent_receipt_without_matching_neighbor*)|(C527_branch_movement_after_failed_save_recovers_original_parent_receipt*)|(C527_all_selected_reverted_delivers_no_op_without_Commit_child*)|(C527_child_audit_distinguishes_ignore_exceptions_in_complete_parent_receipt*)|(C527_repeated_report_selects_this_settlements_notification*)|(C527_unavailable_child_audit_is_reported_to_parent*)|(C527_completion_outbox_recovers_complete_receipt*)|(C527_partial_or_incidental_task_commit_does_not_hide_dirty_work*)|(C527_recovered_settlement_reports_new_dirty_work_without_committing_it*)|(C527_spawned_child_audit_compares_independent_upstream_baselines*)|(C527_obligation_free_history_search_failure_settles_once_with_an_uncommitted_note*)|(C527_obligation_free_history_search_failure_on_a_clean_tree_still_reports_landed*)|(C527_obligation_free_repository_inspection_failure_settles_once*)|(C527_obligation_free_search_and_status_failure_refuses_the_commit_once*)|(C527_resolved_no_commit_obligation_survives_a_failed_search_on_the_rehand*)|(C527_a_worktree_settle_with_a_pending_gated_receipt_still_reports_merged*)|(C527_selected_revert_delivers_exact_parent_receipt*)|(C527_tier1_commits_the_transcript_footprint_and_delivers_committed_to_the_parent*)|(C527_residual_dirty_path_outside_the_footprint_is_left_and_named*)|(C527_report_named_path_is_footprint_when_the_transcript_is_empty*)|(C527_notebook_path_edits_count_as_footprint*)|(C527_unattributable_dirty_tree_spawns_a_commit_child_routed_by_the_card_pin*)|(C527_commit_child_falls_back_to_role_policy_with_a_warning_when_no_pin_service*)|(C527_NoCommit_task_leaves_the_tree_with_the_no_commit_header*)|(C527_project_off_inherited_skips_with_todays_warning*)|(C527_task_Always_beats_project_off*)|(C527_Agent_policy_skips_tier1_and_spawns_the_child*)|(C527_global_off_with_project_null_skips*)|(C527_deleted_gitignore_refuses_spawns_the_child_and_warns*)|(C527_held_lease_is_RepositoryBusy_and_spawns_the_child*)|(C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr*)|(C527_commit_child_audit_flags_an_ignored_path_committed_outside_the_gate*)|(C527_commit_child_audit_flags_a_commit_without_the_gate_trailer*)|(C527_commit_child_audit_flags_upstream_movement*)|(C527_task_cap_leaves_the_header_and_warning*)|(C527_gate_refusal_with_no_child_possible_renders_commit_refused*)|(C527_tracked_then_ignored_path_refuses_the_whole_footprint*)|(C527_foreign_staged_path_survives_tier1_untouched*)|(C527_busy_parent_receives_the_committed_note_on_its_turn_end*)|(C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit*)|(C527_ineligible_tasks_never_run_the_hook*)|(C527_edits_before_dispatch_are_not_in_the_footprint*)|(C527_more_than_twenty_dirty_paths_are_all_attributed_from_the_transcript*)|(C544_shared_commit_on_settle_mints_one_task_completion*)|(C547_commit_child_audit_accepts_a_gated_commit_under_each_separator*)|(C547_commit_child_audit_flags_a_body_prose_mention_without_a_trailer_block*)|(C547_commit_child_audit_read_failure_is_unavailable_not_outside_the_gate*)|(C547_overdue_sweep_past_the_hold_abandons_the_obligation_and_the_parent_hears_it*)|(C547_overdue_sweep_holds_a_settled_gated_commit_until_the_rehand_records_it*)|(C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds*)|(C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending*)|(C547_an_unresolved_obligation_from_another_digest_does_not_hold_this_settlement*)|(C547_an_abandoned_obligation_is_invisible_to_a_later_same_digest_settlement*)|(C714_Grok_batched_replay_uses_report_response*)|(C714_Claude_ack_after_report_uses_report_response*)|(C714_Claude_notification_final_report_still_settles*)|(C714_Codex_final_answer_uses_turn_id*)|(C714_Parent_receives_selected_report_once*)|(C714_Grok_repeated_housekeeping_is_skipped*)|(C714_Grok_rules_then_only_completion_cannot_settle*)|(C714_Genuine_unmarked_turn_is_barrier*)|(C714_Grok_unmarked_prompt_before_reminder_is_barrier*)|(C714_Grok_unmarked_queued_prompt_before_reminder_is_barrier*)|(C714_Claude_unmarked_prompt_before_notification_is_barrier*)|(C714_Claude_auto_compaction_continuation_settles*)|(C714_Codex_unmarked_user_turn_is_barrier*)|(C714_Current_report_is_chosen_after_old_task_history*)|(C714_Predispatch_current_marker_is_rejected*)|(C714_Old_task_marker_is_never_current*)|(C714_Reply_watermark_prevents_stale_report*)|(C714_Grok_selected_report_waits_for_own_text*)|(C714_Late_report_text_after_reminder_keeps_own_api_id*)|(C714_Selected_error_or_cancel_does_not_fall_back*)|(C1499_a_release_the_runner_answers_exited_then_not_found_is_a_normal_release*)|(C1499_a_release_that_is_not_confirmed_still_records_the_Error*)|(C1706_a_reply_merge_conflict_in_eighty_long_names_writes_a_bounded_conflicted_detail*)|(C1706_a_reply_merge_conflict_at_the_task_cap_writes_a_bounded_cap_header*)|(C1710_a_read_only_report_naming_a_directory_of_long_unicode_names_writes_a_bounded_valid_warning*)|(C788_NoChangeFutureDispatchCleansUp*)|(Internal_conversion_settles_with_usage_but_without_sources_or_follow_up*)|(Deferred_is_durable_and_releases_runtime*)|(a_failed_api_error_task_names_session_liveness*)|(releasing_a_shared_task_whose_session_is_mid_turn_pools_it_and_warns*)|(a_transient_stub_with_a_later_prompt_does_not_fail_the_task*)|(a_transient_stub_with_a_later_queued_prompt_does_not_fail_the_task*)|(a_needs_human_stub_with_a_later_prompt_does_not_fail_the_task*)|(the_skip_arm_records_the_death_once_on_the_timeline*)|(a_housekeeping_record_after_the_stub_is_not_a_resume*)|(the_resumed_turns_marked_report_settles_the_task*)|(a_second_stub_on_the_resumed_turn_defers_on_its_own_row*)|(an_unmarked_resumed_turn_is_still_uncorrelated*)|(Phone_parent_preserves_worker_table_and_stage_handoff*)|(Phone_note_waits_for_a_busy_parent_and_lands_on_its_turn_end*)|(Phone_note_survives_a_failed_delivery_and_is_redelivered_exactly_once*)|(Grok_rules_refresh_with_task_marker_neither_settles_nor_nudges*)|(a_marked_turn_settles_the_task_and_stores_the_report_verbatim*)|(an_unmarked_turn_leaves_the_task_running*)|(another_tasks_marker_does_not_settle_this_task*)|(a_cancelled_turn_end_does_not_settle_the_task_and_says_so*)|(a_prior_internal_continue_does_not_make_a_cancelled_turn_a_report*)|(the_interrupted_warning_is_written_once_per_boundary*)|(the_next_end_turn_after_a_cancel_settles_normally*)|(an_end_turn_without_the_closing_line_is_nudged_once_not_settled*)|(a_prior_internal_continue_does_not_turn_unmarked_narration_into_completion*)|(a_narrow_policy_does_not_infer_design_approval_from_report_prose*)|(the_first_unmarked_end_notifies_the_parent_as_well_as_nudging_the_child*)|(the_waiting_note_names_authority_only_when_it_is_set*)|(block_unmarked_waiting_then_answer_resumes_the_same_session*)|(unmarked_waiting_answer_then_unmarked_turns_settle_with_the_new_report*)|(the_nudged_delegates_marked_reply_settles_with_marked_evidence*)|(a_second_unmarked_end_turn_settles_as_unmarked_after_nudge*)|(a_specialist_role_task_is_never_nudged*)|(a_specialist_role_looks_stuck_blocked_token_settles_succeeded_exempt*)|(a_specialist_role_trailing_question_settles_succeeded_exempt*)|(a_specialist_role_failed_token_still_fails_the_task*)' --report-trx --report-trx-filename C054-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C054 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C054/C054-a1.trx; SHA-256 8c9dc829c0246fb241cf80e0909326009673874c79f688a0c0f6adb03e4760ab.

**C104 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C104-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(PendingNoteWithholdTests*)|(PendingSettlementDeliveryTests*)/*' --report-trx --report-trx-filename C104-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C104 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C104/C104-a1.trx; SHA-256 bc942a8640ccd47690399e1313152778872ed8ab92bd1d55d3707146790bc8bb.

**C067 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C067-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(ChannelBridgeTests*)|(ChannelInboundRecoveryTests*)|(ChannelIngressIncidentTests*)/*' --report-trx --report-trx-filename C067-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C067 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C067/C067-a1.trx; SHA-256 e9fb39d6331c1a1ae86406f6099d41ae19a34e26bd4c49e57b3ac25d728757e4.

**C090 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C090-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(DispatchBaseNotificationTests*)|(DispatchHeldAttentionTests*)/*' --report-trx --report-trx-filename C090-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C090 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C090/C090-a1.trx; SHA-256 e3832380800fa3b6d42ae83a8537ccdfa4406fa66ac20aa266728f8e21a46944.

**C115 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C115-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/ReceiptFailureDeliveryTests/*' --report-trx --report-trx-filename C115-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C115 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C115/C115-a1.trx; SHA-256 3e6665cb73ff39bddd8733c9235e52fad106225a885f873f9e7d250fe4271be3.

**C121 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C121-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(RoutingExhaustedPlacementTests*)|(RoutingPinCandidateDispatchTests*)|(RoutingPinDispatcherTests*)/*' --report-trx --report-trx-filename C121-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C121 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C121/C121-a1.trx; SHA-256 636d97d497375a820afdc3ec4f10c84eb03a3b2dd235b2d48a386e69ebb0c8d2.

**C034 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C034-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(AgentTaskCheckInterpreterTests*)|(AgentTaskCheckScheduleTests*)|(AgentTaskCheckSweepTests*)|(AgentTaskCommitEndpointTests*)|(AgentTaskDeadSessionReconciliationTests*)|(AgentTaskDispatchFailureTests*)/*' --report-trx --report-trx-filename C034-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C034 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C034/C034-a1.trx; SHA-256 7fb698c322fe83f61ac07a6f481b31096190ecb3bca27f62bff89a3d9cbeb1d7.

**C068 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C068-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(CheckCompactionAttentionTests*)|(CheckCompactionReceiptTests*)|(CheckNoteDeliveryHandoffTests*)/*' --report-trx --report-trx-filename C068-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C068 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C068/C068-a1.trx; SHA-256 2204944209a06090da05890e2860547c6e2815a97c4d22013b2e4c3a538a80b0.

**C075 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C075-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(DelegationBriefRecoveryTests*)|(DelegationScopeHoldTests*)/*' --report-trx --report-trx-filename C075-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C075 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C075/C075-a1.trx; SHA-256 e81715c8e6a573f245750bf94e4f23c692ddd9f7b0831cf3a86637e178dc1590.

**C114 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C114-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/ReadyGateFailureNoticeDeliveryTests/*' --report-trx --report-trx-filename C114-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C114 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C114/C114-a1.trx; SHA-256 e9b129b370c04387fb460574be8cc1df398da21ebdf80e3b86a839ac4c78f60f.

**C120 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C120-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/RolePolicyToolBundleDispatchTests/*' --report-trx --report-trx-filename C120-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C120 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C120/C120-a1.trx; SHA-256 05a9a844a7b56c288294c77702f1cda322c6a6fd151fd7a74200d22fc530b241.

**C132 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C132-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/*' --report-trx --report-trx-filename C132-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C132 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C132/C132-a1.trx; SHA-256 c720a4d6a747db54e4b97d948a68b08fbe5c36af44faa1a60db125fd29d2cca3.

**C069 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C069-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/ClosingReportNudgeLifecycleTests/*' --report-trx --report-trx-filename C069-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C069 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C069/C069-a1.trx; SHA-256 da962eb8da1031261da2151e59f02e2c7a6068859c240d93257039ecf16a0796.

**C099 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C099-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/MatrixChildReceiptTests/*' --report-trx --report-trx-filename C099-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C099 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C099/C099-a1.trx; SHA-256 8022948d9f26a1cd672edad72734ff68b90ba65540977f49fd33ddde6734cd69.

**C105 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C105-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/PermissionWaitTests/*' --report-trx --report-trx-filename C105-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C105 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C105/C105-a1.trx; SHA-256 557ac3a66d2232874c0202e11bb7da33a6c8cee0728ffd5440a0fdbe9820a867.

**C134 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C134-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/(WorktreeDefaultCallerTests*)|(WorktreeRetirementRaceTests*)/*' --report-trx --report-trx-filename C134-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C134 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C134/C134-a1.trx; SHA-256 e88df5eade5c7d5bf3b78418a50aeb3cd95075a8a345acdfcc29fc8acb97d4f7.

**C074 a1**

```sh
cd /tmp/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-C074-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/DelegateBranchDeliveryTests/*' --report-trx --report-trx-filename C074-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C074 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/C074/C074-a1.trx; SHA-256 913185c74fa478c9eb7e2e26f416d0da16331f99d39716a80a7bfad3e794e0f8.

**R008 a1**

```sh
cd /work/worktrees/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-R008-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/VerificationRoundDeliveryTests/C544_ReportRegeneratedFromSnapshot' --report-trx --report-trx-filename R008-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/R008 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/R008/R008-a1.trx; SHA-256 a5c7232a947555f6d1842552d292407d920b07964508ffb24a02674ac0f88c50.

**R009 a1**

```sh
cd /work/worktrees/task-15406ecc-coverage2
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 15406-cont2-R009-a1 -SlotWaitMinutes 45 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/Antiphon.Tests.Application/WorktreeDefaultCallerTests/SpecialistFactoriesStayOnSeat' --report-trx --report-trx-filename R009-a1.trx --results-directory /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/R009 --output Detailed
```

TRX: /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/R009/R009-a1.trx; SHA-256 6625b13dac7b5b604d57749c7b993de63bf15553b93997332b0b4d03ca4ca2b0.

The two large parameter matrices (active source claims and real dispatch admission) were split into individual UID cases using unchanged committed discovery-fixture identities; a zero selection is never credited. A method with only some parameter variants observed remains partial. Slot waiting is outside the sub-ten-minute execution cap and is included in wall time. The owned measurement helper examines all wrapper threads for the test child so PowerShell worker-thread spawning cannot evade that cap. No framework timeout, assertion or retry was widened. Generated receipts/logs/phase files and drivers remain ignored under /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/.

### Earlier Linux fixture families: Windows rc relevance

| Card | Windows rc relevance |
|---|---|
| CARD-1927 | Real CLI child auth/environment inheritance is portable; caller reports desktop CLI logged in. An inherited OAuth variable can defeat the temporary-config logged-out witness. No desktop credential or Windows execution was inspected here. |
| CARD-1928 | CrashedOwnerLeaseReclaimed starts the same owned PowerShell child on Windows; child-host loader failures can affect it. ChangedAndSymlinkSocketsAreNotReclaimed returns immediately on Windows, excluding that failed Linux path. Specific inner loader cause remains unproved. |
| CARD-1929..1932 | NativeLinuxOnly class; excluded from Windows. |
| CARD-1934 | C519 broker/resource-reaper startup prerequisites can fail on Windows when Docker integration tests are admitted; the Linux failure does not prove a Windows red. |
| CARD-1935 | C699 completion recovery is portable; repeated missing stamps can affect Windows. Product versus fixture attribution remains unresolved. |

Caller reports C1196 is repaired on master, C1029 is in Merge227, and CARD-1924/CARD-1926 plus the g90 trio are assigned to Debug9c1feeba. Those are coordination statements, not newly executed green receipts. Their already-observed cases were excluded here. No live Windows run, desktop, host restart, deployment or Docker volume was touched. Live numeric chunk mapping remains unknown beyond the caller-supplied mappings; source golden boundary IDs are not substituted for live run indices.

### Exact classes still without any result at the stop

| Project | Exact class | Rerun class filter |
|---|---|---|
| Antiphon.E2E | `Antiphon.E2E.OutputDistillationApplyCanaryTests` | `/*/Antiphon.E2E/OutputDistillationApplyCanaryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AbandonedLandingReconcilerTests` | `/*/Antiphon.Tests.Application/AbandonedLandingReconcilerTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentAttachHerdrTests` | `/*/Antiphon.Tests.Application/AgentAttachHerdrTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentChannelServiceIntegrationTests` | `/*/Antiphon.Tests.Application/AgentChannelServiceIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentReportStoreTests` | `/*/Antiphon.Tests.Application/AgentReportStoreTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentStartRecoveryTests` | `/*/Antiphon.Tests.Application/AgentStartRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentSupervisionTests` | `/*/Antiphon.Tests.Application/AgentSupervisionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskAnswerTests` | `/*/Antiphon.Tests.Application/AgentTaskAnswerTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskDispatchBaseGuardTests` | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskDispatcherWiringTests` | `/*/Antiphon.Tests.Application/AgentTaskDispatcherWiringTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskInternalDecisionLifecycleTests` | `/*/Antiphon.Tests.Application/AgentTaskInternalDecisionLifecycleTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandActiveSourceClaimTests` | `/*/Antiphon.Tests.Application/AgentTaskLandActiveSourceClaimTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandAdmissionControlledTests` | `/*/Antiphon.Tests.Application/AgentTaskLandAdmissionControlledTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandAdmissionTests` | `/*/Antiphon.Tests.Application/AgentTaskLandAdmissionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandEnqueueTests` | `/*/Antiphon.Tests.Application/AgentTaskLandEnqueueTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandFailureDiagnosticTests` | `/*/Antiphon.Tests.Application/AgentTaskLandFailureDiagnosticTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandHoldVisibilityTests` | `/*/Antiphon.Tests.Application/AgentTaskLandHoldVisibilityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandMonitoringTests` | `/*/Antiphon.Tests.Application/AgentTaskLandMonitoringTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandNotificationParkFollowUpTests` | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationParkFollowUpTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandNotificationRescanTests` | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationRescanTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandPersistenceFailureTests` | `/*/Antiphon.Tests.Application/AgentTaskLandPersistenceFailureTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandPublicationTests` | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandQueuedReceiptTests` | `/*/Antiphon.Tests.Application/AgentTaskLandQueuedReceiptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandReceiptTests` | `/*/Antiphon.Tests.Application/AgentTaskLandReceiptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandReceiptWatermarkSafetyTests` | `/*/Antiphon.Tests.Application/AgentTaskLandReceiptWatermarkSafetyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskOverdueDeadlineTests` | `/*/Antiphon.Tests.Application/AgentTaskOverdueDeadlineTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskPoolTests` | `/*/Antiphon.Tests.Application/AgentTaskPoolTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskReplyOverlayTests` | `/*/Antiphon.Tests.Application/AgentTaskReplyOverlayTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskReuseEnqueueTests` | `/*/Antiphon.Tests.Application/AgentTaskReuseEnqueueTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskServiceIntegrationTests` | `/*/Antiphon.Tests.Application/AgentTaskServiceIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskSettlementRaceTests` | `/*/Antiphon.Tests.Application/AgentTaskSettlementRaceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskStallEscalationTests` | `/*/Antiphon.Tests.Application/AgentTaskStallEscalationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskStandingAgentDispatchTests` | `/*/Antiphon.Tests.Application/AgentTaskStandingAgentDispatchTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskWorktreeLockOutcomeTests` | `/*/Antiphon.Tests.Application/AgentTaskWorktreeLockOutcomeTests/*` |
| Antiphon.Tests | `Antiphon.Tests.AgentTui.AgentTuiSecretProtectorTests` | `/*/Antiphon.Tests.AgentTui/AgentTuiSecretProtectorTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.AnchoredPathDeleteWitnessTests` | `/*/Antiphon.Tests.Checkpoints/AnchoredPathDeleteWitnessTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AwayDigestNotifierTests` | `/*/Antiphon.Tests.Application/AwayDigestNotifierTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AwayDigestProjectionTests` | `/*/Antiphon.Tests.Application/AwayDigestProjectionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedReplyReportRetentionTests` | `/*/Antiphon.Tests.Application/BlockedReplyReportRetentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedSeatEscalationNotifierTests` | `/*/Antiphon.Tests.Application/BlockedSeatEscalationNotifierTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedTaskNotifierTests` | `/*/Antiphon.Tests.Application/BlockedTaskNotifierTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedTaskParkDeliveryTests` | `/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedTaskParkProjectionTests` | `/*/Antiphon.Tests.Application/BlockedTaskParkProjectionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedTaskParkReclaimTests` | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedTaskParkReleaseTests` | `/*/Antiphon.Tests.Application/BlockedTaskParkReleaseTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BlockedTaskSyncRecoveryTests` | `/*/Antiphon.Tests.Application/BlockedTaskSyncRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BoardServiceIntegrationTests` | `/*/Antiphon.Tests.Application/BoardServiceIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BootLivenessProbeScopeTests` | `/*/Antiphon.Tests.Application/BootLivenessProbeScopeTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BootStallAttentionTests` | `/*/Antiphon.Tests.Application/BootStallAttentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BootStallDetectionTests` | `/*/Antiphon.Tests.Application/BootStallDetectionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BootStallDocumentationTests` | `/*/Antiphon.Tests.Application/BootStallDocumentationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.BootWedgeRelaunchParityTests` | `/*/Antiphon.Tests.Application/BootWedgeRelaunchParityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0747LandReceiptReplayTests` | `/*/Antiphon.Tests.Application/C0747LandReceiptReplayTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0747QueueRowExpiryTests` | `/*/Antiphon.Tests.Application/C0747QueueRowExpiryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824DesktopLegacyReleaseSwitchTests` | `/*/Antiphon.Tests.Application/C0824DesktopLegacyReleaseSwitchTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824RefOnlyCleanupTests` | `/*/Antiphon.Tests.Application/C0824RefOnlyCleanupTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824RefOnlyDebtTests` | `/*/Antiphon.Tests.Application/C0824RefOnlyDebtTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824RefOnlyLandingTests` | `/*/Antiphon.Tests.Application/C0824RefOnlyLandingTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824RefOnlyReadTests` | `/*/Antiphon.Tests.Application/C0824RefOnlyReadTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824RefOnlyReceiptTests` | `/*/Antiphon.Tests.Application/C0824RefOnlyReceiptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824RefOnlySyncTests` | `/*/Antiphon.Tests.Application/C0824RefOnlySyncTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824WorkspaceSlotServerTests` | `/*/Antiphon.Tests.Application/C0824WorkspaceSlotServerTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.C1307CliCancellationTests` | `/*/Antiphon.Tests.Checkpoints/C1307CliCancellationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.C1307ToolRemovalTests` | `/*/Antiphon.Tests.Infrastructure/C1307ToolRemovalTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C1514HostLoadHistoryTests` | `/*/Antiphon.Tests.Application/C1514HostLoadHistoryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C1677DiskAlarmTests` | `/*/Antiphon.Tests.Application/C1677DiskAlarmTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CapacityRecoverySupervisionTests` | `/*/Antiphon.Tests.Application/CapacityRecoverySupervisionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.Card1619ExitConsumerTests` | `/*/Antiphon.Tests.Application/Card1619ExitConsumerTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardClosedAttentionTests` | `/*/Antiphon.Tests.Application/CardClosedAttentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardCorrectionIntegrationTests` | `/*/Antiphon.Tests.Application/CardCorrectionIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardFilePrivacyBoundaryAcceptanceTests` | `/*/Antiphon.Tests.Application/CardFilePrivacyBoundaryAcceptanceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardFilePrivacyConcurrencyTests` | `/*/Antiphon.Tests.Application/CardFilePrivacyConcurrencyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardFilePrivacyRecoveryTests` | `/*/Antiphon.Tests.Application/CardFilePrivacyRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardPrivateNotesBoundaryTests` | `/*/Antiphon.Tests.Application/CardPrivateNotesBoundaryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardReviewServiceIntegrationTests` | `/*/Antiphon.Tests.Application/CardReviewServiceIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardSpawnModelArgumentTests` | `/*/Antiphon.Tests.Application/CardSpawnModelArgumentTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardVerificationPolicyTests` | `/*/Antiphon.Tests.Application/CardVerificationPolicyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardWorkTransitionServiceTests` | `/*/Antiphon.Tests.Application/CardWorkTransitionServiceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CardWorktreeMirrorCleanupTests` | `/*/Antiphon.Tests.Application/CardWorktreeMirrorCleanupTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ChannelBatchingTests` | `/*/Antiphon.Tests.Application/ChannelBatchingTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CheckCompactionAutomaticRestartTests` | `/*/Antiphon.Tests.Application/CheckCompactionAutomaticRestartTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CheckCompactionCrashTests` | `/*/Antiphon.Tests.Application/CheckCompactionCrashTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointExecutorLogTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointExecutorLogTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointSourceExecutionTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointSourceStateTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointSourceStateTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointStatePublicationTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointStatePublicationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointTaskOwnershipTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointTaskOwnershipTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointTempScopeTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointTempScopeTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointTerminalReceiptTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointTerminalReceiptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointToolCopyCleanupTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointToolCopyCleanupTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.CompletionNoteDurableSweepTests` | `/*/Antiphon.Tests.Application/CompletionNoteDurableSweepTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ComplexityAttentionTests` | `/*/Antiphon.Tests.Application/ComplexityAttentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ComplexityDispatcherTests` | `/*/Antiphon.Tests.Application/ComplexityDispatcherTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ContextCompactionSweepTests` | `/*/Antiphon.Tests.Application/ContextCompactionSweepTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DataRetentionServiceTests` | `/*/Antiphon.Tests.Application/DataRetentionServiceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DecisionCardNotifierTests` | `/*/Antiphon.Tests.Application/DecisionCardNotifierTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DefaultRunnerPinTests` | `/*/Antiphon.Tests.Application/DefaultRunnerPinTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DefaultRunnerRerouteTests` | `/*/Antiphon.Tests.Application/DefaultRunnerRerouteTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DelegateCheckProbeTests` | `/*/Antiphon.Tests.Application/DelegateCheckProbeTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Scripts.DerivedImpactIndexTests` | `/*/Antiphon.Tests.Scripts/DerivedImpactIndexTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DiagnosticsBundleServiceTests` | `/*/Antiphon.Tests.Application/DiagnosticsBundleServiceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DialogDeliveryAdmissionTests` | `/*/Antiphon.Tests.Application/DialogDeliveryAdmissionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.TestHelpers.DirectoryLinkFixtureWindowsTests` | `/*/Antiphon.Tests.TestHelpers/DirectoryLinkFixtureWindowsTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DispatchBaseInspectionDiagnosticsTests` | `/*/Antiphon.Tests.Application/DispatchBaseInspectionDiagnosticsTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DispatchConcurrencyDispatchTests` | `/*/Antiphon.Tests.Application/DispatchConcurrencyDispatchTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DispatcherSiblingInspectionSchedulingTests` | `/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DispatcherSweepLifetimeTests` | `/*/Antiphon.Tests.Application/DispatcherSweepLifetimeTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DispatchHoldVisibilityTests` | `/*/Antiphon.Tests.Application/DispatchHoldVisibilityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.DockerDeliveryDatabaseTests` | `/*/Antiphon.Tests.Infrastructure/DockerDeliveryDatabaseTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.DurableRunnerSpillReceiptTests` | `/*/Antiphon.Tests.Application/DurableRunnerSpillReceiptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ExpectationDirectDeliveryTests` | `/*/Antiphon.Tests.Application/ExpectationDirectDeliveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ExpectationEscalationTests` | `/*/Antiphon.Tests.Application/ExpectationEscalationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ExpectationNoteDebtTests` | `/*/Antiphon.Tests.Application/ExpectationNoteDebtTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ExpectationObservationAdapterTests` | `/*/Antiphon.Tests.Application/ExpectationObservationAdapterTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ExpectationResponseTests` | `/*/Antiphon.Tests.Application/ExpectationResponseTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.FileUseCaptureTests` | `/*/Antiphon.Tests.Infrastructure/FileUseCaptureTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.GhostSeatReleaseTests` | `/*/Antiphon.Tests.Application/GhostSeatReleaseTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.HerdrAlwaysOnChannelParityTests` | `/*/Antiphon.Tests.Application/HerdrAlwaysOnChannelParityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.HerdrPaneDisposalApplicationTests` | `/*/Antiphon.Tests.Application/HerdrPaneDisposalApplicationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.HerdrStatusDisagreementTests` | `/*/Antiphon.Tests.Application/HerdrStatusDisagreementTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.HerdrSupervisionAttentionTests` | `/*/Antiphon.Tests.Application/HerdrSupervisionAttentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.HostCleanupCheckpointAuthorityTests` | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointAuthorityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.HostCleanupCheckpointCompatibilityTests` | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointCompatibilityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.HostCleanupCheckpointCustodyTests` | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointCustodyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.HostCleanupWorktreeInventoryTests` | `/*/Antiphon.Tests.Application/HostCleanupWorktreeInventoryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.InterimVerificationPolicyTests` | `/*/Antiphon.Tests.Application/InterimVerificationPolicyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Api.KeepSixReminderTests` | `/*/Antiphon.Tests.Api/KeepSixReminderTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.LandEvidenceWarningDeliveryTests` | `/*/Antiphon.Tests.Application/LandEvidenceWarningDeliveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.LandingGitTests` | `/*/Antiphon.Tests.Infrastructure/LandingGitTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests` | `/*/Antiphon.Tests.Infrastructure/LandingRemovalPolicyControlTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.LandReceiptScanCacheTests` | `/*/Antiphon.Tests.Application/LandReceiptScanCacheTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.LandRequestWriteDiagnosticTests` | `/*/Antiphon.Tests.Application/LandRequestWriteDiagnosticTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.LinuxOwnedGitProcessTests` | `/*/Antiphon.Tests.Infrastructure/LinuxOwnedGitProcessTests/*` |
| Antiphon.Tests | `Antiphon.Tests.TestHelpers.LiveFixtureTextTests` | `/*/Antiphon.Tests.TestHelpers/LiveFixtureTextTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.LogRetentionTests` | `/*/Antiphon.Tests.Infrastructure/LogRetentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.MatrixDefaultComplexityCreateTests` | `/*/Antiphon.Tests.Application/MatrixDefaultComplexityCreateTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.MidTurnInterruptDocumentationTests` | `/*/Antiphon.Tests.Application/MidTurnInterruptDocumentationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.MidTurnInterruptPolicyTests` | `/*/Antiphon.Tests.Application/MidTurnInterruptPolicyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ModelAvailabilityDispatcherTests` | `/*/Antiphon.Tests.Application/ModelAvailabilityDispatcherTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OrchestratorInstructionsRefreshTests` | `/*/Antiphon.Tests.Application/OrchestratorInstructionsRefreshTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OrchestratorServiceIntegrationTests` | `/*/Antiphon.Tests.Application/OrchestratorServiceIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OrchestratorStateProjectionTests` | `/*/Antiphon.Tests.Application/OrchestratorStateProjectionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OrchestratorTrackerCadenceTests` | `/*/Antiphon.Tests.Application/OrchestratorTrackerCadenceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OutputDistillationApplyRaceTests` | `/*/Antiphon.Tests.Application/OutputDistillationApplyRaceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OutputDistillationCleanupTests` | `/*/Antiphon.Tests.Application/OutputDistillationCleanupTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OutputDistillationDeliveryTests` | `/*/Antiphon.Tests.Application/OutputDistillationDeliveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OutputDistillationDispatchTests` | `/*/Antiphon.Tests.Application/OutputDistillationDispatchTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OutputDistillationProducerTests` | `/*/Antiphon.Tests.Application/OutputDistillationProducerTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ParkedMessageSweepServiceTests` | `/*/Antiphon.Tests.Application/ParkedMessageSweepServiceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PinnedAgentKindTests` | `/*/Antiphon.Tests.Application/PinnedAgentKindTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PipelineSpeedIntegrationTests` | `/*/Antiphon.Tests.Application/PipelineSpeedIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PolicyRefreshServiceTests` | `/*/Antiphon.Tests.Application/PolicyRefreshServiceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PoolClaudeEffortTests` | `/*/Antiphon.Tests.Application/PoolClaudeEffortTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PoolProviderProjectionStampTests` | `/*/Antiphon.Tests.Application/PoolProviderProjectionStampTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PostLandMutationWorkflowTests` | `/*/Antiphon.Tests.Application/PostLandMutationWorkflowTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PostLandMutationWorktreeTests` | `/*/Antiphon.Tests.Application/PostLandMutationWorktreeTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.PreDispatchFailureDeliveryTests` | `/*/Antiphon.Tests.Application/PreDispatchFailureDeliveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.ProcessSpawnLimitTests` | `/*/Antiphon.Tests/ProcessSpawnLimitTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ProviderQuotaRefusalAcceptanceTests` | `/*/Antiphon.Tests.Application/ProviderQuotaRefusalAcceptanceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.QuotaEvidenceWireTests` | `/*/Antiphon.Tests.Application/QuotaEvidenceWireTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.RcBridgeProbePlatformTests` | `/*/Antiphon.Tests.Infrastructure/RcBridgeProbePlatformTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Scripts.RcQualificationWatcherScriptTests` | `/*/Antiphon.Tests.Scripts/RcQualificationWatcherScriptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ReceiptScanDestinationIntegrationTests` | `/*/Antiphon.Tests.Application/ReceiptScanDestinationIntegrationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ReceiptScanDestinationTests` | `/*/Antiphon.Tests.Application/ReceiptScanDestinationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RefocusBriefIdentityTests` | `/*/Antiphon.Tests.Application/RefocusBriefIdentityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Scripts.ReleaseImpactEnvelopeTests` | `/*/Antiphon.Tests.Scripts/ReleaseImpactEnvelopeTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.RepositoryMutationLeaseOwnerTests` | `/*/Antiphon.Tests.Infrastructure/RepositoryMutationLeaseOwnerTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests` | `/*/Antiphon.Tests.Infrastructure/RepositoryMutationLeaseTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RestartFailureClassificationTests` | `/*/Antiphon.Tests.Application/RestartFailureClassificationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ReviewLoopTests` | `/*/Antiphon.Tests.Application/ReviewLoopTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerAlarmAttentionTests` | `/*/Antiphon.Tests.Application/RunnerAlarmAttentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerAlarmDeliveryTests` | `/*/Antiphon.Tests.Application/RunnerAlarmDeliveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerAlarmNotifierTests` | `/*/Antiphon.Tests.Application/RunnerAlarmNotifierTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerBranchContractDocumentationTests` | `/*/Antiphon.Tests.Application/RunnerBranchContractDocumentationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.RunnerClaudeAdapterEffortPromptTests` | `/*/Antiphon.Tests.Agents/RunnerClaudeAdapterEffortPromptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.RunnerClaudeAdapterTrustPromptTests` | `/*/Antiphon.Tests.Agents/RunnerClaudeAdapterTrustPromptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.RunnerClaudeAdapterVerifiedPromptTests` | `/*/Antiphon.Tests.Agents/RunnerClaudeAdapterVerifiedPromptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerCodexCliEvidenceTests` | `/*/Antiphon.Tests.Application/RunnerCodexCliEvidenceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.RunnerGrokAdapterReadyTests` | `/*/Antiphon.Tests.Agents/RunnerGrokAdapterReadyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerGrokCredentialProbeDispatcherTests` | `/*/Antiphon.Tests.Application/RunnerGrokCredentialProbeDispatcherTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerReadSourcePinTests` | `/*/Antiphon.Tests.Application/RunnerReadSourcePinTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerSeatOrphanSweepTests` | `/*/Antiphon.Tests.Application/RunnerSeatOrphanSweepTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerSettlementSyncTests` | `/*/Antiphon.Tests.Application/RunnerSettlementSyncTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerSlotEndpointTests` | `/*/Antiphon.Tests.Application/RunnerSlotEndpointTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.RunnerStartupStaleObservationTests` | `/*/Antiphon.Tests.Agents/RunnerStartupStaleObservationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerSyncLeaseFairnessTests` | `/*/Antiphon.Tests.Application/RunnerSyncLeaseFairnessTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.RunnerTerminalSessionGenerationTests` | `/*/Antiphon.Tests.Agents/RunnerTerminalSessionGenerationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RuntimeActionClaimQueryTests` | `/*/Antiphon.Tests.Application/RuntimeActionClaimQueryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ScheduleCardActionTests` | `/*/Antiphon.Tests.Application/ScheduleCardActionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ScheduleSweepTests` | `/*/Antiphon.Tests.Application/ScheduleSweepTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ScopeDriftTests` | `/*/Antiphon.Tests.Application/ScopeDriftTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Scripts.ScriptHarnessProcessContractTests` | `/*/Antiphon.Tests.Scripts/ScriptHarnessProcessContractTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SeatOccupancyAttentionTests` | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionGenerationDeliveryOverlapTests` | `/*/Antiphon.Tests.Application/SessionGenerationDeliveryOverlapTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionHealthTests` | `/*/Antiphon.Tests.Application/SessionHealthTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionReconciliationServiceTests` | `/*/Antiphon.Tests.Application/SessionReconciliationServiceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRunnerAbsenceEvidenceDocumentationTests` | `/*/Antiphon.Tests.Application/SessionRunnerAbsenceEvidenceDocumentationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRunnerEventPumpTests` | `/*/Antiphon.Tests.Application/SessionRunnerEventPumpTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.SessionRunnerGenerationWireTests` | `/*/Antiphon.Tests.Agents/SessionRunnerGenerationWireTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests` | `/*/Antiphon.Tests.Application/SessionRunnerHeldEventExitMatrixTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRuntimeActionDeferralTests` | `/*/Antiphon.Tests.Application/SessionRuntimeActionDeferralTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRuntimeActionRecoveryTests` | `/*/Antiphon.Tests.Application/SessionRuntimeActionRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRuntimeActionRoutingTests` | `/*/Antiphon.Tests.Application/SessionRuntimeActionRoutingTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRuntimeActionTemporaryBlockTests` | `/*/Antiphon.Tests.Application/SessionRuntimeActionTemporaryBlockTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionRuntimeActionTests` | `/*/Antiphon.Tests.Application/SessionRuntimeActionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionStateCacheReadTests` | `/*/Antiphon.Tests.Application/SessionStateCacheReadTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionStateCommitTests` | `/*/Antiphon.Tests.Application/SessionStateCommitTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionStatePinQueryTests` | `/*/Antiphon.Tests.Application/SessionStatePinQueryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionStateWarmupTests` | `/*/Antiphon.Tests.Application/SessionStateWarmupTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SessionTerminationSourcePersistenceTests` | `/*/Antiphon.Tests.Application/SessionTerminationSourcePersistenceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SettlementSyncRecoveryTests` | `/*/Antiphon.Tests.Application/SettlementSyncRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SpecialistAttemptEvidenceTests` | `/*/Antiphon.Tests.Application/SpecialistAttemptEvidenceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SpecialistExecutionIdentityTests` | `/*/Antiphon.Tests.Application/SpecialistExecutionIdentityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SpecialistHealthAttentionTests` | `/*/Antiphon.Tests.Application/SpecialistHealthAttentionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SpecialistPublicationTests` | `/*/Antiphon.Tests.Application/SpecialistPublicationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SpecialistQualificationTests` | `/*/Antiphon.Tests.Application/SpecialistQualificationTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SpecialistStartIntentTests` | `/*/Antiphon.Tests.Application/SpecialistStartIntentTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.SpecialistToolPolicyTests` | `/*/Antiphon.Tests.Agents/SpecialistToolPolicyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.StartRefGitDirectoryCleanupTests` | `/*/Antiphon.Tests.Infrastructure/StartRefGitDirectoryCleanupTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.SubscriptionQuotaGateDispatchTests` | `/*/Antiphon.Tests.Application/SubscriptionQuotaGateDispatchTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TaskCeilingControlTests` | `/*/Antiphon.Tests.Application/TaskCeilingControlTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TaskPlatformDispatchTests` | `/*/Antiphon.Tests.Application/TaskPlatformDispatchTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TaskProgressPolicyTests` | `/*/Antiphon.Tests.Application/TaskProgressPolicyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TaskProgressStallSweepTests` | `/*/Antiphon.Tests.Application/TaskProgressStallSweepTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TaskSupervisionReceiptTests` | `/*/Antiphon.Tests.Application/TaskSupervisionReceiptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TaskSupervisionVisibilityTests` | `/*/Antiphon.Tests.Application/TaskSupervisionVisibilityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TerminalRunnerSeatReleaseTests` | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TerminalTaskResidueTests` | `/*/Antiphon.Tests.Application/TerminalTaskResidueTests/*` |
| Antiphon.Tests | `Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests` | `/*/Antiphon.Tests.TestHelpers/TestLaneCategoryGuardTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TranscriptBindingIncidentTests` | `/*/Antiphon.Tests.Application/TranscriptBindingIncidentTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TranscriptPromptSpanTests` | `/*/Antiphon.Tests.Application/TranscriptPromptSpanTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TranscriptWorkingStateQueryTests` | `/*/Antiphon.Tests.Application/TranscriptWorkingStateQueryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TurnEndGapRestartDeliveryTests` | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.UnsafeDeleteRecoveryTests` | `/*/Antiphon.Tests.Checkpoints/UnsafeDeleteRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.VerificationRoundSettlementTests` | `/*/Antiphon.Tests.Application/VerificationRoundSettlementTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Scripts.VolumeRetirementHostProofTests` | `/*/Antiphon.Tests.Scripts/VolumeRetirementHostProofTests/*` |
| Antiphon.Tests | `Antiphon.Tests.TestHelpers.WindowsDeliveryFixtureWitnessTests` | `/*/Antiphon.Tests.TestHelpers/WindowsDeliveryFixtureWitnessTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.WorkingTranscriptRecoveryTests` | `/*/Antiphon.Tests.Application/WorkingTranscriptRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.WorktreeBaseSelectionTests` | `/*/Antiphon.Tests.Application/WorktreeBaseSelectionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.WorktreeCleanupJournalTests` | `/*/Antiphon.Tests.Application/WorktreeCleanupJournalTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.WorktreeLandingCleanupRetryTests` | `/*/Antiphon.Tests.Application/WorktreeLandingCleanupRetryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.WorktreeLockDiagnosticsTests` | `/*/Antiphon.Tests.Infrastructure/WorktreeLockDiagnosticsTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests` | `/*/Antiphon.Tests.Infrastructure/WorktreeNoFollowDeleteTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.OwnedPtyFixtureCleanupTests` | `/*/Antiphon.Tests.Agents/OwnedPtyFixtureCleanupTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.TestOwnedPtyHostIdentityTests` | `/*/Antiphon.Tests.Agents/TestOwnedPtyHostIdentityTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Agents.TestOwnedPtyHostPolicyTests` | `/*/Antiphon.Tests.Agents/TestOwnedPtyHostPolicyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandContractEndpointTests` | `/*/Antiphon.Tests.Application/AgentTaskLandContractEndpointTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandHalfResetTests` | `/*/Antiphon.Tests.Application/AgentTaskLandHalfResetTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandNotificationPersistenceTests` | `/*/Antiphon.Tests.Application/AgentTaskLandNotificationPersistenceTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskLandRequestTests` | `/*/Antiphon.Tests.Application/AgentTaskLandRequestTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.C0824PublishedBranchDeletionTests` | `/*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.HerdrLabelFollowFlowTests` | `/*/Antiphon.Tests.Application/HerdrLabelFollowFlowTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.HerdrLabelFollowTests` | `/*/Antiphon.Tests.Application/HerdrLabelFollowTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.LandFailureDiagnosticTests` | `/*/Antiphon.Tests.Application/LandFailureDiagnosticTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.OutputDistillationAdmissionTests` | `/*/Antiphon.Tests.Application/OutputDistillationAdmissionTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ReviewEvidenceRecoveryTests` | `/*/Antiphon.Tests.Application/ReviewEvidenceRecoveryTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.ReviewEvidenceResettlementTests` | `/*/Antiphon.Tests.Application/ReviewEvidenceResettlementTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.RunnerTaskSettlementTests` | `/*/Antiphon.Tests.Application/RunnerTaskSettlementTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.TaskCompletionProgressPolicyTests` | `/*/Antiphon.Tests.Application/TaskCompletionProgressPolicyTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Application.VerificationRoundDispatchTests` | `/*/Antiphon.Tests.Application/VerificationRoundDispatchTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointEofStallTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointEofStallTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Checkpoints.CheckpointTimingHarnessTests` | `/*/Antiphon.Tests.Checkpoints/CheckpointTimingHarnessTests/*` |
| Antiphon.Tests | `Antiphon.Tests.Infrastructure.HostCleanupCheckpointReceiptTests` | `/*/Antiphon.Tests.Infrastructure/HostCleanupCheckpointReceiptTests/*` |
| Antiphon.Tests | `Antiphon.Tests.TestHelpers.DirectoryLinkFixtureTests` | `/*/Antiphon.Tests.TestHelpers/DirectoryLinkFixtureTests/*` |
| Antiphon.Tests | `Antiphon.Tests.TestHelpers.ReportBudgetWorkspacePortabilityTests` | `/*/Antiphon.Tests.TestHelpers/ReportBudgetWorkspacePortabilityTests/*` |

### Partial classes: exact remaining methods or argument rows

| Project | Exact class | Observed rows | Still-unselected methods |
|---|---|---|---|
| Antiphon.Tests | `Antiphon.Tests.Application.AgentTaskReplyIntegrationTests` | C054 | a_non_check_blocked_token_still_blocks_marked, continue_replays_standing_authority_on_a_question_block, continue_refuses_a_task_that_is_not_blocked, continue_refuses_a_merge_conflict, continue_refuses_when_there_is_no_authority, unmarked_waiting_parent_note_carries_the_structured_blocked_lines, a_marked_blocked_parent_note_uses_reason_marked_blocked, a_failed_verdict_fails_the_task_with_the_first_line_as_reason, the_completion_header_carries_report_marked, a_brief_that_lost_its_head_in_the_pty_still_settles_the_task, a_report_that_cannot_be_correlated_raises_an_incident, the_uncorrelated_incident_is_raised_once_not_once_per_turn, two_tasks_on_one_session_each_get_their_own_uncorrelated_incident, a_turn_with_no_assistant_text_yet_leaves_the_task_running, a_turn_end_whose_own_response_has_not_written_its_text_does_not_settle, the_final_messages_arrival_settles_the_task_with_the_report, a_turn_end_with_no_api_call_id_settles_as_it_always_did, a_response_that_never_writes_text_settles_after_the_grace_window, a_Codex_final_answer_with_the_turn_identity_settles_cleanly_without_a_warning, a_Codex_turn_with_no_final_answer_still_warns_after_the_grace_window, a_Codex_final_answer_that_arrives_after_its_turn_end_settles_cleanly, a_turn_with_no_text_at_all_at_grace_fails_the_task, the_final_message_missing_incident_is_raised_once_per_session, the_live_split_response_tail_settles_once_with_the_report, the_report_is_the_turn_ending_responses_own_text, a_response_split_over_several_text_blocks_is_joined_in_order, a_delegate_that_asks_a_question_comes_back_blocked_not_finished, a_report_under_the_ceiling_is_not_spilled_to_a_file, a_settled_report_stores_the_first_resolving_markdown_deliverable, a_custom_role_worktree_task_that_names_four_docs_writes_a_source_bundle, a_document_task_completion_note_ends_with_the_deliverable_attach_block, completed_sources_reach_the_parent_as_exact_inline_or_zip_attachments, completion_receipt_respects_legacy_renderer_keys_and_source_kill_switch, actual_completion_routes_source_bytes_directly_or_through_conversion, actual_task_done_turn_dispatches_implied_sources_by_channel_policy, a_report_with_no_resolving_markdown_path_leaves_no_deliverable_pointer, marking_a_task_read_is_idempotent_and_preserves_the_first_stamp, an_oversized_report_is_backstopped_to_a_file_by_the_server, a_spill_file_the_delegate_wrote_itself_is_used_as_is, the_completion_note_is_delivered_into_the_parents_session, a_five_kilobyte_report_spills_and_the_caller_gets_a_marked_excerpt, a_task_with_no_parent_session_settles_without_delivering_anywhere, token_spend_is_rolled_up_onto_the_task, a_stage_task_settlement_writes_a_delegate_row_with_copied_cost, a_stage_task_without_a_finding_line_is_unreported, a_task_without_stage_writes_no_outcome_row, the_three_input_counters_are_kept_apart_and_priced_apart, a_grok_turn_whose_last_segment_is_empty_defers_then_warns, grok_per_segment_ids_on_text_rows_do_not_double_count_turn_usage, a_grok_delegates_spend_is_priced_at_grok_rates_not_at_the_claude_rung_it_shares, a_claude_delegates_spend_is_unmoved_by_the_kind_overlay, usage_repeated_across_one_api_calls_entries_is_counted_once, spend_from_before_the_task_was_dispatched_is_not_charged_to_it, a_turn_that_ended_before_this_task_was_dispatched_is_not_a_report_for_it, a_report_after_the_reuse_compaction_settles_against_the_brief, a_compaction_the_delegate_runs_mid_task_still_settles_against_the_brief, an_unmarked_turn_after_dispatch_is_still_an_uncorrelated_report, a_turn_opened_by_a_queued_brief_settles_the_task, a_queued_prompt_after_the_brief_caps_the_report_window, a_queued_prompt_without_the_marker_is_an_uncorrelated_report_not_a_settle_on_the_brief, a_task_notification_is_still_a_notification_not_a_queued_turn_prompt, a_turn_that_launched_background_agents_does_not_settle_on_its_announcement, a_task_notification_turn_is_not_an_uncorrelated_report, the_last_subagent_notification_settles_the_task_with_the_verdict, a_synchronous_agent_call_settles_normally, a_subagent_that_never_reports_settles_after_the_subagent_grace, a_session_running_no_task_is_ignored, a_settled_shared_delegate_goes_warm_instead_of_dying, a_settled_worktree_delegate_still_retires, a_users_standing_agent_is_never_pooled_or_deleted, a_blocked_delegate_keeps_its_session_and_agent, a_failed_verdict_pools_a_shared_delegate_warm, a_failed_verdict_stops_a_worktree_delegate, a_kill_that_leaves_the_session_live_keeps_the_row_idle, C691_settlement_through_the_real_session_service_keeps_a_false_kill_owner, C691_settlement_preserves_an_AlwaysOn_or_standing_pool_delegate, a_succeeded_worktree_task_lands_its_branch_and_says_so_in_the_note, a_worktree_code_task_with_no_progress_fails_completed_without_progress, a_worktree_code_task_with_a_post_dispatch_commit_succeeds, a_worktree_code_task_with_a_changed_file_succeeds, unavailable_git_on_a_code_worktree_fails_open, a_legacy_task_without_a_baseline_gets_no_remote_credit, a_shared_code_task_is_not_failed_for_zero_worktree_progress, a_plan_task_with_no_commits_does_not_warn, a_shared_task_reports_git_unattributable, a_shared_report_naming_an_uncommitted_path_warns_the_caller, a_shared_report_naming_an_uncommitted_path_is_committed_when_the_policy_is_on, a_shared_report_whose_claimed_paths_are_clean_reports_git_landed, a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate, a_finished_merge_delegate_unblocks_its_conflicted_parent, C753_MergeHelperCannotMakeConflictedRecoveryOwnerLandable, a_retryable_api_error_defers_the_task_and_never_stores_the_error_text, real_narration_beside_the_stub_still_does_not_settle_on_it, a_second_on_turn_end_on_the_same_stub_adds_nothing, the_api_error_incident_is_warning_when_the_agent_is_not_channel_bound, the_api_error_incident_is_critical_when_the_agent_is_channel_bound, a_needs_human_error_is_critical_even_without_a_channel, a_parked_recovery_fails_the_task_naming_exhaustion, an_exhausted_capacity_wait_fails_the_wall_paused_task, a_dirty_shared_checkout_is_named_in_the_api_error_incident, an_unmarked_stub_turn_is_not_an_uncorrelated_report, a_codex_401_fails_as_authentication_required_with_no_resume, an_unexpected_settlement_exception_is_logged_and_releases_the_settle_lock, a_settled_investigate_report_stamps_next_stage_plan_and_header_next_plan, a_stage_role_without_the_block_gets_next_unmarked, a_docs_task_without_the_block_gets_no_next_bit, artifact_wins_over_the_regex_first_match_when_it_resolves, a_missing_artifact_file_falls_back_to_the_regex_path |
| Antiphon.Tests | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests` | B027, R005 | C519_Queue_to_busy_adapter, C519_Partial_prompt_requires_complete_receipt, C519_Enabled_loss_notice_reaches_adapter, C519_Size_refusal, C519_Converter_handoff |
| Antiphon.Tests | `Antiphon.Tests.Application.DelegationDispatchRecoveryBoundaryTests` | C084 | C1153_Real_client_certificate_holds_original_input, C1153_Real_client_bad_evidence_keeps_failure, C1153_Final_certificate_is_revalidated_under_lock, C1153_Certificate_age_is_monotonic, C1153_Working_or_unknown_inventory_withholds, C1153_Only_new_cold_dispatch_prepares_evidence, C1153_Nonpristine_brief_never_closes_identity, C1153_Certificate_failure_does_not_leak_a_hold_on_later_save, C1153_Due_hold_statement_ceiling, C1149_Absent_launch_is_blocked_with_original_input, C1591_Withheld_brief_still_holds_absent_launch, C1149_Hold_is_once_and_automatic_relaunch_bound_is_zero, C1149_Listed_or_unknown_runner_is_never_absence, C1149_Changed_or_working_attempt_is_untouched, C1149_Different_reason_or_attempted_brief_still_uses_failure_policy, C1149_Native_attempt_keeps_the_failure_path, C1149_Failed_hold_does_not_persist_on_a_later_save, C1149_Caller_note_has_one_complete_user_prompt, C1149_Unknown_native_evidence_is_not_unattempted, C1149_Whitelist_gap_keeps_previous_failure, C1151_Brief_and_spill_remain_byte_identical, C1151_Ceiling_failure_note_has_one_complete_user_prompt, C1213_Recovered_brief_keeps_branch_protections_without_git_common, C1150_Brief_envelope_corpus_matches_the_expected_matrix, C1150_Working_probe_failure_keeps_the_committed_brief, C1150_Outer_envelope_and_receipt_floor_through_the_queue, C1150_Concurrent_producers_ensure_one_brief, C1150_Existing_brief_and_spill_are_byte_identical, C1150_Uncertain_evidence_cannot_create_a_replacement, C1150_Old_attempt_evidence_does_not_suppress_current_brief, C1150_Brief_evidence_whitelist_flips_one_condition, C1150_Inline_brief_mentioning_spill_paths_is_not_held, C1150_Real_producers_race_to_one_brief_and_keep_the_followup, C1150_Late_ensure_after_remote_receipt_and_payload_release, C1150_Local_spill_must_be_present_and_intact, C1150_Spill_pointer_forms_fail_closed, C1150_Brief_decision_table_flips_one_condition, C1161_Failed_caller_note_has_one_complete_user_prompt, C1182_Gone_parent_stamps_the_lost_note_once, C1186_Recovered_failure_preserves_obligation_guidance, C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient, C1150_Resumed_launch_post_input_bookkeeping_flips_one_step, C1150_Resume_input_rule_classifies_every_delivery_result, C1150_Running_cut_is_discovered_and_delivered_once, C1150_Insert_before_flush_cut_reuses_pending_row, C1150_Followup_survives_both_queue_orders, C1150_Discovery_gates_withhold_recovery, C1150_Readiness_or_persistence_fault_preserves_custody, C1150_Watchdog_first_recovers_before_failure, C1149_Watchdog_first_holds_absent_launch, C1150_Recovery_exhaustion_is_visible_without_replacement, C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence, C1149_C1150_Working_full_tick_safety, C1149_C1150_Statement_budgets |

Exact remaining parameter rows for partial classes (one UID per sub-ten-minute row):

| Row | Exact UID |
|---|---|
| C264u1 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Queue_to_busy_adapter(System.String).1.1.0` |
| C264u2 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Queue_to_busy_adapter(System.String).2.1.0` |
| C264u3 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Queue_to_busy_adapter(System.String).3.1.0` |
| C268u1 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).1.1.0` |
| C268u2 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).2.1.0` |
| C268u3 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).3.1.0` |
| C268u4 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).4.1.0` |
| C268u5 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).5.1.0` |
| C268u6 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).6.1.0` |
| C268u7 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).7.1.0` |
| C268u8 | `Antiphon.Tests.Application.ChannelOutboundUnifiedTransportTests.1.1.C519_Converter_handoff(System.Boolean, System.String).8.1.0` |

Exact outstanding row specifications, including parameter UIDs, are retained in /work/worktrees/task-15406ecc/.antiphon/sweep15406/continuation2/remaining-rows-at-stop.json (ignored generated evidence). Classes and remaining method names above remain durable in this committed report. The sole E2E class OutputDistillationApplyCanaryTests is guarded first by NativeWindowsGate and requires explicit headed/canary approvals; if unrun below it is not implied green by that guard.

### Final stop and source boundaries

Coverage stopped before the closing validation phase; the final planned agent-family row was refused by the owned driver outer timebox before any test process started and remains in the exact unrun ledger. Final snapshot at2026-11-14T17:49Z:265 classes with no result and three partial. The two isolated validations completed before the hard stop2026-11-14T17:58:21Z. Final origin/master fetch resolved to f701b2ba967d503427d7064fe4fffa17b8c1738d; all measurements remain bound to8cec1e74e75819ef20756c228add987ce4ae4170, not that later tip. No additional stale-contract failure was found in this window. CARD-1940 records the fixture TEMP-checkout precondition; the other new failed case passed its required once-alone retry and is LOAD. Both original failures remain in the counters.

### Completion and handoff

The additional authorized time box is complete. Execution that remains absent is stated explicitly; the report does not claim exhaustive green coverage or authorize a land. No source repair was applied. Newly classified failures need the cards above; earlier repair owners retain their scope.

--- next stage ---
next: debug
handoff: Repair any newly classified stale-contract tests listed in this window, coordinate the earlier CARD-1923/CARD-1924/CARD-1926/CARD-1936 repairs with their active owners, and diagnose the separately filed REAL-DEFECT families. Exact absent coverage remains in the stop ledger.
artifact: /work/worktrees/task-15406ecc/docs/investigations/2026-11-14-card-1892-stale-test-sweep.md
