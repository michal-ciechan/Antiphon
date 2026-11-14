# Why missed: g132 WindowsUnitPlatformAdmissionTests roster closure (run 34d41b7e)

Debug task 6ebbcb6e. rc/2026-11-20 = b559bfba2e9a420495122d27171352fe9b7e1ba4,
Windows Full run 34d41b7e, chunk antiphon-all-group-132.

## Failure and classification

`WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates` failed on
the native Windows desktop with:

    c1210-unrostered-gate: Antiphon.Tests.Scripts.VolumeRetirementContentTests.Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent

Reproduced on Windows at master 8cec1e74e (1 executed, 1 failed, same message). It is
the same test and the same cause as the Linux red at master: the test is a portable
Roslyn source scan, so it fails identically on both platforms. It is not a Windows
product defect, not a timing defect and not an expected Windows skip.

The witness requires every `NativeLinuxGate.Require` / `C1008HostFixture.RequireNativeLinux`
call site under `tests/Antiphon.Tests` to have an entry in
`tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs` (`Native`, `NativeSourceOnly` or
`Existing`), and the reverse. Two gated methods in `VolumeRetirementContentTests` had no
entry:

- `Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent` (8 argument rows)
- `Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof`

The second was masked by the first: the closure stops at the first unrostered identity.

`VolumeRetirementContentTests` itself was already classified: class-level
`[NativeLinuxOnly(NativeLinuxReason)]` and a class-scoped `profiles.rc.exclusions` row in
`tests/test-execution-policy.json` ("Linux-only by construction: content retirement proofs
require native Linux ...", owner CARD-1609). Only the source-gate roster lacked the two new
methods. The policy file and therefore the nightly `policyHash` / `PinnedInputDigest` pins
are unchanged by the repair.

## Repair and verification

Commit 81dcad5270a8df1a2004375b2ffe50dd7b572a3c adds both identities to
`WindowsUnitSkipRoster.NativeSourceOnly` with the class's exact CARD-1609 reason (Results 8
and 1). The closure witness stays strict and unchanged. No product code changed.

Windows checkpoint CP-c132-class at 81dcad527, filter `/*/*/WindowsUnitPlatformAdmissionTests/*`:
executed=7 passed=7 failed=0 skipped=0, sourceState=clean buildSource=verified. The full guard
union run (direct build) passed 143/145; the two reds were environmental: the Windows child
audit needs a checkpoint source stamp (green above) and the regen test needs the unbuilt
`Antiphon.DerivedImpactIndex` driver (as in the CARD-1609 review CP-4).

## Introducing commits and card

- c4ba11dca6f9143ccaeb7856a2b07ea645ef8a2a "test: witness selector fixture retirement refusal
  and checkout residue" (cherry-pick of 512d5822116393dab17d3c2727cbe6d818509719) added
  `Selector_fixture_requires_ignored_scratch_no_remote_and_safe_parent`.
- b0fc2ff14191f6fe90747b1324a32dc3eca9e4ad "test: require a published parent beyond disposable
  seed exceptions" (cherry-pick of 7543063536899f1e7bb9d04ebd2fa9a30d711db0) added
  `Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof`.

Both were authored by Debug task 49ae044d-4cc7-4cc2-81f0-d7ae8f95eb2d (the server2 rollout
gate-9 selector-fixture retirement exception, filed under CARD-1514 / CARD-1609 content
retirement), branch origin/feat/card-task-49ae044d, reviewed tip
0976ca0ca27473bd8d03db76f59199966258a8cf. They reached master on 2026-11-13 with merge batch
209 (5c7e34c59e03829db975847dd12efddcfe591459 regenerated the index for that batch) and are in
rc/2026-11-20.

## Approving review and the selection it ran

Review task 01e2734e (Final, round 1, clean; report
`docs/investigations/2026-11-13-selector-fixture-retirement-final-review.md` on
origin/feat/card-task-01e2734e, commit 2398bf1da1f1541069e63713deba61a1cc2b23a4) accepted the
author's checkpoint receipts at 0976ca0c:

- CP-1 `/*/*/VolumeRetirementContentTests/*` 41/41
- CP-2 VolumeRetirementApplyGate/Receipt/HostProof/Wrapper 31 + 11
- CP-3 RollingVolumeRecycleScriptTests / RemoteScriptContractTests C1008 methods 48/48
- CP-4 ReleaseImpactEnvelopeTests, TestClassificationPolicyTests, TestClassificationGuardTests,
  SlowTestRegistryMergeTests, SlowTestTripwireTests, NativeLinuxPolicyWitnessTests,
  DerivedImpactIndexTests, ProcessSpawnLimitTests, ShellFixtureLineEndingContractTests,
  DockerStackDocumentationTests 162/163 (the one red was the unbuilt regen driver)
- scratch merge: index match, location witness, the eight selector rows and the seed-parent test.

## Why the failing test was outside it

1. `WindowsUnitPlatformAdmissionTests` is not in the standing guard union
   (docs/testing-and-build.md: TestClassificationPolicy/Guard, SlowTestRegistryMerge,
   SlowTestTripwire, NativeLinuxPolicyWitness, MechanicalPcContract, TaskPlatformGuidance,
   C1729LayeringGuard, TestLaneCategoryGuard, plus DerivedImpactIndex and C1584 for Merge).
   NativeLinuxPolicyWitnessTests checks the `[NativeLinuxOnly]` attribute against the policy
   exclusions, which were already correct at class level, so the guards that did run passed.
2. The derived impact index has no edge from a test source file to the closure witness.
   `tests/Antiphon.Tests/Scripts/VolumeRetirementContentTests.cs` has no explicit edge at all
   (a changed test file selects only its own class); `WindowsUnitSkipRoster.cs` maps to
   `WindowsUnitPlatformAdmissionTests`, but that file was not edited. Adding a gated test
   method therefore never selects the witness that requires the roster edit.
3. The author added a gated method, which is exactly the event the roster closure guards, but
   neither the plan's checkpoint table nor the Review asked for the roster witness, and the
   merge batch ran final-tree checks only (land graph, evidence diff, one compile,
   conflict-hunk tests).

## Cheapest pre-land check (impacted-classes row)

    tests/Antiphon.Tests/**/*.cs containing NativeLinuxGate.Require or C1008HostFixture.RequireNativeLinux
      -> /*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates

It is a single source-scan method (about 15-30 s, no process spawn). Equivalent cheaper policy
fix: add that one method to the standing guard union beside NativeLinuxPolicyWitnessTests, since
any Code/Debug that adds a native-Linux gate must already touch the roster.
