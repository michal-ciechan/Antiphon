# CARD-1247 publication cancellation ownership regression

R-7 proves P2's in-flight ownership rule. The producer fixture separates actual delivery from
its caller's cancellable wait, holds delivery on a bounded gate, and lets cancellation end
only the wait. After joining observed caller cancellation, a second dispatcher must not own
the same reply. The fixture then releases and joins every actual delivery and requires one
correctly addressed answer. Both direct routing variants are required.

This is ordinary expected-red verification, not Mutation. PC-9 (forward caller cancellation
into the producer wait and release the claim while transport continues) remains pending.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-11 | cancellation-red | `tests/Antiphon.Tests -> bin-c1247-caee-cancel-red/` | in-flight-cancellation | `/*/Antiphon.Tests.Application/(ChannelReplyLateConfirmDispatchTests*)/(Caller_cancellation_does_not_release_a_still_publishing_direct_reply*)` | R-7 | expected duplicate-owner assertion before repair | 2 | 6 |
