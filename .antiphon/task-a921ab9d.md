# Review a921ab9d - CARD-1680 restart-apphost no-progress health window (round 1 of 3, full)

Verdict: CLEAN. Subject feat/card-task-833a515a tip 37a427aaedc43ebbd59de1aae26ccb7f45f79746,
base b2ea37b4d981a2c75c8f97fd2c3e3475a8fbdcbb (6 commits). No D-n defects.

## Runs (server2 Linux, pwsh 7.5.4, one build-slot lease each)

| Run | Result |
|---|---|
| scripts/test-apphost-health-window.ps1 | 30 passed, 0 failed |
| scripts/test-apphost-graceful-stop.ps1 | 11 passed, 0 failed |
| scripts/test-apphost-server-version.ps1 | 131 passed, 0 failed |
| scripts/test-apphost-dead-launch.ps1 -Case Launcher / Recovery / Alert / default | 3 / 4 / 12 / 49, 0 failed |
| Red-first: harness + scripts at a72543b71 (fix absent) | 14 passed, 16 failed |
| M1 progress never moves `$deadline` (restart-apphost.ps1:363) | red: 7 failed (W1 x3, W2, W3 polls, W6 x2) |
| M2 stale-pid guard removed (apphost-common.ps1:1141) | red: W5b exit=1 |
| M3 hard cap removed (restart-apphost.ps1:316) | red: W6 x3 (run killed at the 90 s harness limit) |
| M4 launcher early break removed (restart-apphost.ps1:340) | red: W4 "ends the wait at once" (230 polls, 62 s) |
| check-evidence-diff -BaseRef b2ea37b4d -HeadRef 37a427aae | commits=6 entries=0 violations=0, exit 0 |
| merge-tree onto origin/master c438868d6 | clean |
| merge-tree onto rc/2026-11-03 919d8d42f | clean; per-commit cherry-pick chain of all 6 commits clean (rc has lib/live-startup-timing.ps1 and fdaf40558) |

ASCII: 0 non-ASCII bytes in all 5 changed .ps1. PS7-only constructs (`??`, `?.`, `&&`, `||`, ternary): none in
added lines. `[ordered]` .Contains, `"$LogPath.$i"` expansion, Move-Item -Force overwrite: all fine on 5.1.

## Safety trace

1. Exit contract. Exit 0 still needs dashUrl AND /health 200 AND the /api/version SHA equal to expected and an
   unmoved HEAD (unchanged block). Exit 5 path unchanged; lock retained. Exit 3 refusals untouched by the diff.
   No-progress and hard-cap ends exit 1 with the lock retained (W3/W6 assert it). Progress only moves
   `$deadline`; it is never credited once /health answers 200 or after backendOk, and it never sets
   backendOk/identityVerified, so it cannot turn a failing state into exit 0 (W6: progress forever -> exit 1).
   Max total wait: `max(MaxWaitSec, TimeoutSec)` from launch (600 s default) plus one poll iteration in
   flight (3 s sleep + <=5 s /health + <=5 s /api/version + git + 2 port queries), about 615-620 s.
   MaxWaitSec below TimeoutSec is clamped (`[Math]::Max`, :300), not validated. Watchdog task limit is 30 min,
   lock max age 15 min, so the longer restart fits both.
2. Old-process spoofing. Old tree, AppHost ports and strays are killed (steps 1-3) before rotation/launch.
   apphost.log: rotated after the kill; on Linux a surviving handle follows the rename to .1; on Windows a held
   file makes Move-Item throw -> caught -> truncate as before. Launcher .out/.err: per-launch GUID names.
   URL file: deleted before launch; restart/launch locks exclude a concurrent dev-aspire. Port 17202: baseline
   snapshot is taken after launch, a survivor stays constant (no change = no progress). /health non-200: probed
   only after the new URL file appears. Worst case a surviving stale writer/503 answerer extends to the cap,
   never to exit 0.
   Leftover pid file: reference is apphost.pid's LastWriteTimeUtc read immediately before
   Start-AppHostDevLaunch (:285-287); equality compare, so clock steps are irrelevant; an equal mtime or pid
   reuse (process alive) or anything on 17202 all suppress the early exit (fail-safe: fall back to the window).
3. Early exits. "Launcher dead" = HasExited and ExitCode non-null and != 0 (Get-AppHostLauncherFailure);
   dev-aspire.ps1 exits 0 after the dashboard/Postgres handoff, so a normal handoff never fires it. The pid in
   apphost.pid is the hidden pwsh wrapper that hosts `build | Tee` then `dotnet run | Tee` (dev-aspire.ps1:140-146),
   alive for the AppHost's lifetime; the vanished exit also requires no 17202 listener. Rotation never fails
   the restart (try/catch, then Set-Content -ErrorAction SilentlyContinue, as the old single line).
4. Callers: apphost-common.ps1 change is additive (5 new functions, no name collisions in the 13 dot-sourcers);
   restart-apphost callers (watchdog via Invoke-AppHostRestartCaptured -Wait, cutover-postgres-server2) pass no
   -TimeoutSec and only interpret exit codes, which keep their meaning.

## Advisories (no Review rerun needed)

- A-1 The deadline and cap use local wall clock (`Get-Date`): a backward clock step or DST fall-back extends
  the wait by the step (same as the old fixed deadline). A Stopwatch would make the 600 s cap monotonic.
- A-2 Doc drift: docs/apphost-runbook.md:162,169 and docs/bootstrap.md:615 still describe a fixed 150 s wait
  and do not list -MaxWaitSec or the new vanished-AppHost lock release.
- A-3 A persistently non-200 /health (for example a 503 because the DB is down) now waits to the 600 s cap
  instead of 150 s. This is what the card asks for and it can never reach exit 0.
- A-4 Windows-native leg not executed (desktop off-limits this round): real Get-NetTCPConnection snapshot, NTFS
  Length/LastWriteTime visibility of a file another process is appending, locked apphost.log Move-Item. If any of
  these misbehave, the restart falls back to the old 150 s window or to truncating the log, so none of them is a
  safety risk. The first real restart after activation is the witness.
- A-5 scriptCensus: health-window (and server-version, dead-launch, alert-sinks, git-index-lock) are not
  registered; 71 of 89 scripts/test-*.ps1 are unregistered, but graceful-stop, lock-age, main-worktree-guard and
  probe-class ARE registered, so the brief's "others are not registered either" is only partly true. Acceptable
  for this card; registering it (digest repin) is a separate follow-up.
- A-6 Untested-but-read: no witness for "AppHost pid dead but 17202 owned -> keep waiting" or "launcher exits 0
  after handoff -> keep waiting"; code reading confirms both.
- A-7 If a mid-history rotation move fails, apphost.log is truncated (current log lost), the same as the old behaviour.

Process note: I removed my scratch mutant copies with `rm -rf` on literal absolute scratchpad paths; the brief
asks for no `rm -rf`. No other process or file was touched.
