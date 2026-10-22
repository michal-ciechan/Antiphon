# CARD-1400: close cheap land-graph docs-input bypasses

Date: 2026-10-21. Code writes this plan because the dispatch requires one checkpoint row. Verification design is here. Next stage after the green row: review.

Baseline: `origin/master` = `02aaaf4712e1d69e92ded72158c14cdeae194c40`. Branch `feat/card-task-5addff15`. Card: CARD-1400, platform Any. This plan pins no runner and no platform. Checkpoint lane: `tests/Antiphon.Tests`, one row. The task sentence requires one red witness per bypass family and this one green row. It forbids a whole Unit lane, so that lane is not in the table.

## Ground truth

CARD-1390's refusal in `scripts/audit-land-verification-graph.ps1` matches `docs`, `.md`, and `README` with ordinal case, resolves every include from the definition file's own directory, does not decode `%XX`, and reads only EmbeddedResource, Content, None, and Compile `Include` values. MSBuild resolves item includes from the importing project, and on Windows the path compare is case-insensitive.

The committed manifest stays digest `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F`, count 57. Real includes that must stay approved: `server/Antiphon.Server.csproj` `Bundles\*.md`, the `docs/**/*.json` embedded resources, and `src/Messaging.Pack.props` `README.md` plus `AdditionalFiles` of the PublicAPI text files. Those resolve under the project directory, not to repo `docs/**/*.md` or the root `README.md`.

## Decisions

**D-1 — Case-insensitive path match.** Segment globs and the `.md` suffix compare with ordinal ignore-case. `../Docs/**/*.md`, `*.MD`, and `../readme.md` refuse when the resolved path can select repo docs markdown or the root README.

**D-2 — One `%XX` decode.** Decode each `%` plus two hex digits once, as UTF-8 bytes, before slash normalization. A decoded NUL refuses. Unknown `%` text stays literal.

**D-3 — Imported files are checked from the file and from every importer.** `.props` and `.targets` item includes are resolved from the file's own directory and from each project that imports them, directly or through another import. The nearest `Directory.Build.props` and `Directory.Build.targets` above each project count as imported. An `Import` `Project` path that starts with `$(MSBuildThisFileDirectory)` or `$(MSBuildProjectDirectory)` is expanded from that property and is not prefixed with the importing file's folder again. `$(MSBuildThisFileDirectory)` in an include is that file's directory. `$(MSBuildProjectDirectory)` is the directory being checked. An `Import` `Project` path that still contains an unknown `$(...)` is refused and is not followed.

**D-4 — AdditionalFiles is inspected** the same way as EmbeddedResource, Content, None, and Compile. Item type names match ignore-case.

**D-5 — Document the unchecked readers.** `Import`, `ReadLinesFromFile`, `Copy`, and property functions that read files are not inspected. Say that in `docs/orchestration-loop.md`.

**D-6 — `$(DocsDir)*.md` stays accepted.** An unknown `$(...)` is refused only when the include text contains the exact spellings `docs` and `.md`, or `README`.

**D-7 — Do not edit the manifest or any definition file.** `-Check` must still print the digest above and count 57.

**D-8 — Master only.** One checkpoint row. No Unit lane.

## Slices

### S1 — Red witnesses

Commit `fecdad3a2633b1c3e5d23e3b0797218c6ee940db` adds four tests and no script change. Unlisted run, build slot `0cc4c85a-6597-4ddd-b20d-bcf63cde1414`, waited=0s, held=194s, build 0 errors; test slot `c9316250-5b12-4899-a76b-584c957ad6ba`, waited=0s, held=7s. Filter `/*/*/LandNonCodeSkipTests/(C1400_*)`. 4 executed, 4 failed, 0 skipped. Each failure is exit 0 with a printed digest:

- `C1400_UpdateRefusesCaseVariantDocsInputs` approved `../Docs/**/*.md`.
- `C1400_UpdateRefusesImportedPropsDocsInclude` approved `eng/x/shared.props` (count 2).
- `C1400_UpdateRefusesPercentEncodedDocsInclude` approved `../%64ocs/**/*.md`.
- `C1400_UpdateRefusesAdditionalFilesDocsInclude` approved an AdditionalFiles include.

### S2 — Refusal and the doc

- `scripts/audit-land-verification-graph.ps1` — D-1 through D-4 and D-6. ASCII.
- `docs/orchestration-loop.md` — D-3, D-5, D-6.
- This plan.

## Verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-1 | Case variants `../Docs/**/*.md`, `../docs/**/*.MD`, and `../readme.md` refuse, name the include, and do not write the manifest. | `C1400_UpdateRefusesCaseVariantDocsInputs` |
| V-2 | `../docs/**/*.md` in an imported props, and `$(MSBuildProjectDirectory)/../docs/**/*.md` in `Directory.Build.props`, refuse. | `C1400_UpdateRefusesImportedPropsDocsInclude` |
| V-3 | `../%64ocs/**/*.md` refuses and the message keeps the encoded include. | `C1400_UpdateRefusesPercentEncodedDocsInclude` |
| V-4 | AdditionalFiles `../docs/**/*.md` refuses. | `C1400_UpdateRefusesAdditionalFilesDocsInclude` |
| V-5 | The original `../docs/**/*.md` refusal still holds, and `-Check` still matches the manifest. | `C1390_UpdateRefusesDocsMarkdownInput`, `C1385_RegistryMatchesNarrowGraph` |
| V-6 | `$(MSBuildThisFileDirectory)../eng/x/shared.props` is followed, and `../docs/**/*.md` in that props refuses. | `C1400_RepairAnchoredImportRefusesDocsInclude` |
| V-7 | A project import of `$(MSBuildProjectDirectory)/../eng/mid.props` and that file's `$(MSBuildThisFileDirectory)x/shared.props` are both followed, and the nested docs include refuses. | `C1400_RepairNestedAnchoredImportRefusesDocsInclude` |
| V-8 | `B/B.csproj` importing `$(RepoRoot)build/x.props` while `src/App/App.csproj` imports the same props relatively still refuses. | `C1400_RepairRepoRootPropertyImportRefuses` |
| V-9 | `B/B.csproj` importing `$(MSBuildThisFileDirectory)../build/x.props` while `src/App` imports it relatively still refuses `../docs/*.md`. | `C1400_RepairSplitAnchoredImportRefusesDocsInclude` |
| V-10 | A props file imported only by a relative path is still checked from its own directory, so `build/x.props` `../docs/*.md` refuses. | `C1400_RepairOwnDirectoryStillRefusesWhenImported` |
| V-11 | An `Import` `Project` path that still contains `$(RepoRoot)` refuses even when the target include is not docs markdown. | `C1400_RepairUnresolvedImportRefuses` |

No R-n. No positive controls. Mutation has nothing new to plant beyond the refusal predicate. Zero new PC rows. Existing land PCs stay pending for SourceLanding Mutation.

### S3 — Anchored-import repair

Review c26b14f3. Unlisted red run at `7d15e1b2e70e2c7e58be70925bd641d520765bd9`, script still the regression. The first command forwarded `-nodeReuse:false` to TUnit and executed no tests (exit 5). Build slot `09476500-ed15-4e28-b032-55bbbd89250e`, waited=0s, held=151s. The witness reused that output: slot `bac7255f-4827-4aba-b234-23215ea3f5fb`, waited=0s, held=13s. Filter `/*/*/LandNonCodeSkipTests/C1400_Repair*`. 6 executed, 6 failed, 0 skipped. Each failure was exit 0 with a printed digest.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1400/` | c1400-docs | `/*/*/LandNonCodeSkipTests/(C1400_*)\|(C1390_UpdateRefusesDocsMarkdownInput*)\|(C1385_RegistryMatchesNarrowGraph*)` | V-1 V-2 V-3 V-4 V-5 V-6 V-7 V-8 V-9 V-10 V-11 | exactly 12 executed, 0 failed/skipped | 12 | 20 |
