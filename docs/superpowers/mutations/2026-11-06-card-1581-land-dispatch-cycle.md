# CARD-1581 unbound control candidates

Original Code owner edc7b76c-0b0e-4c38-8410-41193227cf49. These are unbound candidates,
not a resolved mechanical pack. Every variant remains pending for SourceLanding
Mutation, which owns exact binding, deliberate mutants and red/restore/green evidence.

| PC | Variant | Intended guarded defect | Exact witness method | Decisive oracle |
|---|---|---|---|---|
| PC-1 | Code, Investigate | Remove dependent dispatch exclusion from the land yield decision | AgentTaskLandDispatchYieldTests.C1581_a_same_card_dispatch_waiting_for_land_does_not_make_that_land_yield | expected Complete, actual Held; publication and subsequent dispatch assertions remain |
| PC-2 | mixed | Feed the unfiltered waiter list to the decision/diagnostics | AgentTaskLandDispatchYieldTests.C1581_an_independent_waiter_keeps_its_dispatch_priority | Held detail must omit dependent task ID while naming independent ID |
| PC-3 | different-card, already-cut, read-only, repair, settlement | Incorrectly drop an independent waiter or bypass yielding entirely | AgentTaskLandDispatchYieldTests.C1581_an_independent_waiter_keeps_its_dispatch_priority | expected Held, actual Complete; request admission count stays zero |

Bind each cycle method-scoped with the exact committed source, selector, artifact,
mutation patch and oracle. No extra production bypass or external executor.
