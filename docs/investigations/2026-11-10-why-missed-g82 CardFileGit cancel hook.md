# Why missed: g82 CardFileGit cancel hook (Windows non-atomic tree kill)

Debug d018e35b, rc run 0b292fa4 (rc/2026-11-18 = `56e96eabf`), chunk antiphon-all-group-82:
`CardFileGitFailureAcceptanceTests.Git_failures_are_sanitized_preserve_HEAD_and_retry_after_owned_fault_is_removed(cancel)`
failed at `AssertOwnedHookKilledAsync` (`hook-wait-failed` existed). Reproduced on the
Windows desktop with a pwsh copy of the same `git commit` + hook + `Process.Kill(true)`.

## Cause

`CardFileRepository.RunGitInputAsync` kills a canceled or timed-out git child with
`Process.Kill(entireProcessTree: true)`. On Windows that call is a top-down walk. It
terminates one process, then re-reads the whole process table to find that process's
children, and repeats for each child. The tree under the card-file git child is
`git.exe -> {conhost.exe, sh.exe (hook) -> sh.exe (MSYS fork stub) -> PING.EXE}`.
git gets its own `conhost.exe` because the child starts with `CreateNoWindow`. conhost
is created first, so it is killed first. The hook shell is killed only after a full
process-table read of conhost's (empty) child set.

During that read the hook shell is still running and its console is gone. If it is
starting its next native child (`ping`), the start fails with exit 127. The hook then
runs its own failure branch (`echo wait-failed > hook-wait-failed; exit 1`) after the
cancel. Measured on the desktop:

- Test hook (`ping -n 2`, cancel within about 25 ms of `hook-started`): 1/12, then 2/50 rounds red.
  Failing rounds logged `wait-failed rc=127`.
- Tight-spawn hook (`ping -n 1` in a loop, cancel after 300 ms): `Kill(true)` 4/10 red.
  The same kill preceded by `TerminateJobObject` on a job holding git: 0/10.

This is not the CARD-1850 dead-parent orphan. Here the hook shell itself outlives the
kill for a few ms. The process tree is fully connected
(`git 106036 -> sh 97240 -> sh 18624 -> PING 63540`). It is the same class of defect:
a parent-pid walk on Windows is not an atomic kill of the owned tree. Job containment
fixes both.

A second, latent test defect: the hook wrote `$$` to `hook-pid`. Under MSYS, `$$` is the
MSYS pid (for example 19575), not the Windows pid (97240). `HookAlive` therefore probed
an unrelated Windows pid. It could pass vacuously, or wait 15 s and fail when that pid
happened to be live. The hook now writes `/proc/$$/winpid` when it exists.

## Introducing commit/card

introducing commit/card: c090828752dc2a0b8d82f0f8dcadff674781f2c4 (CARD-0408). The
card-file git kill was `Process.Kill(entireProcessTree: true)` from the start. The
assertion that exposes it, the stall hook with `hook-wait-failed` and
`AssertOwnedHookKilledAsync`, came in e1fd65823671fb3ead5ad261b388f44dcd2f65bd. That
commit is a cherry-pick of `ffd650c97` from Custom task 61c48a13 ("Group 78: CardFileGit
cancel fault hook-ended", Grok, 2026-10-28). The Windows timeout change came in
aa22def08257992c6c2200bbfd11f3b48205c3e6 (Debug d823935e, CARD-1514, 2026-11-07).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- 61c48a13 and d823935e were rc repairs under the direct rc-fix rule. Neither task record
  has review evidence. Their selection was `CardFileGitFailureAcceptanceTests` on the
  server2 Linux runner.
- CARD-0408 S1 review: the class did not exist yet. S3-S4 (`f13dec290`) added it, and it
  ran on Linux.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the class was
selected, but every selection ran on Linux. Linux has no conhost sibling, and `Kill(true)`
there kills the hook shell before its children, so the race cannot happen on Linux. On
Windows the class runs only in the native rc chunks (Integration, Slow, git lane,
`tests/Antiphon.Tests/Application/CardFileGitFailureAcceptanceTests.cs`). The existing
cancel row is red in only about 2-8 % of rounds, so earlier Windows runs passed by chance.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Git/CardFileRepository.cs` -> `/*/*/CardFileGitFailureAcceptanceTests/*` on a native Windows runner.
The new row `Canceled_commit_ends_a_spawning_hook_before_it_sees_its_child_fail` runs 8 rounds
and is about 40 % red per round under the old kill, so a single Windows run catches the defect.

## Fix

`CardFileRepository` now places each card-file git child in a Windows job
(`OwnedChildMarker`, as LandingGit does). The kill path calls `TerminateJobObject` first,
which ends git, its conhost and the hook together. The existing `Kill(true)` then stays
as the fallback for anything outside the job. The job has no KILL_ON_JOB_CLOSE, so a
normal exit and dispose behave exactly as before. Linux is unchanged.

## Exemption

same cause as an already written note: none
