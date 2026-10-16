# CARD-1303 R14: purge pipeline evidence with its scope

Code owner: f723b215-206e-4a90-822c-41cb1864a0ff.
Base: 6ca859d006b0a3cac9a88dc6bd551c37e98b2217, target rc/2026-10-15.

## Implementation

S1 commits regression tests before production changes. S2 adds explicit leaf-first
purges for the five tables introduced by AddPipelineSpeedFoundation. Board/card
deletion removes only that scope; project deletion additionally removes cardless
project records, including an empty project or implicit deletion of the last board's
project. Retain AgentTasks and unrelated evidence. Use the existing transaction;
no FK configuration changes or migration are needed.

## Verification design

V-1: Pipeline_evidence_is_purged_leaf_first_with_its_board_or_project (board/project)
asserts all five seeded tables are removed, board-only corrections are removed,
same-project sibling rows survive board deletion, other-project rows survive both,
and task history survives. Restrict FKs exercise the actual deletion order.

V-2: Pipeline_project_only_evidence_is_purged_with_an_empty_project_or_its_last_board
(empty project/last board) exercises cardless actions, observations and corrections
and retains another project's records.

R-1: Pipeline_evidence_is_restored_when_the_parent_delete_fails (board/project)
asserts transaction rollback restores all five tables and their scope parents.
R-2: full ProjectCascadeForeignKeyTests, ProjectDeletionTests,
BoardProjectArchiveApiTests and ProjectServiceTests cover adjacent deletion behavior.
R-3: whole Unit lane required by the Final profile and stage instruction; the task
paragraph's narrower no-Unit sentence conflicts with that profile. No full assembly
run is planned. Unit costs an estimated 10 minutes; persistence is bounded by R-2.
M-1: inspect the migration/model and both transaction callers for all five tables,
leaf ordering, board/project scope, retained tasks and rollback ownership.

PCs remain pending method-scoped SourceLanding Mutation, including missing-control
discovery. Candidate: docs/superpowers/mutations/2026-10-16-card-1303-r14-candidate.md.
No deliberate mutants in Code. No repetitions after green.

### Cost

S1 red demonstration: 5 minutes. S2 ordinary execution: 17 minutes. Authoring and
receipt inspection: 15 minutes. Tool bootstrap is an unlisted gated build, necessary
because this fresh worktree has no checkpoint executable. Red is expected only in
S1; S2 must rerun it green. Each group uses a fresh isolated output.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-r14-red/` | regression-red | `/*/*/ProjectCascadeForeignKeyTests/Pipeline*` | V-1, V-2, R-1 | all 3 methods; V-1/V-2 expected red before fix | 6 | 5 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-r14-final/` | cascade | `/*/*/ProjectCascadeForeignKeyTests/*` | V-1, V-2, R-1 | all listed, 0 failed | 47 | 5 |
| CP-3 | S2 | CP-2 | adjacent | `/*/*/(ProjectDeletionTests)\|(BoardProjectArchiveApiTests)\|(ProjectServiceTests)/*` | R-2 | all listed, 0 failed | 1 | 2 |
| CP-4 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed | 1 | 10 |
