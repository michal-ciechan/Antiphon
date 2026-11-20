# CARD-1836 S6c prerequisite checkpoints

Original Code task: 3a5b76f5.
Base: c6565bc95776c93a01109385d25420d19372afe8.
Branch: feat/card-task-3a5b76f5.
Authority: port scripts/lib/rc-shadow-sample.ps1 and scripts/lib/rc-dirty-order.ps1 to compiled C# before S6c or S6d wire them. Shadow only. effectiveMode stays Full and gateCredit stays false.
Owned files: scripts/lib/Antiphon.Nightly/RcPort.cs, scripts/lib/Antiphon.Nightly/RcShadowSample.cs, scripts/lib/Antiphon.Nightly/RcDirtyOrder.cs, scripts/lib/rc-shadow-sample.ps1, scripts/lib/rc-dirty-order.ps1, tests/Antiphon.Tests/Scripts/RcPortCorpus.cs, tests/Antiphon.Tests/Scripts/RcShadowSampleCSharpTests.cs, tests/Antiphon.Tests/Scripts/RcDirtyOrderCSharpTests.cs, tests/Antiphon.Tests/Scripts/RcShadowDirtyDifferentialTests.cs, tests/Antiphon.Tests/Scripts/Fixtures/card-1836-s6c-rc-port-golden.json.

Red witness: d0d685fa49f7a20015de657e963bca94d3fa11ef. The 19 new methods fail because Antiphon.Nightly.RcShadowSample and RcDirtyOrder are not compiled into the test assembly. The implementation commit adds those types and the thin wrappers.

## Verification design

The C# port is the only implementation. The PowerShell files forward to it. No production caller is added. Nothing in nightly-run, nightly-tests, release-cut, publish, promote, release-gate, release-selection, or nightly-stopped-run-seed changes.
The differential corpus is the pre-port PowerShell golden, generated once at c6565bc95776c93a01109385d25420d19372afe8. Do not regenerate it after the wrappers replace the old logic.
Existing RcIncrementalShadowTests (12) and RcDirtyLaunchOrderTests (6) stay green through the wrappers.
Touching scripts/lib/Antiphon.Nightly selects LiveLaneRunnerTests (4) and NightlyCoreTests (1). Pre-warm the nightly-core cache before those rows so the 45s child budget is not spent on a cold Release build. That internal build uses its own output directory and is the existing nightly-core precedent.
TestClassificationPolicyTests runs once. The guard union is not in this table because the policy file is not touched.
DerivedImpactIndexTests runs alone. Its driver is built through the build slot before that row, then scripts/regenerate-derived-impact-index.ps1 -NoBuild twice. That driver build is unlisted and named in the Code report.
No PC is defined. Mutation execution is operator-waived (2026-11-12). Every PC stays pending for caller-commissioned SourceLanding Mutation.
Windows-only behavior is UNOBSERVED. WindowsUnitPlatformAdmissionTests is not in a row.
Restart: none. No publish, promote, tag, or IncrementalValidation flip.

## Performance budget

CP-1 EstimatedMinutes is 15, so its derived row deadline stays at the 45-minute advisory. Later rows reuse that build. Rows are serial. The harness child budget stays 45s with 10s cleanup and a 60s TUnit budget. DerivedImpactIndexTests keeps its existing class budget and is not widened.
The nightly-core pre-warm and the derived-index driver build are unlisted. They run through the build slot and are named in the Code report.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | port | `tests/Antiphon.Tests -> bin-c1836-s6c/` | csharp-port | `/*/*/(RcShadowSampleCSharpTests*)\|(RcDirtyOrderCSharpTests*)\|(RcShadowDirtyDifferentialTests*)/*` | S8b and S6a witnesses plus the 199-case golden | all 19 methods, 0 failed/skipped | 19 | 15 | true |
| CP-2 | port | `CP-1` | shadow-harness | `/*/*/RcIncrementalShadowTests/*` | existing S8b harness through the wrapper | all 12 methods, 0 failed/skipped | 12 | 12 | true |
| CP-3 | port | `CP-1` | dirty-harness | `/*/*/RcDirtyLaunchOrderTests/*` | existing S6a harness through the wrapper | all 6 methods, 0 failed/skipped | 6 | 8 | true |
| CP-4 | port | `CP-1` | policy | `/*/*/TestClassificationPolicyTests/*` | classification policy once | all listed, 0 failed/skipped | 42 | 6 | true |
| CP-5 | port | `CP-1` | derived-index | `/*/*/DerivedImpactIndexTests/*` | derived impact index | all listed, 0 failed/skipped | 27 | 12 | true |
| CP-6 | port | `CP-1` | nightly-core | `/*/*/NightlyCoreTests/*` | nightly-core harness after the new sources | all listed, 0 failed/skipped | 1 | 4 | true |
| CP-7 | port | `CP-1` | live-lane | `/*/*/LiveLaneRunnerTests/*` | live-lane runner after the nightly library change | all 4 methods, 0 failed/skipped | 4 | 6 | true |
