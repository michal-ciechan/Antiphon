# CARD-1531: separate checkpoint admission wait from execution budget

Plan date: 2026-10-23. Source inspected: `0643a3a721cfee6e9934e8fa0b01fddd405b9ce0`.
Read CARD-1531 and CARD-1532 in full through `scripts/card.ps1`, and
[the starvation investigation](../../investigations/2026-10-23-build-slot-starvation-debug.md).
This is a documentation-only Plan; no builds, tests, deployment or live qualification changes.
Verification below is the closed proposal for a **separate TestDesign stage**, not executed evidence.

## Outcome and scope

A checkpoint that waits for a build slot can still use its existing execution
allowance after admission. Waiting is finite, overlapping waits earn credit once,
and a running driver never gains time because a sibling is waiting. Receipt
consumers can distinguish admission refusal, execution expiry and hard wall expiry.
Source/output custody, broker capacity, memory floor, retries, owner cancellation,
and Windows process policy remain under their existing owners.

## Ground truth

| Card assumption | Code at the inspected SHA | Consequence for this plan |
|---|---|---|
| Total time includes admission | `Execution/RunScheduler.cs:RunAsync` links one token and calls `CancelAfter(TotalTimeout)` before probe/acquire. | Replace total accounting, not the per-driver allowance. |
| Build and row clocks already exclude acquisition | `BuildOneAsync` and `RunRowAsync` acquire before `RowTimeout.RunWithDeadlineAsync`; build uses a `TimeProvider`, rows currently use the system-time overload. | Preserve deadlines; thread a common injectable clock through the remaining paths. |
| Parallel waits must not be added | One build may overlap ready rows; scheduler width is separate from lease admission. `running.Count` includes rows waiting for a slot. | Track phases explicitly; neither task count nor the sum of `WaitedSeconds` measures queue-only wall time. |
| Admission has a configured bound | `BuildDefaults.SlotWaitMinutes` defaults to 45; `ManifestLoader` normalizes nonpositive values to 45. `CheckpointApp.ExecuteAsync` does **not** pass it to `BuildSlotClient`, whose own default is 45. Its busy loop checks the limit only after an answer. | Wire the existing setting and enforce a timer across the complete acquisition, including pending HTTP/delay. |
| Total CLI/manifest semantics are uniform | `--total-timeout` populates nullable `RunRequest.TotalTimeoutMinutes`; otherwise Execute derives `max(30, 2*sum(selected estimates)+10)`. `timeouts.totalMinutes` exists/defaults to 90 but is not consulted there. | Preserve existing execution-limit resolution; do not silently activate the dormant YAML field. Document it accurately. |
| Timeout diagnostics explain the cause | A canceled acquisition can become a zero-wait placeholder. A timed-out build becomes generic `build-failed`, exit 2. `TotalTimeoutAt` is also used to budget optional baseline comparison. | Preserve partial phase measurements and explicit cause through state/report/merge; update the baseline consumer. |
| `executed=` can report duration | The existing CHECKPOINT token and `ReportRow.Executed` are **test counts**. `WaitedSeconds` is an integer; `Seconds` mixes meanings across rows/builds. | Add timing fields/record without changing test-count or historical duration meanings. |
| Cheap deterministic seams exist | `ControlledTimeProvider`, `FakeDriver`, `FixedSlotClient`, `RunSchedulerTests`, `CheckpointSlotExecutorTests`, and `CheckpointApp.Runtime` already support fake drivers, slots, clocks and owned fixture runs. Tests live in `tests/Antiphon.Tests`, not a separate Checkpoints.Tests project. | Use those seams; no compiler subprocess or live broker inside the new scheduler tests. |

Paths above without a prefix are below `tools/Antiphon.Checkpoints/`.

## Decisions

**D-1 — Charge elapsed work, not aggregate driver time.** Introduce an internal
`Execution/RunBudgetClock.cs` using monotonic `TimeProvider.GetTimestamp` plus its
timers. For one scheduler run define wall `W`, admission-only union `Q`, and charged
execution `E = W - Q`. Suspend E only when at least one acquisition is pending,
all currently runnable activities are awaiting broker admission, and no activity is
in preflight, source hashing, driver execution, result parsing, release or settlement.
Unstarted rows blocked on a build/exclusive lane are not runnable. Before suspending,
the scheduler must have attempted all eligible launches. Probe, owner checks,
source checks, scheduler overhead and cleanup are charged work, never admission credit.
Phase transitions are atomic, update elapsed time before changing phase, and unwind
in `finally`. Overlapping acquisitions contribute their union once. Reject summing
waits, pausing whenever *any* task waits, or extending individual driver deadlines.

**D-2 — Three finite limits, with compatibility explicit.** Keep the current
`--total-timeout` and selected-estimate derivation as the total **execution** limit
E-limit. Keep row/build deadline derivation and `--row-timeout` values unchanged.
Use `build.slotWaitMinutes` (default 45) as the per-acquisition A-limit, now actually
passed to the production client. Add optional positive `timeouts.hardWallMinutes`
and `--hard-wall-timeout`; CLI overrides YAML. If absent, hard-wall W-limit is
checked `E-limit + A-limit`, not number-of-rows times A-limit. An explicit positive
wall limit may be shorter than E-limit and intentionally win. Never disable a
limit with zero, negative, empty, overflow or an infinite duration. Keep legacy
normalization for existing YAML numeric fields; reject invalid **new** values.
`timeouts.totalMinutes` remains legacy/unused by scheduled execution in this card;
do not add an unrelated configuration precedence change.

**D-3 — Admission expiry is a refusal, not a failed test.** Retain exit 4 and the
compatible `slot-timeout` state; add terminal reason
`slot-admission-deadline-exceeded` with the broker's last reason recorded separately.
It launches no driver and reports zero executed tests. Enforce A-limit even when
an HTTP attempt or a retry delay would cross it; clamp delays to remaining time,
link the timer to acquisition I/O, and dispose a grant/holder if cancellation wins
at the grant boundary. Do not fall back to unleased work on this expiry. Preserve
the existing transport-grace policy, without making expiry look like transport failure.

**D-4 — Preserve cause and cancellation ownership.** Use named
`execution-budget-exceeded`, `hard-wall-deadline-exceeded`, `build-deadline-exceeded`
and `row-deadline-exceeded` causes; each remains exit 5. Correct the build-expiry
projection from exit 2 to 5, including dependent rows. Genuine compiler failure
remains 2 and test assertion failure remains 1. Retain the existing confinement
precedence (tampered/restore-failed), caller outcome 130, bound-owner outcome 7 and
receipt-persistence failures. Resolve ties deterministically: caller/owner first,
then existing safety precedence, then wall, execution, acquisition, local deadline.
Cancel pending acquisitions and active work, await all tasks and lease/holder
disposal, and publish terminal evidence only after joining. The wall deadline is
an independent cancellation/stop-admission deadline, never extended by Q. Existing
bounded driver kill/drain (10 seconds) and custody finalization still follow it;
report this teardown separately, never promise that final receipt I/O completes
exactly at the work deadline or hide it in admission credit.

**D-5 — Carry the budget past scheduling.** Start the budget immediately before
scheduler probe and retain it through optional baseline work. Do not reset it at
the end of `RunScheduler.RunAsync`. `CheckpointApp` passes remaining charged and
hard-wall allowance to `BaselineComparer`; its acquisition/driver operations use
the same phase-accounting wrapper and cancellation. An exhausted run launches no
baseline driver, including the current fresh-one-minute cleanup command; retain
and report owned baseline residue if no safe time remains. Keep baseline work
explicitly listed as tooling and preserve failure attribution. New work must never
use the old absolute `TotalTimeoutAt` as an execution-budget substitute. Retain
that legacy observation for old readers, add an authoritative `HardWallTimeoutAt`
and remaining execution duration; heartbeat names acquisition separately. The
end-to-end run wall duration still includes startup/finalization and is distinct
from the budget's wall duration.

**D-6 — Add evidence without reinterpreting old receipts.** Add nullable duration
fields to build/row state and report DTOs (`executedSeconds`, measured after
admission, and wall duration), and run budget fields (`budgetWallSeconds`,
`admissionOnlySeconds`, `executionSeconds`, resolved limits and terminal reason).
Keep existing `waitedSeconds`, including partial nonzero wait on every canceled
acquisition. At run scope execution includes overhead; per-driver execution is
driver time, so their sums need not agree. Emit an additional timing record such
as `CHECKPOINT-TIMING CP-1 waited=300s executed=120s wall=420s`; preserve the original
`CHECKPOINT ... executed=51` count token. New records distinguish never admitted
(execution 0) from old evidence with unknown timing (null/omitted). JSON schemas
2/3 gain optional fields; old receipts remain readable and are never rewritten.
`ReportMerger` preserves each attempt's measurements and actual outcomes; it does
not sum waits into Q or convert a historical timeout/cleanup refusal to green.

**D-7 — Independent production ownership; sequence shared docs and qualification.**
This card owns scheduler, budget, slot, request/manifest and report files, plus
`Baseline/BaselineComparer.cs`. CARD-1532 owns `BuildConfinement.cs` and the two
PowerShell stamp files. Add card-specific test classes; do not edit the common
test csproj or shared clock/fixture files. CARD-1531 may update the existing
`RunSchedulerTests` build-expiry assertion and `CheckpointTaskOwnershipTests`
deadline observations. CARD-1532 owns the existing EOF stamp-seam fixture update.
The common `docs/testing-and-build.md` is edited by CARD-1532 first and CARD-1531
second, after importing its landed base; never concurrently. If stage scope rules
cannot admit these exact path scopes, sequence entire tasks CARD-1532 then CARD-1531.
Even with parallel source work, final CARD-1531 verification follows CARD-1532:
the large shared test assembly otherwise risks refusing its own retained red output.
Do not copy an unreviewed patch or merge/rebase a pushed task branch to get that fix;
dispatch the qualifying task from the reviewed, landed stamp-fix base.

**D-8 — Placement and release.** Read `GET /api/runner-defaults` and
`GET /api/session-runners` again at dispatch. Planning observed defaults revision 2
selecting an eligible Linux runner, plus an eligible Windows runner. These are
observations, not a host pin. All rows below use the **portable .NET fake-clock
lane**, no PostgreSQL, browser, real test build inside fixtures, or native Windows
process qualification. Omit `-Runner` and `-Platform`. This changes no session that
waits for human input; with parking disabled no automatic release deadline exists
(CARD-1083). Broker acquisition is a different wait with the A/W limits above.

## Slices

Each Code slice is 30-60 minutes; S1 is committed/pushed with its real red outcome
before implementation. Do not change source while a checkpoint is in flight.

| Slice | Files and deliverable | Closure |
|---|---|---|
| S1 (45-60 min) | Add `tests/Antiphon.Tests/Checkpoints/CheckpointAdmissionBudgetTests.cs` and `CheckpointAdmissionContractTests.cs`; local fixture helpers in those files. Thread only passive clock/observation seams through `RunScheduler`, `RowRunner` and `CheckpointApp.Runtime` if needed to run the existing algorithm with virtual time. | CP-1/CP-2: real missing-behavior assertions fail before the fix. New DTO members may be passive/null for compilation; no expected-value-returning implementation. |
| S2 (45-60 min) | Add `Execution/RunBudgetClock.cs`; update `Execution/RunScheduler.cs`, `Execution/RowRunner.cs`, `Slots/BuildSlotClient.cs`, `Baseline/BaselineComparer.cs`. Implement phase accounting, bounded acquisition, remaining-budget handoff, exact terminal cause and joined cleanup. | Code review against V-1..V-9; commit before continuing. No intermediate broad build. |
| S3 (45-60 min) | Update `Program.cs`, `RunRequest.cs`, `CheckpointApp.cs`, `Manifest/CheckpointManifest.cs`, `Manifest/ManifestLoader.cs`, `Manifest/ManifestValidator.cs`, `State/RunState.cs`, `Report/ReportModel.cs`, `Report/CheckpointLine.cs`, `Report/ReportWriter.cs`, `Report/ReportMerger.cs`; update only affected existing regression expectations. Finish authoritative testing-guide paragraphs after D-7's doc dependency. | CP-3..CP-10, one shared green test-project build. Add files to scope before editing if implementation finds an additional producer/consumer. |
| S4 (30 min) | Validate exact green receipts, evidence diff, counts and test identity roster. Store full outcome, original red/green CHECKPOINT lines, bootstrap costs and cleanup dispositions in the task report. | No product/source changes merely to rewrite evidence. Fetch master, run conflict-only `git merge-tree --write-tree origin/master HEAD`, push exact task tip, then ordinary Review. |

## Rollout, activation and rc

The tool is built from source for each invocation and shadow-copied into a run.
No server, runner, AppHost or desktop restart is required. A running executor keeps
its old shadow copy and budget; do not replace it or retroactively fix its receipts.
After publication, the next invocation from the updated checkout uses the new code.
Leave supervisor pid 209072 and live Windows qualification alone.

Both defects are present on the observed `origin/rc/2026-10-20` at
`5a126f6fc5ae4e16d803b628716b8f0485a37174`; re-resolve the active rc branch before
porting rather than trusting that dated observation. **Recommend picking both
reviewed fixes**, stamp fix first, to the current rc line: they repair existing
checkpoint tooling. Commission a port on a new assigned branch based on the
fresh rc tip, apply only the reviewed fix/test/docs commits (adapt to rc context),
run this plan's exact affected selections there, then independent ordinary Review.
Publish via the authorized Merge-role workflow from the target tip described in
[release-gates.md](../../release-gates.md), and merge rc to master as that owner
requires. Do not import all of master or move release tags. Source publication
does not grant RC qualification credit; subsequent qualification pins its own SHA.

## Verification design

TestDesign must finalize the table and typed clock/seam wiring before Code. New
names are proposed tests, not existing methods or successful evidence. All new
methods have one TUnit execution each; internal matrices do not inflate `Min`.
Drive real scheduler/receipt paths with `ControlledTimeProvider`, barriers and
fake slots/drivers. Await timer registration before advancing; no sleeps for
minutes, live broker pressure, compiler build, or expected values derived from
the implementation being tested. Every fixture has a short real watchdog and
joins its work in `finally`. A build/fixture error is not a red witness.

### Red witnesses and controls

| ID | Class.method | Required observation; pre-fix witness |
|---|---|---|
| V-1 | `CheckpointAdmissionBudgetTests.C1531_wait_preserves_execution_budget` | E=10 min, A=20, W=30; build admission waits 9, then build/row perform 9 charged minutes and pass. E remains available after wait. Old total timer cancels at wall 10. |
| V-2 | `CheckpointAdmissionBudgetTests.C1531_ready_row_waits_then_executes` | Build completes after 4 charged minutes; its row waits 9, then uses 5 of remaining 6. Verified build binding survives, exactly one build and one row driver. Old total cancels before row execution. |
| V-3 | `CheckpointAdmissionBudgetTests.C1531_overlapping_waits_receive_union_credit` | After first build, next build and ready row wait for overlapping [2,7] and [4,9] intervals; no other work. Q=7, not 10. Assert boundaries while preflight/settlement work prevents credit. Old implementation lacks Q; a summed-wait mutation overcredits. |
| V-4 | `CheckpointAdmissionBudgetTests.C1531_running_sibling_keeps_charging` | One admitted command/build runs while sibling waits. At unchanged E-limit cancel running work; Q=0 for that interval, queued sibling never launches. Control for D-1; also verify exact per-row and per-build limits with E/W larger. |
| V-5 | `CheckpointAdmissionBudgetTests.C1531_stuck_admission_is_bounded` | Block acquisition beyond A with a cancellable fake HTTP attempt, not just a busy-response loop. Exit 4/reason, zero drivers/tests, nonzero partial wait, zero execution, holder disposed. At boundary grant race releases exactly once. Old path exceeds configured A or becomes generic total timeout. |
| V-6 | `CheckpointAdmissionBudgetTests.C1531_hard_wall_bounds_successive_waits` | Each acquisition is below A but cumulative waits cross W before E. Exit 5 with wall reason, cancel/join all work and prohibit another launch. Old algorithm cannot supply this separate policy/cause. |
| V-7 | `CheckpointAdmissionBudgetTests.C1531_execution_overrun_keeps_old_limits` | No wait: total E expiry, row expiry, build expiry at their original values. Explicit build override still wins. Test failure remains 1 and compiler failure 2; build expiry becomes 5, not old generic 2. |
| V-8 | `CheckpointAdmissionBudgetTests.C1531_cancellation_joins_and_keeps_partial_wait` | Owner and direct executor caller cancel during admission and during execution. Hold disposal barrier: no terminal receipt before join. Exit 7/130, wait retained, no false driver execution. Observer `wait` cancellation remains observer-only. Old placeholder loses partial wait. |
| V-9 | `CheckpointAdmissionBudgetTests.C1531_baseline_shares_remaining_budget` | Scheduler finishes with wait credit and one failed test. Baseline gets remaining E/W, not fresh limits or stale absolute execution timestamp. Exhaust either budget during its acquire; no baseline driver/cleanup command launches after expiry, residue is reported. No new real baseline checkout. |
| V-10 | `CheckpointAdmissionContractTests.C1531_inputs_resolve_without_disabling_bounds` | Drive real CLI parse, YAML loader, `RunRequest` round-trip and Execute runtime seams with the absent/empty/value matrix below. Assert actual client timeout and hard-wall timer, not only parsed properties. Old code ignores configured slot wait/new wall field. |
| V-11 | `CheckpointAdmissionContractTests.C1531_receipts_keep_wait_execution_and_counts` | Through Execute, persisted state/report/Markdown contain partial and successful wait, execution, Q/W/E and named cause; `CHECKPOINT` count and separate timing-record seconds have no duplicate/ambiguous keys. Two-attempt merge retains failed history. Old output has no execution timing and zeros canceled wait. |
| V-12 | `CheckpointAdmissionContractTests.C1531_legacy_receipts_remain_unknown_not_zero` | Read actual schema-2/3 JSON with missing new fields, then round-trip/merge with a new attempt. Unknown stays null, historical nonzero exit and cleanup refusal remain invalid for green qualification. Control, not claimed red at base. |

### Absent/empty/value inputs

| Real boundary/type | Absent | Empty/invalid | Value |
|---|---|---|---|
| CLI argument strings, then nullable `int` request values | Missing total keeps selected-estimate derivation; missing wall derives E+A. | Present wall with no token, `""`, whitespace, `0`, negative, non-integer and overflow refuses before launch; no silent infinity. | `--total-timeout 10m --hard-wall-timeout 25m` resolves 10/25 using existing duration grammar; serialized request preserves both. |
| YAML mapping and nullable new `int? HardWallMinutes` | Missing `timeouts`, null mapping, empty mapping and omitted hardWallMinutes derive W. | Explicit hardWallMinutes null/empty scalar/empty string, zero, negative, malformed or overflow must not disable a limit; reject explicit invalid scalar, distinguish it from omission at the loader boundary. | Numeric 25 survives loader/serialization; CLI overrides it. Retain current normalization of legacy numeric fields. |
| Existing `int SlotWaitMinutes` and actual client | Missing build/missing key/empty build mapping use 45. | Existing zero/negative normalization stays 45; malformed scalar refuses. | 2 is passed to client and expires an unresolved acquire at two virtual minutes. |
| Nullable new JSON duration (`double?`), versus legacy test count (`int?`) | Omitted new fields mean unknown, not zero. | JSON null means unknown; string `"0"` and negative/non-finite reported duration cannot qualify as valid new timing. | Numeric 0 is legitimate for never-executed work; numeric wait >0 survives cancellation. Test count remains an integer independent of seconds. |

Explicit-null detection in YAML needs a presence check because a nullable CLR
property alone cannot distinguish omitted from explicit null. TestDesign must pin
the real loader behavior rather than assume a dictionary or string conversion.

### Regressions and positive controls

- R-1: existing scheduler overlap, build serialization, exclusive-row ordering and
  build-deadline derivation; update the one old build-expiry exit assertion in
  `build_timer_uses_selected_row_deadlines_and_override`, retaining its timer proof.
- R-2: the existing exact-wait executor rejection receipt/merge witness and real
  owner/caller join witnesses preserve lease release and exit precedence.
- R-3: existing CHECKPOINT regex and merge-attempt tests protect count semantics.
- R-4: Windows estimate selection and baseline exhausted-budget regression protect
  both consumers of the old absolute deadline. No native Windows process run.

Method-scoped positive controls, later SourceLanding Mutation: remove wait pause
-> V-1/V-2; sum concurrent waits -> V-3; pause whenever any sibling waits -> V-4;
remove A timer or treat it as unreachable fallback -> V-5; remove W timer -> V-6;
extend local deadlines or collapse build timeout -> V-7; drop join/partial-wait
capture -> V-8; restart budget for baseline -> V-9; ignore configured bound ->
V-10; replace partial timing with zeros or replace test count with seconds ->
V-11; default old timing to known zero/clear old failures -> V-12. Each control
runs one exact `/*/*/Class/Method` filter red then green on its bound pack; retain
original red evidence where it already demonstrates the same change.

### Execution and cost

S1 rows share **one** isolated `Antiphon.Tests` build; S3 rows share **one** new
build at the frozen final SHA. Estimate 28 minutes total, excluding admission,
checkpoint-tool bootstrap, and optional Windows platform work (none here).
One slot-gated isolated tool bootstrap per source group is declared tooling cost;
invoke its compiled tool for `run --plan <this-path> --rows <ids>
--expected-source-sha <full-sha>`, then own/await `wait` until exit is not 75.
Do not hold an outer build-slot lease around a running checkpoint executor.
Use forward-slash output paths; retain ignored receipts, then remove only outputs
whose ownership has been established. On slot exit 4 report not run, never retry
unleased. Missing tests, skipped required rows, bootstrap failure and cleanup
refusal cannot be counted as green. Run `scripts/check-evidence-diff.ps1` over
base..HEAD and validate exact final receipts. After a fix rerun only affected rows;
confirm any claimed inherited red at base with that exact method.

### Checkpoints

Closed proposed list; **portable .NET fake-clock lane** for every row. One literal
filter per row. S1 has nine methods in CP-1 and three in CP-2; record actual
assertion reds and passing controls rather than predict an exact failing count.
TestDesign may amend this manifest before Code; Code may not broaden it ad hoc.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1531-red/` | admission-red | `/*/*/CheckpointAdmissionBudgetTests/C1531_*` | V-1..V-9 | all 9 executed, intended behavioral reds, 0 skipped | 9 | 9 |
| CP-2 | S1 | CP-1 | contract-red | `/*/*/CheckpointAdmissionContractTests/C1531_*` | V-10..V-12 | all 3 executed, intended contract reds and legacy control, 0 skipped | 3 | 1 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1531-green/` | admission-green | `/*/*/CheckpointAdmissionBudgetTests/C1531_*` | V-1..V-9 | all 9 passed, 0 failed/skipped | 9 | 10 |
| CP-4 | S3 | CP-3 | contract-green | `/*/*/CheckpointAdmissionContractTests/C1531_*` | V-10..V-12 | all 3 passed, 0 failed/skipped | 3 | 1 |
| CP-5 | S3 | CP-3 | scheduler-regression | `/*/*/RunSchedulerTests/(builds_never_overlap_even_with_free_slots_and_width_two)\|(rows_of_a_finished_build_run_while_the_next_build_is_in_flight)\|(an_exclusive_row_keeps_the_build_lane_idle_until_it_finishes)\|(build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it)\|(build_timer_uses_selected_row_deadlines_and_override)` | R-1 | all 5 passed, 0 failed/skipped | 5 | 1 |
| CP-6 | S3 | CP-3 | slot-regression | `/*/*/CheckpointSlotExecutorTests/executor_busy_then_rejection_retains_exact_wait_in_state_report_and_merge` | R-2 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-7 | S3 | CP-3 | owner-regression | `/*/*/CheckpointTaskOwnershipTests/settlement_cancels_two_drivers_and_skips_later_rows` | R-2 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-8 | S3 | CP-3 | caller-regression | `/*/*/C1307CliCancellationTests/Executor_cancel_persists_130` | R-2 | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-9 | S3 | CP-3 | evidence-regression | `/*/*/(CheckpointLineTests)\|(ReportMergerTests)/(tunit_line_matches_the_pinned_regex_with_slot_suffix)\|(latest_per_cp_wins_and_earlier_attempts_count_as_reruns)` | R-3 | all 2 passed, 0 failed/skipped | 2 | 1 |
| CP-10 | S3 | CP-3 | remaining-budget-regression | `/*/*/(CheckpointTaskOwnershipTests)\|(BaselineComparerTests)/(windows_estimate_selects_row_and_total_deadlines)\|(baseline_stops_when_the_timeout_is_already_spent)` | R-4 | all 2 passed, 0 failed/skipped | 2 | 2 |

--- next stage ---
next: test-design
handoff: Finalize CARD-1531's typed budget/phase seams and exact red roster under D-1..D-8; preserve union-only credit, finite admission/wall limits and receipt compatibility. Coordinate shared-doc and qualification order with CARD-1532; no builds in TestDesign.
artifact: docs/superpowers/plans/2026-10-23-card-1531-checkpoint-admission-budget-plan.md
