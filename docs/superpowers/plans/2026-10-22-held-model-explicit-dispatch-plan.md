# CARD-1490 Held model refuses explicit Level dispatch - plan

Code task b180d6e2, branch `feat/card-task-b180d6e2`, base `0ea49a7cc`.

## Step 1 - launch-path trace (read-only, before any change)

Hold = an active `ModelAvailabilityHold` for `(kind, alias)` or `(kind, *)`
(`ModelAvailability.FindActiveAsync`). Live on 2026-10-22: Manual `ClaudeCode/fable` until
2026-10-23T08:00:00Z.

| Path | Consults the hold today? | Where | Outcome on a held explicit target |
|---|---|---|---|
| Create, explicit `-Kind/-Level` (no chain) | yes | `AgentTaskService` create `else` branch, `RequireAsync` | 409 `model_disabled` with `modelAvailability {kind, modelAlias, disabledUntil, source, available}`; pinned standing agent's `ModelId` wins the alias. Pinned by `ModelAvailabilityCreateTests.Create_Frontier_Claude_against_a_fable_hold_is_409_model_disabled`. |
| Create, explicit + `ignoreModelDisabled` (documented override) | yes | same | 200 Queued with a warning; dispatch waits for the hold. Unchanged. |
| Create, chain-chosen (complexity / matrix default / walked pin) | yes | `ComplexityRoutingService` walk skips held candidates | next candidate chosen, skip named in the warning; Required single pin -> 409 `model_disabled` + pin coda. |
| Dispatcher tick (every Queued task: create, retry, reroute, escalation requeue) | yes | `AgentTaskDispatcher` tick, `IsHeldAsync(ResolveDispatchAliasAsync)` before any spawn | explicit task stays Queued (ModelHeld, capacity wait); list-governed task re-walks. Never spawns. |
| Warm-pool reuse | yes (indirectly) | `TryReuseWarmAgentAsync` runs after the tick hold check; the pool query matches `ModelLevel` + `Kind` | no reuse onto a held alias. |
| Retry | **no** at request time (corrected by Review 6cd4edb3 D-1) | `RetryAsync` -> `RequeueAsync` -> `RequeueCoreAsync` `StopDelegateAsync` | **bypass**: kills the running session, requeues at the same held tier, parks Queued (ModelHeld). Fixed in repair task cd2c1ad3. |
| Manual escalation `POST /escalate` | **no** | `EscalateAsync` | **bypass**: kills the running session, stamps the held tier, parks the task Queued behind the hold. |
| Automatic escalation clock (ships disarmed, CARD-0158) | **no** | `AutoEscalateStalledAsync` | **bypass** when re-armed: same kill-and-park. |
| Reply to a quota-blocked task | partly | `AgentTaskReplyService` checks only the recovery's applied hold id | 409 `subscription_quota_hold` while that hold row is active; a later different active hold on the same alias is not consulted. Deferred (see Out of scope). |
| Released-seat Continue (`RetryAsync` answer path) | yes (corrected by Review 6cd4edb3) | `ContinueReleasedSeatAnswerAsync` `RequireAsync` | 409 `model_disabled`; the seat is already released, so there is no kill. |
| Specialist launches | yes | `SpecialistRequestService` `IsHeldAsync` per candidate | held candidate skipped (`Held`). |
| Orchestrator create | yes | create `else` branch | 409 `model_disabled`. |

No path silently substitutes another kind for an explicit target: create refuses, the tick holds
an explicit task in place, only list-governed tasks re-walk.

## Step 2 - fix (minimal)

- S1 `AgentTaskService.EscalateAsync`: `RequireAsync(kind, ModelLevelAliases.For(kind, target))`
  before any mutation or kill. A held target is the same 409 `model_disabled` create returns
  (kind, alias, until, available list); the task, its tier and its session are untouched.
- S1 `AgentTaskDispatcher.AutoEscalateStalledAsync`: skip a task whose escalation target is held
  (no kill, no FailureReason write); the clock fires again after the hold clears.
- No new override: escalation had none. `ignoreModelDisabled` on create stays the only one.
- Lapsed hold or non-held target: unchanged (no extra mutation; one hold read added).

Error code: the card's wording says `model_held`; the existing documented create refusal for a held
model is `model_disabled` (delegate.ps1, client attention hint, ~13 test classes). This task keeps
`model_disabled` for escalation so one held-model refusal has one code. Renaming it is a separate
wire-contract decision.

## Repair (task cd2c1ad3, Review 6cd4edb3 D-1/D-2)

- D-2: the tick's alias rule moves from the private `AgentTaskDispatcher.ResolveDispatchAliasAsync`
  into `DispatchModelAlias.ForTaskAsync` (specialist recorded alias, else a pinned agent's
  normalizing `ModelId`, else the tier ladder). `DispatchModelAlias.AfterRequeueAsync` applies it
  to the row a requeue leaves (pinned agent kept, ephemeral agent retired). The tick, the
  `EscalateAsync` refusal, the auto-clock skip and the Retry refusal all use it; there is no second
  copy of the rule.
- D-1: `RetryAsync` refuses 409 `model_disabled` (same body as create/escalate) when the
  after-requeue dispatch alias is held, before the launch-repeat block, the auth refusals,
  `StopDelegateAsync` or any row change. A list-governed task (`Complexity` or `RoutingPinId`)
  keeps today's path: the tick re-walks it to an unheld candidate or Blocks it for a human
  (`ComplexityWallRerouteTests` Retry-then-Reroute flow).

### Kill / requeue / tier-change sites (enumerated for the repair)

| Site | Kills a session? | Then | Consults the hold with the dispatch alias before the kill? |
|---|---|---|---|
| `AgentTaskService.RetryAsync` (explicit task) | yes, via `RequeueCoreAsync` | requeue, same tier | **yes (this repair)**: `RequireAsync(AfterRequeueAsync)` first |
| `RetryAsync` (list-governed task) | yes | requeue; tick re-walks or Blocks for a human | no refusal by design: never parks behind the hold except a Required-pin task whose pin forbids reroute (pre-existing; create already refuses a held single Required pin) |
| `RetryAsync` released-seat answer -> `ContinueReleasedSeatAnswerAsync` | no (seat already released and confirmed) | requeue keeping the agent | `RequireAsync` on the tier alias; no kill, so not kill-then-park |
| `EscalateAsync` (manual `/escalate`) | yes | requeue at the target tier | **yes (this repair: dispatch alias)** |
| `AgentTaskDispatcher.AutoEscalateStalledAsync` | via `EscalateAsync` | as above | **yes (this repair: dispatch alias skip, then `EscalateAsync`'s refusal)** |
| `RerouteAsync` | only if a session is still attached; Queued/Blocked-for-routing rows carry none | requeue at the chosen (kind, level) | `RequireAsync` on the chosen tier alias (no kill) |
| `RerouteOnWallAsync` (usage wall) | yes | requeue on a walked candidate | the walk skips held candidates by tier alias; exhausted -> `BlockOnWallAsync`. The session already hit a usage wall. Residual (pre-existing, disclosed): a chain task on a pinned agent whose `ModelId` is held would park at the tick; chain tasks run on pool agents |
| `BlockOnWallAsync` | yes | Blocked for a human | not a park on a hold: the task waits for a human decision |
| `CancelAsync` | yes | Canceled | not a requeue |
| Delivery watchdog / boot-wedge at limit (`FailWedgedAtLimitAsync`) | yes | Failed | not a requeue |
| Boot-wedge relaunch | replaces a never-started session | relaunch at the same alias the tick admitted seconds earlier | no re-check (pre-existing; not kill-then-park) |
| Agent delete / stop (`AgentService`) | yes | agent removed/stopped | not a task requeue |

## Out of scope (named, not done)

- "Next available chain candidate" in the refusal: today the `available` list is carried; a walk
  of the role chain at refusal time is not added here.
- Reply/quota-blocked resume re-reading the current hold for the session alias.
- An ordinary witness for the auto-clock skip (shipped disarmed; harness is shared-Postgres). Pending
  for Mutation as PC-2.

## Verification

| ID | Claim | Witness |
|---|---|---|
| V-1 | Escalation onto a held alias is 409 `model_disabled` naming kind/alias/until/available; no kill, no tier/kind/status/session change (Failed and Working). | `HeldModelEscalationTests.Escalating_onto_a_held_alias_is_409_and_leaves_the_task_untouched` (2 cases) |
| V-2 | Lapsed hold escalates exactly as before. | `HeldModelEscalationTests.Escalating_after_the_hold_lapsed_is_unchanged` |
| V-3 | Non-held target escalates as before while another alias is held. | `HeldModelEscalationTests.Escalating_onto_a_non_held_alias_is_unchanged_while_another_alias_is_held` |
| V-4 | Retry of a Working or Failed explicit task on a held dispatch alias is 409 `model_disabled`; no kill, no status/attempt/level/session change, no event. | `HeldModelEscalationTests.Retrying_onto_a_held_alias_is_409_with_no_kill_and_no_change` (2 cases) |
| V-5 | Retry of a list-governed task on a held alias still requeues (tick re-walk). | `HeldModelEscalationTests.Retrying_a_list_governed_task_on_a_held_alias_still_requeues_for_the_rewalk` |
| V-6 | Escalation of a pinned-agent task checks the agent's `ModelId`: 409 when it is held (no kill), escalates when only the tier alias is held. | `HeldModelEscalationTests.Escalating_a_pinned_agent_task_is_409_when_the_agent_model_is_held`, `Escalating_a_pinned_agent_task_is_unchanged_when_only_the_tier_alias_is_held` |
| V-7 | Escalation of a specialist task checks its recorded alias. | `HeldModelEscalationTests.Escalating_a_specialist_task_is_409_when_its_recorded_alias_is_held` |
| R-1 | Existing escalation, retry and create behaviour unchanged. | CP-2, CP-3, CP-4 |
| R-2 | The tick's hold check is unchanged after the alias rule moved to `DispatchModelAlias.ForTaskAsync`. | CP-5 |
| R-3 | Retry-then-Reroute of a walled chain task is unchanged. | CP-6 |

PCs pending for SourceLanding Mutation: PC-1 drop the `RequireAsync` in `EscalateAsync` -> V-1 red;
PC-2 drop the held skip in `AutoEscalateStalledAsync` -> no ordinary witness (missing control);
PC-3 drop the `RequireAsync` in `RetryAsync` -> V-4 red; PC-4 make `AfterRequeueAsync` return the tier
ladder alias -> V-6 and V-7 red; PC-5 drop the list-governed exemption in `RetryAsync` -> V-5 red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1490/` | held-escalation | `/*/*/HeldModelEscalationTests/*` | V-1..V-7 | 8 methods (10 cases), 0 failed/skipped | 10 | 12 |
| CP-2 | S1 | CP-1 | task-service | `/*/*/AgentTaskServiceIntegrationTests/*` | R-1 | whole class, 0 failed/skipped | 87 | 12 |
| CP-3 | S1 | CP-1 | stall-escalation | `/*/*/AgentTaskStallEscalationTests/*` | R-1 | whole class, 0 failed/skipped | 8 | 6 |
| CP-4 | S1 | CP-1 | hold-create | `/*/*/ModelAvailabilityCreateTests/*` | R-1 | whole class, 0 failed/skipped | 10 | 6 |
| CP-5 | S1 | CP-1 | hold-tick | `/*/*/ModelAvailabilityDispatcherTests/*` | R-2 | whole class, 0 failed/skipped | 3 | 6 |
| CP-6 | S1 | CP-1 | wall-reroute | `/*/*/ComplexityWallRerouteTests/*` | R-3 | whole class, 0 failed/skipped | 11 | 10 |

### Cost

Ordinary floor ~36 minutes row estimate (rows overlap two at a time on Linux) plus the one unlisted
red-witness build-slot run at `8d407e723` (class filter, stated in the Code report). Unit lane not
run: the brief asks for minimal testing and no whole-suite runs; it is deferred, not passed.
