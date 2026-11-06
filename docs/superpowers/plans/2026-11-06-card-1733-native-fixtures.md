# CARD-1733 native allocator fixtures

Code owner 7e4799eb-145c-42e5-815a-265434ba5b7e; base f8455526033cd36c152338197ea07ea5d4f7f412.
Debug 803291f3c: containment accounts for 193/226 seconds in two capped 300-second methods.
Test-side only. Preserve every G-1..G-70 assertion, allowlist, environment sanitization and 300-second cap.

S1 adds discriminating regression cases before repair. S2 changes containment to fresh native metadata,
shares an immutable fixture seed per identity shape in each harness process, privately copies mutable
recipients and rebinds paths/routes; C1339-only snapshot restoration reuses the CARD-1731 approach
without changing the still-incomplete ordinary obligations of that other task. Restore in finally;
restore failure terminates the case. Mark the allocator Slow plus Integration.

## Performance budget

Provisional estimates, CARD-1733, n=1 diagnostic censored observations, no measured p95.
Native initial compile 4 minutes; checkpoint isolated compile 4 minutes; two incident methods once
in one lease, expected <=150 seconds each; full allocator class 8 minutes; guards 2 minutes;
whole Unit lane 15 minutes. Existing importer deadlines remain unchanged exceptions, not measured p95.
Record seed, clone, restore, containment, teardown and actual method TRX durations separately.
Git process deadline remains 15 seconds, harness deadline 300 seconds. No sleep-based witness.
Collect comparable load/platform timing in the next 20 qualifications/20 days, review after five.
No full assembly: Unit invariant spans its category membership; delivery/landing/lease/persistence
are outside Unit and unchanged by this fixture-only work. Named integration scope is full
ReleaseTagAllocatorTests and C1733AllocatorFixtureTests. No other harness families change.

## Verification design

V-1: Native_metadata_avoids_provider_operations counts actual delegated provider commands: zero.
V-2: Boundaries_and_changed_links_are_checked_fresh checks missing descendants, sibling/root boundary,
changed-after-first-check, dangling links and ancestors above the root using real filesystem metadata.
V-3: Shared_seed_keeps_recipients_and_authority_private observes no repeated init/commit; modifies
one clone's tags/fault/store and reads the other recipient; freshly validates the other's authority.
V-4 also checks unchanged-file timestamps, same-size changed bytes, missing parents, absent-directory restoration and restore failure propagation.
V-4: Throwing_reservation_mutation_restores_the_control proves first validity, exact bytes after a
throwing mutation, and next-variant acceptance. Restore errors must propagate.
V-5: Measure both incident methods once in one lease, retain all assertion rows and unchanged cap.
R-1: Full allocator seven methods cover fresh/resumed/authority and all existing assertions.
R-2: Full mechanical, platform, classification, registry, tripwire and native policy guard classes.
R-3: Whole Unit lane. Required manual work: inspect private refs/journals/fault files/card routes,
transport allowlist/sanitization and no production changes; compare evidence diff and merge-tree.
No changed OS-selected production behavior or startup path; test metadata uses real IO.File attributes
and IO.Path parent operations. Linux native link witness executed; Windows native reparse witness
remains pending to its owner; no production bypass. Restart none.

All positive controls stay pending for SourceLanding Mutation. PC-1 reintroduce provider walk;
PC-2 remove link refusal; PC-3 cache success across path replacement; PC-4 reuse mutable seed refs;
PC-5 omit finally restore; PC-6 suppress restore failure. Exact methods bind after Review/landing.
See ../mutations/2026-11-06-card-1733-native-fixtures.md.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | tests/Antiphon.Tests -> bin-c1733-final/ | allocator-fixtures | `/*/*/(C1733AllocatorFixtureTests*)\|(ReleaseTagAllocatorTests*)/*` | V-1,V-2,V-3,V-4,V-5,R-1 | all listed, 0 failed | 12 | 14 | true | TEMP=/tmp;C1733_TIMING_ROOT=.antiphon/c1733/timing |
| CP-2 | S2 | CP-1 | guards | `/*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | R-2 | all listed, 0 failed | 66 | 2 | true | TEMP=/tmp |
| CP-3 | S2 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed | 1 | 15 | true | TEMP=/tmp |

Before CP-1, one native compile and regression red run are explicitly allowed to establish new-test
sensitivity at unchanged base behavior. They are diagnostic, not clean final receipts. Tool bootstrap
is separately gated. Run at most three manifest rows per invocation; never repeat a green row.

Manual inspection found absent-directory contamination after S2. At b8e05d044 the standalone
C1733_SnapshotWork run asserted 4 passes/1 failure at absent-directory-restored (6 s execution).
This unlisted, gated diagnostic establishes the new defect before repair; it is not a TUnit count.
The earlier checkpoint was canceled during admission with no build or tests started.
The same three required rows run at the committed repair SHA; no green row is repeated.
The link witness uses the existing New-C599AEvidenceLink factory (Windows junction, otherwise symlink).

The 93396a85b checkpoint ran all seven allocator methods green (Fresh 153.342s, Target 77.771s).
Its new SharedSeed test lost a script-scoped wrapper variable in the publisher scope; the repair
uses child-process-global references and preserves all three assertions. CP-1 must be rerun at
the repair SHA. CP-2 had the two inherited C0824 reverse-coverage failures; CP-3 was stopped under
the task budget with additional unresolved dialog/secret-floor failures and is NOT a completed
Unit lane. Rerun CP-1/CP-2/CP-3 after the excluded policy repair is present; preserve historical red.
A gated standalone SharedSeed diagnostic may verify the wrapper repair within the remaining task
budget; it cannot replace CP-1. No additional checkpoint build is started without time to await it.
Fresh is slightly above the Debug approximate 150s target; retain 300s and assess qualification
samples/partitioning, never widen the cap. All PCs remain pending for SourceLanding Mutation.

## Finish commission d463ea3b

The finish brief explicitly defers CP-3/R-3 under CARD-1720; the interrupted historical
Unit attempt remains incomplete. This commission runs CP-1 and CP-2 once at its pushed
tip, preserving the two expected inherited native-policy failures rather than changing
the excluded policy inputs. Required Unit completion and unresolved Unit findings stay
with the caller's scoped qualification work; no Full ordinary gate is claimed here.

The permitted native compile at 0bfaf02006f98c20bd82019950271242972bfe4b passed
(0 errors). The exact Shared_seed_keeps_recipients_and_authority_private diagnostic
then passed 1/1 with its three required assertion rows in 12.999 seconds. Separate
build/test leases each waited 75 seconds. This verifies the pushed wrapper repair
against the prior CP-1 failure, which reached only two rows before losing the
script-scoped launcher. CP-1 remains required after this diagnostic; all assertions,
the 300-second execution cap and the six pending Mutation controls remain unchanged.
