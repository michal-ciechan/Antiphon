# CARD-0102 — Isolate the E2E suite onto its own session-runner instance

**Date:** 2026-08-21 · **Decision being implemented:** CARD-0102's 2026-08-21 decision (user
direct): the E2E fixture starts a dedicated session-runner for the run and guarantees that
instance and every pty-host it spawns are killed at teardown. This plan designs HOW; WHETHER is
settled. Do not close the card from this plan — the build pass closes it once shipped.

## Ground truths, read from the current code (not the card's abstract)

1. **Half the card is already built.** `AntiphonAppFixture.DisposeAsync` already runs
   `StopSessionsThisFixtureStartedAsync` (CARD-0102 / coverage-plan P2-2 item 1): it reads this
   fixture's own `AgentSessions` rows (ownership proved via the private Postgres testcontainer),
   kills the matching runner sessions, snapshots host pids BEFORE the kills, runs a 20s census
   (`AssertNoLeakedPtyHostsAsync`, pid + process-name checked), and throws `SessionLeakVerdict`
   at the very END of disposal. What has NOT changed is the target: the fixture still pins
   `["SessionRunner:BaseUrl"] = "http://localhost:17204"`
   (`AntiphonAppFixture.cs:580`) — the production daemon. The remaining work is isolation,
   plus the cleanup guarantees the shared-runner design could never give (a hard-killed test
   process still leaks, and the DB-join ownership dance exists only because the runner is shared).

2. **The runner is a small, DB-less minimal-API app.** `src/Antiphon.SessionRunner/Program.cs` is
   `WebApplication.CreateBuilder` + Serilog + `SessionRunnerRuntime` + three hosted services. All
   of its on-disk state lives under `SessionRunner:SessionLogPath` (manifests at
   `<log>/pty-hosts/manifests/`, shadow-copy bins at `pty-hosts/bin/`, host logs at
   `pty-hosts/logs/` — `SessionRunnerSettings.ResolvedPtyHostDir`). Its own `appsettings.json`
   pins `SessionLogPath = C:\logs\antiphon\session-runner` and `PtyBackend = modern`. It binds
   whatever `ASPNETCORE_URLS` says, and it adopts orphaned hosts from the manifest dir BEFORE the
   HTTP API starts listening — so on a fresh, empty manifest dir, `/health` answering 200 means
   fully ready.

3. **Killing the runner process does NOT kill its pty-hosts — by design, twice over.** The launch
   chain is runner → intermediary (`Antiphon.PtyHost.exe --spawn`, exits immediately) → host, so
   the host's recorded parent is dead (`PtyHostLauncher`), and the host is spawned via
   `CreateProcessW` with `DETACHED_PROCESS | CREATE_BREAKAWAY_FROM_JOB`
   (`Win32ProcessSpawner`) precisely so no tree-kill or job object aimed at anything above it can
   reach it. A job object around the E2E runner cannot contain the hosts either (they break away;
   and if the job denied breakaway they fall back to plain detached). The ONLY sanctioned bulk
   takedown is `POST /sessions/kill-all` → `SessionRunnerRuntime.KillAllAsync`, which kills each
   session through its host's pipe client (child dies, host acks and exits). Teardown must
   therefore be **kill-all first, census, THEN stop the runner process** — never the reverse.

4. **A pty-host learns where to register from one place: the `--manifest-dir` argument** the
   launching runtime passes (`PtyHostLauncher.BuildHostArgs`, fed from
   `_settings.PtyHostManifestDir` in `SessionRunnerRuntime`). An isolated runner with its own
   `SessionLogPath` therefore CANNOT leak a manifest into the production dir — the wrong-daemon
   registration hazard the dispatch brief asked about does not exist as long as the isolated
   instance's `SessionLogPath` is actually overridden (see the env-var note in S1: the copied
   `appsettings.json` pins the PRODUCTION path, so the override is the single load-bearing line
   of this whole design).

5. **In-process hosting is mechanically possible — and rejected.**
   `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs` already hosts
   `SessionRunnerRuntime` in-process as an `ISessionRunnerClient` (the pty-hosts it spawns are
   detached real processes regardless). Swapping it into the E2E server's DI would work and need
   no port, no child process. Rejected because it deletes exactly the layers the E2E suite exists
   to exercise: `SessionRunnerHttpClient` (typed-client serialization, the `/capabilities`
   handshake that `PtyDeliveryProfile`'s ceilings hang off — CARD-0037 made server-side ceilings
   conditional on the RUNNER's answer, and `DirectSessionRunnerClient` answers null),
   `SessionRunnerEventPump`'s SSE connect/reconnect/backfill, and the runner's own endpoint
   surface. The decision's wording ("its own dedicated session-runner instance") also reads as a
   real instance. The in-process client stays what it is: the unit/integration harness.

6. **Provisioning the exe is a solved pattern.** `Antiphon.Tests.csproj` already
   `ProjectReference`s `Antiphon.SessionRunner.csproj` (that is how `DirectSessionRunnerClient`
   compiles), which proves a Web-SDK exe project referenced from a TUnit test project builds
   cleanly, and the SessionRunner's own output already stages `Antiphon.PtyHost.exe` (+ deps) and
   the hash-pinned `conpty\win-x64\` pair. A project reference from `Antiphon.E2E` puts the
   runner apphost, its `deps.json`/`runtimeconfig.json`, `appsettings.json`, the PtyHost binaries
   and the conpty redistributable into the E2E output directory — which also means the exe is
   ALWAYS next to the test assembly regardless of `--property:OutputPath=bin-*/` builds (the
   daemons-lock-bin gotcha), instead of fished out of `src/.../bin/Debug` where it may be stale
   or mid-rebuild.

7. **Nothing but the server talks to the runner.** The browser/client has no 17204 references;
   all runner traffic goes server-side through `ISessionRunnerClient` registered in
   `server/Program.cs:200` from `SessionRunnerSettings.BaseUrl`. Re-pointing ONE config value in
   the fixture's `KestrelWebApplicationFactory` migrates every test for free.

8. **Manifests carry enough for pid-reuse-safe cleanup**: `PtyHostManifest` has `HostPid`,
   `HostStartTimeUtc`, `ChildPid`, `ChildStartTimeUtc`, `Exe`, `Cwd`. A sweep can require
   pid + process name + start-time agreement before killing anything.

9. **Leak-attribution nuance worth carrying into the build pass**: the card's kill list included
   hosts with cwd `antiphon-interp-wire*` / `antiphon-kind-test*` — those prefixes belong to
   `Antiphon.Tests` temp dirs (`AgentTaskCheckInterpreterTests`, `AgentTaskAgentKindTests`),
   whose harnesses stub the runner client (`EmptyRunnerClient`) or never reference it. Either
   another launch path in `Antiphon.Tests` reaches 17204 via the defaulted
   `SessionRunnerSettings.BaseUrl`, or in-proc `DirectSessionRunnerClient` hosts (own manifest
   dirs) were swept up in the hand-kill. This plan fixes the E2E suite; the build pass should
   re-run the before/after manifest diff (S3) and, if 17204 still accretes sessions from
   `Antiphon.Tests`, raise a NEW card rather than widening this one.

## Design

**One dedicated runner process per `AntiphonAppFixture` instance**, started during
`InitializeAsync`, owned and killed by that fixture. Per-fixture (not per-test-session shared)
because ownership then holds by construction: `kill-all` against your own instance can never kill
anyone else's session — the exact property the shared daemon could never offer and the reason the
current cleanup needs its DB-join ownership proof. `SharedApp` already gives most test classes a
shared fixture, so the common case is one runner per test class, started concurrently with the
Postgres testcontainer (whose start dominates fixture init; the runner is an idle minimal-API app
with nothing to adopt). If the build pass measures the per-class process cost as material, the
fallback refinement is a `SharedApp`-style per-test-session runner — but that re-imports shared
ownership, so it is a measured retreat, not the default.

### S1 — provision and spawn an isolated runner per fixture

- `Antiphon.E2E.csproj`: add
  `<ProjectReference Include="..\..\src\Antiphon.SessionRunner\Antiphon.SessionRunner.csproj" />`.
  Build-pass verification (not assumption): E2E output contains `Antiphon.SessionRunner.exe`,
  `.deps.json`, `.runtimeconfig.json`, `appsettings.json`, `Antiphon.PtyHost.exe`, and
  `conpty\win-x64\{conpty.dll,OpenConsole.exe}` — under BOTH a plain build and an
  `--property:OutputPath=bin-x/` build.
- New `tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs` owning the child process:
  - **Run root**: `tests/Antiphon.E2E/TestOutput/runner/run-<guid>/` (repo located by the same
    upward walk `FindClientDistPath` uses; `TestOutput/` is already gitignored). A fixed,
    discoverable root — not a scattered `%TEMP%` GUID — because the S2 sweep needs a directory it
    may safely treat as "everything registered here is E2E-owned", and because leftover run dirs
    are the diagnosis for a crashed run.
  - **Spawn** `Antiphon.SessionRunner.exe` from `AppContext.BaseDirectory`, cwd = run dir,
    stdout/stderr redirected to files in the run dir (a crashed child's last words are the
    diagnosis), environment:
    - `ASPNETCORE_URLS=http://127.0.0.1:<port>` — port from `GetRandomAvailablePort()` (existing
      idiom); retry the spawn once on a bind failure rather than engineering around the TOCTOU.
    - `SessionRunner__SessionLogPath=<rundir>\logs` — **the load-bearing line.** The copied
      `appsettings.json` pins the PRODUCTION `C:\logs\antiphon\session-runner`; the env override
      must be unconditional, never dependent on the file's absence. Everything else — manifests,
      shadow bins, host logs, isolation itself — follows from this value (ground truth 4).
    - `SessionRunner__PtyHostLingerHours=0.02` — defense-in-depth for exited children only; the
      card's correction stands (live `cmd.exe` sessions never start the linger clock; the real
      guarantee is S2).
    - `Serilog__LogPath=<rundir>` — the runner's own log lands with the run's diagnostics.
    - `SessionRunner__PtyBackend` left alone: `appsettings.json`'s `modern` flows through and the
      conpty pair ships in the output; `PtyBackendPolicy` falls back safely on a machine without
      it, and the server's `PtyDeliveryProfile` learns whichever answer is true via the real
      `/capabilities` probe — now exercised end-to-end, which the shared daemon also gave but the
      in-process option would have silenced.
  - **Write `runner.json`** (pid + process start time) into the run dir after spawn — the S2
    sweep's liveness marker.
  - **Readiness**: poll `GET /health` for 200, bounded (~15s; adoption of an empty manifest dir
    is instant). On failure: kill the child, throw with the tail of its stdout/stderr.
- `AntiphonAppFixture.InitializeAsync`: start the runner task alongside `_container.StartAsync()`
  and await both; pass `runner.BaseUrl` into `KestrelWebApplicationFactory` (new ctor param)
  replacing the hardcoded 17204 — runner up before the server host builds, so
  `SessionRunnerEventPump`'s first connect lands.
- `RecordSessionRunnerReachabilityAsync` stays (it now indicts our own child instead of an
  absent daemon); verdict text updated to say whose runner it probed and to include the child's
  output tail on failure.

### S2 — teardown that cannot leak quietly, even when the test process dies

Graceful path (reshape `StopSessionsThisFixtureStartedAsync`):

1. Snapshot `GET /sessions` on the OWN instance — the whole list, not the DB join. "Everything on
   this runner is mine" is now true by construction, and the full list is strictly stronger: it
   also catches a session that somehow never got a DB row. Keep the DB read as a cross-check note
   in diagnostics, drop it as the ownership gate. (The CARD-0056 caution the current code quotes
   — never kill the unclaimed — was about the SHARED runner; on an owned instance the unclaimed
   session is precisely the leak.)
2. `POST /sessions/kill-all` (the sanctioned bulk path; kills child then host via the pipe).
3. Existing census `AssertNoLeakedPtyHostsAsync` over ALL host pids from step 1's snapshot —
   name-checked, 20s budget, `SessionLeakVerdict` thrown LAST in `DisposeAsync`, all unchanged.
4. Stop the runner child: `Process.Kill(entireProcessTree: true)` + bounded `WaitForExit`. Tree
   kill cannot reach detached hosts and does not need to — step 3 already proved them gone. (No
   graceful-shutdown dance: the runner holds no state worth flushing that the census depends on.)
5. Delete the run dir best-effort. If the census failed, LEAVE the run dir — its manifests are
   the next run's sweep input — and throw the verdict as today.

Crashed-run sweep (the piece that makes the guarantee survive `Ctrl-C`, TUnit timeouts, and a
killed `dotnet run`):

- Once per test process (static `Lazy`/gate), before the first fixture starts its runner:
  enumerate `TestOutput/runner/run-*/`.
- Skip any dir whose `runner.json` names a pid that is alive, is named
  `Antiphon.SessionRunner`, and matches the recorded start time — that is a CONCURRENT E2E
  invocation, not a corpse.
- For each dead run dir: load `logs/pty-hosts/manifests/*.json`; for each manifest whose
  `HostPid` is alive, is named `Antiphon.PtyHost`, and whose process start time matches
  `HostStartTimeUtc` (small tolerance), `Kill(entireProcessTree: true)` — the `cmd.exe` child is
  a direct child of the host, so the tree kill reaps it. Then delete the run dir.
- This is safe by construction — nothing but E2E fixtures ever registers a manifest under
  `TestOutput/runner/` — which is the property the fixed root exists to provide, and which no
  sweep against the production manifest dir could ever have (that sweep would be CARD-0056's
  disaster waiting).

### S3 — migrate the session-dependent tests, prove the isolation, update the record

- Re-pointing `SessionRunner:BaseUrl` migrates every test mechanically; this slice verifies none
  of them ASSUMED the always-on daemon:
  - `AgentE2ETests` (named in the dispatch): reading it finds zero session references — its
    session exposure is indirect (agent creation / card assignment can trigger launches). Verify
    green, nothing to rewrite expected.
  - `ChannelE2ETests`, `BoardE2ETests` (the `EnsureSessionRunnerReachable` callers): the guard
    keeps working — it now fails fast only when OUR child crashed, which is the failure it should
    report.
  - `DelegationSequencingE2ETests` / `DelegationPipelineE2ETests` (headed, `ClaudeHarness`-driven
    real Claude through the fixture's server): inherit isolation for free, and their real-Claude
    sessions now die at teardown too — a second leak class (headed-run claude.exe survivors)
    closed by the same change.
- Delete the "This fixture does not start a runner / talks to the always-on daemon" doc comments
  in the fixture; reword `StopSessionsThisFixtureStartedAsync`'s ownership rationale to the
  owned-instance argument.
- **Acceptance evidence for the card**: run the full E2E suite with a before/after listing of
  `C:\logs\antiphon\session-runner\pty-hosts\manifests\` and of live `Antiphon.PtyHost`
  processes. Zero new production manifests and a stable process census is the claim the card
  makes; measure and record suite wall-time delta (runner-per-fixture overhead) in the same
  note. If 17204 still accretes sessions from `Antiphon.Tests` (ground truth 9), new card.
- Docs: `CLAUDE.md`/`AGENTS.md` — amend the "17204 is the canonical session-runner port" bullet
  ("The E2E fixture does not start a runner; session-dependent tests talk to the always-on
  daemon" is inverted by this change) and the E2E-diagnostics bullet if wording references the
  shared daemon.

## Deliberately not in scope

- **Changing `PtyHostLingerHours`'s production default.** The card's own correction established
  it cannot address this leak (live children never start the clock); the isolated instance sets
  its own short value as cheap insurance, production keeps 24h.
- **The alerting gap** (`SessionReconciliationService`'s "N runner sessions with no DB row" log
  line that never became an incident — the proposed `PtyHostCensusDiverged` kind). Already an
  item in `docs/superpowers/plans/2026-08-20-delegate-reliability-test-coverage-plan.md`; not
  duplicated here.
- **Any production server or runner source change.** Kill-all, `/health`, `/capabilities`, env
  overrides — everything this design needs already exists. Expected diff surface: `Antiphon.E2E`
  project only (csproj, fixture, one new fixture-support class, doc comments) plus CLAUDE.md/
  AGENTS.md prose.
- **`Antiphon.Tests` leakage** (ground truth 9): verify attribution during the build pass; fix
  under a new card if real.
- **Aspire AppHost / dev daemon behavior** — untouched; the always-on daemon on 17204 remains
  the production runner for delegates and the operator.

## Card housekeeping

- CARD-0102 stays open through the build pass; add a comment linking this plan and noting the
  teardown-census half already shipped in `AntiphonAppFixture` (P2-2 item 1) so the build pass
  scopes S1–S3 only.
- On ship, the card's closing note should carry the S3 before/after manifest evidence, the suite
  wall-time delta, and the verdict on ground truth 9 (new card or not).

## Build-pass verification checklist (assumptions this plan makes that reading could not settle)

1. ProjectReference copies the runner APPHOST exe + runtimeconfig + transitive content
   (`conpty\win-x64`, `Antiphon.PtyHost.exe`) into E2E output, under plain and `bin-*/` builds.
2. `/health` readiness budget: measure real cold-start of the runner exe on this machine.
3. Per-fixture process cost across a full parallel suite run (fallback: shared per-test-session
   runner, with the ownership caveat stated above).
4. Tree-kill of a pty-host reaps its `cmd.exe` child (direct-child relationship holds through
   the ConPTY spawn path).
5. `SessionRunnerEventPump` behavior when the runner comes up moments before the server host —
   confirm no first-connect race needs a retry the pump doesn't already have.
