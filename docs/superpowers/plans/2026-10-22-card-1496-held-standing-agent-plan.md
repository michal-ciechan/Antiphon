# CARD-1496 Held standing agent model: Retry refusal and wall-reroute Block - plan

Code task 3bf23302, branch `feat/card-task-3bf23302`, base `713801a8f`. Follow-up of CARD-1490
(plan `2026-10-22-held-model-explicit-dispatch-plan.md`, Final Review a547872b gap 2 and A-1/A-2/A-3).

## Defect

A list-governed task (`Complexity` or `RoutingPinId`) created with an explicit standing agent
(`-Complexity` plus `-AgentId`) launches on that agent's `ModelId` (`DispatchModelAlias.ForTaskAsync`)
whatever (kind, level) the walk picks, because the walk compares tier aliases only.

- Retry: chain tasks were exempt from the CARD-1490 refusal, so Retry stopped the session and the
  tick then parked the task behind the hold or rerouted it and launched it on the held agent model.
- Usage-wall reroute (`RerouteOnWallAsync`): the walk skipped the walled tier alias, chose another
  candidate and requeued onto the same standing agent, which launches the held model again.

Reachable only through an explicit `-Complexity` + `-AgentId` create; default and pinned flows run
chains on pool agents.

## Fix (minimal)

- `DispatchModelAlias.StandingAgentModelAsync(db, task, kind)`: the standing (non-ephemeral) agent's
  `ModelId` when it normalizes for `kind` (the `ForTaskAsync` pinned-agent rule), else null.
- `RetryAsync`: a list-governed task whose standing agent model is held is the same 409
  `model_disabled` (`RequireAsync`) as create/escalate, before the repeat block, the auth refusals,
  `StopDelegateAsync` or any row change. Explicit tasks keep the CARD-1490 `AfterRequeueAsync` check.
  An ephemeral agent, an agent with no normalizing `ModelId`, or an unheld agent model: unchanged.
- `RerouteOnWallAsync`: when the walk chose another (kind, level) and the standing agent's model
  for the chosen kind is held, `BlockOnWallAsync` with the named reason
  `AgentTaskService.HeldStandingAgentModelPrefix` (`held standing agent model: ...`) instead of
  `RequeueOnWallAsync`. No requeue, no new attempt, no Rerouted event; the walled session is released
  as every wall Block releases it. The reason deliberately does not start with
  `routing exhausted: `, so the dispatcher's routing-blocked resume does not requeue it onto the
  same held agent model; it waits for a human (attention shows it as a Blocked item).
- `model_disabled` stays the code. Non-held behaviour unchanged.

## Kill-site table additions (to the CARD-1490 table)

| Site | Kills a session? | Then | Consults the hold before the kill? |
|---|---|---|---|
| `RetryAsync` (list-governed task on a standing agent) | yes, via `RequeueCoreAsync` | requeue, same tier | **yes (CARD-1496)**: `RequireAsync(StandingAgentModelAsync)` first |
| `RerouteOnWallAsync` (chain task on a standing agent) | yes (walled session) | Blocked for a human (`held standing agent model:`) | **yes (CARD-1496)**: checked before `RequeueOnWallAsync` |
| `AgentTaskDispatcher.RecoverReleasedSeatAnswersAsync` -> automatic `RetryAsync` (released-seat answer) | no (seat already released and confirmed) | under a hold the refusal throws, the caller logs "Released-seat answer recovery deferred" and retries each tick; the answer is kept | yes (the Retry refusal / `ContinueReleasedSeatAnswerAsync` `RequireAsync`); not kill-then-park (A-2) |

A-3 (noted only, not changed): `AutoEscalateStalledAsync` writes `FailureReason` on the shared
context before `EscalateAsync`; a hold landing between its `IsHeldAsync` skip and the refusal can
let that unsaved reason ride a later `SaveChanges`. The clock ships disabled.

## Verification

| ID | Claim | Witness |
|---|---|---|
| V-1 | Retry of a Working or Failed chain task on a standing agent whose `ModelId` is held is 409 `model_disabled` naming the agent alias and until; no kill, no status/attempt/level/agent/session change, no event. | `HeldStandingAgentChainTests.Retrying_a_chain_task_on_a_standing_agent_whose_model_is_held_is_409_with_no_kill` (2 cases) |
| V-2 | Controls: the same chain task with an unheld agent model (opus, fable held), and with an ephemeral fable agent, still requeues (Retried event, attempt + 1). | `HeldStandingAgentChainTests.Retrying_a_chain_task_requeues_as_today_when_no_standing_agent_model_is_held` (2 cases) |
| V-3 | Wall reroute of such a task Blocks with the `held standing agent model:` reason naming the model, not `routing exhausted: `; kind/level/attempt/agent unchanged, no Rerouted event, one Blocked event (launches nothing). | `HeldStandingAgentChainTests.Wall_reroute_of_a_chain_task_on_a_standing_agent_whose_model_is_held_blocks_and_launches_nothing` |
| V-4 | Controls: unheld agent model and ephemeral agent reroute on the wall to opus as today (session stopped, Queued at High). | `HeldStandingAgentChainTests.Wall_reroute_of_a_chain_task_requeues_as_today_when_no_standing_agent_model_is_held` (2 cases) |
| R-1 | CARD-1490 escalation/Retry refusals unchanged. | CP-2 |
| R-2 | Tick hold check unchanged. | CP-3 |
| R-3 | Wall reroute flows unchanged. | CP-4, CP-5 |

Red record: V-1 (2 cases) and V-3 red at `50a220f65` (one narrow build-slot run, class filter,
7 total / 3 failed / 4 succeeded: the guarded witnesses red, the four controls green).

PCs pending for SourceLanding Mutation: PC-1 drop the standing-agent `RequireAsync` arm in
`RetryAsync` -> V-1 red; PC-2 drop the held-agent-model Block in `RerouteOnWallAsync` -> V-3 red;
PC-3 make `StandingAgentModelAsync` ignore `Ephemeral` -> V-2/V-4 ephemeral cases red;
PC-4 give the Block reason the `routing exhausted: ` prefix -> V-3 red (`C1496-wall-named-reason`).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1496/` | held-standing | `/*/*/HeldStandingAgentChainTests/*` | V-1..V-4 | 4 methods (7 cases), 0 failed/skipped | 7 | 8 |
| CP-2 | S1 | CP-1 | held-escalation | `/*/*/HeldModelEscalationTests/*` | R-1 | whole class, 0 failed/skipped | 10 | 6 |
| CP-3 | S1 | CP-1 | hold-tick | `/*/*/ModelAvailabilityDispatcherTests/*` | R-2 | whole class, 0 failed/skipped | 3 | 6 |
| CP-4 | S1 | CP-1 | wall-reroute | `/*/*/ComplexityWallRerouteTests/*` | R-3 | whole class, 0 failed/skipped | 11 | 10 |
| CP-5 | S1 | CP-1 | runner-reroute | `/*/*/DefaultRunnerRerouteTests/*` | R-3 | whole class, 0 failed/skipped | 16 | 10 |

### Cost

Ordinary floor ~40 minutes row estimate (rows overlap two at a time on Linux) plus the one unlisted
red-witness build-slot run at `50a220f65`. Unit lane and `AgentTaskServiceIntegrationTests` not run:
the brief asks for minimal testing and no whole-suite runs; they are deferred, not passed.
