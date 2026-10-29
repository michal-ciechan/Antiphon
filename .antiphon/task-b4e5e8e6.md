# Review b4e5e8e6: pty-host publication retry (subject bf53504b)

Verdict: **sound, no defects.** The retry does not mask a concurrency defect and cannot publish a partial or wrong binary. Scope: **Interim**, as commissioned. This was a narrow Linux selection; the Unit lane and E2E were not rerun, and the native Windows evidence could not be reached from server2.

Reviewed source: `ddd809cb30e47cf56feb3365a417cb8cca7bd048` (origin `feat/card-task-bf53504b`), base `5e9514d0253aad1ee3a14a5b2766fb39702bd824`. Product diff: `src/Antiphon.PtyHost.Client/ShadowCopyStore.cs` +21/-1. Test diff: the new file `tests/Antiphon.PtyHost.Tests/ShadowCopyStoreTests.Windows.cs`. No existing test file changed.

## Checks

1. **Masking (no).** The staging dir is GUID-unique (`ShadowCopyStore.cs:59`). Only `CopyRecursive` writes to it, and `File.Copy` closes every handle before the rename. Launches in one launcher are serialized by `_gate` (`PtyHostLauncher.cs:24-29`). Each E2E runner owns its own `binRoot`. So no in-repo publisher, earlier pty-host or sibling session can hold a file under `.copying`. The holder has to be an outside process, such as a scanner, indexer or something enumerating the directory. The retry runs only after the copy has completed, so it never changes which bytes get published.
2. **Publication contract (holds).** `Directory.Move` is a same-parent NTFS rename. It is atomic and never replaces an existing destination. If another publisher wins in the meantime, the rename fails with `0x800700B7` (already exists). That code is not retried, so the outer `catch (IOException) when (Directory.Exists(finalDir))` (`:66`) reuses the winner if its payload is intact. If a winner somehow showed up as access denied instead, the retry ends after 900 ms and goes down the same reuse path. Leftover `.copying` dirs never match the `*-{sha8}` reuse lookup (`:42`), so they cannot be launched. See A-1 for post-copy hash verification, which is a pre-existing gap.
3. **Retry scope (narrow).** It requires Windows, an `IOException`, and HResult `0x80070005`. `UnauthorizedAccessException` is not an `IOException` subclass, so it is not caught. Sharing violations (`0x80070020`) are not retried. There are 10 attempts with 9 × 100 ms sleeps. After the last one the original exception propagates, which is the same outcome as before. There is no catch-all and nothing is swallowed. See A-2 (blocking sleep, no cancellation) and A-3 (900 ms is not measured).
4. **Witnesses (not weakened).**
   - The transient-handle test is native-only. It first proves the denial on a raw `Directory.Move` (`ShadowCopyStoreTests.Windows.cs:27-28`), then requires publication once the handle is released. It goes red without the retry: the author recorded a product red at `1985698d5`.
   - The persistent-denial test is native-only. It checks the < 3 s bound and that the original HResult is preserved.
   - The non-overwrite control runs on Linux, but it passes with or without the retry. It is a control, not a retry witness.
   - Nothing on Linux witnesses the retry. The retry is gated by `OperatingSystem.IsWindows()`, and no injection seam was added, by design.
   - The native counts (14 passed, 15 lock/release cycles, six passing split-46 runs) are the author's. Their TRX files are under `C:\Antiphon\worktrees\card-task-bf53504b\.antiphon\task-bf53504b-evidence` and I did not re-verify them.
   - The new tests use the same Windows skip pattern as `PtyHostLauncherTests` and `ParentDeathSpikeTests`. See A-4 for a gap in pinning the narrowing.
5. **Alternatives.** A unique staging name, an in-process lock and identical-content reuse are already in place. What remains is a third-party handle on a rename, and a bounded retry is the standard remedy. The retry is not the weaker option.
6. **Names, roster and census are unchanged and green.** See the table below.

Portability: the base copies of `ShadowCopyStore.cs` and `ShadowCopyStoreTests.cs` are byte-identical to `origin/master` and `origin/rc/2026-10-27`, and the new test file does not exist on either. So the four src/test commits pick cleanly onto both branches. A whole-branch merge into rc/2026-10-27 is clean. A whole-branch merge into master conflicts, matching the 20 conflicts the author reported; that is outside the fix files. `check-evidence-diff` over base..HEAD found 6 commits and 0 violations.

## Runs (server2, Linux, source `ddd809cb3`)

There was one build slot for both projects (UseAppHost=false, `bin-b4e5e8e6/`, 148 s held). The tests then ran in a second slot (1726 s waited, 76 s held) using `dotnet exec`.

| Selection | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| PtyHost `ShadowCopyStoreTests` + `TestClassificationGuardTests` | 16 | 14 | 0 | 2 (both `Windows_publication_*`, native-only) |
| `RcRosterRepairTests` (Mirror, Explicit, Costs) | 3 | 3 | 0 | 0 |
| `WindowsUnitPlatformAdmissionTests` C1210 | 5 | 4 | 0 | 1 (`C1210_Windows_linux_only_outcomes_are_exact`, needs Windows) |
| Census C1404, C1577, C1584, C1410, G2865 | 5 | 5 | 0 | 0 |

TRX files are in `.antiphon/review-b4e5e8e6-evidence/` and are gitignored:
- `ptyhost/ptyhost.trx` sha256 `b73ee1b4…cd626`
- `scripts/scripts.trx` sha256 `23e94c23…6b2f`

I removed all 36 `bin-b4e5e8e6` directories by literal path.

Only native Windows can run: `Windows_publication_survives_a_transient_payload_handle`, `Windows_publication_preserves_a_persistent_access_denied_failure`, `C1210_Windows_linux_only_outcomes_are_exact`, and the split-46 E2E.

## Advisories (non-blocking)

- **A-1 (pre-existing, integrity).** `sha8` is computed from the source (`:39`) before the copy reads that source again (`:62`). A rebuild that races the staging copy can publish content under a name whose hash does not match it, and later calls reuse that name. Fix: hash the staged dir before publishing and refuse on mismatch. The retry does not widen this window.
- **A-2.** `Thread.Sleep` blocks a pool thread for up to 900 ms. That thread is reached synchronously from `LaunchDetachedAsync` (`PtyHostLauncher.cs:57`) and holds `_gate`. The path also has no `CancellationToken`, which predates this change. This is consistent with the synchronous hash and copy that already run under the lock. It is acceptable, but it is the reason the bound has to stay small.
- **A-3.** The 900 ms bound is a chosen budget, not a measurement. The original holder's duration is unknown and the test releases the handle after 150 ms. A hold longer than 900 ms still fails closed, with the same HTTP 500 as before.
- **A-4 (for Mutation).** No witness goes red if the HResult/OS filter is widened to every `IOException`. The non-overwrite control would still pass, only slower. A Windows-native PC could fix this: assert that an already-exists rename throws in < 100 ms. The PCs stay pending.
- **A-5.** Attribution is still unproved, as the author says. The original message `Access to the path '…copying' is denied.` has the same text as an `UnauthorizedAccessException` from `CreateDirectory(stagingDir)`, and the retry would not cover that case. Only the exception type, which was not retained, would tell the two apart.
