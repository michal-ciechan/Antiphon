# Task 91dd821a — stopped-run seed order survives the digest guard

Outcome: D-1 through D-4 are fixed on `feat/card-task-91dd821a` tip `d1e41d83e45d53803d622d51fcc0ce6d98362a1d`. A stopped-run seed with marker `operator-seed-from-stopped-run` keeps failed chunks first. A missing marker, a foreign marker, and a plain previous-run record still fall back. Checkpoint run `20261031-192804-852c` is GREEN, 6/6 rows, on that commit.

Base: `4390c697e2a7255587264514260d9903fea8d719` (combined digest fix `39488ad52` and seed fix `9418c99f2` on rc `e0d49d221`). No rebase, amend, reset, or force-push. No push to rc, master, or another task branch.

## Commits

| SHA | Subject |
|---|---|
| `ba665eccf8c1db45af59a8dd6585cb165a96b4d3` | test: guard seed order, prefix tamper, scale, and proc identity |
| `d1e41d83e45d53803d622d51fcc0ce6d98362a1d` | fix: keep stopped-run seed order out of the plan digest guard |

Files: `scripts/lib/nightly-dispatch.ps1`, `scripts/lib/nightly-failure-history.ps1`, `scripts/lib/nightly-stopped-run-seed.ps1`, `scripts/test-nightly-stopped-seed.ps1`, `tests/Antiphon.Tests/Scripts/LinuxScriptHarnessProcess.cs`, `RcStoppedRunSeedTests.cs`, `ScriptHarnessProcessContractTests.cs`.

## Why this form (D-1)

`Get-NightlyPreviousRunKind` returns `operator-seed` only when the record is not a dispatch progress (no `suites`, `liveness`, or `schemaVersion`), `chunks` and `failedTests` are arrays, `chunks.Count > 0`, and `seedKind` is exactly `operator-seed-from-stopped-run`. That kind returns from `Resolve-NightlyPreviousRunOrder` into `Resolve-NightlyStoppedSeedOrder` before the digest guard. The guard itself, after the permutation check, runs only when the kind is `previous-run`.

A seedKind-only skip would be too wide: a forged marker on a normal record would skip the digest. Normal shape wins, so a forged marker stays on the digest guard.

| Record | Result |
|---|---|
| Builder marker | `operator-seed-order`. Failed chunks stay first. `FailedFirstMapping` requires the first chunk `new-Red`. A different schedule `CandidateSha` is allowed: the seed's `sourceSha` is the stopped run, not the new run. |
| Seed shape, blank marker | Canonical entries, `previous-run-plan-digest-missing`. |
| Seed shape, any other marker | Canonical entries, `operator-seed-kind-foreign`. |
| Normal record, with or without a forged marker | Existing digest guard. Matching digest stays `previous-run-order`. Missing digest stays `previous-run-plan-digest-missing`. |

In place of `canonicalPlanDigest`, `Resolve-NightlyStoppedSeedOrder` checks `sourceSha` (`seed-candidate-missing`, `seed-candidate-malformed` unless `^[a-f0-9]{40}$`), `sourceRunId` (`seed-run-missing`), duplicate suite+chunk (`seed-schema`), and the class set of a colliding chunk (`seed-plan-regrouped`). A fallback returns the original entry objects, so the schedule stays a permutation. `dispatch-not-permutation` still runs before the digest block.

A-3, noted and left as it is: a digest fallback sets `$dispatchOrdered` back to `$dispatchRows` (canonical insertion order). That drops the failed-first partition built just above it. The fallback is the original order on purpose.

## Red, then green

At the combined tip, before the product edit, the three seed tests were red. TRX counters: total 3, executed 3, passed 0, failed 3 (`FailedFirstMapping`, `InvalidSeedFallsBack`, `ScheduleConservesAllChunks`). `FailedFirstMapping` returned canonical order starting at `new-Passed`. The same three, plus `DigestGuardLeavesSeedOrder`, `SummaryPrefixTamper`, `WindowsAncestorSwapRefused`, and `ScaleExportBound`, passed inside CP-1 below.

## D-2 scale bound

`ScaleExportBound` builds 3,300 run files and 1,700 timing files and a summary padded with 4,194,304 spaces at test time. Nothing is committed. The stopwatch covers only `Export-NightlyStoppedRunSeed`. One measured export on this host was 13.175s (n=1, provisional). The bound is 27s, twice that sample, under the reviewer's 60s suggestion (their similar layout was 9.275s). CP-1 method duration was 15.427s and the case passed, so the export itself was inside 27s. C# timeout 180s. This case uses harness 150s/20s. The shared seed harness stays 90s/10s.

## D-3 prefix tamper

`SummaryPrefixTamper` flips a byte after the `,"timingChunks":` cut (inside the invalid bulk, not the key quote). That decoy is accepted. The read barrier then flips byte 8 of `summary.json` on the first read and the export refuses `seed-summary-tampered`. CP-1 duration 7.256s, passed.

## D-4 Windows identity

`Get-NightlyHistoryNodeIdentity` on Windows reads the volume serial and file index through `GetFileInformationByHandle` (ASCII, Windows PowerShell 5.1). File index catches a real directory replacement; a reparse-only check would not. `$script:NightlyHistoryForceWindowsIdentity` forces that branch on Linux, where kernel32 is not called: the branch uses host `lstat` device and inode. The parent is always rechecked (`$i -le 1`), so swapping a grandparent changes the parent's identity. The witness points the grandparent at a separate forged tree. A same-content symlink is not the witness: Linux and a correct file index both accept it. CP-1 `WindowsAncestorSwapRefused` passed in 6.332s.

There is no Windows lane (A-6). The Linux witness forces the branch this host can execute. The file-index call is what a Windows host runs.

## A-5 proc identity

`IsVanishedProc` matches `FileNotFoundException`, `DirectoryNotFoundException`, or an `IOException` whose `HResult` low word is 2 or 3. It does not match the exception message. `UnauthorizedAccessException` is not vanished. Reading `/proc/<dead-pid>/stat` on this host throws `IOException` `HResult` `0x80070003` (low word 3), message "Could not find a part of the path". The witness and the HResult match landed in the test commit and the fix commit together, so the old message matcher was not given its own red run. `ScriptHarnessProcessContractTests` is inside CP-1, which passed 129/129.

## Checkpoints

Plan: `docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-layout-plan.md`. Run `20261031-192804-852c`. Source `d1e41d83e45d53803d622d51fcc0ce6d98362a1d`, clean, buildSource verified. `--serial --row-timeout 90m --total-timeout 180m --expected-source-sha d1e41d83e45d53803d622d51fcc0ce6d98362a1d`. The tool warned that the derived row timeout of 120 minutes exceeds the 45-minute advisory. `request.json` and the row deadlines are 90 minutes. CP-1 finished in 36m03s. Verdict GREEN, exit 0. Wall 50m59s. Sequential-equivalent 38m43s. Builds 4. Max concurrent builds 1. Unlisted: none. Slot waited 0s on every row. Evidence: `.antiphon/checkpoints/20261031-192804-852c/report.md`.

| Row | Result | Wall | Build |
|---|---|---|---|
| CP-1 seed-chain neighbours | executed 129, passed 129, failed 0, skipped 0 | 2163.362s | 290.7s |
| CP-2 CapFitsAdmittedPlannedCost | 1/1 | 19.296s | reused |
| CP-3 LedgerAndReceiptBytesAreUnchanged | 1/1 | 128.165s | reused |
| CP-4 session-runner ProcessSpawnLimit and NativeLinuxPolicyWitness | 5/5 | 4.938s | 68.5s |
| CP-5 pty-host NativeLinuxPolicyWitness | 3/3 | 3.610s | 54.6s |
| CP-6 agents-pty ProcessSpawnLimit and NativeLinuxPolicyWitness | 5/5 | 4.276s | 54.8s |

CP-1 slow classes: `RcFailedFirstPlannerTests` 873s/2, `RcFailedFirstExecutionTests` 812s/2, `RcStoppedRunSeedTests` 125s/16, `TestClassificationPolicyTests` 111s/39, `RcFailureHistoryTests` 109s/2, `C1514LandingSplitBudgetTests` 83s/5.

The digest plan's chain rows are this same Antiphon.Tests build. Digest CP-5 is seed CP-2. Digest CP-6 `ActualLoopFollowsSchedule` and CP-7 `JournalCustodySurvivesInterleaving` are the two `RcFailedFirstExecutionTests` methods inside CP-1. Digest CP-8 is seed CP-3. `RcRosterRepairTests` is in the CP-1 filter. Census cases `C1404`, `C1577`, `C1584`, `C1410`, and `G2865` are the five `C1514LandingSplitBudgetTests` methods inside CP-1. Policy witnesses are CP-4, CP-5, and CP-6, all green.

The tool deleted `bin-d63-seed1/`, `bin-d63-runner1/`, `bin-d63-pty1/`, and `bin-d63-agents1/`.

## Mutants

Each mutant was restored. `git status` was empty after both runners. Digest mutants used `scripts/test-nightly-failed-first.ps1 -Case ActualLoopFollowsSchedule -SmallLoopProbe` (18 rows).

| Mutant | Result |
|---|---|
| DM1 delete the malformed-digest branch | Killed. short/long/nonhex actual `previous-run-plan-mismatch`. 17 passed, 1 failed (`chainIngress`). |
| DM2 absent liveness trusted; non-blank non-closed still throws | Killed. liveness and liveness-blank actual `previous-run-order`. 17/18, `chainIngress`. |
| DM3 absent schema trusted | Killed. schema and schema-blank actual `previous-run-order`. schema-foreign stayed `previous-run-schema`. 17/18, `chainIngress`. |
| DM4 drop `liveness='closed'` from the dispatchProgress producer | Killed. `chainPreviousOrder` failed. 17/18. |
| DM5 delete the blank-sha throw | Killed. candidate and candidate-blank actual `previous-run-schema`. candidate-short stayed `previous-run-schema`. 17/18, `chainIngress`. |
| SM1 timing root taken from the run folder | Killed. explicit=False auto=False outside=True symlink=True other=True direct=`evidence-link`. |
| SM3 drop `-Tolerate` on both `Read-SeedFile` calls | Survived. `ChunkUnfinishedTolerance` exit 0. The caller still classifies a raw IO error as unfinished. Product was not changed to force a red. |
| SM3b delete the unfinished arm (`seed-chunk-unreadable` / `evidence-unreadable`) | Killed. locked=`seed-chunk-unreadable`, denied=`seed-chunk-unreadable`, open completed=False. This is the tolerance removal the suite kills. |
| M9 drop `$closed -and` on chunk completion | Killed by `ChunkUnfinishedTolerance` (`open=completed=True`). `BuilderReadOnly` still passes. Left that way: M9 surviving `BuilderReadOnly` is expected. |
| SM5 drop only the `Linked` check, keep `-not Directory` | Survived. A symlink is not a directory, so `evidence-link` still throws. |
| SM5b accept a link that is not a directory (`-not Linked -and -not Directory`) | Killed. direct= empty. explicit/auto/outside/symlink/other stayed True. The direct `Resolve-NightlyStoppedTimingRoot` call is what kills an accepted symlink (A-4). |
| SM6 delete `seed-timing-run-mismatch` | Killed. other=False. direct=`evidence-link`. |
| SM7 delete the `seed-summary-tampered` prefix recheck | Killed. decoy=accepted, tamper=accepted, cut=287. |
| SM8 recheck only `$i -eq 0` | Killed. file=`evidence-link`, parent=`evidence-link`, swap empty (accepted). |
| D4-ino0 force inode 0 on the non-Win32NT branch | Killed. `WindowsAncestorSwapRefused` dir empty, link empty (both accepted). |

Pre-existing probe warnings, not failures: `active-identity-unavailable` when `active-chunk.json` is not a file, and historical-membership / short-trx warnings on the failed-first fixture.

## Property

Seed `20261031`. Compared ordering reason, suite/chunk order, prior-failure flags, and lanes with `scripts/lib/nightly-dispatch.ps1` at `3693d53b68c818e828e2dae055f7a3db046ff072` (blob `b638a4eb51ccf83a8e96f705de85b8fbdcd13f9c`, matches the saved base copy). Every record has at least two suites and two chunks. Valid records use liveness `closed`, schemaVersion 1, the matching sha, an in-window `completedAt`, and a digest equal to the current canonical plan digest. The base script ignores the digest field.

Mixed run: 500 records, 250 valid and 250 fault, 0 not-permutation, 0 not-deterministic. Valid ordering text differed from base on 0 of 250. Fault records were not compared; they diverge by design.

Second run: 500 valid records, 0 not-permutation, 0 not-deterministic, ordering text differed from base on 0 of 500.

## Land, evidence, rc

`scripts/audit-land-verification-graph.ps1 -Check` only. Approved, 69 definitions, phase pass2, digest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`. No `-Update`. Property allow-list was not widened. This continuation did not re-evaluate: HEAD was still that commit and the tree was clean. The audit canary directory outside this worktree was left in place.

`scripts/check-evidence-diff.ps1 -BaseRef 4390c697e2a7255587264514260d9903fea8d719 -HeadRef d1e41d83e45d53803d622d51fcc0ce6d98362a1d`: commits 2, entries 0, violations 0, exit 0. Re-run on this continuation.

`git fetch origin refs/heads/rc/2026-10-27` then `git merge-tree --write-tree origin/rc/2026-10-27 HEAD`. RC is `e0d49d221513b467f8c853f47587e688e04255e2`. Exit 0, tree `d2e234936fcfb96c670199251caa28a17221b6ff`. No merge was made. Merge-tree was not run onto `origin/master`; the brief's rc target is the one that matters.

## Declined

- A-1. An appended TRX with no plan row (`antiphon-all-group-105.trx`) still refuses the whole export (`seed-source-changed`). Marking only that chunk unfinished changes export admission and is not hit by finished run d47848cc. Left unresolved.
- A-2. Prefix-close heuristic. Left as it is.
- A-6. No Windows lane, as above.
- A-7. Reviewer process notes. Not this change.

Caller note, already covered: wait for top-up 52 before restarting. This task did not restart AppHost or a runner.

--- next stage ---
next: short Final review of D-1..D-4, then merge as rc top-up 52 with the tree-kill fix
handoff: review this tip, then land onto rc/2026-10-27 as top-up 52
artifact: .antiphon/task-91dd821a.md
