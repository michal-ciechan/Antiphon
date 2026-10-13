# CARD-1032 S5 verification — Code owner a7f248c0

Base: 99910a78c7ec4295786ca94923a040013c689fcd.
S5 implements a fixture-composed runner executor. S6 dispatcher, S7 admission,
S8 queue/recovery delivery, native qualification and activation remain outside S5.
No production composition is enabled. No delivery claim is made by S5 tests.

CP-7..10 retain the 23 named methods. Wildcards in each OR operand make those
methods reachable in MTP. CP-8..10 reuse CP-7's isolated runner build at the
same committed SHA. Estimated cost: one build and four minutes of scratch
Git/runtime tests, excluding slot wait. No full assembly run is selected.

Each method comment names its case, mutation and decisive red oracle.
Before implementation all 23 must fail at missing destructive API reachability.
That baseline does not discharge any SourceLanding Mutation control.

V-3: identity, content and first/final proof guards (CP-8/9).
V-4: retained owners, runtime start/adoption and intent (CP-7).
V-5 and R-3: named physical removal and replay witnesses (CP-7..10).
V-1/2/6 and R-1/2 belong to prior/later slices and are carried forward.
Caller decision 2026-10-13: V-3 bound-rebase positive and PC-60 positive
fixture variant move explicitly to S7 CP-13 C1032_AdmissionBoundRebaseProof.
S5 pins Rebased refusal and retains direct-containment positives. Process-death
recovery, evidence disposal and richer vectors remain S5 acceptance obligations.

Caller final refinement aa033f53-c7b7-4c3d-a7fe-7d7425827b8b reaffirms this closed
CP-7..10 selection as S5 acceptance and stops additional Unit/whole-project runs.
The recorded Unit results remain disclosed, not newly passed or a S5 blocker.
All 23 methods are green at e5d8efc179f9a0ed083f534396606f36e10e216f; documentation-only
scope/evidence amendments require no new unchanged proof repetition after green.
Next: Review, with every PC/variant still pending for SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-7 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-owners/` | removal-owners | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)\|(C1017_RunnerStartFence*)\|(C1017_RunnerReadoptionFence*)\|(C1017_RunnerIntentBeforeIo*)\|(C1017_UnknownProcessOwnershipHolds*)` | V-4, V-5, R-3 | all 5 listed; 0 failed/skipped | 5 | 5 | true |
| CP-8 | S5 | CP-7 | removal-identity | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)\|(C1017_RunnerAttemptIdentity*)\|(C1017_RunnerStoreIdentity*)\|(C1017_RunnerRepositoryIdentity*)\|(C1017_RunnerPathIdentity*)\|(C1017_RunnerBranchIdentity*)\|(C1017_RunnerHeadIdentity*)\|(C1017_RunnerPreservationProof*)\|(C1017_RunnerSourceLandingExcluded*)` | V-3, V-5, R-3 | all 9 listed; 0 failed/skipped | 9 | 5 | true |
| CP-9 | S5 | CP-7 | removal-content | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)\|(C1017_RunnerIgnoredPolicy*)\|(C1017_RunnerFreshFinalInspection*)\|(C1017_RunnerNoForceBypass*)` | V-3, V-5, R-3 | all 4 listed; 0 failed/skipped | 4 | 5 | true |
| CP-10 | S5 | CP-7 | removal-components | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)\|(C1017_RunnerDigestReplay*)\|(C1032_Removal*)` | V-5, R-3 | all 9 listed; Linux 0 failed/skipped | 9 | 6 | true |

| CP-28 | S5 | CP-7 | affected-full-classes | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(LegacyWorkspaceRemovalGateTests*)\|(RunnerWorkspaceServiceTests*)/*` | V-3, V-4, V-5, R-1, R-2, R-3 | all 137 full-class results; Linux 0 failed/skipped | 137 | 4 | true |

S5 repair round 2 adds four exact methods selected by CP-10's C1032_Removal*:
C1032_RemovalLinuxIgnoredFifoRefuses, C1032_RemovalLateFileIdentityRefuses,
C1032_RemovalLateFileContentRefuses, C1032_RemovalLateRootIdentityRefuses.
CP-7/8/9/10/28 counts are 5/9/4/9/137 (93/11/33 in CP-28).
Linux FIFO skips explicitly on other OSs; Windows qualification uses exact
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileIdentityRefuses*`,
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileContentRefuses*`, and
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateRootIdentityRefuses*`.
Windows sharing/native semantics remain pending desktop Final Review.
No Unit or server class run; caller repair selection controls.
