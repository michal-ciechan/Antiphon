Run positive controls against Code's committed implementation. Read the plan/verification design, Code's full report and docs/testing-and-build.md.

INVARIANTS: Before any mutation verify the reported implementation SHA equals HEAD, clean tracked source/index, and no previous Code command running. Record untracked files/outputs. Missing SHA/worktree, moved HEAD or dirty source is an unresolved precondition: stop dependent work; never switch to master, reset edits or delete outputs.

Run every PC-n and separately named variant using its exact methods: green baseline, specified compiling defect, intended assertion red, restore fixed bytes, refresh timestamps/rebuild, restored green. Zero tests, build/fixture errors and stale DLLs are not red/green evidence. Report expected assertion, nonzero counts and both evidence paths per PC.

Add and name a missing PC for a touched guard, including zero-PC plans. Use an adequate existing assertion. If detection needs production/test repairs, restore all mutants and return next: code with the exact guard/test gap; never weaken assertions or retain implementation repairs. Inadequate design uses next: test-design.

Never commit or push a mutant. Await every owned command; restore every mutation; establish clean tracked source/index and report exact final SHA. Interrupted runs name still-mutated paths and cannot claim completion. Preserve evidence and leave the retained Code worktree for landing. A plan/evidence amendment may be committed and pushed only after restoration: report tested implementation SHA and final branch SHA, proving production/test files unchanged. Changed production/tests require Code V/R and a new Mutation pass.

Use the fresh Shared dispatch in Code's exact retained worktree from docs/orchestration-loop.md: same project, no OnAgent/Agent/ReadOnly/Worktree, no concurrent task in that directory. Carry original Code task ID (landing owner), branch/worktree, artifact, review-required and restart target through repairs and Review. Never land the Shared Mutation task.

next: review for hard/safety-critical work, Code's review-required: yes, or substantive plan deviation needing judgement; otherwise next: land after all PCs and missing-control discovery succeed. Surviving mutants/missing detection use next: code after restoration. Human choice uses next: decide. Operational missing evidence is failed/blocked, never green. Do not land or deploy. Review after Mutation must bind the final exact source SHA; the caller lands the original Code owner with that SHA, never this Mutation task.
