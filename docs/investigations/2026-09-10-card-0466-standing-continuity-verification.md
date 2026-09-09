# CARD-0466 standing session continuity verification

Code and verification complete: 665/665 .NET cases passed across 32 required/touched classes; 47/47 client tests passed. Zero final failures, zero skipped. Seven deliberate-defect positive controls failed red and passed after restoration. Earlier fixture failures are superseded by the successful results below.

Worktree: C:\Antiphon\worktrees\card-task-e4eeea70
Branch: feat/card-task-e4eeea70

Implementation preserves standing native identity through infrastructure failures, reserves explicit recovery atomically, and holds unavailable continuity for an operator decision. No live stack, migration, provider home, or messaging broker is part of acceptance.

## Verification mapping

| Row | Evidence methods and cases |
|---|---|
| V-01 | RestartFailureClassificationTests.Infrastructure_wrappers_and_cancellation_are_classified_by_evidence: real 57P03, DbUpdateException, nested wrappers/aggregate, HTTP, timeout and unrequested cancellation; stale text/unknown SQL negative controls; typed missing. Separate transient DbException, connection-refused SocketException inner, and mixed native-missing/infrastructure aggregate cases also pass. |
| V-02 | StandingRestartAccountingTests.Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff: thresholds 0/1/2/int.MaxValue, three failures then resume same A. |
| V-03 | Async_infrastructure_failure_is_consumed_once_across_recreation and Early_running_infrastructure_evidence_survives_provider_rebuild_and_distinct_failed_generations: worker gates, cleanup, persisted cause, once per generation, full provider rebuild. |
| V-04 | Sync_failure_after_restamp_is_not_charged_again_by_terminal_observation; V-02 supplies pre-restamp comparison. |
| V-05 | Failed_outcome_save_recovers_as_unknown_without_fresh_authority: storage failure in outcome/bookkeeping, no invented evidence, restored terminal observation and retry. |
| V-06 | Observation_probe_timeout_and_requested_cancellation_authorize_no_attempt; Timeout_retries_but_requested_cancellation_does_not_charge (requested false/true); AgentSessionLaunchFailureTests.Launch_timeout_is_infrastructure_but_requested_cancellation_has_no_failure_outcome covers start/readiness gates x requested/unrequested cancellation, including cleanup. |
| V-07 | Real_failures_cap_escalate_and_reset_only_after_healthy_completion; An_old_start_timestamp_without_completed_boot_cannot_reset_failure_counters (Starting/Running); AgentSupervisionTests.Backoff_ladder_reaches_30_day_cap_and_escalates_once_per_tier. |
| V-08 | Default_start_distinguishes_first_launch_from_unavailable_prior_identity (first/missing/malformed/lost legacy pointer/incompatible); Unsupported_native_kind_keeps_ordinary_compatibility_but_requires_fresh_after_supported_history (supported false/true); Lost_pointer_preserves_supported_history_but_keeps_raw_only_compatibility (Raw/supported history x empty/malformed pointer). |
| V-09 | Missing_native_target_holds_after_one_resume_without_create; backend native missing matrix. |
| V-10 | Infrastructure_failure_with_stale_missing_text_does_not_hold_continuity; native Grok Found/Missing/Unavailable across PtyHost/Herdr; GrokNativeSessionResumeTests, GrokRulesResumeMigrationTests and runner/store classes. |
| V-11 | Held_retry_selection_and_fresh_have_separate_accepted_decisions (retry/selection/Fresh), failed selected target via real HTTP, An_unsuccessful_explicit_retry_restores_the_same_continuity_hold, automatic Start after failed explicit Fresh, and repeated hold suppression/native matrix. |
| V-12 | Recovery_options_cannot_bypass_existing_start_guards: 3 decisions x AlwaysOn on/off x five real guards; automatic/capacity callers separately; Missing_managed_credential_refuses_recovery_without_clearing_intent adds 3 decisions x AlwaysOn on/off using only a synthetic missing credential name. |
| V-13 | Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts: all four reasons, incident pruning, Stop, reconstructed Attention, accepted retry. This repository has no alert acknowledgement endpoint or acknowledged field; the unrelated synthetic infrastructure alert survives holds and accepted recovery unchanged. |
| V-14 | Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state: predecessor schema -> actual CLI migration with old counts 0/2/500; pointer/execution/incident/agreeing/conflicting/Parent-only/pool/card/worktree/no evidence. The added column is proven absent before predecessor-model seeding; the physical pg_indexes definition, model index, no ownership FK, no invented completion/outcome, and no pending model changes are checked. |
| V-15 | Deleting_and_recreating_the_same_name_does_not_adopt_historical_ownership; AgentAttachHerdrTests restamp cases owned/foreign stamp/conflicting legacy/pool/worktree; creation and recovery tests assert physical owner. |
| V-16 | Legacy_historical_owner_can_resume_after_pointer_moved (execution-only/incident-only), history read without stamp, accepted shared resume. Native A->B->A and current-composition methods cover actual flag/composition separately. |
| V-17 | Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects; reservation ownership change; corrupt source-owner queued input refuses both selection/Fresh. |
| V-18 | Invalid_or_busy_targets_refuse_before_reservation matrix (16 cases), options/404/unsupported tests, real HTTP Problem Details, canonical trailing separator positive case. |
| V-19 | Owned_history_uses_current_composition_and_native_resume_identity: Claude/Grok x AlwaysOn on/off, actual first completed rules-file launch, Fresh B, stopped A recovery with changed profile/model/env/bundles/instructions/backend. Native matrix checks repeat selection no extra request. |
| V-20 | History_and_start_preserve_wire_contract_and_revalidate_eligibility: guarded real Program, stable equal-time cursor, foreign exclusion, invalid option pairs, eligibility change, queued DTO then durable native-missing hold. |
| V-21 | Concurrent_starts_and_supervisor_reserve_one_generation: selection/selection, default, automatic and Fresh; real runner gate and bounded independent DB lock acquisition. |
| V-22 | Stop_supersedes_launch_before_spawn_and_before_typing (original post-ready case); Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation (before-spawn/start-RPC/saved-Running). New B is accepted after A releases worker ownership, with an asserted refusal while A still owns the worker. This is the conservative serialization allowed by the active-worker guard, not overlapping A/B launches. |
| V-23 | Reservation_rechecks_source_pointer_work_and_delivery_evidence (pointer/generation/execution/card/ownership/delivery); reservation rollback; two real queue-lock orderings. |
| V-24 | Only_unattempted_messages_move_atomically_and_keep_order_and_routing: UI/channel/delegation/scheduled fields/FIFO/holds and Sent/Canceled/rules exclusions. Native matrix confirms one moved channel input/reply. The same test then makes holds eligible, delivers the mixed-origin rows in order, and confirms each owning UserPrompt above its stored baseline; explicit initial input is submitted once. |
| V-25 | Any_prior_delivery_evidence_refuses_switch_and_fresh: nine independent evidence mutations x both decisions, mixed safe/unsafe queue, no partial moves; persisted scalar values are compared byte-for-byte before and after refusal. |
| V-26 | Target_late_confirmation_and_open_task_guards_remain_intact; Open_execution_on_either_history_or_current_target_refuses_selection (3 statuses x source/target/same-current), completed history/Parent-only child allowed. |
| V-27 | StandingSessionRecovery.test (7), attentionVisuals.test (13), AgentsPage.test (27): 47 passed; payloads, refusal, explicit Fresh, queued wording, disabled pending/dedupe/refetch. |
| V-28 | Standing_history_recovery_preserves_native_identity_and_queued_reply (Claude/Grok x PtyHost/Herdr x AlwaysOn on/off = 8); Standing_native_wire_missing_target_never_creates (missing 4, unavailable Grok 2). Real in-process runner and inbox PtyHost fake executables; Herdr uses fake pane with actual generated launch script and simulated native history. |

## Positive controls

All mutations restored. PC-1 has four red assertion failures and four green passes; each other PC has one red assertion failure and one green pass. Canonical files: c466-PC-1/2/3/4-*-v2.trx; PC-5/7-*-v8.trx; PC-6-*-v3.trx, under tests/Antiphon.Tests/bin-c466/TestResults. PC-5 v8 removes all eight post-ready authority checks as one deliberate defect. PC-7 clears continuity before guards as one deliberate defect. Red compile/discovery failures are not counted.

## Boundaries

Interrupted-startup replay, persisted launch extras/durable launch jobs, native Codex recovery, automatic adoption, landing/deployment and live production recovery are excluded. Reconstructed providers prove durable classification/holds only. Grok inline-to-rules migration retains its explicit Fresh barrier. Unsupported native kinds retain their ordinary creation compatibility and an audit event; explicit selection is refused. A lost pointer cannot erase known supported history.

Client production build: exit 0; Vite bundle-size warning only. Evidence .antiphon/c466-client-build-v10.log. Client 47/47: .antiphon/c466-client-audit-v10.log.

## Final per-class evidence

Each row selects the latest complete class run. Later test-only additions use v11; unchanged classes retain v10. An isolated post-audit wording check is additional evidence, not double-counted.

| Project | Class | Executed | Passed | Failed | Skipped | TRX (absolute) |
|---|---|---:|---:|---:|---:|---|
| Antiphon.Tests | RestartFailureClassificationTests | 2 | 2 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-RestartFailureClassificationTests.trx` |
| Antiphon.Tests | StandingRestartAccountingTests | 14 | 14 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-StandingRestartAccountingTests.trx` |
| Antiphon.Tests | StandingContinuityRecoveryTests | 11 | 11 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-StandingContinuityRecoveryTests.trx` |
| Antiphon.Tests | StandingContinuityAttentionTests | 4 | 4 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-StandingContinuityAttentionTests.trx` |
| Antiphon.Tests | StandingSessionOwnershipTests | 4 | 4 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-StandingSessionOwnershipTests.trx` |
| Antiphon.Tests | StandingSessionSelectionTests | 39 | 39 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-StandingSessionSelectionTests.trx` |
| Antiphon.Tests | StandingSessionRecoveryHttpTests | 1 | 1 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-StandingSessionRecoveryHttpTests.trx` |
| Antiphon.Tests | StandingSessionSwitchConcurrencyTests | 17 | 17 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-StandingSessionSwitchConcurrencyTests.trx` |
| Antiphon.Tests | StandingSessionQueueSwitchTests | 8 | 8 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-StandingSessionQueueSwitchTests.trx` |
| Antiphon.Tests | AttentionServiceTests | 122 | 122 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-AttentionServiceTests.trx` |
| Antiphon.Tests | AgentSupervisionTests | 10 | 10 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-AgentSupervisionTests.trx` |
| Antiphon.Tests | AgentControlServiceIntegrationTests | 31 | 31 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-AgentControlServiceIntegrationTests.trx` |
| Antiphon.Tests | GrokNativeSessionResumeTests | 6 | 6 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-GrokNativeSessionResumeTests.trx` |
| Antiphon.Tests | GrokRulesResumeMigrationTests | 6 | 6 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-GrokRulesResumeMigrationTests.trx` |
| Antiphon.Tests | AgentSessionLaunchFailureTests | 52 | 52 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-AgentSessionLaunchFailureTests.trx` |
| Antiphon.Tests | AgentSessionRuntimeTests | 17 | 17 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-AgentSessionRuntimeTests.trx` |
| Antiphon.Tests | SessionReconciliationServiceTests | 43 | 43 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-SessionReconciliationServiceTests.trx` |
| Antiphon.Tests | HerdrSupervisionBackoffTests | 35 | 35 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-HerdrSupervisionBackoffTests.trx` |
| Antiphon.Tests | HerdrSupervisionAttentionTests | 5 | 5 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-HerdrSupervisionAttentionTests.trx` |
| Antiphon.Tests | CapacityRecoverySupervisionTests | 6 | 6 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-CapacityRecoverySupervisionTests.trx` |
| Antiphon.Tests | PolicyRefreshServiceTests | 20 | 20 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-PolicyRefreshServiceTests.trx` |
| Antiphon.Tests | GrokRulesReadyOrderingTests | 2 | 2 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-GrokRulesReadyOrderingTests.trx` |
| Antiphon.Tests | SessionMessageQueueDeliveryVerificationTests | 104 | 104 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-SessionMessageQueueDeliveryVerificationTests.trx` |
| Antiphon.Tests | SessionMessageQueueInterruptedAttemptTests | 11 | 11 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-SessionMessageQueueInterruptedAttemptTests.trx` |
| Antiphon.Tests | SessionMessageQueueSupervisionTests | 3 | 3 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-SessionMessageQueueSupervisionTests.trx` |
| Antiphon.Tests | HerdrAlwaysOnChannelParityTests | 23 | 23 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v10-HerdrAlwaysOnChannelParityTests.trx` |
| Antiphon.Tests | AgentAttachHerdrTests | 13 | 13 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-audit-v11-AgentAttachHerdrTests.trx` |
| Antiphon.SessionRunner.Tests | HerdrGrokResumeGuardTests | 7 | 7 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.SessionRunner.Tests\bin-c466\TestResults\c466-audit-v11-HerdrGrokResumeGuardTests.trx` |
| Antiphon.SessionRunner.Tests | GrokRulesRunnerRefusalTests | 11 | 11 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.SessionRunner.Tests\bin-c466\TestResults\c466-audit-v11-GrokRulesRunnerRefusalTests.trx` |
| Antiphon.SessionRunner.Tests | GrokRulesStoreFailureTests | 6 | 6 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.SessionRunner.Tests\bin-c466\TestResults\c466-audit-v11-GrokRulesStoreFailureTests.trx` |
| Antiphon.SessionRunner.Tests | PromptSubmissionMatchTests | 31 | 31 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.SessionRunner.Tests\bin-c466\TestResults\c466-audit-v11-PromptSubmissionMatchTests.trx` |
| Antiphon.Agents.Pty.Tests | GrokNativeSessionStoreTests | 1 | 1 | 0 | 0 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Agents.Pty.Tests\bin-c466\TestResults\c466-audit-v11-GrokNativeSessionStoreTests.trx` |

## Positive-control results

| Control | Deliberate defect | Red failed/executed | Restored passed/executed | Evidence |
|---|---|---:|---:|---|
| PC-1 | Classify wrapped real 57P03 as an ordinary failure | 4/4 | 4/4 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-1-red-v2.trx`; `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-1-green-v2.trx` |
| PC-2 | Bypass consumed terminal-generation comparison | 1/1 | 1/1 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-2-red-v2.trx`; `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-2-green-v2.trx` |
| PC-3 | Restore same-ID create fallback after native missing | 1/1 | 1/1 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-3-red-v2.trx`; `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-3-green-v2.trx` |
| PC-4 | Ignore conflicting historical owners | 1/1 | 1/1 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-4-red-v2.trx`; `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-4-green-v2.trx` |
| PC-5 | Skip all post-ready authority checks | 1/1 | 1/1 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-5-red-v8.trx`; `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-5-green-v8.trx` |
| PC-6 | Ignore prior baseline/timestamp/verdict evidence | 1/1 | 1/1 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-6-red-v3.trx`; `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-6-green-v3.trx` |
| PC-7 | Clear continuity before Start guards accept | 1/1 | 1/1 | `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-7-red-v8.trx`; `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-PC-7-green-v8.trx` |

## Rerun and review

Use the class and project columns above with a new run identifier; no combined filter or stale TRX is counted as evidence:

```powershell
dotnet run --project tests/<Project> --property:OutputPath=bin-c466/ -- --treenode-filter "/*/*/<Class>/*" --report-trx --report-trx-filename "<new-run-id>-<Class>.trx"
pwsh -File scripts/test-client.ps1 StandingSessionRecovery.test attentionVisuals.test AgentsPage.test
npm --prefix client run build
```

Run Antiphon.Tests and Antiphon.Agents.Pty.Tests sequentially. The fixtures own isolated Testcontainers databases, fake adapters/providers, temporary native homes, in-process runners and fake gateways. Herdr native-wire evidence means actual runner requests/generated provider launch scripts plus the fake pane/native-store lane; it is not a live Herdr/provider canary. PtyHost arms launch the isolated fake executables through the inbox host.

Review the strict identity default, positive evidence classification, consumed generation, reservation/queue locks, durable hold, physical historical owner, and metadata-only history API. FreshAfterResumeFailures remains only as a deprecated compatibility property plus explicit-configuration startup warning; no operational read remains. Grok rules migration and independent Herdr/liveness/capacity holds retain their existing policies. Boot-reply liveness has its existing bounded intervention budget; it never grants Fresh authority.

The plan's alert-acknowledgement subcase has no applicable operation in this checkout: Alert has no acknowledgement state and there is no alert acknowledgement endpoint. The durable hold is independent of the alert table; Stop, incident pruning, service recreation and preservation of unrelated alerts are tested. Generic PATCH exposes no StandingAgentId mutation contract. Ownership can only be established by the server's validated paths; this change introduces no adoption API.

Earlier audit stops were fixture failures: the composition fake initially lacked valid rules receipt/prompt acknowledgement, and the pool attach fixture expected adoption where the ownership guard refuses it. Both are corrected and their full classes pass. No acceptance row remains unexecuted. No live migration, restart, deployment or production recovery was performed.

## Implementation commits and delivered files

Implementation starts at `d31a0761` and is completed by `114f568a` on `feat/card-task-e4eeea70`. Intermediate commits are `04296ee5`, `2bd25352`, `807e8a96`, `e0e38e66`, and `6a5e0b79`. The report itself is committed separately. Review the full implementation range `10cfbc63..114f568a`.

Core changes are `server/Application/Services/AgentControlService.cs`, `AgentSessionService.cs`, `AgentSupervisorService.cs`, `RestartFailurePolicy.cs`, `StandingContinuityState.cs`, `StandingSessionOwnership.cs`, and `StandingQueueSwitchPolicy.cs`; the domain/session/supervision models and `server/Migrations/20260909204339_StandingSessionContinuity.cs`; the Grok native-store/runner boundary; and `client/src/features/agents/StandingSessionRecovery.tsx` with AgentsPage and Attention integration. Owner documentation is updated in `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/antiphon-api.md`, and `docs/agent-kinds.md`. Tests and isolated fake-provider support are included in the commit range.

The final production change after the full class runs was only the ResumeUnsupported incident wording: ordinary unsupported creation is now labelled `new session (native resume unsupported)`, not `resume selection`. The focused compatibility recheck passed 2/2, zero failed/skipped: `C:\Antiphon\worktrees\card-task-e4eeea70\tests\Antiphon.Tests\bin-c466\TestResults\c466-unsupported-audit-v12.trx`. These two cases are already included in the 665-case class total and are not counted again.

All deliberate mutations are restored. Whitespace validation passed. The next stage is Review; there is no code-stage blocker or remaining acceptance command. Landing and deployment are separate caller actions.
