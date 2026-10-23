# CARD-1539 S2 provider execution selection

Reuses S1 from 83a357afb2dccf3cdaeddf17b8cd849cff8d2cee. This task adds the
Windows observation provider first, per the parent slice order. Fixture/checkpoint
wiring and extraction of the existing redactor facades remain separate work;
existing facades stay intact to avoid CARD-1531's in-flight checkpoint area.

The task brief authorizes minimal verification in place of the Final Unit sweep.
CP-1 is a compile-capable unavailable Windows provider with two independent
assertion failures, before implementation. CP-2 is its ordinary green selection.
CP-3 checks adopted S1 in the same frozen build; no stress repetitions.
CP-5 independently guards the new provider backend field at the durable JSON sink:
the pre-fix field bypasses sanitization. After that guard fails, CP-2/3/4 plus
CP-6/7 qualify the repaired source in one build. CP-6/7 retain S1's R-6 and legacy
R-1 compatibility obligations without changing their production facades.
Two CP-5 builds exhausted the unchanged five-minute execution limit before
TUnit ran. They remain incomplete, not assertion reds. A slot-gated in-memory
provider probe against the source-frozen compiled HostCleanup component reproduced
the sentinel in actual persisted JSON (manual red). The final CP-3 retains the
automated JSON guard. This manual exception adds no checkpoint provenance credit.
S2 adds no databases, hosts, or processes. W-1/W-2 need a separately commissioned
Windows lane; no fake result earns native credit. Windows evidence writer and
production hooks remain unimplemented debt; these rows prove provider behavior.
V-10..V-16, R-2..R-5 and S3-S7 stay deferred. Every PC in the parent plan remains
pending for Mutation, including PC-3/4 and all variants.

## Performance budget

Provisional estimate: one test-project build 2–4 minutes, each portable provider
method <1 second, reused S1 child fixtures <10 seconds total. No measured p95 yet;
record actual per-test TRX and process/IO time from the existing S1 diagnostics.
Native seam calls check cancellation; the caller-owned capture service bounds
waiting to five seconds and retains its worker seat until a late call finishes.
Queue admission remains the checkpoint default; execution is capped at ten minutes
for the complete group. Tool bootstrap is an explicitly listed exception: one
isolated checkpoint tool build under scripts/build-slot.ps1, then --no-build runs.
There is no whole assembly or unbounded class selection in this amendment.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2-red | `tests/Antiphon.Tests -> bin-c1539-s2-red/` | windows-provider-red | `/*/*/FileUseWindowsTests/C1539_*` | V-8, V-9 | 2 assertion failures, 0 skipped | 2 | 4 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1539-s2-green/` | windows-provider | `/*/*/FileUseWindowsTests/C1539_*` | V-8, V-9 | 2 passed, 0 failed/skipped | 2 | 4 |
| CP-3 | S2 | CP-2 | adopted-linux-core | `/*/*/FileUseCaptureTests/C1539_*` | V-1..V-7, R-1 | 13 passed, 0 failed/skipped | 13 | 1 |
| CP-4 | S2 | CP-2 | classification | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | test registration | 1 passed, 0 failed/skipped | 1 | 1 |
| CP-5 | S2-backend-red | `tests/Antiphon.Tests -> bin-c1539-backend-red/` | backend-privacy-red | `/*/*/FileUseCaptureTests/C1539_redaction_before_sinks*` | V-5 backend field | 1 JSON assertion failure, 0 skipped | 1 | 4 |
| CP-6 | S2 | CP-2 | redaction-compatibility | `/*/*/(DiagnosticsRedactorTests*)\|(RunnerProcessProbeRedactionTests*)\|(CheckpointSlotContractTests*)/(No_secret_shaped_string_survives_redaction*)\|(Home_path_is_kept_only_with_IncludePaths*)\|(Project_directories_are_replaced_unless_IncludePaths*)\|(Redacts_each_credential_assignment_without_consuming_neighbors*)\|(Redacts_the_union_of_exact_secret_occurrences_without_order_or_overlap_leaks*)\|(diagnostics_are_bounded_and_escape_body_controls*)` | R-6 | 54 passed, 0 failed/skipped | 54 | 1 |
| CP-7 | S2 | CP-2 | identity-compatibility | `/*/*/WorktreeLockDiagnosticsTests/(C443_PartialRetainsOwners*)\|(C443_EmptyRequiresCompleteCoverage*)\|(C443_ReusedPidDoesNotEnrichOldOwner*)` | R-1 legacy | 3 passed, 0 failed/skipped | 3 | 1 |
