# CARD-1844 S2 Code report (task 8bbde9af)

Outcome: S2 done. `scripts/bootstrap-check.ps1` has a read-only remote Postgres probe. Red witness first, then green. CP-2 is GREEN. Every mutant goes red. Guards, touched class, evidence-diff and merge-tree are clean.

- Branch: `feat/card-task-8bbde9af`, worktree `/work/worktrees/task-8bbde9af` (runner mirror). Task base: S1 tip `db30828432aa72db6b91c7ee01db1fc062603954`.
- Plan: `docs/superpowers/plans/2026-11-09-card-1844-postgres-to-server2.md` (S2, CP-2, V-2/V-3/R-2).
- Original Code task (landing owner): 8bbde9af.

## Commits

| Role | SHA |
|---|---|
| red witness (V-2, V-3, R-2 pins) | `a4ce8a70f9f28ab8db0decbe0f787aae04b41bcc` |
| red witness amendment (adds the behavioural stubbed-section R-2 case) | `3b44058252fafbbe7279afb5c329242f66a65f3d` |
| fix | `7a0696dbd8320b25054107377d909daf2812e83d` |
| tested SHA (CP-2, touched class, guards) | `7a0696dbd8320b25054107377d909daf2812e83d` |

This report is committed on top of the tested SHA. It changes no source.

## Change

- New file `scripts/lib/DatabaseEndpoint.ps1`. It is ASCII and avoids anything Windows PowerShell 5.1 cannot run (`DbConnectionStringBuilder`, no pwsh-7 syntax).
  - `Get-AntiphonDatabaseEndpoint` returns `{Host, Port, Mode}` and nothing else. It returns `$null` when the host is missing, the port is not 1-65535, or the string does not parse. The port defaults to 5432.
  - The mode is `local` only for `localhost`, `127.0.0.1` or `::1` on port 17280. Every other endpoint is `remote`.
  - `Resolve-AntiphonDatabaseEndpoint` checks three sources in order: `-ConnectionString`, then the `aspire-antiphon-apphost` user-secrets `secrets.json`, then the tracked `Antiphon.AppHost/appsettings.json`.
  - The secrets path is `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`, or `~/.microsoft/usersecrets/<id>/` when APPDATA is unset. Both the flat `ConnectionStrings:DefaultConnection` key and the nested form are read.
  - It never runs `dotnet user-secrets`. A secrets file that does not parse resolves to nothing (fails closed). The script then reports `database / endpoint` FAIL and does not fall back to localhost.
- `scripts/bootstrap-check.ps1` gains `-ConnectionString` and `-DatabaseProbeOnly`.
  - The probe prints exactly `database-endpoint host=<h> port=<p> mode=<local|remote>` and exits 0. It exits before any docker call, socket, agent-context check or `Write-Check`. An unresolved endpoint prints `database-endpoint unresolved source=<s>` and exits 1.
  - Without the switch, a new `database / endpoint` line reports host, port, mode and source.
  - Local mode: the existing container, volume, pg_isready and `127.0.0.1:17280` blocks are unchanged, only re-indented inside `elseif ($databaseEndpoint.Mode -eq 'local')`. `git diff -w` shows no change to them.
  - Remote mode: the container and volume lines are a new `SKIP` status, which affects no count. The script TCP-checks the resolved host and port. The FAIL remedial points to the server2 host compose (`docker-compose.server2-postgres.yml`, project `antiphon-server2-postgres`).
  - No connection string, user or password is printed.
- Out of scope and untouched: production cutover, the host compose, S3 docs, appsettings, migrations (none), and `tests/test-execution-policy.json` (unchanged, so no policyHash or PinnedInputDigest refresh).

## Witness `scripts/test-bootstrap-database-endpoint.ps1` (pwsh 7, 62 assertions, about 11 s)

- **V-2:** a child `pwsh -File bootstrap-check.ps1 -DatabaseProbeOnly` with `Host=100.64.0.2;Port=5432;Password=secret-value;...` prints the exact remote line. Neither `secret-value` nor `Username=antiphon` appears on stdout or stderr. A docker shim first on PATH records no call. A positive control proves the shim records a call.
- **V-3:**
  - localhost:17280 gives the local line.
  - With no `-ConnectionString` and an empty scratch APPDATA (the real secrets file is never read), the tracked appsettings gives the local line.
  - User-secrets fixtures in both the flat and the nested form outrank appsettings.
  - Library cases cover loopback variants, port 5432 on localhost giving remote, the default port, and six unresolved inputs. The endpoint has only the properties Host, Port and Mode.
- **R-2:**
  - AST pins on the source:
    - The probe if is a top-level statement and ends in exit.
    - No docker, socket or check call runs before it.
    - All 21 `antiphon-postgres`, `antiphon_pgdata`, `17280`, `pg_isready` and `docker-compose.dev.yml` literals are inside a `$databaseEndpoint.Mode -eq 'local'` branch.
    - The remote `else` branch calls `Test-TcpPort` with `$databaseEndpoint.Host` and `$databaseEndpoint.Port`.
    - Both files are ASCII.
  - Behavioural check: the two gated sections run in process with stubs that record every `Write-Check`, `Test-TcpPort` and `Get-NativeOutput` call.
    - Remote mode, with the port open and closed, runs no native call and exactly one TCP check, to 100.64.0.2:5432, and names no local artefact.
    - Local mode inspects `antiphon-postgres` and `antiphon_pgdata`, runs pg_isready and checks `127.0.0.1:17280`.

### Red / green

| Run | Tree | Result |
|---|---|---|
| red | `db3082843` + witness (a4ce8a70) | 16 pass / 15 fail, exit 1 |
| red | archive of `3b4405825` | 20 pass / 24 fail, exit 1 |
| green | fix `7a0696dbd` | 62 pass / 0 fail, exit 0 |

Unlisted runs, with reasons:
- Red-first witness runs: one at base, one on a scratch archive of `3b4405825`.
- Two green authoring iterations before the fix commit.
- These four were quick pwsh runs of about 10 s each, without a build-slot lease. I should have wrapped them; the mutant runs below were leased.

### Mutants

Each mutant ran on a git-archive scratch copy through `-SourceRoot`, one lease per mutant. All were restored, since the worktree was never edited.

| Mutant | Change | Witness result |
|---|---|---|
| M-3a | the remote branch also runs `volume inspect antiphon_pgdata` | RED 57/5: ungated literal at line 448; remote runs native; remote names a local artefact |
| M-3b | the TCP gate becomes `elseif ($true)` | RED, 10 or more FAIL: ungated lines; gate count; remote TCP; local TCP |
| M-4 | the probe line appends `$ConnectionString` | RED 53/9: exact line, password and connection-string assertions |
| M-8 | loopback on 17280 reports `remote` | RED 57/5: V-3 local, tracked appsettings and three lib loopback cases |

Slot leases: c1844-mutant-m3a waited=15s; m3b, m4 and m8 waited=0s.

## Checkpoints

The run went through the checkpoint tool (`start --plan <plan> --rows CP-2 --expected-source-sha 7a0696dbd...`), then `wait`, then `validate`. Run id `20261111-212444-55a9`.

```
CHECKPOINT CP-2 commit=7a0696dbd8320b25054107377d909daf2812e83d build=n/a filter=pwsh -NoProfile -File scripts/test-bootstrap-database-endpoint.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=15s dirty=0 source=7a0696dbd8320b25054107377d909daf2812e83d sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-2 waited=15s executed=11.695s wall=26.925s
verdict: GREEN exit=0
```

- Row console: `bootstrap-database-endpoint passed=62 failed=0`. It is a command row, so the CHECKPOINT line carries n/a counts.
- `validate --evidence report.json --expected-source-sha 7a0696dbd...` gave `CHECKPOINT SOURCE VALID source=7a0696dbd8320b25054107377d909daf2812e83d rows=1`.
- Evidence: `.antiphon/checkpoints/20261111-212444-55a9/` (gitignored).
- CP-1 (S1) and CP-3 (S3) were not run. CP-1 is S1's row, its files are unchanged here and it is green per S1. CP-3 belongs to S3.

## Ordinary scope (one build, leased: `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, 4m15s, waited=0s)

- **Impacted classes.** `scripts/impacted-classes.ps1 -Range db3082843..HEAD` returns `/*/*/(CardFileBootstrapCheckTests*)/*`, from the derived index row for `scripts/bootstrap-check.ps1`.
- **Touched class.** One leased `--no-build` run of `CardFileBootstrapCheckTests` gave 3/3 passed, 0 skipped. The fresh TRX shows `Antiphon.Tests.Scripts.CardFileBootstrapCheckTests` x3. The lease was granted with waited=0s.
- **Guard batch.** One leased run of `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)` gave 77/77 passed, 0 skipped, in 88 s. The lease was granted with waited=0s.

  | Class | Tests |
  |---|---|
  | TestClassificationPolicyTests | 41 |
  | MechanicalPcContractTests | 11 |
  | C1729LayeringGuardTests | 9 |
  | TaskPlatformGuidanceTests | 5 |
  | SlowTestRegistryMergeTests | 5 |
  | NativeLinuxPolicyWitnessTests | 3 |
  | SlowTestTripwireTests | 2 |
  | TestClassificationGuardTests | 1 |

- **check-evidence-diff.** `-BaseRef db3082843... -HeadRef HEAD` gave `commits=3 entries=0 violations=0`, exit 0. The range was measured at the tested SHA.
- **merge-tree.** `git merge-tree --write-tree origin/master HEAD` (origin/master `3db1a43b02246706dc28075fce7e7b5e84804ddb`) is clean, exit 0, tree `9ed01f264da3dd17032a3152473b1548db984a0f`.
- **Cleanup.** All bin/obj output from this task's builds was removed by literal path. The worktree is clean.

## Owed and pending

- **Windows PowerShell 5.1 parse** of `scripts/bootstrap-check.ps1` and `scripts/lib/DatabaseEndpoint.ps1` is owed on the desktop (plan "Owed natively on Windows"). Both files are ASCII, and the witness checks that. The witness itself needs pwsh 7.
- **Live full-mode run** of `bootstrap-check.ps1` against the real secret is not run, because R-2 forbids it. It is operator step 8 after the cutover window.
- **PCs for Mutation:** none are declared in the plan. M-3, M-4 and M-8 above were run here as Code-stage mutants. SourceLanding Mutation still owns the method-scoped pass, and none is marked discharged.
- **S3 still to do:** docs, CP-3, and V-4/R-3.
