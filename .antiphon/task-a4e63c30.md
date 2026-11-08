# CARD-1783 Review round 3 (blockers only): CLEAN

Subject: origin/feat/card-task-63affa8d tip 4c374c2d0697c69962ddf13e7b539f1a6d5f7e0d. Diff range cfefd4b64e..4c374c2d06 (2 commits: witness 9537de1764, then fix).

## D-2 closed
- docs/orchestration-loop.md adds an Environment column to the Merge table. The CP-merge-subset-e2e cell is `Delegation__CheckInterpreterWorkingDirectory=/tmp/antiphon-merge-e2e-check-interpreter`; the other rows are `n/a`.
- `import --plan` of the verbatim table extract gave `imported 3 rows` with 0 warnings. All three rows are `isCommand: false`, with minExecuted 21/5/1. The e2e row gets `environment: {Delegation__CheckInterpreterWorkingDirectory: /tmp/antiphon-merge-e2e-check-interpreter}`; the others get `{}`. The importer accepts this syntax.
- The documented E2E row ran through the checkpoint tool (run 20261108-164929-73b5): executed=5 passed=5 failed=0, outputs `deleted bin-merge-e2e/`, verdict GREEN exit=0. `validate` gave CHECKPOINT SOURCE VALID rows=1. No bin-* residue and no check-interpreter dir were left in tests/Antiphon.E2E. Server logs (TestOutput/Logs/_shared-app) show the check interpreter at /tmp/antiphon-merge-e2e-check-interpreter.
- AssertRows pins the exact Environment pairs for each row: one pair on e2e, empty on the others. The in-test mutant (cell -> `n/a`) is asserted to throw. merge_process_rows_import_as_one_test_row_per_project_with_minimums passed, so the mutant goes red. The witness commit 9537de1764 precedes the fix.
- The /tmp path on a Windows host would resolve drive-relative (for example C:\tmp\...). That is still outside the repo, and this card's Merge runs on Linux. Not a blocker.

## Runs (server2, reviewed SHA 4c374c2d06, checkpoint tool, slot-gated)
| run | rows | result |
|---|---|---|
| 20261108-164929-73b5 | CP-merge-subset-e2e (documented row) | 5/5, GREEN exit 0, VALID rows=1 |
| 20261108-165549-41af | CP-1 MergeHighRadiusSubsetTests+ImpactedClassesSelectorTests | 23/23 (5+18) |
| 20261108-165549-41af | CP-2 guard batch (TestClassificationPolicy/Guard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness) | 50/50 (39+1+5+2+3), GREEN exit 0, VALID rows=2 |

- check-evidence-diff: cfefd4b64e..HEAD commits=2 violations=0; 7f938e410f..HEAD commits=6 violations=0.
- merge-tree: `git merge-tree --write-tree --merge-base 7f938e410f origin/master(5fef2c23b) HEAD` is clean (tree 14e5ad612d). The 6 CARD-1783 commits land clean.

## Advisories (non-blocking, for cards)
- A-1: concurrent Merge E2E rows on one host share /tmp/antiphon-merge-e2e-check-interpreter. The writes are idempotent, so the risk is low. The fixture-level override (AntiphonAppFixture) remains the real fix, as the doc says.
- A-2: E2E runs leave the ignored tests/Antiphon.E2E/TestOutput/ in place (pre-existing). It was left in this mirror.
