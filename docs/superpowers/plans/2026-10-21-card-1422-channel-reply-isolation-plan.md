# CARD-1422: settlement is not blocked by a failing channel reply

Base: `5faed6dbb36189f21fa0f6e89c40dc57185f74a1` (task d1e0f472's CARD-1416 fixture migration).
Slices: S1 tests `8b2213098` (witnesses, constants only), S2 fix and invariant doc `f14acbc06`.

## Decisions

- Since CARD-1259 a phase that throws keeps its action and ends the drain page. A throwing
  `ChannelReplyDispatcher` at phase 4 therefore held settlement (phase 6) and the `catchup`
  action behind it, indefinitely while the channel kept failing.
- Only the channel phase is isolated. When it throws, `SessionRuntimeActionCoordinator` saves a
  separate `channel` debt row (same session and sequence, attempt 1, capped 1/2/4/8/16/30 s retry)
  in the same `SaveChanges` as the action's phase advance, then runs phases 5 to 7 as before.
- Replies keep transcript order: a route action that reaches phase 4 while earlier channel debt
  is owed saves its own debt row behind it without dispatching. Head selection admits only the
  earliest due debt row. A failed debt retry pushes later debt to its due time and continues the
  page; the not-due debt is skipped by the next selection.
- The queue phase (coordinator recheck and `SessionMessageQueueService.OnTurnEndCoreAsync`)
  excludes channel debt from "unfinished routing": the dispatcher resolves each turn from stored
  rows (CARD-0068 window), so input delivery does not depend on reply publication.
- Late-confirm publication (phase 9) is a channel publication, so it waits (retained with the
  normal retry) while channel debt at or below its sequence is owed.
- Every other failed phase still retains its action and holds later actions, unchanged.

## Verification design

- V-1 `AgentTaskCatchUpSettlementTests.catch_up_settles_even_when_channel_reply_dispatch_throws`
  goes green unchanged. Red evidence: it is the open red recorded at base `5faed6dbb` (CARD-1416
  plan, CP-2), failing for this reason.
- V-2 `SessionRuntimeActionRecoveryTests.C1422_failed_channel_reply_is_retried_in_order_after_the_dispatcher_recovers`:
  with a failing producer the turn's other actions pass phase 4 and a debt row exists; a second
  turn's reply does not overtake the owed first; after recovery both publish, in order, and no
  debt remains. Red at S1 (the failed action stays at phase 4).
- V-3 `AgentTaskCatchUpSettlementTests.a_throwing_settlement_phase_still_holds_the_later_actions_in_order`:
  a throwing settlement keeps its action at phase 6 and the later `catchup`/`transcript` actions
  unexecuted. Green at S1 and S2 by design; it guards against widening the isolation.
- V-4 no regression in the CARD-1259 suites and the Unit lane.

PCs pending for SourceLanding Mutation (method-scoped):

- PC-1 rethrow at phase 4 instead of saving debt (V-1, V-2 red).
- PC-2 swallow the channel failure without saving debt (V-2 red: no reply after recovery).
- PC-3 drop the earliest-debt filter in head selection, or dispatch at phase 4 despite owed debt
  (V-2 red: second reply overtakes).
- PC-4 isolate every phase failure, not just phase 4 (V-3 red).
- Missing controls for Mutation to discover: the phase-9 late-confirm wait for owed debt, and the
  queue-gate exclusion of channel debt, have no dedicated witness.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1422/` | catch-up | `/*/*/AgentTaskCatchUpSettlementTests/*` | V-1, V-3 | all listed, 0 failed | 6 | 3 |
| CP-2 | S2 | CP-1 | coordinator | `/*/Antiphon.Tests.Application/(SessionRuntimeActionRecoveryTests*)\|(SessionRuntimeActionTests*)\|(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionDeferralTests*)\|(SessionRuntimeActionTemporaryBlockTests*)/*` | V-2, V-4 | all listed, 0 failed | 20 | 10 |
| CP-3 | S2 | CP-1 | channel | `/*/Antiphon.Tests.Application/(ChannelReplyDurabilityTests*)\|(ChannelReplyLateConfirmDispatchTests*)\|(AgentTaskMidTurnRefineTests*)/*` | V-4 | all listed; failures limited to the 8 inherited `AgentTaskMidTurnRefineTests` C0491/C1169 cases | 36 | 8 |
| CP-4 | S2 | CP-1 | runtime | `/*/*/AgentSessionRuntimeTests/*` | V-4 | all listed, 0 failed (1 skipped by attribute) | 44 | 7 |
| CP-5 | S2 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | V-4 | all listed; failures limited to inherited `DispatchBaseWarningRosterTests.C1155_RosterNamesEveryDispatchWarningE2EMethod` | 1000 | 10 |

### Unlisted

One compile build (`bin-c1422c/`) and two smoke runs from it before the checkpoint
(`AgentTaskCatchUpSettlementTests/*` 6/6, `SessionRuntimeActionRecoveryTests/C1422*` 1/1), to avoid
spending the checkpoint on a broken test. The output was deleted.

### Run 1 and rerun

Run `20261021-200721-5b39` at `6830d0501`: CP-1 and CP-4 green. CP-3 red only in the 8
`AgentTaskMidTurnRefineTests` C0491/C1169 cases, which fail identically at base `5faed6dbb`
(unlisted base run). CP-2 red in one case, `C1259_shutdown_joins_and_restart_reconciles_submission(reconnect-cohort)`
(10-second `UntilAsync` deadline while CP-2 and CP-3 overlapped). It does not reach the channel
path and passes at base in isolation. CP-5 hit its 15-minute row deadline with output still
progressing at minute 11. Its 4-minute estimate came from the stale 2026-09-10 profile, so it is
corrected to 10 (deadline 30); no test timeout or assertion changes. C1155 failed in it and fails
identically on the pre-fix build. Rerun once: CP-2 and CP-5.

### Cost

Ordinary floor about 30 minutes; one run of all five rows.
