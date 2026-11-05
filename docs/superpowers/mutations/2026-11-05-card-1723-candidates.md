# CARD-1723 unbound Mutation candidates

All pending; Mutation owns exact-path edits, method-scoped red/restore/green, missing
control discovery and final binding to SourceLanding SHA. Never run whole classes per PC.

| PC | Variant/edit | Detector method | Status |
|---|---|---|---|
| PC-1 | ChannelBridgeService.cs wake catch excludes every OCE | C1723_NonShutdownCancellationKeepsWorkerAlive (wake) | pending |
| PC-2 | ChannelBridgeService.cs stall catch excludes every OCE | C1723_NonShutdownCancellationKeepsWorkerAlive (stall) | pending |
| PC-3 | ChannelBridgeService.WithheldIncident.cs catch accepts every OCE as shutdown | C1723_NonShutdownCancellationKeepsWorkerAlive (withheld) | pending |
| PC-4 | remove page SQL inbound incident-stamp exclusion | C1723_IneligibleBacklogDoesNotConsumePage (incident) | pending |
| PC-5 | remove page SQL Running-session exclusion | C1723_IneligibleBacklogDoesNotConsumePage (stopped) | pending |
| PC-6 | disable threshold escalation | C1723_RepeatedScanTimeoutRaisesOnceAndSuccessRearms | pending |
| PC-7 | remove once-per-streak latch AND regenerate incident ID per failure (latch removal alone is equivalent under ON CONFLICT) | C1723_RepeatedScanTimeoutRaisesOnceAndSuccessRearms | pending |
| PC-8 | remove success reset | C1723_RepeatedScanTimeoutRaisesOnceAndSuccessRearms | pending |
| PC-9 | remove report-error catch so non-host incident write cancellation escapes | C1723_RepeatedScanTimeoutRaisesOnceAndSuccessRearms (failed-write variant) | pending |

Prior CARD-1591 PC-1..PC-15 remain separately pending in
2026-11-05-card-1591-s1-candidates.md; this task discharges none.
