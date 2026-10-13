# CARD-1199 repair 13d — Code 6284d6ec

F1/F2 implementation is pushed; Final verification is INCOMPLETE. Budget-stopped with a foreign-state fixture mismatch; the complete-fixture repair is pushed but not rerun. Next: Code.

Landing owner: original Code task 6284d6ec-03f7-4a88-b9f5-12a601bfe017. Predecessor b56c49d8-21a1-409f-9dd7-8683de0c29ec is superseded as landing owner by the brief. Branch feat/card-task-6284d6ec; worktree /work/worktrees/task-6284d6ec; assigned desktop checkout C:\Antiphon\worktrees\card-task-6284d6ec is unreachable. Task base 77fc640757f1cd87f8d7d4e512053f65a84aaf1c. Plan docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md. Evidence .antiphon/c1199r13d/ plus the exact run folders below. Restart: none.

## Changes

ReceiptFile routes state, report pair, build bindings, requests, ownership records, root markers, marker recognition and custody hashes through the regular-file reader. Linux statx checks without following links before open; O_NOFOLLOW/O_NONBLOCK guard the open; metadata on the retained descriptor is rechecked before reading. Windows metadata precheck and exact-handle checks remain unqualified natively. A 64 MiB read limit bounds receipt allocation. ReadBytes preserves exact Markdown digest bytes. Report rendering, validation, terminal wait output and tail reads use the same boundary. No detection repair or deletion outside creation custody was added.

ReceiptJson requires serialized reference facts before defaults can hide their absence, respects nullable annotations, and recursively checks DESERIALIZED collection elements and non-null fields. Required identities/state strings and list string elements cannot be empty. Null nullable diagnostics and ordinary empty collections remain legal; outputPaths must be nonempty when outputNames declares entries. Audit fixes cover report OutputPaths/Builds/Rows/Unlisted/CleanupRefusals/OutputNames, row Failures/RerunLines/SlowClasses and nested source/host/build fields; state Rows/Builds/CreatedOutputPaths and heartbeat/status strings; binding Properties and identity strings; request Rows/KnownFlaky and launch strings; ownership path/identity strings. The report refusal includes the exact exception field/path message. The same recursive guard covers repeat evidence if present.

Tests add three methods with scratch fixtures, hand-written required-field expectations, healthy native controls, all nine FIFO verb/path combinations, eighteen symlink/directory combinations, nested null/wrong-type/missing shapes, invalid list elements, required-empty lists/strings and state-survival checks. FIFO children have a hard four-second cancellation bound, are killed AND awaited, and register with checkpoint custody; no sleeps synchronize them. Linux FIFO qualification explicitly skips elsewhere. The assembly-local process limiter is used. Existing assertions and timeouts were not loosened.

## Defect baseline

Production remained exactly base 77fc64075 through tests-first source 383dac71a759b71b2f8158eb32dfeb3781e319c7. CP-RED run 20261013-093008-5c48: three executions, zero passed, three failed, zero skipped; build and source provenance verified, slot=granted waited=30s (build waited=0s). FIFO test reports ALL NINE children HUNG, killed and awaited at four seconds, including the six newly reachable combinations in Review 443a4222. Symlink test reports eight incorrect success exits. The first shape harness collected exceptions but failed first on a state rewrite before printing its collected exception list. Its strengthened aggregation preserves the state-survival assertion and continues all cases; it does not erase that red result.

To expose the required null failure separately, null-red.ps1 seeds healthy receipts through the unchanged tool's own APIs and changes only report.json outputPaths to null. Native/in-process public verb outcomes: report FAILED NullReferenceException; report-json FAILED NullReferenceException; stop FAILED NullReferenceException. This extra gated driver was necessary because the original harness's first state assertion hid its collected null diagnostics. slot=granted waited=46s, held=2s. Evidence null-red.log. No deliberate mutant or positive-control cycle was run.

## Scope and execution inventory

The explicit repair brief and closed manifest override generic Final Unit requirements: NO Unit lane or full assembly. Original CP-1..CP-11 counts stay 204 passed plus the planned Windows sharing skip; new CP-12..CP-14 add three cases and CP-15 adds all sixteen shared-reader cases. Target total 223 passed plus one planned skip; independent compiled census literal changes 501 -> 504 for exactly three added methods. V-1..V-6 and R-1..R-4 require actual per-ID outcomes below. No unbounded classes. CP-15 is listed because extracting exact byte reads affects the existing shared reader. Only three ordinary hanging-Git executions (CP-1/7/8) are selected; no loaded or extra normal repeats.

Four gated small tool bootstraps (base, first fix, completed comparison hash, final creation hash) are required to execute each candidate. Base/fixed/complete bootstrap logs preserve BUILD SLOT lines; all builds use UseAppHost=false and forward-slash trailing OutputPath. Test builds run only through the checkpoint tool with --keep-outputs, never a build id named tests.

Three interrupted candidate runs are NOT verification evidence: 20261013-094642-c550 imported eleven rows because Markdown table blanks split additions; stopped before tests after detecting the missing rows. The corrected importer initially refused incompatible After groups, then imported all fifteen after the group fix. Run 20261013-094912-9c12 was stopped during build after the audit found remaining pathname custody-hash reads. Run 20261013-095403-9086 was also stopped during build when the final source audit found that the scripted edit had missed the creation-record hash. Its final correction is 89bf1e2fda1787f00bd8c41091a2c3ea862b7022. All exact executors and foreground stop/wait calls finished before source edits; their partial outputs were removed. Final qualification starts only after all source changes are committed and pushed.

Runner defaults revision 2 and session-runners were read through authenticated GET. No Runner/Platform pin, fleet address embedded, restart, rebase, amend or force push. Every meaningful slice was committed and pushed to the assigned ref.

## Merge and qualification limits

Fetched origin/master 7ae6dfdb070ae804d1559d6de0d9a654fb9ebe3e. merge-tree returns 1, conflict tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs (Expected output-path derivation versus target directory normalization). The ORIGINAL TASK BASE against the SAME fetched target also returns 1 on that same path; this is inherited target drift, independently checked in merge-tree-base.txt. No clean merge result is claimed. Caller/Review must preserve project-file directory handling and directory normalization together when landing; the branch fast-forward contract forbids rebasing here. The ordinary implementation can be reviewed without treating this inherited conflict as green.

R-2 native Windows junction/sharing qualification stays pending. CP-11's intentional Windows skip makes the full strict validator refuse; validate selected zero-skip rows separately. A separate wait lacks current-process creation authority for the retained executor shadow and may exit 2 even when stored test verdict is GREEN. Do not relabel that wait exit or its receipts.

## Mutation handoff

Post-land Mutation was NOT run. PC-1 all nine FIFO verb/path variants, PC-2 all eighteen symlink/directory variants, PC-3 every shape/element/empty variant remain pending method-scoped SourceLanding Mutation, together with inherited forged/missing/outside ledgers, output extras/changes/links, sixteen preservation shapes, receipt-pair source/digest/writability controls, held gates and foreign terminal bindings. Missing-control discovery remains Mutation-owned. Candidate documentation unbound: docs/superpowers/mutations/. Next Code completes ordinary verification; only then ordinary Review, caller landing of this original Code task and SourceLanding Mutation. No human implementation choice was requested.

## Actual Final disposition

Latest source/fixture SHA before this report: 8e013142f7d8d869934a89251b1dad93b2854e3c. Actual last tested SHA: 89bf1e2fda1787f00bd8c41091a2c3ea862b7022. Run 20261013-100015-8d3e was explicitly stopped before editing its source. Four earlier bootstraps and three interrupted attempts were required by the stated corrections; the budget was consumed by those interruptions, the 9m33s red build, the 6m56s latest build, and row slot waits. No full green receipt exists. Do NOT call the repaired fixture green.

CP-2: 1 passed, 0 failed/skipped; intended staged_untracked_file_is_kept_and_indexed method confirmed in fresh TRX; slot=granted waited=75s. CP-3: 1 passed, 0 failed/skipped; intended staged_ignored_file_is_kept_and_indexed confirmed fresh; slot=granted waited=14s. Build stamp SHA/fingerprint agree with the stopped run's clean source start; this is NOT a clean terminal certificate. CP-1: started slot=granted waited=0s, emitted stop_refuses_foreign_state_without_writing failure, then stopped without final TRX/counts. CP-4: started slot=granted waited=90s, stopped without final TRX/counts. CP-5..CP-15: not run, queued at stop. Census 504 is the updated independent literal, NOT an executed passing census; three hanging-Git passes are NOT qualified in this task.

The CP-1 fixture contained only runId/toolOwner/phase. The new required-shape contract correctly returns state-receipt-unreadable before the foreign-owner check. Repair serializes a complete RunState with the SAME foreign RunId/ToolOwner/Phase, retaining every existing exit-2, original-byte-survival, named foreign_state and absence-of-stopped assertion. No assertion or timeout was relaxed. This fixture repair and Final rerun remain unqualified.

V-1: PARTIAL, CP-2/3 passed only. V-2, V-3, V-4, R-1: INCOMPLETE, required full rows not completed. V-5/V-6: NOT RUN ordinary; only genuine defect baseline red is recorded. R-2: PENDING native Windows. R-3: INCOMPLETE, no three fresh qualified passes. R-4: NOT RUN shared-reader class. Deferred verification IDs are all V-1..V-6/R-1..R-4 and CP-1/4..15; recommission the complete CP-1..15 closed manifest at committed HEAD, including CP-2/3 again for one coherent Final source group.

All owned foreground commands were awaited; executors were stopped before source edits. Compiler output reclaimed: latest 39,293,946,429 bytes / 97,819 files, plus red 39,294,994,085 bytes and the three task-owned interrupted outputs. Exact cleanup records stay ignored. Task-created bootstrap outputs are reclaimed separately. Checkpoint evidence/shadows stay retained; no unrelated output or tracked files were removed.

Plan/evidence paths are absolute /work/worktrees/task-6284d6ec/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md, /work/worktrees/task-6284d6ec/.antiphon/c1199r13d/ and the named checkpoint run directories. Evidence-history guard passes over full base..HEAD; report-only final commit is checked separately. merge-tree remains 1 on the inherited SourceRunGuard.cs conflict against fetched 7ae6dfdb070ae804d1559d6de0d9a654fb9ebe3e; no exit-0 claim. Restart: none.

## Rerun command

Build the checkpoint tool through scripts/build-slot.ps1 with an assigned fresh bin-*/ output and UseAppHost=false, then:

`dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-<assigned-tool>/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md --expected-source-sha <committed-HEAD> --keep-outputs --max-wait 50s`

Await each exit 75 with wait on the exact run ID. New totals required: CP counts 133,1,1,1,17,10,1,1,13,1,25,1,1,1,16; 223 passes plus the planned Windows skip. Preserve skip/terminal-shadow qualification limits. All PC-1/2/3 variants and inherited families remain pending Mutation. No Post-land Mutation was run.

## Unedited checkpoint lines

```text
CHECKPOINT CP-RED commit=383dac71a759b71b2f8158eb32dfeb3781e319c7 build=ok filter=/*/*/CheckpointReceiptSafetyTests/* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-6284d6ec/.antiphon/checkpoints/20261013-093008-5c48/rows/CP-RED/run.trx slot=granted waited=30s dirty=0 source=383dac71a759b71b2f8158eb32dfeb3781e319c7 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=89bf1e2fda1787f00bd8c41091a2c3ea862b7022 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-6284d6ec/.antiphon/checkpoints/20261013-100015-8d3e/rows/CP-2/run.trx slot=granted waited=75s dirty=0 source=89bf1e2fda1787f00bd8c41091a2c3ea862b7022 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=89bf1e2fda1787f00bd8c41091a2c3ea862b7022 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-6284d6ec/.antiphon/checkpoints/20261013-100015-8d3e/rows/CP-3/run.trx slot=granted waited=14s dirty=0 source=89bf1e2fda1787f00bd8c41091a2c3ea862b7022 sourceState=clean buildSource=verified
```

--- next stage ---
next: code
handoff: Complete CARD-1199 repair 13d Final at committed HEAD: foreign-state fixture repaired without relaxing assertions; run all CP-1..15, inspect fresh TRX/receipts, rerun reds. F1/F2 baseline proven; no green Final. Preserve inherited SourceRunGuard merge conflict and pending Windows/Mutation qualification.
artifact: docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md

[antiphon-report:6284d6ec failed]
