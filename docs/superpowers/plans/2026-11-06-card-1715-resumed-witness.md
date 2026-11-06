# CARD-1715 follow-ups A-2, A-4 and A-6

Code task 555da802-d558-4c26-9e38-49d072636603; original Code/landing owner
420b5c90-74c8-4582-82d5-d6d77237f18a. Branch feat/card-task-555da802;
task base 1a22e0d2ca7b32e7f6ac346a658e1c7824252a46. Final verification.

## Scope and invariant

S1 adds test/doc-only coverage. Production landing guards and all production files
remain byte-identical to task base. After an interrupted CAS and resumed reset, a
real dirty checkout must refuse adopt_local_changed, preserve edits and reset
intent, and never publish or display the status_untrusted explanation. The owner
document distinguishes status_untrusted using the actual recorded explanation.
The optional A-6 witnesses inject status exit 128 after initial and resumed resets
and assert the persisted and recorded sanitized command/exit, without private stderr.
CARD-1715 remains open; this follow-up does not address its other work.

## Verification design

- V-1/R-1: C1715_Resumed_dirty_checkout_preserves_adopt_local_changed reaches the
  resumed post-reset cut after a real CAS interruption and changes feature.txt on
  disk. Assert reason, preserved bytes, retained intent, no operation/target push
  and absence of the stderr explanation. Review d67a21b1 demonstrated M5 survived
  the old 58-test class. PC-5 now names this exact witness and M5 mutation.
- V-2/R-2: C1715_Post_reset_status_error_preserves_diagnostic(false/true) checks
  both sites' ordinary-refusal diagnostics and sanitized event text.
- V-3/R-3: Run every full class in Review's 127-result landing row:
  AgentTaskLandHalfResetTests, AgentTaskLandRegistrationSafetyTests,
  AgentTaskLandSourcePersistenceTests, LandSourceIdentityTests,
  SlowTestRegistryMergeTests. Expected new total 130 (61+7+35+22+5).
- V-4/R-4: Whole Unit lane, including the mechanical instruction contract and
  TaskPlatformGuidanceTests, TestClassificationPolicyTests,
  TestClassificationGuardTests, SlowTestTripwireTests and NativeLinuxPolicyWitnessTests.
  CP-2 separately inventories the requested full guard classes; CP-3 remains the
  exact category lane and includes method-level Unit cases. Inspect fresh TRX for
  every intended class/method and nonzero counts. Any red is rerun precisely;
  inherited attribution requires an exact base run, not another full lane.
- V-5: Manual owner-doc/string inspection, production tree byte identity,
  full task-range check-evidence-diff, OS witness inventory and fresh merge-tree.
  No changed OS branch or startup dependency. Restart: none. No rc startup run
  is applicable to this test/doc-only diff. No client change or client test row.

## Performance budget

The brief explicitly requires one native incremental compile before checkpoints;
that and the checkpoint-tool bootstrap are listed exceptions to the closed rows.
Both take separate build-slot leases, UseAppHost=false and TEMP=/tmp. The tool
then makes one cold build shared by three serial rows. Every run is bound to
committed HEAD using --expected-source-sha; no source edit occurs in flight.
Review d67a21b1 measured the old landing row at 100.438 seconds execution plus
195 seconds admission on Linux; this sample is not p95. Landing and guard row
estimates below are provisional, with cold build/admission dominant. Unit has no
measured p95; CARD-1720/Debug 86a941a3 tracks the >30-minute incident. Its existing
15-minute estimate derives a 45-minute row allowance; no timeout is widened here.
The tool derives total allowance from these estimates and uses bounded admission.
Record per-test durations in fresh TRX under .antiphon/checkpoints/<run-id>/;
phase and external-call timing absent from existing fixtures remains unknown.
Reuse assembly-owned PostgreSQL readiness and isolated schemas. No new setup or
timeout is introduced in the witnesses. No full assembly or namespace run.
No unchanged green row repetitions. If Unit exceeds 30 minutes, preserve the run
identity/timings for the caller's existing Debug obligation rather than retrying.

### Positive controls

All PC-1, PC-2, PC-3, PC-3b, PC-4, PC-5/M5, PC-6a and PC-6b remain pending
for method-scoped SourceLanding Mutation in
docs/superpowers/mutations/2026-11-05-card-1715-controls.md. Code does not execute
deliberate mutants, so it cannot claim a red SHA or discharge their cycles.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1715-followups/` | landing | `/*/*/(AgentTaskLandHalfResetTests*)\|(AgentTaskLandRegistrationSafetyTests*)\|(AgentTaskLandSourcePersistenceTests*)\|(LandSourceIdentityTests*)\|(SlowTestRegistryMergeTests*)/*` | V-1,V-2,V-3,R-1,R-2,R-3 | all listed, 0 failed | 130 | 6 | true |
| CP-2 | S1 | CP-1 | guards | `/*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | V-4,R-4 | all listed, 0 failed | 1 | 4 | true |
| CP-3 | S1 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | V-4,R-4 | >= 1 executed, 0 failed | 1 | 15 | true |
