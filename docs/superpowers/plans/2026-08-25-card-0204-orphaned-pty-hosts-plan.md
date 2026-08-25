# CARD-0204 — 190 of 201 live pty-hosts had no `AgentSessions` row: root cause, fix, cleanup

**Date:** 2026-08-25 · **Card:** CARD-0204 (`6a04ee60-b678-45ef-bf7e-6ce471a7eef7`) · **Status:**
investigated, root cause reproduced, fix + cleanup applied in this pass · **Verified against:**
`master` @ `fe91d09`, worktree `card-task-7f0a4cb4`. Every count below was measured on this machine.

---

## Verdict up front

**Ongoing, not historical — and not a leak in the pty-host lifecycle at all.** The 190 hosts were
never owned by *this* database. They were launched by **test hosts inside `Antiphon.Tests`** on the
always-on production session-runner (port 17204), with their `AgentSessions` rows written to a
throwaway test schema that vanished when the test process exited. Nothing deleted the rows; they
were never here.

| | |
|---|---|
| **Mechanism** | `WebApplicationFactory<Program>` boots the real `Program`, hosted services included, on top of the real `server/appsettings.json` (`SessionRunner:BaseUrl = http://localhost:17204`, `Delegation:CheckInterpreterWorkingDirectory = C:\logs\antiphon\check-interpreter`). `AgentTaskCheckHostedService` calls `CheckInterpreterProvisioner.EnsureAsync` at startup, which creates the AlwaysOn `antiphon-check-interpreter` agent and **starts it immediately** (`CheckInterpreterProvisioner.cs`, "Start it NOW rather than waiting for a supervision tick") through the real `SessionRunnerHttpClient`. `AntiphonWebAppFactory` sets `Agents:DefaultDefinition = test-raw` / `Exe = cmd.exe` (`AntiphonWebAppFactory.cs:76-78`), so the launch is a detached `Antiphon.PtyHost` holding an interactive `cmd.exe` in the production check-interpreter directory. |
| **Why the 24 h linger never fired** | `HostSession.cs:309-314` starts the linger timer **only after the child exits**. An interactive `cmd.exe` with nothing typed into it never exits. These were live sessions nobody stopped, not lingering orphans — the same finding `SessionReconciliationSettings.cs:65-71` already records from CARD-0102. |
| **Why nothing reaped them** | The only thing that deletes `AgentSessions` rows is `DataRetentionService.PruneSessionsAsync` (90-day window, terminal rows only) — irrelevant here, the rows were never in this DB. The reconciler's pass 3 sees "runner session Running, no DB row" every 15 s and, by CARD-0056's rule that **unclaimed never implies kill**, only alerts (`PtyHostCensusDiverged`, which has been firing 30-50 times a day since 2026-08-21 — and every raise fails, see §4). |
| **Reproduced** | One `HealthEndpointTests` run (a single `GET /api/version`, 22 s) put a new `cmd.exe` manifest `49913a8c` in `C:\logs\antiphon\check-interpreter` on the production runner. Killed via the runner afterwards (host and manifest gone in ~6 s). |
| **Fix** | Test-side, three layers (§3). After the fix: 10 test classes / 52 tests, each booting a host, **zero** new manifests. |
| **Cleanup** | 190 killed through the runner's kill endpoint, each verified by host-pid exit, 0 failures (§5). Live pty-hosts 203 → 13; resident 4 466 MB → 317 MB. |

## 1. What was measured

### 1.1 The population (202 manifests, 203 processes, before cleanup)

`<SessionLogPath>/pty-hosts/manifests/*.json` — all 202 still existed (no manifest cleanup had
happened; the manifest is deleted by the host itself only when it exits). Every one had
`exitCode: null`, a live host pid and a live child pid.

| Shape (exe, cwd) | Count | DB row? |
|---|---|---|
| `cmd.exe`, `C:\logs\antiphon\check-interpreter` | **187** | none |
| `claude.exe`, `%TEMP%\antiphon-kind-test*` | **3** | none |
| `claude.exe`/`grok.exe`/`codex.cmd`, real working directories | 12 | all 12 `Running` (the card's 11 + this task's own session) |

The 203rd process had no manifest: a `SessionCpuWatchdogTests` host under a temp dir with
`--linger-hours 0.02`, created that afternoon by someone's in-proc `Antiphon.SessionRunner.Tests`
run. A separate, smaller class (§6); not touched.

### 1.2 They are not historical

Creation dates of the 187, UTC: 20 Aug (74), 21 Aug (49), 22 Aug (4), 23 Aug (6), 24 Aug (54).
CARD-0102 — which isolated the **E2E** suite onto its own runner precisely because of this shape —
landed 2026-08-21 19:10–20:33 UTC (`757923d`, `055b692`, `700f084`). About 110 of the 187 were
created after it. The E2E fixture is genuinely isolated now (`IsolatedSessionRunner`, per-run
manifest root, teardown census); the integration suite was never covered by that card.

Bursts line up with delegate build tasks running `Antiphon.Tests` as their verification (e.g. the
08-24 12:10–12:27 UTC burst of 6 against the CARD-0166 S7 / CARD-0168 tasks live at the time).
`AntiphonWebAppFactory` has 8 consumers that each build their own host (6 shared-per-session
factory types + 2 per-class), so a full run of the suite leaks ~8, and chunked runs by several
delegates leak proportionally more.

### 1.3 The three `claude.exe` ones are the same bug, older arm

`antiphon-kind-test` is a prefix used only by `AgentTaskAgentKindTests.TempWorkspace`
(`AgentTaskAgentKindTests.cs:689`), which creates **queued `AgentTask` rows** in the shared test
schema. Until `dfa589d` (2026-08-20 10:37 UTC, "serialize/isolate the shared-schema races") the
factory host ran against that same shared schema, so its real `AgentTaskDispatcherHostedService`
picked those tasks up and launched **real `claude.exe`** on the production runner. All three are
from 05:38–09:01 UTC that morning, before the isolation; their ansi logs show Claude parked on the
trust dialog in the temp directory ever since (16 KB–3.4 MB of redraws, nothing ever typed).

### 1.4 The positive-orphan test (what was required before killing anything)

All of the following, per host — `scripts/reap-orphaned-pty-hosts.ps1` encodes them as R1–R8:

1. No `AgentSessions` row for the manifest's session id in the production DB (the query must
   *answer*; an unreachable DB aborts).
2. Host pid alive, is `Antiphon.PtyHost.exe`, and its process start time matches the manifest's
   `hostStartTimeUtc` within 5 s (pid-reuse guard).
3. Child pid alive and of the process name the shape predicts (`cmd.exe`; `claude*`).
4. Manifest records no exit.
5. Launch shape is one of the two a test host produces (`cmd.exe` in the check-interpreter dir;
   cwd under `antiphon-kind-test*`).
6. For the `cmd.exe` shape, the ansi log is banner-only (all 187 were exactly 164 bytes: the
   "Microsoft Windows [Version …]" banner and a prompt — nothing was ever typed).
7. Older than 30 minutes.
8. The runner lists it as `Running`, so the kill goes **through the runner** (`POST
   /sessions/{id}/kill` → host kills child → `Exited` → runner acks `Shutdown` → host exits and
   deletes its manifest) — never a bare `Stop-Process`.

Dry run: 202 manifests → 12 protected (DB row), 190 positive orphans, **0 undecided**. Anything
with a row is never touched, whatever else is true of it.

## 2. What was ruled out

- **A retention/archival purge cascading to processes.** `DataRetentionService.PruneSessionsAsync`
  is the only `AgentSessions` delete; 90-day window, terminal statuses only; no row here was ever
  eligible and none of these ids was ever in the table.
- **Card close/archive paths.** None delete sessions (`ProjectCascade` deletes cards/boards, not
  sessions).
- **A runner-restart adoption failure.** The opposite: every restart *re-adopted* them correctly
  (host logs show matched `Runner disconnected` / `Runner connected` pairs across five restarts),
  which is exactly why they survived 12 days.
- **A shorter linger.** Would have collected exactly none of them (child never exits).
- **The production check interpreter.** `345ce4ba` (`claude.exe` in the same directory) has a
  row, is the real specialist, and is protected by R1.

## 3. The fix (applied, test-only)

| Layer | Where | What |
|---|---|---|
| Assembly belt | `tests/Antiphon.Tests/TestHelpers/ProductionRunnerGuard.cs` | `[Before(Assembly)]` sets `SessionRunner__BaseUrl=http://127.0.0.1:1` (a port nothing listens on; refuses in milliseconds) and `Delegation__CheckInterpreterEnabled=false`. Environment outranks `appsettings.json`, so **every** `Program` boot in the assembly — including `SmokeTests`, which builds a bare `WebApplicationFactory<Program>` — inherits both. |
| Factory braces | `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs` | The same values in-memory (so removing the guard cannot silently re-arm the leak), `SessionRunner:Enabled=false` (no event pump against a runner that does not exist), the interpreter's directory under the host's own scratch, and `ISessionRunnerClient` replaced by `RefusingSessionRunnerClient` — lists nothing, streams nothing, and a `StartAsync` is an `InvalidOperationException` naming this card, recorded on `factory.SessionRunner.LaunchAttempts`. |
| Pins | `ProductionRunnerGuardTests` (2), `ProductionRunnerIsolationTests` (2) | The guard ran and its port is dead; a factory boot makes zero launch attempts, its client is the fake, `SessionRunner:BaseUrl` is not 17204, the interpreter is off, its resolved directory is not the production one, and the `antiphon-check-interpreter` row does not exist in the host's schema. |

**Not changed, deliberately:** production code. The runner does nothing wrong launching what it is
asked to; the reconciler's "unclaimed never implies kill" (CARD-0056) is correct — the unclaimed
session it once found was the operator's own. A server-side reaper would need to distinguish "a
test asked for this" from "a human's session whose row we lost", and it cannot; the fix is that
tests stop asking.

### 3.1 Verification

Run with `--property:OutputPath=bin-card0204/` against the live production runner, diffing
`pty-hosts/manifests` before and after each class:

| Class | Tests | Result | New manifests |
|---|---|---|---|
| `ProductionRunnerGuardTests` | 2 | pass | 0 |
| `ProductionRunnerIsolationTests` | 2 | pass | 0 |
| `HealthEndpointTests` (the reproducer) | 1 | pass | 0 |
| `SmokeTests` | 10 | pass | 0 |
| `AgentTuiApiTests` | 10 | pass | 0 |
| `AgentTaskDispatcherWiringTests` | 1 | pass | 0 |
| `ApiKeyApiTests` | 6 | pass | 0 |
| `TrackerSyncEndpointTests` | 8 | pass | 0 |
| `AuditArchiveEndpointTests` | 1 | pass | 0 |
| `CardIdentifierResolutionTests` | 11 | pass | 0 |

Before the fix the same `HealthEndpointTests` run produced one. The remaining factory consumers
(`AgentModelLevelBindTests`, `AttentionServiceTests`' factory arm, the `Card*Api`/thread/comment
tests, `ChannelPreamblePresetEndpointTests`, `DiagnosticsBundleEndpointTests`,
`FileSystemBrowse*Tests`) do not reference the runner client; a full-assembly run is the build
delegate's normal verification and was not repeated here.

## 4. Side findings (recorded, not fixed here)

1. **The census alert has never persisted.** `SessionReconciliationService.RaiseCensusAlertIfDivergedAsync`
   logged `PtyHostCensusDiverged` 30–52 times a day since 2026-08-21 and every one was followed by
   `Alert raise failed … 22001: value too long for type character varying(4000)` — the alert's
   `Detail` (`AppDbContext.cs:162`, `HasMaxLength(4000)`) is exceeded. The one detector built for
   this shape (CARD-0102 / coverage plan P0-3) has been shouting into a full column. Truncate
   `Detail` in `AlertService` (or size it) — worth its own small card; the data in the log line was
   correct throughout (`unclaimed by the database: 190`).
2. **`Antiphon.SessionRunner.Tests` leaks hosts of its own** — in-proc runtimes under
   `%TEMP%\antiphon-*-tests-*` whose tests do not kill their sessions (the manifest-less
   `SessionCpuWatchdogTests` host, §1.1; CARD-0200 §1.7 found the same in
   `TranscriptAdoptionSafetyTests`). Their manifests live in temp dirs and are deleted with them,
   so the reaper cannot see them by manifest; they are identifiable by command line
   (`--manifest-dir C:\Users\…\Temp\…`). Smaller (one or two per run, `cmd /k` children), same
   never-exits shape. A `[After(Assembly)]` sweep in that assembly, mirroring the E2E fixture's
   `StopSessionsThisFixtureStartedAsync`, is the fix.

## 5. Cleanup performed

`pwsh -File scripts/reap-orphaned-pty-hosts.ps1 -Execute -Limit 95 -KillVerifySeconds 15`, then
the same without `-Limit` for the rest:

| | |
|---|---|
| Killed and verified (host pid gone) | **190** (187 `test-raw-check-interpreter` + 3 `kind-test-temp-dir`) |
| Failed / still alive | 0 |
| Protected (DB row), untouched | 12 |
| Not touched (rule failed) | 0 |
| Wall clock | 58 s + 54 s |
| After: manifests / live pty-hosts / runner `Running` | 12 / 13 / 12 — the 12 are exactly the sessions with rows; the 13th is the §1.1 temp-dir host |
| Resident working set, all pty-hosts | 4 466 MB → 317 MB |

The runner now lists 249 `Exited` sessions (in-memory history of the kills; cleared on its next
restart). No manifest, ansi log or other file was deleted by hand — the hosts removed their own
manifests on exit, as designed.

## 6. Follow-ups (decisions for the operator)

1. **Alert `Detail` overflow** (§4.1) — small card; until fixed, the census alert is visible only in
   the server log.
2. **`Antiphon.SessionRunner.Tests` in-proc leak** (§4.2) — small card; the reaper could grow a
   command-line arm (`--manifest-dir` under `%TEMP%`, host older than N hours, child `cmd.exe`) if
   it recurs at scale, but today it is one or two per run.
3. Whether to run the reaper on a schedule (the Windmill weekly cleanup, `reference_windmill_cleanup_schedule`)
   as a backstop. With the fix in, it should find nothing; a non-zero count would be a new leak
   worth a card rather than a silent reap — recommend **dry-run only** on the schedule, alerting on
   `positive orphans > 0`.
