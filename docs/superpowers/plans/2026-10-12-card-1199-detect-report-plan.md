# CARD-1199 repair 13: detect and report only

Operator decision 2026-10-12. Base 6e8b9cc231ea90981888e5ccdab4d519b4451ad2. Landing owner Code 3f0e5491-2cf8-4b0c-a590-bd79ee1f4052, branch feat/card-task-3f0e5491, superseding Code 1baae4ee-3a02-495f-925b-345b0630c01c. Repair base cea686acc7e352e55aa30163c753eae78b71d457.

S1: forged-ledger and missing-receipt regressions plus five recovered MSBuild scenarios, committed before fixes. The baseline run had 3 failures and 8 passes: the five recovered detection scenarios already pass on base production, so no functional red is claimed for their historical deletion. S2: remove automatic worktree/index recovery, exclusively create output, refuse existing collisions, qualify actual invalid receipts, current-process creation custody for cleanup, update census/docs.

### Invariants

- V-1: both entry points preserve the build's resulting user bytes, deleted paths, links, and index; no recovery or preservation claim. Every tampered path is listed with a manual command. Clean builds still execute.
- V-2: existing stamps, output files/directories and linked components refuse before build; bytes unchanged. Build and stamp use fresh owned output.
- V-3: negative receipt exists, binds the intended source/run/row and has exact invalid exit; missing receipt fails the test.
- V-4: cleanup authority is the in-memory record of entries created by this process, with size/hash at creation; a disk ledger cannot authorize deletion. External compiler payloads and separate-invocation outputs require manual removal; extra files/directories, changed files and links refuse.
- R-1: full named checkpoint classes, snapshot membership and compiled namespace census; keep output confinement, tracked preflight, unavailable/failing Git, task ownership and scheduler invariants.
- R-2: Windows junctions unverified on Linux, disclosed and pending native qualification.
- R-3: hanging Git method passes three consecutive ordinary executions, first in CP-1 then CP-7 and CP-8. No timeout changes.

### Checkpoints

Closed scope overrides broad Unit profile per repair brief. Linux only. One isolated test build per source slice, forward slash OutputPath and UseAppHost=false, --keep-outputs. Tool bootstrap is the only extra build needed to execute candidate tooling. No full assembly selection. CP-RED ran 11 new cases over unchanged cea686acc production at tests-first commit 761f428c19bbf950db7095c352669afd502b5ec1 using .antiphon/c1199r13/red-plan.md: 3 failed, 8 passed.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1199r13/` | closed-checkpoints | `/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*\|BaselineComparerTests*\|RunSchedulerTests*\|CheckpointManifestTests*\|CheckpointTaskOwnershipTests*\|CheckpointToolCopyCleanupTests*\|WaitCommandTests*\|RunStateStoreTests*\|CheckpointAppTests*\|CheckpointNamespaceCensusUsageTests*\|CheckpointOutputConfinementTests*\|CheckpointTrackedBaselineTests*)/*` | R-1 R-3 | all listed, 0 failed/skipped | 133 | 15 |
| CP-2 | S2 | CP-1 | staged-untracked | `/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
| CP-3 | S2 | CP-1 | staged-ignored | `/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
| CP-4 | S2 | CP-1 | index-added | `/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
| CP-5 | S2 | CP-1 | detect-only-shapes | `/*/*/CheckpointShapeConflictTests/*` | V-1 V-2 V-3 | all 17 cases, both entry points | 17 | 6 |
| CP-6 | S2 | CP-1 | owned-output-ledger | `/*/*/CheckpointOwnedOutputTests/*` | V-4 | all 10 cases, 0 failed/skipped | 10 | 1 |
| CP-7 | S2 | CP-1 | hanging-git-second | `/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound*` | R-3 | 1 passed | 1 | 1 |
| CP-8 | S2 | CP-1 | hanging-git-third | `/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound*` | R-3 | 1 passed | 1 | 1 |
| CP-9 | S2 | CP-1 | row-runner | `/*/*/RowRunnerTests/*` | R-1 V-3 | full 13 cases, 0 failed/skipped | 13 | 1 |
| CP-10 | S2 | CP-1 | required-script-receipt | `/*/*/CheckpointTrackedBaselineTests/passing_script_requires_writable_source_receipt*` | V-3 F2 | missing receipt is nonzero | 1 | 2 |
| CP-11 | S2 | CP-1 | receipt-and-source-regression | `/*/*/(ReportWriterTests*\|CheckpointSourceStateTests*\|CheckpointSourceExecutionTests*\|EvidenceFolderTests*)/*` | V-3 R-1 | full affected receipt/source classes; Windows sharing case disclosed under R-2 | 1 | 3 |

### Mutation handoff

No deliberate mutants run by Code. All PCs/variants and missing-control discovery, including this zero-PC plan, remain pending method-scoped SourceLanding Mutation after ordinary Review and caller landing. Candidate documentation: docs/superpowers/mutations/. Restart: none.

### Cost

Ordinary floor 36 minutes plus implementation. Authoritative task budget 75 minutes. No broad assembly or unbounded classes selected.

### Historical repair 12c result (superseded; not repair 13 evidence)

The inherited fixture repairs at 8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 passed all nine closed rows in one uninterrupted run on 2026-10-13: 165 executed, 165 passed, zero failed or skipped. Every named class/method had nonzero fresh TRX results. The hanging-Git method passed consecutively in CP-1, CP-7 and CP-8; the compiled namespace census independently confirmed the literal 488. The clean source receipt validated against that exact tested SHA. No further production/test changes, timeout changes, assertion removals or deliberate mutants were needed in repair 12c. Windows junctions remain unqualified as R-2 requires. This ownership/result note and the repair report are documentation added after the run; its receipts retain the actual tested SHA. Full evidence and unedited checkpoint lines: [.antiphon/task-1baae4ee.md](../../../.antiphon/task-1baae4ee.md). Next: ordinary Review, then caller landing and method-scoped SourceLanding Mutation.

### Repair 13 unresolved Final verification

The completed Final run at 8035e4ac7448af3500ee2d6f80e609951ac6fbcf has red rows; its results do not qualify later repairs. Two late regressions were added tests-first: exclusive gate readback while the handle is held, and foreign terminal receipts. The gate fix leaves open streams without deletion fingerprints (they are retained), and receipt consumers bind terminal state to the requested directory. The compiled namespace census literal is now 501. Required next Code work: one full closed CP-1..CP-11 run at the latest committed HEAD, fresh TRX inspection, baseline confirmation of any remaining inherited reds, and rerun red rows. No current green claim; all PCs remain pending.
