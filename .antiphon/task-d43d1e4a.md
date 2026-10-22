# Review d43d1e4a: CARD-1409 repair 7 (Final)

Subject: Code 18ac0f5a-6638-401b-9799-3437c2099980, `feat/card-task-18ac0f5a` at `f8f3233f3cddce69cb4783e0de73565de7a5108d`.

**Outcome: found.** Repair 7 fixes R1. The R1 witnesses refuse at the tip, a value whose only part is a reference behaves as it did at fd20e8dcc, CP-1 is 83/83 and validated, and the mutation turns red. One more regression from repair 6 against fd20e8dcc survives repair 7 (R2 below). It falls in the same category as R1.

## R2 (regression from repair 6, still present at the tip): a later value part that starts with a directory property is skipped

- **Where:** `scripts/audit-land-verification-graph.ps1`.
  - `Resolve-ItemPathText` in `$DirectoryPropertiesOnly` mode, around line 813. It expands `$(MSBuildThisFileDirectory)` and `$(MSBuildProjectDirectory)` only at the start of the whole text. Any later `;`-part that contains `$(` gets `Kind = 'unresolved'`.
  - `Assert-PropertyValuesAllowed`, around line 1404. It runs `if ([string]$spec.Kind -ne 'repo') { continue }`. Repair 7's new comment states this skip: "and so is a part the pipeline returns unresolved".
- **Failure:** on the real `tests/Antiphon.Tests/Antiphon.Tests.csproj` in a scratch worktree, I added `<PackageReadmeFile>x.json;$(MSBuildThisFileDirectory)../../docs/orchestration-loop.md</PackageReadmeFile>` and ran `-Update`:

  | Script | Result |
  |---|---|
  | f8f3233f3 | approves, `count=57` |
  | fd20e8dcc | refuses, `PackageReadmeFile -> docs/orchestration-loop.md` |

  The `$(MSBuildProjectDirectory)/../../README.md` spelling behaves the same way. In the PackageTags fixture matrix, `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md` and `@(None);$(MSBuildThisFileDirectory)../docs/guide.md` are approved at f8f3233f3 and at ad13d6cd3, and refused at fd20e8dcc.
- **Why:** MSBuild expands a directory property in every part, and fd20e8dcc stripped it from every part. Repair 6 made the expansion apply only to the leading part, so a docs path that a directory property introduces in any later part is approved. D-25 says a part that is a literal path "after the two directory properties" is classified. This is a regression of existing behaviour at fd20e8dcc, the same kind as R1. Review 7db88932 noted only the Import side of this repair-6 change. For Imports the change is fail-closed: an unresolved part refuses. For property values it is fail-open.
- **Fix:**
  - In the `$DirectoryPropertiesOnly` branch, expand a leading directory property in each part after the `;` split, not only at the start of the whole text. Each part then resolves from its own root, which is also correct MSBuild for Imports.
  - Add a red witness that refuses `src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md` and its `$(MSBuildProjectDirectory)/` spelling, naming `docs/guide.md`.
  - Correct the repair 7 comment.
  - Rerun real-repo `-Check`, which must print F240D0F1...B72F count=57, and run CP-1 with the new count.
- **Landing owner:** Code 18ac0f5a.

## Brief checks

1. **R1 witnesses at f8f3233f3:**
   - On the real test project, `../../docs/guide.md;@(None)` refuses with `PackageReadmeFile -> docs/guide.md`.
   - In the fixture matrix, `;@(None)`, `;$([System.String]::Copy(''))` and `;%(None.Identity)` beside a docs part each refuse naming `docs/guide.md`. The docs part can come first or last.
   - Values whose only parts are references (`@(None);%(None.Identity)`) are approved, with digests byte-identical to fd20e8dcc.
2. **Script diff ad13d6cd3..f8f3233f3:** three hunks.
   - The header comment and the inline comment change only comments.
   - The functional hunk moves the whole-value `@(`/`%(`/`$([` test into the spec loop as a test on `$spec.Source`. In this mode such a part is already `unresolved`, so the new line is redundant but harmless. It restores the per-part semantics of fd20e8dcc for item, metadata and function references.
   - The test diff adds one witness, which has outcome assertions. The doc diff is wording only.
   - Real-repo `-Check`: `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0.
   - An ordinary edit passes `-Update` with count=57. The edit added a PackageReference, a linked Compile, a ProjectReference with ReferenceOutputAssembly=false, a `data%20files` None, and a prose Description containing `/` and `docs/foo`.
   - The script has 0 non-ASCII bytes and ParseFile reports 0 errors. The added lines contain no `??`, `?.`, `&&` or `||`.
3. **Mixed-part matrix:** 18 values, each run under f8f3233f3, ad13d6cd3 and fd20e8dcc.
   - Parts carrying an item, metadata or function reference never hide another part. That covers the docs part first, middle or last, two reference parts, a `..%2F` escaped docs part, and `docs/README.md`.
   - An escaped `%3B` keeps the text as one part (skipped), which matches all three versions.
   - The only divergence from fd20e8dcc is R2.
4. **Completeness test and count:** `C1409_NormaliserAndItemOperationTablesHaveWitnesses` asserts `rows.Count > 0` and checks the plan rows against the witnesses in both directions. It runs every witness, so it cannot pass vacuously. The count of 83 is 27 `C1409_` + 10 `C1400_` + 3 `C1390_` + 1 `C1385_RegistryMatchesNarrowGraph` (41 methods, counted by awk) + 42 `C1380_` cases, which matches `executed=83`.

## Receipts

```
CHECKPOINT CP-1 commit=f8f3233f3cddce69cb4783e0de73565de7a5108d build=ok filter=/*/*/LandNonCodeSkipTests/(C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=83 passed=83 failed=0 skipped=0 trx=/work/worktrees/task-d43d1e4a/.antiphon/checkpoints/20261022-113618-8583/rows/CP-1/run.trx slot=granted waited=210s dirty=0 source=f8f3233f3cddce69cb4783e0de73565de7a5108d sourceState=clean buildSource=verified
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=f8f3233f3cddce69cb4783e0de73565de7a5108d rows=1
```

- **CP-1 run:** `20261022-113618-8583` on server2, with no runner or platform pin. Wall time 22m47s. `unlisted: none`.
- **Validation:** `Antiphon.Checkpoints validate` and `scripts/validate-checkpoint-receipt.ps1` both exit 0. The tree is clean.
- **Unlisted runs, with reasons:**
  - The checkpoint tool build, a gated `dotnet build` (needed to run the tool).
  - The mutation run, gated: `dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*/LandNonCodeSkipTests/C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart`. It ran in a discarded scratch worktree at f8f3233f3 with the whole-value skip restored. Result: total 1, failed 1, with `ran.Code should be 1 but was 0` and `count=25`. This was the throwaway mutation the brief required.
- **Script-level runs:** the matrix and real-tree probes were pwsh script runs only, in scratch directories.
- **merge-tree:** `git merge-tree --write-tree origin/master f8f3233f3` against `08765c68cdd05f4c80be092e06d39abe704c814e` exits 0.
- **rc cherry-pick:** the 33 commits `9b39df4e0..f8f3233f3`, picked with `-x` onto `origin/rc/2026-10-20` (`ceb6e3d6b`), all apply. The script, the test class and `orchestration-loop.md` are byte-identical to the branch. The scratch worktree was removed.
- **Evidence diff:** `check-evidence-diff.ps1 -Repository . -BaseRef 9b39df4e0 -HeadRef f8f3233f3` reports commits=33 entries=4 violations=0.
- **Cleanup:** the `bin-rvd43` directories were deleted (under `tools/Antiphon.Checkpoints` and `src/Antiphon.HostCleanup`). The checkpoint tool deleted `bin-c1409/`.
- **PCs:** all remain pending.

Disclosure, unchanged: F1, the well-known metadata class, is tracked as CARD-1483.
