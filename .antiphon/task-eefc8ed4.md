# Final Review: CARD-1433/1434 channel debt (Code task bd490132)

Reviewed source: `feat/card-task-bd490132` at `0a7d4325567e1ae14b573ffa53359e3df60c43aa` (product code at `cd7a6a15e`; later commits are docs only). Base `cc3e54fba`. Verdict: **clean**, no blocking defect. There are four advisories below.

## Receipt (checkpoint tool, server2 Linux, at the tip)

Run `20261022-033607-f4cf`, `start --plan docs/superpowers/plans/2026-10-22-card-1433-1434-channel-debt-scope-plan.md --rows CP-3,CP-4,CP-6,CP-7 --expected-source-sha 0a7d4325…`:

| CP | executed | passed | failed | skipped | state |
|---|---:|---:|---:|---:|---|
| CP-3 give-up | 1 | 1 | 0 | 0 | clean, buildSource=verified |
| CP-4 recovery class | 17 | 17 | 0 | 0 | clean, buildSource=verified (Min 17 met) |
| CP-6 channel | 71 | 71 | 0 | 0 | clean, buildSource=verified (Min 60 met) |
| CP-7 coordinator (serial) | 79 | 79 | 0 | 0 | clean, buildSource=verified (Min 14 met) |

`unlisted: none`, `verdict: GREEN exit=0`, and the tool deleted its outputs. `scripts/validate-checkpoint-receipt.ps1 -Rows CP-3,CP-4,CP-6,CP-7` printed `CHECKPOINT SOURCE VALID source=0a7d4325… rows=4` (exit 0). The working tree was clean throughout. No server 502 and no rerun.

CP-5 is not a receipt row because its red is inherited (CARD-1452). I ran `CardRelationshipSchemaTests.C100_Duplicate_pair_is_refused_by_the_unique_index` once on its own in a detached worktree at `cc3e54fba56ea35796228f6464b8f6d0876ab8ac`: 1 executed, 1 failed, `should end with "_AddCardRelationships" but was "20261020142734_AddStandingAutomaticFreshRecovery"`. That is the same assertion that fails at the tip, so the red is inherited. The worktree has been removed.

### Unlisted runs (stated reasons)

- **Mutations.** One scratch detached worktree at the tip, built with `scripts/build-slot.ps1 … dotnet build tests/Antiphon.Tests --property:OutputPath=bin-mut/`. The mutations were toggled by `REVIEW_MUT` so that one build served both runs. Each run was `build-slot.ps1 … dotnet run --no-build … --treenode-filter "/*/*/SessionRuntimeActionRecoveryTests/C143*"` (5 tests).
  - **A: per-destination key comparison dropped** (in head selection and in the phase-4 owed check). 1 of 5 red: `C1434_a_failing_reply_to_one_destination_does_not_hold_another_destination`, where `producer.Sent` was `[]` and `["second answer"]` was expected.
  - **B: give-up fires on a row with `Attempts == 0`** (that is, before its first attempt). 2 of 5 red: `C1433_a_waiting_late_confirm_does_not_hold_later_queue_actions` and `C1435_late_confirm_publication_waits_for_an_earlier_owed_reply`, each with Sent `["first answer"]` where `["first answer","second answer"]` was expected.
  - The worktree was removed with a path confined to the scratchpad, so the mutations and `bin-mut/` are gone.
- **CP-5 base confirmation.** One build and one run in a detached worktree at `cc3e54fba` (described above), through build-slot. Removed afterwards.

### Other gates

- `git merge-tree --write-tree origin/master HEAD` against origin/master `e1e3beb5c` (which contains 705026da8): clean, tree `402e77b9e`. The plan file is byte-identical to the master copy.
- `scripts/check-evidence-diff.ps1 -BaseRef cc3e54fba -HeadRef HEAD`: `commits=11 entries=0 violations=0`.

## Checks from the brief

1. **Exactly-once and ordering.**
   - The phase-4 debt row is added in the same context as `action.Phase++` and saved by the same `SaveChangesAsync`, so a crash replays the whole phase.
   - A replayed publication cannot send twice. The publish path settles correlations (`ChannelOutboundService` settles member ids), and a replayed dispatch only sees open correlations.
   - Head selection and the phase-4 owed check use the same predicate: `(b.key == null || a.key == null || b.key == a.key)`. A null key on either side serialises against everything.
   - The retry DueAt push uses the same key filter, but it only affects wakeups; the gate is head selection.
   - Route resolution now sits inside the failure envelope, so a resolver throw becomes null-key debt. This is the conservative outcome, and catch-up proceeds (`catch_up_settles_even_when_channel_reply_dispatch_throws` is green in CP-6).
2. **Late-confirm debt.**
   - Ids are carried onto the row. In the merge case the row at the same sequence gets a union of ids, and its key becomes null on a mismatch.
   - The key comes from the sources' `ConversationKey`. It is null when there are several keys, none, or a null key.
   - The owed check uses `Sequence <= action.Sequence`, so it also covers the same-sequence phase-4 row.
   - The row is saved together with the action's removal, so a replay recomputes the same thing.
   - When a debt row executes, it runs the turn reply first and the late-confirm second. Running the turn reply again is idempotent: settled correlations are skipped, and machine sources are settled by the outbound send.
   - C1433 and C1435 prove the late-confirm reply is never published ahead of an earlier owed reply to the same destination; both go red under mutation B.
3. **Give-up.**
   - The bound is checked before each debt retry. The attempt bound is `Attempts >= 20`. The time bound requires `Attempts > 0`.
   - `ReportLostAsync(PublishFailed)` settles the correlations. A replay after a crash finds nothing open and only logs, so it reports once, then the row is removed and later replies to that destination flow (CP-3).
   - The validator rejects minutes greater than `PendingReplyTtlMinutes` (30) and rejects negative values; 0 disables a bound (`C1434_give_up_minutes_above_the_ttl_is_refused`, CP-6).
4. **Migration.**
   - It adds one nullable `text` column with no default. That is metadata-only in Postgres and rewrites no data.
   - Existing rows get null, which keeps the old global ordering.
   - Down drops the column.
   - The Designer target model is byte-identical to `AppDbContextModelSnapshot`.
   - No migration has landed on master since the base.
   - The migration-affected suites ran: CP-4, CP-6 and CP-7 green. CP-5's only red is the inherited pin.
5. **rc impact.** rc/2026-10-20 carries CARD-1422's global debt ordering, which over-serialises: it is safe but has worse liveness. This change is a liveness improvement, not a safety fix, so it can wait for the next rc cut. If it is ported, the port needs its own review because rc lacks CARD-1435 and the coordinator differs by about 124 lines.

## Advisories (disclosed, non-blocking under the verdict policy)

- **ADV-1: the machine-turn key can differ from where the reply actually publishes.**
  - Where: `ChannelReplyDispatcher.ResolveActionRouteAsync`, at `keys.Count == 0 → NewestConversationKeyAsync`.
  - Failure, with `ChannelOutbound:UnifiedRecoveryEnabled=true` (non-default): `DispatchMachineTurnFollowUpAsync` publishes to `originalContext.ConversationKey`, the channel row in front of the injection, not to the newest key. Suppose a session talks to two chats, chat A has an owed reply, and a chat-B message arrives before the machine turn's route phase. The machine note then resolves to key B and publishes into A ahead of A's owed reply.
  - Failure, with the default UR=false: a machine note that is waiting as debt keyed A retargets to the newest chat (B) when it retries. A later B reply has already published, so the note now lands after it, where global ordering would have put it first. Sending the note to B at all is the existing misroute.
  - Why it does not block: UR is off by default and in the documented configuration, the scenario needs a multi-chat session, and the plan explicitly chose "newest key".
  - Fix: file a card. Either resolve machine-turn keys through the same target selection the publish uses, or key machine-turn debt as null.
- **ADV-2: the time bound starts at row creation, so waiting time counts.** A row that waited 10 minutes or more behind another row gets one attempt. If that attempt fails, the row is abandoned at its next retry. This is documented, and the loss is reported Critical, not dropped silently.
- **ADV-3: no test covers the `Attempts > 0` guard on the time bound.** The tests use the wall clock and never age `CreatedAt`. Mutation B, which gives up on never-attempted rows, is caught. Dropping only the guard would not be caught, because the 10-minute bound never elapses in a test. Fix: add a witness that ages a waiting row (Attempts 0) past `ReplyDebtGiveUpMinutes` and asserts it is still attempted.
- **ADV-4: abandoning a row with no open correlations logs at Information only.** In practice this happens only when the correlations were already settled (TTL or a concurrent publication), and it logs the sequence. A machine-turn failure never throws, so machine-only debt never gains attempts.

PCs remain pending for method-scoped SourceLanding Mutation; this round discharges none.
