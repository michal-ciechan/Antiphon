# Review f0fb9035: CARD-1409 repair 4 (Code 5406fd51), Final

Outcome: two defects. The resolved-path class is not closed. The required gate is green: CP-1 77/77, receipt valid, `-Check` F240D0F1/57, ordinary edits pass, S14 witnesses red-then-green, the mutation goes red, merge-tree clean, rc cherry-pick clean.

Subject: `feat/card-task-5406fd51` at `5471da833f9e4b4b1cf08dd4bbd70584d67220f8` (tested source `fbd2a780e1913c50f915490bfde2a14ad5ca8728` plus a report-only commit). Land owner: 5406fd51-fdc7-42d1-b16a-463f0fa16c47.

## Defects

### F1: Windows path normalisation (trailing dot, ADS) bypasses the docs and README rules (D-21)
- **Where:** `scripts/audit-land-verification-graph.ps1`. The checks are `Assert-PathCandidateAllowed` (exact branch), `Test-PathUnderDocs`, `Test-PatternMatchesReadme`, `Test-DocumentPath` and `Test-PatternCanReachDocs`. They compare the resolved text literally.
- **Failure:** `-Update` approves (exit 0, count=1) each of these from `server/App.csproj`:
  - `EmbeddedResource Include="../README.md."`
  - `Include="../docs./guide.md."`
  - `Include="../docs./*.md"`
  - `Include="../README.md::$DATA"`
  - `Include="../README.md. ."`

  The controls `../docs/guide.md` and `../README.md` refuse. On the real tree at 5471da833, adding `<EmbeddedResource Include="../../README.md." />` to `tests/Antiphon.Tests/Antiphon.Tests.csproj` passes `-Update` with count=57.
- **Why:** the plan classifies "case-insensitive as on the Windows land host". Win32 path normalisation removes a trailing `.` from each segment and trailing dots and spaces from the last segment, and `::$DATA` opens the default stream. So on the land host these paths read `docs/guide.md` or the root `README.md`. A later docs-only land would then skip a build that embeds that file. `GetExtension("guide.md.")` is empty, so the document allow-list never sees it either.
- **Fix:** fail closed. Refuse any resolved candidate or pattern segment that ends in `.` or a space (other than `.`/`..`) or that contains `:` past a drive prefix, naming the part. Alternatively, normalise each segment the Win32 way before every docs, README and document test, and before `Test-PatternCanReachDocs`. Add a C1409 witness for the four shapes above.

### F2: an item `Update` sets metadata that the `%(Item.Meta)` census never sees (D-22)
- **Where:** `Register-DefinitionFacts`. `ItemDeclarations` records metadata only from elements with `Include`. `Expand-Candidates` then takes `%(Item.Metadata)` from those declarations alone.
- **Failure:** in a scratch commit at 5471da833, I appended this to the real `tests/Antiphon.Tests/Antiphon.Tests.csproj`:

  ```
  <ItemGroup><StagedHelper Update="$(MSBuildThisFileDirectory)..\..\src\Antiphon.FakeGrok\Antiphon.FakeGrok.csproj" Glob="..\..\..\..\..\docs\**\*.md" /></ItemGroup>
  ```

  `-Update` returned exit 0, count=57. MSBuild applies the Update to the line-98 item, so `_StagedHelperGlob` (`%(StagedHelper.Glob)`) becomes `..\..\..\..\..\docs\**\*.md`. `_StagedHelperFiles Include="$(_StagedHelperDir)$(_StagedHelperGlob)"` then climbs from FakeGrok's output dir back to the repository root and selects `docs/**/*.md`, which `Copy` stages into the test output. The control puts the same `Glob` on a new `Include` declaration. It refuses, naming `_StagedHelperFiles Include=$(_StagedHelperDir)$(_StagedHelperGlob)`.
- **Why:** D-22 says `%(Item.Metadata)` takes "every declared value of that metadata". D-26 allow-lists `Glob` on `StagedHelper`, including on `Update`. So the Update value is a declared value the expansion drops, and the output-root `..` guard never sees it.
- **Fix:** record metadata from `Update` elements, static and target-time, as declarations of that item type for `%(...)` expansion. Alternatively, refuse `%(Item.Meta)` when any `Update` of the item sets that metadata. Add a witness on the real-project fixture.

## Disclosure (out of contract, suggest a Backlog card; not a defect)
- **Tracked symlinks are opaque to the tree expansion.** I tracked `server/fixtures` as a 120000 blob pointing to `../docs`, with `Content Include="fixtures/**"`. `-Update` approved it with count=1, because `^server/fixtures/.*$` matches nothing tracked. A symlinked `.md` under an allow-listed pattern such as `server/Bundles/*.md` is a non-definition change, so the digest never re-audits it. This is reachable only where the land checkout materialises symlinks; Git for Windows defaults to `core.symlinks=false`.
- **Suggested fix:** refuse a pattern whose literal prefix or match passes through a 120000 blob, or refuse a 120000 blob under any path-item pattern.

## Gate evidence
- **CP-1** (checkpoint tool on server2, run `20261022-055840-eae2`, no Runner or Platform pin):
  ```
  CHECKPOINT CP-1 commit=5471da833f9e4b4b1cf08dd4bbd70584d67220f8 build=ok filter=/*/*/LandNonCodeSkipTests/(C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=77 passed=77 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=5471da833f9e4b4b1cf08dd4bbd70584d67220f8 sourceState=clean buildSource=verified
  ```
  The report says `unlisted: none` and `verdict: GREEN exit=0`. `scripts/validate-checkpoint-receipt.ps1` printed `CHECKPOINT SOURCE VALID source=5471da833f9e4b4b1cf08dd4bbd70584d67220f8 rows=1`, exit 0. The plan's `### Checkpoints` has one row (CP-1); the Code report has a matching CP-1 line with count 77. No R-n rows. No PCs; existing land PCs stay pending.
- **S14 witnesses:** these runs were not listed in the plan; their reason is the brief's red-proof requirement. All went through build-slot in a scratch detached worktree at `f35c2f778`, with one test build to `bin-rv/` (lease df2a93da, 227s).
  - At `f35c2f778`: total 5, failed 5. Every failure is `ShouldAssertException: ran.Code should be 1` (the audit approved).
  - With the `fbd2a780e` script: 5/5 passed.
- **Mutation:** I added `return $false` at the top of `Test-PatternCanReachDocs`. `C1409_DocsGlobRefusesByResolvedPath` failed (1/1, `ran.Code should be 1`). The mutation was then restored.
- **Acceptance:**
  - `-Check` on 5471da833 printed `digest=F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F count=57`, exit 0.
  - The committed ordinary edit to `tests/Antiphon.Tests/Antiphon.Tests.csproj` passed `-Update`, count=57. It added a new PackageReference, a linked Compile, and a ProjectReference with `ReferenceOutputAssembly="false"`.
  - The audit reads HEAD via `ls-tree`/`cat-file`, so an uncommitted edit is not audited. The scratch edit had to be committed.
- **Landing checks:**
  - `git merge-tree --write-tree origin/master HEAD` (master `daf19e530f0d652a2cbc60b1d43444506cb81b2d`) exited 0.
  - The 18 commits `9b39df4e0..fbd2a780e` cherry-pick onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`) with exit 0. The script, test class and `LandVerificationBuildGraph.cs` are byte-identical to the branch. Scratch was discarded.
  - `check-evidence-diff.ps1 -BaseRef 9b39df4e0 -HeadRef HEAD`: commits=19, violations=0.
- **Cleanup:** scratch worktrees, `bin-rv`, probe repos and the tool's `bin-c1409/` are deleted. The subject branch was not touched.
