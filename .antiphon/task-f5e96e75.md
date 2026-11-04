# task f5e96e75: C448 V36 flake, bounded sharing retry for MoveAside and the set-aside record

Branch `feat/card-task-f5e96e75`, runner mirror `/work/worktrees/task-f5e96e75`, base master
`1ef88240b3c5ee2c759db958e5fb0b635b1248cc`. Original Code task (landing owner): f5e96e75.

- Red (seam plus witnesses, no retry): `37bba23abdeaa6f180e9c1f4a9324d117ba68699`
- Fix: `de6d0392c5b935401992302e47a0a4b4efdb8418` (exact tested SHA for every run below)

## Change

- New `server/Infrastructure/Git/WorktreeRenames.cs`: the seam is an internal record
  `(MoveDirectory, ReplaceFile, Pause, Windows)`. `System` is `Directory.Move`,
  `File.Move(overwrite: true)`, `Thread.Sleep` and `OperatingSystem.IsWindows()`. It defines
  `SharingAttempts = 5` and `SharingPause = 100 ms`, which are the no-follow open's existing
  literal bound. `OpenNoFollow` now uses these named constants, with identical behaviour. No
  shared helper existed before; `WorktreeGuardedCleanup`'s 250 ms nominated retry is a whole-pass
  journal retry, not a reusable helper.
  - `Retrying(rename, src, dst)` retries only when `IsSharingConflict` holds, and makes at most
    5 attempts. After the last attempt the original exception propagates unchanged.
  - `IsSharingConflict` holds only when Windows is true, the exception is an IOException or
    UnauthorizedAccessException, and its HResult is 0x80070020 (sharing violation), 0x80070021
    (lock violation) or 0x80070005 (access denied, which is how a directory rename is refused
    while a child is open).
  - Off Windows nothing is transient, because an HResult there carries errno and EACCES is a
    lasting permission.
- `WorktreeNoFollowDelete.MoveAside(path, aside, renames = null)` and
  `WorktreeSetAside.Record(common, record, renames = null)` call the rename through `Retrying`.
  Record retries only the temp-to-record replace. The temp file write is unchanged.
- `GuardedWorktreeRemoval` gets `internal WorktreeRenames Renames { get; init; } = System` and
  passes it to both calls. The DI singleton and all existing constructions keep `System`.
- Refusals are unchanged. A lasting conflict is still `worktree_remove_failed` with outcome
  `move_error_32` (or 5 or 33) for the move, and `cleanup_inspection_error` for the record, with
  Detail null as before. The brief's "move_error code in Detail" does not match the code: that code
  is carried in `WorktreeGitOutcome.GeneratedCode` (the cleanup journal capture), not in
  `WorktreeRemoval.Detail`. This change leaves both as they were.

## Callers and what the retry changes

| Site | Caller | Effect |
|---|---|---|
| `WorktreeSetAside.Record` | `GuardedWorktreeRemoval.RemoveDirectoryAsync` (record before move aside) | A transient Windows hold of the `.tmp` file or record now succeeds instead of `cleanup_inspection_error`. A lasting hold refuses up to 400 ms later. Success is never delayed. |
| `WorktreeNoFollowDelete.MoveAside` | `GuardedWorktreeRemoval.RemoveDirectoryAsync` (the only production call) | A transient hold now moves aside instead of `worktree_remove_failed`. A lasting hold refuses up to 400 ms later. `failedAt` is stamped after the retries, so `WorktreeGuardedCleanup`'s 10 s extra budget is not consumed by them. |
| `RemoveDirectoryAsync` callers | `GuardedWorktreeRemoval.RemoveAsync`: Publication without journal context, LocalMerge (`DelegationWorktreeService`:909), SettledTask (`TaskWorktreeRetirementService`:379; S4a desktop legacy release), and `WorktreeGuardedCleanup.RemoveAsync` (Publication with journal from `AgentTaskLandingProtocol`:456; first pass and its nominated second pass) | As above, per pass. In the journal path a transient hold now clears in the first pass. A lasting hold still records `move_error_N` and reaches the unchanged nomination (Native.HasSharingConflict) and 250 ms second pass, adding at most 2 x 400 ms to a refused publication cleanup. |
| Test-only | `WorktreeGuardedCleanupTests.C665_SetAsideRestoreRefreshesRegistrationCache` calls Record and MoveAside directly | Defaults to `System`; no change on Linux. |
| Not changed | `WorktreeNoFollowDelete.Restore` (a plain `Directory.Move`, returns false on failure) | Outside the brief. A transient refusal there is still `worktree_removal_incomplete` with the record kept. A later pass resumes it, which is fail-closed. |

## Witnesses (LandingRemovalPolicyControlTests, RemovalFixture gains an injected rename fault)

The fixture uses `WorktreeRenames.System` unless a fault is injected, so the V36 baseline and all
other tests run the production renames, including the real 100 ms sleep on Windows.

- `C448_V36_TransientSharingOnSetAsideRenameIsRetriedToAClean` covers move or record crossed with
  sharing, lock, denied-io and denied, 8 cases. Each fails once and then the removal ends clean,
  with 2 calls, 1 pause of 100 ms and 2 mutations. All 8 were red at 37bba23a (move: Residue
  `worktree_remove_failed`; record: `IsClean` false).
- `C448_V36_LastingSharingOnSetAsideRenameStillRefuses` covers move and record. It asserts the same
  residue, Detail null, `move_error_32` or no outcome, no mutations, the tree intact and no record
  left. It also asserts exactly 5 calls and 4 pauses. The residue, Detail and outcome assertions
  passed at red; the run was red only on the bounded count (1 versus 5).
- `C448_V36_LastingRenameErrorIsNeverRetried` covers not-found, not-empty (145), exists (183) and
  not-a-directory (267) for move and record, 8 cases. Each makes 1 call with 0 pauses; move keeps
  `move_error_{3,145,183,267}`. Green at red too (correct: no retry existed).
- `C448_V36_SharingCodesAreOnlyTransientOnWindows` covers 4 cases with `Windows = false`. Each makes
  1 call with no retry, and `System.Windows == OperatingSystem.IsWindows()`.

## Runs (server2 Linux, all at de6d0392c, one build-slot lease per build or run, UseAppHost=false)

No `### Checkpoints` plan exists for this task: it is a Debug-derived Code brief with named
classes, so the runs below are the brief's list, run directly through build-slot.

| Run | Lease | Waited | Result |
|---|---|---|---|
| build at 37bba23a | fadf4fcc | 0s | succeeded (186 s) |
| red `/*/*/LandingRemovalPolicyControlTests/C448_V36_*` at 37bba23a | 46f13227 | 0s | total 45, failed 10 (8 transient + 2 lasting count), passed 35 |
| build at de6d0392c | db529fd3 | 0s | succeeded (180 s) |
| green, same filter | 1f529d39 | 0s | 45/45 passed |
| affected classes (below) | 259cbf85 | 0s | total 730, passed 718, failed 0, skipped 12 (511 s) |

Affected filter (a `*` after every class name): LandingRemovalPolicyControlTests 92+2skip,
WorktreeGuardedCleanupTests 47+2skip, C0824DesktopLegacyReleasePolicyTests 120,
C0824DesktopLegacyReleaseSettingsTests 32, C0824DesktopLegacyReleaseNativeTests 0+7skip,
DelegationWorktreeTests 49, AgentTaskLandAdoptionTests 21, AgentTaskLandAdoptionConcurrencyTests 7,
AgentTaskLandMonitoringTests 25, AgentTaskLandRequestTests 30, AgentTaskLandTargetRaceTests 11,
AgentTaskLandTargetRaceFakeTests 6, AbandonedLandingReconcilerTests 7, LandRefRetentionTests 39,
LandReplacementApprovalTests 5, LandingProtocolGuardTests 23, LandingProtocolHarnessTests 18,
StageOutcomeSummaryTests 5. It also includes the other set-aside users found by grep:
AgentTaskLandRemovalMatrixTests 51+1skip, AgentTaskLandBareRepositoryCleanupTests 15,
AgentTaskLandCheckpointWorkerTests 2, AgentTaskLandCheckpointMatrixTests 41,
AgentTaskLandRegistrationSafetyTests 7, AgentTaskLandNestedRepositoryCleanupTests 8,
CompletedCardWorktreeRemovalTests 37 and AgentTaskWorktreeLockOutcomeTests 20. There are no
WorktreeNoFollowDelete* or WorktreeSetAside* test classes.

The 12 skips are all Windows-only `Skip.Test` cases. They were not run: nothing runs on the
desktop during the live qualification.
- C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded
- C665_LockedFileMidDeleteResumesOnLaterPass x2
- C665_EmptyUnlistedJunctionTargetSurvivesRemoval
- C448_V20 (windows-file-handle)
- C0824 Native: Held_directory_defers_never_forces (open-handle, cwd), Long_path x2,
  Windows_aliases_and_junctions x3

These cases are the real-sharing witnesses. Under a lasting hold each refuses about 400 ms later
than before, well inside the class's 60 s `[Timeout]`. They are pending the Windows lane.

Not run: the whole Unit lane, which the profile names for a Final round. It takes about 3 h on
server2 against this brief's 60-minute budget. The diff changes only the exception path of two
renames with one production caller, and every class touching that caller ran. No other build or
test run was made.

## Gates

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 1ef88240b -HeadRef HEAD` reported
  commits=2, violations=0 and exit 0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master 5a95ae7a5) was clean, tree 4744c4192.
- Pty-helper flake: Code 931a28cf's TestOwnedPtyHost manifest read is already on master as
  e718850bd, so nothing was done there.

## Pending for Mutation (no PCs declared; candidate controls)

- M-a: drop the `Retrying` call in MoveAside. The move cases of TransientSharing... go red.
- M-b: the same for Record. The record cases go red.
- M-c: `attempt < SharingAttempts` replaced by `true`, or the bound raised. The LastingSharing
  count goes red.
- M-d: retry any IOException. The LastingRenameErrorIsNeverRetried cases go red.
- M-e: drop the `Windows &&` gate. SharingCodesAreOnlyTransientOnWindows goes red.
- M-f: drop one HResult (0x80070020, 0x80070021 or 0x80070005). The matching transient case goes red.
- M-g: `Pause` not called. The `Pauses` assertions go red.
