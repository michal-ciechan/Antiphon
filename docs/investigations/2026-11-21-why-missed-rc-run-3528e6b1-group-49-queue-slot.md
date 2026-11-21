# Why Windows Full run 3528e6b1 missed the last queue slot race

Windows Full run `3528e6b1` on `rc/2026-11-30` at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` reported one failure in
`antiphon-all-group-49.trx`.
`DispatchConcurrencyAdmissionTests.Concurrent_last_queue_slot_has_one_winner`
hit the 180-second TUnit deadline at 3m01s with `WaitingForActivation`. That
status identifies an unfinished async test. The timeout stack in the brief
does not identify which operation was still running.

## Introducing commits

- `af6bc6b926a40f4c52c6c35f22f4ee8de96181dc` (CARD-0505) introduced the
  race witness. The two creates use separate EF contexts and real PostgreSQL
  transactions in one fixture database. The first pauses at save with the
  admission advisory lock held. The second must wait for that lock. The
  three schedules cover the project cap, the role cap and the unscoped cap.
- `000375cf3440441cf3d70430eea1cf85443e79c1` installed the alias-schema
  interceptor in the fixture options. Its unconditional
  `ALTER TABLE "AgentTasks" ADD COLUMN IF NOT EXISTS "RequestedModelAlias"`
  still asks PostgreSQL for `AccessExclusiveLock` on already migrated clones.

## Lock sequence

A has read `AgentTasks` under its admission transaction and is paused in the
save interceptor. B's full-row query in `FindLaunchFailureRepeatAsync` names
`RequestedModelAlias` before B reaches the create gate. On the rc, the
interceptor asks for the exclusive relation lock before executing that query.
A's transaction holds an `AccessShareLock` from its queue count. A is
waiting on the test's save pause, so PostgreSQL sees only B waiting on A.
The old probe looks for an advisory waiter, so B cannot satisfy it. Its
nominal 20-second polling window did not govern the probe's connection open
or SQL calls. The early setup and racer waits also lacked a cancellation
token. On probe failure the old test released the pause and unwound its
contexts without joining the creates.

Both creates take the same advisory lock before the final count and insert.
The fixture uses Npgsql's non-retrying execution strategy. Each clone has a
distinct connection string and pool. The old race uses at most three slots
of the 100-connection default pool at its pause point. This made an
artificial one-connection pressure run unrepresentative of the failure.

Master's fixture fix, `1cdc778c13c445543e18ca4048d4806d7b1f1994`, skips
the DDL once the alias column is visible. The same interceptor caused two
independently reproduced dispatch-claim failures. The claim and create
schedules reach the relation conflict in different directions. [Group 86 investigation](2026-11-21-why-missed-rc-run-3528e6b1-group-86.md)

## Linux evidence and test repair

A dedicated PostgreSQL 16 fixture container reproduced the lock conflict
using the preflight DDL and A's queue count inside its transaction. The
live lock query returned
`relation | AccessExclusiveLock | AccessShareLock | 0 advisory waiters`
for B waiting on A. Both sessions settled when A rolled back.

At master `3ed773829e584eec0c93ec8b47bf3517a69204d7`, checkpoint
`20261121-200117-18ea` built once at one-minute load 13.38 and ran the exact
method five times against Testcontainers Postgres. All five passed. TRX
test-body times were 4.59s, 4.66s, 7.81s, 7.92s and 3.42s. Host invocations
took 30.46s, 28.46s, 31.88s, 41.19s and 40.39s. The five samples are
reproduction evidence, with no measured Windows p95.

Test commit `79ff0d00aa4f88ffcac61226121038301a95b2cc` keeps all three
schedules and the 180-second TUnit cap. It names the scenario and phase
within a 50-second cancellation window per schedule. Its 20-second
observer window now covers the connection open and queries. The observer
binds both PostgreSQL backends and reports a relation wait immediately.
Release and cancellation run on every exit, with the racer operations
joined before disposing their contexts. Checkpoint `20261121-201408-895b`
started the build at load 13.20 and executed one exact result, which passed
in 4.19s of test time and 29.28s of host wall time.

Historical checkpoint `20261121-201842-f259` rebuilt the same test commit
with only the original rc interceptor blob
`5ef575c396b1c401a236d54f5e2937cebe291252` substituted at compile time.
The build started at load 13.97. The exact method executed once and failed
at the project queued cap in 4.79s of test time and 33.97s of host wall
time. The probe expected `advisory` and observed `relation`, with both
backends identified. This establishes the pre-gate obstruction with the
rc interceptor. The Windows TRX does not show which await was active
at its three-minute cutoff.

## Approving review and earlier rc selections

The CARD-0505 carrier `a646a004dd76ed8ffc2d444a4c41152c6f4e005d` records
reviewed tree `4554fed6a692f8ccd582da52759e44883d5e776e`. CARD-0505's CP-2
selected `DispatchConcurrencyAdmissionTests` with the other admission
classes and recorded 94 passing results. That October coverage preceded the
alias interceptor. The November fixture/roster commit has no approving review receipt with an
exact admission selection in the available evidence. The short Merge set
did not select this Integration test. [CARD-0505 checkpoints](../superpowers/plans/2026-09-30-card-0505-configurable-dispatch-concurrency-plan.md#checkpoints)

All seven preceding rc cuts from `rc/2026-11-23` through `rc/2026-11-29`
contain the test and lack the alias interceptor file. The immediately
preceding Full run, `e7f92f66`, was on
`26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`. Source history explains why
those cuts could not hit this interceptor path. Test-by-test results from
all seven runs and the operation that occupied the entire 180-second
Windows budget remain unverified.

## Cheapest pre-land check

`tests/Antiphon.Tests/TestHelpers/RequestedModelAliasSchema.cs` →
`/*/*/RequestedModelAliasSchemaTests/Open_transaction_does_not_keep_access_exclusive_on_agent_tasks`.

Add the exact create schedule when validating the combined fixture and
admission path.
`/*/Antiphon.Tests.Application/DispatchConcurrencyAdmissionTests/Concurrent_last_queue_slot_has_one_winner`.

## Exemption

Group 86 documents the shared interceptor cause and its underlying red
witness. This note records the group-49 create schedule and the caller's
requested native rerun on the next integrated Windows candidate.
