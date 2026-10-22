# Task 4921bd9e report: CARD-1493 S1 importer duplicate-build warning

Branch `feat/card-task-4921bd9e`, worktree `/work/worktrees/task-4921bd9e` (server2 runner mirror),
original Code task 4921bd9e (landing owner). Base `cb7af8ccd54934b7a53dd88ca458dad0b643eae6`.
Plan: `docs/superpowers/plans/2026-10-22-card-1493-s1-plan.md`.

## What changed

- `tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs`: at the row that first defines a build,
  a non-fatal warning when an earlier build in the plan has the same normalised project, the same
  expanded `After` and the same `Repeat` under a different `bin-<x>/` id. Exit code, error, manifest,
  builds and rows unchanged. Rule and normalisation are in the plan.
- `tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs`: V-1..V-4 (`C1493_*`).
- `docs/testing-and-build.md`: one sentence beside the existing import advisories.

Item chosen: card item 1 (pure, plan-text-only, no I/O). Items 2-4 stay on the card unchanged.

## Commits

- `337d9ecf7925ef5fe8c8919ac86ba42b5a97981d` test: red witnesses + plan (S1-test)
- `de5c8c74e9319101113566534481c2860606d4b7` feat: the warning + doc line (S1)
- this report commit; the CP-2/CP-3 receipt run starts at the tip that contains it.

## Checkpoints so far

Bootstrap tool: one gated isolated build `tools/Antiphon.Checkpoints -> bin-c1493tool/` at 337d9ecf
(slot granted, waited=0s, held 36s), used to run every row; disclosed unlisted build.

```
CHECKPOINT CP-1 commit=337d9ecf7925ef5fe8c8919ac86ba42b5a97981d build=ok filter=/*/*/CheckpointImportTests/(C1493_DuplicateBuild*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-4921bd9e/.antiphon/checkpoints/20261022-145754-f7d0/rows/CP-1/run.trx slot=granted waited=105s dirty=0 source=337d9ecf7925ef5fe8c8919ac86ba42b5a97981d sourceState=clean buildSource=verified
```

Red as designed: both failures are the intended `imported.Warnings should be [...] but was []`.
Run `20261022-145754-f7d0`, exit 1. Disclosure: when deleting the kept `bin-c1493-red/` output I
also removed the run's `builds/bin-c1493-red/` folder (its build.log); report.md/json, TRX and
failures.md are intact.

## M-1 census (manual, disclosed)

`import --plan` over all 890 `docs/superpowers/plans/*.md`, with the tool built at 337d9ecf (base
importer) and at de5c8c74 (second gated tool build `bin-c1493tooltip/`, waited 90s, held 8s):

- exit codes identical for all 890 (470 exit 0, 420 exit 2);
- 101 plans (all among the 470 that import) gain 356 duplicate-build warnings; every other
  warning count is unchanged;
- plans dated 2026-10-15 or later: 18 of 190 importable plans warn (e.g. the CARD-1480 plan's
  CP-3/CP-4 `bin-c1480-resolver`/`bin-c1480-dispatch` duplicate CP-2's `bin-c1480-green`).

## Landability

- `git merge-tree --write-tree origin/master HEAD`: clean (origin/master cc2237940).
- Per-commit `merge-tree --merge-base` chain onto `origin/rc/2026-10-20`: both commits clean; the
  importer and test file on rc equal master's base, so the pick compiles there as well.
- `scripts/check-evidence-diff.ps1` base..de5c8c74: commits=2 violations=0.

## Pending for SourceLanding Mutation

PC-1 drop After from the key, PC-2 drop Repeat, PC-3 skip project normalisation, PC-4 warning as
refusal, PC-5 warn on every reuse of one id (see plan). None run here.
