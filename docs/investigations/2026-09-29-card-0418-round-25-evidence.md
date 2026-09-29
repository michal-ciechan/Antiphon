# CARD-0418 round 25: admission commit and released claim

This continues the [round-24 ledger](2026-09-29-card-0418-round-24-evidence.md).
The tested code commit is `b983cb7f92661666342ef096929d43c1102c6213`.
No shared stack, real destination, land, or SourceLanding Mutation was used.

## V-8 admission boundary increment

The CP-5 TRX at
`.antiphon/checkpoints/20260929-115221-0a87/rows/CP-5/run.trx`
records `ChannelOutboundDeliveryTests.Deferred_is_returned_only_after_intent_and_correlation_commit`
as Passed and non-skipped (CP-5: 15/15). The real `ChannelOutboundService`
is paused first immediately before commit, then again immediately after commit
but before it returns `Deferred`.

At the first barrier, a fresh `AppDbContext` sees no delivery and no correlation
link; the caller is still waiting and the fake producer has sent nothing. At
the second barrier, the fresh connection reads the Pending intent, its 64-digit
input hash and the hash-verified frozen answer bytes. It sees the correlation
linked to that intent, `ChannelReplySettledAt` and `LastReplyAt` null, and no
producer send. A `FOR UPDATE NOWAIT` on the delivery succeeds from the second
connection, and that connection updates an unrelated project row. This checks
that the sender has released its database claim before returning. Releasing
the barrier yields `Deferred`.

This closes the **admission/transaction sub-assertions of V-8**, including the
prior before-commit barrier. Whole V-8 remains open: the test does not run the
source session runtime through settlement and a next queued prompt while a
real converter task is held. The `DeliverableDeliveredAt` source-task stamp
and producer-entry count also need a named actual-run oracle in that fixture.
It is not evidence that the runtime is released under a held converter.

## Final checkpoint run

The complete CP-1 through CP-13 closed list ran once on the tested code commit
in `.antiphon/checkpoints/20260929-115221-0a87/`. Its `report.md`, row TRXs,
console logs and slot records are the native results. Every row held a granted
build slot. The tool ran no unlisted row. The separately leased tool bootstrap
build succeeded with the existing `TaskOwnerGuard.cs` CS8602 warning.

| Row | Executed | Passed | Failed | Skipped | Verdict |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3485 | 3485 | 0 | 33 | Green |
| CP-2 source settlement | 371 | 371 | 0 | 0 | Green |
| CP-3 policy/schema | 32 | 32 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 15 | 15 | 0 | 0 | Green; V-8 admission row |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Inherited T1/T2 `codex_desktop_unqualified` |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green |
| CP-11 gateway wire | 125 | 125 | 0 | 0 | Green with broker opt-in |
| CP-12 client | 26 | 26 | 0 | n/a | Green |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

Tool exit 1 is the inherited CP-8 red only; its exact T1 and T2 failures have
the same `codex_desktop_unqualified` cause recorded in round 24. This test-only
slice introduced no checkpoint failure.

## Remaining gates

Whole V-8 is still open. Previously closed IDs remain V-1–V-6, V-19–V-22,
V-24, R-1 and R-12. The ordinary open set remains **V-7–V-18, V-23,
R-2–R-11, R-13–R-14**. The
[round-17 inventory](2026-09-28-card-0418-round-17-evidence.md#remaining-ordinary-vr-assertions),
as amended by rounds 18–24 and the V-8 sub-assertions above, identifies the
missing behavior. V-25 still requires separately authorized actual-destination
acceptance, and PC-1–PC-30 still require method-scoped SourceLanding Mutation.
