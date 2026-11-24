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
| 2026-11-22 | 1: `lib/toolchain-floor.ps1` and its driver `test-toolchain-floor.ps1` deleted; command `toolchain-floor`, run by `restart-server.ps1` and `session-runner-restart-health.ps1` through `Invoke-AntiphonCli` | `59c6fbdc4`, index `eb15165ae` | 354 |
| 2026-11-22 | 2: `lib/test-timings.ps1` and `test-timing-report.ps1` deleted; commands `test-timing-report` and `test-timing-event` (`run-checkpoint.ps1`); `select-test-rosters.ps1` calls `TestTimings.Report` and `AddEstimates` from the loaded core | `7abb0eae2`, index `f07b1a540` | 352 |
| 2026-11-22 | 3: `lib/test-selection.ps1`, `select-test-rosters.ps1` and the driver `test-test-selection.ps1` deleted; command `select-test-rosters` (`TestSelection.Run`); the selector identity is the four `Antiphon.Nightly/TestSelection*.cs` sources (embedded, eol=lf); `release-selection.ps1`, `rc-project-selection.ps1` and the live-lane libraries call `TestSelection`, `TestSelectionJson` and `TestSelectionGit` from the core they load; `select-release` uses the same core (its own `TestSelection.cs`/`SelectionCatalog.cs` are gone) | `6c43a4778` (core `18a1fbb10`, `9e96acede`; land graph `3aae33ea8`; tests `17238b134`; index `7cd22520f`; owners `f75868813`) | 349 |
| 2026-11-23 | 4: `impacted-classes.ps1`, `lib/release-impact.ps1` (its only callee, the `-ReleaseEvidence` envelope) and `lib/SelectorCensus.cs`, the replay host `selector-replay-host.ps1` and the driver `test-selector-census.ps1` deleted; command `impacted-classes` (`ImpactedClasses.Run`, `ReleaseImpactEvidence`), flags in kebab case; `PostLandEarlyWarning` runs it in process; the selector identity (the release algorithm digest, `RcAdmission`'s inventory) is `ImpactedClasses.SelectorFiles` (embedded, eol=lf); the tests run it in process (`ImpactedClassesRun`). `regenerate-derived-impact-index.ps1` stays: its driver compiles Roslyn, which `antiphon-scripts` would then carry (its own unit) | `a3bea8650` (core `5a197ea0f`; index `9019a43c8`; land graph `3aae33ea8`) | 345 |
| 2026-11-23 | 6 qualification-eta, watch-qual: `qualification-eta.ps1`, `orchestration/watch-qual.ps1` and the drivers `test-qualification-eta.ps1` and `test-watch-qual-eta.ps1` deleted; commands `qualification-eta` (`QualificationEta.Run`) and `watch-qual` (`WatchQual.Run`, the ETA in process, its sleep the new `NightlyHost.Sleep`), flags in kebab case; the cases run in process (`QualificationEtaTests`, `WatchQualTests`), with the watch-qual rows of `test-orchestration-watchers.ps1`; the rc-qualification skill and `docs/release-gates.md` name the commands | `ca787f936` | 341 |
| 2026-11-23 | 5.1: `export-expected-duration-roster.ps1` deleted; command `export-expected-duration-roster` (`ExpectedDurationRosterFile.Run`). The roster file's reader and the class-cost projection moved from `tests/Shared/ExpectedDurationRoster.cs` to the core, so the command and `ExpectedDurationSyncTests` call one reader; the `[ExpectedDuration]` scan needs Roslyn and stays test-side, out of the CLI. The test exports in process | `ff5c3dcdd` | 340 |
| 2026-11-23 | 5.2: `summarize-test-trace.ps1` deleted (no caller); command `summarize-test-trace` (`TestTraceSummary.Run`). `tests/Shared/TestTrace.cs` stays the writer (it is compiled into test assemblies that do not reference the core); `TestTraceSummaryTests` pins its record against the reader | `13d962537` | 339 |
| 2026-11-23 | 5.3: `refresh-nightly-class-costs.ps1` deleted; command `refresh-nightly-class-costs` (`ClassCostRefresh.Run`; the TRX read is `NightlyTrx.ClassDurations`, the format the planner's). `ClassCostMeasurementTests` refreshes in process; the committed `nightly-class-costs.json` names the command as its refresh (no row changed). The file is written in ordinal class order with LF line ends | `b90a02e95` | 338 |
| 2026-11-23 | 5.4: `digest.ps1` deleted (no callers: `git grep -F digest.ps1` names only plans); command `digest` (`AwayDigestSend.Run` on the board client's HTTP transport). New `AwayDigestSendTests` bind the body and answer to the server's records; `docs/telegram.md` shows the command. The title lookup works now (under pwsh 7 the script failed whenever two or more channels exist). Unit 5 ran in parallel with unit 6 and was replayed after it (index regenerated once for both) | `90b9727bc` | 337 |
| 2026-11-23 | 7 qualification supervisor and status: `lib/rc-qualification-status.ps1` and `rc-qualification-status.ps1`, `lib/qualification-supervisor.ps1`, the seven `qualification/*.ps1` (launch-rc, observe, preflight, qualification-stop, start-supervisor, stop-supervisor, supervise-rc), `status-snapshot.ps1`, the fixture `fixtures/c1500-status.ps1` and the drivers `test-qualification-supervisor.ps1` and `test-qualification-stop.ps1` deleted; commands `rc-qualification-status` (`RcQualificationStatus`, the shared record reader `RcQualificationRecords`), `qualification-observe`, `qualification-preflight`, `qualification-launch`, `qualification-start-supervisor`, `qualification-supervise` (`QualificationSupervision`, `QualificationLaunch`), `qualification-stop` (`QualificationStop`) and `status-snapshot` (`StatusSnapshot`, `AntiphonCaller`; the caller label stays `status-snapshot.ps1`); the supervisor runs detached from a copy of the CLI in `<task-root>/cli`, and its OS seams (detached start, tree stop, command line, console, session) are `NightlyHost.Qualification` (`IQualificationHost`, production `QualificationOs`); the cases run in process (`RcQualificationStatusTests`, `QualificationSupervisorTests`, `QualificationStopTests`, `StatusSnapshotScriptTests`, `RegisteredCallerScriptTests`, the launch and preflight rows of `LongPathsCheckoutScriptTests`; `NightlyLockFormatTests` reads through the C# reader); the rc-qualification and status-update skills, `docs/release-gates.md`, `docs/logs.md`, `docs/ops-http.md` and `docs/orchestration-loop.md` name the commands | `9cabf2f08` | 323 |
| 2026-11-23 | 8 release selection: `lib/release-selection.ps1`, its pwsh worker `lib/release-selection-worker.ps1`, `lib/rc-project-selection.ps1`, `select-rc-project-batches.ps1` and the drivers `test-change-aware-release.ps1`, `test-rc-project-selection.ps1` and `test-release-selection-cli.ps1` deleted, with the CLI's twins (`select-release`, `Antiphon.Scripts/ReleaseSelection.cs`, `Baseline.cs`, `RcProjectSelection.cs`, `PsJson.cs`, `PsSort.cs`). The attempt writer is `ReleaseSelectionAttempt.Create` (baseline reader `ReleaseSelectionBaseline`), called in process by `NightlyRun` (no process hop, no pwsh fallback; a refusal stays `selection-refused:<reason>`) and by `release-cut.ps1` from the core it loads; the `selection-pwsh-required` capability check went with the worker. The rc project planner is `RcProjectSelection` (command `select-rc-project-batches`, flags in kebab case); `live-lane-fingerprint.ps1` and `test-live-lane-fingerprint.ps1` call `RcProjectSelection.Read`, `Resolve`, `BlobText` and `Downstream` from the loaded core. Unit 8 ran in parallel with unit 7 and was replayed after it. The cases run in process (`RcProjectSelectionTests`, `ReleaseSelectionAttemptTests`, `ChangeAwareReleaseTests`) | `794aa6f1f` | 316 |
| 2026-11-23 | Unreferenced batch: 56 unreferenced scripts and `generate-rc-port-golden.ps1` deleted (12,071 lines); the testing doc and `RcPortCorpus.cs` no longer point at them; seven kept (see "Unreferenced scripts") | `f47da556e` | 259 |
| 2026-11-23 | 10 nightly watchdog deploy: `deploy-nightly-watchdog.ps1` and its driver `test-deploy-nightly-watchdog.ps1` deleted; command `deploy-nightly-watchdog` (`NightlyWatchdogDeploy.Run`, flags `--profile` and `--deploy`; the CLI passes `ANTIPHON_WATCHDOG_DEPLOY_PROFILE` in). dotnet publish, ssh and scp run on the host's watched launcher with the new `NightlyLaunch.CaptureOutput` (output read, never printed), the HEAD stamp on its git seam, the snapshot GET on `NightlyReport.HttpTransport` (15 s), files on `IFileSystem`. The cases run in process (`NightlyVerificationContractTests.C545Deploy.cs`: the three wrapped cases with their rows, `C545_DeployManifest`, which only the nightly census ran, and new argv, failure, lookup-order and refusal cases); the driver's census row leaves both policies. `test-source-revision-stamp.ps1` drops its row; docs/nightly-watchdog.md, the assets README and env.example name the command | `ac2bf54d2` | 257 |
| 2026-11-23 | 9 qualification graph: `lib/qualification-graph.ps1`, its driver `test-qualification-graph.ps1`, the launcher `QualificationGraphContractTests` and the commission seed `fixtures/qualification-graph/s1/preservation.json` deleted; the CARD-1514 S1 reader is `QualificationGraph.Read` with `QualificationGraphJson` (decode, canonical text, UID-set digest) and `QualificationGraphSchema` (the draft 2020-12 subset the checked-in schema uses, evaluated in C#; a keyword outside it fails closed, each violation names its pointer, so the Test-Json message parsing and the pwsh 7.6 location seam are gone). No command: nothing outside the tests reads a graph. The `SchemaAndIdentity` rows run in process (`QualificationGraphTests`); the commission-only `FullCensusUnchanged` case is dropped (it requires `HEAD` to be the S1 candidate, so it can only run at that historical commit, where the PowerShell still exists). The census row `test-qualification-graph` left the policy | `2d704b495` (census `fdf1ece44`) | 255 |
| 2026-11-23 | 12 release-gate library, status, registration: `lib/release-gate.ps1`, `release-status.ps1` and `register-release-gates.ps1` deleted. The library is `ReleaseGateRemote` (remote ref/head/tag reads, the exact tag push, the tag inventory, GitHub on gh through a WatchedLauncher, the publisher's clock, `TestMode` and `PublisherAdmission`), `ReleaseGateJournal` (the publication journal's proposal, reservation, publication, promotion check and final tag on JSON nodes, behind one writer seam; the publish journal's steps) and typed `ReleaseGateTags` (`Tag`, `RemoteRef`, `Allocation`; the PSObject journal helpers are gone); `ReleaseGateNativeLock.Production()` is the live lane's lock. `publish-release.ps1`, `promote-release.ps1`, `release-candidate.ps1`, `release-cut.ps1` (its fetch is `NightlyNativeGit.Run`), `lib/live-lane-run.ps1` and the live-lane and longpaths drivers call the C# from the core they load; a test seams file's `$ReleaseGateSeams.Git`, `.GitHub`, `.UtcNow` and the new `.JournalWrite` (the promoter's journal-write faults, which overrode `Write-NightlyAtomicJson`) are handed to `ReleaseGateRemote.Seamed` and the journal. Commands `release-status` (`ReleaseStatus.Run`; a version answer without a version is now `unknown`) and `register-release-gates` (`ReleaseGateRegistration.Run` on the HTTP transport; a preview sends no Authorization header), flags in kebab case. `ReleaseAuthority.Scripts` drops `lib/release-gate.ps1`; its C# is in the pinned `lib/Antiphon.Nightly` tree digest. The cases run in process (`ReleaseGateLibraryTests`, `ReleaseStatusTests`, `ReleaseGateRegistrationTests`); the driver keeps the publisher, promoter, candidate and cut cases; `docs/release-gates.md` and the Windmill README name the commands | `79aae63f6` | 255 |
| 2026-11-23 | 11 land graph wrappers: `audit-land-verification-graph.ps1`, `audit-land-verification-precheck.ps1`, `land-graph-land-refresh.ps1` and `lib/land-audit-tool.ps1` deleted; commands `land-graph-audit` (`LandGraphAuditRun.Run`; `--check`, `--update`, `--repo`, `--tool-path`, `--evaluation-budget-seconds`, `--land-refresh`), `land-graph-precheck` (`LandGraphAuditRun.Precheck`) and `land-graph-land-refresh` (`LandGraphLandRefresh.Run`; `--base`, `--repo`, `--card`, `--note-path`). The tool stays a separate program (it loads MSBuild): `LandAuditTool` resolves the DLL, hashes its sources against the `land-audit.source-hash` stamp and rebuilds it under the lock on the host's launcher, and the commands run it as a child (`dotnet <dll> audit|precheck|--print-pins`), the audit's tool on this process's streams. The land refresh runs the audit in process (its tool output captured, 900 s) and its audit and pin printer are `LandGraphRefreshSteps` (the former `-AuditScript`/`-PinScript` stubs). `NightlyLaunchResult` gained `StandardOutputText`/`StandardErrorText` (CaptureOutput keeps stdout apart from stderr). The cases run in process (`LandAuditToolResolverTests`, the new `LandGraphCommandTests`, `LandGraphEvaluationTests` through `LandGraphAuditRun`, `LandGraphLandRefreshTests` with C# steps and real git); the orchestrator and rc-qualification skills, `docs/orchestration-loop.md`, `docs/testing-and-build.md` and the tool's comments name the commands | `00dbc99cc` | 251 |
| 2026-11-23 | 13 release candidate: `release-candidate.ps1` deleted; command `release-candidate` (`ReleaseCandidate.Run`; `--ref`, `--release-root`, `--checkout-root`, `--remote-url`, `--source-ref`, `--schedule-slot`, `--what-if`; the test-only `-SeamsPath` and `-PassThru` are gone). Git is a `ReleaseGateRemote`, the clock and the `[release-candidate]` lines the host's, the journal, marker and directories `IFileSystem`; the JSON line is the command's. `release-cut.ps1` (Windmill's rc entry) calls `ReleaseCandidate.Run` from the core it loads, handing a `-SeamsPath` file's `$ReleaseGateSeams.Git` to `ReleaseGateRemote.Seamed`; the driver's `Invoke-C599Candidate` (still used by `C1311_MovingTipAuthority`) does the same. Not in `ReleaseAuthority.Scripts` or any policy; its C# is in the pinned `lib/Antiphon.Nightly` tree. The cases run in process (`ReleaseCandidateTests`: C599_CandidateIdentity, C599_CutRecovery, C1311_LongLivedRcRef and new journal, long-lived, ownership, clone and command-line cases; `ReleaseCandidateGitTests`: C1339 G-7..G-9 on a real bare remote and the staged command's exits; the coordinator rows of `ReleaseGateRunTests.C599_CloneOwnership`; `LongPathsCheckoutScriptTests.LP_CloneFlag`); `docs/release-gates.md` names the command | `bfddae498` | 250 |
| 2026-11-23 | 14 publish and promote: `publish-release.ps1`, `promote-release.ps1` and the fixture `fixtures/c1731-snapshot.ps1` deleted; commands `publish-release` (`ReleasePublish.Run`) and `promote-release` (`ReleasePromote.Run`) on one host, `ReleaseGateHost` (output, wall clock, the release card readback and, in a test, `ReleaseGateSeams`: git, GitHub, the publisher's clock and the journal writer, admitted only in test mode with both fakes bound), flags in kebab case; the CLI reads `ANTIPHON_API` and `ANTIPHON_RELEASE_GATE_TEST_MODE` and passes them in. `release-cut.ps1` (Windmill's antiphon-release-candidates) calls `ReleasePublish.Run` once from the core it loads, on the production host. The gate and the manifest allowlist answer typed records (`ReleaseGatePublication.Gate`, `ManifestAllowlistCheck`; their PSObject forms had no caller left) and `ReleaseGateCredit.CandidatePaths` is StatePaths' plain twin; `ReleaseAuthority.Check` takes the card reader from the host. `ReleaseAuthority.Scripts` drops the two scripts; their C# is in the pinned `lib/Antiphon.Nightly` tree digest, and a record frozen earlier with the two extra keys is checked on the four current keys only. The cases run in process over `ReleaseGateFixture` (in-memory remote and GitHub, real loopback card API) and `ReleaseGateGitFixture` (a real local bare remote): `ReleasePublishTests`, `ReleaseGateStatusTests`, `ReleaseGateAuthorityContractTests`, `ReleaseGatePublicationAuthorityTests`, `ReleasePromoteTests`, `ReleaseTagAllocatorTests`; the driver keeps the coordinator's and the policy/credit cases (C1339 G-7..G-9 with the PowerShell adapter rows G-64/G-65), and drops C1731_SnapshotIsolation, C1731_SnapshotWork, C1733_ReservationFinally, C1733_SnapshotWork and C1733_SharedSeed's publisher probe (PowerShell fixture mechanics of the moved cases). `docs/release-gates.md` names the commands | `61e1774df` | 247 |
| 2026-11-23 | 16 rc boundary pack driver: `test-rc-boundary-pack.ps1` and its fixture `fixtures/rc-boundary-pack-golden/serializer-order.ps1` deleted; the subject was already C# (`NightlyTestsPlan.BoundaryLaunchGroups` and `CompleteBoundaryLaunchGroups`, `NightlyLaunchPlan.CaseSortKey`, `RcBoundary.ChunkId`). The six cases run in process (`RcBoundaryPackTests`, a row per driver check) and again compare each fixture pack with its CARD-1873 golden (`rc-boundary-pack-golden/*.json`, unread since the unreferenced batch deleted `test-rc-boundary-pack-golden.ps1`) and count one boundary index build per pack. The serializer case keeps the C# side of the ConvertTo-Json pin: the oracle's expected escapes as literals, and the ConvertTo-Json bytes for every code unit and for the 84 sample keys as SHA-256 digests captured from pwsh 7.6.10, with the tests' `PsJson` as the per-code-unit oracle; the in-process ConvertTo-Json calls are dropped. The real-tree goldens (`real-tree-*`, which pinned a 2026-11-13 tree's pack for the deleted `test-rc-boundary-pack-real-tree.ps1`) are deleted: nothing reads them and no test can, because the tree they pinned has moved. `fixtures/c487-readonly-windows-witness.ps1` stays: it exercises `Complete-C487Harness` in `lib/c487-harness.ps1` itself and goes with that harness | `eb3d8e3a6` | 245 |
| 2026-11-23 | 15 release cut and the release-gate driver: `release-cut.ps1` is a thin launcher (Windmill's `antiphon_release_candidates` starts it by path; the job is unchanged): its parameter block and defaults stay, a non-empty `-SeamsPath` refuses (exit 3, `seams-path-unsupported`), an explicitly blank `-ReleaseRoot`, `-CoordinationRoot`, `-RemoteUrl` or `-Repository` refuses (exit 2), `-PassThru` reads the record back from `--result-file`, and everything else is the new command `release-cut` (`ReleaseCut.Run` on `ReleaseCutHost`: the candidate, the pinned object's fetch, the selection attempt, the rc run and the publisher; flags in kebab case; the CLI reads `ANTIPHON_API` and `ANTIPHON_RELEASE_GATE_TEST_MODE`). The rc run is `NightlyRun.Run` in the same process (no `nightly-run.ps1 -PassThru` child CLI); the record's `selection` is now the whole attempt (ConvertTo-Json cut it at depth 2). `test-release-gate.ps1`, `fixtures/c1339-fixture-state.ps1`, `fixtures/c1339-release-git.ps1`, `fixtures/c1733-allocator.ps1`, `fixtures/c1731-evidence-path.ps1` and `tests/fixtures/card1336-release-gate-followups.ps1` (and its owner row) deleted; the PowerShell-shaped credit twins that lost their last caller went too (`ReleaseGateCredit.CandidateRef`, `GreenFlags`, `CreditVerdict`, `StatePaths`; `NightlyRunParts.StatePaths`, `CompleteGreenPredicate`). The cases run in process with their row names (`ReleaseGatePolicyTests`, `ReleaseGateRunTests`, `ReleaseGateActivationTests`, `ReleaseGateE2EEvidenceTests`, `C599SnapshotTests`, `C1733AllocatorFixtureTests`, `ReleaseTagAllocatorTests.C1339_ExactRemoteRefs` on the C# adapter, `ReleaseGateFollowupTests.C1336_WrapperRef` under Git Bash), the cut's in `ReleaseCutTests`, `QualificationTimingTests`, `ChangeAwareReleaseTests` and `AntiphonCliLauncherTests`. Dropped: `C599_RunSummaryIdentity` (a driver-local helper and a PowerShell seam), `C1733_NativeMetadata` (PowerShell provider calls), `C1733_SharedSeed`'s seed-built-once row (the PowerShell seed cache). `ReleaseAuthority.Scripts` unchanged; `docs/release-gates.md` and `docs/testing-and-build.md` name the command | `64a5b75cb`, `b6d75435d`, `22ba0a181`, index `25cf0f61b` and `1ff6874e2` | 239 |
| 2026-11-23 | 18 rc platform and coverage admission drivers: `test-rc-platform.ps1` and `test-rc-coverage-admission.ps1` deleted; their subjects were already C# (`NightlyPolicy`, `ExecutionPolicy`, `ReleaseAuthority`, `NightlyTests`, `NightlyCensus`, the coverage verdict). The cases run in process with their row names: `ReleaseGatePlatformTests` (C1323_PlatformPolicy, its frozen ledger on a real fixture commit; C1323_PlatformExecution, `NightlyTests.Run` per platform with a launcher that answers as TUnit; C1199_WindowsRequiredRoster on the compiled roster, the namespace census's skip lists read from `lib/checkpoint-usage.ps1`'s source) and `RcCoverageAdmissionTests`. The Linux-only census that lived in the driver is `LinuxOnlyCensus` (`tests/Antiphon.Tests/Scripts/LinuxOnlyCensus.cs`; ReleaseGatePlatformTests.Census.cs holds LinuxOnlyWindowsExclusionPolicy and the 62 C1686 rows, their fixture sources spelling the attribute as NLO). The census routes its own source instead of the driver (`P-LINUX-ONLY-CENSUS`; catalog and policy hashes resealed). Dropped: the retired `-Case LinuxOnlyWindowsExclusions` refusal (driver-only). `lib/c487-nightly-tests.ps1` keeps its loader `lib/c487-harness.ps1` | `0c0aa99a7` (pin `9bddec70b`, index `799901c7b`) | 237 |
| 2026-11-23 | 19 checkpoint drivers: `test-checkpoint-pool-saturation.ps1` and `test-checkpoint-stamp-ps51-parse.ps1` deleted. The pool witness (`CheckpointReceiptStallBudgetTests.healthy_receipts_complete_with_every_pool_worker_occupied`, Linux, same rows) runs in the staged script harness host (`checkpoint-pool-witness`, a child because the pool cap is process-wide) against the library `CheckpointFileReader` and against the reader `lib/checkpoint-file-reader.ps1` embeds, which the test compiles from that file's here-string with Roslyn (`EmbeddedCheckpointReader`). The 5.1 parse witness is dropped (a host fact); its ASCII half is `CheckpointLargeStampScriptTests.C1532_stamp_scripts_are_ascii`, and `test-checkpoint-large-stamp.ps1` loses its parser mode. The other eight W5 drivers stay for unit 20: each also exercises `run-checkpoint.ps1` functions (AST-extracted) or `lib/checkpoint-owned-output.ps1`'s seal, so their cases move when `run-checkpoint.ps1` does. No library lost its last loader | `a1fc0658b` | 235 |
| 2026-11-23 | 17 release impact script closure: `lib/release-impact-scripts.ps1` (CARD-1820 S1R5) deleted. The closure is `ReleaseImpactScripts.Close` (typed `ReleaseScriptConsumer` in, `ReleaseScriptClosure` out, `ToJson` the `antiphon-release-script-closure-v1` document; the reasons, anchors, caps 64/24/10000/160/128 and outputs are the PowerShell's) and its PowerShell reader `PsReader` (`ReleaseImpactScriptReader.cs`): a tokenizer and parser for what the closure reads, in place of System.Management.Automation's parser, which the test process cannot load. What it does not read is the file's `parse` reason; classes, enums, data and configuration statements (no script uses them) are one, where PowerShell parsed classes. No command and no selector wiring: nothing outside the tests calls it, and composing it into `ImpactedClasses` is S1R6, still pending (CARD-1882 and CARD-1883 first). `ReleaseImpactScriptTests` runs every case in process (no pwsh child, no process limiter) and pins the PowerShell's answers on the forms it reads (`PowerShellFormsKeepTheirTargets`); at port time the C# matched the PowerShell exactly on the real-tree case (231 edges, 24 reasons) and on the closures of two samples of 36 and 40 scripts | `9218398bc` | 234 |
| 2026-11-23 | 21 host operator tools: `host-load.ps1`, `worktree-residue.ps1`, `herdr-pane.ps1`, `runner-drain.ps1`, `workspace-slots.ps1` and the drivers `test-host-load.ps1`, `test-worktree-residue.ps1` and `test-cleanup-codex-test-residue.ps1` deleted; commands `host-load` (`HostLoad`, with its own transport: 3 s and 1 MiB per read), `runner-drain`, `workspace-slots` and `herdr-pane` (the verb first, as the scripts took it) and `worktree-residue` (`--action`), flags in kebab case; the CLI reads `ANTIPHON_API`, `ANTIPHON_TASK_TOKEN`, `ANTIPHON_TASK_ID` and `ANTIPHON_OPERATOR_TOKEN_FILE` (else the server's default path) and passes them in (`OperatorCommands`); the workspace-slots requester default stays `workspace-slots.ps1`. `cleanup-build-junk.ps1` and `cleanup-codex-test-residue.ps1` stay as thin 5.1 launchers (Windmill's `antiphon_build_junk_cleanup` starts the first by path; the second's header names `antiphon_codex_residue_cleanup` as its schedule, unverified) of `cleanup-build-junk` (`BuildJunkCleanup`: a producer passes its ledger's removal in process; the launcher refuses `-CleanupContext`, exit 3) and `cleanup-codex-test-residue` (`CodexTestResidue` on `ICodexResidueSeams`; the fixture seams `-CodexShim` and `-PassThru` are gone). The cases run in process with their row names (`C1514HostLoadScriptTests`; `C1514HostLoadCrashCases` starts the probe's apphost under the ScriptHarness instead of the driver; `WorktreeResidueScriptTests`, `HerdrPaneScriptTests`, `RunnerDrainScriptTests`, `C1960WorkspaceSlotsScriptTests`, the new `CodexTestResidueTests`, `WorktreeRemovalAuthorityTests`, and `ScriptsLaneRepairTests`' opaque, owned and replaced rows, which `test-scripts-lane-repairs.ps1` dropped); the launchers' in `AntiphonCliLauncherTests`. The codex driver's census row left both policies; docs and the rc-qualification skill name the commands | `e62cbe0e3` | 226 |
| 2026-11-23 | 22 session reapers: `reap-zombie-agents.ps1` and the drivers `test-reap-zombie-agents.ps1` and `test-cleanup-claude-sessions.ps1` deleted; commands `cleanup-claude-sessions` (`ClaudeSessionCleanup`: HTTP on `NightlyReportHttp`, the live bridge pids on a process-name delegate; `--require-disconnected true\|false` for the `[bool]`, `--action` Archive or Delete ignoring case) and `reap-zombie-agents` (`ZombieAgentReaper` on `IZombieAgentSeams`: the runner and kill requests, the `docker exec psql` census, a Win32 process table read without WMI (toolhelp snapshot, image path, times, `QualificationOs` command line), `taskkill` and liveness; manifests, ansi logs and transcripts on `IFileSystem`; the verify wait on `NightlyHost.Sleep`), flags in kebab case; the CLI passes `USERPROFILE` and the checkout in (`SessionReaperCommands`). `cleanup-claude-sessions.ps1` stays as a thin 5.1 launcher (Windmill's `u/lndcobra/claude_session_cleanup` starts it by path; job unchanged and not read); its fixture seams `-HttpShim` and `-PassThru` are gone and an unparseable `-Now` is a usage error (2). Nothing scheduled `reap-zombie-agents.ps1` (CARD-0232 closed the proposed Windmill job; the server's Hangfire `antiphon:zombie-census` is the scheduled census), so it has no launcher; its injection seams (`-ProcessesJson`, `-RunnerJson`, `-DbJson`, `-Now`, `-HttpShim`, `-PassThru`) are gone. The server's `ZombieCensusClassifier` ports the same rules but the core cannot reference the server without a project-file edit, so the command keeps the script's own rules. The cases run in process with their row names (`ClaudeSessionCleanupTests` T1-T12, `ZombieAgentReaperTests`), the launcher's in `AntiphonCliLauncherTests`; both census rows left both policies (resealed); `docs/session-runtime-invariants.md` and the server's census comments name the command | `6424234de` (pin `2a9293b5c`) | 223 |
| 2026-11-23 | 23 operator commands: `keep-six.ps1`, `runner-slots.ps1` and `recover-repository-children.ps1` ported; commands `keep-six` (`KeepSix.Run`: `--hold --agent --reason --until`, `--clear-hold --agent`, `--ack --agent`, none for the status read), `runner-slots` (`RunnerSlots.Run`, the verb first; the operator token through `OperatorApi.ReadToken`, as runner-drain) and `recover-repository-children` (`RepositoryChildRecovery.Run`: `--repository`, `--execute`, `--confirm-descendants-exited`; git on the host's seam, the common directory canonicalised as `LandingGit` does, the landing lock and the records on `IFileSystem`, the PID on the `RepositoryChildIdentity` delegate), flags in kebab case; the CLI passes `ANTIPHON_API`, `ANTIPHON_TASK_TOKEN` and the operator token file in (`OperatorCommands`, `RecoverRepositoryChildrenCommand`). The server texts name the commands: the keep-six reminder (`Declare a hold with scripts/antiphon keep-six.`), the lease fence sentence and the journal cleanup failure (`; run scripts/antiphon recover-repository-children`), the stale-journal alarm's recovery line (`scripts/antiphon recover-repository-children --repository <repo>`, then `--execute --confirm-descendants-exited`) and the force-release 403 detail; so do the orchestrator bundle, AGENTS.md, `docs/ops-http.md`, `docs/orchestration-loop.md`, `docs/agent-credentials.md`, `docs/cli-upgrade.md` and `docs/docker-stack.md`. `lib/runner-operator-token.ps1` stays (delegate, deploy-server2, retire-runner-volumes, c590-real and c1609-manual load it). The cases run in process (`RepositoryChildRecoveryTests`, `LandingGitTests`, `RepositoryMutationLeaseTests.C448_C24_KilledWorkerLeavesLiveGitChildFenced` without its pwsh recovery worker), with new `KeepSixCommandTests`, `RunnerSlotsCommandTests` and `RepositoryChildRecoveryCommandTests`. Dropped: the Windows PowerShell 5.1 agreement check (a host fact) and the C24 evidence's worker PID. `cutover-postgres-server2.ps1` (W8, owner-gated) starts `keep-six.ps1` and `recover-repository-children.ps1` by path, so both stay as thin 5.1 launchers into the commands (`Invoke-AntiphonCli`, the script parameters kept, 9 when the CLI cannot start) until the cutover is ported; `runner-slots.ps1` is deleted | `a0f3a3f5e` (launchers `92fe96efe`) | 222 |
| 2026-11-23 | 24 board API wrappers: `capability.ps1`, `model-availability.ps1`, `specialist-routing.ps1`, `pipeline-definition.ps1`, `project.ps1`, `schedule.ps1`, `dispatch-concurrency.ps1` and `routing-pin.ps1` deleted; commands `capability`, `model-availability`, `specialist-routing`, `pipeline-definition`, `project`, `schedule`, `dispatch-concurrency` and `routing-pin` (`CapabilityCli`, `ModelAvailabilityCli`, `SpecialistRoutingCli`, `PipelineDefinitionCli`, `ProjectCli`, `ScheduleCli`, `DispatchConcurrencyCli`, `RoutingPinCli`, sharing `BoardCli`: the verb first and a schedule id or project after it, flags in kebab case, choices matched ignoring case and sent in their listed spelling, one failure line `Antiphon <METHOD> <path> failed: ...` with a problem answer's code, field errors and candidates); the CLI passes `ANTIPHON_API`, `ANTIPHON_TASK_TOKEN`, `ANTIPHON_AGENT_ID` and the capability store (`ANTIPHON_CAPABILITY_STORE`, else `%LOCALAPPDATA%\Antiphon\capabilities`) in (`BoardCommands`). None sent a caller label. The capability token is sealed with DPAPI through `CryptProtectData` (what `ProtectedData` CurrentUser writes; delegate.ps1 reads it unchanged); off Windows issue and rotate refuse before any request. pipeline-definition prints JSON only (its `-Json` is gone). delegate.ps1, complexity-chain.ps1 and migrate-stage-pins-to-cells.ps1 only named the scripts in text: their messages and comments name the commands. The cases run in process (`CapabilityScriptTests`, `ModelAvailabilityScriptTests`, `SpecialistRoutingScriptTests` against the in-process server, `PipelineDefinitionScriptTests`, `ProjectScriptWorkspaceTests`, the project rows of `CommitOnSettleScriptTests`, `DispatchConcurrencyScriptTests`, the routing-pin rows of `RoutingPinScriptTests`), with new `ScheduleCommandTests` and `ProjectCommandTests`; the opt-in `Antiphon.E2E` `ScheduleCliE2ETests` runs the command in process against its real server. `AgentTaskRoleBindingTests` reads `RoutingPinCliOptions.Roles`; the `dpapi-protected-data` row of `tests/impacted-classes.json` names `CapabilityCli.cs`. AGENTS.md, the board-api bundle, the client's RolePolicySection, `docs/antiphon-api.md`, `docs/ops-http.md`, `docs/orchestration-loop.md`, `docs/cli-upgrade.md` and `docs/agent-kinds.md` name the commands | `f0c837046` (pin `f0dee792e`) | 214 |
| 2026-11-23 | 25 agent helpers: `check-agent-context.ps1`, `task-commit.ps1`, `new-review-scratch.ps1`, `remove-review-scratch.ps1`, `orchestration/trx-failures.ps1`, `orchestration/watch-master.ps1`, `orchestration/watch-tasks.ps1`, `stage-value-report.ps1`, `logs.ps1`, `hangfire-dashboard.ps1` and the drivers `test-orchestration-watchers.ps1` and `test-stage-value-report.ps1` deleted; commands `check-agent-context` (`AgentContextCheck`; `bootstrap-check.ps1` runs it through `Invoke-AntiphonCli`), `task-commit` (`TaskCommit`), `new-review-scratch` and `remove-review-scratch` (`ReviewScratch`; RunnerTempSweep's review-clone producer pin names `ReviewScratch.cs`), `trx-failures` (paths positional), `watch-master`, `watch-tasks` (`TrxFailures`, `WatchMaster`, `WatchTasks`, the sleep on `NightlyHost.Sleep`), `stage-value-report` (`StageValueReport`), `logs` (`AntiphonLogs`, ssh on the host's launcher), `hangfire-dashboard` (`HangfireDashboard`, the browser a caller seam the CLI binds on Windows only) and `github-sync` (`GithubSync`), flags in kebab case (`AgentHelperCommands`). `github-sync.ps1` stays as a thin 5.1 launcher (Windmill's `u/lndcobra/antiphon_github_sync` starts it by path with `-Notify`). The server's commit-child brief and commit-on-settle goal name `scripts/antiphon task-commit`, the dashboard refusals `scripts/antiphon hangfire-dashboard`. remove-review-scratch compares git's worktree paths in full form (the script's raw compare never matched git's `/` on Windows). The cases run in process (`OrchestrationWatcherTests`, `StageValueReportTests`, the task-commit rows of `CommitOnSettleScriptTests`, the review rows of `OwnedTempLeakTests`, `GithubSyncScriptTests`), with new `AgentContextCheckTests`, `AntiphonLogsTests`, `HangfireDashboardCommandTests` and the launcher's in `AntiphonCliLauncherTests`; `RcQualificationWatcherScriptTests` (which launched the watcher driver) is deleted. The stage-value-report census row left both policies; the rc-qualification skill and docs name the commands | `a91511adc` (pin `ac9d13f35`) | 202 |
| 2026-11-23 | 26 complexity chain, stage pin migration, orchestrator workspace: `complexity-chain.ps1`, `migrate-stage-pins-to-cells.ps1` and `orchestrator-workspace.ps1` deleted; commands `complexity-chain` (`ComplexityChainCli`: the verb first, `--role` with `Any`, `--complexity` with `All`, `--candidates`, `--provenance`, `--not-after`, `--reason` or `--reason-file`, `--clear-stage-pin`, `--json`), `migrate-stage-pins-to-cells` (`StagePinMigration`: `--role`, `--dry-run`, `--apply`, `--restore --snapshot`, `--out-dir`; the snapshot on `IFileSystem`, the clock the host's) and `orchestrator-workspace` (`OrchestratorWorkspace`: the verb and target first, `--cli`, `--path`, `--verify`, `--json`; every file write, the sibling, the marker, the context files, `.claude/settings.json` and `~/.claude.json`, on `IFileSystem`, the skills junction a `WorkspaceJunction` seam the CLI binds to `mklink /J` on the host's launcher on Windows only), flags in kebab case (`RoutingWorkspaceCommands`); the CLI passes `ANTIPHON_API`, `ANTIPHON_TASK_TOKEN` and `USERPROFILE` (else the profile folder) in. Combination errors the scripts exited 1 on (`-ClearStagePin` outside set All with a named role, `-Reason` with `-ReasonFile`, a missing `-Complexity`, two of `-DryRun`/`-Apply`/`-Restore`) are usage errors (2). The server texts name the commands: the empty-chain exhaustion sentence (`Set one with scripts/antiphon complexity-chain set --role <R> --complexity <C>, or set --complexity <C> for every role.`), the workspace attention excerpt, the workspace warning incident and the six readiness details (`scripts/antiphon orchestrator-workspace plan|setup`); so do the client's ComplexityChainPanel, `docs/antiphon-api.md` and `docs/orchestration-loop.md`. The cases run in process: `ComplexityChainScriptTests` and `MigrateStagePinsScriptTests` on an in-process `ScriptedApi` (no loopback listener, no pwsh), the new `OrchestratorWorkspaceCommandTests` on a MockFileSystem with a recording junction; `AgentTaskRoleBindingTests` reads `ComplexityChainCliOptions.Roles` and `StagePinMigration.Roles`, `OrchestratorInstructionsGuidanceTests` the C# context and settings writers. No caller started the scripts by path, so nothing keeps a launcher | `3c519664c` | 199 |
| 2026-11-23 | 27 card: `card.ps1` deleted; command `card` (`CardCli`: the verb first, then the card or the search phrase, flags in kebab case; `-Limits` is the `limits` verb, and `--clear-alias` joins `--alias ''` because pwsh drops an empty argument to `antiphon.cmd`), in `BoardCommands`; the CLI passes `ANTIPHON_API`, `ANTIPHON_TASK_TOKEN` and its working directory in. The caller label stays `card.ps1` (`AgentTaskGetPollMetrics` meters card GET polls by it; `AntiphonCaller`). The checkout sent as `cwd` is git's top level on the host's git seam, else the `antiphon.workspace.json` marker (on `IFileSystem`), else the working directory; relative files are read against the working directory; diagnose polls on the host's clock and `NightlyHost.Sleep`. What the script refused at exit 1 from the command line alone (no card, phrase, `--blocked-by`, `--board` or `--title` for new, a required reason with neither flag, a text and its file, a contradictory placement, an option on the wrong verb, `--priority`) is a usage error (2), and each verb refuses a flag it does not read. A failure is `BoardCli`'s line; times, `history --json` and `diagnose --json` print as the server wrote them; `unblock` matches the blocker ignoring case, as `-eq` did. The cases run in process with their row names (`CardCloseDeploymentPromptScriptTests`, `CardDiagnoseScriptTests` with a new timeout row on the fake clock, `CardFilePrivacyScriptTests`, `CardListSearchScriptTests`, `CardRelationshipScriptTests`, `RegisteredCallerScriptTests.Card_limits_sends_its_registered_caller_label`, `CardPlatformScriptTests`, `DelegateScriptVerificationRoundTests.C544_CardPolicyPatch`), with the new `CardCommandTests`; the opt-in `Antiphon.E2E` `CardCliE2ETests` runs the command in process against its real server. Dropped: `Block_404_detail_stays_intact_when_concise_view_wraps_at_120_columns` (pwsh ConciseView in a Linux pty; `Block_fails_loudly_when_relationships_returns_404` pins the plain lines), so its CARD-1323 rc exclusion row left the policy (resealed; `RcCoverageAdmissionTests` and `ReleaseGatePlatformTests` follow). `OrchestratorInvestigationDetector` and its hook twin recognise `scripts/antiphon card`, `scripts\antiphon.cmd card` and `antiphon-scripts card` beside `card.ps1`. The card-file index line and the schedule spawn warning name the command; so do AGENTS.md, the board-api bundle, the status-update skill, `TODO.md`'s header and the docs (agent-card-lifecycle, agent-kinds, antiphon-api, card-file-privacy, logs, ops-http, orchestration-loop, project-context, workflow-tracker-block); `tests/card-file-privacy-controls.py` PC-29 mutates `CardCli.cs`. Nothing starts the script by path, so nothing keeps a launcher | `7bcd33808` (pin `61434ddd4`) | 198 |
| 2026-11-24 | 28 delegate: `delegate.ps1` deleted; command `delegate` (`DelegateCli`: the verb first (`create`, `status`, `land`, `reply`, `continue`, `refine`, `finding`, `flag`, `reroute`, `list-areas`, `worktree-health`, `cleanup-verification`), then the task and, for reply and refine, the message; flags in kebab case; `--goal-file` and `--message-file` join `--authority-file`, because cmd.exe cannot carry a multi-line argument; `--env-override NAME=value[,...]` replaces the hashtable), in `BoardCommands`; the CLI passes `ANTIPHON_API`, `ANTIPHON_TASK_TOKEN`, `ANTIPHON_CAPABILITY`, the capability store, the operator token file, `ANTIPHON_VERSION_PROBE_TIMEOUT_SEC`, `X_LLM_PROJECT`/`X_LLM_KEY` and its working directory in (`DelegateCliEnvironment`). The caller label stays `delegate.ps1`; a capability opens with `CapabilityCli.UnprotectCurrentUser`. The script's named refusals keep their exit codes (1, or 2 for ceiling_invalid, workspace_switch_conflict, the runner, start-ref and base-override shapes and operator_token_unavailable); what PowerShell refused while binding is a usage error (2); a failed request prints the server's answer whole. The cases run in process: `DelegateScriptRunner` runs the command on the real HTTP transport against the existing loopback stubs and servers (`TaskCeilingScriptTests` left the slow allowlist and the spawn roster), the source-matching cases are behaviour on a recording transport (`DelegateScriptInterruptSwitchTests`, `DelegateScriptRunnerSwitchTests`, the kind and role allowlists), with the new `DelegateCommandTests`; `Antiphon.E2E` runs it in process (`DelegateCommand`) and the scratch repo forwards to this checkout's launchers. Dropped: the `-EnvOverride @{...}` hashtable form. The server texts that named the script, the client's CeilingHoldCard, the detector and its hook twin, AGENTS.md, the delegate, orchestrator, rc-qualification and status-update skills, the orchestrator and board-api bundles (orchestrator bundle re-pinned, 11,498 of 11,500) and the docs name the command; flag-only texts converted in `57d418dfd`. `lib/antiphon-caller.ps1` and `lib/runner-operator-token.ps1` keep their loaders. Nothing starts the script by path, so nothing keeps a launcher | `2d9ee9a90` (server texts `cbfa8ba23`, docs `1934cca82`) | 197 |
| 2026-11-24 | 29 checkpoint-task and distiller: `checkpoint-task.ps1`, `distiller.ps1` and `lib/antiphon-caller.ps1` (its last loader was `checkpoint-task.ps1`; `AntiphonCaller` is its twin) deleted; commands `checkpoint-task` (`CheckpointTask`: `--task-id`, `--push`, `--dry-run`; the caller label stays `checkpoint-task.ps1`, which `AgentTaskGetPollMetrics` meters; git runs on the host's launcher with `NightlyNativeGit`'s command line, so the porcelain status is read from stdout alone and the add, commit and push lines pass through) and `distiller` (`DistillerLedger`: `--stats`, or `--list` with `--flagged`, and `--since`, whose day count is read against the host's clock), flags in kebab case, in `AgentHelperCommands`; the CLI passes `ANTIPHON_API` and `ANTIPHON_TASK_TOKEN` in. The script's exit codes stay (checkpoint-task: 0, 2 unresolvable, no usable directory or not a repository, 3 shared checkout, 1 a git step; distiller: 0, 1); what PowerShell refused while binding is a usage error (2). distiller now prints the cost: the script's `cost=${1}` expanded an unset variable and always printed `cost=`. The cases run in process: `CheckpointTaskScriptTests` keeps its five rows on a recording transport and real temp repositories, adds the worktree-over-caller (CARD-0217 S5), push with and without an upstream (S8), unresolvable-task and usage rows, and moves to the git lane (`GitProcessSpawnLimit`; its non-git directory is removed with `ReadOnlyDirectory.Delete`, so its cleanup-guard exception row is gone); `RegisteredCallerScriptTests.Checkpoint_task_sends_its_registered_caller_label` runs the command; the new `DistillerCommandTests`. Dropped: `RegisteredCallerScriptTests.Unregistered_caller_label_is_refused_and_not_echoed` (its subject is the deleted helper; `The_compiled_caller_list_is_the_server_list_and_refuses_without_echo` pins `AntiphonCaller` on the same rows). `docs/antiphon-api.md`, `docs/logs.md`, `docs/ops-http.md`, `docs/orchestration-loop.md`, `docs/session-runtime-invariants.md` and the server's label comment name the commands; the ops-http label table names `delegate.ps1` again (unit 28's text pass had replaced the stored label with the command). Nothing starts either script by path, so nothing keeps a launcher | `1a33fb15c` | 194 |
| 2026-11-24 | 30 SessionRunner restart chain: `restart-session-runner.ps1` and its library `session-runner-restart-health.ps1` deleted; command `restart-session-runner` (`RunnerRestart.Run`: `--hard`, `--kill-sessions`, `--wait-only`, `--timeout-sec`; the toolchain floor warnings run in process) on the production platform `RunnerRestartOs` (the PID census and its refusals, the controls, the /health and /capabilities probes, a process's start and image path from a limited-query handle and the 17204 owner from the TCP owner table; the tree stop is the host's qualification seam, the logon task's start `schtasks /Run` on the host's launcher, files `IFileSystem`). `run-daemon.ps1` (the AppHost's `DaemonProcessService` starts it by path, detached; `autostart-session-runner.ps1` calls it) and `check-daemon-build.ps1` (`restart-apphost.ps1` and `dev-aspire.ps1` run it by path) are thin 5.1 launchers of the new commands `run-daemon` (`DaemonSupervisor`: build through `build-slot.ps1` with the HEAD stamp, the built executable under `cmd.exe /s /c ... >> log`, rotation at relaunch, the desired state; the launcher passes its own PID as `--supervisor-pid`, so the startup records keep naming the supervisor the AppHost and the logon task record, and supervision ends once that process is gone) and `check-daemon-build` (`DaemonBuildCheck`, `--fail-on-stale`). The root `restart-session-runner.ps1` calls the command (its bound parameters only); `restart.ps1 -RestartRunner` calls that forwarder. `autostart-session-runner.ps1` is unchanged: its plan is `lib/windows-phone-home-env.ps1`, which four other scripts and `test-windows-phone-home-runner.ps1` use (their own unit). The cases run in process in Antiphon.Tests (`RunnerRestartHealthTests`, `RunnerMilestoneReaderTests`, `RunnerRestartOsTests`, `DaemonSupervisorTests` with one Windows row on a real `.cmd` service, `DaemonBuildCheckTests`; the launchers and their 5.1/pwsh parse in `AntiphonCliLauncherTests`); the SessionRunner.Tests restart classes, `DaemonStartupDiagnosticsTests`, `DaemonLogRotationTests` and the `RestartFixture` AST-preflight fixture (with `platform.ps1`) are deleted. Dropped: the preflight cache and shell identity rows, the call-operator scope witness, the helper-global checks and the build-slot stub boundary row (PowerShell fixture mechanics). The server's capability refusals, the card close prompt, AGENTS.md, the orchestrator, cli-upgrade and rc-qualification skills, the phone-home refusal and the docs name the commands; `test-source-revision-stamp.ps1` drops its run-daemon row | `12bbc3c69` (rc rows `0087a44b0`) | 191 |
| 2026-11-24 | 32 client scripts: `client-mode.ps1`, `lint-client.ps1`, `typecheck-client.ps1`, `lib/client-npm-install.ps1` and the driver `test-client-mode.ps1` deleted; commands `client-mode` (`ClientMode.Run`: `--mode built\|dev`, `--status`, `--rebuild`, `--timeout-sec`, `--port`; the mode file and the rebuild sentinel on `IFileSystem`, the probe on `NightlyReport.HttpTransport` (5 s), the wait on `NightlyHost.Sleep`), `lint-client` (`ClientLint`, `--path`), `typecheck-client` (`ClientTypecheck`, `--project`, run from the checkout root) and `test-client` (`ClientTests`: `--json-result-path`, `--typecheck-project`, `--lint-path`, `--repo-root`; every other argument is a Vitest filter or flag), in `ClientCommands`. node and npm run on the new `ClientToolRunner` seam (production `ClientTools.RunProcess`; each gate's lines teed to its `logs/client-*.log`), and the shared install is `ClientNpmInstall` (`logs/client-npm-ci.lock`, 2 s retries for 15 min, a second look under the lock, `npm ci` or `npm install`, npm through ComSpec as the nightly's prerequisite). The nightly's client chunk runs `dotnet exec <cli> test-client --repo-root <clone> --json-result-path <json>` (`NightlyChunkContext.CliPath`, blank: the entry assembly) instead of `pwsh -File scripts/test-client.ps1`, so the suite's driver is the running CLI's, not the clone's; the c487 harness fake, `FakeTestHost` and `NightlyTestsWorld` read `--json-result-path`; the report's rerun line is `scripts/antiphon test-client <file>`. `test-client.ps1` stays as a thin 5.1 launcher (`scripts/c590-remote.sh`, the CARD-0590 nested-lane client case, runs it by path with `-JsonResultPath`, and its test-image check names it): `-JsonResultPath`, `-TypecheckProject` and `-LintPath` map to the flags, the rest passes through; its census and selection rows stay. Behaviour: client-mode's no-argument refusal (exit 1) is a usage error (2); the state file's times print as written (ConvertFrom-Json made them local dates); a Vitest that cannot start is exit 1 (the script reported the lint gate's 0); `--project` resolves against the checkout root, not the caller's directory. The cases run in process (`ClientModeTests`: the driver's rows, the wait, the rebuild and the status; `ClientChecksTests`: the gates, the suite, the install and the lock on the real file system; `TestClientFilterTests` pins the CARD-0307 argv on the recording runner, its real-Vitest run dropped; the launcher's in `AntiphonCliLauncherTests`). The driver's census rows left both policies (resealed; the frozen `scripts/fixtures/c599-policy-v2.json` keeps them). AGENTS.md, the AppHost's and serve.mjs's comments, `tests/card-file-privacy-controls.py`, `docs/apphost-runbook.md`, `docs/bootstrap.md`, `docs/orchestration-loop.md` and `docs/testing-and-build.md` name the commands | `e009c510b` (index `c0515a24c`, runner test `8304aec36`) | 186 |
| 2026-11-24 | 33 W10 drivers and tools: `test-expected-platform-skips.ps1`, `test-mechanical-pc-contract.ps1`, `test-scripts-lane-repairs.ps1`, `test-duration-tripwire.ps1`, `test-hooks.ps1`, `measure-session-state-cache.ps1` and `verify-agent-tui-profile.ps1` deleted; commands `test-duration-tripwire` (`DurationTripwire`: `--trx`, `--allowlist`, default the checkout's slow-test allowlist), `mechanical-pc` (`MechanicalPc`: the five check snippets of `docs/mechanical-pc-contract.md` as verbs `preflight`, `apply`, `oracle`, `restore` and `instantiate`, same `C479 ACCEPT`/`C479 REFUSE <reason>` lines and exits 0/2; the contract's fenced PowerShell is now a verb table, contract version 1 unchanged), `measure-session-state-cache` (`SessionStateCacheMeasure`: `--mode`, `--phase`, `--round`, `--evidence-root` and the rest in kebab case; the API, the process sample and the platform on `SessionStateCacheSeams`, docker and git on the host's launcher, 30 s and captured, the waits on `NightlyHost.Sleep`; the Windows-only capture, the context checks, the HEAD/version check and the CreateNew evidence stay) and `verify-agent-tui-profile` (`AgentTuiProfileSmoke`: the API and runner reads, the TUI's command line (the qualification seams' reader) and the platform on `AgentTuiProfileSmokeSeams`), in `VerificationToolCommands`; the CLI passes `ANTIPHON_API` and `ANTIPHON_TASK_TOKEN` in. Behaviour: an unreadable mechanical-pc input is exit 1 with one line (the snippets threw), a missing member, closure or edit file is that check's refusal, an empty context is `patch-unresolved`, and `instantiate` refuses a blank or `..` attempt name (`missing-value`, `path-not-contained`) instead of throwing; the tripwire's numbers and durations are invariant culture; the smoke prints ASCII and a canary leaked during the reply poll fails at once (the script's retry swallowed it). The cases run in process: `ExpectedPlatformSkipRosterTests` (the 23 EPS rows on `NightlyChunkVerdict`, `NightlyTrx`, `NightlyExpectedSkips`, `SuiteCompletionParts`), `MechanicalPcRehearsalTests` (the 38 C479 rows on real Git fixtures, plus usage and unreadable rows; `MechanicalPcContractTests` pins the verb table), `ScriptsLaneRepairTests` (origin on `NightlyRun.OriginUrl`; state compiles `CheckpointNativeIo.Move` with Roslyn over the modeled File facade, as the Add-Type did), `TestDurationTripwireTests` and `TestClassificationPolicyTests` on `DurationTripwire`, `MeasureSessionStateCacheScriptTests` (the comparisons, the SQL classifier on `StatisticsSql`, and capture on a fake desktop) and `AgentTuiSmokeScriptTests` (behaviour on fakes and a MockFileSystem); the CARD-0247 node suite `test-hooks.ps1` ran is `OrchestratorInvestigationHookSuiteTests`. Dropped: the smoke script's pwsh parse check and source-text rows (behaviour rows replace them). The census rows `test-duration-tripwire` and `test-hooks` left both policies (resealed, live-lane pin moved; the frozen `scripts/fixtures/c599-policy-v2.json` keeps them). `Get-NightlyOriginUrl` in `lib/nightly-common.ps1` has no caller left and stays (that file is in `ReleaseAuthority.Scripts`). The docs (`docs/testing-and-build.md`, `docs/mechanical-pc-contract.md`, `docs/session-runtime-invariants.md`, `docs/ai-agent-tui-configuration.md`) name the commands. Nothing starts these scripts by path, so nothing keeps a launcher | `3a6f2ed0d` (index `60759205f`, follow-ups `7b7559805`, `b2ec471f0`, `8929269e2`) | 179 |
| 2026-11-24 | 34 build-slot cluster: `scripts/test-build-slot.ps1` (driver), `scripts/measure-linux-shared-compiler.ps1`, `lib/linux-compiler-evidence.ps1` (its only reader) and `fixtures/c589-command-shim.ps1` (only the driver and the removed `C589_COMMAND_SHIM` seam used it) deleted; commands `build-slot` (`BuildSlot`: `--label`, `--slot-wait-minutes`, `--no-slot`, `--holder-pid`, then `--` and the command verbatim; the broker client `BuildSlotBroker` on `NightlyReportHttp` (10 s, no proxy), the clock, sleep, process table, environment and file system on `BuildSlotHost`, children on `BuildSlotProcesses` with the CLI-bound `BuildDriverEnvironment` (CARD-1506) and `LeaseTempDirectory` (CARD-1981; `Antiphon.Scripts.csproj` now compiles it, one line); lines, rules, grace and exits as the script, plus 1 for a command that does not resolve or start and 130 when Ctrl+C or SIGTERM interrupts) and `measure-linux-shared-compiler` (`LinuxCompilerMeasure`, the toolset through `ILinuxCompilerToolset`; Linux only, the platform refused first), in `BuildSlotCommands`. `scripts/build-slot.ps1` stays as a thin 5.1 launcher with its parameters and exit codes (agents, the delegate bundle, other checkouts and the server2 harnesses run it by path): it reads its raw command line under `-File` (only the file the process started with), flattens an in-process caller's arrays, passes its own PID as `--holder-pid` and hands the command over through `Invoke-AntiphonCli`'s new `-Verbatim` tail; agent texts keep naming it, because `scripts\antiphon.cmd` (cmd.exe) drops empty arguments and mangles `^ % & |`. The in-process callers run the slot without a child (`BuildSlotRunner`): run-daemon's rebuild (`DaemonSupervisor`, output captured into the service log), `qualification-launch` (its lease spans `nightly-run.ps1`) and the post-land lane (`settings.Slot`). Kept for owner-gated PowerShell: `lib/build-slot.ps1` shrinks to `Enter`/`Exit-AntiphonBuildSlot` for `run-checkpoint.ps1` (W5; `test-run-checkpoint.ps1`'s `C589_SLOT_SHIM` stays there), `lib/build-driver-environment.ps1` to `Set-AntiphonBuildDriverEnvironment` and `New-AntiphonLeaseTemp` (run-checkpoint, test-checkpoint-bounded-reader, `lib/nightly-owned-process.ps1`, `lib/c487-nightly-tests.ps1`); `run-tests-watched.ps1` takes the overrides from the C# directly. The cases run in process with their row names (`BuildSlotScriptTests`: a scripted broker and a recording child on one timeline with a fake clock; real children for literal argv, interruption and Windows PowerShell 5.1; the real `BuildSlotRoutes` in process), with the new `BuildSlotLauncherTests` (pwsh and 5.1 against cli-echo) and `LinuxCompilerMeasureTests`; `BuildSlotEndToEndTests` lets the launcher build the CLI once. Dropped: named-parameter binding and the exit codes of throw, binding errors and normal completion inside a pwsh `-File` child, and the raw pwsh module-path baseline (PowerShell mechanics). Open: `scripts/c590-remote.sh` (read-only here) bind-mounts only `build-slot.ps1` and the two libraries into its c849 containers, so its real server2 runs need the launcher's CLI there | `38b8f1d0c` (rc rows `d1910ed17`, index `942da9102`, renewal fix `c62a4696e`) | 175 |
| 2026-11-24 | 31 AppHost chain: `start-apphost.ps1` (no caller), `lib/apphost-fixture-closure.ps1` and the drivers `test-apphost-graceful-stop.ps1`, `test-apphost-lock-age.ps1`, `test-apphost-probe-class.ps1`, `test-apphost-git-index-lock.ps1`, `test-apphost-main-worktree-guard.ps1` and `test-longpaths-checkout.ps1` (its last two cases checked the guard driver) deleted; commands `restart-apphost` (`AppHostRestart`), `watchdog-apphost` (`AppHostWatchdog`: the restart runs in its process, captured for the log), `set-apphost-maintenance` (`AppHostMaintenance`), `apphost-watchdog-state-observer` (`AppHostWatchdogStateObserver`), `autostart-apphost` (`AppHostAutostart`, `DevCompose`) and `verify-dev-stack` (`DevStackVerify`), flags in kebab case (`AppHostCommands`). The shared pieces (the launch and restart locks, the CARD-1527 dead-launch proof, the worktree guard, the git index lock, the probes, the graceful stop, the log verdicts, LIVE_TIMING) are `AppHostCommon.cs`; the machine is `IAppHostPlatform`, production `AppHostOs` (the TCP owner tables, a toolhelp census with command lines, the detached start and tree stop on the host's qualification seams, the Task Scheduler over COM, the Event Log over advapi32). Kept as thin 5.1 launchers: `restart-apphost.ps1` and `set-apphost-maintenance.ps1` (`cutover-postgres-server2.ps1` and `deploy-local.ps1` start them by path), `watchdog-apphost.ps1`, `apphost-watchdog-state-observer.ps1` and `autostart-apphost.ps1` (their Scheduled Tasks; `-RestartScript` is refused) and the root `verify-dev-stack.ps1` (deploy-local, `restart.ps1`, `bootstrap-check.ps1`). `apphost-common.ps1` keeps only the worktree guard (`dev-aspire.ps1` and `deploy-local.ps1` still run it; twin `AppHostWorktree`). The phone-home launch refusal is `WindowsPhoneHome.LaunchRefusal`, a temporary twin of `lib/windows-phone-home-env.ps1`'s until that library's unit (dev-aspire.ps1 and three other scripts load it). The launch census skips this process's launcher chain, not only itself (behind a launcher, and with the watchdog as the restart's parent, the two-fire dead-launch reclaim could never complete). The cases run in process (`AppHostRestartTests`, `AppHostCommonTests`, `AppHostWorktreeGuardTests`, `AppHostWatchdogTests`, `AppHostWatchdogStateObserverScriptTests`, `VerifyDevStackSmokeTests`; the launchers in `AntiphonCliLauncherTests`); `RestartApphostLockHintTests` is folded into `AppHostRestartTests`. Dropped: T-7 (ASCII and parse of the deleted files), T-13/T-14 (the driver's dot-source closure), `LP_GuardStderr` and `LP_PathArithmetic` (the guard test links a worktree of a fixture repository, not of this checkout) and the cutover driver's W-7 (it ran T-9, T-10, T-12). The driver census rows left both policies (resealed); `scripts/fixtures/c599-policy-v2.json` is a frozen release-gate fixture and keeps its rows, as after units 18 and 22. The server's shutdown and canonical-advance texts, the delegate texts, AGENTS.md, the orchestrator, run and start skills and the docs name the commands | `02373ad31` (index `fa2b7304a`, lane `3c037aaff`; integrated after unit 34) | 167 |
| 2026-11-24 | 35 W10 fixtures and tools: `scripts/check-os-witnesses.ps1`, `scripts/prune-test-data.ps1` and the test fixtures `tests/Antiphon.Tests/Scripts/Fixtures/directory-link.ps1`, `Scripts/Fixtures/script-harness-process.ps1`, `Agents/Fixtures/claude-effort-dialog.ps1` and `Agents/Fixtures/claude-effort-trace-contract.ps1` deleted, with `TestHelpers/DirectoryLinkCommand.cs`; commands `check-os-witnesses` (`OsWitnessCheck`: `--base-ref`, `--head-ref`, `--evidence-path`, `--plan`, `--repository`; git on its own seam, `OsWitnessGit`, 15 s per call, stdout read exactly; the same OBLIGATION, CHANGED, CLASSIFY, CANDIDATE and NATIVE lines and exits 0/1/2), in `VerificationToolCommands`, and `prune-test-data` (`TestDataPrune`: `--execute`, a repeatable `--match`, `--reason`, `--json`; dry run by default, archive never delete, boards then projects, the already-archived, agent, live-session and open-task skips and the agent-tasks envelope read kept; the CLI passes `ANTIPHON_API` and `ANTIPHON_TASK_TOKEN` in), in `OperatorCommands`. `DirectoryLink` makes the Windows junction in process (CreateDirectoryW, then FSCTL_SET_REPARSE_POINT with a mount point buffer, as mklink /J writes it), so it starts nothing; `ProcessSpawnLimitTests` lists only `ScriptHarnessProcess` as pwsh-backed (the three strict classes that used the link stay strict). The C806 fixture root and the CARD-0449/1516 effort peer are modes of the staged `Antiphon.ScriptHarnessHost` (`HarnessRoot`, `ClaudeEffortDialog`; no new project): `ScriptHarnessProcess.RootArguments` runs a `.dll` script as `dotnet <dll> -Case ...` (the Linux owner's helper applies the same rule; `.ps1` drivers are unchanged), and the PTY tests start `dotnet <host> claude-effort-dialog` or `claude-effort-deaf` (the generated `deaf.ps1` is gone too). Tests: `OsWitnessCheckTests` (56 rows on real Git fixtures, named after the retired `test-os-witnesses.ps1`), `TestDataPruneCommandTests` (a recording transport), `DirectoryLinkFixtureTests` and `DirectoryLinkFixtureWindowsTests` (in process), `ClaudeEffortFixtureTraceTests` (`dotnet <host> claude-effort-trace-contract`, the same four C1516 rows). Dropped: the strict-mode rows of `test-os-witnesses.ps1` (Set-StrictMode), and `DirectoryLinkFixtureTests`' C1061_Command_* rows and `C1061_Junction_command_joins_live_root_and_pipe_holders` (the owned pwsh junction child they pinned is gone). Behaviour: check-os-witnesses' four inputs are required flags (usage, exit 2, as the invalid-input exit was), the evidence version must be the number 1 and git output is read as UTF-8; prune-test-data's default reason names the command (`Test residue (prune-test-data)`), an invalid or blank `--match` is a usage error before any request (was a throw at first use), `-PassThru` is gone and JSON keys match exactly. Not run here: the Linux C806 rows (`ScriptHarnessLinuxOwnershipTests`, skipped on Windows) | `a0a7e1bf8` (index `031287081`) | 161 |
| 2026-11-24 | 36 phone-home and autostart installers: `start-windows-phone-home-runner.ps1`, `verify-phone-home-grok.ps1` (an operator harness; `docker-compose.runner-grok.yml` and the card0490 fixture only name it), `install-autostart.ps1`, `install-cleanup-task.ps1` and the driver `test-windows-phone-home-runner.ps1` deleted; commands `autostart-session-runner` (`SessionRunnerAutostart`: `--phone-home-config-path`, `--supervisor-pid`; the run-daemon supervisor runs in its process, the phone-home variables reach the runner only through the new `DaemonSupervisorOptions.ServiceEnvironment`), `start-windows-phone-home-runner` (`PhoneHomeRunnerStart`), `verify-phone-home-grok` (`PhoneHomeGrokVerify` on `IPhoneHomeGrokPlatform`: ports, processes, docker, HTTP; the server2 firewall rule through netsh), `install-autostart` and `install-cleanup-task` (`AutostartInstall` on `IScheduledTaskRegistry`, production `ScheduledTaskRegistry`: the Task Scheduler over COM), flags in kebab case (`PhoneHomeCommands`). The library's rules are `WindowsPhoneHome` (`ParseConfig`, `RunnerEnvironment`, `AutostartPlan`, `RootRefusal`, `ChildPath`; one parser, System.Text.Json, comments and trailing commas read as pwsh 7 read them, a case-variant key malformed). `autostart-session-runner.ps1` stays a thin 5.1 launcher (the logon task "Antiphon Session Runner" and the phase 1 clone start it by path; its PID is the recorded supervisor; `-PhoneHomeConfigPath` passes only when given). `lib/windows-phone-home-env.ps1` keeps `Get-WindowsPhoneHomeLaunchRefusal` (dev-aspire.ps1) and `Join-PhoneHomeChild` (desktop-win-phase1-secrets.ps1), twins until those callers move. The installers register the same task names, actions (the kept launchers by path under the same PowerShell host rule), triggers and settings; runbook G-1 now holds for the install's compose bring-up. The cases run in process (`C1744G3PhoneHomeSupervisionTests`, `PhoneHomeRunnerStartTests`, `VerifyPhoneHomeGrokScriptTests`, `AutostartInstallTests`, `ScheduledTaskRegistryTests` with one disabled throwaway task; the launcher in `AntiphonCliLauncherTests`). Dropped: autostart-helper-load, env-table.start-calls-helper and start-no-inline-table, the library's 5.1 AST row. `test-source-revision-stamp.ps1` drops its phone-home row; the run skill, the docs and bootstrap-check's advice name the commands | `f225330f6` (lane census `88eead40e`, rc owner `fbe5117d9`) | 156 |
| 2026-11-24 | 37 dev stack: `restart-client.ps1` and `assert-dev-ports-clear.ps1` (only `restart*.ps1` ran them), `scripts/check-daemon-build.ps1` (dev-aspire.ps1 was its last by-path caller), `lib/dev-postgres-compose.ps1` and `lib/live-startup-timing.ps1` (dev-aspire.ps1 was their only loader; their twins are `DevCompose` and `LiveStartupPhase`) and the driver `test-live-startup-timing.ps1` with its runner `StartupScriptTests` deleted; commands `dev-aspire` (`DevAspire.Run`: `--no-build`, `--no-browser`, `--allow-worktree`; the worktree guard `AppHostWorktree` and `WindowsPhoneHome.LaunchRefusal` refuse before the launch lock, exit 3) and its detached AppHost wrapper `run-apphost` (`DevAspire.RunAppHost`: the build stamped with HEAD under a build slot in its process, then `dotnet run --no-build`, both into `logs/apphost.log`), `dev-start`, `dev-stop`, `dev-fresh`, `dev-backup`, `dev-restore`, `dev-upgrade` (`DevStack`), `restart-dev-stack` (restart.ps1's body: the runner restart and verify-dev-stack in process), `restart-server` (the toolchain floor in process), `restart-client`, `stop-server` and `assert-dev-ports-clear` (`DevRestart`), flags in kebab case (`DevStackCommands`); the machine is `IAppHostPlatform` and `IDevStackSeams` (production `DevStackOs`: streamed children, the build slot, the browser, terminal tabs, the prompt). `IAppHostPlatform.StartDetached` became `StartCli` (this CLI from its run copy under `bin-launcher`, `AppHostCli.RunCopy`, so the long-lived wrapper never holds the build output) and `RunAppending` went: `restart-apphost` starts the `dev-aspire` command detached, `autostart-apphost` runs `DevAspire.Run` in its process (its output into its log). Kept as thin 5.1 root launchers, because the runbooks name them: `dev-aspire.ps1`, `dev-start.ps1`, `dev-stop.ps1`, `dev-fresh.ps1`, `dev-backup.ps1`, `dev-restore.ps1`, `dev-upgrade.ps1`, `restart.ps1`, `restart-server.ps1` and `stop-server.ps1`; `scripts/apphost-common.ps1` keeps the worktree guard for `deploy-local.ps1` alone (W8, owner-gated, unchanged). The cases run in process (`DevAspireTests`, `DevStackTests`, `DevRestartTests`, `AppHostWorktreeGuardTests` with real git; the launchers in `AntiphonCliLauncherTests`); `test-source-revision-stamp.ps1` no longer audits the four ported surfaces (C# pins their stamped argv) and `ToolchainFloorWarningTests` runs the server restart in process. Dropped: the C1564 value and exception pass-through rows (Invoke-LiveStartupPhase's script block; no C# caller has one). `C1744G3PhoneHomeSupervisionTests`' aspire-order row still reads dev-aspire.ps1's PowerShell until the phone-home unit (36) replaces its driver. The run and start skills, bootstrap, the AppHost runbook, testing-and-build and release-gates name the commands | `426afe0a8` (integrated after unit 36; guard order and refusal twin `3dcd125dc`) | 150 |
| 2026-11-24 | 39 test drivers and fixtures: `test-source-revision-stamp.ps1`, `fixtures/c487-readonly-windows-witness.ps1`, `test-card0490-native.ps1` and `build-card0490-bootdisk.ps1` deleted. The SourceRevisionId census is `SourceRevisionStampAudit` in the core, read with the core's `PsReader` (the test process cannot load System.Management.Automation's parser), over the same surfaces with the driver's rules and decoys; after `--` PowerShell splits an unquoted `-p:x`, so a dash-led expandable string is not the stamp; the empty `$expected` table and its HEAD checks are gone (the stamped builds are C#). The c487 witness runs in process against the C# twin of `Clear-C487OwnedResultReadOnly`, `NightlyFixture.ClearOwnedReadOnly` (the walk `Dispose` runs), on the same fake Windows tree plus a disk row; `c487-harness.ps1` stays for the W2/W5 drivers, and its PowerShell function has no witness now. The native host checks the D-12 invocation contract itself (`NativeInputPolicy.Admit`: required values, the PC and phase sets, `-Ordinary` or `-BindingFile`, exit 3 or 2) and is run with `dotnet run --project`. Command `build-card0490-bootdisk` (`Card0490BootDisk`: `--qemu-dir` or `CARD0490_QEMU_DIR`, `--output-path`, `--lock-file`, `--virtual-size-mib`, `--smoke`, `--write-lock`; qemu-img on the host's launcher, the QEMU boot a seam); `Card0490BootDiskTests` pins the sector and raw image to the script's bytes and never starts QEMU; the lock's `bootDisk.source` names the command. Kept: `fixtures/c913-image-wrapper.ps1`, whose subject is the W8 `verify-card0660-codex-image.ps1` (it fakes docker and git around that script); it goes when that script is ported | `dea29bb89` (card0490 `bce98b8e7`, index and row `a92769862`) | 146 |
| 2026-11-24 | 38 operator tools: `scripts/bootstrap-check.ps1`, `scripts/reap-orphaned-pty-hosts.ps1` and `scripts/run-tests-watched.ps1` deleted (nothing starts them by path, so no launcher); commands `bootstrap-check` (`BootstrapCheck` on `IBootstrapCheckPlatform`, production `BootstrapCheckOs`: `--no-stack`; the same Phase A checks, lines (`  [PASS|WARN|FAIL] <name> <detail>`, each remedy indented) and exit code (the FAIL count); the agent context (`AgentContextCheck`) and `verify-dev-stack --skip-browser` (on `AppHostOs`) run in its process instead of through `Invoke-AntiphonCli` and a pwsh child; the card-file read is the same one GET of `<ANTIPHON_API>/api/projects`, never a repair; the `--no-stack` network create and remove stay the only side effect; JSON keys match exactly, so a case-variant overlay key no longer counts as a provider key), in `DevStackCommands`; `reap-orphaned-pty-hosts` (`PtyHostReaper` on `IPtyHostReaperSeams`: the runner's GET and kill, `docker exec psql`, the Win32 process table, `ZombieAgentReaper.Win32Processes`, now internal, and liveness; R1-R8 unchanged, a dry run unless `--execute`, a database row protects, a kill only through the runner, `--limit` oldest first; the manifests are read as `PtyHostManifest` JSON with a GUID session id, so the SQL list is safe; a missing manifest directory, an unreadable manifest or an unreadable process table abort with exit 2 before any kill, where the script died with exit 1), in `SessionReaperCommands`; and `run-tests-watched` (`WatchedTestRun` on `IWatchedTestRunSeams`, production `WatchedTestRunOs`: `--exe`, `--filter`, `--tag`, `--out-dir`, `--hard-timeout-sec`, `--quiet-sec`, `--cpu-floor`, `--sample-sec`, `--detailed`, `--env-vars`; the same samples, stall rule, diagnostics, files and exit 124; the CLI binds the build-driver policy, CARD-1506, on the child's start; without a process table the exe itself is still killed; an exe that does not start is one line and exit 1, where the script looped to its hard timeout), in `VerificationToolCommands`. `regenerate-derived-impact-index.ps1` stays as it is: its builder (`tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs`) needs Roslyn and `Antiphon.TestSupport`, which `antiphon-scripts` does not reference (a project-file change, and every CLI call would carry Roslyn), and a launcher in front of it would put the CLI's own build before every `-NoBuild` run, the server's land regeneration (`DerivedImpactIndexRegen`, 180 s under one build-slot hold) included. Tests: `BootstrapCheckTests` (25), `PtyHostReaperTests` (14) and `WatchedTestRunTests` (5) in process on fakes; `CardFileBootstrapCheckTests` keeps its three V-30 rows on a loopback stub, now through `BootstrapCheck.CardFiles` and the production HTTP read instead of the script's AST in pwsh; `AgentContextCheckTests` pins the in-process call. Dropped: nothing (none of the three had a driver). Behaviour: no colours; the stack check's refusals for a missing `verify-dev-stack.ps1` or pwsh went with the child; `bootstrap-check` needs the .NET SDK (without it the launcher exits 9 before any check). Docs: bootstrap, the run skill, testing-and-build, ops-http | this change | 147 |

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


