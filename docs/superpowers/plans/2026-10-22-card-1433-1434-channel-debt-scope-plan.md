# CARD-1433 / CARD-1434: channel debt per destination, a give-up bound, and the late-confirm hold

Base: master `9b39df4e065bb7267b9572cfd2c1acdf2b498e5b`. Builds on CARD-1422 (master fix
`4db9e7385`, land `fa142089d`, Review 6e5b8a50) and on the CARD-1435 witnesses
(`3ed74f74d`, `SessionRuntimeActionRecoveryTests.C1435_*`, in Review on
`feat/card-task-0b46acdb` at `022274402`). Platform Any; the checkpoints run on the default
runner lane (server2, Linux) with no `-Runner` and no `-Platform`.

Both cards are follow-ups the CARD-1422 Review disclosed; neither is a defect in CARD-1422's
own rule. CARD-1434: channel debt is ordered per session, so one reply that keeps failing
(a chat the bot can no longer post to, a provider outage) holds every later reply of that
session to every destination until the 30-minute correlation TTL raises `ChannelReplyLost`,
and the session is excluded from retention while any action row exists. CARD-1433: a
late-confirm publication that waits for an owed reply is retained at phase 9 and, because a
not-due head ends the drain page, holds the session's later queue actions, contrary to the
documented rule that next-message delivery does not wait for channel debt.

## Ground truth

| The card assumes | What the code does |
|---|---|
| Ordering is per session. | True. Head selection (`SessionRuntimeActionCoordinator.cs:162-167`) admits a `channel` row only when no earlier `channel` row exists for the session; the phase-4 wait (`:310-312`) takes the max `DueAt` of every earlier debt row, keyed by session only. `SessionRuntimeAction` has no destination column. |
| A permanently failing reply blocks later replies to every destination until `ChannelReplyLost`. | True, and the clock is the correlation's `SentAt` plus `PendingReplyTtlMinutes` (30; `ChannelReplyDispatcher.AbandonStaleCorrelationsAsync`, `:858-861`), swept per turn end and once a minute. The sweep settles the row and reports it as `TurnUnmatched` ("a turn completed ... but the dispatcher did not route it"), which misdescribes a publish failure. The debt row itself is cleared only on its next retry, when `DispatchAsync` finds no open correlation and returns `Empty` (`:402-407`). Attempts cap at 31 (`:196`) and the backoff at 30 s (`:216-217`), so a dead destination is retried about every 30 s for up to 30 minutes. |
| "Bad binding" is retried. | Only partly. A key with no provider/conversation split is reported `Unroutable` and settled at once (`:506-537`); a routable key whose producer rejects the send is a thrown produce failure (`:633-641`) and becomes debt. The give-up policy is for the second shape. |
| Data retention skips sessions with any action row. | True at `DataRetentionService.cs:191` (session purge), `:251` and `:290` (transcript purge, discovery and recheck), `:357` (queue prune). Channel-origin queue rows are also kept while `ChannelReplySettledAt` is null, independently of actions. |
| A waiting late-confirm publication holds later queue actions. | True. Phase 9 throws when debt at or below its sequence is owed (`:291-294`); the catch retains the action with backoff and returns `false` (`:193-203`); head order is `Phase >= QueuePhase` then `Sequence`, so the retained seq-N action precedes every later queue-phase action and, when not due, ends the page (`:185`). A *failing* late-confirm publication (`AgentSessionRuntime.cs:613-614`) throws the same way and holds the same actions. |
| Not in the card. | Every route action (`RoutesReplies`: a TurnEnd, or any AssistantText row, `AgentSessionRuntime.cs:428-429`) that reaches phase 4 while debt is owed saves its own waiting debt row (`:325-333`), including mid-turn AssistantText rows whose `OnTranscriptActionAsync` would return before publishing (`TranscriptTurnWindow.FindActionBoundaryAsync` yields null). A long turn during an outage mints one no-op debt row per text row. |
| Next-message delivery does not wait for channel debt. | True for plain delivery: the coordinator's post-routing re-query (`:260-262`) and `SessionMessageQueueService.OnTurnEndCoreAsync` (`:1621-1623`) exclude `channel` rows. Witnessed by `C1435_owed_channel_reply_does_not_hold_next_message_delivery`. |
| The TTL loss notice reaches the originating conversation. | For `StaleTtl`/`TurnIncomplete`/`TurnUnmatched` only; `Unroutable` and the provider reasons skip it (`:1066-1071`). |

## Decisions

- D-1 **Ordering scope is per (session, destination).** `SessionRuntimeAction` gains a nullable
  `ConversationKey` (`{provider}:{conversationId}`, unbounded `text` like
  `SessionQueuedMessage.ConversationKey`). A debt row waits only behind earlier debt rows with the
  same key, or with a null key; a null key means "every destination" and is written only when one
  turn addresses more than one conversation (the latent cross-conversation fan-out in
  `DispatchAsync`, degenerate today). The key is resolved at phase 4 by a dispatcher helper:
  action boundary, owning prompt, `MatchChannelRowsAsync` (which includes settled rows, so the
  key survives settlement), distinct keys; a turn that matches no channel row resolves to the
  session's newest Channel `ConversationKey`, the target `DispatchMachineTurnFollowUpAsync` uses.
  Every guarantee a caller relies on survives:
  - *Replies to one chat publish in transcript order* (invariants doc, CARD-1422 paragraph;
    `C1422_failed_channel_reply_is_retried_in_order_after_the_dispatcher_recovers`;
    `C1435_late_confirm_publication_waits_for_an_earlier_owed_reply`): same key, same wait.
  - *A later machine-turn note (task done, check-in) does not overtake an owed reply to the same
    chat*: a non-correlated turn keys to the newest conversation, so it still waits behind that
    chat's debt.
  - *Trailing-text follow-ups* (`DispatchFollowUpAsync`) run from per-(prompt, conversation)
    direct-reply roots that exist only after the main reply published; they never depended on the
    session-wide debt order.
  - *Late-confirm publication* (`OnLateConfirmedAsync`) scans prompts in sequence order within a
    call; across calls it is covered by D-3's keyed wait.
  - *Idempotency*: claim-before-produce (`ChannelReplySettledAt`), the pending-range rules and
    prompt matching are order-independent.
  - *Phases 5-7* (review routing, task settlement, refinement confirmation, catch-up) have not
    waited on channel debt since CARD-1422; unchanged.
  Rejected: one debt row per key per sequence (collides with the unique
  `(AgentSessionId, Sequence, TriggerKind)` index and multiplies rows for a case that does not
  occur); resolving the key at head-selection time (needs the column anyway and repeats the
  transcript walk on every page); keeping per-session order (the card's defect).
- D-2 **A route action with no publishable boundary creates no debt.** When the resolver finds
  no boundary or owning prompt, phase 4 calls the dispatcher as today (it returns before any
  publication) and saves nothing. This removes the one-row-per-AssistantText churn during an
  outage and shortens the retention hold. Rejected: keeping the conservative row (cost, no
  ordering benefit: nothing publishes from such a trigger).
- D-3 **CARD-1433: a late-confirm publication is a channel publication and gets phase 4's
  treatment.** At phase 9 the coordinator resolves the late-confirmed sources' keys; if debt with a
  matching (or null) key is owed at or below the action's sequence, or if the publication is
  attempted and fails (`FailedCorrelationIds`), it saves a `channel` debt row at the action's
  sequence carrying the ids in `LateConfirmedChannelMessageIds` (merging into the phase-4 row at
  that sequence when one exists: union the ids, null the key if the keys differ) and the action
  completes and is removed. Debt execution runs the turn dispatch and then, when the row carries
  ids, `ExecuteActionLateConfirmAsync`; both are idempotent; a failed late-confirm dispatch is
  retained with the ordinary backoff. Later queue actions are no longer behind it. Rejected:
  documenting the caveat (the invariants doc and the CARD-1435 rule-1 witness promise the
  opposite); letting not-due phase-9 actions be skipped in head selection (special-cases the head
  query and the catch, duplicating the debt mechanism without its ordering).
- D-4 **Give-up policy, dispatcher-owned, checked before each attempt.** A due debt row is
  abandoned when `Attempts >= ChannelBridge:ReplyDebtGiveUpAttempts` (default 20) or
  `now - CreatedAt >= ChannelBridge:ReplyDebtGiveUpMinutes` (default 10); either 0 disables that
  bound. Abandoning resolves the turn's open correlations plus any open late-confirm sources on
  the row and reports them through the existing `ReportLostAsync` with a new
  `LossReason.PublishFailed` ("the turn completed but its reply could not be published after N
  attempts over M minutes; last failure X"): Critical `ChannelReplyLost` incident and alert,
  rows settled, originating-conversation notice attempted best-effort (the outage may have
  ended). The coordinator then removes the row, so later debt to that destination flows. Nothing
  is dropped silently: a row whose correlations are already settled (TTL or a concurrent
  publication) is removed with an information log naming the sequence. `PendingReplyTtlMinutes`
  stays the backstop; the validator requires `ReplyDebtGiveUpMinutes <= PendingReplyTtlMinutes`
  or 0. Why both bounds: a fast-rejecting destination burns about 25 attempts in 10 minutes for
  nothing (the attempt bound fires first at about 7.5 minutes), while a destination that times out
  for 30 s per attempt needs about 20 minutes for 20 attempts (the time bound fires first). The
  age is `CreatedAt`: a row that waited behind a dead destination for T minutes and then fails
  once gives up at once, which is correct (the destination has been dead for T minutes) and
  still raises its own incident. Rejected: attempts only; time only; a `FirstFailedAt` column
  (nothing it buys justifies a second column); relying on the TTL alone (30 minutes, a
  misleading reason, and the debt lingers until its next retry).
- D-5 **Retention: no code change.** The four exclusions stay; a debt row now lives at most
  `max(ReplyDebtGiveUpMinutes, PendingReplyTtlMinutes)` plus one backoff, so the exclusion is
  transient, and D-2 stops the churn that multiplied rows. Rejected: excluding `channel` rows
  from the retention predicates (an owed reply needs the transcript and queue rows the dispatcher
  resolves from).
- D-6 **The coordinator stays settings-free.** It passes a `ChannelReplyDebtState(Attempts,
  CreatedAt, FailureReason)` record to the runtime/dispatcher, which owns the policy and already
  holds `ChannelBridgeSettings`. Rejected: injecting `IOptions` into the coordinator.
- D-7 Settings live in `ChannelBridgeSettings` beside `PendingReplyTtlMinutes` and are documented
  in `docs/telegram.md`.
- D-8 No session waits for input in this change; the CARD-1083 release checklist does not apply.

## Slices

Each slice commits its red witness first, then the fix. All tests use the existing
`BridgeQueueHarness`, `RecordingProducer` and `Entry` helpers in
`tests/Antiphon.Tests/Application/SessionRuntimeActionRecoveryTests.cs`. If CARD-1435 has not
landed when Code starts, cherry-pick `3ed74f74d` (tests only, not the plan commit) as the first
commit, because S2 amends one of its tests.

### S1 Per-destination debt (CARD-1434, D-1, D-2)

- `server/Domain/Entities/SessionRuntimeAction.cs`: `public string? ConversationKey { get; set; }`.
- `server/Infrastructure/Data/AppDbContext.cs`: no length constraint (matches the source column).
- `server/Migrations/<stamp>_AddRuntimeActionConversationKey.cs` and `AppDbContextModelSnapshot.cs`:
  one nullable `text` column, shape as `20261015195037_RuntimeActionAttemptAndDirectTail.cs`.
- `server/Application/Services/ChannelReplyDispatcher.cs`: `internal async Task<ChannelReplyRoute?>
  ResolveActionRouteAsync(Guid sessionId, long sequence, CancellationToken ct)`; null when
  `FindActionBoundaryAsync`/`FindOwningPromptAsync` yield nothing; otherwise
  `ChannelReplyRoute(string? ConversationKey)` per D-1.
- `server/Application/Services/AgentSessionRuntime.cs`: scoped passthrough
  `ResolveActionChannelRouteAsync`, beside `ExecuteActionChannelReplyAsync`.
- `server/Application/Services/SessionRuntimeActionCoordinator.cs`:
  - `ExecuteChannelReplyPhaseAsync` resolves the route first; no route: dispatch as today and
    return without debt; otherwise `owed` is the max `DueAt` over earlier debt rows whose key is
    null, equal, or whose own key is null; the saved row carries the key.
  - Head selection and the catch-block push (`a.Sequence > action.Sequence && a.DueAt < due`)
    use the same key predicate.
- Tests (`SessionRuntimeActionRecoveryTests`): `RecordingProducer` gains
  `HashSet<string> FailingConversations` (fails only replies whose `ConversationId` is listed).
  `C1434_a_failing_reply_to_one_destination_does_not_hold_another_destination`: destination A
  failing, B healthy; turn 1 answers A (debt, attempts > 0, key `telegram:A`); turn 2 answers B;
  after the drain `producer.Sent` is `["second answer"]` to B while A's debt row remains; then
  with A healthy and all `DueAt` past, `Sent` is `["second answer", "first answer"]` and no action
  remains. Red at the test commit: nothing publishes (B waits behind A).

### S2 Late-confirm publication becomes debt (CARD-1433, D-3)

- `SessionRuntimeActionCoordinator.ExecuteAsync`: the `Phase > QueuePhase` branch moves to
  `ExecuteLateConfirmPhaseAsync` (keys from the sources' `ConversationKey`; owed check keyed;
  attempt via `runtime.ExecuteActionLateConfirmAsync`; owed or failed saves/merges the debt row
  with the ids; the action is then removed as before). The debt branch runs
  `ExecuteActionChannelReplyAsync` then, when `LateConfirmedChannelMessageIds.Length > 0`,
  `ExecuteActionLateConfirmAsync(action)`.
- Amend `C1435_late_confirm_publication_waits_for_an_earlier_owed_reply`: after the second drain
  no non-debt action remains; the seq-6 `channel` row has `LateConfirmedChannelMessageIds`
  containing `typed`, `Attempts == 0`, key `telegram:late-confirm-recipient`. Its end state
  (`["first answer", "second answer"]`, all to the destination, zero actions) is unchanged.
- `C1433_a_waiting_late_confirm_does_not_hold_later_queue_actions`: the C1435 late-confirm setup
  (first reply owed and held open, typed-once channel message late-confirmed by the TurnEnd at
  6), then a plain UI `Pending` message and a third turn (`UserPrompt` 7 from the terminal,
  `AssistantText` 8, `TurnEnd` 9); after the drain `h.Adapter.SubmittedBodies` is `[next]` and
  the queued row is `Sent` while `producer.Sent` is empty and the first reply is still owed; the
  session holds only `channel` rows (seq 3 failing, seq 6 with the late-confirm ids, seq 9
  waiting). Releasing `DueAt` publishes `["first answer", "second answer"]` and leaves zero
  actions. Red at the test commit: `SubmittedBodies` is empty (the seq-6 phase-9 action is the
  not-due head).

### S3 Give-up bound (CARD-1434, D-4, D-6, D-7)

- `server/Application/Settings/ChannelBridgeSettings.cs`: `ReplyDebtGiveUpAttempts` (20, 0..1000)
  and `ReplyDebtGiveUpMinutes` (10, 0..`PendingReplyTtlMinutes`); validator rules.
- `ChannelReplyDispatcher.cs`: `LossReason.PublishFailed`; `internal async Task<bool>
  TryAbandonExhaustedDebtAsync(Guid sessionId, long sequence, IReadOnlyCollection<Guid>
  lateConfirmedIds, ChannelReplyDebtState state, CancellationToken ct)` (false when the bounds
  are not reached; true after reporting, or when nothing is open, with the log line).
- `AgentSessionRuntime.cs`: scoped passthrough. `SessionRuntimeActionCoordinator.cs`: the debt
  branch calls it first and removes the row on true.
- `C1434_exhausted_channel_debt_is_reported_lost_and_later_replies_flow`: failing producer; the
  debt row's `Attempts` is set to the bound and `DueAt` to the past; after the drain exactly one
  `AgentIncident` with `Kind == ChannelReplyLost`, `FailureReason == "PublishFailed"` exists for
  `h.AgentId`, the correlation has `ChannelReplySettledAt`, no action remains; with the producer
  healthy a second turn to the same destination publishes immediately. Red at the test commit:
  no incident, the debt row remains, the second reply is held.
- `ChannelMachineTurnTextTests` (holds the existing `ChannelBridgeSettingsValidator` tests):
  `C1434_give_up_minutes_above_the_ttl_is_refused` and the default settings validate.

### S4 Documentation

- `docs/session-runtime-invariants.md`, CARD-1422 paragraph, replace from "A route action that
  reaches phase 4" through the `ChannelReplyLost` sentence with: "A route action that reaches
  phase 4 while earlier channel debt to the same destination is owed does not dispatch: it saves
  its own debt row behind it, keyed by the turn's conversation (a turn that addresses several
  conversations, or whose late-confirm sources differ, writes a null key and waits for every
  destination). A route action with no publishable boundary saves no debt. Only the earliest
  debt row per destination runs, and only when due, so replies to one chat publish in transcript
  order while other chats' replies flow. A failed debt retry pushes later same-destination debt
  to its new due time and does not end the drain page. The queue phase does not wait for channel
  debt. A late-confirm publication that finds same-destination debt owed at or below its sequence,
  or that fails, becomes debt at its own sequence carrying the late-confirmed ids, so later queue
  actions proceed (CARD-1433). A debt row that reaches `ChannelBridge:ReplyDebtGiveUpAttempts` or
  `ReplyDebtGiveUpMinutes` is abandoned: its open correlations are settled with a Critical
  `ChannelReplyLost` (`PublishFailed`) and the row is removed so later replies to that chat flow
  (CARD-1434); `PendingReplyTtlMinutes` remains the backstop. Retention excludes a session only
  while such a row exists." Add the new witnesses to the pinned list.
- `docs/telegram.md`: the two settings beside `PendingReplyTtlMinutes`.
- This plan: record the checkpoint runs under `### Runs`.

## Verification design

- V-1 (S1) `C1434_a_failing_reply_to_one_destination_does_not_hold_another_destination`: red
  before the S1 fix, green after.
- V-2 (S1) Same-destination order unchanged: `C1422_failed_channel_reply_is_retried_in_order_after_the_dispatcher_recovers`
  and both `C1435_*` stay green after S1.
- V-3 (S2) `C1433_a_waiting_late_confirm_does_not_hold_later_queue_actions`: red before the S2
  fix (empty `SubmittedBodies`), green after.
- V-4 (S2) The amended `C1435_late_confirm_publication_waits_for_an_earlier_owed_reply` is green
  and still ends with both replies in order and zero actions.
- V-5 (S3) `C1434_exhausted_channel_debt_is_reported_lost_and_later_replies_flow` plus the
  validator witness: red before S3, green after.
- V-6 (S1) Schema: `CardRelationshipSchemaTests` asserts no pending migrations and
  `HasPendingModelChanges() == false`; red if the snapshot is not regenerated.
- V-7 Regression: catch-up settlement, the channel dispatcher classes, the coordinator classes.

Positive controls pending for SourceLanding Mutation (method-scoped):

- PC-1 drop the key predicate from head selection (V-1 red: B held behind A).
- PC-2 resolver always returns a null key (V-1 red).
- PC-3 phase 9 throws instead of saving debt (V-3 red).
- PC-4 debt execution skips the late-confirm ids (V-4 red: second answer never publishes).
- PC-5 give-up compares with `>` or never calls `ReportLostAsync` (V-5 red: no incident).
- PC-6 give-up removes the row without settling (V-5 red: `ChannelReplySettledAt` null).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1434a/` | per-destination | `/*/*/SessionRuntimeActionRecoveryTests/C1434_a_failing*` | V-1 | all listed, 0 failed | 1 | 4 | |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1433/` | late-confirm-hold | `/*/*/SessionRuntimeActionRecoveryTests/C1433*` | V-3 | all listed, 0 failed | 1 | 4 | |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1434b/` | give-up | `/*/*/SessionRuntimeActionRecoveryTests/C1434_exhausted*` | V-5 | all listed, 0 failed | 1 | 4 | |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1434c/` | recovery-class | `/*/*/SessionRuntimeActionRecoveryTests/*` | V-1, V-2, V-3, V-4, V-5 | all listed, 0 failed | 17 | 6 | |
| CP-5 | S4 | CP-4 | schema | `/*/*/CardRelationshipSchemaTests/*` | V-6 | all listed, 0 failed | 3 | 2 | |
| CP-6 | S4 | CP-4 | channel | `/*/Antiphon.Tests.Application/(AgentTaskCatchUpSettlementTests*)\|(ChannelReplyDurabilityTests*)\|(ChannelReplyLateConfirmDispatchTests*)\|(ChannelMachineTurnTextTests*)/*` | V-5, V-7 | all listed, 0 failed | 60 | 8 | |
| CP-7 | S4 | CP-4 | coordinator | `/*/Antiphon.Tests.Application/(SessionRuntimeActionTests*)\|(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionDeferralTests*)\|(SessionRuntimeActionTemporaryBlockTests*)/*` | V-7 | all listed, 0 failed | 14 | 10 | true |

Lane: the default runner (server2, Linux); each `After` group has its own build because the
checkpoint tool refuses a reused build across groups. CP-4's `Min` of 17 is the class's 12
results on master plus the two CARD-1435 witnesses and the three added here. CP-7 is serial
because `SessionRuntimeActionTests.C1259_shutdown_joins_and_restart_reconciles_submission` and
`C1259_saturation_keeps_ingestion_live_and_recovers_overflow` went red in CARD-1422 only under
overlapping rows and are green alone. The Unit lane is not listed: it exceeded its 30-minute
deadline twice on this host in CARD-1422 and remains unverified there; the validator change gets
its own witness in CP-6 instead. The `AgentTaskMidTurnRefineTests` C0491/C1169 cases, red at base
since CARD-1422's run, are deliberately outside CP-6.

### Cost

Ordinary Code floor 38 minutes (sum of `EstimatedMinutes`) plus authoring; one run per committed
slice group, four builds.

### Runs

Code task bd490132 (branch `feat/card-task-bd490132`, runner mirror on server2, Linux). Each
run is `tools/Antiphon.Checkpoints start --plan <this> --rows ... --expected-source-sha <sha>`
then `wait`; evidence under `.antiphon/checkpoints/<run>/` (gitignored).

| Run | Rows | Commit | Outcome |
|---|---|---|---|
| 20261022-015820-4622 | CP-1, CP-2 | `bc3acff74ee380c1eb608b17759d527e150f50f2` (S1/S2 witnesses, no fix) | RED as designed: CP-1 `producer.Sent` was `[]` (B held behind A); CP-2 `SubmittedBodies` was `[]` (phase-9 head not due). |
| 20261022-021011-5464 | CP-1, CP-2 | `4788cdf4826448e7f82b02db4b4b1755e0811731` (S1+S2 fixed) | GREEN: 1/1 and 1/1, clean, buildSource=verified. |
| 20261022-022752-0b56 | CP-3 | `d659be6079f6e92a03aa1d412c49ce7a7a145cfa` (S3 witness, no fix) | RED as designed: no `ChannelReplyLost` incident (0 of 1). |
