# Review 990531c3: CARD-1851 PID read fail-closed and nightly-health closures (round 1, Final)

Verdict: **CLEAN**. No blockers and no regressions. Three advisories stand (A-1..A-3). None holds the land.

- Subject: `origin/feat/card-task-2eeb94f5` at `c05ad9146afbccd18ba29bceb1ad57bc9514404f`. The range is
  `aa5e14b61b4df4be16afbbc9b1d81ce49de5bc58..c05ad9146` (8 commits). Code/landing owner
  `2eeb94f5-6f28-4c2c-8b5a-f53a505460f7`.
- Production files: `scripts/session-runner-restart-health.ps1` (the `$readPid` closure; it is
  dot-sourced by `scripts/restart-session-runner.ps1`) and `scripts/lib/nightly-health.ps1`.
- Host: server2 (Linux, pwsh 7). Every build and run went through the checkpoint tool or
  `scripts/build-slot.ps1`. Child `PATH=/opt/powershell:$PATH`, `TEMP=/tmp`. Nothing ran on the desktop.

## What was checked

| Check | Result |
|---|---|
| CP-1 `RunnerRestartScriptCompatibilityTests/*` (SessionRunner.Tests) | 15 passed, 0 failed, 17 skipped (Windows-only `powershell.exe` cases and one native pwsh case; skip reason CARD-1669) |
| CP-2 `LiveLaneRunnerTests`, `NightlyCoreTests`, `ProcessSpawnLimitTests` (Antiphon.Tests) | 62/62 |
| CP-3 `C544_ProductionJobAdapter`, `C545_JobResultFetch` | 2/2 |
| CP-4 `ProcessSpawnLimitTests` (SessionRunner.Tests) | 2/2 |
| CP-5 `ProcessSpawnLimitTests` (Agents.Pty.Tests) | 2/2 |
| G-1 guard union in Antiphon.Tests (`TestClassificationPolicy`, `TestClassificationGuard`, `SlowTestRegistryMerge`, `SlowTestTripwire`, `NativeLinuxPolicyWitness`, `DerivedImpactIndex`) | 78/79. The one failure, `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`, was the environment precondition: "Antiphon.DerivedImpactIndex.dll does not exist". After building that driver it passed 1/1 in a leased rerun. |
| G-2 shared guards in SessionRunner.Tests (`TestClassificationGuard`, `NativeLinuxPolicyWitness`) | 4/4 |
| Red witness: HEAD tests against master's two production scripts, same CP-1 build copied into a scratch worktree | 9 failed and 2 passed (the `missing` and `live` controls). PID error states `directory`, `locked`, `invalid`, `overflow`, `zero`, `wrapper-directory` and `missing-parent` reached `healthy` through `delete-pids`/`start-task`. The two nightly cases failed with `The term 'Invoke-NightlyWindmillHttp' is not recognized`, the A-2 hazard itself. |
| ASCII | 0 non-ASCII bytes in both production scripts. The CP-1 ASCII/parser test now also covers `nightly-health.ps1`, `lib/nightly-health.ps1` and `lib/nightly-common.ps1`. |
| `check-evidence-diff.ps1` | commits=8, violations=0, exit 0 |
| `git merge-tree --write-tree origin/master c05ad9146` (master `aa5e14b61`) | clean, tree `4afe22757043e2f0cf1f95ac55f3ff90c144629f` |

All seven rows ran in one checkpoint run, `20261112-220013-a724`, at `c05ad9146`. Every row shows
`dirty=0`, `sourceState=clean`, `buildSource=verified` and `slot=granted`. `validate` refuses that
run (`report_failed`). The causes are the 17 native skips in CP-1, which cannot validate on Linux by
construction, and the G-1 precondition red. So `reviewedSourceClean` is false.

## Code reading

- **A-1 (`$readPid`).** Only `[IO.FileNotFoundException]` returns `$null`. Any other read error
  propagates. So do empty, malformed, overflowing (`TryParse`) and non-positive content (`throw`). The
  first `Census` call sits inside `Invoke-RunnerRestart`'s outer `try`, before `write-state`. A failure
  there is `action-failed` with no control taken, so the no-supervisor branch (`delete-pids`,
  `start-task`) can no longer be reached on an unknown PID. The `stop-service` re-read can also throw
  inside that same `try`. That happens after `write-state` and before any stop, which also fails closed.
  The post-wait `Census` stays wrapped. The closure uses only captured `$logDir` and `$identityOf` plus
  .NET calls, so it behaves the same under `-File` and `&`, and from the root forwarders
  (`restart-session-runner.ps1`, `restart.ps1 -RestartRunner`), which use `&`. It uses only
  PS 5.1-compatible constructs: typed catch on a wrapped method exception, `[ref]` TryParse and `throw`.
  The real writers (`run-daemon.ps1`, `autostart-session-runner.ps1`, `DaemonProcessService.WritePid`)
  write a bare PID and delete the file on exit. No normal path leaves an empty or garbage file.
- **A-2 (nightly-health).** All three `GetNewClosure()` blocks call captured helper scriptblocks:
  `Invoke-NightlyWindmillHttp`, `ConvertFrom-NightlyWindmillJob`, `Get-NightlyDueUtcForLondonDate` and
  `Get-NightlyLondonTimeZone`. No by-name helper call remains in a closure.
- **Seams.** The only new seam is an optional `[TimeZoneInfo]$TimeZone` parameter. When it is null it
  falls back to `Get-NightlyLondonTimeZone`. Every other caller passes `-LondonDate` by name, so the happy
  path is unchanged. No HTTP seam was added. The production closures hold the same-file
  `Invoke-NightlyWindmillHttp`. If it were absent, `& $null` would throw before any fetch, never no-op.
- **Network in tests.** The new nightly test shadows `Invoke-WebRequest` with a global function and
  enforces the GET, bearer token and 30 s contract. It uses `https://nightly.invalid/`, and unknown
  routes throw. No real Windmill, scheduler or runner endpoint is reachable. The PID test replaces
  `Control`, `Probe`, `Identity` and `Verify`, so no process is stopped and no Scheduled Task is called.
- **Windows-only cases.** These are skipped through `Skip.Test` with the CARD-1669 reason, never passed
  vacuously. They stay pending native obligations.

## Advisories (to cards; none holds the land)

- **A-1. The fail-closed scope is wider than the no-supervisor branch.** `-WaitOnly`, which takes no
  controls, and `-Hard` now also stop with `action-failed` on an unreadable PID file. A checkout whose
  `logs/` directory does not exist yet now stops with `DirectoryNotFoundException` instead of
  bootstrapping `start-task`. This follows plan V-1 and is safe, but it is an operator-visible change: the
  remedy is to repair or delete the PID file, or create `logs/`. Consider letting `-WaitOnly` observe
  with an unknown supervisor, and treating a missing `logs/` like a missing file.
- **A-2. Linux cannot produce a clean receipt for CP-1.** Its 17 native skips always make validate refuse.
  The Windows CP-1 run, on both shells, is the only clean certificate for these rows.
- **A-3. `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` needs a prebuilt
  driver.** The `scripts/regenerate-derived-impact-index` driver is not in the solution, so the test is
  red on any fresh checkpoint output. This is an environment precondition, not caused by this change.

## Still owed (not discharged here)

- Native Windows CP-1 at `c05ad9146` on `pwsh.exe` and `powershell.exe` 5.1: 17 cases.
- An operator-owned desktop `&` restart acceptance.
- The 8 Mutation variants (swallowed-read, the four http-name variants, conversion/due/timezone-name)
  are WAIVED by the operator (2026-11-12). They were not discharged.

--- review evidence ---
subjectTaskId: 2eeb94f5-6f28-4c2c-8b5a-f53a505460f7
reviewedSourceSha: c05ad9146afbccd18ba29bceb1ad57bc9514404f
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: aa5e14b61b4df4be16afbbc9b1d81ce49de5bc58..c05ad9146afbccd18ba29bceb1ad57bc9514404f
