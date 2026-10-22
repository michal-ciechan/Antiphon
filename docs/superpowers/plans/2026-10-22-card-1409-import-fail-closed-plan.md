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
| CP-1 | S25 | `tests/Antiphon.Tests -> bin-c1409/` | c1409-imports | `/*/*/LandNonCodeSkipTests/(C1409_*)\|(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)\|(C1380_*)` | V-1 V-2 V-3 V-4 V-5 V-7 V-8 V-9 V-10 V-11 V-12 V-13 V-14 V-15 V-16 V-17 V-18 V-19 V-20 V-21 V-22 V-23 V-24 V-25 V-26 V-27 V-28 V-29 V-30 | exactly 83 executed, 0 failed/skipped | 83 | 15 |

### Cost

Ordinary floor 15 minutes for the one row (the 42 `C1380_*` harness cases dominate), plus the unlisted red runs before S2, S6, S9, S12, S15, S18 and S21 and the unlisted green subset runs after S12, S15, S18 and S21.

## Repair 3: default-deny (Review 31c77116)

Final Review 31c77116 found three remaining classes of fail-open after three rounds of enumerated fixes: R1 response files were hashed but never parsed; R2 about thirty SDK imports are driven by ordinary properties (`CustomAfterMicrosoftCommonTargets`, `DirectoryBuildTargetsPath`, `PublishProfile` and their kin) that a name deny-list cannot close; R3 item types the audit did not watch (`EditorConfigFiles`, `GlobalAnalyzerConfigFiles`, `ProjectReference`) accepted docs markdown. The audit only gates `-Update`; a changed definition file is never a docs-only land. The design therefore changes to default-deny: the script carries allow-lists seeded from the tracked definition files, and every construct outside them refuses by name. Branch `feat/card-task-726f1f91`, start ref `9eab17ecfb73cf3f968ce8abba8141282a6a81c9`.

**D-13 — Property names are allow-listed.** Every child element of a `PropertyGroup`, wherever the group sits (under `Project`, inside a `Target`), and every task `Output` with a `PropertyName`, must name one of the 54 properties in the inventory below; names compare case-insensitively, as MSBuild does. Anything else refuses, naming the file and the property as written. The D-12 names keep their own message because that check runs first. A new property is a reviewed edit of the list in `scripts/audit-land-verification-graph.ps1`.

**D-14 — Item types are allow-listed with a rule each.** Every child element of an `ItemGroup`, wherever the group sits, and every `Output` `ItemName`, must be one of the 24 item types below; an unknown type refuses by type. `Include` is split on `;` and each part follows the type's rule. A part that contains `@(` or `%(` refuses for every type. *path* (`Compile`, `EmbeddedResource`, `Content`, `None`, `AdditionalFiles`, `EditorConfigFiles`, `GlobalAnalyzerConfigFiles` and the repository's thirteen target-time items): resolved from the file's own directory and from every importing project's directory as CARD-1400 already did; `escape` and a forbidden unknown property keep the docs refusal; a part that leaves the repository (absolute, or `..` past the root) now refuses as unclassified where it was accepted before; an unresolved `$(...)` keeps the CARD-1400 spelling heuristic; a resolved pattern that selects `docs/**/*.md` or the root `README.md` refuses. *project* (`ProjectReference`): no `$(`, `*`, `?`; the resolved path from every directory must be a `*proj` tracked at HEAD, otherwise the message names the part and ` -> <resolved>`. *name* (`PackageReference`, `FrameworkReference`, `InternalsVisibleTo`): after one `%XX` decode the part contains none of `/`, `\`, `$(`, `*`, `?`, `..`, `:`. The brief's draft rule, "file items only inside the project's own directory and never `docs/**`", was relaxed to this after reading the inventory: ten projects link `Compile` and three link `Content` from sibling directories, and `tests/Antiphon.Tests` and `tests/Antiphon.Agents.Pty.Tests` embed `docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json`; the skip policy already treats docs JSON as a build input, so `docs/**/*.md` and the root README are the only paths that matter. Item metadata, `Exclude`, `Remove` and `Update` are not evaluated: they add no input.

**D-15 — Structure is allow-listed.** The root must be `<Project>`; each `;`-part of its `Sdk` attribute must be `Microsoft.NET.Sdk` or `Microsoft.NET.Sdk.Web`; its children must be `PropertyGroup`, `ItemGroup`, `Import`, `Target` or `Sdk`, and a `<Sdk>` element must name `Aspire.AppHost.Sdk`. `UsingTask`, `ImportGroup`, `Choose`, `ItemDefinitionGroup`, `ProjectExtensions` and every other element refuse naming the file and the element. Element names compare ordinally, as XML does.

**D-16 — Response files are parsed (R1).** Every tracked `.rsp` is read; a leading BOM is dropped, lines are trimmed, blank lines and `#` comments are skipped, and each remaining line must be exactly one switch: `-` or `/`, then `nodeReuse`/`nr` with `true` or `false`, `maxcpucount`/`m` with no value or 1 to 9999, `verbosity`/`v` with `q`, `quiet`, `m`, `minimal`, `n`, `normal`, `d`, `detailed`, `diag` or `diagnostic`, or `nologo` with no value. Anything else refuses naming the file and the line: `-p:`, `-property:`, `@file`, `/t:`, an unknown value, or two switches on one line. The real `Directory.Build.rsp` holds a comment block and `-nodeReuse:false`.

**D-17 — The real repository passes unchanged.** `-Check` still prints digest `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F` count=57; that is the acceptance test that the lists are not too tight. `LandVerificationBuildGraph.Includes` is unchanged (a comment records why): `.rsp` was already selected, and `EditorConfigFiles`/`GlobalAnalyzerConfigFiles` are item types, not files. Three existing fixtures used names outside the inventory and now use inventory names with unchanged assertions: `ActiveDebugProfile` became `Nullable` in `C1409_ProjectUserFileIsAuditedAndImported`, `PackageVersion` became `PackageReference` in `C1409_DirectoryPackagesPropsIsImportedByTheNearestProject`, and `_CleanFile` became `_AntiphonCleanFileChars`, the real `Directory.Build.targets` shape, in `C1409_IntermediateOutputRedirectRefuses`. `PackageVersion` is not allow-listed because no tracked file uses it; adopting central package management is a reviewed edit of the list. V-10's witness also changed: its shadowed root `Directory.Packages.props` relied on `../docs/**/*.md` being silently accepted when it leaves the repository from the file's own directory, which D-14 now refuses, so `C1409_DirectoryPackagesPropsIsImportedByTheNearestProject` now proves nearest-only import through an `Import Project="eng/docs.items"` that the audit follows, and refuses as an uninspected target, only when the root file is the nearest one; shadowed, the root file is still parsed against the allow-lists and counted (`count=3`). A definition file no project reaches has its imports left unfollowed, as MSBuild leaves them unevaluated; its own properties, items and structure are still checked.

**D-18 — Out of scope, and why.** Machine-wide `ImportBefore`/`ImportAfter` under `$(MSBuildExtensionsPath)` and the SDK's own targets are toolchain state outside the tree; `global.json` is hashed, their contents are not in HEAD. NuGet package `build/*.props` and `.targets` live in the package cache, addressed by the `PackageReference` id and version the digest already covers. Solution-level files are hashed, not parsed; a tracked `Directory.Solution.props` or `.targets` is parsed as project XML and meets the same allow-lists. Target bodies are not evaluated: a task parameter (`Exec`, `Copy`, `ReadLinesFromFile`) or a property value can name any file and the audit refuses no task. That is unchanged since CARD-1400 and is accepted because such an edit is a definition-file change, which never lands as docs-only and is reviewed, while R1 to R3 lived in what the SDK evaluates at import time, which the allow-lists now bound.

**D-19 — Verification scope.** CP-1 adds `C1380_*` as the brief requires: the server inventory file changes (comment only) and the land policy depends on it. Expected 72 executed: 16 `C1409_*`, 10 `C1400_*`, 3 `C1390_*`, `C1385_RegistryMatchesNarrowGraph`, 42 `C1380_*` cases. Repair 4 raises it to 77 (21 `C1409_*`; D-27).

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

## Repair 4: classify the resolved result (Review 937bb1c9)

Final Review 937bb1c9 (report `.antiphon/task-937bb1c9.md` on `feat/card-task-937bb1c9`, server2 mirror `/work/worktrees/task-937bb1c9`) confirmed the default-deny design: all 24 earlier reproductions refuse, real-repo `-Check` prints the digest with count=57, ordinary edits pass. It found three fail-opens left, all in how file ITEMS are classified by spelling: F1 the docs-glob test checked a wildcard segment against the sample names `a.md`, `dir` and `a`, so `../docs/guide.*`, `g*`, `*guide*`, `guide.m?`, `**/guide*` and `s*/**/*.md` passed, and widening `tests/Antiphon.Tests/Antiphon.Tests.csproj:141` to `...card-0449-claude-effort-dialog-*` passed with count=57 while MSBuild embedded the readiness plan; F2 an unresolved `$(...)` passed unless its text spelled `docs` and `.md`, so `Include="$(Description)"` with `Description=../docs/**/*.md`, `$(RootNamespace)cs/*.md` and `$(MSBuildStartupDirectory)/docs/**` passed; F3 `ProjectReference AdditionalProperties="CustomAfterMicrosoftCommonTargets=.../docs/build.md"` re-enabled the R2 property import through item metadata. Branch `feat/card-task-5406fd51`, start ref `78063d332761ee1ebe63cc6a6ca498b4ceaeabb5`; this task lands as its own owner.

**D-21 — A path item is classified by what it resolves to.** Every `;`-part of a path item's `Include` and `Update` (the rule of D-14) is expanded, then resolved from the file's own directory and from every importing project's directory as before, then classified by the result: the root `README.md` refuses (real glob walk, unchanged); an exact path under `docs/` refuses unless it is the one allow-listed input (D-23), tracked or not, since a docs file can be added by a docs-only land; a wildcard pattern whose first segment is `**` or can match `docs` refuses whatever it matches today, for the same reason, and the message names the first tracked docs match when there is one; every other wildcard pattern is expanded against every blob tracked at HEAD with a real glob matcher (`*` and `?` inside a segment, `**` across segments, case-insensitive as on the Windows land host), minus the element's `Exclude` parts resolved the same way, and a match under `docs/` or a document match outside the document allow-list (D-24) refuses naming that path. `Remove` only subtracts and is not classified; `Exclude` cannot add and is honoured only for the tracked expansion. The sample-name tests `Test-SegmentCanBeMarkdownFile`, `Test-SegmentCanBeDirectory`, `Test-AlignDocs` and the spelling heuristic `Test-UnresolvedIncludeIsForbidden` are deleted. `_AntiphonRepeatLine` gets its own rule, `text`: its includes are C# source lines handed to `WriteLinesToFile`, never paths, and the `@(`/`%(` refusal still applies.

**D-22 — `$(...)` in a path item is expanded with MSBuild semantics from the audit's own property table, and what cannot be resolved statically refuses by name.** Before any file is classified the audit records every `PropertyGroup` definition (name, value, defining file, whether conditional), every task `Output` with the task that fills it, every item declared with an `Include` and its metadata, and every `PackageReference` with `GeneratePathProperty="true"` (its `Pkg<Id>` path property). A reference expands to the union of every value it can take: `$(MSBuildThisFileDirectory)` is the directory of the file holding the text, also inside a property value defined elsewhere; `$(MSBuildProjectDirectory)` is the evaluating project's directory; an output root (`IntermediateOutputPath`, `BaseIntermediateOutputPath`, `MSBuildProjectExtensionsPath`, `OutDir`, `OutputPath`, `BaseOutputPath`, `TargetDir`, `TargetPath`, `PublishDir`) is a build product root, because the allow-list and D-12 stop a definition file from setting one, the parsed `.rsp` cannot, and the land build passes no `-p:`; a `Pkg*` property with a matching `GeneratePathProperty` reference is a package-cache root; a property filled only by `MSBuild` task `Output`s is a build product (its `TargetOutputs` are other projects' outputs), and so are `@(Item->'%(RootDir)%(Directory)')` over an item filled only by `MSBuild` task `Output`s and never declared with an `Include`, and `$([System.IO.Path]::GetDirectoryName('$(P)'))` over such a property; `%(Item.Metadata)` takes every declared value of that metadata across the item's declarations, and the empty value when a declaration lacks it; every other property takes every `PropertyGroup` value across the tracked files, each expanded where it is defined, plus the empty value when any definition is conditional. A root (directory property, output, package) must be the first token. Anything else refuses naming the file, the item, the part and the reference: a property no tracked file defines (`$(MSBuildStartupDirectory)`, `$(SolutionDir)`, `$(DocsDir)`), an `Exec` or `ReadLinesFromFile` output, any other property function or item transform, an `@(`/`%(` elsewhere, a root in the middle of the text, a self-referencing value, or more than 64 candidates. A path rooted at an output or package root is a build product and accepts any tail that has no `..` segment and no drive letter; `$(FakeBrowserOutputDir)/Antiphon.MarkdownPdf.FakeBrowser.*` keeps its leading separator because `GetDirectoryName` strips the trailing one. The six real property-driven includes (`$(_CustodyChildOutputDir)*`, `$(_FakeClaudeOutputDir)**\*`, `$(_FakeGrokOutputDir)**\*`, `$(_LandFakeGrokDir)**\*`, `$(FakeBrowserOutputDir)/...`, `$(_StagedHelperDir)$(_StagedHelperGlob)` with `Glob` of `*` or `**\*`), `$(_AntiphonRepeatFile)` under `$(IntermediateOutputPath)` and `$(PkgMicrosoft_Windows_Console_ConPTY)\runtimes\...` resolve this way; the review's suggested blanket refusal of unexpanded properties would have refused them. Item includes inside a `Target` are expanded with the same table, so a target-time include is bounded by the same rule, which narrows D-18 for items while task parameters stay out of scope.

**D-23 — docs/ by exact allow-listed path, never by pattern.** `docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json` is the only path under `docs/` a definition file may select, and only as an exact resolved path; a wildcard that could select it refuses. The tracked `docs/investigations/evidence/card-0462/Reproduce.csproj` lives under `docs/`: its own items resolve outside `docs/` (`../../../../tests/...`, `../../../../src/...`) and its default globs are the review's accepted "root project default globs" point, so nothing changes for it, but the shared root files are also evaluated from its directory (D-25 is scoped accordingly).

**D-24 — Markdown and text documents outside docs/ are allow-listed from the real tree.** A document is a `.md`, `.markdown` or `.txt` file. It never skips a land build (`LandVerificationPolicy` treats only `docs/**/*.md` and the root `README.md` as docs-only), so this is not a fail-open; the brief asks the audit to name what it accepts, and the list is seeded from the tracked definition files: the resolved patterns `server/Bundles/*.md`, `server/Bundles/Presets/*.md`, `server/Bundles/Fragments/*.md` (embedded bundles), `tests/Antiphon.Agents.Pty.Tests/golden/**/*`, `tests/Antiphon.Agents.Pty.Tests/Fixtures/CodexStartup/**/*`, `tests/Antiphon.Agents.Pty.Tests/probes/**/*`, `tests/Antiphon.Tests/Agents/Fixtures/**` and `tests/Antiphon.Messaging.Tests/Fixtures/*` (fixture globs that select 25 provenance and golden documents today and grow with fixtures, so an exact-path list would break ordinary fixture additions); the bare names `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt` and `slow-tests-allowlist.txt` the shared props and test projects use; a `README.md` beside a non-root project (`src/Messaging.Pack.props` packs one); and the exact path `server/Application/Services/LandVerificationBuildGraph.manifest.txt`. An allow-listed pattern accepts all its matches; otherwise each document match must carry an allow-listed name or path, and the refusal names the match. One fixture changed with its assertion unchanged: `C1409_SemicolonImportAuditsEveryPart` used `None Include="notes.txt"` as its harmless include, which is now a document outside the list, so it uses `notes.json`. A new definition-file glob that selects documents is a reviewed edit of the list, in the same change.

**D-25 — Property values are checked cheaply (the review's disclosed D-18 items).** Every `PropertyGroup` child value, wherever it sits: a property function that reads the file system (`ReadAllText`, `ReadAllBytes`, `ReadAllLines`, `ReadLines`, `GetFiles`, `GetDirectories`, `GetFileSystemEntries`, `Enumerate*`) must take a first argument that starts with an output-root property, as the real `_AntiphonCleanFileChars` does with `$(IntermediateOutputPath)$(CleanFile)`; anything else refuses naming the file, the property and the call. A literal `;`-part (no `$(` after the two directory properties, no `@(`, `%(` or `$([`, no wildcard) that carries a path separator or a document extension is resolved from the file's own directory only, and a `docs/` path or the root `README.md` refuses naming the resolved path. Importing project directories are not used here: the root `Directory.Build.props` is imported by the `docs/` project of D-23, which would turn every literal value into a docs path; an include that uses a value is expanded from every directory where it is used (D-22). A wildcard value part is an exclude list or a glob an include expands where it is used. `Exists` and `Condition` text are not checked: neither reads content into an input.

**D-26 — Item metadata names are allow-listed per item type (F3).** Every attribute of an item element other than `Include`, `Exclude`, `Update`, `Remove`, `Condition` and `Label` (compared ordinally), and every child element, is metadata and must be in the type's list, compared case-insensitively as MSBuild does; a type absent from the table allows none. Seeded from the inventory below: `PackageReference` {`Version`, `GeneratePathProperty`, `ExcludeAssets`, `PrivateAssets`, `IncludeAssets`}, `ProjectReference` {`Aliases`, `ReferenceOutputAssembly`, `Private`}, `Content` {`CopyToOutputDirectory`, `CopyToPublishDirectory`, `Link`, `TargetPath`}, `Compile` {`Link`}, `EmbeddedResource` {`LogicalName`}, `None` {`Pack`, `PackagePath`, `CopyToOutputDirectory`}, `StagedHelper` {`Folder`, `Glob`}. `AdditionalProperties`, `Properties`, `GlobalProperties`, `SetConfiguration`, `SetPlatform`, `SetTargetFramework`, `UndefineProperties`, `DependentUpon`, `KeepMetadata`, `KeepDuplicates` and every other name refuse naming the file, the item, its `Include=`/`Update=`/`Remove=` text and the name. The brief's wider candidate list (`SkipGetTargetFramework`, `GlobalPropertiesToRemove`, `OutputItemType`) is not seeded because no tracked file uses it.

**D-27 — Acceptance.** `-Check` on the real repository prints `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F` count=57 (V-7, `C1385_RegistryMatchesNarrowGraph`), every tracked definition file passing unchanged; an ordinary edit (new `PackageReference`, new linked `Compile`, new `ProjectReference` with `ReferenceOutputAssembly="false"`) passes `-Update` with count=57 in a scratch worktree; the real `tests/Antiphon.Tests/Antiphon.Tests.csproj` passes in a scratch tree and refuses once widened (V-20). CP-1 expects 77: 21 `C1409_*`, 10 `C1400_*`, 3 `C1390_*`, `C1385_RegistryMatchesNarrowGraph`, 42 `C1380_*`. `LandVerificationBuildGraph.Includes` is unchanged. Still out of scope, as D-18 says: task parameters (`Copy SourceFiles`, `Exec`, `ReadLinesFromFile`, the `MSBuild` task's `Properties`), `Condition` text, and an `Exec`-filled property used as a path (it refuses as unresolvable when used in an include, D-22). Environment variables are not in the table: an undefined property refuses rather than resolving to the empty value, so a `$(HOME)`-style include refuses.

### Repair 4 slices

**S14 — Red witnesses.** `C1409_DocsGlobRefusesByResolvedPath`, `C1409_RealTestProjectWidenedToPlanGlobRefuses`, `C1409_PropertyDrivenIncludeIsResolved`, `C1409_ReferenceMetadataIsAllowListed`, `C1409_PropertyValueNamingDocsRefuses`. One unlisted red run at the witness commit (reason: red proof before the fix; counts in the report).

**S15 — Fix.** `scripts/audit-land-verification-graph.ps1` D-21 to D-26 (ASCII; parses under Windows PowerShell 5.1); the D-24 fixture rename. One unlisted green run of `(C1409_*)|(C1400_*)|(C1390_*)` at the fix commit (reason: catch a fixture regression before the checkpoint build).

**S16 — Wording.** This section, `docs/orchestration-loop.md`, the script header; CP-1 after S16 expecting 77.

### Repair 4 verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-19 | From `server/App.csproj`, `EmbeddedResource` includes `../docs/guide.*`, `../docs/g*`, `../docs/*guide*`, `../docs/guide.m?`, `../docs/**/guide*`, `../docs/s*/**/*.md`, `../docs/future/*.md` (matches nothing tracked) and `../**/*.md` each refuse naming the include; `Content Include="fixtures/**"` over a tracked `server/fixtures/notes.md` refuses naming ` -> server/fixtures/notes.md`; `Bundles\*.md` with `Exclude="Bundles\README.md"`, `Fixtures\*` with no document and `slow-tests-allowlist.txt` are approved with `count=1`. | `C1409_DocsGlobRefusesByResolvedPath` |
| V-20 | The real `tests/Antiphon.Tests/Antiphon.Tests.csproj`, the real root `Directory.Build.*` files and `tests/Shared/CheckpointRepeat.targets`, stub projects at every `ProjectReference`, and the two card-0449 docs files pass `-Update` in a scratch tree; the same tree with line 141 widened to `...card-0449-claude-effort-dialog-*` refuses naming the include and `docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-readiness-plan.md`. | `C1409_RealTestProjectWidenedToPlanGlobRefuses` |
| V-21 | `Include="$(Description)"` with `Description=../docs/**/*.md` and `$(RootNamespace)cs/*.md` with `RootNamespace=../do` refuse as docs markdown; `$(MSBuildStartupDirectory)/docs/**` and `$(PkgSome_Package)\build\a.dll` without a `GeneratePathProperty` reference refuse as unresolvable naming the property; a target-time `$(IntermediateOutputPath)../../docs/guide.md` refuses as unclassified; the real shapes (`GeneratePathProperty` package root, `MSBuild` task output transform with a `%(StagedHelper.Glob)` tail, `GetDirectoryName` over a task output, `$(IntermediateOutputPath)` file) are approved with `count=2`. | `C1409_PropertyDrivenIncludeIsResolved` |
| V-22 | `ProjectReference AdditionalProperties=...`, a `<Properties>` child, `PackageReference SetConfiguration` and `Compile DependentUpon` each refuse naming the item, its include and the metadata name; `Aliases`, `ReferenceOutputAssembly`, `Private`, `Version`, `PrivateAssets`, `ExcludeAssets`, an `<IncludeAssets>` child, `Link`, `CopyToOutputDirectory`, `CopyToPublishDirectory`, `LogicalName`, `Pack`, `PackagePath` and a `<TargetPath>` child on `Update` are approved with `count=2`. | `C1409_ReferenceMetadataIsAllowListed` |
| V-23 | `ReadAllText('$(MSBuildThisFileDirectory)../docs/guide.md')` in `Description`, `PackageReadmeFile=../docs/guide.md` and `GetFileSystemEntries('docs')` in a root `Directory.Build.props` each refuse naming the file, the property and the call or path; the real ledger read under `$(IntermediateOutputPath)`, `PackageReadmeFile=README.md` beside a non-root project, a description with a slash and the self-referencing `DefaultItemExcludes` are approved with `count=4`. | `C1409_PropertyValueNamingDocsRefuses` |

V-7 remains the acceptance witness for D-27. No new PC rows: the refusal predicates are the mutation surface Review already named, and the expansion table is exercised by the approved controls of V-21 to V-23.

### Repair 4 inventory additions (tracked definition files at `78063d332761ee1ebe63cc6a6ca498b4ceaeabb5`)

Item metadata in use (attribute or child element): `PackageReference` Version, GeneratePathProperty, ExcludeAssets, PrivateAssets, IncludeAssets; `ProjectReference` Aliases, ReferenceOutputAssembly, Private; `Content` CopyToOutputDirectory, CopyToPublishDirectory, Link, TargetPath; `Compile` Link; `EmbeddedResource` LogicalName; `None` Pack, PackagePath, CopyToOutputDirectory; `StagedHelper` Folder, Glob. `Exclude` appears once (`server/Antiphon.Server.csproj`, `Bundles\README.md`); `Update` once (`Content Update="Runtime\claude-remote-control-off.json"`); `Remove` three times (`None`, `_StagedHelperTarget`, `_StagedHelperFiles`, the last two as `@(...)`).

Property-driven includes: `$(_CustodyChildOutputDir)*` (two files), `$(_FakeClaudeOutputDir)**\*`, `$(_FakeGrokOutputDir)**\*`, `$(_LandFakeGrokDir)**\*`, `$(FakeBrowserOutputDir)/Antiphon.MarkdownPdf.FakeBrowser.*`, `$(_StagedHelperDir)$(_StagedHelperGlob)`, `$(_AntiphonRepeatFile)`, `$(PkgMicrosoft_Windows_Console_ConPTY)\runtimes\win-x64\native\conpty.dll` and `\build\native\runtimes\x64\OpenConsole.exe`, plus `$(MSBuildThisFileDirectory)` anchors. Their roots are `MSBuild` task outputs (`_CustodyChildTargetPath`, `_FakeClaudeTargetPath`, `_FakeGrokTargetPath`, `_LandFakeGrokTarget`, `_StagedHelperTarget`, `FakeBrowserTargetPath`), `$(IntermediateOutputPath)` and the ConPTY package path property. `Exec` outputs: `_GitHeadSha`, `_GitRevParseExitCode` (never used in a path).

Documents selected by file items at HEAD: 21 bundle, preset and fragment markdown files under `server/Bundles/` (plus the excluded `server/Bundles/README.md`); 25 fixture `.md`/`.txt` files under the three `tests/Antiphon.Agents.Pty.Tests` globs and `tests/Antiphon.Tests/Agents/Fixtures/`; eight `src/*/README.md`; `PublicAPI.*.txt` per messaging project; `slow-tests-allowlist.txt` in seven test projects; the land-graph manifest.


## Repair 5: one path normaliser and the item-operation table (Review f0fb9035)

Final Review f0fb9035 (report `.antiphon/task-f0fb9035.md` on `feat/card-task-f0fb9035`) confirmed the resolved-path design and found two bypasses that pass `-Update` on the real tree at count=57: F1 the docs, README, document and output-root rules compared the resolved text literally, so `../README.md.`, `../docs./guide.md.`, `../docs./*.md`, `../README.md::$DATA` and `../README.md. .` were approved although the Windows land host strips trailing dots and spaces per segment and `::$DATA` opens the file itself; F2 `Register-DefinitionFacts` recorded item metadata only from elements with `Include`, so a `StagedHelper Update=... Glob="..\..\..\..\..\docs\**\*.md"` fed `%(StagedHelper.Glob)` a value the expansion never saw. The Review also disclosed, out of contract, that a tracked symbolic link is opaque to the tree expansion. This round stops adding example checks: one normaliser every comparison goes through, one table of every MSBuild item operation, and the symlink refusal. Branch `feat/card-task-e295bb13`, start ref `5471da833f9e4b4b1cf08dd4bbd70584d67220f8`; this task lands as its own owner and the whole CARD-1409 delta lands with it.

**D-28 — One canonical path normaliser, and a segment that would change refuses by name.** `ConvertTo-CanonicalPath` is the only function that turns resolved text into a repository path, and every comparison consumes its result: a path item's `Include` and `Update` parts and their `Exclude` parts, an `Import` target, a `ProjectReference`, a literal property-value path (D-25), the tail of a path rooted at an output or package root, every tracked match a wildcard selects, and every tracked definition path the audit parses (so `server/obj./x.props` is no longer a way past the `obj/` rule). The text is split on `/` and `\` (equal) and each segment is held to the Windows land host's rules before anything is compared: a trailing `.` or space, a segment of only dots or spaces, a `:` anywhere (the drive prefix `X:/` is converted to a repository path or refused as outside before this point, so it is the only legal colon), and an 8.3 short-name shape (`~` followed by a digit) each REFUSE naming the segment and the reason; nothing is silently rewritten. Only after the segment rules are `.` and empty segments dropped and `..` popped (`outside` above the root, `escape` after a wildcard, as before). Letter case is not changed; every comparison of a canonical path is ordinal-ignore-case, which is the case-fold. The rules are the N-table below; `Normalize-RepoPattern` is replaced. The real tree has no tracked path or include with such a segment, so `-Check` still prints the digest with count=57.

**D-29 — The MSBuild item-operation surface is a closed table.** Every way an item element can introduce a path or a metadata value is enumerated in the O-table below and is either censused like `Include` or refused by name; an attribute that is none of the item-element attributes (`Include`, `Exclude`, `Update`, `Remove`, `Condition`, `Label`), none of the named refused operations (`KeepMetadata`, `RemoveMetadata`, `KeepDuplicates`, `MatchOnMetadata`, `MatchOnMetadataOptions`) and not an allow-listed metadata name refuses as metadata, so an operation outside the table has nowhere to hide. `Update` elements are censused like `Include` elements (F2): their path part is classified (D-21, unchanged) and their metadata values, static or inside a `Target`, join `ItemDeclarations` for `%(Item.Metadata)` expansion and the `Pkg*` path properties; an `Update` of an item also counts as a declaration, so the `@(Item->'%(RootDir)%(Directory)')` build-product rule needs an item never declared by `Include` or `Update`. `Remove` subtracts only and its text is not classified (the real `Remove="@(_StagedHelperTarget)"` idiom), but any metadata on a `Remove` element refuses. An element with none of `Include`, `Update` or `Remove` refuses by name. `Condition` is still not evaluated, so every value counts. `Exclude` is resolved through the normaliser (a divergent segment refuses) and subtracts from the tracked expansion only; an unresolvable `Exclude` part is dropped, so the audit sees more, never less.

**D-30 — A tracked symbolic link under a path item refuses.** Every `ls-tree` row with mode `120000` is recorded. A path item's resolved pattern refuses naming the link when the pattern reaches the link as a file (the link is a match) or as a directory it passes through (the link's path is matched by a prefix of the pattern; exact paths, `*`, `?` and `**` all count), because a link materialised by a land checkout with `core.symlinks=true` can point a non-definition change at `docs/` or at a document outside the allow-list without the digest changing. Imports and project references already require a tracked regular definition blob, so a link cannot stand in for one. No tracked path is a symlink today.

**D-31 — Witnesses: one per F, one table-driven case, one symlink case.** `C1409_WindowsPathNormalisationRefusesBySegment` (F1; the Review's five shapes from `server/App.csproj` and the real `tests/Antiphon.Tests/Antiphon.Tests.csproj` with `<EmbeddedResource Include="../../README.md." />` appended, in the V-20 scratch tree); `C1409_ItemUpdateMetadataFeedsTheCensus` (F2; the Review's real-project `StagedHelper Update` and a target-time `Update` on a small fixture, with the `**\*` control and a `PackageReference Update` with `GeneratePathProperty`); `C1409_NormaliserAndItemOperationTablesHaveWitnesses` reads the N- and O-tables from this plan and fails when a row has no witness (a `table` row must have an entry in the test, a named row must name an existing `C1409_*` method) or when an entry has no row, then runs every `table` entry; `C1409_TrackedSymlinkUnderItemPatternRefuses` tracks `120000` blobs through `git update-index --cacheinfo`, so no host needs symlink rights.

**D-32 — Acceptance.** `-Check` on the real repository prints `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F` count=57 (V-7); the ordinary edit of D-27 passes `-Update` with count=57 in a scratch worktree; the script stays ASCII-only. CP-1 expects 81: 25 `C1409_*`, 10 `C1400_*`, 3 `C1390_*`, `C1385_RegistryMatchesNarrowGraph`, 42 `C1380_*`. Out of contract and left as before (D-18, D-27): task parameters, `Condition` text, environment-variable properties, Windows reserved device names (`CON`, `NUL`; they select no tracked file), and a symlink that a `core.symlinks=false` checkout materialises as a text file.

### Repair 5 path normaliser rules

Every row is exercised by the `table` witness in `C1409_NormaliserAndItemOperationTablesHaveWitnesses`; the Through column names the comparison the witness drives the segment into.

| Rule | Segment shape | Treatment | Through | Witness |
|---|---|---|---|---|
| N-1 | trailing `.` (`../docs./guide.md`, `../README.md.`) | refused naming the segment | `EmbeddedResource Include` | table |
| N-2 | trailing dot and space mix (`../README.md. .`) | refused naming the segment | `EmbeddedResource Include` | table |
| N-3 | only dots or spaces (`../docs/.../guide.md`) | refused naming the segment | `EmbeddedResource Include` | table |
| N-4 | `:` anywhere (`../README.md::$DATA`, `../docs/guide.md:stream`, `Import Project="../eng/shared.props:x"`) | refused naming the segment | `Include`, `Import` | table |
| N-5 | 8.3 short name, `~` then a digit (`../DOCS~1/guide.md`, `ProjectReference Include="../lib/LIB~1.csproj"`) | refused naming the segment | `Include`, `ProjectReference` | table |
| N-6 | letter case (`../DOCS/Guide.MD`) | compared ordinal-ignore-case: refuses as docs markdown | `EmbeddedResource Include` | table |
| N-7 | `\` and `/` (`..\docs\guide.md`) | equal: refuses as docs markdown | `EmbeddedResource Include` | table |
| N-8 | `.` and empty segments (`.//../docs/./guide.md`) | dropped after the segment rules: refuses as docs markdown | `EmbeddedResource Include` | table |
| N-9 | `..` (`sub/../../docs/guide.md`; `../../outside.md`; `../docs/*/../guide.md`) | popped after the segment rules: docs markdown; outside refuses unclassified; after a wildcard refuses as escape | `EmbeddedResource Include` | table |
| N-10 | drive prefix (`C:/repo/README.md`; `C:README.md`) | the only legal colon: converted to a repository path or refused as outside; a drive-relative colon segment refuses naming the segment | `EmbeddedResource Include` | table |
| N-11 | output-root tail (`$(IntermediateOutputPath)docs./x.md` inside a `Target`) | refused naming the segment | `Compile Include` on an output root | table |
| N-12 | a tracked match (`Content Include="fixtures/**"` over a tracked `server/fixtures/notes.md.`) | refused naming the match and the segment | tracked expansion | table |
| N-13 | an `Exclude` part (`Bundles\*.md` with `Exclude="Bundles\README.md."`) | refused naming the segment | `Exclude` | table |
| N-14 | a literal property-value path (`<PackageReadmeFile>../docs./guide.md</PackageReadmeFile>`) | refused naming the property and the segment | property value (D-25) | table |
| N-15 | a tracked definition path (`server/obj./evil.props`) | refused naming the path and the segment | definition inventory | table |

### Repair 5 item-operation table

| Op | Operation | Where | Treatment | Witness |
|---|---|---|---|---|
| O-1 | `Include` | static `ItemGroup` | censused: each `;`-part classified by the type rule (D-14, D-21), metadata names allow-listed, values recorded | table |
| O-2 | `Include` | inside a `Target` | same as O-1 | table |
| O-3 | `Exclude` on an `Include` | both | subtract only: resolved through the normaliser, honoured for the tracked expansion, never adds; an unresolvable part is dropped | table |
| O-4 | `Update` path part | static `ItemGroup` | classified like `Include` | table |
| O-5 | `Update` path part | inside a `Target` | classified like `Include` | table |
| O-6 | `Update` metadata | both | names allow-listed per type; values recorded for `%(Item.Metadata)` and `Pkg*` (F2) | table |
| O-7 | `Remove` | static `ItemGroup` | subtract only, text not classified; any metadata on it refuses | table |
| O-8 | `Remove="@(...)"` | inside a `Target` | the real idiom, accepted | table |
| O-9 | `KeepMetadata`, `RemoveMetadata` | `Include` | refused by name | table |
| O-10 | `KeepDuplicates` | `Include` | refused by name | table |
| O-11 | `MatchOnMetadata`, `MatchOnMetadataOptions` | `Remove` | refused by name | table |
| O-12 | item transform `@(X->'...')` | `Include` or `Update` | refused as unclassified | table |
| O-13 | `%(...)` batching | `Include` or `Update` | refused as unclassified | table |
| O-14 | metadata as an attribute | `Include` or `Update` | name allow-listed per type (`DependentUpon` refuses, `Link` passes) | table |
| O-15 | metadata as a child element | `Include` or `Update` | name allow-listed per type (`Properties` refuses, `IncludeAssets` passes) | table |
| O-16 | `Condition` on the element or its group | both | not evaluated: every value counts | table |
| O-17 | no `Include`, `Update` or `Remove` | both | refused by name | table |
| O-18 | `ItemDefinitionGroup` default metadata | project | refused as an unclassified element (D-15) | table |
| O-19 | task `Output ItemName` | `Target` | type allow-listed; an item filled only by the `MSBuild` task is a build product, any other task refuses as unresolvable when used in a path | table |
| O-20 | any other attribute | any item element | metadata default-deny: refuses unless allow-listed | table |
| O-21 | a metadata value with `%(`, `@(` or an undefined `$(` used in a path | `%(Item.Metadata)` expansion | refuses as unresolvable | table |
| O-22 | a tracked symbolic link the pattern reaches | path item | refused naming the link (D-30) | `C1409_TrackedSymlinkUnderItemPatternRefuses` |

### Repair 5 slices

**S17 — Red witnesses and the tables.** `C1409_WindowsPathNormalisationRefusesBySegment`, `C1409_ItemUpdateMetadataFeedsTheCensus`, `C1409_NormaliserAndItemOperationTablesHaveWitnesses`, `C1409_TrackedSymlinkUnderItemPatternRefuses`; this section with the two tables, which the table-driven case reads. One unlisted red run at the witness commit (reason: red proof before the fix; counts in the report).

**S18 — Fix.** `scripts/audit-land-verification-graph.ps1` D-28 to D-30 (ASCII; parses under Windows PowerShell 5.1). One unlisted green run of `(C1409_*)|(C1400_*)|(C1390_*)` at the fix commit (reason: catch a fixture regression before the checkpoint build).

**S19 — Wording.** `docs/orchestration-loop.md`, the script header; CP-1 after S19 expecting 81.

### Repair 5 verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-24 | From `server/App.csproj`, `EmbeddedResource` includes `../README.md.`, `../docs./guide.md.`, `../docs./*.md`, `../README.md::$DATA` and `../README.md. .` each refuse naming the include and the segment; the real `tests/Antiphon.Tests/Antiphon.Tests.csproj` passes in the V-20 scratch tree and refuses once `<EmbeddedResource Include="../../README.md." />` is appended, naming `segment=README.md.`; `Compile Include="..\lib\Shared.cs"`, `Content Include="Fixtures\*"` and `None Include="slow-tests-allowlist.txt"` are approved with `count=1`. | `C1409_WindowsPathNormalisationRefusesBySegment` |
| V-25 | The real test project with `<StagedHelper Update="$(MSBuildThisFileDirectory)..\..\src\Antiphon.FakeGrok\Antiphon.FakeGrok.csproj" Glob="..\..\..\..\..\docs\**\*.md" />` appended refuses naming `_StagedHelperFiles Include=$(_StagedHelperDir)$(_StagedHelperGlob)`; a target-time `StagedHelper Update` with a `..` glob refuses the same way on a small fixture; the same `Update` with `Glob="**\*"` and a `PackageReference Update` with `GeneratePathProperty="true"` feeding `$(PkgSome_Package)\build\a.dll` are approved with `count=2`. | `C1409_ItemUpdateMetadataFeedsTheCensus` |
| V-26 | Every N- and O-row above has a witness: a `table` row has an entry in the test and a named row names an existing `C1409_*` method, an entry without a row fails, and every `table` entry runs with the row's outcome. | `C1409_NormaliserAndItemOperationTablesHaveWitnesses` |
| V-27 | A tracked `120000` blob `server/fixtures -> ../docs` refuses `Content Include="fixtures/**"` and `Content Include="fixtures/guide.md"` naming `-> server/fixtures`; a tracked `120000` blob `server/notes.jsonl -> ../docs/guide.md` refuses `Content Include="*.jsonl"` naming the link; a link elsewhere (`tools/link`) leaves `Content Include="fixtures/**"` over `server/fixtures/a.jsonl` approved with `count=1`. | `C1409_TrackedSymlinkUnderItemPatternRefuses` |

V-7 remains the acceptance witness for D-32. No new PC rows: the normaliser's refusal predicates and the operation table are the mutation surface Review already named; existing land PCs stay pending for SourceLanding Mutation.

## Repair 6: one expand-split-unescape pipeline (Review c64987ea)

Final Review c64987ea (report `.antiphon/task-c64987ea.md` on `feat/card-task-c64987ea`) closed F1 and F2 of repair 5 and found one new in-contract bypass class: `%XX` escapes were decoded only in the literal include text, before expansion, so a property or metadata value that splices in `%2E%2E` or `%64ocs` reached the root README or `docs/` without a refusal. The witness on the real tree: `<PackageTags>$(RootNamespace)/%2E%2E/%2E%2E/%2E%2E/README.md</PackageTags>` with `<None Include="$(PackageTags)" />` in `tests/Antiphon.Tests/Antiphon.Tests.csproj` passed `-Update` at count=57 while `dotnet msbuild -getItem:None` listed the root README. The same order defect let a decoded `%24(` be expanded as a reference, a `;` inside an expanded value go unsplit, and the D-25 separator-or-extension gate miss a value with no literal `/` or `.`. This round stops patching examples: MSBuild's one order is implemented once and every consumer runs through it. Branch `feat/card-task-8e3c334d`, start ref `fd20e8dccd3cdbe1f075a6973236dae3728a43a6`; this task lands as its own owner and the whole CARD-1409 chain lands with it, master first, then the rc port.

**D-33 — One pipeline, in MSBuild's order.** `Resolve-ItemPathText` is the only function that turns item or import text into path specs, and it does what MSBuild does, in MSBuild's order, read from a scratch `dotnet msbuild -getItem` run on server2 rather than from memory: expand properties and metadata (values are spliced in their escaped form, as MSBuild stores them), split the expanded text on `;` (an escaped `%3B` is not a separator; a `;` inside an expanded value is), then decode `%XX` once per part (two hex digits in either case become one character of that code, as MSBuild does, so `%C3%A9` is two characters; `%252E` becomes the literal segment `%2E`; a malformed `%X`, `%GG` or trailing `%` stays literal; `%00`, which MSBuild keeps as a NUL character, refuses by name), and only then hand each part to `ConvertTo-CanonicalPath` (D-28, unchanged). A decoded `$(`, `@(` or `%(` is literal text, never a reference, because references are read before the decode. MSBuild reads a decoded `*` or `?` as a literal character; the audit reads the decoded include as a wildcard, which selects the literal name and more, so an include refuses at least as much, while an `Exclude` part whose decoded text gained a wildcard character is dropped, so the audit sees more, never less. A root (the two directory properties, an output or package root) belongs to the first part of its expanded text only; later parts are ordinary project-relative paths, as they are for MSBuild. Every consumer is routed through the pipeline and the ad-hoc decode calls are gone: path-item `Include` and `Update` text, `Exclude` text, `Import Project` text (with the two directory properties only, D-1; an empty result is the existing empty-`Project` refusal), `ProjectReference` text, name items (`PackageReference`, `FrameworkReference`, `InternalsVisibleTo`), the `GeneratePathProperty` id that names a `Pkg*` property, literal property-value paths (D-25, whose separator-or-document gate now reads the decoded part and whose wildcard skip reads the escaped part), the tail of a file-reading property function's argument (D-25: after the output root, each `;`-piece decoded once, no `..`, no drive, no second root; a `$(Name)` no tracked file may set, such as the SDK's `$(CleanFile)`, is read as one opaque segment and an allow-listed or defined name expands from the census), and output-root tails. The pipeline is the only `;` split: the attribute text goes in whole, and a refusal names the whole attribute text with the resolved part. `Remove` has no path consumer (O-7: it subtracts and is not classified), and a tracked path from `ls-tree` is a literal name the pipeline never touches (N-12, N-15).

**D-34 — Witnesses.** `C1409_EscapeInExpandedTextIsDecodedOnce` (the Review's real-project shape and the `%64ocs` fixture, with the unmodified real tree as the approved control); the escape table below, every row with a witness, read by `C1409_NormaliserAndItemOperationTablesHaveWitnesses` as the N- and O-tables already are (zero rows fails, a `table` row needs an entry, an entry needs a row, a named row needs an existing `C1409_*` method).

**D-35 — Acceptance.** `-Check` on the real repository prints `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F` count=57 (V-7); the ordinary edit of D-27 passes `-Update` with count=57 in a scratch worktree; the script stays ASCII-only and parses under Windows PowerShell 5.1 rules. CP-1 expects 82: 26 `C1409_*`, 10 `C1400_*`, 3 `C1390_*`, `C1385_RegistryMatchesNarrowGraph`, 42 `C1380_*`. Out of contract and left as before (D-18, D-27, D-32): task parameters, `Condition` text, environment-variable properties (the SDK's `$(CleanFile)` in the real file-reading argument is read as one opaque segment because no tracked file may set it; an environment variable of that name is the stated residue), Windows reserved device names, and a symlink a `core.symlinks=false` checkout materialises as text.

### Repair 6 escape table

Every `table` row runs in `C1409_NormaliserAndItemOperationTablesHaveWitnesses`; N-16 is its own case. The Through column names the consumer the witness drives the escape into.

| Rule | Escape shape | Treatment | Through | Witness |
|---|---|---|---|---|
| N-16 | `%XX` in expanded property text (the real test project with `<PackageTags>$(RootNamespace)/%2E%2E/%2E%2E/%2E%2E/README.md</PackageTags>` and `<None Include="$(PackageTags)" />`; a fixture whose `PackageTags` is `$(Description)/%2E%2E/%2E%2E/README.md` and whose `PackageReadmeFile` is `$(Description)/%2E%2E/%2E%2E/%64ocs/data.json`) | expanded first, decoded once after: refuses as the root README (`-> README.md`) and as a docs path (`-> docs/data.json`) | `None Include`, `EmbeddedResource Include` through a property | `C1409_EscapeInExpandedTextIsDecodedOnce` |
| N-17 | `%2E`, `%2F`, `%5C` (`%2E%2E%2Fdocs%5Cguide.md`) | decoded once: `..` and both separators; refuses as docs markdown naming `-> docs/guide.md` | `EmbeddedResource Include` | table |
| N-18 | `%3B` (`../docs/guide.md%3Bx.json`; `RootNamespace` of `a.json%3B..` used as `$(RootNamespace)/docs/guide.md`) against a literal `;` in a value (`RootNamespace` of `a.json;..`) | an escaped semicolon stays inside one part: `docs/guide.md;x.json` refuses as a docs path, and the value part is the one segment `a.json;..`, which refuses by segment (trailing dots, N-1) with the decoded `;` inside it; a literal `;` in a value splits after expansion, so `../docs/guide.md` refuses as docs markdown | `EmbeddedResource Include` | table |
| N-19 | `%25` (`%252E%252E/%252E%252E/docs/guide.json`; `%2E%2E/docs/guide.md`) | decoded once: `%252E` becomes the literal segment `%2E`, so the first is approved as `server/%2E%2E/%2E%2E/docs/guide.json` (`count=1`) while the second refuses as docs markdown; a second decode would refuse the first as outside | `EmbeddedResource Include` | table |
| N-20 | `%XX` inside metadata (`StagedHelper Glob="%2E%2E/%2E%2E/docs/**/*.md"` reaching `$(_StagedHelperDir)$(_StagedHelperGlob)` through `%(StagedHelper.Glob)`) | the metadata value is spliced escaped and decoded once with the include: refuses as unclassified (`..` in an output-root tail) | `_StagedHelperFiles Include` | table |
| N-21 | `%XX` inside a property-function string argument (`GetDirectoryName('$(FakeBrowserTargetPath)%2E%2E')`; `ReadAllText('$(IntermediateOutputPath)%2E%2E/%2E%2E/docs/guide.md')`) | the audit expands only `$([System.IO.Path]::GetDirectoryName('$(P)'))`, with no literal text, so any other function text in an include refuses as unresolvable and there is nothing to decode; a file-reading function's first argument must start at an output root and its tail is decoded once and may not climb, so the `ReadAllText` refuses naming the property and the call | `FakeBrowserFile Include`, property value (D-25) | table |
| N-22 | hex letter case (`%2e%2e/docs/guide.md`) | decoded like upper case: refuses as docs markdown | `EmbeddedResource Include` | table |
| N-23 | malformed `%X`, `%GG`, trailing `%` (`../docs/g%GGide.md`, `../docs/guide.md%2`, `../docs/guide.md%`) | left literal, as MSBuild leaves them: refuses as docs markdown naming the literal path (`-> docs/g%GGide.md`) | `EmbeddedResource Include` | table |
| N-24 | `%00` (`None Include="a%00.json"`; `Import Project="../eng/shared%00.props"`) | MSBuild keeps a NUL character in the path; the audit refuses by name (unclassified include; unresolved import) | `Include`, `Import` | table |
| N-25 | `%2A`, `%3F` (`../docs/%2A.md`; `Exclude="fixtures/%2A.md"` over a tracked `server/fixtures/notes.md`) | MSBuild reads a literal `*` or `?`, never a wildcard; the audit reads the decoded include as a wildcard, which selects the literal name and more, so the include refuses as docs markdown; an `Exclude` part whose decoded text gained a wildcard character is dropped, so the tracked document is seen and refuses | `EmbeddedResource Include`, `Exclude` | table |
| N-26 | `%24(`, `%40(`, `%25(` (`../%24(X)/../docs/guide.md`; a value `%24(Y)` used as `../$(Description)/../docs/guide.md`) | a decoded `$(`, `@(` or `%(` is literal text, never a reference, because MSBuild decodes after it expands: the literal segment is popped by `..` and the path refuses as docs markdown, not as an unresolvable property | `EmbeddedResource Include` | table |
| N-27 | `%XX` in an `Import Project` (`%2E%2E/eng/%73hared.props`) and a `ProjectReference` (`../lib/%4Cib.csproj`); `Import Project="../eng/shared.props%3Bx.props"` | decoded once after the two directory properties: the import is followed and the reference resolves to the tracked project (`count=3`); the escaped semicolon is one part and refuses as a missing target `-> eng/shared.props;x.props` | `Import`, `ProjectReference` | table |
| N-28 | `%XX` in a literal property-value path (`<PackageReadmeFile>%2E%2E/docs/guide.md</PackageReadmeFile>`; `<PackageReadmeFile>%2E%2E%2Fdocs%2Fguide%2Emd</PackageReadmeFile>`, which has no literal `/` or `.`) | decoded once before the separator-or-document gate: both refuse naming the property and `-> docs/guide.md` | property value (D-25) | table |
| N-29 | `%XX` in a name item (`PackageReference Include="Some%2FPackage"`; `Some%2EPackage` with `GeneratePathProperty="true"` feeding `$(PkgSome_Package)\build\a.dll`) | decoded once: a decoded separator refuses as unclassified; the decoded id names `PkgSome_Package` and the rooted include is approved (`count=1`) | `PackageReference Include` | table |

### Repair 6 slices

**S20 — Red witnesses and the table.** `C1409_EscapeInExpandedTextIsDecodedOnce`; the N-17..N-29 entries of `C1409_NormaliserAndItemOperationTablesHaveWitnesses`; this section. One unlisted red run at the witness commit (reason: red proof before the fix; counts in the report).

**S21 — Fix.** `scripts/audit-land-verification-graph.ps1` D-33 (ASCII; parses under Windows PowerShell 5.1). One unlisted green run of `(C1409_*)|(C1400_*)|(C1390_*)` at the fix commit (reason: catch a fixture regression before the checkpoint build).

**S22 — Wording.** `docs/orchestration-loop.md`, the script header; CP-1 after S22 expecting 82.

### Repair 6 verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-28 | The unmodified real test project tree is approved; with `<PackageTags>$(RootNamespace)/%2E%2E/%2E%2E/%2E%2E/README.md</PackageTags>` and `<None Include="$(PackageTags)" />` appended it refuses naming `None Include=$(PackageTags) -> README.md`; a fixture with `Description=sub`, `PackageTags=$(Description)/%2E%2E/%2E%2E/README.md` and `PackageReadmeFile=$(Description)/%2E%2E/%2E%2E/%64ocs/data.json` refuses `None Include=$(PackageTags) -> README.md` and `EmbeddedResource Include=$(PackageReadmeFile) -> docs/data.json`. | `C1409_EscapeInExpandedTextIsDecodedOnce` |
| V-29 | Every N-16..N-29 row has a witness and every `table` entry runs with the row's outcome (V-26 extended to the escape table). | `C1409_NormaliserAndItemOperationTablesHaveWitnesses` |

V-7 remains the acceptance witness for D-35. No new PC rows: the pipeline's decode, split and root rules are the mutation surface Review already named; existing land PCs stay pending for SourceLanding Mutation.

## Repair 7: the D-25 part skip (Review 7db88932)

Final Review 7db88932 (second pass; report `.antiphon/task-7db88932.md` on `feat/card-task-7db88932`) found one regression repair 6 introduced and no new class: `Assert-PropertyValuesAllowed` (D-25) skipped a whole property value when any part of it carried `@(`, `%(` or `$([`, where `fd20e8dcc` applied that test to each `;`-part. On the real tree `<PackageReadmeFile>../../docs/guide.md;@(None)</PackageReadmeFile>` appended to `tests/Antiphon.Tests/Antiphon.Tests.csproj` is refused at `fd20e8dcc` (`-> docs/guide.md`) and approved at `ad13d6cd3` (`-Update` exit 0, count=57); the same with `;$([System.String]::Copy(''))` or `;%(None.Identity)`. The well-known-metadata gap (Review 8e8726fd F1) is pre-existing and stays CARD-1483. Branch `feat/card-task-18ac0f5a`, start ref `ad13d6cd36018d2eaedc39b80f99d5612fefd27c`; this task lands as its own owner and the whole CARD-1409 chain lands with it, master first, then the rc port.

**D-36 — The D-25 skip is per part.** The `@(`, `%(`, `$([` test runs inside the pipeline's spec loop on each part's escaped text (`Source`), as D-25 states it and as `fd20e8dcc` did: a part that carries a reference is left alone on its own (a value is not an input by itself; an include that uses it is classified where it is used), and every other part of the same value is still classified. Nothing else in `Assert-PropertyValuesAllowed` changes: the file-reading function rule, the wildcard skip, the separator-or-document gate and the folded docs/README refusal run as repair 6 left them, and a part the pipeline returns as `unresolved` (a reference past a leading directory property) is still skipped.

**D-37 — Witnesses.** `C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart`: the real test project tree is approved unmodified and refuses `-> docs/guide.md` naming `PackageReadmeFile` once `../../docs/guide.md;@(None)`, `../../docs/guide.md;$([System.String]::Copy(''))` or `../../docs/guide.md;%(None.Identity)` is appended; on a fixture the reference may come first (`@(None);../docs/guide.md`, `%(None.Identity);../docs/guide.md`) and the value still refuses; a value whose only part is a reference (`@(None)`, `%(None.Identity)`, `$([System.String]::Copy('../docs/guide.md'))`) and a value whose other part is not a document (`@(None);src/x.json`) are approved with `count=1`, as at `fd20e8dcc` (each outcome confirmed with the `fd20e8dcc` and `ad13d6cd3` scripts on a scratch fixture before the witness was written).

**D-38 — Acceptance.** `-Check` on the real repository prints `F240D0F1DBF9AA627A0A3637B52318B8030181B60F20E677C6E3DE2D2285B72F` count=57 (V-7); the ordinary edit of D-27 passes `-Update` with count=57 in a scratch worktree; the script stays ASCII-only and parses under Windows PowerShell 5.1 rules. CP-1 expects 83: 27 `C1409_*`, 10 `C1400_*`, 3 `C1390_*`, `C1385_RegistryMatchesNarrowGraph`, 42 `C1380_*`. The out-of-contract residue of D-35 is unchanged.

### Repair 7 slices

**S23 — Red witness.** `C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart`; this section; CP-1 moved to S25 expecting 83. One unlisted red run at the witness commit (reason: red proof before the fix; counts in the report).

**S24 — Fix.** `scripts/audit-land-verification-graph.ps1` D-36 (ASCII; parses under Windows PowerShell 5.1). One unlisted green run of the witness, `C1409_PropertyValueNamingDocsRefuses` and the table case at the fix commit (reason: catch a value-rule regression before the checkpoint build; same dll, the tests copy the script at run time).

**S25 — Wording.** `docs/orchestration-loop.md`, the script header; CP-1 after S25 expecting 83.

### Repair 7 verification design

| ID | Behaviour | Witness |
|---|---|---|
| V-30 | The unmodified real test project tree is approved; with `<PackageReadmeFile>../../docs/guide.md;@(None)</PackageReadmeFile>`, `...;$([System.String]::Copy(''))` or `...;%(None.Identity)` appended it refuses naming `tests/Antiphon.Tests/Antiphon.Tests.csproj PackageReadmeFile -> docs/guide.md`; a fixture refuses `@(None);../docs/guide.md` and `%(None.Identity);../docs/guide.md` naming `server/App.csproj PackageTags -> docs/guide.md`; `@(None)`, `%(None.Identity)`, `$([System.String]::Copy('../docs/guide.md'))` and `@(None);src/x.json` are approved with `count=1`. | `C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart` |

V-7 remains the acceptance witness for D-38. No new PC rows: the per-part skip is one line of the D-25 rule Review already named; existing land PCs stay pending for SourceLanding Mutation.

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
