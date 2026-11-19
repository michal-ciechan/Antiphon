# CARD-1820 S5 checkpoints

Code task `e922a94b`. Base `9bcd61113f631ec58f158ff3fb637736fb786fd4`. Branch `feat/card-task-e922a94b`.

## Verification design

- V-1: `RcTagBackingTests` answers `full`, `chain-to-full(anchor, age)`, or `not-backed(reason)`. Exactly 7 days and older are `chain-older-than-seven-days`. A shadow chain with fewer than two trusted cuts is `shadow-trust-absent`. A carry with no origin Full is `carry-without-anchor`. A missing origin, a seed anchor, and an anchor off the tagged parent walk each refuse. `gateCredit` on every result is false.
- R-1: `TestClassificationPolicyTests` once. Floor 42 is the argument-expanded roster on this tree (28 methods).
- R-2: `DerivedImpactIndexTests` alone. Floor 27. The driver project is built before this table, through `scripts/build-slot.ps1`, because `regenerate_script_reports_changed_then_unchanged` runs `scripts/regenerate-derived-impact-index.ps1 -NoBuild`. Building `tests/Antiphon.Tests` does not produce that driver. That build is unlisted for that reason. If `tests/derived-impact-index.json` changes, it is the last commit.
- R-3: the Linux guard batch below. Floor 39 on this tree: guard 1, merge 5, tripwire 2, native Linux 3, mechanical 11, `TaskPlatformGuidanceTests` 5, `C1729LayeringGuardTests` 11, lane 1. The four `[Test]` methods on `RunnerDefaultGuidanceTests` in the same file are outside the `TaskPlatformGuidanceTests*` filter, so they are not in this floor. `C1729LayeringGuardTests` stays `VIOLATIONS 0 LISTED 162`.
- R-4: `scripts/audit-land-verification-graph.ps1 -Check` exits 0.
- R-5: `scripts/check-evidence-diff.ps1` over `9bcd61113f631ec58f158ff3fb637736fb786fd4..HEAD` exits 0.
- R-6: `PinnedInputDigest` stated in `scripts/test-live-lane-run.ps1` equals the digest printed by `-PrintInputDigest`. Protected inputs are unchanged by `RcTagBacking.cs`, so the pin stays `063da62b48257c28f1b7fa1415ca4f5d7e739c8090631bc1ef9104584b68c8d1`.

`WindowsUnitPlatformAdmissionTests` is not a row. On Linux `C1210_Windows_linux_only_outcomes_are_exact` skips, and a skip is not a pass. Windows behaviour of this change is unobserved. The class is run once outside this table for that reason.

Not in this closed list, under the short-test bound (operator 2026-11-18): nightly script suites, stress, and the rest of the Unit lane. No positive controls. Mutation is waived (operator 2026-11-12). The brief's reseal-then-rebase sentence is not applied: this branch is fast-forward-only, and the protected pin inputs are not edited.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-irq-s5/` | tag-backing | `/*/*/RcTagBackingTests/*` | V-1 | all listed, 0 failed | 13 | 15 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-2 | all | CP-1 | policy | `/*/*/TestClassificationPolicyTests/*` | R-1 | all listed, 0 failed | 42 | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-3 | all | CP-1 | derived-index | `/*/*/DerivedImpactIndexTests/*` | R-2 | all listed, 0 failed | 27 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-4 | all | CP-1 | linux-guards | `/*/*/(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-3 | all listed, 0 failed | 39 | 10 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-5 | all | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0 | n/a | 8 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-6 | all | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef 9bcd61113f631ec58f158ff3fb637736fb786fd4 -HeadRef HEAD` | R-5 | exit 0 | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
| CP-7 | all | n/a | pin-digest | `grep -F "\$script:PinnedInputDigest = '063da62b48257c28f1b7fa1415ca4f5d7e739c8090631bc1ef9104584b68c8d1'" scripts/test-live-lane-run.ps1 && pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest > /tmp/s5-pin-computed.txt && computed=$(tr -d '[:space:]' < /tmp/s5-pin-computed.txt) && test "$computed" = 063da62b48257c28f1b7fa1415ca4f5d7e739c8090631bc1ef9104584b68c8d1 && rm /tmp/s5-pin-computed.txt && echo pin-ok` | R-6 | exit 0; stated digest equals computed digest | n/a | 3 | true | `ANTIPHON_LINUX_COMPILER_POLICY=0` |
