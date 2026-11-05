# Repair the three inherited Unit failures

Original Code/landing owner: 9f03d97e. Base: 388ff482046a62f813a251208bfeabe7c21808ea.
Investigation: fc2bcb80, docs/investigations/2026-11-05-investigate-three-unit-base-failures.md on origin/feat/card-task-fc2bcb80.

## Repair
Restore the documented mechanical contract phrase in the TestDesign bundle within the 2,480-character guard. Add the two already NativeLinuxOnly retention methods to the existing method-scoped Windows exclusion, refreshing the canonical policy hash. Preserve tests, markers, baseline, lane categories and production code.

## Verification design
### Inspection
Read the mechanical contract assertion and composition path, stage-size assertion, native policy witness and marker, policy hash canonicalization, classification policy/guard, merge registry and slow tripwire tests, and the marked retention methods. The policy inspector checks compiled metadata on every OS. The two added methods are Integration cases; they remain required on Linux and excluded only on Windows. No OS-selected production behavior, helper or startup behavior changes. Activating the embedded instruction text needs a server rebuild/restart after landing; no restart is performed in this task.
### Proves it works now
- V-1: composed TestDesign instructions contain binding required after Code, while stage text stays <=2,480; full MechanicalPcContractTests and TaskPlatformGuidanceTests.
- V-2: compiled NativeLinuxOnly method coverage and canonical policy hash succeed; full NativeLinuxPolicyWitnessTests, TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests and SlowTestTripwireTests.
- V-3: every full C1677RetentionServerTests case remains green, including both newly covered methods through the real retention transport.
### Guards the regression
- R-1: C479_R06_LowNeverDiscoversAndTierRowsStayLow fails on the original bundle phrase; reproduce at exact base and rerun green.
- R-2: Policy_rows_require_pinned_attribute and Markers_do_not_expand_policy_exclusions fail on both missing entries; reproduce at exact base and rerun green.
### Guard inventory
- G-1: mandatory binding after Code instruction | PC-1.
- G-2: Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay exact method coverage | PC-2.
- G-3: Ignored_nested_repository_secret_survives_real_transport exact method coverage | PC-3.
### Positive controls
- PC-1: shorten the restored phrase to the original need binding wording; exact MechanicalPcContractTests.C479_R06_LowNeverDiscoversAndTierRowsStayLow must fail its composed instruction assertion.
- PC-2: remove only Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay and reseal policyHash; exact NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute must fail reverse-coverage naming that method.
- PC-3: remove only Ignored_nested_repository_secret_survives_real_transport and reseal policyHash; exact NativeLinuxPolicyWitnessTests.Markers_do_not_expand_policy_exclusions must fail reverse-coverage naming that method.
All three remain pending for method-scoped SourceLanding Mutation; Code does not execute deliberate mutants. Guards=3, mapped=3, missing=0, duplicate maps=0.
### Out of scope
The brief explicitly exempts the whole Unit lane for this bounded test/policy repair. The closed selection below is its named full affected classes, with the three repaired methods verified individually in the full-class TRX. No delivery, landing, leases, persistence or Windows execution changes. The earlier 35-minute whole-Unit timeout stays unresolved and is not credited as passed. Product changes are forbidden.
### Checkpoints
| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-three-repair/ | full-contract-and-guards | /*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/* | V-1, V-2, R-1, R-2 | all listed, 0 failed | 51 | 8 |
| CP-2 | S1 | CP-1 | full-retention-class | /*/*/C1677RetentionServerTests*/* | V-3 | all listed, 0 failed | 28 | 8 |
### Cost
Ordinary V/R floor=16 estimated minutes including the isolated build. Expected base witness=8 minutes plus checkpoint-tool bootstrap=2 minutes. Admission adds approximately 5-10 minutes per independent driver under current load and is reported separately. The base has one isolated build and one three-method run; the fix has one isolated build reused across two rows, avoiding one repeated build and one redundant rerun of the repaired methods. Run serially to preserve assembly-local process and database isolation. Row timeout=24m (initial fixed bound), execution allowance=42m. Per-test TRX durations, build/slot timing and host wall time are retained; unavailable fixture phase attribution stays unknown. No repeated green proof required. Mutation floor estimated 6 minutes for three method-scoped red/restore/green cycles plus admission/build time; all pending.
