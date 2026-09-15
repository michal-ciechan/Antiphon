# CARD-0527 Code evidence

Landing owner: `b23d0fe2`. Branch: `feat/card-task-b23d0fe2`.
Worktree: `C:\Antiphon\worktrees\card-task-b23d0fe2`.
Plan: [commit-on-settle](2026-09-15-card-0527-commit-on-settle-plan.md).

## S2 checkpoint

`3c5c5b72` built successfully (234 warnings, zero errors). Filter
`/*/*/GatedCommitServiceTests/*` executed 15 cases: 15 passed, zero failed/skipped.
Fresh TRX: `.antiphon/c527-evidence/gate-s2/gate.trx`.
Build log: `.antiphon/c527-evidence/build-s2.log`.
An explicit held-lease success case is added after this run and awaits the next build.

Design clarification: D-5 step 6's scoped staged-subset check conflicts with V-8's
foreign staged path. Capture the pre-stage index; reject newly staged foreign paths,
preserving existing foreign staged entries. `commit --only` excludes those entries.
Literal pathspecs prevent a filename from becoming a git pathspec pattern.
Verbose check-ignore negations allow the path and are not refusals.

No deliberate mutation was executed. PC-1 through PC-31 and all variants remain pending.
