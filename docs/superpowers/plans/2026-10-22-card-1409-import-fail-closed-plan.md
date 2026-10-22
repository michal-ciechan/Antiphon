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

**D-7 — An `Import` `Project` is split on `;` (Review 23aedc52 D1).** MSBuild treats `a.props;b.props` as two imports. The audit splits the raw attribute on `;`, trims, drops empty parts, and resolves and follows each part on its own; a list with no non-empty part is the existing empty-`Project` refusal. Refusals name the whole attribute text and the resolved part. `%3B` is decoded after the split, so an escaped semicolon stays inside its part, as in MSBuild.

**D-8 — A wildcard `Import` is refused outright (Review 23aedc52 D2).** This supersedes the wildcard sentence of D-3 and withdraws V-6. Land digests only definition files, so a wildcard accepted today would not be re-audited when a later non-definition file starts matching it. A part containing `*` or `?` after the two known properties are expanded refuses with a message naming the importing file and the attribute text, whatever it matches; the tracked-path scan goes away. The real repository has no wildcard import; a future one changes the audit first, as D-1 already says for unknown properties. Proving that a given wildcard can only match parsed definition files was not attempted.

**D-9 — The nearest `Directory.Packages.props` is an implicit import (Review 23aedc52 D3).** `Microsoft.Common.props` imports it into every project, so it joins `Directory.Build.props` and `Directory.Build.targets` in the nearest-ancestor lookup and its includes resolve from each importing project's directory. Only the nearest file is imported, as MSBuild does.

**D-10 — `*proj.user` joins both inventories (Review 23aedc52 D4).** `Microsoft.Common.targets` imports `$(MSBuildProjectFullPath).user` when it exists. A lowercased file name ending in `proj.user` is a definition file in `LandVerificationBuildGraph.Includes` and in the script's `Test-DefinitionPath`, is parsed as project XML, and is enqueued as an import of its project. No `*proj.user` is tracked at HEAD, so the manifest digest and count do not change (D-5 holds).

## Slices

### S1 — Red witnesses for items 1 and 2

`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs` gains the `C1409_*` tests below and no script change. One unlisted red run proves them red at the witness commit (reason: red proof before the fix; reported with its CHECKPOINT line).

### S2 — Item 1: unsupported references and attributes refuse

`scripts/audit-land-verification-graph.ps1` D-1 and D-2. ASCII.

### S3 — Item 2: missing and uninspected targets refuse

`scripts/audit-land-verification-graph.ps1` D-3. ASCII.

### S4 — Item 3: wording

`docs/orchestration-loop.md`, the script header, the CARD-1400 plan separator row, and this plan.

### S5 — Repair witnesses (Review 23aedc52 D1-D4)

`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs` gains `C1409_SemicolonImportAuditsEveryPart`, `C1409_WildcardImportRefuses` (replacing `C1409_EmptyWildcardImportIsAccepted`), `C1409_DirectoryPackagesPropsIsImportedByTheNearestProject`, `C1409_ProjectUserFileIsAuditedAndImported` and `C1409_ProjectUserFileIsInTheHashedInventory`; the wildcard half of `C1409_UninspectedImportTargetRefuses` moves into the wildcard witness. One unlisted red run at the witness commit (reason: red proof before the fix; reported with its CHECKPOINT line and counts).

### S6 — Repair fixes

`scripts/audit-land-verification-graph.ps1` D-7 to D-10 and `server/Application/Services/LandVerificationBuildGraph.cs` D-10. ASCII.

### S7 — Repair wording

`docs/orchestration-loop.md`, the script header, and this plan.

## Verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-1 | `<Import Project="@(SharedProps)" />` and `<Import Project="%(Shared.Path)" />` refuse, name the attribute text, and do not write the manifest. | `C1409_ImportWithItemOrMetadataReferenceRefuses` |
| V-2 | `<Import Project="Sdk.props" Sdk="Some.Sdk" />` refuses even though `server/Sdk.props` is tracked and harmless; the message names the attribute. | `C1409_ImportWithUnsupportedAttributeRefuses` |
| V-3 | `<Import Project="../eng/missing.props" />` with no such tracked file refuses and names `eng/missing.props`. | `C1409_MissingImportTargetRefuses` |
| V-4 | `<Import Project="../../outside.props" />` from `server/App.csproj` resolves outside the repository and refuses. | `C1409_ImportOutsideRepositoryRefuses` |
| V-5 | A tracked `eng/docs.items` imported by exact path refuses as an uninspected target. The `../eng/*.items` wildcard half moved to V-9 under D-8. | `C1409_UninspectedImportTargetRefuses` |
| V-6 | Withdrawn by the repair (D-8): an empty wildcard import is refused, not accepted. `C1409_EmptyWildcardImportIsAccepted` is replaced by `C1409_WildcardImportRefuses` (V-9). | none |
| V-7 | The CARD-1400 and CARD-1390 refusals still hold, the conditional real-repo import is still followed, and `-Check` still matches the manifest. | `C1400_*`, `C1390_*`, `C1385_RegistryMatchesNarrowGraph` |
| V-8 | `<Import Project="../eng/docs.items;../eng/*.props" />` refuses naming the whole attribute text and `-> eng/docs.items`; a root project importing `eng/a.props;eng/x/docs.props` is refused on the second part's `docs/**/*.md` include, so every part is followed. | `C1409_SemicolonImportAuditsEveryPart` |
| V-9 | `../eng/*.props` matching nothing, `../eng/*.props` matching a tracked harmless file, `../eng/shared.?rops`, and `../eng/*.items` each refuse with the wildcard message naming the attribute text. | `C1409_WildcardImportRefuses` |
| V-10 | A root `Directory.Packages.props` with `<None Include="../docs/**/*.md" />` is refused for `server/App.csproj`; with a harmless `server/Directory.Packages.props` nearer to `server/sub/App.csproj`, `-Update` exits 0 with count=3 (nearest only). | `C1409_DirectoryPackagesPropsIsImportedByTheNearestProject` |
| V-11 | A tracked `server/App.csproj.user` with a docs include refuses; one that imports `../eng/docs.items` refuses naming `-> eng/docs.items`; a harmless one is counted (`count=2`). | `C1409_ProjectUserFileIsAuditedAndImported` |
| V-12 | `LandVerificationBuildGraph.Includes` is true for `*proj.user` (case-insensitive) and false for `settings.user`, `Antiphon.sln.user` and `*.csproj.user.bak`; a tracked `server/Antiphon.Server.csproj.user` already on the target makes a docs-only land build with `unrecognized-build-graph`. | `C1409_ProjectUserFileIsInTheHashedInventory` |

No R-n. No positive controls. Mutation has nothing new to plant beyond the refusal predicates. Zero new PC rows. Existing land PCs stay pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S7 | `tests/Antiphon.Tests -> bin-c1409/` | c1409-imports | `/*/*/LandNonCodeSkipTests/(C1409_*)\|(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)` | V-1 V-2 V-3 V-4 V-5 V-7 V-8 V-9 V-10 V-11 V-12 | exactly 24 executed, 0 failed/skipped | 24 | 15 |

### Cost

Ordinary floor 15 minutes for the one row, plus the unlisted red run before S2.
