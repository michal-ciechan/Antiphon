# CARD-1199 repair 13f — Code f3302b7a

Replay and ordinary Linux Final verification are complete and pushed: CP-1..CP-15 have 223 passes plus one planned Windows sharing skip; repaired master-regression CP-16 has all three passes. Final selected total 226 passes, zero unresolved failures, one disclosed skip. Native Windows R-2 and all SourceLanding Mutation PCs remain pending. Next: ordinary Review.

Code/Review subject: f3302b7a-cae8-49be-a0ff-95bbb13a3666. Branch feat/card-task-f3302b7a, worktree /work/worktrees/task-f3302b7a; assigned desktop C:\Antiphon\worktrees\card-task-f3302b7a is unreachable. Original predecessor Code line: 6284d6ec-03f7-4a88-b9f5-12a601bfe017 (tip f3fd89a3d0edf38bc486d76ecc1cb6d516b7364d), with intervening blocked Code 361919bc. The current dispatch has no structured repairSourceTaskId; caller owns landing/adoption of this reviewed replay, not the old divergent tip. Task base and fetched target: 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2. No rebase, amend, reset, force push, merge commit, restart or deployment.

Plan: /work/worktrees/task-f3302b7a/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md. Report: /work/worktrees/task-f3302b7a/.antiphon/task-f3302b7a.md. Ignored evidence: /work/worktrees/task-f3302b7a/.antiphon/c1199r13f/ and the exact checkpoint folders below. CARD-1199 and both supplied predecessor reports were read in full. Authenticated GET /api/runner-defaults returned revision 2 and GET /api/session-runners returned three runners; no Runner/Platform pin was applied.

## Replay and fidelity

The requested 3dffd8d2c..f3fd89a3d continuation comprises 19 commits, but its earlier CARD-1199 prerequisite source is also absent from master. Starting at the requested master and replaying only those 19 would omit the repair's confinement, custody and detection redesign. Therefore replayed all 68 CARD-1199 commits absent from master, including the 49 prerequisites through 3dffd8d2c. `git cherry origin/master origin/feat/card-task-6284d6ec` marked every one `+`; no patch-already-present or empty commit was skipped. Each cherry-pick was individually pushed to the assigned ref.

One content conflict occurred while replaying 530a278c2254a26bf7829a4674683802936970d8. Exact conflicting initializer:

```text
<<<<<<< HEAD
        RepositoryRoot = NormalizeDirectory(root),
        Project = NormalizeDirectory(Path.Combine(root, project)),
        OutputPath = NormalizeDirectory(Path.Combine(root, project, output)),
=======
        RepositoryRoot = Path.GetFullPath(root),
        Project = projectFull,
        OutputPath = Path.GetFullPath(Path.Combine(ProjectWriteRoots.DirectoryOf(projectFull), output)),
>>>>>>> 530a278c2 (fix(CARD-1199): refuse tracked output across the project graph before building)
```

Resolution:

```csharp
        RepositoryRoot = NormalizeDirectory(root),
        Project = NormalizeDirectory(projectFull),
        OutputPath = NormalizeDirectory(Path.Combine(ProjectWriteRoots.DirectoryOf(projectFull), output)),
```

Master's NormalizeDirectory implementation and normalized repository/project/output comparisons remain; candidate project-file directory handling, safe receipt reader and exclusive custody behavior remain. `git range-diff cdc2cd3112bb146787248c28a3e7b63bd3b13beb..f3fd89a3d0edf38bc486d76ecc1cb6d516b7364d 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2..e3f2766d6` pairs all 68 commits. Only two pairs differ: 530a278c's resolved initializer and c4fb0b936's context carrying master's normalized comparison around the safe-deserializer edit. All other 66 pairs are patch-identical. Net checkpoint source/test/script differences from the old tip before the later fixture repair are precisely SourceRunGuard normalization, retained master's three-case CheckpointBuildBindingPathTests, census 504 -> 507, and the plan's added master row. The testing guide additionally retains master's unrelated CARD-0989 verified-download documentation. No original CARD-1199 change was omitted.

`git merge-tree --write-tree HEAD origin/master` passed exit 0 against the fetched target; it is ancestry compatibility, not the fidelity proof. The range-diff supplies the latter. Full evidence-history guard passed base..4012260494488d152110f1d025020e6c0d4c2eda: 69 commits, 8 introduced evidence entries, 0 violations. Final full-range guard is repeated after the report commit; its exact outcome is in the caller-facing report.

## Scope, counts and provenance

Explicit brief/closed manifest override the generic Final Unit profile: no Unit lane, namespace-wide test selection or full assembly. No unbounded classes. The original plan requires independent census 504, not 501; all 504 CARD-1199 cases are retained. Master's additional three compiled binding cases make the merged independent literal 507. The plan now records 507 and adds CP-16 to run those exact three existing master methods, reusing CP-1's build. Ordinary estimated floor is 42 minutes (41 original rows + 1 master row), budget 75 minutes.

Tool bootstrap is the only unlisted build: required to execute candidate tooling, gated with UseAppHost=false and OutputPath=bin-c1199r13f-tool/. Build passed, slot=granted waited=75s held=8s, one existing TaskOwnerGuard nullable warning, zero errors. The candidate tool was reused for the later test-only fixture repair because production tool source did not change. No unlisted test driver or additional hanging-Git repetition occurred.

Initial full run 20261013-104034-e204 tested committed 4012260494488d152110f1d025020e6c0d4c2eda. One isolated build bin-c1199r13/, never a build id named tests; UseAppHost=false; --keep-outputs. Build 488.3511965s, slot=granted waited=45s. All 16 rows acquired their own granted leases with waited=0s. Source start/end dirty=0; SHA and fingerprint match; build-source provenance verified. Stored verdict RED exit 1 is preserved: 225 passed, 1 failed, 1 planned skip, with the sole failure CP-16. No result is relabeled green.

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-1 | 133 | 133 | 0 | 0 |
| CP-2 | 1 | 1 | 0 | 0 |
| CP-3 | 1 | 1 | 0 | 0 |
| CP-4 | 1 | 1 | 0 | 0 |
| CP-5 | 17 | 17 | 0 | 0 |
| CP-6 | 10 | 10 | 0 | 0 |
| CP-7 | 1 | 1 | 0 | 0 |
| CP-8 | 1 | 1 | 0 | 0 |
| CP-9 | 13 | 13 | 0 | 0 |
| CP-10 | 1 | 1 | 0 | 0 |
| CP-11 | 25 | 25 | 0 | 1 |
| CP-12 | 1 | 1 | 0 | 0 |
| CP-13 | 1 | 1 | 0 | 0 |
| CP-14 | 1 | 1 | 0 | 0 |
| CP-15 | 16 | 16 | 0 | 0 |
| CP-16 initial | 3 | 2 | 1 | 0 |

Fresh TRX was inspected independently through TestDefinitions/UnitTest/TestMethod identity and UnitTestResult.testId mappings, with exact executed/pass/fail/skip counters compared to each row receipt. All named class/method selections have nonzero results. CP-1 class counts: OutputCleanup 29, BaselineComparer 7, RunScheduler 15, Manifest 7, TaskOwnership 24, ToolCopyCleanup 16, WaitCommand 12, RunStateStore 1, CheckpointApp 3, NamespaceCensus 1, OutputConfinement 5, TrackedBaseline 13. CP-11 counts including skip: ReportWriter 6, SourceState 8, SourceExecution 6, EvidenceFolder 6. CP-5/6/9/15/16 match 17/10/13/16/3. All exact single-method rows matched. Report pair SHA-256 was independently verified over exact Markdown bytes, and row/build source envelopes checked against actual tested SHA. Inspection script's initial uppercase/lowercase digest representation comparison was corrected; no stored receipt was edited.

Both `scripts/validate-checkpoint-receipt.ps1` and tool `validate` passed for initial CP-1..CP-10,CP-12..CP-15 (14 rows) against 4012260494488d152110f1d025020e6c0d4c2eda. CP-11-only and full strict validation return exit 2 row_failed: CP-11 carries the planned Windows sharing skip, and full run additionally carries CP-16 failure. CP-11 source/build/digest/count facts remain verified separately, without modifying receipts. Native Windows is not qualified. Separate wait's original stored-red exit was 1; a later green separate wait may return 2 for retained tool-shadow custody and is disclosed separately.

## CP-16 fixture repair

Initial Different_directory_or_provenance_is_refused fails the healthy-companion assertion for project: a prior repositoryRoot tamper remains because repeated binding.Write refuses pre-existing stamp writes under CARD-1199. This is a fixture incompatibility introduced by combining master's test with the intentional exclusive-write contract, not an inherited defect or an excuse to restore overwrite behavior. Equivalent-spelling fixture also attempted ignored repeated writes; it now uses fresh independent fixtures for every spelling, preventing cross-variant carryover. All producer/audit, equivalent spellings, Linux case-distinction, missing stamp, eight different-directory/provenance refusal and healthy-companion assertions remain. Added assertions require each fresh stamp Write to succeed. No timeout widened, assertion weakened, sleep or retry added.

Repair committed/pushed 97bc9378ccf8716e4e849d33d0d7e302fc7747e9. Only CheckpointBuildBindingPathTests changes after the full baseline. Following the required fix-and-target rule, rerun the full affected CP-16 class through the same checkpoint manifest at that SHA, with a fresh isolated build after reclaiming only the original task-created output. CP-1..CP-15 production/test/script/plan inputs are unchanged; their original evidence remains bound to its actual SHA. Repeating the hanging-Git battery would exceed its three unchanged ordinary proof selections, so no extra battery is claimed or required.

Initial owned compiler output reclaimed after executor death and baseline inspection: 97,867 files, 39,819,226,960 bytes, exact path /work/worktrees/task-f3302b7a/tests/Antiphon.Tests/bin-c1199r13. The cleanup checks task root, exact producer-owned allowlist, zero tracked files and no links. Tool shadows/checkpoint evidence remain ignored and retained; no unrelated outputs are removed.

## Ordinary invariant outcomes

V-1 PASS: CP-1..CP-5 preserve resulting bytes/deletions/links/index, disclose manual commands and keep clean execution; all named recovered scenarios pass. V-2 PASS: CP-1/5 prove pre-existing and linked output/stamp collision refusals without repair. V-3 PASS on Linux: CP-5/9/10/11 prove mandatory eligible bound receipts, missing receipts are nonzero, both report forms and stop refuse malformed receipts. CP-11's native Windows sharing case is pending.

V-4 PASS: CP-1/6 prove in-memory creation custody, forged persisted ledgers never authorize deletion and compiler/separate-invocation/extra/changed/link outputs remain for manual handling. V-5 PASS: CP-12's nine FIFO verb/path combinations all promptly exit 2 inside the unchanged four-second hard bound; CP-13's eighteen symlink/directory combinations all refuse and preserve linked target; CP-15's sixteen shared-reader cases pass. V-6 PASS: CP-14 checks report/state required null, wrong-type, missing, empty-string and collection-element/empty-value variants across verbs; named exit 2, no exception success or state rewrite.

R-1 PASS for original named classes/compiled census 507 at baseline; retained master full-class CP-16 also PASS at 1548d4f4a62955361b13081110019efc76c783a8 after the fixture repairs below. R-2 PENDING native Windows junction/handle/sharing qualification, as explicitly accepted in the brief. R-3 PASS: three consecutive ordinary hanging-Git executions, CP-1 10:49:44.5135907–10:49:59.8083755, CP-7 10:55:12.9572536–10:55:28.2787002, CP-8 10:55:48.3147147–10:56:03.6611910 UTC; zero reds, no loaded or extra repeats. R-4 PASS: all sixteen PlanCoverageHandleTests, confinement/links/size/FIFO/path-replacement and exact-byte shared-reader behavior.

Inherited defect-red evidence remains predecessor qualification, not a new Mutation run: tests-first 383dac71a759b71b2f8158eb32dfeb3781e319c7 had 3 expected failures, all nine FIFO children hung/killed/awaited at four seconds and eight symlink success exits; native null report/report-json/stop yielded NullReferenceException. The replay preserves those tests/fixes. No deliberate mutant was run.

## Mutation handoff

Post-land Mutation NOT run. PC-1 all nine FIFO verb/path variants pending; PC-2 all eighteen symlink/directory verb/path/type variants pending; PC-3 every required-field null/wrong-type/missing, element and empty-value variant pending. All inherited forged/missing/outside ledgers, extra/changed/linked outputs, sixteen preservation shapes, source/digest/writability report-pair controls, held gates, foreign terminal bindings and missing-control discovery remain pending method-scoped SourceLanding Mutation. No ordinary or nightly pass discharges a PC. Candidate documentation remains unbound under docs/superpowers/mutations/. Next: ordinary Review; CP-16 is green; caller lands the original commissioned Code subject/adopted replay then commissions SourceLanding Mutation. Restart: none.

## Unedited initial CHECKPOINT lines

```text
CHECKPOINT CP-2 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointShapeConflictTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointOwnedOutputTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=4012260494488d152110f1d025020e6c0d4c2eda build=ok filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*|BaselineComparerTests*|RunSchedulerTests*|CheckpointManifestTests*|CheckpointTaskOwnershipTests*|CheckpointToolCopyCleanupTests*|WaitCommandTests*|RunStateStoreTests*|CheckpointAppTests*|CheckpointNamespaceCensusUsageTests*|CheckpointOutputConfinementTests*|CheckpointTrackedBaselineTests*)/* executed=133 passed=133 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/RowRunnerTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointTrackedBaselineTests/passing_script_requires_writable_source_receipt* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointReceiptSafetyTests/linux_fifo_receipts_refuse_within_four_seconds* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointReceiptSafetyTests/symlink_and_directory_receipts_refuse_within_four_seconds* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/PlanCoverageHandleTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/(ReportWriterTests*|CheckpointSourceStateTests*|CheckpointSourceExecutionTests*|EvidenceFolderTests*)/* executed=25 passed=25 failed=0 skipped=1 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=4012260494488d152110f1d025020e6c0d4c2eda build=reused filter=/*/*/CheckpointBuildBindingPathTests/* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-104034-e204/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=4012260494488d152110f1d025020e6c0d4c2eda sourceState=clean buildSource=verified
```

## First fixture rerun — retained red evidence

Run 20261013-110123-4cda at 97bc9378ccf8716e4e849d33d0d7e302fc7747e9 executed CP-16: 3 executed, 0 passed, 3 failed, 0 skipped; stored RED exit 1 and separate wait exit 1. Build passed in 426.8181679s; build and row slot=granted waited=0s. The newly asserted Write return exposed output-owner-missing in every fresh fixture: Write had created the stamp before refusing its output seal. This additional fixture error is not credited as a guarded production-defect baseline. Full TRX/digest/source/build facts were inspected and retained.

Second fixture repair 1548d4f4a62955361b13081110019efc76c783a8 creates the fixture-owned parent and calls SafeWrite.CreateFreshDirectory exactly as RowRunner/RunScheduler do before binding.Write. This establishes genuine in-process directory custody and the owner record; no fake ledger or production bypass. Every new Write assertion and every original behavioral assertion remains. CP-16's third ordinary selection is the required red-row repair rerun, with no unchanged green repetition. No hanging-Git repeats were added.

A mistyped expected SHA in one launcher invocation was refused exit 2 source_mismatch or source_dirty before any run, build or test was created. It was corrected to the actual committed SHA; this is not a test/build execution or qualification. Run 20261013-111100-caec is the correctly admitted second repair run.

First repair compiler output was removed after executor death and inspection: 97,867 files, 39,819,713,112 bytes, same exact task-created bin-c1199r13 path. Build-source stamps were retained in ignored external-to-output evidence before removal. Intermittent ownership HTTP 502/12-second timeout observations are preserved in executor logs; no guard was bypassed and the prior completed runs remained source-clean with verified builds.

```text
CHECKPOINT CP-16 commit=97bc9378ccf8716e4e849d33d0d7e302fc7747e9 build=ok filter=/*/*/CheckpointBuildBindingPathTests/* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-110123-4cda/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=97bc9378ccf8716e4e849d33d0d7e302fc7747e9 sourceState=clean buildSource=verified
```

## Completed Final disposition

Run 20261013-111100-caec tested committed 1548d4f4a62955361b13081110019efc76c783a8: CP-16 executed=3 passed=3 failed=0 skipped=0, all three exact master methods confirmed in fresh TRX. Stored verdict GREEN exit=0; build 440.3136513s, build slot=granted waited=0s, row slot=granted waited=0s. Source start/end dirty=0, clean, matching SHA/fingerprint, buildSource=verified, exact report Markdown digest verified. Both PowerShell and tool validators pass for the complete one-row report against that exact SHA. CP-16 was repaired/rerun twice (initial 2/1, first repair 0/3, final 3/0); those two red receipts are preserved, not relabeled or hidden.

The separate foreground wait finishes exit 2 solely for TOOL CLEANUP Refused output-memory-authority-missing at its retained tool shadow, an accepted brief disclosure. Stored test verdict remains GREEN exit 0. Both launcher and every wait invocation were awaited until not 75; all executors finished before subsequent source changes or manual output removal. No run is orphaned. The source-qualified groups are initial full CP-1..CP-15 at 4012260494488d152110f1d025020e6c0d4c2eda and fixed CP-16 at 1548d4f4a62955361b13081110019efc76c783a8. Only the master fixture class changed between them; original V/R and census inputs remain byte-identical. This is Final with required targeted red-row repairs, not an Interim selection or a claim that earlier receipts tested a later SHA.

Final ordinary outcomes: V-1 PASS, V-2 PASS, V-3 PASS on Linux with disclosed Windows sharing skip, V-4 PASS, V-5 PASS, V-6 PASS; R-1 PASS including retained master binding methods and independent merged census 507, R-2 PENDING accepted native Windows qualification, R-3 PASS exactly three chronological ordinary hanging-Git executions, R-4 PASS sixteen shared-reader cases. Final selected totals: 226 executed/passed, zero unresolved failures, one planned skip. Historical executions across all three source groups include four fixture failures and are not the final selection's counts. No additional green repetitions or loaded repeats, no deliberate mutants, no timeout/assertion loosening.

Final compiler output removal: 97,867 files / 39,819,835,236 bytes at tests/Antiphon.Tests/bin-c1199r13. Tool bootstrap removal: 35 files / 19,609,187 bytes at tools/Antiphon.Checkpoints/bin-c1199r13f-tool. Both checks confirmed exact current-task producer paths, no tracked files/links and absent directories after removal. Retained ignored checkpoint tool shadows/receipts remain as disclosed; source and unrelated outputs were preserved. Three isolated test builds rather than the original one were necessary solely for the two cause-level CP-16 fixture repairs; every build was the checkpoint row's own gated build, not an unlisted driver. Bootstrap remained the sole extra build. All work finished within the 75-minute task budget.

Full base..1548d4f4a62955361b13081110019efc76c783a8 evidence-history guard passed: 71 commits, 8 introduced evidence entries, 0 violations. Final merge-tree at that source against fetched origin/master passed exit 0, tree f7059526f2605549044cee71f3ff1f652ac4ac5e. The final report-only commit preserves actual tested SHAs; full task base..published HEAD history guard and remote-ref verification are reported in the caller-facing final message. No further implementation or ordinary Linux verification remains; Windows and every PC/variant remain pending in their explicitly accepted separate qualification lanes.

Receipt revalidation without builds:

```sh
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261013-104034-e204/report.json -ExpectedSourceSha 4012260494488d152110f1d025020e6c0d4c2eda -Rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-12,CP-13,CP-14,CP-15
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261013-111100-caec/report.json -ExpectedSourceSha 1548d4f4a62955361b13081110019efc76c783a8
```

CP-11's exact source/build/digest/count/skip facts are in the original report and independently inspected roster; strict zero-skip validation deliberately rejects that row. All validators were run before manual compiler output reclamation; the ignored build-source stamps remain retained beside the inspection evidence. Mutation stays pending for PC-1/2/3 and all inherited families, including the retained master's path-normalization/provenance controls and missing-control discovery.

## Unedited final CHECKPOINT line

```text
CHECKPOINT CP-16 commit=1548d4f4a62955361b13081110019efc76c783a8 build=ok filter=/*/*/CheckpointBuildBindingPathTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f3302b7a/.antiphon/checkpoints/20261013-111100-caec/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=1548d4f4a62955361b13081110019efc76c783a8 sourceState=clean buildSource=verified
```

--- next stage ---
next: review
handoff: Review the master-based CARD-1199 replay: CP-1..15 qualify at 401226049, repaired master CP-16 at 1548d4f4a; 226 final passes plus one Windows skip, census 507=504+3. Preserve SourceRunGuard normalization/project-file resolution, exact two red fixture receipts and separate wait exit 2. R-2 and all PCs remain pending; caller lands Code then commissions SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md

[antiphon-report:f3302b7a done]
