# CARD-1836 S6a dirty launch order verification

Owner: task 51136c73. Base 9bcd61113f631ec58f158ff3fb637736fb786fd4.
Work starts at ef331572957c36548dffed10a6f68a026268193f. Shadow only.
Implements activation-plan S6a / D-5 / D-6 / V-10 and regression R-3.
Owned files: scripts/lib/rc-dirty-order.ps1, scripts/test-rc-dirty-order.ps1,
tests/Antiphon.Tests/Scripts/RcDirtyLaunchOrderTests.cs. This table is the
closed checkpoint list for the slice.

Launch set L is dirty union sample. Carried unsampled rows are omitted.
Order bands are failed, interrupted, not-run, changed, then clean sample.
Sample membership stays in L. Within a band, keys sort ordinal.
Discharge uses the CARD-1823 containment rule: a later green discharges only
when its source contains a fix the failing source did not contain. A same-tip
retry, a new input digest, or a caller FixProven flag does not discharge.
Result constants stay gateCredit false, effectiveMode Full, trustTransition
false, shadow true, activation off. No caller is added to nightly-run.ps1,
nightly-tests.ps1, or release-cut.ps1. Publish and promote stay untouched.
Full seed ordering remains Resolve-NightlyStoppedSeedOrder in
scripts/lib/nightly-stopped-run-seed.ps1. This slice does not call it.

The predicate stays in scripts/lib/rc-dirty-order.ps1. The nightly-live-lane
impact glob is scripts/lib/Antiphon.Nightly/**, which would select
LiveLaneRunnerTests and NightlyCoreTests. Those are another slice's long
regressions. Antiphon.Nightly was not extended.

Red first, library file absent, so the harness uses the Full-launch adapter
(input order, every kept row band failed, same-tip or FixProven drops that
key, duplicate keys are accepted). Script preflight is an explained unlisted
driver: pwsh only, no dotnet, no build. Cases 1 and 2 ran at loadavg 23.08.
Case 3 was rerun at loadavg 15.82 after the fixture stopped using
git cherry-pick (exit 129) and wrote the trailer with git commit -m.
Observed FAIL details, each an outcome assertion:

- OnlyDirtyAndSampleLaunch: only-dirty-and-sample-launch; accepted:duplicate-obligation
- OpenFailurePrecedesChanged: open-failure-precedes-changed; band-names
- SameTipPassCannotClearFailure: same-tip-pass-stays-failed; fix-proven-stays-failed; digest-only-stays-failed; later-contained-fix-leaves-failed; cherry-pick-fix-leaves-failed

Off-switch notes passed on the adapter. later-unrelated-green-stays-failed,
fix-present-at-failure-stays-failed, and discharged-still-has-no-gate-credit
also passed on the adapter, so a function that never discharges cannot go green.

CP-10 EstimatedMinutes is 15 so the 3x row deadline stays at the 45-minute
advisory. The activation plan's 8 is the provisional plan figure, not this row.
Min values are floors. CP-90 is the activation-plan guard union plus
ProcessSpawnLimitTests. It does not include WindowsUnitPlatformAdmissionTests.
Do not run parent-plan CP-17, CP-18, or CP-19 on Linux.

spawn-limit-rosters matches tests/Antiphon.Tests/** plus the literal
ParallelLimiter<. RcDirtyLaunchOrderTests declares that limiter, and
ProcessSpawnLimitTests exists in three projects. CP-90 covers the
Antiphon.Tests census. CP-91 and CP-92 are separate checkpoint invocations
(--rows CP-91, then --rows CP-92) so they are not in the Antiphon.Tests run.
CP-10, CP-90, and CP-93 share one serial invocation. Re-run
scripts/impacted-classes.ps1 -Range <base>..HEAD before that run and amend
this table if the selector disagrees.

CP-93 covers DerivedImpactIndexTests after the index regeneration. Build the
derived-impact driver first, then regenerate twice; the second run must print
unchanged. If tests/derived-impact-index.json changes, that file is its own
last commit before the checkpoint run. No migrations. Restart none.
Native Windows behavior is UNOBSERVED. This slice changes no OS-selected
production branch, factory, or startup path. Positive controls stay pending
for SourceLanding Mutation. No green repetitions. Budgets below are provisional.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-10 | S6a | `tests/Antiphon.Tests -> bin-c1836-s6a/` | linux-dirty-order | `/*/*/(RcDirtyLaunchOrderTests*)\|(RcFailureHistoryTests*)/*` | V-10 R-3 | all listed, 0 failed/skipped | 3 | 15 |
| CP-90 | S6a | CP-10 | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |
| CP-93 | S6a | CP-10 | index | `/*/*/DerivedImpactIndexTests/*` | derived guard | all methods, 0 failed/skipped | 27 | 5 |
| CP-91 | S6a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s6a-pty/` | pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn census | both methods, 0 failed/skipped | 2 | 1 |
| CP-92 | S6a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s6a-runner/` | runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn census | both methods, 0 failed/skipped | 2 | 2 |
