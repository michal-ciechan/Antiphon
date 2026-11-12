# restart-session-runner.ps1: `Get-RunnerProcessIdentity` is not recognized (Debug 4ca0643a, 2026-11-12)

## Symptom

On 2026-11-12 at 09:48Z the orchestrator restarted the desktop session runner from the canonical
checkout `C:\src\Antiphon` at `rc/2026-11-18` (`56e96eabf698e9c1fcd462836ad32ed1fb259e83`) right
after an AppHost restart. The run failed at once and stopped nothing:

```text
action-failed: stop-service: The term 'Get-RunnerProcessIdentity' is not recognized as a name of a cmdlet, function, script file, or executable program.
RUNNER RESTART RESULT: {"outcome":"action-failed","mode":"restart","exitCode":1,"timeoutSec":180,"waitElapsedMs":0,"commandElapsedMs":2260.035,...}
```

The runner (PID 6092 on 17204) stayed up and healthy.

## How it was actually invoked

The brief recorded the documented command line (`pwsh -NoProfile -File scripts/restart-session-runner.ps1`).
The orchestrator transcript (session `b2f1a869`, sequence 247327) shows the real command. It
launched a detached process through `Win32_Process.Create`, so it could redirect every stream to a
log:

```text
"C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -Command "& 'C:\src\Antiphon\scripts\restart-session-runner.ps1' *> '<log>'; exit $LASTEXITCODE"
```

The difference between `-File` and the call operator `&` is the whole bug.

## Root cause

`Get-RunnerProcessIdentity` has been defined in `scripts/session-runner-restart-health.ps1` since
CARD-0420 (`a253a2ec3`, 2026-09-07). `git log -S Get-RunnerProcessIdentity --all -- scripts` shows
only that commit. It has never been renamed or removed, and the entry point dot-sources the helper
file. Nothing was missing from the dot-source or the load order, and it was not a Windows
PowerShell 5.1 versus pwsh 7 difference.

`New-RunnerRestartPlatform` builds its platform members as `{ ... }.GetNewClosure()` blocks. These are
`$readPid`, `$stop`, `Verify`, `Census` and `Control`. A `GetNewClosure()` block is bound to a new
dynamic module. When it runs, it resolves commands from that module and the **global** scope only,
not from the scope chain of the script that created it.

- With `pwsh -File script.ps1`, PowerShell runs the script in the global scope. The dot-sourced helper
  functions are global, so the closures find them. This was verified on Linux pwsh 7.5.4 and on pwsh
  7.6.0, where a variable that a `-File` script sets is visible as `$global:`.
- With `& script.ps1`, which is what `pwsh -Command "& '...'"` runs, the script runs in its own script
  scope. The dot-sourced helpers are script-scoped, so every closure that calls
  `Get-RunnerProcessIdentity` by name throws "not recognized".

The repository's own forwarders use the call operator too: the root `restart-session-runner.ps1`
(`& "$PSScriptRoot\scripts\restart-session-runner.ps1" @PSBoundParameters`) and `restart.ps1`. Any
invocation through them has failed the same way since `a253a2ec3`.

On the base commit, under `&`, the closures behave like this:

| Platform member | Behaviour under `&` |
|---|---|
| `Census` (`$readPid`) | `$readPid` swallows every error (`try { ... } catch { }`), so `supervisor` and `wrapper` are silently `$null`. No error is raised. |
| `Control 'stop-service'` | `$stop (& $readPid ...)` returns early on `$null`. Then `Get-Process Antiphon.SessionRunner \| ForEach-Object { Get-RunnerProcessIdentity ... }` throws. This is the reported error. It fires only when a process named `Antiphon.SessionRunner` exists. |
| `Control 'stop-supervisor'` and `Control 'kill-sessions'` (`$stop`) | Throw the same error. |
| `Verify` | Throws the same error once the identity path equals the expected Debug executable. |

A second, quieter hazard follows from the silent `Census`. With no `Antiphon.SessionRunner` process
present, `stop-service` would not throw. `Invoke-RunnerRestart` would then see `-not $census.supervisor`
and, even without `-Hard`, run `delete-pids` and `start-task`. That is the "no supervisor" path,
taken while a supervisor might still be alive.

## Fix

In `scripts/session-runner-restart-health.ps1`, `New-RunnerRestartPlatform` now captures the helper
once:

```powershell
$identityOf = ${function:Get-RunnerProcessIdentity}
```

Every `GetNewClosure()` block calls `& $identityOf ...` instead of the function name. The block
captures the variable, and the function's script block stays bound to the session state where it
was defined, so it resolves under `-File`, under `&` and from any caller scope. `Identity` and
`Process` are plain script blocks, not closures. They are invoked from `Invoke-RunnerRestart` in the
helper's own scope chain and keep the direct call. The script remains ASCII-only and uses Windows
PowerShell 5.1 syntax: the `${function:Name}` variable notation exists in 5.1.

## Red witness

The witness is
`RunnerRestartScriptCompatibilityTests.Real_platform_closures_resolve_helpers_under_the_call_operator`
in `tests/Antiphon.SessionRunner.Tests`, with arguments `pwsh.exe` and `powershell.exe`. The
`powershell.exe` row skips off Windows (CARD-1669).

- The driver runs under `-File`, as the fixture always does. It defines a **global** `Get-Process`
  fake. A bare name such as `Antiphon.SessionRunner` returns `[pscustomobject]@{ Id = $PID }`, the
  test's own child. `-Id` is delegated to the real cmdlet. The driver then invokes a call-operator
  entry (`& call-operator-entry.ps1`) that dot-sources the **real** helper, exactly like
  `restart-session-runner.ps1`. It builds the **real** platform with `-Root` set to the fixture
  directory.
- The entry writes `logs/session-runner.supervisor.pid` with the child's PID. It then calls
  `Census`, `Control 'stop-service'`, `Control 'stop-supervisor'` and `Verify`. `stop-supervisor`
  receives the child's PID with a synthetic start time, so it must throw `Process identity changed
  before stop`. `Verify` receives the child's real identity with the expected Debug path.
- Nothing real is stopped. The listed process is the test child. Its path is never the checkout's
  Debug runner, so `stop-service` skips it, and `stop-supervisor` refuses on the identity mismatch
  before `taskkill`. The fixture has no `session-runner.service.pid`, `write-state`, `delete-pids` or
  `start-task` call.
- The test asserts that `Get-RunnerProcessIdentity` is not global in the driver. This proves that the
  witness keeps the call-operator shape.

Red at `9e420856946e0b358f32eae4f5c6785c8137e376`, before the fix, on Linux pwsh 7.5.4:

```text
{"census":null,
 "stopService":"The term 'Get-RunnerProcessIdentity' is not recognized as a name of a cmdlet, function, script file, or executable program.",
 "stopSupervisor":"The term 'Get-RunnerProcessIdentity' is not recognized ...",
 "verify":"The term 'Get-RunnerProcessIdentity' is not recognized ...",
 "listed":1,"helperGlobal":false,"pid":26339}
```

The same probe was repeated by hand with pwsh 7.6.0. The base helper under `&` failed with the same
error. The base helper under `-File` passed, and the fixed helper under `&` passed.

## Why no test caught it

1. **The fixture never runs the real platform.** `RestartFixture.Run`, which drives
   `RunnerRestartHealthTests`, `RunnerRestartPreflightSafetyTests` and
   `Documented_arguments_execute_the_expected_mode`, copies the real entry. It replaces the helper's
   `New-RunnerRestartPlatform` with the synthetic `Fixtures/RunnerRestart/platform.ps1`, whose
   closures define their own fakes. The real `Control`, `Census` and `Verify` bodies never executed in
   any test.
2. **Every child runs under `-File`.** `RestartFixture.Process` launches with `-File`, and so does
   the C1698 harness (`scripts/test-toolchain-floor.ps1`). That puts the dot-sourced functions in the
   global scope, which is the one invocation form where the closures work. The only test that builds
   the real platform, `Hung_http_response_is_canceled_at_wait_deadline`, also runs under `-File`, and
   it overrides `Census`, `Identity`, `Verify` and `Control` with its own blocks.
3. **The failing line needs a live `Antiphon.SessionRunner` process.** No test or harness lists one.
   The C1698 entry runs use `-WaitOnly -Hard`, which fails at `validate-arguments` before any control
   operation. Linux runs never have a real runner.
4. **`Census` swallows the same failure.** `$readPid` catches every exception, so the read path
   degraded to "no supervisor" without an error that a test could see.
5. **The documented command is `-File`, but the operator path is not.** The documentation, the help
   text and the stale-runner warning all say `pwsh -File`. Orchestrators launching detached with
   redirected output, and the repository's own root forwarders, use `&`. That invocation form was
   never tested.

The new witness closes items 1 to 4 for this file. It runs the real closures under the call
operator with a listed process.

## Related risk (not changed here)

`scripts/lib/nightly-health.ps1` (`New-NightlyProductionWindmillApi` and the nearby closures) has
`GetNewClosure()` blocks that call script-defined functions by name, such as
`Invoke-NightlyWindmillHttp` and `Get-NightlyDueUtcForLondonDate`. It works when
`scripts/nightly-health.ps1` runs under `-File`, and breaks the same way if it is ever invoked with
`&` or from a function scope. It needs its own card.

## Verification (server2, Linux, pwsh 7.5.4)

| Run | Commit | Result |
|---|---|---|
| Witness, red | `9e420856946e0b358f32eae4f5c6785c8137e376` | 2 total: 1 failed (`pwsh`), 1 skipped (`powershell.exe`) |
| Witness, green | fix `49ad68919addd68c625eaa52881ba00d046ad994` | 2 total: 1 passed, 1 skipped |
| SessionRunner `(RunnerRestart*)\|(ProcessSpawnLimitTests*)` | `49ad68919` | 124 total: 100 passed, 24 skipped (`powershell.exe` and Windows-only rows), 0 failed |
| Antiphon.Tests `ToolchainFloorWarningTests` + `NightlyScriptsTests` + nine-class guard union + `DerivedImpactIndexTests` | `49ad68919` | 124 total: 123 passed, 1 failed; nine-class guard subset 78/78 |
| `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`, rerun | `49ad68919` | 1/1 passed |

The single failure was `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`
with "The application to execute does not exist ... Antiphon.DerivedImpactIndex.dll". That is the
known environment precondition, because the regeneration driver is not in the solution build. After
`scripts/regenerate-derived-impact-index.ps1` reported `derived-impact-index unchanged`, it passed.

**Owed (orchestrator, native Windows):**

- Run the real restart on the desktop. Use either the documented `-File` form, or the detached
  `pwsh -Command "& '...\restart-session-runner.ps1' *> log"` form that failed. Confirm
  `"outcome":"healthy"`.
- Run the `powershell.exe` rows of `RunnerRestartScriptCompatibilityTests`.
