# Linux reds on master: stale derived index and nightly core goldens

Code task `e5626886`, base master `0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8`. Branch `feat/card-task-e5626886`. Short Final. No deploy and no restart. Windows behaviour of these two rows is UNOBSERVED from this host.

## Design

1. **Regenerate the committed index.** `tests/derived-impact-index.json` is stale. The builder is built first, then `scripts/regenerate-derived-impact-index.ps1 -NoBuild` is run twice (changed, then unchanged) as its own commit.
2. **Path-separator-neutral diagnostic golden.** `Test-NCGolden -NeutralPathSeparators` folds only the `<root>\\` placeholder to `<root>/` on both sides. The four diagnostic filenames and every other character stay in the comparison. The golden file is not rewritten.
3. **Ordinal tie-break for the latest diagnostic log.** `NightlyCoverageParts.LatestDiagnosticLog` keeps the newest `.diag`, else the newest file, by `LastWriteTimeUtc`. Equal timestamps take the ordinally first full path. The removed body at `95d43ee27da2` listed with `Get-ChildItem` (NTFS name order, Hidden and System omitted) and `Sort-Object LastWriteTimeUtc -Descending`, which keeps that order, so `a.diag` wins a tie with `b.diag`. Hidden and System are still skipped.
4. **Hidden assertion follows the attribute.** The coverage golden still contains `hidden=a.diag`. When `SetAttributes(Hidden)` persists, that row is compared. When it does not (Linux leaves Attributes Normal), the case is omitted from the live payload, that one golden field is dropped from the comparison, and the harness prints the skip reason. The Windows witness string stays required in the golden file.

## Verification design

- V-1. `NC CoreMatchesFallback.diagnostics` matches the diagnostic golden after separator folding (`NightlyCoreTests.CoreMatchesFallback`, harness `CoreMatchesFallback`).
- V-2. `NC CoreMatchesFallback.coverageParts` matches, including `diagTie=a.diag`. The hidden row is compared where the attribute persists and skipped with a stated reason where it does not.
- R-1. `DerivedImpactIndexTests` passes, including `committed_index_matches_the_builder`.
- R-2. The guard union passes. `C1729LayeringGuardTests` stays at 0 violations and 162 listed. `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux.
- R-3. `scripts/audit-land-verification-graph.ps1 -Check` approves, `scripts/check-evidence-diff.ps1` over base..HEAD exits 0, and `git merge-tree --write-tree origin/master HEAD` exits 0.
- R-4. Nightly `policyHash` and `PinnedInputDigest` are recomputed and reported outside this table. The print exits 0 whether or not the value matches. Re-pin only if a protected input changes.

No PC-n is named. Mutation execution is waived (operator 2026-11-12) and is not discharged.

Red confirmation at base `0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8` is the unlisted run before this fix. `committed_index_matches_the_builder` failed at `DerivedImpactIndexTests.cs:177` (stdout `windowsOnly count=31 linuxOnly count=1`; load 17.52, above the start gate, lease `685b2561-adbd-4b5b-ac58-753d37fcc456`). `NightlyCoreTests` was 33 passed, 2 failed, 35 rows (load 9.67, lease `53ae1edc-d3d4-4db5-9c64-43d117a3dc41`): diagnostics golden False actual 2863 chars with compiled and fallback both 317141; coverageParts `diagTie=b.diag` and `hidden=h.diag` against `diagTie=a.diag` and `hidden=a.diag`. It is not a row here because this plan closes the fix.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-nc-linux/ | nightly-core | `/*/*/NightlyCoreTests/*` | V-1, V-2 | 1 case, 0 failed | 1 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0;TEMP=/tmp;TMP=/tmp |
| CP-2 | S1 | CP-1 | derived-alone | `/*/*/DerivedImpactIndexTests/*` | R-1 | 27 cases, 0 failed | 27 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0;TEMP=/tmp;TMP=/tmp |
| CP-3 | S1 | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-2 | 113 passed plus the Linux skip of C1210_Windows_linux_only_outcomes_are_exact; 0 failed | 113 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0;TEMP=/tmp;TMP=/tmp |
| CP-4 | S1 | n/a | nightly-harness | `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case CoreMatchesFallback` | V-1, V-2 | exit 0; 35 passed, 0 failed | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0;TEMP=/tmp;TMP=/tmp |
| CP-5 | S1 | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-3 | exit 0, approved | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0;TEMP=/tmp;TMP=/tmp |
| CP-6 | S1 | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 0822e8bbc60f2c8f333504d60cf3484cc7b6f2b8 -HeadRef HEAD` | R-3 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | S1 | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-3 | exit 0 and a tree id | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
