# Immutable StartRef does not wait for a sibling land

Code task / landing owner: `dc59c094-9321-47b5-802b-a5e8803629aa`.
Card: CARD-1514.
Base: `e54065fe59f798fb5dc395a222679545ebe1c9e0`.
Branch: `feat/card-task-dc59c094`.

## Implementation

A Worktree task whose StartRef is a 40- or 64-character commit id dispatches while a
same-card sibling land is still pending. The ordinary sibling warning stays for an
uncontained sibling. Past the inventory cap the task dispatches with the
incomplete-inspection warning (`sibling-inspection-incomplete`, reason `candidate_limit`).
A symbolic ref such as `master` still holds. The reviewed-source exemption is unchanged.
A git inspection error still holds.

The peek that follows a consumed sibling-land result repeats that landing sentence, so
exact-text dedupe keeps one Held event. The stale-observation path still records the
pending sentence.

## Performance budget

CP-1 carries the cold `Antiphon.Tests` build. CP-2, CP-3 and CP-4 reuse that build.
No repeat block after green. No whole Unit lane. Cost floor is the sum of
`EstimatedMinutes` (45). Mutation is waived (operator 2026-11-12). No positive controls.

## Unlisted runs

- Red witness at `f16d1c91773f9d9c5377df4f4d1eb984ad52b82d`, before this plan existed.
  The combined filter expanded to all of `AgentTaskDispatchBaseGuardTests`: 103 executed,
  2 failed. `C1514_ImmutableStartRefDoesNotWaitForPendingSiblingLand` stayed not
  Dispatched. `C1514_SiblingLandHoldDetailStaysStableAcrossReinspection` recorded the
  pending sentence twice.
- Red witness of `C1514_ImmutableStartRefDoesNotWaitBeyondInventoryCap` alone at the same
  SHA: 1 executed, 1 failed, status `Queued`.
- One leased green run of those three methods before the checkpoint tool, so a fixture
  miss does not spend the cold checkpoint build.

## Verification design

- V-1: a 40-hex StartRef dispatches while a sibling land is pending, with the ordinary
  sibling warning.
- V-2: the same StartRef past the inventory cap dispatches with
  `sibling-inspection-incomplete` containing `candidate_limit`.
- V-3: two inspections of one pending land write the pending sentence once and the
  landing sentence once.
- R-1: `C1349_PendingLandSurvivesLimits` with an unrelated symbolic StartRef still holds,
  and `C753_ExplicitStartRefContainingReviewedSourceBypassesSiblingHold` stays silent.
- R-2: `AgentTaskDispatchBaseGuardTests` stays green, including V-3.
- R-3: `DispatchSiblingInspectionTests` and `DispatcherSiblingInspectionSchedulingTests`
  stay green, including V-2 and the symbolic-StartRef hold.
- R-4: explicit StartRef sync-debt identity in `AgentTaskWorktreeBaseSyncDebtTests` is
  unchanged.

No PC variants. Every PC stays pending for method-scoped SourceLanding Mutation.
Restart: none. Windows is UNOBSERVED.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | fix | `tests/Antiphon.Tests -> bin-c1514-proof/` | immutable-witnesses | `/*/*/(AgentTaskDispatchBaseGuardTests*)\|(DispatchSiblingInspectionTests*)/C1514_Immutable*` | V-1, V-2 | 2 executed, 0 failed/skipped | 2 | 15 |
| CP-2 | fix | CP-1 | dispatch-base-guard | `/*/*/AgentTaskDispatchBaseGuardTests/*` | V-3, R-1, R-2 | 103 executed, 0 failed/skipped | 103 | 10 |
| CP-3 | fix | CP-1 | sibling-inspection | `/*/*/(DispatchSiblingInspectionTests*)\|(DispatcherSiblingInspectionSchedulingTests*)/*` | V-2, R-1, R-3 | 83 executed, 0 failed/skipped | 83 | 12 |
| CP-4 | fix | CP-1 | sync-debt | `/*/*/AgentTaskWorktreeBaseSyncDebtTests/*` | R-4 | 10 executed, 0 failed/skipped | 10 | 8 |
