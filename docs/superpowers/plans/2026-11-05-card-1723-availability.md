# CARD-1723 channel bridge availability

Code/landing owner c5d82758; base 9c13c6d42bab1281e409dcb24c09cae85a943a65.
Scope A-1..A-3 from Review 2e0375f1. A-4 tooling stays outside this product repair.

Invariant: non-host cancellation and ordinary pass failures cannot terminate the wake,
stall-check or withheld-scan sibling and thereby stop the server. Shutdown still joins promptly.
Already-stamped inbounds and stopped sessions cannot spend the 64-head incident page budget.
Three consecutive failed scan passes produce one Critical ChannelReplyLost monitor incident
on the existing RecentCriticalIncident attention surface; success resets the streak.
Failed alert persistence retries with the same incident identity and never escapes the loop.

Implementation: use the established GrokRulesRecoveryHostedService catch/log/continue
pattern, retaining host-token cancellation. Add inbound/session eligibility to the page SQL
before LIMIT; retain the partial index, final eligibility checks, SKIP LOCKED and all deadlines.
No migration/model/project/props changes. Incident reporting uses its own bounded transaction
after the failed scan transaction has unwound. Monitoring state is process-local, like the
existing wake-stall monitor; restarting starts a new failure streak, durable history remains.

## Callers and impact

| Entry | Callers | Required behavior |
|---|---|---|
| RunWakeWorkerAsync | ExecuteAsync | retry local cancellation, keep consuming |
| RunWakeWorkerStallCheckAsync | ExecuteAsync | retry local cancellation, keep monitoring |
| RunWithheldIncidentLoopAsync | ExecuteAsync | retry all pass faults; count/reset/escalate |
| ScanWithheldChannelIncidentsAsync | withheld loop; integration tests | filter before page; no input or queue semaphore |
| RecentCriticalIncident projection | AttentionService.GetAsync | existing incident kind and grouping, unchanged code |

## Performance budget

Prior Review 2e0375f1: cold build 362s, withheld 39s test wall, channels 190s,
guards 90s. Expanded withheld estimate provisional 4m; loop proofs use fake time,
20s real progress bounds and existing 5s shutdown joins. One shared assembly database
template with per-test isolated schemas. Capture checkpoint phases, fresh TRX per-test
durations, admission and execution separately; finer external-call phases unknown.
No p95 claimed. CARD-1404/1514 own measurement gaps; collect this single ordinary run.
The mandatory Unit lane has unknown unbounded classes beyond this feature; no full
assembly run. Unit category is partitioned A-D/E-H/I-L/M-P/Q-T/U-Z, including method tags.
Its invariant is the full pure-code regression contract (delivery/landing/leases/persistence
are outside that lane). Prior censored 535s and CARD-1720 Debug 86a941a3 inform provisional
costs; not a p95. Each partition <30m expected, 20m row deadline; bootstrap 15m bound.
Slot admission is separately bounded at 45m per lease. Red proof once; green full classes
once, target only new failures. Never widen a timeout or loosen an assertion.

## Verification design

- V-1: C1723_NonShutdownCancellationKeepsWorkerAlive (wake/stall/withheld): real
  EF interceptor throws non-host OCE; another SQL pass executes, Warning logs, shutdown joins.
  Red witness is the original production tree with new tests, before the fix.
- V-2: C1723_IneligibleBacklogDoesNotConsumePage (incident/stopped): 192 older
  irrelevant heads, one newly aged row found in the first scan, <=16 DB commands and no input.
- V-3: C1723_RepeatedScanTimeoutRaisesOnceAndSuccessRearms: K-1 quiet, K one incident,
  more failures stay once, success resets, another K failures re-arms; actual AttentionService row.
  Variants: normal alert write and first alert write canceled (retry without ending the loop).
- R-1: full ChannelInputWithheldIncidentTests and DialogDeliveryAdmissionTests preserve
  transaction races, lock deadlines, no-input, index plan, restart and delivered prompt witnesses.
- R-2: full ChannelBridge*/ChannelInbound* (includes ChannelInboundRecoveryTests C1716).
- R-3: all named classification/registry guards and all Unit partitions.
- Manual-1: inspect callers, absence of OS-dependent changed behavior and rc startup impact;
  no live input/restart/rollout required or authorized. Read runner defaults/catalog first.

All deliberate PCs stay pending for SourceLanding Mutation. Candidates:
../mutations/2026-11-05-card-1723-candidates.md. Ordinary pre-fix red is not PC certification.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-0 | RED | `tests/Antiphon.Tests -> bin-c1723-red/` | pre-fix | `/*/*/ChannelInputWithheldIncidentTests/C1723*` | V-1,V-2,V-3 red first | guarded defects fail | 6 | 4 | true | `TEMP=/tmp` |
| CP-1 | FIX | `tests/Antiphon.Tests -> bin-c1723-final/` | withheld-hosted | `/*/*/(ChannelInputWithheldIncidentTests*)\|(DialogDeliveryAdmissionTests*)/*` | V-1,V-2,V-3,R-1 | all pass, no skips | 32 | 4 | true | `TEMP=/tmp` |
| CP-2 | FIX | CP-1 | channels | `/*/*/(ChannelBridge*)\|(ChannelInbound*)/*` | R-2 | all pass, no skips | 88 | 6 | true | `TEMP=/tmp` |
| CP-3 | FIX | CP-1 | guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | R-3 | all pass, no skips | 47 | 3 | true | `TEMP=/tmp` |
| CP-4 | FIX | CP-1 | unit-ad | `/*/*/(A*)\|(B*)\|(C*)\|(D*)/*[Category=Unit]` | R-3 | nonzero, all pass | 1 | 15 | true | `TEMP=/tmp` |
| CP-5 | FIX | CP-1 | unit-eh | `/*/*/(E*)\|(F*)\|(G*)\|(H*)/*[Category=Unit]` | R-3 | nonzero, all pass | 1 | 5 | true | `TEMP=/tmp` |
| CP-6 | FIX | CP-1 | unit-il | `/*/*/(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit]` | R-3 | nonzero, all pass | 1 | 5 | true | `TEMP=/tmp` |
| CP-7 | FIX | CP-1 | unit-mp | `/*/*/(M*)\|(N*)\|(O*)\|(P*)/*[Category=Unit]` | R-3 | nonzero, all pass | 1 | 10 | true | `TEMP=/tmp` |
| CP-8 | FIX | CP-1 | unit-qt | `/*/*/(Q*)\|(R*)\|(S*)\|(T*)/*[Category=Unit]` | R-3 | nonzero, all pass | 1 | 15 | true | `TEMP=/tmp` |
| CP-9 | FIX | CP-1 | unit-uz | `/*/*/(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit]` | R-3 | nonzero, all pass | 1 | 5 | true | `TEMP=/tmp` |
