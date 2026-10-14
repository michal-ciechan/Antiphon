# CARD-1248 Code qualification after scope refinement

The caller's refinement removed whole Unit from the required scope and directed
Review using the already green gateway classes and migration. The broad run had
already ended; it remains unqualified and was not repeated. This report supersedes
the earlier scope blocker, while preserving the original receipts and tested SHAs.

Original Code task and landing owner: `3e82b1eb-d660-47b9-aaf1-e8d5f79c9f66`.
Branch: `feat/card-task-3e82b1eb`.
Worktree: `/work/worktrees/task-3e82b1eb`.
Task base: `a02389cab727d508532e6d3db8ff6cbcf3eb7366`.
Actual final tested source: `e0dafac93192f7b9c758d718ab2bcc0f3364a6be`.
Fetched master: `d74b595de6444b9b59fce7ac91819e1432326ce4`, already integrated by
that tested merge commit. A fresh fetch after refinement found no further commits.
The refinement changes only the plan and reports; no production or test source
changed after qualification, and no additional build or test was necessary.

## Outcome and required verification

The EF lookup, unique index and in-memory key include ConversationId. Distinct
chats retain distinct receipts; true replay preserves the original broker offset.
The PostgreSQL migration preserves populated legacy rows and monitoring stamps,
enforces the new identity, and refuses a conflicting downgrade without data loss.
Kafka publication precedes receipt storage, so the demonstrated defect is lost
inbox persistence/monitoring evidence, not proven downstream Kafka message loss.

| Check | Actual outcome |
|---|---|
| CP-1 red first | 15 executed, 8 passed, 7 failed, 0 skipped; all seven new defect cases failed before the fix |
| CP-1 after implementation | 15 executed, 15 passed, 0 failed, 0 skipped |
| CP-2 final affected classes | 40 executed, 40 passed, 0 failed, 0 skipped |
| CP-4 final Messaging Unit | 3 executed, 3 passed, 0 failed, 0 skipped |
| CP-3 historical broad Unit | unqualified: original 40-minute deadline, no terminal TRX; counts n/a |
| CP-5 exact-base diagnostic | 59 executed, 42 passed, 17 failed, 0 skipped |
| CP-6 exact-base diagnostic | 4 executed, 0 passed, 4 failed, 0 skipped |

V-1 passed: distinct-chat EF receipts and true replay.
V-2 passed: direct database uniqueness.
V-3 passed: legacy offset backfill stays with its chat.
V-4 passed: both recorded and seeded in-memory variants.
V-5 passed: populated PostgreSQL upgrade, duplicate rejection, safe downgrade and
rollback/re-upgrade.
V-6 passed: hosted consumer stores broker A/B/B as two receipts at offsets 0/1.
R-1 passed: complete EF receipt and consumer classes.
R-2 passed: complete gateway and monitor classes.
R-3 passed in the refined scope: all three Messaging Unit cases. Broad Unit is
outside that scope and remains unqualified, never reported as passed.
R-4 passed: SQL inspection, fresh TRX inspection, both final receipt validators,
full-range evidence guard and publication/merge checks, subject to the separate
waiter disclosure below. Final publication details are in the caller report.

Fresh CP-2 TRX covers EF store 9, consumer 3, migration 1, memory 2, gateway 7 and
monitor 18 cases. CP-4 contains memory 2 and classification guard 1; row totals
overlap. Both final receipt validators exited 0 with clean source and verified
build provenance. Both final rows report slot=granted and waited=0s.

## Inherited failure names

The requested 17 CP-5 failures reproduced at the exact task base:

- deleting_root_rejects_registration
- copies_the_dll_set_never_the_source_tree
- preparing_journal_precedes_all_run_io
- rerun_lines_and_reruns_suffix
- unlisted_failure_never_reruns
- row_green_only_when_rerun_passed_and_nothing_else_failed
- rerun_invokes_once_per_class
- confirmed_dead_local_identity_permits_cleanup
- reruns_only_known_flaky_names_once
- environment_reaches_only_its_row_and_its_reruns
- late_executor_ack_resolves_launch_unknown
- dead_executor_ack_recovers_without_progress
- partial_copy_failure_rolls_back_owned_run
- launch_intent_failure_prevents_native_launch
- post_launch_bookkeeping_failure_keeps_image
- definite_native_refusal_rolls_back_owned_run
- Registry_matches_compiled_metadata

Four later failure cases also reproduced at base in CP-6:

- total_deadline_skips_a_queued_build_without_starting_it
- total_deadline_kills_running_and_skips_queued
- C1066_Fixture_npm_install_uses_argument_log_under_nounset(True)
- C1066_Fixture_npm_install_uses_argument_log_under_nounset(False)

## Disclosures

The broad Unit timeout itself was not reproduced at base; its cause remains
unverified. No timeout was widened and no assertion was loosened. All observed
Unit wrappers/host and the final executor exited before the earlier report.

The final checkpoint executor finished with exit 0. Its waiter exited 2 because
required references included an unused old bin-c1248-red build stamp: first
receipt-source-mismatching, then receipt-missing after literal cleanup. Neither
the receipts nor stamps were edited to hide this refusal. Both independent final
receipt validators and fresh TRX inspection passed. The waiter is not claimed green.

Owned project alternate output directories were removed; generated TRX, JSON,
logs and checkpoint evidence remain ignored. No deployment occurred. Restart: none.

All PC-1 through PC-6 variants remain pending for SourceLanding Mutation:
PC-1 distinct-chat/backfill; PC-2 model index; PC-3 recorded-positive/seeded-negative;
PC-4 seeded-positive/recorded-negative; PC-5 Up/Down; PC-6 EF, both memory arguments
and consumer replay. No deliberate mutant ran. Review is next; the caller then
lands the original Code task and commissions SourceLanding Mutation.

Plan: `docs/superpowers/plans/2026-10-14-card-1248-gateway-inbox-key-plan.md`.
Earlier frozen evidence: `docs/superpowers/reports/2026-10-14-card-1248-code-evidence.md`.
Unbound candidate: `docs/superpowers/mutations/2026-10-14-card-1248-inbox-key-candidate.md`.
Full generated report: `/work/worktrees/task-3e82b1eb/.antiphon/task-3e82b1eb.md`.

## Unedited final CHECKPOINT lines

```
CHECKPOINT CP-2 commit=e0dafac93192f7b9c758d718ab2bcc0f3364a6be build=ok filter=/*/*/(EfInboxReceiptStoreTests*)|(InMemoryInboxReceiptStoreTests*)|(InboxConversationMigrationTests*)|(InboxConsumerServiceTests*)|(GatewayTests*)|(InboundUnconsumedMonitorTests*)/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-3e82b1eb/.antiphon/checkpoints/20261014-182331-ed58/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e0dafac93192f7b9c758d718ab2bcc0f3364a6be sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=e0dafac93192f7b9c758d718ab2bcc0f3364a6be build=reused filter=/*/*/*/*[Category=Unit] executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-3e82b1eb/.antiphon/checkpoints/20261014-182331-ed58/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=e0dafac93192f7b9c758d718ab2bcc0f3364a6be sourceState=clean buildSource=verified
```
