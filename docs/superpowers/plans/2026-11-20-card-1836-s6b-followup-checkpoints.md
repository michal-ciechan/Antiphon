# CARD-1836 S6b follow-up checkpoints

Code/landing owner 56e5508d. Base fc45631910173cbba5f458498ac2479a85066b4f. Product fix 983da5cb93c09ec0ed54a7c1031aee548f525bbc.

One evidence root per nightly run. The source snapshot, build seal, launch join, discovery revision and chunk revision for a run live under that run's directory. Passed requires exit 0, a present parseable results file, and no drain timeout. A swallowed recording failure is one run-log line and does not change the verdict. A torn revision is renamed into place, stays not-run, and does not block the next slot. Launch output is stamp-checked before the timed phase. Recording stays shadow: effectiveMode Full, gateCredit false, no Incremental, no publish. `ANTIPHON_RC_RUN_EVIDENCE=off` writes nothing.

## Verification design

V-12 stays the four original RcRunEvidenceTests methods. The follow-up adds OneEvidenceRootPerRun, ChunkOutcomeNeedsResults, TornRevisionStaysNotRun and LaunchStampIsCheaper. Impacted classes on this range are NightlyCoreTests, LiveLaneRunnerTests and TestLaneCategoryGuardTests. TestLaneCategoryGuardTests is inside the guard row.

PC-V12-join, PC-V12-inputs, PC-V12-interrupt and PC-V12-once stay pending for SourceLanding Mutation.

## Performance budget

CP-12 includes the real nightly-run witness, about one minute per run after the nightly core is cached, three runs in that witness. The first row EstimatedMinutes is 15. Later rows are separate builds. Serial execution: do not overlap these rows. Windows is UNOBSERVED. `scripts/lib/nightly-run-impl.ps1` is a restart-impact path. This task does not restart.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-12 | S6b-followup | `tests/Antiphon.Tests -> bin-c1836-s6bf/` | linux-run-evidence | `/*/*/(RcRunEvidenceTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-12, one-root witness, chunk outcome, torn revision, launch stamp, nightly core, live lane | all listed, 0 failed/skipped | 13 | 15 |
| CP-20 | S6b-followup | `tests/Antiphon.Tests -> bin-c1836-s6bf-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | policy, categories, layering | all listed, 0 failed/skipped | 81 | 15 |
| CP-21 | S6b-followup | `tests/Antiphon.Tests -> bin-c1836-s6bf-impact/` | derived-impact | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | committed derived-impact index | all listed, 0 failed/skipped | 27 | 15 |

### Restart

Restart target: none. `scripts/lib/nightly-run-impl.ps1`, `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/rc-run-evidence.ps1` and `scripts/lib/Antiphon.Nightly/RcRunEvidence.cs` are on the startup census. A later rc pick owes the Windows startup probe. This Linux task does not run it.
