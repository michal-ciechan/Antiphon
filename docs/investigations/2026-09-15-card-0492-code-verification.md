# CARD-0492 Code verification — db8d01f9

S1–S5 are implemented and pushed. All 25 planned ordinary cases pass. Unit and affected
integration sweeps have inherited failures, reproduced on the exact base commit.
Build-output cleanup was rejected before execution; 30 inventoried directories remain.

## Identity and handoff

- Original Code task / landing owner: `db8d01f9`.
- Branch: `feat/card-task-db8d01f9`.
- Exact worktree: `C:\Antiphon\worktrees\card-task-db8d01f9`.
- Tested implementation SHA: `d431cba2c9899ec285aabbf3c1c8adb72fa2c6c3`.
- Base: `e4191c39cbf3db6da38af3f9bd01e18b846bfbad`.
- [Landed plan and execution clarification](../superpowers/plans/2026-09-14-card-0492-stale-api-error-stub-settlement-plan.md).
- Evidence root: `C:\Antiphon\worktrees\card-task-db8d01f9\.antiphon\c492-evidence`.
- Next: ordinary read-only Review. Restart target: **server**, after caller-owned landing.
- No landing, deployment or deliberate mutation was performed. The caller records the companion
  verification obligation, lands the original Code task after Review, then commissions SourceLanding
  Mutation. All R controls below remain pending.

This report is a documentation-only addition after the tested implementation. The implementation
and test files were frozen during each run. All owned build/test processes were awaited.
The worktree was temporarily detached at base for failure comparisons, then restored to the
original branch. `restored-binaries.json` confirms the original tested server/test DLL hashes were
unchanged by the separate baseline build.

## Changes

- **S1+S2 together:** one housekeeping-aware prompt predicate is shared by recovery and settlement.
  After adoption, a later real user/queued prompt keeps the task Working for every classification.
  Deferred events remain once per stub, with an exact `(seq N)` identity. Both recovery prompt
  variants carry the open task marker; taskless recovery remains unchanged.
- **S3:** recent working sessions are protected from TTL and cap retirement, with the idle clock
  restarted. Cap replacement selects the next oldest idle member. A full TTL of transcript silence
  permits retirement. Unpinned reuse skips working candidates; Shared release still pools and warns.
- **S4:** task detail exposes read-time session liveness; the drawer and CLI show it. Failed/Blocked
  parent headers carry the observation, and API-error failure reasons describe it before release.
- **S5:** updated runtime/orchestration/API documentation and corrected CARD-0072's historical
  settlement promise. No settings, schema or migrations were added.

Production paths: `server/Application/Services/{TranscriptPromptSpan,ApiErrorRecoveryService,
AgentTaskReplyService,AgentTaskDispatcher,AgentTaskService,DelegationReportFormatter}.cs`,
`server/Application/Dtos/AgentTaskDtos.cs`, `client/src/api/agentTasks.ts`,
`client/src/features/delegations/TaskDetailBody.tsx`, `scripts/delegate.ps1`.
Tests are in the existing reply, recovery, pool, detail, `DelegationUnitTests.cs` and drawer files.

S1+S2 checkpoint: `4704880c74df6e2ee1fe74e6a12206c5a8405be6`;
S3: `1dba34bd31504dc038a215d50ebd5be6966157f6`;
S4: `7571152a510d28918db71feb39e4ca39105cc699`;
S5: `2bbd096f2101ca36914395f8df2ba7b50ba74245`.
Every slice/fix was committed and pushed before its next long run.

## Commands, counts and evidence

All paths below are relative to the evidence root unless stated otherwise.
`verification-summary.json` joins each V case to executed final TRX identities and records the
full filters, class counts, source SHA, baseline results and pending controls.
`cases.json`, `base-cases.json` and `run-ordinary.ps1` retain exact rerun arguments.

Build: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c492/ --nologo`.
Final build: **0 errors, 231 warnings**, 96.07 seconds (`build-4.log`, `build-source-4.txt`).
Earlier compilation found and repaired a duplicate test-class name and a nullable assertion
overload; neither changed an assertion threshold. Initial method run found one incomplete V-12
fixture (missing its agent row), repaired without weakening the incident assertion.

Ordinary TUnit command template (fresh results directory each invocation):

```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c492/ -- --treenode-filter '<filter>' --report-trx --report-trx-filename run.trx --results-directory '<fresh-directory>' --output Detailed
```

| Command | Filter / scope | Actual expanded counts | Evidence |
|---|---|---|---|
| M1 | Each exact `/*/*/<Class>/<Method>` from `cases.json` | 23 selected/executed; 22 passed, V-12 failed before fixture repair | `methods-1.json`, `V-1-1/run.trx` through `V-23-1/run.trx`; SHA e0395716a6a8eae0f89c891ef60165253d00e4cb |
| M2 | Exact V-10, V-12, V-22 methods | 3 selected/executed/passed; zero failures | `repairs-2.json`, `V-10-2/run.trx`, `V-12-2/run.trx`, `V-22-2/run.trx`; tested SHA above |
| U | `/*/*/*/*[Category=Unit]` | 2461 selected, 2460 executed: 2456 passed, 4 inherited failures, 1 skipped | `unit-1/run.trx`; 136.6 seconds |
| I | Eight named integration classes below | 282 selected/executed: 273 passed, 9 inherited failures | `integration-1/run.trx`; 167.9 seconds |
| C | `pwsh -NoProfile -File scripts/test-client.ps1 -JsonResultPath <client.json>` | 912 selected: 907 passed, 4 failed, 1 skipped, 100 files | `client.json`, `client.log`; 339.59 seconds after install |
| C-base | Four failed client methods only, at exact base | 4 executed: 2 failed identically, 2 passed; 35 other cases skipped | `client-base.json`, `client-base.log` |
| C-isolated | Board move and Herdr label submission methods on Code branch | 2 executed/passed; 33 other cases skipped | `client-isolated.json`, `client-isolated.log` |
| B | Each of 13 failed backend methods, exact-method filters, at base | 13 executed, **all 13 reproduced their assertion failure**; zero empty runs/build/fixture failures | `baseline-1.json`, `base-1-1/run.trx` through `base-13-1/run.trx` |
| TypeScript | `node node_modules/typescript/bin/tsc -b` in client | Exit 0 | `client-typecheck.log` |
| CLI syntax | PowerShell AST parse of `scripts/delegate.ps1` | 0 errors | `delegate-parse.json` |

The client source was unchanged after its C run at 2bbd096f2101ca36914395f8df2ba7b50ba74245.
The baseline backend build used `bin-c492-base/` and completed with 0 errors / 231 warnings.
The baseline tests were failure comparisons, not deliberate PC executions.

Exact I filter:

```text
/*/Antiphon.Tests.Application/(ApiErrorRecoveryServiceTests*)|(AgentTaskReplyIntegrationTests*)|(ComplexityWallRerouteTests*)|(AgentTaskSettlementRaceTests*)|(AgentTaskPoolTests*)|(AgentTaskCheckScheduleTests*)|(GrokDelegateDispatchTests*)|(AgentTaskDetailBlockedContextTests*)/*
```

| Intended class (all present, no extra classes) | Expanded | Passed | Failed |
|---|---:|---:|---:|
| ApiErrorRecoveryServiceTests | 40 | 33 | 7 |
| AgentTaskReplyIntegrationTests | 138 | 138 | 0 |
| ComplexityWallRerouteTests | 11 | 9 | 2 |
| AgentTaskSettlementRaceTests | 8 | 8 | 0 |
| AgentTaskPoolTests | 28 | 28 | 0 |
| AgentTaskCheckScheduleTests | 9 | 9 | 0 |
| GrokDelegateDispatchTests | 38 | 38 | 0 |
| AgentTaskDetailBlockedContextTests | 10 | 10 | 0 |

No namespace/full-assembly run was used; the current Fast lane and stage contract supersede the
plan's older broad-sweep sentence. Unit plus the plan's bounded affected integration classes ran.

## Every V outcome

Each C# row also had an exact-method execution (M1 or M2). U/I are the shared final commands
at the tested implementation SHA; the summary script requires exactly one passing executed
identity for every C# V in those final TRX files.

| ID | Executed class.method | Final outcome / expanded | Shared command |
|---|---|---|---|
| V-1 | `AgentTaskReplyIntegrationTests.a_transient_stub_with_a_later_prompt_does_not_fail_the_task` | Passed, 1 | I |
| V-2 | `AgentTaskReplyIntegrationTests.a_transient_stub_with_a_later_queued_prompt_does_not_fail_the_task` | Passed, 1 | I |
| V-3 | `AgentTaskReplyIntegrationTests.a_needs_human_stub_with_a_later_prompt_does_not_fail_the_task` | Passed, 1 | I |
| V-4 | `AgentTaskReplyIntegrationTests.the_skip_arm_records_the_death_once_on_the_timeline` | Passed, 1 | I |
| V-5 | `AgentTaskReplyIntegrationTests.a_housekeeping_record_after_the_stub_is_not_a_resume` | Passed, 1 | I |
| V-6 | `ApiErrorRecoveryServiceTests.A_local_command_record_after_the_stub_does_not_supersede_the_resume` | Passed, 1 | I |
| V-7 | `ApiErrorRecoveryServiceTests.The_resume_prompt_carries_the_open_tasks_marker` | Passed, 1 | I |
| V-8 | `ApiErrorRecoveryServiceTests.A_taskless_session_gets_the_bare_resume_prompt` | Passed, 1 | I |
| V-9 | `ApiErrorRecoveryServiceTests.The_wall_resume_prompt_carries_the_marker_too` | Passed, 1 | I |
| V-10 | `AgentTaskReplyIntegrationTests.the_resumed_turns_marked_report_settles_the_task` | Passed, 1 | I |
| V-11 | `AgentTaskReplyIntegrationTests.a_second_stub_on_the_resumed_turn_defers_on_its_own_row` | Passed, 1 | I |
| V-12 | `AgentTaskReplyIntegrationTests.an_unmarked_resumed_turn_is_still_uncorrelated` | Passed, 1 | I |
| V-13 | `AgentTaskPoolTests.the_janitor_does_not_retire_a_warm_agent_whose_session_is_mid_turn` | Passed, 1 | I |
| V-14 | `AgentTaskPoolTests.the_janitor_retires_it_after_the_turn_ends_and_the_ttl_elapses_again` | Passed, 1 | I |
| V-15 | `AgentTaskPoolTests.the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl` | Passed, 1 | I |
| V-16 | `AgentTaskPoolTests.the_per_directory_cap_skips_a_mid_turn_agent` | Passed, 1 | I |
| V-17 | `AgentTaskPoolTests.a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop` | Passed, 1 | I |
| V-18 | `AgentTaskReplyIntegrationTests.releasing_a_shared_task_whose_session_is_mid_turn_pools_it_and_warns` | Passed, 1 | I |
| V-19 | `AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_for_a_live_working_session` | Passed, 1 | I |
| V-20 | `AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_for_an_ended_session` | Passed, 1 | I |
| V-21 | `AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_is_null_without_a_session` | Passed, 1 | I |
| V-22 | `AgentTaskReplyIntegrationTests.a_failed_api_error_task_names_session_liveness` | Passed, 1 | I |
| V-23 | `DelegationReportFormatterTests.the_header_carries_the_session_bit_only_when_supplied` | Passed, 1 | U |
| V-24 | `TaskDetailBody.test.tsx`: shows session liveness under a failed task; omits it when the detail has no session | Passed, 2 | C |

## Inherited failures, individually reproduced

| Base command | Test | Same failing assertion on Code and base |
|---|---|---|
| base-1 | TestClassificationGuardTests.Registry_matches_compiled_metadata | HerdrPaneDisposalEndpointTests lacks Unit/Integration classification |
| base-2 | ScopedVerificationInstructionTests.C487_G142 | Expects obsolete `next: mutation` Code-stage text |
| base-3 | TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration | Same untagged Herdr class |
| base-4 | TestClassificationPolicyTests.C487_G068 | Same lane-xor metadata error |
| base-5 | ComplexityWallRerouteTests.Required_pinned_task_is_untouched_on_a_Fable_5_wall | Expected Failed, actual Working |
| base-6 | ComplexityWallRerouteTests.Non_chain_task_fails_on_Fable_5_as_today | Expected Failed, actual Working |
| base-7 | ApiErrorRecoveryServiceTests.Session_limit_stub_schedules_one_resume_at_reset_plus_padding | Expected July 15 reset, actual September 15 |
| base-8 | ApiErrorRecoveryServiceTests.Codex_TurnEnd_text_without_AssistantText_still_parses_session_limit | Expected September 5 reset, actual September 15 |
| base-9 | ApiErrorRecoveryServiceTests.Empty_wall_adopt_is_repaired_when_a_later_call_supplies_the_real_text | Expected WallModelPaused, actual null |
| base-10 | ApiErrorRecoveryServiceTests.Grok_402_stub_writes_a_fallback_hold_for_grok_4_6_and_never_enqueues | Expected whole-second hold timestamp, actual fractional timestamp |
| base-11 | ApiErrorRecoveryServiceTests.Claude_production_shape_session_limit_uses_AssistantText_not_the_6h_fallback | Expected September 5 reset, actual September 15 |
| base-12 | ApiErrorRecoveryServiceTests.Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue | Expected whole-second hold timestamp, actual fractional timestamp |
| base-13 | ApiErrorRecoveryServiceTests.Wall_parks_after_three_deaths | Expected Replaced, actual Superseded |

Client lint failure reproduces on base at `SpecialistRoutingPanel.tsx` (state set synchronously
inside an effect). The existing drawer approval-evidence case reproduces the duplicate
`/Approved original/` match on base. BoardPage's optimistic move and AgentHerdrPlacement's
submission timeout both passed in explicit isolation on base and the Code branch.
The original full client run remains a red run; it is not relabelled green.

## Pending deliberate controls

The plan names its PCs **R-1–R-14**, with 20 named method targets; there are no separate PC-n IDs.
No deliberate mutant ran. Every control requires post-land SourceLanding red/restore/green
evidence, method-scoped, plus missing-control discovery.

| ID | Planned mutation | Method targets | Actual outcome |
|---|---|---|---|
| R-1 | Invert settlement guard | V-1, V-3 | Pending SourceLanding Mutation |
| R-2 | Count only UserPrompt | V-2 | Pending SourceLanding Mutation |
| R-3 | Remove deferred-event idempotence | V-4 | Pending SourceLanding Mutation |
| R-4 | Use raw prompt kinds without housekeeping exclusion | V-5, V-6 | Pending SourceLanding Mutation |
| R-5 | Omit recovery task-marker prefix | V-7, V-9, V-10 | Pending SourceLanding Mutation |
| R-6 | Prefix taskless recovery | V-8 | Pending SourceLanding Mutation |
| R-7 | Remove janitor gate | V-13, V-16 | Pending SourceLanding Mutation |
| R-8 | Ignore silence bound | V-15 | Pending SourceLanding Mutation |
| R-9 | Do not restart idle clock | V-13 | Pending SourceLanding Mutation |
| R-10 | Remove reuse working filter | V-17 | Pending SourceLanding Mutation |
| R-11 | Remove release warning | V-18 | Pending SourceLanding Mutation |
| R-12 | Return null detail session | V-19, V-20 | Pending SourceLanding Mutation |
| R-13 | Omit parent-header session bit | V-22 | Pending SourceLanding Mutation |
| R-14 | Omit failure-reason liveness sentence | V-22 | Pending SourceLanding Mutation |

## Limits and coverage findings

- The original V-10 fixture could not detect removal of the producer's marker. This gap is closed:
  V-10 now fires the real recovery service, confirms the actual queued UserPrompt, and consumes
  the final report without seeding another prompt. V-22 confirms the failure header in the
  parent's matching UserPrompt through the real persistence/queue path.
- Receipt tests use the established fake protocol adapter. Live provider continuation and
  post-deployment retirement observation remain unperformed, as does live CLI status output
  (its syntax and the DTO/drawer behaviours are verified).
- Duration tripwire: `pwsh -File scripts/test-duration-tripwire.ps1 -Trx <integration-1/run.trx>`
  returned 1 with 26 unlisted rows >=5 seconds. New V-19–V-21 measured 33.08–34.08 seconds alongside
  all ten detail tests at startup; shared database initialization is the likely cause, not a
  measured isolated service-time claim. New pool checks measured 0.04–0.07 seconds in I;
  strengthened V-10 was 1.50 seconds and V-22 was 0.92 seconds. No timeout or assertion was loosened.

## Cleanup refusal

Automatic approval review rejected the guarded recursive removal of the 30 exact inventoried
`bin-c492` / `bin-c492-base` directories with **“blocked by policy”**. The command did not start;
read-only verification confirmed all 30 remain. No alternate deletion mechanism was attempted.
`cleanup-refusal.json`, `owned-build-directories.txt` and `owned-base-build-directories.txt`
retain the exact paths for caller-owned cleanup. This is output residue, not outstanding
implementation or ordinary verification.

