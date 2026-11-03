# Master Unit lane second backstop at bac789483 (task ecf8b748, 2026-11-02/03)

Verdict: **no real regression from the master-only lands in batches 11 to 19.** No test that passed at the
first backstop (master 5376ac529, task a8bd6706) fails now. All 4 failure groups that are still red were
already red in the first report. Twenty-seven failures from that report are now green: 19 in Antiphon.Tests and 8 in SessionRunner.Tests. Antiphon.Agents.Pty.Tests
is still green.

- Subject: master `bac78948348cbf690d9742edfb0651212e215868`, which was `origin/master` when the task started. During the run master moved on to `907edaebb`; that commit was **not** tested.
- First backstop for comparison: master `5376ac529`, report `docs/investigations/2026-11-02-master-unit-lane-5376ac529.md` (origin/feat/card-task-a8bd6706, commit 9be1461ce). Its TRX files are kept in the runner mirror at `/work/worktrees/task-a8bd6706/.antiphon/a8bd-unit/`.
- Host: server2 (Linux), load average 3 to 41 during the run.
- Each test project was built once, in the native default output, with `-p:UseAppHost=false`. Each build had its own `scripts/build-slot.ps1` lease (Antiphon.Tests 260 s, SessionRunner.Tests 59 s, Pty.Tests 29 s).
- Every shard used `dotnet run --project tests/<P> --no-build -p:UseAppHost=false -- --treenode-filter <f> --timeout 45m --report-trx`, with its own lease. `--timeout 45m` is the Microsoft.Testing.Platform total-run timeout, and the brief's 45-minute total timeout maps onto it.
- At most two lanes ran at once. Lane A held RemoteScriptContract, Rolling and VolumeRetirement-v3. Lane B held the other letter shards. SessionRunner.Tests and then the client lane ran as the second lane after lane B finished. Pty.Tests ran alone, after everything else had finished.
- No shard hit the 45m cap, so no shard needed a smaller re-run. Wall time was about 3h05m (21:40 to 00:46 UTC).
- Evidence (gitignored, runner mirror): `.antiphon/ecf8-unit/*.log|*.trx`, and the TRX union at `.antiphon/ecf8-trx-unit/`.

## Completeness

A System.Reflection.Metadata census of `Antiphon.Tests.dll` found 4,115 `[Test]` methods with `Category("Unit")`
on the class or the method. At the first backstop the figure was 4,108. The union of the 20 shard TRX files covers
4,114 of them, with no extras. The one method missing is `LiveLaneIsolationTests.StartupArgvMatchesProductionApplyOff`,
which is `[Explicit]`, the same as last time. The union has 6,522 results: 6,476 passed, 2 failed, 44 skipped.

## Per-shard counts (Antiphon.Tests, `[Category=Unit]`)

| Shard | Filter (class segment / method segment) | Executed | Passed | Failed | Skipped | Test duration | Slot wait |
|---|---|---|---|---|---|---|---|
| rsc-c10 | RemoteScriptContractTests / C099*,C10* | 31 | 31 | 0 | 0 | 32m40s | 0s |
| rsc-c110 | RSC / C110* | 217 | 217 | 0 | 0 | 20m10s | 105s |
| rsc-c116-9 | RSC / C116*,C118*,C119* | 50 | 50 | 0 | 0 | 8m38s | 450s |
| rsc-c12-13 | RSC / C12*,C13* | 83 | 83 | 0 | 0 | 3m28s | 240s |
| rsc-rest | RSC / non-C1 prefixes | 130 | 130 | 0 | 0 | 9m10s | 255s |
| rolling2-script-c1008 | RollingVolumeRecycleScriptTests / C1008* | 19 | 19 | 0 | 0 | 24m09s | 0s |
| rolling2-script-rest | RVRS / C1087*,C13*,C1594* | 58 | 58 | 0 | 0 | 15m36s | 136s |
| rolling2-mount-census | RollingProductionMount, RollingVolumeRecycleCensusChurn | 13 | 13 | 0 | 0 | 1m35s | 270s |
| ch | Ch* | 577 | 576 | 0 | 1 | 27m42s | 0s |
| ret | Ret* | 38 | 38 | 0 | 0 | 16m13s | 0s |
| r-other | other R (23 prefixes) | 501 | 501 | 0 | 0 | 7m49s | 0s |
| c-other | C0,C1,C4,C5,C9,Ca,Cl,Co,Cr,Cu | 386 | 381 | **2** | 3 | 9m22s | 0s |
| adw | A*,D*,W* | 1388 | 1378 | 0 | 10 | 3m28s | 240s |
| st | S*,T* | 775 | 768 | 0 | 7 | 6m04s | 135s |
| op | O*,P* | 790 | 782 | 0 | 8 | 6m19s | 135s |
| ghi | G*,H*,I* | 656 | 644 | 0 | 12 | 11m30s | 420s |
| misc-a | B,E,F,J,K,L,M,N,Q,U,Z,Va,Ve,Vi | 756 | 753 | 0 | 3 | 14m11s | 331s |
| misc-v1 | VolumeRetirementApplyGate, Content | 28 | 28 | 0 | 0 | 12m28s | 0s |
| misc-v2 | VolumeRetirementHostProof | 9 | 9 | 0 | 0 | 2m05s | 270s |
| misc-v3 | VolumeRetirementReceipt, Wrapper, D*, F* | 17 | 17 | 0 | 0 | 25m19s | 0s |

Other lanes:

| Lane | Executed | Passed | Failed | Skipped | Duration |
|---|---|---|---|---|---|
| Antiphon.SessionRunner.Tests (whole) | 1914 | 1695 | 137 | 82 | 12m45s |
| Antiphon.Agents.Pty.Tests (whole, run alone) | 755 | 522 | 0 | 233 | 47s |
| Client (`scripts/test-client.ps1`: typecheck PASS, lint PASS, vitest) | 1848 | 1845 | 2 | 1 | 651s including npm ci |

## Real regressions

None. Nothing fails now that was absent from the first report's failure list, so there was nothing to bisect between
the batch tips.

## Failures compared with the first backstop

| Test(s) | First (5376ac529) | Now (bac789483) | Status | Class / evidence |
|---|---|---|---|---|
| `Scripts.RollingProductionMountTests` (6) | red | green, 12/12 | **fixed** | CARD-1659, 6d50f55b9 |
| `Scripts.HostJqPrerequisiteScriptTests` (12) | red | green (ghi shard has 0 failures) | **fixed** | CARD-1659, 56fc439bf |
| `Checkpoints.TimeoutTests.total_deadline_kills_running_and_skips_queued` | red (load flake) | green | **fixed** | CARD-1659, 7745b1a77 (200 ms to 2000 ms) |
| SessionRunner `CompactionContinuationStopTests` (4), `SessionCpuWatchdogTests` (3), `TranscriptAdoptionSafetyTests.ToDto_reports_unbound_while_locating_exact_after_bind_and_sidecar_after_readopt` | red | green | **fixed** | CARD-1650, 5e14b1485 / db0d2b8a5 |
| `Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_admission_reports_waited` | red (`seed.Exit should be 77 but was 2`) | red: `boundaries.Select(n) should be [1, 2] but was [1]`, `V-5/temp/resume-removing ... DIAGNOSIS=RecycleResumeMismatch` | unchanged (expected red) | (d) pre-existing. 6d50f55b9 (CARD-1659) moved the test to the CARD-1609 temp topology, and it now fails at the temp resume seed instead of preflight. The fix is the CARD-1661 temp-vector repair, which is in review |
| `Scripts.C1364BoundLandWaitProofTests.C1364_Host_recycle_post_removal_reports_waited` | red (`phase` was `preflight`) | red: `boundary["record"]["phase"] should be "containersRemoved" but was "removing"` | unchanged (expected red) | (d) pre-existing, same cause and fix as the row above |
| SessionRunner.Tests: 137 failures in RunnerRestartHealth, RunnerRestartPreflightSafety, CodexWindowsLaunchPolicy, RunnerRestartScriptCompatibility and others | 145 red | 137 red | unchanged (the 137 are a subset of the 145) | (a) Windows/cmd.exe/pwsh.exe dependent (CARD-1652). A TRX name diff shows **0 new** failing names and 8 fixed (the CARD-1650 rows above). The 26 extra SessionRunner results (1914 against 1888), which include the CARD-1653 S1 classes, all pass |
| client `SessionTerminal.mouse.test.tsx` > CARD-0148 mouse policy > `protocol 'ANY' stays local`, `reset and instances remain independent` | red (20 s timeout) | red (`Test timed out in 20000ms`, 34.4 s / 20.6 s) | unchanged | (b)/(d) timing, pre-existing. The first report ran the file alone: 1 of 14 red (`reset ...`) at both 5376ac529 and 3a49b6013. `client/src/features/board/` is unchanged in 5376ac529..bac789483 |

## Notes

- The two C1364 reds are the only Antiphon.Tests Unit failures left on Linux. CARD-1661 is the remaining Linux fix for this lane.
- SessionRunner.Tests still has 137 Windows-only tests that do not skip themselves on Linux (CARD-1652).
