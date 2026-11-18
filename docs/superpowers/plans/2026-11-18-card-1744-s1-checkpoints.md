# CARD-1744 S1 checkpoints

Slice S1 of [2026-11-18-card-1744-blockers-g1-g3-plan.md](2026-11-18-card-1744-blockers-g1-g3-plan.md).
CP-1 is the red witness (entry absent; WhatIf returns before the key list). CP-2 is the green row.
One build and one filter per row. The test bodies are under five seconds; the estimates include a cold `tests/Antiphon.Tests` build.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-red | tests/Antiphon.Tests -> bin-c1744g1r/ | g1-red | `/*/*/(C1744G1*)/*` | W-1, W-2, W-3 | red: 3 failed, 0 passed | 3 | 15 |
| CP-2 | S1 | tests/Antiphon.Tests -> bin-c1744g1/ | g1-green | `/*/*/(C1744G1*)\|(Server2ProductionServerContractTests*)/*` | W-1, W-2, W-3, existing contract | 0 failed | 18 | 12 |
