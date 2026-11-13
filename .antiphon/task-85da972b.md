# Review 85da972b: CARD-1844 round 2 (diff 0d983100e..b82f4eccd)

Verdict: CLEAN. F1 and F2 from Review 37deb941 are repaired. No blockers or regressions.
ordinaryScopeCompleted: Interim, because HangfirePostgreSqlStorageTests (a database class from the S1/S2 impacted selection) could not run here: no test Postgres at 127.0.0.1:54329 on server2.

Subject: feat/card-task-0303dd11 @ b82f4eccdd6b7e2a497bf614eef3910990122c97 (Code task 0303dd11-25fb-4f74-9a6c-938ddb17a251, Grok). Original landing owner: 8bbde9af-716d-4bfb-84d0-59b358bca67b.

## Checks

1. Precedence against the real AppHost. I read the Aspire.Hosting 9.3.0 DistributedApplicationBuilder IL directly: `new HostApplicationBuilder(settings{Args})`, then AddInMemoryCollection for AppHost:* keys only and a second AddCommandLine with switch mappings. The effective order is command line > unprefixed environment (with the CUSTOMCONNSTR_/SQLCONNSTR_/SQLAZURECONNSTR_/MYSQLCONNSTR_ aliases) > user secrets (Development only) > appsettings.{env}.json > appsettings.json > DOTNET_-prefixed host configuration. Resolve-AntiphonDefaultConnection follows the same order. Counter-examples covered by CP-2 witnesses:
   - environment beats secret (three key spellings)
   - command line beats environment
   - empty, prose and `Port=broken` secrets fail closed with a reason
   - an explicitly empty `-ConnectionString` fails closed
   - malformed environment fails closed
   - Production excludes Development secrets
   - an absent parent environment follows the launch profile (Development)
   - an empty secret with an absent environment fails closed

   JSON null and whitespace values reduce to `empty`, which fails closed.
2. psql safety. RemoteDatabase.ps1 starts psql with a constant argument string, no shell, `UseShellExecute=false`, `-X -w`. It clears every inherited PG* variable. Credentials go only into the child's environment (PGPASSWORD). PGPASSFILE points at a nonexistent file. PGCONNECT_TIMEOUT is 5 and statement_timeout is 5000 on a read-only transaction. A hard WaitForExit plus Kill, with a 1 s drain, bounds the run. The catch returns only a reason code, never exception or client text. Missing client gives `client-unavailable` (FAIL). The other outcomes each fail with a distinct reason: `identity-mismatch` (wrong or missing database, case-sensitive compare), `authentication-failed`, `protocol-failed` (non-PostgreSQL) and `timeout`. All were proven against real psql 15.18 and ephemeral loopback wire stubs. Timings (ms): ready 333, wrong-db 54, missing-db 40, timeout about 5000 (under the 6500 assertion).
3. Local branch. The bootstrap-check.ps1 diff touches only the header comment, the resolver call (now a splat), the endpoint FAIL text (adds the reason) and the remote else-branch. The local docker, volume, TCP and pg_isready branch is byte-identical. The R-2 local stub assertions pass.
4. No real database or volume. The tests use recording stubs and Card1844PostgresStub, which listens only on an ephemeral loopback port (IPAddress.Loopback, 0). Fixture children strip ConnectionStrings*/DOTNET_*/ASPNETCORE_* from their environment.
5. ASCII and PowerShell 5.1. All three scripts are ASCII-only. I found no PowerShell 7-only construct in the libraries (ProcessStartInfo.EnvironmentVariables, Task.WaitAll and DbConnectionStringBuilder all exist on .NET Framework). The native Windows PowerShell 5.1 parse and probe is still owed (the desktop was out of bounds). The guard union is green.

## Fresh runs at b82f4eccdd6b7e2a497bf614eef3910990122c97 (server2, all leased)

| Row | Result |
|---|---|
| CP-2 (checkpoint run 20261113-033841-bab6) | exit 0; endpoint passed=81 failed=0, protocol passed=34 failed=0; CHECKPOINT SOURCE VALID rows=1 |
| CP-1 (checkpoint run 20261113-041408-e01f) | exit 0; apphost-postgres-owner passed=18 failed=0; CHECKPOINT SOURCE VALID rows=1 |
| Guard union + impacted CardFileBootstrapCheckTests | 81/81, skipped 0 (TestClassificationPolicy 41, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3, MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 9, TestLaneCategoryGuard 1, CardFileBootstrapCheck 3) |
| Round-1 deferrals: AppHostBrokerSourceGuard 2, HangfireStorageSelection 12, LinuxServerRestartGate 17, ProcessSpawnLimit 57 | 88/88, skipped 0 |
| PtyHostDeploymentGuardTests (round-1 deferral, Antiphon.SessionRunner.Tests) | 3/3 |
| DerivedImpactIndexTests.committed_index_matches_the_builder | 1/1 |
| impacted-classes over 0d983100e..b82f4eccd | CardFileBootstrapCheckTests only (run above) |
| check-evidence-diff 0d983100e..b82f4eccd / 3fd231a2e..b82f4eccd | 0 violations (8 / 16 commits) |
| merge-tree vs origin/master fcc4b428a | clean |
| Migration downgrade selector | no migration in range: 0 rows |

Unlisted runs, each with its reason:
- Checkpoint-tool build (needed to run the tool).
- Antiphon.Tests build (6m20s).
- Antiphon.SessionRunner.Tests build (42s).
- Two guard attempts that included the whole DerivedImpactIndexTests class. Each was cut by my 9.5 min foreground cap: the first after a 496 s slot wait, the second while running. Neither produced counts and neither gets credit. Whole-class DerivedImpactIndexTests is the >30 min cost Review 37deb941 already sent to Debug; only its index method is in the impact scope.

## Pending (not discharged)

- HangfirePostgreSqlStorageTests: needs the test Postgres (127.0.0.1:54329 is closed on server2). The round-2 diff does not touch it.
- Native Windows PowerShell 5.1 parse and probe of bootstrap-check.ps1, DatabaseEndpoint.ps1 and RemoteDatabase.ps1.
- Operator cutover acceptance.
- All PCs, for SourceLanding Mutation.

## Advisories (cards; they do not hold the land)

- A-1: An explicit non-Development DOTNET_ENVIRONMENT in the bootstrap shell is treated as effective. dev-aspire.ps1 starts the AppHost with `dotnet run --no-build` and the default launch profile, which sets DOTNET_ENVIRONMENT=Development for the child and may override the inherited value. In that shell the AppHost could load user secrets while bootstrap checks appsettings. This is an edge case.
- A-2: On Linux, environment keys that differ only by case resolve last-wins in enumeration order, as .NET does. There is no witness for this.
- Out of scope: the landed cutover script gaps (phase 3 sequence query, rollback row counts).
