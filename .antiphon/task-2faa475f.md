# Merge batch 152 (CARD-1819), task 2faa475f

master `da4f168de5a771adf5dab22336826d19b4ec613b` -> `8bc2c1842141cd3c69c0235f89f33215570db024` (plain fast-forward, first try). No rc ref touched.

## Commits (cherry-pick -x, linear, all patch-ids match)

| Source | Landed | Subject |
|---|---|---|
| 75d0f154c6193382d9605eba99c06f49ae6fff8a | 75750fdd5 | test: witness the why-missed selector rows |
| bf084a0d51aec7a0b5c5c8a71bae79f7928d84ca | e65ce2f53 | fix: select the why-missed classes |
| 669d0cf862a152b8fe6b31b57a5db69550f41fd8 | c38fd24c7 | test: drop the selected name in the witness mutant |
| 89317fcc95c7d3028e44446d63f0b54bf07c0d99 | 9fedc71ed | test: witness Windows-only classes stay off Linux filters |
| 8d46c481775d7eabe82999ae6ea9567fd6c5c2c9 | b237addc3 | test: ProtectedData witness class census |
| 7a83645762962f0ee92a2525ed188a50e72bc862 | 3025c89e7 | fix: keep Windows-only classes out of Linux filters |
| c1272ef7a7722b55dc5da473efb9482c923448ec | e93bd0822 | test: witness Linux kinds that name a Windows skip |
| d196ac954ba7abe9c2a5147f5ab1215826c32872 | 8bc2c1842 | fix: Windows lease methods off the Linux selector row |

Skipped (report-only, .antiphon/*): 025fc42bf, b6b6e9075, 934c4056d. `89317fcc9` was not named in the brief's chain list but is a `+` test commit in `git cherry`; picked. `git diff d196ac954 HEAD` (excl. .antiphon) equals master's own drift since merge base 9e050cf96 (59 files, identical stat), so the landed CARD-1819 content equals the tested code.

## Selector table (impacted-classes.ps1 -MergeSubset -Json, range `<sha>^..<sha>`)

classes/Linux filters/windows filters, all equal to the Code report: none 15/5/0, ff7cceaf 67/7/0, dcb973d8 18/5/0, a54a6d4f 17/6/0, ae5b545d9 73/8/2, 0f76e7b0a9 21/5/1. land-1 and land-2 equal the documented strings on every range. 0f76e7b0a9 windows row `/*/*/(RepositoryLeaseWindowsBoundaryTests*)|(ServerImagePushCredentialTests*)|(ServerRepositoryMirrorScriptTests*)/*`.
Batch range da4f168de..HEAD: 18/6/1, kinds dpapi-protected-data, spawn-limit-rosters, temp-family-registry, test-lane-category; extra rows ProcessSpawnLimitTests + TestLaneCategoryGuardTests (tests row), CP-merge-subset-sessionrunner `C1677TempFamilyRegistryTests`; windows `CapabilityScriptTests|DelegateScriptCapabilityTests`.
`import --plan` of the Merge rows built from each selector output (doc table format, EstimatedMinutes 5, doc floors): none 5 rows, ff7cceaf 7, dcb973d8 5, a54a6d4f 6, ae5b545d9 8, 0f76e7b0a9 5, batch 6; all exit 0, 0 warnings. Generated plans/manifests deleted, not committed.

## Final-tree checks (tree = 8bc2c1842)

- Build `dotnet build Antiphon.sln -c Debug -p:UseAppHost=false` (parallel, no NETSDK1047): lease b6cae2ed waited=0s held=231s, 0 errors, 2199 warnings.
- Run A (one batched run): ImpactedClassesSelector 40, MergeHighRadiusSubset 6, RepositoryMutationLease 32, MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 9, TestLaneCategoryGuard 1, ProcessSpawnLimit 55, AppDbContextModel 3, CheckpointReceiptSafety 8, HostCleanupCheckpointReceipt 8, ReceiptScanDestinationIntegration 23, TestTimingOperationLabel 2: total 203, failed 0, skipped 0, 8m55s (lease bdba5fe3 held=540s). C1729 `VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3`. RepositoryMutationLeaseTests 32/32, zero skips.
- Policy guard batch alone (TestClassificationPolicy|TestClassificationGuard|SlowTestRegistryMerge|SlowTestTripwire|NativeLinuxPolicyWitness): 52/52, 0 skipped, no stack overflow (lease 772f9a7c held=89s).
- A5 pair: 2/2 (lease 881e49cd).
- Fixed land rows land-1+land-2 classes: 138/138 = 34 + 104 (lease 4c73343e held=274s).
- SessionRunner C1677TempFamilyRegistryTests: 3/3 (lease 80f7fa7a).
- E2E ContractSnapshotTests: 5/5 (lease 873fc857; Kafka 19092 refused noise only). TestOutput/ removed before -Check.
- land-graph -Check (`-ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll`): exit 0, `approved`. No refresh needed.
- check-evidence-diff da4f168de..HEAD: commits=8 entries=0 violations=0.
- Migrations: none.
- No flakes, no reruns.

## Pins (recomputed on the final tree right before the push; master did not move)

- policyHash computed = stated = `fdc043758617a5e8c21735126539c559faf9dad95abe5f1122a6d5780ee9df4d` (unchanged).
- PinnedInputDigest printed = stored = `fb0af076ce8dcaec52af83234cc9fe5d642047028194f866c1c19ac6290e98a3` (unchanged).
- The brief's 50c33c11.../8510514a... values are not on master da4f168de; no CARD-1777 subject in this batch, so nothing re-pinned.

## Owed

- Native Windows lane: `RepositoryLeaseWindowsBoundaryTests` (windows: only), `CapabilityScriptTests`, `DelegateScriptCapabilityTests`, `AgentTaskLandDeliveryE2ETests`, and the windows: required rows listed in the CARD-1819 Code report.
- Method-scoped SourceLanding Mutation for the CARD-1819 PCs.
- No rc cut/port (master only, per brief). Activation: master land is not activation.
