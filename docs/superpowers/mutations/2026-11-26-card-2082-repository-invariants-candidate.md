# CARD-2082 repository invariant controls

Unbound candidate for Final Review. Every variant is pending. SourceLanding Mutation owns deliberate defects and the precise red, restore and green evidence.

| Verification | Exact TUnit filter |
|---|---|
| V-1 | `/*/*/PlanCheckpointClassSelectionGuardTests/C2082_Repository_readers_belong_to_the_invariant_namespace` |
| V-2 | `/*/*/PlanCheckpointClassSelectionGuardTests/C2082_Owned_fixture_reads_remain_in_the_product_lane` |
| V-3 | `/*/*/NightlyTestsPlanTests/C2082_Full_prefix_precedes_ordinary_work_and_stops_on_failure` |
| V-4 | `/*/*/MergeHighRadiusSubsetTests/C2082_Merge_executes_invariants_on_empty_and_unrelated_diffs` |
| V-5 | `/*/*/RcProjectSelectionTests/C2082_Incremental_requires_fresh_invariant_receipts` |

| PC | Break | Required failing subcase/assertion |
|---|---|---|
| PC-1 | Move a real standalone fixture to its former namespace while retaining its category. Separately remove only its category. | V-1 live differences include the exact method, tracked input and missing designation in each variant. Keep FQN references compiling. |
| PC-2 | Filter discovered readers to keys already in the roster before computing discrepancies. | V-1 unknown partial-helper reader disappears from the expected offender set. |
| PC-3 | Compare only relative path when resolving the helper's read root. | V-2 exact set gains the owned parser method on the duplicate policy filename. |
| PC-4 | Return successful empty results on a missing scan root. | V-2 missing-root error assertion fails. |
| PC-5 | Resolve a split historical operand by surviving short class alone, omitting its extracted argument. | V-1 before/after mapped row is missing the specified argument while its class and Min still resolve. |
| PC-6 | Collapse the second UID under its class. Separately let an excluded UID enter an unpartitioned owner. | V-3 exact partition loses argument two or gains the excluded UID, respectively. |
| PC-7 | Release the first streamed suite's ordinary child immediately. | V-3 no-history actual first-start assertion names the ordinary child. |
| PC-8 | Sort prior failures globally. Separately replay a saved ordinary-first sequence globally. | V-3 history and previous-order callback traces put the ordinary launch before the final invariant receipt. |
| PC-9 | Continue the ordinary loop after a failed prefix. Separately mark an interrupted prefix complete when persisting it. | V-3 failure trace gains an ordinary launch or incomplete manifest loses the outstanding identity. |
| PC-10 | Gate adding invariant rows on at least one impacted class. | V-4 empty-diff consumer receives fewer than the two portable owner sets. |
| PC-11 | Deduplicate the subject intersection by short class alone. | V-4 overlapping project/argument multiset loses the second qualified identity. |
| PC-12 | Count a required Windows row as fulfilled by the portable host's same-name pass. | V-4 completion succeeds while matching Windows receipt is withheld. |
| PC-13 | Drop one projected linked owner in the standalone renderer, then in the active Final renderer. | V-4 standalone/Final membership comparison misses that project in the corresponding variant. |
| PC-14 | Clear the invariant always-dirty reason in shadow selection for an empty diff. | V-5 shadow decision for the invariant becomes carry. Ordinary neighbor still carries. |
| PC-15 | Permit the prior identical-digest invariant on a new attempt. Separately reuse an older attempt's invariant in the resume path. | V-5 cut or resume trace has no fresh invariant execution for the required attempt. |
| PC-16 | Accept carried disposition, foreign SHA, stale attempt, wrong native platform, mismatched digest, or an incomplete UID set at reconciliation. Mutate one comparison per run with all other fields valid. | V-5 named forged-receipt variant closes the required invariant obligation. |
| PC-17 | Carry an opaque mixed baseline by its old group name/digest. | V-5 opaque-history Full fallback assertion fails. |

PC-5, PC-11, PC-12 and PC-13 require the expanded historical, overlapping-project, native-receipt and active Final witnesses described in the verification appendix before binding. Review must confirm each assertion reaches the named defect. See [verification design](../plans/2026-11-26-card-2082-repository-invariants-test-design.md).
