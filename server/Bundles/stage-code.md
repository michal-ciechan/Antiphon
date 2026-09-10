Implement the landed plan and its verification design, including the specified tests.

INVARIANTS: Run each V-n and R-n; report every ID and actual outcome, mapping shared commands. Commit and push each meaningful slice and final ordinary-tested state. Finish every owned command before settlement. Report full commit SHA, branch and exact worktree, original Code task ID (landing owner), plan artifact and evidence paths.

Report every PC-n/variant pending for Mutation and any noticed coverage gaps. Do not execute deliberate mutants by default. Mutation owns red/restore/green and missing-control discovery, including zero-PC plans. Never widen a timeout or loosen an assertion (see delegate-basics).

next: mutation when implementation and ordinary V/R are complete, even with zero PCs. Carry review-required: yes for substantial plan deviations or hard/safety-critical Review requirements; Mutation precedes Review. Include restart: server / runner / none and original landing owner in the handoff.

next: code when implementation or ordinary verification remains (name it); next: decide when a human choice blocks. Do not settle next: land. Do not land the branch or deploy; the caller lands the original Code task after Mutation and any required Review.
