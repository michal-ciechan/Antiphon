# CARD-0167 S3 re-cut onto master: addendum to the S3 repair 2 plan

Addendum to `2026-10-22-card-0167-provider-endpoint-plan-s3-repair2.md` for Code task 937df27f. The S3 chain of
`feat/card-task-8c2a794e` (reviewed CLEAN at `a36448ab4` by Final Review 4520ae2c) is re-cut onto origin/master
`9cf77dbad`: the S2 carry `25ae68691` drops as already upstream (its files are byte-identical on master except the
two master-evolved files AppDbContext and the model snapshot), and the other 17 commits are cherry-picked in order.

## Resolution

One conflict, at `4135dcb83` in `server/Application/Services/AgentTaskDispatcher.cs`: master's
`DeclinesReuseForClaudeEffort` (ClaudeEffortByRole, CARD-1481 repair) sits beside the old 2-argument
`InheritedEnvMatchesPool(agent, task)` wrapper that S3 replaces. Kept master's method, its summary and both
pool-only call sites byte-for-byte; deleted the 2-argument wrapper; kept S3's endpoint-map wrapper. No caller of a
2-argument form remains (the two instance callers pass `projectEndpoints`; tests call the static 4-argument
predicate). Per file, the +/- lines of `9cf77dbad..tip` equal those of `25ae68691..a36448ab4`, so no other hunk
changed. Against the reviewed tree, only `AgentTaskDispatcher.cs` and the model snapshot differ among the S3 files,
both by master's own commits.

### Checkpoints

One build serves every row (one test assembly, one slice group). CP-3, CP-4 and CP-5 are the repair 2 rows with
their reviewed counts as floors: CP-3 and CP-5 by the brief, CP-4 because `LaunchEnvLayersIntegrationTests` drives
`AgentTaskDispatcher`, the one file the conflict touched. CP-1 and CP-2 are not rerun: none of their classes
references the dispatcher, and their production files are unchanged against the reviewed tree. CP-7 and CP-8 are
the CARD-1481 rows on the same reuse method.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | RECUT | `tests/Antiphon.Tests -> bin-c0167rc/` | resolver-unit | `/*/*/(AgentTuiLaunchResolverTests*)\|(AgentLaunchEnvTests*)\|(PhoneHomeTaskRoutingTests*)/*` | D-4, D-5, D-11, pool predicate witnesses | all listed, 0 failed, 0 skipped | 62 | 12 |
| CP-4 | RECUT | CP-3 | resolver-layers | `/*/*/LaunchEnvLayersIntegrationTests/*` | D-4 on the legacy registry path through the dispatcher | all listed, 0 failed, 0 skipped | 25 | 10 |
| CP-5 | RECUT | CP-3 | stamp-producers | `/*/*/(PoolProviderProjectionStampTests*)\|(AgentSessionBackendTests*)\|(AgentAttachHerdrTests*)/*` | R1 stamp producers | all listed, 0 failed, 0 skipped | 43 | 15 |
| CP-7 | RECUT | CP-3 | role-effort-dispatch | `/*/*/ClaudeEffortByRoleDispatchTests/*` | CARD-1481 pool-only effort decline beside the resolved wrapper | all listed, 0 failed, 0 skipped | 10 | 5 |
| CP-8 | RECUT | CP-3 | role-effort-unit | `/*/*/ClaudeEffortByRoleSettingsTests/*` | CARD-1481 effort settings and RoleOverrideDiffersFromTier | all listed, 0 failed, 0 skipped | 18 | 5 |

No V-n/R-n IDs or PC rows are added; the repair 2 plan's PCs (none) and operator acceptance items are unchanged.
