# CARD-0667 S4b repair — complete, next Review

The D-1 production guard and both fixture repairs are complete. Final CP-18, CP-22, CP-23 and CP-24 are green at one committed source SHA. AutomaticEnabled remains false; S4c and activation were not started.

## Identity and source

- Repair / Review subject: `b367debd-eb1f-4d2a-b230-3529f64daf62`.
- Original Code task / landing owner: `8eed758c-eec5-4f96-a7bc-fc97381b05b8`. The caller must land by adoption into that owner after Review; this repair does not become the landing owner.
- Branch: `feat/card-task-b367debd`.
- Worktree: `/work/worktrees/task-b367debd`. Assigned desktop mirror `C:\Antiphon\worktrees\card-task-b367debd` was not accessed.
- Task base: `7264ec74a1beefef4d51bcef793dc29fab848936`.
- Actual final tested implementation/test/plan SHA: `cbdda35787f6a75a5d765d2ff71f068967b02ddb`.
- This Markdown report is a subsequent report-only commit. Its pushed SHA and full-range evidence-diff result are supplied in the final response. Earlier receipts are not relabeled as testing that report commit.
- Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`.
- Final evidence: `/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-213845-3348/report.json` and `report.md`; fresh TRX files are `rows/CP-{18,22,23,24}/run.trx` beneath that directory.

Round: Final for the expressly commissioned S4b repair. The specific brief and plan exclude whole Unit, full assembly and S4c qualification. This is not card-wide final qualification. No ordinary verification remains for this repair.

## Implementation and verification design

`TerminalRunnerSeatReleaseService.OwnsAutomaticPath` now covers ordinary remote tasks regardless of workspace mode. Settlement and the pool-release sweep preserve intentional Shared pooling before invoking the conditional coordinator at the destructive retirement arm. An unpooled remote Shared task therefore cannot fall through to the ordinary stopper after a Working/unsupported hold when this dormant feature is enabled. The existing remote janitor interception also covers Shared tasks; intentional warm ownership remains a conditional-policy veto. Local release and SourceLanding custody retain their existing paths. There is no new unconditional stop, no widened timeout and no relaxed assertion.

Production files: `server/Application/Services/TerminalRunnerSeatReleaseService.cs`, `AgentTaskReplyService.cs`, and `AgentTaskDispatcher.cs`. The runtime owner document records the Shared guard. The plan retains CP-18 unchanged and adds CP-22 (new safety/compatibility cases), CP-23 (nine full affected server classes), and CP-24 (full runner class), including expected expanded counts, reuse and costs. These added rows implement this brief's repair scope, not S4c.

The plan and production settlement code are right about runner-sync Blocked reports: `NextStage=Decide`, `NextHandoff`, and a Warning event carry the explanation; `FailureReason` is not the contract. This fixture lacks `IRemoteSettlementSync`, so `TaskCompletionProgressService.PrepareAsync` returns `Unavailable` / `runner_sync_dependency_unavailable`. The corrected test asserts the exact handoff and exact Warning text. Both the explicit marked-Blocked and sync-Blocked variants now reach the existing physical release, stopped-row, retained custody/report/branch/workspace assertions.

The claim-after-inventory fixture now stamps runner `fixture` on both the session and task, seeds the intended Dispatched claimant, and requires the `Owned` disposition as well as zero release/force calls and retained rows. It no longer passes through a runner-identity mismatch or asserts Dispatched against a Working seed.

Tests changed: `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs` and `RunnerSlotEndpointTests.cs`. Six new argument cases cross settlement/pool sweep with Working, unsupported peer, and unsupported HTTP transport. Every case failed against unchanged production code on exactly one observed legacy stopper call. After the guard, each requires zero stopper/force/conditional commands, a persisted Working/Unsupported hold and retained session/agent. Two additional cases require actual remote Shared warm-pool status, timestamp and reservation with no wire calls or release debt. These are behavioral checks, not self-comparisons or constants standing in for execution.

## Final ordinary outcomes

All rows below used `cbdda35787f6a75a5d765d2ff71f068967b02ddb`, `dirty=0`, `sourceState=clean`, `buildSource=verified`, `slot=granted`, `waited=0s`. Checkpoint `validate` accepted the report for all four selected IDs: `CHECKPOINT SOURCE VALID source=cbdda35787f6a75a5d765d2ff71f068967b02ddb rows=4`. Fresh TRX identities, intended methods, per-class nonzero counts and zero failures/skips were inspected independently of exit status.

| Checkpoint | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-18: original eight S4b methods | 8 | 8 | 0 | 0 |
| CP-22: six D-1 variants plus two warm-pool cases | 8 | 8 | 0 | 0 |
| CP-23: full affected server classes | 733 | 733 | 0 | 0 |
| CP-24: full runner TerminalSeatReleaseTests | 38 | 38 | 0 | 0 |

787 final executions include overlap: CP-18 and CP-22 are also in the 733 full server results. The full classes contain 771 distinct expanded cases across the two assemblies.

| Full class | Passed / executed |
|---|---:|
| AgentTaskPoolTests | 48 / 48 |
| AgentTaskReplyIntegrationTests | 334 / 334 |
| AgentTaskServiceIntegrationTests | 120 / 120 |
| AttentionApiTests | 2 / 2 |
| AttentionServiceTests | 164 / 164 |
| RunnerSeatOrphanSweepTests | 17 / 17 |
| RunnerSlotEndpointTests | 8 / 8 |
| RunnerSlotRulesTests | 2 / 2 |
| TerminalRunnerSeatReleaseTests | 38 / 38 |
| TerminalSeatReleaseTests (runner) | 38 / 38 |

CP-18 method outcomes, each PASS: Working_session_keeps_ownership_and_visible_debt; Janitor_cannot_bypass_a_release_hold; Cancellation_reconciles_without_second_stop; Settlement_delivery_precedes_release_and_survives_release_fault; Parent_receipt_rejects_ack_stale_or_partial_prompt; Blocked_report_with_running_runner_frees_the_seat; Failed_settlement_releases_without_success_branch; Existing_job_discovers_debt_without_settlement_callback.

CP-22 variants, each PASS: Unpooled_remote_shared_release_never_uses_the_legacy_stopper(False, Working), (True, Working), (False, UnsupportedPeer), (True, UnsupportedPeer), (False, UnsupportedTransport), (True, UnsupportedTransport); Remote_shared_release_keeps_intentional_warm_pooling(False), (True).

| Plan ID | Actual outcome |
|---|---|
| V-1 | PASS: full runner class, 38/38, including Working and unsupported capability/transport guards. Fake-backed qualification, not live native process/provider proof. |
| V-2 | PASS: full terminal lifecycle class 38/38, CP-18 8/8, CP-22 8/8, plus all three full task pool/reply/service lifecycle classes. |
| V-3 | PASS: full orphan/discovery class 17/17, including the real existing job, callback-free recovery, and default-off composition. |
| R-1 | NOT RUN: classifier/capacity roster deferred to separate S4c qualification, as commissioned. |
| R-2 | PASS: endpoint class 8/8 and rules class 2/2. The repaired claim-race method passed and returned Owned. |
| R-3 | PASS: all four named existing compatibility methods confirmed individually in final TRX; also both new remote warm cases. |
| R-4 | NOT RUN: Windows qualification deferred to separate S4c commission. |

The four R-3 methods are a_settled_shared_delegate_goes_warm_instead_of_dying, a_failed_verdict_pools_a_shared_delegate_warm, a_users_standing_agent_is_never_pooled_or_deleted, and a_blocked_delegate_keeps_its_session_and_agent. Each executed once in CP-23 and passed. The existing Unsupported_server_transport_never_falls_back_to_force cases also ran in the full class, preserving HTTP 404/501 and old phone-home no-force checks.

Manual/source acceptance: default false inspected and its production-default composition exercised; no migration/model-snapshot or scheduler changes; no daemon restart, deployment or live-provider launch; no new Working-stop authority. The existing CARD-0079 exception was not changed or reused. Complete parent receipt, retained report/workspace and ordinary cancellation's single requested stop all passed their named methods. GET `/api/runner-defaults` and `/api/session-runners` succeeded before implementation (defaults revision 2); no fleet location, routing pin or platform override was added.

Deferred, never marked passed: S4c CP-1 through CP-6, R-1, R-4, separate activation acceptance, and every positive control below. Previously landed preparatory rows are historical, not rerun obligations for this repair.

## Run history, cost and provenance

1. `a16a32ea4b9656a11d8679d42572751e29b48b82`: test-only commit, unchanged production. Direct checkpoint row `S4b-D1-red` executed 6, passed 0, failed 6, skipped 0. All failures were the named `f.RecordedStops.Killed.ShouldBeEmpty` assertion, each observing exactly one stop. Build succeeded and source was clean/verified. This is ordinary proof of the existing defect, not a deliberate mutant or a PC cycle. Evidence: `.antiphon/c667-repair/S4b-D1-red-20261005-212106-99a9/` with build log, console, fresh TRX, and the unchanged tool-produced `checkpoint-build-source.json` preserved before output cleanup. The direct row tool emitted its CHECKPOINT line; it did not create a standalone source.json receipt.
2. Repair round 1, `48b15468a3417a06534b72bfa597ba48978d99cd`: CP-18 7/8, CP-22 8/8, CP-23 732/733, CP-24 38/38. The only failing method in both red rows was Blocked_report_with_running_runner_frees_the_seat: my new expected reason was branch mismatch while the fixture actually exercises missing sync dependency. The production guard, claim-race fixture and all other full-class cases passed. Evidence: `.antiphon/checkpoints/20261005-212538-0ff7/`. Wall time 12m35s.
3. Repair round 2, `cbdda35787f6a75a5d765d2ff71f068967b02ddb`: exact handoff/Warning expectation corrected to the real missing-dependency path, with the pending PC-14 variant mapping recorded. All required rows green together, as above. Evidence: `.antiphon/checkpoints/20261005-213845-3348/`. Wall time 12m46s. No timeout or assertion was loosened.

Only two repair rounds were used. No loaded repetitions, known-flaky reruns or post-green reassurance runs occurred. The final full rows were repeated only to meet the brief's requirement that every required row share the final committed source SHA after the expectation correction. Each unchanged selection stayed within the repetition budget.

Declared commands beyond the initial landed CP-18 row: (a) one checkpoint-tool bootstrap build because this worktree had no prepared tool, through `scripts/build-slot.ps1`, succeeded, granted/waited=0s; (b) the precise six-case D-1 red row explicitly required by this brief; (c) CP-22-24, added to the plan before repair verification to express the new cases and explicitly requested full affected classes. No full assembly, whole Unit or unrelated test run occurred. The checkpoint tool took the row/build slots itself; it was not nested inside another slot. All drivers were awaited. All grants had waited=0s. Each four-row run performed two actual builds (server output reused by three rows, separate runner output); the tool's displayed `builds: 22` counts the imported plan definitions, not 22 executed build commands. Maximum concurrent builds was one.

The first repair executor recovered one owner-read HTTP 502; no deadline changed. Final receipt validation passed. Every final TRX has the intended classes/methods and nonzero counts. Generated JSON, TRX, logs and checkpoint directories remain ignored. Only this individual Markdown report is added to Git.

Cleanup: checkpoint cleanup removed 37 product output directories for `bin-c667-s4b/` and `bin-c667-s4b-repair-runner/`; bounded exact-name cleanup removed 29 red-proof/bootstrap directories for `bin-c667-d1-red/` and `bin-c667-repair-tool/`. All owned alternate outputs were removed, and both checkpoint executors' shadow tools reported identity-dead cleanup. Evidence remains in the ignored roots above. Source worktree retained for Review/adoption.

To rerun after checking out the intended source, rebuild the tool through `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c667-repair-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-repair-tool/ --property:UseAppHost=false`; then run its DLL with `run --plan docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md --rows CP-18,CP-22,CP-23,CP-24 --expected-source-sha <committed HEAD> --keep-outputs --max-wait 50s`. Use the emitted wait command until exit is not 75. No further ordinary run is requested by this report.

Restart: none performed. The caller/orchestrator owns any later server restart after Review and adoption/landing; this repair changes no runner code and requires no runner restart. Feature activation remains a separate commission after S4c. Next is Review of this repair, then caller-owned adoption into original Code owner 8eed758c and post-land SourceLanding Mutation.

## Mutation remains pending

All PC-1 through PC-104 and every plan variant remain pending for method-scoped SourceLanding Mutation. Ordinary green, the real-defect red proof, and nightly do not discharge any control. The new PC-14 variants are settlement/sweep crossed with Working, unsupported peer and unsupported transport, in addition to the existing Failed/Succeeded/Blocked Worktree variants. Mutation owns deliberate mutants, red/restore/green and missing-control discovery, including coverage of the warm-pooling placement. The complete pending matrix from the authoritative plan follows after the unedited checkpoint records.

## Unedited checkpoint records

```text
CHECKPOINT S4b-D1-red commit=a16a32ea4b9656a11d8679d42572751e29b48b82 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/Unpooled_remote_shared_release_never_uses_the_legacy_stopper* executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/c667-repair/S4b-D1-red-20261005-212106-99a9/run.trx slot=granted waited=0s dirty=0 source=a16a32ea4b9656a11d8679d42572751e29b48b82 sourceState=clean buildSource=verified
```


Run 20261005-212538-0ff7

```text
CHECKPOINT CP-18 commit=48b15468a3417a06534b72bfa597ba48978d99cd build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Working_session_keeps_ownership_and_visible_debt*)|(Janitor_cannot_bypass_a_release_hold*)|(Cancellation_reconciles_without_second_stop*)|(Settlement_delivery_precedes_release_and_survives_release_fault*)|(Parent_receipt_rejects_ack_stale_or_partial_prompt*)|(Blocked_report_with_running_runner_frees_the_seat*)|(Failed_settlement_releases_without_success_branch*)|(Existing_job_discovers_debt_without_settlement_callback*) executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-212538-0ff7/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=48b15468a3417a06534b72bfa597ba48978d99cd sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=48b15468a3417a06534b72bfa597ba48978d99cd build=reused filter=/*/*/TerminalRunnerSeatReleaseTests*/(Unpooled_remote_shared_release_never_uses_the_legacy_stopper*)|(Remote_shared_release_keeps_intentional_warm_pooling*) executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-212538-0ff7/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=48b15468a3417a06534b72bfa597ba48978d99cd sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=48b15468a3417a06534b72bfa597ba48978d99cd build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)|(AttentionServiceTests*)|(AttentionApiTests*)|(RunnerSlotEndpointTests*)|(RunnerSlotRulesTests*)|(AgentTaskServiceIntegrationTests*)|(AgentTaskReplyIntegrationTests*)|(AgentTaskPoolTests*)/* executed=733 passed=732 failed=1 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-212538-0ff7/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=48b15468a3417a06534b72bfa597ba48978d99cd sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=48b15468a3417a06534b72bfa597ba48978d99cd build=ok filter=/*/*/TerminalSeatReleaseTests*/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-212538-0ff7/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=48b15468a3417a06534b72bfa597ba48978d99cd sourceState=clean buildSource=verified
```


Run 20261005-213845-3348

```text
CHECKPOINT CP-18 commit=cbdda35787f6a75a5d765d2ff71f068967b02ddb build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Working_session_keeps_ownership_and_visible_debt*)|(Janitor_cannot_bypass_a_release_hold*)|(Cancellation_reconciles_without_second_stop*)|(Settlement_delivery_precedes_release_and_survives_release_fault*)|(Parent_receipt_rejects_ack_stale_or_partial_prompt*)|(Blocked_report_with_running_runner_frees_the_seat*)|(Failed_settlement_releases_without_success_branch*)|(Existing_job_discovers_debt_without_settlement_callback*) executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-213845-3348/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=cbdda35787f6a75a5d765d2ff71f068967b02ddb sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=cbdda35787f6a75a5d765d2ff71f068967b02ddb build=reused filter=/*/*/TerminalRunnerSeatReleaseTests*/(Unpooled_remote_shared_release_never_uses_the_legacy_stopper*)|(Remote_shared_release_keeps_intentional_warm_pooling*) executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-213845-3348/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=cbdda35787f6a75a5d765d2ff71f068967b02ddb sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=cbdda35787f6a75a5d765d2ff71f068967b02ddb build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)|(AttentionServiceTests*)|(AttentionApiTests*)|(RunnerSlotEndpointTests*)|(RunnerSlotRulesTests*)|(AgentTaskServiceIntegrationTests*)|(AgentTaskReplyIntegrationTests*)|(AgentTaskPoolTests*)/* executed=733 passed=733 failed=0 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-213845-3348/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=cbdda35787f6a75a5d765d2ff71f068967b02ddb sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=cbdda35787f6a75a5d765d2ff71f068967b02ddb build=ok filter=/*/*/TerminalSeatReleaseTests*/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-b367debd/.antiphon/checkpoints/20261005-213845-3348/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=cbdda35787f6a75a5d765d2ff71f068967b02ddb sourceState=clean buildSource=verified
```

## Pending positive-control matrix

| PC | Deliberate defect (Mutation owns execution) | Detecting test and variants | Status |
|---|---|---|---|
| PC-1 | Break G-1: restrict the terminal predicate to Succeeded. | `TerminalRunnerSeatReleaseTests.Completed_attempt_registers_release_debt`: Failed/Canceled/Blocked ledger count is 1. | Pending |
| PC-2 | Break G-2: remove the CompletedAt requirement. | `TerminalRunnerSeatReleaseTests.Incomplete_settlements_never_authorize_release`: uncommitted and null-CompletedAt cases have 0 conditional commands. | Pending |
| PC-3 | Break G-3: accept Blocked with CompletedAt but no settlement/report. | `TerminalRunnerSeatReleaseTests.Incomplete_settlements_never_authorize_release`: routing-hold case has 0 commands and reason IncompleteReport. | Pending |
| PC-4 | Break G-4: omit the session-ID owner query arm. | `TerminalRunnerSeatReleaseTests.Unsettled_blocked_or_queued_owner_is_preserved`: same-session/different-agent owner has 0 commands. | Pending |
| PC-5 | Break G-5: omit the agent-ID owner query arm. | `TerminalRunnerSeatReleaseTests.Unsettled_blocked_or_queued_owner_is_preserved`: same-agent/different-session owner has 0 commands. | Pending |
| PC-6 | Break G-6: omit expected attempt from the conditional reservation predicate. | `RunnerSeatOrphanSweepTests.Claim_between_inventory_and_release_vetoes_action`: newer-attempt-only change reserves 0 rows. | Pending |
| PC-7 | Break G-7: omit the settlement identity/status comparison. | `RunnerSeatOrphanSweepTests.Claim_between_inventory_and_release_vetoes_action`: same-attempt revised settlement reserves 0 rows. | Pending |
| PC-8 | Break G-8: omit the non-pool owner exclusion. | `TerminalRunnerSeatReleaseTests.Standing_warm_and_verification_owners_are_preserved`: ordinary standing owner has 0 commands. | Pending |
| PC-9 | Break G-9: omit AlwaysOn from owner exclusion. | `TerminalRunnerSeatReleaseTests.Standing_warm_and_verification_owners_are_preserved`: otherwise releasable AlwaysOn pool seat has 0 commands. | Pending |
| PC-10 | Break G-10: omit BoardId from owner exclusion. | `TerminalRunnerSeatReleaseTests.Standing_warm_and_verification_owners_are_preserved`: otherwise releasable board-owned seat has 0 commands. | Pending |
| PC-11 | Break G-11: omit the specialist-role/owner exclusion. | `TerminalRunnerSeatReleaseTests.Standing_warm_and_verification_owners_are_preserved`: role-only and owner-only specialist cases have 0 commands. | Pending |
| PC-12 | Break G-12: treat the warm disposition as releasable. | `TerminalRunnerSeatReleaseTests.Standing_warm_and_verification_owners_are_preserved`: Shared warm case remains listed and occupied. | Pending |
| PC-13 | Break G-13: omit the SourceLanding exclusion. | `TerminalRunnerSeatReleaseTests.Standing_warm_and_verification_owners_are_preserved`: verification seat has 0 generic release commands. | Pending |
| PC-14 | Break G-14: route runner-bound ReleaseDelegateAsync or the pool-release sweep into the existing stopper arm. | `TerminalRunnerSeatReleaseTests.Working_session_keeps_ownership_and_visible_debt`: Failed/Succeeded/Blocked Working cases have 0 stopper calls. `TerminalRunnerSeatReleaseTests.Unpooled_remote_shared_release_never_uses_the_legacy_stopper`: settlement/sweep crossed with Working/unsupported-peer/unsupported-transport also have 0 stopper calls; all six variants remain pending Mutation. | Pending |
| PC-15 | Break G-15: retain the old remote-worktree janitor kill arm. | `TerminalRunnerSeatReleaseTests.Janitor_cannot_bypass_a_release_hold`: after TTL the held Working/Unknown seat has 0 stopper calls. | Pending |
| PC-16 | Break G-16: call the ordinary stopper again from the post-commit release hook. | `TerminalRunnerSeatReleaseTests.Cancellation_reconciles_without_second_stop`: operator cancellation causes exactly 1 requested stop. | Pending |
| PC-17 | Break G-17: ignore ExpectedRunnerStoreId while keeping generation validation. | `TerminalSeatReleaseTests.Replacement_generation_is_never_released`: store-only replacement returns GenerationMismatch and retains child. | Pending |
| PC-18 | Break G-18: remove accepted-generation comparison. | `TerminalSeatReleaseTests.Replacement_generation_is_never_released`: generation-only replacement returns GenerationMismatch. | Pending |
| PC-19 | Break G-19: look up a valid observation token without checking its session/object. | `TerminalSeatReleaseTests.Token_for_another_session_is_refused`: cross-session token returns StaleObservation and signals 0. | Pending |
| PC-20 | Break G-20: remove the runtime-epoch comparison from the production token authorization decision. | `TerminalSeatReleaseTests.Restart_invalidates_volatile_observation_tokens`: the production authorization decision rejects an otherwise valid old-epoch proof; end-to-end restart requires a new window. | Pending |
| PC-21 | Break G-21: use Snapshot() for the Claude fresh-observation implementation. | `TerminalSeatReleaseTests.Fresh_tail_reads_each_provider`: Claude unread prompt yields Working. | Pending |
| PC-22 | Break G-22: use Snapshot() for the Grok fresh-observation implementation. | `TerminalSeatReleaseTests.Fresh_tail_reads_each_provider`: Grok unread prompt/tool activity yields Working. | Pending |
| PC-23 | Break G-23: use Snapshot() for the Codex fresh-observation implementation. | `TerminalSeatReleaseTests.Fresh_tail_reads_each_provider`: Codex unread prompt/tool activity yields Working. | Pending |
| PC-24 | Break G-24: map unsuccessful fresh-read status to an eligible Idle observation. | `TerminalSeatReleaseTests.Unknown_or_partial_tail_never_authorizes_release`: each incomplete-read case returns Unknown before qualification. | Pending |
| PC-25 | Break G-25: omit the before/after binding and consumed-file identity comparison. | `TerminalSeatReleaseTests.Binding_changes_during_read_refuse_qualification`: revoked/replaced/truncated binding returns Unknown or StaleObservation. | Pending |
| PC-26 | Break G-26: omit the current prompt floor check. | `TerminalSeatReleaseTests.Old_turn_end_does_not_qualify_a_new_generation`: old-end-only observation is not qualified. | Pending |
| PC-27 | Break G-27: remove the Working arm from final authorization. | `TerminalSeatReleaseTests.Working_remains_protected_after_arbitrary_silence`: otherwise valid proof returns Working after 1 hour and signals 0. | Pending |
| PC-28 | Break G-28: ignore the committed server Working projection. | `TerminalRunnerSeatReleaseTests.Working_session_keeps_ownership_and_visible_debt`: runner Idle/server Working case sends 0 conditional commands. | Pending |
| PC-29 | Break G-29: set the stable observation minimum to zero. | `TerminalSeatReleaseTests.Two_observations_require_the_full_safety_margin`: 119.999-second decision is not qualified. | Pending |
| PC-30 | Break G-30: omit settlement age comparison. | `TerminalRunnerSeatReleaseTests.Settlement_age_has_its_own_safety_margin`: stable runner plus 119.999-second settlement sends 0 commands. | Pending |
| PC-31 | Break G-31: retain the first-observed instant when a revision changes. | `TerminalSeatReleaseTests.Activity_resets_the_qualification_window`: each single-component revision change resets qualified elapsed to 0. | Pending |
| PC-32 | Break G-32: leave prior qualification intact after Unknown/unavailable. | `TerminalSeatReleaseTests.Unavailable_observation_discards_qualification`: recovered observation at old t+120 remains unqualified. | Pending |
| PC-33 | Break G-33: ignore pending input/launch/adoption or unaccounted external custody in final authorization. | `TerminalSeatReleaseTests.Unknown_backend_custody_refuses_release`: otherwise eligible custody-uncertain cases signal 0. | Pending |
| PC-34 | Break G-34: remove the server pending-delivery decision. | `TerminalRunnerSeatReleaseTests.Pending_delivery_prevents_release`: each owed/attempted/held message case returns PendingDelivery and sends 0 commands. | Pending |
| PC-35 | Break G-35: remove only normal input's launch-gate acquisition, retaining its revision increment. | `TerminalSeatReleaseTests.Input_winning_the_gate_invalidates_release`: while a launch barrier owns the gate with no release marker, normal-input writer-entry count is 0. | Pending |
| PC-36 | Break G-36: omit the input revision increment after conditional input. | `TerminalSeatReleaseTests.Conditional_input_invalidates_release`: conditional-input-first outcome is StaleObservation and signals 0. | Pending |
| PC-37 | Break G-37: omit the release-in-progress branch from the production already-locked input authorization decision. | `TerminalSeatReleaseTests.Release_winning_the_gate_refuses_later_input`: production input decision is ReleaseInProgress before exit; both public input routes later refuse with 0 child writes. | Pending |
| PC-38 | Break G-38: reuse the earlier observation at the final reinspection. | `TerminalSeatReleaseTests.Tail_growth_at_final_check_refuses_signal`: native activity at the barrier causes signal count 0. | Pending |
| PC-39 | Break G-39: remove last pre-signal output-revision comparison. | `TerminalSeatReleaseTests.Output_growth_at_signal_boundary_refuses_release`: output-only growth at the signal barrier causes signal count 0. | Pending |
| PC-40 | Break G-40: forget tracked/durable custody after KillAsync without testing HasExited. | `TerminalSeatReleaseTests.Kill_failure_retains_manifest_and_capacity`: throw/cancel/non-exit retain manifest and occupied count. | Pending |
| PC-41 | Break G-41: cache action results by action ID alone and ignore generation on replay. | `TerminalSeatReleaseTests.Duplicate_action_is_idempotent`: same action on replacement returns GenerationMismatch; original signal count stays 1. | Pending |
| PC-42 | Break G-42: delete generation history with the session manifest. | `TerminalSeatReleaseTests.Confirmed_exit_forgets_only_the_expected_generation`: generation history remains readable after confirmed eviction. | Pending |
| PC-43 | Break G-43: skip pre-dispatch owner revalidation after reservation. | `RunnerSeatOrphanSweepTests.Claim_between_inventory_and_release_vetoes_action`: claim committed after reservation sends 0 commands. | Pending |
| PC-44 | Break G-44: skip response-time runner/store/session/generation and owner comparison. | `RunnerSeatOrphanSweepTests.Response_does_not_stop_a_replacement`: replacement row remains Running and owner remains unchanged. | Pending |
| PC-45 | Break G-45: replace conditional update with tracked SaveChanges and report success. | `TerminalRunnerSeatReleaseTests.Concurrent_reservations_have_one_winner`: two independent contexts produce exactly 1 reservation winner. | Pending |
| PC-46 | Break G-46: use a new SemaphoreSlim instead of queue.GetLock for release. | `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner`: answer at owned-gate barrier cannot enter old-session write or reserve concurrently. | Pending |
| PC-47 | Break G-47: invoke ReleaseSlotAsync when conditional operation is unsupported. | `TerminalSeatReleaseTests.Unsupported_capability_never_falls_back_to_force`: unconditional release and generation-kill counters are both 0. | Pending |
| PC-48 | Break G-48: treat unavailable/partial/pre-adoption inventory as AlreadyAbsent. | `RunnerSeatOrphanSweepTests.Missing_or_stale_runner_evidence_is_not_absence`: all non-authoritative cases retain pending debt and Running row. | Pending |
| PC-49 | Break G-49: send the conditional command before saving its reservation. | `RunnerSeatOrphanSweepTests.Reservation_precedes_the_runner_command`: fresh context at wire entry reads the exact persisted action ID. | Pending |
| PC-50 | Break G-50: re-send release immediately from pending recovery. | `RunnerSeatOrphanSweepTests.Lost_reply_reconciles_without_blind_second_kill`: mutation request count remains 1 after dropped successful reply. | Pending |
| PC-51 | Break G-51: mark release complete in memory and omit pending-result reconciliation. | `RunnerSeatOrphanSweepTests.Failed_audit_commit_recovers_stopped_row_and_attention`: restart yields Stopped row and exactly 1 committed release note. | Pending |
| PC-52 | Break G-52: remove the candidate budget break from discovery. | `RunnerSeatOrphanSweepTests.Sweep_budget_is_bounded_and_resumes_fairly`: one tick processes no more than its supplied budget. | Pending |
| PC-53 | Break G-53: return from the discovery loop on first candidate exception. | `RunnerSeatOrphanSweepTests.One_runner_failure_does_not_hide_other_candidates`: second runner/candidate has 1 confirmed release. | Pending |
| PC-54 | Break G-54: overwrite existing termination source/timestamps unconditionally. | `RunnerSeatOrphanSweepTests.Response_does_not_stop_a_replacement`: already-stopped row retains its original termination source and timestamps. | Pending |
| PC-55 | Break G-55: require a server AgentSession join before discovery. | `RunnerSeatOrphanSweepTests.Unknown_server_session_with_idle_runner_is_released`: local and phone-home rowless cases each confirm release without synthetic row. | Pending |
| PC-56 | Break G-56: key each discovery row by a new random ID without tuple upsert. | `RunnerSeatOrphanSweepTests.Discovery_is_idempotent_across_restart`: repeated/concurrent discovery leaves exactly 1 row and action per seat generation. | Pending |
| PC-57 | Break G-57: save the new attempt before saving accepted answer fields. | `TerminalRunnerSeatReleaseTests.Answer_recovery_preserves_round_and_admission_guards`: at the injected save cut no committed attempt lacks its exact answer. | Pending |
| PC-58 | Break G-58: omit the current round comparison. | `TerminalRunnerSeatReleaseTests.Answer_recovery_preserves_round_and_admission_guards`: stale-round reply returns conflict and changes 0 attempts/answer rows. | Pending |
| PC-59 | Break G-59: omit the accepted-answer revision comparison in requeue. | `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner`: concurrent accepted duplicate requests yield exactly 1 target attempt. | Pending |
| PC-60 | Break G-60: enqueue accepted answer to the old session while release is unresolved. | `TerminalRunnerSeatReleaseTests.Answer_racing_release_preserves_one_owner`: uncertain-release case has 0 old-session writes and durable answer text. | Pending |
| PC-61 | Break G-61: skip StopDelegateAsync whenever the server session row is missing. | `TerminalRunnerSeatReleaseTests.Answer_stop_bypass_requires_the_exact_release_receipt`: absent-row or mismatched receipt does not take the confirmed-release bypass. | Pending |
| PC-62 | Break G-62: skip quota/availability admission for released answers. | `TerminalRunnerSeatReleaseTests.Answer_admission_guards_preserve_the_accepted_reply`: active quota refusal retains answer and has 0 launch admissions. | Pending |
| PC-63 | Break G-63: admit released-answer dispatch without capacity check. | `TerminalRunnerSeatReleaseTests.Answer_admission_guards_preserve_the_accepted_reply`: capacity-full case stays queued and has 0 launch admissions. | Pending |
| PC-64 | Break G-64: bypass unresolved CommitRecoveryObligations for released answer. | `TerminalRunnerSeatReleaseTests.Answer_admission_guards_preserve_the_accepted_reply`: commit-recovery case retains obligation and answer; target not launched. | Pending |
| PC-65 | Break G-65: skip WorkspaceUseAdmission for released answer. | `TerminalRunnerSeatReleaseTests.Answer_admission_guards_preserve_the_accepted_reply`: foreign workspace-use claim refuses dispatch and preserves text. | Pending |
| PC-66 | Break G-66: replace refused preferred runner/kind with an available default. | `TerminalRunnerSeatReleaseTests.Answer_admission_guards_preserve_the_accepted_reply`: refused preference produces 0 alternate-kind/host launch admissions. | Pending |
| PC-67 | Break G-67: invoke dispatcher launch directly from the released-answer handler. | `TerminalRunnerSeatReleaseTests.Answer_admission_guards_preserve_the_accepted_reply`: before explicit dispatch, launch-call count is 0. | Pending |
| PC-68 | Break G-68: clip accepted answer to 4000 characters in BuildBrief. | `TerminalRunnerSeatReleaseTests.Long_answer_keeps_complete_content_and_spill_receipt`: recovered input file/inline body equals full Unicode answer including final canary. | Pending |
| PC-69 | Break G-69: remove target-attempt comparison in BuildBrief. | `TerminalRunnerSeatReleaseTests.Answer_fields_do_not_leak_into_a_later_attempt`: later explicit retry brief contains 0 stale-answer copies. | Pending |
| PC-70 | Break G-70: clear WorktreePath/BaseRef/report evidence before requeue. | `TerminalRunnerSeatReleaseTests.Answer_keeps_workspace_and_report_context`: same workspace/branch/report artifacts and reservation are retained. | Pending |
| PC-71 | Break G-71: start release before committing settlement/completion obligation. | `TerminalRunnerSeatReleaseTests.Settlement_delivery_precedes_release_and_survives_release_fault`: wire-entry fresh context contains terminal result and exact owed parent note. | Pending |
| PC-72 | Break G-72: clear accepted answer before queue insertion commits. | `TerminalRunnerSeatReleaseTests.Accepted_answer_after_release_is_delivered_once`: enqueue failure plus restart still yields exactly 1 complete target UserPrompt. | Pending |
| PC-73 | Break G-73: finish released-answer recovery on Sent/ack without recipient match. | `TerminalRunnerSeatReleaseTests.Answer_receipt_rejects_ack_stale_or_partial_prompt`: wrong/stale/partial/QueuedUserPrompt-only cases remain unconfirmed. | Pending |
| PC-74 | Break G-74: retire the parent completion obligation when queue status is Sent. | `TerminalRunnerSeatReleaseTests.Parent_receipt_rejects_ack_stale_or_partial_prompt`: wrong/stale/partial/QueuedUserPrompt-only cases retain ConfirmedAt null. | Pending |
| PC-75 | Break G-75: inner-join release projection to AgentSession/Agent. | `RunnerSeatOrphanSweepTests.Attention_recovery_deduplicates_rowless_seats_without_leaking_payload`: GET contains both distinct rowless session IDs. | Pending |
| PC-76 | Break G-76: group release attention by nullable AgentId. | `RunnerSeatOrphanSweepTests.Attention_recovery_deduplicates_rowless_seats_without_leaking_payload`: GET has exactly 2 distinct release condition keys for 2 rowless seats. | Pending |
| PC-77 | Break G-77: count deferred/Unresolved as Released in DTO projection. | `TerminalRunnerSeatReleaseTests.Attention_contains_release_identity_and_reason`: deferred-only sweep reports Released=0 and no released headline. | Pending |
| PC-78 | Break G-78: append raw observation path/transcript/answer to release diagnostic. | `RunnerSeatOrphanSweepTests.Attention_recovery_deduplicates_rowless_seats_without_leaking_payload`: ledger JSON, GET and captured structured logs exclude every synthetic canary. | Pending |
| PC-79 | Break G-79: mark invalidation delivered before publish and disable pending republish. | `RunnerSeatOrphanSweepTests.Attention_recovers_a_missed_invalidation_after_commit`: failed publication plus restart produces same-ID invalidation and GET row. | Pending |
| PC-80 | Break G-80: cancel recipient queue rows before testing release eligibility. | `TerminalRunnerSeatReleaseTests.Pending_delivery_prevents_release`: all message bodies/statuses/attempt evidence remain unchanged. | Pending |
| PC-81 | Break G-81: change PhoneHomeOperation.Input from explicit value 9 to unused value 28. | `TerminalSeatReleaseTests.Http_and_phone_home_share_conditional_semantics`: the pinned numeric assertion for PhoneHomeOperation.Input equals 9. | Pending |
| PC-82 | Break G-82: send parent report through immediate input instead of WhenIdle. | `TerminalRunnerSeatReleaseTests.Settlement_delivery_precedes_release_and_survives_release_fault`: busy parent has 0 writes before committed TurnEnd. | Pending |
| PC-83 | Break G-83: enqueue the new attempt brief with SendNow bypass. | `TerminalRunnerSeatReleaseTests.Accepted_answer_after_release_is_delivered_once`: busy target has 0 writes before committed TurnEnd. | Pending |
| PC-84 | Break G-84: publish freshly inspected rows through the hub before poll cursor ownership. | `TerminalSeatReleaseTests.Fresh_observation_does_not_publish_duplicate_entries`: observation adds 0 published entries; later poll publishes each row exactly once. | Pending |
| PC-85 | Break G-85: route every Blocked answer through released-seat requeue. | `TerminalRunnerSeatReleaseTests.Reply_on_live_local_or_warm_session_is_unchanged`: live/local/warm reply retains attempt/session and has 0 cold admissions. | Pending |
| PC-86 | Break G-86: catch Unsupported in the server coordinator and call unconditional ReleaseSlotAsync. | `TerminalRunnerSeatReleaseTests.Unsupported_server_transport_never_falls_back_to_force`: HTTP and phone-home old-peer cases call force/generation-kill 0 times. | Pending |
| PC-87 | Break G-87: remove operator-token validation from the release endpoints. | `RunnerSlotEndpointTests.Force_release_without_the_operator_token_is_forbidden_even_from_loopback`: missing/wrong token returns 403 for single-seat and sweep requests. | Pending |
| PC-88 | Break G-88: apply the 24-hour success cutoff to unresolved rows too. | `TerminalRunnerSeatReleaseTests.Attention_contains_release_identity_and_reason`: at 24h+1 tick unresolved debt remains visible while old success expires. | Pending |
| PC-89 | Break G-89: omit only normal input's revision increment while keeping its shared gate. | `TerminalSeatReleaseTests.Input_winning_the_gate_invalidates_release`: completed normal input invalidates prior proof with StaleObservation and signals 0. | Pending |
| PC-90 | Break G-90: remove only conditional input's launch-gate acquisition, retaining revision updates. | `TerminalSeatReleaseTests.Conditional_input_invalidates_release`: while a launch barrier owns the gate with no release marker, conditional-input writer-entry count is 0. | Pending |
| PC-91 | Break G-91: Move the initial native read after the backend body write. | `TerminalSeatReleaseTests.Native_delivery_floor_is_captured_before_backend_write`: writer-entry captured floor equals the prior revision, before immediate native output. | Pending |
| PC-92 | Break G-92: Recapture the floor on Enter-only retry. | `TerminalSeatReleaseTests.Native_delivery_floor_survives_body_enter_and_reenter`: capture ID and floor after re-Enter equal the first submission capture. | Pending |
| PC-93 | Break G-93: Leave a successfully prepared capture Submitted after a thrown backend write. | `TerminalSeatReleaseTests.Uncertain_input_never_publishes_delivery_evidence`: actual capture is Invalid after the write failure (assert before downstream custody masks it). | Pending |
| PC-94 | Break G-94: On a later Enter, repair a failed/unbound body capture from the now-bound idle file. | `TerminalSeatReleaseTests.Unbound_delivery_never_backfills_an_idle_floor`: late binding followed by Enter leaves capture unavailable; it cannot acquire a post-body baseline. | Pending |
| PC-95 | Break G-95: Remove the capture reset from accepted-generation rebind. | `TerminalSeatReleaseTests.Native_delivery_evidence_is_cleared_on_rebind`: actual current capture is empty immediately after rebind (before downstream identity checks). | Pending |
| PC-96 | Break G-96: Resolve the captured request with literal floor zero instead of its stored floor. | `TerminalSeatReleaseTests.Discovery_evidence_refuses_old_generation_idle_in_real_runtime`: old-end-only real runtime observation is OldPrompt at both clock instants, with zero signals. | Pending |
| PC-97 | Break G-97: When capture is missing, accept the caller-supplied binding/floor in captured mode. | `TerminalSeatReleaseTests.Captured_delivery_mode_requires_current_evidence`: forged supplied fields with missing runner capture still return Unknown/no token. | Pending |
| PC-98 | Break G-98: In PhoneHomeRuntimeAdapter.ObserveTerminalSeatAsync, pass the request with UseCapturedDeliveryEvidence=false (HTTP remains intact). | `TerminalSeatReleaseTests.Captured_delivery_mode_has_http_phone_home_parity`: phone-home captures the current delivery and returns Waiting then Qualified through the actual adapter. | Pending |
| PC-99 | Break G-99: Change the omitted request flag default to true. | `TerminalSeatReleaseTests.Legacy_floor_requests_keep_explicit_semantics`: old JSON with omitted flag retains supplied-floor Waiting/Qualified behavior. | Pending |
| PC-100 | Break G-100: Have the production automatic-request factory select legacy supplied-floor mode. | `RunnerSeatOrphanSweepTests.Discovery_request_uses_runner_owned_delivery_evidence`: actual emitted request has captured mode true and unusable empty/-1 caller evidence, with real native success. | Pending |
| PC-101 | Break G-101: Use a persisted qualified observation/token instead of calling the runner after recreating server services. | `RunnerSeatOrphanSweepTests.Server_restart_reacquires_runner_delivery_evidence`: recreated server makes a fresh observation; restarted runner remains held and receives no release. | Pending |
| PC-102 | Break G-102: Remove only the new delivery-evidence capability check, retaining the old release-capability check. | `RunnerSeatOrphanSweepTests.Evidence_missing_or_peer_unsupported_defers_discovery`: release-only peer receives zero captured-observation calls and returns Unsupported, with no fallback. | Pending |
| PC-103 | Break G-103: omit only capture-ID equality in the final production authorization decision. | `TerminalSeatReleaseTests.Captured_delivery_mode_requires_current_evidence`: with every other proof input valid, changed capture ID returns StaleObservation; end-to-end capture replacement also signals zero. | Pending |
| PC-104 | Break G-104: change TerminalRunnerSeatReleaseOptions.AutomaticEnabled default from false to true. | `RunnerSeatOrphanSweepTests.Existing_job_discovers_debt_without_settlement_callback`: production-default composition has zero new observation/reservation/command calls; explicit-true case still exercises the real hook and legacy reconciliation remains active. | Pending |
