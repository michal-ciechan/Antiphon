# CARD-1972 checkpoint extract (Code task 26e01f09)

Count `GET /api/agent-tasks/{id}` by a bounded caller label and serve the snapshot at
`GET /api/diagnostics/agent-task-polls`. The counter lives in
`server/Application/Services/AgentTaskGetPollMetrics.cs`. The detail route records the
`X-Antiphon-Caller` header and the `AgentTaskService.Caller` it already resolved.
`Record` does not query Postgres. `GET /api/agent-tasks` (the list) is not counted.

A usable header is an ASCII letter, then up to 63 letters, digits, `.`, `_` or `-`, stored
lowercased. A guid, a hex string of 32 or more characters, the word `unknown`, and any
other shape are not labels. With no usable header, a resolved task token is
`kind:<AgentKind>`, a capability token is `capability:<name>` or `capability`, and a
session token with no task is `session`. Anything left is `unknown`, which is its own
counter and does not take a label slot. At most 64 other labels; a further new label
increments `overflow` and is not stored. The snapshot is process-lifetime
(`startedAt`, `total`, `unknown`, `overflow`, `callers`). Sampling it twice is the rate.

`scripts/delegate.ps1 -Status`, `scripts/status-snapshot.ps1`, `scripts/checkpoint-task.ps1`,
and the client `useAgentTask` drawer do not send `X-Antiphon-Caller`. This slice does not
edit them and does not change their intervals. A task token already on `-Status` counts as
`kind:<AgentKind>` until a later change sends the header. That change waits on a read of
this endpoint.

No migration. No positive-control row. Mutation execution is waived (operator 2026-11-12)
and is not discharged. Short tests only (operator 2026-11-18).

`WindowsUnitPlatformAdmissionTests` is not a row. The class is untouched, carries
`[Timeout(600_000)]`, and `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux
before it launches a child host. A skipped result is not a clean checkpoint certificate.
The other guard classes in the standing union are CP-3, CP-5, and CP-6.
`TaskPlatformGuidanceTests` is five methods. `RunnerDefaultGuidanceTests` is four more methods in the same file and is not this filter. CP-5's floor is the 34 results those seven classes execute (mechanical arguments included).

The red witnesses ran once, unlisted, at `446db1876829992c721ac3dfda3faadfc6fd7e7f`
(filter `/*/*/AgentTaskGetPollMetricsTests/*`, 7 failed, 0 passed) while the counter stub
recorded nothing. This table is the green closed list. It has no expected-red row.

## Verification design

- W-1. `C1972_script_header_is_counted_ahead_of_agent_kind`: a script header wins over agent kind and is stored lowercased.
- W-2. `C1972_task_token_uses_agent_kind`: a blank or missing header with a resolved task is `kind:<AgentKind>`.
- W-3. `C1972_absent_identity_is_unknown`: no caller, an empty caller, the word `unknown`, and a rejected shape are `unknown`. `startedAt` is about now.
- W-4. `C1972_unsafe_header_is_not_a_label`: a long hex header falls through to agent kind; a guid with no caller is `unknown`. Neither value is stored.
- W-5. `C1972_distinct_labels_stop_at_64`: the 65th new label is overflow; an existing label still increments; `unknown` stays separate.
- W-6. `C1972_session_and_capability_are_bounded_labels`: session and capability labels, with no guid in the snapshot.
- W-7. `C1972_detail_get_and_diagnostics_route_are_wired`: the detail GET calls `Record` with the header, diagnostics maps `Read`, and Program registers the singleton.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1972-a/ | c1972-polls | `/*/*/AgentTaskGetPollMetricsTests/*` | W-1, W-2, W-3, W-4, W-5, W-6, W-7 | 0 failed, executed 7 | 7 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S1 | CP-1 | c1972-dbrate | `/*/*/(DbRateSamplerTests*)\|(DbCallSiteMetricsTests*)/*` | db-rate and call-site counters | 0 failed, executed 11 | 11 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S1 | CP-1 | c1972-policy | `/*/*/TestClassificationPolicyTests/*` | policy sweep | 0 failed, executed 42 | 42 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S1 | CP-1 | c1972-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | derived-impact index | 0 failed, executed 27 | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S1 | CP-1 | c1972-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | guard, tripwire, native Linux, mechanical contract, platform guidance, layering, lane | 0 failed, executed 34 | 34 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S1 | CP-1 | c1972-slowmerge | `/*/*/SlowTestRegistryMergeTests/*` | slow-registry merge | 0 failed, executed 5 | 5 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S1 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | land graph | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S1 | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 81add8595d60295b4bf1d3bb76af2e927842421f -HeadRef HEAD` | evidence diff over the task base | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 51 minutes (sum of EstimatedMinutes). Derived total timeout is 112 minutes.
CP-1's deadline is 45 minutes. No row's `3 * EstimatedMinutes` exceeds the 45-minute advisory.
