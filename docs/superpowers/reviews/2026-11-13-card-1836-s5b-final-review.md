# CARD-1836 S5b Final Review

Review task 3e20dd08. Code owner 1180038b-0042-4788-acdf-76bb818f5b33.
Reviewed source `50ee155d2211d86690818dc968533f88ccb5efbf` on `feat/card-task-1180038b`.
Range `b57583cee95b75c62edd496247e2361c7cc4e034..50ee155d2211d86690818dc968533f88ccb5efbf`.
Round 1, Final, ordinary scope Full. Verdict: clean. The module admits nothing.

`Get-RcAdmissionAlgorithmCoverage` returns `complete=$false` and `release-graph-incomplete` with no caller override (`scripts/lib/rc-admission.ps1`). `Read-RcAdmission` sets `admitted` only when eligibility and that flag are both true, so `admitted` stays false, `forceFull` stays true, and `gateCredit` and `executionEvidence` stay false. `scripts/rc-ledger-import.ps1` always writes `admitted=0`, shard outcome `unproven`, and `forceFull=$true`. No launch, nightly, qualification, or supervisor file references the module. No environment variable opts in. `-SelectionMode Full` is unchanged.

## Semantic merge

Nine subject commits, oldest `ecbd6ba8ee9471d7e9a7b4da062f9cf48bcb6070` through `50ee155d2211d86690818dc968533f88ccb5efbf`. No `.antiphon` report commits. Cherry-pick onto `b57583cee` applied cleanly; the tree matched the reviewed tip (`e7fa81d2eff8b864ede1fbe61b961b94928f2bf4`).

`origin/master` moved during the review to `3dc5e5c335c9ea0e3d622b042ebf809584ce0117` (`perf(nightly): TRX identity rows are built in the compiled core`). That commit does not touch any path in the slice. Cherry-pick of the same nine commits onto it applied cleanly (`d832a42a4c0f5d6800181f7c662bf0540aabbaf3`). `git merge-tree --write-tree origin/master HEAD` exited 0 with tree `dda9719d0135418ad1ac64c7bed871e3bc7a685b`, the same tree as that cherry-pick.

`scripts/regenerate-derived-impact-index.ps1` on the first scratch, after a native driver build, printed `derived-impact-index unchanged` (lease `cbc6b47a-8b29-465c-8af8-4f927de339d2`). The later master commit does not change the index.

`scripts/check-evidence-diff.ps1 -BaseRef b57583cee95b75c62edd496247e2361c7cc4e034 -HeadRef 50ee155d2211d86690818dc968533f88ccb5efbf`: commits=9 entries=0 violations=0.

## Checkpoints

All receipts are at `50ee155d2211d86690818dc968533f88ccb5efbf`, `sourceState=clean`, `buildSource=verified`, slot granted. Tool: `dotnet run --project tools/Antiphon.Checkpoints --no-build -p:UseAppHost=false`.

| Run | Row | Executed | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| 20261113-134631-bc44 | CP-6 | 24 | 24 | 0 | 0 |
| 20261113-134631-bc44 | CP-90 | 136 | 136 | 0 | 0 |
| 20261113-134631-bc44 | CP-93 | 27 | 26 | 1 | 0 |
| 20261113-134631-bc44 | CP-94 | 10 | 10 | 0 | 0 |
| 20261113-134631-bc44 | CP-95 | 24 | 24 | 0 | 0 |
| CP-93-20261113-140225-e314 | regenerate_script_reports_changed_then_unchanged | 1 | 1 | 0 | 0 |
| 20261113-140331-cd49 | CP-91 | 2 | 2 | 0 | 0 |
| 20261113-140331-cd49 | CP-92 | 2 | 2 | 0 | 0 |

CP-91 and CP-92 ran in a second invocation with `--parallel 1`. The class receipt for CP-93 failed only `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`: the script is invoked with `-NoBuild`, and the checkpoint build does not produce `scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll`. That is the documented precondition (slice plan: build the driver in the native default output before regenerating; same failure recorded in `docs/investigations/2026-11-12-restart-session-runner-missing-function.md`). Native driver build lease `8dcb5b67-40b7-4c3c-9896-87004d75eeda` succeeded, 0 warnings. The exact method then passed on the existing verified `bin-c1836-s5b/` binding. Author's 225 passed matches 24+136+27+10+24+2+2 once that method is counted from the green re-run. The red class receipt is kept.

Unlisted, named by this review brief and absent from the closed table: `RcHistoricalEvidenceTests` on the same verified binding, run `RcHistoricalEvidenceTests-20261113-140830-b2f4`, executed=3 passed=3 failed=0 skipped=0.

## Counterexamples

A fixture probe (`/tmp/c1836-s5b-probe.ps1`, work directory `/tmp/c1836-s5b-probe-work5`) exercised one flipped ingredient at a time. Diagnostic eligibility can be true. Admission stays false, with `release-graph-incomplete` and `algorithm-uncovered:*` for every required path outside the two covered files. Refusals covered: Linux platform, non-`rc/YYYY-MM-DD` line, chunk and case outcomes including Failed, Timeout, Aborted, Skipped, Inconclusive and NotExecuted, incomplete chunk, algorithm digest mismatch, release evidence not complete, an extra `complete` field (`invalid-catalog-field:complete`), anchor at exactly seven days, future anchor, unverified anchor, manifest rehash mismatch, the real S2 collision pair `10788987499786589518` / `17490688019260441962`, and an unassigned chunk. Unicode NFC versus NFD and case-fold do not alias. Banned helpers `ConvertTo-RcBoundaryIdentity`, `Compare-RcBoundaryIdentity`, and two-argument `Add-RcBoundaryString` are absent. Stored digests are rehashed. Missing, corrupt, lone-surrogate, and huge inputs throw. A zero-executed TRX, a summary outcome of `Passed`, and nonzero timeout or notExecuted counters throw. A Failed result with counters still saying passed=1 failed=0 is refused as `cases-not-passed`. Import with and without a manifest stays `admitted=0` and `forceFull=$true`.

## Not blockers

These do not admit while `complete` is hardcoded false. They are advisories for the slice that later turns coverage on. They do not hold this land.

- The TRX `inconclusive` counter is not checked. The decision still requires every `UnitTestResult` outcome to be `Passed`.
- Import does not bind `ExpectedSha` or `ExpectedRunId` from a retained summary. It always records `unproven`.
- Import of an empty dirty set prints `dirty=0 unproven=0` while `forceFull` remains true.
- A TRX that the history document does not name is not scanned.
- `UtcNow` is an injected clock for the S8a fixture, defaulting to system UTC. It is not an environment opt-in. A lying caller can keep an old anchor inside the window; `Read-RcAdmission` still does not admit.
- The 1,163-chunk / ~23k-case universe was not timed. Fifty decisions on a one-case fixture averaged about 95 ms (about 110 s extrapolated). That cost is not on a launch path.

Positive controls PC-V6-manifest, PC-V6-inventory, PC-V6-provenance, PC-V6-terminal-roster, PC-V6-origin-history, PC-V6-anchor, PC-V6-algorithm-coverage, and PC-V6-inert stay pending. Operator waived Code mutation. Native Windows proof stays owed. No OS startup change. Restart none. No new input wait. Parking disabled, so there is no automatic release deadline (CARD-1083).

`scripts/impacted-classes.ps1 -MergeSubset` on this range: `migrations=false`, `windowsFilters` empty. The downgrade selector does not run when the migrations row does not match, so it was not run. Impacted classes are the fixed land/safety set plus `ProcessSpawnLimitTests` and `TestLaneCategoryGuardTests`. The spawn and lane rows ran (CP-90, CP-91, CP-92). The fixed land classes are the standing census, not files this slice edits, and they are outside the closed table.

Fixture residue under `.antiphon/` is the checkpoint receipts and the inbox brief. Both are gitignored. Scratch trees `/tmp/c1836-s5b-review-scratch` and `/tmp/c1836-s5b-review-scratch-3dc5e5c`, and the probe work directories, were left in place.
