Implement landed plan, verification design and tests.

SCOPE: Unit plus the named affected integration classes. Unit misses delivery, landing, leases, persistence. Before full-assembly runs name invariant, unbounded classes and cost.

CHECKPOINTS: Run the closed list in ### Checkpoints via checkpoint tool per committed group (docs/testing-and-build.md). Wait; inspect fresh TRX for each intended class/method and nonzero counts. Rerun red rows. Explain unlisted builds/tests; report slot= and waited=. New tests must fail on their guarded defect; self-comparison or a constant is a stub, not done.

repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.

SOURCE: Pass committed HEAD as expected SHA; keep actual tested SHA and unedited CHECKPOINT lines with clean receipts/verified build provenance. Run scripts/check-evidence-diff.ps1 over full task base..HEAD.

ROUND: Follow the brief. Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run each V-n and R-n the round requires; report every ID and actual outcome. Commit and push slices; report full commit SHA, branch, worktree, original Code task ID (landing owner), plan/evidence paths.

ADVISORY (D-2): No Review rerun solely for advisory docs/evidence corrections; initial ordinary Review still applies. Keep behavior, verification completeness, advisory corrections and introduced/inherited/unresolved attribution separate. Missing acceptance, failed tests or untrustworthy required verification cannot become Clean/Full by relabeling. Run an exact missing selection once; attach existing same-source receipts only through deterministic validation and append-only audit when available, otherwise retain the incomplete gate. Preserve historical outcomes/costs. Freeze reviewed source: publish its valid tip first, then use a separate Docs helper's exact SHA, delta digest and unlatched caller approval for inspected narrative-only corrections. Never copy the old Review/source-clean assertion, use an Interim latch escape, or classify instructions, policy, executable/generated/configuration content or unproved comments as narrative.

Report every PC-n/variant pending for Mutation. Mutation owns deliberate mutants, red/restore/green and missing-control discovery, incl. zero-PC plans. Never widen a timeout or loosen an assertion.

next: review when implementation and ordinary V/R are complete, even with zero PCs. unbound candidate docs/superpowers/mutations/. Restart: server/runner/none.

next: code when implementation/ordinary verification remains; decide for a blocking human choice. Do not settle next: land or deploy. After Review caller lands original Code task and commissions SourceLanding Mutation.

Platform: read GET /api/runner-defaults and /api/session-runners. Do not embed a fleet location. Omit -Runner unless pinning one host. Omit -Platform unless OS needed; -Platform Any unpins.
