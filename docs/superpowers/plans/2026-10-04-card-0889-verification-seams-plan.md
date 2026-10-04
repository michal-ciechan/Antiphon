# CARD-0889 plan amendment: finite test witnesses and C578 cancellation custody

Date: 2026-10-04. Plan task: `6c3e5034-88db-4fa5-897c-75c43e3f3436`.
Inspected source: `bfeb335c5b0b907a440a7d01e59d1cdce8dd4118`.
Branch: `feat/card-task-6c3e5034`, fast-forward-only from that source.

## Outcome and authority

This amendment supplies the implementation design for N1-N4 in the
[verification refresh](2026-10-04-card-0889-grouped-flaky-fixes-test-design.md).
It adds only test fixture, test driver and offline harness scope to the
[grouped fix plan](2026-10-04-card-0889-grouped-flaky-fixes-plan.md). S4's worker
migration and complete recipient-receipt requirements remain as specified there
and in the refresh. S6 incorporates the cancellation and observation design below.
The five published S1/S2/S3/S5/S7 implementations, production defaults and historical
qualification ledger remain intact. N1/N2/N4 improve their tests, not their product
behavior. No production source change is authorized by this amendment.

**Next: test-design.** TestDesign is a separate stage in this brief. It must confirm
finite assertion reachability and freeze the revised roster before Code; this
Plan does not claim that unimplemented seams or all PCs are executable today.
There is no unresolved preference or approval decision. D-1 through D-8 are
engineering decisions within the requested test-only scope, not new product defaults.

All **54 inherited controls remain pending**, with their original IDs and detecting
methods. The refresh's additional PC-55..74 also remain pending. None is discharged,
renumbered or replaced by new ordinary tests. Preserve the existing V-1..10/R-1..9,
delivery inventory and method-scoped Mutation selections. Historical ordinary
qualification is report-backed, not revalidated by this documentation task.

## Ground truth

Bodies inspected at the source above: the three affected adapter/resilience test
methods and their drivers; `ControlledTimeProvider`, `ScriptedCodexRunnerClient`,
`CodexSubmitConfirmation`, resilience retry classification/OnRetry logging;
`ScriptHarness`, all C578 shim/observer/cleanup bodies and the 24-method bridge;
representative build-slot, nightly and release-status callers. Owners read:
project context, resilience, HTTP operations, orchestration and testing/build;
the existing refresh retains the session/delivery owner analysis.

| Card/previous design assumes | What the code actually does | Required change and finite witness |
|---|---|---|
| N1: holding handler completion exercises the boundary driver's pending loop. | `AdvanceAfterAsync` receives `cancelled.Task`, which can complete inside `AdvanceTo`. `releaseFirst` holds a different task. PC-10 can miss the zero-advance statement entirely. | Pass a genuinely held cancellation phase to that driver; acknowledge a real loop step including its captured error/time before releasing the phase. |
| N1: terminal cancellation can always be awaited directly. | Slow-first waits for `firstSend`; an admitted retry can wait for an undriven delay before the detecting assertion. The real pipeline already emits structured `OnRetry` logging before that delay. | Race terminal outcome against a request-local retry signal and unexpected handler entry; assert the terminal verdict without advancing time to make a mutant finish. |
| N2: every send schedule produces four Enters. | `DriveSendAsync` awaits a prescribed Enter after each timer. Fewer retries can register another poll without Enter four; an accepted old receipt can complete the send early. | Observe terminal task, next timer and next Enter together, return outcome/counts to the test, and never require an event the mutant removed. |
| N2: direct fake-timer assertions detect System-clock substitutions first. | Poll/settle subcases follow the adapter schedule, which can hit its five-second watchdog first. | Move both direct synchronous timer-inventory probes ahead of the corresponding adapter schedule. |
| N3: C578 can consume the existing outer cancellation. | The 300-second CTS exists only inside C#. No signal reaches PowerShell; disposing `Process` on timeout neither kills nor joins it. | Publish an owned cancellation signal from that same token; cooperate in the script; retain independent C# kill/join/pipe-drain custody. |
| N3: ready means the parent has observed the streams. | The shim flushes streams and writes ready; the wrapper's log may still lag. Failed-build is not held. Readiness has a local ten-second cutoff. | Hold failed-build, consume complete wrapper-log evidence, use event subscriptions plus rechecks, and remove elapsed time as a readiness verdict. |
| N4: unexpected extra reads can finish before the first poll assertion. | `BeforeSnapshotAsync` blocks attempt two from construction. PC-1 can block before the first poll exists. | Install that hold only after observing the first poll and asserting one completed read, before advancing its clock. |
| C578 labels and TUnit results are interchangeable. | The bridge requires `PASS C578 `; failed-build has five labels, class has 24 results, full harness has 109 rows. | Keep five labels, add two fully prefixed labels: 7 failed-build / 111 full harness. Add separate cancellation tests without changing those counts. |

## Decisions

**D-1 — Preserve implementations and control identities.** Limit new changes to
the enumerated test paths. Keep the shared `ControlledTimeProvider` and scripted
client APIs; their existing timer/snapshot/Enter hooks are sufficient. Rejected:
reimplementing published fixes, weakening assertions, widening budgets, dropping
old controls, or modifying `scripts/run-checkpoint.ps1` to fit its harness.

**D-2 — Observe outcomes rather than assuming progress.** A task fault, unexpected
retry, premature completion or missing marker after a stream fence is data for
the detecting assertion. Watchdogs are infrastructure failures, never PC reds.
Rejected: waiting only for the expected success event, awaiting a fault before
capturing its details, or testing a spare clock instead of the actual driver.

**D-3 — One C578 observation implementation.** Put the shared observation cycle
and decision function in an ASCII test fixture imported by both parent and shim.
Logical schedules execute that cycle with injected snapshots/subscription events;
real waits execute it with file/process observations. Rejected: a separately
tested reducer that real waits never call, sleep-based late-ready proof, or
watchers whose notification itself counts as evidence.

**D-4 — C# owns cancellation until all its children and I/O are accounted for.**
Keep the 300-second operation guard. Add a caller-token overload and an opt-in
C578 signal protocol under the owned results root; keep the existing caller
signature as a forwarding wrapper. Fix final cleanup for every ScriptHarness
caller, including noncooperative scripts. Rejected: passing a .NET token as an
argv string, assuming `Process.Dispose` kills, or canceling pipe reads and claiming
they drained. Signal only local inherited test children; no service or runner call.

**D-5 — Arm the readiness hold at the measured boundary.** Before first-poll
registration all snapshot reads may finish. Install the existing second-read
hook only while the registered fake timer remains unadvanced. Rejected: moving
the first count assertion after the hold or using attempts as completed reads.

**D-6 — Keep the shared-helper regression scope explicit.** Add a dedicated
11-result cancellation/validation class and three exact existing compatibility
methods, alongside the existing 24-result bridge. They test the shared process
owner and result validator; unrelated release/nightly business rules do not need
whole-suite repetition. Rejected: silently changing common cleanup with only
the failed-build happy path tested, or adding cancellation cases to C578's
no-Case discovery and inflating its promised 111-row inventory.

**D-7 — Route by platform lane.** On 2026-10-04 at 12:51:31 UTC, both required
GETs succeeded: `/api/runner-defaults` revision 2, global preference and no
kind overrides; `/api/session-runners` showed eligible/accepting Linux capacity
10 with 9 occupied, Windows capacity 2 with 0 occupied, and one unavailable,
draining entry. These facts are not reservations. Omit `-Runner`; normally omit
`-Platform`. Use `-Platform Linux` or `-Platform Windows` only for the respective
qualification lane, and `-Platform Any` to remove a prior OS pin. No host ID,
fleet address or checkout location is part of the implementation design.

**D-8 — TestDesign follows this amendment.** Keep the prior 74-control inventory
and qualify new cancellation/cleanup guards explicitly. Rejected: calling a plan
an executable certificate, claiming a cancellation test passed because rescue
eventually killed the child, or moving straight to Code from this document.

## N1: held driver step and terminal-cancellation witness

Change `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceTestSupport.cs`
and `ResilienceBudgetTests.cs`; retain all seven budget and seven registration
test results. Do not alter the separate runner 3-second/git 10-second owner tests.

1. Extend `AdvanceAfterAsync` with an optional test step observer (before its
   final cancellation-token argument, or a compatibility overload). Its record
   contains actual current UTC, requested boundary, whether the supplied phase
   was pending, and any exception from the zero-advance/invariant check. Publish
   that record in `finally` around the **existing loop body**; rethrow the original
   exception after publication. Do not wrap a second clock or add a second advance.
   Existing callers without the observer keep their behavior.
2. In `Slow_first_attempt_consumes_the_same_budget`, make the passed phase await
   both the retained cancellation callback and a new `releasePhase` TCS. Keep
   `releaseFirst` separate so the actual HTTP handler also remains held. At
   t=10 the callback is complete, but `releasePhase` is not: the driver must enter
   its while-loop. Capture the driver task's completion/exception separately.
3. Race first step observation against captured driver completion, bounded only
   by the existing diagnostic watchdog. Before releasing either hold, assert a
   step actually ran with phase pending, step error is null, driver has not
   faulted, and step/current UTC both equal started+10 seconds, all under
   `held-completion-keeps-time-at-10`. PC-10 changes that actual zero advance to
   +1 ms: its step reports the invariant error and t=10.001, so this assertion
   fails finitely. A helper exception must not escape ahead of this assertion.
4. Release `releasePhase`, join the driver, then release the HTTP handler. Before
   that release, subscribe a request-local retry observation using the existing
   injected `CollectingLoggerProvider`: add an optional structured log callback
   in the same support file, selecting `Resilience retry` with the current
   operation/dependency/RetryNumber. Capture unexpected handler entry too. No
   production callback or telemetry change is needed.
5. Race terminal `firstSend` outcome, retry observation, and unexpected handler
   entry. Inspect all completed signals (not just the arbitrary WhenAny winner).
   At `cancelled-attempt-is-terminal`, require terminal TaskCanceledException,
   one send, and no retry. The real OnRetry log precedes a retry delay, providing
   PC-7's witness even when virtual time remains at 10. The scratch PC-7 predicate
   must positively admit the canceled-attempt outcome; merely removing its early
   rejection still falls through to false and is not a meaningful defect.
6. Only after that assertion, detach the first-request observer, issue the second
   logical request with the original budget and retain the absolute t=30 test.
   This prevents the second request's legitimate 503 retries from contaminating
   the first request's verdict. Keep both sends in outer scope; finally release
   both holds, cancel caller tokens, await driver and both sends, dispose any
   responses, then dispose the observer/provider. Preserve the primary assertion
   if cleanup also fails, while reporting the cleanup failure.

PC-5/6 retain their timer-inventory assertions before potentially pending work;
PC-7/10 now have explicit finite witnesses. Existing diagnostic bounds are not
relaxed. No additional TUnit method is needed.

## N2: finite submit driver and assertion order

Change only `tests/Antiphon.Tests/Agents/RunnerCodexAdapterSubmitConfirmTests.cs`.
The existing eight methods and production submit options remain unchanged.

- Replace `Task<Task>` driving with a captured result containing send success or
  exception, observed Enter count/times, and terminal virtual time. The driver
  must not throw a delivery exception before the test can inspect it. Each test
  owns/awaits its adapter lifetime and retains its existing semantic assertions.
- Install a reusable Enter-change signal before starting the send; use counts
  rather than indexing a five-element array. Record an unexpected fifth Enter
  without an IndexOutOfRangeException preempting the semantic assertion. Restore
  the previous `OnEnter` handler in finally.
- At each cycle race captured send completion, the next new **create/change**
  timer record and next Enter signal. Register/recheck signals before waiting;
  exclude timer fire/dispose records and consumed sequence numbers. After an
  Enter, continue observing the next timer or terminal result; do not wait
  exclusively for a prescribed next Enter. If a timer and terminal task are both
  complete, collect final state and prefer the terminal outcome.
- Advance only the next registered admissible deadline: 250-ms polling through
  t=2 seconds, then (where applicable) one registered `AbsentSettle` delay. Keep
  an explicit finite schedule bound (eight poll deadlines and at most one
  settle), and capture unexpected timers/events as driver diagnostics. Never
  invent an extra advance to make a missing Enter occur. The five-second guard
  remains solely for a broken fixture with neither task nor event progress.
- PC-33: with only two extra attempts, Enter counts stop at three while polls
  continue to t=2; collect PromptDeliveryException and assert count four at
  `submit-enter-4-before-deadline`. Do not throw on the missing fourth Enter
  inside the driver. In the allowed schedule, retain Enter two/three at
  250/500 ms and Enter four at 750 ms before the deadline.
- PC-38: the second turn's stale-receipt mutant completes early; return that
  successful outcome immediately. The test fails
  `submit-does-not-reuse-old-receipt` because it is not PromptDeliveryException.
  No expected-Enter or next-timer wait can preempt it.
- Move the direct poll-clock probe to the beginning of
  `An_unconfirmed_submit_over_a_live_transcript_throws_prompt_delivery`, before
  starting the adapter schedule. All direct delegates complete synchronously,
  so fake timer inventory is available on return from SubmitAsync. Assert the
  registered 250-ms timer at `submit-poll-uses-clock`, then cancel/drain in finally.
  PC-39's System substitution must fail that inventory assertion immediately.
- Likewise move the direct zero-budget blind probe to the beginning of
  `A_blind_first_turn_with_the_body_still_standing_after_every_Enter_throws_composer_may_hold_body`.
  Assert the fake `AbsentSettle` timer at `blind-settle-uses-clock` before driving
  the adapter. PC-40 cannot reach the driver first.
- Retain the separate held-third-read cutoff schedule and
  `submit-expired-budget-stops-repress` (PC-37), body-once checks, two blind looks
  and omitted/null/System defaults. Finally cancels the send and outstanding
  timer/Enter waits, awaits every owned task, then disposes CTS/adapter; no
  detached `ContinueWith` is responsible for lifetime cleanup.

## N4: readiness hold after the first-poll observation

In `RunnerCodexAdapterReadyTests.One_snapshot_is_used_for_each_startup_decision`,
construct the scripted client without `BeforeSnapshotAsync`. Start readiness,
observe the registered 50-ms poll without advancing time, and assert exactly one
**completed** snapshot at `snapshot-one-before-first-poll`. PC-1's extra awaited
read is now free to finish: count two fails this assertion.

Only after that assertion, install the existing attempt-two hook and advance to
the registered deadline. Preserve the race among second entry, readiness terminal
task and next timer; require two attempts/one completion while held, then release
and require exactly two completions. Thus PC-2 and PC-3 keep their current witnesses.
Keep System-default assertions and unconditional release/cancel/join/dispose.
Only this test file changes; no new test result, clock API or client API is needed.

## N3: C578 observation and cancellation ownership

### Files and protocol

Extend `Scripts/ScriptHarness.cs` and `Scripts/RunCheckpointScriptTests.cs` under
`tests/Antiphon.Tests`, and `scripts/test-run-checkpoint.ps1`. Add
`scripts/fixtures/c889-c578-observation.ps1` (shared real/scheduled observation),
`scripts/fixtures/c889-script-harness-child.ps1` (generic harness contract child),
and `tests/Antiphon.Tests/Scripts/ScriptHarnessCancellationTests.cs`. Keep all
PowerShell ASCII. The production checkpoint, build-slot and nightly/release
scripts are read-only. There is no new worker mode or TestDb lifecycle count.

Keep `RunHarnessCaseAsync(harness,prefix,caseName,expectedRows,requiredRows)` as a
source-compatible forwarding wrapper. Factor its process execution and unchanged
PASS/FAIL/exit/inventory validation so the new overload can accept a caller token
and typed test controls (phase barrier, deliberate noncooperation, diagnostics).
Both entry points use the same process owner, validator and 300-second guard.
Pre-canceled input returns cancellation before Process.Start. A terminal run's
captured exit/output is not reclassified by cancellation arriving after completion.

For C578 only, create a cancellation descriptor before launch: unique results
root, invocation nonce, and confined cancel/ack paths. Supply it through the
child's `ProcessStartInfo.Environment`, not mutable parent environment or command
text. The operation token links the existing 300-second guard with the caller
token. Its registration atomically publishes a nonce-bearing cancel file and
records any write failure for fallback diagnostics; it executes no PowerShell.
Cancellation before subscription is handled by immediate file recheck. Unrelated
scripts receive no new mandatory arguments and use forced cleanup if canceled.

The standalone no-Case CP-14 invocation has no C# parent descriptor. In that
entry path the offline script owns an equivalent invocation-scoped 300-second
guard and descriptor, using a timer event in its foreground event loop to
publish cancellation and enter the same finally cleanup. It must not silently
run an uncancelable event wait or fall back to the old ten-second readiness
decision. The checkpoint executor remains its outer process owner. Exercise
this descriptor-absent initialization in the shared fixture's finite schedules;
CP-14 also proves the ordinary standalone path. Do not install an additional
300-second guard when the C# descriptor already supplies one.

Each real child identity is retained as an open Process plus observed start time.
The C578 harness records its wrapper immediately after Process.Start; the shim
publishes entry before waiting on any test barrier, with root nonce/phase/PID/start.
Persist that small identity journal under the owned root so the C# owner can adopt
already-started children even if the harness exits before cooperative cleanup.
Validate nonce, confined root and process generation before touching one; preserve
the current cross-process Linux start-time tolerance with captured identity, not
PID-only lookup. Never discover/kill by process name. A cancellation test does
not proceed until its targeted phase and ownership record have been acknowledged.

### One observation path for readiness, log completion and release

The shared fixture exports a single decision function and an observation-cycle
function. A snapshot contains invocation/phase/identity, wrapper and child liveness,
entry/ready/release/cancel state, complete log contents and stream-fence state.
Decisions include Pending, ObservedReady, Released, Cancelled, PrematureExit,
IdentityMismatch and MarkersMissing. Logical elapsed time is diagnostic only.

Real parent waits and shim release waits both invoke this cycle; no inline shadow
of its checks is permitted. Subscribe to relevant file changes/creation/rename,
process exit and cancellation, then immediately read complete state; after every
wakeup reread rather than trusting the event. Retain subscriptions in an owned set
and dispose/unregister all of them in finally. File-change duplicates and files
created before subscription must not lose progress. Partial/unreadable records
remain Pending unless cancellation, exit or a completed contradictory record
provides a terminal observation.

`New-C578Case -FailBuild -Hold build` is the normal failure setup. Ready admission
requires the matching live wrapper/child, expected phase and nonce, ready record,
and both stream markers in the **wrapper log**. Add a nonce-bearing fence after
each stream marker, flushed on that same stream. Observing both fences means the
earlier marker positions have been consumed; a missing marker then produces
MarkersMissing finitely. Ready alone or shim-side Flush does not prove that.
This supplies PC-43's missing-stderr witness without a timeout or production
wrapper instrumentation. Pending partial logs must not be rejected prematurely.

After ready is admitted, assert child-held evidence, then publish the phase/nonce
release. The shim invokes the shared release observation path, rechecks release
after subscription and exits with the existing 37; parent joins and drains the
wrapper and asserts actual log exit 37 exactly once, checkpoint exit 2 and no test
entry/log/TRX/green receipt. On MarkersMissing, capture that diagnostic, release
and drain the still-owned child, then fail the existing stdout/stderr assertion.
Do not throw a generic ready timeout ahead of PC-43's named assertion.

Controlled schedules precede the real shim run and execute the same observation
cycle: child-exit-before-observation, valid Ready at logical +11 seconds,
wrong identity, files-complete-before-subscription with explicit cycle end and
no later event, and release-before-subscription. The shared hold/release decision
must actually gate the shim's exit; disabling hold fails the finite logical
live-held schedule for PC-41. PC-42 mutates the same elapsed-time decision;
PC-69/70/71 mutate the actual identity/recheck paths. PC-72 observes a nonempty
subscription set after inner disposal is suppressed. Logical cycle-end is a
test input for finite observation, never a fabricated real filesystem event.

### Cancellation and two cleanup owners

| Owner/boundary | Obligation |
|---|---|
| C# before start | Allocate descriptor, retain operation CTS and cancellation registration; start both pipe drains immediately after Process.Start; retain process outside the try body. |
| Harness wait / shim hold | Feed cancel-file observation into the same shared cycle, including before-ready and partial-log states. On cancellation do not publish success readiness or normal release. Record Cancelled acknowledgment with nonce and phase. |
| Harness finally | On interrupted/canceled build, stop wrapper **first**, then captured shim, to preserve the interrupted-build no-completion-receipt contract. On normal failed-build, release then join normally. Join exact owned processes, drain wrapper pipes and dispose all subscriptions/process handles. Write cleanup acknowledgment only after those facts hold. |
| C# finally, independent rescue | Publish cancellation if abnormal/pending; allow at most two seconds for cooperative termination/ack, solely a cleanup grace. If absent, kill the retained harness tree while its root is live, then sweep only independently captured/journal-validated surviving children. Join root and each owned child and await both stdout/stderr drains under an uncanceled cleanup budget (at most the existing ten-second join bound). Dispose cancellation registration before deleting its root. |
| Failed cleanup | Preserve root, identity/phase/output diagnostics and report cleanup failure. A kill request, disposed Process or abandoned ReadToEnd task is not joined/drained evidence. Do not delete ownership records before cleanup completes or label fallback as successful cooperative cancellation. |

The two-second grace and ten-second cleanup bound start **after** the operation
has ended/canceled; they cannot decide readiness or enlarge its 300-second guard.
An exhausted cleanup bound is a failed ownership assertion requiring diagnosis,
not a passing cancellation case. Keep primary failure and cleanup failure together.
Do not use the already-canceled operation token to await cleanup.

PC-47/72 observe inner cleanup **before** outer rescue. Their held child or live
subscription must fail `C578 FailedBuild owned processes exited`; outer rescue
then actually releases/kills/joins/disposes. For a deliberately stranded shim,
retain the parent observation/custody objects until that rescue has finished.
The C# rescue remains independent of the PowerShell cleanup under test.

### New tests and unchanged label contract

The new `ScriptHarnessCancellationTests` is Integration with the assembly's
`ParallelLimiter<ProcessSpawnLimit>`. Exactly five methods / eleven results:

| Method and literal arguments | Results | Required outcome |
|---|---:|---|
| `C578_cancellation_reaches_the_held_phase_and_joins_owned_processes("before-ready"|"partial-log"|"release-held")` | 3 | Actual C# token publishes cancellation to actual C578 wait; cooperative Cancelled acknowledgment, no forced rescue, root/wrapper/shim exited, both streams drained, subscriptions empty, no green/run receipt. |
| `C578_cancellation_falls_back_to_outer_owned_tree_cleanup()` | 1 | Same real C578 process tree deliberately ignores cooperative cancellation; outer fallback is recorded, every captured identity exits and streams finish; the outcome remains canceled. |
| `Noncooperative_harness_cancellation_joins_process_and_drains_streams()` | 1 | Generic fixture child ignores signal and holds both pipes; cancel after its entry barrier, exercise shared C# tree kill/join/drain, with pre-cancel stdout/stderr sentinels retained. |
| `Harness_result_validation_preserves_exit_and_inventory_failures("success"|"nonzero"|"missing-label"|"fail-label"|"wrong-count")` | 5 | Generic real child emits controlled output; unchanged shared validator accepts only success and identifies each specific failure. Cleanup is complete for every result. |
| `Precancelled_harness_does_not_launch()` | 1 | Pre-cancel caller token; observed launch count zero and no child-entry file, not merely a cancellation exception. |

Before-ready means shim entry/ownership is acknowledged but ready is withheld.
Partial-log means stdout marker/fence observed in wrapper log, stderr/ready phase
withheld. Release-held means complete ready/full-log admission, normal release
withheld. Parent and shim both observe cancellation while these test barriers
are held. Trigger cancellation from those causal phase acknowledgments, never
from a timed sleep or by shortening the normal 300-second budget.

Expose these phase controls through the C# overload and a confined test-only
environment descriptor. Invoke the real `C578_FailedBuildKeepsLogAndExit` path
with those controls; do not register extra `Test-C578_*` script functions.
The generic child lives under `scripts/fixtures`, not top-level harness discovery.
Cancellation tests capture the canceled result and cleanup diagnostic, bypassing
the *success* inventory assertion only because cancellation is their expected
outcome; ordinary calls still run the unchanged validator.

Use explicit detecting labels `harness-cancel-reaches-phase`,
`harness-cancel-cooperative`, `harness-fallback-joins-owned-tree`,
`harness-both-pipes-drained`, `harness-subscriptions-disposed`,
`harness-precancel-no-launch` and `harness-result-validation` in this new class.
Carry descriptor-source (caller or standalone), cancellation-published/observed
and cleanup-mode facts separately so fallback cannot counterfeit propagation.
Controlled cancellation records and observation-cycle completion provide finite
logic witnesses before rescue; later native exit/drain assertions prove actual
process cleanup. A test barrier after ready must keep running that cancellable
observation cycle while normal release is withheld, not block on an unobserved
release-only wait.

Keep the five existing failed-build labels verbatim, and add exactly:

- `C578 c578-child-held-until-observed`
- `C578 c578-late-ready-event-accepted`

Each emits once, aggregating internal schedule checks. Update failed-build
`expectedRows` 5 -> 7, and `C585ExpectedRows` `62 + 28 + 19` -> `62 + 28 + 21`.
Streaming/failed/interrupted counts remain 8/7/6; bridge remains 24 TUnit results;
full no-Case harness becomes 111 rows. New fixture ASCII checks are internal to
the new contract tests and must not add PASS labels to this inventory.

## Implementation slices and TestDesign handoff

| Slice | Files | Tests / control acceptance |
|---|---|---|
| N1 | `tests/Antiphon.Tests/Infrastructure/Resilience/ResilienceTestSupport.cs`, `ResilienceBudgetTests.cs` in the same directory | Existing slow-first method; 14-result two-class regression. PC-5..10 preserved, especially finite retry/held-step PC-7/10. |
| N2 | `tests/Antiphon.Tests/Agents/RunnerCodexAdapterSubmitConfirmTests.cs` | Eight existing methods; PC-33..40 retain labels, finite PC-33/38 and direct-first PC-39/40. |
| N4 | `tests/Antiphon.Tests/Agents/RunnerCodexAdapterReadyTests.cs` | Existing one-snapshot method, 12-result class; PC-1..4 retained. |
| N3 with S6 | `tests/Antiphon.Tests/Scripts/ScriptHarness.cs`, `RunCheckpointScriptTests.cs`, new `ScriptHarnessCancellationTests.cs`; `scripts/test-run-checkpoint.ps1`, new `scripts/fixtures/c889-c578-observation.ps1`, new `scripts/fixtures/c889-script-harness-child.ps1` | Existing C578 controls PC-41..47/69..72, 24 bridge results / 111 no-Case rows; eleven cancellation/validator results plus three exact compatibility methods below. |

Paths listed as same-directory leaves above are relative to the fully named
directory in their row. No files under `server/` or `src/` may change. Keep the
parent S4 slice and new helper paths unchanged. Commit/push each meaningful source
slice; the combined ordinary manifest runs at the final committed source.

TestDesign must:

1. Incorporate these four seams into the refresh and replace its current Plan
   refusal with a source-grounded reachability assessment. Preserve all 74 IDs
   and each old label. In particular audit exception/count assertion order,
   PC-7 retry observation, PC-43 fence semantics, and PC-47/72 inner-vs-rescue checks.
2. Add guard/control mappings for cancellation publication/consumption, the three
   held phases, fallback process cleanup, stream drain, registration disposal,
   pre-cancel launch refusal and shared validator compatibility. Assign new PC
   IDs after 74; never absorb them by deleting inherited PCs. Each compiling
   defect needs a finite named assertion before independent rescue. No raw
   300-second timeout is an acceptable failure witness.
3. Confirm the eleven new results and shared-caller regression choices below;
   recompute method-scoped Mutation execution/build/restoration costs. The old
   1,775-minute estimate covers only its 74 controls, not these new guards.
4. Re-read current defaults/runners plus pipeline/hosts and full board-scoped task
   goals and exact changed/dirty paths immediately before Code. Include the new
   shared ScriptHarness and fixture paths in collisions; the refresh's previous
   disjointness observation is not a reservation. No host pin bypasses a collision.
5. Freeze one authoritative executable `## Verification design` and checkpoint
   table. If slices are commissioned separately, define their own build rows and
   After groups first; do not import rows from two documents or reuse stale bins.

### Checkpoints

This is the **proposed complete replacement roster for TestDesign**, not an
executable Code commission. CP-1..14 retain the refresh's scope and IDs; CP-15..18
are the explicit shared-helper additions. V-11/R-10 are proposed cancellation
and shared-harness coverage IDs for TestDesign to formalize. Lane appears in
Group/Expect, not a new importer column. `all` means S4, S6 and N1-N4 committed.
Linux selects all rows except CP-6; Windows selects all except CP-5.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c889-seams/` | both-ready | `/*/*/RunnerCodexAdapterReadyTests*/*` | V-6,R-5 | Linux and Windows: 12, 0 failed/skipped | 12 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | all | CP-1 | both-resilience | `/*/*/(ResilienceBudgetTests*)\|(HttpResilienceRegistrationTests*)/*` | V-7,R-6 | Both OSes: 14, 0 failed/skipped | 14 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | all | CP-1 | both-submit | `/*/*/RunnerCodexAdapterSubmitConfirmTests*/*` | V-8,R-7 | Both OSes: 8, 0 failed/skipped | 8 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | all | CP-1 | both-scaled | `/*/*/ScaledTimeProviderTests*/*` | V-10,R-9 | Both OSes: 6, 0 failed/skipped | 6 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | all | CP-1 | linux-grok-launch | `/*/*/GrokRulesLaunchRefusalTests*/(Cold_grok_delegate_keeps_full_composed_bundles_in_typed_payload)\|(Named_grok_agent_with_a_single_line_append_composes_the_rendered_line_byte_identical)\|(Over_budget_single_line_composition_still_throws_invalid_operation)` | V-9,R-8 | Linux: 3, 0 failed/skipped | 3 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | all | CP-1 | windows-grok-launch | `/*/*/GrokRulesLaunchRefusalTests*/*` | V-9,R-8 | Windows: 5, 0 failed/skipped | 5 | 3 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c889-policy/` | both-grok-policy | `/*/*/GrokRulesArgvPolicyTests*/*` | V-9,R-8 | Both OSes: 26, 0 failed/skipped | 26 | 4 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | all | CP-1 | both-land-worker | `/*/*/AgentTaskLandRecoveryTests*/C448_V15_RealWorkerDeathRecoversDurableBoundaries` | V-1,R-1,R-2 | Both OSes: all 7 cuts, 0 failed/skipped | 7 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | all | CP-1 | both-land-delivery | `/*/*/PostLandMutationDeliveryTests*/C478_V09a_LandCrashMatrix` | V-2,R-1,R-3 | Both OSes: 12, whole recipient receipt assertions, 0 failed/skipped | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | all | CP-1 | both-retirement | `/*/*/WorktreeResidueRecoveryTests*/C459_WorkerDeathAtEveryRetirementHandoff` | V-3,R-2 | Both OSes: all 13 cuts, 0 failed/skipped | 13 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | all | CP-1 | both-worker-registry | `/*/*/TestDbFixtureLifecycleTests*/Worker_mode_list_names_every_owned_child_worker` | V-4,R-2 | Both OSes: 1, 0 failed/skipped | 1 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | all | CP-1 | both-worker-warmup | `/*/*/TestDbFixtureLazyInitializationTests*/A_worker_child_exits_before_the_shared_store_warmup` | V-4,R-2 | Both OSes: 8 after S4, 0 failed/skipped | 8 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | all | CP-1 | both-script-bridge | `/*/*/RunCheckpointScriptTests*/*` | V-5,R-4 | Both OSes: 24, 0 failed/skipped; C578 8/7/6 labels | 24 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | all | n/a | both-script-full-inventory | `pwsh -NoProfile -File scripts/test-run-checkpoint.ps1` | V-5,R-4 | Both OSes: 111 passed, 0 failed, 111 rows; C487 HARNESS EXIT CODE: 0 | n/a | 5 | true | n/a |
| CP-15 | all | CP-1 | both-harness-cancellation | `/*/*/ScriptHarnessCancellationTests/*` | V-11,R-10 | Both OSes: exact 11 results, 0 failed/skipped, no owned residue | 11 | 5 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | all | CP-1 | both-harness-buildslot-compat | `/*/*/BuildSlotScriptTests/C589_WrapperRunsUnderLease` | V-11,R-10 | Both OSes: 1, 0 failed/skipped, 7 internal labels | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | all | CP-1 | both-harness-nightly-compat | `/*/*/NightlyVerificationContractTests/C544_DailyValidity` | V-11,R-10 | Both OSes: 1, 0 failed/skipped, 6 internal labels | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-18 | all | CP-1 | both-harness-release-compat | `/*/*/ReleaseGateStatusTests/C599_StatusIdentity` | V-11,R-10 | Both OSes: 1, 0 failed/skipped, 7 internal labels | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

The three compatibility methods exercise existing unchanged callers with distinct
prefixes/scripts through the retained API. Existing CP-13 covers normal success,
failure, inventory and stream checks; CP-15 adds explicit shared-validator negative
cases and process cancellation. This bounds the shared-helper change without
retesting unrelated release publication or nightly decision policies wholesale.

### Cost and execution constraints

Projected roster: **148 Linux / 150 Windows TUnit results** (prior 134/136 plus
11 new and three existing compatibility selections), plus 111 CP-14 internal
rows per OS. Ordinary literal table cost **83 minutes**; resolve one OS alternative
to **80 minutes per OS / 160 minutes total**, up 16 from the refresh. The 10-minute
two-OS tool/setup allowance remains separate. Values are planning estimates;
there were **zero builds, tests or mutations** in this Plan task. New seam authoring
is estimated at 4-8 engineer hours, excluding inherited S4/S6 implementation.
TestDesign must add new guard costs to the inherited 74-control Mutation estimate;
no new total Mutation cost or executable certificate is asserted here.

After TestDesign freezes its manifest, run the checkpoint tool once per committed
group with exact rows and `--expected-source-sha`; keep waiting through exit 75
until complete. Bootstrap/other build or test drivers take `scripts/build-slot.ps1`;
checkpoint rows own their slots. Slot exit 4 is not permission to bypass the gate.
Retain one isolated Antiphon.Tests build and one policy-project build per OS;
reuse only inside the same source/OS/After/run with verified build provenance.
No source edits during runs; TUnit uses `dotnet run`, not `dotnet test`. Keep
process-limit fixtures and avoid co-scheduling with Pty/FakeClaude suites.

Future Code/Review report each CP's actual counts, unedited CHECKPOINT lines,
exact source/OS and clean build receipts; run `check-evidence-diff.ps1` over their
full task range. Generated payloads stay ignored. Diagnose any red at the same
exact method on the recorded base before blaming new source; never widen a
timeout/assertion to make it pass. Clean only owned alternate outputs using the
checkpoint cleanup path. Post-land Mutation remains separately commissioned,
method-scoped and externally recorded, with independent restoration and cleanup.
