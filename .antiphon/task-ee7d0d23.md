# S3 round 2: admission rule fixes and witnesses

Outcome: the four admission defects are fixed on `feat/card-task-ee7d0d23`. The closed checkpoint run `20261030-040251-7020` is green at `5548074c83f3731e45c16dee030724b0491734dc` (5 rows, 0 failed, 0 skipped). Production still does not calibrate; an ineligible or unmeasured launch keeps that suite's existing watchdog, and a planned cost above that watchdog is refused.

## Commits

| SHA | Subject |
|---|---|
| `6a13ca7999f4e0650cb774a0c71f38a64e07fcf7` | test: fail when an unmeasured plan exceeds the suite watchdog |
| `cba6a8e41b8abeb78b70c38c379b5a6c3e6fe935` | test: fail when a measured error factor is below one |
| `c0f4ba192f324e4fc4d32185e34a367194188cda` | test: fail when an unparseable wall is dropped from calibration |
| `fb6c86611f9e9916d70068d0e8f4eb05b48f17e2` | fix: refuse unsafe watchdog admission and censor bad walls |
| `5548074c83f3731e45c16dee030724b0491734dc` | fix: witness launch admission and let the timeout disposition own coverage |

The first three commits were red on the then-current tip for the stated reason. `fb6c86611` is the D-1/D-2/D-3 fix in `scripts/lib/nightly-watchdog-admission.ps1` only. `5548074c8` is the D-4 witness and the timeout-disposition change, plus the plan note. Checkpoint source is `5548074c8`, clean, `buildSource=verified`.

## Rules

- Unmeasured path: if planned seconds exceed that suite's existing watchdog, admission is refused (`suite-watchdog-ceiling`) and the planner must not schedule it. P equal to the suite watchdog is still admitted. The pairing holds for every policy suite. Session-runner P=2400 is refused at 1800 s. pty-host, agents-pty, markdown-pdf, and messaging P=1800 are refused at 1200 s. Antiphon 3600 s and e2e 7200 s already covered their planner ceilings.
- Measured eligibility requires E95 >= 1 and Emax >= E95. E95=Emax=0.2 and E95=0.01 stay on the unmeasured path at the suite watchdog (3600 s for antiphon), not 960 s or 600 s.
- An unparseable or non-positive wall, including `1936,5`, null, `n/a`, 0, and negative, censors the calibration. Twenty 900 s walls plus one bad Failed wall resolve unmeasured at 3600 s and do not kill elapsed 1936 s. A zero wall does not pad N. A real de-DE double 1936.5 is kept. Parsing uses invariant culture.
- A measured watchdog is not clamped down to the suite cap. W(C) above Wmax refuses `policy-max-mismatch`. P above the calibrated bound C refuses `launch-estimate-ceiling`.
- Native timeout coverage comes only from `Get-NightlyWatchdogTimeoutDisposition`. The later `if ($run.TimedOut) { $coverageComplete = $false }` in `scripts/lib/nightly-tests-impl.ps1` was removed because it hid a mutation that marked the timeout complete. The launch still passes `-PolicyObject` and uses `admission.WatchdogMilliseconds` with no 600000 ms floor. The pty-host witness records 1200000 ms and coverage incomplete, including when a TRX file is present.

The healthy 30-minute quiet case is not killed. It is flagged `hangSuspected` after 20 minutes of silence. That flag is report-only. A flag is not a kill. The absolute watchdog remains the only kill authority. This is in the plan note in `docs/superpowers/plans/2026-10-29-card-1514-rc-full-speedups-plan.md`.

`TimeoutKeepsCoverageIncomplete` stays at 60 seconds. The witness itself finished in 5 seconds, so the timeout was not widened.

## Byte-identical to parent `cbdf5f76bf4b14845f1e15fd8961065ace0c14c6`

| Path | sha256 |
|---|---|
| `tests/test-execution-policy.json` | `427c6b3bf39e55c26ba4fc5f27c90d6f44067c19d7429c637e548be1c699f48d` |
| `scripts/lib/nightly-class-costs.json` | `507caee1c73ef5271c285000dc0c53b06aba51c491c339d0b21f1cc0038b288c` |
| `scripts/lib/nightly-coverage.ps1` | `3c9cd3653bc853ff06f42e0d67b36bd89e2e57b46a843e40089535fa8998b26c` |
| `scripts/lib/release-authority.ps1` | `d4a56ad19a2494f5c8c5d6439e7cde903725dfd174f3c32c8fbc37f9c595db21` |
| `scripts/lib/nightly-policy.ps1` | `2b64405c0317cb2089a85e8b3a14ff9d30940d3ce2f375eaeac6bb636ecf0359` |

Watchdog milliseconds are unchanged: npm-ci/npm-build 600000, dotnet-build/pty-host/agents-pty/messaging/client/scripts/markdown-pdf 1200000, session-runner 1800000, antiphon 3600000, e2e 7200000, observability 180000. No definition file was edited and `-Update` was not run.

`scripts/check-evidence-diff.ps1 -BaseRef 659dc8534906d1bbb60400e3b3301751e1dfc5e7 -HeadRef 5548074c83f3731e45c16dee030724b0491734dc` exited 0: commits=5, entries=0, violations=0.

`scripts/check-os-witnesses.ps1` exited 0 (syntax/inventory only). The unchanged `$IsLinux` fallback at `nightly-tests-impl.ps1:22` is classified non-behavior for this slice; the timeout witness supplies `HostPlatform` through the seam. Restart census matches the two changed `scripts/lib` files. No shared host was restarted. Native Windows owner execution is pending.

## Checkpoints

Run `20261030-040251-7020`, tool bootstrapped through `scripts/build-slot.ps1` to `bin-s3r2/` with `UseAppHost=false` (slot waited 0s). The run itself took its own slot and was not double-leased. Manifest row ceilings: CP-8=14, CP-9=6, CP-17=2, CP-18=2, CP-19=2 minutes. `slotWaitMinutes=10`. `--total-timeout 26m` (twice the 13 minute estimate sum). `--hard-wall-timeout 86m` (26 plus 10 minutes for one build and five rows). Actual slot waits were 0s. Build phase 245 s. Execution 419.5 s. Admission-only 0.005 s. Wall 6m59s. Unlisted commands: none. Verdict GREEN exit=0. Source clean, `buildSource=verified`.

```text
CHECKPOINT CP-8 commit=5548074c83f3731e45c16dee030724b0491734dc build=ok filter=/*/Antiphon.Tests.Checkpoints/CheckpointExecutorLogTests/(concurrent_callbacks_append_each_line_once_without_overlap*)|(note_returns_when_the_sink_resumes_on_the_caller_context*)|(repeat_dispose_completes_after_the_sink_dispose_faults*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ee7d0d23/.antiphon/checkpoints/20261030-040251-7020/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=5548074c83f3731e45c16dee030724b0491734dc sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=5548074c83f3731e45c16dee030724b0491734dc build=reused filter=/*/Antiphon.Tests.Scripts/(RcFullWatchdogTests*)|(ReleaseGatePolicyTests*)/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-ee7d0d23/.antiphon/checkpoints/20261030-040251-7020/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=5548074c83f3731e45c16dee030724b0491734dc sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=5548074c83f3731e45c16dee030724b0491734dc build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogHarnessTests/HeartbeatExistingContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ee7d0d23/.antiphon/checkpoints/20261030-040251-7020/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=5548074c83f3731e45c16dee030724b0491734dc sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=5548074c83f3731e45c16dee030724b0491734dc build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogHarnessTests/C1397ExistingContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ee7d0d23/.antiphon/checkpoints/20261030-040251-7020/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=5548074c83f3731e45c16dee030724b0491734dc sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=5548074c83f3731e45c16dee030724b0491734dc build=reused filter=/*/Antiphon.Tests.Scripts/RcRosterRepairTests/Costs executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ee7d0d23/.antiphon/checkpoints/20261030-040251-7020/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=5548074c83f3731e45c16dee030724b0491734dc sourceState=clean buildSource=verified
```

CP-9 stayed at 10 results (4 `RcFullWatchdogTests` + 6 `ReleaseGatePolicyTests`) by extending existing methods. No test method was added. Evidence: `/work/worktrees/task-ee7d0d23/.antiphon/checkpoints/20261030-040251-7020/report.md`.

## Mutation table

Scratch only. Nothing mutated was committed. Cap and progress mutations ran against a copy of `scripts/lib`. Launch mutations ran on this worktree and the two source files were restored; the committed tree is the green tree (witness exit 0, launch 1200000 ms, coverage false). A symlink-tree attempt at the launch path failed the project-universe check because `Get-ChildItem -Recurse` does not follow the `tests` link. That attempt is not the proof. The real-tree observations are.

| Mutation | Result | Killing observation |
|---|---|---|
| M1 drop failed walls | RED | failed-wall-dropped; g103 and deadline watchdogs 3600 |
| M1b drop the tail term | RED | g103-watchdog 1800; g103-killed; deadline-watchdog 600 |
| M2 drop the unmeasured fallback | RED | unmeasured-replay-not-3600; unknown-cost-used-600 |
| M2b null planned returns 600 | RED | legacy-estimate-refused |
| M3 price default/pace as measured | RED | basis-reduced pace 600; basis-reduced default 600 |
| M4 min(watchdog, suite) on the measured return | RED | measured-clamped-to-suite W=3600 adm=True |
| M4b min on the unmeasured path | RED | unknown-cost-not-3600; unknown-cost-used-600 |
| M5 ignore the planner ceiling | RED | ceiling-above-admitted; e2e-above-admitted |
| M5b drop W(C)>Wmax | RED | ceiling-above-wmax-admitted W=1800 reason=measured |
| M5c clamp to Wmax | RED | watchdog-clamped-down; policy-max-reason |
| M5d drop P>C | RED | planned-above-bound-admitted W=2400 reason=measured |
| M6 ignore censored | RED | censored-wall-reduced |
| M6b n>=19 | RED | n19-reduced |
| M6c ignore stratum | RED | stratum-mismatch-applied |
| M6d ignore hash | RED | hash-mismatch-applied |
| M6e drop the E95>=1 clamp | RED | e95-clamp-removed 0.2 |
| M7c drop diagnostic growth | RED | diag-stamp-moved; diagnostic-growth-stamp-missing |
| M7d drop copy-forward | RED | completed-uid-lost |
| M8 set coverageComplete true on timeout | RED | launch-timeout-marked-complete; ms=1200000; cov=True |
| M8b disposition follows TrxPresent | RED | trx-present-marked-complete; launch-timeout-marked-complete |
| M9 Min(600000, admission) | RED | launch-timeout-ms 600000 |
| M9b drop -PolicyObject | RED | launch-timeout-ms 3600000 |
| baseline cap / progress | GREEN | exit 0 |
| baseline timeout on the real tree | GREEN | ms=1200000 cov=False |

## Owed

- `supervise-rc.ps1` is outside this checkout. Until it passes `DiagnosticProgress`, the new progress fields stay empty on a live qualification. Wire that call before treating the fields as populated.
- Native Windows rows CP-2, CP-4, CP-6, and CP-7 were not run. Linux does not substitute.
- Windows RV-2 was not run.
- Positive-control cycles for this slice are not the post-land method-scoped mutation pass. The scratch mutations above are the Code proof, not that pass.
- No shared AppHost or session runner was restarted.

## Cleanup

Deleted literal `src/Antiphon.HostCleanup/bin-s3r2` and `tools/Antiphon.Checkpoints/bin-s3r2`. The tool deleted the project `bin-rcspeed-b2log/` output. The run keeps `.antiphon/checkpoints/20261030-040251-7020/builds/bin-rcspeed-b2log/build.log`.

--- next stage ---
next: review
handoff: Review S3 round 2 on feat/card-task-ee7d0d23. Verified source is 5548074c83f3731e45c16dee030724b0491734dc. Closed run 20261030-040251-7020 is green: CP-8 3/3, CP-9 10/10, CP-17 1/1, CP-18 1/1, CP-19 1/1. Admission refuses an unmeasured plan above the suite watchdog, requires E95>=1, and censors a bad wall. The timeout disposition owns coverage. supervise-rc DiagnosticProgress, native CP-2/4/6/7, Windows RV-2, and post-land positive controls remain owed.
artifact: .antiphon/task-ee7d0d23.md
