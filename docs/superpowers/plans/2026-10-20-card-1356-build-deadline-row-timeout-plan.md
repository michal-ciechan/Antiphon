# CARD-1356 — scheduled builds use the row timeout

Plan against fetched master `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`.
Read CARD-1356 in full; no builds or tests run during Plan. Verification design is
folded into this dispatch. Code budget: **30–60 minutes**; next: **code** after
CARD-1368 lands.

## Ground truth and root cause

References below are at the master SHA above unless explicitly marked otherwise.

| Card assumption | What the code does | Consequence |
|---|---|---|
| The build dies at the manifest's 15-minute default. | `TimeoutDefaults.RowMinutes` defaults to 15 (`tools/Antiphon.Checkpoints/Manifest/CheckpointManifest.cs:39`). `BuildOneAsync` passes `TimeSpan.FromMinutes(Math.Max(15, request.Manifest.Timeouts.RowMinutes))` to the actual deadline wrapper (`tools/Antiphon.Checkpoints/Execution/RunScheduler.cs:421–425`). `RowTimeout.RunWithDeadlineAsync` arms cancellation with that value (`tools/Antiphon.Checkpoints/Execution/RowTimeout.cs:28–30`). | Confirmed code defect. A 30- or 40-minute row still has a 15-minute build deadline. The reported host load is incident context, not measured in this Plan. |
| `--row-timeout 40m --total-timeout 90m` should let the build continue. | CLI parsing sets `RunRequest.RowTimeoutMinutes` (`tools/Antiphon.Checkpoints/Program.cs:647`); `CheckpointApp` forwards it as `SchedulerRequest.RowTimeoutOverride` (`tools/Antiphon.Checkpoints/CheckpointApp.cs:216`). Only `RunRowAsync` consumes it (`RunScheduler.cs:522–524,549`); the build call ignores it. | On this master, that workaround cannot change the scheduled build's 15-minute timer. A successful retry alone would not prove otherwise. |
| A row's derived timeout should cover its build. | Plan import derives `max(15, 3 × estimate)` (`tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs:154`). Scheduled builds are shared and execute once before dependent rows (`RunScheduler.cs:155–162`). The standalone `row` path already passes `request.Deadline` to its build (`tools/Antiphon.Checkpoints/Execution/RowRunner.cs:208–212`). | Fix the scheduled build path; resolve all selected dependents of that build. Keep existing separate build/test phase budgets. |
| Slot/load scaling might be necessary. | Build-slot acquisition precedes the build timer (`RunScheduler.cs:364,421`); the linked total-run timer covers scheduling and acquisition (`RunScheduler.cs:50–52`). | Preserve slot-wait and total deadlines. The build budget begins at the existing driver boundary; no load estimator is required. |
| CARD-1368 only overlaps cleanup and reader code. | Reviewed tip `359cc912235d840e9f94202592e86480d87db468` already carries CARD-1356 commit `00349d0b1b15a9fa2c9c72d2fce762306fd4b1c7`. It adds `RowDeadlineMinutes`, `BuildDeadlineMinutes`, their build call, and recorded `DeadlineMinutes` (`RunScheduler.cs:299–318,347–351,445–449` **at that tip**). It also adds two scheduler tests. | Reuse this implementation after landing. Its tests check calculation and recorded values; they do not hold a build past the old deadline and observe the actual cancellation token. That behavioral witness is the remaining work. |

## Decisions

- **D-1 — Reuse the queued fix.** Code starts from master containing CARD-1368's
  landed equivalent; inspect the diff because landing can change SHAs. Direct file
  overlap is `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs` and
  `tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs`. The dependency also
  changes `CheckpointApp.cs`, `Execution/RowRunner.cs`, `Report/ReportModel.cs`,
  `State/RunState.cs`, `CheckpointTestSupport.cs`, and `docs/testing-and-build.md`.
  Preserve its cleanup/reader changes. Rejected: independently copying or
  cherry-picking the pending branch, which duplicates already reviewed work.
- **D-2 — Keep the dependency's exact policy.** Without an override, a build gets
  `max(15, manifest row default, effective timeout of each selected non-command
  row referencing this build)`. Each row uses explicit `TimeoutMinutes`, else
  existing estimate/default derivation. An override replaces that result,
  including shorter values, with the existing ceiling-to-minutes/minimum-one
  normalization. Preserve the manifest floor for compatibility. Rejected:
  first-row-only selection (shared builds can serve a longer row), all manifest
  rows (unselected rows must not extend a build), increasing every default, or
  load-based resizing (unnecessary variable policy).
- **D-3 — Exercise the real timer using an instance clock.** Add an internal
  three-argument `RunScheduler` constructor accepting `TimeProvider`; the existing
  constructor supplies `TimeProvider.System`. Pass that clock only to the build's
  deadline wrapper. Add a `RowTimeout.RunWithDeadlineAsync` overload with the clock
  before the final cancellation token; retain the current signature. Its shared
  implementation uses a provider-backed deadline CTS linked with caller
  cancellation, with a nonpositive deadline still disabling only the deadline.
  Preserve timeout classification and confinement handling. Reuse
  `ControlledTimeProvider` and `FakeDriver`; the test project already references
  TimeProvider testing. Rejected: a 15-minute sleep, source-text assertions, or a
  fake resolver that never exercises the production cancellation path.
- **D-4 — One defect-red and one narrow green.** Since the fix will already be
  landed, the red commit temporarily restores **only the deadline argument** to
  the old master expression, while retaining the clock seam and new witness.
  Restore the fixed argument in the next commit. This deliberate defect is never
  the final candidate. The final delta adds the clock seam and behavioral test,
  without duplicating the dependency's fix or adding a separate mutation battery.

## Slices

| Slice | Files and work | Completion |
|---|---|---|
| S1 | `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`, `tools/Antiphon.Checkpoints/Execution/RowTimeout.cs`: D-3 clock seam, and D-4's single old-expression substitution. `tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs`: add `build_timer_uses_selected_row_deadlines_and_override`, using existing helpers without editing their files. | Commit/push the truthful defect-red slice; run CP-1 once. Require the expected assertion failure, not a fixture/build failure. |
| S2 | `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`: restore the inherited `TimeSpan.FromMinutes(deadlineMinutes)` argument, retaining clock injection. | Commit/push before CP-2. Check the final diff contains no deliberate regression; validate CP-2's exact source receipt and report counts. |

### Cost

Authoring and dependency inspection: 20–35 minutes. Checkpoint floor: 16 minutes
(8 + 8), including two isolated test-project builds; test bodies should take
seconds. Expected Code total: 36–51 minutes, budget 30–60. Slot contention is
reported separately; it does not authorize an unleased run or a broader filter.

## Verification design

**W-1:** one new TUnit method,
`RunSchedulerTests.build_timer_uses_selected_row_deadlines_and_override`.
Use fresh scratch/project/output directories for each arm, an immediate fake slot
grant, a fake build driver held behind a completion barrier and observing its
actual cancellation token, and instant test drivers writing normal fixture TRX.
Await build entry and the controlled timer registration before advancing time.
The existing scope owns all tasks; always release/cancel and await them in
`finally`, including after a red assertion. No real child build or wall-clock
15-minute wait occurs inside this test.

| Arm | Inputs and decisive observations |
|---|---|
| Derived timeout; **first arm / red witness** | Default 15, one row with `TimeoutMinutes = null`, estimate 10: hold build, advance 16 virtual minutes, assert its token is **not canceled**, then finish at minute 20. Assert one build, build state `ok`, row green, and no global driver kill. Old expression cancels at minute 15 and fails the token assertion. |
| Shared build and selected rows | Selected rows on one build have explicit 15 and 40 minutes, shorter row first; an unselected row on the same build has 90. Assert the registered build timer is 40 minutes; release at 35 and require both selected rows green and exactly one build. This also detects using only the first row or all manifest rows. |
| Longer CLI override at the scheduler boundary | Derived 30-minute row, override 40: hold until minute 35, assert no cancellation, release, require green and the 40-minute timer. This uses the existing `RowTimeoutOverride` forwarding contract, not a new CLI option. |
| Shorter override / timer still enforced | Explicit row 40, manifest default 20, override 7: alive at minute 6, canceled at minute 7. Await scheduler completion; build failed, zero test-driver launches, no global driver kill. Assert the current build-failure result (`ExitCodes.Invalid`), without changing exit classification in this card. |

In W-1 also exercise caller cancellation directly through the new deadline
overload with a held fake driver: cancel the caller before the deadline and
require `OperationCanceledException`, preserving caller-versus-timeout behavior.
Keep that check independent of the virtual survivor assertions. The test proves
deadline wiring, not real MSBuild throughput under a particular load.

**R-1/R-2:** retain and select the dependency's existing
`build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it`
and `scheduler_passes_the_derived_build_deadline_and_records_it` methods. They
cover resolver defaults/override and state/report recording. Together with W-1
these three executions are the complete narrow ordinary green scope.

Execution: run the checkpoint tool once per committed slice, selecting `--rows
CP-1` then `--rows CP-2`, with `--expected-source-sha <that full SHA>`. Use the
tool's host build-slot gate and isolated outputs; any tool bootstrap build also
runs through `scripts/build-slot.ps1` and is reported as necessary launcher
bootstrap, not another test selection. Keep the executing launcher on the fixed
dependency version while exercising the intentional defect inside the test
assembly. `run --plan <this path>` owns execution; continue foreground `wait`
calls until exit is not 75. A slot timeout is not a test result. Preserve fresh
TRX, report and unedited CHECKPOINT lines under ignored evidence paths, validate
the green receipt, and clean owned alternate outputs after both runs. Code and
Review run `scripts/check-evidence-diff.ps1` over their recorded base..HEAD.

### Checkpoints

Lane: **portable `Antiphon.Tests` Unit methods**, selected explicitly below; no
whole Unit, class, namespace, or fleet lane run. GET `/api/runner-defaults` and GET
`/api/session-runners` both succeeded during Plan; placement remains runtime
resolved. Omit `-Runner` and `-Platform` for Code/Review; no OS-specific work is
required. Re-read live placement at dispatch instead of embedding a fleet host.
CP-1 is deliberately red; CP-2 is the final acceptance run. All `Min` values are
TUnit execution counts, including W-1's multiple arms as **one** test.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1356-red/` | portable-unit-deadline-red | `/*/Antiphon.Tests.Checkpoints/RunSchedulerTests/build_timer_uses_selected_row_deadlines_and_override` | W-1 defect-red | 1 executed, 1 intended token assertion failure, 0 skipped | 1 | 8 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1356-green/` | portable-unit-deadline-green | `/*/Antiphon.Tests.Checkpoints/RunSchedulerTests/(build_timer_uses_selected_row_deadlines_and_override*)\|(build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it*)\|(scheduler_passes_the_derived_build_deadline_and_records_it*)` | W-1, R-1, R-2 | All 3 named methods, 0 failed/skipped | 3 | 8 |
