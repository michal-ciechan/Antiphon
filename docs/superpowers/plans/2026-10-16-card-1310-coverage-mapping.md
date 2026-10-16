# CARD-1310 coverage mapping custody repair

Original Code / landing owner: a5c09b23-f3a1-4956-a4bd-221d9bbf7628.
Base: 6e23e0b4c8d6b2ae12218c2c0058593dbf773530.

Round 3 continuation (Code e9f573b2) runs the exact new regression class and
existing checkpoint custody classes. The caller explicitly excludes the whole
Unit lane (CARD-1287); CP-4/R-2 must be reported NOT RUN. Generic creation
records remain append-only; sealed tool-copy authority remains separate. A mapping
append may gain a separate, process-local build seal only for the same file identity,
unchanged initial prefix, observed build exit, and unchanged final bytes. Other
files, replacement mappings, links, tracked paths and post-seal writes stay refused.

## Verification design

- V-1: mapping appended after its creation record is accepted at the build seal and removed.
- V-2: unrelated changed, added, replaced, linked, hard-linked or tracked paths refuse.
- V-3: mapping rewrite/replacement, late write, absent exit, post-seal write and reseal refuse.
- R-1: existing generic custody, native output cleanup and durable tool authority retain their oracles.
- V-4: Windows files created inside the build window with copied old change/write times are adopted.
- V-5: Windows files created before the root remain refused even with recent change/write times.
- V-6: a 65 MiB output is adopted and checked with bounded allocation; a tail rewrite is detected.
- V-7: an opened handle above the 1 GiB output ceiling is refused before streaming.
- V-8: the confinement ledger accepts an unchanged 65 MiB output and detects a tail rewrite.
- R-3: confinement ledger and existing receipt/handle reader contracts remain enforced.
- M-1 (required, round 3 brief): one small E2E case and both receipt validators.
- R-2: whole Unit lane NOT RUN by explicit continuation brief (CARD-1287).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1310/` | mapping | `/*/*/(CheckpointCoverageMappingTests*)\|(CheckpointCoverageMappingNativeTests*)/*` | V-1,V-2,V-3,V-4,V-5,V-6,V-7 | all 21 results, 0 failed/skipped | 21 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | custody | `/*/*/(OutputCleanupTests*)\|(CheckpointOwnedOutputTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointAuthorityTests*)/*` | R-1 | all available Windows results, report existing platform skips separately | 65 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `tests/Antiphon.E2E -> bin-c1310-e2e/` | e2e-receipt | `/*/*/OutputDistillationCanaryGuardTests/Configuration_refuses_unowned_resources_before_start` | M-1 | all 12 results, 0 failed/skipped; both validators accept | 12 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | CP-1 | streaming-ledger | `/*/*/(CheckpointConfinementLedgerTests*)\|(HostCleanupCheckpointReceiptTests*)\|(PlanCoverageHandleTests*)/*` | V-8,R-3 | all available Windows results; report existing skips separately | 1 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

Run CP-1 red before the fix, then CP-1/CP-2 after the fix. CLI bootstrap builds
through build-slot.ps1 are necessary unlisted setup, reported separately. No PCs
run in Code. Every PC and variant remains pending SourceLanding Mutation.
Restart: none.

CP-4 (whole Unit lane, R-2) is NOT RUN under the explicit continuation scope.
Before streaming repair, the retained E2E output was audited through production
OutputCustody.Refusal, SafeWrite.Preflight and ReceiptFile.ReadBytes: all 2,673
entries (2,376 files), exactly two size refusals, both 93,580,104-byte node.exe
copies (.playwright/node/win32_x64/node.exe and fakegrok/.playwright/node/win32_x64/node.exe).
No time-window, owner/ACL, link, tracked-path or other refusal was found. The
unpersisted exit instant was bracketed by build-log completion and build-stamp
creation; no entry had an ambiguous result. Receipt limits remain unchanged;
output hashing gains a separate 1 GiB hard ceiling and bounded streaming memory.
The first red attempt executed zero tests: the confinement ledger independently
used the same capped reader and marked those retained outputs unreadable/tampered.
CP-5 covers the additional ledger hash consumer and the shared handle-read loop;
it is a bounded affected-class check, not the excluded Unit lane. Red-first proof
uses a detached checkout of committed test source so retained outputs do not
prevent admission. No confinement guard or output bytes are bypassed or changed.

Round 3 changes only the Windows lower bound to entry creation time. Change time
and write time must still be at or before the observed build exit. Linux retains
its change-time lower bound. Run CP-3 separately from known-red CP-2 so its cleanup
and both receipt validators can be qualified independently. CP-1 now has the
original 17 cases plus two native creation-time regressions. The pre-root case
uses recent change/write times, isolating the creation-time rejection.

Windows inspection found build adoption is Linux-only at this base. Extend the
same seal-time window to native Windows identity/change-time/owner facts, creating
the fresh root with a protected current-owner-only ACL. Preserve portable ledger
admission hashing and sealed-tool authority. Native success/late-write cases are
included in CP-1. Import this table and explicitly set build.properties.UseAppHost
to false: the existing confinement rewrite builds without an apphost on Windows,
but the default no-build driver otherwise attempts to launch a missing exe.
