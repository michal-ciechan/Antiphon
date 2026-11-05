# Repair the census pin after the native policy repair

Code/landing owner: 29e42131. Assigned branch: feat/card-task-29e42131.
Task base/red SHA: 19b9e69513551259b7c56eec03b77ef3a4cc861c, the tip of
the preceding repair owner 9f03d97e. No production change is authorized.

## Invariant and implementation

The offline paid-wrapper contract must admit the reviewed, newline-normalized
five-input census and reject a changed policy or reader, including committed
changes with a correctly recomputed policy hash. Update only PinnedInputDigest
in scripts/test-live-lane-run.ps1. Do not regenerate the policy, change its
canonicalization, remove an input, or weaken either census or admission checks.

The preceding repair changed tests/test-execution-policy.json's canonical hash
from 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4
to 069d68cae5af52e9769ce11bdc27426a8f1910f63803f2d8bc183a0c8fe92d32.
It already carries that correct new hash. The corresponding protected-input pin
changes from 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7
to 8b78c9f53455a71a0408e40845976d5240523e2b177867ed102478bd80c82edd.
Search scripts/, tests/, docs/, server/ and the release-authority/gate libraries
for both old hashes, PinnedInputDigest and policyHash. Historical prose is not an
executable expectation. The live-lane pin is the only stale executable literal.

## Verification design

- V-1: LiveLaneRunnerTests in full proves paid-wrapper admission, the real
  protected-input hash and census, LF/CRLF equivalence, changed-policy/reader
  refusal, and committed-input refusal. Inspect WrapperOnlyPaidManifest by name.
- V-2: Full ReleaseGatePolicyTests, ReleaseGatePlatformTests and NightlyScriptsTests
  cover the shipped nightly policy, canonical hash validation and policy scripts.
  Full NativeLinuxPolicyWitnessMutationTests covers the shared hash inspector's
  ordinary synthetic invalid-input cases; this is not a SourceLanding PC run.
- V-3: Full MechanicalPcContractTests, TaskPlatformGuidanceTests and its sibling
  classes, NativeLinuxPolicyWitnessTests, TestClassificationPolicy*,
  TestClassificationGuard*, SlowTestRegistryMerge* and SlowTestTripwire* preserve
  the three formerly red methods and the instruction/classification guards.
- V-4: Full classes with literal policyHash references: RcFailureHistoryTests,
  ReleaseGatePublicationAuthorityTests, ReleaseGateActivationTests,
  InterimVerificationReadinessTests and ReleaseGateReportDeliveryTests.
  Shared NativeLinuxPolicyWitness has no TUnit tests itself; its witness and
  mutation-fixture classes are covered above. C545World is a fixture, not a class
  with independent test methods. No literal old policy-hash expectation exists.
- R-1: At the exact base, run test-live-lane-run.ps1 -Case
  C1545_WrapperOnlyPaidManifest. Observed 19 passed/2 failed assertion rows:
  full-inputs and crlf-inputs. This existing committed witness guards the defect.
- R-2: At the exact base, run test-release-gate.ps1 -Case C599_ProfileSchema.
  Observed 9 passed/0 failed assertion rows, including stale-hash rejection.
  Policy tests remain green because the preceding repair recomputed policyHash;
  the live-lane hash includes the policy bytes and was not updated with it.
- R-3: Whole Antiphon.Tests Unit lane, partitioned by initial class letter into
  six disjoint selections. This is a repository instruction/policy invariant
  requirement; no product-wide impact is asserted. No full assembly is selected.
  The Unit lane omits delivery, landing, leases and persistence coverage.
- M-1: PrintInputDigest equals the new pin; Read-NightlyExecutionPolicy accepts
  the shipped hash; re-search for stale executable literals; evidence-diff over
  full task base..HEAD; merge-tree against freshly fetched origin/master.

No new test is needed: the existing base witness fails on the guarded defect.
No OS-selected production branch, helper or startup path changes. The changed
script is an offline test harness. Restart: none. Native Windows work: none for
this pin-only change. Inspect the real policy reader and paid-wrapper census,
not a forced platform seam. E2E uses the existing isolated runner and Program.

### Positive controls pending for SourceLanding Mutation

- PC-1/old-census-pin: restore the old PinnedInputDigest only; exact
  LiveLaneRunnerTests.WrapperOnlyPaidManifest must fail full-inputs and
  crlf-inputs, then restore and run that same method green.
- PC-2/old-policy-hash: restore only the old policyHash, without resealing;
  exact ReleaseGatePolicyTests.C599_ProfileSchema must fail shipped-policy
  validation, then restore and run that same method green.

Mutation owns both deliberate mutants, red/restore/green and missing-control
discovery. The prior repair's PC-1/PC-2/PC-3 also remain pending with owner
9f03d97e; this task discharges none of them. No repetitions after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-digest-repair/ | instruction-and-guards | /*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/* | V-3 | all listed, 0 failed | 51 | 8 | true | TEMP=/tmp |
| CP-2 | S1 | CP-1 | live-and-nightly-policy | /*/*/(LiveLaneRunnerTests*)\|(ReleaseGatePolicyTests*)\|(ReleaseGatePlatformTests*)\|(NightlyScriptsTests*)/* | V-1,V-2,R-1,R-2 | all listed, 0 failed | 21 | 5 | true | TEMP=/tmp |
| CP-3 | S1 | CP-1 | hash-inspector-fixtures | /*/*/NativeLinuxPolicyWitnessMutationTests*/* | V-2 | all listed, 0 failed | 1 | 3 | true | TEMP=/tmp |
| CP-4 | S1 | CP-1 | policy-hash-consumers | /*/*/(RcFailureHistoryTests*)\|(ReleaseGatePublicationAuthorityTests*)\|(ReleaseGateActivationTests*)\|(InterimVerificationReadinessTests*)/* | V-4 | all listed, 0 failed | 37 | 6 | true | TEMP=/tmp |
| CP-5 | S1 | tests/Antiphon.E2E -> bin-digest-delivery/ | report-delivery | /*/*/ReleaseGateReportDeliveryTests*/* | V-4 | all listed, 0 failed | 2 | 8 | true | TEMP=/tmp |
| CP-6 | S1 | CP-1 | unit-ad | /*/*/(A*)\|(B*)\|(C*)\|(D*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 15 | true | TEMP=/tmp |
| CP-7 | S1 | CP-1 | unit-eh | /*/*/(E*)\|(F*)\|(G*)\|(H*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 5 | true | TEMP=/tmp |
| CP-8 | S1 | CP-1 | unit-il | /*/*/(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 5 | true | TEMP=/tmp |
| CP-9 | S1 | CP-1 | unit-mp | /*/*/(M*)\|(N*)\|(O*)\|(P*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 10 | true | TEMP=/tmp |
| CP-10 | S1 | CP-1 | unit-qt | /*/*/(Q*)\|(R*)\|(S*)\|(T*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 15 | true | TEMP=/tmp |
| CP-11 | S1 | CP-1 | unit-uz | /*/*/(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit] | R-3 | >= 1 executed, 0 failed | 1 | 5 | true | TEMP=/tmp |

## Performance budget

All estimates are provisional, not measured p95. Guard estimates come from the
preceding repair plan; Unit shard estimates follow the CARD-1720 plan rather than
repeating the known whole-lane timeout. CARD-1720/earlier Unit timeout remains
the performance investigation; no timeout widening or green claim follows from
that history. Total selected estimates 85 minutes; the importer derives the
finite total execution allowance, with admission separately bounded at 45 minutes
per acquisition. The 60-minute task budget may leave exact verification pending.
Build/test driver deadlines use the existing checkpoint derivation unchanged.

One isolated Antiphon.Tests build is shared across ten exact rows; one E2E build
serves its full affected class. All rows are serial; Antiphon.Agents.Pty.Tests is
not co-scheduled. Baseline scripts each used one lease, waited=0s: paid-wrapper
13s held, policy schema 11s held. Tool bootstrap is an explicitly unlisted native
default-output build, leased once, expected 2 minutes. No direct test build is
needed. Source is committed before checkpoint execution and remains frozen.

TRX retains method membership and duration. Checkpoint receipts retain build,
execution and admission timing in .antiphon/checkpoints/. Unavailable container,
fixture and external-call phase attribution stays unknown; setup ownership is
the existing assembly/fixture implementation and is not altered here. A row over
30 minutes requires Debug follow-up, not a timeout change. Pending rows never
become passed through a scope amendment. Final evidence is .antiphon/task-29e42131.md.
