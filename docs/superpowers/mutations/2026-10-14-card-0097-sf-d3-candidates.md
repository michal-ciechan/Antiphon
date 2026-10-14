# CARD-0097 S-F D3 — unbound Mutation candidates

Code task `786f7451-93ff-4b26-b62e-73714d454377`; original landing owner
`e4c19130-d80f-4275-855f-db5b826c9948`. These are candidates for the caller's
post-Review, post-land SourceLanding Mutation commission, not a bound execution pack.
Code ran ordinary baseline/fixed tests; no deliberate mutant was inserted.

Existing S-F controls remain pending: PC-32, PC-33 (`complexity-chain`), PC-34,
PC-35(a), PC-42(b), PC-44, PC-50 (Hard/Medium/Easy/together), PC-51 (Hard/together),
and PC-52 (API pointer). Their plan bindings remain authoritative.

| Candidate | Exact method for later method-scoped binding | Guarded defect / variant |
|---|---|---|
| Default source | `OrchestratorInstructionsRefreshTests.A_failed_chain_read_notice_reports_unknown_and_recovery_reports_the_candidate` | Medium/together: the source falls back to rolePolicy on the failed default-chain read |
| Stage-pin source | `OrchestratorInstructionsRefreshTests.Failed_reads_are_unknown_in_file_and_notice_then_recover` | `stage pin`: an unreadable pin becomes absent and the chain source becomes definite |
| Read-state normalization | Same method | availability, chain metadata and standing-agent rows: exception-bearing withholding text replaces an unknown state; a changed exception class triggers another revision/notice |
| Section unknown state | Same method | policy/local-budget, runners, defaults, holds and pins rows: a failed section is treated as configuration, none, or removed; recovery loses observed values |
| File diagnostic visibility | `OrchestratorInstructionsRendererTests.Unknown_matrix_details_keep_the_byte_cap_and_matrix_first_elision` | Remove the extra effective/source detail, count comma-separated unknown reasons as candidates, exceed 160 bytes, or move matrix elision after card pins |
| Delta clipping | `OrchestratorInstructionsDeltaTests.A_long_known_value_keeps_the_unknown_transition_reason_visible` | Restore clipping that retains the long old value and hides the new unknown reason |

Mutation owns missing-control discovery, binding and red/restore/green evidence. Select one
exact method with `/*/*/ClassName/ExactTestMethod` for a bound cycle; the ordinary class
checkpoint filters are not mutation selections. The baseline non-default-chain rows were
already green from D2 and are retained regression coverage, not a new red witness.
