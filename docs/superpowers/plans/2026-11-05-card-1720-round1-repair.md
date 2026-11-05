# CARD-1720 round 1 repair

Repair owner e3c30656-689f-4662-a873-1f6278ade7b1; original Code landing owner c3ba1792-e318-4638-b9f2-b8222086960b. Review a2cfcdc1 and production base 7198f531dcd3adfd6e40a21a440edd055a9d669c. Preserve [the review](../../investigations/2026-11-05-review-card-1720.md). Review round 2 reads only the diff from that production base.

## Implementation

Invariant B-1: explicit totals use 2*sum+10 at plan, YAML and CLI admission; only an omitted total has a 30-minute default floor. Keep checked overflow and existing override behavior. The three live-lane diagnostic harnesses retain their existing 26m execution and 29m wall caps with explicit reasons, including control selections. Evidence's CP-29 total 20m exceeds its real 12m threshold and needs no override or script edit. No test/row timeout is widened and no assertion is relaxed.

Invariant B-2: the test-design bundle fits the existing 2,480-character trimmed guard and retains all instructions. Clarify default versus explicit total in the template and testing owner. No product or OS-selected behavior, project graph or startup dependency changes. Restart: none. Desktop execution is prohibited while qualification b19a9880 is live.

## Performance budget

One native red build (measured 3m06s, 0s admission) and a red witness run (18 results, 13 expected failures, 2.67s, 0s admission) at test-only commit 7c5c27b6f0575eea2a3482510a05cf29f0afc729 establish the unchanged production-base defects. One leased tool bootstrap at the repair SHA is required because the tool itself changes. The checkpoint tool then owns one isolated shared build and one exact filter per row below. Native output is used only for bootstrap/red witness work. No full application assembly run.

CP-1 preserves the caller's full checkpoint namespace closure (Review measured 48m15s under contention, 816 results). CP-2 covers named adjacent full classes, including all six resilience consumers, bundle/doc guards and classification guards (Review's larger comparable selection took 2m43s). Estimates 20m and 5m are provisional, not measured p95; importer-derived 60m/15m caps retain its documented 3x exception under CARD-1720/CARD-1404. This is a new repair selection, not a widened test timeout. Slot admission remains separately bounded at 45m per driver. Review already investigated the over-30m selection; retain its cost, including known 137/OwnerEnded flakes, rather than adding routine repetitions. Phase attribution absent from logs remains unknown, never zero. Test fixture lifetime/schema/process isolation is unchanged.

The brief explicitly excludes the whole Unit lane from this repair. Original V-4 and original CP-2..CP-7 remain incomplete; this repair cannot supply Full acceptance for the original candidate. No shard-coverage advisory work or override-receipt advisory expansion is included.

## Verification design

### Inspection

Read RowTimeout, all three total lint consumers, four live-lane harnesses and their imported plans, existing total-lint tests, the unchanged pump and its consumers, bundle guard and retained instructions. Delivery, landing, leases and persistence are outside Unit scope; checkpoint infrastructure integration fixtures remain in CP-1. Inspect fresh TRX for every selected class and changed method with nonzero counts.

### Proves it works now

- V-1: original gated pump 200/700ms variants, CP-2; exact five-second owner stop.
- V-2: original plan/YAML/CLI explicit-total acceptance/refusal/override and executor handling, CP-1.
- V-3: original flushed canceled-shard TRX preservation, CP-1.
- V-4: original whole-Unit partition evidence remains incomplete and excluded by the repair brief; checkpoint Integration and named adjacent Integration classes run in CP-1/CP-2.
- V-5: manual template/guidance inspection plus full bundle/doc classes, CP-2; preserve delivery, roster, Min, provisional estimates and measured-p95 policy.
- V-6: CheckpointCallerBudgetTests.explicit_totals_use_estimates_without_the_default_floor, plan/yaml/cli variants accept 12m for estimate 1 while the omitted default remains 30m, CP-1.
- V-7: CheckpointCallerBudgetTests.committed_live_lane_callers_pass_total_lint reads actual committed script totals/row assignments/control rosters/estimates and invokes real LoadSelection. Startup/delivery base+controls, isolation producer/consumer base+controls and both evidence calls: ten results, CP-1. The extractor refuses unknown source shapes; it does not execute live-lane payloads.

### Guards the regression

- R-1: original Narrower_parent_deadlines_win and gated pump cases, CP-2.
- R-2: original below-derived refusal tests at plan/YAML/CLI, CP-1.
- R-3: original timeout verdict with retained flushed TRX, CP-1.
- R-4: full TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests and SlowTestTripwireTests, CP-2.
- R-5: unchanged TaskPlatformGuidanceTests.Stage_bundles_leave_twenty_characters_below_the_size_cap and full InstructionBundleTests/ScopedVerificationInstructionTests, CP-2.

### Positive controls

Every original PC-1..PC-6 and its variants remains pending for method-scoped SourceLanding Mutation. Code does not own deliberate mutants, red/restore/green, or missing-control discovery. Preserve Review a2cfcdc1's historic lint-flip and pump-overshoot evidence; do not count it as discharge.

- PC-7: reintroduce the 30-minute minimum in DeriveMinimumTotalMinutes; exact method `/*/*/CheckpointCallerBudgetTests/explicit_totals_use_estimates_without_the_default_floor`, plan/yaml/cli variants must fail admission of 12m.
- PC-8: restore the 2,483-character bundle; exact method `/*/*/TaskPlatformGuidanceTests/Stage_bundles_leave_twenty_characters_below_the_size_cap` must fail the 2,480 bound.
- PC-9: remove explicit harness overrides, one script per cycle; exact method `/*/*/CheckpointCallerBudgetTests/committed_live_lane_callers_pass_total_lint`; startup controls, delivery controls, isolation producer-controls and consumer base/controls must fail real LoadSelection admission.

Unbound candidate: docs/superpowers/mutations/2026-11-05-card-1720-controls.md. Mutation also owns the requested deliberate reapplication of Review's lint comparison flip and pump deadline overshoot.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | tests/Antiphon.Tests -> bin-c1720-repair/ | checkpoint-tool | `/*/Antiphon.Tests.Checkpoints/*/*` | V-2,V-3,V-6,V-7,R-2,R-3 | all classes, 0 failed | 1 | 20 |
| CP-2 | S1-S2 | CP-1 | affected-guards | `/*/*/(ResilienceBudgetTests*)\|(ResilienceCircuitTests*)\|(ResilienceOptionsAndTelemetryTests*)\|(HttpReadResilienceTests*)\|(HttpResilienceRegistrationTests*)\|(DatabaseReadResilienceTests*)\|(ResiliencePredicateTests*)\|(TaskPlatformGuidanceTests*)\|(InstructionBundleTests*)\|(ScopedVerificationInstructionTests*)\|(InstructionFragmentTests*)\|(CheckpointManifestDocumentationTests*)\|(AgentBundleAttachmentTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/*` | V-1,V-5,R-1,R-4,R-5 | all classes, 0 failed | 1 | 5 |

### Cost

25m provisional estimate sum plus isolated build and admission; total execution default 60m, wall default adds admission allowance. Run serially so these Antiphon.Tests hosts do not compete. Expected observed namespace run may approach Review's 48m; no repeats after green. Independent per-row TRX and one shared build avoid another cold build. PC cost remains separately pending for Mutation; provisionally original six controls 15m plus repair controls 10m and slot waits, to be revised by Mutation with measurements.
