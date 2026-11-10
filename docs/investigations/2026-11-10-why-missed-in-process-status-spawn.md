# Why missed: post-window in-process status spawn

One committed note for rc run 6591e378, chunk antiphon-all-group-120.
`LandingSourceOptionalLockTests.C1453_PostWindowSourceStatusCreatesNoIndexLock` failed at
2026-11-10 16:51:58Z: `inProcess.StatusSpawns should be 0 but was 1`. The other 88 tests in the
chunk passed. CARD-1781.

The Docker/WSL outage that removed the Hyper-V NIC at 16:47Z did not cause this. Group 120 is
recorded as a real assertion failure, not an outage victim, in
`docs/investigations/2026-11-10-docker-outage-rc-run-6591e378.md` on `feat/card-task-88c9a263`.
A local `git status` of a temp worktree does not use that NIC. The porcelain was empty; only the
spawn count failed.

## Failure

The in-process status answers only when the git child's status config agrees with libgit2
(`GitStatusConfigAgreement`). This assembly's `LibGit2ConfigIsolation` points libgit2 at an empty
directory. `SpawnCountingGit` in `LandingSourceOptionalLockTests` did not set
`GIT_CONFIG_NOSYSTEM`, so on Windows the child still reads Git for Windows' system gitconfig,
which sets `core.autocrlf=true`. The keys disagree, `InProcessGitStatus.TryAnswer` returns null,
and `LandingGit` spawns `git status`. Git's own porcelain stays empty, and `GIT_OPTIONAL_LOCKS=0`
still prevents `index.lock`. Production is unchanged: there the child and libgit2 read the same
files, so a clean tree stays in process.

Linux reproduction, before the isolation: a system file containing `core.autocrlf=true` on the
child, with libgit2 still isolated. `C1453_WindowsSystemAutocrlfStillAnswersInProcess`: 1 failed,
`StatusSpawns` was 1, porcelain empty. Removing the isolation after the fix (mutant, restored,
not committed) failed the same way: 1 failed, `StatusSpawns` was 1.

## Introducing commit/card

introducing commit/card: cfaf42afe8d67ef6968d6ec9982c27a8fff3e1ee (CARD-1453)

`test: CARD-1453 post-window source status must not take index.lock` added
`C1453_PostWindowSourceStatusCreatesNoIndexLock` and the assertion
`inProcess.StatusSpawns.ShouldBe(0, "a clean tree is answered in process")`.
The landed fix is 1d4a76c9fbcabcc8cf5eaa9414484dfbc33c29d9. The test's git child was never given
the suite isolation that `LibGit2VsGitWorkingTreeVerdictTests` already uses.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `19af8f63-8ae6-43ff-9956-90ba1c41438f`, scope Full (commissioned Final, outcome Clean,
  `ordinaryScopeCompleted=Full` in the report; the stage-outcome row stores Unknown), subject
  Code `eab93359-70e1-4fe8-8cb4-030b66bde8fb`, reviewed source
  `1ccdcd4d36cf412154653bd4a68c251373487534`, runner server2, observed platform linux.
  Selection actually run: `LandingSourceOptionalLockTests*`, `ServerGitProcessTests*`,
  `AgentTaskLandPublicationTests*` — 88 total, 87 passed, 1 skipped, 0 failed. The skip is
  `C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(locked, False)`, a Windows sharing denial.
  Guard batch on that same linux run: 77 passed, 0 failed.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane,
`Integration`, `tests/Antiphon.Tests/Infrastructure/LandingSourceOptionalLockTests.cs`

The method was inside the class filter. The selection ran on linux, where this host's system
gitconfig does not set `core.autocrlf`. The Windows child that still reads Git for Windows'
system file was outside the platform that review ran. `requiredPlatform` on the Code and Review
tasks was Any; `observedPlatform` was linux.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/Infrastructure/LandingSourceOptionalLockTests.cs` -> `/*/*/LandingSourceOptionalLockTests/*`

That class now contains `C1453_WindowsSystemAutocrlfStillAnswersInProcess`, which injects the
Windows system `core.autocrlf` on Linux. The class filter catches the spawn without a Windows host.

## Exemption

same cause as an already written note: none.

The `core.autocrlf` fixture miss in
[2026-11-08-why-missed-land-graph-land-refresh](2026-11-08-why-missed-land-graph-land-refresh.md)
is a different cause (a land-graph manifest copy and a checkout that had to set `core.autocrlf=false`).
