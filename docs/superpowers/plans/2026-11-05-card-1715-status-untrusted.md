# CARD-1715: explain and surface untrusted Git status

Original Code/landing owner: 420b5c90. Base: 1527c29fef2737bf3eaa49f2293d82d85402265a. Branch: feat/card-task-420b5c90. Final ordinary verification.

## Scope and invariant

S1 commits red-first witnesses on unchanged product code. S2 preserves `status_untrusted` at the initial and resumed post-reset land inspections, explains that stderr prevented proof of cleanliness in recorded refusal details, and shows the source refusal with that explanation in the existing task detail. All other refusal codes, acceptance predicates, publication, cleanup, state transitions and decision logic stay unchanged. No stderr text is exposed. The brief selects task detail as the operator surface; policy-tick aggregation, warning buckets, path-walk controls and new alerting are outside this smallest-change slice.

## Verification design

- V-1/R-1: AgentTaskLandHalfResetTests.C1715_Initial_adoption_preserves_untrusted_status_reason reaches a successful reset, injects an exit-zero/empty-stdout status with non-English stderr, then checks the persisted reason, recorded explanation, absence of private stderr, retained recovery intent and no publication/target push.
- V-2/R-2: AgentTaskLandHalfResetTests.C1715_Resumed_adoption_preserves_untrusted_status_reason first interrupts after CAS, sweeps the request and reaches the resumed successful reset; the same observations distinguish the other land site.
- V-3/R-3: TaskDetailBody.test.tsx C1715 shows the source refusal in task detail, status_untrusted and source_dirty variants. Actual rendering must show the machine reason and the honest explanation only for the untrusted case.
- V-4/R-4: Full affected landing classes: AgentTaskLandHalfResetTests, AgentTaskLandRegistrationSafetyTests, AgentTaskLandSourcePersistenceTests and LandSourceIdentityTests. These cover reset/recovery guards, registration refusals, source persistence and source identity. Unit alone omits these landing/persistence invariants.
- V-5/R-5: Whole Unit lane plus full TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests, SlowTestTripwireTests and MechanicalPcContractTests. The four Unit classes execute within CP-2; registry merge executes in CP-1. Inspect fresh TRX by class/method and nonzero counts; retain known inherited red rows with their actual outcomes.
- V-6: Manual diff inspection confirms no acceptance/decision/landing semantic changes, no raw stderr rendering, and no new sink/column. Run full-range check-evidence-diff and merge-tree against freshly fetched origin/master. No OS-selected behavior changes; existing production Git path remains unchanged. Restart: none; eventual server and client activation belongs to caller after landing, not this task.

## Performance budget

No measured p95 for this selection. Estimates are provisional under CARD-1404/CARD-1720, collected once here in fresh TRX. Native incremental compile before cold checkpoint build is explicitly required by the brief and also builds the red witnesses. Native red tests and client red witness are additional diagnostic runs, not green ordinary receipts. Bootstrap the checkpoint tool in a separate slot, then run its committed closed list once. No repetitions after green. Admission is bounded by the tool's existing 45-minute slot limit; execution allowance derives from selected estimates. Rows run serially to avoid competing Antiphon.Tests hosts. One shared cold build is estimated 5-12 minutes, landing execution 1-5 minutes, Unit execution provisionally 15-30 minutes, client prerequisite installation/typecheck/lint and test execution 2-5 minutes. The Unit lane's prior >30-minute incident is investigated by Debug 86a941a3/CARD-1720; do not call that censored run p95 or widen any timeout. Any newly observed selection above 30 minutes needs caller Debug with the retained timings. Existing assembly-owned DB fixture and per-test schema isolation are reused. Phase timings not emitted by existing instrumentation stay unknown. Preserve build/slot wait separately from execution. No full-assembly run is planned: the mandated Unit predicate is the exact category lane, plus the bounded named integration classes.

### Positive controls

Unbound candidates: docs/superpowers/mutations/2026-11-05-card-1715-controls.md. All PCs and variants remain pending for method-scoped SourceLanding Mutation; Code does not execute deliberate mutants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1715-final/` | landing | `/*/*/(AgentTaskLandHalfResetTests*)\|(AgentTaskLandRegistrationSafetyTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(LandSourceIdentityTests*)\|(SlowTestRegistryMergeTests*)/*` | V-1,V-2,V-4,R-1,R-2,R-4,R-5 | all listed, 0 failed | 1 | 12 | true |
| CP-2 | S2 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | V-5,R-5 | >= 1 executed, 0 failed | 1 | 15 | true |
| CP-3 | S2 | n/a | detail | `pwsh -NoProfile -File scripts/test-client.ps1 TaskDetailBody.test.tsx -JsonResultPath ../.antiphon/c1715-final-client.json` | V-3,R-3 | client file passes, 0 failed | n/a | 3 | true |

CP-1 intentionally combines five bounded full classes to share a cold build. CP-2 includes the full named Unit guard/mechanical classes; separate green reruns would duplicate coverage. CP-3 also performs the required client typecheck and lint prerequisites. At most three rows per run. Known base failures named in the brief: C479_R06_LowNeverDiscoversAndTierRowsStayLow, Policy_rows_require_pinned_attribute, Markers_do_not_expand_policy_exclusions, LinuxOnlyWindowsExclusionPolicy. Confirm any observed inherited reds against unchanged production base before attributing them.
