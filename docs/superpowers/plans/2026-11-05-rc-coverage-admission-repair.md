# RC coverage admission repair

Code and landing owner: 07ab1e49. Task base: cc4ab8e31349db759181a2931e8840ec6d79805f.
Source investigation: origin/feat/card-task-d8f70e4c at 1d15909de, docs/investigations/2026-11-05-debug-rc-coverage-incomplete.md. The original Windows 58-row reconciliation and exit-2/8 chunk attribution remain owed to Windows qualification.

## Implementation

S1 commits a failing independent admission roster witness. S2 adds the exact Windows-only rows/retained-reason markers in Debug's inventory, splits C0824's unreadable-directory argument into its own native method, and declares two separate all-platform manual obligations. Keep batch-85's retention rows. No coverage calculation, production behavior, assertion or timeout changes. Refresh policyHash and live-lane PinnedInputDigest together. The non-native exclusion digest changes only for the two explicitly recorded manual rows; no migration baseline entries change.

## Performance budget

The initial native incremental compile and tool bootstrap are explicitly allowed additional builds: they establish compiler health before any cold checkpoint. Native output; UseAppHost=false; TEMP=/tmp. One lease per driver. Main checkpoint build is shared by three rows. Three other suite witnesses have their own isolated builds. All estimates below are provisional, not measured p95; CARD-1404/CARD-1720 own bounded measurements. Admission is separate (45-minute checkpoint default). DeriveTotalMinutes supplies the execution allowance; do not widen it. Preserve TRX per-test durations and tool build/slot/row timings in ignored evidence; fixture phases not emitted remain unknown.

No full assembly is selected. Unit's invariant is classification/compiled policy consistency across the entire Unit lane; it omits delivery, landing, leases and persistence. Whole Unit has a historical 35-minute incomplete run (CARD-1720); if this selection exceeds 30 minutes the caller owes Debug, and it cannot be credited green from a timeout. No unchanged green repetition is needed. No test changes while a run is active. Groups contain at most three rows.

## Verification design

V-1: independent source roster for every public test method in the affected files and the real nightly disposition/coverage functions distinguish native Windows exclusions, Linux requirements and portable siblings. A missing native row leaves NotExecuted and fails coverage.
V-2: source census, classification/registry/tripwire, all four compiled native witnesses and refusal/parity cases enforce exact pinned markers, owners, both-platform dispositions and unchanged baseline reasons.
V-3: full nightly/release policy and coverage consumers, live-lane input pin, mechanical instruction and platform guidance contracts; policyHash and PrintInputDigest agree with shipped literals.
V-4: complete C0824 release-policy class preserves the supported long-path sibling after splitting the POSIX input. Its existing real git/service fixture and outcome assertions remain unchanged.
V-5: whole Unit lane. V-6: named E2E report-delivery policyHash consumer (real isolated Program, never the production runner).
R-1: new admission witness red on pre-fix source, then green. R-2: required skipped terminal rows remain incomplete; ordinary release coverage tests preserve missing-expanded-UID and zero-result refusal.
Manual M-1: strict coverage implementation unchanged; exact batch-85 rows retained; full base..HEAD evidence guard; merge-tree against fetched master; OS witness inventory and startup audit (no runtime/startup change, restart none).
Manual M-2 (pending Windows): reconcile the original 58 raw results and exit-2/8 chunks; fresh pinned Windows Full run must execute all remaining obligations, including all 35 CARD-1199 cases and native landing/capability siblings. No Linux receipt discharges this.
Manual M-3 (separately commissioned): CARD-1545 exact-filter parity producer/context/consumer receipt; CARD-1605 native Linux ANTIPHON_REPORT_STORE_MEASUREMENTS=1 normal/contended cost sample. Their exclusion is no execution credit.

### Positive controls

All pending SourceLanding Mutation, method-scoped on the landed source; Code performs no deliberate mutation.
PC-1: remove each new native row (11 variants; one per added row), reseal policyHash and the input pin, and require RcCoverageAdmissionTests.Windows_and_linux_obligations_remain_exact to fail at exact-windows-dispositions/coverage-sweep, naming the missing methods. Restore/green each selection.
PC-2: broaden a native row to all platforms (11 variants); the same exact method must fail linux-retains-native.
PC-3: broaden each manual obligation to a class exclusion (2 variants); the same method must fail portable-siblings/exact-windows-dispositions. If a class has no ordinary sibling, use LiveLaneIsolationTests only; load-sample scope is independently asserted by the explicit row contract in the witness.
PC-4: remove either manual row (2 variants), reseal hashes; the same method must fail exact-windows-dispositions and coverage-sweep.
PC-5: omit one retained-reason native marker; NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute in its owning assembly must fail missing-marker (one variant per affected suite). PC-6: stale either hash literal; C599_ProfileSchema / LiveLaneRunnerTests.WrapperOnlyPaidManifest must fail (2 variants). Mutation owns missing-control discovery.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | n/a | admission-red-first | `pwsh -NoProfile -File scripts/test-rc-coverage-admission.ps1` | V-1, R-1, R-2 | 7 harness assertions, 0 failed after S2 | n/a | 1 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-07ab-policy/ | policy-consumers | `/*/*/(RcCoverageAdmissionTests*)\|(ReleaseGatePlatformTests*)\|(ReleaseGatePolicyTests*)\|(LiveLaneRunnerTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(NativeLinuxPolicyMatcherParityTests*)\|(NativeLinuxPolicyWitnessMutationTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(NightlyScriptsTests*)\|(NightlyDiscoveryParserTests*)\|(NightlyExecutionDiagnosticTests*)\|(ReleaseGatePublicationAuthorityTests*)\|(RcFailureHistoryTests*)\|(ReleaseGateActivationTests*)\|(InterimVerificationReadinessTests*)/*` | V-1, V-2, V-3, R-1, R-2 | all listed, 0 failed | 100 | 8 |
| CP-3 | S2 | CP-2 | whole-unit | `/*/*/*/*[Category=Unit]` | V-5 | >=1 executed, 0 failed | 1 | 20 |
| CP-4 | S2 | CP-2 | full-split-class | `/*/*/C0824DesktopLegacyReleasePolicyTests*/*` | V-4 | all listed, 0 failed | 1 | 5 |
| CP-5 | S2 | tests/Antiphon.SessionRunner.Tests -> bin-07ab-runner/ | runner-markers | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | V-2 | all listed, 0 failed | 4 | 3 |
| CP-6 | S2 | tests/Antiphon.PtyHost.Tests -> bin-07ab-host/ | host-markers | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | V-2 | all listed, 0 failed | 4 | 3 |
| CP-7 | S2 | tests/Antiphon.Agents.Pty.Tests -> bin-07ab-pty/ | pty-markers | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | V-2 | all listed, 0 failed | 4 | 3 |
| CP-8 | S2 | tests/Antiphon.E2E -> bin-07ab-e2e/ | report-policy-consumer | `/*/*/ReleaseGateReportDeliveryTests*/*` | V-6 | all listed, 0 failed | 1 | 5 |
| CP-9 | S2 | n/a | nightly-coverage-script | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1` | V-3, R-2 | >=80 harness assertions, 0 failed | n/a | 2 |

| CP-10 | S3 | n/a | platform-owner-rosters | `pwsh -NoProfile -File scripts/test-rc-platform.ps1 -Case C1323_PlatformPolicy` | V-3, R-2 | 20 harness assertions, 0 failed | n/a | 1 |

## S3 fixture roster repair

The first S2 policy run exposed two introduced stale assumptions in C1323_PlatformPolicy: CARD-1323 and CARD-1614 had exactly three owned rows before this change. Retain the original three Linux E2E identities and all original mixed-platform checks, then assert the exact five CARD-1323 suite/class/platform keys and both new CARD-1614 native scopes (five rows total). No coverage, timeout, or result assertion is relaxed. CP-10 closes this repair before another cold build; the full ReleaseGatePlatformTests class remains in CP-2. PC-1 deletion variants for these four rows must also fail C1323_PlatformPolicy in separate method-scoped Mutation cycles.

## Non-native digest maintenance record

Before: 89 non-native records; digest `8397984d90368a0ef1cbc5991610ae50328165b40ac2503559ae0562d72b554d`. After: 91 records; digest `be806b71aa6cdf11edc0961cb1babd782034c0556e5f2e985dbbe8f2f7d7592f`. All existing records are byte-for-byte canonically retained. Only these two records are appended (neither is a migrated native identity):

```json
[
  {
    "suite": "antiphon",
    "class": "LiveLaneIsolationTests",
    "methods": [
      "StartupArgvMatchesProductionApplyOff"
    ],
    "reason": "Separate CARD-1545 producer/consumer parity workflow: exact-filter consumer requires an owned portable context and source/run/build-bound PTY producer capture receipt; commissioned via scripts/test-live-lane-isolation.ps1, never the RC wildcard.",
    "owner": "CARD-1545"
  },
  {
    "suite": "antiphon",
    "class": "AgentReportStoreLoadTests",
    "methods": [
      "Store_samples_record_normal_and_contended_phases"
    ],
    "reason": "Separate manual CARD-1605 measurement obligation: commission ANTIPHON_REPORT_STORE_MEASUREMENTS=1 on native Linux and retain normal/contended phase samples; enabling it on Windows cannot run its Linux executable.",
    "owner": "CARD-1605"
  }
]
```

The literal digest remains a frozen reviewed oracle, never computed from live policy during a test. Existing R-13 variants and four suite witnesses remain required.
