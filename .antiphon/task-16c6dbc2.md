# Review 16c6dbc2: CARD-1836 S3a (round 1, Final)

Subject: origin/feat/card-task-3ca2caa6, reviewed tip 553dd32f60589ad1794729785411de1d213a4096, base cc48c70050a2932eddf4abe18cea8659d478c33e (7 commits).
Plan: docs/superpowers/plans/2026-11-13-card-1836-s3a-checkpoints.md (on the subject branch).

Verdict: BLOCKER D-1 (multi-owner / committed `*` consumer rule has no witness that can go red). Code is otherwise conservative and inert.

## Checkpoint evidence (run 20261113-020751-3057, at 553dd32f6, sourceState=clean buildSource=verified)

| CP | executed | passed | failed | skipped | note |
|---|---:|---:|---:|---:|---|
| CP-2 RcInputManifestTests | 4 | 4 | 0 | 0 | |
| CP-90 guard union (10 classes) | 135 | 135 | 0 | 0 | |
| CP-91 SessionRunner ProcessSpawnLimitTests | 2 | 2 | 0 | 0 | |
| CP-92 Pty ProcessSpawnLimitTests | 2 | 2 | 0 | 0 | |
| CP-93 DerivedImpactIndexTests | 27 | 26 | 1 | 0 | `regenerate_script_reports_changed_then_unchanged`: driver dll not built (environment precondition, not in Antiphon.sln build) |

CP-93 failed method rerun after a gated build of scripts/regenerate-derived-impact-index (plan-permitted generator bootstrap): run 20261113-024932-e62f, executed=1 passed=1 failed=0 skipped=0, sourceState=clean buildSource=verified, verdict GREEN, `validate` CHECKPOINT SOURCE VALID rows=1 at 553dd32f6.
The whole-run verdict is RED because of the precondition, so no validated receipt exists for this run.

## Other gates

- check-evidence-diff cc48c7005..553dd32f6: commits=7 entries=0 violations=0, exit 0.
- merge-tree origin/master 586be574b vs 553dd32f6: clean, tree 54220a7637061e938a7470322d000fde6d873cdf.
- Inert: `git grep` finds rc-input-manifest / Get-RcInputManifest / rc-input-owners only in the new lib, its harness and its TUnit wrapper. No qualification launch file or -SelectionMode Full path changed (diff touches only the 6 listed files).

## Safety analysis

1. Unknown is dirty: every tracked path not owned by any suite becomes `unknown-path` and broadens all suites (line 188, 207, 259). Non-regular modes, `.gitattributes`, case collisions, dynamic MSBuild (Target/UsingTask/Sdk/MSBuild), `$()` expressions, missing imports, map change, non-ancestor baseline all broaden to all-dirty. Correct direction.
2. Conditions are ignored and Update/Remove never subtract; ancestor Directory.Build.* are all included. Conservative.
3. Index: positive edges only add records and dirty marks; unknown consumers or invalid paths broaden to all. No code path removes a dirty mark.
4. Replay (synthetic commits = real tree + map, chained so ancestry holds):
   - Committed `**`/`*` map: every suite owns every path, plus 3 `.gitattributes` in the repo force `external-content-contract` -> all 27 suites dirty on both diffs. The full replay with the committed map did not finish within 9 minutes (performance advisory A-1).
   - Closure-only map (roots=[], broadening disabled to list ownership): 3db1a43b0..56e96eabf: 91 changed, 40 changed paths unowned by MSBuild closure (scripts invoked by tests, policy JSONs, docs, Antiphon.sln, tests/Shared, fixtures); 56e96eabf..cc48c7005: 49 changed, 24 unowned. Each unowned path triggers dirty-all. Antiphon.Tests (holding SessionMessageQueueServiceTests and ReleaseSelectionCliTests) is dirty on both diffs and its inventory contains both test files. Unresolved also includes dynamic-build=11, so the real repo is always all-dirty at S3a.
5. Scratch mutants (git-archive copy, pwsh harness, restored and diff-checked):
   - M1 drop unknown-path rule: DynamicAndUnownedAreDirty RED (`unknown-path-all-dirty`). Killed.
   - M3 index edge does not union input record: PartialIndexNeverSubtracts RED (`positive-index-unions-input-record`). Killed.
   - M3b positive index replaces the dirty set: PartialIndexNeverSubtracts RED (`partial-index-keeps-conservative-consumers`, `index-cannot-subtract:*`). Killed.
   - M2 each root keeps only its first consumer: ALL FOUR cases PASS. SURVIVED.
   - M2b `*` consumer resolves to only the first suite: ALL FOUR cases PASS. SURVIVED.
   - Control: with ProductAndHelperFanout reordered so tests/Shared/Helper.cs is the first change, pristine passes and M2 goes RED (`fanout:tests/Shared/Helper.cs`), proving the existing assertion is masked.

## D-1 (blocker): multi-owner rule has no witness; committed `*` form untested

- Where: scripts/test-rc-input-manifest.ps1:71-76 (ProductAndHelperFanout) and the absence of any case over the committed scripts/lib/rc-input-owners.json; the rule lives at scripts/lib/rc-input-manifest.ps1:109-114.
- Failure: if a root's consumer list (or `*`) resolves to fewer suites than declared, a shared helper or any `**`-owned path is owned by one suite only and is no longer `unknown-path`; every other suite reads it as clean. With the committed map (`"**"` -> `["*"]`) that is 26 of 27 suites silently clean for any non-closure input (scripts, policy JSON, fixtures, docs). M2 and M2b both survive all four cases.
- Why: the Helper change is compared against the fixed baseline after Queue.cs already changed, so `runner` is dirty through its ProjectReference to Core regardless of Helper ownership; `fanout:tests/Shared/Helper.cs` cannot go red. No case asserts that a `*` consumer covers every suite, and the real committed map is never loaded by a test. In a safety slice whose purpose is "unknown/multi-owner stays dirty", the multi-owner rule must be witnessed.
- Fix: make each fanout change independent (commit each path on a fresh branch from baseline, or compare against the previous commit) so Helper alone must dirty `runner` and `server` and leave `other`; add a case where a `*` root owns a path no closure reaches and assert every suite is dirty and holds the record; add a case that loads the committed scripts/lib/rc-input-owners.json from the tip and asserts every discovered suite owns every tracked path. Show each new assertion red under M2/M2b, then green.

## Advisories (cards, not land holds)

- A-1 performance: Get-RcInputManifest over the real repo (10,516 paths x 27 suites with the committed `**` map) did not finish in 9 minutes; closure-only one-side inventory took 155-199 s. S3b must budget or index this before any activation.
- A-2 the real repo is permanently all-dirty at S3a: 3 tracked `.gitattributes` (external-content-contract) and 11 dynamic-build projects. Expected for shadow-only, but S3b needs a declared allowlist for these before the inventory can ever mark a suite clean.
- A-3 `Reference/HintPath` and custom item types (e.g. Protobuf) are not parsed; with a scoped map, a file owned by another suite and consumed that way would be a false negative. Inert under the committed all-inputs map.
- A-4 suites are discovered only under tests/**/*.csproj; consumers of the inventory must treat a suite absent from both snapshots as dirty.
