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
