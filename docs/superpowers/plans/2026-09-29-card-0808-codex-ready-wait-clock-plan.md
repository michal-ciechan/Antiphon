# CARD-0808: bound Codex readiness test clock driving

Date: 2026-09-29. Stage: Plan, with TestDesign folded in for this test-only repair.
Next: Code. Baseline: `16da56da5a9c9bc3d93ba5006dd139b072750c26`.

## Outcome and scope

Repair the scheduling-sensitive waits in
`tests/Antiphon.Agents.Pty.Tests/CodexReadyWaitTests.cs`. Drive fake time in
bounded steps until the required observation or completion, and bound every
test-owned asynchronous wait with a real-time deadline. Keep the readiness,
cancellation, exit, trust, and diagnostic assertions meaningful.

The implementation footprint is that test file, including its private helpers
and one regression test. Production `CodexReadyWait`, its settings, fixtures,
other test classes, and test-runner policy need no changes. No live Codex process,
database, browser, server restart, or deployment is needed.

## Evidence and failure mechanism

The full CARD-0808 description was read with `scripts/card.ps1 get CARD-0808`:
card `b6e4b48d-4a85-4c00-b4c0-ed4d56d5f0ef`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`. It reports hangs at both base and HEAD,
roughly once per 10-25 class runs, and points to CARD-0777 review task `e2b9bf48`.
The newer ready-frame half passed 95 repetitions but has the same unsafe shape.
These are supplied observations; this Plan did not reproduce or rerun them.

All 570 lines of the test file and the readiness loop were inspected. In
`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`, the loop observes a snapshot,
checks remaining time, then registers `Task.Delay(delay, time, ct)` (lines
569-580 at this baseline). A read counter becoming positive does not establish
that this timer exists. `AdvanceAfterWaitStartedAsync` merely sleeps for 25 ms;
its name promises synchronization that it does not provide. If a continuation
registers a timer after the only `Advance`, an untimed terminal await can remain
pending indefinitely. Even an advance to the nominal deadline can miss a poll
whose remaining duration was calculated before that advance.

The requested template commit, `16da56da`, was read in full for this file. It
changes the first half of `Not_ready_hands_the_last_frame...` to use up to ten
50 ms advances before requiring two reads, and retains the later deadline
advance. It **does not add a timeout to its final await**. Reuse its bounded
progress pattern and add the real-time bounds required by CARD-0808. The
[CARD-0777 investigation](../../investigations/2026-09-28-card-0777-codex-desktop-update-modal.md)
documents the behavior that its update-modal assertions must retain.

## Complete wait inventory

Line numbers refer to the baseline above. This distinguishes the vulnerable
clock-driving paths from waits that are already guarded or return immediately.
There is one `One_observation...` method in this checkout, and two
`...on_the_final_snapshot...` methods.

| Test or helper | Baseline sites | Required treatment |
|---|---|---|
| `One_observation_never_establishes_readiness` | 28-33 | One advance precedes the second-read signal and a bare gate await. Pump to the second-read signal, retain a full settle with that read held, release it, then pump/bound completion. |
| `A_new_wait_and_generation_start_without_a_candidate` | 40, 53-61 | Its first wait inherits the `WaitReadyAsync` defect. Its second signal is already bounded; cleanup advances once then awaits without a bound. Bound and observe cleanup as well. |
| `Deadline_or_zero_total_budget_never_authorizes_input` | 69-86 | Zero budget returns immediately today. Both nonzero-budget branches advance once then await bare tasks. Bound all three results; drive the latter two to completion with a finite deadline budget. |
| `Trust_consumes_the_original_total_budget` | 123-127 | The single 600 ms advance can leave the bounded write predicate waiting for a poll; the 900 ms advance precedes a bare gate await. Pump to the trust write and then the original deadline. |
| `A_stalled_snapshot_finishes_at_the_gate_deadline` | 146-151 | Already bounded by the entered signal and `WhenAny` plus identity assertion. Preserve the exact 2,000 ms deadline and absence of caller cancellation. A uniform timed result await may replace the existing guard. |
| `A_stalled_trust_write_finishes_at_the_gate_deadline` | 172-176 | Same existing guard; preserve the exact virtual deadline and stalled write. |
| `Cancellation_at_entry_prevents_reads_and_input` | 187-200 | Immediate canceled result today; put the real-time bound inside the exception assertion. |
| `Cancellation_during_read_prevents_trust_input` | 227 | Immediate cancellation path today; bound its exception assertion too. |
| `Cancellation_on_the_final_snapshot_cannot_return_ready` | 247-249 | Single 50 ms advance and untimed exception assertion. Pump until the gate completes, then require cancellation through a timed await. |
| `Exited_process_never_receives_startup_input` | 257-268 | Immediate exit today; bound the result without introducing a pump. |
| `Exit_on_the_final_snapshot_cannot_return_ready` | 289-291 | Single 50 ms advance and bare await. Pump, require the second read/exit to occur before the 5,000 ms budget, then assert false. |
| `Unavailable_or_failed_snapshot_cannot_reuse_a_ready_frame` | 302, 314-316 | Bound the injected-exception assertion. Drive and bound the ready-then-null branch; require a null observation before expiry so deadline alone cannot satisfy the assertion. |
| `Trust_response_forces_a_new_snapshot_and_full_settle` | 346-352 | Keep the pre-settle incomplete assertion after 999 ms. Replace the final single 50 ms advance/bare await with bounded completion driving. Release held reads on failure. |
| `Update_modal_mid_wait_is_skipped_once_then_requires_fresh_readiness` (two arguments) | 393-403 | Already uses bounded loops, but their exhaustion falls into a bare gate await. Bound the final result and retain all Escape, stale-modal, and fresh-settle assertions. |
| `Bare_continue_prompt_does_not_send_escape` | 432-434 | Single deadline advance/bare await. Drive and bound completion; preserve empty writes. |
| `Deadline_immediately_after_escape_reports_blocking_update` | 455-459 | The advance inside the write is intentional deadline injection, not a poll driver. Keep it and bound the result. |
| `Not_ready_hands_the_last_frame_over_once_and_keeps_it_out_of_the_diagnostic` | 490-497, 519-521 | Preserve the first half's two-read classification barrier, then bound deadline completion. Replace the ready half's single 100 ms advance with bounded completion driving. |
| `WaitReadyAsync` | 548-551 | Shared single-advance/bare-await path. Use the same bounded completion driver. |
| `WaitUntilAsync`, `AdvanceAfterWaitStartedAsync` | 554-568 | The predicate wait already has a five-second bound. Use monotonic real elapsed time and a phase-specific failure message; replace/rename the misleading advance helper. |

## Implementation decisions

### D-1: Small class-local clock driver

Add a private `AdvanceUntilAsync` helper taking the fake clock, a completion or
milestone predicate, an explicit maximum number of advances, a step duration,
and a phase description. It observes task completion without awaiting or
unwrapping the result; cancellation tests still assert the original exception.

Use 50 ms virtual steps by default, matching `Options.PollInterval`. Yield to
continuations between steps using the existing small **real-time** delay. A
delay gives the scheduler an opportunity; it is never evidence that a poll has
registered. Check the predicate before advancing and after yielding. Stop at
the first satisfied predicate. After the last step, allow a bounded real-time
drain for the continuation woken by that step before failing.

Bound the entire phase with five seconds of monotonic real time, including
delays and the final drain. Use `TimeProvider.System`/`Stopwatch` or the normal
real-time `WaitAsync(TimeSpan)` overload, never the frozen fake provider for
this watchdog. On exhaustion fail with the phase, step count, and fake elapsed
time. Never fall out of the loop into an untimed task wait.

Give each call a finite virtual budget appropriate to its assertion:

- Short readiness/final-snapshot waits: at most 20 x 50 ms (1,000 ms), below
  their existing 5,000 ms deadline. Reaching the production deadline must not
  substitute for the final-read cancellation/exit behavior.
- The held-second-read test: at most 20 steps to reach that read; while it is
  held, advance one full 1,000 ms settle and assert incomplete. Only then
  release it and pump completion. The second-read signal proves the first
  observation was processed; a first-read counter alone does not.
- The post-trust positive completion has ample room under its existing
  60,000 ms budget; allow up to 40 x 50 ms after the 999 ms negative assertion.
- Expected deadline failures: drive toward the original absolute deadline,
  with at most two extra poll steps to wake a poll registered around the final
  advance. Require the intended blocker/null/trust milestone before expiry.
- Preserve the existing update-modal phase caps (10 and 20 steps) and
  assertions. A gate that runs out of virtual steps must fail within the real
  bound, including when the last continuation has not yet run.

Keep virtual budgets explicit at call sites. Do not turn on auto-advance,
increase production `MaxWait`/`Settle`, or introduce a global background pump.
Exact advances that establish a boundary or inject expiry remain exact.

### D-2: Bound results and cleanup without weakening assertions

Every test-owned result/signal wait gets a real-time bound, including
`WaitReadyAsync`, `Should.ThrowAsync` lambdas, and `finally` drains. Five seconds
matches the class's existing signal budget; the already bounded two-second
stalled-operation checks can remain unchanged. Finite scheduling delays and
helper calls protected by the phase watchdog need no redundant timeout layer.

Place `gate.WaitAsync(realTimeout)` **inside** an expected
`OperationCanceledException` assertion. A watchdog `TimeoutException` must fail
that assertion. Do not call `CancelAfter` on the cancellation token under test:
it would let missing production cancellation appear correct.

Held snapshot/write callbacks deliberately await their supplied token; those
are simulated operations, not unbounded test orchestration. Give each scenario
that can leave a held callback a failure cleanup path: release its TCS and/or
cancel a dedicated caller CTS in `finally`, then drain with a real timeout.
Perform successful-path assertions, especially “no caller cancellation,”
before cleanup. Observe expected cleanup cancellation only; remove broad
exception swallowing that would hide a cleanup timeout.

Preserve the trust test's origin and 1,500 ms total budget. After reaching
600 ms, pump only as far as needed for the write, require it before expiry,
then drive the remainder of that same budget rather than assuming 900 ms
still remains. In the post-trust settle test, anchor the 999 ms negative
boundary at release of the first post-trust ready frame; do not pump past
that boundary before checking incompleteness. Preserve all existing R-numbered
assertions, exact input sequences, exception identity, frame count, and
screen-free diagnostics. Use `Volatile.Read` for cross-continuation counters.

### D-3: Force the problematic ordering once

Add `Snapshot_released_after_the_first_clock_advance_still_reaches_readiness`
in the same class. Start the real `CodexReadyWait` with a first snapshot held
by a TCS, signal entry, and keep subsequent snapshots positive. Wait for entry
with a real timeout, advance 50 ms **while that first read remains held**, and
assert the gate is incomplete. Release the snapshot and use D-1 to drive the
gate to true. Require at least two completed positive reads. Use settle 50 ms,
max wait 5,000 ms, and at most 20 further 50 ms steps; clean up in `finally`.

This deliberately places the first advance before the poll can exist. It
exercises the real readiness loop and its returned outcome. It does not depend
on machine load or a larger sleep winning a race. Without further advances,
the settled ready result is impossible and the real-time bound must fail.

## Slice and acceptance

**S1 (one committed slice):** implement D-1 through D-3 in the single test file,
audit the complete inventory, then run the closed checkpoint list below.
Record the new method name, actual counts, and each checkpoint's evidence.

Acceptance: no test-owned task can remain pending indefinitely, forced late
registration reaches true, the final-snapshot tests execute their second-read
side effects, all existing behavior assertions remain, and the six class runs
pass without skipped results. Timeout/step exhaustion is a failed test, never
an accepted “not ready” or expected caller cancellation.

## Verification design

No builds/tests were run during Plan. The baseline has 17 `[Test]` methods and
18 expanded results (the update-modal method has two arguments). D-3 adds one
method/result: expect 18 methods and **19 results per class run**. Six runs
therefore expect 114 results, each reported separately. Update these counts
only for an explained source change.

| ID | Evidence in `CodexReadyWaitTests` |
|---|---|
| V-1 | New held-first-snapshot regression proves continued driving after an advance that preceded timer registration. |
| V-2 | `One_observation...`, both final-snapshot tests, `A_new_wait...`/`WaitReadyAsync`, and the ready-frame half complete with their original success/cancel/exit outcomes. |
| V-3 | Deadline, trust, unavailable/null, bare-continue, and both frame-diagnostic phases complete with explicit real bounds and retain their observations/assertions. |
| R-1 | Both stalled-operation tests still fail closed at exactly 2,000 virtual ms, without caller cancellation providing the verdict. |
| R-2 | Both update-modal arguments still send exactly one Escape, observe the stale modal, and require a fresh full settle; immediate-after-Escape diagnostics retain `BlockingUpdate`. |
| R-3 | Six fresh-process class executions through the checkpoint tool, sharing one isolated build, all green. This is supplemental repeat evidence, not a statistical claim that load can never cause a timeout. |
| R-4 | Review the complete wait inventory, bounded failure/cleanup paths, and preservation of all existing R-18 through R-37 assertions present in this class. |

### Positive control for the later Mutation stage

PC-1: in D-3 only, replace the post-release clock-driver call with a bounded
await of the gate, leaving the initial advance while the snapshot is held.
Run exactly
`/*/*/CodexReadyWaitTests/Snapshot_released_after_the_first_clock_advance_still_reaches_readiness`.
Expect one failure within the real watchdog: after release no clock moves, so
a positive candidate cannot settle and its next poll cannot fire. Restore the
driver, rebuild, and require one pass. This control mutates the test clock
driving being repaired; do not mutate production readiness for this card.
Keep the real watchdog and cleanup in both arms. Code does not run this
mutation; Mutation records its own leased, method-scoped red/restore runs.

### Execution and cost

Use the CARD-0723 checkpoint tool for one run after S1 is committed:

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-29-card-0808-codex-ready-wait-clock-plan.md --after S1 --max-wait 50s
```

Prefer an already built tool with `--no-build`. If a tool bootstrap build is
needed, perform that build through `scripts/build-slot.ps1` first and release
its lease before running the checkpoint executor; record the bootstrap reason.
Do not hold an outer build slot while checkpoint rows request their own slots.
After exit 75, call the tool's `wait <run-id> --max-wait 50s` until terminal;
do not finish while a checkpoint run is live. The checkpoint drivers lease
their own builds/runs and use `UseAppHost=false` on Linux as documented.

Run this Unit class only: changes affect its private harness, and the
readiness implementation is untouched. There is no integration/native surface
requiring a broader filter. Each repeat reuses CP-1's output and has its own
TRX. Do not co-schedule `Antiphon.Tests`. Report each generated `CHECKPOINT`
line with executed/passed/failed/skipped counts and reruns, plus any unlisted
command's reason. Review audits the report against the table.

### Cost

Ordinary Code verification floor: 5 + 1 + 1 + 1 + 1 + 1 = **10 minutes**,
including one isolated test build. Authoring/audit estimate: 15-20 minutes;
Code `ExpectAbout`: 25-30 minutes. Estimates allow for host contention and are
not measured timings. No open decisions or external prerequisites remain.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c808/` | readiness-clock | `/*/*/CodexReadyWaitTests/*` | V-1, V-2, V-3, R-1, R-2, R-4 | all 19 results, 0 failed, 0 skipped | 19 | 5 | true |
| CP-2 | S1 | CP-1 | readiness-repeat-1 | `/*/*/CodexReadyWaitTests/*` | V-1, V-2, V-3, R-1, R-2, R-3 | all 19 results, 0 failed, 0 skipped | 19 | 1 | true |
| CP-3 | S1 | CP-1 | readiness-repeat-2 | `/*/*/CodexReadyWaitTests/*` | V-1, V-2, V-3, R-1, R-2, R-3 | all 19 results, 0 failed, 0 skipped | 19 | 1 | true |
| CP-4 | S1 | CP-1 | readiness-repeat-3 | `/*/*/CodexReadyWaitTests/*` | V-1, V-2, V-3, R-1, R-2, R-3 | all 19 results, 0 failed, 0 skipped | 19 | 1 | true |
| CP-5 | S1 | CP-1 | readiness-repeat-4 | `/*/*/CodexReadyWaitTests/*` | V-1, V-2, V-3, R-1, R-2, R-3 | all 19 results, 0 failed, 0 skipped | 19 | 1 | true |
| CP-6 | S1 | CP-1 | readiness-repeat-5 | `/*/*/CodexReadyWaitTests/*` | V-1, V-2, V-3, R-1, R-2, R-3 | all 19 results, 0 failed, 0 skipped | 19 | 1 | true |
