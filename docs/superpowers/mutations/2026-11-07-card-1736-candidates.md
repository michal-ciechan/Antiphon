# CARD-1736 unbound Mutation candidates

Original Code/landing owner: b61de2b0. Plan:
`docs/superpowers/plans/2026-11-07-card-1736-event-pump-clock-plan.md`.
These candidates are pending; Code does not execute deliberate mutations.

Both controls use the exact method filter:
`/*/*/SessionRunnerEventPumpTests/C1259_confirming_prompt_and_other_session_progress_before_confirmation`.
Retain all five parameter cases and report each intended failed variant separately.

| ID | Guarded variant | Deliberate defect | Required red |
|---|---|---|---|
| PC-1 | live-delayed-body | Replace registration-driven advances with the old 100 x 50 ms advance/20 ms real-yield loop, then release the held body after that loop. Preserve the five virtual seconds of held-body delay and every assertion. | Submission wait times out after body release; no build/fixture failure. |
| PC-2 | subscription-ready-delayed-body | Same driver mutation, independently attributed to this variant. | Submission wait times out after body release; no build/fixture failure. |

The original red commit records this exact fixed-loop ordering. Mutation binds the
landed source, runs red/restore/green method-scoped cycles and discovers any missing
control. Do not widen timeouts, remove barriers or loosen assertions to make a run pass.
