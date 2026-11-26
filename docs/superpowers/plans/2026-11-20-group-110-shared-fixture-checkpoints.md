# CARD-2006 group-110 shared fixture builds

Mutation-shape and staged-baseline fixtures were SDK projects, so every checkpoint entry point paid for `Microsoft.NET.Sdk`. A second entry point cannot reuse the first output directory: `SourceRunGuard` refuses `pre-existing-output`. The factories now emit a classic project whose `Build` target is the mutation. `clean-build` and the baseline cases that assert a compiled assembly stay on the SDK. Confinement stays on the SDK: each import graph is used once, and the assertions require the tracked `Lib.dll` and `project.assets.json` bytes. Product node reuse and `UseArtifactsOutput` are unchanged. Windows timings are unobserved. `tests/test-execution-policy.json` has no numeric group budget, so none was lowered.

Linux phase timings, build-slot, `ANTIPHON_LINUX_COMPILER_POLICY=0`, no `LINUX COMPILER` line. Unlisted because the plan did not exist yet.

| Class | Commit | Result | Wall |
|---|---|---|---|
| CheckpointShapeConflictTests | f64e04774 | 18 passed, 0 failed, 0 skipped | 3m 25s |
| CheckpointTrackedBaselineTests | f64e04774 | 14 passed, 0 failed, 0 skipped | 2m 43s |
| CheckpointShapeConflictTests | 4285cbfd3 | 18 passed, 0 failed, 0 skipped | 1m 53s |
| CheckpointTrackedBaselineTests | 4285cbfd3 | 14 passed, 0 failed, 0 skipped | 2m 06s |

Slot waits were 0s. Leases: shape before `104d8406`, baseline before `265977d1`, after build `ee6e1f17` (held 571s, 0 errors), shape after `978d1919`, baseline after `46d1230f`.

## Verification design

- V-1. `shape_fixture_sdk_project_text_count` counts 0 SDK-importing project texts for the sampled mutation shapes, and `clean-build` still imports the SDK.
- V-2. `CheckpointShapeConflictTests` stays green.
- V-3. `CheckpointTrackedBaselineTests` stays green, including `baseline_staged_fixture_sdk_project_text_count`.
- V-4. Nine-class guard union, excluding `WindowsUnitPlatformAdmissionTests` on Linux.
- V-5. Derived-impact driver build, then `DerivedImpactIndexTests`.
- V-6. `scripts/check-evidence-diff.ps1` over `f997d3f06f7e86c3e66d1eac23425cce3965f421..HEAD`.
- V-7. Recompute nightly `policyHash` and `PinnedInputDigest`. Re-pin only if a pinned input changed.

No PC rows. Mutation is waived. Positive controls stay pending for SourceLanding.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c2006/` | shape-witness | `/*/*/CheckpointShapeConflictTests/shape_fixture_sdk_project_text_count` | V-1 | SDK-importing project text count 0 | 1 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | S1 | CP-1 | shape-class | `/*/*/CheckpointShapeConflictTests/*` | V-2 | all listed, 0 failed | 18 | 6 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | S1 | CP-1 | baseline-class | `/*/*/CheckpointTrackedBaselineTests/*` | V-3 | all listed, 0 failed | 14 | 6 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | S1 | CP-1 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-4 | 0 failed, executed at least 81 | 81 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | S1 | n/a | driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false` | V-5 | build exit 0 | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | S1 | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-5 | 0 failed, executed at least 27 | 27 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
