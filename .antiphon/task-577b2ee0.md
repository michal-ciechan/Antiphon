# Review 577b2ee0, round 1 of 3: feat/card-task-f5e96e75 (C448 V36 bounded sharing retry)

Verdict: **Clean, no defects.** Subject tip 33af3d5c78d2b56ad9b8148c5850089d835352ac. Its code is the
tested fix de6d0392c5b935401992302e47a0a4b4efdb8418; the tip adds only `.antiphon/task-f5e96e75.md`.
Red 37bba23abdeaa6f180e9c1f4a9324d117ba68699, base 1ef88240b3c5ee2c759db958e5fb0b635b1248cc. Landing owner: Code f5e96e75.

## Brief items

1. **Fail-closed unchanged.** After the last attempt, `Retrying` rethrows the fifth attempt's exception.
   It reaches the same catch as before. MoveAside clears the record and reports `worktree_remove_failed` with
   `GeneratedCode` = `move_error_<HResult & 0xFFFF>` and `Detail` null, because `setAsideDetail` is not yet set.
   Record's exception reaches the outer catch and reports `cleanup_inspection_error` with outcome null.
   Only the timestamps differ: `Outcome.At` and `failedAt` are taken after the retries, up to 400 ms later.
   Not found (0x80070003), not empty (145), already exists (183) and not a directory (267) are not retried.
   Nothing is forced, and nothing is deleted differently. The Lasting and NeverRetried witnesses pin this:
   no mutations, the tree is intact, no record remains and the branch is present.
2. **The HRESULT match is right.** The code compares the full HRESULT values 0x80070020, 0x80070021 and
   0x80070005 (not the low word). It requires `Windows && ex is IOException or UnauthorizedAccessException`.
   The precedence is correct: `or` is a pattern combinator. On Windows .NET builds:
   - sharing as `IOException(MakeHRFromErrorCode(32))`;
   - lock (33) through the default `IOException(MakeHR)` mapping;
   - access denied as `UnauthorizedAccessException`, whose default HResult is COR_E_UNAUTHORIZEDACCESS = 0x80070005.
   `Directory.Move` maps ERROR_ACCESS_DENIED to an `IOException` with HResult 0x80070005, and that case is covered (denied-io).
   An IOException whose low word is 5, 32 or 33 under another facility (raw errno, 0x8013xxxx) is NOT retried.
3. **A retry cannot change a decision.**
   - `MoveFileExW` on one volume is atomic. It either renames or fails, and it never "fails after succeeding" on
     a local path. The record temporary file is a sibling, so the replace is also a same-volume rename.
   - A transient failure leaves the source present and the target absent, so a retry cannot apply twice.
   - The hypothetical "attempt 1 moved, then threw" case is handled no worse than before. Attempt 2 throws
     not-found, and the catch clears the record exactly as the single failed attempt did.
   - For Record in the same case, attempt 2 throws FileNotFound and the pass reports `cleanup_inspection_error`
     with the record kept and the tree unmoved. The next pass sees the record with no aside and clears it.
   - No success turns into a new refusal.
4. **Timing and locks.** All three callers hold a `RepositoryLease` across `TryRemoveAsync`. That lease
   already spans git processes, so this is not new. No DB transaction is open across the call:
   - AgentTaskLandingProtocol:1025 and :1046 are scoped inside `TransitionAsync` and `SaveAsync`;
   - TaskWorktreeRetirementService:120 and :223 are other methods.

   Worst-case added delay:

   | Path | Added delay |
   |---|---|
   | One pass, transient Record then a lasting move | 0.8 s |
   | Lasting move hold alone | 0.4 s |
   | Publication (two passes) | up to 1.6 s |
   | S4a SettledTask (one pass), 25 per tick | up to 10 s for lasting holds; 20 s only if every candidate also had a transient Record |

   `failedAt` is stamped after the retries, so the 10 s extra budget is not consumed.
5. **OpenNoFollow is unchanged.** It still makes 5 attempts at a 100 ms `Thread.Sleep`, and its comparison
   is identical (`error != 32 || attempt == 5`).
6. **Witnesses and mutants.**
   - At the red commit (37bba23ab, lease e5e90264): 45 total, 10 failed (8 Transient and 2 Lasting), 35 passed.
   - At the fix (bytes equal to de6d0392c, lease fce4ed80): 45 of 45 passed.

   | Mutant (WorktreeRenames.cs) | Build lease | Run lease | Result |
   |---|---|---|---|
   | M1 drop `Windows &&` | e86c5acc | 5e345d08 | red, 4 failed (SharingCodesAreOnlyTransientOnWindows ×4) |
   | M2 retry every IOException or UnauthorizedAccessException | a8d87ca1 | 6620e122 | red, 8 failed (LastingRenameErrorIsNeverRetried ×8) |
   | M3 no attempt bound | d59cfd33 | 9866e619 | not green: LastingSharing hangs; session cancelled at `--timeout 90s`, exit 1 |
   | M4 `attempt <= SharingAttempts` | aa6baffd | 7a4fac0a | red, 2 failed (LastingSharing ×2) |

   All mutants were restored, and the worktree was returned to its clean HEAD.
7. **Affected classes** at the fix bytes, 785 total, 773 passed, 0 failed, 12 skipped:

   | Run | Lease | Total | Passed | Skipped |
   |---|---|---|---|---|
   | A | fa031c42 | 463 | 452 | 11 |
   | B | 277250c3 | 322 | 321 | 1 |

   Per-class counts come from the TRX files:
   - LandingRemovalPolicyControlTests 94
   - WorktreeGuardedCleanupTests 49
   - C0824DesktopLegacyRelease Policy 120, Settings 32, Native 7 (all skipped)
   - DelegationWorktreeTests 49
   - TaskWorktreeRetirementTests 40
   - WorktreeResidueSweepTests 15
   - CompletedCardWorktreeRemovalTests 37
   - AgentTaskWorktreeLockOutcomeTests 20
   - AgentTaskLandAdoption 21 + 7
   - Monitoring 25, Request 30, TargetRace 11 + 6
   - AbandonedLandingReconciler 7, LandRefRetention 39, LandReplacementApproval 5
   - LandingProtocolGuard 23, LandingProtocolHarness 18, StageOutcomeSummary 5
   - RemovalMatrix 52, BareRepositoryCleanup 15, CheckpointWorker 2, CheckpointMatrix 41
   - RegistrationSafety 7, NestedRepositoryCleanup 8

   Code's 730 tests are all included, plus TaskWorktreeRetirement and WorktreeResidueSweep (55 more).
   The 12 skips are the same Windows-only cases Code listed (C721, C665 ×3, C448_V20 windows-file-handle, C0824 Native ×7).

   Gates:
   - check-evidence-diff over 1ef88240b..33af3d5c7: commits=3, violations=0, exit 0.
   - `git merge-tree --write-tree origin/master 33af3d5c7` (master 5a95ae7a5): clean, tree d1aac9a20.

## Advisories (no Review rerun needed)

- A-1: `Pause` is `Thread.Sleep` on an async path, as OpenNoFollow already does. It blocks a pool thread for
  up to 400 ms per rename and cannot be cancelled. The Publication retry pass can overrun its 10 s extra
  budget by up to 0.8 s before the next `CheckBudget` call.
- A-2: The window between the final authority read and the rename grows by up to 0.8 s. The race is the same
  kind as before (a holder releases just before the move); it adds no new kind of race.
- A-3: A lasting Windows ACL ERROR_ACCESS_DENIED is retried 4 times (up to 400 ms), then refuses exactly as before.
- A-4 (for Mutation): mutating the match to `(ex.HResult & 0xFFFF) is 32 or 33 or 5` survives the current
  witnesses, which have no foreign-facility HResult with low word 5, 32 or 33. Production code is correct;
  this is candidate M-h.

## Scope

- The Unit lane was not run. The brief accepts an operator-approved Interim scope for master-only work,
  so `ordinaryScopeCompleted` is Interim.
- No checkpoint plan exists, so there are no SHA-validated receipts and `reviewedSourceClean` is false.
- All runs were direct build-slot runs on a tree byte-identical to de6d0392c for server/ and tests/.
- Owed: the 12 Windows-only real-sharing witnesses on the desktop lane.
