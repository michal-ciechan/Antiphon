# RC db2c2088 Windows file, handle and path lane

Five fixture defects repaired; **313 passed, 18 skipped, 0 failed** in the final Linux selection. Native Windows qualification remains owed. Product code, existing assertion outcomes, timeouts and retries were not changed.

## Provenance and scope

- Debug task `2a61a460`, branch `feat/card-task-2a61a460`, runner mirror `/work/worktrees/task-2a61a460`.
- Freshly fetched base/current `origin/master`: `e9703a6334df49f87490a0e67c1e5ac124f01aad`.
- Executable test source verified: `05e51a70c5bdac0215d9dd017da21c7cae653381`. This report is a subsequent Markdown-only change.
- Initial dependency build source: `9e513a70c5b0bcc3fc2a6d63531ffec1ef5e19c9`. Later commits change only fixture/test sources; product dependency sources are identical.
- Tested `Antiphon.Tests.dll`: 68,701,184 bytes; SHA-256 `72d7ffe970ad95bb992802c40e378f50c5282d1802bfd57298c031701ff78f5c`.
- Original Windows errors came from the assigned brief and the fetched `feat/card-task-d805bdeb` extraction, not a new inspection of the desktop. Candidate `a13e544e2baea214376944129a66ec4b525fafce`, qualification `db2c2088f3a4454ca48c0bee60c3f53a`.
- No desktop/native Windows test, qualification lock, native build slot, release script, tag or live process was touched. No whole Unit lane or Review was run, as commissioned for fixture-only work.

## Repairs and witnesses

| Mechanism | Change | Evidence and limit |
|---|---|---|
| Windows cannot recursively delete read-only Git loose objects | `CheckpointTestScope` clears read-only attributes with the existing shared helper, after its inventory/custody checks and before deletion. The helper's clearing method is exposed internally. | A Linux test sets the real read-only bit and injects the Windows deletion refusal at `beforeDelete`. It failed with that refusal before the fix and passed afterward. Links and unsafe roots still refuse before attributes are changed. This does not attribute every original access-denied error to that bit. |
| Runtime Windows capabilities registered as Linux | The local `RunnerSeatReleaseFixture.RegisterRuntimeAsync` passes `capabilities.Platform` explicitly. The shared phone-home fixture and product platform guard are unchanged. | A real test server receiving forced `Platform="windows"` capabilities returned 409 before the fix and issued a ticket afterward on Linux. |
| Git porcelain uses forward slashes for Windows paths | The half-reset registration assertion normalizes its expected path to Git separators. | Forced `C:\fixture\trees\moved-source` against `C:/fixture/trees/moved-source` failed before and passed after; all identity/byte-preservation assertions remain. |
| Relative symlink targets retained `/` on Windows | The graph fixture converts link targets to backslashes only on Windows, before creating either kind of link. | The pure-string Windows target witness failed before and passed after. Both assigned linked-path methods also passed on Linux. Actual Windows link creation still requires a native rerun. |
| Native recovery fixture omitted tool output custody | `CheckpointRecoveryWindowsTests.Run` creates/seals a genuinely owned tool directory; its payload writes use `SafeWrite` and reseal the ledger. | An isolated Linux API probe reproducing the old setup returned `TOOL CLEANUP Refused ... reason=output-ledger-missing`. The final portable test executes the corrected `Run` fixture and requires actual removal. Native child/process rows remain skipped here. |

The first four witnesses were built and run separately at `9e513a70c5b0bcc3fc2a6d63531ffec1ef5e19c9`: four executions, four intended failures, no skips. Their exact methods are:

- `CheckpointTempScopeTests.teardown_clears_read_only_git_objects_before_windows_delete`
- `RunnerSeatOrphanSweepTests.Windows_capabilities_register_with_the_runtime_platform`
- `AgentTaskLandHalfResetTests.Windows_registration_path_matches_git_porcelain`
- `LandGraphEvaluationTests.Windows_link_targets_use_native_separators`

The recovery API probe is **not** a TUnit red receipt. Its initial witness commit `973b31fe0c7b12577b34d2e991eab47fb2ac9e46` referenced helpers supplied in the following repair and was not independently built. The final witness is self-contained around the existing `Run` helper and public cleanup API. Its green TUnit execution is included below.

## Diagnostic-only changes and native work still owed

- **TerminalRunnerSeatReleaseTests / `Answer_racing_release_preserves_one_owner`:** the fixture catches deletion failure only to print the original exception, current PID, root metadata, bounded no-follow discovery of `landing.lock`, file attributes/timestamps and Windows ACLs, then rethrows. No retry or handle-owner fix was invented. The owner of the original Windows handle is still unknown.
- **CardDoneWorktreeCleanupTests / CardWorktreeCleanupRecoveryTests:** their shared fixture now prints every cleanup result, including `Residue`; on Windows refusals it also prints endpoint metadata/ACLs and bounded Git registration/status observations with exit/output/diagnostic data. Diagnostic errors do not replace the cleanup result. Linux green does not explain the original Windows early refusals; use the new reason first.
- **CheckpointRecoveryWindowsTests:** cleanup receipts and wait output/exit are now retained. Copy failure prints source/destination DLL metadata and ACLs. The staged DLL's three-second read stall remains unestablished; its size alone is not a diagnosis. The reader deadline and content limits are unchanged.
- **CheckpointConfinementLedgerTests:** read-only teardown is repaired. If native `dirty-tracked / DIRTY App/App.csproj` remains, inspect the first body failure rather than attributing it to teardown. Capture effective `core.autocrlf`, `core.eol`, attributes, raw line-ending counts, `git status`, `git diff`, and index/worktree object hashes before disposal. No newline-policy fix was guessed.
- **OutputCleanupTests, CheckpointSelectedOutputTests, half-reset, orphan sweep and graph linked-path rows:** the common/fixture changes are verified on Linux; Windows outcomes remain owed. Four OutputCleanup methods and fourteen native recovery methods were skipped.

The next native owner should run the exact assigned classes from a fresh producer-owned isolated build of the landed code, only after the live qualification releases its lane. Preserve TRX and stdout/stderr. A per-class command, after that owner's one isolated build, is:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label db2c-filelane-native -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-db2c-filelane-native/ -- --treenode-filter '/*/*/TerminalRunnerSeatReleaseTests/*' --report-trx --report-trx-filename terminal.trx --results-directory .antiphon/db2c-filelane-native/terminal
```

Substitute each exact class from the results table. Do not use the invalid combined expression recorded below. The narrower original terminal failure is `/*/*/TerminalRunnerSeatReleaseTests/Answer_racing_release_preserves_one_owner`.

For handle ownership, capture **while the failing fixture process is still alive**. Use the already trusted Handle executable (`WorktreeLockSettings.HandleExecutablePath`), existing rights and completed license setup; if unavailable, record that fact. Do not infer ownership from a later empty handle list. The exact read-only capture is:

```powershell
# Values come from FIXTURE_FILE output and the existing approved Handle configuration.
& $trustedHandle -nobanner -v $failedPath 1> $handleCsv 2> $handleStderr
$handleExit = $LASTEXITCODE
Get-Item -LiteralPath $failedPath -Force | Select-Object FullName,Length,Attributes,CreationTimeUtc,LastWriteTimeUtc,LinkTarget
(Get-Acl -LiteralPath $failedPath).Sddl
# Record $handleExit, both output files, and each returned PID's start time/path twice.
```

The current instrumentation captures PID/path/metadata/ACLs, but does not automatically launch an unconfigured Handle tool. If the DLL stall recurs, retain its full inner refusal and capture native CreateFile/ReadFile operation results and durations for that exact path, together with the active holder PID/start/path. Do not widen the three-second stall window to manufacture green.

## Build and test evidence

All drivers used the server2 host build-slot wrapper. All test executions used the compiled output with `--no-build`; the checkpoint script was not used because this test DLL exceeds its 64 MiB cap, as stated in the brief.

There was **one full isolated dependency build**, then **two test-only recompiles** with `--no-restore --property:BuildProjectReferences=false` to obtain red/green evidence and repair an introduced diagnostic compile error. This is an explicit departure from a literal one-invocation reading of the build budget; no second dependency build was run.

| Source | Invocation | Result | Build time |
|---|---|---|---:|
| `9e513a70c5b0bcc3fc2a6d63531ffec1ef5e19c9` | Full `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-debug-2a61a460/ --nologo -v:q` | 0 errors | 3m37.42s |
| `db27094013c1c98e31abb25219b3e9356cad32f9` | Test-only recompile | Failed: introduced CS0173 in the new ACL conditional | 3m33.17s |
| `05e51a70c5bdac0215d9dd017da21c7cae653381` | Test-only recompile, explicit `FileSystemSecurity` type | 0 errors, 1,154 warnings | 2m28.63s |

Final exact class filter for every row was `/*/*/<Class>/*`, with a fresh TRX directory and no build. All eleven driver exits were zero:

| Class | Total | Passed | Failed | Skipped | Driver wall seconds |
|---|---:|---:|---:|---:|---:|
| CheckpointTempScopeTests | 12 | 12 | 0 | 0 | 6.093 |
| CheckpointConfinementLedgerTests | 20 | 20 | 0 | 0 | 90.542 |
| OutputCleanupTests | 49 | 45 | 0 | 4 | 21.349 |
| CheckpointSelectedOutputTests | 3 | 3 | 0 | 0 | 17.427 |
| CheckpointRecoveryWindowsTests | 15 | 1 | 0 | 14 | 5.489 |
| RunnerSeatOrphanSweepTests | 18 | 18 | 0 | 0 | 82.513 |
| AgentTaskLandHalfResetTests | 56 | 56 | 0 | 0 | 61.179 |
| LandGraphEvaluationTests | 93 | 93 | 0 | 0 | 333.285 |
| TerminalRunnerSeatReleaseTests | 39 | 39 | 0 | 0 | 73.293 |
| CardWorktreeCleanupRecoveryTests | 6 | 6 | 0 | 0 | 34.040 |
| CardDoneWorktreeCleanupTests | 20 | 20 | 0 | 0 | 40.468 |
| **Total** | **331** | **313** | **0** | **18** | **765.678** |

Largest selection was 5m33s driver wall; none reached 30 minutes. These are single-run observations, not measured p95 budgets; no timeout was changed. TRX has per-case durations. Opt-in phase JSONL is retained; missing phases/unattributed remainder remain unknown, not evidence of zero I/O. Build time and slot/driver overhead are separate from test case spans.

Generated evidence remains ignored under `/work/worktrees/task-2a61a460/.antiphon/evidence-2a61a460/`:

- `<Class>/<Class>.trx` and `<Class>.log` for each final row.
- `red-{path,link,readonly,registration}/` and matching `.log` files for the four intended red tests.
- `recovery-ownership-red.log` and its standalone witness script for the valid API refusal.
- `build-red.log`, `build-green.log`, `build-green-fixed.log`, `verify-checkpoints.log`, `verify-application.log`.
- `timing/cases-*.jsonl`, source-stamped class start/end lines and the exact output-directory inventory.

Preserved non-creditable attempts: a reflection harness could not initialize the test assembly outside TUnit; a standalone probe initially omitted an image-directory value; and a combined class expression selected **zero tests**, exit 8. None counts as red/green evidence. Exact class runs above replaced the zero-test attempt.

All 33 producer-owned `bin-debug-2a61a460` directories are removed before settlement. A future rerun must rebuild its isolated output first. `git diff --check`, the base..HEAD evidence-policy check and conflict-free `git merge-tree --write-tree origin/master HEAD` are final settlement checks; the final task report records their results and pushed tip.

--- next stage ---
next: land
handoff: Fixture-only repairs and diagnostics; Linux selections are green. Native Windows qualification and unresolved handle/read/refusal causes remain owed to the next native owner; this is not an RC qualification receipt.
artifact: /work/worktrees/task-2a61a460/docs/investigations/2026-10-25-rc-db2c2088-windows-file-lane.md
