# CARD-1716 survivor witnesses

Original Code/landing owner: c1e2a660. Base: 9c13c6d42bab1281e409dcb24c09cae85a943a65.
Test-only repair of W5/W7/W8 from Mutation a4933b4f. One slice S1 changes only
`tests/Antiphon.Tests/Application/ChannelInboundRecoveryTests.C1716.cs` and this plan.
No production, project graph, timeout or existing assertion changes.

## Verification design

### Inspection

The production bridge publishes a summary after every pass, selects a five-second
busy retry versus ten-second idle wait, and starts an independent minute stall
checker in ExecuteAsync. Existing tests directly call the selector/checker and
therefore miss the hosted-loop wiring. Use the real bridge with the existing
isolated database/harness and a timer-observing FakeTimeProvider wrapper. Gate
the actual grouped inbound scan with a DbCommandInterceptor; stall checks use a
separate non-grouped query and remain free to run. All held work is released and
the hosted service stopped/joined in finally.

### Ordinary verification

| ID | Test / selection | Required observable outcome |
|---|---|---|
| V-1 | `ChannelInboundRecoveryTests.C1716_BusyPass_PublishesHeartbeatAndPreventsPrematureStall` | An old pending row remains unowned while its session lock is held; LastPass contains the completed busy pass time and counts 1 seen, 0 queued, 1 busy; the heartbeat prevents a startup-age false incident. |
| V-2 | `ChannelInboundRecoveryTests.C1716_HostedWorker_RetriesBusyAtFiveSecondsThenUsesIdleTick` | Hosted service queues the healthy recipient during the busy pass; actual timer is five seconds; skipped row remains unowned at 4.999 seconds, gains ownership at five; the next actual timer is ten seconds and next pass starts at that boundary. |
| V-3 | `ChannelInboundRecoveryTests.C1716_HostedService_StartsIndependentStallCheckerAndRearmsAfterRecovery` | Hosted service is held before its first pass; scheduled checker raises one critical ChannelWakeWorkerStalled incident, remains deduplicated, and raises a second after a completed pass and a new gated stall. No direct call to the stall helper. |
| R-1 | Full `ChannelInboundRecoveryTests*` | All inbound recovery behavior remains green. |
| R-2 | Full `ChannelBridge*` and `ChannelInputWithheldIncidentTests*` | Bridge and withheld incident neighbors remain green. |
| R-3 | Full `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*` | Existing classification and slow registry guard contracts remain green. |

### Guard inventory and Mutation handoff

| PC | Variant | Exact detecting method | Expected red |
|---|---|---|---|
| PC-1 | W5: suppress PassCompleted for busy passes | `/*/*/ChannelInboundRecoveryTests/C1716_BusyPass_PublishesHeartbeatAndPreventsPrematureStall` | busy-heartbeat publication assertion |
| PC-2 | W7: replace NextWakeDelay(pass) with WakeIdleTick in RunWakeWorkerAsync | `/*/*/ChannelInboundRecoveryTests/C1716_HostedWorker_RetriesBusyAtFiveSecondsThenUsesIdleTick` | busy-loop-delay actual timer 10 versus 5 seconds |
| PC-3 | W8: remove stall-check startup in ExecuteAsync, substituting a cancellation-bound never-completing task | `/*/*/ChannelInboundRecoveryTests/C1716_HostedService_StartsIndependentStallCheckerAndRearmsAfterRecovery` | no scheduled minute timer/incident while wake pass is held |

Every PC/variant remains pending for method-scoped SourceLanding Mutation;
the Code stage does not discharge deliberate mutant red/restore/green cycles.

### Out of scope

No full assembly. The brief explicitly waives whole Unit (CARD-1720 owns that
defect); only the named full integration classes and guard classes are required.
No production startup/OS branches or optional configuration inputs change.
Restart impact: none; no server/runner restart or native Windows work required.

### Cost

Three ordinary rows share one isolated build; one leased bootstrap builds the
checkpoint tool separately because no executable exists in this fresh worktree.
Provisional execution estimates: cold build 5-10 min, recovery 2-4 min, adjacent
classes 2-4 min, guards 1-3 min; expected admission 5-10 min per lease on the
loaded host. No repetition required after green. No full-assembly/unbounded
selection is authorized. These are not measured p95 values. Historical recovery
pack execution was 101-135 seconds in Mutation a4933b4f. New waits retain the
existing 8-second pass/poll diagnostic and 10-second timer/gate observation
bounds; teardown joins retain 30 seconds. Existing assertions/timeouts stay intact.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1716-witness/` | recovery | `/*/*/ChannelInboundRecoveryTests*/*` | V-1, V-2, V-3, R-1 | all listed, 0 failed | 48 | 12 | true | `TEMP=/tmp` |
| CP-2 | S1 | CP-1 | bridge-adjacent | `/*/*/(ChannelBridge*)\|(ChannelInputWithheldIncidentTests*)/*` | R-2 | all listed, 0 failed | 2 | 4 | true | `TEMP=/tmp` |
| CP-3 | S1 | CP-1 | classification-guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | R-3 | all listed, 0 failed | 4 | 3 | true | `TEMP=/tmp` |

## Performance budget

One shared process fixture per row, isolated per-test databases, existing cached
Postgres/schema lifetime. Retain fresh TRX per-test membership/durations and tool
build/row/slot timings in ignored `.antiphon/checkpoints/`; unavailable setup/IO
phases remain unknown. The tool's derived total execution allowance is 48 min
(2 * 19 + 10), separately bounded from admission; row defaults are provisional
under CARD-1404/R5, not p95-qualified. Stop and investigate any actual selection
exceeding 30 minutes. Expected authoring plus execution fits the brief's 60-minute
window only if shared-host admission permits. Record any receipt gap explicitly.
