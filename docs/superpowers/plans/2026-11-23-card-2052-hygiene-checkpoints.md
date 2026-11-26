# CARD-2052 hygiene candidate: checkpoints

This is a separable candidate requested by the operator on 2026-11-23 ("merge what we have so far if it is
mergeable"). It is master `0f5fea9f158bad684030538d4cb908e8c467ff94` plus the parts of the CARD-2052 chain that change
no unlink decision:

- `57207d39a7b31375252f0fd94ec6eefce9dc2c74`: the four OutputCleanupTests Windows-only methods skip through
  `WindowsUnitSkipRoster.SkipOnLinux` and are rostered.
- The `WindowsUnitSkipRoster.cs` part of `32e16c27c492d7604b6b6d773d4d949f7eed64a9`: 27 Windows-only identities.
  These are the native custody and handle classes and the EvidenceFolderTests held-file retry.

The speedup (`TrackedIndexScope`, `OwnedOutputLedger`, `SafeDelete`, `SafeWrite`), its witnesses and plans, and the
context seams are not part of this candidate. The derived impact index was regenerated and is unchanged. Production
code is unchanged. Round: Final for this slice.

- V-1: OutputCleanupTests runs on Linux by method prefix with zero skips. That is 45 methods, master's Linux set.
- V-2: the roster guards pass. These are the C1210 roster/audit rows, the Final guards and the CP-9 driver ahead of
  DerivedImpactIndexTests.
- V-3: EvidenceFolderTests runs on Linux without its rostered Windows-only method.

The native Windows executions of the rostered methods stay pending, as on the CARD-2052 task branch. PCs: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2052-hyg/ | output-cleanup-linux | `/*/*/OutputCleanupTests/(a*)\|(b*)\|(c*)\|(d*)\|(e*)\|(f*)\|(g*)\|(h*)\|(i*)\|(j*)\|(l*)\|(m*)\|(n*)\|(o*)\|(p*)\|(r*)\|(s*)\|(t*)\|(u*)\|(wrapper_*)\|(windows_seal_docs_*)` | V-1 | 45 pass, 0 skipped | 45 | 15 | true | |
| CP-2 | all | CP-1 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(ProcessSpawnLimitTests*)/*` | V-2 | >= 40 executed, 0 failed, 0 skipped | 40 | 8 | true | |
| CP-3 | all | CP-1 | platform-admission-linux | `/*/*/(WindowsUnitPlatformAdmissionTests*)\|(WindowsUnitPlatformAdmissionInvariantTests*)/(C1210_Guard_*)\|(C1210_Native_*)\|(C1210_Roster_*)\|(C1210_Audit_*)\|(C1210_Host_*)\|(C1226_*)` | V-2 | >= 6 executed, 0 failed, 0 skipped | 6 | 4 | true | |
| CP-4 | all | CP-1 | evidence-folder-linux | `/*/*/EvidenceFolderTests/(red_run_*)\|(rerun_text_*)\|(green_run_*)\|(non_image_*)\|(host_and_git_*)\|(git_snapshot_*)` | V-3 | >= 6 executed, 0 failed, 0 skipped | 6 | 3 | true | TMPDIR=/tmp |
| CP-5 | all | n/a | derived-impact-driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false --verbosity minimal` | V-2 | exit 0 | n/a | 5 | true | |
| CP-6 | all | CP-1 | derived-impact | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | V-2 | >= 5 executed, 0 failed, 0 skipped | 5 | 6 | true | |
