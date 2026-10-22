# CARD-1446 plan amendment: milestone 3 checkpoints (S5)

Code stage, 2026-10-22, task `b163cc39`, branch `feat/card-task-b163cc39`. This file carries only the
`### Checkpoints` table for the S5 rows (CP-14..CP-16) of
[2026-10-22-card-1446-stale-landings-plan.md](2026-10-22-card-1446-stale-landings-plan.md), plus one
unlisted regress row (RV-2) with its reason. The plan file and the milestone-2 amendment
([2026-10-22-card-1446-stale-landings-plan-m2-checkpoints.md](2026-10-22-card-1446-stale-landings-plan-m2-checkpoints.md))
are unchanged (both are on master; editing either here would be an add/add conflict at landing). This file is
the `--plan` the checkpoint tool runs for this milestone.

## Filter rewrites

TUnit's `TreeNodeFilter` rejects a `/` inside an OR operand and `(ClassA)|(ClassB)/M*` runs 0 tests
(milestone 1 report, task `10e48480`, CP-4). The working form is `(ClassA*)|(ClassB*)/M*`.

| CP | Plan filter | Rewritten filter | Note |
|---|---|---|---|
| CP-14 | `/*/*/WorktreeResidueCandidateRowTests/C1446_unchanged_held_rows_are_not_rewritten` | unchanged | one class, one method: no OR operand, parses as written |
| CP-15 | `/*/*/WorktreeResidueCandidateRowTests/*` | unchanged | one class: no OR operand, parses as written |
| CP-16 | `/*/*/(WorktreeResidueRegistrationTests/*)\|(WorktreeRetirementRaceTests/*)\|(CardWorktreeMirrorCleanupTests/*)` | `/*/*/(WorktreeResidueRegistrationTests*)\|(WorktreeRetirementRaceTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | same roster (every method of the three classes); identical to the milestone-2 amendment's rewrite, which was not run there |

RV-2 is unlisted in the plan: S5 replaces the SettledTask lane's `PersistCooldownAsync` cursor write with the
preloaded-cursor path the dedupe needs and changes when a candidate row is written, which
`WorktreeResidueSweepTests` (`C459_CooldownSurvivesRestart`, `C459_FairnessSurvivesRestart`, `C459_BudgetIsShared`,
`C459_OnlyCompleteCountsRemoved`, ...) covers and CP-16 does not. It reuses CP-15's build.

### Checkpoints

Lane: server2 (D-7). Build output `bin-c1446-*/` (forward slash). CP-14 is run at the S5-test commit, before the
implementation commit exists.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-14 | S5-test | `tests/Antiphon.Tests -> bin-c1446-s5red/` | s5-red | `/*/*/WorktreeResidueCandidateRowTests/C1446_unchanged_held_rows_are_not_rewritten` | V-11 witness | **red**: 1 executed, 1 failed (second run writes N rows) | 1 | 7 |
| CP-15 | S5-impl | `tests/Antiphon.Tests -> bin-c1446-s5/` | s5-green | `/*/*/WorktreeResidueCandidateRowTests/*` | V-11, V-12 | all listed, 0 failed/skipped | 3 | 8 |
| CP-16 | S5-impl | CP-15 | s5-regress | `/*/*/(WorktreeResidueRegistrationTests*)\|(WorktreeRetirementRaceTests*)\|(CardWorktreeMirrorCleanupTests*)/*` | V-13 | all listed, 0 failed/skipped | 20 | 12 |
| RV-2 | S5-impl | CP-15 | s5-sweep | `/*/*/WorktreeResidueSweepTests/*` | unlisted (S5 cursor write path; see above) | all listed, 0 failed/skipped | 18 | 8 |
