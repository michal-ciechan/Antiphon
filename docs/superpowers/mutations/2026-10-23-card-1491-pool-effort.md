# CARD-1491 pending SourceLanding controls

Unbound candidate; no landing operation or source snapshot is bound yet. Original Code/landing
owner: `d15bbee2-70df-47c3-af9e-244806a661f7`. Ordinary Review comes next. After Review and
landing, commission SourceLanding Mutation against its owned snapshot. Every row and variant
below is **pending**. The Code red witnesses are retained evidence, not completed Mutation cycles.
Mutation owns deliberate mutants, red/restore/green, and discovery of missing controls.

Use one exact method filter per control: `/*/*/<Class>/<Method>`. Parameterized methods execute
their variants under that method filter. Do not run the whole class or widen any timeout/assertion.

| ID / variants | Deliberate defect | Exact Class.Method | Required failed observation |
|---|---|---|---|
| PC-1 / separate, equals, conflict, stop | Ignore explicit argv, equals syntax, conflicts, or the `--` terminator | PoolClaudeEffortTests.C1491_resolved_argv_stamp | Literal expected effort/null differs; argv unchanged |
| PC-2 / cold, wedge | Omit the resolved-spec effort write | PoolClaudeEffortTests.C1491_dispatch_launch_persists_effort | Fresh database read at enqueue lacks literal xhigh; replacement must later record high |
| PC-3 / Pool, Pinned | Bypass the corresponding candidate effort comparison | PoolClaudeEffortTests.C1491_review_rejects_overridden_process | Review reuses the xhigh session instead of SpawnFresh |
| PC-4 / Pool, Pinned | Restore the blanket override decline | PoolClaudeEffortTests.C1491_matching_effort_reuses | Matching xhigh session is replaced/declined; pinned tick launches a second process |
| PC-5 / Pool-null, Pinned-null, Pool-invalid, Pinned-invalid | Treat unknown/invalid launch stamp as the tier | PoolClaudeEffortTests.C1491_unknown_effort_declines | Unknown process is reused instead of declined |
| PC-6 | Select the first candidate before checking effort | PoolClaudeEffortTests.C1491_search_selects_matching_candidate | Older matching session is not selected |
| PC-7 / Codex, Grok | Apply the Claude predicate to every provider | PoolClaudeEffortTests.C1491_non_claude_ignores_stamp | Otherwise eligible provider fails reuse |
| PC-8 / Codex, Grok | Preserve stale Claude stamp on a non-Claude launch | PoolClaudeEffortTests.C1491_non_claude_launch_clears_stamp | Persisted/enqueue stamp remains xhigh |
| PC-9 / cold, wedge | Omit claim-time or wedge invalidation | PoolClaudeEffortTests.C1491_interrupted_launch_keeps_unknown | Forced spec error leaves previous xhigh stamp |
| PC-10 | Omit the control-start write or move it before ReloadAsync | AgentSessionBackendTests.C1491_control_start_replaces_stale_effort_stamp | Literal high argv but fresh persisted row remains xhigh |
| PC-11 | Omit adoption invalidation or restrict it to Claude rows | AgentAttachHerdrTests.C1491_adoption_clears_stale_claude_effort | Adopted Grok row retains old Claude xhigh |
| PC-12 / missing-column, nonnullable, defaulted, Down | Omit/add constraints/default to the column or break its inverse | PoolClaudeEffortMigrationTests.C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown | Upgrade/roundtrip/schema assertions fail; build or fixture errors alone are not red |

PC-8 needs care: dropping the final non-Claude write alone is masked by earlier invalidation.
Mutation must identify a realizable stale-stamp defect (for example preservation across both
replacement and final spec write), and record any redundant writer as such rather than treating
a surviving mutant as an executed control. PC-12 may need an alternate behavioral mutation if a
schema change only produces fixture/bootstrap failure. Missing-control discovery remains pending.
