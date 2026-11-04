# Mutation sweep: bounded sharing retry for the guarded removal (C448 V36)

- **Task:** Mutation d2564da4, branch `feat/card-task-d2564da4`, server2 (Linux), 2026-11-04.
- **Subject:** L = master `84e19071e25abc7a595fddfac145b187dc5f2d11`. It is the batch-65 landing of Code f5e96e75 (red `37bba23ab` → `e05d39d56`, fix `de6d0392c` → `84e19071e`), reviewed by Review 577b2ee0 (round 1 clean, Interim).
- **Code under test:** `server/Infrastructure/Git/WorktreeRenames.cs` (the `Retrying` loop, `IsSharingConflict`, `SharingAttempts`/`SharingPause`, `System`), `WorktreeNoFollowDelete.MoveAside`, the rename in `WorktreeSetAside.Record`, `OpenNoFollow`'s use of the constants, and the `GuardedWorktreeRemoval.Renames` default.
- **Witnesses:** `LandingRemovalPolicyControlTests` `C448_V36_*`, 45 cases (transient 8, lasting 2, never-retried 8, not-Windows 4, plus the earlier V36 cases).

## Method

- **One mutant build.** Each mutant sits behind the environment variable `MUT_C448=<id>[,<id>...]`, so one Debug build of `Antiphon.Tests` (`-p:UseAppHost=false`, native output) serves every mutant. It took lease `f89b8af3`.
- **One lease per run.** Each run is `dotnet run --no-build` under its own build-slot lease, with the filter `/*/*/LandingRemovalPolicyControlTests*/C448_V36_*`.
- **Switches-off baseline.** On the mutant build with `MUT_C448` unset, the V36 cases passed 45 of 45 (lease `6edfcaeb`).
- **Survivors packed.** All ten survivors ran together over the brief's affected classes, `(LandingRemovalPolicyControlTests*)|(C0824DesktopLegacyRelease*)|(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)|(DelegationWorktreeTests*)`. Result: 357 total, 348 passed, 0 failed, 9 skipped (Windows-only). Lease `acea7e43`.
- **Restoration.** All four edited files were restored with `git checkout -- <exact path>`, and the tree was rebuilt (lease `8676c18a`). At the restored bytes:
  - the V36 cases passed 45 of 45 (lease `031b2f20`);
  - the affected classes gave 357 total, 348 passed, 0 failed, 9 skipped (lease `44942a80`).

  The tracked source and the index are clean at L.

## Results

| ID | Mutant | Verdict | Red tests (count) and the failing assertion |
|---|---|---|---|
| M1 | Windows guard deleted from `IsSharingConflict` | **killed** | `SharingCodesAreOnlyTransientOnWindows` move/record × sharing/denied (4): `Residue` should be the refusal, but the retried rename succeeded |
| M2 | Retried set widened to every `IOException`/`UnauthorizedAccessException` | **killed** | `LastingRenameErrorIsNeverRetried` all 8: `RenameCalls` should be 1 |
| M3a | Narrowed: 33 (lock violation) dropped | **killed** | `TransientSharing…ToAClean` move/record × lock (2): `IsClean` should be True |
| M3b | Narrowed: 5 (access denied) dropped | **killed** | `TransientSharing…ToAClean` move/record × denied, denied-io (4): `IsClean` |
| M3c | Narrowed: `UnauthorizedAccessException` dropped | **killed** | `TransientSharing…ToAClean` move/record × denied (2): `IsClean` |
| M4 | HResult compared on the LOW WORD only (`(HResult & 0xFFFF) is 0x20 or 0x21 or 5`) | **SURVIVED** | none; a witness is missing (below) |
| M5a | Attempt bound dropped (`int.MaxValue`) | **killed (hang)** | `LastingSharing…StillRefuses`: no result within the 420 s run timeout (exit 124). M5d is the assertion-red proxy. The leftover lease `a1b3940a` was released by its exact ID. |
| M5d | Bound raised to 50 (effectively unbounded) | **killed** | `LastingSharing…StillRefuses` move/record (2): `RenameCalls` should be 5, but was 50 |
| M5b | Off by one low (4 attempts) | **killed** | `LastingSharing…StillRefuses` (2): `RenameCalls` should be 5 |
| M5c | Off by one high (6 attempts) | **killed** | `LastingSharing…StillRefuses` (2): `RenameCalls` should be 5 |
| M6 | Last attempt retried once more, without a pause | **killed** | `LastingSharing…StillRefuses` (2): `RenameCalls` should be 5 |
| M7a | Pause of 0 ms | **killed** | `TransientSharing…ToAClean` 8 and `LastingSharing…` 2 (10): `Pauses` should be `[00:00:00.1]` |
| M7b | Pause call removed | **killed** | the same 10: `Pauses` |
| M7c | `SharingPause` constant 100 → 200 ms | **SURVIVED** | none; a witness is missing |
| M7d | `SharingAttempts` constant 5 → 6 (shared with `OpenNoFollow`) | **SURVIVED** | none; a witness is missing |
| M8 | After the last attempt, both sites throw a different refusal (a wrapping `IOException`, HResult `COR_E_IO`) | **killed** | `LastingSharing…StillRefuses(move)` (1): `GeneratedCode` should be `move_error_32` |
| M8r | The same, at the Record site only | **SURVIVED, equivalent** | the proof is below |
| M9 | After the retries, the refusal is rebuilt as `new IOException(message, same HResult)` (type and message change, code kept) | **SURVIVED** | none; a witness is missing for `UnauthorizedAccessException` |
| M10 | Failure timestamp (`failedAt` and `Outcome.At`) stamped BEFORE `MoveAside`'s retries | **SURVIVED** | none; a witness is missing |
| M11a | `MoveAside` not retried (Record still retried) | **killed** | `TransientSharing…ToAClean(move,*)` 4 and `LastingSharing…(move)` 1 (5) |
| M11b | Record not retried (`MoveAside` still retried) | **killed** | `TransientSharing…ToAClean(record,*)` 4 and `LastingSharing…(record)` 1 (5) |
| M12 | `OpenNoFollow` diverges from the shared constants (literal 6 attempts, 200 ms) | **SURVIVED** | none; reachable only on Windows, and no seam exists |
| M13a | Production `GuardedWorktreeRemoval.Renames` default replaced: `Pause` is a no-op | **SURVIVED** | none; equivalent on Linux, live on Windows |
| M13b | Production `Renames` default replaced: `Windows = false` | **SURVIVED** | none; equivalent on Linux, live on Windows |
| M13c | `WorktreeRenames.System.Pause` replaced by a no-op (no `Thread.Sleep`) | **SURVIVED** | none; unreachable on Linux, live on Windows |

**Totals:** 25 mutants. 14 were killed by an assertion and 1 by a hang (M5a). 10 survived:

- 1 is equivalent: M8r.
- 9 are coverage survivors: M4, M7c, M7d, M9, M10, M12, M13a, M13b and M13c. M13a and M13b share one missing pin.

There were no infrastructure failures and no defect in unmutated L.

## Survivors: missing witness or equivalence proof

1. **M4, the low-word match. A witness is missing.** No case throws a foreign error whose low word is 5, 32 or 33. The lasting set covers only 3, 0x91, 0xB7 and 0x10B.
   - **Missing case:** add to `C448_V36_LastingRenameErrorIsNeverRetried`, on Windows, a fault such as `new IOException("foreign facility", unchecked((int)0x80040020))` or `unchecked((int)0x800A0005)`.
   - **Expected:** one call and no pause, for both move and record. For move, the code is `move_error_32` or `move_error_5`, because the outcome itself takes the low word.
   - This is the Review 577b2ee0 advisory, now confirmed.
2. **M7c and M7d, the shared constant values. A witness is missing.** Every V36 assertion reads `WorktreeRenames.SharingAttempts` and `SharingPause` symbolically, so a changed value moves the expectation with it. The brief's "exactly 5 attempts" is not pinned anywhere.
   - **Missing case:** `WorktreeRenames.SharingAttempts.ShouldBe(5)` and `WorktreeRenames.SharingPause.ShouldBe(TimeSpan.FromMilliseconds(100))`.
   - Alternatively, write the literals in `LastingSharing…StillRefuses`: `RenameCalls == 5`, 4 pauses of 100 ms.
   - **Not equivalent:** the values bound the worst added delay (0.4 s per refused pass) that Review checked against the 10 s cleanup budget.
3. **M8r, the Record site's refusal altered. Equivalent.**
   - `WorktreeSetAside.Record` has one production caller, `GuardedWorktreeRemoval.RemoveDirectoryAsync` (the `Record(...)` line just before the `MoveAside` try).
   - That call sits outside the `MoveAside` catch. Any `IOException` or `UnauthorizedAccessException` from it reaches the method's outer catch (`… or ArgumentException or TimeoutException`).
   - The outer catch sets `failedAt` only when `outcome` is non-null, and `outcome` is still null there. It returns `Finish("cleanup_inspection_error")`, and `Detail` is `setAsideDetail`, which is assigned only after the move and so is null.
   - The exception's type, message and HResult are therefore never observed, so any `IOException`-typed replacement is indistinguishable. Only a non-I/O exception type would escape, and the mutant does not produce one.
4. **M9, the refusal rebuilt with the same HResult. A witness is missing for `UnauthorizedAccessException`.**
   - For an `IOException` input it is equivalent at the only observer: the move catch reads only `HResult` (giving `move_error_<low word>`) and `GetType().Name`, and both are unchanged.
   - For an `UnauthorizedAccessException`, `Outcome.ExceptionType` changes from `UnauthorizedAccessException` to `IOException`. That value is the persisted cleanup diagnostic, so the mutant is not equivalent.
   - **Missing case:** add `[Arguments("move", "denied")]` (a lasting `UnauthorizedAccessException`) to `C448_V36_LastingSharingOnSetAsideRenameStillRefuses`. Assert `Outcome.GeneratedCode == "move_error_5"` and `Outcome.ExceptionType == "UnauthorizedAccessException"`.
   - Today the lasting cases are only `sharing` (an `IOException`).
5. **M10, failure time stamped before the retries. A witness is missing.**
   - The fixture's `Pause` only records (`Pauses.Add`) and the pass runs on `TimeProvider.System`, so no time passes during the retries.
   - **Missing case:** a lasting move case driven by a `FakeTimeProvider` whose injected `Pause` advances that clock by the requested span. Assert that `pass.FailureTimestamp` and `pass.Outcome.At` are both taken after the fourth pause; with the default constants that is 400 ms after the start.
   - **Not equivalent:** `WorktreeGuardedCleanup` computes `failureStart = first.FailureTimestamp`, so an early stamp spends up to 0.4 s of the 10 s extra budget. That is exactly what the Code report says does not happen, and nothing proves it.
6. **M12, `OpenNoFollow` diverging from the shared constants. A witness is missing; the code is reachable only on Windows.**
   - `OpenNoFollow` is a `CreateFileW` P/Invoke reached only through `DeleteWindows`, and it has no seam.
   - On Linux it is unreachable. The 12 Windows-only skips hold handles for good and cannot count attempts, so even a Windows run would not kill this mutant.
   - **Missing case:** route `OpenNoFollow`'s attempt and pause through the injectable `WorktreeRenames` (`Pause`, plus an open seam). Alternatively, add a Windows-gated test that holds an entry open for about 250 ms, then releases it and expects the delete to succeed, together with the M7 pin.
   - **Not equivalent:** the divergence changes the native delete's bound.
7. **M13a and M13b, the production `Renames` default replaced. Equivalent on Linux; a witness is missing for Windows.**
   - Every V36 fixture sets `Renames` explicitly, so the `GuardedWorktreeRemoval` initializer is never exercised by a witness. The other classes run on Linux, where `System.Windows` is already false.
   - On Windows these mutants silently remove the fix: no pause, or no retry at all.
   - **Missing case:** one platform-independent test, `new GuardedWorktreeRemoval(git, leases, evidence).Renames.ShouldBeSameAs(WorktreeRenames.System)`. This kills both.
8. **M13c, `System.Pause` reduced to a no-op. Unreachable on Linux; a witness is missing.**
   - `System.Windows` is false on Linux, so `System.Pause` is never invoked. The existing pin covers only `System.Windows`.
   - **Missing case:** time `WorktreeRenames.System.Pause(TimeSpan.FromMilliseconds(50))` and assert it is at least about 45 ms. `Pause` does not depend on `Windows`, so this runs on Linux.

## Evidence

- **Logs:** per-run logs and summaries are in the session scratchpad, `…/scratchpad/evidence/`: `build-mutant.log`, `run-v36-<id>.log`, `run-classes-P1.log`, `build-restored.log`, `run-classes-restored.log`, `run-v36-restored.log`. The mutant switch diff is `mutant-switch.diff`.
- **Product code and tests:** none written.
- **Build outputs:** deleted after the restored runs.
