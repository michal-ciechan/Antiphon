# CARD-1889 Host-stats gap

Plan only. No code, setting, restart, or host change is in this document's dispatch.

Master read: `300c8a99d0d267a39791410b7540189ab702f251` (`git fetch origin master` on 2026-11-17; `git rev-parse origin/master`). CARD-1893 collector `9cf3e491705247bc03814e6afed77a6534805dfd` is an ancestor of that master. This branch starts at the same SHA.

Lane: Linux. `GET /api/runner-defaults` returned `globalRunnerId=server2`, `revision=2`, empty `kindDefaults`. `GET /api/session-runners` returned `desktop` (windows, available, dispatch-eligible), `server2` (linux, available, dispatch-eligible), and `server2-temp` (not available, not eligible). Checkpoints do not pass `-Runner` or `-Platform`.

## Round-2 amendment

This amends the plan for the four round-1 blockers in `docs/superpowers/reviews/2026-11-17-card-1889-plan-review.md` (`origin/feat/card-task-52aeb04f` at `ad5480101f6a7fb26d4c7eee9096ca8cfe0c7f84`). Master read again on 2026-11-17: `300c8a99d0d267a39791410b7540189ab702f251`, unchanged. Plan task `4f978baa`. Nothing was written to any host.

| Item | Change | Sections |
|---|---|---|
| B-1 | The runner half of the desktop binding now has a named source: gitignored `appsettings.Development.json` and `appsettings.Production.json` in the main checkout's `src/Antiphon.SessionRunner`, with the same keys in both. The plan gives each restart that loads a source, and a stop check that reads it back. | G-1, D-1, D-7, Rollout, Q-1 |
| B-2 | `HostTopConsumerTests` is selected by method and leaves out the Windows-only method. The class has 33 methods on master, not 24, so the row selects 32. | Checkpoints CP-1, CP-2, budget, V section |
| B-3 | V-5 asserts on `CreateDbCommand().CommandText`. `RuntimeActionClaimQueryTests` (3) is added to the server row. | V-5, CP-4 |
| B-4 | The `docker stats` timeout is no longer a 1 s guess. It is UNOBSERVED. Code step S0 measures the desktop p95, and the slice stops when 2 × p95 reaches one cadence. | D-4, Rollout, budget |
| A-1 | Applied. `C1514HostLoadSampleTests` has 6 methods. | budget, V section, CP-2 |
| A-2 | Applied. The tag allowlist moves to `Antiphon.Observability`. `DbCommandMetrics.LeadingTag` calls it, so there is one copy and nothing to pin. | D-5, Files |
| A-3 | Applied. A truncated scan publishes no `build` or `test` row. | D-3, V-3 |
| A-4 | Applied as a Code check. The desktop server process name is UNOBSERVED from Linux. | D-3 |
| A-5 | Applied. Noted beside Q-2. | Q-2 |
| A-6 | Applied. The cost is measured in S0 and stays UNOBSERVED until then. | budget |

One correction to the review's B-1 text. Ingest does not reject an unbound sample. `HostLoadIngestService.IngestAsync` stores it with `PhysicalHostKey` and `SourceEpoch` null and `IsPrimary` false (`HostLoadIngestService.cs:50-56`). Both history reads filter on `PhysicalHostKey == binding.PhysicalHostKey` (`HostLoadHistoryService.cs:29-32,81-82,95-96`), so those rows never appear. The result is the same: history stays empty, and nothing relabels the stored null rows later.

## Problem measured

The card is the measurement gap from Investigate task 1540fb0b, written up in `docs/investigations/2026-11-13-card-1886-antiphon-cpu-attribution.md`. Three gaps are still open. The short spool and missed ticks are not this card.

| Gap | What happened | How often | Evidence | This plan |
|---|---|---|---|---|
| Desktop history has no binding | `GET /api/hosts/desktop/load/history` returns no items and `gaps: ["binding-unknown"]`. Load coverage is `validCount 0 / expectedCount 720`. | Still true on 2026-11-17. A 24 h window `2026-11-16T06:39:11Z`–`2026-11-17T06:39:11Z` at `resolution=hour` returned HTTP 200, `itemCount=0`, `complete=false`, `gaps=binding-unknown`. `GET /api/hosts/desktop/load` returned `physicalHostKey` null, `current` null, `coverage=0`, `authority.state=unknown`, last-hour CPU `validCount=0`, `expectedCount=720`, `complete=false`. | Card text; investigation § Host-load history; this read of the live API. | Operator step at two sources: server user-secrets and the runner's gitignored appsettings (D-1). This plan states the values and does not write them. |
| `/host-stats` does not name server, Postgres, or the build/test tree | On 2026-11-13 at 99.4% host CPU the runner's `/host-stats` attributed 0.55 cores to the processes it lists (runner, sessions, pty hosts). `Antiphon.Server` was about 0.95 cores and `antiphon-postgres` was 1.3–3.4 cores in `docker stats`. | One measured minute, plus the code path below, which still drops those roles. Today's live `/host-stats` body is UNOBSERVED: the server load projection's `current` is null, so this dispatch did not read process names from a live sample. | Investigation § Per-process CPU and the 13:39:54Z `/host-stats` sentence. Code: `HostStatsSamplerService.ReadProcesses` emits only `runner`, `session`, and `pty-host`. | Code. Live rows only. |
| `LIVE_SLOW_CALL phase=db-query` has no statement identity | 4,243 warnings from 07:06 to 13:44Z on 2026-11-13 (p50 1,257 ms, p95 2,252 ms, max 5,627 ms, 5,946 s total). | That window. Not remeasured. The log line in `LiveTiming.Finish` still has no tag. | Investigation § Server log. `LiveTiming.cs` template and `DbCommandMetrics` call `RecordExternal(ExternalOperation.Database, duration, succeeded)` with no tag. | Code. Allowlisted tag, or the word `untagged`. |
| Spool held about 31 samples and missed 5 s ticks | `%LOCALAPPDATA%\Antiphon\host-load` on 2026-11-13. The 12:48–13:27Z hole cannot be rebuilt from stored data. | One desktop day. Current desktop spool contents are UNOBSERVED from this Linux mirror. | Investigation. Collector hardening is CARD-1893, on master as `9cf3e4917`. | Out of scope. Do not change the collector. |

`GET /api/hosts/server2/load` on the same read also had `physicalHostKey` null, `current` null, and last-hour CPU `validCount=0 / expectedCount=720`. That is the CARD-1893 server2 binding, which is not applied yet. This plan does not write it and does not roll server2.

No `pg_stat_statements` query was run. The 2026-11-13 statement ranking stays the investigation's. No secret and no transcript text was read.

## Ground truth

| Id | What the card assumes | What the code does | What follows |
|---|---|---|---|
| G-1 | Desktop history is empty because the binding is missing | `HostLoadProjection` sets `unknownReasons["physicalHostKey"]` to `binding-unknown` when `PhysicalHostKey` is null (`HostLoadProjection.cs`). `HostLoadHistoryService.Binding` returns null unless exactly one configured row covers `now`, and the route then returns `gaps: ["binding-unknown"]` before it reads samples (`HostLoadHistoryService.cs`). Runner `appsettings.json` sets no `PhysicalHostKey`. `HostLoadCounterCache.Canonical` maps `local` and `desktop` to `desktop`. Ingest sets a sample's `PhysicalHostKey` only when exactly one server row has the same canonical runner, covers `SampledAt`, and equals the sample's `PhysicalHostKey`, `BindingRevision`, and `SourceEpoch` (`HostLoadIngestService.Binding`, `:76-83`). Otherwise the sample is stored with a null key and is never read. The runner takes those three values only from `SessionRunner:HostStats` (`HostStatsSettings.cs:32-34`), read once at start (`IOptions`). `Antiphon.SessionRunner` has no `UserSecretsId`. The desktop runner is the AppHost daemon: `Antiphon.AppHost/Program.cs:32-38` launches the built exe with `--urls` only, working directory `src/Antiphon.SessionRunner` of the main checkout, so that directory is the content root. | Configuration, not a code defect. CARD-1893 D-1 stores the server2 row in desktop user-secrets `antiphon-server` and uses `PhysicalHostKey=server2`. A desktop row must be a different index and a different key. Both halves are needed: the server row and the runner values. |
| G-2 | The process list is only runner, session, and pty-host | `ReadProcesses` emits those three names. `docs/ops-http.md` says the same. CARD-1884 added `topConsumers` (at most eight, ranked by CPU) with roles `compiler`, `build`, `test`, `agent-cli`, `antiphon-runner`, `pty-host`, `docker`, `other`. `HostConsumerClassifier.Match` does not name `Antiphon.Server`. Postgres inside the Docker VM is not a host pid. | 1884 can show a hot compiler. It does not reserve a server row, a Postgres container row, or a sum of every build/test process. Do not change the 1884 ranking, cap, classifier, or empty-`topConsumers` hash omission. |
| G-3 | The server poll already adds the server | `HostStatsPollService.AddServerProcess` runs only for host id `desktop` and appends `antiphon.server` with CPU null and the server working set. A null CPU does not attribute the 0.95 cores. | Keep that fallback for an old runner. Do not append it beside a measured `server` row, and do not replace a measured CPU with null. |
| G-4 | A new process name can be stored in history | `HostLoadProjection` renames any name other than `runner`, `session`, or `pty-host` to `session`. `HostLoadIngestService.Validate` rejects any other name. Renaming would store server CPU as a session and still pass ingest. | Drop role rows before history. Do not widen the ingest allowlist. |
| G-5 | `LIVE_SLOW_CALL` can name the query | The warning template has run, phase, operation, elapsed, outcome. `DbCommandMetrics.LeadingTag` already extracts an allowlisted `-- tag` (`session-state.` or `rt.`, length 1–64, `[a-z0-9.-]`). The executed callbacks do not pass it. `SessionRuntimeActionClaimQuery.Sql` starts with `WITH`, so the largest measured statement has no tag. | Pass `LeadingTag` through. Prefix the claim SQL with `-- rt.runtime-action.claim`. Non-allowlisted text stays `untagged`. |
| G-6 | Host-stats reads retry | `docs/resilience.md`: the host-stats poll and series read make one attempt. | Do not add a retry. |

## Decisions

Defaults for the following review. A no answer changes the slice; the code does not start from a different design inside the same task.

- **D-1. Desktop binding stays an operator write.** Server user-secrets id `antiphon-server`, next free `HostLoadHistory:Bindings:N` (UNOBSERVED whether index 0 is already the CARD-1893 `server2` row; do not overwrite a `server2` row). `RunnerId=local` (canonical `desktop`), `PhysicalHostKey=desktop`, `Revision=1`, `Primary=true`, `From` = the UTC write time, no `To`, `SourceEpoch` = one new GUID. The desktop runner gets the same `PhysicalHostKey`, `BindingRevision`, and `SourceEpoch` under `SessionRunner:HostStats`. `RunnerId` already defaults to `local`. Rejected: tracked `appsettings.json` (CARD-1893 D-1; it would bind every test host), and this dispatch writing the secrets.
  - **Values** (chosen, not derived; validation is `HostLoadHistorySettings.cs:18-21`). `PhysicalHostKey=desktop` (1-128 characters; must not be `server2`). `Revision`/`BindingRevision` = `1` (at least 1; both sides equal). `SourceEpoch` = one GUID made once with `[guid]::NewGuid()` and written to both sides. It must not be `00000000-0000-0000-0000-000000000000`, which refuses start with `HostLoadHistory binding is invalid.`, and it must not reuse the CARD-1893 `server2` epoch. `From` = the UTC time of the write, ISO 8601 with `Z`. Ingest stores a sample only when `SampledAt >= From`, so samples taken before `From` stay unbound. No `To`. `Primary=true`.
  - **Server source.** `dotnet user-secrets set "HostLoadHistory:Bindings:<N>:<Field>" "<value>" --id antiphon-server`, one field at a time: `RunnerId`, `PhysicalHostKey`, `SourceEpoch`, `Revision`, `Primary`, `From`. `N` is the next index the CARD-1893 E-1 projection shows as free. That projection prints only `HostLoadHistory*` keys, never the whole secret list. `N` is `1` when index 0 holds the `server2` row. The server reads user-secrets because the AppHost pins `ASPNETCORE_ENVIRONMENT=Development` for it (`Antiphon.AppHost/Program.cs`, server resource). There is no hot reload (`IOptions`, CARD-1893 G-1).
  - **Runner source.** Gitignored files in the main checkout (`C:\src\Antiphon`): `src\Antiphon.SessionRunner\appsettings.Development.json` and `src\Antiphon.SessionRunner\appsettings.Production.json`. `.gitignore:5` is `appsettings.*.json`. Both files get the same section: `{"SessionRunner":{"HostStats":{"PhysicalHostKey":"desktop","BindingRevision":1,"SourceEpoch":"<the GUID>"}}}`. If a file already exists, add these keys and leave the others alone. The content root is the runner's working directory (`WebApplication.CreateBuilder`; `run-daemon.ps1:163-165` sets `WorkingDirectory`). Both files are needed because the environment name depends on what started the supervisor. An AppHost start runs `dotnet run --no-build` with the AppHost launch profile (`dev-aspire.ps1:140`; `Antiphon.AppHost/Properties/launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`). The supervisor inherits that environment through `UseShellExecute=true` (`DaemonProcessService.cs:100-105`) and passes it to the runner (`run-daemon.ps1:163-166`, `UseShellExecute=false`). A `-Hard` restart, or a restart with no live supervisor, starts the `Antiphon Session Runner` Scheduled Task instead (`session-runner-restart-health.ps1:122,230-234`). That gets the account's registry environment, so it is `Production` unless the account sets the name. Which launcher started today's supervisor is UNOBSERVED. The runner logs `Hosting environment: <name>` at start, which is a cross-check, not a gate.
  - **Rejected runner sources.** User or machine environment variables `SessionRunner__HostStats__*`. Every desktop process would inherit them, including worktree test hosts and E2E's isolated runner. A soft restart does not re-read them, because the supervisor's environment block is fixed when it starts, and an AppHost-launched supervisor inherits the AppHost's environment, which can be older than the write. Also rejected: a code change that adds a runner `UserSecretsId`, which is outside this card's slice.
  - **Side effect, observation only.** With `PhysicalHostKey` set and no `AuthorityEndpoint`, the runner's samples carry an in-process `authority` snapshot (`known`, mode `Lease`, from the local build-slot listing) instead of `unknown` (`HostAdmissionSnapshotClient.cs:27-31`). `/build-slots/host-load` still answers `unknown` until `AuthorityId` is set (`BuildSlotRoutes.cs:73-75`). Neither path acquires, releases, or changes admission (`HostLoadContracts.cs`, `HostAdmissionSnapshot` summary).
- **D-2. Live role rows, not history rows.** The runner appends `server`, `postgres`, `build`, and `test` to the live `processes` list when it can measure them. `HostLoadProjection` drops every other name. It no longer renames an unknown name to `session`. Ingest validation is unchanged, so a sample that still contains only `runner` / `session` / `pty-host` keeps today's canonical hash. Rejected: widening `HostLoadIngestService.Validate` (a new runner would fail ingest on an old server, and a renamed `session` row would hide the server).
- **D-3. Sums come from the scan that CARD-1884 already runs, before the top-eight cut.** `HostTopConsumerTracker.Prepare` computes the sums from processes that already have a CPU delta, then truncates the published ranking exactly as it does now. Sort, cap, classifier stems, and the empty-array hash omission stay. `Antiphon.Server` remains role `other` if it also ranks in the eight. The dedicated row is named `server` and matches that basename, case-insensitive. `build` sums role `build`. `test` sums role `test`. Compiler rows stay in `topConsumers` only. No members means the row is absent. Members whose cores sum to 0 publish 0. First scan has no delta, so the row is absent rather than 0. A scan that stopped early (`Truncated`, published as `TopConsumersTruncated`, which happens at the 512-process cap or when the 10 ms budget runs out) publishes no `build` and no `test` row. A partial sum would be a lower bound that looks like a total (review A-3). The `server` row follows the same rule, because a truncated scan may not have reached the server pid. The server's process basename on the desktop is UNOBSERVED from this Linux mirror (review A-4). If the AppHost runs the server under `dotnet` rather than `Antiphon.Server.exe`, the basename is `dotnet` and the row is absent, which is the fail-closed path. Code checks how the Windows process table handles `.exe`, and its report gives the observed name from the desktop's live `/host-stats`. Rejected: a second process-table walk, and rejected: summing only the published eight (a quiet server falls off when compilers fill the list).
- **D-4. Postgres is `docker stats` text for container `antiphon-postgres`, off the scan.** A pure parser accepts one percentage such as `133.00%` and stores 133 in `CpuPercent` (the process list's unit: 100 = one core). `0.00%` is a real idle reading and is published. Empty text, a missing `%`, a non-finite value, or a throw omits the row. The read has its own timeout and its own cache. It is not inside the 10 ms scan budget. There is no new setting and no cgroup reader. **The timeout is a named constant, `HostStatsRoleProbe.PostgresReadTimeout`, and its value is UNOBSERVED** (review B-4). `docker stats --no-stream` usually waits for a second daemon stats sample before it can print CPU%, and nobody has timed it on the desktop (Docker Desktop on WSL2). server2 has no `antiphon-postgres` container, and this mirror cannot reach the desktop. Code step S0 measures it before S1 (Rollout). The rule is `PostgresReadTimeout` = 2 × the measured p95, rounded up to 100 ms, and the constant's comment cites the S0 measurement. The read reschedules one cadence after it completes, so a published reading is at most one cadence plus one read old. It must stay inside the staleness bound of two cadences (10 s at 5 s), so the timeout must be under one cadence (5,000 ms). **If 2 × p95 is 5,000 ms or more, S0 fails the slice.** Code stops blocked and does not raise the bound or lengthen the cadence. The design then has to change (for example to a streaming `docker stats` reader), and that goes back to plan review. Rejected: cgroup (desktop Postgres is inside the Docker VM, which is where the 2026-11-13 measurement used `docker stats`), and rejected: probing `antiphon-server2-postgres` (that name is CARD-1893's host, and this card's measurement is the desktop container).
- **D-5. Slow-call identity is the allowlisted tag or the word `untagged`.** `RecordExternal` takes an optional tag. The warning line gains `tag={Tag}`. `DbCommandMetrics` passes `LeadingTag`. Anything else, including `Secret Tag`, SQL, or a failed read, logs `tag=untagged` and still logs the warning when elapsed is over 1,000 ms. The claim statement gains the prefix `-- rt.runtime-action.claim` so the existing `rt.` allowlist accepts it. No other query is retagged. There is one allowlist (review A-2). The rule (`session-state.` or `rt.` prefix, `[a-z0-9.-]`, length 1-64) moves from `DbCommandMetrics.LeadingTag` into `Antiphon.Observability`, as a public static method beside `LiveTiming`, because Observability cannot reference the server. `DbCommandMetrics.LeadingTag` calls that method, and `LiveTiming` runs the same method on the tag it receives. That keeps one copy and no twin to pin. `DbCommandMetricsTests` (2) stays green on the moved rule. The server already references `Antiphon.Observability` (`server/Antiphon.Server.csproj:90`). Rejected: a stack walk (`MoveNext`, and a frame can carry a path), and rejected: logging every leading comment (the counter's allowlist exists so a tag cannot become a metric key).
- **D-6. One code slice.** Role rows and the slow-call tag share no migration and no phone-home ordinal. Review of this plan is the next stage. Mutation stays waived (operator 2026-11-12); the positive controls below are designed and not a stage.
- **D-7. Activation is a desktop restart after land, not a server2 roll and not a migration.** The AppHost restart picks up `LIVE_SLOW_CALL` and the poll change. The desktop session-runner restart picks up the role rows. Neither restart is this plan task. History stays `binding-unknown` until the operator applies D-1 and restarts. Each source has its own load. The server row is loaded by the AppHost restart (`pwsh -NoProfile -File scripts/restart-apphost.ps1` from the main checkout). The runner files are loaded by any runner process start. The ordinary soft `pwsh -NoProfile -File scripts/restart-session-runner.ps1` is enough, because the supervisor relaunches the exe in the same working directory and the files are read from there. `-Hard` is not needed and changes the environment name (D-1). Both restarts are the orchestrator's operational autonomy. Writing the values is the operator's step (Q-1). There is no backfill. Twenty-four complete hourly items require a day of ingest after that write. An empty window then reports `missing`, not `binding-unknown` (`HostLoadHistoryService`).

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
- `src/Antiphon.Observability/LiveTiming.cs`. Optional tag on the slow line. The one tag allowlist goes here or in a new file in the same project (D-5, A-2).
- `server/Infrastructure/Data/DbCommandMetrics.cs`. Pass `LeadingTag`.
- `server/Infrastructure/Data/SessionRuntimeActionClaimQuery.cs`. One leading `-- rt.runtime-action.claim` line.
- `docs/ops-http.md`. One sentence: live role rows, history still the three names.
- Tests listed under Verification design.

Existing tests that must stay green, because a role row is omitted when the probe has nothing to say: `C1514HostLoadSampleTests.ProcessCpuDoesNotBecomeHostCpu` (`Processes.Single()`), and `HostTopConsumerTests` (ranking and classifier). They are in CP-1 and CP-2 so a projection change that relabels them fails the row. `RuntimeActionClaimQueryTests` (3) pins `SessionRuntimeActionClaimQuery.cs`, which this slice edits. It covers parameter binding, oracle equivalence, and the `PREPARE ... AS <CommandText>` plan bounds that use that exact text. It runs in CP-4.

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

**Code step S0, before S1 (B-4): measure the Postgres read on the desktop.** This is a read-only step on the desktop main checkout. Either the orchestrator runs it under operational autonomy, or a Code task pinned to the desktop runs it. A server2 Code task gets the numbers in its brief. Twelve timed calls, at least 5 s apart, of `docker stats --no-stream --format '{{.CPUPerc}}' antiphon-postgres`. Use `Measure-Command` for wall time and the `docker` process's `TotalProcessorTime` for the CLI's own CPU (A-6). Record each wall time, p50, p95 (the 12th of 12 sorted; with only 12 samples, p95 is the maximum), the CLI CPU per call, and the 1-minute host load at the time. Then set `PostgresReadTimeout` by the D-4 rule. If 2 × p95 is 5,000 ms or more, S0 fails the slice and Code ends blocked (D-4). The measurement goes in the Code report and in the constant's comment. It is not a checkpoint row, because the checkpoint lane is Linux and has no such container.

**Operator step, pending after land (Q-1).** The operator writes both halves of D-1 and does not apply one without the other:

1. Read the free index with the CARD-1893 E-1 projection. It prints only `HostLoadHistory*` keys.
2. Generate one GUID. Write the six server fields with `dotnet user-secrets set ... --id antiphon-server`, then write the same `PhysicalHostKey`, `BindingRevision`, and `SourceEpoch` into both runner files (D-1).

The orchestrator then runs, from the main checkout, after checking `logs/apphost.restart.lock` and `logs/apphost.launch.lock`:

3. `pwsh -NoProfile -File scripts/restart-apphost.ps1` (exit 0, and `GET /api/version` SHA equal to the main checkout HEAD).
4. `pwsh -NoProfile -File scripts/restart-session-runner.ps1` (soft, exit 0).

The rollback, if start fails with `HostLoadHistory binding is invalid.` or any read-back check below fails: `dotnet user-secrets remove` each written key, remove the three runner keys from both files, and restart both again.

**Read-back stop check, before the 24 h wait starts.** Stop at the first check that fails:

- R-1, server row loaded: `GET /api/hosts/desktop/load` shows `physicalHostKey` equal to `desktop`. That field is the server binding (`HostLoadHistoryService.cs:27-28,52`), so this check proves the server half only.
- R-2, runner publishes the key: within 2 minutes of step 4, the same route shows `current` non-null, `current.physicalHostKey` equal to `desktop`, and `currentAgeSeconds` under 30. `current` comes only from rows that ingest bound, so a non-null `current` means the runner's three values matched the server row (`HostLoadIngestService.cs:76-83`). Optionally, read the runner directly: page `GET http://localhost:17204/host-stats/history?limit=120` by `nextCursor` while `more` is true. The last sample's `physicalHostKey`, `bindingRevision`, and `sourceEpoch` must equal the written values.
- R-3, history no longer unbound: `GET /api/hosts/desktop/load/history?from=<step 4 time>&to=<now>&resolution=raw&limit=5` returns at least one item, and `gaps` is `partial` or empty, never `binding-unknown`.
- R-4, cursor progress (ack): run the CARD-1893 E-4 query, read-only on desktop Postgres, with the connection string never printed, restricted to `"RunnerId"='desktop'`. It shows one row with `cursor_present` true and `CommittedAt` moving forward across two reads 30 s apart. That row must keep its `RunnerStoreId`, because the binding does not change the spool store (CARD-1893 H-1). `gaps_present` is reported but does not gate this card (CARD-1893 H-3 owns it).

Only after R-1 to R-4 pass does the 24 h wait for the card's first verify line begin. The code slice lands and activates without this step. The live `server`, `postgres`, `build`, and `test` rows and the `tag=` field do not need a binding.

## Review and merge

1. Plan review reads this file. It does not start Code.
2. Code commits the red tests, then the fix, and runs the checkpoint table as one serial run with no `--row-timeout`.
3. Code review re-runs CP-1, CP-2, CP-3, and CP-4 and does not edit the branch. Positive controls stay pending. Mutation is waived.
4. Land requires `git fetch origin master` and `git merge-tree --write-tree origin/master HEAD` with no conflicts. Do not merge master into the task branch to get there. A conflict only in `tests/derived-impact-index.json` is regenerated on the land rebase, not hand-merged, and this slice should not touch that file.
5. Merge runs `scripts/impacted-classes.ps1` on the batch range. The fixed high-radius subset still runs. No new selector row.

## Operator decisions

- **Q-1.** After land, should the operator write the D-1 desktop binding at both sources (server user-secrets `antiphon-server` and the runner's two gitignored appsettings files), so the orchestrator restarts and runs R-1 to R-4? **Pending operator. Not a default.** Recommendation: yes, once Code has landed and activated. The code slice lands without it, and no task writes it.
- **Q-2.** Probe only container `antiphon-postgres`, and leave server2 Postgres unprobed? **Yes.** After the CARD-1406 (A2) or CARD-1744 cutover, desktop `antiphon-postgres` is no longer the production database. The row is then absent, or it measures a database that production does not use (review A-5). A follow-up card picks the production container then.
- **Q-3.** Add live `server`, `postgres`, `build`, and `test` rows, and leave compilers on the CARD-1884 top-consumer list? **Yes.**
- **Q-4.** Drop role rows out of durable history instead of widening ingest? **Yes.**
- **Q-5.** Log the allowlisted tag or `untagged`, and tag the claim statement `rt.runtime-action.claim`? **Yes.**
- **Q-6.** Activate with a desktop AppHost restart and a desktop runner restart, and do not roll server2? **Yes.**

## Performance budget

No test ran in this plan dispatch. Durations below are provisional. The checkpoint tool's row deadline is `max(15, 3 × EstimatedMinutes)`. That is wider than the 2× p95 rule. It is the provisional deadline until Code records a p95 and files a Debug task if a row, including slot wait, passes 30 minutes. `ANTIPHON_LINUX_COMPILER_POLICY=0` on every `dotnet` command. Read `/proc/loadavg` and start under 16.

| Row | Selection | Setup | Provisional body | Timeout |
|---|---|---|---|---|
| CP-1 | `HostTopConsumerTests`, 32 of its 33 methods by name. The Windows-only `Production_factory_observes_this_process_on_windows` is left out; it is owed to the native Windows lane. | No container. One cold `Antiphon.SessionRunner.Tests` build in `bin-c1889-role/`. Slot wait is outside the test body. | Unit tests plus the existing `Timeout(8_000)`/`Timeout(30_000)` scan tests, seconds to a minute after the build. Build is the cost. | Estimate 15 min. Derived row deadline 45 min. |
| CP-2 | `HostStatsRoleProbeTests` (3 new) and `C1514HostLoadSampleTests` (6) | Reuses the CP-1 build (`CP-1`, same `After`). | Seconds. | Estimate 2 min. Derived row deadline 15 min. |
| CP-3 | `LiveTimingTests` (4 existing + 1 new) | No container. Cold `Antiphon.Observability.Tests` build in `bin-c1889-timing/`. | Seconds after the build. | Estimate 8 min. Derived row deadline 24 min. |
| CP-4 | `HostStatsPollServiceTests` (10 + 1), `DbCommandMetricsTests` (2 + 1), and `RuntimeActionClaimQueryTests` (3) | `TestDbFixture` once per `Antiphon.Tests` assembly (shared Postgres; schema cache UNOBSERVED on this host). Each poll test starts an in-process phone-home host. The two claim-query database tests each create an isolated schema and run `EXPLAIN`. No desktop `antiphon-postgres`. Cold build in `bin-c1889-server/`. | Build dominates. Fixture start provisional 60 s. Claim-query body UNOBSERVED, provisionally under 2 min. | Estimate 15 min. Derived row deadline 45 min. |

Derived total is `2 × (15 + 2 + 8 + 15) + 10 = 90` minutes. The run is one serial checkpoint invocation and does not pass `--row-timeout`. Per-test phase timing stays the existing opt-in. No new external-call span. The Postgres parser is pure and is not a docker call inside the test.

The production `docker stats` call is not a test cost. Its wall time sets `PostgresReadTimeout` (D-4). Its host cost is one `docker` CLI process per 5 s cadence on the desktop, that is 12 per minute, and both are UNOBSERVED until S0 records the wall p95 and the CLI CPU per call (review A-6). If S0 shows the CLI costs more than 0.05 cores (60 ms of CPU per call), the Code report says so and Q-2 is put to the operator again before activation.

## Verification design

Red at `300c8a99d0d267a39791410b7540189ab702f251` means the assertion is false in today's code. A witness that already passes is not one of these.

- **V-1** `HostStatsRoleProbeTests.Server_row_uses_the_server_basename_and_history_drops_it`. Observations include pid 50, name `Antiphon.Server`, role `other`, and a delta of 0.95 cores. The live list has one `server` row at `CpuPercent` 95. `HostLoadProjection` does not contain `server` and does not contain an extra `session` made from it. Red today: `ReadProcesses` never emits `server`, and projection renames any other name to `session`.
- **V-2** `HostStatsRoleProbeTests.Postgres_percent_is_published_and_a_fault_omits_the_row`. Text `133.00%` publishes `postgres` at 133. A throw publishes no `postgres` row and no row whose CPU is 0. Red today: there is no parser, so the 133 assertion fails.
- **V-3** `HostStatsRoleProbeTests.Build_and_test_sums_include_processes_outside_the_top_eight`. Nine `build` processes at 0.2 cores each publish one `build` row at 180, even though the top eight cannot hold all nine. A `compiler` observation is not in that sum. `Antiphon.Server` is still role `other` on the top-consumer row. A scan that ends with `Truncated` true publishes no `build`, `test`, or `server` row (D-3, A-3). Red today: the tracker publishes only eight ranked rows and no `build` sum.
- **V-4** `LiveTimingTests.Slow_database_line_names_the_allowlisted_tag`. `RecordExternal(Database, 1500 ms, succeeded, "session-state.seed")` logs `tag=session-state.seed`. The same call with `Secret Tag` or text containing `SELECT` logs `tag=untagged` and does not log `SELECT` or `Secret`. A 999 ms call logs nothing. Red today: the template has no tag. Code adds the optional parameter in the failing-test commit and leaves the template unchanged, so the test compiles and the assertion fails.
- **V-5** `DbCommandMetricsTests.Claim_query_text_starts_with_the_allowlisted_runtime_tag`. `SessionRuntimeActionClaimQuery.Query(...).CreateDbCommand().CommandText` starts with `-- rt.runtime-action.claim\n`, and `DbCommandMetrics.LeadingTag(text)` returns `rt.runtime-action.claim`. This uses the same unused-host `AppDbContext` as `RuntimeActionClaimQueryTests.Query_binds_the_phase_and_channel_kind_supplied_by_its_caller` (`:21-28`), so it needs no database. It does not use `ToQueryString()`. EF Core's relational query-string factory writes one `-- <parameter>=<value>` line per bound parameter before the command text, and this query binds four, so that string would start with a parameter line even after the fix (review B-3). Red today: `CommandText` starts with `WITH`, so `StartsWith` fails and `LeadingTag` returns null.
- **V-6** `HostStatsPollServiceTests.Desktop_keeps_a_measured_server_row`. The desktop sample already has `server` at CPU 95. After one tick the projected desktop processes still have that 95 and do not also contain `antiphon.server`. Red today: `AddServerProcess` always appends `antiphon.server` with a null CPU.

`HostTopConsumerTests` (32 of 33 selected by name in CP-1) and `C1514HostLoadSampleTests` (6, in CP-2) keep the ranking and the `Processes.Single()` session row green. `RuntimeActionClaimQueryTests` (3, in CP-4) keeps the tagged claim text bound and planned. None of them is a red witness. The 33rd method, `HostTopConsumerTests.Production_factory_observes_this_process_on_windows`, calls `Skip.Test` off Windows (`:52-57`). `ReportValidator` fails a row with any skipped test, so it is not selected here. It is owed to the native Windows lane and runs there unchanged.

Positive controls, pending, mutation waived:

| PC | Mutant | Witness | Expected red |
|---|---|---|---|
| PC-1 | Publish CPU 0 when the docker reader throws | `/*/*/HostStatsRoleProbeTests/Postgres_percent_is_published_and_a_fault_omits_the_row` | A `postgres` row appears with CPU 0. |
| PC-2 | Rename a `server` process to `session` in the projection | `/*/*/HostStatsRoleProbeTests/Server_row_uses_the_server_basename_and_history_drops_it` | History contains a `session` that was not a session. |
| PC-3 | Copy command text into the slow line when the tag is rejected | `/*/*/LiveTimingTests/Slow_database_line_names_the_allowlisted_tag` | The warning contains `SELECT`. |
| PC-4 | Keep appending null-CPU `antiphon.server` beside `server` | `/*/*/HostStatsPollServiceTests/Desktop_keeps_a_measured_server_row` | Both rows are present. |
| PC-5 | Publish the `build` sum when the scan is truncated | `/*/*/HostStatsRoleProbeTests/Build_and_test_sums_include_processes_outside_the_top_eight` | A `build` row appears after a truncated scan. |

Code runs, after the red tests exist and the fix is in, one serial run, with `ANTIPHON_LINUX_COMPILER_POLICY=0` and load average under 16, and without `--row-timeout`:

`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-11-17-card-1889-host-stats-gap-plan.md --serial`

Build through the checkpoint tool so it takes the host slot. Do not pass `-o`, `--output`, or `OutputPath` on a direct build. The output directories below belong to the checkpoint rows.

### Checkpoints

Lane: Linux. Serial. No `-Runner`, no `-Platform`, no `--row-timeout`. `totalTimeoutMinutes` is the derived floor. Provisional estimates. CP-1 carries the cold-build 15 minutes so its derived deadline is 45 minutes. CP-1 selects `HostTopConsumerTests` by method: 32 of 33 methods, every operand parenthesised with a trailing `*`. The Windows-only `Production_factory_observes_this_process_on_windows` is not selected, because a skipped test fails validation (`ReportValidator.cs:52-54`). It is owed to the native Windows lane. No operand matches it, and no class operand has a suffix twin under `tests/` (checked 2026-11-17; `HostStatsRoleProbeTests` is new, and Code must not add another class with that prefix). CP-2 reuses the CP-1 build in the same `After` group.

totalTimeoutMinutes: 90

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1889-role/ | role | `/*/*/HostTopConsumerTests/(Production_factory_selects*)\|(Factory_*)\|(Shared_*)\|(Pid_*)\|(Vanished_*)\|(Top_*)\|(Command_*)\|(Scan_*)\|(Short_*)\|(Missing_*)\|(Process_*)\|(Blocking_*)\|(Unsafe_*)\|(Roles_*)\|(Old_*)\|(Projection_*)\|(Eight_*)\|(Live_*)\|(Held_*)\|(Stop_*)\|(Dispose_*)\|(Stalled_*)\|(Enumeration_*)\|(Windows_*)\|(Cached_*)\|(Source_*)` | CARD-1884 guard | >= 32 executed, 0 failed, 0 skipped | 32 | 15 |
| CP-2 | S1 | CP-1 | role | `/*/*/(HostStatsRoleProbeTests*)\|(C1514HostLoadSampleTests*)/*` | V-1, V-2, V-3 | >= 9 executed, 0 failed | 9 | 2 |
| CP-3 | S1 | tests/Antiphon.Observability.Tests -> bin-c1889-timing/ | timing | `/*/*/LiveTimingTests/*` | V-4 | >= 5 executed, 0 failed | 5 | 8 |
| CP-4 | S1 | tests/Antiphon.Tests -> bin-c1889-server/ | slow | `/*/*/(HostStatsPollServiceTests*)\|(DbCommandMetricsTests*)\|(RuntimeActionClaimQueryTests*)/*` | V-5, V-6 | >= 17 executed, 0 failed | 17 | 15 |
