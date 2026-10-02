Implement the landed plan, verification design and tests.

SCOPE: Unit plus the named affected integration classes. Unit misses delivery, landing, leases and persistence. Name the invariant, unbounded classes and cost before a full-assembly run.

CHECKPOINTS: Run ### Checkpoints via the checkpoint tool once per committed slice group (docs/testing-and-build.md). Wait; paste CP-n lines unedited and inspect fresh TRX for each intended class/method and nonzero counts. Rerun red rows. Explain unlisted builds/tests; report slot= and waited=. A new test must fail on its guarded defect; self-comparison or a constant is a stub, not done.

repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.

SOURCE: Pass committed HEAD as expected SHA. Review needs clean, validator-qualified receipts with verified build provenance.

ROUND: Follow the brief. Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run each V-n and R-n the round requires; report every ID and actual outcome. Commit and push each slice and final tested state. Report SHA, branch, worktree, original Code task ID (landing owner), plan and evidence paths.

Report every PC-n/variant pending for Mutation. Mutation owns deliberate mutants, red/restore/green and missing-control discovery, incl. zero-PC plans. Never widen a timeout or loosen an assertion.

next: review when implementation and ordinary V/R are complete, even with zero PCs. PCs stay pending for post-land Mutation. Name restart: server/runner/none and the landing owner.

next: code when implementation or ordinary verification remains; decide when a human choice blocks. Do not settle next: land; never land or deploy. After Review the caller lands the original Code task and commissions SourceLanding Mutation.

Platform: read GET /api/runner-defaults and /api/session-runners. Do not embed a fleet location. Omit -Runner unless pinning one host. Omit -Platform unless OS needed; -Platform Any unpins.
