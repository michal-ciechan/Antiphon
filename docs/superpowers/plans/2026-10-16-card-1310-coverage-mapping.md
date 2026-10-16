# CARD-1310 coverage mapping custody repair

Original Code / landing owner: a5c09b23-f3a1-4956-a4bd-221d9bbf7628.
Base: 6e23e0b4c8d6b2ae12218c2c0058593dbf773530.

The explicit brief excludes the whole Unit lane. This Final round uses the exact
new regression class and existing checkpoint custody classes. Generic creation
records remain append-only; sealed tool-copy authority remains separate. A mapping
append may gain a separate, process-local build seal only for the same file identity,
unchanged initial prefix, observed build exit, and unchanged final bytes. Other
files, replacement mappings, links, tracked paths and post-seal writes stay refused.

## Verification design

- V-1: mapping appended after its creation record is accepted at the build seal and removed.
- V-2: unrelated changed, added, replaced, linked, hard-linked or tracked paths refuse.
- V-3: mapping rewrite/replacement, late write, absent exit, post-seal write and reseal refuse.
- R-1: existing generic custody, native output cleanup and durable tool authority retain their oracles.
- M-1 (optional, brief): one small E2E case and both receipt validators if Windows build custody permits it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1310/` | mapping | `/*/*/(CheckpointCoverageMappingTests*)\|(CheckpointCoverageMappingNativeTests*)/*` | V-1,V-2,V-3 | all 17 results, 0 failed/skipped | 17 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | custody | `/*/*/(OutputCleanupTests*)\|(CheckpointOwnedOutputTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointAuthorityTests*)/*` | R-1 | all available Windows results, report existing platform skips separately | 65 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.E2E -> bin-c1310-e2e/` | e2e-receipt | `/*/*/OutputDistillationCanaryGuardTests/Configuration_refuses_unowned_resources_before_start` | M-1 | all 12 results, 0 failed/skipped; both validators accept | 12 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Run CP-1 red before the fix, then CP-1/CP-2 after the fix. CLI bootstrap builds
through build-slot.ps1 are necessary unlisted setup, reported separately. No PCs
run in Code. PC-1..PC-4 and every variant remain pending SourceLanding Mutation.
Restart: none.

Windows inspection found build adoption is Linux-only at this base. Extend the
same seal-time window to native Windows identity/change-time/owner facts, creating
the fresh root with a protected current-owner-only ACL. Preserve portable ledger
admission hashing and sealed-tool authority. Native success/late-write cases are
included in CP-1. Import this table and explicitly set build.properties.UseAppHost
to false: the existing confinement rewrite builds without an apphost on Windows,
but the default no-build driver otherwise attempts to launch a missing exe.
