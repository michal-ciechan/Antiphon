# CARD-1889 host-stats gap plan review, round 1

BLOCKERS (4). The gap and most of the plan's ground truth match the code. Two problems hold the land. First, the desktop activation leaves the runner-side binding without a config source, so history stays empty after D-1. Second, the checkpoint table cannot produce a clean receipt: CP-1 contains a skip, V-5 cannot go green, and the touched claim-query pin class has no row. The 1 s `docker stats` timeout has no measurement behind it.

Reviewed source `95e6a1b5ce609affc4b4d6dcc5f577c5dc72b5db` on `feat/card-task-ef70b56b`. Range `300c8a99d0d267a39791410b7540189ab702f251..95e6a1b5ce609affc4b4d6dcc5f577c5dc72b5db` is one commit and one file: the plan. Plan owner `ef70b56b` (Grok). Reviewer is Claude Opus. Master is `300c8a99d0d267a39791410b7540189ab702f251`. Mutation is waived (operator 2026-11-12). Positive controls stay pending.

## Verified claims

| Claim | Verdict | Evidence |
|---|---|---|
| Desktop history is unbound | TRUE | Read on 2026-11-17 at about 07:48Z, last 24 h, `resolution=hour`. `GET /api/hosts/desktop/load/history` returned 0 items, `complete=false`, `gaps=["binding-unknown"]`. `GET /api/hosts/desktop/load` returned `physicalHostKey` null, `current` null, and last-hour CPU `validCount=0`, `expectedCount=720`, `coverage=0`, `sample_stale`. server2 returned the same shape. |
| G-1: the cause is the missing binding | TRUE | `HostLoadHistoryService.Binding` (`:175-179`) needs exactly one row covering `now`, and `:72` returns `binding-unknown` before reading samples. Ingest `HostLoadIngestService.Binding` (`:76-83`) needs RunnerId, `PhysicalHostKey`, `Revision`, and `SourceEpoch` to equal the **sample's** values, so the runner must also carry them. `HostLoadProjection.cs:35` records `binding-unknown` when the runner key is null. |
| G-2: process list is runner/session/pty-host | TRUE | `HostStatsSamplerService.ReadProcesses` (`:270-300`). |
| G-3: `AddServerProcess` | TRUE | `HostStatsPollService.cs:124,136-142`. Desktop only. Always appends `antiphon.server` with CPU null. |
| G-4: history renames, ingest rejects | TRUE | `HostLoadProjection.cs:38` maps any other name to `session`. `HostLoadIngestService.cs:98` rejects any other name. Dropping role rows before history keeps today's samples byte-identical, because no other name reaches the projection today. |
| G-5: slow line has no tag | TRUE | Template at `LiveTiming.cs:108`. Every `DbCommandMetrics` executed/failed callback calls `RecordExternal(Database, duration, ok)` with no tag. `LeadingTag` allowlist is `session-state.` or `rt.`, `[a-z0-9.-]`, length 1-64. Claim SQL starts with `WITH`. Production runs it uncomposed (`SessionRuntimeActionCoordinator.cs:165`, `.ToListAsync()`), so a leading `-- rt.runtime-action.claim` line stays at the start of `CommandText`. |
| Tag carries no payload | TRUE | The tag is `untagged` or an allowlisted leading comment. Those come from code-constant `TagWith` strings or `TagSegment` names. Claim tag is a constant. `LIVE_SLOW_CALL` has no other parser in code or scripts. |
| No setting, migration, eligibility, or dispatch change | TRUE | Role rows reach only the in-memory `HostStatsStore` ring, `/host-stats`, and the poll cache/SignalR projection. No admission or eligibility code reads `processes`; host eligibility comes from the runner descriptor (`HostStatsPollService.cs:153-163`). Container name and timeout are constants. |
| Counts | Mostly TRUE | LiveTimingTests 4, HostStatsPollServiceTests 10, DbCommandMetricsTests 2, HostTopConsumerTests 24. C1514HostLoadSampleTests has **6** methods, not 5 (A-1). |

### Collisions

The plan's file list was diffed against every CARD-1884 and CARD-1893 task branch.

- CARD-1893 branches touch none of the files: `f99cc46c`, `74b623f9`, `4d0255a8`, `aefcc5e5`, `e7394f29`, `eb85f57b`, `a1d414b6`.
- CARD-1884 branches `65c1da44` and `05ef2301` touch `HostLoadProjection.cs`, `HostStatsSamplerService.cs`, `HostTopConsumerTracker.cs`, `HostTopConsumerTests.cs`, and `docs/ops-http.md`. That code is already on master: `git diff origin/master origin/feat/card-task-65c1da44` over those four source and doc files is empty. The only unpicked CARD-1884 commits are the impacted-index regeneration and the Linux proc rows.
- The CARD-1893 collector `9cf3e4917` is an ancestor of master.
- The plan does not list `AgentSessionRuntime.cs`, any phone-home file, or any selector or index file.

There is no shared in-flight file. Once Code edits `HostTopConsumerTracker.cs`, a CARD-1884 follow-up becomes a collision.

## Blockers

### B-1. The runner half of the desktop binding has no source, so activation cannot fill history

- **Where:** D-1, D-7, Rollout, and Q-1.
- **Failure:** The operator writes the server row to `antiphon-server` user-secrets and restarts. Every desktop sample still carries `PhysicalHostKey=null`. `HostLoadIngestService.Binding` matches nothing, so no sample is stored. The history stays empty, and the card's first verify line never passes.
- **Why:** D-1 says the runner "gets the same `PhysicalHostKey`, `BindingRevision`, and `SourceEpoch` under `SessionRunner:HostStats`", but it does not name the configuration source. `Antiphon.SessionRunner` has no `UserSecretsId`; only `server/Antiphon.Server.csproj` has one. D-1 rejects the tracked `src/Antiphon.SessionRunner/appsettings.json`, and the CARD-1893 plan solves the same problem for server2 with compose environment (D-5). The desktop runner is the AppHost daemon (`Antiphon.AppHost/Program.cs:32-38`), with content root `src/Antiphon.SessionRunner` in the main checkout and `--urls` as its only argument. The remaining sources are:
  - user or machine environment `SessionRunner__HostStats__PhysicalHostKey`, `__BindingRevision`, and `__SourceEpoch`, which the supervisor inherits only when it starts, so a soft restart may not pick them up;
  - a gitignored `src/Antiphon.SessionRunner/appsettings.<Environment>.json`, which depends on the daemon's environment name.

  Neither source is named or checked. The plan also has no read-back step showing the runner publishes the key.
- **Fix:**
  1. Name the runner source and the exact keys and values: `PhysicalHostKey=desktop`, `BindingRevision=1`, and the same `SourceEpoch` GUID as the server row.
  2. Name the restart that loads them, either a supervisor `-Hard` refresh or an AppHost restart, and check which one actually re-reads that source.
  3. Add a stop check after the restart. The runner's history page must show `physicalHostKey=desktop`, then `GET /api/hosts/desktop/load` must show `physicalHostKey=desktop` with `current` non-null, before the 24 h wait starts.
  4. Keep the write an operator step, as it is now.

### B-2. CP-1 cannot validate: the Windows-only HostTopConsumerTests method skips on Linux

- **Where:** `### Checkpoints` CP-1 filter `(HostTopConsumerTests*)`, which the plan itself notes: "One Windows-only method ... skips on Linux".
- **Failure:** `HostTopConsumerTests.Production_factory_observes_this_process_on_windows` (`:52-57`) calls `Skip.Test` when the host is not Windows. The row ends with skipped=1. `ReportValidator.cs:52-54` returns `row_failed` for any test row with `Skipped != 0`. `validate` then refuses the whole run, and Code and Review get no clean receipt (`reviewedSourceClean` cannot be true).
- **Why:** A Min floor that excludes the method does not remove it from the selection.
- **Fix:** Select HostTopConsumerTests by method so the Windows witness is out. Either put the 23 methods in a method-level OR filter in CP-1, or give HostTopConsumerTests its own row reusing `CP-1`'s build. For example: `/*/*/HostTopConsumerTests/(Production_factory_selects*)|(Factory_*)|(Shared_*)|(Pid_*)|(Vanished_*)|(Top_*)|(Command_*)|(Scan_*)|(Short_*)|(Missing_*)|(Process_*)|(Unsafe_*)|(Roles_*)|(Old_*)|(Projection_*)|(Eight_*)|(Live_*)|(Windows_*)|(Cached_*)|(Source_*)`, with Min 23. Re-import it and confirm 0 import warnings.

### B-3. V-5 stays red after the fix, and the claim-query pin class has no row

- **Where:** V-5 and CP-3.
- **Failure, part 1:** V-5 asserts that `SessionRuntimeActionClaimQuery.Query(...).ToQueryString()` starts with `-- rt.runtime-action.claim`. The query binds four `NpgsqlParameter`s. Npgsql 9.0.4 has no query-string factory, so EF Core's `RelationalQueryStringFactory` applies (its `(Size = `, `(DbType = ` facet strings are present in `Microsoft.EntityFrameworkCore.Relational` 9.0.4). That factory writes one `-- <param>=<value>` comment line per parameter **before** the command text. The string therefore starts with a parameter line, `LeadingTag` of that line is null, and V-5 fails at the fixed SHA as well.
- **Failure, part 2:** The fix edits the SQL text of `SessionRuntimeActionClaimQuery.cs`, but its pin class `RuntimeActionClaimQueryTests` is in no row. That class covers parameter binding, legacy-oracle equivalence, and the `PREPARE c1886 ... AS <CommandText>` plan bounds that consume that exact text (`:218-240`).
- **Fix:**
  1. Have V-5 assert on `.CreateDbCommand().CommandText`, as `RuntimeActionClaimQueryTests` already does at `:27-28`. It should check `StartsWith("-- rt.runtime-action.claim\n")` and `DbCommandMetrics.LeadingTag(text) == "rt.runtime-action.claim"`.
  2. Add `(RuntimeActionClaimQueryTests*)` (3 methods, same `TestDbFixture` build) to CP-3 and raise Min to 17.

### B-4. The `docker stats` timeout of 1 s has no measurement

- **Where:** D-4, which sets a 1 s timeout for the Postgres read.
- **Failure:** `docker stats --no-stream` usually takes longer than one daemon stats interval, because CPU% needs a second sample. The plan cites no measurement on the desktop (Docker Desktop on WSL2). If the read usually takes more than 1 s, the `postgres` row is permanently absent, which the design deliberately treats as "cannot say". The card's second verify line ("`/host-stats` lists `server` and `postgres` rows") then fails silently. On server2 this review could not time it, because no container is running here. UNOBSERVED either way.
- **Why:** The test-performance standard sets timeouts near 2× the measured p95. This is a production probe timeout with no measured basis, on the one row the card names.
- **Fix:**
  1. Record a desktop measurement of `docker stats --no-stream --format '{{.CPUPerc}}' antiphon-postgres`: at least five samples, with p95. This is an orchestrator read with no change.
  2. Set the timeout to about 2× that p95, and keep the read off the tick.
  3. Make sure the "older than two cadences" staleness bound (10 s at the 5 s cadence) can still publish a successful read.
  4. If the measurement cannot be taken before Code, state the timeout as provisional, and have Code record the first live p95 in its report.

## Operator decisions Q-1 to Q-6

- **Q-1 cannot be a default yes.** The plan task never writes the binding, and that part is correct. But writing the binding is a real operator action on the desktop, at two sources once B-1 names the runner source. The action stays **pending operator** after land. A default does not discharge it. The code slice can land without it.
- **Q-6 can default yes.** The desktop AppHost restart and the desktop runner restart are covered by orchestrator operational autonomy, including a planned `-Hard` supervisor refresh. Not rolling server2 is right.
- **Q-2 to Q-5 can default yes.** They are design choices the code supports: probing only `antiphon-postgres`, using live rows only, dropping role rows before history instead of widening ingest, and using the allowlisted tag or `untagged`.

## Is the code slice useful without the binding?

Yes, for two of the three gaps:

- The live `server`, `postgres`, `build`, and `test` rows appear on `/host-stats` after the desktop runner restart, with or without a binding.
- The `LIVE_SLOW_CALL` `tag=` appears after the AppHost restart, with or without a binding.

V-1 to V-6 are unit or fixture witnesses that need no binding. Only the 24 h history verify line depends on the operator write.

## Checkpoint import (read-only)

Import ran through the build slot (`waited=60s`, `held=14s`) with `ANTIPHON_LINUX_COMPILER_POLICY=0`, from 1-minute load 14.06:

```
dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1889-host-stats-gap-plan.md --out .antiphon/c1889-review-manifest.yml
```

Result: `imported 3 rows`, 0 import warnings. The only warnings were nullable warnings from compiling the tool itself. Ids are `CP-1`, `CP-2`, `CP-3`. Row timeouts are 45, 24, and 45 minutes. `totalTimeoutMinutes` is 86. The first row carries the cold-build 15 minutes. The manifest stays in ignored `.antiphon/`.

Other checks:

- `git merge-tree --write-tree origin/master 95e6a1b5c`: exit 0, tree `ac3487b89075900fb4d38eebcce363c60fc797ff`.
- `scripts/check-evidence-diff.ps1` over the range: `commits=1 entries=0 violations=0`.

The guard batch and touched-class reruns were not run, because the subject changes no code or tests.

## Advisories (do not hold the land; card them)

- **A-1.** C1514HostLoadSampleTests has 6 methods, not 5. Correct the counts in the CP-1 text, V section, and budget (the Min floor still holds).
- **A-2.** V-4 requires `LiveTiming` to reject `Secret Tag` and SQL text by itself. Observability cannot reference the server's `DbCommandMetrics`, so the plan should say that the allowlist (`session-state.`/`rt.`, `[a-z0-9.-]{1,64}`) is duplicated or moved into `Antiphon.Observability`. It should also say that the two copies are pinned to each other.
- **A-3.** Role sums come from a scan capped at 512 processes with a 10 ms budget. When `TopConsumersTruncated` is true, a `build`/`test` sum is a lower bound. Either publish no sum on a truncated scan, or say on the row that it is partial.
- **A-4.** The `server` row matches the basename `Antiphon.Server`. If the desktop server runs under `dotnet` (not the apphost exe), the row is absent. Confirm the process name on the desktop and the Windows table's `.exe` handling in Code.
- **A-5.** After the CARD-1406 (A2) or CARD-1744 cutover, desktop `antiphon-postgres` is no longer the production database. The row is then absent or measures the wrong thing. Note this beside Q-2.
- **A-6.** Spawning `docker` on every cadence from the desktop runner adds its own CPU. Give the cost in the budget once B-4 is measured.
