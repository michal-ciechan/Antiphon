# CARD-1844 restore drill plan review, round 1

Final review of `docs/superpowers/plans/2026-11-18-postgres-restore-drill-plan.md`. Blockers and regressions only. Reviewer is Claude Opus (review task `576f6140`). Author is Grok, plan task `c88affea`, tip `f83c34e21e424bc5c9c3e5834303c0469c005280`.

Reviewed range `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4..f83c34e21e424bc5c9c3e5834303c0469c005280`. That is 1 commit with 1 added file (+447), the plan only. Fetched `origin/master` is `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`. `git merge-tree --write-tree origin/master f83c34e21` exited 0 with tree `e9c0b55a86a366a5668f7d78bfa095db1547ff65`. `scripts/check-evidence-diff.ps1 -Repository . -BaseRef e229fb4d3 -HeadRef f83c34e21` reported commits=1, entries=0, violations=0, exit 0.

The subject changes no code and no tests, so the brief's SHORT rows (touched classes and the guard batch) do not apply and were not run. No build or test driver ran. Two existing tests read plans:

- `PlanCatalogServiceTests.Every_plan_already_written_in_this_repo_is_in_the_catalog` counts its own files and does not assert a fixed number. The new file has an `#` heading.
- `PlanCoverageBindingTests` reads pinned fixtures only.

S-1..S-3 are landed script witnesses that the plan says it does not run. M-1..M-6 and the S0 statement pair are manual operator or orchestrator work after `f2de5b30`, so they stay pending. Mutation is waived (operator 2026-11-12). Positive controls stay pending. `reviewedSourceClean` is false because no checkpoint-tool receipt exists for a plan that has no rows.

## Verdict

CLEAN. No blocker and no regression. The two advisories below stand. Neither one holds the land.

## Brief requirements checked against the tree

- **Existing dump, no new dump (D-1).** `scripts/postgres-snapshot.ps1` writes `pg_dump -Fc --no-owner --no-acl` (`:1710-1712`). It writes to `C:\Users\lndco\antiphon-backups` (`:80`) with the pattern `^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$` (`:79`). `snapshot-status.json` says `mode=plain` and `encryption=none` (`:1381`). `Get-SnapshotPlainTableCount` counts `TABLE DATA` lines (`:1335-1341`). M-1 needs exactly one `antiphon-20261117T075*.dump` and refuses `.dump.age` and `20261118` files. The `--no-owner --no-acl` restore flags match how the dump was made.
- **CARD-1406 drill compose.** The blob is `cace91c5a7a94ec4cccf1c55e588982a05114f98`. The project, the pinned volume `antiphon-server2-postgres-drill_pgdata`, loopback `127.0.0.1:5434`, `POSTGRES_USER/DB=antiphon`, `cpus: 4.0`, `mem_limit: 4g`, and the three `-c` flags are as the plan states. The deploy script's volume refusal (`Test-Server2PostgresDrillVolume`) refuses every name except that pinned one.
- **Two probe restores (D-2, D-3, M-4).**
  - `Test-CutoverProbeContainerName` needs `^c197[45]-[a-f0-9]{16}$` and no `antiphon` in the name. The drill compose container cannot be a probe, as the plan says.
  - In the probe, `Invoke-CutoverPsql` runs `docker exec -i <container> psql -U antiphon -d antiphon`. The M-4 `docker run` sets that user and that database.
  - `-VerificationProbe` is handled at `:1082-1084`, before the `-WhatIf` and `-Execute` paths. It refuses `-Execute`, `-Rollback`, and `-ConfirmWindow`.
  - The sequences probe prints `PHASE 3 match: sequences`, the fingerprint, and the read count. The rollback-rows probe prints `ROLLBACK CONTENT fingerprint:`, `ROLLBACK CONTENT compared:`, and `ROLLBACK ROWS clean`. The plan's pass lines match the script.
- **One-microsecond update must refuse (M-5).**
  - `Test-CutoverNewHasExtraRows` returns true when any target table's `count(*)` plus row-JSON md5 fingerprint differs from the source.
  - The probe then fails with `REFUSED: stop gate 6: the new database has rows the old desktop volume lacks. ...` through `Fail-Cutover`, which exits 3.
  - That makes the identical-pair comparison falsifiable. M-5 is the adequacy check, so the pair is not a self-compare that cannot go red.
- **Image (D-4).** `Test-Server2PostgresImage` accepts only `postgres:16@sha256:<64 hex>` or `postgres@sha256:<64 hex>`. It refuses `alpine` anywhere in the reference. The plan pins image id `f40a5645…` through a desktop `docker image inspect`. It does not pull. It never uses floating `postgres:16`, `16.16`, or alpine for the drill or the probes.
- **Desktop SQL.** The only production SQL is the S0 `pg_stat_statements` pair, owned by the orchestrator. It runs once after `f2de5b30`, with no reset and no `docker stats` loop. The plan explains why it rejects `postgres-jit-window.ps1`.
- **Sizing targets.** Each of the four GUCs is listed as unset in compose, with a target for a later change. `shared_buffers` is 1 GB. `work_mem` and `max_connections` keep the image default. `max_parallel_workers_per_gather` is 2. Nothing changes in the drill.
- **Drill versus script tests.** S-1..S-3 are script tests and are not re-run. M-1..M-6 are manual and are not a checkpoint row. The plan says not to import its table.
- **Drill record.** `passed`, `latencyMedianMs`, `dumpRestoreSec`, and `copySec` match the window script's reads (`:199-220`). The script refuses a value above 20 ms, a time sum above 1800 s, and a sum above 600 s unless `-AcceptDrillTime` is passed.
- **Cited figures.** These match their sources: the latency medians 1.87/1.93/1.60 ms and 65 s/h, the 104G free and the 4g limit, the 93.5 s restore anchor, the 17.3 s fingerprint with its 1096552 kB spill, the CP-1 receipt (175.553 s, 10 passed), and the CARD-1979 min/max gap.

No OS-selected production path changes. Producer/recipient delivery and input-wait release do not apply. The plan adds no session that waits for input, so parking does not apply.

## Advisories (file as cards; do not hold the land)

- **A-1. M-5 can fail falsely on a NULL `Timestamp`.**
  - `TranscriptEntries."Timestamp"` is nullable (`DateTime?`, `AppDbContextModelSnapshot.cs:10272`). The M-5 subquery picks `LIMIT 1` with no NULL filter.
  - If that row's `Timestamp` is NULL, `NULL + interval` leaves it NULL and `row_to_json` does not change. The second probe then exits 0, and the pass table counts that as a stop.
  - This fails safe: it is a false drill failure, never a false pass. Fix: add `WHERE "Timestamp" IS NOT NULL` to the subquery.
- **A-2. Some shell steps are left implicit.**
  - M-1 never assigns `$src` from `Get-ChildItem`, but M-1 then uses `$src.FullName`.
  - The M-4 `docker run` uses `-e POSTGRES_PASSWORD` and `"$ANTIPHON_POSTGRES_IMAGE"` in the host shell. The plan does not say to export them from the env file first (for example `set -a; . "$ENV_FILE"; set +a`).
  - `$NAME`, `$Src`, and `$Tgt` cross from bash to pwsh without a definition.
  - The probe step assumes pwsh is on the server2 host. That was not observed from this runner.
  - An operator can resolve each of these on the spot. None changes a gate.
