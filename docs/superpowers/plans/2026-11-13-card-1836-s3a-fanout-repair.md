# CARD-1836 S3a fanout witness repair

Task 91085723; original Code/landing owner 3ca2caa6-dae9-4738-a352-bcb58ce85f62.
Task base and Review-round-2 diff base: 553dd32f60589ad1794729785411de1d213a4096.
Review 16c6dbc2 D-1: shared ownership must dirty every consumer. No production library, ownership map, launch wiring or Full-mode behavior changes.

V-2 remains the four existing RcInputManifestTests methods. ProductAndHelperFanout now contains independent single-edit comparisons; explicit shared-owner dirty/inventory checks; an unreachable-by-project build/wildcard-only.txt owned by *; and the exact committed ownership map loaded via Git objects into the controlled repository. It checks every covered tracked path in every discovered test project in both revisions, including the rule reason. Closure must be known, so unresolved conservative fallback cannot satisfy the ownership assertion. The fixture has three test projects, including an otherwise unrelated project. No new rostered class or method is added.

Red-first witness: commit b05f66e30 adds the assertion that each fanout comparison contains exactly its intended edit. A bounded gated script preflight fails on the original accumulated fixture before the repair. The preflight is an authorized unlisted driver (inherited plan allowance), not a TUnit/checkpoint receipt. No long red build is needed to diagnose this script-only fixture error.

Final closed selection is CP-2 and CP-90, as the repair brief requires. CP-90 shares CP-2's one fresh isolated build because both use identical project/properties/After/repetition. The parent slice's CP-91/CP-92 spawn rosters and CP-93 derived-index check are not carried into this repair unless the full-range impacted selector or a changed roster requires them. The diff changes no limiter, C# roster, production dependency or derived-index input declaration. Record the full-range selector output before qualification. Every selected class/method must have fresh nonzero TRX counts; rerun a red row after repair. No ordinary R-n applies to S3a. Other slices' V/R IDs remain out of scope, not passed.

M-a (first explicit owner only) and M-b (* expands to one project only) remain pending variants for caller-commissioned, method-scoped SourceLanding Mutation. The stage contract reserves deliberate mutants/red-restore-green to Mutation; Code does not credit inferred mutant outcomes. Both target RcInputManifestTests.ProductAndHelperFanout. No PC IDs were defined by this slice; zero-PC discovery remains Mutation-owned. Next stage after ordinary verification is Review, even with these pending variants.

## Performance budget

This is the narrow repair requested after the original 44m59s five-row group (CARD-1864 timing Debug owed before repeating that full group). Two rows share one cold Antiphon.Tests build. Estimates: CP-2 8 minutes, CP-90 4 minutes including cold build/discovery; total execution budget remains tool-derived. Fixture body provisional <=30s, existing 45s child plus 10s joined cleanup and 60s TUnit timeout unchanged. Each local Git operation remains bounded at 30s. No DB/AppHost/browser setup. A fresh tiny repository is required to distinguish input closure across immutable commits. Preserve per-method TRX durations; record build, admission wait, body and unknown phase remainder separately. No redundant repetitions after green. An over-30-minute narrowed group requires timing Debug, not a timeout increase.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | S3a-repair | `tests/Antiphon.Tests -> bin-c1836-s3a-91085723/` | linux-inputs | `/*/*/RcInputManifestTests/*` | V-2, Review D-1 | all 4 methods including all fanout subcases, 0 failed/skipped | 4 | 8 |
| CP-90 | S3a-repair | `CP-2` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |
