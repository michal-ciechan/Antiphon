# CARD-0097 S-F — unbound Mutation candidate

Binding required after Code, ordinary Review and confirmed SourceLanding. No Mutation ran.
Original Code/landing owner: `e4c19130-d80f-4275-855f-db5b826c9948`.
Repair branch: `feat/card-task-b52a06a7` (Review `44e2b653` D1/D2).
This is a recipe, not an eligible execution pack.

Owners: [landed plan](../plans/2026-10-14-card-0097-matrix-plan.md#positive-controls-pending-one-per-behaviour-mutation-is-not-run-in-this-card),
[test design](../plans/2026-10-14-card-0097-matrix-test-design.md#positive-controls),
[ordinary evidence](../evidence/2026-10-14-card-0097-sf-code.md).

| Pending ID / variant | Method-scoped filter | Planned defect and decisive assertion |
|---|---|---|
| PC-32 / V-F1 | `/*/*/OrchestratorInstructionsRendererTests/Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` | Omit matrix rendering: literal `## 5. Role × complexity matrix` is absent |
| PC-33 / V-F2 `complexity-chain` | `/*/*/OrchestratorInstructionsRefreshTests/Covered_writes_each_regenerate_and_notify` | Drop the UpsertAsync settings signal: the complexity-chain argument has zero submitted notices instead of one; inspect that argument separately among the seven method results |
| PC-34 / V-F3 equal rewrite | `/*/*/OrchestratorInstructionsDeltaTests/An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` | Report a matrix delta for equal input: expected empty string becomes nonempty |
| PC-35 / V-F7 part a | `/*/*/OrchestratorInstructionsRendererTests/The_matrix_section_is_bounded_per_line_and_elided_in_place_under_pressure` | Remove the row byte cap: an eight-candidate tier fixture exceeds 160 UTF-8 bytes |
| PC-42 / V-F7 part b | `/*/*/OrchestratorInstructionsRendererTests/The_matrix_section_is_bounded_per_line_and_elided_in_place_under_pressure` | Move matrix elision behind the card-pin loop: at full minus one byte the card pin disappears and the matrix remains expanded |
| PC-44 / V-F2b | `/*/*/OrchestratorInstructionsRefreshTests/A_cell_write_notice_is_received_as_one_complete_user_prompt_equal_to_its_queued_body` | Drop the UpsertAsync signal: zero notice-header queue rows instead of one, with startup notifications disabled and a seeded transcript floor |
| PC-50 / V-F9 Hard, Medium, Easy, together | `/*/*/OrchestratorInstructionsRefreshTests/A_failed_chain_read_marks_the_affected_file_cells_unknown` | Restore candidate-only tier formatting: the persisted failed tier reads `unset` instead of the literal unknown reason |
| PC-51 / V-F10 Hard, together | `/*/*/OrchestratorInstructionsRefreshTests/A_failed_chain_read_notice_reports_unknown_and_recovery_reports_the_candidate` | In `DiffMatrix`, replace the new Hard unknown string with `unset`: the delivered transition assertion fails while the file stays correct |
| PC-52 / V-F11 API pointer | `/*/*/OrchestratorInstructionsGuidanceTests/Routing_cross_references_resolve_to_an_existing_owner_heading` | Point the model-disabled paragraph at a nonexistent title: owner-heading membership fails |

This report discharges none of the plan's PC-1–PC-52; S-F's nine rows above remain pending.
PCs for the other slices receive no execution credit here. The initial missing-feature red and ordinary green runs do
not discharge any PC. Mutation owns deliberate mutations, exact assertion reds, restoration,
restored green and missing-control discovery, including the added direct signal-count, clear,
already-clear, unchanged-cell and snapshot-roster assertions. Bind independently at landed L;
do not treat this candidate as authority to patch by line number or run a whole class for a PC.
