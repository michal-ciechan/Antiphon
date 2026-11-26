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

Fix round, task 557c848e. D-1 stores obligation state in an Ordinal
Dictionary[string,object]. D-2 folds Carry rows with the other obligations and
throws carry-with-open-failure when one ends failed or interrupted. A clean
Carry, and a Carry discharged by containment, stays omitted. A-1 witnesses
that an unknown fix SHA and an unknown green SHA each throw
repo-sha-unresolved and do not discharge. Shadow constants stay gateCredit
false, effectiveMode Full, trustTransition false, shadow true, activation off.
No production caller.

Case-folding audit of scripts/lib/rc-dirty-order.ps1: `$states` was the only
obligation-key hashtable and is now Ordinal. `$cache` stays `@{}` because each
key is a pair of SHAs that already matched `^[0-9a-f]{40}$` under -cnotmatch.
Per-key state values stay hashtables of fixed literal property names. `$allowed`
and `$seen` were already Ordinal HashSets. There is no Group-Object,
Sort-Object, or -contains. `-eq` compares sequence and rank numbers only.
Outcome and disposition strings use -ceq/-cne. Launch and omit sorts use
StringComparer.Ordinal. Get-RcDirtyProp's Hashtable lookup is case-insensitive
for property names only (key, disposition, outcome, sourceSha, sequence,
results, fixSha) and does not store obligation keys.

Red at a6dc9ac664ace623bdfa5ea1d9a0373b30a513d0, witnesses only, library still
the previous behavior. Unlisted pwsh through build-slot, loadavg 12.34,
waited=0s. FAIL details:

- CaseDistinctKeysStaySeparate: case-distinct-launch:Suite/fooTests:failed:Sample; case-distinct-fix:Suite/fooTests:changed:Dirty
- CarryWithOpenFailureRefuses: accepted:carry-with-open-failure; accepted:carry-with-open-failure; accepted:carry-with-open-failure
- UnknownShaRefuses passed. The throw was already present; the new witness is what an exit-128-as-contained mutant did not have.

The library fix is ce442024e02be1172fac3c628a046fcc68a9ebf4. The same six
harness cases then passed. Unlisted pwsh, loadavg 12.02, waited=0s.

CP-10 Min is 8: six RcDirtyLaunchOrderTests methods plus the two
RcFailureHistoryTests methods. CP-10 EstimatedMinutes stays 15 so the 3x row
deadline stays at the 45-minute advisory. The activation plan's 8 is the
provisional plan figure, not this row. Min values are floors. CP-90 is the
activation-plan guard union plus ProcessSpawnLimitTests. It does not include
WindowsUnitPlatformAdmissionTests. Do not run parent-plan CP-17, CP-18, or
CP-19 on Linux.

spawn-limit-rosters matches tests/Antiphon.Tests/** plus the literal
ParallelLimiter<. RcDirtyLaunchOrderTests declares that limiter, and
ProcessSpawnLimitTests exists in three projects. CP-90 covers the
Antiphon.Tests census. CP-91 and CP-92 are separate checkpoint invocations
(--rows CP-91, then --rows CP-92) so they are not in the Antiphon.Tests run.
CP-10, CP-90, CP-93, and CP-94 share one serial invocation. Re-run
scripts/impacted-classes.ps1 -Range 9bcd61113f631ec58f158ff3fb637736fb786fd4..HEAD
before that run and amend this table if the selector disagrees. At
ce442024e02be1172fac3c628a046fcc68a9ebf4 the selector kinds were
spawn-limit-rosters and test-lane-category. Its Antiphon.Tests filter adds
ReleaseImpactEnvelopeTests because the tests/derived-impact-index.json edge
names that class. CP-94 is that row. RcFailureHistoryTests stays on CP-10 as
the R-3 neighbour.

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
| CP-10 | S6a | `tests/Antiphon.Tests -> bin-c1836-s6a/` | linux-dirty-order | `/*/*/(RcDirtyLaunchOrderTests*)\|(RcFailureHistoryTests*)/*` | V-10 R-3 D-1 D-2 A-1 | all listed, 0 failed/skipped | 8 | 15 |
| CP-90 | S6a | CP-10 | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |
| CP-93 | S6a | CP-10 | index | `/*/*/(DerivedImpactIndexTests*)\|(DerivedImpactIndexInvariantTests*)/*` | derived guard | all methods, 0 failed/skipped | 27 | 5 |
| CP-94 | S6a | CP-10 | release-impact | `/*/*/ReleaseImpactEnvelopeTests/*` | impacted index edge | all listed, 0 failed/skipped | 8 | 4 |
| CP-91 | S6a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s6a-pty/` | pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn census | both methods, 0 failed/skipped | 2 | 1 |
| CP-92 | S6a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s6a-runner/` | runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn census | both methods, 0 failed/skipped | 2 | 2 |
