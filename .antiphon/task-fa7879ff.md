# CARD-1613 native Windows verification

## Preserved red evidence before repairs

Assigned source: `aa72491b72b1af57141e7c37ac50a384485755a0`.
Requested comparison base: `ade54222543381e2e1b31ad79d8ae33a4035f70b`.
Host: native Windows 10.0.19045, .NET 9.0.22, SDK 10.0.300.

Checkpoint run `20261029-213135-7252` used the committed plan, selecting only CP-9, CP-10 and CP-11. Its source was clean and buildSource verified. Counts are executed/passed/failed/skipped:

| Row | Counts | Result |
|---|---|---|
| CP-9 | 4/3/1/0 | NativeLinuxPolicyExecutionTests always asserted Linux's nine-pass result on Windows. The real child correctly returned 1 passed, 8 skipped, and no marked side effects. |
| CP-10 | 1/0/1/0 | `c1210-child-source-stamp`: OuterRowBuildStamp expected no properties, but the checkpoint build records ArtifactsPath and UseArtifactsOutput. |
| CP-11 | 2/2/0/0 | Both retained C1050 parents passed. |

Receipt: `C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-213135-7252\report.md`.
Wall 17m06s; isolated build 822.668s; row walls 26.924s, 21.639s and 70.928s. Build and rows received host slots.

Base run `20261029-220712-2bfe` reproduced CP-10: 1 executed, 1 failed, identical source-stamp assertion. This failure predates CARD-1613. The native parent failure is present at this task's immutable starting source. No repair had been applied for either observation.

## Antiphon.Tests comparison

All 41 policy rows are covered: 165 expanded class-row cases and 353 expanded method-row cases. All **518** are NotExecuted at both requested revisions. Per-case identity/multiplicity, outcome and UTF-8 skip-reason digest comparison reports **zero differences**. Exact-selection receipts have clean source and verified build provenance; their intentional skips are comparison evidence, not green qualification certificates.

Machine-readable observations, row coverage, TRX hashes and comparison inputs are in `C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\c1613-windows-fa7879ff\` (`comparison.json`, `case-observations.json`, `row-coverage.json`, `comparison-inputs.json`). The original TRX files remain at the absolute paths recorded there. Base receipts are under `C:\Antiphon\worktrees\c1613-base-fa7879ff\.antiphon\checkpoints\20261029-220712-2bfe\`.

The first attempt selected all 41 whole classes, including portable siblings. It hit the unchanged 15-minute row timeout without a TRX. Its console recorded two additional failures: `LandGraphEvaluationTests.C1483_S4_RealRepositoryApproves` refused the new `KeepNativeLinuxPolicyFixturesInMainAssembly` target's missing pin; `C1483_S5_CheckRequiresEvaluationRecord` refused embedded `Fixtures/NativeLinuxPolicy/*.cs.txt` documents. The narrower retry selected exactly the policy's class rows and named methods and checked every returned node against the policy, with no extras. The whole-class attempt is retained at `.antiphon/c1613-windows-fa7879ff/tip-antiphon/CMP-tip-antiphon-20261029-214950-988c/`.

Base attribution of those two portable landing tests was still running when this red-evidence slice was committed. The three smaller suites and repaired native checkpoints remain pending in this historical slice.

## Custody and constraints

The canonical checkout, AppHost and production runners were not modified. All build/test drivers used the host slot gate. No tags, deployment, target publication, rebase or branch switch occurred. Comparison checkouts are detached and task-created.

Automatic approval review rejected recursive removal of the task-created `C:\Antiphon\worktrees\card-task-fa7879ff\tests\Antiphon.Tests\bin-c1613-windows` directory with `blocked by policy`. It remains intact. Repairs will use a fresh isolated output. The initial bootstrap command had a quoting error in `-nodeReuse:false` before any compilation; the quoted retry succeeded with 0 errors and 5 warnings.
