# Testing and build operations

CARD-1022 release A: the five PTY assembly guards clear inherited `ANTIPHON_PTY_BACKEND`;
the isolated default is ModernConPty on Windows (both staged files required) and UnixPty
elsewhere. A native Windows qualification with missing prerequisites fails. Explicit legacy
fixtures are retained for release B and excluded from A's nine-row checkpoint manifest.
Typed/paste parity peers explicitly use modern and choose unwrapped/wrapped encoding through
the shared helper; changing the backend alone does not preserve a typed-input experiment.

CARD-1049: `AmServiceDeployReadinessTests` uses native loopback and real curl on
Windows. The held, non-listening fixture admits raw curl 7/28 on Windows (7 on
Linux); accepted stalls remain exact 28 and HTTP 503 remains exact 22. Its
timeout-then-ready case distinguishes accepted requests from delayed refusal.
Git sh/sleep/cygpath are discovered in one installation's `usr/bin` from supplied
PATH/Git locations, and only child PATH includes them. Curl is resolved first on
the original PATH. Missing Git tools fail; do not edit machine PATH or use WSL.
Run the [plan's exact native lane rows](superpowers/plans/2026-10-04-card-1049-windows-readiness-retry-plan.md#checkpoints)
through the checkpoint tool; the production readiness deadlines/retries are unchanged.


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #18

- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. **A plan's verify step names the test class(es) the change touched** — `--treenode-filter "/*/*/AttentionServiceTests/*"` — not a namespace. The namespace-wide form (`/*/Antiphon.Tests.Application/*/*`) is for a genuinely cross-cutting change, or for chunking a full `Antiphon.Tests` run into foreground-sized windows (Gotcha #74 has the full-run timing); it is not the default for one service plus its own test file. CARD-0239 is the evidence: a one-service AttentionService change was verified with the 2349-test Application filter (~26 minutes), which surfaced 66 pre-existing CARD-0297 failures that then had to be triaged for nothing — the class filter would have been seconds (CARD-0307). Gotcha #74's full-suite quiet phase is a fact about the full run; it does not license namespace-wide verify on a narrow card. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group. `ClaudeTrustPromptCanaryTests` is the canary that must be re-run whenever Claude's trust dialog changes: `$env:ANTIPHON_HEADED_TESTS='1'; dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/ClaudeTrustPromptCanaryTests/*"` (CARD-0390 §4.4: `j` then Enter on modern ConPTY; inbox conhost does not deliver the Select binding). Process-spawning tests (pty, pwsh probe, fakeclaude/fakegrok, session runtime) also carry `[ParallelLimiter<ProcessSpawnLimit>]` so at most one runs at a time per assembly — CARD-0050 S5; a new class that starts a child must take the same attribute; the three `ProcessSpawnLimitTests` roster tests are floors, not censuses; a new limiter class need not be listed, a known spawner must stay listed. Do not co-schedule `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`: the in-process lane cannot cap the other exe, and even `--maximum-parallel-tests 2` on `Antiphon.Tests` still left FakeClaude rotating under a concurrent pair. Run the two projects one after the other.

### Preserved Gotcha #19

- **Client vitest suite: run it via `pwsh -File scripts/test-client.ps1`, and NEVER read a Bash pipeline's exit code as the verdict** (CARD-0069). In Bash, `npm test 2>&1 | tail -50` reports **tail's** exit code — always 0 — which is exactly how a merge reported "Exit code 0" over six failing tests; vitest itself propagates exit 1 correctly in every direct invocation (measured 2026-08-19). The wrapper tees full output to `logs/client-tests.log` and prints an unmissable `CLIENT TESTS EXIT CODE: n` line that survives any output capping. The suite has ONE test budget — `testTimeout: 20_000` in `client/vite.config.ts` — replacing 14 per-file `vi.setConfig` overrides; do not add per-file timeout overrides (that is how the .NET flake cast became furniture), and a test that needs more than the global budget is a test to make cheaper, not a deadline to widen. A failing client test is only "flaky" after an isolation re-run (`pwsh -File scripts/test-client.ps1 <File>`) actually passes — say which re-run you did when reporting it. See `docs/superpowers/plans/2026-08-19-card-0069-client-flake-cast-plan.md` for the measured evidence. **The wrapper's filter really does pass through** (`pwsh -File scripts/test-client.ps1 attentionVisuals.test` runs one file, ~15–35 s) — since CARD-0307 it runs `node client/node_modules/vitest/vitest.mjs run @args`, never `npx vitest`. On Windows `npx` is the installer's `npx.ps1`, which rebuilds argv from the SOURCE TEXT of the calling line, so the literal `@args` reached Node and every scoped run silently became the whole 5–7 minute suite (that is most of why CARD-0239's execute ran 61 minutes against a 25-minute estimate). Do not write `npx vitest` in that script; `TestClientFilterTests` pins it, and a scoped run that takes minutes is the bug back.

### Preserved Gotcha #20

- **E2E browser tests serve `client/dist` — rebuild it or they mean nothing.** `UsePrebuiltFrontend = true` serves the last `npm run build` output, which nothing rebuilds automatically. A stale bundle made new UI tests fail for no visible reason (dist was a month old, 2026-08-08) and would let a test of *removed* UI keep passing. `AntiphonAppFixture.EnsureClientBundleIsCurrent` now hard-fails when any `client/src` file is newer than `client/dist/index.html`, naming the file — run `npm run build` in `client/`.
- **Native Land delivery protocol waits use progress evidence** (CARD-0550). A protocol-bound wait has a 60-second quiet deadline that renews while `protocol-git-*.json` records grow, with a 600-second absolute cap. Its `wait-*.json` record names the evidence, elapsed time, before/after git counts, and whether it renewed. A timeout says `quiet 60s after <elapsed>s; progress <count>`; a stalled protocol still fails after 60 quiet seconds. Scan-count and clock waits keep their plain deadlines.

### Preserved Gotcha #21

- **Diagnosing a failing E2E test: read `tests/Antiphon.E2E/TestOutput/Logs/<TestName>/`, not stdout.** Wire a test up with `TestDiagnostics.For(context.Metadata.TestName)` in `[Before(Test)]`, set `_appFixture.DiagnosticsDirectory`, call `_diagnostics.Attach(page)` after `NewPageAsync()` and `await _diagnostics.CompleteAsync(page, passed)` in the `finally`. You get `antiphon-<date>.log` (that test's server log alone), `browser.log` (console + page errors + failed requests + 4xx/5xx responses), `page.html` (DOM at the moment of failure) and `notes.log`. Setting `DiagnosticsDirectory` also turns the server's console sink down to Warning (`Serilog:ConsoleMinimumLevel`) so the assertion isn't buried under ~1500 lines of the run's own log. `TestOutput/` is gitignored; screenshots stay under `TestOutput/Screenshots/<TestName>/`.

### Preserved Gotcha #24

- **Integration tests share one Postgres — never assert on a global count.** `TestDbFixture` starts ONE `postgres:16-alpine` testcontainer per assembly run (`antiphon_test`) and every test in `Antiphon.Tests` shares it — it is not the dev database on 17280, but it is just as shared. `[NotInParallel("X")]` only serialises tests in the *same* group, so other groups are writing rows throughout. An assertion over an unscoped query (`supervisor.TickAsync().ShouldBe(1)`, `channels.ShouldHaveSingleItem()`, `.ShouldBeEmpty()`) silently also asserts "no other test has data right now" and fails at random. Scope every assertion to the row the test made (`s.AgentId == agent.Id`, `c.ExternalId == h.ChatId`); use `ShouldBeGreaterThanOrEqualTo` where the count is a sweep total. Three separate "flaky" tests were this. **A test that drives a global sweep needs `[NotInParallel]` with NO group key** — `AgentSupervisionTests` had a key, which serialised it only against itself while other suites' supervisors ticked concurrently; scoping the assertions was not enough on its own.

### Preserved Gotcha #26

On a high-core server2 runner, the combined CARD-0691 attention/census checkpoint
exhausted PostgreSQL's connections during isolated-database cleanup (53300); a
fleet-wide query-count test also observed other fixtures changing between reads.
Run that selection with `TUNIT_MAX_PARALLEL_TESTS=1` when using `run-checkpoint.ps1`.
This preserves the exact class filter and executed roster. It is a test-process
environment setting, not a production database or runner change.

- **A test host that boots the real `Program` launches the check interpreter on the PRODUCTION session-runner unless told not to — and nothing ever kills it** (CARD-0204, measured 2026-08-25: 190 of 201 live `Antiphon.PtyHost.exe` had no `AgentSessions` row, ~4 GB resident, oldest 12 days). `WebApplicationFactory<Program>` runs every hosted service on top of the real `server/appsettings.json`, so `AgentTaskCheckHostedService` → `CheckInterpreterProvisioner.EnsureAsync` provisions the AlwaysOn `antiphon-check-interpreter` in `C:\logs\antiphon\check-interpreter` and starts it AT ONCE through `SessionRunner:BaseUrl` = 17204; with `AntiphonWebAppFactory`'s `test-raw` definition that is a detached pty-host holding an interactive `cmd.exe`, whose 24 h linger clock never starts because the child never exits, while the session row lives in the throwaway test schema. One `HealthEndpointTests` run reproduced it. The diagnose seat (CARD-0352) and output-distiller seat (CARD-0330) are the same leak on a second and third AlwaysOn slug. `ProductionRunnerGuard` (`[Before(Assembly)]`) now sets `SessionRunner__BaseUrl=http://127.0.0.1:1`, `Delegation__CheckInterpreterEnabled=false`, `Delegation__DiagnoseEnabled=false`, and `Delegation__OutputDistillerEnabled=false` for every `Program` boot in `Antiphon.Tests`, and `AntiphonWebAppFactory` additionally swaps `ISessionRunnerClient` for `RefusingSessionRunnerClient` (a launch is a loud exception, recorded). **A new test host that boots `Program` outside this assembly must do the same, or start its own runner the way `Antiphon.E2E`'s `IsolatedSessionRunner` does (CARD-0102).** The reconciler will never reap these (`unclaimed never implies kill`, CARD-0056); `pwsh -File scripts/reap-orphaned-pty-hosts.ps1` (dry run by default, `-Execute` to kill) is the operator's census-and-reap, and it kills only a host with no DB row AND a recognised test-launch shape AND a banner-only ansi log AND a live child matching that shape, through the runner's kill endpoint.

### Preserved Gotcha #30

CARD-0691 R2's Windows host work is a separate rollout from the R3 attention feed.
Until R2 lands, the following teardown contract is planned, not a shipped guarantee:
test runtimes set `SessionRunner:PtyHostExitWithOwner=true`; the leak sweep derives its
root matcher from `TestSessionLogRoot.KnownPrefixes`; and
`PtyHostTestSettingsGuardTests.every_linger_override_in_tests_also_exits_with_owner`
guards new runtime fixtures. The linger clock starts at child exit; a live child never
expires from linger alone. R2's opt-in owner watch supplies the test-time bound, while
production retains restart/re-adoption with the default `PtyHostExitWithOwner=false`.

- **Building while daemons run**: the always-on session-runner (and dev server) lock their `bin/` outputs. To build/test without restarting them, use an alternate output path: `dotnet run --project tests/<X> --property:OutputPath=bin-ptyhost/` (gitignored by `bin-*/`). **End it with a forward slash, never a backslash.** `'--property:OutputPath=bin-x\'` loses its trailing backslash to Windows argv quoting, and the mangled value creates junk directories — `bin-x --treenode-filter`, `bin-check --nologo`, and worst of all `bin-profile ` *with a trailing space* (see the next bullet — that one breaks the whole repo's build). `OutputPath` applies to every project in the graph, so one run drops a `bin-<name>/` in ~12 directories. Keep an exact producer-owned output inventory; a directory name or age alone does not authorize deletion. `cleanup-build-junk.ps1` retains its existing age-based cleanup behavior (see below). Process-spawning tests share a 1-wide `ProcessSpawnLimit` lane (CARD-0050 S5); a failure there is a real defect unless it also fails at the base commit (stash and re-run).

- **Restoring a mutation-test source backup with `Copy-Item` preserves its old modification time** (CARD-0412 D4). An incremental build can then reuse the mutated DLL even though the source diff is restored. Update the restored file's `LastWriteTime` or explicitly rebuild, and verify the test output contains the freshly built DLL before treating the restored-green run as evidence.

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

- **A test harness that registers a frozen-clock `TimeProvider` for the whole server graph hangs the test process forever, at ~0 CPU, on whichever run wins a race** (CARD-0222; `docs/investigations/2026-08-28-card-0222-antiphon-tests-hang.md`): `SessionMessageQueueService` has six poll loops of the shape `deadline = UtcNow() + N; while (…) await Task.Delay(poll, _timeProvider, ct)`. A private `MutableTimeProvider` whose `GetUtcNow()` moves only on `Advance()` — but which inherits the base `CreateTimer`, so the delays run in REAL time — makes the deadline unreachable once the loop is entered, and it is entered whenever the runtime already holds `LastSequence` for the session. `HerdrAlwaysOnChannelParityTests` hung 4/4 on both arms (dumpasync leaf `SettlePostEvidenceAsync → Task+DelayPromise`), and `AgentSupervisionTests` carries the same provider. Rule: a fake clock handed to the queue must be an **offset over the real clock** (`GetUtcNow() => DateTimeOffset.UtcNow + _offset`), a `FakeTimeProvider` with `AutoAdvanceAmount`, or a `ScaledTimeProvider`, which keeps real timers at a fixed multiple; `BridgeQueueHarness.HarnessOptions.ClockSpeed`. Never a frozen instant. Diagnose any "test process alive, no CPU, no output" with `dotnet-dump collect -p <pid>` then `dotnet-dump analyze <dmp> -c dumpasync` — thread stacks show nothing for an async wedge; the chain names the method and the await.

### Preserved Gotcha #74

- **Do not treat 3,893 tests / ~25.5 minutes as a current full-suite duration.** That figure is CARD-0110's 2026-09-03 re-measure (after S2 migrate-once and CARD-0238 connection-exhaustion fix; was ~28 min on 2026-08-29). A newer 2026-09-10 profile discovered **7,072** cases; a clean Unit selection of **1,992** took 70.29s outer wall (1,988 pass, three fail, one skip); a **492-case** broad slice took **94m 23.46s** raw (491 pass, one fail). The 91m 35.3s "adjusted" broad figure substitutes one clean case for a diagnostic pause — arithmetic, not a clean rerun — and shared-host interference affected that sample. A current full-suite wall time was **not** measured. The old 35-minute observation threshold is **not** a kill deadline. TUnit still runs global `[NotInParallel]` last, and `--output Normal` prints nothing for passing tests, so a long quiet stretch is not a hang. Use `--output Detailed` or `pwsh -File scripts/run-tests-watched.ps1 -Exe <bin-x>/Antiphon.Tests.exe -Detailed`. Postgres `53300`/`53200` exhaustion is fixed (CARD-0238). **The local foreground loop is Unit plus named affected integration classes**, not the full assembly (CARD-0475 S5): `--treenode-filter "/*/*/*/*[Category=Unit]"` — a category predicate works; it is not an OR.

### Preserved Gotcha #75

- **A `dotnet build` that sits for 20+ minutes at near-zero CPU is probably reading `obj/…/*.FileListAbsolute.txt`, not hung** (CARD-0222, same doc): every `--property:OutputPath=bin-<name>/` build shares the project's one `obj/` and appends to that ledger, `IncrementalClean` reads/filters/rewrites it on every build and prunes only entries under the CURRENT `OutDir`, so it grew to 228 MB / 770,706 lines for `Antiphon.SessionRunner` and 97 MB for `Antiphon.Tests` (nested `bin-X\bin-Y\…` trees from before CARD-0110's exclude). Measured: 157 s of a 181 s Tests build in `ReadLinesFromFile`+`FindUnderPath`; full graph 21m31s → 1m28s after a reset. `Directory.Build.targets` now deletes a ledger over 2 MB (`AntiphonCleanFileMaxBytes`, `0` to disable) with a warning naming the card. The tell from outside: the outer `dotnet` process has ~1 s of CPU, one MSBuild node ticks at ~10 % of a core in `FindUnderPath` (`dotnet-stack report -p <node>`), and no `Antiphon.Tests.exe` has been spawned yet — there is never a `testhost.exe` under the Microsoft.Testing.Platform runner.
<!-- CARD-0254 preserved source ends -->

## CARD-0459 worktree residue

Ordinary Code/Review uses `OutputPath=bin-c459/` (forward slash), the Unit lane, and the named
classes in `docs/superpowers/plans/2026-09-19-card-0459-worktree-cleanup-plan.md`.
`scripts/test-worktree-residue.ps1` covers the operator script. `WorktreeResidue:Execute` stays
false in shipped config. Do not treat `PruneStaleAsync` or a shorter TTL as cleanup authority.
CARD-0452 ignored-content policy is unchanged; residue tests must not add a production override.

CARD-0664 (consumer-slot release) builds to `bin-c664-a/`, `bin-c664-b1/` and `bin-c664-b2/` and
runs the plan's Checkpoints over `WorkspaceReservationLivenessTests` (Unit), `WorktreeRetirementRaceTests`,
`TaskWorktreeRetirementTests`, `WorktreeResidueSweepTests`, `WorktreeResidueRecoveryTests`,
`AgentTaskSettlementRaceTests`, `AgentTaskLandRequestTests`, `AgentTaskLandRefusedRetryTests`,
`SessionGenerationExitTests` and `AgentAttachHerdrTests`
(`docs/superpowers/plans/2026-09-24-card-0664-consumer-slot-release-plan.md`). New `C664_*` tests
age a `Launch` row by writing `CreatedAt`; they never shorten `LaunchGraceMinutes` or switch the
journal to a fake clock. CARD-0801 replaces the historical Linux fake-listener hang with a native
Unix socket listener and a five-second, tokenless readiness deadline. Terminal bind/accept faults
complete every readiness waiter; disposal settles pre-start waits. The exact execution closure is
the eight server classes (110 results) and 24 SessionRunner classes (497 results) in
[`2026-09-30-card-0801-cross-platform-fake-herdr-plan.md`](superpowers/plans/2026-09-30-card-0801-cross-platform-fake-herdr-plan.md).
Run that plan's literal class filters on both Linux and Windows. The CARD-0801 split decision
gates 48 named Linux results: CP-6 skips 8 for CARD-0863, CP-7 skips 1 for CARD-0871,
CP-9 skips 23 for CARD-0864, CP-10 skips 14 (13 for CARD-0865 and one for CARD-0878),
and CP-11 skips 2 for CARD-0866. The other 599 CP-2–CP-13
results execute on Linux with zero failures; Windows retains all 647 results with zero skips.
`AgentAttachHerdrTests` belongs in the Linux filter. CP-1 covers the original 24 focused cases;
CP-2 covers 39 focused listener and transport results plus the linked classification guard.
Use the plan's separate Windows W-1
at the final Linux-tested SHA; a Linux pass does not prove the Windows named-pipe branch.

## Fast lane (CARD-0110 / CARD-0475 S5)

CARD-0590's Linux image, roster, and session-created stack are in [docker-stack.md](docker-stack.md). Those checkpoints are ordinary Docker evidence. They do not run SourceLanding Mutation. Since CARD-0604 the server2 runner owns a **nested** Docker daemon of its own, so Testcontainers works there unmodified (the mapped port and the test process share one network namespace); `scripts/verify-card0604-dind-runner.ps1` is the local harness that proves that image on Docker Desktop before server2 ever sees it.

CARD-0700's 2026-09-25 server2 run reproduced 20 card-file integration failures on
unchanged production code (baseline parent `fbdc3c6e66c7e35d01260cb64f49a404809b55a6`).
CARD-0713 tracks four Git-hook fault cases in `CardFileGitFailureAcceptanceTests`
(hooks lack executable permission), six junction cases in `CardFilePrivacyGitTests`
and `CardFilePrivacyPathTests`, and ten `CardFilePrivacyScriptTests` cases displaying
remote Windows paths through a nonexistent local `C:` drive. This is separate
from CARD-0681's Unit-lane portability work. See the
[measured baseline and checkpoint report](investigations/2026-09-25-card-0700-cached-board-lookups.md).

### Default Code/Review recipe

Build once into a producer-owned isolated output (forward slash on `OutputPath`). Execute the Unit lane. Execute named affected integration classes together where the pinned TUnit 1.44 OR syntax allows. Inspect a **fresh TRX** for each intended class/method and nonzero counts. `--list-tests` is not execution evidence on this runner. Do not combine UID and tree selectors. Unit and named integrations may be separate invocations of the same built output; do not invent unverified mixed category/class filter syntax. Combined class-filter syntax lives in [Combined class filters (CARD-0403)](#combined-class-filters-card-0403).

The brief/verification section must list coverage-to-class and end with the `### Checkpoints` table (below). Code and Review report the filters they ran and the actual expanded counts. Unit-only is insufficient for native delivery, landing, leases or persistence. A broad namespace/full-assembly exception names the affected cross-cutting invariant, the classes that cannot be bounded, and the expected cost **before** the run; missing rationale is a Review defect. CI/nightly keep the broad run. Per-PC Mutation stays method-scoped. Do not silently edit `LandVerifyFilter` or the production verifier as part of a documentation policy change.

Example (directory names are examples — use a fresh empty results directory per invocation):

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c475/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c475-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c475/ -- --treenode-filter '/*/*/(AgentTaskLandBoundaryControlledTests*)|(AgentTaskLandAdmissionControlledTests*)|(AgentTaskLandConcurrencyControlledTests*)/*' --report-trx --report-trx-filename controlled.trx --results-directory .antiphon/c475-controlled
```

### Offline rolling harness jq receipts (CARD-0983)

On a jq-expected host, run `scripts/test-deploy-server2.ps1 -RequireJq` or
`scripts/test-deploy-server2-jq.ps1 -Case present -RequireJq` through the build-slot
wrapper. For example:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rolling-required-jq -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case present -RequireJq
```

The marker-reader shell needs jq: native bash on Linux, the default WSL bash on
Windows. Native Windows jq does not qualify WSL. The regression lane also needs
pwsh in that shell. `-RequireJq` rejects an unavailable probe with
`C983_JQ_REQUIRED`, exit 1, before any rolling group, including `-Only` selections.
An available required run includes T-20 and reports 24 groups / 66 invocations /
227 assertions. Without the switch jq remains optional: unavailable probes keep
the named skip and 23 / 62 / 218 roster. Successful jq-driver runs retain 31
assertions. The absent, missing-shell and failing-shell driver cases remain
optional unless explicitly required. The C1008 Unit consumer requires jq in its
present invocation; a missing prerequisite is a failure of that proof.

The sixteen named C1008 host contracts in `RemoteScriptContractTests` and
`RollingVolumeRecycleScriptTests` explicitly require native Linux (CARD-1050).
Their entry guards skip before host setup on other platforms; missing Linux
tools still fail. The five extracted source/ASCII and PowerShell wrapper tests
run on Windows and Linux. Windows coverage requires the exact method/reason
skip audit plus those portable passes; intentional skips are placement evidence,
not a clean zero-skip checkpoint certificate or passing host coverage.

#### jq qualification and bounded consumer proof (CARD-1040)

Qualify these three locations independently:

| Location | Required evidence / owner |
|---|---|
| Test child shell | Resolve bash and jq in the non-login environment inherited by the actual child. Record OS/architecture, uid, executable paths and resolved symlinks, jq version/SHA-256/owner/mode, and any child-only prerequisite adjustment. A login-shell probe with a different PATH does not qualify the child. |
| Executing runner image | Reuse the [CARD-0927 image pin](../docker/session-runner-grok/Dockerfile) and [image contract](docker-stack.md). The outer container's owning host supplies container ID, creation/start times, immutable image ID/digest and selected source provenance, joined to build/activation receipts and runner `buildVersion`. Nested Docker and runner health cannot establish the outer image identity. |
| Outer deployment host | The landed [CARD-1025 helper and receipt contract](superpowers/plans/2026-10-04-card-1025-host-jq-prerequisite-plan.md) owns jq on the host PATH used by real recycling. Reuse that implementation and its separate receipt; a runner-image binary does not qualify the host. Coordinate its prerequisite before rollout phases that recycle. |

For the native Linux CARD-1040 proof, the actual child must resolve the qualified
`/usr/local/bin/jq` (or an explicit alias to that same verified file): `jq-1.7.1`,
SHA-256 `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`,
root:root mode 0755, executable as uid 1654. A matching user-home copy alone is
provisional diagnostic evidence. Discover jq on the actual non-login child PATH
after clearing command hashing; do not prepend the canonical directory to hide
a shadow. An alias must resolve to the regular, executable, non-symlink canonical
leaf. A canonical spelling that points into home refuses. This trusts canonical
directory integrity; hardlinks and a writer's check-to-use swap remain CARD-1058.
Record missing image identity or activation
receipts as pending obligations; do not install into a standing container or
infer activation from a checkout SHA.

Reuse only the existing [jq-version probe](../docker/session-runner-grok/verify-codex-image.sh)
in a throwaway container of the recorded immutable image: reviewed script mounted
readonly, uid `1654:1654`, `--network none`, no ports/socket and private writable
`/c660-home` tmpfs owned by 1654. Consume the reviewed
[CARD-1054 probe and frozen row contract](superpowers/plans/2026-10-05-card-1054-jq-path-qualification-plan.md#verification-design)
at its recorded source SHA. The [CARD-1054 Code handoff](../.antiphon/task-dfea030e.md)
records the implementation candidate and per-row source receipts for separate
Review before S2 consumes it. Require exit 0 and exactly one physical success row:
`C660_ROW jq-version ok jq-1.7.1 as uid 1654 lookupPath=/usr/local/bin/jq path=/usr/local/bin/jq`
for direct lookup. An approved alias changes only `lookupPath` to the actual
found path, Bash `%q`-escaped with `LC_ALL=C`; `path` retains the canonical target.
Retain the unedited row. Path refusals exit 1 and retain one
`C660_ROW jq-version fail reason=REASON lookupPath=Q(found) path=Q(resolved)`
row, where `Q` denotes that same escaping, not literal output; unavailable
observations are `unavailable`. Reasons are `JqNotFound`, `JqLookupInvalid`,
`JqResolveFailed` or `JqPathUnapproved`. A refused jq must never execute, even to
format diagnostics. Older path-free rows cannot satisfy this updated gate.
These observations supplement the active-container checks; they do not prove
activation. Private CARD-1054 fixtures cannot satisfy the immutable-image or
activation gate. The full image wrapper also runs unrelated provider probes and
is outside this bounded qualification.

The [CARD-1040 Plan/TestDesign](superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md#checkpoints)
freezes CP-1/CP-2/CP-3: exactly twelve Remote methods, two Rolling methods and the
one legacy roster method, respectively. Its `After=S1` rows execute **during S2,
after image qualification**; S1 documentation may record the outstanding gate.
Use the checkpoint tool, serial isolated outputs, exact committed expected SHA
and exact frozen filters. Inspect fresh TRX for equality with all fifteen literal
names, exactly 15 passed and zero failed/skipped, plus four driver summaries of
31 assertions each. Present must execute `-RequireJq`, T-20 and 24/66/227 without
`C973_JQ_SKIPPED`; the other three modes retain their optional branches and
23/62/218. Required jq absence remains visible as failure, including production
`RecycleToolsMissing`; optional C912/C973 guards retain their existing contract.
Do not substitute a whole Unit/class/namespace run or change assertions,
deadlines, retries or process limits. PC-1 remains the isolated method-scoped
jq-absence control for post-land SourceLanding Mutation.

Account separately for slot wait, isolated build, host startup, wall interval
between method boundaries and teardown. Preserve source receipts, unedited
CHECKPOINT lines and per-method outcomes; do not sum parallel durations to infer
wall time. The plan's retained red run has zero timeouts/aborts and already
separates these phases. Missing phase evidence is unattributed and does not
authorize another timed probe or a longer deadline.

### Offline Pty provider oracles (CARD-1007)

The Codex `debug prompt-input` and Grok `inspect --json` methods in
`OrchestratorWorkspaceLayoutCanaryTests` require `ANTIPHON_PTY_PROVIDER_ORACLES=1`.
Like the headed canaries, the shared gate accepts exactly `1`; otherwise both skip
with `Set ANTIPHON_PTY_PROVIDER_ORACLES=1 to opt in to offline Codex/Grok provider oracles`
before binary lookup, scratch layout creation or process launch. This flag enables
only the offline probes; headed canaries keep their existing opt-ins. Both original
oracle assertions still run when their existing binary prerequisites are satisfied.

Run the intentional lane from a clean committed checkout (PowerShell):

```powershell
$env:ANTIPHON_PTY_PROVIDER_ORACLES='1'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name provider-oracles -Project tests/Antiphon.Agents.Pty.Tests -OutputPath bin-provider-oracles/ -Filter '/*/*/OrchestratorWorkspaceLayoutCanaryTests*/(Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root*)|(Grok_inspect_is_bounded_at_the_nested_checkout_git_root*)' -MinExecuted 2 -Expect 'Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root,Grok_inspect_is_bounded_at_the_nested_checkout_git_root' -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/provider-oracles
Remove-Item Env:ANTIPHON_PTY_PROVIDER_ORACLES
```

The existing resolvers use Windows install paths; `ANTIPHON_CODEX_EXE` can select a
native Codex executable on another platform. An enabled skip for a missing binary
is incomplete oracle evidence. CARD-0596 owns the separate Grok inspect assertion
failure; this gate does not change that assertion or its timeout.

### Alternate-output cleanup safety (CARD-0448)

`scripts/cleanup-build-junk.ps1` retains its pre-CARD-0448 behavior: the weekly Windmill
job deletes `bin-*` directories older than its configured age threshold, leaving `bin/`,
`obj/` and `workspace/` alone. The inventory-only rewrite was reverted as out of scope.
The landing verifier writes to a unique external artifacts directory and retains it.
Worktree removal's ignored-content policy remains separate (follow-up CARD-0452).

A `[Category=X]` predicate works in `--treenode-filter` (measured; a single category is not an OR). CI / nightly keep the full run. After a TRX (`--report-trx --report-trx-filename run.trx`), check for new ≥5 s tests:

```
pwsh -File scripts/test-duration-tripwire.ps1 -Trx path\to\run.trx
```

The allowlist is `tests/Antiphon.Tests/slow-tests-allowlist.txt` (exact simple or fully-qualified class names, case-insensitive). A class marked `Slow` must be registered there by fully-qualified name when it is added: `TestClassificationGuardTests.Registry_matches_compiled_metadata` fails with a first line `missing classes:` that names every Slow class absent from that file. A row for a class that is not Slow is `unmarked-registered` and fails the same guard. Every test class is tagged `Unit` xor `Integration` (`TestLaneCategoryGuardTests`).

### Checkpoint manifest (CARD-0585)

**Evidence Git policy (CARD-1015).** Keep generated TRX, JSON receipts, logs,
archives and checkpoint output ignored; the generic slice-commit requirement does
not include them. Under root `.antiphon/` (case-insensitive), permitted formats are
Markdown and the named `approved-json-fixture` exception (CARD-1036), matching the
case-sensitive complete Git path
`\A\.antiphon/fixtures/(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+\.approved\.json\z`.
Both require regular mode `100644`/`100755`, at most 1,048,576 committed bytes,
and no directory component containing `checkpoints` (case-insensitive).
The leaf `checkpoints.md` is allowed. An approved fixture is an intentionally
retained expected input/result consumed by a named regression test; its Code/Review
description identifies that consumer. The guard enforces path/type/size, not semantic
approval or JSON provenance. Keep `.gitignore` defaults and stage each exact reviewed
fixture individually; never unignore or force-add an evidence directory. Receipts,
source JSON, TRX, logs, archives and actual results remain run output, including
inside `fixtures/`; moving or renaming them does not confer fixture approval.
Do not relocate generated payloads elsewhere to evade the rule; the
canonical `.antiphon/reports/` store remains runtime-owned and ignored.

Code and read-only Review run `scripts/check-evidence-diff.ps1 -BaseRef <recorded
task base> -HeadRef <exact pushed SHA>` over the entire candidate history, including
report-only commits. The checker reads pinned Git objects and every introduced
commit, including side branches/root commits, using no-renames raw records. Exit
0 proves compliance, 1 reports policy violations, and 2 means input/history/Git
cannot be verified. Unchanged legacy entries are grandfathered; introduced or
changed entries must satisfy the rule. The independent Actions workflow enforces
the same CLI; its green result does not establish branch protection.

Continue generating and inspecting structured receipts and fresh TRX. Essential
unedited CHECKPOINT lines, actual tested SHA, counts and provenance belong in the
stored task report. A report commit changes HEAD: freeze tracked reports before
final source qualification, then put later receipt facts in the stored report.
Never relabel earlier receipts. Review writes no source/evidence; SourceLanding
Mutation keeps its stricter external-evidence/no-commit exception.

Ignored output does not dirty source; modified tracked evidence does. Publication
and cleanup remain separate: the currently registered `RefusingEvidenceRetention`
can preserve residue after publication. Completed-card whole-worktree removal is
CARD-1017, outside this policy; raw artifacts gain no new durability guarantee.
CARD-1015's separately marked normal deletion commit is checked by
`check-evidence-deletion.ps1` against the independently recorded start-tree rule
inventory, preserving allowed Markdown and proving historical object recovery.
For a later cleanup, pass `-SupplementalCleanup CARD-nnnn` in both inventory and
validation calls. This opt-in uses the later immutable `-InventoryRef` and the
exact `Antiphon-Evidence-Deletion: CARD-nnnn` commit trailer, without requiring the
original 108 entries at that base. It retains inventory count/bytes/digest, exact
deletion-only set, old identities, kept Markdown, final-tree and blob-recovery
checks. Omission preserves CARD-1015's anchored contract; `CARD-1015` is not an
allowed supplemental label. Run the full task-range history guard separately.

`Antiphon.Tests` ScriptHarness invocations own a private Windows Job Object or
Linux process group before running PowerShell. One 300-second deadline covers
launch, logical root exit, stdout EOF and stderr EOF. Every outcome explicitly
terminates the owner, even if the logical root has exited or both streams have
closed; cleanup has a separate total 10-second budget. Native regression cases
use a 5-second execution budget and 2-second cleanup budget. Their portable
helper is staged beside the test output in `script-harness-host/`. The Windows
owner requires a real `pwsh.exe` path, and the Linux owner requires its confirmed
private-session supervisor. A failed death or pipe observation retains the
invocation's results/control directories for diagnosis.

CARD-1047 Windows qualification uses the plan's four bounded final rows (32
contract/classification, 21 native ownership, 24 checkpoint callers and 18
positively selected slot callers), rather than a whole Unit or assembly run.
Every native Windows test requires an installed regular pwsh.exe and the staged
helper. Fixture observations use retained process handles outside harness-owned
paths, nonce-checked readiness and a bounded release barrier; each invocation has
an independent 30-second watchdog (45 seconds for the three-invocation repeat).
Emergency termination/join has five seconds total and any rescued residue fails
ordinary proof. W10 deliberately injects an acknowledged stop with live members,
or failed accounting after real membership: both must fail cleanup and retain
diagnostic paths before independent joins and fixture scratch removal. A job close
or stop acknowledgement cannot replace successful empty accounting and signaled
process observations. W9 refuses WindowsApps path shapes and reparse classification
before native creation, with an installed-executable output control and no installed
alias/privilege requirement. All 20 method-scoped PCs remain a separate post-land
SourceLanding Mutation obligation. See the
[Windows plan](superpowers/plans/2026-10-04-card-1047-windows-script-harness-plan.md).
The W7 child pins the transported handle before opening the nonce-named fixture
event and compares kernel object identity before signaling; a process-local handle
value can identify a different runtime event in the child. W10's independent reap
runs before job close, so fixture termination cannot race kill-on-close; the
already-terminated query-error arm joins its retained observations without another
termination request. Both use the existing five-second fixture cleanup cap.

Use the checkpoint tool for repeated class runs. Before any ad hoc destructive shell cleanup, verify both variable components are nonempty, quote their expansions, and resolve the target inside the intended scratch root; a missing component must exit nonzero before `rm`.

A Plan/TestDesign artifact ends its `## Verification design` with a `### Checkpoints` table: one row per isolated build plus one exact test-filter group, bound to the plan slice it closes, and a Code dispatch runs that table as a **closed list** rather than an ad hoc build/test loop. It removes the extra rebuilds (CARD-0490 ran 19 builds for 8 test runs), the hunting for files the plan already named, and the second Code round that CARD-0459 paid for; it does not shrink the named Slow/native V/R work, which is the coverage itself (investigation `docs/superpowers/investigations/2026-09-20-card-0585-batched-edit-test-workflow.md`).

Schema:

| Column | Meaning |
|---|---|
| `CP` | `CP-1`, `CP-2`, ... in run order. |
| `After` | Plan slice(s) whose commits must exist first: `S1`, `S1-S3`, `all`. |
| `Build` | `<project> -> <bin-x/>` (forward slash, `bin-` prefix), or `CP-n` to reuse that row's output with `--no-build`, allowed only when both rows share the same `After`. |
| `Group` | Short name, unique in the table, used in results paths and the report line. |
| `Filter` | The exact `--treenode-filter` (CARD-0403 combined-class syntax), or the exact command for a non-TUnit group. |
| `Covers` | The V-n/R-n IDs this row is evidence for; the union of all rows is the whole ordinary scope. |
| `Expect` | Roster rule: `all listed, 0 failed` (default) or `>= N executed, 0 failed` for a lane. |
| `Min` | The integer passed to `-MinExecuted`: a floor on **TUnit executed test results**, and nothing else. `n/a` for a non-TUnit row, which states its own success/assertion count in `Expect` instead. |
| `EstimatedMinutes` | Estimated wall-clock **minutes** for the row, including its build when the row builds; Cost's ordinary floor is the sum of this column. |
| `EstimatedMinutesWindows` | Optional positive Windows estimate. The importer selects it for the resolved row and deadlines on Windows; Linux keeps `EstimatedMinutes`. Blank or `n/a` inherits the base estimate. |
| `Serial` | Optional `true`/`false` (case-insensitive; blank is false). A serial row runs alone: no other row and no build is in flight while it runs. It does not set TUnit's in-process parallel limit. |
| `Environment` | Optional row-only child environment: `NAME=value;OTHER=value` (plain or one backtick-wrapped list). Blank/`n/a` means empty. Names match `[A-Za-z_][A-Za-z0-9_]*` and are unique regardless of case; values may be empty or contain more `=`. Use YAML for a literal semicolon in a value. |

The nine required columns stay in order; optional columns may follow in any order. Unknown or duplicate headers and rows with too many or too few cells are refused. Escape a literal filter pipe as `\|`. A reused build may be `CP-n` or `CP-n (-NoBuild)` and must point to an earlier filter row in the same `After` group. Repeat the exact filter instead of `same as CP-n`, and put test-host settings in `Environment` rather than after the filter.

`Min` is a count and `EstimatedMinutes` is a time; neither is derived from the other, and a row carries both. A TUnit method that performs 12 internal assertions still contributes **one** execution unless the runner reports separately parameterized results; an argument-expanded test contributes its reported result count, so a six-argument `[Arguments]` method is 6. Internal assertion rows, harness `PASS` lines, matrix combinations, loop iterations, unique source methods and elapsed minutes are separate evidence and are never a `-MinExecuted` floor. `-Expect` checks names; it does not turn assertions or minutes into executed tests.

**Legacy plans.** Before CARD-0617 the `Min` column meant estimated minutes, so plans written then (CARD-0585, CARD-0590, CARD-0594, CARD-0599, CARD-0604, CARD-0606, CARD-0607, CARD-0610) legitimately hold minute values under `Min`. Do not reinterpret those numbers as counts and do not relabel their historical evidence. Before reusing such a plan, rename its time column to `EstimatedMinutes` and derive any execution floor from its own roster or TRX, never by copying the old number across.

Rules:

1. The table is the closed list of builds and test runs for the round. Each row runs once, in order, after its `After` slice is committed; a red row is fixed and rerun as the same row (count the reruns).
2. Any other build or test command is unlisted: it is reported with a reason, never omitted. A compile error found by a row's own build is fixed and the same `CP-n` rerun, not an unlisted run.
3. Every row is reported as one line: `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N failed=N skipped=N trx=<path>` with its slot/wait fields and mandatory `dirty=<count> source=<sha>[+dirty:<sha256>] sourceState=<clean|dirty|changed|unknown> buildSource=<verified|unknown|mismatch|notApplicable>` tokens, plus `reruns=k` when k > 0. `scripts/run-checkpoint.ps1` or the checkpoint tool prints the line; preserve it unedited.
4. A new test that cannot go red against the production line it guards (self-compare, constant, no outcome assertion) is a stub, not done; Review rejects it.
5. Review checks the report's lines against the table: a missing row, zero count, unlisted build/test run without a reason, or a broad run without a named invariant and cost is a defect.
6. Nothing in the table is skipped to save time; splitting a row that exceeds one foreground window is done by the classes/methods it already names.

Illustrative example — the class, group and path names below are invented to show the two columns, not lifted from any plan:

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-ex/` | sample-surface | `/*/*/ExampleSurfaceTests/*` | V-1, R-1 | all 3 methods, 0 failed/skipped (between them they assert 12 named rows) | 3 | 9 |
| CP-2 | S2 | n/a | client-lint | `pwsh -File scripts/test-client.ps1 -Lint` | V-2 | 0 errors, 0 warnings | n/a | 2 |

`Min` on CP-1 is **3** because three TUnit methods execute — not 12, which is how many assertions they make, and not 9, which is how many minutes the row is budgeted. CP-2 runs no TUnit at all, so its `Min` is `n/a` and its whole success criterion lives in `Expect`. Cost's ordinary floor for this pair is 9 + 2 = 11 minutes.

Running one row (same illustrative names; add `-NoBuild` to reuse an earlier row's output):

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-ex/ -Filter '/*/*/ExampleSurfaceTests/*' -MinExecuted 3 -Expect ExampleSurfaceTests -ResultsRoot .antiphon/ex-checkpoints
```

The script builds unless `-NoBuild`, runs the one filter into a fresh results directory, parses the TRX counters and the executed `Class.Method` roster (from `TestDefinitions/UnitTest/TestMethod@className`, never the display name), then prints the `CHECKPOINT` line, one `FAILED <Class.Method>` line per failure, up to 300 `EXECUTED <Class.Method>` lines and a `CHECKPOINT <Name> EXIT CODE: <n>` trailer — so no delegate opens the TRX. Exit codes: **0** green, **1** one or more failed tests, **2** invalid input (a non-`bin-x/` `OutputPath`, a results directory that already exists), a failed build or no TRX written, **3** fewer than `-MinExecuted` executed tests or an `-Expect` token that matches no executed name. It never deletes anything, never edits a filter and never calls `--list-tests`. Give several `-Expect` tokens as one comma-separated value (`-Expect A,B`): `pwsh -File` binds every argument as a string, so a repeated `-Expect` fails to bind there. Quote characters around the tokens are tolerated (CARD-0615) — `-Expect "'A','B'"` and `-Expect '"A","B"'` match exactly as `-Expect A,B` does, because each split token has its leading and trailing quotes stripped. Only the edges: a quote inside a token stays part of the name and will miss.

**Source qualification (CARD-0835).** Ordinary Code/Review passes the exact committed SHA with `-ExpectedSourceSha`; the checkpoint tool's `run`, `start`, and `row` use `--expected-source-sha`. The driver captures a version-1 source snapshot before work and at execution boundaries. A clean certificate has `dirty=0`, a plain full SHA in `source=`, `sourceState=clean`, `buildSource=verified`, zero failed/skipped tests and a matching structured `source.json` (script) or schema-2 `report.json` (tool). `scripts/validate-checkpoint-receipt.ps1 -Evidence <source.json|report.json> -ExpectedSourceSha <sha> [-Rows CP-1,CP-2]` checks that evidence and exits 2 for incomplete, dirty, changed, unknown or conflicting facts. The tool also offers `validate --evidence <report.json> --expected-source-sha <sha> [--rows CP-1,CP-2]` without launching a build. A stable dirty run without strict mode remains diagnostic; a missing or mismatched `checkpoint-build-source.json` makes strict `-NoBuild` refuse before tests. Commit source before the first row and leave it unchanged through the last; a brief edit restored between observations is outside this boundary-based check.

Activation note: land pending cards that rely on existing clean Review evidence before restarting AppHost onto this change. After activation, every older Review without `reviewedSourceClean: true` needs a fresh Review before evidence-backed land, including owner recovery. Index paths marked assume-unchanged or skip-worktree make source capture unknown until those flags are cleared; task worktrees here are full checkouts, and sparse-checkout content is not yet independently verified against HEAD.

CARD-1035: `run-checkpoint.ps1` starts both dotnet children in the certified Git root,
including when the caller used `Push-Location` and its native process directory differs.
Relative `-Project` paths resolve from that root, matching the build-source stamp;
the same child-directory binding applies to `-NoBuild` reuse and the dotnet shim.

**MSBuild properties (CARD-0671).** `-MsBuildProperty Name=Value` is forwarded as `--property:Name=Value` to **both** the build and the `dotnet run`, so a `-NoBuild` row evaluates the same output its build produced. Give several as one comma-separated value (`-MsBuildProperty UseAppHost=false,Foo=bar`), for the same `pwsh -File` binding reason as `-Expect`; a value that itself needs a comma uses MSBuild's `%2C` escape. A token that is not `Name=Value`, or that sets `OutputPath`, `OutDir` or `BaseOutputPath` (the `-OutputPath` guard owns those), exits 2 before any `dotnet` call. Every forwarded property is echoed as a `MSBUILD PROPERTY <Name=Value>` line after the `CHECKPOINT` line. **Off Windows the script adds `UseAppHost=false` by default** unless a supplied property names `UseAppHost` (`-MsBuildProperty UseAppHost=true` opts out): on Linux the referenced FakeClaude project's extensionless `fakeclaude` apphost lands on the same path as the `fakeclaude/` directory `Antiphon.Tests`' `CopyFakeClaude` target stages, which breaks the build, and without an apphost `dotnet run --no-build` runs the test assembly through `dotnet exec <dll>`. A Linux row therefore needs no `-DotnetShim` wrapper or hand-run `dotnet` commands for this (the workaround earlier server2 plans describe). The trade-off: under the default, a test that resolves the `fakeclaude/fakeclaude` apphost through `TestAppHostPath` finds no file; with `UseAppHost=true` the collision returns, so such a test has no Linux checkpoint lane until that staging changes. On Windows with no `-MsBuildProperty` the `dotnet` arguments are exactly the pre-CARD-0671 ones. The offline harness forces either branch with `C671_PLATFORM=windows|linux`.

CARD-0847: `Antiphon.Tests` also stages a real `Antiphon.PtyHost` apphost on Linux. Its `CopyOwnedPtyHostAppHost` target restores `Antiphon.PtyHost` with `UseAppHost=true` before the inner build, because an outer restore with `UseAppHost=false` does not download the net9 apphost pack. An empty `NUGET_PACKAGES` cache must still build the test project with the checkpoint default.

The `### Cost` block's ordinary Code floor is the sum of the table's `EstimatedMinutes` column — a time, never the `Min` counts — and `-ExpectAbout` for a Code dispatch is that sum plus authoring time. The Code brief points at the table with one line, `checkpoints: <plan artifact path>@<full plan commit sha> section "### Checkpoints"` — the shape the server already renders for an Interim `selection:`.

**Repeat proof (CARD-0885).** `-Repeat N` on `run-checkpoint.ps1`, and `--repeat N` on checkpoint-tool `run`, `start`, and `row`, request N total executions of each selected TUnit case in one test host; omitted means one. TUnit generates separate native results and runs ordinary hooks. A different N needs its own isolated build output and matching build-source stamp. Qualify a repeated receipt with `-ExpectedRepeat N` or `--expected-repeat N`; the script writes source envelope v2 and the tool writes report schema 3, with every ordinal's cases and phase timings. The TRX and receipt must agree; a later pass does not erase an earlier failure. One-host repeats do not prove behavior that needs a fresh process per trial.

`repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.` Count total executions per case across proof invocations. Changing output paths does not reset the budget. A required first V/R run or a genuine code-change/failure-driven rerun is reported separately. Loaded means recorded ambient host load. CARD-0885's named five-repetition acceptance benchmark is a one-time exception, not a reusable limit increase.

### Checkpoint runner tool (CARD-0723)

`tools/Antiphon.Checkpoints` (`dotnet run --project tools/Antiphon.Checkpoints -- <verb>`) reads the plan's `### Checkpoints` table and runs it. The command every delegate uses is `run --plan <plan.md>`: that is `start` plus `wait`. `start` validates the table, writes `.antiphon/checkpoints/<run-id>/`, shadow-copies the tool, and launches a detached executor (`setsid` on Linux; `CreateProcessW` with `DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP` and never `CREATE_BREAKAWAY_FROM_JOB` on Windows). A checkpoint row is not that launcher: on Windows the row starts with `Process.Start` and `ProcessStartInfo.ArgumentList`, then the driver assigns the process to a kill-on-close Job Object on a best-effort basis. A child spawned before that assignment, or by a package alias such as the MSIX `pwsh` stub, can escape the job; timeout and cancel still sweep the process list and the redirected pipes are abandoned after 10 seconds. Linux rows stay on the runtime process type. `wait` prints one `HEARTBEAT` line a minute and, when the run finishes, the report. `wait --max-wait 570s` returns exit 75 while the run is still going; call `wait` again. A foreground call capped near 600 seconds cannot hold a long run, so do not end the turn while `wait` reports running — `stop <run-id>` if you must abandon it.

One run per committed slice group (`--after S1-S3` or `--rows CP-1,CP-2`). Rows overlap (two at a time on Linux, one on Windows). Builds never overlap in one run: every build in a worktree shares its projects' `obj/` because `OutputPath` moves only `bin`. The scheduler holds one build lane and starts at most one `dotnet build` at a time, in the order rows first need the builds. Rows whose build is done may run beside the next build. `tests/Antiphon.Agents.Pty.Tests`, `tests/Antiphon.PtyHost.Tests`, and `serial: true` rows run alone, builds included; `--serial` makes every row exclusive, so the run does one activity at a time. `report.json` carries `maxConcurrentBuilds` beside `maxConcurrentRows`; `wait` prints a needed pending build as `<id> queued` until the lane is free (CARD-0810). Every build uses `-nodeReuse:false`, `--property:OutputPath=bin-<name>/`, and `UseAppHost=false` off Windows unless the manifest names `UseAppHost`. The slot client probes `ANTIPHON_BUILD_SLOTS_URL` (else `http://127.0.0.1:8080/build-slots` on Linux and `http://localhost:17204/build-slots` on Windows) once: 200 leases, 404 is `unavailable` with no per-driver grace, and a definitive broker refusal is immediate exit 2 (`slot=refused`, `slot-reason=<code>`), before any driver. Only transport failure after the bounded 60-second grace becomes `unleased` at `-maxcpucount:4`. Each running driver posts its own holder-process pid and captured UTC start time, so a broker that repeats one lease per pid cannot make two drivers share it, and a checkpoint line says `slot=granted` only for a lease that driver still holds. A slot wait that expires is exit 4. `executor.log` records bounded probe/acquire response details and renew/release failures; report JSON and checkpoint lines retain the stable slot reason. A reachable enabled Linux broker must show `slot=granted` for every selected build and row.

For acquisition, the 60-second grace starts with the first transport failure or 5xx after the most recent busy answer. A busy 409 resets it; the overall busy wait still uses the configured slot wait limit. A 5xx in the current grace window ends in refusal if no later busy or grant answer clears it.

Exit codes: 0 green, 1 failed tests, 2 invalid manifest or build or missing/malformed TRX, 3 `minExecuted` or roster miss, 4 slot timeout, 5 row or total timeout, 6 the executor died, 7 owning task ended or could not be verified, 75 still running. A bound owner read has a 12-second HTTP deadline and a three-minute uncertainty budget; each failure is recorded in `executor.log`. Existing work runs during uncertainty, and new launches wait for a live answer. When the budget expires the run is owner-unverified and new launches stay blocked, but the guard keeps reading the owner: a later definitive settlement cancels the running drivers and releases their slots. Live readers of executor.log, including wait, must open it with FileShare.ReadWrite | FileShare.Delete while the writer still holds the file. The run exit is the highest of 2, 6, 4, 5, 1, 3, 0 for unbound runs; owner exit 7 takes precedence when bound ownership ends. Reruns happen only for names passed as `--known-flaky` (or `rerun.knownFlaky`), once, method-scoped, and the report prints `RERUN` lines with `reruns=1`. `--baseline <ref>` classifies each failure `INHERITED`, `INTRODUCED`, or `NEW` from a detached worktree of that commit. The report is `report.md` and `report.json` under the run folder. A red run keeps `failures.md`, the TRX, console logs, `host.txt`, and `git.txt`. A green run deletes that run's `bin-<name>/` directories. The executor never deletes its own shadow copy. `wait` deletes `<run>/tool/` once `phase=done` and the executor pid is gone; the next `start` sweeps `tool/` folders left by finished runs. `clean --run <id>` deletes a red run's outputs and never touches `bin/`, `obj/`, `workspace/`, or a `bin-*` name the manifest does not own.

`import --plan <plan.md>` writes the same manifest as YAML under `.antiphon/` when a row needs `knownFlaky`, `serial`, or a timeout pin. A legacy eight-column table (minutes stored under `Min`) is refused until the time column is renamed `EstimatedMinutes` (CARD-0617). `import` treats 45 minutes as an advisory threshold: imported row timeouts are `max(15, 3 * selected EstimatedMinutes)`, using the platform-selected estimate. Estimate 15 gives 45 minutes without a warning; estimate 16 gives 48 minutes with a warning suggesting a split. Row timeouts are not capped at 45 minutes; positive explicit timeouts are used unchanged, and without an estimate the manifest fallback is `max(15, manifestDefault)`. The independent total-run deadline still applies and can stop a row earlier. `import` also warns when a row names more than four land classes. A land row stays at about 120 `[Test]` methods. `row` runs one row with the same lines as `scripts/run-checkpoint.ps1`. The repo tool manifest does not list this package until a feed exists; prove the package with `dotnet pack` and `dotnet tool install --tool-path`.

### Static plan-to-test coverage (CARD-0891/0901)

`coverage --plan <path> [--tests <literal.cs> ...] [--format text|json]
[--checklist <path>] [--repo-root <root>]` performs a read-only syntax lint. It imports
the checkpoint table through the existing importer, selects C# class declarations
under its named projects and literal Scope/implementation test paths, and binds each
Verification design promise to its named method. Class OR operands and trailing `*`
are supported; partial declarations are included. Unsupported category/command
selection needs explicit files and checklist bindings. Missing plan-selected files,
ambiguous bindings, malformed syntax, stale checklist locations and paths escaping
the repository (including resolved links) remain visible; explicit files do not erase
a missing selection. No driver, build slot, Git process or run-state write occurs in
this verb.

V/R paragraphs and tables, and PC tables, are read inside `## Verification design`;
checkpoint rows and fenced examples are excluded. Backtick methods, assertion/witness
labels and exclusion canaries are bound to their V/test. Unmapped legacy prose is
reported with original line/column coordinates. One inline `plan-coverage-v1` JSON
fence, or an external checklist, can add exact promises and map an exact legacy
clause. Its schema is `{version:1, items:[{id,test,kind,name,planLine,maps?}]}`;
kinds are `method`, `label`, `canary`, `member`, `empty`, `null`, `value`.
`test` is class-qualified; mappings supplement extraction and cannot remove a label
or canary. The empty string is a valid expected `value`, but never a label.
For prose expectations, map each exact original clause with `maps`, its current
`planLine` and the row's bound `test`; use separate typed items for a member and
its expected value. Remove code formatting from input spellings only. Keep labels
and canaries as obligations and distinguish future mutation errors from baseline
assertions. Stale clause text or coordinates refuse the checklist.

A PC table's separate detecting-filter cell binds one literal method using
`/*/*/Class/Method` or `/*/Exact.Namespace/Class/Method`. Qualified class names
are supported. Wildcards in class/method, OR/category/command selections, multiple
filters and conflicts with explicit methods remain binding findings. Mutation
cells supply no promises; target coordinates still refer to the expected-red cell.
Legacy explicit-method and V-reference bindings remain supported.

Declared V/R census promises are checked against distinct successfully bound method
obligations for the same ID, including continuation rows and additive checklist items.
In the class cell, `<class>` in a code span followed by `(N results)` counts results;
`(N existing)` counts methods. The literal sentence `All N class-qualified methods
in the checklist are required` declares the ID's total method count. Class counts
use the resolved declaring class; qualified/unqualified aliases count once. A class-cell
count is checked only when at least one method binds for that ID and class; a class-only
whole-class run census is skipped. The ID-wide total is still checked with zero bindings. A deficit
or surplus emits `CHECKLIST_COUNT_MISMATCH` at the declaration and exits 1. Result
counts support single-result methods and method-level `Arguments` attributes. Dynamic
data sources, Matrix/Repeat or class/parameter expansion emit
`CHECKLIST_COUNT_UNMAPPED` (exit 1); syntax cannot invent their execution census.
This checks the declared promise set. It does not infer that every current method
selected by a class filter belongs in an older frozen checklist. No declarations are added to the JSON report schema,
and a matching census adds no output records.

Version 1 checklists may explicitly promise `selectedClassCensus: true`, in either
the inline fence or the external checklist. Absence and `false` preserve legacy
behavior and report bytes. The optional key accepts only JSON booleans; duplicate
keys, unknown keys, malformed values and conflicting checklist sources remain
`CHECKLIST_INVALID` (exit 2). Upgrade/rebuild the reader before adding the flag to
a plan; older readers reject it. Historical plans remain unchanged.

The promise compares the union of directly declared TUnit test methods selected
by whole-class checkpoint rows with distinct successfully bound checklist
`method` items across V/R/PC IDs. It compares declaration identities, rather than
counts or display aliases. Prose, labels and canaries do not supply roster members;
helpers and unselected methods are extra members. Parameter expansion contributes
one declared identity; declared-result count validation remains independent.
`CLASS_CENSUS_MISMATCH` (exit 1) identifies each missing/extra method and its source
location. Missing members retain the earliest selecting CP's original location;
extra members retain the checklist requirement's location. A match adds no records
and does not prove PC reachability or change the missing-assertion count.

Static census supports assembly `*`, namespace `*` or an exact namespace, literal
or OR class operands and trailing class wildcards, and method segment `*`.
Project boundaries, nested class identities and partial declarations are retained;
loading a file does not select its siblings. Recognized `Test`/`TestAttribute`
spellings include TUnit.Core and global qualification. Command/category/method
selection, unsupported filter syntax, aliases/shadowing, unresolved/inherited-test
base chains, generated/conditional declarations, Skip and Explicit eligibility
produce `CLASS_CENSUS_UNMAPPED` (exit 1). A proven test-free base chain is admitted
without adding supporting files to serialized sources. An opted-in pure analyzer
without trusted checkpoint selection context is unmapped. Census never executes
discovery or loads an assembly; malformed manifest/path/source inputs remain exit 2.

Roslyn parses syntax only. Supported Shouldly signatures distinguish the actual,
expected, Case and custom-message arguments. Setup/comments/another method cannot
satisfy an assertion. Literal aliases, interpolated literal segments, literal-array
loops and uniquely bound local helpers supply bounded evidence; unknown assertion
syntax/helper dispatch is unmapped. A member must occur in the asserted actual;
a canary must occur in an exclusion assertion; an expected value must occur in the
expected expression. Null and empty are separate obligations. No general dataflow,
whole-object expansion, semantic compilation or helper execution is performed.
Called local functions expand in place, resolving the nearest enclosing block
including declarations after the call. Uncalled bodies, sibling blocks and other
tests supply no evidence; local names shadow class helpers and member-access calls
cannot bind local functions. Block/expression bodies, literal positional/named/
default arguments and terminal params arrays (zero, explicit array or multiple
trailing arguments) are supported. Captured literal aliases and parameters retain
lexical ownership and shadowing. Cycles, depth overflow and ambiguous dispatch
remain findings. Class/partial helper expansion and the test census are unchanged.

Every PC prints its method/target and predecessor findings. Unlabeled predecessors
are issues; earlier different stable labels are advisories. Every PC, including
`static-labeled`, carries `reachability=unproven`. **A static pass means the syntax
checks passed, not that a mutant reached its intended assertion.** SourceLanding
Mutation remains the separate method-scoped proof.

Exit 0 means no missing/unmapped obligations or PC issues (advisories may remain),
1 means completed analysis with findings, and 2 means invalid input/analysis. Text
uses JSON-quoted string fields, a `PLAN-COVERAGE` header and `PLAN-COVERAGE-END`
footer; JSON is a separate schemaVersion 1 report with selected paths/classes,
SHA-256 digests, obligations/matches, exclusions, diagnostics, PC statuses and counts.
Neither form includes timestamps, source bodies or environment values.

The derived inline checklist uses LF separators on every OS, preserving its final
newline, blank lines and per-line whitespace. Its digest is therefore independent
of the host's native newline. Plan, selected source and external checklist digests
still hash the original decoded text as UTF-8: LF and CRLF inputs have distinct
provenance. Comparing complete report bytes across OSes requires identical input
bytes; the combined input digest includes the checklist digest. The seven raw
coverage golden fixtures are individually pinned to LF in `.gitattributes`, with
their provenance hashes unchanged (CARD-1013).

Code builds the changed tool once through a leased bootstrap, then runs the verb
before final ordinary checkpoints. For example:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label coverage-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-coverage-tool/ --nologo
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-coverage-tool/ -- coverage --plan <plan.md>
```

Paste the **complete** text output before final CHECKPOINT lines and give a disposition
for remaining advisories. If findings cause a tracked edit, commit/push and regenerate
coverage/checkpoint evidence at the resulting source. Review builds at its reviewed
source, reruns the same plan/selection/checklist, and compares digests/diagnostics;
it does not accept Code's paste as its own run. Lint findings are evidence to reconcile,
not an independent Final Review gate: the operator's regression/fail-open standard
continues to govern.

### CARD-0490 phone-home runner and native PC-28–31

Opt-in Linux Grok phone-home is `docker-compose.runner-grok.yml` plus `scripts/verify-phone-home-grok.ps1`. It never publishes a runner port and never targets production 17202–17205. `PhoneHome__ServerOrigin` is launch env only (`PHONE_HOME_SERVER_ORIGIN`); compose/Dockerfile do not hardcode it. V-7 allocates an isolated server/Postgres, writes `.antiphon/card0490-live.json` (example: `tests/fixtures/card0490-linux/card0490-live.example.json`), and tears the instance down. Local Docker injects `http://host.docker.internal:<isolated-port>`. `-Placement server2` injects `http://<desktop-tailscale-ipv4>:<isolated-port>` (this desktop is `100.79.51.37` / `desktop-ktlkpif`; `host.docker.internal` on server2 is server2 itself). The image installs Grok 1.0.41 from `https://x.ai/cli/install.sh` (CARD-0575) and does not `COPY` credential files. Grok OAuth is a **copy** of primary `GROK_HOME` `auth.json` (optional `config.toml`, `version.json`) into a throwaway directory, bind-mounted **read-only** at `/state/grok` via `PHONE_HOME_GROK_HOME`. Session writes use `PHONE_HOME_GROK_SESSIONS`. Never live-mount the primary store. Not `XAI_API_KEY`. QEMU `assets.lock.json` pins are a native-lane obligation and do not block the isolated container.

A silent phone-home peer for one operation is `PhoneHomeScriptedPeer.SilentFor(operation)` (that operation's reply is null; every other operation keeps its scripted or default reply). CARD-0633's preparer and starvation tests use it for `WorkspaceMirror`.

`scripts/verify-phone-home-grok.ps1` drives the live turn through isolated Postgres, an isolated local SessionRunner, an isolated server, then the phone-home container. (1) `SessionRunner:BaseUrl` is that isolated runner, never the dead `http://127.0.0.1:1` guard and never 17204. (2) The server starts first, the standing Grok agent is created (and the server restarted with its id if needed), then the container, so both phone-home epochs are 1. The harness waits for `dispatchEligible=true`, not merely `available`. (3) The first standing start is `fresh: true`. It then waits for `liveSession.status=Running` (not `agent.status`, which is Running as soon as launch is queued) before queueing `Reply with PHONE_HOME_OK_<nonce> and do not use tools.`

What blocked V-7 was CARD-0594's launch deadlock, not a mount or visibility problem ([investigation](investigations/2026-09-22-card-0594-linux-pty-host-launch-deadlock.md), [plan](superpowers/plans/2026-09-22-card-0594-linux-pty-host-launch-deadlock-plan.md)). `LaunchDetachedAsync` waited for the intermediary's stdout **and** stderr EOF, which the detached host kept open through duplicate descriptors of the inherited stdio, so the runner's `Connect` only began after the host had already exited at its 30s launch timeout and then spent its 15s budget retrying `ECONNREFUSED` against the orphaned socket file (30 + 15 = the 45s the launch took). Three fixes: `PosixProcessSpawner` closes every descriptor above 2 that aliases the original 0/1/2, `PtyHostLauncher` gates on the intermediary's exit plus its pid line, and `PtyHostServer` disposes the listening stream on the cancelled accept so the socket file is unlinked. Evidence is `pwsh -NoProfile -File scripts/verify-card0594-linux-launch.ps1 -Image <tag> -Expect baseline|fixed` (Docker Desktop only, never binds 17202-17205): it grades a real `POST /sessions`, the intermediary's pipe EOF against a live host, and the orphan socket file a timed-out host leaves behind. The live V-7 turn itself is still to be re-run. Do not treat agent.Status=Running as a ready Linux host.

`LinuxPtyHostLauncherTests` (and the other `RequireLinux()` classes) still have no execution lane on this Windows host until CARD-0605 gives them one, so every method in them skips; CARD-0594's Windows-executing launcher and server tests are written platform-neutral so that lane runs them unchanged.

#### The production `server2` runner (CARD-0604 Cut A)

The persistent deployment is `docker-compose.server2-runner.yml` (project `antiphon-runner`) on
server2, registering with the **production** desktop server over
`https://antiphon.desktop.codeperf.net`. It is the only standing Antiphon process there: no server
and no Postgres, because the throwaway stack a session needs is the nested child it creates itself.
The CARD-0490 `grok-linux` canary keeps working unchanged against its own isolated server through
`verify-phone-home-grok.ps1`; that harness's settings block now carries `AllowDelegatedTasks=false`,
because a canary is one pinned agent and nothing else.

Turning it on is an operator step and a production change, not something a test run does. In the
**main checkout** (`C:\src\Antiphon`, never a worktree):

The user-secrets go in the **`antiphon-server`** store (`dotnet user-secrets set <key> <value>
--id antiphon-server`), not the AppHost's. The AppHost forwards exactly one key
(`AntiphonMessaging:BootstrapServers`) to the server process; a `PhoneHomeRunner:*` key set against
`--project Antiphon.AppHost` is silently inert. Set the POSIX-path values from PowerShell, not Git
Bash - MSYS rewrites a bare `/work` argument into `C:/Program Files/Git/work` (CARD-0604 CP-6a).

1. `git pull --rebase`, then set the user-secrets in the `PhoneHomeRunner:Runners` map (CARD-0727
   D-2). `Runners:server2` carries the production entry: `AllowDelegatedTasks=true`,
   `HostWorkspaceRoot=C:\src\Antiphon`, `RunnerWorkspace=/work`,
   `RunnerRepository=/work/repos/antiphon`, `MaxCapacity=10`, the child homes, the probe flags,
   `CallbackOrigin=https://antiphon.desktop.codeperf.net`, and `SharedSecret` from the value
   `deploy-parent` generated on server2 (see [agent-credentials.md](agent-credentials.md) §5 — it
   never crosses the SSH bridge into a script or an evidence file). `Runners:server2-temp` repeats
   those same values, including the same `SharedSecret`, with `DisplayName` `server2 (temp)`. The
   temp entry stays configured: offline it is unavailable and costs nothing. Also
   `PhoneHomeRunner:Enabled=true`. The legacy `AllowedRunnerId` key is only the import the catalogue
   normalises when the map is empty; do not keep a second shape beside the map.
2. `pwsh -NoProfile -File scripts/restart-apphost.ps1`.
3. Confirm `GET /api/version` is the SHA you just built, then
   `GET /api/session-runners` lists `desktop`, `server2` and `server2-temp`.
   `GET /api/session-runners/server2/status` reports `available: true` and
   `dispatchEligible: true` within ~120 s of the runner reconnecting. Before `server2-temp`
   connects, its status is 200 with `available: false`, `dispatchEligible: false` and null
   `runnerStoreId`, `processBootId` and `buildVersion`. An id that is not in the map is 404, and
   that 404 is not eligible (CARD-0729). A passing `/health` is not that confirmation.

Once eligible, `scripts/delegate.ps1 -Runner server2 -Worktree ...` routes an ordinary Grok or
Claude task there (Claude needs the runner's `CLAUDE_CODE_OAUTH_TOKEN` or the fallback login first;
see [agent-credentials.md](agent-credentials.md) §5 and `GET
/api/session-runners/server2/provider-auth/claude`). The **desktop** worktree is still created and stays canonical: the branch is pushed to
origin, the runner mirrors it at that exact commit, the session works and pushes, and settlement
fast-forwards the desktop worktree (`--ff-only`; a divergence or a dirty desktop tree is a warning,
never a reset). Landing, retirement and residue accounting are unchanged. Card-backed starts,
OnAgent, Shared, ReadOnly, pins and SourceLanding are all refused at create.

An explicit `-Runner server2` may also carry `-Kind Codex` for a Worker task (CARD-0660).

CARD-0812 permits `-Worktree` for another project when its desktop checkout's `origin` is under
the runner's `PhoneHome:AllowedCloneSources` prefix (default `https://github.com/michal-ciechan/`)
and anonymously fetchable. The runner clones it under `/work/repos/<repository-name>` and
advertises `workspaceRepositoryV1`. Before making a secondary mirror, the default probe uses
`git push --dry-run` through the push URL; a receive-pack refusal returns
`phone_home_repository_push_unauthorized`. The current Antiphon deploy key cannot push to another
repository, so each one needs its own server2 push credential.
CARD-0710 places a supported Codex worker by the runtime runner defaults: per-kind, then global,
then the built-in fallback. `Delegation:DefaultRunnerId` is an import input only. The first
missing runtime-defaults row copies it once; after that row exists, including an explicit null
global, editing the key or restarting does not change placement. Settings > Routing and
`GET`/`PUT /api/runner-defaults` are the live control. Do not seed a Codex desktop exception.
`PhoneHomeRunner:ChildCodexHome`
(default `/state/codex`, POSIX absolute, never `/tmp`) is projected as `CODEX_HOME` and needs no
user-secret while the runner's compose home agrees. `PhoneHomeRunner:CodexAuthProbeEnabled`
(default true) asks that runner at create and retry, and a definite signed-out answer is 409
`provider_sign_in_required` with `agentKind: Codex`, `codexHome`, `runnerId` and the
`codex login --device-auth` remedy. The local `codex` definition (`codex.cmd`) is unchanged; the
projection maps it to the image's native `codex`.

PC-28 through PC-31 run in an inherited QEMU/TCG guest via `scripts/test-card0490-native.ps1`. Ordinary Code uses `-Ordinary -Phase baseline` only. Sourced Mutation requires `-BindingFile` and must not downgrade to ordinary. Asset pins live in `tests/fixtures/card0490-linux/assets.lock.json`; any `pending-operator-pin` there refuses the lane with exit 4.

QEMU itself is the pinned weilnetz `qemu-w64-setup-20241220.exe` bundle (9.2.0); extract it and point `CARD0490_QEMU_DIR` at the result. The boot disk is **built, not downloaded**: `pwsh -NoProfile -File scripts/build-card0490-bootdisk.ps1 -QemuDir <dir> -Smoke -WriteLock` generates a 512-byte MBR from a table in the script, pads to a 64 MiB raw image and converts it with the pinned `qemu-img`, so the bytes are identical on every machine. It refuses to run if the QEMU binaries do not match their `assets.lock.json` hashes. The image carries no application source, no compiled Antiphon assemblies, no credentials and no saved VM memory. Its guest contract on COM1 is: emit `CARD0490-BOOT-OK`, echo every received byte, and on `q` emit `CARD0490-HALT` and power off through ACPI. `-Smoke` asserts all three through the plan's own `-blockdev`/`virtio-blk-pci` overlay invocation. That boot disk proves the guest boots and is drivable; it does **not** yet carry the SDK `10.0.204`/net9 runtime/offline NuGet payload that sourced PC-28–31 execution needs.

### Simulating a stale `index.lock` (CARD-0543)

Only inside `ScratchGitRepo`, `LandingGitFixture` or `LandingSafetyHarness` temp directories.
Resolve the path with `git rev-parse --path-format=absolute --git-path index.lock` in the
checkout under test; create it with `File.WriteAllBytes(path, [])`; age it with
`File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(1))`. Never read or
write `C:\src\Antiphon\.git`. Script coverage: `pwsh -File scripts/test-apphost-git-index-lock.ps1`
(scratch repo under `$env:TEMP`; does not touch `logs/apphost.*.lock`).

### CARD-0443 Windows cleanup qualification

Run the explicit Windows class against the same producer-owned build, with fresh results:

```powershell
$c443WindowsResults = Join-Path '.antiphon' ('c443-windows-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c443/ -- --treenode-filter '/*/*/WorktreeLockDiagnosticsWindowsTests/*' --report-trx --report-trx-filename run.trx --results-directory $c443WindowsResults
```

The [CARD-0443 plan](superpowers/plans/2026-09-14-card-0443-receipt-backed-cleanup-plan.md)
requires eight named methods. The current [Code checkpoint](investigations/2026-09-14-card-0443-code-checkpoint.md)
implements only the two native-probe methods: owned file and root-directory holders,
actual sharing error 32, unchanged bytes/attributes, and joined child release. These
work without Handle elevation. A green two-case run does not qualify the other six.

The remaining Git/Handle cases require an isolated receipt-backed repository, owned
holder children, an explicitly configured trusted Handle executable, existing elevated
access and completed license setup. Record file/directory calibration and PID/start/path
evidence. Do not auto-elevate or accept the license during a test. Intact transient retry
and partial removal are separate outcomes: retained partial residue cannot count as
intact recovery. Report unavailable prerequisites, missing methods and actual expanded
counts separately. Ordinary qualification uses restored production code; deliberate
PC cycles remain method-scoped work for post-land SourceLanding Mutation.

### Lazy PostgreSQL and restart preflight cache (CARD-0476)

`TestDbFixture` constructs no container for a selection that never reaches a default-store member (`ConnectionString`, `CreateDbContextOptions`, instance `CreateDbContext()`, or `CreateIsolatedSchemaAsync()`). When the selected tests do reach one, `[Before(Assembly)]` awaits that single readiness task before any test runs, so the synchronous getters are already complete and do not hold thread-pool threads across startup (CARD-0646). Explicit connection strings passed to `CreateDbContextOptions` and `new TestDbFixture()` stay inert; `CreateDbContextOptions` is one method, so a call that only passes an explicit string still counts as reaching it, and the CARD-0476 db-free probe defers warmup instead. A CARD-0476 probe whose mode is not `mixed`, and worker children, still construct no container. Assembly teardown is a no-op when the database was never requested. `SessionDeliveryProfileTests` remains `Category("Unit")` and still starts PostgreSQL when that class runs, so the full Unit lane is not a zero-DB benchmark. `ProductionRunnerGuard` and `PtyBackendEnvGuard` stay eager and independent of the lazy task. Every owned-child worker mode (`CodexStartupDeliveryWorker`, `PostLandMutationDeliveryWorker`, `LandQueueRaceWorker`, `CheckCompactionCrashWorker`) is dispatched from the one list in `TestWorkerModes`, inside `TestDbFixture.InitializeAsync` and ahead of the warm-up, so the skip does not depend on assembly-hook ordering; worker children use a parent-owned connection and must not start a private database, and `TestDbFixtureLazyInitializationTests.A_worker_child_exits_before_the_shared_store_warmup` asserts `dbLifecycle=never-requested` per mode. A new `*Worker` with a `Marker` constant must join that list (`Worker_mode_list_names_every_owned_child_worker`).

`RestartFixture.Run` caches only a successful AST preflight, keyed by resolved shell identity plus the hashed copied entry, helper, platform, wrapper, and validator identity. Every accepted `Run` still launches the real entry child. `Script` and `DecodeCapturedMilestones` stay uncached. Resolver identity walks PATH for the first readable regular file and launches that absolute path (skipping zero-byte/reparse WindowsApps aliases). Measured Code-stage Health+Compatibility+Safety selection: 94 passed in 8m 45s into `bin-c476/`. Paired before/after savings for the S3 procedure are left for Mutation/Review if needed; do not treat the historical 24–37 s bootstrap or 143 s Health total as recovered wall.

### dotnet-ef from the repo tool manifest (CARD-0677)

`dotnet-ef` is a repo-local tool pinned in `.config/dotnet-tools.json`, not a machine-global install. Before any `dotnet ef` command in a worktree (Windows or the server2 runner), run `dotnet tool restore` from the repo root, then `dotnet ef migrations add <Name> --project server`. The pin is an exact version whose major matches the server's `Microsoft.EntityFrameworkCore.Design` reference (currently `9.*`, pinned `9.0.20`); bump the pin together with an EF Core major move. `DotnetToolManifestContractTests` guards the exact pin and the major match. The runner image needs no extra layer: restore writes to the runner user's `~/.nuget/packages` and reaches nuget.org the same way package restore does.

### Host stats (CARD-0718)

Each session runner samples host health every five seconds into a 360-slot, 30-minute memory
ring; the server polls every five seconds and keeps only the latest snapshot. No sample is
written to PostgreSQL. The server exposes `/api/hosts/stats`, the runner exposes `/host-stats`,
and the Hosts page joins SignalR group `hosts` for `HostStatsUpdated`. A missing observation is
null, not zero. On Windows, the displayed page-file figures are commit charge and commit limit.

The 2026-09-26 server2 baseline measured 0.143 ms per read of `/proc/stat`, `/proc/meminfo`,
`/proc/loadavg` and two volume stats; a full `/proc` process sweep measured 1.17 ms and is
deferred to Round 2. CP-4's full sampler check compares runner process CPU with the sampler on
and off across two 60-second pairs; its acceptance is under one percentage point of one core.
On this Linux lane on 2026-09-26, CP-4 measured off `0.233, 0.267` %, on `0.800, 0.967` %,
and mean delta `0.633` percentage points (24 cores; `on-samples=15`, disabled route 404).

| Process | Setting | Default / purpose |
|---|---|---|
| runner | `SessionRunner:HostStats:Enabled` | `true`; `false` answers host-stats 404 / unsupported |
| runner | `SessionRunner:HostStats:IntervalMs`, `RetentionMinutes` | `5000`, `30` |
| runner | `SessionRunner:HostStats:ProcessSampling`, `Volumes` | `true`; current directory and session log volume unless overridden |
| server | `HostStats:Enabled`, `HostStats:PollIntervalMs`, `HostStats:StaleAfterMs` | `true`, `5000`, `15000` |
| server | `HostStats:RequestTimeoutMs`, `HostStats:SeriesTimeoutMs` | `3000`, `5000`; single-attempt reads |

### Build slots (CARD-0589)

Nothing else bounds how many `dotnet build` / `dotnet run --project tests/*` drivers, MSBuild worker nodes, compilers and test hosts run at once across the sessions on one host; the outage behind CARD-0589 had 203 build processes and 1.1 GB free. Three layers do, cheapest first. Plan: [2026-09-25-card-0589-build-fanout-cap-plan.md](superpowers/plans/2026-09-25-card-0589-build-fanout-cap-plan.md).

**`Directory.Build.rsp` at the repo root** carries `-nodeReuse:false`, so every invoker (a delegate's raw command, the wrappers, the land verifier, the nightly, `run-daemon.ps1`'s rebuild) leaves no MSBuild worker node behind. CP-1 measured it on the server2 Linux runner (SDK 10.0.401, 24 cores) building `tests/Antiphon.Messaging.Tests --no-incremental`: nine worker nodes at peak, every one started `/nodeReuse:false`, zero alive ten seconds after the driver exited (before this file, 17 orphaned `nodeReuse:true` nodes held 4.7 GB for up to 3.3 h). A `-maxcpucount:2` line in the rsp was **ignored**: the SDK prepends its own bare `-maxcpucount`, the diagnostic log still showed `MSBuildNodeCount = 24` and the peak stayed at nine. On the command line `-maxcpucount:3` gave `MSBuildNodeCount = 3`. So the rsp holds no count (`DirectoryBuildRspTests` pins that) and the per-build count comes from the grant. `UseSharedCompilation` stays on (the shared `VBCSCompiler` idles out after ten minutes).

**The session runner brokers a host budget of build/test driver leases** at `/build-slots` (desktop `http://localhost:17204/build-slots`, the server2 container `http://127.0.0.1:8080/build-slots`). One lease is one driver invocation, held by the wrapper process that asked for it: `POST` grants `{ leaseId, maxCpuCount, occupied, budget, expiresAtUtc }` or answers 409 `build_slot_busy` (`occupied`, `budget`, `queuePosition`, `retryAfterMs`) or 409 `build_slot_memory_floor` (`availableMb`, `floorMb`); `DELETE /build-slots/{leaseId}` releases (404 `build_slot_unknown` once gone); `GET /build-slots` lists budget, occupancy, live memory, leases (`holderAlive`) and waiters. Waiters are served in FIFO order and a waiter silent for 60 s loses its place. A grant is refused while live available memory (`GlobalMemoryStatusEx` / `/proc/meminfo` `MemAvailable`) is below the floor, even with a free slot; a held lease is never revoked. The runner reaps a lease whose holder pid died or was recycled, or that is past its 90-minute TTL, on every acquire and every 30 s, and logs each reap. It kills nothing.

Settings `SessionRunner:BuildSlots` (`Enabled`, `MaxConcurrent`, `MaxCpuCount`, `MinAvailableMemoryMb`, `LeaseTtlMinutes` 90, `RetryAfterMs` 15000, `WaiterSilenceMs` 60000, `SweepIntervalMs` 30000):

| Host | `MaxConcurrent` | `MaxCpuCount` | `MinAvailableMemoryMb` | Where |
|---|---:|---:|---:|---|
| desktop | 2 | 4 | 6144 | code defaults (`BuildSlotSettings`) |
| server2 | 4 | 6 | 16384 | `docker-compose.server2-runner.yml` `SessionRunner__BuildSlots__*` ([docker-stack.md](docker-stack.md)) |

**How a delegate takes one.** `scripts/run-checkpoint.ps1` takes a slot itself for every row, `-NoBuild` rows included (the test host and its Postgres are the memory), after input validation and the fresh results directory, builds with the grant's `-maxcpucount:N`, and releases after the run; its `CHECKPOINT` line ends `slot=<granted|unleased|unlimited|skipped> waited=<s>s`. Any other driver goes through the wrapper:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label mutation-shard-1 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-x/ --nologo
```

It adds the grant's `-maxcpucount:N` to `dotnet build|test|publish|pack|msbuild` unless the command already names `-m`/`-maxcpucount`, runs the command in the foreground and exits with its exit code. It never adds the switch to `dotnet run`: measured on SDK 10.0.401, `dotnet run -maxcpucount:2` hands `-maxcpucount:2` to the program as an argument. A wrapped `dotnet run` is leased and prints a `BUILD SLOT note:` line; build first and run with `--no-build` to cap its nodes. Under `pwsh -File` the wrapper reads its own raw command line after `--`, because the PowerShell binder splits `-m:2` into `-m` and `2`.

The wrapper starts native executables through `ProcessStartInfo.ArgumentList`, passing each argument literally and setting the child's working directory to the caller's PowerShell location. This avoids PowerShell expanding a Linux TUnit filter such as `/*/*/*/*[Category=Unit]` into filesystem matches before `dotnet` starts. A resolved `.ps1` target uses the same literal launcher with the PowerShell 7 binary under `$PSHOME`, `-NoProfile -NonInteractive -File`, the script path and unchanged argument tokens. The child binds named parameters, inherits environment and console streams, and returns its process exit code (explicit `exit N` gives N; normal completion gives 0; a terminating or binding error gives 1). It does not inherit parent PowerShell functions, variables, aliases, preferences or live objects. Scripts that need a failing exit after a nonterminating error must throw or exit nonzero. The wrapper polls for completion so Ctrl+C can run cleanup, and kills a still-running child tree before releasing its lease. Windows `.cmd`/`.bat` shims retain call-operator execution. The offline `C589_COMMAND_SHIM` follows the native launch path. Its harness logs `CWD`, `PID`, and JSON `ARGV` lines before `CMD`, so tests can check the child's directory and exact tokens. `scripts/test-client.ps1` still uses the call operator for native `node`; a glob passed as a Vitest filter can expand on Linux when it matches files under `client/`.

On Windows, a resolved `powershell.exe` child is the one environment exception (CARD-1048): the wrapper removes only that child's `PSModulePath` before launch, so Windows PowerShell 5.1 reconstructs its own default paths and can autoload `Get-FileHash`. Executable leaf matching is case-insensitive. The wrapper/caller's environment, every other variable, and PowerShell 7 children retain their inherited values. Process-only custom 5.1 module roots must be set inside the child script; no user, machine or registry settings are changed. Indirect command-shell descendants and renamed executables are outside this exception.

Keep one slot-owning layer: invoke `scripts/run-checkpoint.ps1` directly with `pwsh -NoProfile -File` because that driver takes its own lease. Do not put it behind `build-slot.ps1`; a child checkpoint would wait for a second lease while the wrapper holds the first. Use the wrapper for script targets that rely on it to acquire their lease, quoting each path or value containing spaces in the caller's shell.

The wait is visible in the transcript: `BUILD SLOT waiting label=<l> position=<n> occupied=<o>/<b> elapsed=<m>m` (or `reason=memory_floor available=<a>MB floor=<f>MB`) on each new reason and once a minute, then `BUILD SLOT granted lease=<id> waited=<s>s maxcpucount=<n>` and `BUILD SLOT released lease=<id> held=<s>s`. Two failure modes:

- **Timeout** (busy or below the floor for the whole `-SlotWaitMinutes`, default 45): `BUILD SLOT timeout after 45m position=<n>`, **exit 4**, nothing built and no `dotnet` call. Report the row as not run, or end `blocked`; never retry it unleased or with `-NoSlot`.
- **Unreachable runner** (connection refused, a timeout, or an old runner answering 404 on `/build-slots`) for 60 s: the command runs unleased at `-maxcpucount:4` and prints `BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=<no answer|http 404>`, so Review can tell a budgeted run from an unbudgeted one. A delegate stalled behind a restarting runner is the CARD-0448 shape; the outage needs many concurrent builds, which nothing dispatches while the runner is down.

`ANTIPHON_BUILD_SLOTS_URL` overrides the endpoint (an isolated runner; the tests' loopback broker). `-NoSlot` (`BUILD SLOT skipped by -NoSlot`, no lease, no `-maxcpucount`) is for an operator shell only. `SessionRunner__BuildSlots__Enabled=false` makes every acquire answer `unlimited` (`BUILD SLOT unlimited maxcpucount=<n>`, nothing held): the rollback lever, with deleting `Directory.Build.rsp` the other. A raw driver typed outside the gate still runs (with `-nodeReuse:false`); Review flags it as a checkpoint defect, the same shape as an unlisted run.

**The desktop land verifier** takes a slot through the local runner (`SessionRunnerBuildSlotGate`) for its build and test run with the same rules: it waits at most `Landing:BuildSlotWaitMinutes` (30; a timeout is a `build-slot` verification failure with nothing built), builds with the grant's `-maxcpucount`, and an unreachable runner is never a land failure: it builds unleased at `-maxcpucount:4` after `Landing:BuildSlotUnreachableGraceSeconds` (60). Its `BUILD SLOT` lines go to the verifier observer and the server log with the land correlation. The nightly joins in CARD-0589 Round 2, with a detect-only build watchdog and the phone-home read/set of the budget.

Offline seams, tests only: `C589_SLOT_SHIM` (a script answering in place of the HTTP call; `scripts/fixtures/c589-slot-shim.ps1` scripts `granted|unlimited|busy|memory_floor|notfound|unreachable` per `C589_SLOT_SCRIPT`), `C589_SLOT_WAIT_SECONDS`, `C589_SLOT_RETRY_MS`, `C589_SLOT_GRACE_SECONDS`, and the wrapper's `C589_COMMAND_SHIM`. Harnesses: `scripts/test-run-checkpoint.ps1` (`Test-C589_*`, `RunCheckpointScriptTests`) and `scripts/test-build-slot.ps1` (`BuildSlotScriptTests`); the cross-process proof is `BuildSlotEndToEndTests` against a loopback broker, never a production runner.

## Combined class filters (CARD-0403)

For one invocation covering several named classes on the pinned TUnit 1.44 runner, use
`/*/Antiphon.Tests.Application/(ClassA*)|(ClassB*)/*` and verify each intended class in the
fresh TRX. Each OR operand needs its own parentheses ([TUnit filter syntax](https://tunit.dev/docs/execution/test-filters/)).
The trailing wildcards also prevent this version's source-generated discovery from treating
the entire OR expression as one literal class name ([pinned hint extractor](https://github.com/thomhurst/TUnit/blob/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Engine/Services/MetadataFilterMatcher.cs#L164)).
Check that the suffix patterns selected only the intended classes; do not infer coverage from
exit zero. A failed filter can produce a fresh TRX with zero tests (native exit 8).
When a land test injects a Git fault, scope it to the target-branch observation, because CARD-0488's resolver observes the source branch first (CARD-0567 groups 1 and 4).
The method-segment OR form `/*/*/Class/(MethodA*)|(MethodB*)` works on the pinned TUnit 1.44, while a bare `|` between two full paths does not: it silently ran the whole `AgentTaskLandRefusedRetryTests` class in one case and zero tests in another.

`--list-tests --treenode-filter ...` is not scoped-execution evidence on this runner: CARD-0403
observed all 5389 discovery entries even with a single-class filter. Require actual executed
method names, outcomes and nonzero counters in the execution TRX. A positive-control red run
must contain the expected assertion failures; a build failure, fixture error or zero-test run
does not satisfy it.

When restoring fixed source after a baseline red run, refresh its last-write timestamp.
PowerShell `Copy-Item` preserves the backup's older timestamp, so an incremental build can
reuse the baseline DLL even though the source contains the fix. Verify the new method in
the test output's DLL before accepting the green run (CARD-0412 D6).

## Mutation-stage positive-control execution (CARD-0451)

The default is Code ordinary V/R -> separate ordinary Review -> confirmed land -> SourceLanding
Mutation. Review checks the pending PC design and ordinary evidence; it does not require executed
PCs. Follow the companion commissioning and triage recipe in docs/orchestration-loop.md. Sourced
snapshots never commit/push amendments; keep evidence externally and request separate repair work.
Use local inherited execution only; never grant snapshot access to an external executor, broker,
remote service or pre-existing process.

There are two supported producers (CARD-0604 D-17/D-19). Windows local inherited execution is
`windows-job-v1`. The persistent server2 runner is `linux-cgroup-v1`: `delegate.ps1 -Role Mutation
-Runner server2 -Worktree -SourceLanding <operation>` creates the verification snapshot **on the
runner** at the exact landed sha, runs the battery inside a root-owned cgroup the session cannot
leave, and writes `restoration.json` to the runner's own evidence root under
`/work/repos/antiphon/.git/antiphon/verification/<operation>/<task>/`. The desktop filesystem is
never consulted for such a task: its custody read, its restoration read and its worktree removal
all go through the bound runner. Neither backend is a substitute for the other -- a binding whose
backend is not the one the executing runner advertised is refused at the runner, and a receipt
whose observation method is not the one that backend produces is refused at the server.

A Mutation dispatched to server2 can still fail to start for a reason that is not about custody at
all: `provider_sign_in_required` means the runner's Grok store has no usable session (the operator
runs `grok login` once inside the container, D-16). That is a launch refusal to report, not a
reason to reroute the Mutation to another runner or to the local lane -- the execution is bound to
the runner whose store the reservation named.

Each PC-n still needs red-then-green evidence: apply the planned mutation, observe the
expected assertion failure, restore the fixed source, and observe green. Scope **every** phase
to only that PC's specific test method. Run a baseline before editing and give baseline,
red and restored green separate output and results paths in the task's external verification
evidence root. For a parameterized method, use its method prefix and a trailing wildcard;
inspect the individual argument rows in that phase's fresh TRX. A literal parameter suffix
can select zero tests even when the build completed successfully.

Use the unchanged local `scripts/run-checkpoint.ps1` driver for each phase. In a SourceLanding
snapshot, copy that driver and `scripts/lib/build-slot.ps1` to the external evidence root
with the same relative `lib/` layout before mutating source. The driver runs in the snapshot
working directory, so its test fixture still reads the source under test. For each phase,
set the following values from that PC and invoke the driver once:

```powershell
$phase = 'baseline' # then 'red', then 'green' after exact source restoration
$methodName = 'ExactTestMethod'
$methodFilter = $methodName + '*' # use the method prefix for parameterized rows
$resultsRoot = Join-Path $evidenceRoot "pc1-$phase"
pwsh -NoProfile -File $driver -Name "PC-1-$phase" `
  -Project tests/Antiphon.Tests -OutputPath "bin-pc1-$phase/" `
  -Filter "/*/*/ClassName/$methodFilter" -Expect "ClassName.$methodName" `
  -MinExecuted 1 -ResultsRoot $resultsRoot
$phaseExit = $LASTEXITCODE
```

Await each complete driver and its owned child before editing source or starting another
build in the same SourceLanding worktree. Inspect that phase's `build.log` completion
receipt (`DOTNET build EXIT CODE: 0`), `run.log` receipt and fresh `run.trx` before
accepting its verdict. A partial log without its exit receipt is an incomplete build,
even if it contains ordinary compiler output. An actual failed build or missing TRX is
driver exit **2**; an empty or wrong executed roster is exit **3**. Neither is a PC red.
An intended red is driver exit **1** with the named assertion failure in the fresh TRX;
baseline and restored green are exit **0** with the expected executed roster and nonzero
count. Keep logs and results in the external evidence root, never committed source.
The class-scoped verification guidance above is for regression verification, not individual
PC cycles. For a batch, select only the exact methods for those PCs (separate method-filtered
invocations are fine); never widen to a class, namespace or suite to combine them.

Batch genuinely independent mutations that touch **different files and methods** and cannot
interfere with one another: apply them together, run their specific tests red, restore all,
then run those same tests green. Keep a result for every PC-n in the stage report. Controls
that share a file/method or affect one another must run separately, not in the same batch.
Refresh restored source timestamps/rebuild as described above so green uses restored code.

For large plans (roughly >15-20 PC rows), the Mutation delegate may create one or more additional
worktrees **off the same task branch** and shard independent controls across them concurrently.
Start from the same committed branch tip, using detached worktrees or temporary branches
from that tip; do not force a branch to be checked out twice. Each shard has its own source,
build outputs and results, and can use method-scoped batches. This is local test concurrency,
not permission to sub-delegate. Own and await every run before ending the turn.

Concurrent `Antiphon.Tests` shards rely on per-test DB schema isolation and the existing
assembly-local `ParallelLimiter<ProcessSpawnLimit>` model. That limiter serializes process
spawns within one assembly process, not across processes; do not co-schedule these shards
with `Antiphon.Agents.Pty.Tests`/FakeClaude. Controls depending on shared external state
without isolation must remain serial. Restore all temporary mutations. Production/test repairs return to Code for ordinary V/R
and a new post-land SourceLanding pass; sourced Mutation keeps amendments in external evidence.
A SourceLanding task has one recorded managed snapshot: do not create extra unbound snapshot
worktrees or temporary branches. For legacy explicitly commissioned unsourced sharding only,
remove owned extra trees after preserving evidence. The sourced tree remains for explicit guarded
CleanupVerification, never normal landing.

While a long run is in flight, avoid tight identical-command polling loops. Space status
checks out and use the wait to read/investigate the next planned fix. CARD-0450 still applies:
do not edit source in a running worktree; wait for its run to finish or stop it before editing.
Commit before each big run and record the commit and temporary PC mutations it exercised.

## Hand-built ServiceCollections and the delegation worktree graph

- **A test harness that builds its own `ServiceCollection` and resolves `AgentTaskDispatcher`, `DelegationWorktreeService`, `AgentTaskReplyService`, `DelegateBindRefusalRecovery` or `AgentReviewCheckpointService` registers the git graph through `DelegationTestServices` (`tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs`), never by hand** (CARD-0297). `services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = … })` is the one registration for `IOptions<GitSettings>`, the real `WorktreeManager` and `GitService`, `GitWorkspaceService` and the scoped `DelegationWorktreeService`; `services.AddGitWorkspaceService()` is the reply-only / card-service form. Both are `TryAdd`, so a harness that already holds a fake `IWorktreeManager` (as `BridgeQueueHarness` does) keeps it, and calling the helper twice is a no-op. Do not add `AddSingleton<GitWorkspaceService>()` next to a local `CreateDispatchHarness`, and do not register `GitSettings` separately when the helper is called — pass it in. The helper assumes `AddLogging()` and a `TimeProvider` are already registered, which every dispatcher harness has. Evidence: when `DelegationWorktreeService` gained a `GitWorkspaceService` constructor dependency (c4d7e0d, 2026-08-26), eight copied harnesses went red at `GetRequiredService<AgentTaskDispatcher>()` with `No service for type 'GitWorkspaceService' has been registered` — `PinnedAgentKindTests.T3` was the one that got noticed — and seventeen more each grew their own one-liner with a CARD-0230 comment. `DelegationTestServicesTests` pins the contract: logging + a clock + the helper resolve the whole graph, and a prior one-liner or fake is not duplicated. `DelegationHarnessCensusTests` fails a new dispatcher or reply harness that skips the helper, and names the file (CARD-0244).
- Seeded settlement turns must end with `DelegationReportFormatter.ReportToken(id, "done")` unless the test is about the nudge.

## Nightly

**Deployment audit, 2026-09-11 (CARD-0487): the named Windmill script and schedule
are absent.** A read-only census of the live Windmill database found no matching
script, schedule or retained job. The latest local `last-run.json` is a failed
September 4 client-only feature-branch run. The current script also omits
SessionRunner, PtyHost and Messaging test projects. Do not credit this as an
operational full-suite backstop. See the
[audit and prerequisite plan](superpowers/plans/2026-09-11-card-0487-scoped-dispatch-testing-plan.md).
This finding does not activate that plan's reduced-dispatch policy.

The implemented bootstrap is `scripts/nightly-run.ps1`; its intended Windmill
registration is `u/lndcobra/antiphon_nightly_tests`, 00:30 Europe/London. Checked-in
payloads live under `scripts/windmill/`. Do not add a local Windows Scheduled
Task. **Reduced dispatch verification stays inactive** until S4 qualification
(a manual full unattended green plus a real subsequent 00:30 scheduled green,
independent health monitor, and recorded notification receipt).

CARD-0487 S1-S3 (landed, policy inactive): `tests/test-execution-policy.json`
is the execution universe (now schemaVersion 2 plus a recomputed `policyHash`).
Default unattended suites are `antiphon`, `session-runner`, `pty-host`,
`agents-pty`, `messaging`, `client`, and `scripts`. `e2e` is a manual exception
in the nightly lane. CARD-0599 added `profiles.nightly` (those same seven) and
`profiles.rc` (those seven **plus `e2e`**); the selected profile is authoritative
for required suites, an unknown profile refuses, and in a profile lane `OptIn`
alone no longer excludes a case - only a declared exclusion row with a reason and
an owner does. Owner: [release gates](release-gates.md).
Producer-owned `StateRoot` defaults to `C:\Antiphon\nightly` (Windows backslash
paths). Lock acquisition is atomic (`FileMode.CreateNew`); a live owner is never
replaced by age. `last-run.json` is the last attempt; `last-complete-green.json`
advances only for scheduled master runs with `coverageComplete`, `testsPassed`
and `reportDelivered`. Unchanged-SHA skipping is removed.

CARD-0545: independent outage detection and notification belong to the **nightly
watchdog** ([owner doc](nightly-watchdog.md)), a systemd-supervised process on an
operator-chosen host outside both the Windows host and Windmill's failure domain,
named only in an untracked deploy profile. The former Windmill health-monitor
definition is deleted. `scripts/nightly-health.ps1` is now the Windows-side local
readiness evaluator, `u/lndcobra/antiphon_nightly_readiness` (desktop tag, every 30
minutes, `-ReadinessConfigPath C:\Antiphon\nightly\readiness-config.json`); it folds
the watchdog snapshot into `Health` (`watchdog-unreachable`, `watchdog-stale` beyond
20:00, `watchdog-malformed`, `watchdog-identity-mismatch`, `watchdog-outage-open`) and
records `Identity.WatchdogInstanceId`/`WatchdogHeartbeatAt`; the server reader requires
the receipt's `watchdogInstanceId` to equal it. Its PowerShell notification ledger
remains only as harness-guarded logic: the production Windmill notification sink is
removed.

CARD-0544 D-6 readiness (`Test-NightlyMonitorHealth`): `ReadyForDeferral` is
health plus a valid **scheduled** green for the London due date, never the age of
`completedAt`. A green is a scheduled Windmill job whose result names the native
run and due date (jobs/list rows map through `ConvertFrom-NightlyWindmillJob`:
queued/running/success/failed/unknown; string booleans or a missing result are
unknown) and whose native state (`last-run.json` or `last-complete-green.json`) is
that run, `trigger=scheduled`, master, coverage complete, tests passed and report
delivered. Before 08:00 London the previous due day's green bridges while today's
run is pending (a run with activity in the last 60 minutes is pending; older is
`stale-progress`); a newer failed/incomplete completed attempt or failed scheduled
job revokes it at once; at/after 08:00 only today's green counts. Start grace is
overdue at exactly 30 minutes. Manual runs never stand in for the scheduled run.
Monitor freshness is the server reader's check on `last-monitor.json` `RecordedAt`
(0-60 minutes); the monitor also records `Identity` (repository, project, policy
and script hashes, scheduled run ID, job native run ID, Windmill job ID) from
`-RepositoryPath`/`-ProjectId` or `ANTIPHON_NIGHTLY_REPOSITORY_PATH`/
`ANTIPHON_NIGHTLY_PROJECT_ID`. CARD-0545 D-10: `nightly-run.ps1` prints, as its last
stdout line on every exit path (refusals included), one compact JSON record
`{nativeRunId, sha, ref, trigger, localDueDate, policyHash, coverageComplete,
testsPassed, reportDelivered, exitCode, summaryPath}`, which Windmill stores as the job
result; `last-run.json` and `last-complete-green.json` carry `localDueDate` (the London
date of the run start). `jobs/list` rows carry no result, so the production adapter
fetches `jobs_u/completed/get_result/{id}` for at most the five newest completed
scheduled rows and derives `scheduledFor` from `localDueDate`; an unfetched,
unfetchable or incomplete result stays `unknown`. `-NoReport` never claims delivery.
Interim verification stays disabled (`InterimVerification:Enabled=false`) until the
operator-run qualification publishes its receipt.

It syncs an **isolated** clone at `C:\Antiphon\nightly\checkout` to
`origin/master` (never `C:\src\Antiphon`, never a worktree), builds (`npm ci`,
`npm run build`, client lint, `dotnet build Antiphon.sln`), then runs the
policy suites sequentially (native project groups one at a time). Logs live
under `<StateRoot>\logs\<yyyy-MM-dd-HHmm>-<runId>\` (`summary.json`, per-suite
logs, `build.log`). Headed/live opt-in names are cleared in child environments;
`ANTIPHON_BROKER_TESTS=1` is set only for the messaging suite.

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
nightly schedule. `-Suites e2e` is **not** a way to run E2E: `nightly-tests-impl.ps1`
skipped `mode=manual` unconditionally, so the selection was accepted and then
dropped. Use `nightly-run.ps1 -Profile rc`, which requires E2E and runs it with
real Playwright/bundle prerequisites - a missing browser or stale bundle is red or
incomplete, never a skip. See [release gates](release-gates.md). Per-project Slow registries are
`tests/<Project>/slow-tests-allowlist.txt` (FQN plus adjacent reason). Slow is
a cost marker, never Skip. Offline harnesses: `scripts/test-nightly-run.ps1`,
`scripts/test-nightly-tests.ps1`, `scripts/test-nightly-report.ps1`,
`scripts/test-nightly-health.ps1` (`-Case C487_GNNN`, `-Case C544_<Name>` or `-Case C545_<Name>`, `-ResultsDirectory <fresh>`),
`scripts/test-deploy-nightly-watchdog.ps1` (`-Case C545_<Name>`). `NightlyVerificationContractTests` runs each C544/C545
harness case and, in-process on `C545World`, the watchdog's carried CARD-0544 notification controls;
`NightlyWatchdogCoreTests` covers the watchdog core (clock, evaluator, ledger, body, snapshot, transport, reader).

## Asynchronous outcome delivery verification (CARD-0467)

StageTestDesign requires a producer/destination/persistence/recovery/receipt inventory,
joined by a durable identity. Real queue tests cover busy and already eligible callers
and each crash/enqueue boundary; session receipt is a matching complete UserPrompt.
StageReview rejects missing producer-to-recipient evidence. See
[the standing bundle](../server/Bundles/stage-test-design.md) and its
[review audit](../server/Bundles/stage-review.md). Text checks protect this rule;
they do not prove transport delivery.

## Verification restoration contract (CARD-0478)

The SourceLanding brief names O/L, task and creation IDs and the external root
`<canonical-common-git-dir>/antiphon/verification/<O:N>/<task:N>/`. Keep full reports, PC matrices,
logs and restoration evidence there; do not use symlinks/junctions or paths inside the snapshot.
The authoritative full report is the stored task Result (including any reporting block/token
that settlement retained), independent of distilled completion text. Await all owned commands
and restore exact tracked/index bytes before writing restoration.json. Never forge a runtime
receipt: the worker record only describes files, outputs and test disposition.

The UTF-8 JSON object uses camelCase:

```json
{
  "schemaVersion": 1,
  "source": { "taskId": "<task-guid>", "sourceOperationId": "<O-guid>", "landedSha": "<L>" },
  "creationId": "<creation-guid>",
  "restored": true,
  "disposition": "<complete clean, finding or failed battery; evidence references>",
  "reportSha256": "<uppercase SHA256 of exact UTF-8 stored task Result>",
  "outputs": [{ "relativePath": "<exact task-owned output file>", "sha256": "<uppercase file SHA256>" }]
}
```

After settlement, the caller verifies the full Result and its digest against the external report
before completing/reconciling this record; do not hash a distilled notification or reworded report.
Outputs are exact files, never directory globs. Use `[]` when none remain. Tracked paths, escapes,
reparse points, unlisted ignored/untracked files and unknown empty directories refuse removal.
Already-removed exact outputs allow idempotent retry. Evidence remains outside cleanup.

Cleanup seals the durable task attempt set under the reservation lock, then separately obtains
native sealed receipts for every accepted generation. Receipt import checks binding, source,
creation, expected runner store, original host/container, successful zero accounting and drained
I/O; status/PIDs/kill acknowledgements cannot substitute. NeverReserved is a separate proven
empty history. The fresh DB evidence reader and Git/filesystem checks run under the genuine
repository lease before output/ref/tree deletion; unknown custody remains residue, never a kill.
Publication, report disposition and cleanup are independent verdicts. Deploy all source/custody/
cleanup/contracts together and directly inspect loaded selector, bundle hashes and runner/host
tracking before commissioning the feature's own PCs; caller-owned rollout cannot be fixture proof.

## Checkpoint temp custody and usage (CARD-0804/0805)

Checkpoint tests allocate `c723-<guid>` roots through `CheckpointTestBase.TempDir()`.
The per-test TUnit hooks register a marker and root index before returning the path,
join registered work and exact child processes, and remove owned roots after each test.
A failed teardown is a test failure. A linked tree, live or uncertain nested executor,
or failed delete leaves the marked root visible for a later guarded retry. Do not use
`CheckpointFixtures.TempDir()` or an unmarked test root.

The assembly startup hook admits a bounded sweep every five minutes. Registration
uses one independent index file per marked root under `.checkpoint-temp-roots/`, so
allocations do not wait for the shared sweep lock. Disposal removes its index file;
stale entries for absent roots are removed when visited. The sweep checks the
persisted interval before trying the lock. An older shared JSONL index is migrated
under that lock, with disposed entries discarded. It considers only exact indexed direct
children of the OS temp directory, waits ten minutes from
marker creation, and requires confirmed owner and nested executor death. One pass
is bounded by 512 index entries, 10000 descendants per root, 16 completed roots,
256 MiB of payload and two seconds. A partially deleted root keeps its marker and
is rechecked on resumption. Unknown identities, linked paths, malformed/unmarked
roots and launch attempts without a proven executor outcome remain visible.

`pwsh -NoProfile -File scripts/inspect-checkpoint-temp.ps1` inventories candidates
without deleting them. `-TempRoot` scopes inspection to a test-owned sandbox;
`-OutputFile` writes the JSON inventory outside the candidates. Never infer a
cleanup verdict from name or age alone.

The CARD-0804/0805 plan's CP-9..CP-13 commands call
`scripts/verify-checkpoint-temp-usage.ps1` from inside the checkpoint row's build
slot. Namespace and Full passes use a shared sandbox per pair and a pinned DLL
digest; their reports and event logs are under `.antiphon/c804-usage/<sha>/`.
The observer samples allocated bytes at one-second intervals (Linux `du` blocks;
Windows cluster-rounded `GetCompressedFileSizeW` that never follows a reparse
point, the sampled root included, and fails on any error but a vanished path) and
records created/deleted root IDs, copied logical bytes, TRX/roster counts, peaks
and final residuals. A missing TRX, event tail, owner exit or byte sample is red
evidence. The sampler and the roster/TRX/event gates live in
`scripts/lib/checkpoint-usage.ps1`, which `CheckpointTempUsageTests` drives with
supplied inputs. CP-13 uses a separate owner-death sandbox and preserves protected
live, uncertain and unmarked fixtures until its bounded sweep checks finish. These
reports do not replace the method-scoped SourceLanding Mutation PCs.

`Get-NamespaceCensus` deliberately keeps an independent literal count of compiled,
non-explicit `Antiphon.Tests.Checkpoints` cases with argument rows expanded. Both
Namespace admission and the Full checkpoint execution floor use it; do not derive
it from the selected roster being checked. When adding or removing a checkpoint
case, update this count and run
`CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`.
That separate class carries `Category=Unit`, so the ordinary Unit lane catches
census drift while `CheckpointTempUsageTests` remains in Integration.
