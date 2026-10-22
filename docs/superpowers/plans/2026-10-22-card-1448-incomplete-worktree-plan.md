# CARD-1448 plan: never reuse an incomplete worktree

Source: Debug 8e5fd2d6, `docs/investigations/2026-10-22-runner-sync-identity-mismatch.md` section 6.
A killed `git worktree add` leaves the registration `locked initializing` over a partial checkout and
the schema-2 creation intent at `CreationComplete=false`. The rollback skipped the locked registration
silently, the next dispatch tick adopted the half-built directory because the listing ignored the
creation flag, and the settlement sync then refused with `runner_sync_identity_mismatch`
(273b3a0d, 6fea402b, 4c2309e1, 7c30c896, a1f403eb).

## Decisions

- **D-1** `WorktreeInfo.CreationIncomplete` is set from schema-2 metadata with `CreationComplete=false`.
  `DelegationWorktreeService` never adopts such an entry: neither the pre-create adopt nor the
  `ConflictException` fallback (which now refuses with a named message). The card-spawn adopt in
  `AgentSessionService` takes the same filter.
- **D-2** `WorktreeManager.CreateAsync`, under the repository lease and the creation file lock and
  before the stale-registration heal and the path/branch checks, recovers an interrupted creation only
  when the intent is schema 2, incomplete, with `CreationId` and `InitialSha` for this repository and
  branch; exactly one registration at the path is locked with reason `initializing` on that branch;
  and the branch tip equals `InitialSha`. It then runs `worktree remove --force --force` (or, with no
  directory, deletes only that registration's admin directory, never a repository-wide prune), checks
  the registration is gone, deletes the branch with the old value checked, deletes the intent and
  falls through to a fresh add. Any mismatch is a `ConflictException` that removes nothing. An
  incomplete intent with no registration keeps today's path/branch checks.
- **D-3** `RollbackFailedAddAsync` logs a Warning when it skips a locked registration.

## Slices

One slice: witnesses first (S1-tests, red), then the fix (S1-fix).

## Verification design

Fixtures simulate the interrupted add with a real `worktree add`: delete everything but `.git`,
write `<admin>/locked`, and rewrite the intent with `CreationComplete=false`
(`tests/Antiphon.Tests/TestHelpers/InterruptedWorktreeCreation.cs`).

The red witnesses were run once before the fix through `scripts/build-slot.ps1` (stated in the Code
report): 8 executed, the 5 behaviour witnesses failed, the 3 guards passed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-fix | `tests/Antiphon.Tests -> bin-c1448-fix/` | c1448-manager | `/*/*/WorktreeManagerGitIntegrationTests/C1448_*` | D-1 listing, D-2 recover (with and without directory), refuse on another lock reason or a moved branch | 5 passed, 0 failed/skipped | 5 | 8 |
| CP-2 | S1-fix | CP-1 | c1448-service | `/*/*/DelegationWorktreeTests/C1448_*` | D-1 never adopts (pre-create and fallback), recreate records the base, a complete creation is still adopted | 3 passed, 0 failed/skipped | 3 | 4 |
| CP-3 | S1-fix | CP-1 | c1448-regression | `/*/*/(WorktreeManager*)\|(DelegationWorktreeTests*)/*` | regression over every `WorktreeManager*` class and `DelegationWorktreeTests` | all executed pass, 0 failed/skipped | 70 | 12 |

This is a closed list, run once through the checkpoint tool on server2 at the reported tip. Linux only;
nothing runs on the desktop. D-3 is a log line with no checkpoint.

### Cost

About 24 minutes of rows plus slot waits.

## RC

`WorktreeManager.cs`, `DelegationWorktreeService.cs` and the adopt site are identical on rc
`0714a9a89`; the Code report states the per-commit merge-tree result onto `origin/rc/2026-10-20`.
Low urgency: port with the next rc bundle.
