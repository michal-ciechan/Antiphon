# Testing and build operations


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #18

- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. **A plan's verify step names the test class(es) the change touched** — `--treenode-filter "/*/*/AttentionServiceTests/*"` — not a namespace. The namespace-wide form (`/*/Antiphon.Tests.Application/*/*`) is for a genuinely cross-cutting change, or for chunking a full `Antiphon.Tests` run into foreground-sized windows (Gotcha #74 has the full-run timing); it is not the default for one service plus its own test file. CARD-0239 is the evidence: a one-service AttentionService change was verified with the 2349-test Application filter (~26 minutes), which surfaced 66 pre-existing CARD-0297 failures that then had to be triaged for nothing — the class filter would have been seconds (CARD-0307). Gotcha #74's full-suite quiet phase is a fact about the full run; it does not license namespace-wide verify on a narrow card. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group. Process-spawning tests (pty, pwsh probe, fakeclaude/fakegrok, session runtime) also carry `[ParallelLimiter<ProcessSpawnLimit>]` so at most one runs at a time per assembly — CARD-0050 S5; a new class that starts a child must take the same attribute. Do not co-schedule `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`: the in-process lane cannot cap the other exe, and even `--maximum-parallel-tests 2` on `Antiphon.Tests` still left FakeClaude rotating under a concurrent pair. Run the two projects one after the other.

### Preserved Gotcha #19

- **Client vitest suite: run it via `pwsh -File scripts/test-client.ps1`, and NEVER read a Bash pipeline's exit code as the verdict** (CARD-0069). In Bash, `npm test 2>&1 | tail -50` reports **tail's** exit code — always 0 — which is exactly how a merge reported "Exit code 0" over six failing tests; vitest itself propagates exit 1 correctly in every direct invocation (measured 2026-08-19). The wrapper tees full output to `logs/client-tests.log` and prints an unmissable `CLIENT TESTS EXIT CODE: n` line that survives any output capping. The suite has ONE test budget — `testTimeout: 20_000` in `client/vite.config.ts` — replacing 14 per-file `vi.setConfig` overrides; do not add per-file timeout overrides (that is how the .NET flake cast became furniture), and a test that needs more than the global budget is a test to make cheaper, not a deadline to widen. A failing client test is only "flaky" after an isolation re-run (`pwsh -File scripts/test-client.ps1 <File>`) actually passes — say which re-run you did when reporting it. See `docs/superpowers/plans/2026-08-19-card-0069-client-flake-cast-plan.md` for the measured evidence. **The wrapper's filter really does pass through** (`pwsh -File scripts/test-client.ps1 attentionVisuals.test` runs one file, ~15–35 s) — since CARD-0307 it runs `node client/node_modules/vitest/vitest.mjs run @args`, never `npx vitest`. On Windows `npx` is the installer's `npx.ps1`, which rebuilds argv from the SOURCE TEXT of the calling line, so the literal `@args` reached Node and every scoped run silently became the whole 5–7 minute suite (that is most of why CARD-0239's execute ran 61 minutes against a 25-minute estimate). Do not write `npx vitest` in that script; `TestClientFilterTests` pins it, and a scoped run that takes minutes is the bug back.

### Preserved Gotcha #20

- **E2E browser tests serve `client/dist` — rebuild it or they mean nothing.** `UsePrebuiltFrontend = true` serves the last `npm run build` output, which nothing rebuilds automatically. A stale bundle made new UI tests fail for no visible reason (dist was a month old, 2026-08-08) and would let a test of *removed* UI keep passing. `AntiphonAppFixture.EnsureClientBundleIsCurrent` now hard-fails when any `client/src` file is newer than `client/dist/index.html`, naming the file — run `npm run build` in `client/`.

### Preserved Gotcha #21

- **Diagnosing a failing E2E test: read `tests/Antiphon.E2E/TestOutput/Logs/<TestName>/`, not stdout.** Wire a test up with `TestDiagnostics.For(context.Metadata.TestName)` in `[Before(Test)]`, set `_appFixture.DiagnosticsDirectory`, call `_diagnostics.Attach(page)` after `NewPageAsync()` and `await _diagnostics.CompleteAsync(page, passed)` in the `finally`. You get `antiphon-<date>.log` (that test's server log alone), `browser.log` (console + page errors + failed requests + 4xx/5xx responses), `page.html` (DOM at the moment of failure) and `notes.log`. Setting `DiagnosticsDirectory` also turns the server's console sink down to Warning (`Serilog:ConsoleMinimumLevel`) so the assertion isn't buried under ~1500 lines of the run's own log. `TestOutput/` is gitignored; screenshots stay under `TestOutput/Screenshots/<TestName>/`.

### Preserved Gotcha #24

- **Integration tests share one Postgres — never assert on a global count.** `TestDbFixture` starts ONE `postgres:16-alpine` testcontainer per assembly run (`antiphon_test`) and every test in `Antiphon.Tests` shares it — it is not the dev database on 17280, but it is just as shared. `[NotInParallel("X")]` only serialises tests in the *same* group, so other groups are writing rows throughout. An assertion over an unscoped query (`supervisor.TickAsync().ShouldBe(1)`, `channels.ShouldHaveSingleItem()`, `.ShouldBeEmpty()`) silently also asserts "no other test has data right now" and fails at random. Scope every assertion to the row the test made (`s.AgentId == agent.Id`, `c.ExternalId == h.ChatId`); use `ShouldBeGreaterThanOrEqualTo` where the count is a sweep total. Three separate "flaky" tests were this. **A test that drives a global sweep needs `[NotInParallel]` with NO group key** — `AgentSupervisionTests` had a key, which serialised it only against itself while other suites' supervisors ticked concurrently; scoping the assertions was not enough on its own.

### Preserved Gotcha #26

- **A test host that boots the real `Program` launches the check interpreter on the PRODUCTION session-runner unless told not to — and nothing ever kills it** (CARD-0204, measured 2026-08-25: 190 of 201 live `Antiphon.PtyHost.exe` had no `AgentSessions` row, ~4 GB resident, oldest 12 days). `WebApplicationFactory<Program>` runs every hosted service on top of the real `server/appsettings.json`, so `AgentTaskCheckHostedService` → `CheckInterpreterProvisioner.EnsureAsync` provisions the AlwaysOn `antiphon-check-interpreter` in `C:\logs\antiphon\check-interpreter` and starts it AT ONCE through `SessionRunner:BaseUrl` = 17204; with `AntiphonWebAppFactory`'s `test-raw` definition that is a detached pty-host holding an interactive `cmd.exe`, whose 24 h linger clock never starts because the child never exits, while the session row lives in the throwaway test schema. One `HealthEndpointTests` run reproduced it. The diagnose seat (CARD-0352) and output-distiller seat (CARD-0330) are the same leak on a second and third AlwaysOn slug. `ProductionRunnerGuard` (`[Before(Assembly)]`) now sets `SessionRunner__BaseUrl=http://127.0.0.1:1`, `Delegation__CheckInterpreterEnabled=false`, `Delegation__DiagnoseEnabled=false`, and `Delegation__OutputDistillerEnabled=false` for every `Program` boot in `Antiphon.Tests`, and `AntiphonWebAppFactory` additionally swaps `ISessionRunnerClient` for `RefusingSessionRunnerClient` (a launch is a loud exception, recorded). **A new test host that boots `Program` outside this assembly must do the same, or start its own runner the way `Antiphon.E2E`'s `IsolatedSessionRunner` does (CARD-0102).** The reconciler will never reap these (`unclaimed never implies kill`, CARD-0056); `pwsh -File scripts/reap-orphaned-pty-hosts.ps1` (dry run by default, `-Execute` to kill) is the operator's census-and-reap, and it kills only a host with no DB row AND a recognised test-launch shape AND a banner-only ansi log AND a live child matching that shape, through the runner's kill endpoint.

### Preserved Gotcha #30

- **Building while daemons run**: the always-on session-runner (and dev server) lock their `bin/` outputs. To build/test without restarting them, use an alternate output path: `dotnet run --project tests/<X> --property:OutputPath=bin-ptyhost/` (gitignored by `bin-*/`). **End it with a forward slash, never a backslash.** `'--property:OutputPath=bin-x\'` loses its trailing backslash to Windows argv quoting, and the mangled value creates junk directories — `bin-x --treenode-filter`, `bin-check --nologo`, and worst of all `bin-profile ` *with a trailing space* (see the next bullet — that one breaks the whole repo's build). `OutputPath` applies to every project in the graph, so one run drops a `bin-<name>/` in ~12 directories; delete them afterwards (`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-<name> | Remove-Item -Recurse -Force`). Process-spawning tests share a 1-wide `ProcessSpawnLimit` lane (CARD-0050 S5); a failure there is a real defect unless it also fails at the base commit (stash and re-run).

### Preserved Gotcha #31

- **A build directory whose name ends in a SPACE breaks the ENTIRE build, with an error that names the wrong thing** (live miss 2026-08-10). Symptom, on projects you did not touch:

  ```
  error MSB3552: Resource file "**/*.resx" cannot be found.
  ```

  The repo contains **zero** `.resx` files, so the message is pure misdirection. Mechanism: `Directory.Build.props` excludes `bin-profile*/**` from the default item globs; MSBuild enumerates the match, Win32 path normalization strips the trailing space, the path fails to resolve, the glob crawl aborts, and `**/*.resx` survives as an unexpanded literal — which is what MSB3552 actually reports. It fires only on *leaf* projects that have no clean `bin-profile` sibling (it was `Antiphon.PtyHost.Protocol` and `Antiphon.SessionRunner.Contracts`); everything downstream is skipped, so the two named projects look arbitrary and unrelated to your change. **It is not the `--property:OutputPath=` flag** — it reproduces on a plain `dotnet build <leafproject>`, which is the quickest way to confirm you are looking at this and not at your own edit. `.gitignore`'s `bin-*/` hides these from `git status`, so they accumulate invisibly. Find them:

  ```powershell
  Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory | Where-Object { $_.Name -match '\s$' }
  ```

  Normal path APIs cannot open them (`Rename-Item` reports "does not exist"), so delete via the `\\?\` prefix: `[System.IO.Directory]::Delete("\\?\C:\src\Antiphon\src\<Project>\bin-profile ", $true)`.

### Preserved Gotcha #73

- **A test harness that registers a frozen-clock `TimeProvider` for the whole server graph hangs the test process forever, at ~0 CPU, on whichever run wins a race** (CARD-0222; `docs/investigations/2026-08-28-card-0222-antiphon-tests-hang.md`): `SessionMessageQueueService` has six poll loops of the shape `deadline = UtcNow() + N; while (…) await Task.Delay(poll, _timeProvider, ct)`. A private `MutableTimeProvider` whose `GetUtcNow()` moves only on `Advance()` — but which inherits the base `CreateTimer`, so the delays run in REAL time — makes the deadline unreachable once the loop is entered, and it is entered whenever the runtime already holds `LastSequence` for the session. `HerdrAlwaysOnChannelParityTests` hung 4/4 on both arms (dumpasync leaf `SettlePostEvidenceAsync → Task+DelayPromise`), and `AgentSupervisionTests` carries the same provider. Rule: a fake clock handed to the queue must be an **offset over the real clock** (`GetUtcNow() => DateTimeOffset.UtcNow + _offset`) or a `FakeTimeProvider` with `AutoAdvanceAmount`; never a frozen instant. Diagnose any "test process alive, no CPU, no output" with `dotnet-dump collect -p <pid>` then `dotnet-dump analyze <dmp> -c dumpasync` — thread stacks show nothing for an async wedge; the chain names the method and the await.

### Preserved Gotcha #74

- **A full `Antiphon.Tests` run is ~25.5 minutes** (CARD-0110 re-measure 2026-09-03, 3893 tests, after S2 migrate-once and CARD-0238 connection-exhaustion fix; was ~28 min on 2026-08-29). The "last 15–18 minutes are the sequential tail" claim is **pre-S2** (CARD-0165): the biggest `[NotInParallel]` classes now do ~21–24 s of real work. The remaining serial pole is the 1-wide `ProcessSpawnLimit` lane (~9–10 min). TUnit still runs global `[NotInParallel]` last, and `--output Normal` prints nothing for passing tests, so a long quiet stretch is not a hang. Do not kill a full run before ~35 minutes; use `--output Detailed` or `pwsh -File scripts/run-tests-watched.ps1 -Exe <bin-x>/Antiphon.Tests.exe -Detailed`. Postgres `53300`/`53200` exhaustion is fixed (CARD-0238). **The local foreground loop is the Unit lane, not the full assembly** (CARD-0110 S7′): `--treenode-filter "/*/*/*/*[Category=Unit]"` — a category predicate works; it is not an OR.

### Preserved Gotcha #75

- **A `dotnet build` that sits for 20+ minutes at near-zero CPU is probably reading `obj/…/*.FileListAbsolute.txt`, not hung** (CARD-0222, same doc): every `--property:OutputPath=bin-<name>/` build shares the project's one `obj/` and appends to that ledger, `IncrementalClean` reads/filters/rewrites it on every build and prunes only entries under the CURRENT `OutDir`, so it grew to 228 MB / 770,706 lines for `Antiphon.SessionRunner` and 97 MB for `Antiphon.Tests` (nested `bin-X\bin-Y\…` trees from before CARD-0110's exclude). Measured: 157 s of a 181 s Tests build in `ReadLinesFromFile`+`FindUnderPath`; full graph 21m31s → 1m28s after a reset. `Directory.Build.targets` now deletes a ledger over 2 MB (`AntiphonCleanFileMaxBytes`, `0` to disable) with a warning naming the card. The tell from outside: the outer `dotnet` process has ~1 s of CPU, one MSBuild node ticks at ~10 % of a core in `FindUnderPath` (`dotnet-stack report -p <node>`), and no `Antiphon.Tests.exe` has been spawned yet — there is never a `testhost.exe` under the Microsoft.Testing.Platform runner.
<!-- CARD-0254 preserved source ends -->

## Fast lane (CARD-0110)

The local default verification loop is the Unit category, not the full ~25.5 min assembly. A `[Category=X]` predicate works in `--treenode-filter` (measured; a single category is not an OR).

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c110/ -- --treenode-filter "/*/*/*/*[Category=Unit]"
```

CI / nightly keep the full run. After a full run that wrote a TRX (`--report-trx --report-trx-filename run.trx`), check for new ≥5 s tests:

```
pwsh -File scripts/test-duration-tripwire.ps1 -Trx path\to\run.trx
```

The allowlist is `tests/Antiphon.Tests/slow-tests-allowlist.txt`. Every test class is tagged `Unit` xor `Integration` (`TestLaneCategoryGuardTests`).

## Hand-built ServiceCollections and the delegation worktree graph

- **A test harness that builds its own `ServiceCollection` and resolves `AgentTaskDispatcher`, `DelegationWorktreeService`, `AgentTaskReplyService`, `DelegateBindRefusalRecovery` or `AgentReviewCheckpointService` registers the git graph through `DelegationTestServices` (`tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`), never by hand** (CARD-0297). `services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = … })` is the one registration for `IOptions<GitSettings>`, the real `WorktreeManager` and `GitService`, `GitWorkspaceService` and the scoped `DelegationWorktreeService`; `services.AddGitWorkspaceService()` is the reply-only / card-service form. Both are `TryAdd`, so a harness that already holds a fake `IWorktreeManager` (as `BridgeQueueHarness` does) keeps it, and calling the helper twice is a no-op. Do not add `AddSingleton<GitWorkspaceService>()` next to a local `CreateDispatchHarness`, and do not register `GitSettings` separately when the helper is called — pass it in. The helper assumes `AddLogging()` and a `TimeProvider` are already registered, which every dispatcher harness has. Evidence: when `DelegationWorktreeService` gained a `GitWorkspaceService` constructor dependency (c4d7e0d, 2026-08-26), eight copied harnesses went red at `GetRequiredService<AgentTaskDispatcher>()` with `No service for type 'GitWorkspaceService' has been registered` — `PinnedAgentKindTests.T3` was the one that got noticed — and seventeen more each grew their own one-liner with a CARD-0230 comment. `DelegationTestServicesTests` pins the contract: logging + a clock + the helper resolve the whole graph, and a prior one-liner or fake is not duplicated. `DelegationHarnessCensusTests` fails a new dispatcher or reply harness that skips the helper, and names the file (CARD-0244).
- Seeded settlement turns must end with `DelegationReportFormatter.ReportToken(id, "done")` unless the test is about the nudge.

## Nightly

The overnight job is `scripts/nightly-run.ps1`, invoked by Windmill
(`u/lndcobra/antiphon_nightly_tests`, 00:30 Europe/London). Do not add a local
Windows Scheduled Task.

It syncs an **isolated** clone at `C:\Antiphon\nightly\checkout` to
`origin/master` (never `C:\src\Antiphon`, never a worktree), builds (`npm ci`,
`npm run build`, `dotnet build Antiphon.sln`), then runs `Antiphon.Tests`,
`Antiphon.Agents.Pty.Tests` and the client vitest suite sequentially. Logs live
at `C:\Antiphon\nightly\logs\<yyyy-MM-dd-HHmm>\` (`summary.json`, per-suite
logs, `build.log`). `C:\Antiphon\nightly\last-run.json` is written every run.

On red it files or updates **one** Antiphon-board card labelled `nightly`
(plus `build` or `tests`). A second red night patches that card; it does not
open another. Green auto-closes the card only when it is still in Backlog and
unassigned; otherwise it posts a discussion line and leaves the card where a
human or agent put it. A missing morning card is **not** evidence of green —
check `last-run.json` and the Windmill run list (the job can die before it
files).

Re-run one failing test from the card's "Re-run one:" line, typically:

```
tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.exe --treenode-filter "/*/*/ClassName/method_name"
pwsh -File scripts/test-client.ps1 BoardPage.test
```

The clone is already at the sha the card names; or pass that filter against any
checkout of the same commit. Headed tests and `Antiphon.E2E` stay off the
schedule (`-Suites e2e` is a manual opt-in).
