Implement the landed plan and its verification design, including its tests.

SCOPE: Ordinary verification is Unit plus the named affected integration classes from the plan's coverage-to-class list. Unit-only is insufficient for native delivery, landing, leases or persistence. A namespace/full-assembly run needs the cross-cutting invariant, unbounded classes and expected cost named first. See docs/testing-and-build.md Fast lane.

CHECKPOINTS: the plan's ### Checkpoints table is the closed list of builds and test runs. Run each CP-n once, in order, after its named slice is committed: one isolated build (forward-slash OutputPath), one filter, one fresh TRX; inspect fresh TRX for each intended class/method and nonzero counts. Fix a red CP-n and rerun it. Any other build/test command is unlisted: report it with a reason. Report per CP-n: commit, filter, executed/passed/failed/skipped, TRX path, slot=/waited=, reruns. A new test that cannot go red against the production line it guards (self-compare, constant, no outcome assertion) is a stub, not done.

ROUND: the brief's verification profile governs. Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run each V-n and R-n the round requires; report every ID and actual outcome. Commit and push each meaningful slice and final ordinary-tested state. Report full commit SHA, branch and exact worktree, original Code task ID (landing owner), plan artifact and evidence paths.

Report every PC-n/variant pending for Mutation and any noticed coverage gaps. Mutation owns every deliberate mutant, red/restore/green and missing-control discovery, incl. zero-PC plans. Never widen a timeout or loosen an assertion (see delegate-basics).

next: review when implementation and ordinary V/R are complete, even with zero PCs. Review precedes land; PCs stay pending for post-land Mutation. Include restart: server/runner/none and original landing owner in the handoff.

next: code when implementation or ordinary verification remains (name it); decide when a human choice blocks. Do not settle next: land; never land or deploy. The caller lands the original Code task after ordinary Review, then commissions SourceLanding Mutation.

Platform: read GET /api/runner-defaults and GET /api/session-runners. Do not embed a fleet location. Omit -Runner unless pinning one host. -Platform Windows for junction, file-sharing, ConPTY, or Windows path/CRLF/E2E. CP-13 and CP-14 use -Platform Windows. Portable rows stay on server2.
