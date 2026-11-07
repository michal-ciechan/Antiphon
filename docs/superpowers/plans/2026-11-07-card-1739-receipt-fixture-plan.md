# CARD-1739: complete healthy receipt fixture counts

Original Code / landing owner: 68c34eea-6a4d-4a4f-97eb-b8e77ad9e9a6.
Base: fbb2be214a849549cfa72f2e9a17646cc848cfeb.
Scope: tests/Antiphon.Tests/Checkpoints/CheckpointReceiptSafetyTests.cs, Fixture().
Append passed=1 failed=0 skipped=0 to its existing executed=1 receipt token.
Retain every assertion and the strict validators; no product or policy change.
No delivery, landing, lease or persistence behavior changes. Restart: none.

## Performance budget

The requested native incremental base build is a separately leased bootstrap and
also builds the checkpoint tool. Its measured wall was 344.83 seconds, slot wait
1 second, on this Linux worktree at the base above (one sample, not p95).
The two base methods use an unchanged 5-minute host cap and unchanged 4-second
child deadlines. Their native run is an explicitly accounted unlisted baseline
exception: the brief requires execution at the exact base before the fixture fix.
An initial shell timeout spelling was rejected before any driver or lease started;
the actual invocation uses 570s, with 4-minute admission and 5-minute test caps.

Run CP-1 alone, CP-2/CP-3 together, then CP-4/CP-5 together: never over three rows
per tool invocation. All source stays frozen during execution. Each invocation
gets a cold isolated build; rows in that invocation reuse its build. Linux uses
UseAppHost=false for both build and no-build execution. TEMP/TMP/TMPDIR=/tmp;
the worktree stays outside /tmp. No desktop execution or host pinning.

Provisional estimates: CP-1 includes an approximately 6-minute cold compile plus
2-3 minutes receipt-safety, approximately 2 minutes source integration and 2 minutes
guards (prior card receipt: 265-second build, 104-second guards, 121-second source
class). Unit shards have no measured p95; CARD-1404/CARD-1573 retain that gap.
CARD-1727 recorded a stopped A-L Unit shard over 35 minutes at this same base.
This plan splits it further; any new over-30-minute run requires caller-owned
Debug with its timing evidence (this Code delegate cannot subdelegate).
The whole required Unit sweep is a broad regression obligation, not evidence of
unbounded product impact. No full-assembly selection is commissioned. Its invariant
is preservation of the existing Unit contracts while repairing the receipt fixture;
the brief requires all Unit classes, which cannot be reduced to affected classes.
Four alphabet partitions cover that lane. Expected Unit total is provisionally
40-60 minutes, plus cold builds; do not silently repeat a green row.

Use a fixed 20-minute row/build cap and the importer's selected-row total derivation;
do not widen a failing timeout. This is a provisional exception tracked by the
existing timing cards, not a measured-p95 claim. The earlier Unit incident is not
permission to call missing execution green. Keep slot/admission separate from body.
No new expensive fixture/setup is introduced. Existing assembly DB setup is shared;
native command fixtures own private roots. Keep fresh per-test TRX durations,
checkpoint phase/build/slot timing and logs under ignored .antiphon/; uninstrumented
external-call and setup phases remain unknown rather than zero.

## Verification design

| ID | Obligation | Execution |
|---|---|---|
| V-1 | Both original methods red at base, green after fixture repair; healthy controls retain exit 0 and negative identity/state controls retain their documented exits | Native base red; CP-1 full CheckpointReceiptSafetyTests (8 results) |
| V-2 | Dropping each executed/passed/failed/skipped token refuses with receipt_counts in both real validators; retain healthy control and all assertions | CP-1 full CheckpointSourceExecutionTests (14 results), especially validators_refuse_changed_count_tokens |
| V-3 | Manual diff confirms only healthy synthetic data changes, all assertions/deadlines/validators unchanged; OS/startup census, evidence history and merge-tree | Inspect base..HEAD; check-os-witnesses.ps1; check-evidence-diff.ps1; merge-tree |
| R-1 | Whole Unit lane | CP-2 through CP-5; inspect fresh TRX counts and membership |
| R-2 | Required policy sweep, layering, mechanical and platform contracts | CP-1 named full guard classes |

The existing count-token witness already tests missing, wrong and noncanonical
tokens through both validators with independent expected outcomes. Reuse it rather
than adding duplicate tests. No new test is a constant/self-comparison witness.
No test-execution-policy.json edit; nightly policyHash/PinnedInputDigest unchanged.
No changed OS-selected production behavior or startup path. The existing Linux
FIFO guard is unchanged; disposition it explicitly in the OS inventory.

All deliberate controls remain pending SourceLanding Mutation after ordinary Review
and caller landing. Code does not execute or claim these cycles:

| PC | Exact method | Pending variants |
|---|---|---|
| PC-1 | CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing | Omit each of executed, passed, failed, skipped from Fixture's healthy line; CP-fixture control must fail |
| PC-2 | CheckpointReceiptSafetyTests.malformed_state_shape_by_verb_table_matches_documented_exits | Omit each of executed, passed, failed, skipped from Fixture's healthy line; report-only healthy validates must fail |
| PC-3 | CheckpointSourceExecutionTests.validators_refuse_changed_count_tokens | Bypass each count-token check separately in each validator (4 tokens x 2 validators); missing-token control must fail |

Candidate inventory is unbound; Mutation owns reachability, red/restore/green and
missing-control discovery. No ordinary green run discharges these 16 variants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1739-final/ | affected-and-guards | `/*/*/(CheckpointReceiptSafetyTests*)\|(CheckpointSourceExecutionTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(CheckpointToolProjectContractTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/*` | V-1, V-2, R-2 | every named full class, zero failed/skipped | 80 | 12 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-2 | S1 | CP-1 | unit-a-c | `/*/*/(A*)\|(B*)\|(C*)/*[Category=Unit]` | R-1 | every selected Unit result, zero failed | 1 | 15 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-3 | S1 | CP-1 | unit-d-l | `/*/*/(D*)\|(E*)\|(F*)\|(G*)\|(H*)\|(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit]` | R-1 | every selected Unit result, zero failed | 1 | 10 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-4 | S1 | CP-1 | unit-m-r | `/*/*/(M*)\|(N*)\|(O*)\|(P*)\|(Q*)\|(R*)/*[Category=Unit]` | R-1 | every selected Unit result, zero failed | 1 | 10 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
| CP-5 | S1 | CP-1 | unit-s-z | `/*/*/(S*)\|(T*)\|(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit]` | R-1 | every selected Unit result, zero failed | 1 | 15 | true | TEMP=/tmp; TMP=/tmp; TMPDIR=/tmp |
