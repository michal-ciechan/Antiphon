# CARD-0967 retired alias holds

Code task / landing owner: 78b9203d-612f-4c2e-bc87-0485f7062150.
Base: c6d5d56b5b4c565d36157e21de9c85029cc45b53. Round: Final.
No pre-existing CARD-0967 Plan/TestDesign artifact was supplied or found.
This bounded implementation and verification manifest records the card acceptance.

Gap: "an active hold row keyed on one of them can no longer be cleared or converted through the API or the hold form."
Acceptance: "DELETE (clear) accepts any alias that ModelAlias.Normalize recognizes";
"Decide whether PUT should accept a retired-but-selectable profile slug";
"The hold panel lists and clears a held row whose alias is not in the current dropdown."

## Implementation

S1: Commit regression tests over unchanged production at the base, run CP-1 for red evidence.
S2: Have the manual PUT/DELETE validator use ModelAlias.Normalize with the parsed kind.
Accept PUT as well: AgentTaskService.CreateAsync and DispatchModelAlias.Resolve honor
recognized explicit ModelId selections, including retired slugs. Keep tier ladder,
ListAvailable, kind validation, expiry, clearing stamps and dispatch behavior unchanged.
CanonicalHoldAlias remains a current-ladder utility; clarify its documentation.
No client changes: ModelAvailabilityPanel maps snapshot.holds directly and passes
row.kind / row.modelAlias to Clear; the hold form's dropdown only controls new holds.
S3: Complete ordinary evidence and record the task-chip follow-up after searching the board.

## Verification design

Invariant: recognized historical hold identities remain operable, while unknown identities
remain rejected and a historical hold blocks only matching explicit selections.
No delivery, landing or lease implementation changes. Persistence is exercised through
HTTP with real PostgreSQL and fresh database reads. Source runs are committed and clean.
No unbounded classes; no full assembly run. Final includes the whole Unit lane.

| ID | Verification / guard |
|---|---|
| V-1 | `ModelAvailabilityHttpTests.Retired_manual_hold_is_listed_and_DELETE_clears_persisted_row`: seed manual open-ended rows, GET, DELETE 204, persisted OperatorCleared and ReleasePendingAt, repeat preserves stamp, GET absent; Codex sol/terra/old sol and Grok 4.6. |
| V-2 | `ModelAvailabilityHttpTests.PUT_accepts_retired_selectable_alias_and_converts_auto_hold`: manual PUT creates retired rows; subsequent PUT converts auto rows in place with operator deadline. |
| V-3 | `ModelAvailabilityCreateTests.Retired_sol_hold_allows_current_tier_but_blocks_exact_selection`: High/Medium create permitted, exact pinned ModelId create and dispatch preflight refused, after clear exact create permitted. |
| V-4 | `ModelAvailabilityHttpTests.Unknown_alias_PUT_and_DELETE_remain_422_without_persisting_a_hold`: independent PUT/DELETE variants check alias problem field and zero garbage persistence. |
| V-5 | Manual read: panel lists API holds without dropdown filtering and clear uses row alias; API clear suffices. |
| R-1 | Whole affected ModelAvailability*, ModelAlias* and HTTP classes, including scripts. |
| R-2 | Whole Category=Unit lane. |

Positive controls remain pending for method-scoped post-land SourceLanding Mutation:
PC-1: restore current-ladder validation in ClearAsync; `ModelAvailabilityHttpTests.Retired_manual_hold_is_listed_and_DELETE_clears_persisted_row` must fail at assertion `C967-clear-status`. Variants: Codex sol/terra/old sol, Grok 4.6.
PC-2: make unknown alias validation permissive; `ModelAvailabilityHttpTests.Unknown_alias_PUT_and_DELETE_remain_422_without_persisting_a_hold` must fail at assertion `C967-unknown-status`. Variants: independent PUT and DELETE.
PC-3: restore current-ladder validation in PUT; `ModelAvailabilityHttpTests.PUT_accepts_retired_selectable_alias_and_converts_auto_hold` must fail at assertion `C967-retired-put-status`. Variants: sol/terra/old sol, bare Sol, folded Terra.
PC-4A: remap historical Sol to current Sol at create selection; `ModelAvailabilityCreateTests.Retired_sol_hold_allows_current_tier_but_blocks_exact_selection` must fail at assertion `C967-create-exact-refusal`. Variants: High/Medium.
PC-4B: remap historical Sol to current Sol at dispatch selection; `ModelAvailabilityCreateTests.Retired_sol_hold_allows_current_tier_but_blocks_exact_selection` must fail at assertion `C967-dispatch-exact-alias`. Variants: High/Medium.
No scratch mutants are run by Code under the standing stage ownership contract.

The lane and manual-read rows use executed TRX census and source inspection; the following
inline checklist enables explicit source selection for static lint of the named guards.

```plan-coverage-v1
{"version":1,"items":[]}
```

Existing validation test data changes from claude-fable-5 (recognized by Normalize) to
claude-mystery-9 (unrecognized). All existing 422 assertions remain intact.
PUT family-text variants pin the expanded recognized-vocabulary behavior.
Follow-up CARD-1009 (e44a579c-427c-44eb-80f8-57648280596e) owns historical task-chip display.

### Cost

Ordinary floor: 11 minutes; authoring/evidence about 15 minutes. Mutation floor: 8 minutes
plus discovery/reporting. Run once per committed slice group, no repeats after green.
CP-1 is intentionally red, S1 tests on unchanged base production; CP-2 is its repaired
full-class rerun plus the affected classes at S2; CP-3 is the Final Unit lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c967-red/` | retired-baseline | `/*/*/ModelAvailabilityHttpTests*/Retired_manual_hold_is_listed_and_DELETE_clears_persisted_row` | V-1 | 4 executed, intended 422 versus 204 failures | 4 | 3 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c967-final/` | affected | `/*/*/(ModelAvailabilityTests*)\|(ModelAvailabilityManualTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)\|(ModelAvailabilityHttpTests*)\|(ModelAvailabilityScriptTests*)\|(ModelAliasTests*)/*` | V-1,V-2,V-3,V-4,R-1 | all listed, 0 failed | 7 | 5 |
| CP-3 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1 executed, 0 failed | 1 | 3 |
