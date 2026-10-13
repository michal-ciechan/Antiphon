# CARD-1199 repair 13: detect and report only

Operator decision 2026-10-12. Base 6e8b9cc231ea90981888e5ccdab4d519b4451ad2. Landing owner Code b56c49d8-21a1-409f-9dd7-8683de0c29ec, branch feat/card-task-b56c49d8, superseding Code 059e44d6-18b4-4ef2-b14e-5dc1211201cd, 3f0e5491-2cf8-4b0c-a590-bd79ee1f4052 and 1baae4ee-3a02-495f-925b-345b0630c01c. Repair base cea686acc7e352e55aa30163c753eae78b71d457.

S1: forged-ledger and missing-receipt regressions plus five recovered MSBuild scenarios, committed before fixes. The baseline run had 3 failures and 8 passes: the five recovered detection scenarios already pass on base production, so no functional red is claimed for their historical deletion. S2: remove automatic worktree/index recovery, exclusively create output, refuse existing collisions, qualify actual invalid receipts, current-process creation custody for cleanup, update census/docs.

### Invariants

- V-1: both entry points leave the build's resulting user bytes, deleted paths, links, and index untouched during settlement; no source recovery or preservation claim. Every detected tampered path is listed with a manual command. Clean builds still execute.
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
| CP-11 | S2 | CP-1 | receipt-and-source-regression | `/*/*/(ReportWriterTests*\|CheckpointSourceStateTests*\|CheckpointSourceExecutionTests*\|EvidenceFolderTests*)/*` | V-3 R-1 | full affected receipt/source classes; Windows sharing case disclosed under R-2 | 25 | 3 |
| CP-12 | S2 | CP-1 | linux-fifo-receipts | `/*/*/CheckpointReceiptSafetyTests/linux_fifo_receipts_refuse_within_four_seconds*` | V-5 F1 | nine combinations promptly exit 2, Linux gated | 1 | 1 |
| CP-13 | S2 | CP-1 | receipt-file-types | `/*/*/CheckpointReceiptSafetyTests/symlink_and_directory_receipts_refuse_within_four_seconds*` | V-5 F1 | eighteen combinations refuse, linked target unchanged | 1 | 1 |
| CP-14 | S2 | CP-1 | receipt-json-shapes | `/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals*` | V-6 F2 | required collection/string shapes refuse; state unchanged | 1 | 2 |
| CP-15 | S2 | CP-1 | shared-verified-reader | `/*/*/PlanCoverageHandleTests/*` | R-4 V-5 | full affected reader class, all sixteen cases | 16 | 1 |
| CP-16 | S2 | CP-1 | master-path-binding | `/*/*/CheckpointBuildBindingPathTests/*` | R-1 | all three retained master cases, 0 failed/skipped | 3 | 1 |

| CP-17 | S2 | CP-1 | nonempty-receipt-identities | `/*/*/CheckpointReceiptSafetyTests/receipt_row_and_build_identities_refuse_blank_without_writing*` | V-6 F1 | four handwritten input rows; all receipt-consuming verbs, no writes on refusal | 4 | 2 |

### Mutation handoff

No deliberate mutants run by Code. All PCs/variants and missing-control discovery, including this zero-PC plan, remain pending method-scoped SourceLanding Mutation after ordinary Review and caller landing. Candidate documentation: docs/superpowers/mutations/. Restart: none.

### Cost

Ordinary floor 42 minutes plus implementation (41 original rows plus the one-minute master regression row). Authoritative task budget 75 minutes. No broad assembly or unbounded classes selected.

The continuation sections below record historical source-qualified outcomes and obligations. Repair 13g is the current selection; earlier unresolved states and budgets describe their named historical sources.

### Historical repair 12c result (superseded; not repair 13 evidence)

The inherited fixture repairs at 8bb2b2dfab02f0ca89f1d39024cb28f2c84bc1a3 passed all nine closed rows in one uninterrupted run on 2026-10-13: 165 executed, 165 passed, zero failed or skipped. Every named class/method had nonzero fresh TRX results. The hanging-Git method passed consecutively in CP-1, CP-7 and CP-8; the compiled namespace census independently confirmed the literal 488. The clean source receipt validated against that exact tested SHA. No further production/test changes, timeout changes, assertion removals or deliberate mutants were needed in repair 12c. Windows junctions remain unqualified as R-2 requires. This ownership/result note and the repair report are documentation added after the run; its receipts retain the actual tested SHA. Full evidence and unedited checkpoint lines: [.antiphon/task-1baae4ee.md](../../../.antiphon/task-1baae4ee.md). Next: ordinary Review, then caller landing and method-scoped SourceLanding Mutation.

### Repair 13 unresolved Final verification

The completed Final run at 8035e4ac7448af3500ee2d6f80e609951ac6fbcf has red rows; its results do not qualify later repairs. Two late regressions were added tests-first: exclusive gate readback while the handle is held, and foreign terminal receipts. The gate fix leaves open streams without deletion fingerprints (they are retained), and receipt consumers bind terminal state to the requested directory. The compiled namespace census literal is now 501. Required next Code work: one full closed CP-1..CP-11 run at the latest committed HEAD, fresh TRX inspection, baseline confirmation of any remaining inherited reds, and rerun red rows. No current green claim; all PCs remain pending.

### Repair 13b continuation

The uninterrupted full closed run at b4b281abff6830b9addf9b60ff15452014ddf668 passed 204 executions, zero failures, with one disclosed Windows sharing skip. It qualified both late regressions, the three repaired cleanup fixtures, the independent census 501 and three consecutive hanging-Git passes. A separate native receipt audit then reproduced ten incorrect success exits in report/stop across fifteen outcomes. The existing receipt regression was expanded (no new compiled case count) before the production repair; its attempted TUnit baseline exposed a compile-overload error, then an inadmissible unknown-source merge fixture, neither credited as defect-red evidence. Native results are the actual defect baseline. S3 validates required state/report pairs before report rendering/merging and terminal stop side effects; invalid source fields refuse with a named reason. The healthy merge fixture now supplies eligible source evidence. Required post-S3 ordinary rows are the complete affected CP-1 and CP-11 classes; CP-2..CP-10 retain their actual b4b281ab evidence for unchanged production paths. All Mutation controls remain pending.

### Repair 13c continuation

This finish-line brief requires the complete CP-1..CP-11 manifest after S3, superseding the previous continuation's narrower rerun instruction. Budget: 60 minutes; no Unit lane or full assembly. Tests-first commit 06cc44d900ee71decbe08cc55d359ab122191813 supplied eligible merge source; its first run exposed a status fixture root error and is not credited as defect red. Commit 5de862c47e7bea2fa144d231a199a38fb9c4236a corrected the root. Its method-scoped CP-RED at 20261013-081424-7aa8 executed one case, failed one assertion: missing-md report-json returned 0 instead of required 2. Production remained unchanged. The S3 fix is cherry-picked from 5b58ed50bbb866d5f93eeb4b23a78b79ec180644; the already-proved local fixture is retained. Ordinary verification remains pending at this source slice. All PCs remain pending SourceLanding Mutation.

Repair 13c Final completed at f58eae39576ce4e48719029ec12595715d07e3b6, run 20261013-082401-bb7d: CP-1..CP-11 recorded 204 passed, zero failed and one planned Windows sharing skip. All named classes and required methods had fresh TRX results; census 501 and chronological hanging-Git CP-1/7/8 passed. Strict receipt validation passed CP-1..CP-10; the full zero-skip validator correctly refused CP-11 (row_failed), whose source/build/digest/count facts were inspected separately without modifying its receipt. Separate wait exited 2 because a new process cannot delete the retained tool shadow copy; stored checkpoint verdict is GREEN, exit 0. Ordinary Linux verification is complete; R-2 and all SourceLanding Mutation controls remain pending. Full report and unedited checkpoint lines: .antiphon/task-b56c49d8.md. Next: ordinary Review, then caller landing and SourceLanding Mutation. Restart: none.

### Repair 13d verification design

Landing owner Code 6284d6ec-03f7-4a88-b9f5-12a601bfe017, branch feat/card-task-6284d6ec; repair base 77fc640757f1cd87f8d7d4e512053f65a84aaf1c. V-5: required state/report/build receipt reads inspect paths without following links, refuse nonregular files before open, and recheck opened handles; Linux open is nonblocking. V-6: deserialized collection/string shapes are validated before verb dereferences, missing required facts cannot hide behind defaults, and malformed input yields named exit 2. CP-RED selects the new full class with unchanged base production; CP-1..14 is Final closed scope, no Unit/full assembly. Original counts remain 204 passes plus Windows skip; additive rows total 207 passes plus that skip. Independent census is 504 (501 original plus three methods). Budget 50 minutes, expected two isolated builds (tests-first and fix) plus small tool bootstraps; no unbounded classes. R-2 and existing qualification limits remain pending.

| PC | Detecting method/filter | Variant | Status |
|---|---|---|---|
| PC-1 | `/*/*/CheckpointReceiptSafetyTests/linux_fifo_receipts_refuse_within_four_seconds` | nine verb/path pairs; precheck or nonblocking-open removal | pending SourceLanding Mutation |
| PC-2 | `/*/*/CheckpointReceiptSafetyTests/symlink_and_directory_receipts_refuse_within_four_seconds` | eighteen verb/path/type pairs; type/link guard removal | pending SourceLanding Mutation |
| PC-3 | `/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals` | required field null/wrong-type/missing and element/empty variants; shape guard removal | pending SourceLanding Mutation |

R-4: shared reader extraction returns exact bytes through the already verified handle; full PlanCoverageHandleTests (16 cases) guards containment, links, size bounds, FIFO refusal and path replacement. CP-15 is required because receipt digests now use this same reader. Final new total: 223 passed plus one planned Windows skip, census 504. Tests-first baseline 383dac71a759b71b2f8158eb32dfeb3781e319c7: 3 failed, including nine hard-bounded FIFO hangs and symlink successes; native null fixture reports three NullReferenceExceptions. No timeout/assertion was relaxed. Required presence applies to serialized reference fields; nullable fields may carry null, empty diagnostic strings/collections remain allowed, identities and collection elements must be nonempty.

### Repair 13d continuation required

Budget-stopped at source 89bf1e2fda1787f00bd8c41091a2c3ea862b7022, run 20261013-100015-8d3e. CP-2 and CP-3 each have one fresh pass; CP-1 emitted stop_refuses_foreign_state_without_writing red because its three-field JSON is now malformed under the required-facts contract, before the foreign-owner check. The fixture is repaired to serialize a complete foreign RunState; every existing exit, bytes-survival and named-refusal assertion remains unchanged. That fixture amendment is NOT qualified yet. CP-1 and CP-4..CP-15 have no completed qualifying row; all V/R completion remains pending. Next Code must run the full contiguous CP-1..CP-15 manifest at its committed HEAD with one fresh isolated output, inspect every TRX/roster and re-run red rows. No green Final claim; native Windows and PC-1..PC-3/all inherited variants remain pending. Current fetched target independently conflicts at the original task base in SourceRunGuard.Expected; Review/landing must reconcile project-file output derivation with target directory normalization. Report: .antiphon/task-6284d6ec.md.

### Repair 13f master replay

Task f3302b7a starts at master 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2. All CARD-1199 commits absent from master are replayed, including prerequisites before 3dffd8d2c that the requested continuation range assumes. The SourceRunGuard conflict combines master's NormalizeDirectory producer/comparison behavior with candidate ProjectWriteRoots.DirectoryOf project-file handling. No rebase or merge commits.

The inherited plan requires census 504, not the earlier brief's 501. Preserve that 504-case CARD-1199 roster and the three master CheckpointBuildBindingPathTests; the merged independent census therefore requires 507. CP-16 explicitly verifies those existing master methods, reusing the CP-1 build. This additional row is required by the replay brief's master-behavior proof, not an extra repeat or broad selection. CP-1..CP-15 remain unchanged. Expected combined total: 226 passed plus one disclosed Windows sharing skip. All V-1..V-6/R-1..R-4 obligations and pending PC variants remain as above; native R-2 remains pending on Linux.

### Repair 13g verification design

Landing owner Code 2a33144d-0f31-4ca1-a086-bbde8fee6edd, branch feat/card-task-2a33144d; repair base c30634df617dd2b31f0b2e8271f18c7faa786a44. F1 is enforced: row and build identities in state/report receipts must be nonempty, including whitespace-only refusal. CP-17 is tests-first with handwritten empty, whitespace, null and valid argument rows. It exercises report, report --json, report --merge, report --merge --json, status, terminal stop, wait, explicit clean, and report validate. The valid clean control retains its documented exit 2; validate consumes report only. Invalid controls assert named refusals and unchanged complete run-file inventories/bytes.

CP-RED selects CP-17 against unchanged base production. Final selects CP-1..CP-17 with --serial so CP-1, CP-7 and CP-8 hanging-Git passes cannot overlap. Each run owns one isolated build with UseAppHost=false and a trailing forward-slash OutputPath. The tests-first build is necessary to prove the guarded defect; the fix needs a fresh build because source changed. No Unit lane or full assembly. Expected Final total: 230 passed, zero failed, one planned Windows sharing skip. Independent census: 511 (507 inherited plus four argument rows). Budget 50 minutes; bounded affected classes only.

| PC | Detecting method/filter | Variant | Status |
|---|---|---|---|
| PC-4 | `/*/*/CheckpointReceiptSafetyTests/receipt_row_and_build_identities_refuse_blank_without_writing` | row/build identity guard removal; state/report x empty/whitespace/null x each admitted receipt consumer; valid controls and no-write checks | pending method-scoped SourceLanding Mutation |

V-1..V-6 and R-1/R-3/R-4 retain their full closed selection. R-2 native Windows qualification and every inherited/new PC remain pending. Post-land Mutation has not run. Restart: none.

Tests-first proof at 8ce2bf272fb902f296d258c69fe8a87b98dcf749, unchanged c30634df6 production: CP-RED ran four argument cases and failed four. Empty and whitespace controls each caught success exits for invalid state/report row/build IDs; null caught two state wait exits 6 instead of 2. The valid control separately exposed a fixture-only row/line mismatch in validate (row_receipt_missing); it is not defect-red evidence. The repaired valid argument uses the existing CP-fixture ID so the receipt line agrees without weakening assertions. Final verification remains pending until the complete CP-1..CP-17 run.
