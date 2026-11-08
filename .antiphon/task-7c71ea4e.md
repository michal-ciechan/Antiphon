# Batch 121

Master fast-forwarded from 6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa to d0a3ee89e495baf364e8260abeab326db9c98f76. First push accepted. No subject excluded. rc/2026-11-10 stayed 74a8004f405bc54666fea33889839f0ff4d62ec7. Loop count 1.

## Commits

| Land | Source | Patch-id | Subject |
|---|---|---|---|
| 97d41cefee9ad4f5505fb7eeae71bd1f08e7fb18 | 444365c203b3368bd6629f701a1f4a7d71dbee24 | cfdaa7b8e8903319fc0bdc21b00e7077c731e15b | standing-ownership history |
| efddbf0e94a449e17f285bbed138f8ec5fcbf8c5 | ced32c1491f24badae89ba5e9ea0a6a7ccbdf618 | ecc4bdcf709ab1d4342d756bd46e02f1bd746de9 | card-file privacy forward |
| 7e7f5a966399df086ad09b6a3b3093de3e83e453 | 573a1a7f268bf0cb7a62f992d1fada106857f165 | 294dc1acebb2ef73e88556ff08f0f03a9cc76fd8 | pipeline.json load:null |
| c46677af596b0d2b7bc7cf8ddba185e94938ffcd | e62c70951f40a54c4217684caaac2ed94803188c | b5f175e589a02ea25eacfbc6aeb659686d62705a | CARD-1762 red witness |
| 91fac7eb2de6515798fb572c1b35a439778701d9 | 4f676427af5206a7a13bdd3439ac00c6578dc4da | 11a3c0e1ec99ae96cf1e6c7a217acf625386cdde | CARD-1762 source-generated JSON |
| 292f49dfa8797a868e73d7e27c5e06b9d59c1ce3 | 58d4a347ba3935da49e96a00a224527fe581db22 | 071a09d5a40ee3ba3051a662c9df5b2a4cb3df19 | CARD-1747 red roster |
| e51d4375a4bc57297bc052ef3734d0d3ee824347 | 1516f551cbca8a9d07e16917a4aa5c816928fb4f | 880c443f1796f2cd8bc50b2a7e71f4d967027e3f | CARD-1747 width-2 lane |
| d0a3ee89e495baf364e8260abeab326db9c98f76 | (this batch) | n/a | land-graph refresh |

All seven cherry-picks used `git cherry-pick -x`. Stable patch-ids matched. The last two auto-merged `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs` and still matched. No `.antiphon` or report-only commit was on a subject. Every subject commit was `+` from `git cherry origin/master`.

## Evidence

`scripts/check-evidence-diff.ps1` on 6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa..d0a3ee89e495baf364e8260abeab326db9c98f76: commits=8 entries=0 violations=0. The mirror was not shallow. `tests/test-execution-policy.json` was unchanged.

## Checks

Antiphon.sln build, `UseAppHost=false`, build-slot lease a24ca3f9, maxcpucount=6, 0 errors, 2044 warnings, 4m 25s. No `-m:1` rebuild.

Antiphon.Tests union, lease a42085b6: 134 passed, 0 failed, 0 skipped. C1729 stdout: `VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3`.

`C467*` and `StrictTrxReport*` are not test class names in this tree, so those operands selected nothing. The union still ran StandingSessionOwnershipTests, CardFilePrivacyMigrationTests, C1514HostLoadMigrationTests (both Down refusals and the three upgrade pairs), AppDbContextModelTests, RunnerReadJsonClientTests, ProcessSpawnLimitTests (including `Checkpoint_tool_classes_carry_their_own_limiter`), CheckpointToolProjectContractTests, and the policy sweep (TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness), plus MechanicalPcContractTests and TaskPlatformGuidanceTests.

Antiphon.E2E ContractSnapshotTests, lease 4c0a7695, waited 60s: 5 passed, 0 failed, 0 skipped. Kafka bootstrap lines to 127.0.0.1:19092 were refused during startup and did not fail the run.

Antiphon.SessionRunner.Tests RunnerReadJsonTests, lease 1f043d40: 3 passed, 0 failed, 0 skipped.

## Land graph

`-Check` before the refresh refused: selected digest E0565240FF824353AA5AA6B90D579DE5C7AEE799C6A73D5990F946C1D780BC2D count=71, recorded manifest 655B2EF4C8BBA2C254C69B41FBD22F0689643B9C1032403635EAD7D6400926E7 count=71. The cause is `InternalsVisibleTo` on `src/Antiphon.SessionRunner.Contracts/Antiphon.SessionRunner.Contracts.csproj`. One `-Update` approved. Pins blob stayed 7a2586e58bd4768648c6f670f6f2c4432f68936a. SDK stayed 10.0.403. Post-commit `-Check` approved the new digest, count 71. The refresh commit touches only the manifest and evaluation record.

## Owed

Windows native reruns, SourceLanding positive controls, and the whole Unit lane. The known inherited red `receipt_row_and_build_identities_refuse_blank_without_writing` (CARD-1772, Debug 8161c466) is in CheckpointReceiptSafetyTests, was not in this sample, and was not a reason to exclude CARD-1747.
