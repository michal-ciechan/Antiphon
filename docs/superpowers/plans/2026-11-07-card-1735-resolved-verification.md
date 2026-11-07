# CARD-1735 already-landed repair verification

Original Code task and landing owner: 2d93e0e1-f395-46d4-a353-698e8554622f.
Branch: feat/card-task-2d93e0e1. Worktree: /work/worktrees/task-2d93e0e1.
Base: fbb2be214a849549cfa72f2e9a17646cc848cfeb. Round: Final.

## Finding and scope

The brief explicitly permits closure when the base native witness is green.
The exact exclusion already exists for
`C0824TransportGitTests.Scratch_git_deadline_includes_inherited_output_drains`.
The landed repair is 32aea64a2e27c46b075688988550dfe3f2023852,
cherry-picked from ce49cd02ed49e87cf93b5194ae29cfc7aea24ea7.
The earlier repair plan is
`docs/superpowers/plans/2026-11-06-scratch-git-policy-repair.md`;
its historical red source is 3b7d80c66c28e8cae7a5c1b762d066dee6f3c07b.
That historical red is not a new run by this task.

The required native preflight succeeded at base in 247.22 seconds, slot wait 0s.
The required once-at-base witness then passed all three methods, no skips,
11.735 seconds test duration, slot wait 0s, under a 2m diagnostic run timeout.
Fresh TRX: `.antiphon/c1735-base-native/base-native.trx`.
These two explicitly requested diagnostics are outside the checkpoint list.
No production, test, marker, policy or digest changes are necessary. This plan
records the remaining Final verification rather than reapplying a landed fix.

## Performance budget

One cold checkpoint build of Antiphon.Tests, reused by both serial rows; no
full-assembly selection. UseAppHost=false and TEMP/TMP/TMPDIR=/tmp throughout.
The tool already built in the native project graph; invoke it with --no-build.
Checkpoint driver owns each build/test slot; tool launcher performs no build.

CP-1 provisionally budgets 6 minutes including the cold build (the native build
measured 247.22s under concurrent host load); CP-2 budgets 15 minutes for the
required whole Unit lane. Estimates are not measured p95. Importer's existing
18m/45m local deadlines and derived 52m execution budget are retained, admission
bounded separately at 45m. No timeout is widened and no green row is repeated.
The whole Unit lane's cross-cutting invariant is that all Unit-tagged behavior
and repository guards remain valid. Its classes cannot be bounded further while
satisfying the required whole lane. Unit omits delivery, landing, leases and
persistence; this documentation-only closure affects none of those behaviors.
Prior over-30-minute Unit work is tracked by CARD-1720; a new over-30-minute
selection requires caller Debug with the exact run timing, not a blind repeat.

Retain build/admission/row wall times, per-test durations and membership in the
checkpoint reports/TRX. Fixture startup, preparation, database, teardown and
external-call phases without instrumentation are unknown, not zero. This task
adds no fixture or expensive setup. No additional native platform is required.

## OS and startup audit

Only this Markdown plan changes. No changed OS branch, helper, production
factory/property, optional configuration or startup path exists. The existing
Linux scratch test invokes real C0824SlotTransportWorld.RunGit with a shell alias
and native sleep child retaining output pipes, then asserts TimeoutException
and the unchanged deadline. NativeLinuxPolicyWitnessTests reflects the compiled
NativeLinuxOnly marker and checks exact policy coverage in both directions.
Read GET /api/runner-defaults and /api/session-runners before execution; work
stays in the assigned Linux checkout. Restart: none. Run check-os-witnesses with
an empty changed-code inventory and required=false startup declaration.

## Verification design

- V-1: all NativeLinuxPolicyWitnessTests methods and full C0824TransportGitTests
  preserve exact registration and real deadline/cancellation/output behavior.
- V-2: full TestClassificationPolicy*, TestClassificationGuard*,
  SlowTestRegistryMerge*, SlowTestTripwire*, C1729LayeringGuardTests*,
  TestLaneCategoryGuardTests*, MechanicalPcContractTests*, TaskPlatformGuidanceTests*
  and RunnerDefaultGuidanceTests* preserve policy, layering and instruction contracts.
- V-3: whole Unit lane, including method tags, under the Final profile.
- R-1: manually recompute the nightly policy hash and live-lane protected input
  digest against their shipped literals; run test-mechanical-pc-contract.ps1 under
  its own slot. Neither digest input changes, so old equals new.
- R-2: git diff --check, full base..HEAD check-evidence-diff, OS witness audit,
  fetched origin/master merge-tree and final remote branch SHA.

No ordinary V/R is deferred. The historical repair plan's policy-consumer
expansion belongs to its policy-changing commission; no policy changes here.
PC-1/remove-exact-exclusion from that plan remains pending for method-scoped
SourceLanding Mutation: remove only this method exclusion, reseal both digests,
run NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute, require
the named reverse-coverage failure, restore and run green. This task runs no
deliberate mutant; Mutation owns red/restore/green and missing-control discovery.
No new PC is introduced by this Markdown-only closure. Ordinary Review is next.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | closure | tests/Antiphon.Tests -> bin-c1735-closure/ | native-and-guards | /*/*/(C0824TransportGitTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)/* | V-1,V-2 | every listed class, 0 failed/skipped | 70 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | closure | CP-1 | whole-unit | /*/*/*/*[Category=Unit] | V-3 | whole lane, disclose inherited red/skips | 1000 | 15 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
