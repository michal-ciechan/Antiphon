# Review CARD-1806 round 1 (Final, Full): restart-apphost dashboard probe

Subject: feat/card-task-acf70e92 tip d6ef7bb4d22123af2339d013817ac3385f45c0f1 (Code acf70e92; red 3e958b613, fix d6ef7bb4d, base a0e14b3e1).
Verdict: CLEAN, no blockers. One advisory (A-1).

## Checks

1. Exit 0 is not weakened. `$dashUrl` can now come from the 17205 probe, but exit 0 still requires
   `$backendOk` (/health 200 on 17202) and `$identityVerified` (/api/version SHA == expected and HEAD
   unmoved). The exit-5 path (wrong SHA, lock retained) and the exit-1 paths are unchanged. CARD-0495
   server-version passes 131/0. The dead-launch harness passes 49/0.
2. A foreign listener on 17205: the port is in `$appHostPorts`, so step 2 kills any owner before launch
   (the session-runner PID is spared). The only window left is a process that binds 17205 after the
   kill. It would make the probe report a dashboard, but it cannot reach exit 0 without a healthy 17202
   at the expected SHA. This is not a blocker.
3. Progress bound: the probe runs only while `$dashUrl` is unset. `$dashFound` is reset each poll and set
   only on the poll where the dashboard is first found, so progress is credited once. W12d ends at the
   2 s no-progress window ("no launch progress for 2s ... last progress: dashboard answered").
   The dead-launch harness passes 49/0.
4. The real probe body, run on server2 (Linux pwsh 7; `localhost` resolves to ::1 first, and the listener
   was bound to 127.0.0.1 as Aspire binds):
   - nothing listening -> Url null (169 ms)
   - 200 -> found
   - 401 -> found
   - 302 to /login (200) -> found; the follow sent no Authorization header
   - 302 to /login (401) -> found; no Authorization header
   - 500 -> found
   - a listener that accepts the connection but never answers -> Url null after 3130 ms, at the 3 s timeout
   - exceptions are caught (same pattern as Invoke-AppHostHealthProbe; no StrictMode)
   - plain http, so no TLS path
   - no non-ASCII bytes in restart-apphost.ps1, apphost-common.ps1 or the two harnesses (the dead-launch
     R-1 ASCII row also passes)
5. Red/green: W12 at the red commit 3e958b613 with the base script: 3 passed, 5 failed (exit 1). At the
   tip: 91/0. Docs: the apphost-runbook exit-0 row and no-progress paragraph, and the bootstrap
   dashboard line, are accurate.

## Runs (server2, Linux pwsh; each under its own build-slot lease, at d6ef7bb4d)

| Run | Result |
|---|---|
| test-apphost-health-window.ps1 (W1-W12, incl. W12d) | 91 passed, 0 failed |
| test-apphost-server-version.ps1 | 131 passed, 0 failed |
| test-apphost-graceful-stop.ps1 | 11 passed, 0 failed |
| test-apphost-dead-launch.ps1 | 49 assertions, 0 failed |
| W12 at red 3e958b613 | 3 passed, 5 failed (red as intended) |
| Guard batch (Antiphon.Tests, one treenode filter, TRX) | 52/52: TestClassificationPolicy 41, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3 |
| check-evidence-diff a0e14b3e1..d6ef7bb4d | commits=2 violations=0 |
| merge-tree vs origin/master 8acb630c6 | clean |

All harnesses run on Linux pwsh; none is Windows-only for these cases.

## Mutants (scratch worktrees at the tip)

- mA: delete the `dashboard answered on $dashFound` progress line. Caught by W12d ("the dashboard
  answering counts as progress"): 7 passed, 1 failed.
- mB: `if (-not $dashUrl)` -> `if ($true)`. The probe runs every poll and every answer counts as progress.
  This mutant SURVIVES (8/0): W12d ran to the 60 s hard cap instead of the 2 s window.
- M1 (no fallback) is equivalent to the red-base run above, and is caught.

## Advisory

A-1 (test adequacy; the product code is correct):
- Where: scripts/test-apphost-health-window.ps1, W12d.
- Failure: a regression that credits the dashboard's progress on every poll keeps a wedged launch (dashboard
  up, backend never healthy) alive to `-MaxWaitSec` (600 s by default) instead of `-TimeoutSec`, and W12d
  still passes.
- Why: W12d asserts exit 1, the "last progress" text and the lock, but not which limit ended the wait.
- Fix: in W12d, assert `$run.Text -match 'no launch progress for 2s'`, or assert that 'hit the hard cap'
  does not appear.
