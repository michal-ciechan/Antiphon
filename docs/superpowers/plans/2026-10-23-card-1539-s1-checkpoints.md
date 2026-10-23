# CARD-1539 S1 frozen execution selection

S1 only; parent plan is `2026-10-23-card-1539-lock-holder-capture-plan.md`.
No Unit/assembly sweep. CP-1 intentionally fails holder assertions against the
unavailable scaffold. CP-2 freezes the thirteen expanded core cases. CP-7 and
CP-S1-R1 reuse CP-2's one build for S1's R-6 and R-1 compatibility obligations.
Windows provider/native rows and S2-S7 wiring remain deferred, never passed.
PC inventory/independent variants remain as in the parent plan, pending Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1539-red/` | linux-real-red | `/*/*/FileUseCaptureTests/C1539_linux_holder*` | V-1 | 2 intended missing-holder assertion failures, 0 skipped | 2 | 5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1539-s1/` | linux-core | `/*/*/FileUseCaptureTests/C1539_*` | V-1..V-7, R-1 | 13 passed, 0 failed/skipped | 13 | 5 |
| CP-S1-R1 | S1 | CP-2 | compatibility | `/*/*/WorktreeLockDiagnosticsTests/(C443_PartialRetainsOwners*)\|(C443_EmptyRequiresCompleteCoverage*)\|(C443_ReusedPidDoesNotEnrichOldOwner*)` | R-1 | 3 passed, 0 failed/skipped | 3 | 1 |
| CP-7 | S1 | CP-2 | redaction-compatibility | `/*/*/(DiagnosticsRedactorTests*)\|(RunnerProcessProbeRedactionTests*)\|(CheckpointSlotContractTests*)/(No_secret_shaped_string_survives_redaction*)\|(Home_path_is_kept_only_with_IncludePaths*)\|(Project_directories_are_replaced_unless_IncludePaths*)\|(Redacts_each_credential_assignment_without_consuming_neighbors*)\|(Redacts_the_union_of_exact_secret_occurrences_without_order_or_overlap_leaks*)\|(diagnostics_are_bounded_and_escape_body_controls*)` | R-6 | 54 passed, 0 failed/skipped | 54 | 1 |
