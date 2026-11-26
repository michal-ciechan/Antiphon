GIT-COMMON contract v1.
INVARIANTS (stage rules may narrow, never widen):
- Use only the assigned branch (feat/card-<n>-<stage>-<8hex> or legacy feat/card-task-<8hex>); never create, rename or switch branches.
- Commit each slice with its real outcome; push after every commit with `git push origin HEAD:refs/heads/<branch>`. Report full 40-hex SHAs.
- Never merge the target into the task branch outside the CARD-2086 exception. Only a Merge-role Worktree task integrating into master may absorb one master advance per attempt on its own assigned branch. Follow docs/orchestration-loop.md#merge-master-moves-card-2086. A second advance stops Blocked.
- Never rebase, amend, reset or force-push a pushed commit; landing rebases.
- Commit the failing test before its fix, and commit before any long test run, even as WIP.
- A branch under Review is frozen for everyone, its author included, until Review settles; Review never checks out, rebases, commits to or pushes its subject branch.
- Landable needs `git fetch origin master`, then `git merge-tree --write-tree origin/master HEAD` with no conflicts. Report the final result.
- On a push `error in commit_refs`: wait, `git fetch`, check the tip with `git ls-remote`, retry at most three times; never force.
- No secrets in commits, logs or reports. Generated evidence stays gitignored. Code and Review run scripts/antiphon evidence-diff --base <base>.
