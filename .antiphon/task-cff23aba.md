# Review cff23aba: CARD-1483 S1 re-cut (Code a1f403eb) at 5d1623b88f708774194c02e6a5fb38986117825b

Verdict: found. There is one defect, in the second-lander recipe. The EACCES repair, the receipt, the manifest and merge-tree are clean.

## Receipt (own run 20261022-161037-9f39, server2, checkpoint tool)

    CHECKPOINT CP-A commit=5d1623b88f708774194c02e6a5fb38986117825b build=ok filter=/*/*/LandGraphEvaluationTests/(C1483_S1_*) executed=7 passed=7 failed=0 skipped=0 ... sourceState=clean buildSource=verified
    CHECKPOINT CP-B commit=5d1623b88f708774194c02e6a5fb38986117825b build=reused filter=/*/*/LandNonCodeSkipTests/(C1409_*)|(C1400_*)|(C1390_*)|(C1385_RegistryMatchesNarrowGraph*)|(C1380_*) executed=85 passed=85 failed=0 skipped=0 ... sourceState=clean buildSource=verified
    unlisted: none   verdict: GREEN exit=0
    validate-checkpoint-receipt.ps1: CHECKPOINT SOURCE VALID source=5d1623b88f708774194c02e6a5fb38986117825b rows=2

check-evidence-diff (6a2fe6ae3..5d1623b88): commits=11 violations=0. The tree was clean at 5d1623b88.
Unlisted runs, each with a reason and each through build-slot: the checkpoint tool build (bin-cff); two LandGraphAudit
probe builds (original and mutant) in scratch `git archive` copies; and one `dotnet msbuild` of a 3-file scratch
fixture that proves the D-1 import. Direct `audit-land-verification-graph.ps1 -Update/-Check` runs in scratch repos
used no build or test driver. All bin-cff outputs and scratch copies are deleted.

## D-1: HelperTargetFramework is allowed with any value and reopens the global-property import channel

- **Where:** `scripts/audit-land-verification-graph.ps1:197` (`$ItemMetadata['StagedHelper'] = @('Folder', 'Glob', 'HelperTargetFramework')`),
  which feeds `tests/Antiphon.Tests/Antiphon.Tests.csproj` CopyStagedHelpers: `Properties="Configuration=$(Configuration);TargetFramework=$(_StagedHelperFramework)"`.
- **Failure:** the value is expanded into an MSBuild task `Properties` string, which MSBuild splits on `;`. Setting
  `HelperTargetFramework="net10.0;CustomBeforeMicrosoftCommonTargets=$(MSBuildThisFileDirectory)..\..\docs\evil.md"`
  on the LandGraphAudit StagedHelper is approved by `-Update` at this tip (exit 0, digest 4A81F698.../58). The relative
  form is also approved. Real MSBuild replicating the exact target imports the docs file: a scratch fixture printed
  `EVIL-IMPORTED-FROM-DOCS` from `docs/evil.md` during the helper's `GetTargetPath`, and the build stayed green with the
  helper still resolved at `net10.0`. A docs-only edit to that file would then change the land build while the
  non-code skip treats it as docs-only.
- **Why:** this is the same R2 global-property import channel the recipe pins `SetTargetFramework` against (the
  comment says "they reopen the R2 property imports"). `HelperTargetFramework` is new, and it is wider than needed:
  the tree uses only the value `net10.0`. On master it refused (staged-htf spot check: master exit 1, branch exit 0).
  The S1 tool does not catch it either, because it evaluates sln projects with sln globals, not inner MSBuild-task
  global properties.
- **Fix:** move `HelperTargetFramework` out of `$ItemMetadata['StagedHelper']` and into `$PinnedItemMetadata` as
  `'StagedHelper' = @{ 'HelperTargetFramework' = 'net10.0' }`. That form is attribute-only, ordinal, and not allowed
  on Remove, the same as the SetTargetFramework pin. Add a LandNonCodeSkipTests (or LandGraph) case where
  `HelperTargetFramework="net10.0;X=Y"`, another value, and the child-element form all refuse. -Update must still
  give 90CCCDA2/58, because the tree value is unchanged.

## Checks that passed

1. **EACCES repair** (tool probes with the original build and a mutant that drops the GetAttributes fallback):
   - A link inside a mode-000 dir: the original exits 2 with `UnauthorizedAccessException ... App/locked/lk`. **The
     mutant exits 0 `approved`**, which is the exact outcome C1483_S1_UnreadableLinkComponentDenies asserts against
     (exit 2), so that test goes red under the mutation.
   - Original and mutant behave identically on: a missing component (approved, kept as written), a mode-000 file
     outside docs (approved), a mode-000 docs file (refused), a link to a mode-000 docs file (refused), a broken link
     (approved), a broken link into docs (refused), a link loop (exit 2 "more than 40 links"), a dir link into docs
     (refused), a file link outside docs (approved), and `link/..` into docs (refused).
   - New, fail-closed: a plain non-link item inside a mode-000 dir now exits 2 (the mutant approves it). This is not
     a regression.
   - The test runs on uid 1654: it skips only if listing a mode-000 dir succeeds (root). CP-A shows skipped=0, and the
     validator refuses skipped>0.
2. **Recipe:**
   - These all REFUSE naming the metadata: SetTargetFramework with net9.0, with `;X=Y`, with a lower-case value, with a
     leading space, as a child element, on PackageReference, or on Remove; and `Properties=`, alone or beside the pin.
   - Removing each of the four entries refuses naming it.
   - Base `-Update` gives 90CCCDA2/58.
   - Pin-widening mutant (drop the ordinal value compare): net9.0 and `;CustomBefore...docs\evil.md` are approved
     (exit 0), while the original refuses both.
   - The pin also allows the literal on any ProjectReference (exit 0). That is harmless: it carries only the TF.
   - The script diff vs 6a2fe6ae3 is only the four entries plus the pinned table and loop check.
   - Spot check of 19 CARD-1409 shapes on master's tree, master script vs branch script: 15 SAME. The 4 DIFF are
     exactly the new entries (projref-stf10, projref-skip, staged-htf, prop-staged-fw).
3. **Manifest:** branch-script `-Check` gives 90CCCDA2/58 twice (stable). Master's script refuses
   `_StagedHelperFramework`, which is the R1 red. Master made no definition-file changes since 6a2fe6ae3.
4. **CopyStagedHelpers:** for a helper without the metadata, `_StagedHelperFramework` falls back to
   `$(TargetFramework)` inside the per-item batch, so the Properties string is unchanged.
5. **Merge and diffs:** `git merge-tree --write-tree origin/master(e73257069) HEAD` is clean (79fa00c3). The tool,
   LandGraphEvaluationTests.cs, Antiphon.Tests.csproj and both amendment docs are byte-identical to 2904c9bcb. No
   CARD-1409 file is touched beyond the script entries and the manifest.
6. **Residuals** (S2 closure, future globs, /proc/self/cwd, no restore, hard links) are disclosure only.

PCs stay pending for SourceLanding Mutation, plus a HelperTargetFramework pin PC once D-1 is fixed.
