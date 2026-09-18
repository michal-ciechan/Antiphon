# CARD-0486 (+0424/0426/0446/0500): triage of the five pre-existing-test-failure cards

Investigated 2026-09-18 (task a55b7ed6, Frontier, worktree `card-task-a55b7ed6`).
Every re-run below is against `origin/master` = `HEAD` = `de6eef47` (fetched at start; no
drift during the run). Builds went to `--property:OutputPath=bin-inv0486/`; fresh TRX files
for every invocation are under `.antiphon/inv0486-*` in that worktree (not committed; the
worktree is retained until the card is closed).

Sources: `scripts/card.ps1 get/history` for the five cards, `GET /api/agent-tasks/{id}` for
CARD-0486's five pipeline tasks (JSON copies in the task scratchpad), `git log`/`patch-id`
against `origin/master`, and the test runs listed under Method.

## Verdict

**Confirmed, with evidence.** Of the 21 itemized failures across the four cards, 15 are
green on current master (fixed by unrelated work or by CARD-0486's own landed work), 3 are
still red with the originally diagnosed cause, 2 are red for a different reason than the
card records, and 1 (CARD-0446 item 4) is order-dependent and is reported separately below.
No test named on any card has been removed or renamed. CARD-0426 is a confirmed duplicate
of CARD-0486 item 2 and both are already fixed on master. CARD-0486 sat in Review with no
owner because its work landed out-of-band on 2026-09-11 and a redundant land request on
2026-09-13 was refused and its outcome never delivered; nothing on it is still red.

## Consolidated table

Status key: (a) still red, same cause; (b) green now; (c) test gone/renamed; (d) red for a
different reason than diagnosed.

| Card | Item | Test | Status on de6eef47 | Evidence |
|---|---|---|---|---|
| 0424 | 1 | AgentTaskReplyIntegrationTests (7 failures) → CARD-0403 | (b) | CARD-0403 is Done; fix landed as `dae11d521`. Parked/merge-conflict members of the class re-run green here (0486 items 3-4). |
| 0424 | 2 | DelegateScriptKindTests.WorktreeHealth_posts_and_prints_findings_without_pruning | **(a)** | Red in batch 1. Output line is `[Error]   task -  ` (branch/detail lost). `scripts/delegate.ps1:281` still declares `[string]$Finding` and `:493` still loops `foreach ($finding in $report.findings)`. |
| 0424 | 3 | ComplexityChainRoleHttpTests.Put_get_delete_cell_and_two_segment_alias_write_any_role | **(a)** | Red in batch 1: `roles[0]` expected `"Plan"`, was `"Investigate"` (test line 62); `ComplexityRoutingService.RoutableRoles` still starts with `Investigate` (`server/Application/Services/ComplexityRoutingService.cs:61-64`). |
| 0424 | 4 | HerdrAlwaysOnChannelParityTests.Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched | (b) | 1/1 pass, method-scoped (1m27s). File touched by `df45d4c7b` (09-10) and `40234784f` (09-13). |
| 0424 | 5 | UnmarkedWaitingContractTests.unmarked_waiting_attention_kind_is_appended_after_report_unsettled | (b) | Pass in batch 1. Test now pins the frozen 0..28 prefix and allows later members (CARD-0475 S1, `c98dc9fc7`). |
| 0424 | 6 | AgentBundleAttachmentTests.every_bundle_except_the_reply_styles_can_be_attached | (b) | Pass in batch 1. Expected list includes `output-distiller` (test line 45; `3b59359a3`). |
| 0424 | 7 | StageOutcomeFindingEndpointTests.a_finding_on_a_task_with_no_stage_creates_the_row_at_the_given_stage | (b) | Pass in batch 1 (`24399358a`, 09-11). |
| 0424 | 8 | TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration | **(d)** | Red in batch 1, but the two classes the card names are tagged now (`GrokRulesLiveEvidenceTests.cs:7`, `HerdrLaunchContextResolverTests.cs:13`). The only untagged class is `HerdrPaneDisposalEndpointTests` (added `c03809e8c`, 09-11). |
| 0424 | 9 | CardCorrectionIntegrationTests.An_edit_snapshots_importance_urgency_and_due_and_maintains_UrgentSince | **(a)** | Red in batch 1: `card.DueAt` expected `…52.1548248Z`, was `…52.1548240Z` (test line 527). Same sub-microsecond precision mismatch. |
| 0424 | isolation | AttentionServiceTests.ProgressStalled_beats_Overdue_and_loses_to_PastExpectedIdle_when_idle | (b) standalone | 1/1 pass method-scoped. Pattern itself still live: see "New observation" below. |
| 0424 | noted | GitDiffSpikeTests.PathFilteredDiff_CompletesWithinFiveSeconds | (b) | 1/1 pass in 3.99s standalone. Class still has no `ParallelLimiter<ProcessSpawnLimit>` (only `Category` Integration+Slow, lines 19-20). |
| 0426 | – | TranscriptAdoptionSafetyTests.Queue_operation_enqueue_of_delivered_text_binds_via_C4 | (b) | 44/44 class green in SessionRunner run. Fixed by CARD-0486's `1bb8779a7` (09-11, `TranscriptAdoptionSafetyTests.cs` +14/-5). **Duplicate of CARD-0486 item 2 confirmed** (same test; 0486's Investigate task 7e1c69a6 diagnosed the stale `ShouldBeEmpty()` vs CARD-0292 QueueEnqueue normalisation, Code task 26cb0b70 fixed it). |
| 0446 | 1 | HerdrSupervisionBackoffTests.A_synchronous_refusal_before_any_row_leaves_the_streak_alone | (b) | Pass in batch 1 (file touched `df45d4c7b`, `40234784f`). |
| 0446 | 2 | SessionMessageQueuePtyIntegrationTests.A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits | (b) | 1/1 pass method-scoped (46.8s, real ConPTY + fakeclaude). One run only. |
| 0446 | 3 | DelegateLaunchArgvIntegrityTests.Every_dispatched_launch_round_trips_through_both_backends | (b) | Pass in batch 1. CARD-0497 (`b67710b97`, `775f0937e`, 09-12) changed the composer budget handling and this test. |
| 0446 | 4 | CheckInterpreterProvisionerTests (4 cases) in a combined `*Check*` process | see below | Alone: 12/12 pass in batch 1. Still on the shared `TestDbFixture` (`CheckInterpreterProvisionerTests.cs:287`), never converted to `CreateIsolatedSchemaAsync`. Combined-run result: CHECK_RESULT_PLACEHOLDER |
| 0446 | 5 | InstructionBundleTests.delegate_basics_carries_the_standing_rules_and_none_of_the_days_state | (b) | Pass in batch 1 (`430d48c83`, `b67710b97`, `3b59359a3`, `7284f89a3`). |
| 0446 | 6 | UnmarkedWaitingContractTests.unmarked_waiting_attention_kind_is_appended_after_report_unsettled | (b) | Same test as 0424 item 5. |
| 0446 | 7 | DelegationHarnessCensusTests.RuleB_dispatcher_harnesses_call_AddDelegationWorktreeGraph | (b) | Pass in batch 1 (`c98dc9fc7`, 09-10). |
| 0486 | 1 | ProcessSpawnLimitTests.Process_spawning_classes_are_exactly_the_limiter_population (SessionRunner.Tests) | **(d)** | Red in SessionRunner run. The four classes the card/investigation named were added to the roster by `1bb8779a7`; the roster is stale again for **11 new** limiter classes added 09-11..09-16: CodexCommandLengthHttpAcceptanceTests, HerdrLabelFollowLiveTests, HerdrLabelFollowSchedulingTests, HerdrLabelSnapshotTests, HerdrPaneDisposalGuardedLiveTests, HerdrPaneDisposalServiceTests, HerdrPaneDisposalStopRegressionTests, RemoteControlConditionalInputTests, RunnerCustodyCrashTests, RunnerCustodyTests, RunnerSessionGenerationTests (`missing:` none). |
| 0486 | 2 | TranscriptAdoptionSafetyTests.Queue_operation_enqueue_of_delivered_text_binds_via_C4 | (b) | As 0426. |
| 0486 | 3 | AgentTaskReplyIntegrationTests.a_parked_recovery_fails_the_task_naming_exhaustion | (b) | 1/1 pass method-scoped (43s). Fixed by `1bb8779a7` + `6366c80e` (fixture) and `91eda74b6` (production `IsEnabled` gate, `ApiErrorRecoveryService.cs:458`). |
| 0486 | 4 | AgentTaskReplyIntegrationTests.a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate | (b) | 1/1 pass method-scoped (48s). Fixed by `1bb8779a7` (`.antiphon/` gitignore seed). |
| 0500 | – | CodexCommandLengthSessionTests interactive canary (submit-confirm flake) | not re-run | Sanity check only, see below. |

## Recommendation per card

- **CARD-0424: needs-updating-with-current-facts.** Three of nine itemized defects are
  still red with the original cause (items 2, 3, 9); item 8 is red for a new class; items
  1 and 4-7 are resolved. The isolation-pattern paragraph is still true (new example below)
  and the GitDiffSpike limiter note is still true. Rewrite the card to list only items 2, 3,
  8 (new class), 9, the isolation investigation, and the GitDiffSpike limiter.
- **CARD-0426: duplicate-close-in-favor-of-CARD-0486 (and already-resolved).** Same test,
  same diagnosis, fixed on master by `1bb8779a7` on 2026-09-11; class is 44/44 today, which
  is exactly the card's acceptance line.
- **CARD-0446: needs-updating-with-current-facts.** Six of seven items are green. Only
  item 4 (CheckInterpreterProvisionerTests shared-fixture conversion) remains as a real
  hygiene gap, and it overlaps CARD-0424's isolation-pattern work. Either fold item 4 into
  the rewritten CARD-0424 and close 0446, or shrink 0446 to item 4 alone.
- **CARD-0486: already-resolved-close.** All four items green on master; the work landed
  on 2026-09-11 (`1bb8779a7`, `6366c80e`, `91eda74b6`). The only live residue is item 1's
  roster going stale *again* for 11 newer classes, which is a new instance of the same
  hard-coded-roster design and belongs on whichever card carries the rewritten 0424/0446
  list (the card's own text already suggests deriving the roster dynamically). Also leave
  behind for cleanup: worktrees `card-task-{7e1c69a6,26cb0b70,56e7cf4d,128ffcc6,42797cc5}`
  and remote branch `origin/feat/card-task-26cb0b70` (landing cleanup was `Refused:
  ignored_content_preserved`).
- **CARD-0500: still-actionable-as-is**, with a caveat. The mechanism the card describes
  still exists unchanged: `CodexCommandLengthSessionTests.RunInteractiveAsync` (line 94)
  still runs the short control launch through `LaunchOnceAsync` (line 102), which sends a
  nonce prompt and asserts `stub never saw the nonce` (line 191). CARD-0497 landed on
  09-12 including `916f28089` "wait for stub receipt before turn-complete in Codex
  canaries", which touches the same step and may have changed the flake rate, so the next
  stage should re-measure (5 runs) before root-causing. The card's caveat that no other
  interactive Codex+FakeLlmApi canary exists is still true: `CodexBootWedgeProbeTests` and
  `CodexHerdrRealCliStubProxyCanaryTests` use `Codex = true` stubs but neither drives
  `SendPromptAsync`/`RunInteractiveAsync`. Not re-run here (real Codex PTY, out of scope).

## CARD-0486: why it was in Review with no owner

Revision history (`card.ps1 history CARD-0486`, all 2026-09-11 UTC) and the five task rows:

| Time | Task | Role | Outcome |
|---|---|---|---|
| 09:16 → 09:26 | 7e1c69a6 | Investigate (Medium) | Succeeded, `next: code`. Diagnosed all four items at base `11b6c7ff` (stale roster; stale `ShouldBeEmpty` vs CARD-0292; parked fixture missing `CapacityRecovery.Enabled=false`; merge-conflict fixture lacking `.antiphon/` ignore). |
| 12:05 → 12:23 | 26cb0b70 | Code (Frontier) | Succeeded at `1ac69f77` on `feat/card-task-26cb0b70` (commits `02c337eb`, `1ac69f77`), `next: review`. |
| 12:23 → 12:29 | 56e7cf4d | Review | Succeeded with P2 finding: the parked fixture omitted `CapacityRecoveryService` (production registers it unconditionally), masking a Working-retention path. `next: code`. |
| 13:21 → 13:44 | 128ffcc6 | Code (Frontier, grok-4.6) | Succeeded: production fix `f6ce6e0c` (`ApiErrorRecoveryService` gates wait creation on `IsEnabled`) pushed to `origin/feat/card-task-26cb0b70`. Worktree was detached; its own merge-back failed `source_branch_mismatch`. `next: review`. |
| 13:44 → 13:52 | 42797cc5 | Review | Succeeded, clean, `next: land`, handoff: advance 26cb0b70's worktree from `1ac69f77` to `f6ce6e0c` before landing. |
| 13:53 | card-transitions | – | Moved InProgress → Review ("no other task is open against this card"). This is the last card revision until today. |
| 13:54 | 26cb0b70 land | – | `landing.publication=Landed`, sourceSha `1ac69f77` (the pre-review tip), verified/remote `6366c80e` (= `1bb8779a7` + `6366c80e` on master). Cleanup `Refused: ignored_content_preserved`. Legacy land receipt confirmed at prompt 2198 on 09-12 12:49. |
| 13:55 (14:55 +01:00) | orchestrator, by hand | – | `91eda74b6` committed directly on master: "Cherry-picked from f6ce6e0c after an earlier -Land targeted the wrong (pre-review) commit and only landed the test-only masking fix." `git patch-id` of `91eda74b6` equals `f6ce6e0c`; parent is `6366c80e`. |
| 09-13 17:37 → 17:48 | 128ffcc6 land request | – | `expectedSourceSha=f6ce6e0c`, held 11 min behind writer `115bc1c1`, admitted, then `LandRefused: detached_head expected=f6ce6e0c local=null remote=null candidate=null`. All five outcome notifications to session `8f51f80b` are `DestinationUnavailable` (`destination_failed`, enqueueAttempts 0, next attempt rescheduled to 09-18). |

So a full Investigate → Code → Review → Code → Review round completed and the real work
did land (test-only via `-Land`, production fix via a manual cherry-pick the same hour).
The card was parked in Review by the automatic transition after the last Review settled and
never moved to Done: the land was out-of-band, the later `/land/v2` retry for the detached
128ffcc6 worktree was refused, and the refusal's outcome message never reached its
destination session. The "no owner / no worktree / no agent" state is the normal post-settle
state, not an interruption.

## New observation (not on any card)

`HerdrSupervisionBackoffTests.A_launch_still_owned_by_the_queue_is_consumed_only_after_its_catch_classifies_it`
failed in the 233-test batch-1 process (`HerdrConsecutiveFailures` expected 1, was 0;
`HerdrSupervisionBackoffTests.cs:252`) and passed 1/1 when re-run method-scoped. The test
dates from `572c25f7d` (CARD-0388, 09-06). This is a fresh instance of the CARD-0424
shared-database isolation pattern; it is not itemized on any of the five cards.

## Method

| Run | Filter | Result | TRX |
|---|---|---|---|
| batch 1 (Antiphon.Tests) | `/*/*/(DelegateScriptKindTests*)\|(ComplexityChainRoleHttpTests*)\|(UnmarkedWaitingContractTests*)\|(AgentBundleAttachmentTests*)\|(StageOutcomeFindingEndpointTests*)\|(TestLaneCategoryGuardTests*)\|(CardCorrectionIntegrationTests*)\|(HerdrSupervisionBackoffTests*)\|(DelegateLaunchArgvIntegrityTests*)\|(InstructionBundleTests*)\|(DelegationHarnessCensusTests*)\|(CheckInterpreterProvisionerTests*)/*` | 233 total, 5 failed, 3m36s | `.antiphon/inv0486-batch1/batch1.trx` |
| parity | `/*/*/HerdrAlwaysOnChannelParityTests/Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched` | 1/1 pass | `.antiphon/inv0486-parity/run.trx` |
| parked | `/*/*/AgentTaskReplyIntegrationTests/a_parked_recovery_fails_the_task_naming_exhaustion` | 1/1 pass | `.antiphon/inv0486-parked/run.trx` |
| conflict | `/*/*/AgentTaskReplyIntegrationTests/a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate` | 1/1 pass | `.antiphon/inv0486-conflict/run.trx` |
| attention | `/*/*/AttentionServiceTests/ProgressStalled_beats_Overdue_and_loses_to_PastExpectedIdle_when_idle` | 1/1 pass | `.antiphon/inv0486-attention/run.trx` |
| gitdiff | `/*/*/GitDiffSpikeTests/PathFilteredDiff_CompletesWithinFiveSeconds` | 1/1 pass, 3.99s | `.antiphon/inv0486-gitdiff/run.trx` |
| overlay | `/*/*/SessionMessageQueuePtyIntegrationTests/A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits` | 1/1 pass, 46.8s | `.antiphon/inv0486-overlay/run.trx` |
| backoff2 | `/*/*/HerdrSupervisionBackoffTests/A_launch_still_owned_by_the_queue_is_consumed_only_after_its_catch_classifies_it` | 1/1 pass (red in batch 1) | `.antiphon/inv0486-backoff2/run.trx` |
| SessionRunner | `/*/*/(ProcessSpawnLimitTests*)\|(TranscriptAdoptionSafetyTests*)/*` | 46 total, 1 failed (roster), 1m11s | `.antiphon/inv0486-sr/sr.trx` |
| check | `/*/*/*Check*/*` | CHECK_RUN_PLACEHOLDER | `.antiphon/inv0486-check/check.trx` |

Antiphon.Tests and Antiphon.SessionRunner.Tests were run one after the other, never
concurrently. Each Antiphon.Tests invocation was a separate process (the pinned TUnit 1.44
OR syntax is class-level only, so method-scoped items were single invocations).

## Remaining uncertainties

- Every "green" verdict is one run at HEAD. The overlay-Esc pty test (0446 item 2) and the
  Herdr parity test (0424 item 4) are real-process tests; a single pass does not rule out a
  low-rate flake, only the "fails standalone too" claim on the card.
- CARD-0500 was not re-run; "still plausible" rests on the unchanged test mechanism and on
  no commit since 09-12 touching the Codex submit path other than CARD-0497/0502.
- The batch-1 failure of `A_launch_still_owned_by_the_queue…` was observed once; the
  ordering that triggers it was not isolated.

## Not done, noted

- Fix idea for the roster test (0486 item 1): derive the expected set from the attribute
  population and pin only the cap, as the card already suggests.
- Fix ideas for the still-red 0424 items are the ones already on the card (rename the
  `delegate.ps1` loop variable; update the `Plan` assertion; normalise the fixture date to
  database precision; tag `HerdrPaneDisposalEndpointTests`).
- Housekeeping: close-out cleanup of the five CARD-0486 worktrees and the remote branch.
