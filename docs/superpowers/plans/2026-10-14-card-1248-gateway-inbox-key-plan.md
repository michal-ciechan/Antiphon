# CARD-1248 gateway inbox conversation identity

Original Code/landing owner: `3e82b1eb-d660-47b9-aaf1-e8d5f79c9f66`.
Task base: `a02389cab727d508532e6d3db8ff6cbcf3eb7366`.
Branch: `feat/card-task-3e82b1eb`. Round: Final. Restart: none in Code;
operator deployment of the messaging service is separate.

## Investigation and implementation

`GatewayIngressService` publishes to Kafka before invoking receipt sinks. The EF store
looks up `(Channel, ChannelMessageId)` and returns an existing row, or swallows a
unique violation on insertion. `InboxConsumerService` consumes the same published
record and calls the same store; its `stored` log means that call returned, not that
a distinct row was inserted. A collision therefore loses the second chat's receipt
and monitor evidence. It does not suppress the already accepted broker publication.
The inbox consumer uses auto commits; duplicate handling continues consumption.
No live delivery or historical loss claim follows from this investigation.

S1: commit tests first and execute CP-1 against unchanged production. Literal
two-chat receipt, database index, offset backfill, fake-store and broker-consumer
oracles must fail; true-redelivery regression cases remain green.

S2: use `(Channel, ConversationId, ChannelMessageId)` for EF lookup, unique index,
and in-memory identity (including seeded receipts). Generate an EF migration with
the repository CLI; create the replacement index before dropping the old index
in one transaction. No table recreation, row deletion or backfill is necessary:
ConversationId is already non-null. Down creates the restrictive old index first;
if legitimate cross-chat collisions exist, PostgreSQL refuses rollback without
losing rows or the new index. Update comments/index identity and Telegram owner.
Do not expand the pre-existing general unique-exception classification policy.

S3: correct the slow-test registry's comment placement after CP-4 exposed the
format error in both the new entry and an existing Kafka entry. The existing entry
was independently reproduced at the task base (one executed, one failed). Comments
belong on the preceding line; retain both registered classes and their Slow tags.
Rerun CP-4 after this documentation/registry-only slice; do not repeat green rows.

CP-3 reached its initial 40-minute deadline without a terminal TRX. Its observed
initial 17 failures were reproduced at the task base by the bounded CP-5 selection:
59 executed, 42 passed, 17 failed, zero skipped. The completed partial log contained
four further failures and 37 skip messages. CP-6 reused the certified base build
and reproduced all four additional cases (four executed, four failed, zero skipped).
This proves all 21 observed failures are
inherited; it does not prove the cause of the whole-Unit timeout. The earlier
CARD-1253 plan records a similar unqualified Unit timeout. No timeout or assertion
was widened. The caller's refinement explicitly removes whole Unit from this
task's required scope and directs handoff to Review using the green gateway
classes and migration. CP-3 remains historically unqualified, never passed. It
and the completed CP-5/CP-6 base diagnostics are retired from the active manifest;
their original filters, counts and receipts remain in the evidence report.

## Verification design

V-1: `EfInboxReceiptStoreTests.Same_message_id_in_two_chats_preserves_both_receipts_and_redelivery`
asserts two literal chat IDs, original envelope, partition, offset and reply handle.
V-2: `Database_unique_key_allows_two_chats_but_rejects_a_true_duplicate` inserts
directly, bypassing the application lookup, then repeats the true key.
V-3: `Offset_backfill_does_not_attach_another_chats_delivery_to_a_legacy_row`
keeps the original chat's null offset and stores the other chat separately.
V-4: `InMemoryInboxReceiptStoreTests.Chat_identity_is_preserved_for_recorded_and_seeded_receipts`
has recorded/seeded variants, each with two chats and replay of both.
V-5: `InboxConversationMigrationTests.Upgrade_preserves_legacy_data_and_enforces_conversation_scoped_identity`
uses disposable PostgreSQL 16, migrates from the previous deployed schema with a
populated receipt, checks original identity/payload/monitor stamps, inserts another
chat, checks true duplicate SQLSTATE and literal index name, checks safe rollback
refusal, then rollback/re-upgrade after explicitly resolving the fixture collision.
V-6: `InboxConsumerServiceTests.Two_chats_with_the_same_message_id_are_stored_after_broker_publication`
publishes A/B/B to a disposable Redpanda topic, runs the hosted consumer with the
real EF store, awaits all three sink calls, and asserts two rows at offsets 0/1.

R-1: full `EfInboxReceiptStoreTests` and `InboxConsumerServiceTests` preserve true
duplicate suppression, same-key concurrent inserts, offset backfill, host survival
on duplicate and host stop on non-duplicate persistence failure.
R-2: full `GatewayTests` and `InboundUnconsumedMonitorTests` preserve publication,
receipt failure handling, monitor acknowledgement and retry behavior.
R-3: affected Messaging Unit lane. The caller's scope refinement explicitly says
no whole-Unit lane is required and its absence is not a blocker. CP-4 passes all
three Messaging Unit cases. The broad CP-3 attempt remains unqualified and is
reported separately; no full assembly run is required.
R-4: inspect generated migration SQL/order, run both receipt validators, inspect
fresh TRX per named class/method, full-range evidence guard, fetched-master merge
and `git merge-tree --write-tree`, clean worktree, push/ref equality.

Manual acceptance: migration is exercised only against the disposable database;
inspect the owner documentation and additive Up/Down order. Production deployment
and live Telegram receipt are operator work, outside this task.

### Mutation handoff (all pending)

- PC-1: omit ConversationId from EF lookup; V-1 and V-3 independently guard it.
- PC-2: restore old model index; V-2 guards it.
- PC-3: remove chat identity from the Record key; recorded positive and seeded
  negative variants, both pending qualification.
- PC-4: remove chat identity from the Add key; seeded positive and recorded
  negative variants, both pending qualification.
- PC-5: omit migration index creation in Up or Down; both V-5 variants pending.
- PC-6: overwrite original receipt offsets on replay; EF, both memory arguments
  and broker-consumer variants pending.

Mutation owns deliberate defects, red/restore/green and missing-control discovery.
Unbound candidate: `docs/superpowers/mutations/2026-10-14-card-1248-inbox-key-candidate.md`.

### Cost

Active table floor: 15 minutes plus authoring and slot waits; the final CP-2/CP-4
selection is 9 minutes. The historical table included a broad CP-3 estimate and
the 7-minute CP-5 and 1-minute CP-6 base diagnostics. Those completed diagnostics
and the unqualified broad run are no longer active obligations after refinement.
CP-1 is the required red-first diagnostic, rerun green with S2 if needed; CP-2
qualifies the same complete affected classes plus adjacent monitoring/ingress.
One run per committed group, `--row-timeout 40m --total-timeout 150m --serial`,
exact committed `--expected-source-sha`. The effective manifest rowMinutes is 40.
UseAppHost=false is the Linux checkpoint default. Tool bootstrap and CLI migration
generation are disclosed auxiliary build-slot operations, not extra proof loops.
No repetitions after green. Rerun only red rows after repairs.
CP-5 runs only in the detached task-base checkout with ExpectedSourceSha equal to
the recorded base. It is an explicitly disclosed failure-triage addition to the
original four-row manifest, not another full assembly or Unit run. The same plan
was copied to that checkout's ignored `.antiphon/c1248-baseline-plan.md` for the
CP-4/CP-5 baseline diagnostics. The initial/final row timeout override is 40 minutes
in request.json; no effective timeout was increased after a failure.
CP-6 is a disclosed direct checkpoint-tool `row --no-build` call with strict base
SHA/build provenance, selecting only the four later failures from CP-3's final
partial log. It avoids another assembly build and does not rerun the whole lane.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Messaging.Tests -> bin-c1248-red/` | red-first | `/*/*/(EfInboxReceiptStoreTests*)\|(InMemoryInboxReceiptStoreTests*)\|(InboxConversationMigrationTests*)\|(InboxConsumerServiceTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, R-1 | all listed; expected defect failures before S2 | 15 | 6 | `ANTIPHON_BROKER_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Messaging.Tests -> bin-c1248-final/` | inbox-and-adjacent | `/*/*/(EfInboxReceiptStoreTests*)\|(InMemoryInboxReceiptStoreTests*)\|(InboxConversationMigrationTests*)\|(InboxConsumerServiceTests*)\|(GatewayTests*)\|(InboundUnconsumedMonitorTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, R-1, R-2 | all listed, 0 failed/skipped | 15 | 7 | `ANTIPHON_BROKER_TESTS=1` |
| CP-4 | S2 | CP-2 | messaging-unit | `/*/*/*/*[Category=Unit]` | R-3 | >= 1 executed, 0 failed/skipped | 1 | 2 | n/a |
