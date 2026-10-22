# Review 1aedf0f7: CARD-1483 S1 re-cut repair 3 (Code deb6730d), Final

Subject `feat/card-task-deb6730d` at `0fab803fde898c026529d69988a92ae9aae51e0e` (clean, equals the pushed tip).
Script diff 5d1623b88..tip: the HelperTargetFramework / SkipGetTargetFrameworkProperties pins, the
`$PinnedPropertyValues` table, its element-text check and its task-Output refusal. Nothing else.
Verdict policy (operator, third round): `found` only for a regression or a branch-csproj channel master refuses.

## Receipt (own run at the reviewed SHA)

Run `20261022-180648-8dfd` (checkpoint tool, plan amendment table, server2). It used one build, `slotWait=195s`,
and `source=0fab803fd... state=clean buildSource=verified`. Verdict GREEN exit=0. Unlisted: none.
- `CHECKPOINT CP-A ... filter=/*/*/LandGraphEvaluationTests/(C1483_S1_*) executed=7 passed=7 failed=0 skipped=0`
- `CHECKPOINT CP-C ... filter=/*/*/LandNonCodeSkipTests/(C1483_*) executed=16 passed=16 failed=0 skipped=0`
- `CHECKPOINT CP-B ... (C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=85 passed=85 failed=0 skipped=0`
- Both `validate --rows CP-A,CP-B,CP-C` and `scripts/validate-checkpoint-receipt.ps1` print
  `CHECKPOINT SOURCE VALID source=0fab803fde898c026529d69988a92ae9aae51e0e rows=3` (exit 0).
- Other runs not in the table: the build-slot build of `tools/Antiphon.Checkpoints` (`bin-rv1aedf/`, since
  deleted), and direct `pwsh audit-land-verification-graph.ps1 -Update` probes in scratch clones. The probes
  used no build or test driver.

## (1) Own sweep: hostile shapes x scripts

Method: a `git clone --shared` of the repo per shape, edit, `docs/evil.md` tracked, commit, then `-Update` with
the tip script, the 5d1623b88 (= 197784c9f) script and the origin/master (c5f35c862) script. EVIL =
`net10.0;CustomBeforeMicrosoftCommonTargets=$(MSBuildThisFileDirectory)..\..\docs\evil.md`.
Master's script refuses the tip tree as a whole (`_StagedHelperFramework` is not allowed there), so every
tip-tree shape is 1 under master. No shape is approved at the tip that master would approve differently.

| Shape (on the tip tree) | tip | old tip | refusal at tip |
|---|---|---|---|
| tree | 0 90CCCDA2/58 | 0 90CCCDA2/58 | control |
| HTF `net10.0;X=Y`, EVIL, `&#59;` EVIL, `%3B` EVIL, ` net10.0`, `net10.0 `, `NET10.0`, empty, `$(TargetFramework)`, `net10%2E0` | 1 | 0 (all 10) | metadata, HelperTargetFramework |
| HTF child element (literal / EVIL) | 1 | 0 | metadata |
| HTF on Remove | 1 | 1 | metadata |
| HTF on `Update` = EVIL; lower-case attribute name = EVIL; lower-case item type `stagedhelper Update` = EVIL | 1 | 0 | metadata |
| HTF on `Update`, literal; lower-case name, literal; item Condition `Exists(docs\evil.md)`, literal | 0 | 0 | harmless (literal only) |
| HTF via ItemDefinitionGroup (child / attribute) | 1 | 1 | element not classified |
| HTF in-target `StagedHelper Update=@(StagedHelper)` EVIL | 1 | 1 | metadata (tip) |
| HTF on ProjectReference; `StagedHelper Include=@(ProjectReference)` | 1 | 1 | metadata / include not classified |
| Skip `false`, `true;X=Y`, `True`, `true&#59;X=Y` | 1 | 0 (all 4) | metadata |
| STF `TargetFramework=`EVIL, `&#59;`, `%3B`, trailing space, on Remove, via ItemDefinitionGroup | 1 | 1 | metadata / element |
| `_StagedHelperFramework` top-level EVIL; in Directory.Build.props EVIL | 1 | 0 | pinned property |
| in-target EVIL (both lines); trailing space; `$(TargetFramework);X=Y`; `&#59;`; CDATA EVIL; lower-case element name; `$(targetframework)` | 1 | 0 (all) | pinned property |
| CDATA of the literal `$(TargetFramework)` | 1 | 0 | false refusal, harmless |
| CreateProperty Output `_StagedHelperFramework`; Output `" _stagedhelperframework "` | 1 | 0 | pinned property |
| top-level literal `$(TargetFramework)`; in-target Condition `Exists(docs)` on the literal | 0 | 0 | literal only |
| tool csproj `CustomBeforeMicrosoftCommonTargets` property | 1 | 1 | property not allowed |

The repair closes every value shape. The branch's csproj adds no surviving channel.

## (2) Manifest, ordinary edits, CARD-1409 parity, script hygiene

- `-Update` on the tip tree: `digest=90CCCDA244F1CACAADCB2ED5AD9EF049B987697CD4A6DA2F175F667C5B0E1937 count=58`.
- Ordinary edits still approve at the tip: new PackageReference, Compile `Link`, ProjectReference
  `ReferenceOutputAssembly="false"`, and the tool csproj PackageReference `GeneratePathProperty`.
- 21 CARD-1409-class shapes on the master tree, master script vs tip script. 20 give an identical exit and
  message: docs None/Compile/Content glob/AdditionalFiles glob/EmbeddedResource README, Import docs,
  ProjectReference AdditionalProperties/Properties, `doc%73` escape, `DOCS` case, unknown property,
  CustomBefore property, docs json, None Update, Exclude, transform, `$(Pkg*)`, `..\..\**\*.md`, the two
  ordinary approvals, and top-level `Configuration` with `;`. The master tree approves under both scripts
  as `F240D0F1/57`.
- The one difference is intended (S1, already reviewed): `SetTargetFramework="TargetFramework=net10.0"` on
  any ProjectReference approves at the tip and refuses on master. That is a literal only, so nothing can
  be imported.
- The script is ASCII-only, its pwsh parser reports 0 errors, and it adds no PS7-only syntax (string
  `Equals` with Ordinal, hashtable literals).
- `check-evidence-diff`: `commits=15 entries=2 violations=0`.
- `git merge-tree --write-tree origin/master(c5f35c862) HEAD`: clean.

## (3) CP-C red and mutations

- The script at 197784c9f is byte-identical to 5d1623b88. The old-tip column above reproduces the stated
  red: 11 approved shapes (the HTF value and child shapes, skip-false, skip-semicolon, and the three
  property shapes). htf-remove and stf-* already refused at the old tip, and the tree control approves.
- Mutations of the tip script, run against the CP-C shapes (tree is 0 under every mutation):

  | Mutation | Shape newly approved (exit 0) | CP-C case that goes red |
  |---|---|---|
  | M1: HTF back in `$ItemMetadata`, pin entry dropped | htf-semicolon | htf-* value cases |
  | M2: drop the `_StagedHelperFramework` element-text check | prop-toplevel, prop-intarget | prop-toplevel, prop-intarget |
  | M3: drop the task-Output refusal | prop-output | prop-output |
  | M4: drop `-not $isRemove` | htf-remove | htf-remove |

  Each case asserts `ran.Code.ShouldBe(1)` and names the refusal needle, so an approval turns it red. These
  mutations were run on scripts in the scratch area, not as test runs. Method-scoped PCs stay pending for
  Mutation.

## (4) Earlier checks

CP-A covers the tool tests 7/7: ApiSurface, EvaluatedSkippableItem, HarmlessFixture, EvaluationFailure,
UnevaluatedSolutionEntry, LinkedSkippableItem, and UnreadableLinkComponent (EACCES, `File.GetAttributes`).
CP-B covers the 85 CARD-1409 suite tests. The tool code is unchanged since 5d1623b88.

## (5) Pre-existing gaps (master approves them; disclosure, not blockers)

Probes against master's own tree and script (c5f35c862):
- CopyStagedHelpers MSBuild-task `Properties` + `;CustomBeforeMicrosoftCommonTargets=...docs\evil.md`:
  **approved** `40F93292/57`, identical under the tip script.
- in-target `<TargetFramework>`EVIL (feeds `TargetFramework=` in the same string): **approved** `86C4BCDE/57`.
- item `Condition="$([System.IO.File]::ReadAllText('...docs\evil.md').Contains('x'))"`: **approved**
  `CDA4039D/57`. The same shape on the tip's in-target `_StagedHelperFramework` Condition approves at the
  tip and at the old tip.
- `Condition="Exists('...docs\evil.md')"`: approved `3BD8A392/57`.

S2..S5 coverage, from the real-msbuild plan:
- D-5 (S3) hashes every `<Target>` OuterXml against a human-pinned row. Any edit to a Target body therefore
  refuses until Review re-pins it. That covers the Properties string, in-target properties and in-target
  conditions.
- The task-parameter content of a pinned Target is not machine-checked. It is trusted at pin review
  (residual R-3).
- D-6/D-7 see only evaluation, not the inner MSBuild-task invocation at execution. A top-level property that
  feeds a pinned Target's Properties string would not show up in evaluation or the canary diff. Today
  `Configuration`/`TargetFramework` at top level are either not allowed or are SDK-owned, so this is
  hypothetical.
- D-6(b) covers evaluation-time `ReadAllText` conditions.

Proposed Backlog card text:
"CARD-1483 follow-up: the land-graph audit approves target-body channels into an inner MSBuild invocation.
Master's script approves (1) a `;Name=Value` appended to a Target's MSBuild-task `Properties`, (2) in-target
properties feeding it (`_StagedHelperDest`, `TargetFramework`), and (3) Conditions that read docs files
(`[System.IO.File]::ReadAllText`, `Exists`). Real MSBuild imports `CustomBeforeMicrosoftCommonTargets` from
docs for (1)/(2). S3's Target pin set (D-5) refuses any such edit until re-pinned, but R-3 trusts task
parameters at pin review. Add a pin-review checklist item and a static check that a pinned Target's
`Properties`/`AdditionalProperties` strings contain only `Name=$(Prop)` pairs whose properties are pinned or
SDK-owned, and refuse file-reading property functions in any Condition."
