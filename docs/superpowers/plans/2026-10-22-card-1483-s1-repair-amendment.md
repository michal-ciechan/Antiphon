# CARD-1483 S1 repair: amendment to the real-MSBuild land audit plan

Date: 2026-10-22. Code task da684330, repairing S1 (Code 65d0b3d3) after Final Review cb067299.
Amends `docs/superpowers/plans/2026-10-22-card-1483-real-msbuild-land-audit-plan.md`, which master
carries through its own commit; the amendment lives in this separate file so the branch does not edit
that plan (add/add). Everything not restated here is unchanged.

## Review findings repaired

- **D-1 (unevaluated solution entries).** `dotnet build Antiphon.sln` also builds a solution entry of a
  type the audit did not evaluate (an unknown type GUID, a `.dcproj`, an `.esproj`) when the file loads
  as a project. The tool now skips only solution folders. Any entry type other than
  `KnownToBeMSBuildFormat` is `evaluation-failed` (exit 2), never approved. The real `Antiphon.sln` has
  only C# entries and solution folders.
- **D-2 (links).** MSBuild reports a lexical `FullPath`, but the build opens an item through any symbolic
  link or junction in its path. Each item is checked three ways: its lexical `FullPath`, the real path
  of that `FullPath`, and the real path of the include as written. The real path walks every component
  from the root and replaces each existing link with its target before the next component, so
  `link/..` leaves the target. A match against `<real repo>/docs/**/*.md` or `<real repo>/README.md`
  refuses, and the refusal shows the path under the repository. More than 40 links on one path is
  `evaluation-failed`. On Windows the OS folds `..` lexically, so there the real-path candidate of a
  `link/..` include over-approximates: it can add a refusal but never removes one.

## Verification additions

| # | Case | Test |
|---|---|---|
| V-1a | An item reaching docs through a tracked directory link (`App/link -> ../docs`; `link/guide.md`, `link/../docs/sub/deep.md`) or a file link (`App/top.md -> ../README.md`) refuses with the docs/README path. So do the three SDK default `None` items that the `**/*` glob finds through those links, which makes exactly 6 refusals. The test creates the links itself, using a junction on a Windows host without symlink privilege. | `C1483_S1_LinkedSkippableItemRefuses` |
| V-3a | A solution entry with an unknown project-type GUID, pointing at a project that embeds `../docs/guide.md`, gives exit 2 `evaluation-failed` naming the entry and never `approved`. | `C1483_S1_UnevaluatedSolutionEntryDenies` |

Red witness (unlisted, stated): commit 7a5f32417b8bafd42820f511fd4afef77f1b26aa, one build-slot `dotnet run` of
`tests/Antiphon.Tests` to `bin-c1483-red/` with `--treenode-filter /*/*/LandGraphEvaluationTests/(C1483_S1_*)`:
total 6, passed 4, failed 2. Both new tests failed with exit 0 and `approved`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1483-s1/` | c1483-s1 | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)` | V-1 V-1a V-2 V-3 V-3a V-4 | exactly 6 executed, 0 failed/skipped | 6 | 15 |

This replaces only the plan's CP-1 row (4 becomes 6). CP-2 to CP-5 in the plan still count S1 as 4. The
S2 to S5 Code tasks add 2 to each of those rows' expected and minimum counts (CP-2 9, CP-3 12, CP-4 15,
CP-5 17 plus the pre-check count). They state the change in their own amendment.

## Residuals (disclosure, unchanged)

S2 closure over a `ProjectReference` outside the sln, and globs that would match future files, remain
S2's. Hard links are not followed: git does not record them, so a fresh checkout has no shared inode.

Fix dev check (unlisted, stated): an incremental rerun of the same `bin-c1483-red/` build with the fix gave 5 of 6.
`C1483_S1_LinkedSkippableItemRefuses` counted 6 refusals where the test expected 3. The SDK default `None`
glob also reaches `link/guide.md`, `link/sub/deep.md` and `top.md`, and the tool correctly refuses them
(`Microsoft.NET.Sdk.DefaultItems.props None ...`). The test now asserts those three and exactly 6. The staged
tool run on the real tree at the fix gave `approved` in 5 s under lease c34ca7ca and changed no tracked or
ignored path.
