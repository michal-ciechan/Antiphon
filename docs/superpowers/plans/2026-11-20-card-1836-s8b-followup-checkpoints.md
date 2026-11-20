# CARD-1836 S8b follow-up checkpoints

Original Code task: bdc2e824-9d04-4689-9a09-68860816ef60.
Follow-up Code task: 2f6279b4.
Base: d663f5eed4d22e69752963878602ab1b269e7b2f.
Follow-up base: ccf2dbb0a77d1f291dc8303fc92506afcc7c6ff4.
Authority: activation plan D-8, S8b review advisories A-1 through A-4 (review c383b0ccb85a2fef07269e5b7c992d02d1a64215), and follow-up blockers D-1 and D-2 (review 7d08422440fbc6682f603daaa436f52b98219727). Shadow mode only.
Owned files: scripts/lib/rc-shadow-sample.ps1, scripts/test-rc-shadow-sample.ps1, tests/Antiphon.Tests/Scripts/RcIncrementalShadowTests.cs.

Red witness commit: 7288542f4c585a77448c72be5587c463e7b3d323. Those four cases fail on the landed counter. The fix commit makes them pass and keeps V-11 green.
Round-2 red witness: 62d2271e174185492ebe99398fef98f6c4dc69c9. NonBooleanProvenanceFailureInvalidates and CarriedChunkResultInvalidatesCarries fail on ccf2dbb0a because carries stay valid with reason pilot-count. AbsentEvidenceKeepsCarriesUntilWired passes there and must stay green.

## Verification design

V-11 stays SampleSizeStrataAndReplay, MismatchInvalidatesCarries, and NoCarryAndOnePilotDoNotGrantTrust.
A-1 OneExecutedSampleIsNotAPilot: one executed sample, one of a planned 30, a planned N below the D-8 minimum, and 30 samples when Cchunk=601 (N=31) are not pilots. A bound plan of N=1 (Cchunk=1) and a plan of N=31 stay eligible. Two under-sampled cuts stay at pilot-count.
A-2 EmptyDigestIsUnproven: an empty digest does not match an empty digest, a missing digest does not match, and an uppercase digest does not match. A 64-character lowercase digest still matches.
A-3 MissingCarryableIsDirty: a chunk with no completelyCarryable value is dirty. It leaves Cchunk and cannot dilute the sample. An explicit true stays eligible. An explicit false stays out.
A-4 EvidenceDerivesSampleFailure: with no contracts and no caller failure, a missing outcome, an empty outcome, an absent result, an unexpected skip, a provenance failure, or a failed outcome invalidates every carry. A clean passed/verified result does not. A caller SampleFailure remains an additional failure and is the preserved object.
D-1 NonBooleanProvenanceFailureInvalidates: a present provenanceFailure that is not boolean false is provenance-failure. Witnesses cover a string, a number (including zero), a present null, an object, and the same values after ConvertFrom-Json. unexpectedSkip uses that same closed-flag rule, including a present null. Boolean false stays clean.
D-2 CarriedChunkResultInvalidatesCarries: once sampled is present, a result row whose chunkId is a carry and is not in sampled fails closed as carried-result for every outcome, including passed, and invalidates every carry. A dirty unsampled row that is not a carry stays dropped. A carry id listed in sampled is the launched sample and keeps the ordinary row rules, so the earlier clean passed/verified witness stays green.
F-1 precondition: before S6c or S6d uses this library for any carry decision, absent or unrecognised sample evidence must invalidate every carry when Carries is nonempty or CurrentSample is roster or executed, and a pilot without cchunk and stratumCount must be ineligible. This slice does not change that behavior. AbsentEvidenceKeepsCarriesUntilWired documents the current fail-open: null, empty, string, and wrong-case evidence keep carries, and an incomplete pilot with n=30 and nExecuted=30 counts. Flipping it now would turn the V-11 MismatchInvalidatesCarries roster and unknown assertions red. A skipped or pending marker is not used.
effectiveMode stays Full, gateCredit stays false, IncrementalValidation stays Shadow, and no trust record is written. No production caller is added.
R-1 through R-7 belong to other slices. This follow-up changes none of their surfaces.
No PC is defined here. Mutation execution is operator-waived (2026-11-12). Every PC stays pending for caller-commissioned SourceLanding Mutation.
Windows-only behavior is UNOBSERVED. WindowsUnitPlatformAdmissionTests is not in a zero-skip row because C1210_Windows_linux_only_outcomes_are_exact skips on Linux.
Restart: none. No publish, promote, tag, or IncrementalValidation flip.
Impacted-classes globs for these paths select no extra class. RcIncrementalShadowTests is the touched class.

## Performance budget

CP-1 EstimatedMinutes is 15, so its derived row deadline stays at the 45-minute advisory. CP-2 and CP-3 reuse that build. Rows are serial. No measured p95 yet; the harness child budget stays 45s with 10s cleanup and a 60s TUnit budget. CP-3's class timeout is the existing 600s DerivedImpactIndexTests budget and is not widened.
The driver for DerivedImpactIndexTests is built before CP-3 because the Antiphon.Tests build does not produce it. That build is unlisted and named in the Code report: the regenerate test runs scripts/regenerate-derived-impact-index.ps1 -NoBuild and fails when the driver DLL is absent.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | followup | `tests/Antiphon.Tests -> bin-c1836-s8bf/` | shadow-followup | `/*/*/RcIncrementalShadowTests/*` | V-11, A-1, A-2, A-3, A-4, D-1, D-2, F-1 | all 10 methods, 0 failed/skipped | 10 | 15 | true |
| CP-2 | followup | `CP-1` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | policy, mechanical contract, platform guidance, layering | all listed, 0 failed/skipped | 78 | 6 | true |
| CP-3 | followup | `CP-1` | derived-index | `/*/*/DerivedImpactIndexTests/*` | derived impact index | all listed, 0 failed/skipped | 27 | 12 | true |
