# CARD-1248 Code evidence before final registry qualification

The gateway now uses (Channel, ConversationId, ChannelMessageId) for EF lookup,
database uniqueness and the fake gateway's in-memory receipt identity. The CLI-
generated migration creates each replacement index before dropping the prior one,
inside a transaction. Existing inbox rows and monitoring stamps survive upgrade;
downgrade refuses conflicting cross-chat rows without deleting them.

Original Code/landing owner: `3e82b1eb-d660-47b9-aaf1-e8d5f79c9f66`.
Branch: `feat/card-task-3e82b1eb`.
Worktree: `/work/worktrees/task-3e82b1eb`.
Base: `a02389cab727d508532e6d3db8ff6cbcf3eb7366`.
Red-first source: `3c738de5043d2678ebc8112b4b2b9d3a12d62e88`.
Implementation/ordinary-test source: `5f32647dc585fdeeacfe90a2b4eee209fd18cf58`.
This file is frozen before the registry repair's CP-4 qualification. Final receipt
facts belong in `.antiphon/task-3e82b1eb.md` and the caller report, not a relabeling
of these earlier tested SHAs.

## Findings

GatewayIngressService publishes to Kafka before receipt storage. The old lookup
could return another chat's row; the old index could reject a separate chat's
insert, with the 23505 swallowed. InboxConsumerService's stored log therefore did
not prove a distinct receipt existed. This demonstrates lost inbox persistence
and monitoring evidence, not a confirmed downstream Kafka-message drop.

CP-1 before production changes: 15 executed, 8 passed, 7 failed, 0 skipped. The
seven failures are the three EF collision/backfill cases, two memory arguments,
the PostgreSQL upgrade case and the hosted broker-consumer collision case. They
failed at the guarded behavior, not at build or fixture setup.

CP-1 after the fix: 15 passed, no failures/skips. CP-2: 40 passed, no failures/skips.
Fresh TRX class census: EfInboxReceiptStoreTests 9, InboxConsumerServiceTests 3,
InboxConversationMigrationTests 1, InMemoryInboxReceiptStoreTests 2, GatewayTests 7,
InboundUnconsumedMonitorTests 18. V-1 through V-6 and R-1/R-2 passed.

R-3 whole Unit is UNQUALIFIED. CP-3 timed out at its original 40-minute deadline
without a terminal TRX; executed/passed/failed/skipped totals are unavailable.
Its partial console has 21 failure messages and 37 platform/environment skip
messages. CP-5 at the task base reproduced the first 17 failures (59 executed,
42 passed, 17 failed); CP-6 reused that certified base build and reproduced the
remaining four cases (4 executed, 0 passed, 4 failed). Both base probes had zero
skips. All 21 observed failures are inherited. The timeout itself was not repeated
at base, and its cause is unverified; the earlier CARD-1253 plan records a similar
whole-Unit timeout, which is context rather than a substitute for current proof.

CP-4 initially had 3 executed, 2 passed, 1 failed. The slow registry parser requires
comments on preceding lines. The existing Kafka entry's failure reproduced at
base (1 executed, 1 failed), and the new migration entry copied the invalid inline
format. S3 corrects both registrations, retaining the classes and their Slow tags;
the final CP-4 rerun is pending at this report's freeze point.

The written brief conflicts: it says no whole-Unit lane and later supplies a Final
profile requiring the whole Unit lane. Qualification-scope clarification was
requested. No waiver or whole-Unit green is inferred from elapsed time or silence.

## Validation and disclosures

Both the tool validator and PowerShell receipt validator accepted CP-1/CP-2 at
5f32647dc585fdeeacfe90a2b4eee209fd18cf58: clean source, dirty=0, verified build
provenance, all intended classes/methods present with nonzero fresh TRX counts.
The full four-row run is not qualified. Red-first and base diagnostics are not
green certificates; both validators reject the failed rows. The first red run
also retained outputs with a cleanup refusal. Later runs explicitly kept their
owned outputs for inspection, followed by literal-path cleanup.

Offline EF-generated Up/Down SQL was inspected: START TRANSACTION, CREATE UNIQUE
INDEX, DROP INDEX, migration-history update, COMMIT. No inbox-row deletion or table
recreation. The PostgreSQL test exercises populated upgrade, true-duplicate
SQLSTATE/index name, safe conflicting downgrade, and rollback/re-upgrade after
explicit fixture-only collision resolution. R-4 manual migration work is done.
Deployment/live Telegram acceptance is outside Code. Restart: none.

Auxiliary operations outside the original four-row ordinary list:

- Checkpoint tool bootstrap build: required executable; slot=granted, waited=165s,
  0 errors and 2 warnings.
- EF model build for CLI migration scaffolding: slot=granted, waited=0s, 0 errors,
  0 warnings. Restored repository dotnet-ef 9.0.20 first. One no-build CLI metadata
  attempt looked for an extra net9.0 path; AppendTargetFrameworkToOutputPath=false
  corrected the lookup without another build.
- Offline migration Up and Down SQL generation: no build, no database apply;
  each slot=granted, waited=0s.
- Necessary base CP-4, CP-5 and CP-6 diagnostics established inherited failures;
  CP-6 reused the verified base build, with no new compilation.
- One malformed expected-SHA argument was refused before the red-first driver
  launched; corrected to the actual git rev-parse value. No tests ran on that
  refused invocation.
- The main Unit build had 1072 warnings and 0 errors. No warning/assertion was
  suppressed. There was no full-assembly run or deliberate Mutation.

Only CP-1 was repeated red-to-green before this freeze. Green rows are not repeated
after the registry-only repair; CP-4 is rerun. No timeout was widened. All PC-1
through PC-6 variants remain pending in
`docs/superpowers/mutations/2026-10-14-card-1248-inbox-key-candidate.md`.

The first fetched-master merge was already up to date at the task base;
git merge-tree --write-tree exited 0. The full-range evidence guard through
5f32647dc585fdeeacfe90a2b4eee209fd18cf58 found zero violations. Final publication,
full-range guard and clean-worktree facts are recorded after the S3 commit.

## Unedited checkpoint receipts

The following lines are copied directly from the generated report JSON. Generated
TRX, JSON, logs and checkpoint folders remain ignored.

```
CHECKPOINT CP-1 commit=3c738de5043d2678ebc8112b4b2b9d3a12d62e88 build=ok filter=/*/*/(EfInboxReceiptStoreTests*)|(InMemoryInboxReceiptStoreTests*)|(InboxConversationMigrationTests*)|(InboxConsumerServiceTests*)/* executed=15 passed=8 failed=7 skipped=0 trx=/work/worktrees/task-3e82b1eb/.antiphon/checkpoints/20261014-172245-642d/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=3c738de5043d2678ebc8112b4b2b9d3a12d62e88 sourceState=clean buildSource=verified
```

```
CHECKPOINT CP-1 commit=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 build=ok filter=/*/*/(EfInboxReceiptStoreTests*)|(InMemoryInboxReceiptStoreTests*)|(InboxConversationMigrationTests*)|(InboxConsumerServiceTests*)/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-3e82b1eb/.antiphon/checkpoints/20261014-172927-0fc2/rows/CP-1/run.trx slot=granted waited=30s dirty=0 source=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 build=ok filter=/*/*/(EfInboxReceiptStoreTests*)|(InMemoryInboxReceiptStoreTests*)|(InboxConversationMigrationTests*)|(InboxConsumerServiceTests*)|(GatewayTests*)|(InboundUnconsumedMonitorTests*)/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-3e82b1eb/.antiphon/checkpoints/20261014-172927-0fc2/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 build=reused filter=/*/*/*/*[Category=Unit] executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-3e82b1eb/.antiphon/checkpoints/20261014-172927-0fc2/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 build=ok filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=40m slot=granted waited=0s dirty=0 source=5f32647dc585fdeeacfe90a2b4eee209fd18cf58 sourceState=clean buildSource=verified
```

```
CHECKPOINT CP-4 commit=a02389cab727d508532e6d3db8ff6cbcf3eb7366 build=ok filter=/*/*/*/*[Category=Unit] executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-3e82b1eb-baseline/.antiphon/checkpoints/20261014-173644-f2ee/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a02389cab727d508532e6d3db8ff6cbcf3eb7366 sourceState=clean buildSource=verified
```

```
CHECKPOINT CP-5 commit=a02389cab727d508532e6d3db8ff6cbcf3eb7366 build=ok filter=/*/*/(TestClassificationGuardTests*)|(CheckpointImportTests*)|(CheckpointLaunchCleanupTests*)|(CheckpointProcessIdentityTests*)|(CheckpointRunOwnershipTests*)|(RerunPolicyTests*)|(ShadowCopyTests*)/* executed=59 passed=42 failed=17 skipped=0 trx=/work/worktrees/task-3e82b1eb-baseline/.antiphon/checkpoints/20261014-175450-4023/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=a02389cab727d508532e6d3db8ff6cbcf3eb7366 sourceState=clean buildSource=verified
```

```
CHECKPOINT CP-6 commit=a02389cab727d508532e6d3db8ff6cbcf3eb7366 build=reused filter=/*/*/(TimeoutTests*)|(RemoteScriptContractTests*)/(total_deadline_skips_a_queued_build_without_starting_it*)|(total_deadline_kills_running_and_skips_queued*)|(C1066_Fixture_npm_install_uses_argument_log_under_nounset*) executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-3e82b1eb-baseline/.antiphon/checkpoints/CP-6-20261014-182039-7847/run.trx slot=granted waited=0s dirty=0 source=a02389cab727d508532e6d3db8ff6cbcf3eb7366 sourceState=clean buildSource=verified
```

