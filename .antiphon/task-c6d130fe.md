# c6d130fe Debug result

No failure reproduced on Linux: 23 passed, 0 failed, 3 explicitly skipped. No product or fixture fix was justified. The seven recorded Windows failures remain unresolved pending native reruns and their assertion diagnostics.

Tested source: `bedea1c03cd788d83b2ff54c40bfb603927bd69c`, exactly equal to fetched `origin/rc/2026-10-26` at task start. Assigned branch remained `feat/card-task-c6d130fe`; no reset, rebase, branch switch, desktop access, native lock, release operation or shared-service restart occurred. This report is the only tracked deliverable.

## Verification

One isolated build, leased through the host build-slot gate:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c6d130fe-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c6d130fe/ --nologo
```

Build passed with 0 errors in 219.11 seconds (lease held 220 seconds). Existing warnings remain in the log. Four sequential compiled-DLL runs followed, each through `scripts/build-slot.ps1`, using the exact filters below. Direct execution avoided the checkpoint reader limit specified in the brief; no implicit rebuild occurred.

| Class filter | Passed | Failed | Skipped | Run duration |
|---|---:|---:|---:|---:|
| `/*/*/C1008PlatformContractTests/*` | 4 | 0 | 2 | 2.123 s |
| `/*/*/ReleaseGateRunTests/*` | 8 | 0 | 1 | 62.934 s |
| `/*/*/RunCheckpointRepeatScriptTests/*` | 4 | 0 | 0 | 50.486 s |
| `/*/*/CardRelationshipScriptTests/*` | 7 | 0 | 0 | 12.953 s |

Each invocation used this form, substituting its exact class filter and unique evidence names:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c6d130fe-c1008 -- dotnet tests/Antiphon.Tests/bin-c6d130fe/Antiphon.Tests.dll --treenode-filter '/*/*/C1008PlatformContractTests/*' --report-trx --report-trx-filename c1008.trx --results-directory /tmp/c6d130fe-results/c1008
```

## Classification and native reruns

- C1008: native-only audit coverage. `C1050_Windows_remote_outcomes_are_exact` and `C1050_Windows_rolling_outcomes_are_exact` explicitly skip Linux. All four portable tests pass, including the corrupted-receipt negative matrix. Native audit source requires an already built Windows apphost and the outer producer's `checkpoint-build-source.json`; preserve both when rerunning. No claim is made about which assertion failed on the desktop.
- Release gate: not reproduced on Linux. All eight runnable cases pass, including RC parameter hops and master-state isolation. `C599_CloneOwnership` explicitly skips because it checks Windows canonical roots. Existing fixture code already identifies summaries by parsed `runId` and injects the Windows execution-platform observation. No new Windows-path defect was established.
- Checkpoint repeat: not reproduced on Linux. All four cases pass, including receipt validation, deliberate partial failure, invalid input and build reuse/refusal. Existing per-fixture output isolation is retained.
- Card relationships: not reproduced on Linux. All seven cases pass, including Unicode reason-file round-trip, JSON output, relationship lookup failures and fetched-token behavior.

The brief supplied only class-level Windows failure counts, not failing method identities or messages. Attribution to input guards, CRLF, shell selection, state leakage or load would be speculation. No assertions, timeouts or skips were changed, and no unsupported fix or new witness was invented.

Native rerun rows are the four exact class filters in the table, on the candidate selected by the caller after its current native qualification releases ownership. In an existing native PowerShell lane, use the same compiled-output command form with that producer's Windows DLL path. Keep the C1008 build-source stamp beside its original output and retain TRX plus stdout/stderr for any remaining failures. Do not start a second native qualification or acquire its lock while the existing owner is active.

## Evidence and cleanup

Ignored full logs, TRX and HTML reports: `/work/worktrees/task-c6d130fe/.antiphon/c6d130fe-evidence/`. These are local generated evidence and are not pushed. The committed report preserves counts, tested source, commands and limitations. The 33 producer-owned `bin-c6d130fe` directories are inventoried in `outputs.txt` there and were removed after all runs. Evidence-history validation over report commit `1f06eb63ed0a000885d461dbc5bdc6232c47b648` passed: 1 commit, 1 entry, 0 violations. No reviewed-source-clean or Windows-green claim is made.

The read-only `git merge-tree --write-tree origin/rc/2026-10-26 HEAD` check was clean. After fetching master, its corresponding check found inherited add/add conflicts in `.antiphon/task-af2b78c8.md` and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`. Neither path belongs to this report-only delta; no merge or conflict repair was performed. Landing should preserve RC/master target policy and handle inherited divergence separately.

--- next stage ---
next: land
handoff: Report-only result; no product fix to land. Preserve the seven Windows failures as unresolved and rerun the four native rows with assertion diagnostics after the current native owner releases its lane.
artifact: /work/worktrees/task-c6d130fe/.antiphon/task-c6d130fe.md
