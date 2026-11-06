# CARD-0824 S5b-2 migration controls: unbound candidates

The caller retained both source migrations and required safe Down behavior.
Original PC-1..PC-159 and all variants remain pending, unchanged. These two new
candidates are also pending for method-scoped SourceLanding Mutation; bind the
landed source, exact payload/oracle, case identities and restoration before use.
Code did not execute a deliberate mutant or any red/restore/green PC cycle.

| ID | Source guard candidate | Exact method selector | Decisive variant/oracle |
|---|---|---|---|
| PC-160 | Remove the refusal SQL in AddRefOnlySettlementDebtWorkspaceSnapshot.Down, retaining DropColumn | `/*/*/C0824RefOnlySnapshotMigrationTests/Downgrade_refuses_recorded_snapshot_and_preserves_its_bytes` | debt: Should.ThrowAsync must fail because recorded authority was silently dropped; park remains an independent control |
| PC-161 | Remove the refusal SQL in AddRefOnlyParkWorkspaceSnapshot.Down, retaining DropColumn | `/*/*/C0824RefOnlySnapshotMigrationTests/Downgrade_refuses_recorded_snapshot_and_preserves_its_bytes` | park: same assertion must fail; debt remains an independent control |

Both parameter cases run under this method selector; count each expected assertion
failure separately and do not batch the two mutations on one method. The ordinary
positive migration test covers null-column rollback and re-upgrade, and both
negative variants verify exact bytes and applied history after refusal before
clearing only the test-owned snapshot and completing rollback/re-upgrade.

Mutation owns missing-control discovery, including the independently removable
lock/transaction boundary and concurrent writers. No concurrency-control discharge
is claimed from the ordinary nonnull-row cases. Unknown binding or a test that
cannot fail is an adequacy gap, never a passing control.
