# CARD-1836 S5b admission verification amendment

Code owner 1180038b; base b57583cee95b75c62edd496247e2361c7cc4e034.
Owner: [activation plan](2026-11-12-card-1836-incremental-activation-plan.md), S5b, V-6 and R-1.
S3b, S4a and S5a are landed. This dispatch implements S5b next. No launch caller,
activation flag, nightly default, or policy input changes are authorized.

The complete-provenance test proves diagnostic eligibility before the release gate;
actual admission additionally requires complete algorithm coverage. CARD-1882's
release graph is incomplete, so even an otherwise eligible fixture requires Full.
No caller-supplied switch can declare that graph complete. CARD-1881 collisions
are dirty before S3b recomputation, including the real S2 short-id collision pair.

V-6: all seven RcHistoricalAdmissionTests methods, including per-ingredient negative
matrices, independently rehashed S3b records, fresh S5a bundle verification, terminal
TRX/roster joins, origin-chain refusals, seven-day boundary and inert import/shards.
R-1: RcIncrementalLedgerTests and RcLedgerDurationTests, unchanged strict S1 readers.
Adjacent smoke: S2 IDs, S3b digest/case manifest, S8a forced Full. Native Windows
path/handle evidence remains owed by the orchestrator under CP-17. No OS-selected
production behavior or startup change is intended. Restart none.

Every new integration class uses the existing process limiter and harness-owned
system temporary fixtures. CP-90/91/92 cover the resulting impacted census rows.
CP-93/94 cover the requested index/envelope guards. Build the derived-index driver
in the native default output before regenerating; commit an index delta separately.
Any additional impacted match will amend this closed list before execution.

Red-first diagnostics invoke the new script cases through build-slot before the
implementation exists. These explicit unlisted runs prove missing-feature red;
the final closed rows verify the committed implementation and nonzero fresh TRX.
No deliberate mutants are executed in Code. PC-V6-manifest, PC-V6-inventory,
PC-V6-provenance, PC-V6-terminal-roster, PC-V6-origin-history, PC-V6-anchor,
PC-V6-algorithm-coverage and PC-V6-inert remain pending for SourceLanding Mutation
(all variants in the candidate document). Operator waived Code mutation execution.
V-1..5, V-7..18, R-2..7 remain in their owning slices, never credited here.

Provisional cost: 10m core plus 4m guards, 5m index, 1m envelope, 2m adjacent,
3m two other project builds. Run core/main rows in one serial tool invocation,
then CP-91/92 separately to keep Pty execution sequential. Method budget 60s,
child 45s plus 10s cleanup; no timeout widening. No repeated green proof.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-6 | S5b | `tests/Antiphon.Tests -> bin-c1836-s5b/` | linux-admission | `/*/*/(RcHistoricalAdmissionTests*)\|(RcIncrementalLedgerTests*)\|(RcLedgerDurationTests*)/*` | V-6 R-1 | all named methods/classes, no failures/skips | 7 | 10 |
| CP-90 | S5b | CP-6 | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, no failures/skips | 79 | 4 |
| CP-93 | S5b | CP-6 | linux-index | `/*/*/DerivedImpactIndexTests/*` | derived index guard | all methods, no failures/skips | 27 | 5 |
| CP-94 | S5b | CP-6 | linux-release-envelope | `/*/*/ReleaseImpactEnvelopeTests/*` | release envelope guard | all methods, no failures/skips | 4 | 1 |
| CP-95 | S5b | CP-6 | linux-adjacent | `/*/*/(RcBoundaryChunkIdTests*)\|(RcChunkInputDigestTests*)\|(RcForcedFullTests*)/*` | named adjacent contracts | all methods, no failures/skips | 24 | 2 |
| CP-91 | S5b | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s5b-pty/` | linux-pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, no failures/skips | 2 | 1 |
| CP-92 | S5b | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s5b-runner/` | linux-runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, no failures/skips | 2 | 2 |
