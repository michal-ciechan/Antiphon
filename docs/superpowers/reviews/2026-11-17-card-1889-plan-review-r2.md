# CARD-1889 host-stats gap plan review, round 2

CLEAN. The diff `95e6a1b5ce609affc4b4d6dcc5f577c5dc72b5db..83686e7822e891fff4bc3170676b047e4f34b822` is the plan file only (75 insertions, 23 deletions). B-1 through B-4 are closed. A-1 through A-6 are in the amendment. Q-1 stays an operator decision after land. One advisory below does not hold the land.

Reviewed source `83686e7822e891fff4bc3170676b047e4f34b822` on `feat/card-task-4f978baa`. Plan owner `4f978baa-43f5-47fc-94c3-1d6c8e30aae5` (Claude Opus). Reviewer is Grok, task `43193a26-9302-4a55-ba07-1c8b02a1efa1`. Previous reviewed SHA `95e6a1b5ce609affc4b4d6dcc5f577c5dc72b5db`. Round-1 review is `origin/feat/card-task-52aeb04f:docs/superpowers/reviews/2026-11-17-card-1889-plan-review.md`. Current `origin/master` is `300c8a99d0d267a39791410b7540189ab702f251`. Mutation stays waived (operator 2026-11-12). Positive controls stay pending.

## B-1 accepted

The runner half is loaded from the main-checkout project directory, so the missing copy-to-output rule is not a miss.

`WebApplication.CreateBuilder` (`src/Antiphon.SessionRunner/Program.cs:12`) uses the .NET 9 default host. `HostApplicationBuilder` calls `HostingHostBuilderExtensions.SetDefaultContentRoot`, which sets the content root to the process current directory when that directory is not `C:\Windows\system32`. The default app configuration then adds optional `appsettings.json` and `appsettings.{EnvironmentName}.json` from that content root, then environment variables, then the command line. The runner csproj is `Microsoft.NET.Sdk.Web`, has no `UserSecretsId`, and has no `appsettings` content item. SDK 10 Web targets do not copy `appsettings.*.json` either. `.gitignore:5` is `appsettings.*.json`. Gitignore does not hide a file from the physical file provider, and a rebuild does not delete a gitignored file that sits in the project directory.

The desktop runner is the built exe, not `dotnet run`. `Antiphon.AppHost/Program.cs:32-38` starts `src/Antiphon.SessionRunner/bin/Debug/net9.0/Antiphon.SessionRunner.exe` with working directory `src/Antiphon.SessionRunner` of the main checkout. `dev-aspire.ps1:140` runs `dotnet run --no-build` of the AppHost after the slot-leased build. The only launch profile sets `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT` to `Development` (`Antiphon.AppHost/Properties/launchSettings.json`). The supervisor starts with `UseShellExecute=true` (`DaemonProcessService.cs:104`), so it inherits that environment, and `run-daemon.ps1:163-166` launches the exe with `UseShellExecute=false` and `WorkingDirectory` equal to the project directory. A `-Hard` restart, or a restart with no live supervisor, starts the `Antiphon Session Runner` Scheduled Task (`session-runner-restart-health.ps1:122,230-234`). That task does not set the environment name, so ASP.NET Core uses `Production`. The plan writes the same `PhysicalHostKey`, `BindingRevision`, and `SourceEpoch` into both gitignored files. Whichever launcher owns today's supervisor, one of those files is the file the host loads. A soft `restart-session-runner.ps1` starts a new exe in the same working directory, so the new process reads the files. It does not pick up a user or machine variable set after the supervisor started.

`SessionRunner__HostStats__*` environment variables are the wrong source. They override JSON, and a user or machine value would be inherited by desktop test hosts and the isolated E2E runner. The plan states the values and does not write them.

The correction paragraph matches the ingest code. `HostLoadIngestService.IngestAsync` inserts every valid sample (`HostLoadIngestService.cs:50-57`). `Binding` returns a row only when the canonical runner, window, `PhysicalHostKey`, revision, and `SourceEpoch` all match (`:76-82`). Otherwise the stored `PhysicalHostKey` and `SourceEpoch` are null and `IsPrimary` is false. History reads filter `PhysicalHostKey == binding.PhysicalHostKey` and, for raw samples, `IsPrimary` (`HostLoadHistoryService.cs:29-32,81-82,95-96`). A null key never matches. Nothing updates `PhysicalHostKey` after insert. Tracked `appsettings.json` sets `IntervalMs` 5000 and does not set `PhysicalHostKey`. `HostLoadCounterCache.Canonical` maps `local` and `desktop` to `desktop`.

R-1 through R-4 are observable. `GET /api/hosts/desktop/load` returns `physicalHostKey` from the server binding (`HostLoadCurrentDto.PhysicalHostKey`, `HostLoadHistoryService.cs:27-28`), which is R-1. `current` is the decoded primary sample and `currentAgeSeconds` is its age (`HostLoadDtos.cs:30-31`). `Decode` copies the stored key onto the sample (`HostLoadHistoryService.cs:181-182`), so a non-null `current.physicalHostKey` of `desktop` is R-2 and means the sample matched the server row. With a binding and at least one item, history `gaps` is `["partial"]` or `[]`, and `["binding-unknown"]` is returned only when no single binding covers now (`:72,114`). That is R-3. The ingest cursor is stored under the canonical runner id (`HostLoadIngestService.cs:28,39`), so `"RunnerId"='desktop'` is the desktop row for R-4. `PollIntervalMs` must be 5000 (`HostLoadHistorySettings.Validate`).

## B-2 accepted

`HostTopConsumerTests` has 33 `[Test]` methods. The only `Skip.Test` is `Production_factory_observes_this_process_on_windows` (`:52-57`). CP-1's parenthesized method prefixes match the other 32 names and do not match that method. `Production_factory_selects*` does not match `Production_factory_observes_this_process_on_windows`. The two `Windows_*` methods are selected and do not skip on Linux. The import kept the filter, `minExecuted: 32`, and `>= 32 executed, 0 failed, 0 skipped`.

`C1514HostLoadSampleTests` has 6 `[Test]` methods and no skip. `HostStatsRoleProbeTests` is not in the tree. No class under `tests/` starts with `HostStatsRole`. CP-2 reuses build `bin-c1889-role` in the same `After` group, and the import set its minimum to 9 (3 new + 6 existing).

## B-3 accepted

V-5 asserts `SessionRuntimeActionClaimQuery.Query(...).CreateDbCommand().CommandText` and `DbCommandMetrics.LeadingTag`. The cited test at `RuntimeActionClaimQueryTests.cs:21-28` builds that command on an unused-host `AppDbContext` and binds `sessionId`, `now`, `queuePhase`, and `channelKind`. `LeadingTag` returns null unless the text starts with `-- ` (`DbCommandMetrics.cs:36-44`), so `ToQueryString()` would hide the tag behind parameter comments. CP-4 is `HostStatsPollServiceTests` (10 + 1), `DbCommandMetricsTests` (2 + 1), and `RuntimeActionClaimQueryTests` (3). Those classes have 10, 2, and 3 `[Test]` methods today and no skip. The import set CP-4 `minExecuted` to 17.

## B-4 accepted

The sampling cadence is 5000 ms. `HostStatsSettings.IntervalMs` defaults to 5000, tracked runner `appsettings.json` sets 5000, and `HostLoadHistorySettings.Validate` refuses any `PollIntervalMs` other than 5000. D-4 sets `PostgresReadTimeout` to 2 × the measured p95, rounded up to 100 ms, and stops the slice when that value is 5000 ms or more. The staleness bound in the same decision is two cadences (10 s), and the read reschedules one cadence after completion, so a timeout of one full cadence would consume the bound. S0 is twelve desktop `docker stats --no-stream` calls at least 5 s apart, and with twelve samples p95 is the maximum. The timeout stays UNOBSERVED until that measurement. The plan does not raise the cadence to make a slow reading fit.

## Advisories applied

A-1: CP-2 counts `C1514HostLoadSampleTests` as 6. A-2: one allowlist moves into `Antiphon.Observability`, and `DbCommandMetrics.LeadingTag` calls it. The server already references that project (`server/Antiphon.Server.csproj:90`). A-3 and A-4: a truncated scan publishes no `build`, `test`, or `server` row, and the desktop server basename is a Code check. A-5 is noted on Q-2. A-6: S0 records `docker` `TotalProcessorTime` per call. PC-5 is the truncated-scan mutant on the V-3 method.

Q-1 is pending operator, not a default yes. Q-2 through Q-6 are yes.

## Advisory, not a land hold

D-1 Values says ingest stores a sample only when `SampledAt >= From`. Insert does not have that condition. `SampledAt >= From` is one of the match terms in `Binding` (`HostLoadIngestService.cs:78-81`). A valid sample taken before `From` is stored with a null key and `IsPrimary` false, and history never reads it. The amendment's correction paragraph already states that model. The outcome for the empty window is the same. This does not send the plan back to Code.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `e8d94b299bb31ec2a45ecb029651035b515b15c2`, exit 0 |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=1, entries=0, violations=0, exit 0 |
| `scripts/impacted-classes.ps1 -Range` | kinds, classes, files, derived classes, and filters empty; downgrade selector count 0 (no `server/Migrations/**`, `AppDbContext.cs`, or `*Model.cs` in the range) |
| Checkpoint import, read-only | `imported 4 rows` to gitignored `.antiphon/c1889-plan-review-r2.yml`. CP-1..CP-4 timeouts 45/15/24/45, `explicitTotalMinutes` 90, `minExecuted` 32/9/5/17. CP-2 reuses `bin-c1889-role`. No importer warning. Load 13.63 before start. Slot `9ad829c0-00f7-468f-9ba0-71e222f3ed72`, waited 0s, held 11s, exit 0 |

The import is a build-slot lease of `dotnet run --project tools/Antiphon.Checkpoints -- import`, not a checkpoint-run receipt, so `reviewedSourceClean` stays false. The diff changes no code or test, so the guard batch and the plan's test rows were not run. No OS-selected production path changed. Producer/recipient delivery and input-wait release do not apply. Parking is not applicable: the plan adds no session that waits for input. The row deadlines are the tool's `max(15, 3 × EstimatedMinutes)` provisional budgets. The plan already says to file a Debug task if a row, including slot wait, passes 30 minutes.
