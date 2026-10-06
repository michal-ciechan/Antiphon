# CARD-1079 S3 Final Review rerun (task c8b81183)

Review-owned row table: one isolated build, every class of the brief's ordinary scope in its own driver.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c8b8-rev/` | seat-attention | `/*/Antiphon.Tests.Application/SeatOccupancyAttentionTests/*` | V-13, V-14, V-15, V-16, V-17, V-18, V-19, V-20 | all 8 methods, 0 failed/skipped | 8 | 9 | false |
| CP-2 | S3 | CP-1 | attention-regressions | `/*/Antiphon.Tests.Application/(RunnerAlarmAttentionTests*)\|(CardClosedAttentionTests*)/*` | R-5, R-6 | all 7 methods, 0 failed/skipped | 7 | 4 | false |
| CP-3 | S3 | CP-1 | seat-sampler | `/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/*` | V-7, V-8, V-9, V-10, V-11, V-12 | all 6 methods, 0 failed/skipped | 6 | 5 | false |
| CP-4 | S3 | CP-1 | seat-join-slots | `/*/Antiphon.Tests.Application/(SeatDesktopJoinTests*)\|(RunnerSlotRulesTests*)\|(RunnerSlotEndpointTests*)/*` | V-6, R-1, R-2 | all 13 methods, 0 failed/skipped | 13 | 5 | false |
| CP-5 | S3 | CP-1 | attention-existing | `/*/Antiphon.Tests.Application/(AttentionServiceTests*)\|(AttentionApiTests*)\|(AttentionKindWireTests*)/*` | review | >= 160 executed, 0 failed/skipped | 160 | 12 | false |
| CP-6 | S3 | CP-1 | registry-guard | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | review | all 3 methods, 0 failed/skipped | 3 | 3 | false |
| CP-7 | S3 | CP-1 | seat-release | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/*` | review | all 39 results, 0 failed/skipped | 39 | 10 | true |
