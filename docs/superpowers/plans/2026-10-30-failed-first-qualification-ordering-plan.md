# Failed-first qualification ordering and explicit partial sweep

Plan task `38d5d544`, 2026-10-30. Status: skeleton; design is being grounded against base `2991d96bb301f6de5177c45bd18c51542561875c`. Leave run `d47848cc` untouched.

This plan is filed under the CARD-1514 qualification workstream; no new card number is asserted. The caller-requested filename and card-numbered stage filename contain identical plan text and must be updated together.

## Ground truth

| Brief assumption | Observed code | Consequence |
|---|---|---|
| History can prioritize qualification | No history input; suite-at-a-time execution | Global schedule over frozen suite plans |
| TRX contains all failures | Group 105 has no TRX, seven diagnostic errors and a hang | Include diagnostic identities and the incomplete chunk |
| Partial can reuse Full reporting | Summary defaults to Full even for incomplete suite selections | Explicit non-credit scope throughout |

## Decisions

D-1. Preserve Full coverage, identities, prices and verdict requirements; change order only. Use global priority.

D-2. A separately explicit Partial sweep cannot earn release credit. Missing evidence never silently narrows the list.

## Ordered implementation slices

Pending source-verified seams, witnesses and 150-minute sizing. Do not commission implementation from this skeleton.

## Verification design

Test design is folded in: real-planner replay, assertion-first RED, consumer refusals and exact checkpoint rows will be supplied before completion.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards`. Historical replay: 19,012 nodes, 150 chunks, 15.847 seconds; one observation, not p95. Share setup, record phases, budget about 2x measured p95 after bounded collection. No Full run is authorized; any selection over 30 minutes requires Debug.

## Landing and commissioning

Rc first, separate master port. No rc rebase, tags, publish, desktop edits or live-run operations. Next: independent Opus plan review, then Code.
