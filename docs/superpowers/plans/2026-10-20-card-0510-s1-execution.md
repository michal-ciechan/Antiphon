# CARD-0510 S1 execution selection

Original Code/landing owner: `b8bef420`. Base:
`6fcb2f952623e214042d75d1e2cbc17d8d00bec3`.
The accepted [design](2026-10-20-card-0510-standing-restart-escalation-plan.md)
defines S1 as durable accounting, not fresh launch authorization.
S2 (one authorized fresh conversation) and S3 (queue safety/audit/alerts) remain deferred.
The requested core witness therefore proves three admitted failed resumes and healthy
reset, retaining the old conversation in this slice.

The brief and the caller's refinement reduce S1 to one red witness and one narrow
green group on the new S1 tests only. Original CP-1/CP-2/CP-3's new tests are
merged into CP-1 below: V-1/V-2, including migration/model agreement. Original
CP-4 and existing R-1/R-5/R-6 are not executed under that refinement.
Unit and the existing full accounting/ownership/supervision classes are not run;
this is not a claim that the default Final/Full verification gate is complete.
Original CP-5 through CP-15, V-3 through V-8 and R-2/R-3/R-4 belong to S2/S3 and
are not certified by S1. No native, client, live-stack or external delivery acceptance
is required for accounting. No full-assembly selection is used.

V-1's main witness combines initial-crash exclusion, three real accepted failures,
duplicate ticks, preserved legacy counts, automatic acceptance and exact healthy boundary.
Additional S1 cases cover nonqualifying outcomes, generation matching, synchronous
post-commit observation, provider recreation, reset variants and migration defaults.
The original nine-argument V-1 roster is consolidated into these behaviors; actual
execution counts must come from fresh TRX. This is not completion of all 147 original
S1-S3 executions.

One red build/run against the scaffold commit and one green group against the implementation
commit; no repeats after green. All use `--row-timeout 40m`, committed
`--expected-source-sha`, `--serial` and trailing wildcard selectors. EF CLI generation
is an additional required build, through build-slot, using isolated configuration
`c510-ef` because the EF CLI does not accept OutputPath.
Tool bootstrap also uses build-slot and an isolated OutputPath.
Expected green cost: one build 5-7 minutes, new integrations 1-3 minutes.
PC-1 through PC-89 and all their variants remain unbound and pending for Mutation;
no deliberate mutants are authorized in this task.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-0 | W | `tests/Antiphon.Tests -> bin-c510-red/` | red-witness | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests*/C510_counts_only_distinct_admitted_failed_resumes*` | V-1, V-2 witness | one assertion failure at missing admitted count | 1 | 4 |
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c510-green/` | accounting | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests*/*` | V-1, V-2, migration/model agreement | all 23 new S1 results, no failures/skips | 23 | 8 |
