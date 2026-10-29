# Review 186b4dc1: harness post-exit drain bound and git gc hardening (Group 123)

Outcome: 2 defects. The narrow runs are green on Linux (80 tests: 79 passed, 1 expected Windows-only skip, 0 failed, no leaked processes). But the drain drops the execution deadline, and the contract test that guarded that deadline was rewritten so it no longer checks it (D-1). The git hardening also cannot remove the Windows pipe holder that was named as the cause (D-2).

Reviewed: subject 0889258e-b51d-469d-8d2a-0dbcfc49be6d, code tip afb64ae704391c1413c23006a8e15dc4225b0d75 (red witness 30ebffb66, report tip 9774f683fe4e5f9c150ba937daeabb2c4fe4ccad), base rc/2026-10-27 fffa9e954e9dfe6685a4e6793ae0ae543688fcb8. Scope: **Interim**. Only the brief's named narrow classes ran, on Linux. The whole Unit lane did not run, nothing ran on Windows, and the witness was not re-run at base.

## Defects

### D-1: the drain no longer enforces the execution deadline, and its guard test was weakened
- **Where:** `tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs:92-93`. While draining, the wait token links only `callerToken` and `postExit`; `execution.Token` is left out. The rewritten test is at `tests/Antiphon.Tests/Scripts/ScriptHarnessProcessContractTests.cs:90-109`.
- **Failure:** when the root exits near the end of the execution budget, the harness still waits the full drain budget past that deadline: up to 300 s + 10 s in production, then cleanup. The base test `Root_exit_does_not_reset_deadline` would go red on the fix. Under the base test (1 s budget, root completes at 800 ms, clock advanced to 1000 ms), no timer fires at 1000 ms because the drain timer is set for 1800 ms. `Capture`'s 3 s real wait then returns null and `ShouldBeOfType<TimeoutException>` fails. The author changed the test to a 30 s execution budget with a 200 ms drain, so it now proves only the drain. It no longer asserts the invariant its name states.
- **Why:** a contract assertion was weakened to pass a behaviour change, and the deadline guarantee "root exit does not reset the deadline" is no longer true.
- **Fix:** during the drain, link `callerToken`, `execution.Token` and `postExit.Token`, so the effective limit is the smaller of the remaining execution budget and the drain budget. Keep the execution-budget timeout message when the execution budget expires first. Restore the base body of `Root_exit_does_not_reset_deadline` verbatim, and keep the new drain assertion as a separate test.

### D-2: the git hardening does not remove the Windows holder; the hardening is incomplete
- **Where:** `scripts/lib/CheckpointProcessRunner.cs:105`, `scripts/lib/release-authority.ps1:100`. Both commits claim these fix the native Windows C1313_BaselineAuthority hang.
- **Failure:** a detached git auto-gc or auto-maintenance child cannot be what held the harness pipes:
  1. **Measured on this host (git 2.47.3):** in a scratch repo, `git -c gc.auto=1 -c gc.autoDetach=true gc --auto | cat` printed "Auto packing the repository in background". The caller's pipe reached EOF after 16 ms, and the background gc then packed 120 objects. git's `daemonize()` closes fds 0, 1 and 2 before the child keeps running, so a detached gc does not keep inherited std pipes.
  2. **Windows:** Git for Windows cannot daemonize. gc.autoDetach applies only "if the system supports it", and `NO_POSIX_GOODIES` is set on MINGW, so `daemonize()` returns ENOSYS. Auto gc and maintenance then run in the foreground and end before the git command returns.
  3. **Fixture size:** the C1313 fixture repos (a few commits, one pack per clone) never reach the gc.auto threshold of 6700 loose objects anyway.

  So afb64ae70 turns the Windows 300 s timeout into a failure after about 10 s that names the job members. C1313_BaselineAuthority stays red on Windows whenever the real holder recurs.
- **Why:** the shipped claim that C1313 is fixed rests on a cause that cannot happen on Windows. There is no Windows run at afb64ae70.
- **Likely real exposure (not proven):** on Windows, .NET `Process.Start` called from the root pwsh creates the child with `bInheritHandles=TRUE` and no handle list. Every child then inherits pwsh's inheritable std handles, which are the harness pipes. That covers `Invoke-TestSelectionGit` → CheckpointProcessRunner, `Invoke-ReleaseAuthorityGit`, `dotnet FilterHost.dll --list-tests` in `Get-FxDiscovery` (`scripts/test-change-aware-release.ps1:108`), and `Invoke-NightlyExactRoster`. Any descendant of those that outlives the root (a test-host child, an MSBuild or VBCSCompiler node, a process that survived a kill) keeps EOF from arriving.
- **fsmonitor:** HardenGit already passes `core.fsmonitor=false`. `Invoke-ReleaseAuthorityGit` and `Invoke-NightlyGit` (`scripts/lib/nightly-common.ps1:517`, native git, no hardening at all) do not pass it, and they do not set `GIT_CONFIG_NOSYSTEM`. A desktop system gitconfig with `core.fsmonitor=true` (an option in the Git for Windows installer) therefore starts one long-lived `git fsmonitor--daemon` per fixture repo inside the harness job. That daemon starts with `no_stdin/no_stdout/no_stderr`, and Git for Windows restricts handle inheritance, so it is an unlikely pipe holder. It does stay a job member and can keep fixture directory handles open, which would break owned-path deletion.
- **Fix:**
  1. Run `ChangeAwareReleaseTests.C1313_BaselineAuthority` on the desktop at afb64ae70 and read `holder=<pid:image>`. The fix's new diagnostic exists for exactly this.
  2. Fix the holder that run identifies.
  3. Add `core.fsmonitor=false` to `Invoke-ReleaseAuthorityGit` and to `Invoke-NightlyGit`/`Get-NightlyOriginUrl`.
  4. Do not claim the Windows C1313 fix until a Windows green run exists.

  The gc and maintenance flags are harmless and can stay.

## Advisories
- **A-1 flake margin:** the production drain is 10 s (CleanupBudget). Once the root exits, at most one pipe buffer is left to read, so a healthy drain normally takes milliseconds. But a host stall of 10 s or more between root exit and the final EOF read now fails a healthy case that base would have passed, and this Windows run saw ordinary stalls of 20-40 s. Consider a production drain floor of about 30 s, still 10x under 300 s. The witness bound (`ScriptHarnessProcessTests.cs`, elapsed under 12 s) also includes pwsh startup, which is tight on a loaded Windows host.
- **A-2:** the contract test synchronises with a real `await Task.Delay(100)` before advancing the FakeTimeProvider (`ScriptHarnessProcessContractTests.cs:103`). If the drain timer is not armed within 100 ms on a loaded host, the advance is lost and the test fails at the 3 s Capture. Wait on an observable instead, or advance in a loop.
- **A-3:** on Windows, `DescribePipeHolders` lists every job member, not the processes that actually hold the pipes. A holder outside the job shows as "unidentified". The Windows code path (`JobProcessIds`, `QueryFullProcessImageNameW`, offset 8 of `JOBOBJECT_BASIC_PROCESS_ID_LIST`) is correct on reading but was not executed. On Linux, the scan of all of /proc runs synchronously before Terminate, outside the cleanup budget; it only runs on the failure path.
- **Checked clean:**
  - Output is not lost: the diagnostic clips stdout and stderr only after Terminate and the cleanup pump drain.
  - The holder is only described, never killed. Killing is still the owned Job or Linux-tree termination, so no unrelated process is killed.
  - The `CheckpointVerifierBoundsTests` change strengthens the check: it requires two more settings.
  - The `-c` values are passed as ArgumentList elements with no shell, so there is no injection risk.
  - `gc.auto=0` on the checkpoint and verifier read gits and on the release-authority git changes nothing that matters. A long-lived release clone simply does not auto-gc through these callers.
  - Census and roster hashes are unaffected (C1410, C1404, C1577, C1584, G2865 and RcRosterRepair are green).

## Runs
One build-slot build (`dotnet build tests/Antiphon.Tests --property:OutputPath=bin-rv186/ --property:UseAppHost=false`, exit 0, 0 errors). Then one build-slot `dotnet exec --treenode-filter "/*/*/(ScriptHarnessProcessContractTests*)|(ScriptHarnessProcessTests*)|(ScriptHarnessLinuxOwnershipTests*)|(CheckpointVerifierBoundsTests*)|(DirectoryLinkFixtureTests*)|(WindowsUnitPlatformAdmissionTests*)|(RcRosterRepairTests*)|(C1514LandingSplitBudgetTests*)/*"` at afb64ae70: total 80, 79 passed, 1 skipped, 0 failed, 6m05s.

Per class:

| Class | Tests | Note |
|---|---|---|
| ScriptHarnessProcessContractTests | 27 | |
| ScriptHarnessProcessTests | 12 | Printed_pass witness 3.9 s |
| ScriptHarnessLinuxOwnershipTests | 21 | |
| CheckpointVerifierBoundsTests | 2 | C1233_VerifierGit temp/orphan |
| DirectoryLinkFixtureTests | 5 | |
| WindowsUnitPlatformAdmissionTests | 5 | 4 passed, including C1210_Roster_closure_matches_source_gates; 1 skipped, C1210_Windows_linux_only_outcomes_are_exact (Windows-only) |
| RcRosterRepairTests | 3 | |
| C1514LandingSplitBudgetTests | 5 | C1404, C1577, C1584, C1410, G2865 |

Other checks:
- **Processes:** a ps before/after diff showed no leftover script-harness or C806 processes, and no c806 result or control dirs remain in /tmp.
- **Evidence diff:** `check-evidence-diff.ps1` over fffa9e954..9774f683f found 3 commits, 0 violations.
- **rc:** merge-tree against rc/2026-10-27 is clean.
- **master:** whole-branch merge-tree conflicts only on rc-vs-master files outside this card (.antiphon/task-af2b78c8.md, docs/investigations/2026-10-25-card-1584...). Simulated per-commit picks of 30ebffb66 and then afb64ae70 onto origin/master merge clean.

Not run: the witness at base, C1313_BaselineAuthority itself, anything on Windows, and the whole Unit lane.
