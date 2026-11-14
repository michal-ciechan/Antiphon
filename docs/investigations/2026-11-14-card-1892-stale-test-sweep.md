# CARD-1892 stale test sweep — 2026-11-14

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

--- next stage ---
next: debug
handoff: Repair STALE-CONTRACT CARD-1922/CARD-1923/CARD-1924/CARD-1926/CARD-1936; diagnose REAL-DEFECT candidate CARD-1937 (Sent switch admission) and CARD-1938 (Linux ceiling fixture portability), plus the earlier separately filed fixture/native/bootstrap families; continue 380 unobserved and three partial classes in a new time box.
artifact: /work/worktrees/task-15406ecc/docs/investigations/2026-11-14-card-1892-stale-test-sweep.md


## Second 150-minute coverage window — in progress

Caller reopened the task at 2026-11-14 15:28:21 UTC with a hard stop at 17:58:21 UTC. Fresh detached scratch /tmp/task-15406ecc-coverage2 starts at fetched origin/master 8cec1e74e75819ef20756c228add987ce4ae4170; no source changes or merges are applied. The assigned report branch remains fast-forward-only. This window excludes already observed cases, prioritizes the named queue/input/launch/PhoneHome/standing paths, and will append its exact remaining ledger. Prior outcomes and costs remain historical.

Build completed in 314.209 seconds including gate admission (296-second lease); load 20.36 19.91 20.98 -> 27.35 27.33 24.03. Native Antiphon.Tests binary version 1.0.0+8cec1e74e75819ef20756c228add987ce4ae4170, SHA-256 01b79c9298b8120f17d95aaff6c594783e85c5a1d8fb03ab4a3a3d708c7870d3. Scratch tracked source remains clean. No already-observed case is intentionally selected.

### Earlier Linux fixture families: Windows rc relevance

| Card | Can the failing path execute in Windows rc? |
|---|---|
| CARD-1927 ClaudeAuthProbe | Yes. Real CLI child auth/environment inheritance is portable; caller reports desktop CLI logged in. An inherited OAuth variable can defeat the temporary-config logged-out witness. This Linux task did not inspect desktop credentials or execute the Windows case. |
| CARD-1928 Herdr peer loader | CrashedOwnerLeaseReclaimed starts the same owned PowerShell child on Windows, so its child-host loader failure can affect Windows. ChangedAndSymlinkSocketsAreNotReclaimed returns immediately on Windows and does not exercise the failed Linux path. Specific inner module-loader cause remains unproved. |
| CARD-1929..1932 Linux cgroup custody | No. The whole class is NativeLinuxOnly and is excluded from Windows execution. |
| CARD-1934 C519 broker bootstrap | Yes, when Docker-backed integration tests are admitted; Redpanda readiness and Testcontainers resource-reaper startup are prerequisites on Windows too. Linux startup failures do not prove a Windows failure. |
| CARD-1935 C699 completion recovery | Yes. The database/event recovery test is portable, with no Windows exclusion. Its repeated missing stamps can affect Windows rc; product-versus-fixture attribution is unresolved. |

Live Windows numeric chunks remain unmapped except supplied g54/g58. Source golden boundary IDs earlier in this report identify committed roster boundaries only. No live Windows log or desktop was accessed.

Progress checkpoint C001-C003: 73 executed, 73 passed, zero failed, four skipped. AgentControlServiceIntegrationTests passed33/33; six PhoneHome transport/reconnect classes passed40/40; three explicit Grok acceptance classes supplied the four Windows/headed skips. Current-master results remain separate from historical baseline results. Generated receipts are under the ignored continuation2 evidence directory.

Progress checkpoint C001-C007: 229 executed, all229 passed, zero failed, four skipped. All newly selected PhoneHome transport, connection/epoch, pending-inventory, rolling, spill, stranded-queue and task-projection rows passed. The seven previously unobserved PhoneHomeOutageMentionTests methods passed; its earlier in-flight-Enter failure remains historical and excluded here. Static selection audit found zero overlaps with previously observed methods. Standing families are next.
