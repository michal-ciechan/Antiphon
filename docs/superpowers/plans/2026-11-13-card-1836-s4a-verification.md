# CARD-1836 S4a: opt-in boundary packing

Historical S4a Code task/landing owner: `6c1c4d05-c107-433b-837f-f882784093ac`.
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
discovery identities, overlapping leaves and malformed/empty membership. S2
resolves short-ID collisions as unassigned/dirty chunks retaining full digests. No caller opts in, no launch/coverage/publisher logic changes.

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
| CP-8 | S4a | `tests/Antiphon.Tests -> bin-c1836-portfinal/` | linux-pack | `/*/*/(RcBoundaryPackTests*)\|(RcBoundaryChunkIdTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)/*` | V-8 R-2 | 5 packer methods, 11 S2 methods and every adjacent method, zero failed/skipped | 21 | 8 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-portguards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(DerivedImpactIndexTests*)\|(ReleaseImpactEnvelopeTests*)/*` | policy, categories, spawn census, index/envelope | each class nonzero, zero failed/skipped | 79 | 6 |
| CP-91 | S4a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-portpty/` | pty-roster | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | all methods, zero failed/skipped | 1 | 2 |
| CP-92 | S4a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-portrunner/` | runner-roster | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | all methods, zero failed/skipped | 1 | 2 |

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
That red source was `21f1bbfe0957bf3a0dd22f9d87d95992e827f918`.
The repair uses metadata class names for exact filters, retains constructor
identities in case manifests, and refuses case-colliding classes before the
legacy case-insensitive grouping. No timeout or assertion was loosened.
The derived-index command built its native default-output driver (0 errors),
reported `derived-impact-index changed`, and was committed separately; slot
wait 105s, held 45s, nested build 5.77s. The wrapper's generic non-dotnet cap
warning is expected: this script explicitly forwards the granted CPU cap.
No deliberate mutant or redundant green repetition was run. Final runs use the
checkpoint tool with the exact committed HEAD as expected source, parallel 1.
Post-regeneration full-range impact also selects ReleaseImpactEnvelopeTests from
the changed index; CP-90 now includes it. Run `20261113-050513-983e` at
`a18ffaf029eb9d5b32b1a1c8edc383d29a62d26f` was stopped through the supported
tool command and joined before this amendment: no build/test executed, no TRX,
exit 130/supported-stop, 37s observed wall. It earns no verification credit.

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

## S4a port onto hardened S2 (task 063b81c6)

Current Code/landing owner: `063b81c6-63d9-408a-853c-681772c288aa`.
Task base: `2349a1938b8ff11829ca6becf3c4719383bf9e52`.
The seven historical S4a commits are cherry-picked with provenance. The S2
library is unchanged. Case identities (bare class/method plus discovered UID)
are passed to S2's resolver, which alone owns framing, hashing and collision
policy. Exposed identityKeys/caseKeys are paired per case; unique methods are
a projection. The packer retains full discovered case class names separately.

Port V-8 adds CanonicalDigestIncludesCaseKeys (pinned canonical ID and changed
case sensitivity) and ShortIdCollisionUsesS2Policy (real CTests/M short collision
fixture through the packer's shared finalizer). The latter checks metadata and
case custody of already partitioned launches; it does not assert that a method
filter can select an individual argument row. CP-8 also runs the 11 S2 harness
cases. No ordinary V/R outside V-8 and R-2 is claimed by this slice.

Two direct gated harness invocations before the fix establish semantic red for
the new cases without another cold assembly build; these are explicitly unlisted
diagnostics and do not replace final fresh TRX. A gated native-output bootstrap
of the checkpoint tool and driver-first index regeneration are also required
unlisted tooling work. Manual real-tree proof reuses the final CP-8 assembly,
produces discovery once, verifies manifest and actual-filter case unions exactly
once, boundary separation/prefix safety and byte equality of opt-out with base.
No full test-assembly execution is authorized or needed for this census.

Performance remains provisional under CARD-1836/CARD-1873; keep the existing
45s child/60s test budgets and collect current timings, without green repeats.
Native Windows startup/packing proof is caller-owned; restart none here.
PC-V8-canonical-case-key and PC-V8-S2-collision join the four historical PC IDs
as pending for method-scoped SourceLanding Mutation; Code executes no deliberate
mutants under the operator waiver.

Port red at `8393b965a7941f23f2fb49857f9c390179190125`: both direct new
harness cases failed their contract assertion on the production ID/digest
mismatch (one assertion row each, zero passed). Slots were granted with 0s
wait; canonical case held 6s, collision case held 5s. The checkpoint tool
bootstrap built successfully (11 warnings, 0 errors; granted, 0s wait, held 7s).
Driver-first native-output index regeneration at
`5d80ffdf1fa99ea24409c92887d6612c382f7dde` built successfully (2053 inherited
warnings, 0 errors, build 262.52s), then reported index unchanged (31 Windows-only,
1 Linux-only); granted, 0s wait, held 293s. The separately cherry-picked index
commit `d7ef504cbdc029991f9cd134fb7f00d5dc0b75f4` already contains those exact
regenerated bytes, so no additional index-byte commit is necessary.

The port's method metadata intentionally repeats identityKeys for expanded
cases, paired with caseKeys. Constructor assertions require both pairs and both
UIDs; the unique-method projection is checked separately. No assertion or timeout
is relaxed. The compiled path indexes full discovery once per boundary pack;
the PowerShell-only fallback retains its existing per-partition indexing cost.
