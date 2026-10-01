Implement the landed plan and its verification design, including its tests.

SCOPE: Ordinary verification is Unit plus the named affected integration classes. Unit-only misses native delivery, landing, leases and persistence. Name the invariant, unbounded classes and cost before a full-assembly run.

CHECKPOINTS: Run the plan's closed ### Checkpoints list through the checkpoint tool once per committed slice group (docs/testing-and-build.md). Wait for its report; paste CP-n lines unedited and inspect fresh TRX identities/counts. Fix and rerun a red row. Explain any unlisted build/test. Report slot= and waited=. A new test must fail against its guarded production defect; self-comparison or a constant assertion is a stub.

SOURCE: Commit before ordinary checks. Pass full HEAD with `-ExpectedSourceSha` (script) or `--expected-source-sha` (tool). Approve only validator-qualified receipts with matching SHA, `dirty=0 sourceState=clean buildSource=verified`.

ROUND: the brief's verification profile governs. Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run each V-n and R-n the round requires; report every ID and actual outcome. Commit and push each slice and the final ordinary-tested state. Report full commit SHA, branch and exact worktree, original Code task ID (landing owner), plan artifact and evidence paths.

Report every PC-n/variant pending for Mutation. Mutation owns deliberate mutants, red/restore/green and missing-control discovery, incl. zero-PC plans. Never widen a timeout or loosen an assertion.

next: review when implementation and ordinary V/R are complete, even with zero PCs. PCs stay pending for post-land Mutation. Name restart: server/runner/none and the landing owner.

next: code when implementation or ordinary verification remains; decide when a human choice blocks. Do not settle next: land; never land or deploy. After Review the caller lands the original Code task and commissions SourceLanding Mutation.

Platform: read GET /api/runner-defaults and GET /api/session-runners. Do not embed a fleet location. Omit -Runner unless pinning one host. Omit -Platform unless OS needed; -Platform Any unpins.
