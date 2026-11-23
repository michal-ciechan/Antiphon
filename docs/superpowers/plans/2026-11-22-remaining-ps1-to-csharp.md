# Remaining PowerShell to C#: inventory and order (2026-11-22)

**Owner direction, 2026-11-22:** "get rid of more .ps1, what is left, make a note, then carry on removing 1 by 1,
migrating to c#. Function calls, as well as top level, so hopefully we will just call the csharplib once, and maybe
even in future as part of sessionrunner / remote host etc."

This note lists every tracked `.ps1` that is left and the order in which they move to C#. It follows on from the
W2-nightly plan (`2026-11-21-w2-nightly-port-slices.md`), which moved the nightly run, its tests, health, report,
seeds, timing and live-lane manifest into `scripts/lib/Antiphon.Nightly` and the `antiphon-scripts` CLI.

## Where we are

Snapshot: `wip/next` after W2-nightly S27 (`782034998`). There are **356 tracked `.ps1` files, 93,083 lines**.

| Group | Files | Lines |
|---|---:|---:|
| `scripts/*.ps1` entry scripts | 134 | 39,386 |
| `scripts/test-*.ps1` test drivers | 106 | 37,299 |
| `scripts/lib/*.ps1` libraries | 43 | 9,784 |
| `scripts/fixtures` | 25 | 2,938 |
| `scripts/qualification` | 7 | 829 |
| `scripts/windmill` | 3 | 442 |
| `scripts/orchestration` | 4 | 262 |
| Repository root (`dev-*.ps1`, `restart*.ps1` and others) | 20 | 992 |
| `tests/**` fixtures and helpers | 9 | 935 |
| `docs/investigations`, `.antiphon` evidence | 5 | 216 |

Two of the drivers are in-process twins of C# that already exists: `ToolchainFloorTwinTests` and
`TestTimingsTwinTests` compare `lib/toolchain-floor.ps1` and `lib/test-timings.ps1` with `ToolchainFloor.cs` and
`TestTimings.cs`. Those two libraries are the first to go.

## End state

1. **One library and one CLI.**
   - The compiled core (`scripts/lib/Antiphon.Nightly`, to be renamed when it stops being only the nightly) holds
     the behaviour.
   - `antiphon-scripts` (`scripts/lib/Antiphon.Scripts`) is the only executable, and its command table names every
     command. Run it as `scripts/antiphon <command>` (sh) or `scripts\antiphon.cmd <command>`.
2. **Top level.** Every entry script becomes a CLI command, and the `.ps1` is deleted. Docs, `CLAUDE.md`, skills and
   agent prompts that name the script move to the command in the same change.
3. **Function calls.** Each library function moves into the core, and callers call the C# directly, with no wrapper
   functions (steer of 2026-11-19, "One C# call, not a wrapper library").
   - A `.ps1` caller that is not ported yet calls the CLI, or loads the core once.
   - A ported function never gets a PowerShell twin back.
4. **Hosting in-process (SessionRunner, remote host).**
   - A command is a class with typed options and one `Run(options, host)` entry.
   - Everything outside the process goes through the host seams already in the core: `NightlyHost` (process
     launcher, git, clock and process table), `IFileSystem`, and the evidence writer.
   - A command never writes to `Console`, calls `Environment.Exit` or reads ambient environment variables directly.
     The CLI is one host: it parses the arguments, binds the production host and maps the result to an exit code.
   - SessionRunner or a remote host can bind its own host and run the same command without starting a process.
5. **External registrations.** A script that Windmill or a desktop Scheduled Task starts by path keeps a thin
   launcher until that registration changes. The model is `nightly-run.ps1` and `nightly-health.ps1`: the
   parameters are kept, the work is passed to the CLI, and there are no dot-sources. Windmill is left alone (steer
   of 2026-11-20). In the appendix, `Windmill?` marks files whose name appears next to Windmill text; check the job
   definition before porting one of them.
6. **Tests.** A driver `scripts/test-<x>.ps1` is deleted together with its subject, and its cases become in-process
   C# tests. No test launches a `.ps1` (steers of 2026-11-19 and 2026-11-20).

## How each script moves

The unit is one script, or one library together with its only caller. For each unit:

1. Port the behaviour to plain C# in the core, and add the CLI command if it is an entry point.
2. Re-point every caller: `.ps1` callers, C# tests, docs, agent instructions and Windmill launchers (launchers only,
   never the job).
3. Delete the `.ps1` and its driver, and move the driver's cases in-process. A case that only checked PowerShell
   mechanics is dropped, with the reason in the commit.
4. Upkeep, when the change calls for it:
   - re-record the land graph for project-file changes;
   - regenerate `tests/derived-impact-index.json` for added or deleted files;
   - reseal the policy hashes if a policy lists the file;
   - re-pin the live lane if a protected input changes;
   - update the rc input census owners.
5. Build and run the affected classes, one at a time, then commit.

## Order

Leaves first, so that every deleted library's callers are either ported already or call the CLI.

| Step | Wave | Files | Lines | Why here |
|---|---|---:|---:|---|
| 1 | W3 test selection, timings, toolchain | 11 | 2,821 | `toolchain-floor.ps1` and `test-timings.ps1` already have C# twins. The rest of selection (`test-selection.ps1`, `impacted-classes.ps1`, `select-test-rosters.ps1`) feeds the nightly core that is now C#. |
| 2 | W1 release and RC qualification | 54 | 17,692 | The W2-rc PowerShell: release gate, selection, cut, publish, promote and the qualification supervisor (the supervisor set, the rc status and the stop moved in unit 7). When it goes, `nightly-common.ps1` and `nightly-core.ps1` go with it, and the PSObject overloads (S28) can go. |
| 3 | W4 land graph and audit | 6 | 831 | Small, and already CLI-adjacent. |
| 4 | W2 live lane | 18 | 3,545 | The contract and receipts are owner-gated (S21 is open), so this waits for that approval. |
| 5 | W5 checkpoint | 28 | 7,153 | `run-checkpoint.ps1` and the `checkpoint-*` libraries. |
| 6 | W6 host, runner and daemon operations | 63 | 15,425 | AppHost, SessionRunner restart and health, slots, reapers. These are the first candidates for in-process hosting by SessionRunner. |
| 7 | W7 board and agent tools | 28 | 6,603 | `card.ps1`, `delegate.ps1` and the other API wrappers. Agent instructions name them, so the docs move with each one. |
| 8 | W8 deploy, Postgres and infrastructure | 44 | 23,593 | Outward-facing: confirm each changed deploy path with the owner. |
| 9 | W9 dev stack and client | 30 | 2,163 | Repository-root `dev-*.ps1`, client lint, typecheck and mode. |
| 10 | W10 one-offs, evidence and fixtures | 74 | 13,257 | Decide per file whether to port or delete (see below). |

## Unreferenced scripts

65 files (12,766 lines) are not named by any code or configuration (`.ps1`, `.cs`, `.json`, `.sh`, `.yml`,
`.cmd`, `.ts`, project files); only card docs mention them. They are manual witnesses, one-off experiments and old
evidence harnesses, such as `run-card-1885-bounded-experiment.ps1`, `cleanup-gym-stat-one-off-agents.ps1` and
`test-apphost-server-version.ps1`. Nothing runs them, so porting them buys nothing unless someone still uses them.
**Proposal:** delete those whose card is closed, in batches the owner confirms. Port only the ones that are still
used. The appendix marks them `unreferenced`.

**Done 2026-11-23** (owner: "Yes delete these"). Rechecked on `wip/next` after units 1-8: 63 files were still unreferenced. 56 of them were deleted, together with `generate-rc-port-golden.ps1`, which replayed the pre-port PowerShell libraries at `c6565bc95776` to rebuild the CARD-1836 golden and so cannot be ported. The board was not reachable, so card status was not checked; nothing runs these files. Seven were kept because a current doc still presents them as a tool, a witness or evidence: `distiller.ps1`, `prune-test-data.ps1` and `reap-orphaned-pty-hosts.ps1` (operator tools), `run-tests-watched.ps1` (the testing doc's watched run), `test-server2-postgres-compose.ps1` (the server2 Postgres cpuset witness), `test-apphost-git-index-lock.ps1` (the testing doc's script coverage) and `docs/investigations/evidence/card-1477/run-witness.ps1` (an investigation's evidence). They move with their waves.

## Progress

| Date | Unit | Commit | Files left |
|---|---|---|---:|
| 2026-11-22 | Inventory | this note | 356 |
| 2026-11-22 | 1: `lib/toolchain-floor.ps1` and its driver `test-toolchain-floor.ps1` deleted; command `toolchain-floor`, run by `restart-server.ps1` and `session-runner-restart-health.ps1` through `Invoke-AntiphonCli` | `00f1009e1`, index `0bb5ad8e5` | 354 |
| 2026-11-22 | 2: `lib/test-timings.ps1` and `test-timing-report.ps1` deleted; commands `test-timing-report` and `test-timing-event` (`run-checkpoint.ps1`); `select-test-rosters.ps1` calls `TestTimings.Report` and `AddEstimates` from the loaded core | `eb65393ab`, index `dceab1a2d` | 352 |
| 2026-11-22 | 3: `lib/test-selection.ps1`, `select-test-rosters.ps1` and the driver `test-test-selection.ps1` deleted; command `select-test-rosters` (`TestSelection.Run`); the selector identity is the four `Antiphon.Nightly/TestSelection*.cs` sources (embedded, eol=lf); `release-selection.ps1`, `rc-project-selection.ps1` and the live-lane libraries call `TestSelection`, `TestSelectionJson` and `TestSelectionGit` from the core they load; `select-release` uses the same core (its own `TestSelection.cs`/`SelectionCatalog.cs` are gone) | `cd31f150a` (core `f3e00bcc1`, `99896d190`; land graph `ee0a618d2`; tests `756243d08`; index `6cbf2f9c6`; owners `c1e72ec90`) | 349 |
| 2026-11-23 | 4: `impacted-classes.ps1`, `lib/release-impact.ps1` (its only callee, the `-ReleaseEvidence` envelope) and `lib/SelectorCensus.cs`, the replay host `selector-replay-host.ps1` and the driver `test-selector-census.ps1` deleted; command `impacted-classes` (`ImpactedClasses.Run`, `ReleaseImpactEvidence`), flags in kebab case; `PostLandEarlyWarning` runs it in process; the selector identity (the release algorithm digest, `RcAdmission`'s inventory) is `ImpactedClasses.SelectorFiles` (embedded, eol=lf); the tests run it in process (`ImpactedClassesRun`). `regenerate-derived-impact-index.ps1` stays: its driver compiles Roslyn, which `antiphon-scripts` would then carry (its own unit) | `01aca35b6` (core `f06dd6ef0`; index `de8fa60a7`; land graph `28eca592b`) | 345 |
| 2026-11-23 | 6 qualification-eta, watch-qual: `qualification-eta.ps1`, `orchestration/watch-qual.ps1` and the drivers `test-qualification-eta.ps1` and `test-watch-qual-eta.ps1` deleted; commands `qualification-eta` (`QualificationEta.Run`) and `watch-qual` (`WatchQual.Run`, the ETA in process, its sleep the new `NightlyHost.Sleep`), flags in kebab case; the cases run in process (`QualificationEtaTests`, `WatchQualTests`), with the watch-qual rows of `test-orchestration-watchers.ps1`; the rc-qualification skill and `docs/release-gates.md` name the commands | `0bfe2346a` | 341 |
| 2026-11-23 | 5.1: `export-expected-duration-roster.ps1` deleted; command `export-expected-duration-roster` (`ExpectedDurationRosterFile.Run`). The roster file's reader and the class-cost projection moved from `tests/Shared/ExpectedDurationRoster.cs` to the core, so the command and `ExpectedDurationSyncTests` call one reader; the `[ExpectedDuration]` scan needs Roslyn and stays test-side, out of the CLI. The test exports in process | `f05189123` | 340 |
| 2026-11-23 | 5.2: `summarize-test-trace.ps1` deleted (no caller); command `summarize-test-trace` (`TestTraceSummary.Run`). `tests/Shared/TestTrace.cs` stays the writer (it is compiled into test assemblies that do not reference the core); `TestTraceSummaryTests` pins its record against the reader | `e67297001` | 339 |
| 2026-11-23 | 5.3: `refresh-nightly-class-costs.ps1` deleted; command `refresh-nightly-class-costs` (`ClassCostRefresh.Run`; the TRX read is `NightlyTrx.ClassDurations`, the format the planner's). `ClassCostMeasurementTests` refreshes in process; the committed `nightly-class-costs.json` names the command as its refresh (no row changed). The file is written in ordinal class order with LF line ends | `187a03374` | 338 |
| 2026-11-23 | 5.4: `digest.ps1` deleted (no callers: `git grep -F digest.ps1` names only plans); command `digest` (`AwayDigestSend.Run` on the board client's HTTP transport). New `AwayDigestSendTests` bind the body and answer to the server's records; `docs/telegram.md` shows the command. The title lookup works now (under pwsh 7 the script failed whenever two or more channels exist). Unit 5 ran in parallel with unit 6 and was replayed after it (index regenerated once for both) | `c957ad4de` | 337 |
| 2026-11-23 | 7 qualification supervisor and status: `lib/rc-qualification-status.ps1` and `rc-qualification-status.ps1`, `lib/qualification-supervisor.ps1`, the seven `qualification/*.ps1` (launch-rc, observe, preflight, qualification-stop, start-supervisor, stop-supervisor, supervise-rc), `status-snapshot.ps1`, the fixture `fixtures/c1500-status.ps1` and the drivers `test-qualification-supervisor.ps1` and `test-qualification-stop.ps1` deleted; commands `rc-qualification-status` (`RcQualificationStatus`, the shared record reader `RcQualificationRecords`), `qualification-observe`, `qualification-preflight`, `qualification-launch`, `qualification-start-supervisor`, `qualification-supervise` (`QualificationSupervision`, `QualificationLaunch`), `qualification-stop` (`QualificationStop`) and `status-snapshot` (`StatusSnapshot`, `AntiphonCaller`; the caller label stays `status-snapshot.ps1`); the supervisor runs detached from a copy of the CLI in `<task-root>/cli`, and its OS seams (detached start, tree stop, command line, console, session) are `NightlyHost.Qualification` (`IQualificationHost`, production `QualificationOs`); the cases run in process (`RcQualificationStatusTests`, `QualificationSupervisorTests`, `QualificationStopTests`, `StatusSnapshotScriptTests`, `RegisteredCallerScriptTests`, the launch and preflight rows of `LongPathsCheckoutScriptTests`; `NightlyLockFormatTests` reads through the C# reader); the rc-qualification and status-update skills, `docs/release-gates.md`, `docs/logs.md`, `docs/ops-http.md` and `docs/orchestration-loop.md` name the commands | `4618e2c60` | 323 |
| 2026-11-23 | 8 release selection: `lib/release-selection.ps1`, its pwsh worker `lib/release-selection-worker.ps1`, `lib/rc-project-selection.ps1`, `select-rc-project-batches.ps1` and the drivers `test-change-aware-release.ps1`, `test-rc-project-selection.ps1` and `test-release-selection-cli.ps1` deleted, with the CLI's twins (`select-release`, `Antiphon.Scripts/ReleaseSelection.cs`, `Baseline.cs`, `RcProjectSelection.cs`, `PsJson.cs`, `PsSort.cs`). The attempt writer is `ReleaseSelectionAttempt.Create` (baseline reader `ReleaseSelectionBaseline`), called in process by `NightlyRun` (no process hop, no pwsh fallback; a refusal stays `selection-refused:<reason>`) and by `release-cut.ps1` from the core it loads; the `selection-pwsh-required` capability check went with the worker. The rc project planner is `RcProjectSelection` (command `select-rc-project-batches`, flags in kebab case); `live-lane-fingerprint.ps1` and `test-live-lane-fingerprint.ps1` call `RcProjectSelection.Read`, `Resolve`, `BlobText` and `Downstream` from the loaded core. Unit 8 ran in parallel with unit 7 and was replayed after it. The cases run in process (`RcProjectSelectionTests`, `ReleaseSelectionAttemptTests`, `ChangeAwareReleaseTests`) | `73b56317d` | 316 |
| 2026-11-23 | Unreferenced batch: 56 unreferenced scripts and `generate-rc-port-golden.ps1` deleted (12,071 lines); the testing doc and `RcPortCorpus.cs` no longer point at them; seven kept (see "Unreferenced scripts") | `97878b2a9` | 259 |
| 2026-11-23 | 10 nightly watchdog deploy: `deploy-nightly-watchdog.ps1` and its driver `test-deploy-nightly-watchdog.ps1` deleted; command `deploy-nightly-watchdog` (`NightlyWatchdogDeploy.Run`, flags `--profile` and `--deploy`; the CLI passes `ANTIPHON_WATCHDOG_DEPLOY_PROFILE` in). dotnet publish, ssh and scp run on the host's watched launcher with the new `NightlyLaunch.CaptureOutput` (output read, never printed), the HEAD stamp on its git seam, the snapshot GET on `NightlyReport.HttpTransport` (15 s), files on `IFileSystem`. The cases run in process (`NightlyVerificationContractTests.C545Deploy.cs`: the three wrapped cases with their rows, `C545_DeployManifest`, which only the nightly census ran, and new argv, failure, lookup-order and refusal cases); the driver's census row leaves both policies. `test-source-revision-stamp.ps1` drops its row; docs/nightly-watchdog.md, the assets README and env.example name the command | `da30d2372` | 257 |
| 2026-11-23 | 9 qualification graph: `lib/qualification-graph.ps1`, its driver `test-qualification-graph.ps1`, the launcher `QualificationGraphContractTests` and the commission seed `fixtures/qualification-graph/s1/preservation.json` deleted; the CARD-1514 S1 reader is `QualificationGraph.Read` with `QualificationGraphJson` (decode, canonical text, UID-set digest) and `QualificationGraphSchema` (the draft 2020-12 subset the checked-in schema uses, evaluated in C#; a keyword outside it fails closed, each violation names its pointer, so the Test-Json message parsing and the pwsh 7.6 location seam are gone). No command: nothing outside the tests reads a graph. The `SchemaAndIdentity` rows run in process (`QualificationGraphTests`); the commission-only `FullCensusUnchanged` case is dropped (it requires `HEAD` to be the S1 candidate, so it can only run at that historical commit, where the PowerShell still exists). The census row `test-qualification-graph` left the policy | this change | 255 |
| 2026-11-23 | 12 release-gate library, status, registration: `lib/release-gate.ps1`, `release-status.ps1` and `register-release-gates.ps1` deleted. The library is `ReleaseGateRemote` (remote ref/head/tag reads, the exact tag push, the tag inventory, GitHub on gh through a WatchedLauncher, the publisher's clock, `TestMode` and `PublisherAdmission`), `ReleaseGateJournal` (the publication journal's proposal, reservation, publication, promotion check and final tag on JSON nodes, behind one writer seam; the publish journal's steps) and typed `ReleaseGateTags` (`Tag`, `RemoteRef`, `Allocation`; the PSObject journal helpers are gone); `ReleaseGateNativeLock.Production()` is the live lane's lock. `publish-release.ps1`, `promote-release.ps1`, `release-candidate.ps1`, `release-cut.ps1` (its fetch is `NightlyNativeGit.Run`), `lib/live-lane-run.ps1` and the live-lane and longpaths drivers call the C# from the core they load; a test seams file's `$ReleaseGateSeams.Git`, `.GitHub`, `.UtcNow` and the new `.JournalWrite` (the promoter's journal-write faults, which overrode `Write-NightlyAtomicJson`) are handed to `ReleaseGateRemote.Seamed` and the journal. Commands `release-status` (`ReleaseStatus.Run`; a version answer without a version is now `unknown`) and `register-release-gates` (`ReleaseGateRegistration.Run` on the HTTP transport; a preview sends no Authorization header), flags in kebab case. `ReleaseAuthority.Scripts` drops `lib/release-gate.ps1`; its C# is in the pinned `lib/Antiphon.Nightly` tree digest. The cases run in process (`ReleaseGateLibraryTests`, `ReleaseStatusTests`, `ReleaseGateRegistrationTests`); the driver keeps the publisher, promoter, candidate and cut cases; `docs/release-gates.md` and the Windmill README name the commands | `898111786` | 255 |
| 2026-11-23 | 11 land graph wrappers: `audit-land-verification-graph.ps1`, `audit-land-verification-precheck.ps1`, `land-graph-land-refresh.ps1` and `lib/land-audit-tool.ps1` deleted; commands `land-graph-audit` (`LandGraphAuditRun.Run`; `--check`, `--update`, `--repo`, `--tool-path`, `--evaluation-budget-seconds`, `--land-refresh`), `land-graph-precheck` (`LandGraphAuditRun.Precheck`) and `land-graph-land-refresh` (`LandGraphLandRefresh.Run`; `--base`, `--repo`, `--card`, `--note-path`). The tool stays a separate program (it loads MSBuild): `LandAuditTool` resolves the DLL, hashes its sources against the `land-audit.source-hash` stamp and rebuilds it under the lock on the host's launcher, and the commands run it as a child (`dotnet <dll> audit|precheck|--print-pins`), the audit's tool on this process's streams. The land refresh runs the audit in process (its tool output captured, 900 s) and its audit and pin printer are `LandGraphRefreshSteps` (the former `-AuditScript`/`-PinScript` stubs). `NightlyLaunchResult` gained `StandardOutputText`/`StandardErrorText` (CaptureOutput keeps stdout apart from stderr). The cases run in process (`LandAuditToolResolverTests`, the new `LandGraphCommandTests`, `LandGraphEvaluationTests` through `LandGraphAuditRun`, `LandGraphLandRefreshTests` with C# steps and real git); the orchestrator and rc-qualification skills, `docs/orchestration-loop.md`, `docs/testing-and-build.md` and the tool's comments name the commands | this change | 251 |
| 2026-11-23 | 13 release candidate: `release-candidate.ps1` deleted; command `release-candidate` (`ReleaseCandidate.Run`; `--ref`, `--release-root`, `--checkout-root`, `--remote-url`, `--source-ref`, `--schedule-slot`, `--what-if`; the test-only `-SeamsPath` and `-PassThru` are gone). Git is a `ReleaseGateRemote`, the clock and the `[release-candidate]` lines the host's, the journal, marker and directories `IFileSystem`; the JSON line is the command's. `release-cut.ps1` (Windmill's rc entry) calls `ReleaseCandidate.Run` from the core it loads, handing a `-SeamsPath` file's `$ReleaseGateSeams.Git` to `ReleaseGateRemote.Seamed`; the driver's `Invoke-C599Candidate` (still used by `C1311_MovingTipAuthority`) does the same. Not in `ReleaseAuthority.Scripts` or any policy; its C# is in the pinned `lib/Antiphon.Nightly` tree. The cases run in process (`ReleaseCandidateTests`: C599_CandidateIdentity, C599_CutRecovery, C1311_LongLivedRcRef and new journal, long-lived, ownership, clone and command-line cases; `ReleaseCandidateGitTests`: C1339 G-7..G-9 on a real bare remote and the staged command's exits; the coordinator rows of `ReleaseGateRunTests.C599_CloneOwnership`; `LongPathsCheckoutScriptTests.LP_CloneFlag`); `docs/release-gates.md` names the command | this change | 250 |

## Appendix: every remaining `.ps1`, by wave

"Named by" counts the other tracked `.ps1` and `.cs` files that contain the file name. `driver` marks
`scripts/test-*.ps1`, `unreferenced` marks a file that no code or configuration names, and `Windmill?` marks a
name that appears in Windmill-related files.

### W1 Release and RC qualification (54 files, 17692 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `scripts/deploy-nightly-watchdog.ps1` | 231 | 7 | 2 / 4 |  |
| `scripts/digest.ps1` | 34 | 0 | 0 / 2 |  |
| `scripts/fixtures/c1339-release-git.ps1` | 144 | 3 | 2 / 0 |  |
| `scripts/fixtures/c487-readonly-windows-witness.ps1` | 73 | 4 | 0 / 1 |  |
| `scripts/fixtures/rc-boundary-pack-golden/serializer-order.ps1` | 174 | 11 | 2 / 0 |  |
| `scripts/generate-rc-port-golden.ps1` | 580 | 27 | 0 / 1 |  |
| `scripts/lib/c487-harness.ps1` | 388 | 17 | 24 / 9 |  |
| `scripts/lib/c487-nightly-tests.ps1` | 269 | 6 | 3 / 6 |  |
| `scripts/lib/nightly-common.ps1` | 477 | 30 | 27 / 23 |  |
| `scripts/lib/nightly-core.ps1` | 264 | 9 | 15 / 16 |  |
| `scripts/lib/nightly-owned-process.ps1` | 56 | 2 | 1 / 2 |  |
| `scripts/lib/qualification-graph.ps1` | 409 | 11 | 1 / 2 |  |
| `scripts/lib/qualification-supervisor.ps1` | 312 | 12 | 5 / 1 |  |
| `scripts/lib/rc-project-selection.ps1` | 265 | 6 | 6 / 2 |  |
| `scripts/lib/rc-qualification-status.ps1` | 188 | 7 | 6 / 3 |  |
| `scripts/lib/release-gate.ps1` | 502 | 26 | 14 / 26 |  |
| `scripts/lib/release-impact-scripts.ps1` | 773 | 33 | 0 / 1 |  |
| `scripts/lib/release-impact.ps1` | 123 | 4 | 1 / 2 |  |
| `scripts/lib/release-selection-worker.ps1` | 15 | 0 | 1 / 1 |  |
| `scripts/lib/release-selection.ps1` | 321 | 12 | 4 / 5 |  |
| `scripts/nightly-health.ps1` | 70 | 0 | 0 / 11 | Windmill? |
| `scripts/nightly-run.ps1` | 212 | 1 | 5 / 22 | Windmill? |
| `scripts/promote-release.ps1` | 216 | 3 | 2 / 3 |  |
| `scripts/publish-release.ps1` | 406 | 3 | 4 / 3 |  |
| `scripts/qualification-eta.ps1` | 212 | 8 | 3 / 1 |  |
| `scripts/qualification/launch-rc.ps1` | 77 | 1 | 5 / 1 |  |
| `scripts/qualification/observe.ps1` | 49 | 0 | 4 / 0 |  |
| `scripts/qualification/preflight.ps1` | 25 | 0 | 1 / 0 |  |
| `scripts/qualification/qualification-stop.ps1` | 412 | 14 | 2 / 1 |  |
| `scripts/qualification/start-supervisor.ps1` | 90 | 0 | 3 / 0 |  |
| `scripts/qualification/stop-supervisor.ps1` | 46 | 0 | 2 / 0 |  |
| `scripts/qualification/supervise-rc.ps1` | 130 | 1 | 4 / 1 |  |
| `scripts/rc-qualification-status.ps1` | 19 | 0 | 5 / 3 |  |
| `scripts/refresh-nightly-class-costs.ps1` | 124 | 1 | 0 / 1 |  |
| `scripts/register-release-gates.ps1` | 266 | 4 | 1 / 0 | Windmill? |
| `scripts/release-candidate.ps1` | 301 | 2 | 5 / 1 |  |
| `scripts/release-cut.ps1` | 184 | 1 | 2 / 11 | Windmill? |
| `scripts/release-status.ps1` | 84 | 2 | 1 / 0 |  |
| `scripts/select-rc-project-batches.ps1` | 19 | 0 | 0 / 0 | unreferenced |
| `scripts/test-change-aware-release.ps1` | 377 | 20 | 0 / 2 | driver |
| `scripts/test-deploy-nightly-watchdog.ps1` | 234 | 8 | 0 / 4 | driver |
| `scripts/test-qualification-eta.ps1` | 194 | 11 | 0 / 1 | driver |
| `scripts/test-qualification-graph.ps1` | 418 | 18 | 0 / 2 | driver |
| `scripts/test-qualification-stop.ps1` | 593 | 23 | 0 / 0 | driver |
| `scripts/test-qualification-supervisor.ps1` | 495 | 8 | 0 / 1 | driver |
| `scripts/test-rc-boundary-pack-golden.ps1` | 283 | 16 | 0 / 0 | unreferenced, driver |
| `scripts/test-rc-boundary-pack-real-tree.ps1` | 119 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-rc-boundary-pack.ps1` | 197 | 14 | 0 / 1 | driver |
| `scripts/test-rc-coverage-admission.ps1` | 151 | 1 | 0 / 1 | driver |
| `scripts/test-rc-platform.ps1` | 1000 | 27 | 0 / 3 | driver |
| `scripts/test-rc-project-selection.ps1` | 132 | 7 | 1 / 1 | driver |
| `scripts/test-release-gate.ps1` | 4495 | 148 | 2 / 18 | driver |
| `scripts/test-release-selection-cli.ps1` | 317 | 15 | 0 / 2 | driver |
| `tests/fixtures/card1336-release-gate-followups.ps1` | 147 | 2 | 1 / 1 |  |

### W2 Live lane (18 files, 3545 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `scripts/lib/live-lane-attestation.ps1` | 269 | 10 | 1 / 1 |  |
| `scripts/lib/live-lane-fingerprint.ps1` | 365 | 16 | 3 / 1 |  |
| `scripts/lib/live-lane-outcome.ps1` | 200 | 11 | 1 / 2 |  |
| `scripts/lib/live-lane-receipt.ps1` | 337 | 11 | 2 / 1 |  |
| `scripts/lib/live-lane-run.ps1` | 229 | 5 | 3 / 2 |  |
| `scripts/lib/live-startup-timing.ps1` | 37 | 3 | 5 / 1 |  |
| `scripts/live-lane-status.ps1` | 28 | 0 | 3 / 0 |  |
| `scripts/run-live-lane.ps1` | 19 | 0 | 2 / 0 |  |
| `scripts/test-live-lane-attestation.ps1` | 268 | 17 | 0 / 1 | driver |
| `scripts/test-live-lane-delivery.ps1` | 55 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-live-lane-evidence.ps1` | 167 | 2 | 0 / 1 | driver |
| `scripts/test-live-lane-fingerprint.ps1` | 364 | 23 | 0 / 1 | driver |
| `scripts/test-live-lane-isolation.ps1` | 102 | 1 | 0 / 0 | driver |
| `scripts/test-live-lane-outcome.ps1` | 340 | 12 | 0 / 1 | driver |
| `scripts/test-live-lane-receipt.ps1` | 295 | 20 | 0 / 1 | driver |
| `scripts/test-live-lane-run.ps1` | 400 | 19 | 0 / 1 | driver |
| `scripts/test-live-lane-startup.ps1` | 55 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-live-startup-timing.ps1` | 15 | 0 | 0 / 1 | driver |

### W3 Test selection, timings, toolchain (11 files, 2821 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `scripts/export-expected-duration-roster.ps1` | 52 | 1 | 0 / 2 |  |
| `scripts/impacted-classes.ps1` | 1312 | 37 | 2 / 7 |  |
| `scripts/lib/toolchain-floor.ps1` | 148 | 9 | 3 / 5 |  |
| `scripts/regenerate-derived-impact-index.ps1` | 31 | 0 | 1 / 2 |  |
| `scripts/select-test-rosters.ps1` | 40 | 0 | 6 / 7 |  |
| `scripts/selector-replay-host.ps1` | 54 | 0 | 1 / 1 |  |
| `scripts/summarize-test-trace.ps1` | 80 | 2 | 0 / 1 |  |
| `scripts/test-derived-impact-driver.ps1` | 54 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-selector-census.ps1` | 267 | 6 | 0 / 0 | unreferenced, driver |
| `scripts/test-test-selection.ps1` | 456 | 31 | 0 / 6 | driver |
| `scripts/test-toolchain-floor.ps1` | 327 | 22 | 0 / 1 | driver |

### W4 Land graph and audit (6 files, 831 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `scripts/audit-land-verification-graph.ps1` | 160 | 6 | 2 / 9 |  |
| `scripts/audit-land-verification-precheck.ps1` | 85 | 2 | 0 / 7 |  |
| `scripts/land-graph-land-refresh.ps1` | 357 | 14 | 1 / 3 |  |
| `scripts/lib/land-audit-tool.ps1` | 112 | 3 | 3 / 4 |  |
| `scripts/test-land-git-resolution.ps1` | 43 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-land-graph-c1528-fixtures.ps1` | 74 | 1 | 0 / 0 | unreferenced, driver |

### W5 Checkpoint (28 files, 7153 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `scripts/checkpoint-orphan-owner.ps1` | 39 | 0 | 1 / 0 |  |
| `scripts/checkpoint-task.ps1` | 181 | 10 | 1 / 4 |  |
| `scripts/fixtures/checkpoint-output-windows-witness.ps1` | 24 | 0 | 0 / 0 | unreferenced |
| `scripts/inspect-checkpoint-temp.ps1` | 72 | 0 | 0 / 1 |  |
| `scripts/lib/checkpoint-file-reader.ps1` | 630 | 6 | 17 / 6 |  |
| `scripts/lib/checkpoint-index-paths.ps1` | 14 | 0 | 2 / 1 |  |
| `scripts/lib/checkpoint-owned-output.ps1` | 84 | 3 | 4 / 1 |  |
| `scripts/lib/checkpoint-process-runner.ps1` | 20 | 1 | 16 / 5 |  |
| `scripts/lib/checkpoint-repeat.ps1` | 139 | 3 | 2 / 0 |  |
| `scripts/lib/checkpoint-source.ps1` | 236 | 9 | 3 / 6 |  |
| `scripts/lib/checkpoint-tool-reads.ps1` | 89 | 5 | 2 / 1 |  |
| `scripts/lib/checkpoint-usage.ps1` | 775 | 10 | 2 / 2 |  |
| `scripts/run-checkpoint.ps1` | 1539 | 46 | 20 / 25 |  |
| `scripts/test-checkpoint-bounded-reader.ps1` | 74 | 2 | 0 / 0 | unreferenced, driver |
| `scripts/test-checkpoint-compiled-facade.ps1` | 74 | 3 | 0 / 1 | driver |
| `scripts/test-checkpoint-git-encoding.ps1` | 119 | 5 | 0 / 1 | driver |
| `scripts/test-checkpoint-large-stamp.ps1` | 214 | 4 | 1 / 1 | driver |
| `scripts/test-checkpoint-output-inventory.ps1` | 301 | 14 | 0 / 1 | driver |
| `scripts/test-checkpoint-owned-output.ps1` | 129 | 9 | 0 / 1 | driver |
| `scripts/test-checkpoint-placement-skip.ps1` | 53 | 1 | 0 / 0 | unreferenced, driver |
| `scripts/test-checkpoint-pool-saturation.ps1` | 65 | 0 | 0 / 1 | driver |
| `scripts/test-checkpoint-reader-deadline.ps1` | 280 | 2 | 1 / 1 | driver |
| `scripts/test-checkpoint-stamp-ps51-parse.ps1` | 46 | 1 | 1 / 0 | driver |
| `scripts/test-checkpoint-stamp.ps1` | 61 | 0 | 0 / 1 | driver |
| `scripts/test-run-checkpoint.ps1` | 1310 | 59 | 3 / 5 | driver |
| `scripts/validate-checkpoint-receipt.ps1` | 201 | 4 | 1 / 10 |  |
| `scripts/verify-checkpoint-orphan-usage.ps1` | 244 | 5 | 1 / 1 |  |
| `scripts/verify-checkpoint-temp-usage.ps1` | 140 | 0 | 1 / 3 |  |

### W6 Host, runner and daemon operations (63 files, 15425 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `restart-session-runner.ps1` | 14 | 0 | 8 / 9 |  |
| `scripts/apphost-common.ps1` | 1244 | 60 | 13 / 2 | Windmill? |
| `scripts/apphost-watchdog-state-observer.ps1` | 115 | 3 | 2 / 2 |  |
| `scripts/autostart-apphost.ps1` | 124 | 2 | 4 / 1 |  |
| `scripts/autostart-session-runner.ps1` | 130 | 1 | 4 / 1 |  |
| `scripts/build-slot.ps1` | 187 | 2 | 24 / 17 |  |
| `scripts/check-daemon-build.ps1` | 76 | 0 | 9 / 3 |  |
| `scripts/cleanup-build-junk.ps1` | 44 | 0 | 2 / 1 | Windmill? |
| `scripts/cleanup-claude-sessions.ps1` | 605 | 15 | 1 / 1 | Windmill? |
| `scripts/cleanup-codex-test-residue.ps1` | 398 | 14 | 1 / 1 |  |
| `scripts/cleanup-gym-stat-one-off-agents.ps1` | 431 | 9 | 0 / 0 | unreferenced |
| `scripts/fixtures/script-harness-cleanup-flow-witness.ps1` | 85 | 0 | 0 / 0 | unreferenced |
| `scripts/herdr-pane.ps1` | 96 | 1 | 0 / 1 |  |
| `scripts/host-load.ps1` | 109 | 4 | 1 / 2 |  |
| `scripts/install-autostart.ps1` | 376 | 4 | 6 / 1 |  |
| `scripts/install-cleanup-task.ps1` | 32 | 0 | 1 / 0 |  |
| `scripts/keep-six.ps1` | 115 | 4 | 2 / 3 |  |
| `scripts/lib/apphost-fixture-closure.ps1` | 79 | 3 | 5 / 0 |  |
| `scripts/lib/build-driver-environment.ps1` | 65 | 4 | 9 / 1 |  |
| `scripts/lib/build-slot.ps1` | 213 | 10 | 24 / 17 |  |
| `scripts/lib/linux-compiler-evidence.ps1` | 51 | 3 | 3 / 0 |  |
| `scripts/lib/runner-operator-token.ps1` | 13 | 1 | 6 / 1 |  |
| `scripts/lib/windows-phone-home-env.ps1` | 276 | 11 | 7 / 1 |  |
| `scripts/measure-linux-shared-compiler.ps1` | 171 | 0 | 1 / 0 |  |
| `scripts/reap-orphaned-pty-hosts.ps1` | 237 | 3 | 0 / 0 | unreferenced |
| `scripts/reap-zombie-agents.ps1` | 914 | 24 | 1 / 3 |  |
| `scripts/recover-repository-children.ps1` | 166 | 4 | 2 / 11 |  |
| `scripts/restart-apphost.ps1` | 436 | 1 | 19 / 9 |  |
| `scripts/restart-session-runner.ps1` | 34 | 0 | 8 / 9 |  |
| `scripts/retire-runner-volumes.ps1` | 169 | 9 | 0 / 3 |  |
| `scripts/run-daemon.ps1` | 193 | 4 | 4 / 7 |  |
| `scripts/runner-drain.ps1` | 126 | 4 | 0 / 2 |  |
| `scripts/runner-slots.ps1` | 58 | 2 | 3 / 4 |  |
| `scripts/session-runner-restart-health.ps1` | 332 | 6 | 2 / 7 |  |
| `scripts/set-apphost-maintenance.ps1` | 116 | 1 | 6 / 1 |  |
| `scripts/start-apphost.ps1` | 3 | 0 | 22 / 9 |  |
| `scripts/start-windows-phone-home-runner.ps1` | 91 | 1 | 2 / 0 |  |
| `scripts/test-apphost-alert-sinks.ps1` | 108 | 4 | 0 / 0 | unreferenced, driver |
| `scripts/test-apphost-dead-launch.ps1` | 264 | 25 | 0 / 0 | unreferenced, driver |
| `scripts/test-apphost-git-index-lock.ps1` | 92 | 5 | 0 / 0 | unreferenced, driver |
| `scripts/test-apphost-graceful-stop.ps1` | 695 | 30 | 1 / 1 | driver |
| `scripts/test-apphost-health-window.ps1` | 419 | 22 | 0 / 0 | unreferenced, driver |
| `scripts/test-apphost-lock-age.ps1` | 206 | 8 | 0 / 1 | driver |
| `scripts/test-apphost-lock-hints.ps1` | 145 | 23 | 0 / 0 | unreferenced, driver |
| `scripts/test-apphost-main-worktree-guard.ps1` | 120 | 5 | 1 / 1 | driver |
| `scripts/test-apphost-probe-class.ps1` | 110 | 3 | 0 / 1 | driver |
| `scripts/test-apphost-server-version.ps1` | 609 | 35 | 0 / 0 | unreferenced, driver |
| `scripts/test-build-driver-environment.ps1` | 189 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-build-slot.ps1` | 889 | 40 | 1 / 1 | driver |
| `scripts/test-cache-build-slot-inputs.ps1` | 53 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-cleanup-claude-sessions.ps1` | 434 | 12 | 0 / 1 | Windmill?, driver |
| `scripts/test-cleanup-codex-test-residue.ps1` | 109 | 4 | 1 / 1 | driver |
| `scripts/test-host-load.ps1` | 17 | 0 | 0 / 2 | driver |
| `scripts/test-isolated-helper-build.ps1` | 149 | 2 | 0 / 0 | unreferenced, driver |
| `scripts/test-linux-shared-compiler.ps1` | 223 | 5 | 0 / 0 | unreferenced, driver |
| `scripts/test-reap-zombie-agents.ps1` | 581 | 11 | 0 / 1 | driver |
| `scripts/test-set-apphost-maintenance.ps1` | 137 | 9 | 0 / 0 | unreferenced, driver |
| `scripts/test-windows-phone-home-runner.ps1` | 593 | 25 | 0 / 1 | driver |
| `scripts/test-worktree-residue.ps1` | 35 | 2 | 0 / 1 | driver |
| `scripts/verify-phone-home-grok.ps1` | 757 | 21 | 0 / 3 |  |
| `scripts/watchdog-apphost.ps1` | 317 | 7 | 4 / 0 |  |
| `scripts/workspace-slots.ps1` | 153 | 0 | 0 / 1 |  |
| `scripts/worktree-residue.ps1` | 97 | 1 | 1 / 1 |  |

### W7 Board and agent tools (28 files, 6603 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `scripts/capability.ps1` | 152 | 4 | 1 / 3 |  |
| `scripts/card.ps1` | 1087 | 22 | 7 / 21 | Windmill? |
| `scripts/check-agent-context.ps1` | 51 | 0 | 1 / 0 |  |
| `scripts/complexity-chain.ps1` | 317 | 9 | 2 / 6 |  |
| `scripts/delegate.ps1` | 1494 | 14 | 6 / 64 |  |
| `scripts/dispatch-concurrency.ps1` | 172 | 7 | 1 / 1 |  |
| `scripts/distiller.ps1` | 73 | 2 | 4 / 34 |  |
| `scripts/github-sync.ps1` | 112 | 0 | 0 / 1 | Windmill? |
| `scripts/hangfire-dashboard.ps1` | 63 | 1 | 1 / 4 |  |
| `scripts/model-availability.ps1` | 139 | 3 | 2 / 1 |  |
| `scripts/new-review-scratch.ps1` | 31 | 0 | 1 / 2 |  |
| `scripts/orchestration/trx-failures.ps1` | 50 | 1 | 1 / 0 |  |
| `scripts/orchestration/watch-master.ps1` | 42 | 0 | 1 / 0 |  |
| `scripts/orchestration/watch-qual.ps1` | 112 | 0 | 2 / 0 |  |
| `scripts/orchestration/watch-tasks.ps1` | 58 | 0 | 1 / 0 |  |
| `scripts/orchestrator-workspace.ps1` | 281 | 12 | 0 / 6 |  |
| `scripts/pipeline-definition.ps1` | 79 | 2 | 0 / 2 |  |
| `scripts/project.ps1` | 256 | 7 | 0 / 2 |  |
| `scripts/remove-review-scratch.ps1` | 81 | 0 | 1 / 1 |  |
| `scripts/routing-pin.ps1` | 270 | 6 | 3 / 5 |  |
| `scripts/schedule.ps1` | 306 | 6 | 0 / 1 |  |
| `scripts/specialist-routing.ps1` | 53 | 0 | 0 / 1 |  |
| `scripts/stage-value-report.ps1` | 171 | 6 | 1 / 1 |  |
| `scripts/status-snapshot.ps1` | 189 | 3 | 1 / 4 |  |
| `scripts/task-commit.ps1` | 60 | 0 | 0 / 4 |  |
| `scripts/test-job-status.ps1` | 536 | 33 | 0 / 0 | unreferenced, driver |
| `scripts/test-stage-value-report.ps1` | 166 | 9 | 1 / 1 | driver |
| `scripts/windmill/job-status.ps1` | 202 | 10 | 3 / 0 | Windmill? |

### W8 Deploy, Postgres and infrastructure (44 files, 23593 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `scripts/check-server-docker-inputs.ps1` | 112 | 3 | 1 / 0 |  |
| `scripts/codex-boot-census.ps1` | 88 | 0 | 0 / 0 | unreferenced |
| `scripts/cutover-postgres-server2.ps1` | 1198 | 50 | 2 / 1 |  |
| `scripts/deploy-am-service.ps1` | 393 | 16 | 2 / 2 |  |
| `scripts/deploy-local.ps1` | 129 | 4 | 2 / 0 |  |
| `scripts/deploy-server2-postgres.ps1` | 471 | 23 | 1 / 0 |  |
| `scripts/deploy-server2-server.ps1` | 392 | 16 | 0 / 3 |  |
| `scripts/deploy-server2-staging-server.ps1` | 306 | 15 | 1 / 1 |  |
| `scripts/deploy-server2.ps1` | 1530 | 38 | 4 / 14 |  |
| `scripts/desktop-slot-provision.ps1` | 454 | 23 | 1 / 0 |  |
| `scripts/desktop-win-phase1-secrets.ps1` | 1207 | 55 | 1 / 0 |  |
| `scripts/fixtures/deploy-server2-output-witness.ps1` | 158 | 4 | 0 / 1 |  |
| `scripts/lib/dev-postgres-compose.ps1` | 76 | 5 | 4 / 0 |  |
| `scripts/pg-qps.ps1` | 361 | 13 | 1 / 0 |  |
| `scripts/postgres-cutover-monitor.ps1` | 1008 | 33 | 1 / 0 |  |
| `scripts/postgres-jit-window.ps1` | 126 | 4 | 0 / 1 |  |
| `scripts/postgres-snapshot.ps1` | 2194 | 89 | 6 / 1 | Windmill? |
| `scripts/refresh-server2-github-token.ps1` | 102 | 2 | 0 / 3 |  |
| `scripts/register-postgres-snapshot.ps1` | 109 | 0 | 1 / 0 | Windmill? |
| `scripts/server2-repository-mirrors.ps1` | 184 | 7 | 0 / 3 |  |
| `scripts/test-cutover-postgres-server2.ps1` | 2246 | 77 | 0 / 1 | driver |
| `scripts/test-cutover-postgres-verify.ps1` | 714 | 38 | 0 / 0 | unreferenced, driver |
| `scripts/test-deploy-am-service-readiness.ps1` | 140 | 2 | 1 / 0 | driver |
| `scripts/test-deploy-am-service.ps1` | 454 | 15 | 0 / 2 | driver |
| `scripts/test-deploy-server2-jq.ps1` | 90 | 1 | 0 / 2 | driver |
| `scripts/test-deploy-server2.ps1` | 529 | 17 | 1 / 1 | driver |
| `scripts/test-desktop-slot-provision.ps1` | 466 | 9 | 0 / 0 | unreferenced, driver |
| `scripts/test-desktop-win-phase1-secrets.ps1` | 1099 | 17 | 0 / 0 | unreferenced, driver |
| `scripts/test-dev-postgres-optout.ps1` | 189 | 7 | 0 / 0 | unreferenced, driver |
| `scripts/test-docker-container.ps1` | 66 | 1 | 0 / 2 | driver |
| `scripts/test-docker.ps1` | 109 | 1 | 0 / 1 | driver |
| `scripts/test-pg-qps.ps1` | 339 | 10 | 0 / 0 | unreferenced, driver |
| `scripts/test-postgres-cutover-monitor.ps1` | 969 | 47 | 0 / 0 | unreferenced, driver |
| `scripts/test-postgres-snapshot.ps1` | 2602 | 91 | 0 / 1 | driver |
| `scripts/test-server-docker-inputs.ps1` | 45 | 1 | 0 / 0 | driver |
| `scripts/test-server2-postgres-compose.ps1` | 966 | 20 | 0 / 0 | unreferenced, driver |
| `scripts/test-server2-staging-server-compose.ps1` | 280 | 20 | 0 / 0 | unreferenced, driver |
| `scripts/verify-card0604-dind-runner.ps1` | 364 | 3 | 0 / 1 |  |
| `scripts/verify-card0660-codex-image.ps1` | 189 | 5 | 1 / 3 |  |
| `scripts/verify-docker-stack.ps1` | 433 | 1 | 4 / 7 |  |
| `scripts/windmill/invoke-postgres-snapshot-stale.ps1` | 24 | 0 | 0 / 0 | Windmill? |
| `scripts/windmill/invoke-postgres-snapshot.ps1` | 216 | 8 | 2 / 0 | Windmill? |
| `tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1` | 99 | 0 | 0 / 2 |  |
| `tests/Antiphon.Tests/Infrastructure/Fixtures/c504-deploy-am-service-baseline.ps1` | 367 | 15 | 0 / 2 |  |

### W9 Dev stack and client (30 files, 2163 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `approve-gate.ps1` | 8 | 0 | 0 / 0 | unreferenced |
| `assert-dev-ports-clear.ps1` | 59 | 1 | 3 / 0 |  |
| `check-artifacts.ps1` | 29 | 0 | 0 / 0 | unreferenced |
| `check-workflow.ps1` | 3 | 0 | 0 / 0 | unreferenced |
| `create-workflow.ps1` | 47 | 0 | 0 / 0 | unreferenced |
| `dev-aspire.ps1` | 217 | 0 | 19 / 2 |  |
| `dev-backup.ps1` | 41 | 0 | 3 / 0 |  |
| `dev-fresh.ps1` | 41 | 0 | 0 / 0 | unreferenced |
| `dev-restore.ps1` | 67 | 0 | 0 / 0 | unreferenced |
| `dev-start.ps1` | 75 | 1 | 5 / 0 |  |
| `dev-stop.ps1` | 31 | 1 | 3 / 0 |  |
| `dev-upgrade.ps1` | 41 | 0 | 1 / 0 |  |
| `monitor-workflow.ps1` | 43 | 0 | 0 / 0 | unreferenced |
| `restart-client.ps1` | 26 | 0 | 1 / 0 |  |
| `restart-server.ps1` | 43 | 0 | 3 / 3 |  |
| `restart.ps1` | 31 | 0 | 1 / 0 |  |
| `scripts/client-mode.ps1` | 124 | 1 | 1 / 2 |  |
| `scripts/lib/client-npm-install.ps1` | 92 | 2 | 4 / 0 |  |
| `scripts/lint-client.ps1` | 80 | 0 | 4 / 0 |  |
| `scripts/logs.ps1` | 96 | 2 | 1 / 0 |  |
| `scripts/test-client-gate-exit.ps1` | 360 | 10 | 0 / 0 | unreferenced, driver |
| `scripts/test-client-lint.ps1` | 122 | 4 | 0 / 0 | unreferenced, driver |
| `scripts/test-client-mode.ps1` | 49 | 3 | 0 / 1 | driver |
| `scripts/test-client-typecheck.ps1` | 101 | 4 | 0 / 0 | unreferenced, driver |
| `scripts/test-fake-provider-layout.ps1` | 56 | 0 | 0 / 0 | unreferenced, driver |
| `scripts/test-logs.ps1` | 33 | 3 | 0 / 0 | unreferenced, driver |
| `scripts/typecheck-client.ps1` | 72 | 0 | 5 / 0 |  |
| `stop-server.ps1` | 9 | 0 | 0 / 0 |  |
| `test-api.ps1` | 7 | 0 | 0 / 0 | unreferenced |
| `verify-dev-stack.ps1` | 160 | 3 | 5 / 1 |  |

### W10 One-offs, evidence and fixtures (74 files, 13257 lines)

| File | Lines | Functions | Named by (.ps1 / .cs) | Note |
|---|---:|---:|---|---|
| `.antiphon/c959-second-evidence/inspect-receipts.ps1` | 17 | 0 | 0 / 0 |  |
| `docs/investigations/evidence/card-1477/run-witness.ps1` | 32 | 0 | 0 / 1 |  |
| `docs/investigations/evidence/card-1477/stub/claude-home/skills/launch-remote/launch-claude.ps1` | 4 | 0 | 1 / 0 |  |
| `docs/investigations/evidence/card-1477/test/harness.ps1` | 73 | 1 | 25 / 9 |  |
| `docs/investigations/evidence/card-1477/test/run-case.ps1` | 90 | 8 | 1 / 0 |  |
| `scripts/bootstrap-check.ps1` | 629 | 13 | 0 / 1 |  |
| `scripts/build-card0490-bootdisk.ps1` | 206 | 3 | 0 / 0 |  |
| `scripts/c1008-host-proof.ps1` | 291 | 8 | 1 / 5 |  |
| `scripts/c1609-host-proof.ps1` | 640 | 20 | 2 / 5 |  |
| `scripts/c1609-manual.ps1` | 222 | 4 | 1 / 1 |  |
| `scripts/c1609-read-packet.ps1` | 60 | 1 | 2 / 2 |  |
| `scripts/c590-command.ps1` | 68 | 2 | 3 / 4 |  |
| `scripts/c590-real.ps1` | 960 | 23 | 6 / 12 |  |
| `scripts/c849-import-saved-donor.ps1` | 135 | 6 | 1 / 1 |  |
| `scripts/check-os-witnesses.ps1` | 291 | 9 | 1 / 0 |  |
| `scripts/fixtures/c1339-fixture-state.ps1` | 121 | 5 | 1 / 0 |  |
| `scripts/fixtures/c1500-status.ps1` | 157 | 10 | 1 / 0 |  |
| `scripts/fixtures/c1731-evidence-path.ps1` | 138 | 6 | 1 / 0 |  |
| `scripts/fixtures/c1731-snapshot.ps1` | 57 | 3 | 1 / 0 |  |
| `scripts/fixtures/c1733-allocator.ps1` | 107 | 9 | 1 / 0 |  |
| `scripts/fixtures/c589-command-shim.ps1` | 45 | 0 | 1 / 1 |  |
| `scripts/fixtures/c589-slot-shim.ps1` | 44 | 0 | 4 / 3 |  |
| `scripts/fixtures/c727-fake-http.ps1` | 211 | 0 | 1 / 3 |  |
| `scripts/fixtures/c727-fake-verify.ps1` | 205 | 0 | 1 / 3 |  |
| `scripts/fixtures/c889-c578-observation.ps1` | 435 | 24 | 1 / 2 |  |
| `scripts/fixtures/c913-image-wrapper.ps1` | 107 | 4 | 0 / 1 |  |
| `scripts/fixtures/c913-receipts.ps1` | 146 | 4 | 0 / 1 |  |
| `scripts/fixtures/cache-bridge-diagnostics.ps1` | 79 | 5 | 0 / 1 |  |
| `scripts/fixtures/group76-native-diagnostics.ps1` | 59 | 1 | 0 / 0 | unreferenced |
| `scripts/fixtures/group77-windows-input-witness.ps1` | 94 | 6 | 0 / 0 | unreferenced |
| `scripts/fixtures/retired-temp-platform-witness.ps1` | 20 | 0 | 0 / 0 | unreferenced |
| `scripts/fixtures/script-groups-windows-witness.ps1` | 180 | 2 | 0 / 0 | unreferenced |
| `scripts/fixtures/script-harness-linux-admission-witness.ps1` | 14 | 0 | 0 / 0 | unreferenced |
| `scripts/fixtures/seed-smoke-targeting-packs.ps1` | 61 | 0 | 0 / 0 | unreferenced |
| `scripts/lib/antiphon-caller.ps1` | 29 | 1 | 4 / 3 |  |
| `scripts/lib/antiphon-cli.ps1` | 261 | 10 | 3 / 6 |  |
| `scripts/lib/phase-journal-diagnostic.ps1` | 6 | 0 | 1 / 0 |  |
| `scripts/lib/test-selection.ps1` | 367 | 16 | 9 / 13 |  |
| `scripts/lib/test-timings.ps1` | 210 | 8 | 3 / 2 |  |
| `scripts/measure-host-stats-overhead.ps1` | 191 | 4 | 0 / 0 | unreferenced |
| `scripts/measure-session-state-cache.ps1` | 426 | 17 | 0 / 1 |  |
| `scripts/migrate-stage-pins-to-cells.ps1` | 244 | 7 | 0 / 1 |  |
| `scripts/prepare-card0490-linux-assets.ps1` | 66 | 1 | 0 / 0 | unreferenced |
| `scripts/prune-test-data.ps1` | 350 | 9 | 0 / 1 |  |
| `scripts/restart-linux-server.ps1` | 307 | 14 | 2 / 3 |  |
| `scripts/run-card-1885-bounded-experiment.ps1` | 83 | 0 | 0 / 0 | unreferenced |
| `scripts/run-tests-watched.ps1` | 123 | 2 | 0 / 0 | Windmill?, unreferenced |
| `scripts/test-c1361-windows-input.ps1` | 133 | 3 | 0 / 0 | unreferenced, driver |
| `scripts/test-c1989-affected.ps1` | 52 | 1 | 0 / 0 | unreferenced, driver |
| `scripts/test-c1989-control-window.ps1` | 250 | 8 | 0 / 0 | unreferenced, driver |
| `scripts/test-card0490-native.ps1` | 24 | 0 | 0 / 1 | driver |
| `scripts/test-client.ps1` | 132 | 0 | 7 / 6 | Windmill?, driver |
| `scripts/test-duration-tripwire.ps1` | 126 | 4 | 0 / 3 | driver |
| `scripts/test-expected-platform-skips.ps1` | 225 | 3 | 0 / 1 | driver |
| `scripts/test-hooks.ps1` | 30 | 0 | 0 / 1 | driver |
| `scripts/test-longpaths-checkout.ps1` | 301 | 12 | 0 / 1 | driver |
| `scripts/test-mechanical-pc-contract.ps1` | 642 | 41 | 0 / 1 | driver |
| `scripts/test-orchestration-watchers.ps1` | 215 | 8 | 0 / 1 | driver |
| `scripts/test-os-witnesses.ps1` | 286 | 7 | 0 / 0 | unreferenced, driver |
| `scripts/test-restart-linux-server.ps1` | 432 | 14 | 0 / 1 | driver |
| `scripts/test-scripts-lane-repairs.ps1` | 154 | 2 | 0 / 1 | driver |
| `scripts/test-source-revision-stamp.ps1` | 219 | 8 | 0 / 1 | driver |
| `scripts/test-timing-report.ps1` | 24 | 0 | 0 / 1 | driver |
| `scripts/test-watch-qual-eta.ps1` | 138 | 8 | 0 / 1 | driver |
| `scripts/validate-card0418-ordinary-evidence.ps1` | 95 | 0 | 0 / 0 | unreferenced |
| `scripts/verify-agent-tui-profile.ps1` | 267 | 5 | 0 / 1 |  |
| `scripts/verify-card0594-linux-launch.ps1` | 235 | 3 | 0 / 0 | unreferenced |
| `scripts/verify-card0849-caches.ps1` | 294 | 5 | 2 / 1 |  |
| `tests/Antiphon.SessionRunner.Tests/Fixtures/RunnerRestart/platform.ps1` | 56 | 1 | 1 / 6 |  |
| `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-dialog.ps1` | 59 | 3 | 1 / 1 |  |
| `tests/Antiphon.Tests/Agents/Fixtures/claude-effort-trace-contract.ps1` | 48 | 2 | 0 / 1 |  |
| `tests/Antiphon.Tests/Scripts/Fixtures/c997-import-saved-donor.ps1` | 34 | 0 | 0 / 1 |  |
| `tests/Antiphon.Tests/Scripts/Fixtures/directory-link.ps1` | 20 | 0 | 0 / 2 |  |
| `tests/Antiphon.Tests/Scripts/Fixtures/script-harness-process.ps1` | 105 | 0 | 0 / 1 |  |


