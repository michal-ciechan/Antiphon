# CARD-1890 plan review, round 1

CLEAN. No blocker. Advisories below do not hold the land.

Reviewed source `6c8f1b4f1f085d854d631e5007220e36ff2bdbdf` on `feat/card-task-5db7c600`, plan owner `5db7c600-814a-4ebf-822c-5ad784a8b766`. Base and `origin/master` are `300c8a99d0d267a39791410b7540189ab702f251`. The diff is only `docs/superpowers/plans/2026-11-17-card-1890-server-log-volume-plan.md` (2 commits). Author is Claude Opus. Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending. The card is In Progress, rank 10, four revisions; the plan header's "Backlog, one revision" matches the card at authoring time.

## (1) Findings

The four findings hold against the code at `300c8a99`.

| Claim | Code | Arithmetic |
|---|---|---|
| Five-day retention is already failing | `server/appsettings.json` is the only server appsettings file. Serilog `LogPath=logs`, `FileSizeLimitMb=100`, `RetainedFileCountLimit=14`, `RetainedFileTimeLimitDays=5`, Default Information (`server/appsettings.json:444-457`). `FileLogRetentionPolicy.MaxDiskBytes` is 100 MiB × 14 = 1,400 MiB. `MaxMegabytesPerHourFor(5)` is 1400/120 = 11.666…, and the test asserts 11.666 within 0.01 (`FileLogRetentionPolicy.cs:46-61`, `LogRetentionTests.cs:78`). | 84.7 MB / 6.63 h = 12.78 MB/h. 1400 / 12.8 = 109.4 h = 4.6 days. Constants `MeasuredPostChangeMbPerHour=0.032` and peak 0.92 are the 2026-08-13 file (`LogRetentionTests.cs:40-46`). 12.8/0.032 = 400 and 12.8/0.92 = 13.9, so the plan's 14–400× stands. |
| LiveTiming does not time inbound requests | `LiveTiming` logs `LIVE_TIMING` at Information for startup and `LIVE_SLOW_CALL` at Warning only when an outbound call exceeds 1000 ms (`src/Antiphon.Observability/LiveTiming.cs:105-109`). | The card sentence "request timing is already in LiveTiming" does not match this code. |
| The debt query fills one log line | `ObserveDebtAsync` (`SessionRuntimeActionCoordinator.cs:438-451`) runs `SessionRuntimeActions.Where(session).GroupBy.Select(Count, Max Sequence)` and passes `Count` and `Frontier` only into the Information worker line. The catch swallows. Callers are the start and end of `DrainPageAsync` (lines 146 and 148). | The 1,466,281 count is the plan's cited desktop read. This review did not re-query the desktop database. The code uses the result for nothing else. |
| No shipper; the console sink duplicates | `Program.cs:102-105` writes Console at Verbose unless `Serilog:ConsoleMinimumLevel` is set, and the file sink is beside it (lines 106-119). No Promtail, Loki, Filebeat, Fluent Bit, Vector, or Seq config is in `server/`, `src/`, `scripts/`, the compose files, `docs/logs.md`, or `docs/docker-stack.md`. | Desktop console goes to the AppHost and Aspire dashboard. Production server stdout is `docker logs` (`docs/logs.md:92-94`). Line 84 records that Docker logging-driver rotation is host policy and has not been established here. |

## (2) Design safety

**S1.** The two override keys are the real source contexts on the 9.0.23 shared framework this host runs.

- `Microsoft.AspNetCore.Routing.dll` contains the Information templates "Executing endpoint" and "Executed endpoint". `EndpointMiddleware` is `ILogger<EndpointMiddleware>` in namespace `Microsoft.AspNetCore.Routing`, so the source context is `Microsoft.AspNetCore.Routing.EndpointMiddleware`. The Information log is skipped when Information is disabled.
- `Microsoft.AspNetCore.Http.Results.dll` contains the category literal `Microsoft.AspNetCore.Http.Result.OkObjectResult` (and the sibling result types) and the Information template "Setting HTTP status code {StatusCode}". Serilog's longest-prefix match makes `Microsoft.AspNetCore.Http.Result` cover those categories.
- `UseSerilog` (`Program.cs:81`) replaces the MEL factory, so `Logging:LogLevel:Microsoft.AspNetCore=Warning` (`appsettings.json:459-463`) does not quiet these lines. The shipped Serilog overrides are Antiphon, Hosting.Diagnostics, EF Command, and HttpClient.

Warning and Error stay visible. W-1's synthetic Warning from each source still passes a Warning override. Thrown failures stay on `ExceptionMiddleware`: status ≥ 500 is Error, any other thrown status is Warning, both with `TraceId` (`ExceptionMiddleware.cs:46-52`). `AlertingLogSink` forwards Warning and above (`AlertingLogSink.cs:34`), so moving Information lines does not change what it sees.

No test, script, or log recipe depends on those Information lines. A search of `tests/` and `scripts/` for "Executing endpoint", "Setting HTTP status code", `OkObjectResult`, and `EndpointMiddleware` hits only historical docs and this plan. `tests/` has no match for `Action worker` or `attempts {Attempts}`. `docs/logs.md:19-27` (`landing_concurrency_conflict`) names task, request, and diagnostic ID on the land event. It does not name `RequestId` and does not grep EndpointMiddleware. `CorrelationIdMiddleware` runs before `CurrentUserMiddleware` (`Program.cs:1088-1089`), so the client-IP line keeps `CorrelationId`.

**S2.** `ObserveAction` runs at line 185, before `ExecuteAsync` increments `Attempts`. A fresh deferral therefore has `Attempts == 0` on that pass and is Debug under S2. `ExecuteAsync` then sets `FailureReason = "Deferred"`, increments attempts, and sets `DueAt = RetryAt` (`:298-306`) with no `Diagnostic` call. `RetryAt(1)` is 1 second and the cap is 30 seconds (`:230-231`). The first Information line is the next drain, about one second later, because that pass has `Attempts > 0`. After that, the map emits Information on the first drain at or after 10 minutes. The action is visited at most every 30 seconds, so the gap is at most 10 minutes plus one retry interval. Clearing the 1,024-entry map on overflow logs more. The exception path still calls `Diagnostic` on every attempt (line 201), and that Warning is unchanged. For a deferred action the phase Information line is the only log evidence it is stuck. `CanWriteResumeInputAsync`, `OnRuntimeActionAsync`, and `OnTurnEndCoreAsync` do not log a Warning on the defer return. The throttle cannot hide a stuck action past that bound.

## (3) Witnesses and the checkpoint table

Each witness is red, or green for a guard, at base for the reason the plan names.

| Id | At base | Why |
|---|---|---|
| W-1 | Red | Shipped overrides lack both keys and Default is Information, so both Information events are collected and `ShouldBeEmpty` fails. A synthetic Warning still passes once that assertion is past. |
| W-2 | Red | "Executing endpoint" and "Setting HTTP status code" are Information. A minimal `TypedResults.Ok` GET emits those under `Microsoft.AspNetCore.Routing` and `Microsoft.AspNetCore.Http`. `Microsoft.AspNetCore.Mvc.Testing` 9.0.21 is referenced at `Antiphon.Tests.csproj:29`. |
| W-3 | Red | Lines 447 and 455 are `LogInformation`. |
| W-4 | Red | `ObserveDebtAsync` runs the query with no level gate, so a Debug-off count equals a Debug-on count. The shared `RecordingLogger<T>.IsEnabled` always returns true (`tests/Antiphon.Tests/TestHelpers/RecordingLogger.cs:15`); W-4 needs the test-local logger the plan already names. |
| W-5 | Red | Every pass logs Information, so the pass-2 Debug assertion fails. The seed has to be due, phase 8 (`QueuePhase = 8`), `FailureReason = Deferred`, and the queue has to defer. Existing deferral fixtures do that. |
| G-1 | Green | The `Antiphon` override already keeps `Antiphon.Server.Api.Middleware.CurrentUserMiddleware` at Information. A more specific Warning override would turn it red. Q-1 is no, so G-1 stays. |
| G-2 | Green | Default is Information, so a synthetic event at the named source is collected. See the advisory: the real lifetime category is `Microsoft.Hosting.Lifetime`. |
| G-3 | Green on the throwing path | The Warning at line 201 fires on every caught attempt today and S2 leaves it alone. The deferred seed in W-5 does not enter that catch. The same method needs a separate throwing pass. |

`LogRetentionTests` has 13 `[Test]` methods, two of them expanded by 4 and 6 `[Arguments]`, which is 21 executed results. W-1, W-2, and G-2 add three methods and G-1 adds one arguments case: 25. CP-2's three methods are W-3, W-4, and W-5, with G-3 inside W-5. CP-3's 96 is the historical node count of the five named classes. The suffix claim holds: `SessionRuntimeActionLogVolumeTests` matches none of the CP-3 operands, `SessionRuntimeActionLoadLimit` is a limiter, and `LogRetentionTests` is the only `LogRetention*` class. `DrainPageAsync` is internal (`:144`) and `InternalsVisibleTo` includes `Antiphon.Tests` (`Antiphon.Server.csproj:13`), so the new class compiles against it.

The table is one build, `bin-c1890/` with a trailing slash, CP-2 and CP-3 reuse CP-1, one filter per row, and ids `CP-1`..`CP-3`. Derived timeouts are `max(15, 3 × EstimatedMinutes)`: 45, 15, and 36 minutes.

Re-import at this review, read-only, into ignored `.antiphon/c1890-review-32a9a06f.yml`: **3 rows, importer line `imported 3 rows`, no importer warning**. Timeouts 45 / 15 / 36, minExecuted 24 / 3 / 96, filters and expect classes match the plan. `serial: false` is the importer default; the plan's "one `--serial` run" is prose, not a table column. Lease `97303347-ef46-430a-b60c-42338c723583`, held 9s, released. The compiler warnings in that build are existing checkpoint-tool warnings. The 1-minute load at start was 28.41, above the start gate of 16. The import had already finished; it was not re-run. The result is a manifest parse.

## (4) Collisions

The plan's changed files are `server/appsettings.json`, `SessionRuntimeActionCoordinator.cs`, `LogRetentionTests.cs`, the new `SessionRuntimeActionLogVolumeTests.cs`, and `docs/logs.md`. None of those paths are in the current branch diffs against `300c8a99`:

| Card | Tip | Overlap with the plan's files |
|---|---|---|
| CARD-1957 `7f6ac898` | `300c8a99` (empty) | none |
| CARD-1960 `aaabade9` | `5d0ac136` | none |
| CARD-1964 `cad08e40` | `4cfec30c` | none of the five; it does edit `tests/derived-impact-index.json` |
| CARD-1820 `a89a352d` | `e6481c88` (moved since the plan named `822d7dd6`) | none of the five; it also edits `tests/derived-impact-index.json` |
| CARD-1514 `5e090a01` | `300c8a99` (empty) | none |
| CARD-1889 `ef70b56b` | `95e6a1b5` | none |

`impacted-classes.ps1` on `300c8a99..HEAD` returned empty kinds, classes, and files. Downgrade selector count is 0. No migration. No input-wait change, so CARD-1083 parking does not apply.

## (5) Operator decisions

- **Q-1. No.** Keep `CurrentUserMiddleware` at Information. It is NFR11 ("Client IP is logged on every API request for audit trail", `_bmad-output/planning-artifacts/prd.md:597`; `CurrentUserMiddleware.cs:8,32`). After S1 it is the only per-request line.
- **Q-2. Yes.** One Information line per 10 minutes for a retrying or deferred action, with the first line on the next drain and the Warning path left on every attempt.
- **Q-3. Yes.** File a separate card for who calls `GET /api/agent-tasks/{id}`. `CurrentUserMiddleware` wrote 22,008 lines, one per request of every route. EndpointMiddleware wrote 44,019, which is two per request plus three. The measured 109,155 is those two sources plus 43,128 Ok lines. 39,000 is not a request count.
- **Q-4. Yes.** Re-pin the 2026-08-13 rate constants from the post-rollout measurement. A projection is not that measurement.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `bbdaa3078d56ec792f4d940b1b47c7f0b671976f`, no conflicts. `origin/master` fetched and still `300c8a99` |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=2, entries=0, violations=0 |
| `scripts/impacted-classes.ps1 -Range` | kinds, classes, and files empty; downgrade count 0 |
| `scripts/check-os-witnesses.ps1` | exit 0. The only changed path is the plan markdown. Empty inventory, restart required false |
| Checkpoint import | 3 rows, 0 importer warnings, timeouts 45/15/36. Started at load 28.41 |

CP-1 through CP-3 name tests this tree does not contain, so they were imported and not executed. The subject changes no code or tests, so the guard batch was not re-run. `reviewedSourceClean` stays false: there is no checkpoint-tool test receipt. Producer/recipient delivery does not apply.

## Advisories

- **G-2 names `Microsoft.AspNetCore.Hosting.Lifetime`.** `Microsoft.AspNetCore.Hosting.dll` 9.0.23 contains `Microsoft.Hosting.Lifetime`, "Now listening on", and "Application started", and does not contain `Microsoft.AspNetCore.Hosting.Lifetime`. The fake gateway already pins the real key (`src/Antiphon.Messaging.FakeGateway/appsettings.json`). A `Microsoft.AspNetCore` override does not drop the real lifetime lines. Aim G-2 at `Microsoft.Hosting.Lifetime`.
- **4xx status lines share the result category.** "Setting HTTP status code {StatusCode}" is Information for 200 and for 4xx `IResult`s. The prefix drops both. Thrown 4xx and 5xx stay on `ExceptionMiddleware`. W-1's synthetic Warning shows the override still lets Warning through; it does not show the framework emitting 4xx at Warning.
- **CP-1 Min 24** is one under the 25-case count that Q-1 = no produces. The plan says 24 in case Q-1 removes G-1. Q-1 is no, so the expected count is 25. The floor still passes at 25.
- **G-3 needs a throwing pass.** A deferred action returns at line 306 and does not call `Diagnostic`. Assert the retry Warning on a pass that hits the catch at line 201.
- **Fresh deferral.** The first `ObserveAction` sees `Attempts == 0` and is Debug. The first Information line is the following drain (about 1 second). Later gaps are at most 10 minutes plus one 30-second retry.
- **Derived-impact index.** `SessionRuntimeActionCoordinator.cs` already lists exactly 25 classes (`tests/derived-impact-index.json`), and `DerivedImpactIndexBuilder.Cap` is 25. A new test class that names the coordinator can displace one entry and change the committed bytes. The plan forbids editing that file. Regenerate it only when the builder output changes, then re-check `merge-tree`: CARD-1820 and CARD-1964 already edit it.
- **Import start gate.** This review's import started at 1-minute load 28.41. It completed and was not repeated.
- **`serial: false` in the imported manifest.** Code still runs the three rows as the plan's one `--serial` run.
