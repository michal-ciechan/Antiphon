# CARD-1887 S3 unbound Mutation candidate

Original Code/landing owner: ce95788a-daf2-4198-bb1d-958903809081.
Plan: docs/superpowers/plans/2026-11-13-card-1887-pin-query-code-plan.md.
All controls are pending. This candidate is not a SourceLanding-bound pack;
Mutation binds it to the published source and owns red/restore/green execution,
missing-control discovery and restoration evidence. Code ran no deliberate mutant.

| ID / variant | Change in server/Infrastructure/Data/SessionStateLoader.cs | Exact method in SessionStatePinQueryTests | Expected red |
|---|---|---|---|
| PC-3-parent / omit-parent | Remove the ParentSessionId branch of task_pins | Pin_membership_preserves_all_reasons | Parent-only open tasks lose required pins |
| PC-3-queue / omit-queue | Remove queued-message membership from Pinned | Pin_membership_preserves_all_reasons | Pending and unsettled sent queue sessions lose pins |
| PC-3-status / omit-blocked | Remove persisted status 3 from open_tasks | Pin_membership_preserves_all_reasons | Blocked child/parent sessions lose pins |
| PC-3-correlation / old-task-or | Restore the frozen correlated task predicate | Scale_plans_enumerate_open_tasks_once | Task scan Actual Loops exceeds 1 |

Use one precise method filter for each cycle:
`/*/*/SessionStatePinQueryTests/Pin_membership_preserves_all_reasons` or
`/*/*/SessionStatePinQueryTests/Scale_plans_enumerate_open_tasks_once`.
These share a production file and must not run concurrently. Keep the inherited
60s per database call and checkpoint row bounds; never weaken the assertions.
An ordinary baseline regression is recorded in the Code receipts; it discharges
none of these controls. Discover additional missing negative controls during Mutation.
