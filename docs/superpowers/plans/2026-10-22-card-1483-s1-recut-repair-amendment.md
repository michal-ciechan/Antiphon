# CARD-1483 S1 re-cut repair: pinned helper metadata (amendment)

Task deb6730d (Code, land owner of the repaired re-cut). Start ref: `5d1623b88` (tip of Code a1f403eb,
[the re-cut addendum](2026-10-22-card-1483-s1-recut-plan.md)). The owners 65d0b3d3, da684330, d205268d and
a1f403eb are not landed. Fixes Review cff23aba D-1.

## Slices

- **R1 (red)** — `C1483_PinnedHelperMetadataRefusesOtherShapes` in LandNonCodeSkipTests: 16 cases on the
  real `tests/Antiphon.Tests` project tree (`tree` approves; every other shape must refuse naming the
  metadata or the property). Red at the tip for the HelperTargetFramework value/child shapes, both
  SkipGetTargetFrameworkProperties shapes and the three `_StagedHelperFramework` value shapes.
- **R2 (fix)** — `scripts/audit-land-verification-graph.ps1`:
  - `HelperTargetFramework` moves from `$ItemMetadata['StagedHelper']` to `$PinnedItemMetadata` as the
    literal `net10.0` (ordinal, attribute form, not on a Remove);
  - `SkipGetTargetFrameworkProperties` moves from `$ItemMetadata['ProjectReference']` to the pinned table as
    the literal `true`;
  - new `$PinnedPropertyValues`: `_StagedHelperFramework` may only be set, as element text (ordinal), to
    `%(StagedHelper.HelperTargetFramework)` or `$(TargetFramework)`, and no task Output may set it.

  `-Update` still gives `90CCCDA244F1CACAADCB2ED5AD9EF049B987697CD4A6DA2F175F667C5B0E1937` / 58 (the tree is
  unchanged).
- **R3** — the task report, committed before the checkpoint run so the tested tip carries it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-A | R3 | `tests/Antiphon.Tests -> bin-c1483-repair/` | c1483-repair | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)` | V-1 V-1a V-1b V-2 V-3 V-3a V-4 | exactly 7 executed, 0 failed/skipped | 7 | 15 |
| CP-B | R3 | `tests/Antiphon.Tests -> bin-c1483-repair/` | c1483-repair | `/*/*/LandNonCodeSkipTests/(C1409_*)\|(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)\|(C1380_*)` | CARD-1409 V-1..V-32, recipe | exactly 85 executed, 0 failed/skipped | 85 | 15 |
| CP-C | R3 | `tests/Antiphon.Tests -> bin-c1483-repair/` | c1483-repair | `/*/*/LandNonCodeSkipTests/(C1483_*)` | D-1 pins | exactly 16 executed, 0 failed/skipped | 16 | 10 |

### Cost

One build, three rows, about 25 minutes on server2. Unlisted: one narrow build-slot run of CP-C's filter at
the R1 red commit; direct `-Update` script runs in scratch worktrees; one build-slot `dotnet msbuild`
probe fixture for the sweep (no test driver).

### Pending for Mutation

Every S1 PC stays pending. New: each pin's value compare (HelperTargetFramework, SkipGetTargetFrameworkProperties,
SetTargetFramework), the `-not $isRemove` guard, the attribute-only form, and the `_StagedHelperFramework`
element-text and task-Output checks. CP-C has a refusing case for each.
