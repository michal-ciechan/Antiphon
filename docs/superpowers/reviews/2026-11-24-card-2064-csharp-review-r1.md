# CARD-2064 Review round 1 (C# stack checks): Clean

- Subject: `origin/feat/card-task-cae8bb4f` tip `650e778cd12cdad34d424978ea045b70ab07c37e` (Code task cae8bb4f, Codex High).
- Range reviewed: `a8982a509d823ffe1d8639838747ea6dc3f42bc6..650e778cd12cdad34d424978ea045b70ab07c37e` (3 commits).
- Reviewer: Review task 69d20d94 (Claude Opus 5.5), server2 runner mirror, Linux. Nothing ran on the desktop runner.
- Verdict: **Clean**. No blockers and no regressions.

## What was checked

- **Scope.** The diff changes only `scripts/lib/Antiphon.Nightly/{BootstrapCheck,DevStackVerify,DevStack,NightlyHost,DevPostgresCheck}.cs`,
  the project file (Npgsql 9.0.3 and Microsoft.Extensions.Configuration.Json 9.0.21) and the two test classes. Nothing outside that area changed.
- **Skip flag.** `ANTIPHON_SKIP_DEV_POSTGRES` is read from the process environment first, then from the Windows User environment
  (`NightlyHost.UserVariables`, null on other OSes). It uses the same `DevCompose.Skipped` rule (`Trim() == "1"`) as dev-aspire and the
  autostart. `DevStack` passes `UserVariables` to the verify-dev-stack phase that bootstrap-check runs inside.
- **Flag unset.** The original docker inspect, volume, pg_isready and TCP 127.0.0.1:17280 code runs unchanged in the else branches.
  Remedies still name compose. The non-skip witnesses and the pre-existing tests assert this.
- **Skip path.** The container, volume, localhost:17280 and Port 17280 rows report SKIP, which counts as neither a pass nor a failure.
  In verify-dev-stack, `Ok == false` alone counts as a failure. The skip path names no compose remedy.
- **Fail closed.** `DevPostgresCheck.Probe` returns FAIL in these cases: no connection string, an empty host, multiple hosts, a
  Unix-socket host, an invalid port, a parser or file exception, and a TCP connect that is closed or times out after 2 s.
  The connection string is resolved in the same order as the AppHost's `AddConnectionString("DefaultConnection")`:
  - the `ConnectionStrings__DefaultConnection` environment variable, or its `:` spelling;
  - user secrets (`aspire-antiphon-apphost`, or `antiphon-server` in simple mode);
  - `appsettings.Development.json`;
  - `appsettings.json`.
- **No secret echoed.** The detail shows only host:port or a fixed message. Exception messages are not printed. Tests assert that the
  fixture passwords never appear in the output.

## Evidence (this review, at 650e778cd)

- Build: `dotnet build tests/Antiphon.Tests` through build-slot, 0 errors.
- Tests: `/*/Antiphon.Tests.Scripts/(BootstrapCheckTests)|(VerifyDevStackSmokeTests)/*` gave total 36, failed 0, skipped 0. This matches
  the author's count.
- Red then green. At the witness commits 2b908191f and 3b322ca91 the tests failed to compile, because `NightlyHost.UserVariables` only
  exists after the fix. That compile failure is the only red they showed. A fail-open mutant of `DevPostgresCheck.Probe` (Ok=true for an
  invalid endpoint and for a closed port) then ran against the `C2064*` filter: 2 of 7 failed, exactly
  `C2064_Verify_fails_closed_on_an_invalid_override_without_disclosing_it` and
  `C2064_Bootstrap_fails_when_the_configured_database_is_unreachable`. The mutant was reverted with `git checkout`.
- Loading in PowerShell. `nightly-core.ps1` runs `Add-Type` on every dll in the core output. A Release build with
  `CopyLocalLockFileAssemblies=true` produced 16 dependency dlls, including Npgsql and Microsoft.Extensions.*. All 16 and the core
  loaded in pwsh 7.5.4 in sorted order, using the same loop.
- `git merge-tree --write-tree origin/master(afbe900a1) HEAD` gives no conflicts.
- `scripts/antiphon evidence-diff --base a8982a509` gives commits=3, entries=0, violations=0.

## Advisories (not blocking)

- A-1. The skip flag falls back to the User environment, but the connection string does not. A User-only
  `ConnectionStrings__DefaultConnection` in a shell started before it was set falls through to the secrets and settings files. This
  fails closed, so it is not fail-open.
- A-2. `Probe` catches a fixed list of exception types. Any other exception would escape with its message. No such path is known.
- A-3. The card's desktop acceptance is still pending: running `verify-dev-stack.ps1 -SkipBrowser` and `bootstrap-check.ps1` on the
  desktop, once with the variable set and once unset. This lane could not run it because the desktop slot was held.
