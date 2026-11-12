# CARD-1851: PID read failures and nightly health closure scope

Code owner / landing owner: `2eeb94f5-6f28-4c2c-8b5a-f53a505460f7`.
Base: `aa5e14b61b4df4be16afbbc9b1d81ce49de5bc58`. Final verification.
The brief supplies no earlier Plan/TestDesign artifact. This scoped implementation plan
records its two acceptance requirements and the ordinary selections before execution.

## Implementation and verification design

S1 commits the regression tests before the fix. S2 repairs production and reruns CP-1,
then the affected nightly integration rows. No live restart, session control, Windmill
request, message or deployment is authorized or needed for these tests.

- V-1: the real `New-RunnerRestartPlatform` PID reader stops `Invoke-RunnerRestart`
  before any control for directory-at-file, exclusive lock, malformed or overflowing PID,
  and missing-parent errors. Only `FileNotFoundException` means an absent PID file.
  Missing PID and live child PID are positive controls for the expected restart paths.
  Preserve the unreadable PID path. Drive the factory under a separate `&` script scope;
  replace only control and health boundaries so no host process is stopped.
- V-2: all three `New-NightlyProductionWindmillApi` closures resolve their actual HTTP,
  job conversion and London due-time helpers under `&`, without global helper imports.
  Fake only `Invoke-WebRequest`; verify registration, schedule, real job conversion,
  GET URL/credential/deadline contract, five-result fetch cap, failed fetch and missing
  result fail-closed behavior, removal of untrusted inline results, and summer/winter UTC.
- R-1: existing restart compatibility behaviors and ASCII/parser coverage stay green.
  Extend the existing ASCII/parser inventory to the nightly entry and imported helpers.
- R-2: impacted index `nightly-live-lane` selects `LiveLaneRunnerTests` and
  `NightlyCoreTests`. Run both classes; record the actual base..HEAD index resolution.
- R-3: existing nightly job adapter and bounded-result-fetch integration methods remain green.
- V-3: manually inspect every changed closure and transitive helper; run ASCII byte checks,
  OS witness declaration/lint, full-range evidence-diff and merge-tree. Record native Windows
  obligations explicitly. No OS-selected branch is changed; both production factories are
  driven on Linux with native filesystem/PowerShell objects. Native Windows PowerShell 5.1
  argument rows and an operator-owned desktop `&` restart remain pending for the caller.

The brief's minimal-testing exception omits the guard union unless a rostered policy test is
touched. This task touches no such test, limiter, policy, schema or generated index. No whole
Unit lane, full assembly, loaded repetitions or deliberate mutants run in Code.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1851-runner/ | script-compatibility | `/*/*/RunnerRestartScriptCompatibilityTests/*` | V-1,V-2,R-1 | >= 13 executed, 0 failed; native Windows rows pending on Linux | 13 | 3 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1851-nightly/ | nightly-impact | `/*/*/(LiveLaneRunnerTests)\|(NightlyCoreTests)/*` | R-2 | all listed, 0 failed | 5 | 4 |
| CP-3 | S2 | CP-2 | nightly-adapters | `/*/*/NightlyVerificationContractTests/(C544_ProductionJobAdapter)\|(C545_JobResultFetch)` | R-3 | all listed, 0 failed | 2 | 2 |

Code runs CP-1 once at S1 for the expected red witness, then all three rows at S2 with
`--expected-source-sha <committed HEAD>`. Inspect fresh TRX by class/method/arguments and
validate receipt source/build provenance. Preserve all original CHECKPOINT lines.
Windows must run CP-1 at the reviewed source using both installed shells; Linux skipped
Windows rows are pending obligations, never native green certificates.

### Cost

Ordinary final checkpoint estimate: 9 minutes, plus the initial red CP-1 (3 minutes),
authoring and manual checks. Mutation floor is provisional 6 minutes for the three exact
method controls below plus discovery/reporting. No full assembly is justified by this scope.

## Performance budget

These are provisional estimates for CARD-1851, not measured p95. Collect one red and one
green compatibility sample plus one affected integration sample; no repeats after green.
Use checkpoint driver defaults without increasing a timeout. Existing fixture children
have a 45-second exit budget. Each new test starts one PowerShell child with scratch
filesystem state; fresh script scope is the behavior under test. No database, container,
web host, migration, snapshot or real external HTTP setup occurs in the new tests.
Existing project graph: SessionRunner.Tests references SessionRunner, CustodyTestChild and
FakeLlmApi; Antiphon.Tests owns the existing nightly harnesses. No production .NET graph
changes. Both selections are Integration; the affected app owners are runner and nightly.
Retain TRX durations, checkpoint build/slot/execution timings and phase journal where
available under ignored `.antiphon/checkpoints/`. Child setup/body/teardown and filesystem
IO subdivisions without spans are unknown/remainder, not zero. HTTP is a bounded synthetic
cmdlet boundary (production timeout remains 30 seconds). Slot waits are separate and finite.
Any run over 30 minutes needs Debug under the standing policy; do not widen a timeout.

## Pending SourceLanding Mutation inventory

All deliberate controls stay pending for the caller-commissioned Mutation task at L.
Candidate pack: `docs/superpowers/mutations/2026-11-12-card-1851-script-closures.md` (unbound).

- PC-1 / swallowed-read: restore catch-all PID read suppression. Exact method:
  `RunnerRestartScriptCompatibilityTests/Pid_read_errors_stop_restart_before_any_control`.
  Directory, exclusive lock, invalid, overflow and missing-parent arguments must detect it.
- PC-2 / HTTP-name: change the captured HTTP helper back to a by-name call in each of
  GetRegistration, GetSchedule and GetJobs (three independent variants).
  Exact method: `RunnerRestartScriptCompatibilityTests/Nightly_health_closures_resolve_helpers_under_the_call_operator`.
- PC-3 / conversion-name and due-name: restore each by-name helper lookup in GetJobs
  (two variants). Same nightly method must fail actual mapping or expected UTC assertions.

Mutation owns method-scoped red/restore/fresh-build/green, missing-control discovery and
binding to the actual published SHA. Ordinary regressions here are not mutant evidence.
Restart: none for Code; caller owns any later desktop runner acceptance.
