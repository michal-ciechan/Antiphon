# CARD-0418 round 11: machine/control route and full checkpoint sweep

This continues the [round-10 record](2026-09-28-card-0418-round-10-evidence.md) and the [round-8 open-case ledger](2026-09-28-card-0418-round-8-evidence.md). The tested source commit is `b3f3b57a401bcda814cc4509549f555bcc0ae4b8`. This is a Final-profile checkpoint sweep, not a claim that the ordinary V/R matrix, live acceptance, or method-scoped positive controls are complete.

## Added assertion

`ChannelOutboundDeliveryTests.Deferred_intent_freezes_route_and_only_publication_stamps_correlation` now exercises a Markdown control notice while an EveryAgentReply conversion profile is active. It asserts immediate publication under the notice's own thread and no third intent. It then sends a machine completion, asserts a distinct deferred `machine` intent, and publishes its frozen body and thread after the pump runs. This narrows V-6/V-7/V-14 and R-4/R-10. It does not replace the named full main/trailing/machine, real control-caller, or T1/T2 matrices. CP-5 passed 6/6 on the committed slice in `20260928-135938-c262` before the full sweep.

## CP-1 through CP-13 Final-profile sweep

The checkpoint tool ran the plan's full closed list on the same source commit in `20260928-140159-6802`. Native report, TRX, command logs, and CP-8 failure details are under `.antiphon/checkpoints/20260928-140159-6802/`. All build/test drivers held granted host slots; the tool reported no unlisted build or test commands. A separate tool bootstrap build used `scripts/build-slot.ps1` and had one existing CS8602 warning in `TaskOwnerGuard.cs`.

| Row | Executed | Passed | Failed | Skipped | Result |
|---|---:|---:|---:|---:|---|
| CP-1 Unit | 3467 | 3467 | 0 | 33 platform skips | Green |
| CP-2 source settlement | 340 | 340 | 0 | 0 | Green |
| CP-3 policy/schema | 29 | 29 | 0 | 0 | Green |
| CP-4 file boundary | 57 | 57 | 0 | 0 | Green |
| CP-5 purpose/deadline | 6 | 6 | 0 | 0 | Green |
| CP-6 crash/transport | 16 | 16 | 0 | 0 | Green |
| CP-7 routing/attention | 320 | 320 | 0 | 0 | Green |
| CP-8 existing deadlines | 71 | 69 | 2 | 0 | Red: inherited `PinnedAgentKindTests.T1` and `T2` `codex_desktop_unqualified` refusals; round 7 proved the same result on its prior baseline. |
| CP-9 renderer | 19 | 19 | 0 | 0 | Green |
| CP-10 real browser | 1 | 1 | 0 | 0 | Green on Linux |
| CP-11 gateway wire | 122 | 122 | 0 | 0 | Green |
| CP-12 client | 26 | 26 | 0 | 0 | Green; `CLIENT TESTS EXIT CODE: 0` |
| CP-13 client bundle | n/a | n/a | 0 | n/a | Build exit 0 |

CP-8 was not called green. Its two failures match [round 7](2026-09-28-card-0418-round-7-evidence.md), without a source change in this round to the pinned-kind path. The tool's overall exit was 1; 12 rows were green and one red. CP-1's 33 platform skips do not provide Windows execution evidence for the browser child-tree fixture.

## CARD-0784 and remaining gates

After this round started, `git fetch origin master` advanced `origin/master` to `f259593a1` (`fix(card-0784): route static logs through owned host sinks`). Its [owned-host evidence](2026-09-27-card-0418-code-evidence.md) records F-5 class 3/3 and the logging repair's assertion-red/restored-green run. That commit is on master, not in this worktree's tested commit. Its fixture covers admitted four-source PDF upload through broker/gateway/fake Slack and owned-host restart; it explicitly leaves V-24's automatic dispatcher routing, second conversation, and failing-tool fallback open. Do not count this sweep as integration testing that separate master change.

The [round-8 open-case table](2026-09-28-card-0418-round-8-evidence.md#remaining-ordinary-scope) and [round-10 narrowing](2026-09-28-card-0418-round-10-evidence.md#remaining-gates) remain the ledger, except for this round's narrow machine/control assertions and CARD-0784's landed partial F-5 result. Full P/Q X/Y/Z settlement and source provenance; all send shapes and withheld/real control callers; runtime release; worker-purpose companions and refusal/capacity/deadline races; remaining file/ZIP and serialized fallback faults; complete T1/T2, C-1–C-8, multi-target, attention/TTL and policy-race oracles; fresh DI/legacy warning; full monitor and near-default wire publication cases; and an actual R-14 evidence index/validator invocation still lack named complete evidence. The round-10 Windows browser child-tree fixture has only executed on Linux; dispatch the same committed source to a Windows host for its native timeout/cancellation assertions, without treating a Linux simulation as Windows evidence.

V-25 remains the later authorized live destination/model/receipt gate. PC-1–PC-30 remain pending method-scoped SourceLanding Mutation; no checkpoint or nightly green discharges one. This branch was not landed, no shared stack was restarted, and no live destination was sent a message.
