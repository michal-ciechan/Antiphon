# CARD-1562 first slice: session-owned read-only HTTP fixture

Code owner/landing task: 3dc95294. Base: 4497af9b48136a8003def54331f49ac66fd2dec9.
The direct-Code brief authorizes choosing the smallest safe slice. No existing CARD-1562
plan was found. This document records that choice and its closed verification selection.

The assembly already owns one lazy PostgreSQL container, one migrated/cached template,
and private database clones. Do not rebuild those mechanisms. The scoped-list HTTP class
starts a host and seeds the same fleet for each read-only test. Share that immutable
host/database/seed through TUnit's PerTestSession data source, with disposable per-consumer
HTTP clients. Keep mutation cases on private cloned databases. No mutable host is promoted
to shared state; this is a bounded first slice, not repository-wide R3 completion.

Deferred: mutable scoped-list worlds, empty/none variants, other HTTP classes, AppHost,
shared Git setup, cross-launch container leasing/invalidation. These need separate measured
slices; no fleet-wide speedup or next-rc qualification claim is made.

## Performance budget

One baseline/candidate pair, same full affected class and recorder settings. Baseline adds
a sharing assertion and seed span only; it must fail on distinct real factory instances.
Existing host-startup/shutdown and DB clone phases plus FixtureSetup/seed record setup.
Record driver wall, TRX durations and fixture phase totals separately. Unknown readiness,
individual database-call attribution and host load remain unknown. The paired sample is
provisional, not p95; CARD-1562 tracks later measurement. No timeout/assertion is loosened.
Provisional row/build deadline 10 minutes, total 12 minutes per run, below the 30-minute
Debug threshold. One extra slot-gated isolated tool bootstrap build is necessary to run
the checkpoint tool. No whole Unit/assembly run: the explicit minimal-testing brief
overrides the default Final scope. Full affected class is 11 original methods plus the
sharing guard (12 cases); no downstream production dependency changes.

The recorder requires an absolute root; row environment binds this checkout explicitly. Timing roots: .antiphon/c1562/baseline/timing and .antiphon/c1562/candidate/timing.
TUnit/TRX is authoritative. Preserve failed receipts, exact source SHA and clean provenance.

## Verification design

V-1: two read-only consumer leases reference the same real host and return the exact seeded
fleet. New guard must fail before sharing; no constant/self-comparison oracle.
V-2: all original HTTP membership, exclusion, validation, empty/none and summary assertions
remain green, including private mutating cases, in the same test session as shared readers.
R-1: disposal of a read-only lease must not shut down the session-owned factory (guard will
dispose one lease then query through another after implementation).
R-2: mutation stores remain private; candidate guard will mutate one private fleet and
independently query the shared fleet to prove unchanged membership.
Manual: compare timing phase counts/totals and verify branch merge-tree against master.
PC-1: pending SourceLanding Mutation, remove shared reuse, exact sharing guard.
PC-2: pending SourceLanding Mutation, dispose borrowed factory, exact sharing guard.
PC-3: pending SourceLanding Mutation, route a mutating case to shared fleet, exact isolation guard.
Mutation owns deliberate mutants/red-restore-green, even if controls need refinement.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1562-baseline/` | baseline | `/*/*/AgentTaskScopedListEndpointTests/*` | V-1 red witness; V-2 baseline | 11 passed, 1 sharing assertion failed | 12 | 5 | true | ANTIPHON_TEST_PHASE_ROOT=/work/worktrees/task-3dc95294/.antiphon/c1562/baseline/timing;ANTIPHON_TEST_PHASE_RUN_ID=c1562-baseline;ANTIPHON_TEST_PHASE_GROUP_ID=scoped-list;ANTIPHON_TEST_PHASE_ATTEMPT=1;ANTIPHON_TEST_PHASE_SUITE=Antiphon.Tests |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1562-candidate/` | candidate | `/*/*/AgentTaskScopedListEndpointTests/*` | V-1, V-2, R-1, R-2 | all listed, 0 failed/skipped | 13 | 5 | true | ANTIPHON_TEST_PHASE_ROOT=/work/worktrees/task-3dc95294/.antiphon/c1562/candidate/timing;ANTIPHON_TEST_PHASE_RUN_ID=c1562-candidate;ANTIPHON_TEST_PHASE_GROUP_ID=scoped-list;ANTIPHON_TEST_PHASE_ATTEMPT=1;ANTIPHON_TEST_PHASE_SUITE=Antiphon.Tests |

Baseline red is intentional and remains historical. Candidate supersedes it after the fix;
do not rerun baseline green on the fixed source or claim the baseline as acceptance.
