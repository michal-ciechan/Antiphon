# CARD-1491 Code evidence and frozen qualification handoff

Original Code and landing owner: d15bbee2-70df-47c3-af9e-244806a661f7.
Branch: feat/card-task-d15bbee2. Worktree: /work/worktrees/task-d15bbee2.
Task base: bf66aedb13cd7dca818a7f6840d689090d464eb7 (clean assigned plan tip,
parent master 5073726ce95955a09cf0a40b64bfdd501cf99562). The assigned branch is
fast-forward-only; master advanced during this task. No rebase, reset, merge, branch switch,
desktop operation, production migration or deployment was performed.

Plan: docs/superpowers/plans/2026-10-23-card-1491-claude-pool-effort-plan.md.
The brief explicitly skips TestDesign, limits Code to nine checkpoint rows, and defers Unit
and whole-suite work. This overrides its generic Final boilerplate. This report is committed
before CP-3..CP-9 so the last pushed source can be the actual tested source. Final outcomes,
qualified SHA, unedited checkpoint lines, receipt validation and merge-tree observations are
written after the run to the ignored /work/worktrees/task-d15bbee2/.antiphon/task-d15bbee2.md.
This prequalification document does not claim pending green rows passed.

## Implemented behavior

Separate nullable text Agent.PoolClaudeEffort; no default, backfill, index or DTO change.
Both pool match sites compare candidate launch effort with incoming resolved effort before
selection, with standing placement and the busy-pin wait preserved. Cold/wedge replacements
invalidate; resolved specs stamp before enqueue; admitted control start stamps after reload;
external adoption clears even on another provider. Codex/Grok composers and provider stamps
remain unchanged. The parser reads argv tokens without mutation or prompt tokenization.
Legacy null stamps fail closed even with an empty role map. This one-time eligibility transition
can increase cold starts and temporarily retain unused idle seats until ordinary retirement;
there is no pool flush or fixed one-extra-start-per-row guarantee. Runtime manual effort changes
are outside the launch stamp's scope. Pool resume remains refused.

## Migration and activation

CLI-generated 20261023023903_AddPoolClaudeEffort. The snapshot diff adds only its three-line
nullable text property. Up adds exactly that nullable column; Down drops exactly that column.
At generation: master ef121b23f3d78bbeb31e09886dcf8448e4a633e5, latest migration
20261022110953_AddPoolProviderProjection; rc/2026-10-20
5a126f6fc5ae4e16d803b628716b8f0485a37174, latest migration
20261022095749_AddWorktreeResidueCursorLastOutcome. Fresh fetch before qualification:
master cb87cfc42caf63502205787f51713f935721de56; rc unchanged; both maxima unchanged.
The new migration sorts after both. V-12 will exercise actual down/SQL legacy insertion/up,
nullable/no-default metadata, old fields, EF roundtrip, exact column inventory and model agreement.

RC lacks the provider-projection schema and writer blocks. A dedicated rc port must adapt the
source insertion contexts and regenerate/validate designer and snapshot against the rc model,
keeping migration identity coordinated and importing no unrelated master migrations. Apply the
additive schema before starting the new server binary. AppHost activation must occur from the
canonical reviewed rc checkout, with /api/version SHA verification; master landing alone does
not activate product hosts. Restart requirement: server, after the rc port/schema; runner none.
No restart is performed by this task. Keep the column for binary rollback; an old binary does
not enforce matching effort.

## Historical red evidence

Passive foundation da747b0bbb8500dc8fca71c775d1b1d1c73b8848; red-witness/schema commit
6f8df6a70b31bf88994111eddaee950520bbc973. Both pushed before execution.
CP-1 fresh TRX: V-1 8 executions (4 fail/4 pass); V-2 2 fail; V-3 2 fail;
V-4 4 executions (2 fail/2 pass); V-5 4 fail; V-6 1 fail; V-7 2 pass;
V-8 2 fail; V-9 2 fail. Total 27, 19 expected assertion failures, 8 passed controls.
CP-2's first OR filter selected zero: invalid, not a red witness. Corrected the documented
TUnit wildcard-hint syntax and reran only CP-2 through the checkpoint tool row verb with
--no-build at the same source SHA. Exactly V-10 and V-11 failed their persisted-stamp assertions.
No production defect, timeout or assertion was weakened. The Code checkpoint supplement records the syntax correction
for all OR rows. Final producer fixtures use owned isolated databases; guarded assertions remain.

```text
CHECKPOINT CP-1 commit=6f8df6a70b31bf88994111eddaee950520bbc973 build=ok filter=/*/*/PoolClaudeEffortTests/C1491_* executed=27 passed=8 failed=19 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-024400-8609/rows/CP-1/run.trx slot=granted waited=150s dirty=0 source=6f8df6a70b31bf88994111eddaee950520bbc973 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=6f8df6a70b31bf88994111eddaee950520bbc973 build=reused filter=/*/*/(AgentSessionBackendTests)|(AgentAttachHerdrTests)/(C1491_control_start_replaces_stale_effort_stamp)|(C1491_adoption_clears_stale_claude_effort) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-024400-8609/rows/CP-2/run.trx slot=granted waited=270s dirty=0 source=6f8df6a70b31bf88994111eddaee950520bbc973 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=6f8df6a70b31bf88994111eddaee950520bbc973 build=reused filter=/*/*/(AgentSessionBackendTests*)|(AgentAttachHerdrTests*)/(C1491_control_start_replaces_stale_effort_stamp*)|(C1491_adoption_clears_stale_claude_effort*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/c1491-producer-red/CP-2-20261023-025613-859c/run.trx slot=granted waited=15s dirty=0 source=6f8df6a70b31bf88994111eddaee950520bbc973 sourceState=clean buildSource=verified
```

Red structured receipts: .antiphon/checkpoints/20261023-024400-8609/report.json and
.antiphon/c1491-producer-red/CP-2-20261023-025613-859c/report.json. Both bind clean source
and verified build provenance. Neither is green qualification. CP-2 reruns=1 due zero selection.

## Fixture audit and verification limits

Known-launch stamps updated in ClaudeEffortByRoleDispatchTests, AgentTaskPoolTests (including
recovered live delegates), LaunchEnvLayersIntegrationTests (three insertion paths),
GrokDelegateDispatchTests (Claude controls), ModelAvailabilityDispatcherTests,
ComplexityDispatcherTests, RoutingPinDispatcherTests, RoutingPinCandidateDispatchTests,
AgentTaskCheckScheduleTests, TaskPlatformDispatchTests and DispatchConcurrencyTestHost.
WallRerouteDispatchTests and ReceiptFailureDeliveryTests now seed their declared High launch
explicitly instead of editing a Medium seed's tier afterward. A tier edit is not a restamp.
CodexDelegateDispatchTests has warm Codex rows and pure Claude argv controls; PinnedAgentKindTests
exercises a Grok-to-Claude kind mismatch; PhoneHomeTaskDispatchProjectionTests has warm Codex.
Those need no stamp change. Other ModelAvailability helper consumers inherit a known Medium
launch; held/expired admissions and already-dispatched Codex cases do not require an effort rewrite.

CP-3..CP-9 remain pending in this committed pre-run report: V-1..V-12 and R-1..R-4.
Their intended totals are 27, 2, 1, 8, 10, 4, 2 (54 executions). Only listed method selections
are authorized by the specific brief; Unit and full affected classes are deferred, not passed.
No live provider, native Windows or production activation acceptance is claimed.

Tooling outside checkpoints: repository-pinned dotnet tool restore; one EF generation build
(slot=granted waited=0s held=104s; tools 9.0.20 warned runtime 9.0.21); one checkpoint bootstrap
build (slot=granted waited=30s held=37s, four existing nullable warnings, zero errors).
One malformed expected-SHA invocation was rejected before launch; no build/test ran.
Red build held 326.4342891s, slot=granted waited=0s. CP-1 row waited=150s;
invalid CP-2 row waited=270s; corrected CP-2 row waited=15s. Exact lines above retain costs.
The CP-2 correction reused the single S1 build. CP-3..CP-9 share one fresh S2 build.
No repeated green proof is planned. Owned alternate outputs are removed after use.

All PC-1..PC-12 variants remain pending for SourceLanding Mutation:
docs/superpowers/mutations/2026-10-23-card-1491-pool-effort.md. Ordinary Review is next when
required ordinary rows are complete; missing exact execution remains Code, not approval to land.

## Prequalification publication correction

The S2 implementation/report commit 766668c50027a6cbe6e0e447b7f5860de153a3ce was pushed.
Its first green run 20261023-030208-e6d9 was stopped immediately after merge-tree found an
add/add conflict in the independently landed plan. No test row executed. The build had acquired
a slot (waited=0s) and was interrupted; wait returned exit 6 (executor died before phase=done),
so this is invalid evidence, never green. Stop cleanup first reported IOException; the owning
wait then confirmed executor death and removed the shadow copy.

The original plan is restored byte-for-byte to origin/master. Executable corrections now live
in docs/superpowers/plans/2026-10-23-card-1491-code-checkpoints.md, preserving the nine rows.
This correction is test-selection content required for mergeability, not an advisory-only
Review correction. A fresh single build and CP-3..CP-9 qualify the new committed source.
The earlier merge-tree against rc 5a126f6fc5ae4e16d803b628716b8f0485a37174 also showed the
expected provider-context conflicts (dispatcher, control, Agent and related fixtures). A direct
rc cherry-pick is not clean; a separate adapted rc port and rc-model schema proof are required.

## First S2 outcome and fixture correction

At a306dc71b3f02b7a5d06e4a76271a0c4eb329be7, run 20261023-030358-ee59
executed all 54 intended cases: 52 passed, 2 failed, none skipped. Source was clean and
build provenance verified. V-4 pinned xhigh was blocked after an unrelated-root compact
was queued with this execution identity; the brief evidence gate treated that as uncertain.
The corrected fixture is a same-root Code-to-Review follow-up, keeping the Dispatched,
same-agent/session and zero-second-launch assertions. R-3 mid-turn passed its assertions
but synchronously disposed CardSiblingInspectionQueue (IAsyncDisposable). Its provider is
now awaited. Neither timeout nor assertion changes. No inherited-red classification is claimed.

The run also reported cleanup refusal still-present, so its overall receipt is unqualified.
Raw historical receipts remain unchanged. The terminal executor has exited. One fresh build
and the same bounded CP-3..CP-9 group will verify the final committed tip together and produce
a complete qualifying report; this repeats five green rows only to satisfy the requested
single-source final receipt after the two red rows. It is the second normal S2 selection,
within the repeat budget. Unit/full classes remain deferred. No broader test run is added.

```text
CHECKPOINT CP-3 commit=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 build=ok filter=/*/*/PoolClaudeEffortTests/C1491_* executed=27 passed=26 failed=1 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 build=reused filter=/*/*/(AgentSessionBackendTests*)|(AgentAttachHerdrTests*)/(C1491_control_start_replaces_stale_effort_stamp*)|(C1491_adoption_clears_stale_claude_effort*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-4/run.trx slot=granted waited=15s dirty=0 source=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 build=reused filter=/*/*/PoolClaudeEffortMigrationTests/C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-5/run.trx slot=granted waited=30s dirty=0 source=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 build=reused filter=/*/*/ClaudeEffortByRoleDispatchTests/(a_code_task_launches_at_the_overridden_effort_while_review_keeps_the_tier*)|(an_empty_map_launches_todays_argv*)|(an_override_changes_only_its_own_roles_effort_element*)|(a_check_task_pinned_to_its_standing_seat_keeps_the_seats_placement_under_an_override*)|(a_task_pinned_to_a_busy_pool_delegate_still_waits_for_it_under_an_override*)|(a_role_override_that_differs_from_the_tier_declines_warm_reuse*)|(a_task_pinned_to_a_free_pool_delegate_launched_at_another_effort_relaunches*) executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 build=reused filter=/*/*/LaunchEnvLayersIntegrationTests/(C1495_*)|(TryReuseWarmAgentAsync_declines_a_non_empty_override_and_not_an_empty_one*)|(TryReuseWarmAgentAsync_declines_when_inherited_env_projections_differ*) executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-7/run.trx slot=granted waited=15s dirty=0 source=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 build=reused filter=/*/*/AgentTaskPoolTests/(a_queued_task_takes_over_a_warm_agent_in_its_directory*)|(the_reused_sessions_bearer_token_now_resolves_to_the_new_task*)|(a_follow_up_in_the_same_run_keeps_the_context_uncompacted*)|(a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop*) executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-8/run.trx slot=granted waited=60s dirty=0 source=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 build=reused filter=/*/*/(CodexDelegateDispatchTests*)|(GrokDelegateDispatchTests*)/(a_codex_delegate_launches_the_codex_definition_with_a_slug_an_effort_and_developer_instructions*)|(a_grok_delegate_launches_the_grok_definition_with_a_grok_model_when_nothing_composes*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-d15bbee2/.antiphon/checkpoints/20261023-030358-ee59/rows/CP-9/run.trx slot=granted waited=60s dirty=0 source=a306dc71b3f02b7a5d06e4a76271a0c4eb329be7 sourceState=clean buildSource=verified
```

Historical wallSeconds=887.3084456; build slot=granted waited=150s; buildSeconds=440.4104551.
Remaining build/output cleanup and final receipt validation are recorded in the ignored final report.
