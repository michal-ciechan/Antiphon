# CARD-0418 round 35: recovery and remaining ordinary verification

This continues [round 34](2026-09-29-card-0418-round-34-evidence.md) from
`c87a5641a7756d2c84958be096593cb95a990b5e`. Round 34 established the
durable source/input/output hashes, task and correlation ownership, publication
stamps, post-restart attention and actual broker acceptance evidence for V-15.
It left the dispatched-worker transcript/process reconciliation at C-3 and
normal worker settlement across death at C-4 open. This round records any new
receipts below. The inherited CP-8 `PinnedAgentKindTests.T1/T2` failures for
`codex_desktop_unqualified` remain outside this card's changes.

## V-15 C-3/C-4

The dispatched C-3 row now keeps the killed dispatcher's task/session identity,
records a marked transcript closing turn, and settles that existing task through
`AgentTaskReplyService`. The worker writes its output manifest under the actual
request's output directory. A second process dies at the C-4 pre-claim boundary
after normal settlement and before pump observation. A fresh probe then validates
the output and must publish the converted attachment. The test checks the
retained task id/session, transcript marker, output hash and accepted payload.

An initial CP-6 run on `5a94ecc99720c712703ef661b65ca970ff6d3300` executed
29 / passed 28 / failed 1 at `.antiphon/checkpoints/r35-cp6-a/`. The C-4
barrier was initially after the pump's claim; the killed process left a five-minute
lease on a two-minute conversion deadline, so immediate recovery stayed
`Converting`. This did not validate the normal worker output. The barrier moved
to just before claim; that cut still follows committed transcript settlement and
precedes pump observation. Post-claim death with a short configured conversion
deadline is a separate lease/deadline behavior and is not claimed by this case.

## Checkpoint receipts

Pending. PC-1–PC-30 remain reserved for method-scoped SourceLanding Mutation;
ordinary and nightly green do not discharge them.
