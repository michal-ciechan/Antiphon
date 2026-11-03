# Review d2c3674f: CARD-1680 restart-apphost health window docs (round 1 of 3)

Subject: task 894ccf98-ee67-472c-b80a-193fa73e52bf, branch feat/card-task-894ccf98,
tip c04630cd75d0fc00431478b72cd47ed1923c4365, base master f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0.
Verdict: one defect (D-1). The new text itself is accurate against
scripts/restart-apphost.ps1 and scripts/apphost-common.ps1 at the tip.

## Verified claims
1. Defaults -TimeoutSec 150, -MaxWaitSec 600; hard cap = Max(MaxWaitSec, TimeoutSec) from launch (restart-apphost.ps1:66-67, :298-299). Correct.
2. Progress = length/mtime change of apphost.log, launcher .out/.err, dashboard URL file; port 17202 listening change; non-200 /health. Not credited once /health answers 200 (:357-364, apphost-common Get-AppHostLaunchSnapshot/Progress). Correct.
3. Exit 1 covers build failed, launcher nonzero exit, launched AppHost gone with nothing on 17202 (both checked each poll while backend unconfirmed, :339-349), no-progress timeout, hard cap; summary names limit and last progress with age (:390-395). Exits 0/3/4/5 unchanged. Correct.
4. Lock: removed on exit 0, BuildFailed, launcher nonzero exit and vanished AppHost before health; kept on no-progress/hard-cap timeouts (dashboard-up and else branches), exit 4, exit 5 (:323-436, finally :444). New runbook row and bootstrap Gotcha #11 match.
5. Rotation .1..10 (Move-AppHostLogHistory); pid file only counts if its mtime differs from the pre-launch stamp (Get-AppHostLaunchedPid). Correct.

## D-1 (blocking): stale lock-removal paragraph contradicts the new lock row
- Where: docs/apphost-runbook.md:312-316 (dead-launch recovery section).
- Failure: it still says "At health timeout, only when backend health has not been confirmed and the launcher is known to have exited nonzero does the script remove this restart's own stamp ... A successful detached exit, running launcher or unreadable exit still retains it." Since CARD-1680 the script also removes the stamp, immediately and not at timeout, when this launch's AppHost exits with nothing on 17202 - including after a successful (exit 0) detached launcher exit (restart-apphost.ps1:341-348). The same document's lock row (:169) now says the opposite.
- Why: this is the lock-retention statement an operator reads before deciding whether a retry is admitted; two contradictory answers in one runbook.
- Fix: reword to: before backend health is confirmed, a nonzero launcher exit or this launch's AppHost exiting with nothing on 17202 ends the wait at once, removes this restart's own stamp, prints both captured paths and exits 1; a no-progress or hard-cap timeout with a running launcher, successful detached exit or unreadable exit retains it.

## Advisories
- A-1: neither doc says rotation falls back to emptying apphost.log when a move fails (history may then be missing).
- A-2: "port 17202 opening" - any listening-state change (also closing) counts as progress.

## Checks (tip c04630cd7)
- Build: Antiphon.Tests via build-slot (lease e33b4416, waited 15s, held 182s), 0 errors.
- Tests via build-slot (lease f4d6c192): StandingPipelinePolicyDocumentationTests 12, KeepSixDocumentationTests 3, GrokRunnerImageContractTests 26 = 41 passed, 0 failed, 0 skipped (TRX).
- scripts/test-os-witnesses.ps1 -PolicyOnly: PASS policy-surface, 26 assertions, exit 0.
- check-evidence-diff f32aed42e..c04630cd7: commits=1 entries=0 violations=0, exit 0.
- merge-tree onto origin/master f32aed42e: clean, tree 55d98564aaaa65bb93725a65495a645f5a3b8a6a.
- Other living docs grepped (150, TimeoutSec, fixed, health wait): only D-1; historical plans/investigations not counted.
