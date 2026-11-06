# CARD-1581 unbound control candidates

Original Code owner edc7b76c-0b0e-4c38-8410-41193227cf49. These are unbound candidates,
not a resolved mechanical pack. Every variant remains pending for SourceLanding
Mutation, which owns exact binding, deliberate mutants and red/restore/green evidence.

| PC | Variant | Intended guarded defect | Exact witness method | Decisive oracle |
|---|---|---|---|---|
| PC-1 | Code, Investigate | Remove dependent dispatch exclusion from the land yield decision | AgentTaskLandDispatchYieldTests.C1581_a_same_card_dispatch_waiting_for_land_does_not_make_that_land_yield | expected Complete, actual Held; publication and subsequent dispatch assertions remain |
| PC-2 | mixed | Feed the unfiltered waiter list to the decision/diagnostics | AgentTaskLandDispatchYieldTests.C1581_an_independent_waiter_keeps_its_dispatch_priority | Held detail must omit dependent task ID while naming independent ID |
| PC-3 | different-card, already-cut, read-only, repair, settlement | Incorrectly drop an independent waiter or bypass yielding entirely | AgentTaskLandDispatchYieldTests.C1581_an_independent_waiter_keeps_its_dispatch_priority | expected Held, actual Complete; request admission count stays zero |
| PC-4 | explicit-source, explicit-descendant, already-contained | Restore the over-broad database-only filter, excluding candidates without a proven pending_land hold on this exact task | AgentTaskLandDispatchYieldTests.C1581_a_runnable_same_card_dispatch_keeps_priority | real sibling guard permits dispatch; expected Held, actual Complete, Attempt must remain zero and waiter identity retained |
| PC-5 | eligible, busy, restart, enqueue-failure | Remove dependent filtering so the second hosted pass yields to the dispatch that needs publication | AgentTaskLandDispatchYieldTests.C1581_hosted_land_and_dispatch_reach_complete_recipient_prompts | expected second pass Completed, actual Held; joined task/request/event/note/queue identities and complete recipient UserPrompt, single target publication and dispatch required |
| PC-6 | current-intent | Re-enqueue the rejected siblingIntent instead of the current route/default after a stale claim | AgentTaskDispatchBaseGuardTests.C1581_ChangedDefaultReinspectionUsesCurrentIntent | bounded replacement-worker barrier; expected Dispatched, actual Queued, with all C540 base/sibling warning assertions retained |

Review round-1 repair ordinary red: a4f79174cda92bb664470832bd3b2d1ba61e847e,
CP-5 run 20261106-202610-6cf1, 7/7 failed at the priority assertion, clean source
and verified build. This is not a deliberate mutant cycle. PC-4/PC-5 and every
original PC variant remain pending SourceLanding Mutation. The new acceptance
also rejects the over-broad filter at its first hosted pass.

Bind each cycle method-scoped with the exact committed source, selector, artifact,
mutation patch and oracle. No extra production bypass or external executor.

Round-3 ordinary pre-fix witness: b3982b3146eec7e27cc3aeabc29092b90943b408,
CP-9 run 20261106-224130-24b4, 1/1 intended dispatch assertion failure, clean source
and verified build. PC-6 and every earlier variant remain pending; Code ran no
deliberate mutation cycle.
