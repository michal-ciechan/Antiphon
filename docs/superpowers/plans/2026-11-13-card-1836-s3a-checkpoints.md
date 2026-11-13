# CARD-1836 S3a verification selection

Task 3ca2caa6. Base cc48c70050a2932eddf4abe18cea8659d478c33e.
Source plan: origin/feat/card-task-0f623d08 at 2c7f3548a, docs/superpowers/plans/2026-11-12-card-1836-incremental-activation-plan.md, S3a and D-4. This dispatch copies exactly CP-2 and CP-90; other units remain with their owners. V-2 comprises ProductAndHelperFanout, DynamicAndUnownedAreDirty, ConfigFixtureScriptAndLockInputs, PartialIndexNeverSubtracts. No R-n applies to this standalone slice. No deliberate mutation runs (operator waiver); all missing controls and future PCs remain Mutation-owned.

Input inventory only: no activation, launch, publication, or change to Full. Map and tree bytes come from each full committed SHA. The initial repository scanner map deliberately includes every tracked input for every suite. Scoped fixture maps prove transitive projects/imports/linked items and unrelated project isolation. Unknowns broaden to all inputs and suites. This inventory does not certify actual runtime, generated outputs, resolved packages, or case manifests; S3b/admission must supply those proofs.

## Performance budget

Provisional CARD-1836: each small method <=30s, 60s test deadline; harness child 45s plus 10s joined cleanup. Tiny local Git repositories only, no database or AppHost. Existing hardened Git runner bounds each operation at 30s. Four fixture repositories are cheap per-case isolation; there is no expensive fixture setup. CP-2 estimate 8m and CP-90 4m include isolated build/discovery; no measured p95 yet. Preserve all red and green costs. Collect comparable samples during ordinary Review/pilots, not redundant repetitions. Timing phases not observed remain unknown.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | S3a | `tests/Antiphon.Tests -> bin-c1836-s3a/` | linux-inputs | `/*/*/RcInputManifestTests/*` | V-2 | all 4 named methods, 0 failed/skipped | 4 | 8 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |
