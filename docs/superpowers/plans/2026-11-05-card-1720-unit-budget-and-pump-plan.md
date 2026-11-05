# CARD-1720: Unit budget lint, sharding and fake-clock deadline

Original Code owner: c3ba1792 (resumes 64fb3f5d). Task base fbbb008824a8a168113e3af87d8308e582721286; complete candidate base cc8845f030d934507e7a0d2db7d939be06d9f2bc. Debug 86a941a3 supplies the incident measurements. Test infrastructure only; no project/props graph changes or product timeout changes.

## Implementation

S1 retains the committed importer, YAML and CLI refusal of an explicit total below 2*sum(selected estimates)+10, with an explicit nonblank override reason. Only the default total has the max(30, ...) floor. Preserve flushed TRX on scheduler cancellation. S2 caps the fake-clock pump's final advance at the owner deadline, then waits for the pending continuation on real time within the existing bound. Keep Narrower_parent_deadlines_win's seconds+2 assertion. A gated continuation with 200 ms and 700 ms steps observes the exact five-second boundary.

Round 1 blockers and the caller's bounded repair verification are specified in [the repair plan](2026-11-05-card-1720-round1-repair.md). Its checkpoint table applies to repair e3c30656; the original whole-Unit V-4 gate below is not discharged by that repair scope.

## Performance budget

All estimates below are provisional, tracked by CARD-1720; no measured Unit p95 exists. Incident 20261105-013711-58df's 535 s before kill is censored, not p95. Debug 86a941a3 measured the old resilience class at 3.7-8.5 s. Collect this run's per-test durations/TRX and row execution/admission separately; use approximately 2x measured p95 when enough samples exist. No test timeout is widened. Importer 15-minute floor/3x estimate remains a documented exception, not R5 compliance. The red-first cold build measured 11m11s at eacba3616 on this Linux host; allow 5-12 minutes provisionally, host slot admission bounded at the existing 45 minutes. Shared setup remains assembly-owned; database tests use their existing isolated schema fixture. Unknown fixture/external-call phases remain unknown in the report. Ordinary estimate sum is 81 minutes; derived charged cap 172 minutes (not an explicit 30). There is no requirement to consume that allowance. Run rows serially to avoid competing Antiphon.Tests hosts. Any selection over 30 minutes is already investigated by Debug 86a941a3; preserve new timing evidence for further Debug if needed.

Unit partitions use the category predicate on every shard, including method-level Unit tags. Class initials A-Z partition the current discovered class census; inspect fresh TRX membership and compare the source census, including MatrixDefaultComplexityCreateTests.The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled. This avoids the class-tag-only omission in the earlier CARD-1573 guidance. The checkpoint tool tests are the union of all Unit checkpoint classes and CP-8's Integration checkpoint classes. No full assembly run is planned. Shared scheduler/importer invariants require all checkpoint tool classes; they are bounded by that namespace rather than the application assembly.

## Verification design

### Inspection

Read ResilienceTestSupport.Pump and its six callers: ResilienceBudgetTests, ResilienceCircuitTests, ResilienceOptionsAndTelemetryTests, HttpReadResilienceTests, HttpResilienceRegistrationTests, DatabaseReadResilienceTests. Only Narrower_parent_deadlines_win supplies ownerDeadline; other callers retain the existing path. Read PlanTableImporter, ManifestValidator, Program.LoadSelection, CheckpointApp.ExecuteAsync, RunScheduler cancellation result, and their CheckpointImportTests, CheckpointTaskOwnershipTests and TimeoutTests witnesses. Inspect stage-test-design template and retain its existing delivery and checkpoint requirements.

### Proves it works now

- V-1: Pump_stops_at_the_owner_deadline_when_the_continuation_is_gated, both 200/700 ms variants: exactly 5 s while the continuation is held.
- V-2: CheckpointImportTests explicit total 54 accepted, total 30 refused with checkpoint-total-below-derived, reason permits import/YAML/CLI. Executor honors the manifest total in CheckpointTaskOwnershipTests.
- V-3: TimeoutTests.unit_shards_keep_their_trx_when_the_total_budget_kills_a_later_shard: completed and flushed interrupted shard each retain one pass.
- V-4: Whole Unit lane is partitioned without losing method-level tags, each shard has its own fresh TRX. Tool Integration classes and named adjacent Integration classes execute in full.
- V-5: Manual guidance/template inspection: estimates labelled provisional, incident not called p95, 2x measured-p95 policy retained; no lost pre-existing delivery, Min, coverage or roster requirements.

### Guards the regression

- R-1: ResilienceBudgetTests.Narrower_parent_deadlines_win: 3/5/10 s parent deadlines preserve elapsed >= seconds and < seconds+2; gated witness prevents continuation overshoot including non-divisible steps.
- R-2: CheckpointImportTests explicit_total_below_derived_is_refused_without_an_override_reason, cli_total_below_derived_is_refused_without_an_override_reason, yaml_explicit_total_below_derived_is_refused_without_a_reason: refusal before launch.
- R-3: TimeoutTests.unit_shards_keep_their_trx_when_the_total_budget_kills_a_later_shard: cancellation preserves already-flushed evidence without changing timeout verdict.
- R-4: Full TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests, SlowTestTripwireTests remain green.

### Guard inventory

G-1 owner stop; G-2 final-step clamp; G-3 plan total lint; G-4 YAML lint; G-5 CLI lint; G-6 canceled-row TRX retention. Each maps 1:1 to the following controls.

### Positive controls

All remain pending for method-scoped SourceLanding Mutation; Code red-first evidence does not discharge them.

- PC-1/G-1: remove owner-deadline stop and clamp together; gated Pump witness (200 ms) fails exact five seconds.
- PC-2/G-2: remove final-step clamp; gated Pump witness (700 ms) observes 5.6 s and fails exact five seconds.
- PC-3/G-3: accept short plan total; explicit_total_below_derived_is_refused_without_an_override_reason fails exit 2 assertion.
- PC-4/G-4: skip ManifestValidator total check; yaml_explicit_total_below_derived_is_refused_without_a_reason fails expected exception.
- PC-5/G-5: skip Program.LoadSelection total check; cli_total_below_derived_is_refused_without_an_override_reason fails expected exception.
- PC-6/G-6: discard flushed cancellation TRX; unit_shards_keep_their_trx_when_the_total_budget_kills_a_later_shard fails second.Trx nonnull.

Unbound candidate docs/superpowers/mutations/2026-11-05-card-1720-controls.md. No OS-dependent behavior changed: importer uses its existing platform-selected estimate; pump and receipt retention are portable. No AppHost/runner/startup changes; restart none. Desktop execution prohibited while qualification b19a9880 is live.

### Out of scope

Product resilience changes; delivery/landing/leases/persistence verification outside the named test infrastructure classes. Production stack or runner restart and project graph changes are prohibited. Deliberate positive controls belong to Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1720-final/` | core | `/*/*/(CheckpointImportTests*)\|(CheckpointTaskOwnershipTests*)\|(TimeoutTests*)\|(ResilienceBudgetTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | V-1,V-2,V-3,R-1,R-2,R-3,R-4 | all listed, 0 failed | 1 | 10 |
| CP-2 | S1-S2 | CP-1 | unit-ad | `/*/*/(A*)\|(B*)\|(C*)\|(D*)/*[Category=Unit]` | V-4 | >= 1 executed, 0 failed | 1 | 15 |
| CP-3 | S1-S2 | CP-1 | unit-eh | `/*/*/(E*)\|(F*)\|(G*)\|(H*)/*[Category=Unit]` | V-4 | >= 1 executed, 0 failed | 1 | 5 |
| CP-4 | S1-S2 | CP-1 | unit-il | `/*/*/(I*)\|(J*)\|(K*)\|(L*)/*[Category=Unit]` | V-4,V-5 | >= 1 executed, 0 failed | 1 | 5 |
| CP-5 | S1-S2 | CP-1 | unit-mp | `/*/*/(M*)\|(N*)\|(O*)\|(P*)/*[Category=Unit]` | V-4 | >= 1 executed, 0 failed | 1 | 10 |
| CP-6 | S1-S2 | CP-1 | unit-qt | `/*/*/(Q*)\|(R*)\|(S*)\|(T*)/*[Category=Unit]` | V-4,R-1,R-4 | >= 1 executed, 0 failed | 1 | 15 |
| CP-7 | S1-S2 | CP-1 | unit-uz | `/*/*/(U*)\|(V*)\|(W*)\|(X*)\|(Y*)\|(Z*)/*[Category=Unit]` | V-4 | >= 1 executed, 0 failed | 1 | 5 |
| CP-8 | S1-S2 | CP-1 | checkpoint-integration | `/*/Antiphon.Tests.Checkpoints/*/*[Category=Integration]` | V-4,R-3 | >= 1 executed, 0 failed | 1 | 10 |
| CP-9 | S1-S2 | CP-1 | database-resilience | `/*/*/DatabaseReadResilienceTests/*` | V-4,R-1 | all listed, 0 failed | 1 | 3 |
| CP-10 | S1-S2 | CP-1 | registry-merge | `/*/*/SlowTestRegistryMergeTests/*` | R-4 | all listed, 0 failed | 1 | 1 |
| CP-11 | S1-S2 | CP-1 | bundle-attachment | `/*/*/AgentBundleAttachmentTests/*` | V-5 | all listed, 0 failed | 1 | 2 |

### Cost

Ordinary floor 81 minutes provisional, including one cold build; no extra repeats after green. PC floor six method-scoped red/restore/green cycles, approximately 12 method runs plus builds, provisional 15 minutes (Mutation measures). Sharing CP-1 output avoids ten redundant cold builds (approximately 50-100 minutes). Direct red build/run is necessary to establish the newly found final-step defect before its repair, not extra ordinary coverage. Tool bootstrap uses its native output and a separate slot; checkpoint rows take their own slots.
