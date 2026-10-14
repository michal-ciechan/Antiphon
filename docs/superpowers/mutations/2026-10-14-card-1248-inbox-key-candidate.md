# CARD-1248 unbound Mutation candidate

All controls and variants are pending. No deliberate mutation was executed by Code.
Separate ordinary Review and caller landing of original Code task
`3e82b1eb-d660-47b9-aaf1-e8d5f79c9f66` precede SourceLanding Mutation.

Plan: `docs/superpowers/plans/2026-10-14-card-1248-gateway-inbox-key-plan.md`.
Test project: `tests/Antiphon.Messaging.Tests`. Restart: none for these tests.
Production gateway deployment remains an operator step.

These are candidate defects and predicted outcomes, not executed PC evidence. Bind
each to the landed source. Mutation owns red/restore/green and missing-control
discovery. Run only the named method for each cycle, through the host build gate;
retain separate baseline, red and restored-green receipts. Do not widen a timeout
or loosen an assertion. Compile/fixture failures and zero tests are not controls.

| Control / variant | Mutation | Detecting method | Expected red | Status |
|---|---|---|---|---|
| PC-1 / distinct chats | Remove ConversationId from the EF existing-row predicate | `EfInboxReceiptStoreTests.Same_message_id_in_two_chats_preserves_both_receipts_and_redelivery` | Expected two receipts, observes one | Pending |
| PC-1 / legacy backfill | Same predicate mutation, separate cycle | `EfInboxReceiptStoreTests.Offset_backfill_does_not_attach_another_chats_delivery_to_a_legacy_row` | Expected two receipts and untouched first-chat offset | Pending |
| PC-2 | Restore the old two-column runtime model index, without changing the migration | `EfInboxReceiptStoreTests.Database_unique_key_allows_two_chats_but_rejects_a_true_duplicate` | The test action inserting chat-2 is rejected by the old unique key | Pending |
| PC-3 / recorded positive, seeded negative | In InMemoryInboxReceiptStore.RecordAsync replace the conversation tuple component with an empty string, preserving tuple arity | `InMemoryInboxReceiptStoreTests.Chat_identity_is_preserved_for_recorded_and_seeded_receipts` | false/recorded expects two rows but gets one; true/seeded is a predicted passing negative variant for this isolated mutation | Both pending |
| PC-4 / seeded positive, recorded negative | In InMemoryInboxReceiptStore.Add replace the conversation tuple component with an empty string | `InMemoryInboxReceiptStoreTests.Chat_identity_is_preserved_for_recorded_and_seeded_receipts` | true/seeded expects two rows but gets three; false/recorded remains a predicted passing negative variant | Both pending |
| PC-5 / Up | Omit CreateIndex in the new migration's Up, retaining DropIndex | `InboxConversationMigrationTests.Upgrade_preserves_legacy_data_and_enforces_conversation_scoped_identity` | True duplicate save no longer throws the expected DbUpdateException | Pending |
| PC-5 / Down | Omit CreateIndex in Down, retaining DropIndex | `InboxConversationMigrationTests.Upgrade_preserves_legacy_data_and_enforces_conversation_scoped_identity` | Colliding downgrade no longer throws the expected PostgresException | Pending |
| PC-6 / EF replay | Make the existing-row offset update unconditional | `EfInboxReceiptStoreTests.Same_message_id_in_two_chats_preserves_both_receipts_and_redelivery` | Original group offset 20 becomes replay offset 21 | Pending |
| PC-6 / memory replay | Make the in-memory existing-row offset update unconditional | `InMemoryInboxReceiptStoreTests.Chat_identity_is_preserved_for_recorded_and_seeded_receipts` | Both variants lose their original offsets 10/20 | Both pending |
| PC-6 / consumer replay | Same EF offset mutation, separate broker-consumer cycle | `InboxConsumerServiceTests.Two_chats_with_the_same_message_id_are_stored_after_broker_publication` | Original second-chat offset 1 becomes replay offset 2 | Pending |

Use `/*/*/ClassName/ExactMethod` for each non-parameterized method above. For the
boolean method use only
`/*/*/InMemoryInboxReceiptStoreTests/Chat_identity_is_preserved_for_recorded_and_seeded_receipts*`
and inspect both argument results separately; this suffix selects only that method.
PC-3/PC-4 must establish the stated positive and negative variants, not merely one
failed result somewhere in the method. These independent mutations touch the same
files and must be restored between cycles.

The PostgreSQL method owns a disposable PostgreSQL 16 container. The consumer method
requires ANTIPHON_BROKER_TESTS=1 and its disposable Redpanda fixture, never a live
gateway broker. Bind and qualify those fixture requirements in the Mutation pack.
