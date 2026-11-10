# CARD-1820 S1 Final Review (round 1 of the PlatformTaxonomy design)

Verdict: clean. Ordinary scope Full. Positive controls stay pending for method-scoped Mutation.

Reviewed source: `cb931f0890126ee1327eb2fc9fe29103e3667fe4` on `origin/feat/card-task-0ddd898d-r4`.
Landing owner: Code task `0ddd898d-0458-4810-b936-e1210feabe82` (Failed only because `feat/card-task-0ddd898d` was not pushed). Land with `-RecoverReviewedSource -ExpectedSourceSha cb931f0890126ee1327eb2fc9fe29103e3667fe4`.
Reviewed range: `a8d830d12f0258a8f133364e58b8de4eb25d77bd..cb931f0890126ee1327eb2fc9fe29103e3667fe4` (21 commits).
Live `origin/master` at review time: `a69789c77865b2db502500ebd26d58f72a8013c0`. The brief named `29e6736ee88b8ee686fea28128fe13056d79828d`; that tip had moved. `git merge-tree --write-tree origin/master HEAD` exited 0, tree `0df7a262f55141f0b9d399dbf3e69fa51c204c11`. No merge was made.

## Checks

Checkpoint run `20261110-182815-10cf`, verdict GREEN, exit 0, host Debian GNU/Linux 12, sourceState=clean, buildSource=verified, dirty=0, slot=granted, unlisted tool commands none. Cold output `bin-c1820-s1/` was deleted by the tool after the run.

- CP-1 `/*/*/(DerivedImpactIndexTests*)|(ImpactedClassesSelectorTests*)/*` executed=64 passed=64 failed=0 skipped=0. Wall 1375.457s (build 470.8s, tests 1359.6s). DerivedImpactIndexTests 735s / 24 tests. ImpactedClassesSelectorTests 623s / 40 tests. Above the plan minimum of 41.
- CP-G nine-class guard batch executed=78 passed=78 failed=0 skipped=0, build reused. Wall 163.005s. TestClassificationPolicyTests 128s / 41 tests. 78 is the documented nine-class count.
- CARD-1809 R4, named extra run after the cold output was deleted. Native `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` through `scripts/build-slot.ps1` (lease `70b17aa8-7c3b-45b5-b861-73e97aafb3eb`, 0 errors, 2m54s), then `dotnet run --no-build` filter `/*/*/ProcessSpawnLimitTests*/*` (lease `b59aeed2-f6f4-4419-9ec2-e52fc944d4cc`). total=57 succeeded=57 failed=0 skipped=0 duration=14.676s. TRX `/tmp/c1820-spawn/spawn.trx`. `DerivedImpactIndexTests` carries `[ParallelLimiter<ProcessSpawnLimit>]`; the selector kind `spawn-limit-rosters` named this class.

`scripts/check-evidence-diff.ps1` over the reviewed range: commits=21 entries=0 violations=0, exit 0.

Downgrade selector count: 0. The migrations kind did not match, so the `MigrateAsync(` / `IMigrator` / `MigrationTestDatabase` / `.Down(` scan added no callers. Impacted rows for the same range: kinds `spawn-limit-rosters,test-lane-category`; classes=38; derivedClasses=36; filters=2; windowsFilters=0; skippedByCap=0; owed=1 (`.gitattributes` count=56). The commissioned round capped execution at CP-1, the nine-class guard batch, and the R4 census. It did not re-run the rest of the derived edges (including `LandGraphEvaluationTests`).

## Lane result

`PlatformTaxonomy.Derive` plus `tests/derived-impact-index-known-os-gated.txt` (31 ordinal names) is what CP-1 executed. Windows-only names leave the Linux filter. Linux-only names stay on the Linux filter and off `windowsFilters`. Undeclared classes stay on the ordinary filter. The committed index matches a rebuild (byte witness in `DerivedImpactIndexTests`). No OS-gated class in that selection was left on the wrong exclusive lane.

## CARD-1809

- R1. `SelectAsync` reads both streams and `WaitForExitAsync` (120s, then kill) before any assertion. `s1_replay_matches_declaration_and_index_edges` asserts the last sweep record (`OwedCount` 7 on the leak-sweep path) only after that exit. The loop in `merge_subset_matches_the_hand_selector` locates a byte difference after both processes have exited.
- R2. Selector owed text is compared after `Replace("\r\n", "\n")`. Index bytes are forced LF (`NewLine = "\n"` plus a trailing LF) and `.gitattributes` marks the index and the override file `eol=lf`. That is the determinism contract the CRLF-source witness pins.
- R3. No production terminal-state change. The range is the plan, the selector script, the builder, its tests, the index, the override list, and `.gitattributes`.
- R4. Counts above.
- R5. No repeated passes-alone red.

No production `OperatingSystem` / `IsWindows` branch in the script diff. No session waits for input. Parking does not apply.

## Advisory

The checkpoint tool wall was 35m30s (execution 2130s, one build). CP-1 alone was 22.9 minutes, under the 30-minute row trigger. The plan's 60-second rebuild budget is covered by the passing committed-index witness inside that row. This does not hold the land.

Plan CP-2 through CP-5 are later slices and were not in this range.

PCs remain pending.
