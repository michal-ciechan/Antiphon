# CARD-1890 plan: server log volume (per-request and per-action Information lines)

Plan task 5db7c600, 2026-11-17. Read at `origin/master` **`300c8a99d0d267a39791410b7540189ab702f251`**.
Plan only: no code, setting, restart or host change was made. Card state: Backlog, rank 10, one
revision (2026-11-14 CPU-share append). It is not parked, not operator-only, not done on master,
and does not touch the selector/index chain held by CARD-1820.

## Problem measured

### Evidence the card cites (2026-11-13, Investigate 1540fb0b)

Source: the card and `docs/investigations/2026-11-13-card-1886-antiphon-cpu-attribution.md:62-68`.

| Fact | Value | Status |
|---|---|---|
| Window | `C:\src\Antiphon\server\logs\antiphon-20261113_001.log`, 07:06:30-13:44:30Z (6.63 h) | cited |
| Lines | 244,266 = 36,800/h; 192,061 INF, 10,599 WRN, 150 ERR | cited |
| `SessionRuntimeActionCoordinator` | 72,134 lines (29.5%) | cited |
| `EndpointMiddleware` + `OkObjectResult` | 44,019 + 43,128 = 87,147 (35.7%) | cited |
| `CurrentUserMiddleware` | 22,008 (9.0%) | cited |
| Stuck action | 878325e0 phase 8, `attempts 31`, every 30 s for >1.6 h | cited |
| CPU share of logging | 1.4 s of 300 s sampled = 0.005 cores | cited (card revision 1) |

**Fresh hour count: UNOBSERVED.** The brief asked for a fresh per-SourceContext and per-template
count of one full hour of the desktop Serilog file. This plan ran on the server2 runner mirror.
That file is on the desktop disk. `docs/logs.md:11` documents only local reads of it
(`scripts/logs.ps1 -Source desktop-server`), and the server exposes no log-read route
(`docs/antiphon-api.md` has none). The read-only script for the desktop lane, run from
`C:\src\Antiphon` (prints counts and templates only, never payloads):

```powershell
$f = Get-ChildItem C:\src\Antiphon\server\logs\antiphon-*.log | Sort-Object LastWriteTime | Select-Object -Last 1
$from = (Get-Date).ToUniversalTime().AddHours(-2); $to = $from.AddHours(1)
$rx = '^(?<ts>\S+ \S+ \S+) \[(?<lvl>\w{3})\] .*"SourceContext": "(?<src>[^"]+)"'
Get-Content $f.FullName -ReadCount 5000 | ForEach-Object { $_ } |
  ForEach-Object { if ($_ -match $rx) { $t = [datetimeoffset]::Parse($matches.ts).UtcDateTime
    if ($t -ge $from -and $t -lt $to) { [pscustomobject]@{ Src = $matches.src; Lvl = $matches.lvl } } } } |
  Group-Object Src, Lvl | Sort-Object Count -Descending | Select-Object Count, Name -First 25
```

Message-template counts need the `MessageTemplate` property, which this output template
(`{Message:lj} {Properties:j}`, `server/Program.cs:117`) does not write. Grouping by the rendered
message would print payloads, so per-template counts stay UNOBSERVED unless the desktop lane reads
the code's templates (listed under Design) against the SourceContext counts.

### New measurements by this plan (read-only)

`pg_stat_statements` on the desktop production database `antiphon` (stats reset
2026-11-11T17:45:28Z, read 2026-11-17T07:33Z, 133.8 h), `BEGIN READ ONLY` ... `ROLLBACK`:

- The coordinator's log-only debt query
  `SELECT count(*)::int AS "Count", max(s."Sequence") AS "Frontier" FROM "SessionRuntimeActions" ... GROUP BY`
  ran **1,466,281 times** (about 10,960 per hour, 3.0 per second; 31.0 s total execution, 0.02 ms mean).
  It exists only to fill the `Action worker start|end` log line (`SessionRuntimeActionCoordinator.cs:438-451`).
  So there were about 733,000 drain pages, or 5,480 per hour. The card's 72,134 coordinator lines in 6.63 h
  are 10,880 per hour, which matches two worker lines per drain plus the phase lines.
- `SessionRuntimeActions` now holds 4 rows. All are phase 8 `FailureReason=Deferred`
  (`TriggerKind=transcript`), two at `Attempts=31` (age 0.9 h) and two at 13-14 (age 0.1 h). A
  deferred queue action logs **only** the Information phase line every 30 s and no Warning
  (`SessionRuntimeActionCoordinator.cs:298-306`, no `Diagnostic` call). That line is the only log
  evidence that a queue action is stuck.

### What the volume costs

- **Disk and retention (CARD-0043): the 5-day retention is not delivered.** The card's "190 MB per
  day" is the total by 13:44Z, not a full day. The measured file alone is 84.7 MB in 6.63 h =
  **12.8 MB/h** (about 306 MB/day, about 350 bytes/line). The disk backstop is 14 files x 100 MB =
  1,400 MB. `FileLogRetentionPolicy.MaxMegabytesPerHourFor(5)` = 11.67 MB/h
  (`tests/Antiphon.Tests/Infrastructure/LogRetentionTests.cs:73-77`). At 12.8 MB/h the count cap
  evicts after 1,400 / 12.8 = 109 h = **4.6 days**. CARD-0043's "five days of history" fails
  silently at today's rate. Its test constants (0.032 MB/h average, 0.92 MB/h peak) are 2026-08-13
  values and are now 14-400x stale. The earlier 104.9 MB file implies 14.8 MB/h if it started at
  00:00Z. The roll time zone is UNOBSERVED, so this plan does not use that number.
- **The log shipper: none exists.** No shipper (Promtail, Loki, Seq, Vector, Fluent Bit, Filebeat)
  is configured in this repository. The Console sink duplicates every line (`server/Program.cs:102-105`,
  level Verbose unless `Serilog:ConsoleMinimumLevel` is set). On the desktop that goes to the AppHost
  and Aspire dashboard. On the server2 production server it goes to `docker logs`, whose rotation
  `docs/logs.md:84` records as "not established". Its size is UNOBSERVED.
- **Time to read.** Of every 100 lines, 74 are request or action bookkeeping. An incident read of
  the last hour scans about 36,800 lines for about 2,000 WRN/ERR and lifecycle lines. A grep for a
  task id is drowned by 39-per-minute `GET /api/agent-tasks/{id}` request lines. This cost is
  qualitative; read time was not measured.
- **CPU: not a lever** (card revision 1: 0.005 cores). The debt query is cheap in Postgres (31 s
  over 5.6 days). Its scope, DbContext and round trip on the server side cost something per drain,
  but that cost is UNOBSERVED. Removing the query is a side benefit, not the goal.

### Ground truth: what the card assumes vs what the code does

| Card assumes | Code at 300c8a99 | Consequence |
|---|---|---|
| "Request timing is already in `LiveTiming`" | `LiveTiming` (`src/Antiphon.Observability/LiveTiming.cs:105-108`) logs startup phases and outbound external calls (`LIVE_TIMING`, `LIVE_SLOW_CALL`). It does **not** time inbound requests. | Dropping all four request lines would leave no per-request line at all. `CurrentUserMiddleware` would become the only one, so it stays (D-1). |
| `CurrentUserMiddleware` is noise | It is NFR11 ("Client IP is logged on every API request for audit trail", `_bmad-output/planning-artifacts/prd.md:597`; `CurrentUserMiddleware.cs:8,32`). The `"Antiphon": "Information"` override pins it. | Turning it down is an audit-requirement change and is the operator's call (Q-1). |
| `Logging:LogLevel:Microsoft.AspNetCore=Warning` already quiets ASP.NET | `UseSerilog` (`server/Program.cs:81`) replaces the MEL factory. Only `Serilog:MinimumLevel:Override` applies, and it overrides only `Microsoft.AspNetCore.Hosting.Diagnostics`. | That is why EndpointMiddleware and Result lines appear. The fix belongs in the Serilog override block, as CARD-0043 did. |
| About 39,000 `GET /api/agent-tasks/{id}` requests in 6.6 h | 44,019 EndpointMiddleware lines (2 per request) and 22,008 CurrentUserMiddleware lines (1 per request) mean about **22,000 requests of all routes** in the window. | The 39,000 figure probably counted two lines per request. Each request costs 5 INF lines (2 + 2 + 1), and 22,000 x 5 = 109,155 lines. The poller question stays a separate card (Q-3). |
| Coordinator lines are three per action | `ObserveDebtAsync` writes worker start and end lines **per drain page** (`:146-148`), and each of those also runs a DB query. `ObserveAction` writes once per claimed phase pass (`:185`). | The saving includes about 10,960 queries per hour, not only lines. |
| "rate-limit the repeated retry line" | Failed phases log `Warning "Action phase retained for retry"` on every attempt (`:201`). Deferred phases log Information only (`:298-306`). | Leave the Warning unchanged (failure evidence). Rate-limit only the Information phase line for `Attempts > 0`. |

## Design

### Smallest change

**S1. Configuration only** (`server/appsettings.json`, `Serilog:MinimumLevel:Override`), the
CARD-0043 pattern:

```json
"Microsoft.AspNetCore.Routing.EndpointMiddleware": "Warning",
"Microsoft.AspNetCore.Http.Result": "Warning"
```

Serilog matches an override by prefix, so the second key covers `...Http.Result.OkObjectResult` and its
siblings. This removes 87,147 lines out of 244,266 (35.7%). It needs no code change, and a config
edit re-arms it.

**S2. Coordinator** (`server/Application/Services/SessionRuntimeActionCoordinator.cs`):

1. `ObserveDebtAsync`: return before the scope and query unless `logger.IsEnabled(LogLevel.Debug)`.
   Log `Action worker {Transition} ...` at **Debug**. The template is unchanged.
2. `ObserveAction`: log the phase line at **Debug** when `action.Attempts == 0`. When
   `Attempts > 0` (a retry or a deferral), log it at **Information** the first time this action id is
   seen and then at most once per `RetryLogInterval` (10 minutes) per action id. Other passes log at
   Debug. Keep the state in a bounded `ConcurrentDictionary<Guid, DateTime>` (capacity 1,024). Read
   the time from the injected `TimeProvider`. On overflow, **clear** the map, so a failure here logs
   more, not less. The template is unchanged, so existing greps still match.
3. `Diagnostic` (Warning), `Hint` saturation (Warning) and every admission, ordering or ownership
   path stay **unchanged**.

Projected effect (lines, from the card's window): -87,147 (S1), and about -72,000 (S2) less the
retry residue. One stuck action falls from 360 lines per hour (3 lines x 120 passes) to about 6 per
hour. Remaining volume is about 85,000 lines in 6.63 h, about 12,800 per hour (**-65%**). With Q-1
yes it is about 63,000 lines, about 9,500 per hour (-74%). Byte volume assumes equal line length
per source, which is UNOBSERVED: about 4.5 MB/h (default) or 3.3 MB/h. Either restores the 5-day
fit with 2.6-3.5x headroom. Verify after rollout with the same Group-Object count (Rollout).

### Trade-offs and rejected alternatives

- **Rejected: turning down `Microsoft.AspNetCore` as a whole.** That would also drop
  `Hosting.Lifetime` "Now listening on" and "Application started" lines, which restart diagnosis
  reads. Only the two measured sources are turned down. A guard pins `Hosting.Lifetime`.
- **Rejected: rewriting the coordinator lines in code with a level switch or sampler.** Prefix
  overrides cannot separate the retry line from the routine line inside one source, so S2 needs
  code. But it uses the plain MEL level and stays re-armable with
  `Serilog:MinimumLevel:Override:Antiphon.Server.Application.Services.SessionRuntimeActionCoordinator=Debug`.
  Re-arming needs a server restart because `ReadFrom.Configuration` takes no live level switch. That
  is the same constraint CARD-0043 accepted.
- **Rejected: rate-limiting the retry `Warning`.** The share of the 10,599 WRN lines that come from
  it is UNOBSERVED. A warning is failure evidence, and this card is about Information lines.
- **Rejected: a stateless rate limit (`age mod 600 < 30`).** It can log zero lines in a window when
  passes jitter past 30 s. The bounded map is deterministic under `FakeTimeProvider`.
- **Rejected: updating CARD-0043's measured-rate constants with projected numbers.** Projections are
  not measurements. Rollout gives the post-change rate. A follow-up can pin it as a measured
  constant.
- **Deferred to a separate card (Q-3): the `/api/agent-tasks/{id}` poller, ETag/304 or long-poll.**
  The log has no User-Agent, and finding the poller is an investigation, not a log-level change.

### What must NOT change

- `Microsoft.AspNetCore.Hosting.Diagnostics` stays at Warning (security: query strings).
  `LogRetentionTests.Shipped_config_keeps_hosting_diagnostics_at_Warning_because_it_logs_query_strings`.
- The `"Antiphon": "Information"` override and its six pinned sources
  (`Shipped_config_keeps_delegation_session_and_supervision_at_Information`).
- Source levels live in configuration, never in `Program.cs`
  (`Turning_a_source_down_is_configuration_not_code`).
- No delivery evidence changes. Transcript, UserPrompt receipts and session delivery are untouched,
  and so is every DB write, claim, ordering and ownership path in the coordinator
  (`docs/session-runtime-invariants.md` is not engaged). The removed query is read-only and feeds
  only a log line.
- **Fail-closed:** a wrong answer here could hide a stuck action. A retrying or deferred action
  always gets its first Information line, and another within each 10 minutes it stays stuck.
  Failure Warnings are unchanged. When the map overflows it forgets, which means it logs again.
- `AlertingLogSink` (Warning+ by default) sees exactly the same events.

## RED witnesses

Each witness is red at base, proved on paper from the code at 300c8a99. The Code stage commits each
one failing before its fix (git-common).

| Id | Class / method | Assertion | Why red at base |
|---|---|---|---|
| W-1 | `LogRetentionTests.Shipped_config_drops_per_request_framework_lines_but_keeps_their_warnings` (new) | Logger from `ShippedServerConfiguration()`. Information from `Microsoft.AspNetCore.Routing.EndpointMiddleware` and from `Microsoft.AspNetCore.Http.Result.OkObjectResult` produces **no** event. A Warning from each produces one. | The shipped overrides lack both keys and `Default` is Information (`appsettings.json:449-457`), so both Information events are collected and `ShouldBeEmpty` fails. |
| W-2 | `LogRetentionTests.Shipped_config_request_pipeline_writes_no_framework_Information` (new) | An in-process `TestServer` (`Microsoft.AspNetCore.Mvc.Testing` is referenced at `Antiphon.Tests.csproj:29`). `UseSerilog` reads the shipped config into a collecting sink. One minimal `GET` endpoint returns `TypedResults.Ok(...)`. After one request, no Information event has a SourceContext under `Microsoft.AspNetCore.Routing` or `Microsoft.AspNetCore.Http`. | Same config gap, and it does not rely on guessing the category names: at base, "Executing endpoint" and "Setting HTTP status code" are both Information. This catches a wrong key in W-1. |
| W-3 | `SessionRuntimeActionLogVolumeTests.Routine_phase_and_worker_lines_are_Debug` (new class, `BridgeQueueHarness` with `ConfigureServices` registering a `RecordingLogger<SessionRuntimeActionCoordinator>`) | One phase-0 action drained by `DrainPageAsync`. Every entry whose message starts `Action worker` or `Action ` with `attempts 0` has `LogLevel.Debug`. | `SessionRuntimeActionCoordinator.cs:447,455` call `LogInformation`. |
| W-4 | `SessionRuntimeActionLogVolumeTests.Debt_query_is_skipped_when_Debug_is_off` (new) | Use a test-local logger whose `IsEnabled(Debug)` is false and a `DbCommandInterceptor` command counter (the harness's own `AppDbContext` hook). `DrainPageAsync` on a session with no due action runs **fewer** commands than with Debug on. | At base `ObserveDebtAsync` (`:438-451`) runs the query unconditionally, so the two counts are equal. |
| W-5 | `SessionRuntimeActionLogVolumeTests.Retry_phase_line_is_Information_once_per_interval` (new, `FakeTimeProvider`) | Seed a due action with `Attempts=31`, `FailureReason=Deferred`, phase 8 (it defers through the fake queue). Pass 1: phase line at Information. Advance 31 s and pass 2: Debug. Advance 10 min and pass 3: Information. | At base every pass logs Information, so the pass-2 assertion fails. |
| G-1 | `LogRetentionTests.Shipped_config_keeps_delegation_session_and_supervision_at_Information`, one added `[Arguments("Antiphon.Server.Api.Middleware.CurrentUserMiddleware")]` | Stays Information (D-1). | Guard, green at base. It goes red if a future edit drops NFR11 by accident. Removed if Q-1 = yes. |
| G-2 | `LogRetentionTests.Shipped_config_keeps_host_lifetime_at_Information` (new) | `Microsoft.AspNetCore.Hosting.Lifetime` Information lands. | Guard, green at base. It goes red if someone widens S1 to `Microsoft.AspNetCore`. |
| G-3 | inside W-5 | The failing-phase path still writes `Warning "Action phase retained for retry"` on every attempt. | Guard, green at base. |

W-3, W-4 and W-5 compile at base: they use only the public `DrainPageAsync`, `RecordingLogger` and
harness hooks. The red is an assertion, never a build error. A row with zero tests or a fixture
error is not red.

## Slices

| Slice | Files | Commit order |
|---|---|---|
| S0 | `tests/Antiphon.Tests/Infrastructure/LogRetentionTests.cs` (W-1, W-2, G-1, G-2), `tests/Antiphon.Tests/Application/SessionRuntimeActionLogVolumeTests.cs` (new: W-3, W-4, W-5, G-3) | Committed red first (`test(card-1890): ...`). |
| S1 | `server/appsettings.json`: two override keys | W-1 and W-2 go green. |
| S2 | `server/Application/Services/SessionRuntimeActionCoordinator.cs`: the gate in `ObserveDebtAsync`, the level choice and bounded map in `ObserveAction` | W-3, W-4 and W-5 go green. |
| S3 | `docs/logs.md` desktop-server row: one sentence giving the turned-down sources and the re-arm keys | Docs, no test. |

The new class carries `[Category("Integration")]`, `[Category("Slow")]` and
`[ParallelLimiter<ProcessSpawnLimit>]`, like `SessionRuntimeActionDeferralTests`, since it uses
the same harness and test Postgres.

**Existing tests affected.** No migration, public DTO or route, operation label, remote script
function or statement budget changes. The impacted-classes table (CARD-1782):
`scripts/impacted-classes.ps1 -Path server/appsettings.json,server/Application/Services/SessionRuntimeActionCoordinator.cs,tests/Antiphon.Tests/Infrastructure/LogRetentionTests.cs`
returned **no rows** (`kinds=` empty) at 300c8a99. No existing test asserts the coordinator's
log text: a grep of `tests/` for `Action worker` and `attempts {Attempts}` is empty. The
coordinator classes run as a regression row (CP-3) because S2 edits their subject.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards) and
[test performance standards](../../test-performance-standards.md). This Plan ran no build or test.

- **Selection.** `tests/Antiphon.Tests` only. One build serves three rows. There is no
  downstream project: the server change is config plus one class, and no Pty or E2E surface changes.
- **Historical costs** (`scripts/lib/nightly-class-costs.json`, Windows **serial test-seconds**,
  task 44a6e14e, runs 79b742de and 8f055f87, not p95): SessionRuntimeActionTests 103 s/28,
  Recovery 45 s/17, Routing 115 s/35, Deferral 62 s/12, TemporaryBlock 9 s/4. Sum 334 s/96.
  `LogRetentionTests` is not costed: pure Unit, 21 results today (11 plain methods and 10
  `[Arguments]` cases), expected under 10 s. W-2's in-process TestServer is expected under 5 s.
  These are UNMEASURED and provisional.
- **Phases.** The build is cold in a fresh `bin-c1890/` on loaded server2 (load 21-25 at plan time).
  Budget 10-15 min, with the slot wait reported separately. Each SessionRuntimeAction class sets up
  a per-test schema clone from the shared test database: the assembly owns that setup once, and the
  test owns isolation. Teardown is per-test schema drop. No containers start. Migrations happen once
  per assembly through the fixture.
- **Timeouts.** No new timeout. The new class uses the harness's existing waits. The checkpoint
  tool derives row deadlines as `max(15, 3 x EstimatedMinutes)`. Do not pass `--row-timeout`. Run
  all three rows as **one `--serial` run**. These estimates are provisional, since there is no
  measured p95 for these selections on Linux. Record per-test durations on the first run, and set
  any later timeout near 2 x the measured p95. Any row over 30 minutes gets a Debug task.
- **Recording.** Run receipts under `.antiphon/` (ignored) with per-row TRX.
  `ANTIPHON_LINUX_COMPILER_POLICY=0` is exported before every dotnet command (the policy is 2.2x
  slower). Read `/proc/loadavg` before starting, and start only under 16.

### Checkpoints

Lane: portable Linux server2 (`tests/Antiphon.Tests`, test Postgres). Omit `-Runner` and
`-Platform`. One `--serial` run at the S2 tip (all rows `After` S2; the S0 red is proved per
witness before each fix, by running that row's filter at the red commit through `build-slot.ps1`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | tests/Antiphon.Tests -> bin-c1890/ | log-config | `/*/*/LogRetentionTests/*` | W-1, W-2, G-1, G-2 | all listed, 0 failed | 24 | 15 |
| CP-2 | S2 | CP-1 | coordinator-log-volume | `/*/*/SessionRuntimeActionLogVolumeTests/*` | W-3, W-4, W-5, G-3 | all listed, 0 failed | 3 | 4 |
| CP-3 | S2 | CP-1 | coordinator-regression | `/*/*/(SessionRuntimeActionTests*)\|(SessionRuntimeActionRecoveryTests*)\|(SessionRuntimeActionRoutingTests*)\|(SessionRuntimeActionDeferralTests*)\|(SessionRuntimeActionTemporaryBlockTests*)/*` | S2 regression | all listed, 0 failed | 96 | 12 |

Suffix check: the only classes named `SessionRuntimeAction*` are the five listed and the new
`SessionRuntimeActionLogVolumeTests`. That one does not match any operand, because each operand's
literal stem ends at `Tests`, `RecoveryTests`, and so on, and `SessionRuntimeActionLoadLimit` is a
limiter, not a test class. `LogRetentionTests` is the only `LogRetention*` class. Min counts:
CP-1 = 21 today + W-1 + W-2 + G-2 (3 new methods) + G-1 (1 new `[Arguments]` case) = 25. The table
uses 24 as the floor in case Q-1 removes G-1. CP-2 has 3 methods (G-3 lives inside W-5). CP-3 is
the 96 historical nodes. Code reports the actual TRX counts.

Imported read-only at fc08bfff8 with `dotnet run --project tools/Antiphon.Checkpoints -- import --plan <this plan> --out .antiphon/c1890-checkpoints.yml` (build slot, `ANTIPHON_LINUX_COMPILER_POLICY=0`, load 14.8): **3 rows, 0 import warnings**; derived row timeouts CP-1 45 min, CP-2 15 min, CP-3 36 min.

## Files

Changed: `server/appsettings.json`, `server/Application/Services/SessionRuntimeActionCoordinator.cs`,
`tests/Antiphon.Tests/Infrastructure/LogRetentionTests.cs`,
`tests/Antiphon.Tests/Application/SessionRuntimeActionLogVolumeTests.cs` (new), `docs/logs.md`.

Must NOT touch: `server/Program.cs` (levels stay in configuration), `CurrentUserMiddleware.cs`
(D-1), `AgentSessionRuntime.cs` (CARD-1888 transcript persistence), phone-home files (CARD-1960,
CARD-1964), `scripts/impacted-classes.ps1`, `tests/impacted-classes.json`,
`tests/derived-impact-index.json` (CARD-1820 chain), `src/Antiphon.Observability/LiveTiming.cs`,
`FileLogRetentionPolicy.cs` and every migration.

**Collisions.** None at plan time. The in-flight Antiphon tasks were 7f6ac898 Merge CARD-1957,
aaabade9 Code CARD-1960, cad08e40 Code CARD-1964, a89a352d Review CARD-1820, 5e090a01 Debug
CARD-1514 and ef70b56b Plan CARD-1889. Their branch diffs against `origin/master` touch none of the
files above. `server/appsettings.json` changes often on master (CARD-1886 at 12cd80492, CARD-1794 and
others). Edit only the override block, and re-check `merge-tree` before the land.

## Rollout

- **No migration.** No schema change, so the human-only migration gate is not engaged.
- **Restart needed.** The levels are read at host start. Activation is the next AppHost restart
  (`scripts/restart-apphost.ps1`) or the server2 production server restart after the CARD-1744
  window, from the rc line that carries the change. Product hosts track the rc tip, so the change
  needs a reviewed rc port, not only a master land.
- **Verify after activation** (desktop lane, read-only): the Group-Object script above on one full
  hour, before and after, then `GET /api/version` for the SHA. Expected: no `EndpointMiddleware` or
  `Http.Result` INF lines, coordinator INF lines only for retrying or deferred actions, and the
  file rate under 11.67 MB/h. Also after activation, the `pg_stat_statements` call rate of the
  debt query should fall to about zero (reset or compare deltas).
- **Re-arm for a debugging session:** set
  `Serilog__MinimumLevel__Override__Antiphon.Server.Application.Services.SessionRuntimeActionCoordinator=Debug`
  (or the two Microsoft keys to `Information`) and restart.

## Review and Merge

Plan-review (Grok) next. Then Code runs S0-S3 against the closed CP list. Review round 1 is full.
Rounds 2-3 cover only the diff (CARD-1635). Before land, run `git fetch origin master` and
`git merge-tree --write-tree origin/master HEAD` with no conflicts, and Code and Review run
`scripts/check-evidence-diff.ps1 -Repository . -BaseRef <base> -HeadRef HEAD`. After the land,
port to rc (reviewed) and activate as above. Mutation checks are waived (operator 2026-11-12).

## Decisions (defaults this plan is written under)

- **D-1.** Keep `CurrentUserMiddleware` at Information (NFR11 audit). After S1 it is the only
  per-request line, because LiveTiming does not time inbound requests. Q-1 can reverse this.
- **D-2.** The retry Information interval is 10 minutes per action id, with a bounded map of 1,024
  entries that clears on overflow.
- **D-3.** `Diagnostic` Warnings are not rate-limited.
- **D-4.** The poller and ETag work is out of scope (Q-3).

## Operator decisions (yes/no)

- **Q-1. Turn `CurrentUserMiddleware` down to Debug too (another 9%, about 3,300 lines per hour),
  dropping the NFR11 every-request IP line?** Recommendation: **no**. It is the only remaining
  per-request trace. Mutating calls carry the client IP into audit events through `AuditMiddleware`,
  but GETs have no other trace. If yes: a config key
  `"Antiphon.Server.Api.Middleware.CurrentUserMiddleware": "Warning"`, G-1 is removed, and the NFR11
  text is amended.
- **Q-2. Accept that a deferred or retrying action logs one Information line per 10 minutes instead
  of every 30 s?** Recommendation: **yes**. The first line still appears at once.
- **Q-3. File a separate Investigate card for who polls `GET /api/agent-tasks/{id}` at about 39 per
  minute (per-route counts need the fresh hour above)?** Recommendation: **yes**.
- **Q-4. Re-pin CARD-0043's measured-rate constants from the post-rollout measurement in a
  follow-up?** Recommendation: **yes**. The 2026-08-13 constants hide today's 12.8 MB/h.
