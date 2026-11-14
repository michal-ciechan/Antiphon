# CARD-1886 S2 claim-query layering repair

Code/landing owner: `6a3c6a69-5ec6-44c8-8ad4-bebc4dc8a540`.
Task base: `d5c798092590ad377361292e2785a56b3b93fe16`.
S2 design and historical evidence: [original plan](2026-11-13-card-1886-postgres-jit-and-hot-queries-plan.md).

The assigned branch already starts at current master. Preserve it and cherry-pick S2 with `-x`.
The old generated-index commit conflicts with newer master entries and is superseded by an
independent regeneration after building the current driver. Preserve every master entry.

Pass QueuePhase and ChannelReplyDebt from the coordinator into the Infrastructure query as
typed values. Keep the constants in Application as the single source of truth, the entire SQL
literal byte-identical, Npgsql parameter names/types/order, and every selection assertion.
No migration, configuration, delivery, lease, timeout or OS behavior changes.

V-2: original eighteen-fixture identity/drain oracle and eight-plan matrix, plus all five
coordinator classes in CP-2. Confirm the estimated costs remain about 86.30/105.42 against
legacy 500,469/10,704,139. V-L: real syntax-analysis assertion rejects the claim query's
Application edge regardless of allowlist mode; CP-RED runs it before the fix and prints the
existing inventory (expected VIOLATIONS 1 LISTED 162). CP-10 afterward must print
VIOLATIONS 0 LISTED 162. R-1: full guard union, actual impact selection, source receipts,+OS declaration, evidence-diff over the entire task range, and conflict-free merge-tree.
V-1/V-3/V-4/V-5 and CP-1/3/4/5/6/7/8/9 belong to separately commissioned S1/S3/S4/S5,
not this S2 repair. CP-RED is expected assertion-red, never a green gate.

Known CARD-1905 recovery/routing failures are independently red at master. Preserve their
assertions; rerun any observed failures method-scoped at this tip and the exact task base.
No inherited failure becomes a passing result. Manual live CPU/query windows, active server
SHA, trace/counters and live operation shares remain pending deployment acceptance owned by
the caller; this task is forbidden to restart or touch live hosts.

All PC-S2-1a/b/c, PC-S2-2a/b/c, PC-S2-3a/b/c/d, PC-S2-4a/b remain pending SourceLanding
Mutation under the [unbound candidate](../mutations/2026-11-13-card-1886-s2-unbound.md).
The new layering witness also adds PC-L-1: reintroduce the coordinator constant reference and
require its exact method to fail; pending Mutation. No deliberate mutants in Code.
Restart target: server after caller-commissioned publication/activation; none performed here.

## Performance budget

Use the original plan's provisional budgets and CARD-1404/1514 timing follow-up. Historical
cold compilation was 4m36s–5m22s; CP-2 had 105 test results. These are observations, not p95.
Each row runs alone, with a nine-minute execution/build cap and separate bounded slot admission;
no timeout is widened. One checkpoint run per committed group; CP-RED alone before repair,
CP-2 and CP-10 serially after the separate generated-index commit. Fresh isolated output per
row; inspect fresh TRX identities/counts and verified source binding. Enable timing JSONL in
ignored evidence. Setup is shared by TestDbFixture per host; each case uses isolated clones.
Unknown phase spans stay unknown, not zero. Record load (especially >30), build, slot wait,
body, teardown and remainder independently. No repetitions after green. No full Unit lane.
The standalone checkpoint/impact drivers are required infrastructure builds, leased with
native default output. The generator runs twice; second must print unchanged. Protected
nightly inputs remain unchanged, so policy/digest re-pinning is not warranted.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-RED | witness | tests/Antiphon.Tests -> bin-c1886-l-red/ | layering-red | `/*/*/C1729LayeringGuardTests/*` | V-L | new method assertion-red; inventory VIOLATIONS 1 LISTED 162 | 10 | 6 |
| CP-2 | repair | tests/Antiphon.Tests -> bin-c1886-l-s2/ | action-claim | `/*/*/(RuntimeActionClaimQueryTests*)\|(SessionRuntimeActionTests*)\|(SessionRuntimeActionRecoveryTests*)\|(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionDeferralTests*)\|(SessionRuntimeActionTemporaryBlockTests*)/*` | V-2 | all classes; inherited red explicitly attributed | 6 | 6 |
| CP-10 | repair | tests/Antiphon.Tests -> bin-c1886-l-guards/ | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | V-L, R-1 | all classes, 0 failed/skipped; VIOLATIONS 0 LISTED 162 | 10 | 6 |

--- next stage ---
next: review
handoff: Review the S2 layering repair, unchanged SQL and inherited-failure evidence; preserve all pending SourceLanding Mutation controls and live acceptance obligations.
artifact: docs/superpowers/plans/2026-11-14-card-1886-s2-layering-repair.md
