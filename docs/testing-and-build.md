# Testing and build operations

CARD-1210: `ShellFixtureLineEndingContractTests.Shell_scripts_are_forced_lf_by_gitattributes`
checks the active repository-wide `*.sh text eol=lf` rule and Git's effective attributes for
every tracked shell script; `Shell_scripts_have_no_carriage_returns` checks raw working bytes
and a scratch `core.autocrlf=true` checkout of the three previously affected fixtures.
For an existing clean Windows checkout, run
`git restore --source=HEAD --worktree -- scripts/fixtures/*.sh`, and if clean tracked working
files still contain CRLF, remove only those working files and restore them from HEAD again
(leave index entries intact), then rerun the byte test, which also exercises this restore path.

CARD-1022 release A: the five PTY assembly guards clear inherited `ANTIPHON_PTY_BACKEND`;
the isolated default is ModernConPty on Windows (both staged files required) and UnixPty
elsewhere. A native Windows qualification with missing prerequisites fails. Explicit legacy
fixtures are retained for release B and excluded from A's nine-row checkpoint manifest.
Typed/paste parity peers explicitly use modern and choose unwrapped/wrapped encoding through
the shared helper; changing the backend alone does not preserve a typed-input experiment.

C1022 backend-launch teardown retains native handles for the test-owned host, child and
OpenConsole before requesting stop. It joins those handles even after an assertion or stop
failure; deletion requires confirmed exit and successful ownership cleanup. Unresolved cleanup
retains the directory and reports PID, process name and elapsed wait, preserving the original
failure. `Cleanup_joins_owned_child_even_when_body_or_stop_fails` provides portable held-child
witnesses for all three launch cases. These witnesses do not replace the Windows ConPTY run.

CARD-1049: `AmServiceDeployReadinessTests` uses native loopback and real curl on
Windows. The held, non-listening fixture admits raw curl 7/28 on Windows (7 on
Linux); accepted stalls remain exact 28 and HTTP 503 remains exact 22. Its
timeout-then-ready case distinguishes accepted requests from delayed refusal.
Git sh/sleep/cygpath are discovered in one installation's `usr/bin` from supplied
PATH/Git locations, and only child PATH includes them. Curl is resolved first on
the original PATH. Missing Git tools fail; do not edit machine PATH or use WSL.
Every git.exe/sh.exe on the supplied PATH is a discovery root (CARD-1060).
An incomplete earlier installation, such as Grok's bundled Git missing sleep.exe/cygpath.exe, is skipped and named with its missing files in the refusal message.
Run the [plan's exact native lane rows](superpowers/plans/2026-10-04-card-1049-windows-readiness-retry-plan.md#checkpoints)
through the checkpoint tool; the production readiness deadlines/retries are unchanged.


CARD-1613: the rc Linux-only Windows exclusions are pinned `[NativeLinuxOnly]` markers
witnessed by reflection in each of the four suites (`NativeLinuxPolicyWitnessTests`).
`scripts/lib/linux-only-gate-witness.cs` is retired. `test-rc-platform.ps1 -Case
LinuxOnlyWindowsExclusionPolicy` checks the shipped rows and census gaps; the old
`-Case LinuxOnlyWindowsExclusions` refuses. The native child is
`tests/Antiphon.NativeLinuxPolicy.Probe`, staged for `NativeLinuxPolicyExecutionTests`.

CARD-0849: the C1192 driver's deadline/go witness projects generated `/fixture` paths
into its private filesystem model and fakes only the `dotnet` restore boundary. It
requires no user/mount namespace, bind mounts or `pivot_root` under WSL. The
`C849_Driver_deadline_witness_does_not_require_mount_namespaces` regression refuses
those tools and checks both the intact deadline and the existing m7 control.

## Performance standards

[Test performance standards](test-performance-standards.md) owns the standing engineering rules.
Any selection, row, batch or run expected or observed over 30 minutes triggers a Debug task with
its timing evidence; retrying alone is insufficient. Per-test/per-row timeouts are about 2x
measured p95 (40 seconds gets at most approximately two minutes). Longer allowances need a
recorded measurement, justification and card. Preserve historical costs and failures.

### Plan format: Performance budget

Every Plan includes `## Performance budget`, even when TestDesign follows separately. State:

- Exact test selection and app/category batches, project graph/downstream coverage, expected
  per-test/row/batch/run duration and measurement source (SHA, host/platform, load, sample and p95).
- Phase budgets for container start/readiness, data preparation, host startup, migrations,
  snapshot/restore/clone, test body and teardown; build and admission/slot wait reported separately.
- Once-per-run/assembly/session setup ownership, per-test isolation and any fresh-start witness.
- Recording artifact location, per-test/group membership and external-call spans/bounds.
- Every timeout set, its approximately 2x measured-p95 derivation, and card/justification for an
  exception or provisional sample. Reference the Debug task for any duration over 30 minutes.

For no-test plans state that fact and why. TestDesign refines the budget and checkpoint costs
from measurements; this section does not add checkpoint columns or alter receipt schemas.

### Review performance checklist

For each new or changed test/fixture and its Plan budget, Review checks:

- Expensive AppHost/web-host/container/migration/seed setup is shared once per run/assembly/session;
  per-test isolation uses snapshots/templates/clones/transactions, with explicit startup-test exceptions.
- Test and row timeouts track about 2x measured p95; longer limits and provisional samples carry
  evidence, justification and a card. Cost rows use measurements rather than guesses.
- Per-test duration, exact group/category membership, phase breakdown and individually bounded
  external-call timing are recorded; unknown/remainder and slot wait stay explicit.
- App categories and downstream dependency selection preserve coverage; any over-30-minute
  selection/batch/run has a Debug task and timing evidence. Pending instrumentation stays pending.

Current checkpoint importer defaults described below (including the 15-minute floor and 3x
estimate multiplier) are not the R5 policy: explicit measured budgets or documented exceptions
are required until tooling is updated. This documentation does not change those defaults, live
qualification, release gate credit or production timeouts.

<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #18

- **TUnit tests**: Use `dotnet run --project tests/<ProjectName>`, not `dotnet test`. Filter by `--treenode-filter`. **A plan's verify step names the test class(es) the change touched** — `--treenode-filter "/*/*/AttentionServiceTests/*"` — not a namespace. The namespace-wide form (`/*/Antiphon.Tests.Application/*/*`) is for a genuinely cross-cutting change, or for chunking a full `Antiphon.Tests` run into foreground-sized windows (Gotcha #74 has the full-run timing); it is not the default for one service plus its own test file. CARD-0239 is the evidence: a one-service AttentionService change was verified with the 2349-test Application filter (~26 minutes), which surfaced 66 pre-existing CARD-0297 failures that then had to be triaged for nothing — the class filter would have been seconds (CARD-0307). Gotcha #74's full-suite quiet phase is a fact about the full run; it does not license namespace-wide verify on a narrow card. Headed tests need `ANTIPHON_HEADED_TESTS=1` and must be in `[NotInParallel("Headed")]` group. `ClaudeTrustPromptCanaryTests` is the canary that must be re-run whenever Claude's trust dialog changes: `$env:ANTIPHON_HEADED_TESTS='1'; dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-trust/ -- --treenode-filter "/*/*/ClaudeTrustPromptCanaryTests/*"` (CARD-0390 §4.4: `j` then Enter on modern ConPTY; inbox conhost does not deliver the Select binding). Process-spawning tests run on one of two lanes. Classes whose only children are `git`, run in repositories the fixture owns, carry `[ParallelLimiter<GitProcessSpawnLimit>]`. That lane is `Clamp(ProcessorCount / 2, 2, 4)` wide, and `ANTIPHON_TEST_GIT_SPAWN_LIMIT` (range 1-8) overrides it. `1` serialises the git lane for bisecting a suspected flake, but the two lanes still run beside each other, so it does not restore the pre-CARD-1421 order (CARD-1421). Every other spawner (pty, pwsh probe, cmd, dotnet worker, fakeclaude/fakegrok, web host, session runtime) carries `[ParallelLimiter<ProcessSpawnLimit>]`, so at most one runs at a time per assembly (CARD-0050 S5). A class that mixes git with any other child, including one reached through a partial-class sibling file or a helper, takes the strict limiter. So does a class that runs git in the real checkout, or one whose assertions read the host-wide git process census (`AgentTaskLandIndexLockTests`). Classify by what is spawned, never by name. `DirectoryLink.TryCreate` is one such helper: it runs `Scripts/Fixtures/directory-link.ps1` through `ScriptHarnessProcess` on Windows, and `ProcessSpawnLimitTests.Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers` fails any git-lane class whose compiled type reaches `DirectoryLink`, `DirectoryLinkCommand` or `ScriptHarnessProcess` (CARD-1421 repair). Both limiters are assembly-local and are not shared across test projects. A new class that starts a child must take one of them. The `ProcessSpawnLimitTests` roster tests (strict and git) are floors, not censuses: a new limiter class need not be listed, a known spawner must stay listed, and no class may carry both limiters. Do not co-schedule `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests`: the in-process lane cannot cap the other exe, and even `--maximum-parallel-tests 2` on `Antiphon.Tests` still left FakeClaude rotating under a concurrent pair. Run the two projects one after the other.

### Preserved Gotcha #19

- **Client vitest suite: run it via `pwsh -File scripts/test-client.ps1`, and NEVER read a Bash pipeline's exit code as the verdict** (CARD-0069). In Bash, `npm test 2>&1 | tail -50` reports **tail's** exit code — always 0 — which is exactly how a merge reported "Exit code 0" over six failing tests; vitest itself propagates exit 1 correctly in every direct invocation (measured 2026-08-19). The wrapper tees full output to `logs/client-tests.log` and prints an unmissable `CLIENT TESTS EXIT CODE: n` line that survives any output capping. The suite has ONE test budget — `testTimeout: 20_000` in `client/vite.config.ts` — replacing 14 per-file `vi.setConfig` overrides; do not add per-file timeout overrides (that is how the .NET flake cast became furniture), and a test that needs more than the global budget is a test to make cheaper, not a deadline to widen. A failing client test is only "flaky" after an isolation re-run (`pwsh -File scripts/test-client.ps1 <File>`) actually passes — say which re-run you did when reporting it. See `docs/superpowers/plans/2026-08-19-card-0069-client-flake-cast-plan.md` for the measured evidence. **The wrapper's filter really does pass through** (`pwsh -File scripts/test-client.ps1 attentionVisuals.test` runs one file; a warm run measured 16.8 s on 2026-10-21) — since CARD-0307 it runs `node client/node_modules/vitest/vitest.mjs run @args`, never `npx vitest`. On Windows `npx` is the installer's `npx.ps1`, which rebuilds argv from the SOURCE TEXT of the calling line, so the literal `@args` reached Node and every scoped run silently became the whole 5–7 minute suite (that is most of why CARD-0239's execute ran 61 minutes against a 25-minute estimate). Do not write `npx vitest` in that script; `TestClientFilterTests` pins it, and a scoped run that takes minutes is the bug back. `scripts/test-client.ps1` runs `scripts/typecheck-client.ps1` before Vitest: `tsc -p` on each client project with `--noEmit --incremental` and a `tsBuildInfoFile` under `client/node_modules/.tmp`. The up-to-date decision is that file's content hash, not the input mtime (measured 9.2 s warm on 2026-10-21). A source diagnostic is exit 2. A nonzero typecheck skips Vitest. A direct typecheck outside the checkpoint tool goes through `scripts/build-slot.ps1`. `-TypecheckProject <tsconfig>` and `-LintPath <file>` forward to those gates and are not vitest filters. It then runs `scripts/lint-client.ps1` (`eslint . --max-warnings 0 --cache --cache-strategy content`, cache file `client/node_modules/.tmp/eslintcache`) after that typecheck and before Vitest (measured 65.0 s cold, ~2.5 s warm). The cache key is file content, so a same-size edit with a restored mtime still fails the gate. A nonzero lint skips Vitest. A direct lint outside the checkpoint tool goes through `scripts/build-slot.ps1`. When `vitest`, `tsc`, or `eslint` is missing, `test-client.ps1` runs one `npm ci` for all three, holding `logs/client-npm-ci.lock` so a second invocation waits instead of installing beside it.

### Preserved Gotcha #20

- **E2E browser tests serve `client/dist` — rebuild it or they mean nothing.** `UsePrebuiltFrontend = true` serves the last `npm run build` output, which nothing rebuilds automatically. A stale bundle made new UI tests fail for no visible reason (dist was a month old, 2026-08-08) and would let a test of *removed* UI keep passing. `AntiphonAppFixture.EnsureClientBundleIsCurrent` now hard-fails when any `client/src` file is newer than `client/dist/index.html`, naming the file — run `npm run build` in `client/`.
- **Native Land delivery protocol waits use progress evidence** (CARD-0550). A protocol-bound wait has a 60-second quiet deadline that renews while `protocol-git-*.json` records grow, with a 600-second absolute cap. Its `wait-*.json` record names the evidence, elapsed time, before/after git counts, and whether it renewed. A timeout says `quiet 60s after <elapsed>s; progress <count>`; a stalled protocol still fails after 60 quiet seconds. Scan-count and clock waits keep their plain deadlines.
  Native receipt timeouts also name the awaited kind, task/caller/request/notification identities,
  the last completed Git record, and bounded request/operation/notification/queue/transcript metadata.
  `receipt-timeout-<identity>.txt` under the fixture evidence root retains the diagnostic before teardown.
  Server, child and isolated-runner log paths include timestamp/level tails with fixed categories;
  payloads, Git arguments, raw errors and credentials are omitted. Producer-state collection has a
  separate five-second diagnostic bound. A Git count is completed commands, not a delivery verdict:
  verification, build-slot admission and native delivery can be silent between Git records.

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

- **Building while daemons run**: the always-on session-runner (and dev server) lock their `bin/` outputs. To build/test without restarting them, use an alternate output path: `dotnet run --project tests/<X> --property:OutputPath=bin-ptyhost/` (gitignored by `bin-*/`). **End it with a forward slash, never a backslash.** `'--property:OutputPath=bin-x\'` loses its trailing backslash to Windows argv quoting, and the mangled value creates junk directories — `bin-x --treenode-filter`, `bin-check --nologo`, and worst of all `bin-profile ` *with a trailing space* (see the next bullet — that one breaks the whole repo's build). `OutputPath` applies to every project in the graph, so one run drops a `bin-<name>/` in ~12 directories. Keep an exact producer-owned output inventory; a directory name or age alone does not authorize deletion. `cleanup-build-junk.ps1` requires producer creation records (see below). Non-git process-spawning tests share a 1-wide `ProcessSpawnLimit` lane (CARD-0050 S5); git-only tests share a 2-4-wide `GitProcessSpawnLimit` lane (CARD-1421). A failure on either lane is a real defect unless it also fails at the base commit (stash and re-run).

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
| Outer deployment host | The [CARD-1025 helper and receipt contract](superpowers/plans/2026-10-04-card-1025-host-jq-prerequisite-plan.md), hardened by [CARD-1058](superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md), requires a single-link canonical jq and hashes/executes it through one retained descriptor. Installed jq must match the pin before execution; functional existing jq records its actual version/digest. Final identity, link and access checks precede the point-in-time receipt. Directory/ancestor integrity, inode contents and toolchain remain trusted; in-place writes, ABA and later pathname use are outside the proof. The unchanged image probe still trusts directory integrity. A runner-image binary does not qualify the host; coordinate this separate prerequisite before recycling. |

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

`scripts/cleanup-build-junk.ps1` removes old `bin-*` outputs only when its caller supplies
the producing process's `CheckpointCleanupContext` through `-CleanupContext`. The shared
`CheckpointOutputLedger.Remove` requires that context's creation records and sealed content,
checks tracked/link/changed entries, and removes individual recorded entries. Directory names,
age and disk owner markers alone grant no authority. Without the context, including the weekly
Windmill invocation, it logs a skip and preserves legacy outputs for explicit inspection.
`bin/`, `obj/` and `workspace/` remain outside its candidate pattern. The earlier CARD-0448
inventory-only rewrite was reverted as out of scope; the confirmed opaque-file deletion and
its separately authorized repair are recorded in
[the scripts-lane repair report](investigations/2026-10-27-scripts-lane-product-repairs.md).
The landing verifier writes to a unique external artifacts directory and retains it.
Worktree removal's ignored-content policy remains separate (follow-up CARD-0452).

A `[Category=X]` predicate works in `--treenode-filter` (measured; a single category is not an OR). CI / nightly keep the full run. After a TRX (`--report-trx --report-trx-filename run.trx`), check for new ≥5 s tests:

```
pwsh -File scripts/test-duration-tripwire.ps1 -Trx path\to\run.trx
```

The allowlist is `tests/Antiphon.Tests/slow-tests-allowlist.txt` (exact simple or fully-qualified class names, case-insensitive). A class marked `Slow` must be registered there by fully-qualified name when it is added: `TestClassificationGuardTests.Registry_matches_compiled_metadata` fails with a first line `missing classes:` that names every Slow class absent from that file. A row for a class that is not Slow is `unmarked-registered` and fails the same guard. Every test class is tagged `Unit` xor `Integration` (`TestLaneCategoryGuardTests`).

For this Antiphon.Tests registry, `## ` header comments belong only at the top.
Each fully-qualified class row must immediately follow its own single-`#`,
nonempty, qualifying reason comment. Blank lines may separate complete pairs;
they cannot separate a reason from its class. The existing metadata guard rejects
detached reasons, late headers, missing adjacent reasons and duplicate classes,
including case variants. It reads layout and entries from one file snapshot.
The public registry reader and other assemblies retain their existing format.

Only this exact registry path uses Git's built-in `merge=union` attribute
(CARD-1383). Independent appends can merge without a configured driver, but
coalesced reason or class lines can produce invalid pairs: run the metadata guard
on the result. Do not sort or regenerate the registry as a merge step. The rule
must already be on the landing target; a source commit introducing it cannot be
assumed to resolve its own first landing conflict. Other registries and Markdown
files do not use this union rule.

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
and cleanup remain separate: the registered `FileSystemEvidenceRetention` (CARD-1446)
copies evidence-class ignored files, byte-verified, to `WorktreeCleanup:RetainedEvidenceRoot`
before removal; a protected name still refuses and preserves residue after publication. Completed-card whole-worktree removal is
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

CARD-0931: plan import and manifest validation refuse a class/method OR operand
without its own trailing `*` as `checkpoint-filter-or-wildcard`; full-path OR,
unparenthesized alternatives and unsupported nested path expressions receive
`checkpoint-filter-or-shape`. This happens before checkpoint builds or slots;
the tool itself may need a separately accounted bootstrap build. Accepted filters
are preserved, including property-expression OR. If a valid TRX executes zero
tests despite a positive Min or missing nonblank roster token, the row retains
exit 3 and reports `zero-tests-executed` in its state and CHECKPOINT reason.
There is no automatic retry or rebuild; independent rows can reuse the build.
Explicit skip-only rows with Min=0 and all named skips retain their result.

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

The script builds unless `-NoBuild`, runs the one filter into a fresh results directory, parses the TRX counters and the executed `Class.Method` roster (from `TestDefinitions/UnitTest/TestMethod@className`, never the display name), then prints the `CHECKPOINT` line, one `FAILED <Class.Method>` line per failure, up to 300 `EXECUTED <Class.Method>` lines and a `CHECKPOINT <Name> EXIT CODE: <n>` trailer for transcript inspection; receipt qualification still inspects fresh TRX identities and counts. Exit codes: **0** green, **1** one or more failed tests, **2** invalid input (a non-`bin-x/` `OutputPath`, a results directory that already exists), a dirty tracked tree, a detected tracked change, a failed build, or no TRX written, **3** fewer than `-MinExecuted` executed tests or an `-Expect` token that matches no executed name, **8** reserved legacy code (current detection never attempts recovery). The script retains build outputs and evidence, uses the supplied filter, and does not call `--list-tests`. Give several `-Expect` tokens as one comma-separated value (`-Expect A,B`): `pwsh -File` binds every argument as a string, so a repeated `-Expect` fails to bind there. Quote characters around the tokens are tolerated (CARD-0615) — `-Expect "'A','B'"` and `-Expect '"A","B"'` match exactly as `-Expect A,B` does, because each split token has its leading and trailing quotes stripped. Only the edges: a quote inside a token stays part of the name and will miss.

**Source qualification (CARD-0835).** Ordinary Code/Review passes the exact committed SHA with `-ExpectedSourceSha`; the checkpoint tool's `run`, `start`, and `row` use `--expected-source-sha`. The driver captures a version-1 source snapshot before work and at execution boundaries. A clean certificate has `dirty=0`, a plain full SHA in `source=`, `sourceState=clean`, `buildSource=verified`, zero failed/skipped tests and a matching structured `source.json` (script) or schema-2 `report.json` (tool). `scripts/validate-checkpoint-receipt.ps1 -Evidence <source.json|report.json> -ExpectedSourceSha <sha> [-Rows CP-1,CP-2]` checks that evidence and exits 2 for incomplete, dirty, changed, unknown or conflicting facts. The tool also offers `validate --evidence <report.json> --expected-source-sha <sha> [--rows CP-1,CP-2]` without launching a build. A stable dirty run without strict mode remains diagnostic; a missing or mismatched `checkpoint-build-source.json` makes strict `-NoBuild` refuse before tests. Commit source before the first row and leave it unchanged through the last; a brief edit restored between observations is outside this boundary-based check.

Activation note: land pending cards that rely on existing clean Review evidence before restarting AppHost onto this change. After activation, every older Review without `reviewedSourceClean: true` needs a fresh Review before evidence-backed land, including owner recovery. Index paths marked assume-unchanged or skip-worktree make source capture unknown until those flags are cleared; task worktrees here are full checkouts, and sparse-checkout content is not yet independently verified against HEAD.

CARD-1035: `run-checkpoint.ps1` starts both dotnet children in the certified Git root,
including when the caller used `Push-Location` and its native process directory differs.
Relative `-Project` paths resolve from that root, matching the build-source stamp;
the same child-directory binding applies to `-NoBuild` reuse and the dotnet shim.

**MSBuild properties (CARD-0671).** `-MsBuildProperty Name=Value` is forwarded as `--property:Name=Value` to **both** the build and the `dotnet run`, so a `-NoBuild` row evaluates the same output its build produced. Give several as one comma-separated value (`-MsBuildProperty UseAppHost=false,Foo=bar`), for the same `pwsh -File` binding reason as `-Expect`; a value that itself needs a comma uses MSBuild's `%2C` escape. A token that is not `Name=Value`, or that sets `OutputPath`, `OutDir` or `BaseOutputPath` (the `-OutputPath` guard owns those), exits 2 before any `dotnet` call. Every forwarded property is echoed as a `MSBUILD PROPERTY <Name=Value>` line after the `CHECKPOINT` line. **Off Windows the script adds `UseAppHost=false` by default** unless a supplied property names `UseAppHost` (`-MsBuildProperty UseAppHost=true` opts out): in a shared-output Linux build the referenced FakeClaude project's extensionless `fakeclaude` apphost lands on the same path as the `fakeclaude/` directory `Antiphon.Tests`' `CopyStagedHelpers` target stages, which breaks the build, and without an apphost `dotnet run --no-build` runs the test assembly through `dotnet exec <dll>`. A Linux row therefore needs no `-DotnetShim` wrapper or hand-run `dotnet` commands for this (the workaround earlier server2 plans describe). CARD-1367: relative isolated builds keep both fake providers out of the consumer root with `Private="false"` project-reference metadata; `CopyStagedHelpers` owns their nested outputs. The checkpoint tool uses a shared absolute output, where a producer can still write an apphost directly into the consumer root, so its `UseAppHost=false` workaround remains. `CopyOwnedHerdrFakeAppHosts` separately stages native `fakeclaude/fakeclaude` and `fakegrok/fakegrok` apphosts on Linux. On Windows with no `-MsBuildProperty` the `dotnet` arguments are exactly the pre-CARD-0671 ones. The offline harness forces either branch with `C671_PLATFORM=windows|linux`.

CARD-0847: `Antiphon.Tests` also stages a real `Antiphon.PtyHost` apphost on Linux. Its `CopyOwnedPtyHostAppHost` target restores `Antiphon.PtyHost` with `UseAppHost=true` before the inner build, because an outer restore with `UseAppHost=false` does not download the net9 apphost pack. An empty `NUGET_PACKAGES` cache must still build the test project with the checkpoint default.

The `### Cost` block's ordinary Code floor is the sum of the table's `EstimatedMinutes` column — a time, never the `Min` counts — and `-ExpectAbout` for a Code dispatch is that sum plus authoring time. The Code brief points at the table with one line, `checkpoints: <plan artifact path>@<full plan commit sha> section "### Checkpoints"` — the shape the server already renders for an Interim `selection:`.

**Repeat proof (CARD-0885).** `-Repeat N` on `run-checkpoint.ps1`, and `--repeat N` on checkpoint-tool `run`, `start`, and `row`, request N total executions of each selected TUnit case in one test host; omitted means one. TUnit generates separate native results and runs ordinary hooks. A different N needs its own isolated build output and matching build-source stamp. Qualify a repeated receipt with `-ExpectedRepeat N` or `--expected-repeat N`; the script writes source envelope v2 and the tool writes report schema 3, with every ordinal's cases and phase timings. The TRX and receipt must agree; a later pass does not erase an earlier failure. One-host repeats do not prove behavior that needs a fresh process per trial.

`repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.` Count total executions per case across proof invocations. Changing output paths does not reset the budget. A required first V/R run or a genuine code-change/failure-driven rerun is reported separately. Loaded means recorded ambient host load. CARD-0885's named five-repetition acceptance benchmark is a one-time exception, not a reusable limit increase.

### Checkpoint runner tool (CARD-0723)

CARD-1531 separates admission from execution. `--total-timeout` is the charged
execution allowance (existing selected-estimate derivation when omitted);
`timeouts.totalMinutes` remains a legacy field unused by scheduled execution.
Only the union of intervals where every runnable activity waits for broker
admission and no driver/preflight/settlement work runs is excluded. A queued
sibling never extends a running driver's deadline. `build.slotWaitMinutes`
(default 45) bounds each complete acquisition, including pending HTTP calls.
`--hard-wall-timeout` overrides optional positive `timeouts.hardWallMinutes`;
when absent the independent wall cap is execution allowance plus one admission
allowance. Explicit invalid total/wall CLI values or wall YAML values refuse.
Row/build deadlines and source/output custody remain unchanged. Admission expiry
is exit 4, execution/wall/local deadline expiry is exit 5 (including build expiry),
owner cancellation is 7, and direct caller cancellation is 130, after joined cleanup.
State/report `budget` records wall, execution, admission-only union, limits and
terminal reason; nullable row/build `executedSeconds` and `wallSeconds` preserve
unknown timing in old receipts. `CHECKPOINT-TIMING` carries elapsed seconds;
the original `CHECKPOINT executed=` token remains a test count. Partial canceled
waits are retained. Optional baseline comparison shares the remaining budget;
expiry does not grant a fresh cleanup-command allowance. Final receipt and
bounded teardown work may finish after the cancellation deadline.

`tools/Antiphon.Checkpoints` (`dotnet run --project tools/Antiphon.Checkpoints -- <verb>`) reads the plan's `### Checkpoints` table and runs it. The command every delegate uses is `run --plan <plan.md>`: that is `start` plus `wait`. `start` validates the table, writes `.antiphon/checkpoints/<run-id>/`, shadow-copies the tool, and launches a detached executor (`setsid` on Linux; `CreateProcessW` with `DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP` and never `CREATE_BREAKAWAY_FROM_JOB` on Windows). A checkpoint row is not that launcher: on Windows the row starts with `Process.Start` and `ProcessStartInfo.ArgumentList`, then the driver assigns the process to a kill-on-close Job Object on a best-effort basis. A child spawned before that assignment, or by a package alias such as the MSIX `pwsh` stub, can escape the job; timeout and cancel still sweep the process list and the redirected pipes are abandoned after 10 seconds. Linux rows stay on the runtime process type. `wait` prints one `HEARTBEAT` line a minute and, when the run finishes, the report. `wait --max-wait 570s` returns exit 75 while the run is still going; call `wait` again. A foreground call capped near 600 seconds cannot hold a long run, so do not end the turn while `wait` reports running — `stop <run-id>` if you must abandon it.

One run per committed slice group (`--after S1-S3` or `--rows CP-1,CP-2`). Rows overlap (two at a time on Linux, one on Windows). Builds never overlap in one run. A build's deadline is the largest deadline of the rows that need it, at least 15 minutes and at least `timeouts.rowMinutes`; `--row-timeout` replaces it exactly as it replaces a row's deadline, and `report.json` records it per build as `deadlineMinutes` (CARD-1356; `RunSchedulerTests.build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it`). The scheduler holds one build lane and starts at most one `dotnet build` at a time, in the order rows first need the builds. Rows whose build is done may run beside the next build. `tests/Antiphon.Agents.Pty.Tests`, `tests/Antiphon.PtyHost.Tests`, and `serial: true` rows run alone, builds included; `--serial` makes every row exclusive, so the run does one activity at a time. `report.json` carries `maxConcurrentBuilds` beside `maxConcurrentRows`; `wait` prints a needed pending build as `<id> queued` until the lane is free (CARD-0810). The manifest and the arguments handed to the driver keep the relative `--property:OutputPath=bin-<name>/` token (forward slash, trailing `/`). A real `dotnet build` with an empty dotnet shim rewrites that token to one absolute global `OutputPath` under the entry project directory (a directory argument is that directory; a project file uses its parent) and adds `ArtifactsPath` plus `UseArtifactsOutput=true`, so referenced projects, including references that arrive through an imported `.props` or `Directory.Build.props`, write into that owned directory. A shared command-line `BaseIntermediateOutputPath` is not passed: a global property is not re-expanded, so `$(MSBuildProjectName)` would be one literal directory and restore then cycles. `-nodeReuse:false` stays on the build. `UseAppHost=false` is added off Windows unless the manifest names `UseAppHost`, and the real-dotnet rewrite also adds it when the build arguments omit it. The static project-XML preflight remains defence in depth and does not see imports or `Directory.Build.props`. If the imported graph cannot be evaluated the row refuses `graph-unevaluated` before the build. The guarantee is detection and reporting. Before each real build the tracked tree must be clean; HEAD, the index and tracked content hashes are captured together with ordinary and ignored pre-existing user paths. After success, failure, timeout or cancellation the tool compares the baseline. It never changes worktree files or index entries to recover source. A modified, deleted, type-changed or newly staged path makes the row refuse with exit 2 and `refused: tampered`; every changed path is listed with a manual command and invalid evidence. Deleted or changed pre-existing untracked files are also reported; user recovery requires their backup. The tool states that it made no worktree/index changes. Arbitrary MSBuild targets can still change source: this tool detects their effect and does not undo it. Build output directories must be created exclusively for this run, with no linked path components. A pre-existing stamp refuses `pre-existing-build-source`; any existing compiler-output directory refuses `pre-existing-output`, before the build. Stamps and receipts use SafeWrite inside owned output/evidence roots. Build-output deletion authority comes from append-only creation records in the current process, including file size and hash at creation, hashed through a streaming digest bounded at 1 GiB; an entry over that bound stays unrecorded (CARD-1368). The disk output ledger is diagnostic and cannot authorize deletion. CARD-1254 adds the separate, narrowly bound shadow-copy authority described below. Cleanup rechecks containment, links and content and deletes files individually and directories nonrecursively. Unknown or changed paths are left in place. Compiler outputs written by the build into the fresh root are adopted when the same executor seals that root after the green build, with size and hash taken at the seal (CARD-1242), but only under seal-time custody: the root was created owner-only (0700) by that process, the driver observed the build exit, and each entry is a regular single-link file or a directory owned by the tool's uid whose ctime lies between the root's creation and that exit and whose mtime is not after it. Entries outside that custody window are retained for manual removal. Entries the row's own test host and its children write under the sealed root are adopted at the driver-observed host exit under the same custody rule (CARD-1368); existing records are never replaced, so a modified build entry still refuses. A same-uid writer inside a custody window, or within one kernel timestamp tick after its observed exit, is indistinguishable from that window's process. Windows has no statx: after the observed build exit the seal adopts a regular single-link file or directory that is not a reparse; a second link or a reparse stays unrecorded, and other platforms adopt nothing. Windows change time is settable by the same account, the same-account trust boundary already stated for a writer inside the build window. Failed partial runs retain their evidence. Starting a run does not sweep older directories or overwrite a shared latest pointer; explicit run IDs name evidence. The same absolute OutputPath, ArtifactsPath, and UseArtifactsOutput values are used for the build, the `dotnet run --no-build`, and the build-source stamp. Global OutputPath, ArtifactsPath, and UseArtifactsOutput redirection, and the static project-XML preflight, stay as defence in depth only. Writes outside the repository, a row command string, and a `-DotnetShim` or `--dotnet` replacement are outside this guarantee. The slot client probes `ANTIPHON_BUILD_SLOTS_URL` (else `http://127.0.0.1:8080/build-slots` on Linux and `http://localhost:17204/build-slots` on Windows) once: 200 leases, 404 is `unavailable` with no per-driver grace, and a definitive broker refusal is immediate exit 2 (`slot=refused`, `slot-reason=<code>`), before any driver. Only transport failure after the bounded 60-second grace becomes `unleased` at `-maxcpucount:4`. Each running driver posts its own holder-process pid and captured UTC start time, so a broker that repeats one lease per pid cannot make two drivers share it, and a checkpoint line says `slot=granted` only for a lease that driver still holds. A slot wait that expires is exit 4. `executor.log` records bounded probe/acquire response details and renew/release failures; report JSON and checkpoint lines retain the stable slot reason. A reachable enabled Linux broker must show `slot=granted` for every selected build and row.

Confinement scope per scheduled build (`run`/`start`, CARD-1241). Write roots are the owned layout plus `bin-<id>/` and `obj/` beside each statically visible project. The `row` verb takes its stamps inside its slot. Test names below are `CheckpointConfinementLedgerTests` methods unless a class is given.

| Paths | Stamped | Checked | Named test |
|---|---|---|---|
| Tracked, untracked and ignored files outside the write roots (the ledger) | before the slot is taken: a stat stamp (Linux: type, device, inode, size, mtime, ctime; elsewhere type, size, mtime), then a content hash | once the slot is granted, before the build starts: an entry's hash is skipped only when its stat stamp has an inode and a ctime (the Linux stamp), is unchanged, and its mtime and ctime are more than 2 s before the capture; off Linux each entry is hashed; with `build.admissionHash: strict` each entry is hashed; a hashed difference is refused `tampered` before launch; again in full after the slot is released | `ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it`, `file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch`, `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch`, `rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch`, `stat_without_a_change_time_hashes_each_entry_at_admission`, `unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted` (Linux), `entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted`, `entry_with_a_change_time_inside_the_racy_window_is_hashed_at_admission`, `shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode`, `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch`, `admission_hash_other_than_stat_or_strict_is_rejected`, `non_output_file_changed_before_slot_release_is_refused`, `scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release` |
| Tracked, untracked and ignored files under the write roots, and paths absent from the ledger | inside the slot, before the build | inside the slot, after the build | `tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger`, `changed_file_under_an_output_root_is_still_refused_inside_the_slot` |
| HEAD, index entries, `git status`, new non-ignored untracked paths | inside the slot, before the build | inside the slot, after the build | `CheckpointTrackedBaselineTests`, `untracked_source_file_added_during_the_build_is_still_refused` |

Before the build the write-root preflight refuses `tracked` when a tracked path sits under a build output directory (`OutputCleanupTests.build_refuses_tracked_output_before_the_driver`, `OutputCleanupTests.referenced_project_tracked_output_is_refused_before_the_build`). Not covered: a change made between the admission check and the build's launch, or after a path's after-release stamp, when the final bytes equal the earlier ones; a new ignored file. A ledger entry the reader refuses (a hard link, an access refusal, a stalled read) stamps `unreadable|<reason>` and is reported `refused: unreadable` with an `UNREADABLE <path>: <reason>` line per entry and no restore command, since no content difference was observed; a content difference beside it is still `refused: tampered`, with the `UNREADABLE` lines listed before the restore line; a recorded `unreadable` stamp is reported at admission without a hash (CARD-1358; `ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered`, `tampered_entry_beside_an_unreadable_entry_is_still_refused_tampered`, `in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable`, `scheduler_reports_an_unreadable_ledger_entry_as_unreadable`). Both kinds exit 2; a build that also timed out reports the timeout, because only `tampered` and `restore-failed` outrank it.

Admission limit (Review 9f1c8a2e D1). The stat stamp is not a content version.

| Behaviour | Named test | What it asserts |
|---|---|---|
| Default admission (`build.admissionHash` absent or `stat`): a non-output file changed during the slot wait through a shared memory mapping, or by any writer that does not update the ctime, keeps its stamp and is not hashed | `shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode` | stat provider returns the captured stamp after A -> B: 0 hashes, no refusal; after A is written back `VerifyLedger` is clean |
| The same change when the build then writes the old bytes back: the build launches and the after-release hash sees no difference; a difference still present after release is reported (`non_output_file_changed_before_slot_release_is_refused`) | `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` (default run) | build launched, build state `ok`, no TAMPERED line, file holds A |
| `build.admissionHash: strict` hashes each ledger entry at admission whatever its stat, at the in-slot hash cost the stat rule avoids (206 s measured for one 32k-file `node_modules`) | `shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode`, `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` (strict run) | hash count equals the ledger size; `refused: tampered` with the exact path before launch; B kept on disk |
| A value other than `stat` or `strict` refuses the manifest | `admission_hash_other_than_stat_or_strict_is_rejected` | `ManifestValidationException` on field `admissionHash` |

Set it in a manifest's `build:` block. For a plan, write the manifest with `import --plan <plan> --out <file>`, add `admissionHash: strict` under `build:` and run that manifest.

For acquisition, the 60-second grace starts with the first transport failure or 5xx after the most recent busy answer. A busy 409 resets it; the overall busy wait still uses the configured slot wait limit. A 5xx in the current grace window ends in refusal if no later busy or grant answer clears it.

Exit codes: 0 green, 1 failed tests, 2 invalid manifest/build, missing/malformed required receipt or TRX, detected tampering, or an unprovable ledger entry (`refused: unreadable`, CARD-1358), 3 `minExecuted` or roster miss, 4 slot timeout, 5 row or total timeout, 6 a recorded executor-crash code or a dead executor whose refreshed state has no `phase=done`, 7 owning task ended or could not be verified, 8 reserved legacy code, 75 the wait limit elapsed before a dead observation. A bound owner read has a 12-second HTTP deadline and a three-minute uncertainty budget; each failure is recorded in `executor.log`. Existing work runs during uncertainty, and new launches wait for a live answer. When the budget expires the run is owner-unverified and new launches stay blocked, but the guard keeps reading the owner: a later definitive settlement cancels the running drivers and releases their slots. Live readers of executor.log, including wait, must open it with FileShare.ReadWrite | FileShare.Delete while the writer still holds the file. The run exit is the highest of 8, 2, 6, 4, 5, 1, 3, 0 for unbound runs; owner exit 7 takes precedence when bound ownership ends. Reruns happen only for names passed as `--known-flaky` (or `rerun.knownFlaky`), once, method-scoped, and the report prints `RERUN` lines with `reruns=1`. `--baseline <ref>` classifies each failure `INHERITED`, `INTRODUCED`, or `NEW` from a detached worktree of that commit. That worktree is `<temp>/antiphon-checkpoints-baseline/<run-id>/baseline-<suffix>`, `<suffix>` being the build id after `bin-` (CARD-1231): `TestLaneCategoryGuardTests` and other repository censuses skip a path with a `bin`, `obj` or `bin-*` segment, so a checkout named after the build id scanned nothing at the base and a failure red there was labelled `INTRODUCED` (`BaselineComparerTests.red_at_base_census_is_inherited_when_the_census_skips_build_output_folders`). Keep the checkout path free of such segments. The same guard also misread text fixtures as test classes; that is a separate fix (CARD-0507 S1, for the CARD-1155 fixture). The report is `report.md` and `report.json` under the run folder. A red run keeps `failures.md`, the TRX, console logs, `host.txt`, and `git.txt`. A green run can reclaim unchanged tool-created output entries in the same process. The executor that created a fresh build output root adopts what the build wrote into it when it seals the root after the green build, recording size and SHA-256 then (CARD-1242), only for entries inside the seal-time custody window above (or, for new host-written entries, the custody window ending at the driver-observed host exit, CARD-1368): on Linux an owner-only root, the tool uid, ctime inside the window and mtime not after the exit; on Windows a regular single-link file or directory that is not a reparse after the observed build exit. An entry outside that window, an already recorded entry changed after the seal, a link, a tracked path or a tree over 10,000 entries still refuses and is left for manual removal. The executor never deletes its own shadow copy. `wait` reads `state.json` and observes the executor. When that observation is dead and the phase just read is not `done`, including a missing, empty, malformed, or unreadable state file, `wait` reads `state.json` again. Nonregular state files and malformed row/build identities instead refuse immediately with a named exit 2 before cleanup. Finish can publish `phase=done` after the `done exit=` log line in the same window the process exits, so the first read can still show an earlier phase. When the refreshed phase is `done` and the observation is dead, `wait` returns the recorded exit code, including non-zero codes such as a stored 2 or 6, only after validating both report files, their digest, source and terminal exit binding. Missing or mismatching receipts return 2 with a named reason. CARD-1254 persists the starter's verified creation inventory in `tool-copy-authority.json` before launch intent, with its SHA-256 bound in `ownership.json`. A separate wait process can remove that exact run's unchanged `tool` child after proving executor death. It retains copies with missing, malformed, changed or foreign authority, changed/added entries, links, tracked paths or replaced directory identity. Legacy copies without durable authority still need current-process creation records or manual removal. When the refreshed phase is not `done` and the observation is dead, `wait` returns 6 and prints `executor died without phase=done`. Exit 6 has two origins: that fallback, where a dead executor's refreshed state has no `phase=done`, and a recorded exit code 6 on a `phase=done` state. An executor exception sets `phase=done` and exit code 6 and tries to write that state before returning 6; when the write is what `wait` reads, `wait` returns the recorded 6. When the observation is not dead, `wait` does not return the recorded code or 6; if the wait limit elapses it prints `STILL RUNNING` and returns 75, including when `phase=done` is already stored and the process is still alive, and when the observation is unknown or a reused pid. Tool cleanup for `<run>/tool/` runs on both dead returns; the receipt can report the directory removed, already absent, retained, or failed. `start` retains pre-existing run directories. `clean` prints the directory and requests manual removal; persisted state and generic output ledgers do not confer deletion authority; the run-bound shadow-copy receipt is the specific exception.

CARD-1254 shadow-copy safety boundary: only paths copied and recorded by the starter are sealed; no disk record is imported into SafeWrite's generic creation registry. The cleanup gate, executor-generation check, current-image veto and SafeDelete Git/link/containment checks remain in force. Inventory and bytes are checked before cleanup and again per entry; files are unlinked individually and directories removed nonrecursively. The receipt and ownership journal share the OS-account trust boundary: their hash detects changes to the receipt alone, not an actor able to forge both control records. Cooperating writers use the gate; there is no atomic filesystem transaction against a hostile same-account writer. Linux directories bind device/inode; the portable fallback uses creation time and requires native Windows qualification. A definite native launch refusal now records `not-started`; ambiguous launch or failed custody publication remains retained.

CARD-1307 caller cancellation returns **130**, with `canceled: caller-requested`.
Work verbs install a `Console.CancelKeyPress` handler for their invocation and
remove it on exit; repeated interrupts cancel the same token. Internal `hold`,
`smoke-write` and `smoke-detach` helpers keep their existing signal behavior.
Canceling `run` or `wait` stops that observer; the detached executor continues.
Keep the printed run ID for a later `wait --run <run>` or explicit `stop`.
Direct `row` cancellation reaches slot probing, acquisition and its driver.
A directly canceled executor joins scheduler/driver and lease cleanup before
attempting non-green terminal state/report publication with exit 130 and reason
`caller-requested`. Receipt write refusals remain diagnostic failures; an exit
code alone does not prove successful terminal persistence. Cooperative cancellation
may wait for bounded synchronous custody operations. Internal receipt deadlines,
row/total timeouts, owner settlement, crashes and max-wait retain their existing
codes unless the caller token itself is canceled. Exit 130 is an invocation
outcome and is not added to row-result precedence.

CARD-1307 sealed tool removal publishes `<run>/tool-removal-started.json` under the
cleanup lock after dead-owner and full authority admission, before any tool unlink.
It records the run, tool path and authority digest through ordinary safe-write
ownership. Failure to publish permits no tool deletion. The marker stays after
successful removal and records process interruption, not power-loss durability.
If removal fails or a later invocation finds that marker beside a remaining tool,
cleanup returns non-clean `Refused` with `tool-removal-incomplete`, paths and manual
inspection guidance. The tool may be partially removed; the marker can also precede
the first unlink. No retry imports or reseals the incomplete inventory, and marker
presence never grants custody. Invalid or linked markers remain refusals.

To recover, establish executor death and inspect containment, links and the remaining
files before manually removing that specific tool residue. Keep the run receipts,
ownership seal and removal marker. A later `wait --run <run>` can observe
`AlreadyAbsent`; `clean` still requires manual removal. Deleting the marker,
editing/resealing the inventory or importing it into creation records is not recovery.
Legacy unsealed copies retain their current-process ledger behavior.

`import --plan <plan.md>` writes the same manifest as YAML under `.antiphon/` when a row needs `knownFlaky`, `serial`, or a timeout pin. A legacy eight-column table (minutes stored under `Min`) is refused until the time column is renamed `EstimatedMinutes` (CARD-0617). `import` treats 45 minutes as an advisory threshold: imported row timeouts are `max(15, 3 * selected EstimatedMinutes)`, using the platform-selected estimate. Estimate 15 gives 45 minutes without a warning; estimate 16 gives 48 minutes with a warning suggesting a split. Row timeouts are not capped at 45 minutes; positive explicit timeouts are used unchanged, and without an estimate the manifest fallback is `max(15, manifestDefault)`. The independent total-run deadline still applies and can stop a row earlier. `import` also warns when a row names more than four land classes. It also warns, without changing the exit code or the manifest, when a row defines a second build equivalent to an earlier one: a different `bin-<x>/` id with the same project (separators, a leading `./`, a trailing `/` and a `<Dir>/<Dir>.csproj` file name normalised, case-insensitive), the same expanded `After` and the same `Repeat`; the warning names the Build cell to write: the earlier `CP-n` id, or the earlier `<project> -> bin-<x>/` cell when that row id is not `CP-<digits>` (CARD-1493). Builds in different `After` groups are never equivalent. A land row stays at about 120 `[Test]` methods. `row` runs one row with the same lines as `scripts/run-checkpoint.ps1`. The repo tool manifest does not list this package until a feed exists; prove the package with `dotnet pack` and `dotnet tool install --tool-path`.

## OS branch witnesses

For each changed OS-selected production behavior, Code and Review must identify
its real runtime object from the **production factory/property**, the selected
branch and the discriminating input. Inspect the test that drives that object
through production and asserts both setup and observable result. A seam or mock
may supplement this witness but cannot replace it. Audit changed helpers and
their OS-gated callers even when the OS check itself did not change. This is a
changed-behavior obligation, not a repository-wide coverage campaign.

For optional configuration, absent, empty and value are different states. Remove
the key and assert absence before the call; an empty assignment is not removal.
CARD-1525 corrected the CARD-1506 incident premise: the old script already used
real `ProcessStartInfo.EnvironmentVariables`, but every case assigned the opt-out
key. The missing absent-input witness was decisive. CARD-1525 owns the runtime
repair and its reviewed file/method/SHA; do not credit an investigation diagnostic
or bind a speculative method as passing evidence while that repair is pending.

Force an existing OS parameter while retaining the real type when possible.
Native-only APIs require a separately scoped native witness with its exact OS,
source, command and budget; do not add a production bypass for testing. A native
witness not executed stays pending, never a passed skip. Read runtime placement
from `GET /api/runner-defaults` and `GET /api/session-runners`; leave portable work
unpinned and use `-Platform Windows` only for the native Windows piece.

Code records the following declaration in ignored JSON, then runs:

```powershell
pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef <full-base-sha> -HeadRef <full-head-sha> -EvidencePath <ignored-json> -Plan <repo-relative-plan.md>
```

Optional `-Repository` selects the checkout. Both refs must be full commit SHAs.
The checker reads Git objects at those revisions, including the plan at head;
unstaged source cannot satisfy it. It prints both resolved SHAs and preserves
candidate diagnostics even when a reviewer dispositions them. Exit 0 means
complete syntax/inventory, 1 missing/contradictory obligations, 2 invalid or
unreadable input. It never executes commands from evidence, authenticates a
receipt or proves runtime identity/reachability. Review inspects the diff and
callers, the real test and fresh checkpoint receipts; this lint cannot discover
all indirect OS dependencies. Existing land admission and receipt validation are
unchanged. Missing required execution remains verification incomplete under
[D-2](orchestration-loop.md#d-2-advisory-policy-card-1281); standing rule 4 still
owns `found`. A declaration saying `real-type` alone proves no execution.

The pinned test file must contain the exact declared test method or named script
case, the production type and factory in code, and each optional-input
`setupAssertion` as code (including the absent-key assertion). The bounded
declaration matcher recognizes attributed C# test methods and PowerShell
functions / `It`, `Run-Case`, or `Test-Case` declarations. It strips `//`, `/* */`,
`#` and PowerShell block comments with a deterministic scanner. C# `/* */`
blocks do not nest; PowerShell `<# #>` blocks do. Unterminated blocks refuse
with exit 2 naming the comment kind, including after otherwise valid code.
Quoted assertion arguments are preserved in the comment-free view and masked
in the executable-token view; `/*` or `<#` inside a closed string is literal
text, never a comment opener. C# ordinary/character literals use backslash
escapes, verbatim literals use doubled quotes; PowerShell single quotes double
and double quotes use backtick escapes, selected by the test path extension.
Unterminated literals and unsupported ambiguous forms (C# raw/interpolated
strings, PowerShell here-strings or quoted subexpressions) refuse with exit 2
and a reason. This bounded scanner does not guess where such strings end.
Quoted examples cannot supply executable tokens. This is lexical consistency,
not dataflow proof that the asserted setup reaches production. Review still
checks removal or never-assigned absence before the call and the actual outcome.

Version 1 fields (names and enum values are case-sensitive by convention):

| Record | Required content |
|---|---|
| Root | `version: 1`, `baseSha`, `headSha`, arrays `inspectedPaths`, `witnesses`, object `restartImpact` |
| Each inspected path | `path`, `disposition` (`os-gated`, `portable`, `test-only`, `removed`), nonblank `reason`, arrays `helpers` and `candidates` |
| Each candidate | `revision` (`base`/`head`), one-based `line`, `disposition: witness` with `witnessId`, or `disposition: non-behavior` with a reason |
| Each witness | Unique `id`, `sourcePath`, literal `branchAnchor` present at head, `targetOS`, `witnessKind: real-type`, `productionType`, `factory`, `testPath`, exact `testCase` method/script case, `setup`, `outcome`, `executionMode` (`portable-forced`/`native`), `checkpointId`, `input`, `nativeObligation` |
| Optional configuration input | `kind: optional-config`, `key`, `cases` containing `absent`, `empty`, and `value` states (multiple distinct values allowed); each has `state`, boolean `keyPresent`, nonblank `setupAssertion`. Absent has false and no `value` property; empty has true and `value: ""`; value has true and a nonempty string |
| Other input | `kind: other`, `description`, `setupAssertion`; Review checks that an optional read was not misclassified |
| Native obligation | `status: pending` plus `reason`, or `status: recorded` plus `executionMode: native` and `receiptReference`; both require `targetOS`, exact head `sourceSha`, `command`. Portable-only witnesses may use `status: not-required` plus `reason` |
| Restart impact | Boolean `required`, exact startup-path census `paths`, array `additionalPaths` for reviewed startup dependencies, `reason`; if required, a Windows `nativeObligation` at head |

Supported target OS names are Windows, Linux, macOS, FreeBSD, Android, iOS,
Browser and Other. Source/test paths must be regular files in pinned Git trees.
Removed paths are checked against base; renames retain old and new identities.
Every changed non-document path must be inventoried; only `.md`, `.rst`, `.txt`
are automatically documentation. Unknown extensions are explicitly reported for
classification. `helpers` names additional inspected paths for indirect callers.
Duplicate identities, stale candidates, contradictory dispositions and unknown
checkpoint IDs fail. Candidates are scanned in complete changed files at both
revisions and in named helpers. An unchanged OS guard in a changed file therefore
still needs an explicit disposition, without demanding a new test for unchanged
behavior. Deleted guards use a reasoned non-behavior disposition.

The bounded lexical scan recognizes C# (`.cs`, `.csx`), PowerShell (`.ps1`, `.psm1`,
`.psd1`), shell (`.sh`, `.bash`, `.zsh`) and JS/TS (`.js`, `.jsx`, `.mjs`, `.cjs`,
`.ts`, `.tsx`). Spellings: `OperatingSystem.Is*`,
`RuntimeInformation.IsOSPlatform`, `Environment.OSVersion.Platform`, `$IsWindows`,
`$IsLinux`, `$IsMacOS`, `$env:OS`, OS preprocessor guards with WINDOWS/WIN32/_WIN32,
LINUX/__linux__, OSX/MACOS/__APPLE__, `uname`, `$OSTYPE`, `process.platform` and
`os.platform()`. Comments/strings can be candidates; aliases and other spellings
can be missed. Keep candidate diagnostics and record reasoned non-behavior
exemptions instead of hiding matches.

Check startup impact for either side of a rename/delete touching `dev-aspire.ps1`,
`scripts/restart-apphost.ps1`, `scripts/build-slot.ps1`, `scripts/lib/**`,
`scripts/apphost-common.ps1`, `Antiphon.AppHost/**` or a reviewed dependency.
Document/test-only changes can say not applicable with their actual census and
reason. Other matched paths require a native obligation; pending is valid lint
syntax, not permission for an rc pick. Follow the [native probe and restart
rehearsal](release-gates.md#startup-path-rc-picks) before publication/activation.

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

A packed PC label prefix is the known leading text of the message expression, up to its first colon. A string literal, a verbatim literal, or a raw literal contributes its value as complete text. An interpolated string contributes the text before its first hole and is incomplete at that hole. Parentheses contribute the inner expression. A conditional contributes each branch as a separate candidate, so an alternative whose prefix is valid binds that alternative's labels. Concatenation appends the next operand while the candidate is complete. An identifier, member access, invocation, cast, array or collection creation, object creation, element access, null-coalescing expression, switch expression, or other form contributes no text and ends the candidate. When every whitespace-separated token of that prefix matches the plan reader's hyphenated label shape `^[a-z][a-z0-9]*(?:-[a-z0-9]+)+$`, each token is bound. When the known text has no colon, or a prefix token does not match that shape, the candidate binds the labels the literal-segment reading binds, including a message such as `target-label before`.

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

Opt-in Linux Grok phone-home is `docker-compose.runner-grok.yml` plus `scripts/verify-phone-home-grok.ps1`. It never publishes a runner port and never targets production 17202–17205. `PhoneHome__ServerOrigin` is launch env only (`PHONE_HOME_SERVER_ORIGIN`); compose/Dockerfile do not hardcode it. V-7 allocates an isolated server/Postgres, writes `.antiphon/card0490-live.json` (example: `tests/fixtures/card0490-linux/card0490-live.example.json`), and tears the instance down. Local Docker injects `http://host.docker.internal:<isolated-port>`. `-Placement server2` injects `http://<desktop-tailscale-ipv4>:<isolated-port>` (this desktop is `100.79.51.37` / `desktop-ktlkpif`; `host.docker.internal` on server2 is server2 itself). The image installs Grok 1.0.41 by downloading `https://x.ai/cli/grok-1.0.41-linux-x86_64` and verifying SHA-256 `9ce03ed23e16ea01072b4496263d6213a27899e1e3e107f008d36edf82e70407` before install (CARD-0989; version pin from CARD-0575) and does not `COPY` credential files. Grok OAuth is a **copy** of primary `GROK_HOME` `auth.json` (optional `config.toml`, `version.json`) into a throwaway directory, bind-mounted **read-only** at `/state/grok` via `PHONE_HOME_GROK_HOME`. Session writes use `PHONE_HOME_GROK_SESSIONS`. Never live-mount the primary store. Not `XAI_API_KEY`. QEMU `assets.lock.json` pins are a native-lane obligation and do not block the isolated container.

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
and fetchable. The runner clones it under `/work/repos/<repository-name>` and
advertises `workspaceRepositoryV1`. Before making a secondary mirror, the default probe uses
`git push --dry-run` through the push URL; a receive-pack refusal returns
`phone_home_repository_push_unauthorized` with a safe category and remedy. CARD-0817 keeps the
exact Antiphon primary URL on its SSH deploy key; other admitted HTTPS repositories share
one token through the baked credential helper and read-only `RUNNER_GITHUB_TOKEN_DIR`
directory bind on both runners. The same helper can answer an admitted private fetch's
authentication challenge. Ordinary checkpoints use inert files and scratch repositories;
provisioning, rollout and live acceptance remain operator-owned (see
[agent-credentials.md](agent-credentials.md)).
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

**Cached test schema (CARD-1420).** The shared store no longer replays every EF migration in each test process. Bootstrap computes a key, the SHA-256 (32 hex characters) of a format constant, the bytes of the migrations assembly (`Antiphon.Server`), the image `postgres:16-alpine` and the EF Core Relational and Npgsql provider versions, and looks it up under `ANTIPHON_TEST_SCHEMA_CACHE`: unset uses `<temp>/antiphon-test-schema-cache` (host-wide for the user, shared by every worktree), `off` (any case) always migrates and never reads or writes, and any other value is the root directory (a relative path is made absolute). On a hit the snapshot (a plain `pg_dump --no-owner --no-privileges` of the migrated `antiphon_test`, schema and data, taken by the container's own `pg_dump`) is replayed with the container's `psql -v ON_ERROR_STOP=1 --single-transaction`, and the restored `__EFMigrationsHistory` must equal the assembly's migrations. On a miss the process migrates, captures and publishes `<key>/schema.sql` plus `manifest.json` through an atomic temp-directory rename; a concurrent loser keeps its own store. A failed restore drops and recreates `antiphon_test` and migrates; a failed capture is logged and ignored, so a cache defect costs time, never a red run. Only the process's own container and only `antiphon_test` are touched; no configuration connection string is read. Each bootstrap writes one stderr line, `C1420 schema-cache <hit|miss|off|fallback> key=<key> ms=<elapsed>`. After a publish the cache keeps the 8 most recently used entries, drops entries unused for 14 days and stale `*.tmp-*`/`*.bad-*` directories older than an hour; deleting the root is always a safe reset. The CARD-0476 probe children run with the cache `off`, so their `migrate == 1` contract still describes the migrate path. Migration correctness stays covered by every miss, `TestSchemaSnapshotTests` (restored parity against a fresh migration, with a dropped-table negative control) and the `*MigrationTests` classes, which migrate their own databases.

`RestartFixture.Run` caches only a successful AST preflight, keyed by resolved shell identity plus the hashed copied entry, helper, platform, wrapper, and validator identity. Every accepted `Run` still launches the real entry child. `Script` and `DecodeCapturedMilestones` stay uncached. Resolver identity walks PATH for the first readable regular file and launches that absolute path (skipping zero-byte/reparse WindowsApps aliases). Measured Code-stage Health+Compatibility+Safety selection: 94 passed in 8m 45s into `bin-c476/`. Paired before/after savings for the S3 procedure are left for Mutation/Review if needed; do not treat the historical 24–37 s bootstrap or 143 s Health total as recovered wall.

### dotnet-ef from the repo tool manifest (CARD-0677)

`dotnet-ef` is a repo-local tool pinned in `.config/dotnet-tools.json`, not a machine-global install. Before any `dotnet ef` command in a worktree (Windows or the server2 runner), run `dotnet tool restore` from the repo root, then `dotnet ef migrations add <Name> --project server`. The pin is an exact version whose major matches the server's `Microsoft.EntityFrameworkCore.Design` reference (both pinned `9.0.20`); bump the pin together with an EF Core major move. `DotnetToolManifestContractTests` guards the exact pin and the major match. The runner image needs no extra layer: restore writes to the runner user's `~/.nuget/packages` and reaches nuget.org the same way package restore does.

### NuGet version pins (CARD-1229)

Package versions in `.csproj`, `.props` and `.targets` files must not float (`9.*`, `9.0.*`, `*`). A floating reference takes the highest matching version on the feed, so a feed that publishes one package of a family before its siblings can break restore on a branch that changed no project file: on 2026-10-13 Microsoft.Extensions.Logging.Console floated to 9.0.21 before Microsoft.Extensions.Options.ConfigurationExtensions 9.0.21 was listed, the transitive floor resolved 10.0.0, and restore failed with NU1605. Apart from the two exceptions below, the repo's package references use plain versions such as `9.0.21` (Review d93e4ca3's audit of 2026-10-13; no test requires it). In NuGet syntax a plain version is a minimum, not an exact constraint; NuGet picks the lowest applicable version available, so it resolves to the named version whenever that version is on the feed (otherwise the next higher one, with NU1603).

What `PackageVersionPinContractTests` (`/*/*/PackageVersionPinContractTests/*`) enforces, and no more: it reads the raw XML of every file whose extension is exactly `.csproj`, `.props` or `.targets` under the repo root (skipping `bin`, `bin-*`, `obj`, `node_modules`, `.git`, `.antiphon` and `workspace` directories), and fails on any `PackageReference`, `PackageVersion` or `GlobalPackageReference` whose `Version` or `VersionOverride`, as an attribute or a child element, contains a literal `*`; `Detector_flags_star_versions_in_attributes_overrides_and_child_elements` proves a star is flagged in a `Version` attribute on each of the three item types, in a `VersionOverride` attribute and in a child `<Version>` element, and that a plain version and a bracketed range are not; a child `<VersionOverride>` element goes through the same code but has no fixture case. It does not require an exact version. A non-floating bracketed range passes: `Antiphon.Messaging.Gateway` keeps `Confluent.Kafka` at `[2.6.0,3.0.0)`, which the lowest-applicable rule resolves to 2.6.0 while 2.6.0 is on the feed (a newer 2.x is taken only if it is not); restore resolves 2.6.0 today. MSBuild properties are not evaluated: a `$(Name)` reference passes unless its own text contains `*`, so a property defined elsewhere as `9.*` is not caught. `samples/EchoGateway` takes `Antiphon.Messaging.Gateway` from `$(AntiphonMessagingVersion)`, which defaults to `1.0.0` and can be overridden with `-p:`. `.config/dotnet-tools.json` is outside this scan; `DotnetToolManifestContractTests` owns it.

Pins follow the latest-stable policy and are bumped deliberately, not frozen: list candidates with `pwsh -NoProfile -File scripts/build-slot.ps1 -Label pin-bump -- dotnet list Antiphon.sln package --outdated --highest-patch` (drop `--highest-patch` for a deliberate minor/major move), move one package family per bump to one version in every project that references the packages you move, restore with `dotnet restore Antiphon.sln --force-evaluate --no-http-cache` through the build slot and accept the bump only with zero NU1603/NU1605 diagnostics, then build the affected projects. No test checks that a bump followed this procedure, or that a family shares one version; today versions differ within families (EF Core packages sit at 9.0.21, 9.0.20 and, in the Messaging projects, 9.0.4). Keep the `dotnet-ef` manifest pin above on the same major as `Microsoft.EntityFrameworkCore.Design`. Disclosure: `Microsoft.EntityFrameworkCore.Design` 9.0.21 is now on nuget.org, but the server's Design pin and the `dotnet-ef` pin both stay at `9.0.20` on purpose; move them together in a deliberate EF bump. CARD-1229 took the other pins from a clean restore of the earlier floating references; for Design its Code report says 9.0.21 was not yet listed, which Review d93e4ca3 could not independently confirm (the 9.0.21 catalog entry is dated during that work), so this one pin is not proven to match what the floating reference resolved before.

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

CARD-1506: Windows build/test children use the single policy in
`scripts/lib/BuildDriverEnvironment.cs`: shared compilation, server/concurrent GC,
MSBuild server use and node reuse are disabled before child startup. The build-slot
wrapper, checkpoint tool/script, land verifier, nightly native/exact runners and
watched-test launcher apply it. AppHost and daemon builds use the wrapper; their
subsequent runtime launches retain the caller's normal environment. Linux is
unchanged. Set `ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT=1` in the invoking environment
to bypass this policy (existing graph-evaluation/node-reuse protections remain).
No process is stopped and no machine/user environment is modified. Incremental
Windows builds may be slower. After the reviewed rc pick, restart the rc AppHost
and desktop session runner when the active qualification permits it; a master
land alone does not activate the compiled land-verifier helper.

Nothing else bounds how many `dotnet build` / `dotnet run --project tests/*` drivers, MSBuild worker nodes, compilers and test hosts run at once across the sessions on one host; the outage behind CARD-0589 had 203 build processes and 1.1 GB free. Three layers do, cheapest first. Plan: [2026-09-25-card-0589-build-fanout-cap-plan.md](superpowers/plans/2026-09-25-card-0589-build-fanout-cap-plan.md).

**`Directory.Build.rsp` at the repo root** carries `-nodeReuse:false`, so every invoker (a delegate's raw command, the wrappers, the land verifier, the nightly, `run-daemon.ps1`'s rebuild) leaves no MSBuild worker node behind. CP-1 measured it on the server2 Linux runner (SDK 10.0.401, 24 cores) building `tests/Antiphon.Messaging.Tests --no-incremental`: nine worker nodes at peak, every one started `/nodeReuse:false`, zero alive ten seconds after the driver exited (before this file, 17 orphaned `nodeReuse:true` nodes held 4.7 GB for up to 3.3 h). A `-maxcpucount:2` line in the rsp was **ignored**: the SDK prepends its own bare `-maxcpucount`, the diagnostic log still showed `MSBuildNodeCount = 24` and the peak stayed at nine. On the command line `-maxcpucount:3` gave `MSBuildNodeCount = 3`. So the rsp holds no count (`DirectoryBuildRspTests` pins that) and the per-build count comes from the grant. `UseSharedCompilation` stays on (the shared `VBCSCompiler` idles out after ten minutes).

**The session runner brokers a host budget of build/test driver leases** at `/build-slots` (desktop `http://localhost:17204/build-slots`, the server2 container `http://127.0.0.1:8080/build-slots`). One lease is one driver invocation, held by the wrapper process that asked for it: `POST` grants `{ leaseId, maxCpuCount, occupied, budget, expiresAtUtc }` or answers 409 `build_slot_busy` (`occupied`, `budget`, `queuePosition`, `retryAfterMs`) or 409 `build_slot_memory_floor` (`availableMb`, `floorMb`); `DELETE /build-slots/{leaseId}` releases (404 `build_slot_unknown` once gone); `GET /build-slots` lists budget, occupancy, live memory, leases (`holderAlive`) and waiters. `GET /build-slots/metrics` (CARD-1489) is a read-only lifecycle summary over the last three hours: grants (once per lease, however often the holder re-asks), explicit releases, reaps, dropped silent waiters, busy and memory-floor refusal polls (per-minute counts, not queued jobs), acquire-wait and held mean/p95/max and releases per hour. It keeps at most 4096 lifecycle records and says so (`truncated`, `droppedRecords`, `windowStartUtc` at the oldest retained record); `partialWindow` means the broker started inside the window. Reading it neither sweeps nor changes admission. On server2 read the shared broker at `ANTIPHON_BUILD_SLOTS_URL`, not the runner loopback. Waiters are served in FIFO order and a waiter silent for 60 s loses its place. A grant is refused while live available memory (`GlobalMemoryStatusEx` / `/proc/meminfo` `MemAvailable`) is below the floor, even with a free slot; a held lease is never revoked. The runner reaps a lease whose holder pid died or was recycled, or that is past its 90-minute TTL, on every acquire and every 30 s, and logs each reap. It kills nothing.

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

Never chain a second driver after the gated command with a shell operator: the lease ends when build-slot.ps1 returns, so `-- dotnet build ... && dotnet exec ...` runs the exec unleased and Review flags it.

The wait is visible in the transcript: `BUILD SLOT waiting label=<l> position=<n> occupied=<o>/<b> elapsed=<m>m` (or `reason=memory_floor available=<a>MB floor=<f>MB`) on each new reason and once a minute, then `BUILD SLOT granted lease=<id> waited=<s>s maxcpucount=<n>` and `BUILD SLOT released lease=<id> held=<s>s`. Two failure modes:

- **Timeout** (busy or below the floor for the whole `-SlotWaitMinutes`, default 45): `BUILD SLOT timeout after 45m position=<n>`, **exit 4**, nothing built and no `dotnet` call. Report the row as not run, or end `blocked`; never retry it unleased or with `-NoSlot`.
- **Unreachable runner** (connection refused, a timeout, or an old runner answering 404 on `/build-slots`) for 60 s: the command runs unleased at `-maxcpucount:4` and prints `BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=<no answer|http 404>`, so Review can tell a budgeted run from an unbudgeted one. A delegate stalled behind a restarting runner is the CARD-0448 shape; the outage needs many concurrent builds, which nothing dispatches while the runner is down.

`ANTIPHON_BUILD_SLOTS_URL` overrides the endpoint (an isolated runner; the tests' loopback broker). `-NoSlot` (`BUILD SLOT skipped by -NoSlot`, no lease, no `-maxcpucount`) is for an operator shell only. `SessionRunner__BuildSlots__Enabled=false` makes every acquire answer `unlimited` (`BUILD SLOT unlimited maxcpucount=<n>`, nothing held): the rollback lever, with deleting `Directory.Build.rsp` the other. A raw driver typed outside the gate still runs (with `-nodeReuse:false`); Review flags it as a checkpoint defect, the same shape as an unlisted run.

**The desktop land verifier** takes a slot through the local runner (`SessionRunnerBuildSlotGate`) for its build and test run with the same rules: it waits at most `Landing:BuildSlotWaitMinutes` (30; a timeout is a `build-slot` verification failure with nothing built), builds with the grant's `-maxcpucount`, and an unreachable runner is never a land failure: it builds unleased at `-maxcpucount:4` after `Landing:BuildSlotUnreachableGraceSeconds` (60). CARD-1461 option C adds a bounded wait for the memory floor alone: once the runner has answered `memory_floor` for `Landing:BuildSlotMemoryFloorFallbackMinutes` (default 0, off: the floor counts toward the wait and times out as above), the verifier builds unleased at `-maxcpucount:<Landing:BuildSlotMemoryFloorFallbackMaxCpuCount>` (default 2, clamped to at least 1) and reports `BUILD SLOT unleased reason=memory_floor_fallback available=<a>MB floor=<f>MB waited=<s>s maxcpucount=<n>`, logged at `Warning` where every other `BUILD SLOT` line is `Information`. The window is measured from the last answer that was not a floor refusal (zero at the start of the acquire; a busy answer or a missed answer restarts it), so a busy broker never falls back and the unreachable grace is unchanged; the timeout check comes first, so a window at or above `Landing:BuildSlotWaitMinutes` never fires. The fallback build is unbudgeted: it holds no lease, other waiters do not see it, and there is nothing to release afterwards. Its `BUILD SLOT` lines go to the verifier observer and the server log with the land correlation. The nightly joins in CARD-0589 Round 2, with a detect-only build watchdog and the phone-home read/set of the budget.

Offline seams, tests only: `C589_SLOT_SHIM` (a script answering in place of the HTTP call; `scripts/fixtures/c589-slot-shim.ps1` scripts `granted|unlimited|busy|memory_floor|notfound|unreachable` per `C589_SLOT_SCRIPT`), `C589_SLOT_WAIT_SECONDS`, `C589_SLOT_RETRY_MS`, `C589_SLOT_GRACE_SECONDS`, and the wrapper's `C589_COMMAND_SHIM`. Harnesses: `scripts/test-run-checkpoint.ps1` (`Test-C589_*`, `RunCheckpointScriptTests`) and `scripts/test-build-slot.ps1` (`BuildSlotScriptTests`); the cross-process proof is `BuildSlotEndToEndTests` against a loopback broker, never a production runner.

CARD-1379 changes the verifier's repository-lock lifetime, not its slot or
compiler policy. The land-execution lock remains held; the repository mutation
lease and its read cache are released before artifact preparation and slot
waiting. Only matching live verifier journals can be ignored by a typed
preflight from that same live coordinator. Child exit and both output drains
precede closing the window and reacquiring a genuine repository lease.
Interrupted Prepared operations verify again. A completed artifact generation
is never a publication receipt.

## Host-independent cache disk readings (CARD-0997)

`RemoteScriptContractTests` reaches `c849_budget_gate` and `c849-import-saved-donor.ps1` with fake free-space readings. The shell gate's `df` is a fixture-local function that accepts only `df -Pk <DockerRootDir>` and never calls the host `df`. The importer's optional `-GetAvailableFreeBytes` scriptblock defaults to `DriveInfo.AvailableFreeSpace` for the stage path it is given; `Check-Space` still compares that reading with 20 GiB plus the validated package and npm byte totals. `c849_saved_copy` and every other operational caller omit the parameter, so they keep the real disk admission. These Unit tests do not lower either threshold and do not skip when the host disk is low.

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

Import and manifest validation enforce the canonical suffix on **every** class
or method operand (CARD-0931). All-bare OR has demonstrated zero discovery;
mixed starred/bare OR is refused by canonical-form policy even though one
wildcard can suppress this pin's literal discovery hint. Single exact methods
need no suffix. OR inside square-bracket property expressions is preserved.
Use separate exact-method rows sharing one build if suffix matches are too broad.

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
remote service or pre-existing process. Literal eligibility checks are in docs/mechanical-pc-contract.md.

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
assembly-local `ParallelLimiter<ProcessSpawnLimit>` (and `GitProcessSpawnLimit`) model. That limiter serializes process
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

For local rc inspection, emergency manual stop and evidence-preserving stale-lock
recovery, use [Inspecting and stopping a qualification](release-gates.md#inspecting-and-stopping-a-qualification).
The shared `native-run.lock` serializes native work across lanes and is never
automatically reclaimed; the state root's `run.lock` has separate reclaim rules.
A dead recorded owner alone does not prove its children have stopped.

The pinned TUnit 1.44.0/MTP 2.2.2 diagnostic adapter reads unescaped human-readable
records, not a general serialization. It selects the unique identifier/location-to-state
transition and validates exception boundaries against the explanation echo. Ordinary
quoted headers, braces and delimiters are preserved. A complete competing transition
keeps execution Failed; an ambiguous explanation split omits Message. TRX still owns
the suite verdict. See [CARD-1325 fixtures](../scripts/fixtures/nightly/c1325-diagnostic-records.ps1)
for captured, derived and reflected provenance. The original bounded 2026-10-20
CARD-1325 timeout and cancellation probes both emitted Error; their controls
establish reflected-shape parsing only. The subsequent
[CARD-1375 captures](../scripts/fixtures/nightly/c1375-provenance.md) supply real
Timeout and Cancelled producer records: an elapsed `[Timeout]` with detailed
stack traces, and local cancellation followed by TUnit's public result override.
The latter does not establish automatic classification of external cancellation.
These native records have empty exception fields and an empty Timeout value;
an empty cancelled explanation remains absent Message. Changing
`lib/nightly-coverage.ps1` correctly invalidates an authority bound to its old script
digest; this changes no authority-refresh or release procedure.

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
Task. Each qualification chunk retains `<LogRoot>/<suite>-<chunk>.timing.json`
and `LogRoot/summary.json` carries `timingRoot`, `timingChunks`, and
`suiteTimeline`, as described under
[Opt-in per-test phase timing](#opt-in-per-test-phase-timing-card-1514).
**Reduced dispatch verification stays inactive** until S4 qualification
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
replaced by age. A reader of `run.lock` opens with `FileShare.ReadWrite` so
Windows can read it while the owner holds `FileAccess.Write` and `FileShare.Read`.
`dead-owner-rebooted` applies only when both boot ids are registry `BootId`
decimals. A Linux boot-id UUID change stays `lock-held`, and a lock whose
`bootId` is still a `LastBootUpTime` string stays `lock-held` across one Windows
reboot; both need the stale `run.lock` removed by hand. If the shared reopen
after a reclaim publish fails, the winner still owns the lock and the run logs
`reclaim-shared-reopen-failed`. `last-run.json` is the last attempt; `last-complete-green.json`
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

### Windows rc watchdogs and gate lanes (2026-10-18/19)

**Windows rc profile watchdogs.** `tests/test-execution-policy.json` `watchdogs` are `antiphon`
3600000 ms, `session-runner` 1800000 ms and `e2e` 7200000 ms. The cap applies per generated
launch; profile-aware rc suites can have multiple class/method launch groups. A watchdog kill
deletes that launch's process before TUnit writes its TRX, so a timed-out launch is
`missing-trx` / `coverageComplete=false` whatever its tests did. The numbers come from the
2026-10-18/19 gate logs. Session-runner went from 20 to 30 minutes because the expanded suite
(about 1,550 UIDs) was still admitting its last test at the 20-minute kill while its longest
returned test is under three minutes, so 30 minutes writes the TRX. `e2e` got its own key at 120
minutes because the suite had been using the shared 60-minute fallback and the serial
`AgentTaskLandDeliveryE2ETests` class alone needs about 90 minutes. `antiphon` stays at 60
minutes because raising it is not the remedy: the one-slot `ProcessSpawnLimit` lane admits about
46 UIDs a minute (a figure from before CARD-1421 moved git-only classes to the wider `GitProcessSpawnLimit` lane) and neither 90 nor 120 minutes reaches the end of the census. Do not read a
silent `InProgress` in the `--no-progress` diagnostics as a hang: keep the last state per UID and
treat a `Duration` as returned. Records: [watchdog sizing](investigations/2026-10-19-rc-watchdog-sizing.md)
on `feat/card-task-210e914f`, [hangs](investigations/2026-10-19-rc-windows-hangs.md) on
`feat/card-task-5b8ad483`, [live reds](investigations/2026-10-19-rc-windows-live-reds.md) on
`feat/card-task-93971d09`.

The [2026-10-22 group-10 remeasurement](investigations/2026-10-22-rc-group-10-timeout-debug.md)
corrects the earlier single-line diagnostic interpretation: group 10 kept completing tests
until its watchdog killed the underpriced launch. The
[measured-budget plan](superpowers/plans/2026-10-22-card-1404-group-10-measured-budget-plan.md)
prices its two complete classes from the slower complete sample and labels Publication's
partial-sample price as provisional `pace`. Existing method splitting gives seven launches
under the unchanged 60-minute cap. CARD-1514 S3 subsequently separates the two real
CheckpointMatrix worker cases from its 41 Git-only cases: the split projection has eight
launches, and each split class conservatively retains the full historical 1560-second
bound as `pace` until Windows per-method measurements exist. The original 43-node
measurement and raw listing remain historical evidence. Historical failures and
missing-TRX coverage remain incomplete.

**Gate lanes run beside other work on server2.** The Linux rc roster shares the host with Code,
Review and other gate tasks, and must tolerate that: run it in its own detached worktree and
output path, and read slot wait and memory from the broker rather than assuming an idle host. The
`rc/2026-10-18` re-gates flipped between green and red with no source change, and neither flip was
a neighbour's build. The five `CheckpointConfinementLedgerTests` reds came from the gate driver
unsetting `ANTIPHON_TASK_TOKEN` while leaving `ANTIPHON_TASK_ID` set, so `TaskOwnerGuard`
answered `owner-unverified` (exit 7) before the scheduler ran; unset the task id and token
together, as the final gate did, or pass `EnvironmentLookup = _ => null`
([ledger nondeterminism](investigations/2026-10-19-confinement-ledger-nondeterminism.md) on
`feat/card-task-5151af90`). The one load-sensitive red is the `/proc/<pid>/stat` registration
race: a short-lived `git init` can be reaped between `Process.GetProcessById` and the stat read,
and the resulting `DirectoryNotFoundException` is treated as "process gone" by neither
`CheckpointTestScope.Register` nor `ProcessIdentityProbe.Observe`; it is a flake (40 of 40
isolated passes) whose fix is those two catch sites
([/proc flake](investigations/2026-10-19-rc-proc-stat-flake.md) on `feat/card-task-75ae5020`).
What still couples a gate to its neighbours is the build-slot lease held for the whole roster,
the 16 GB memory floor, the shared git object directory and one roster test that deletes
`/tmp/grok-download` ([gate lanes beside other tasks](investigations/2026-10-19-test-concurrency-isolation.md)
on `feat/card-task-047e36c5`). The final green roster ran with zero competing leases and a
one-minute load average up to 25 ([Linux final](investigations/2026-10-19-rc-linux-final.md) on
`feat/card-task-aaee7f74`).

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
repository lease before output/ref/tree deletion. Unknown custody remains residue. Terminal-task
CleanupVerification can terminate Linux containment; a Windows seal does not terminate, and the
existing linger and kill-on-close job remain. The task orchestrator invokes that cleanup after
Succeeded, Failed, or Canceled settlement and retries unresolved residue. It is not periodic and
it has no age deadline. Dead-host recovery kills only the recorded container and stays Unknown.
A live host that adoption could not take stays alive until the operator stops that exact host.
Missing or unreadable accounting is never zero. Prolonged Draining and unresolved Unknown are
warnings only. A successful kill is not a receipt and is not a source-clean verdict.
Publication, report disposition and cleanup are independent verdicts. Deploy all source/custody/
cleanup/contracts together and directly inspect loaded selector, bundle hashes and runner/host
tracking before commissioning the feature's own PCs; caller-owned rollout cannot be fixture proof.

## Checkpoint temp custody and usage (CARD-0804/0805)

CARD-1264: every lifecycle-host caller passes `--results-directory` pointing to
`TestResults` under its owned temp sandbox, rather than beside the staged DLL.
The two C# callers and the orphan-usage script also set TMPDIR/TEMP/TMP to that
sandbox and join the child before removing it. TUnit keeps its generated entry point.
This keeps HTML reports out of sealed build output, so a green Linux checkpoint
run needs no `--keep-outputs` workaround. Post-seal files are adopted only by custody at the driver-observed host exit, never by name
(CARD-1368); a file outside that window still refuses cleanup.
CARD-1272 applies the same rule to the staged filter host (`RerunFilterHostTests`)
and repeat host (`CheckpointRepeatHostTests`): each launch passes a results directory
in its owned sandbox and sets TMPDIR/TEMP/TMP to it.

Checkpoint tests allocate `c723-<guid>` roots through `CheckpointTestBase.TempDir()`.
The per-test TUnit hooks register a marker and root index before returning the path,
join registered work and exact child processes, and remove owned roots after each test.
A failed teardown is a test failure. A linked tree, live or uncertain nested executor,
or failed delete leaves the marked root visible for a later guarded retry. Do not use
`CheckpointFixtures.TempDir()` or an unmarked test root.

The assembly startup hook admits a bounded sweep every five minutes. Registration
uses one independent index file per marked root under `.checkpoint-temp-roots/`, so
allocations do not wait for the shared sweep lock. Disposal removes its index file;
stale entries for absent roots are removed when visited. A test that deliberately
makes teardown retain a root releases it with a second scope disposal after clearing
its veto, never a bare `Directory.Delete`, so the root's index pointer leaves with it
(CARD-1063). The sweep checks the
persisted interval before trying the lock. An older shared JSONL index is migrated
under that lock, with disposed entries discarded. It considers only exact indexed direct
children of the OS temp directory, waits ten minutes from
marker creation, and requires confirmed owner and nested executor death. Before
taking a root lock, a validated marker excludes young roots and live or uncertain
owners so their nested runs can register without sweep contention. This preflight
never authorizes deletion: after acquiring the root lock the sweep freshly checks
marker identity, grace, owner death, containment, inventory and nested custody.
One pass
is bounded by 512 index entries, 10000 descendants per root, 16 completed roots,
256 MiB of payload and two seconds. A partially deleted root keeps its marker and
is rechecked on resumption. Unknown identities, linked paths, malformed/unmarked
roots and launch attempts without a proven executor outcome remain visible.

CARD-1254 uses a dedicated marker transition while holding the root gate: the
sweep rechecks the exact marker identity and dead owner, then atomically replaces
only that marker with state `deleting`. It does not grant general overwrite
authority to files created by another process. The test scope records its own
exclusive marker creation through SafeWrite so same-process marker changes also
retain their creation provenance.

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

`Get-NamespaceCensus` derives `selected` by a comment- and string-aware scan of
`tests/Antiphon.Tests/Checkpoints` (CARD-1342). The scan counts the same cases as
`CheckpointRoster.CompiledCases`: non-abstract classes in the exact namespace
`Antiphon.Tests.Checkpoints`, instance `[Test]` or `[TestAttribute]` methods that
are not `[Explicit]`, one row per `[Arguments]` or `[ArgumentsAttribute]`, and one
row when a method has none. `[Test]` text inside comments or string literals is
not a case. Namespace admission and the Full checkpoint execution floor compare
the supplied roster with that count. Do not derive the count from the roster
being checked, and do not replace the scan with a hand-maintained literal. OS
skip names stay explicit lists matched against the roster.
`CheckpointNamespaceCensusUsageTests.namespace_census_matches_compiled_checkpoint_cases`
checks the scan against reflection. That class carries `Category=Unit`, so the
ordinary Unit lane catches scanner drift while `CheckpointTempUsageTests` remains
in Integration.

CARD-1199 receipt readers inspect file types without following links before opening required state,
report and build evidence, then recheck the retained handle. Linux uses nonblocking opens so a
FIFO substitution cannot block an open. Unsupported required receipt file types (links, directories, pipes, sockets, devices) and invalid
row/build identities refuse with a named reason and exit 2. Report/terminal receipt validation also
returns named exit 2 for missing serialized facts and invalid shapes. Wait retains its documented
dead-executor exit 6 for other missing, empty, malformed or unreadable state reads; if death is
unconfirmed it continues until its wait limit. Nullable diagnostic fields may be null; empty collections are valid
when no entries are required. Receipt reads have a 64 MiB bound. Markdown digests hash exact bytes.

CARD-1199 source-ledger stamping and receipt reads open regular files only through the verified native reader. CARD-1532 regular-file stamps stream every byte into SHA-256 with a 1 GiB inclusive content bound and retain `file|length|lowercase-sha256`; receipts retain their separate 64 MiB cap (documents 4 MiB, source text 1 MiB). CARD-1601 runs each read on a dedicated thread. Native Linux reads execute synchronously on that thread; Windows keeps its overlapped, no-follow opens. The caller supervises progress with monotonic monitor waits, without a thread-pool timer. Three seconds of silence refuses with `read-stalled: <got> of <size> bytes`; caller cancellation stays cancellation. An independent overall cap is 30 seconds plus one second per rounded-up MiB of inspected content (31 seconds for a tiny receipt, 1054 seconds for a 1 GiB stamp). The cap budgets at least 1 MiB/s after a fixed allowance and never restarts on short reads; expiry says `read-deadline-exceeded`. The three-second silence budget removes CARD-1599's six-second scheduling stopgap because Linux reads no longer wait for pool workers. A cancelled operation gets at most 100 ms, within the remaining overall cap, to unwind and perform the retained-handle EOF check. A syscall that cannot be cancelled retains its handle, buffer and sink on its owning thread until it returns; the caller refuses without waiting for it. Initial oversize and growth past the content bound refuse; no whole-file buffer is used for stamps. Links/directories/absent paths contribute metadata-only shape stamps; unsupported special files and unreadable regular contents are unprovable. Ignored prior `bin-*` and `obj` files stay covered: write roots change capture timing, never ownership or inclusion. Settlement lists changed/unprovable paths and leaves the resulting filesystem shape and index untouched. Import and coverage refuse blank checkpoint identities before writing a manifest or reporting clean coverage; build identities are derived from nonblank isolated output syntax.

Windows checkpoint reads use `FILE_FLAG_OVERLAPPED` in both `CheckpointFileReader`
and the embedded `scripts/lib/checkpoint-file-reader.ps1` reader. The retained
`FileStream` detects that handle mode; a synchronous Windows handle makes
`ReadAsync` queue blocking I/O on a worker even though the caller is already
blocking for the bounded result. Worker availability can then consume the
three-second window before any bytes arrive. Keep the no-follow and sequential
flags alongside the overlapped flag. `CheckpointWindowsReadModeTests` models
unavailable workers with controlled time; the `windows-open` case in
`CheckpointScriptReaderDeadlineTests` executes the embedded Windows factory with
only its kernel open substituted and checks the resulting stream mode/content.
These portable witnesses supplement native Windows qualification; they do not
establish the scheduling state of a historical Windows run.

### CARD-1199 repair 13j reader qualification

The admitted content/Git/graph readers added or changed by CARD-1199 are the project XML preflight, the four BuildConfinement/run-checkpoint Git/graph readers (CP-21) and SafeDelete.RunGit, the cleanup and output-write Git preflight (CP-22 plus the CP-1/CP-7/CP-8 contract cases); [the scoped reader inventory](superpowers/plans/2026-10-13-card-1199-inherited-readers.md) classifies the process launches that its repair 13k census command finds. The inventory gives the command and its limits so a reviewer can rerun it. Eight repaired fixture sites run through CheckpointFixtureProcess and the shared runner (CP-23): OutputCleanupTests.Git and its parent-symlink and wrapper-preflight pwsh probes, the CheckpointTaskOwnershipTests ownership-loss `git init`, CheckpointOutputConfinementTests.Git and SeedBuild, CheckpointShapeConflictTests.GitOut/Git and CheckpointTrackedBaselineTests.GitOut/Git; the other CARD-added fixture launches and their gaps are in the inventory's [repair 13k census](superpowers/plans/2026-10-13-card-1199-inherited-readers.md#repair-13k-census-and-fixture-qualification). The pre-existing unbounded readers, unchanged-master origins, reproductions, identity exceptions and accepted diagnostic/staging/execution-loader exclusions are listed in [the scoped reader inventory](superpowers/plans/2026-10-13-card-1199-inherited-readers.md). In particular, inherited SourceSnapshot.RunGit and Get-CheckpointGitBytes bound process exit but retain unbounded output drains. The new project preflight uses the existing regular-file/size/cancellation boundary; the four added Git/graph readers and SafeDelete.RunGit share CheckpointProcessRunner, whose 15/30/120-second deadline covers config inspection, exit and both drains, with fsmonitor, hooks, optional locks, global/system config and named filter/textconv drivers disabled. Linux process groups retain descendant custody after direct-child exit. Windows job assignment is attempted after launch; native qualification, including the pre-assignment child race, remains R-2.

Tool import/coverage and strict receipt admission refuse whitespace row/build IDs. The inherited PowerShell -Name boundary admits whitespace because Name@Project is nonblank; broker label admission does not supply a whitespace-name guard. The standalone Invoke-Dotnet and legacy acceptance-harness output loops have no local deadline.

CARD-1363 ports the CARD-1358 design to the PowerShell path. `scripts/lib/checkpoint-file-reader.ps1` embeds the `ConfinedFileReader` behind `Read-CheckpointBytes`, originally used by the `run-checkpoint.ps1` source stamp and still used by `checkpoint-source.ps1` (when the tool assembly is already loaded, its own reader serves instead). It uses the same dedicated-thread, three-second silence and size-proportional overall budgets as the compiled reader (CARD-1601), including cancellation and retained-handle EOF reinspection. Neither deadline requires a thread-pool callback. `Get-CheckpointContentStamp` stamps a refused read `unreadable|<reason>` (the innermost message on one line, at most 200 characters) and `Complete-CheckpointBaseline` classifies it: an entry whose recorded or current stamp is unreadable is `refused: unreadable` with an `UNREADABLE <path>: <reason>` line per entry and no restore command, since no content difference was observed; a readable difference beside it is still `refused: tampered`, with the `UNREADABLE` lines listed before the restore line. Both exit 2. The reader's optional `TimeProvider` and handle-stream factory parameters are test seams only. `CheckpointScriptReaderDeadlineTests` drives `scripts/test-checkpoint-reader-deadline.ps1`, which paces a manual clock inside an injected stream (no wall-clock sleep) and proves the ledger kinds with a second hard link outside a fixture repository (Linux); `CheckpointStampSafetyTests` expects the FIFO stamp to start `unreadable|`.

CARD-1532 moves only regular-file PowerShell stamps to `Get-CheckpointFileDigest`;
receipt/text consumers remain on `Read-CheckpointBytes`. Both embedded consumers
share the verified-handle consumption loop. As in CARD-1369's .NET reader, an EOF
stall succeeds only after reinspection of that same handle proves its size is
unchanged and every initially inspected byte arrived. Growth, shrink, failed
reinspection and caller cancellation cannot certify complete content. The loaded
tool's compatibility facade forwards digest calls to the same .NET implementation.
`CheckpointLargeStampTests` and `CheckpointLargeStampScriptTests` cover real 70 MiB
files, a one-byte edit at 69 MiB with restored mtime, independent full hashes,
separate bounds and custody. The scripts remain ASCII and require PS7 at runtime;
the parser-only harness requires actual Windows PowerShell 5.1 Desktop unless its
explicit PS7 test switch is supplied. PS7 parsing is not Windows parser evidence.

### Fresh-output inventory (CARD-1573)

`Seal-CheckpointFreshOutput`, called by `run-checkpoint.ps1` after a verified build, inventories the fresh output with `Get-CheckpointFileDigest`. That wrapper calls `ConfinedFileReader.Digest`: streaming SHA-256, 256 KiB chunks, the inclusive 1 GiB `ContentLimit`. The compiled facade in `tools/Antiphon.Checkpoints/Coverage/ConfinedFileReader.cs` forwards `Digest` to `CheckpointFileReader`, which is the library owner of the verified read. The embedded reader in `scripts/lib/checkpoint-file-reader.ps1` implements the same `Digest` method and is compiled only when that facade is not already loaded. Source stamps and this seal call `Digest`, `ReadBytes`, and `ContentLimit`. Those members are the shared surface; a seal-only method on one facade would make a healthy stamp fail when the tool assembly is already loaded. `scripts/test-checkpoint-compiled-facade.ps1` loads the compiled assembly and then runs the real `scripts/lib/checkpoint-file-reader.ps1` caller.

The ledger value is the uppercase hex of those bytes. A small file still matches a receipt-reader `SHA256.HashData` of `Read-CheckpointBytes`. The 64 MiB cap remains the receipt and text bound. Memory stays one chunk. The three-second silence window re-arms after each delivered chunk; the independent overall budget does not.

After every regular file has been hashed, and before the ledger is written, the seal hashes each path again, one file at a time. The second digest is the content fingerprint. The ledger is written only when every rechecked file still has the same length and SHA-256. A replacement that reuses the original inode and preserves size and mtime changes the fingerprint and is refused with `changed while sealing`, and no ledger is written. A file above 1 GiB, or one that grows past the bound while it is read, is refused before a ledger is written. A symlink, reparse point, or non-regular file is refused too. The refusal names the file, the size, and the limit: `output-inventory-failed: <path> size=<bytes> limit=<bytes>: <reason>`.

A file changed after its own second hash has matched, while a later file is still being re-hashed, is sealed with that earlier digest. The ledger then records the wrong digest for the file that changed in that window. That window is no worse than base. A writer that restores the original bytes before that file's own second hash, and a writer that changes a file after the ledger write, are outside this seal.

`scripts/test-checkpoint-output-inventory.ps1` seals a real 70 MiB file and refuses one byte over 1 GiB on every host. Append, truncate, and same-size swap stay portable. The symlink, FIFO, and inode-reuse (`stat -c`) cases run when `$IsLinux` is true, the same gate as the FIFO case in `scripts/test-checkpoint-large-stamp.ps1`. `-HideStat` forces the other branch and removes `stat` from `PATH`. On that branch the three cases print `SKIP <case> (Linux only; pending native Windows witness)` and do not call `stat` or `mkfifo`. The native Windows run of those three cases is still pending.

### Unit lane checkpoint shards (CARD-1573)

A Full Final receipt for the Unit lane is a set of checkpoint rows that share one build. The selection `/*/*/*/*[Category=Unit]` on `tests/Antiphon.Tests` remains the local and nightly definition of that lane. Its wall time is past 30 minutes, so one row exhausts the default execution allowance from `RowTimeout.DeriveTotalMinutes` (`max(30, 2 * sum(EstimatedMinutes) + 10)` when `--total-timeout` is omitted). An `EstimatedMinutes` above 15 makes `DeriveRowMinutes` return more than 45, and `PlanTableImporter` warns above `RowTimeoutAdvisoryMinutes` (45).

Each shard row:

- Names `tests/Antiphon.Tests`, the same `OutputPath=bin-<id>/` (forward slash), and the same `After` group. The tool builds once, seals that fresh output once, and later rows reuse the build. The seal above is what lets that build record `buildSource=verified` for `Antiphon.Tests.dll`.
- Sets `EstimatedMinutes` to at most 15, so the derived row timeout stays on the 45-minute advisory.
- Uses a [combined class filter](#combined-class-filters-card-0403): `/*/Antiphon.Tests.<Namespace>/(ClassA*)|(ClassB*)/*`. Every class in the row carries `[Category("Unit")]` on the class. A class with another category stays in its own non-Unit row. The shard rows partition the Unit classes, each class in one row.
- Selects only that class-level tag. A method-level `[Category("Unit")]` on a class with another category is outside these rows. `tests/Antiphon.Tests/Application/MatrixDefaultComplexityCreateTests.cs` is `[Category("Integration")]`, and `The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled` at line 130 is `[Test, Category("Unit")]`. The full lane filter `/*/*/*/*[Category=Unit]` still selects that method. A class shard does not.
- Is split again when a measurement would put `EstimatedMinutes` above 15.

Set `--total-timeout` to at least the derived execution allowance for the whole shard set. Shard runtimes are UNMEASURED. Because those runtimes are unmeasured and the shard rows omit method-level Unit tests, executing the shard rows does not demonstrate `ordinaryScopeCompleted=Full`. A Full Final receipt for the Unit lane is not yet demonstrated.

## Opt-in per-test phase timing (CARD-1514)

S1 and S2 record where a test spends time when a run opts in. The sole runtime enable is `ANTIPHON_TEST_PHASE_ROOT`. When that variable is unset or blank the recorder does nothing: no file, no stopwatch, no redaction, and no default path. Metadata variables are `ANTIPHON_TEST_PHASE_RUN_ID`, `ANTIPHON_TEST_PHASE_GROUP_ID`, `ANTIPHON_TEST_PHASE_ATTEMPT`, and `ANTIPHON_TEST_PHASE_SUITE`. Missing or invalid metadata disables only that recorder (`phase-metadata-missing`, `phase-root-invalid`). A failed append disables the sink with `phase-sink-failed` and leaves the test outcome unchanged. Discovery, npm, lint, dotnet build, and ordinary child wrappers do not receive the variables. Ordinary CI and dev runs stay off unless the root is set.

Nightly and rc qualification, including `scripts/release-cut.ps1`, retain a timing root for every chunk. An explicit `-TimingRoot` wins. Otherwise the root is `<LogRoot>/timing-<RunId>` when a run id is set, and `<LogRoot>/timing` when it is not. `release-cut.ps1` preserves a sealed attempt's timing root, including a blank one, and resolves that default only when it seals a new attempt. `nightly-run.ps1` resolves it only when no selection attempt is already sealed, so an older attempt whose `timingRoot` is blank still matches the hop check. The sealed attempt file is not rewritten. When that sealed value is blank, `summary.json` copies the resolved root onto its in-memory `selection.timingRoot`.

Every chunk writes `<LogRoot>/<suite>-<chunk>.timing.json` beside the TRX path, including native chunks, e2e class splits, client, scripts, an e2e prerequisite failure (`chunk` `prereq`, `why` `e2e-prerequisite-failed`), and a suite the build or preflight never started (`chunk` `unstarted`). A copy is also placed under the timing root when that directory is different. The sidecar written before the long process (`lastPhase` `run`, `lastPhaseUtc` set) comes from the call arguments only. JSONL shards, TRX-derived durations, and sidecar content are read only after that chunk process has exited and its exit code is recorded. TRX results and JSONL shards are streamed: memory is capped per start tag and per JSONL line at 256 KiB, and per captured StdOut at 16 KiB, while the shared post-exit stopwatch still stops the read at 200 ms. The stream does not cap the file size or the result count. The timing.json sidecar keeps a 256 KiB whole-document cap. A refused or budget-expired read leaves the phase fields empty and sets `why` to `timing-read-refused` when the chunk did not already have a why. It does not change the chunk exit code. A timeout keeps `lastPhase` `timeout` and `why` `timeout` and does not invent `completedAt`. A chunk with no TRX still has a row: `missing-trx`, `no-trx:client`, `missing-client-json`, `no-trx:scripts`, or `script-failed`. A timing write failure does not change the suite verdict.

`LogRoot/summary.json` lists `timingRoot`, `timingChunks`, and `suiteTimeline`. Each chunk row has queue wait, build, discovery, and run wall seconds, per-test p50/p95/max, the slowest 20 tests, `lastPhase`, `lastPhaseUtc`, and `why`. `suiteTimeline` is the per-suite start and end in UTC (minimum `startedAt`, maximum non-empty `completedAt`). The qualification wrapper does not take a build slot, so `buildSlot` is `not-used` and `buildSlotWaitSeconds` is null. Output distillation is not a phase of this wrapper, so `distillationSeconds` is null. Fixture setup, fixture teardown, process spawn, and process wait come from the JSONL phase spans when a matching complete record exists, and stay null when they do not. Unknown stays null rather than zero. Native children, including e2e splits, receive `ANTIPHON_TEST_PHASE_ROOT` set to that timing root. JSONL shards are `cases-<pid>-<nonce>.jsonl` in `chunk-<sha256>/` under the timing root. The hash is SHA-256 of the length-prefixed chunk, run id, attempt, and suite. The owner sidecar in that directory is `own-<same hash>-<pid>-<nonce>.txt`. The post-exit read lists only that directory, so another chunk or an earlier attempt is not enumerated. A journal there with no matching sidecar is attributed from its first record, and a sidecar for a different identity does not hide it. The planned manifest and `timing/summary.json`, `timing/tests.csv`, and `timing/summary.md` are written at the timing root. The executed manifest is a journal under `groups-executed.journal/`. Its flat `groups-executed.json` is projected once at the terminal boundary (success, failed run, timeout, or orchestration error) and on an explicit snapshot or `scripts/summarize-test-phases.ps1 -RecoverExecutedManifest`. It is not rewritten after every chunk. Live publication progress is the accessor tuple `(generation, lastCommittedSequence, recordKind, publicationQuality)`, also printed by that summarizer. A stable flat file or a directory timestamp is not that signal. A gap, or a missing or mismatched `groups-executed.state.json`, makes the timing summary `complete=false` and does not change the coverage ledger, receipt, or qualification verdict. The publication plan is [docs/superpowers/plans/2026-10-30-card-1514-incremental-manifest-publication-plan.md](superpowers/plans/2026-10-30-card-1514-incremental-manifest-publication-plan.md). Historical log-mtime notes elsewhere in this document describe log finalization, not this progress contract.

TRX stays authoritative. Sidecars are private and are not release-gate inputs. The JSONL `testId` is the TUnit native id (`TestDetails.TestId`), also printed as `C1514_NATIVE <phase> <id>` and copied into TRX StdOut. The TRX `testId` attribute is a different GUID. Join a JSONL row to the TRX row whose StdOut contains that native id. Do not join on the TRX GUID, class, method, display name, or time order.

The plan lives on master at [docs/superpowers/plans/2026-10-23-card-1514-test-timing-instrumentation-plan.md](superpowers/plans/2026-10-23-card-1514-test-timing-instrumentation-plan.md). Windows CP-8 (real Windows, Windows PowerShell 5.1, and the paired overhead preflight) is owed and is not claimed from Linux. S3 (remaining process, filesystem, and shared-fixture choke points, V-14/V-15) is deferred.

Linux supervisor-death custody is a known gap. The setsid, watchdog, and stdin-pipe portable launcher was removed. `Invoke-NightlyOwnedProcess` again calls `Start-NightlyNativeOwner`, the Windows Job Object owner. On Linux that owner throws `Nightly native custody requires Windows.` A killed Linux supervisor does not take the chunk or its descendants with it. The 24-hour qualification that uses this recorder runs on Windows, where Job Object custody is the boundary.
