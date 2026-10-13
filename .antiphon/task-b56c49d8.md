# CARD-1199 repair 13c Code report

COMPLETE for the commissioned Linux ordinary scope. The expanded required-receipt regression is proven assertion-red on unchanged production, then green after the receipt fix. CP-1..CP-11: 204 passed, 0 failed, 1 planned Windows-only skip. Next: ordinary Review. No deliberate Mutation controls were run.

## Source and landing ownership

Landing owner/original Code task: b56c49d8-21a1-409f-9dd7-8683de0c29ec. Branch: feat/card-task-b56c49d8. Worktree: /work/worktrees/task-b56c49d8. Assigned desktop mirror C:\Antiphon\worktrees\card-task-b56c49d8 was not accessed. Task base: 1a779642dc725759dfa66ebc73bdb43041980da7. This owner supersedes Code 059e44d6-18b4-4ef2-b14e-5dc1211201cd. Branch remained fast-forward-only; no rebase, amend, reset or force push.

Tests-first slices, both pushed immediately:
- 06cc44d900ee71decbe08cc55d359ab122191813: eligible source evidence for the healthy report-merge control.
- 5de862c47e7bea2fa144d231a199a38fb9c4236a: bind the status fixture to its actual results root.

Production fix and actual Final tested SHA: f58eae39576ce4e48719029ec12595715d07e3b6. The useful newer predecessor commit 5b58ed50bbb866d5f93eeb4b23a78b79ec180644 was cherry-picked with provenance; the test conflict retained this task's already-proven fixture. The plan records current ownership and the brief's full closed rerun, superseding the predecessor's narrower instruction. The fix was pushed before the Final build. A report/plan-only publication commit follows verification; it is not substituted for the actual tested SHA.

Plan/test design/repair notes: /work/worktrees/task-b56c49d8/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md. Full evidence root: /work/worktrees/task-b56c49d8/.antiphon/c1199r13c/. Final checkpoint root: /work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/.

## Implementation

Program.cs validates required state/report pairs before report rendering, JSON rendering, merging, and terminal stop side effects. Missing, malformed and mismatching receipts refuse with exit 2 and a named reason. Merging cannot silently omit a run whose remaining state/report files identify it. Runtime output captures report diagnostics. State source reads in Program/WaitCommand and ReportWriter refuse unreadable source data instead of dereferencing it.

WaitCommandTests.completed_wait_requires_report_receipts retains its assertions and exercises 41 combinations: six verbs over control, missing Markdown, missing JSON, both missing, malformed JSON, wrong source and missing state (the established missing-state wait executor-death case remains separate). Invalid cases require exit 2, a receipt diagnostic and unchanged state bytes. The healthy merge fixture now supplies eligible source, and all entry points use the fixture's actual results root. No test was removed, assertion weakened, timeout widened, sleep added or compiled census changed.

## Red proof and honest failed attempt

The first CP-RED at 06cc44d900ee71decbe08cc55d359ab122191813, run 20261013-080525-8b57, compiled successfully and executed 1 test: 0 passed, 1 failed, 0 skipped. It failed the healthy status control because the fixture omitted --results-root. That is fixture red, NOT guarded-defect evidence. Its native build had 0 errors. Evidence remains unchanged.

After the tests-only correction, CP-RED at 5de862c47e7bea2fa144d231a199a38fb9c4236a, run 20261013-081424-7aa8, compiled and executed exactly 1 test: 0 passed, 1 assertion failure, 0 skipped. The production diff before this run was empty; only the test fixture differed from task base. Fresh TRX and inspect-red.ps1 confirmed the exact class/method and assertion:

```text
ShouldAssertException: exit
    should be
2
    but was
0

Additional Info:
    missing-md report-json:
```

This is the required missing-receipt success defect. The post-fix ordinary CP-1 re-executed the same method green, covering all 41 combinations. No baseline compile/fixture failure is credited as defect red. No extra repeat was run after green.

## Final scope, commands and cost

The explicit brief and closed manifest override the generic Final Unit profile: NO Unit lane and no full-assembly selection. Every named affected class, V/R and required Linux acceptance in CP-1..CP-11 was run. No unbounded class selection. The plan's ordinary floor is 36 minutes; this task's authoritative budget is 60 minutes. Actual Final tool wall: 13m37s, one isolated test build, max one build/two rows concurrently. Native build: 6m14s, 0 errors, 691 inherited warnings. All selected build/row slots were granted with waited=0s and maxcpucount=6.

Two extra small gated tool bootstraps were necessary to execute the pre-fix and candidate tools. Each ran through scripts/build-slot.ps1, with UseAppHost=false and forward-slash trailing OutputPath; each succeeded with 0 errors, 1 existing CS8602 warning, slot=granted waited=0s. Bootstrap logs: bootstrap.log and bootstrap-fixed.log. Two method-scoped CP-RED runs were necessary to obtain valid tests-first red evidence after the fixture failure; each used one isolated build and --keep-outputs. No other build/test driver was run.

Candidate bootstrap and Final driver:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1199r13c-fixed-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1199r13c-tool-fixed/ --property:UseAppHost=false --nologo
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1199r13c-tool-fixed/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md --expected-source-sha f58eae39576ce4e48719029ec12595715d07e3b6 --keep-outputs --max-wait 50s --heartbeat 30s
```

Every running exit 75 was followed by foreground wait with that exact run ID until a terminal result. Every owned command was awaited. Source stayed frozen through execution. Do not rerun against existing output roots; this task reclaimed its own roots after verification.

The checkpoint report and executor completed GREEN, exit 0. The separate terminal wait exited 2 because its process lacks in-memory deletion authority for the executor's tool shadow: TOOL CLEANUP Refused, output-memory-authority-missing, left in place for manual removal. This is the intended detect/report-only retention behavior, not a test failure. The tool shadows and all checkpoint evidence remain retained; the report does not claim they were deleted.

Runner defaults and session runners were read through authenticated GET /api/runner-defaults and GET /api/session-runners (defaults revision 2). No Runner/Platform pin or embedded fleet location. Intermittent owner HTTP 502/timeouts recovered within the unchanged ownership guard; no bypass or restart. Restart: none.

## Fresh results and receipt limits

| CP | Executed | Passed | Failed | Skipped | Slot | Waited |
|---|---:|---:|---:|---:|---|---:|
| CP-1 | 133 | 133 | 0 | 0 | granted | 0s |
| CP-2 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-3 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-4 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-5 | 17 | 17 | 0 | 0 | granted | 0s |
| CP-6 | 10 | 10 | 0 | 0 | granted | 0s |
| CP-7 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-8 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-9 | 13 | 13 | 0 | 0 | granted | 0s |
| CP-10 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-11 | 25 | 25 | 0 | 1 | granted | 0s |

Fresh full class rosters: final-trx-roster.txt. Exact required methods and timestamp ordering: required-roster.txt. CP-1 has all 12 selected classes, CP-11 all 4, and all other selected classes/methods have nonzero counts. The independent compiled namespace census agrees with literal 501. Held exclusive gate (CP-6), foreign terminal receipts (CP-1), expanded required receipt regression (CP-1), ownership cancellation, unsafe IDs/tracked survival and all repaired inherited cleanup fixtures passed.

scripts/validate-checkpoint-receipt.ps1 returned 0 for CP-1..CP-10 at the exact tested SHA. Full validation returned 2, reason=row_failed, because its strict zero-skip policy rejects CP-11's planned Windows sharing skip. This limit is preserved; no validator or receipt was edited. The only skipped identity is EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open, whose source explicitly requires Windows. CP-11's 25 passes, one skip, report digest, terminal state, every row's clean source observations and build provenance were separately checked by receipt-facts.ps1. Do not describe the full strict certificate as passing.

Original build receipt bytes were archived before manual cleanup as build-source-original.json, SHA256 FB1A5B9C25710C6FCE5AC80464F5B398BD454B0B42CED2DD1364841283F04A97. Source start/end SHA and fingerprint matched, dirty=0, buildSource=verified across the terminal report/state and all rows. Removing compiler outputs leaves historical receipts intact; live report consumers can subsequently refuse the intentionally removed output stamp rather than fabricating success.

## V/R disposition

- V-1 PASSED: CP-1/2/3/4/5 preserved resulting user bytes, deletions, links and index, disclosed changed paths/manual commands, and exercised clean builds on both entry points. No restoration claim.
- V-2 PASSED: CP-1/5/6 fresh owned output and collision/link/stamp refusals with preserved bytes.
- V-3 PASSED for Linux: CP-1/5/9/10/11 negative receipt binding, exact invalid exits, writable source receipts, foreign terminal refusal and the expanded missing/malformed receipt matrix. CP-11 strict zero-skip certificate limitation is disclosed above.
- V-4 PASSED: CP-1/6 in-memory creation custody, retained external/separate-invocation outputs, forged ledger/extra/changed/link refusals, held exclusive stream regression.
- R-1 PASSED for the named Linux selection: all full affected classes, snapshot membership and compiled namespace census 501.
- R-2 DISCLOSED/PENDING: no native Windows junction or sharing qualification; the one planned skip remains unqualified, not passed.
- R-3 PASSED: three consecutive ordinary hanging-Git executions, CP-1 08:31:03.225..08:31:18.376 UTC, CP-7 08:35:50.800..08:36:06.723, CP-8 08:36:21.201..08:36:36.612. Fresh TRX confirms chronology. No timeout changes or repetitions beyond the brief's required rows.

## Mutation and caller action

Every PC-n/variant remains pending method-scoped SourceLanding Mutation after ordinary Review and caller landing, including missing-control discovery for this zero-PC plan. Pending families: forged/missing/truncated/outside ledgers; extra/changed files/directories/links; sixteen shape variants; missing/unwritable/corrupt/source-mismatched receipt pairs across entry points; held exclusive gates; stale terminal-directory binding. No deliberate mutants, red/restore/green cycles or post-land Mutation were run. Candidate documentation remains unbound at docs/superpowers/mutations/. Restart: none.

Commission ordinary Review against the pushed final branch, preserving actual tested SHA and these qualification limits. Caller then lands this original Code task and commissions SourceLanding Mutation. No human implementation choice remains.

## Cleanup and publication

Only task-created producer outputs were manually reclaimed, after their commands exited. Cleanup required assigned exact paths, resolved containment, fresh ancestor/descendant link checks, empty git ls-files, per-file size/mtime rechecks, individual file deletion and nonrecursive directory deletion. Both red build roots each removed about 39.29 GB. Final test output and both bootstrap outputs were also removed. Exact records: cleanup-first-red.json, cleanup-second-red.json, cleanup-final-output.json, cleanup-old-tool.json, cleanup-fixed-tool.json. No predecessor output, tracked file, daemon bin directory, evidence directory or unrelated path was deleted.

The fetched origin/master was 99910a78c7ec4295786ca94923a040013c689fcd; merge-tree at the actual tested source returned 0 (tree c48c0b4c849c7dc5ac5beff94f1ff57dfeb56ff6). Final report-only HEAD merge-tree and full-task evidence-diff checks are recorded in the final caller report/evidence logs. All source slices and this individual optional Markdown report are committed/pushed; generated TRX/JSON/logs/checkpoint directories remain ignored.

## Unedited checkpoint lines

```text
CHECKPOINT CP-RED commit=06cc44d900ee71decbe08cc55d359ab122191813 build=ok filter=/*/*/WaitCommandTests/completed_wait_requires_report_receipts executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-080525-8b57/rows/CP-RED/run.trx slot=granted waited=0s dirty=0 source=06cc44d900ee71decbe08cc55d359ab122191813 sourceState=clean buildSource=verified
CHECKPOINT CP-RED commit=5de862c47e7bea2fa144d231a199a38fb9c4236a build=ok filter=/*/*/WaitCommandTests/completed_wait_requires_report_receipts executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-081424-7aa8/rows/CP-RED/run.trx slot=granted waited=0s dirty=0 source=5de862c47e7bea2fa144d231a199a38fb9c4236a sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/CheckpointShapeConflictTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/CheckpointOwnedOutputTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=ok filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*|BaselineComparerTests*|RunSchedulerTests*|CheckpointManifestTests*|CheckpointTaskOwnershipTests*|CheckpointToolCopyCleanupTests*|WaitCommandTests*|RunStateStoreTests*|CheckpointAppTests*|CheckpointNamespaceCensusUsageTests*|CheckpointOutputConfinementTests*|CheckpointTrackedBaselineTests*)/* executed=133 passed=133 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/RowRunnerTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/CheckpointTrackedBaselineTests/passing_script_requires_writable_source_receipt* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=f58eae39576ce4e48719029ec12595715d07e3b6 build=reused filter=/*/*/(ReportWriterTests*|CheckpointSourceStateTests*|CheckpointSourceExecutionTests*|EvidenceFolderTests*)/* executed=25 passed=25 failed=0 skipped=1 trx=/work/worktrees/task-b56c49d8/.antiphon/checkpoints/20261013-082401-bb7d/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=f58eae39576ce4e48719029ec12595715d07e3b6 sourceState=clean buildSource=verified
```

--- next stage ---
next: review
handoff: Review CARD-1199 repair 13c: expanded receipt regression proven red then fixed; complete CP-1..CP-11 passed 204 with one planned Windows sharing skip. Preserve actual tested f58eae39576ce4e48719029ec12595715d07e3b6, strict full-certificate skip limitation and pending SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md

[antiphon-report:b56c49d8 done]
