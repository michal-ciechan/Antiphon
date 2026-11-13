# CARD-1836 S4a: opt-in boundary packing

Original Code task/landing owner: `6c1c4d05-c107-433b-837f-f882784093ac`.
Base: `272ee17c0500be3de39097acd5de321dc51840c5`.
Selected S4a because S2's boundary API is on master, whereas S3a is not.
The [activation plan](2026-11-12-card-1836-incremental-activation-plan.md)
owns D-2/D-4/D-6 and V-8/R-2. S2 hardening remains separate.

Only packing functions in `nightly-tests-impl.ps1` change. A new explicit
`BoundaryAware` switch takes a suite and ordinal class-boundary map; missing
mapping is unassigned/always dirty. Partition leaves before the existing time
packer. Retain the full discovery universe for prefix safety across partitions.
Attach full method membership, separate complete discovered case membership,
full SHA-256 membership digest and membership-derived IDs. Reject duplicate
discovery identities, overlapping leaves, malformed/empty membership and ID
collisions. No caller opts in, no launch/coverage/publisher logic changes.

V-8 consists of `TwoBoundariesAndPrefixCollisionsStaySeparate`,
`RepackPreservesCaseUniverse`, and `DefaultFullPackKeepsIds`. Each calls the real
packer and checks concrete filters, membership or pinned legacy behavior. The
pre-fix adapter calls the old packer when the opt-in API is absent, yielding
semantic assertion reds. R-2 selects all NightlyCoreTests/LiveLaneRunnerTests.

### Checkpoints

This is S4a's closed CP-8 + CP-90 selection, with additional impacted rows added
before their first run. CP-90 includes DerivedImpactIndexTests because a rostered
class is added. Reuse one isolated output per row as the activation plan requires.
No suffix-collision class matches are allowed; inspect fresh TRX per operand.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-8 | S4a | `tests/Antiphon.Tests -> bin-c1836-s4agreen/` | linux-pack | `/*/*/(RcBoundaryPackTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-8 R-2 | 3 new methods and every existing method, zero failed/skipped | 8 | 8 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-s4aguards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(DerivedImpactIndexTests*)/*` | policy, categories, spawn census, index | each class nonzero, zero failed/skipped | 79 | 6 |
| CP-91 | S4a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s4apty/` | pty-roster | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | all methods, zero failed/skipped | 1 | 2 |
| CP-92 | S4a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s4arunner/` | runner-roster | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | all methods, zero failed/skipped | 1 | 2 |

Red witness run `20261113-043938-d9be` tested committed SHA
`64b002a42a753875b5bff5d297680cce1982f30b`: CP-8 executed 8, passed 5,
failed the 3 new semantic contract assertions, skipped 0. Source clean and build
verified. Build slot waited 75s; test slot waited 240s; test execution 75.300s;
whole wall 742.303s, admission-only 315.121s, execution 427.183s. CP-90 was
deferred until the implementation/index were ready; this red-only selection
avoids repeating green guards. One earlier invocation refused an incorrectly
typed expected SHA before any test/build; no credit claimed. Tool bootstrap
used native default output under a granted slot, waited 0s, held 17s.
The failed red output is retained; the final row uses a fresh green output.
Full-range impacted selectors require CP-91/92 (same-named class in two other
projects); CP-8/90 cover nightly-live-lane and test-lane-category.

Two direct gated RepackPreservesCaseUniverse diagnostics avoid cold builds while
developing the new production path; they do not replace fresh checkpoint TRX.
At `00b3be4029c780d2a3dc72df4abcc4e7a67dd1d5`, the initial case passed (wait
315s, held 4s). The expanded constructor/case-collision witness was committed
before its repair, then failed both intended assertions (wait 60s, held 5s).
The repair uses metadata class names for exact filters, retains constructor
identities in case manifests, and refuses case-colliding classes before the
legacy case-insensitive grouping. No timeout or assertion was loosened.

## Performance budget

Provisional under CARD-1836: 45s child/10s joined cleanup, 60s test timeout;
no measured p95 yet. Fixtures are small in-memory discovery rows, no containers,
DB, browser, network or native custody. Existing helper compilation is once per
harness process, no builds in the new tests. Collect first green test timings
and checkpoint build/discovery/slot/phase records; unavailable phases stay null.
The importer floors remain its existing policy exception. Reuse ordinary
Code/Review observations toward five samples; do not repeat green for samples.

## Manual and pending work

Verify no production caller supplies BoundaryAware, Full ID/filter shape is
pinned, layers.txt and selection/publication behavior stay unchanged. No changed
OS branch, migration, HTTP contract or operation label. Broad startup census
includes nightly-tests-impl.ps1; native Windows startup receipt remains caller
owned before rc integration. Restart: none for this inactive opt-in implementation.
S7d CP-17/18/19 and operational historical admission/Shadow remain pending.
Other V-1..V-7/V-9..V-18 and R-1/R-3..R-7 belong to their slices, not passed here.
PC-V8-boundary, PC-V8-prefix, PC-V8-case-membership, PC-V8-default-ID remain
pending for Mutation (operator waived deliberate mutation execution in Code).
