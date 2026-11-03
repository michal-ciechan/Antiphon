# Review CARD-1674 round 1 (full): alarm loop no-spin guard

Verdict: clean. No D-n found. Subject feat/card-task-a382d6ef at 15f1bfffeedbf2ff0046e14f2c501925c3f534a4 (base master 7b920d84aae97139998f940ee2cce3f457bb6e70); every run below built that exact tree (worktree clean before each build).

## Findings by brief item

1. Invariant holds. The coordinator publishes a drain expiry only when `overran is null`, i.e. `expires >= now`, so after a successful pass every published `overrunAt = expires + 1s` is greater than `lastPass` (`now`). The guard `overrunAt > lastPass` drops only stale values: an expiry left in the snapshot by an earlier pass when this pass threw before it republished. Those cannot lose a wake, because the failure path calls `wake.RequestSweep()`, so the next `WaitAsync` returns at once and the next pass re-evaluates (with the existing 2..60 s backoff after the second failure). Boundaries: at the inclusive bound (`expires == now`) the expiry is published and wakes at +1s (C1671 witness green). An expiry exactly 1 s before the pass (`overrunAt == lastPass`) is excluded, and witness 2 pins that case. A `>=` mutant would make the wait 0 and turn that witness red. Several drains: the minimum over the live ones. Retired, cleared or future-start drains are never published (ExpiresAt is null for retired or missing start; a future start has `overran` non-null). If the pass runs past an expiry that was live when the pass started, `WaitAsync` returns at once one time; the next pass sees the drain as overrun and stops publishing it. That is a single catch-up, not a spin. A cancelled pass: an OCE on `stoppingToken` is not caught and ends the loop; any other OCE counts as a failed pass, and its time is recorded as `lastPass`. Clock: `TimeProvider.GetUtcNow()` throughout. A backward clock jump only moves `lastPass` earlier, so no spin.
2. No timing change. `now` moved one statement earlier, ahead of `wake.Take()`, which cannot throw. Every use inside the pass is unchanged: the sweep test, `nextSweep = now + SweepMinutes`, journal evaluation and EvaluateRunnersAsync. Grace is now read once per wait and passed in instead of read inside NextDue. The recovery and backoff paths are untouched. The diff touches only RunnerAlarmHostedService (+ tests, bootstrap.md).
3. The pending-raise exemption is correct. After a successful pass an unraised episode has `now - DownSince < grace`, i.e. `raiseAt > now`. The one exception is a newly created episode whose `DownSince` is an older disconnect: its `raiseAt` can already be past, which gives one immediate re-pass, and that pass raises it. If the raise throws (pinned/live DB read), the per-runner catch rethrows at the end and the failure path's backoff applies. An episode for a disabled or removed runner cannot persist: PhoneHomeRunnerDirectory slots and `Entry.Enabled` are fixed at construction (IOptions; never reassigned or removed), and the in-memory state starts empty each process. SweepMinutes and RunnerGraceSeconds are validated as greater than 0, so nextSweep cannot spin either.
4. Docs match the code. A missing or future start fails safe (`Overran` returns non-null and an offline runner alarms), it is never published as an expiry, and the loop wakes once past a live bound and never for an expiry at or before its last pass.

## Runs (server2, each run in its own build-slot lease; UseAppHost=false; TEMP=/tmp)

- Build at 15f1bfffe: succeeded (3m52s).
- The 8 alarm classes (AlarmSettingsValidatorTests, RunnerAlarmAttentionTests, RunnerAlarmWiringTests, RunnerAlarmCoordinatorTests, RunnerStateAlarmExclusionTests, RunnerAlarmDeliveryTests, RunnerAlarmNotifierTests, RunnerAlarmHostedServiceTests): total 47, failed 0, skipped 0 (1m21s). The TRX contains both C1674 witnesses and all C1671 witnesses, every one Passed.
- RunnerAlarmHostedServiceTests three times in one lease: 9/9, 9/9 and 9/9 (20.7 s, 24.5 s and 20.7 s). The C1674 loop witness took 3.20 s, 3.66 s and 4.25 s.
- Mutant M1: dropped `overran is null &&` at RunnerAlarmCoordinator.cs:77, rebuilt, hosted-service class.
  - Run 1: 2 failed (C1674 loop witness and the pre-existing a_failing_pass_is_logged_and_retried_without_waiting_for_the_sweep). Both timed out at a 5 s wait on a host with load average 34. The C1674 failure was at line 239 (`FirstTimer` wait), which runs before any behaviour M1 changes, so it is not valid red.
  - Run 2: 8/9, with C1674_a_reachable_drain... red at `DrainExpiresAt should be empty but had 1 item`. That is the correct red.
- Mutant M2: removed the `overrunAt > lastPass &&` guard, rebuilt, hosted-service class: 8/9, with C1674_a_drain_expiry_not_after_the_last_pass... red at `due - lastPass should be 00:15:00 but was -00:09:59`. That is the correct red.
- Both mutants were restored with `git checkout -- <exact path>`; `git status` was clean afterwards.

## Gates

- check-evidence-diff over 7b920d84a..15f1bfffe: commits=2, entries=0, violations=0, exit 0.
- merge-tree onto origin/master 74178698ea316af2902c5ef653e8ecedecf3c698: clean (tree d030143dc8bc9ba7694d053c9fe4b18180a37eff). Master has not touched the alarm files since the base.
- rc/2026-11-01 (10a826b9d61f2c2cdfa29a43cac59fe360c99ea9) lacks CARD-1663, CARD-1666 and CARD-1671 (0 commits on rc; 7, 2 and 2 on master), and rc's RunnerAlarmHostedService has no DrainExpiresAt. A chained `--merge-base` pick of the two commits conflicts in RunnerAlarmHostedService.cs, RunnerAlarmHostedServiceTests.cs and bootstrap.md. An rc port needs CARD-1663, CARD-1666 and CARD-1671 first.

## Scope note

Ran the brief's commissioned selection: the 8 alarm classes and both mutants. I did not run the profile's whole Unit lane: no CARD-1674 plan or `### Checkpoints` exists, and the lane takes about 3 h on server2, against a 45-minute budget. There are no checkpoint-tool receipts, so reviewedSourceClean=false (TRX from build-slot runs only). PCs stay pending.

## Advisories

- A-1: The class-wide 5 s `FirstTimer` / `ExecuteTask` waits in RunnerAlarmHostedServiceTests (lines 54, 66, 88, 115, 135, 180, 207, 239; inherited from CARD-0726, d1949613f) are load-sensitive. On loaded server2 they flaked twice in one run (5.6 s and 6.5 s), including a pre-existing test. Unmutated, the witness passed 4/4. A follow-up should widen these waits to about 2x loaded p95 (for example 15 s) under the test-performance standards. This is not a CARD-1674 logic defect.
- A-2: `.antiphon/task-a382d6ef.md` is not on the subject branch, so the Code report was not read.

Build outputs deleted: 72 bin/obj directories, removed by exact path.
