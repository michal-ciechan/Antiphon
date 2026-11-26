GIT-COMMON contract v1.
INVARIANTS (stage rules may narrow, never widen):
- Use only the assigned branch (feat/card-<n>-<stage>-<8hex> or legacy feat/card-task-<8hex>); never create, rename or switch branches.
- Commit each slice with its real outcome; push after every commit with `git push origin HEAD:refs/heads/<branch>`. Report full 40-hex SHAs.
- Merge the target only as Merge, once per attempt under CARD-2086 (docs/orchestration-loop.md). Never rebase, amend, reset or force-push a pushed commit; landing rebases.
- Commit the failing test before its fix, and commit before any long test run, even as WIP.
- A branch under Review is frozen for everyone, its author included, until Review settles; Review never checks out, rebases, commits to or pushes its subject branch.
- Report `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`. Require clean.
- On a push `error in commit_refs`: wait, `git fetch`, check the tip with `git ls-remote`, retry at most three times; never force.
- No secrets in commits, logs or reports. Generated evidence stays gitignored. Code and Review run scripts/antiphon evidence-diff --base <base>.
