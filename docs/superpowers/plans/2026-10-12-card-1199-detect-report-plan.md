# CARD-1199 repair 12: detect and report only

Operator decision 2026-10-12. Base 6e8b9cc231ea90981888e5ccdab4d519b4451ad2. Landing owner Code 1baae4ee-3a02-495f-925b-345b0630c01c, branch feat/card-task-1baae4ee, superseding Code 6c9fe082-cfe7-48bc-9d40-a9a58918da4f under the repair 12c brief.

S1: failing F1/F2/F3 tests, committed before redesign. S2: remove automatic worktree/index recovery, exclusively create output, refuse existing collisions, qualify actual invalid receipts, ledger-bound cleanup, update census/docs.

### Invariants

- V-1: both entry points preserve the build's resulting user bytes, deleted paths, links, and index; no recovery or preservation claim. Every tampered path is listed with a manual command. Clean builds still execute.
- V-2: existing stamps, output files/directories and linked components refuse before build; bytes unchanged. Build and stamp use fresh owned output.
- V-3: negative receipt exists, binds the intended source/run/row and has exact invalid exit; missing receipt fails the test.
- V-4: ledger cleanup removes unchanged owned outputs only; extra files/directories, changed files and links refuse.
- R-1: full named checkpoint classes, snapshot membership and compiled namespace census; keep output confinement, tracked preflight, unavailable/failing Git, task ownership and scheduler invariants.
- R-2: Windows junctions unverified on Linux, disclosed and pending native qualification.
- R-3: hanging Git method passes three consecutive ordinary executions, first in CP-1 then CP-7 and CP-8. No timeout changes.

### Checkpoints

Closed scope overrides broad Unit profile per repair brief. Linux only. One isolated test build per source slice, forward slash OutputPath and UseAppHost=false, --keep-outputs. Tool bootstrap is the only extra build needed to execute candidate tooling. No full assembly selection. CP-RED ran the new tests over unchanged base production, expected red, in /tmp/c1199-r12-red-plan.md.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1199r12/` | closed-checkpoints | `/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*\|BaselineComparerTests*\|RunSchedulerTests*\|CheckpointManifestTests*\|CheckpointTaskOwnershipTests*\|CheckpointToolCopyCleanupTests*\|WaitCommandTests*\|RunStateStoreTests*\|CheckpointAppTests*\|CheckpointNamespaceCensusUsageTests*\|CheckpointOutputConfinementTests*\|CheckpointTrackedBaselineTests*)/*` | R-1 R-3 | all listed, 0 failed/skipped | 125 | 15 |
| CP-2 | S2 | CP-1 | staged-untracked | `/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
| CP-3 | S2 | CP-1 | staged-ignored | `/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
| CP-4 | S2 | CP-1 | index-added | `/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
| CP-5 | S2 | CP-1 | detect-only-shapes | `/*/*/CheckpointShapeConflictTests/*` | V-1 V-2 V-3 | all 17 cases, both entry points | 17 | 6 |
| CP-6 | S2 | CP-1 | owned-output-ledger | `/*/*/CheckpointOwnedOutputTests/*` | V-4 | all 5 cases, 0 failed/skipped | 5 | 1 |
| CP-7 | S2 | CP-1 | hanging-git-second | `/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound*` | R-3 | 1 passed | 1 | 1 |
| CP-8 | S2 | CP-1 | hanging-git-third | `/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound*` | R-3 | 1 passed | 1 | 1 |
| CP-9 | S2 | CP-1 | row-runner | `/*/*/RowRunnerTests/*` | R-1 V-3 | full 13 cases, 0 failed/skipped | 13 | 1 |

### Mutation handoff

No deliberate mutants run by Code. All PCs/variants and missing-control discovery, including this zero-PC plan, remain pending method-scoped SourceLanding Mutation after ordinary Review and caller landing. Candidate documentation: docs/superpowers/mutations/. Restart: none.

### Cost

Ordinary floor 31 minutes plus implementation. Authoritative task budget 75 minutes. No broad assembly or unbounded classes selected.

### Repair 12c ordinary result

The inherited fixture repairs at 8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 passed all nine closed rows in one uninterrupted run on 2026-10-13: 165 executed, 165 passed, zero failed or skipped. Every named class/method had nonzero fresh TRX results. The hanging-Git method passed consecutively in CP-1, CP-7 and CP-8; the compiled namespace census independently confirmed the literal 488. The clean source receipt validated against that exact tested SHA. No further production/test changes, timeout changes, assertion removals or deliberate mutants were needed in repair 12c. Windows junctions remain unqualified as R-2 requires. This ownership/result note and the repair report are documentation added after the run; its receipts retain the actual tested SHA. Full evidence and unedited checkpoint lines: [.antiphon/task-1baae4ee.md](../../../.antiphon/task-1baae4ee.md). Next: ordinary Review, then caller landing and method-scoped SourceLanding Mutation.
