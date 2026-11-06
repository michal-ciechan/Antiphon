# CARD-1729 P0-G resolver repair verification addendum

Code task a342b58a; original landing owner 742b9c5c-9e25-4fac-910e-799803e461c9.
Baseline: 93efbc748def106ce2c2ca15d56589142c46986b. Repairs Review
5960094e (ad6009c5c), B-1/B-2. This extends the L-variants of frozen
`origin/feat/card-task-65e6f8b0:docs/superpowers/testdesign/2026-11-05-card-1729-phase0-testdesign.md`.
All nine method names and their existing assertions remain unchanged.

| Case | Added variants and invariant | Oracle |
|---|---|---|
| L5 | color-color-fires; parameter-color-color-fires; local-color-color-fires; creation-with-shadowing-member-fires; qualified-with-shadowing-member-fires; qualified-expression-with-shadowing-member-fires; global-qualified-with-shadowing-member-fires; qualified-with-own-nested-type-fires | Each valid synthetic Core file produces exactly `core/Value.cs -> Svc @ app/Svc.cs` and fails strict mode. Member/local/parameter and own-type names cannot hide a type-position or qualified reference. All variants are evaluated before aggregating missing findings. |
| L6 | findings=0:initializer-member; findings=0:with-member; findings=0:named-argument; findings=0:property-pattern; findings=0:attribute-member | Each valid synthetic input produces zero unlisted findings and ambiguities and passes strict mode. Member labels cannot become type references. All variants are evaluated before aggregating false findings. |

Red-first: commit these variants with the unchanged engine, run CP-1, require L5
and L6 assertion failures (not compilation, fixture or discovery failures).
Then repair the resolver and reseed only the 67 stale rows plus the single real
LandWorkspace row under P0-C5. Final CP-1 must print zero violations/stale rows.
Parity is scoped by the repair brief to added test methods: zero methods added,
zero removed, nine retained. No discovery driver is needed for in-body variants.

V-1 and R-1 bind CP-1 and every new L5/L6 variant. V-2 real-tree strict ratchet
activation stays with P0-G2; synthetic strict acceptance is exercised here.
V-3..V-12 and R-2..R-8 belong to other Phase 0 slices and are not this repair.
The named full affected integration class is SlowTestRegistryMergeTests (CP-91).
CP-91 also runs the named full classification/registry/tripwire/native-policy classes;
CP-92 runs the full mechanical contract class.

Mutation retains PC-1..PC-12 from P0-G and every variant, all pending. PC-11/12
still require P0-G2. New PC-36: remove only the member-label syntax exclusion;
method-scoped L6 must fail its five named findings=0 variants. New PC-37: restore
the unconditional own/declared-name exclusion; method-scoped L5 must fail its
eight shadowing variants. SourceLanding Mutation owns deliberate edits,
red/restore/green cycles and missing-control discovery. No PC is discharged here.
The rest of the phase's PC-13..PC-35 remain with their owning slices.

## Performance budget

One native baseline compile first, as requested (unlisted prerequisite run).
Each checkpoint group is committed and uses at most three rows. Exact rows run
once per changed source; only red rows are repeated. UseAppHost=false and TEMP=/tmp.
The checkpoint tool owns isolated output/provenance and each driver's build-slot lease.
Native direct builds use default outputs. No timeout widening or assertion loosening.

CP-1 baseline Review: L4 7.91 seconds cold, parser about 4.6 seconds on 1,349
files. Cold build comparator p50 432s, p95 817s (n=11, frozen TestDesign).
Guard rows are provisionally under two minutes excluding build/admission; build
cap 20 minutes, slot admission 12 minutes. Shared repository setup should record
census, read and analysis separately; fixture lives once per class, synthetic
inputs are isolated in memory. Evidence: ignored `.antiphon/checkpoints/<run>/`.
Missing external-call spans are unknown, not zero.

Final whole Unit invariant: every Unit-tagged test continues to behave after the
shared syntax resolver/test-fixture changes. This selection is unbounded by class
name and includes existing script/process tests; it omits delivery, landing,
lease and persistence integration suites. It is required by the Final profile.
Historical whole-Unit sample: 2,310.6s host, 3,983 total (task 78dc9321,
`.antiphon/c1008-evidence-round5/unit-outcome.md`); no measured p95 exists.
CP-93 estimate 20 minutes is provisional, with a finite 45-minute row cap and
derived total allowance. Its over-30-minute exposure needs caller Debug triage
under R1; this delegate cannot dispatch additional agents. Record actual cost
and do not repeat a completed green lane. CP-93 runs separately from the three
targeted rows, so no run exceeds the three-row cap.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | repair | tests/Antiphon.Tests -> bin-a342b58a-guard/ | layering-guard | /*/Antiphon.Tests.Infrastructure/C1729LayeringGuardTests/* | V-1, R-1; L1..L9 and new variants | 9 methods, zero failures/skips after repair; inventory printed | 9 | 10 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-1 |
| CP-91 | repair | CP-1 | classification | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)/* | classification and registry | 50 cases, zero failures/skips | 50 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-91 |
| CP-92 | repair | CP-1 | mechanical-contract | /*/*/(MechanicalPcContract*)/* | mechanical instruction contract | 11 cases, zero failures/skips | 11 | 2 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-92 |
| CP-93 | final | tests/Antiphon.Tests -> bin-a342b58a-unit/ | whole-unit | /*/*/*/*[Category=Unit] | Final whole Unit lane | nonzero executions; every red/skip inspected | 1 | 20 | true | ANTIPHON_TEST_PHASE_GROUP_ID=CP-93 |
