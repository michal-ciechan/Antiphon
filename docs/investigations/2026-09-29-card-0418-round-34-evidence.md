# CARD-0418 round 34: crash-cut hashes, ownership and attention

This continues the [round-33 evidence](2026-09-29-card-0418-round-33-evidence.md)
from `03e8eca1536204ac101ad9312b092d6222146f61`. Source/test commits are
`8363550b3`, `bde0432fb`, `6b3876862` and `0e877cd0d`. No shared stack,
actual destination, land, live provider or SourceLanding Mutation was used.

## V-15 ordinary verdict

**V-15 advanced but remains open.** The plan-named
`ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary`
now checks C-1 through C-3 after killing the identified probe process. It
independently hashes the serialized source reply and the durable input file,
checks intent/task counts, the task's project/agent link and dispatched session
owner, correlation FK and null publication stamps from fresh database contexts.
It checks that no delivery attention appears before a terminal hold and that
restart reuses one delivery and one task without losing its sealed input.

`Recovery_preserves_settled_worker_and_publication_boundaries` now checks C-4
through C-8 at the death cut and after a fresh process recovers: input and sealed
output hashes, task linkage/status, attempts, correlation/source/channel stamps,
producer acceptance count, bounded uncertainty reason and the attention row's
delivery/channel/task evidence. The separate actual Redpanda C-7 case checks
that the accepted record's UTF-8 hash is the frozen reply hash, then verifies
the Publishing cut and PublishUncertain recovery with no second broker record,
no success stamps, bounded reason and critical attention. These assertions use
the existing isolated schema, durable file store and independent process probe.

The red control changed the local attention projection to select Published in
place of PublishUncertain, without committing it. CP-6 ran its exact filter and
failed 8 of 29 tests on the new presence/absence attention assertions at
`.antiphon/checkpoints/20260929-221742-1641/`. The projection was restored;
the final full sweep below ran on the restored source. No stub test was added.

V-15 still needs the C-3 submitted/running task's normal transcript/process
reconciliation proof and C-4 normal worker completion across death; the current
probe refuses an external launch and the settled-worker row is seeded. The
per-cut hash/ownership/attention additions are complete, but those process
ownership and normal-settlement boundaries prevent closing the whole V-15 ID.
V-16, V-17, V-18 and V-23 were not started in this round.

## Checkpoint receipts and limits

The first affected-row run at `20260929-215633-3f03` failed to compile the new
test due to a shadowed local and missing DTO namespace; `bde0432fb` fixed both.
The next CP-1/CP-6 run at `20260929-215854-adda` had CP-1 green and three CP-6
failures because the new assertion read the attention headline instead of its
title; `6b3876862` corrected that, and CP-6 passed at `20260929-221054-2707`.
The final CP-1–CP-13 closed list ran on `0e877cd0d4e619d0cabebe7e3752e7a4a39910d4`
at `.antiphon/checkpoints/20260929-222515-d535/`. All runs used the
checkpoint tool and its build slot; no unlisted build/test commands ran.

| Row | Executed / passed / failed | Final ordinary receipt |
|---|---:|---|
| CP-1 | 3491 / 3491 / 0 | Whole Unit lane; 33 platform skips |
| CP-2 | 373 / 373 / 0 | Source settlement |
| CP-3 | 32 / 32 / 0 | Policy/schema |
| CP-4 | 64 / 64 / 0 | File boundary |
| CP-5 | 53 / 53 / 0 | Purpose/deadline |
| CP-6 | 29 / 29 / 0 | Crash/transport, including C-1–C-8 and actual broker C-7 |
| CP-7 | 320 / 320 / 0 | Routing/attention |
| CP-8 | 71 / 69 / 2 | Inherited `PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` refusals |
| CP-9 | 19 / 19 / 0 | Renderer |
| CP-10 | 1 / 1 / 0 | Headed browser PDF case |
| CP-11 | 125 / 125 / 0 | Gateway/adapter/monitor/broker classes |
| CP-12 | wrapper exit 0 | Two client files |
| CP-13 | build exit 0 | Production client bundle |

Whole ordinary IDs closed remain V-1–V-14, V-19–V-22, V-24, R-1, R-7, R-8,
R-10 and R-12. Still open: V-15–V-18, V-23, R-2–R-6, R-9, R-11 and
R-13–R-14. V-25 is the later authorized live gate. PC-1–PC-30 remain pending
method-scoped SourceLanding Mutation; ordinary and nightly green runs do not
discharge them.
