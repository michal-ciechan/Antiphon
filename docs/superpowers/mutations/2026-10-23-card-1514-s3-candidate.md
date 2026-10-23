# CARD-1514 S3 unbound mutation candidate

Status: unbound; all controls pending. This file authorizes no mutation or execution.
Original Code / landing owner: `2c5c38b8-548d-40ee-8948-6bb8557b1b5a`.
Implementation: `b1513df1f6c98738a334f6cef1b7629489c94ebb`.
Ordinary-reviewed C, landing O/L/R, companion card, SourceLanding task and external
evidence root must be supplied by the caller after Review and confirmed publication.
Never substitute the moving master tip for L.

[Plan](../plans/2026-10-23-card-1514-slow-test-speedups-plan.md),
[S3 verification design](../plans/2026-10-23-card-1514-s3-code-verification.md),
[Code evidence](../../investigations/2026-10-23-card-1514-s3-code-evidence.md).
Scope is S3 only. S1/S2/S4/S5 and their future controls are not discharged here.

| ID / variant | Guarded defect | Exact method filter | Intended failure |
|---|---|---|---|
| PC-3a / worker-on-git | Replace Worker's strict limiter with the Git limiter in AgentTaskLandCheckpointWorkerTests.cs | `/*/*/ProcessSpawnLimitTests/C1514_Checkpoint_workers_are_separate_from_the_git_lane` | Worker lacks the required strict attribute |
| PC-3b / lost-worker-mapping | Remove the Worker row from c1514-checkpoint-split-listing.txt | `/*/*/C1514LandingSplitBudgetTests/C1404_Group10MeasuredBudget` | Historical-to-split mapping is not bijective |
| PC-3c / missing-worker-cost | Remove the Worker property from nightly-class-costs.json, retaining valid JSON | `/*/*/C1514LandingSplitBudgetTests/C1404_Group10MeasuredBudget` | Real cost loader/packer fails the independent worker cost oracle |
| PC-3d / matrix-back-on-strict | Replace Matrix's Git limiter with the strict limiter in AgentTaskLandCheckpointMatrixTests.cs | `/*/*/ProcessSpawnLimitTests/C1514_Checkpoint_workers_are_separate_from_the_git_lane` | Matrix lacks the required Git attribute |

All four variants are pending for method-scoped SourceLanding Mutation. Bind a pack
to L before execution; Mutation owns the deliberate edits, intended assertion red,
restore/fresh-build/green, per-control evidence and missing-control discovery.
Zero tests, parse/build/fixture errors, or a different failing assertion are not
successful controls. Do not run a class-wide PC, weaken an assertion or widen a
bound. The variants share methods, so do not batch them as independent mutations.
No timing threshold or speedup PC is introduced: ordinary matched timing plus the
source/assertion bijection establishes the scheduling observation.
Restart: none.
