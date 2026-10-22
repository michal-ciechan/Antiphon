# CARD-1409: audit imports fail closed

Date: 2026-10-22. Code writes this plan because the dispatch requires one checkpoint row. Verification design is here. Next stage after the green row: review.

Baseline: `origin/master` = `9b39df4e065bb7267b9572cfd2c1acdf2b498e5b`. Branch `feat/card-task-8ef9ed35`. Card: CARD-1409, platform Any. This plan pins no runner and no platform. Checkpoint lane: `tests/Antiphon.Tests`, one row. The task sentence requires one red witness per item and this one green row. It forbids a whole Unit lane, so that lane is not in the table.

## Ground truth

CARD-1400 made `scripts/audit-land-verification-graph.ps1` follow `Import` elements. An `Import` `Project` path that still contains `$(` after `$(MSBuildThisFileDirectory)` and `$(MSBuildProjectDirectory)` are expanded is refused. Everything else the audit cannot follow is dropped without a word: an `@(...)` item or `%(...)` metadata reference, an `Import` that carries an `Sdk`, `Version` or `MinimumVersion` attribute, a `Project` path that resolves outside the repository, a path that is not tracked at HEAD, and a tracked target whose extension the audit does not parse (anything other than `*proj`, `.props`, `.targets`). A wildcard `Import` is matched only against the parsed project files, so a tracked `eng/*.items` match is also dropped.

The real repository has eight `Import` elements: seven `..\Messaging.Pack.props` and one `$(MSBuildThisFileDirectory)tests/Shared/CheckpointRepeat.targets` with a `Condition`. All resolve to tracked project XML. The committed manifest stays digest `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F`, count 57.

## Decisions

**D-1 — Keep unknown `$(...)` fail-closed; extend it to every reference the audit cannot expand.** After the two known properties are expanded, an `Import` `Project` text that still contains `$(`, `@(` or `%(` is refused. `$(MSBuildExtensionsPath)`, `$(NuGetPackageRoot)` and `$([MSBuild]::GetPathOfFileAbove(...))` stay refused; a future chained `Directory.Build.props` that needs one of them changes the audit first. No allow-list.

**D-2 — An `Import` attribute the audit does not understand is refused.** `Project`, `Condition` and `Label` are accepted; `Condition` is ignored, so a conditional import is still followed. `Sdk`, `Version`, `MinimumVersion` and any other attribute refuse, because the audit cannot say where such an import comes from.

**D-3 — A missing or uninspected import target is refused, not skipped.** A non-wildcard `Project` path must resolve inside the repository to a path tracked at HEAD that the audit parses as project XML. Outside the repository, absent from HEAD, or a tracked file with another extension each refuse and name the importing file, the attribute text and the resolved path. A wildcard `Import` is matched against every path tracked at HEAD; a match the audit does not parse refuses; a wildcard that matches nothing is accepted, as MSBuild accepts it.

**D-4 — Fix the wording.** `docs/orchestration-loop.md` says an `Import` is followed and describes D-1 to D-3; the sentence that listed `Import` beside the uninspected readers now lists only `ReadLinesFromFile`, `Copy` and property functions. The script header says the same. The CARD-1400 plan's `### Checkpoints` separator row gets its ninth column.

**D-5 — Do not edit the manifest or any definition file.** `-Check` must still print the digest above and count 57.

**D-6 — Master only; cherry-pick status reported.** The audit script, the test class and the doc are byte-identical on `origin/master` and `origin/rc/2026-10-20`, so the branch should cherry-pick cleanly; the report says whether `merge-tree` agrees.

## Slices

### S1 — Red witnesses for items 1 and 2

`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs` gains the `C1409_*` tests below and no script change. One unlisted red run proves them red at the witness commit (reason: red proof before the fix; reported with its CHECKPOINT line).

### S2 — Item 1: unsupported references and attributes refuse

`scripts/audit-land-verification-graph.ps1` D-1 and D-2. ASCII.

### S3 — Item 2: missing and uninspected targets refuse

`scripts/audit-land-verification-graph.ps1` D-3. ASCII.

### S4 — Item 3: wording

`docs/orchestration-loop.md`, the script header, the CARD-1400 plan separator row, and this plan.

## Verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-1 | `<Import Project="@(SharedProps)" />` and `<Import Project="%(Shared.Path)" />` refuse, name the attribute text, and do not write the manifest. | `C1409_ImportWithItemOrMetadataReferenceRefuses` |
| V-2 | `<Import Project="Sdk.props" Sdk="Some.Sdk" />` refuses even though `server/Sdk.props` is tracked and harmless; the message names the attribute. | `C1409_ImportWithUnsupportedAttributeRefuses` |
| V-3 | `<Import Project="../eng/missing.props" />` with no such tracked file refuses and names `eng/missing.props`. | `C1409_MissingImportTargetRefuses` |
| V-4 | `<Import Project="../../outside.props" />` from `server/App.csproj` resolves outside the repository and refuses. | `C1409_ImportOutsideRepositoryRefuses` |
| V-5 | A tracked `eng/docs.items` imported by exact path, and the same file matched by `../eng/*.items`, refuse as uninspected targets. | `C1409_UninspectedImportTargetRefuses` |
| V-6 | `<Import Project="../eng/*.props" />` that matches nothing is accepted: `-Update` exits 0 and writes the manifest. Acceptance control for D-3; red when the fix refuses an empty wildcard. | `C1409_EmptyWildcardImportIsAccepted` |
| V-7 | The CARD-1400 and CARD-1390 refusals still hold, the conditional real-repo import is still followed, and `-Check` still matches the manifest. | `C1400_*`, `C1390_*`, `C1385_RegistryMatchesNarrowGraph` |

No R-n. No positive controls. Mutation has nothing new to plant beyond the refusal predicates. Zero new PC rows. Existing land PCs stay pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S4 | `tests/Antiphon.Tests -> bin-c1409/` | c1409-imports | `/*/*/LandNonCodeSkipTests/(C1409_*)\|(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)` | V-1 V-2 V-3 V-4 V-5 V-6 V-7 | exactly 20 executed, 0 failed/skipped | 20 | 15 |

### Cost

Ordinary floor 15 minutes for the one row, plus the unlisted red run before S2.
