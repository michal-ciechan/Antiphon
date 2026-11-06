# Scratch Git native policy registration repair

Original Code and landing owner: 11212cb7 (full task GUID from the session).
Base: 3b7d80c66c28e8cae7a5c1b762d066dee6f3c07b. Final verification.

## Implementation

Register only C0824TransportGitTests.Scratch_git_deadline_includes_inherited_output_drains
in profiles.rc.exclusions, suite antiphon, platform windows, owner CARD-0824.
Keep its portable siblings required. The new object sits immediately after
C0824WorkspaceSlotProcessScanTests and before C1677RetentionEngineTests; the pending
07ab1e49 coverage branch appends its new objects near the end of exclusions.
Refresh policyHash and the live-lane PinnedInputDigest together. Both hash lines
will need recalculation when that branch lands; do not resolve their conflict by
choosing one branch's literal. No guard, marker, assertion or timeout changes.

## Performance budget

The brief requires one native incremental compiler preflight, then checkpoint
cold builds. The native build at base took 193 seconds plus 0 seconds slot wait;
base native witness took 12.721 seconds, three results, two intended failures.
These additional diagnostic commands establish the exact-base RED and compiler
health; they are explicitly accounted outside the closed final selection.
The tool is already built by the native project graph, so no bootstrap build is
needed. UseAppHost=false is retained by the checkpoint Linux build confinement;
TEMP=/tmp for every driver. One lease per build/run; serial rows, no repetitions.

The estimates below are provisional, not measured p95. Preserve build, slot and
row timing separately and TRX per-test duration; unavailable fixture phase timing
remains unknown. Existing whole-Unit evidence exceeded 30 minutes (CARD-1720),
so the caller owes Debug for its performance; an incomplete run earns no Full
credit. Unit is the required broad invariant sweep across all Unit-tagged tests;
its classes cannot be bounded without omitting the requested whole lane.
No full-assembly selection. Do not increase timeouts to hide a failure. Tool
derived row and execution deadlines remain unchanged. At most three rows per
invocation; CP-4 is a separate invocation after CP-1..CP-3.

## OS and startup audit

Policy/test-only registration for an existing native test. The real test runs
C0824SlotTransportWorld.RunGit with a Linux shell alias and sleep child retaining
redirected pipes, and asserts a TimeoutException within the existing operation
deadline. No OS-selected production behavior or startup code changes. Existing
NativeLinuxOnly marker, baseline and coverage implementation are unchanged.
Restart: none. Runtime defaults and runner catalogue were read before execution;
all work runs on the assigned Linux mirror, with no desktop run.

## Verification design

V-1: NativeLinuxPolicyWitnessTests enumerates every uncovered/reverse-uncovered
compiled method; ReleaseGatePlatformTests.LinuxOnlyWindowsExclusionPolicy also
enumerates shipped source markers and policy across suites. The full classification,
registry merge and tripwire classes remain required.
V-2: the full native scratch Git class, mechanical instruction contract,
TaskPlatformGuidanceTests and LiveLaneRunnerTests preserve real deadline behavior,
instruction composition and the live-lane input census pin.
V-3: every test class mentioning policyHash is covered: RcFailureHistoryTests,
ReleaseGatePublicationAuthorityTests, ReleaseGateActivationTests,
InterimVerificationReadinessTests and ReleaseGateReportDeliveryTests.
NativeLinuxPolicyWitness is a helper covered by V-1; C545World/C545Jobs are helpers,
not test classes. No C# class mentions PinnedInputDigest; LiveLaneRunnerTests invokes
its script. ReleaseGatePolicyTests covers missing expanded UIDs and zero-result
admission. RcCoverageAdmissionTests does not exist at this task base; it exists
only on the pending 07ab1e49 branch and is not imported into this repair.
V-4: whole Unit lane including method-level tags; Unit omits delivery, landing,
leases and persistence, which are unaffected by this policy-only repair.
R-1: exact-base NativeLinuxPolicyWitnessTests reproduced 1 pass / 2 reverse-coverage
failures naming only this method; final CP-1 must make both guarded assertions green.
R-2: manually recompute Get-NightlyPolicyHash and -PrintInputDigest at final HEAD;
assert equality with both shipped literals. Run scripts/test-mechanical-pc-contract.ps1
under its own build-slot lease (script harness assertions, not TUnit counts).
R-3: full base..HEAD check-evidence-diff, git diff --check, fetched-master merge-tree.

### Guard and positive-control inventory

G-1: exact method registration | PC-1: remove the new exclusion object, reseal both
digests, and run only NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute;
require reverse-coverage naming the scratch Git method, restore, fresh build, green.
One PC, one variant, pending method-scoped SourceLanding Mutation. Code performs
no deliberate mutant. Mutation also owns missing-control discovery. No ordinary
V/R is deferred to Final; this is the Final selection.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | repair | tests/Antiphon.Tests -> bin-11212cb7-policy/ | policy-and-contract | /*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(ReleaseGatePlatformTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(LiveLaneRunnerTests*)\|(C0824TransportGitTests*)/* | V-1,V-2,R-1 | all listed, 0 failed | 45 | 6 | true | TEMP=/tmp |
| CP-2 | repair | CP-1 | policy-consumers | /*/*/(RcFailureHistoryTests*)\|(ReleaseGatePublicationAuthorityTests*)\|(ReleaseGateActivationTests*)\|(InterimVerificationReadinessTests*)\|(ReleaseGatePolicyTests*)/* | V-3 | all listed, 0 failed | 20 | 6 | true | TEMP=/tmp |
| CP-3 | repair | CP-1 | whole-unit | /*/*/*/*[Category=Unit] | V-4 | whole Unit lane, disclose inherited red/skips | 1000 | 15 | true | TEMP=/tmp |
| CP-4 | repair | tests/Antiphon.E2E -> bin-11212cb7-report/ | report-policy-consumer | /*/*/ReleaseGateReportDeliveryTests*/* | V-3 | both isolated real-Program methods, 0 failed | 2 | 5 | true | TEMP=/tmp |
