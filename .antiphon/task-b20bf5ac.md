Fixed the Windows state-publication race and checkpoint batch contention; the assigned native verification passes.

Branch: feat/card-task-b20bf5ac. Base: f7c77683ffb940291f26eb6dee63f92efed25bff. Tested code tip: 7e067f8a0955d2b5dc9d19a66411bf1fe276deb1. This final report is a documentation-only follow-up; all execution below belongs to that tested tip unless explicitly marked historical.

Evidence: C:\Antiphon\releases\task-b20bf5ac. Host DESKTOP-KTLKPIF, native Windows 10.0.19045, .NET 9.0.22, SDK 10.0.300, TUnit 1.44.0 / MTP 2.2.2. Per-row run.json records exact commands, source, host and wall time; raw TRX and run.log are retained. Build JSON and fixed-assembly-provenance.json retain SHA-256 and informational versions for the test, checkpoint and cleanup assemblies. All match the tested tip; tracked source stayed clean and frozen during execution. Timing JSONL is under timing/; absent phase timings remain unknown.

The failing operation was verification after a successful state.json replacement. Natural isolated attempts 1 and 2 passed and attempt 3 reproduced StatePublicationException -> IOException `replacement-unverified`, HRESULT 0x80131620. That HRESULT is a managed I/O failure, not a Win32 sharing-error code. The controlled native witness then demonstrated two successful File.Replace publications in PID 216488, threads 18 and 19, to C:\Users\lndco\AppData\Local\Temp\c723-01a187dff5957fccb1852c5eeb064761\state.json. The first verification read the second generation. No native write denial, external holder, path-length failure or reserved-name failure was involved in this reproduction. The dedicated reader's read/write/delete sharing is retained.

Fix commits, all pushed:

- 244aaf9e573f8e28367eb01a708c60e25c80a959: RunStateStore serializes serialization, replacement, byte verification, custody recording and history through one instance gate shared by the executor's scheduler and row publishers. Atomic replacement and real-failure refusal/retry behavior remain intact.
- 7e067f8a0955d2b5dc9d19a66411bf1fe276deb1: CheckpointAppTests, CheckpointAdmissionContractTests and CheckpointExecutorLogTests now carry the mandatory ProcessSpawnLimit, with their entries added to its roster guard. These fixtures invoke real dotnet --version and Git snapshot children even with fake row drivers. Existing two-second, ten-second and class timeouts and all acceptance assertions are unchanged. The missing limiter allowed competing snapshot work and async completions to outlive test watchdogs in the batch. No production logger or admission-deadline behavior was changed.
- 997fea8826f42519283267489f2d2442348bf350: committed the controlled native concurrency witness and log-sink timing diagnostics before the fixes. The witness failed at its intended assertion against unchanged production code, then passed after serialization. Baseline report commit: 601ee24ee08827d8349faaef1bacbe280fae2a4f.

Post-fix execution: 206 passed, 0 failed, 0 skipped (counts include deliberate repeats). The original 95-case batch passed twice. Both originally batch-only methods passed individually and in both batches. The exact slot method passed ten isolated repetitions, in addition to both batch passes. Its ten TRX durations were 5.964-8.980 seconds; nearest-rank p95 8.980 seconds is provisional for other host/load conditions. State-publication regressions passed 2/2, including native sharing contention, retry exhaustion, disk-full/read-only/foreign-file refusal, completed-row retention and the new overlap witness. Both limiter guard methods passed individually, 1/1 each.

Per-class counts for the original batch, expressed as passed/failed; every run had zero skips:

| Class | Baseline | Diagnostic red | Fixed | Fixed repeat |
|---|---:|---:|---:|---:|
| CheckpointAdmissionContractTests | 3/0 | 2/1 | 3/0 | 3/0 |
| CheckpointAppTests | 3/0 | 3/0 | 3/0 | 3/0 |
| CheckpointEofStallTests | 10/0 | 10/0 | 10/0 | 10/0 |
| CheckpointExecutorLogTests | 5/1 | 4/2 | 6/0 | 6/0 |
| CheckpointGitEncodingScriptTests | 2/0 | 2/0 | 2/0 | 2/0 |
| CheckpointLaunchCleanupTests | 11/0 | 11/0 | 11/0 | 11/0 |
| CheckpointOrFilterTests | 31/0 | 31/0 | 31/0 | 31/0 |
| CheckpointReceiptStallBudgetTests | 6/0 | 6/0 | 6/0 | 6/0 |
| CheckpointSlotExecutorTests | 12/1 | 12/1 | 13/0 | 13/0 |
| CheckpointWindowsReadModeTests | 10/0 | 10/0 | 10/0 | 10/0 |
| Total | 93/2 | 91/4 | 95/0 | 95/0 |

Baseline batch source is 601ee24ee08827d8349faaef1bacbe280fae2a4f (production/test code identical to base); diagnostic red is 997fea8826f42519283267489f2d2442348bf350. The diagnostic batch also exposed concurrent_callbacks_append_each_line_once_without_overlap exceeding its unchanged 60-second timeout; it passed in both fixed batches. All red outcomes remain retained. The new native witness was 0/1 before repair. The two auxiliary isolated methods were 1/0 each before and after repair. Per-run details are in per-class-counts.json.

One combined limiter-method selector matched zero tests (exit 8; fixed-limiter-guard). It receives no credit. The corrected exact-method runs are fixed-limiter-required and fixed-limiter-exclusive, each 1/0. No assertion, deadline or production rejection was relaxed to obtain passing results.

Builds: baseline 546.12s, witness 519.36s, fixed 540.18s; each 0 errors and 1374 warnings. Every build/test driver used scripts/build-slot.ps1. The external diagnostic startup hook was separately built through that gate (0 errors, 0 warnings). The fixed build waited 137s and the limiter guard waited 151s for memory-floor admission; neither bypassed the gate. Batch wall times were 108.2s baseline, 229.3s diagnostic red, 230.9s fixed, and 195.8s fixed repeat. The state regression selection was 160.1s wall. No selection approached the 30-minute Debug threshold.

Reruns require a rebuild because task outputs were removed. The retained external build-project.ps1 and run-row.ps1 are the exact entry points; use fresh Label/Row values. Relevant filters:

- /*/*/CheckpointSlotExecutorTests/executor_reachable_broker_grants_build_and_rows
- /*/*/CheckpointStatePublicationTests/*
- /*/*/CheckpointExecutorLogTests/note_returns_when_the_sink_resumes_on_the_caller_context
- /*/*/CheckpointAdmissionContractTests/C1531_inputs_resolve_without_disabling_bounds
- /*/*/ProcessSpawnLimitTests/Process_spawning_classes_carry_the_limiter
- /*/*/ProcessSpawnLimitTests/No_class_carries_both_limiters

The original batch filter and exact invocation are in fixed-batch/run.json and fixed-batch-repeat/run.json. No broad Unit/full assembly run was added: this Custom Debug brief commissioned the narrow native selection. The hook build, native witness, state-failure matrix, limiter guards and second batch were directly required to diagnose or verify the reproduced defects.

Cleanup: all 34 inventoried worktree bin-b20bf5ac directories plus the external hook output were removed; zero remain. Exact literal paths were inspected for containment and task creation times. Automatic approval review rejected the initial combined force-delete command before execution; smaller literal-path deletions without Force succeeded. cleanup-result.json and build-output-creation-inventory.json retain the result. Both native witness temp roots were already removed by fixture teardown. All foreground commands and test children were awaited.

No assigned test failure remains. This is not a full native qualification or land approval: the broader land-graph/C1313 gaps from task 7783f346 were outside this brief and were not rerun. git fetch origin master followed by git merge-tree --write-tree found 15 conflicts against e30ba1237a0a975220782f73eb74c1d90700efc1; the same comparison at this task's base has 14. The additional conflict is CheckpointExecutorLogTests.cs. Both read-only comparisons are preserved externally; no merge/rebase was performed. The evidence-history guard and git diff --check passed. No tags, publication, canonical checkout edits, stack restarts or foreign process termination occurred. The caller's standing authority covered the fixes; no further approval was needed.



Historical diagnosis notes follow unchanged:
# Windows checkpoint debugging — b20bf5ac

Base: f7c77683ffb940291f26eb6dee63f92efed25bff. Native Windows repair of the persistent CheckpointSlotExecutorTests.executor_reachable_broker_grants_build_and_rows state publication refusal and investigation of the two batch-only checkpoint failures. Historical red evidence is retained read-only at C:\Antiphon\releases\task-7783f346 and in origin/feat/card-task-7783f346:.antiphon/task-7783f346.md.

Assigned verification: native isolated reproduction up to ten attempts; the two historical batch-only methods individually and the original 95-case checkpoint selection together; targeted state publication regressions for any fix. No full Unit run: this Custom Debug brief specifies narrow native verification. Every driver uses scripts/build-slot.ps1. Evidence: C:\Antiphon\releases\task-b20bf5ac. Alternate output: bin-b20bf5ac/.

Historical results: slot method 0/1 in both fixed-checkpoints and focused-native-checks. Batch-only failures: CheckpointExecutorLogTests.note_returns_when_the_sink_resumes_on_the_caller_context and CheckpointAdmissionContractTests.C1531_inputs_resolve_without_disabling_bounds; both passed in the focused run. These are unresolved observations, not attributed regressions.

## Native baseline reproduced

At source 601ee24ee08827d8349faaef1bacbe280fae2a4f (product/test code identical to base), exact slot attempts 1 and 2 passed; attempt 3 failed. The external .NET startup hook captured StatePublicationException -> IOException `replacement-unverified`, HRESULT 0x80131620, from ReplacementPublication.Publish line 47 after Move returned. This is a managed verification failure, not a Win32 sharing error. Evidence: baseline-slot-01..03 and exception-hook under the external root.

The two batch-only methods passed alone (1/0 each). Original 95-case batch: 93 passed, 2 failed (slot publication and caller-context log test); admission passed. Log failure retained its unchanged two-second deadline. A native controlled-interleaving witness and sink timestamps are now committed for diagnosis before product changes.

## Controlled native cause and production repair

At 997fea8826f42519283267489f2d2442348bf350, the new exact-method witness failed at the intended assertion: first publication must succeed. PID 216488 threads 18/19 both reported successful native moves to C:\Users\lndco\AppData\Local\Temp\c723-01a187dff5957fccb1852c5eeb064761\state.json; before the first verification it observed the second generation. The actual refusal is managed replacement-unverified, not a Win32 error or a denying external holder. Its raw log/TRX are witness-native-red. RunStateStore now serializes serialization, replacement, verification, custody recording and history per executor store. Native atomic replacement, reader sharing, retry policy and permanent-failure handling are unchanged.

The diagnostic 95-case batch at the same witness source completed 91/4. Failures: slot publication; note_returns_when_the_sink_resumes_on_the_caller_context; C1531_inputs_resolve_without_disabling_bounds (expected 4, got watchdog cancellation 130); concurrent_callbacks_append_each_line_once_without_overlap (60-second test timeout). The latter is additional retained evidence, not silently discarded. CheckpointExecutorLogTests, CheckpointAdmissionContractTests and CheckpointAppTests all reach CheckpointApp -> HostSnapshot(dotnet --version) and GitSnapshot(real git), yet lacked the mandatory ProcessSpawnLimit. Their scheduling repair is a separate slice.

--- next stage ---
next: review
handoff: Review the state-publication serialization and missing checkpoint process limiters at tested code tip 7e067f8a0955d2b5dc9d19a66411bf1fe276deb1, retaining historical red evidence and the broader qualification/merge limitations. Final follow-up changes only this report.
artifact: C:\Antiphon\worktrees\card-task-b20bf5ac\.antiphon\task-b20bf5ac.md
