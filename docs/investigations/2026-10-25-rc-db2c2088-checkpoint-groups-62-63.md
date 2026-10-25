# db2c2088 checkpoint groups 62 and 63

The 26 reported Windows failures remain unexplained. All 13 assigned classes passed on Linux both before and after a diagnostic-only change: **59 passed, 1 skipped, 0 failed** per selection. No product repair or Windows qualification is claimed.

## Source and boundaries

- Task `91ea9029`, branch `feat/card-task-91ea9029`, worktree `/work/worktrees/task-91ea9029`.
- Fetched starting master/base: `e9703a6334df49f87490a0e67c1e5ac124f01aad`.
- Tested diagnostic source: `29161e45af3de7dc21201e488d6970261fdb4c88`. This report is a later Markdown-only change.
- Diagnostic commits: `91c906765f00172399b18654f799073044e51d1b` and `29161e45af3de7dc21201e488d6970261fdb4c88`.
- Final test DLL SHA-256: `3fe17b07969ae2b0c2b2c1cdc1759fa5fa673daad869c47ccb00de5db8320dd5`. Initial base DLL SHA-256: `f2edac94a84989f9474aced6f8459de93ffcf15e7fc3f1a83923be6ef4b4bf5d`.
- Dependency binaries came from the one full isolated build at the base. The two later recompiles changed only test sources; dependency sources were unchanged. These are direct build/TRX records, not checkpoint-tool clean receipts or RC release evidence.
- Original qualification: `db2c2088f3a4454ca48c0bee60c3f53a`, candidate `a13e544e2baea214376944129a66ec4b525fafce`. Windows counts below come from the brief. The desktop-only TRX files and their exact failing methods/errors were not available here.
- The group-61 report on `feat/card-task-2a61a460`, `docs/investigations/2026-10-25-rc-db2c2088-windows-file-lane.md`, was read before investigation. Its five fixture repairs were not duplicated. Our shared-scope edit only adds diagnostic capture before disposal; it does not implement its read-only Git-object repair.
- No desktop/native test, qualification lock, native build slot, Unit lane, tag, release script, deployment or restart was used. Product files under `tools/Antiphon.Checkpoints` were not changed. No assertions, timeouts or retry policies were weakened or added.

## What changed

`CheckpointNativeDiagnostics.cs` records bounded, read-only evidence before fixture disposal. It applies only to the 13 assigned classes, automatically on Windows. `ANTIPHON_CHECKPOINT_NATIVE_DIAGNOSTICS=1` enables the same capture on Linux for verification; it does not simulate the Windows kernel.

The capture includes PID, UTC time, OS/runtime, available thread-pool workers, fixture roots, file attributes, lengths and mtimes. Selected receipt probes record retained-handle regular-file status, link count, size and resolved path, verified-reader failures including inner exceptions, read duration, newline counts, and selected state/build/row/ownership fields. It does not dump environment variables, request contents, arbitrary file contents or DLL bytes. Limits are eight latest roots, 96 inventory entries, depth six and eight receipt reads per capture; JSON diagnostic reads have a separate 32 KiB cap and use the existing verified reader. Truncation is stated, and `.git` contents and reparse points are not traversed.

The shared `CheckpointTestBase` hook calls the helper before teardown, including for green bodies, because the original failure may occur during teardown. `CheckpointBuildBindingPathTests.BindingFixture.Dispose` captures its separately allocated root before deletion. Additional focused traces preserve fake baseline-driver arguments/working directories, rerun row output, the native held-file cleanup receipt including its reason, and coverage-command JSON on failure. A failed coverage input can be probed as C# or Markdown without incorrectly parsing it as a JSON receipt.

These probes are observations after the body. An extra successful diagnostic read cannot establish the state of the original open or explain an earlier refusal. The capture does not identify every open-handle owner or provide a native first-open system trace. Native causes still require the original error plus the new observations; if a handle refusal persists, its owner must be captured while the fixture process is alive using the existing trusted native tooling.

## Per-class results and native rerun rows

Every row used exact filter `/*/*/<Class>/*`, a fresh TRX directory and the compiled output with `--no-build`. All 26 outer class-driver exits were zero and each log records `BUILD SLOT granted`. The summary validates TRX class identities and nonzero counts separately from the shell loop's final exit.

| Group | Class | Reported Windows failures | Linux base P/F/S | Linux diagnostics P/F/S | Base/final driver wall seconds | Native rerun |
|---|---|---:|---|---|---:|---|
| 62 | CheckpointLargeStampTests | 3 | 6/0/0 | 6/0/0 | 44 / 60 | Owed |
| 62 | BaselineComparerTests | 2 | 8/0/0 | 8/0/0 | 6 / 9 | Owed |
| 62 | CheckpointBuildBindingPathTests | 2 | 3/0/0 | 3/0/0 | 5 / 9 | Owed |
| 62 | CheckpointTempUsageTests | 2 | 8/0/0 | 8/0/0 | 41 / 47 | Owed |
| 62 | CheckpointAdmissionContractTests | 1 | 3/0/0 | 3/0/0 | 7 / 9 | Owed |
| 62 | CheckpointAppTests | 1 | 3/0/0 | 3/0/0 | 5 / 5 | Owed |
| 62 | CheckpointLargeStampScriptTests | 1 | 3/0/0 | 3/0/0 | 43 / 44 | Owed |
| 63 | C1307CliCancellationTests | 5 | 5/0/0 | 5/0/0 | 7 / 7 | Owed |
| 63 | RerunPolicyTests | 3 | 6/0/0 | 6/0/0 | 5 / 6 | Owed |
| 63 | EvidenceFolderTests | 2 | 5/0/1 | 5/0/1 | 6 / 6 | Owed, including held-file case |
| 63 | PlanCoverageBindingTests | 2 | 6/0/0 | 6/0/0 | 162 / 214 | Owed |
| 63 | CheckpointRepeatBuildIsolationTests | 1 | 1/0/0 | 1/0/0 | 215 / 238 | Owed |
| 63 | CheckpointToolSourceTests | 1 | 2/0/0 | 2/0/0 | 6 / 4 | Owed |
| | **Total** | **26** | **59/0/1** | **59/0/1** | **552 / 658** | **All 13 classes** |

The skip is `EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open`, which requires native Windows file sharing. No Linux-red acceptance behavior or Windows-input witness establishing a cause was found, so no speculative behavioral fix was made. In particular, path spelling/case, CRLF, held files, cancellation and load remain hypotheses rather than attributed causes. The three repository-based LargeStamp methods may also be affected by the separately owned shared cleanup repair; only native errors/results can establish that attribution.

Diagnostic headers appear in the final TRX for every assigned class. The separate source-level helper probe passed: retained-handle metadata and CRLF counts were captured, an unselected JSON field was omitted, and a synthetic coverage failure kept exit 2. That probe compiles the diagnostic helper with the project's implicit usings and replaces compile-time `nameof` references with their identical string constants to avoid the test assembly's registration initializer. It is not a TUnit test or Windows proof.

## Build, timing and retained attempts

All build/test/probe drivers used `scripts/build-slot.ps1`. The checkpoint script was not used as the outer test driver because of the test-DLL size limitation stated in the brief. The repeat-isolation test retains its existing internally controlled checkpoint/build invocations under its parent test-driver lease.

| Source | Driver | Result | Build duration |
|---|---|---|---:|
| `e9703a6334df49f87490a0e67c1e5ac124f01aad` | Full `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-debug-91ea9029/ --nologo -v:q` | 0 errors, 1,196 warnings | 3m34.04s |
| `91c906765f00172399b18654f799073044e51d1b` | Test-only recompile, `--no-restore --property:BuildProjectReferences=false`, same output | 0 errors, 1,154 warnings | 2m22.59s |
| `29161e45af3de7dc21201e488d6970261fdb4c88` | Test-only recompile after the source-vs-JSON diagnostic correction | 0 errors, 1,154 warnings | 3m16.46s |

There was one full dependency build plus two test-only recompiles, exceeding a literal one-build-invocation reading of the brief. The extra invocations compiled the commissioned diagnostic change and its correction; no second dependency build or broad Unit run was performed.

The largest final class took 3m52s of test execution (repeat isolation); coverage binding took 3m31s. Whole selections took 9m12s and 10m58s of summed driver wall time, including gate/startup overhead. These are single observations per source, not measured p95 or an overhead experiment. No run approached 30 minutes and no timeout was changed. TRX retains per-test durations and opt-in phase JSONL is retained; missing phase attribution remains unknown, with unattributed time a remainder.

Non-creditable attempts are preserved: an unavailable `python3` launcher (exit 127); a PowerShell launcher stopped before it spawned a test driver after native-argument wildcard enumeration (exit 143, no test or lease); a direct diagnostic reflection probe that could not initialize the test assembly outside TUnit; and a source-probe reflection binding error before explicit string conversion. The corrected source probe passed. None of these attempts counts as an acceptance-test failure, red witness or green test execution. An initial summary used the XML document's empty `InnerText` when counting diagnostic headers; its preserved output was corrected to the document element without changing any TRX counts.

Generated evidence stays ignored at `/work/worktrees/task-91ea9029/.antiphon/evidence-91ea9029/`: `initial/<Class>/run.trx`, `diagnostics/<Class>/run.trx`, matching `run.log` files, three build logs, `summary.json`/`summary.csv`/`drivers.log`, assembly hashes, probe scripts/logs, timing JSONL and the producer-owned output inventory. All 33 inventoried `bin-debug-91ea9029` directories were removed after the drivers completed; a new run must rebuild its output.

## Native continuation

Land this diagnostic-only branch and integrate the separately owned group-61 repairs. After the active desktop qualification releases its lane, the native owner must build the integrated candidate into a fresh producer-owned output directory once and rerun every class in the table. Retain native TRX and complete stdout/stderr. Do not infer Windows success from the Linux runs or from a master-only land.

Example native commands, not executed here:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label db2c-g6263-native-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-db2c-g6263-native/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label db2c-g6263-native-row -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-db2c-g6263-native/ -- --treenode-filter '/*/*/C1307CliCancellationTests/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/db2c-g6263-native/C1307CliCancellationTests
```

Substitute each exact class from the table, using a fresh results directory per row. Diagnostics enable automatically on Windows. The original 26 failures remain unresolved until this native evidence identifies their causes; this branch supplies instrumentation, not an RC qualification receipt.

Settlement checks are recorded in the final task report: full base..HEAD evidence-policy check, whitespace check, current-master conflict-free merge-tree, output cleanup and remote tip verification. The pre-report merge-tree was conflict-free against fetched master `16d663409c27963e22d821af7a82b72822ad386a`.

--- next stage ---
next: land
handoff: Diagnostic-only, Linux selections passed. All 13 native rerun rows and attribution of the 26 reported Windows failures remain owed; no product repair or RC qualification is claimed.
artifact: /work/worktrees/task-91ea9029/docs/investigations/2026-10-25-rc-db2c2088-checkpoint-groups-62-63.md
