# CARD-1199 repair 13i reader scope

Repair 13j qualification: the admitted readers added or changed by CARD-1199 are the regular-file readers, the project XML preflight, the four BuildConfinement/run-checkpoint Git/graph readers (CP-21) and SafeDelete.RunGit (CP-22, with the CP-1/CP-7/CP-8 contract cases); each named site meets its stated bound. Repair 13i left SafeDelete.RunGit as a CARD-added exception (separate exit and drain budgets, configured fsmonitor executed, descendant survivable); repair 13j routed it through the shared runner. The following pre-existing readers are not bounded. This means the admitted content/Git/graph readers, with the diagnostic, staging and execution-loader exclusions below. It is not a certificate for every repository operation. Lines refer to d13387548b88ac3a6b37dc046370f40d880cae32, so the inventory remains stable after repair.

Comparison: merge-base with fetched origin/master is 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2. git diff merge-base..d13387548 and git blame identify the reader expressions, rather than treating a file with other changes as a wholly new reader. The six inherited expressions below are identical on master. Provenance and native outcomes are retained in .antiphon/c1199r13i/reader-origin.txt and the Final Review evidence /work/review-evidence/task-9f9613bd/.

| Reader | Origin | Reproduction / limitation |
| --- | --- | --- |
| scripts/verify-checkpoint-temp-usage.ps1:22 | ca876f92cd, unchanged | Get-FileHash of repository-derived DLL blocks opening a FIFO; ordinary hash passes, five-second external watchdog kills and awaits the child. |
| scripts/verify-checkpoint-temp-usage.ps1:109 | ca876f92cd, unchanged | Same after-run DLL hash: ordinary passes, FIFO exceeds five seconds. |
| scripts/test-run-checkpoint.ps1:353 | 00fdfed4a7, unchanged | ASCII check reads the repository script path; ordinary passes, FIFO exceeds five seconds. This is not a known-created fixture. |
| scripts/test-run-checkpoint.ps1:356 | 00fdfed4a7, unchanged | ASCII check reads its own repository harness path; ordinary passes, FIFO exceeds five seconds. |
| tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:198-206 | 31411ea310, unchanged | RunGit bounds direct-child exit at 30 seconds but not subsequent output drain. An exited child leaving a descendant holding stderr exceeds the 35-second watchdog; installed Git with core.fsmonitor hook reproduces. |
| scripts/lib/checkpoint-source.ps1:19-26 | 31411ea310, unchanged | Get-CheckpointGitBytes has the same process-only 30-second deadline and unbounded drain; held-pipe and installed-Git controls exceed 35 seconds. |
| scripts/verify-checkpoint-temp-usage.ps1:17 | ca876f92cd, unchanged (added to this list by the repair 13j sweep) | `& git -C $repo rev-parse HEAD` call operator with no local deadline or Git hardening. Not natively reproduced in repair 13j; listed by construction, not by measurement. |

Fixed admitted readers: scripts/run-checkpoint.ps1:335 (new project XML reader, 2c531276f0), :133-141 (new Git reader, 8017666882), :759-767 (new graph reader, c761d418b5), tools/Antiphon.Checkpoints/BuildConfinement.cs:604-616/:410-421 (new Git/graph readers, c761d418b5), and, from repair 13j, tools/Antiphon.Checkpoints/Cleanup/SafeDelete.cs RunGit (CARD-added ac568bf53c/8017666882; callers PreflightRoots, GitRefusal, SafeWrite.Preflight/Refusal). The project reader uses Read-CheckpointText and parses memory; the five process readers use the same CheckpointProcessRunner compiled for the tool and loaded by PowerShell. Its deadline includes config inspection, process exit and both output drains. Expiry kills the owned Linux process group and abandons pending drains. Windows attempts job assignment and tree/job termination; its pre-assignment child race remains unqualified under R-2. Git overrides fsmonitor/hooks, optional locks, replace refs, external diff, global/system config and named clean/smudge/process/textconv drivers. Linux FIFO/held-pipe cases have ordinary controls and hard child watchdogs; Windows remains pending R-2.

Accepted exclusions retained from Review: fixed kernel metadata (/proc identity and host metrics), legacy human diagnostic GitSnapshot/HostSnapshot collectors, executing-tool staging via SafeWrite.CopyOverwrite/File.Copy and ShadowCopy.CopyFiles, script/assembly execution loading, known-created test fixture content, and checkpoint-task publication helpers. CopyToolOutput is a callback property, not a reader. These staging/diagnostic paths are not certified as bounded. The legacy verify-checkpoint-temp-usage output stream loop and standalone run-checkpoint Invoke-Dotnet line loop have no local deadline; an enclosing caller may bound them, but this document does not claim they do.

Whitespace identity scope: tool import/coverage and strict receipt consumers refuse null/empty/whitespace row/build IDs, as CP-17/CP-19 prove. Script run-checkpoint -Name retains PowerShell's empty-string binding refusal and admits whitespace-only names; its composite Name@Project broker label is nonblank. This parameter and behavior pre-exist unchanged on master. Review's real broker control was granted and released after a 180-second wait (whitespace-label.log), disproving a broker whitespace-name guard. Legacy script validation also has no universal whitespace build-ID guard. No inherited identity behavior is changed by this repair.

## Repair 13j process-launch sweep

The Process.Start, ProcessStartInfo, new Process and Git/dotnet call-operator sites repair 13j found in tools/Antiphon.Checkpoints, scripts/run-checkpoint.ps1, scripts/lib/checkpoint-*.ps1, the checkpoint verification scripts and tests/Antiphon.Tests/Checkpoints, at the repair 13j source. This list was not complete (Review 6b8368dc F2): it omitted scripts/verify-checkpoint-orphan-usage.ps1, scripts/test-run-checkpoint.ps1 and the BaselineComparer/GitWorktree Git requests. The repair 13k census below adds them and states the command that produced it. Origin is git blame against merge-base 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2. "Shared runner" means CheckpointProcessRunner with one deadline over config inspection, exit and both drains and Linux process-group kill; it is proved per site only by the named rows.

| Site | Origin | Class | Bound / proof |
| --- | --- | --- | --- |
| tools/Antiphon.Checkpoints/Cleanup/SafeDelete.cs RunGit | CARD-1199 | shared runner (repair 13j; was bypass F1) | 15 s or caller timeout; CP-22 fsmonitor and exited-parent cases, CP-1/CP-7/CP-8 unavailable/failed/timeout contract |
| tools/Antiphon.Checkpoints/BuildConfinement.cs Git (ProcessStartInfo "git") | CARD-1199 | shared runner | 30 s; CP-21 tool-git |
| tools/Antiphon.Checkpoints/BuildConfinement.cs Evaluate (ProcessStartInfo "dotnet") | CARD-1199 | shared runner | 120 s; CP-21 tool-eval |
| scripts/run-checkpoint.ps1 Invoke-CheckpointGit | CARD-1199 | shared runner | 15 s; CP-21 script-git |
| scripts/run-checkpoint.ps1 Invoke-CheckpointGraphEval | CARD-1199 | shared runner | 120 s; CP-21 script-eval |
| tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs RunGit | master 31411ea310 | inherited-unchanged, listed above | exit 30 s, drain unbounded |
| scripts/lib/checkpoint-source.ps1 Get-CheckpointGitBytes | master 31411ea310 | inherited-unchanged, listed above | exit 30 s, drain unbounded |
| scripts/verify-checkpoint-temp-usage.ps1:17 git rev-parse | master ca876f92cd | inherited-unchanged, listed above (new in 13j) | none |
| scripts/verify-checkpoint-temp-usage.ps1:66-81 dotnet child | master ca876f92cd/b6c2ec3d7a | accepted exclusion (legacy output loop, above) | no local deadline |
| scripts/run-checkpoint.ps1 Invoke-Dotnet | master; one CARD log-open line (a0c895c701) | accepted exclusion (standalone row/build line loop, above) | no local deadline |
| tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs SystemProcessHandle / RunAsync | master start/wait/kill-drain; CARD-1199 wraps it with build confinement | row/build execution driver, not an admitted reader | row deadline by cancellation, then master 10-s post-kill drain; CP-9 RowRunnerTests |
| tools/Antiphon.Checkpoints/Execution/WindowsJobProcessHandle.cs | master | row execution driver (Windows), not a reader | job custody; native qualification R-2 |
| tools/Antiphon.Checkpoints/Execution/DetachedLauncher.cs | master | detached executor launcher; reads no output | not a reader |
| tools/Antiphon.Checkpoints/Slots/LeaseHolder.cs | master | slot holder launcher | not a reader |
| tools/Antiphon.Checkpoints/Evidence/GitSnapshot.cs, HostSnapshot.cs | master | accepted diagnostic exclusion (above) | not certified |
| scripts/test-checkpoint-stamp.ps1, test-checkpoint-bounded-reader.ps1, test-checkpoint-bounded-readers.mjs | CARD-1199 | test harness; fixture Git/dotnet stubs in scratch roots | enclosing test child watchdog (CP-18 four seconds, CP-21 node supervisor) |
| tests/Antiphon.Tests/Checkpoints fixture launches (git init/add/commit, pwsh/dotnet/node harness children; CARD-added in CheckpointOutputConfinementTests, CheckpointShapeConflictTests, CheckpointStampSafetyTests, CheckpointTrackedBaselineTests, CheckpointOwnedOutputTests, CheckpointReceiptSafetyTests, CheckpointBoundedReaderTests, OutputCleanupTests, CheckpointTaskOwnershipTests, SafeDeleteProcessBoundaryTests) | CARD-1199 and master | test fixture children, not admitted readers | superseded by the repair 13k fixture table below. The 13j wording was wrong: seven CARD-added sites read both redirected streams synchronously before WaitForExit, so a blocked child never reached that limit (Review 6b8368dc F1). |

No bypass remains among admitted product readers in this list. The table makes no claim about launches outside these paths.

## Repair 13k census and fixture qualification

This replaces the 13j scope statement and the 13j fixture row (Review 6b8368dc F1/F2). It makes no universal claim beyond what the census command below prints. A reviewer can rerun the command at any SHA and compare the result line by line.

Census command, run from the repository root. It matches every `Process.Start(`, `[Diagnostics.Process]::Start`, `ProcessStartInfo`, `new Process`, `Start-Process`, `DriverRequest(`, `new("git"|"dotnet"`, node `spawn`/`execFile`/`execSync`, a call operator before git/dotnet/pwsh/node/setsid, a bare `git -`/`dotnet -`/`pwsh -` statement, and `FileName =`:

```sh
git ls-files -- 'tools/Antiphon.Checkpoints/*.cs' 'scripts/*checkpoint*.ps1' 'scripts/*checkpoint*.mjs' \
  'scripts/lib/*checkpoint*.ps1' 'scripts/lib/CheckpointProcessRunner.cs' 'tests/Antiphon.Tests/Checkpoints/*.cs' |
  xargs grep -nE 'Process\.Start\(|Process\]::Start|ProcessStartInfo|new Process( |\(|\{|$)|Start-Process|DriverRequest\(|new\("(git|dotnet)"|spawn(Sync)?\(|execFile|execSync|& *(git|dotnet|pwsh|node|setsid)( |$)|(^|[(=|;] *)(git|dotnet|pwsh) +-|FileName *= *'
```

Origin of each hit: `git blame -L <line>,<line> --porcelain HEAD -- <file>`, then the hit is master when `git merge-base --is-ancestor <commit> 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2` succeeds, CARD otherwise. This classifies the matched line only. A file-level `git diff --numstat 6e36b695f..HEAD -- <file>` is recorded where a master expression sits in a CARD-changed file.

Limits of the method: it finds launches spelled in these forms in these files. It does not find launches through other helpers or other files (for example DirectoryLinkHelper's `cmd.exe` is found; a launch hidden in an unlisted script is not). Strings passed to a shell (for example `git` inside a CheckpointShapeConflictTests project command) appear as hits, and the table classifies them by the launch that runs them.

The census and the 13j table give the following. Rows marked "13k" were missing from 13j.

| Site (census hits) | Origin | Class | Bound / limitation |
| --- | --- | --- | --- |
| scripts/verify-checkpoint-orphan-usage.ps1:6 `& git -C $repo rev-parse HEAD` (13k) | master; file unchanged from merge-base | inherited-unchanged | call operator, no local deadline or Git hardening; not natively reproduced |
| scripts/verify-checkpoint-orphan-usage.ps1:83/89 owner pwsh (13k) | master; file unchanged | inherited-unchanged verification launch | three-minute readiness poll, then Kill(true) and an untimed WaitForExit; async drains |
| scripts/verify-checkpoint-orphan-usage.ps1:117/120 protected `dotnet hold` child (13k) | master; file unchanged | inherited-unchanged verification launch | no output redirection; lifetime managed by the script |
| scripts/verify-checkpoint-orphan-usage.ps1:161/173 native sweeper (13k) | master; file unchanged | inherited-unchanged verification launch | 30-s WaitForExit, then Kill(true) and untimed WaitForExit; async drains awaited without a limit |
| scripts/test-run-checkpoint.ps1:140-147 and :667-696 pwsh children (13k) | master; file unchanged | inherited-unchanged legacy acceptance harness | as master; not re-audited |
| tools/Antiphon.Checkpoints/Baseline/BaselineComparer.cs:45 fetch, :54 rev-parse, :79 worktree add, :156 worktree remove; :95/:128 dotnet build/run (13k) | master expressions; file changed by CARD elsewhere (55+/10-) | baseline execution through ProcessDriver | ProcessDriver row deadline by cancellation, then master post-kill drain; no Git hardening |
| tools/Antiphon.Checkpoints/Baseline/GitWorktree.cs:7/:10 request factories (13k) | master; file unchanged | baseline execution through ProcessDriver | as above |
| tools/Antiphon.Checkpoints/Execution/RowRunner.cs:119/174/223/282, RunScheduler.cs:386 DriverRequest | master expressions; files changed by CARD (build confinement) | row/build execution through ProcessDriver | row deadline; CP-9 RowRunnerTests |
| tools/Antiphon.Checkpoints/Slots/LeaseHolder.cs:47/59 | master; file unchanged | slot holder launcher | not a reader |
| scripts/lib/CheckpointProcessRunner.cs:33/87 | CARD-1199 | the shared runner itself | CP-21, CP-22, CP-23 |
| all other non-test hits | as in the 13j table above | as in the 13j table above | as in the 13j table above |

CARD-added test fixture launches (tests/Antiphon.Tests/Checkpoints; census hits with CARD blame):

| Fixture site | Bound after 13k | Not bounded / not certified |
| --- | --- | --- |
| CheckpointFixtureProcess.Run/Git, used by OutputCleanupTests.Git, the OutputCleanupTests parent-symlink stamp probe (pwsh, 30 s) and wrapper-preflight (pwsh, 90 s), CheckpointTaskOwnershipTests ownership-loss `git init`, CheckpointOutputConfinementTests.Git and SeedBuild (dotnet build, 120 s), CheckpointShapeConflictTests.GitOut/Git and CheckpointTrackedBaselineTests.GitOut/Git (30 s) | CheckpointProcessRunner (Git hardening off): one deadline covers exit and both drains. Expiry kills the Linux process group and fails the fixture step. CP-23 proves this: a FIFO-blocked config open and a pipe-holding descendant each fail within 4 s against a 2-s limit, and the descendant dies. Both cases have ordinary controls. Red at the 13k tests-first commit. | Windows custody, R-2 |
| CheckpointOutputConfinementTests:127-148, CheckpointShapeConflictTests WaitAsync, CheckpointTrackedBaselineTests WaitAsync (pwsh run-checkpoint/tool children) | Exit wait and both drains take the caller's CancellationToken (the test Timeout token where the method declares one), so cancellation ends the step | On cancellation the helper does not kill the child. RegisterCheckpointChild kills the registered tree at scope teardown. A descendant that has left the tree is not killed. No native held-pipe control. |
| CheckpointStampSafetyTests (pwsh harness) and CheckpointReceiptSafetyTests.Child (dotnet tool) | Four-second exit deadline, then Kill(entireProcessTree) | Drains are awaited after exit or kill with no separate limit, so a descendant that left the tree and still holds a pipe would block it. Not reproduced. |
| SafeDeleteProcessBoundaryTests.Run | 10-s exit limit, Kill(entireProcessTree), then a 2-s drain wait asserted | Kill(entireProcessTree) only reaches descendants that are still in the tree |
| CheckpointBoundedReaderTests (node supervisor) | The supervisor script owns the hard child watchdog and group kill (CP-21) | The test's own WaitForExitAsync has no token or limit, and the method has no Timeout. The supervisor and the outer row deadline are the only bounds. |
| CheckpointOwnedOutputTests.InitRepo `git init` | No redirected output. WaitForExit(30_000) fails the step, and RegisterCheckpointChild kills the tree at teardown. | The child is not killed when the wait expires, only at teardown |
| scripts/test-checkpoint-stamp.ps1, test-checkpoint-bounded-reader.ps1, test-checkpoint-bounded-readers.mjs (CARD-added harness scripts) | Enclosing test child watchdog (CP-18 four seconds, CP-21 node supervisor) | The script-internal `& git` calls have no own deadline |

Master test fixture launches (census hits with master blame: BaselineComparerTests, CheckpointImportTests, CheckpointRecovery*Tests, CheckpointRepeat*, CheckpointSource*Tests, CheckpointTaskOwnershipTests apart from the 13k change, CheckpointTemp*Tests, DetachedLauncherTests, DirectoryLinkHelper, PlanCoverageHandleTests, RerunFilterHostTests, TimeoutTests) are inherited and were not re-audited. DirectoryLinkHelper (Windows `mklink`) and TimeoutTests (Windows powershell) keep master's synchronous reads before their wait.

The outer checkpoint row deadline still applies to every test above. It is the only bound for the sites the table says have none.
