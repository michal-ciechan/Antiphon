Implement the landed plan and its verification design, including the specified tests.

SCOPE: Ordinary verification is Unit plus the named affected integration classes from the plan's coverage-to-class list. Build once into producer-owned isolated output; inspect fresh TRX for each intended class/method and nonzero counts. Report the filters and actual expanded counts. Unit-only is insufficient for native delivery, landing, leases or persistence. A namespace/full-assembly run needs the affected cross-cutting invariant, unbounded classes and expected cost named first. Mutation stays per-PC method-scoped. See docs/testing-and-build.md Fast lane.

ROUND: the brief's verification profile governs. Final (default, first round): whole Unit lane, every full affected class, every ordinary V/R, required manual work. Interim (explicit only): cumulative changed cases since the full baseline incl. earlier repair cases, unresolved-finding tests, named adjacent smoke; unbounded shared impact needs Final. List deferred-to-final IDs; never mark them passed.

INVARIANTS: Run each V-n and R-n the round requires; report every ID and actual outcome. Commit and push each meaningful slice and final ordinary-tested state. Finish every owned command before settlement. Report full commit SHA, branch and exact worktree, original Code task ID (landing owner), plan artifact and evidence paths.

Report every PC-n/variant pending for Mutation and any noticed coverage gaps. Do not execute deliberate mutants by default. Mutation owns red/restore/green and missing-control discovery, including zero-PC plans. Never widen a timeout or loosen an assertion (see delegate-basics).

To claim alternate-source or remote-only progress, include `[antiphon-progress:<task D guid> commit=<full-40-or-64-hex-sha>]` before the `--- next stage ---` block.

next: review when implementation and ordinary V/R are complete, even with zero PCs. Ordinary read-only Review precedes land; PCs stay pending for post-land Mutation. Include restart: server / runner / none and original landing owner in the handoff.

next: code when implementation or ordinary verification remains (name it); next: decide when a human choice blocks. Do not settle next: land. Do not land the branch or deploy; the caller lands the original Code task after ordinary Review, then commissions SourceLanding Mutation.
