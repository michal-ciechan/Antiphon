# CARD-0420 investigation

Confirmed a real restart-budget mismatch: the reported restart reached listening about 99 seconds after the previous process exited, with successful build and adoption of 41 surviving sessions. Plan is warranted; no fix is proposed or implemented here.

Inspected checkout: `5d96d8cbf3f0974d2af903ed28612f9f52d686f6`. Investigation performed read-only against the running stack on 2026-09-06. No restart, build, test, or session mutation was performed.

## Observed timing

Primary evidence is `logs/session-runner.log`, lines 80356-80511 (comparison) and 80628-80783 (reported incident). Its supervisor and console timestamps have whole-second precision and omit dates. The corresponding dated Serilog file, `%TEMP%\antiphon-logs\session-runner-20260906.log`, confirms both runs occurred on September 6, local time UTC+01:00, and supplies milliseconds for application events.

| Milestone | Comparison restart | Reported incident |
|---|---|---|
| Previous process exit | Not recorded in adjacent excerpt | 23:08:00 |
| Supervisor says restarting in 3 seconds | 09:46:22 | 23:08:05 |
| Build begins | 09:46:25 | 23:08:09 |
| Build succeeds / launch requested | 09:46:42 | 23:09:01 |
| MSBuild elapsed | 16.23 s | 50.35 s |
| Supervisor build wall interval | about 17 s | about 52 s |
| PTY backend startup log | 09:46:43.262 | 23:09:04.557 |
| Transcript claims restored | 09:46:57.907; 749 claims | 23:09:15.877; 737 claims |
| First successful individual adoption (console) | 09:46:58 | 23:09:16 |
| Adoption summary | 09:47:01.353; 42 sessions | 23:09:39.102; 41 sessions |
| Listening | 09:47:01.541 | 23:09:39.252 |

Derived intervals:

- Launch-request log to listening: about 19.5 s versus 38.3 s. These are launch-log proxies, not instrumented OS process-start times.
- PTY backend log to restored-claims summary: 14.645 s versus 11.320 s. This includes runtime resolution and restoration; there is no exact claim-restoration start marker.
- Restored-claims summary to adoption summary: 3.446 s versus 23.225 s. This brackets the subsequent adoption work (including any Herdr pass and manifest enumeration), not isolated pipe connection time.
- Adoption summary to listening: 0.188 s versus 0.150 s.
- Build-begin to listening: about 36.5 s versus 90.3 s. Restart-delay announcement to listening: about 39.5 s versus 94.3 s. The incident's recorded old-process exit to listening is about 99.3 s.

The incident spends time in both build and application startup. Near-identical session counts produced very different adoption intervals, so count alone is not an established predictor. Logs do not separate replay CPU, file I/O, pipe waits, machine contention, or rules-receipt verification costs.

Neither console nor dated application log records successful `/health` requests. Therefore listening-to-first-200 and exact restart-script deadline-to-first-200 cannot be reconstructed. The card's two connection refusals followed by 200 are operator evidence without timestamps. A fresh read-only probe returned HTTP 200, body `Healthy`, observed at 2026-09-06T23:17:35.5506194+01:00; that proves current health only.

## Startup and readiness contract

`src/Antiphon.SessionRunner/Program.cs:135-147` awaits `AdoptOrphanedHostsAsync` before mapping health and starting HTTP. `/health` cannot respond during adoption; adoption finishes before listening, rather than continuing after listening.

`SessionRunnerRuntime.cs:730` first restores transcript claims, then awaits Herdr adoption, then iterates pty-host manifests serially, awaiting each `RunnerSession.AdoptAsync`. The latter (`:1769`) verifies any Grok rules receipt, connects to the host, requests status, rebuilds terminal interpretation from the bounded ANSI tail, attaches with possible resync/replay retries, publishes adoption, and restores transcript tailing. Connection has a five-second budget; status and attach request waits each use a 30-second reply timeout (`PtyHostClient.cs:136`). There is no overall adoption deadline in Program, which passes `CancellationToken.None`.

The HTTP gate is deliberate session protection: `server/Application/Services/SessionReconciliationService.cs:134` skips reconciliation when the runner is unreachable, whereas its unknown-session branch around `:180` marks a DB-live session Failed. Serving a partial session list would violate this contract. Parallel or background adoption is not currently implemented, and its safety/performance is not established by these logs. Early health and complete session readiness cannot simply be assumed equivalent.

## Build and timeout behavior

`scripts/autostart-session-runner.ps1` always supplies `-BuildProjectDir`. `scripts/run-daemon.ps1:118-132` unconditionally invokes `dotnet build <runnerDir> -c Debug --nologo` on each launch, captures output until completion, appends it, and then starts the built executable. Build failure retries after five seconds. This is an incremental build invocation, not an unconditional clean/rebuild, but there is no supervisor-level unchanged-input skip or no-build restart option.

Unchanged SessionRunner source alone does not establish unchanged build inputs: the project references Pty, Contracts, PtyHost, and PtyHost.Client; Protocol is transitive. Repository-wide props/targets, packages, copied content, and the git SHA stamped by `Directory.Build.props:32` also matter. The incident restored four of six projects and reports zero warnings/errors. Without a build trace, the remaining build time cannot be assigned to compilation, evaluation, copying, or contention, and a safe skip policy or its savings has not been demonstrated.

`scripts/restart-session-runner.ps1:35` defaults to 60 seconds. Its deadline starts at line 121, after service killing and relaunch selection; it is not an overall command deadline. Each loop sleeps two seconds, checks the port, then makes a five-second HTTP request. In-flight checks can extend beyond the nominal deadline. On expiry it prints the red timeout message and log-tail suggestion and exits 1. It neither cancels the supervisor nor distinguishes building, adopting, continuing startup, or actual failure. Its `~3s` relaunch text describes the supervisor delay while omitting the build and adoption duration. The log does not timestamp the script's own polling start or exit.

## Disposition

A real fix is warranted. Choosing script/reporting scope versus startup optimization belongs to Plan. Evidence establishes the insufficient default for this incident and the required readiness barrier, but does not establish that parallelization or build skipping would reliably meet 60 seconds. Remaining measurement gaps are first-health timing, per-phase adoption costs, and build target costs. No implementation decision is required to complete this investigation.

--- next stage ---
next: plan
handoff: Plan CARD-0420 using the measured 90.3-second build-to-listen incident and 36.5-second comparison; preserve complete-session readiness, account for variable build and adoption costs, and distinguish observed timeout from process failure. First-health and detailed phase timings remain unmeasured; no fix has been selected.
artifact: docs/investigations/2026-09-06-card-0420-runner-restart-timeout.md
