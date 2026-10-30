# Review ffc90c76: CARD-1514 S3 (hang detection and watchdog admission)

Subject task 6ea254d2-b36e-4e70-a27f-06d3bb0147c5, branch `feat/card-task-6ea254d2`, tip `194705e096adbe801adda08a839249f75aa5c325` (RED `871230f10`, fix `5b9dd4a649bd369d4eb5e9927e4e800a01755323`, report `194705e09`), parent `cbdf5f76bf4b14845f1e15fd8961065ace0c14c6`. Linux read on server2. The round was commissioned as Final, but the brief caps it at **Interim scope**.

## Verdict

**Defects found.** Today's production behaviour is safe. Every launch gets the same cap as at the parent, because no calibration exists and production never passes `-Calibration`. No real chunk is killed early: every one of the 40 longest real walls fits under its cap. The defects are in the admission rule itself (one on the unmeasured path, two on the measured path) and in witnesses that cannot go red. These are the code S3 delivers, and the next calibration slice would switch the measured-path rules on.

## Defects

**D-1: the unmeasured path admits planned costs above the suite watchdog**
- Where: `scripts/lib/nightly-watchdog-admission.ps1:107-111,171-173,209-210`.
- Failure: the ceiling is 2400 s for every suite except e2e. The unmeasured branch then returns `Admitted=true` with the suite watchdog. Measured: session-runner P=2400 gets W=1800; pty-host, agents-pty, markdown-pdf and messaging at P=1800 or 2400 get W=1200, all `adm=True`.
- Scenario: a pty-host launch planned at 1800 s is admitted and killed at 1200 s. It never got past a third of its planned cost.
- Why it matters: S3 claims the planner-ceiling pairing, which says the watchdog must never kill a chunk the planner may schedule. That holds only for antiphon (3600 ≥ 2400) and e2e (7200 ≥ 1800).
- Not reachable today: `nightly-class-costs.json` prices no class in those suites near 1200 s, and the largest real wall there is session-runner g1 at 799 s. The cap values are unchanged from the parent.
- Fix: refuse before launch when P exceeds the suite watchdog minus a margin, or derive C per suite so that C fits inside the suite watchdog. Add witness rows for each suite id.

**D-2: the measured path accepts an error factor below 1**
- Where: `nightly-watchdog-admission.ps1:204`. The gate only checks `$e95 -gt 0`.
- Failure: a calibration with E95=Emax=0.2, C=2400, Wmax=4800 at P=2400 gives `Admitted=True W=960`. With E95=0.01 it gives W=600.
- Scenario: a chunk at exactly the planner ceiling of 2400 s is admitted and killed at 960 s.
- Why it matters: the plan defines E95 = max(1, p95), and its proof needs W(P) ≥ 2·E95·P > P. The resolver trusts the stored factor and never re-applies the clamp (mutation M6e, removing the clamp, also survives).
- Fix: require E95 ≥ 1 and Emax ≥ E95 for eligibility; otherwise keep the suite watchdog. Add a witness.

**D-3: a failed wall that cannot be parsed is silently dropped**
- Where: `nightly-watchdog-admission.ps1:55-58`. An unparseable value hits `continue` without being marked censored.
- Failure: twenty 900 s Passed walls plus one Failed wall recorded as `'1936,5'`, `$null` or `'n/a'` give N=20, Emax=1, Censored=false, and then `Admitted=True W=1800`.
- Scenario: this is the g103 replay. Its 1936 s failed wall would be killed at 1800 s.
- Why it matters: the plan says missing terminal accounting is censored and blocks a reduction. The same code also counts walls of 0 or below as complete samples, which pads N.
- Fix: treat a non-finite, unparseable or non-positive wall as `Censored=true`.

**D-4: mutations the witnesses do not catch**
Each mutation was applied to a scratch copy of the tip and run through the real fixture in all four modes.
- **M4** (brief-named): `[Math]::Min($watchdog, $suiteSeconds)` on the measured return (`:220`) stays green. No witness has a measured W above the suite watchdog, even though the plan's own boundary W(C=2400)=4800 is above it.
- **M5b**: dropping `$atCeiling -gt $wmax` (`:217`) stays green. No witness has W(P) ≤ Wmax < W(C).
- **M5d**: dropping `$planned -gt $bound` (`:212`) stays green.
- **M6e**: dropping the E95 ≥ 1 clamp (`:70`) stays green.
- **M9**: in production, `-TimeoutMilliseconds ([Math]::Min(600000, [int]$admission.WatchdogMilliseconds))` (`nightly-tests-impl.ps1:1410`) stays green. That is a smaller production cap going undetected. **M9b** (dropping `-PolicyObject` at `:1403`) also stays green. The `wired` check is a substring `IndexOf` (`rc-full-watchdog-witness.ps1:155-158`).
- **M8**: `$coverageComplete = $true` on timeout (`nightly-tests-impl.ps1:1535`) stays green. **M8b** (making the disposition depend on `TrxPresent`) also stays green. `Get-NightlyWatchdogTimeoutDisposition` returns constants, and `TimeoutKeepsCoverageIncomplete` only asserts those constants, so it cannot go red.
- Fix: add a measured witness with W above the suite cap, a W(C) > Wmax witness, a P > C witness and a p95 < 1 witness. Drive the native launch and timeout branch through a fake `Invoke-NightlyOwnedProcess` and observe the timeout passed in and the coverage verdict.

Mutations that did go red: M1 (drop failed walls), M1b (drop the tail term), M2 and M2b (drop the fallback), M3 (price default/pace walls as if measured, the 0.5 s/node case), M4b (min on the unmeasured path), M5 (ignore the planner ceiling), M5c (clamp to Wmax), M6 (censored), M6b (n ≥ 19), M6c (stratum), M6d (hash), M7c (diag growth), M7d (copy-forward).

Survivors that are not defects: M9d is equivalent, because the planner's refusal is unreachable after its own ceiling throw. M9c only falls back to the parent's behaviour. M7 and M7b are redundant guards, so the witness goes red only when both the heartbeat-path exclusion and the UID regex are removed.

RED at parent `cbdf5f76b`: all four fixture modes exit 1 with `admission-proof-absent`, naming six missing functions. The RED is real but structural.

## Admission proof with real numbers (run 28bbb58e)

- **Production path:** every launch gets `unmeasured-cost` and the suite watchdog from the policy, exactly as at the parent.
- **antiphon** has a 3600 s cap. Its longest walls are g103 1936 s (failed), g109 1360, g11 1352, g119 1257, g9 1221, g17 1172, g18 1166 and g126 1109. Every antiphon chunk in the top 40 is at most 1936 s, below the cap and below the 2400 s ceiling.
- **g105** (3615 s) is the hang, correctly killed at 3600.
- **session-runner g1** ran 799 s under an 1800 s cap.
- **e2e** has a 7200 s cap and an 1800 s ceiling.
- **Group 9** at 9.2 s/test is priced as `pace` or `default`, so it keeps 3600.
- **Boundaries:** P=2400 is admitted with 3600. P=2400.0000001 and e2e P=1801 are refused before launch.
- **Bad inputs:** P of 0, -5, NaN, Infinity, `'1,5'` or 1e308 is refused before launch. Null P gets 3600.
- **Culture:** under de-DE, a double 1936.5 is fine.
- **Missing price:** a missing or corrupt price file means 0.5 s/node pricing, basis `default`, and the suite watchdog. It never falls back to a smaller cap.
- **Overflow:** E95=1e300 throws a decimal overflow before launch. That is a refusal, never a smaller cap.
- **Malformed policy value:** a policy watchdog of `'abc'` throws, which is pre-existing `Get-NightlyWatchdogMs` behaviour.
- **Synthetic case:** P=900 with the failed 1936 s wall gives 2040.

## Hang detection

- **Report-only.** There is no kill token in the heartbeat library, and the absolute watchdog is still the kill authority.
- **Signal:** the new signal is `TestNodeUid { Value = .. }` plus a state, on the same line, read incrementally from the TUnit execution `.diag`. The heartbeat path is excluded. It does not use mtimes, so the tests.log or manifest rewrite noted by afedcf4e cannot fool it. During a hang, the harness is blocked inside `Invoke-NightlyOwnedProcess` and is not republishing.
- **Not wired in this repo:** no production caller exists here. Only the harness and fixture call the library, and the supervisor (`supervise-rc.ps1`) is outside the checkout. Until the supervisor passes `-DiagnosticProgress`, the new fields stay null.
- **g105 replay:** the last diag line was at 21:14:39. With the 20 min default budget (or max(10, P/60) when P is known) and CPU under 5 s/min, it is flagged at about 21:34, about 32 min before the kill.
- **30-minute quiet healthy test:** not killed under the 3600 s cap, but it **is flagged** `hangSuspected` after 20 min. The plan's own witness asserts that flag at 35 min. Whether it should be flagged is the caller's call.
- **False positives and negatives:**
  - A diag line over 1 MiB with no newline stalls the read offset and blinds progress.
  - A partial last line read at EOF is consumed.
  - A truncated and rewritten diag re-reads old lines as growth.
  - `lastCompletedAtUtc` stamps the observation time, not the record time.
  - A backward clock jump only resets idle; a forward jump can only flag early.

## Unchanged items

- `tests/test-execution-policy.json`, `nightly-policy.ps1`, `nightly-class-costs.json`, `release-authority.ps1` and `nightly-coverage.ps1` are byte-identical to the parent.
- The suite watchdog constants are unchanged. The native fallback for a missing key changes from 3600000 for all suites to 7200000 for e2e, so it is equal or larger.
- No csproj, props or targets file changed.
- ASCII: 0 non-ASCII bytes in all four PowerShell files, and only constructs available in PowerShell 5.1.
- `check-os-witnesses.ps1` exit 0. It checks syntax and inventory only; `NATIVE pending restartImpact`.
- `check-evidence-diff.ps1` exit 0: commits=3, entries=1, violations=0.

## Merge

- **rc:** the S3 commits cherry-pick cleanly onto rc `ade54222`, alone (tree `2ba37df9`) or as the whole S1+S3 range from `8edda9996` (tree `a311cfb7`).
- **master** (`98fc3ceb6`): the S3 pick is clean (`4d5bcfe2`; code only `04fb9906`). The author's ten-path conflict list is a whole-branch `merge-tree` across rc-line history, and the heartbeat add/add belongs to that, not to the port.
- **A clean port keeps:** the new admission library, the dot-source line, the planner stamp, the native admission and timeout handoff, the timeout disposition, the heartbeat additions and the three test files.
- **bb0b355c** (plan-only at `e5b87f905`): its M2 slice will edit `nightly-tests-impl.ps1` in two places next to S3 hunks:
  - the `Publish-NightlyChunkObservation` call at tip `:1418`, 7 lines after S3's admission hunk (`:1395-1411`);
  - the timeout/terminal paths, which include S3's disposition hunk (`:1534-1536`).
  It also edits `test-phase-evidence.ps1`, which S3 does not touch. Its D-10 already says to serialize with S3, and its V-13 builds on S3's UID progress.

## Checkpoint run 20261030-031928-58da (tip 194705e09, Linux)

GREEN exit 0. 7 rows, 1 build, `sourceState=clean buildSource=verified` on all rows, `unlisted: none`, wall 8m46s.

| Row | Result |
|---|---|
| CP-8 | 3/3 |
| CP-9 | 10/10 |
| CP-17 | 1/1 |
| CP-18 | 1/1 |
| RV-1 (`RcRosterRepairTests`, which includes CP-19 Costs) | 3/3 |
| RV-2 (`WindowsUnitPlatformAdmissionTests`) | 4 passed, 1 skipped |
| RV-3 (`C1514LandingSplitBudgetTests`: C1404, C1577, C1584, C1410, G2865) | 5/5 |

- The RV-2 skip is `C1210_Windows_linux_only_outcomes_are_exact`, which needs Windows placement by design.
- `validate` refuses the whole run (`row_failed`) because of that skip. With `--rows CP-8,CP-9,CP-17,CP-18,RV-1,RV-3` it reports `SOURCE VALID rows=6`.
- The row table is `.antiphon/review-ffc90c76-plan.md` (ignored).
- Not run, owed natively: CP-2, CP-4, CP-6, CP-7 and the RV-2 Windows case.
- PCs are still pending.
- Cleanup: the `bin-rvffc` output in `tools/Antiphon.Checkpoints` and `src/Antiphon.HostCleanup` was deleted by literal path. The checkpoint tool removed its own test output.
