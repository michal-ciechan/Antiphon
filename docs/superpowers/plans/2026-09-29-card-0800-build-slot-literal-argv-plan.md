# CARD-0800: pass literal wildcard argv through scripts/build-slot.ps1

Date: 2026-09-29. Stage: Plan, with TestDesign folded in (the brief asks for the closed
`### Checkpoints` table and per-CP red/green expectations). Next: Code.
Baseline: `15b66136a6d2081754935bfdc88139e761ed4e57`.
Card `73602ba0-c74f-4ead-bd11-b851de455a5b` on board `8988ca03-7414-47ad-b0b6-51556c701703`,
read with `scripts/card.ps1 get CARD-0800`. Owner docs read: `docs/testing-and-build.md`
(Build slots, Checkpoint manifest, Checkpoint runner tool), `docs/orchestration-loop.md` for the
stage contract.

## Outcome and scope

Make `scripts/build-slot.ps1` start its wrapped command with the exact argv it was given, on
Linux as on Windows, so a TUnit filter such as `/*/*/*/*[Category=Unit]` reaches `dotnet`
unchanged and the child starts within milliseconds of `BUILD SLOT granted`. Keep the CARD-0589
lease, wait, `-maxcpucount`, exit-code and `C589_COMMAND_SHIM` contract. Add offline harness
cases that are red on Linux at the baseline and green after the fix, wired into
`BuildSlotScriptTests`. Document the launcher in the Build slots section of
`docs/testing-and-build.md`.

Footprint: `scripts/build-slot.ps1`, `scripts/test-build-slot.ps1`,
`scripts/fixtures/c589-command-shim.ps1`, `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`,
`docs/testing-and-build.md`. No change to `scripts/lib/build-slot.ps1` (the lease client),
`scripts/run-checkpoint.ps1` (already literal, see ground truth), `tools/Antiphon.Checkpoints`,
the session runner, AGENTS.md or any bundle.

## Ground truth

Measured on server2 (Linux, pwsh 7.5.4, `$PSNativeCommandArgumentPassing = Standard`) during
this Plan, from the worktree at the baseline.

| Card / brief assumes | What the code and the host do | Consequence |
|---|---|---|
| The wrapper launches with `& $command[0] @rest`. | True: `scripts/build-slot.ps1:83-87`. Both branches use the call operator: the shim seam runs `& pwsh ... -File $env:C589_COMMAND_SHIM @command`, the real path `& $command[0] @rest`. | Both branches go through the new launcher, or the offline tests prove nothing about the path delegates use (D-3). |
| PowerShell expands the wildcard before the native child starts. | Reproduced. `& /usr/bin/printf "%s\n" @a` with `/*/*/*/*[Category=Unit]` had not returned after 250 s (exit 124); `/*/*/Foo/*` not after 100 s; the existing harness token `/*/*/A/*` not after 120 s. A relative pattern that matches is expanded: `*.txt` in a directory holding `a.txt b.txt` arrived as two tokens, `[ab].txt` likewise. A pattern with no match arrives literally (`zz-nomatch-*.nope`, `x[Category=Unit]`). `[Category=Unit]` is a PowerShell character class, so on a real filesystem the Unit filter also matches every depth-4 path ending in one of `C a t e g o r y = U n i`; after the scan the filter would arrive as thousands of paths, not as a filter. | Scan time is host-dependent, so the deterministic red is a relative pattern matching fixture files in the child's working directory (V-1); the incident shape is a bounded start-deadline case (V-2). |
| `ProcessStartInfo.ArgumentList` passes tokens literally. | Confirmed: `/usr/bin/printf` started through `ArgumentList` printed `/*/*/*/*[Category=Unit]` and `test-build-*.ps1` unchanged, instantly. | D-1. |
| `run-checkpoint.ps1` may have the same problem. | It does not. Commit `5fd3a7555` (CARD-0723, 2026-09-25) moved `Invoke-Dotnet` to `ProcessStartInfo.ArgumentList` (`scripts/run-checkpoint.ps1:129-176`); its harness `scripts/test-run-checkpoint.ps1:127-140` launches the script the same way for the same reason (that commit measured about 40 s per scan on this host). | No change to `run-checkpoint.ps1`; it is the precedent for the launcher and for the harness change. |
| The checkpoint tool already uses `ArgumentList`. | True: `tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs:70`, `Execution/DetachedLauncher.cs:29-32`, `Slots/LeaseHolder.cs:46-49`, `Evidence/GitSnapshot.cs:40`. | No change. |
| The offline harness can drive the wrapper with a wildcard. | Not at the baseline: `Invoke-C589Wrapper` (`scripts/test-build-slot.ps1:57`) runs `& pwsh ... -File $script:Wrapper @WrapperArgs`, so the harness expands a wildcard before the wrapper sees it. The existing `wrapper-run` sub-case of `Test-C589_WrapperMaxCpuCountRules` already carries `/*/*/A/*`, so at the baseline on Linux that case scans twice. `BuildSlotScriptTests` is absent from `tests/linux-test-roster.json` and `RunCheckpointScriptTests` is excluded there; neither has a recorded Linux run. | S1 moves the harness launch to `ProcessStartInfo` with an explicit working directory and a deadline (D-4). |
| The child's working directory is unchanged by the switch. | Not automatically: after `Push-Location /tmp`, `[Environment]::CurrentDirectory` stayed at the process start directory while `$PWD` was `/tmp`. The call operator runs the child at `$PWD`; `Process.Start` without `WorkingDirectory` runs it at the process directory, so a relative `tests/Antiphon.Tests` would resolve wrongly for an in-process caller. | `WorkingDirectory = $PWD.ProviderPath` is part of D-1 and pinned by V-1's in-process sub-case. |
| Streamed output and the exit code must be preserved. | With no redirection the child inherits the wrapper's stdout/stderr: `Write-Host before`, child output, `Write-Host after` arrived in that order through a pipe; a child writing to stderr and exiting 7 gave `ExitCode` 7 with its stderr on the wrapper's stderr. | D-6: inherit, do not pump. |
| Cancellation must be preserved. | At the baseline, SIGINT to the wrapper process alone leaves the native child running and PowerShell waiting for it (`sleep 25` survived; the wrapper exited 1 only afterwards). With `Process.Start`, a `WaitForExit(250)` poll loop and `finally { if (-not $proc.HasExited) { $proc.Kill($true) } }`, SIGINT ran the `finally`, killed the child and the wrapper exited within a second. | D-1 includes the poll loop and the tree kill; V-4 pins it on Linux. A runner-side SIGKILL runs no `finally` in either design; a pid-mode lease is reaped by the runner and a renew-mode lease expires, as today. |
| `dotnet` and `pwsh` resolve the same way under `Process.Start`. | `Get-Command dotnet` is `Application` at `/usr/bin/dotnet`, `pwsh` at `/opt/powershell/pwsh`, a `.ps1` is `ExternalScript`. On Windows `npm` can resolve to `npm.ps1` (CARD-0307) and a `.cmd` shim needs `cmd.exe`. | D-2: literal launch only for an `Application` that is not `.cmd`/`.bat`; everything else keeps the call operator, which never globs a non-native command and never globs on Windows. |
| Other wrappers forward arguments the same way. | Inventory below. | Only `build-slot.ps1` and its harness change. |
| Platform. | `GET /api/runner-defaults`: `globalRunnerId = server2`, no kind defaults, operator reason "default all work to server2; desktop only for truly desktop (Windows) work". `GET /api/session-runners`: `desktop` (windows, capacity 2 delegated tasks) and `server2` (linux, capacity 10 sessions), both eligible. | Every checkpoint row runs on the Linux lane (server2). No `-Runner`, no `-Platform`. Windows cannot show the red (PowerShell does not glob there) and needs no green run. |

### Argument-forwarding wrappers: inventory

| Script | Launch shape | Wildcard exposure | Verdict |
|---|---|---|---|
| `scripts/build-slot.ps1:83-87` | `& pwsh -File <shim> @command` and `& $command[0] @rest` | every delegate build/test command, including TUnit filters | **Fix (S2)** |
| `scripts/test-build-slot.ps1:57` | `& pwsh -File $Wrapper @WrapperArgs` | the harness's own wildcard tokens | **Fix (S1)**; precedent `test-run-checkpoint.ps1:127-140` |
| `scripts/run-checkpoint.ps1:129-176` | `ProcessStartInfo.ArgumentList` | none | already literal (CARD-0723); no change |
| `scripts/test-run-checkpoint.ps1:127-140` | `ProcessStartInfo.ArgumentList` | none | already literal; no change |
| `tools/Antiphon.Checkpoints` (`ProcessDriver`, `DetachedLauncher`, `LeaseHolder`, `GitSnapshot`) | `ArgumentList` | none | already literal; no change |
| `scripts/test-client.ps1` (`node .../vitest.mjs run @args`) | call operator, native `node` | a vitest file filter or `-t` regex arrives literally unless it matches a file in `client/`; no plan row has passed one that does | out of scope; named in the D-7 paragraph |
| `scripts/deploy-local.ps1:103` (`& pwsh -File $ScriptPath @Arguments`) | native `pwsh` | fixed switches (`-SkipBrowser`, `-TimeoutSec 30`, restart arguments) | no change |
| `scripts/bootstrap-check.ps1:64` (`& $File @Arguments`) | native tools | fixed (`--version`, `docker volume inspect ...`) | no change |
| `scripts/lib/nightly-common.ps1:524` (`& git -C ... @Arguments`) | native `git` | refs and paths built by the nightly; no call site passes `*`, `?` or `[` | no change; Windows nightly |
| `scripts/deploy-am-service.ps1:125`, `scripts/deploy-nightly-watchdog.ps1:122` (`& $Runner @Arguments`) | `$Runner` is a scriptblock | in-process, never globbed | no change |
| `scripts/run-tests-watched.ps1:69-73`, `scripts/lib/nightly-tests-impl.ps1:74-99` | `Start-Process -ArgumentList` | not the call operator; Windows-only tooling | no change |
| `scripts/nightly-report.ps1:375,536` | builds a rerun string for a human | not a launch | no change |

## Decisions

### D-1: launch native commands through `ProcessStartInfo.ArgumentList`, inherit stdio, poll, kill on stop

In `scripts/build-slot.ps1`, replace the two call-operator launches with one script-local
function (`Start-AntiphonWrappedCommand` or similar) that builds `ProcessStartInfo` with
`UseShellExecute = $false`, no redirection, `WorkingDirectory = (Get-Location).ProviderPath`,
`FileName` = the resolved executable, one `ArgumentList.Add([string]$token)` per token; starts
it; loops `while (-not $proc.WaitForExit(250)) { }`; returns `$proc.ExitCode`. The existing
`try { ... } finally { Exit-AntiphonBuildSlot -Lease $slot }` gains, before the release,
`if ($null -ne $proc -and -not $proc.HasExited) { try { $proc.Kill($true) } catch { } }`.
`$code` is the launcher's return value; the `$LASTEXITCODE`/`$?` fallback stays only for the
D-2 call-operator branch.

Why: `ArgumentList` is the only PowerShell-reachable API that hands argv to the child
byte-for-byte on every OS. Inheriting stdio keeps the console pass-through the call operator gave,
with no pump and no reordering. The poll loop lets a Ctrl+C in an operator shell stop the
pipeline between waits so the `finally` runs; a blocking `WaitForExit()` would not.
`WorkingDirectory` is required for correctness (ground truth: PowerShell location and process
directory differ).

Rejected: redirect plus `ReadLineAsync` pump as in `run-checkpoint.ps1` (that script also
writes a phase log; the wrapper has none, and a pump is where ordering and abandoned-pipe bugs
live). `bash -c "<joined command>"` (the incident workaround: Linux-only, re-tokenised by a
second shell, and literal argv is the requirement). `--%` stop-parsing (splits on spaces, drops
quoting, Windows semantics). `Start-Process -ArgumentList` (joins into one string on Unix with
its own quoting; adds nothing over `Process`). Quoting or escaping at call sites (the wrapper
receives tokens from argv; there is no quoting left to preserve, and PowerShell has no escape
that reaches a native argv unchanged).

### D-2: literal launch for executables only; scripts and `.cmd` shims keep the call operator

Resolve `$command[0]` once with
`Get-Command -Name $command[0] -ErrorAction SilentlyContinue | Select-Object -First 1`. Use D-1
when the result is `CommandType Application` and, on Windows, its `Source` does not end in `.cmd`
or `.bat`; pass `Source` as `FileName`. Otherwise (`ExternalScript`, `Function`, `Alias`,
`Cmdlet`, a `.cmd`/`.bat` shim, or no resolution) keep today's `& $command[0] @rest`, which for a
non-native command never globs and for a missing command fails exactly as today (error, exit 1,
lease released).

Why: PowerShell globs only native commands and only off Windows, where every `Application` is a
real executable. On Windows `npm` can resolve to `npm.ps1` (CARD-0307) and a `.cmd` needs
`cmd.exe` with different quoting. Keeping the non-native branch unchanged means existing plan
rows that wrap `pwsh -File scripts/test-client.ps1 ...` or `npm run build --prefix client`
behave exactly as before on the desktop.

Rejected: routing everything through `Process.Start` (breaks `.ps1`/`.cmd` commands on Windows
for no wildcard benefit). Resolving with `which`/`where` (not portable; `Get-Command` already
knows PATH and PATHEXT).

### D-3: the `C589_COMMAND_SHIM` seam uses the production launcher

When `C589_COMMAND_SHIM` is set, the launcher gets `FileName = (Get-Command pwsh).Source` and
tokens `-NoProfile -NonInteractive -File <shim>` followed by `$command`; nothing else differs
from a real launch. Why: the offline harness is the only automated evidence for this script (its
C# wrapper contacts no runner), so the seam must exercise the code the real branch runs.
Rejected: leaving the shim branch on the call operator (the harness would pass with the
production branch still globbing).

### D-4: the harness launches the wrapper literally, in a chosen directory, under a deadline

`Invoke-C589Wrapper` starts `pwsh -NoProfile -NonInteractive -File <wrapper> <args>` through
`ProcessStartInfo.ArgumentList` with `RedirectStandardOutput`/`RedirectStandardError`,
`WorkingDirectory` = a per-call `-WorkingDirectory` (default: repo root, as `Push-Location` gave)
and a `-DeadlineSeconds` (default 120). On deadline it kills the wrapper tree and returns
`Exit = -1` plus `TimedOut = $true`, so a case asserts instead of hanging the 300 s
`ScriptHarness` budget. Output is merged stdout then stderr, as `test-run-checkpoint.ps1` does.
An optional `-NoWait` returns the started process so a case can signal it (V-4). The command shim
additionally logs, before its `CMD` line, `CWD <pwd>`, `PID <pid>` and `ARGV <json array>`
(`ConvertTo-Json -Compress @($argv)`), so exact tokens and the child's directory are observable.
`Get-C589Order` and `Get-C589Cmd` ignore unknown prefixes, and the in-process regex in
`Test-C589_WrapperRunsUnderLease` (`^SLOT POST label=in-process .*,CMD ...,SLOT DELETE`) absorbs
lines that precede `CMD`, which is why the new lines are written before it.

Why: without this the harness expands the wildcard itself and a red run at the baseline cannot
finish. Rejected: a `bash`-launched harness (the Windows nightly runs this harness) and per-case
`timeout` shell wrappers (not portable; the C# wrapper already owns the outer budget).

### D-5: red-first cases, named for the card, discovered alongside the C589 ones

New harness functions are `Test-C800_*` with `PASS C800 ...` rows; the harness's full run
appends `Get-C487CaseFunctions -Prefix 'C800_'` (pattern: `test-run-checkpoint.ps1:797`), and
`BuildSlotScriptTests` adds `[Test]` methods calling
`ScriptHarness.RunHarnessCaseAsync("test-build-slot.ps1", "C800", ...)` through a
`RunC800CaseAsync` helper. `$script:C589ExpectedRows` becomes `35 + 11` plus 3 on Linux (the
interrupt case runs only there; on Windows it prints one `SKIP` line and adds no rows). The class
stays `[Category("Integration")]` with `[ParallelLimiter<ProcessSpawnLimit>]`. No roster change:
the class is not in `tests/linux-test-roster.json` (a CARD-0590 Docker-stack roster), and the
checkpoint tool does not read that file.

Cases (row names are the harness's `PASS` names; each C# method lists them as required rows):

1. `Test-C800_WrapperPassesWildcardArgvLiterally` (6 rows). Fixture: case root holding
   `a/b/Unit`, `run.trx`, `old.trx`. Wrapper args
   `-Label c800-argv -- dotnet run --project tests/X --no-build -- --treenode-filter */*/*[Category=Unit] --report-trx-filename *.trx`,
   `C589_COMMAND_EXIT=3`, `-WorkingDirectory <case root>`. Rows:
   `C800 WrapperPassesWildcardArgvLiterally propagates exit 3`;
   `... runs the command between grant and release` (`POST,CMD,DELETE`);
   `... every token arrives literally` (`ARGV` equals the exact nine-token JSON array, no
   `-maxcpucount` because it is `dotnet run`);
   `... the child runs in the caller's directory` (`CWD` = case root).
   In-process sub-case: `Push-Location <case root>/inproc` (a directory that is not the harness
   process's start directory) then
   `& $script:Wrapper -Label c800-inproc -- dotnet build tests/X *.trx` with `run.trx` present
   in `inproc`: `... an in-process call passes the token literally` (`ARGV` holds `*.trx` and
   `-maxcpucount:3`); `... an in-process call runs the child at the pushed location`
   (`CWD` = `inproc`). Red at the baseline on Linux: the first `ARGV` holds `a/b/Unit` and
   `old.trx run.trx`; the in-process `ARGV` holds `run.trx`. Green on Windows at both commits.
2. `Test-C800_WrapperStartsUnitFilterWithinDeadline` (3 rows). Wrapper args from the incident:
   `-Label c800-unit -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c800/ -- --treenode-filter /*/*/*/*[Category=Unit] --report-trx --report-trx-filename run.trx --results-directory <case root>`
   with `-DeadlineSeconds 30`. Rows: `... exits within 30 s` (`TimedOut` false, `Exit` 0);
   `... passes the Unit filter literally` (an `ARGV` element equals `/*/*/*/*[Category=Unit]`);
   `... releases the lease` (`POST,CMD,DELETE`). Red at the baseline on Linux: the scan exceeds
   250 s, so the harness kills the wrapper at 30 s with no `CMD` and no `SLOT DELETE` line. Green
   after S2 in about two seconds. The 30 s is the assertion, not a tolerance: a green run needs
   well under 5 s, and the value is never widened.
3. `Test-C800_WrapperKeepsScriptCommandsInProcess` (2 rows). `C589_COMMAND_SHIM` unset; wrapper
   args `-NoSlot -- scripts/fixtures/c589-command-shim.ps1 --treenode-filter *.trx` from a case
   root holding `run.trx`, `C589_COMMAND_EXIT=6`, `C589_SLOT_LOG` pointing at the case log. Rows:
   `... propagates exit 6 from a script command`;
   `... a script command receives its tokens unchanged` (`ARGV` = `["--treenode-filter","*.trx"]`,
   because a PowerShell script is never globbed). Green at the baseline; it guards the D-2
   branch and goes red when everything is routed through `Process.Start` (PC-3).
4. `Test-C800_WrapperInterruptKillsChildAndReleasesLease` (3 rows, Linux only).
   `C589_COMMAND_SLEEP_SECONDS=20`; the wrapper is started through the D-4 launcher with
   `-NoWait`; the case polls the log for the `PID` line (10 s cap), then runs
   `/bin/kill -INT <wrapper pid>`. Rows: `... the wrapper exits within 5 s of SIGINT`;
   `... the child is gone within 5 s` (`Test-Path /proc/<child pid>` false);
   `... the lease is released after the command` (`SLOT DELETE` after `CMD`). The case kills the
   wrapper tree after its assertions so a red run does not linger. Red at the baseline: the
   child survives and the wrapper waits the full 20 s. On Windows the function writes
   `SKIP C800 WrapperInterruptKillsChildAndReleasesLease (Linux only)` and returns; the C# method
   returns early unless `OperatingSystem.IsLinux()` (the `RequireLinux` shape in
   `tests/Antiphon.PtyHost.Tests/LinuxPtyHostLauncherTests.cs:20`).

Rejected: a timing assertion alone for case 1 (host-dependent; a small filesystem scans in under
a second). Asserting on the joined `CMD` line (spaces inside tokens are invisible there). A new
test class (would need a lane declaration for nothing).

### D-6: inherit stdout and stderr; no redirection in the wrapper

See D-1. The `BUILD SLOT` lines come from `Write-Host` in the wrapper process and the child
writes to the same descriptors; ground truth shows the order is preserved through a pipe, which
is how both the delegate transcript and `ScriptHarness` capture it.

### D-7: documentation

`docs/testing-and-build.md`, section "Build slots (CARD-0589)", after the paragraph that starts
"It adds the grant's `-maxcpucount:N`": one paragraph stating that the wrapper starts an
executable command through `ProcessStartInfo.ArgumentList` in the caller's current directory
with inherited stdio, so wildcard tokens (`/*/*/*/*[Category=Unit]`) arrive literally on Linux,
where the call operator scans the filesystem for minutes and expands a matching pattern
(CARD-0800); that a script or `.cmd` command still runs through the call operator; that Ctrl+C
kills the child tree before the lease is released; that the shim seam logs `CWD`, `PID` and
`ARGV`; and that `scripts/test-client.ps1` still forwards through the call operator, so a vitest
pattern that matches a file in `client/` would expand there (none does today). Every phrase
`CheckpointManifestDocumentationTests.the_doc_has_the_build_slots_section` requires stays. No
AGENTS.md or bundle change: the gate instruction is unchanged and the `bash -c` workaround was
never documented.

### D-8: scope for the Code stage

`-Scope "ops,docs,tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs"` (`ops` is
`scripts/**`, `docs` is `docs/**` with weight allow, from `antiphon.areas.json`; the test file is
in no area). A Code task already touching `scripts/build-slot.ps1`, `scripts/lib/build-slot.ps1`,
`scripts/run-checkpoint.ps1` or `tools/Antiphon.Checkpoints` defers this one.

### D-9: lane

All rows: Linux, server2, through the checkpoint tool. No `-Runner`, no `-Platform`. Reason:
the defect exists only where PowerShell globs native arguments (Unix), the runner defaults
already place work on server2, and the tool's bootstrap build goes through `build-slot.ps1` with
no wildcard token. Until S2 exists, a Code delegate on Linux must not put a wildcard filter
through `build-slot.ps1` by hand; the checkpoint tool and `run-checkpoint.ps1` are already
literal.

| Row | Lane | Why |
|---|---|---|
| CP-1, CP-2, CP-3 | Linux (server2) only | the red exists only where PowerShell globs native argv |
| CP-4, CP-5, CP-6 | Linux (server2); Windows would also pass but is not required | platform-neutral assertions; the Windows nightly picks the class up unchanged |

## Slices

### S1: red-first (tests only)

Files: `scripts/test-build-slot.ps1` (D-4 launcher with `-WorkingDirectory`, `-DeadlineSeconds`,
`-NoWait`; the four `Test-C800_*` functions; `C800_` discovery; expected rows),
`scripts/fixtures/c589-command-shim.ps1` (`CWD`, `PID`, `ARGV` lines before `CMD`),
`tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs` (four `[Test]` methods,
`RunC800CaseAsync`). All three files stay ASCII-only (`Test-C589_WrapperAsciiOnly`).
Commit: `test(CARD-0800): pin literal wildcard argv through build-slot.ps1 red-first`.

Acceptance: CP-1, CP-2 and CP-3 red with the named failures; CP-4 green. The existing
`C589_WrapperMaxCpuCountRules` is expected slow at S1 on Linux (its `/*/*/A/*` sub-case still
scans inside the baseline wrapper) and is not a checkpoint row until CP-5.

### S2: fix and doc

Files: `scripts/build-slot.ps1` (D-1, D-2, D-3, D-6; header comment updated; ASCII only),
`docs/testing-and-build.md` (D-7).
Commit: `fix(CARD-0800): start build-slot commands with literal argv`.

Acceptance: CP-5 and CP-6 green; the header of `build-slot.ps1` still describes exit codes 2 and
4 and the `C589_COMMAND_SHIM` seam; `scripts/lib/build-slot.ps1` is untouched.

## Verification design

No build or test ran during Plan; the probes above used `pwsh` and `/usr/bin/printf` directly.
`BuildSlotScriptTests` has 8 `[Test]` methods at the baseline (seven `C589_*` plus
`Wrapper_renews_a_renew_mode_grant_while_the_command_runs`); S1 adds four, so a class run is
**12 results**. `CheckpointManifestDocumentationTests` has 7 methods.

| ID | Evidence |
|---|---|
| V-1 | `C800_WrapperPassesWildcardArgvLiterally`: exact argv and working directory through the shim seam, from a fresh process and in-process. |
| V-2 | `C800_WrapperStartsUnitFilterWithinDeadline`: the incident command starts and finishes inside 30 s with the Unit filter literal. |
| V-3 | `C800_WrapperKeepsScriptCommandsInProcess`: a `.ps1` command still runs in-process with its exit code and tokens. |
| V-4 | `C800_WrapperInterruptKillsChildAndReleasesLease` (Linux): SIGINT ends the child and the lease is released. |
| V-5 | `CheckpointManifestDocumentationTests.the_doc_has_the_build_slots_section` still finds every required phrase after D-7. |
| R-1 | The eight existing methods (lease order, `-maxcpucount` rules, failure release, timeout exit 4, unreachable fallback, ASCII, renew) pass unchanged through the new launcher. |
| R-2 | The `wrapper-run` sub-case (`/*/*/A/*`) of `C589_WrapperMaxCpuCountRules` completes in seconds on Linux, which is why CP-5 fits its `EstimatedMinutes`. |
| R-3 | Review reads `build-slot.ps1` for: `WorkingDirectory` set from `Get-Location`, no redirection, `Kill($true)` before `Exit-AntiphonBuildSlot`, the D-2 resolution rule, the shim branch on the same launcher, and no change to `scripts/lib/build-slot.ps1`. |
| V-6 | `C800_WrapperLaunchesNativeExecutableLiterally`: with the shim seam unset, a real `pwsh` executable receives matching wildcard tokens and the fixture working directory literally. |
| R-4 | The full `BuildSlotEndToEndTests` class passes against its isolated broker. |
| R-5 | The complete `Antiphon.Tests` Unit lane passes. |

### Positive controls for the later Mutation stage (method-scoped, Linux)

- PC-1: in `build-slot.ps1` make the D-2 decision always take the call operator. Run
  `/*/*/BuildSlotScriptTests/C800_WrapperLaunchesNativeExecutableLiterally`: red on the native
  `ARGV` row (`*.trx` expands to `old.trx run.trx`; `?` matches the fixture's `x`). Restore,
  rerun, green.
- PC-2: remove the `Kill($true)` in the `finally`. Run
  `/*/*/BuildSlotScriptTests/C800_WrapperInterruptKillsChildAndReleasesLease`: red on
  "the child is gone within 5 s". Restore, green.
- PC-3: route `ExternalScript` commands through `Process.Start` too. Run
  `/*/*/BuildSlotScriptTests/C800_WrapperKeepsScriptCommandsInProcess`: red (start failure,
  exit not 6). Restore, green.
- PC-4: drop the `WorkingDirectory` assignment. Run
  `/*/*/BuildSlotScriptTests/C800_WrapperPassesWildcardArgvLiterally`: red on
  "an in-process call runs the child at the pushed location". Restore, green.

Code does not run these; Mutation records its own leased, method-scoped red/restore runs.

### Execution

One checkpoint run per committed slice, through the CARD-0723 tool. Bootstrap the tool under
its own slot and release that slot before `run`; each row leases its own driver.

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c800-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c800-tool/ --nologo
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c800-tool/ -- run --plan docs/superpowers/plans/2026-09-29-card-0800-build-slot-literal-argv-plan.md --after S1 --max-wait 50s
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c800-tool/ -- run --plan docs/superpowers/plans/2026-09-29-card-0800-build-slot-literal-argv-plan.md --rows CP-5,CP-6 --max-wait 50s
```

The first run selects CP-1 to CP-4 (their `After` is `S1`). The second names CP-5 and CP-6
explicitly so the deliberately red rows are not rerun after the fix; their methods are
re-verified green inside CP-5. After exit 75, call `wait <run-id> --max-wait 50s` until
terminal; never end the turn while a run is live. CP-1 to CP-3 are expected red at S1 and are
reported as such with their `FAILED` lines, not rerun; a red CP-4, CP-5 or CP-6 is fixed and
rerun as the same row. Report every `CHECKPOINT` line with executed/passed/failed/skipped and
the TRX path. Delete every `bin-c800*` directory before finishing (a red run keeps its
outputs; remove them once the report is written).

### Cost

Ordinary Code floor: 6 + 2 + 2 + 1 + 8 + 1 = **20 minutes** (two isolated builds of
`tests/Antiphon.Tests`, measured at about 4 minutes each on server2 under a slot, CARD-0738).
Authoring: 40-60 minutes. Code `ExpectAbout`: 75-90 minutes. Estimates allow for host
contention and are not measured timings. No open decisions or external prerequisites.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c800/` | argv-literal-red | `/*/*/BuildSlotScriptTests/C800_WrapperPassesWildcardArgvLiterally` | V-1 (red) | 1 executed, 1 failed; the failure names `a/b/Unit` and `old.trx run.trx` in `ARGV`; not rerun | 1 | 6 | true |
| CP-2 | S1 | CP-1 | unit-filter-red | `/*/*/BuildSlotScriptTests/C800_WrapperStartsUnitFilterWithinDeadline` | V-2 (red) | 1 executed, 1 failed; the failure names the 30 s deadline with no `CMD` line; not rerun | 1 | 2 | true |
| CP-3 | S1 | CP-1 | interrupt-red | `/*/*/BuildSlotScriptTests/C800_WrapperInterruptKillsChildAndReleasesLease` | V-4 (red) | 1 executed, 1 failed; the failure names the surviving child pid; not rerun | 1 | 2 | true |
| CP-4 | S1 | CP-1 | script-inprocess | `/*/*/BuildSlotScriptTests/C800_WrapperKeepsScriptCommandsInProcess` | V-3 | 1 executed, 0 failed | 1 | 1 | true |
| CP-5 | S1-S2 | `tests/Antiphon.Tests -> bin-c800s2/` | wrapper-green | `/*/*/BuildSlotScriptTests/*` | V-1, V-2, V-3, V-4, R-1, R-2 | all 13 results, 0 failed, 0 skipped | 13 | 8 | true |
| CP-6 | S1-S2 | CP-5 | doc-contract | `/*/*/CheckpointManifestDocumentationTests/*` | V-5 | all 7 results, 0 failed | 7 | 1 | true |
| CP-7 | S1-S2 | CP-5 | native-branch | `/*/*/BuildSlotScriptTests/C800_WrapperLaunchesNativeExecutableLiterally` | V-6, PC-1 target | 1 executed, 0 failed; native argv and CWD literal | 1 | 1 | true |
| CP-8 | S1-S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c800runner/` | broker-integration | `/*/*/BuildSlotEndToEndTests/*` | R-4 | 2 executed, 0 failed | 2 | 5 | true |
| CP-9 | S1-S2 | CP-5 | unit-lane | `/*/*/*/*[Category=Unit]` | R-5 | whole Unit lane, 0 failed | 1 | 8 | true |
