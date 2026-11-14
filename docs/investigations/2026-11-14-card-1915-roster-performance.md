# CARD-1915 migration roster performance

Measurement in progress (Debug task 71aaddba). No CARD-1915 implementation.

Source: current `origin/master`, checked with `git ls-remote` on 2026-11-14:
`1e09a851acef35a3d08eec4a79be2214d51c64df`.
Plan: `e5c926262`,
`docs/superpowers/plans/2026-11-14-card-1915-seed-transcript-aggregation-plan.md`.
Assigned branch already starts at that master; measurements use a fresh detached
scratch worktree `/work/worktrees/task-71aaddba-measure` at the exact source SHA.
Raw logs, TRX, load snapshots and phase records remain ignored under
`/work/worktrees/task-71aaddba/.antiphon/evidence/card-1915-roster/`.

Scope: cold native-output builds followed by exact CP-8–CP-33 filters, serially,
with separate admission and execution timings. One run is not a p95 estimate.
Final measurements and recommendations will replace this progress note.
