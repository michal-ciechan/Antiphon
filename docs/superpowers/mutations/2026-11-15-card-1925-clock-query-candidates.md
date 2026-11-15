# CARD-1925 unbound positive-control candidates

All five controls are pending for Mutation. This is a candidate document, not a bound
mechanical pack, execution authority or evidence. The brief records the operator's
2026-11-12 Mutation waiver. Code executed no deliberate source mutations; the stage
contract reserves red/restore/green and missing-control discovery for Mutation.

The subject tests are in tests/Antiphon.Tests/Application/EfProductionClockQueryTests.cs.
Each candidate needs a fresh baseline, one precise method filter, an expected assertion
failure rather than setup/build failure, byte restoration, and a restored green run.

| ID | Production file and operation | Candidate defect | Exact method filter | Decisive oracle |
|---|---|---|---|---|
| PC-1 | server/Application/Services/AgentTaskDispatcher.cs, DispatchCoreAsync optional expiry | Replace t.ExecutionDeadlineAt <= expiredAt with t.ExecutionDeadlineAt <= UtcNow() | `/*/*/EfProductionClockQueryTests/Dispatcher_expiry_parameters_change_without_recompiling_and_tokens_stay_per_row` | compilation count becomes 2 and/or SQL contains a timestamp literal |
| PC-2 | server/Application/Services/ChannelOutboundDeliveryPump.cs, ClaimAsync | Replace d.LeaseUntil <= now with d.LeaseUntil <= UtcNow() | `/*/*/EfProductionClockQueryTests/Outbound_claim_uses_clock_parameters_without_recompiling` | cutoff clock parameter disappears, SQL contains a literal, compilation count becomes 2 |
| PC-3 | server/Application/Services/SessionMessageQueueService.cs, CancelExpiredBriefsAsync | Replace CompletedAt setter argument completedAt with UtcNow() | `/*/*/EfProductionClockQueryTests/Queue_expiry_completion_uses_clock_parameters_without_recompiling` | completion clock parameter disappears and/or compilation count becomes 2 |
| PC-4 | server/Application/Services/OutboundConversionTaskRunner.cs, CreateAsync | Capture DateTime.UtcNow into a local immediately before the fence and use that local in the lease predicate | `/*/*/EfProductionClockQueryTests/Conversion_fence_uses_database_now_in_the_production_command` | now() absent; DateTime parameter present |
| PC-5 | server/Application/Services/AgentTaskDispatcher.cs, DispatchCoreAsync optional expiry | Capture Guid.NewGuid() once into a local before the bulk UPDATE and use the local setter argument | `/*/*/EfProductionClockQueryTests/Dispatcher_expiry_parameters_change_without_recompiling_and_tokens_stay_per_row` | gen_random_uuid() absent |

Binding must resolve exact source anchors and patches at the commissioned snapshot SHA.
No source seam or test assertion may be weakened to obtain a passing control. No repetition
is needed after baseline green except the single mutant and restored executions per control.
