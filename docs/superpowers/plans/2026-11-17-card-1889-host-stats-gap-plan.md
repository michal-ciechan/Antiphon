# CARD-1889 Host-stats gap

Plan only. No code, setting, restart, or host change is in this document's dispatch.

Master read: `300c8a99d0d267a39791410b7540189ab702f251` (`git fetch origin master` on 2026-11-17; `git rev-parse origin/master`). CARD-1893 collector `9cf3e491705247bc03814e6afed77a6534805dfd` is an ancestor of that master. This branch starts at the same SHA.

Lane: Linux. `GET /api/runner-defaults` returned `globalRunnerId=server2`, `revision=2`, empty `kindDefaults`. `GET /api/session-runners` returned `desktop` (windows, available, dispatch-eligible), `server2` (linux, available, dispatch-eligible), and `server2-temp` (not available, not eligible). Checkpoints do not pass `-Runner` or `-Platform`.

## Problem measured

The card is the measurement gap from Investigate task 1540fb0b, written up in `docs/investigations/2026-11-13-card-1886-antiphon-cpu-attribution.md`. Three gaps are still open. The short spool and missed ticks are not this card.

| Gap | What happened | How often | Evidence | This plan |
|---|---|---|---|---|
| Desktop history has no binding | `GET /api/hosts/desktop/load/history` returns no items and `gaps: ["binding-unknown"]`. Load coverage is `validCount 0 / expectedCount 720`. | Still true on 2026-11-17. A 24 h window `2026-11-16T06:39:11Z`–`2026-11-17T06:39:11Z` at `resolution=hour` returned HTTP 200, `itemCount=0`, `complete=false`, `gaps=binding-unknown`. `GET /api/hosts/desktop/load` returned `physicalHostKey` null, `current` null, `coverage=0`, `authority.state=unknown`, last-hour CPU `validCount=0`, `expectedCount=720`, `complete=false`. | Card text; investigation § Host-load history; this read of the live API. | Operator user-secrets only. This plan states the values and does not write them. |
| `/host-stats` does not name server, Postgres, or the build/test tree | On 2026-11-13 at 99.4% host CPU the runner's `/host-stats` attributed 0.55 cores to the processes it lists (runner, sessions, pty hosts). `Antiphon.Server` was about 0.95 cores and `antiphon-postgres` was 1.3–3.4 cores in `docker stats`. | One measured minute, plus the code path below, which still drops those roles. Today's live `/host-stats` body is UNOBSERVED: the server load projection's `current` is null, so this dispatch did not read process names from a live sample. | Investigation § Per-process CPU and the 13:39:54Z `/host-stats` sentence. Code: `HostStatsSamplerService.ReadProcesses` emits only `runner`, `session`, and `pty-host`. | Code. Live rows only. |
| `LIVE_SLOW_CALL phase=db-query` has no statement identity | 4,243 warnings from 07:06 to 13:44Z on 2026-11-13 (p50 1,257 ms, p95 2,252 ms, max 5,627 ms, 5,946 s total). | That window. Not remeasured. The log line in `LiveTiming.Finish` still has no tag. | Investigation § Server log. `LiveTiming.cs` template and `DbCommandMetrics` call `RecordExternal(ExternalOperation.Database, duration, succeeded)` with no tag. | Code. Allowlisted tag, or the word `untagged`. |
| Spool held about 31 samples and missed 5 s ticks | `%LOCALAPPDATA%\Antiphon\host-load` on 2026-11-13. The 12:48–13:27Z hole cannot be rebuilt from stored data. | One desktop day. Current desktop spool contents are UNOBSERVED from this Linux mirror. | Investigation. Collector hardening is CARD-1893, on master as `9cf3e4917`. | Out of scope. Do not change the collector. |

`GET /api/hosts/server2/load` on the same read also had `physicalHostKey` null, `current` null, and last-hour CPU `validCount=0 / expectedCount=720`. That is the CARD-1893 server2 binding, which is not applied yet. This plan does not write it and does not roll server2.

No `pg_stat_statements` query was run. The 2026-11-13 statement ranking stays the investigation's. No secret and no transcript text was read.

## Ground truth

| Id | What the card assumes | What the code does | What follows |
|---|---|---|---|
| G-1 | Desktop history is empty because the binding is missing | `HostLoadProjection` sets `unknownReasons["physicalHostKey"]` to `binding-unknown` when `PhysicalHostKey` is null (`HostLoadProjection.cs`). `HostLoadHistoryService.Binding` returns null unless exactly one configured row covers `now`, and the route then returns `gaps: ["binding-unknown"]` before it reads samples (`HostLoadHistoryService.cs`). Runner `appsettings.json` sets no `PhysicalHostKey`. `HostLoadCounterCache.Canonical` maps `local` and `desktop` to `desktop`. | Configuration, not a code defect. CARD-1893 D-1 stores the server2 row in desktop user-secrets `antiphon-server` and uses `PhysicalHostKey=server2`. A desktop row must be a different index and a different key. |
| G-2 | The process list is only runner, session, and pty-host | `ReadProcesses` emits those three names. `docs/ops-http.md` says the same. CARD-1884 added `topConsumers` (at most eight, ranked by CPU) with roles `compiler`, `build`, `test`, `agent-cli`, `antiphon-runner`, `pty-host`, `docker`, `other`. `HostConsumerClassifier.Match` does not name `Antiphon.Server`. Postgres inside the Docker VM is not a host pid. | 1884 can show a hot compiler. It does not reserve a server row, a Postgres container row, or a sum of every build/test process. Do not change the 1884 ranking, cap, classifier, or empty-`topConsumers` hash omission. |
| G-3 | The server poll already adds the server | `HostStatsPollService.AddServerProcess` runs only for host id `desktop` and appends `antiphon.server` with CPU null and the server working set. A null CPU does not attribute the 0.95 cores. | Keep that fallback for an old runner. Do not append it beside a measured `server` row, and do not replace a measured CPU with null. |
| G-4 | A new process name can be stored in history | `HostLoadProjection` renames any name other than `runner`, `session`, or `pty-host` to `session`. `HostLoadIngestService.Validate` rejects any other name. Renaming would store server CPU as a session and still pass ingest. | Drop role rows before history. Do not widen the ingest allowlist. |
| G-5 | `LIVE_SLOW_CALL` can name the query | The warning template has run, phase, operation, elapsed, outcome. `DbCommandMetrics.LeadingTag` already extracts an allowlisted `-- tag` (`session-state.` or `rt.`, length 1–64, `[a-z0-9.-]`). The executed callbacks do not pass it. `SessionRuntimeActionClaimQuery.Sql` starts with `WITH`, so the largest measured statement has no tag. | Pass `LeadingTag` through. Prefix the claim SQL with `-- rt.runtime-action.claim`. Non-allowlisted text stays `untagged`. |
| G-6 | Host-stats reads retry | `docs/resilience.md`: the host-stats poll and series read make one attempt. | Do not add a retry. |

## Decisions

Defaults for the following review. A no answer changes the slice; the code does not start from a different design inside the same task.

- **D-1. Desktop binding stays an operator write.** Server user-secrets id `antiphon-server`, next free `HostLoadHistory:Bindings:N` (UNOBSERVED whether index 0 is already the CARD-1893 `server2` row; do not overwrite a `server2` row). `RunnerId=local` (canonical `desktop`), `PhysicalHostKey=desktop`, `Revision=1`, `Primary=true`, `From` = the UTC write time, no `To`, `SourceEpoch` = one new GUID. The desktop runner gets the same `PhysicalHostKey`, `BindingRevision`, and `SourceEpoch` under `SessionRunner:HostStats`. `RunnerId` already defaults to `local`. Rejected: tracked `appsettings.json` (CARD-1893 D-1; it would bind every test host), and this dispatch writing the secrets.
- **D-2. Live role rows, not history rows.** The runner appends `server`, `postgres`, `build`, and `test` to the live `processes` list when it can measure them. `HostLoadProjection` drops every other name. It no longer renames an unknown name to `session`. Ingest validation is unchanged, so a sample that still contains only `runner` / `session` / `pty-host` keeps today's canonical hash. Rejected: widening `HostLoadIngestService.Validate` (a new runner would fail ingest on an old server, and a renamed `session` row would hide the server).
- **D-3. Sums come from the scan that CARD-1884 already runs, before the top-eight cut.** `HostTopConsumerTracker.Prepare` computes the sums from processes that already have a CPU delta, then truncates the published ranking exactly as it does now. Sort, cap, classifier stems, and the empty-array hash omission stay. `Antiphon.Server` remains role `other` if it also ranks in the eight. The dedicated row is named `server` and matches that basename, case-insensitive. `build` sums role `build`. `test` sums role `test`. Compiler rows stay in `topConsumers` only. No members means the row is absent. Members whose cores sum to 0 publish 0. First scan has no delta, so the row is absent rather than 0. Rejected: a second process-table walk, and rejected: summing only the published eight (a quiet server falls off when compilers fill the list).
- **D-4. Postgres is `docker stats` text for container `antiphon-postgres`, off the scan.** A pure parser accepts one percentage such as `133.00%` and stores 133 in `CpuPercent` (the process list's unit: 100 = one core). `0.00%` is a real idle reading and is published. Empty text, a missing `%`, a non-finite value, or a throw omits the row. The read has its own timeout (1 s) and its own cache. It is not inside the 10 ms scan budget. There is no new setting and no cgroup reader. Rejected: cgroup (desktop Postgres is inside the Docker VM, which is where the 2026-11-13 measurement used `docker stats`), and rejected: probing `antiphon-server2-postgres` (that name is CARD-1893's host, and this card's measurement is the desktop container).
- **D-5. Slow-call identity is the allowlisted tag or the word `untagged`.** `RecordExternal` takes an optional tag. The warning line gains `tag={Tag}`. `DbCommandMetrics` passes `LeadingTag`. Anything else, including `Secret Tag`, SQL, or a failed read, logs `tag=untagged` and still logs the warning when elapsed is over 1,000 ms. The claim statement gains the prefix `-- rt.runtime-action.claim` so the existing `rt.` allowlist accepts it. No other query is retagged. Rejected: a stack walk (`MoveNext`, and a frame can carry a path), and rejected: logging every leading comment (the counter's allowlist exists so a tag cannot become a metric key).
- **D-6. One code slice.** Role rows and the slow-call tag share no migration and no phone-home ordinal. Review of this plan is the next stage. Mutation stays waived (operator 2026-11-12); the positive controls below are designed and not a stage.
- **D-7. Activation is a desktop restart after land, not a server2 roll and not a migration.** The AppHost restart picks up `LIVE_SLOW_CALL` and the poll change. The desktop session-runner restart picks up the role rows. Neither restart is this plan task. History stays `binding-unknown` until the operator applies D-1 and restarts. There is no backfill. Twenty-four complete hourly items require a day of ingest after that write. An empty window then reports `missing`, not `binding-unknown` (`HostLoadHistoryService`).

## Design

Smallest change that makes the three verify lines possible:

1. `GET /host-stats` on the desktop runner lists `server` and `postgres` when those probes can read, and lists `build` and `test` when the scan saw those roles. CPU null is not used for a role row. Absence means "cannot say". A fault does not publish 0.
2. Durable history processes stay `runner`, `session`, and `pty-host`. Role CPU cannot be stored as a session.
3. A slow database warning names `rt.runtime-action.claim` for the claim statement, names any other allowlisted `TagWith`, and otherwise says `untagged`. The line never contains SQL.

`CpuPercent` on a role row uses the same unit as the existing process rows (`delta / elapsed * 100`). Docker's `133%` is 133, not 1.33.

The server poll still adds `antiphon.server` with a null CPU only when the runner sample has no `server` row. A sample that already has `server` is left alone. One attempt, as `docs/resilience.md` requires.

This does not touch transcripts, session delivery, or pty input. `docs/session-runtime-invariants.md` is unchanged. `docs/project-context.md` naming applies to the new type (`HostStatsRoleProbe`, private `_` fields). No new HTTP route. The existing `processes[].name` string gains four live values. `docs/ops-http.md` currently says the process list remains only `runner`, `session`, and `pty-host`; that sentence is updated in the same slice, and the history sentence stays.

Fail-closed:

- A probe fault, timeout, or parse failure omits the row. It does not publish 0 and does not reuse a value older than two cadences as if it were current.
- A role name never enters `HostLoadSample.Processes`.
- A tag that fails the allowlist cannot change the warning into silence and cannot copy command text into the log.
- `AddServerProcess` must not overwrite a measured CPU with null.

## What must not change

- CARD-1884: `HostConsumerRules` roles and `MaxCount`, classifier stems (`VBCSCompiler`, `csc`, `vbc`, and the other `Match` arms), top-eight sort, 512-process cap, 10 ms scan budget, and the canonical omission of an absent or empty `topConsumers` array.
- CARD-1893: collector, `HistoryPath`, spool bounds, ingest cursor, compose, `scripts/deploy-server2.ps1`, and the server2 binding values. Do not apply the desktop binding either.
- CARD-1960 and CARD-1964: no edit to phone-home operation ordinals, `PhoneHomeContracts.cs`, or `PhoneHomeCommandDispatcher.cs`. Role rows reuse `RunnerHostProcessDto`.
- CARD-1888: no edit to `AgentSessionRuntime.cs`. The claim SQL lives in `SessionRuntimeActionClaimQuery.cs`.
- CARD-1820: no edit to `scripts/impacted-classes.ps1`, `scripts/lib/release-impact*`, `tests/impacted-classes.json`, or `tests/derived-impact-index.json`.
- Ingest name allowlist in `HostLoadIngestService.Validate`.
- Host-stats retry policy.
- Tracked `src/Antiphon.SessionRunner/appsettings.json` (no `PhysicalHostKey` there).

## Files

Change:

- `src/Antiphon.SessionRunner/HostStatsRoleProbe.cs` (new). Pure sums and the docker percentage parser. Container name constant `antiphon-postgres`.
- `src/Antiphon.SessionRunner/HostTopConsumerTracker.cs`. Publish the pre-truncation sums beside the existing ranking. Do not change the ranking.
- `src/Antiphon.SessionRunner/HostStatsSamplerService.cs`. Append role rows onto the live process list. Postgres read is off the tick.
- `src/Antiphon.SessionRunner/HostLoadProjection.cs`. Drop names other than `runner`, `session`, and `pty-host`.
- `server/Application/Services/HostStatsPollService.cs`. Skip the null-CPU append when `server` is already present.
- `src/Antiphon.Observability/LiveTiming.cs`. Optional tag on the slow line.
- `server/Infrastructure/Data/DbCommandMetrics.cs`. Pass `LeadingTag`.
- `server/Infrastructure/Data/SessionRuntimeActionClaimQuery.cs`. One leading `-- rt.runtime-action.claim` line.
- `docs/ops-http.md`. One sentence: live role rows, history still the three names.
- Tests listed under Verification design.

Existing tests that must stay green, because a role row is omitted when the probe has nothing to say: `C1514HostLoadSampleTests.ProcessCpuDoesNotBecomeHostCpu` (`Processes.Single()`), and `HostTopConsumerTests` (ranking and classifier). They are in CP-1 so a projection change that relabels them fails the row.

Impacted-classes row `host-stats` matches `src/Antiphon.SessionRunner/HostStats*.cs` and names `RunnerCapabilitiesTests`. `HostStatsSamplerService.cs` and the new `HostStatsRoleProbe.cs` match that glob. `HostLoadProjection.cs`, `HostTopConsumerTracker.cs`, `LiveTiming.cs`, `HostStatsPollService.cs`, and `SessionRuntimeActionClaimQuery.cs` do not. Code runs `pwsh -NoProfile -File scripts/impacted-classes.ps1 -Range origin/master..HEAD -Json` and does not edit the selector. No migration, no public route, no operation-label change, so there is no migration downgrade witness.

## Collisions

| Work | Why it stays clear |
|---|---|
| CARD-1893 | Activation plan owns server2 `HistoryPath`, compose, and the `server2` binding. This slice does not edit those files and does not write secrets. |
| CARD-1884 | Top consumers stay a separate field. This slice adds live process rows and does not retune the ranking. |
| CARD-1960, CARD-1964 | Phone-home files are not in the file list. |
| CARD-1888 | `AgentSessionRuntime.cs` is not in the file list. |
| CARD-1820 | Selector and index files are not in the file list. |

## Rollout

No migration. A migration is a human-only gate; this card does not have one.

No server2 image roll. The desktop runner is the host that could see `Antiphon.Server` and `antiphon-postgres` in the 2026-11-13 measurement. On server2 the constant container name does not match, so the Postgres row is absent, which is the fault path and not a zero.

After a reviewed land, the operator applies D-1, then the desktop AppHost and the desktop session runner restart. This plan task does not restart either. Code land alone does not make history complete.

## Review and merge

1. Plan review reads this file. It does not start Code.
2. Code commits the red tests, then the fix, and runs the checkpoint table as one serial run with no `--row-timeout`.
3. Code review re-runs CP-1, CP-2, and CP-3 and does not edit the branch. Positive controls stay pending. Mutation is waived.
4. Land requires `git fetch origin master` and `git merge-tree --write-tree origin/master HEAD` with no conflicts. Do not merge master into the task branch to get there. A conflict only in `tests/derived-impact-index.json` is regenerated on the land rebase, not hand-merged, and this slice should not touch that file.
5. Merge runs `scripts/impacted-classes.ps1` on the batch range. The fixed high-radius subset still runs. No new selector row.

## Operator decisions

- **Q-1.** Write the D-1 desktop binding in user-secrets after land, and leave this task from writing it? **Yes.**
- **Q-2.** Probe only container `antiphon-postgres`, and leave server2 Postgres unprobed? **Yes.**
- **Q-3.** Add live `server`, `postgres`, `build`, and `test` rows, and leave compilers on the CARD-1884 top-consumer list? **Yes.**
- **Q-4.** Drop role rows out of durable history instead of widening ingest? **Yes.**
- **Q-5.** Log the allowlisted tag or `untagged`, and tag the claim statement `rt.runtime-action.claim`? **Yes.**
- **Q-6.** Activate with a desktop AppHost restart and a desktop runner restart, and do not roll server2? **Yes.**

## Performance budget

No test ran in this plan dispatch. Durations below are provisional. The checkpoint tool's row deadline is `max(15, 3 × EstimatedMinutes)`. That is wider than the 2× p95 rule. It is the provisional deadline until Code records a p95 and files a Debug task if a row, including slot wait, passes 30 minutes. `ANTIPHON_LINUX_COMPILER_POLICY=0` on every `dotnet` command. Read `/proc/loadavg` and start under 16.

| Row | Selection | Setup | Provisional body | Timeout |
|---|---|---|---|---|
| CP-1 | `HostStatsRoleProbeTests` (3), `C1514HostLoadSampleTests` (5), `HostTopConsumerTests` (24 methods, one Windows-only skip on Linux) | No container. One cold `Antiphon.SessionRunner.Tests` build in `bin-c1889-role/`. Slot wait is outside the test body. | Unit tests, seconds after the build. Build is the cost. | Estimate 15 min. Derived row deadline 45 min. |
| CP-2 | `LiveTimingTests` (4 existing + 1 new) | No container. Cold `Antiphon.Observability.Tests` build in `bin-c1889-timing/`. | Seconds after the build. | Estimate 8 min. Derived row deadline 24 min. |
| CP-3 | `HostStatsPollServiceTests` (10 + 1) and `DbCommandMetricsTests` (2 + 1) | `TestDbFixture` once per `Antiphon.Tests` assembly (shared Postgres; schema cache UNOBSERVED on this host). Each poll test starts an in-process phone-home host. No desktop `antiphon-postgres`. Cold build in `bin-c1889-server/`. | Build dominates. Fixture start provisional 60 s. Poll tests are the existing class plus one tick. | Estimate 15 min. Derived row deadline 45 min. |

Derived total is `2 × (15 + 8 + 15) + 10 = 86` minutes. The run is one serial checkpoint invocation and does not pass `--row-timeout`. Per-test phase timing stays the existing opt-in. No new external-call span. The Postgres parser is pure and is not a docker call inside the test.

## Verification design

Red at `300c8a99d0d267a39791410b7540189ab702f251` means the assertion is false in today's code. A witness that already passes is not one of these.

- **V-1** `HostStatsRoleProbeTests.Server_row_uses_the_server_basename_and_history_drops_it`. Observations include pid 50, name `Antiphon.Server`, role `other`, and a delta of 0.95 cores. The live list has one `server` row at `CpuPercent` 95. `HostLoadProjection` does not contain `server` and does not contain an extra `session` made from it. Red today: `ReadProcesses` never emits `server`, and projection renames any other name to `session`.
- **V-2** `HostStatsRoleProbeTests.Postgres_percent_is_published_and_a_fault_omits_the_row`. Text `133.00%` publishes `postgres` at 133. A throw publishes no `postgres` row and no row whose CPU is 0. Red today: there is no parser, so the 133 assertion fails.
- **V-3** `HostStatsRoleProbeTests.Build_and_test_sums_include_processes_outside_the_top_eight`. Nine `build` processes at 0.2 cores each publish one `build` row at 180, even though the top eight cannot hold all nine. A `compiler` observation is not in that sum. `Antiphon.Server` is still role `other` on the top-consumer row. Red today: the tracker publishes only eight ranked rows and no `build` sum.
- **V-4** `LiveTimingTests.Slow_database_line_names_the_allowlisted_tag`. `RecordExternal(Database, 1500 ms, succeeded, "session-state.seed")` logs `tag=session-state.seed`. The same call with `Secret Tag` or text containing `SELECT` logs `tag=untagged` and does not log `SELECT` or `Secret`. A 999 ms call logs nothing. Red today: the template has no tag. Code adds the optional parameter in the failing-test commit and leaves the template unchanged, so the test compiles and the assertion fails.
- **V-5** `DbCommandMetricsTests.Claim_query_text_starts_with_the_allowlisted_runtime_tag`. `SessionRuntimeActionClaimQuery.Query(...).ToQueryString()` starts with `-- rt.runtime-action.claim`, and `DbCommandMetrics.LeadingTag` returns that string. Red today: the SQL starts with `WITH`.
- **V-6** `HostStatsPollServiceTests.Desktop_keeps_a_measured_server_row`. The desktop sample already has `server` at CPU 95. After one tick the projected desktop processes still have that 95 and do not also contain `antiphon.server`. Red today: `AddServerProcess` always appends `antiphon.server` with a null CPU.

`C1514HostLoadSampleTests` (5) and `HostTopConsumerTests` (23 executed on Linux) are in CP-1 so the ranking and the `Processes.Single()` session row stay green. They are not the red witnesses.

Positive controls, pending, mutation waived:

| PC | Mutant | Witness | Expected red |
|---|---|---|---|
| PC-1 | Publish CPU 0 when the docker reader throws | `/*/*/HostStatsRoleProbeTests/Postgres_percent_is_published_and_a_fault_omits_the_row` | A `postgres` row appears with CPU 0. |
| PC-2 | Rename a `server` process to `session` in the projection | `/*/*/HostStatsRoleProbeTests/Server_row_uses_the_server_basename_and_history_drops_it` | History contains a `session` that was not a session. |
| PC-3 | Copy command text into the slow line when the tag is rejected | `/*/*/LiveTimingTests/Slow_database_line_names_the_allowlisted_tag` | The warning contains `SELECT`. |
| PC-4 | Keep appending null-CPU `antiphon.server` beside `server` | `/*/*/HostStatsPollServiceTests/Desktop_keeps_a_measured_server_row` | Both rows are present. |

Code runs, after the red tests exist and the fix is in, one serial run, with `ANTIPHON_LINUX_COMPILER_POLICY=0` and load average under 16, and without `--row-timeout`:

`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-11-17-card-1889-host-stats-gap-plan.md --serial`

Build through the checkpoint tool so it takes the host slot. Do not pass `-o`, `--output`, or `OutputPath` on a direct build. The output directories below belong to the checkpoint rows.

### Checkpoints

Lane: Linux. Serial. No `-Runner`, no `-Platform`, no `--row-timeout`. `totalTimeoutMinutes` is the derived floor. Provisional estimates. CP-1 carries the cold-build 15 minutes so its derived deadline is 45 minutes. One Windows-only method in `HostTopConsumerTests` skips on Linux and is not in the Min floor.

totalTimeoutMinutes: 86

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1889-role/ | role | `/*/*/(HostStatsRoleProbeTests*)\|(C1514HostLoadSampleTests*)\|(HostTopConsumerTests*)/*` | V-1, V-2, V-3 | >= 31 executed, 0 failed | 31 | 15 |
| CP-2 | S1 | tests/Antiphon.Observability.Tests -> bin-c1889-timing/ | timing | `/*/*/LiveTimingTests/*` | V-4 | >= 5 executed, 0 failed | 5 | 8 |
| CP-3 | S1 | tests/Antiphon.Tests -> bin-c1889-server/ | slow | `/*/*/(HostStatsPollServiceTests*)\|(DbCommandMetricsTests*)/*` | V-5, V-6 | >= 14 executed, 0 failed | 14 | 15 |
