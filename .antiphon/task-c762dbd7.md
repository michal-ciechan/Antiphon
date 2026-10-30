# Review c762dbd7: CARD-1514 S3 round 2 (admission attacks and mutations)

Subject task ee7d0d23-691e-41bb-98e7-9f8fc59ffed8, branch `feat/card-task-ee7d0d23`, tip `d1d49d8906c814152cd61ef31600156d257eb2ff` (verified source `5548074c8`; code is the same at the tip). Parent: S1 final `cbdf5f76b`. Read on Linux (server2). The round was commissioned as Final, but this is a focused **Interim-scope** review.

## Verdict

**Defects found.** Production behaviour today is the same as at the parent and is safe. Production never passes a calibration or a `costBasis`. Non-antiphon suites are priced at the 0.5 s/node default, so no reachable launch gets a smaller cap.

Round-1 defects:
- D-2 (E95 >= 1) is closed.
- D-3 (bad walls) is closed.
- D-4: every named mutation is now red.
- D-1 is closed only inside `Resolve-NightlyLaunchWatchdog`. The planner still schedules what the runtime refuses, and the runtime refusal has no witness. That is D-1 below.

Two more measured-path holes survive (D-2, D-3). The checkpoint rows are green (8/8 rows, 28/28 results).

## Defects

### D-1: unmeasured suite-watchdog pairing is still unpaired at the planner, and its only enforcement is unwitnessed

- **Where:**
  - `scripts/lib/nightly-tests-impl.ps1:438-442`: the planner ceiling is 2400 for every non-E2E suite.
  - `:446-450`: the planner calls `Resolve-NightlyLaunchWatchdog -SuiteId 'antiphon'` for every non-E2E launch, without `-PolicyObject`.
  - `:454`: it stamps `watchdogSeconds`.
  - `:1403-1406`: the only refusal that can fire is at the chunk's own launch.
- **Failure, measured:**
  - A profile-aware pty-host class priced at 1500 s gives a plan of n=1, `seconds=1500`, stamp `watchdogSeconds=3600 watchdogAdmitted=True reason=unmeasured-cost`.
  - At launch, the same chunk resolves `adm=False W=1200 suite-watchdog-ceiling` and throws `watchdog-admission-refused`. That happens after every earlier chunk of that suite has run.
  - Plan `:118` says "never launch then discover the cap mismatch". The round-2 plan note says the plan "is refused before launch, so the planner must not schedule it". The planner does schedule it, and its stamp records the wrong suite's watchdog.
- **Surviving mutation M9g:** deleting the runtime throw (`:1404-1406`) stays GREEN in all four fixture modes. That mutant launches the 1500 s chunk with 1200000 ms and kills it at 1200 s. That is exactly the round-1 D-1 scenario, and no test catches it.
- **M9f:** deleting the planner throw (`:448-450`) is also green. It is equivalent today only because the planner resolves as 'antiphon' and so cannot refuse.
- **Reachability:** not reachable today. Non-antiphon suites are priced at 0.5 s/node, so a class would need more than 2400 nodes, or a future cost row.
- **Fix:**
  - Thread `$suiteId` and `$policy.Object` through `Resolve-NightlySuiteChunks` (call site `:1268`) into `Get-NightlyProfileLaunchPlan`, and resolve admission with them.
  - Refuse at plan time, before any process of that suite starts, and stamp the true W.
  - Add a witness: a profile-aware pty-host plan with P in (1200, 2400] is refused at planning.
  - Add a launch witness: a refused admission never reaches `StartProcess`. This kills M9g.

### D-2: surviving measured-path mutations (smaller cap undetected)

- **N3d:** `nightly-watchdog-admission.ps1:103` with `$raw = [decimal]600` replaced by `$raw = [decimal]$PlannedSeconds` drops the plan's `max(600, ...)` floor (plan `:118`). It stays GREEN in all four modes.
  - Measured P=60, E95=Emax=1, M=0 gives W=600 at the tip and W=120 in the mutant.
  - Fix: add a small-P measured witness row (P=60/E=1/M=0 → 600).
- **N2a:** a branch that accepts an empty calibration object as eligible (C = ceiling, Wmax = 2C) stays GREEN.
  - Each existing guard rejects `{}` on its own: hash, N, stratum, numbers, C > 0 and Wmax > 0. Removing the guard set together (N2b) is RED. But no witness passes `{}` or a calibration with missing fields.
  - Fix: add one empty-calibration row and one missing-field row that assert `unmeasured-cost` at the suite watchdog.

### D-3: an unusable PlannedSeconds silently discards every ratio and yields an uncensored E=1 calibration

- **Where:** `nightly-watchdog-admission.ps1:33-43,76,78-89`.
- **Failure, measured:** PlannedSeconds of null, 0, NaN or `'1,5'` counts every wall in N but adds no ratios, and returns E95=Emax=1 with `Censored=False`.
  - Example: twenty 900 s walls plus a failed 1936 s wall with PlannedSeconds null gives N=21, E95=1, Emax=1, cens=False.
  - Built into a calibration for P=900, that is eligible with W=1800, which kills the 1936 s failed wall. This reopens the g103 scenario that round-1 D-3 closed for bad walls.
- **Reachability:** none today, because no production calibration producer exists. The next calibration slice turns this on.
- **Fix:** treat an unknown or invalid PlannedSeconds as `Censored=true` (or refuse), and add a witness.

## Round-1 defects re-checked

- **D-1:** `:223-226` refuses unmeasured P > suite W for every policy suite. At W it is admitted; at W+0.001 it is refused (session-runner 1800, pty-host/agents-pty/messaging/markdown-pdf/client/scripts 1200, observability 180, npm-ci 600). antiphon and e2e refuse at the planner ceiling first.
- **D-2 closed:** `:218` requires E95 >= 1 and Emax >= E95.
  - E95=0.9999999, Emax=0.9999999, 0.2/0.2, 0.01, E95 > Emax, NaN/Infinity/`'1,5'`/null factors: all go to `unmeasured-cost` 3600.
  - E95=1e300 throws a decimal conversion, which is a refusal and not a smaller cap.
- **D-3 closed for walls:** `:58-72`. These walls all censor, and N is not padded: `'1936,5'`, null, `'n/a'`, `'0'`, 0, -1, NaN, `'Infinity'`, `''`, `'1e400'`, a TimeSpan, and a missing Wall property.
  - A double 1936.5, a decimal 1936.5, `'1936.5'` and `' 1936.5 '` are all kept as N=21 with W=2040.
  - Under de-DE, the double is kept and the string `'1936,5'` is censored.
- **N:** 19, `'19.9'`, `'20.0'`, 20.5, -1, `'1e2'`, null, `'x'` and 2^31 are all ineligible. 20 and `'20'` are eligible.
- **Bad P:** 0, -5, NaN, ±Infinity, `'1,5'`, `'1936,5'`, `'n/a'`, `''`, `$true` and `'0x10'` are refused as `watchdog-seconds-invalid`. 1e308 and decimal.MaxValue are refused as `launch-estimate-ceiling`. Null keeps the suite watchdog.
- **Missing policy:** keeps a larger cap (fallback 3600). A zero policy value also falls back to 3600.
- **D-4:** all 22 of the author's mutations are RED in my scratch reruns, using a `git archive` copy and all four fixture modes: M1, M1b, M2, M2b, M3, M4, M4b, M5, M5b, M5c, M5d, M6, M6b, M6c, M6d, M6e, M7c, M7d, M8, M8b, M9, M9b.
- **Reverting each round-2 fix is RED:** D1r, D2r, D2s, D3r, D3s.
- **The brief's three new mutations:**
  - Reversing the E95/Emax order (N1) is RED.
  - Accepting an empty calibration (N2a) survives; see D-2.
  - Max in the wrong direction is RED as N3a (min of the factors), N3b (min(P, cap)) and N3c (flipped comparison). The floor variant N3d survives; see D-2.
- **Fake-owner seam is real:** the timeout mode drives `Invoke-AntiphonNightlyTests` against a seam `StartProcess`. Baseline records 1200000 ms with coverage false.
  - Mutating `nightly-tests-impl.ps1` turns each of these RED: Min(600000) (M9), dropping `-PolicyObject` (M9b, which goes to 3600000), a constant 600000 (M9c), `coverageComplete=$true` on timeout (M8) and `testsPassed=$true` (M8c).
  - The substring `wired` check in cap mode still exists, but it is no longer the only witness.

## Byte-identity, ASCII, definitions

- `git diff cbdf5f76b HEAD` is empty for:
  - `tests/test-execution-policy.json`
  - `scripts/lib/nightly-class-costs.json`
  - `scripts/lib/nightly-coverage.ps1`
  - `scripts/lib/release-authority.ps1`
  - `scripts/lib/nightly-policy.ps1`
- The suite watchdog constants are unchanged.
- No csproj, props, targets or json file changed.
- 0 non-ASCII bytes in the four PowerShell files.
- `check-evidence-diff.ps1 -BaseRef 659dc8534 -HeadRef d1d49d890` exit 0: commits=6, entries=1, violations=0.
- I did not reproduce `check-os-witnesses.ps1`; it needs an `-EvidencePath` inventory I do not have. The author reports exit 0.

## Hang detection

- **Report-only:** confirmed. There are no Stop-Process, taskkill, TerminateProcess or `.Kill(` tokens in the heartbeat or admission libraries. The absolute watchdog is the only kill authority.
- **Plan note:** the round-2 note says explicitly that a healthy 30-minute quiet test is flagged `hangSuspected` after 20 minutes, and that a flag is not a kill.
- **Operator action owed:** in `supervise-rc.ps1`, which is outside this checkout, on each heartbeat tick:
  1. Call `Read-QualificationDiagnosticProgress -Path <chunk execution .diag> -Offset <offset persisted from the previous tick> -HeartbeatPath <heartbeat file>`.
  2. Pass the result as `-DiagnosticProgress` to `Update-QualificationSupervisorHeartbeat`.
  3. Persist the returned `Offset`.

  Until that is done, `lastCompletedUid`, `inFlightUid`, `lastPhase` and `lastDiagGrowthAtUtc` stay null on a live qualification. No production caller exists in the repo.

## Merge

Simulated with chained `git merge-tree --merge-base`, without writing to the shared refs.

- **rc `ade542225` with the S1 range (`8edda9996..cbdf5f76b`) then S3:** clean, tree `c6b92691d`.
- **S3 alone on rc:** conflicts only on the plan doc (modify/delete). rc lacks the plan, which S1's `c4001e86c` carries.
- **master `98fc3ceb6`:** S1+S3 chain is clean (tree `00e638741`). S3 alone conflicts only on the plan doc.
- **Whole-branch `merge-tree` against master:** 10 conflict paths, all rc-line history. None of them is the port.
- **No definition files are touched**, which matters for the land-graph approval.
- **Manifest-publication plan `bb0b355c`:** its M2 slice edits `nightly-tests-impl.ps1` next to S3's admission hunk (`:1392-1411`) and timeout-disposition hunk (`:1532-1537`). Serialize the two.

## Checkpoints: run 20261030-042504-4fdf (tip d1d49d890, Linux, GREEN exit 0)

The tool was bootstrapped through `build-slot.ps1` (lease aeea5111, 8 s, UseAppHost=false). The run took one build `bin-rvc762` and its own slots (all `waited=0s`). `unlisted: none`. Wall 10m32s. `validate --expected-source-sha d1d49d890`: **SOURCE VALID rows=8**. All rows `sourceState=clean buildSource=verified`.

| Row | Filter (abbrev.) | Result |
|---|---|---|
| CP-8 | CheckpointExecutorLogTests 3 exact | 3/3 |
| CP-9 | RcFullWatchdogTests + ReleaseGatePolicyTests | 10/10 |
| CP-17 | HeartbeatExistingContract | 1/1 |
| CP-18 | C1397ExistingContract | 1/1 |
| CP-19 | RcRosterRepairTests/Costs | 1/1 |
| RV-1 | RcRosterRepairTests/* | 3/3 |
| RV-2 | WindowsUnitPlatformAdmissionTests C1210 Guard/Native/Roster/Audit | 4/4 |
| RV-3 | C1514LandingSplitBudgetTests (C1404, C1577, C1584, C1410, G2865) | 5/5 |

The row table is `.antiphon/review-c762dbd7-plan.md` (ignored).

**Not run:**
- the Unit lane;
- native Windows CP-2, CP-4, CP-6 and CP-7;
- RV-2's Windows case `C1210_Windows_linux_only_outcomes_are_exact`, which needs Windows placement.

PCs remain pending.

## Advisories

- **A-1:** an unmeasured P equal to the suite watchdog is admitted with zero headroom. pty-host P=1200 gets W=1200, while antiphon's admitted P has 1.5x headroom (2400/3600). Whether unmeasured suites need a margin is the caller's call. The plan only says to "retain the existing watchdog".
- **A-2:** a calibration with no `Censored` field is treated as uncensored (`$censored -ne $true`).
- **A-3:** this is pre-existing `Get-NightlyWatchdogMs` behaviour. A negative policy value gives W=-0.005 s and refuses. A 1 ms value admits P ≤ 0.001. `'abc'` throws.
- **A-4:** a measured W may exceed the suite policy watchdog. For example, pty-host P=1100 gets W=2220. This is by design: the plan says not to clamp, and Wmax governs.

## Cleanup

- The checkpoint tool removed its `bin-rvc762` output.
- I left `tools/Antiphon.Checkpoints/bin-rvc762t` and `src/Antiphon.HostCleanup/bin-rvc762t` (ignored) in place, because the brief forbids `rm -rf`. Delete them by literal path.
- The scratch mutant copies stay in the session scratchpad.
