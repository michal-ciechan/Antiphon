# CARD-1491 executable Code checkpoint supplement

Implementation decisions and V/R definitions remain in the landed
[plan](2026-10-23-card-1491-claude-pool-effort-plan.md). The Code brief explicitly skipped
TestDesign and authorized its nine bounded rows, deferring Unit/full-suite verification.
This supplement owns only executable filter corrections. No tests, assertions or counts are
removed or widened. TUnit 1.44 source-generated hint extraction requires trailing wildcards
on OR operands; the original CP-2 executed zero. Corrected CP-2 reused S1's existing build
and produced the two intended assertion failures. Fresh TRX must prove the exact intended
class/method roster, including all argument expansions. No extra matches are accepted.

Use this supplement for CP-3..CP-9 at one frozen committed SHA. Each group builds once.
S1 historical receipts and the correction cost are in
[Code evidence](2026-10-23-card-1491-code-evidence.md). S1 is already complete; do not repeat
its green controls. All positive controls remain pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1491-red/` | effort-red | `/*/*/PoolClaudeEffortTests/C1491_*` | V-1..V-9 | intentional red: 19 failed assertions, 8 passed controls, 0 skipped | 27 | 9 |
| CP-2 | S1 | CP-1 | producer-red | `/*/*/(AgentSessionBackendTests*)\|(AgentAttachHerdrTests*)/(C1491_control_start_replaces_stale_effort_stamp*)\|(C1491_adoption_clears_stale_claude_effort*)` | V-10, V-11 | intentional red: 2 failed stamp assertions, 0 skipped | 2 | 1 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1491-green/` | effort-green | `/*/*/PoolClaudeEffortTests/C1491_*` | V-1..V-9 | all 27 passed, 0 failed, 0 skipped | 27 | 9 |
| CP-4 | S2 | CP-3 | producer-green | `/*/*/(AgentSessionBackendTests*)\|(AgentAttachHerdrTests*)/(C1491_control_start_replaces_stale_effort_stamp*)\|(C1491_adoption_clears_stale_claude_effort*)` | V-10, V-11 | all 2 passed, 0 failed, 0 skipped | 2 | 1 |
| CP-5 | S2 | CP-3 | migration | `/*/*/PoolClaudeEffortMigrationTests/C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown` | V-12 | 1 passed, 0 failed, 0 skipped | 1 | 1 |
| CP-6 | S2 | CP-3 | claude-compatibility | `/*/*/ClaudeEffortByRoleDispatchTests/(a_code_task_launches_at_the_overridden_effort_while_review_keeps_the_tier*)\|(an_empty_map_launches_todays_argv*)\|(an_override_changes_only_its_own_roles_effort_element*)\|(a_check_task_pinned_to_its_standing_seat_keeps_the_seats_placement_under_an_override*)\|(a_task_pinned_to_a_busy_pool_delegate_still_waits_for_it_under_an_override*)\|(a_role_override_that_differs_from_the_tier_declines_warm_reuse*)\|(a_task_pinned_to_a_free_pool_delegate_launched_at_another_effort_relaunches*)` | R-1 | all 8 passed, 0 failed, 0 skipped | 8 | 1 |
| CP-7 | S2 | CP-3 | environment-compatibility | `/*/*/LaunchEnvLayersIntegrationTests/(C1495_*)\|(TryReuseWarmAgentAsync_declines_a_non_empty_override_and_not_an_empty_one*)\|(TryReuseWarmAgentAsync_declines_when_inherited_env_projections_differ*)` | R-2 | all 10 passed, 0 failed, 0 skipped | 10 | 1 |
| CP-8 | S2 | CP-3 | pool-compatibility | `/*/*/AgentTaskPoolTests/(a_queued_task_takes_over_a_warm_agent_in_its_directory*)\|(the_reused_sessions_bearer_token_now_resolves_to_the_new_task*)\|(a_follow_up_in_the_same_run_keeps_the_context_uncompacted*)\|(a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop*)` | R-3 | all 4 passed, 0 failed, 0 skipped | 4 | 1 |
| CP-9 | S2 | CP-3 | provider-argv | `/*/*/(CodexDelegateDispatchTests*)\|(GrokDelegateDispatchTests*)/(a_codex_delegate_launches_the_codex_definition_with_a_slug_an_effort_and_developer_instructions*)\|(a_grok_delegate_launches_the_grok_definition_with_a_grok_model_when_nothing_composes*)` | R-4 | all 2 passed, 0 failed, 0 skipped | 2 | 1 |

