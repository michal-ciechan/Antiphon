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

**D-11 — A tracked definition file under `obj/` is refused by its path (Review 8989ece8 F1).** `Microsoft.Common.props` and `Microsoft.Common.targets` import `$(MSBuildProjectExtensionsPath)$(MSBuildProjectFile).*.props` and `.*.targets`, `obj/` by default, and the audit does not follow those wildcards. Any path that `Test-DefinitionPath` selects and that has an `obj` directory segment, compared case-insensitively, refuses in both `-Check` and `-Update`, whatever the file contains; the message names the path. The server inventory is unchanged: a tracked `obj/` `.props` or `.targets` already counts by its extension, so no manifest can cover such a tree. No tracked path has an `obj` segment today, so D-5 holds.

**D-12 — A redirected extension or intermediate directory is refused (Review 8989ece8 F1).** A parsed definition file with an element named `BaseIntermediateOutputPath`, `MSBuildProjectExtensionsPath` or `IntermediateOutputPath`, compared case-insensitively and wherever it sits, refuses with a message naming the file and the element as written; a redirected path cannot be evaluated. Reading one of those properties inside another property, as the real `Directory.Build.targets` and `tests/Shared/CheckpointRepeat.targets` do, is not setting it, so `-Check` still prints the digest above and count 57.

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

### S8 — F1 witnesses (Review 8989ece8)

`tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs` gains `C1409_ObjExtensionDefinitionFileRefuses` and `C1409_IntermediateOutputRedirectRefuses`. One unlisted red run at the witness commit (reason: red proof before the fix; reported with counts).

### S9 — F1 fix

`scripts/audit-land-verification-graph.ps1` D-11 and D-12; a comment in `server/Application/Services/LandVerificationBuildGraph.cs` records why the inventory needs no change. ASCII.

### S10 — F1 wording

`docs/orchestration-loop.md`, the script header, and this plan; CP-1 expects 26.

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
| V-13 | A tracked `server/obj/App.csproj.evil.props` or `.evil.targets` with `<None Include="../docs/**/*.md" />`, a harmless `server/OBJ/Debug/App.csproj.harmless.props` and an `obj/Generated.csproj` each refuse naming the path; `Includes` is true for the obj `.props`/`.targets` and false for `obj/project.assets.json`. | `C1409_ObjExtensionDefinitionFileRefuses` |
| V-14 | `BaseIntermediateOutputPath` in the project, `MSBuildProjectExtensionsPath` in `Directory.Build.props` and a lower-case `intermediateoutputpath` in an imported `.props` each refuse naming the file and the element; a `Directory.Build.targets` that only reads `$(IntermediateOutputPath)` is approved with `count=2`. | `C1409_IntermediateOutputRedirectRefuses` |

No R-n. No positive controls. Mutation has nothing new to plant beyond the refusal predicates. Zero new PC rows. Existing land PCs stay pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S13 | `tests/Antiphon.Tests -> bin-c1409/` | c1409-imports | `/*/*/LandNonCodeSkipTests/(C1409_*)\|(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)\|(C1380_*)` | V-1 V-2 V-3 V-4 V-5 V-7 V-8 V-9 V-10 V-11 V-12 V-13 V-14 V-15 V-16 V-17 V-18 | exactly 72 executed, 0 failed/skipped | 72 | 15 |

### Cost

Ordinary floor 15 minutes for the one row (the 42 `C1380_*` harness cases dominate), plus the unlisted red runs before S2, S6, S9 and S12 and the unlisted green subset run after S12.

## Repair 3: default-deny (Review 31c77116)

Final Review 31c77116 found three remaining classes of fail-open after three rounds of enumerated fixes: R1 response files were hashed but never parsed; R2 about thirty SDK imports are driven by ordinary properties (`CustomAfterMicrosoftCommonTargets`, `DirectoryBuildTargetsPath`, `PublishProfile` and their kin) that a name deny-list cannot close; R3 item types the audit did not watch (`EditorConfigFiles`, `GlobalAnalyzerConfigFiles`, `ProjectReference`) accepted docs markdown. The audit only gates `-Update`; a changed definition file is never a docs-only land. The design therefore changes to default-deny: the script carries allow-lists seeded from the tracked definition files, and every construct outside them refuses by name. Branch `feat/card-task-726f1f91`, start ref `9eab17ecfb73cf3f968ce8abba8141282a6a81c9`.

**D-13 — Property names are allow-listed.** Every child element of a `PropertyGroup`, wherever the group sits (under `Project`, inside a `Target`), and every task `Output` with a `PropertyName`, must name one of the 54 properties in the inventory below; names compare case-insensitively, as MSBuild does. Anything else refuses, naming the file and the property as written. The D-12 names keep their own message because that check runs first. A new property is a reviewed edit of the list in `scripts/audit-land-verification-graph.ps1`.

**D-14 — Item types are allow-listed with a rule each.** Every child element of an `ItemGroup`, wherever the group sits, and every `Output` `ItemName`, must be one of the 24 item types below; an unknown type refuses by type. `Include` is split on `;` and each part follows the type's rule. A part that contains `@(` or `%(` refuses for every type. *path* (`Compile`, `EmbeddedResource`, `Content`, `None`, `AdditionalFiles`, `EditorConfigFiles`, `GlobalAnalyzerConfigFiles` and the repository's thirteen target-time items): resolved from the file's own directory and from every importing project's directory as CARD-1400 already did; `escape` and a forbidden unknown property keep the docs refusal; a part that leaves the repository (absolute, or `..` past the root) now refuses as unclassified where it was accepted before; an unresolved `$(...)` keeps the CARD-1400 spelling heuristic; a resolved pattern that selects `docs/**/*.md` or the root `README.md` refuses. *project* (`ProjectReference`): no `$(`, `*`, `?`; the resolved path from every directory must be a `*proj` tracked at HEAD, otherwise the message names the part and ` -> <resolved>`. *name* (`PackageReference`, `FrameworkReference`, `InternalsVisibleTo`): after one `%XX` decode the part contains none of `/`, `\`, `$(`, `*`, `?`, `..`, `:`. The brief's draft rule, "file items only inside the project's own directory and never `docs/**`", was relaxed to this after reading the inventory: ten projects link `Compile` and three link `Content` from sibling directories, and `tests/Antiphon.Tests` and `tests/Antiphon.Agents.Pty.Tests` embed `docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json`; the skip policy already treats docs JSON as a build input, so `docs/**/*.md` and the root README are the only paths that matter. Item metadata, `Exclude`, `Remove` and `Update` are not evaluated: they add no input.

**D-15 — Structure is allow-listed.** The root must be `<Project>`; each `;`-part of its `Sdk` attribute must be `Microsoft.NET.Sdk` or `Microsoft.NET.Sdk.Web`; its children must be `PropertyGroup`, `ItemGroup`, `Import`, `Target` or `Sdk`, and a `<Sdk>` element must name `Aspire.AppHost.Sdk`. `UsingTask`, `ImportGroup`, `Choose`, `ItemDefinitionGroup`, `ProjectExtensions` and every other element refuse naming the file and the element. Element names compare ordinally, as XML does.

**D-16 — Response files are parsed (R1).** Every tracked `.rsp` is read; a leading BOM is dropped, lines are trimmed, blank lines and `#` comments are skipped, and each remaining line must be exactly one switch: `-` or `/`, then `nodeReuse`/`nr` with `true` or `false`, `maxcpucount`/`m` with no value or 1 to 9999, `verbosity`/`v` with `q`, `quiet`, `m`, `minimal`, `n`, `normal`, `d`, `detailed`, `diag` or `diagnostic`, or `nologo` with no value. Anything else refuses naming the file and the line: `-p:`, `-property:`, `@file`, `/t:`, an unknown value, or two switches on one line. The real `Directory.Build.rsp` holds a comment block and `-nodeReuse:false`.

**D-17 — The real repository passes unchanged.** `-Check` still prints digest `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F` count=57; that is the acceptance test that the lists are not too tight. `LandVerificationBuildGraph.Includes` is unchanged (a comment records why): `.rsp` was already selected, and `EditorConfigFiles`/`GlobalAnalyzerConfigFiles` are item types, not files. Three existing fixtures used names outside the inventory and now use inventory names with unchanged assertions: `ActiveDebugProfile` became `Nullable` in `C1409_ProjectUserFileIsAuditedAndImported`, `PackageVersion` became `PackageReference` in `C1409_DirectoryPackagesPropsIsImportedByTheNearestProject`, and `_CleanFile` became `_AntiphonCleanFileChars`, the real `Directory.Build.targets` shape, in `C1409_IntermediateOutputRedirectRefuses`. `PackageVersion` is not allow-listed because no tracked file uses it; adopting central package management is a reviewed edit of the list. V-10's witness also changed: its shadowed root `Directory.Packages.props` relied on `../docs/**/*.md` being silently accepted when it leaves the repository from the file's own directory, which D-14 now refuses, so `C1409_DirectoryPackagesPropsIsImportedByTheNearestProject` now proves nearest-only import through an `Import Project="eng/docs.items"` that the audit follows, and refuses as an uninspected target, only when the root file is the nearest one; shadowed, the root file is still parsed against the allow-lists and counted (`count=3`). A definition file no project reaches has its imports left unfollowed, as MSBuild leaves them unevaluated; its own properties, items and structure are still checked.

**D-18 — Out of scope, and why.** Machine-wide `ImportBefore`/`ImportAfter` under `$(MSBuildExtensionsPath)` and the SDK's own targets are toolchain state outside the tree; `global.json` is hashed, their contents are not in HEAD. NuGet package `build/*.props` and `.targets` live in the package cache, addressed by the `PackageReference` id and version the digest already covers. Solution-level files are hashed, not parsed; a tracked `Directory.Solution.props` or `.targets` is parsed as project XML and meets the same allow-lists. Target bodies are not evaluated: a task parameter (`Exec`, `Copy`, `ReadLinesFromFile`) or a property value can name any file and the audit refuses no task. That is unchanged since CARD-1400 and is accepted because such an edit is a definition-file change, which never lands as docs-only and is reviewed, while R1 to R3 lived in what the SDK evaluates at import time, which the allow-lists now bound.

**D-19 — Verification scope.** CP-1 adds `C1380_*` as the brief requires: the server inventory file changes (comment only) and the land policy depends on it. Expected 72 executed: 16 `C1409_*`, 10 `C1400_*`, 3 `C1390_*`, `C1385_RegistryMatchesNarrowGraph`, 42 `C1380_*` cases.

**D-20 — Landing.** None of the CARD-1409 commits is on `origin/master` or `origin/rc/2026-10-20`. The report states the `merge-tree` result against master and a chained per-commit `merge-tree` onto the rc tip; nothing is merged.

### Repair 3 slices

**S11 — Red witnesses.** `C1409_ResponseFileSwitchesAreAllowListed`, `C1409_UnknownPropertyRefuses`, `C1409_UnknownItemTypeRefuses`, `C1409_UnclassifiedConstructRefuses`. One unlisted red run at the witness commit (reason: red proof before the fix; CHECKPOINT line and counts in the report).

**S12 — Fix.** `scripts/audit-land-verification-graph.ps1` D-13 to D-16 (ASCII; parses under Windows PowerShell 5.1); the three fixture renames of D-17. One unlisted green run of `(C1409_*)|(C1400_*)|(C1390_*)` at the fix commit (reason: catch a fixture regression before the checkpoint build).

**S13 — Wording.** This section, `docs/orchestration-loop.md`, the script header, the `LandVerificationBuildGraph.cs` comment; CP-1 widened and expecting 72.

### Repair 3 verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-15 | `-p:BaseIntermediateOutputPath=ext/`, `-p:_DirectoryBuildPropsFile=docs/build.md` and `@docs/flags.md` in `Directory.Build.rsp`, `-property:...` in `eng/build.rsp`, `-nodeReuse:maybe` and `/t:Build` each refuse naming the file and the line; a BOM, CRLF, comments, blanks, `-nodeReuse:false`, `-maxcpucount`, `/m:4`, `-verbosity:minimal`, `-v:q` and `-nologo` are approved with `count=2`. | `C1409_ResponseFileSwitchesAreAllowListed` |
| V-16 | `CustomAfterMicrosoftCommonTargets` in a project, `CustomBeforeMicrosoftCommonProps` in `Directory.Build.props`, `DirectoryBuildTargetsPath` in an imported `.props`, `_DirectoryBuildTargetsFile` inside a `Target`, `BeforeMicrosoftNETSdkTargets` in a `.user`, `DirectoryPackagesPropsPath` in `Directory.Packages.props`, `PublishProfile`, the unlisted `_antiphonFutureImportHook`, and an `Output PropertyName="CustomAfterMicrosoftCSharpTargets"` each refuse naming the file and the property; `TargetFramework`, lower-case `nullable`, `AssemblyName`, `RootNamespace` are approved with `count=1`. | `C1409_UnknownPropertyRefuses` |
| V-17 | `EditorConfigFiles Include="../docs/style.md"` and `GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)docs/analyzers.md"` refuse as docs markdown; `ProjectReference` to `../docs/x.md` and to an untracked `../lib/Missing.csproj` refuse naming ` -> ` the resolved path; a wildcard `ProjectReference` and a `PackageReference Include="../docs/x.md"` refuse as unclassified; `Reference`, a `_AntiphonFutureFiles` item inside a target and an `Output ItemName="_AntiphonFutureOutputs"` refuse by type; a tracked `ProjectReference`, `PackageReference`, `FrameworkReference`, `InternalsVisibleTo`, `.editorconfig`, `analyzers.globalconfig`, a linked `Compile` and a `None` are approved with `count=2`. | `C1409_UnknownItemTypeRefuses` |
| V-18 | `UsingTask`, `ImportGroup`, `Choose`, `ItemDefinitionGroup`, `ProjectExtensions` refuse naming the element; `Sdk="Evil.Sdk/1.0.0"` and `<Sdk Name="Evil.Sdk">` refuse naming the SDK; `Content Include="@(Docs)"`, `None Include="%(Docs.FullPath)"`, `Compile Include="../../outside/Shared.cs"` and `None Include="/etc/hosts"` refuse as unclassified; the Web SDK, the Aspire `<Sdk>` element, a conditional import, an `Exec` with `Output PropertyName="_GitHeadSha"`, a `%(RecursiveDir)` link and a target-time `Compile` are approved with `count=2`. | `C1409_UnclassifiedConstructRefuses` |

V-7 remains the acceptance witness for D-17: `C1385_RegistryMatchesNarrowGraph` runs `-Check` on the real repository. No new PC rows; the refusal predicates are the mutation surface Review already named.

### Inventory (tracked definition files at `9eab17ecfb73cf3f968ce8abba8141282a6a81c9`)

57 files: 46 `*.csproj`, `Antiphon.sln`, `Directory.Build.props`, `Directory.Build.rsp`, `Directory.Build.targets`, `global.json`, `src/Messaging.Pack.props`, `tests/Shared/CheckpointRepeat.targets`, two nightly fixture `Directory.Build.*` pairs under `scripts/fixtures/nightly/` (`c1375-probe`, `c487-probe`). Project root `Sdk`: `Microsoft.NET.Sdk` (42), `Microsoft.NET.Sdk.Web` (4). `<Sdk>` element: `Aspire.AppHost.Sdk` (`Antiphon.AppHost`). Project children: `PropertyGroup` (49 files), `ItemGroup` (40), `Import` (8), `Target` (8), `Sdk` (1). Task elements inside targets: `Copy`, `Delete`, `Error`, `Exec`, `MSBuild`, `Warning`, `WriteLinesToFile`. `Directory.Build.rsp`: four comment lines and `-nodeReuse:false`.

Property names set in a `PropertyGroup` (51) plus three set only by a task `Output` (`_GitHeadSha`, `_GitRevParseExitCode` in `Directory.Build.props`; `FakeBrowserTargetPath` in `tests/Antiphon.MarkdownPdf.Tests`):

| Property | Files | Where (when one or two) |
|---|---:|---|
| `_AntiphonCleanFileChars` | 1 | `Directory.Build.targets` |
| `_AntiphonRepeatAdditional` | 1 | `tests/Shared/CheckpointRepeat.targets` |
| `_AntiphonRepeatFile` | 1 | `tests/Shared/CheckpointRepeat.targets` |
| `_CustodyChildOutputDir` | 2 | `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj`, `tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj` |
| `_F5PtyHostNativeDir` | 1 | `tests/Antiphon.E2E/Antiphon.E2E.csproj` |
| `_FakeClaudeOutputDir` | 1 | `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj` |
| `_FakeGrokOutputDir` | 1 | `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj` |
| `_GitHeadShaTrimmed` | 1 | `Directory.Build.props` |
| `_LandFakeGrokDir` | 1 | `tests/Antiphon.E2E/Antiphon.E2E.csproj` |
| `_PtyHostNativeDir` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
| `_StagedHelperDest` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
| `_StagedHelperDir` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
| `_StagedHelperGlob` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
| `_StagedHelperShared` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
| `AntiphonCleanFileMaxBytes` | 1 | `Directory.Build.targets` |
| `AntiphonMessagingVersion` | 1 | `samples/EchoGateway/EchoGateway.csproj` |
| `AssemblyName` | 27 |  |
| `Authors` | 1 | `src/Messaging.Pack.props` |
| `ConcurrentGarbageCollection` | 1 | `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj` |
| `DefaultItemExcludes` | 2 | `Directory.Build.props`, `scripts/fixtures/nightly/c1375-probe/Probe.csproj` |
| `Description` | 11 |  |
| `EnableMicrosoftTestingPlatformRunner` | 12 |  |
| `EnablePublicApiAnalyzers` | 2 | `src/Antiphon.Messaging.FakeGateway/Antiphon.Messaging.FakeGateway.csproj`, `src/Messaging.Pack.props` |
| `FakeBrowserOutputDir` | 1 | `tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj` |
| `ImplicitUsings` | 46 |  |
| `InvariantGlobalization` | 4 |  |
| `IsAspireHost` | 1 | `Antiphon.AppHost/Antiphon.AppHost.csproj` |
| `IsPackable` | 12 |  |
| `IsTestProject` | 12 |  |
| `Nullable` | 46 |  |
| `OutputType` | 20 |  |
| `PackageId` | 2 | `src/Antiphon.Messaging.FakeGateway/Antiphon.Messaging.FakeGateway.csproj`, `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` |
| `PackageLicenseExpression` | 1 | `src/Messaging.Pack.props` |
| `PackageProjectUrl` | 1 | `src/Messaging.Pack.props` |
| `PackageReadmeFile` | 1 | `src/Messaging.Pack.props` |
| `PackageTags` | 1 | `src/Messaging.Pack.props` |
| `PackAsTool` | 2 | `src/Antiphon.Messaging.FakeGateway/Antiphon.Messaging.FakeGateway.csproj`, `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` |
| `PublishRepositoryUrl` | 1 | `src/Messaging.Pack.props` |
| `RepositoryUrl` | 1 | `src/Messaging.Pack.props` |
| `RollForward` | 1 | `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` |
| `RootNamespace` | 31 |  |
| `ServerGarbageCollection` | 1 | `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj` |
| `SourceControlInformationFeatureSupported` | 1 | `Directory.Build.props` |
| `SourceRevisionId` | 1 | `Directory.Build.props` |
| `TargetFramework` | 46 |  |
| `ToolCommandName` | 2 | `src/Antiphon.Messaging.FakeGateway/Antiphon.Messaging.FakeGateway.csproj`, `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` |
| `UseAppHost` | 1 | `tests/Antiphon.ScriptHarnessHost/Antiphon.ScriptHarnessHost.csproj` |
| `UsePublishedPackages` | 1 | `samples/EchoGateway/EchoGateway.csproj` |
| `UserSecretsId` | 2 | `Antiphon.AppHost/Antiphon.AppHost.csproj`, `server/Antiphon.Server.csproj` |
| `Version` | 2 | `src/Messaging.Pack.props`, `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` |
| `WarningsAsErrors` | 1 | `src/Messaging.Pack.props` |

Item types (18 declared in an `ItemGroup`) plus four set only by a task `Output` `ItemName` (`_CustodyChildTargetPath`, `_FakeClaudeTargetPath`, `_FakeGrokTargetPath`, `_LandFakeGrokTarget`), and `EditorConfigFiles` and `GlobalAnalyzerConfigFiles` from the review's R3, which no tracked file declares:

| Item type | Files | Where (when one or two) |
|---|---:|---|
| `_AntiphonRepeatLine` | 1 | `tests/Shared/CheckpointRepeat.targets` |
| `_StagedHelperFiles` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
| `_StagedHelperTarget` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
| `AdditionalFiles` | 1 | `src/Messaging.Pack.props` |
| `Compile` | 11 |  |
| `Content` | 8 |  |
| `CustodyChildOutput` | 2 | `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj`, `tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj` |
| `EmbeddedResource` | 3 |  |
| `FakeBrowserFile` | 1 | `tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj` |
| `FakeClaudeOutput` | 1 | `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj` |
| `FakeGrokOutput` | 1 | `tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj` |
| `FrameworkReference` | 5 |  |
| `InternalsVisibleTo` | 12 |  |
| `LandFakeGrokOutput` | 1 | `tests/Antiphon.E2E/Antiphon.E2E.csproj` |
| `None` | 9 |  |
| `PackageReference` | 30 |  |
| `ProjectReference` | 27 |  |
| `StagedHelper` | 1 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` |
