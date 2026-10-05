# CARD-0820: Windows checkpoint timing and temp-root contention

Date: 2026-10-05. Design baseline: `be16e6c35b5fd9ced3622c2b3e313ae836f416f8`.
Card: CARD-0820, Antiphon board. Related fixture-lifetime defect: CARD-0828.
Complexity: **medium**. Next stage: **test-design**, separately commissioned after this
plan is landed. This document does not authorize Code before the formal verification
design and executable checkpoint manifest are added.

## Outcome and evidence limits

Make checkpoint tests advance on observed phases, own every asynchronous execution,
and exercise Windows sharing violations without racing the thread pool. Preserve
production ownership, renewal and deletion semantics. Avoid taking a test-root lock
for roots already known to be too young or owned by a live/uncertain process; still
revalidate every deletion condition under that lock.

The supplied CARD-1039 M-2.n evidence at `53e165503` reports six deadline failures
and one held-open tool-copy failure in the 32-class `m2n-unit-016` Windows Unit
chunk. Its provenance is task `7951fe34` and
`C:\logs\antiphon\card-1039\0fd2a915\m2n-0025fbfd\m2n-unit-016\run.trx`.
That artifact is on another host and was not opened in this Plan task. Treat its
counts and the Linux baseline passes as supplied evidence, not a fresh reproduction.
No build or test was run during Plan.

Read the live CARD-0820 and CARD-0828 descriptions. CARD-0828 adds stronger causal
evidence than “passes alone”: Windows Review `c1148a3a` captured a CPU-spinning
`TaskOwnerGuard.WatchAsync` after the two uncertainty tests timed out; Review
`37267e24` observed an unjoined execution recreate a deleted fixture root fifteen
minutes later. The current test bodies still permit both behaviors. The proportion
of the seven newer failures attributable to this leak, ambient load, filesystem
latency or other blocking work is not measured. Linux passes do not rule out a logic
or lifetime defect on Windows.

## Ground truth

Paths below are relative to the repository. Line numbers refer to the design baseline.

| Card/brief assumption | What the current code does | Design consequence |
|---|---|---|
| Replace sleeps with event waits in all six deadline failures. | The four named ownership tests already await `TaskCompletionSource` events, usually with a five-second `WaitAsync`. See `tests/Antiphon.Tests/Checkpoints/CheckpointTaskOwnershipTests.cs:224,350,527,602`. | Keep the events. Remove phase-local elapsed-time success criteria; fix the fake clock, ownership and missing acknowledgments. A polling helper alone would change nothing. |
| Renewal tests have a fully virtual clock. | `BuildSlotClient` accepts acquisition `clock`/`delay`, but `RenewUntilReleasedAsync` directly calls `Task.Delay` at `tools/Antiphon.Checkpoints/Slots/BuildSlotClient.cs:315`. Renewal fixtures return a one-second interval and race a three-second observation limit. | Add a separate controllable renewal-delay seam with the existing real delay as its default. Do not reuse the acquisition fake's immediately-completing delay. |
| `renew_and_release_diagnostics_keep_status_and_body` is one three-second wait. | `CheckpointSlotContractTests.cs:245` first waits up to ten seconds for the request, polls the log for another ten, then uses two three-second waits for renewal entry and disposal. Request observation precedes production diagnostic emission. | Observe the diagnostic sink itself and the drain/delete ordering; explicitly drive both lease instances. |
| Uncertainty fixtures finish or cancel on failure. | `GatedUncertaintyClock.Release()` permanently completes one gate; every subsequent delay completes immediately and advances `_now`. Both tests pass `CancellationToken.None` to `ExecuteAsync`; their `finally` swallows a timeout without cancellation or a completed join. | Replace the permanent-open gate with one permit per requested delay, and own/cancel/join `ExecuteAsync`, including every failing assertion path. This is the CARD-0828 overlap. |
| Slot-loss test proves a late grant cannot launch. | The method named `ownership_loss_cancels_slot_wait_and_rejects_a_late_grant` currently only throws cancellation from `Acquire`; it never returns a late lease. | Preserve cancellation coverage and add a controlled late-grant branch, with zero driver starts and exactly one release. |
| Terminal-publication test proves both driver-exit and lease-disposal barriers. | It waits for driver cancellation, releases `driverExit`, and immediately checks `execute.IsCompleted`; it never acknowledges entry into lease release before the second negative assertion. | Observe `release-entered` before asserting no terminal publication, then release and await the final receipt/state. |
| `tool_copy_removal_retries_while_a_file_is_still_held_open` proves retry behavior on every OS. | `EvidenceFolderTests.cs:12` schedules stream disposal via `Task.Run` plus 300 ms, then synchronously deletes. `ToolCopyCleanup.Remove` retries ten attempts with nine sleeps totaling 2.25 s. Unix can unlink the open file on its first attempt. | Make the sharing-violation proof explicitly Windows-only and release on the second observed delete attempt. Keep a portable wrapper-success test. |
| Every `TempDir()` registration still waits on a global lock and fsyncs the JSONL index. | Commit `dbf9f67e3` replaced registration with per-root files. `CheckpointTempRootSweep.Register` writes/renames its own index entry without acquiring the sweep gate; `OpenGate` is a single nonblocking attempt. Legacy JSONL is migrated and removed. See `CheckpointTempRootSweep.cs:62-109,323` and `docs/testing-and-build.md`, “Checkpoint temp custody and usage.” | Do not reimplement the landed index fix or enlarge its lock budget. The historical 95 KB index and 3,936 unmarked roots are not current-host measurements. |
| The sweeper may contend with a live root's `RegisterRun`. | This remains possible: `SweepOnce` takes `TestRootGuard.TryLock(root)` before `Eligible` checks grace/owner, while `TestRootGuard.RegisterRun` immediately throws “checkpoint test root is busy” on failure. | Reproduce this ordering deterministically in a sandbox and add a conservative pre-lock exclusion. Retain the complete locked eligibility check before deletion. |
| Registering a background task alone makes fixture deletion safe. | `CheckpointTestScope.DisposeAsync` records a failed/timed-out work join, then continues root deletion unless a child process is unsafe. Unfinished in-process work is not a deletion veto. | Add a veto for registered work still incomplete after the existing cleanup deadline; retain roots and fail teardown. A completed faulted task is reported but cannot write again. |
| All older card failures belong to the same mechanism. | CARD-0742 changes to resilience tests are present as `c345371e2`. `C448_V36_EachAuthorityCoordinatePrecedesMutation` validates 23 authority combinations; `C688_UnregisterDropsHeadFilesWithoutTouchingSetAsideOrRecreatedPath` exercises separate no-follow filesystem removal. Neither body contains the checkpoint phase waits above. | Do not alter resilience budgets or landing authority/deletion behavior on the strength of this checkpoint evidence. Keep those observations open for their own targeted investigation. |

## Decisions

- **D-1 — Medium complexity; separate TestDesign.** There are independently bypassable
  ownership, cancellation, renewal-drain and deletion guards, a real Windows file
  sharing oracle, and an overlap with CARD-0828. Reject an “easy timeout edit” and a
  prose-only verification section. TestDesign must map each guard to one distinct,
  executable PC and complete the manifest before Code.
- **D-2 — No numeric budget increase.** The evidence establishes unsynchronized
  scheduling and abandoned work, not a necessary new timeout value. Keep production
  renewal intervals, HTTP limits, owner uncertainty budget, tool-copy retry count/
  backoff, and sweep limits unchanged. Await phase tasks with the test's cancellation
  token; keep hang detection outside semantic assertions at the bounded checkpoint
  row. The fixture's existing ten-second teardown watchdog remains a failure bound,
  with retained roots on an incomplete join. Do not replace 3/5 with 30/60 or remove
  the external watchdog. TestDesign must specify cancellation propagation for direct
  test invocation as well as checkpoint execution, without inventing a larger phase
  deadline. A later budget proposal requires measured phase/queue/cleanup durations
  on the fixed source, an exact failing method, and evidence that the intended event
  actually occurred after the existing enclosing limit; return to Plan first.
- **D-3 — One owner for the CARD-0828 overlap.** S1 includes the fixture-lifetime
  repair because leaving the known CPU loop in place invalidates this timing fix.
  The caller must serialize CARD-0828 work against S1 and reconcile the shared
  acceptance evidence. If an equivalent fix lands first, reuse it and preserve its
  tests instead of maintaining two helpers. No production `TaskOwnerGuard` policy
  change is needed.
- **D-4 — Isolate renewal scheduling from acquisition.** Add an optional
  `Func<TimeSpan, CancellationToken, Task>` renewal delay at the end of the
  `BuildSlotClient` constructor, defaulting to `Task.Delay`. Tests provide a
  cancelable, one-step scheduler. Reject changing `_delay` globally: existing
  acquisition fakes intentionally return `Task.CompletedTask`, which would create
  an unbounded renewal loop or prevent acquisition returning.
- **D-5 — Test file sharing with an observed attempt.** Use the existing
  `ToolCopyCleanup(beforeDelete: ...)` seam and a real `FileStream` opened without
  `FileShare.Delete`. First deletion must really fail on Windows; release the handle
  inside the second callback, then require `Removed`, exactly two attempts and no
  tool directory. No new deletion seam or background release worker is needed.
  Retain `EvidenceFolderTests.non_image_remove_drops_the_tool_copy` as wrapper
  coverage. Reject a longer sleep, retrying the test, or accepting Unix unlink as a
  Windows sharing proof.
- **D-6 — Pre-lock checks only exclude; they never authorize deletion.** Before a
  root lock, read the validated marker and skip young, live or uncertain roots.
  For remaining candidates acquire the lock and re-run all existing eligibility,
  identity, containment, inventory and nested-executor checks. Reject removing the
  root lock, caching permission to delete, retrying `RegisterRun` indefinitely, or
  deleting legacy roots by name/age. No index-format or migration change.
- **D-7 — Narrow runs and unchanged assertions.** Retain exit-code, state-file,
  report, cancellation, release-count and no-mutation assertions. Exercise the six
  named deadline methods and native file case, plus the small directly affected
  guard roster. No whole Unit, namespace, assembly or artificial CPU-burner run.
  Normal checkpoints and at most two Windows ambient-load repetitions are enough
  for this design; repetitions are evidence, not retries that erase failures.
- **D-8 — Placement follows live policy.** Read `GET /api/runner-defaults` and
  `GET /api/session-runners` before execution/dispatch. Both were read on 2026-10-05:
  defaults revision 2 had no per-kind overrides, and eligible Linux and Windows
  execution were advertised. These are observations, not a fixed fleet location.
  Portable work omits `-Runner` and `-Platform`; native Windows qualification uses
  `-Platform Windows` and omits `-Runner`. `-Platform Any` is only for clearing a
  prior OS pin. No host names, addresses or occupancy values are execution inputs
  to this plan.
- **D-9 — Completion is scoped.** Completing these slices establishes the named
  checkpoint guarantees. It does not establish that all historical Windows Unit
  flakes are solved. Preserve the unrelated observations and investigate them with
  their exact method/argument filters before closing those obligations.

## Implementation slices

Each slice is a 30–60 minute Code unit including its targeted checkpoint, with a
commit/push before the checkpoint. These are implementation instructions for the
later Code dispatch, not changes made by this Plan task.

### S1 — Own execution lifetime and advance owner checks explicitly (50–60 minutes)

Files: `tests/Antiphon.Tests/Checkpoints/CheckpointTaskOwnershipTests.cs`,
`CheckpointTestScope.cs`, `CheckpointTempScopeTests.cs`, and a small shared
`CheckpointTimingHarness.cs` in the same directory if extracting repeated fixture
logic is necessary. If new checkpoint cases are added, update the independent
census in `scripts/lib/checkpoint-usage.ps1` in this same slice.

Use a linked abort source for each of the four named ownership executions. Register
the execution with the scope immediately, hold all driver/release gates in a fixture
owner, and in `finally` cancel, release held gates and join before deletion can
begin. Report faults; do not swallow a timeout and return success. Keep the abort
path independent of production owner settlement, so failed assertions can clean up
even when the behavior under test is broken. `CheckpointTestScope` must retain all
roots when a registered task is still incomplete at teardown, and emit a failure.

Replace `GatedUncertaintyClock` with explicitly acknowledged delay requests: one
advance completes one request; subsequent delays remain pending and honor
cancellation. Advance virtual time only when that request is released. Synchronize
shared status/counters and retain `RunContinuationsAsynchronously`. Drive enough
three-second steps to reach the configured uncertainty budget; observe the
`owner-unverified` sink receipt before asserting the running row survives. For late
settlement, change the handler to `Canceled`, release the next pending read and
await driver cancellation and the completed execution.

For slot loss, cover both cooperative cancellation and an intentionally late lease
returned by the fake after cancellation. Assert no driver launch and release of that
lease. For terminal publication, acknowledge lease-release entry after driver exit;
assert `state.json` is not done until release completes, then assert final exit and
release count. Race a phase wait against early execution completion so an unexpected
executor failure surfaces as that failure rather than an opaque wait timeout.

Tests: retain all four original method names in the ground-truth table. Add
`CheckpointTaskOwnershipTests.aborted_phase_joins_execution_before_root_teardown`
(force fixture abort at a known phase, await the completed executor, then tear down
the root; no sleep-based “nothing happened” assertion), and
`CheckpointTempScopeTests.unfinished_registered_work_retains_roots`. The latter
must explicitly release/join its synthetic held task in its own `finally` so the
negative scenario itself leaves no work behind. Reuse a teardown-deadline seam if
available; otherwise introduce only a fixture-local controllable cancellation seam,
with the existing ten-second default unchanged.

### S2 — Control renewal and observe emitted diagnostics (35–45 minutes)

Files: `tools/Antiphon.Checkpoints/Slots/BuildSlotClient.cs`,
`tests/Antiphon.Tests/Checkpoints/BuildSlotClientTests.cs`,
`CheckpointSlotContractTests.cs`, and the S1 timing helper if needed.

Add D-4's renewal-only seam. In
`renewable_grant_is_renewed_until_the_checkpoint_releases_it`, wait for the delay
registration, release exactly one renewal step, and observe the HTTP renewal.
Dispose the lease and prove the next pending delay was canceled, renewal finished,
and DELETE occurred. Do not open all future delays or rely on an observation sleep.

In `renew_and_release_diagnostics_keep_status_and_body`, signal a task from the
log callback when the actual renewal diagnostic has been appended. Assert its
status/body and the release diagnostic after disposal. For the drain case, keep
renewal completion behind an acknowledgment even after cancellation is observed;
assert DELETE has not begun at that boundary, release the drain, then require the
exact event sequence and completed disposal. Dispose every acquired lease on every
failure path. Preserve both null-renewal regressions and acquisition delay semantics.

### S3 — Make the held-file retry proof deterministic on Windows (30–40 minutes)

Files: `tests/Antiphon.Tests/Checkpoints/EvidenceFolderTests.cs` and
`scripts/lib/checkpoint-usage.ps1` for the explicit OS-skip roster. The existing
`ToolCopyCleanup` implementation needs no retry-budget change.

Rewrite `tool_copy_removal_retries_while_a_file_is_still_held_open` as D-5's direct
cleanup proof, with an explicit Windows guard. Retain its historical method name.
Use `using`/`finally` for the stream even if cleanup fails. The first callback leaves
the stream held; the second releases it. A no-retry defect must produce `Failed`
and fail the `Removed` assertion. A test that deletes successfully on its first
attempt must fail the attempt-count assertion. Include the portable wrapper test
and `CheckpointToolCopyCleanupTests.failed_delete_receipt_is_truthful_and_retryable`
as existing regression coverage for retained failure and later retry.

### S4 — Avoid unnecessary live-root lock contention (40–55 minutes)

Files: `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs`,
`CheckpointTempRootSweepTests.cs`, `scripts/lib/checkpoint-usage.ps1`, and the
checkpoint-temp paragraph of `docs/testing-and-build.md`.

First reproduce the ordering in an owned sandbox using a probe/barrier: while the
sweep is observing a live owner's eligibility, call the real `TestRootGuard.RegisterRun`
for that marked root. The current implementation holds the root gate and refuses
registration. Do not use probabilistic racing threads or a machine-wide temp scan.
If the current implementation no longer exhibits this ordering at Code's base,
report the changed premise and amend the slice before touching it.

Implement D-6's conservative preflight. Add
`live_roots_are_skipped_without_taking_the_root_lock` and
`eligibility_is_rechecked_after_the_root_lock`. The first must register a real nested
run during the live-owner observation, preserve its journal/root, and report the
live skip. The second changes observed ownership from dead during preflight to
live under the lock and requires retained payload/marker. Existing grace, partial
deletion and nested-custody tests remain the deletion authority. Do not change
`TestRootGuard.RegisterRun`, index formats, sweep grace, retry counts or cleanup
authority. Update the owner documentation to distinguish preflight exclusion from
locked deletion authorization.

## Verification handoff to TestDesign

This section fixes coverage and placement requirements; it is deliberately not the
required `## Verification design`. TestDesign must add that complete section, with
Inspection, Delivery inventory, V/R IDs, Guard inventory, one distinct method-scoped
PC per independently bypassable behavior, Out of scope, numeric Cost and the final
`### Checkpoints` table. Review remains separate; PCs run after implementation land
from the commissioned SourceLanding snapshot.

Use the real in-process executor for owner tests. The observable chain is fake owner
HTTP response -> `TaskOwnerGuard` -> scheduler cancellation/admission -> driver exit
and lease release -> persisted state/report, joined by the fixture run directory and
row IDs. Also observe diagnostic callback completion and root registration/deletion.
No new external queue, message or session-delivery path is introduced. The fakes
cannot prove native process-tree termination or live broker behavior; those are
unchanged and outside this patch.

At minimum, design separate positive controls for: fixture abort/join ownership;
unfinished-work root retention; uncertainty versus settlement cancellation;
late-grant rejection/release; publication after driver exit; publication after lease
release; renewal invocation; renewal drain before DELETE; diagnostic status/body
preservation; locked-file retry; pre-lock live-root exclusion; and the locked
eligibility recheck. Inventory any additional independent guard while reading the
final design, and give it its own PC. Each mutation must compile and reach the
specific assertion in one exact method; a fixture exception, timeout with no
assertion, zero-test selection or build failure is not positive-control evidence.

### Bounded checkpoint allocation

These are **candidate rows for TestDesign**, not a closed executable manifest.
Each row below states its lane, exact selection and known case floor. New method
names are proposed contracts from S1/S4, not claims that they already exist.
TestDesign binds V/R IDs, build reuse, budgets, OS-specific skips and actual
argument-expanded counts in the final schema. Keep lane placement outside the
importer's columns (it does not accept an arbitrary `Lane` column).

All projects are `tests/Antiphon.Tests`. Give each committed slice one unique
`bin-c820-sN/` build and reuse it only for rows with the same `After`. Use the
checkpoint tool through the host build-slot gate with `--rows` selection,
`--expected-source-sha`, `--row-timeout 5m`, and a **ten-minute total cap per
invocation**. If a slice has too many rows for that cap, split the invocation by
the listed rows without changing source or rebuilding a valid stamped output.
Do not silently widen the cap after a failure. Estimates below include one build
per slice; they are planning estimates, not measured durations.

| Candidate | After | Lane / placement | Exact TUnit filter | Executed floor | Estimated minutes |
|---|---|---|---|---:|---:|
| C1 | S1 | Unit, portable | `/*/*/CheckpointTaskOwnershipTests/(ownership_loss_cancels_slot_wait_and_rejects_a_late_grant*)\|(terminal_publication_waits_for_driver_exit_and_lease_disposal*)\|(uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified*)\|(late_settlement_after_owner_unverified_cancels_the_running_row*)\|(aborted_phase_joins_execution_before_root_teardown*)` | 5 | 6 |
| C2 | S1 | Unit, portable, reuse C1 build | `/*/*/CheckpointTempScopeTests/(unfinished_registered_work_retains_roots*)\|(sealed_scope_awaits_registered_work*)\|(teardown_preserves_failure_and_attempts_other_roots*)` | 3 | 1 |
| C3 | S2 | Unit, portable | `/*/*/BuildSlotClientTests/(renewable_grant_is_renewed_until_the_checkpoint_releases_it*)\|(pid_liveness_grant_with_null_renewal_is_granted_without_renewing*)\|(null_renewal_interval_is_granted_without_renewal*)\|(release_on_dispose*)` | 4 | 5 |
| C4 | S2 | Unit, portable, reuse C3 build | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | 1 | 1 |
| C5 | S3 | Unit, Windows only; `-Platform Windows` | `/*/*/EvidenceFolderTests/(tool_copy_removal_retries_while_a_file_is_still_held_open*)\|(non_image_remove_drops_the_tool_copy*)` | 2 | 5 |
| C6 | S3 | Unit, Windows qualification; reuse C5 build | `/*/*/CheckpointToolCopyCleanupTests/failed_delete_receipt_is_truthful_and_retryable` | 1 | 1 |
| C7 | S4 | Unit, portable | `/*/*/CheckpointTempRootSweepTests/(live_roots_are_skipped_without_taking_the_root_lock*)\|(eligibility_is_rechecked_after_the_root_lock*)\|(grace_is_additional_to_dead_ownership*)\|(resumed_deletion_rechecks_every_veto*)\|(marker_survives_partial_deletion*)` | 5 | 5 |
| C8 | S4 | Unit, portable, reuse C7 build | `/*/*/CheckpointToolCopyCleanupTests/(nested_live_executor_vetoes_whole_root*)\|(unknown_nested_custody_vetoes_whole_root*)` | 2 | 1 |
| C9 | Each slice that changes case/skip census | Unit, same OS as that slice, reuse its build | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | 1 each invocation | 1 each |

Windows qualification must include the **same C1–C4 and C7–C8 selections at the
final candidate SHA**, in bounded per-slice groups, as well as native C5–C6.
Portable success alone cannot close a Windows flake. This qualification may be a
separate Windows Code/Review execution task; never count an OS skip as a pass or
edit source beneath either run. Name these duplicate OS rows explicitly in the
final manifest and charge their build cost. S1/S4 census cases and S3's changed
Windows skip declaration must each pass C9; enumerate those three rows rather than
using “each slice” in the executable manifest.

For the seven reported cases, capture monotonic phase timestamps, virtual delay
requests/completions, handler calls, cancellation/join outcome, retry attempts,
root retention/deletion and native TRX durations. Record ambient Windows CPU/memory
and checkpoint build-slot occupancy. If qualification finds another red, reproduce
only the failing method at the recorded base and distinguish regression from
inherited failure. Do not rerun the whole chunk. An explicit bounded loaded proof
may repeat the unchanged affected selections at most twice; record the actual
ambient load and do not claim load coverage from an idle-host repeat.

Estimated ordinary authoring plus first-slice verification is 155–200 minutes across
four slices. The candidate rows account for 28 minutes including three census
invocations; separate final Windows qualification needs its own estimate and bounded
rows in TestDesign. TestDesign must calculate the numeric ordinary and post-land PC
floors from its completed tables; these estimates do not authorize a long combined
Code dispatch or omitted controls. Static documentation inspection and `git diff
--check` suffice for landing this Plan artifact.

## Out of scope and retained investigation obligations

- No production owner-settlement, admission, delivery, cancellation or lease policy
  changes; no live runner/broker calls in the new tests.
- No whole Unit/namespace/assembly run; no global TUnit serialization, thread-pool
  tuning, `knownFlaky` additions, automatic retries or timeout inflation.
- No new temp-index design, host-wide cleanup, legacy-root deletion, relaxing
  process identity/containment checks, or changes to retention on unknown custody.
- Resilience tests: reuse landed CARD-0742 and its separate stress follow-up. Only
  investigate a new exact failure at current base; do not edit resilience budgets.
- Landing cases: retain `LandingRemovalPolicyControlTests.C448_V36_EachAuthorityCoordinatePrecedesMutation`
  and `ControlledLandingGitTests.C688_UnregisterDropsHeadFilesWithoutTouchingSetAsideOrRecreatedPath(False)`
  as separate Windows investigations. Measure exact failed argument, native error,
  file identity and handle owner at failure before proposing a cleanup change.
- The old global-registration-lock hypothesis is superseded in source. To reopen
  it, measure current `TempDir` registration/sweep durations, per-root lock attempts,
  index entry counts and stacks at the failed phase on the tested SHA; historical
  JSONL size and root count cannot establish it.

## Handoff and publication

Land this documentation-only task through the caller's normal landing flow, then
commission TestDesign against this artifact. This delegate commits and pushes only
`feat/card-task-6da81ee9`; it must not push master or land its own running worktree.
TestDesign should preserve D-1 through D-9, finish the guard/PC mapping and bounded
OS-specific manifest, and return `next: code` only once the verification section is
complete. Implementation then proceeds Code -> ordinary Review -> land -> separately
commissioned SourceLanding Mutation, with CARD-0828 overlap reconciled by the caller.
