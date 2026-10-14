# CARD-0100 S1+S2 repair (Code 59dcf45c)

Outcome: CP-2's FK failure is fixed at the cause (test teardown order). CP-1..CP-4 ran once
through the checkpoint tool, all green (3/12/19/30). The tool exits 2 only for output cleanup
(`output-ledger-mismatch`, CARD-1242; tool validator `cleanup_refused`, CARD-1236).

- Branch `feat/card-task-59dcf45c`, worktree `/work/worktrees/task-59dcf45c` (desktop
  `C:\Antiphon\worktrees\card-task-59dcf45c`). Starts at 4e60f7c5f, the tip of Code b371d29d.
  No rebase and no merge of master.
- Landing owner: original Code task b371d29d (this repair stacks on its branch).
- Plan: `docs/superpowers/plans/2026-10-14-card-0100-card-relationships-plan.md`.
- Test design: `docs/superpowers/plans/2026-10-14-card-0100-test-design.md` (its `### Checkpoints`
  table is the one that ran).
- Evidence (gitignored): `.antiphon/checkpoints/20261014-060602-8a4c/` (report.md, report.json,
  rows/CP-n/run.trx) and the manifest `.antiphon/c0100-evidence/import.yml`.
- The author report `.antiphon/task-b371d29d.md` named in the brief does not exist in this
  checkout, on its branch, or on origin. I worked from the commit messages, the plan and the test
  design.

## Commits (this task)

| SHA | What |
|---|---|
| 48e21c990d3815b865d336d6b62c32f080913583 | Brings CARD-1238 into this branch: `tests/Antiphon.Tests/Antiphon.Tests.csproj` and `TestHelpers/BuildOutputNestingTests.cs` set to their master 8995111253 content (commits 92bcc28ef, f5979a733, 3714664c3). Here the csproj matched 6131aed14 byte for byte, so this takes exactly those changes with no conflict. The `docs/testing-and-build.md` hunk of f5979a733 is not taken. |
| 223f4794b5cfcc112bb83bdd7e38182354c294b5 | `CardCorrectionApiTests` After hook deletes `CardWorktreeCleanups` before `CardRevisions`. |
| (this report commit) | `.antiphon/task-59dcf45c.md` only. |

## CP-2 root cause

`Reopen_over_http_returns_the_card_live_again_with_history` is the only test in the class that
moves a card to Done:

1. On that move, `CardRevisionLog.AppendMove` (`server/Application/Services/CardRevisionLog.cs:50`,
   CARD-1017 e3aa561f2, already on master) adds a `CardWorktreeCleanup`.
2. That row's `FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId` is `Restrict`
   (`AppDbContext.cs:2931`).
3. The class's `[After(Test)]` deleted `CardRevisions` first, which raises 23503.

Neither the new join table nor CARD-0100 production code is involved:
- `CardRelationships` cascade on both FKs.
- The teardown is byte-identical on origin/master, so the red comes from master.
- I did not re-run it at the base commit because of host load. That conclusion rests on the code
  above, not on a base run.

The fix mirrors 4e60f7c5f for `CardRelationshipApiTests`. Production code is unchanged.

Related but out of scope:
- `server/Application/Services/ProjectCascade.cs:127` hard-deletes cards without clearing
  `CardWorktreeCleanups`, whose FK to `Cards` is also `Restrict`.
- So a project delete over a card that has ever been moved to Done would likely fail with the
  same 23503. That is a CARD-1017 production defect. It is not tested here, and I found no
  existing card for it (searched "ProjectCascade" and "CardWorktreeCleanups").
- Suggest filing a card.

## Checkpoint run 20261014-060602-8a4c

- Command: `run .antiphon/c0100-evidence/import.yml --rows CP-1,CP-2,CP-3,CP-4 --expected-source-sha 223f4794b5cfcc112bb83bdd7e38182354c294b5 --serial`.
- Manifest: imported from the test design, then edited to `timeouts.rowMinutes: 40`,
  `totalMinutes: 150`, and per-row `timeoutMinutes: 40` for CP-1..CP-4.
- Tool bootstrap: one unlisted build, slot-gated, into `bin-c0100-tool/` with `UseAppHost=false`
  (waited 75 s).
- The first `run` foreground wrapper was killed by my shell timeout while the detached executor
  kept running. I re-attached with `wait`; the run was not restarted.

```
source: 223f4794b5cfcc112bb83bdd7e38182354c294b5 state=clean buildSource=verified
CHECKPOINT CP-1 commit=223f4794b5cfcc112bb83bdd7e38182354c294b5 build=ok filter=/*/*/CardRelationshipSchemaTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-59dcf45c/.antiphon/checkpoints/20261014-060602-8a4c/rows/CP-1/run.trx slot=granted waited=391s dirty=0 source=223f4794b5cfcc112bb83bdd7e38182354c294b5 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=223f4794b5cfcc112bb83bdd7e38182354c294b5 build=reused filter=/*/*/CardCorrectionApiTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-59dcf45c/.antiphon/checkpoints/20261014-060602-8a4c/rows/CP-2/run.trx slot=granted waited=285s dirty=0 source=223f4794b5cfcc112bb83bdd7e38182354c294b5 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=223f4794b5cfcc112bb83bdd7e38182354c294b5 build=reused filter=/*/*/CardRelationshipApiTests/* executed=19 passed=19 failed=0 skipped=0 trx=/work/worktrees/task-59dcf45c/.antiphon/checkpoints/20261014-060602-8a4c/rows/CP-3/run.trx slot=granted waited=211s dirty=0 source=223f4794b5cfcc112bb83bdd7e38182354c294b5 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=223f4794b5cfcc112bb83bdd7e38182354c294b5 build=reused filter=/*/Antiphon.Tests.Application/(CardIdentifierResolutionTests*)|(CardTaskFileRendererTests*)/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-59dcf45c/.antiphon/checkpoints/20261014-060602-8a4c/rows/CP-4/run.trx slot=granted waited=60s dirty=0 source=223f4794b5cfcc112bb83bdd7e38182354c294b5 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 30m37s  sequential-equivalent: 23m44s  builds: 3  max-concurrent-builds: 1  rows: 4 green 0 red 0 skipped
refused: output-ledger-mismatch; left in place: /work/worktrees/task-59dcf45c/tests/Antiphon.Tests/bin-c0100-s12/fakegrok.dll
verdict: RED exit=2 reason=refused: output-ledger-mismatch; left in place: /work/worktrees/task-59dcf45c/tests/Antiphon.Tests/bin-c0100-s12/fakegrok.dll
```

The `wait` invocation also printed `TOOL CLEANUP Refused ... reason=output-memory-authority-missing`
for `<run>/tool`. That is the documented case where a separate `wait` keeps the tool directory.

TRX inspection (Counters and className census):
- CP-1: 3 `CardRelationshipSchemaTests`.
- CP-2: 12 `CardCorrectionApiTests`.
- CP-3: 19 `CardRelationshipApiTests`.
- CP-4: 18 `CardIdentifierResolutionTests` + 12 `CardTaskFileRendererTests`.
- Every row: executed = passed, with 0 failed, error, timeout, aborted or notExecuted.

Validators over `report.json` with expected SHA 223f4794b:
- `Antiphon.Checkpoints validate --rows CP-1,CP-2,CP-3,CP-4` gives
  `CHECKPOINT SOURCE INVALID reason=cleanup_refused` and exits 2.
- `scripts/validate-checkpoint-receipt.ps1` gives
  `CHECKPOINT SOURCE VALID source=223f4794b5cfcc112bb83bdd7e38182354c294b5 rows=4` and exits 0.

Outputs removed afterwards (literal paths):
- `tests/Antiphon.Tests/bin-c0100-s12/` (774 MB)
- `tools/Antiphon.Checkpoints/bin-c0100-tool/`
- the run's `tool/` shadow copy

No `bin-c0100-*` remains outside the evidence folder. `bin-c0100-s3/` and `bin-c0100-s4/` were
never created.

## V/R and PCs

- V-1 (CP-1): passed.
- R-1 (CP-2): passed.
- V-2, V-3, V-4, V-5 (CP-3): passed.
- R-1b and R-2 (CP-4): passed.
- Not in this task: V-6/R-3 (S3), V-7/R-4 (S4), V-8/R-5 (S5) and CP-5..CP-11. They are not run
  and not passed.
- No whole-Unit lane, as the brief directed.
- Mutation not run. PC-1..PC-31 (the server-side PCs for S1+S2 per the test design) stay pending
  for SourceLanding Mutation. The 23503 fix is test infrastructure and has no PC.

## Source checks

- Merge-base with origin/master is 88540babcef8413c662a34a07f2e19f7055ca5d9; origin/master is
  8995111253ae0b54b66c2d480aee76508d265220.
- `git merge-tree --write-tree origin/master HEAD` exits 0 (tree 7fef20a56), with no conflicts.
- `scripts/check-evidence-diff.ps1 -BaseRef 88540babcef8413c662a34a07f2e19f7055ca5d9 -HeadRef HEAD`
  gives `EVIDENCE result commits=6 entries=0 violations=0` and exits 0. That ran at HEAD
  223f4794b, before this report commit.
