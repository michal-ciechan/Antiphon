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

## Verification design

TestDesign baseline: `9a0e821947f33b49577ecc4571fb497a0c9562dc`.
This append supplies the verification contract; D-1 through D-9 and S1 through S4
above remain the fix design. No build, implementation test or Windows qualification
was run in TestDesign. Method additions below are explicit Code deliverables, not
claims about methods already present.

### Inspection

Bodies read, including their setup, cleanup and assertions:

- `CheckpointTaskOwnershipTests.cs`: the four repaired methods, `Runtime`,
  `OwnerHandler`, `BoundarySlots`, `GatedUncertaintyClock`, `OwnerLineSink`
  and the adjacent settlement/owner-read cases; `CheckpointTestSupport.cs`:
  `FakeDriver`, `ScriptedHttpHandler`, `CheckpointFixtures.MarkRun`,
  `CreateRun` inputs and `WaitUntil` | running/pending, cooperative/late grant,
  uncertainty/settlement, abort, driver-exit/release ordering -> V-1, R-1, R-6.
- Entire `CheckpointTestScope.cs` (including `CheckpointTestBase`),
  `CheckpointTempScopeTests.cs`, and the linked-file entries in
  `Antiphon.Checkpoints.LifecycleHost.csproj` | pending/success/faulted work,
  two roots, canceled test/fresh cleanup token -> V-2, R-2. A new harness must
  not make the linked scope depend on an Antiphon.Tests-only type.
- Entire `BuildSlotClientTests.cs`, the diagnostic method and local helpers in
  `CheckpointSlotContractTests.cs`; `BuildSlotClient` constructor,
  acquisition, renewal and disposal bodies | positive/null interval, busy
  acquisition/renewal, cancel-before-next-delay/cancel-in-HTTP, two diagnostic
  leases and all four status/body assertions -> V-3, R-3.
- Entire `EvidenceFolderTests.cs`, `CheckpointToolCopyCleanupTests.cs`,
  `ToolCopyCleanup.Remove`, `ObserveExecutor` and `ContainedCleanup` |
  real Windows sharing failure, portable wrapper, failed receipt/later success
  and nested live/missing custody -> V-4, R-4, R-5.
- Entire `CheckpointTempRootSweep.cs`, `CheckpointTempRootSweepTests.cs`,
  `TestRootGuard.cs` | preflight Dead/AliveSame/Unknown/ReusedPid; grace one
  tick before/exactly at boundary; lock unavailable; state changed between
  reads; partial deletion/resumption -> V-5, R-5.
- `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`
  and `UsageLibrary` in `CheckpointTempUsageTests.cs`;
  `Get-NamespaceCensus` and OS skip matching in
  `scripts/lib/checkpoint-usage.ps1` | independent count 382 at this baseline,
  new argument rows and Windows-only skip -> V-6.
- `TaskOwnerGuard.EnsureLiveAsync/WatchAsync/End`,
  `CheckpointApp.ExecuteAsync/CreateRun`, scheduler admission and
  `RunRowAsync`, command branch in `RowRunner.RunAsync`, `RowTimeout` |
  real in-process consumers and state/report publication -> V-1, R-1;
  native process-tree termination is unchanged and excluded.
- `ControlledTimeProvider.cs`, adjacent `ScaledTimeProviderTests` timer and
  cancellation bodies, `TimeoutTests.windows_quick_row_finishes_beside_a_slow_row`,
  `CheckpointRepeatBuildIsolationTests`, importer and CLI repeat/build-binding
  bodies | nearest setup for new `CheckpointTimingHarness.cs` /
  `CheckpointTimingHarnessTests.cs`, direct-invocation cancellation,
  timer registration, repeated result floors -> V-2, R-6, V-7.
- Owners read: `docs/project-context.md`; testing/build sections for manifests,
  repeat proof, slots, temp custody, delivery, coverage and Mutation; orchestration
  stage/landing contract. No changes to their production policy are proposed.

**Missing setup to deliver in the existing slices.** S1 needs the small shared
timing harness, a cancellable one-request-at-a-time scheduler, a scope cleanup
`TimeProvider` option (System default; ten seconds unchanged), synchronized
handler status/counters, an execution owner and named phase receipts. S2 needs
the renewal-only delegate, a second-delay cancellation receipt and a held HTTP
drain acknowledgement. S4 needs a scripted `ProcessIdentityProbe` that can
perform real registration or change a marker during its first observation.
The probe captures registration exceptions for assertions; it must not throw
fixture errors instead of reaching those assertions. These are fixture seams
within S1/S2/S4, with default production behavior preserved.

For live/uncertain preflight tests, attempt registration at every owner probe,
using a distinct nested run path each time, and retain all captured exceptions.
Require every attempt to succeed. This catches a mutant that performs a harmless
preflight registration, then probes again while holding the root lock. The
abort test similarly exposes an acknowledged disposal-entry boundary after its
cancel call, holds the execution-exit gate for its join assertion, and exercises
scope disposal before owner disposal in its separate registration subcase.

In both uncertainty cases, after the unverified sink receipt also acknowledge
the next owner-delay registration. That proves End has returned before asserting
the running driver's cancellation receipt is absent; only then release the row
or change owner status. Drive the default 180-second uncertainty budget in the
first case and the existing six-second override in the late-settlement case,
in three-second virtual steps from the first failed read. Diagnostic callbacks
signal after appending a line identified by operation/lease, never by its expected
status or body: corrupt diagnostic content must reach the content assertion.

**Bounded waiting contract (D-2).** Use one whole-fixture wall-clock envelope:
the existing five-minute row bound supplies 290 seconds for test work plus the
existing ten-second cleanup bound. Start that work deadline once on entry;
all phases consume its remaining time. This is an enclosing hang watchdog, not
a replacement 3/5-second semantic success threshold or a new per-phase budget.
Use System `TimeProvider` by default, a finite deadline CTS, and link the TUnit
method's incoming `CancellationToken`. Direct invocation uses the same deadline
even outside the checkpoint tool. Do not depend on TUnit supplying a token that
will eventually be canceled. Preserve the outer five-minute row/ten-minute run caps.

A phase wait races its actual receipt, early execution failure and the linked
deadline. An early execution fault is rethrown with its cause. Missing receipt at
cancellation/deadline reaches an assertion with the literal phase name, method,
run/row identity, elapsed/remaining budget and last acknowledged phase, e.g.
`phase=release-entered condition never completed`; a bare TimeoutException is
insufficient. Receipt success is checked after the race, never inferred from
`WhenAny`. Capture the failure, then cancel/release/join in `finally` using the
fresh cleanup token, preserving both primary and cleanup failures. Artificial
held gates are all released on abort. An incomplete join retains every root.
No fixture task is abandoned merely because its cleanup deadline expired.

Use `ControlledTimeProvider` only for watchdog regression tests: observe timer
creation synchronously through Events (never await a timer missing in a mutant),
advance to one tick before 290 seconds, then the final tick; assert
the token synchronously before awaiting its result. Start a second phase before
expiry and prove it creates zero additional deadline timers (compare event counts
immediately before/after that phase), then reaches the original deadline. Do not
assert a total timer count first: the no-timer PC must reach its specific deadline
assertion. This detects removal/reset of the timer without hanging a test. Test
linked incoming cancellation separately.
The synthetic incomplete-work test advances the unchanged ten-second cleanup
clock and releases/joins its held task in its own final cleanup. A completed
faulted task must report its fault while allowing root deletion.

The shared `missing_phase_reports_condition_and_joins_work` test has exactly
16 literal string argument cases, one for each phase in the wait guard table.
For each, first prove a completed receipt succeeds, then hold the same receipt,
advance the registered deadline, capture the assertion, and require its exact
phase diagnostic and the completed cleanup join. Its independent rescue finally
releases all gates even when the guard is mutated. The repaired methods must use
these named waits; Review checks every call site. This helper proof cannot by
itself establish that a real producer runs: the original methods in V-1/V-3
supply that separate proof. No additional unbounded await is permitted in a
new helper; cleanup joins use the fresh ten-second token.

**Overlap sequencing.** CARD-0820 S1 goes first and owns
`CheckpointTaskOwnershipTests.cs`, `CheckpointTestScope.cs`,
`CheckpointTempScopeTests.cs`, the new timing harness/tests and their census
delta. The caller must serialize CARD-0828 against those paths before dispatching
Code. CARD-0828 then consumes the landed S1 SHA, abort/join and retained-root
receipts; it does not author a second clock or lifetime helper. If CARD-0828 has
already landed an equivalent fix when Code starts, record its SHA and acceptance
methods, reuse it, and change the verification mapping before execution. This is
a sequencing precondition, not a request to rebase this TestDesign branch.

### Delivery inventory

There is no new or changed durable message, queue, caller-note or session-input
delivery path. A real-queue busy/eligible recipient and crash/enqueue-failure
battery is therefore excluded: none of these slices produces a queue item.
Do not represent HTTP requests, log events or acknowledgement gates as delivered
session input. Were that scope to change, return to Plan for producer-to-recipient
queue tests and matching complete UserPrompt transcripts.

The changed asynchronous test chains still require consumer evidence:

| Chain / durable join | Producer -> destination | Persistence boundary and recovery | Observable receipt / limits |
|---|---|---|---|
| Owner/execution: run directory + run ID + CP-1/CP-2 + owner task/session IDs | Controlled owner HTTP -> real TaskOwnerGuard -> real scheduler -> fake driver and slot lease | Real state.json and report.md/report.json; abort independently cancels and joins before scope deletion. Incomplete registered work keeps marked roots/index entries. | V-1 reads final persisted row states/exit/reason and report after execution, and proves cancellation, zero late launches and release count. A fake driver cannot prove an OS process exited; no native lifecycle change is claimed. |
| Renewal: lease ID L-renew/L/L-drain + operation + label | Single renewal permit -> real BuildSlotClient -> scripted handler; diagnostic -> real callback queue | No durable renewal queue; lease lifetime owns cancellation/drain/DELETE. Every acquisition is disposed on assertion failure, with held HTTP drain released by the fixture owner. | V-3 requires matching handler consumption, diagnostic text appended before signalling, ordered drained/delete receipt and completed disposal. Fake HTTP cannot prove broker persistence, network recovery or host capacity. |
| Root custody: RootId + AttemptId + root path + nested run path | Fixture owner / sweep -> real TestRootGuard.RegisterRun and filesystem | Marker, per-root index, RunsName and nested custody journal; partial deletion retains marker and later sweep repeats all locked checks. Failed delete remains retryable. | V-2/V-5 inspect payload, marker/index and exact registered run journal; V-4 requires native deletion outcome and absence of tool directory. A callback alone is never the deletion receipt. |

No crash recovery at an external delivery handoff is changed. Existing cleanup
retry/partial-resumption boundaries are covered by R-4/R-5. Safety-critical
lifetime, drain and receipt guards all have PCs below.

### Proves it works now

- V-1: Ownership phases complete without phase sleeps | in-process executor |
  the four historical `CheckpointTaskOwnershipTests` methods selected by CP-1,
  plus `aborted_phase_joins_execution_before_root_teardown` | uncertainty lets
  CP-1 finish green, CP-2 stays owner-unverified/7; later Canceled ends CP-1/7;
  cooperative and deliberately late slot arms launch zero drivers; late lease
  releases once; state remains nonterminal at both acknowledged barriers, then
  terminal state/report and exit 7 agree. Abort test has separate cancellation,
  held-join and registered-work subcases and preserves the forced assertion.
- V-2: Test lifetime and watchdogs are observable | fixture unit |
  `CheckpointTempScopeTests.unfinished_registered_work_retains_roots`,
  `completed_faulted_work_reports_failure_and_deletes_roots`, and all six
  explicitly selected `CheckpointTimingHarnessTests` methods in CP-3 |
  incomplete work retains two roots, completed fault reports failure and deletes,
  each delay consumes one permit, cancellation does not advance time, and missing
  conditions produce named diagnostics with joined cleanup.
- V-3: Renewals are explicitly driven | client unit |
  `BuildSlotClientTests.renewable_grant_is_renewed_until_the_checkpoint_releases_it`,
  `renewal_delay_is_separate_from_acquisition_delay` and
  `CheckpointSlotContractTests.renew_and_release_diagnostics_keep_status_and_body`
  | exactly one granted step renews the matching lease; next pending delay cancels;
  DELETE follows completed renewal/drain; actual appended renew and release
  diagnostics preserve 404 and their distinct bodies.
- V-4: Windows held-file retry | native Windows filesystem unit |
  `EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open`
  | first callback leaves FileShare.Delete denied, second disposes stream;
  outcome Removed, exactly two callbacks and absent tool directory. Explicit
  non-Windows Skip belongs in the independent skip roster. Portable wrapper
  `non_image_remove_drops_the_tool_copy` remains green.
- V-5: Live/young/uncertain roots avoid contention while dead candidates stay
  guarded | sandbox filesystem unit | the five new method names in CP-10 |
  real RegisterRun succeeds during live/uncertain probe, with durable run journal;
  young skip creates no root lock; holding a dead root lock retains payload.
  `eligibility_is_rechecked_after_the_root_lock` has six argument cases:
  live, unknown, reused, grace, marker, inventory. Each changes the observation
  after a valid Dead preflight and retains marker/payload under the locked check.
- V-6: Case and skip inventory remains accurate | compiled roster + PowerShell |
  `CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`
  after every slice that adds cases, including S2 | exact compiled count and all
  skip patterns match. Expected baseline delta is S1 +24, S2 +1, S3 +0,
  S4 +11, giving 418 cases if no intervening change; derive again at Code base.
- V-7: Final Windows qualification | Windows Unit, real held-file case |
  CP-13 through CP-22, exactly two executions per selected case at the final
  candidate SHA | every ordinal green, zero skipped, all seven reported methods
  each executed twice, scoped guard roster also green. Record machine load;
  this does not assert a reproduction of the old 32-class lane.

### Guards the regression

- R-1: No permanent-open uncertainty clock or abandoned execution |
  `single_step_delay_never_opens_future_requests`,
  `single_step_delay_cancellation_does_not_advance_time`,
  `phase_wait_surfaces_early_execution_failure` and
  `aborted_phase_joins_execution_before_root_teardown` |
  second request stays pending, canceled delay leaves clock unchanged, executor
  fault is preserved, cancellation/join/registration assertions all hold.
- R-2: Teardown neither hides a fault nor deletes beneath unfinished work |
  the four CP-2 methods | two-root preservation for incomplete work; completed
  registered work joined before removal; original/delete failures remain visible
  and other safe roots are attempted.
- R-3: Renewal seam cannot change acquisition or null-interval semantics |
  `busy_waits_then_grants`, `release_on_dispose`,
  `pid_liveness_grant_with_null_renewal_is_granted_without_renewing`,
  `null_renewal_interval_is_granted_without_renewal` and
  `renewal_delay_is_separate_from_acquisition_delay` |
  busy acquisition advances only acquisition delay; null interval creates no
  renewal request; disposal releases matching lease. Separate-clock fixture
  caps the synthetic HTTP handler after two renews so a wrong immediate delay
  fails a count assertion rather than spinning forever.
- R-4: Failure receipt remains truthful and retryable |
  `CheckpointToolCopyCleanupTests.failed_delete_receipt_is_truthful_and_retryable`
  | first failure retains tool; next normal call returns Removed and removes it.
- R-5: Preflight cannot authorize deletion |
  `grace_is_additional_to_dead_ownership`,
  `resumed_deletion_rechecks_every_veto`, `marker_survives_partial_deletion`,
  `nested_live_executor_vetoes_whole_root`,
  `unknown_nested_custody_vetoes_whole_root` plus V-5 |
  grace -1 tick retains/exact boundary deletes only dead owner, partial marker
  survives, changed owner/nested veto prevents any further payload mutation.
  Add explicit non-null marker assertion before dereferencing in the partial test.
- R-6: Missing producers cannot silently pass or hang direct invocation |
  `phase_deadline_is_finite_and_shared`,
  `phase_deadline_links_test_cancellation`,
  `missing_phase_reports_condition_and_joins_work` (16 argument cases) |
  finite shared timer/cancellation and exact per-phase diagnostics, with every
  held task joined independently. Synchronous S3/S4 callbacks introduce no waits.

### Guard inventory

Every listed safety guard maps to one distinct PC. Multiple mutations for the
same verdict predicate are named variants of that PC. G-10 requires removing
both redundant admission vetoes to demonstrate an admitted pending row; either
alone still prevents that unsafe launch. Other independently bypassable guards
are split. No new external delivery guard exists.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | S1: fixture abort cancels its execution independently of owner settlement | PC-1 |
| G-2 | S1: fixture abort joins before root teardown | PC-2 |
| G-3 | S1: every started execution is immediately registered with its scope | PC-3 |
| G-4 | S1: incomplete registered work vetoes deletion of every owned root | PC-4 |
| G-5 | S1: completed task faults remain teardown failures | PC-5 |
| G-6 | S1: one clock permit completes only one delay request | PC-6 |
| G-7 | S1: canceling a requested delay does not advance virtual time | PC-7 |
| G-8 | S1: an early executor fault is surfaced ahead of an absent phase | PC-8 |
| G-9 | S1: owner uncertainty does not cancel an already running row | PC-9 |
| G-10 | S1: owner uncertainty does not admit pending work | PC-10 |
| G-11 | S1: late definitive settlement cancels surviving work | PC-11 |
| G-12 | S1: owner loss cancels an outstanding cooperative slot acquisition | PC-12 |
| G-13 | S1: a lease arriving after cancellation cannot launch a driver | PC-13 |
| G-14 | S1: a rejected late lease is disposed exactly once | PC-14 |
| G-15 | S1: terminal state cannot precede driver exit | PC-15 |
| G-16 | S1: terminal state cannot precede lease release completion | PC-16 |
| G-17 | S2: one released renewal step invokes the renewal request | PC-17 |
| G-18 | S2/D-4: renewal scheduling is independent of acquisition’s immediate fake delay | PC-18 |
| G-19 | S2: lease disposal cancels the next renewal delay | PC-19 |
| G-20 | S2: renewal is drained before DELETE | PC-20 |
| G-21 | S2: renew diagnostic retains HTTP status | PC-21 |
| G-22 | S2: renew diagnostic retains response detail | PC-22 |
| G-23 | S2: release diagnostic retains HTTP status | PC-23 |
| G-24 | S2: release diagnostic retains response detail | PC-24 |
| G-25 | S2: null renewal interval does not create a renewal worker | PC-25 |
| G-26 | S3/D-5: native sharing failure is retried before successful removal | PC-26 |
| G-27 | S3: a failed delete cannot claim successful cleanup | PC-27 |
| G-28 | S4/D-6: live owners are excluded before taking the root lock | PC-28 |
| G-29 | S4/D-6: young roots are excluded before taking the root lock | PC-29 |
| G-30 | S4/D-6: uncertain owners are excluded before taking the root lock | PC-30 |
| G-31 | S4/D-6: a dead candidate still requires the root lock | PC-31 |
| G-32 | S4/D-6: owner liveness is freshly checked under the root lock | PC-32 |
| G-33 | S4/D-6: grace is freshly checked under the root lock | PC-33 |
| G-34 | S4/D-6: marker identity is freshly validated under the root lock | PC-34 |
| G-35 | S4/D-6: a newly incomplete inventory vetoes deletion under the lock | PC-35 |
| G-36 | S4/D-6: live nested executors veto whole-root deletion | PC-36 |
| G-37 | S4/D-6: missing nested custody vetoes whole-root deletion | PC-37 |
| G-38 | S4: partial deletion retains the root’s durable marker for revalidation | PC-38 |
| G-39 | S1/D-2: the whole-fixture deadline is finite | PC-39 |
| G-40 | S1/D-2: incoming test cancellation reaches all phase waits | PC-40 |
| G-41 | S1-S4: checkpoint case census remains independent and accurate | PC-41 |
| G-42 | S1/S2/R-6: missing owner-delay receipt fails at its named phase (ownership uncertainty and settlement scenarios; each requested clock step) | PC-42 |
| G-43 | S1/S2/R-6: missing driver-entered receipt fails at its named phase (all three running-driver ownership scenarios and abort scenario) | PC-43 |
| G-44 | S1/S2/R-6: missing slot-entered receipt fails at its named phase (both slot-loss branches) | PC-44 |
| G-45 | S1/S2/R-6: missing slot-canceled receipt fails at its named phase (cooperative slot-loss branch) | PC-45 |
| G-46 | S1/S2/R-6: missing owner-unverified receipt fails at its named phase (both uncertainty scenarios; actual OwnerLineSink append) | PC-46 |
| G-47 | S1/S2/R-6: missing driver-canceled receipt fails at its named phase (late settlement, terminal publication and abort) | PC-47 |
| G-48 | S1/S2/R-6: missing release-entered receipt fails at its named phase (terminal publication after driverExit) | PC-48 |
| G-49 | S1/S2/R-6: missing execution-finished receipt fails at its named phase (all four ownership methods and fixture-owned abort join) | PC-49 |
| G-50 | S1/S2/R-6: missing renew-delay receipt fails at its named phase (both renewal lease acquisitions and separate-clock regression) | PC-50 |
| G-51 | S1/S2/R-6: missing renew-http receipt fails at its named phase (renewable-grant method; handler return after matching request) | PC-51 |
| G-52 | S1/S2/R-6: missing renew-next-delay receipt fails at its named phase (renewable-grant method after first successful renewal) | PC-52 |
| G-53 | S1/S2/R-6: missing renew-stop receipt fails at its named phase (renewable-grant disposal; canceled second delay) | PC-53 |
| G-54 | S1/S2/R-6: missing renew-diagnostic receipt fails at its named phase (diagnostic lease; append callback after queue insertion) | PC-54 |
| G-55 | S1/S2/R-6: missing drain-entered receipt fails at its named phase (second diagnostic lease; request inside GatedRenewHandler) | PC-55 |
| G-56 | S1/S2/R-6: missing drain-canceled receipt fails at its named phase (second diagnostic lease; cancellation acknowledged while drain remains held) | PC-56 |
| G-57 | S1/S2/R-6: missing lease-disposed receipt fails at its named phase (both diagnostic leases and renewable-grant final disposal) | PC-57 |
| G-58 | S1/D-2: a second phase cannot reset the whole-fixture deadline | PC-58 |

### Positive controls

S1 implementation note (Code task `0b309def`): installed Shouldly 4.3's generic
`ThrowAsync<ShouldAssertException>` rethrows the assertion exception, confirmed by
the slot-gated library diagnostic in `.antiphon/c820-s1/probe-assertion.ps1`.
The expected assertion-exception checks below therefore use
`CheckpointTimingAssertions.CaptureAsync`: it catches only `ShouldAssertException`
and requires a non-null captured exception with the listed target label. The
exact phase/message assertions remain required. Successful return or a different
exception still fails; no guarded condition, timeout, PC or variant is relaxed.

Code runs V/R, and ordinary Review judges this design and its implementation before
land. Mutation runs these controls only after the implementation's confirmed land,
from its commissioned SourceLanding snapshot. This is a pending inventory, not
executed PC evidence.

Each row names a compiling defect and an exact detecting method. The target label
is a required assertion message to add where the current method lacks one; existing
outcome assertions remain. Run an unmutated baseline, break, build/run to the
specified assertion red, restore exact bytes, freshly build/run green. Use one
method filter per phase, never a class or suite. For the two- and six-argument
methods and the 16-phase matrix, the method selects all argument cases; verify
the intended failing argument in fresh TRX. A method-prefix trailing star is
permitted only to include that method's argument suffix, as the owner guide
requires. Do not select a literal argument suffix that might execute zero cases.

PC-30 has two independent mutation runs (Unknown, ReusedPid); PC-32 has three
(AliveSame, Unknown, ReusedPid). Every other PC has one mutation run. Thus 58
controls require **61 red/restore/green cycles**. PC-42 through PC-57 each bypass
one named missing-receipt guard in the common wait. Their 16-case negative test
proves a never-produced condition is rejected and the diagnostic fires; the
original producer/consumer scenarios remain separate required V/R.

For PC-11/12/17/19, the companion fixture edit explicitly cancels the same
whole-fixture deadline after the indicated acknowledged operation. It only
accelerates detection of the intentionally missing receipt; it cannot supply
that receipt. Keep independent rescue cancellation/gate release in finally.
The red must be the listed phase assertion, not host timeout or teardown failure.
For early-publication controls, inserting the publication before the relevant
acknowledgement makes the persisted-state assertion causal rather than a
scheduler-dependent snapshot.

| PC | Compiling defect (break the matching G) | Detecting filter | Expected assertion red |
|---|---|---|---|
| PC-1 | remove the fixture owner’s abort.Cancel() call; keep gate release and joining | `/*/*/CheckpointTaskOwnershipTests/aborted_phase_joins_execution_before_root_teardown` | abort-token-observed: driver cancellation receipt is complete |
| PC-2 | replace the fixture owner’s awaited execution join with Task.CompletedTask | `/*/*/CheckpointTaskOwnershipTests/aborted_phase_joins_execution_before_root_teardown` | abort-join-pending: fixture disposal is incomplete while the acknowledged executor-exit gate is held |
| PC-3 | omit RegisterCheckpointWork(execute) in the shared execution owner | `/*/*/CheckpointTaskOwnershipTests/aborted_phase_joins_execution_before_root_teardown` | registered-execution-retained: explicit scope disposal cannot delete either root while execute is held |
| PC-4 | force the new incomplete-work deletion veto false after the cleanup token expires | `/*/*/CheckpointTempScopeTests/unfinished_registered_work_retains_roots` | unfinished-work-roots-retained: both payloads, markers and index entries remain |
| PC-5 | discard the caught registered-work exception from the teardown failure list | `/*/*/CheckpointTempScopeTests/completed_faulted_work_reports_failure_and_deletes_roots` | registered-fault-reported: Should.ThrowAsync<IOException> contains the synthetic fault; both completed-work roots are still deleted |
| PC-6 | reuse the first completed delay gate for the next request | `/*/*/CheckpointTimingHarnessTests/single_step_delay_never_opens_future_requests` | second-delay-pending: second registered delay IsCompleted is false and virtual time advances once |
| PC-7 | advance virtual time in the cancellation/finally path too | `/*/*/CheckpointTimingHarnessTests/single_step_delay_cancellation_does_not_advance_time` | canceled-delay-clock-unchanged: Now equals its value before cancellation |
| PC-8 | ignore the completed execution task in the phase wait and return a generic phase failure | `/*/*/CheckpointTimingHarnessTests/phase_wait_surfaces_early_execution_failure` | early-execution-fault: caught exception retains the original synthetic-executor-fault message |
| PC-9 | in TaskOwnerGuard.End cancel _settled for owner-unverified as well as owner-ended | `/*/*/CheckpointTaskOwnershipTests/uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified` | uncertainty-keeps-running: cancelled.Task.IsCompleted is false after the unverified sink receipt |
| PC-10 | bypass uncertainty admission at both the scheduler AdmissionBlock and EnsureLiveAsync’s owner-unverified return (return true); these redundant vetoes must both be removed to admit CP-2 | `/*/*/CheckpointTaskOwnershipTests/uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified` | pending-owner-unverified: CP-2 state is owner-unverified and exit code is 7 |
| PC-11 | remove End("owner-ended") from TaskOwnerGuard’s Reason == owner-unverified later-read arm; expire the fixture deadline after that controlled read | `/*/*/CheckpointTaskOwnershipTests/late_settlement_after_owner_unverified_cancels_the_running_row` | phase=driver-canceled: the condition-receipt assertion is false, with a condition-specific diagnostic |
| PC-12 | pass CancellationToken.None to the command row’s AcquireAsync; expire the fixture deadline after the owner settlement read | `/*/*/CheckpointTaskOwnershipTests/ownership_loss_cancels_slot_wait_and_rejects_a_late_grant` | phase=slot-canceled: the cooperative arm fails the receipt assertion |
| PC-13 | remove the command branch’s BeforeLaunch call in RowRunner.RunAsync; the fake driver deliberately records entry even for a canceled token | `/*/*/CheckpointTaskOwnershipTests/ownership_loss_cancels_slot_wait_and_rejects_a_late_grant` | late-grant-no-launch: driver.Count is zero in the late-return arm |
| PC-14 | change RunScheduler.RunRowAsync’s await using lease declaration to an ordinary var declaration | `/*/*/CheckpointTaskOwnershipTests/ownership_loss_cancels_slot_wait_and_rejects_a_late_grant` | late-grant-release-once: releases equals 1 after execute finishes |
| PC-15 | in RunRowAsync, before awaiting _rows.RunAsync, set request.State.Phase = "done" and invoke request.Publish?.Invoke() | `/*/*/CheckpointTaskOwnershipTests/terminal_publication_waits_for_driver_exit_and_lease_disposal` | driver-exit-barrier: persisted state Phase is not done while driverExit is held |
| PC-16 | wrap _rows.RunAsync in try/finally and set request.State.Phase = "done"; request.Publish?.Invoke() in that finally, before the existing await-using disposal | `/*/*/CheckpointTaskOwnershipTests/terminal_publication_waits_for_driver_exit_and_lease_disposal` | lease-release-barrier: persisted state Phase is not done after release-entered and before release completion |
| PC-17 | replace the renewal POST with a synthetic NoContent response; expire the fixture deadline after releasing the step | `/*/*/BuildSlotClientTests/renewable_grant_is_renewed_until_the_checkpoint_releases_it` | phase=renew-http: matching POST /L-renew/renew receipt assertion is false |
| PC-18 | assign the new renewal-delay field from delay instead of renewalDelay | `/*/*/BuildSlotClientTests/renewal_delay_is_separate_from_acquisition_delay` | renewal-clock-separated: acquisition delay count stays at the one busy retry and a renewal-delay request is registered |
| PC-19 | omit renewalStop.Cancel(); expire the fixture deadline only after disposal has entered its held join | `/*/*/BuildSlotClientTests/renewable_grant_is_renewed_until_the_checkpoint_releases_it` | phase=renew-stop: pending renewal delay cancellation receipt is absent |
| PC-20 | move ReleaseAsync(leaseId, label, token) before awaiting renewal, leaving its single invocation intact | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | renewal-drained-before-delete: delete count is zero while acknowledged renewal drain is held; final events equal renew-entered, renew-canceled, renew-drained, delete |
| PC-21 | pass status 200 instead of the response status only to Observe("renew", ...) | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | renew-status-preserved: appended renewal line contains status=404 |
| PC-22 | pass an empty body only to Observe("renew", ...) | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | renew-body-preserved: appended renewal line contains renew gone |
| PC-23 | pass status 200 only to Observe("release", ...) | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | release-status-preserved: appended release line contains status=404 |
| PC-24 | pass an empty body only to Observe("release", ...) | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | release-body-preserved: appended release line contains release gone |
| PC-25 | map JSON null renewEverySeconds to 1 in TryGrant; instrument the existing null test with the controlled renewal scheduler | `/*/*/BuildSlotClientTests/pid_liveness_grant_with_null_renewal_is_granted_without_renewing` | null-renewal-no-delay: registered renewal request count is zero after acquisition and disposal |
| PC-26 | replace attempt < 9 with attempt < 0 in ToolCopyCleanup.Remove’s retry catch filter | `/*/*/EvidenceFolderTests/tool_copy_removal_retries_while_a_file_is_still_held_open` | held-file-removed: outcome equals Removed; the real first sharing failure instead returns Failed |
| PC-27 | change the outer deletion exception receipt from Failed to Removed | `/*/*/CheckpointToolCopyCleanupTests/failed_delete_receipt_is_truthful_and_retryable` | failed-delete-truthful: first Outcome equals Failed and tool remains; subsequent ordinary removal succeeds |
| PC-28 | skip the new preflight’s AliveSame exclusion while retaining locked Eligible | `/*/*/CheckpointTempRootSweepTests/live_roots_are_skipped_without_taking_the_root_lock` | live-preflight-registers: the probe-captured exception from real RegisterRun is null and its exact run path is in RunsName |
| PC-29 | skip only the new preflight grace exclusion | `/*/*/CheckpointTempRootSweepTests/young_roots_are_skipped_without_taking_the_root_lock` | young-preflight-no-lock: the young root has no newly created LockName file after sweep |
| PC-30 | skip preflight exclusion for Unknown, then separately for ReusedPid | `/*/*/CheckpointTempRootSweepTests/uncertain_roots_are_skipped_without_taking_the_root_lock` | uncertain-preflight-registers: real RegisterRun succeeds during the matching verdict probe; payload and journal remain |
| PC-31 | remove the rootGate-null refusal and allow Eligible/deletion to proceed under a held foreign fixture gate | `/*/*/CheckpointTempRootSweepTests/root_lock_is_still_required_for_dead_candidates` | root-busy-retains-payload: completed roots is 0 and sentinel remains |
| PC-32 | make Eligible’s owner verdict test accept AliveSame, then Unknown, then ReusedPid in separate variants | `/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock` | locked-owner-retained: payload bytes and marker remain after preflight Dead changes to each protected verdict |
| PC-33 | remove Eligible’s grace test, retaining preflight’s test | `/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock` | locked-grace-retained: root made young by the first owner probe remains unchanged |
| PC-34 | remove only the RootId equality check in the sweep’s ReadMarker; preflight sees the valid marker, then the first owner probe replaces its RootId before the locked read | `/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock` | locked-marker-retained: original payload remains after preflight rewrites RootId to a different valid GUID |
| PC-35 | skip Eligible’s SafeTree check while preserving SafeAncestors | `/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock` | locked-inventory-retained: payload remains after preflight creates more entries than MaxDescendantEntries |
| PC-36 | ignore a nested observation with Verdict AliveSame in Eligible | `/*/*/CheckpointToolCopyCleanupTests/nested_live_executor_vetoes_whole_root` | nested-live-retained: CompletedRoots is 0 and sentinel text remains keep |
| PC-37 | continue instead of returning nested-custody-missing when a run-like directory lacks its journal | `/*/*/CheckpointToolCopyCleanupTests/unknown_nested_custody_vetoes_whole_root` | nested-missing-retained: CompletedRoots is 0 and tool sentinel text remains keep |
| PC-38 | delete the marker before returning incomplete from DeleteBounded’s oversized-file arm | `/*/*/CheckpointTempRootSweepTests/marker_survives_partial_deletion` | partial-marker-retained: TestRootGuard.Read(root) is non-null and State equals deleting after the first sweep |
| PC-39 | construct the deadline CTS with Timeout.InfiniteTimeSpan instead of 290 seconds on the supplied TimeProvider | `/*/*/CheckpointTimingHarnessTests/phase_deadline_is_finite_and_shared` | fixture-deadline-fired: after registered timer + 290 virtual seconds, deadline token IsCancellationRequested is true; second phase has no fresh budget |
| PC-40 | omit the incoming test token from the fixture deadline’s linked CTS | `/*/*/CheckpointTimingHarnessTests/phase_deadline_links_test_cancellation` | test-cancel-linked: canceling the supplied token makes the linked token canceled before any clock advance |
| PC-41 | subtract one from Get-NamespaceCensus.selected | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | existing ShouldBe(names.Length) assertion reports stale compiled checkpoint census |
| PC-42 | for the literal "owner-delay" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-owner-delay: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=owner-delay condition never completed |
| PC-43 | for the literal "driver-entered" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-driver-entered: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=driver-entered condition never completed |
| PC-44 | for the literal "slot-entered" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-slot-entered: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=slot-entered condition never completed |
| PC-45 | for the literal "slot-canceled" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-slot-canceled: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=slot-canceled condition never completed |
| PC-46 | for the literal "owner-unverified" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-owner-unverified: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=owner-unverified condition never completed |
| PC-47 | for the literal "driver-canceled" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-driver-canceled: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=driver-canceled condition never completed |
| PC-48 | for the literal "release-entered" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-release-entered: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=release-entered condition never completed |
| PC-49 | for the literal "execution-finished" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-execution-finished: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=execution-finished condition never completed |
| PC-50 | for the literal "renew-delay" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-renew-delay: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=renew-delay condition never completed |
| PC-51 | for the literal "renew-http" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-renew-http: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=renew-http condition never completed |
| PC-52 | for the literal "renew-next-delay" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-renew-next-delay: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=renew-next-delay condition never completed |
| PC-53 | for the literal "renew-stop" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-renew-stop: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=renew-stop condition never completed |
| PC-54 | for the literal "renew-diagnostic" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-renew-diagnostic: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=renew-diagnostic condition never completed |
| PC-55 | for the literal "drain-entered" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-drain-entered: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=drain-entered condition never completed |
| PC-56 | for the literal "drain-canceled" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-drain-canceled: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=drain-canceled condition never completed |
| PC-57 | for the literal "lease-disposed" case only, bypass the missing-receipt assertion and return success from the common phase wait | `/*/*/CheckpointTimingHarnessTests/missing_phase_reports_condition_and_joins_work` | missing-phase-lease-disposed: Should.ThrowAsync<ShouldAssertException> must occur and its message contains phase=lease-disposed condition never completed |
| PC-58 | create a fresh 290-second deadline CTS on entry to the second phase instead of sharing the fixture deadline | `/*/*/CheckpointTimingHarnessTests/phase_deadline_is_finite_and_shared` | fixture-deadline-shared: entering phase two creates zero additional deadline timers; the second phase expires at the original deadline |

All introduced helper bodies must exist and every listed label must bind to the
named assertion before Code hands off. Static coverage/discovery is not a mutation
verdict. Zero tests, unexpected argument failure, build failure, fixture exception,
or an opaque timeout is not red evidence. Restore even after an invalid control;
record the finding and request a separately commissioned repair rather than
editing the landed snapshot permanently. Native PC-26 requires Windows; commission
this complete Mutation battery with `-Platform Windows`, local inherited children
and the assigned external evidence root. No remote execution of the snapshot.

### Out of scope

- Native child-process termination, real build-broker/network persistence and
  session delivery are unchanged. The in-process fakes cannot prove them.
- Whole Unit/namespace/assembly runs, artificial CPU burners, unlimited repeats,
  knownFlaky entries, retries that erase red and budget increases are excluded.
- No new sweep index/migration, root naming, process identity or no-follow
  algorithm is proposed. Existing path containment, symlink and malformed-index
  exhaustive matrices remain owned by the temp-custody plan; this design covers
  the changed ordering with fresh marker and inventory rejection, real locks,
  dead/live/uncertain identities and nested custody. It does not claim an
  exhaustive adversarial filesystem proof.
- No Cartesian product of every owner status with every root state: unrelated
  owners and filesystem predicates are not composed by these slices. The six
  locked-change arguments plus live/young/unknown/reused preflight and the exact
  grace boundary isolate the independently changed vetoes. Existing settlement
  status equivalence is unchanged; the repaired scenarios use Succeeded/Canceled.
- Resilience and the two historical landing failures remain separate obligations
  from the landed plan; their budgets and production policies are untouched.
- TestDesign does not execute or claim Windows load qualification. The caller must
  land this document through the normal flow and serialize the CARD-0828 overlap.

### Checkpoints

All rows are Unit selections in `tests/Antiphon.Tests`; census invokes its existing
owned PowerShell child. The closed ordinary scope is the union below. CP-1..7 and
CP-10..12 are portable and omit placement pins. CP-8..9 and CP-13..22 require
`-Platform Windows`, no `-Runner` pin. Read current runner defaults/catalogue
before dispatch. A skipped native method is incomplete evidence.

CP-1..12 use repeat 1. CP-13..22 use **`--repeat 2`** at the final committed
candidate SHA, including both native held-file ordinals. In their rows Min is the
per-repetition floor supplied to the driver; Expect states the doubled execution
count. Verify receipts with `--expected-repeat 2`. The repeated output is separate
from every repeat-1 build. There are exactly two Windows qualification executions
per selected case, not two more rounds until green. Cap additional qualification
rounds at zero under this authorization. Failure-driven reruns require the
reported exact failure/base comparison and are not relabeled repeat proof.

Windows load recipe: run on the assigned Windows host under its existing ambient
workload, with one CARD-0820 row at a time, host build slots enabled, no generated
CPU load and no concurrent Antiphon.Agents.Pty.Tests/FakeClaude run. Capture Windows
version, logical cores, available/total memory, CPU percentage and occupied/queued
build slots immediately before and after each group and at row boundaries.
Also record per-phase monotonic timestamps, delay registrations/completions,
handler calls, cancellation/join outcomes, retry count and TRX durations. Report
actual values/ranges in evidence. If the machine is idle, label it idle ambient
qualification; do not claim loaded proof or start extra rounds. Historical M-2.n
load is unavailable in this checkout, so matching it is not an acceptance claim.

Invoke the checkpoint tool through `scripts/build-slot.ps1`, one committed
group per command; the tool also leases its own build/test drivers. Pass the exact
SHA, `--row-timeout 5m --total-timeout 10m --serial` and the listed `--rows`.
The permitted groups and estimates are CP-1..4 (8m), CP-5..7 (7m), CP-8..9 (6m),
CP-10..12 (7m), CP-13..16 (8m), CP-17..18 (6m), CP-19..20 (6m), CP-21..22 (6m).
Each group has its own build output; there is no unsupported build reuse across
separate `run` invocations. Use a tool-only isolated output for the launcher too:

```powershell
$planPath = 'docs/superpowers/plans/2026-10-05-card-0820-windows-unit-timing-plan.md'
$candidateSha = git rev-parse HEAD
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c820-cp1-4 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c820-tool/ -- run --plan $planPath --rows CP-1,CP-2,CP-3,CP-4 --expected-source-sha $candidateSha --row-timeout 5m --total-timeout 10m --serial
```

For subsequent groups substitute their exact row list; Windows final groups also
pass `--repeat 2`. Foreground wait until the executor exits; if a tool call returns
75, continue `wait` (bounded foreground calls) for that run. Never edit beneath
a running group. A row/run cap or slot exit 4 is a reported incomplete/red result,
not authority to widen the cap or run without a slot. Validate SHA/clean/build
binding and preserve unedited CHECKPOINT lines with actual counts. Code/Review
run the full task-range evidence-diff guard. Remove only task-owned alternate
outputs on completion; generated receipts/TRX/logs remain ignored.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c820-s1/` | s1-owner | `/*/*/CheckpointTaskOwnershipTests/(ownership_loss_cancels_slot_wait_and_rejects_a_late_grant*)\|(terminal_publication_waits_for_driver_exit_and_lease_disposal*)\|(uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified*)\|(late_settlement_after_owner_unverified_cancels_the_running_row*)\|(aborted_phase_joins_execution_before_root_teardown*)` | V-1, R-1 | all listed; 5 executed, 0 failed/skipped | 5 | 5 |
| CP-2 | S1 | `CP-1` | s1-scope | `/*/*/CheckpointTempScopeTests/(unfinished_registered_work_retains_roots*)\|(completed_faulted_work_reports_failure_and_deletes_roots*)\|(sealed_scope_awaits_registered_work*)\|(teardown_preserves_failure_and_attempts_other_roots*)` | V-2, R-2 | all listed; 4 executed, 0 failed/skipped | 4 | 1 |
| CP-3 | S1 | `CP-1` | s1-timing | `/*/*/CheckpointTimingHarnessTests/(single_step_delay_never_opens_future_requests*)\|(single_step_delay_cancellation_does_not_advance_time*)\|(phase_wait_surfaces_early_execution_failure*)\|(phase_deadline_is_finite_and_shared*)\|(phase_deadline_links_test_cancellation*)\|(missing_phase_reports_condition_and_joins_work*)` | V-2, R-1, R-6 | all listed; 21 executed, 0 failed/skipped | 21 | 1 |
| CP-4 | S1 | `CP-1` | s1-census | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | V-6 | all listed; 1 executed, 0 failed/skipped | 1 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c820-s2/` | s2-renewal | `/*/*/BuildSlotClientTests/(renewable_grant_is_renewed_until_the_checkpoint_releases_it*)\|(renewal_delay_is_separate_from_acquisition_delay*)\|(pid_liveness_grant_with_null_renewal_is_granted_without_renewing*)\|(null_renewal_interval_is_granted_without_renewal*)\|(release_on_dispose*)\|(busy_waits_then_grants*)` | V-3, R-3 | all listed; 6 executed, 0 failed/skipped | 6 | 5 |
| CP-6 | S2 | `CP-5` | s2-diagnostics | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | V-3 | all listed; 1 executed, 0 failed/skipped | 1 | 1 |
| CP-7 | S2 | `CP-5` | s2-census | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | V-6 | all listed; 1 executed, 0 failed/skipped | 1 | 1 |
| CP-8 | S3 | `tests/Antiphon.Tests -> bin-c820-s3w/` | s3-held-file | `/*/*/EvidenceFolderTests/(tool_copy_removal_retries_while_a_file_is_still_held_open*)\|(non_image_remove_drops_the_tool_copy*)` | V-4 | all listed; 2 executed, 0 failed/skipped | 2 | 5 |
| CP-9 | S3 | `CP-8` | s3-cleanup-census | `/*/*/(CheckpointToolCopyCleanupTests)\|(CheckpointNamespaceCensusUsageTests)/(failed_delete_receipt_is_truthful_and_retryable*)\|(namespace_census_matches_compiled_checkpoint_cases*)` | R-4, V-6 | all listed; 2 executed, 0 failed/skipped | 2 | 1 |
| CP-10 | S4 | `tests/Antiphon.Tests -> bin-c820-s4/` | s4-sweep | `/*/*/CheckpointTempRootSweepTests/(live_roots_are_skipped_without_taking_the_root_lock*)\|(young_roots_are_skipped_without_taking_the_root_lock*)\|(uncertain_roots_are_skipped_without_taking_the_root_lock*)\|(root_lock_is_still_required_for_dead_candidates*)\|(eligibility_is_rechecked_after_the_root_lock*)\|(grace_is_additional_to_dead_ownership*)\|(resumed_deletion_rechecks_every_veto*)\|(marker_survives_partial_deletion*)` | V-5, R-5 | all listed; 14 executed, 0 failed/skipped | 14 | 5 |
| CP-11 | S4 | `CP-10` | s4-nested | `/*/*/CheckpointToolCopyCleanupTests/(nested_live_executor_vetoes_whole_root*)\|(unknown_nested_custody_vetoes_whole_root*)` | R-5 | all listed; 2 executed, 0 failed/skipped | 2 | 1 |
| CP-12 | S4 | `CP-10` | s4-census | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | V-6 | all listed; 1 executed, 0 failed/skipped | 1 | 1 |
| CP-13 | all | `tests/Antiphon.Tests -> bin-c820-win1-r2/` | win-owner-r2 | `/*/*/CheckpointTaskOwnershipTests/(ownership_loss_cancels_slot_wait_and_rejects_a_late_grant*)\|(terminal_publication_waits_for_driver_exit_and_lease_disposal*)\|(uncertainty_lets_the_running_row_finish_and_marks_pending_owner_unverified*)\|(late_settlement_after_owner_unverified_cancels_the_running_row*)\|(aborted_phase_joins_execution_before_root_teardown*)` | V-1, R-1, V-7 | all listed; 10 executed, 0 failed/skipped, repeat 2 | 5 | 5 |
| CP-14 | all | `CP-13` | win-scope-r2 | `/*/*/CheckpointTempScopeTests/(unfinished_registered_work_retains_roots*)\|(completed_faulted_work_reports_failure_and_deletes_roots*)\|(sealed_scope_awaits_registered_work*)\|(teardown_preserves_failure_and_attempts_other_roots*)` | V-2, R-2, V-7 | all listed; 8 executed, 0 failed/skipped, repeat 2 | 4 | 1 |
| CP-15 | all | `CP-13` | win-timing-r2 | `/*/*/CheckpointTimingHarnessTests/(single_step_delay_never_opens_future_requests*)\|(single_step_delay_cancellation_does_not_advance_time*)\|(phase_wait_surfaces_early_execution_failure*)\|(phase_deadline_is_finite_and_shared*)\|(phase_deadline_links_test_cancellation*)\|(missing_phase_reports_condition_and_joins_work*)` | V-2, R-1, R-6, V-7 | all listed; 42 executed, 0 failed/skipped, repeat 2 | 21 | 1 |
| CP-16 | all | `CP-13` | win-census-r2 | `/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases` | V-6, V-7 | all listed; 2 executed, 0 failed/skipped, repeat 2 | 1 | 1 |
| CP-17 | all | `tests/Antiphon.Tests -> bin-c820-win2-r2/` | win-renewal-r2 | `/*/*/BuildSlotClientTests/(renewable_grant_is_renewed_until_the_checkpoint_releases_it*)\|(renewal_delay_is_separate_from_acquisition_delay*)\|(pid_liveness_grant_with_null_renewal_is_granted_without_renewing*)\|(null_renewal_interval_is_granted_without_renewal*)\|(release_on_dispose*)\|(busy_waits_then_grants*)` | V-3, R-3, V-7 | all listed; 12 executed, 0 failed/skipped, repeat 2 | 6 | 5 |
| CP-18 | all | `CP-17` | win-diagnostics-r2 | `/*/*/CheckpointSlotContractTests/renew_and_release_diagnostics_keep_status_and_body` | V-3, V-7 | all listed; 2 executed, 0 failed/skipped, repeat 2 | 1 | 1 |
| CP-19 | all | `tests/Antiphon.Tests -> bin-c820-win3-r2/` | win-held-file-r2 | `/*/*/EvidenceFolderTests/(tool_copy_removal_retries_while_a_file_is_still_held_open*)\|(non_image_remove_drops_the_tool_copy*)` | V-4, V-7 | all listed; 4 executed, 0 failed/skipped, repeat 2 | 2 | 5 |
| CP-20 | all | `CP-19` | win-cleanup-r2 | `/*/*/CheckpointToolCopyCleanupTests/failed_delete_receipt_is_truthful_and_retryable` | R-4, V-7 | all listed; 2 executed, 0 failed/skipped, repeat 2 | 1 | 1 |
| CP-21 | all | `tests/Antiphon.Tests -> bin-c820-win4-r2/` | win-sweep-r2 | `/*/*/CheckpointTempRootSweepTests/(live_roots_are_skipped_without_taking_the_root_lock*)\|(young_roots_are_skipped_without_taking_the_root_lock*)\|(uncertain_roots_are_skipped_without_taking_the_root_lock*)\|(root_lock_is_still_required_for_dead_candidates*)\|(eligibility_is_rechecked_after_the_root_lock*)\|(grace_is_additional_to_dead_ownership*)\|(resumed_deletion_rechecks_every_veto*)\|(marker_survives_partial_deletion*)` | V-5, R-5, V-7 | all listed; 28 executed, 0 failed/skipped, repeat 2 | 14 | 5 |
| CP-22 | all | `CP-21` | win-nested-r2 | `/*/*/CheckpointToolCopyCleanupTests/(nested_live_executor_vetoes_whole_root*)\|(unknown_nested_custody_vetoes_whole_root*)` | R-5, V-7 | all listed; 4 executed, 0 failed/skipped, repeat 2 | 2 | 1 |

### Cost

All numbers are **estimates**, not measurements from this TestDesign task.

- Ordinary V/R floor (Code): **54 minutes**, the sum of CP-1..22
  EstimatedMinutes. S1 filters cost 8m, S2 7m, S3 Windows 6m, S4 7m;
  final Windows groups cost 8 + 6 + 6 + 6 = 26m. This contains eight isolated
  test builds at 4m each (32m) and 22 filter executions at 1m each (22m).
  Expected execution floor: CP-1..12 60 results plus CP-13..22 114 results =
  **174 TUnit results**; assertions, repeated ordinals and minutes are distinct.
- Mutation PC floor: **600 minutes** on Windows. The 28 distinct exact
  detecting filters above each receive one initial clean baseline at 4m
  (3m build + 1m method run): 112m. PC-1..58 contain 61 mutation variants:
  each red build/run 4m plus each fresh restored-green build/run 4m = 488m.
  PC-30 costs 16m for its two red/green cycles; PC-32 costs 24m for three;
  each other PC costs 8m, plus its share of the initial baselines. Parameter
  matrices remain one precise method per run; inspect all argument outcomes.
  Every PC touches the same helper/client/sweep areas, so no speculative
  simultaneous mutation build savings are assumed.
- Setup/launcher/discovery allowance: **8 minutes**, 4m for Code's isolated
  tool launcher/static manifest checks and 4m for Mutation source/receipt
  discovery. Build slots may add measured queue time; do not silently count
  it as assertion runtime or widen deadlines.
- Total execution/setup floor = 8 + 54 + 600 = **662 minutes**. Do not commission
  this as one unbounded run: Code retains four 30–60 minute implementation
  slices plus bounded Windows qualification; Mutation owns the separately
  scheduled method-scoped cycles after land. Code authoring remains the
  landed estimate's 127–172m excluding its original 28m verification, yielding
  181–226m authoring + current ordinary V/R, before the 4m Code setup.
- Build savings: ordinary reuse saves 14 redundant test builds x 4m = **56m**
  versus rebuilding all 22 filters. Sharing the same unchanged initial
  method baseline across matching PCs saves (61 - 28) x 4m = **132m**;
  every mutant and restoration still gets a fresh build. No repeat-round
  savings are claimed beyond the fixed two Windows ordinals, and no full-Unit
  comparison is invented.

Handoff audit: bodies read as recorded above; **guards=58, mapped=58, missing=0,
duplicate PC maps=0**. All 58 PCs have a named compiling defect, exact detecting
method and decisive assertion; 61 variant cycles are scheduled after land.
New method/seam setup is explicitly assigned to S1/S2/S4 and all ordinary
methods are in the closed manifest. This design is ready for Code after document
landing and caller-enforced CARD-0828 serialization; it is not a claim that the
implementation or its pending PCs have passed.
