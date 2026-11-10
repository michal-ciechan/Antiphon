# Merge batch 160

Master fast-forwarded from `f5541c4a140f12429895eb390fc0b0cdf1221d0d` to `b067f912ae01234d744c5862cc90285b5859dc1a`. First push accepted. No subject excluded. No migration. No land-graph refresh. Pins unchanged. rc refs were not pushed.

## Commits

| Master | Source | Patch-id | Subject |
|---|---|---|---|
| `d4d264bfeb8fe23fb42a95ac4f06634a970ce5a6` | `8889be4c0ee6add76922c3bf582aebc06c9dc3fb` | `ed174ee32badad145906757fd65bb3ce73c05127` | CARD-1459 red witnesses |
| `edc100dda4af776c8c58b06672bb3299725d13ff` | `df14acfb44b4230563a4d339610f7e0bb6524b16` | `c3334ce818f0b8210689b0548fbca9e915e8cd93` | CARD-1459 null exit, failed refuse, exhausted debt |
| `36b1ae6ae6f8ec7c9ed81f7bcc8c3bac8793db10` | `022611e0e6e6d70f768c9bac2d9f759de12dfb99` | `d597c75fa5fbcc5bcf709207dde4b30ed3b755a6` | CARD-1459 trimmed desktop HEAD |
| `cfa66f8f42f1ab3e13946fbf546b6c87c2320766` | `572550426e1f61607f1d562d40d2f633a93a65f7` | `90a82381d2b7b8e38b91d290aef44c626cb4f11a` | CARD-1501 red working-scan witness |
| `b067f912ae01234d744c5862cc90285b5859dc1a` | `ff87ab653a85a54c8654986abefbd542835e1bf4` | `833fbe9770ef617673e54c3eb8d8ad8341262a32` | CARD-1501 legacy note after the working scan |

`git cherry origin/master` on the source tips listed only these five `+` commits. None were report-only or under `.antiphon/`. Stable patch-ids match. `docs/orchestration-loop.md` auto-merged on the second CARD-1459 pick with no conflict markers in the picked range.

Evidence `f5541c4a140f12429895eb390fc0b0cdf1221d0d..b067f912ae01234d744c5862cc90285b5859dc1a`: commits=5 entries=0 violations=0. The mirror was not shallow.

## Pins

Recomputed immediately before the push. Stored and computed are the same, so there is no re-pin commit.

- policyHash `fdc043758617a5e8c21735126539c559faf9dad95abe5f1122a6d5780ee9df4d`
- PinnedInputDigest `4bc09f3840a39436be6ca4035ea0946c01f96ae1883d0a7b4e5513132dbf74ec`

## Checks

- `dotnet build Antiphon.sln -m:1 --property:UseAppHost=false` through `scripts/build-slot.ps1`: 0 errors, 2207 warnings, 15:07. Lease `15aaac24-3ed6-44db-9f6b-bdaff795c88e`.
- Policy guards, own process, `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)/*`: 52 passed, 0 failed, 0 skipped.
- One batched run of the CARD-1459 classes, `CheckCompactionWorkerLifetimeTests*`, `CheckNoteDeliveryHandoffTests*`, and the ten-class guard union: 224 passed, 0 failed, 0 skipped. Class counts: AbandonedLandingReconcilerTests 11, SettlementSyncRecoveryTests 30, WorktreeResidueSweepTests 16, RunnerBranchContractDocumentationTests 10 (67, matching Review CP-1), CheckNoteDeliveryHandoffTests 19, CheckCompactionWorkerLifetimeTests 3, and the guard union 135 (policy 41, classification guard 1, slow registry 5, tripwire 2, NativeLinuxPolicyWitness 3, mechanical 11, TaskPlatformGuidance 5, C1729 9, lane guard 1, ProcessSpawnLimit 57).
- A5 pair `C1686_LinuxOnlyCensus` and `LinuxOnlyWindowsExclusionPolicy`: 2 passed, 0 failed, 0 skipped.
- `C1729LayeringGuardTests.Repository_graph_satisfies_the_committed_allowlist`: passed. Printed `VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3`.
- `ContractSnapshotTests`: 5 passed, 0 failed, 0 skipped.
- `import --plan` of the five documented Merge rows (messaging omitted, no migration): `imported 5 rows`, exit 0, zero `WARNING` lines.
- `scripts/audit-land-verification-graph.ps1 -Check`: exit 0, `approved`, digest `3ED4A38CAA52947D313A1BAD14BA15203DD7F261A219BBC20704CB13E329EB46`, count 75. No `-Update`. Pins file sha256 `3011128f6beb7bda663f95e20305988a4370269e6458741ea873390a55138d87`.
- `scripts/impacted-classes.ps1 -MergeSubset`: `migrations=False`, kind `test-lane-category` only, no `windowsFilter`.

`origin/master` was still `f5541c4a140f12429895eb390fc0b0cdf1221d0d` at the push. Loop count 1.

## Left out

Hang capture (Debug 741fb04b), CARD-1546 S3 (`3d1b7734`), CARD-1820, and the desktop trace (`e1fa7e11`) were not in this batch. CARD-1459 positive controls stay pending. The fixed land-notification class rows were imported and not executed. The Unit lane was not run. `rc/2026-11-11` stayed `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`. `rc/2026-11-13` stayed `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`.
