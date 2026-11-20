# CARD-2010 S6c port advisory checkpoints

Code task: c4c6bc59. Port landing owner: 3a5b76f5.
Base: 8af57860b6c0f334e7ffb6bf75f86b9b90179ed3 on feat/card-task-c4c6bc59. origin/master does not contain that port commit, so this work stays on the replay tip.
Authority: close the CARD-1836 S6c review advisories before S6c or S6d wiring. Shadow only. effectiveMode stays Full and gateCredit stays false.

## Verification design

A-1 unwraps PSObject at the RcPort boundary so a wrapped chunk id, attempt, or one-element array matches the pre-port libraries at c6565bc95776c93a01109385d25420d19372afe8.
A-2 pins camelCase on the PowerShell wrappers. The proposal file writer already emits camelCase.
A-3 commits scripts/generate-rc-port-golden.ps1. `-AppendAdvisories -Prove` rewrote the six advisory cases from those old libraries and compared the file to `JsonNode.ToJsonString(WriteIndented)` plus a trailing newline: 205 cases, 0 mismatches, bytes match. The diff against the 199-case fixture is one append-only hunk.
A-4 adds the contract Keys entry, the non-integer sequence, and the array-valued key, with the old libraries as the expected output. Keys shadowing is only on the contract path.
A-5: the port wrappers do not dot scripts/lib/rc-forced-full.ps1. S6c and S6d wiring must dot that file in the wiring script. scripts/test-rc-shadow-sample.ps1 and scripts/test-rc-dirty-order.ps1 dot the wrappers and do not call Get-RcForcedFullDecision. scripts/lib/rc-admission.ps1 dots rc-forced-full.ps1 itself, and scripts/lib/rc-baseline.ps1 dots rc-admission.ps1. scripts/test-rc-forced-full.ps1 dots rc-forced-full.ps1 directly.
Windows behavior is UNOBSERVED. WindowsUnitPlatformAdmissionTests is omitted on Linux.
No PC is defined. Mutation execution is operator-waived (2026-11-12). Every PC stays pending for caller-commissioned SourceLanding Mutation.
Restart: none.

Min floors are the case counts on this tree: a `[Test]` with N `[Arguments]` counts as N, and `[Test]` tokens inside string literals are not cases. Policy is 45. The guard union is 43. DerivedImpactIndexTests is 27. Antiphon.Tests ProcessSpawnLimitTests is 57.

Unlisted, named in the Code report: the red four-class run, the two green confirmations of that filter, the derived-impact driver build and its two regenerate passes, and the checkpoint tool host build. The nightly-core cache for this source stamp is installed by the casing witness. A separate pre-warm is only needed if that stamp changes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | advisories | `tests/Antiphon.Tests -> bin-c2010-s6c/` | csharp-advisories | `/*/*/(RcShadowSampleCSharpTests*)\|(RcDirtyOrderCSharpTests*)\|(RcShadowDirtyDifferentialTests*)\|(RcPortWrapperCasingTests*)/*` | V-1 | all 26, 0 failed/skipped | 26 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | advisories | `CP-1` | shadow-harness | `/*/*/RcIncrementalShadowTests/*` | V-2 | all 12, 0 failed/skipped | 12 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | advisories | `CP-1` | dirty-harness | `/*/*/RcDirtyLaunchOrderTests/*` | V-3 | all 6, 0 failed/skipped | 6 | 6 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | advisories | `CP-1` | policy | `/*/*/TestClassificationPolicyTests/*` | V-4 | 45 executed, 0 failed/skipped | 45 | 6 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | advisories | `CP-1` | guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | V-5 | 43 executed, 0 failed/skipped | 43 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | advisories | `CP-1` | derived-index | `/*/*/DerivedImpactIndexTests/*` | V-6 | 27 executed, 0 failed/skipped | 27 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | advisories | `CP-1` | nightly-core | `/*/*/NightlyCoreTests/*` | V-7 | 1 executed, 0 failed/skipped | 1 | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | advisories | `CP-1` | live-lane | `/*/*/LiveLaneRunnerTests/*` | V-8 | all 4, 0 failed/skipped | 4 | 6 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | advisories | `CP-1` | spawn-tests | `/*/*/ProcessSpawnLimitTests/*` | V-9 | 57 executed, 0 failed/skipped | 57 | 10 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | advisories | `tests/Antiphon.SessionRunner.Tests -> bin-c2010-s6c-runner/` | spawn-runner | `/*/*/ProcessSpawnLimitTests/*` | V-10 | both methods, 0 failed/skipped | 2 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | advisories | `tests/Antiphon.Agents.Pty.Tests -> bin-c2010-s6c-pty/` | spawn-pty | `/*/*/ProcessSpawnLimitTests/*` | V-11 | both methods, 0 failed/skipped | 2 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | advisories | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | V-12 | exit 0; inherited mismatch is a warning | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-13 | advisories | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 8af57860b6c0f334e7ffb6bf75f86b9b90179ed3 -HeadRef HEAD` | V-13 | exit 0 | n/a | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-14 | advisories | n/a | pins | `pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $policy = Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path; $raw = @(& ./scripts/test-live-lane-run.ps1 -PrintInputDigest); $digest = [string]$raw[-1]; $pin = [regex]::Match([IO.File]::ReadAllText("./scripts/test-live-lane-run.ps1"), "PinnedInputDigest = .([0-9a-f]{64}).").Groups[1].Value; if ($digest -cne $pin) { throw ("digest drift " + $digest) }; Write-Output ("policyHash " + $policy.Hash); Write-Output ("digest " + $digest)'` | V-14 | exit 0, policyHash matches the file, digest matches the pin | n/a | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-15 | advisories | n/a | merge-tree | `git fetch origin master && git merge-tree --write-tree origin/master HEAD` | V-15 | exit 0, no conflicts | n/a | 4 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 109 minutes, the sum of EstimatedMinutes. CP-1 is 15 so its derived row deadline stays at the 45-minute advisory.
