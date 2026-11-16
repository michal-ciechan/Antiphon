# Group 109: recycle census declaration count drift

Debug `8b491f75`, Linux server2, fetched current master
`52a90a3935f1f8ed4198200ce3a5e21ca8dab370`. Windows qualification
`160200cb` on rc/2026-11-22 at
`a71c45ab4d5703d480f4eee4c4e30adab45bc4f9` reported one failure in the
48-result qualification group: C1098 expected 20 declarations and found 19.
The test, wrapper source and CARD-1087 plan have no differences between that
rc cut and this master.

## Introducing change and repair

CARD-1901 commit `c2ef37839b97eb5b6845dc7b0c22d07a5c7b47a0`
(original `05e34c2396dd969085de00951b537d45bef96a0b`) replaced
`C1008_Legacy_rolling_and_jq_rosters_remain`, one async parameterized method,
with four ordinary Task-returning methods ending in `present`, `absent`,
`missing_shell` and `failing_shell`. Each still executes its complete existing
roster. No test execution disappeared. The C1098 regex admitted only
`public async Task` and `public void`, so it lost the old declaration and
ignored all four replacements: 20 - 1 = 19 instead of 20 - 1 + 4 = 23.

The repair enumerates public declared methods marked `[Test]` on the compiled
`RollingVolumeRecycleScriptTests` type, filtered to the same two exact prefixes.
This admits both async and ordinary Task-returning declarations and exposes
the type dependency to the derived selector. CP-3b names the four replacements
individually and has an exact 19-method floor; CP-3 remains four, total 23.
Exact set equality, no overlap, serial execution, shared build, separate groups,
14-minute combined estimate, 72-minute manifest estimate and unchanged caps
remain asserted. Historical costs stay recorded; the four roster executions
and their workload are unchanged. The existing failing test supplies the red
witness; no duplicate test or relaxed assertion is needed.

## Why pre-land selection missed it

Merge 242 carried CARD-1901: its combined-tree index commit is
`8f7950a46f56776e474bc85780fcac6762c452a8`. The CARD-1901 retirement-audit
plan includes the four CP-49 roster methods but no `CheckpointImportTests` row.
The current pre-fix derived-index entry for
`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` likewise omits
`CheckpointImportTests`. Reading a source filename and matching its contents
with a regex did not establish a compiled type dependency. Passing wrapper
behavior tests and the ordinary policy guard union therefore missed this
plan/census consumer. Merge 243 and Merge 244 inherited the mismatch; Merge
244's rc cut is not the introducing change. This attribution follows committed
plans and index bytes; unavailable delegate transcripts are not claimed as
execution evidence.

The cheapest pre-land row after building the test project is:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g109-census -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/*/CheckpointImportTests/C1098_card_1087_wrapper_census_row_is_split_under_the_same_cap'
```

Run the impacted selection over the full landing range and execute the selected
consumer. The explicit type reference repairs this missing derived edge without
adding a hand-maintained selector row. A repository search found no other class
reading the CARD-1087 plan or implementing `DeclaredWrapperCensusMethods`.

## Baseline and verification scope

The default-apphost native-output baseline build passed: 0 errors, 2,138 warnings,
355.72 seconds MSBuild time, 360-second lease, zero admission wait. The exact
C1098 row failed deterministically at clean master: 1 total, 0 passed, 1 failed,
0 skipped, exit 2, 274 ms test body, 6.009-second framework run, 11-second lease.
Its assertion was exactly `declared.Length should be 20 but was 19` at line 442.
Raw TRX, logs and opt-in timing records are ignored under
`/work/worktrees/ws-02/.antiphon/task-8b491f75/`. Initial load was
`1.23 1.44 1.28`; before the red row it was `14.47 8.84 4.68`.
These are contended-host samples, not p95. Uninstrumented phases are unknown.

Commissioned verification is the complete `CheckpointImportTests` class, the
checkpoint limiter census `ProcessSpawnLimitTests`, and the supplied eleven-class
guard union. Build the derived-index driver first, regenerate twice (second
unchanged), and verify the regenerated type edge. Rows run serially, each below
ten minutes, with fresh TRX and opt-in phase records. No TestTiming span or
hand-written selector row changes, so the optional label/selector test classes
are not required. No production wrapper behavior changes or live host operations.

The production reader verified policyHash
`c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909` and
protected-input digest
`2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`.
LegacyExclusionDigest is pinned to
`6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`;
the required native-policy guard verifies its census. These edits touch none
of those pin inputs. Mutation checks are waived by operator authority
(2026-11-12). Work proceeds under the supplied standing authority without
another approval request. This test-only repair hands off with `next: merge`.

## Final verification

Every final row ran at clean committed source
`c5715e13c3512551b85572217893b3ce54bf9790`, using the native default output
and default apphost. Each build and row held its own granted build-slot lease;
all admission waits were zero. Source stayed frozen during all builds and rows.
The repaired-source build passed with 0 errors and 2,138 warnings in 438.36
seconds MSBuild time (441-second lease). The driver prerequisite build passed
with 0 errors and 0 warnings in 6.25 seconds (9-second lease).

| Final row | Total | Passed | Failed | Skipped | Framework time | Lease time |
|---|---:|---:|---:|---:|---:|---:|
| CheckpointImportTests, complete class | 34 | 34 | 0 | 0 | 6.423s | 11s |
| ProcessSpawnLimitTests, complete class | 57 | 57 | 0 | 0 | 8.809s | 13s |
| Required eleven-class guard union | 115 | 114 | 0 | 1 | 207.036s | 211s |

The repaired C1098 method passed in 0.0918759 seconds. There are exactly 34
`[Test]` declarations in `CheckpointImportTests.cs`, and all 34 appear in the
class TRX. The commissioning qualification-group total of 48 is not this
single-class census. Total final results: 206, with 205 passed, zero failed,
one skipped. The skip is
`WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`:
its child skip-roster audit requires Windows placement. No Windows execution
credit is claimed.

Every requested guard class has nonzero TRX results: classification policy 42,
classification guard 1, slow registry 5, slow tripwire 2, native Linux policy 3,
derived index 27, mechanical contract 11, platform guidance 5, layering 11,
lane category 1, Windows admission 7. Layering reports:

```text
VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3
```

Driver regeneration first reported `derived-impact-index changed` (33-second
lease), then `derived-impact-index unchanged` (31-second lease). The generated
delta adds only `CheckpointImportTests` to twelve existing edges, including the
wrapper source edge (now 15 classes). No hand-written selector row changes.
All three policy pins remain unchanged; the native-policy guard passed 3/3.

The final build began at load `12.59 11.81 6.72`, with a sample reaching
`21.48 18.62 10.91`. Final rows began at `8.65 14.48 10.56`,
`6.66 13.65 10.37`, and `5.63 12.86 10.19` respectively; the guard row ended
at `5.31 9.23 9.27`. These sparse samples do not establish a p95. TRX retains
individual durations and membership; opt-in phase JSONL and source/filter/load
metadata live beside each row under the ignored evidence root above.
Uninstrumented phase attribution remains unknown. All builds and rows finished
below ten minutes, serially.

Pushed source slices:

- `5c50e70f556cee30237eddb07788e1cb031ae442`: census repair, exact plan update,
  and initial why-missed investigation.
- `c5715e13c3512551b85572217893b3ce54bf9790`: regenerated impact index, in its
  own commit after the driver build and stable second regeneration.

Before this evidence-only appendix, the evidence-history guard reported two
commits, zero evidence entries and zero violations. A fresh master fetch still
resolved to `52a90a3935f1f8ed4198200ce3a5e21ca8dab370`;
`git merge-tree --write-tree origin/master HEAD` exited 0 with tree
`360db2b81f3cd9ec427817e284190f3cf115d1bb`. The caller report records the final
appendix commit and final checks. This appendix changes only this narrative;
the verified code and generated index are unchanged.
