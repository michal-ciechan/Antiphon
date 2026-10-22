# Code deb6730d: CARD-1483 S1 re-cut repair (Review cff23aba D-1)

Branch `feat/card-task-deb6730d`, runner mirror `/work/worktrees/task-deb6730d` (server2). Start ref
`5d1623b88f708774194c02e6a5fb38986117825b` (tip of Code a1f403eb). This task owns the land. The owners
65d0b3d3, da684330, d205268d and a1f403eb must not be landed. Plan amendment:
`docs/superpowers/plans/2026-10-22-card-1483-s1-recut-repair-amendment.md`. This report is committed before
the one checkpoint run, so the pushed tip is the tested source. The CHECKPOINT lines and the validator output
are in the final message.

## Commits (base 5d1623b88..)

| Slice | SHA | What |
|---|---|---|
| R1 red | 6253559c47711023cecca11d94710792ab907946 | `C1483_PinnedHelperMetadataRefusesOtherShapes`, 13 cases |
| R1 red | 197784c9f50d61a2fbb7e92e53e44bb36ed54579 | + 3 `_StagedHelperFramework` value cases (16 total) |
| R2 fix | cbfdd9f415528347695891c30808f3e8686cbdb2 | audit-script pins |
| R3 | (this commit) | plan amendment + report |

## Fix (R2, `scripts/audit-land-verification-graph.ps1`)

- `HelperTargetFramework` is removed from `$ItemMetadata['StagedHelper']`. It is now in `$PinnedItemMetadata` as
  `'StagedHelper' = @{ 'HelperTargetFramework' = 'net10.0' }`. The value is compared ordinally, the attribute form
  is the only form, and it is never allowed on a Remove. This uses the existing SetTargetFramework pin code path.
- `SkipGetTargetFrameworkProperties` is removed from `$ItemMetadata['ProjectReference']`. It is now pinned to `'true'`.
  It is not exploitable (see the sweep), but it is pinned so that only the tree value is allowed.
- New `$PinnedPropertyValues = @{ '_StagedHelperFramework' = @('%(StagedHelper.HelperTargetFramework)', '$(TargetFramework)') }`:
  - `Assert-AllowedProperties` refuses any PropertyGroup element of that name whose InnerXml is not one of
    those two texts. The refusal message is `sets a pinned property to a value the audit does not allow`.
  - It also refuses any task `Output PropertyName` of that name.
- `-Update`/`-Check` at the fix are unchanged: `digest=90CCCDA244F1CACAADCB2ED5AD9EF049B987697CD4A6DA2F175F667C5B0E1937 count=58`.
  The manifest is not modified.

## Red witness (unlisted narrow run, stated)

`pwsh scripts/build-slot.ps1 -Label c1483-repair-red -- dotnet run --project tests/Antiphon.Tests
--property:OutputPath=bin-c1483red/ -- --treenode-filter "/*/*/LandNonCodeSkipTests/(C1483_*)"` at 197784c9f:
total 16, failed 11, passed 5, skipped 0. The build slot was held 461 s.

- **Failed (11):** htf-semicolon, htf-docs-import, htf-net9, htf-leading-space, htf-case, htf-child,
  skip-false, skip-semicolon, prop-toplevel, prop-intarget, prop-output. Each failed with the guarded assertion
  `ran.Code should be 1 but was 0` (the shape was approved).
- **Passed by design (5):**
  - `tree` is the control.
  - `htf-remove` and `stf-net9`/`stf-semicolon`/`stf-child` guard behaviour that already refused. They are
    witnesses for the `-not $isRemove` guard and the SetTargetFramework value compare, which Mutation will
    attack. The a1f403eb report flagged that these had no test.

The bin-c1483red directories (29) were deleted.

## Sweep: entries that accept a value that can reach an MSBuild Properties string or an Import

Columns:
- **Audit tip/fix:** `-Update` on the real tree with the edit, using the 5d1623b88 script and then the fix
  script, in a scratch detached worktree with `docs/evil.md` tracked.
- **Real MSBuild:** a build-slot `dotnet msbuild` probe fixture that copies the CopyStagedHelpers framework
  selection and the ProjectReference; `docs/evil.md` is an `InitialTargets` project.

EVIL = `net10.0;CustomBeforeMicrosoftCommonTargets=$(MSBuildThisFileDirectory)..\..\docs\evil.md`.

| Entry / shape | Audit tip | Audit fix | Real MSBuild | Action |
|---|---|---|---|---|
| tree (unchanged) | 0, 90CCCDA2/58 | 0, 90CCCDA2/58 | not imported, FW=net10.0 | control |
| StagedHelper `HelperTargetFramework`=EVIL | **0** (4A81F698) | 1, names HelperTargetFramework | **IMPORTED** | pinned `net10.0` (D-1) |
| HTF `net10.0&#59;X=Y` (XML entity) | 0 | 1 | (same as `;`) | pinned |
| HTF `net10.0%3BX=Y` (MSBuild escape) | 0 | 1 | n/a | pinned |
| HTF lower-case attribute name, value `net10.0` | 0 | 0 | same as tree (metadata names are case-insensitive) | harmless, literal only |
| HTF child element / Remove | test: refuses | test: refuses | n/a | attribute-only, Include/Update only |
| `_StagedHelperFramework` top-level PropertyGroup = EVIL | **0** | 1 | not imported (the target overwrites it) | pinned anyway |
| `_StagedHelperFramework` in-target element = EVIL | **0** | 1 | **IMPORTED** | pinned to the 2 tree texts |
| `_StagedHelperFramework` via `CreateProperty` Output | **0** | 1 | (same channel) | Output refused |
| `_StagedHelperFramework` leading space | 0 | 1 | n/a | exact text |
| ProjectReference `SkipGetTargetFrameworkProperties` = `true;CustomBefore...docs\evil.md` | **0** | 1 | not imported (Condition-only use in Microsoft.Common.CurrentVersion.targets) | pinned `true` |
| `SkipGetTargetFrameworkProperties` = `false` | 0 | 1 | not imported (normal TF negotiation) | pinned |
| ProjectReference `SetTargetFramework` = `TargetFramework=`+EVIL / net9.0 / child | 1 | 1 | n/a | already pinned (now has tests) |
| tools/Antiphon.LandGraphAudit.csproj (new definition file) | only master-allowed properties and PackageReference `ExcludeAssets` | same | n/a | no new entry |

The fix leaves no recipe entry that approves a value capable of selecting a docs/README file.

**Pre-existing class (master, not introduced here; disclosure, card suggested).** The script header says
"Target task parameters are not evaluated". Two edits approve on master's own script and tree
(445df8a80, digests 40F93292 and 9AA7F05F), and on this branch at the tip and the fix:
- writing `;CustomBeforeMicrosoftCommonTargets=...docs\evil.md` straight into the CopyStagedHelpers MSBuild task
  `Properties` attribute;
- setting the master-allowed in-target property `_StagedHelperDest` (which feeds `OutDir=` in the same
  string) to a `;`-bearing value.

So target bodies can already reach this channel on master. This repair does not widen that. A follow-up card
could pin the task `Properties` attributes and the in-target properties they read.

## Pending for Mutation

Every S1 PC stays pending. These are new or now covered by CP-C:
- each pin's ordinal value compare (HelperTargetFramework, SkipGetTargetFrameworkProperties, SetTargetFramework);
- the pinned check's `-not $isRemove`;
- the attribute-only form (the child loop does not consult the pin);
- the `_StagedHelperFramework` element-text check and the task-Output check;
- removing each pin table entry.

## Unlisted runs

- The R1 red run above (narrow, through build-slot).
- The checkpoint tool build `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1483tool/`
  (build-slot, waited 105 s), used for `import --plan` (exit 0, 3 rows), `start`, `wait` and `validate`.
- One build-slot `dotnet msbuild` probe fixture in the scratchpad (waited 616 s, held 32 s).
- Direct `pwsh scripts/audit-land-verification-graph.ps1 -Update/-Check` runs in two scratch detached worktrees
  (tip and origin/master), with no build or test driver. The worktrees were removed.
