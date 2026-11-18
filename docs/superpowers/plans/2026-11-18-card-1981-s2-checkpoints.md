# CARD-1981 S2 lease TMPDIR and the dotnet-sdk-empty family

Base: 6414a71967575b2a7dfbf283ee273f930b7a2100. Linux only. The SDK leak was observed on Linux; Windows `%TEMP%` behaviour stays unchanged.

CP-1 and CP-3 through CP-5 are one TUnit method. CP-2 runs both methods of `C1981LeaseTempDriverTests`: the leased child and the symlink refusal. The first row pays the cold `Antiphon.Tests` build. Later rows of the same project reuse it. Session runner rows build that project once. Rows are serial and stay under ten minutes. Mutation checks are waived. CP-6 is the guard union: a `[NativeLinuxOnly]` marker is a policy input, and this slice adds the Windows rc exclusion rows for the three new antiphon witnesses. `C1210_Windows_linux_only_outcomes_are_exact` is the one Linux skip.

## Verification design

The red commit adds the three witnesses before the production change. A leased build-slot child and a checkpoint-tool `dotnet` child must see `/tmp/aslot-<8 hex>` as `TMPDIR` and must leave no 6-character directory there or as a new top-level `/tmp` name. The sweep witness accepts only an empty mode-0700 uid-1654 directory older than one hour and rejects every other shape. `TempSweep` stays disabled, and `dotnet-sdk-empty` ships with `DefaultEnabled` false.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1981a/` | lease-wrapper | `/*/*/BuildSlotScriptTests/C1981_LeaseTempDirConfinesSdkScratch` | V-1 | all listed, 0 failed | 1 | 9 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | S2 | CP-1 | lease-driver | `/*/*/C1981LeaseTempDriverTests/*` | V-2 | all listed, 0 failed | 2 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1981b/` | sdk-shape | `/*/*/C1677TempSweepNativeTests/C1981_dotnet_sdk_empty_accepts_only_the_exact_shape` | V-3 | all listed, 0 failed | 1 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | S2 | CP-3 | registry | `/*/*/C1677TempFamilyRegistryTests/Every_family_producer_still_contains_its_prefix` | V-4 | all listed, 0 failed | 1 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | S2 | CP-3 | random-prefix | `/*/*/C1677TempSweepTests/Unregistered_random_and_process_names_aggregate_by_prefix` | V-5 | all listed, 0 failed | 1 | 2 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | S2 | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | V-6 | all listed, 0 failed; C1210_Windows_linux_only_outcomes_are_exact skips on Linux | 118 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
