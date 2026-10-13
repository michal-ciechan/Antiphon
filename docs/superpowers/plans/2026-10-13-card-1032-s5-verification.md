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
Positive rebase and process-death recovery remain acceptance obligations;
unsupported proof and exception cuts do not satisfy those promises.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-7 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-owners/` | removal-owners | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)\|(C1017_RunnerStartFence*)\|(C1017_RunnerReadoptionFence*)\|(C1017_RunnerIntentBeforeIo*)\|(C1017_UnknownProcessOwnershipHolds*)` | V-4, V-5, R-3 | all 5 listed; 0 failed/skipped | 5 | 5 | true |
| CP-8 | S5 | CP-7 | removal-identity | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)\|(C1017_RunnerAttemptIdentity*)\|(C1017_RunnerStoreIdentity*)\|(C1017_RunnerRepositoryIdentity*)\|(C1017_RunnerPathIdentity*)\|(C1017_RunnerBranchIdentity*)\|(C1017_RunnerHeadIdentity*)\|(C1017_RunnerPreservationProof*)\|(C1017_RunnerSourceLandingExcluded*)` | V-3, V-5, R-3 | all 9 listed; 0 failed/skipped | 9 | 5 | true |
| CP-9 | S5 | CP-7 | removal-content | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)\|(C1017_RunnerIgnoredPolicy*)\|(C1017_RunnerFreshFinalInspection*)\|(C1017_RunnerNoForceBypass*)` | V-3, V-5, R-3 | all 4 listed; 0 failed/skipped | 4 | 5 | true |
| CP-10 | S5 | CP-7 | removal-components | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)\|(C1017_RunnerDigestReplay*)\|(C1032_Removal*)` | V-5, R-3 | all 5 listed; 0 failed/skipped | 5 | 6 | true |
