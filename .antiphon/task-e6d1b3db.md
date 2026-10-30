# CARD-1514 CP-8 loaded-host repair: incomplete verification

The specified test-only repairs and the V-6 advisory are implemented and pushed. Verification remains RED: CP-8 failed its retained FIFO in-process 0.2-second predicate, and two fully loaded repeats failed the unchanged FIFO launch witness. Do not merge to rc from this report. No product deadline was changed.

Source verified: 5e7a2dc4d3222e1405330e208aaac9b22d8a458f. Assigned base: 1f6065cce08bc5ee74014076c90b0855044bc236. Branch: feat/card-task-e6d1b3db. Linux/server2, TEMP=/tmp, host 4551c5a7852f.

## Changes and commits

- 499fda76b82bde219f7b2e9b499980131c80b980: raise FIFO/denied child startup waits from 5000 to 15000 ms; split exit, exit-code, in-process and unsafe-input checks; retain both 0.2-second checks. Assert foreign corpus size, actual journal bytes and a 0.75-second wall bound. V-6 publishes a successor chunk and byte-compares the complete seeded prefix against a legacy replay in torn/sealed/identity modes.
- 5109b4000e8d254e6bbd94cd4162df88afcb8516: correct the new byte counter to observe the active JSONL timing/header scanner streams.
- 5e7a2dc4d3222e1405330e208aaac9b22d8a458f: apply the same structural/wall separation to the inherited 600-journal wall proxy, after reproducing its failure at base.

Only scripts/test-qualification-timing.ps1 and tests/Antiphon.Tests/TestHelpers/ManifestPublicationHarness.ps1 changed. Both remain ASCII. No definition file, product script, policy, test selection, or plan changed.

## Required checkpoint outcomes

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-1 | 8 | 8 | 0 | 0 |
| CP-2 | 6 | 6 | 0 | 0 |
| CP-3 | 6 | 6 | 0 | 0 |
| CP-4 | 1 | 1 | 0 | 0 |
| CP-5 | 3 | 3 | 0 | 0 |
| CP-6 | 10 | 10 | 0 | 0 |
| CP-7 | 2 | 2 | 0 | 0 |
| CP-8 | 1 | 0 | 1 | 0 |
| CP-9 | 1 | 1 | 0 | 0 |

Total: 38 executed, 37 passed, 1 failed, 0 skipped. CP-1 ran once in 20261030-150902-0479; CP-2..CP-9 ran once in 20261030-145529-e3d0. One isolated build per selected slice; no native rows. CP-1 source validation passes. M2 validation correctly refuses with report_failed; no clean overall receipt is claimed. Every row reports dirty=0, sourceState=clean and buildSource=verified, including the red row.

- 20261030-150902-0479: wall 778.4 s; execution 598.4 s; admission-only 180.0 s; exit 0; report JSON SHA256 c577052f2cebf3d8faa13b995d752e0178f4e294414be1b4037e8795424f3b71.
- 20261030-145529-e3d0: wall 1317.8 s; execution 837.4 s; admission-only 480.4 s; exit 1; report JSON SHA256 7c456ae33bb59c2ab94088d9dbc506ffc18563c56d4dfdee2884e46391b556c5.

Bootstrap used the build-slot gate and bin-e6-tool/. Import was through the built checkpoint tool. Importer defaults were explicitly used, as authorized: CP-1 row 24 minutes, M1 selected execution 30 minutes; M2 selected execution 58 minutes; admission 45 minutes separately. The tool's two run totals are not summed into a claim about elapsed wall time because the runs overlapped.

## CP-8 repetitions

Each trial used the exact CP-8 TUnit filter, the certified M2 output at 5e7a2dc4d3222e1405330e208aaac9b22d8a458f, a fresh test process/results directory, and the real POSIX-tool-hiding fixture.

- Alone: 5/5 pass; 68.0, 62.7, 77.4, 114.1, 112.9 seconds. No owned build was active. Other checkpoint test rows could be active; 'alone' uses the brief's explicit no-other-owned-build condition.
- Fully loaded: 1/3 pass; 97.4 s (pass, build active at end=true), 97.2 s (FAIL, build active at end=true), 100.1 s (FAIL, build active at end=true). Each eligible trial observed the same actual dotnet build PID before and after the test.

The all-eight-green requirement is NOT met. Two passing diagnostic trials are excluded: 85.4 seconds matched an MSBuild evaluation probe; 97.5 seconds outlasted its controlled build. Their results/logs remain in evidence and were not counted as full-load success.

The required CP-1 build was shorter than the three-trial window after admission. Three extra, fresh isolated Antiphon.Tests builds (bin-e6-load/, bin-e6-load2/, bin-e6-load3/) used exactly the CP-1 project graph solely as controlled load, with their own build-slot leases. Each native driver was owned and awaited. These builds and the pre-land positive-control/probe scripts are unlisted relative to the ordinary manifest, with this explicit brief-directed load/guard diagnostic reason. The checkpoint tool itself reports unlisted:none.

## Remaining failures and attribution

1. Required CP-8: fifo-cases-file-in-process-budget at scripts/test-qualification-timing.ps1:540. Child exited, exit code zero and unsafe-input refusal all passed. The retained seconds < 0.2 predicate failed; the exact seconds were not retained by the existing child-temp cleanup. The new foreign corpus/394-byte/wall checks and the 600-journal checks passed in this run. This is the original in-process condition, now separately named, not a relaxed replacement.
2. Fully loaded trials 2 and 3: fifo-sidecar-does-not-delay-launch at scripts/test-qualification-timing.ps1:624. Its unchanged child wrapper allows 12000 ms including cold startup; its child also checks the invocation wall against 12 seconds. The combined assertion does not preserve which term failed (exit, exit code, marker). Do not claim a proven timeout or product regression from this combined result alone.

Both predicates are inherited, but failure attribution remains unresolved: a focused base FIFO-child probe (20 fresh children, unchanged product library, added measurement split only) passed 20/20, p95 0.0225509 s, max 0.0248887 s. It did not reproduce the retained 0.2-second failure. The launch witness was not reproduced at base. No assertion was weakened or product deadline changed to make these failures green. A Debug commission must retain exact child seconds, launch exited/exit/marker facts, host load and startup-versus-body spans before proposing further test-only repair.

## Positive controls and earlier outcomes

Sixteen distinct changed guards were demonstrated red: four child exit/exit-code checks; two in-process limits; two unsafe-input checks; foreign corpus count (41), actual read-byte excess, wall excess and changed verdict; 600-journal corpus, wall and phase predicates; V-6's new successor publication comparison. Restored foreign/crowd witnesses and V-6 passed. V-6's scratch mutation empties successor Context.SeedRows while retaining seed.json and went red at successor-published-chunk-lost-prefix torn; ordinary CP-1 passes all three modes. These are pre-land controls, not SourceLanding credit.

An early raw harness run at 499fda76b failed two checks: the introduced counter observed the obsolete reader (0 versus 394 bytes), corrected in 5109b4000; and the inherited 600-journal 200 ms wall proxy measured 216.1 ms. A 30-sample exact base crowd probe reproduced 1 failure at 255.8 ms, justifying the additional test-only 0.75-second wall split. An early checkpoint invocation rejected a short expected SHA before building; the subsequent 499fda76b run was intentionally stopped and joined while its build ran, before source correction. Its stopped/exit-6 evidence is retained and has no test credit. No failure was silently replaced with a passing rerun.

## Budgets and policy checks

Measured isolated builds: CP-1 269.7 s and CP-2 350.2 s. CP-1 test body 277.0 s, startup 7.2 s, teardown 1.3 s; M1 execution 598.4 s, admission 180.0 s. M2 execution 837.4 s, admission 480.4 s. No Docker/database/web host/migration setup applies to these script-focused rows; finer unattributed CLI/IO spans remain unknown rather than zero.

Raise the M1 16-minute group/CP-1 coupled ceiling: this run alone suggests 20 minutes at approximately 2x its 10-minute execution. To accommodate the review's 12-14-minute loaded builds plus this run's 4.6-minute test body, use a provisional 40-minute M1/CP-1 ceiling (roughly 2x 19 minutes), tracked under CARD-1514/1404. Raise CP-2's build-coupled 18-minute row ceiling to a provisional 32 minutes (2x the reviewed 14-minute build plus the measured 2-minute row body). The M2 48-minute group ceiling already exceeds 2x this run's 14-minute execution and need not rise on these measurements. These combined-load estimates are provisional, not measured p95s; separate cold/warm/load observations at the next ordinary rc pick are required. Retain a separate finite 45-minute admission allowance; do not turn queue wait into execution budget. Native budgets remain unmeasured/owed. No plan was edited.

The real scripts/audit-land-verification-graph.ps1 -Check passed with the staged evaluator under a build slot: definition digest 20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6, count 66, pass2 approved. -Update was not run. The initial attempt without a built evaluator refused and is not the accepted result. Evidence guard over base..source: 3 commits, 0 violations. A fetched origin/master merge-tree conflicts due to broad inherited branch drift; no merge/rebase was performed, and no whole-branch landable claim is made.

## Restoration and cleanup

All foreground drivers and owned build children were joined. All three controlled load builds completed with zero errors (195 s, 209 s and 234 s slot-held costs; build command elapsed 194.29/207.74/232.97 s). The mutable scratch targets were restored from the unmutated source; hashes are in restoration.json. The seven owned alternate output roots were removed individually by exact literal path: the two bin-e6-tool roots, bin-manifest-core, bin-manifest-integration and bin-e6-load/bin-e6-load2/bin-e6-load3 under Antiphon.Tests. Checkpoint JSON/TRX/log evidence was retained and remains gitignored. No other cleanup was performed.

## Evidence and continuation

Evidence root: /work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence. Essential checkpoint lines below are unedited. Native CP-10/CP-11, historical replay 28bbb58e / manifest SHA 9db1b295..., deployed/private watcher inventory and SourceLanding Mutation remain owed as in .antiphon/task-93994c70.md. No native tests, desktop files, live qualification d47848cc, rc merge, tag, publication or activation were performed.

A future Merge task must use the review's ordered pick list plus all three source fixes listed above, after these red checks are resolved. Current next stage is Debug, not Merge.

```text
CHECKPOINT CP-1 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=ok filter=/*/Antiphon.Tests.TestHelpers/(RcManifestJournalTests*)|(RcManifestRecoveryTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-150902-0479/rows/CP-1/run.trx slot=granted waited=165s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=ok filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=reused filter=/*/Antiphon.Tests.TestHelpers/TestPhaseSummaryTests/C1514T_MergeAndQuality executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=reused filter=/*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/C487_G059 executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=reused filter=/*/Antiphon.Tests.Scripts/ReleaseGateAuthorityContractTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-5/run.trx slot=granted waited=165s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=reused filter=/*/Antiphon.Tests.Scripts/RcFullTimingContractTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-6/run.trx slot=granted waited=30s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=reused filter=/*/Antiphon.Tests.TestHelpers/TestPhaseSupervisorTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-7/run.trx slot=granted waited=15s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-8/run.trx slot=granted waited=105s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=5e7a2dc4d3222e1405330e208aaac9b22d8a458f build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogHarnessTests/HeartbeatExistingContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-e6d1b3db/.antiphon/e6d1b3db-evidence/checkpoints/20261030-145529-e3d0/rows/CP-9/run.trx slot=granted waited=90s dirty=0 source=5e7a2dc4d3222e1405330e208aaac9b22d8a458f sourceState=clean buildSource=verified
```

--- next stage ---
next: debug
handoff: Investigate the retained FIFO 0.2-second in-process failure and unchanged 12-second launch child check; preserve failures and rerun only repaired/exact missing selections. Do not merge while required CP-8 is red.
artifact: /work/worktrees/task-e6d1b3db/.antiphon/task-e6d1b3db.md
