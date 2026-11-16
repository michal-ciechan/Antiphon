# Group 109: recycle census declaration count drift

Debug `8b491f75`, Linux server2, fetched current master
`52a90a3935f1f8ed4198200ce3a5e21ca8dab370`. Windows qualification
`160200cb` on rc/2026-11-22 at
`a71c45ab4d5703d480f4eee4c4e30adab45bc4f9` reported one failure among
48 `CheckpointImportTests` results: C1098 expected 20 declarations and found 19.
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
