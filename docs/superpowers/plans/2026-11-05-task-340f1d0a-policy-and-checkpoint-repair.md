# Complete native retention policy coverage and split consumer verification

Code/landing owner: 340f1d0a-ca9c-4a81-b2af-aaea56e9ade0.
Branch: feat/card-task-340f1d0a. Task base/red SHA:
88ed031df17ad9559d830e7cf120faaaddf05b27 (preceding Code owner 29e42131).
This is a test/policy-only Final repair; no production change is authorized.

## Invariant and implementation

Every NativeLinuxOnly marker must have exactly matching Windows rc policy coverage;
Linux must still require the marked tests, and portable siblings must remain required
on both platforms. Add only the missing method-scoped CARD-1718 exclusion for
C1677RetentionEngineTests.Doubt_inside_collapsed_ignored_repository_keeps_the_tree.
Its four existing arguments use real Unix permissions, symlinks and control-character
filenames inside a scratch nested repository. Keep the marker, guard and assertions.

The full source-marker census at the exact base reports 75 marker keys and exactly
one uncovered method, the method above (5 passing / 1 failing assertion rows).
Inspect the CARD-1718 RunnerWorkspaceIgnoredContentFloorTests, retention-engine,
server-retention, nested cleanup/recovery tests and other RunnerWorkspace classes;
the global census, rather than a second hand-maintained list, checks every marker.

Refresh the coupled executable expectations together, using their existing readers:

- policyHash: 069d68cae5af52e9769ce11bdc27426a8f1910f63803f2d8bc183a0c8fe92d32
  -> 0686a97cccbec3d421b2298b47ff64aaa63d923d654252e2ea44bd3d1532487b.
- PinnedInputDigest: 8b78c9f53455a71a0408e40845976d5240523e2b177867ed102478bd80c82edd
  -> f5151a2a95ed14a8de0a50f9a475d0d45853949ce61c1ed83304c04178bfd73f.

S1 is the single coupled repair, this verification design and its checkpoint table.
No test implementation changes are necessary: the committed census is red at base.

## Verification design

- V-1: Full LiveLaneRunnerTests: real five-input census, paid-wrapper admission,
  LF/CRLF equivalence, changed-policy/reader and committed-input refusal.
- V-2: Full ReleaseGatePolicyTests, ReleaseGatePlatformTests, NightlyScriptsTests
  and NativeLinuxPolicyWitnessMutationTests. The latter are existing ordinary
  synthetic invalid-input fixtures, not a SourceLanding Mutation cycle.
- V-3: Full MechanicalPcContractTests, TaskPlatformGuidanceTests and sibling
  RunnerDefaultGuidanceTests, NativeLinuxPolicyWitnessTests, TestClassificationPolicy*,
  TestClassificationGuard*, SlowTestRegistryMerge* and SlowTestTripwire*.
  Inspect the four formerly red witnesses by name: C479_R06_LowNeverDiscoversAndTierRowsStayLow,
  Policy_rows_require_pinned_attribute, Markers_do_not_expand_policy_exclusions,
  and LinuxOnlyWindowsExclusionPolicy (which must cover the C1677 method).
- V-4: Every test class mentioning policyHash or PinnedInputDigest: full
  RcFailureHistoryTests, ReleaseGatePublicationAuthorityTests, ReleaseGateActivationTests,
  InterimVerificationReadinessTests and ReleaseGateReportDeliveryTests, plus the
  V-1/V-2 reader fixtures. NativeLinuxPolicyWitness and C545World are support code,
  not separately executable test classes. Search scripts, tests, docs, server and
  gate/authority libraries for stale executable digests.
- R-1: Red-first, exact-base LinuxOnlyWindowsExclusionPolicy script sweep; green
  after the coupled repair, including the entire source-marker census.
- R-2: Full ReleaseGatePolicyTests.C599_ProfileSchema and
  LiveLaneRunnerTests.WrapperOnlyPaidManifest preserve both hash checks.
- R-3: Whole Antiphon.Tests Unit lane, six disjoint class-initial partitions.
  Filters apply Category=Unit to methods as well as classes, including Unit methods
  on Integration classes. No full assembly is selected. The invariant is repository
  instruction/classification policy; Unit omits delivery, landing, leases and persistence.
- M-1: At final tree, PrintInputDigest matches the pin, Get-NightlyPolicyHash and
  Read-NightlyExecutionPolicy match policyHash; inspect full missing-method list;
  evidence-diff over base..HEAD, OS witness declaration/lint and fresh master merge-tree.

### Positive controls pending for SourceLanding Mutation

- PC-1/missing-C1677-row: remove only the new exclusion, recompute policyHash so
  the sweep reaches the decisive marker/row assertion; exact
  ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy must fail
  marker-without-row for the C1677 method, then restore and run that method green.
- PC-2/old-policy-hash: replace only policyHash with the base value; exact
  ReleaseGatePolicyTests.C599_ProfileSchema must fail shipped-policy validation.
- PC-3/old-census-pin: replace only PinnedInputDigest with its base value; exact
  LiveLaneRunnerTests.WrapperOnlyPaidManifest must fail full-inputs and crlf-inputs.

Mutation owns all deliberate mutants, red/restore/green and missing-control discovery.
The brief's remove-entry mutant is recorded here for that stage, not discharged by Code.
All earlier controls remain pending: 29e42131 PC-1/old-census-pin, PC-2/old-policy-hash;
9f03d97e PC-1/TestDesign phrase, PC-2/ignored secrets and lost-reply replay,
PC-3/nested repository secret. Unbound candidate: docs/superpowers/mutations/.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1677-repair/ | instruction-and-guards | /*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/* | V-3 | all listed, 0 failed | 66 | 8 | true | TEMP=/tmp |
| CP-2 | S1 | CP-1 | live-and-nightly-policy | /*/*/(LiveLaneRunnerTests*)\|(ReleaseGatePolicyTests*)\|(ReleaseGatePlatformTests*)\|(NightlyScriptsTests*)/* | V-1,V-2,R-1,R-2 | all listed, 0 failed | 21 | 5 | true | TEMP=/tmp |
| CP-3 | S1 | CP-1 | hash-inspector-fixtures | /*/*/NativeLinuxPolicyWitnessMutationTests*/* | V-2 | all listed, 0 failed | 12 | 3 | true | TEMP=/tmp |
| CP-4 | S1 | CP-1 | policy-hash-readers | /*/*/(RcFailureHistoryTests*)\|(ReleaseGateActivationTests*)\|(InterimVerificationReadinessTests*)/* | V-4 | all listed, 0 failed | 22 | 6 | true | TEMP=/tmp |
| CP-5 | S1 | tests/Antiphon.E2E -> bin-c1677-delivery/ | report-delivery | /*/*/ReleaseGateReportDeliveryTests*/* | V-4 | all listed, 0 failed | 2 | 8 | true | TEMP=/tmp |
| CP-6 | S1 | CP-1 | unit-ad | /*/*/(A*)\|(B*)\|(C*)\|(D*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 15 | true | TEMP=/tmp |
| CP-7 | S1 | CP-1 | unit-eh | /*/*/(E*)\|(F*)\|(G*)\|(H*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 5 | true | TEMP=/tmp |
| CP-8 | S1 | CP-1 | unit-il | /*/*/(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 5 | true | TEMP=/tmp |
| CP-9 | S1 | CP-1 | unit-mp | /*/*/(M*)\|(N*)\|(O*)\|(P*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 10 | true | TEMP=/tmp |
| CP-10 | S1 | CP-1 | unit-qt | /*/*/(Q*)\|(R*)\|(S*)\|(T*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 15 | true | TEMP=/tmp |
| CP-11 | S1 | CP-1 | unit-uz | /*/*/(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 5 | true | TEMP=/tmp |
| CP-12 | S1 | CP-1 | publication-policy | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PinnedPolicy | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-13 | S1 | CP-1 | publication-schema | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PinnedPolicySchema | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-14 | S1 | CP-1 | publication-fields | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PinnedPolicyFields | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-15 | S1 | CP-1 | publication-identity | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PinnedPolicyIdentityFields | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-16 | S1 | CP-1 | publication-blob | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PinnedPolicyBlobFields | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-17 | S1 | CP-1 | publication-suites | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PinnedPolicySuiteFields | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-18 | S1 | CP-1 | publication-bindings | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PinnedPolicyBindings | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-19 | S1 | CP-1 | publication-ledger | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedger | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-20 | S1 | CP-1 | publication-chunks | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedgerChunks | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-21 | S1 | CP-1 | publication-counts | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedgerCounts | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-22 | S1 | CP-1 | publication-joins | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedgerJoins | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-23 | S1 | CP-1 | publication-markers | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedgerMarkers | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-24 | S1 | CP-1 | publication-receipt | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedgerReceipt | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-25 | S1 | CP-1 | publication-readback | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_ReceiptReadback | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |
| CP-26 | S1 | CP-1 | publication-test-mode | /*/*/ReleaseGatePublicationAuthorityTests*/C599A_PublisherTestMode | V-4 | all listed, 0 failed | 1 | 2 | true | TEMP=/tmp |

## Performance budget

Prior CP-4 (run 20261105-192619-1dbe, SHA 88ed031) acquired its slot immediately,
ran 1080.7 seconds, hit the 18-minute row deadline and wrote no TRX. Console output
only identifies TUnit startup; the preceding owner's live process observation names
C599A_PinnedPolicySuiteFields. Exact per-method elapsed time is unknown. It is an
execution timeout, not admission wait. Its selection included 15 publication scripts
behind ProcessSpawnLimit plus 22 readiness/history cases. Each publication script
creates one real-git fixture and exercises a many-variant authority/ledger matrix.
The old six-minute combined estimate is disproven by the partial 18-minute run.

Split the old CP-4 into CP-4 (the three remaining complete classes) and CP-12..CP-26
(one existing publication method per row). Their union retains the full four classes.
No script/test deadline changes; each new publication row derives the existing
15-minute floor, below the former 18-minute ceiling. Two-minute method estimates
are provisional, not measured p95. Retain fresh per-method TRX and measure once;
CARD-1404 owns measured cost packing. No repetitions after green.

Keep all rows serial, one isolated Antiphon.Tests build and one E2E build, with no
Agents.Pty co-scheduling. Unit partitions retain method-level categories and the
original estimates/deadlines. Their total 55-minute estimate remains provisional;
CARD-1720 owns the demonstrated whole-Unit performance issue. Unit lane shape is
independent of the old CP-4's Integration-only selection. Full selected estimates
are 115 minutes; derive the finite execution allowance using the importer (240m),
with separately bounded 45m admission per acquisition. The 75-minute task budget
may require an honest receipt-gap handoff; it never changes a pending row to green.

The full checkpoint batch is estimated above 30 minutes: retain run/source/host/load
and exact membership for a caller-commissioned Debug performance task; this delegate
must not sub-delegate. Existing CARD-1720 and CARD-1404 are related, not proof that
the CP-4 issue is discharged. Expensive fixtures retain their existing ownership:
one real-git publication world per script invocation, required for that case's frozen
identity/snapshot variants; no setup or assertion is removed. No product-wide
unbounded integration class or full assembly is selected.

Record slot waits separately from build/test execution. Fresh TRX retains membership
and per-test durations; checkpoint state retains build/run budgets. Fixture/container,
external-call and teardown phases remain unknown where instrumentation emits none.
All drivers use TEMP=/tmp and UseAppHost=false, with one host lease per build/run.
Explicitly unlisted runs: the required red-first source census, direct hash inspection,
and one native-default-output checkpoint-tool bootstrap build. No extra test build.
Commit/push S1 before the checkpoint run and freeze source until it completes/stops.

No OS-selected production behavior/helper/startup changes. Inspect the real marker,
source census and policy reader; this policy registers an existing native-only test,
not a newly implemented OS branch. Native Windows work: none for this delta.
Restart: none. No rc activation/deployment is authorized. Full evidence:
.antiphon/task-340f1d0a.md. After complete ordinary V/R work: initial Review, then
caller lands the original Code task and commissions method-scoped SourceLanding Mutation.
