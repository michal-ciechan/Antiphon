# CARD-1820 S5 follow-up checkpoints

Code task `4940cf9f`. Base `cec1ed34f6c0b547ead0eb945c18d19f05f9a039`. Branch `feat/card-task-4940cf9f`.

## Verification design

- V-1: `RcTagBackingTests` answers `full` or `not-backed(reason)`. `Kind` is `RcTagBackingKind` (`Full`, `ChainToFull`, `NotBacked`). Chain refusals `anchor-in-future` and `anchor-time-unknown` do not contain the letters `full`. An exact Full completed after the document `utcNow` by more than `ExactFullClockSkewMinutes` (5) answers `full-time-in-future`. A null exact-Full time answers `full-time-unknown`. A completed time at the five-minute bound stays `full`. The document `utcNow` is the clock: a time after a year-2000 document clock is refused, and a time before a year-2099 document clock stays `full`. `chain-to-full` stays compiled off (`ChainFormEnabled` is the constant false) until a CARD-1836 D-8 producer exists. The result constructor and the `Chain`, `Full`, and `NotBacked` factories are internal. Depth 32, 10000, and 100000 answer `chain-invalid` and do not throw. `gateCredit` on every result is false. The earlier 40 witnesses stay green. Floor 51.
- R-1: `TestClassificationPolicyTests` once. Floor 42 is the argument-expanded roster carried from the S5 tree (28 methods). A higher executed count still meets the floor.
- R-2: `DerivedImpactIndexTests` alone. Floor 27. The driver project is built before this table, through `scripts/build-slot.ps1`, because `regenerate_script_reports_changed_then_unchanged` runs `scripts/regenerate-derived-impact-index.ps1 -NoBuild`. Building `tests/Antiphon.Tests` does not produce that driver. That build is unlisted for that reason. If `tests/derived-impact-index.json` changes, it is the last commit.
- R-3: the Linux guard batch below. Floor 39 on the S5 tree: guard 1, merge 5, tripwire 2, native Linux 3, mechanical 11, `TaskPlatformGuidanceTests` 5, `C1729LayeringGuardTests` 11, lane 1. `C1729LayeringGuardTests` stays `VIOLATIONS 0 LISTED 162`.
- R-4: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-5: `scripts/check-evidence-diff.ps1` over `cec1ed34f6c0b547ead0eb945c18d19f05f9a039..HEAD` exits 0.
- R-6: `PinnedInputDigest` stated in `scripts/test-live-lane-run.ps1` equals the digest printed by `-PrintInputDigest`. Protected inputs are unchanged by this follow-up, so the pin stays `3162a7daa58003f844e4d2b39237095e7cb54df897efc5dd8fc002dfb25a78c6`. `policyHash` stated in `tests/test-execution-policy.json` stays `0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542` because `Read-NightlyExecutionPolicy` refuses a stale hash. Neither pin is rewritten.

`WindowsUnitPlatformAdmissionTests` is not a row. On Linux `C1210_Windows_linux_only_outcomes_are_exact` skips, and a skip is not a pass. Windows behaviour of this change is unobserved.

Not in this closed list, under the short-test bound (operator 2026-11-18): nightly script suites, stress, and the rest of the Unit lane. No positive controls. Mutation is waived (operator 2026-11-12). The red-first run of this class at `e29c5881c8f3d728ebe19014a5b17553a3cbe1e1` is unlisted: it is the pre-fix witness, one build and one filter, before this table existed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1820-s5f/` | tag-backing | `/*/*/RcTagBackingTests/*` | V-1 | all listed, 0 failed | 51 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | all listed, 0 failed | 42 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-2 | all listed, 0 failed | 27 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | CP-1 | linux-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | all listed, 0 failed | 39 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef cec1ed34f6c0b547ead0eb945c18d19f05f9a039 -HeadRef HEAD` | R-5 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | n/a | pin-digest | `grep -F "\$script:PinnedInputDigest = '3162a7daa58003f844e4d2b39237095e7cb54df897efc5dd8fc002dfb25a78c6'" scripts/test-live-lane-run.ps1 && grep -F 0396d6a543917cd112d2f28dd0adfe8bd80ae549f2803df2bb866a751b261542 tests/test-execution-policy.json && pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest > /tmp/s5fu-pin-computed.txt && computed=$(tr -d '[:space:]' < /tmp/s5fu-pin-computed.txt) && test "$computed" = 3162a7daa58003f844e4d2b39237095e7cb54df897efc5dd8fc002dfb25a78c6 && rm /tmp/s5fu-pin-computed.txt && pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; Read-NightlyExecutionPolicy -RepoRoot (Get-Location).Path \| Out-Null; Write-Output pin-ok'` | R-6 | exit 0; stated digests equal computed digests | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
