# CARD-0388 Code verification

Implemented and verified S1-S5: 509 .NET tests and 39 client tests passed; all 14 positive controls (16 variants) failed as intended and passed after restoration. Cleanup is blocked by automatic tool review; deployment and live ownership repair remain operator work. Built on landed CARD-0384 (`3b9696cd`, confirmed ancestor of this branch). Code commits: `572c25f7`, `6d468ba8`, `3c8398a1`; subsequent verification-only updates are in this branch history.

## Behavior and boundaries

Three consumed terminal Herdr attempts in the PaneClosed/ChildGone/DetectTimeout family now produce a durable hold. Explicit Start acknowledgement is the only release; ordinary guards still apply. State-backed Attention, the retry UI, and channel-drop handling are included. No runner protocol or allocator change was made; the in-process test client now maps launch exceptions like HTTP.

The migration was regenerated with EF CLI against the landed model: `20260906090842_AddHerdrSupervisionHold`, seven added columns (six state, one session evidence), no destructive Up operation. Historical evidence remains null. Apply migrations and verify the actual loaded server and named-placement runner versions during deployment from the main checkout. This Code stage did not deploy, alter a live owner/binding, or send a live channel message. Historical September 5 causation remains unconfirmed; live repair and any channel handover still require the documented operator procedure.

## Final forced runs

| Project/class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Antiphon.Tests/HerdrSupervisionFailureEvidenceTests | 24 | 0 | 0 |
| Antiphon.SessionRunner.Tests/HerdrLaunchShapeTests | 35 | 0 | 0 |
| Antiphon.Tests/AgentSessionRuntimeTests | 17 | 0 | 0 |
| Antiphon.Tests/SessionReconciliationServiceTests | 43 | 0 | 0 |
| Antiphon.Tests/AgentSessionLaunchFailureTests | 43 | 0 | 0 |
| Antiphon.Tests/AgentControlServiceIntegrationTests | 31 | 0 | 0 |
| Antiphon.Tests/HerdrSupervisionBackoffTests | 35 | 0 | 0 |
| Antiphon.Tests/AgentSupervisionTests | 10 | 0 | 0 |
| Antiphon.Tests/AgentAttachHerdrTests | 9 | 0 | 0 |
| Antiphon.Tests/ChannelBridgeTests | 40 | 0 | 0 |
| Antiphon.Tests/HerdrSupervisionAttentionTests | 5 | 0 | 0 |
| Antiphon.Tests/AttentionServiceTests | 122 | 0 | 0 |
| Antiphon.Tests/GrokRulesLaunchRefusalTests | 5 | 0 | 0 |
| Antiphon.Tests/InstructionFileStampTests | 15 | 0 | 0 |
| Antiphon.Tests/HerdrAlwaysOnChannelParityTests | 9 | 0 | 0 |
| Antiphon.Tests/HerdrPlacementPreflightTests | 7 | 0 | 0 |
| Antiphon.SessionRunner.Tests/HerdrAdoptionSweepTests | 22 | 0 | 0 |
| Antiphon.SessionRunner.Tests/GrokRulesRunnerRefusalTests | 11 | 0 | 0 |
| Antiphon.SessionRunner.Tests/GrokRulesArgvPolicyTests | 26 | 0 | 0 |

Total: 509 .NET passes. Client: AgentsPage 27/27 and attentionVisuals 12/12. `npm run build` passed; Vite emitted its existing large-chunk advisory.

Rerun server classes individually with `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c388/ -- --treenode-filter "/*/*/<Class>/*"`; runner classes use `tests/Antiphon.SessionRunner.Tests`. Run assemblies sequentially. Client: `pwsh -NoProfile -File scripts/test-client.ps1 AgentsPage.test attentionVisuals.test --maxWorkers 1`; build from `client` with `npm run build`. Remove verified workspace-local `bin-c388` outputs afterwards.

## Verification design coverage

| Design | Evidence |
|---|---|
| V1-V3 | Classification/settings/EF model plus exact migration Up operations in HerdrSupervisionFailureEvidenceTests. |
| V4-V7 | Runtime, launch-catch and reconciliation writers, typed timeout preservation, unknown absence, intentional-stop exclusion, missing-resume fallback and new-generation clearing. |
| V8-V19 | Mixed three-failure hold, unchanged general ladder/fresh policy, limits 1/10, microsecond dedupe, repeated writers, same-cwd sibling, null/nonqualifying distinction, queue ownership, negative scope, persisted hold and concurrent tick/Start in HerdrSupervisionBackoffTests. |
| V20-V24 | Observed-running health window (also across restart), new-generation clearing, first-guard ordering including zero named preflight calls, durable retry before model refusal, re-hold after three new attempts, backend/AlwaysOn/Stop/live-Start/attach preservation. |
| V25-V27 | Two held inbound messages yield one incident, no new session/input/reply/queue, existing drop alert, state-backed Attention beyond 48h/pruning/AlwaysOn-off, DTO and client flag/visual coverage. |
| V28-V30 | All three assembled named-placement cases in HerdrAlwaysOnChannelParityTests: real event-pump liveness verification, kept timeout pane/hint and no subsequent pane mutations, live specialist replay control, same-pane explicit retry, zero-RPC unsafe Grok refusal, and pre-row versus asynchronous occupancy refusal. |

V28 labels the already-running unpinned specialist tab MavRef-DL; V30 uses the landed SeedTab fixture. Named seat and specialist share the workspace and remain independently supervised. The fake process probe reports the removed child dead, so the real liveness verifier produces PaneClosed rather than the orphan-process RestartPresumedDead classification.

R1-R16 are covered by the corresponding V cases and forced existing regression classes. PC-2 uses an after-commit interceptor and supervisor log assertion to isolate the due-handoff recheck from the separate Start hold gate. PC-3 exercises the shared latch helper through attach, which reaches that helper while held. PC-13 injects a forbidden runner KillAsync call in the held branch and asserts zero destructive runner RPCs (the destructive runner RPC boundary; the server has no raw pane client); the failed session is no longer in the runner, so the mutation catches its not-found response to reach the assertion. No production kill/close was performed.

## Positive controls: actual red assertions, restored green

Each mutation was applied separately, its targeted test run red, original bytes restored, then the same test run green. PC-6 and PC-10 each have two variants. Full logs remain in `.antiphon/c388/`; assertion excerpts below are retained in Git.

| Control | Red exit | Restored exit |
|---|---:|---:|
| PC-1 | 2 | 0 |
| PC-2 | 2 | 0 |
| PC-3 | 2 | 0 |
| PC-4 | 2 | 0 |
| PC-5 | 2 | 0 |
| PC-6a | 2 | 0 |
| PC-6b | 2 | 0 |
| PC-7 | 2 | 0 |
| PC-8 | 2 | 0 |
| PC-9 | 2 | 0 |
| PC-10a | 2 | 0 |
| PC-10b | 2 | 0 |
| PC-11 | 2 | 0 |
| PC-12 | 2 | 0 |
| PC-13 | 2 | 0 |
| PC-14 | 2 | 0 |

### PC-1

`HerdrSupervisionBackoffTests/Held_start_without_the_flag_is_409_before_latch_clear_composition_and_the_model_gate`

```text
failed Held_start_without_the_flag_is_409_before_latch_clear_composition_and_the_model_gate (3s 413ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: f.Harness.Runner.CheckCalls.Count
      should be
  0
      but was
  1
```

Restored result: exit 0.

### PC-2

`HerdrSupervisionBackoffTests/Hold_committed_at_due_handoff_is_rechecked_before_Start_is_invoked`

```text
failed Hold_committed_at_due_handoff_is_rechecked_before_Start_is_invoked (6s 683ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: f.Harness.SupervisorLog
      should not contain an element satisfying the condition
  s.Contains("supervised restart attempt")
      but does
```

Restored result: exit 0.

### PC-3

`AgentAttachHerdrTests/Attach_binds_the_native_id_running_with_origin_attached`

```text
failed Attach_binds_the_native_id_running_with_origin_attached(True) (411ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: state.HerdrFailureHeldAt
      should not be null but was
```

Restored result: exit 0.

### PC-4

`AgentSessionLaunchFailureTests/A_runner_detect_timeout_409_stamps_DetectTimeout_and_keeps_the_runner_prose`

```text
failed A_runner_detect_timeout_409_stamps_DetectTimeout_and_keeps_the_runner_prose (4s 016ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: row.HerdrSupervisionFailureKind
      should be
  HerdrSupervisionFailureKind.DetectTimeout
      but was
  HerdrSupervisionFailureKind.NonQualifying
```

Restored result: exit 0.

### PC-5

`AgentSessionRuntimeTests/A_pane_closed_exit_after_DetectTimeout_evidence_keeps_DetectTimeout`

```text
failed A_pane_closed_exit_after_DetectTimeout_evidence_keeps_DetectTimeout (1s 721ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await db.AgentSessions.SingleAsync(s => s.Id == sessionId)).HerdrSupervisionFailureKind
      should be
  HerdrSupervisionFailureKind.DetectTimeout
      but was
  HerdrSupervisionFailureKind.PaneClosed
```

Restored result: exit 0.

### PC-6a

`HerdrSupervisionBackoffTests/Same_row_resumed_twice_and_a_fresh_row_count_three_and_a_sibling_agent_stays_at_zero`

```text
failed Same_row_resumed_twice_and_a_fresh_row_count_three_and_a_sibling_agent_stays_at_zero (3s 724ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await f.StateAsync()).HerdrConsecutiveFailures
      should be
  2
      but was
  1
```

Restored result: exit 0.

### PC-6b

`HerdrSupervisionBackoffTests/Dedupe_pair_survives_the_timestamp_round_trip`

```text
failed Dedupe_pair_survives_the_timestamp_round_trip (3s 899ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await f.StateAsync()).HerdrConsecutiveFailures
      should be
  2
      but was
  3
```

Restored result: exit 0.

### PC-7

`HerdrSupervisionBackoffTests/Same_row_resumed_twice_and_a_fresh_row_count_three_and_a_sibling_agent_stays_at_zero`

```text
failed Same_row_resumed_twice_and_a_fresh_row_count_three_and_a_sibling_agent_stays_at_zero (3s 698ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await f.SessionAsync()).HerdrSupervisionFailureKind
      should be null but was
  HerdrSupervisionFailureKind.PaneClosed
```

Restored result: exit 0.

### PC-8

`HerdrSupervisionAttentionTests/The_row_survives_the_lookback_incident_pruning_and_always_on_off`

```text
failed The_row_survives_the_lookback_incident_pruning_and_always_on_off (1s 711ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Items.Count(i => i.AgentId == agent && i.Kind == AttentionKind.HerdrSupervisionHeld)
      should be
  1
      but was
  0
```

Restored result: exit 0.

### PC-9

`HerdrSupervisionBackoffTests/A_launch_still_owned_by_the_queue_is_consumed_only_after_its_catch_classifies_it`

```text
failed A_launch_still_owned_by_the_queue_is_consumed_only_after_its_catch_classifies_it (3s 116ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await f.StateAsync()).HerdrConsecutiveFailures
      should be
  1
      but was
  0
```

Restored result: exit 0.

### PC-10a

`HerdrSupervisionBackoffTests/A_non_qualifying_terminal_attempt_resets_the_streak`

```text
failed A_non_qualifying_terminal_attempt_resets_the_streak (3s 063ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await f.StateAsync()).HerdrConsecutiveFailures
      should be
  0
      but was
  2
```

Restored result: exit 0.

### PC-10b

`HerdrSupervisionBackoffTests/A_synchronous_refusal_before_any_row_leaves_the_streak_alone`

```text
failed A_synchronous_refusal_before_any_row_leaves_the_streak_alone (2s 900ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await f.StateAsync()).HerdrConsecutiveFailures
      should be
  2
      but was
  0
```

Restored result: exit 0.

### PC-11

`ChannelBridgeTests/Herdr_held_agent_inbound_is_dropped_with_one_incident_and_no_start_notice_or_reroute`

```text
failed Herdr_held_agent_inbound_is_dropped_with_one_incident_and_no_start_notice_or_reroute (2s 753ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: `bridge.HandleInboundAsync( TelegramText(chatId, "held message " + i, title: "Family"), CancellationToken.None)`
      should not throw but threw
  Antiphon.Server.Application.Exceptions.ConflictException
      with message
  "Herdr retries paused for BridgeQueue: 3 of 3 attempts failed (DetectTimeout); inspect, fix, then Retry and resume; explicitly send resetHerdrFailureHold:true."
```

Restored result: exit 0.

### PC-12

`HerdrSupervisionBackoffTests/Sixty_seconds_of_Starting_contributes_no_healthy_time`

```text
failed Sixty_seconds_of_Starting_contributes_no_healthy_time (2s 756ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: (await f.StateAsync()).HerdrHealthySince!.Value
      should be greater than or equal to
  2026-09-06T09:40:54.9217877Z
      but was
  2026-09-06T09:38:54.1502120Z
```

Restored result: exit 0.

### PC-13

`HerdrAlwaysOnChannelParityTests/Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry`

```text
failed Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry (5s 815ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: h.Runner.KillCalls
      should be
  0
      but was
  4
  
  Additional Info:
      the hold must issue no destructive runner RPC
```

Restored result: exit 0.

### PC-14

`HerdrSupervisionFailureEvidenceTests/HerdrFailureLimit_outside_1_to_10_fails_validation`

```text
failed HerdrFailureLimit_outside_1_to_10_fails_validation(0) (29ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] ShouldAssertException: result.Failed
      should be
  True
      but was
  False
```

Restored result: exit 0.

## Failures resolved during execution

Initial new-test fixture errors (bridge harness, canonical model alias, fake process liveness, and waiting for Running on an intentionally Failed attempt) were corrected and rerun. Two test-only type compilation errors were corrected. After the CARD-0384 rebase, three existing AgentsPage exact-payload assertions lacked the new null placement fields and explicit PtyHost backend; corrected assertions passed 27/27. A multi-worker Vitest startup failure was rerun successfully with one worker. The first final-run PowerShell wrapper also splatted a scalar `--no-build` into characters (exit 5 before tests); its array typing was corrected and every affected class rerun. These are recorded as failures followed by successful reruns, not dismissed as flakes.

No unresolved final test failures. Automatic approval review rejected deleting the ignored temporary `.antiphon/c388/channel-hang.dmp` diagnostic dump, stating only `blocked by policy`; that file remains local and is not in Git. The same automatic review also rejected cleanup of 20 verified worktree-local build-output directories (`bin-c388` and `bin/c388`); these ignored outputs remain and require cleanup outside this blocked tool action. No production ownership choice was made. The owner docs explain inspecting current channel bindings and transcript identity before any live handover.


## Item-by-item V/R result

| Item | Assertion area | Passing evidence |
|---|---|---|
| V-1 | Classification | HerdrSupervisionFailureEvidenceTests |
| V-2 | Limits/default | HerdrSupervisionFailureEvidenceTests |
| V-3 | EF model and additive migration | HerdrSupervisionFailureEvidenceTests |
| V-4 | Runtime exit writer | AgentSessionRuntimeTests |
| V-5 | Launch-catch/fallback writer | AgentSessionLaunchFailureTests |
| V-6 | Reconciliation writer and timeout precedence | SessionReconciliationServiceTests |
| V-7 | Resume/fresh clear evidence | AgentControlServiceIntegrationTests + HerdrSupervisionBackoffTests |
| V-8 | Three mixed failures, no fourth launch | HerdrSupervisionBackoffTests |
| V-9 | Existing backoff/fresh ladder | HerdrSupervisionBackoffTests + AgentSupervisionTests |
| V-10 | Thresholds 1 and 10 | HerdrSupervisionBackoffTests |
| V-11 | Three observers consume once | HerdrSupervisionBackoffTests |
| V-12 | Same-row generation/fresh-row/sibling isolation | HerdrSupervisionBackoffTests |
| V-13 | Microsecond round trip | HerdrSupervisionBackoffTests |
| V-14 | Null and pre-row refusal neutrality | HerdrSupervisionBackoffTests |
| V-15 | Launch queue ownership | HerdrSupervisionBackoffTests |
| V-16 | Card/pool/PtyHost/nonqualifying exclusions | HerdrSupervisionBackoffTests |
| V-17 | Server restart persistence | HerdrSupervisionBackoffTests |
| V-18 | Committed hold due-restart protection | HerdrSupervisionBackoffTests |
| V-19 | Concurrent tick/Start locking | HerdrSupervisionBackoffTests |
| V-20 | Observed Running reset and generation clearing | HerdrSupervisionBackoffTests |
| V-21 | First Start gate including named preflight | HerdrSupervisionBackoffTests |
| V-22 | Explicit retry and re-hold; acknowledgement before refusals | HerdrSupervisionBackoffTests |
| V-23 | Hold survives settings/Stop/live Start | HerdrSupervisionBackoffTests |
| V-24 | Attach preserves hold | AgentAttachHerdrTests |
| V-25 | Held bridge input | ChannelBridgeTests |
| V-26 | State-backed Attention/DTO | HerdrSupervisionAttentionTests + AttentionServiceTests |
| V-27 | Retry UI and visuals | AgentsPage.test + attentionVisuals.test |
| V-28 | Named seat, three failures, retained pane, repaired retry | HerdrAlwaysOnChannelParityTests |
| V-29 | Unsafe Grok rules refuse before RPC/row | HerdrAlwaysOnChannelParityTests + GrokRulesLaunchRefusalTests |
| V-30 | Preflight vs asynchronous occupancy refusal | HerdrAlwaysOnChannelParityTests + HerdrPlacementPreflightTests |
| R-1 | Hold gates | V8/V21; PC1/PC2 |
| R-2 | Automatic callers/shared latch preserve hold | V21/V24/V25; PC3 |
| R-3 | Generation dedupe | V11-V13; PC6a/PC6b |
| R-4 | Typed timeout precedence | V4-V6/V28; PC4/PC5 |
| R-5 | Resume clearing, enqueue neutrality | V7/V20; PC7 |
| R-6 | State-backed Attention | V26; PC8 |
| R-7 | Held refusal avoids generic restart failure handling | V18/V19; PC2 |
| R-8 | Evidence does not come from prose/incidents | V1/V11/V16; PC4 |
| R-9 | Queue ownership | V15; PC9 |
| R-10 | Reset distinction | V14/V30; PC10a/PC10b |
| R-11 | No hold kill/close | V28; PC13 (runner RPC boundary adaptation) |
| R-12 | CARD-0382 rules guard | V29; forced GrokRulesLaunchRefusal/RunnerRefusal/ArgvPolicy classes |
| R-13 | CARD-0383 kept-pane behavior | V4/V28; forced HerdrLaunchShapeTests |
| R-14 | Stop/liveness semantics | V23/V24; AgentSupervisionTests |
| R-15 | Same-cwd siblings/routing preserved | V12/V25; ChannelBridgeTests |
| R-16 | Hold precedes named placement | V21; zero PlacementCheck calls; HerdrPlacementPreflightTests |

## Changed areas

- Evidence/state: `server/Application/Services/HerdrSupervisionFailureEvidence.cs`, `HerdrSupervisionStateService.cs`; `AgentSession`/`AgentSupervisionState`, the typed enum, settings/validator and CLI migration.
- Launch/reconciliation/supervision: `AgentControlService`, `AgentSessionService`, `AgentSessionRuntime`, `SessionReconciliationService`, `AgentSupervisorService`, and DI in `Program.cs`.
- Operator surfaces: `ChannelBridgeService`, `AttentionService`, `AgentService`, DTOs/enums; client agents API/AgentsPage and attention types/visuals.
- Fixtures/regressions: the forced test classes above plus `FakeAgentProtocolAdapter`, `DirectSessionRunnerClient`, and shared harness access. The runner production protocol and allocator were unchanged.
- Owner docs: `docs/herdr-sessions.md`, `docs/ops-http.md`, `docs/antiphon-api.md`.
