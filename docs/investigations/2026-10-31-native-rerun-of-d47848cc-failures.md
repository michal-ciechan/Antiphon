# Native Windows rerun of qualification run d47848cc failures (task e960ffeb)

Status: IN PROGRESS (interim; updated after each project).

- Subject: rc tip `fc064a68247b0186918dfb1f04465e6be0475389` (origin/rc/2026-10-27), desktop Windows 10 (19045).
- Old run: d47848cc (candidate rc cde8def41), 22,780 results, 58 failed / 31 causes.
- Precondition (2026-10-31): old supervisor PID 283840 and child PID 317412 not running;
  `commission-exit.txt` = 1; `C:\Antiphon\verification\native-run.lock` (the run's coordination
  root) absent, so the native lock was released. Nothing under `C:\Antiphon\releases` was changed.
- Method: per project, `scripts/run-checkpoint.ps1` rows (one build per project, later rows
  `-NoBuild` on the same stamped output), each row wrapped in the release-gate shared native lock
  (`Enter-ReleaseGateNativeLock`, RunId `native-rerun-e960ffeb`, lane `rc`). Filters are
  method-level ORs `/*/*/*/(Method*)|...` with an `-Expect` token per method. Windows default
  build arguments (normal native apphosts, as the qualification and the owed C1022/C1614 commands
  require), not `UseAppHost=false`.

## Results

| Test | Project | Before (d47848cc) | After (fc064a68) | Status | Evidence |
|---|---|---|---|---|---|
| HostCustodyTests.C1614_Windows_tracked_linger_releases_job | PtyHost.Tests (pty-host g1) | FAIL | PASS | FIXED | `CHECKPOINT e960-ptyhost` exit 0, executed=2 passed=2, `.antiphon/checkpoints/e960-ptyhost-20261031-104813-b9d5/run.trx` (6.95 s) |
| NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute | PtyHost.Tests (pty-host g2) | FAIL | PASS | FIXED | same row |
| C1022BackendLaunchTests.Configured_owned_host_runs_modern | SessionRunner.Tests (session-runner g4) | FAIL: `UnauthorizedAccessException: Access to the path 'Antiphon.PtyHost.exe' is denied` from `Directory.Delete(root)` (C1022BackendLaunchTests.cs:126), 1.56 s | FAIL 2/2: `UnauthorizedAccessException: Access to the path 'Antiphon.Agents.Pty.dll' is denied` at `RemoveDirectoryRecursive` <- C1022BackendLaunchTests.cs:116 (`() => Directory.Delete(root, true)`) <- OwnedProcessDirectoryCleanup.cs:79 <- TestOwnedPtyHost.ScopeAsync:245; 4.91 s | STILL FAILING (same cause) | `e960-sessionrunner-20261031-105121-70f1/run.trx`; repeat `e960-sessionrunner-r2-20261031-105537-80fa/run.trx` (whole class: 9 executed, 8 passed; siblings Unset_owned_host_runs_modern and Declared_modern_overrides_inherited_inbox PASS) |
| NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute | SessionRunner.Tests (session-runner g6) | FAIL | PASS | FIXED | `e960-sessionrunner-20261031-105121-70f1/run.trx` |

C1022 detail: the test body completed (ModernConPty host log, conpty hash and both capability JSON lines printed; host pid 390760, child 448268, OpenConsole 111940). All captured owned processes exited inside the 10 s budget (no AggregateException), then the owned delete of `%TEMP%\antiphon-backend-seam-<guid>` (staged copy `pty-hosts\bin\<stamp>\`) was refused on a file of the staged host copy. Checked about 1 minute later: the directory was gone and none of the three pids nor any process imaged from that directory was alive, so the hold was transient after process exit. Only the `Configured` case fails, deterministically; the C1022 `OwnedProcessDirectoryCleanup` repair (join owned handles before delete) does not cover whatever still holds the staged file at delete time (candidates: an uncaptured process in the session tree or a delayed image-section release; not established here).

| Test | Project | Before (d47848cc) | After (fc064a68) | Status | Evidence |
|---|---|---|---|---|---|
| NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute | Agents.Pty.Tests (agents-pty g3) | FAIL | PASS (0.84 s) | FIXED | `e960-agentspty-20261031-105650-f90a/run.trx` |
