# rc run group-81: CapacityObsoleteStandingTests CAS race (task 9478d178)

Status: **root cause confirmed. It is a test-arrangement race (a flake), not a product defect.**
Base: master `2b342761f22f36f5624f054f9928820447e0df50`. The failing rc cut is `f32aed42e` (rc/2026-11-04).

## Symptom

Windows Full run `0b05506b` failed one test in group 81 (447 tests, 446 passed) on DESKTOP-KTLKPIF at 2026-11-04T01:16Z:

`CapacityObsoleteStandingTests.C1603_obsolete_cleanup_loses_compare_and_set_safely`, with
`after.State should be CapacityRecoveryWaitState.Progressed but was CapacityRecoveryWaitState.ActionPending`
at `tests/Antiphon.Tests/Application/CapacityObsoleteStandingTests.cs:330`.

## Mechanism

1. The test calls `Control.StartAsync(...)` (`CapacityObsoleteStandingTests.cs:313`). It then sets the
   `capacity-obsolete-before-finish` boundary and calls `Recovery.ReconcileAsync` straight away (`:328`).
   It never calls `Harness.LaunchQueue.WaitForIdleAsync`.
2. `StartAsync` returns once the replacement session row is committed. The process launch keeps running in the
   background on `AgentSessionLaunchQueue` (`server/Application/Services/AgentSessionLaunchQueue.cs:169-198`,
   `Task.Run`). That launch writes the replacement `AgentSessions` row several times
   (`AgentSessionService.cs` `LaunchInteractiveProcessAsync`, the `SaveChangesAsync` calls at about :626 and :700).
   Each write holds the row lock for a short time.
3. The standing cleanup takes `FOR UPDATE NOWAIT` on the session row, then on the agent row, then
   `pg_try_advisory_xact_lock` on the provider (`CapacityRecoveryService.ObsoleteConsumers.cs:275-316`). If any
   of these is busy, it returns `CleanupOutcome.Busy` and rolls back. It does this *before* the
   `capacity-obsolete-before-finish` boundary at `:336-338`. The test's injected `Progressed` write sits behind
   that boundary, so it never runs. The wait stays at its granted `ActionPending` state and keeps its version.
   This is exactly the CI assertion.
4. The sibling witness `C1603_standing_cleanup_serializes_with_acceptance` hit the same race under CPU load
   (CARD-1603 D-1). Commit `98fc3ceb6` fixed it by adding `LaunchQueue.WaitForIdleAsync` before its reconcile
   (`CapacityObsoleteStandingTests.cs:196-200`), with the comment: "Reconciling in that window is the busy
   deferral: session NOWAIT fails". That commit is in the rc cut. The same wait was never added to
   `C1603_obsolete_cleanup_loses_compare_and_set_safely`.

## Evidence (server2, Linux, one build lease, repeat build `AntiphonCheckpointRepeat=20`)

| Run | Selection | Result |
|---|---|---|
| method x20 alone | `C1603_obsolete_cleanup_loses_compare_and_set_safely` | 20/20 passed, 57 s |
| class x20, chunk A | obsolete + serializes(4) + bounded(2) | 140/140 passed, 2m25s |
| class x20, chunk B | human_start(4) + keeps(7) | 220/220 passed, 2m49s |
| probe: launch lock window x20 (temporary, reverted) | measured immediately after `StartAsync` | launch still pending when Start returned in 19/20; launch settled 60-1470 ms later; side-connection `FOR UPDATE NOWAIT` on the replacement session was busy in 18/20 samples (1-2 hits of 44-430 tries, within 56-341 ms of Start, plus one cold sample at 2.9 s); agent and provider never busy |
| probe: held lock during Reconcile x20 per arm (temporary, reverted) | session row (`UPDATE` no-op), agent row, provider advisory lock | 60/60 reproduce the CI signature: candidate discovered, `before-finish` never reached, `state=ActionPending`, `version=2` (unchanged) |

These are the exact probe output lines (sorted, counted) from the scratch file during the run:

```
20 held=session  discovered=True finishReached=False state=ActionPending version=2 beforeVersion=2
20 held=provider discovered=True finishReached=False state=ActionPending version=2 beforeVersion=2
20 held=agent    discovered=True finishReached=False state=ActionPending version=2 beforeVersion=2
```

The race is real but its window is narrow: brief session-row writes in a launch of roughly 60-250 ms. That
matches 20/20 green alone and 360/360 in the class on an idle server2. Windows group 81 runs 447 tests with
neighbours in parallel, which stretches both the launch and the reconcile prelude. That is the load pattern
CARD-1603 D-1 already recorded for the sibling test.

## Product defect?

No. `Busy` is the designed deferral. It leaves the wait, its version and the grant unchanged for the next
reconcile pass. The supervisor tick runs that pass, and it supersedes the wait once the launch's row writes are
done. The comment in `C1603_human_start_supersedes_standing_wait` documents this explicitly: "A busy try-lock
leaves the granted wait ActionPending; reconcile again until the supersede commits". That test retries up to 5
times. In a real run, a recovery wait is held at most one tick longer while a replacement launch is still writing
its session row. It is not left stuck.

## Drift since the cut

`git diff f32aed42e 2b342761f` shows no changes to `CapacityObsoleteStandingTests.cs`,
`CapacityRecoveryService*.cs`, `AgentControlService.cs`, `AgentSessionService*.cs`,
`AgentSessionLaunchQueue.cs`, `StandingSessionOwnership.cs` or `AgentSupervisionTests.cs`. Master has the same
latent race.

## Remaining uncertainty

- The desktop TRX and logs are not readable from server2. The specific lock the Windows run lost (session,
  agent or provider) is inferred, not observed. All three give the same signature. Only the session row was ever
  busy in the measured launch window.
- The run was not reproduced naturally on server2 (0 natural failures: 40 executions of the method, 380 executions in total). The CI failure is reconstructed from
  the measured window plus the deterministic held-lock reproduction.
- `C1603_standing_cleanup_is_bounded_and_provider_busy_safe` (`:255` start loop, reconciles at `:266` and `:290`) also reconciles right after its third
  `StartAsync` without waiting for the launch queue. It carries the same latent hazard: a `Busy` there would lower
  the first superseded count from 2.

## Not done, noted

- Fix (test-only, for a Code task): add `await fx.Harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);` after `StartAsync` in `C1603_obsolete_cleanup_loses_compare_and_set_safely` (and in `..._is_bounded_...` after the start loop). Keep every assertion.
- Rerun: a rerun-only chunk of group 81 (or just this method) is enough to clear the rc gate. The product code is unchanged.
