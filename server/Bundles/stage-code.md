Implement landed plan, verification design and tests.

SCOPE: Unit plus the named affected integration classes. Unit misses delivery, landing, leases, persistence. Before full-assembly runs name invariant, unbounded classes and cost.

CHECKPOINTS: Run the closed list in ### Checkpoints via the checkpoint tool per committed slice group (docs/testing-and-build.md). Wait; inspect fresh TRX for each intended class/method and nonzero counts. Rerun red rows. Explain unlisted builds/tests; report slot= and waited=. New tests must fail on their guarded defect; self-comparison or a constant is a stub, not done.

repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.

SOURCE: Pass committed HEAD as expected SHA; keep actual tested SHA and unedited CHECKPOINT lines with clean receipts/verified build provenance. Run scripts/check-evidence-diff.ps1 over full task base..HEAD.

ROUND: Follow the brief. Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run each V-n and R-n the round requires; report every ID and actual outcome. Commit and push slices; report full commit SHA, branch, worktree, original Code task ID (landing owner), plan/evidence paths.

Report every PC-n/variant pending for Mutation. Mutation owns deliberate mutants, red/restore/green and missing-control discovery, incl. zero-PC plans. Never widen a timeout or loosen an assertion.

next: review after implementation and ordinary V/R, even with zero PCs. PCs stay pending post-land Mutation. Report restart: server/runner/none and owner.

next: code when implementation/ordinary verification remains; decide for a blocking human choice. Do not settle next: land or deploy. After Review caller lands original Code task and commissions SourceLanding Mutation.

Platform: read GET /api/runner-defaults and /api/session-runners. Do not embed a fleet location. Omit -Runner unless pinning one host. Omit -Platform unless OS needed; -Platform Any unpins.
