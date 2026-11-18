# Postgres move to server2, gap G-4 (daily backup retarget): checkpoints

Code task `b92e5206`, stacked on the part A tip `2679a4db8c` (Code `801355c0`: G-1 dev-aspire opt-out and G-2 image pin stay). The design is the reviewed plan [2026-11-18-postgres-backup-retarget-plan.md](2026-11-18-postgres-backup-retarget-plan.md) (Docs `36161193` tip `d04fa1304`, Review round 2 clean). Part A's superseded G-4 commits `830d7e92b8` and `e1031dbc71` are reverted by `db18cdd75b` and `50f5519f8f`. Part A's G-1/G-2 rows stay green there and are not re-run here.

Script-only. No Docker daemon, no ssh, no production database, no desktop run. The server2 cases use a POSIX `sh` docker fake that logs one argv per line (`Install-SnapshotServer2Fake`), so they SKIP on Windows like the other fake cases. `scripts/cutover-postgres-server2.ps1` and its two test scripts belong to part B (`6a008315`) and are not touched: this task provides `Set-SnapshotTarget` and its witness V-15 only.

Version rule (operator 2026-11-18): the move keeps PostgreSQL 16.14 on Debian pgdg13, pinned as `postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`.

## Verification design

The V-n and R-n ids are the reviewed plan's. Each slice's V cases were committed and run red before its fix:

- S1 (V-1..V-6, V-14): red at `ef486f0487` with the scripts at base, 45 passed, 42 failed; green at `60dffbe0ae`, 106 passed, 0 failed.
- S2 (V-7..V-10, V-15): red at `737fda788c`, job-status 77 passed, 5 failed; green at `557fc0b137`, 140 passed, 0 failed.
- S3 (V-11..V-13): red at `35a29991d6` with the S2 script, 29 passed, 21 failed (V-12 `PlainDrillDesktopRefused` is green by design: it pins the refusal the desktop keeps); green at `d037f383da`, 50 passed, 0 failed.

S3 moves the age drill's guard prefix into `Invoke-SnapshotDrillScratch` unchanged. CP-16 (`DrillRefusesIdPrefix`) covers that prefix. A one-off unlisted run of `DrillRefusesIdPrefix`, `DrillRemovesScratch`, `DrillRefusesTaintedReuse`, `DrillPicksExactSnapshot`, `DrillReusesImageEnvScratch`, `DrillRefusesOwnVolume` and `DrillRefusesForeignVolume` passed 165, failed 0 at `e153864341`.

No PC-n is named. Mutation is waived (operator 2026-11-12) and is not discharged. R-3 (`PosixHarnessToolTests` snapshot pair) is not a row: no case is renamed and the C# list is unchanged. The full snapshot harness is Review's one-off run, not a row (SHORT rule).

Owed off-row, operator only: the Windows PowerShell 5.1 parse of `postgres-snapshot.ps1`, `invoke-postgres-snapshot.ps1` and `job-status.ps1` (plan W-1), the P-1..P-3 prerequisites, and the window steps W-0..W-6.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetDesktopUnchanged` | V-1 | 14 passed, 0 failed, exit 0 | n/a | 15 |
| CP-2 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2Plan` | V-2 | 12 passed, 0 failed, exit 0 | n/a | 1 |
| CP-3 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2ContextOnEveryCall` | V-3 | 11 passed, 0 failed, exit 0 | n/a | 1 |
| CP-4 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2RefusesTcp` | V-4 | 15 passed, 0 failed, exit 0 | n/a | 1 |
| CP-5 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2SourceMismatch` | V-5 | 20 passed, 0 failed, exit 0 | n/a | 1 |
| CP-6 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetStatusFields` | V-6 | 20 passed, 0 failed, exit 0 | n/a | 1 |
| CP-7 | S2 | n/a | job-status | `pwsh -NoProfile -File scripts/test-job-status.ps1` | V-7, V-8, V-9, V-10, V-15, R-2 | 140 passed, 0 failed, exit 0 | n/a | 2 |
| CP-8 | S3 | n/a | snapshot-drill | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainDrillServer2` | V-11 | 23 passed, 0 failed, exit 0 | n/a | 1 |
| CP-9 | S3 | n/a | snapshot-drill | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainDrillDesktopRefused` | V-12 | 12 passed, 0 failed, exit 0 | n/a | 1 |
| CP-10 | S3 | n/a | snapshot-drill | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainDrillRefusesProduction` | V-13 | 15 passed, 0 failed, exit 0 | n/a | 1 |
| CP-11 | all | n/a | snapshot-regression | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainPublishPruneOrder` | R-1 | 18 passed, 0 failed, exit 0 | n/a | 1 |
| CP-12 | all | n/a | snapshot-regression | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case DockerDaemonPinned` | R-1 | 0 failed, exit 0 | n/a | 1 |
| CP-13 | all | n/a | snapshot-regression | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainStatusNoSecret` | R-1 | 0 failed, exit 0 | n/a | 1 |
| CP-14 | all | n/a | snapshot-regression | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case PlainDrillRefused` | R-1 | 0 failed, exit 0 | n/a | 1 |
| CP-15 | S2 | n/a | job-status | `pwsh -NoProfile -File scripts/test-job-status.ps1 -Case TargetRollbackRestoresDesktop` | V-15 (W-5 rollback write-back) | 24 passed, 0 failed, exit 0 | n/a | 1 |
| CP-16 | all | n/a | snapshot-regression | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case DrillRefusesIdPrefix` | R-1 | 0 failed, exit 0 | n/a | 1 |
| CP-17 | S1 | n/a | snapshot-target | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1 -Case TargetServer2RefusesContainer` | V-14 | 14 passed, 0 failed, exit 0 | n/a | 1 |

Lane: server2 (the runner default). No `-Runner`, no `-Platform`. Rows run serially (`--serial`), one run per After group (S1, S2, then S3 with all).

### Cost

The ordinary floor is 32 minutes (the sum of EstimatedMinutes; CP-1 carries the cold first-row budget). Each case measured under 1 minute on server2 at a 1-minute load of 12 to 16. The job-status harness is about 10 s.

### Receipts

Both runs were at `b4c6d79cc91f4ae49ac0bfa16bc73a0550f49099`, serial, every row `slot=granted sourceState=clean buildSource=notApplicable`, verdict GREEN, `validate` printed `CHECKPOINT SOURCE VALID`:

- `20261118-075153-813e` (`--after S1`, 12 rows): CP-1 14/0, CP-2 12/0, CP-3 11/0, CP-4 15/0, CP-5 20/0, CP-6 20/0, CP-11 18/0, CP-12 33/0, CP-13 16/0, CP-14 4/0, CP-16 10/0, CP-17 14/0 (passed/failed from each row's console).
- `20261118-080435-7f77` (`--rows CP-7,CP-15,CP-8,CP-9,CP-10`): CP-7 140/0, CP-8 23/0, CP-9 12/0, CP-10 15/0, CP-15 24/0.
