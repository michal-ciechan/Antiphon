# CARD-0418 round 26: held converter and source runtime progress

This continues the [round-25 ledger](2026-09-29-card-0418-round-25-evidence.md).
The final tested code commit is `695143cb239126f47f9a5d811025b94b91626a63`.
No shared-stack restart, actual destination, land, or SourceLanding Mutation was used.

## V-8 actual runtime evidence

`AgentTaskReplyIntegrationTests.Deferred_is_durable_and_releases_runtime` is the
named CP-2 integration result. It settles a real Docs source task from a
transcript, checks its actual completion note and exact Markdown source bytes,
and delivers that note to the inbound session. The inbound session answers the
note through `AgentSessionRuntime.ObserveTranscriptAsync`. On the committed
outbound admission boundary, an independent pump creates the linked converter
task and pauses its next real `Converting` observation. The converter barrier
stays closed while `ObserveTranscriptAsync` finishes within a five-second wall
watchdog and the runtime submits a queued next prompt. The fake adapter writes
the matching `UserPrompt` transcript row; the test checks that row and the
queued message's `Sent` state, not just the adapter input buffer.

With the converter still paused, a fresh `AppDbContext` reads the `Converting`
intent, source session/task identity, conversion task FK, 64-digit input hash,
hash-verified frozen answer and exact source attachment, plus the completion
note's correlation FK. `ChannelReplySettledAt`, the source task's
`DeliverableDeliveredAt`, and the channel's `LastReplyAt` are all null. The
fake producer has zero entries. A separate connection acquires the delivery row
with `FOR UPDATE NOWAIT` and updates an unrelated project row while the worker
is held. The barrier is released in `finally`.

Together with round 25's named
`ChannelOutboundDeliveryTests.Deferred_is_returned_only_after_intent_and_correlation_commit`
(CP-5: before-commit invisibility, post-commit visibility, released locks,
`Deferred` outcome), this closes **whole V-8**. R-5 remains open because V-16's
multi-target acceptance and retry assertions are incomplete. This is local
fake-destination evidence, not V-25 actual-destination acceptance.

## Checkpoint trail

- First CP-1/CP-2 run at `1440a3b6cd45d1bed4f3cb138fd57fa90258dead`:
  CP-1 3485/3485 with 33 platform skips; CP-2 371/372. The new fixture
  omitted an answering `UserPrompt` after the fake adapter's synthetic
  delivery `TurnEnd`, so the converter hook was never reached. The fixture was
  corrected and that same row rerun.
- Second CP-1/CP-2 run at `f066a09474c9de4bc14620d1ce03cda11a92731a`:
  CP-2 372/372. CP-1 3484/3485: the previously recorded
  `OperatorShutdownCoordinatorTests.Stop_proceeds_when_the_drain_bound_expires`
  one-second timing edge measured 0.9987036 seconds.
- Complete CP-1 through CP-13 run at that same commit:
  `.antiphon/checkpoints/20260929-124426-c5e5/`. The named V-8 method was
  Passed and non-skipped in `rows/CP-2/run.trx`. CP-1 passed 3485/3485.
- The final complete CP-1 through CP-13 run after the fixture's temporary
  directory cleanup is `.antiphon/checkpoints/20260929-130508-4ca3/` on
  `695143cb239126f47f9a5d811025b94b91626a63`. Its CP-2 TRX again records
  the named V-8 method Passed and non-skipped. Every row result below is from
  this final run.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3485 | 0 | 33 | Green |
| CP-2 source settlement | 372 | 372 | 0 | 0 | Green; V-8 runtime |
| CP-3 policy/schema | 32 | 32 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 15 | 15 | 0 | 0 | Green; V-8 admission |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with broker opt-in |
| CP-12 client | 26 | 26 | 0 | n/a | Green |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

The checkpoint tool ran no unlisted row. Its bootstrap build had a separately
granted build slot and the existing `TaskOwnerGuard.cs` CS8602 warning. The
final run's exit 1 is only CP-8's inherited pair. Native report, TRXs,
failure detail and slot records are in its run directory above.

## Remaining gates

Closed IDs remain V-1–V-6, V-8, V-19–V-22, V-24, R-1 and R-12. The ordinary
open set is **V-7, V-9–V-18, V-23, R-2–R-11, R-13–R-14**. The
[round-17 inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions),
as amended by rounds 18–25 and the closure above, identifies each missing
matrix. V-25 still requires its later authorized actual-destination gate;
PC-1–PC-30 still require method-scoped SourceLanding Mutation. No ordinary
checkpoint discharges those later gates.
