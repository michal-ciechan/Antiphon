# Merge batch 185 (task 053dff43)

Outcome: landed. All three subjects merged, nothing excluded. master 24a3e6059ffad1e5e57234b2a9488b52bc9da112 ->
60009e9b4b8777e2f8f902966a1b6ebd6c740871 (plain fast-forward, first try; master had not moved since the start).
No rc/* ref touched.

## Per-commit table (base 24a3e6059)

| New SHA | From | Patch-id | Subject |
|---|---|---|---|
| b5d8930f9d8ba5345144b41f308d344efcf4c2cc | 9692473692d3 | DIFF (resolved) | fix(rc-g131): C1336 admits policy suite executables; build the index driver with the solution |
| (skipped, empty) | b91c030ca67c | folded into b5d8930f9 | fix(rc-g131): place the index driver's solution entries clear of master's suite appends |
| b74d466ee6c797aa751fe3c530b6cbe5d424edd9 | cf81ba0bc564 | MATCH | docs: run ae84478d g131 ... Windows failure and why missed |
| c4b3134ebcc7b61d524737ee2b280951336d8bda | a25f7e33e1c3 | MATCH | docs: g131 note covers master's CARD-1830 suite |
| fcb5831463b755c8b169b00a5fe35db214f28007 | 0f391a03dd76 | MATCH | test(rc-g131): solution build must produce the regenerate driver |
| dbb726ef8ae9d12c744304421726fdca7ff1f812 | 5945286c4fdb | MATCH | docs: why run ae84478d g131 reds were missed |
| 8917844b6e5259b15dab03763337b42a3a34545d | ae20e70ca235 | MATCH | docs: g131 why-missed note names the mirror-project split family |
| 42d25c8ec75bdf3ebc98a1c98424e69178a61de0 | 785d5db11e44 | MATCH | test(card-0747): red witnesses for stale land/completion note replay |
| f02b8e5d1a5ea01609a1c9d4637e006908c4e551 | d5358ec99438 | MATCH | fix(card-0747): settle aged terminal land notes ... |
| 182955b35702f332809c5b8bf3e97a2d3dc75970 | 448e1e59f5ee | MATCH | fix(card-0747): never expire a typed note whose receipt is stored ... |
| 956806d72bf12e2ac784fdcf2759bdde584ae020 | 8a4ec2ca1218 | MATCH | test(card-0747): typed Held row still confirms ... |
| dda72b2b6c96635ce55c80acf2ded023178a5a47 | 02d556fe120e | MATCH | test(card-0747): a typed Pending row of an aged note still confirms ... |
| fa3f8e0f0c985c5838fb2c1683477e75e745f044 | 6dfcb3749dbe | MATCH | docs(card-0747): receipt replay investigation ...; expire route |
| e6b95fe0019060b3c2233039e9ee054d1cd2dada | a3190d661713 | MATCH | test(card-0747): D-1 red witnesses for the expire route ... |
| 13c2f6d84bbc50c101576cef3226e822959b67fe | bf90a5d1a5a8 | MATCH | test(card-0747): D-1 child task fixture uses a Dispatched task |
| d511322926f0292b967b68bdf750ad1f2d1b2b60 | 8a931a10705a | MATCH | fix(card-0747): D-1 expire apply needs the operator token ... |
| b4c860dea5912f44f7570455adf922dda91f624d | 69bdd6720219 | MATCH | test(card-0747): D-1 expire witnesses compare the notes present before the request |
| e9d02e72cdc5b12f2e2ee051890855f4dee51ebb | 5727977525b0 | MATCH | test(card-1843): mirror test paths must select their own project |
| 8e5892703bd21406ef29578304c4046dc902af9d | 1a11a04e2c0f | MATCH | fix(card-1843): impacted-classes maps mirror test paths ... |
| b165bbd4f3ac702de1f01c1dddd966f54c8de54f | 984ded3e5e9c | DIFF (context only) | fix(card-1843): a class declared in several projects runs in each ... |
| 357808314502d5bfd4c4c485c67ba9626e0eec38 | f0d09b5e17f5 | MATCH | test(card-1843): regenerate the derived impact index ... |
| ee98990d02cfa6a009fdf9ac2b09de9eb7dceca9 | 944fff9b35b5 | MATCH | test(card-1843): C1801 why-missed expects InProcessGitCommandsTests ... |
| 47261a24dd39d5e1df8b4a09086f69400499fb2b | merge | n/a | chore: refresh land-graph records for the landing batch (land-graph-land-refresh.ps1) |
| 1637b5d56343c297fe22479eae43d7cd2c04db9a | merge | n/a | test: regenerate the derived impact index for merge batch 185 |
| 2850a7411a7c8c33a4ce7ca6ec227648a09de807 | merge | n/a | test: count the CARD-0747 land-note classes as owed notification consumers |
| 60009e9b4b8777e2f8f902966a1b6ebd6c740871 | merge | n/a | test: pin the selector filters after the CARD-0747 index refresh |

Skipped as report-only (.antiphon/*): 117987081 (Debug report), 12d2604be (fc778a50 report), 4d1b25be3 (3e96732b
report), 566852759 (3b0e3e9e report + plans).

Patch-id notes:
- b5d8930f9: 969247369 conflicted on Antiphon.sln with master's CARD-1830 GitInProcess.Tests entries. Resolved to
  the branch's own final placement (b91c030ca): master's sln plus the net 3db1a43b0..b91c030ca sln change. Proof:
  the changed lines of b5d8930f9 equal the changed lines of `git diff 3db1a43b0 b91c030ca -- Antiphon.sln tests/fixtures`.
  b91c030ca was then empty and skipped. Final sln differs from the branch tip only by master's GitInProcess entries.
- b165bbd4f: changed lines identical to 984ded3e5; only hunk context differs (master's CARD-1304 7f632fb9b
  reflowed the WhyMissedTestsFilter constant).

## Merge-made changes (finding)

1. Land graph (sanctioned `scripts/land-graph-land-refresh.ps1`, own commit 47261a24d): -Check refused solely on a
   manifest mismatch. Only definition row changed: Antiphon.sln (the g131 driver project). old digest
   B0933881E8256BD9DEF52BF190009B9B536AA7AE33AC1FD24422ABB6D885B991 -> new
   B9A655F11128D56A769790EEEC758809FC425406A40E9C6DA396F0C89CEB9162, count 80 -> 80, pins
   415e66232d0d44ad4e59c579d332c8935147402e unchanged, changed-records: Antiphon.sln. The card-note post failed
   (I passed -Card rc-g131, not a board card); the notice is in this report. -Check after: approved, exit 0
   (and again on the final tree).
2. Derived impact index regenerated (1637b5d56): +2 classes from CARD-0747 (C0747LandNoteExpireEndpointTests,
   C0747LandReceiptReplayTests) in many path rows. The capped path AgentTaskLandNotificationService.cs went 38 -> 40
   classes, owed 13 -> 15, and its 25 kept classes now owe DispatchHoldVisibilityTests and
   RepositoryMutationLeaseOwnerTests.
3. That reddened three exact pins (combination of CARD-0747's new classes + the regen; no subject was red alone):
   - DerivedImpactIndexTests.capped_real_paths_keep_the_specific_class: `OwedCount(...NotificationServicePath)` 13 -> 15
     (2850a7411). Red proven at 1637b5d56 (should be 13 but was 15), same shape as CARD-1304 89bd8c855.
   - ImpactedClassesSelectorTests C1801_non_merge_land_notification_filter_names_every_class and
     C1801_why_missed_paths_select_their_classes_and_windows_filters: red in the union at 2850a7411; constants
     LandNotificationDerivedFilter and WhyMissedTestsFilter updated (+2 C0747 classes, -DispatchHoldVisibilityTests,
     -RepositoryMutationLeaseOwnerTests) in 60009e9b4. Each constant now equals the selector's output byte for byte.
     Same shape as CARD-1304 7f632fb9b. Regen -NoBuild after it: unchanged.

## Verification (Linux server2, all on the final code unless noted)

- Solution: `dotnet build Antiphon.sln -c Debug -m:1 -p:UseAppHost=false` 0 errors (282 s); it produced
  scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll, so the g131 driver fix is
  proven by the normal build. Second incremental sln build after the refresh (embedded manifest) 212 s; third,
  Antiphon.Tests only, after the constant edit 264 s. Three builds instead of one: the manifest is embedded and two
  test pins had to change after the regen.
- Union (at 2850a7411, one process): 32 classes: DerivedImpactIndexTests, ReleaseGateFollowupTests,
  C0747LandReceiptReplayTests, C0747LandNoteExpireEndpointTests, C0747LandNoteExpireOffSwitchEndpointTests,
  AgentTaskLandQueuedReceiptTests, ImpactedClassesSelectorTests, MergeHighRadiusSubsetTests, guards
  MechanicalPcContractTests, TaskPlatformGuidanceTests, C1729LayeringGuardTests, TestLaneCategoryGuardTests,
  ProcessSpawnLimitTests, C1514LandingSplitBudgetTests, and every selector Antiphon.Tests class (AppDbContextModel,
  CheckpointReceiptSafety, ExpectationDebt, HostCleanupCheckpointReceipt, PreDispatchFailureDelivery,
  ReceiptScanDestinationIntegration, TestTimingOperationLabel, the 8 fixed land classes, AgentTaskLandReceipt,
  LandGraphAuditProjectInstance, LandReceiptScanCache). Result: 570 total, 568 passed, 2 failed (the two C1801
  constants above), 0 skipped, 26 min. C1729 printed `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.
- ImpactedClassesSelectorTests re-run at 60009e9b4: 42/42 (501 s, load ~28). A first attempt hit my 9 min cap with
  no summary (not run).
- Policy guard batch alone (TestClassificationPolicy|TestClassificationGuard|SlowTestRegistryMerge|SlowTestTripwire|
  NativeLinuxPolicyWitness): 52/52, 90 s, no stack overflow.
- A5 pair ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus|LinuxOnlyWindowsExclusionPolicy): 2/2.
- E2E (ContractSnapshotTests + selector LandDeliveryFixtureProbeTests): 19/19 (Kafka connect-refused noise only).
- land-graph -Check on the final tree: digest B9A655F1... count 80, approved, exit 0.
- import --plan of the documented Merge rows (6 rows: tests, land-1, land-2, land-3 = AgentTaskLandReceipt|
  LandGraphAuditProjectInstance|LandReceiptScanCache, ReleaseGatePlatformTests A5, e2e; messaging dropped, no
  migration): imported 6 rows, 0 warnings.
- Pins on the final tree: policyHash a1d534f78407944871f438cc0d0d008d6aa0b107db645ee1396796bfa504e99c (stored ==
  computed), PinnedInputDigest ba3268c2df4dd127bfb011d5b26c701d8780603040646f7b9738fc0f83a64bc1 (stored == computed).
  No re-pin commit. The brief's 50c33c11.../8510514a... are stale. CARD-1777 was not in this batch.
- check-evidence-diff 24a3e6059..60009e9b4: commits=25 violations=0.
- Migrations: none in any subject.
- Flakes: none seen.

## Owed

- Native Windows: ReleaseGateFollowupTests/C1336_NightlyCandidateIdentity and
  DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged after a Windows `dotnet build Antiphon.sln`
  (the g131 failures), plus the selector windowsFilter tests/Antiphon.E2E AgentTaskLandDeliveryE2ETests (windows:required).
- Mutation PCs: CARD-0747 M1-M4 (C0747_Expire_apply_needs_the_operator_token arms, C0747_Expire_apply_honours_the_off_switch);
  CARD-1843 PC-1..PC-5 (3e96732b) and PC-6 (GitInProcess row ShouldContain). The two merge-made pin edits
  (owed count 15, C1801 constants) are exact-equality pins and need no PC of their own.
- CARD-0747 live apply of POST /api/agent-tasks/land-notifications/expire (operator token) after a server build that
  contains it; this land is master only and does not activate anything.
- The g131 fixes reach the rc line only through the next cut.
