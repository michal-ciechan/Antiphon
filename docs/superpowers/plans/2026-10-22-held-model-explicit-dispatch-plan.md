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
| Retry | no at request time; tick holds | `RetryAsync` -> `RequeueAsync` | kills the old session, requeues at the same (held) tier, parks Queued. Not a launch on the hold. |
| Manual escalation `POST /escalate` | **no** | `EscalateAsync` | **bypass**: kills the running session, stamps the held tier, parks the task Queued behind the hold. |
| Automatic escalation clock (ships disarmed, CARD-0158) | **no** | `AutoEscalateStalledAsync` | **bypass** when re-armed: same kill-and-park. |
| Reply to a quota-blocked task | partly | `AgentTaskReplyService` checks only the recovery's applied hold id | 409 `subscription_quota_hold` while that hold row is active; a later different active hold on the same alias is not consulted. Deferred (see Out of scope). |
| Released-seat Continue (`RetryAsync` answer path) | no | `ContinueReleasedSeatAnswerAsync` | continues the same seat's conversation; not a new model choice. Deferred. |
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
| R-1 | Existing escalation, retry and create behaviour unchanged. | CP-2, CP-3, CP-4 |

PCs pending for SourceLanding Mutation: PC-1 drop the `RequireAsync` in `EscalateAsync` -> V-1 red;
PC-2 drop the held skip in `AutoEscalateStalledAsync` -> no ordinary witness (missing control).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1490/` | held-escalation | `/*/*/HeldModelEscalationTests/*` | V-1, V-2, V-3 | 3 methods (4 cases), 0 failed/skipped | 4 | 12 |
| CP-2 | S1 | CP-1 | task-service | `/*/*/AgentTaskServiceIntegrationTests/*` | R-1 | whole class, 0 failed/skipped | 87 | 12 |
| CP-3 | S1 | CP-1 | stall-escalation | `/*/*/AgentTaskStallEscalationTests/*` | R-1 | whole class, 0 failed/skipped | 8 | 6 |
| CP-4 | S1 | CP-1 | hold-create | `/*/*/ModelAvailabilityCreateTests/*` | R-1 | whole class, 0 failed/skipped | 10 | 6 |

### Cost

Ordinary floor ~36 minutes row estimate (rows overlap two at a time on Linux) plus the one unlisted
red-witness build-slot run at `8d407e723` (class filter, stated in the Code report). Unit lane not
run: the brief asks for minimal testing and no whole-suite runs; it is deferred, not passed.
