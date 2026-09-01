# CARD-0298 — Native Hangfire zombie census plan

## Decision

Implement one **daily, report-only** Hangfire recurring job inside the server for the
card-level **Class B OS zombie census**. Use the locked packages
`Hangfire.AspNetCore` **1.8.25** and official `Hangfire.InMemory` **1.0.0**, with a
single in-process worker and the built-in local-only dashboard at `/hangfire`. Configure
in-memory `MaxExpirationTime` to **eight days**, rather than accepting its three-hour
default: this preserves the previous daily run and a week of burn-in history while still
bounding process-lifetime memory. Restarting the server deliberately loses the in-memory
definitions and history, so startup must call `RecurringJob.AddOrUpdate` every time.

The job ports the PowerShell script's process census, identity ladder (I1–I5), and rule
evaluation (Z1–Z7) to C#. It is observational in v1: it reads WMI, runner, manifests,
and Postgres; logs a structured result; and returns normally even when it finds candidates.
It does **not** kill a process, call a session kill endpoint, alter an agent/session/task/card,
or invoke `AttentionService`.

This is intentionally different from CARD-0239's card-level Class C
`AgentOutlivedTask` projection. That projection is read-time only and explicitly has no
hosted sweep or script change [docs/superpowers/plans/2026-09-01-card-0239-agent-outlived-task.md:190-202].
The port retains the script's names `PoolExpired`, `ReconcilerOwned`,
`EndedButAlive`, and `Unclaimed` as **OS-census labels**; they are not a second
implementation of `AgentOutlivedTask`.

No database migration, API endpoint, client work, scheduler script, or external service is
introduced. Keep `scripts/reap-zombie-agents.ps1` as the manual, independently usable
operator tool; the native job must never shell out to it.

## Existing constraints and evidence

- The server targets `net9.0` [server/Antiphon.Server.csproj:3-9], already wires the
  session-runner through `ISessionRunnerClient` [server/Program.cs:217-227], and uses
  `Program.cs` as the DI composition root [docs/project-context.md:76-90].
- The current hosted-service block is unconditional [server/Program.cs:489-515]. A
  `WebApplicationFactory<Program>` therefore boots those workers against normal settings;
  the documented production-runner leak is the directly comparable test-safety failure
  [docs/testing-and-build.md:28-30]. `ProductionRunnerGuard` sets process-wide overrides
  before every test host boot [tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs:27-65],
  while `AntiphonWebAppFactory` reinforces them in its own in-memory configuration and
  replaces the runner client [tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs:71-113].
- The local stack pins Development in AppHost [Antiphon.AppHost/Program.cs:60-77], so an
  environment-only dashboard gate would not be a security boundary. `CurrentUserMiddleware`
  currently creates a hard-coded admin rather than authenticating a request
  [server/Api/Middleware/CurrentUserMiddleware.cs:6-45]. Use Hangfire's explicit
  `LocalRequestsOnlyAuthorizationFilter`; do not invent HTTP auth or rely on Development.
- The script makes a candidate safe only when every relevant rule passes
  [scripts/reap-zombie-agents.ps1:5-42]. Its injected fixture suite already covers the
  normal census, pool expiry, warm-pool grace, reconciliation ownership, quiet activity
  gate, pid reuse, unavailable prerequisites, and selected kill path
  [scripts/test-reap-zombie-agents.ps1:219-239,304-318,365-376,415-429,468-580,594-607].
- Failed/Stopped rows that the runner still claims are owned by reconciliation, not this
  job: failed is positively re-adopted, stopped is the single pre-authorised retry-kill arm,
  and a missing row is never killed [server/Application/Services/SessionReconciliationService.cs:292-305,393-545].

## Slice 1 — dependencies, typed configuration, and safe host wiring

**Files**

- Modify `server/Antiphon.Server.csproj`.
- Add `server/Application/Settings/HangfireSettings.cs` and its validator.
- Add `server/Application/Settings/ZombieCensusSettings.cs` and its validator.
- Modify `server/appsettings.json`.
- Modify `server/Program.cs`.

1. Add exactly these scheduling packages to the server project:

   ```xml
   <PackageReference Include="Hangfire.AspNetCore" Version="1.8.25" />
   <PackageReference Include="Hangfire.InMemory" Version="1.0.0" />
   ```

   Do not add the `Hangfire` metapackage and do not add the deprecated community
   `Hangfire.MemoryStorage` package. Add `System.Management` at the repository's existing
   `9.*` convention as the separate Windows WMI dependency: the existing WMI precedent is
   compiled by an explicit `System.Management` reference
   [tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj:14-19]. It is
   not a Hangfire storage dependency.

2. Bind and validate two typed settings sections, consistent with the existing
   `IOptions<T>` pattern [docs/project-context.md:119-123]. `HangfireSettings` owns
   `ServerEnabled` (default `true`) and `HistoryRetentionDays` (default `8`, positive).
   `ZombieCensusSettings` owns `Enabled` (default `true`), the fixed recurring-job id,
   five-field daily cron (`30 9 * * *`), `Europe/London` timezone id, and the script's
   non-action thresholds: 120-minute done/age floor, six quiet hours, five-second pid-reuse
   tolerance, and the runner `C:\logs\antiphon\session-runner` log root. Validate a
   positive retention/threshold, parsable cron, and resolvable timezone at startup. The
   Windows path is intentionally backslash-form.

3. In `Program.cs`, configure `AddHangfire` with `UseInMemoryStorage(new
   InMemoryStorageOptions { MaxExpirationTime = TimeSpan.FromDays(...) })`. Register the
   scoped census service/job and its external Windows-process seam in the existing service
   registration area. Add `AddHangfireServer` only when `Hangfire:ServerEnabled` is true;
   set `WorkerCount = 1` because this deployment has one low-frequency, read-only job.
   `AddHangfire` itself is safe to register in test hosts, but the server is not: no worker
   means no WMI census and no runner call.

4. Add both test defences before any `Program` boot. Extend `ProductionRunnerGuard` with
   `Hangfire__ServerEnabled=false`, and repeat `Hangfire:ServerEnabled=false` in
   `AntiphonWebAppFactory`'s in-memory overrides. This covers both bare factories and the
   shared factory, exactly as the existing runner guard does. Do not infer test mode from
   `IHostEnvironment` or from Development.

5. After the existing migrate/seed block has completed, and only when both server and census
   settings are enabled, call `RecurringJob.AddOrUpdate` for a stable id such as
   `antiphon:zombie-census`. Target the DI-activated `ZombieCensusJob`, use the configured
   cron and London timezone, and do this on **every** process start. The worker begins only
   when the host starts, so registration completes before its first execution. The explicit
   re-registration is required because the selected storage does not survive an AppHost
   restart.

6. Map `app.MapHangfireDashboard("/hangfire", new DashboardOptions { Authorization =
   [new LocalRequestsOnlyAuthorizationFilter()] })` after the normal endpoint mappings but
   before `MapFallbackToFile` [server/Program.cs:643-680]. Spell out the built-in filter rather
   than relying on a package default; there is no new policy, credentials, or environment gate.

## Slice 2 — native, testable census port

**Files**

- Add `server/Application/Dtos/ZombieCensusDtos.cs` for immutable process, identity,
  database-snapshot, verdict, count, and result records.
- Add `server/Application/Interfaces/IZombieProcessCensus.cs` as the external OS-read seam.
- Add `server/Infrastructure/Agents/WindowsZombieProcessCensus.cs`.
- Add `server/Infrastructure/Agents/ZombieCensusService.cs`.
- Add `server/Infrastructure/Agents/ZombieCensusJob.cs`.

`ZombieCensusService` is scoped because it reads `AppDbContext`; its classifier is a
stateless concrete collaborator with immutable inputs, so tests can exercise it with
fixture-shaped records and never enumerate the developer's machine. The service performs a
single census run as follows:

1. Ask `IZombieProcessCensus` for a WMI process snapshot, then obtain
   `ISessionRunnerClient.ListAsync` once. Project `AgentSessions`, `Agents`, and
   `AgentTasks` with `AsNoTracking` from `AppDbContext`; do not reproduce the script's
   `docker exec psql` transport [scripts/reap-zombie-agents.ps1:236-356]. Read manifests from
   the configured runner path with `System.Text.Json`, never a regex or shell command.
2. Build the pid-to-runner map from both `Pid` and `HostPid`, which the DTO already exposes
   [server/Application/Dtos/SessionRunnerDtos.cs:5-33]. Build parent chains with loop
   detection, exclude WindowsApps and operator-launched ancestry, then apply I1 through I5 in
   the script's exact first-answer-wins order [scripts/reap-zombie-agents.ps1:445-665].
3. Evaluate Z2 through Z6 using the same UTC thresholds and record all failed-rule text on
   every row. Preserve the four script labels, including `ReconcilerOwned` as an observation
   only. For an `EndedButAlive` OS label, preserve the script's ansi/Claude activity check;
   this reads filesystem mtimes only and never calls `AttentionService`.
4. Return a `ZombieCensusResult` containing bounded row details, class counts, ignored and
   unresolved counts, duration, and prerequisite failures. The job logs the result; it does
   not translate script exit code `3` into a failed Hangfire execution because a reported
   candidate is the expected result of a dry run.

### Port map

| Script responsibility | Native implementation | Constraint / deliberate boundary |
|---|---|---|
| WMI process data plus five-second CPU sample [scripts/reap-zombie-agents.ps1:408-442] | `WindowsZombieProcessCensus` uses `ManagementObjectSearcher`/`Win32_Process`, snapshots the necessary fields twice, and calculates CPU only as report-only data. | This is the only Windows-specific hard part. Treat WMI/property access failure as a failed census, never as an empty process list; no test calls this seam. The same WMI pattern already handles per-property access failure in `PtyHostLeakSweep` [tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs:122-167]. |
| Runner `GET /sessions` [scripts/reap-zombie-agents.ps1:238-256] | One `ISessionRunnerClient.ListAsync` call. | Reuse server-owned HTTP/configuration and honour its cancellation token; do not call raw HTTP. An unavailable runner fails the Hangfire invocation visibly. |
| Docker/psql JSON census [scripts/reap-zombie-agents.ps1:258-356] | `AppDbContext` projections over the same session/agent/task facts. | No shell, container name, or production connection string leaks into the job; no tracking or writes. |
| Manifest and activity lookup [scripts/reap-zombie-agents.ps1:359-380,548-574] | Typed JSON manifest read and direct `FileInfo.LastWriteTimeUtc` probes. | Read-only and best-effort only for the OS `EndedButAlive` label. It must not read credentials or use transcript content. |
| I1–I5 and Z1–Z7 [scripts/reap-zombie-agents.ps1:600-821] | Pure records plus a deterministic classifier. | Fixture tests pin precedence, pre-filters, pid reuse, timing, and class boundaries independently of WMI/EF/runner. |
| Server/runner/taskkill actions [scripts/reap-zombie-agents.ps1:909-995] | Not invoked in v1. Keep an internal future execution seam next to the verdict model. | A later, separately approved execution change must reuse `AgentSessionService`/runner kill for owned sessions and `RunnerProcessCleanup.KillTree` for a proven orphan tree [server/Infrastructure/Agents/Tui/RunnerProcessReaper.cs:327-369]; it must re-prove the same rules immediately before acting. |

`ZombieCensusJob.ExecuteAsync` calls the service once, logs, and returns the result. Disable
automatic retry for this job: a prerequisite outage should produce one failed dashboard entry
and a normal next-day attempt, rather than repeated WMI/runner scans. It must accept the
host/job cancellation signal and pass it to every async call.

## Slice 3 — reporting, dashboard, and burn-in contract

The job has no separate JSON-report directory and no database table: the card chose in-memory
Hangfire specifically to avoid new persistence. Its operator surfaces are:

- **Information:** one structured completion record with duration and every class/count,
  including zero candidates.
- **Warning:** a bounded per-candidate record and a bounded summary when `PoolExpired` or
  `EndedButAlive` passes all rules; include pid, identity method, session id, label, and
  failed-rule/ownership context, but never command lines, transcript content, or secrets.
- **Error:** WMI, runner, manifest, or database read failure, rethrown so the Hangfire run is
  shown Failed. A cancellation caused by host shutdown is not logged as an error.
- **Hangfire dashboard:** `/hangfire` shows the recurring definition, next/last invocation,
  duration, and success/failure state. It is execution history, not a replacement for Serilog
  row detail; operators use the server log for the structured census evidence. The eight-day
  storage horizon means yesterday's result remains visible before the next daily fire.

Keep the existing server Serilog policy: `Antiphon` is explicitly retained at Information
[server/appsettings.json:211-224], and the operational guide confirms Serilog rather than
`Logging:LogLevel` decides persistence [docs/bootstrap.md:372-398].

For v1, every discovered candidate is report-only. Do not add an `Execute` configuration switch
that can be flipped silently. After the burn-in has independently demonstrated the CARD-0237
0/0 false-positive bar, file/reopen a separate decision and implementation slice for actions;
it must re-evaluate immediately before mutation and add action-specific tests. The manual
PowerShell `-Execute` path remains the only execution mechanism until then.

## Slice 4 — tests and verification

**Files**

- Add `tests/Antiphon.Tests/Infrastructure/ZombieCensusServiceTests.cs`.
- Add `tests/Antiphon.Tests/Infrastructure/HangfireStartupSafetyTests.cs`.
- Modify `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs`.
- Modify `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs`.

Use a fake `IZombieProcessCensus`, fake/recording `ISessionRunnerClient`, and an isolated test
schema. Do not invoke WMI or the production runner from a test. Scope all database assertions to
the seeded ids; the suite shares PostgreSQL and global sweep tests need the documented isolation
[docs/testing-and-build.md:24-30].

| Scenario | Expected result |
|---|---|
| Normal 22-process fixture | 21 I1 runner claims, one WindowsTerminal/operator exclusion, six Codex three-hop chains, zero candidates. |
| Historical pool-expiry fixture | One `PoolExpired` verdict with I1/server ownership metadata; the job logs Warning but performs no kill. |
| Warm pooled task only 20 minutes old | Fails age/done rules and is not a candidate. |
| Failed/Stopped row still runner-claimed | `ReconcilerOwned` observation, no candidate, no runner/session call beyond the initial list; proves reconciliation remains authoritative. |
| Unclaimed terminal OS process with recent then old activity | Recent activity fails Z5; old activity yields the OS `EndedButAlive` observation only. Neither path queries or duplicates `AttentionService`. |
| Pid created before session start | Z3 fails and cannot be a candidate. |
| Runner, WMI, or DB projection failure | Job logs/rethrows a contextual failure; Hangfire records Failed, with no mutation. |
| Server test host boot | `Hangfire:ServerEnabled=false` results in no Hangfire worker, no process-census invocation, and no runner call. Pin both assembly guard and factory override. |
| Production registration configuration | The configured storage exposes an eight-day maximum expiration, `antiphon:zombie-census` is re-added on a fresh in-memory host, and its cron/timezone are the configured daily London values. |
| Dashboard route | A loopback request reaches `/hangfire`; a non-local request is rejected by `LocalRequestsOnlyAuthorizationFilter`. No custom auth/policy is registered. |

Implementation verification runs the focused TUnit class with `dotnet run --project
tests/Antiphon.Tests -- --treenode-filter "/*/Antiphon.Tests.Infrastructure/*/ZombieCensus*"`,
then the Hangfire startup class, followed by the relevant full server test namespace. Run
`Antiphon.Tests` separately from the pty suite, as required by the test operations guide
[docs/testing-and-build.md:8-10]. If daemon file locks require it, use an isolated OutputPath
ending in a forward slash [docs/testing-and-build.md:32-34].

After implementation and tests, restart the canonical AppHost with
`pwsh -NoProfile -File scripts/restart-apphost.ps1`, verify the stack with
`pwsh -File verify-dev-stack.ps1 -SkipBrowser`, open local `/hangfire`, trigger one manual
dry-run invocation, and inspect its structured server-log summary. Do not run that operational
smoke from a linked worktree; the restart controls shared ports
[docs/bootstrap.md:464-468].

## Files and non-goals summary

| Change | Purpose |
|---|---|
| `server/Antiphon.Server.csproj` | Locked Hangfire packages and required WMI assembly. |
| `server/appsettings.json`, new typed settings/validators | Explicit worker/test gate, bounded history, cadence, timezone, and census thresholds. |
| `server/Program.cs` | Hangfire storage/server/dashboard wiring and process-start recurring registration. |
| New application DTO/interface and infrastructure census/job files | Native, read-only WMI/runner/EF/filesystem port with a pure classification core. |
| Test helpers plus two new infrastructure test classes | Fixture parity, no worker in web-factory boots, storage/registration/dashboard safety. |

No migrations, API/React changes, `AttentionService` changes, tracker writes, automatic process
cleanup, or changes to the existing PowerShell script are in this card.
