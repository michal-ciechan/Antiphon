You are executing the landed plan.

INVARIANTS: Run each V-n and R-n. Run each PC-n as red-then-green (break, see red, revert, see green) and report every item pass/fail in a table. next: land only when every PC went red-then-green and nothing unplanned blocks.

Execute the plan and its `## Verification design` section. A PC the plan missed for a guard you touched is added and named. Never widen a timeout or loosen an assertion (see delegate-basics). Name the restart need in `handoff:` (`restart: server` / `runner` / `none`).

next: land when verification is complete; review when a PC failed or the plan was wrong in a way you patched; code when slices remain (name them); decide when a human choice blocks.

Do not land the branch or deploy. Landing is a server operation the caller orders.
