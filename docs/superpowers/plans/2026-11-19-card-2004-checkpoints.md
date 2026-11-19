# CARD-2004 checkpoint extract (Code task 6852e148)

`X-Antiphon-Caller` on `GET /api/agent-tasks/{id}` is stored only when the trimmed,
lowercased value is a registered label. The list lives in one place,
`AgentTaskGetPollMetrics.RegisteredLabels`: `delegate.ps1`, `status-snapshot.ps1`,
`checkpoint-task.ps1`, `card.ps1`, `board-watch`, and `useagenttask` (header
`useAgentTask`). Any other header is not a label and is not copied into the snapshot,
a log, or an error. With no registered header, a resolved task token is
`kind:<AgentKind>`, a capability token is `capability:<name>` or `capability`, and a
session token with no task is `session`. Anything left is `unknown`.

`kind:*`, `session`, and bare `capability` have their own slots. Named capability
labels, and registered header labels, each stop at 64. Junk headers cannot evict the
server-derived rows. `GET /api/agent-tasks` (the list) is not counted. The diagnostics
route stays `GET /api/diagnostics/agent-task-polls`.

This slice does not change `scripts/delegate.ps1`, `scripts/status-snapshot.ps1`,
`scripts/checkpoint-task.ps1`, `scripts/card.ps1`, or the client `useAgentTask` to send
the header. No migration. No positive-control row. Mutation execution is waived
(operator 2026-11-12) and is not discharged. Short tests only (operator 2026-11-18).

`WindowsUnitPlatformAdmissionTests` is not a row. The class is untouched, carries a
long timeout, and `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux before
it launches a child host. A skipped result is not a clean checkpoint certificate.

The red witnesses ran once, unlisted, at `7bd7e9450294f977bbebdf27280735e422f59c11`
(filter `/*/*/AgentTaskGetPollMetricsTests/*`, build slot `c2004-red` waited=0s,
run slot `c2004-red-run` waited=0s): executed 11, failed 6, passed 5. The six failures
were the secret-shaped header, the unregistered `client` label, and the junk-header
eviction. This table is the green closed list. It has no expected-red row.

## Verification design

- W-1. `C1972_script_header_is_counted_ahead_of_agent_kind`: a registered header wins over agent kind; `client` falls through to `kind:Grok`.
- W-2. `C1972_task_token_uses_agent_kind`: a blank or missing header with a resolved task is `kind:<AgentKind>`.
- W-3. `C1972_absent_identity_is_unknown`: no caller, an empty caller, the word `unknown`, a rejected shape, and `client` are `unknown`.
- W-4. `C1972_unsafe_header_is_not_a_label`: hex, guid, `ghp_`, `sk-`, 40 alphanumerics, and a base64 string are absent from the snapshot.
- W-5. `C1972_distinct_labels_stop_at_64`: the 65th capability name is overflow; `kind:Grok` is still stored.
- W-6. `C1972_session_and_capability_are_bounded_labels`: session and capability labels, with no guid in the snapshot.
- W-7. `C1972_detail_get_and_diagnostics_route_are_wired`: the detail GET calls `Record` once, diagnostics calls `Read` once, and Program registers the singleton. The counts fail if the call is removed.
- W-8. `C2004_registered_labels_count_and_other_headers_do_not`: the six registered names count; `client` and a `ghp_` token do not.
- W-9. `C2004_junk_headers_cannot_evict_server_labels`: 69 junk headers stay out of the rows and leave every kind, `session`, and `capability` in place.
- W-10. `C2004_unknown_header_with_resolved_task_uses_kind`: header `unknown` with a resolved task is `kind:Codex`.
- W-11. `C2004_list_route_is_not_counted`: `polls.Record` appears once, in the detail GET, and not in the list GET.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c2004-a/ | c2004-polls | `/*/*/AgentTaskGetPollMetricsTests/*` | W-1, W-2, W-3, W-4, W-5, W-6, W-7, W-8, W-9, W-10, W-11 | 0 failed, executed 11 | 11 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S1 | CP-1 | c2004-dbrate | `/*/*/(DbRateSamplerTests*)\|(DbCallSiteMetricsTests*)/*` | db-rate and call-site counters | 0 failed, executed 11 | 11 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S1 | CP-1 | c2004-policy | `/*/*/TestClassificationPolicyTests/*` | policy sweep | 0 failed, executed 42 | 42 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S1 | CP-1 | c2004-index | `/*/*/DerivedImpactIndexTests/*` | derived-impact index | 0 failed, executed 27 | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S1 | CP-1 | c2004-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | guard, tripwire, native Linux, mechanical contract, platform guidance, layering, lane | 0 failed, executed 34 | 34 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S1 | CP-1 | c2004-slowmerge | `/*/*/SlowTestRegistryMergeTests/*` | slow-registry merge | 0 failed, executed 5 | 5 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S1 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | land graph | exit 0 | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | S1 | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef c84185e9a424a4cc74011c7d3884e382d3bf9992 -HeadRef HEAD` | evidence diff over the task base | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 51 minutes (sum of EstimatedMinutes). Derived total timeout is 112 minutes.
CP-1's deadline is 45 minutes. No row's `3 * EstimatedMinutes` exceeds the 45-minute advisory.
