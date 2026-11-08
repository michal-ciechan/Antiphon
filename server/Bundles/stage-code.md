Implement landed plan, verification design and tests.

SCOPE: Closed rows plus the named affected integration classes; the Unit lane is nightly/rc. Plus the impacted-classes rows whose globs match git diff --name-only base..HEAD. Unit omits delivery, landing, leases, persistence. Name invariant, unbounded classes, cost before full-assembly.

CHECKPOINTS: Run the ### Checkpoints closed list via checkpoint tool per committed group. inspect fresh TRX for each intended class/method and nonzero counts. Rerun red rows. Explain unlisted; report slot= and waited=. Guarded defects fail; self-compare or constant is a stub, not done.

repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.

SOURCE: Pass committed HEAD as expected SHA; keep actual tested SHA and unedited CHECKPOINT lines with verified provenance. Run scripts/check-evidence-diff.ps1 over full task base..HEAD.

ROUND: Follow the brief. Final (default, first round): closed rows, named classes, not the whole lane, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run required V-n/R-n; report every ID and actual outcome. Commit and push slices; report full commit SHA, branch, worktree, original Code task ID (landing owner), plan/evidence paths.

Report every PC-n/variant pending for Mutation. Mutation owns deliberate mutants (red/restore/green), missing-control discovery and zero-PC plans. Never widen a timeout or loosen an assertion.

next: review when implementation and ordinary V/R are complete, even with zero PCs. unbound candidate docs/superpowers/mutations/. Restart: server/runner/none.

next: code; decide for a blocking human choice. Never next: land/deploy. Caller commissions SourceLanding Mutation.

MIGRATION: grep tests/ for MigrateAsync(<target>), .Down(, CreateIsolatedSchemaAsync then back; run model classes; report red/green counts
Platform: read GET /api/runner-defaults and /api/session-runners. Do not embed a fleet location. Omit -Runner unless pinning one host. Omit -Platform unless OS needed; -Platform Any unpins.
Apply docs/testing-and-build.md#os-branch-witnesses.
