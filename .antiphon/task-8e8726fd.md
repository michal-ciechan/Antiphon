# Review 8e8726fd: Final Review of Code 8e3c334d (CARD-1409 repair 6)

Outcome: found. Repair 6 closes the percent-escape class against real MSBuild and CP-1 is green (82/82). One new in-contract bypass class remains, proven against real MSBuild: **well-known item metadata read as empty**. It predates repair 6 (it also passes at `fd20e8dcc`), so it is a missed class, not a regression.

Subject: `feat/card-task-8e3c334d` at `ad13d6cd36018d2eaedc39b80f99d5612fefd27c` (tested source `3245dee42` plus a report-only commit: `git diff --stat 3245dee42 ad13d6cd3` shows only `.antiphon/task-8e3c334d.md`). Landing owner: Code 8e3c334d-0435-489e-9492-90d829ddd8ea.

## F1: well-known metadata in a batched `%(Item.Meta)` property value is read as the empty value

- **Where:** `scripts/audit-land-verification-graph.ps1:700`, the `%(Item.Metadata)` branch of `Expand-Candidates`. `$value` stays `''` when the name is not in `$declaration.Metadata`.
- **Failure (witness, real tree):** append this to `tests/Antiphon.Tests/Antiphon.Tests.csproj` at `ad13d6cd3` and commit in a scratch worktree:
  ```xml
  <Target Name="C1409Probe"><ItemGroup><_StagedHelperFiles Include="../../global.json" /></ItemGroup><PropertyGroup><UsePublishedPackages>%(_StagedHelperFiles.RelativeDir)</UsePublishedPackages></PropertyGroup><ItemGroup><None Include="$(UsePublishedPackages)docs/data.json" /></ItemGroup></Target>
  ```
  `audit-land-verification-graph.ps1 -Update` exits 0 with `count=57`. Real MSBuild (`dotnet msbuild <csproj> -t:C1409Probe -restore:false`, build-slot gated) puts `$(UsePublishedPackages)` = `../../` and `None` = `<repo>/docs/data.json`. The second variant uses the unchecked `text` item: `<_AntiphonRepeatLine Include="../../README.md" />` plus `<PackageTags>%(_AntiphonRepeatLine.Identity)</PackageTags>` and `<None Include="$(PackageTags)" />` inside a target. `-Update` exits 0, and MSBuild's `None` is `<repo>/README.md`. Both variants are also approved by the repair-5 script at `fd20e8dcc`.
- **Why:** MSBuild computes well-known metadata from the item's evaluated include: Identity, FullPath, RootDir, Directory, RelativeDir, Filename, Extension, RecursiveDir, DefiningProject*, and the times. None of them is ever a declared value. The audit reads such a name as an undeclared, empty value. It neither expands it from the declaration's Include nor refuses it as unresolved. This is in contract: the plan resolves `%(Item.Metadata)` of declared items (D-21 and repairs 4 and 5), refuses an unresolvable reference, and treats `%(Docs.FullPath)` in an Include as unclassified (V-18). Well-known metadata is not in the D-18/D-27/D-32/D-35 residue list.
- **Fix:** in the batch branch, refuse a well-known metadata name with `Set-Unresolved`, which is the cheap and safe choice. The alternative is to derive it from each declaration's Include through `Resolve-ItemPathText`. Add a V-row/C1409 witness for both variants (path item RelativeDir, text item Identity) and move CP-1 expect to the new count.

## Repair 6 checks (all pass)

1. **c64987ea witnesses.**
   - The real-tree `PackageTags=$(RootNamespace)/%2E%2E/%2E%2E/%2E%2E/README.md` + `None Include="$(PackageTags)"` refuses: `tests/Antiphon.Tests/Antiphon.Tests.csproj None Include=$(PackageTags) -> README.md`.
   - The `%64ocs` fixture refuses inside `C1409_EscapeInExpandedTextIsDecodedOnce`, which passed in CP-1.
2. **Real MSBuild probes.** Scratch `p.proj`, `dotnet msbuild -getItem/-getProperty`, build-slot leases `010fdb16`, `ccf473b2`, `e06154ce`. MSBuild's results:
   - `%252E%252E/README.md` gives `%2E%2E/README.md`.
   - `a%3Bb` gives the one item `a;b`.
   - `%2A.md` gives the literal `*.md`, and `%24(Pct)` gives the literal `$(Pct)`.
   - `x%00y` keeps a NUL.
   - `a%X`, `b%GG` and `c%2` stay literal.
   - Nested `$(Pct)2E...` with `Pct=%25` gives `%2E%2E/...`, a single decode. The bare-`%` variant gives `../../README.md`.
   - `Dbl=%252E...` gives `%2E%2E/...`.
   - The metadata `Meta="%2E%2E/%2E%2E/README.md"` through `%(M.Meta)` gives `../../README.md`.
   - A file-reading argument `$(OutDir)%2E%2E/%2E%2E/README.md` is decoded once and climbs: MSBuild read the root README. The audit's tail rule refuses it ('climbs out').
   - `%3B` in a function argument does not split.

   No difference from the audit pipeline. Escaped `*`/`?` is over-approximated as a glob, which refuses or drops the Exclude, so it errs in the safe direction.
3. **One decode call site:** `ConvertFrom-PercentEncoding` at `:847`, inside `Resolve-ItemPathText`. The one path `;` split is at `:834`. The only other `.Split(';')` is the project `Sdk` attribute name list at `:1149`, which is not a path. Include, Update, Exclude, Import Project, ProjectReference, name items, Pkg* ids, literal property values and output/file-reading tails all consume pipeline specs. Remove is left alone because it only subtracts.
4. **Real-repo and ordinary edits.**
   - `-Check` prints `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57` and exits 0.
   - The ordinary edits (`PackageReference Humanizer.Core`, linked `Compile ..\..\server\Program.cs`, `ProjectReference ... ReferenceOutputAssembly="false"`) pass `-Update` with count=57.
5. **Script and table test.** The script has 0 non-ASCII bytes and `Parser::ParseFile` reports 0 errors. The table test fails on zero rows, on a row without a witness and on a witness without a row, so it cannot pass vacuously.
6. **Residue in the Code report:** task parameters, Condition, env-var properties (including the SDK-owned `$(CleanFile)` read as opaque), reserved device names, symlink-as-text, and the `Include=` label on Update path refusals. All are acceptable disclosure under the plan text. F1 is not part of that residue.

## Receipts

```
CHECKPOINT CP-1 commit=ad13d6cd36018d2eaedc39b80f99d5612fefd27c build=ok filter=/*/*/LandNonCodeSkipTests/(C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=82 passed=82 failed=0 skipped=0 trx=/work/worktrees/task-8e8726fd/.antiphon/checkpoints/20261022-091715-4d36/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=ad13d6cd36018d2eaedc39b80f99d5612fefd27c sourceState=clean buildSource=verified
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=ad13d6cd36018d2eaedc39b80f99d5612fefd27c rows=1
```

- **CP-1 run:** `20261022-091715-4d36` on server2, with no `-Runner`/`-Platform` pin. It ran at the reviewed tip `ad13d6cd3`, not `3245dee42`: the tool requires HEAD to equal the expected SHA, and the two commits differ by the report file only. `Antiphon.Checkpoints validate` and `scripts/validate-checkpoint-receipt.ps1` both exit 0. Unlisted runs: none by the tool.
- **Mutation:** in a scratch worktree I replaced the one decode with `$decoded = $part`, built `tests/Antiphon.Tests` to `bin-c1409rvm/` (lease `7fb8e29d`, 252 s) and ran it (lease `286434e8`). Total 2, failed 2: `C1409_EscapeInExpandedTextIsDecodedOnce` and `C1409_NormaliserAndItemOperationTablesHaveWitnesses` (table row N-17). The worktree was discarded.
- **merge-tree:** `git merge-tree --write-tree origin/master HEAD` against `b59706530` (contains `7febbb891`) exits 0.
- **rc cherry-pick:** cherry-picking the 29 commits `9b39df4e0..ad13d6cd3` with `-x` onto `origin/rc/2026-10-20` (`ceb6e3d6b`) exits 0. The script, the test class and `docs/orchestration-loop.md` are byte-identical to the branch. The scratch worktree was removed and nothing was pushed.
- **Evidence diff:** `check-evidence-diff.ps1 -Repository . -BaseRef 9b39df4e0 -HeadRef ad13d6cd3` reports commits=29 entries=3 violations=0.

Unlisted drivers, each build-slot gated, with their reasons:
- the tool build `bin-c1409rvt/`, needed to run the checkpoint tool;
- seven scratch `dotnet msbuild` probes, MSBuild ground truth as the brief requires;
- the mutation build and run, which the brief requires.

All `bin-c1409rv*` directories have been deleted.
