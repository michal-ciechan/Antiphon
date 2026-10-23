# CARD-1539 S2 provider execution selection

Reuses S1 from 83a357afb2dccf3cdaeddf17b8cd849cff8d2cee. This task adds the
Windows observation provider first, per the parent slice order. Fixture/checkpoint
wiring and extraction of the existing redactor facades remain separate work;
existing facades stay intact to avoid CARD-1531's in-flight checkpoint area.

The task brief authorizes minimal verification in place of the Final Unit sweep.
CP-1 is a compile-capable unavailable Windows provider with two independent
assertion failures, before implementation. CP-2 is its ordinary green selection.
CP-3 checks adopted S1 in the same frozen build; no stress repetitions.
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
