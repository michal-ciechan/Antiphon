# CARD-1493 S1 repair amendment (Review 1fddbdba D-1)

Amends `docs/superpowers/plans/2026-10-22-card-1493-s1-plan.md`: when the earlier row id is not `CP-<digits>` the warning recommends `'<project> -> <bin-x>/'` (the earlier build's own cell, which the Build grammar accepts) instead of the row id; **V-5 `C1493_DuplicateBuild_recommendation_imports_for_non_numeric_row_id`** applies every recommended cell to the plan text and re-imports it (exit 0, no warning); red at the test commit with one gated build-slot run; CP-2 now expects 26.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | S1-repair | `tests/Antiphon.Tests -> bin-c1493/` | import-class | `/*/*/CheckpointImportTests/*` | V-1..V-5, R-1 | all executed, 0 failed, 0 skipped | 26 | 10 |
