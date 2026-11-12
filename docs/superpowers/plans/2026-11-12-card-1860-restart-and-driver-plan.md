# CARD-1860: missing logs and DerivedImpact checkpoint prerequisite

Code / landing owner: `042b61c0-ab0d-49e7-a298-93ce0cf8792b`.
Base: `cc48c70050a2932eddf4abe18cea8659d478c33e`. Final verification.
This brief has no separate landed CARD-1860 Plan/TestDesign; this scoped design records
its supplied decisions and closed ordinary scope. CARD-1851's existing plan is the
checkpoint consumer repaired for A-3. A-2 Windows skip qualification is out of scope.

## Implementation and verification design

S1 commits failing regression witnesses before the production repair. Run CP-1 and CP-2
once at S1. S2 adds the missing-directory catch and declares the driver build prerequisite
in CARD-1851's closed checkpoint list. Run CP-0,CP-3..CP-6 at S2 with `--serial` so the
prerequisite completes before any driver-consuming tests. Its explicit native-default
build is intentional: regenerate-derived-impact-index.ps1 -NoBuild looks there.

- V-1: real New-RunnerRestartPlatform Census treats absent logs as two absent PID files,
  without creating logs, under both -File and &. Invoke-RunnerRestart then reaches the
  expected restart controls and healthy result through isolated control/health boundaries.
- V-2: a fresh checkout's driver-absent regression fails CP-2 before repair. The explicit
  CP-0 prerequisite builds the driver, then the unchanged changed/unchanged regeneration
  witness succeeds in CP-4. The durable CARD-1851 plan declares this same prerequisite
  and the consuming DerivedImpactIndexTests row. This chooses the brief's prerequisite
  option; no build is launched from inside a test and no dependency cycle is introduced.
- R-1: existing unreadable, locked, invalid, overflow, zero and wrapper PID fail-closed
  witnesses stay green. Added Hard/WaitOnly unreadable and garbage variants must take no
  control and retain their PID path and a clear error. Missing PID and live PID stay green.
- R-2: run touched RunnerRestartScriptCompatibilityTests, named DerivedImpactIndexTests
  and diff-selected ProcessSpawnLimitTests in all three declaring projects. This checks
  real child-process serialization. No limiter or rostered guard class is edited; the
  brief's minimal-testing exception omits the guard union and the whole Unit lane.
- V-3: manual closure/caller inspection, ASCII bytes, OS-witness declaration/lint,
  base..HEAD impacted resolution, evidence-diff and merge-tree. No live restart/control.
  This repair is portable filesystem behavior in a Windows runner factory. Native Windows
  PowerShell 5.1/7 cases and desktop & restart remain caller-owned, pending, never passed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1860-red-runner/ | red-missing-logs | `/*/*/RunnerRestartScriptCompatibilityTests/Missing_logs_is_an_absent_pid_without_creating_the_directory` | V-1 | expected red in both entry forms | 2 | 2 |
| CP-2 | S1 | tests/Antiphon.Tests -> bin-c1860-red-driver/ | red-missing-driver | `/*/*/DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged` | V-2 | expected missing driver red | 1 | 3 |
| CP-0 | S2 | n/a | driver-prerequisite | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --nologo` | V-2 | exit 0; native-default driver exists before CP-4 | n/a | 4 |
| CP-3 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-c1860-final-runner/ | restart-compatibility | `/*/*/RunnerRestartScriptCompatibilityTests/*` | V-1,R-1 | >= 20 executed, 0 failed; native Windows skips pending | 20 | 3 |
| CP-4 | S2 | tests/Antiphon.Tests -> bin-c1860-final-driver/ | driver-and-impact | `/*/*/(DerivedImpactIndexTests*)\|(ProcessSpawnLimitTests*)/*` | V-2,R-2 | both classes, 0 failed | 2 | 4 |
| CP-5 | S2 | CP-3 | runner-limiter-impact | `/*/*/ProcessSpawnLimitTests/*` | R-2 | 0 failed | 2 | 1 |
| CP-6 | S2 | tests/Antiphon.Agents.Pty.Tests -> bin-c1860-final-pty/ | pty-limiter-impact | `/*/*/ProcessSpawnLimitTests/*` | R-2 | 0 failed | 2 | 2 |

Use checkpoint tool run --plan with --after S1 or --after S2, --serial,
--expected-source-sha committed HEAD. Record actual tested SHA, fresh TRX method counts,
unaltered CHECKPOINT lines, source/build provenance, slot= and waited=. CP-1/CP-3's
Windows skips are the explicitly deferred A-2 gate; do not claim a strict clean receipt.
No row timeout is increased; no repetitions after green. Unlisted builds are limited to
the leased checkpoint-tool bootstrap, necessary because the fresh checkout lacks it.

## Performance budget

Provisional, not measured p95: S1 up to 5 minutes tests/builds; S2 up to 14 minutes,
plus separate slot wait. Gather one red/one green selection; no loaded repetitions.
Existing child exit budgets remain 45 seconds and regeneration 180 seconds. Imported
checkpoint floors remain unchanged, not newly measured budgets (CARD-1404). The explicit
native prerequisite compiles the existing driver -> Antiphon.Tests graph once, then each
checkpoint builds its owned isolated graph. No new project/dependency graph, container,
web host, database, migration or HTTP setup is introduced. Every child test needs its own
script scope/scratch files to observe closure semantics. Test setup/body/teardown IO
subdivisions without spans are unknown/remainder. Retain per-test TRX and build/slot/run
phase evidence under ignored .antiphon/checkpoints; no full assembly or repeat is needed.
Over 30 minutes triggers Debug; no widened timeout or assertion.

## Pending Mutation inventory

Operator waived deliberate mutations for this task (2026-11-12). Nothing here credits a
PC as passed. Candidate pack: docs/superpowers/mutations/2026-11-12-card-1860-restart-and-driver.md.
PC-1/missing-directory-catch: remove only DirectoryNotFoundException handling; the exact
Missing_logs_is_an_absent_pid_without_creating_the_directory method must fail both forms.
PC-2/swallowed-unreadable and PC-2/invalid-as-absent: suppress unreadable or invalid PID;
Pid_read_errors_stop_restart_before_any_control must fail, including Hard/WaitOnly variants.
PC-3/omitted-driver-prerequisite: omit CP-0 in an owned fresh checkout; the exact
regenerate_script_reports_changed_then_unchanged method must fail for missing driver.
All pending for caller-commissioned method-scoped SourceLanding Mutation if later required;
Mutation owns deliberate red/restore/green and missing controls. Restart: none.
