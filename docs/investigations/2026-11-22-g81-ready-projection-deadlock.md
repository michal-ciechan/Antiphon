# rc group 81 ready projection deadlock

Windows Full rc run `3528e6b1` at `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`
reported PostgreSQL `40P01` for
`RemoteWorkspaceReadinessTests.C1349_ReadyProjectionSurvivesLostNotification(concurrent capture)`
in `antiphon-all-group-81.trx` after 3.782 seconds.
[Run analysis](2026-11-22-rc-run-3528e6b1-final-analysis.md)

## Capture lock order

The test waits until the mirror request has been sent and its preparation binding
committed. It then races two fresh contexts on the same task and preparation.
Each `CaptureMirrorBindingAsync` opens a transaction and takes
`SELECT "Id" FROM "AgentTasks" WHERE "Id" = … FOR UPDATE` first. It reads the
`TaskWorktreeMirrorBindings` row by `PreparationId` and saves the returned binding.
`RecordPathAsync` uses the same transaction and re-locks the same task row
with a full-entity `SELECT … FOR UPDATE`. It checks the attempt, runner and path,
updates that task, then reads its latest Held or Dispatched event. It inserts
a ready Held event only when the latest hold is still in flight. The task-row
lock serializes the two captures through commit. The second capture sees the first
capture's ready event, and the later scripted runner reply exercises repeat capture
and the notification. The case checks for one persistent ready event and one hint.
[Capture](../../server/Application/Services/RemoteWorkspaceService.cs#L477)
[Projection](../../server/Application/Services/RemoteWorkspaceReadiness.cs#L22)
[Test](../../tests/Antiphon.Tests/Application/RemoteWorkspaceReadinessTests.cs#L143)

The rc fixture interceptor from `000375cf3440441cf3d70430eea1cf85443e79c1`
could introduce a lock upgrade. Capture A takes the row lock. Capture B takes
`ROW SHARE` on `AgentTasks` and waits for A's row lock. A's full-entity
query names `RequestedModelAlias`, so the rc interceptor runs
`ALTER TABLE "AgentTasks" ADD COLUMN IF NOT EXISTS "RequestedModelAlias" …`.
The requested `ACCESS EXCLUSIVE` waits for B's `ROW SHARE`, completing the cycle.
Master `1cdc778c13c445543e18ca4048d4806d7b1f1994` skips that DDL when the
migrated fixture column is visible. This is the lock cycle inferred from the source
and PostgreSQL lock modes. The Windows TRX redacted the lock graph.
[PostgreSQL 16 lock modes](https://www.postgresql.org/docs/16/explicit-locking.html)
[Shared fixture investigation and pre-land witness](2026-11-21-why-missed-rc-run-3528e6b1-group-86.md)

## Linux master replay

Covered by the master fix. At `70c13dfe001c5df6c2108000584da6151ce42e43`,
one Linux build took 246.11 seconds after the load gate admitted it at 12.10.
The build and each of the eight serial `--no-build` runs had a host build
slot and used `ANTIPHON_LINUX_COMPILER_POLICY=0`. The test hosts used disposable
Testcontainers PostgreSQL databases. Each TRX had one matching result, and
phase timing was recorded for every run.

| Replay | Result | TRX seconds | Driver wall seconds |
| --- | --- | ---: | ---: |
| 1 | Passed | 10.52 | 40 |
| 2 | Passed | 17.84 | 51 |
| 3 | Passed | 12.52 | 41 |
| 4 | Passed | 9.75 | 36 |
| 5 | Passed | 11.08 | 39 |
| 6 | Passed | 11.89 | 37 |
| 7 | Passed | 12.34 | 37 |
| 8 | Passed | 10.81 | 37 |

Eight passed, zero failed, zero skipped and zero reported `40P01`. The rc
candidate and tested master have identical capture, readiness and test source.
The next integrated Windows candidate still owes the native group-81 rerun.

To repeat a single case on the built source, use the UID filter and a fresh
results directory. The checkpoint tool's TUnit driver uses a tree filter, which
would select all six argument cases of this method.
[Checkpoint driver](../../tools/Antiphon.Checkpoints/Execution/BuildStep.cs#L81)
[TUnit 1.44 filter implementation](https://github.com/thomhurst/TUnit/blob/v1.44.0/TUnit.Engine/Services/TestFilterService.cs#L135-L202)

```sh
export ANTIPHON_LINUX_COMPILER_POLICY=0
uid='Antiphon.Tests.Application.RemoteWorkspaceReadinessTests.1.1.C1349_ReadyProjectionSurvivesLostNotification(System.String).5.1.0'
results=$(mktemp -d "$PWD/.antiphon/g81-replay.XXXXXX")
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g81-replay -- \
  dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false --no-build -- \
  --filter-uid "$uid" --timeout 570s --report-trx --results-directory "$results"
```

## Why missed

The shared fixture failure escaped the short merge selection when the historical
alias-column shim landed. This Integration case exercises the shim indirectly
through the migrated clone and two live transactions. The group-86 investigation
records that miss and the focused alias lock witness.
[Shared fixture investigation](2026-11-21-why-missed-rc-run-3528e6b1-group-86.md#outside-the-selection)
