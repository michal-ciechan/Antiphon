# CARD-1436 git census exited-process holder

Brief-scoped fix (no separate Plan stage). `GitIndexLock.CensusGitProcesses` listed every git on the
host and recorded a process that exited before its start time was read as `(pid, null)`;
`HasHolder` counts a null start as a holder, so a short-lived git anywhere on the host could make a
stale index lock read as held (`LandingGit.InspectIndexLockAsync`, found by CARD-1421 as a 1-in-3
red of `AgentTaskLandIndexLockTests.C688_...(main-checkout)`).

## Change

- S1 (tests): `GitIndexLockTests.C1436_census_drops_git_that_exits_mid_census` (V-1),
  `GitIndexLockTests.C1436_census_keeps_live_holders` (V-2); `LandingGitFixture.FixtureGit` narrows
  the census to its own git children through the new seam.
- S1 (fix): `GitIndexLock.IProcessSource`/`ICensusProcess`, `CensusGitProcesses(IProcessSource)`
  skips a process whose start-time read fails and whose `HasExited` is true (an
  `InvalidOperationException` from `HasExited` also means gone); a live process with an unreadable
  start (access denied, or `HasExited` unanswerable) stays a `null`-start holder.
  `LandingGit.GitProcesses` is the protected seam (default `HostProcessSource.Instance`).

## Verification

- V-1: a fake source yields a process whose start-time read throws `InvalidOperationException` and
  whose `HasExited` is true; the census is empty, the process is disposed, and a 600 s lock is Stale.
- V-2: a live git started before the lock and a live git whose start and exit reads both throw
  `Win32Exception` are both kept; each alone classifies a 600 s lock as Held.
- R-1: `GitIndexLockTests`, `GitIndexLockPathTests`, `AgentTaskLandIndexLockTests` whole classes.
- PC-1 (pending, Mutation): drop the `if (Exited(process)) continue;` skip; V-1 must go red.
- PC-2 (pending, Mutation): make `Exited` return true for every probe; V-2 must go red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1436-1/` | census-witness | `/*/*/GitIndexLockTests/C1436*` | V-1, V-2 | all listed, 2 new tests, 0 failed/skipped | 2 | 5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1436-1/` | census-classes | `/*/*/(GitIndexLockTests*)\|(GitIndexLockPathTests*)/*` | R-1 | all listed, 0 failed/skipped | 13 | 3 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1436-1/` | land-index-lock | `/*/*/AgentTaskLandIndexLockTests/*` | R-1 | all listed, 0 failed/skipped | 13 | 8 |
