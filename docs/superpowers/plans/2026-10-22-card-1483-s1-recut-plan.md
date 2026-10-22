# CARD-1483 S1 re-cut onto master with the CARD-1409 audit (addendum)

Task a1f403eb (Code, land owner of the re-cut). Source: `feat/card-task-d205268d` at
`2904c9bcba8a234c028ccfac44bc6c21c933a320`. Start ref: `6a2fe6ae3` (master with the CARD-1409 chain).
The old owners 65d0b3d3, da684330 and d205268d are not landed. This addendum extends
[the S1 plan](2026-10-22-card-1483-real-msbuild-land-audit-plan.md) and its two amendments
([repair](2026-10-22-card-1483-s1-repair-amendment.md), [repair 2](2026-10-22-card-1483-s1-repair2-amendment.md)).

## Slices

- **R0** — cherry-pick the S1 code commits in order (0a991fb7b feca0ce22 5a5ea5f5b 7a5f32417 aa905becf
  c8969a913 ede58371c 530f730af), dropping the report commits and the manifest regen 09876e2d5.
- **R1 (red)** — regenerate `LandVerificationBuildGraph.manifest.txt` for the S1 tree (90CCCDA2.../58)
  without touching the audit script. Master's CARD-1409 `-Check` refuses
  `tests/Antiphon.Tests/Antiphon.Tests.csproj _StagedHelperFramework`, which is also the `-Check` step
  of `C1385_RegistryMatchesNarrowGraph`.
- **R2 (fix)** — the second-lander recipe (Reviews cb067299, 8e9f68b4): four allow-list entries in
  `scripts/audit-land-verification-graph.ps1`:
  - property `_StagedHelperFramework`;
  - ProjectReference metadata `SkipGetTargetFrameworkProperties`;
  - StagedHelper metadata `HelperTargetFramework`;
  - ProjectReference metadata `SetTargetFramework`, pinned to the single literal value
    `TargetFramework=net10.0` (ordinal, attribute form only). Any other value, the child-element form
    and every other global-property name still refuse.

  `-Update` then `-Check` give `90CCCDA244F1CACAADCB2ED5AD9EF049B987697CD4A6DA2F175F667C5B0E1937` / 58.
- **R3** — the task report, committed before the checkpoint run so the tested tip carries it.

No LandNonCodeSkipTests row is added or changed: the 1409 suite stays green with the four entries.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-A | R3 | `tests/Antiphon.Tests -> bin-c1483-recut/` | c1483-recut | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)` | V-1 V-1a V-1b V-2 V-3 V-3a V-4 | exactly 7 executed, 0 failed/skipped | 7 | 15 |
| CP-B | R3 | `tests/Antiphon.Tests -> bin-c1483-recut/` | c1483-recut | `/*/*/LandNonCodeSkipTests/(C1409_*)\|(C1400_*)\|(C1390_*)\|(C1385_RegistryMatchesNarrowGraph*)\|(C1380_*)` | CARD-1409 V-1..V-32, R2 recipe | exactly 85 executed, 0 failed/skipped | 85 | 15 |

### Cost

One build, two rows, about 20 minutes on server2. Unlisted: direct `-Check`/`-Update` script runs at
R1 and R2 and in a scratch probe worktree (no build, no test driver).

### Pending for Mutation

Every S1 PC stays pending. New for this re-cut: the SetTargetFramework pin (value compare and the
attribute-only form) and each of the four entries; no test in either class exercises a non-pinned
SetTargetFramework value, so a mutant that allows the name for any value survives both rows.
