# CARD-1653 S1 identity refusal keeps the host: test design

TestDesign task 053fe121, 2026-11-02, server2 (Linux, uid 1654), base master
`0e88f5adba7249c356f3b06e1626a9250525156b` plus the plan commits `eef852467`/`289eef0ba` (cherry-picked).
Scope: **S1 only** of the [plan](2026-11-02-card-1653-pty-host-pipe-hardening.md) (§3.1, §4 S1, D-1..D-5,
plan V-1..V-7, plan CP-1..CP-3). This companion adds verification detail and does not change the fix
design. No production or test source was changed. Every expected result below is a design, not executed
evidence, except the measurements under "Measured facts".

## Measured facts used by this design (server2, base `0e88f5adb`, native output, build-slot leases)

- `tests/Antiphon.SessionRunner.Tests` cold build: **82 s** inside the lease (slot wait 211 s, not counted).
- `Card1636AdoptionIdentityTests/*`, three runs, load average 22-31 on 24 cores: **6/6 green each time**,
  class wall 3.7-4.0 s. Per-test TRX durations (n=3 per case): the five fake-host bodies 0.12-1.12 s
  (p95 1.12 s), `Real_host_records_its_hello_instance_and_is_adopted` 1.28-1.42 s (p95 1.42 s).
- `CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders`: 1/1 green, 1.7 s.
- `Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/*` under the SessionRunner.Tests build: 3/3 green, 1.8 s
  (the CARD-1650-era legacy-digest red is not present at this base).
- Fake-host probe (scratch, no build): a `/bin/sh` script named `Antiphon.PtyHost` that starts a copy of
  `/usr/bin/sleep` named `fakePtyHost` in the background and echoes `$!` prints a pid whose `/proc/<pid>/comm`
  and .NET `ProcessName` are `fakePtyHost` (it contains `PtyHost`, so `KillHostIfStillOurs` accepts it). The
  process is reparented to `docker-init`, which reaps it after a kill (`/proc/<pid>` gone).
- `scripts/audit-land-verification-graph.ps1 -Check` at the base: `approved`, `count=69`,
  `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE`.

## Verification design

### Inspection

Bodies read at `0e88f5adb` before naming the cases:

- `SessionRunnerRuntime.AdoptOrphanedHostsAsync` (`:2020-2145`): the generic catch kills `manifest.HostPid`
  through `KillPidBestEffort` (no process-name check for `ProcessStopTarget.Host`, so the test's `sleep`/`ping`
  stand-in **is** killed at base), then `CreateAdoptedExited` (publishes `SessionExited`) and `TryDeleteFile`.
  An already-registered id is skipped (`_sessions.TryGetValue … continue`). → V-1, V-5, G-1..G-9.
- `RunnerSession.AdoptCoreAsync` (`:3542-3600`): the CARD-1636 check runs right after `ConnectAsync` and
  before `GetStatusAsync`; it logs the fixed token and throws `InvalidOperationException`. → V-1, G-7, G-25.
- `RunnerSession.StartAsync` (`:3413-3505`): non-custody goes from `ConnectAsync` straight to `LaunchAsync`
  with `request.Env`; the catch runs `TearDownFailedLaunch` → `KillHostIfStillOurs` (name contains `PtyHost`,
  Linux start-time match, `StopIntents.StopProcess`). → V-6, G-18..G-24.
- `CreateWorkspaceConflict` (`:3758`), `CompletePendingAsExited` (`:3838`, clears `_pendingReason`, publishes
  `SessionExited` once), `MarkVanishedIfDead` (`:4456`, returns unless `Running`), `SweepVanishedSessions(Async)`
  (`:2542-2585`), `KillAsync` (`:4357`, a row with no client, sidecar or herdr child returns at once),
  `ToDto` (`:4150`, `Pending`, `HostPid`). → V-1b, V-3, V-3b, G-10..G-17.
- `PtyHostLauncher.LaunchDetachedAsync`/`AwaitPidAsync` and `ShadowCopyStore.HasRuntimePayload`/`CopyRecursive`
  (Unix mode preserved): the fake host source needs `Antiphon.PtyHost` + `Antiphon.PtyHost.deps.json`; success
  is intermediary exit 0 plus a pid line, never stdout EOF. → V-6 fixture (plan's "TestDesign confirms" item:
  **confirmed**, no runtime-internal seam needed).
- `PtyHostClient.RequestAsync`/`ReadLoopAsync`: an `ErrorMessage` reply to `StatusRequest` makes
  `GetStatusAsync` throw `InvalidOperationException` at once (no 30 s wait). → V-5 fixture.
- `HostSession.GetHelloAck`: the real host answers its own session, its instance and its current status
  (`WaitingForLaunch` before Launch). → G-30 control.
- Tests/fixtures: `Card1636AdoptionIdentityTests` (StrangerWorld, RecordingLogger, AssertRefused),
  `PtyHostLeakSweep.cs` (`TestSessionLogRoot.KnownPrefixes`, `TestSessionTeardown`),
  `CompletedCardWorkspaceRemovalTests.C1032_FenceStartupAdoptionOrders` (the other `CreateWorkspaceConflict`
  consumer, asserts `Pending == "workspace_ownership_conflict"` and the manifest kept),
  `SessionReconciliationServiceTests` (`Pending_herdr_session_is_not_failed_by_pass_1`,
  `HerdrUnreachable_incident_fires_only_after_the_alert_minutes_threshold`, `BuildService`/`FakeRunnerClient`),
  `tests/test-execution-policy.json` (`platform: windows` Linux-only rows, `policyHash`),
  `tests/Shared/NativeLinuxPolicyWitnessTests.cs`. → V-8, R-1..R-3.
- Server `SessionReconciliationService` pass 1 (`:363`) and pass 1b (`:438-480`): every check is
  `!string.IsNullOrEmpty(Pending)`, reason-agnostic; the incident carries `FailureReason = Pending`. → V-8.

### Delivery inventory

S1 adds no message and no new transport. It changes what the runner reports for a refused adoption (a held
pending row instead of `SessionExited`) and adds one release that publishes `SessionExited`. Session input
is not touched (pending rows already refuse input), so no UserPrompt transcript evidence applies.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt (recipient) | Durable identity |
|---|---|---|---|---|---|---|
| D-A held refusal | adoption sweep identity-refusal branch | server pass 1 (hold) and pass 1b (incident) via the runner session list | the retained manifest file (the in-memory row is rebuilt from it) | runner restart re-runs adoption and re-registers the row (V-1 restart leg); server restart rebuilds `_pendingAlerts` and re-raises after the threshold (existing) | `AgentIncident` with `FailureReason == "pty_host_identity_unproven"` and `SessionId`, DB session not failed (V-8) | `SessionId` + manifest `HostPid`/`HostStartTimeUtc` |
| D-B release | `MarkVanishedIfDead` identity-pending branch | server via `SessionExited` event and the list (`Status=Exited`) | none new; the manifest is deleted after the transition | a lost event is recovered by the list (existing pass 1 handles `Exited` rows) | runner `SessionExited` published exactly once + DTO `Exited/ProcessVanished` (V-3) | `SessionId` |

Recipient evidence and declared substitutes:

- D-A busy recipient (threshold not yet reached): V-8 first scan, no incident and the session not failed.
  Already eligible: V-8 scan past the threshold, exactly one incident. Crash/enqueue recovery at the runner
  handoff: V-1 restart leg (the row comes back from the kept manifest). At the server handoff: in-memory
  alert state only; the existing `HerdrUnreachable_incident_*` tests own its re-raise.
- **Substitute 1:** V-8 feeds the server a `FakeRunnerClient` row, not the real runner HTTP/phone-home
  transport. It cannot prove that the transport carries the new reason string. It is the same `string?
  Pending` field the workspace and Herdr reasons already travel in, unchanged by S1.
- **Substitute 2:** D-B recipient evidence is the runner hub publish (V-3), not a server receipt. The server
  consumption of `SessionExited`/`Exited` is unchanged existing behaviour. It cannot prove ordering against
  a concurrent server stop.

### Fixtures and binding conventions

- **`PtyHostStrangerWorld`** (new `tests/Antiphon.SessionRunner.Tests/PtyHostStrangerWorld.cs`, no `[Test]`):
  Card1636's private `StrangerWorld` extracted unchanged in behaviour, plus: `rootPrefix`; optional manifest
  `ChildPid`/`ChildStartTimeUtc`; `StatusReply` mode (`Running` as today, or `Error` = answer
  `StatusRequestMessage` with `ErrorMessage("c1653", "fixture")`); `StandIn` (the `Process`); `ManifestPath`
  and the manifest bytes hash at creation; `ExitedEvents(id)` (a `Runtime.Subscribe` reader opened before
  the first sweep, drained with `TryRead`; events publish synchronously in the sweep); `PeerClosedAsync(TimeSpan)`
  (completes when the server sees EOF/IOException on the current connection; every "nothing past hello"
  assertion awaits it first, so a late `Shutdown` frame cannot race the read); `RestartAsync()` (disposes the
  runtime and builds a new one over the same settings, logger and fake server). Card1636 and Card1653 both use it.
- **`FakeHostSource`** (inside `Card1653FreshLaunchIdentityTests`): writes `<root>/fake-host-src/` with
  `Antiphon.PtyHost` (mode 0755: `#!/bin/sh`, `"$(dirname "$0")/fakePtyHost" 60 </dev/null >/dev/null 2>&1 &`,
  `echo $! > '<root>/fake-host.pid'`, `echo $!`, `exit 0`), `fakePtyHost` (a copy of `/usr/bin/sleep`, mode
  0755) and `Antiphon.PtyHost.deps.json` (`{}`). Settings: `SessionLogPath=<root>`,
  `PtyHostSourceDir=<root>/fake-host-src`, `PtyHostConnectTimeoutSec=5`. A test pipe server is bound at
  `PtyHostProtocol.PipeNameFor(id)` **before** `StartAsync`. It answers the hello with the case's ack, records
  every later frame as `(type name, JSON)` and closes the connection on the first one, so the base red is
  immediate (`LaunchAsync` fails on EOF) and never waits for a timeout.
- Root prefixes `c1653-adopt` and `c1653-launch` are added to `TestSessionLogRoot.KnownPrefixes`.
- Every new class: `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, per-test
  `[Timeout(15_000)]` (see Performance budget). `Card1653FreshLaunchIdentityTests` also carries
  `[NativeLinuxOnly("CARD-1653 fake pty-host is a /bin/sh script and a copied sleep; requires Linux")]`
  and a `tests/test-execution-policy.json` row (`suite: session-runner`, `platform: windows`, reason
  `Linux-only by construction: the fake pty-host source is a /bin/sh script plus a copied /usr/bin/sleep. The
  case remains required on Linux.`, owner `CARD-1653`), with `policyHash` resealed from the
  `NativeLinuxPolicyWitnessTests` output (memory recipe). The row lands in the red commit.
- Stranger ids are asserted absent in `D` and `N` formats, case-insensitive (CARD-1636 pattern).

### Proves it works now

Class `Card1653RefusalRetainsHostTests` (portable: Linux now, Windows with `ping.exe` stand-ins on the
windows-native lane later), class `Card1653FreshLaunchIdentityTests` (linux-native), changed class
`Card1636AdoptionIdentityTests` (portable), one new method in `SessionReconciliationServiceTests`.

- **V-1** `Refused_identity_retains_the_host_as_a_pending_row(string shape)`, cases `"other-session"` (hello =
  stranger session, manifest instance), `"other-instance"` (manifest session, stranger instance),
  `"missing-instance"` (manifest session, null instance). Setup: `PtyHostStrangerWorld(recordHostInstance:
  true, rootPrefix: "c1653-adopt")`. Assertions, in this order:
  1. `AdoptAsync()` returns 0 (the refusal itself);
  2. `PeerClosedAsync(5 s)`, then `Requests` empty (nothing past hello, no `Shutdown`);
  3. `StandIn.WaitForExit(500)` is false ("a refused identity never kills the manifest's host");
  4. `File.Exists(ManifestPath)` and its bytes hash is unchanged;
  5. `Runtime.Get(id).Status == "Starting"`;
  6. `.Pending == "pty_host_identity_unproven"`;
  7. `.HostPid == StandIn.Id` and `.ExitReason` null;
  8. `ExitedEvents(id)` count 0;
  9. the log contains `pty_host_identity_mismatch: adoption refused for session {id}`;
  10. the log does not contain the stranger session/instance (D/N), for the two shapes that carry one;
  11. `RestartAsync()`; the stand-in is still alive (`WaitForExit(500)` false) after the old runtime's
      dispose;
  12. the new runtime's `AdoptAsync()` returns 0 again, the row is again `Starting` +
      `pty_host_identity_unproven`, the stand-in is alive and the manifest is present.

  Layer: runtime + real named pipe + real process. Red at base: assertion 3 (the stand-in is killed by
  `KillPidBestEffort`). Expect 3 executions.
- **V-1b** `Stop_of_a_pending_identity_row_never_kills_its_host`: after the V-1 "other-session" refusal,
  `Runtime.KillAsync(id, 2 s)` returns; the stand-in is alive (`WaitForExit(500)` false); the row is still
  `Starting`/`pty_host_identity_unproven`. Then `SweepVanishedSessions(new SystemProcessLivenessProbe())`
  with the stand-in alive returns an empty list, and the row and manifest are unchanged. Red at base: the
  base row is `Exited` and the stand-in is already dead (the alive assertion).
- **V-3** `Pending_identity_row_is_released_once_its_host_is_dead`: manifest `ChildPid` is a second stand-in
  whose `StartTime` was read while alive and which was then killed and reaped. After the refusal:
  1. a sweep with the host stand-in alive returns `[]`; the row is still `Starting`, the manifest present
     (the release is keyed on the host pid and start, not the dead child);
  2. kill and reap the host stand-in; the sweep returns `[id]`;
  3. `Status == "Exited"`, `ExitReason == "ProcessVanished"`, `Pending` null;
  4. `ExitedEvents(id)` count 1;
  5. the manifest is absent;
  6. a second sweep returns `[]` and `ExitedEvents(id)` is still 1.

  Red at base: the base registers `Exited` at refusal (event count 1 already), so assertion 1 (`Starting`)
  fails. Red on S1 without D-5 (PC-11) at assertion 2.
- **V-3b** `Vanish_sweep_releases_only_identity_pending_rows`: two rows built directly with
  `RunnerSession.CreatePendingConflict(manifest, settings, events, logger, reason)`, each over a saved
  manifest whose `HostPid` is a killed-and-reaped process: reason `workspace_ownership_conflict` →
  `MarkVanishedIfDead(new SystemProcessLivenessProbe())` is false, the status stays `Starting`, `Pending` is
  unchanged and its manifest is present; reason `pty_host_identity_unproven` → true (the in-method positive
  control that the fixture can release). Red at base: does not compile (`CreatePendingConflict` is new); it
  is a guard witness (PC-17), not a behaviour red.
- **V-5** `Non_identity_adoption_failure_keeps_the_exit_path` (D-3 control): the hello names the manifest
  session and instance, and `StatusReply=Error`. `AdoptAsync()` returns 0, `Status == "Exited"`, `Pending`
  null, the manifest is absent, and the log does **not** contain `pty_host_identity_mismatch`. The stand-in's
  fate is deliberately not asserted: S1 keeps today's path (D-3), and S2 changes it. Green at base.
- **V-6** `Fresh_launch_refuses_an_unproven_hello_before_Launch(string shape)` (linux-native), cases
  `"other-session"` (stranger session, a fresh instance, `WaitingForLaunch`), `"missing-instance"` (own
  session, null instance, `WaitingForLaunch`) and `"running-status"` (own session, a fresh instance,
  `Running`). Each case isolates one predicate. Request: `/bin/sh -c "read line"`, cwd `<root>`, `Env =
  { C1653_CANARY = <guid> }`. Assertions, in this order:
  1. `runtime.StartAsync(...)` throws;
  2. `PeerClosedAsync(5 s)`, then the recorded frame list is empty: no `LaunchMessage`, and no frame JSON
     contains the canary;
  3. `ex.ToString()` contains `pty_host_identity_mismatch` and not the stranger ids (D/N);
  4. the log contains `pty_host_identity_mismatch: launch refused for session {id}` and not the stranger ids;
  5. the pid in `<root>/fake-host.pid` is dead within 5 s (polling `SystemProcessLivenessProbe`): the host we
     spawned is torn down (I-2, proven by spawn).

  `finally`: kill that pid best effort, dispose the runtime. Red at base: assertion 2 (`LaunchMessage`
  recorded, with the canary in its JSON). Expect 3 executions.
- **V-4 / V-7 mapping.** The plan's V-4 control (a matching fake hello is adopted Running) is the existing
  `Card1636AdoptionIdentityTests.Hello_naming_the_manifest_session_and_host_is_adopted`. It is not
  duplicated. The plan's V-7 is folded into V-6's `"missing-instance"` and `"running-status"` cases.
- **V-8** `SessionReconciliationServiceTests.Pty_identity_unproven_pending_row_is_held_and_raises_the_pending_incident`
  (Antiphon.Tests). It follows the shape of `HerdrUnreachable_incident_fires_only_after_the_alert_minutes_threshold`,
  with a `FakeRunnerClient` row `Status: "Starting", Adopted: true, Backend: SessionBackends.PtyHost,
  Pending: "pty_host_identity_unproven"` for a seeded Working agent's Running session.
  1. First scan: the DB session stays `Running` with `FailureReason` null, and there is no incident.
  2. A scan past the threshold: exactly one incident for the agent, with `SessionId == id` and
     `FailureReason == "pty_host_identity_unproven"`.

  Green at base (the server is reason-agnostic). It is the recipient witness for D-A and the guard witness
  for PC-26/PC-27.

### Guards the regression

- **R-1** CARD-1636 contract, `Card1636AdoptionIdentityTests` (CP-2), with changed refusal assertions.
  `AssertRefused(Guid stranger)` today asserts: `Requests` empty, `Status == "Exited"`, the log token with
  the session, and no stranger ids. It becomes:
  - **kept:** `Requests` empty (now after `PeerClosedAsync`); the log token with the session; no stranger
    ids. Every refusing test keeps `(await world.AdoptAsync()).ShouldBe(0)`.
  - **replaced:** `Status.ShouldBe("Exited")` → `Status.ShouldBe("Starting")` plus
    `Pending.ShouldBe("pty_host_identity_unproven")`.
  - **added:** `HostPid == StandIn.Id`, `StandIn.WaitForExit(500)` false, and the manifest file present.
  - The `StartAsync` comment "refused adoption then kills that pid" becomes "refused adoption must leave
    that pid alive; the world disposes it".

  This is not a weakening. The refusal itself is still asserted four ways: the adoption count is 0, nothing
  past hello is sent, the session is not `Running`, and the fixed log token appears. `Exited` was the
  observable consequence of the G1 defect (the base kills `manifest.HostPid`, publishes `SessionExited` and
  deletes the manifest). The plan (§3.1, D-2, R-1) replaces it deliberately with a held, visible row. The
  new assertions are strictly stronger: they pin the no-kill invariant (I-2) and the retained evidence
  (I-3), which the old assertion did not check at all. The three accepting tests
  (`Hello_naming_the_manifest_session_and_host_is_adopted`, the legacy half of
  `Manifest_without_a_host_instance_adopts_only_its_own_session`, and
  `Real_host_records_its_hello_instance_and_is_adopted`) are unchanged. The real-host test is also the G2
  control: a real host's fresh launch still passes the new hello check.
- **R-2** the workspace-conflict reason is byte-identical after `CreateWorkspaceConflict` →
  `CreatePendingConflict`: `CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` (CP-15).
- **R-3** the Windows suites stay sealed with the new Linux-only class: `NativeLinuxPolicyWitnessTests/*` (CP-14).

### Guard inventory

Every safety-critical guard is listed, split where it can be bypassed independently, and maps 1:1 to a PC.

| G | Plan ref + guard | PC |
|---|---|---|
| G-1 | §3.1/I-2 an identity refusal never calls `KillPidBestEffort(manifest.HostPid)` | PC-1 |
| G-2 | §3.1/I-3 an identity refusal keeps the manifest file | PC-2 |
| G-3 | §3.1/D-2 the refusal registers a `Starting` pending row, never `CreateAdoptedExited` | PC-3 |
| G-4 | §3.1 the refusal publishes no `SessionExited` (the server must not see a false exit) | PC-4 |
| G-5 | §3.1/D-2 the pending reason is exactly `pty_host_identity_unproven` (the release is keyed on it) | PC-5 |
| G-6 | D-3 only `PtyHostIdentityRefusedException` takes the hold branch; other failures keep the exit path | PC-6 |
| G-7 | I-1 the identity check runs before any frame past hello (before `GetStatusAsync`) | PC-7 |
| G-8 | I-1 the refused session is detached without `Shutdown` | PC-8 |
| G-9 | I-2 disposing the runtime (restart) never kills a held row's host | PC-9 |
| G-10 | I-2/§9 `KillAsync` on a held identity row never kills its host | PC-10 |
| G-11 | D-5 the vanish sweep releases a held identity row once its host is dead | PC-11 |
| G-12 | D-5/I-3 no release while the host pid (at its start time) is alive | PC-12 |
| G-13 | D-5 the release liveness check uses the manifest host pid, not the child pid | PC-13 |
| G-14 | D-5 the release publishes `SessionExited` exactly once | PC-14 |
| G-15 | D-5 the release deletes the retained manifest | PC-15 |
| G-16 | D-5 the release clears `Pending` (otherwise pass 1b keeps alerting on an Exited row) | PC-16 |
| G-17 | D-5 the release is scoped to the identity reason (workspace/Herdr rows untouched) | PC-17 |
| G-18 | D-4/G2 fresh launch: `Hello.SessionId == _sessionId` | PC-18 |
| G-19 | D-4/G2 fresh launch: `Hello.HostInstanceId` non-null | PC-19 |
| G-20 | D-4/G2 fresh launch: `Hello.Status == WaitingForLaunch` | PC-20 |
| G-21 | D-4 the check runs before `LaunchAsync` (env never reaches an unproven peer) | PC-21 |
| G-22 | I-2 a refused fresh launch tears down the host it spawned | PC-22 |
| G-23 | I-7 the fresh-launch refusal log names the session only | PC-23 |
| G-24 | I-7 the typed exception text (surfaced to the server as the launch failure) carries no claimed ids | PC-24 |
| G-25 | CARD-1636 the adoption identity comparison itself (`HelloNamesManifestHost`) | PC-25 |
| G-26 | GT-9 server pass 1 leaves any `Pending` row alone (no fail, no relaunch) | PC-26 |
| G-27 | GT-9 server pass 1b raises the incident for any `Pending` reason | PC-27 |
| G-28 | §4 the Linux-only class is pinned and excluded on Windows (policy seal) | PC-28 |
| G-29 | I-8 a legacy manifest without an instance still adopts its own session | PC-29 |
| G-30 | F-S1-3 a real host's fresh launch passes the G2 check (no false refusal) | PC-30 |
| G-31 | §3.1 the workspace callers keep `workspace_ownership_conflict` | PC-31 |
| G-32 | I-7 the adoption refusal log names the session only | PC-32 |

guards=32, mapped=32, missing=0, duplicate PC maps=0.

### Positive controls

Each PC breaks its guard with a compiling defect and expects the named method red at the named assertion.
Mutation runs break/red/restore/green after the land. Code runs the V/R rows. Review judges before the land.
Filters are method-scoped (`--treenode-filter "/*/<ns>/<Class>/<Method>"`); a method with `[Arguments]` runs
all its cases, and the named case is the one that must go red.

| PC | Break (in `SessionRunnerRuntime.cs` unless named) | Expect red |
|---|---|---|
| PC-1 | M-1: call `KillPidBestEffort(manifest.HostPid, …)` in the identity branch | `Card1653RefusalRetainsHostTests/Refused_identity_retains_the_host_as_a_pending_row` (all cases) at 3, stand-in alive |
| PC-2 | M-2: `TryDeleteFile(file)` in the identity branch | same, at 4, manifest present |
| PC-3 | register `CreateAdoptedExited(...)` instead of the pending row (no kill, no delete) | same, at 5, `Starting` |
| PC-4 | publish `SessionExited` for the id after registering the pending row | same, at 8, event count 0 |
| PC-5 | pass `"workspace_ownership_conflict"` as the identity branch's reason | same, at 6, `Pending` |
| PC-6 | change the branch's catch to `catch (Exception)` | `Non_identity_adoption_failure_keeps_the_exit_path` at `Status == "Exited"` |
| PC-7 | move the `HelloNamesManifestHost` check below `GetStatusAsync` | `Refused_identity_retains_the_host_as_a_pending_row` at 2, `Requests` empty |
| PC-8 | `await session.ShutdownHostAsync()` (or `client.ShutdownAsync`) before disposing the refused session | same, at 2, `Requests` empty |
| PC-9 | `RunnerSession.DisposeAsync`: kill `_hostPid` when `_client is null && _pendingReason is not null` | same, at 11, stand-in alive after restart |
| PC-10 | `RunnerSession.KillAsync`: for a no-client pending row, `Process.GetProcessById(_hostPid).Kill()` | `Stop_of_a_pending_identity_row_never_kills_its_host` at stand-in alive |
| PC-11 | M-5: delete the D-5 branch in `MarkVanishedIfDead` | `Pending_identity_row_is_released_once_its_host_is_dead` at 2, sweep returns `[id]` |
| PC-12 | the D-5 branch skips the liveness check | same, at 1, alive sweep returns `[]` |
| PC-13 | the D-5 branch probes `_childPid` with `_startedAt` instead of `_hostPid`/`HostStartTimeUtc` | same, at 1 |
| PC-14 | the D-5 branch publishes `SessionExited` again after `CompletePendingAsExited` (double publish) | same, at 4, event count 1 |
| PC-15 | the D-5 branch skips the manifest delete | same, at 5, manifest absent |
| PC-16 | the D-5 branch sets `Exited` but leaves `_pendingReason` | same, at 3, `Pending` null |
| PC-17 | the D-5 branch fires for any non-null `_pendingReason` | `Vanish_sweep_releases_only_identity_pending_rows` at the workspace row `MarkVanishedIfDead == false` |
| PC-18 | M-3 (narrow): drop the `SessionId` predicate | `Card1653FreshLaunchIdentityTests/Fresh_launch_refuses_an_unproven_hello_before_Launch("other-session")` at 2, no frame |
| PC-19 | drop the `HostInstanceId is not null` predicate | same method, `("missing-instance")` at 2 |
| PC-20 | drop the `WaitingForLaunch` predicate | same method, `("running-status")` at 2 |
| PC-21 | M-4: move the whole check after `LaunchAsync` | same method, all cases at 2 (`LaunchMessage` with the canary) |
| PC-22 | rethrow the typed refusal before `TearDownFailedLaunch` (`catch (PtyHostIdentityRefusedException) { throw; }` ahead of the general catch) | same method, all cases at 5, fake host dead |
| PC-23 | log `_client.Hello.SessionId` in the launch refusal line | same method, `("other-session")` at 4 |
| PC-24 | build the exception with `$"{code}: {_client.Hello.SessionId}"` | same method, `("other-session")` at 3 |
| PC-25 | `HelloNamesManifestHost` returns true | `Card1636AdoptionIdentityTests/Hello_naming_another_session_is_not_adopted` at `AdoptAsync() == 0` |
| PC-26 | `server/Application/Services/SessionReconciliationService.cs:363`: hold only `Pending == HerdrPendingReasons.Unreachable` | `SessionReconciliationServiceTests/Pty_identity_unproven_pending_row_is_held_and_raises_the_pending_incident` at the first scan, DB `Running` |
| PC-27 | `:442`: select only Herdr reasons for pass 1b | same method, at the incident present |
| PC-28 | remove `[NativeLinuxOnly]` from `Card1653FreshLaunchIdentityTests` (test file) | `Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/Policy_rows_require_pinned_attribute` |
| PC-29 | `HelloNamesManifestHost` requires a non-null manifest instance | `Card1636AdoptionIdentityTests/Manifest_without_a_host_instance_adopts_only_its_own_session` at the legacy `ShouldBe(1)` |
| PC-30 | the G2 check requires `Status == Running` (inverted) | `Card1636AdoptionIdentityTests/Real_host_records_its_hello_instance_and_is_adopted` at `dto.Status == "Running"` |
| PC-31 | `CreatePendingConflict` callers in the workspace branches pass `"pty_host_identity_unproven"` | `CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` at `Pending == "workspace_ownership_conflict"` |
| PC-32 | log `_client.Hello.SessionId` in the adoption refusal line | `Refused_identity_retains_the_host_as_a_pending_row("other-session")` at 10 |

Notes:

- PC-22 uses a compiling defect that keeps the typed refusal but skips teardown. Zero tests run, a build
  error or a fixture error is not red.
- PC-28: the policy witness checks rows against attributes. Mutation confirms the exact red method name from
  the witness output at the land SHA and records it.

### Out of scope

- S2 and S3 (plan V-8..V-26): peer credentials, socket directory, proof. In S1, I-2 is "never kill on an
  identity refusal"; the plan does not ask S1 to prove the peer.
- The windows-native run of the portable classes. S1's run is server2 Linux only. The portable classes are
  written to run on Windows (`ping.exe` stand-ins) and ride the plan's S2 windows-native Card1636 row (plan
  CP-8). Gap: S1's no-kill on Windows is code-identical but not executed before S2.
- A cross-uid squatter and the GT-2 host bind failure: one uid on server2; this is the plan's R-4.
- The pass-1b Herdr wording for non-Herdr reasons (plan F-2). V-8 asserts `FailureReason`, not the message.
- Herdr pending rows under D-5: covered by construction (PC-17 with a workspace row; Herdr rows have
  `_hostPid == 0`). `HerdrAdoptionSweepTests` is not a CP row: its Linux run is unmeasured here.
- Custody (`VerificationBinding`) adoption and launch: unchanged by S1.

### Implementation steps (red first, then green)

1. Red commit: extract `PtyHostStrangerWorld`; the R-1 assertion change in `Card1636AdoptionIdentityTests`;
   both new classes; the prefixes; the policy row and resealed hash; the V-8 method; and a minimal compile
   shim. The shim is the `PtyHostIdentityRefusedException` type plus `CreatePendingConflict` delegating to
   the existing workspace factory, with no behaviour change. Expected red: V-1 (3), V-1b, V-3, V-3b (its
   identity half) and V-6 (3); the Card1636 refusal cases (4). Expected green: V-5 and V-8 (controls whose
   ability to go red is shown by PC-6, PC-26 and PC-27), CP-14 and CP-15.
2. Green: the typed throw, the identity branch in the sweep catch, the G2 check, and the D-5 branch. Commit,
   then checkpoint run A, then run B.

## Performance budget

Sources: the measurements above (base, server2, loaded) and the plan's cf8b6982 receipts (SessionRunner.Tests
build p95 135 s, Antiphon.Tests build p95 579 s).

- **Per-test bodies (estimated from the measured fake bodies, p95 1.12 s).**
  - V-1: two sweeps, a restart and four 500 ms "still alive" waits: about 3.5 s.
  - V-1b: about 2 s. V-3: about 2.5 s (two stand-ins, three sweeps). V-3b and V-5: about 1 s.
  - V-6: shadow copy of about 45 KB, spawn and connect, plus a teardown poll: about 1.5 s per case.
  - Card1636: measured p95 1.42 s.
  - V-8: about 3 s after the shared `TestDbFixture`.
- **`[Timeout]`.** 15 000 ms per test on the new classes; about 2x a body p95 of about 3.5 s would be 7 s.
  The floor is the largest internal bound a body can legitimately wait, plus margin: the 5 s adoption
  connect, the 5 s `PtyHostConnectTimeoutSec` and the 5 s `PeerClosedAsync`. Card1636 keeps its 60 s fixture
  CTS and gains `[Timeout(15_000)]` too (measured p95 1.42 s). Code records the per-test TRX durations from
  the first green run, and resets each class to max(10 s, 2x measured p95, the largest internal bound + 5 s)
  if they differ.
- **Shared setup.** None across tests. One stand-in pair per test; `TestDbFixture` once per assembly for
  CP-16. No AppHost or container.
- **Teardown.** At most 2 s: the world disposes its stand-ins, and V-6 kills the fake pid best effort.

### Checkpoints

S1 runs as **two checkpoint runs**:

- **Run A** `--rows CP-1,CP-2,CP-3,CP-14,CP-15`: SessionRunner.Tests, one build.
- **Run B** `--rows CP-16`: Antiphon.Tests, one build.

These rows replace the plan's S1 rows. CP-3 reuses CP-1's build, because both lanes run on server2 in one
After group (the plan's §Checkpoints allowance; it saves one build). CP-14..CP-16 are new; their numbers
start after the plan's CP-13. `Min` is the executed TUnit floor (`-MinExecuted`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | tests/Antiphon.SessionRunner.Tests -> bin-c1653-s1/ | portable-identity-refusal | /*/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests/* | V-1, V-1b, V-3, V-3b, V-5 | class, 0 failed/skipped | 7 | 4 | true |
| CP-2 | S1 | CP-1 | portable-c1636-regression | /*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* | R-1, V-4 | class, 0 failed/skipped | 6 | 1 | true |
| CP-3 | S1 | CP-1 | linux-native-fresh-launch | /*/Antiphon.SessionRunner.Tests/Card1653FreshLaunchIdentityTests/* | V-6 (V-7 folded) | class, 0 failed/skipped | 3 | 1 | true |
| CP-14 | S1 | CP-1 | portable-native-policy-regression | /*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* | R-3 | existing class, 0 failed/skipped | 3 | 1 | true |
| CP-15 | S1 | CP-1 | portable-workspace-conflict-regression | /*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders | R-2 | method, 0 failed/skipped | 1 | 1 | true |
| CP-16 | S1 | tests/Antiphon.Tests -> bin-c1653-s1s/ | portable-pending-incident | /*/Antiphon.Tests.Application/SessionReconciliationServiceTests/Pty_identity_unproven_pending_row_is_held_and_raises_the_pending_incident | V-8 | method, 0 failed/skipped | 1 | 13 | true |

How Min is counted (executions):

- CP-1: V-1 (3 cases) + V-1b + V-3 + V-3b + V-5 = 7.
- CP-2: 4 methods + 2 `[Arguments]` cases = 6 (measured).
- CP-3: 3 cases. CP-14: 3 (measured). CP-15: 1 (measured). CP-16: 1.

Roster: no existing class starts with `Card1653`. `Card1653RefusalRetainsHostTests` and
`Card1653FreshLaunchIdentityTests` diverge after `Card1653`. `PtyHostStrangerWorld` has no `[Test]`. The plan's
S2/S3 rows (CP-4..CP-13) stay with their slices. Code runs `import --plan` on this table before the first run.

### Cost

Labels: measured where stated, otherwise estimated. Slot wait (45-211 s per lease today) is reported
separately.

- **Ordinary V/R floor (Code).**
  - Run A: CP-1 4 (build measured at 82 s, plan p95 135 s, plus about 30 s of class) + CP-2 1 + CP-3 1 +
    CP-14 1 + CP-15 1 = **8 min**.
  - Run B: CP-16 = **13 min** (build p95 9.7 min plus fixture and method).
  - Total **21 min** of row time.
  - Savings against the plan's S1 rows: the plan had CP-1..CP-3 at 5 + 2 + 5 = 12 min. This design spends
    -3 min on S1 rows (CP-3 reuses CP-1's build, 2.3 min p95) and +3 min on CP-14/CP-15. CP-16 adds 13 min
    of recipient evidence.
  - Dropping CP-16 would save 13 min but leave D-A without a recipient witness and PC-26/PC-27 without a
    method. The bundle rejects that.
- **Positive-control floor (Mutation)**, method-scoped, one incremental build per red and per restore:
  - 30 SessionRunner PCs (PC-1..PC-25, PC-28..PC-32): an incremental SessionRunner.Tests build of about
    60 s, twice, plus two method runs of about 8 s = **about 2.3 min each → 69 min**.
  - 2 server PCs (PC-26, PC-27): an incremental Antiphon.Tests build of 4-5 min, twice, plus runs = **about
    10 min each → 20 min**.
  - Setup: one cold build per assembly = 1.4 + 9.7 = **11 min**.
  - PC total: about **100 min serial**.
- **Batching and sharding.**
  - Batching saves about 2 min only. PC-28 (the test file) can share one red/restore build pair with one
    `SessionRunnerRuntime.cs` PC. Every other SessionRunner PC edits `SessionRunnerRuntime.cs`, and
    PC-26/PC-27 edit one server file and witness the same method, so they cannot batch.
  - With 32 rows above the 15-20 threshold, two worktree shards bring the wall time to about 55 min.
- **Total** = setup 11 + V/R 21 + PC red/restore/green 89 = **about 121 min estimated** (about 76 min wall
  with two Mutation shards).

### Completion and handoff

- guards=32, mapped=32, missing=0, duplicate PC maps=0. All 32 PCs run. Cost is numeric (estimated, with
  the measured inputs marked).
- No unverifiable seam: the fake host source satisfies `HasRuntimePayload` and the teardown name check
  (measured). Open questions below each have a default.

## Open questions for Code (each with a default)

- **Q-1 Release implementation.** Default: the D-5 branch checks liveness outside the gate, then calls
  `CompletePendingAsExited("ProcessVanished")`. That clears `Pending`, publishes once and is idempotent under
  the gate. It then deletes the retained manifest path stored on the row. This satisfies G-14..G-16 without
  new state machinery.
- **Q-2 Where the fresh-launch refusal surfaces.** `runtime.StartAsync` may wrap the typed exception. V-6
  asserts on `ex.ToString()` of whatever surfaces, so wrapping is allowed if the token survives and no
  claimed id is added.
- **Q-3 Fixture extraction.** Default: move `StrangerWorld` and `RecordingLogger` usage into
  `PtyHostStrangerWorld.cs`. Card1636 keeps its `RecordingLogger` nested type, which is referenced elsewhere
  by name, unless a grep shows no other user.
- **Q-4 `[Timeout]` values.** 15 s until the first green TRX, then per the formula above, recorded in the Code
  report.
