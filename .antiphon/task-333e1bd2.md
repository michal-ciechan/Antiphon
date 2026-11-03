# Code 333e1bd2: CARD-1617 Review 46541b3e D-1, RootExitedForHangup signals witnessed

Branch feat/card-task-333e1bd2, worktree /work/worktrees/task-333e1bd2 (server2, Linux). Base
fc709c93804c6922251f05f99272cbdfee1d22b8. Landing owner: original Code task f4830a56-0f3e-4153-bc31-ced3393b8a37.
Plan amendment: docs/superpowers/plans/2026-11-03-card-1617-review-d1-amendment.md.

## Commits
- 6b526a2620437e40de7d8d4b69344cfdc2794abb S4a: red-first C1681_Root_* tests (15 cases) in
  Card1681PtyHangupTests. The RootExited/ProcStatReader stubs are inert; production is unchanged.
- 6aae722a89cc2dcf92f03d6d67ede6b3a316f216 S4b: `internal static bool RootExited(bool exitObserved, Func<string?>? readStat)`
  and `ProcStatReader(path)`. RootExitedForHangup passes `_exitTcs.Task.IsCompleted` and the /proc
  reader, or null when there is no pid. Order and exception mapping are unchanged. The Windows
  span sha256 is 2fd1ec493d80b8038c32994e9bcae1742057e369498ce7cc0a62db9d2c712e9a (re-pinned,
  unchanged).

## Checkpoints (unedited lines)
CHECKPOINT CP-3 commit=6aae722a89cc2dcf92f03d6d67ede6b3a316f216 build=ok filter=/*/*/LinuxCustodyReleaseHostTests/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-333e1bd2/.antiphon/checkpoints/20261103-094833-9a09/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6aae722a89cc2dcf92f03d6d67ede6b3a316f216 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=6aae722a89cc2dcf92f03d6d67ede6b3a316f216 build=reused filter=/*/*/(HostCustodyPlatformTests*)|(CustodyReceiptBackendTests*)/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-333e1bd2/.antiphon/checkpoints/20261103-094833-9a09/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=6aae722a89cc2dcf92f03d6d67ede6b3a316f216 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=6aae722a89cc2dcf92f03d6d67ede6b3a316f216 build=ok filter=/*/*/Card1681PtyHangupTests/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-333e1bd2/.antiphon/checkpoints/20261103-094833-9a09/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=6aae722a89cc2dcf92f03d6d67ede6b3a316f216 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=6b526a2620437e40de7d8d4b69344cfdc2794abb build=ok filter=/*/*/Card1681PtyHangupTests/* executed=30 passed=18 failed=12 skipped=0 trx=/work/worktrees/task-333e1bd2/.antiphon/checkpoints/20261103-094631-f866/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=6b526a2620437e40de7d8d4b69344cfdc2794abb sourceState=clean buildSource=verified

- CP-8 is run 20261103-094631-f866, RED exit 1, recorded red. It had 12 failures, not the
  planned 10: the two C1681_Root_live_stat_is_not_exited cases fail on their read-count
  assertion because the stub never reads. The plan records this.
- S4b is run 20261103-094833-9a09, GREEN exit 0. `validate --expected-source-sha 6aae722a...` gave
  CHECKPOINT SOURCE VALID rows=3. W2/W3 are unchanged and green.
- slot=granted waited=0s on every row. The tool reported "unlisted: none" for both runs.

## Mutants
Scratch detached worktree at 6aae722a8. Each mutant got one build-slot lease for the build and
one for the --no-build run. All were restored and the worktree was removed.
- M2, drop the stat signal: RED 8/30 (stat_alone x5, missing x2, real_files).
- M3, drop the exit signal: RED 2/30 (both exit_observed cases).
- M5, unreadable stat counts as exited: RED 3/30 (unreadable io/access, real_files).
- M4, root-exit always true: RED 1/9 LinuxCustodyReleaseHostTests (C1681_Eio_while_root_alive_stays_drain_failure).
- M1, backoff `1 << k`: RED 2/9 (C1617_Throwing_termination_counts_as_failed_attempt,
  C1617_Failed_termination_retries_on_capped_backoff).

These mutant builds and runs are unlisted and were commissioned by the brief. The tool also
built itself once, unleased, through `dotnet run --project tools/Antiphon.Checkpoints -- run --help`.

## Other
- check-evidence-diff fc709c938..6aae722a8: commits=2 entries=0 violations=0.
- `git merge-tree --write-tree origin/master(a2584ef6e) HEAD` is clean.
- A-3 was not taken. `HostSession` (Antiphon.PtyHost) calls UseCustodyClockForTests, and the
  `Antiphon.Agents.Pty` InternalsVisibleTo does not reach that assembly. csproj edits are
  forbidden.
- Not run:
  - the whole Unit lane (Final profile). The brief scoped this round to CP-3/CP-4/CP-7.
  - the Windows PtyCustodyTests/HostCustodyTests, which are owed to a Windows lane.
- PC-1..PC-19 stay pending for Mutation. The new ones are PC-15..PC-19.
