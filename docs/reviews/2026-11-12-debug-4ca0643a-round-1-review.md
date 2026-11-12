# Review 7128bbf6: Debug 4ca0643a restart-session-runner call-operator fix (CARD-1514, round 1, Final)

Verdict: **CLEAN**. No blockers and no regressions. Two advisories stand (A-1, A-2). Neither holds the land.

- Subject: `origin/feat/card-task-4ca0643a` at `8d0ec0050e680975126cdced6a166dda809240c2`. The range is
  `56e96eabf698e9c1fcd462836ad32ed1fb259e83..8d0ec0050`. It has 3 commits: red witness `9e4208569`, fix
  `49ad68919`, investigation doc `8d0ec0050`. The code author was Grok.
- Files: `scripts/session-runner-restart-health.ps1`,
  `tests/Antiphon.SessionRunner.Tests/RunnerRestartScriptCompatibilityTests.cs`,
  `docs/investigations/2026-11-12-restart-session-runner-missing-function.md`.
- Host: server2 (Linux, pwsh 7.5.4). Every build and run went through `scripts/build-slot.ps1`, using
  the native default output, `UseAppHost=false` and `TEMP=/tmp`. Nothing ran on the desktop.

## What was checked

| Check | Result |
|---|---|
| The fix covers every `GetNewClosure()` call site | All 7 closures were audited. Each by-name `Get-RunnerProcessIdentity` call inside a closure now calls `& $identityOf`: `$readPid`, `$stop` (2 calls), `Verify`, `Control` stop-service, and kill-sessions. `Identity` and `Process` are unbound plain blocks. They are invoked from the helper's own scope chain, and the witness drives `Process` under `&`. No other script function is called by name inside a closure. |
| `-File` versus `&`, real entry, scratch copy, `-WaitOnly -TimeoutSec 1`, supervisor pid = a live process | Base `-File`: supervisor found. Base `&`: **supervisor null** (silent). Fix `-File` and fix `&`: supervisor found. |
| Root forwarder `restart-session-runner.ps1` (restart mode, `-TimeoutSec 1`) | Base: `supervisor:null`, then took the **no-supervisor path without -Hard**. The supervisor pid file was deleted, and the run reached `start-task` (`Start-ScheduledTask` is absent on Linux, so action-failed). Fix: supervisor found, no delete-pids or start-task, `wait-expired`. |
| `restart.ps1 -RestartRunner` forwarder | Same as the root forwarder. Base: `supervisor:null`, pid file deleted, action-failed at `start-task`. Fix: supervisor found, pid file kept, `wait-expired`. |
| Witness red/green (the test's exact entry and driver scripts against the base and fixed helper) | Base: `census:null`, and `stopService`, `stopSupervisor` and `verify` all report "The term 'Get-RunnerProcessIdentity' is not recognized". Fix: `census == pid`, `stopService:ok`, `stopSupervisor:"Process identity changed before stop"`, `verify:true`, `listed:1`, `helperGlobal:false`. The test calls the real stop-service, stop-supervisor, Verify and Census closures with a global `Get-Process` fake. It cannot go red by construction: its six assertions each read real output. |
| `SessionRunner.Tests` `/*/*/RunnerRestart*/*` at `8d0ec0050` | 122 total: 98 passed, 24 skipped, 0 failed. The skips are the `powershell.exe` rows and Windows-only rows (CARD-1669). The new method's `pwsh.exe` row passed, and its `powershell.exe` row was skipped. |
| Guard union in `Antiphon.Tests` (`TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`) | 52 total: 52 passed, 0 failed. All five classes are present in the TRX: 41, 1, 5, 2 and 3 tests. |
| ASCII and Windows PowerShell 5.1 | 0 non-ASCII bytes in the touched helper and the entry. `${function:Name}` and `& $scriptblock` are valid in 5.1. 5.1 was checked statically only, because the `powershell.exe` rows skip off Windows. |
| No other behaviour change | The diff only replaces function-name calls with `& $identityOf` calls to the same function body. Under `-File`, the base and fix probes give identical results. |
| `check-evidence-diff` base..tip | commits=3 entries=0 violations=0. |
| `git merge-tree --write-tree origin/master 8d0ec0050` | Clean (tree `d25a55523`). `origin/master` = `56e96eabf`, the subject's own base. |
| Commit order | The red witness `9e4208569` precedes the fix `49ad68919`. |

The brief named no plan `### Checkpoints` table, so the brief's class list was run directly through
`build-slot.ps1`. No other build or test runs were made beyond the two builds that those runs needed.

## Advisories (card, do not hold the land)

**A-1: Census still fails open on errors other than the fixed one.**

- Where: `scripts/session-runner-restart-health.ps1:69-72` (`$readPid` `try { ... } catch { }`) and
  `:2-7` (`Get-RunnerProcessIdentity` catch returns `$null`).
- Failure: the reported error class is closed. The pid is now read under both invocation forms, and
  the witness pins `census == pid`. But any other exception on the read path, such as an unreadable or
  locked pid file, or a `StartTime` access denial, still becomes `supervisor = $null`.
  `Invoke-RunnerRestart:220` then runs delete-pids and start-task without `-Hard`.
- Why: this is pre-existing, not a regression. The supervisor writes the pid file once
  (`autostart-session-runner.ps1:35`), so the remaining window is narrow. Failing closed changes
  restart behaviour, which this Debug was told not to do.
- Fix: in a separate card, have `$readPid` return null only for a missing file or a non-numeric
  pid, and rethrow other errors so the restart becomes action-failed. Consider a separate unknown
  state for an identity that exists but cannot be read.

**A-2: Related closure hazard in `scripts/lib/nightly-health.ps1`.** This is recorded by the author
under "Related risk". Its `GetNewClosure()` blocks call script functions by name and would break the
same way if invoked with `&`. File a card if none exists. Separately, the untouched root
`restart-session-runner.ps1:6` carries one em dash in a comment. It predates this change
(`d04ab36d2`) and is harmless.

## Owed (not this review)

- The `powershell.exe` rows of `RunnerRestartScriptCompatibilityTests`, on native Windows.
- A desktop restart in the `&` or detached form. The orchestrator proved only `-File`.
- Every PC stays pending for SourceLanding Mutation.
