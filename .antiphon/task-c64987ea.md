# Review c64987ea: CARD-1409 repair 5 (Code e295bb13), Final

Outcome: one defect, a new in-contract bypass class. F1 and F2 are closed, and the required gate is green: CP-1 81/81, receipt valid, `-Check` F240D0F1/57, ordinary edits pass, the mutation goes red, merge-tree is clean and the rc cherry-pick is clean. The defect: MSBuild unescapes `%XX` in expanded property text, but the audit decodes only the literal include text, so a percent-escaped property value reaches the root README or `docs/` without a refusal.

Subject: `feat/card-task-e295bb13` at `fd20e8dccd3cdbe1f075a6973236dae3728a43a6`. That is a report-only commit on top of the tested source `c2a6398c771ad2979369e12d44fd4c8ec6079a8e`. Land owner: e295bb13-3356-47b9-b725-0804682827b8.

## Defect

### F1: MSBuild escape decoding of expanded property text is not normalised (new class)
- **Where:** `scripts/audit-land-verification-graph.ps1`.
  - `Assert-ItemIncludeAllowed` calls `ConvertFrom-PercentEncoding` once, on the literal include segment, before expansion.
  - `Expand-Candidates` and `Get-PropertyCandidates` then splice raw property values into the candidate. The same raw splice applies to `%(Item.Metadata)` values.
  - `ConvertTo-CanonicalPath` never decodes, so `%2E%2E` stays a plain segment name and `%64ocs` never becomes `docs`.
  - `Assert-PropertyValuesAllowed` does decode, but it skips any part that still contains a `$(` other than the two directory properties.
- **Failure (real tree, scratch detached worktree at fd20e8dcc, committed, discarded):** I appended this to `tests/Antiphon.Tests/Antiphon.Tests.csproj`. Both names are allow-listed, and the property sets no output root.

  ```
  <PropertyGroup><PackageTags>$(RootNamespace)/%2E%2E/%2E%2E/%2E%2E/README.md</PackageTags></PropertyGroup>
  <ItemGroup><None Include="$(PackageTags)" /></ItemGroup>
  ```

  `-Update` returned exit 0 with `count=57`. Then `dotnet msbuild tests/Antiphon.Tests/Antiphon.Tests.csproj -getItem:None`, run under build-slot, lists `"Identity": "Antiphon.Tests/../../../README.md"` with `FullPath` = `<repo>/README.md`, which is the root README.

  I also built a small fixture with `server/App.csproj`, `Description=sub`, `PackageTags=$(Description)/%2E%2E/%2E%2E/README.md` and `PackageReadmeFile=$(Description)/%2E%2E/%2E%2E/%64ocs/data.json`, used as `None` and `EmbeddedResource` includes.
  - `-Update` returned exit 0 with `count=1`.
  - MSBuild resolves those items to `<repo>/README.md` and `<repo>/docs/data.json`.
  - The unescaped control (`..` and `docs`) refuses: `None Include=$(PackageTags) -> README.md`.
- **Why this is in contract:**
  - CARD-1400 (script header) says matching happens "after one %XX decode".
  - D-7 says `%3B` is decoded after the split, "as in MSBuild".
  - D-21 says file items are classified "by what they RESOLVE to, not by spelling".
  - D-28 makes `ConvertTo-CanonicalPath` "the only function that turns resolved text into a repository path".

  MSBuild unescapes the whole item spec after property and metadata expansion, so the text the audit compares is not the path the build reads. This is not a Windows segment rule (N-table) or an item operation (O-table). It is the order in which escapes are evaluated. The same class covers an unescaped `;` inside expanded text, which MSBuild splits after expansion while the audit treats it as an ordinary character. In my probes the trailing-dot rule happened to block the `;..` shapes; the escape shape is what gets through.
- **Fix:** model MSBuild's expansion order. After expanding a candidate from property or metadata values, split it on unescaped `;`, then percent-decode each part once, then canonicalise. Alternatively, refuse any `%` followed by two hex digits in an expanded property or metadata value that reaches a path item. Either way, `-Check` must stay at F240D0F1/57.
- **Test to add:** a C1409 witness for the real-project shape above and the fixture's `%64ocs` shape. Add an N-row, for example N-16 "escape in expanded text", so the table test covers it.

## What holds (brief checks 1-5)
1. **F1/F2 reproductions at fd20e8dcc refuse and name the construct.**
   - `../../README.md.` gives `segment=README.md. (trailing dot or space)`.
   - `../../docs./x.md` gives `segment=docs.`.
   - `../../README.md::$DATA` gives `segment=README.md::$DATA (colon)`.
   - The real-project `StagedHelper Update ... Glob="..\..\..\..\..\docs\**\*.md"` refuses with `_StagedHelperFiles Include=$(_StagedHelperDir)$(_StagedHelperGlob)`.
   - I tested two other ideas and both were already safe:
     - **Win32 `?` semantics** (`../README.md?`, `../README?.md`, `../docs?/guide.md`): MSBuild post-filters these and selects nothing, so there is no gap.
     - **UNC paths and other drives:** these map to `outside`, which is refused.
2. **New class:** F1 above, with one witness.
3. **Acceptance.**
   - `-Check` prints `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0.
   - The ordinary edit (new PackageReference, linked Compile, ProjectReference with `ReferenceOutputAssembly="false"`) passes `-Update` with count=57.
   - Fold mode passes a prose `Description` containing `/` and `docs/guide.md`. It still refuses `PackageReadmeFile ../../docs./guide.md` (`-> docs/guide.md segment=docs.`) and `../../README.md:x`.
   - Includes use the strict mode, so fold mode is not over-permissive.
4. **The table test is not vacuous, and the script is clean.**
   - A plan with zero N/O rows fails (`rows.Count > 0`).
   - A row with no entry fails, and an entry with no row fails.
   - A named row must name an existing `C1409_` method.
   - The script has 0 non-ASCII bytes, and `Parser::ParseFile` reports 0 errors.
5. **Residue I accept as disclosure.**
   - `CON` and `NUL` open devices, not docs files.
   - A `core.symlinks=false` link becomes a text blob holding the target string, so the build reads that text, not the target file.
   - Task parameters, `Condition` text, environment-variable properties and the `Include=` label on Update refusals stay as stated in D-32.
   - I do not read either case as in contract under D-28 or D-30.

## Gate evidence
- **CP-1** (checkpoint tool on server2, run `20261022-072725-8f2b`, no Runner or Platform pin):
  ```
  CHECKPOINT CP-1 commit=fd20e8dccd3cdbe1f075a6973236dae3728a43a6 build=ok filter=/*/*/LandNonCodeSkipTests/(C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=81 passed=81 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=fd20e8dccd3cdbe1f075a6973236dae3728a43a6 sourceState=clean buildSource=verified
  unlisted: none
  verdict: GREEN exit=0
  ```
  - Both `Antiphon.Checkpoints validate` and `scripts/validate-checkpoint-receipt.ps1` print `CHECKPOINT SOURCE VALID source=fd20e8dc... rows=1`, exit 0.
  - The plan has one Checkpoints row, and Code's CP-1 line matches it (81).
  - There are no R-n rows. The PCs stay pending for SourceLanding Mutation.
- **Unlisted runs, each with its reason. All went through build-slot.**
  - Two `dotnet msbuild` evaluations of scratch fixtures (Win32 `?` probe and escape probe).
  - Two evaluations of the real test project in the scratch witness worktree (one `README` grep and one full JSON dump).
  - One `tests/Antiphon.Tests` build to `bin-c64m/` in a scratch worktree, for the mutation, held 188 s.
  - Two single-method runs: the mutation run and the restore run.
- **Mutation:** `if ($false -and $trimmed.Length -ne $part.Length)` in `ConvertTo-CanonicalPath` made `C1409_WindowsPathNormalisationRefusesBySegment` fail 1/1 (`ran.Code should be 1`). After restoring it, the same test passes 1/1.
- **Landing checks:**
  - `git merge-tree --write-tree origin/master HEAD` (master `bb43ec3529b9d8ad2a799f81080b9e34d89fa9c9`, which contains e0080cfc6) exits 0.
  - The 23 commits `9b39df4e0..c2a6398c7` cherry-pick with `-x` onto `origin/rc/2026-10-20` (`ceb6e3d6b`) with exit 0. The script, the test class, `docs/orchestration-loop.md`, `LandVerificationBuildGraph.cs` and the plan are byte-identical to the branch. Scratch was discarded.
  - `check-evidence-diff.ps1 -BaseRef 9b39df4e0 -HeadRef HEAD`: commits=24, entries=2, violations=0.
- **Cleanup:** the scratch worktrees, fixtures, `bin-c64t` and `bin-c64m` are deleted, and the tool removed `bin-c1409/`. The subject branch was not touched.
