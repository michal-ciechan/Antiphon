# Final Review ccbcfec4, round 2: child tree kill D-1..D-4 (feat/card-task-8562dc2a)

Subject: `feat/card-task-8562dc2a` at tip `426211ca25cb9a545e909a81611b2916b10d04b9`. The product commit is `47c28a2dd4bed8b509f4bd9e9fa44edd8f0f5680`; the witness fix is `1f069c291433b60e2d595dc9e17c0a0b169168f1`. The branch is built on `24415a6bb`. Code landing owner: task 8562dc2a.

**Verdict: two defects, both Low. Do not top up rc yet.** D-1 from round 1 is closed in the planner. The root identity is the one captured at spawn, and the stale-parent case that could kill the AppHost is excluded and witnessed. D-2, D-3 and D-4 are closed. The sweep is source-valid. What remains:

- The production snapshot-failure fallback has no witness that can go red (mutant Mf survived).
- On Windows, the snapshot can pair a parent edge read at one time with start ticks read later. That still lets a newborn stranger through.

## Defects

### D-1 (Low, test adequacy): the owned-root-only fallback cannot go red

- **Where:** `server/Infrastructure/Git/OwnedProcessTree.cs:294-306` (`KillOwnedHandle`, `root.Kill(entireProcessTree: false)` at :300). The witness is `tests/Antiphon.Tests/Infrastructure/OwnedProcessTreeTests.cs:187-210` (`C448_SnapshotFailureKillsOnlyTheOwnedRoot`).
- **Failure:** the witness injects `() => handleKills++` as the fallback. It proves that the delegate is called, not what the production delegate does. Mutant Mf changed `:300` to `root.Kill(entireProcessTree: true)`. That is exactly the regression the D-4 fix exists to prevent: the old unguarded tree kill. Mf survived OwnedProcessTreeTests, LandingGitTests C448*/C1076* and RepositoryMutationLeaseTests C448*/C1076*, with 70 passed, 0 failed and 2 Windows skips.
- **Why it matters:** the round claims "kills ONLY the owned root with entireProcessTree false", and no test pins that claim. A later edit can bring the unguarded walk back silently.
- **Fix:** give `KillOwnedHandle` a seam, for example an injected `Action<Process, bool>`, or return the `entireProcessTree` flag it used. Then witness it in one of two ways:
  - assert `false` through the seam; or
  - better: in a live Linux test, start a sleeper child that has its own grandchild and call the production fallback directly. Assert the child is dead and the grandchild is alive. Kill the grandchild in `finally` by pid plus ticks.

### D-2 (Low, Windows stop path): the snapshot pairs a T0 parent edge with T1 ticks

- **Where:** `OwnedProcessTree.cs:308-338` (`ReadSnapshot`). The parent comes from the T0 table at `:311`/`:331`, and the ticks come from a later `Process.GetProcessById` at `:332`/`:340-350`.
- **Failure:**
  1. Descendant C is in the Toolhelp table at T0 with parent = root.
  2. Before its ticks are read, C exits and Windows hands pid C to a new, unrelated process X.
  3. The row becomes `(C, root, X.ticks)`. Since `X.ticks >= root.ticks`, the planner accepts it at `:105-110`.
  4. The kill-time re-read at `:169-182` sees X's ticks again, so they match and X is killed.
  5. Passes 2 to 4 then walk X's children, because their start is after X's.
  - The same tear on a parent row admits the stranger and drops the real children.
- **Scope:** this is the residual of the round-1 D-1 class. The difference is that the victim must have been created inside the millisecond window, so the AppHost, runner, dashboard and dcp are out of reach. Any newborn process is in reach, for example a fresh session child or a build node.
  - On Windows, freed pids are reused quickly.
  - On Linux the risk is negligible: a pid comes back only after a full wrap of `pid_max`, and that takes minutes.
- **Why the seam does not see it:** `readTable` hands the planner rows that are already paired, so no current witness can go red.
- **Fix (cheap):** take `t0 = DateTime.UtcNow` before each Windows Toolhelp snapshot, and treat any row with `ticks > t0` as unreadable, which means do not include it and do not walk it.
  - A process that existed at T0 started at or before T0. A genuine child born later is picked up by the next pass with a fresh T0, which is what the four passes exist for.
  - On Linux, read `ppid` and `starttime` from the same `/proc/<pid>/stat` read, or apply the T0 rule on Windows only.
  - Witness it by making `ReadSnapshot` take an injectable clock and tick reader.

## Brief answers

### (1) Is round-1 D-1 closed in the code?

- **Every edge:** `Plan` checks `childTicks >= parentTicks` for every edge at `:107`. The parent ticks come from `accepted[current]` (`:98`), and a null tick is skipped and not walked past (`:105`). Mutant Ma applied the check only at the root edge; it went red on `C448_StaleParentPidExcludesTheChildAndItsSubtree`, the depth-2 row 23.
- **Root identity:**
  - `rootTicks` is `TryStartTicks(root)` on the owned `Process`. The spawn path already read it into the journal (`LandingGit.cs:122`/`:983`, `TryReadStartIdentity`), and .NET caches it: I confirmed by reflection on .NET 9.0.10 that `Process._startTime` is a `Nullable<DateTime>`. So the value used is the identity captured at spawn.
  - `:75-78` blocks a live root row whose ticks differ (`root_identity_mismatch`). Mutant Mb skipped that check and went red.
  - A missing or unreadable root row is walked with the captured ticks. That is safe:
    - On Windows the server holds the root handle, so the pid cannot be reused.
    - On Linux an exited root returns `root_gone` before any walk (`:131-135`).
- **Kill-time re-check:** each sealed pid is re-read through a fresh `Process` at `:169-182`, and only an exact match is signalled. The second compare inside `KillHeld` (`:280`) and the one in `KillOwnedHandle` (`:299`) read the cached `_startTime`, so they are constant self-compares. The guard that actually holds is the `ReadHeldTicks` re-read immediately before the kill (advisory A-1).
- **The declined "parent re-check at kill time" is not a gap.** A start-tick match at kill time pins the same process that was a descendant in the snapshot; re-parenting does not change who it is. Killing it after root-first re-parenting is the intended behaviour. The witness is `C448_ParentDeathGrandchildDoesNotKeepTheJournal`: the root is killed first, the PDEATHSIG grandchild is re-parented, then killed, and the journal clears. It is green in CP-3. Mutant Mc (re-walk the table after each kill) went red on `C448_SnapshotIsSealedBeforeTheFirstKill`. The residual risk is the snapshot-time tear (D-2), not the kill-time one.

### (2) Is TryStartTicks the CARD-1619 identity?

- They encode the same kernel datum differently:
  - `TryStartTicks` is `Process.StartTime` in UTC ticks. On Linux that is `/proc/<pid>/stat` field 22 plus the runtime's cached boot time; on Windows it is the `GetProcessTimes` creation time.
  - `LinuxProcessIdentity` is `boot_id` plus raw field 22.
- **Not a gap.**
  - The boot id matters only for an identity that is persisted across a reboot. Absolute UTC start ticks already separate boots.
  - The in-memory snapshot cannot cross a boot.
  - A pid wrap is caught by the start ticks. Linux resolution is one 10 ms jiffy, and a wrap of 32,768 pids inside one jiffy does not happen.

### (3) The fallback can never hit a reused root

- On Windows, `root.Kill` terminates through the owned handle, and the pid cannot be reused while that handle is open.
- On Linux, the target is the server's own unreaped child. The pid can only be reused after the runtime reaps it, and reaping sets `HasExited`, which `:298` checks.
- The tick compare at `:299` is constant (cached), so it is not the guard. That is harmless.

### (4) Witnesses, mutants and the bound

All mutants ran in a scratch detached worktree at the tip. Each mutant had one build-slot lease for its build and one for its run. Baseline was 10/10 green.

| Mutant | Change | Result |
|---|---|---|
| Ma | tick check at the root edge only | **RED** `C448_StaleParentPidExcludesTheChildAndItsSubtree` |
| Mb | root identity check skipped | **RED** `C448_RootIdentityMismatchWalksNothing` |
| Mc | kill list re-walked after each kill | **RED** `C448_SnapshotIsSealedBeforeTheFirstKill` |
| Md | server self-exclusion dropped in `Plan` | **RED** `C448_ServerAndOwnerPidsAreExcluded` |
| Me | planner bound removed | **RED** `C448_TreeSizeIsBounded` |
| Mf | fallback back to `Kill(entireProcessTree: true)` | **survived**, 70/0/2 (D-1) |
| Mh | production passes -1 instead of `Environment.ProcessId` | survived, 70/0/2 (A-3) |
| restored | none | 72 total, 70 passed, 0 failed, 2 Windows skips; tree clean |

Red runs were 9/10 with exactly the one named failure. Mf, Mh and the restored run used the wide selection: OwnedProcessTreeTests plus LandingGitTests and RepositoryMutationLeaseTests `C448*`/`C1076*`.

**What happens at 65 or more:**

- `Plan` returns the root plus the first 63 descendants in BFS order, with no block reason. All of them are identity-checked, so the partial kill is safe.
- The rest stay alive. If they hold the pipes, the handoff keeps the journal and the fence until they reach EOF, then clears both on its own.
- After a server crash the record stays. `recover-repository-children.ps1` reports the root as `dead` and removes the record only with `-Execute -ConfirmDescendantsExited`, after the operator has inspected the survivors. So this is recoverable and never silently unfenced.
- Gap: the success line `descendants=63` does not say it was truncated (A-2).

### (5) The journal is never removed while a pipe holder lives

The detached-pipe witnesses are green in CP-3:

- `C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder` (`LandingGitTests.cs:607`)
- `C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits` (`:903`)
- `C448_JournalStaysWhileTheRootLives` (`:1058`)

Round 1's M5 already showed that skipping the release goes red on two of them.

### (6) Sweep

- **Checkpoint run** `20261031-184258-c205` at `426211ca25cb9a545e909a81611b2916b10d04b9`, using the worker's plan copied to `.antiphon/rv-ccbc-plan.md` with my paths and bin names:
  - verdict GREEN; 10 rows green, 0 red, 0 skipped; `unlisted: none`
  - wall 20m03s; 4 builds
  - `validate --expected-source-sha 426211ca2…` printed `CHECKPOINT SOURCE VALID source=426211ca25cb9a545e909a81611b2916b10d04b9 rows=10`
- **Row counts** (each row is executed, passed, failed, skipped):

| Row | Selection | Counts |
|---|---|---|
| CP-1 | OwnedProcessTreeTests | 10, 10, 0, 0 |
| CP-2 | RepositoryMutationLeaseTests, Linux-runnable methods | 28, 28, 0, 0 |
| CP-3 | LandingGitTests | 65, 65, 0, 0 |
| CP-4 | child-journal group | 33, 33, 0, 0 |
| CP-5 | RcRosterRepair plus C1514 census (C1404, C1577, C1584, C1410, G2865) | 8, 8, 0, 0 |
| CP-6 to CP-9 | NativeLinuxPolicyWitness in Antiphon.Tests, SessionRunner.Tests, PtyHost.Tests and Agents.Pty.Tests | 3, 3, 0, 0 each |

- **CP-2 omits two methods:** `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` and `C448_V13_WindowsJunction…`. Both are Windows-only and skip on Linux, and a skip fails receipt validation.
- **Land graph** `-Check` (CP-10): `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69 approved`, exit 0. `-Update` was not run.
- **check-evidence-diff** (`-Repository . -BaseRef … -HeadRef 426211ca2…`):
  - base `24415a6bb`: commits=5, entries=1, violations=0
  - base `fc064a682`: commits=8, entries=2, violations=0
- **rc:** `git merge-tree --write-tree origin/rc/2026-10-27 HEAD`, with rc at `e0d49d221513b467f8c853f47587e688e04255e2`, exits 0 with clean tree `e0806e5ae44db2939827f7c86d0eecfa5535b6dc`.
  - The worker's tree `db37f0a1` was computed at `d44e36398`, before the report commit.
  - `24415a6bb` is not on rc. The rc commits, once the defects are fixed, are `a909cdd16`, `c70d88af9`, `24415a6bb`, `47c28a2dd`, `1f069c291`, `6a65b3b20`, `d44e36398`, `426211ca2`, plus the fix round.
- **master:**
  - With master at `2051d327123a5e98524b4bd1ecf31c6e516bdb4e`, a whole-branch merge-tree exits 1 with 49 conflicts. Those include `LandingGit.cs`, `RepositoryChildJournal.cs` and `LandingGitTests.cs`; this is the standing rc-versus-master divergence.
  - A chained cherry-pick of `a909cdd16`, `c70d88af9`, `47c28a2dd` and `1f069c291` onto master is clean.
  - After the pick, `OwnedProcessTree.cs`, `OwnedProcessTreeTests.cs` and `LandingGitTests.cs` are byte-identical to the branch.
  - `LandingGit.cs` differs only in master's lack of the rc-only `cleanup_continuation_failed` catch (8 lines). `RepositoryChildJournal.cs` differs only in master's lack of the rc-only delete-retry seam (`494baf667`).
  - So the port must pick those four commits in order, after the fix round's commits. It does not need the C448 delete-retry work. The pick was not built on master.

### Windows owed rows

`C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion` and `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` are still owed. Read their stderr as follows:

- **`descendants=0` and the json remains:** the walk captured nothing. Look for:
  - a `snapshot_failed … fallback=owned_root_only` line, meaning enumeration failed; or
  - `kill_failed pid=<root> reason=root_identity_mismatch`.

  If neither is there, the sleeper's recorded parent was not a live accepted pid. A dead intermediate shell orphans its children from the walk; .NET's tree kill has the same limit.
- **`descendants>0` and the sleeper alive:** compare the sleeper pid with `killed=`.
  - If it is listed but alive, the terminate failed.
  - If it is absent, look for `kill_failed pid=<sleeper> reason=reused|unreadable|Win32Exception`. With no such line it was never accepted.
  - On Windows, an exited root also logs `kill_failed pid=<root> reason=unreadable` (A-4). Ignore that line.
- **`descendants>0`, sleeper dead, json remains:** the problem is in drain or delete; read `CleanupFailure`.

### The worker's two failed runs

Neither is a product defect.

- **Run `20261031-165356-3c92`** (`row_failed`): CP-2 selected the whole class, and the 2 Windows-only skips count as `row_failed`. The plan was then narrowed.
- **Run `20261031-173646-994f`** (`refused: tampered`): CP-10 is a command row that runs its own Release build. That build rewrote ignored files under the LandGraphAudit `obj/Release` and `bin-tk8562land` trees, which were already in the ledger from run 1, and the confinement check refused as designed.
  - My run used a fresh worktree and passed. I deleted my `bin-rvccbcland` trees afterwards, because the tool does not clean up after a command row.
  - A rerun in the same worktree must delete those trees first.

## Advisories

- **A-1:** on Windows, a `Process` from `GetProcessById` holds no handle, so `held.Kill` reopens by pid. That leaves a window of microseconds between the tick re-read and the kill. Read `held.SafeHandle` before `StartTime` so the read and the kill share one pinned handle. The `:280`/`:299` re-compares are constant and can go, or should be documented as such.
- **A-2:** log truncation explicitly, for example `truncated=true max=64`, when `Plan` stops at `MaxDescendants` or `ReadSnapshot` stops at `MaxCandidateRows`.
- **A-3 (Mh):** the production `Environment.ProcessId` wiring has no witness. It is defence in depth: the server always started before any git root, so the tick ordering already excludes it.
- **A-4:** on the Windows dead-root path every kill logs `kill_failed pid=<root> reason=unreadable`. Skip the root when `HasExited` is already known, so the native triage does not read it as a failure.

## Scope

- The ordinary scope is **Interim**. The brief's named sweep ran in full and validated. The whole Unit lane was **not** rerun at `426211ca2`: it takes about 70 minutes of wall time on server2, past this 75-minute budget.
- The last full Unit lane was review 877889c5 at `24415a6bb`: 6,310 executed, 2 load flakes that were rerun green.
- The delta since then is `OwnedProcessTree.cs`, `LandingGit.cs` and their two test classes, all of which ran here.
- PCs stay pending for SourceLanding Mutation.
