# CARD-1892 stale delivery-contract sweep

Measurement in progress. No production or test files are changed by this task.

Baseline: current origin/master `b559bfba2e9a420495122d27171352fe9b7e1ba4`, which is also the assigned task branch base. CARD-1892 production changes are inventoried from `58309bad0^..15afa4846`, including the round-3 CARD-1905 attention closure.

The selection will union the impacted-classes selector, all capped reverse consumers of each changed production type, and delivery/fault vocabulary matches. All Linux rows run serially, under the host build-slot gate, using the repository TestDbFixture shared PostgreSQL and its isolated databases. Every row records its exact filter, result counts, duration, and /proc/loadavg. Pure timeouts are retried once alone. No live qualification or host is changed.

Full generated evidence stays ignored under `.antiphon/sweep15406/`.

## Published interim findings

The uncapped reconstruction has 272 distinct production consumers (257 are absent from the prior Code evidence's named class list). Adding fault/outcome vocabulary and Pending-plus-fault matches yields 472 Linux project/class selections and nine Windows-only selections. These are a census, not a passing claim. The diagnostic copy only raises the index builder's cap/serialization limit in ignored evidence; tracked test/product files are unchanged.

| Family | Executed | Passed | Failed | Skipped | Wall seconds | Load start/end (1/5/15 min) | Classification |
|---|---:|---:|---:|---:|---:|---|---|
| LandRefusalDeliveryWitnessTests | 24 | 23 | 1 | 0 | 168.516 | 14.16/15.68/15.86 -> 27.78/19.59/17.19 | Known STALE-CONTRACT, C1196_Pretyping_transport_failure_recovers_the_same_row; Backlog CARD-1922, repair Debug b4f39ca7 |
| CodexCliObservationGapTests | 7 | 6 | 1 | 0 | 409.265 | 27.78/19.59/17.19 -> 26.73/24.75/20.67 | Known STALE-CONTRACT, C1029_Unobservable_timestamp_floor_is_original; Backlog CARD-1923, repair Debug 31c683a8 |
| SessionMessageQueueWedgedHeadTests | 62 | 62 | 0 | 0 | 161.778 | recorded in ignored results.jsonl | Passed |
| SessionMessageQueueCodexPointerTests | 28 | 28 | 0 | 0 | 119.060 | recorded in ignored results.jsonl | Passed |
| SessionMessageQueueDeliveredSpillRecoveryTests | 5 | 5 | 0 | 0 | 169.167 | recorded in ignored results.jsonl | Passed |

After these complete rows, the measurement driver was stopped at the next row boundary before launching another test. No test process was interrupted. Continued selections group related class families to amortize repeated discovery/fixture startup, retain serial execution and the ten-minute row ceiling. Execution remains in progress; this interim report does not establish an exhaustive list of remaining failures.

## Second published measurement slice

The syntax-audited vocabulary union was expanded to include deferred IOException construction (including Task.FromException), not only explicit throw statements. Current census: **504 Linux project/class selections and nine Windows-only selections**. Two earlier regex-only selections were fixture strings, not live classes, and were removed. Remaining coverage is still not claimed exhaustive.

Additional stale families:

- **CARD-1924**: GrokRulesCompactionRecoveryTests.Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(in-flight, standing), line 172: SubmittedBodies should contain one item, actual zero. Its prior-row LateConfirmed assertion passes. CARD-1892 composerless recovery deliberately makes that flush receipt-only; the test expects automatic advancement to the next body in the same flush. Preserve the no-write boundary and drive a subsequent safe flush in the repair.
- **CARD-1926**: C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock, line 409: entered.Task.IsCompleted should be false, actual true. The fixture uses held Input as MutationLock owner; CARD-1892 deliberately removed Input from that lock. Hold a genuine workspace mutation to preserve the PC-74 lease-serialization witness.

A separate non-delivery failure is under diagnosis: ClaudeAuthProbeTests.Fresh_config_dir_reports_logged_out_with_the_real_cli expects false, actual true. The test overrides the probe's environment lookup but the launched CLI still inherits the process environment. Presence-only inspection confirmed an inherited Claude OAuth variable; no secret value or raw auth response was printed. This is a test-isolation candidate, not a stale delivery contract.

| Row | Executed | Passed | Failed | Skipped | Wall seconds | Family |
|---|---:|---:|---:|---:|---:|---|
| F001 | 144 | 143 | 1 | 14 | 150.768 | GrokRules* |
| F002 | 304 | 304 | 0 | 13 | 439.519 | SessionMessageQueue* |
| F003 | 230 | 230 | 0 | 1 | 132.804 | AgentSession* |
| F004b | 20 | 20 | 0 | 2 | 593.509 | runner C1514HostLoad*; execution 574s, slot wait 15s |
| F005b | 217 | 216 | 1 | 0 | 45.287 | runner input/auth/phone-home |
| F006b | 95 | 94 | 1 | 0 | 88.144 | runner C0824Workspace* |
| F007b | 472 | 472 | 0 | 21 | 272.212 | runner workspace/retention/custody |

F004-F007 returned zero tests with the initial OR syntax; those attempts are preserved and receive no coverage credit. Their corrected suffix-wildcard filters are the b rows above. The first per-class driver stopped before S027 at a measurement boundary; no test process was interrupted. All builds, prerequisite builds and test runs used the slot gate. Shared-Postgres rows and all test projects ran serially.
